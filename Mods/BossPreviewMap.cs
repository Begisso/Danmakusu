using System;
using System.Collections.Generic;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The little map that the attack preview of the boss menu plays: 4 seconds of lead-in, a kiai section of 40 beats with circles (normal, whistle, clap, finish) and sliders of different kinds (short, long and curved,
    /// fast, slow, with a repeat, with a whistle on the body: a laser), then a spinner. The bosses attack in the kiai section and in the spinner, like in a real map.
    /// </summary>
    public static class BossPreviewMap
    {
        public const double BEAT = 500;
        public const double KIAI_START = 4000;
        public const double KIAI_LENGTH = 40 * BEAT;
        public const double SPINNER_START = KIAI_START + KIAI_LENGTH + 2 * BEAT;
        public const double SPINNER_LENGTH = 8 * BEAT;

        /// <summary>The preview plays from 0 to this and starts again.</summary>
        public const double LENGTH = SPINNER_START + SPINNER_LENGTH + 4 * BEAT;

        public static OsuBeatmap Create()
        {
            var beatmap = new OsuBeatmap
            {
                ControlPointInfo = new LegacyControlPointInfo(),
            };

            beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;

            var difficulty = beatmap.Difficulty;
            difficulty.CircleSize = 4;
            difficulty.DrainRate = 5;
            difficulty.OverallDifficulty = 8;
            difficulty.ApproachRate = 9;
            difficulty.SliderMultiplier = 1.4;
            difficulty.SliderTickRate = 1;

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = BEAT });
            beatmap.ControlPointInfo.Add(KIAI_START, new EffectControlPoint { KiaiMode = true });
            beatmap.ControlPointInfo.Add(KIAI_START + KIAI_LENGTH, new EffectControlPoint { KiaiMode = false });

            var objects = new List<OsuHitObject>();

            // 20 beats of objects, played twice (the second time mirrored left to right)
            for (int round = 0; round < 2; round++)
            {
                double first = KIAI_START + round * 20 * BEAT;
                bool mirror = round == 1;

                Vector2 at(float x, float y) => new Vector2(mirror ? 512 - x : x, y);
                double time(double beat) => first + beat * BEAT;

                HitCircle circle(double beat, float x, float y, string? addition, bool newCombo = false)
                    => new HitCircle { StartTime = time(beat), Position = at(x, y), NewCombo = newCombo, Samples = samples(addition) };

                Slider slider(double beat, PathType type, Vector2[] points, double length, int repeats, double velocity, string? addition, bool bodyWhistle = false, bool newCombo = false)
                {
                    var start = points[0];
                    var relative = new Vector2[points.Length];

                    for (int i = 0; i < points.Length; i++)
                        relative[i] = (mirror ? new Vector2(512 - points[i].X, points[i].Y) : points[i]) - (mirror ? new Vector2(512 - start.X, start.Y) : start);

                    var nodes = new List<IList<HitSampleInfo>>();

                    for (int i = 0; i < repeats + 2; i++)
                        nodes.Add(samples(i == 0 ? addition : null));

                    return new Slider
                    {
                        StartTime = time(beat),
                        Position = at(start.X, start.Y),
                        Path = new SliderPath(type, relative, length),
                        RepeatCount = repeats,
                        SliderVelocityMultiplier = velocity,
                        NewCombo = newCombo,
                        Samples = bodyWhistle ? samples(HitSampleInfo.HIT_WHISTLE) : samples(null),
                        NodeSamples = nodes,
                    };
                }

                // a stream of the four kinds of circle
                objects.Add(circle(0, 96, 96, null, true));
                objects.Add(circle(0.5, 176, 112, HitSampleInfo.HIT_WHISTLE));
                objects.Add(circle(1, 256, 96, HitSampleInfo.HIT_CLAP));
                objects.Add(circle(1.5, 336, 112, HitSampleInfo.HIT_FINISH));

                // a short straight slider
                objects.Add(slider(3, PathType.LINEAR, new[] { new Vector2(416, 150), new Vector2(416, 290) }, 140, 0, 1, null, newCombo: true));

                // a long curved slider (a finish on its head)
                objects.Add(slider(5, PathType.BEZIER, new[] { new Vector2(64, 300), new Vector2(144, 180), new Vector2(264, 330), new Vector2(344, 210) }, 460, 0, 1, HitSampleInfo.HIT_FINISH, newCombo: true));

                // a fast slider on an arc
                objects.Add(slider(9, PathType.PERFECT_CURVE, new[] { new Vector2(80, 100), new Vector2(240, 40), new Vector2(400, 120) }, 360, 0, 2, HitSampleInfo.HIT_CLAP, newCombo: true));

                // a slider with a repeat and a whistle on its body (it fires a laser)
                objects.Add(slider(11.5, PathType.LINEAR, new[] { new Vector2(100, 200), new Vector2(300, 200) }, 200, 1, 1, null, bodyWhistle: true, newCombo: true));

                // three circles down the screen
                objects.Add(circle(14, 400, 300, null, true));
                objects.Add(circle(14.5, 320, 330, HitSampleInfo.HIT_CLAP));
                objects.Add(circle(15, 240, 300, HitSampleInfo.HIT_WHISTLE));

                // a slow slider
                objects.Add(slider(16, PathType.LINEAR, new[] { new Vector2(160, 330), new Vector2(360, 330) }, 200, 0, 0.5, HitSampleInfo.HIT_WHISTLE, newCombo: true));

                objects.Add(circle(19, 256, 200, HitSampleInfo.HIT_FINISH, true));
            }

            objects.Add(new Spinner { StartTime = SPINNER_START, Duration = SPINNER_LENGTH, Position = new Vector2(256, 192), NewCombo = true });

            beatmap.HitObjects.AddRange(objects);

            // the same steps as a real map goes through: combos, then the defaults of every object, then stacking
            var processor = new OsuBeatmapProcessor(beatmap);
            processor.PreProcess();

            foreach (var hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            processor.PostProcess();

            return beatmap;
        }

        private static IList<HitSampleInfo> samples(string? addition)
        {
            var list = new List<HitSampleInfo> { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };

            if (addition != null)
                list.Add(new HitSampleInfo(addition));

            return list;
        }
    }
}
