using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Yuyuko: a sakura tree grows from the bottom middle of the screen when her warning is over. Its trunk and branches are curved brown strokes that grow out of each other (they are drawn by the
        // beam renderer, all together, so they are one piece of wood); the screen shakes a little (1..2 px) while the tree grows. In every attack sakura flowers (five pink ellipses in a star, with a bloom; the bullets round them
        // are darkened) grow on it; when the attack ends their petals fly away as pink petal bullets, and the next attack grows new ones. When she goes the tree grows backwards until it is gone. The wood and the petals hurt
        // like a slider body. Everything that leaves the top of the screen meanwhile turns into purple petals and falls (see BossOps).
        // ======================================================================================================================================================

        private sealed class TreeAct : StageAct
        {
            private bool? holeOnMap;

            /// <summary>One branch (or the trunk): a curve of points in the tree's own square (x and y both 0..1, y down, the tree is as wide as it is tall), with a half width at every point.</summary>
            private sealed class TreeBranch
            {
                public Vector2[] Points = [];
                public float[] Arc = []; // (how far along the branch each point is)
                public float[] Width = [];
                public float[] Heading = []; // (the direction at each point: 0 is up, positive turns right)
                public float Length;
                public float Begin, Finish; // (when it starts and stops growing, 0..1 of the growth)
                public int Depth;
            }

            private const int max_depth = 5;
            private const float flower_bare = 0.15f; // (the part of a branch next to where it starts that carries no flowers)
            private static readonly int[] child_counts = { 4, 3, 2, 2, 2, 0 };
            private static readonly float[] bends = { 0.45f, 0.9f, 1.0f, 1.1f, 1.2f, 1.2f }; // (how far a branch of each level turns over its length, at most, in radians)
            private static readonly float[] spreads = { 0.95f, 0.85f, 0.75f, 0.7f, 0.65f, 0f }; // (how far its children leave it, at most)
            private static readonly float[] length_ratios = { 0.8f, 0.78f, 0.78f, 0.78f, 0.78f, 0f }; // (how long its children are)

            private readonly List<(double Start, double End)> attacks;
            private readonly double nextStart;
            private TreeBranch[] branches = [];
            private int[] petalBranches = [];
            private List<double>[] crossings = []; // for every attack: when each of the bullets that left the screen at the bottom did (a flower grows for each)
            private readonly HashSet<long> sown = new HashSet<long>(); // (the bullets that have brought a flower, in whichever attack: once each)
            private double lastUpdate = double.MinValue;
            private List<Vector2>[] slotAt = []; // where the flowers can sit (a few different arrangements, for the attacks in turn): every one is as far from the ones before it as it can be, so any number of them are evenly spread
            private List<int>[] slotBranch = [];
            private List<float>[] slotAlong = [];

            private const int slot_variants = 3;
            private const int slot_candidates = 8;
            private float[] petalReach = []; // (for every branch that carries flowers: how much of the length of all of them is up to its end)
            private bool[] built = [];
            private readonly Vector2[] revealPoints = new Vector2[48];
            private readonly float[] revealWidths = new float[48];
            private readonly float[] revealArc = new float[48];

            /// <summary>When the tree is gone: it grows backwards from the end of her sections at the speed it grew (or until the next tree starts).</summary>
            public double Gone;

            public TreeAct(BossPeriod owner, double start, double end, List<(double Start, double End)> attacks, double nextStart)
            {
                Period = owner;
                Start = start;
                End = end;
                this.attacks = attacks;
                this.nextStart = nextStart;
            }

            private double beat => Math.Max(Period.BeatLength, 1.0);

            private double growMs => Math.Min(DanmakusuBalance.YUYUKO_TREE_GROW_BEATS * beat, Math.Max((End - Start) * 0.85, beat)); // (a short stay still gives the whole tree: it grows faster then)

            private static float treeSize(BulletField f) => DanmakusuBalance.YUYUKO_TREE_HEIGHT * (f.stageMax.Y - f.stageMin.Y);

            private static Vector2 treeTopLeft(BulletField f, float size) => new Vector2((f.stageMin.X + f.stageMax.X) * 0.5f - size / 2f, f.stageMax.Y - size);

            /// <summary>How far the tree has grown (0..1): it grows from her first appearing, and backwards from the end of her sections.</summary>
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

                var list = new List<TreeBranch>();
                int n = 0;
                float rnd() => hash01(Id, n++, 11);

                Vector2 pointOf(TreeBranch branch, float s) => pointAlong(branch, s, out _);

                void grow(Vector2 start, float heading, float length, float width0, int depth, float begin)
                {
                    int k = Math.Clamp((int)(length / 0.025f), 6, 22);
                    float bend = depth == 0 ? 0f : (rnd() - 0.5f) * 2f * bends[depth]; // (the trunk grows straight up)
                    float wobble = depth == 0 ? 0f : (rnd() - 0.5f) * 2f * 0.35f;
                    float phase = rnd() * MathF.Tau;
                    float droop = depth >= 2 ? (0.25f + 0.35f * rnd()) * (depth - 1f) / max_depth : 0f; // (the outer branches arch over and hang a little, as the ones of a cherry tree do)
                    float knots = 0.05f + 0.06f * rnd(); // (the wood is a little knobbly)
                    float knotPhase = rnd() * MathF.Tau;
                    float step = length / k;
                    float h = heading;
                    float endRatio = depth == 0 ? 0.55f : depth == max_depth ? 0.05f : 0.5f;

                    var branch = new TreeBranch
                    {
                        Points = new Vector2[k + 1],
                        Arc = new float[k + 1],
                        Width = new float[k + 1],
                        Heading = new float[k + 1],
                        Length = length,
                        Begin = begin,
                        Finish = begin + length,
                        Depth = depth,
                    };

                    branch.Points[0] = start;
                    branch.Heading[0] = h;

                    for (int i = 1; i <= k; i++)
                    {
                        float s0 = (i - 1f) / k, s1 = (float)i / k;

                        // (an arc and a wobble on top of it: a branch is never straight)
                        h += bend / k + wobble * (MathF.Sin(phase + s1 * 5.4f) - MathF.Sin(phase + s0 * 5.4f));

                        if (depth >= 2 && MathF.Abs(h) > 0.2f)
                            h += MathF.Sign(h) * droop * (s1 - s0);

                        if (depth > 0)
                            h = Math.Clamp(h, -1.9f, 1.9f);

                        Vector2 next = branch.Points[i - 1] + new Vector2(MathF.Sin(h), -MathF.Cos(h)) * step;

                        // (the branches stay in the crown: one that leaves it is turned back towards its middle)
                        if (depth > 0 && next.Y < 0.72f)
                        {
                            float ex = (next.X - 0.5f) / 0.45f, ey = (next.Y - 0.40f) / 0.37f;

                            if (ex * ex + ey * ey > 1f)
                            {
                                float toward = MathF.Atan2(0.5f - next.X, -(0.40f - next.Y));
                                h += Math.Clamp(MathF.IEEERemainder(toward - h, MathF.Tau), -0.35f, 0.35f) * 0.6f;
                                next = branch.Points[i - 1] + new Vector2(MathF.Sin(h), -MathF.Cos(h)) * step;
                            }
                        }

                        branch.Points[i] = next;
                        branch.Heading[i] = h;
                        branch.Arc[i] = branch.Arc[i - 1] + step;
                    }

                    for (int i = 0; i <= k; i++)
                    {
                        float s = branch.Arc[i] / length;
                        float w = width0 * (1f + (endRatio - 1f) * s);

                        if (depth == 0)
                            w *= 1f + 0.6f * MathF.Max(0f, 1f - s / 0.15f); // (the trunk flares out at its foot)

                        w *= 1f + knots * MathF.Sin(knotPhase + s * (depth == 0 ? 17f : 11f)) * (i == 0 ? 0f : 1f);

                        branch.Width[i] = w;
                    }

                    list.Add(branch);

                    int children = child_counts[depth];

                    // (every branch of a level forks the same way, so the crown grows evenly)
                    float firstSide = rnd() < 0.5f ? -1f : 1f;
                    float lastSide = firstSide;
                    bool lowLeftGone = false;
                    int rightFrom = -1, rightTo = -1; // (the lowest branch on the right of the trunk, with everything that grows out of it)

                    for (int j = 0; j < children; j++)
                    {
                        bool tip = j == children - 1;
                        float forkMin = depth == 0 ? 0.55f : 0.3f;
                        float fork = tip ? 1f : forkMin + (0.95f - forkMin) * (j + 0.5f) / Math.Max(children - 1, 1) + (rnd() - 0.5f) * 0.06f;
                        fork = Math.Clamp(fork, forkMin, 0.97f);

                        // (one to either side in turn, the same spread and nearly the same length)
                        float side = j == 0 ? firstSide : -lastSide;
                        lastSide = side;

                        float spread = spreads[depth] * (0.85f + 0.3f * rnd()) * (tip ? 0.4f : 1f);
                        float childLength = length * length_ratios[depth] * (0.9f + 0.2f * rnd());

                        int mark = list.Count;

                        grow(pointOf(branch, fork * length), headingAlong(branch, fork * length) + side * spread, childLength, widthAlong(branch, fork * length) * (0.62f + 0.18f * rnd()), depth + 1, begin + length * fork);

                        // (the lowest branch on the left of the trunk is not there: it is grown like the others, so that the rest of the tree stays as it was, and taken away again)
                        if (depth == 0 && !tip && side < 0f && !lowLeftGone)
                        {
                            lowLeftGone = true;
                            list.RemoveRange(mark, list.Count - mark);
                        }
                        else if (depth == 0 && !tip && side > 0f && rightFrom < 0)
                        {
                            rightFrom = mark;
                            rightTo = list.Count;
                        }
                    }

                    // (the bottom left branch is the bottom right one the other way round, with its sub-branches, almost: the same shape bent a little differently)
                    if (depth == 0 && rightFrom >= 0)
                    {
                        for (int q = rightFrom; q < rightTo; q++)
                        {
                            var source = list[q];
                            var mirror = new TreeBranch
                            {
                                Points = new Vector2[source.Points.Length],
                                Arc = (float[])source.Arc.Clone(),
                                Width = (float[])source.Width.Clone(),
                                Heading = new float[source.Heading.Length],
                                Length = source.Length,
                                Begin = source.Begin,
                                Finish = source.Finish,
                                Depth = source.Depth,
                            };

                            for (int i = 0; i < source.Points.Length; i++)
                            {
                                float dx = source.Points[i].X - 0.5f;
                                float y = source.Points[i].Y;
                                mirror.Points[i] = new Vector2(0.5f - dx * (1f + 0.05f * MathF.Sin(11f * y)), y + 0.012f * MathF.Abs(dx) * MathF.Sin(7f * dx + 0.5f));
                                mirror.Heading[i] = -source.Heading[i];
                            }

                            list.Add(mirror);
                        }
                    }
                }

                grow(new Vector2(0.5f, 1.03f), 0f, 0.38f, 0.055f, 0, 0f);

                // the whole growth is spread over the first part of the time (the rest is for the petals)
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

                // every branch but the trunk carries flowers, spread evenly over all of them (by length; the first part of each is bare)
                var eligible = new List<int>();
                var reaches = new List<float>();
                float carried = 0f;

                for (int i = 0; i < branches.Length; i++)
                {
                    if (branches[i].Depth >= 1)
                    {
                        carried += branches[i].Length * (1f - flower_bare);
                        eligible.Add(i);
                        reaches.Add(carried);
                    }
                }

                petalBranches = eligible.ToArray();
                petalReach = reaches.ToArray();

                // the places of the flowers are made as they are needed (see place)
                slotAt = new List<Vector2>[slot_variants];
                slotBranch = new List<int>[slot_variants];
                slotAlong = new List<float>[slot_variants];

                for (int v = 0; v < slot_variants; v++)
                {
                    slotAt[v] = new List<Vector2>();
                    slotBranch[v] = new List<int>();
                    slotAlong[v] = new List<float>();
                }

                crossings = new List<double>[attacks.Count];

                for (int j = 0; j < attacks.Count; j++)
                    crossings[j] = new List<double>();

                built = new bool[attacks.Count];
            }

            private static int segmentAt(TreeBranch branch, float s)
            {
                int i = 1;

                while (i < branch.Arc.Length - 1 && branch.Arc[i] < s)
                    i++;

                return i;
            }

            /// <summary>The point <paramref name="s"/> along the branch (in the tree's units).</summary>
            private static Vector2 pointAlong(TreeBranch branch, float s, out int segment)
            {
                segment = segmentAt(branch, s);
                float span = branch.Arc[segment] - branch.Arc[segment - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[segment - 1]) / span, 0f, 1f) : 0f;

                return Vector2.Lerp(branch.Points[segment - 1], branch.Points[segment], u);
            }

            private static float widthAlong(TreeBranch branch, float s)
            {
                int i = segmentAt(branch, s);
                float span = branch.Arc[i] - branch.Arc[i - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[i - 1]) / span, 0f, 1f) : 0f;

                return branch.Width[i - 1] + (branch.Width[i] - branch.Width[i - 1]) * u;
            }

            private static float headingAlong(TreeBranch branch, float s)
            {
                int i = segmentAt(branch, s);
                float span = branch.Arc[i] - branch.Arc[i - 1];
                float u = span > 1e-6f ? Math.Clamp((s - branch.Arc[i - 1]) / span, 0f, 1f) : 0f;

                return branch.Heading[i - 1] + (branch.Heading[i] - branch.Heading[i - 1]) * u;
            }

            /// <summary>
            /// The part of a branch that has grown (<paramref name="p"/>, 0..1 of its length), in field coordinates, with its half widths in pixels, in the scratch arrays: while it grows its tip is sharp (the nearer the tip, the thinner),
            /// and the sharpness goes as it arrives. Returns how many points there are.
            /// </summary>
            private int reveal(TreeBranch branch, float p, float size, Vector2 topLeft)
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
                    revealPoints[i] = topLeft + revealPoints[i] * size;
                }

                return n;
            }

            private float progressOf(TreeBranch branch, float u) => Math.Min((u - branch.Begin) / Math.Max(branch.Finish - branch.Begin, 1e-4f), 1f);

            /// <summary>Adds the wood of the tree at time <paramref name="t"/> to the beams that are drawn.</summary>
            public void AddWood(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                float u = growth(t);

                if (t < Start || t >= Gone || u <= 0f)
                    return;

                float size = treeSize(f);
                Vector2 topLeft = treeTopLeft(f, size);

                foreach (var branch in branches)
                {
                    float p = progressOf(branch, u);

                    if (p <= 0f)
                        continue;

                    int n = reveal(branch, p, size, topLeft);

                    if (n >= 2)
                        f.addWoodBeam(revealPoints, revealWidths, n, ref beams, ref pointTotal);
                }
            }

            /// <summary>0..1: the screen shakes (1..2 px) while the tree grows, and while it grows backwards.</summary>
            public float ShakeAt(double now)
            {
                if (now < Start || now >= Gone)
                    return 0f;

                return now > End || now < Start + growMs ? DanmakusuBalance.YUYUKO_SHAKE : 0f;
            }

            private int activeAttack(double t)
            {
                for (int j = 0; j < attacks.Count; j++)
                {
                    if (t >= attacks[j].Start && t < attacks[j].End)
                        return j;
                }

                return -1;
            }

            /// <summary>Flower <paramref name="i"/> of attack <paramref name="attack"/>: where its middle sits (in the tree's units), how long one of its petals is (a share of the tree's height), how it is turned (radians), its tone (0..1), and when it starts to grow (after its branch has reached it).</summary>
            private void flower(int attack, int i, out Vector2 at, out float length, out float turn, out float tone, out double born)
            {
                int key = attack * 4096 + i;

                // evenly: flower i sits at place i of this attack's arrangement (see Init: each place is as far from the earlier ones as it could be, so the first flowers are spread over the whole crown and the next ones fill in the gaps)
                int variant = attack % slot_variants;
                ensureSlots(variant, i + 1);

                at = slotAt[variant][i];
                length = DanmakusuBalance.YUYUKO_PETAL_SIZE * (0.8f + 0.4f * hash01(Id, key, 45));
                turn = hash01(Id, key, 47) * MathF.Tau;
                tone = hash01(Id, key, 48);

                // the tree's own flowers come out soon after the attack starts, the others when a bullet has crossed the bottom of the screen; or when their branch has got there, if it is still growing (they show
                // on it as it grows)
                double wanted = i < DanmakusuBalance.YUYUKO_OWN_FLOWERS
                    ? attacks[attack].Start + hash01(Id, key, 44) * DanmakusuBalance.YUYUKO_PETAL_SPREAD_BEATS * beat
                    : crossings[attack][i - DanmakusuBalance.YUYUKO_OWN_FLOWERS];

                born = Math.Max(wanted, reachOf(attack, i));
            }

            /// <summary>When the branch of flower <paramref name="i"/> of attack <paramref name="attack"/> has grown to its place.</summary>
            private double reachOf(int attack, int i)
            {
                int variant = attack % slot_variants;
                ensureSlots(variant, i + 1);
                var branch = branches[slotBranch[variant][i]];

                return Start + (branch.Begin + (branch.Finish - branch.Begin) * slotAlong[variant][i]) * growMs;
            }

            private float petalScale(double t, double born) => smoothStep((t - born) / (DanmakusuBalance.YUYUKO_PETAL_GROW_BEATS * beat));

            /// <summary>How many flowers the tree has in this attack: its own and one for every bullet that has crossed the bottom (so far).</summary>
            /// <summary>Makes sure that the first <paramref name="count"/> places of arrangement <paramref name="v"/> are there: for each, a few places on the branches (by length, scattered a little round the branch) are tried and the one farthest from all the earlier ones is taken.</summary>
            private void ensureSlots(int v, int count)
            {
                var at = slotAt[v];

                for (int s = at.Count; s < count; s++)
                {
                    float bestDistance = -1f;
                    Vector2 bestPlace = default;
                    int bestBranch = 0;
                    float bestAlong = 0f;

                    for (int c = 0; c < slot_candidates; c++)
                    {
                        int key = (v * 65536 + s) * 16 + c;
                        float target = hash01(Id, key, 50) * petalReach[^1];
                        int pick = 0;

                        while (pick < petalReach.Length - 1 && petalReach[pick] < target)
                            pick++;

                        var carrier = branches[petalBranches[pick]];
                        float before = pick > 0 ? petalReach[pick - 1] : 0f;
                        float along = flower_bare + (1f - flower_bare) * Math.Clamp((target - before) / Math.Max(petalReach[pick] - before, 1e-6f), 0f, 1f);
                        float angle = hash01(Id, key, 51) * MathF.Tau;
                        float scatter = DanmakusuBalance.YUYUKO_PETAL_SCATTER * MathF.Sqrt(hash01(Id, key, 52));
                        Vector2 place = pointAlong(carrier, along * carrier.Length, out _) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * scatter;

                        float nearest = float.MaxValue;

                        for (int o = 0; o < s; o++)
                            nearest = Math.Min(nearest, Vector2.DistanceSquared(place, at[o]));

                        if (nearest > bestDistance)
                        {
                            bestDistance = nearest;
                            bestPlace = place;
                            bestBranch = petalBranches[pick];
                            bestAlong = along;
                        }
                    }

                    at.Add(bestPlace);
                    slotBranch[v].Add(bestBranch);
                    slotAlong[v].Add(bestAlong);
                }
            }

            private int flowerCount(int attack) => DanmakusuBalance.YUYUKO_OWN_FLOWERS + crossings[attack].Count;

            /// <summary>A bullet (number <paramref name="bullet"/> of ring <paramref name="ring"/>) has gone out of the screen at the bottom at time <paramref name="t"/>: a flower grows on the tree for it (once per bullet).</summary>
            public void Sow(int ring, int bullet, double t)
            {
                if (t < Start || t >= Gone)
                    return;

                int attack = activeAttack(t);

                // (one that goes out in a pause counts for the next attack, from when that starts: it does not wait for it outside the screen)
                if (attack < 0)
                {
                    for (int j = attacks.Count - 1; j >= 0 && attacks[j].Start > t; j--)
                        attack = j;

                    if (attack < 0)
                        return;

                    t = attacks[attack].Start;
                }

                // (only part of the bullets bring a flower)
                // (a ring can have far more than 64 bullets: the number of a bullet is the whole of the low part of its key, so two bullets never share one and one of them is never left without its flower)
                long key = (long)ring * 4096 + bullet;

                if (hash01(Id, ring * 4096 + bullet, 61) >= DanmakusuBalance.YUYUKO_SOW_SHARE || sown.Contains(key))
                    return;

                // (its flower shows as soon as its branch has grown to its place, also while the tree still grows; one whose place is not reached in time to show in its attack is kept for the first attack in which
                // it is, and comes out there: none is lost because its attack was over first)
                double showing = DanmakusuBalance.YUYUKO_PETAL_GROW_BEATS * beat;

                while (attack < attacks.Count && reachOf(attack, flowerCount(attack)) > attacks[attack].End - showing)
                    attack++;

                if (attack >= attacks.Count)
                    return;

                sown.Add(key);
                crossings[attack].Add(Math.Max(t, attacks[attack].Start));
            }

            public override void Update(BulletField f, double now)
            {
                // time went back: what has been counted since is forgotten (the bullets will cross again)
                if (now < lastUpdate - 1)
                {
                    foreach (var list in crossings)
                        list.Clear();

                    sown.Clear();
                }

                lastUpdate = now;

                // the petals of an attack that is over have flown away (as bullets); if time went back, they are made again
                bool consistent = true;

                for (int j = 0; j < attacks.Count && consistent; j++)
                    consistent = !built[j] || now >= attacks[j].End;

                if (!consistent)
                {
                    Array.Clear(built, 0, built.Length);
                    ClearBullets();
                }

                for (int j = 0; j < attacks.Count; j++)
                {
                    if (!built[j] && now >= attacks[j].End)
                    {
                        built[j] = true;
                        fly(f, j);
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(built, 0, built.Length);
                ClearBullets();

                foreach (var list in crossings)
                    list.Clear();

                sown.Clear();
                lastUpdate = double.MinValue;
            }

            /// <summary>The attack is over: every petal of every flower that has grown flies away (from its place on the flower, out of the flower and away from the middle of the crown) as a pink petal.</summary>
            private void fly(BulletField f, int attack)
            {
                float size = treeSize(f);
                Vector2 topLeft = treeTopLeft(f, size);
                Vector2 crown = topLeft + new Vector2(0.5f, 0.40f) * size;
                double end = attacks[attack].End;
                float baseSpeed = stageBulletSpeed(Period);

                for (int i = 0; i < flowerCount(attack); i++)
                {
                    flower(attack, i, out Vector2 local, out float length, out float turn, out _, out double born);
                    float radius = length * 0.5f * size; // (as big as the petal it was)

                    if (born >= end)
                        continue; // (it had not started to grow)

                    Vector2 centre = topLeft + local * size;
                    Vector2 away = centre - crown;
                    away = away.LengthSquared > 1e-3f ? Vector2.Normalize(away) : new Vector2(0, -1);

                    for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
                    {
                        int key = (attack * 4096 + i) * 8 + k;
                        float angle = turn + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                        var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        Vector2 mixed = outward * 0.7f + away * 0.3f;
                        Vector2 direction = turned(Vector2.Normalize(mixed), (hash01(Id, key, 3) - 0.5f) * 0.3f);

                        // (a flower has five petals; three of them are shot out as they are (which three differs from flower to flower), the other two fall straight down)
                        bool falls = (k + i) % DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER >= DanmakusuBalance.YUYUKO_SHOT_PETALS;

                        if (falls)
                            direction = new Vector2(0f, 1f); // (and they keep the angle they had on the flower)
                        float speed = Math.Max(baseSpeed * (0.6f + 0.6f * hash01(Id, key, 4)), 0.02f);

                        AddBullet(new StageBullet
                        {
                            T0 = end,
                            P0 = centre + outward * (DanmakusuBalance.YUYUKO_PETAL_REACH * length * size),
                            V = direction * speed,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = (i + k) % f.palette.Length,
                            Shape = BulletShape.Petal,
                            Tint = BulletRenderer.TINT_PINK,
                            TintAmount = 1f,
                            Until = end + f.stageLife(direction * speed),
                            Face = falls ? outward : Vector2.Zero,
                            KeepFace = falls,
                            Untraced = holeOnMap ??= f.bulletOps.Any(op => op.SkipPetals), // (with Utsuho's black hole on the map the ops leave the petals of her flowers alone)
                        });
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                float u = growth(now);

                if (now < Start || now >= Gone || u <= 0f)
                    return false;

                float size = treeSize(f);
                Vector2 topLeft = treeTopLeft(f, size);
                Vector2 player = f.characterPosition;
                float hit = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // the wood
                foreach (var branch in branches)
                {
                    float p = progressOf(branch, u);

                    if (p <= 0f)
                        continue;

                    int n = reveal(branch, p, size, topLeft);

                    for (int k = 0; k + 1 < n; k++)
                    {
                        if (distanceToSegment(player, revealPoints[k], revealPoints[k + 1]) <= Math.Max(revealWidths[k], revealWidths[k + 1]) + hit)
                            return true;
                    }
                }

                // the flowers
                int attack = activeAttack(now);

                if (attack < 0)
                    return false;

                for (int i = 0; i < flowerCount(attack); i++)
                {
                    flower(attack, i, out Vector2 local, out float length, out _, out _, out double born);
                    float scale = petalScale(now, born);

                    if (scale < 0.3f)
                        continue;

                    if (Vector2.Distance(player, topLeft + local * size) <= (DanmakusuBalance.YUYUKO_PETAL_REACH + 0.5f) * length * size * scale + hit)
                        return true;
                }

                return false;
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                int attack = activeAttack(t);

                if (attack < 0 || t < Start || t >= Gone)
                    return;

                // how much of the crown is in flower: a sample of them
                float grown = 0f;

                for (int i = 0; i < flowerCount(attack); i += 4)
                {
                    flower(attack, i, out _, out _, out _, out _, out double born);
                    grown += petalScale(t, born);
                }

                float presence = Math.Min(grown / (DanmakusuBalance.YUYUKO_DIM_FLOWERS / 16f), 1f); // (the dim of the bullets round the tree is full when this many quarters of the flowers are in bloom)

                if (presence < 0.01f)
                    return;

                float size = treeSize(f);

                into.Add(new StageDim
                {
                    Centre = treeTopLeft(f, size) + new Vector2(0.5f, 0.40f) * size,
                    Radius = DanmakusuBalance.YUYUKO_DIM_RADIUS * size,
                    Strength = DanmakusuBalance.YUYUKO_DIM * presence,
                });
            }

            public override void Draw(BulletField f, double t)
            {
                float size = treeSize(f);
                Vector2 topLeft = treeTopLeft(f, size);

                // a soft sunbeam falls from the top of the screen onto the tree; it is as wide as the tree has grown
                float grown = smoothStep(growth(t));

                // it shines during the attacks only: in a pause it fades out (its width stays, it is not grown backwards) and comes back with the next attack
                float shining = 0f;

                foreach (var (attackStart, attackEnd) in attacks)
                    shining = Math.Max(shining, smoothStep((t - attackStart) / DanmakusuBalance.YUYUKO_BEAM_FADE_MS) * smoothStep((attackEnd - t) / DanmakusuBalance.YUYUKO_BEAM_FADE_MS));

                if (grown > 0.01f && shining > 0.01f && t >= Start && t < Gone)
                {
                    float start = StageLayer.ConeStartFor(DanmakusuBalance.NUE_CONE_HALF_ANGLE_DEGREES);
                    float height = f.stageMax.Y - f.stageMin.Y;
                    float total = height / (1f - start);
                    float halfWidth = DanmakusuBalance.YUYUKO_BEAM_WIDTH * size * 0.5f * grown;
                    var apex = new Vector2(topLeft.X + 0.5f * size, f.stageMin.Y - start * total);

                    f.stageBack.Add(StageTexture.Cone, apex + new Vector2(0f, total * 0.5f), new Vector2(total, 2f * halfWidth), 90f, DanmakusuBalance.YUYUKO_BEAM_ALPHA * shining, new Color4(1f, 0.95f, 0.75f, 1f), true);
                }

                int attack = activeAttack(t);

                if (attack < 0 || t < Start || t >= Gone)
                    return;

                var bloom = new Color4(1f, 0.62f, 0.78f, 1f);
                var dark = new Color4(0.85f, 0.20f, 0.40f, 1f);

                // (the petals themselves are drawn with the bullets, see AddPetals: the same petal as a petal bullet, with the same border and gradient)
                // the dark middle of every flower, then the bloom of every flower over all of it (a soft glow that adds to what is under it: the middles are lit by it like the petals)
                for (int i = 0; i < flowerCount(attack); i++)
                {
                    flower(attack, i, out Vector2 local, out float length, out _, out _, out double born);
                    float scale = petalScale(t, born);

                    if (scale <= 0.01f)
                        continue;

                    f.stageTree.Add(StageTexture.Orb, topLeft + local * size, new Vector2(length * 0.5f * size * scale), 0f, 0.95f, dark);
                }

                for (int i = 0; i < flowerCount(attack); i++)
                {
                    flower(attack, i, out Vector2 local, out float length, out _, out _, out double born);
                    float scale = petalScale(t, born);

                    if (scale <= 0.01f)
                        continue;

                    f.stageTree.Add(StageTexture.Glow, topLeft + local * size, new Vector2(length * size * DanmakusuBalance.YUYUKO_BLOOM_SIZE * scale), 0f, DanmakusuBalance.YUYUKO_BLOOM_ALPHA * scale, bloom, true);
                }
            }

            /// <summary>
            /// Adds the petals of the flowers at time <paramref name="t"/> to the bullets that are drawn (after they have been dimmed: the flowers are not): the very petal of a petal bullet, pointing at the middle of its
            /// flower with its notch out at the tip, in the pink of the bullets that fly away, with the same border and gradient.
            /// </summary>
            public int AddPetals(BulletField f, double t, int count)
            {
                int attack = activeAttack(t);

                if (attack < 0 || t < Start || t >= Gone)
                    return count;

                float size = treeSize(f);
                Vector2 topLeft = treeTopLeft(f, size);

                for (int i = 0; i < flowerCount(attack); i++)
                {
                    flower(attack, i, out Vector2 local, out float length, out float turn, out _, out double born);
                    float scale = petalScale(t, born);

                    if (scale <= 0.01f)
                        continue;

                    Vector2 centre = topLeft + local * size;

                    for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
                    {
                        if (count == f.instanceBuffer.Length)
                            Array.Resize(ref f.instanceBuffer, f.instanceBuffer.Length * 2);

                        float angle = turn + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                        var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        f.instanceBuffer[count++] = new BulletInstance
                        {
                            Position = centre + outward * (DanmakusuBalance.YUYUKO_PETAL_REACH * length * size * scale),
                            Radius = length * 0.5f * size * scale,
                            Style = 0,
                            Direction = -outward, // (the tip of the petal points at the middle of the flower, the notch is at the outer end)
                            Shape = BulletShape.Petal,
                            Speed = 0f,
                            Overlay = BulletRenderer.TINT_PINK,
                            OverlayAmount = 1f,
                            Under = BulletShape.Petal,
                            Morph = 1f,
                            Immune = true, // (part of the tree: the other bosses do not hide it)
                        };
                    }
                }

                return count;
            }
        }
    }
}
