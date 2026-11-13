using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Logging;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Danmakusu.Stars
{
    /// <summary>
    /// Counts the bullets, beams and bosses of the map song select shows when they are not known yet, on a thread of its own, so that the panel shows the map at once (with "…" where a count is still coming)
    /// and not the last map's numbers while this one is planned (0.5-3.7 s). The map chosen last goes first; a few chosen before it wait, older ones are dropped (the background rating of the library,
    /// <see cref="StarFiller"/>, gets to them). It waits while anything is played. When a map is done, song select is told (<see cref="Finished"/>, read by <see cref="StarFiller"/> on the update thread)
    /// and shows it again (<see cref="SongSelectCounts.Refresh"/>). Only in osu! itself: the tools count where they ask (<see cref="DanmakusuBeatmap"/>).
    /// </summary>
    public static class CountsInBackground
    {
        /// <summary>(set by <see cref="StarFiller"/> when osu! itself runs) the counts are made here instead of where song select asks.</summary>
        public static volatile bool Enabled;

        // (how many maps chosen before the last one are still counted)
        private const int most_waiting = 6;

        private static readonly object gate = new object();
        private static readonly LinkedList<(string key, string md5, IBeatmap beatmap, bool keep)> waiting = new LinkedList<(string, string, IBeatmap, bool)>();
        private static readonly HashSet<string> waitingOrCounting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Thread? thread;

        // (what was counted here but is not kept: bosses chosen without the song, or a map that could not be counted; shown until osu! closes, so that it is not counted again every time it is shown)
        private static readonly ConcurrentDictionary<string, DanmakusuStars.Rating?> shownOnly = new ConcurrentDictionary<string, DanmakusuStars.Rating?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The maps whose counts have come in, for song select to show again.</summary>
        public static readonly ConcurrentQueue<string> Finished = new ConcurrentQueue<string>();

        /// <summary>What was counted here and not kept with the stars, if anything (null inside: the map could not be counted). A changed map's under <see cref="KeyOf"/>.</summary>
        public static bool TryGetShownOnly(string key, out DanmakusuStars.Rating? rating) => shownOnly.TryGetValue(key, out rating);

        /// <summary>The name a map is counted under here: its MD5, or for a map a Danmakusu mod has changed (<see cref="Mods.SectionMarks.ChangedBy"/>) its MD5 and the mods (never an MD5, so never kept).</summary>
        public static string KeyOf(string md5, string? changedBy) => changedBy == null ? md5 : md5 + "|" + changedBy;

        /// <summary>
        /// Counts this map soon (first, before those asked for earlier). Nothing happens if it is already waiting or being counted. A map a Danmakusu mod has changed (<paramref name="changedBy"/>) is counted
        /// as it is and only shown: nothing of it is kept with the map's stars.
        /// </summary>
        public static void Request(string md5, IBeatmap beatmap, string? changedBy = null)
        {
            string key = KeyOf(md5, changedBy);

            lock (gate)
            {
                if (!waitingOrCounting.Add(key))
                    return;

                waiting.AddFirst((key, md5, beatmap, changedBy == null));

                while (waiting.Count > most_waiting + 1)
                {
                    waitingOrCounting.Remove(waiting.Last!.Value.key);
                    waiting.RemoveLast();
                }

                if (thread == null)
                {
                    thread = new Thread(run) { IsBackground = true, Name = "Danmakusu counts", Priority = ThreadPriority.BelowNormal };
                    thread.Start();
                }

                Monitor.Pulse(gate);
            }
        }

        private static void run()
        {
            while (true)
            {
                (string key, string md5, IBeatmap beatmap, bool keep) next;

                lock (gate)
                {
                    while (waiting.Count == 0)
                        Monitor.Wait(gate);

                    next = waiting.First!.Value;
                    waiting.RemoveFirst();
                }

                try
                {
                    while (StarFiller.Playing)
                        Thread.Sleep(500);

                    if (!next.keep)
                    {
                        // (changed by a mod: shown while it is, kept nowhere)
                        shownOnly[next.key] = DanmakusuBeatmap.Count(next.beatmap, next.md5, keep: false);
                    }
                    else if (!DanmakusuStars.Complete(next.md5))
                    {
                        var counted = DanmakusuBeatmap.Count(next.beatmap, next.md5);

                        // (not all of it kept: song select would ask again and again)
                        if (!DanmakusuStars.Complete(next.md5))
                            shownOnly[next.md5] = counted;
                    }
                }
                catch (Exception e)
                {
                    shownOnly[next.key] = null;
                    Logger.Log($"Danmakusu: no counts for {next.beatmap.BeatmapInfo} ({e.Message})");
                }
                finally
                {
                    lock (gate)
                        waitingOrCounting.Remove(next.key);

                    Finished.Enqueue(next.md5); // (song select shows the map again, whichever way it was counted)
                }
            }
        }
    }
}
