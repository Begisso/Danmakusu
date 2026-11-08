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
        // Wakasagihime: in her attacks the screen dims ocean blue and everything turns blue (tideLevel), and every bullet fired meanwhile swims: it goes from side to side of its way by a circle radius (the planner's, a closed form).
        // Every thirty-second bullet on the screen is a hollow bubble twice as big that drifts on the way the bullet went: it hurts as a bullet does (and is used up by the hit), and it pops into a ring of eight bullets sixteen beats after it appeared, flashing red for the last four (it no longer pops when the player
        // touches it). Every eighth bullet that goes out of the bottom grows a blade of dark green seaweed there, half the screen high and twice as wide as a slider's beam, narrowing to a sharp tip, that sways from side to side, hurts like a slider body, and after eight beats comes
        // off the ground and falls upwards at Zanmu's pull. Six green petals grow on every blade, on both sides by turns, that hurt while they are on it (and are not used up); as it comes off the ground they spread away from it, petal bullets that flutter as they fly.
        // She shoots Marisa's stars, half as big and twice as often (the planner's).
        // Every bullet that swims leaves a trace of beam behind it, the last two beats of its way, as wide all along, blue: it hurts like a slider body, and so does the bullet (the planner's). What she shoots herself are bubbles
        // from the start, all through her section, a quarter as many as a boss's stars.
        // ======================================================================================================================================================

        private sealed class TideAct : StageAct
        {
            private sealed class Bubble
            {
                public int Index; // (in Bullets)
                public double Pops;
                public bool Popped;
                public int Style;
                public float Speed, Radius, Hit;
            }

            private sealed class Blade
            {
                public double Born;
                public float X, Phase;
                public float Length; // (how tall it grows: a share of WAKASAGI_BLADE_HEIGHT, each its own)
                public int Number; // (which blade it is: its petals go by it)
                public bool Shed; // (its petals have flown off)
            }

            private const int blade_points = 10;

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly List<Bubble> bubbles = new List<Bubble>();
            private readonly List<Blade> blades = new List<Blade>();
            private readonly List<Vector2> points = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private int crossings;

            // (the traces of what swims, worked out once a frame: where each starts in tracePoints and how many points it has, the bullet first)
            private readonly List<(int Start, int Count)> traces = new List<(int, int)>();
            private readonly List<Vector2> tracePoints = new List<Vector2>();
            private readonly List<Vector2> traceLine = new List<Vector2>();
            private double tracedAt = double.NaN;

            /// <summary>What is kept of the trace of one bullet from frame to frame: where it was at the moments of a grid on its own clock, a step apart, newest first, and the newest of those moments (they do not change: only a new one comes as its clock passes the next).</summary>
            private sealed class KeptTrace
            {
                public double Grid = double.NaN;
                public Vector2[] Points = [];
                public int Count;
                public int Seen; // (the frame it was last traced in)
            }

            private readonly Dictionary<long, KeptTrace> keptTraces = new Dictionary<long, KeptTrace>();
            private readonly List<long> forgotten = new List<long>();
            private int traceFrame;

            // (every bullet to trace this frame: its ring, its number, its clock now and what is kept of it; where it is now and whether it is there, worked out on several cores)
            private readonly List<(BulletEmitter Emitter, int Bullet, double Now, KeptTrace Kept)> traceJobs = new List<(BulletEmitter, int, double, KeptTrace)>();
            private Vector2[] jobHeads = new Vector2[0];
            private bool[] jobAlive = new bool[0];

            private float traceWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.WAKASAGI_TRACE_WIDTH;

            /// <summary>The traces as they were worked out this frame (in Update); before the first frame, now.</summary>
            private void trace(BulletField f, double t)
            {
                if (double.IsNaN(tracedAt))
                    traceNow(f, t);
            }

            /// <summary>
            /// Where every bullet that swims (and is there) has been over the last two beats, at <paramref name="t"/>: where it is now, then where it was at the moments of a grid on its own clock, its closed form taken back. Those
            /// are kept from frame to frame, and only the newest is worked out as its clock passes it (all of them again after a seek). A trace that is all off the screen is left out.
            /// </summary>
            private void traceNow(BulletField f, double t)
            {
                tracedAt = t;
                traces.Clear();
                tracePoints.Clear();
                traceFrame++;

                double length = beat * DanmakusuBalance.WAKASAGI_TRACE_BEATS;
                double step = length / DanmakusuBalance.WAKASAGI_TRACE_STEPS;
                int full = DanmakusuBalance.WAKASAGI_TRACE_STEPS + 2; // (one more than the trace needs: its end is put between the last two, exactly so far back, so that it glides instead of stepping back a point at a time)
                float margin = traceWidth(f) * 2f;

                traceJobs.Clear();

                for (int k = 0; k < f.activeCount; k++)
                {
                    var emitter = f.emitters[f.active[k]];

                    // (only what really swims: a straight bullet that was fired in her attack but that another boss steers, or that wraps round the screen, has no trace)
                    if (emitter.Retired || !emitter.Waves || emitter.Count == 0)
                        continue;

                    double now = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));

                    if (now <= emitter.Time)
                        continue;

                    ulong skip = emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask;

                    for (int b = 0; b < emitter.Count && b < 64; b++)
                    {
                        if ((skip & (1UL << b)) != 0)
                            continue;

                        long key = ((long)f.active[k] << 6) | (long)b;

                        if (!keptTraces.TryGetValue(key, out var kept))
                            keptTraces[key] = kept = new KeptTrace { Points = new Vector2[full] };

                        kept.Seen = traceFrame;
                        traceJobs.Add((emitter, b, now, kept));
                    }
                }

                // (what has not been traced for a while is gone: forgotten now and then)
                if (traceFrame % 60 == 0)
                {
                    forgotten.Clear();

                    foreach (var (key, kept) in keptTraces)
                    {
                        if (kept.Seen < traceFrame - 1)
                            forgotten.Add(key);
                    }

                    foreach (long key in forgotten)
                        keptTraces.Remove(key);
                }

                if (jobHeads.Length < traceJobs.Count)
                {
                    jobHeads = new Vector2[Math.Max(traceJobs.Count, jobHeads.Length * 2)];
                    jobAlive = new bool[jobHeads.Length];
                }

                // (each only reads its ring and writes what is kept of its own bullet: they can be worked out side by side, on several cores)
                void one(int job)
                {
                    var (emitter, b, now, kept) = traceJobs[job];

                    emitter.Evaluate(b, now, out jobHeads[job], out _, out jobAlive[job]);

                    if (!jobAlive[job])
                        return;

                    double grid = Math.Floor(now / step) * step;

                    // (where it was at the moments of the grid from the k-th on, as far back as it was there, into the kept points from the first free place)
                    void fill(int fromK, int toK)
                    {
                        for (int k = fromK; k < toK; k++)
                        {
                            double at = grid - k * step;
                            bool spawned = at <= emitter.Time;

                            emitter.Evaluate(b, spawned ? emitter.Time : at, out Vector2 point, out _, out bool there);

                            if (!there)
                                break;

                            kept.Points[kept.Count++] = point;

                            if (spawned)
                                break;
                        }
                    }

                    if (double.IsNaN(kept.Grid) || grid < kept.Grid || grid - kept.Grid > step * (full - 0.5))
                    {
                        // (all of it: new, or time went back, or too far on)
                        kept.Count = 0;
                        fill(0, full);
                    }
                    else if (grid > kept.Grid)
                    {
                        // (the clock has passed one or a few new moments of the grid: the kept points move back by as many, the oldest drop off, and only the new ones are worked out)
                        int newer = (int)Math.Round((grid - kept.Grid) / step);
                        int left = Math.Min(kept.Count, full - newer);

                        Array.Copy(kept.Points, 0, kept.Points, newer, left);
                        kept.Count = 0;
                        fill(0, newer);

                        // (if fewer came out than were asked for, the way back reached where it was fired: nothing older than that is its)
                        if (kept.Count == newer)
                            kept.Count = newer + left;
                    }

                    kept.Grid = grid;
                }

                if (traceJobs.Count >= 128)
                    System.Threading.Tasks.Parallel.For(0, traceJobs.Count, parallelOptions, one);
                else
                {
                    for (int job = 0; job < traceJobs.Count; job++)
                        one(job);
                }

                for (int job = 0; job < traceJobs.Count; job++)
                {
                    if (!jobAlive[job])
                        continue;

                    var (jobEmitter, _, jobNow, kept) = traceJobs[job];
                    int start = tracePoints.Count;

                    tracePoints.Add(jobHeads[job]);

                    // (the kept points, newest first, as far back as the trace reaches: the last one is put where the bullet was exactly the trace's length ago, between the two kept points round that moment)
                    double tail = jobNow - length;
                    Vector2 previous = jobHeads[job];
                    double previousTime = jobNow;

                    for (int i = jobNow - kept.Grid < 1e-6 ? 1 : 0; i < kept.Count; i++)
                    {
                        double at = Math.Max(kept.Grid - i * step, jobEmitter.Time); // (the oldest may be where it was fired)

                        if (at < tail)
                        {
                            float share = previousTime - at > 1e-6 ? (float)((previousTime - tail) / (previousTime - at)) : 1f;
                            tracePoints.Add(Vector2.Lerp(previous, kept.Points[i], share));
                            break;
                        }

                        tracePoints.Add(kept.Points[i]);
                        previous = kept.Points[i];
                        previousTime = at;
                    }

                    bool seen = false;

                    for (int i = start; i < tracePoints.Count && !seen; i++)
                    {
                        Vector2 p = tracePoints[i];
                        seen = p.X >= f.stageMin.X - margin && p.X <= f.stageMax.X + margin && p.Y >= f.stageMin.Y - margin && p.Y <= f.stageMax.Y + margin;
                    }

                    if (seen && tracePoints.Count - start >= 2)
                    {
                        traces.Add((start, tracePoints.Count - start));
                        TraceProbeCount++;

                        for (int i = start + 1; i < tracePoints.Count; i++)
                            TraceProbeLongest = Math.Max(TraceProbeLongest, Vector2.Distance(tracePoints[i - 1], tracePoints[i]));
                    }
                    else
                        tracePoints.RemoveRange(start, tracePoints.Count - start);
                }
            }

            public TideAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (end > start && t >= start && t < end)
                        return true;
                }

                return false;
            }

            private bool ownBubbles; // (she shoots bubbles: all through her section, not only in her attacks, the act takes them)

            public override void Init(BulletField f)
            {
                base.Init(f);

                foreach (var emitter in f.emitters)
                {
                    if (emitter.Bubbles && emitter.Time >= Start && emitter.Time < End)
                    {
                        ownBubbles = true;
                        break;
                    }
                }
            }

            public override bool Absorbs(double t) => inAttack(t) || (ownBubbles && t >= Start && t <= End);

            public override bool AbsorbsBetween(double from, double to)
            {
                if (ownBubbles && Start <= to && End >= from)
                    return true;

                foreach (var (start, end) in attacks)
                {
                    if (end > start && start <= to && end > from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                bubbles.Clear();
                blades.Clear();
                tracedAt = double.NaN;
                keptTraces.Clear();
                crossings = 0;
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Deflector)
                    return false;

                // (what she shoots herself is a bubble from the start, whenever she shoots it)
                if (emitter.Bubbles)
                    return to.X >= f.stageMin.X - 64f && to.X <= f.stageMax.X + 64f && to.Y >= f.stageMin.Y - 64f && to.Y <= f.stageMax.Y + 64f && addBubble(f, t, from, to, emitter);

                if (!inAttack(t))
                    return false;

                // (out of the bottom: seaweed; the bullet has gone anyway)
                float bottom = f.stageMax.Y;

                if (from.Y <= bottom && to.Y > bottom)
                {
                    if (crossings++ % DanmakusuBalance.WAKASAGI_EVERY == 0)
                        blades.Add(new Blade
                        {
                            Born = t, X = to.X, Phase = hash01(crossings, 8, 81) * MathF.Tau, Number = crossings,
                            Length = DanmakusuBalance.WAKASAGI_BLADE_SHORTEST + (DanmakusuBalance.WAKASAGI_BLADE_LONGEST - DanmakusuBalance.WAKASAGI_BLADE_SHORTEST) * hash01(crossings, 8, 82),
                        });

                    return false;
                }

                if ((Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.WAKASAGI_BUBBLE_EVERY != 0 || to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > bottom)
                    return false;

                return addBubble(f, t, from, to, emitter);
            }

            /// <summary>The bullet becomes a hollow bubble twice as big, drifting on the way it went: harmless, it pops into a ring of eight sixteen beats later (or at once when the player touches it).</summary>
            private bool addBubble(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter)
            {
                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                Vector2 way = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitY;
                Vector2 velocity = way * (stored.Speed * DanmakusuBalance.WAKASAGI_BUBBLE_SPEED);
                double pops = t + beat * DanmakusuBalance.WAKASAGI_BUBBLE_BEATS;
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = stored.Radius * DanmakusuBalance.WAKASAGI_BUBBLE_SIZE,
                    Hit = stored.Hit * DanmakusuBalance.WAKASAGI_BUBBLE_SIZE, // (it hurts as a bullet does, and is used up by the hit: it does not pop then)
                    Style = stored.Style,
                    Shape = BulletShape.Circle,
                    Hidden = true, // (the act draws it: as a bubble)
                    Until = pops,
                });

                if (Bullets.Count == index)
                    return false;

                bubbles.Add(new Bubble { Index = index, Pops = pops, Style = stored.Style, Speed = stored.Speed, Radius = stored.Radius, Hit = stored.Hit });
                return true;
            }

            /// <summary>A bubble pops: a ring of eight bullets from its middle.</summary>
            private void pop(BulletField f, Bubble bubble, double t)
            {
                bubble.Popped = true;

                var shell = Bullets[bubble.Index];
                Vector2 at = stageBulletPosition(shell, t);

                shell.Until = t;
                Bullets[bubble.Index] = shell;

                for (int k = 0; k < DanmakusuBalance.WAKASAGI_POP_BULLETS; k++)
                {
                    float angle = k * MathF.Tau / DanmakusuBalance.WAKASAGI_POP_BULLETS;
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * bubble.Speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at,
                        V = velocity,
                        Radius = bubble.Radius * DanmakusuBalance.WAKASAGI_POP_SIZE,
                        Hit = bubble.Hit * DanmakusuBalance.WAKASAGI_POP_SIZE,
                        Style = bubble.Style,
                        Shape = BulletShape.Circle,
                        Tint = BulletRenderer.TINT_BLUE,
                        TintAmount = 1f,
                        Until = t + f.stageLife(velocity),
                    });
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var bubble in bubbles)
                {
                    if (bubble.Popped)
                        continue;

                    if (now >= bubble.Pops)
                    {
                        pop(f, bubble, bubble.Pops);
                        continue;
                    }

                    // (one that has hit the player is used up: it does not pop)
                    if (f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + bubble.Index))
                        bubble.Popped = true;
                }

                blades.RemoveAll(blade => now > blade.Born + beat * DanmakusuBalance.WAKASAGI_BLADE_BEATS + DanmakusuBalance.WAKASAGI_BLADE_GONE_MS);

                // (as a blade comes off the ground its petals spread away from it: each flies off the way it pointed, a little turned, and flutters)
                foreach (var blade in blades)
                {
                    double detach = blade.Born + beat * DanmakusuBalance.WAKASAGI_BLADE_BEATS;

                    if (blade.Shed || now < detach)
                        continue;

                    blade.Shed = true;

                    for (int k = 0; k < DanmakusuBalance.WAKASAGI_PETALS; k++)
                    {
                        petal(f, blade, k, detach, out Vector2 position, out Vector2 outward, out float radius);

                        Vector2 direction = turned(outward, (hash01(blade.Number, k, 84) - 0.5f) * 0.6f);
                        Vector2 velocity = direction * (stageBulletSpeed(Period) * DanmakusuBalance.WAKASAGI_PETAL_SPEED * (0.75f + 0.5f * hash01(blade.Number, k, 85)));

                        AddBullet(new StageBullet
                        {
                            T0 = detach,
                            P0 = position,
                            V = velocity,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = (blade.Number + k) % f.palette.Length,
                            Shape = BulletShape.Petal,
                            Tint = BulletRenderer.TINT_SEAWEED,
                            TintAmount = 1f,
                            Wander = f.circleRadius * DanmakusuBalance.WAKASAGI_PETAL_SWAY_CIRCLES,
                            WanderPhase = hash01(blade.Number, k, 86) * MathF.Tau,
                            Until = detach + f.stageLife(velocity),
                        });
                    }
                }

            }

            /// <summary>
            /// The traces, once a frame (what hurts and what is drawn are the same): late in it, once the rings that fire now have been aimed - a boss's ring comes out of where she is only then, and traced before it its
            /// first point stayed where the planner had put it, off the top left of the screen, and a straight beam ran from there to her for two beats (with Junko on the screen: a bundle of them).
            /// </summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                traceNow(f, now);
            }

            /// <summary>A point of a blade: <paramref name="u"/> of the way up from its root. It grows to its own length over two beats, sways more the higher up, and comes off the ground after its time, falling upwards.</summary>
            private Vector2 bladePoint(BulletField f, Blade blade, float u, double t)
            {
                float height = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.WAKASAGI_BLADE_HEIGHT * blade.Length * smoothStep((t - blade.Born) / (beat * DanmakusuBalance.WAKASAGI_BLADE_GROW_BEATS));
                float sway = f.circleRadius * DanmakusuBalance.WAKASAGI_BLADE_SWAY_CIRCLES * u * u * MathF.Sin((float)((t - blade.Born) / (beat * DanmakusuBalance.WAKASAGI_BLADE_SWAY_BEATS) * Math.Tau) + blade.Phase + u * 2f);
                float rise = (float)Math.Max(t - blade.Born - beat * DanmakusuBalance.WAKASAGI_BLADE_BEATS, 0.0);

                return new Vector2(blade.X + sway, f.stageMax.Y - height * u - 0.5f * DanmakusuBalance.ZANMU_GRAVITY * rise * rise);
            }

            private float bladeWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.WAKASAGI_BLADE_WIDTH;

            /// <summary>
            /// The <paramref name="k"/>-th petal of a blade at <paramref name="t"/>: from a fifth of the way up to near its tip, on one side and the next on the other, just off its edge, pointing out and a little up along it (its tip that way), and as big as a
            /// plain bullet once the blade has grown (smaller higher up).
            /// </summary>
            private void petal(BulletField f, Blade blade, int k, double t, out Vector2 position, out Vector2 outward, out float radius)
            {
                float u = 0.2f + 0.7f * k / Math.Max(DanmakusuBalance.WAKASAGI_PETALS - 1f, 1f);
                float side = (blade.Number + k) % 2 == 0 ? 1f : -1f;
                Vector2 along = bladePoint(f, blade, Math.Min(u + 0.05f, 1f), t) - bladePoint(f, blade, Math.Max(u - 0.05f, 0f), t);
                Vector2 up = along.LengthSquared > 1e-6f ? Vector2.Normalize(along) : new Vector2(0f, -1f);
                Vector2 across = new Vector2(-up.Y, up.X) * side;
                float grown = Math.Max(smoothStep((t - blade.Born) / (beat * DanmakusuBalance.WAKASAGI_BLADE_GROW_BEATS)), 0.15f);

                outward = Vector2.Normalize(across + up * 0.45f);
                radius = f.stageBaseRadius(Period) * DanmakusuBalance.WAKASAGI_PETAL_SIZE * grown * (1.1f - 0.4f * u);
                position = bladePoint(f, blade, u, t) + across * (bladeWidth(f) * (1f - u * u) * 0.8f) + outward * radius;
            }

            /// <summary>The petals on the blades, until they fly off (drawn only: they hurt in <see cref="Hurts"/>, by the petal's own shape).</summary>
            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (f.failed)
                    return;

                foreach (var blade in blades)
                {
                    if (t < blade.Born || t >= blade.Born + beat * DanmakusuBalance.WAKASAGI_BLADE_BEATS)
                        continue;

                    for (int k = 0; k < DanmakusuBalance.WAKASAGI_PETALS; k++)
                    {
                        petal(f, blade, k, t, out Vector2 position, out Vector2 outward, out float radius);

                        into.Add(new StageBead
                        {
                            Position = position,
                            Radius = radius,
                            Style = (blade.Number + k) % f.palette.Length,
                            Shape = BulletShape.Petal,
                            Direction = outward,
                            Overlay = BulletRenderer.TINT_SEAWEED,
                            OverlayAmount = 1f,
                            Harmless = true,
                        });
                    }
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                var green = new Color4(0.08f, 0.32f, 0.16f, 1f);
                var edge = new Color4(0.03f, 0.16f, 0.09f, 1f);

                foreach (var blade in blades)
                {
                    points.Clear();
                    widths.Clear();

                    // (it narrows all the way up to a sharp tip)
                    for (int i = 0; i <= blade_points; i++)
                    {
                        float u = i / (float)blade_points;

                        points.Add(bladePoint(f, blade, u, t));
                        widths.Add(Math.Max(bladeWidth(f) * (1f - u * u), 0.4f));
                    }

                    f.addPlainBeam(points, bladeWidth(f), green, edge, ref beams, ref pointTotal, widths);
                    f.laserInstances[beams - 1].Wood = true;
                    f.laserInstances[beams - 1].CoreColour = new Color4(0.14f, 0.45f, 0.24f, 1f);
                }

                // (the traces of what swims: blue, narrowing towards their ends)
                trace(f, t);

                if (traces.Count == 0)
                    return;

                Color4 blue = BulletRenderer.SpecialBodies[BulletRenderer.TINT_BLUE - 1];
                Color4 border = DanmakusuBalance.BorderColour(blue);
                float width = traceWidth(f);

                foreach (var (start, count) in traces)
                {
                    traceLine.Clear();
                    widths.Clear();

                    for (int i = 0; i < count; i++)
                    {
                        traceLine.Add(tracePoints[start + i]);
                        widths.Add(width * (1f - (1f - DanmakusuBalance.WAKASAGI_TRACE_END) * i / Math.Max(count - 1f, 1f)));
                    }

                    f.addPlainBeam(traceLine, width, blue, border, ref beams, ref pointTotal, widths);
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float reach = bladeWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (the traces of what swims)
                trace(f, now);

                float traceReach = traceWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE;

                foreach (var (start, count) in traces)
                {
                    for (int i = 1; i < count; i++)
                    {
                        float narrower = 1f - (1f - DanmakusuBalance.WAKASAGI_TRACE_END) * i / Math.Max(count - 1f, 1f);

                        if (distanceToSegment(f.characterPosition, tracePoints[start + i - 1], tracePoints[start + i]) <= traceReach * narrower + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                            return true;
                    }
                }

                foreach (var blade in blades)
                {
                    // (its petals hurt while they are on it too, by their own shape, and are not used up: they fly off as bullets when it comes off the ground)
                    if (now >= blade.Born && now < blade.Born + beat * DanmakusuBalance.WAKASAGI_BLADE_BEATS)
                    {
                        for (int k = 0; k < DanmakusuBalance.WAKASAGI_PETALS; k++)
                        {
                            petal(f, blade, k, now, out Vector2 position, out Vector2 outward, out float radius);

                            float hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE;
                            Vector2 rel = f.characterPosition - position;

                            if (rel.LengthSquared <= (hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS) * (hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                && BulletCollision.Hits(BulletShape.Petal, hit, outward, rel, rel, DanmakusuBalance.PLAYER_HITBOX_RADIUS))
                                return true;
                        }
                    }

                    Vector2 last = bladePoint(f, blade, 0f, now);

                    for (int i = 1; i <= blade_points; i++)
                    {
                        float u = i / (float)blade_points;
                        Vector2 next = bladePoint(f, blade, u, now);

                        if (distanceToSegment(f.characterPosition, last, next) <= reach * (1f - u * u) + DanmakusuBalance.PLAYER_HITBOX_RADIUS * u * u)
                            return true;

                        last = next;
                    }
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float level = f.attackPresence(attacks, t);

                if (level > 0.002f)
                {
                    if (f.hasScreenBounds)
                    {
                        f.tideBackground.Position = f.screenMin;
                        f.tideBackground.Size = f.screenMax - f.screenMin;
                    }

                    // (the light under water: the dim breathes, in two slow waves of their own, and pale patches of light drift across)
                    float breath = 1f + 0.3f * MathF.Sin((float)t * 0.0013f) + 0.2f * MathF.Sin((float)t * 0.0031f + 1.3f);

                    f.tideBackground.Alpha = Math.Max(f.tideBackground.Alpha, level * DanmakusuBalance.WAKASAGI_BACKGROUND_ALPHA * breath);

                    Vector2 size = f.stageMax - f.stageMin;

                    for (int i = 0; i < 6; i++)
                    {
                        float x = 0.5f + 0.5f * MathF.Sin((float)t * (0.00011f + i * 0.00003f) + i * 2.1f), y = 0.5f + 0.5f * MathF.Sin((float)t * (0.00017f + i * 0.00002f) + i * 1.3f);
                        float patch = size.Y * (0.5f + 0.15f * MathF.Sin((float)t * 0.0009f + i));

                        f.stageBack.Add(StageTexture.Glow, f.stageMin + new Vector2(size.X * x, size.Y * y), new Vector2(patch), 0f, level * 0.1f, new Color4(0.45f, 0.8f, 1f, 1f), true);
                    }
                }

                foreach (var bubble in bubbles)
                {
                    if (bubble.Popped)
                        continue;

                    var shell = Bullets[bubble.Index];

                    if (t < shell.T0 || (f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + bubble.Index)))
                        continue;

                    Vector2 at = stageBulletPosition(shell, t);

                    // (hollow: only its rim and a highlight. In its last beats it warns that it is about to pop: it flashes red)
                    double left = bubble.Pops - t;
                    double warn = beat * DanmakusuBalance.WAKASAGI_BUBBLE_WARN_BEATS;
                    float flash = left < warn ? 0.5f + 0.5f * MathF.Cos((float)(left / (beat * 0.5) * Math.Tau)) : 0f;
                    var rim = new Color4(0.85f + 0.15f * flash, 0.95f - 0.7f * flash, 1f - 0.75f * flash, 1f);

                    f.stageRipple.AddRing(at, shell.Radius, 2f, 0.9f, rim);
                    f.stageRipple.AddRing(at, shell.Radius * 0.82f, 1f, 0.3f, rim);
                    f.stageTree.Add(StageTexture.Glow, at + new Vector2(-0.4f, -0.4f) * shell.Radius, new Vector2(shell.Radius * 0.6f), 0f, 0.7f, Color4.White, true);

                    if (flash > 0f)
                        f.stageTree.Add(StageTexture.Glow, at, new Vector2(shell.Radius * 2.8f), 0f, 0.5f * flash, new Color4(1f, 0.2f, 0.2f, 1f), true);
                }
            }
        }

        private readonly List<(double Start, double End)> tideWindows = new List<(double, double)>();

        /// <summary>(Tools/SectionCheck, TRACE_PROBE: how many traces were drawn, summed over the frames, and the longest piece between two of their points, px)</summary>
        public static long TraceProbeCount;

        public static float TraceProbeLongest;

        /// <summary>0..1: how much of an attack is there at <paramref name="t"/>: it comes in as it starts and goes out after it.</summary>
        private float attackPresence(List<(double Start, double End)> windows, double t)
        {
            float level = 0f;

            foreach (var (start, end) in windows)
            {
                if (end > start && t >= start && t <= end + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    level = Math.Max(level, smoothStep((t - start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((end + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS));
            }

            return level;
        }
    }
}
