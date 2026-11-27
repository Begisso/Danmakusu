using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Momoyo Himemushi (2026-11-27). The screen is a grid of cells as big as an osu! circle (MOMOYO_BLOCK_CIRCLES x its diameter, stretched a little so that they fit it exactly, wall to wall). In her
        // attacks she throws a block of stone (the user's stone picture) every MOMOYO_THROW_EVERY_BEATS into the next free cell along the walls - the cells against the walls first, then the ring inside
        // them, MOMOYO_LAYERS rings at most, each ring in an order of its own: it flies from her for MOMOYO_THROW_BEATS and lies there, hurting like a slider body, until her section is over. In her pauses
        // (at the start of each, and every MOMOYO_CENTIPEDE_EVERY_BEATS of a long one) a big centipede (the user's picture, MOMOYO_CENTIPEDE_CIRCLES circle radii long) comes in from behind the wall at her
        // back - a red arrow shows where for MOMOYO_CENTIPEDE_WARN_BEATS - and charges straight across at where the player was, MOMOYO_CENTIPEDE_SPEED times a bullet's speed, and out of the screen. It hurts
        // like a slider body where the picture is (its mask), and every block it goes through bursts into a square of MOMOYO_BURST_BULLETS grey bullets that keeps its shape as it flies out; the cell is
        // free again. Where she and the player are is taken as each block is thrown and each centipede comes; a seek back lays the blocks again from the start.
        // ======================================================================================================================================================

        private sealed class MiningAct : StageAct
        {
            private sealed class Block
            {
                public int Cell;
                public double Thrown, Lands;
                public Vector2 From;
                public double GoneAt = double.PositiveInfinity;
            }

            private sealed class Crawler
            {
                public double Warn, Appear;
                public Vector2 Start, Way, Wall;
            }

            private readonly double beat;
            private readonly double[] throws;
            private readonly double[] crawls;
            private readonly List<Block> blocks = new List<Block>();
            private readonly List<Crawler> crawlers = new List<Crawler>();
            private int nextThrow, nextCrawl;
            private double resetTime = double.NegativeInfinity;

            // (the grid: made once the screen is known)
            private int columns, rows;
            private Vector2 cellSize, gridMin;
            private int[] order = Array.Empty<int>();
            private int[] lying = Array.Empty<int>(); // (which block is in each cell, -1: none)

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
                End = period.End + beat * 32;
            }

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.MOMOYO_CENTIPEDE_SPEED;

            private float crawlerLength(BulletField f) => f.circleRadius * DanmakusuBalance.MOMOYO_CENTIPEDE_CIRCLES;

            private float crawlerHeight(BulletField f) => crawlerLength(f) * Mask.Height / Mask.Width;

            private double crawlLife(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) + crawlerLength(f) * 2f) / Math.Max(speed, 1e-3f);

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

                resetTime = time;
                ClearBullets();
            }

            private void makeGrid(BulletField f)
            {
                Vector2 size = f.stageMax - f.stageMin;
                float side = Math.Max(f.circleRadius * 2f * DanmakusuBalance.MOMOYO_BLOCK_CIRCLES, 4f);
                columns = Math.Max((int)(size.X / side), 1);
                rows = Math.Max((int)(size.Y / side), 1);
                cellSize = new Vector2(size.X / columns, size.Y / rows);
                gridMin = f.stageMin;
                lying = new int[columns * rows];
                Array.Fill(lying, -1);

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
            }

            private Vector2 cellCentre(int cell) => gridMin + new Vector2((cell % columns + 0.5f) * cellSize.X, (cell / columns + 0.5f) * cellSize.Y);

            private static bool lies(Block block, double t) => t >= block.Lands && t < block.GoneAt;

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                if (order.Length == 0)
                    makeGrid(f);

                // her throws: into the first free cell, along the walls first
                while (nextThrow < throws.Length && throws[nextThrow] <= now)
                {
                    double t = throws[nextThrow++];
                    int cell = -1;

                    foreach (int c in order)
                    {
                        if (lying[c] < 0)
                        {
                            cell = c;
                            break;
                        }
                    }

                    if (cell < 0)
                        continue; // (every cell is full)

                    lying[cell] = blocks.Count;
                    blocks.Add(new Block { Cell = cell, Thrown = t, Lands = t + beat * DanmakusuBalance.MOMOYO_THROW_BEATS, From = f.bossPositionOf(Period) });
                }

                // the centipedes: from behind the wall at her back, at where the player is
                while (nextCrawl < crawls.Length && crawls[nextCrawl] <= now)
                {
                    double t = crawls[nextCrawl++];
                    Vector2 boss = f.bossPositionOf(Period), player = f.characterPosition;
                    Vector2 back = boss - player;
                    back = back.LengthSquared > 1f ? Vector2.Normalize(back) : -Vector2.UnitY;
                    Vector2 wall = boss + back * (float)f.toWall(boss, back, out _);
                    Vector2 way = player - wall;
                    way = way.LengthSquared > 1f ? Vector2.Normalize(way) : -back;

                    crawlers.Add(new Crawler
                    {
                        Warn = t, Appear = t + beat * DanmakusuBalance.MOMOYO_CENTIPEDE_WARN_BEATS, Wall = wall, Way = way,
                        Start = wall - way * (crawlerLength(f) * 0.5f + 8f),
                    });
                }

                // every block a centipede goes through bursts
                double life = crawlLife(f);
                float reach = Math.Min(cellSize.X, cellSize.Y) * 0.5f;

                foreach (var crawler in crawlers)
                {
                    if (now < crawler.Appear || now > crawler.Appear + life)
                        continue;

                    Vector2 centre = crawlerAt(crawler, now);
                    float far = crawlerLength(f) * 0.5f + reach;

                    foreach (var block in blocks)
                    {
                        if (!lies(block, now))
                            continue;

                        Vector2 at = cellCentre(block.Cell);

                        if (Vector2.DistanceSquared(at, centre) > far * far || !inCrawler(f, crawler, centre, at, reach))
                            continue;

                        block.GoneAt = now;
                        lying[block.Cell] = -1;
                        burst(f, at, now);
                    }
                }

                crawlers.RemoveAll(c => now > c.Appear + life);
            }

            private Vector2 crawlerAt(Crawler crawler, double t) => crawler.Start + crawler.Way * (speed * (float)Math.Max(t - crawler.Appear, 0));

            /// <summary>Is <paramref name="point"/> within <paramref name="reach"/> of the centipede's body (its picture's mask)?</summary>
            private bool inCrawler(BulletField f, Crawler crawler, Vector2 centre, Vector2 point, float reach)
            {
                // (the picture's head is on its left: its x axis points from the head back, against the way it goes)
                Vector2 back = -crawler.Way;
                Vector2 side = new Vector2(-back.Y, back.X);
                Vector2 off = point - centre;
                float length = crawlerLength(f), height = crawlerHeight(f);
                float u = Vector2.Dot(off, back) / length + 0.5f, v = Vector2.Dot(off, side) / height + 0.5f;
                float ru = reach / length, rv = reach / height;

                if (u < -ru || u > 1f + ru || v < -rv || v > 1f + rv)
                    return false;

                if (Mask.At(u, v))
                    return true;

                for (int k = 0; k < 8; k++)
                {
                    float angle = k * MathF.Tau / 8f;

                    if (Mask.At(u + MathF.Cos(angle) * ru, v + MathF.Sin(angle) * rv))
                        return true;
                }

                return false;
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

            /// <summary>The blocks that lie hurt like a slider body; so does the centipede, where its picture is.</summary>
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
                        if (!lies(block, now))
                            continue;

                        Vector2 off = player - cellCentre(block.Cell);

                        if (MathF.Abs(off.X) <= cellSize.X * 0.5f + hit && MathF.Abs(off.Y) <= cellSize.Y * 0.5f + hit)
                            return true;
                    }
                }

                double life = crawlLife(f);

                foreach (var crawler in crawlers)
                {
                    if (now < crawler.Appear || now > crawler.Appear + life)
                        continue;

                    Vector2 centre = crawlerAt(crawler, now);

                    if (Vector2.Distance(centre, player) <= crawlerLength(f) * 0.5f + hit && inCrawler(f, crawler, centre, player, hit))
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
                    foreach (var block in blocks)
                    {
                        if (t < block.Thrown || t >= block.GoneAt)
                            continue;

                        Vector2 at = cellCentre(block.Cell);

                        // (it flies from her, turning, and grows to its size as it lands)
                        if (t < block.Lands)
                        {
                            float u = (float)((t - block.Thrown) / Math.Max(block.Lands - block.Thrown, 1));
                            float eased = smoothStep(u);
                            Vector2 place = Vector2.Lerp(block.From, at, eased);
                            f.stageBack.Add(StageTexture.MomoyoStone, place, cellSize * (0.6f + 0.4f * eased), (1f - eased) * 360f, fade, Color4.White);
                            continue;
                        }

                        f.stageBack.Add(StageTexture.MomoyoStone, at, cellSize, 0f, fade, Color4.White);
                    }
                }

                double life = crawlLife(f);

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

                    if (t < crawler.Appear || t > crawler.Appear + life)
                        continue;

                    Vector2 back = -crawler.Way;
                    f.stageBack.Add(StageTexture.Centipede, crawlerAt(crawler, t), new Vector2(crawlerLength(f), crawlerHeight(f)), MathF.Atan2(back.Y, back.X) * 180f / MathF.PI, 1f, Color4.White);
                }
            }

            /// <summary>Where the centipede's picture is (not see-through), in cells, read from the picture itself once.</summary>
            private static class Mask
            {
                public static readonly int Width, Height;
                private static readonly bool[,] cells;
                private const int cell_px = 2;

                static Mask()
                {
                    using var stream = typeof(MiningAct).Assembly.GetManifestResourceStream("osu.Game.Rulesets.Danmakusu.Resources.Textures.momoyo_centipede.png");

                    if (stream == null)
                    {
                        Width = 400;
                        Height = 211;
                        cells = new bool[1, 1];
                        return;
                    }

                    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                    Width = image.Width;
                    Height = image.Height;
                    cells = new bool[(Width + cell_px - 1) / cell_px, (Height + cell_px - 1) / cell_px];

                    for (int y = 0; y < Height; y++)
                    {
                        for (int x = 0; x < Width; x++)
                        {
                            if (image[x, y].A >= 128)
                                cells[x / cell_px, y / cell_px] = true;
                        }
                    }
                }

                /// <summary>Is the picture there at (<paramref name="u"/>, <paramref name="v"/>), 0..1 across and down?</summary>
                public static bool At(float u, float v)
                {
                    if (u < 0f || v < 0f || u >= 1f || v >= 1f)
                        return false;

                    return cells[Math.Min((int)(u * cells.GetLength(0)), cells.GetLength(0) - 1), Math.Min((int)(v * cells.GetLength(1)), cells.GetLength(1) - 1)];
                }
            }
        }
    }
}
