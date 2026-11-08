using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Audio.Track;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// How strong the song's treble, bass and mid frequencies are over time, read from the track's waveform (averaged over short stretches), and where its drums hit: short strong bursts
    /// of the bass (the waveform's bass band is 20-100 Hz: kicks) and of the mid (100-2000 Hz: snares) - sudden jumps of the band well above the jumps round them that fall back again
    /// within 150 ms. Used to choose sections for the bosses
    /// when the map has too few of its own (see <see cref="BulletPlanner.Sections"/>).
    /// </summary>
    public sealed class SongBands
    {
        /// <summary>The waveform has one point per millisecond; this many of them are averaged into one stretch.</summary>
        private const int bucket_ms = 50;

        private readonly float[] low, mid, high;

        /// <summary>The drum hits that come at the frequency the song's drums most often hit at (from the hit before or to the hit after), in order: when (ms) and how hard.</summary>
        private readonly List<(double Time, float Strength)> normalHits;

        /// <summary>The drum hits that come at half that frequency (twice as far apart: half-time, a dubstep drop), in order.</summary>
        private readonly List<(double Time, float Strength)> halfTimeHits;

        private SongBands(float[] low, float[] mid, float[] high, List<(double Time, float Strength)> hits)
        {
            this.low = low;
            this.mid = mid;
            this.high = high;

            double usual = usualSpacing(hits);
            normalHits = atSpacing(hits, usual);
            halfTimeHits = atSpacing(hits, usual * 2);
        }

        // ---- how often the drums hit ----

        /// <summary>The usual spacing of the drums is looked for between these (ms), in steps of this; two spacings this close (as a share) are the same.</summary>
        private const int spacing_min_ms = 150, spacing_max_ms = 2000, spacing_step_ms = 10;

        private const double spacing_tolerance = 0.12;

        /// <summary>The spacing the song's drum hits most often have to the next one (their most common frequency, as ms between hits); 0 when there are too few hits to tell.</summary>
        private static double usualSpacing(List<(double Time, float Strength)> hits)
        {
            if (hits.Count < 8)
                return 0;

            var counts = new int[(spacing_max_ms - spacing_min_ms) / spacing_step_ms + 1];

            for (int i = 1; i < hits.Count; i++)
            {
                double spacing = hits[i].Time - hits[i - 1].Time;

                if (spacing >= spacing_min_ms && spacing <= spacing_max_ms)
                    counts[(int)((spacing - spacing_min_ms) / spacing_step_ms)]++;
            }

            int best = Array.IndexOf(counts, counts.Max());
            return counts[best] > 0 ? spacing_min_ms + (best + 0.5) * spacing_step_ms : 0;
        }

        /// <summary>The hits that are <paramref name="spacing"/> ms (give or take <see cref="spacing_tolerance"/>) from the hit before or after them.</summary>
        private static List<(double Time, float Strength)> atSpacing(List<(double Time, float Strength)> hits, double spacing)
        {
            var result = new List<(double Time, float Strength)>();

            if (spacing <= 0)
                return result;

            bool matches(double gap) => Math.Abs(gap - spacing) <= spacing * spacing_tolerance;

            for (int i = 0; i < hits.Count; i++)
            {
                bool before = i > 0 && matches(hits[i].Time - hits[i - 1].Time);
                bool after = i + 1 < hits.Count && matches(hits[i + 1].Time - hits[i].Time);

                if (before || after)
                    result.Add(hits[i]);
            }

            return result;
        }

        /// <summary>The half-time drums between <paramref name="from"/> and <paramref name="to"/> (ms): how many per ms (<paramref name="rate"/>) and how hard on average (<paramref name="strength"/>).</summary>
        public void HalfTimeDrums(double from, double to, out double rate, out double strength) => measure(halfTimeHits, from, to, out rate, out strength);

        // ---- the drums: kicks in the bass, snares in the mid ----

        /// <summary>Drum hits are looked for in frames this long (ms).</summary>
        private const int frame_ms = 10;

        /// <summary>A hit is a jump of a band (from one frame to the next) this many times as big as the jumps round it (half a second either way) on average, and the biggest one near it.</summary>
        private const float hit_threshold = 2f;

        private const int hit_window_frames = 50;
        private const int hit_gap_frames = 5; // (two hits of one band are at least 50 ms apart)

        /// <summary>A hit is a short burst: within this many frames of its peak the band has fallen back by at least <see cref="burst_fall"/> of what it jumped (a long bass note is no drum).</summary>
        private const int burst_frames = 15;

        private const double burst_fall = 0.5;

        /// <summary>The drum hits of one band: where it jumps up much more than it does round there (a kick in the bass, a snare in the mid).</summary>
        private static void findHits(Waveform.Point[] points, Func<Waveform.Point, double> band, List<(double, float)> into)
        {
            int frames = points.Length / frame_ms;

            if (frames < 3)
                return;

            var level = new double[frames];
            double total = 0;

            for (int f = 0; f < frames; f++)
            {
                for (int i = f * frame_ms; i < (f + 1) * frame_ms; i++)
                    level[f] += band(points[i]);

                level[f] /= frame_ms;
                total += level[f];
            }

            double average = total / frames;

            if (average <= 1e-9)
                return;

            var rise = new double[frames];

            for (int f = 1; f < frames; f++)
                rise[f] = Math.Max(0, level[f] - level[f - 1]);

            // (the average jump round every frame, kept as a running sum)
            var sums = new double[frames + 1];

            for (int f = 0; f < frames; f++)
                sums[f + 1] = sums[f] + rise[f];

            int last = -hit_gap_frames;

            for (int f = 1; f < frames - 1; f++)
            {
                int from = Math.Max(0, f - hit_window_frames), to = Math.Min(frames, f + hit_window_frames + 1);
                double around = (sums[to] - sums[from]) / (to - from);

                if (rise[f] <= around * hit_threshold || rise[f] < rise[f - 1] || rise[f] < rise[f + 1] || f - last < hit_gap_frames)
                    continue;

                // (only a short burst: the band falls back soon after it)
                double peak = level[f], fallenTo = peak;

                for (int g = f + 1; g < Math.Min(frames, f + 1 + burst_frames); g++)
                    fallenTo = Math.Min(fallenTo, level[g]);

                if (peak - fallenTo < rise[f] * burst_fall)
                    continue;

                into.Add((f * frame_ms, (float)(rise[f] / average)));
                last = f;
            }
        }

        /// <summary>
        /// The drums between <paramref name="from"/> and <paramref name="to"/> (ms of the song) that hit at their usual frequency: how many kicks and snares there are per ms
        /// (<paramref name="rate"/>) and how hard they are on average (<paramref name="strength"/>).
        /// </summary>
        public void Drums(double from, double to, out double rate, out double strength) => measure(normalHits, from, to, out rate, out strength);

        private static void measure(List<(double Time, float Strength)> list, double from, double to, out double rate, out double strength)
        {
            int first = lowerBound(list, from), count = lowerBound(list, to) - first;
            double sum = 0;

            for (int i = first; i < first + count; i++)
                sum += list[i].Strength;

            rate = count / Math.Max(to - from, 1);
            strength = count > 0 ? sum / count : 0;
        }

        private static int lowerBound(List<(double Time, float Strength)> list, double time)
        {
            int low = 0, high = list.Count;

            while (low < high)
            {
                int middle = (low + high) / 2;

                if (list[middle].Time < time)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        /// <summary>The bands of the song of <paramref name="working"/>; null when its track cannot be read.</summary>
        public static SongBands? From(IWorkingBeatmap? working) => From(working?.Waveform);

        /// <summary>The bands of a song's <paramref name="waveform"/>; null when it cannot be read.</summary>
        public static SongBands? From(Waveform? waveform)
        {
            try
            {
                var points = waveform?.GetPoints();

                if (points == null || points.Length == 0)
                    return null;

                int count = (points.Length + bucket_ms - 1) / bucket_ms;
                var low = new float[count];
                var mid = new float[count];
                var high = new float[count];

                for (int b = 0; b < count; b++)
                {
                    int from = b * bucket_ms, to = Math.Min(points.Length, from + bucket_ms);

                    for (int i = from; i < to; i++)
                    {
                        low[b] += (float)points[i].LowIntensity;
                        mid[b] += (float)points[i].MidIntensity;
                        high[b] += (float)points[i].HighIntensity;
                    }

                    int n = Math.Max(to - from, 1);
                    low[b] /= n;
                    mid[b] /= n;
                    high[b] /= n;
                }

                var hits = new List<(double Time, float Strength)>();
                findHits(points, point => point.LowIntensity, hits); // (kicks)
                findHits(points, point => point.MidIntensity, hits); // (snares)
                hits.Sort((a, b) => a.Time.CompareTo(b.Time));

                return new SongBands(low, mid, high, hits);
            }
            catch
            {
                return null; // (no track, or it cannot be decoded: the map's own measures choose alone)
            }
        }

        /// <summary>How strong the bass is on average between <paramref name="from"/> and <paramref name="to"/> (ms of the song).</summary>
        public float Bass(double from, double to) => average(low, from, to);

        /// <summary>How strong the mid frequencies are on average between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public float Mid(double from, double to) => average(mid, from, to);

        /// <summary>How strong the treble is on average between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public float Treble(double from, double to) => average(high, from, to);

        private static float average(float[] buckets, double from, double to)
        {
            int first = Math.Clamp((int)(from / bucket_ms), 0, buckets.Length);
            int last = Math.Clamp((int)Math.Ceiling(to / bucket_ms), first, buckets.Length);

            if (last <= first)
                return 0f;

            float sum = 0f;

            for (int b = first; b < last; b++)
                sum += buckets[b];

            return sum / (last - first);
        }
    }
}
