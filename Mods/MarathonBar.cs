using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The one boss bar of a whole marathon (2026-11-27, "one boss bar shared among all maps"): every map of it has the same bar, its maps one after the other, as long as they are - the ones played
    /// already empty, the one played going down, the ones to come full - each with its share of the bosses. As the marathon starts, its other maps are read in the background (their sections, as the bar
    /// needs them, with their bosses: <see cref="BulletPlanner.BarPeriods"/>), and the field of each map puts its own sections among them (<see cref="Combine"/>).
    /// </summary>
    public static class MarathonBar
    {
        private sealed record Layout(BossPeriod[] Periods, BossInfo[] Bosses, double Start, double End);

        private static Task<Layout?[]>? preparing;

        /// <summary>Reads every map of the marathon for the bar, in the background, with the boss selection mod's bosses and their shares (no boss selection: no bar of bosses at all).</summary>
        public static void Prepare(BeatmapManager manager, string[] maps, BeatmapInfo?[] infos, IReadOnlyList<Mod> mods)
        {
            var selection = mods.OfType<DanmakusuModBossSelection>().FirstOrDefault();

            if (selection == null)
            {
                preparing = null;
                return;
            }

            string value = selection.Bosses.Value;
            bool doubled = selection.Double, triple = selection.Triple;
            int least = triple ? 3 : doubled ? 2 : 1;
            int count = Math.Max(BossPicker.CountOf(value), least);
            int[] shares = BossPicker.SharesOf(value);

            if (shares.Length != maps.Length)
                shares = BossPicker.SplitShares(count, least, Enumerable.Repeat(1, maps.Length).ToArray());

            var all = BossPicker.MarathonBosses(BossPicker.Bosses(value), count, maps);
            var sectionMods = mods.Where(mod => mod is DanmakusuModAlwaysBoss).ToArray();
            string[] list = maps.ToArray();

            preparing = Task.Run(() =>
            {
                var layouts = new Layout?[list.Length];

                // (one map after the other: osu!'s store of working maps is not asked from many threads at once)
                for (int j = 0; j < list.Length; j++)
                {
                    int share = Math.Max(shares[j], least);
                    int offset = shares.Take(j).Sum();
                    layouts[j] = layoutOf(manager, infos[j], share, all.Skip(offset).Take(share).ToList(), doubled, triple, sectionMods);
                }

                Logger.Log($"Danmakusu: Marathon bar: {layouts.Count(l => l != null)} of {list.Length} maps read");

                return layouts;
            });
        }

        private static Layout? layoutOf(BeatmapManager manager, BeatmapInfo? info, int share, IReadOnlyList<BossInfo> picked, bool doubled, bool triple, Mod[] sectionMods)
        {
            try
            {
                if (info == null)
                    return null;

                var working = manager.GetWorkingBeatmap(info);
                var converted = working.GetPlayableBeatmap(new DanmakusuRuleset().RulesetInfo, sectionMods);

                if (converted.HitObjects.Count == 0)
                {
                    Logger.Log($"Danmakusu: Marathon bar: {info} has nothing to play");
                    return null;
                }

                SongBands? song = null;
                var periods = BulletPlanner.BarPeriods(converted, share, doubled, () => song ??= SongBands.From(working), triple);
                var colours = MapColours.Read(working);

                if (colours.Count == 0 && BackgroundColour.Of(working) is Color4 background)
                    colours = [background];

                var bosses = DanmakusuModBossSelection.ChooseFrom(picked, BossMatcher.Match(colours), share);
                return new Layout(periods, bosses, converted.HitObjects.Min(h => h.StartTime), converted.HitObjects.Max(h => h.GetEndTime()));
            }
            catch (Exception e)
            {
                Logger.Log($"Danmakusu: Marathon could not read a map for its bar ({e.Message})");
                return null;
            }
        }

        /// <summary>
        /// The bar of the marathon for the map <paramref name="md5"/>, which has these sections and bosses and lasts from <paramref name="start"/> to <paramref name="end"/>: the maps before it laid
        /// out before it, the ones after it after it, every map's boss slots after the ones before. Null when it is no map of a marathon going on, or the other maps could not be read.
        /// </summary>
        public static (BossPeriod[] Periods, BossInfo[] Bosses, double Start, double End)? Combine(string? md5, BossPeriod[] own, BossInfo[] ownBosses, double start, double end)
        {
            int index = MarathonRun.IndexOf(md5);
            var task = preparing;

            if (index < 0)
                return null;

            if (task == null || own.Length == 0)
            {
                Logger.Log($"Danmakusu: Marathon bar: map {index + 1} keeps its own bar ({(task == null ? "no boss selection" : "no bosses on it")})");
                return null;
            }

            Layout?[] layouts;

            try
            {
                // (read while the first map loaded; the later ones have it at once)
                if (!task.Wait(TimeSpan.FromSeconds(30)))
                {
                    Logger.Log($"Danmakusu: Marathon bar: the other maps were not read in time: map {index + 1} keeps its own bar");
                    return null;
                }

                layouts = task.Result;
            }
            catch (Exception)
            {
                return null;
            }

            if (layouts.Length != MarathonRun.Count || index >= layouts.Length)
                return null;

            if (double.IsNaN(start))
                start = own.Min(p => p.ShootStart);

            if (double.IsNaN(end))
                end = own.Max(p => p.End);

            // (where each map lies on the one line: this one where it is, the others before and after it, end to end)
            var shift = new double[layouts.Length];
            double before = start;

            for (int j = index - 1; j >= 0; j--)
            {
                if (layouts[j] is not Layout layout)
                    continue;

                before -= layout.End - layout.Start;
                shift[j] = before - layout.Start;
            }

            double after = end;

            for (int j = index + 1; j < layouts.Length; j++)
            {
                if (layouts[j] is not Layout layout)
                    continue;

                shift[j] = after - layout.Start;
                after += layout.End - layout.Start;
            }

            var periods = new List<BossPeriod>();
            var bosses = new List<BossInfo>();

            for (int j = 0; j < layouts.Length; j++)
            {
                BossPeriod[] mapPeriods;
                BossInfo[] mapBosses;

                if (j == index)
                {
                    mapPeriods = own;
                    mapBosses = ownBosses;
                }
                else if (layouts[j] is Layout layout)
                {
                    mapPeriods = layout.Periods;
                    mapBosses = layout.Bosses;
                }
                else
                    continue;

                int slots = bosses.Count;
                bosses.AddRange(mapBosses);

                foreach (var period in mapPeriods)
                {
                    periods.Add(new BossPeriod
                    {
                        Start = period.Start + shift[j],
                        ShootStart = period.ShootStart + shift[j],
                        End = period.End + shift[j],
                        Kind = period.Kind,
                        BossSlot = period.BossSlot + slots,
                        Boss = period.Boss,
                        Rank = period.Rank,
                    });
                }
            }

            Logger.Log($"Danmakusu: Marathon bar: map {index + 1} of {layouts.Length} shows {layouts.Count(l => l != null) + (layouts[index] == null ? 1 : 0)} maps, {periods.Count} sections, {bosses.Count} bosses");
            return (periods.ToArray(), bosses.ToArray(), before, after);
        }
    }
}
