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
        // Misumaru Tamatsukuri: all through her section (in a spinner too) she throws a yin-yang orb at the player every eight beats, and every thirtieth bullet (anyone's, counted ring by ring, the first time it is
        // seen) turns into one where it is and flies on its way. Her orbs are Reimu's, one and a half times as big: they fly as fast as Reimu's, ricochet off the edges of the screen and off each other (an elastic
        // collision, as Hecatia's worlds), hurt like a slider body and shrink away sixteen beats after they came; at most twelve fly at once (the oldest shrinks away for a new one). Their flight is worked out step by
        // step from when each came and kept, so a seek shows the same.
        // In her attacks bullets whose bodies touch ricochet off each other, once each: each one that was flying into the other is turned back off it as off a wall, at the speed it had (the one seen first a frame
        // later). What is looked at is a grid of the bullets of this frame, as Eika's clumps are. (Out of her attacks the bullets are looked at for a moment every beat, for the ones that turn into orbs.)
        // ======================================================================================================================================================

        private sealed class OrbsAct : StageAct
        {
            private sealed class Orb
            {
                public double Born;
                public Vector2 Origin;
                public Vector2 Direction;
                public double Until;
                public bool Thrown; // (by her: the others were bullets)
                public readonly List<Vector2> Path = new List<Vector2>();
                public Vector2 Pos, Velocity;
                public bool Flying, Fresh;
                public int Number;
            }

            private struct Entry
            {
                public Vector2 Position, Velocity;
                public float Radius;
                public BulletEmitter Emitter;
                public int Bullet;
                public int Next;
            }

            private const double sim_step = DanmakusuBalance.STAGE_STEP_MS;

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly double[] throws;
            private readonly bool[] thrown;
            private readonly Orb[] throwOrbs;
            private readonly List<Orb> orbs = new List<Orb>(); // (every orb that has come, in the order they came)
            private readonly List<Orb> waiting = new List<Orb>(); // (made, not yet in the simulation)
            private readonly List<Orb> flying = new List<Orb>();
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;
            private double simTime = double.NaN;
            private double resetTime = double.NegativeInfinity;
            private readonly List<double> sounds = new List<double>();
            private int soundNext;
            private double lastSound = double.NegativeInfinity;

            // (the ricochets of the bullets)
            private readonly Dictionary<long, int> cells = new Dictionary<long, int>();
            private readonly List<Entry> entries = new List<Entry>();
            private readonly HashSet<(BulletEmitter, int)> bounced = new HashSet<(BulletEmitter, int)>();
            private readonly Dictionary<(BulletEmitter, int), (Vector2 Way, double Time)> owed = new Dictionary<(BulletEmitter, int), (Vector2, double)>();
            private double gridTime = double.NaN;

            public OrbsAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.MISUMARU_THROW_EVERY_BEATS)
                    times.Add(at);

                throws = times.ToArray();
                thrown = new bool[throws.Length];
                throwOrbs = new Orb[throws.Length];
            }

            private double lifeMs => beat * DanmakusuBalance.REIMU_ORB_LIFE_BEATS;

            private float orbRadius(BulletField f) => DanmakusuBalance.REIMU_ORB_RADIUS * DanmakusuBalance.MISUMARU_ORB_SIZE * f.circleRadius;

            private float orbSpeed => Math.Max(stageBulletSpeed(Period) * DanmakusuBalance.REIMU_ORB_SPEED_FACTOR, DanmakusuBalance.REIMU_ORB_MIN_SPEED);

            /// <summary>Her last orbs fly on after her section: she stays until they are gone.</summary>
            public override double MechanicsEnd => Period.End + lifeMs;

            public override bool PushIsRicochet => true;

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            /// <summary>
            /// (out of her attacks the bullets are only looked at for a moment every beat: a bullet that is to be an orb turns within a beat of being fired. Looking at every bullet at every step cost a good deal
            /// with a boss whose ops act on every ring)
            /// </summary>
            private const double look_ms = 60;

            private bool looks(double t) => t >= Period.ShootStart && t < Period.End && (inAttack(t) || (t - Period.ShootStart) % beat <= look_ms);

            public override bool Absorbs(double t) => looks(t);

            public override bool AbsorbsBetween(double from, double to)
            {
                if (to < Period.ShootStart || from >= Period.End)
                    return false;

                foreach (var (start, end) in attacks)
                {
                    if (start <= to && end > from)
                        return true;
                }

                // (a look every beat)
                double into = (Math.Max(from, Period.ShootStart) - Period.ShootStart) % beat;
                return into <= look_ms || to - from >= beat - into;
            }

            public override void Reset(BulletField f, double time)
            {
                // (the orbs she threw before this moment are flown again from the start, from where she was; the bullets are looked at again from when they were fired, and turn into the same orbs)
                orbs.Clear();
                waiting.Clear();
                flying.Clear();
                bases.Clear();
                counted = 0;
                simTime = double.NaN;
                sounds.Clear();
                soundNext = 0;
                resetTime = time;

                for (int k = 0; k < throws.Length; k++)
                {
                    if (throws[k] > time)
                        thrown[k] = false;
                    else if (thrown[k])
                        waiting.Add(fresh(throwOrbs[k]));
                }

                cells.Clear();
                entries.Clear();
                bounced.Clear();
                owed.Clear();
                gridTime = double.NaN;
            }

            private static Orb fresh(Orb orb) => new Orb { Born = orb.Born, Origin = orb.Origin, Direction = orb.Direction, Thrown = orb.Thrown };

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                for (int k = 0; k < throws.Length && throws[k] <= now; k++)
                {
                    if (thrown[k])
                        continue;

                    thrown[k] = true;

                    Vector2 origin = f.bossVisualOf(Period);
                    Vector2 toPlayer = f.characterPosition - origin;
                    throwOrbs[k] = new Orb { Born = throws[k], Origin = origin, Direction = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY, Thrown = true };
                    waiting.Add(fresh(throwOrbs[k]));

                    if (now - throws[k] < 250 && throws[k] > resetTime)
                        f.playStageSound("reimu_orb_throw");
                }

                // (a new grid every frame of an attack)
                if (inAttack(now))
                {
                    gridTime = now;
                    cells.Clear();
                    entries.Clear();
                }
            }

            // ---- every thirtieth bullet turns into an orb; in her attacks bullets ricochet off each other ----

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Period.ShootStart || t >= Period.End || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.MISUMARU_ORB_EVERY != 0 || to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                Vector2 way = to - from;
                waiting.Add(new Orb { Born = t, Origin = to, Direction = way.LengthSquared > 1e-8f ? Vector2.Normalize(way) : Vector2.UnitY });
                return true;
            }

            private float cellSize(BulletField f) => Math.Max(f.stageBaseRadius(Period) * DanmakusuBalance.MISUMARU_CELL, 4f);

            private static long cellOf(Vector2 at, float size) => ((long)MathF.Floor(at.X / size) << 32) ^ (uint)(int)MathF.Floor(at.Y / size);

            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                if (f.failed || t != gridTime || !emitter.CanBeDeflected || emitter.Shape == BulletShape.Train || emitter.Sharp || t - emitter.Time < DanmakusuBalance.MISUMARU_ARM_MS || emitter.IsStuck(bullet))
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                var key = (emitter, bullet);
                Vector2 velocity = (to - from) / (float)Math.Max(t - ScanPrevious, 1e-3);
                float pace = velocity.Length;

                if (pace < 1e-4f)
                    return false;

                // (it was hit last frame by one that saw it first: it turns now)
                if (owed.Remove(key, out var owe))
                {
                    if (t - owe.Time > 250)
                        return false;

                    direction = owe.Way;
                    speed = pace;
                    return true;
                }

                float size = cellSize(f);
                float body = emitter.VisualRadius * emitter.BigScale(bullet) * 0.9f;
                bool mayTurn = !bounced.Contains(key);

                if (mayTurn)
                {
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

                                if (ReferenceEquals(other.Emitter, emitter))
                                    continue;

                                Vector2 apart = to - other.Position;
                                float distance = apart.Length;

                                if (distance < 1e-3f || distance > body + other.Radius)
                                    continue;

                                Vector2 n = apart / distance; // (from the other to this one)

                                if (Vector2.Dot(velocity - other.Velocity, n) >= 0f)
                                    continue; // (they are already going apart)

                                var otherKey = (other.Emitter, other.Bullet);
                                bool turned = false;

                                // (each that flies into the other is turned back off it)
                                if (Vector2.Dot(velocity, n) < 0f)
                                {
                                    Vector2 off = velocity - 2f * Vector2.Dot(velocity, n) * n;
                                    direction = Vector2.Normalize(off);
                                    speed = pace;
                                    turned = true;
                                }

                                if (!bounced.Contains(otherKey) && Vector2.Dot(other.Velocity, n) > 0f)
                                {
                                    Vector2 off = other.Velocity - 2f * Vector2.Dot(other.Velocity, n) * n;

                                    if (off.LengthSquared > 1e-10f)
                                    {
                                        owed[otherKey] = (Vector2.Normalize(off), t);
                                        bounced.Add(otherKey);
                                    }
                                }

                                f.addFlash(t, (to + other.Position) * 0.5f, (body + other.Radius) * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f);

                                if (turned)
                                {
                                    bounced.Add(key);
                                    return true;
                                }

                                break;
                            }
                        }
                    }
                }

                var entry = new Entry { Position = to, Velocity = velocity, Radius = body, Emitter = emitter, Bullet = bullet };
                long cell = cellOf(to, size);
                entry.Next = cells.TryGetValue(cell, out int head) ? head : -1;
                cells[cell] = entries.Count;
                entries.Add(entry);
                return false;
            }

            // ---- the orbs ----

            private void bounceOffWalls(BulletField f, Orb orb, Vector2 low, Vector2 high)
            {
                if (high.X <= low.X || high.Y <= low.Y)
                    return;

                bool hit = false;

                if ((orb.Pos.X < low.X && orb.Velocity.X < 0f) || (orb.Pos.X > high.X && orb.Velocity.X > 0f))
                {
                    orb.Pos.X = orb.Pos.X < low.X ? low.X + (low.X - orb.Pos.X) : high.X - (orb.Pos.X - high.X);
                    orb.Velocity.X = -orb.Velocity.X;
                    hit = true;
                }

                if ((orb.Pos.Y < low.Y && orb.Velocity.Y < 0f) || (orb.Pos.Y > high.Y && orb.Velocity.Y > 0f))
                {
                    orb.Pos.Y = orb.Pos.Y < low.Y ? low.Y + (low.Y - orb.Pos.Y) : high.Y - (orb.Pos.Y - high.Y);
                    orb.Velocity.Y = -orb.Velocity.Y;
                    hit = true;
                }

                if (hit)
                {
                    sound(simTime);
                    f.addFlash(simTime, Vector2.Clamp(orb.Pos, low, high), orbRadius(f) * 3f);
                }
            }

            private void sound(double time)
            {
                if (time - lastSound < 120)
                    return;

                lastSound = time;
                sounds.Add(time);
            }

            /// <summary>Flies the orbs on, in steps of a few ms, up to <paramref name="to"/>; the ones that have come meanwhile join on time.</summary>
            private void advance(BulletField f, double to)
            {
                if (waiting.Count > 0)
                    waiting.Sort((a, b) => a.Born.CompareTo(b.Born));

                if (double.IsNaN(simTime))
                {
                    if (waiting.Count == 0)
                        return;

                    simTime = waiting[0].Born - sim_step;
                }

                float radius = orbRadius(f);
                Vector2 low = f.stageMin + new Vector2(radius);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(radius));
                float dt = (float)sim_step;
                int guard = 0;

                while (simTime + sim_step <= to && guard++ < 20000)
                {
                    if (flying.Count == 0)
                    {
                        // nothing flies: on to when the next comes (or to now)
                        if (waiting.Count == 0)
                        {
                            simTime = Math.Max(simTime, to);
                            break;
                        }

                        if (waiting[0].Born - sim_step > simTime)
                        {
                            simTime = waiting[0].Born - sim_step;
                            continue;
                        }
                    }

                    simTime += sim_step;

                    // (what has come: an orb takes its place in the flight, its path starting at this step; with too many flying, the oldest shrinks away)
                    while (waiting.Count > 0 && waiting[0].Born <= simTime)
                    {
                        var orb = waiting[0];
                        waiting.RemoveAt(0);

                        orb.Number = orbs.Count;
                        orb.Born = simTime;
                        orb.Fresh = true;
                        orb.Pos = Vector2.Clamp(orb.Origin, low, high);
                        orb.Velocity = orb.Direction * orbSpeed;
                        orb.Until = orb.Born + lifeMs;
                        orb.Path.Clear();
                        orb.Path.Add(orb.Pos);
                        orb.Flying = true;

                        int alive = 0;
                        Orb? oldest = null;

                        foreach (var other in flying)
                        {
                            if (other.Until <= simTime + DanmakusuBalance.REIMU_ORB_POP_MS)
                                continue;

                            alive++;
                            oldest ??= other;
                        }

                        if (alive >= DanmakusuBalance.MISUMARU_ORBS_MOST && oldest != null)
                            oldest.Until = Math.Min(oldest.Until, simTime + DanmakusuBalance.REIMU_ORB_POP_MS);

                        orbs.Add(orb);
                        flying.Add(orb);
                    }

                    flying.RemoveAll(o => simTime >= o.Until);

                    foreach (var orb in flying)
                    {
                        if (orb.Fresh)
                            continue; // (its first place is there already)

                        orb.Pos += orb.Velocity * dt;
                        bounceOffWalls(f, orb, low, high);
                    }

                    // (two orbs that touch ricochet off each other: the same mass, they swap the speed they have along the line between them)
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
                            sound(simTime);
                            f.addFlash(simTime, (a.Pos + b.Pos) / 2f, radius * 3f);
                        }
                    }

                    foreach (var orb in flying)
                    {
                        if (orb.Fresh)
                            orb.Fresh = false;
                        else
                            orb.Path.Add(orb.Pos);
                    }
                }
            }

            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                advance(f, now);

                while (soundNext < sounds.Count && sounds[soundNext] <= now)
                {
                    double time = sounds[soundNext++];

                    if (time > resetTime && now - time < 250)
                        f.playStageSound("taiko_slam");
                }

                // (what was owed and never came to turn: the bullet is gone)
                if (owed.Count > 64)
                {
                    var stale = new List<(BulletEmitter, int)>();

                    foreach (var (key, owe) in owed)
                    {
                        if (now - owe.Time > 250)
                            stale.Add(key);
                    }

                    foreach (var key in stale)
                        owed.Remove(key);
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

                // (past what has been simulated - the autoplay asks where it will be - it goes straight on as it went last)
                if (i >= orb.Path.Count - 1)
                    return orb.Path.Count >= 2 && t < orb.Until ? orb.Path[^1] + (orb.Path[^1] - orb.Path[^2]) * (float)(x - (orb.Path.Count - 1)) : orb.Path[^1];

                return Vector2.Lerp(orb.Path[i], orb.Path[i + 1], (float)(x - i));
            }

            /// <summary>How much of an orb there is (0..1): it grows out when it comes and shrinks away at the end.</summary>
            private static float presence(Orb orb, double t)
                => Math.Min(smoothStep((t - orb.Born) / DanmakusuBalance.REIMU_ORB_POP_MS), smoothStep((orb.Until - t) / DanmakusuBalance.REIMU_ORB_POP_MS));

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float hit = DanmakusuBalance.REIMU_ORB_HIT * orbRadius(f);

                foreach (var orb in orbs)
                {
                    if (!orb.Flying || now < orb.Born || now > orb.Until)
                        continue;

                    float grown = presence(orb, now);

                    if (grown >= 0.5f && Vector2.Distance(f.characterPosition, positionOf(orb, now)) <= hit * grown + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float radius = orbRadius(f);

                foreach (var orb in orbs)
                {
                    if (!orb.Flying || t < orb.Born || t > orb.Until)
                        continue;

                    float grown = presence(orb, t);
                    Vector2 at = positionOf(orb, t);

                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(radius * 5f * grown), 0f, 0.35f * grown, new Color4(1f, 0.45f, 0.45f, 1f), true);
                    f.stageFront.Add(StageTexture.YinYang, at, new Vector2(radius * 2f * grown), (float)(t * DanmakusuBalance.REIMU_ORB_SPIN), 1f, Color4.White);
                }
            }
        }
    }
}
