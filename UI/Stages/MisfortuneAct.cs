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
        // Shion Yorigami: every eight beats everything there is, bullets and beams, turns dark blue and flies back the way it came (an op, see BossOps). For the four beats before that a small red arrow flashes beside every second bullet, pointing back the way it came:
        // the way it will go (reverseLevel). Her own talismans fly in hexagons; one that goes out of the screen comes back in the way it came, once: that is here.
        // ======================================================================================================================================================

        private sealed class MisfortuneAct : StageAct
        {
            public MisfortuneAct(BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + 60000;
            }

            public override void Reset(BulletField f, double time) => ClearBullets();

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || !emitter.FollowsBoss || emitter.BossRank != Period.Rank || emitter.Shape != BulletShape.Talisman || emitter.Time < Start || emitter.Time >= End)
                    return false;

                Vector2 low = f.stageMin, high = f.stageMax;
                bool wasIn = from.X >= low.X && from.X <= high.X && from.Y >= low.Y && from.Y <= high.Y;
                bool isOut = to.X < low.X || to.X > high.X || to.Y < low.Y || to.Y > high.Y;

                if (!wasIn || !isOut)
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                Vector2 velocity = delta.LengthSquared > 1e-10f ? -Vector2.Normalize(delta) * stored.Speed : Vector2.Zero;
                int before = Bullets.Count;

                // (it comes back off the border: a ricochet, it flashes there)
                f.addFlash(t, from, stored.Radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = from,
                    V = velocity,
                    Radius = stored.Radius,
                    Hit = stored.Hit,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = t + f.stageLife(velocity),
                });

                return Bullets.Count > before;
            }
        }

        private readonly List<double> reverseMarks = new List<double>();
        private double reverseWarning; // (how long before a mark the arrows show, ms)

        /// <summary>0..1: how much of Shion's warning shows at <paramref name="t"/>: the arrows come in over the first beat of the four before everything turns round.</summary>
        private float reverseLevel(double t)
        {
            float level = 0f;

            foreach (double mark in reverseMarks)
            {
                if (t >= mark - reverseWarning && t < mark)
                    level = Math.Max(level, smoothStep((t - mark + reverseWarning) / (reverseWarning * 0.25)));
            }

            return level;
        }

        /// <summary>How bright Shion's arrows are at <paramref name="t"/>: they flash, twice a beat.</summary>
        private float reverseFlash(double t)
        {
            double beat = reverseWarning / DanmakusuBalance.SHION_WARN_BEATS;
            return 0.6f + 0.4f * MathF.Cos((float)(t / (beat * DanmakusuBalance.SHION_FLASH_BEATS) * Math.Tau));
        }

        /// <summary>The turn that the warning at <paramref name="t"/> is for (NaN when there is none).</summary>
        private double warnedReverse(double t)
        {
            foreach (double mark in reverseMarks)
            {
                if (t >= mark - reverseWarning && t < mark)
                    return mark;
            }

            return double.NaN;
        }

        /// <summary>
        /// Will bullet <paramref name="bullet"/> of <paramref name="emitter"/> (now at <paramref name="ringTime"/> on its own clock) be turned round at <paramref name="mark"/> (<paramref name="now"/> is the time of the screen)? Only what flies on
        /// its own way can be (not what is pulled or bounces, nor Utsuho's big balls, nor a train, nor what was fired too late or is gone by then, nor what falls from the top): its way just after the turn is looked at, and it has turned dark blue there.
        /// </summary>
        private static bool willReverse(BulletEmitter emitter, int bullet, double ringTime, double now, double mark)
        {
            if (double.IsNaN(mark) || emitter.Homing || emitter.Ricochet || emitter.Ops == null || emitter.Sharp || emitter.Shape == BulletShape.Train)
                return false;

            emitter.Evaluate(bullet, ringTime + (mark - now) + 1, out _, out _, out bool alive, out var after);
            return alive && after is { Overlay: BulletRenderer.TINT_DARK_BLUE };
        }

        // (Shion's arrows of this frame: where each is, how big and which way it points; they are drawn with the stage, as the arrows of Seija's dress)
        private readonly List<(Vector2 At, float Size, float Degrees)> shionArrows = new List<(Vector2, float, float)>();

        /// <summary>Shion's arrows: red, not see-through, flashing brighter and darker twice a beat, coming in over the first beat of the warning.</summary>
        private void drawShionArrows(double t)
        {
            if (shionArrows.Count == 0)
                return;

            float glow = 0.5f + 0.5f * reverseFlash(t);
            var red = new Color4(glow, 0.1f * glow, 0.12f * glow, 1f);
            float level = reverseLevel(t);

            foreach (var (at, size, degrees) in shionArrows)
                stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), degrees, level, red);
        }
    }
}
