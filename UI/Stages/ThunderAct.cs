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
        // Iku Nagae (2026-11-24): every second bullet of her rings (the planner's spirals, BulletEmitter.LeavesTrace; IKU_EVERY, the user: "skip every second bullet") leaves a narrow beam behind it, IKU_TRACE_BEATS long - where it has been over that time, a point
        // every IKU_TRACE_STEP_BEATS, fainter towards its end - that hurts like a slider body. Every IKU_TETHER_GROUP neighbouring bullets of those of a ring fired in her section (every second one: 0, 2, 4, 6, 8; 10...) are tied together (the planner's
        // BulletEmitter.TetherGroup: none gets further than half of IKU_TETHER_CIRCLES osu! circles from the middle one of its group, so no two are further apart than that); a straight beam joins each to the
        // next, and hurts like a slider body. A bullet that has been used up or taken, or has left the screen, takes its beams with it.
        // Since 2026-11-25 a group of five is joined as a five-pointed star (every second one to the next, and back), its beams gold all through and twice as thick (IKU_LINK_WIDTH) - the user saw none.
        // All of it is worked out from the rings' closed forms; where each bullet was at the moments of the grid that have come (IKU_TRACE_STEP_BEATS apart) is kept, as that does not change (going back in
        // time drops what is after it): worked out again for every frame it cost 7 ms a frame, and as much again for the autoplay.
        // 2026-11-26, her rework ("All osu objects and sliders including her too - shoots ring of 5 bullets, but they are all connected with straight laserbeams forming a star. Half of the stars are
        // harmless at start for 4 beats, others are harmful, interchanging every 4 beats"): every ring of her section is IKU_STAR_POINTS bullets (the planner: BulletEmitter.StarLinked), joined here by
        // straight gold beams as a five-pointed star that grows as they fly out; in every second stretch of IKU_SWAP_BEATS of her section the beams of half the stars hurt like a slider body and those of
        // the other half are see-through (IKU_HARMLESS_ALPHA) and harmless, then the other way round (BulletEmitter.StarPhase), fading from one to the other. Her own rings no longer curl nor leave beams
        // behind them, and nothing is tied (the traces and ties above are left for no ring now).
        // 2026-11-27 ("bullets are connected by slider laserbeams. By default all ring of 5s form perfect shape stars"): the stars are slider beams - the ring's own colour and edge, as wide as a slider's beam - and every ring is rebuilt as an even circle of five (BulletEmitter.AsEvenRing), also one that was five already.
        // 2026-11-28 ("laserbeams do not disconnect by themselves"): a star stays whole while any of its corners is on the screen - a corner that has left it, hit or been taken stays where its ring has it -
        // and is gone, all of it at once, when none is (a corner leaving the screen broke it there).
        // Later that day ("halve the amount ... Each beam connection has 5 gaps in it"): only every IKU_STAR_EVERY-th ring is a star (the planner; the others stay as they are), and every beam between two
        // corners is in IKU_LINK_GAPS + 1 pieces with gaps between them, IKU_GAP_SHARE of its length in all - drawn, hurting and foreseen so.
        // ======================================================================================================================================================

        private sealed class ThunderAct : StageAct
        {
            /// <summary>Where the bullets of one of her rings were at the moments of the grid since it was fired (as far as they have come), each until it was gone.</summary>
            private sealed class Kept
            {
                public readonly BulletEmitter Ring;
                public readonly long First; // (the first moment of the grid at or after it was fired)
                public readonly List<Vector2>[] Points;
                public readonly bool[] Gone;

                public Kept(BulletEmitter ring, double step)
                {
                    Ring = ring;
                    First = (long)Math.Ceiling(ring.Time / step);
                    Points = new List<Vector2>[ring.Count];
                    Gone = new bool[ring.Count];

                    for (int b = 0; b < ring.Count; b++)
                        Points[b] = new List<Vector2>(); // (the odd ones stay empty: only every IKU_EVERY-th leaves a beam)
                }
            }

            private readonly Kept[] kept;
            private readonly BulletEmitter[] traced;
            private readonly BulletEmitter[] tied;
            private readonly BulletEmitter[] stars; // (2026-11-26: the rings of five joined as stars)

            /// <summary>Do the beams of star <paramref name="ring"/> hurt at <paramref name="t"/>? In every second stretch of IKU_SWAP_BEATS of her section, half the stars in the one, half in the other.</summary>
            private bool harmful(BulletEmitter ring, double t)
            {
                long stretch = (long)Math.Floor((t - Period.ShootStart) / (beat * DanmakusuBalance.IKU_SWAP_BEATS));
                return ((stretch + ring.StarPhase) & 1) == 1;
            }

            /// <summary>
            /// The five points of star <paramref name="ring"/> at <paramref name="t"/>, in the order its beam joins them, into <see cref="line"/> (false: nothing of it). Since 2026-11-28 ("laserbeams do not
            /// disconnect by themselves") a bullet that has left the screen, been used up or been taken keeps its corner where its ring has it - the star is whole while any corner is on the screen, and gone,
            /// all of it, once none is: until then a corner that left the screen (or hit the player, or that another boss's act took) broke it there.
            /// </summary>
            private bool starLine(BulletField f, BulletEmitter ring, double t, double now, List<(int From, int To)> runs)
            {
                line.Clear();
                runs.Clear();
                int runStart = 0;

                // (once all five corners have left the screen, the star is gone, all of it at once: kept on until then, its inner lines swept the whole screen long after - the autoplay was hit 42
                // times in 20 s, nowhere safe)
                bool anyOn = false;

                for (int m = 0; m < 5 && m < ring.Count; m++)
                {
                    if (ring.Time <= now && place(f, ring, m, t, out Vector2 corner) && corner.X >= f.stageMin.X && corner.X <= f.stageMax.X && corner.Y >= f.stageMin.Y && corner.Y <= f.stageMax.Y)
                    {
                        anyOn = true;
                        break;
                    }
                }

                if (!anyOn)
                    return false;

                foreach (int m in star_order)
                {
                    if (ring.Time > now || !place(f, ring, m, t, out Vector2 at))
                    {
                        if (line.Count - runStart >= 2)
                            runs.Add((runStart, line.Count));

                        runStart = line.Count;
                        continue;
                    }

                    line.Add(at);
                }

                if (line.Count - runStart >= 2)
                    runs.Add((runStart, line.Count));

                return runs.Count > 0;
            }

            private readonly List<(int From, int To)> starRuns = new List<(int, int)>();
            private readonly List<(Vector2 A, Vector2 B)> dashList = new List<(Vector2, Vector2)>(32);

            /// <summary>
            /// The pieces of a star's beam from <paramref name="a"/> to <paramref name="b"/> (2026-11-28, "Each beam connection has 5 gaps in it"): IKU_LINK_GAPS gaps evenly along it, IKU_GAP_SHARE of its length
            /// in all; each piece is shortened by its round ends (<paramref name="half"/>), so the gaps are as wide as they are meant to be.
            /// </summary>
            private void dashes(Vector2 a, Vector2 b, float half, List<(Vector2 A, Vector2 B)> into)
            {
                into.Clear();
                Vector2 along = b - a;
                float length = along.Length;

                if (length < 1e-3f)
                    return;

                along /= length;
                int pieces = DanmakusuBalance.IKU_LINK_GAPS + 1;
                float gap = length * DanmakusuBalance.IKU_GAP_SHARE / Math.Max(DanmakusuBalance.IKU_LINK_GAPS, 1);
                float dash = length * (1f - DanmakusuBalance.IKU_GAP_SHARE) / pieces;
                float trim = Math.Min(half, dash * 0.5f - 0.25f);

                for (int i = 0; i < pieces; i++)
                {
                    float from = i * (dash + gap) + Math.Max(trim, 0f), to = i * (dash + gap) + dash - Math.Max(trim, 0f);

                    if (to < from)
                        from = to = i * (dash + gap) + dash * 0.5f;

                    into.Add((a + along * from, a + along * to));
                }
            }
            private readonly List<Vector2> piece = new List<Vector2>(8);
            private readonly double beat;
            private readonly List<Vector2> line = new List<Vector2>(64);
            private readonly List<float> fades = new List<float>(64);
            private readonly MomentShapes shapes = new MomentShapes();
            private readonly GridShapes grid = new GridShapes();

            public ThunderAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var tracedList = new List<BulletEmitter>();
                var tiedList = new List<BulletEmitter>();
                var starList = new List<BulletEmitter>();
                double last = period.End;

                foreach (var ring in f.emitters)
                {
                    if (ring.Time < period.ShootStart || ring.Time >= period.End)
                        continue;

                    if (ring.LeavesTrace && ring.BossRank == period.Rank)
                        tracedList.Add(ring);

                    if (ring.TetherGroup > 1)
                        tiedList.Add(ring);

                    if (ring.StarLinked && ring.Count == DanmakusuBalance.IKU_STAR_POINTS)
                        starList.Add(ring);

                    if (ring.LeavesTrace || ring.TetherGroup > 1 || ring.StarLinked)
                        last = Math.Max(last, ring.Time + ring.Lifetime * 2 + traceMs); // (its life is by its own clock: slowed, it lasts longer)
                }

                traced = tracedList.ToArray();
                tied = tiedList.ToArray();
                stars = starList.ToArray();
                kept = Array.ConvertAll(traced, ring => new Kept(ring, step));
                Start = period.ShootStart;
                End = last;
            }

            private double traceMs => beat * DanmakusuBalance.IKU_TRACE_BEATS;

            private double step => beat * DanmakusuBalance.IKU_TRACE_STEP_BEATS;

            private float traceHalf(BulletField f) => f.circleRadius * DanmakusuBalance.IKU_TRACE_WIDTH;

            private float linkHalf(BulletField f) => f.circleRadius * DanmakusuBalance.IKU_LINK_WIDTH;

            /// <summary>How wide half a beam of a star is: a slider's beam (2026-11-27).</summary>
            private float starHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            public override void Update(BulletField f, double now)
            {
                shapes.Forget();

                if (f.failed)
                    return;

                // (where every bullet was at the moments of the grid that have come)
                foreach (var k in kept)
                {
                    var ring = k.Ring;

                    if (ring.Time > now || now > ring.Time + ring.Lifetime * 2 + traceMs)
                        continue;

                    for (int b = 0; b < ring.Count; b += DanmakusuBalance.IKU_EVERY)
                    {
                        var points = k.Points[b];

                        while (!k.Gone[b] && (k.First + points.Count) * step <= now)
                        {
                            if (!place(f, ring, b, (k.First + points.Count) * step, out Vector2 at))
                            {
                                k.Gone[b] = true;
                                break;
                            }

                            points.Add(at);
                        }
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                shapes.Forget();

                // (what is after the moment gone back to may come out otherwise: the pushes after it are undone)
                foreach (var k in kept)
                {
                    long keep = Math.Max(0, (long)Math.Floor(time / step) - k.First + 1);

                    for (int b = 0; b < k.Points.Length; b++)
                    {
                        if (k.Points[b].Count > keep)
                            k.Points[b].RemoveRange((int)keep, k.Points[b].Count - (int)keep);

                        k.Gone[b] = false;
                    }
                }
            }

            /// <summary>Is bullet <paramref name="bullet"/> of the ring still there (fired, not used up, taken or hidden)?</summary>
            private static bool there(BulletEmitter ring, int bullet, double now)
            {
                if (ring.Time > now || bullet >= 64)
                    return false;

                ulong bit = 1UL << bullet;
                return ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & bit) == 0;
            }

            /// <summary>Where the bullet is at <paramref name="t"/> by the ring's own clock (false: it is gone by then).</summary>
            private static bool place(BulletField f, BulletEmitter ring, int bullet, double t, out Vector2 at)
            {
                double own = ring.WithoutFrozenTime(f.effects.Effective(ring, t));
                ring.Evaluate(bullet, own, out at, out _, out bool alive);
                return alive;
            }

            /// <summary>The beam behind a bullet at <paramref name="t"/>, newest point first, into <see cref="line"/> (and how faint each point is into <see cref="fades"/>).</summary>
            private void trace(BulletField f, Kept k, int bullet, double t)
            {
                line.Clear();
                fades.Clear();

                var ring = k.Ring;
                var points = k.Points[bullet];
                double from = Math.Max(ring.Time, t - traceMs);
                double step = this.step;

                // (one that has left the screen is gone for good, and its beam with it)
                if ((k.Gone[bullet] && t >= (k.First + points.Count) * step) || !place(f, ring, bullet, t, out Vector2 head))
                    return;

                line.Add(head);
                fades.Add(1f);

                if (from >= t - 0.5)
                    return; // (just fired)

                // (on a grid of moments, so that it does not shimmer from frame to frame; the oldest point where it was IKU_TRACE_BEATS ago)
                for (double s = Math.Floor(t / step) * step; ; s -= step)
                {
                    double at = Math.Max(s, from);

                    if (at >= t)
                        continue;

                    // (a moment of the grid that has come: kept; the end of it, and what the autoplay asks about ahead, worked out)
                    long index = (long)Math.Round(at / step) - k.First;
                    Vector2 p;

                    if (at == s && index >= 0 && index < points.Count)
                        p = points[(int)index];
                    else if (at == s && index >= points.Count && (index + k.First) % 4 != 0)
                        continue; // (ahead of what has come, asked about by the autoplay: a point every four steps - every one cost it 2 ms a frame)
                    else if (!place(f, ring, bullet, at, out p))
                        break;

                    line.Add(p);
                    fades.Add(0.25f + 0.75f * (float)((at - from) / Math.Max(t - from, 1)));

                    if (at <= from)
                        break;
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || t < Start || t > End)
                    return;

                // (her colours: a pale blue beam with a deep blue edge behind every bullet, and pale gold ones with a rose edge - her ribbon - between those tied)
                var traceCore = new Color4(0.82f, 0.92f, 1f, 1f);
                var traceBorder = DanmakusuBalance.BorderColour(new Color4(0.3f, 0.36f, 0.9f, 1f));
                var linkCore = new Color4(1f, 0.84f, 0.3f, 1f); // (gold, 2026-11-25: a pale gold with a white middle before)
                var linkBorder = DanmakusuBalance.BorderColour(new Color4(0.9f, 0.3f, 0.45f, 1f));
                float half = traceHalf(f), link = linkHalf(f);

                foreach (var k in kept)
                {
                    var ring = k.Ring;

                    if (ring.Time > t || t > ring.Time + ring.Lifetime * 2 + traceMs)
                        continue;

                    for (int b = 0; b < ring.Count; b += DanmakusuBalance.IKU_EVERY)
                    {
                        if (!there(ring, b, t))
                            continue;

                        trace(f, k, b, t);

                        if (line.Count >= 2)
                            f.addPlainBeam(line, half, traceCore, traceBorder, ref beams, ref pointTotal, null, fades);
                    }
                }

                foreach (var ring in tied)
                    drawLinks(f, ring, t, link, linkCore, linkBorder, ref beams, ref pointTotal);

                // (the stars: gold, solid while they hurt, see-through while they do not - fading between the two over a sixth of a beat)
                double swap = beat * DanmakusuBalance.IKU_SWAP_BEATS, soft = beat / 6;

                foreach (var ring in stars)
                {
                    if (ring.Time > t || t > ring.Time + ring.Lifetime * 2 || !starLine(f, ring, t, t, starRuns))
                        continue;

                    // (slider beams: the ring's own colour and edge, as wide as a slider's beam - 2026-11-27, "bullets are connected by slider laserbeams"; gold, thinner, before)
                    int style = f.styleOf(ring);
                    Color4 starBody = f.palette.Length > 0 ? f.palette[style % f.palette.Length] : linkCore;
                    Color4 starBorder = f.borderPalette.Length > 0 ? f.borderPalette[style % f.borderPalette.Length] : linkBorder;
                    float sliderHalf = starHalf(f);

                    double into = ((t - Period.ShootStart) % swap + swap) % swap;
                    float edge = smoothStep(into / soft) * smoothStep((swap - into) / soft);
                    float solid = harmful(ring, t) ? DanmakusuBalance.IKU_HARMLESS_ALPHA + (1f - DanmakusuBalance.IKU_HARMLESS_ALPHA) * edge : DanmakusuBalance.IKU_HARMLESS_ALPHA + (1f - DanmakusuBalance.IKU_HARMLESS_ALPHA) * (1f - edge);

                    // (every beam between two corners in pieces, with gaps between them: 2026-11-28)
                    foreach (var (from, to) in starRuns)
                    {
                        for (int i = from; i + 1 < to; i++)
                        {
                            dashes(line[i], line[i + 1], sliderHalf, dashList);

                            foreach (var (a, b) in dashList)
                            {
                                piece.Clear();
                                fades.Clear();
                                piece.Add(a);
                                piece.Add(b + (b == a ? new Vector2(0.25f, 0f) : Vector2.Zero));
                                fades.Add(solid);
                                fades.Add(solid);
                                f.addPlainBeam(piece, sliderHalf, starBody, starBorder, ref beams, ref pointTotal, null, fades);
                            }
                        }
                    }
                }
            }

            // (a group of five is joined as a five-pointed star, every second one to the next and back to the first: 2026-11-25, "star-forming laserbeam connections" - they were a chain in ring order)
            private static readonly int[] star_order = { 0, 2, 4, 1, 3, 0 };
            private readonly List<int> order = new List<int>(8);

            /// <summary>The bullets of the group that starts at <paramref name="first"/>, in the order their beam joins them.</summary>
            private List<int> linkOrder(int first, int group, int stride)
            {
                order.Clear();

                if (group == 5)
                {
                    foreach (int m in star_order)
                        order.Add(first + m * stride);
                }
                else
                {
                    for (int m = 0; m < group; m++)
                        order.Add(first + m * stride);
                }

                return order;
            }
            private void drawLinks(BulletField f, BulletEmitter ring, double t, float half, Color4 core, Color4 border, ref int beams, ref int pointTotal)
            {
                if (ring.Time > t || t > ring.Time + ring.Lifetime * 2)
                    return;

                int group = ring.TetherGroup, stride = Math.Max(ring.TetherStride, 1);

                for (int first = 0; first + (group - 1) * stride < ring.Count; first += group * stride)
                {
                    line.Clear();

                    foreach (int b in linkOrder(first, group, stride))
                    {
                        // (a bullet that is gone breaks the beam there)
                        if (!there(ring, b, t) || !place(f, ring, b, t, out Vector2 at))
                        {
                            if (line.Count >= 2)
                                f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, null, core);

                            line.Clear();
                            continue;
                        }

                        line.Add(at);
                    }

                    if (line.Count >= 2)
                        f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, null, core); // (gold all through: with a white middle they passed for nothing - 2026-11-25)
                }
            }

            /// <summary>
            /// Everything of hers that hurts at <paramref name="t"/> into <paramref name="shape"/>, <paramref name="extra"/> px wider - of the tied bullets only the groups whose middle one is within
            /// <paramref name="range"/> of <paramref name="around"/> (where the autoplay looks: working out every group on the screen for every moment it asks about cost 2.4 ms a frame).
            /// </summary>
            private void fill(BulletField f, MomentShapes.Moment shape, double t, float extra, Vector2 around, float range)
            {
                float traceReach = traceHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + extra;
                float linkReach = linkHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + extra;
                double now = f.Time.Current;

                foreach (var k in kept)
                {
                    var ring = k.Ring;

                    if (ring.Time > t || ring.Time > now || t > ring.Time + ring.Lifetime * 2 + traceMs)
                        continue;

                    for (int b = 0; b < ring.Count; b += DanmakusuBalance.IKU_EVERY)
                    {
                        if (!there(ring, b, now))
                            continue;

                        trace(f, k, b, t);

                        if (line.Count < 2)
                            continue;

                        foreach (var p in line)
                            shape.Point(p, traceReach);

                        shape.EndRun();
                    }
                }

                foreach (var ring in tied)
                {
                    if (ring.Time > t || ring.Time > now || t > ring.Time + ring.Lifetime * 2)
                        continue;

                    int group = ring.TetherGroup, stride = Math.Max(ring.TetherStride, 1);
                    float near = range + ring.TetherReach * 0.5f + linkReach;

                    if (!ringNear(f, ring, t, around, near))
                        continue;

                    for (int first = 0; first + (group - 1) * stride < ring.Count; first += group * stride)
                    {
                        // (far from where it looks: not worked out)
                        if (!place(f, ring, first + group / 2 * stride, t, out Vector2 middle) || Vector2.DistanceSquared(middle, around) > near * near)
                            continue;

                        foreach (int b in linkOrder(first, group, stride))
                        {
                            if (!there(ring, b, now) || !place(f, ring, b, t, out Vector2 at))
                            {
                                shape.EndRun();
                                continue;
                            }

                            shape.Point(at, linkReach);
                        }

                        shape.EndRun();
                    }
                }

                // (the stars whose beams hurt then, near where it looks)
                foreach (var ring in stars)
                {
                    float starReach = starHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + extra;

                    if (ring.Time > t || ring.Time > now || t > ring.Time + ring.Lifetime * 2 || !harmful(ring, t) || !starNear(f, ring, t, around, range + starReach)
                        || !starLine(f, ring, t, now, starRuns))
                        continue;

                    float half = starHalf(f);

                    foreach (var (from, to) in starRuns)
                    {
                        for (int i = from; i + 1 < to; i++)
                        {
                            dashes(line[i], line[i + 1], half, dashList);

                            foreach (var (a, b) in dashList)
                            {
                                shape.Point(a, starReach);
                                shape.Point(b, starReach);
                                shape.EndRun();
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Can a bullet of the ring be within <paramref name="reach"/> of <paramref name="around"/> at <paramref name="t"/>? A ring that nothing has turned or pushed has its bullets on a band round where it
            /// was fired, from its slowest to its fastest (the tied ones between those and their middle one); for any other, yes.
            /// </summary>
            private static bool ringNear(BulletField f, BulletEmitter ring, double t, Vector2 around, float reach)
            {
                if (ring.Ops != null || ring.Swirl != null || ring.Curl != 0f || ring.Origins != null || ring.Waves || ring.SlowsToWall || ring.Gravity != 0f || ring.Falling || ring.WrapsOnce
                    || (ring.Deflections != null && ring.Deflections.Count > 0))
                    return true;

                double own = ring.WithoutFrozenTime(f.effects.Effective(ring, t));
                float outer = ring.RadiusAt(own), inner = ring.SlowestRadiusAt(own);
                float d = Vector2.Distance(around, ring.Origin);

                return d + reach >= inner && d - reach <= outer;
            }

            /// <summary>Can a beam of star <paramref name="ring"/> be within <paramref name="reach"/> of <paramref name="around"/> at <paramref name="t"/>? Its beams cross its middle, so: within its outer circle (as <see cref="ringNear"/>, without the inner).</summary>
            private static bool starNear(BulletField f, BulletEmitter ring, double t, Vector2 around, float reach)
            {
                if (ring.Ops != null || ring.Swirl != null || ring.Curl != 0f || ring.Origins != null || ring.Waves || ring.SlowsToWall || ring.Gravity != 0f || ring.Falling || ring.WrapsOnce
                    || (ring.Deflections != null && ring.Deflections.Count > 0))
                    return true;

                double own = ring.WithoutFrozenTime(f.effects.Effective(ring, t));
                return Vector2.Distance(around, ring.Origin) - reach <= ring.RadiusAt(own);
            }

            /// <summary>Does anything of hers hurt a character at <paramref name="player"/> at <paramref name="now"/>? (the hit check: once a frame, at once)</summary>
            private bool touches(BulletField f, double now, Vector2 player)
            {
                float traceReach = traceHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float linkReach = linkHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var k in kept)
                {
                    var ring = k.Ring;

                    if (ring.Time > now || now > ring.Time + ring.Lifetime * 2 + traceMs)
                        continue;

                    for (int b = 0; b < ring.Count; b += DanmakusuBalance.IKU_EVERY)
                    {
                        if (!there(ring, b, now))
                            continue;

                        trace(f, k, b, now);

                        for (int i = 0; i + 1 < line.Count; i++)
                        {
                            if (distanceToSegment(player, line[i], line[i + 1]) <= traceReach)
                                return true;
                        }
                    }
                }

                foreach (var ring in tied)
                {
                    if (ring.Time > now || now > ring.Time + ring.Lifetime * 2)
                        continue;

                    int group = ring.TetherGroup, stride = Math.Max(ring.TetherStride, 1);
                    float near = ring.TetherReach * 0.5f + linkReach;

                    if (!ringNear(f, ring, now, player, near))
                        continue;

                    for (int first = 0; first + (group - 1) * stride < ring.Count; first += group * stride)
                    {
                        if (!place(f, ring, first + group / 2 * stride, now, out Vector2 middle) || Vector2.DistanceSquared(middle, player) > near * near)
                            continue;

                        bool had = false;
                        Vector2 previous = default;

                        foreach (int b in linkOrder(first, group, stride))
                        {
                            if (!there(ring, b, now) || !place(f, ring, b, now, out Vector2 at))
                            {
                                had = false;
                                continue;
                            }

                            if (had && distanceToSegment(player, previous, at) <= linkReach)
                                return true;

                            previous = at;
                            had = true;
                        }
                    }
                }

                // (the stars whose beams hurt now)
                foreach (var ring in stars)
                {
                    float starReach = starHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    if (ring.Time > now || now > ring.Time + ring.Lifetime * 2 || !harmful(ring, now) || !starNear(f, ring, now, player, starReach)
                        || !starLine(f, ring, now, now, starRuns))
                        continue;

                    float half = starHalf(f);

                    foreach (var (from, to) in starRuns)
                    {
                        for (int i = from; i + 1 < to; i++)
                        {
                            if (distanceToSegment(player, line[i], line[i + 1]) > starReach)
                                continue; // (nowhere near this beam at all)

                            dashes(line[i], line[i + 1], half, dashList);

                            foreach (var (a, b) in dashList)
                            {
                                if (distanceToSegment(player, a, b) <= starReach)
                                    return true;
                            }
                        }
                    }
                }

                return false;
            }

            /// <summary>The beams behind her bullets and between those tied hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start || now > End)
                    return false;

                Vector2 player = f.characterPosition;

                // (the autoplay, for many places at a few moments: everything at the nearest moment of a grid, worked out once and kept for a few frames, as much wider as the bullets fly in half a step)
                if (autoAsking)
                {
                    var shape = grid.Get(now, f.Time.Current, out double at, out bool fresh);

                    if (fresh)
                        fill(f, shape, at, stageBulletSpeed(Period) * 2f * (float)(GridShapes.GRID_MS / 2), f.realCharacterPosition, auto_range);

                    return shape.Touches(player);
                }

                return touches(f, now, player);
            }

            /// <summary>(the autoplay looks this far round the character at most, its ways out and the places it may teleport to: px)</summary>
            private const float auto_range = 360f;
        }
    }
}
