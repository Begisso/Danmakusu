using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace osu.Game.Rulesets.Danmakusu.Generation
{
    /// <summary>
    /// What the map generator knows about osu! maps, learnt from the user's downloaded maps (Tools/SectionCheck <c>trainmap</c>: every osu!standard map of <c>H:\osu maps\osu files</c> that has Danmakusu stars in
    /// <c>danmakusu_stars.csv</c>), one bucket for every whole number of Danmakusu stars (1..10). In a bucket: the difficulty settings maps of those stars have (medians), the rhythm (which object comes after which two:
    /// how many twelfths of a beat after the one before, what it is, how long it lasts), how far the cursor jumps and how sharply it turns (by how close in time the objects are), how often a slider is curved,
    /// the hitsounds (on the first beat of a bar, on a beat, between beats) and how long combos are. Kept as JSON in Resources/Generation/map_model.json (embedded).
    /// </summary>
    public sealed class MapModel
    {
        public int Version { get; set; } = 1;

        public List<Bucket> Buckets { get; set; } = new List<Bucket>();

        /// <summary>
        /// For every star level the slider can choose (1..10): which bucket's maps a generated map is made like (fractions mix the two buckets round it; past the last bucket its rhythm is drawn closer together), so
        /// that what comes out is rated that many stars on average (calibrated by Tools/SectionCheck <c>gentest</c>).
        /// </summary>
        public List<double> SourceFor { get; set; } = new List<double>();

        /// <summary>How many stars the generated maps of source 1, 2, 3... came out on average over the calibration's songs (rising): the generator corrects a song that comes out harder or easier with it.</summary>
        public List<double> MeanStars { get; set; } = new List<double>();

        /// <summary>How many stars a map of <paramref name="source"/> comes out on average (between the calibrated sources: in a straight line).</summary>
        public double Expected(double source)
        {
            if (MeanStars.Count == 0)
                return source;

            // (past the calibrated sources: on along the last step; below the first, back along the first)
            if (source > MeanStars.Count)
                return MeanStars[^1] + (source - MeanStars.Count) * lastStep;

            if (source < 1)
                return Math.Max(MeanStars[0] - (1 - source) * firstStep, 0.3);

            double at = Math.Clamp(source, 1, MeanStars.Count) - 1;
            int low = Math.Min((int)Math.Floor(at), MeanStars.Count - 1);
            int high = Math.Min(low + 1, MeanStars.Count - 1);

            return MeanStars[low] + (MeanStars[high] - MeanStars[low]) * (at - low);
        }

        /// <summary>(stars a source more gives past the calibrated ones: the last step, half a star at least)</summary>
        private double lastStep => MeanStars.Count >= 2 ? Math.Max(MeanStars[^1] - MeanStars[^2], 0.5) : 1;

        /// <summary>How far past the calibrated sources the map of a song that comes out too easy may still be drawn closer together (sources).</summary>
        public const int BEYOND = 6;

        /// <summary>(stars a source less gives below the first calibrated one: the first step, a star at least - the easiest maps are drawn further apart there, see <see cref="MapGenerator"/>)</summary>
        private double firstStep => MeanStars.Count >= 2 ? Math.Max(MeanStars[1] - MeanStars[0], 1) : 1;

        /// <summary>How far below the first source a map may be drawn further apart (sources): 1 star has to be reachable (since 2026-11-13).</summary>
        public const double BELOW = 0.75;

        /// <summary>The source whose maps come out <paramref name="stars"/> stars on average (as near as the calibrated sources reach).</summary>
        public double SourceOf(double stars)
        {
            if (MeanStars.Count == 0)
                return Math.Clamp(stars, 1, 10);

            if (stars <= MeanStars[0])
                return Math.Max(1 - (MeanStars[0] - stars) / firstStep, 1 - BELOW);

            for (int i = 0; i + 1 < MeanStars.Count; i++)
            {
                if (stars >= MeanStars[i] && stars <= MeanStars[i + 1])
                    return i + 1 + (stars - MeanStars[i]) / Math.Max(MeanStars[i + 1] - MeanStars[i], 1e-6);
            }

            // (past the last: on along its step, as far as BEYOND)
            return Math.Min(MeanStars.Count + Math.Max(stars - MeanStars[^1], 0) / lastStep, MeanStars.Count + BEYOND);
        }

        public sealed class Bucket
        {
            public int Stars { get; set; }
            public int Maps { get; set; }
            public float CircleSize { get; set; } = 4;
            public float ApproachRate { get; set; } = 9;
            public float OverallDifficulty { get; set; } = 8;
            public float DrainRate { get; set; } = 5;
            public double SliderMultiplier { get; set; } = 1.4;
            public int Volume { get; set; } = 70;

            /// <summary>The objects that came, as tokens (<see cref="Token"/>), and how often each came after none, one or two others.</summary>
            public Dictionary<string, int> First { get; set; } = new Dictionary<string, int>();

            public Dictionary<string, Dictionary<string, int>> AfterOne { get; set; } = new Dictionary<string, Dictionary<string, int>>();

            public Dictionary<string, Dictionary<string, int>> AfterTwo { get; set; } = new Dictionary<string, Dictionary<string, int>>();

            /// <summary>How far from where the cursor was (the end of the object before) the next one starts, by how close in time they are (<see cref="GapClass"/>): counts in bins of <see cref="DISTANCE_BIN"/> osu! px.</summary>
            public int[][] Distance { get; set; } = [];

            /// <summary>How sharply the way turns from one jump to the next (0 = straight on, the last bin = straight back), by <see cref="GapClass"/>: counts in bins of <see cref="TURN_BIN_DEGREES"/>.</summary>
            public int[][] Turn { get; set; } = [];

            /// <summary>How often a turn goes the same way round as the one before.</summary>
            public double SameWay { get; set; } = 0.5;

            /// <summary>How often a slider is curved (not a straight line).</summary>
            public double Curved { get; set; } = 0.5;

            /// <summary>How often each addition is on an object, on the first beat of a bar / on another beat / between beats: [class][whistle, clap, finish].</summary>
            public double[][] Sounds { get; set; } = [];

            /// <summary>How long combos are: counts of 1..<see cref="MOST_COMBO"/> objects.</summary>
            public int[] Combo { get; set; } = [];

            // ---- the structure of the maps (since 2026-11-13; the defaults are what the generator did by hand before) ----

            /// <summary>In a combo, how much the jumps of the same rhythm differ in length (the root mean square of how far each is from their mean, as a share of it).</summary>
            public double SpacingJitter { get; set; } = 0.046;

            /// <summary>In a combo, how much the turns of the same rhythm differ (degrees, their spread).</summary>
            public double TurnSpread { get; set; } = 0;

            /// <summary>How many combos zigzag (their turns go the other way round more often than the same way).</summary>
            public double Zigzag { get; set; } = -1;

            /// <summary>How often a bar has the same rhythm as the bar before it; and when the bar before had already repeated its own.</summary>
            public double BarRepeat { get; set; } = 0.6;

            public double BarRepeatAgain { get; set; } = 0.3;

            /// <summary>How often an object on the first beat of a bar starts a new combo.</summary>
            public double ComboOnDownbeat { get; set; } = 1;

            /// <summary>How far curved sliders bend away from the line between their ends, as a share of that line: counts in bins of <see cref="BEND_BIN"/>.</summary>
            public int[] Bend { get; set; } = [];
        }

        public const double BEND_BIN = 0.05;
        public const int BEND_BINS = 12;

        public const int GAP_CLASSES = 4;
        public const float DISTANCE_BIN = 12f;
        public const int DISTANCE_BINS = 48;
        public const float TURN_BIN_DEGREES = 15f;
        public const int TURN_BINS = 12;
        public const int MOST_COMBO = 16;

        /// <summary>A rhythm is in twelfths of a beat (it holds quarters, thirds, sixths and eighths... nearly).</summary>
        public const int PARTS = 12;

        /// <summary>Gaps longer than this many twelfths (8 beats) are all the same rest.</summary>
        public const int MOST_GAP = 96;

        public const int CIRCLE = 0, SLIDER = 1, SPINNER = 2;

        /// <summary>An object as a token: twelfths of a beat since the one before started, what it is, how often a slider goes back, how long it lasts in twelfths.</summary>
        public static string Token(int gap, int kind, int repeats, int duration) => $"{gap}/{kind}/{repeats}/{duration}";

        public static (int Gap, int Kind, int Repeats, int Duration) Parse(string token)
        {
            var parts = token.Split('/');
            return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        }

        /// <summary>How close in time two objects are: up to a quarter beat (streams), a half, a beat, or more.</summary>
        public static int GapClass(int gap) => gap <= 3 ? 0 : gap <= 6 ? 1 : gap <= 12 ? 2 : 3;

        /// <summary>A number of beats in twelfths of a beat (at least 1, at most <see cref="MOST_GAP"/>).</summary>
        public static int Parts(double beats) => Math.Clamp((int)Math.Round(beats * PARTS), 1, MOST_GAP);

        /// <summary>
        /// The beat that rhythms are counted in at <paramref name="time"/>: the bosses' (<see cref="DanmakusuBalance.BossBeat"/>: a very fast tempo is halved and a very slow one doubled until it is between
        /// 100 and 200 BPM or so), so that a 300 BPM song mapped in halves is the same rhythm as a 150 BPM one mapped in quarters. Learning and making count alike.
        /// </summary>
        public static double BeatAt(osu.Game.Beatmaps.ControlPoints.ControlPointInfo timing, double time)
            => Math.Max(DanmakusuBalance.BossBeat(DanmakusuBalance.CapBeat(timing.TimingPointAt(time).BeatLength)), 1);

        private static MapModel? loaded;
        private static readonly object gate = new object();

        /// <summary>The model the game has (embedded); null when there is none.</summary>
        public static MapModel? Embedded
        {
            get
            {
                lock (gate)
                {
                    if (loaded != null)
                        return loaded;

                    using var stream = typeof(MapModel).Assembly.GetManifestResourceStream("osu.Game.Rulesets.Danmakusu.Resources.Generation.map_model.json");

                    if (stream == null)
                        return null;

                    return loaded = Read(stream);
                }
            }
        }

        public static MapModel Read(Stream stream) => JsonSerializer.Deserialize<MapModel>(stream) ?? new MapModel();

        public void Write(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));

        /// <summary>The bucket of <paramref name="stars"/> stars, or the nearest there is.</summary>
        public Bucket? Nearest(int stars)
        {
            Bucket? best = null;

            foreach (var bucket in Buckets)
            {
                if (bucket.Maps == 0)
                    continue;

                if (best == null || Math.Abs(bucket.Stars - stars) < Math.Abs(best.Stars - stars))
                    best = bucket;
            }

            return best;
        }
    }
}
