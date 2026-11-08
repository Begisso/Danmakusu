using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Rumia: all the time her section lasts (she never pauses) a ring of 10 invisible circles is thrown from her every 8 beats (as often as Sannyo's smoke), each 5 circle radii big, flying straight out at the speed of
        // Raiko's ripples. They do nothing to the player. Every bullet and every piece of beam that is inside one cannot be seen (as a ripple of Raiko's changes what it goes over, these hide it; what is hidden is still there and
        // still hurts). Over the edge of a circle what is under it is half see-through, and it comes and goes smoothly. They are never drawn.
        // ======================================================================================================================================================

        private sealed class RumiaAct : StageAct
        {
            private readonly double[] times;
            private readonly Vector2[] origins;
            private readonly bool[] known;

            /// <summary>When the rings of circles of a section that runs from <paramref name="start"/> to <paramref name="end"/> are thrown (the first at the start).</summary>
            public static double[] RingTimes(BossPeriod period, double start, double end)
            {
                double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.RUMIA_EVERY_BEATS;
                var list = new List<double>();

                for (double t = start; t < end && list.Count < 400; t += gap)
                    list.Add(t);

                return list.ToArray();
            }

            public RumiaAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                times = RingTimes(period, start, end);
                origins = new Vector2[times.Length];
                known = new bool[times.Length];
            }

            private static float radiusOf(BulletField f) => f.circleRadius * DanmakusuBalance.RUMIA_CIRCLE_RADIUS;

            /// <summary>The longest a circle can last: until it has crossed the playfield's diagonal and is out of it altogether.</summary>
            private static double maxLife(BulletField f)
                => (Vector2.Distance(f.stageMin, f.stageMax) + 2f * radiusOf(f)) / DanmakusuBalance.RUMIA_CIRCLE_SPEED * 1.15;

            private Vector2 centreOf(int ring, int i, double age)
            {
                int count = DanmakusuBalance.RUMIA_CIRCLES;
                float angle = ring * MathF.Tau / (2f * count) + i * MathF.Tau / count;
                return origins[ring] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (DanmakusuBalance.RUMIA_CIRCLE_SPEED * (float)age);
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                double life = maxLife(f);

                if (now < Start - 1 || now > End + life)
                    return;

                float radius = radiusOf(f);
                float reach = radius + f.circleRadius * DanmakusuBalance.RUMIA_EDGE_CIRCLES;

                for (int k = 0; k < times.Length; k++)
                {
                    double age = now - times[k];

                    if (age < 0)
                    {
                        known[k] = false;
                        continue;
                    }

                    if (!known[k])
                    {
                        if (age > 200)
                            continue; // (too late to know where she threw it from)

                        known[k] = true;
                        origins[k] = f.bossVisualOf(Period);
                    }

                    if (age > life)
                        continue;

                    float presence = smoothStep(age / DanmakusuBalance.RUMIA_FADE_IN_MS);

                    for (int i = 0; i < DanmakusuBalance.RUMIA_CIRCLES; i++)
                    {
                        Vector2 centre = centreOf(k, i, age);

                        if (centre.X < f.stageMin.X - reach || centre.X > f.stageMax.X + reach || centre.Y < f.stageMin.Y - reach || centre.Y > f.stageMax.Y + reach)
                            continue;

                        f.addHide(centre, radius, presence);
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < times.Length; k++)
                {
                    if (times[k] > time)
                        known[k] = false;
                }
            }
        }
    }
}
