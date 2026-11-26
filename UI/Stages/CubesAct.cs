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
        // Yachie Kicchou (2026-11-26): no pauses.
        // Her rings: every YACHIE_RING_EVERY_BEATS a ring of YACHIE_RING_ARROWS arrowheads from where she is, flying off its own way (by a hash: "shoots randomly") at YACHIE_RING_SPEED a plain bullet's
        // speed, straight (they do not home). The ring opens out from its middle to YACHIE_RING_CIRCLES circle radii and closes in to its middle again every YACHIE_BREATHE_BEATS, half out and half in,
        // as a cosine (StageBullet.OrbitBreathe), all the way; every arrowhead's body turns round once every YACHIE_SPIN_BEATS, every ring one way or the other (by a hash).
        // Her lattice ("big cube pattern", the user's picture: a rhombille tiling - three rhombi to a hexagon, seen as cubes): one after another, standing where she was when it came, edges
        // YACHIE_CUBE_CIRCLES circle radii long, across the whole screen, a vertical edge in every cube as in the picture. It shows see-through and harmless for YACHIE_PREVIEW_BEATS (YACHIE_PREVIEW_ALPHA),
        // then turns solid from its middle out over YACHIE_ON_BEATS, a front running out to past the farthest corner of the screen (with Byakuren's beam sound), then goes again from its middle out over
        // YACHIE_OFF_BEATS, as Mai's snowflake does; the fronts are soft over YACHIE_FRONT_CIRCLES circle radii. When it is all gone the next one shows. It is drawn as one piece, so its joints are
        // seamless; where it is solid it hurts like a slider body.
        // ======================================================================================================================================================

        private sealed class CubesAct : StageAct
        {
            private readonly double beat;

            // ---- the rings ----

            private readonly double[] ringTimes;
            private readonly List<Vector2> ringFrom = new List<Vector2>(); // (where she was as each was shot, taken then and kept)
            private int ringsMade;

            // ---- the lattices ----

            private readonly double[] starts;
            private int placed = -1;
            private Vector2 centre;
            private float reach; // (from its middle to past the farthest corner of the screen)
            private int sounded = -1;
            private double resetTime = double.NegativeInfinity;

            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();

            private const int edge_steps = 6; // (every edge is drawn in this many pieces, so that the fronts are soft along it)

            public CubesAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var rings = new List<double>();

                for (double t = period.ShootStart; t < period.End && rings.Count < 10000; t += beat * DanmakusuBalance.YACHIE_RING_EVERY_BEATS)
                    rings.Add(t);

                ringTimes = rings.ToArray();

                var list = new List<double>();
                double cycle = previewMs + onMs + offMs;

                // (one shows as long as it can turn solid whole in her section; the last one goes after it)
                for (double t = period.ShootStart; t + previewMs + onMs <= period.End && list.Count < 10000; t += cycle)
                    list.Add(t);

                if (list.Count == 0)
                    list.Add(period.ShootStart);

                starts = list.ToArray();
                End = Math.Max(starts[^1] + cycle, period.End + beat * 32);
            }

            private double previewMs => beat * DanmakusuBalance.YACHIE_PREVIEW_BEATS;
            private double onMs => beat * DanmakusuBalance.YACHIE_ON_BEATS;
            private double offMs => beat * DanmakusuBalance.YACHIE_OFF_BEATS;
            private double cycleMs => previewMs + onMs + offMs;

            /// <summary>She stays until her last lattice is gone.</summary>
            public override double MechanicsEnd => starts[^1] + cycleMs;

            public override void Reset(BulletField f, double time)
            {
                // (the rings shot by then keep where they were shot from; their arrowheads are made again)
                int keep = 0;

                while (keep < ringFrom.Count && ringTimes[keep] <= time)
                    keep++;

                ringFrom.RemoveRange(keep, ringFrom.Count - keep);
                ringsMade = 0;
                ClearBullets();

                placed = -1;
                sounded = -1;
                resetTime = time;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every ring whose time has come, from where she is)
                while (ringFrom.Count < ringTimes.Length && ringTimes[ringFrom.Count] <= now)
                    ringFrom.Add(f.bossPositionOf(Period));

                for (; ringsMade < ringFrom.Count; ringsMade++)
                    makeRing(f, ringsMade);

                // (the lattice there now: placed where she is when it shows)
                int k = latticeAt(now);

                if (k >= 0 && k != placed)
                {
                    placed = k;
                    centre = f.bossPositionOf(Period);
                    reach = 0f;

                    foreach (var corner in new[] { f.stageMin, f.stageMax, new Vector2(f.stageMin.X, f.stageMax.Y), new Vector2(f.stageMax.X, f.stageMin.Y) })
                        reach = Math.Max(reach, Vector2.Distance(corner, centre));

                    reach += f.circleRadius * DanmakusuBalance.YACHIE_FRONT_CIRCLES + halfWidth(f);
                }

                // (it turns solid with Byakuren's beam sound)
                if (k >= 0 && k > sounded && now >= starts[k] + previewMs)
                {
                    sounded = k;
                    double solid = starts[k] + previewMs;

                    if (now - solid < 250 && solid > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            // ---- the rings ----

            private void makeRing(BulletField f, int k)
            {
                double t0 = ringTimes[k];
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.YACHIE_ARROW_SIZE;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.YACHIE_RING_SPEED;
                float way = hash01(Id, k, 171) * MathF.Tau;
                Vector2 v = new Vector2(MathF.Cos(way), MathF.Sin(way)) * speed;
                float ring = f.circleRadius * DanmakusuBalance.YACHIE_RING_CIRCLES;
                float spin = (hash01(Id, k, 172) < 0.5f ? -1f : 1f) * MathF.Tau / (float)(beat * DanmakusuBalance.YACHIE_SPIN_BEATS);
                double until = t0 + f.stageLife(v) + ring / Math.Max(speed, 1e-4f);
                int count = DanmakusuBalance.YACHIE_RING_ARROWS;

                for (int i = 0; i < count; i++)
                {
                    float phase = i * MathF.Tau / count;

                    AddBullet(new StageBullet
                    {
                        T0 = t0,
                        P0 = ringFrom[k],
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(k + i, true, Period.Rank),
                        Shape = BulletShape.Triangle,
                        Orbit = ring,
                        OrbitPhase = phase,
                        OrbitBreathe = beat * DanmakusuBalance.YACHIE_BREATHE_BEATS,
                        HubCentre = ringFrom[k],
                        HubUntil = t0,
                        HubV = v,
                        Spin = spin,
                        SpinPhase = phase, // (pointing out of the ring as it comes)
                        Until = until,
                    });
                }
            }

            // ---- the lattice ----

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>The lattice that is there at <paramref name="t"/> (from when it shows until it is all gone), or -1.</summary>
            private int latticeAt(double t)
            {
                int k = (int)Math.Floor((t - Start) / cycleMs);
                return k >= 0 && k < starts.Length && t >= starts[k] && t < starts[k] + cycleMs ? k : -1;
            }

            /// <summary>
            /// How lattice <paramref name="k"/> is <paramref name="distance"/> px from its middle at <paramref name="t"/>: how solid (0 see-through .. 1 solid, as the first front has passed) and how much of it is
            /// still there (1 .. 0, as the second front passes).
            /// </summary>
            private void stateAt(BulletField f, int k, double t, float distance, out float solid, out float there)
            {
                float soft = f.circleRadius * DanmakusuBalance.YACHIE_FRONT_CIRCLES;
                double on = starts[k] + previewMs, off = on + onMs;
                float first = reach * (float)Math.Clamp((t - on) / onMs, 0, 1);
                float second = reach * (float)Math.Clamp((t - off) / offMs, 0, 1);

                solid = t < on ? 0f : smoothStep((first - distance) / soft);
                there = t < off ? 1f : 1f - smoothStep((second - distance) / soft);
            }

            /// <summary>The edge length, and the lattice's two steps: up one edge, and to the right one row (60 degrees round from it).</summary>
            private static void steps(BulletField f, out float edge, out Vector2 e1, out Vector2 e2)
            {
                edge = f.circleRadius * DanmakusuBalance.YACHIE_CUBE_CIRCLES;
                e1 = new Vector2(0f, edge);
                e2 = new Vector2(edge * MathF.Sqrt(3f) * 0.5f, edge * 0.5f);
            }

            /// <summary>The six neighbours of a corner where six rhombi meet (every third corner of the triangular lattice; the others are where three meet, and no edge joins two of those).</summary>
            private static readonly (int Di, int Dj)[] neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1), (-1, 1), (1, -1) };

            private static bool sixfold(int i, int j) => ((i - j) % 3 + 3) % 3 == 0;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                int k = latticeAt(t);

                if (k < 0 || k != placed)
                    return;

                steps(f, out float edge, out Vector2 e1, out Vector2 e2);
                float half = halfWidth(f);
                float shows = smoothStep((t - starts[k]) / beat); // (the preview fades in over a beat)
                var core = new Color4(1f, 0.96f, 0.86f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.82f, 0.22f, 0.2f, 1f)); // (her red)
                int piece = ++f.beamPieces; // (the whole lattice drawn layer by layer together: its joints are seamless)

                // (every corner where six meet, over the screen and an edge round it: its six edges)
                Vector2 low = f.stageMin - new Vector2(edge * 1.5f), high = f.stageMax + new Vector2(edge * 1.5f);
                float column = e2.X;
                int jFrom = (int)MathF.Floor((low.X - centre.X) / column), jTo = (int)MathF.Ceiling((high.X - centre.X) / column);

                for (int j = jFrom; j <= jTo; j++)
                {
                    float x = centre.X + j * column;
                    float yOffset = centre.Y + j * e2.Y;
                    int iFrom = (int)MathF.Floor((low.Y - yOffset) / edge), iTo = (int)MathF.Ceiling((high.Y - yOffset) / edge);

                    for (int i = iFrom; i <= iTo; i++)
                    {
                        if (!sixfold(i, j))
                            continue;

                        Vector2 a = new Vector2(x, yOffset + i * edge);

                        foreach (var (di, dj) in neighbours)
                        {
                            Vector2 b = a + e1 * di + e2 * dj;

                            line.Clear();
                            fades.Clear();
                            bool any = false;

                            for (int s = 0; s <= edge_steps; s++)
                            {
                                Vector2 p = Vector2.Lerp(a, b, s / (float)edge_steps);
                                stateAt(f, k, t, Vector2.Distance(p, centre), out float solid, out float there);
                                float alpha = there * (DanmakusuBalance.YACHIE_PREVIEW_ALPHA * shows + (1f - DanmakusuBalance.YACHIE_PREVIEW_ALPHA * shows) * solid);
                                line.Add(p);
                                fades.Add(alpha);
                                any |= alpha > 0.01f;
                            }

                            if (!any)
                                continue;

                            f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, fades);
                            f.laserInstances[beams - 1].Piece = piece;
                        }
                    }
                }
            }

            /// <summary>Where the lattice is solid it hurts like a slider body: the edges round the player, at the place nearest to them.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                int k = latticeAt(now);

                if (k < 0 || k != placed || now < starts[k] + previewMs)
                    return false;

                steps(f, out float edge, out Vector2 e1, out Vector2 e2);
                Vector2 player = f.characterPosition;
                float reachOut = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (the corners where six meet near the player: their edges are the only ones that can touch it)
                int j0 = (int)MathF.Round((player.X - centre.X) / e2.X);

                for (int j = j0 - 2; j <= j0 + 2; j++)
                {
                    float yOffset = centre.Y + j * e2.Y;
                    int i0 = (int)MathF.Round((player.Y - yOffset) / edge);

                    for (int i = i0 - 2; i <= i0 + 2; i++)
                    {
                        if (!sixfold(i, j))
                            continue;

                        Vector2 a = new Vector2(centre.X + j * e2.X, yOffset + i * edge);

                        if (Vector2.DistanceSquared(a, player) > (edge + reachOut) * (edge + reachOut))
                            continue;

                        foreach (var (di, dj) in neighbours)
                        {
                            Vector2 b = a + e1 * di + e2 * dj;
                            Vector2 along = b - a;
                            float u = Math.Clamp(Vector2.Dot(player - a, along) / along.LengthSquared, 0f, 1f);
                            Vector2 nearest = a + along * u;

                            if (Vector2.Distance(player, nearest) > reachOut)
                                continue;

                            stateAt(f, k, now, Vector2.Distance(nearest, centre), out float solid, out float there);

                            if (solid >= 0.5f && there >= 0.5f)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
