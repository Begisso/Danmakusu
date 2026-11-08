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
        // Yuuka Kazami: all through her section every fourth bullet that goes out of the bottom of the screen grows into a sunflower there, after flashing on the ground for a beat. All the heads are alike (a dark brown middle, sixteen yellow petals); the stems
        // are not: each has its own height, lean and bend, and leaves of its own, so the bottom of the screen looks like a field, and each sways slowly left and right. The stem does not hurt, the head hurts like a slider body. After eight beats the stem shrinks back
        // into the ground and the head flies up, five degrees to the left (the first flower) or to the right (the second), and so on; its petals leave it as a ring of sixteen yellow petals that fly away slowly, as Yuyuko's flowers do.
        // ======================================================================================================================================================

        private sealed class SunflowerAct : StageAct
        {
            private sealed class Flower
            {
                public double Warned, Born, Flies;
                public int Number;
                public float X, Height, Lean, Bend;
                public bool Burst;
            }

            private const int stem_points = 12;

            private readonly double beat;
            private readonly List<Flower> flowers = new List<Flower>();
            private readonly List<Vector2> stem = new List<Vector2>();
            private int crossings;
            private int grown;

            public SunflowerAct(BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
                beat = Math.Max(period.BeatLength, 1);
            }

            public override void Reset(BulletField f, double time)
            {
                flowers.Clear();
                crossings = 0;
                grown = 0;
                ClearBullets();
            }

            private static int standing(List<Flower> flowers, double t)
            {
                int count = 0;

                foreach (var flower in flowers)
                {
                    if (t < flower.Flies)
                        count++;
                }

                return count;
            }

            /// <summary>A bullet goes out of the bottom: every fourth one grows a flower where it went out (it flies on: it has gone anyway).</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                float bottom = f.stageMax.Y;

                if (f.failed || t < Start || t >= End || from.Y > bottom || to.Y <= bottom || crossings++ % DanmakusuBalance.YUUKA_EVERY != 0 || standing(flowers, t) >= DanmakusuBalance.YUUKA_MAX_FLOWERS)
                    return false;

                float u = (bottom - from.Y) / Math.Max(to.Y - from.Y, 1e-6f);
                int number = grown++;
                float screen = f.stageMax.Y - f.stageMin.Y;

                double born = t + beat * DanmakusuBalance.YUUKA_WARN_BEATS;

                flowers.Add(new Flower
                {
                    Warned = t,
                    Born = born,
                    Flies = born + beat * DanmakusuBalance.YUUKA_LIFE_BEATS,
                    Number = number,
                    X = from.X + (to.X - from.X) * u,
                    Height = screen * (DanmakusuBalance.YUUKA_HEIGHT_MIN + (DanmakusuBalance.YUUKA_HEIGHT_MAX - DanmakusuBalance.YUUKA_HEIGHT_MIN) * hash01(Id, number, 21)),
                    Lean = screen * DanmakusuBalance.YUUKA_LEAN * (hash01(Id, number, 22) - 0.5f),
                    Bend = screen * DanmakusuBalance.YUUKA_LEAN * (hash01(Id, number, 23) - 0.5f),
                });

                return false;
            }

            /// <summary>A point of the stem: <paramref name="u"/> of the way up. It sways from side to side, the more the higher up, each flower in a time of its own.</summary>
            private Vector2 stemPoint(BulletField f, Flower flower, float u, double t)
            {
                float sway = f.circleRadius * DanmakusuBalance.YUUKA_SWAY_CIRCLES * u * u * MathF.Sin((float)((t - flower.Born) / (beat * DanmakusuBalance.YUUKA_SWAY_BEATS) * Math.Tau) + hash01(Id, flower.Number, 24) * MathF.Tau);

                return new Vector2(flower.X + flower.Lean * u + flower.Bend * MathF.Sin(u * MathF.PI) + sway, f.stageMax.Y - flower.Height * u);
            }

            /// <summary>0..1: how much of the stem is there at <paramref name="t"/>.</summary>
            private static float height(Flower flower, double t)
                => t < flower.Flies ? smoothStep((t - flower.Born) / DanmakusuBalance.YUUKA_GROW_MS) : 1f - smoothStep((t - flower.Flies) / DanmakusuBalance.YUUKA_GROW_MS);

            private Vector2 way(Flower flower)
            {
                float angle = DanmakusuBalance.YUUKA_FLY_DEGREES * MathF.PI / 180f * (flower.Number % 2 == 0 ? -1f : 1f);
                return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            }

            private Vector2 headAt(BulletField f, Flower flower, double t)
            {
                if (t < flower.Flies)
                    return stemPoint(f, flower, height(flower, t), t);

                return stemPoint(f, flower, 1f, flower.Flies) + way(flower) * (stageBulletSpeed(Period) * (float)(t - flower.Flies));
            }

            private static float flowerRadius(BulletField f) => f.circleRadius * DanmakusuBalance.YUUKA_FLOWER_CIRCLES;

            /// <summary>The size the head and the leaves of the stem go by.</summary>
            private static float unitOf(BulletField f) => flowerRadius(f) / (2f * DanmakusuBalance.YUYUKO_PETAL_REACH + 1f);

            /// <summary>A petal is as big as a petal of Yuyuko's flowers.</summary>
            private static float petalOf(BulletField f) => DanmakusuBalance.YUYUKO_PETAL_SIZE * 0.5f * DanmakusuBalance.YUYUKO_TREE_HEIGHT * (f.stageMax.Y - f.stageMin.Y);

            /// <summary>How far from the middle of the head the petals stand: all round its edge.</summary>
            private static float petalRing(BulletField f) => unitOf(f) * DanmakusuBalance.YUUKA_HEAD_SIZE + petalOf(f);

            /// <summary>The petals that have left a head fly away as a ring.</summary>
            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                float petal = petalOf(f);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.YUUKA_PETAL_SPEED;

                foreach (var flower in flowers)
                {
                    if (flower.Burst || flower.Flies > now)
                        continue;

                    flower.Burst = true;
                    Vector2 head = stemPoint(f, flower, 1f, flower.Flies);

                    for (int k = 0; k < DanmakusuBalance.YUUKA_PETALS; k++)
                    {
                        float angle = k * MathF.Tau / DanmakusuBalance.YUUKA_PETALS;
                        Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        AddBullet(new StageBullet
                        {
                            T0 = flower.Flies,
                            P0 = head + outward * petalRing(f),
                            V = outward * speed,
                            Radius = petal,
                            Hit = petal * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Shape = BulletShape.Petal,
                            Tint = BulletRenderer.TINT_YELLOW,
                            TintAmount = 1f,
                            Until = flower.Flies + f.stageLife(outward * speed),
                        });
                    }
                }
            }

            /// <summary>Where a flower is going to come out flashes, in the beat before it does.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float size = flowerRadius(f) * 5f;

                foreach (var flower in flowers)
                {
                    if (t < flower.Warned || t >= flower.Born)
                        continue;

                    float flash = 0.5f + 0.5f * MathF.Cos((float)((t - flower.Warned) / (beat * 0.25) * Math.Tau));

                    f.stageBack.Add(StageTexture.Glow, new Vector2(flower.X, f.stageMax.Y), new Vector2(size), 0f, 0.9f * flash, new Color4(1f, 0.85f, 0.25f, 1f), true);
                }
            }

            /// <summary>The stems, drawn like the wood of a tree (under the bullets; the field calls this while it hands the beams to the renderer).</summary>
            public void AddStems(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                float width = f.circleRadius * DanmakusuBalance.YUUKA_STEM_WIDTH;
                // (the green of Wakasagihime's seaweed, see TideAct)
                var green = new Color4(0.08f, 0.32f, 0.16f, 1f);
                var dark = new Color4(0.03f, 0.16f, 0.09f, 1f);

                foreach (var flower in flowers)
                {
                    float grownUp = height(flower, t);

                    if (t < flower.Born || grownUp <= 0.01f)
                        continue;

                    stem.Clear();

                    for (int i = 0; i <= stem_points; i++)
                        stem.Add(stemPoint(f, flower, grownUp * i / stem_points, t));

                    f.addPlainBeam(stem, width, green, dark, ref beams, ref pointTotal);
                    f.laserInstances[beams - 1].Wood = true;
                    f.laserInstances[beams - 1].CoreColour = new Color4(0.14f, 0.45f, 0.24f, 1f);
                }
            }

            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (f.failed)
                    return;

                float petal = petalOf(f);
                float unit = unitOf(f);

                foreach (var flower in flowers)
                {
                    if (t < flower.Born)
                        continue;

                    Vector2 head = headAt(f, flower, t);

                    if (t >= flower.Flies && !(head.Y > f.stageMin.Y - unit * 2f && head.X > f.stageMin.X - unit * 2f && head.X < f.stageMax.X + unit * 2f))
                        continue;

                    float scale = t < flower.Flies ? Math.Max(height(flower, t), 0.15f) : 1f;

                    // (its leaves, each on a side and at a height of its own, for as long as the stem is there)
                    float stemNow = height(flower, t);

                    for (int k = 0; k < DanmakusuBalance.YUUKA_LEAVES && stemNow > 0.05f; k++)
                    {
                        float at = stemNow * (0.15f + 0.7f * (k + hash01(Id, flower.Number, 30 + k)) / DanmakusuBalance.YUUKA_LEAVES);
                        float side = (flower.Number + k) % 2 == 0 ? 1f : -1f;
                        Vector2 outward = Vector2.Normalize(new Vector2(side, -0.45f));

                        into.Add(new StageBead
                        {
                            Position = stemPoint(f, flower, at, t) + outward * (unit * 1.1f * stemNow),
                            Radius = unit * 1.3f * stemNow,
                            Shape = BulletShape.Petal,
                            Direction = -outward,
                            Overlay = BulletRenderer.TINT_SEAWEED,
                            OverlayAmount = 1f,
                            Harmless = true,
                        });
                    }

                    // (the petals are on the head until it flies)
                    for (int k = 0; t < flower.Flies && k < DanmakusuBalance.YUUKA_PETALS; k++)
                    {
                        float angle = k * MathF.Tau / DanmakusuBalance.YUUKA_PETALS;
                        Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        into.Add(new StageBead
                        {
                            Position = head + outward * (petalRing(f) * scale),
                            Radius = petal * scale,
                            Shape = BulletShape.Petal,
                            Direction = -outward,
                            Overlay = BulletRenderer.TINT_YELLOW,
                            OverlayAmount = 1f,
                            Harmless = true,
                        });
                    }

                    float radius = unit * DanmakusuBalance.YUUKA_HEAD_SIZE * scale;

                    into.Add(new StageBead
                    {
                        Position = head,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Shape = BulletShape.Circle,
                        Direction = Vector2.UnitX,
                        Overlay = BulletRenderer.TINT_BROWN,
                        OverlayAmount = 1f,
                    });
                }
            }
        }
    }
}
