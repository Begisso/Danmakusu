using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Seiga Kaku: every second bullet that goes out through a wall of the playfield while she attacks (the bullets of the hit objects and the other boss too) comes back. It turns round where it went out, flies the way it came at a
        // quarter of its speed as a paper talisman (whatever it was), and fades out once sixteen beats have passed. Under it, the bullet that went out comes back too, tinted brown and harmless (2026-11-22).
        // Like the bullets of any act it is a function of time: a straight line from the wall.
        // Yoshika Miyako (2026-11-13) does the same with every fourth bullet, which comes back at its own speed as a talisman YOSHIKA_TALISMAN_SIZE times as big, straight back the way it came since 2026-11-25
        // ("remove spinning"), its train behind it. From 2026-11-22 until then it came back along a curve
        // as Aunn's beams do: bending ever more over its first YOSHIKA_CURL_CIRCLES circle radii, once round a loop YOSHIKA_LOOP_BIG wide, once round one YOSHIKA_LOOP_SMALL wide, then straight on out of the
        // screen (each one curling the other way from the last; legs every few px, so all of it is worked out when the bullet turns and known before it comes), and a train of its copies as long as it goes in YOSHIKA_TRAIN_BEATS
        // follows it end to end along the same legs, as the body of Aunn's beam follows its head. (Until then its copies stood where it left them for eight beats - since the curve, rings
        // of talismans standing all over the screen: the user's "why circles stopped".)
        // Since 2026-11-26 ("talismans out of field live for 4 beats. Also return her echoing talismans mechanics, retain size") a returning talisman lives YOSHIKA_RETURN_LIFE_BEATS, and instead of the
        // train it leaves its echoes again: copies as big as it is, standing where it left them, end to end from the wall, for YOSHIKA_ECHO_BEATS each (leaveEchoes).
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

                // (Yoshika's: twice as big, along a curve as Aunn's beams - bending ever more, round a big loop and a smaller one, then straight on - leaving its echoes all along it: 2026-11-22)
                if (echoes)
                    return returnAlongCurve(f, b, emitter, bullet, wall, velocity, crossed);

                // (Seiga's: the bullet's own body, brown, under the talisman, only to look at - it flies with it: 2026-11-22)
                var body = b;
                body.Tint = BulletRenderer.TINT_BROWN;
                body.TintAmount = 1f;
                body.Harmless = true;

                // it comes back as a talisman (no longer a darker bullet with a talisman on it)
                b.Shape = BulletShape.Talisman;
                b.Blades = 0;

                // (Seiga's lives sixteen beats; 2026-11-14)
                double fades = crossed + beat * DanmakusuBalance.KAKU_RETURN_BEATS;
                b.AlphaFrom = body.AlphaFrom = fades;
                b.AlphaOver = body.AlphaOver = DanmakusuBalance.KAKU_RETURN_FADE_MS;
                b.Until = body.Until = Math.Min(fades + DanmakusuBalance.KAKU_RETURN_FADE_MS, crossed + f.stageLife(velocity));

                // (the body first: it is drawn under the talisman)
                AddBullet(body);
                int index = Bullets.Count;
                AddBullet(b);

                return Bullets.Count > index;
            }

            /// <summary>
            /// Yoshika's returning talisman (2026-11-22): twice as big, from the wall along a curve as Aunn's beams run - bending ever more over YOSHIKA_CURL_CIRCLES circle radii, then once round a loop
            /// YOSHIKA_LOOP_BIG circle radii wide and once round one YOSHIKA_LOOP_SMALL wide, then straight on out of the screen - at its own speed, as legs a few px long; a train of its copies follows it
            /// (followTrain). Every second one curls the other way.
            /// </summary>
            private bool returnAlongCurve(BulletField f, StageBullet b, BulletEmitter emitter, int bullet, Vector2 wall, Vector2 velocity, double crossed)
            {
                b.Shape = BulletShape.Talisman;
                b.Blades = 0;
                b.Radius *= DanmakusuBalance.YOSHIKA_TALISMAN_SIZE;
                b.Hit *= DanmakusuBalance.YOSHIKA_TALISMAN_SIZE;
                b.Untraced = true; // (its curve is its own: no other boss's op turns it)

                float speed = velocity.Length;

                if (speed < 1e-4f)
                    return false;

                // (straight back the way it came: 2026-11-25, "remove spinning" - from 2026-11-22 it went round a big loop and a small one first, as Aunn's beams do. It lives YOSHIKA_RETURN_LIFE_BEATS
                // since 2026-11-26, "talismans out of field live for 4 beats": until then until it had left the screen)
                double until = Math.Min(crossed + f.stageLife(velocity), crossed + beat * DanmakusuBalance.YOSHIKA_RETURN_LIFE_BEATS);
                b.Legs = new[] { new StageLeg { T = crossed, P = wall, V = velocity } };
                b.AlphaFrom = until - DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                b.AlphaOver = DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                b.Until = until;

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                leaveEchoes(f, b, wall, velocity, crossed, until);
                return true;
            }

            /// <summary>
            /// Yoshika's echoes (back since 2026-11-26, "return her echoing talismans mechanics, retain size"; 2026-11-13 to 2026-11-22, then a train that followed it): as it goes it leaves copies of itself
            /// standing behind it, one every talisman's length (end to end, no gap), as big as it is, facing its way, each for YOSHIKA_ECHO_BEATS from when it was left (fading over YOSHIKA_ECHO_FADE_MS), from the
            /// wall until it is gone or leaves the screen. All of them are known as it turns (it flies straight), so the autoplay sees them coming.
            /// </summary>
            private void leaveEchoes(BulletField f, StageBullet talisman, Vector2 wall, Vector2 velocity, double crossed, double until)
            {
                float speed = velocity.Length;
                float length = talisman.Radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * 2f; // (a talisman from end to end, along its way)

                if (length < 0.5f || speed < 1e-4f)
                    return;

                Vector2 way = velocity / speed;
                double lives = beat * DanmakusuBalance.YOSHIKA_ECHO_BEATS;

                for (int k = 1; k <= 64; k++)
                {
                    double left = crossed + k * length / speed;

                    if (left >= until)
                        break;

                    Vector2 at = wall + way * (k * length);

                    if (outside(f, at))
                        break;

                    var echo = talisman;
                    echo.Legs = null;
                    echo.LegsDelay = 0;
                    echo.T0 = left;
                    echo.P0 = at;
                    echo.V = Vector2.Zero;
                    echo.Face = way;
                    echo.KeepFace = true;
                    echo.Until = left + lives;
                    echo.AlphaFrom = echo.Until - DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                    echo.AlphaOver = DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;

                    int count = Bullets.Count;
                    AddBullet(echo);

                    if (Bullets.Count == count)
                        break; // (the act is full)
                }
            }

            // (the train that followed it, 2026-11-22 to 2026-11-26 - copies going along the same legs a talisman's length after each other, StageBullet.LegsDelay - gave way to the echoes again)
        }
    }
}
