using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Audio.Track;
using osu.Game.Beatmaps;
using osu.Game.Extensions;

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

        /// <summary>
        /// How long one point of the waveform really is (ms). osu! puts (int)(frequency / 1000 x channels) samples into a point, so a 44.1 kHz song's points are 44/44.1 ms long: counted as a millisecond
        /// each, everything came later and later through the song (0.4 s by three minutes; until 2026-11-13).
        /// </summary>
        private readonly double scale;

        /// <summary>The drum hits that come at the frequency the song's drums most often hit at (from the hit before or to the hit after), in order: when (ms) and how hard.</summary>
        private readonly List<(double Time, float Strength)> normalHits;

        /// <summary>The drum hits that come at half that frequency (twice as far apart: half-time, a dubstep drop), in order.</summary>
        private readonly List<(double Time, float Strength)> halfTimeHits;

        /// <summary>Every drum hit (kicks and snares), in order: when (ms) and how hard (how many times the band's average it jumped). The map generator puts objects on them.</summary>
        public IReadOnlyList<(double Time, float Strength)> Hits { get; }

        /// <summary>The hits of the bass alone (kicks), in order: where the beats of a song without a map are put (<see cref="Generation.Tempo"/>).</summary>
        public IReadOnlyList<(double Time, float Strength)> Kicks { get; private init; } = Array.Empty<(double, float)>();

        /// <summary>How long the song is that the bands were read from (ms).</summary>
        public double Length => low.Length * (double)bucket_ms * scale;

        private SongBands(float[] low, float[] mid, float[] high, List<(double Time, float Strength)> hits, double scale)
        {
            this.low = low;
            this.mid = mid;
            this.high = high;
            this.scale = scale;
            Hits = hits;

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

        /// <summary>
        /// A hit is found this much after it is heard (ms): the waveform's bands are worked out over blocks of 1024 samples (23 ms). Measured against the objects of 21 mapped songs (SectionCheck
        /// tempotest, TEMPO_DEBUG=1): the hits came a median 16 ms after them (2026-11-13).
        /// </summary>
        private const double hit_lag_ms = 16;

        /// <summary>The drum hits of one band: where it jumps up much more than it does round there (a kick in the bass, a snare in the mid).</summary>
        private static void findHits(Waveform.Point[] points, Func<Waveform.Point, double> band, List<(double, float)> into, double scale)
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

                into.Add((Math.Max(f * frame_ms * scale - hit_lag_ms, 0), (float)(rise[f] / average)));
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
        public static SongBands? From(IWorkingBeatmap? working)
        {
            if (working == null)
                return null;

            // (how long a point of its waveform is: from the song file's own header)
            double scale = 1;

            try
            {
                string? path = (working.BeatmapInfo?.BeatmapSet as osu.Game.Database.IHasRealmFiles)?.GetPathForFile(working.BeatmapInfo!.Metadata.AudioFile);

                if (!string.IsNullOrEmpty(path))
                {
                    using var stream = working.GetStream(path);
                    scale = MsPerPoint(stream);
                }
            }
            catch
            {
                scale = 1;
            }

            return From(working.Waveform, scale);
        }

        /// <summary>The bands of a song file (the tools, and songs without maps).</summary>
        public static SongBands? FromFile(string path)
        {
            double scale;

            using (var stream = File.OpenRead(path))
                scale = MsPerPoint(stream);

            using var waveform = new Waveform(File.OpenRead(path));
            return From(waveform, scale);
        }

        /// <summary>
        /// How long one point of osu!'s waveform of a song is (ms), from the song's header: osu! puts (int)(frequency x 0.001 x channels) samples into a point (the same sum here, in the same float numbers).
        /// 1 when it cannot be read.
        /// </summary>
        public static double MsPerPoint(Stream? audio)
        {
            if (audio == null)
                return 1;

            try
            {
                var procedures = new ManagedBass.FileProcedures
                {
                    Close = _ => { },
                    Length = _ => audio.Length,
                    Read = (buffer, length, _) =>
                    {
                        var bytes = new byte[length];
                        int read = audio.Read(bytes, 0, length);
                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, buffer, read);
                        return read;
                    },
                    Seek = (offset, _) =>
                    {
                        audio.Seek(offset, SeekOrigin.Begin);
                        return true;
                    },
                };

                int handle = ManagedBass.Bass.CreateStream(ManagedBass.StreamSystem.NoBuffer, ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Float, procedures);

                if (handle == 0)
                    return 1;

                try
                {
                    if (!ManagedBass.Bass.ChannelGetInfo(handle, out var info) || info.Frequency <= 0 || info.Channels <= 0)
                        return 1;

                    int perPoint = (int)((float)info.Frequency * 0.001f * (float)info.Channels);
                    return perPoint > 0 ? perPoint / (double)info.Channels / info.Frequency * 1000 : 1;
                }
                finally
                {
                    ManagedBass.Bass.StreamFree(handle);
                    GC.KeepAlive(procedures);
                }
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>The bands of a song's <paramref name="waveform"/>; null when it cannot be read. <paramref name="scale"/>: how long one of its points is (ms, see <see cref="MsPerPoint"/>).</summary>
        public static SongBands? From(Waveform? waveform, double scale = 1)
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
                findHits(points, point => point.LowIntensity, hits, scale); // (kicks)
                var kicks = hits.ToList();
                findHits(points, point => point.MidIntensity, hits, scale); // (snares)
                hits.Sort((a, b) => a.Time.CompareTo(b.Time));

                return new SongBands(low, mid, high, hits, scale) { Kicks = kicks };
            }
            catch
            {
                return null; // (no track, or it cannot be decoded: the map's own measures choose alone)
            }
        }

        /// <summary>How strong the bass is on average between <paramref name="from"/> and <paramref name="to"/> (ms of the song).</summary>
        public float Bass(double from, double to) => average(low, from / scale, to / scale);

        /// <summary>How strong the mid frequencies are on average between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public float Mid(double from, double to) => average(mid, from / scale, to / scale);

        /// <summary>How strong the treble is on average between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public float Treble(double from, double to) => average(high, from / scale, to / scale);

        /// <summary>How loud the song is on average between <paramref name="from"/> and <paramref name="to"/>: its three bands together.</summary>
        public float Loudness(double from, double to) => Bass(from, to) + Mid(from, to) + Treble(from, to);

        /// <summary>The drum hit nearest to <paramref name="time"/> within <paramref name="reach"/> ms: how hard it is (0 = none that near).</summary>
        public float HitNear(double time, double reach)
        {
            int at = lowerBound(Hits as List<(double Time, float Strength)> ?? Hits.ToList(), time - reach);
            float best = 0f;

            for (int i = at; i < Hits.Count && Hits[i].Time <= time + reach; i++)
                best = Math.Max(best, Hits[i].Strength);

            return best;
        }

        /// <summary>(<paramref name="from"/> and <paramref name="to"/> in points of the waveform)</summary>
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
