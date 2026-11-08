using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Wriggle Nightbug: all through her section every fourth bullet on the screen is a firefly: twice as big, bright green, with bloom and a soft glow of the same green that pulses. It leaves its way: it drifts on the way it was
        // going at less than half its speed, wandering from side to side and back as a firefly does, restlessly and not far (two waves of its own, twice as fast as a petal's sway, three quarters of a circle radius; a closed form of time, so a replay and a seek give the same).
        // ======================================================================================================================================================

        private sealed class SwarmAct : StageAct
        {
            private static readonly Color4 glow = BulletRenderer.SpecialBodies[BulletRenderer.TINT_GREEN - 1];

            public SwarmAct(BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            public override void Reset(BulletField f, double time) => ClearBullets();

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                // (by the ring's time as well as its number, as rings can be smaller than sixteen: which one it is stays the same in a replay and a seek)
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train || emitter.Deflector || (Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.WRIGGLE_EVERY != 0)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                Vector2 facing = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitY;
                Vector2 velocity = facing * (stored.Speed * DanmakusuBalance.WRIGGLE_SPEED);
                float size = emitter.BigScale(bullet); // (it swells to twice this by itself, see ScaleTo: bigger than that the picture of a bullet is no longer sharp)
                double until = t + Math.Min(120000, f.stageLife(velocity));
                int before = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Face = facing,
                    Radius = stored.Radius * size,
                    Hit = stored.Hit * size,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_GREEN,
                    TintAmount = 1f,
                    FadeIn = DanmakusuBalance.WRIGGLE_TURN_MS,
                    ScaleFrom = t,
                    ScaleOver = DanmakusuBalance.WRIGGLE_TURN_MS,
                    ScaleTo = DanmakusuBalance.WRIGGLE_SIZE,
                    BloomAt = t,
                    BloomTo = DanmakusuBalance.WRIGGLE_BLOOM,
                    Shade = stored.Shade,
                    Until = until,
                    AlphaFrom = until - DanmakusuBalance.WRIGGLE_TURN_MS,
                    AlphaOver = DanmakusuBalance.WRIGGLE_TURN_MS,
                    Wander = f.circleRadius * DanmakusuBalance.WRIGGLE_WANDER_CIRCLES,
                    WanderPace = DanmakusuBalance.WRIGGLE_WANDER_PACE,
                    WanderPhase = hash01(bullet, (int)emitter.Time, 3) * MathF.Tau,
                });

                return Bullets.Count > before;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                for (int i = DrawFrom; i < Bullets.Count; i++)
                {
                    var bug = Bullets[i];

                    if (t < bug.T0 || t > bug.Until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + i)))
                        continue;

                    if (!f.stageBulletAt(bug, t, out Vector2 position))
                        continue;

                    // (it pulses, each at its own moment)
                    float pulse = 0.65f + 0.35f * MathF.Sin((float)(t * DanmakusuBalance.WRIGGLE_PULSE) + bug.WanderPhase);
                    float alpha = pulse * smoothStep((t - bug.T0) / DanmakusuBalance.WRIGGLE_TURN_MS) * smoothStep((bug.Until - t) / DanmakusuBalance.WRIGGLE_TURN_MS);

                    // (in the green the firefly itself is drawn in)
                    f.stageBack.Add(StageTexture.Glow, position, new Vector2(bug.Radius * DanmakusuBalance.WRIGGLE_SIZE * DanmakusuBalance.WRIGGLE_GLOW_SIZE), 0f, alpha * DanmakusuBalance.WRIGGLE_GLOW, glow, true);
                }
            }
        }
    }
}
