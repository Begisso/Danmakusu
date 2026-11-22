using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Ariya Iwanaga (2026-11-22): her section goes in stretches of ARIYA_STONE_BEATS, one after the other (BossOps.StoneStretches). What is flying when a stretch begins stops, grey, until it ends (BossOps:
        // a freeze; what came before the stretch before stopped then, and flies on - nothing stops twice). Every circle and slider that is over in a stretch (and Doremy's and Sagume's copies of them) does not go:
        // it stays where it is, grey and still, shown as what it is by the FallLayer (as Urumi's falling ones, which go first when both would show one), harmless; what it would shoot in the stretch is held (the
        // planner) and shot, all of it at once, as the stretch ends - when the held ones go. Over the last ARIYA_WARN_BEATS of every stretch a big white flash swells over the screen, the held ones glowing white,
        // and it dies away after; Byakuren's beam sound comes with it.
        // She shoots a row of ellipses down from the top of the screen every ARIYA_ROW_EVERY_BEATS, across the whole screen ARIYA_COLUMN_GAP bullet radii apart, every second row half a gap aside, so that they
        // make a grid; each bounces once off the bottom and flies back up out of the screen. Her own ellipses never stop (2026-11-22; they stopped in a stretch as everything else does).
        // ======================================================================================================================================================

        private sealed class FossilAct : StageAct
        {
            private sealed class Held
            {
                public OsuHitObject? Source;
                public double Time; // (when it is over)
                public double Until; // (when its stretch ends)
                public Vector2 Place;
                public float Radius;
                public SliderZone? Slider;
            }

            private readonly double beat;
            private readonly List<(double Start, double End)> stretches;
            private readonly List<Held> held = new List<Held>();
            private readonly List<double> rows = new List<double>();
            private int nextRow;
            private int soundedRelease = -1;
            private double resetTime = double.NegativeInfinity;
            private readonly List<Vector2> beamPoints = new List<Vector2>();

            public FossilAct(BossPeriod period, CircleZone[] circles, SliderZone[] sliders)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                stretches = BossOps.StoneStretches(period);

                foreach (var circle in circles)
                {
                    int s = stretchOf(circle.Time);

                    if (s >= 0)
                        held.Add(new Held { Source = circle.Source, Time = circle.Time, Until = stretches[s].End, Place = circle.Position, Radius = circle.Radius });
                }

                foreach (var slider in sliders)
                {
                    // (not what falls, a catch map's)
                    if (slider.FallSpeed > 0f || slider.Points.Length < 2)
                        continue;

                    int s = stretchOf(slider.EndTime);

                    if (s >= 0)
                        held.Add(new Held { Source = slider.Source, Time = slider.EndTime, Until = stretches[s].End, Place = slider.Points[slider.Points.Length / 2], Radius = slider.Radius, Slider = slider });
                }

                held.Sort((a, b) => a.Time.CompareTo(b.Time));

                for (double t = period.ShootStart; t < period.End - 1 && rows.Count < 10000; t += beat * DanmakusuBalance.ARIYA_ROW_EVERY_BEATS)
                    rows.Add(t);

                Start = period.ShootStart;
                End = Math.Max(period.End, stretches.Count > 0 ? stretches[^1].End : period.End) + beat;
            }

            /// <summary>She stays until her last stretch is over.</summary>
            public override double MechanicsEnd => stretches.Count > 0 ? stretches[^1].End + beat * 0.5 : double.NegativeInfinity;

            private int stretchOf(double t)
            {
                for (int s = 0; s < stretches.Count; s++)
                {
                    if (t >= stretches[s].Start && t < stretches[s].End)
                        return s;
                }

                return -1;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                nextRow = 0;
                resetTime = time;
                soundedRelease = -1;

                for (int s = 0; s < stretches.Count; s++)
                {
                    if (stretches[s].End <= time)
                        soundedRelease = s;
                }

                // (her rows before that moment are shot again the same way: where they are follows from the time)
                for (; nextRow < rows.Count && rows[nextRow] <= time; nextRow++)
                    row(f, nextRow, false);
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (; nextRow < rows.Count && rows[nextRow] <= now; nextRow++)
                    row(f, nextRow, true);

                // (the held ones are let go with Byakuren's beam sound)
                for (; soundedRelease + 1 < stretches.Count && stretches[soundedRelease + 1].End <= now; soundedRelease++)
                {
                    double end = stretches[soundedRelease + 1].End;

                    if (now - end < 250 && end > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            // ---- her grid of ellipses ----

            /// <summary>Row <paramref name="k"/>: ellipses across the whole screen, coming down from just above its top, every second row half a gap aside; each bounces once off the bottom, and stops once in a stretch.</summary>
            private void row(BulletField f, int k, bool live)
            {
                double t0 = rows[k];
                float r = f.stageBaseRadius(Period) * DanmakusuBalance.ARIYA_ELLIPSE_SIZE;
                float gap = Math.Max(r * DanmakusuBalance.ARIYA_COLUMN_GAP, 4f);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.ARIYA_SPEED;
                float top = f.stageMin.Y - r, bottom = f.stageMax.Y;
                int style = f.comboStyle(k, true, Period.Rank);
                float hit = r * DanmakusuBalance.BULLET_HITBOX_SCALE;

                // (her own ellipses do not stop in her stretches: 2026-11-22, "Her grid bullets are not affected by freeze" - they stopped once until then)
                double bounceAt = t0 + (bottom - top) / speed;
                double outAt = bounceAt + (bottom - top + r) / speed;

                for (float x = f.stageMin.X + gap * (k % 2 == 0 ? 0.5f : 1f); x < f.stageMax.X; x += gap)
                {
                    AddBullet(new StageBullet
                    {
                        T0 = t0,
                        P0 = new Vector2(x, top),
                        V = new Vector2(0f, speed),
                        Legs = new[]
                        {
                            new StageLeg { T = t0, P = new Vector2(x, top), V = new Vector2(0f, speed) },
                            new StageLeg { T = bounceAt, P = new Vector2(x, bottom), V = new Vector2(0f, -speed) },
                        },
                        Radius = r,
                        Hit = hit,
                        Style = style,
                        Shape = BulletShape.Ellipse,
                        Until = outAt,
                    });

                    // (every bounce flashes)
                    if (live)
                        f.addFlash(bounceAt, new Vector2(x, bottom), r * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                }
            }

            // ---- the held circles and sliders ----

            /// <summary>The first held one that can still be there at <paramref name="t"/> (they are in the order they were over; a stretch is the longest any is held).</summary>
            private int firstHeld(double t)
            {
                double longest = beat * DanmakusuBalance.ARIYA_STONE_BEATS;
                int low = 0, high = held.Count;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (held[middle].Time + longest < t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            /// <summary>What is held at <paramref name="t"/>, for the <see cref="FallLayer"/>: grey and still where it was (not what is already shown, falling in Urumi's section).</summary>
            public void Collect(double t, List<(OsuHitObject Source, double Began, Vector2 By)> into)
            {
                int shownBefore = into.Count;

                for (int i = firstHeld(t); i < held.Count && held[i].Time <= t; i++)
                {
                    var h = held[i];

                    if (h.Source == null || t >= h.Until)
                        continue;

                    bool shown = false;

                    for (int j = 0; j < shownBefore && !shown; j++)
                        shown = ReferenceEquals(into[j].Source, h.Source);

                    if (!shown)
                        into.Add((h.Source, t, Vector2.Zero)); // (began now: it does not darken)
                }
            }

            /// <summary>How white the held ones and the screen flash at <paramref name="t"/> (0 .. 1): it swells over the last beat of a stretch and dies away after it.</summary>
            private float flash(double t)
            {
                double warn = beat * DanmakusuBalance.ARIYA_WARN_BEATS;
                float most = 0f;

                foreach (var (_, end) in stretches)
                {
                    if (t < end - warn || t > end + beat * 0.5)
                        continue;

                    most = Math.Max(most, t < end ? smoothStep((t - (end - warn)) / warn) : 1f - smoothStep((t - end) / (beat * 0.5)));
                }

                return most;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                float white = flash(t);
                var grey = BulletRenderer.SpecialBodies[BulletRenderer.TINT_GRAY - 1];

                for (int i = firstHeld(t); i < held.Count && held[i].Time <= t; i++)
                {
                    var h = held[i];

                    if (t >= h.Until)
                        continue;

                    // (they glow white as the flash swells)
                    if (white > 0.01f)
                    {
                        if (h.Slider != null)
                        {
                            for (int k = 0; k < h.Slider.Points.Length; k += 3)
                                f.stageOrbs.Add(StageTexture.Glow, h.Slider.Points[k], new Vector2(h.Radius * 3f), 0f, white * 0.5f, Color4.White, true);
                        }
                        else
                            f.stageOrbs.Add(StageTexture.Glow, h.Place, new Vector2(h.Radius * 3f), 0f, white, Color4.White, true);
                    }

                    // (with no layer, as in the boss menu's preview: a plain grey circle; a slider is drawn with the beams)
                    if (!f.FallingObjectsShown && h.Slider == null)
                    {
                        float size = h.Radius * 2f;
                        f.stageOrbs.Add(StageTexture.Orb, h.Place, new Vector2(size), 0f, 1f, DanmakusuBalance.BorderColour(grey));
                        f.stageOrbs.Add(StageTexture.Orb, h.Place, new Vector2(size * MOKOU_BODY_SCALE), 0f, 1f, grey);
                    }
                }

                // (the big flash over the whole screen)
                if (white > 0.01f)
                {
                    Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                    float across = Vector2.Distance(f.stageMin, f.stageMax) * 2.2f;
                    f.stageTop.Add(StageTexture.Glow, middle, new Vector2(across), 0f, white * DanmakusuBalance.ARIYA_FLASH_ALPHA, Color4.White, true);
                }
            }

            /// <summary>With no <see cref="FallLayer"/>: the held sliders, drawn grey like the other beams.</summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.FallingObjectsShown)
                    return;

                var grey = BulletRenderer.SpecialBodies[BulletRenderer.TINT_GRAY - 1];

                for (int i = firstHeld(t); i < held.Count && held[i].Time <= t; i++)
                {
                    var h = held[i];

                    if (h.Slider == null || t >= h.Until)
                        continue;

                    beamPoints.Clear();
                    beamPoints.AddRange(h.Slider.Points);
                    f.addPlainBeam(beamPoints, h.Radius, grey, DanmakusuBalance.BorderColour(grey), ref beams, ref pointTotal);
                }
            }
        }
    }
}
