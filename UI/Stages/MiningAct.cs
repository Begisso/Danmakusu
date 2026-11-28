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
        // Momoyo Himemushi (2026-11-27). The screen is a grid of cells as big as an osu! circle (MOMOYO_BLOCK_CIRCLES x its diameter, stretched a little so that they fit it exactly, wall to wall). In her
        // attacks she throws a block of stone (the user's stone picture) every MOMOYO_THROW_EVERY_BEATS into the next free cell along the walls - the cells against the walls first, then the ring inside
        // them, MOMOYO_LAYERS rings at most, each ring in an order of its own: it flies from her for MOMOYO_THROW_BEATS, turning and growing to its size, and lies there until her section is over. It
        // hurts like a slider body, where it is drawn, flying too (since 2026-11-28, "Momoyo should shoot blocks as before, with twice frequency but quadruple less flying speed. Hits like sliders": a
        // throw every quarter beat, two beats in the air; only a block that lay hurt before).
        // In her pauses (at the start of each, and every MOMOYO_CENTIPEDE_EVERY_BEATS of a long one) a big centipede (MOMOYO_CENTIPEDE_CIRCLES circle radii long) comes in from behind the wall at her
        // back - a red arrow shows where for MOMOYO_CENTIPEDE_WARN_BEATS - and charges at where the player was, MOMOYO_CENTIPEDE_SPEED times a bullet's speed. Every block it goes through bursts into a
        // square of MOMOYO_BURST_BULLETS grey bullets that keeps its shape as it flies out; the cell is free again. Where she and the player are is taken as each centipede comes; a seek back lays the
        // blocks again from the start.
        // Since 2026-11-28 ("Centipede - high resolution, Her body, legs, scales move separately in joints, centipede can slowly turn into player and move away. Other Body parts move too") it is no
        // longer one picture (the user's, 400 x 211, with white left between its tails) but put together of pieces drawn sharp (StageLayer's CentipedePlate, CentipedeHead, CentipedeLeg, in the
        // picture's colours): MOMOYO_CENTIPEDE_PLATES plates, each turned along the way its body goes there, following its head's path, which sways from side to side; on every plate a pair of legs
        // of two joints each that stroke back and forth as it goes, a wave running down its body; feelers and fangs at its head and two long feelers at its tail that move too. For
        // MOMOYO_CHASE_BEATS after it comes in its head turns at the player, MOMOYO_TURN_DEGREES_PER_BEAT at most, then it goes straight on, out of the screen. It hurts like a slider body where it is
        // drawn (its plates, its head, its legs, its fangs and its tail's feelers; not the feelers at its head).
        // (Its head turns at where the player really is, step by step in Update; asked about the time to come, its head goes on straight as it goes then.)
        // ======================================================================================================================================================

        // (2026-11-28, "blocks should be built on playfield walls, not on centre. Again - from playfield EDGES": the grid is over the whole screen - its edges, sides and corners - and made again when the
        // screen is known or changes. It was made on her first frame only: when the screen was not known yet it lay over the osu! playfield, the middle of a wide screen, and the blocks were built round
        // that from the first time on.)

        private sealed class MiningAct : StageAct
        {
            private sealed class Block
            {
                public int Rank; // (its place in the order the cells are filled in: its cell is order[Rank], so it keeps its place along the walls when the grid is made anew for another screen)
                public double Thrown, Lands;
                public Vector2 From; // (where she was as she threw it)
                public double GoneAt = double.PositiveInfinity;
            }

            /// <summary>Where every piece of a centipede is at one moment.</summary>
            private sealed class Pose
            {
                public double T = double.NaN;
                public Vector2 Head;
                public float HeadAngle;
                public readonly Vector2[] Plates = new Vector2[DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES];
                public readonly float[] Angles = new float[DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES];
                public readonly float[] Widths = new float[DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES];

                // (legs, the tail's feelers and the fangs - all that hurts - then the head's feelers)
                public readonly (Vector2 A, Vector2 B, float Thick)[] Limbs = new (Vector2, Vector2, float)[DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES * 4 + 10];
                public int Hurting, Count;
                public int Legs; // (the legs come first, then the tail's feelers, then the fangs)
                public int Fangs;
                public Vector2 Middle;
                public float Reach;
            }

            private sealed class Crawler
            {
                public double Warn, Appear;
                public Vector2 Start, Way, Wall;

                /// <summary>The path its head went (without its sway): a point every <see cref="step_px"/>, from where it came in.</summary>
                public readonly List<Vector2> Path = new List<Vector2>();

                public Vector2 Heading;
                public double GoneAt = double.PositiveInfinity;
                public readonly Pose[] Kept = { new Pose(), new Pose(), new Pose(), new Pose() };
                public int NextKept;
            }

            private const float step_px = 4f;

            private readonly double beat;
            private readonly double[] throws;
            private readonly double[] crawls;
            private readonly List<Block> blocks = new List<Block>();
            private readonly List<Crawler> crawlers = new List<Crawler>();
            private int nextThrow, nextCrawl;

            // (the grid over the whole screen, made again whenever the screen is other than it was made for: 2026-11-28 - it was made once, on her first frame, and if the screen was not known yet then, it
            // was made over the osu! playfield, which on a wide screen is its middle: the blocks lay round the middle of the screen, not along its edges)
            private int columns, rows;
            private Vector2 cellSize, gridMin, gridLow, gridHigh;
            private int[] order = Array.Empty<int>();
            private int[] lying = Array.Empty<int>(); // (which block is at each place of the order, -1: none)

            public MiningAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = Math.Max(start, period.ShootStart); t < end && list.Count < 20000; t += beat * DanmakusuBalance.MOMOYO_THROW_EVERY_BEATS)
                        list.Add(t);
                }

                throws = list.ToArray();

                var times = new List<double>();

                foreach (var (start, end) in BossOps.PausesWithTail(period))
                {
                    for (double t = start; t < end - beat && times.Count < 4000; t += beat * DanmakusuBalance.MOMOYO_CENTIPEDE_EVERY_BEATS)
                        times.Add(t);
                }

                crawls = times.ToArray();
                Start = period.ShootStart;
                End = period.End + beat * 64;
            }

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.MOMOYO_CENTIPEDE_SPEED;

            private float crawlerLength(BulletField f) => f.circleRadius * DanmakusuBalance.MOMOYO_CENTIPEDE_CIRCLES;

            private float crawlerWidth(BulletField f) => crawlerLength(f) * DanmakusuBalance.MOMOYO_CENTIPEDE_WIDTH;

            private double chase => beat * DanmakusuBalance.MOMOYO_CHASE_BEATS;

            /// <summary>How long a centipede can be there at the most: its chase, then straight across the screen twice and its own length.</summary>
            private double crawlLife(BulletField f) => chase + (Vector2.Distance(f.stageMin, f.stageMax) * 2f + crawlerLength(f) * 2f) / Math.Max(speed, 1e-3f);

            public override double MechanicsEnd => Period.End;

            public override void Reset(BulletField f, double time)
            {
                blocks.Clear();
                crawlers.Clear();
                Array.Fill(lying, -1);
                nextThrow = 0;
                nextCrawl = 0;

                while (nextCrawl < crawls.Length && crawls[nextCrawl] + crawlLife(f) < time)
                    nextCrawl++;

                ClearBullets();
            }

            private void makeGrid(BulletField f, double now)
            {
                gridLow = f.stageMin;
                gridHigh = f.stageMax;
                Vector2 size = f.stageMax - f.stageMin;
                float side = Math.Max(f.circleRadius * 2f * DanmakusuBalance.MOMOYO_BLOCK_CIRCLES, 4f);
                columns = Math.Max((int)(size.X / side), 1);
                rows = Math.Max((int)(size.Y / side), 1);
                cellSize = new Vector2(size.X / columns, size.Y / rows);
                gridMin = f.stageMin;

                var cells = new List<(int Layer, float Key, int Cell)>();

                for (int j = 0; j < rows; j++)
                {
                    for (int i = 0; i < columns; i++)
                    {
                        int layer = Math.Min(Math.Min(i, columns - 1 - i), Math.Min(j, rows - 1 - j));

                        if (layer < DanmakusuBalance.MOMOYO_LAYERS)
                            cells.Add((layer, hash01(i, j, 523), j * columns + i));
                    }
                }

                cells.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Key.CompareTo(b.Key));
                order = cells.ConvertAll(c => c.Cell).ToArray();

                // (the blocks there are keep their places in the order; one whose place this grid has not is gone)
                lying = new int[order.Length];
                Array.Fill(lying, -1);

                for (int k = 0; k < blocks.Count; k++)
                {
                    var block = blocks[k];

                    if (block.GoneAt <= now)
                        continue;

                    if (block.Rank < order.Length)
                        lying[block.Rank] = k;
                    else
                        block.GoneAt = now;
                }
            }

            private Vector2 cellCentre(int cell) => gridMin + new Vector2((cell % columns + 0.5f) * cellSize.X, (cell / columns + 0.5f) * cellSize.Y);

            private Vector2 rankCentre(int rank) => cellCentre(order[Math.Min(rank, order.Length - 1)]);

            private static bool lies(Block block, double t) => t >= block.Lands && t < block.GoneAt;

            /// <summary>Where a block is at <paramref name="t"/>, how big and turned how far (degrees): flying from her, turning and growing to its size as it lands; then in its cell. False before it is thrown or once it is gone.</summary>
            private bool blockAt(Block block, double t, out Vector2 place, out Vector2 size, out float degrees)
            {
                place = rankCentre(block.Rank);
                size = cellSize;
                degrees = 0f;

                if (t < block.Thrown || t >= block.GoneAt)
                    return false;

                if (t < block.Lands)
                {
                    float eased = smoothStep((t - block.Thrown) / Math.Max(block.Lands - block.Thrown, 1));
                    place = Vector2.Lerp(block.From, place, eased);
                    size = cellSize * (0.6f + 0.4f * eased);
                    degrees = (1f - eased) * 360f;
                }

                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                if (order.Length == 0 || Vector2.Distance(gridLow, f.stageMin) > 1f || Vector2.Distance(gridHigh, f.stageMax) > 1f)
                    makeGrid(f, now);

                // her throws: into the first free cell, along the walls first
                while (nextThrow < throws.Length && throws[nextThrow] <= now)
                {
                    double t = throws[nextThrow++];
                    int rank = Array.IndexOf(lying, -1);

                    if (rank < 0)
                        continue; // (every cell is full)

                    lying[rank] = blocks.Count;
                    blocks.Add(new Block { Rank = rank, Thrown = t, Lands = t + beat * DanmakusuBalance.MOMOYO_THROW_BEATS, From = f.bossPositionOf(Period) });
                }

                // the centipedes: from behind the wall at her back, at where the player is
                while (nextCrawl < crawls.Length && crawls[nextCrawl] <= now)
                {
                    double t = crawls[nextCrawl++];
                    Vector2 boss = f.bossPositionOf(Period), player = f.realCharacterPosition;
                    Vector2 back = boss - player;
                    back = back.LengthSquared > 1f ? Vector2.Normalize(back) : -Vector2.UnitY;
                    Vector2 wall = boss + back * (float)f.toWall(boss, back, out _);
                    Vector2 way = player - wall;
                    way = way.LengthSquared > 1f ? Vector2.Normalize(way) : -back;
                    float spacing = crawlerLength(f) / (DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES + 2.5f);

                    var crawler = new Crawler
                    {
                        Warn = t, Appear = t + beat * DanmakusuBalance.MOMOYO_CENTIPEDE_WARN_BEATS, Wall = wall, Way = way, Heading = way,
                        Start = wall - way * (spacing + crawlerWidth(f)),
                    };

                    crawler.Path.Add(crawler.Start);
                    crawlers.Add(crawler);
                }

                double life = crawlLife(f);
                float reach = Math.Min(cellSize.X, cellSize.Y) * 0.5f;
                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;

                foreach (var crawler in crawlers)
                {
                    if (now < crawler.Appear || now >= crawler.GoneAt)
                        continue;

                    walk(f, crawler, now);
                    var pose = poseAt(f, crawler, now);

                    // every block it goes through bursts
                    foreach (var block in blocks)
                    {
                        if (!lies(block, now))
                            continue;

                        Vector2 at = rankCentre(block.Rank);

                        if (Vector2.Distance(at, pose.Middle) > pose.Reach + reach || !touches(pose, at, reach * 0.9f, false))
                            continue;

                        block.GoneAt = now;
                        lying[block.Rank] = -1;
                        burst(f, at, now);
                    }

                    // (gone once it has stopped turning and is wholly off the screen)
                    if (now > crawler.Appear + life || (now > crawler.Appear + chase && offScreen(pose, low, high, crawlerWidth(f) * 3f)))
                        crawler.GoneAt = now;
                }

                crawlers.RemoveAll(c => now > c.GoneAt + 1000);
            }

            /// <summary>Its head goes on to where it is at <paramref name="now"/>, step by step, turning at the player while it chases.</summary>
            private void walk(BulletField f, Crawler crawler, double now)
            {
                float target = speed * (float)(now - crawler.Appear);
                double stepMs = step_px / Math.Max(speed, 1e-4f);
                float most = (float)(DanmakusuBalance.MOMOYO_TURN_DEGREES_PER_BEAT * MathF.PI / 180f / beat * stepMs);
                Vector2 player = f.realCharacterPosition;

                for (int guard = 0; (crawler.Path.Count - 1) * step_px < target && guard < 100000; guard++)
                {
                    Vector2 last = crawler.Path[^1];
                    double at = crawler.Appear + crawler.Path.Count * stepMs;

                    if (at < crawler.Appear + chase)
                    {
                        Vector2 toPlayer = player - last;

                        if (toPlayer.LengthSquared > 1f)
                        {
                            float want = MathF.Atan2(toPlayer.Y, toPlayer.X), now_ = MathF.Atan2(crawler.Heading.Y, crawler.Heading.X);
                            float turn = want - now_;

                            while (turn > MathF.PI)
                                turn -= MathF.Tau;

                            while (turn < -MathF.PI)
                                turn += MathF.Tau;

                            float heading = now_ + Math.Clamp(turn, -most, most);
                            crawler.Heading = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                        }
                    }

                    crawler.Path.Add(last + crawler.Heading * step_px);
                }

                // (its poses were kept for the path as it was)
                foreach (var pose in crawler.Kept)
                    pose.T = double.NaN;
            }

            /// <summary>Where its head's path is <paramref name="distance"/> along it (before it came in: straight behind where it started; past where it has gone: straight on as it goes).</summary>
            private static Vector2 pathAt(Crawler crawler, float distance)
            {
                if (distance <= 0f)
                    return crawler.Start + crawler.Way * distance;

                float index = distance / step_px;
                int last = crawler.Path.Count - 1;

                if (index >= last)
                    return crawler.Path[last] + crawler.Heading * ((index - last) * step_px);

                int i = (int)index;
                return Vector2.Lerp(crawler.Path[i], crawler.Path[i + 1], index - i);
            }

            /// <summary>Where its body is <paramref name="distance"/> along its head's path, swaying from side to side, and the way it goes there.</summary>
            private static Vector2 bodyAt(Crawler crawler, float distance, float sway, float swayLength, out Vector2 along)
            {
                Vector2 ahead = pathAt(crawler, distance + step_px * 2f), behind = pathAt(crawler, distance - step_px * 2f);
                Vector2 tangent = ahead - behind;
                tangent = tangent.LengthSquared > 1e-6f ? Vector2.Normalize(tangent) : crawler.Way;
                Vector2 normal = new Vector2(-tangent.Y, tangent.X);
                float phase = distance / swayLength * MathF.Tau;

                // (the way it goes there takes the sway into account too)
                float slope = sway * MathF.Tau / swayLength * MathF.Cos(phase);
                along = Vector2.Normalize(tangent + normal * slope);
                return pathAt(crawler, distance) + normal * (sway * MathF.Sin(phase));
            }

            private static Vector2 turned(Vector2 way, float radians) => new Vector2(way.X * MathF.Cos(radians) - way.Y * MathF.Sin(radians), way.X * MathF.Sin(radians) + way.Y * MathF.Cos(radians));

            /// <summary>Where every piece of a centipede is at <paramref name="t"/> (kept for a few moments: the autoplay asks about the same ones many times).</summary>
            private Pose poseAt(BulletField f, Crawler crawler, double t)
            {
                foreach (var kept in crawler.Kept)
                {
                    if (kept.T == t)
                        return kept;
                }

                var pose = crawler.Kept[crawler.NextKept];
                crawler.NextKept = (crawler.NextKept + 1) % crawler.Kept.Length;
                pose.T = t;

                int n = DanmakusuBalance.MOMOYO_CENTIPEDE_PLATES;
                float length = crawlerLength(f), width = crawlerWidth(f);
                float spacing = length / (n + 2.5f);
                float sway = width * DanmakusuBalance.MOMOYO_SWAY, swayLength = Math.Max(length * DanmakusuBalance.MOMOYO_SWAY_LENGTH, 1f);
                float went = speed * (float)(t - crawler.Appear);
                float seconds = (float)((t - crawler.Appear) / 1000.0);
                float stroke = DanmakusuBalance.MOMOYO_LEG_STROKE_DEGREES * MathF.PI / 180f;
                float stride = Math.Max(f.circleRadius * DanmakusuBalance.MOMOYO_LEG_STRIDE_CIRCLES, 1f);
                float walking = went / stride * MathF.Tau;
                int limbs = 0;

                // the head
                pose.Head = bodyAt(crawler, went - spacing * 0.5f, sway, swayLength, out Vector2 headWay);
                pose.HeadAngle = MathF.Atan2(headWay.Y, headWay.X);

                // the plates and their legs
                for (int i = 0; i < n; i++)
                {
                    float share = i / (float)Math.Max(n - 1, 1);
                    Vector2 at = bodyAt(crawler, went - spacing * (1.4f + i), sway, swayLength, out Vector2 way);
                    float plateWidth = width * (1f - 0.45f * Math.Clamp((share - 0.55f) / 0.45f, 0f, 1f));
                    float rock = 0.05f * MathF.Sin(walking * 0.5f - i * 0.7f); // (each plate rocks a little on its joint)

                    pose.Plates[i] = at;
                    pose.Angles[i] = MathF.Atan2(way.Y, way.X) + rock;
                    pose.Widths[i] = plateWidth;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 outward = new Vector2(-way.Y, way.X) * side;
                        Vector2 root = at + outward * (plateWidth * 0.42f);
                        float wave = walking - i * MathF.Tau * DanmakusuBalance.MOMOYO_LEG_WAVES / n + (side > 0 ? MathF.PI : 0f);
                        float forward = (30f - 70f * share) * MathF.PI / 180f + stroke * MathF.Sin(wave);
                        Vector2 upper = outward * MathF.Cos(forward) + way * MathF.Sin(forward);
                        Vector2 knee = root + upper * (width * DanmakusuBalance.MOMOYO_LEG_UPPER);
                        float bent = forward - (50f + 12f * MathF.Cos(wave)) * MathF.PI / 180f;
                        Vector2 lower = outward * MathF.Cos(bent) + way * MathF.Sin(bent);
                        Vector2 foot = knee + lower * (width * DanmakusuBalance.MOMOYO_LEG_LOWER);

                        pose.Limbs[limbs++] = (root, knee, width * 0.13f);
                        pose.Limbs[limbs++] = (knee, foot, width * 0.1f);
                    }
                }

                pose.Legs = limbs;

                // the long feelers at its tail, swaying
                {
                    Vector2 tailWay = new Vector2(MathF.Cos(pose.Angles[n - 1]), MathF.Sin(pose.Angles[n - 1]));
                    Vector2 root = pose.Plates[n - 1] - tailWay * (spacing * 0.5f);

                    for (int side = -1; side <= 1; side += 2)
                    {
                        float spread = (14f + 6f * MathF.Sin(seconds * 3.1f + side)) * MathF.PI / 180f * side;
                        Vector2 first = turned(-tailWay, spread);
                        Vector2 middle = root + first * (width * 1.1f);
                        Vector2 second = turned(first, (8f + 5f * MathF.Sin(seconds * 4.3f + side * 2f)) * MathF.PI / 180f * side);
                        pose.Limbs[limbs++] = (root, middle, width * 0.12f);
                        pose.Limbs[limbs++] = (middle, middle + second * (width * 1.2f), width * 0.09f);
                    }
                }

                // the fangs at its head, opening and closing
                Vector2 headAhead = new Vector2(MathF.Cos(pose.HeadAngle), MathF.Sin(pose.HeadAngle));
                Vector2 headSide = new Vector2(-headAhead.Y, headAhead.X);

                for (int side = -1; side <= 1; side += 2)
                {
                    float open = (20f + 18f * (0.5f + 0.5f * MathF.Sin(seconds * 5f))) * MathF.PI / 180f * side;
                    Vector2 root = pose.Head + headAhead * (width * 0.3f) + headSide * (width * 0.2f * side);
                    pose.Limbs[limbs++] = (root, root + turned(headAhead, open) * (width * 0.45f), width * 0.14f);
                }

                pose.Fangs = 2;
                pose.Hurting = limbs;

                // the feelers at its head, waving (they do not hurt)
                for (int side = -1; side <= 1; side += 2)
                {
                    float spread = (28f + 9f * MathF.Sin(seconds * 2.3f + side * 1.3f)) * MathF.PI / 180f * side;
                    Vector2 root = pose.Head + headAhead * (width * 0.4f) + headSide * (width * 0.16f * side);
                    Vector2 first = turned(headAhead, spread);
                    Vector2 middle = root + first * (width * 1.0f);
                    Vector2 second = turned(first, (12f + 6f * MathF.Sin(seconds * 3.7f + side)) * MathF.PI / 180f * side);
                    pose.Limbs[limbs++] = (root, middle, width * 0.08f);
                    pose.Limbs[limbs++] = (middle, middle + second * (width * 1.1f), width * 0.06f);
                }

                pose.Count = limbs;

                // (a circle round all of it, to pass it over quickly)
                pose.Middle = pose.Plates[n / 2];
                float reach = 0f;

                for (int i = 0; i < pose.Count; i++)
                    reach = Math.Max(reach, Math.Max(Vector2.Distance(pose.Limbs[i].A, pose.Middle), Vector2.Distance(pose.Limbs[i].B, pose.Middle)));

                pose.Reach = Math.Max(reach, Vector2.Distance(pose.Head, pose.Middle) + width) + width;
                return pose;
            }

            /// <summary>Is <paramref name="point"/> within <paramref name="reach"/> of the centipede (its plates, its head and its limbs; its head's feelers too if <paramref name="feelers"/>)?</summary>
            private static bool touches(Pose pose, Vector2 point, float reach, bool feelers)
            {
                if (Vector2.Distance(point, pose.Head) <= pose.Widths[0] * 0.48f + reach)
                    return true;

                for (int i = 0; i < pose.Plates.Length; i++)
                {
                    if (Vector2.Distance(point, pose.Plates[i]) <= pose.Widths[i] * 0.45f + reach)
                        return true;
                }

                int count = feelers ? pose.Count : pose.Hurting;

                for (int i = 0; i < count; i++)
                {
                    var (a, b, thick) = pose.Limbs[i];

                    if (distanceToSegment(point, a, b) <= thick * 0.4f + reach)
                        return true;
                }

                return false;
            }

            private static bool offScreen(Pose pose, Vector2 low, Vector2 high, float margin)
            {
                bool outside(Vector2 p) => p.X < low.X - margin || p.X > high.X + margin || p.Y < low.Y - margin || p.Y > high.Y + margin;

                if (!outside(pose.Head))
                    return false;

                foreach (Vector2 plate in pose.Plates)
                {
                    if (!outside(plate))
                        return false;
                }

                return true;
            }

            /// <summary>A block bursts into a square of grey bullets that keeps its shape as it flies out.</summary>
            private void burst(BulletField f, Vector2 at, double t)
            {
                float radius = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period);
                int count = DanmakusuBalance.MOMOYO_BURST_BULLETS;

                for (int i = 0; i < count; i++)
                {
                    float s = i * 4f / count;
                    int edge = (int)s;
                    float along = (s - edge) * 2f - 1f;
                    Vector2 p = edge switch
                    {
                        0 => new Vector2(along, -1f),
                        1 => new Vector2(1f, along),
                        2 => new Vector2(-along, 1f),
                        _ => new Vector2(-1f, -along),
                    };
                    Vector2 velocity = p * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(i, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Tint = BulletRenderer.TINT_GRAY,
                        TintAmount = 1f,
                        Untraced = true,
                        Until = t + f.stageLife(velocity),
                    });
                }

                f.addFlash(t, at, radius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 2f);
            }

            /// <summary>The blocks hurt like a slider body where they are drawn, flying or lying; so does the centipede (not its head's feelers).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || order.Length == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float hit = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                if (now < Period.End)
                {
                    foreach (var block in blocks)
                    {
                        if (!blockAt(block, now, out Vector2 place, out Vector2 size, out float degrees))
                            continue;

                        // (in the block's own frame: it turns as it flies)
                        Vector2 off = player - place;
                        float angle = -degrees * MathF.PI / 180f;
                        Vector2 local = new Vector2(off.X * MathF.Cos(angle) - off.Y * MathF.Sin(angle), off.X * MathF.Sin(angle) + off.Y * MathF.Cos(angle));

                        if (MathF.Abs(local.X) <= size.X * 0.5f + hit && MathF.Abs(local.Y) <= size.Y * 0.5f + hit)
                            return true;
                    }
                }

                foreach (var crawler in crawlers)
                {
                    if (now < crawler.Appear || now >= crawler.GoneAt)
                        continue;

                    var pose = poseAt(f, crawler, now);

                    if (Vector2.Distance(pose.Middle, player) <= pose.Reach + hit && touches(pose, player, hit, false))
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || order.Length == 0)
                    return;

                float fade = 1f - smoothStep((t - Period.End) / beat);

                if (fade > 0.01f)
                {
                    // (those lying first, so that the ones flying over them are seen)
                    foreach (var block in blocks)
                    {
                        if (lies(block, t))
                            f.stageBack.Add(StageTexture.MomoyoStone, rankCentre(block.Rank), cellSize, 0f, fade, Color4.White);
                    }

                    foreach (var block in blocks)
                    {
                        if (t < block.Lands && blockAt(block, t, out Vector2 place, out Vector2 size, out float degrees))
                            f.stageBack.Add(StageTexture.MomoyoStone, place, size, degrees, fade, Color4.White);
                    }
                }

                foreach (var crawler in crawlers)
                {
                    // (a red arrow at the wall it will come in by)
                    if (t >= crawler.Warn && t < crawler.Appear)
                    {
                        double u = (t - crawler.Warn) / Math.Max(crawler.Appear - crawler.Warn, 1);
                        float level = smoothStep(u / 0.25) * (0.6f + 0.4f * MathF.Cos((float)u * MathF.PI * 6f));
                        float arrow = f.circleRadius * 2.4f;
                        Vector2 place = Vector2.Clamp(crawler.Wall, f.stageMin, f.stageMax) + crawler.Way * arrow;
                        f.stageRipple.Add(StageTexture.SeijaArrow, place, new Vector2(arrow), MathF.Atan2(crawler.Way.Y, crawler.Way.X) * 180f / MathF.PI, level, new Color4(1f, 0.2f, 0.15f, 1f));
                        continue;
                    }

                    if (t < crawler.Appear || t >= crawler.GoneAt)
                        continue;

                    drawCrawler(f, poseAt(f, crawler, t));
                }
            }

            /// <summary>The centipede, piece by piece: its legs and its tail's feelers, its fangs, its plates from its tail on, its head, the feelers at its head.</summary>
            private void drawCrawler(BulletField f, Pose pose)
            {
                int n = pose.Plates.Length;
                float width = crawlerWidth(f);
                float spacing = crawlerLength(f) / (n + 2.5f);

                for (int i = 0; i < pose.Hurting; i++)
                    drawLimb(f, pose.Limbs[i]);

                for (int i = n - 1; i >= 0; i--)
                {
                    // (the plate's picture is as long as 0.6 of it and as wide as 0.97 of it)
                    f.stageBack.Add(StageTexture.CentipedePlate, pose.Plates[i], new Vector2(spacing * 1.3f / 0.6f, pose.Widths[i] / 0.97f), pose.Angles[i] * 180f / MathF.PI, 1f, Color4.White);
                }

                f.stageBack.Add(StageTexture.CentipedeHead, pose.Head, new Vector2(width * 1.05f / 0.82f, width * 0.95f / 0.8f), pose.HeadAngle * 180f / MathF.PI, 1f, Color4.White);

                for (int i = pose.Hurting; i < pose.Count; i++)
                    drawLimb(f, pose.Limbs[i]);
            }

            /// <summary>One joint of a limb, from <paramref name="limb"/>'s A to its B, as thick as it is at its root.</summary>
            private static void drawLimb(BulletField f, (Vector2 A, Vector2 B, float Thick) limb)
            {
                Vector2 along = limb.B - limb.A;
                float length = along.Length;

                if (length < 0.5f)
                    return;

                along /= length;
                float rod = StageLayer.CENTIPEDE_LEG_TO - StageLayer.CENTIPEDE_LEG_FROM;
                float whole = length / rod;

                // (the picture's middle is not the rod's: it is shifted towards the thin end)
                Vector2 centre = (limb.A + limb.B) * 0.5f - along * (whole * ((StageLayer.CENTIPEDE_LEG_FROM + StageLayer.CENTIPEDE_LEG_TO) * 0.5f - 0.5f));
                f.stageBack.Add(StageTexture.CentipedeLeg, centre, new Vector2(whole, limb.Thick / StageLayer.CENTIPEDE_LEG_ROOT), MathF.Atan2(along.Y, along.X) * 180f / MathF.PI, 1f, Color4.White);
            }
        }
    }
}
