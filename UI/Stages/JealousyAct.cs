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
        // Parsee Mizuhashi (2026-11-24): every PARSEE_BALL_EVERY_BEATS of her attacks PARSEE_BALLS big green balls (PARSEE_BALL_CIRCLES circle radii) in a fan PARSEE_FAN_DEGREES wide at the player, at
        // PARSEE_BALL_SPEED times a plain bullet's speed. Every PARSEE_FLOWER_EVERY_BEATS of a ball's flight on the screen a flower comes out where it is - Yuyuko's, five pink petals round a dark middle - that
        // stands PARSEE_FLOWER_BEATS, its petals hurting as bullets do; then they fly apart, straight out from the middle. A ball that has hit leaves no more flowers.
        // Her clone stands in the middle of the screen, see-through (Seija's clone, BulletField.Boss), all through her section; it shoots her arrowheads (the planner's, BulletEmitter.FansOnLeaving). One that
        // leaves the screen fans out PARSEE_FAN_BULLETS basic bullets, PARSEE_FAN_SIZE as big as a plain one, PARSEE_FAN_SPREAD_DEGREES wide, back in from where it left, square to that edge (when it left is
        // worked out from its flight, so a seek makes the same fans).
        // (2026-11-24: the flowers are as small as Yuyuko's tree's, scattered round each ball's way - a little earlier or later and to one side or the other, PARSEE_FLOWER_SCATTER of its radius at most, by a hash -
        // every quarter beat, and their petals fly off as the tree's: three of each out of it, two falling straight down.)
        // Where the balls go is where the player was when they were thrown: that is taken then and kept.
        // ======================================================================================================================================================

        private sealed class JealousyAct : StageAct
        {
            private sealed class Volley
            {
                public double T;
                public Vector2 From;
                public Vector2 Aim; // (unit: at the player as it was thrown)
                public readonly int[] Index = new int[DanmakusuBalance.PARSEE_BALLS]; // (each ball's bullet in Bullets)
                public readonly int[] Flowers = new int[DanmakusuBalance.PARSEE_BALLS]; // (how many flowers each has left)
                public readonly bool[] Done = new bool[DanmakusuBalance.PARSEE_BALLS];
                public bool Made;
            }

            private readonly struct Flower
            {
                public readonly Vector2 At;
                public readonly double T;
                public readonly float Length; // (of a petal, px: as Yuyuko's tree's)

                public Flower(Vector2 at, double t, float length)
                {
                    At = at;
                    T = t;
                    Length = length;
                }
            }

            private readonly double beat;
            private readonly double[] throwTimes;
            private readonly List<Volley> volleys = new List<Volley>();
            private readonly List<Flower> flowers = new List<Flower>();
            private readonly BulletEmitter[] arrows;
            private readonly HashSet<BulletEmitter> fanned = new HashSet<BulletEmitter>();
            private int firstArrow; // (the arrows before it have all fanned or are gone)

            public JealousyAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var times = new List<double>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = start; t < end; t += beat * DanmakusuBalance.PARSEE_BALL_EVERY_BEATS)
                        times.Add(t);
                }

                throwTimes = times.ToArray();

                var list = new List<BulletEmitter>();

                foreach (var ring in f.emitters)
                {
                    if (ring.FansOnLeaving && ring.BossRank == period.Rank && ring.Time >= period.ShootStart && ring.Time < period.End)
                        list.Add(ring);
                }

                arrows = list.ToArray();
                End = period.End + beat * (DanmakusuBalance.PARSEE_FLOWER_BEATS + 32);
            }

            private float ballRadius(BulletField f) => f.circleRadius * DanmakusuBalance.PARSEE_BALL_CIRCLES;

            private float ballSpeed => stageBulletSpeed(Period) * DanmakusuBalance.PARSEE_BALL_SPEED;

            private double flowerMs => beat * DanmakusuBalance.PARSEE_FLOWER_BEATS;

            public override double MechanicsEnd => throwTimes.Length > 0 ? throwTimes[^1] + flowerMs : double.NegativeInfinity;

            public override void Reset(BulletField f, double time)
            {
                // (the volleys thrown by then keep where they were aimed; their bullets, flowers and the fans are made again)
                volleys.RemoveAll(v => v.T > time);

                foreach (var volley in volleys)
                {
                    volley.Made = false;
                    Array.Clear(volley.Flowers);
                    Array.Clear(volley.Done);
                }

                flowers.Clear();
                fanned.Clear();
                firstArrow = 0;
                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (the clone, in the middle of the screen while her section lasts)
                float presence = smoothStep((now - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - now) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (presence > 0f)
                {
                    f.seijaCloneLevel = Math.Max(f.seijaCloneLevel, presence * DanmakusuBalance.PARSEE_CLONE_ALPHA / DanmakusuBalance.SEIJA_CLONE_ALPHA);
                    f.seijaCloneAt = (f.stageMin + f.stageMax) / 2f;
                    f.seijaCloneSlot = Period.BossSlot;
                    f.seijaCloneTurn = now - 100000; // (no stars fly in to it)
                }

                // (every volley whose time has come is aimed at the player, from where she is)
                while (volleys.Count < throwTimes.Length && throwTimes[volleys.Count] <= now)
                {
                    Vector2 from = f.bossPositionOf(Period);
                    Vector2 way = f.characterPosition - from;
                    volleys.Add(new Volley { T = throwTimes[volleys.Count], From = from, Aim = way.LengthSquared > 1e-6f ? Vector2.Normalize(way) : Vector2.UnitY });
                }

                foreach (var volley in volleys)
                {
                    if (!volley.Made)
                        makeVolley(f, volley);

                    for (int i = 0; i < volley.Index.Length; i++)
                        sow(f, volley, i, now);
                }

                fans(f, now);
            }

            private Vector2 ballVelocity(Volley volley, int i)
            {
                int count = DanmakusuBalance.PARSEE_BALLS;
                float spread = DanmakusuBalance.PARSEE_FAN_DEGREES * MathF.PI / 180f;
                float angle = MathF.Atan2(volley.Aim.Y, volley.Aim.X) + (count > 1 ? -spread / 2f + i * spread / (count - 1) : 0f);
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ballSpeed;
            }

            private void makeVolley(BulletField f, Volley volley)
            {
                float radius = ballRadius(f);

                for (int i = 0; i < volley.Index.Length; i++)
                {
                    Vector2 v = ballVelocity(volley, i);
                    volley.Index[i] = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = volley.T,
                        P0 = volley.From,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(i, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Until = volley.T + f.stageLife(v) + radius / Math.Max(ballSpeed, 1e-4f),
                        Hidden = true,
                        Untraced = true, // (it flies its own straight line: the flowers are where it is)
                    });

                    if (Bullets.Count <= volley.Index[i])
                        volley.Index[i] = int.MaxValue;
                }

                volley.Made = true;
            }

            private bool consumed(BulletField f, int index) => DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index);

            /// <summary>The flowers of ball <paramref name="i"/> whose time has come, while it is on the screen and has not hit.</summary>
            private void sow(BulletField f, Volley volley, int i, double now)
            {
                if (volley.Done[i] || volley.Index[i] >= Bullets.Count)
                    return;

                double every = beat * DanmakusuBalance.PARSEE_FLOWER_EVERY_BEATS;
                Vector2 v = ballVelocity(volley, i);
                Vector2 side = v.LengthSquared > 1e-12f ? Vector2.Normalize(new Vector2(-v.Y, v.X)) : Vector2.UnitX;
                float scatter = ballRadius(f) * DanmakusuBalance.PARSEE_FLOWER_SCATTER;

                for (double t = volley.T + (volley.Flowers[i] + 1) * every; t <= now; t = volley.T + (volley.Flowers[i] + 1) * every)
                {
                    // (scattered round its way, each a little earlier or later and to one side or the other, by its own hash: 2026-11-24, "random and more packed")
                    int number = (int)(volley.T % 100000) * 8 + i * 4096 + volley.Flowers[i];
                    double when = t - every * 0.4 * hash01(Id, number, 141);

                    // (where the ball really is then - on the bullets' clock, which Gengetsu speeds up: 2026-11-25, "flower traces are not keeping up with ball", they were where it would be by the game's time)
                    Vector2 onWay = f.stageBulletAt(Bullets[volley.Index[i]], when, out Vector2 ballThen) ? ballThen : volley.From + v * (float)(when - volley.T);
                    Vector2 at = onWay + side * (scatter * (hash01(Id, number, 142) * 2f - 1f));

                    if (consumed(f, volley.Index[i]) || at.X < f.stageMin.X || at.X > f.stageMax.X || at.Y < f.stageMin.Y || at.Y > f.stageMax.Y)
                    {
                        volley.Done[i] = true;
                        return;
                    }

                    float length = DanmakusuBalance.YUYUKO_PETAL_SIZE * (0.8f + 0.4f * hash01(Id, number, 45)) * treeHeight(f);
                    bloom(f, at, t, flowers.Count, length);
                    flowers.Add(new Flower(at, t, length));
                    volley.Flowers[i]++;
                }
            }

            private static float treeHeight(BulletField f) => DanmakusuBalance.YUYUKO_TREE_HEIGHT * (f.stageMax.Y - f.stageMin.Y);

            /// <summary>
            /// A flower at <paramref name="at"/>, as one of Yuyuko's tree (2026-11-24: its size, its petals - <paramref name="length"/> px long, as far out): five pink petals that stand there, then fly off as the
            /// tree's do - three of them out of the flower, each a little turned and at a speed of its own, the other two falling straight down, keeping the angle they had.
            /// </summary>
            private void bloom(BulletField f, Vector2 at, double t, int number, float length)
            {
                float radius = length * 0.5f;
                float reach = DanmakusuBalance.YUYUKO_PETAL_REACH * length;
                double release = t + flowerMs;
                float baseSpeed = stageBulletSpeed(Period) * DanmakusuBalance.PARSEE_PETAL_SPEED;
                float turn = hash01(Id, number, 47) * MathF.Tau;
                int petals = DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;

                for (int k = 0; k < petals; k++)
                {
                    int key = number * 8 + k;
                    float angle = turn + k * MathF.Tau / petals;
                    var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 p = at + outward * reach;
                    bool falls = (k + number) % petals >= DanmakusuBalance.YUYUKO_SHOT_PETALS;
                    Vector2 way = falls ? Vector2.UnitY : turned(outward, (hash01(Id, key, 3) - 0.5f) * 0.3f);
                    Vector2 v = way * Math.Max(baseSpeed * (0.6f + 0.6f * hash01(Id, key, 4)), 0.02f);

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = p,
                        V = Vector2.Zero,
                        Legs = new[] { new StageLeg { T = release, P = p, V = v } },
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(number + k, true, Period.Rank),
                        Shape = BulletShape.Petal,
                        Tint = BulletRenderer.TINT_PINK,
                        TintAmount = 1f,
                        Face = -outward, // (the tip at the middle of the flower, as on Yuyuko's)
                        KeepFace = falls, // (what falls keeps its angle; what is shot faces the way it flies)
                        Until = release + f.stageLife(v),
                        Untraced = true,
                    });
                }
            }

            /// <summary>The clone's arrowheads that have left the screen by <paramref name="now"/> fan out bullets back in from where they left.</summary>
            private void fans(BulletField f, double now)
            {
                while (firstArrow < arrows.Length && (fanned.Contains(arrows[firstArrow]) || arrows[firstArrow].Time + arrows[firstArrow].Lifetime * 2 < now))
                    firstArrow++;

                for (int r = firstArrow; r < arrows.Length && arrows[r].Time <= now; r++)
                {
                    var ring = arrows[r];

                    if (fanned.Contains(ring) || ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & 1UL) != 0)
                        continue;

                    if (!inside(f, ring, now, out _))
                    {
                        // (when it left: between when it was fired, in the middle, and now, the moment it crossed the edge)
                        double lo = ring.Time, hi = now;

                        for (int n = 0; n < 24; n++)
                        {
                            double mid = (lo + hi) / 2;

                            if (inside(f, ring, mid, out _))
                                lo = mid;
                            else
                                hi = mid;
                        }

                        inside(f, ring, lo, out Vector2 last);
                        fanned.Add(ring);
                        fan(f, Vector2.Clamp(last, f.stageMin, f.stageMax), inwardAt(f, last), hi, r);
                    }
                }
            }

            /// <summary>Is the arrowhead inside the screen at <paramref name="t"/> (and where)? Gone counts as outside.</summary>
            private static bool inside(BulletField f, BulletEmitter ring, double t, out Vector2 at)
            {
                double own = ring.WithoutFrozenTime(f.effects.Effective(ring, t));
                ring.Evaluate(0, own, out at, out _, out bool alive);
                return alive && at.X >= f.stageMin.X && at.X <= f.stageMax.X && at.Y >= f.stageMin.Y && at.Y <= f.stageMax.Y;
            }

            /// <summary>The way back in, square to the edge nearest <paramref name="p"/>.</summary>
            private static Vector2 inwardAt(BulletField f, Vector2 p)
            {
                float left = p.X - f.stageMin.X, right = f.stageMax.X - p.X, top = p.Y - f.stageMin.Y, bottom = f.stageMax.Y - p.Y;
                float least = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

                return least == left ? Vector2.UnitX : least == right ? -Vector2.UnitX : least == top ? Vector2.UnitY : -Vector2.UnitY;
            }

            private void fan(BulletField f, Vector2 at, Vector2 inward, double t, int number)
            {
                int count = DanmakusuBalance.PARSEE_FAN_BULLETS;
                float spread = DanmakusuBalance.PARSEE_FAN_SPREAD_DEGREES * MathF.PI / 180f;
                float middle = MathF.Atan2(inward.Y, inward.X);
                float speed = stageBulletSpeed(Period);
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.PARSEE_FAN_SIZE;

                for (int k = 0; k < count; k++)
                {
                    float angle = middle + (count > 1 ? -spread / 2f + k * spread / (count - 1) : 0f);
                    Vector2 v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(number + k, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Until = t + f.stageLife(v),
                    });
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                // (the dark middle of every flower that stands, and its bloom over it, as Yuyuko's)
                var bloomColour = new Color4(1f, 0.62f, 0.78f, 1f);
                var dark = new Color4(0.85f, 0.2f, 0.4f, 1f);

                foreach (var flower in flowers)
                {
                    if (t < flower.T || t >= flower.T + flowerMs)
                        continue;

                    float shown = smoothStep((t - flower.T) / 80.0);
                    f.stageTree.Add(StageTexture.Orb, flower.At, new Vector2(flower.Length * 0.5f * shown), 0f, 0.95f, dark);
                    f.stageTree.Add(StageTexture.Glow, flower.At, new Vector2(flower.Length * DanmakusuBalance.YUYUKO_BLOOM_SIZE * shown), 0f, DanmakusuBalance.YUYUKO_BLOOM_ALPHA * shown, bloomColour, true);
                }

                // (her balls: green, as her eyes)
                var green = new Color4(0.3f, 0.82f, 0.4f, 1f);

                foreach (var volley in volleys)
                {
                    if (!volley.Made || t < volley.T)
                        continue;

                    foreach (int index in volley.Index)
                    {
                        if (index >= Bullets.Count || t > Bullets[index].Until || consumed(f, index))
                            continue;

                        if (f.stageBulletAt(Bullets[index], t, out Vector2 at))
                            mokouOrb(f, at, ballRadius(f), green, false, green);
                    }
                }
            }
        }
    }
}
