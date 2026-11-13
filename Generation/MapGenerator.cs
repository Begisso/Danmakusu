using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Generation
{
    /// <summary>
    /// Makes a new osu! map for a song out of the <see cref="MapModel"/> (what maps of that many stars are like), for the Map Generation mod: the map's own circles, sliders and spinners are thrown away; its
    /// timing (where the beats are), its breaks (nothing is put in them) and how long it goes on stay. Objects come one after the other as the model's rhythm says (each by the two before it), on the beat grid,
    /// and a bar's rhythm is often played again in the next, as mapped songs repeat themselves. Combos start on bars (or end at the model's length), and every combo has a shape of its own, as mapped ones do:
    /// every jump of the same rhythm in it (counted from the end of the object before) is as long, and turns as sharply, the same way round or zigzagging - drawn once per combo from the model's maps.
    /// Sliders go straight or curve, as long as their time needs at the model's slider speed. The hitsounds are the map's own at the same moments (the mapper's, with the song's drums), and where it has none, its
    /// sampleset with the model's additions on fixed places of the bar. The difficulty settings are the model's too. Nothing is random: the same song and stars always give the same map.
    /// With the song (<see cref="SongBands"/>: the game reads its waveform) it listens too: of several next objects the model offers, the one that lands on a drum hit (a kick or a snare) is taken rather than
    /// one that does not, the dense ones where the song is loud and the sparse ones where it is quiet (loud and quiet as against the rest of the song, so that on the whole the model's density stays); jumps are
    /// longer where it is loud, and a finish comes more often on a strong hit.
    /// </summary>
    public static class MapGenerator
    {
        private const float margin = 24f; // (osu! px: objects stay this far inside the playfield)

        private const int candidates = 6; // (next objects offered by the model to choose from by the song)
        private const double on_hit_ms = 35; // (an object this close to a drum hit is on it)

        private static readonly Vector2 size = OsuPlayfield.BASE_SIZE;
        private static readonly Vector2 middle = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>Above the last bucket the rhythm is drawn closer together: every step of a source past it takes this much off the gaps (as a share).</summary>
        private const double squeeze_per_step = 0.3;

        /// <summary>Below the first bucket it is drawn further apart: every step of a source below it makes the gaps this much longer (as a share).</summary>
        private const double stretch_per_step = 1.0;

        /// <summary>A map that has been rated is made again from a corrected source when it is this many stars off (once).</summary>
        private const double rated_tolerance = 0.3;

        /// <summary>A generated map, not yet put in place.</summary>
        private sealed class Made
        {
            public List<OsuHitObject> Objects = new List<OsuHitObject>();
            public float CircleSize, ApproachRate, OverallDifficulty, DrainRate;
            public double SliderMultiplier;
        }

        /// <summary>
        /// Puts a generated map of <paramref name="stars"/> stars in place of the map's objects, listening to <paramref name="song"/> when it is there; false when there is no model or nothing to put it in.
        /// With <paramref name="rate"/> it is rated as the game rates maps (no mods) and, when it is far off, made once more from the source that should give the stars on this song (a song's tempo and slider
        /// speeds make its bullets faster or slower than the songs it was calibrated on); the nearer of the two stays.
        /// </summary>
        public static bool Generate(IBeatmap beatmap, int stars, Bullets.SongBands? song = null, MapModel? model = null, bool rate = true)
        {
            model ??= MapModel.Embedded;

            if (model == null || beatmap is not Beatmap<OsuHitObject> target)
                return false;

            double start, end;

            if (beatmap.HitObjects.Count == 0)
            {
                // (a song without a map of its own, a placeholder's: from the first beat at its first drum hit to a beat before its end; without the song there is nothing to go by)
                if (song == null || song.Length <= 0)
                    return false;

                double firstHit = song.Hits.Count > 0 ? song.Hits[0].Time : 0;
                start = onBeatAfter(beatmap.ControlPointInfo, Math.Max(firstHit - 60, 0));
                end = song.Length - Math.Max(beatmap.ControlPointInfo.TimingPointAt(start).BeatLength, 100);

                if (end <= start)
                    return false;
            }
            else if (!spanOf(beatmap, out start, out end))
                return false;

            double source = stars - 1 < model.SourceFor.Count && stars >= 1 ? model.SourceFor[stars - 1] : stars;

            // (the map's own hitsounds, before its objects are thrown away: the generated objects take the ones the mapper put on their moments)
            var own = new OwnSounds(beatmap);

            // (made before, for this map and stars - song select makes the map over and over, for the panel, the stars and the boss menu, and then the play does: the source the tries settled on is
            // made again at once, and it is the same map, nothing being random)
            string key = $"{beatmap.BeatmapInfo?.MD5Hash}|{beatmap.BeatmapInfo?.Metadata?.Artist}|{beatmap.BeatmapInfo?.Metadata?.Title}|{beatmap.BeatmapInfo?.DifficultyName}|{stars}|{song != null}|{start:R}|{end:R}";

            if (rate && settled.TryGetValue(key, out double known) && build(beatmap, model, known, stars, song, start, end, own) is Made again)
            {
                put(target, again);
                LastReport = $"made again from source {known:F2} (settled before)";
                return true;
            }

            var first = build(beatmap, model, source, stars, song, start, end, own);

            if (first == null)
                return false;

            put(target, first);
            LastReport = $"source {source:F2}: {first.Objects.Count} objects";

            // (a map too big to rate within osu!'s time for loading a map stays as the calibration makes it: a long map's average is the calibrated one)
            if (!rate || model.MeanStars.Count < 2 || first.Objects.Count > rate_objects_budget)
                return true;

            double got = rated(beatmap);
            int ratedObjects = first.Objects.Count;
            LastReport += $" -> {got:F2} stars";

            if (double.IsNaN(got))
                return true;

            // (up to three tries; the nearest stays)
            var tries = new List<(double Source, Made Map, double Stars)> { (source, first, got) };

            for (int attempt = 1; attempt < most_tries; attempt++)
            {
                var best = tries.OrderBy(x => Math.Abs(x.Stars - stars)).First();

                if (Math.Abs(best.Stars - stars) <= rated_tolerance)
                    break;

                // (another try is about as big as the last: not when it would go past what can be rated in time - counted in objects, not on the clock, so that every computer makes the same map)
                if (ratedObjects + tries[^1].Map.Objects.Count > rate_objects_budget)
                    break;

                var (lastSource, _, lastStars) = tries[^1];
                double next;

                if (tries.Count >= 2 && Math.Abs(tries[^1].Stars - tries[^2].Stars) > 0.05)
                {
                    // (between the last two: where the line through them reaches the stars)
                    var (before, _, beforeStars) = tries[^2];
                    next = lastSource + (stars - lastStars) * (lastSource - before) / (lastStars - beforeStars);
                }
                else
                {
                    // (this song is this many times as hard as the average song at that source: the source that gives the stars on it)
                    double expected = model.Expected(lastSource);
                    next = model.SourceOf(expected > 0 ? stars / (lastStars / expected) : stars);
                }

                next = Math.Clamp(next, 1 - MapModel.BELOW, Math.Max(model.MeanStars.Count, 1) + MapModel.BEYOND);

                if (tries.Any(x => Math.Abs(x.Source - next) < 0.05))
                    break;

                var made = build(beatmap, model, next, stars, song, start, end, own);

                if (made == null)
                    break;

                put(target, made);
                double madeStars = rated(beatmap);
                ratedObjects += made.Objects.Count;
                LastReport += $"; source {next:F2}: {made.Objects.Count} objects -> {madeStars:F2} stars";

                if (double.IsNaN(madeStars))
                    break;

                tries.Add((next, made, madeStars));
            }

            var kept = tries.OrderBy(x => Math.Abs(x.Stars - stars)).First();
            put(target, kept.Map);

            if (settled.Count > 1000)
                settled.Clear();

            settled[key] = kept.Source;
            return true;
        }

        /// <summary>How many maps are made at most to come near the stars (each is rated: about a second).</summary>
        private const int most_tries = 3;

        /// <summary>
        /// How many objects are rated at most, over all the tries: osu! gives up on a map it cannot make ready within 10 s (and song select makes it too), and the conversion and the song come first. About
        /// 4000 objects rate in a second or two.
        /// </summary>
        private const int rate_objects_budget = 4000;

        /// <summary>The source the tries settled on, for every map and stars made so far (see <see cref="Generate"/>).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> settled = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();

        /// <summary>(for the tools) what the last map made on this thread came to: the tries, their objects and stars.</summary>
        [ThreadStatic]
        public static string? LastReport;

        /// <summary>
        /// From the first object to the end of the last. osu! hands the map to the mods before its objects are made ready, when a slider's end is not known yet (it would seem endless, and the map with
        /// it): they are made ready here first, as osu! makes them after. False when the map has no length.
        /// </summary>
        private static bool spanOf(IBeatmap beatmap, out double start, out double end)
        {
            foreach (var hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            start = beatmap.HitObjects.Min(h => h.StartTime);
            end = beatmap.HitObjects.Max(h => double.IsFinite(h.GetEndTime()) ? h.GetEndTime() : h.StartTime);

            // (still nothing sensible: the last start)
            if (!double.IsFinite(end) || end - start > 6 * 3600_000)
                end = beatmap.HitObjects.Max(h => h.StartTime);

            return double.IsFinite(start) && double.IsFinite(end) && end > start;
        }

        private static void put(Beatmap<OsuHitObject> target, Made made)
        {
            target.HitObjects = made.Objects;

            // (an osu!standard map, whatever the map was: a mania, taiko or catch map was converted without stacking)
            target.StackLeniency = 0.7f;
            target.Difficulty.CircleSize = made.CircleSize;
            target.Difficulty.ApproachRate = made.ApproachRate;
            target.Difficulty.OverallDifficulty = made.OverallDifficulty;
            target.Difficulty.DrainRate = made.DrainRate;
            target.Difficulty.SliderMultiplier = made.SliderMultiplier;
        }

        /// <summary>The stars of the map as it is now (its objects made ready as the game makes them); NaN when it cannot be rated.</summary>
        private static double rated(IBeatmap beatmap)
        {
            try
            {
                // (as osu! makes a map ready: its processor too, which marks the last object of every combo - a ring of stars in this game - and stacks what lies on each other)
                var processor = new DanmakusuBeatmapProcessor(beatmap);
                processor.PreProcess();

                foreach (var hitObject in beatmap.HitObjects)
                    hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

                processor.PostProcess();
                return Bullets.StarRating.Rate(beatmap).Stars;
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>A map made like the maps of bucket <paramref name="source"/> (between two buckets: mixed; past the last: the last, closer together).</summary>
        private static Made? build(IBeatmap beatmap, MapModel model, double source, int stars, Bullets.SongBands? song, double start, double end, OwnSounds own)
        {
            int lastBucket = model.Buckets.Count > 0 ? model.Buckets.Max(b => b.Stars) : 10;

            // (past the last bucket its rhythm is drawn closer together; below the first, further apart: 1 star is easier than the easiest maps rated)
            double squeeze = source > lastBucket ? 1 / (1 + squeeze_per_step * (source - lastBucket)) : source < 1 ? 1 + stretch_per_step * (1 - source) : 1;
            source = Math.Clamp(source, 1, lastBucket);

            var low = model.Nearest((int)Math.Floor(source));
            var high = model.Nearest((int)Math.Ceiling(source));

            if (low == null || high == null)
                return null;

            float mix = (float)Math.Clamp(source - Math.Floor(source), 0, 1);
            var meta = beatmap.BeatmapInfo?.Metadata;
            var rng = new Rng(hash($"{meta?.Artist}|{meta?.Title}|{beatmap.BeatmapInfo?.DifficultyName}|{stars}"));

            // (the difficulty settings: the two buckets' mixed)
            float blend(float a, float b) => a + (b - a) * mix;
            var result = new Made
            {
                CircleSize = blend(low.CircleSize, high.CircleSize),
                ApproachRate = blend(low.ApproachRate, high.ApproachRate),
                OverallDifficulty = blend(low.OverallDifficulty, high.OverallDifficulty),
                DrainRate = blend(low.DrainRate, high.DrainRate),
                SliderMultiplier = low.SliderMultiplier + (high.SliderMultiplier - low.SliderMultiplier) * mix,
            };

            var difficulty = new BeatmapDifficulty { SliderMultiplier = result.SliderMultiplier };
            int volume = (int)Math.Round(blend(low.Volume, high.Volume));
            var made = result.Objects;
            var timing = beatmap.ControlPointInfo;
            var intensity = new Intensity(new Loudness(song, timing, start, end), timing, start, end);

            string? before = null, last = null; // (the two tokens before)
            int lastDuration = 0;
            double t = start;
            Vector2 cursor = middle + new Vector2(rng.Range(-60f, 60f), rng.Range(-40f, 40f));
            float heading = rng.Range(0f, MathF.Tau);
            var pattern = new Pattern();
            int comboCount = 0, comboMost = 0;
            bool first = true;
            int guard = 0;

            // (the rhythm bar by bar: the tokens of this bar and of the bar before, and what is left of a bar played again)
            var motif = new List<string>();
            var lastMotif = new List<string>();
            var replay = new Queue<string>();
            long bar = long.MinValue;
            int repeatsInRow = 0;

            // (as many as 25 a second at most, so that a marathon is filled to its end too)
            int mostObjects = Math.Max(20000, (int)((end - start) / 1000 * 25));

            while (t <= end && made.Count < mostObjects && guard++ < mostObjects * 5)
            {
                var bucket = rng.Next() < mix ? high : low;

                // (nothing in a break: the next object comes on the first beat after it, as if the map began again)
                var inBreak = beatmap.Breaks.FirstOrDefault(b => t >= b.StartTime - 1 && t < b.EndTime);

                if (inBreak != null)
                {
                    t = onBeatAfter(timing, inBreak.EndTime);
                    before = last = null;
                    lastDuration = 0;
                    first = true;
                    comboCount = 0;
                    motif.Clear();
                    lastMotif.Clear();
                    replay.Clear();
                    bar = long.MinValue;
                    continue;
                }

                // (a bar played again goes on with its own rhythm; otherwise the model's, listening to the song)
                bool replaying = replay.Count > 0;
                string token = replaying ? replay.Dequeue() : first ? pick(bucket, before, last, rng) : listen(bucket, before, last, rng, song, intensity, timing, t, lastDuration, squeeze);
                var (gap, kind, repeats, duration) = MapModel.Parse(token);

                // (past the last bucket: closer together; below the first: further apart)
                if (squeeze != 1)
                {
                    gap = Math.Max(1, (int)Math.Round(gap * squeeze));
                    duration = kind == MapModel.CIRCLE ? 0 : Math.Max(1, (int)Math.Round(duration * squeeze));
                }

                if (!first)
                {
                    // (never before the object before has ended)
                    if (gap < lastDuration + 1)
                        gap = lastDuration + Math.Max(1, MapModel.PARTS / 4);

                    t = advance(timing, t, gap);

                    if (t > end)
                        break;

                    if (beatmap.Breaks.Any(b => t >= b.StartTime - 1 && t < b.EndTime))
                        continue; // (it has gone into a break: see above)
                }

                first = false;

                // a new bar: maps play the rhythm of a bar again often (a song repeats itself), as the bar before, from its first object on - this object is the bar before's first, on its own way here
                long barNow = barOf(timing, t);
                bool newBar = barNow != bar;

                if (newBar)
                {
                    bar = barNow;
                    lastMotif.Clear();
                    lastMotif.AddRange(motif);
                    motif.Clear();
                    replay.Clear();

                    // (as often as the model's maps play a bar again, and again after that)
                    if (lastMotif.Count >= 2 && rng.Next() < (repeatsInRow == 0 ? bucket.BarRepeat : bucket.BarRepeatAgain))
                    {
                        var (_, firstKind, firstRepeats, firstDuration) = MapModel.Parse(lastMotif[0]);
                        token = MapModel.Token(gap, firstKind, firstRepeats, firstDuration);
                        (kind, repeats, duration) = (firstKind, firstRepeats, squeeze != 1 && firstKind != MapModel.CIRCLE ? Math.Max(1, (int)Math.Round(firstDuration * squeeze)) : firstDuration);

                        for (int k = 1; k < lastMotif.Count; k++)
                            replay.Enqueue(lastMotif[k]);

                        repeatsInRow++;
                    }
                    else
                        repeatsInRow = 0;
                }

                double beat = MapModel.BeatAt(timing, t);
                double length = duration * beat / MapModel.PARTS;

                if (kind != MapModel.CIRCLE && (length < beat / 8 || t + length > end + beat))
                {
                    kind = MapModel.CIRCLE; // (too short to be one, or past the end of the song)
                    duration = 0;
                }

                // a new combo on the first beat of a bar (as often as the model's maps start one there, when the combo has a few), or when it is as long as the model's, or for a spinner; a new combo is
                // a new shape (see Pattern)
                bool newCombo = kind == MapModel.SPINNER || comboCount == 0 || comboCount >= comboMost || (comboCount >= 2 && onBarStart(timing, t) && rng.Next() < bucket.ComboOnDownbeat);

                if (newCombo)
                {
                    comboCount = 0;
                    comboMost = Math.Max(sampleCombo(bucket, rng), 2);
                    pattern.Start(bucket, rng);
                }

                comboCount++;

                // where it starts: as far from the cursor as this combo jumps for this rhythm, turned as this combo turns - the rhythm counted from the end of the object before, as mappers space
                // (after a circle the same as from its start, which is how the model counted)
                int gapClass = MapModel.GapClass(Math.Max(gap - lastDuration, 1));
                float loud = intensity.At(t);
                Vector2 at = kind == MapModel.SPINNER ? middle : place(bucket, gapClass, cursor, ref heading, rng, 0.8f + 0.4f * loud, pattern);

                float strength = song?.HitNear(t, on_hit_ms) ?? 0f;
                var samples = sounds(own, bucket, timing, t, volume, strength);
                OsuHitObject hitObject;

                switch (kind)
                {
                    case MapModel.SPINNER:
                        hitObject = new Spinner { StartTime = t, EndTime = t + Math.Max(length, beat), Position = middle, Samples = sounds(own, bucket, timing, t + Math.Max(length, beat), volume, 0f), NewCombo = true };
                        cursor = middle;
                        comboCount = 0;
                        break;

                    case MapModel.SLIDER:
                    {
                        // (its path is as long as the slider's own speed needs for that time: osu!'s speed goes by the timing point's real beat)
                        var slider = slide(bucket, difficulty, at, heading, length, repeats, rng, Math.Max(timing.TimingPointAt(t).BeatLength, 1));
                        slider.StartTime = t;
                        slider.NewCombo = newCombo;
                        slider.Samples = samples;
                        var nodes = new List<IList<HitSampleInfo>> { samples };
                        double spanTime = length / (repeats + 1);

                        // (every repeat and the tail: the hitsounds of their own moments)
                        for (int n = 1; n <= repeats + 1; n++)
                            nodes.Add(sounds(own, bucket, timing, t + spanTime * n, volume, song?.HitNear(t + spanTime * n, on_hit_ms) ?? 0f));

                        slider.NodeSamples = nodes;
                        hitObject = slider;

                        Vector2 tip = at + slider.Path.PositionAt(1);
                        cursor = repeats % 2 == 0 ? tip : at;

                        if ((tip - at).LengthSquared > 1f)
                            heading = MathF.Atan2(tip.Y - at.Y, tip.X - at.X) + (repeats % 2 == 0 ? 0f : MathF.PI);

                        break;
                    }

                    default:
                        hitObject = new HitCircle { StartTime = t, Position = at, NewCombo = newCombo, Samples = samples };
                        cursor = at;
                        duration = 0;
                        break;
                }

                made.Add(hitObject);
                motif.Add(token);
                before = last;
                last = token; // (the model's own token: what follows is what follows it in the maps)
                lastDuration = kind == MapModel.CIRCLE ? 0 : duration;
            }

            return made.Count == 0 ? null : result;
        }

        // ---- rhythm ----

        /// <summary>
        /// The next object, chosen by the song out of a few the model offers after the two before (each offered as often as the model has it): one that lands on a drum hit is better, and so is a short gap where
        /// the song is loud and a long one where it is quiet; a little chance keeps it from always being the same. Without the song, the model's alone.
        /// </summary>
        private static string listen(MapModel.Bucket bucket, string? before, string? last, Rng rng, Bullets.SongBands? song, Intensity intensity, ControlPointInfo timing, double t, int lastDuration, double squeeze)
        {
            if (song == null && !intensity.Varies)
                return pick(bucket, before, last, rng);

            string best = string.Empty;
            double bestScore = double.NegativeInfinity;

            for (int c = 0; c < candidates; c++)
            {
                string token = pick(bucket, before, last, rng);
                var (gap, _, _, _) = MapModel.Parse(token);
                gap = Math.Max(1, (int)Math.Round(gap * squeeze));
                double at = advance(timing, t, Math.Max(gap, lastDuration + 1));
                float hit = song?.HitNear(at, on_hit_ms) ?? 0f;
                float loud = intensity.At(at);

                // (dense where it is loud, sparse where it is quiet: a quarter beat or less counts as dense, more than a beat as sparse)
                float denseness = gap <= MapModel.PARTS / 4 ? 1f : gap <= MapModel.PARTS / 2 ? 0.5f : gap <= MapModel.PARTS ? 0f : -1f;
                double score = (hit > 0f ? 1.0 + Math.Min(hit, 4f) * 0.1 : 0.0) + (loud - 0.5f) * denseness * 1.2 + rng.Next() * 0.35;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = token;
                }
            }

            return best;
        }

        /// <summary>
        /// How busy the map should be at a moment (0..1): the song's loudness as against the rest of it (<see cref="Loudness"/>), at least <see cref="kiai_least"/> in the map's kiai, at most
        /// <see cref="intro_most"/> in its intro - up to its first kiai, or (without one) up to where the song first gets loud, a third of the map at most, or 15 % of it without the song.
        /// </summary>
        private sealed class Intensity
        {
            private readonly Loudness loudness;
            private readonly List<(double Start, double End)> kiai = new List<(double, double)>();
            private readonly double introEnd;

            /// <summary>Is there anything to go by (the song, a kiai, an intro)?</summary>
            public bool Varies { get; }

            public Intensity(Loudness loudness, ControlPointInfo timing, double start, double end)
            {
                this.loudness = loudness;

                double kiaiFrom = double.NaN;

                foreach (var point in timing.EffectPoints)
                {
                    if (point.KiaiMode && double.IsNaN(kiaiFrom))
                        kiaiFrom = point.Time;
                    else if (!point.KiaiMode && !double.IsNaN(kiaiFrom))
                    {
                        kiai.Add((kiaiFrom, point.Time));
                        kiaiFrom = double.NaN;
                    }
                }

                if (!double.IsNaN(kiaiFrom))
                    kiai.Add((kiaiFrom, double.PositiveInfinity));

                double longest = start + (end - start) / 3;

                if (kiai.Count > 0 && kiai[0].Start > start)
                    introEnd = Math.Min(kiai[0].Start, longest);
                else if (loudness.Heard)
                {
                    double step = Math.Max(timing.TimingPointAt(start).BeatLength, 100);
                    introEnd = start;

                    while (introEnd < longest && loudness.At(introEnd) < loud_from)
                        introEnd += step;
                }
                else
                    introEnd = start + (end - start) * 0.15;

                Varies = loudness.Heard || kiai.Count > 0 || introEnd > start;
            }

            public float At(double t)
            {
                float level = loudness.At(t);

                foreach (var (from, to) in kiai)
                {
                    if (t >= from && t < to)
                        return Math.Max(level, kiai_least);
                }

                return t < introEnd ? Math.Min(level, intro_most) : level;
            }
        }

        /// <summary>(in a kiai the map is at least this busy; in the intro at most this; the intro is over where the song is this loud as against the rest)</summary>
        private const float kiai_least = 0.8f, intro_most = 0.3f, loud_from = 0.5f;

        /// <summary>How loud the song is at a moment as against the rest of it (0 = the quietest beat, 1 = the loudest; 0.5 everywhere without the song): its loudness beat by beat, ranked.</summary>
        private sealed class Loudness
        {
            /// <summary>Is there a song to go by?</summary>
            public bool Heard => rank.Length > 0;

            private readonly double start, step;
            private readonly float[] rank = [];

            public Loudness(Bullets.SongBands? song, ControlPointInfo timing, double start, double end)
            {
                this.start = start;
                step = Math.Max(timing.TimingPointAt(start).BeatLength, 100);

                if (song == null || end <= start)
                    return;

                int count = Math.Clamp((int)((end - start) / step) + 1, 1, 20000);
                var levels = new float[count];

                for (int i = 0; i < count; i++)
                {
                    double from = start + (i - 1) * step, to = start + (i + 1) * step; // (two beats round it)
                    levels[i] = song.Loudness(Math.Max(from, 0), to);
                }

                var order = Enumerable.Range(0, count).OrderBy(i => levels[i]).ThenBy(i => i).ToArray();
                rank = new float[count];

                for (int r = 0; r < count; r++)
                    rank[order[r]] = count > 1 ? r / (float)(count - 1) : 0.5f;
            }

            public float At(double t)
            {
                if (rank.Length == 0)
                    return 0.5f;

                return rank[Math.Clamp((int)Math.Round((t - start) / step), 0, rank.Length - 1)];
            }
        }

        /// <summary>The next object as the model has it after the two before (or one, or none: what it has seen most).</summary>
        private static string pick(MapModel.Bucket bucket, string? before, string? last, Rng rng)
        {
            if (before != null && last != null && bucket.AfterTwo.TryGetValue(before + "|" + last, out var two) && two.Count > 0 && sum(two) >= 3)
                return draw(two, rng);

            if (last != null && bucket.AfterOne.TryGetValue(last, out var one) && one.Count > 0)
                return draw(one, rng);

            return draw(bucket.First, rng);
        }

        private static int sum(Dictionary<string, int> counts)
        {
            int all = 0;

            foreach (int count in counts.Values)
                all += count;

            return all;
        }

        private static string draw(Dictionary<string, int> counts, Rng rng)
        {
            int all = sum(counts);

            if (all <= 0)
                return MapModel.Token(MapModel.PARTS, MapModel.CIRCLE, 0, 0);

            double at = rng.Next() * all;

            // (in a fixed order: a dictionary keeps the order it was filled in, which is the same every time it is read)
            foreach (var (token, count) in counts)
            {
                at -= count;

                if (at < 0)
                    return token;
            }

            return counts.Keys.Last();
        }

        /// <summary><paramref name="parts"/> twelfths of a beat after <paramref name="t"/>; a new timing point on the way starts the beat grid again there.</summary>
        private static double advance(ControlPointInfo timing, double t, int parts)
        {
            double next = t + parts * MapModel.BeatAt(timing, t) / MapModel.PARTS;

            foreach (var point in timing.TimingPoints)
            {
                if (point.Time > t + 1 && point.Time < next - 1)
                    return point.Time;
            }

            return next;
        }

        /// <summary>The first whole beat at or after <paramref name="t"/>.</summary>
        private static double onBeatAfter(ControlPointInfo timing, double t)
        {
            var point = timing.TimingPointAt(t);
            double beat = Math.Max(point.BeatLength, 1);
            return point.Time + Math.Ceiling((t - point.Time) / beat - 1e-6) * beat;
        }

        // ---- where things go ----

        private static Vector2 way(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        private static bool inside(Vector2 p) => p.X >= margin && p.X <= size.X - margin && p.Y >= margin && p.Y <= size.Y - margin;

        private static int drawBin(int[] counts, Rng rng)
        {
            long all = 0;

            foreach (int count in counts)
                all += count;

            if (all <= 0)
                return 0;

            double at = rng.Next() * all;

            for (int i = 0; i < counts.Length; i++)
            {
                at -= counts[i];

                if (at < 0)
                    return i;
            }

            return counts.Length - 1;
        }

        /// <summary>The bar <paramref name="t"/> is in (of its timing point: a new timing point starts the bars again).</summary>
        private static long barOf(ControlPointInfo timing, double t)
        {
            var point = timing.TimingPointAt(t);
            double length = Math.Max(point.BeatLength, 1) * Math.Max(point.TimeSignature.Numerator, 1);
            return (long)Math.Round(point.Time) * 1_000_000 + (long)Math.Floor((t - point.Time) / length + 1e-6);
        }

        /// <summary>Is <paramref name="t"/> on the first beat of a bar?</summary>
        private static bool onBarStart(ControlPointInfo timing, double t)
        {
            var point = timing.TimingPointAt(t);
            double beats = (t - point.Time) / Math.Max(point.BeatLength, 1);
            int meter = Math.Max(point.TimeSignature.Numerator, 1);
            return Math.Abs(beats - Math.Round(beats)) < 1.0 / 24 && ((long)Math.Round(beats) % meter + meter) % meter == 0;
        }

        /// <summary>
        /// The shape of a combo, as mapped combos have one: every jump of the same rhythm in it is as long (spacing that goes with the rhythm, drawn once per rhythm from the model's), and turns as sharply, the same
        /// way round (a triangle, a square, a curve of a stream) or the other way every time (a zigzag), as often as the model's maps turn the same way.
        /// </summary>
        private sealed class Pattern
        {
            public readonly Dictionary<int, float> Distance = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Turn = new Dictionary<int, float>();
            public bool Zigzag;
            public float Side = 1f;

            public void Start(MapModel.Bucket bucket, Rng rng)
            {
                Distance.Clear();
                Turn.Clear();
                Zigzag = rng.Next() < (bucket.Zigzag >= 0 ? bucket.Zigzag : 1 - bucket.SameWay); // (as many combos zigzag as in the model's maps)
                Side = rng.Next() < 0.5 ? 1f : -1f;
            }
        }

        /// <summary>
        /// Where the next object starts: a jump from the cursor as long as this combo's for this rhythm, turned from the way before as this combo turns; inside the playfield (the other way round, then any turn
        /// the model has, else bounced in).
        /// </summary>
        private static Vector2 place(MapModel.Bucket bucket, int gapClass, Vector2 cursor, ref float heading, Rng rng, float stretch, Pattern pattern)
        {
            var distances = gapClass < bucket.Distance.Length ? bucket.Distance[gapClass] : [];
            var turns = gapClass < bucket.Turn.Length ? bucket.Turn[gapClass] : [];

            // (a stack stays a stack; the other jumps are longer where the song is loud: stretch)
            if (!pattern.Distance.TryGetValue(gapClass, out float distance))
            {
                distance = distances.Length > 0 ? (drawBin(distances, rng) + (float)rng.Next()) * MapModel.DISTANCE_BIN : 100f;

                if (distance >= MapModel.DISTANCE_BIN)
                    distance *= stretch;

                pattern.Distance[gapClass] = distance;
            }

            if (!pattern.Turn.TryGetValue(gapClass, out float shapeTurn))
            {
                shapeTurn = (turns.Length > 0 ? (drawBin(turns, rng) + (float)rng.Next()) * MapModel.TURN_BIN_DEGREES : rng.Range(0f, 180f)) * MathF.PI / 180f;
                pattern.Turn[gapClass] = shapeTurn;
            }

            if (pattern.Zigzag)
                pattern.Side = -pattern.Side;

            Vector2 low = new Vector2(margin), high = size - new Vector2(margin);

            if (distance < 1f)
                return Vector2.Clamp(cursor, low, high); // (a stack)

            // (by hand, never quite the same: as much as the model's maps differ in a combo - an even spread with the same root mean square - in length and in turn)
            float jitter = (float)bucket.SpacingJitter * 1.732f;
            distance *= 1f + rng.Range(-jitter, jitter);

            float turnJitter = (float)bucket.TurnSpread * 1.732f;
            shapeTurn = Math.Clamp(shapeTurn + rng.Range(-turnJitter, turnJitter) * MathF.PI / 180f, 0f, MathF.PI);

            // (the combo's own turn, the same way round and the other; then the turn a little more or less, so that the shape turns a little but the jump stays as long: spacing is what shows)
            for (int attempt = 0; attempt < shape_tries.Length * 2; attempt++)
            {
                float turn = shapeTurn + shape_tries[attempt / 2] * MathF.PI / 180f;
                float side = attempt % 2 == 0 ? pattern.Side : -pattern.Side;
                float angle = heading + turn * side;
                Vector2 at = cursor + way(angle) * distance;

                if (!inside(at))
                    continue;

                heading = angle;

                // (turned the other way to stay in: the combo goes on that way round)
                if (side != pattern.Side && !pattern.Zigzag)
                    pattern.Side = side;

                return Vector2.Clamp(at, low, high);
            }

            // (nothing of the shape fits: as long a jump towards the middle, or as far as the playfield goes that way)
            float toMiddle = MathF.Atan2(middle.Y - cursor.Y, middle.X - cursor.X);
            Vector2 aim = way(toMiddle);
            float room = distance;

            if (aim.X > 1e-4f) room = Math.Min(room, (high.X - cursor.X) / aim.X);
            if (aim.X < -1e-4f) room = Math.Min(room, (low.X - cursor.X) / aim.X);
            if (aim.Y > 1e-4f) room = Math.Min(room, (high.Y - cursor.Y) / aim.Y);
            if (aim.Y < -1e-4f) room = Math.Min(room, (low.Y - cursor.Y) / aim.Y);

            heading = toMiddle;
            return Vector2.Clamp(cursor + aim * Math.Max(room, 0f), low, high);
        }

        /// <summary>(degrees added to a combo's turn, one after the other, when its own does not fit in the playfield)</summary>
        private static readonly float[] shape_tries = { 0f, 15f, -15f, 30f, -30f, 45f, -45f };

        /// <summary>
        /// A slider from <paramref name="at"/>, lasting <paramref name="length"/> ms with <paramref name="repeats"/> repeats at the model's slider speed: a straight line or a curve (as often as the model curves them),
        /// set out somewhere near the way the cursor was going so that it stays in the playfield; when none fits, a line that bounces off its edges.
        /// </summary>
        private static Slider slide(MapModel.Bucket bucket, BeatmapDifficulty difficulty, Vector2 at, float heading, double length, int repeats, Rng rng, double beat)
        {
            double span = length / (repeats + 1);
            float distance = (float)Math.Max(100 * difficulty.SliderMultiplier * span / beat, 8);
            bool curved = rng.Next() < bucket.Curved;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = heading + rng.Range(-1.2f, 1.2f) + (attempt >= 8 ? MathF.PI : 0f);
                Vector2 dir = way(angle);

                if (curved)
                {
                    // (as far as the model's curved sliders bend from the line between their ends: a quadratic curve bends half as far as its middle point is pulled)
                    float bent = bucket.Bend.Length > 0 && bucket.Bend.Sum() > 0 ? (drawBin(bucket.Bend, rng) + (float)rng.Next()) * (float)MapModel.BEND_BIN : rng.Range(0.075f, 0.2f);
                    float bend = Math.Clamp(bent * 2f, 0.05f, 0.8f) * (rng.Next() < 0.5 ? 1f : -1f);
                    float chord = distance / MathF.Sqrt(1f + 16f / 3f * bend * bend);
                    Vector2 tip = dir * chord;
                    Vector2 control = tip * 0.5f + new Vector2(-dir.Y, dir.X) * (chord * bend);

                    if (inside(at + tip) && inside(at + control))
                        return made(new[] { new PathControlPoint(Vector2.Zero, PathType.BEZIER), new PathControlPoint(control), new PathControlPoint(tip) }, distance, repeats);
                }
                else if (inside(at + dir * distance))
                    return made(new[] { new PathControlPoint(Vector2.Zero, PathType.LINEAR), new PathControlPoint(dir * distance) }, distance, repeats);
            }

            // (too long for the room there is: it bounces off the edges of the playfield)
            var points = new List<PathControlPoint> { new PathControlPoint(Vector2.Zero, PathType.LINEAR) };
            Vector2 position = at, d = way(heading);
            float left = distance;
            Vector2 lowEdge = new Vector2(margin), highEdge = size - new Vector2(margin);

            for (int i = 0; i < 32 && left > 0.5f; i++)
            {
                float step = left;

                if (d.X > 1e-4f) step = Math.Min(step, (highEdge.X - position.X) / d.X);
                if (d.X < -1e-4f) step = Math.Min(step, (lowEdge.X - position.X) / d.X);
                if (d.Y > 1e-4f) step = Math.Min(step, (highEdge.Y - position.Y) / d.Y);
                if (d.Y < -1e-4f) step = Math.Min(step, (lowEdge.Y - position.Y) / d.Y);

                step = Math.Max(step, 0f);
                position += d * step;
                left -= step;
                points.Add(new PathControlPoint(position - at));

                if (position.X <= lowEdge.X + 0.01f || position.X >= highEdge.X - 0.01f)
                    d.X = -d.X;

                if (position.Y <= lowEdge.Y + 0.01f || position.Y >= highEdge.Y - 0.01f)
                    d.Y = -d.Y;

                if (step < 0.01f)
                    position += d * 0.5f;
            }

            return made(points.ToArray(), distance, repeats);

            Slider made(PathControlPoint[] controlPoints, float pathLength, int repeatCount) => new Slider
            {
                Position = at,
                Path = new SliderPath(controlPoints, pathLength),
                RepeatCount = repeatCount,
                SliderVelocityMultiplier = 1,
                TickDistanceMultiplier = 1,
            };
        }

        // ---- sounds and combos ----

        private static List<HitSampleInfo> plain(int volume, string bank = HitSampleInfo.BANK_NORMAL) => new List<HitSampleInfo> { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, bank, volume: volume) };

        /// <summary>
        /// The map's own hitsounds: the sounds of every object (and of every repeat and tail of its sliders) at its moment. A generated object takes the ones the mapper put on its moment - they go with the song's
        /// drums - and where the map has nothing, the sampleset and volume of the nearest.
        /// </summary>
        private sealed class OwnSounds
        {
            private readonly double[] times;
            private readonly IList<HitSampleInfo>[] samples;

            public OwnSounds(IBeatmap beatmap)
            {
                var found = new List<(double Time, IList<HitSampleInfo> Samples)>();

                foreach (var hitObject in beatmap.HitObjects)
                {
                    if (hitObject is Slider { NodeSamples.Count: > 0 } slider && double.IsFinite(slider.SpanDuration))
                    {
                        for (int n = 0; n < slider.NodeSamples.Count; n++)
                            found.Add((slider.StartTime + n * slider.SpanDuration, slider.NodeSamples[n]));
                    }
                    else if (hitObject.Samples.Count > 0)
                        found.Add((hitObject is Spinner ? hitObject.GetEndTime() : hitObject.StartTime, hitObject.Samples));
                }

                found.Sort((a, b) => a.Time.CompareTo(b.Time));
                times = found.Select(f => f.Time).ToArray();
                samples = found.Select(f => f.Samples).ToArray();
            }

            private int nearest(double t)
            {
                int i = Array.BinarySearch(times, t);

                if (i >= 0)
                    return i;

                i = ~i;

                if (i == 0)
                    return times.Length > 0 ? 0 : -1;

                if (i >= times.Length)
                    return times.Length - 1;

                return t - times[i - 1] <= times[i] - t ? i - 1 : i;
            }

            /// <summary>The sounds of the map's own object within <paramref name="within"/> ms of <paramref name="t"/>, if there is one.</summary>
            public IList<HitSampleInfo>? At(double t, double within)
            {
                int i = nearest(t);
                return i >= 0 && Math.Abs(times[i] - t) <= within ? samples[i] : null;
            }

            /// <summary>The sounds of the map's own object nearest <paramref name="t"/> (for its sampleset and volume).</summary>
            public IList<HitSampleInfo>? Nearest(double t)
            {
                int i = nearest(t);
                return i >= 0 ? samples[i] : null;
            }
        }

        /// <summary>
        /// The hitsounds of an object at <paramref name="t"/>: those of the map's own object at that moment, if it has one. Otherwise its sampleset and volume from the nearest, and the additions on fixed places of
        /// the bar, as the model's maps have them there most (not drawn at random every time: a mapped section sounds the same bar after bar): a clap on the backbeat, a whistle on the first beat of a bar, a
        /// finish on the first beat of every fourth bar and on a strong drum hit (<paramref name="strength"/>), each where the model's maps have it often enough.
        /// </summary>
        private static List<HitSampleInfo> sounds(OwnSounds own, MapModel.Bucket bucket, ControlPointInfo timing, double t, int volume, float strength)
        {
            var point = timing.TimingPointAt(t);
            double beatLength = Math.Max(point.BeatLength, 1);
            var mapped = own.At(t, Math.Min(beatLength / 8, 40));

            if (mapped != null)
            {
                var copy = new List<HitSampleInfo>(mapped);

                if (!copy.Any(s => s.Name == HitSampleInfo.HIT_NORMAL))
                    copy.Insert(0, new HitSampleInfo(HitSampleInfo.HIT_NORMAL, copy.Count > 0 ? copy[0].Bank : HitSampleInfo.BANK_NORMAL, volume: copy.Count > 0 ? copy[0].Volume : volume));

                return copy;
            }

            var near = own.Nearest(t)?.FirstOrDefault();
            string bank = near?.Bank ?? HitSampleInfo.BANK_NORMAL;
            int loudness = near != null && near.Volume > 0 ? near.Volume : volume;
            var list = plain(loudness, bank);

            double beats = (t - point.Time) / beatLength;
            bool onBeat = Math.Abs(beats - Math.Round(beats)) < 1.0 / 24;
            int meter = Math.Max(point.TimeSignature.Numerator, 1);
            int beatInBar = onBeat ? (int)(((long)Math.Round(beats) % meter + meter) % meter) : -1;
            long barNumber = (long)Math.Floor(Math.Round(beats, 3) / meter);
            int where = beatInBar < 0 ? 2 : beatInBar == 0 ? 0 : 1;

            if (where >= bucket.Sounds.Length || bucket.Sounds[where].Length < 3)
                return list;

            double whistle = bucket.Sounds[where][0], clap = bucket.Sounds[where][1], finish = bucket.Sounds[where][2];

            void add(string name) => list.Add(new HitSampleInfo(name, bank, volume: loudness));

            if (where == 0 && whistle >= sound_often)
                add(HitSampleInfo.HIT_WHISTLE);

            if (where == 1 && beatInBar % 2 == 1 && clap >= sound_often / 2)
                add(HitSampleInfo.HIT_CLAP);

            if ((where == 0 && barNumber % 4 == 0 && finish >= sound_often / 4) || (where <= 1 && strength >= 2.5f && finish > 0.02))
                add(HitSampleInfo.HIT_FINISH);

            return list;
        }

        /// <summary>(an addition is put on its fixed place when the model's maps have it there at least about this often)</summary>
        private const double sound_often = 0.3;

        private static int sampleCombo(MapModel.Bucket bucket, Rng rng) => bucket.Combo.Length > 0 ? drawBin(bucket.Combo, rng) + 1 : 4;

        // ---- the same every time ----

        private static ulong hash(string text)
        {
            ulong h = 14695981039346656037UL;

            foreach (char c in text)
            {
                h ^= c;
                h *= 1099511628211UL;
            }

            return h == 0 ? 1 : h;
        }

        /// <summary>A random number generator that gives the same numbers on every machine and every version of .NET (xorshift64*).</summary>
        private sealed class Rng
        {
            private ulong state;

            public Rng(ulong seed)
            {
                state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            }

            public double Next()
            {
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                return ((state * 2685821657736338717UL) >> 11) / (double)(1UL << 53);
            }

            public float Range(float from, float to) => from + (to - from) * (float)Next();
        }
    }
}
