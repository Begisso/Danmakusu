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
        // Yoshika Miyako (2026-11-13) does the same with every fourth bullet, which comes back at its own speed and leaves static copies of itself behind it all the way, end to end with no gap between them, each
        // there for eight beats from when it is left; all of them are worked out when the bullet turns (it flies straight), so they are known before they come. The returning talisman itself (the head) lives two
        // beats (YOSHIKA_HEAD_BEATS, 2026-11-14; it was sixteen as Seiga's, then eight), so it leaves its echoes only that far.
        // ======================================================================================================================================================

        private sealed class NecromancyAct : StageAct
        {
            private readonly double beat;
            private readonly bool echoes; // (Yoshika's)
            private readonly int every;
            private readonly float slower;

            public NecromancyAct(BulletField field, BossPeriod period, double start, double end, bool echoes = false)
            {
                _ = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                AbsorbFrom = start;
                AbsorbTo = end;
                this.echoes = echoes;
                every = echoes ? DanmakusuBalance.YOSHIKA_RETURN_EVERY : DanmakusuBalance.KAKU_RETURN_EVERY;
                slower = echoes ? DanmakusuBalance.YOSHIKA_RETURN_SPEED_DIVISOR : DanmakusuBalance.KAKU_RETURN_SPEED_DIVISOR;
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

                if ((Math.Abs((long)emitter.Time) + bullet) % every != 0)
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

                Vector2 velocity = -direction * (speed / slower);
                var b = f.makeStageBullet(stored, emitter, bullet, crossed, wall, velocity);

                // it comes back as a talisman (no longer a darker bullet with a talisman on it)
                b.Shape = BulletShape.Talisman;
                b.Blades = 0;

                // (Yoshika's lives two beats - it leaves its echoes only that far; Seiga's sixteen; 2026-11-14)
                double fades = crossed + beat * (echoes ? DanmakusuBalance.YOSHIKA_HEAD_BEATS : DanmakusuBalance.KAKU_RETURN_BEATS);
                b.AlphaFrom = fades;
                b.AlphaOver = DanmakusuBalance.KAKU_RETURN_FADE_MS;
                b.Until = Math.Min(fades + DanmakusuBalance.KAKU_RETURN_FADE_MS, crossed + f.stageLife(velocity));

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                if (echoes)
                    leaveEchoes(f, b, wall, velocity, crossed);

                return true;
            }

            /// <summary>
            /// Yoshika's: the copies a returning talisman leaves behind it, one every talisman's length of its way (they touch, end to end), from the wall until it fades or leaves the screen, each standing where it was
            /// left for eight beats and fading out at the end.
            /// </summary>
            private void leaveEchoes(BulletField f, StageBullet talisman, Vector2 wall, Vector2 velocity, double crossed)
            {
                float speed = velocity.Length;
                float length = talisman.Radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * 2f; // (a talisman from end to end, along its way)

                if (speed < 1e-4f || length < 0.5f)
                    return;

                double gap = length / speed;
                double life = beat * DanmakusuBalance.YOSHIKA_ECHO_BEATS;
                Vector2 way = velocity / speed;
                Vector2 low = (f.hasScreenBounds ? f.screenMin : f.stageMin) - new Vector2(length), high = (f.hasScreenBounds ? f.screenMax : f.stageMax) + new Vector2(length);

                for (double at = crossed + gap; at < talisman.Until; at += gap)
                {
                    Vector2 position = wall + velocity * (float)(at - crossed);

                    if (position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y)
                        break;

                    var echo = talisman;
                    echo.T0 = at;
                    echo.P0 = position;
                    echo.V = Vector2.Zero;
                    echo.KeepFace = true; // (it faces the way the talisman flew)
                    echo.Still = true;
                    echo.Untraced = true; // (it stays where it was left: the ops of the other bosses leave it be - tracing thousands of them through those every frame cost 4 ms and more, 2026-11-14)
                    echo.Face = way;
                    echo.AlphaFrom = at + life - DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                    echo.AlphaOver = DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                    echo.Until = at + life;

                    int count = Bullets.Count;
                    AddBullet(echo);

                    if (Bullets.Count == count)
                        break; // (the act is full)
                }
            }
        }
    }
}
