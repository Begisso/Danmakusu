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
        // Sariel (2026-11-27). In her attacks she shoots a ring of SARIEL_RING_BATS bats every SARIEL_RING_EVERY_BEATS: Koakuma's bats, flying and turning half as fast as hers (SARIEL_BAT_SLOW), for
        // SARIEL_BAT_LIFE_BEATS, homing as Koakuma's do for the first KOAKUMA_HOMING_BEATS; every beam fired in her section that has left the screen (its end out) lets one of Koakuma's own bats in where
        // it left, flying back in (SARIEL_BATS_MOST bats at once at most). Every bat shoots a basic bullet at the player every SARIEL_STREAM_BEATS while it is on the screen.
        // All through her section every SARIEL_WRAP_EVERY-th bullet of a ring (by a hash: anyone's) that leaves the screen by the bottom comes in again at the top, half as big, flying on as it flew, and
        // so SARIEL_WRAPS times in all; one that leaves by the top comes in at the bottom.
        // After her last section on the map her last word: everything flying turns dark purple (BossOps: a turn by nothing with the look), the screen dims (SARIEL_DIM, fading out over SARIEL_DIM_BEATS),
        // and SARIEL_WARN_BEATS later everything, bullets and beams, turns at where the player is then and flies through it (an aim her act places), warned of by red arrows beside every bullet.
        // The bats cannot be written as a function of time: the act moves them step by step, as Koakuma's.
        // ======================================================================================================================================================

        private sealed class AngelOfDeathAct : StageAct
        {
            private const double step_ms = DanmakusuBalance.HOMING_STEP_MS;

            private sealed class Bat
            {
                public int Index;
                public Vector2 Position, Velocity;
                public double Time, Born, NextShot;
                public float Side, Gravity, Radius, Phase;
                public bool Gone;
                public List<Vector2> Trail = null!;
            }

            private readonly double beat;
            private readonly bool last;
            private readonly double[] rings;
            private readonly bool[] ringMade;
            private readonly List<Bat> bats = new List<Bat>();
            private readonly Dictionary<int, Bat> batOf = new Dictionary<int, Bat>();
            private readonly int[] watched;
            private readonly double[][] spawned;
            private readonly List<TrajectoryOp> aims = new List<TrajectoryOp>();

            public AngelOfDeathAct(BulletField field, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                last = BossOps.IsLastOfHers(field.bosses, period);

                var list = new List<double>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = Math.Max(start, period.ShootStart); t < end && list.Count < 4000; t += beat * DanmakusuBalance.SARIEL_RING_EVERY_BEATS)
                        list.Add(t);
                }

                rings = list.ToArray();
                ringMade = new bool[rings.Length];

                var beams = new List<int>();

                for (int i = 0; i < field.lasers.Length; i++)
                {
                    var laser = field.lasers[i];

                    if (laser.SpinnerLike || laser.FollowsBoss || laser.Rotates || laser.BallPositions.Length == 0 || laser.StartTime < period.ShootStart - 1 || laser.StartTime >= period.End)
                        continue;

                    beams.Add(i);
                }

                watched = beams.ToArray();
                spawned = new double[watched.Length][];

                for (int k = 0; k < watched.Length; k++)
                {
                    spawned[k] = new double[field.lasers[watched[k]].BeamCount];
                    Array.Fill(spawned[k], double.NaN);
                }

                if (last)
                {
                    foreach (var op in field.bulletOps)
                    {
                        if (op.Kind == TrajectoryOpKind.Aim && ReferenceEquals(op.After, BossOps.SarielPurple) && op.Time >= period.End - 1 && op.Time <= period.End + beat * DanmakusuBalance.SARIEL_WARN_BEATS + 1)
                            aims.Add(op);
                    }
                }

                Start = period.ShootStart;
                End = period.End + beat * (DanmakusuBalance.SARIEL_BAT_LIFE_BEATS + DanmakusuBalance.SARIEL_DIM_BEATS + 8);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override double MechanicsEnd => last ? Period.End + beat * DanmakusuBalance.SARIEL_DIM_BEATS : Period.End;

            public override void Reset(BulletField f, double time)
            {
                bats.Clear();
                batOf.Clear();
                ClearBullets();
                Array.Fill(ringMade, false);

                foreach (var beams in spawned)
                    Array.Fill(beams, double.NaN);

                foreach (var op in aims)
                {
                    if (op.Time > time)
                        op.Placed = false;
                }

                resetTime = time;
            }

            private double resetTime = double.NegativeInfinity;

            private float batSpeed => stageBulletSpeed(Period) * DanmakusuBalance.BYAKUREN_TRAIL_SPEED;

            private float batGravity => Period.ArrowGravity * DanmakusuBalance.KOAKUMA_BAT_TURN_FACTOR;

            private void addBat(BulletField f, double t, Vector2 at, Vector2 velocity, float gravity, int style)
            {
                if (f.failed || bats.Count - goneCount() >= DanmakusuBalance.SARIEL_BATS_MOST)
                    return;

                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.SARIEL_BAT_SIZE; // (twice as big since 2026-11-28, "all her bats have double the size": what they hit with too)
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = velocity,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = f.comboStyle(style, false, Period.Rank),
                    Shape = BulletShape.Circle,
                    Until = t + beat * DanmakusuBalance.SARIEL_BAT_LIFE_BEATS,
                    Trail = new List<Vector2>(128) { at },
                    TrailStep = step_ms,
                    Hidden = true,
                });

                if (Bullets.Count == index)
                    return;

                var made = new Bat
                {
                    Index = index, Position = at, Velocity = velocity, Time = t, Born = t, NextShot = t + beat * DanmakusuBalance.SARIEL_STREAM_BEATS * 0.5,
                    Gravity = gravity, Radius = radius, Phase = hash01(index, 3, 811), Trail = Bullets[index].Trail!,
                };

                bats.Add(made);
                batOf[index] = made;
            }

            private int goneCount()
            {
                int n = 0;

                foreach (var bat in bats)
                {
                    if (bat.Gone)
                        n++;
                }

                return n;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // her rings of slow bats
                Vector2 from = f.bossPositionOf(Period);

                for (int r = 0; r < rings.Length && rings[r] <= now; r++)
                {
                    if (ringMade[r])
                        continue;

                    ringMade[r] = true;

                    if (rings[r] < resetTime - 1 && now - rings[r] > beat * DanmakusuBalance.SARIEL_BAT_LIFE_BEATS)
                        continue;

                    float phase = r * 0.61803f * MathF.Tau / DanmakusuBalance.SARIEL_RING_BATS;

                    for (int i = 0; i < DanmakusuBalance.SARIEL_RING_BATS; i++)
                    {
                        float angle = phase + i * MathF.Tau / DanmakusuBalance.SARIEL_RING_BATS;
                        addBat(f, rings[r], from, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (batSpeed * DanmakusuBalance.SARIEL_BAT_SLOW), batGravity * DanmakusuBalance.SARIEL_BAT_SLOW, r + i);
                    }
                }

                watchBeams(f, now);
                stepBats(f, now);

                // her last word: the screen dims, and the aim is placed at where the player is
                if (last && now >= Period.End)
                {
                    double u = (now - Period.End) / (beat * DanmakusuBalance.SARIEL_DIM_BEATS);

                    if (u < 1)
                        f.sarielDim = Math.Max(f.sarielDim, DanmakusuBalance.SARIEL_DIM * smoothStep(u / 0.03) * (1f - smoothStep(u)));

                    foreach (var op in aims)
                    {
                        if (op.Placed || op.Time > now)
                            continue;

                        op.Point = f.characterPosition;
                        op.Placed = true;
                        f.autoOpsMoved = true;
                    }
                }
            }

            /// <summary>Every beam of her section whose end has left the screen lets a bat in where it left (as Kurumi's fans: see VampireAct).</summary>
            private void watchBeams(BulletField f, double now)
            {
                Vector2 low = f.stageMin, high = f.stageMax;

                for (int k = 0; k < watched.Length; k++)
                {
                    var laser = f.lasers[watched[k]];

                    if (now < laser.EndTime || now > laser.EndTime + laser.Lifetime + 2000)
                        continue;

                    Vector2 lastBall = laser.BallPositions[^1];
                    bool straight = laser.Ops == null && laser.Portals == null && laser.Clock == null;
                    float pad = laser.HalfWidth * 2f;

                    for (int b = 0; b < laser.BeamCount; b++)
                    {
                        if (!double.IsNaN(spawned[k][b]) || laser.Directions[b].LengthSquared < 0.5f)
                            continue;

                        Vector2 leftAt, inward;
                        double when;

                        if (straight)
                        {
                            if (!VampireAct.leaves(lastBall, laser.Directions[b], low - new Vector2(pad), high + new Vector2(pad), out float gone, out _)
                                || !VampireAct.leaves(lastBall, laser.Directions[b], low, high, out float edge, out inward))
                            {
                                spawned[k][b] = double.NegativeInfinity;
                                continue;
                            }

                            when = laser.EndTime + gone / Math.Max(laser.Speed, 1e-4f);

                            if (when > now)
                                continue;

                            leftAt = lastBall + laser.Directions[b] * edge;
                        }
                        else
                        {
                            laser.TraceSlice(lastBall, laser.Directions[b] * laser.Speed, laser.EndTime, now, out Vector2 end, out _);

                            if (end.X >= low.X - pad && end.X <= high.X + pad && end.Y >= low.Y - pad && end.Y <= high.Y + pad)
                                continue;

                            leftAt = Vector2.Clamp(end, low, high);
                            inward = end.X < low.X ? Vector2.UnitX : end.X > high.X ? -Vector2.UnitX : end.Y < low.Y ? Vector2.UnitY : -Vector2.UnitY;
                            when = now;
                        }

                        spawned[k][b] = when;

                        if (now - when < beat * DanmakusuBalance.SARIEL_BAT_LIFE_BEATS)
                            addBat(f, when, leftAt, inward * batSpeed, batGravity, laser.ComboIndex + b);
                    }
                }
            }

            private void stepBats(BulletField f, double now)
            {
                Vector2 player = f.characterPosition;
                float step = (float)step_ms;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.SARIEL_STREAM_SPEED; // (twice as fast since 2026-11-27)
                float radius = f.stageBaseRadius(Period);
                double every = beat * DanmakusuBalance.SARIEL_STREAM_BEATS;

                foreach (var bat in bats)
                {
                    int guard = 0;

                    while (!bat.Gone && bat.Time + step_ms <= now && guard++ < 600)
                    {
                        bool homing = bat.Time - bat.Born < beat * DanmakusuBalance.KOAKUMA_HOMING_BEATS;

                        if (homing && bat.Velocity.LengthSquared > 1e-12f)
                            bat.Velocity = BatAct.steer(bat.Position, bat.Velocity, player, step, bat.Gravity, bat.Index, ref bat.Side);

                        bat.Position += bat.Velocity * step;
                        bat.Time += step_ms;
                        bat.Trail.Add(bat.Position);

                        bool on = bat.Position.X >= f.stageMin.X && bat.Position.X <= f.stageMax.X && bat.Position.Y >= f.stageMin.Y && bat.Position.Y <= f.stageMax.Y;

                        // (it streams basic bullets at the player while it is on the screen)
                        if (bat.Time >= bat.NextShot)
                        {
                            bat.NextShot += every;

                            if (on && bat.Time < Bullets[bat.Index].Until)
                            {
                                Vector2 way = player - bat.Position;
                                way = way.LengthSquared > 1e-4f ? Vector2.Normalize(way) : Vector2.UnitY;
                                Vector2 velocity = way * speed;

                                AddBullet(new StageBullet
                                {
                                    T0 = bat.Time,
                                    P0 = bat.Position,
                                    V = velocity,
                                    Radius = radius,
                                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                    Style = Bullets[bat.Index].Style,
                                    Shape = BulletShape.Circle,
                                    Until = bat.Time + f.stageLife(velocity),
                                });
                            }
                        }

                        if (bat.Time >= Bullets[bat.Index].Until)
                        {
                            bat.Gone = true;
                            break;
                        }

                        Vector2 lo = f.stageMin - new Vector2(bat.Radius * 2f), hi = f.stageMax + new Vector2(bat.Radius * 2f);

                        if (!homing && (bat.Position.X < lo.X || bat.Position.X > hi.X || bat.Position.Y < lo.Y || bat.Position.Y > hi.Y))
                        {
                            bat.Gone = true;
                            var bullet = Bullets[bat.Index];
                            bullet.Until = Math.Min(bullet.Until, bat.Time);
                            Bullets[bat.Index] = bullet;
                        }
                    }
                }
            }

            /// <summary>(the autoplay) Where a bat will be: its trail, then step by step homing on <paramref name="player"/>, as the act moves it.</summary>
            public override bool ForeseePath(BulletField f, int index, Vector2 player, double[] times, Vector2[] into)
            {
                if (f.failed || !batOf.TryGetValue(index, out var bat) || bat.Gone)
                    return false;

                var bullet = Bullets[index];
                float step = (float)step_ms;
                Vector2 position = bat.Position, velocity = bat.Velocity;
                double time = bat.Time;
                float side = bat.Side;

                for (int s = 0; s < times.Length && s < into.Length; s++)
                {
                    double t = times[s];

                    if (t <= time)
                    {
                        into[s] = stageBulletPosition(bullet, t);
                        continue;
                    }

                    int guard = 0;

                    while (time + step_ms <= t && guard++ < 200)
                    {
                        if (time - bat.Born < beat * DanmakusuBalance.KOAKUMA_HOMING_BEATS)
                            velocity = BatAct.steer(position, velocity, player, step, bat.Gravity, bat.Index, ref side);

                        position += velocity * step;
                        time += step_ms;
                    }

                    into[s] = position + velocity * (float)(t - time);
                }

                return true;
            }

            // ---- every so many bullets that leave by the top or the bottom come in at the other side ----

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Sharp || emitter.Homing || to.X < f.stageMin.X || to.X > f.stageMax.X)
                    return false;

                bool down = from.Y <= f.stageMax.Y && to.Y > f.stageMax.Y;
                bool up = from.Y >= f.stageMin.Y && to.Y < f.stageMin.Y;

                if (!down && !up)
                    return false;

                if (hash01(Id, (int)(Math.Abs((long)emitter.Time) % 1000000) * 64 + bullet, 907) * DanmakusuBalance.SARIEL_WRAP_EVERY >= 1f)
                    return false;

                var stored = f.makeStored(emitter, t);
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                Vector2 velocity = (to - from) / (float)dt;

                if (MathF.Abs(velocity.Y) < 1e-4f)
                    return false;

                float big = emitter.BigScale(bullet) * 0.5f;
                float height = f.stageMax.Y - f.stageMin.Y;
                Vector2 at = new Vector2(to.X, down ? to.Y - height : to.Y + height);
                double time = t;
                int count = Bullets.Count;

                for (int wrap = 0; wrap < DanmakusuBalance.SARIEL_WRAPS; wrap++)
                {
                    // (it flies on until it leaves again by the side it was going to: then the next comes in)
                    double across = down ? (f.stageMax.Y - at.Y) / velocity.Y : (f.stageMin.Y - at.Y) / velocity.Y;
                    double life = Math.Min(Math.Max(across, 0), f.stageLife(velocity));

                    AddBullet(new StageBullet
                    {
                        T0 = time,
                        P0 = at,
                        V = velocity,
                        Radius = stored.Radius * big,
                        Hit = stored.Hit * big,
                        Style = stored.Style,
                        Shape = stored.Shape,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Blades = stored.Blades,
                        BladeSpin = stored.BladeSpin,
                        Untraced = true,
                        Until = time + life,
                    });

                    if (life < across - 1)
                        break; // (it left by a side)

                    time += across;
                    at = new Vector2(at.X + velocity.X * (float)across, down ? at.Y + velocity.Y * (float)across - height : at.Y + velocity.Y * (float)across + height);

                    if (at.X < f.stageMin.X || at.X > f.stageMax.X)
                        break;
                }

                return Bullets.Count > count;
            }

            // ---- what it looks like ----

            private bool alive(BulletField f, Bat bat, double t)
            {
                var bullet = Bullets[bat.Index];

                if (t < bullet.T0 || t >= bullet.Until || (bat.Gone && t > bat.Time))
                    return false;

                return !(DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + bat.Index));
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                foreach (var bat in bats)
                {
                    if (!alive(f, bat, t))
                        continue;

                    var bullet = Bullets[bat.Index];
                    Vector2 position = stageBulletPosition(bullet, t);
                    Vector2 velocity = stageBulletVelocity(bullet, t);
                    float heading = velocity.LengthSquared > 1e-12f ? MathF.Atan2(velocity.Y, velocity.X) * 180f / MathF.PI + 90f : 0f;
                    float wing = bat.Radius * DanmakusuBalance.KOAKUMA_BAT_SIZE_FACTOR;
                    float flap = (float)((t - bullet.T0) / DanmakusuBalance.KOAKUMA_BAT_FLAP_MS) + bat.Phase;
                    int frame = (int)MathF.Floor((flap - MathF.Floor(flap)) * StageLayer.BatFrames) % StageLayer.BatFrames;
                    float fade = 1f - smoothStep((t - (bullet.Until - 300)) / 300.0);

                    f.stageBats.Add(StageLayer.BatFrame(frame), position, new Vector2(2f * StageLayer.BatExtent * wing), heading, fade, Color4.White);
                }
            }
        }
    }
}
