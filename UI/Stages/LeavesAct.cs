using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Shizuha Aki: she shoots autumn leaves (orange maple leaves; the planner's). In her attacks every fourth bullet on the screen turns orange, more and more (BulletField.autumnLevel). When a pause starts each of them is an
        // autumn leaf: it stops, and falls from where it was, swaying, at an eighth of Zanmu's pull. A leaf that gets to the bottom of the screen fades out, and two fade in on the floor, each in a place of its own, so that they lie evenly
        // spread there (the places follow one another by the golden ratio; they cannot hit until they are there), and when the next attack starts they fall upwards at the same pull, five degrees to the left or to the right, half of them each way.
        // ======================================================================================================================================================

        private sealed class LeavesAct : StageAct
        {
            private struct Leaf
            {
                public int Index; // (in Bullets: the leaf while it falls)
                public int Number;
                public double Lands;
                public double PauseEnd;
                public bool Landed;
            }

            private const float golden_ratio = 0.618034f;

            private readonly List<(double Start, double End)> pauses;
            private readonly List<Leaf> leaves = new List<Leaf>();
            private int count;

            public LeavesAct(BossPeriod period, List<(double Start, double End)> pauses)
            {
                Period = period;
                this.pauses = pauses;
                Start = pauses[0].Start;
                End = pauses[^1].End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private static float gravity => DanmakusuBalance.ZANMU_GRAVITY / DanmakusuBalance.SHIZUHA_FALL;

            private int pauseAt(double t)
            {
                for (int i = 0; i < pauses.Count; i++)
                {
                    if (t >= pauses[i].Start && t < pauses[i].End)
                        return i;
                }

                return -1;
            }

            public override bool Absorbs(double t) => pauseAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var (start, end) in pauses)
                {
                    if (start <= to && end > from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                leaves.Clear();
                count = 0;
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                int pause = pauseAt(t);

                // (what is fired in the pause has not turned orange: it flies on)
                if (f.failed || pause < 0 || bullet % DanmakusuBalance.SHIZUHA_EVERY != 1 || emitter.Time >= pauses[pause].Start || emitter.Shape == BulletShape.Train || emitter.Deflector)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                var stored = f.makeStored(emitter, t);
                float radius = stored.Radius * DanmakusuBalance.SHIZUHA_LEAF_SIZE;
                float floor = f.stageMax.Y - radius;
                double falls = to.Y < floor ? Math.Sqrt(2.0 * (floor - to.Y) / gravity) : 0.0;
                double lands = t + falls;
                double fadesFrom = Math.Max(t, lands - DanmakusuBalance.SHIZUHA_FLOOR_FADE_MS);
                int before = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    Acc = new Vector2(0f, gravity),
                    Radius = radius,
                    Hit = stored.Hit * DanmakusuBalance.SHIZUHA_LEAF_SIZE,
                    Style = stored.Style,
                    Shape = BulletShape.Maple,
                    Tint = BulletRenderer.TINT_ORANGE,
                    TintAmount = 1f,
                    Shade = stored.Shade,
                    Until = lands,
                    AlphaFrom = fadesFrom,
                    AlphaOver = lands - fadesFrom,
                    Wander = f.circleRadius * DanmakusuBalance.SHIZUHA_SWAY_CIRCLES,
                    WanderPhase = hash01(bullet, (int)emitter.Time, 5) * MathF.Tau,
                    Spin = (bullet % 2 == 0 ? 1f : -1f) * DanmakusuBalance.SHIZUHA_TURN,
                    SpinPhase = hash01(bullet, (int)emitter.Time, 6) * MathF.Tau,
                });

                if (Bullets.Count == before)
                    return false;

                leaves.Add(new Leaf { Index = before, Number = count++, Lands = lands, PauseEnd = pauses[pause].End });
                return true;
            }

            /// <summary>Every leaf that has got to the bottom is two lying there, which fade in, each in its place, and wait for the next attack.</summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int i = 0; i < leaves.Count; i++)
                {
                    var leaf = leaves[i];

                    if (leaf.Landed || leaf.Lands > now)
                        continue;

                    leaf.Landed = true;
                    leaves[i] = leaf;

                    var fell = Bullets[leaf.Index];
                    float floor = f.stageMax.Y - fell.Radius;
                    float height = f.stageMax.Y - f.stageMin.Y + fell.Radius * 2f;
                    double fade = DanmakusuBalance.SHIZUHA_FLOOR_FADE_MS;

                    for (int k = 0; k < DanmakusuBalance.SHIZUHA_FLOOR_LEAVES; k++)
                    {
                        int number = leaf.Number * DanmakusuBalance.SHIZUHA_FLOOR_LEAVES + k;
                        float place = f.stageMin.X + (f.stageMax.X - f.stageMin.X) * (number * golden_ratio % 1f);
                        float sideways = MathF.Sin(DanmakusuBalance.SHIZUHA_RISE_DEGREES * MathF.PI / 180f) * (number % 2 == 0 ? -1f : 1f);

                        // (they do not all go at once: each waits a little time of its own after the pause)
                        double rises = Math.Max(leaf.PauseEnd + Math.Max(Period.BeatLength, 1) * DanmakusuBalance.SHIZUHA_RISE_SPREAD_BEATS * hash01(number, Id, 7), leaf.Lands + fade);

                        AddBullet(new StageBullet
                        {
                            T0 = leaf.Lands,
                            P0 = new Vector2(place, floor),
                            Radius = fell.Radius,
                            Hit = fell.Hit,
                            Style = fell.Style,
                            Shape = BulletShape.Maple,
                            Spin = fell.Spin,
                            SpinPhase = hash01(number, Id, 9) * MathF.Tau,
                            FallSpin = (number % 2 == 0 ? 1f : -1f) * DanmakusuBalance.SHIZUHA_TURN * (1f + 3f * hash01(number, Id, 8)),
                            Tint = BulletRenderer.TINT_ORANGE,
                            TintAmount = 1f,
                            Shade = fell.Shade,
                            AppearOver = fade,
                            GhostUntil = leaf.Lands + fade, // (nothing comes out of nowhere under the player)
                            GhostVisible = 1f,
                            FallAt = rises,
                            FallDir = new Vector2(sideways, -MathF.Cos(DanmakusuBalance.SHIZUHA_RISE_DEGREES * MathF.PI / 180f)),
                            FallScale = 1f / DanmakusuBalance.SHIZUHA_FALL,
                            Until = rises + Math.Sqrt(2.0 * height / gravity),
                        });
                    }
                }
            }
        }
    }
}
