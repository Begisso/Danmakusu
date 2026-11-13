using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Yumeko: all through her section every eighth bullet (anyone's but her own knives, counted ring by ring, as soon as it is seen) turns into a knife with a yellow handle where it is: it stops, and falls
        // towards where the player was at that moment, faster and faster. Everything that goes all the way out of the screen is remembered, sixteen beats at a time: in the four beats after them it all comes back
        // as yellow-handled knives, in the order it left and four times as close together, each from the place where it left, flying straight across from that wall (what left at the left comes back from the left
        // and flies to the right) at the speed it had. Her own knives (fans of five at the player every two beats) are the planner's.
        // ======================================================================================================================================================

        private sealed class RecallAct : StageAct
        {
            private struct Left
            {
                public double Time;
                public Vector2 Where; // (on the edge of the screen)
                public Vector2 Inward; // (straight across from its wall)
                public float Speed;
                public float Radius;
                public float Hit;
                public int Style;
            }

            private readonly double beat;
            private readonly double[] windowStarts;
            private readonly double[] windowEnds;
            private readonly List<Left>[] left;
            private readonly bool[] returned;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;

            public RecallAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var starts = new List<double>();
                var ends = new List<double>();
                double record = beat * DanmakusuBalance.YUMEKO_RECORD_BEATS;

                for (double at = period.ShootStart; at < period.End - 1; at += record)
                {
                    starts.Add(at);
                    ends.Add(Math.Min(at + record, period.End));
                }

                windowStarts = starts.ToArray();
                windowEnds = ends.ToArray();
                left = new List<Left>[windowStarts.Length];
                returned = new bool[windowStarts.Length];

                for (int w = 0; w < left.Length; w++)
                    left[w] = new List<Left>();

                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            /// <summary>What left in her last sixteen beats comes back after her section: she stays until it has.</summary>
            public override double MechanicsEnd => Period.End + beat * DanmakusuBalance.YUMEKO_RETURN_BEATS;

            public override void Reset(BulletField f, double time)
            {
                // (every ring is looked at again from when it was fired: the same bullets turn and leave)
                bases.Clear();
                counted = 0;
                ClearBullets();

                for (int w = 0; w < left.Length; w++)
                {
                    left[w].Clear();
                    returned[w] = false;
                }
            }

            private int windowAt(double t)
            {
                for (int w = 0; w < windowStarts.Length; w++)
                {
                    if (t >= windowStarts[w] && t < windowEnds[w])
                        return w;
                }

                return -1;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Period.ShootStart || t >= Period.End || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                // what goes all the way out of the screen is remembered (it is gone anyway: nothing is taken)
                float r = emitter.VisualRadius * emitter.BigScale(bullet);
                Vector2 low = f.stageMin - new Vector2(r), high = f.stageMax + new Vector2(r);
                bool wasIn = from.X >= low.X && from.X <= high.X && from.Y >= low.Y && from.Y <= high.Y;
                bool isOut = to.X < low.X || to.X > high.X || to.Y < low.Y || to.Y > high.Y;

                if (wasIn && isOut)
                {
                    remember(f, t, from, to, emitter, bullet);
                    return false;
                }

                // every eighth bullet (counted ring by ring, as each ring is first seen) turns into a falling knife; not her own knives
                if (emitter.YellowHandle)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                // (the first time it is seen on the screen: once it is a knife its ring has it no more)
                if ((first + bullet + 1) % DanmakusuBalance.YUMEKO_KNIFE_EVERY != 0 || !(to.X >= f.stageMin.X && to.X <= f.stageMax.X && to.Y >= f.stageMin.Y && to.Y <= f.stageMax.Y))
                    return false;

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 toPlayer = f.characterPosition - to;
                Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                float gravity = DanmakusuBalance.YUMEKO_FALL_GRAVITY;
                float diagonal = Vector2.Distance(f.stageMin, f.stageMax);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = way * 0.001f,
                    Acc = way * gravity,
                    Face = way,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = BulletShape.Knife,
                    YellowHandle = true,
                    Until = t + Math.Sqrt(2.0 * (diagonal + stored.Radius * 4f) / gravity) + 100,
                });

                return true;
            }

            /// <summary>Notes a bullet that has just gone all the way out of the screen: where (on the edge), from which wall, how fast, how it looked.</summary>
            private void remember(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                int w = windowAt(t);

                if (w < 0 || left[w].Count >= DanmakusuBalance.YUMEKO_RETURN_MOST)
                    return;

                Vector2 delta = to - from;
                Vector2 low = f.stageMin, high = f.stageMax;
                float u = 1f;
                Vector2 inward = Vector2.UnitX;

                // (which wall it went out through: the one its way crosses first)
                if (delta.X > 1e-6f && to.X > high.X && (high.X - from.X) / delta.X < u)
                {
                    u = (high.X - from.X) / delta.X;
                    inward = -Vector2.UnitX;
                }
                else if (delta.X < -1e-6f && to.X < low.X && (low.X - from.X) / delta.X < u)
                {
                    u = (low.X - from.X) / delta.X;
                    inward = Vector2.UnitX;
                }

                if (delta.Y > 1e-6f && to.Y > high.Y && (high.Y - from.Y) / delta.Y < u)
                {
                    u = (high.Y - from.Y) / delta.Y;
                    inward = -Vector2.UnitY;
                }
                else if (delta.Y < -1e-6f && to.Y < low.Y && (low.Y - from.Y) / delta.Y < u)
                {
                    u = (low.Y - from.Y) / delta.Y;
                    inward = Vector2.UnitY;
                }

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                float speed = emitter.Ops == null ? Math.Max(delta.Length / (float)dt, 0.02f) : stored.Speed;

                left[w].Add(new Left
                {
                    Time = t,
                    Where = Vector2.Clamp(from + delta * Math.Clamp(u, 0f, 1f), low, high),
                    Inward = inward,
                    Speed = Math.Min(speed, stored.Speed * 3f),
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                });
            }

            /// <summary>When a window is over, what left in it comes back as knives, in four beats: each from where it left, straight across from its wall.</summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int w = 0; w < windowEnds.Length; w++)
                {
                    if (returned[w] || now < windowEnds[w])
                        continue;

                    returned[w] = true;

                    // (in the order they left, so the same bullets always come back the same)
                    var list = left[w];
                    list.Sort((a, b) => a.Time.CompareTo(b.Time));

                    double squeeze = DanmakusuBalance.YUMEKO_RETURN_BEATS / DanmakusuBalance.YUMEKO_RECORD_BEATS;

                    foreach (var one in list)
                    {
                        double back = windowEnds[w] + (one.Time - windowStarts[w]) * squeeze;
                        Vector2 velocity = one.Inward * one.Speed;

                        AddBullet(new StageBullet
                        {
                            T0 = back,
                            P0 = one.Where - one.Inward * one.Radius, // (just outside its wall: it slides in)
                            V = velocity,
                            Radius = one.Radius,
                            Hit = one.Hit,
                            Style = one.Style,
                            Shape = BulletShape.Knife,
                            YellowHandle = true,
                            Untraced = true, // (there can be hundreds: the ops of the others leave them be)
                            Until = back + f.stageLife(velocity),
                        });
                    }
                }
            }
        }
    }
}
