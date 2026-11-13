using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kana Anaberal: when her section begins and every sixteen beats after, 32 of the bullets on the screen (anyone's; chosen by a hash of the ring and the bullet, so the same ones on every play) stop where
        // they are. Over four beats they grow to twice their size (hitbox too), turn white and glow; eight beats after they stopped they fly off, each its own way (again by the hash), twice as fast as they flew,
        // and ricochet off the edges of the screen three times before they fly out. Her own shots, a fan of three big bullets at the player every eight beats, are the planner's.
        // ======================================================================================================================================================

        private sealed class PoltergeistAct : StageAct
        {
            private struct Seen
            {
                public BulletEmitter Emitter;
                public int Bullet;
                public Vector2 At;
                public double Time;
                public int Order;
            }

            private const double window_beats = 1;

            private readonly double beat;
            private readonly double[] marks;
            private readonly bool[] done;
            private readonly Dictionary<(BulletEmitter, int), Seen>[] seen;
            private readonly Dictionary<BulletEmitter, int> ringOrder = new Dictionary<BulletEmitter, int>();
            private readonly List<(double Time, Vector2 Where)> bounces = new List<(double, Vector2)>();
            private double resetTime = double.NegativeInfinity;

            public PoltergeistAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.KANA_EVERY_BEATS)
                    times.Add(at);

                marks = times.ToArray();
                done = new bool[marks.Length];
                seen = new Dictionary<(BulletEmitter, int), Seen>[marks.Length];

                for (int i = 0; i < seen.Length; i++)
                    seen[i] = new Dictionary<(BulletEmitter, int), Seen>();

                for (int i = 0; i < f.emitters.Length; i++)
                    ringOrder[f.emitters[i]] = i;

                Start = period.ShootStart;
                End = marks.Length > 0 ? marks[^1] + beat * DanmakusuBalance.KANA_HOLD_BEATS : period.End;
                AbsorbFrom = Start;
                AbsorbTo = marks.Length > 0 ? marks[^1] + beat * window_beats : Start - 1;
            }

            /// <summary>The bullets she stopped last fly off after her section: she stays until they have.</summary>
            public override double MechanicsEnd => End;

            private int markAt(double t)
            {
                for (int i = 0; i < marks.Length; i++)
                {
                    if (t >= marks[i] && t <= marks[i] + beat * window_beats)
                        return i;
                }

                return -1;
            }

            public override bool Absorbs(double t) => markAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double mark in marks)
                {
                    if (mark <= to && mark + beat * window_beats >= from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                // (every ring is looked at again from when it was fired: the same bullets stop)
                ClearBullets();

                for (int i = 0; i < marks.Length; i++)
                {
                    done[i] = false;
                    seen[i].Clear();
                }

                resetTime = time;
            }

            /// <summary>Takes nothing: notes every bullet on the screen at a mark (that was fired before it) and where it is.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Sharp || emitter.Shape == BulletShape.Train)
                    return false;

                int mark = markAt(t);

                if (mark < 0 || done[mark] || emitter.Time > marks[mark])
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                {
                    seen[mark].Remove((emitter, bullet));
                    return false;
                }

                seen[mark][(emitter, bullet)] = new Seen { Emitter = emitter, Bullet = bullet, At = to, Time = t, Order = ringOrder.TryGetValue(emitter, out int order) ? order : int.MaxValue };
                return false;
            }

            /// <summary>At a mark the 32 are chosen out of what was seen, taken out of their rings, and stand where they were.</summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int m = 0; m < marks.Length; m++)
                {
                    if (done[m] || now < marks[m])
                        continue;

                    done[m] = true;

                    var all = new List<Seen>(seen[m].Values);
                    seen[m].Clear();

                    // (the ones whose hash is lowest: always the same ones)
                    all.Sort((a, b) =>
                    {
                        float ha = hash01(a.Order, a.Bullet, 151 + m), hb = hash01(b.Order, b.Bullet, 151 + m);
                        return ha != hb ? ha.CompareTo(hb) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Bullet.CompareTo(b.Bullet);
                    });

                    int stopped = 0;

                    foreach (var one in all)
                    {
                        if (stopped >= DanmakusuBalance.KANA_STOPPED)
                            break;

                        ulong bit = 1UL << one.Bullet;

                        if (((one.Emitter.ConsumedMask | one.Emitter.AbsorbedMask | one.Emitter.HiddenMask) & bit) != 0)
                            continue;

                        one.Emitter.AbsorbedMask |= bit;
                        stop(f, m, one);
                        stopped++;
                    }

                    if (stopped > 0 && now - marks[m] < 250 && marks[m] > resetTime)
                        f.playStageSound("sakuya_stop");
                }
            }

            private void stop(BulletField f, int mark, in Seen one)
            {
                var stored = f.makeStored(one.Emitter, one.Time);
                float big = one.Emitter.BigScale(one.Bullet);
                double release = marks[mark] + beat * DanmakusuBalance.KANA_HOLD_BEATS;
                float angle = hash01(one.Order, one.Bullet, 977 + mark) * MathF.Tau;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float speed = stored.Speed * DanmakusuBalance.KANA_SPEED;

                bounces.Clear();
                var legs = f.bouncingLegs(release, one.At, way * speed, DanmakusuBalance.KANA_RICOCHETS, bounces);
                var lastLeg = legs[^1];
                float radius = stored.Radius * big;

                AddBullet(new StageBullet
                {
                    T0 = one.Time,
                    P0 = one.At,
                    V = Vector2.Zero,
                    Legs = legs,
                    Face = way,
                    Radius = radius,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.Tint > 0 ? stored.TintAmount : 0f,
                    Shade = stored.Shade,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,

                    // (over four beats: twice as big, white, glowing)
                    ScaleFrom = one.Time,
                    ScaleOver = beat * DanmakusuBalance.KANA_GROW_BEATS,
                    ScaleTo = DanmakusuBalance.KANA_GROW,
                    Tint2At = one.Time,
                    Tint2 = BulletRenderer.TINT_WHITE,
                    Tint2Amount = 1f,
                    Tint2Over = beat * DanmakusuBalance.KANA_GROW_BEATS,
                    BloomAt = one.Time,
                    BloomTo = DanmakusuBalance.KANA_BLOOM,

                    Until = lastLeg.T + f.toWall(lastLeg.P, lastLeg.V, out _) + radius * DanmakusuBalance.KANA_GROW * 3f / Math.Max(speed, 1e-3f),
                });

                foreach (var (time, where) in bounces)
                    f.addFlash(time, where, radius * DanmakusuBalance.KANA_GROW * DanmakusuBalance.RICOCHET_FLASH_SIZE);
            }
        }
    }
}
