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
        // Eirin Yagokoro: every eight beats of her section every four bullets on the screen that are nearest each other are joined into a constellation by a thin silver beam, from one to the next nearest, that
        // hurts like a slider body while they stand still: only the bullets of the constellations stop (since 2026-11-14; everything stopped before), a little lighter, for four beats, then the beams are gone and
        // they fly on the way they went. Constellations do not overlap: one whose beams would cross or touch those of another is not made. For the four beats before, the beams are already there, see-through
        // and harmless, where the bullets will stop (worked out from their flight).
        // The bullets are looked at as they fly (Takes, which takes nothing): where each will be when they stop. They are joined a beat at a time - first what is on the screen when the preview begins, then what
        // was fired in each beat after it - each time in the order the rings come in the map, so the same bullets always make the same constellations (a replay and a seek show the same).
        // ======================================================================================================================================================

        private sealed class ConstellationAct : StageAct
        {
            private const float cell_size = 48f;
            private const int search_cells = 5; // (no farther than this many cells for a neighbour)

            private struct Star
            {
                public Vector2 At; // (where it stops)
                public double Fired;
                public int Ring; // (the ring's place in the map's list: the order they are joined in)
                public int Bullet;
            }

            private sealed class Mark
            {
                public double Time;
                public readonly HashSet<(BulletEmitter, int)> Known = new HashSet<(BulletEmitter, int)>();
                public readonly List<Star> Waiting = new List<Star>(); // (seen, not joined yet)
                public readonly List<Star> Left = new List<Star>(); // (joined to nothing yet: too far from the others)
                public readonly List<Vector2[]> Groups = new List<Vector2[]>();
                public readonly List<Star[]> Members = new List<Star[]>(); // (the bullets of every constellation, in the order of its beam)
                public readonly List<double> GroupFrom = new List<double>(); // (when its preview comes)
                public int Batches;
                public bool Sounded;
                public bool Stopped; // (its bullets have been stopped)
            }

            private readonly double beat;
            private readonly Mark[] marks;
            private readonly Dictionary<BulletEmitter, int> ringOrder = new Dictionary<BulletEmitter, int>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private double resetTime = double.NegativeInfinity;

            public ConstellationAct(BulletField f, BossPeriod period, List<double> times)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                marks = new Mark[times.Count];

                for (int i = 0; i < marks.Length; i++)
                    marks[i] = new Mark { Time = times[i] };

                Start = marks.Length > 0 ? marks[0].Time - previewMs : period.ShootStart;
                End = marks.Length > 0 ? marks[^1].Time + holdMs : period.End;
                AbsorbFrom = Start;
                AbsorbTo = marks.Length > 0 ? marks[^1].Time + 100 : Start - 1;

                for (int i = 0; i < f.emitters.Length; i++)
                    ringOrder[f.emitters[i]] = i;
            }

            private double previewMs => beat * DanmakusuBalance.EIRIN_PREVIEW_BEATS;

            private double holdMs => beat * DanmakusuBalance.EIRIN_HOLD_BEATS;

            /// <summary>She stays until the last constellation is gone.</summary>
            public override double MechanicsEnd => End;

            /// <summary>
            /// (the bullets are looked at only for a moment at every beat of the preview and when they stop: what is on the screen then is all that can be there when they stop, and whenever a bullet is first seen
            /// where it will stop is worked out from its flight. Looking all through the preview cost a millisecond and more a frame)
            /// </summary>
            private const double look_ms = 80;

            private int markAt(double t)
            {
                for (int i = 0; i < marks.Length; i++)
                {
                    double from = marks[i].Time - previewMs;

                    if (t < from || t > marks[i].Time + look_ms)
                        continue;

                    double intoBeat = (t - from) % beat;

                    if (intoBeat <= look_ms)
                        return i;
                }

                return -1;
            }

            public override bool Absorbs(double t) => markAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var mark in marks)
                {
                    double start = mark.Time - previewMs;

                    if (start > to || mark.Time + look_ms < from)
                        continue;

                    for (int k = 0; k <= (int)Math.Round(DanmakusuBalance.EIRIN_PREVIEW_BEATS); k++)
                    {
                        double at = start + k * beat;

                        if (at <= to && at + look_ms >= from)
                            return true;
                    }
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                // (every ring is looked at again from when it was fired: the constellations come out the same)
                foreach (var mark in marks)
                {
                    mark.Known.Clear();
                    mark.Waiting.Clear();
                    mark.Left.Clear();
                    mark.Groups.Clear();
                    mark.Members.Clear();
                    mark.GroupFrom.Clear();
                    mark.Batches = 0;
                    mark.Sounded = mark.Time <= time;
                    mark.Stopped = false;
                }

                ClearBullets();
                resetTime = time;
            }

            /// <summary>Takes nothing: notes where every bullet that flies in the preview will stop.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Homing || emitter.Sharp || emitter.Shape == BulletShape.Train)
                    return false;

                int index = markAt(t);

                if (index < 0)
                    return false;

                var mark = marks[index];

                if (emitter.Time >= mark.Time || !mark.Known.Add((emitter, bullet)))
                    return false;

                // (where its flight puts it when they stop: from then on it stands there)
                double own = emitter.WithoutFrozenTime(f.effects.Effective(emitter, mark.Time));
                emitter.Evaluate(bullet, own, out Vector2 at, out _, out bool alive);

                if (!alive || at.X < f.stageMin.X || at.X > f.stageMax.X || at.Y < f.stageMin.Y || at.Y > f.stageMax.Y)
                    return false;

                mark.Waiting.Add(new Star { At = at, Fired = emitter.Time, Ring = ringOrder.TryGetValue(emitter, out int order) ? order : int.MaxValue, Bullet = bullet });
                return false;
            }

            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var mark in marks)
                {
                    if (now < mark.Time - previewMs || now > mark.Time + holdMs)
                        continue;

                    // (a beat at a time: what was on the screen when the preview began, then what was fired in each beat after it)
                    while (mark.Batches <= (int)Math.Round(DanmakusuBalance.EIRIN_PREVIEW_BEATS))
                    {
                        double boundary = mark.Time - previewMs + mark.Batches * beat;

                        if (now < boundary)
                            break;

                        join(mark, boundary);
                        mark.Batches++;
                    }

                    if (!mark.Stopped && now >= mark.Time)
                    {
                        mark.Stopped = true;
                        stop(f, mark);
                    }

                    if (!mark.Sounded && now >= mark.Time)
                    {
                        mark.Sounded = true;

                        if (now - mark.Time < 250 && mark.Time > resetTime && mark.Groups.Count > 0)
                            f.playStageSound("touhou_strike") /* (Byakuren's sound, 2026-11-14: it was Cirno's freeze) */;
                    }
                }
            }

            /// <summary>
            /// The bullets of the constellations (and only those; 2026-11-14) are taken out of their rings and stand where they are, a little lighter, while the beams join them; when the beams go they fly on the
            /// way they were going, at the speed they had.
            /// </summary>
            private void stop(BulletField f, Mark mark)
            {
                double release = mark.Time + holdMs;

                foreach (var members in mark.Members)
                {
                    foreach (var star in members)
                    {
                        if (star.Ring < 0 || star.Ring >= f.emitters.Length)
                            continue;

                        var emitter = f.emitters[star.Ring];
                        ulong bit = 1UL << star.Bullet;

                        if (star.Bullet >= 64 || ((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                            continue;

                        // (how it was flying as it stopped: from where its flight puts it a moment before)
                        double own = emitter.WithoutFrozenTime(f.effects.Effective(emitter, mark.Time));
                        emitter.Evaluate(star.Bullet, own, out Vector2 at, out _, out bool alive);
                        emitter.Evaluate(star.Bullet, own - 10, out Vector2 before, out _, out bool aliveBefore);

                        if (!alive)
                            continue;

                        Vector2 velocity = aliveBefore ? (at - before) / 10f : Vector2.Zero;
                        emitter.AbsorbedMask |= bit;

                        var stored = f.makeStored(emitter, mark.Time);
                        var b = f.makeStageBullet(stored, emitter, star.Bullet, mark.Time, star.At, Vector2.Zero);
                        b.Legs = new[] { new StageLeg { T = release, P = star.At, V = velocity } };
                        b.Face = velocity.LengthSquared > 1e-10f ? Vector2.Normalize(velocity) : Vector2.UnitY;
                        b.Tint2At = mark.Time;
                        b.Tint2 = BulletRenderer.TINT_WHITE;
                        b.Tint2Amount = DanmakusuBalance.EIRIN_STOPPED_LIGHTER;
                        b.Tint2Over = 150;
                        b.Until = release + f.stageLife(velocity);
                        AddBullet(b);
                    }
                }
            }

            private static long cellOf(Vector2 at) => ((long)MathF.Floor(at.X / cell_size) << 32) ^ (uint)(int)MathF.Floor(at.Y / cell_size);

            /// <summary>Joins what has been seen and was fired by <paramref name="boundary"/> (and what was left over before) into constellations of four: each, in the order of the map, with its three nearest.</summary>
            private void join(Mark mark, double boundary)
            {
                var pool = new List<Star>(mark.Left);

                for (int i = mark.Waiting.Count - 1; i >= 0; i--)
                {
                    if (mark.Waiting[i].Fired <= boundary)
                    {
                        pool.Add(mark.Waiting[i]);
                        mark.Waiting.RemoveAt(i);
                    }
                }

                pool.Sort((a, b) => a.Ring != b.Ring ? a.Ring.CompareTo(b.Ring) : a.Bullet.CompareTo(b.Bullet));
                mark.Left.Clear();

                int group = DanmakusuBalance.EIRIN_GROUP;

                if (pool.Count < group)
                {
                    mark.Left.AddRange(pool);
                    return;
                }

                cells.Clear();

                for (int i = 0; i < pool.Count; i++)
                {
                    long key = cellOf(pool[i].At);

                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<int>();

                    list.Add(i);
                }

                bool[] used = new bool[pool.Count];
                var nearest = new List<(float Distance, int Index)>(16);

                for (int i = 0; i < pool.Count; i++)
                {
                    if (used[i])
                        continue;

                    Vector2 at = pool[i].At;
                    int cx = (int)MathF.Floor(at.X / cell_size), cy = (int)MathF.Floor(at.Y / cell_size);
                    nearest.Clear();

                    for (int ring = 0; ring <= search_cells; ring++)
                    {
                        for (int x = cx - ring; x <= cx + ring; x++)
                        {
                            for (int y = cy - ring; y <= cy + ring; y++)
                            {
                                if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || !cells.TryGetValue(((long)x << 32) ^ (uint)y, out var list))
                                    continue;

                                foreach (int j in list)
                                {
                                    if (j != i && !used[j])
                                        nearest.Add((Vector2.DistanceSquared(at, pool[j].At), j));
                                }
                            }
                        }

                        // (enough, and nothing in a further ring of cells can be nearer than the third one)
                        if (nearest.Count >= group - 1)
                        {
                            nearest.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));
                            float third = MathF.Sqrt(nearest[group - 2].Distance);

                            if (third <= ring * cell_size)
                                break;
                        }
                    }

                    if (nearest.Count < group - 1)
                        continue; // (nobody near enough: it may join what comes later)

                    nearest.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));

                    // (the beam goes from it through the others: of every order they can come in, the shortest whose beam does not cross itself (2026-11-14) and keeps clear of the constellations made before;
                    // when there is none, it is not made, and its bullets may join what comes later)
                    var rest = new List<Star>();

                    for (int k = 0; k < group - 1; k++)
                        rest.Add(pool[nearest[k].Index]);

                    Star[]? members = bestOrder(mark, pool[i], rest);

                    if (members == null)
                        continue;

                    var points = new Vector2[members.Length];

                    for (int k = 0; k < members.Length; k++)
                        points[k] = members[k].At;

                    used[i] = true;

                    for (int k = 0; k < group - 1; k++)
                        used[nearest[k].Index] = true;

                    mark.Groups.Add(points);
                    mark.Members.Add(members);
                    mark.GroupFrom.Add(boundary);
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    if (!used[i])
                        mark.Left.Add(pool[i]);
                }
            }

            /// <summary>
            /// The order of a constellation starting at <paramref name="first"/> through <paramref name="others"/>: the shortest beam that does not cross itself (its pieces that do not meet keep
            /// <see cref="DanmakusuBalance.EIRIN_APART"/> apart) and does not overlap a constellation made before; null when no order does.
            /// </summary>
            private static Star[]? bestOrder(Mark mark, Star first, List<Star> others)
            {
                Star[]? best = null;
                float shortest = float.PositiveInfinity;
                var order = new Star[others.Count + 1];
                var points = new Vector2[others.Count + 1];
                var used = new bool[others.Count];

                void tryFrom(int depth, float length)
                {
                    if (length >= shortest)
                        return;

                    if (depth == order.Length)
                    {
                        if (crossesItself(points) || overlaps(mark, points))
                            return;

                        shortest = length;
                        best = (Star[])order.Clone();
                        return;
                    }

                    for (int k = 0; k < others.Count; k++)
                    {
                        if (used[k])
                            continue;

                        used[k] = true;
                        order[depth] = others[k];
                        points[depth] = others[k].At;
                        tryFrom(depth + 1, length + Vector2.Distance(points[depth - 1], points[depth]));
                        used[k] = false;
                    }
                }

                order[0] = first;
                points[0] = first.At;
                tryFrom(1, 0f);
                return best;
            }

            /// <summary>Does a beam through <paramref name="points"/> cross itself (two pieces that do not share a point meet or come too close)?</summary>
            private static bool crossesItself(Vector2[] points)
            {
                for (int i = 0; i + 1 < points.Length; i++)
                {
                    for (int j = i + 2; j + 1 < points.Length; j++)
                    {
                        if (segmentsApart(points[i], points[i + 1], points[j], points[j + 1]) < DanmakusuBalance.EIRIN_APART)
                            return true;
                    }
                }

                return false;
            }

            /// <summary>Would a constellation through <paramref name="points"/> cross, or come within <see cref="DanmakusuBalance.EIRIN_APART"/> of, one already made for this mark?</summary>
            private static bool overlaps(Mark mark, Vector2[] points)
            {
                float apart = DanmakusuBalance.EIRIN_APART;

                foreach (var other in mark.Groups)
                {
                    for (int i = 0; i + 1 < points.Length; i++)
                    {
                        for (int j = 0; j + 1 < other.Length; j++)
                        {
                            if (segmentsApart(points[i], points[i + 1], other[j], other[j + 1]) < apart)
                                return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>How far apart two segments are (0 when they cross).</summary>
            private static float segmentsApart(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                static float cross(Vector2 o, Vector2 p, Vector2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);

                float d1 = cross(c, d, a), d2 = cross(c, d, b), d3 = cross(a, b, c), d4 = cross(a, b, d);

                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                    return 0f;

                return Math.Min(Math.Min(distanceToSegment(a, c, d), distanceToSegment(b, c, d)), Math.Min(distanceToSegment(c, a, b), distanceToSegment(d, a, b)));
            }

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.EIRIN_BEAM_WIDTH;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0 || t < Start || t > End)
                    return;

                // (silver beams with a dark blue edge: her hair and her dress)
                int style = f.comboStyle(1, true, Period.Rank);
                Color4 colour = f.palette[style % f.palette.Length];
                Color4 border = f.borderPalette[f.comboStyle(2, true, Period.Rank) % f.borderPalette.Length];
                float half = halfWidth(f);

                foreach (var mark in marks)
                {
                    if (t < mark.Time - previewMs || t >= mark.Time + holdMs)
                        continue;

                    bool solid = t >= mark.Time;
                    float fading = solid ? 1f - smoothStep((t - (mark.Time + holdMs - beat * 0.25)) / (beat * 0.25)) : 1f;

                    for (int g = 0; g < mark.Groups.Count; g++)
                    {
                        if (t < mark.GroupFrom[g])
                            continue;

                        float alpha = solid ? fading : DanmakusuBalance.EIRIN_PREVIEW_ALPHA * smoothStep((t - mark.GroupFrom[g]) / (beat * 0.5));

                        if (alpha < 0.01f)
                            continue;

                        line.Clear();
                        line.AddRange(mark.Groups[g]);
                        f.addPlainBeam(line, solid ? half : half * 0.7f, colour, border, ref beams, ref pointTotal);

                        if (alpha < 0.999f)
                            Array.Fill(f.laserFadePointBuffer, alpha, pointTotal - line.Count, line.Count);
                    }
                }
            }

            /// <summary>The beams hurt like a slider body while the bullets stand still (not as they fade at the end).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float hit = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var mark in marks)
                {
                    if (now < mark.Time || now >= mark.Time + holdMs - beat * 0.25)
                        continue;

                    foreach (var points in mark.Groups)
                    {
                        for (int i = 0; i + 1 < points.Length; i++)
                        {
                            if (distanceToSegment(player, points[i], points[i + 1]) <= hit)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
