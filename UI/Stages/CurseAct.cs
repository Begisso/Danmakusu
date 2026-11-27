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
        // Since 2026-11-26 (the user's picture: "I still don't see symbols after asking for 10 times ... they are white, they glow, they bloom") a curse is the picture of 呪 (Resources/Textures/
        // noroiko_curse.png, Tools/Bake glyph), white, over a soft white glow and a wider bloom; its ink hurts (Bullets/CurseMask, the same picture in cells). A slider's is as big as the slider is long in
        // time, NOROIKO_SLIDER_CIRCLES_PER_BEAT circle radii a beat.
        // Where a curse goes depends on where the player has been, so it is followed step by step (NOROIKO_STEP_MS) as the game goes and kept: going back in time takes the steps back. Asked about a time not
        // followed yet (the autoplay), it flies straight on from the last step.
        // ======================================================================================================================================================

        private sealed class CurseAct : StageAct
        {
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

                    // (as big as the slider is long in time since 2026-11-26, "its size grows proportionally to slider duration": NOROIKO_SLIDER_CIRCLES_PER_BEAT circle radii a beat - it was as big as
                    // the slider was wide)
                    float lasts = (float)((zone.EndTime - zone.StartTime) / beat);
                    float sliderSize = Math.Clamp(f.circleRadius * DanmakusuBalance.NOROIKO_SLIDER_CIRCLES_PER_BEAT * lasts, f.circleRadius * DanmakusuBalance.NOROIKO_SLIDER_LEAST, f.circleRadius * DanmakusuBalance.NOROIKO_SLIDER_MOST);
                    list.Add(new Throw(zone.StartTime, sliderSize, 0, zone.Points[0]));
                }

                list.Sort((a, b) => a.T.CompareTo(b.T));
                throws = list.ToArray();

                Start = period.ShootStart;
                End = throws.Length > 0 ? throws[^1].T + homing + beat * 16 : period.End;
            }

            private double homing => beat * DanmakusuBalance.NOROIKO_HOMING_BEATS;

            public override double MechanicsEnd => throws.Length > 0 ? throws[^1].T + homing : double.NegativeInfinity;

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

            /// <summary>
            /// Every curse as the picture of 呪 (2026-11-26, the user's picture: "I still don't see symbols ... they are white, they glow, they bloom"; drawn in thick red beams until then): white, over a
            /// soft white glow and a wider bloom, as big as it is thrown.
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || flights.Count == 0)
                    return;

                foreach (var flight in flights)
                {
                    var thrown = throws[flight.Throw];

                    if (t < thrown.T || t > flight.Gone)
                        continue;

                    Vector2 at = placeOf(f, flight, t);

                    if (!onScreen(f, at, thrown.Size))
                        continue;

                    float shown = smoothStep((t - thrown.T) / 120.0);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(thrown.Size * DanmakusuBalance.NOROIKO_BLOOM_SIZE), 0f, DanmakusuBalance.NOROIKO_BLOOM_ALPHA * shown, Color4.White, true);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(thrown.Size * DanmakusuBalance.NOROIKO_GLOW_SIZE), 0f, DanmakusuBalance.NOROIKO_GLOW_ALPHA * shown, Color4.White, true);
                    // (the character itself is its beams: DrawBeams)
                }
            }

            // ---- where the picture is ink: how far every cell of its square is from the nearest inked one (in cells; 0 in the ink) ----

            private static readonly float[,] inkDistance = makeInkDistance();

            private static float[,] makeInkDistance()
            {
                var rows = CurseMask.ROWS;
                int n = rows.Length;
                var ink = new List<(int X, int Y)>();

                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        if (rows[y][x] == '#')
                            ink.Add((x, y));
                    }
                }

                var distance = new float[n, n];

                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float best = float.MaxValue;

                        foreach (var (ix, iy) in ink)
                            best = Math.Min(best, (ix - x) * (ix - x) + (iy - y) * (iy - y));

                        distance[x, y] = ink.Count > 0 ? MathF.Sqrt(best) : float.MaxValue;
                    }
                }

                return distance;
            }

            /// <summary>How far <paramref name="p"/> is from the ink of a curse at <paramref name="at"/>, <paramref name="size"/> px tall (px; about a cell's width finer).</summary>
            private static float inkAway(Vector2 p, Vector2 at, float size)
            {
                int n = inkDistance.GetLength(0);
                float cell = size / n;
                Vector2 local = (p - at) / size + new Vector2(0.5f);
                float cx = local.X * n, cy = local.Y * n;

                // (outside its square: as far as the nearest cell of its edge, and how far out of it)
                float outX = cx < 0 ? -cx : cx > n ? cx - n : 0f, outY = cy < 0 ? -cy : cy > n ? cy - n : 0f;
                int x = Math.Clamp((int)cx, 0, n - 1), y = Math.Clamp((int)cy, 0, n - 1);
                float inside = inkDistance[x, y];

                return (inside + MathF.Sqrt(outX * outX + outY * outY)) * cell;
            }

            /// <summary>The ink of every curse hurts like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || flights.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var flight in flights)
                {
                    var thrown = throws[flight.Throw];

                    if (now < thrown.T || now > flight.Gone)
                        continue;

                    Vector2 at = placeOf(f, flight, now);
                    float half = strokeHalf(f, thrown.Size) * DanmakusuBalance.BULLET_HITBOX_SCALE + reach;

                    // (nowhere near it: its strokes are not looked at)
                    if (MathF.Abs(player.X - at.X) > thrown.Size * 0.5f + half || MathF.Abs(player.Y - at.Y) > thrown.Size * 0.5f + half)
                        continue;

                    // (its beams hurt where they are drawn: 2026-11-27)
                    foreach (var stroke in CurseGlyph.STROKES)
                    {
                        for (int i = 0; i + 1 < stroke.Length; i++)
                        {
                            if (distanceToSegment(player, at + stroke[i] * thrown.Size, at + stroke[i + 1] * thrown.Size) <= half)
                                return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>How wide half a stroke of a curse is: as thick as a slider's beam, thicker on a big curse.</summary>
            private static float strokeHalf(BulletField f, float size) => Math.Max(f.circleRadius * DanmakusuBalance.NOROIKO_STROKE_CIRCLES, size * DanmakusuBalance.NOROIKO_STROKE_SHARE);

            private readonly List<Vector2> strokePoints = new List<Vector2>(8);
            private readonly List<float> strokeFades = new List<float>(8);

            /// <summary>
            /// Every curse as 呪 in laser beams (2026-11-27, "she still didn't shoot a single letter shaped laserbeams. At all."; a picture of it, white, since 2026-11-26, and thick red beams before): its
            /// strokes as beams with a white core and a red edge, glowing.
            /// </summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || flights.Count == 0)
                    return;

                var edge = new Color4(0.95f, 0.12f, 0.2f, 1f);

                foreach (var flight in flights)
                {
                    var thrown = throws[flight.Throw];

                    if (t < thrown.T || t > flight.Gone)
                        continue;

                    Vector2 at = placeOf(f, flight, t);

                    if (!onScreen(f, at, thrown.Size))
                        continue;

                    float shown = smoothStep((t - thrown.T) / 120.0);
                    float half = strokeHalf(f, thrown.Size);

                    foreach (var stroke in CurseGlyph.STROKES)
                    {
                        strokePoints.Clear();
                        strokeFades.Clear();

                        foreach (var point in stroke)
                        {
                            strokePoints.Add(at + point * thrown.Size);
                            strokeFades.Add(shown);
                        }

                        f.addPlainBeam(strokePoints, half, Color4.White, edge, ref beams, ref pointTotal, null, strokeFades, Color4.White, DanmakusuBalance.NOROIKO_BEAM_BLOOM);
                    }
                }
            }
        }
    }
}
