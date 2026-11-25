using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Takane Yamashiro (2026-11-25): all through her section every bullet of a ring at least TAKANE_BIG times as big as a basic one (anyone's) grabs every smaller bullet of a ring whose middle comes within
        // TAKANE_GRAB_CIRCLES osu! circle radii of its body: the grabbed one leaves its ring and goes round the big one once every TAKANE_ORBIT_BEATS, as far out as it was grabbed, on a circle tilted
        // towards the screen - seen as an ellipse TAKANE_ORBIT_FLAT as wide across as along, its long axis the way it was grabbed from - so that each goes round on a slant of its own, as the electrons of an
        // atom do, wherever the big one goes; at most TAKANE_MOST_GRABBED round one. When the big one is gone (it has hit, something took it, it has left the screen), what went round it flies off the way
        // it was going. That the bullets of her section fly the slower the bigger they are, and her rings of big balls, are the planner's.
        // The big bullets are looked for where they are at the end of each frame's scan, as Eika's clumps are (so what is grabbed depends on the frames; going back in time grabs again from there).
        // ======================================================================================================================================================

        private sealed class SatelliteAct : StageAct
        {
            private struct Nucleus
            {
                public BulletEmitter Ring;
                public int Bullet;
                public Vector2 At;
                public float Radius;
                public int Next;
            }

            private sealed class Atom
            {
                public readonly List<int> Members = new List<int>(); // (in Bullets)
                public bool Released;
            }

            private readonly double beat;
            private readonly List<Nucleus> nuclei = new List<Nucleus>();
            private readonly Dictionary<long, int> cells = new Dictionary<long, int>();
            private readonly Dictionary<(BulletEmitter, int), Atom> atoms = new Dictionary<(BulletEmitter, int), Atom>();
            private readonly List<(BulletEmitter Ring, int Bullet, Atom Atom)> holding = new List<(BulletEmitter, int, Atom)>();
            private double gridTime = double.NaN;
            private float cellSize = 64f;

            public SatelliteAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * DanmakusuBalance.LIFE_MOST_BEATS; // (what goes round flies on after her section, until its big one is gone)
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                nuclei.Clear();
                cells.Clear();
                atoms.Clear();
                holding.Clear();
                gridTime = double.NaN;
                ClearBullets();
            }

            /// <summary>How big a bullet must be to grab (px).</summary>
            private float bigEnough(BulletField f) => DanmakusuBalance.TAKANE_BIG * f.stageBaseRadius(Period) - 1e-3f;

            private static long cellOf(Vector2 at, float size) => ((long)MathF.Floor(at.X / size) << 32) ^ (uint)(int)MathF.Floor(at.Y / size);

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                releaseOrphans(f, now);

                if (now < AbsorbFrom || now > AbsorbTo)
                    return;

                // (the big bullets on the screen now, in a grid as wide as the most any of them reaches)
                nuclei.Clear();
                cells.Clear();
                gridTime = now;

                float big = bigEnough(f);
                float grab = f.circleRadius * DanmakusuBalance.TAKANE_GRAB_CIRCLES;
                float widest = 0f;
                Vector2 low = f.stageMin - new Vector2(grab), high = f.stageMax + new Vector2(grab);

                for (int k = 0; k < f.activeCount; k++)
                {
                    var ring = f.emitters[f.active[k]];

                    if (ring.Retired || ring.Count == 0 || ring.Shape == BulletShape.Train || now < ring.Time || ring.VisualRadius * ring.BigScaleMax < big)
                        continue;

                    double own = ring.WithoutFrozenTime(f.effects.Effective(ring, now));

                    for (int b = 0; b < ring.Count && b < 64; b++)
                    {
                        if (((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & (1UL << b)) != 0)
                            continue;

                        float radius = ring.VisualRadius * ring.BigScale(b);

                        if (radius < big)
                            continue;

                        ring.Evaluate(b, own, out Vector2 at, out _, out bool alive);

                        if (!alive || at.X < low.X || at.X > high.X || at.Y < low.Y || at.Y > high.Y)
                            continue;

                        nuclei.Add(new Nucleus { Ring = ring, Bullet = b, At = at, Radius = radius, Next = -1 });
                        widest = Math.Max(widest, radius + grab);
                    }
                }

                cellSize = Math.Max(widest, 8f);

                for (int i = 0; i < nuclei.Count; i++)
                {
                    var nucleus = nuclei[i];
                    long key = cellOf(nucleus.At, cellSize);
                    nucleus.Next = cells.TryGetValue(key, out int head) ? head : -1;
                    cells[key] = i;
                    nuclei[i] = nucleus;
                }
            }

            /// <summary>What goes round a big bullet that is gone flies off from where it is, the way it was going.</summary>
            private void releaseOrphans(BulletField f, double now)
            {
                for (int h = holding.Count - 1; h >= 0; h--)
                {
                    var (ring, bullet, atom) = holding[h];
                    bool gone = ring.Retired || ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & (1UL << bullet)) != 0;

                    if (!gone)
                    {
                        ring.Evaluate(bullet, ring.WithoutFrozenTime(f.effects.Effective(ring, now)), out Vector2 at, out _, out bool alive);
                        float far = f.circleRadius * 4f;
                        gone = !alive || at.X < f.stageMin.X - far || at.X > f.stageMax.X + far || at.Y < f.stageMin.Y - far || at.Y > f.stageMax.Y + far;
                    }

                    if (!gone)
                        continue;

                    foreach (int index in atom.Members)
                    {
                        var orbiting = Bullets[index];

                        if (now > orbiting.Until || f.stageConsumed.Contains(Id * 65536 + index))
                            continue;

                        Vector2 at = stageBulletPosition(orbiting, now);
                        Vector2 velocity = stageBulletVelocity(orbiting, now);
                        orbiting.Until = now;
                        Bullets[index] = orbiting;

                        AddBullet(new StageBullet
                        {
                            T0 = now,
                            P0 = at,
                            V = velocity,
                            Radius = orbiting.Radius,
                            Hit = orbiting.Hit,
                            Style = orbiting.Style,
                            Shape = orbiting.Shape,
                            Tint = orbiting.Tint,
                            TintAmount = orbiting.TintAmount,
                            Shade = orbiting.Shade,
                            Until = now + f.stageLife(velocity),
                            Untraced = true,
                        });
                    }

                    atom.Released = true;
                    holding.RemoveAt(h);
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t != gridTime || nuclei.Count == 0 || emitter.Shape == BulletShape.Train)
                    return false;

                float size = emitter.VisualRadius * emitter.BigScale(bullet);

                if (size >= bigEnough(f))
                    return false; // (a big one is not grabbed)

                float grab = f.circleRadius * DanmakusuBalance.TAKANE_GRAB_CIRCLES;
                long x = (long)MathF.Floor(to.X / cellSize), y = (long)MathF.Floor(to.Y / cellSize);

                for (long a = x - 1; a <= x + 1; a++)
                {
                    for (long b = y - 1; b <= y + 1; b++)
                    {
                        if (!cells.TryGetValue((a << 32) ^ (uint)(int)b, out int at))
                            continue;

                        for (; at >= 0; at = nuclei[at].Next)
                        {
                            var nucleus = nuclei[at];
                            float reach = nucleus.Radius + grab;

                            if (nucleus.Radius <= size || Vector2.DistanceSquared(to, nucleus.At) > reach * reach)
                                continue;

                            if (!atoms.TryGetValue((nucleus.Ring, nucleus.Bullet), out var atom))
                            {
                                atom = new Atom();
                                atoms[(nucleus.Ring, nucleus.Bullet)] = atom;
                                holding.Add((nucleus.Ring, nucleus.Bullet, atom));
                            }

                            if (atom.Released || atom.Members.Count >= DanmakusuBalance.TAKANE_MOST_GRABBED)
                                continue;

                            if (grabbed(f, atom, nucleus, emitter, bullet, to, t))
                                return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>Bullet <paramref name="bullet"/> of <paramref name="emitter"/>, at <paramref name="at"/>, goes round <paramref name="nucleus"/> from now on.</summary>
            private bool grabbed(BulletField f, Atom atom, in Nucleus nucleus, BulletEmitter emitter, int bullet, Vector2 at, double t)
            {
                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 off = at - nucleus.At;
                float distance = off.Length;

                // (right on its middle: a way of its own; never inside the big one)
                if (distance < 1e-3f)
                {
                    float angle = (atom.Members.Count + 1) * 2.4f;
                    off = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    distance = 1f;
                }

                float orbit = Math.Max(distance, nucleus.Radius + stored.Radius * big * 0.5f);
                double hubEnd = nucleus.Ring.Time + nucleus.Ring.Lifetime + beat * 4;
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = Vector2.Zero,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Orbit = orbit,
                    OrbitOmega = MathF.Tau / (float)(beat * DanmakusuBalance.TAKANE_ORBIT_BEATS),
                    OrbitPhase = 0f,
                    OrbitFlat = DanmakusuBalance.TAKANE_ORBIT_FLAT,
                    OrbitTilt = MathF.Atan2(off.Y, off.X),
                    HubRing = nucleus.Ring,
                    HubBullet = nucleus.Bullet,
                    HubEffects = f.effects,
                    Until = Math.Max(hubEnd, t + beat),
                    Untraced = true,
                });

                if (Bullets.Count <= index)
                    return false;

                atom.Members.Add(index);
                return true;
            }
        }
    }
}
