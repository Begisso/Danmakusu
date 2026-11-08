using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Seiga Kaku: every second bullet that goes out through a wall of the playfield while she attacks (the bullets of the hit objects and the other boss too) comes back. It turns round where it went out, flies the way it came at a
        // quarter of its speed as a paper talisman (whatever it was), and fades out once sixteen beats have passed.
        // Like the bullets of any act it is a function of time: a straight line from the wall.
        // ======================================================================================================================================================

        private sealed class NecromancyAct : StageAct
        {
            private readonly double beat;

            public NecromancyAct(BulletField field, BossPeriod period, double start, double end)
            {
                _ = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
            }

            private static bool outside(BulletField f, Vector2 p) => p.X < f.stageMin.X || p.X > f.stageMax.X || p.Y < f.stageMin.Y || p.Y > f.stageMax.Y;

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                if (!outside(f, to) || outside(f, from))
                    return false;

                if ((Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.KAKU_RETURN_EVERY != 0)
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                Vector2 direction = Vector2.Normalize(delta);

                // where (and when) it went through the wall: the first wall it got past
                Vector2 low = f.stageMin, high = f.stageMax;
                float s = 1f;

                if (to.X < low.X && Math.Abs(delta.X) > 1e-6f)
                    s = Math.Min(s, (low.X - from.X) / delta.X);

                if (to.X > high.X && Math.Abs(delta.X) > 1e-6f)
                    s = Math.Min(s, (high.X - from.X) / delta.X);

                if (to.Y < low.Y && Math.Abs(delta.Y) > 1e-6f)
                    s = Math.Min(s, (low.Y - from.Y) / delta.Y);

                if (to.Y > high.Y && Math.Abs(delta.Y) > 1e-6f)
                    s = Math.Min(s, (high.Y - from.Y) / delta.Y);

                s = Math.Clamp(s, 0f, 1f);
                Vector2 wall = from + delta * s;
                double crossed = t - (1f - s) * Math.Clamp(t - ScanPrevious, 0, 50);

                Vector2 velocity = -direction * (speed / DanmakusuBalance.KAKU_RETURN_SPEED_DIVISOR);
                var b = f.makeStageBullet(stored, emitter, bullet, crossed, wall, velocity);

                // it comes back as a talisman (no longer a darker bullet with a talisman on it)
                b.Shape = BulletShape.Talisman;
                b.Blades = 0;

                double fades = crossed + beat * DanmakusuBalance.KAKU_RETURN_BEATS;
                b.AlphaFrom = fades;
                b.AlphaOver = DanmakusuBalance.KAKU_RETURN_FADE_MS;
                b.Until = Math.Min(fades + DanmakusuBalance.KAKU_RETURN_FADE_MS, crossed + f.stageLife(velocity));

                int index = Bullets.Count;
                AddBullet(b);

                return Bullets.Count > index;
            }
        }
    }
}
