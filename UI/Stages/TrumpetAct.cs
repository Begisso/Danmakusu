using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        private readonly List<TrumpetAct> trumpetActs = new List<TrumpetAct>();

        /// <summary>Merlin's warning: the screen flashes red before each of her attacks ends (2026-11-21).</summary>
        private Box merlinBackground = null!;

        /// <summary>Merlin's attacks: the map's circles and sliders do not hurt then (see <see cref="TrumpetAct"/>).</summary>
        private (double Start, double End)[] trumpetWindows = Array.Empty<(double, double)>();

        /// <summary>Has Merlin attacks in this map (the playfield colours her circles and sliders green).</summary>
        public bool HasTrumpet => trumpetWindows.Length > 0;

        /// <summary>Is <paramref name="p"/> in one of Merlin's safe places at <paramref name="t"/> (round her circles and sliders, along the follow points between them): nothing hurts there.</summary>
        private bool merlinShields(Vector2 p, double t)
        {
            if (failed)
                return false;

            foreach (var act in trumpetActs)
            {
                if (t >= act.Start - 1 && t <= act.End && act.Shields(this, p, t))
                    return true;
            }

            return false;
        }

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
        // slider at its head), sends out a thick white ring instead, as Raiko's ripples go out - not see-through, four times as thick as their line - which hurts like a slider body. But when the player is on
        // a circle as it is hit (a slider: on its body), everything within two circle radii of it is cleared, bullets and slices of beams, with a green flash, and its ring goes out half as solid and harmless
        // (2026-11-20; it sent out none before). The rings are drawn as exact arcs (2026-11-20: they were pictures stretched over the screen).
        // As a warning the circles and sliders of her attacks are green, glowing, and their numbers are gone: a ring of green arrows closes in on each as it comes (the playfield colours them, see
        // DanmakusuPlayfield). In her spinners she throws six circles and sliders round her every eight beats, flying away from her; each sends out such a ring every two beats - half as solid and harmless
        // while the player is on it.
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
                public Osu.Objects.OsuHitObject? Source;
                public Vector2 End; // (where it ends: a slider's end, after its repeats)
            }

            /// <summary>The follow points from one of her circles and sliders to the next of its combo: there from when the next comes until it is hit, as wide as a circle (2026-11-21).</summary>
            private struct FollowPath
            {
                public Vector2 From, To;
                public double Shown, Until;
                public float HalfWidth;
            }

            private readonly List<FollowPath> follows = new List<FollowPath>();

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
            private readonly List<(double Time, Vector2 At, bool Faint)> thrownRings = new List<(double, Vector2, bool)>(); // (Faint: the player was on it - half as solid, harmless)
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
                            shots.Add(new Shot { Time = zone.Time, Position = zone.Position, Radius = zone.Radius, Shown = zone.Time - (zone.Source?.TimePreempt ?? 600), Source = zone.Source, End = zone.Position });
                    }

                    foreach (var zone in f.sliderZones)
                    {
                        if (zone.Points.Length > 0 && inAttack(zone.StartTime))
                            shots.Add(new Shot
                            {
                                Time = zone.StartTime, Position = zone.Points[0], Radius = zone.Radius, Slider = zone, Shown = zone.StartTime - (zone.Source?.TimePreempt ?? 600), Source = zone.Source,
                                End = zone.Source is Osu.Objects.Slider slider ? zone.Points[0] + (slider.StackedEndPosition - slider.StackedPosition) : zone.Points[^1],
                            });
                    }

                    shots.Sort((a, b) => a.Time.CompareTo(b.Time));

                    for (int i = 0; i < shots.Count; i++)
                        decided.Add(-1);

                    // (the follow points: from each to the next of its combo, as osu! draws them)
                    for (int i = 0; i + 1 < shots.Count; i++)
                    {
                        var (a, b) = (shots[i], shots[i + 1]);

                        if (a.Source == null || b.Source == null || b.Source.ComboIndex != a.Source.ComboIndex || b.Source.IndexInCurrentCombo != a.Source.IndexInCurrentCombo + 1)
                            continue;

                        if (Vector2.Distance(a.End, b.Position) < 1f)
                            continue;

                        follows.Add(new FollowPath { From = a.End, To = b.Position, Shown = b.Shown, Until = b.Time, HalfWidth = b.Radius * DanmakusuBalance.MERLIN_FOLLOW_WIDTH });
                    }
                }

                End = period.End + 8000; // (the last rings go out over the screen after her section)
                AbsorbFrom = Start;
                AbsorbTo = End;
                attacks = BossOps.Attacks(period).FindAll(a => a.End > a.Start);
            }

            private readonly List<(double Start, double End)> attacks;

            /// <summary>
            /// How red her rings and the screen are at <paramref name="t"/> (0..1): over the last MERLIN_WARN_BEATS of each of her attacks they flash red MERLIN_WARN_FLASHES times, warning that the circles and
            /// sliders will hurt again (2026-11-21, "circles and bg flashes red before attack ends").
            /// </summary>
            private float warning(double t)
            {
                double span = beat * DanmakusuBalance.MERLIN_WARN_BEATS;

                foreach (var (_, end) in attacks)
                {
                    if (t >= end || t < end - span)
                        continue;

                    double u = 1 - (end - t) / span;
                    return 0.5f - 0.5f * MathF.Cos((float)(u * DanmakusuBalance.MERLIN_WARN_FLASHES * MathF.Tau));
                }

                return 0f;
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

                        // (the player on it: its ring goes out half as solid and harmless - 2026-11-20; it sent none before)
                        thrownRings.Add((thrownChecks[k], at, onThrown(f, one, at)));
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

                // (round her circles and sliders her rings do not hurt: 2026-11-21)
                if (inSafePlace(f, player, now))
                    return false;

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

                foreach (var (time, at, faint) in thrownRings)
                {
                    if (faint || time > now || now - time > life)
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

            /// <summary>A ring whose middle line is <paramref name="middle"/> from <paramref name="centre"/>, as thick as it hurts, with a black outline (2026-11-21).</summary>
            private void ring(BulletField f, Vector2 centre, float middle, float alpha, Color4 colour)
            {
                float outer = middle + halfWidth;
                float edge = Math.Max(halfWidth * 0.25f, 1.5f);
                f.stageRipple.AddSolidRing(centre, outer + edge, Math.Min(2f * (halfWidth + edge), outer + edge), alpha, Color4.Black, false);
                f.stageRipple.AddSolidRing(centre, outer, Math.Min(2f * halfWidth, outer), alpha, colour, false);
            }

            /// <summary>How far round one of her circles and sliders the safe place reaches (in its radii): nothing hurts in it.</summary>
            private static float safeRadii => DanmakusuBalance.MERLIN_SAFE_RADII;

            /// <summary>Nothing hurts the character at <paramref name="p"/> at <paramref name="t"/>: it is in a safe place (2026-11-21, "player is immortal").</summary>
            public bool Shields(BulletField f, Vector2 p, double t) => inSafePlace(f, p, t);

            /// <summary>
            /// Is <paramref name="p"/> in the safe place round one of her circles and sliders at <paramref name="t"/> (2026-11-21, "circle shape is green gradient around osu objects and harmless around them"):
            /// round a circle from when it comes until it is hit, along a slider until it ends, round a thrown one while it flies - and (2026-11-21, later) along the follow points from one to the next. Nothing hurts
            /// there (<see cref="Shields"/>: "player is immortal").
            /// </summary>
            private bool inSafePlace(BulletField f, Vector2 p, double t)
            {
                foreach (var follow in follows)
                {
                    if (t >= follow.Shown && t <= follow.Until && distanceToSegment(p, follow.From, follow.To) <= follow.HalfWidth)
                        return true;
                }

                for (int i = firstSince(t - 20000); i < shots.Count && shots[i].Shown <= t; i++)
                {
                    var shot = shots[i];

                    if (shot.Slider is SliderZone zone)
                    {
                        if (t <= zone.EndTime && zone.Touches(p - zone.FallOffset(t) - (zone.FallSpeed > 0f ? f.CatchLanding : Vector2.Zero), zone.Radius * (safeRadii - 1f)))
                            return true;
                    }
                    else if (t <= shot.Time && Vector2.Distance(p, shot.Position) <= shot.Radius * safeRadii)
                        return true;
                }

                foreach (var one in thrown)
                {
                    if (t < one.Time)
                        continue;

                    Vector2 at = thrownAt(one, t);

                    if (onScreen(f, at))
                    {
                        thrownEnds(f, one, at, out Vector2 a, out Vector2 b);

                        if (distanceToSegment(p, a, b) <= f.circleRadius * safeRadii)
                            return true;
                    }
                }

                return false;
            }

            // (the safe places of the moment, as circles - along a slider, one every so often: where her rings go over one they are green)
            private readonly List<(Vector2 Centre, float Radius)> safeCircles = new List<(Vector2, float)>();

            private void gatherSafeCircles(BulletField f, double t)
            {
                safeCircles.Clear();

                for (int i = firstSince(t - 20000); i < shots.Count && shots[i].Shown <= t; i++)
                {
                    var shot = shots[i];

                    if (shot.Slider is SliderZone zone)
                    {
                        if (t > zone.EndTime || zone.Points.Length == 0)
                            continue;

                        float safe = zone.Radius * safeRadii;
                        float gap = Math.Max(safe * 0.6f, 4f);
                        Vector2 last = zone.Points[0];
                        safeCircles.Add((last, safe));

                        for (int p = 1; p < zone.Points.Length; p++)
                        {
                            if (Vector2.Distance(zone.Points[p], last) >= gap || p == zone.Points.Length - 1)
                            {
                                last = zone.Points[p];
                                safeCircles.Add((last, safe));
                            }
                        }
                    }
                    else if (t <= shot.Time)
                        safeCircles.Add((shot.Position, shot.Radius * safeRadii));
                }

                foreach (var one in thrown)
                {
                    Vector2 at = thrownAt(one, t);

                    if (t < one.Time || !onScreen(f, at))
                        continue;

                    thrownEnds(f, one, at, out Vector2 a, out Vector2 b);
                    float safe = f.circleRadius * safeRadii;
                    safeCircles.Add((a, safe));

                    if (one.Slider)
                    {
                        safeCircles.Add(((a + b) * 0.5f, safe));
                        safeCircles.Add((b, safe));
                    }
                }
            }

            /// <summary>
            /// Where the ring round <paramref name="centre"/> (its middle line <paramref name="middle"/> out) goes over a safe place it is green, fading out towards the edges of the place (2026-11-21, "growing circles have
            /// green gradients around osu object safe spots"): three green arcs over it, each narrower than the last.
            /// </summary>
            private void greenOver(BulletField f, Vector2 centre, float middle, float alpha, Color4 green)
            {
                if (middle < 1f || safeCircles.Count == 0)
                    return;

                float outer = middle + halfWidth, thick = Math.Min(2f * halfWidth, outer);

                foreach (var (spot, safe) in safeCircles)
                {
                    float d = Vector2.Distance(spot, centre);

                    if (MathF.Abs(d - middle) > safe + halfWidth)
                        continue;

                    float from, sweep;

                    if (d < 1e-3f || d + middle <= safe)
                    {
                        from = 0f;
                        sweep = MathF.Tau; // (all of the ring is in it)
                    }
                    else
                    {
                        float reach = safe + halfWidth * 0.5f;
                        float half = MathF.Acos(Math.Clamp((middle * middle + d * d - reach * reach) / (2f * middle * d), -1f, 1f));
                        from = MathF.Atan2(spot.Y - centre.Y, spot.X - centre.X) - half;
                        sweep = 2f * half;
                    }

                    for (int step = 0; step < 3; step++)
                    {
                        float share = 1f - step / 3f;
                        f.stageRipple.AddArc(centre, outer, thick, from + sweep * (1f - share) * 0.5f, sweep * share, alpha * DanmakusuBalance.MERLIN_RING_GREEN, green);
                    }
                }
            }

            /// <summary>The follow points: see-through beams as wide as a circle, from one of her circles and sliders to the next (the arrows along them are drawn by Draw).</summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || follows.Count == 0)
                    return;

                var colour = new Color4(0.75f, 1f, 0.8f, 1f);
                var border = DanmakusuBalance.BorderColour(colour);

                foreach (var follow in follows)
                {
                    if (t < follow.Shown || t > follow.Until)
                        continue;

                    float alpha = DanmakusuBalance.MERLIN_FOLLOW_ALPHA * smoothStep((t - follow.Shown) / 200.0);
                    followLine.Clear();
                    followLine.Add(follow.From);
                    followLine.Add(follow.To);
                    followFades.Clear();
                    followFades.Add(alpha);
                    followFades.Add(alpha);
                    f.addPlainBeam(followLine, follow.HalfWidth, colour, border, ref beams, ref pointTotal, null, followFades);
                }
            }

            private readonly List<Vector2> followLine = new List<Vector2>();
            private readonly List<float> followFades = new List<float>();

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                var white = Color4.White;
                var green = new Color4(0.35f, 1f, 0.45f, 1f);
                double life = lifeOf(f);

                // the rings: exact arcs (2026-11-20: they were pictures stretched over the screen, not smooth); the ring of an object the player was on goes out half as solid, and is harmless.
                // Before her attack ends they flash red, and so does the screen (2026-11-21)
                float solid = DanmakusuBalance.MERLIN_RING_ALPHA, faint = solid * DanmakusuBalance.MERLIN_FAINT;
                float warn = warning(t);
                var red = new Color4(1f, 0.12f, 0.1f, 1f);
                var ringColour = new Color4(1f + (red.R - 1f) * warn, 1f + (red.G - 1f) * warn, 1f + (red.B - 1f) * warn, 1f);

                if (warn > 0.001f)
                {
                    if (f.hasScreenBounds)
                    {
                        f.merlinBackground.Position = f.screenMin;
                        f.merlinBackground.Size = f.screenMax - f.screenMin;
                    }

                    f.merlinBackground.Alpha = Math.Max(f.merlinBackground.Alpha, warn * DanmakusuBalance.MERLIN_WARN_BACKGROUND_ALPHA);
                }

                gatherSafeCircles(f, t);

                for (int i = firstSince(t - life); i < shots.Count && shots[i].Time <= t; i++)
                {
                    if (decided[i] < 0)
                        continue;

                    ring(f, shots[i].Position, speed * (float)(t - shots[i].Time), decided[i] == 1 ? faint : solid, ringColour);
                    greenOver(f, shots[i].Position, speed * (float)(t - shots[i].Time), decided[i] == 1 ? faint : solid, green);
                }

                foreach (var (time, at, isFaint) in thrownRings)
                {
                    if (time <= t && t - time <= life)
                    {
                        ring(f, at, speed * (float)(t - time), isFaint ? faint : solid, ringColour);
                        greenOver(f, at, speed * (float)(t - time), isFaint ? faint : solid, green);
                    }
                }

                // the follow points from one to the next: green arrows running along them, from the one to the next, above the rings (their see-through beams: DrawBeams)
                foreach (var follow in follows)
                {
                    if (t < follow.Shown || t > follow.Until)
                        continue;

                    float shown = smoothStep((t - follow.Shown) / 200.0);
                    Vector2 way = follow.To - follow.From;
                    float length = way.Length;
                    way /= length;
                    float gap = follow.HalfWidth * 2.2f, size = follow.HalfWidth * 0.9f;
                    float shift = (float)((t - follow.Shown) * DanmakusuBalance.MERLIN_FOLLOW_ARROW_SPEED % gap);
                    float degrees = MathF.Atan2(way.Y, way.X) * 180f / MathF.PI;

                    for (float s = shift; s < length; s += gap)
                    {
                        // (they come in at the one end and go out at the other)
                        float ends = Math.Min(smoothStep(s / gap), smoothStep((length - s) / gap));
                        f.stageTree.Add(StageTexture.SeijaArrow, follow.From + way * s, new Vector2(size), degrees, 0.9f * shown * ends, green);
                    }
                }

                // the clearings: a green flash out to how far they reach
                float reach = clearRadius(f);

                foreach (var (time, at) in clears)
                {
                    double age = t - time;

                    if (age < 0 || age > 400)
                        continue;

                    float u = (float)(age / 400.0);
                    f.stageRipple.AddSolidRing(at, reach * smoothStep(u * 2.5f) + 1.5f, 3f, 0.9f * (1f - u), green);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(reach * 2.6f), 0f, 0.6f * (1f - u), green, true);
                }

                // the safe places round her circles and sliders: a green gradient circle (2026-11-21; a soft glow before), along a slider until it ends
                for (int i = firstSince(t - 20000); i < shots.Count && shots[i].Shown <= t + 1; i++)
                {
                    var shot = shots[i];
                    double over = shot.Slider?.EndTime ?? shot.Time;

                    if (t < shot.Shown || t > over)
                        continue;

                    float come = smoothStep((t - shot.Shown) / 200.0);
                    float safe = shot.Radius * safeRadii;
                    f.stageBack.Add(StageTexture.Glow, shot.Position, new Vector2(safe * 2.4f), 0f, DanmakusuBalance.MERLIN_GLOW_ALPHA * come, green, true);
                    f.stageRipple.AddSolidRing(shot.Position, safe, Math.Max(safe * 0.05f, 1.5f), 0.7f * come, green);

                    if (shot.Slider is SliderZone zone)
                    {
                        for (int p = 0; p < zone.Points.Length; p += Math.Max(1, zone.Points.Length / 24))
                            f.stageBack.Add(StageTexture.Glow, zone.Points[p], new Vector2(zone.Radius * safeRadii * 2.2f), 0f, DanmakusuBalance.MERLIN_GLOW_ALPHA * 0.6f * come, green, true);
                    }

                    if (t > shot.Time)
                        continue;

                    // (the arrows: a ring of them round it, pointing at its middle, closing in over and over)
                    float cycle = (float)(((t - shot.Shown) / 500.0) % 1.0);
                    float out_ = shot.Radius * (1.9f - 0.8f * cycle);
                    float size = shot.Radius * 0.55f;

                    for (int a = 0; a < DanmakusuBalance.MERLIN_ARROWS; a++)
                    {
                        float angle = a * MathF.Tau / DanmakusuBalance.MERLIN_ARROWS + (float)(t * 0.0006);
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        f.stageTree.Add(StageTexture.SeijaArrow, shot.Position + way * out_, new Vector2(size), angle * 180f / MathF.PI + 180f, come * (0.4f + 0.6f * (1f - cycle)), green); // (above her rings: 2026-11-21)
                    }
                }

                // her spinner's thrown circles and sliders: green, glowing
                foreach (var one in thrown)
                {
                    Vector2 at = thrownAt(one, t);

                    if (t < one.Time || !onScreen(f, at))
                        continue;

                    thrownEnds(f, one, at, out Vector2 a, out Vector2 b);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(f.circleRadius * safeRadii * (one.Slider ? 3.4f : 2.4f)), 0f, 0.5f, green, true);
                    f.stageRipple.AddSolidRing(a, f.circleRadius * safeRadii, Math.Max(f.circleRadius * safeRadii * 0.05f, 1.5f), 0.7f, green);

                    if (one.Slider)
                        f.stageRipple.AddSolidRing(b, f.circleRadius * safeRadii, Math.Max(f.circleRadius * safeRadii * 0.05f, 1.5f), 0.7f, green);

                    f.stageRipple.AddSolidRing(a, f.circleRadius, f.circleRadius * 0.16f, 0.95f, white);

                    if (one.Slider)
                        f.stageRipple.AddSolidRing(b, f.circleRadius, f.circleRadius * 0.16f, 0.95f, white);
                }
            }
        }
    }
}
