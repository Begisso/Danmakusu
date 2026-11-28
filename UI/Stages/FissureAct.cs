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
        // Suika Ibuki: all through her section (in a spinner too) she stomps every four beats, where she stands: a ring goes out over the ground, everything within four circle radii of her (bullets of anyone)
        // is thrown on the way it was going, twice as fast, glowing like magma, with a narrow tail of beam two beats long behind it (it hurts like a slider body), and where it reaches the wall it bursts into four
        // ellipses half its size, fanned out over 90 degrees round the way it would bounce. The stomp hurts the player if they are in it, and thick cracks of magma in the ground, meeting under her, come up at once
        // and fade out over two beats, hurting like a slider body meanwhile.
        // Every sixteen beats a wide crack of glowing magma, two circles across and as jagged as the World's Ender's cut, splits the ground from where she stands towards the player, out past the edge of the screen:
        // its front runs out at an even speed over eight beats (since 2026-11-14; it was out in a beat), as Yuyuko's tree grows, and branches (to one side and the other, the longer ones with a twig) grow out of
        // it as the front gets to them (Hecatia's cut sounds as it starts). It hurts like a slider body, branches too. Eight beats after it has grown it explodes (Hecatia's explosion): for a moment the World's
        // Ender's impact frame turns it and a smooth-edged band round it inside out, in flashes (its stars come round the crack in the flashes of the last SUIKA_STARS_BEFORE_BEATS before, too); glowing
        // magma ellipses fly off both sides all along it, as the World's Ender's bullets do, and it cools away.
        // Since 2026-11-22 every crack is jagged as a crack in the ground (SUIKA_JAG), the stomp's thin ones with branches of their own (SUIKA_THIN_BRANCHES), and drawn as molten rock (the user's second
        // picture): a glow round it, a dark crust along its edges and, inside, a seamless picture of cracked lava (StageLayer.BakeLava) running along it. All the cracks of a frame are drawn layer by
        // layer together, so they run into each other without a seam. Every sound of hers plays twice at once.
        // 2026-11-28: where glows lie over each other the brighter shows, not both (MeshQuad.Brightest: "crack bloom absolute seamless, no overlaps"), and the soft edge of the impact band is three times
        // as wide (SUIKA_IMPACT_FEATHER_PX).
        // 2026-11-24: the jags a little sharper, more and longer branches, each with a twig and the longer ones two; the glow follows the crack's line smoothed (it folded into wedges round the jags' corners);
        // the impact frame follows only the crack's widest bend, in finer pieces, round at both ends (where its band folded over itself it turned back in jagged slivers), and its stars are on and round the
        // crack (they were by the edges of the screen); two ellipses at the wall, not four.
        // 2026-11-25: the lava picture is laid flat over the ground, the same size on every crack and going on across their corners and where they meet (it was laid along each crack: sheared at its
        // jags and as big as the crack was wide); the crust is as thick on every crack (SUIKA_CRUST_CIRCLES); the branches and twigs end in a point; the impact frame's band and stars have soft edges (a
        // strip a pixel and a half wide round them, ImpactStarLayer.AddOutline); the stars come over the last beat before it explodes (two).
        // 2026-11-26: the branches longer (SUIKA_BRANCH_CIRCLES), every one with two twigs and one near its root, the longer ones a fourth near the tip; the band's soft edge 60 px wide (14).
        // ======================================================================================================================================================

        private sealed class FissureAct : StageAct
        {
            private const int crack_points = 13; // (points of each thin crack: 7 until 2026-11-22, when they became jagged)
            private const int fissure_steps = 192; // (96 until 2026-11-22: the jags need more)

            private readonly double beat;
            private readonly double[] stomps;
            private readonly Vector2[] stompAt;
            private readonly bool[] stompKnown;
            private readonly HashSet<(BulletEmitter, int)>[] stompSeen;

            private readonly double[] fissures;
            private readonly Vector2[] fissureFrom;
            private readonly Vector2[] fissureWay;
            private readonly float[] fissureLength;
            private readonly bool[] fissureKnown;
            private readonly bool[] exploded;

            private readonly List<(int Index, double Until)> tails = new List<(int, double)>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private readonly List<Vector2> crack = new List<Vector2>();
            private double resetTime = double.NegativeInfinity;

            public FissureAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.SUIKA_STOMP_EVERY_BEATS)
                    times.Add(at);

                stomps = times.ToArray();
                stompAt = new Vector2[stomps.Length];
                stompKnown = new bool[stomps.Length];
                stompSeen = new HashSet<(BulletEmitter, int)>[stomps.Length];

                for (int k = 0; k < stompSeen.Length; k++)
                    stompSeen[k] = new HashSet<(BulletEmitter, int)>();

                times.Clear();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.SUIKA_FISSURE_EVERY_BEATS)
                    times.Add(at);

                fissures = times.ToArray();
                fissureFrom = new Vector2[fissures.Length];
                fissureWay = new Vector2[fissures.Length];
                fissureLength = new float[fissures.Length];
                fissureKnown = new bool[fissures.Length];
                exploded = new bool[fissures.Length];
                branchesOf = new List<Branch>[fissures.Length];

                for (int k = 0; k < fissures.Length; k++)
                    branchesOf[k] = new List<Branch>();

                End = fissures.Length > 0 ? Math.Max(period.End, explodeAt(fissures.Length - 1) + beat) : period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = stomps.Length > 0 ? stomps[^1] + takeMs : Start - 1;
            }

            private double takeMs => beat * 0.5;

            private double grownAt(int k) => fissures[k] + beat * DanmakusuBalance.SUIKA_FISSURE_GROW_BEATS;

            private double explodeAt(int k) => grownAt(k) + beat * DanmakusuBalance.SUIKA_FISSURE_STAY_BEATS;

            /// <summary>Her last crack explodes after her section: she stays until it has.</summary>
            public override double MechanicsEnd => End;

            private float stompRadius(BulletField f) => f.circleRadius * DanmakusuBalance.SUIKA_STOMP_CIRCLES;

            private float fissureHalf(BulletField f) => f.circleRadius * DanmakusuBalance.SUIKA_FISSURE_CIRCLES;

            private static readonly Color4 magma = new Color4(1f, 0.38f, 0.08f, 1f);
            private static readonly Color4 magma_border = new Color4(0.45f, 0.06f, 0.02f, 1f);

            private int stompAtTime(double t)
            {
                for (int k = 0; k < stomps.Length; k++)
                {
                    if (t >= stomps[k] && t <= stomps[k] + takeMs)
                        return k;
                }

                return -1;
            }

            public override bool Absorbs(double t) => stompAtTime(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double stomp in stomps)
                {
                    if (stomp <= to && stomp + takeMs >= from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                // (what she stomped and blew up is made again: the rings are looked at again from when they were fired, and the explosions come from where the cracks were)
                ClearBullets();
                tails.Clear();
                resetTime = time;

                for (int k = 0; k < stomps.Length; k++)
                {
                    stompSeen[k].Clear();

                    if (stomps[k] > time)
                    {
                        stompKnown[k] = false;
                        piecesOfStomp.Remove(k); // (made again where it comes)
                    }
                }

                for (int k = 0; k < fissures.Length; k++)
                {
                    exploded[k] = false;

                    if (fissures[k] > time)
                    {
                        fissureKnown[k] = false;
                        piecesOfFissure.Remove(k);
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                for (int k = 0; k < stomps.Length && stomps[k] <= now; k++)
                {
                    if (stompKnown[k])
                        continue;

                    stompKnown[k] = true;
                    stompAt[k] = f.bossVisualOf(Period);

                    if (now - stomps[k] < 250 && stomps[k] > resetTime)
                        sound(f, "hecatia_earth_hit");
                }

                for (int k = 0; k < fissures.Length && fissures[k] <= now; k++)
                {
                    if (!fissureKnown[k])
                    {
                        fissureKnown[k] = true;
                        Vector2 from = f.bossVisualOf(Period);
                        Vector2 toPlayer = f.characterPosition - from;
                        Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                        fissureFrom[k] = from;
                        fissureWay[k] = way;
                        fissureLength[k] = (float)f.toWall(from, way, out _) + fissureHalf(f) * 2f; // (out past the edge of the screen)
                        makeBranches(f, k);

                        if (now - fissures[k] < 250 && fissures[k] > resetTime)
                            sound(f, "hecatia_cut");
                    }

                    if (!exploded[k] && now >= explodeAt(k))
                    {
                        exploded[k] = true;
                        explode(f, k);

                        if (now - explodeAt(k) < 250 && explodeAt(k) > resetTime)
                            sound(f, "hecatia_explosion");
                    }
                }
            }

            /// <summary>Every sound of hers plays twice at once (2026-11-14: "all her sounds play double times at the same time").</summary>
            private static void sound(BulletField f, string name)
            {
                f.playStageSound(name);
                f.playStageSound(name);
            }

            // ---- the stomp ----

            /// <summary>What is in the stomp when it comes is thrown on, twice as fast, glowing; at the wall it bursts into four small ellipses.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Sharp || emitter.Shape == BulletShape.Train)
                    return false;

                int k = stompAtTime(t);

                if (k < 0 || !stompKnown[k] || emitter.Time > stomps[k] || !stompSeen[k].Add((emitter, bullet)))
                    return false; // (only what is there when she stomps, the first time it is seen)

                if (Vector2.Distance(to, stompAt[k]) > stompRadius(f))
                    return false;

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                Vector2 way = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : to - stompAt[k];

                if (way.LengthSquared < 1e-8f)
                    way = Vector2.UnitY;

                way = Vector2.Normalize(way);

                float pace = emitter.Ops == null && delta.LengthSquared > 1e-8f ? Math.Max(delta.Length / (float)dt, 0.02f) : stored.Speed;
                Vector2 velocity = way * (pace * DanmakusuBalance.SUIKA_STOMP_SPEED);
                double reach = f.toWall(to, velocity, out bool sideways);
                double wall = t + reach;
                float radius = stored.Radius * big;
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = radius,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_FIRE,
                    TintAmount = 1f,
                    Bloom = DanmakusuBalance.SUIKA_MAGMA_BLOOM,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Untraced = true, // (its flight to the wall is known: the ops of the others leave it be)
                    Until = wall,
                });

                if (Bullets.Count == index)
                    return false; // (too many: it stays as it is)

                tails.Add((index, wall));

                // (at the wall: four ellipses half its size, at the speed it had, fanned out round the way it would bounce)
                Vector2 at = Vector2.Clamp(to + velocity * (float)reach, f.stageMin, f.stageMax);
                Vector2 bounce = sideways ? new Vector2(-way.X, way.Y) : new Vector2(way.X, -way.Y);
                float spread = DanmakusuBalance.SUIKA_WALL_SPREAD_DEGREES * MathF.PI / 180f;
                int pieces = DanmakusuBalance.SUIKA_WALL_PIECES;

                for (int p = 0; p < pieces; p++)
                {
                    Vector2 pieceVelocity = turned(bounce, spread * (p / (float)Math.Max(pieces - 1, 1) - 0.5f)) * pace;
                    float pieceRadius = radius * DanmakusuBalance.SUIKA_WALL_PIECE_SIZE;

                    AddBullet(new StageBullet
                    {
                        T0 = wall,
                        P0 = at,
                        V = pieceVelocity,
                        Radius = pieceRadius,
                        Hit = pieceRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = stored.Style,
                        Shape = BulletShape.Ellipse,
                        Tint = BulletRenderer.TINT_FIRE,
                        TintAmount = 1f,
                        Bloom = DanmakusuBalance.SUIKA_MAGMA_BLOOM * 0.5f,
                        Untraced = true, // (there are many: the ops of the others leave them be, or every one would be traced every frame)
                        Until = wall + f.stageLife(pieceVelocity),
                    });
                }

                return true;
            }

            /// <summary>
            /// Crack <paramref name="c"/> of stomp <paramref name="k"/> (into <see cref="crack"/>, its half width at every point into <see cref="crackWidths"/>): from right under her out to the edge of the stomp,
            /// jagged, unevenly wide and narrowing to its end (all of them meet in one pool of magma under her).
            /// </summary>
            private void thinCrack(BulletField f, int k, int c)
            {
                crack.Clear();
                crackWidths.Clear();

                float reach = stompRadius(f);
                float half = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES;
                float angle = (c + 0.6f * (hash01(k, c, 71) - 0.5f)) * MathF.Tau / DanmakusuBalance.SUIKA_CRACKS + k * 0.9f;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 side = new Vector2(-way.Y, way.X);
                float length = reach * (0.75f + 0.25f * hash01(k, c, 72));

                // (bent in one curve and narrowing evenly - and jagged, as the ground cracks: 2026-11-22, "cracks are more aggressive, representing earth cracks shape"; they were smooth since 2026-11-21)
                float bend = (hash01(k * 31 + c, 1, 73) - 0.5f) * reach * 0.3f;

                for (int i = 0; i < crack_points; i++)
                {
                    float u = i / (crack_points - 1f);
                    float zig = jag(k * 31 + c, u, DanmakusuBalance.SUIKA_THIN_JAG_EVERY) * reach * DanmakusuBalance.SUIKA_THIN_JAG * smoothStep(u / 0.15f);
                    crack.Add(stompAt[k] + way * (length * u) + side * (bend * u * u + zig));
                    crackWidths.Add(half * (1f - 0.8f * u));
                }
            }

            /// <summary>
            /// Branch <paramref name="j"/> of crack <paramref name="c"/> of stomp <paramref name="k"/> (into <see cref="crack"/>; the crack itself must be in <see cref="thinMain"/>): out of it part of the way along, to
            /// one side and then the other, jagged and narrowing to nothing (2026-11-22, "cracks have branches too").
            /// </summary>
            private void thinBranch(BulletField f, int k, int c, int j)
            {
                crack.Clear();
                crackWidths.Clear();

                float along = 0.3f + 0.25f * j + 0.15f * hash01(k * 31 + c, j, 74);
                int at = Math.Clamp((int)MathF.Round(along * (thinMain.Count - 1)), 1, thinMain.Count - 2);
                Vector2 root = thinMain[at];
                Vector2 ahead = thinMain[at + 1] - thinMain[at - 1];
                ahead = ahead.LengthSquared > 1e-6f ? Vector2.Normalize(ahead) : Vector2.UnitX;
                float side = ((c + j) & 1) == 0 ? 1f : -1f;
                Vector2 way = turned(ahead, side * (35f + 20f * hash01(k * 31 + c, j, 75)) * MathF.PI / 180f);
                Vector2 across = new Vector2(-way.Y, way.X);
                float length = stompRadius(f) * (0.22f + 0.18f * hash01(k * 31 + c, j, 76));
                float width = thinMainWidths[at] * 0.6f;
                const int points = 6;

                for (int i = 0; i < points; i++)
                {
                    float u = i / (points - 1f);
                    float zig = jag(k * 311 + c * 7 + j, u, 0.3f) * length * 0.12f * smoothStep(u / 0.25f);
                    crack.Add(root + way * (length * u) + across * zig);
                    crackWidths.Add(width * (1f - u)); // (to a point: 2026-11-25, "why branches are not sharp" - it ended at 15 % of its width, round)
                }
            }

            private readonly Dictionary<int, List<(Vector2 A, Vector2 B, float Width)>> piecesOfStomp = new Dictionary<int, List<(Vector2, Vector2, float)>>();

            /// <summary>Every piece of the cracks of stomp <paramref name="k"/> and of their branches, with its half width (the wider end): made once, when first asked for after the stomp is known.</summary>
            private List<(Vector2 A, Vector2 B, float Width)> stompPieces(BulletField f, int k)
            {
                if (piecesOfStomp.TryGetValue(k, out var known))
                    return known;

                var pieces = new List<(Vector2, Vector2, float)>();

                for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                {
                    thinCrack(f, k, c);
                    keepMain();

                    for (int j = -1; j < DanmakusuBalance.SUIKA_THIN_BRANCHES; j++)
                    {
                        if (j >= 0)
                            thinBranch(f, k, c, j);

                        for (int i = 0; i + 1 < crack.Count; i++)
                            pieces.Add((crack[i], crack[i + 1], Math.Max(crackWidths[i], crackWidths[i + 1])));
                    }
                }

                // (only once its place is known: a stomp not come yet would be kept where it is not)
                if (stompKnown[k])
                    piecesOfStomp[k] = pieces;

                return pieces;
            }

            private readonly List<Vector2> thinMain = new List<Vector2>();
            private readonly List<float> thinMainWidths = new List<float>();

            /// <summary>The crack just made (<see cref="crack"/>) kept, for its branches.</summary>
            private void keepMain()
            {
                thinMain.Clear();
                thinMain.AddRange(crack);
                thinMainWidths.Clear();
                thinMainWidths.AddRange(crackWidths);
            }

            /// <summary>A zigzag of its own (-1 .. 1 across, by <paramref name="seed"/>): straight between corners every <paramref name="every"/> of the way, each at a place of its own - the jags of a crack in the ground.</summary>
            private static float jag(int seed, float u, float every)
            {
                float x = u / Math.Max(every, 1e-3f);
                int i = (int)MathF.Floor(x);
                float a = hash01(seed, i, 131) - 0.5f, b = hash01(seed, i + 1, 131) - 0.5f;
                return (a + (b - a) * (x - i)) * 2f;
            }

            private readonly List<float> crackWidths = new List<float>();

            private double cracksMs => beat * DanmakusuBalance.SUIKA_CRACKS_BEATS;

            // ---- the big crack ----

            /// <summary>How far the line of the crack runs from straight at <paramref name="u"/> (0..1 along it), a share of its length: the World's Ender's cut.</summary>
            /// (2026-11-21: smooth, as a branch of Yuyuko's tree bends - "earthquakes by drawing style look like yuyuko trees"; the fine jags of the World's Ender's cut are gone)
            private static float profile(float u)
                => 0.035f * (0.5f * MathF.Sin(MathF.Tau * 1.3f * u + 0.7f) + 0.3f * MathF.Sin(MathF.Tau * 3.1f * u + 2.1f) + 0.12f * MathF.Sin(MathF.Tau * 5.3f * u + 4.4f));

            /// <summary>The line of crack <paramref name="k"/> at <paramref name="u"/>: its smooth bend and, on it, the jags of a crack in the ground (2026-11-22: "more aggressive, representing earth cracks shape").</summary>
            private Vector2 lineAt(int k, float u)
            {
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                float zig = jag(9000 + k, u, DanmakusuBalance.SUIKA_JAG_EVERY) * fissureLength[k] * DanmakusuBalance.SUIKA_JAG * smoothStep(u / 0.05f);
                return smoothLineAt(k, u) + side * zig;
            }

            /// <summary>The line of crack <paramref name="k"/> without its jags: what the impact frame follows, so that its edge stays smooth (2026-11-22).</summary>
            private Vector2 smoothLineAt(int k, float u)
            {
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                float length = fissureLength[k];
                // (it starts right at her: the bend grows in from nothing)
                return fissureFrom[k] + fissureWay[k] * (length * u) + side * (length * 0.5f * (profile(u) - profile(0f)) * smoothStep(u / 0.05f));
            }

            /// <summary>How wide the crack is on each side at <paramref name="u"/>: two circles across at her, narrowing evenly towards its end as a branch of Yuyuko's tree does (2026-11-21; it was uneven all along).</summary>
            private float widthAt(BulletField f, int k, float u)
                => fissureHalf(f) * (1.15f - 0.6f * u) * (0.7f + 0.3f * smoothStep(u / 0.04f));

            /// <summary>How far along it has grown at <paramref name="t"/> (0..1): its front runs out at an even speed, as Yuyuko's tree grows (2026-11-14; it was out in a beat).</summary>
            private float grownTo(int k, double t) => (float)Math.Clamp((t - fissures[k]) / (grownAt(k) - fissures[k]), 0, 1);

            // ---- the branches of the big crack (2026-11-14, "add subbranches to crack") ----

            private struct Branch
            {
                public float Along; // (where it leaves the crack, 0..1 of its length; or of its branch, for a twig)
                public float Turn; // (radians from the way of what it leaves)
                public float Length; // (px)
                public float Width; // (its half width where it leaves, a share of what it leaves)
                public int Parent; // (-1: it leaves the crack; else the branch it leaves)
                public int Seed;
            }

            private readonly List<Branch>[] branchesOf;

            /// <summary>The branches of crack <paramref name="k"/>: every SUIKA_BRANCH_EVERY of its length, to one side and then the other, at 30-55 degrees forward, each SUIKA_BRANCH_CIRCLES circle radii long or so; the longer ones with a twig.</summary>
            private void makeBranches(BulletField f, int k)
            {
                var list = branchesOf[k];
                list.Clear();

                float length = fissureLength[k];
                float step = f.circleRadius * DanmakusuBalance.SUIKA_BRANCH_EVERY_CIRCLES / Math.Max(length, 1f);
                int number = 0;

                for (float along = step * (0.6f + 0.6f * hash01(k, 0, 101)); along < 0.95f; along += step * (0.7f + 0.6f * hash01(k, number, 102)), number++)
                {
                    float side = number % 2 == 0 ? 1f : -1f;
                    float turn = side * (30f + 25f * hash01(k, number, 103)) * MathF.PI / 180f;
                    float branchLength = f.circleRadius * DanmakusuBalance.SUIKA_BRANCH_CIRCLES * (0.6f + 0.8f * hash01(k, number, 104)) * (1f - 0.4f * along);
                    int parent = list.Count;

                    list.Add(new Branch { Along = along, Turn = turn, Length = branchLength, Width = 0.55f, Parent = -1, Seed = k * 1000 + number });

                    // (a twig off every one, about halfway along, the other way; the longer ones a second, further out, back the way the branch turned - 2026-11-24, "crack branches longer with subbranches":
                    // only the longer ones had one)
                    list.Add(new Branch { Along = 0.35f + 0.2f * hash01(k, number, 105), Turn = -side * (35f + 20f * hash01(k, number, 106)) * MathF.PI / 180f, Length = branchLength * 0.45f, Width = 0.6f, Parent = parent, Seed = k * 1000 + number + 500 });

                    // (since 2026-11-26, "longer branches, more sub-branches": every one has the second twig, and the longer ones a third near the tip, the first way again; one close to the root too)
                    list.Add(new Branch { Along = 0.65f + 0.15f * hash01(k, number, 108), Turn = side * (30f + 20f * hash01(k, number, 109)) * MathF.PI / 180f, Length = branchLength * 0.35f, Width = 0.6f, Parent = parent, Seed = k * 1000 + number + 700 });
                    list.Add(new Branch { Along = 0.12f + 0.1f * hash01(k, number, 110), Turn = side * (40f + 20f * hash01(k, number, 111)) * MathF.PI / 180f, Length = branchLength * 0.3f, Width = 0.6f, Parent = parent, Seed = k * 1000 + number + 800 });

                    if (branchLength > f.circleRadius * DanmakusuBalance.SUIKA_BRANCH_CIRCLES * 0.8f)
                        list.Add(new Branch { Along = 0.85f + 0.07f * hash01(k, number, 112), Turn = -side * (30f + 20f * hash01(k, number, 113)) * MathF.PI / 180f, Length = branchLength * 0.25f, Width = 0.6f, Parent = parent, Seed = k * 1000 + number + 900 });
                }
            }

            /// <summary>
            /// Branch <paramref name="b"/> of crack <paramref name="k"/> as far as it has grown when the crack's front is <paramref name="front"/> px along it (into <see cref="crack"/> and <see cref="crackWidths"/>; it starts
            /// growing when the front gets to where it leaves, and grows as fast): jagged, narrowing to its end. False when nothing of it is there yet.
            /// </summary>
            private bool branchAt(BulletField f, int k, int b, float front)
            {
                crack.Clear();
                crackWidths.Clear();

                var branch = branchesOf[k][b];
                frameOf(f, k, b, out Vector2 root, out Vector2 way, out float rootWidth, out float rootDepth);
                float grown = Math.Min(front - rootDepth, branch.Length);

                if (grown <= 1f)
                    return false;

                const int points = 11; // (6 until 2026-11-22: the jags need more)

                for (int i = 0; i < points; i++)
                {
                    float s = grown * i / (points - 1);
                    crack.Add(pointOf(branch, root, way, s));
                    crackWidths.Add(rootWidth * (1f - s / branch.Length)); // (to a point, 2026-11-25: it ended at a fifth of its width, round)
                }

                return true;
            }

            private struct FissurePiece
            {
                public Vector2 A, B;
                public float Width; // (its half width at its wider end)
                public float From, To; // (how far along the whole crack it starts and ends, px from her)
                public int Branch; // (-1: the crack itself)
            }

            private readonly Dictionary<int, (List<FissurePiece> Pieces, Vector2 Min, Vector2 Max)> piecesOfFissure = new Dictionary<int, (List<FissurePiece>, Vector2, Vector2)>();

            /// <summary>The pieces of crack <paramref name="k"/> and of its branches grown in full, and a box round them with their widths (for hits): made once it is known.</summary>
            private (List<FissurePiece> Pieces, Vector2 Min, Vector2 Max) fissurePieces(BulletField f, int k)
            {
                if (piecesOfFissure.TryGetValue(k, out var known))
                    return known;

                var pieces = new List<FissurePiece>();
                float length = fissureLength[k];
                Vector2 previous = lineAt(k, 0f);

                for (int i = 1; i <= fissure_steps; i++)
                {
                    float u0 = (i - 1f) / fissure_steps, u = i / (float)fissure_steps;
                    Vector2 next = lineAt(k, u);
                    pieces.Add(new FissurePiece { A = previous, B = next, Width = widthAt(f, k, u0), From = u0 * length, To = u * length, Branch = -1 });
                    previous = next;
                }

                const int points = 11; // (as branchAt draws them)

                for (int b = 0; b < branchesOf[k].Count; b++)
                {
                    var branch = branchesOf[k][b];
                    frameOf(f, k, b, out Vector2 root, out Vector2 way, out float rootWidth, out float rootDepth);
                    Vector2 last = root;

                    for (int i = 1; i < points; i++)
                    {
                        float s0 = branch.Length * (i - 1) / (points - 1), s = branch.Length * i / (points - 1);
                        Vector2 point = pointOf(branch, root, way, s);
                        pieces.Add(new FissurePiece { A = last, B = point, Width = rootWidth * (1f - s0 / Math.Max(branch.Length, 1f)), From = rootDepth + s0, To = rootDepth + s, Branch = b });
                        last = point;
                    }
                }

                Vector2 min = new Vector2(float.MaxValue), max = new Vector2(float.MinValue);

                foreach (var piece in pieces)
                {
                    float w = piece.Width * DanmakusuBalance.BULLET_HITBOX_SCALE;
                    min = Vector2.ComponentMin(min, Vector2.ComponentMin(piece.A, piece.B) - new Vector2(w));
                    max = Vector2.ComponentMax(max, Vector2.ComponentMax(piece.A, piece.B) + new Vector2(w));
                }

                var made = (pieces, min, max);

                if (fissureKnown[k])
                    piecesOfFissure[k] = made;

                return made;
            }

            /// <summary>Where branch <paramref name="b"/> of crack <paramref name="k"/> has grown to when the front is <paramref name="front"/> px along the whole crack.</summary>
            private Vector2 branchPoint(BulletField f, int k, int b, float front)
            {
                var branch = branchesOf[k][b];
                frameOf(f, k, b, out Vector2 root, out Vector2 way, out _, out float rootDepth);
                return pointOf(branch, root, way, Math.Clamp(front - rootDepth, 0f, branch.Length));
            }

            /// <summary>Where branch <paramref name="b"/> leaves what it grows from, the way it goes, how wide it is there and how far along the whole crack that is (px from her).</summary>
            private void frameOf(BulletField f, int k, int b, out Vector2 root, out Vector2 way, out float rootWidth, out float rootDepth)
            {
                var branch = branchesOf[k][b];

                if (branch.Parent < 0)
                {
                    float u = branch.Along;
                    root = lineAt(k, u);
                    Vector2 ahead = lineAt(k, Math.Min(u + 0.01f, 1f)) - root;
                    way = turned(ahead.LengthSquared > 1e-6f ? Vector2.Normalize(ahead) : fissureWay[k], branch.Turn);
                    rootWidth = widthAt(f, k, u) * branch.Width;
                    rootDepth = u * fissureLength[k];
                    return;
                }

                // (a twig: on its branch's own jagged line)
                var parent = branchesOf[k][branch.Parent];
                frameOf(f, k, branch.Parent, out Vector2 parentRoot, out Vector2 parentWay, out float parentWidth, out float parentDepth);
                float s = parent.Length * branch.Along;
                root = pointOf(parent, parentRoot, parentWay, s);
                way = turned(parentWay, branch.Turn);
                rootWidth = parentWidth * (1f - branch.Along) * branch.Width;
                rootDepth = parentDepth + s;
            }

            /// <summary>The point <paramref name="s"/> px along a branch: on its line, bent in one smooth curve as a twig of Yuyuko's tree is (2026-11-21; it was jagged, straight between five jags).</summary>
            private static Vector2 pointOf(in Branch branch, Vector2 root, Vector2 way, float s)
            {
                Vector2 side = new Vector2(-way.Y, way.X);
                float u = Math.Clamp(s / Math.Max(branch.Length, 1f), 0f, 1f);
                float bend = (hash01(branch.Seed, 1, 107) - 0.5f) * branch.Length * 0.3f;

                // (jagged too, as the crack: 2026-11-22)
                float zig = jag(branch.Seed, u, 0.2f) * branch.Length * 0.1f * smoothStep(u / 0.2f);
                return root + way * s + side * (bend * u * u + zig);
            }

            /// <summary>The explosion: glowing magma ellipses fly off both sides all along the crack (as the World's Ender's bullets: each at a speed, a size and a way of its own).</summary>
            private void explode(BulletField f, int k)
            {
                double t = explodeAt(k);
                float radius = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period);
                float spread = DanmakusuBalance.ENDER_SPREAD_DEGREES * MathF.PI / 180f;
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                int points = DanmakusuBalance.SUIKA_FISSURE_POINTS;

                for (int i = 0; i < points; i++)
                {
                    float u = (i + 0.5f) / points;
                    Vector2 place = lineAt(k, u);

                    if (place.X < f.stageMin.X || place.X > f.stageMax.X || place.Y < f.stageMin.Y || place.Y > f.stageMax.Y)
                        continue;

                    for (int b = 0; b < DanmakusuBalance.SUIKA_FISSURE_BULLETS * 2; b++)
                    {
                        int number = i * 16 + b;
                        Vector2 outward = b % 2 == 0 ? side : -side;
                        float angle = spread * (hash01(number, k, 81) - 0.5f);
                        float fast = speed * (DanmakusuBalance.ENDER_BURST_SLOWEST + (DanmakusuBalance.ENDER_BURST_FASTEST - DanmakusuBalance.ENDER_BURST_SLOWEST) * hash01(number, k, 82));
                        float size = radius * DanmakusuBalance.ENDER_BULLET_SIZE * (0.7f + 0.7f * hash01(number, k, 83));
                        Vector2 velocity = turned(outward, angle) * fast;
                        double t0 = t + hash01(number, k, 84) * beat * 0.25;

                        AddBullet(new StageBullet
                        {
                            T0 = t0,
                            P0 = place,
                            V = velocity,
                            Radius = size,
                            Hit = size * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = 0,
                            Shape = BulletShape.Ellipse,
                            Tint = BulletRenderer.TINT_FIRE,
                            TintAmount = 1f,
                            Bloom = DanmakusuBalance.ENDER_BULLET_BLOOM,
                            Untraced = true,
                            Until = t0 + f.stageLife(velocity),
                        });
                    }
                }
            }

            // ---- hurting ----

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float me = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // the stomp itself, and its cracks while they glow
                float thinMost = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES * 1.25f + me;

                for (int k = 0; k < stomps.Length && stomps[k] <= now; k++)
                {
                    if (!stompKnown[k] || now > stomps[k] + cracksMs)
                        continue;

                    if (now <= stomps[k] + DanmakusuBalance.SUIKA_STOMP_HURT_MS && Vector2.Distance(player, stompAt[k]) <= stompRadius(f) + me)
                        return true;

                    // (far from all of them: none)
                    if (Vector2.Distance(player, stompAt[k]) > stompRadius(f) * 1.2f + thinMost)
                        continue;

                    // (the cracks and their branches, as pieces made once for the stomp: they stand still, and the autoplay asks hundreds of times a frame - 2026-11-22)
                    foreach (var (a, b, width) in stompPieces(f, k))
                    {
                        if (distanceToSegment(player, a, b) <= width * DanmakusuBalance.BULLET_HITBOX_SCALE + me)
                            return true;
                    }
                }

                // the big crack, as far as it has grown, until it explodes
                for (int k = 0; k < fissures.Length && fissures[k] <= now; k++)
                {
                    if (!fissureKnown[k] || now >= explodeAt(k))
                        continue;

                    // (it and its branches as pieces made once, each with how far along the crack it starts and ends: those the front has passed hurt, the one it is in up to the front - 2026-11-22, the autoplay asked for 192 points of it and every branch hundreds of times a frame)
                    var cut = fissurePieces(f, k);
                    float front = grownTo(k, now) * fissureLength[k];
                    float scale = DanmakusuBalance.BULLET_HITBOX_SCALE;

                    if (player.X < cut.Min.X - me || player.Y < cut.Min.Y - me || player.X > cut.Max.X + me || player.Y > cut.Max.Y + me)
                        continue;

                    foreach (var piece in cut.Pieces)
                    {
                        if (piece.From >= front)
                            continue;

                        Vector2 end = piece.B;

                        if (piece.To > front)
                            end = piece.Branch < 0 ? lineAt(k, front / fissureLength[k]) : branchPoint(f, k, piece.Branch, front);

                        if (distanceToSegment(player, piece.A, end) <= piece.Width * scale + me)
                            return true;
                    }
                }

                // the tails of what she stomped (as tail() makes them, without making them: its head, its end and its widest - 2026-11-22, for the autoplay)
                var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bullets);

                foreach (var (index, until) in tails)
                {
                    ref readonly var bullet = ref all[index];

                    if (now < bullet.T0 || now >= until)
                        continue;

                    double back = Math.Min(now - bullet.T0, beat * DanmakusuBalance.SUIKA_TAIL_BEATS);

                    if (back <= 1)
                        continue;

                    Vector2 head = bullet.P0 + bullet.V * (float)(now - bullet.T0);
                    Vector2 end = bullet.P0 + bullet.V * (float)(now - back - bullet.T0);
                    float reach = bullet.Radius * DanmakusuBalance.SUIKA_TAIL_WIDTH * DanmakusuBalance.BULLET_HITBOX_SCALE * 0.6f + me;

                    if (Math.Abs(player.X - head.X) > Math.Abs(head.X - end.X) + reach || Math.Abs(player.Y - head.Y) > Math.Abs(head.Y - end.Y) + reach)
                        continue;

                    if (distanceToSegment(player, head, end) <= reach && !(DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        return true;
                }

                return false;
            }

            /// <summary>The tail of a stomped bullet at <paramref name="t"/> (into <see cref="line"/>, from its head back, with the widths): two beats of the way it has come, narrowing to a point.</summary>
            private bool tail(BulletField f, in StageBullet bullet, double t, double until)
            {
                line.Clear();
                widths.Clear();

                double back = Math.Min(t - bullet.T0, beat * DanmakusuBalance.SUIKA_TAIL_BEATS);

                if (back <= 1)
                    return false;

                Vector2 head = bullet.P0 + bullet.V * (float)(t - bullet.T0);
                Vector2 end = bullet.P0 + bullet.V * (float)(t - back - bullet.T0);
                float half = bullet.Radius * DanmakusuBalance.SUIKA_TAIL_WIDTH;
                const int points = 5;

                for (int i = 0; i < points; i++)
                {
                    float u = i / (points - 1f);
                    line.Add(Vector2.Lerp(head, end, u));
                    widths.Add(half * (1f - 0.9f * u));
                }

                return true;
            }

            // ---- drawing ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || tails.Count == 0)
                    return;

                foreach (var (index, until) in tails)
                {
                    var bullet = Bullets[index];

                    if (t < bullet.T0 || t >= until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    if (tail(f, bullet, t, until))
                        f.addPlainBeam(line, widths[0], magma, magma_border, ref beams, ref pointTotal, widths);
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                // the stomps: a ring over the ground, and the thin cracks fading out
                for (int k = 0; k < stomps.Length && stomps[k] <= t; k++)
                {
                    if (!stompKnown[k] || t > stomps[k] + cracksMs)
                        continue;

                    double age = t - stomps[k];
                    float ring = smoothStep(age / 220.0);
                    float radius = stompRadius(f);

                    // (twice as far and half as thick since 2026-11-21)
                    if (age < 400)
                        f.stageRipple.AddSoftRing(stompAt[k], radius * (0.3f + 0.7f * ring), Math.Max(f.circleRadius * 0.125f, 1.5f), Math.Max(f.circleRadius * 0.075f, 1f), 0.8f * (1f - smoothStep(age / 400.0)), new Color4(1f, 0.7f, 0.4f, 1f));

                    float glow = Math.Min(smoothStep(age / 60.0), 1f - smoothStep(age / cracksMs));

                    for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                    {
                        thinCrack(f, k, c);
                        addBand(crack, crackWidths, glow, k * 977 + c * 31, true); // (they all meet under her: their glows come in from there, so they do not lie over each other)
                        keepMain();

                        // (and their branches, coming in from where they leave them)
                        for (int j = 0; j < DanmakusuBalance.SUIKA_THIN_BRANCHES; j++)
                        {
                            thinBranch(f, k, c, j);
                            addBand(crack, crackWidths, glow, k * 977 + c * 31 + 7 + j, true);
                        }
                    }
                }

                // the big cracks
                for (int k = 0; k < fissures.Length && fissures[k] <= t; k++)
                {
                    if (!fissureKnown[k] || t > explodeAt(k) + beat * 0.5)
                        continue;

                    float grown = grownTo(k, t);
                    float heat = t < explodeAt(k) ? 0.75f + 0.25f * smoothStep((t - grownAt(k)) / (explodeAt(k) - grownAt(k))) : 1f - smoothStep((t - explodeAt(k)) / (beat * 0.5));

                    crack.Clear();
                    crackWidths.Clear();

                    for (int i = 0; i <= fissure_steps; i++)
                    {
                        float u = Math.Min(i / (float)fissure_steps, grown);
                        crack.Add(lineAt(k, u));
                        crackWidths.Add(widthAt(f, k, u));

                        if (u >= grown)
                            break;
                    }

                    addBand(crack, crackWidths, heat, 50000 + k * 131);

                    // (its branches, growing as its front gets to them; their glows come in from where they leave it, so they do not lie over its glow)
                    float front = grown * fissureLength[k];

                    for (int b = 0; b < branchesOf[k].Count; b++)
                    {
                        if (branchAt(f, k, b, front))
                            addBand(crack, crackWidths, heat, 60000 + k * 131 + b * 7, true);
                    }

                    // (the explosion flashes along it)
                    double flash = (t - explodeAt(k)) / (beat * 0.5);

                    if (flash >= 0 && flash < 1)
                        f.stageFront.Add(StageTexture.Glow, lineAt(k, 0.5f), new Vector2(fissureLength[k] * 1.2f), 0f, 0.7f * (float)((1 - flash) * (1 - flash)), new Color4(1f, 0.6f, 0.25f, 1f), true);

                    impactFrame(f, k, t);
                }

                drawBands(f);

                // (what glows like magma glows by its own bloom only, 2026-11-21, "blooms and glows should be seamless": a soft light sprite of its own under every one, added up where they lay over each
                // other, made blotches)
            }

            // ---- the magma (2026-11-14: thick, as the user's picture of a crack in cooling lava; one seamless piece) ----

            // (the cracks of this frame, all drawn at once layer by layer: every point with its side (mitred, so the pieces of a crack meet without a seam or a gap), its half width and its vein's width)
            private readonly List<Vector2> bandPoints = new List<Vector2>();
            private readonly List<Vector2> bandSides = new List<Vector2>();
            private readonly List<float> bandWidths = new List<float>();
            private readonly List<float> bandVeins = new List<float>();
            private readonly List<float> bandGlows = new List<float>();
            private readonly List<(int Start, int Count, float Heat)> bandRuns = new List<(int, int, float)>();

            /// <summary>
            /// Keeps a crack through <paramref name="points"/> (half as wide as <paramref name="halfWidths"/> says at each point), <paramref name="heat"/> bright, for <see cref="drawBands"/>. A crack that
            /// <paramref name="growsOut"/> of another (a branch, or one of the stomp's, which all meet under her) has no glow where it leaves it, coming in over twice its width: the glows never lie over each
            /// other there (2026-11-14, "blooms should be seamless without overlaps").
            /// </summary>
            private void addBand(List<Vector2> points, List<float> halfWidths, float heat, int seed, bool growsOut = false)
            {
                if (points.Count < 2 || heat <= 0.01f)
                    return;

                int start = bandPoints.Count;
                float travelled = 0f, comeIn = halfWidths[0] * 4f;

                for (int i = 0; i < points.Count; i++)
                {
                    // (the side at a corner is between the sides of the two pieces, longer so that the band keeps its width: at most twice as long at a sharp corner)
                    Vector2 before = i > 0 ? points[i] - points[i - 1] : points[1] - points[0];
                    Vector2 after = i + 1 < points.Count ? points[i + 1] - points[i] : before;
                    Vector2 sideBefore = before.LengthSquared > 1e-8f ? Vector2.Normalize(new Vector2(-before.Y, before.X)) : Vector2.UnitY;
                    Vector2 sideAfter = after.LengthSquared > 1e-8f ? Vector2.Normalize(new Vector2(-after.Y, after.X)) : sideBefore;
                    Vector2 side = sideBefore + sideAfter;
                    side = side.LengthSquared > 1e-6f ? Vector2.Normalize(side) : sideAfter;
                    float stretch = Math.Min(1f / Math.Max(Vector2.Dot(side, sideAfter), 0.5f), 2f);

                    if (i > 0)
                        travelled += before.Length;

                    bandPoints.Add(points[i]);
                    bandSides.Add(side * stretch);
                    bandWidths.Add(halfWidths[i]);
                    bandVeins.Add(0.04f + 0.08f * hash01(seed, i, 91)); // (the white-hot vein in the middle, unevenly wide: half as wide since 2026-11-14)
                    bandGlows.Add(growsOut ? smoothStep(travelled / Math.Max(comeIn, 1f)) : 1f);
                }

                bandRuns.Add((start, points.Count, heat));
            }

            /// <summary>
            /// Every crack kept this frame, as molten rock: a soft orange glow round it, a dark crust along its edges, orange inside going yellow, and a white-hot vein down the middle. Each layer of every crack is
            /// drawn before the next layer of any (the glows, then the crusts, then the insides), so where cracks meet or cross their insides run together and no edge lies across them.
            /// </summary>
            private void drawBands(BulletField f)
            {
                if (bandRuns.Count == 0)
                    return;

                crustPx = f.circleRadius * DanmakusuBalance.SUIKA_CRUST_CIRCLES;

                // (from the middle out, as shares of the half width: where each layer begins and ends, and its colour at its inner and outer edge. Inside the crust it is lava - the lava picture, end to end
                // without a seam: 2026-11-22, "magma crack has texture inside, seamless"; it was orange going yellow, with a white-hot vein)
                for (int layer = 0; layer < 3; layer++)
                {
                    foreach (var (start, count, heat) in bandRuns)
                    {
                        if (layer == 2)
                        {
                            lavaRun(f, start, count, heat);
                            continue;
                        }

                        // (the glow: along a smoothed line, 2026-11-24 "blooms seamless like mima moon" - round the jags' sharp corners it folded over itself in wedges)
                        if (layer == 0)
                        {
                            glowRun(f, start, count, heat);
                            continue;
                        }

                        Color4 near, far;
                        float from, to;
                        bool vein = false;

                        switch (layer)
                        {
                            case 0: // (the glow over the ground round it)
                                from = 1f;
                                to = 1.75f;
                                near = new Color4(1f, 0.36f, 0.06f, 0.42f * heat);
                                far = new Color4(0.9f, 0.22f, 0.04f, 0f);
                                break;

                            default: // (the dark crust: half as thick since 2026-11-14; as thick everywhere since 2026-11-25, SUIKA_CRUST_CIRCLES - from the lava's edge at each point, set below)
                                from = 0f;
                                to = 1f;
                                near = new Color4(0.42f, 0.09f, 0.03f, heat);
                                far = new Color4(0.12f, 0.035f, 0.02f, heat);
                                break;
                        }

                        for (int i = start + 1; i < start + count; i++)
                        {
                            Vector2 a = bandPoints[i - 1], b = bandPoints[i];
                            Vector2 sa = bandSides[i - 1] * (vein ? bandWidths[i - 1] * bandVeins[i - 1] : bandWidths[i - 1]);
                            Vector2 sb = bandSides[i] * (vein ? bandWidths[i] * bandVeins[i] : bandWidths[i]);
                            Color4 inner = near;

                            // (the glow of a crack that grows out of another comes in from where it leaves it)
                            if (layer == 0)
                            {
                                float glow = Math.Min(bandGlows[i - 1], bandGlows[i]);

                                if (glow < 0.01f)
                                    continue;

                                inner.A *= glow;
                            }

                            float fromA = layer == 1 ? lavaShare(i - 1) : from, fromB = layer == 1 ? lavaShare(i) : from;
                            f.stageMeshes.Add(new MeshQuad { A = a + sa * fromA, B = b + sb * fromB, C = a + sa * to, D = b + sb * to, Near = inner, Far = far });
                            f.stageMeshes.Add(new MeshQuad { A = a - sa * fromA, B = b - sb * fromB, C = a - sa * to, D = b - sb * to, Near = inner, Far = far });
                        }

                        // (round ends, as a branch of Yuyuko's tree has - 2026-11-21: the layer goes on round the end in a half circle; not where a crack grows out of another, which it starts inside)
                        roundEnd(f, start + count - 1, start + count - 2, layer == 1 ? lavaShare(start + count - 1) : from, to, near, far, vein, layer == 0);

                        if (bandGlows[start] >= 0.999f)
                            roundEnd(f, start, start + 1, layer == 1 ? lavaShare(start) : from, to, near, far, vein, layer == 0);
                    }
                }

                bandPoints.Clear();
                bandSides.Clear();
                bandWidths.Clear();
                bandVeins.Clear();
                bandGlows.Clear();
                bandRuns.Clear();
            }

            private readonly List<Vector2> glowPoints = new List<Vector2>();
            private readonly List<Vector2> glowSides = new List<Vector2>();
            private readonly List<float> glowArc = new List<float>();

            /// <summary>
            /// The soft glow round one crack (band points <paramref name="start"/> on), as Mima's moon has round it (2026-11-24): over the ground from its middle out to GLOW_OUT of its half width, the
            /// crack's line smoothed over about its half width on each side first, so that it bends gently where the crack has its jags - nothing of the glow lies over itself, and it has no edge but its
            /// soft outer one. It is under the crack: the crust and the lava cover its middle.
            /// </summary>
            private void glowRun(BulletField f, int start, int count, float heat)
            {
                glowPoints.Clear();
                glowSides.Clear();
                glowArc.Clear();

                float arc = 0f;

                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        arc += Vector2.Distance(bandPoints[start + i], bandPoints[start + i - 1]);

                    glowArc.Add(arc);
                }

                // (a weighted average of the points within its half width along the crack; the two ends stay where they are, as the round ends do)
                for (int i = 0; i < count; i++)
                {
                    if (i == 0 || i == count - 1)
                    {
                        glowPoints.Add(bandPoints[start + i]);
                        continue;
                    }

                    float reach = Math.Max(bandWidths[start + i], 1f);
                    Vector2 sum = Vector2.Zero;
                    float weights = 0f;

                    for (int j = i; j >= 0 && glowArc[i] - glowArc[j] <= reach; j--)
                    {
                        float w = 1f - (glowArc[i] - glowArc[j]) / reach;
                        sum += bandPoints[start + j] * w;
                        weights += w;
                    }

                    for (int j = i + 1; j < count && glowArc[j] - glowArc[i] <= reach; j++)
                    {
                        float w = 1f - (glowArc[j] - glowArc[i]) / reach;
                        sum += bandPoints[start + j] * w;
                        weights += w;
                    }

                    glowPoints.Add(weights > 0f ? sum / weights : bandPoints[start + i]);
                }

                for (int i = 0; i < count; i++)
                {
                    Vector2 ahead = glowPoints[Math.Min(i + 1, count - 1)] - glowPoints[Math.Max(i - 1, 0)];
                    glowSides.Add(ahead.LengthSquared > 1e-8f ? Vector2.Normalize(new Vector2(-ahead.Y, ahead.X)) : bandSides[start + i]);
                }

                var near = new Color4(1f, 0.36f, 0.06f, 0.42f * heat);
                var far = new Color4(0.9f, 0.22f, 0.04f, 0f);

                for (int i = 1; i < count; i++)
                {
                    float glow = Math.Min(bandGlows[start + i - 1], bandGlows[start + i]);

                    if (glow < 0.01f)
                        continue;

                    Color4 inner = near;
                    inner.A *= glow;
                    Vector2 a = glowPoints[i - 1], b = glowPoints[i];
                    Vector2 sa = glowSides[i - 1] * bandWidths[start + i - 1], sb = glowSides[i] * bandWidths[start + i];

                    // (where glows lie over each other - two cracks crossing, a glow and its round end - the brighter shows, not both: 2026-11-28, "crack bloom absolute seamless, no overlaps")
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        f.stageMeshes.Add(new MeshQuad { A = a, B = b, C = a + sa * sign, D = b + sb * sign, Near = inner, Far = inner, Brightest = true }); // (under the crack)
                        f.stageMeshes.Add(new MeshQuad { A = a + sa * sign, B = b + sb * sign, C = a + sa * (sign * glow_out), D = b + sb * (sign * glow_out), Near = inner, Far = far, Brightest = true });
                    }
                }

                // (round at its ends: the glow goes on round the end in a half circle - not where a crack grows out of another)
                roundEnd(f, start + count - 1, start + count - 2, 0f, 1f, near, near, false, true);
                roundEnd(f, start + count - 1, start + count - 2, 1f, glow_out, near, far, false, true);

                if (bandGlows[start] >= 0.999f)
                {
                    roundEnd(f, start, start + 1, 0f, 1f, near, near, false, true);
                    roundEnd(f, start, start + 1, 1f, glow_out, near, far, false, true);
                }
            }

            /// <summary>How far out the glow goes, a share of the crack's half width from its middle (1.75 from the edge of its jags until 2026-11-24).</summary>
            private const float glow_out = 1.9f;

            /// <summary>How thick the crust is this frame (px): the same on every crack (2026-11-25; it was 13 % of the crack's half width).</summary>
            private float crustPx;

            /// <summary>How far out from the middle the lava of a crack goes at band point <paramref name="i"/> (a share of its half width): to the crust, nothing where the crack is no wider than the crust is thick.</summary>
            private float lavaShare(int i) => bandWidths[i] > crustPx ? 1f - crustPx / bandWidths[i] : 0f;

            /// <summary>
            /// The lava of one crack (band points <paramref name="start"/> on): the lava picture laid flat over the ground (2026-11-25, "magma texture is inconsistently splitting and stretching": it was laid along
            /// the crack, as big as the crack was wide, so it was sheared across the pieces at every jag, of another size on every crack and cut where one crack met another). Every piece is cut where the
            /// picture starts again, so that each corner has its own place on it and no piece goes over its edge; round at its ends, as the rest of the crack.
            /// </summary>
            private void lavaRun(BulletField f, int start, int count, float heat)
            {
                // (the picture is twice as wide as it is tall)
                float tileWidth = f.circleRadius * DanmakusuBalance.SUIKA_LAVA_TILE_CIRCLES, tileHeight = tileWidth * 0.5f;
                var colour = new Color4(1f, 1f, 1f, heat);

                for (int i = start + 1; i < start + count; i++)
                {
                    Vector2 a = bandPoints[i - 1], b = bandPoints[i];
                    Vector2 sa = bandSides[i - 1] * (bandWidths[i - 1] * lavaShare(i - 1)), sb = bandSides[i] * (bandWidths[i] * lavaShare(i));

                    if (sa.LengthSquared < 1e-6f && sb.LengthSquared < 1e-6f)
                        continue;

                    // (its corners round the piece: down one side, back along the other)
                    lavaPolygon.Clear();
                    lavaPolygon.Add(a - sa);
                    lavaPolygon.Add(a + sa);
                    lavaPolygon.Add(b + sb);
                    lavaPolygon.Add(b - sb);
                    lavaPiece(f, colour, tileWidth, tileHeight);
                }

                // (the round ends: the dark rock of the lava, under the crust - not where a crack grows out of another)
                var rock = new Color4(0.22f, 0.06f, 0.03f, heat);
                roundEnd(f, start + count - 1, start + count - 2, 0f, lavaShare(start + count - 1), rock, rock, false, false);

                if (bandGlows[start] >= 0.999f)
                    roundEnd(f, start, start + 1, 0f, lavaShare(start), rock, rock, false, false);
            }

            private readonly List<Vector2> lavaPolygon = new List<Vector2>();
            private readonly List<Vector2> lavaClipped = new List<Vector2>();

            /// <summary>The piece of lava in <see cref="lavaPolygon"/>, cut by every repeat of the picture it lies on, each part with its corners' places on the picture.</summary>
            private void lavaPiece(BulletField f, Color4 colour, float tileWidth, float tileHeight)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

                foreach (var p in lavaPolygon)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                int fromX = (int)MathF.Floor(minX / tileWidth), toX = Math.Min((int)MathF.Floor(maxX / tileWidth), fromX + 4);
                int fromY = (int)MathF.Floor(minY / tileHeight), toY = Math.Min((int)MathF.Floor(maxY / tileHeight), fromY + 4);

                for (int ty = fromY; ty <= toY; ty++)
                {
                    for (int tx = fromX; tx <= toX; tx++)
                    {
                        float left = tx * tileWidth, top = ty * tileHeight;

                        lavaClipped.Clear();
                        lavaClipped.AddRange(lavaPolygon);
                        clipBy(0, left, 1f);
                        clipBy(0, left + tileWidth, -1f);
                        clipBy(1, top, 1f);
                        clipBy(1, top + tileHeight, -1f);

                        if (lavaClipped.Count < 3)
                            continue;

                        Vector2 uvOf(Vector2 p) => new Vector2(Math.Clamp((p.X - left) / tileWidth, 0f, 1f), Math.Clamp((p.Y - top) / tileHeight, 0f, 1f));

                        // (a fan from its first corner, two triangles to a piece; C, A, B, D go round a piece)
                        Vector2 first = lavaClipped[0];

                        for (int j = 1; j + 1 < lavaClipped.Count; j += 2)
                        {
                            Vector2 p1 = lavaClipped[j], p2 = lavaClipped[j + 1], p3 = j + 2 < lavaClipped.Count ? lavaClipped[j + 2] : first;

                            f.stageMeshes.Add(new MeshQuad
                            {
                                C = first, A = p1, B = p2, D = p3, Near = colour, Far = colour,
                                Lava = true, UvC = uvOf(first), UvA = uvOf(p1), UvB = uvOf(p2), UvD = uvOf(p3),
                            });
                        }
                    }
                }
            }

            private readonly List<Vector2> clipScratch = new List<Vector2>();

            /// <summary>Cuts <see cref="lavaClipped"/> by a line across x (<paramref name="axis"/> 0) or y (1) at <paramref name="at"/>, keeping what is on its <paramref name="keep"/> side (1 above it, -1 below).</summary>
            private void clipBy(int axis, float at, float keep)
            {
                if (lavaClipped.Count == 0)
                    return;

                clipScratch.Clear();

                for (int i = 0; i < lavaClipped.Count; i++)
                {
                    Vector2 p = lavaClipped[i], q = lavaClipped[(i + 1) % lavaClipped.Count];
                    float dp = ((axis == 0 ? p.X : p.Y) - at) * keep, dq = ((axis == 0 ? q.X : q.Y) - at) * keep;

                    if (dp >= 0f)
                        clipScratch.Add(p);

                    if ((dp >= 0f) != (dq >= 0f))
                        clipScratch.Add(Vector2.Lerp(p, q, dp / (dp - dq)));
                }

                lavaClipped.Clear();
                lavaClipped.AddRange(clipScratch);
            }

            /// <summary>One layer of the round end of a crack at band point <paramref name="end"/> (the point before it is <paramref name="before"/>): a half circle beyond it, between the layer's shares of its width.</summary>
            private void roundEnd(BulletField f, int end, int before, float from, float to, Color4 near, Color4 far, bool vein, bool glow)
            {
                Vector2 p = bandPoints[end];
                Vector2 forward = p - bandPoints[before];

                if (forward.LengthSquared < 1e-8f)
                    return;

                forward = Vector2.Normalize(forward);
                Vector2 side = new Vector2(-forward.Y, forward.X);
                float w = vein ? bandWidths[end] * bandVeins[end] : bandWidths[end];
                Color4 inner = near;

                if (glow)
                    inner.A *= bandGlows[end];

                const int segments = 10;

                for (int j = 0; j < segments; j++)
                {
                    float a0 = MathF.PI * j / segments, a1 = MathF.PI * (j + 1) / segments;
                    Vector2 d0 = side * MathF.Cos(a0) + forward * MathF.Sin(a0), d1 = side * MathF.Cos(a1) + forward * MathF.Sin(a1);
                    f.stageMeshes.Add(new MeshQuad { A = p + d0 * (w * from), B = p + d1 * (w * from), C = p + d0 * (w * to), D = p + d1 * (w * to), Near = inner, Far = far, Brightest = glow });
                }
            }

            /// <summary>
            /// The World's Ender's impact frame on the big crack when it explodes (2026-11-14, "World End's Impact frames on crack and slightly around it"): for a moment, in flashes, the crack and a jagged band
            /// round it are turned inside out (black where it was white, everything under it), its edges jumping to new places at every flash, as the stars of her impact frame do.
            /// </summary>
            private void impactFrame(BulletField f, int k, double t)
            {
                double since = t - explodeAt(k);

                // (before it explodes, its stars come round it already, flickering, over the last SUIKA_STARS_BEFORE_BEATS: 2026-11-22, "impact frame stars should appear around cracks before explosion too")
                double before = beat * DanmakusuBalance.SUIKA_STARS_BEFORE_BEATS;

                if (since < 0 && since >= -before)
                {
                    int warning = (int)Math.Floor(since / DanmakusuBalance.SUIKA_IMPACT_FLASH_MS);

                    if ((warning & 1) == 0)
                    {
                        impactStarsOf(f, k, warning);
                        f.impactStars.ShapesDone();
                    }

                    return;
                }

                if (since < 0 || since >= DanmakusuBalance.SUIKA_IMPACT_MS)
                    return;

                // (on and off: a flash, a gap as long, and so on)
                int flash = (int)(since / DanmakusuBalance.SUIKA_IMPACT_FLASH_MS);

                if (flash % 2 == 1)
                    return;

                // (its edge follows a smooth line along the crack, not its jags: 2026-11-22, "impact frame black white area edge is smooth". 2026-11-24, "way smoother edges": the band is so wide that
                // on the inside of the crack's smaller bends its edge folded over itself, and where pieces lie over each other the frame turns back - jagged slivers. It now follows only the crack's widest
                // bend (no bend of it is tighter than the band is wide), in finer pieces, as wide everywhere as the crack narrows, and is round at both ends, as the crack is)
                const int steps = 160;
                Vector2 previousPoint = bandLineAt(k, 0f), previousSide = Vector2.Zero;
                float previousWidth = 0f;
                Vector2 firstSide = Vector2.Zero, lastSide = Vector2.Zero;
                bandLeft.Clear();
                bandRight.Clear();

                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    Vector2 point = bandLineAt(k, u);
                    Vector2 ahead = bandLineAt(k, Math.Min(u + 0.5f / steps, 1f)) - bandLineAt(k, Math.Max(u - 0.5f / steps, 0f));
                    Vector2 side = ahead.LengthSquared > 1e-6f ? Vector2.Normalize(new Vector2(-ahead.Y, ahead.X)) : new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                    float width = bandWidthAt(f, k, u);

                    if (i > 0)
                        f.impactStars.AddPiece(previousPoint + previousSide * previousWidth, point + side * width, previousPoint - previousSide * previousWidth, point - side * width);
                    else
                        firstSide = side;

                    bandLeft.Add(point + side * width);
                    bandRight.Add(point - side * width);
                    lastSide = side;
                    previousPoint = point;
                    previousSide = side;
                    previousWidth = width;
                }

                bandCap(f, bandLineAt(k, 0f), firstSide, bandWidthAt(f, k, 0f), -1f);
                bandCap(f, bandLineAt(k, 1f), lastSide, bandWidthAt(f, k, 1f), 1f);

                // (its outline, for its soft edge - 2026-11-25, "why explosion impact frame area edges are not smooth as asked several times": a shape's edge falls between the pixels of the screen in
                // steps unless it is softened over a pixel or so. Up one side, round the far end, back down the other side and round the near end, through the very corners of its pieces)
                bandOutline.Clear();
                bandOutline.AddRange(bandLeft);
                capOutline(bandLineAt(k, 1f), lastSide, bandWidthAt(f, k, 1f), 1f);

                for (int i = bandRight.Count - 1; i >= 0; i--)
                    bandOutline.Add(bandRight[i]);

                capOutline(bandLineAt(k, 0f), firstSide, bandWidthAt(f, k, 0f), -1f);
                f.impactStars.AddOutline(bandOutline, DanmakusuBalance.SUIKA_IMPACT_FEATHER_PX); // (a wide soft edge: 2026-11-25, the same day - 1.5 px was not seen; three times as wide since 2026-11-28)

                impactStarsOf(f, k, flash);
                f.impactStars.ShapesDone();
            }

            private readonly List<Vector2> bandLeft = new List<Vector2>(), bandRight = new List<Vector2>(), bandOutline = new List<Vector2>();

            /// <summary>The arc of a round end of the impact frame's band (as <see cref="bandCap"/> makes it, without its two ends, which are the sides' last points) onto <see cref="bandOutline"/>, from the left side round.</summary>
            private void capOutline(Vector2 at, Vector2 side, float width, float forward)
            {
                const int segments = 24; // (as bandCap)
                Vector2 ahead = new Vector2(side.Y, -side.X) * forward;

                // (the far end goes from the left side round to the right; the near end, coming back up the right side, from the right round to the left)
                for (int j = 1; j < segments; j++)
                {
                    float a = MathF.PI * (forward > 0f ? j : segments - j) / segments;
                    bandOutline.Add(at + (side * MathF.Cos(a) + ahead * MathF.Sin(a)) * width);
                }
            }

            /// <summary>The line the impact frame follows (2026-11-24): the crack's widest bend only, gentle enough for the band round it not to fold over itself.</summary>
            private Vector2 bandLineAt(int k, float u)
            {
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                float length = fissureLength[k];
                static float wide(float v) => 0.035f * 0.5f * MathF.Sin(MathF.Tau * 1.3f * v + 0.7f);
                return fissureFrom[k] + fissureWay[k] * (length * u) + side * (length * 0.5f * (wide(u) - wide(0f)) * smoothStep(u / 0.05f));
            }

            /// <summary>How wide the impact frame's band is on each side at <paramref name="u"/>: as the crack narrows, without the kink of its first few px.</summary>
            private float bandWidthAt(BulletField f, int k, float u) => fissureHalf(f) * (1.15f - 0.6f * u) * DanmakusuBalance.SUIKA_IMPACT_MARGIN;

            /// <summary>A round end of the impact frame's band: a half circle beyond <paramref name="at"/>, the way <paramref name="forward"/> says (-1 back, 1 on), in pieces that do not lie over each other.</summary>
            private static void bandCap(BulletField f, Vector2 at, Vector2 side, float width, float forward)
            {
                const int segments = 24;
                Vector2 ahead = new Vector2(side.Y, -side.X) * forward; // (the band's way, from its side)

                for (int j = 0; j < segments; j++)
                {
                    float a0 = MathF.PI * j / segments, a1 = MathF.PI * (j + 1) / segments;
                    Vector2 d0 = side * MathF.Cos(a0) + ahead * MathF.Sin(a0), d1 = side * MathF.Cos(a1) + ahead * MathF.Sin(a1);
                    f.impactStars.AddPiece(at, at + d0 * width, at, at + d1 * width); // (a triangle: the first and the third corner are the same)
                }
            }

            /// <summary>The stars of the impact frame, as the World's Ender's: beside the crack, to one side and then the other, each in a place of its own at every flash (2026-11-21).</summary>
            private void impactStarsOf(BulletField f, int k, int flash)
            {
                for (int s = 0; s < DanmakusuBalance.SUIKA_IMPACT_STARS; s++)
                {
                    float u = (s + 0.3f + 0.4f * hash01(k * 17 + s, flash, 121)) / DanmakusuBalance.SUIKA_IMPACT_STARS;
                    Vector2 ahead = smoothLineAt(k, Math.Min(u + 0.01f, 1f)) - smoothLineAt(k, Math.Max(u - 0.01f, 0f));
                    Vector2 side = ahead.LengthSquared > 1e-6f ? Vector2.Normalize(new Vector2(-ahead.Y, ahead.X)) : new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                    // (on the crack or close round it, 2026-11-24: "impact frame stars should be on cracks or around, not on playfield edges" - they were beyond the band, by the edges of the screen)
                    float crackWidth = widthAt(f, k, u);
                    float size = crackWidth * (1.6f + 1.2f * hash01(k * 17 + s, flash, 122));
                    Vector2 at = lineAt(k, u) + side * ((s % 2 == 0 ? 1f : -1f) * crackWidth * 2.2f * hash01(k * 17 + s, flash, 124));

                    f.addImpactStar(at, new Vector2(size, size * (0.8f + 0.4f * hash01(k * 17 + s, flash, 123))), k * 64 + s, flash);
                }
            }
        }
    }
}
