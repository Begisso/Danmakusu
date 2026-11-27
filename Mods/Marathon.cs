using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Marathon (2026-11-27): several maps played back to back. The maps are checked in its settings (<see cref="MarathonPicker"/>), in the order they will be played; their MD5s are the value, so a score
    /// keeps them. Starting a map in song select starts the first of them (whichever map was selected), and when one has been played to its end the next one starts by itself at once
    /// (<see cref="MarathonRun"/>). The bosses of a boss selection mod are the marathon's: the most there can be is what all its maps have room for, and each map has its share of them, in order. Quitting a map, or failing it and leaving, ends the marathon; a replay never goes on to the next map.
    /// </summary>
    public class DanmakusuModMarathon : Mod, IApplicableToPlayer
    {
        public override string Name => "Marathon";
        public override string Acronym => "MR";
        public override LocalisableString Description => "Play several maps back to back: check them in the list, in the order you will play them. Starting any map starts the first; each next one starts as soon as the one before is over.";

        [Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;

        public override ModType Type => ModType.DifficultyReduction; // (so it is in Danmakusu's own column of the mod screen, as the others: see DanmakusuModColumn)

        public override bool RequiresConfiguration => true;

        [SettingSource("Maps", "Search as in song select, and click the maps to check them: the number on one is when it is played. Click a checked map again to take it out.", SettingControlType = typeof(MarathonPicker))]
        public Bindable<string> Maps { get; } = new Bindable<string>(string.Empty);

        /// <summary>The MD5s of the checked maps, in their order.</summary>
        public static string[] MapsOf(string? value) => string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static string ValueOf(IEnumerable<string> md5s) => string.Join(",", md5s);

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                int count = MapsOf(Maps.Value).Length;

                if (count > 0)
                    yield return ("Maps", count == 1 ? "1 map" : $"{count} maps");
            }
        }

        public void ApplyToPlayer(Player player)
        {
            // (a replay never goes on to the next map; the autoplay, which osu! plays as a replay too, does)
            if (player is ReplayPlayer && player.Mods.Value?.Any(mod => mod is ModAutoplay) != true)
                return;

            string[] maps = MapsOf(Maps.Value);
            MarathonRun.Playing(player, maps);
            player.OnShowingResults += () => MarathonRun.Finished(maps);
        }
    }

    /// <summary>
    /// How a marathon goes on from map to map (Harmony, as the other patches of song select: if osu! has changed what is patched, only Marathon is lost, and the log says so). Song select's start (solo
    /// play) begins the marathon with its first map. When a map that has another after it is over, a black curtain comes down over everything at once (2026-11-27, "switch between marathon maps -
    /// seamless"): osu!'s second before the results is cut, the results are left at once, song select puts the next map on and starts it on the next frame, its loader as quick as osu!'s quick
    /// restart, and the curtain goes up as the next map's player comes in - none of the screens between is seen. (A five-second line counted down over the results before, the same day.)
    /// </summary>
    public static class MarathonRun
    {
        private static readonly object gate = new object();
        private static bool tried;

        public static string Status { get; private set; } = "not tried";

        // (the marathon going on: its maps, which one is played now, and whether the next one is due)
        private static bool running;
        private static string[] maps = Array.Empty<string>();
        private static int position;
        private static bool due;

        private const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static MethodInfo onStart = null!;
        private static PropertyInfo beatmapsOfSongSelect = null!;
        private static MethodInfo addInternal = null!;
        private static FieldInfo? loaderOfSongSelect;
        private static FieldInfo? quickRestart;
        private static PropertyInfo? gameOfSongSelect;

        // (the players of a marathon, not replays: their results come at once when another map follows)
        private static readonly ConditionalWeakTable<Player, string[]> players = new ConditionalWeakTable<Player, string[]>();

        private static Curtain? curtain;

        /// <summary>Which map of the marathon going on <paramref name="md5"/> is (0 the first), or -1 when it is none of a marathon going on.</summary>
        public static int IndexOf(string? md5) => running && position < maps.Length && md5 != null && maps[position] == md5 ? position : -1;

        /// <summary>How many maps the marathon going on has.</summary>
        public static int Count => maps.Length;

        public static void Apply()
        {
            lock (gate)
            {
                if (tried)
                    return;

                tried = true;
            }

            try
            {
                OtherModesInSongSelect.EnsureHarmony();
                patch();
                Logger.Log(Status = "Danmakusu: Marathon goes on from map to map");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: Marathon cannot go on from map to map ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}", level: LogLevel.Important);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var solo = typeof(SoloSongSelect);
            var start = solo.GetMethod("OnStart", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException(solo.Name, "OnStart");
            var resuming = solo.GetMethod(nameof(SoloSongSelect.OnResuming), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException(solo.Name, "OnResuming");
            var resultsEntering = typeof(ResultsScreen).GetMethod(nameof(ResultsScreen.OnEntering), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                                  ?? throw new MissingMethodException(nameof(ResultsScreen), "OnEntering");
            var toResults = typeof(Player).GetMethod("progressToResults", any, new[] { typeof(bool) }) ?? throw new MissingMethodException(nameof(Player), "progressToResults");
            var playerEntering = typeof(Player).GetMethod(nameof(Player.OnEntering), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                                 ?? throw new MissingMethodException(nameof(Player), "OnEntering");

            onStart = start;
            beatmapsOfSongSelect = typeof(SongSelect).GetProperty("beatmaps", any) ?? throw new MissingMemberException(nameof(SongSelect), "beatmaps");
            addInternal = typeof(CompositeDrawable).GetMethod("AddInternal", any, new[] { typeof(Drawable) }) ?? throw new MissingMethodException(nameof(CompositeDrawable), "AddInternal");

            // (the loader of the next map is quick, as osu!'s quick restart, and the curtain hangs over the whole game: without these it is only slower, or seen)
            loaderOfSongSelect = solo.GetField("playerLoader", any);
            quickRestart = typeof(PlayerLoader).GetField("<QuickRestart>k__BackingField", any);
            gameOfSongSelect = solo.GetProperty("game", any);

            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.Marathon");
            harmony.Patch(start, prefix: new HarmonyLib.HarmonyMethod(typeof(MarathonRun), nameof(startPrefix)));
            harmony.Patch(resuming, postfix: new HarmonyLib.HarmonyMethod(typeof(MarathonRun), nameof(resumingPostfix)));
            harmony.Patch(resultsEntering, postfix: new HarmonyLib.HarmonyMethod(typeof(MarathonRun), nameof(resultsPostfix)));
            harmony.Patch(toResults, prefix: new HarmonyLib.HarmonyMethod(typeof(MarathonRun), nameof(toResultsPrefix)));
            harmony.Patch(playerEntering, postfix: new HarmonyLib.HarmonyMethod(typeof(MarathonRun), nameof(playerEnteringPostfix)));
        }

        /// <summary>(a player of a marathon has loaded) it is one, not a replay.</summary>
        public static void Playing(Player player, string[] played) => players.AddOrUpdate(player, played);

        /// <summary>(a map with Marathon on has been played to its end: its results are shown) the next one is due, if there is one.</summary>
        public static void Finished(string[] played)
        {
            if (!running || played.Length == 0 || !played.SequenceEqual(maps))
                return;

            due = position + 1 < maps.Length;

            if (!due)
                stop(); // (the last one: the marathon is over)
        }

        private static void stop()
        {
            running = false;
            due = false;
            curtain?.Open();
        }

        private static bool goesOn(Player player) => running && position + 1 < maps.Length && players.TryGetValue(player, out var played) && played.SequenceEqual(maps);

        /// <summary>A map of the marathon is over and another follows: no second before the results, and the curtain comes down.</summary>
        private static void toResultsPrefix(Player __instance, ref bool withDelay)
        {
            if (!goesOn(__instance))
                return;

            withDelay = false;
            curtain?.Close();
        }

        /// <summary>The next map's player comes in: the curtain goes up.</summary>
        private static void playerEnteringPostfix(Player __instance)
        {
            if (running && players.TryGetValue(__instance, out _))
                curtain?.Open();
        }

        private static DanmakusuModMarathon? marathonOf(SongSelect songSelect) => songSelect.Mods.Value?.OfType<DanmakusuModMarathon>().FirstOrDefault();

        /// <summary>Puts the map of <paramref name="index"/> (or the first one after it that is in the library) on song select; false when there is none.</summary>
        private static bool select(SongSelect songSelect, ref int index)
        {
            var manager = beatmapsOfSongSelect.GetValue(songSelect) as BeatmapManager;

            for (; index < maps.Length; index++)
            {
                string md5 = maps[index];
                var info = manager?.QueryBeatmap(b => b.MD5Hash == md5 && !b.Hidden);

                if (info == null || manager == null)
                {
                    Logger.Log($"Danmakusu: Marathon map {index + 1} is not in the library any more: it is left out", level: LogLevel.Important);
                    continue;
                }

                if (songSelect.Beatmap.Value?.BeatmapInfo.MD5Hash != md5)
                    songSelect.Beatmap.Value = manager.GetWorkingBeatmap(info);

                return true;
            }

            return false;
        }

        /// <summary>Song select starts a map: with Marathon on, a new marathon begins with its first map (whichever was selected); the next map of one going on starts as it is.</summary>
        private static void startPrefix(SoloSongSelect __instance)
        {
            try
            {
                var marathon = marathonOf(__instance);
                string[] list = marathon != null ? DanmakusuModMarathon.MapsOf(marathon.Maps.Value) : Array.Empty<string>();

                if (list.Length == 0)
                {
                    stop();
                    return;
                }

                // (the next one of the marathon going on: started by resumingPostfix)
                if (running && list.SequenceEqual(maps) && position < maps.Length && __instance.Beatmap.Value?.BeatmapInfo.MD5Hash == maps[position])
                    return;

                maps = list;
                position = 0;
                due = false;
                running = select(__instance, ref position);

                if (!running)
                    return;

                // (the curtain, over the whole game, once)
                if (curtain == null && gameOfSongSelect?.GetValue(__instance) is Container game)
                    game.Add(curtain = new Curtain());

                // (the bar of the whole marathon: its maps are read in the background now)
                if (beatmapsOfSongSelect.GetValue(__instance) is BeatmapManager manager)
                    MarathonBar.Prepare(manager, maps, __instance.Mods.Value ?? Array.Empty<Mod>());
            }
            catch (Exception e)
            {
                stop();
                Logger.Log($"Danmakusu: Marathon could not start ({e.Message})", level: LogLevel.Important);
            }
        }

        /// <summary>Song select is back (the results were left): the next map, if it is due, is put on and started on the next frame; anything else ends the marathon.</summary>
        private static void resumingPostfix(SoloSongSelect __instance)
        {
            if (!running || !due)
            {
                stop();
                return;
            }

            due = false;
            position++;
            var songSelect = __instance;

            addInternal.Invoke(songSelect, new object[]
            {
                new After(0, () =>
                {
                    if (!running || !songSelect.IsCurrentScreen())
                    {
                        stop();
                        return;
                    }

                    int index = position;

                    if (!select(songSelect, ref index))
                    {
                        stop();
                        return;
                    }

                    position = index;
                    onStart.Invoke(songSelect, null);

                    try
                    {
                        if (loaderOfSongSelect?.GetValue(songSelect) is PlayerLoader loader)
                            quickRestart?.SetValue(loader, true);
                    }
                    catch (Exception)
                    {
                        // (only slower)
                    }
                }),
            });
        }

        /// <summary>The results of a map of a marathon with another map after it are left at once: the next map comes.</summary>
        private static void resultsPostfix(ResultsScreen __instance)
        {
            if (!running || !due || __instance is not SoloResultsScreen)
                return;

            var results = __instance;
            addInternal.Invoke(results, new object[] { new After(0, () =>
            {
                if (results.IsCurrentScreen())
                    results.Exit();
            }) });
        }

        /// <summary>Runs something once, a while after it is added (on the update thread).</summary>
        private partial class After : Component
        {
            private readonly double delay;
            private readonly Action action;
            private double since;
            private bool done;

            public After(double delay, Action action)
            {
                this.delay = delay;
                this.action = action;
            }

            protected override void Update()
            {
                base.Update();

                if (done)
                    return;

                since += Time.Elapsed;

                if (since < delay)
                    return;

                done = true;
                action();
                Expire();
            }
        }

        /// <summary>Black over the whole game while one map of a marathon gives way to the next (it goes up by itself if the next has not come in after a while).</summary>
        private partial class Curtain : CompositeDrawable
        {
            private const double longest_ms = 15000;
            private double closedFor = -1;

            public Curtain()
            {
                RelativeSizeAxes = Axes.Both;
                Depth = float.MinValue;
                Alpha = 0f;
                AlwaysPresent = true;
                InternalChild = new osu.Framework.Graphics.Shapes.Box { RelativeSizeAxes = Axes.Both, Colour = osuTK.Graphics.Color4.Black };
            }

            public void Close()
            {
                Schedule(() =>
                {
                    closedFor = 0;
                    this.FadeIn(120);
                });
            }

            public void Open()
            {
                Schedule(() =>
                {
                    closedFor = -1;
                    this.FadeOut(300);
                });
            }

            protected override void Update()
            {
                base.Update();

                if (closedFor < 0)
                    return;

                closedFor += Time.Elapsed;

                if (closedFor > longest_ms)
                {
                    closedFor = -1;
                    this.FadeOut(300);
                }
            }
        }
    }
}
