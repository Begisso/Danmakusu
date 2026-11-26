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
        // Eiki Shiki: when an attack begins a magic circle, 4.8 circle radii from its middle to its edge, with rings of runes turning round its edge, is placed round the player, and stays where it is for the whole attack; a second one,
        // as big, is placed where the first is mirrored through the middle of the screen (both ways: left and right, top and bottom). The character cannot leave the two: wherever the cursor goes, or a teleport, it stays inside one of
        // them, on the border of the nearer one at most (so it jumps across when the cursor is nearer to the other); the circles themselves do not hurt. Everything, bullets and every slice of every beam, that enters a circle is sent
        // at its middle at half its speed, flies on through it, and has its speed back when the circles are gone (one op for both, see BossOps: this act tells it where they are and how big; a thing is taken by the one it enters
        // first, once). Bullets outside both circles are drawn dim. The circles fade out in the pause. She shoots talismans in very dense rings (the planner's).
        // Since 2026-11-26, for EIKI_WARN_BEATS before each circle comes, EIKI_WARN_ARROWS big red arrows stand round the player and round where they are mirrored, on the circle's edge, pointing in.
        // ======================================================================================================================================================

        private sealed class JudgementAct : StageAct
        {
            private readonly List<(double Start, double End)> attacks;
            private readonly Vector2[] centres;
            private readonly Vector2[] mirrors; // (the second circle of each attack)
            private readonly bool[] known;

            public JudgementAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                centres = new Vector2[attacks.Count];
                mirrors = new Vector2[attacks.Count];
                known = new bool[attacks.Count];
                Start = period.ShootStart;
                End = period.End + DanmakusuBalance.STAGE_FADE_OUT_MS;
            }

            private static float radius(BulletField f) => f.circleRadius * DanmakusuBalance.EIKI_RADIUS_CIRCLES;

            /// <summary>The attack whose circle is there at <paramref name="t"/> (also while it fades out), or -1.</summary>
            private int circleAt(double t)
            {
                for (int k = attacks.Count - 1; k >= 0; k--)
                {
                    if (known[k] && attacks[k].End > attacks[k].Start && t >= attacks[k].Start && t <= attacks[k].End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                        return k;
                }

                return -1;
            }

            private float presence(int k, double t)
                => smoothStep((t - attacks[k].Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((attacks[k].End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

            /// <summary>Time went back: a circle whose attack begins later is placed again; one that was placed before that moment stays where it was.</summary>
            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < attacks.Count; k++)
                {
                    if (attacks[k].Start > time)
                        known[k] = false;
                }
            }

            /// <summary>
            /// Where the character may be at <paramref name="t"/>: in an attack in one of the two circles, on the border of the nearer one at most. (The first frame of an attack places the circle round the character: the field asks this before it moves any bullet, so
            /// nothing is sent at the wrong place.)
            /// </summary>
            public Vector2 Limit(BulletField f, Vector2 position, double t)
            {
                for (int k = 0; k < attacks.Count; k++)
                {
                    var (start, end) = attacks[k];

                    if (end <= start || t < start || t >= end)
                        continue;

                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = position;
                        mirrors[k] = f.stageMin + f.stageMax - position;

                        foreach (var op in f.bulletOps)
                        {
                            if (op.PointKept && op.Kind == TrajectoryOpKind.Redirect && op.Time == start)
                            {
                                op.Point = position;
                                op.SecondPoint = mirrors[k];
                                op.Radius = radius(f);
                            }
                        }
                    }

                    // (either circle: inside one it goes where it likes; outside both it stays on the border of the nearer one, so with the cursor it jumps to the other one when the cursor is nearer to it)
                    Vector2 away = position - centres[k], awayMirror = position - mirrors[k];
                    float reach = radius(f);

                    if (away.LengthSquared <= reach * reach || awayMirror.LengthSquared <= reach * reach)
                        return position;

                    return away.LengthSquared <= awayMirror.LengthSquared ? centres[k] + Vector2.Normalize(away) * reach : mirrors[k] + Vector2.Normalize(awayMirror) * reach;
                }

                return position;
            }

            /// <summary>
            /// Where the character would be held at <paramref name="t"/> if it tried to be at <paramref name="position"/> - as <see cref="Limit"/>, but only by circles already placed, and placing none: the
            /// autoplay asks this for the places it looks at, so that it sees where it would really end up (2026-11-14: it walked to places outside the circles, was held on their border, and was hit there).
            /// </summary>
            public Vector2 Held(BulletField f, Vector2 position, double t)
            {
                for (int k = 0; k < attacks.Count; k++)
                {
                    var (start, end) = attacks[k];

                    if (end <= start || t < start || t >= end || !known[k])
                        continue;

                    Vector2 away = position - centres[k], awayMirror = position - mirrors[k];
                    float reach = radius(f);

                    if (away.LengthSquared <= reach * reach || awayMirror.LengthSquared <= reach * reach)
                        return position;

                    return away.LengthSquared <= awayMirror.LengthSquared ? centres[k] + Vector2.Normalize(away) * reach : mirrors[k] + Vector2.Normalize(awayMirror) * reach;
                }

                return position;
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                int k = circleAt(t);

                if (!f.failed && k >= 0)
                    into.Add(new StageDim { Centre = centres[k], Radius = radius(f), Strength = DanmakusuBalance.EIKI_DIM * presence(k, t), Outside = true, Second = mirrors[k] });
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                drawWarning(f, t);

                int k = circleAt(t);

                if (k < 0)
                    return;

                float level = presence(k, t);

                drawCircle(f, centres[k], level, t);
                drawCircle(f, mirrors[k], level, t);
            }

            /// <summary>
            /// For EIKI_WARN_BEATS before a circle comes (2026-11-26, "before green circle spawns - big red arrow warnings pointing at its centre, they are round circle"): EIKI_WARN_ARROWS big red arrows
            /// round where it will be - on its edge, pointing in at its middle - round the player (it is placed round them) and round where it will be mirrored, blinking faster as it comes.
            /// </summary>
            private void drawWarning(BulletField f, double t)
            {
                double warn = Math.Max(Period.BeatLength, 1) * DanmakusuBalance.EIKI_WARN_BEATS;

                for (int k = 0; k < attacks.Count; k++)
                {
                    var (start, end) = attacks[k];

                    if (end <= start || t >= start || t < start - warn)
                        continue;

                    double left = (start - t) / warn;
                    float level = smoothStep((1 - left) / 0.25) * (0.6f + 0.4f * MathF.Cos((float)(1 - left) * MathF.PI * 6f));
                    Vector2 centre = f.characterPosition;
                    float reach = radius(f);
                    float arrow = f.circleRadius * DanmakusuBalance.EIKI_WARN_ARROW_SIZE;
                    var red = new Color4(1f, 0.2f, 0.15f, 1f);

                    foreach (var middle in new[] { centre, f.stageMin + f.stageMax - centre })
                    {
                        for (int a = 0; a < DanmakusuBalance.EIKI_WARN_ARROWS; a++)
                        {
                            float angle = a * MathF.Tau / DanmakusuBalance.EIKI_WARN_ARROWS;
                            Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                            f.stageRipple.Add(StageTexture.SeijaArrow, middle + outward * reach,new Vector2(arrow), angle * 180f / MathF.PI + 180f, level, red);
                        }
                    }

                    break;
                }
            }

            private static void drawCircle(BulletField f, Vector2 centre, float level, double t)
            {
                float reach = radius(f);
                var colour = new Color4(0.45f, 0.8f, 0.55f, 1f);

                f.stageBack.Add(StageTexture.Glow, centre, new Vector2(reach * 2.6f), 0f, 0.18f * level, colour, true);

                // (a magic circle round the edge: two rings of runes that turn against each other, and bright lines between them)
                float turn = (float)(t * 0.006);

                f.stageRipple.Add(StageTexture.MagicCircle, centre, new Vector2(reach * 2.32f), turn, DanmakusuBalance.EIKI_RING_ALPHA * level, colour);
                f.stageRipple.Add(StageTexture.MagicCircle, centre, new Vector2(reach * 2.08f), -turn * 1.6f, 0.5f * DanmakusuBalance.EIKI_RING_ALPHA * level, new Color4(0.75f, 1f, 0.8f, 1f));
                f.stageRipple.AddRing(centre, reach, Math.Max(f.circleRadius * 0.12f, 2f), DanmakusuBalance.EIKI_RING_ALPHA * level, colour);
            }
        }

        private readonly List<JudgementAct> judgementActs = new List<JudgementAct>();
    }
}
