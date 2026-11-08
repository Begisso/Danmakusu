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
        // Reimu: she stands in the middle. While she attacks every second bullet cannot leave the screen (the others leave it as ever): each stops when it first touches the border (or the bullets that already lie there) and lies down in the pile, which is
        // spread evenly all round the border and packed tight: every bullet lies body against body by their shapes, as Eika's bullets stick, with no shape over another and no room to spare. In a pause bullets fly as they always do and what lies there stays; every few beats she throws a ring of five yin-yang orbs, the first at the player (they hurt
        // like a slider and flies like Hecatia's Earth and Moon: it ricochets off the border and off the other orbs until the pause ends, with their hit sounds; it takes no part in what the bullets do). When the pause ends everything that lay there is gone, and the orbs stop.
        // In a break and in a spinner she attacks and pauses at the same time, all the time (what lies there goes when the section ends).
        // The pile depends on the order in which the bullets arrive, so it is built as the game runs (every bullet is caught once, in the frame it touches) and thrown away, bullets and all, when time goes back.
        // ======================================================================================================================================================

        private sealed class SealAct : StageAct
        {
            private struct Pile
            {
                public Vector2 Position;
                public float Radius;
                public BulletShape Shape;
                public Vector2 Face; // (the way its shape lies: the way it was flying, or the way a star had spun)
                public double Gone;

                /// <summary>When (game time) the bullet came to rest: a bullet that passes before that does not see it (it was still flying then).</summary>
                public double Since;

                public BulletEmitter Emitter;
                public int Bullet;
            }

            private sealed class Orb
            {
                public Vector2 Origin;
                public Vector2 Direction;

                /// <summary>When it was put into the simulation, and where it was at every step from then on (<see cref="sim_step"/> ms apart).</summary>
                public double Born;

                public readonly List<Vector2> Path = new List<Vector2>();

                /// <summary>When it is gone (the pause it was thrown in ends, or it has flown for long enough).</summary>
                public double Until;

                public Vector2 Pos;
                public Vector2 Velocity;
                public bool Fresh;
                public int Number;
            }

            /// <summary>A sound of an orb (Raiko's drum): it hits the border of the screen (kind 0 and 1, which are throttled apart so that two orbs hitting at once both sound) or another orb (2).</summary>
            private struct OrbSound
            {
                public double Time;
                public int Kind;
            }

            private const float cell_size = 48f;

            private readonly (double Start, double End, double Gone)[] windows;
            private readonly double[] throwTimes;
            private readonly double[] throwEnds;
            private readonly double[] warnTimes;
            private readonly bool[] made;
            private readonly Orb[] orbs;
            private readonly float orbSpeed;
            private readonly double lifeMs;

            private const double sim_step = DanmakusuBalance.STAGE_STEP_MS;

            private readonly List<Orb> flying = new List<Orb>();
            private readonly List<OrbSound> sounds = new List<OrbSound>();
            private readonly double[] soundAdded = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            private int soundNext;
            private int nextOrb;
            private double simTime = double.NaN;
            private double resetTime = double.NegativeInfinity;

            private readonly List<Pile> piles = new List<Pile>();
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private readonly List<(BulletEmitter Emitter, int Bullet)> lying = new List<(BulletEmitter, int)>();
            private float biggest;
            private Vector2 pileLow = new Vector2(float.MaxValue);
            private Vector2 pileHigh = new Vector2(float.MinValue);

            public SealAct(BulletField f, BossPeriod period)
            {
                Period = period;
                double beat = Math.Max(period.BeatLength, 1);
                var attacks = BossOps.Attacks(period);
                var catching = new List<(double Start, double End, double Gone)>();
                var throwing = new List<(double Start, double End)>();
                double first = double.PositiveInfinity;

                foreach (var (attackStart, attackEnd) in attacks)
                {
                    if (attackEnd > attackStart)
                        first = Math.Min(first, attackStart);
                }

                if (double.IsPositiveInfinity(first))
                    first = period.ShootStart;

                if (period.Kind == BossKind.Kiai)
                {
                    // attacks catch, pauses throw (what an attack caught goes where the pause after it ends)
                    for (int i = 0; i < attacks.Count; i++)
                    {
                        if (attacks[i].End <= attacks[i].Start)
                            continue;

                        double gone = i + 1 < attacks.Count ? attacks[i + 1].Start : period.End;
                        catching.Add((attacks[i].Start, attacks[i].End, Math.Max(gone, attacks[i].End)));
                    }

                    throwing.AddRange(BossOps.PausesWithTail(period));
                }
                else
                {
                    // a break or a spinner: both, all the time
                    catching.Add((first, period.End, period.End));
                    throwing.Add((first, period.End));
                }

                windows = catching.ToArray();
                Start = windows.Length > 0 ? windows[0].Start : first;
                End = period.End;

                if (windows.Length > 0)
                {
                    AbsorbFrom = windows[0].Start;
                    AbsorbTo = windows[^1].End;
                }

                var list = new List<(double At, double End)>();

                foreach (var (throwStart, throwEnd) in throwing)
                {
                    for (double at = throwStart; at < throwEnd - 1 && list.Count < 800; at += beat * DanmakusuBalance.REIMU_ORB_EVERY_BEATS)
                    {
                        for (int orb = 0; orb < DanmakusuBalance.REIMU_ORB_RING; orb++)
                            list.Add((at, throwEnd));
                    }
                }

                list.Sort((a, b) => a.At.CompareTo(b.At));
                throwTimes = list.ConvertAll(x => x.At).ToArray();
                throwEnds = list.ConvertAll(x => x.End).ToArray();
                made = new bool[throwTimes.Length];
                orbs = new Orb[throwTimes.Length];

                for (int k = 0; k < orbs.Length; k++)
                    orbs[k] = new Orb();

                lifeMs = beat * DanmakusuBalance.REIMU_ORB_LIFE_BEATS;

                // (in a kiai section the orbs come in the pauses, which have no warning of their own: the power-up leads into the first orb of every pause; a break and a spinner have the warning of the section)
                var warns = new List<double>();

                if (period.Kind == BossKind.Kiai)
                {
                    foreach (var pause in throwing)
                        warns.Add(pause.Start);
                }

                warnTimes = warns.ToArray();
                orbSpeed = Math.Max(stageBulletSpeed(period) * DanmakusuBalance.REIMU_ORB_SPEED_FACTOR, DanmakusuBalance.REIMU_ORB_MIN_SPEED);
            }

            private int windowAt(double t)
            {
                for (int i = 0; i < windows.Length; i++)
                {
                    if (t >= windows[i].Start && t <= windows[i].End)
                        return i;
                }

                return -1;
            }

            public override bool Absorbs(double t) => windowAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var window in windows)
                {
                    if (window.Start <= to && window.End >= from)
                        return true;
                }

                return false;
            }

            private static long cellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

            // ---- the pile ----

            public override bool Catches(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float fraction, out double gone)
            {
                position = to;
                direction = Vector2.UnitX;
                fraction = 1f;
                gone = double.PositiveInfinity;

                int window = windowAt(t);

                if (window < 0)
                    return false;

                // (what earlier attacks caught is all gone by now: the pile starts again. It used to keep them, gone as they were, and once it held as many as it can it caught nothing more for the rest of her section)
                if (piles.Count > 0 && t >= piles[^1].Gone)
                {
                    piles.Clear();
                    cells.Clear();
                    biggest = 0f;
                    pileLow = new Vector2(float.MaxValue);
                    pileHigh = new Vector2(float.MinValue);
                }

                // (a bullet that the ops of a boss act on is caught too: lying still, it is out of their reach)
                if (piles.Count >= DanmakusuBalance.REIMU_PILE_MAX || emitter.Deflector || emitter.Retired || emitter.IsStuck(bullet))
                    return false;

                // (only every second bullet piles up: the others leave the screen as ever)
                if ((Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.REIMU_PILE_EVERY != 0)
                    return false;

                float radius = Math.Max(emitter.VisualRadius * emitter.BigScale(bullet), 1f);
                Vector2 move = to - from;
                Vector2 face = move.LengthSquared > 1e-9f ? Vector2.Normalize(move) : Vector2.UnitX;
                BulletShape shape = emitter.Shape;

                // (the way its shape lies: the way it flew, but a star lies turned as far as it had spun when it stopped)
                Vector2 lies = shape == BulletShape.Star ? emitter.Orientation(bullet, emitter.WithoutFrozenTime(f.effects.Effective(emitter, t))) : face;

                // (how far its body reaches towards the walls, by its shape as it lies: it lies with its body against the border)
                Vector2 wallReach = new Vector2(BulletCollision.Reach(shape, radius, lies, Vector2.UnitX), BulletCollision.Reach(shape, radius, lies, Vector2.UnitY));
                Vector2 low = f.stageMin + wallReach;
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - wallReach);
                float best = 2f;
                bool inside = from.X >= low.X && from.X <= high.X && from.Y >= low.Y && from.Y <= high.Y;

                if (inside)
                {
                    // the border: the first moment the bullet's edge touches it
                    if (to.X < low.X && move.X < 0f)
                        best = Math.Min(best, (low.X - from.X) / move.X);
                    else if (to.X > high.X && move.X > 0f)
                        best = Math.Min(best, (high.X - from.X) / move.X);

                    if (to.Y < low.Y && move.Y < 0f)
                        best = Math.Min(best, (low.Y - from.Y) / move.Y);
                    else if (to.Y > high.Y && move.Y > 0f)
                        best = Math.Min(best, (high.Y - from.Y) / move.Y);
                }
                else if (from.X > f.stageMin.X && from.X < f.stageMax.X && from.Y > f.stageMin.Y && from.Y < f.stageMax.Y)
                {
                    best = 0f; // (on the screen but already at the border: it stays right there)
                }
                else
                {
                    return false; // (off the screen already: it flies on)
                }

                // (the step the bullet flew: from game time `before` to `t`)
                double before = Math.Min(ScanPrevious, t);
                double span = t - before;

                // the bullets that already lie there
                if (best > 0f && piles.Count > 0)
                {
                    float reachMost = radius + biggest;
                    float minX = Math.Min(from.X, to.X) - reachMost, maxX = Math.Max(from.X, to.X) + reachMost;
                    float minY = Math.Min(from.Y, to.Y) - reachMost, maxY = Math.Max(from.Y, to.Y) + reachMost;

                    if (maxX >= pileLow.X && minX <= pileHigh.X && maxY >= pileLow.Y && minY <= pileHigh.Y)
                    {
                        int cellX0 = (int)MathF.Floor(minX / cell_size), cellX1 = (int)MathF.Floor(maxX / cell_size);
                        int cellY0 = (int)MathF.Floor(minY / cell_size), cellY1 = (int)MathF.Floor(maxY / cell_size);
                        float a = move.LengthSquared;

                        for (int cx = cellX0; cx <= cellX1 && best > 0f; cx++)
                        {
                            for (int cy = cellY0; cy <= cellY1 && best > 0f; cy++)
                            {
                                if (!cells.TryGetValue(cellKey(cx, cy), out var inCell))
                                    continue;

                                foreach (int index in inCell)
                                {
                                    var pile = piles[index];

                                    if (t >= pile.Gone)
                                        continue;

                                    // (one that has been hit or taken away is not there any more)
                                    if (((pile.Emitter.ConsumedMask | pile.Emitter.AbsorbedMask | pile.Emitter.HiddenMask) & (1UL << pile.Bullet)) != 0)
                                        continue;

                                    float reach = radius + pile.Radius;
                                    Vector2 s = from - pile.Position;
                                    float c = s.LengthSquared - reach * reach;

                                    if (c <= 0f)
                                    {
                                        // already touching it: stops where it is (unless it is deep inside, which it only is if it was made there: that one flies on)
                                        if (s.LengthSquared >= 0.7225f * reach * reach && before >= pile.Since - 1e-6)
                                        {
                                            best = 0f;
                                            break;
                                        }

                                        continue;
                                    }

                                    if (a < 1e-9f)
                                        continue;

                                    float half = Vector2.Dot(s, move);
                                    float disc = half * half - a * c;

                                    if (disc < 0f)
                                        continue;

                                    float u = (-half - MathF.Sqrt(disc)) / a;

                                    // (it only stops against a bullet that was lying there by the time it got there, not one that is still on its way)
                                    if (u >= 0f && u <= 1f && u < best && before + span * u >= pile.Since - 1e-6)
                                        best = u;
                                }
                            }
                        }
                    }
                }

                if (best > 1f)
                    return false;

                fraction = best;
                position = best <= 0f && !inside ? Vector2.Clamp(from, low, high) : from + move * best;
                direction = face;
                gone = windows[window].Gone;
                position = evenSpot(f, position, radius, shape, lies, low, high, t);

                int number = piles.Count;
                piles.Add(new Pile { Position = position, Radius = radius, Shape = shape, Face = lies, Gone = gone, Since = before + span * fraction, Emitter = emitter, Bullet = bullet });

                long key = cellKey((int)MathF.Floor(position.X / cell_size), (int)MathF.Floor(position.Y / cell_size));

                if (!cells.TryGetValue(key, out var members))
                {
                    members = new List<int>();
                    cells[key] = members;
                }

                members.Add(number);
                biggest = Math.Max(biggest, radius);
                pileLow = Vector2.ComponentMin(pileLow, position - new Vector2(radius));
                pileHigh = Vector2.ComponentMax(pileHigh, position + new Vector2(radius));
                lying.Add((emitter, bullet));
                return true;
            }

            /// <summary>
            /// Where a caught bullet lies so that the pile is spread evenly all round the border: the places along the border follow one another by the golden ratio (so however many there are, they are evenly spread),
            /// and from there the bullet goes inwards only as far as it must to lie against what is there, body against body by their shapes (as Eika's bullets stick: no shape over another, and no room to spare),
            /// so the pile grows evenly thick all round and as dense as the bullets are. <paramref name="hit"/> is where it touched, if there is nowhere to go.
            /// </summary>
            private Vector2 evenSpot(BulletField f, Vector2 hit, float radius, BulletShape shape, Vector2 face, Vector2 low, Vector2 high, double t)
            {
                float width = Math.Max(high.X - low.X, 0f), height = Math.Max(high.Y - low.Y, 0f);
                float perimeter = 2f * (width + height);

                if (perimeter < 1f)
                    return hit;

                float along = (0.5f + piles.Count * 0.6180340f) % 1f * perimeter;
                Vector2 edge, inward;

                if (along < width)
                {
                    edge = new Vector2(low.X + along, low.Y);
                    inward = new Vector2(0f, 1f);
                }
                else if (along < width + height)
                {
                    edge = new Vector2(high.X, low.Y + along - width);
                    inward = new Vector2(-1f, 0f);
                }
                else if (along < 2f * width + height)
                {
                    edge = new Vector2(high.X - (along - width - height), high.Y);
                    inward = new Vector2(0f, -1f);
                }
                else
                {
                    edge = new Vector2(low.X, high.Y - (along - 2f * width - height));
                    inward = new Vector2(1f, 0f);
                }

                float step = radius * 0.25f;

                // (never on top of the player: a bullet that lands where the player stands goes further in)
                float keepAway = radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.REIMU_PILE_PLAYER_GAP;

                bool free(float depth)
                {
                    Vector2 place = edge + inward * depth;
                    return Vector2.DistanceSquared(place, f.characterPosition) >= keepAway * keepAway && roomAt(place, radius, shape, face, t);
                }

                for (int n = 0; n < 240; n++)
                {
                    if (!free(step * n))
                        continue;

                    // (no further in than it must: the least depth with room, between the last step that had none and this one)
                    float blocked = step * Math.Max(n - 1, 0), open = step * n;

                    for (int k = 0; n > 0 && k < 6; k++)
                    {
                        float middle = (blocked + open) / 2f;

                        if (free(middle))
                            open = middle;
                        else
                            blocked = middle;
                    }

                    return edge + inward * open;
                }

                return hit;
            }

            /// <summary>Is there room for a bullet of <paramref name="radius"/> and <paramref name="shape"/> at <paramref name="place"/> between the bullets that lie there (no shape over another, by their shapes as they lie)?</summary>
            private bool roomAt(Vector2 place, float radius, BulletShape shape, Vector2 face, double t)
            {
                if (piles.Count == 0)
                    return true;

                float reachMost = radius + biggest;
                int cellX0 = (int)MathF.Floor((place.X - reachMost) / cell_size), cellX1 = (int)MathF.Floor((place.X + reachMost) / cell_size);
                int cellY0 = (int)MathF.Floor((place.Y - reachMost) / cell_size), cellY1 = (int)MathF.Floor((place.Y + reachMost) / cell_size);

                for (int cx = cellX0; cx <= cellX1; cx++)
                {
                    for (int cy = cellY0; cy <= cellY1; cy++)
                    {
                        if (!cells.TryGetValue(cellKey(cx, cy), out var inCell))
                            continue;

                        foreach (int index in inCell)
                        {
                            var pile = piles[index];

                            if (t >= pile.Gone || ((pile.Emitter.ConsumedMask | pile.Emitter.AbsorbedMask | pile.Emitter.HiddenMask) & (1UL << pile.Bullet)) != 0)
                                continue;

                            Vector2 apart = place - pile.Position;
                            float distance = apart.Length;

                            if (distance > radius + pile.Radius)
                                continue;

                            if (distance < 1e-3f)
                                return false;

                            Vector2 way = apart / distance;

                            if (distance < BulletCollision.Reach(pile.Shape, pile.Radius, pile.Face, way) + BulletCollision.Reach(shape, radius, face, -way))
                                return false;
                        }
                    }
                }

                return true;
            }

            public override void Reset(BulletField f, double time)
            {
                // every bullet that lay there is let go again (the field looks at all the rings again from when they fired, and the pile is built anew)
                foreach (var (emitter, bullet) in lying)
                    emitter.Undeflect(bullet);

                lying.Clear();
                piles.Clear();
                cells.Clear();
                biggest = 0f;
                pileLow = new Vector2(float.MaxValue);
                pileHigh = new Vector2(float.MinValue);

                // the orbs that were thrown before this moment are flown again from the start (their flight is the same every time); the others have not been thrown yet
                for (int k = 0; k < throwTimes.Length; k++)
                {
                    if (throwTimes[k] > time)
                        made[k] = false;

                    orbs[k].Path.Clear();
                }

                flying.Clear();
                sounds.Clear();
                soundNext = 0;
                nextOrb = 0;
                simTime = double.NaN;
                resetTime = time;
                soundAdded[0] = soundAdded[1] = soundAdded[2] = double.NegativeInfinity;
            }

            public override void Warnings(BulletField f, double now, List<double> ends)
            {
                double length = DanmakusuBalance.POWER_UP_LENGTH_MS;

                foreach (double at in warnTimes)
                {
                    if (at - length <= now && at > now - 2000)
                        ends.Add(at);
                }
            }

            // ---- the orbs ----
            // The orbs fly like Hecatia's Earth and Moon (without the chains, and without the bullets they throw): they ricochet off the border of the screen and off each other (an elastic collision)
            // until the pause they were thrown in ends. The flight of all orbs is simulated in steps of a few ms (the orbs hit each other, so it can not be a formula) and remembered; every hit has a sound
            // (Raiko's drum for every hit).

            private static float orbRadius(BulletField f) => DanmakusuBalance.REIMU_ORB_RADIUS * f.circleRadius;

            private void addSound(double time, int kind)
            {
                if (time - soundAdded[kind] < 80)
                    return;

                soundAdded[kind] = time;
                sounds.Add(new OrbSound { Time = time, Kind = kind });
            }

            private void bounce(BulletField f, Orb orb, Vector2 low, Vector2 high)
            {
                if (high.X <= low.X || high.Y <= low.Y)
                    return; // (the screen is smaller than the orb)

                bool hit = false;

                if (orb.Pos.X < low.X && orb.Velocity.X < 0f)
                {
                    orb.Pos.X = low.X + (low.X - orb.Pos.X);
                    orb.Velocity.X = -orb.Velocity.X;
                    hit = true;
                }
                else if (orb.Pos.X > high.X && orb.Velocity.X > 0f)
                {
                    orb.Pos.X = high.X - (orb.Pos.X - high.X);
                    orb.Velocity.X = -orb.Velocity.X;
                    hit = true;
                }

                if (orb.Pos.Y < low.Y && orb.Velocity.Y < 0f)
                {
                    orb.Pos.Y = low.Y + (low.Y - orb.Pos.Y);
                    orb.Velocity.Y = -orb.Velocity.Y;
                    hit = true;
                }
                else if (orb.Pos.Y > high.Y && orb.Velocity.Y > 0f)
                {
                    orb.Pos.Y = high.Y - (orb.Pos.Y - high.Y);
                    orb.Velocity.Y = -orb.Velocity.Y;
                    hit = true;
                }

                if (hit)
                {
                    addSound(simTime, orb.Number % 2);
                    f.addFlash(simTime, Vector2.Clamp(orb.Pos, low, high), orbRadius(f) * 3f); // (a ricochet: it flashes where it hit)
                }
            }

            /// <summary>Flies the orbs on in steps of a few ms up to <paramref name="to"/>.</summary>
            private void advance(BulletField f, double to)
            {
                if (double.IsNaN(simTime))
                {
                    if (nextOrb >= throwTimes.Length || !made[nextOrb])
                        return;

                    simTime = throwTimes[nextOrb] - sim_step;
                }

                float radius = orbRadius(f);
                Vector2 low = f.stageMin + new Vector2(radius);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(radius));
                float dt = (float)sim_step;
                int guard = 0;

                while (simTime + sim_step <= to && guard++ < 6000)
                {
                    if (flying.Count == 0)
                    {
                        // nothing flies: on to when the next orb is thrown (or to now)
                        if (nextOrb < throwTimes.Length && made[nextOrb])
                        {
                            if (throwTimes[nextOrb] - sim_step > simTime)
                            {
                                simTime = throwTimes[nextOrb] - sim_step;
                                continue;
                            }
                        }
                        else
                        {
                            simTime = Math.Max(simTime, to);
                            break;
                        }
                    }

                    simTime += sim_step;

                    // thrown: from where Reimu was, at where the player was
                    while (nextOrb < throwTimes.Length && made[nextOrb] && throwTimes[nextOrb] <= simTime)
                    {
                        var orb = orbs[nextOrb];
                        orb.Number = nextOrb;
                        orb.Born = simTime;
                        orb.Pos = Vector2.Clamp(orb.Origin, low, high);
                        orb.Velocity = orb.Direction * orbSpeed;
                        orb.Fresh = true;
                        orb.Path.Clear();
                        orb.Path.Add(orb.Pos);
                        flying.Add(orb);
                        nextOrb++;
                    }

                    flying.RemoveAll(o => simTime >= o.Until);

                    foreach (var orb in flying)
                    {
                        if (orb.Fresh)
                            continue;

                        orb.Pos += orb.Velocity * dt;
                        bounce(f, orb, low, high);
                    }

                    // two orbs that touch ricochet off each other (the same mass: they swap the speed they have along the line between them)
                    for (int i = 0; i < flying.Count; i++)
                    {
                        for (int j = i + 1; j < flying.Count; j++)
                        {
                            var a = flying[i];
                            var b = flying[j];
                            Vector2 d = b.Pos - a.Pos;
                            float distance = d.Length;

                            if (distance < 1e-3f || distance >= 2f * radius)
                                continue;

                            Vector2 n = d / distance;
                            float closing = Vector2.Dot(a.Velocity - b.Velocity, n);

                            if (closing <= 0f)
                                continue;

                            a.Velocity -= n * closing;
                            b.Velocity += n * closing;

                            float overlap = 2f * radius - distance;
                            a.Pos -= n * (overlap / 2f);
                            b.Pos += n * (overlap / 2f);
                            addSound(simTime, 2);
                            f.addFlash(simTime, (a.Pos + b.Pos) / 2f, radius * 3f); // (they ricochet off each other: a flash where they touch)
                        }
                    }

                    foreach (var orb in flying)
                    {
                        if (orb.Fresh)
                            orb.Fresh = false; // (its first place is there already)
                        else
                            orb.Path.Add(orb.Pos);
                    }
                }
            }

            private static Vector2 positionOf(Orb orb, double t)
            {
                if (orb.Path.Count == 0)
                    return orb.Origin;

                double x = (t - orb.Born) / sim_step;

                if (x <= 0)
                    return orb.Path[0];

                int i = (int)x;

                if (i >= orb.Path.Count - 1)
                    return orb.Path[orb.Path.Count - 1];

                return Vector2.Lerp(orb.Path[i], orb.Path[i + 1], (float)(x - i));
            }

            /// <summary>How much of an orb there is (0..1): it grows out of Reimu when it is thrown and shrinks away when its pause is over.</summary>
            private float presence(int k, double t)
                => Math.Min(smoothStep((t - throwTimes[k]) / DanmakusuBalance.REIMU_ORB_POP_MS), smoothStep((orbs[k].Until - t) / DanmakusuBalance.REIMU_ORB_POP_MS));

            private void playSounds(BulletField f, double now)
            {
                while (soundNext < sounds.Count && sounds[soundNext].Time <= now)
                {
                    var sound = sounds[soundNext++];

                    if (sound.Time <= resetTime || now - sound.Time >= 250)
                        continue;

                    f.playStageSound("taiko_slam");
                }
            }

            public override void Update(BulletField f, double now)
            {
                for (int k = 0; k < throwTimes.Length; k++)
                {
                    if (now < throwTimes[k])
                        break;

                    if (made[k])
                        continue;

                    made[k] = true;

                    Vector2 origin = f.bossVisualOf(Period);
                    Vector2 toPlayer = f.characterPosition - origin;
                    Vector2 aim = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : new Vector2(0f, 1f);

                    orbs[k].Origin = origin;
                    orbs[k].Direction = turned(aim, k % DanmakusuBalance.REIMU_ORB_RING * MathF.Tau / DanmakusuBalance.REIMU_ORB_RING);
                    orbs[k].Until = Math.Min(throwEnds[k], throwTimes[k] + lifeMs);

                    if (now - throwTimes[k] < 250 && throwTimes[k] > resetTime && k % DanmakusuBalance.REIMU_ORB_RING == 0)
                        f.playStageSound("reimu_orb_throw");
                }

                advance(f, now);
                playSounds(f, now);
            }

            public override bool Hurts(BulletField f, double now)
            {
                float hit = DanmakusuBalance.REIMU_ORB_HIT * orbRadius(f);

                for (int k = 0; k < throwTimes.Length; k++)
                {
                    if (now < throwTimes[k])
                        break;

                    if (!made[k] || now > orbs[k].Until)
                        continue;

                    float grown = presence(k, now);

                    if (grown < 0.5f)
                        continue;

                    if (Vector2.Distance(f.characterPosition, positionOf(orbs[k], now)) <= hit * grown + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                float radius = orbRadius(f);

                for (int k = 0; k < throwTimes.Length; k++)
                {
                    if (t < throwTimes[k])
                        break;

                    if (!made[k] || t > orbs[k].Until)
                        continue;

                    float grown = presence(k, t);
                    Vector2 at = positionOf(orbs[k], t);

                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(radius * 5f * grown), 0f, 0.35f * grown, new Color4(1f, 0.45f, 0.45f, 1f), true);
                    f.stageFront.Add(StageTexture.YinYang, at, new Vector2(radius * 2f * grown), (float)(t * DanmakusuBalance.REIMU_ORB_SPIN), 1f, Color4.White);
                }
            }
        }
    }
}
