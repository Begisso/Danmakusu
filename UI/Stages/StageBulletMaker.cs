using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        /// <summary>
        /// The bullet of an act that a bullet of a ring turns into when an act takes it: where it is (<paramref name="at"/>) at <paramref name="time"/>, flying at <paramref name="velocity"/>, with everything it looked like
        /// (size, hitbox, colour, shape, blades). The caller changes what it wants (motion, tints, ...) and adds it.
        /// </summary>
        private StageBullet makeStageBullet(in StoredBullet stored, BulletEmitter emitter, int bullet, double time, Vector2 at, Vector2 velocity)
        {
            float big = emitter.BigScale(bullet);

            return new StageBullet
            {
                T0 = time,
                P0 = at,
                V = velocity,
                Radius = stored.Radius * big,
                Hit = stored.Hit * big,
                Style = stored.Style,
                Shape = stored.Shape,
                Tint = stored.Tint,
                TintAmount = stored.TintAmount,
                Shade = stored.Shade,
                Blades = stored.Blades,
                BladeSpin = stored.BladeSpin,
                BladeAngle0 = stored.Blades > 1 ? emitter.BladeAngle(bullet, time) : 0f,
                Until = time + stageLife(velocity),
            };
        }

        /// <summary>How fast the bullet of a ring is flying where it was seen between two steps (the steps are <paramref name="previous"/> and <paramref name="time"/> ms): a bullet that the ops of other bosses move is asked, not measured.</summary>
        private static float seenSpeed(in StoredBullet stored, BulletEmitter emitter, Vector2 from, Vector2 to, double previous, double time)
        {
            double dt = Math.Max(time - previous, 1e-3);
            return emitter.Ops == null ? Math.Max((to - from).Length / (float)dt, 0.02f) : stored.Speed;
        }
    }
}
