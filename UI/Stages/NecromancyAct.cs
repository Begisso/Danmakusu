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
        // Yoshika Miyako (2026-11-13) does the same with every fourth bullet, which comes back at its own speed as a talisman YOSHIKA_TALISMAN_SIZE times as big and leaves static copies of itself behind it
        // all the way, end to end with no gap between them, each there for eight beats from when it is left. Since 2026-11-22 it comes back along a curve as Aunn's beams do: bending ever more over its first
        // YOSHIKA_CURL_CIRCLES circle radii, once round a loop YOSHIKA_LOOP_BIG wide, once round one YOSHIKA_LOOP_SMALL wide, then straight on for YOSHIKA_STRAIGHT_CIRCLES (each one curling the other way from
        // the last; legs every few px, so all of it, echoes too, is worked out when the bullet turns and known before it comes). The returning talisman itself (the head) lives YOSHIKA_HEAD_BEATS (one beat;
        // it was sixteen as Seiga's, then eight, then two), so it leaves its echoes only that far.
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
            /// YOSHIKA_LOOP_BIG circle radii wide and once round one YOSHIKA_LOOP_SMALL wide, then straight on for YOSHIKA_STRAIGHT_CIRCLES - at its own speed, as legs a few px long; it leaves its echoes all the way,
            /// end to end. Every second one curls the other way.
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

                float circle = f.circleRadius;
                float curl = circle * DanmakusuBalance.YOSHIKA_CURL_CIRCLES, big = circle * DanmakusuBalance.YOSHIKA_LOOP_BIG, small = circle * DanmakusuBalance.YOSHIKA_LOOP_SMALL;
                float total = curl + MathF.Tau * (big + small) + circle * DanmakusuBalance.YOSHIKA_STRAIGHT_CIRCLES;
                float side = ((Math.Abs((long)emitter.Time) / Math.Max(every, 1) + bullet) & 1) == 0 ? 1f : -1f;
                float heading = MathF.Atan2(velocity.Y, velocity.X);

                // (how much it turns per px at s along the curve: ever more over the curl, then a loop's worth, then the other's, then none)
                float bend(float s) => s < curl ? s / curl / big : s < curl + MathF.Tau * big ? 1f / big : s < curl + MathF.Tau * (big + small) ? 1f / small : 0f;

                const float step = 8f;
                int steps = Math.Clamp((int)MathF.Ceiling(total / step), 2, 400);
                var legs = new StageLeg[steps];
                var places = new Vector2[steps + 1];
                var ways = new float[steps + 1];
                Vector2 p = wall;
                float angle = heading;

                // (a leg every step of its way, each along the heading half way through that step, so that the legs meet end to end)
                places[0] = p;
                ways[0] = angle;

                for (int k = 0; k < steps; k++)
                {
                    float turn = side * bend((k + 0.5f) * step) * step;
                    float middle = angle + turn * 0.5f;
                    Vector2 way = new Vector2(MathF.Cos(middle), MathF.Sin(middle));

                    legs[k] = new StageLeg { T = crossed + k * step / speed, P = p, V = way * speed };
                    p += way * step;
                    angle += turn;
                    places[k + 1] = p;
                    ways[k + 1] = angle;
                }

                double over = crossed + steps * step / speed;
                b.Legs = legs;
                b.AlphaFrom = over - DanmakusuBalance.KAKU_RETURN_FADE_MS;
                b.AlphaOver = DanmakusuBalance.KAKU_RETURN_FADE_MS;
                b.Until = over;

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                leaveEchoes(f, b, places, ways, step, speed, crossed);
                return true;
            }

            /// <summary>
            /// Yoshika's: the copies a returning talisman leaves behind it along its curve (<paramref name="places"/>, a point every <paramref name="step"/> px, and which way it heads there), one every talisman's
            /// length (they touch, end to end), from the wall to the end of its way - not off the screen - each standing where it was left for eight beats, facing the way it went, and fading out at the end.
            /// </summary>
            private void leaveEchoes(BulletField f, StageBullet talisman, Vector2[] places, float[] ways, float step, float speed, double crossed)
            {
                float length = talisman.Radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * 2f; // (a talisman from end to end, along its way)

                if (length < 0.5f || places.Length < 2)
                    return;

                double life = beat * DanmakusuBalance.YOSHIKA_ECHO_BEATS;
                Vector2 low = (f.hasScreenBounds ? f.screenMin : f.stageMin) - new Vector2(length), high = (f.hasScreenBounds ? f.screenMax : f.stageMax) + new Vector2(length);
                float total = step * (places.Length - 1);

                for (float s = length; s < total; s += length)
                {
                    int k = Math.Min((int)(s / step), places.Length - 2);
                    float u = s / step - k;
                    Vector2 position = Vector2.Lerp(places[k], places[k + 1], u);

                    if (position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y)
                        continue; // (it may come back in after a loop)

                    float heading = ways[k] + (ways[k + 1] - ways[k]) * u;
                    double at = crossed + s / speed;

                    var echo = talisman;
                    echo.Legs = null;
                    echo.T0 = at;
                    echo.P0 = position;
                    echo.V = Vector2.Zero;
                    echo.KeepFace = true; // (it faces the way the talisman went)
                    echo.Still = true;
                    echo.Untraced = true; // (it stays where it was left: the ops of the other bosses leave it be - tracing thousands of them through those every frame cost 4 ms and more, 2026-11-14)
                    echo.Face = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
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
