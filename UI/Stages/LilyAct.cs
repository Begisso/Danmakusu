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
        // Lily White: from the four corners of the screen a branch grows inwards (as big as a sub-branch of Yuyuko's tree, with its own sub-branches, drawn by the beam renderer like her wood; it
        // hurts like a slider body). In her attacks every bullet that leaves the screen at a side grows a sakura flower (half as often as Yuyuko's, as fast) on the branches, evenly spread, and eight beats after it
        // started to grow the flower bursts: its five petals fly away as pink petal bullets. In her pauses no flower grows, but the ones that are there still go on growing and burst on time. When she goes the branches
        // grow backwards until they are gone (a flower that has not burst by then bursts as they go).
        // ======================================================================================================================================================

        private sealed class LilyAct : StageAct
        {
            private sealed class Branch
            {
                public Vector2[] Points = []; // (in units of the tree's size, from the place the branch's anchor is at)
                public float[] Arc = [];
                public float[] Width = [];
                public float[] Heading = []; // (the direction at each point: 0 is up, positive turns right)
                public float Length;
                public float Begin, Finish; // (when it starts and stops growing, 0..1 of the growth)
                public int Depth;
                public int Anchor;
            }

            private sealed class Flower
            {
                public Vector2 At; // (field coordinates)
                public float Length; // (how long a petal is, in units of the tree's size)
                public float Turn;
                public int Key;
                public double Born;
                public double Burst;
                public bool Done;
            }

            /// <summary>Where the branches start (as a part of the width and the height of the playfield) and which way they grow (0 is up, positive turns right): the four corners, all inwards.</summary>
            private static readonly (float X, float Y, float Heading)[] anchors =
            {
                (0f, 0f, 3f * MathF.PI / 4f), (1f, 0f, -3f * MathF.PI / 4f), (1f, 1f, -MathF.PI / 4f), (0f, 1f, MathF.PI / 4f),
            };

            private static readonly float[] along_places = { 0.3f, 0.5f, 0.7f, 0.9f }; // (where on a branch a flower can sit)

            private const int max_depth = 5;
            private static readonly int[] child_counts = { 4, 3, 2, 2, 2, 0 };
            private static readonly float[] bends = { 0.45f, 0.9f, 1.0f, 1.1f, 1.2f, 1.2f };
            private static readonly float[] spreads = { 0.95f, 0.85f, 0.75f, 0.7f, 0.65f, 0f };
            private static readonly float[] length_ratios = { 0.8f, 0.78f, 0.78f, 0.78f, 0.78f, 0f };

            private readonly List<(double Start, double End)> attacks;
            private readonly double nextStart;
            private Branch[] branches = [];
            private readonly List<Flower> flowers = new List<Flower>();
            private readonly HashSet<int> sown = new HashSet<int>();
            private int liveFrom; // (every flower before this one has burst)
            private double lastUpdate = double.MinValue;
            private readonly Vector2[] revealPoints = new Vector2[48];
            private readonly float[] revealWidths = new float[48];
            private readonly float[] revealArc = new float[48];

            /// <summary>When the branches are gone: they grow backwards from the end of her sections at the speed they grew (or until the next ones start).</summary>
            public double Gone;

            public LilyAct(BossPeriod owner, double start, double end, List<(double Start, double End)> attacks, double nextStart)
            {
                Period = owner;
                Start = start;
                End = end;
                this.attacks = attacks;
                this.nextStart = nextStart;
            }

            private double beat => Math.Max(Period.BeatLength, 1.0);

            private double growMs => DanmakusuBalance.YUYUKO_TREE_GROW_BEATS * beat; // (always this long: a short stay only grows the branches part of the way)

            private static float treeSize(BulletField f) => DanmakusuBalance.YUYUKO_TREE_HEIGHT * (f.stageMax.Y - f.stageMin.Y);

            private static Vector2 anchorAt(BulletField f, int anchor)
                => new Vector2(f.stageMin.X + anchors[anchor].X * (f.stageMax.X - f.stageMin.X), f.stageMin.Y + anchors[anchor].Y * (f.stageMax.Y - f.stageMin.Y));

            private float growth(double t)
            {
                if (t <= Start)
                    return 0f;

                if (t <= End)
                    return (float)Math.Min((t - Start) / growMs, 1.0);

                float top = (float)Math.Min((End - Start) / growMs, 1.0);

                return (float)Math.Max(top - (t - End) / growMs, 0.0);
            }

            public override void Init(BulletField f)
            {
                float top = (float)Math.Min((End - Start) / growMs, 1.0);
                Gone = Math.Min(End + top * growMs, nextStart);

                var list = new List<Branch>();
                int n = 0;
                float rnd() => hash01(Id, n++, 21);

                void grow(int anchor, Vector2 start, float heading, float length, float width0, int depth, float begin)
                {
                    int k = Math.Clamp((int)(length / 0.025f), 6, 22);
                    float bend = (rnd() - 0.5f) * 2f * bends[depth];
                    float wobble = (rnd() - 0.5f) * 2f * 0.35f;
                    float phase = rnd() * MathF.Tau;
                    float knots = 0.05f + 0.06f * rnd();
                    float knotPhase = rnd() * MathF.Tau;
                    float step = length / k;
                    float h = heading;
                    float endRatio = depth == max_depth ? 0.05f : 0.5f;

                    var branch = new Branch
                    {
                        Points = new Vector2[k + 1],
                        Arc = new float[k + 1],
                        Width = new float[k + 1],
                        Heading = new float[k + 1],
                        Length = length,
                        Begin = begin,
                        Finish = begin + length,
                        Depth = depth,
                        Anchor = anchor,
                    };

                    branch.Points[0] = start;
                    branch.Heading[0] = h;

                    for (int i = 1; i <= k; i++)
                    {
                        float s0 = (i - 1f) / k, s1 = (float)i / k;

                        // (an arc and a wobble on top of it: a branch is never straight)
                        h += bend / k + wobble * (MathF.Sin(phase + s1 * 5.4f) - MathF.Sin(phase + s0 * 5.4f));
                        branch.Points[i] = branch.Points[i - 1] + new Vector2(MathF.Sin(h), -MathF.Cos(h)) * step;
                        branch.Heading[i] = h;
                        branch.Arc[i] = branch.Arc[i - 1] + step;
                    }

                    for (int i = 0; i <= k; i++)
                    {
                        float s = branch.Arc[i] / length;
                        float w = width0 * (1f + (endRatio - 1f) * s);

                        w *= 1f + knots * MathF.Sin(knotPhase + s * 11f) * (i == 0 ? 0f : 1f);
                        branch.Width[i] = w;
                    }

                    list.Add(branch);

                    int children = child_counts[depth];
                    float lastSide = rnd() < 0.5f ? -1f : 1f;
                    bool first = true;

                    for (int j = 0; j < children; j++)
                    {
                        bool tip = j == children - 1;
                        float fork = tip ? 1f : 0.3f + (0.95f - 0.3f) * (j + 0.5f) / Math.Max(children - 1, 1) + (rnd() - 0.5f) * 0.06f;
                        fork = Math.Clamp(fork, 0.3f, 0.97f);

                        // (one to either side in turn)
                        float side = first ? lastSide : -lastSide;
                        first = false;
                        lastSide = side;

                        float spread = spreads[depth] * (0.85f + 0.3f * rnd()) * (tip ? 0.4f : 1f);
                        float childLength = length * length_ratios[depth] * (0.9f + 0.2f * rnd());
                        Vector2 forkPoint = pointAlong(branch, fork * length, out _);

                        grow(anchor, forkPoint, headingAlong(branch, fork * length) + side * spread, childLength, widthAlong(branch, fork * length) * (0.62f + 0.18f * rnd()), depth + 1, begin + length * fork);
                    }
                }

                // every anchor grows one branch the size of a sub-branch of Yuyuko's tree (two levels below its trunk), with all the branches that come out of it
                for (int a = 0; a < anchors.Length; a++)
                    grow(a, Vector2.Zero, anchors[a].Heading, DanmakusuBalance.LILY_BRANCH_LENGTH * (0.9f + 0.2f * rnd()), DanmakusuBalance.LILY_BRANCH_WIDTH, 2, 0f);

                // the whole growth is spread over the first part of the time (the rest is for the first flowers)
                float finish = 0f;

                foreach (var branch in list)
                    finish = Math.Max(finish, branch.Finish);

                float scale = DanmakusuBalance.YUYUKO_WOOD_DONE / Math.Max(finish, 1e-3f);

                foreach (var branch in list)
                {
                    branch.Begin *= scale;
                    branch.Finish *= scale;
                }

                branches = list.ToArray();
            }

            private static int segmentAt(Branch branch, float s)
            {
                int i = 1;

                while (i < branch.Arc.Length - 1 && branch.Arc[i] < s)
                    i++;

                return i;
            }

            private static Vector2 pointAlong(Branch branch, float s, out int segment)
            {
                segment = segmentAt(branch, s);
                float span = branch.Arc[segment] - branch.Arc[segment - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[segment - 1]) / span, 0f, 1f) : 0f;

                return Vector2.Lerp(branch.Points[segment - 1], branch.Points[segment], u);
            }

            private static float widthAlong(Branch branch, float s)
            {
                int i = segmentAt(branch, s);
                float span = branch.Arc[i] - branch.Arc[i - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[i - 1]) / span, 0f, 1f) : 0f;

                return branch.Width[i - 1] + (branch.Width[i] - branch.Width[i - 1]) * u;
            }

            private static float headingAlong(Branch branch, float s)
            {
                int i = segmentAt(branch, s);
                float span = branch.Arc[i] - branch.Arc[i - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[i - 1]) / span, 0f, 1f) : 0f;

                return branch.Heading[i - 1] + (branch.Heading[i] - branch.Heading[i - 1]) * u;
            }

            /// <summary>The part of a branch that has grown (<paramref name="p"/>, 0..1 of its length), in field coordinates, with its half widths in pixels, in the scratch arrays (a sharp tip while it grows, as Yuyuko's). Returns how many points there are.</summary>
            private int reveal(Branch branch, float p, float size, Vector2 origin)
            {
                float reach = Math.Clamp(p, 0f, 1f) * branch.Length;
                int n = 0;
                int all = branch.Points.Length;

                for (int i = 0; i < all && n < revealPoints.Length - 1; i++)
                {
                    if (branch.Arc[i] > reach + 1e-6f)
                        break;

                    revealPoints[n] = branch.Points[i];
                    revealWidths[n] = branch.Width[i];
                    revealArc[n] = branch.Arc[i];
                    n++;
                }

                if (n < all && n >= 1 && reach > revealArc[n - 1] + 1e-5f)
                {
                    float span = branch.Arc[n] - branch.Arc[n - 1];
                    float u = span > 1e-6f ? (reach - branch.Arc[n - 1]) / span : 0f;

                    revealPoints[n] = Vector2.Lerp(branch.Points[n - 1], branch.Points[n], u);
                    revealWidths[n] = branch.Width[n - 1] + (branch.Width[n] - branch.Width[n - 1]) * u;
                    revealArc[n] = reach;
                    n++;
                }

                float tip = Math.Max(0.12f * branch.Length, 0.03f);
                float relax = p * p * p;

                for (int i = 0; i < n; i++)
                {
                    float taper = Math.Clamp((reach - revealArc[i]) / tip, 0.03f, 1f);
                    float factor = taper + (1f - taper) * relax;

                    revealWidths[i] = Math.Max(revealWidths[i] * factor * size, 0.35f);
                    revealPoints[i] = origin + revealPoints[i] * size;
                }

                return n;
            }

            private static float progressOf(Branch branch, float u) => Math.Min((u - branch.Begin) / Math.Max(branch.Finish - branch.Begin, 1e-4f), 1f);

            /// <summary>Adds the wood at time <paramref name="t"/> to the beams that are drawn.</summary>
            public void AddWood(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                float u = growth(t);

                if (t < Start || t >= Gone || u <= 0f)
                    return;

                float size = treeSize(f);

                foreach (var branch in branches)
                {
                    float p = progressOf(branch, u);

                    if (p <= 0f)
                        continue;

                    int n = reveal(branch, p, size, anchorAt(f, branch.Anchor));

                    if (n >= 2)
                        f.addWoodBeam(revealPoints, revealWidths, n, ref beams, ref pointTotal);
                }
            }

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            private float petalScale(double t, double born) => smoothStep((t - born) / (DanmakusuBalance.YUYUKO_PETAL_GROW_BEATS * beat));

            /// <summary>A bullet (number <paramref name="bullet"/> of ring <paramref name="ring"/>) has gone out of the screen at <paramref name="position"/> at time <paramref name="t"/>: in her attacks a flower grows for it (once per bullet) on the branches, evenly spread.</summary>
            public void Sow(BulletField f, int ring, int bullet, double t, Vector2 position)
            {
                if (t < Start || t >= Gone || flowers.Count >= DanmakusuBalance.LILY_MAX_FLOWERS)
                    return;

                // (one that goes out in a pause brings its flower when the next attack starts: it does not wait for it outside the screen)
                if (!inAttack(t))
                {
                    double next = double.PositiveInfinity;

                    foreach (var (start, end) in attacks)
                    {
                        if (end > start && start > t)
                            next = Math.Min(next, start);
                    }

                    if (next >= Gone)
                        return;

                    t = next;
                }

                int key = ring * 4096 + bullet;

                // (only part of the bullets bring a flower: half as many as Yuyuko's)
                if (hash01(Id, key, 61) >= DanmakusuBalance.YUYUKO_SOW_SHARE * DanmakusuBalance.LILY_SOW_FACTOR || !sown.Add(key))
                    return;

                // evenly: of a few places on the branches (picked by the bullet's number) the one farthest from the flowers that are there now is taken, so the flowers spread over all the branches
                float size = treeSize(f);
                float bestGap = -1f;
                int bestBranch = 0;
                float bestAlong = 0f;
                Vector2 bestPlace = default;

                for (int c = 0; c < DanmakusuBalance.LILY_FLOWER_CANDIDATES; c++)
                {
                    int b = Math.Min((int)(hash01(Id, key * 16 + c, 63) * branches.Length), branches.Length - 1);
                    float along = along_places[Math.Min((int)(hash01(Id, key * 16 + c, 64) * along_places.Length), along_places.Length - 1)];
                    var branch = branches[b];

                    // (a short stay does not grow the branches all the way: no flower where they never get to)
                    if (Start + (branch.Begin + (branch.Finish - branch.Begin) * along) * growMs > End)
                        continue;

                    Vector2 place = anchorAt(f, branch.Anchor) + pointAlong(branch, along * branch.Length, out _) * size;
                    float gap = float.MaxValue;

                    for (int i = liveFrom; i < flowers.Count; i++)
                    {
                        if (!flowers[i].Done)
                            gap = Math.Min(gap, Vector2.DistanceSquared(place, flowers[i].At));
                    }

                    if (gap > bestGap)
                    {
                        bestGap = gap;
                        bestBranch = b;
                        bestAlong = along;
                        bestPlace = place;
                    }
                }

                if (bestGap < 0f)
                    return;

                var carrier = branches[bestBranch];
                float angle = hash01(Id, key, 51) * MathF.Tau;
                float scatter = DanmakusuBalance.YUYUKO_PETAL_SCATTER * size * 0.5f * MathF.Sqrt(hash01(Id, key, 52));
                double reach = Start + (carrier.Begin + (carrier.Finish - carrier.Begin) * bestAlong) * growMs; // (the branch has to have got there)
                double born = Math.Max(t, reach);
                double burst = Math.Min(born + beat * DanmakusuBalance.LILY_FLOWER_BEATS, Gone);

                if (burst <= born)
                    return;

                flowers.Add(new Flower
                {
                    At = bestPlace + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * scatter,
                    Length = DanmakusuBalance.YUYUKO_PETAL_SIZE * (0.8f + 0.4f * hash01(Id, key, 45)),
                    Turn = hash01(Id, key, 47) * MathF.Tau,
                    Key = key,
                    Born = born,
                    Burst = burst,
                });
            }

            public override void Update(BulletField f, double now)
            {
                // time went back: what has been counted since is forgotten (the bullets will leave again)
                if (now < lastUpdate - 1)
                {
                    flowers.Clear();
                    sown.Clear();
                    ClearBullets();
                    liveFrom = 0;
                }

                lastUpdate = now;

                while (liveFrom < flowers.Count && flowers[liveFrom].Done)
                    liveFrom++;

                for (int i = liveFrom; i < flowers.Count; i++)
                {
                    var flower = flowers[i];

                    if (!flower.Done && flower.Burst <= now)
                    {
                        flower.Done = true;
                        burstInto(f, flower);
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                flowers.Clear();
                sown.Clear();
                ClearBullets();
                liveFrom = 0;
                lastUpdate = double.MinValue;
            }

            /// <summary>A flower bursts: every petal flies away (from its place on the flower, away from the middle of the flower) as a pink petal.</summary>
            private void burstInto(BulletField f, Flower flower)
            {
                float size = treeSize(f);
                float radius = flower.Length * 0.5f * size; // (as big as the petal it was, like Yuyuko's)
                float baseSpeed = stageBulletSpeed(Period);

                for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
                {
                    int key = flower.Key * 8 + k;
                    float angle = flower.Turn + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                    var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 direction = turned(outward, (hash01(Id, key, 3) - 0.5f) * 0.3f);
                    float speed = Math.Max(baseSpeed * (0.6f + 0.6f * hash01(Id, key, 4)), 0.02f);

                    AddBullet(new StageBullet
                    {
                        T0 = flower.Burst,
                        P0 = flower.At + outward * (DanmakusuBalance.YUYUKO_PETAL_REACH * flower.Length * size),
                        V = direction * speed,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = (flower.Key + k) % f.palette.Length,
                        Shape = BulletShape.Petal,
                        Tint = BulletRenderer.TINT_PINK,
                        TintAmount = 1f,
                        Until = flower.Burst + f.stageLife(direction * speed),
                    });
                }
            }

            private static bool alive(Flower flower, double t) => !flower.Done && flower.Born <= t && t < flower.Burst;

            public override bool Hurts(BulletField f, double now)
            {
                float u = growth(now);

                if (now < Start || now >= Gone || u <= 0f)
                    return false;

                float size = treeSize(f);
                Vector2 player = f.characterPosition;
                float hit = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // the wood
                foreach (var branch in branches)
                {
                    float p = progressOf(branch, u);

                    if (p <= 0f)
                        continue;

                    int n = reveal(branch, p, size, anchorAt(f, branch.Anchor));

                    for (int k = 0; k + 1 < n; k++)
                    {
                        if (distanceToSegment(player, revealPoints[k], revealPoints[k + 1]) <= Math.Max(revealWidths[k], revealWidths[k + 1]) + hit)
                            return true;
                    }
                }

                // the flowers
                for (int i = liveFrom; i < flowers.Count; i++)
                {
                    var flower = flowers[i];

                    if (!alive(flower, now))
                        continue;

                    float scale = petalScale(now, flower.Born);

                    if (scale < 0.3f)
                        continue;

                    if (Vector2.Distance(player, flower.At) <= (DanmakusuBalance.YUYUKO_PETAL_REACH + 0.5f) * flower.Length * size * scale + hit)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (t < Start || t >= Gone)
                    return;

                float size = treeSize(f);
                var bloom = new Color4(1f, 0.62f, 0.78f, 1f);
                var dark = new Color4(0.85f, 0.20f, 0.40f, 1f);

                // (the petals themselves are drawn with the bullets, see AddPetals) the dark middle of every flower, then the bloom of every flower over all of it (brighter as the burst nears)
                for (int i = liveFrom; i < flowers.Count; i++)
                {
                    var flower = flowers[i];

                    if (!alive(flower, t))
                        continue;

                    float scale = petalScale(t, flower.Born);

                    if (scale <= 0.01f)
                        continue;

                    f.stageTree.Add(StageTexture.Orb, flower.At, new Vector2(flower.Length * 0.5f * size * scale), 0f, 0.95f, dark);
                }

                for (int i = liveFrom; i < flowers.Count; i++)
                {
                    var flower = flowers[i];

                    if (!alive(flower, t))
                        continue;

                    float scale = petalScale(t, flower.Born);

                    if (scale <= 0.01f)
                        continue;

                    float u = (float)Math.Clamp((t - flower.Born) / Math.Max(flower.Burst - flower.Born, 1.0), 0.0, 1.0);

                    f.stageTree.Add(StageTexture.Glow, flower.At, new Vector2(flower.Length * size * DanmakusuBalance.YUYUKO_BLOOM_SIZE * scale), 0f, DanmakusuBalance.YUYUKO_BLOOM_ALPHA * scale * (1f + 1.5f * u * u * u), bloom, true);
                }
            }

            /// <summary>Adds the petals of the flowers at time <paramref name="t"/> to the bullets that are drawn: the very petal of a petal bullet, pointing at the middle of its flower with its notch out at the tip, in the pink of the bullets that fly away.</summary>
            public int AddPetals(BulletField f, double t, int count)
            {
                if (t < Start || t >= Gone)
                    return count;

                float size = treeSize(f);

                for (int i = liveFrom; i < flowers.Count; i++)
                {
                    var flower = flowers[i];

                    if (!alive(flower, t))
                        continue;

                    float scale = petalScale(t, flower.Born);

                    if (scale <= 0.01f)
                        continue;

                    for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
                    {
                        if (count == f.instanceBuffer.Length)
                            Array.Resize(ref f.instanceBuffer, f.instanceBuffer.Length * 2);

                        float angle = flower.Turn + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                        var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        f.instanceBuffer[count++] = new BulletInstance
                        {
                            Position = flower.At + outward * (DanmakusuBalance.YUYUKO_PETAL_REACH * flower.Length * size * scale),
                            Radius = flower.Length * 0.5f * size * scale,
                            Style = 0,
                            Direction = -outward,
                            Shape = BulletShape.Petal,
                            Speed = 0f,
                            Overlay = BulletRenderer.TINT_PINK,
                            OverlayAmount = 1f,
                            Under = BulletShape.Petal,
                            Morph = 1f,
                            Immune = true, // (part of the branches: the other bosses do not hide it)
                        };
                    }
                }

                return count;
            }
        }
    }
}
