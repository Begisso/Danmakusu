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
        // Nitori Kawashiro: in every attack a giant icosahedron of beams fades in round the middle of the playfield: thirty beams, one for every edge of the figure, in five colours (six edges each, a ribbon
        // round it), with dark knots at its twelve corners. It turns about the line through two opposite corners, which lies across the screen (the animation the user gave, turned 90 degrees), once in
        // NITORI_SPIN_SECONDS (half the animation's speed), every next attack the other way round, and turning round its middle in the plane of the screen too, once in NITORI_ROLL_SECONDS, the same way; it is seen a little from the side and drawn without perspective. It grows from nothing to as wide as the
        // playfield over eight beats and lasts the whole attack; its beams hurt like a slider body once it has half faded in. When the attack is over it comes apart into plain bullets half the usual size,
        // spread along its edges as they were at that moment, in their colours, flying straight out from its middle.
        // While she attacks she shoots arcs of beam at the player very often: every quarter beat an arc ten degrees wide round the way to the player, from the edge of her magic circle, flying out one and a
        // half times as fast as her plain bullets (as the arcs of Luna Child's rings); they hurt like a slider body.
        // ======================================================================================================================================================

        private sealed class IcosahedronAct : StageAct
        {
            // ---- the figure: twelve corners of a regular icosahedron (as far as 1 from its middle), two of them on the axis it turns about (x), and its thirty edges, each with its colour ----

            private static readonly Vector3[] corners = makeCorners();
            private static readonly (int A, int B, int Colour)[] edges = makeEdges();

            private static Vector3[] makeCorners()
            {
                var list = new Vector3[12];
                float h = 1f / MathF.Sqrt(5f), r = 2f / MathF.Sqrt(5f);

                list[0] = new Vector3(1f, 0f, 0f);
                list[11] = new Vector3(-1f, 0f, 0f);

                for (int k = 0; k < 5; k++)
                {
                    float a = k * MathF.Tau / 5f, b = a + MathF.PI / 5f;
                    list[1 + k] = new Vector3(h, r * MathF.Cos(a), r * MathF.Sin(a)); // (the ring near the first corner)
                    list[6 + k] = new Vector3(-h, r * MathF.Cos(b), r * MathF.Sin(b)); // (the ring near the other, turned half a step)
                }

                return list;
            }

            private static (int, int, int)[] makeEdges()
            {
                var list = new List<(int, int, int)>();

                // (every colour is the six edges of one fifth of the way round: from the first corner, along its ring, down to the other ring both ways, along that ring, to the other corner)
                for (int k = 0; k < 5; k++)
                {
                    int up = 1 + k, upNext = 1 + (k + 1) % 5, down = 6 + k, downBefore = 6 + (k + 4) % 5, downNext = 6 + (k + 1) % 5;

                    list.Add((0, up, k));
                    list.Add((up, upNext, k));
                    list.Add((up, down, k));
                    list.Add((up, downBefore, k));
                    list.Add((down, downNext, k));
                    list.Add((down, 11, k));
                }

                return list.ToArray();
            }

            // (blue, red, gold, cyan, magenta: the colours of the animation)
            private static readonly Color4[] colours =
            {
                new Color4(0.12f, 0.24f, 0.98f, 1f),
                new Color4(0.92f, 0.12f, 0.12f, 1f),
                new Color4(0.96f, 0.76f, 0.24f, 1f),
                new Color4(0.30f, 0.90f, 0.84f, 1f),
                new Color4(0.76f, 0.26f, 0.86f, 1f),
            };

            // (what the bullets it comes apart into are tinted, edge by edge: as near the colours above as the tints go)
            private static readonly int[] tints = { BulletRenderer.TINT_BLUE, BulletRenderer.TINT_RED, BulletRenderer.TINT_YELLOW, BulletRenderer.TINT_AQUAMARINE, BulletRenderer.TINT_PURPLE };

            private readonly double beat;
            private readonly (double Start, double End)[] attacks;
            private readonly bool[] apart; // (every attack whose figure has come apart into bullets)

            // (her arcs: when each is shot, and from where and which way, taken when it comes)
            private readonly double[] arcTimes;
            private readonly Vector2[] arcFrom;
            private readonly float[] arcAim;
            private readonly bool[] arcKnown;

            private readonly Vector2[] projected = new Vector2[12];
            private int projectedAttack = -1;
            private double projectedTime = double.NaN;
            private Vector2 projectedMiddle;
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();

            public IcosahedronAct(BossPeriod period, List<(double Start, double End)> attackList)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<(double, double)>();

                foreach (var (start, end) in attackList)
                {
                    if (end > start && end > period.ShootStart)
                        list.Add((Math.Max(start, period.ShootStart), end));
                }

                attacks = list.ToArray();
                apart = new bool[attacks.Length];

                var times = new List<double>();
                double every = beat * DanmakusuBalance.NITORI_ARC_EVERY_BEATS;

                foreach (var (start, end) in attacks)
                {
                    for (double t = start; t < end && times.Count < 100000; t += every)
                        times.Add(t);
                }

                arcTimes = times.ToArray();
                arcFrom = new Vector2[arcTimes.Length];
                arcAim = new float[arcTimes.Length];
                arcKnown = new bool[arcTimes.Length];

                Start = attacks.Length > 0 ? attacks[0].Start : period.ShootStart;
                End = attacks.Length > 0 ? attacks[^1].End + DanmakusuBalance.NITORI_FADE_OUT_MS : period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                Array.Clear(apart);
                Array.Clear(arcKnown);
                projectedAttack = -1;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (every arc whose time has come takes where she is and where the player is, once)
                for (int k = firstArc(f, now); k < arcTimes.Length && arcTimes[k] <= now; k++)
                {
                    if (arcKnown[k])
                        continue;

                    arcKnown[k] = true;
                    arcFrom[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - arcFrom[k];
                    arcAim[k] = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                }

                // (an attack that is over: its figure comes apart into bullets, as it was at the end - also when that is long past, after a seek: they are where they would be)
                for (int k = 0; k < attacks.Length && attacks[k].End <= now; k++)
                {
                    if (apart[k])
                        continue;

                    apart[k] = true;
                    comeApart(f, k);
                }
            }

            // ---- the figure ----

            private float fullRadius(BulletField f) => (f.stageMax.X - f.stageMin.X) * 0.5f * DanmakusuBalance.NITORI_SIZE;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.NITORI_BEAM_WIDTH;

            /// <summary>The attack whose figure is there at <paramref name="t"/> (fading out a moment after it ends), or -1.</summary>
            private int attackAt(double t)
            {
                for (int k = 0; k < attacks.Length; k++)
                {
                    if (t >= attacks[k].Start && t < attacks[k].End + DanmakusuBalance.NITORI_FADE_OUT_MS)
                        return k;
                }

                return -1;
            }

            /// <summary>How visible the figure of attack <paramref name="k"/> is at <paramref name="t"/>: fading in at the start, and out just after the end, when it has come apart.</summary>
            private float alphaOf(int k, double t)
            {
                var (start, end) = attacks[k];
                float faded = smoothStep((t - start) / (beat * DanmakusuBalance.NITORI_FADE_IN_BEATS));
                return t < end ? faded : faded * (1f - (float)Math.Clamp((t - end) / DanmakusuBalance.NITORI_FADE_OUT_MS, 0, 1));
            }

            /// <summary>Does the figure hurt at <paramref name="t"/>? Once it has half faded in, until its attack is over.</summary>
            private bool figureHurts(int k, double t) => t < attacks[k].End && alphaOf(k, t) >= 0.5f;

            /// <summary>Where the twelve corners of the figure of attack <paramref name="k"/> are on the screen at <paramref name="t"/> (into <see cref="projected"/>; worked out once for a moment).</summary>
            private void project(BulletField f, int k, double t)
            {
                if (k == projectedAttack && t == projectedTime)
                    return;

                projectedAttack = k;
                projectedTime = t;

                var (start, _) = attacks[k];
                float size = fullRadius(f) * smoothStep((Math.Min(t, attacks[k].End) - start) / (beat * DanmakusuBalance.NITORI_GROW_BEATS));
                float way = k % 2 == 0 ? 1f : -1f; // (every next attack the other way round)
                float turn = way * (float)((Math.Min(t, attacks[k].End) - start) / (DanmakusuBalance.NITORI_SPIN_SECONDS * 1000.0) * Math.Tau);
                float cosTurn = MathF.Cos(turn), sinTurn = MathF.Sin(turn);
                float tilt = DanmakusuBalance.NITORI_TILT_DEGREES * MathF.PI / 180f;
                float cosTilt = MathF.Cos(tilt), sinTilt = MathF.Sin(tilt);

                projectedMiddle = (f.stageMin + f.stageMax) * 0.5f;

                // (and the whole of it turns round its middle in the plane of the screen too, the same way round as it spins: 2026-11-14)
                float roll = way * (float)((Math.Min(t, attacks[k].End) - start) / (DanmakusuBalance.NITORI_ROLL_SECONDS * 1000.0) * Math.Tau);
                float cosRoll = MathF.Cos(roll), sinRoll = MathF.Sin(roll);

                for (int c = 0; c < corners.Length; c++)
                {
                    Vector3 p = corners[c];

                    // (turned about its axis, across the screen; then seen a little from the side; no perspective)
                    float y = p.Y * cosTurn - p.Z * sinTurn;
                    float z = p.Y * sinTurn + p.Z * cosTurn;
                    float x = p.X * cosTilt + z * sinTilt;

                    projected[c] = projectedMiddle + new Vector2(x * cosRoll - y * sinRoll, x * sinRoll + y * cosRoll) * size;
                }
            }

            /// <summary>
            /// The figure of attack <paramref name="k"/> comes apart at the end of the attack: plain bullets, half their size, one every NITORI_BULLET_EVERY_CIRCLES circle radii along every edge as it was then, in
            /// its colour, flying straight out from the figure's middle.
            /// </summary>
            private void comeApart(BulletField f, int k)
            {
                double at = attacks[k].End;

                if (alphaOf(k, at - 1) < 0.5f)
                    return; // (an attack too short for it to have formed)

                project(f, k, at);

                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.NITORI_BULLET_SIZE;
                float every = Math.Max(f.circleRadius * DanmakusuBalance.NITORI_BULLET_EVERY_CIRCLES, 4f);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.NITORI_BULLET_SPEED;
                int style = f.comboStyle(0, true, Period.Rank);

                foreach (var (a, b, colour) in edges)
                {
                    Vector2 from = projected[a], to = projected[b];
                    int count = Math.Max((int)MathF.Round(Vector2.Distance(from, to) / every), 1);

                    for (int i = 0; i < count; i++)
                    {
                        Vector2 position = Vector2.Lerp(from, to, (i + 0.5f) / count);
                        Vector2 outward = position - projectedMiddle;

                        if (outward.LengthSquared < 1f)
                            outward = new Vector2(-(to - from).Y, (to - from).X); // (at the very middle: across its edge)

                        Vector2 velocity = (outward.LengthSquared > 1e-6f ? Vector2.Normalize(outward) : -Vector2.UnitY) * speed;

                        AddBullet(new StageBullet
                        {
                            T0 = at,
                            P0 = position,
                            V = velocity,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = style,
                            Shape = BulletShape.Circle,
                            Tint = tints[colour],
                            TintAmount = 1f,
                            Until = at + f.stageLife(velocity),
                        });
                    }
                }
            }

            // ---- her arcs ----

            private float arcSpeed => stageBulletSpeed(Period) * DanmakusuBalance.NITORI_ARC_SPEED;

            private double arcLife(BulletField f)
            {
                float diagonal = f.hasScreenBounds ? Vector2.Distance(f.screenMin, f.screenMax) : 1500f;
                return (diagonal + DanmakusuBalance.MAGIC_CIRCLE_RADIUS) / Math.Max(arcSpeed, 1e-4f);
            }

            private float arcRadius(int k, double t) => DanmakusuBalance.MAGIC_CIRCLE_RADIUS + arcSpeed * (float)(t - arcTimes[k]);

            /// <summary>The first arc that can still be there at <paramref name="t"/>.</summary>
            private int firstArc(BulletField f, double t)
            {
                double span = arcLife(f);
                int low = 0, high = arcTimes.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (arcTimes[middle] < t - span)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            // ---- drawn ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                int k = attackAt(t);

                if (k >= 0)
                {
                    float alpha = alphaOf(k, t);

                    if (alpha > 0.005f)
                    {
                        project(f, k, t);
                        float half = halfWidth(f);

                        foreach (var (a, b, colour) in edges)
                        {
                            if (Vector2.DistanceSquared(projected[a], projected[b]) < 0.25f)
                                continue; // (it has only just begun to grow)

                            line.Clear();
                            line.Add(projected[a]);
                            line.Add(projected[b]);
                            fades.Clear();
                            fades.Add(alpha);
                            fades.Add(alpha);
                            f.addPlainBeam(line, half, colours[colour], DanmakusuBalance.BorderColour(colours[colour]), ref beams, ref pointTotal, null, fades);
                        }
                    }
                }

                if (f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 own = f.palette[style % f.palette.Length];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = halfWidth(f);
                float span = DanmakusuBalance.NITORI_ARC_DEGREES * MathF.PI / 180f;

                for (int a = firstArc(f, t); a < arcTimes.Length && arcTimes[a] <= t; a++)
                {
                    if (!arcKnown[a])
                        continue;

                    float r = arcRadius(a, t);
                    line.Clear();
                    bool visible = false;

                    for (int p = 0; p <= DanmakusuBalance.NITORI_ARC_POINTS; p++)
                    {
                        float angle = arcAim[a] - span * 0.5f + span * p / DanmakusuBalance.NITORI_ARC_POINTS;
                        Vector2 point = arcFrom[a] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
                        line.Add(point);

                        if (!f.hasScreenBounds || (point.X >= f.screenMin.X - width && point.X <= f.screenMax.X + width && point.Y >= f.screenMin.Y - width && point.Y <= f.screenMax.Y + width))
                            visible = true;
                    }

                    if (visible)
                        f.addPlainBeam(line, width, own, border, ref beams, ref pointTotal);
                }
            }

            /// <summary>Dark knots at the corners of the figure (they do not hurt).</summary>
            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (f.failed)
                    return;

                int k = attackAt(t);

                if (k < 0)
                    return;

                float alpha = alphaOf(k, t);

                if (alpha <= 0.005f)
                    return;

                project(f, k, t);
                float size = halfWidth(f) * 1.7f;

                for (int c = 0; c < corners.Length; c++)
                {
                    into.Add(new StageBead
                    {
                        Position = projected[c],
                        Radius = size,
                        Fade = 1f - alpha,
                        Style = 0,
                        Shape = BulletShape.Circle,
                        Overlay = BulletRenderer.TINT_HISAMI_HAIR,
                        OverlayAmount = 1f,
                        Harmless = true,
                    });
                }
            }

            // ---- what hurts ----

            /// <summary>The beams of the figure and her arcs hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                int k = attackAt(now);

                if (k >= 0 && figureHurts(k, now))
                {
                    project(f, k, now);

                    foreach (var (a, b, _) in edges)
                    {
                        if (distanceToSegment(player, projected[a], projected[b]) <= reach)
                            return true;
                    }
                }

                float half = DanmakusuBalance.NITORI_ARC_DEGREES * 0.5f * MathF.PI / 180f;

                for (int a = firstArc(f, now); a < arcTimes.Length && arcTimes[a] <= now; a++)
                {
                    if (!arcKnown[a])
                        continue;

                    Vector2 offset = player - arcFrom[a];
                    float distance = offset.Length;

                    if (distance < 1e-3f || MathF.Abs(distance - arcRadius(a, now)) > reach)
                        continue;

                    // (the angle across the width of the beam counts too)
                    float off = MathF.Abs(MathF.IEEERemainder(MathF.Atan2(offset.Y, offset.X) - arcAim[a], MathF.Tau));

                    if (off <= half + reach / distance)
                        return true;
                }

                return false;
            }
        }
    }
}
