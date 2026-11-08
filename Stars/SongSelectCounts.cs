using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.Danmakusu.Stars
{
    /// <summary>
    /// How song select shows Danmakusu's counts (<see cref="DanmakusuBeatmap"/>: Bullets, Beams, Spinners, Most bosses). osu! lays a map's counts out as bars, as its difficulty bars on the right (circle
    /// size, approach rate, ...), all as wide as the widest name and at least 65, and when they do not all fit beside the difficulty bars it shows them as a small list instead: four of ours, "Most bosses"
    /// among them, never fit (osu!'s own are three). Ours are laid out here: as wide as osu!'s where they fit, narrower down to their own names (and numbers) where they do not, and the list only when even
    /// that does not fit. And when the counts of the map shown come in from the background (<see cref="CountsInBackground"/>), song select shows them (<see cref="Refresh"/>). osu!'s own modes stay as
    /// they are. Done with Harmony, like <see cref="SongSelectStars"/>: if osu! has changed where these are, only this is lost, and the log says so.
    /// </summary>
    public static class SongSelectCounts
    {
        private static readonly object gate = new object();
        private static bool tried;

        /// <summary>(for the tools) what came of it.</summary>
        public static string Status { get; private set; } = "not tried";

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
                Logger.Log(Status = "Danmakusu: song select shows its counts as bars and counts maps in the background");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: song select shows its counts as osu! lays them out ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        // ---- what osu! has, found once ----

        private const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static MethodInfo updateCountStatistics = null!;
        private static FieldInfo cancellationSource = null!;
        private static PropertyInfo shownBeatmap = null!;

        // (the number under a bar: it may be wider than the name; without it only the names count)
        private static FieldInfo? valueText;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var display = typeof(BeatmapTitleWedge.DifficultyDisplay);
            var bars = typeof(BeatmapTitleWedge.DifficultyStatisticsDisplay);

            updateCountStatistics = display.GetMethod("updateCountStatistics", any, new[] { typeof(CancellationToken) }) ?? throw new MissingMethodException(display.Name, "updateCountStatistics");
            cancellationSource = display.GetField("cancellationSource", any) ?? throw new MissingFieldException(display.Name, "cancellationSource");
            shownBeatmap = display.GetProperty("beatmap", any) ?? throw new MissingMemberException(display.Name, "beatmap");
            valueText = typeof(BeatmapTitleWedge.StatisticDifficulty).GetField("valueText", any);

            var updateLayout = bars.GetMethod("updateLayout", any) ?? throw new MissingMethodException(bars.Name, "updateLayout");
            var update = bars.GetMethod("Update", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)
                         ?? throw new MissingMethodException(bars.Name, "Update");

            // (what the patches are given of osu!'s, by name)
            foreach (string field in new[] { "autoSize", "statisticsFlow", "tinyStatisticsGrid", "displayedTinyStatistics" })
            {
                if (bars.GetField(field, any) == null)
                    throw new MissingFieldException(bars.Name, field);
            }

            // (everything is looked up before anything is patched: either all of it, or nothing)
            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.SongSelectCounts");
            harmony.Patch(updateLayout, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectCounts), nameof(layoutPrefix)));
            harmony.Patch(update, postfix: new HarmonyLib.HarmonyMethod(typeof(SongSelectCounts), nameof(updatePostfix)));
            harmony.Patch(updateCountStatistics, postfix: new HarmonyLib.HarmonyMethod(typeof(SongSelectCounts), nameof(countsPostfix)));
        }

        // ---- the bars ----

        /// <summary>(osu!'s width for every bar)</summary>
        private const float bar_width = 65;

        /// <summary>(osu! lets the bars run this far into the gap before its difficulty bars before it shows the list)</summary>
        private const float slack = 20;

        /// <summary>Are these bars Danmakusu's counts?</summary>
        private static bool ours(FillFlowContainer<BeatmapTitleWedge.StatisticDifficulty> flow) => flow.Count > 0 && flow[0].Value.Label.ToString() == DanmakusuBeatmap.BULLETS_LABEL;

        /// <summary>(instead of osu!'s layout of the counts, for ours) nothing: <see cref="updatePostfix"/> lays them out.</summary>
        private static bool layoutPrefix(bool ___autoSize, FillFlowContainer<BeatmapTitleWedge.StatisticDifficulty> ___statisticsFlow) => ___autoSize || !ours(___statisticsFlow);

        /// <summary>(every frame, for ours) as wide as osu!'s bars where they fit, narrower down to their names and numbers where they do not, and the list only when even that does not fit.</summary>
        private static void updatePostfix(BeatmapTitleWedge.DifficultyStatisticsDisplay __instance, bool ___autoSize, FillFlowContainer<BeatmapTitleWedge.StatisticDifficulty> ___statisticsFlow,
                                          GridContainer ___tinyStatisticsGrid, ref bool ___displayedTinyStatistics)
        {
            if (___autoSize || !ours(___statisticsFlow))
                return;

            var flow = ___statisticsFlow;
            int n = flow.Count;
            float room = __instance.DrawWidth + slack - flow.Spacing.X * (n - 1);

            Span<float> need = stackalloc float[n];

            for (int i = 0; i < n; i++)
            {
                float number = valueText?.GetValue(flow[i]) is Drawable value ? value.DrawWidth : 0;
                need[i] = Math.Max(flow[i].LabelWidth, number);
            }

            // (the widest all the bars can share: a bar is never narrower than its own name)
            float level = bar_width;
            float total = widths(need, level);

            while (total > room && level > 0)
            {
                level = Math.Max(level - 1, 0);
                total = widths(need, level);
            }

            bool fits = total <= room;

            if (fits)
            {
                for (int i = 0; i < n; i++)
                {
                    float width = Math.Max(need[i], level);

                    if (flow[i].Width != width)
                        flow[i].Width = width;
                }
            }

            // (as osu! changes between the bars and the list)
            if (fits == ___displayedTinyStatistics)
            {
                if (fits)
                {
                    ___tinyStatisticsGrid.Hide();
                    flow.FadeIn(200, Easing.OutQuint);
                }
                else
                {
                    flow.Hide();
                    ___tinyStatisticsGrid.FadeIn(200, Easing.OutQuint);
                }

                ___displayedTinyStatistics = !fits;
            }
        }

        private static float widths(ReadOnlySpan<float> need, float level)
        {
            float sum = 0;

            foreach (float width in need)
                sum += Math.Max(width, level);

            return sum;
        }

        // ---- showing the counts again when they come in ----

        private static WeakReference<BeatmapTitleWedge.DifficultyDisplay>? shown;

        private static void countsPostfix(BeatmapTitleWedge.DifficultyDisplay __instance) => shown = new WeakReference<BeatmapTitleWedge.DifficultyDisplay>(__instance);

        /// <summary>(on the update thread, when maps were counted in the background) song select shows the counts of its map again, if it was one of them.</summary>
        public static void Refresh()
        {
            var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (CountsInBackground.Finished.TryDequeue(out string? md5))
                finished.Add(md5);

            if (finished.Count == 0 || shown == null || !shown.TryGetTarget(out var display))
                return;

            try
            {
                if (display.LoadState != LoadState.Loaded || display.Parent == null)
                    return;

                var working = (shownBeatmap.GetValue(display) as IBindable<WorkingBeatmap>)?.Value;

                if (working?.BeatmapInfo?.MD5Hash == null || !finished.Contains(working.BeatmapInfo.MD5Hash))
                    return;

                var token = (cancellationSource.GetValue(display) as CancellationTokenSource)?.Token ?? CancellationToken.None;
                updateCountStatistics.Invoke(display, new object[] { token });
            }
            catch (Exception e)
            {
                // (song select was on its way out)
                Logger.Log($"Danmakusu: song select could not show the counts again ({e.Message})");
            }
        }
    }
}
