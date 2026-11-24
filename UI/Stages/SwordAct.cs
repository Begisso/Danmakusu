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
        // Meira (2026-11-24): no pauses. Her section goes in rounds of MEIRA_CYCLE_BEATS. In the first MEIRA_SLIDING_BEATS of a round she slides after the player MEIRA_SLIDES times, as Elly does (each slide
        // starting evenly over those beats and lasting MEIRA_SLIDE_LONGEST_BEATS at most, at MEIRA_SLIDE_SPEED times a plain bullet's speed, and over as soon as she is as near as Elly comes); behind her a
        // thick beam (MEIRA_TRACE_WIDTH circle radii each side) stays where she went, hurting like a slider body. In the rest of the round she stands and shoots streams of fans at the player (the planner's).
        // When the next round begins (or her section ends) the beams of the round go: every MEIRA_SLICE_CIRCLES circle radii along them a ring of MEIRA_SLICE_BULLETS basic bullets, MEIRA_SLICE_SIZE as big as
        // a plain one, flies out from there.
        // Where she slides depends on where the player is, so it is followed step by step (MEIRA_STEP_MS) and kept: going back in time takes the steps back.
        // ======================================================================================================================================================

        private sealed class SwordAct : StageAct
        {
            private sealed class Slide
            {
                public double Start;
                public double Until;
                public readonly List<Vector2> Steps = new List<Vector2>(); // (a place every MEIRA_STEP_MS from its start)
                public bool Stopped; // (she got near enough: the rest of it she stands)
            }

            private readonly double beat;
            private readonly List<Slide> slides = new List<Slide>();
            private readonly List<double> slideStarts = new List<double>();
            private readonly List<double> slideEnds = new List<double>();
            private readonly List<Vector2> line = new List<Vector2>();
            private Vector2 standing;
            private bool known;
            private int dissolved; // (how many rounds' beams have gone into rings)

            public SwordAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;

                double cycle = beat * DanmakusuBalance.MEIRA_CYCLE_BEATS;
                double gap = beat * DanmakusuBalance.MEIRA_SLIDING_BEATS / DanmakusuBalance.MEIRA_SLIDES;

                for (int k = 0; Start + k * cycle < End; k++)
                {
                    for (int j = 0; j < DanmakusuBalance.MEIRA_SLIDES; j++)
                    {
                        double start = Start + k * cycle + j * gap;

                        if (start >= End)
                            break;

                        slideStarts.Add(start);
                        slideEnds.Add(Math.Min(start + Math.Min(gap, beat * DanmakusuBalance.MEIRA_SLIDE_LONGEST_BEATS), End));
                    }
                }
            }

            private double cycleMs => beat * DanmakusuBalance.MEIRA_CYCLE_BEATS;

            /// <summary>When the beams of round <paramref name="k"/> go: as the next round begins, or as her section ends.</summary>
            private double goneAt(int k) => Math.Min(Start + (k + 1) * cycleMs, End);

            private int roundOf(double t) => (int)Math.Floor((t - Start) / cycleMs + 1e-9);

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.MEIRA_SLIDE_SPEED;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.MEIRA_TRACE_WIDTH;

            private static float near(BulletField f) => (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.ELLY_RADIUS * DanmakusuBalance.ELLY_NEAR;

            public override void Reset(BulletField f, double time)
            {
                // (back to the step she was at then; the slides after it are made again)
                slides.RemoveAll(s => s.Start > time);

                foreach (var slide in slides)
                {
                    int keep = Math.Max(1, (int)Math.Floor((Math.Min(time, slide.Until) - slide.Start) / DanmakusuBalance.MEIRA_STEP_MS) + 1);

                    if (slide.Steps.Count > keep)
                        slide.Steps.RemoveRange(keep, slide.Steps.Count - keep);

                    slide.Stopped = false;
                }

                known = slides.Count > 0;

                if (known)
                    standing = slides[^1].Steps[^1];

                ClearBullets();
                dissolved = 0;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                if (!known)
                {
                    standing = f.bossVisualOf(Period);
                    known = true;
                }

                // (every slide whose time has come starts from where the one before ended; each goes on step by step until now)
                if (slides.Count > 0)
                    extend(f, slides[^1], now);

                while (slides.Count < slideStarts.Count && slideStarts[slides.Count] <= now)
                {
                    int index = slides.Count;
                    var slide = new Slide { Start = slideStarts[index], Until = slideEnds[index] };
                    slide.Steps.Add(slides.Count > 0 ? slides[^1].Steps[^1] : standing);
                    slides.Add(slide);
                    extend(f, slide, now);
                }

                if (slides.Count > 0)
                    standing = slides[^1].Steps[^1];

                // (the beams of every round that is over go into rings)
                while (dissolved * cycleMs + Start < End && goneAt(dissolved) <= now)
                {
                    dissolve(f, dissolved);
                    dissolved++;
                }
            }

            /// <summary>She goes on in <paramref name="slide"/> step by step until <paramref name="now"/> (or its end), after where the player is, and stops as near as Elly comes.</summary>
            private void extend(BulletField f, Slide slide, double now)
            {
                double step = DanmakusuBalance.MEIRA_STEP_MS;
                double until = Math.Min(now, slide.Until);
                float reach = near(f);

                while (!slide.Stopped && slide.Start + slide.Steps.Count * step <= until)
                {
                    Vector2 at = slide.Steps[^1];
                    Vector2 toPlayer = f.characterPosition - at;
                    float away = toPlayer.Length;

                    if (away <= reach)
                    {
                        slide.Stopped = true;
                        break;
                    }

                    Vector2 next = at + toPlayer * (Math.Min(speed * (float)step, away - reach) / away);
                    slide.Steps.Add(Vector2.Clamp(next, f.stageMin, f.stageMax));
                }
            }

            /// <summary>Where she is in a slide at <paramref name="t"/>: along its steps, standing after the last.</summary>
            private static Vector2 placeIn(Slide slide, double t)
            {
                double u = (t - slide.Start) / DanmakusuBalance.MEIRA_STEP_MS;

                if (u <= 0)
                    return slide.Steps[0];

                int i = (int)Math.Floor(u);
                return i + 1 < slide.Steps.Count ? Vector2.Lerp(slide.Steps[i], slide.Steps[i + 1], (float)(u - i)) : slide.Steps[^1];
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = standing;

                if (!known || now < Start || now > End)
                    return false;

                for (int i = slides.Count - 1; i >= 0; i--)
                {
                    if (slides[i].Start <= now)
                    {
                        position = placeIn(slides[i], now);
                        break;
                    }
                }

                return true;
            }

            /// <summary>The beams of round <paramref name="k"/> go: every MEIRA_SLICE_CIRCLES circle radii along each a ring of small basic bullets flies out.</summary>
            private void dissolve(BulletField f, int k)
            {
                double at = goneAt(k);
                float gap = Math.Max(f.circleRadius * DanmakusuBalance.MEIRA_SLICE_CIRCLES, 4f);
                float plain = stageBulletSpeed(Period);
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.MEIRA_SLICE_SIZE;
                int bullets = DanmakusuBalance.MEIRA_SLICE_BULLETS;
                int slice = 0;

                foreach (var slide in slides)
                {
                    if (roundOf(slide.Start) != k || slide.Steps.Count < 2)
                        continue;

                    float carried = 0f;

                    for (int i = 0; i + 1 < slide.Steps.Count; i++)
                    {
                        Vector2 a = slide.Steps[i], b = slide.Steps[i + 1];
                        float length = Vector2.Distance(a, b);

                        for (float along = gap - carried; along <= length; along += gap)
                        {
                            Vector2 p = Vector2.Lerp(a, b, along / Math.Max(length, 1e-4f));
                            float phase = slice * 0.7f; // (each ring turned a little from the one before)

                            for (int n = 0; n < bullets; n++)
                            {
                                float angle = phase + n * MathF.Tau / bullets;
                                Vector2 v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * plain;

                                AddBullet(new StageBullet
                                {
                                    T0 = at,
                                    P0 = p,
                                    V = v,
                                    Radius = radius,
                                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                    Style = f.comboStyle(slice + n, true, Period.Rank),
                                    Shape = BulletShape.Circle,
                                    Until = at + f.stageLife(v),
                                });
                            }

                            slice++;
                        }

                        carried = (carried + length) % gap;
                    }
                }
            }

            /// <summary>The beams of the round that is on at <paramref name="t"/>, each slide's from its start to where she is (or stopped).</summary>
            private IEnumerable<Slide> showing(double t)
            {
                int k = roundOf(t);

                foreach (var slide in slides)
                {
                    if (slide.Start <= t && roundOf(slide.Start) == k && t < goneAt(k))
                        yield return slide;
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || slides.Count == 0)
                    return;

                // (her colours: a pale violet beam with a deep purple edge)
                var core = new Color4(0.96f, 0.88f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.55f, 0.25f, 0.8f, 1f));
                float half = halfWidth(f);

                foreach (var slide in showing(t))
                {
                    line.Clear();
                    double u = (t - slide.Start) / DanmakusuBalance.MEIRA_STEP_MS;
                    int last = Math.Min((int)Math.Floor(u), slide.Steps.Count - 1);

                    for (int i = 0; i <= last; i++)
                        line.Add(slide.Steps[i]);

                    Vector2 now = placeIn(slide, t);

                    if (line.Count == 0 || Vector2.DistanceSquared(line[^1], now) > 0.25f)
                        line.Add(now);

                    if (line.Count >= 2)
                        f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal);
                }
            }

            /// <summary>Her beams hurt like a slider body until they go.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || slides.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var slide in showing(now))
                {
                    double u = (now - slide.Start) / DanmakusuBalance.MEIRA_STEP_MS;
                    int last = Math.Min((int)Math.Floor(u), slide.Steps.Count - 1);
                    Vector2 previous = slide.Steps[0];

                    for (int i = 1; i <= last; i++)
                    {
                        if (distanceToSegment(player, previous, slide.Steps[i]) <= reach)
                            return true;

                        previous = slide.Steps[i];
                    }

                    if (distanceToSegment(player, previous, placeIn(slide, now)) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
