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
        // Yuki: in her attacks the laser bursts every boss had before Marisa's Master Spark: five beams evenly round her (three with another boss on the screen, two with two more), the first aimed at the player
        // when the attack begins, turning slowly together (as fast as a spinner's beams) so that the nearest comes round to the player; their slices fly out twice as fast as a plain bullet. Since 2026-11-14 they
        // are as thick as a spinner's beams and grow as theirs do (an osu! circle's radius where they are fired, widening to the spinner beam's widest; they were thin). But every slice goes out (fading) as far
        // from her as the player was when it was fired, and every third leaves a scorch mark where it went out: a charred disc with a glowing edge, all the marks one seamless outline where they overlap, that
        // burns for eight beats and hurts like a slider body. Slices still flying when her attack ends (or she leaves) fly on out to the edge of the screen instead, and leave no mark.
        // The beams turn the way the player has gone, so it is all worked out step by step (a slice every 30 ms) and kept; time going back throws away what came after.
        // Her own shots, all through her section (in a spinner too): every four beats five rings of five ricochet stars, each from one of five places round her, two and a half circle radii away; a star ricochets
        // once off the edge of the screen and then flies out.
        // ======================================================================================================================================================

        private sealed class ScorchAct : StageAct
        {
            private struct Slice
            {
                public double Time;
                public Vector2 From;
                public float Angle; // (of the first beam)
                public float Reach; // (how far out it goes)
                public int Number;
                public double AttackEnd; // (when the attack it was fired in ends: one still flying then goes on out to the edge of the screen)
            }

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly List<Slice> slices = new List<Slice>();
            private readonly double[] volleys;
            private readonly Vector2[] volleyFrom;
            private readonly bool[] volleyKnown;
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();
            private readonly List<(double Time, Vector2 Where)> bounces = new List<(double, Vector2)>();
            private int beamCount = DanmakusuBalance.SPINNER_LASER_BEAMS;
            private int sliceNumber;

            public ScorchAct(BulletField f, BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.YUKI_STARS_EVERY_BEATS)
                    times.Add(at);

                volleys = times.ToArray();
                volleyFrom = new Vector2[volleys.Length];
                volleyKnown = new bool[volleys.Length];

                // (with another boss on the screen as many beams as any boss keeps then: half, or a third)
                int sharing = 1;

                foreach (var other in f.bosses)
                {
                    if (other != period && other.Rank != period.Rank && !other.Borrowed && other.ShootStart < period.End && other.End > period.ShootStart)
                        sharing = Math.Max(sharing, other.Rank + 1);
                }

                sharing = Math.Max(sharing, period.Rank + 1);

                if (sharing > 1)
                    beamCount = Math.Max(1, (DanmakusuBalance.SPINNER_LASER_BEAMS + sharing - 1) / sharing);
            }

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.YUKI_BEAM_SPEED;

            /// <summary>How big the scorch marks are: from the thin beams she had before 2026-11-14 (the marks stay as big as they were).</summary>
            private float scorchHalf => Math.Max(Period.StarRadius, 2f) * DanmakusuBalance.YUKI_BEAM_WIDTH;

            /// <summary>
            /// How wide a slice is when it has flown <paramref name="flown"/> px: as a spinner's beam (2026-11-14, "as thick and growing as spinner placeholder laserbeams") - an osu! circle's radius where it is
            /// fired, widening fast at first and ever more slowly to the spinner beam's widest over <see cref="DanmakusuBalance.LASER_GROWTH_DISTANCE"/>.
            /// </summary>
            private float widthAt(float flown)
            {
                if (Period.SpinnerBeamHalfWidth <= 0f)
                    return scorchHalf;

                float start = Period.SpinnerBeamStartHalfWidth;
                float grown = Math.Max(Period.SpinnerBeamHalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR, start);
                float u = Math.Clamp(flown / DanmakusuBalance.LASER_GROWTH_DISTANCE, 0f, 1f);
                return start + (grown - start) * (1f - (1f - u) * (1f - u));
            }

            private float widest => Period.SpinnerBeamHalfWidth > 0f ? Math.Max(Period.SpinnerBeamHalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR, Period.SpinnerBeamStartHalfWidth) : scorchHalf;

            /// <summary>
            /// How far a slice goes: as far as the player was when it was fired - but one still flying when its attack ends (or she leaves) flies on out to the edge of the screen, so the beam is not gone at once
            /// when she stops (2026-11-14).
            /// </summary>
            private float reachOf(BulletField f, in Slice slice)
                => slice.Time + slice.Reach / Math.Max(speed, 1e-3f) > slice.AttackEnd ? Vector2.Distance(f.stageMin, f.stageMax) + widest * 2f : slice.Reach;

            /// <summary>Does the slice go out where the player was (and may leave a scorch mark there)?</summary>
            private bool stopsShort(in Slice slice) => slice.Time + slice.Reach / Math.Max(speed, 1e-3f) <= slice.AttackEnd;

            private double scorchMs => beat * DanmakusuBalance.YUKI_SCORCH_BEATS;

            /// <summary>The scorch marks of her last attack burn on after her section: she stays until they are out.</summary>
            public override double MechanicsEnd => attacks.Count > 0 ? Math.Max(Period.End, attacks[^1].End + scorchMs + 3000) : Period.End;

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                shapes.Forget();

                while (slices.Count > 0 && slices[^1].Time > time)
                    slices.RemoveAt(slices.Count - 1);

                sliceNumber = slices.Count > 0 ? slices[^1].Number + 1 : 0;

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleys[k] > time)
                        volleyKnown[k] = false;
                }

                // (the stars are made again from where she was: they fly the same)
                ClearBullets();

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleyKnown[k])
                        shootStars(f, k, false);
                }
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the beams move on)

                if (f.failed || now < Start)
                    return;

                fire(f, now);

                for (int k = 0; k < volleys.Length && volleys[k] <= now; k++)
                {
                    if (volleyKnown[k])
                        continue;

                    volleyKnown[k] = true;
                    volleyFrom[k] = f.bossVisualOf(Period);
                    shootStars(f, k, true);
                }
            }

            // ---- the beams ----

            /// <summary>Fires the slices up to <paramref name="now"/>: from where she is, as far out as the player is, the beams turning a little towards the player.</summary>
            private void fire(BulletField f, double now)
            {
                double step = DanmakusuBalance.YUKI_SLICE_MS;
                Vector2 player = f.characterPosition;
                Vector2 from = f.bossVisualOf(Period);
                Vector2 toPlayer = player - from;
                float playerAngle = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                float reach = Math.Max(toPlayer.Length, f.circleRadius);
                float gap = MathF.Tau / beamCount;

                foreach (var (start, end) in attacks)
                {
                    if (now < start || end <= start)
                        continue;

                    double last = slices.Count > 0 && slices[^1].Time >= start ? slices[^1].Time : double.NaN;
                    float angle = double.IsNaN(last) ? playerAngle : slices[^1].Angle;
                    int guard = 0;

                    for (double t = double.IsNaN(last) ? start : last + step; t < Math.Min(now, end) + 1e-6 && guard++ < 2000; t += step)
                    {
                        if (!double.IsNaN(last) || t > start)
                        {
                            // (they turn together, the nearest beam towards the player, as fast as a spinner's beams)
                            float off = MathF.IEEERemainder(playerAngle - angle, gap);
                            float most = Period.AngularSpeed * (float)step;
                            angle += Math.Clamp(off, -most, most);
                        }

                        slices.Add(new Slice { Time = t, From = from, Angle = angle, Reach = reach, Number = sliceNumber++, AttackEnd = end });
                        last = t;
                    }
                }
            }

            /// <summary>The first slice that can still be seen or leave a mark at <paramref name="t"/> (they are kept in time order).</summary>
            private int firstShown(BulletField f, double t)
            {
                float diagonal = Vector2.Distance(f.stageMin, f.stageMax);
                double bound = t - scorchMs - diagonal / Math.Max(speed, 1e-3f) - 100;
                int low = 0, high = slices.Count;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (slices[middle].Time < bound)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            private double outAt(in Slice slice) => slice.Time + slice.Reach / Math.Max(speed, 1e-3f);

            private static Vector2 wayOf(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            // (the runs of a beam: drawn, or cut into the pieces that hurt)
            private bool drawing;
            private int drawnBeams, drawnTotal;
            private Color4 runColour, runBorder;
            private readonly List<(Vector2 A, Vector2 B, float Half)> hitSegments = new List<(Vector2, Vector2, float)>();
            private readonly List<float> widths = new List<float>();
            private readonly MomentShapes shapes = new MomentShapes(); // (the beams and marks at the moments the autoplay asks about, kept for the frame)

            /// <summary>
            /// Beam <paramref name="beam"/> at <paramref name="t"/>, from her out: every run of slices that are still flying (a slice that has gone out cuts it, when an older one fired further still flies on) is drawn,
            /// or its pieces are kept for the hit check.
            /// </summary>
            private void beamAt(BulletField f, int beam, double t, int from)
            {
                line.Clear();
                fades.Clear();
                widths.Clear();

                float gap = MathF.Tau / beamCount;
                float fadeLength = Math.Max(scorchHalf * 2f, 12f);
                bool started = false;

                for (int i = slices.Count - 1; i >= from; i--)
                {
                    var slice = slices[i];

                    if (slice.Time > t)
                        continue;

                    float flown = speed * (float)(t - slice.Time);
                    float reach = reachOf(f, slice);

                    if (flown >= reach)
                    {
                        runDone(f);
                        continue;
                    }

                    // (the newest run starts at her, while she still fires)
                    if (!started)
                    {
                        if (t < slice.AttackEnd)
                        {
                            line.Add(slice.From);
                            fades.Add(1f);
                            widths.Add(widthAt(0f));
                        }

                        started = true;
                    }

                    line.Add(slice.From + wayOf(slice.Angle + beam * gap) * flown);
                    fades.Add(smoothStep((reach - flown) / fadeLength));
                    widths.Add(widthAt(flown));
                }

                runDone(f);
            }

            private void runDone(BulletField f)
            {
                if (line.Count >= 2)
                {
                    if (drawing)
                    {
                        f.addPlainBeam(line, widest, runColour, runBorder, ref drawnBeams, ref drawnTotal, widths);

                        for (int i = 0; i < fades.Count; i++)
                            f.laserFadePointBuffer[drawnTotal - line.Count + i] = fades[i];
                    }
                    else
                    {
                        for (int i = 0; i + 1 < line.Count; i++)
                        {
                            if (fades[i] > 0.5f && fades[i + 1] > 0.5f)
                                hitSegments.Add((line[i], line[i + 1], Math.Max(widths[i], widths[i + 1])));
                        }
                    }
                }

                line.Clear();
                fades.Clear();
                widths.Clear();
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0 || slices.Count == 0)
                    return;

                int style = f.comboStyle(1, true, Period.Rank);
                runColour = f.palette[style % f.palette.Length];
                runBorder = f.borderPalette[style % f.borderPalette.Length];
                drawing = true;
                drawnBeams = beams;
                drawnTotal = pointTotal;

                int from = firstShown(f, t);

                for (int beam = 0; beam < beamCount; beam++)
                    beamAt(f, beam, t, from);

                beams = drawnBeams;
                pointTotal = drawnTotal;
                drawing = false;
            }

            // ---- the scorch marks ----

            /// <summary>How much a scorch mark left by <paramref name="slice"/> burns at <paramref name="t"/> (0 = not there).</summary>
            private float burning(in Slice slice, double t)
            {
                if (slice.Number % DanmakusuBalance.YUKI_SCORCH_EVERY != 0 || !stopsShort(slice))
                    return 0f;

                double age = t - outAt(slice);

                if (age < 0 || age > scorchMs)
                    return 0f;

                return Math.Min(smoothStep(age / 120.0), 1f - smoothStep((age - (scorchMs - beat)) / beat));
            }

            private Vector2 markOf(in Slice slice, int beam) => slice.From + wayOf(slice.Angle + beam * MathF.Tau / beamCount) * slice.Reach;

            /// <summary>
            /// The scorch marks (2026-11-14: more visible, with an edge): every mark is a charred disc with a glowing ember edge round it. The edges of all of them are drawn first and the charred insides over
            /// them, so where marks overlap the inner edges are covered: one seamless outline round all of the burnt ground. Embers flicker in the middles.
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || slices.Count == 0)
                    return;

                float radius = scorchHalf * DanmakusuBalance.YUKI_SCORCH_SIZE;
                float edge = Math.Max(radius * DanmakusuBalance.YUKI_SCORCH_EDGE, 2f);
                var char_colour = new Color4(0.13f, 0.065f, 0.035f, 1f);
                var rim = new Color4(1f, 0.5f, 0.12f, 1f);
                var ember = new Color4(1f, 0.42f, 0.08f, 1f);
                int first = firstShown(f, t);

                for (int pass = 0; pass < 3; pass++)
                {
                    for (int i = first; i < slices.Count; i++)
                    {
                        var slice = slices[i];
                        float level = burning(slice, t);

                        if (level < 0.01f)
                            continue;

                        for (int beam = 0; beam < beamCount; beam++)
                        {
                            Vector2 at = markOf(slice, beam);

                            switch (pass)
                            {
                                case 0: // (the edges, under everything of the marks)
                                    f.stageOrbs.Add(StageTexture.Orb, at, new Vector2(2f * (radius + edge)), 0f, level, rim);
                                    break;

                                case 1: // (the charred insides, over every edge)
                                    f.stageOrbs.Add(StageTexture.Orb, at, new Vector2(2f * radius), 0f, level, char_colour);
                                    break;

                                default: // (embers)
                                    float flicker = 0.75f + 0.25f * MathF.Sin((float)(t * 0.013) + slice.Number * 1.7f + beam * 2.3f);
                                    f.stageOrbs.Add(StageTexture.Glow, at, new Vector2(radius * 1.6f), 0f, 0.5f * level * flicker, ember, true);
                                    break;
                            }
                        }
                    }
                }
            }

            /// <summary>The beams and the scorch marks hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || slices.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;

                // (asked about the same moment again - the autoplay, for many places: the marks and beams of that moment are worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    shapesAt(f, now, fresh);
                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                int from = firstShown(f, now);

                // the marks
                float markHit = scorchHalf * DanmakusuBalance.YUKI_SCORCH_SIZE * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = from; i < slices.Count; i++)
                {
                    var slice = slices[i];

                    if (burning(slice, now) < 0.5f)
                        continue;

                    for (int beam = 0; beam < beamCount; beam++)
                    {
                        if (Vector2.DistanceSquared(player, markOf(slice, beam)) <= markHit * markHit)
                            return true;
                    }
                }

                // the beams (as wide as they are there)
                hitSegments.Clear();

                for (int beam = 0; beam < beamCount; beam++)
                    beamAt(f, beam, now, from);

                foreach (var (a, b, half) in hitSegments)
                {
                    if (distanceToSegment(player, a, b) <= half * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            /// <summary>What hurts at <paramref name="now"/>, as runs for a kept moment: every burning mark a point, every piece of a beam a run of two as wide as its wider end.</summary>
            private void shapesAt(BulletField f, double now, MomentShapes.Moment moment)
            {
                int from = firstShown(f, now);
                float markHit = scorchHalf * DanmakusuBalance.YUKI_SCORCH_SIZE * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = from; i < slices.Count; i++)
                {
                    var slice = slices[i];

                    if (burning(slice, now) < 0.5f)
                        continue;

                    for (int beam = 0; beam < beamCount; beam++)
                    {
                        moment.Point(markOf(slice, beam), markHit);
                        moment.EndRun();
                    }
                }

                hitSegments.Clear();

                for (int beam = 0; beam < beamCount; beam++)
                    beamAt(f, beam, now, from);

                foreach (var (a, b, half) in hitSegments)
                {
                    float reach = half * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                    moment.Point(a, reach);
                    moment.Point(b, reach);
                    moment.EndRun();
                }
            }

            // ---- her stars ----

            /// <summary>Volley <paramref name="k"/>: from five places round where she was, a ring of five ricochet stars each, flying out from her; each ricochets once and then flies out of the screen.</summary>
            private void shootStars(BulletField f, int k, bool flash)
            {
                double t = volleys[k];
                float radius = Math.Max(Period.StarRadius, 2f);
                float starSpeed = stageBulletSpeed(Period);
                float place = f.circleRadius * DanmakusuBalance.YUKI_STAR_PLACE_CIRCLES;
                float turn = k * 2.3999632f;

                for (int p = 0; p < DanmakusuBalance.YUKI_STAR_PLACES; p++)
                {
                    float placeAngle = turn + p * MathF.Tau / DanmakusuBalance.YUKI_STAR_PLACES;
                    Vector2 at = volleyFrom[k] + wayOf(placeAngle) * place;

                    for (int s = 0; s < DanmakusuBalance.YUKI_STARS; s++)
                    {
                        Vector2 velocity = wayOf(placeAngle + s * MathF.Tau / DanmakusuBalance.YUKI_STARS) * starSpeed;
                        bounces.Clear();
                        var legs = f.bouncingLegs(t, at, velocity, 1, bounces);
                        var lastLeg = legs[^1];
                        double gone = lastLeg.T + f.toWall(lastLeg.P, lastLeg.V, out _) + radius * 3f / Math.Max(starSpeed, 1e-3f);

                        AddBullet(new StageBullet
                        {
                            T0 = t,
                            P0 = at,
                            V = velocity,
                            Legs = legs,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = f.comboStyle(p + s, true, Period.Rank),
                            Shape = BulletShape.Star,
                            Spin = 0.004f * (s % 2 == 0 ? 1f : -1f),
                            SpinPhase = s,
                            Until = gone,
                        });

                        if (flash)
                        {
                            foreach (var (time, where) in bounces)
                                f.addFlash(time, where, radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                        }
                    }
                }
            }
        }
    }
}
