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
