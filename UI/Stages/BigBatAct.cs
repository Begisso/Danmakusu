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
        // Elis (2026-11-27): every ELIS_EVERY_BEATS of her section (from when she shoots) she flashes red for ELIS_WARN_BEATS, then turns into a big bat (a Koakuma bat ELIS_BAT_CIRCLES circle radii across
        // the wing, red) and for ELIS_FLY_BEATS flies at the player as fast as Koakuma's bats fly, turning as they do (BatAct.steer). The bat hurts like a slider body (ELIS_HIT_CIRCLES), and every bullet
        // of a ring she flies through (within ELIS_RECORD_CIRCLES of her middle: her magic circle) is taken into it, ELIS_RECORD_MOST at most. Then she stops where she is and is herself again, and for
        // ELIS_SHOOT_BEATS her magic circle shoots what it took, one after the other evenly, all the same way - the way from her to the player as she stops, so that they fly side by side, spread across her
        // circle - ELIS_SHOT_SPEED times as fast as each flew and ELIS_SHOT_SIZE times as big, red, each drawing a beam behind it as long as ELIS_TRACE_BEATS of its flight that hurts like one. Then she
        // glides back to where bosses stand. Her flight is worked out step by step (the player is where they are then); a seek back clears it (what was in the air then is gone).
        // ======================================================================================================================================================

        private sealed class BigBatAct : StageAct
        {
            private const double step_ms = DanmakusuBalance.HOMING_STEP_MS;

            private sealed class Taken
            {
                public StoredBullet Stored;
                public float Speed;
            }

            private sealed class Shot
            {
                public double T0;
                public Vector2 From;
                public Vector2 Velocity;
                public double Gone; // (its trace has left the screen)
            }

            private sealed class Flight
            {
                public double Born;
                public readonly List<Vector2> Trail = new List<Vector2>(256);
                public Vector2 Velocity;
                public double Time;
                public float Side;
                public readonly List<Taken> Taken = new List<Taken>();
                public readonly List<Shot> Shots = new List<Shot>();
                public Vector2 Stop;
                public Vector2 Way;
                public bool Stopped;
                public int Released;
            }

            private readonly double beat;
            private readonly double[] cycles; // (when each flash begins)
            private readonly Flight?[] flights;
            private double resetTime = double.NegativeInfinity;

            public BigBatAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                for (double c = period.ShootStart; c + beat * (DanmakusuBalance.ELIS_WARN_BEATS + DanmakusuBalance.ELIS_FLY_BEATS) <= period.End && list.Count < 4000; c += beat * DanmakusuBalance.ELIS_EVERY_BEATS)
                    list.Add(c);

                cycles = list.ToArray();
                flights = new Flight?[cycles.Length];
                Start = period.ShootStart;
                End = period.End + beat * (DanmakusuBalance.ELIS_SHOOT_BEATS + DanmakusuBalance.ELIS_TRACE_BEATS + 32);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = End;
            }

            private double flyStart(int k) => cycles[k] + beat * DanmakusuBalance.ELIS_WARN_BEATS;

            private double flyEnd(int k) => flyStart(k) + beat * DanmakusuBalance.ELIS_FLY_BEATS;

            private double shootEnd(int k) => flyEnd(k) + beat * DanmakusuBalance.ELIS_SHOOT_BEATS;

            private float batSpeed => stageBulletSpeed(Period) * DanmakusuBalance.BYAKUREN_TRAIL_SPEED;

            private double traceMs => beat * DanmakusuBalance.ELIS_TRACE_BEATS;

            public override double MechanicsEnd => cycles.Length > 0 ? Math.Max(Period.End, shootEnd(cycles.Length - 1)) : Period.End;

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(flights);
                ClearBullets();
                resetTime = time;
            }

            /// <summary>The cycle <paramref name="t"/> is in (its flash, flight and shots), or -1.</summary>
            private int cycleAt(double t)
            {
                int lo = 0, hi = cycles.Length - 1, found = -1;

                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;

                    if (cycles[mid] <= t)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else
                        hi = mid - 1;
                }

                return found;
            }

            /// <summary>The flight of cycle <paramref name="k"/>, begun from where she is when it is first asked for (none for one that began before a seek back).</summary>
            private Flight? flightOf(BulletField f, int k)
            {
                if (flights[k] != null)
                    return flights[k];

                if (flyStart(k) < resetTime - 1)
                    return null;

                var made = new Flight { Born = flyStart(k), Time = flyStart(k) };
                Vector2 at = f.bossPositionOf(Period);
                Vector2 toPlayer = f.characterPosition - at;
                made.Trail.Add(at);
                made.Velocity = (toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY) * batSpeed;
                return flights[k] = made;
            }

            /// <summary>Flies the bat on to <paramref name="upTo"/> (no further than its flight), homing on where the player is now.</summary>
            private void advance(BulletField f, int k, Flight flight, double upTo)
            {
                double end = Math.Min(upTo, flyEnd(k));
                Vector2 player = f.characterPosition;
                float gravity = Period.ArrowGravity * DanmakusuBalance.KOAKUMA_BAT_TURN_FACTOR;
                int guard = 0;

                while (flight.Time + step_ms <= end && guard++ < 2000)
                {
                    Vector2 at = flight.Trail[^1];
                    flight.Velocity = BatAct.steer(at, flight.Velocity, player, (float)step_ms, gravity, k, ref flight.Side);
                    Vector2 next = at + flight.Velocity * (float)step_ms;

                    // (she stays on the screen)
                    next = Vector2.Clamp(next, f.stageMin, f.stageMax);
                    flight.Trail.Add(next);
                    flight.Time += step_ms;
                }

                if (!flight.Stopped && upTo >= flyEnd(k))
                {
                    // (she is herself again: what she took flies out the way from her to the player)
                    flight.Stopped = true;
                    flight.Stop = positionIn(k, flight, flyEnd(k));
                    Vector2 way = f.characterPosition - flight.Stop;
                    flight.Way = way.LengthSquared > 1f ? Vector2.Normalize(way) : Vector2.UnitY;
                }
            }

            /// <summary>Where the bat is at <paramref name="t"/> in its flight: on its trail, or flying on straight from its end (what is to come, for the autoplay).</summary>
            private Vector2 positionIn(int k, Flight flight, double t)
            {
                if (flight.Stopped && t >= flyEnd(k))
                    return flight.Stop;

                double into = Math.Max(t - flight.Born, 0) / step_ms;
                int i = (int)into;

                if (i + 1 < flight.Trail.Count)
                    return Vector2.Lerp(flight.Trail[i], flight.Trail[i + 1], (float)(into - i));

                double past = Math.Min(t, flyEnd(k)) - flight.Time;
                return flight.Trail[^1] + flight.Velocity * (float)Math.Max(past, 0);
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;
                int k = cycleAt(now);

                if (k < 0 || now < flyStart(k) || now > shootEnd(k) || f.failed)
                    return false;

                var flight = flightOf(f, k);

                if (flight == null)
                    return false;

                advance(f, k, flight, now);
                position = positionIn(k, flight, now);
                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                int k = cycleAt(now);

                if (k >= 0 && now >= flyStart(k) && now <= shootEnd(k) && flightOf(f, k) is Flight flight)
                {
                    advance(f, k, flight, now);

                    if (now < flyEnd(k))
                        f.stageHideRank[Math.Clamp(Period.Rank, 0, 2)] = 1f;
                }

                // (what she took is shot evenly over her shots, the first half a step in)
                for (int c = Math.Max(k - 1, 0); c <= k && c >= 0; c++)
                {
                    var made = flights[c];

                    if (made == null || !made.Stopped || made.Taken.Count == 0)
                        continue;

                    double span = beat * DanmakusuBalance.ELIS_SHOOT_BEATS;
                    int n = made.Taken.Count;

                    while (made.Released < n)
                    {
                        int i = made.Released;
                        double at = flyEnd(c) + span * (i + 0.5) / n;

                        if (at > now)
                            break;

                        release(f, made, i, at);
                        made.Released++;
                    }
                }
            }

            private void release(BulletField f, Flight flight, int i, double at)
            {
                var taken = flight.Taken[i];
                var stored = taken.Stored;
                Vector2 across = new Vector2(-flight.Way.Y, flight.Way.X);
                float slot = (i * 0.618034f) % 1f * 2f - 1f;
                Vector2 from = flight.Stop + across * (slot * DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 0.85f);
                Vector2 velocity = flight.Way * (Math.Max(taken.Speed, stageBulletSpeed(Period) * 0.5f) * DanmakusuBalance.ELIS_SHOT_SPEED);
                double life = f.stageLife(velocity);

                AddBullet(new StageBullet
                {
                    T0 = at,
                    P0 = from,
                    V = velocity,
                    Radius = stored.Radius * DanmakusuBalance.ELIS_SHOT_SIZE,
                    Hit = stored.Hit * DanmakusuBalance.ELIS_SHOT_SIZE,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_RED,
                    TintAmount = 1f,
                    Shade = stored.Shade,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Untraced = true,
                    Until = at + life,
                });

                flight.Shots.Add(new Shot { T0 = at, From = from, Velocity = velocity, Gone = at + life + traceMs });
            }

            /// <summary>The bat takes what of the rings it flies through.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                int k = cycleAt(t);

                if (k < 0 || t < flyStart(k) || t >= flyEnd(k) || flights[k] is not Flight flight || flight.Taken.Count >= DanmakusuBalance.ELIS_RECORD_MOST)
                    return false;

                float reach = f.circleRadius * DanmakusuBalance.ELIS_RECORD_CIRCLES;

                if (Vector2.DistanceSquared(to, positionIn(k, flight, t)) > reach * reach)
                    return false;

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                stored.Radius *= big;
                stored.Hit *= big;
                flight.Taken.Add(new Taken { Stored = stored, Speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t) });
                return true;
            }

            // ---- what it looks like ----

            private void trace(Shot shot, double t, out Vector2 tail, out Vector2 head)
            {
                double back = Math.Max(shot.T0, t - traceMs);
                tail = shot.From + shot.Velocity * (float)(back - shot.T0);
                head = shot.From + shot.Velocity * (float)(t - shot.T0);
            }

            private float traceHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.ELIS_TRACE_WIDTH;

            private readonly List<Vector2> line = new List<Vector2>(2);

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                var red = new Color4(0.95f, 0.15f, 0.15f, 1f);
                var edge = new Color4(1f, 0.6f, 0.55f, 1f);
                float half = traceHalf(f);

                foreach (var flight in flights)
                {
                    if (flight == null)
                        continue;

                    foreach (var shot in flight.Shots)
                    {
                        if (t <= shot.T0 || t > shot.Gone)
                            continue;

                        trace(shot, t, out Vector2 tail, out Vector2 head);
                        line.Clear();
                        line.Add(tail);
                        line.Add(head);
                        f.addPlainBeam(line, half, red, edge, ref beams, ref pointTotal);
                    }
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                int k = cycleAt(t);

                if (k < 0)
                    return;

                // (the flash: red, blinking faster and faster)
                if (t < flyStart(k))
                {
                    double warn = beat * DanmakusuBalance.ELIS_WARN_BEATS;
                    float u = (float)((t - cycles[k]) / Math.Max(warn, 1));
                    float level = (0.5f + 0.5f * MathF.Cos(u * u * MathF.PI * 8f)) * smoothStep(u / 0.2f);
                    f.stageFront.Add(StageTexture.Glow, f.bossPositionOf(Period), new Vector2(DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2.6f), 0f, 0.75f * level, new Color4(1f, 0.1f, 0.1f, 1f), true);
                    return;
                }

                if (t >= flyEnd(k) || flights[k] is not Flight flight)
                    return;

                Vector2 position = positionIn(k, flight, t);
                Vector2 velocity = flight.Velocity;
                float heading = velocity.LengthSquared > 1e-12f ? MathF.Atan2(velocity.Y, velocity.X) * 180f / MathF.PI + 90f : 0f;
                float wing = f.circleRadius * DanmakusuBalance.ELIS_BAT_CIRCLES;
                float flap = (float)((t - flight.Born) / (DanmakusuBalance.KOAKUMA_BAT_FLAP_MS * 1.6));
                int frame = (int)MathF.Floor((flap - MathF.Floor(flap)) * StageLayer.BatFrames) % StageLayer.BatFrames;
                float shown = smoothStep((t - flyStart(k)) / 150.0) * (1f - smoothStep((t - (flyEnd(k) - 150)) / 150.0));

                f.stageFront.Add(StageTexture.Glow, position, new Vector2(wing * 3f), 0f, 0.35f * shown, new Color4(1f, 0.15f, 0.15f, 1f), true);
                f.stageBats.Add(StageLayer.BatFrame(frame), position, new Vector2(2f * StageLayer.BatExtent * wing), heading, shown, new Color4(1f, 0.55f, 0.55f, 1f));
            }

            /// <summary>The bat hurts like a slider body; so do the traces of what she shoots.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                int k = cycleAt(now);

                if (k >= 0 && now >= flyStart(k) && now < flyEnd(k) && flights[k] is Flight flight)
                {
                    float reach = f.circleRadius * DanmakusuBalance.ELIS_HIT_CIRCLES + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    if (Vector2.DistanceSquared(player, positionIn(k, flight, now)) <= reach * reach)
                        return true;
                }

                float width = traceHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int c = Math.Max(k - 1, 0); c <= k && c >= 0; c++)
                {
                    var made = flights[c];

                    if (made == null)
                        continue;

                    foreach (var shot in made.Shots)
                    {
                        if (now <= shot.T0 || now > shot.Gone)
                            continue;

                        trace(shot, now, out Vector2 tail, out Vector2 head);

                        if (distanceToSegment(player, tail, head) <= width)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
