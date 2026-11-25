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
        // Noroiko (2026-11-23): every NOROIKO_EVERY_BEATS of her attacks (and her spinner) she throws two curses - the character 呪 ("curse") drawn in red beams, NOROIKO_CURSE_CIRCLES circle radii tall -
        // one to her left and one to her right. Each flies off that way at NOROIKO_SPEED_FROM times a plain bullet's speed and for NOROIKO_HOMING_BEATS speeds up evenly to NOROIKO_SPEED_TO times it, turning
        // towards the player as it goes (at most NOROIKO_TURN_DEGREES a beat); then it flies straight on out of the screen. Every slider of her section throws one too instead of its beam, from where it
        // starts, at the player, as big as the slider is (its widest, NOROIKO_SLIDER_LEAST..MOST circle radii). Every stroke hurts like a slider body.
        // Where a curse goes depends on where the player has been, so it is followed step by step (NOROIKO_STEP_MS) as the game goes and kept: going back in time takes the steps back. Asked about a time not
        // followed yet (the autoplay), it flies straight on from the last step.
        // ======================================================================================================================================================

        private sealed class CurseAct : StageAct
        {
            /// <summary>The strokes of 呪 in a box one unit tall (y down): its mouth on the left, and 兄 - a mouth over two legs - on the right.</summary>
            private static readonly Vector2[][] strokes =
            {
                // 口 (left)
                new[] { new Vector2(-0.48f, -0.16f), new Vector2(-0.48f, 0.2f) },
                new[] { new Vector2(-0.48f, -0.16f), new Vector2(-0.2f, -0.16f), new Vector2(-0.2f, 0.2f) },
                new[] { new Vector2(-0.48f, 0.2f), new Vector2(-0.2f, 0.2f) },
                // 口 (right, top)
                new[] { new Vector2(-0.06f, -0.48f), new Vector2(-0.06f, -0.06f) },
                new[] { new Vector2(-0.06f, -0.48f), new Vector2(0.44f, -0.48f), new Vector2(0.44f, -0.06f) },
                new[] { new Vector2(-0.06f, -0.06f), new Vector2(0.44f, -0.06f) },
                // 儿 (the legs)
                new[] { new Vector2(0.08f, -0.06f), new Vector2(0.07f, 0.16f), new Vector2(0f, 0.32f), new Vector2(-0.12f, 0.44f), new Vector2(-0.22f, 0.5f) },
                new[] { new Vector2(0.28f, -0.06f), new Vector2(0.28f, 0.38f), new Vector2(0.31f, 0.46f), new Vector2(0.38f, 0.49f), new Vector2(0.48f, 0.49f), new Vector2(0.5f, 0.4f) },
            };

            /// <summary>One thrown: when, how big (px tall), which way it sets off (0: at the player; else her left or right), and where it starts (a slider's start; NaN: where she is when it is thrown).</summary>
            private readonly struct Throw
            {
                public readonly double T;
                public readonly float Size;
                public readonly int Side;
                public readonly Vector2 From;

                public Throw(double t, float size, int side, Vector2 from)
                {
                    T = t;
                    Size = size;
                    Side = side;
                    From = from;
                }
            }

            /// <summary>A curse on its way: its steps (a place every NOROIKO_STEP_MS from when it was thrown), how fast it goes and which way at the last of them.</summary>
            private sealed class Flight
            {
                public int Throw;
                public readonly List<Vector2> Steps = new List<Vector2>();
                public Vector2 Velocity;

                /// <summary>Where the autoplay foresees it after its last step, while it still homes (on where the player really is), and how fast it goes at the last of those: worked out again every frame.</summary>
                public readonly List<Vector2> Ahead = new List<Vector2>();

                public Vector2 AheadVelocity;

                /// <summary>When it has left the screen for good (known once it no longer homes; NaN before).</summary>
                public double Gone = double.NaN;
            }

            private readonly double beat;
            private readonly Throw[] throws;
            private readonly List<Flight> flights = new List<Flight>();
            private readonly List<Vector2> line = new List<Vector2>(8);

            public CurseAct(BulletField f, BossPeriod period, SliderZone[] sliderZones)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<Throw>();
                float size = f.circleRadius * DanmakusuBalance.NOROIKO_CURSE_CIRCLES;

                // hers, from round her, left and right
                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = start; t < end; t += beat * DanmakusuBalance.NOROIKO_EVERY_BEATS)
                    {
                        list.Add(new Throw(t, size, -1, new Vector2(float.NaN)));
                        list.Add(new Throw(t, size, 1, new Vector2(float.NaN)));
                    }
                }

                // the sliders', from where each starts, as big as it is
                foreach (var zone in sliderZones)
                {
                    if (zone.StartTime < period.ShootStart || zone.StartTime >= period.End || zone.Points.Length == 0)
                        continue;

                    Vector2 low = zone.Points[0], high = zone.Points[0];

                    foreach (var point in zone.Points)
                    {
                        low = Vector2.ComponentMin(low, point);
                        high = Vector2.ComponentMax(high, point);
                    }

                    float extent = Math.Max(high.X - low.X, high.Y - low.Y) + zone.Radius * 2f;
                    float sliderSize = Math.Clamp(extent, f.circleRadius * DanmakusuBalance.NOROIKO_SLIDER_LEAST, f.circleRadius * DanmakusuBalance.NOROIKO_SLIDER_MOST);
                    list.Add(new Throw(zone.StartTime, sliderSize, 0, zone.Points[0]));
                }

                list.Sort((a, b) => a.T.CompareTo(b.T));
                throws = list.ToArray();

                Start = period.ShootStart;
                End = throws.Length > 0 ? throws[^1].T + homing + beat * 16 : period.End;
            }

            private double homing => beat * DanmakusuBalance.NOROIKO_HOMING_BEATS;

            public override double MechanicsEnd => throws.Length > 0 ? throws[^1].T + homing : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.NOROIKO_STROKE_CIRCLES;

            public override void Reset(BulletField f, double time)
            {
                for (int i = flights.Count - 1; i >= 0; i--)
                {
                    var flight = flights[i];
                    double thrown = throws[flight.Throw].T;

                    if (thrown > time)
                    {
                        flights.RemoveAt(i);
                        continue;
                    }

                    // (back to the step at the time gone back to, and the way it went there)
                    int keep = Math.Max(1, (int)Math.Floor((time - thrown) / DanmakusuBalance.NOROIKO_STEP_MS) + 1);

                    flight.Gone = double.NaN;
                    flight.Ahead.Clear();

                    if (flight.Steps.Count > keep)
                    {
                        flight.Steps.RemoveRange(keep, flight.Steps.Count - keep);

                        if (flight.Steps.Count >= 2)
                        {
                            Vector2 last = flight.Steps[^1] - flight.Steps[^2];
                            flight.Velocity = last / (float)DanmakusuBalance.NOROIKO_STEP_MS;
                        }

                        flight.AheadVelocity = flight.Velocity;
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every throw whose time has come starts its flight, where she is or where its slider starts)
                int next = flights.Count == 0 ? 0 : flights[^1].Throw + 1;

                for (; next < throws.Length && throws[next].T <= now; next++)
                {
                    var thrown = throws[next];
                    Vector2 from = float.IsNaN(thrown.From.X) ? f.bossPositionOf(Period) : thrown.From;
                    Vector2 way = thrown.Side != 0 ? new Vector2(thrown.Side, 0f) : f.characterPosition - from;

                    if (way.LengthSquared < 1e-6f)
                        way = Vector2.UnitY;

                    var flight = new Flight { Throw = next, Velocity = Vector2.Normalize(way) * stageBulletSpeed(Period) * DanmakusuBalance.NOROIKO_SPEED_FROM };
                    flight.AheadVelocity = flight.Velocity;
                    flight.Steps.Add(from);
                    flights.Add(flight);
                }

                // (each flies on step by step until now, as long as it homes; after that it flies straight, worked out from its last step)
                double step = DanmakusuBalance.NOROIKO_STEP_MS;
                float plain = stageBulletSpeed(Period);
                float turn = turnPerStep;

                foreach (var flight in flights)
                {
                    double thrown = throws[flight.Throw].T;
                    double until = Math.Min(now, thrown + homing);

                    while (thrown + (flight.Steps.Count - 1) * step + step <= until)
                        flight.Steps.Add(stepOnce(flight.Steps[^1], ref flight.Velocity, (flight.Steps.Count - 1) * step, f.characterPosition, plain, turn));

                    // (what was foreseen beyond the steps is foreseen again from the new ones)
                    flight.Ahead.Clear();
                    flight.AheadVelocity = flight.Velocity;

                    // (once it no longer homes, when it is out of the screen with all of it)
                    if (double.IsNaN(flight.Gone) && now >= thrown + homing)
                    {
                        float size = throws[flight.Throw].Size;
                        double across = f.toWall(flight.Steps[^1], flight.Velocity, out _);
                        flight.Gone = thrown + (flight.Steps.Count - 1) * step + (double.IsInfinity(across) ? 0 : across) + size / Math.Max(flight.Velocity.Length, 1e-3f);
                    }
                }
            }

            /// <summary>How far a curse turns towards the player in one step at most.</summary>
            private float turnPerStep => DanmakusuBalance.NOROIKO_TURN_DEGREES * MathF.PI / 180f / (float)beat * (float)DanmakusuBalance.NOROIKO_STEP_MS;

            /// <summary>One step of a curse's flight from <paramref name="at"/>, <paramref name="age"/> ms after it was thrown: as fast as it is by then, turned towards <paramref name="target"/> as far as it may.</summary>
            private Vector2 stepOnce(Vector2 at, ref Vector2 velocity, double age, Vector2 target, float plain, float turn)
            {
                float speed = plain * (DanmakusuBalance.NOROIKO_SPEED_FROM + (DanmakusuBalance.NOROIKO_SPEED_TO - DanmakusuBalance.NOROIKO_SPEED_FROM) * (float)Math.Clamp(age / homing, 0, 1));
                Vector2 toTarget = target - at;
                float heading = MathF.Atan2(velocity.Y, velocity.X);

                if (toTarget.LengthSquared > 1f)
                {
                    float wanted = MathF.Atan2(toTarget.Y, toTarget.X);
                    heading += Math.Clamp(MathF.IEEERemainder(wanted - heading, MathF.Tau), -turn, turn);
                }

                velocity = new Vector2(MathF.Cos(heading), MathF.Sin(heading)) * speed;
                return at + velocity * (float)DanmakusuBalance.NOROIKO_STEP_MS;
            }

            /// <summary>
            /// Where curse <paramref name="flight"/> is at <paramref name="t"/>: along its steps; beyond them, while it still homes, as the autoplay foresees what homes - on towards where the player really is (worked out
            /// once a frame, <see cref="Flight.Ahead"/>); then straight on.
            /// </summary>
            private Vector2 placeOf(BulletField f, Flight flight, double t)
            {
                double age = t - throws[flight.Throw].T;
                double step = DanmakusuBalance.NOROIKO_STEP_MS;

                if (age <= 0)
                    return flight.Steps[0];

                double u = age / step;
                int i = (int)Math.Floor(u);

                if (i + 1 < flight.Steps.Count)
                    return Vector2.Lerp(flight.Steps[i], flight.Steps[i + 1], (float)(u - i));

                int real = flight.Steps.Count - 1;
                int needed = Math.Min(i + 1, (int)Math.Floor(homing / step)) - real;

                while (flight.Ahead.Count < Math.Min(needed, 400))
                {
                    Vector2 from = flight.Ahead.Count > 0 ? flight.Ahead[^1] : flight.Steps[^1];
                    flight.Ahead.Add(stepOnce(from, ref flight.AheadVelocity, (real + flight.Ahead.Count) * step, f.realCharacterPosition, stageBulletSpeed(Period), turnPerStep));
                }

                Vector2 pointAt(int k) => k <= 0 ? flight.Steps[real] : flight.Ahead[k - 1];

                int beyond = i - real;

                if (beyond + 1 <= flight.Ahead.Count)
                    return Vector2.Lerp(pointAt(beyond), pointAt(beyond + 1), (float)(u - i));

                return pointAt(flight.Ahead.Count) + flight.AheadVelocity * (float)(age - (real + flight.Ahead.Count) * step);
            }

            /// <summary>Is it on the screen at all (with its size round it)?</summary>
            private static bool onScreen(BulletField f, Vector2 at, float size)
                => at.X > f.stageMin.X - size && at.X < f.stageMax.X + size && at.Y > f.stageMin.Y - size && at.Y < f.stageMax.Y + size;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || flights.Count == 0)
                    return;

                var core = new Color4(1f, 0.32f, 0.3f, 1f);
                var glow = new Color4(1f, 0.62f, 0.58f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.62f, 0.02f, 0.06f, 1f));
                float half = halfWidth(f);

                foreach (var flight in flights)
                {
                    var thrown = throws[flight.Throw];

                    if (t < thrown.T || t > flight.Gone)
                        continue;

                    Vector2 at = placeOf(f, flight, t);

                    if (!onScreen(f, at, thrown.Size))
                        continue;

                    foreach (var stroke in strokes)
                    {
                        line.Clear();

                        foreach (var point in stroke)
                            line.Add(at + point * thrown.Size);

                        f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, null, glow); // (red all through: with a white middle, as other beams, a thin curse looked like a white slider beam - 2026-11-25)
                    }
                }
            }

            /// <summary>Every stroke hurts like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || flights.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var flight in flights)
                {
                    var thrown = throws[flight.Throw];

                    if (now < thrown.T || now > flight.Gone)
                        continue;

                    Vector2 at = placeOf(f, flight, now);

                    // (nowhere near it: not looked at stroke by stroke)
                    if (Vector2.Distance(player, at) > thrown.Size * 0.75f + reach)
                        continue;

                    foreach (var stroke in strokes)
                    {
                        for (int i = 0; i + 1 < stroke.Length; i++)
                        {
                            if (distanceToSegment(player, at + stroke[i] * thrown.Size, at + stroke[i + 1] * thrown.Size) <= reach)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
