using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kurumi (2026-11-23): in her section every slider shoots a beam instead of bullets and every circle a ring of straight beams KURUMI_BEAM_BEATS long (the planner). Every one of those beams that
        // has left the screen altogether - its end, the last slice it fired, is out - shoots a fan of KURUMI_FAN_BULLETS basic bullets KURUMI_FAN_DEGREES wide back in from where its end left, square to
        // that edge of the screen, once.
        // A beam that nothing else steers flies straight: when and where its end leaves is worked out from it, so the fan comes at that moment wherever time is looked at from. One that another boss's ops
        // steer is looked at frame by frame, and fans when its end is seen out.
        // ======================================================================================================================================================

        private sealed class VampireAct : StageAct
        {
            // (her beams: the field's lasers of her section that a circle or a slider fires; and when each beam of each fanned, NaN while it has not)
            private readonly int[] watched;
            private readonly double[][] fanned;

            public VampireAct(BulletField field, BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;

                var list = new List<int>();
                double end = period.End;

                for (int i = 0; i < field.lasers.Length; i++)
                {
                    var laser = field.lasers[i];

                    if (laser.SpinnerLike || laser.FollowsBoss || laser.Rotates || laser.BallPositions.Length == 0 || laser.StartTime < period.ShootStart - 1 || laser.StartTime >= period.End)
                        continue;

                    list.Add(i);
                    end = Math.Max(end, laser.EndTime + laser.Lifetime);
                }

                watched = list.ToArray();
                fanned = new double[watched.Length][];

                for (int k = 0; k < watched.Length; k++)
                {
                    fanned[k] = new double[field.lasers[watched[k]].BeamCount];
                    Array.Fill(fanned[k], double.NaN);
                }

                End = end;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();

                foreach (var beams in fanned)
                {
                    for (int b = 0; b < beams.Length; b++)
                    {
                        if (beams[b] > time)
                            beams[b] = double.NaN;
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                Vector2 low = f.stageMin, high = f.stageMax;

                for (int k = 0; k < watched.Length; k++)
                {
                    var laser = f.lasers[watched[k]];

                    if (now < laser.EndTime)
                        continue; // (its end has not been fired yet)

                    Vector2 last = laser.BallPositions[^1];
                    bool straight = laser.Ops == null && laser.Portals == null && laser.Clock == null;
                    float pad = laser.HalfWidth * 2f;

                    for (int b = 0; b < laser.BeamCount; b++)
                    {
                        if (!double.IsNaN(fanned[k][b]) || laser.Directions[b].LengthSquared < 0.5f)
                            continue;

                        Vector2 velocity = laser.Directions[b] * laser.Speed;
                        Vector2 leftAt;
                        double when;
                        Vector2 inward;

                        if (straight)
                        {
                            // (its end flies straight: when it is a beam's width past the edge, and where it crossed the edge)
                            if (!leaves(last, laser.Directions[b], low - new Vector2(pad), high + new Vector2(pad), out float gone, out _)
                                || !leaves(last, laser.Directions[b], low, high, out float edge, out inward))
                                continue;

                            when = laser.EndTime + gone / Math.Max(laser.Speed, 1e-4f);

                            if (when > now)
                                continue;

                            leftAt = last + laser.Directions[b] * edge;
                        }
                        else
                        {
                            laser.TraceSlice(last, velocity, laser.EndTime, now, out Vector2 end, out _);

                            if (end.X >= low.X - pad && end.X <= high.X + pad && end.Y >= low.Y - pad && end.Y <= high.Y + pad)
                                continue;

                            leftAt = Vector2.Clamp(end, low, high);
                            inward = end.X < low.X ? Vector2.UnitX : end.X > high.X ? -Vector2.UnitX : end.Y < low.Y ? Vector2.UnitY : -Vector2.UnitY;
                            when = now;
                        }

                        fanned[k][b] = when;
                        fan(f, leftAt, inward, when, laser.ComboIndex + b);
                    }
                }
            }

            /// <summary>Where a line from <paramref name="from"/> going <paramref name="way"/> leaves the box (how far along), and the way back into it square to the side it leaves by (false: it starts outside or never leaves).</summary>
            private static bool leaves(Vector2 from, Vector2 way, Vector2 low, Vector2 high, out float distance, out Vector2 inward)
            {
                distance = float.PositiveInfinity;
                inward = Vector2.Zero;

                if (from.X < low.X || from.X > high.X || from.Y < low.Y || from.Y > high.Y)
                    return false;

                if (way.X > 1e-6f && (high.X - from.X) / way.X < distance) { distance = (high.X - from.X) / way.X; inward = -Vector2.UnitX; }
                if (way.X < -1e-6f && (low.X - from.X) / way.X < distance) { distance = (low.X - from.X) / way.X; inward = Vector2.UnitX; }
                if (way.Y > 1e-6f && (high.Y - from.Y) / way.Y < distance) { distance = (high.Y - from.Y) / way.Y; inward = -Vector2.UnitY; }
                if (way.Y < -1e-6f && (low.Y - from.Y) / way.Y < distance) { distance = (low.Y - from.Y) / way.Y; inward = Vector2.UnitY; }

                return !float.IsInfinity(distance);
            }

            /// <summary>A fan of basic bullets back into the screen from <paramref name="at"/>, round <paramref name="inward"/>, from <paramref name="t"/> on.</summary>
            private void fan(BulletField f, Vector2 at, Vector2 inward, double t, int style)
            {
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.KURUMI_FAN_SIZE; // (half a plain one since 2026-11-24)
                float speed = stageBulletSpeed(Period);
                float spread = DanmakusuBalance.KURUMI_FAN_DEGREES * MathF.PI / 180f;
                float centre = MathF.Atan2(inward.Y, inward.X);
                int count = DanmakusuBalance.KURUMI_FAN_BULLETS;

                for (int i = 0; i < count; i++)
                {
                    float angle = centre + spread * (count > 1 ? i / (count - 1f) - 0.5f : 0f);
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(style, false, Period.Rank),
                        Shape = BulletShape.Circle,
                        Until = t + f.stageLife(velocity),
                    });
                }
            }
        }
    }
}
