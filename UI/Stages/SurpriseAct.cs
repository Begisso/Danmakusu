using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kogasa Tatara (2026-11-23): the circles and sliders of her section turn round the middle of the playfield, every second the other way round (SurpriseOrbit: the playfield moves them, the zones
        // hurt where they are), and stay KOGASA_HOLD_BEATS after they are over, still turning - red the ones that turn clockwise, blue the others - hurting as a slider's body does (the zones) and shooting what
        // they would have shot out from the middle over that time (the planner). This act says which of them stay where, for the FallLayer that shows them as what they are. Her six spirals are the planner's.
        // Her umbrella turns in the middle all the while (2026-11-24), and an arrow beside each of hers shows which way it turns (2026-11-24, drawArrows).
        // ======================================================================================================================================================

        private sealed class SurpriseAct : StageAct
        {
            private readonly (OsuHitObject Source, Vector2 Head, SurpriseOrbit.Turn Turn)[] held;

            public SurpriseAct(BossPeriod period, SliderZone[] sliderZones)
            {
                Period = period;
                Start = period.ShootStart;

                held = sliderZones.Where(zone => zone.Orbit.HasValue && zone.Source != null && zone.Source.StartTime >= period.ShootStart && zone.Source.StartTime < period.End)
                                  .Select(zone => (zone.Source!, zone.Head, zone.Orbit!.Value))
                                  .ToArray();

                End = held.Length > 0 ? held.Max(h => h.Turn.Until) : period.End;
            }

            /// <summary>She stays until the last of hers is gone.</summary>
            public override double MechanicsEnd => held.Length > 0 ? End : double.NegativeInfinity;

            /// <summary>
            /// Her umbrella (2026-11-24, the user's photo): in the middle of the playfield, KOGASA_UMBRELLA_SIZE px across, under everything, turning as her circles and sliders do (clockwise, KOGASA_ORBIT_DEGREES
            /// a beat) - from when her section begins to when the last of hers is gone, fading in and out over half a beat. It only shows.
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                double beat = Math.Max(Period.BeatLength, 1);
                double fade = beat * 0.5;
                float alpha = (float)Math.Min(Math.Clamp((t - (Start - fade)) / fade, 0, 1), Math.Clamp((End - t) / fade, 0, 1));

                if (alpha <= 0.002f)
                    return;

                float degrees = (float)((t - Start) / beat * DanmakusuBalance.KOGASA_ORBIT_DEGREES);
                f.stageBack.Add(StageTexture.KogasaUmbrella, osu.Game.Rulesets.Osu.UI.OsuPlayfield.BASE_SIZE / 2f, new Vector2(DanmakusuBalance.KOGASA_UMBRELLA_SIZE), degrees,
                    alpha * DanmakusuBalance.KOGASA_UMBRELLA_ALPHA, Color4.White);

                drawArrows(f, t, beat);
            }

            /// <summary>
            /// The arrows (2026-11-24, "arrow warnings for Kogasa spin"): beside every circle and slider of hers, from when it shows until it is gone, an arrow just ahead of its head the way it goes round the
            /// middle, red or blue as it turns (as it is drawn once it is over). Only to look at.
            /// </summary>
            private void drawArrows(BulletField f, double t, double beat)
            {
                Vector2 middle = osu.Game.Rulesets.Osu.UI.OsuPlayfield.BASE_SIZE / 2f;

                foreach (var (source, head, turn) in held)
                {
                    double shows = source.StartTime - source.TimePreempt;

                    if (t < shows || t >= turn.Until)
                        continue;

                    float alpha = (float)Math.Min(Math.Clamp((t - shows) / Math.Max(source.TimeFadeIn, 1), 0, 1), Math.Clamp((turn.Until - t) / (beat * 0.5), 0, 1)) * DanmakusuBalance.KOGASA_ARROW_ALPHA;

                    if (alpha <= 0.01f)
                        continue;

                    Vector2 at = head + turn.Offset(head, t);
                    Vector2 outward = at - middle;
                    float distance = outward.Length;

                    if (distance < 1f)
                        continue;

                    // (the way round: clockwise on the screen turns the way out a quarter turn to the right)
                    Vector2 round = new Vector2(-outward.Y, outward.X) / distance * turn.Way;
                    float radius = (float)source.Radius;
                    float size = radius * DanmakusuBalance.KOGASA_ARROW_SIZE;

                    f.stageRipple.Add(StageTexture.SeijaArrow, at + round * (radius + size * 0.55f), new Vector2(size), MathF.Atan2(round.Y, round.X) * 180f / MathF.PI, alpha,
                        turn.Way > 0 ? DanmakusuBalance.KOGASA_RED : DanmakusuBalance.KOGASA_BLUE);
                }
            }

            /// <summary>What stays at <paramref name="t"/> (over, but not yet gone): where it has turned to, and its colour.</summary>
            public void Collect(double t, List<(OsuHitObject Source, double Began, Vector2 By, Color4? Colour)> into)
            {
                foreach (var (source, head, turn) in held)
                {
                    double over = source.GetEndTime();

                    if (t < over || t >= turn.Until)
                        continue;

                    into.Add((source, over, turn.Offset(head, t), turn.Way > 0 ? DanmakusuBalance.KOGASA_RED : DanmakusuBalance.KOGASA_BLUE));
                }
            }
        }
    }
}
