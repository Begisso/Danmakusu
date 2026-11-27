using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Shingyoku (2026-11-27): all through her section she turns into a yin-yang orb as big as her magic circle (MAGIC_CIRCLE_RADIUS x SHINGYOKU_ORB_SIZE) and throws herself at the player, at SHINGYOKU_ORB_SPEED
        // times the speed of Reimu's orbs: the orb ricochets off the edges of the screen SHINGYOKU_BOUNCES times, flies straight back to where it was thrown from and is the boss again; SHINGYOKU_COOLDOWN_BEATS
        // after that she throws again. A red glow grows on whoever is thrown for SHINGYOKU_WARN_BEATS before. While it flies the orb hurts like a slider body, and every bullet that comes into it is put out on its
        // edge (as Futo's boat pushes them) and ricochets off it. When other bosses are on the screen she throws them instead (all of them at once, spread round the way to the player), and they keep doing
        // what they do as orbs; a boss whose own mechanic is moving her (Mokou's jumps...) is not thrown, and if hers starts to move her in the flight it goes as hers says (BulletField.Carries is asked
        // only after the boss's own acts). Where the player is is taken at each throw; the throws are kept, so a seek shows the same.
        // ======================================================================================================================================================

        private sealed class YinYangAct : StageAct
        {
            private sealed class Throw
            {
                public double Time;
                public BossPeriod Who = null!;
                public readonly List<Vector2> Points = new List<Vector2>(4);
                public readonly List<double> Times = new List<double>(4);
                public double Done => Times[^1];
            }

            private readonly double beat;
            private readonly List<Throw> throws = new List<Throw>();
            private double next; // (when the next throw goes, once the last is back)
            private double resetTime = double.NegativeInfinity;

            public YinYangAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart - beat * DanmakusuBalance.SHINGYOKU_WARN_BEATS;
                End = period.End + beat * 32;
                next = firstThrow;
            }

            private double firstThrow => Period.ShootStart + beat * DanmakusuBalance.SHINGYOKU_WARN_BEATS;

            private float orbRadius => DanmakusuBalance.MAGIC_CIRCLE_RADIUS * DanmakusuBalance.SHINGYOKU_ORB_SIZE;

            private float orbSpeed => Math.Max(stageBulletSpeed(Period) * DanmakusuBalance.REIMU_ORB_SPEED_FACTOR, DanmakusuBalance.REIMU_ORB_MIN_SPEED) * DanmakusuBalance.SHINGYOKU_ORB_SPEED;

            /// <summary>Her last throw comes back after her section: she stays until it has.</summary>
            public override double MechanicsEnd => throws.Count > 0 && throws[^1].Who == Period ? Math.Max(throws[^1].Done, Period.End) : Period.End;

            public override bool PushIsRicochet => true;

            public override void Reset(BulletField f, double time)
            {
                // (the throws made before this moment stay as they were; the later ones are thrown again)
                throws.RemoveAll(t => t.Time > time);
                next = firstThrow;

                foreach (var t in throws)
                    next = Math.Max(next, t.Done + beat * DanmakusuBalance.SHINGYOKU_COOLDOWN_BEATS);

                resetTime = time;
            }

            /// <summary>The bosses to throw now: the others on the screen (not one her own mechanic is moving), or she herself.</summary>
            private void throwNow(BulletField f, double at, double now)
            {
                var who = new List<BossPeriod>(2);

                for (int rank = 0; rank < 3; rank++)
                {
                    var other = f.latestBoss(at, rank);

                    if (other == null || other == Period || other.Boss == null || other.Boss.Mechanic == BossMechanic.YinYang)
                        continue;

                    // (a boss her own mechanic moves goes as it says)
                    if (ownPlace(f, other, at))
                        continue;

                    who.Add(other);
                }

                if (who.Count == 0)
                    who.Add(Period);

                Vector2 player = f.characterPosition;
                double done = at;

                for (int k = 0; k < who.Count; k++)
                {
                    Vector2 from = f.bossPositionOf(who[k]);
                    Vector2 toPlayer = player - from;
                    float aim = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    float spread = DanmakusuBalance.SHINGYOKU_THROW_SPREAD_DEGREES * MathF.PI / 180f;
                    aim += who.Count == 1 ? 0f : spread * (k / (float)(who.Count - 1) - 0.5f) * 2f;

                    var made = new Throw { Time = at, Who = who[k] };
                    plan(f, made, from, new Vector2(MathF.Cos(aim), MathF.Sin(aim)));
                    throws.Add(made);
                    done = Math.Max(done, made.Done);
                }

                next = done + beat * DanmakusuBalance.SHINGYOKU_COOLDOWN_BEATS;

                if (at > resetTime && now - at < 250)
                    f.playStageSound("reimu_orb_throw");
            }

            /// <summary>Is a boss put somewhere by an act of her own at <paramref name="at"/>?</summary>
            private bool ownPlace(BulletField f, BossPeriod other, double at)
            {
                foreach (var act in f.stageActs)
                {
                    if (act.Period == other && act.BossAt(f, at, out _))
                        return true;
                }

                return false;
            }

            /// <summary>The orb's flight: straight on, off the edges of the screen (less its radius) so many times, then straight back to where it was thrown from.</summary>
            private void plan(BulletField f, Throw made, Vector2 from, Vector2 way)
            {
                float r = orbRadius;
                Vector2 lo = f.stageMin + new Vector2(r), hi = f.stageMax - new Vector2(r);

                if (lo.X > hi.X)
                    lo.X = hi.X = (f.stageMin.X + f.stageMax.X) / 2f;

                if (lo.Y > hi.Y)
                    lo.Y = hi.Y = (f.stageMin.Y + f.stageMax.Y) / 2f;

                float speed = Math.Max(orbSpeed, 1e-3f);
                Vector2 p = from;
                double time = made.Time;

                made.Points.Add(p);
                made.Times.Add(time);

                for (int bounce = 0; bounce < DanmakusuBalance.SHINGYOKU_BOUNCES; bounce++)
                {
                    float tx = way.X > 1e-6f ? (hi.X - p.X) / way.X : way.X < -1e-6f ? (lo.X - p.X) / way.X : float.PositiveInfinity;
                    float ty = way.Y > 1e-6f ? (hi.Y - p.Y) / way.Y : way.Y < -1e-6f ? (lo.Y - p.Y) / way.Y : float.PositiveInfinity;
                    float d = Math.Max(Math.Min(tx, ty), 0f);

                    if (float.IsInfinity(d))
                        break;

                    p += way * d;
                    time += d / speed;
                    made.Points.Add(p);
                    made.Times.Add(time);

                    if (tx <= ty + 1e-3f)
                        way.X = -way.X;

                    if (ty <= tx + 1e-3f)
                        way.Y = -way.Y;
                }

                time += Vector2.Distance(p, from) / speed;
                made.Points.Add(from);
                made.Times.Add(time);
            }

            private static Vector2 orbAt(Throw t, double time)
            {
                if (time <= t.Times[0])
                    return t.Points[0];

                for (int i = 1; i < t.Times.Count; i++)
                {
                    if (time <= t.Times[i])
                    {
                        double span = t.Times[i] - t.Times[i - 1];
                        float u = span > 1e-6 ? (float)((time - t.Times[i - 1]) / span) : 1f;
                        return Vector2.Lerp(t.Points[i - 1], t.Points[i], u);
                    }
                }

                return t.Points[^1];
            }

            private Vector2 orbVelocity(Throw t, double time)
            {
                for (int i = 1; i < t.Times.Count; i++)
                {
                    if (time <= t.Times[i])
                    {
                        Vector2 d = t.Points[i] - t.Points[i - 1];
                        return d.LengthSquared > 1e-8f ? Vector2.Normalize(d) * orbSpeed : Vector2.Zero;
                    }
                }

                return Vector2.Zero;
            }

            private static bool flying(Throw t, double time) => time >= t.Time && time <= t.Done;

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                int guard = 0;

                while (next <= now && next < Period.End && guard++ < 8)
                    throwNow(f, next, now);

                // (whoever is an orb is not seen as herself)
                foreach (var t in throws)
                {
                    if (flying(t, now))
                        f.stageHideRank[Math.Clamp(t.Who.Rank, 0, 2)] = 1f;
                }
            }

            public override bool Carries(BulletField f, BossPeriod other, double now, out Vector2 position)
            {
                for (int i = throws.Count - 1; i >= 0; i--)
                {
                    var t = throws[i];

                    if (t.Who == other && flying(t, now))
                    {
                        position = orbAt(t, now);
                        return true;
                    }
                }

                position = default;
                return false;
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position) => Carries(f, Period, now, out position);

            // ---- bullets ricochet off the orb ----

            private bool anyFlying(double from, double to)
            {
                for (int i = throws.Count - 1; i >= 0; i--)
                {
                    if (throws[i].Time <= to && throws[i].Done >= from)
                        return true;

                    if (throws[i].Done < from - 60000)
                        break;
                }

                return false;
            }

            public override bool Absorbs(double t) => anyFlying(t, t);

            public override bool AbsorbsBetween(double from, double to) => anyFlying(from, to);

            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitY;
                speed = 0f;

                if (f.failed || !emitter.CanBeDeflected || emitter.Shape == BulletShape.Train)
                    return false;

                float body = emitter.VisualRadius * emitter.BigScale(bullet);
                float r = orbRadius;

                foreach (var th in throws)
                {
                    if (!flying(th, t))
                        continue;

                    Vector2 c = orbAt(th, t);
                    Vector2 off = to - c;
                    float distance = off.Length;

                    if (distance >= r + body - 0.25f)
                        continue;

                    Vector2 n = distance > 1e-3f ? off / distance : Vector2.UnitY;
                    Vector2 step = to - from;
                    Vector2 way = step.LengthSquared > 1e-8f ? Vector2.Normalize(step) : n;

                    // (put out on its edge; flying into it, it is turned back off it as off a wall)
                    position = c + n * (r + body + 0.5f);
                    direction = Vector2.Dot(way, n) < 0f ? way - n * (2f * Vector2.Dot(way, n)) : way;
                    speed = emitter.SpeedOf(bullet);

                    // (the orb is faster than what it pushes: what is in front of it flies out of its way at least as fast)
                    Vector2 orb = orbVelocity(th, t);

                    if (Vector2.Dot(orb, n) > 0f)
                        speed = Math.Max(speed, Vector2.Dot(orb, n) * 1.05f);

                    if (direction.LengthSquared < 1e-8f)
                        direction = n;
                    else
                        direction = Vector2.Normalize(direction);

                    return true;
                }

                return false;
            }

            // ---- what it looks like, and its hits ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float r = orbRadius;

                // (a red glow grows on whoever is to be thrown)
                double warn = beat * DanmakusuBalance.SHINGYOKU_WARN_BEATS;

                if (next < Period.End && t >= next - warn && t < next)
                {
                    float level = smoothStep((t - (next - warn)) / warn);

                    for (int rank = 0; rank < 3; rank++)
                    {
                        var other = f.latestBoss(t, rank);

                        if (other == null || other.Boss == null)
                            continue;

                        bool mine = other == Period;
                        bool anyOther = false;

                        for (int k = 0; k < 3 && !anyOther; k++)
                        {
                            var o = f.latestBoss(t, k);
                            anyOther = o != null && o != Period && o.Boss != null && o.Boss.Mechanic != BossMechanic.YinYang;
                        }

                        if (mine == anyOther)
                            continue;

                        f.stageFront.Add(StageTexture.Glow, f.bossPositionOf(other), new Vector2(r * 3f * (0.6f + 0.4f * level)), 0f, 0.55f * level, new Color4(1f, 0.25f, 0.2f, 1f), true);
                    }
                }

                foreach (var th in throws)
                {
                    if (!flying(th, t))
                        continue;

                    Vector2 at = orbAt(th, t);
                    float grown = smoothStep(Math.Min(t - th.Time, th.Done - t) / Math.Max(beat * 0.25, 1));
                    float size = 0.5f + 0.5f * grown;

                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(r * 3.2f * size), 0f, 0.35f, new Color4(1f, 0.45f, 0.45f, 1f), true);
                    f.stageBack.Add(StageTexture.YinYang, at, new Vector2(r * 2f * size), (float)(t * DanmakusuBalance.REIMU_ORB_SPIN * 1.5), 1f, Color4.White);
                }
            }

            /// <summary>The orb hurts like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float reach = orbRadius * 0.92f + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var th in throws)
                {
                    if (flying(th, now) && Vector2.DistanceSquared(orbAt(th, now), f.characterPosition) <= reach * reach)
                        return true;
                }

                return false;
            }
        }
    }
}
