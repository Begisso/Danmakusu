using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.Generation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Always Boss: boss sections all through the song, from its first object to its last: the breaks and the spinners keep their own sections as they are, and everything between them is a section (a kiai's).
    /// It only marks the map (<see cref="SectionMarks"/>); the planner's <see cref="BulletPlanner.Sections"/> does the rest. Without a boss selection mod there are no bosses, as ever.
    /// </summary>
    public class DanmakusuModAlwaysBoss : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Always Boss";
        public override string Acronym => "AB";
        public override LocalisableString Description => "A boss all through the song: breaks and spinners keep their own sections, and everything between them is a boss section.";
        [Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.DifficultyReduction;

        public void ApplyToBeatmap(IBeatmap beatmap) => SectionMarks.MarkAlwaysBoss(beatmap);
    }

    /// <summary>
    /// Map Generation: the map's own circles, sliders and spinners are thrown away and a map made by <see cref="MapGenerator"/> takes their place, as many stars as the slider says (learnt from the downloaded
    /// maps), on the song's own timing, listening to the song. A map of mania, taiko or catch is replaced just the same, and what is made of it is played as an osu!standard map (the user, 2026-11-13: "treat
    /// them as osu standard maps"): its objects are plain circles, sliders and spinners, none of the rings or the falling of the converted modes.
    /// </summary>
    public class DanmakusuModMapGeneration : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Map Generation";
        public override string Acronym => "MG";
        public override LocalisableString Description => "Plays a map generated for the song instead of this one (an osu!standard map, whatever mode this one is), as hard as the stars you choose.";
        [Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.DifficultyReduction;

        [SettingSource("Stars", "How hard the generated map is, in Danmakusu stars.")]
        public BindableInt Stars { get; } = new BindableInt(6) { MinValue = 1, MaxValue = 10, Precision = 1 };

        public override string ExtendedIconInformation => $"{Stars.Value}*";

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            // (the song, when osu! is making the map out of a working beatmap that has it)
            SongBands? song = null;

            try
            {
                song = SongBands.From(PlayableBeatmapSource.Current);
            }
            catch
            {
                song = null;
            }

            // (only osu!'s copy of the map made for this play or this look in song select is changed: the map itself, its file and what is kept of it are never touched, so with the mod off it is the map
            // it was. What song select counts of a generated map is shown and not kept: see SectionMarks.ChangedBy)
            try
            {
                if (MapGenerator.Generate(beatmap, Stars.Value, song))
                    SectionMarks.MarkGenerated(beatmap, Stars.Value);
            }
            catch (Exception e)
            {
                Logger.Log($"Danmakusu: no map could be generated for {beatmap.BeatmapInfo}: {e.Message}", level: LogLevel.Important);
            }
        }
    }

    /// <summary>What the Danmakusu mods have marked on a map (the map itself, as long as it lives).</summary>
    public static class SectionMarks
    {
        private static readonly ConditionalWeakTable<IBeatmap, object> always_boss = new ConditionalWeakTable<IBeatmap, object>();
        private static readonly ConditionalWeakTable<IBeatmap, object> generated = new ConditionalWeakTable<IBeatmap, object>();

        public static void MarkAlwaysBoss(IBeatmap beatmap) => always_boss.AddOrUpdate(beatmap, true);

        /// <summary>Is the Always Boss mod on for this map?</summary>
        public static bool AlwaysBoss(IBeatmap beatmap) => always_boss.TryGetValue(beatmap, out _);

        /// <summary>(Map Generation) this map's objects are a generated map's, of so many stars.</summary>
        public static void MarkGenerated(IBeatmap beatmap, int stars) => generated.AddOrUpdate(beatmap, stars);

        /// <summary>Are this map's objects a generated map's (the Map Generation mod)?</summary>
        public static bool Generated(IBeatmap beatmap) => generated.TryGetValue(beatmap, out _);

        /// <summary>
        /// What the Danmakusu mods have changed of this map, as a few letters ("MG6", "AB", "MG6+AB"), or null when it is the map as it is. What is counted of a changed map is shown while it is changed and
        /// never kept under the map's own MD5 (<see cref="Stars.DanmakusuStars"/>): with the mods off, song select shows the map's own counts and stars again.
        /// </summary>
        public static string? ChangedBy(IBeatmap beatmap)
        {
            string? made = generated.TryGetValue(beatmap, out object? stars) ? $"MG{stars}" : null;
            string? always = AlwaysBoss(beatmap) ? "AB" : null;
            return made == null ? always : always == null ? made : made + "+" + always;
        }
    }

    /// <summary>
    /// The working beatmap osu! is making a playable map out of, on this thread, while it does (a mod only gets the map, not the song: the map generator wants the song). Done with Harmony round
    /// <see cref="WorkingBeatmap.GetPlayableBeatmap(IRulesetInfo, IReadOnlyList{Mod}, CancellationToken)"/>, like the other patches: if osu! has changed it, the generator makes its maps without the song,
    /// and the log says so.
    /// </summary>
    public static class PlayableBeatmapSource
    {
        [ThreadStatic]
        private static IWorkingBeatmap? current;

        private static readonly object gate = new object();
        private static bool tried;

        public static string Status { get; private set; } = "not tried";

        /// <summary>The working beatmap being made playable on this thread (null when none, or the patch did not take).</summary>
        public static IWorkingBeatmap? Current => current;

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
                Logger.Log(Status = "Danmakusu: the map generator hears the song");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: the map generator cannot hear the song ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var method = typeof(WorkingBeatmap).GetMethod(nameof(WorkingBeatmap.GetPlayableBeatmap), BindingFlags.Public | BindingFlags.Instance, new[] { typeof(IRulesetInfo), typeof(IReadOnlyList<Mod>), typeof(CancellationToken) })
                         ?? throw new MissingMethodException(nameof(WorkingBeatmap), nameof(WorkingBeatmap.GetPlayableBeatmap));

            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.PlayableBeatmapSource");
            harmony.Patch(method, prefix: new HarmonyLib.HarmonyMethod(typeof(PlayableBeatmapSource), nameof(prefix)), finalizer: new HarmonyLib.HarmonyMethod(typeof(PlayableBeatmapSource), nameof(finalizer)));
        }

        private static void prefix(WorkingBeatmap __instance, out IWorkingBeatmap? __state)
        {
            __state = current;
            current = __instance;
        }

        private static Exception? finalizer(Exception? __exception, IWorkingBeatmap? __state)
        {
            current = __state;
            return __exception;
        }
    }
}
