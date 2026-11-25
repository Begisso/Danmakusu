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
        // Narumi Yatadera (2026-11-24): no pauses. Every ring fired in her section flies at NARUMI_SLOW of its speed (the planner's, BulletEmitter.WaveRestores). Every NARUMI_WAVE_EVERY_BEATS she sends out a
        // wave like Raiko's ripples, a circle that grows from where she is at NARUMI_WAVE_SPEED times a plain bullet's speed; a bullet of those that it goes over gets its own speed back, flying on the way it
        // went (a push, once). It does not hurt.
        // Every NARUMI_BALL_EVERY_BEATS she throws a big ball (NARUMI_BALL_CIRCLES circle radii) at the player that for NARUMI_HOMING_BEATS turns towards them (NARUMI_TURN_DEGREES a beat at most), then
        // flies straight on out; every NARUMI_RING_EVERY_BEATS of its homing it shoots a ring of NARUMI_RING_BULLETS basic bullets, which start at her half speed too and get theirs back from her waves.
        // (2026-11-24: the bullets start at a quarter of their speed, a wave every four beats, twice as fast, and what it goes over flies at NARUMI_BOOST times its own speed - its own before. 2026-11-25,
        // "initial bullets speed 0.25x, wave speed buff 2x": what a wave goes over flies NARUMI_BOOST times as fast as it was flying - half its own speed.)
        // Where a ball goes depends on where the player has been: it is followed step by step (NARUMI_STEP_MS) and kept; going back in time takes the steps back. Where a wave starts is taken when it is sent.
        // ======================================================================================================================================================

        private sealed class StatueAct : StageAct
        {
            private sealed class Ball
            {
                public double T;
                public readonly List<Vector2> Steps = new List<Vector2>();
                public Vector2 Velocity;
                public int Index = -1; // (its bullet in Bullets, once made)
                public int Rings;
                public bool Out; // (it has left the screen: no more steps)
            }

            private readonly double beat;
            private readonly double[] waves;
            private readonly Vector2[] centres;
            private readonly bool[] known;
            private readonly double[] throwTimes;
            private readonly List<Ball> balls = new List<Ball>();
            private readonly Dictionary<(BulletEmitter, int), double> restored = new Dictionary<(BulletEmitter, int), double>();
            private readonly List<int> own = new List<int>(); // (the bullets of the balls' rings in Bullets: they get their speed back here)

            public StatueAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var list = new List<double>();

                for (double t = Start; t < period.End; t += beat * DanmakusuBalance.NARUMI_WAVE_EVERY_BEATS)
                    list.Add(t);

                waves = list.ToArray();
                centres = new Vector2[waves.Length];
                known = new bool[waves.Length];

                list.Clear();

                for (double t = Start; t < period.End; t += beat * DanmakusuBalance.NARUMI_BALL_EVERY_BEATS)
                    list.Add(t);

                throwTimes = list.ToArray();
                End = period.End + waveLife(f) + beat * 16;
                AbsorbFrom = Start;
                AbsorbTo = (waves.Length > 0 ? waves[^1] : Start) + waveLife(f);
            }

            private float waveSpeed => stageBulletSpeed(Period) * DanmakusuBalance.NARUMI_WAVE_SPEED;

            /// <summary>How long a wave lasts: until it has gone past the farthest corner of the screen from anywhere.</summary>
            private double waveLife(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / Math.Max(waveSpeed, 1e-4f);

            private double homing => beat * DanmakusuBalance.NARUMI_HOMING_BEATS;

            private float ballRadius(BulletField f) => f.circleRadius * DanmakusuBalance.NARUMI_BALL_CIRCLES;

            public override double MechanicsEnd => throwTimes.Length > 0 ? throwTimes[^1] + homing : double.NegativeInfinity;

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < waves.Length; k++)
                {
                    if (waves[k] > time)
                        known[k] = false;
                }

                var gone = new List<(BulletEmitter, int)>();

                foreach (var (key, at) in restored)
                {
                    if (at > time)
                        gone.Add(key);
                }

                foreach (var key in gone)
                    restored.Remove(key);

                // (back to the step at the time gone back to; its bullet and its rings are made again)
                balls.RemoveAll(b => b.T > time);

                foreach (var ball in balls)
                {
                    int keep = Math.Max(1, (int)Math.Floor((time - ball.T) / DanmakusuBalance.NARUMI_STEP_MS) + 1);

                    if (ball.Steps.Count > keep)
                    {
                        ball.Steps.RemoveRange(keep, ball.Steps.Count - keep);

                        if (ball.Steps.Count >= 2)
                            ball.Velocity = (ball.Steps[^1] - ball.Steps[^2]) / (float)DanmakusuBalance.NARUMI_STEP_MS;
                    }

                    ball.Index = -1;
                    ball.Rings = 0;
                    ball.Out = false;
                }

                ClearBullets();
                own.Clear();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (where each wave starts: where she is as it is sent)
                for (int k = 0; k < waves.Length; k++)
                {
                    if (now < waves[k])
                        break;

                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = f.bossVisualOf(Period);
                    }
                }

                // (every ball whose time has come sets off at the player)
                while (balls.Count < throwTimes.Length && throwTimes[balls.Count] <= now)
                {
                    Vector2 from = f.bossPositionOf(Period);
                    Vector2 way = f.characterPosition - from;
                    var ball = new Ball { T = throwTimes[balls.Count], Velocity = (way.LengthSquared > 1e-6f ? Vector2.Normalize(way) : Vector2.UnitY) * ballSpeed };
                    ball.Steps.Add(from);
                    balls.Add(ball);
                }

                float radius = ballRadius(f);
                float turn = DanmakusuBalance.NARUMI_TURN_DEGREES * MathF.PI / 180f / (float)beat * (float)DanmakusuBalance.NARUMI_STEP_MS;
                double step = DanmakusuBalance.NARUMI_STEP_MS;

                foreach (var ball in balls)
                {
                    // (step by step until now: towards the player while it homes, straight on after; until it has left the screen)
                    while (!ball.Out && ball.T + ball.Steps.Count * step <= now)
                    {
                        Vector2 at = ball.Steps[^1];
                        double age = (ball.Steps.Count - 1) * step;

                        if (age < homing)
                        {
                            Vector2 toPlayer = f.characterPosition - at;
                            float heading = MathF.Atan2(ball.Velocity.Y, ball.Velocity.X);

                            if (toPlayer.LengthSquared > 1f)
                                heading += Math.Clamp(MathF.IEEERemainder(MathF.Atan2(toPlayer.Y, toPlayer.X) - heading, MathF.Tau), -turn, turn);

                            ball.Velocity = new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * ballSpeed;
                        }

                        Vector2 next = at + ball.Velocity * (float)step;
                        ball.Steps.Add(next);

                        if (next.X < f.stageMin.X - radius || next.X > f.stageMax.X + radius || next.Y < f.stageMin.Y - radius || next.Y > f.stageMax.Y + radius)
                            ball.Out = true;
                    }

                    // (the ball itself: a big bullet that goes along its steps, drawn here)
                    if (ball.Index < 0)
                    {
                        ball.Index = Bullets.Count;

                        AddBullet(new StageBullet
                        {
                            T0 = ball.T,
                            P0 = ball.Steps[0],
                            V = ball.Velocity,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = f.comboStyle(0, true, Period.Rank),
                            Shape = BulletShape.Circle,
                            Trail = ball.Steps,
                            TrailStep = step,
                            Until = ball.T + DanmakusuBalance.LIFE_MOST_BEATS * beat,
                            Hidden = true,
                        });

                        if (Bullets.Count <= ball.Index)
                            ball.Index = int.MaxValue; // (no room)
                    }

                    if (ball.Index < Bullets.Count && ball.Out)
                    {
                        var b = Bullets[ball.Index];

                        if (b.Until > ball.T + (ball.Steps.Count - 1) * step)
                        {
                            b.Until = ball.T + (ball.Steps.Count - 1) * step;
                            Bullets[ball.Index] = b;
                        }
                    }

                    // (its rings, while it homes and is there)
                    for (int m = ball.Rings + 1; ball.Index < Bullets.Count && !consumed(f, ball); m++)
                    {
                        double at = ball.T + m * beat * DanmakusuBalance.NARUMI_RING_EVERY_BEATS;

                        if (at > now || at > ball.T + homing + 1)
                            break;

                        shootRing(f, ball, m, at);
                        ball.Rings = m;
                    }
                }

                // (the waves give the rings' bullets their speed back: where a wave meets one that flies straight is worked out, not looked for frame by frame)
                for (int i = 0; i < own.Count; i++)
                {
                    var b = Bullets[own[i]];

                    if (b.Legs != null || b.Until < now)
                        continue;

                    double best = double.PositiveInfinity;

                    for (int k = 0; k < waves.Length && waves[k] <= now; k++)
                    {
                        if (known[k] && meets(b.P0, b.V, b.T0, centres[k], waves[k], waveSpeed, out double when) && when <= now)
                            best = Math.Min(best, when);
                    }

                    if (double.IsPositiveInfinity(best))
                        continue;

                    Vector2 there = b.P0 + b.V * (float)(best - b.T0);
                    Vector2 v = b.V * DanmakusuBalance.NARUMI_BOOST; // (twice the speed it has: 2026-11-25)
                    b.Legs = new[] { new StageLeg { T = b.T0, P = b.P0, V = b.V }, new StageLeg { T = best, P = there, V = v } }; // (before its first leg a bullet stands: the slow one is a leg too)
                    b.Until = best + f.stageLife(v);
                    Bullets[own[i]] = b;
                }
            }

            private float ballSpeed => stageBulletSpeed(Period) * DanmakusuBalance.NARUMI_BALL_SPEED;

            private bool consumed(BulletField f, Ball ball) => DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + ball.Index);

            /// <summary>Where the ball is at <paramref name="t"/>, along its steps.</summary>
            private static Vector2 ballAt(Ball ball, double t)
            {
                double u = (t - ball.T) / DanmakusuBalance.NARUMI_STEP_MS;

                if (u <= 0)
                    return ball.Steps[0];

                int i = (int)Math.Floor(u);
                return i + 1 < ball.Steps.Count ? Vector2.Lerp(ball.Steps[i], ball.Steps[i + 1], (float)(u - i)) : ball.Steps[^1];
            }

            private void shootRing(BulletField f, Ball ball, int m, double at)
            {
                Vector2 from = ballAt(ball, at);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.NARUMI_SLOW;
                float radius = f.stageBaseRadius(Period);
                int count = DanmakusuBalance.NARUMI_RING_BULLETS;
                float phase = m * MathF.PI / count;

                for (int n = 0; n < count; n++)
                {
                    float angle = phase + n * MathF.Tau / count;
                    Vector2 v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                    int index = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = at,
                        P0 = from,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(m + n, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Until = at + f.stageLife(v),
                    });

                    if (Bullets.Count > index)
                        own.Add(index);
                }
            }

            /// <summary>
            /// When a wave sent at <paramref name="sent"/> from <paramref name="centre"/>, growing at <paramref name="speed"/>, first goes over a bullet that flies from <paramref name="p0"/> at <paramref name="v"/> from
            /// <paramref name="t0"/> on (false: it never does - the bullet was behind it when it was fired, or outruns it).
            /// </summary>
            private static bool meets(Vector2 p0, Vector2 v, double t0, Vector2 centre, double sent, float speed, out double when)
            {
                when = double.PositiveInfinity;
                double from = Math.Max(t0, sent);
                Vector2 d = p0 + v * (float)(from - t0) - centre; // (where the bullet is, from the middle, when both are there)
                float r0 = speed * (float)(from - sent);

                if (d.Length <= r0)
                    return false; // (behind the wave already)

                // (|d + v s| = r0 + speed s: (v.v - speed^2) s^2 + 2 (d.v - r0 speed) s + d.d - r0^2 = 0, the first root past 0)
                double a = Vector2.Dot(v, v) - speed * speed;
                double b = 2 * (Vector2.Dot(d, v) - r0 * speed);
                double c = Vector2.Dot(d, d) - r0 * r0;

                if (a >= -1e-9)
                    return false; // (it is as fast as the wave or faster)

                double disc = b * b - 4 * a * c;

                if (disc < 0)
                    return false;

                double s = (-b - Math.Sqrt(disc)) / (2 * a);

                if (s < 0)
                    return false;

                when = from + s;
                return true;
            }

            /// <summary>A bullet of the rings fired in her section that a wave goes over gets its own speed back, flying on the way it went (once).</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                if (f.failed || !emitter.WaveRestores || !emitter.CanBeDeflected || restored.ContainsKey((emitter, bullet)))
                    return false;

                double before = Math.Min(ScanPrevious, t);

                for (int k = 0; k < waves.Length && waves[k] <= t; k++)
                {
                    if (!known[k])
                        continue;

                    float now = waveSpeed * (float)(t - waves[k]);
                    float then = waveSpeed * (float)Math.Max(before - waves[k], 0);

                    if (now > Vector2.Distance(f.stageMin, f.stageMax) + 50f)
                        continue; // (long gone)

                    if (Vector2.Distance(to, centres[k]) > now || Vector2.Distance(from, centres[k]) <= then)
                        continue; // (not gone over in this step)

                    Vector2 way = to - from;
                    direction = way.LengthSquared > 1e-8f ? Vector2.Normalize(way) : emitter.Directions[bullet].LengthSquared > 1e-8f ? Vector2.Normalize(emitter.Directions[bullet]) : Vector2.UnitX;
                    speed = emitter.SpeedOf(bullet) * DanmakusuBalance.NARUMI_BOOST; // (twice the speed it has, a quarter of its own: half of its own - 2026-11-25; twice its own on 2026-11-24)
                    restored[(emitter, bullet)] = t;
                    return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                // (her waves: a soft green ring, fading as it grows)
                var green = new Color4(0.55f, 0.92f, 0.6f, 1f);
                float half = Math.Max(f.circleRadius * DanmakusuBalance.NARUMI_WAVE_WIDTH, 1.5f);
                double life = waveLife(f);

                for (int k = 0; k < waves.Length && waves[k] <= t; k++)
                {
                    if (!known[k] || t > waves[k] + life)
                        continue;

                    float r = waveSpeed * (float)(t - waves[k]);
                    float alpha = 0.7f * (1f - smoothStep((t - waves[k]) / life)) * smoothStep((t - waves[k]) / 120.0);

                    if (alpha > 0.01f && r > half)
                        f.stageRipple.AddRing(centres[k], r, half, alpha, green);
                }

                // (her balls: grey stone, with her red)
                foreach (var ball in balls)
                {
                    if (ball.Index >= Bullets.Count || t < ball.T || t > Bullets[ball.Index].Until || consumed(f, ball))
                        continue;

                    if (f.stageBulletAt(Bullets[ball.Index], t, out Vector2 at))
                        mokouOrb(f, at, ballRadius(f), f.palette.Length > 0 ? f.palette[0] : Color4.White, false, new Color4(0.62f, 0.6f, 0.64f, 1f));
                }
            }
        }
    }
}
