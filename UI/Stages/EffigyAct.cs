using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Ubame Chirizuka (2026-11-22): every UBAME_EVERY_BEATS of her section she throws a fan of UBAME_DOLLS dolls (the user's photo of a fumo, UBAME_DOLL_CIRCLES circle radii tall, turning as they fly),
        // spread evenly over UBAME_FAN_DEGREES round the way to the player as it is then, flying straight on at UBAME_DOLL_SPEED of a bullet's speed until they have left the screen. A doll hurts like a slider
        // body (as far as UBAME_DOLL_HIT of its height round its middle).
        // Every bullet of a ring that a doll touches (anyone's, but Yukari's train and what homes) is shot again from the doll, at the player as they are then, UBAME_RESHOT_SIZE times as big, at the speed it
        // had, with a flash. Two such bullets of two different dolls that touch bounce off each other (each as off a wall, keeping its speed), with a flash; each bounces once.
        // Where each doll is follows from the time; where each fan was aimed is kept, so time going back throws them again the same way.
        // ======================================================================================================================================================

        private sealed class EffigyAct : StageAct
        {
            private readonly double beat;
            private readonly double[] throws;
            private readonly Vector2[] thrownFrom;
            private readonly float[] thrownAim;
            private readonly bool[] known;
            private readonly double[] gone; // (when each doll of each fan has left the screen: throw * UBAME_DOLLS + doll)

            // (the bullets shot again, one entry for every one of the act's bullets: which doll shot it, and whether it has bounced)
            private readonly List<int> dollOf = new List<int>();
            private readonly List<bool> bounced = new List<bool>();
            private readonly List<int> loose = new List<int>(); // (the ones that can still bounce)
            private readonly List<(double Time, int A, int B)> meets = new List<(double, int, int)>();
            private double checkedTo = double.NegativeInfinity;

            // (where the dolls are at the moment last asked for)
            private double placedAt = double.NaN;
            private readonly List<(Vector2 At, float Scale, int Id)> placed = new List<(Vector2, float, int)>();

            private const float picture_aspect = 226f / 256f; // (the doll's picture: height over width)

            public EffigyAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                for (double t = period.ShootStart; t < period.End - 1 && list.Count < 10000; t += beat * DanmakusuBalance.UBAME_EVERY_BEATS)
                    list.Add(t);

                throws = list.ToArray();
                thrownFrom = new Vector2[throws.Length];
                thrownAim = new float[throws.Length];
                known = new bool[throws.Length];
                gone = new double[throws.Length * DanmakusuBalance.UBAME_DOLLS];
                Array.Fill(gone, double.PositiveInfinity);

                Start = period.ShootStart;
                End = period.End + 20000; // (the last dolls and what they shot fly on after her section)
                AbsorbFrom = period.ShootStart;
                AbsorbTo = End;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                dollOf.Clear();
                bounced.Clear();
                loose.Clear();
                checkedTo = double.NegativeInfinity;
                placedAt = double.NaN;

                for (int k = 0; k < throws.Length; k++)
                {
                    if (throws[k] > time)
                        known[k] = false;
                }
            }

            // ---- the dolls ----

            private float dollHeight(BulletField f) => f.circleRadius * DanmakusuBalance.UBAME_DOLL_CIRCLES;

            private float dollSpeed() => stageBulletSpeed(Period) * DanmakusuBalance.UBAME_DOLL_SPEED;

            private Vector2 dollWay(int k, int i)
            {
                int n = DanmakusuBalance.UBAME_DOLLS;
                float fan = DanmakusuBalance.UBAME_FAN_DEGREES * MathF.PI / 180f;
                float angle = thrownAim[k] + (n > 1 ? fan * (i / (n - 1f) - 0.5f) : 0f);
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            /// <summary>How far out of her a doll is (0 .. 1): it comes out over UBAME_COME_OUT_BEATS.</summary>
            private float outOfHer(int k, double t) => 0.3f + 0.7f * smoothStep((t - throws[k]) / (beat * DanmakusuBalance.UBAME_COME_OUT_BEATS));

            private Vector2 dollAt(int k, int i, double t) => thrownFrom[k] + dollWay(k, i) * (dollSpeed() * (float)Math.Max(t - throws[k], 0));

            /// <summary>The first fan that can still have a doll on the screen at <paramref name="t"/>.</summary>
            private int firstThrow(double t)
            {
                double longest = 60000; // (a doll crosses the screen well within this)
                int k = Array.BinarySearch(throws, t - longest);
                return Math.Max(k < 0 ? ~k : k, 0);
            }

            /// <summary>Where every doll that is out is at <paramref name="t"/> (kept for the frame: the hit check and the bullets ask many times).</summary>
            private List<(Vector2 At, float Scale, int Id)> dollsAt(double t)
            {
                if (t == placedAt)
                    return placed;

                placedAt = t;
                placed.Clear();

                for (int k = firstThrow(t); k < throws.Length && throws[k] <= t; k++)
                {
                    if (!known[k])
                        continue;

                    for (int i = 0; i < DanmakusuBalance.UBAME_DOLLS; i++)
                    {
                        if (t >= gone[k * DanmakusuBalance.UBAME_DOLLS + i])
                            continue;

                        placed.Add((dollAt(k, i, t), outOfHer(k, t), k * DanmakusuBalance.UBAME_DOLLS + i));
                    }
                }

                return placed;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (every fan whose time has come takes where she is and where the player is, once)
                for (int k = firstThrow(now); k < throws.Length && throws[k] <= now; k++)
                {
                    if (known[k])
                        continue;

                    known[k] = true;
                    thrownFrom[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - thrownFrom[k];
                    thrownAim[k] = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    float speed = dollSpeed();
                    float margin = dollHeight(f) / picture_aspect;

                    for (int i = 0; i < DanmakusuBalance.UBAME_DOLLS; i++)
                        gone[k * DanmakusuBalance.UBAME_DOLLS + i] = throws[k] + f.toWall(thrownFrom[k], dollWay(k, i) * speed, out _) + margin / Math.Max(speed, 1e-4f);

                    placedAt = double.NaN;

                    if (now - throws[k] < 250)
                        f.playStageSound("reimu_orb_throw");
                }

                bounceOff(f, now);
            }

            /// <summary>A doll hurts like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = dollHeight(f) * DanmakusuBalance.UBAME_DOLL_HIT + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var (at, scale, _) in dollsAt(now))
                {
                    if (Vector2.DistanceSquared(player, at) <= reach * reach * scale * scale)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                float height = dollHeight(f);
                Vector2 size = new Vector2(height / picture_aspect, height);

                foreach (var (at, scale, id) in dollsAt(t))
                {
                    int k = id / DanmakusuBalance.UBAME_DOLLS;
                    float spin = (float)((t - throws[k]) * 0.18) * (id % 2 == 0 ? 1f : -1f); // (each turns as it flies, every second one the other way)
                    f.stageFront.Add(StageTexture.RumiaFumo, at, size * scale, spin, 1f, Color4.White);
                }
            }

            // ---- what the dolls touch ----

            /// <summary>A bullet that a doll touches is shot again from the doll at the player, half as big again.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                var dolls = dollsAt(t);

                if (dolls.Count == 0)
                    return false;

                float big = emitter.BigScale(bullet);
                float reach = dollHeight(f) * DanmakusuBalance.UBAME_DOLL_HIT + emitter.HitRadius * big;

                foreach (var (at, scale, id) in dolls)
                {
                    float r = reach * scale;

                    // (a quick look first: the step's box against the doll)
                    if (at.X < Math.Min(from.X, to.X) - r || at.X > Math.Max(from.X, to.X) + r || at.Y < Math.Min(from.Y, to.Y) - r || at.Y > Math.Max(from.Y, to.Y) + r)
                        continue;

                    if (distanceToSegment(at, from, to) > r)
                        continue;

                    var stored = f.makeStored(emitter, t);
                    float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                    Vector2 toPlayer = f.characterPosition - at;
                    Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                    var again = f.makeStageBullet(stored, emitter, bullet, t, at, way * speed);
                    again.Radius *= DanmakusuBalance.UBAME_RESHOT_SIZE;
                    again.Hit *= DanmakusuBalance.UBAME_RESHOT_SIZE;
                    again.Untraced = true; // (its bounce is worked out here: no other boss's op turns it)

                    int before = Bullets.Count;
                    AddBullet(again);

                    if (Bullets.Count == before)
                        return false;

                    dollOf.Add(id);
                    bounced.Add(false);
                    loose.Add(Bullets.Count - 1);
                    f.addFlash(t, at, again.Radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Two bullets shot again by two different dolls that touch between the last check and <paramref name="now"/> bounce off each other, the earliest first: each turns as off a wall that faces the other,
            /// keeping its speed, and flies on so (its legs from then on); each does it once.
            /// </summary>
            private void bounceOff(BulletField f, double now)
            {
                double since = Math.Max(checkedTo, now - 250);

                if (now <= checkedTo)
                {
                    checkedTo = now;
                    return;
                }

                checkedTo = now;

                // (no more the ones that have bounced, are gone, or have hit the player)
                loose.RemoveAll(i => bounced[i] || Bullets[i].Until < since || f.stageConsumed.Contains(Id * 65536 + i));

                // (the newest few hundred: two hundred bullets are twenty thousand pairs)
                if (loose.Count > 300)
                    loose.RemoveRange(0, loose.Count - 300);

                if (loose.Count < 2)
                    return;

                meets.Clear();

                for (int x = 0; x < loose.Count; x++)
                {
                    var a = Bullets[loose[x]];

                    if (a.T0 > now)
                        continue;

                    for (int y = x + 1; y < loose.Count; y++)
                    {
                        if (dollOf[loose[x]] == dollOf[loose[y]])
                            continue;

                        var b = Bullets[loose[y]];
                        double t0 = Math.Max(Math.Max(a.T0, b.T0), since);

                        if (t0 > now)
                            continue;

                        Vector2 pa = a.P0 + a.V * (float)(t0 - a.T0), pb = b.P0 + b.V * (float)(t0 - b.T0);
                        Vector2 dp = pb - pa, dv = b.V - a.V;
                        float touch = (a.Radius + b.Radius) * 0.85f;
                        float c = Vector2.Dot(dp, dp) - touch * touch;
                        float half = Vector2.Dot(dp, dv);

                        if (half >= 0f)
                            continue; // (not closing in)

                        if (c <= 0f)
                        {
                            meets.Add((t0, loose[x], loose[y]));
                            continue;
                        }

                        float dd = Vector2.Dot(dv, dv);
                        float disc = half * half - dd * c;

                        if (dd < 1e-12f || disc < 0f)
                            continue;

                        double s = (-half - MathF.Sqrt(disc)) / dd;

                        if (s >= 0 && t0 + s <= now)
                            meets.Add((t0 + s, loose[x], loose[y]));
                    }
                }

                if (meets.Count == 0)
                    return;

                meets.Sort((m1, m2) => m1.Time.CompareTo(m2.Time));

                foreach (var (time, ia, ib) in meets)
                {
                    if (bounced[ia] || bounced[ib])
                        continue;

                    var a = Bullets[ia];
                    var b = Bullets[ib];
                    Vector2 pa = a.P0 + a.V * (float)(time - a.T0), pb = b.P0 + b.V * (float)(time - b.T0);
                    Vector2 normal = pb - pa;
                    normal = normal.LengthSquared > 1e-6f ? Vector2.Normalize(normal) : new Vector2(1f, 0f);

                    a = bounceOne(f, a, time, pa, normal);
                    b = bounceOne(f, b, time, pb, -normal);
                    Bullets[ia] = a;
                    Bullets[ib] = b;
                    bounced[ia] = bounced[ib] = true;
                    f.addFlash(time, (pa + pb) * 0.5f, Math.Max(a.Radius, b.Radius) * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                }

                LastUntil = double.NegativeInfinity;

                foreach (var bullet in Bullets)
                    LastUntil = Math.Max(LastUntil, bullet.Until);
            }

            /// <summary>The bullet turned at <paramref name="time"/> as off a wall facing <paramref name="towards"/> (when it was going that way), flying on from <paramref name="at"/>.</summary>
            private static StageBullet bounceOne(BulletField f, StageBullet b, double time, Vector2 at, Vector2 towards)
            {
                Vector2 v = b.V;
                float into = Vector2.Dot(v, towards);

                if (into > 0f)
                    v -= towards * (2f * into);

                b.Legs = new[]
                {
                    new StageLeg { T = b.T0, P = b.P0, V = b.V },
                    new StageLeg { T = time, P = at, V = v },
                };
                b.Until = time + f.stageLife(v);
                return b;
            }
        }
    }
}
