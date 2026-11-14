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
        // Mai: no pauses. A giant snowflake of beams, one after the other all through her section: it stands still where she was when it began (turned so that the player is between two of its arms), six arms
        // as long as MAI_ARM_SIZE of the playfield's width, each with side branches at 60 degrees (the longest in the middle of the arm) and the longest of those with a twig to either side, round a small hexagon
        // in the middle. Every beam is as wide as a slider's and ends in a sharp tip; a branch starts on the middle line of the one it grows out of, so the snowflake is one piece. It grows from nothing over
        // eight beats as Yuyuko's tree does: a front runs out from the middle along every beam at the same speed, and a branch starts growing when the front gets to it. Then over the next eight beats it goes
        // again from the middle out to the tips (another front, the same way), and what the front leaves behind comes apart into plain bullets half the size, one every MAI_BULLET_EVERY_CIRCLES circle radii,
        // flying straight out from the snowflake's middle. When it is all gone the next one begins. The beams hurt like a slider body.
        // ======================================================================================================================================================

        private sealed class SnowflakeAct : StageAct
        {
            // ---- the shape, with arms as long as 1: every beam's root, its way, when the front gets to its root (how far along the snowflake from the middle) and how long it is ----

            private static readonly (Vector2 Root, Vector2 Way, float Begin, float Length)[] shape = makeShape();
            private static readonly float deepest = deepestOf(shape);

            private static (Vector2, Vector2, float, float)[] makeShape()
            {
                var list = new List<(Vector2, Vector2, float, float)>();
                const float hexagon = 0.09f;

                // (how long a side branch is by where it leaves the arm: short near the middle and the tip, the longest a little inside the middle of the arm)
                float[] profileAt = { 0.12f, 0.22f, 0.38f, 0.54f, 0.68f, 0.8f, 0.9f, 0.97f };
                float[] profileLength = { 0.12f, 0.24f, 0.34f, 0.33f, 0.26f, 0.17f, 0.09f, 0.03f };

                static float profile(float[] at, float[] length, float a)
                {
                    for (int i = 1; i < at.Length; i++)
                    {
                        if (a <= at[i])
                            return length[i - 1] + (length[i] - length[i - 1]) * (a - at[i - 1]) / (at[i] - at[i - 1]);
                    }

                    return length[^1];
                }

                static Vector2 way(float degrees) => new Vector2(MathF.Cos(degrees * MathF.PI / 180f), MathF.Sin(degrees * MathF.PI / 180f));

                // (a pair of twigs at 60 degrees to either side of a beam, leaving it <paramref name="p"/> along)
                void pair(Vector2 root, float angle, float begin, float p, float twig, bool more)
                {
                    Vector2 from = root + way(angle) * p;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        float twigAngle = angle + side * 60f;
                        list.Add((from, way(twigAngle), begin + p, twig));

                        // (the longest twigs have a pair of their own, halfway along)
                        if (more && twig >= 0.08f)
                            pair(from, twigAngle, begin + p, twig * 0.5f, twig * 0.35f, false);
                    }
                }

                // (the twigs of a branch: pairs one every MAI_TWIG_STEP along it from a fifth of the way, shorter towards its tip)
                void twigs(Vector2 root, float angle, float begin, float length)
                {
                    for (float p = length * 0.2f; p <= length * 0.85f; p += DanmakusuBalance.MAI_TWIG_STEP)
                    {
                        float twig = Math.Min(0.45f * (length - p), 0.14f);

                        if (twig >= 0.02f)
                            pair(root, angle, begin, p, twig, true);
                    }
                }

                for (int arm = 0; arm < 6; arm++)
                {
                    float armAngle = arm * 60f;
                    Vector2 armWay = way(armAngle);
                    list.Add((Vector2.Zero, armWay, 0f, 1f));

                    // (the hexagon in the middle: from this arm's corner to the next one's)
                    Vector2 corner = armWay * hexagon, next = way(armAngle + 60f) * hexagon;
                    list.Add((corner, Vector2.Normalize(next - corner), hexagon, hexagon));

                    // (side branches in pairs all along the arm, each with twigs of its own)
                    for (float a = 0.14f; a <= 0.92f; a += DanmakusuBalance.MAI_BRANCH_STEP)
                    {
                        float length = profile(profileAt, profileLength, a);
                        Vector2 root = armWay * a;

                        for (int side = -1; side <= 1; side += 2)
                        {
                            float branchAngle = armAngle + side * 60f;
                            list.Add((root, way(branchAngle), a, length));
                            twigs(root, branchAngle, a, length);
                        }
                    }
                }

                return list.ToArray();
            }

            private static float deepestOf((Vector2 Root, Vector2 Way, float Begin, float Length)[] beams)
            {
                float most = 0f;

                foreach (var beam in beams)
                    most = Math.Max(most, beam.Begin + beam.Length);

                return most;
            }

            private readonly double beat;
            private readonly double[] starts; // (when every snowflake begins)
            private double growMs => beat * DanmakusuBalance.MAI_GROW_BEATS;
            private double outMs => beat * DanmakusuBalance.MAI_OUT_BEATS;

            // (the snowflake that is there now, placed when it began)
            private int placed = -1;
            private Vector2 centre;
            private float size;
            private readonly Vector2[] roots = new Vector2[shape.Length];
            private readonly Vector2[] ways = new Vector2[shape.Length];

            // (where it comes apart into bullets, and when: in the order the front gets there)
            private readonly List<(double Time, Vector2 Position)> spots = new List<(double, Vector2)>();
            private int spawned;

            private readonly MomentShapes shapes = new MomentShapes(); // (the snowflake at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();

            public SnowflakeAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double cycle = beat * (DanmakusuBalance.MAI_GROW_BEATS + DanmakusuBalance.MAI_OUT_BEATS);

                // (one begins as long as it can grow whole in her section; the last one goes away after it)
                for (double t = period.ShootStart; t + beat * DanmakusuBalance.MAI_GROW_BEATS <= period.End && list.Count < 10000; t += cycle)
                    list.Add(t);

                if (list.Count == 0)
                    list.Add(period.ShootStart);

                starts = list.ToArray();
                Start = starts[0];
                End = starts[^1] + cycle;
            }

            /// <summary>She stays until her last snowflake is gone.</summary>
            public override double MechanicsEnd => End;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>The snowflake that is there at <paramref name="t"/> (from its beginning until it is all gone), or -1.</summary>
            private int flakeAt(double t)
            {
                double cycle = growMs + outMs;
                int k = (int)Math.Floor((t - Start) / cycle);
                return k >= 0 && k < starts.Length && t >= starts[k] && t < starts[k] + cycle ? k : -1;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                placed = -1;
                spots.Clear();
                spawned = 0;
                shapes.Forget();
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (a new frame)

                if (f.failed)
                    return;

                // (what the one there comes apart into so far - also the last of it, when a frame has gone past its end)
                spawn(f, now);

                int k = flakeAt(now);

                if (k < 0 || k == placed)
                    return;

                // a new snowflake: where she is, turned so that the player is between two of its arms
                placed = k;
                centre = f.bossPositionOf(Period);
                size = (f.stageMax.X - f.stageMin.X) * DanmakusuBalance.MAI_ARM_SIZE;

                Vector2 toPlayer = f.characterPosition - centre;
                float turn = (toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f) + MathF.PI / 6f;
                float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
                Vector2 rotate(Vector2 v) => new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

                for (int i = 0; i < shape.Length; i++)
                {
                    roots[i] = centre + rotate(shape[i].Root) * size;
                    ways[i] = rotate(shape[i].Way);
                }

                // (where it will come apart: along every beam, each place when the second front gets to it)
                spots.Clear();
                spawned = 0;
                float every = Math.Max(f.circleRadius * DanmakusuBalance.MAI_BULLET_EVERY_CIRCLES, 4f);
                double goesFrom = starts[k] + growMs;

                for (int i = 0; i < shape.Length; i++)
                {
                    float length = shape[i].Length * size;
                    int count = Math.Max((int)MathF.Round(length / every), 1);

                    for (int s = 0; s < count; s++)
                    {
                        float along = (s + 0.5f) / count * length;
                        float depth = shape[i].Begin * size + along;
                        Vector2 position = roots[i] + ways[i] * along;

                        // (out of the screen a bullet flying out from the middle never comes in: none there)
                        if (position.X < f.stageMin.X || position.X > f.stageMax.X || position.Y < f.stageMin.Y || position.Y > f.stageMax.Y)
                            continue;

                        spots.Add((goesFrom + depth / (deepest * size) * outMs, position));
                    }
                }

                spots.Sort((a, b) => a.Time.CompareTo(b.Time));
                spawn(f, now);
            }

            /// <summary>The bullets of every place the second front has passed by <paramref name="now"/>: plain, half their size, flying straight out from the middle.</summary>
            private void spawn(BulletField f, double now)
            {
                if (placed < 0)
                    return;

                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.MAI_BULLET_SIZE;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.MAI_BULLET_SPEED;
                int style = f.comboStyle(0, true, Period.Rank);

                for (; spawned < spots.Count && spots[spawned].Time <= now; spawned++)
                {
                    var (time, position) = spots[spawned];
                    Vector2 outward = position - centre;
                    Vector2 velocity = (outward.LengthSquared > 1e-4f ? Vector2.Normalize(outward) : -Vector2.UnitY) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = time,
                        P0 = position,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = style,
                        Shape = BulletShape.Circle,
                        Tint = BulletRenderer.TINT_WHITE,
                        TintAmount = 1f,
                        Until = time + f.stageLife(velocity),
                    });
                }
            }

            /// <summary>
            /// What is there of beam <paramref name="i"/> at <paramref name="t"/>: from <paramref name="near"/> to <paramref name="far"/> along it (px), and whether each of those ends is a sharp tip (the front, or
            /// the beam's own tip; its root, where it grows out of another, is not). False when nothing of it is there.
            /// </summary>
            private bool partOf(int k, int i, double t, out float near, out float far)
            {
                near = far = 0f;
                float deep = deepest * size;
                float grown = deep * (float)Math.Clamp((t - starts[k]) / growMs, 0, 1);
                float gone = deep * (float)Math.Clamp((t - starts[k] - growMs) / outMs, 0, 1);
                float begin = shape[i].Begin * size, length = shape[i].Length * size;

                near = Math.Clamp(gone - begin, 0f, length);
                far = Math.Clamp(grown - begin, 0f, length);
                return far - near > 0.5f;
            }

            /// <summary>How long the sharp tip at the end of a beam is: a few beam widths, at most a third of what is there of it.</summary>
            private static float tipOf(float half, float near, float far) => Math.Min(half * DanmakusuBalance.MAI_TIP_WIDTHS, (far - near) / 3f);

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                int k = flakeAt(t);

                if (k < 0 || k != placed)
                    return;

                float half = halfWidth(f);
                var ice = new Color4(0.84f, 0.94f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.45f, 0.7f, 0.95f, 1f));
                int piece = ++f.beamPieces; // (the whole snowflake drawn layer by layer together: every edge under every inside, so the joints are seamless)

                for (int i = 0; i < shape.Length; i++)
                {
                    if (!partOf(k, i, t, out float near, out float far) || offScreen(f, roots[i] + ways[i] * near, roots[i] + ways[i] * far, half))
                        continue;

                    float tip = tipOf(half, near, far);
                    bool sharpNear = near > 0f; // (gone from its root on: the front that takes it away leaves a sharp end too)

                    line.Clear();
                    widths.Clear();
                    line.Add(roots[i] + ways[i] * near);
                    widths.Add(sharpNear ? 0f : half);

                    if (sharpNear)
                    {
                        line.Add(roots[i] + ways[i] * (near + tip));
                        widths.Add(half);
                    }

                    line.Add(roots[i] + ways[i] * (far - tip));
                    widths.Add(half);
                    line.Add(roots[i] + ways[i] * far);
                    widths.Add(0f);

                    f.addPlainBeam(line, half, ice, border, ref beams, ref pointTotal, widths);
                    f.laserInstances[beams - 1].Piece = piece;
                }
            }

            /// <summary>Whether a beam from <paramref name="a"/> to <paramref name="b"/> is wholly out of the screen on one side (most of the snowflake is: its arms are longer than the screen is wide).</summary>
            private static bool offScreen(BulletField f, Vector2 a, Vector2 b, float half)
            {
                Vector2 min = f.stageMin - new Vector2(half + 4f), max = f.stageMax + new Vector2(half + 4f);
                return (a.X < min.X && b.X < min.X) || (a.X > max.X && b.X > max.X) || (a.Y < min.Y && b.Y < min.Y) || (a.Y > max.Y && b.Y > max.Y);
            }

            /// <summary>The snowflake hurts like a slider body (its sharp tips as thin as they are drawn).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                int k = flakeAt(now);

                if (k < 0 || k != placed)
                    return false;

                Vector2 player = f.characterPosition;
                float half = halfWidth(f);
                float scale = DanmakusuBalance.BULLET_HITBOX_SCALE, room = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the snowflake of that moment is worked out once and kept for the frame; there every piece is taken as wide as the beam)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    for (int i = 0; i < shape.Length; i++)
                    {
                        if (!partOf(k, i, now, out float near, out float far) || offScreen(f, roots[i] + ways[i] * near, roots[i] + ways[i] * far, half))
                            continue;

                        fresh.Point(roots[i] + ways[i] * near, half * scale + room);
                        fresh.Point(roots[i] + ways[i] * far, half * scale + room);
                        fresh.EndRun();
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                for (int i = 0; i < shape.Length; i++)
                {
                    if (!partOf(k, i, now, out float near, out float far) || offScreen(f, roots[i] + ways[i] * near, roots[i] + ways[i] * far, half))
                        continue;

                    float tip = tipOf(half, near, far);
                    bool sharpNear = near > 0f;
                    Vector2 a = roots[i] + ways[i] * (sharpNear ? near + tip : near), b = roots[i] + ways[i] * (far - tip);

                    // (the full part, then the tips, narrowing to nothing)
                    if (distanceToSegment(player, a, b) <= half * scale + room
                        || touchesTip(player, b, roots[i] + ways[i] * far, half * scale, room)
                        || (sharpNear && touchesTip(player, a, roots[i] + ways[i] * near, half * scale, room)))
                        return true;
                }

                return false;
            }

            /// <summary>Does <paramref name="p"/> touch a tip that is <paramref name="width"/> wide at <paramref name="from"/> and narrows to nothing at <paramref name="to"/>?</summary>
            private static bool touchesTip(Vector2 p, Vector2 from, Vector2 to, float width, float room)
            {
                Vector2 along = to - from;
                float lengthSquared = along.LengthSquared;
                float u = lengthSquared < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - from, along) / lengthSquared, 0f, 1f);
                return Vector2.Distance(p, from + along * u) <= width * (1f - u) + room;
            }
        }
    }
}
