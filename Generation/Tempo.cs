using System;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.Generation
{
    /// <summary>
    /// The tempo of a song that has no map (a placeholder's, see <see cref="Placeholders"/>), from where its drums hit (<see cref="SongBands.Hits"/>): for every beat length from 60 to 220 BPM, how well the hits
    /// line up on a grid that long (the hits as points round a circle one beat long, weighted by how hard they are: how far their average is from the middle), weighted towards the tempos songs most often have
    /// (a grid of half the beat always lines up at least as well), then found to a hundredth of a millisecond near the best. The first beat of a bar is the beat of four that the hits fall on most.
    /// </summary>
    public static class Tempo
    {
        private const double slowest_bpm = 60, fastest_bpm = 300, likeliest_bpm = 170;

        /// <summary>(how near a hit has to be to a beat to count for where the beats are: ms, the spread of a bell)</summary>
        private const double on_beat_ms = 12;

        /// <summary>(how much less likely a tempo an octave from the likeliest is: the spread of the weighting, in octaves)</summary>
        private const double octave_spread = 0.6;

        private const double coarse_step_ms = 0.5, fine_step_ms = 0.01;

        /// <summary>Finds the song's beat length (ms) and where a bar of it starts (ms, within the first bar); false when it has too few drum hits to tell.</summary>
        public static bool Find(SongBands song, out double offset, out double beatLength)
        {
            offset = 0;
            beatLength = 500;

            var hits = song.Hits;

            if (hits.Count < 16)
                return false;

            var times = new double[hits.Count];
            var weights = new double[hits.Count];

            for (int i = 0; i < hits.Count; i++)
            {
                times[i] = hits[i].Time;
                weights[i] = Math.Min(hits[i].Strength, 6f); // (one huge hit does not decide the tempo)
            }

            double best = 0, bestScore = double.NegativeInfinity;

            for (double length = 60000 / fastest_bpm; length <= 60000 / slowest_bpm; length += coarse_step_ms)
            {
                double score = lineUp(times, weights, length, out _) * likelihood(length);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = length;
                }
            }

            // (to a hundredth of a millisecond round it: over a whole song a beat off by that much drifts by a few ms)
            double fine = best, fineScore = double.NegativeInfinity, phase = 0;

            for (double length = best - coarse_step_ms; length <= best + coarse_step_ms; length += fine_step_ms)
            {
                double score = lineUp(times, weights, length, out double at);

                if (score > fineScore)
                {
                    fineScore = score;
                    fine = length;
                    phase = at;
                }
            }

            beatLength = fine;
            offset = Place(song, beatLength);
            return true;
        }

        /// <summary>Where a bar of <paramref name="beatLength"/> ms starts in the song (ms, within the first bar): the beats where the most kicks fall, the bar's first where the hits are hardest.</summary>
        public static double Place(SongBands song, double beatLength)
        {
            var hits = song.Hits;
            var times = hits.Select(h => h.Time).ToArray();
            var weights = hits.Select(h => (double)Math.Min(h.Strength, 6f)).ToArray();

            if (times.Length == 0 || beatLength <= 0)
                return 0;

            // (where the beats are: the grid most of the kicks fall right on, to 2 ms - the kicks mark the beat; their average place on it is pulled away by the hits between beats, and the other hits
            // - bass lines, the mid's notes - fall between beats as often as on them)
            var kicks = song.Kicks.Count >= 16 ? song.Kicks : hits;
            double beatAt = 0, mostOn = -1;

            for (double at = 0; at < beatLength; at += 2)
            {
                double on = 0;

                for (int i = 0; i < kicks.Count; i++)
                {
                    double away = ((kicks[i].Time - at) % beatLength + beatLength) % beatLength;
                    away = Math.Min(away, beatLength - away);

                    if (away < on_beat_ms * 3)
                        on += Math.Min(kicks[i].Strength, 6f) * Math.Exp(-away * away / (2 * on_beat_ms * on_beat_ms));
                }

                if (on > mostOn)
                {
                    mostOn = on;
                    beatAt = at;
                }
            }

            // (the first beat of a bar: the one of four that the hits near it are hardest on)
            var onBeat = new double[4];
            double window = beatLength / 8;

            for (int i = 0; i < times.Length; i++)
            {
                double beats = (times[i] - beatAt) / beatLength;
                double nearest = Math.Round(beats);

                if (Math.Abs(beats - nearest) * beatLength > window)
                    continue;

                onBeat[(int)(((long)nearest % 4 + 4) % 4)] += weights[i];
            }

            int downbeat = Array.IndexOf(onBeat, Math.Max(Math.Max(onBeat[0], onBeat[1]), Math.Max(onBeat[2], onBeat[3])));
            double offset = beatAt + downbeat * beatLength;

            while (offset >= beatLength * 4)
                offset -= beatLength * 4;

            return offset;
        }

        /// <summary>How well the hits line up on a grid of <paramref name="length"/> ms: 0 (anywhere) to 1 (all on it); <paramref name="phase"/>: where on the beat they are (radians).</summary>
        private static double lineUp(double[] times, double[] weights, double length, out double phase)
        {
            double sumCos = 0, sumSin = 0, sumWeights = 0;
            double turn = 2 * Math.PI / length;

            for (int i = 0; i < times.Length; i++)
            {
                double angle = times[i] * turn;
                sumCos += weights[i] * Math.Cos(angle);
                sumSin += weights[i] * Math.Sin(angle);
                sumWeights += weights[i];
            }

            phase = Math.Atan2(sumSin, sumCos);
            return sumWeights > 0 ? Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / sumWeights : 0;
        }

        /// <summary>How likely a beat of <paramref name="length"/> ms is, as against the likeliest tempo (1).</summary>
        private static double likelihood(double length)
        {
            double octaves = Math.Log2(60000 / length / likeliest_bpm) / octave_spread;
            return Math.Exp(-0.5 * octaves * octaves);
        }
    }
}
