using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Danmakusu.Stars
{
    /// <summary>
    /// Rates every map of the library in the background (its Danmakusu stars, its counts of bullets and beams, how many bosses it has room for), so that song select can sort by them and show them
    /// (<see cref="SongSelectStars"/>, <see cref="DanmakusuBeatmap"/>): the maps of which not all of that is known for this version yet
    /// (<see cref="DanmakusuStars"/>), osu! maps first, on a quarter of the cores (at least one), at the lowest priority, waiting while anything is played. When it has gone through them it looks for new
    /// maps every two minutes. While song select shows Danmakusu and goes by stars it sorts again every two minutes as new ratings come in, and once more when a round is over. It also has the map song
    /// select shows counted in the background (<see cref="CountsInBackground"/>) and has song select show the counts when they come in (<see cref="SongSelectCounts.Refresh"/>).
    /// It rides in the ruleset's icon, as <see cref="Mods.DefaultBossMod"/> does (osu! loads the icons when it starts); several icons share one rating.
    /// </summary>
    public partial class StarFiller : Component
    {
        private static int started;
        private static IBindable<LocalUserPlayingState>? playState;
        private static long resortedAt;
        private static long resortedAdded;
        private static volatile bool roundOver;

        private const long resort_every_ms = 120_000;
        private const int look_again_ms = 120_000;

        private BeatmapStore? beatmapStore;
        private BeatmapManager? beatmapManager;
        private IRulesetInfo? danmakusu;
        private Storage? storage;
        private IBindable<LocalUserPlayingState>? playing;

        public StarFiller()
        {
            AlwaysPresent = true; // (it must be updated to sort again)
        }

        /// <summary>Is a map being played (or in a break of one)? The background work waits then.</summary>
        public static bool Playing => playState?.Value is LocalUserPlayingState.Playing or LocalUserPlayingState.Break;

        [BackgroundDependencyLoader(true)]
        private void load(BeatmapStore? beatmapStore, BeatmapManager? beatmapManager, IRulesetStore? rulesets, Storage? storage, ILocalUserPlayInfo? playInfo)
        {
            this.beatmapStore = beatmapStore;
            this.beatmapManager = beatmapManager;
            this.storage = storage;
            danmakusu = rulesets?.GetRuleset(DanmakusuRuleset.SHORT_NAME);
            playing = playInfo?.PlayingState.GetBoundCopy();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // (only in osu! itself, with everything there, and once)
            if (beatmapStore == null || beatmapManager == null || danmakusu == null || storage == null || Interlocked.Exchange(ref started, 1) == 1)
                return;

            var store = beatmapStore;
            var manager = beatmapManager;
            var ruleset = danmakusu;
            string file = storage.GetFullPath("danmakusu_stars.csv");
            var play = playing;
            playState = play;

            // (song select shows a map's counts at once and counts the rest in the background: CountsInBackground)
            CountsInBackground.Enabled = true;

            new Thread(() => run(store, manager, ruleset, file, play)) { IsBackground = true, Name = "Danmakusu stars", Priority = ThreadPriority.Lowest }.Start();
        }

        protected override void Update()
        {
            base.Update();

            // (counts of a map song select showed have come in)
            if (!CountsInBackground.Finished.IsEmpty)
                SongSelectCounts.Refresh();

            long now = Environment.TickCount64;
            long added = DanmakusuStars.Added;

            if (added == resortedAdded || (now - resortedAt < resort_every_ms && !roundOver))
                return;

            resortedAt = now;
            resortedAdded = added;
            roundOver = false;

            if (playing?.Value is LocalUserPlayingState.Playing or LocalUserPlayingState.Break)
                return;

            SongSelectStars.Resort();
        }

        private static void run(BeatmapStore store, BeatmapManager manager, IRulesetInfo ruleset, string file, IBindable<LocalUserPlayingState>? play)
        {
            try
            {
                DanmakusuStars.Enable(file);
            }
            catch (Exception e)
            {
                Logger.Log($"Danmakusu: no star ratings are kept ({e.Message})", level: LogLevel.Important);
                return;
            }

            int workers = Math.Max(1, Environment.ProcessorCount / 4);

            while (true)
            {
                try
                {
                    var todo = unrated(store);

                    if (todo.Count > 0)
                    {
                        Logger.Log($"Danmakusu: rating {todo.Count} maps for song select ({DanmakusuStars.Count} rated already), on {workers} of {Environment.ProcessorCount} cores");
                        var clock = Stopwatch.StartNew();
                        var queue = new ConcurrentQueue<BeatmapInfo>(todo);
                        int failed = 0;
                        var threads = new List<Thread>();

                        for (int i = 0; i < workers; i++)
                        {
                            var thread = new Thread(() =>
                            {
                                while (queue.TryDequeue(out var beatmap))
                                {
                                    waitWhilePlaying(play);

                                    if (!rate(manager, ruleset, beatmap))
                                        Interlocked.Increment(ref failed);
                                }
                            }) { IsBackground = true, Name = $"Danmakusu stars {i + 1}", Priority = ThreadPriority.Lowest };

                            threads.Add(thread);
                            thread.Start();
                        }

                        foreach (var thread in threads)
                            thread.Join();

                        Logger.Log($"Danmakusu: {todo.Count} maps rated in {clock.Elapsed.TotalMinutes:0.0} minutes{(failed > 0 ? $" ({failed} could not be rated: they keep osu!'s stars)" : string.Empty)}");
                        roundOver = true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Log($"Danmakusu: the star rating of the library stopped for a while ({e.GetType().Name}: {e.Message})", level: LogLevel.Important);
                }

                Thread.Sleep(look_again_ms);
            }
        }

        /// <summary>The maps Danmakusu can play (osu!, taiko, catch and mania) that have no rating of this version yet, osu! maps first.</summary>
        private static List<BeatmapInfo> unrated(BeatmapStore store)
        {
            BeatmapSetInfo[] sets = Array.Empty<BeatmapSetInfo>();

            // (the list is osu!'s detached copy, which the game changes as maps come and go: read it whole, again if it changed under the reading)
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    sets = store.GetBeatmapSets(null).ToArray();
                    break;
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
                {
                    Thread.Sleep(1000);
                }
            }

            var todo = new List<BeatmapInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var set in sets)
            {
                if (set.DeletePending)
                    continue;

                foreach (var beatmap in set.Beatmaps)
                {
                    if (beatmap.Hidden || beatmap.Ruleset.OnlineID is < 0 or > 3 || string.IsNullOrEmpty(beatmap.MD5Hash) || DanmakusuStars.Complete(beatmap.MD5Hash) || !seen.Add(beatmap.MD5Hash))
                        continue;

                    todo.Add(beatmap);
                }
            }

            return todo.OrderBy(beatmap => beatmap.Ruleset.OnlineID == 0 ? 0 : 1).ToList();
        }

        private static DanmakusuStars.Rating rated(StarRating.Result result) => new DanmakusuStars.Rating(result.Stars, result.Bullets, result.Beams, 0);

        private static void waitWhilePlaying(IBindable<LocalUserPlayingState>? play)
        {
            while (play?.Value is LocalUserPlayingState.Playing or LocalUserPlayingState.Break)
                Thread.Sleep(1000);
        }

        /// <summary>
        /// One map: converted for Danmakusu as for play, without mods, and rated as the calculator rates it, with its counts of bullets and beams, and how many bosses it has room for (the song is read
        /// only when the map has too few sections of its own, as in the game).
        /// </summary>
        private static bool rate(BeatmapManager manager, IRulesetInfo ruleset, BeatmapInfo beatmap)
        {
            if (DanmakusuStars.Complete(beatmap.MD5Hash))
                return true;

            try
            {
                var working = manager.GetWorkingBeatmap(beatmap);
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var playable = working.GetPlayableBeatmap(ruleset, Array.Empty<Mod>(), cancel.Token);

                if (playable.HitObjects.Count == 0)
                {
                    DanmakusuStars.Put(beatmap.MD5Hash, new DanmakusuStars.Rating(0, 0, 0, 1));
                    return true;
                }

                // (what osu!'s own rating found already is not planned again: only the bosses are missing then)
                var rating = DanmakusuStars.TryGetRating(beatmap.MD5Hash, out var known) && known.Bullets >= 0 && known.Beams >= 0
                    ? known
                    : rated(StarRating.Rate(playable));

                int bosses = BulletPlanner.MostBosses(playable, () => SongBands.From(working));
                DanmakusuStars.Put(beatmap.MD5Hash, rating with { Bosses = bosses });
                return true;
            }
            catch (Exception e)
            {
                DanmakusuStars.Put(beatmap.MD5Hash, DanmakusuStars.UNRATED);
                Logger.Log($"Danmakusu: {beatmap} could not be rated ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }
    }
}
