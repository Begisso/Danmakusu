using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Satono Nishida (2026-11-25): all through her section every bullet of a ring that goes out of the top of the screen comes back down from where it left, straight down, at the speed it had (once; not
        // one that ricochets, falls or homes); every bullet of a ring that goes out of the bottom - one that came back down too - leaves a talisman behind where it left, as big as a boss's star, flying
        // straight at the player as they are then at SATONO_TALISMAN_SPEED times a plain bullet's speed, never turning (the talismans themselves do neither).
        // Every SATONO_STAR_EVERY_BEATS a Marisa's star (the soft star, as big as a boss's star) comes out somewhere in her magic circle, stands SATONO_STAR_STAND_BEATS, then flies off along the line from her
        // to the player as it is then (parallel to it, not at the player) at SATONO_STAR_SPEED times a plain bullet's speed; her stars too come back down from the top and leave talismans at the bottom.
        // Where a star comes out (round where she is), which way it flies and where a talisman flies are taken when it happens, and kept; going back in time makes their bullets again from what was kept.
        // ======================================================================================================================================================

        private sealed class MyogaAct : StageAct
        {
            private sealed class Star
            {
                public double T; // (when it comes out)
                public Vector2 At;
                public bool Flies; // (its way is known)
                public Vector2 Way;
                public int Index = -1; // (its bullet in Bullets; -1: not made yet)
                public double TalismanAt = double.NaN; // (when it goes out of the bottom, if it does)
                public Vector2 TalismanFrom;
                public bool TalismanKnown;
                public Vector2 TalismanWay;
                public bool TalismanMade;
            }

            private readonly double beat;
            private readonly double[] starTimes;
            private readonly List<Star> stars = new List<Star>();
            private readonly HashSet<(BulletEmitter, int)> dropped = new HashSet<(BulletEmitter, int)>(); // (a bullet comes back down once)
            private readonly HashSet<(BulletEmitter, int)> released = new HashSet<(BulletEmitter, int)>(); // (a bullet leaves one talisman)
            private int talismans; // (how many talismans there have been: their colours go round)

            public MyogaAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.SATONO_STAR_EVERY_BEATS;

                for (double t = period.ShootStart; t < period.End && list.Count < 60000; t += every)
                    list.Add(t);

                starTimes = list.ToArray();
                Start = period.ShootStart;
                End = period.End + beat * 32; // (her last stars and their talismans fly on after her section)
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            private double standMs => beat * DanmakusuBalance.SATONO_STAR_STAND_BEATS;

            private float starRadius(BulletField f) => Period.StarRadius > 0 ? Period.StarRadius : f.stageBaseRadius(Period) * 1.5f;

            public override double MechanicsEnd => starTimes.Length > 0 ? starTimes[^1] + standMs : double.NegativeInfinity;

            public override void Reset(BulletField f, double time)
            {
                // (the stars that had come out keep where they did, and the way they flew if that was known; their bullets and all the talismans are made again - the field scans the rings again from their start)
                stars.RemoveAll(star => star.T > time);

                foreach (var star in stars)
                {
                    if (star.T + standMs > time)
                    {
                        star.Flies = false;
                        star.TalismanAt = double.NaN;
                    }

                    if (star.TalismanAt > time)
                        star.TalismanKnown = false;

                    star.Index = -1;
                    star.TalismanMade = false;
                }

                dropped.Clear();
                released.Clear();
                talismans = 0;
                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every star whose time has come comes out somewhere in her magic circle, round where she is now)
                while (stars.Count < starTimes.Length && starTimes[stars.Count] <= now)
                {
                    int n = stars.Count;
                    float angle = hash01(Id, n, 211) * MathF.Tau;
                    float distance = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * MathF.Sqrt(hash01(Id, n, 212)); // (evenly over the disc)
                    stars.Add(new Star { T = starTimes[n], At = f.bossPositionOf(Period) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance });
                }

                for (int n = 0; n < stars.Count; n++)
                {
                    var star = stars[n];

                    // (it sets off along the line from her to the player, as that is when its time comes)
                    if (!star.Flies && star.T + standMs <= now)
                    {
                        Vector2 line = f.characterPosition - f.bossPositionOf(Period);
                        star.Way = line.LengthSquared > 1f ? Vector2.Normalize(line) : Vector2.UnitY;
                        star.Flies = true;

                        if (star.Index >= 0 && star.Index < Bullets.Count)
                        {
                            var flying = Bullets[star.Index];
                            setFlight(f, star, ref flying);
                            Bullets[star.Index] = flying;
                        }
                    }

                    if (star.Index < 0)
                        makeStar(f, star, n);

                    // (its talisman, when it goes out of the bottom)
                    if (star.Flies && !star.TalismanMade && !double.IsNaN(star.TalismanAt) && star.TalismanAt <= now)
                    {
                        if (!star.TalismanKnown)
                        {
                            star.TalismanWay = towardsPlayer(f, star.TalismanFrom);
                            star.TalismanKnown = true;
                        }

                        talisman(f, star.TalismanAt, star.TalismanFrom, star.TalismanWay);
                        star.TalismanMade = true;
                    }
                }
            }

            private void makeStar(BulletField f, Star star, int n)
            {
                float radius = starRadius(f);
                var bullet = new StageBullet
                {
                    T0 = star.T,
                    P0 = star.At,
                    V = Vector2.Zero,
                    Legs = Array.Empty<StageLeg>(), // (it stands where it came out until its flight is known)
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = f.comboStyle(n, true, Period.Rank),
                    Shape = BulletShape.SoftStar,
                    Spin = 0.003f * (n % 2 == 0 ? 1f : -1f),
                    SpinPhase = n,
                    Until = star.T + standMs + beat * 64,
                    Untraced = true,
                };

                if (star.Flies)
                    setFlight(f, star, ref bullet);

                star.Index = Bullets.Count;
                AddBullet(bullet);

                if (Bullets.Count <= star.Index)
                    star.Index = int.MaxValue;
            }

            /// <summary>
            /// The legs of a star that has set off: straight along its way; if it goes out of the top (inside the screen's width) it comes back down from there, as fast; where it goes out of the bottom after
            /// that it leaves a talisman.
            /// </summary>
            private void setFlight(BulletField f, Star star, ref StageBullet bullet)
            {
                double release = star.T + standMs;
                Vector2 v = star.Way * (stageBulletSpeed(Period) * DanmakusuBalance.SATONO_STAR_SPEED);
                var legs = new List<StageLeg> { new StageLeg { T = release, P = star.At, V = v } };
                float top = f.stageMin.Y, bottom = f.stageMax.Y;

                if (v.Y < -1e-6f && star.At.Y >= top)
                {
                    double up = (top - star.At.Y) / v.Y;
                    float x = star.At.X + v.X * (float)up;

                    if (x >= f.stageMin.X && x <= f.stageMax.X)
                        legs.Add(new StageLeg { T = release + up, P = new Vector2(x, top), V = new Vector2(0f, v.Length) });
                }

                var last = legs[^1];
                star.TalismanAt = double.NaN;

                if (last.V.Y > 1e-6f && last.P.Y <= bottom)
                {
                    double down = (bottom - last.P.Y) / last.V.Y;
                    float x = last.P.X + last.V.X * (float)down;

                    if (x >= f.stageMin.X && x <= f.stageMax.X)
                    {
                        star.TalismanAt = last.T + down;
                        star.TalismanFrom = new Vector2(x, bottom);
                    }
                }

                bullet.Legs = legs.ToArray();
                bullet.Until = last.T + f.stageLife(last.V) + bullet.Radius * 3f / Math.Max(last.V.Length, 1e-3f);
            }

            private static Vector2 towardsPlayer(BulletField f, Vector2 from)
            {
                Vector2 way = f.characterPosition - from;
                return way.LengthSquared > 1f ? Vector2.Normalize(way) : -Vector2.UnitY;
            }

            /// <summary>A talisman from <paramref name="at"/> at <paramref name="t"/>, flying straight along <paramref name="way"/>.</summary>
            private void talisman(BulletField f, double t, Vector2 at, Vector2 way)
            {
                float radius = starRadius(f);
                Vector2 v = way * (stageBulletSpeed(Period) * DanmakusuBalance.SATONO_TALISMAN_SPEED);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = v,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = f.comboStyle(talismans++, true, Period.Rank),
                    Shape = BulletShape.Talisman,
                    Until = t + f.stageLife(v) + radius * 3f / Math.Max(v.Length, 1e-3f),
                    Untraced = true, // (it never changes its way)
                });
            }

            /// <summary>
            /// A bullet of a ring that goes out of the bottom leaves a talisman behind (it flies on out); one that goes out of the top comes back down from there, straight down at the speed it had.
            /// </summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitY;
                speed = 0f;

                if (f.failed || t < AbsorbFrom || t > AbsorbTo || emitter.Shape == BulletShape.Train)
                    return false;

                float top = f.stageMin.Y, bottom = f.stageMax.Y;

                // out of the bottom: a talisman where it left, at the player as they are now (once)
                if (from.Y <= bottom && to.Y > bottom)
                {
                    float u = Math.Clamp((bottom - from.Y) / Math.Max(to.Y - from.Y, 1e-6f), 0f, 1f);
                    float x = from.X + (to.X - from.X) * u;

                    if (x >= f.stageMin.X && x <= f.stageMax.X && released.Add((emitter, bullet)))
                    {
                        double when = ScanPrevious + (t - ScanPrevious) * u;
                        var at = new Vector2(x, bottom);
                        talisman(f, when, at, towardsPlayer(f, at));
                    }

                    return false;
                }

                // out of the top: it comes back down from there (once)
                if (!emitter.CanBeDeflected || emitter.Ricochet || from.Y < top || to.Y >= top || dropped.Contains((emitter, bullet)))
                    return false;

                float w = Math.Clamp((from.Y - top) / Math.Max(from.Y - to.Y, 1e-6f), 0f, 1f);
                float across = from.X + (to.X - from.X) * w;

                if (across < f.stageMin.X || across > f.stageMax.X)
                    return false;

                double before = emitter.WithoutFrozenTime(f.effects.Effective(emitter, ScanPrevious));
                double after = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));
                float flown = (to - from).Length;

                position = new Vector2(across, top);
                direction = Vector2.UnitY;
                speed = after - before > 1e-6 && flown > 1e-6f ? flown / (float)(after - before) : Math.Max(emitter.Speed, 0.02f);
                dropped.Add((emitter, bullet));
                return true;
            }
        }
    }
}
