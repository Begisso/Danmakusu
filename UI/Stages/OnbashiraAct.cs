using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kanako Yasaka (2026-11-20): when each of her attacks begins and every eight beats in it she throws five big circles of talismans round her (each KANAKO_CIRCLE_RADII circle radii wide, their middles
        // evenly round her): the talismans lie end to end all round each circle, every fourth one left out. The circles fly at once (2026-11-21; they turned round her for two beats first)
        // straight away from her, the talismans still going round each moving circle (the circles themselves are not seen), until eight beats later the circle stops turning and flies on as it is
        // (2026-11-21). She always stands in the middle (2026-11-21). A circle that has wholly left the screen turns round and flies back the way it came,
        // its talismans dark blue, as everything a boss shoots does in her section (the rest of that is an op, see BossOps).
        // Like every act's bullets they are a function of time (StageBullet.Orbit).
        // ======================================================================================================================================================

        private sealed class OnbashiraAct : StageAct
        {
            private readonly double beat;
            private readonly List<double> volleys = new List<double>();
            private int next;

            public OnbashiraAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = start; t < end - 1; t += beat * DanmakusuBalance.KANAKO_EVERY_BEATS)
                        volleys.Add(t);
                }

                volleys.Sort();
                Start = volleys.Count > 0 ? volleys[0] : period.ShootStart;
                End = period.End;
            }

            private readonly Dictionary<int, (Vector2 From, float Aim)> thrownFrom = new Dictionary<int, (Vector2, float)>(); // (where each volley was thrown from, and at)

            public override void Reset(BulletField f, double time)
            {
                // (the volleys before that moment are thrown again from where they were thrown; the later ones from where she is when their time comes)
                ClearBullets();
                next = 0;

                foreach (int k in new List<int>(thrownFrom.Keys))
                {
                    if (volleys[k] > time)
                        thrownFrom.Remove(k);
                }

                for (; next < volleys.Count && volleys[next] <= time; next++)
                {
                    if (thrownFrom.TryGetValue(next, out var known))
                        throwVolley(f, next, volleys[next], known.From, known.Aim);
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (; next < volleys.Count && volleys[next] <= now; next++)
                {
                    Vector2 from = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from;
                    float aim = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    thrownFrom[next] = (from, aim);
                    throwVolley(f, next, volleys[next], from, aim);
                }
            }

            /// <summary>Volley <paramref name="k"/>: five circles round her, turning round her one way (the next volley the other), then flying away from her.</summary>
            private void throwVolley(BulletField f, int k, double t, Vector2 centre, float aim)
            {
                float ring = f.circleRadius * DanmakusuBalance.KANAKO_CIRCLE_RADII;
                float hubOut = ring * DanmakusuBalance.KANAKO_HUB_OUT;
                float turn = k % 2 == 0 ? 1f : -1f;
                float hubOmega = turn * (float)(Math.Tau / (beat * DanmakusuBalance.KANAKO_HUB_TURN_BEATS));
                float ringOmega = turn * (float)(Math.Tau / (beat * DanmakusuBalance.KANAKO_RING_TURN_BEATS));
                double spinUntil = t + beat * DanmakusuBalance.KANAKO_SPIN_BEATS;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.KANAKO_SPEED;
                float radius = f.stageBaseRadius(Period);
                float length = radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * 2f; // (a talisman from end to end)
                int around = Math.Max(8, (int)MathF.Floor(MathF.Tau * ring / Math.Max(length, 1f)));

                // (the screen, and as much more as a circle and a talisman reach: a circle out of that has wholly left it)
                Vector2 low = (f.hasScreenBounds ? f.screenMin : f.stageMin) - new Vector2(ring + length), high = (f.hasScreenBounds ? f.screenMax : f.stageMax) + new Vector2(ring + length);

                for (int c = 0; c < DanmakusuBalance.KANAKO_CIRCLES; c++)
                {
                    float hubPhase = aim + c * MathF.Tau / DanmakusuBalance.KANAKO_CIRCLES;
                    float leaveAngle = hubPhase + hubOmega * (float)(spinUntil - t);
                    Vector2 hubThen = centre + new Vector2(MathF.Cos(leaveAngle), MathF.Sin(leaveAngle)) * hubOut;
                    Vector2 away = hubThen - centre;
                    Vector2 velocity = (away.LengthSquared > 1e-4f ? Vector2.Normalize(away) : Vector2.UnitY) * speed;

                    // (when it has wholly left the screen it turns round, and is gone when it has wholly left it again on the other side)
                    double outAt = spinUntil + timeOut(hubThen, velocity, low, high);
                    Vector2 turnAt = hubThen + velocity * (float)(outAt - spinUntil);
                    double backAt = outAt + timeOut(turnAt - velocity * 1f, -velocity, low, high, true);
                    int style = f.comboStyle(c, true, Period.Rank);

                    for (int i = 0; i < around; i++)
                    {
                        if (i % DanmakusuBalance.KANAKO_SKIP_EVERY == DanmakusuBalance.KANAKO_SKIP_EVERY - 1)
                            continue; // (every fourth talisman left out)

                        float phase = i * MathF.Tau / around;

                        AddBullet(new StageBullet
                        {
                            T0 = t,
                            P0 = centre,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = style,
                            Shape = BulletShape.Talisman,
                            Orbit = ring,
                            OrbitOmega = ringOmega,
                            OrbitPhase = phase,
                            HubCentre = centre,
                            HubRadius = hubOut,
                            HubOmega = hubOmega,
                            HubPhase = hubPhase,
                            HubUntil = spinUntil,
                            HubV = velocity,
                            HubBackAt = outAt,
                            OrbitUntil = spinUntil + beat * DanmakusuBalance.KANAKO_STOP_TURNING_BEATS, // (8 beats after the circle flies out it stops turning: 2026-11-21)
                            Spin = ringOmega, // (each lies along its circle: end to end with the next)
                            SpinPhase = phase + turn * MathF.PI / 2f,
                            Tint2At = outAt,
                            Tint2 = BulletRenderer.TINT_DARK_BLUE,
                            Tint2Amount = 1f,
                            Tint2Over = 200,
                            Grow = 200,
                            Untraced = true,
                            Until = backAt,
                        });
                    }
                }
            }

            /// <summary>
            /// How long something flying from <paramref name="from"/> at <paramref name="velocity"/> takes to leave the box: from inside it, or (<paramref name="through"/>) from outside it, through it and out
            /// of it on the far side. At most a minute.
            /// </summary>
            private static double timeOut(Vector2 from, Vector2 velocity, Vector2 low, Vector2 high, bool through = false)
            {
                double best = 60000;

                for (int axis = 0; axis < 2; axis++)
                {
                    float p = axis == 0 ? from.X : from.Y, v = axis == 0 ? velocity.X : velocity.Y;
                    float lo = axis == 0 ? low.X : low.Y, hi = axis == 0 ? high.X : high.Y;

                    if (MathF.Abs(v) < 1e-6f)
                        continue;

                    // (inside: the wall it flies at; through: the far wall, past the near one)
                    double out_ = v > 0 ? (hi - p) / v : (lo - p) / v;

                    if (out_ >= 0)
                        best = Math.Min(best, out_);
                }

                return through ? best : Math.Max(best, 0);
            }
        }
    }
}
