using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Sannyo Komakusa: all the time her section lasts a ring of 8 smoke clouds is thrown from her every 8 beats (each ring turned half a step against the one before). A cloud flies straight at half the speed of Raiko's ripples,
        // swells, sways and rises a little, and does not fade: it goes on until it has left the playfield. It is drawn as a few grey, see-through puffs. Bullets in it are half see-through, twice as big and hurt like a slider body; the smoke does not hurt.
        // ======================================================================================================================================================

        private sealed class SmokeAct : StageAct
        {
            private readonly double[] times;
            private readonly Vector2[] origins;
            private readonly bool[] known;

            /// <summary>When the rings of an attack that runs from <paramref name="start"/> to <paramref name="end"/> are thrown (the first at the start).</summary>
            public static double[] RingTimes(BossPeriod period, double start, double end)
            {
                double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.SANNYO_SMOKE_EVERY_BEATS;
                var list = new List<double>();

                for (double t = start; t < end && list.Count < 400; t += gap)
                    list.Add(t);

                return list.ToArray();
            }

            public SmokeAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                times = RingTimes(period, start, end);
                origins = new Vector2[times.Length];
                known = new bool[times.Length];
            }

            /// <summary>The longest a cloud can last: until it has crossed the playfield's diagonal and is out of it altogether (a safe limit).</summary>
            private static double maxLife(BulletField f)
                => (Vector2.Distance(f.stageMin, f.stageMax) + 4f * f.circleRadius * DanmakusuBalance.SANNYO_SMOKE_RADIUS_END) / DanmakusuBalance.SANNYO_SMOKE_SPEED * 1.15;

            private static bool outside(BulletField f, Vector2 centre, float radius)
                => centre.X < f.stageMin.X - radius || centre.X > f.stageMax.X + radius || centre.Y < f.stageMin.Y - radius || centre.Y > f.stageMax.Y + radius;

            /// <summary>Where cloud <paramref name="i"/> of ring <paramref name="ring"/> is <paramref name="age"/> ms after it was thrown, how big it is and how present (0..1: it comes in and goes out softly).</summary>
            private void cloud(BulletField f, int ring, int i, double age, out Vector2 centre, out float radius, out float presence)
            {
                int count = DanmakusuBalance.SANNYO_SMOKE_COUNT;
                float angle = ring * MathF.Tau / (2f * count) + i * MathF.Tau / count;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var side = new Vector2(-direction.Y, direction.X);
                float seed = hash01(Id, ring * 16 + i, 21);
                float sway = MathF.Sin((float)(age / DanmakusuBalance.SANNYO_SMOKE_SWAY_PERIOD_MS) * MathF.Tau + seed * MathF.Tau) * DanmakusuBalance.SANNYO_SMOKE_SWAY * f.circleRadius * smoothStep(age / 2000.0);

                centre = origins[ring] + direction * (DanmakusuBalance.SANNYO_SMOKE_SPEED * (float)age) + side * sway + new Vector2(0f, -DanmakusuBalance.SANNYO_SMOKE_RISE * (float)age);
                radius = f.circleRadius * (DanmakusuBalance.SANNYO_SMOKE_RADIUS_START + (DanmakusuBalance.SANNYO_SMOKE_RADIUS_END - DanmakusuBalance.SANNYO_SMOKE_RADIUS_START) * (1f - MathF.Exp(-(float)(age / DanmakusuBalance.SANNYO_SMOKE_GROW_MS))));
                presence = smoothStep(age / 400.0); // (it comes in softly and never goes out: it leaves the playfield)
            }

            public override void Update(BulletField f, double now)
            {
                double life = maxLife(f);

                if (now < Start - 1 || now > End + life)
                    return;

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

                    for (int i = 0; i < DanmakusuBalance.SANNYO_SMOKE_COUNT; i++)
                    {
                        cloud(f, k, i, age, out Vector2 centre, out float radius, out float presence);

                        if (presence > 0.01f && !outside(f, centre, radius))
                            f.addSmoke(centre, radius, presence);
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

            public override void Draw(BulletField f, double t)
            {
                double life = maxLife(f);

                if (t < Start || t > End + life)
                    return;

                var grey = new Color4(0.62f, 0.62f, 0.66f, 1f);

                for (int k = 0; k < times.Length; k++)
                {
                    double age = t - times[k];

                    if (!known[k] || age <= 0 || age > life)
                        continue;

                    for (int i = 0; i < DanmakusuBalance.SANNYO_SMOKE_COUNT; i++)
                    {
                        cloud(f, k, i, age, out Vector2 centre, out float radius, out float presence);

                        if (presence <= 0.01f || outside(f, centre, radius * 1.5f))
                            continue;

                        // a cloud is a few puffs that drift round in it
                        for (int p = 0; p < DanmakusuBalance.SANNYO_SMOKE_PUFFS; p++)
                        {
                            int key = k * 128 + i * 8 + p;
                            float orbit = radius * (0.15f + 0.45f * hash01(Id, key, 31));
                            float turn = hash01(Id, key, 32) * MathF.Tau + (float)age * (hash01(Id, key, 33) - 0.5f) * 0.0012f;
                            float size = radius * 2f * (0.55f + 0.35f * hash01(Id, key, 34));
                            Vector2 at = centre + new Vector2(MathF.Cos(turn), MathF.Sin(turn)) * orbit;

                            f.stageBack.Add(StageTexture.Glow, at, new Vector2(size), 0f, DanmakusuBalance.SANNYO_SMOKE_ALPHA * presence, grey);
                        }
                    }
                }
            }
        }
    }
}
