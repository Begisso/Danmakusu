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
        // Toyosatomimi no Miko: she stands in the middle and never pauses. Every eight beats she counts the bullets on the screen by direction, and the thirty 3 degree cones from her to the edge of the screen that hold the most
        // light up and stay. The bullets in them (and what flies into them for the next six beats) glow and are pulled to her, to arrive when the cones are over. A cone comes up half as visible as it then stands and grows back
        // to that over its first six beats; the cones grow brighter over their last two beats and after eight
        // beats turn into bright still beams, as wide as the cones were, for four beats, while the screen dims; then the beams fade and everything that was pulled in comes out of her as five rings, half a beat apart. So a new
        // set of cones lights up just as the last one turns into beams. Every four beats sixteen big glowing bullets start outside the screen and fly at her, the first of them from behind the player.
        // ======================================================================================================================================================

        private sealed class RaysAct : StageAct
        {
            private const int bins = 120; // (360 / 3)

            private sealed class Cones
            {
                public double Mark;
                public Vector2 Centre;
                public int Taken, Fired;
                public bool Lit;
                public readonly bool[] Chosen = new bool[bins];
                public readonly float[] Angles = new float[DanmakusuBalance.MIKO_CONES];
                public readonly List<StoredBullet>[] Rings = new List<StoredBullet>[DanmakusuBalance.MIKO_RINGS];
            }

            private static readonly float half_angle = MathF.PI / bins;
            private static readonly float half_tangent = MathF.Tan(half_angle);

            private readonly double[] marks;
            private readonly double[] throws;
            private readonly double beat;
            private readonly List<Cones> sets = new List<Cones>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private readonly float[] scores = new float[bins];
            private readonly int[] order = new int[bins];
            private int nextMark, nextThrow;

            public RaysAct(BossPeriod period, double[] marks, double[] throws)
            {
                Period = period;
                this.marks = marks;
                this.throws = throws;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            /// <summary>(the cones of her last mark light up, beam, fade and throw out their rings after her section: she stays until then)</summary>
            public override double MechanicsEnd => marks.Length == 0 ? double.NegativeInfinity
                : marks[^1] + beat * (DanmakusuBalance.MIKO_CONE_BEATS + DanmakusuBalance.MIKO_BEAM_BEATS + Math.Max(DanmakusuBalance.MIKO_FADE_BEATS, (DanmakusuBalance.MIKO_RINGS - 1) * DanmakusuBalance.MIKO_RING_GAP_BEATS));

            private double takenUntil(Cones set) => set.Mark + beat * DanmakusuBalance.MIKO_TAKE_BEATS;

            private double beamsAt(Cones set) => set.Mark + beat * DanmakusuBalance.MIKO_CONE_BEATS;

            private double fadesAt(Cones set) => beamsAt(set) + beat * DanmakusuBalance.MIKO_BEAM_BEATS;

            private double goneAt(Cones set) => fadesAt(set) + beat * DanmakusuBalance.MIKO_FADE_BEATS;

            private double firedAt(Cones set, int ring) => fadesAt(set) + ring * beat * DanmakusuBalance.MIKO_RING_GAP_BEATS;

            /// <summary>Is a set of cones taking bullets at <paramref name="t"/> (the newest one is the only one that can be)?</summary>
            private Cones? takingAt(double t)
            {
                if (sets.Count == 0)
                    return null;

                var set = sets[^1];
                return t >= set.Mark && t < takenUntil(set) ? set : null;
            }

            private bool markAt(double from, double to)
            {
                foreach (double mark in marks)
                {
                    if (mark <= to && mark + beat * DanmakusuBalance.MIKO_TAKE_BEATS > from)
                        return true;
                }

                return false;
            }

            public override bool Absorbs(double t) => markAt(t, t);

            public override bool AbsorbsBetween(double from, double to) => markAt(from, to);

            public override void Reset(BulletField f, double time)
            {
                sets.Clear();
                ClearBullets();
                nextMark = 0;
                nextThrow = 0;

                while (nextMark < marks.Length && marks[nextMark] < time)
                    nextMark++;

                while (nextThrow < throws.Length && throws[nextThrow] < time)
                    nextThrow++;
            }

            private static int binOf(float angle) => Math.Clamp((int)((angle + MathF.PI) / MathF.Tau * bins), 0, bins - 1);

            private static float angleOf(int bin) => (bin + 0.5f) / bins * MathF.Tau - MathF.PI;

            /// <summary>Counts the bullets on the screen at <paramref name="mark"/> by direction from her, and lights up the cones that hold the most.</summary>
            private Cones choose(BulletField f, double mark, int number)
            {
                // (from the middle of the screen, where she stands: by where she was drawn the cones came from beside it when she went round the middle with another boss there, or had not got there yet)
                var set = new Cones { Mark = mark, Centre = (f.stageMin + f.stageMax) * 0.5f };

                for (int ring = 0; ring < set.Rings.Length; ring++)
                    set.Rings[ring] = new List<StoredBullet>();

                Array.Clear(scores, 0, bins);

                for (int k = 0; k < f.activeCount; k++)
                {
                    var emitter = f.emitters[f.active[k]];

                    if (emitter.Retired || emitter.Count == 0 || emitter.Homing || emitter.Time > mark)
                        continue;

                    ulong skip = emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask;
                    double e = emitter.WithoutFrozenTime(f.effects.Effective(emitter, mark));

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        if ((skip & (1UL << b)) != 0)
                            continue;

                        emitter.Evaluate(b, e, out Vector2 position, out _, out bool there);

                        if (!there || position.X < f.stageMin.X || position.X > f.stageMax.X || position.Y < f.stageMin.Y || position.Y > f.stageMax.Y)
                            continue;

                        Vector2 offset = position - set.Centre;
                        scores[binOf(MathF.Atan2(offset.Y, offset.X))]++;
                    }
                }

                // (where cones hold as many, the choice goes by number, so that it is the same every time)
                for (int bin = 0; bin < bins; bin++)
                {
                    scores[bin] += hash01(bin, number, 77) * 0.5f;
                    order[bin] = bin;
                }

                Array.Sort(scores, order);

                // (no two cones next to each other, so that each can be told from the others, and none on a cone of the set before, which is turning into beams right now; if that leaves too few, the second time round the set before does not count)
                var before = sets.Count > 0 ? sets[^1] : null;
                int chosen = 0;

                for (int round = 0; round < 2 && chosen < DanmakusuBalance.MIKO_CONES; round++)
                {
                    for (int i = bins - 1; i >= 0 && chosen < DanmakusuBalance.MIKO_CONES; i--)
                    {
                        int bin = order[i];

                        if (set.Chosen[bin] || set.Chosen[(bin + 1) % bins] || set.Chosen[(bin + bins - 1) % bins] || (round == 0 && before != null && before.Chosen[bin]))
                            continue;

                        set.Chosen[bin] = true;
                        set.Angles[chosen++] = angleOf(bin);
                    }
                }

                return set;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                while (nextMark < marks.Length && marks[nextMark] <= now)
                {
                    sets.Add(choose(f, marks[nextMark], nextMark));
                    nextMark++;
                }

                while (nextThrow < throws.Length && throws[nextThrow] <= now)
                    throwRing(f, throws[nextThrow++]);

                foreach (var set in sets)
                {
                    if (!set.Lit && now >= beamsAt(set))
                    {
                        set.Lit = true;

                        if (now - beamsAt(set) < 250)
                            f.playStageSound("mokou_wall_hit");
                    }

                    while (set.Fired < set.Rings.Length && firedAt(set, set.Fired) <= now)
                        fire(f, set, set.Fired++);
                }

                while (sets.Count > 0 && now > goneAt(sets[0]) && sets[0].Fired == sets[0].Rings.Length)
                    sets.RemoveAt(0);
            }

            /// <summary>Sixteen big bullets outside the screen, all flying at her, the first from behind the player.</summary>
            private void throwRing(BulletField f, double time)
            {
                Vector2 centre = f.bossVisualOf(Period);
                Vector2 look = f.characterPosition - centre;
                float first = look.LengthSquared > 1e-3f ? MathF.Atan2(look.Y, look.X) : 0f;
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.MIKO_BIG;
                float far = reach(f) + radius;
                float speed = stageBulletSpeed(Period);
                int style = f.comboStyle(0, true, Period.Rank);

                for (int i = 0; i < DanmakusuBalance.MIKO_BIG_BULLETS; i++)
                {
                    float angle = first + i * MathF.Tau / DanmakusuBalance.MIKO_BIG_BULLETS;
                    Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    AddBullet(new StageBullet
                    {
                        T0 = time,
                        P0 = centre + way * far,
                        V = -way * speed,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = style,
                        Shape = BulletShape.Circle,
                        Bloom = DanmakusuBalance.MIKO_BLOOM,
                        Until = time + far / speed,
                        Fade = 150,
                    });
                }
            }

            /// <summary>What was pulled in comes out of her, evenly round her.</summary>
            private void fire(BulletField f, Cones set, int number)
            {
                var ring = set.Rings[number];

                if (ring.Count == 0)
                    return;

                double time = firedAt(set, number);
                float speed = stageBulletSpeed(Period);
                float step = MathF.Tau / ring.Count;

                for (int i = 0; i < ring.Count; i++)
                {
                    var look = ring[i];
                    float angle = (i + number / (float)set.Rings.Length) * step;
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = time,
                        P0 = set.Centre,
                        V = velocity,
                        Radius = look.Radius,
                        Hit = look.Hit,
                        Style = look.Style,
                        Shape = look.Shape,
                        Tint = look.Tint,
                        TintAmount = look.TintAmount,
                        Shade = look.Shade,
                        Blades = look.Blades,
                        BladeSpin = look.BladeSpin,
                        Bloom = DanmakusuBalance.MIKO_BLOOM,
                        Until = time + f.stageLife(velocity),
                    });
                }
            }

            /// <summary>A bullet that is on the screen in one of the cones stops where it is and is pulled to her, to arrive when the cones are over.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                var set = takingAt(t);

                if (set == null || f.failed || emitter.Shape == BulletShape.Train)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                Vector2 offset = to - set.Centre;

                if (!set.Chosen[binOf(MathF.Atan2(offset.Y, offset.X))])
                    return false;

                var look = f.makeStored(emitter, t);
                var ring = set.Rings[set.Taken % set.Rings.Length];
                int number = set.Taken % set.Rings.Length;
                set.Taken++;

                // (a ring keeps gaps to dodge through: what does not fit into the five is swallowed)
                bool shot = ring.Count < DanmakusuBalance.MIKO_RING_MOST;

                if (shot)
                    ring.Add(look);

                double fired = fadesAt(set);
                float pull = (float)(fired - t);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    Acc = -offset * (2f / (pull * pull)),
                    Switch = fired,
                    Radius = look.Radius,
                    Hit = look.Hit,
                    Style = look.Style,
                    Shape = look.Shape,
                    Tint = look.Tint,
                    TintAmount = look.TintAmount,
                    Shade = look.Shade,
                    Blades = look.Blades,
                    BladeSpin = look.BladeSpin,
                    BloomAt = t,
                    BloomTo = DanmakusuBalance.MIKO_BLOOM,
                    Untraced = true,
                    Until = shot ? firedAt(set, number) : fired,
                });

                return true;
            }

            /// <summary>How far from her the cones and beams go: past every corner of the screen.</summary>
            private float reach(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) * 0.5f + f.circleRadius * 2f;

            private static readonly Color4 light = new Color4(1f, 0.88f, 0.55f, 1f);
            private static readonly Color4 core = new Color4(1f, 0.97f, 0.85f, 1f);

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float length = reach(f) * 2f; // (the wedge fades along its length: it is drawn twice as long, and the screen ends halfway)
                float dark = 0f;

                foreach (var set in sets)
                {
                    if (t < set.Mark || t > goneAt(set))
                        continue;

                    if (t >= beamsAt(set))
                    {
                        float beamLevel = t < fadesAt(set) ? 1f : 1f - smoothStep((t - fadesAt(set)) / (goneAt(set) - fadesAt(set)));
                        dark = Math.Max(dark, beamLevel);
                        float halo = reach(f) * half_tangent * DanmakusuBalance.MIKO_HALO;

                        foreach (float angle in set.Angles)
                        {
                            Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                            f.stageTree.Add(StageTexture.Light, set.Centre + way * (reach(f) * 0.5f), new Vector2(reach(f), halo), angle * 180f / MathF.PI, beamLevel * 0.7f, light, true);
                        }

                        continue;
                    }

                    double brightenFrom = beamsAt(set) - beat * DanmakusuBalance.MIKO_BRIGHTEN_BEATS;
                    float brighter = smoothStep((t - brightenFrom) / (beat * DanmakusuBalance.MIKO_BRIGHTEN_BEATS));

                    // (half as visible when it comes up, back to the plain level, evenly, by the time it starts to brighten)
                    float settled = (float)Math.Clamp((t - set.Mark) / Math.Max(brightenFrom - set.Mark, 1.0), 0.0, 1.0);
                    float faint = DanmakusuBalance.MIKO_CONE_START_ALPHA + (DanmakusuBalance.MIKO_CONE_ALPHA - DanmakusuBalance.MIKO_CONE_START_ALPHA) * settled;
                    float level = smoothStep((t - set.Mark) / 300.0) * (faint + (1f - faint) * brighter);
                    Vector2 size = new Vector2(length, 2f * length * half_tangent);

                    foreach (float angle in set.Angles)
                    {
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        Vector2 place = set.Centre + way * (length * 0.5f);
                        float degrees = angle * 180f / MathF.PI;

                        f.stageBack.Add(StageTexture.Wedge, place, size, degrees, level * 0.7f, light);
                        f.stageBack.Add(StageTexture.Wedge, place, size, degrees, level, core, true);
                    }
                }

                if (dark > 0.002f)
                {
                    if (f.hasScreenBounds)
                    {
                        f.mikoBackground.Position = f.screenMin;
                        f.mikoBackground.Size = f.screenMax - f.screenMin;
                    }

                    f.mikoBackground.Alpha = Math.Max(f.mikoBackground.Alpha, dark * DanmakusuBalance.MIKO_BACKGROUND_ALPHA);
                }
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                if (f.failed)
                    return;

                foreach (var set in sets)
                {
                    if (t >= beamsAt(set) && t < goneAt(set))
                    {
                        float level = t < fadesAt(set) ? 1f : 1f - smoothStep((t - fadesAt(set)) / (goneAt(set) - fadesAt(set)));
                        into.Add(new StageDim { Centre = set.Centre, Radius = 0f, Strength = level * DanmakusuBalance.MIKO_DIM, Outside = true });
                    }
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                float length = reach(f);
                float wide = length * half_tangent;

                foreach (var set in sets)
                {
                    if (t < beamsAt(set) || t > goneAt(set))
                        continue;

                    float alpha = t < fadesAt(set) ? 1f : 1f - smoothStep((t - fadesAt(set)) / (goneAt(set) - fadesAt(set)));

                    foreach (float angle in set.Angles)
                    {
                        line.Clear();
                        line.Add(set.Centre);
                        line.Add(set.Centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length);
                        widths.Clear();
                        widths.Add(1f);
                        widths.Add(wide);
                        f.addPlainBeam(line, wide, core, light, ref beams, ref pointTotal, widths);

                        if (alpha < 0.999f)
                            Array.Fill(f.laserFadePointBuffer, alpha, pointTotal - 2, 2);
                    }
                }
            }

            /// <summary>The beams hurt like a slider body until they start to fade.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                foreach (var set in sets)
                {
                    if (now < beamsAt(set) || now >= fadesAt(set))
                        continue;

                    Vector2 offset = f.characterPosition - set.Centre;
                    float distance = Math.Max(offset.Length, 1f);
                    float direction = MathF.Atan2(offset.Y, offset.X);
                    float near = half_angle * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS / distance;

                    foreach (float angle in set.Angles)
                    {
                        float apart = MathF.Abs(direction - angle);

                        if (Math.Min(apart, MathF.Tau - apart) <= near)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
