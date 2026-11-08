using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Eika Ebisu: every four beats of her section every bullet on the screen gets as much speed again, at right angles to the line between it and her: clockwise for a bullet within one circle radius of her, anticlockwise
        // within two, clockwise within three, and so on. In her attacks bullets whose bodies touch (by their shapes, not by a circle round them) stick together, body against body and never on top of each other, up to eight in a clump, and
        // fly on as one, at the speed the law of impulse gives them: all their momentum, shared by all their mass (a bullet's mass is its area), but never slower than the fastest of them. She shoots ellipses in rings (the planner's). What is looked at is a grid of the bullets of this step, so that only neighbours are compared.
        // ======================================================================================================================================================

        private sealed class StackingAct : StageAct
        {
            private sealed class Clump
            {
                public readonly List<int> Members = new List<int>(); // (in Bullets)
                public Vector2 Velocity;
                public float Mass;
                public float Fastest; // (the clump is never slower than the fastest bullet that has joined it)

                /// <summary>How it really flies: faster than the law of impulse gives it (which is kept apart, so that it is not sped up again by every bullet that joins).</summary>
                public Vector2 Flight => Velocity * DanmakusuBalance.EIKA_CLUMP_SPEED;
            }

            private struct Entry
            {
                public Vector2 Position, Velocity;
                public float Radius; // (of its body)
                public BulletShape Shape;
                public Vector2 Face;
                public BulletEmitter? Emitter;
                public int Bullet;
                public Clump? Clump;
                public int Next;
            }

            private const double mark_ms = 60;

            private readonly double[] marks;
            private readonly List<(double Start, double End)> attacks;
            private readonly List<Clump> clumps = new List<Clump>();
            private readonly Dictionary<long, int> cells = new Dictionary<long, int>();
            private readonly List<Entry> entries = new List<Entry>();
            private double gridTime = double.NaN;

            public StackingAct(BossPeriod period, double[] marks, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.marks = marks;
                this.attacks = attacks;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            public override void Reset(BulletField f, double time)
            {
                clumps.Clear();
                cells.Clear();
                entries.Clear();
                gridTime = double.NaN;
                ClearBullets();
            }

            private int markAt(double t)
            {
                int low = 0, high = marks.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (marks[middle] + mark_ms <= t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low < marks.Length && marks[low] <= t ? low : -1;
            }

            /// <summary>The grid is made once a frame, and only the last step of a ring's scan looks at it: the rings are scanned one after the other, and all of them get there.</summary>
            public override void Update(BulletField f, double now)
            {
                if (!f.failed && inAttack(now))
                    startGrid(f, now, cellSize(f));
            }

            private float cellSize(BulletField f) => Math.Max(f.stageBaseRadius(Period) * DanmakusuBalance.EIKA_CELL, 4f);

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            public override bool Absorbs(double t) => inAttack(t) || markAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var (start, end) in attacks)
                {
                    if (start <= to && end > from)
                        return true;
                }

                foreach (double mark in marks)
                {
                    if (mark <= to && mark + mark_ms > from)
                        return true;
                }

                return false;
            }

            private Vector2 bossNow(BulletField f) => f.bossVisualOf(Period);

            /// <summary>The way a bullet at <paramref name="at"/> is pushed: at right angles to the line to her, clockwise in every odd circle radius of distance, anticlockwise in every even one.</summary>
            private Vector2 sideways(BulletField f, Vector2 at)
            {
                Vector2 away = at - bossNow(f);
                float distance = away.Length;

                if (distance < 1e-3f)
                    return Vector2.UnitX;

                Vector2 clockwise = new Vector2(-away.Y, away.X) / distance;
                return (int)(distance / Math.Max(f.circleRadius, 1f)) % 2 == 0 ? clockwise : -clockwise;
            }

            /// <summary>Every bullet on the screen is pushed at a mark, once.</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                int mark = markAt(t);

                if (f.failed || mark < 0 || !emitter.CanBeDeflected || emitter.Shape == BulletShape.Train)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                double marked = emitter.WithoutFrozenTime(f.effects.Effective(emitter, marks[mark]));

                if (emitter.Deflections != null && emitter.Deflections.TryGetValue(bullet, out var turned) && turned.Time >= marked)
                    return false;

                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);

                if (delta.LengthSquared < 1e-10f)
                    return false;

                Vector2 velocity = delta / (float)dt;
                Vector2 pushed = velocity + sideways(f, to) * velocity.Length;

                direction = Vector2.Normalize(pushed);
                speed = pushed.Length;
                return true;
            }

            private static long cellOf(Vector2 at, float size) => ((long)MathF.Floor(at.X / size) << 32) ^ (uint)(int)MathF.Floor(at.Y / size);

            private void put(in Entry entry, float size)
            {
                long key = cellOf(entry.Position, size);
                var linked = entry;
                linked.Next = cells.TryGetValue(key, out int head) ? head : -1;
                cells[key] = entries.Count;
                entries.Add(linked);
            }

            /// <summary>A new moment: the grid starts again, with the clumps in it.</summary>
            private void startGrid(BulletField f, double t, float size)
            {
                gridTime = t;
                cells.Clear();
                entries.Clear();

                for (int c = clumps.Count - 1; c >= 0; c--)
                {
                    var clump = clumps[c];
                    bool here = false;

                    foreach (int member in clump.Members)
                    {
                        var bullet = Bullets[member];

                        if (t > bullet.Until)
                            continue;

                        here = true;
                        put(new Entry { Position = stageBulletPosition(bullet, t), Velocity = clump.Velocity, Radius = bullet.Radius, Shape = bullet.Shape, Face = bullet.Face, Clump = clump }, size);
                    }

                    if (!here)
                        clumps.RemoveAt(c);
                }
            }

            /// <summary>Everything in a clump flies on from where it is with the velocity of the clump.</summary>
            private void flyOn(BulletField f, Clump clump, double t)
            {
                for (int i = 0; i < clump.Members.Count; i++)
                {
                    int old = clump.Members[i];
                    var bullet = Bullets[old];

                    if (f.stageConsumed.Contains(Id * 65536 + old))
                        continue;

                    Vector2 here = stageBulletPosition(bullet, t);
                    bullet.Until = t;
                    Bullets[old] = bullet;

                    bullet.T0 = t;
                    bullet.P0 = here;
                    bullet.V = clump.Flight;
                    bullet.Until = t + f.stageLife(clump.Flight);

                    int index = Bullets.Count;
                    AddBullet(bullet);

                    if (Bullets.Count > index)
                        clump.Members[i] = index;
                }
            }

            /// <summary>How far apart the middles of two bullets are when their bodies touch, along <paramref name="way"/> (from the first to the second).</summary>
            private static float contact(BulletShape shapeA, float radiusA, Vector2 faceA, BulletShape shapeB, float radiusB, Vector2 faceB, Vector2 way)
                => BulletCollision.Reach(shapeA, radiusA, faceA, way) + BulletCollision.Reach(shapeB, radiusB, faceB, -way);

            /// <summary>Where a bullet lies in a clump: its body against the body of the nearest of them, and on top of none of the others (it is pushed out of what it lies on).</summary>
            private Vector2 beside(Clump clump, Vector2 at, BulletShape shape, float radius, Vector2 face, double t)
            {
                if (clump.Members.Count == 0)
                    return at;

                int nearest = clump.Members[0];
                float closest = float.MaxValue;

                foreach (int member in clump.Members)
                {
                    float distance = Vector2.DistanceSquared(stageBulletPosition(Bullets[member], t), at);

                    if (distance < closest)
                    {
                        closest = distance;
                        nearest = member;
                    }
                }

                // (right on top of it: a way of its own)
                Vector2 wayFrom(int member, Vector2 there)
                {
                    Vector2 away = at - there;
                    float angle = member * 2.4f;

                    return away.LengthSquared > 1e-6f ? Vector2.Normalize(away) : new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                var first = Bullets[nearest];
                Vector2 middle = stageBulletPosition(first, t);
                Vector2 way = wayFrom(nearest, middle);

                at = middle + way * contact(first.Shape, first.Radius, first.Face, shape, radius, face, way);

                for (int round = 0; round < 8; round++)
                {
                    bool moved = false;

                    foreach (int member in clump.Members)
                    {
                        var other = Bullets[member];
                        Vector2 there = stageBulletPosition(other, t);
                        Vector2 from = wayFrom(member, there);
                        float apart = contact(other.Shape, other.Radius, other.Face, shape, radius, face, from);

                        if (Vector2.DistanceSquared(at, there) >= apart * apart - 1e-3f)
                            continue;

                        at = there + from * apart;
                        moved = true;
                    }

                    if (!moved)
                        break;
                }

                return at;
            }

            /// <summary>A bullet joins a clump, at <paramref name="t"/>: its momentum is shared by all the mass there is now, but the clump is never slower than the fastest bullet in it, and the bullet lies beside the others.</summary>
            private void join(BulletField f, Clump clump, BulletEmitter emitter, int bullet, Vector2 at, Vector2 velocity, double t)
            {
                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                float mass = (stored.Radius * big) * (stored.Radius * big);
                float all = clump.Mass + mass;

                clump.Velocity = (clump.Velocity * clump.Mass + velocity * mass) / Math.Max(all, 1e-3f);
                clump.Mass = all;
                clump.Fastest = Math.Max(clump.Fastest, velocity.Length);

                float pace = clump.Velocity.Length;

                if (pace < clump.Fastest)
                    clump.Velocity = pace > 1e-6f ? clump.Velocity * (clump.Fastest / pace) : velocity;

                Vector2 face = velocity.LengthSquared > 1e-10f ? Vector2.Normalize(velocity) : Vector2.UnitX;

                at = beside(clump, at, stored.Shape, stored.Radius * big, face, t);

                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = clump.Flight,
                    Face = face,
                    KeepFace = true, // (it keeps the way it faced, so that the bodies stay against each other whichever way the clump flies)
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = t + f.stageLife(clump.Flight),
                });

                if (Bullets.Count > index)
                    clump.Members.Add(index);
            }

            /// <summary>Do the bodies touch? (First the circles round them, which is quicker: most are not near.)</summary>
            private static bool touching(in Entry other, Vector2 at, BulletShape shape, float radius, Vector2 face)
            {
                Vector2 apart = at - other.Position;
                float distance = apart.Length;

                if (distance > other.Radius + radius)
                    return false;

                return distance < 1e-3f || distance <= contact(other.Shape, other.Radius, other.Face, shape, radius, face, apart / distance);
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t != gridTime || emitter.Shape == BulletShape.Train || emitter.Deflector || t - emitter.Time < DanmakusuBalance.EIKA_ARM_MS || emitter.IsStuck(bullet))
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                float size = cellSize(f);
                Vector2 velocity = (to - from) / (float)Math.Max(t - ScanPrevious, 1e-3);
                Vector2 face = velocity.LengthSquared > 1e-10f ? Vector2.Normalize(velocity) : Vector2.UnitX;
                float body = emitter.VisualRadius * emitter.BigScale(bullet);
                long x = (long)MathF.Floor(to.X / size), y = (long)MathF.Floor(to.Y / size);

                for (long a = x - 1; a <= x + 1; a++)
                {
                    for (long b = y - 1; b <= y + 1; b++)
                    {
                        if (!cells.TryGetValue((a << 32) ^ (uint)(int)b, out int at))
                            continue;

                        for (; at >= 0; at = entries[at].Next)
                        {
                            var other = entries[at];

                            if (ReferenceEquals(other.Emitter, emitter) || !touching(other, to, emitter.Shape, body, face))
                                continue;

                            var clump = other.Clump;

                            if (clump == null)
                            {
                                // (two bullets: the first one leaves its ring and starts the clump)
                                clump = new Clump();
                                clumps.Add(clump);
                                other.Emitter!.AbsorbedMask |= 1UL << other.Bullet;
                                join(f, clump, other.Emitter, other.Bullet, other.Position, other.Velocity, t);

                                var taken = other;
                                taken.Emitter = null;
                                taken.Clump = clump;
                                entries[at] = taken;
                            }
                            else if (clump.Members.Count >= DanmakusuBalance.EIKA_MAX_CLUMP)
                                continue;

                            join(f, clump, emitter, bullet, to, velocity, t);
                            flyOn(f, clump, t);
                            return true;
                        }
                    }
                }

                put(new Entry { Position = to, Velocity = velocity, Radius = body, Shape = emitter.Shape, Face = face, Emitter = emitter, Bullet = bullet }, size);
                return false;
            }
        }
    }
}
