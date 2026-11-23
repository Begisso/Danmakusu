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
        // Yoshika Miyako (2026-11-13) does the same with every fourth bullet, which comes back at its own speed as a talisman YOSHIKA_TALISMAN_SIZE times as big. Since 2026-11-22 it comes back along a curve
        // as Aunn's beams do: bending ever more over its first YOSHIKA_CURL_CIRCLES circle radii, once round a loop YOSHIKA_LOOP_BIG wide, once round one YOSHIKA_LOOP_SMALL wide, then straight on out of the
        // screen (each one curling the other way from the last; legs every few px, so all of it is worked out when the bullet turns and known before it comes), and a train of its copies as long as it goes in YOSHIKA_TRAIN_BEATS
        // follows it end to end along the same legs, as the body of Aunn's beam follows its head. (Until then its copies stood where it left them for eight beats - since the curve, rings
        // of talismans standing all over the screen: the user's "why circles stopped".)
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
                Vector2 p = wall;
                float angle = heading;

                // (a leg every step of its way, each along the heading half way through that step, so that the legs meet end to end)
                for (int k = 0; k < steps; k++)
                {
                    float turn = side * bend((k + 0.5f) * step) * step;
                    float middle = angle + turn * 0.5f;
                    Vector2 way = new Vector2(MathF.Cos(middle), MathF.Sin(middle));

                    legs[k] = new StageLeg { T = crossed + k * step / speed, P = p, V = way * speed };
                    p += way * step;
                    angle += turn;
                }

                // (after its curve it flies straight on, the way its last leg goes, out of the screen: gone there)
                double over = crossed + steps * step / speed;
                double until = over + f.stageLife(legs[^1].V);
                b.Legs = legs;
                b.AlphaFrom = until - DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                b.AlphaOver = DanmakusuBalance.YOSHIKA_ECHO_FADE_MS;
                b.Until = until;

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                followTrain(f, b, speed, crossed);
                return true;
            }

            /// <summary>
            /// Yoshika's: the train behind a returning talisman (2026-11-22; it left copies standing on its way for eight beats, and its loops stood on the screen as rings): as long as it goes in YOSHIKA_TRAIN_BEATS, copies
            /// one every talisman's length (end to end), each going along the very same legs a length later than the one before it, out of the wall where it came back - the body of a snake following its head, as Aunn's
            /// beams do - and on out of the screen. The legs are shared (<see cref="StageBullet.LegsDelay"/>).
            /// </summary>
            private void followTrain(BulletField f, StageBullet talisman, float speed, double crossed)
            {
                float length = talisman.Radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * 2f; // (a talisman from end to end, along its way)

                if (length < 0.5f)
                    return;

                int cars = Math.Clamp((int)Math.Round(speed * beat * DanmakusuBalance.YOSHIKA_TRAIN_BEATS / length), 0, 64);

                for (int k = 1; k <= cars; k++)
                {
                    double delay = k * length / speed;

                    var car = talisman;
                    car.LegsDelay = delay;
                    car.T0 = crossed + delay;
                    car.AlphaFrom = talisman.AlphaFrom + delay;
                    car.Until = talisman.Until + delay;

                    int count = Bullets.Count;
                    AddBullet(car);

                    if (Bullets.Count == count)
                        break; // (the act is full)
                }
            }
        }
    }
}
