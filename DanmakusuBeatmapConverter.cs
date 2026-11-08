using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// osu!'s own converter for osu! maps; maps of the other modes are made into circles, sliders and spinners of their own (see <see cref="ConvertedModes"/> for what each becomes and where it stands).
    /// </summary>
    public class DanmakusuBeatmapConverter : OsuBeatmapConverter
    {
        public DanmakusuBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        private int mode;
        private int keys = DanmakusuBalance.CONVERT_TAIKO_KEYS;
        private float ringRadius;
        private int dons, kats; // (taiko: how many of each so far: they take turns on their two places)
        private float fallSpeed; // (catch, px per ms)

        /// <summary>(song select shows its bullets, beams, spinners and most bosses instead of its circles and sliders: <see cref="Stars.DanmakusuBeatmap"/>)</summary>
        protected override Beatmap<OsuHitObject> CreateBeatmap() => new Stars.DanmakusuBeatmap();

        protected override Beatmap<OsuHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
        {
            mode = ConvertedModes.SourceMode(original);
            dons = kats = 0;

            if (mode == ConvertedModes.MANIA)
                keys = Math.Clamp((int)Math.Round(original.Difficulty.CircleSize), 1, 18); // (a mania map's circle size is its key count)
            else if (mode == ConvertedModes.TAIKO)
                keys = DanmakusuBalance.CONVERT_TAIKO_KEYS;

            ringRadius = ConvertedModes.RingRadius(keys);
            fallSpeed = DanmakusuBalance.CATCH_FALL_HEIGHT / (float)Math.Max(DanmakusuBalance.ApproachPreempt(original.Difficulty.ApproachRate), 1); // (from wall to wall in its approach time)

            var beatmap = base.ConvertBeatmap(original, cancellationToken);

            if (mode != ConvertedModes.STANDARD)
            {
                // (the objects of these modes stand where they stand: none is moved by osu!'s stacking, which would push one note of a column after the other further away)
                beatmap.StackLeniency = 0;

                if (mode is ConvertedModes.MANIA or ConvertedModes.TAIKO)
                    beatmap.Difficulty.CircleSize = DanmakusuBalance.CONVERT_CS; // (a mania map's is its key count, a taiko map's means nothing)
            }

            return beatmap;
        }

        protected override IEnumerable<OsuHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            switch (mode)
            {
                case ConvertedModes.MANIA:
                    return convertMania(original, beatmap);

                case ConvertedModes.TAIKO:
                    return convertTaiko(original, beatmap, cancellationToken);

                case ConvertedModes.CATCH:
                    return convertCatch(original, beatmap, cancellationToken);

                default:
                    return base.ConvertHitObject(original, beatmap, cancellationToken);
            }
        }

        private static readonly Vector2 middle = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>(osu!'s: the px a slider goes in a beat at a slider multiplier and velocity of 1)</summary>
        private const double base_scoring_distance = 100;

        private ConvertedModes.Info ringInfo(int column, double time, IBeatmap beatmap) => new ConvertedModes.Info
        {
            Mode = mode,
            Way = ConvertedModes.RingWay(column, keys, ConvertedModes.BeatsAt(beatmap, time)),
            Radius = ringRadius,
        };

        // ---- osu!mania ----

        private IEnumerable<OsuHitObject> convertMania(HitObject original, IBeatmap beatmap)
        {
            float x = (original as IHasXPosition)?.X ?? 0f;
            int column = Math.Clamp((int)Math.Floor(x * keys / 512f), 0, keys - 1);
            var info = ringInfo(column, original.StartTime, beatmap);
            Vector2 at = middle + info.Way * info.Radius;

            // a long note: a slider that does not move, with a whistle on its head
            if (original is IHasDuration hold && original is not IHasPath && hold.Duration > 0)
            {
                info.Static = true;
                return made(staticSlider(original, at, hold.Duration, beatmap), info);
            }

            return made(circle(original, at), info);
        }

        // ---- osu!taiko ----

        private IEnumerable<OsuHitObject> convertTaiko(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            var samples = original.Samples;

            // a swell is a spinner, as osu! makes it
            if (original is IHasDuration && original is not IHasPath)
                return base.ConvertHitObject(original, beatmap, cancellationToken);

            bool kat = samples.Any(s => s.Name is HitSampleInfo.HIT_WHISTLE or HitSampleInfo.HIT_CLAP);
            bool big = samples.Any(s => s.Name == HitSampleInfo.HIT_FINISH);
            bool roll = original is IHasPathWithRepeats;

            // (kat, don, don, kat: the dons and the drumrolls take turns on the middle two, the kats on the ends)
            int column = kat && !roll ? (kats++ % 2 == 0 ? 0 : 3) : 1 + dons++ % 2;
            var info = ringInfo(column, original.StartTime, beatmap);
            info.Big = big && !roll;
            Vector2 at = middle + info.Way * info.Radius;

            if (roll)
            {
                double duration = sliderDuration(original, beatmap, cancellationToken);

                if (duration > 0)
                {
                    info.Static = true;
                    return made(staticSlider(original, at, duration, beatmap), info);
                }
            }

            return made(circle(original, at), info);
        }

        // ---- osu!catch ----

        private IEnumerable<OsuHitObject> convertCatch(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            // a banana shower is a spinner, as osu! makes it
            if (original is IHasDuration && original is not IHasPath)
                return base.ConvertHitObject(original, beatmap, cancellationToken);

            float x = (original as IHasXPosition)?.X ?? 256f;
            var info = new ConvertedModes.Info { Mode = mode, FallSpeed = fallSpeed };

            if (original is IHasPathWithRepeats && base.ConvertHitObject(original, beatmap, cancellationToken).FirstOrDefault() is Slider stream)
            {
                var trail = trailOf(stream, beatmap);

                if (trail != null)
                    return made(trail, info);
            }

            return made(circle(original, new Vector2(x, DanmakusuBalance.CATCH_BOTTOM)), info);
        }

        /// <summary>
        /// A juice stream as a slider that falls: its trail, every point of it as far above the bottom as the stream is later there, so that, falling evenly, every point lands when the stream is there (the
        /// repeats are part of the one path). As fast along its path as it has to be to last as long as the stream.
        /// </summary>
        private Slider? trailOf(Slider stream, IBeatmap beatmap)
        {
            stream.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            double duration = stream.Duration;

            if (duration <= 0)
                return null;

            int steps = Math.Clamp((int)Math.Ceiling(duration / DanmakusuBalance.CATCH_TRAIL_STEP_MS), 2, 600);
            var points = new PathControlPoint[steps + 1];
            double length = 0;
            Vector2 previous = Vector2.Zero;

            for (int i = 0; i <= steps; i++)
            {
                double progress = i / (double)steps;
                Vector2 point = new Vector2(stream.CurvePositionAt(progress).X, -(float)(duration * progress) * fallSpeed);

                if (i > 0)
                    length += Vector2.Distance(previous, point);

                points[i] = new PathControlPoint(point, i == 0 ? PathType.LINEAR : null);
                previous = point;
            }

            var timing = beatmap.ControlPointInfo.TimingPointAt(stream.StartTime);
            double plain = base_scoring_distance * beatmap.Difficulty.SliderMultiplier / Math.Max(timing.BeatLength, 1); // (px per ms at a slider velocity of 1)
            var nodes = stream.NodeSamples.Count > 0 ? new List<IList<HitSampleInfo>> { stream.NodeSamples[0], stream.NodeSamples[^1] } : new List<IList<HitSampleInfo>>();

            return new Slider
            {
                StartTime = stream.StartTime,
                Position = new Vector2(stream.Position.X, DanmakusuBalance.CATCH_BOTTOM),
                NewCombo = stream.NewCombo,
                ComboOffset = stream.ComboOffset,
                Samples = stream.Samples,
                NodeSamples = nodes,
                RepeatCount = 0,
                Path = new SliderPath(points, length),
                SliderVelocityMultiplier = Math.Clamp(length / duration / plain, 0.1, 10),
                TickDistanceMultiplier = stream.TickDistanceMultiplier,
                GenerateTicks = stream.GenerateTicks,
            };
        }

        // ---- the parts ----

        private static IEnumerable<OsuHitObject> made(OsuHitObject hitObject, ConvertedModes.Info info)
        {
            ConvertedModes.Set(hitObject, info);
            yield return hitObject;
        }

        private static HitCircle circle(HitObject original, Vector2 at) => new HitCircle
        {
            StartTime = original.StartTime,
            Samples = original.Samples,
            Position = at,
            NewCombo = (original as IHasCombo)?.NewCombo ?? false,
            ComboOffset = (original as IHasCombo)?.ComboOffset ?? 0,
        };

        /// <summary>
        /// A slider that stays where it is, as long as <paramref name="duration"/>: its path only goes back and forth by a pixel, as long as the note at the slowest slider velocity osu! allows. Its head has a
        /// whistle (its beams: see BulletPlanner.addLaser).
        /// </summary>
        private static Slider staticSlider(HitObject original, Vector2 at, double duration, IBeatmap beatmap)
        {
            var samples = original.Samples.ToList();
            var basis = samples.FirstOrDefault() ?? new HitSampleInfo(HitSampleInfo.HIT_NORMAL);
            var head = samples.Where(s => s.Name != HitSampleInfo.HIT_WHISTLE).Append(basis.With(newName: HitSampleInfo.HIT_WHISTLE)).ToList();

            var slider = new Slider
            {
                StartTime = original.StartTime,
                Position = at,
                NewCombo = (original as IHasCombo)?.NewCombo ?? false,
                ComboOffset = (original as IHasCombo)?.ComboOffset ?? 0,
                Samples = samples,
                SliderVelocityMultiplier = DanmakusuBalance.CONVERT_HOLD_VELOCITY,
            };

            var timing = beatmap.ControlPointInfo.TimingPointAt(original.StartTime);
            double velocity = base_scoring_distance * beatmap.Difficulty.SliderMultiplier / LegacyRulesetExtensions.GetPrecisionAdjustedBeatLength(slider, timing, "osu");
            double length = Math.Max(velocity * duration, 0.5);
            int count = (int)Math.Ceiling(length) + 1;
            var points = new PathControlPoint[count + 1];

            for (int i = 0; i <= count; i++)
                points[i] = new PathControlPoint(new Vector2(i % 2, 0f), i == 0 ? PathType.LINEAR : null);

            slider.Path = new SliderPath(points, length);
            slider.NodeSamples = new List<IList<HitSampleInfo>> { head, samples.ToList() };
            return slider;
        }

        /// <summary>How long a drumroll lasts (as osu! times a slider of its path, repeats and velocity).</summary>
        private double sliderDuration(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            if (base.ConvertHitObject(original, beatmap, cancellationToken).FirstOrDefault() is not Slider standard)
                return 0;

            standard.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            return standard.Duration;
        }
    }
}
