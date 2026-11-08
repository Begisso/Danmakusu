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
        // Yumemi Okazaki: in her attacks every tenth bullet has a beam for a tail, the last four beats of its way, that flies along with it (BulletField.Tails). In her pauses everything, bullets and beams, is pulled straight away
        // from her, eight times as hard as Zanmu's pull (an op, see BossOps: this act says where she is when a pause begins), and the screen shakes; a big flash warns of it, and a big grey wave goes out from her as it starts. She shoots nothing but flowers of beams: every two beats a five-petalled
        // flower of beams grows out of where she stands, every petal two smooth arcs that meet at a sharp tip, with gaps between the petals to slip through; the beams hurt like a slider body. A flower is a formula: what touches it is worked out from angle and distance.
        // ======================================================================================================================================================

        private sealed class PossibilityAct : StageAct
        {
            private readonly double[] marks;
            private readonly Vector2[] centres;
            private readonly bool[] known;
            private readonly List<(double Start, double End)> pauses;
            private readonly bool[] pointed;
            private readonly List<Vector2> petal = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private readonly Vector2[] pushedFrom;

            private static float step => MathF.Tau / DanmakusuBalance.YUMEMI_PETALS;

            public PossibilityAct(BossPeriod period, double[] marks, List<(double Start, double End)> pauses)
            {
                Period = period;
                this.marks = marks;
                this.pauses = pauses;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                pointed = new bool[pauses.Count];
                pushedFrom = new Vector2[pauses.Count];
                Start = marks[0];
                End = period.End;
            }

            private float speed => stageBulletSpeed(Period);

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private double life(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / (speed * (1f - DanmakusuBalance.YUMEMI_PETAL_DEPTH));

            private static float phaseOf(int flower) => flower * 0.37f;

            /// <summary>How far the outline of a flower of this size is from its middle: every petal is a pointed arch, two arcs of a circle from the gaps that meet at a sharp tip.</summary>
            private static float radiusAt(float scale, float angle, float phase)
            {
                float within = (angle - phase + step * 0.5f) % step;

                if (within < 0f)
                    within += step;

                float off = MathF.Abs(within - step * 0.5f) / (step * 0.5f); // (0 at the tip, 1 in the gap)
                float arc = MathF.Sqrt(Math.Max(4f - (off + 1f) * (off + 1f), 0f)) / MathF.Sqrt(3f);

                return scale * (1f + DanmakusuBalance.YUMEMI_PETAL_DEPTH * (2f * arc - 1f));
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);

                for (int k = 0; k < pauses.Count; k++)
                {
                    if (pauses[k].Start > time)
                        pointed[k] = false;
                }
            }

            public override void Early(BulletField f, double now)
            {
                for (int k = 0; k < pauses.Count; k++)
                {
                    if (pointed[k] || now < pauses[k].Start)
                        continue;

                    pointed[k] = true;
                    Vector2 here = f.bossVisualOf(Period);

                    pushedFrom[k] = here;

                    if (now - pauses[k].Start < 250)
                        f.playStageSound("yumemi_bomb");

                    foreach (var op in f.bulletOps)
                    {
                        if (op.PointKept && op.Kind == TrajectoryOpKind.Outward && op.Time == pauses[k].Start)
                            op.Point = here;
                    }
                }

                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = f.bossVisualOf(Period);
                    }
                }
            }

            /// <summary>0..1: the screen shakes all through a pause.</summary>
            public float ShakeAt(double now)
            {
                foreach (var (start, end) in pauses)
                {
                    if (now >= start && now < end)
                        return 0.5f;
                }

                return 0f;
            }

            /// <summary>A big flash warns of a pause, and when it comes a big grey wave goes out from her with everything she pushes away.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                double beat = Math.Max(Period.BeatLength, 1);
                double warn = beat * DanmakusuBalance.YUMEMI_WARN_BEATS;
                float span = Vector2.Distance(f.stageMin, f.stageMax);

                for (int k = 0; k < pauses.Count; k++)
                {
                    double start = pauses[k].Start;

                    if (t >= start - warn && t < start)
                    {
                        float flash = 0.5f + 0.5f * MathF.Cos((float)((start - t) / beat * Math.Tau));

                        // (bright red)
                        f.stageTop.Add(StageTexture.Glow, f.bossVisualOf(Period), new Vector2(span * 1.6f), 0f, 0.7f * flash, new Color4(1f, 0.08f, 0.06f, 1f), true);
                    }

                    float reach = speed * DanmakusuBalance.YUMEMI_WAVE_SPEED * (float)(t - start);

                    if (pointed[k] && t >= start && reach < span)
                        f.stageRipple.AddRing(pushedFrom[k], reach, DanmakusuBalance.YUMEMI_WAVE_HALF_WIDTH, DanmakusuBalance.YUMEMI_WAVE_ALPHA * (1f - reach / span), new Color4(0.62f, 0.62f, 0.66f, 1f));
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = halfWidth(f);
                double span = life(f);
                float length = step * DanmakusuBalance.YUMEMI_PETAL_SHARE;

                for (int k = marks.Length - 1; k >= 0; k--)
                {
                    double age = t - marks[k];

                    if (age < 0 || !known[k])
                        continue;

                    if (age > span)
                        break;

                    float scale = speed * (float)age;
                    float phase = phaseOf(k);

                    for (int p = 0; p < DanmakusuBalance.YUMEMI_PETALS; p++)
                    {
                        // (a petal is the bulge of the outline, from gap to gap)
                        float from = phase + (p - 0.5f) * step + (step - length) * 0.5f;
                        int n = Math.Clamp((int)(scale * length / 14f), 4, 40) & ~1; // (even: a point falls right on the tip)
                        petal.Clear();
                        widths.Clear();

                        for (int i = 0; i <= n; i++)
                        {
                            float angle = from + length * i / n;

                            petal.Add(centres[k] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radiusAt(scale, angle, phase));

                            // (each side is thickest in its middle and comes to a point at both ends: at the tip, and at the gap)
                            widths.Add(Math.Max(width * MathF.Pow(MathF.Sin(MathF.PI * MathF.Abs(2f * i / n - 1f)), 0.6f), 0.4f));
                        }

                        f.addPlainBeam(petal, width, colour, border, ref beams, ref pointTotal, widths);
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float thick = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE;
                float reach = thick + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                double span = life(f);

                for (int k = marks.Length - 1; k >= 0; k--)
                {
                    double age = now - marks[k];

                    if (age < 0 || !known[k])
                        continue;

                    if (age > span)
                        break;

                    Vector2 offset = f.characterPosition - centres[k];
                    float distance = offset.Length;
                    float angle = MathF.Atan2(offset.Y, offset.X);
                    float phase = phaseOf(k);
                    float off = MathF.Abs(distance - radiusAt(speed * (float)age, angle, phase));

                    if (off > reach)
                        continue;

                    // (in a gap it does not hurt, and a petal is as thin at its tip and its ends as it is drawn)
                    float within = (angle - phase + step * 0.5f) % step;

                    if (within < 0f)
                        within += step;

                    float across = MathF.Abs(within - step * 0.5f) / (step * DanmakusuBalance.YUMEMI_PETAL_SHARE * 0.5f);

                    if (across <= 1f && off <= thick * MathF.Pow(MathF.Sin(across * MathF.PI), 0.6f) + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }
        }
    }
}
