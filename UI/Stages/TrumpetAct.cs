using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        private readonly List<TrumpetAct> trumpetActs = new List<TrumpetAct>();

        /// <summary>Merlin's attacks: the map's circles and sliders do not hurt then (see <see cref="TrumpetAct"/>).</summary>
        private (double Start, double End)[] trumpetWindows = Array.Empty<(double, double)>();

        /// <summary>Has Merlin attacks in this map (the playfield colours her circles and sliders green).</summary>
        public bool HasTrumpet => trumpetWindows.Length > 0;

        /// <summary>Do the map's circles and sliders hurt at <paramref name="t"/>? Not in Merlin's attacks: they send out her rings instead.</summary>
        public bool ObjectsHarmless(double t)
        {
            foreach (var (start, end) in trumpetWindows)
            {
                if (t >= start && t < end)
                    return true;
            }

            return false;
        }

        // ======================================================================================================================================================
        // Merlin Prismriver (2026-11-20): in her attacks the map's circles and sliders do not hurt, and shoot neither their bullets nor their beams (the planner leaves those out): each, when it is hit (a
        // slider at its head), sends out a thick white ring instead, as Raiko's ripples go out - not see-through, four times as thick as their line - which hurts like a slider body. But a circle that the
        // player is on when it is hit (a slider: its body) sends out nothing: everything within two circle radii of it is cleared instead, bullets and slices of beams, with a green flash.
        // As a warning the circles and sliders of her attacks are green, glowing, and their numbers are gone: a ring of green arrows closes in on each as it comes (the playfield colours them, see
        // DanmakusuPlayfield). In her spinners she throws six circles and sliders round her every eight beats, flying away from her; each sends out such a ring every two beats - not while the player is on it.
        // Whether each sent out a ring is decided when it is hit (where the player is then), and done again when time goes back.
        // ======================================================================================================================================================

        private sealed class TrumpetAct : StageAct
        {
            private struct Shot
            {
                public double Time;
                public Vector2 Position;
                public float Radius;
                public SliderZone? Slider; // (a slider's head: being on its body counts)
                public double Shown; // (when it comes: its approach)
            }

            private struct Thrown
            {
                public double Time;
                public Vector2 From;
                public Vector2 Velocity;
                public bool Slider;
            }

            private readonly double beat;
            private readonly List<Shot> shots = new List<Shot>(); // (the map's circles and sliders of her attacks, in time order)
            private readonly List<int> decided = new List<int>(); // (-1 not yet, 0 a ring, 1 cleared)
            private readonly List<(double Time, Vector2 At)> clears = new List<(double, Vector2)>();
            private readonly List<Thrown> thrown = new List<Thrown>();
            private readonly List<(double Time, Vector2 At)> thrownRings = new List<(double, Vector2)>();
            private readonly List<double> throws = new List<double>();
            private readonly List<double> thrownChecks = new List<double>(); // (when each thrown one sends out a ring: decided as its time comes)
            private int nextShot, nextThrow;
            private double resetTime = double.NegativeInfinity;

            public TrumpetAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.Start;

                if (period.Kind == BossKind.Spinner)
                {
                    for (double t = period.ShootStart; t < period.End - 1; t += beat * DanmakusuBalance.MERLIN_THROW_EVERY_BEATS)
                        throws.Add(t);
                }
                else
                {
                    var windows = BossOps.Attacks(period);

                    bool inAttack(double t)
                    {
                        foreach (var (start, end) in windows)
                        {
                            if (t >= start && t < end)
                                return true;
                        }

                        return false;
                    }

                    foreach (var zone in f.circleZones)
                    {
                        if (inAttack(zone.Time))
                            shots.Add(new Shot { Time = zone.Time, Position = zone.Position, Radius = zone.Radius, Shown = zone.Time - (zone.Source?.TimePreempt ?? 600) });
                    }

                    foreach (var zone in f.sliderZones)
                    {
                        if (zone.Points.Length > 0 && inAttack(zone.StartTime))
                            shots.Add(new Shot { Time = zone.StartTime, Position = zone.Points[0], Radius = zone.Radius, Slider = zone, Shown = zone.StartTime - (zone.Source?.TimePreempt ?? 600) });
                    }

                    shots.Sort((a, b) => a.Time.CompareTo(b.Time));

                    for (int i = 0; i < shots.Count; i++)
                        decided.Add(-1);
                }

                End = period.End + 8000; // (the last rings go out over the screen after her section)
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            /// <summary>She stays until her last rings are out of the screen.</summary>
            public override double MechanicsEnd => Period.End;

            private float halfWidth => DanmakusuBalance.RAIKO_RIPPLE_LINE * DanmakusuBalance.MERLIN_RING_THICKNESS * 0.5f;

            private static float speed => DanmakusuBalance.RAIKO_RIPPLE_SPEED;

            /// <summary>How long a ring goes on (until it has left the screen from anywhere on it).</summary>
            private double lifeOf(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) + 2f * halfWidth) / speed;

            public override void Reset(BulletField f, double time)
            {
                for (int i = 0; i < shots.Count; i++)
                {
                    if (shots[i].Time > time)
                        decided[i] = -1;
                }

                nextShot = 0;

                while (nextShot < shots.Count && shots[nextShot].Time <= time && decided[nextShot] >= 0)
                    nextShot++;

                clears.RemoveAll(c => c.Time > time);

                // (what was thrown before that moment stays as it was, and its rings until then; the later throws come again when their time comes)
                for (int k = thrown.Count - 1; k >= 0; k--)
                {
                    if (thrown[k].Time > time)
                    {
                        thrown.RemoveAt(k);

                        if (k < thrownChecks.Count)
                            thrownChecks.RemoveAt(k);
                    }
                }

                double every = beat * DanmakusuBalance.MERLIN_THROWN_RING_BEATS;

                for (int k = 0; k < thrownChecks.Count; k++)
                    thrownChecks[k] = thrown[k].Time + every * (Math.Floor(Math.Max(time - thrown[k].Time, 0) / every) + 1);

                thrownRings.RemoveAll(r => r.Time > time);
                nextThrow = 0;

                while (nextThrow < throws.Count && throws[nextThrow] <= time)
                    nextThrow++;

                resetTime = time;
            }

            /// <summary>Is the character on shot <paramref name="i"/> (a circle, or a slider's body) right now?</summary>
            private bool onShot(BulletField f, int i, double now)
            {
                var shot = shots[i];

                if (shot.Slider is SliderZone zone)
                    return zone.Touches(f.characterPosition - zone.FallOffset(now) - (zone.FallSpeed > 0f ? f.CatchLanding : Vector2.Zero), DanmakusuBalance.PLAYER_HITBOX_RADIUS);

                return Vector2.Distance(f.characterPosition, shot.Position) <= shot.Radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // the map's circles and sliders as they are hit: a ring, or - the player on it - a clearing round it
                for (; nextShot < shots.Count && shots[nextShot].Time <= now; nextShot++)
                {
                    if (decided[nextShot] >= 0)
                        continue;

                    bool on = onShot(f, nextShot, now);
                    decided[nextShot] = on ? 1 : 0;

                    if (on)
                    {
                        Vector2 at = shots[nextShot].Slider != null ? f.characterPosition : shots[nextShot].Position;
                        clears.Add((shots[nextShot].Time, at));

                        if (now - shots[nextShot].Time < 250 && shots[nextShot].Time > resetTime)
                            f.playStageSound("touhou_strike");
                    }
                }

                // her spinner: six circles and sliders thrown round her, each sending out a ring every two beats, unless the player is on it then
                for (; nextThrow < throws.Count && throws[nextThrow] <= now; nextThrow++)
                {
                    double t = throws[nextThrow];
                    Vector2 from = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from;
                    float aim = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    float flying = stageBulletSpeed(Period) * DanmakusuBalance.MERLIN_THROWN_SPEED;

                    for (int i = 0; i < DanmakusuBalance.MERLIN_THROWN; i++)
                    {
                        float angle = aim + (i + 0.5f) * MathF.Tau / DanmakusuBalance.MERLIN_THROWN; // (none straight at the player)
                        thrown.Add(new Thrown { Time = t, From = from, Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * flying, Slider = i % 2 == 1 });
                    }
                }

                double every = beat * DanmakusuBalance.MERLIN_THROWN_RING_BEATS;

                for (int k = 0; k < thrown.Count; k++)
                {
                    var one = thrown[k];

                    while (thrownChecks.Count <= k)
                        thrownChecks.Add(one.Time + every);

                    for (; thrownChecks[k] <= now; thrownChecks[k] += every)
                    {
                        Vector2 at = thrownAt(one, thrownChecks[k]);

                        if (!onScreen(f, at))
                            continue;

                        if (!onThrown(f, one, at))
                            thrownRings.Add((thrownChecks[k], at));
                    }
                }
            }

            private Vector2 thrownAt(in Thrown one, double t) => one.From + one.Velocity * (float)(t - one.Time);

            /// <summary>A thrown slider lies along the way it flies, two circle radii long.</summary>
            private void thrownEnds(BulletField f, in Thrown one, Vector2 at, out Vector2 a, out Vector2 b)
            {
                Vector2 way = one.Velocity.LengthSquared > 1e-9f ? Vector2.Normalize(one.Velocity) : Vector2.UnitX;
                float half = one.Slider ? f.circleRadius : 0f;
                a = at - way * half;
                b = at + way * half;
            }

            private bool onThrown(BulletField f, in Thrown one, Vector2 at)
            {
                thrownEnds(f, one, at, out Vector2 a, out Vector2 b);
                return distanceToSegment(f.characterPosition, a, b) <= f.circleRadius + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
            }

            private static bool onScreen(BulletField f, Vector2 p) => p.X >= f.stageMin.X && p.X <= f.stageMax.X && p.Y >= f.stageMin.Y && p.Y <= f.stageMax.Y;

            // ---- the rings ----

            /// <summary>The rings hurt like a slider body. What is not decided yet (the autoplay asks about the time to come) is taken to send out its ring.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                double life = lifeOf(f);
                float reach = halfWidth + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (from the first that can still be on the screen)
                int i = firstSince(now - life);

                for (; i < shots.Count && shots[i].Time <= now; i++)
                {
                    if (decided[i] == 1)
                        continue;

                    float r = speed * (float)(now - shots[i].Time);

                    if (MathF.Abs(Vector2.Distance(player, shots[i].Position) - r) <= reach)
                        return true;
                }

                foreach (var (time, at) in thrownRings)
                {
                    if (time > now || now - time > life)
                        continue;

                    if (MathF.Abs(Vector2.Distance(player, at) - speed * (float)(now - time)) <= reach)
                        return true;
                }

                // (the thrown ones that will send out a ring before that moment: the autoplay is told they will)
                if (autoAsking)
                {
                    for (int k = 0; k < thrown.Count && k < thrownChecks.Count; k++)
                    {
                        double next = thrownChecks[k];

                        if (next > now)
                            continue;

                        Vector2 at = thrownAt(thrown[k], next);

                        if (onScreen(f, at) && MathF.Abs(Vector2.Distance(player, at) - speed * (float)(now - next)) <= reach)
                            return true;
                    }
                }

                return false;
            }

            private int firstSince(double t)
            {
                int lo = 0, hi = shots.Count;

                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;

                    if (shots[mid].Time < t)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                return lo;
            }

            // ---- clearing ----

            private float clearRadius(BulletField f) => f.circleRadius * DanmakusuBalance.MERLIN_CLEAR_CIRCLES;

            private const double clear_ms = 60;

            public override bool Absorbs(double t)
            {
                foreach (var (time, _) in clears)
                {
                    if (t >= time && t <= time + clear_ms)
                        return true;
                }

                return false;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var (time, _) in clears)
                {
                    if (time <= to && time + clear_ms >= from)
                        return true;
                }

                return false;
            }

            /// <summary>A clearing takes every bullet within two circle radii of where it is, as it comes.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train)
                    return false;

                float reach = clearRadius(f) + emitter.HitRadius;

                foreach (var (time, at) in clears)
                {
                    if (t >= time && t <= time + clear_ms && Vector2.DistanceSquared(to, at) <= reach * reach)
                        return true;
                }

                return false;
            }

            /// <summary>And every slice of a beam that was within two circle radii of it then (see <see cref="IBeamBlasts"/>).</summary>
            public bool Blasted(BulletField f, Vector2 start, Vector2 velocity, double fired, double time)
            {
                float reach = clearRadius(f);

                foreach (var (at, centre) in clears)
                {
                    if (at < fired || at > time)
                        continue;

                    if (Vector2.DistanceSquared(start + velocity * (float)(at - fired), centre) <= reach * reach)
                        return true;
                }

                return false;
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                var white = Color4.White;
                var green = new Color4(0.35f, 1f, 0.45f, 1f);
                double life = lifeOf(f);

                // the rings
                for (int i = firstSince(t - life); i < shots.Count && shots[i].Time <= t; i++)
                {
                    if (decided[i] != 0)
                        continue;

                    f.stageRipple.AddRing(shots[i].Position, speed * (float)(t - shots[i].Time), halfWidth, DanmakusuBalance.MERLIN_RING_ALPHA, white);
                }

                foreach (var (time, at) in thrownRings)
                {
                    if (time <= t && t - time <= life)
                        f.stageRipple.AddRing(at, speed * (float)(t - time), halfWidth, DanmakusuBalance.MERLIN_RING_ALPHA, white);
                }

                // the clearings: a green flash out to how far they reach
                float reach = clearRadius(f);

                foreach (var (time, at) in clears)
                {
                    double age = t - time;

                    if (age < 0 || age > 400)
                        continue;

                    float u = (float)(age / 400.0);
                    f.stageRipple.AddRing(at, reach * smoothStep(u * 2.5f), 3f, 0.9f * (1f - u), green);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(reach * 2.6f), 0f, 0.6f * (1f - u), green, true);
                }

                // the warning: the circles and sliders that come glow, and green arrows close in on them (their numbers are gone, see DanmakusuPlayfield)
                for (int i = firstSince(t); i < shots.Count && shots[i].Shown <= t + 1; i++)
                {
                    var shot = shots[i];

                    if (t < shot.Shown || t > shot.Time)
                        continue;

                    float come = smoothStep((t - shot.Shown) / 200.0);
                    f.stageBack.Add(StageTexture.Glow, shot.Position, new Vector2(shot.Radius * 3.2f), 0f, DanmakusuBalance.MERLIN_GLOW_ALPHA * come, green, true);

                    if (shot.Slider is SliderZone zone)
                    {
                        for (int p = 0; p < zone.Points.Length; p += Math.Max(1, zone.Points.Length / 24))
                            f.stageBack.Add(StageTexture.Glow, zone.Points[p], new Vector2(zone.Radius * 3f), 0f, DanmakusuBalance.MERLIN_GLOW_ALPHA * 0.5f * come, green, true);
                    }

                    // (the arrows: a ring of them round it, pointing at its middle, closing in over and over)
                    float cycle = (float)(((t - shot.Shown) / 500.0) % 1.0);
                    float out_ = shot.Radius * (1.9f - 0.8f * cycle);
                    float size = shot.Radius * 0.55f;

                    for (int a = 0; a < DanmakusuBalance.MERLIN_ARROWS; a++)
                    {
                        float angle = a * MathF.Tau / DanmakusuBalance.MERLIN_ARROWS + (float)(t * 0.0006);
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        f.stageRipple.Add(StageTexture.SeijaArrow, shot.Position + way * out_, new Vector2(size), angle * 180f / MathF.PI + 180f, come * (0.4f + 0.6f * (1f - cycle)), green);
                    }
                }

                // her spinner's thrown circles and sliders: green, glowing
                foreach (var one in thrown)
                {
                    Vector2 at = thrownAt(one, t);

                    if (t < one.Time || !onScreen(f, at))
                        continue;

                    thrownEnds(f, one, at, out Vector2 a, out Vector2 b);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(f.circleRadius * (one.Slider ? 4.4f : 3.2f)), 0f, 0.5f, green, true);
                    f.stageRipple.AddRing(a, f.circleRadius * 0.92f, f.circleRadius * 0.08f, 0.95f, white);

                    if (one.Slider)
                        f.stageRipple.AddRing(b, f.circleRadius * 0.92f, f.circleRadius * 0.08f, 0.95f, white);
                }
            }
        }
    }
}
