using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Medicine Melancholy (2026-11-27): all through her section, when two bullets of different rings touch (their bodies, after MEDICINE_ARM_BEATS of flight - 200 ms at first, on the screen) both are gone, and where they met
        // a small flower of MEDICINE_PETALS of Yuyuko's tree's petals (pink, MEDICINE_PETAL_SIZE as big as a plain bullet) bursts out, flying away at a bullet's speed. The bullets of this frame are put on a
        // grid as they are looked at (as Misumaru's are); the one seen second goes at once, the other a frame later. In her attacks she shoots streams from all round her magic circle: MEDICINE_STREAMS points
        // on its edge, evenly, turning MEDICINE_TURN_DEGREES_PER_BEAT, each a bullet every MEDICINE_STREAM_BEATS straight out, MEDICINE_STREAM_SPEED times a bullet's speed (into a pool of acts).
        // ======================================================================================================================================================

        private sealed class PoisonAct : StageAct
        {
            private struct Entry
            {
                public Vector2 Position;
                public float Radius;
                public BulletEmitter Emitter;
                public int Bullet;
                public int Next;
            }

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly Dictionary<long, int> cells = new Dictionary<long, int>();
            private readonly List<Entry> entries = new List<Entry>();
            private readonly Dictionary<(BulletEmitter, int), double> doomed = new Dictionary<(BulletEmitter, int), double>();
            private double gridTime = double.NaN;
            private double streamNext = double.NaN;
            private int burst;

            public ShotPool? Pool;

            public PoisonAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                attacks = BossOps.Attacks(period);
                Start = period.ShootStart;
                End = period.End + beat * 16;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                cells.Clear();
                entries.Clear();
                doomed.Clear();
                gridTime = double.NaN;
                streamNext = double.NaN;
                burst = 0;
                ClearBullets();
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

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (a new grid every frame)
                gridTime = now;
                cells.Clear();
                entries.Clear();

                if (doomed.Count > 512)
                {
                    var old = new List<(BulletEmitter, int)>();

                    foreach (var (key, when) in doomed)
                    {
                        if (now - when > 500)
                            old.Add(key);
                    }

                    foreach (var key in old)
                        doomed.Remove(key);
                }

                streams(f, now);
            }

            /// <summary>Her streams: a bullet out of every point round her circle every so often while she attacks.</summary>
            private void streams(BulletField f, double now)
            {
                double every = beat * DanmakusuBalance.MEDICINE_STREAM_BEATS;

                if (double.IsNaN(streamNext))
                    streamNext = Period.ShootStart + Math.Ceiling(Math.Max(now - every * 2 - Period.ShootStart, 0) / every) * every;

                Vector2 centre = f.bossPositionOf(Period);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.MEDICINE_STREAM_SPEED;
                float radius = f.stageBaseRadius(Period);
                int guard = 0;

                for (; streamNext <= now && streamNext < Period.End && guard++ < 64; streamNext += every)
                {
                    double t = streamNext;

                    if (!inAttack(t))
                        continue;

                    float turn = (float)((t - Period.ShootStart) / beat) * DanmakusuBalance.MEDICINE_TURN_DEGREES_PER_BEAT * MathF.PI / 180f;
                    int n = (int)Math.Round((t - Period.ShootStart) / every);

                    for (int k = 0; k < DanmakusuBalance.MEDICINE_STREAMS; k++)
                    {
                        float angle = turn + k * MathF.Tau / DanmakusuBalance.MEDICINE_STREAMS;
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        Vector2 velocity = way * speed;
                        var made = new StageBullet
                        {
                            T0 = t,
                            P0 = centre + way * DanmakusuBalance.MAGIC_CIRCLE_RADIUS,
                            V = velocity,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = f.comboStyle(k + n, true, Period.Rank),
                            Shape = BulletShape.Circle,
                            Until = t + f.stageLife(velocity),
                        };

                        if (Pool != null)
                            Pool.Add(made);
                        else
                            AddBullet(made);
                    }
                }
            }

            private float cellSize(BulletField f) => Math.Max(f.stageBaseRadius(Period) * 4f, 4f);

            private static long cellOf(long x, long y) => (x << 32) ^ (uint)(int)y;

            /// <summary>Two bullets of different rings that touch are both gone, and a small flower of petals bursts where they met.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t != gridTime || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                var key = (emitter, bullet);

                // (the other one of a pair that touched: it goes now)
                if (doomed.Remove(key))
                    return true;

                if (t - emitter.Time < beat * DanmakusuBalance.MEDICINE_ARM_BEATS || to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                float size = cellSize(f);
                float body = emitter.VisualRadius * emitter.BigScale(bullet) * 0.9f;
                long cx = (long)MathF.Floor(to.X / size), cy = (long)MathF.Floor(to.Y / size);

                for (long a = cx - 1; a <= cx + 1; a++)
                {
                    for (long b = cy - 1; b <= cy + 1; b++)
                    {
                        if (!cells.TryGetValue(cellOf(a, b), out int at))
                            continue;

                        for (; at >= 0; at = entries[at].Next)
                        {
                            var other = entries[at];

                            if (ReferenceEquals(other.Emitter, emitter) || other.Emitter == null)
                                continue;

                            if (Vector2.DistanceSquared(to, other.Position) > (body + other.Radius) * (body + other.Radius))
                                continue;

                            // (both go: this one now, the other a frame later; it is taken off the grid so that nothing else meets it)
                            doomed[(other.Emitter, other.Bullet)] = t;
                            entries[at] = new Entry { Position = other.Position, Radius = other.Radius, Emitter = null!, Bullet = -1, Next = other.Next };
                            flower(f, t, (to + other.Position) * 0.5f, Math.Max(body, other.Radius));
                            return true;
                        }
                    }
                }

                var entry = new Entry { Position = to, Radius = body, Emitter = emitter, Bullet = bullet };
                long cell = cellOf(cx, cy);
                entry.Next = cells.TryGetValue(cell, out int head) ? head : -1;
                cells[cell] = entries.Count;
                entries.Add(entry);
                return false;
            }

            private void flower(BulletField f, double t, Vector2 at, float size)
            {
                int number = burst++;
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.MEDICINE_PETAL_SIZE;
                float speed = stageBulletSpeed(Period);
                float turn = hash01(Id, number, 311) * MathF.Tau;

                for (int k = 0; k < DanmakusuBalance.MEDICINE_PETALS; k++)
                {
                    float angle = turn + k * MathF.Tau / DanmakusuBalance.MEDICINE_PETALS;
                    Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 velocity = outward * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at + outward * (size * 0.5f),
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(number + k, false, Period.Rank),
                        Shape = BulletShape.Petal,
                        Tint = BulletRenderer.TINT_PINK,
                        TintAmount = 1f,
                        Untraced = true,
                        Until = t + f.stageLife(velocity),
                    });
                }

                f.addFlash(t, at, size * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f);
            }
        }
    }
}
