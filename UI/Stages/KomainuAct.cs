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
        // Aunn Komano (2026-11-21): every AUNN_EVERY_BEATS of her attacks she shoots AUNN_BEAMS curving beams from where she is, the first at the player and the others evenly round: each is as wide as a
        // slider's beam and AUNN_BEAM_LENGTH_CIRCLES circle radii long, and its head runs along a curve (the body following it, as a snake's) - bending ever more over its first AUNN_CURL_CIRCLES circle
        // radii, then once round a loop AUNN_LOOP_CIRCLES circle radii wide, then straight out of the screen. Every second volley curls the other way. They hurt like a slider body.
        // Her other mechanic is the ops' (BossOps: every bullet fired in her section stops (it no longer swells: 2026-11-22), then sets off at the player - AUNN_MOVE_SPEED times as fast as it was fired - moves, stands, sets off again...); this act places where the player is when each of
        // those moves begins (every ring's own: BulletEmitter.ConfigureOps), and the field draws the arrows that warn of them (SwellWarnings).
        // ======================================================================================================================================================

        private sealed class KomainuAct : StageAct
        {
            private readonly double beat;

            // (her volleys: when each is shot, and from where and which way, taken when it comes)
            private readonly double[] volleyTimes;
            private readonly Vector2[] volleyFrom;
            private readonly float[] volleyAim;
            private readonly bool[] volleyKnown;

            // (every move of every ring of her section, in the order of their times: where the player is when each comes is placed then)
            private readonly (TrajectoryOp Op, int Ring)[] aims;
            private int nextAim;

            private readonly MomentShapes shapes = new MomentShapes(); // (the beams at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> line = new List<Vector2>();

            // (the curve every beam runs along: from 0, heading along +x and curling towards +y, a point every path_step px; made when first needed, as big as the circles are)
            private Vector2[]? path;
            private float pathLength;

            private const float path_step = 2f;
            private const float body_step = 6f;

            public KomainuAct(BulletField f, BossPeriod period, List<(double Start, double End)> attackList)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var times = new List<double>();
                double every = beat * DanmakusuBalance.AUNN_EVERY_BEATS;

                foreach (var (start, end) in attackList)
                {
                    if (end <= start || end <= period.ShootStart)
                        continue;

                    for (double t = Math.Max(start, period.ShootStart); t < end && times.Count < 100000; t += every)
                        times.Add(t);
                }

                volleyTimes = times.ToArray();
                volleyFrom = new Vector2[volleyTimes.Length];
                volleyAim = new float[volleyTimes.Length];
                volleyKnown = new bool[volleyTimes.Length];

                var aimList = new List<(TrajectoryOp, int)>();

                for (int ring = 0; ring < f.emitters.Length; ring++)
                {
                    var emitter = f.emitters[ring];

                    if (emitter.Ops == null || emitter.GoAt == null)
                        continue;

                    foreach (var op in emitter.Ops)
                    {
                        if (op.PlayerAim && op.Time >= period.Start - 1 && op.Time <= period.End + 1)
                            aimList.Add((op, ring));
                    }
                }

                aimList.Sort((a, b) => a.Item1.Time.CompareTo(b.Item1.Time));
                aims = aimList.ToArray();

                Start = period.ShootStart;
                End = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < volleyTimes.Length; k++)
                {
                    if (volleyTimes[k] > time)
                        volleyKnown[k] = false;
                }

                // (where the player is is placed again when the time of each move comes)
                nextAim = aims.Length;

                for (int k = 0; k < aims.Length; k++)
                {
                    if (aims[k].Op.Time > time)
                    {
                        aims[k].Op.Placed = false;
                        nextAim = Math.Min(nextAim, k);
                    }
                }

                shapes.Forget();
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the beams move on)

                if (f.failed)
                    return;

                // (every move whose time has come sets off at where the player is now, once)
                for (; nextAim < aims.Length && aims[nextAim].Op.Time <= now; nextAim++)
                {
                    var (op, ring) = aims[nextAim];

                    if (op.Placed)
                        continue;

                    op.Point = f.characterPosition;
                    op.Placed = true;
                    f.autoRingMoved(ring); // (that ring's way after it has changed: the autoplay looks at it again)
                }

                // (every volley whose time has come takes where she is and where the player is, once)
                for (int k = firstVolley(f, now); k < volleyTimes.Length && volleyTimes[k] <= now; k++)
                {
                    if (volleyKnown[k])
                        continue;

                    volleyKnown[k] = true;
                    volleyFrom[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - volleyFrom[k];
                    volleyAim[k] = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                }
            }

            // ---- the curve ----

            private float beamLength(BulletField f) => f.circleRadius * DanmakusuBalance.AUNN_BEAM_LENGTH_CIRCLES;

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.AUNN_BEAM_SPEED;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.AUNN_BEAM_WIDTH;

            private void ensurePath(BulletField f)
            {
                if (path != null)
                    return;

                float circle = Math.Max(f.circleRadius, 4f);
                float curl = circle * DanmakusuBalance.AUNN_CURL_CIRCLES, loopRadius = circle * DanmakusuBalance.AUNN_LOOP_CIRCLES;
                float loop = MathF.Tau * loopRadius;
                float diagonal = f.hasScreenBounds ? Vector2.Distance(f.screenMin, f.screenMax) : 1500f;

                pathLength = curl + loop + diagonal + beamLength(f);
                int count = (int)(pathLength / path_step) + 2;
                path = new Vector2[count];

                // (the way it heads: bending ever more over the curl - its bend grows evenly with the way gone - then as much as the loop's all round it, then not at all)
                float heading(float s) => s <= curl
                    ? s * s / (2f * curl * loopRadius)
                    : s <= curl + loop
                        ? curl / (2f * loopRadius) + (s - curl) / loopRadius
                        : curl / (2f * loopRadius) + MathF.Tau;

                Vector2 p = Vector2.Zero;

                for (int i = 0; i < count; i++)
                {
                    path[i] = p;
                    float h = heading((i + 0.5f) * path_step);
                    p += new Vector2(MathF.Cos(h), MathF.Sin(h)) * path_step;
                }
            }

            /// <summary>Where the curve is <paramref name="s"/> px along it (from its start, heading along +x).</summary>
            private Vector2 along(float s)
            {
                float x = Math.Clamp(s / path_step, 0f, path!.Length - 1.001f);
                int i = (int)x;
                return Vector2.Lerp(path[i], path[i + 1], x - i);
            }

            /// <summary>How long a volley is there: until the end of its beams has run off the end of the curve.</summary>
            private double life(BulletField f)
            {
                ensurePath(f);
                return (pathLength + beamLength(f)) / Math.Max(speed, 1e-4f);
            }

            /// <summary>The first volley that can still be there at <paramref name="t"/>.</summary>
            private int firstVolley(BulletField f, double t)
            {
                double span = life(f);
                int low = 0, high = volleyTimes.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (volleyTimes[middle] < t - span)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            /// <summary>The points of beam <paramref name="beam"/> of volley <paramref name="k"/> at <paramref name="t"/>, from its end to its head, into <paramref name="into"/>; false when it is not there.</summary>
            private bool body(BulletField f, int k, int beam, double t, List<Vector2> into)
            {
                into.Clear();

                if (!volleyKnown[k] || t < volleyTimes[k])
                    return false;

                ensurePath(f);
                float head = speed * (float)(t - volleyTimes[k]);
                float tail = Math.Max(head - beamLength(f), 0f);
                head = Math.Min(head, pathLength);

                if (head - tail < 1f)
                    return false;

                float aim = volleyAim[k] + beam * MathF.Tau / DanmakusuBalance.AUNN_BEAMS;
                float way = k % 2 == 0 ? 1f : -1f; // (every second volley curls the other way)
                float cos = MathF.Cos(aim), sin = MathF.Sin(aim);
                Vector2 from = volleyFrom[k];
                int steps = Math.Max((int)MathF.Ceiling((head - tail) / body_step), 1);

                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = along(tail + (head - tail) * i / steps);
                    p.Y *= way;
                    into.Add(from + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos));
                }

                return true;
            }

            private bool onScreen(BulletField f, List<Vector2> points, float margin)
            {
                if (!f.hasScreenBounds)
                    return true;

                foreach (var p in points)
                {
                    if (p.X >= f.screenMin.X - margin && p.X <= f.screenMax.X + margin && p.Y >= f.screenMin.Y - margin && p.Y <= f.screenMax.Y + margin)
                        return true;
                }

                return false;
            }

            // ---- drawn ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0 || volleyTimes.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 own = f.palette[style % f.palette.Length];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = halfWidth(f);

                for (int k = firstVolley(f, t); k < volleyTimes.Length && volleyTimes[k] <= t; k++)
                {
                    for (int b = 0; b < DanmakusuBalance.AUNN_BEAMS; b++)
                    {
                        if (!body(f, k, b, t, line) || !onScreen(f, line, width))
                            continue;

                        f.addPlainBeam(line, width, own, border, ref beams, ref pointTotal);
                    }
                }
            }

            // ---- what hurts ----

            /// <summary>The beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || volleyTimes.Length == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the beams of that moment are worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    for (int k = firstVolley(f, now); k < volleyTimes.Length && volleyTimes[k] <= now; k++)
                    {
                        for (int b = 0; b < DanmakusuBalance.AUNN_BEAMS; b++)
                        {
                            if (!body(f, k, b, now, line))
                                continue;

                            foreach (var p in line)
                                fresh.Point(p, reach);

                            fresh.EndRun();
                        }
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                for (int k = firstVolley(f, now); k < volleyTimes.Length && volleyTimes[k] <= now; k++)
                {
                    for (int b = 0; b < DanmakusuBalance.AUNN_BEAMS; b++)
                    {
                        if (!body(f, k, b, now, line))
                            continue;

                        if (Vector2.Distance(player, line[0]) <= reach)
                            return true;

                        for (int i = 1; i < line.Count; i++)
                        {
                            if (distanceToSegment(player, line[i - 1], line[i]) <= reach)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
