using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kutaka Niwatari (2026-11-27): everything fired in her section flies at KUTAKA_SLOW of its speed (the planner). What the circles and sliders fire is taken as it is fired and shot again from
        // where it was fired at where the player is then, in two streams side by side (KUTAKA_STREAM_APART_CIRCLES circle radii apart): the ring's bullets two at a time, a pair every
        // KUTAKA_STREAM_GAP_BEATS, at the speed they had. What she and the other bosses shoot is left alone.
        // ======================================================================================================================================================

        private sealed class StreamsAct : StageAct
        {
            private readonly double beat;
            private readonly Dictionary<BulletEmitter, Vector2> aims = new Dictionary<BulletEmitter, Vector2>();

            public StreamsAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * 32;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                aims.Clear();
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Period.ShootStart || t > Period.End || emitter.OwnShot || emitter.FollowsBoss || emitter.Derived || emitter.Sharp || emitter.Homing
                    || emitter.Shape == BulletShape.Train)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                Vector2 origin = emitter.Origins != null ? emitter.Origins[bullet] : emitter.Origin;

                // (the way to the player as the ring is fired, the same for all of it)
                if (!aims.TryGetValue(emitter, out Vector2 way))
                {
                    Vector2 toPlayer = f.characterPosition - origin;
                    way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                    aims[emitter] = way;

                    if (aims.Count > 4096)
                        aims.Clear();
                }

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 across = new Vector2(-way.Y, way.X);
                float side = bullet % 2 == 0 ? -0.5f : 0.5f;
                double at = emitter.Time + bullet / 2 * beat * DanmakusuBalance.KUTAKA_STREAM_GAP_BEATS;
                Vector2 velocity = way * Math.Max(stored.Speed, 0.02f);

                AddBullet(new StageBullet
                {
                    T0 = at,
                    P0 = origin + across * (side * f.circleRadius * DanmakusuBalance.KUTAKA_STREAM_APART_CIRCLES),
                    V = velocity,
                    Face = way,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Untraced = true,
                    Until = at + f.stageLife(velocity),
                });

                return true;
            }
        }
    }
}
