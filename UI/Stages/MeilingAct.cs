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
        // Hong Meiling: she never pauses. In pulls of sixteen beats she draws in every bullet within five osu! circle radii of her (the ones of other bosses too; the ring that shows how far she reaches is like Nazrin's, in her
        // colours). A pulled bullet flies in to her and is fed into one big ball that grows on her (every second bullet that comes within her reach is left alone: it flies on as it was): the ball's area is the sum of the areas of all that were fed into it, its speed twice the average of their speeds and its colour the
        // average of their colours. When the pull is over the ball is thrown at the player (it is a plain round bullet from then on: a hit uses it up) and the next pull begins at once; what it draws in is everything but the ball that
        // was thrown. A thrown ball goes on feeding: every bullet it touches on its way is swallowed, and it grows by its area (and is hit-tested at that size). The ball glows, has bloom and dims the bullets round it, like Mokou's big circle (and is as sharp: the same picture). When its middle reaches a wall it impacts as Mokou's ball does (rings, shake, a flash, her sound)
        // and ricochets; at the next wall it impacts again, stops and fades out. A ball that a hit has used up does not impact.
        // ======================================================================================================================================================

        private sealed class MeilingAct : StageAct
        {
            private struct Arrival
            {
                public double Time; // (when it has been fed into the ball)
                public float Area; // (of the bullet, radius squared: the circle's area without the common factor)
                public float Speed;
                public Color4 Colour;
            }

            private sealed class Throw
            {
                public double Time;
                public Vector2 Position;
                public Vector2 Velocity;
                public float Radius;
                public Color4 Colour;
                public int Index; // (in Bullets)
                public double Bounce, Landing; // (when its middle reaches the first wall, and the second)
                public Vector2 BounceAt, LandingAt, Away; // (where, and the way it goes off the first)
                public bool Bounced, Landed;
                public readonly List<Arrival> Fed = new List<Arrival>(); // (what it has swallowed on its way)
                public float Area; // (all it is now: its own and what it has swallowed)
            }

            private readonly BulletField field;
            private readonly double beat;
            private readonly double pull;
            private readonly Dictionary<int, List<Arrival>> arrivals = new Dictionary<int, List<Arrival>>();
            private readonly List<Throw> throws = new List<Throw>();
            private readonly List<(double Time, Vector2 At, Color4 Colour)> impacts = new List<(double, Vector2, Color4)>();
            private readonly HashSet<int> thrown = new HashSet<int>();
            private readonly HashSet<int> sounded = new HashSet<int>();
            private int seen; // (how many bullets have come within her reach: every second one is left alone)
            private readonly HashSet<(BulletEmitter, int)> spared = new HashSet<(BulletEmitter, int)>();

            public MeilingAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                pull = beat * DanmakusuBalance.MEILING_PULL_BEATS;
                AbsorbFrom = start;
                AbsorbTo = end + 60000; // (a ball that is still flying when she has gone goes on swallowing: see Absorbs)
            }

            private bool flying(double from, double to)
            {
                foreach (var ball in throws)
                {
                    if (ball.Time <= to && ball.Landing > from)
                        return true;
                }

                return false;
            }

            public override bool Absorbs(double t) => t >= Start && (t < End || flying(t, t));

            public override bool AbsorbsBetween(double from, double to) => to >= Start && (from < End || flying(from, to));

            private int cycleCount => (int)Math.Ceiling((End - Start) / pull - 1e-9);

            private double cycleStart(int k) => Start + k * pull;

            private double cycleEnd(int k) => Math.Min(Start + (k + 1) * pull, End);

            private int cycleAt(double t) => (int)Math.Floor((t - Start) / pull);

            private Vector2 centre(BulletField f) => f.bossVisualOf(Period);

            private float reach(BulletField f) => f.circleRadius * DanmakusuBalance.MEILING_REACH_CIRCLES;

            private static Color4 colourOf(BulletField f, int style) => f.palette[((style % f.palette.Length) + f.palette.Length) % f.palette.Length];

            public override void Reset(BulletField f, double time)
            {
                arrivals.Clear();
                throws.Clear();
                impacts.Clear();
                thrown.Clear();
                sounded.Clear();
                seen = 0;
                spared.Clear();
                ClearBullets();
            }

            /// <summary>
            /// A bullet that a thrown ball touches is swallowed by it, and the ball grows by its area. A bullet within reach of her is drawn in: it flies straight to her (the place she is at) at a few times its own
            /// speed, and is fed into the ball when it gets there.
            /// </summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || emitter.Shape == BulletShape.Train)
                    return false;

                if (swallows(f, t, to, emitter, bullet))
                    return true;

                if (t >= End)
                    return false;

                int k = cycleAt(t);

                if (k < 0 || k >= cycleCount)
                    return false;

                Vector2 middle = centre(f);
                Vector2 toMiddle = middle - to;
                float distance = toMiddle.Length;

                if (distance > reach(f))
                    return false;

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                float speed = Math.Max(stored.Speed * DanmakusuBalance.MEILING_PULL_SPEED_FACTOR, DanmakusuBalance.MEILING_PULL_MIN_SPEED);
                float radius = stored.Radius * big;
                double arrives = t + distance / speed;

                // (one that would not be in the ball before this pull is over is left alone: the next pull will have it)
                if (arrives + DanmakusuBalance.MEILING_RAMP_MS > cycleEnd(k))
                    return false;

                // (every second bullet that comes within her reach is left alone: it is not drawn in, and flies on as it was)
                if (spared.Contains((emitter, bullet)))
                    return false;

                if (seen++ % 2 == 1)
                {
                    spared.Add((emitter, bullet));
                    return false;
                }

                Vector2 direction = distance > 1e-3f ? toMiddle / distance : new Vector2(0f, 1f);
                int before = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = direction * speed,
                    Radius = radius,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape == BulletShape.Knife ? BulletShape.Circle : stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = arrives,
                    Harmless = true,
                });

                if (Bullets.Count == before)
                    return false; // (too many bullets: it stays as it is)

                if (!arrivals.TryGetValue(k, out var list))
                    arrivals[k] = list = new List<Arrival>();

                list.Add(new Arrival { Time = arrives, Area = radius * radius, Speed = stored.Speed, Colour = colourOf(f, stored.Style) });
                return true;
            }

            /// <summary>Does a ball that is flying touch this bullet? Then it swallows it: it grows by its area, and it is hit-tested at its new size from now on.</summary>
            private bool swallows(BulletField f, double t, Vector2 at, BulletEmitter emitter, int bullet)
            {
                foreach (var ball in throws)
                {
                    if (t < ball.Time || t >= ball.Landing || consumed(f, ball) || !f.stageBulletAt(Bullets[ball.Index], t, out Vector2 middle))
                        continue;

                    float radius = emitter.VisualRadius * emitter.BigScale(bullet);
                    float reach = Math.Min(MathF.Sqrt(ball.Area), biggest(f)) + radius;

                    if ((at - middle).LengthSquared > reach * reach)
                        continue;

                    ball.Fed.Add(new Arrival { Time = t, Area = radius * radius });
                    ball.Area += radius * radius;

                    var body = Bullets[ball.Index];
                    body.Radius = Math.Min(MathF.Sqrt(ball.Area), biggest(f));
                    body.Hit = body.Radius * DanmakusuBalance.BULLET_HITBOX_SCALE;
                    Bullets[ball.Index] = body;
                    return true;
                }

                return false;
            }

            /// <summary>No ball grows past this radius (a share of the playfield's height), however much it is fed.</summary>
            private static float biggest(BulletField f) => (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.MEILING_MAX_RADIUS;

            /// <summary>How big a thrown ball is at <paramref name="t"/>: what it was thrown with, and what it has swallowed since (each growing in over a moment).</summary>
            private static float thrownRadius(BulletField f, Throw ball, double t)
            {
                float area = ball.Radius * ball.Radius;

                foreach (var fed in ball.Fed)
                {
                    if (t > fed.Time)
                        area += fed.Area * smoothStep((t - fed.Time) / DanmakusuBalance.MEILING_RAMP_MS);
                }

                return Math.Min(MathF.Sqrt(area), biggest(f));
            }

            /// <summary>How big the ball of pull <paramref name="k"/> is at <paramref name="t"/>: its area is the sum of the areas of what has been fed into it (each growing in over a moment).</summary>
            private float ballRadius(BulletField f, int k, double t)
            {
                if (!arrivals.TryGetValue(k, out var list))
                    return 0f;

                float area = 0f;

                foreach (var arrival in list)
                {
                    if (t > arrival.Time)
                        area += arrival.Area * smoothStep((t - arrival.Time) / DanmakusuBalance.MEILING_RAMP_MS);
                }

                return Math.Min(MathF.Sqrt(area), biggest(f));
            }

            public override void Late(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // every pull that is over throws its ball at the player
                int last = Math.Min(cycleAt(now), cycleCount - 1);

                for (int k = 0; k <= last; k++)
                {
                    double over = cycleEnd(k);

                    if (over > now || thrown.Contains(k))
                        continue;

                    thrown.Add(k);

                    if (!arrivals.TryGetValue(k, out var list) || list.Count == 0)
                        continue;

                    float area = 0f, speed = 0f, r = 0f, g = 0f, b = 0f;

                    foreach (var arrival in list)
                    {
                        area += arrival.Area;
                        speed += arrival.Speed;
                        r += arrival.Colour.R;
                        g += arrival.Colour.G;
                        b += arrival.Colour.B;
                    }

                    float radius = Math.Min(MathF.Sqrt(area), biggest(f));
                    speed = Math.Max(speed / list.Count, 0.03f) * DanmakusuBalance.MEILING_BALL_SPEED;
                    var colour = new Color4(r / list.Count, g / list.Count, b / list.Count, 1f);
                    Vector2 from = centre(f);
                    Vector2 toPlayer = f.characterPosition - from;
                    Vector2 direction = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : new Vector2(0f, 1f);
                    Vector2 velocity = direction * speed;
                    int index = Bullets.Count;

                    // (the way it will go: to the wall its middle reaches first, off it, and on to the next)
                    from = Vector2.Clamp(from, f.stageMin, f.stageMax);
                    double first = f.toWall(from, velocity, out bool side);
                    Vector2 bounceAt = from + velocity * (float)first;
                    Vector2 away = side ? new Vector2(-velocity.X, velocity.Y) : new Vector2(velocity.X, -velocity.Y);
                    double second = Math.Max(f.toWall(bounceAt, away, out _), 60.0);

                    // (it is a plain round bullet: it is hit-tested like one and used up by a hit; the act draws it, as a picture as sharp as Mokou's big circle)
                    AddBullet(new StageBullet
                    {
                        T0 = over,
                        P0 = from,
                        V = velocity,
                        Switch = over + first,
                        V2 = away,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = 0,
                        Shape = BulletShape.Circle,
                        Until = over + first + second,
                        Hidden = true,
                    });

                    if (Bullets.Count > index)
                    {
                        throws.Add(new Throw
                        {
                            Time = over, Position = from, Velocity = velocity, Radius = radius, Colour = colour, Index = index,
                            Bounce = over + first, BounceAt = bounceAt, Away = away, Landing = over + first + second, LandingAt = bounceAt + away * (float)second,
                            Area = radius * radius,
                        });
                    }

                    if (now - over < 250 && sounded.Add(k))
                        f.playStageSound("mokou_wall_hit");
                }

                // every ball whose middle has reached a wall impacts
                foreach (var ball in throws)
                {
                    if (!ball.Bounced && now >= ball.Bounce)
                    {
                        ball.Bounced = true;
                        impact(f, ball, ball.Bounce, ball.BounceAt, ball.Velocity, now);
                    }

                    if (!ball.Landed && now >= ball.Landing)
                    {
                        ball.Landed = true;
                        impact(f, ball, ball.Landing, ball.LandingAt, ball.Away, now);
                    }
                }
            }

            /// <summary>The impact of a ball on a wall, as Mokou's: rings from its middle, the screen shakes, a flash and her sound. A ball that a hit has used up makes none.</summary>
            private void impact(BulletField f, Throw ball, double time, Vector2 at, Vector2 way, double now)
            {
                if (consumed(f, ball))
                    return;

                impacts.Add((time, at, ball.Colour));

                if (now - time < 250)
                    f.playStageSound("mokou_wall_hit");

                float baseRadius = f.stageBaseRadius(Period);
                float baseSpeed = Math.Max(stageBulletSpeed(Period), 0.02f);
                int colours = Math.Max(f.palette.Length, 1);
                double ringGap = beat * DanmakusuBalance.MOKOU_RING_GAP_BEATS;
                float twist = DanmakusuBalance.MOKOU_RING_TWIST_DEGREES * MathF.PI / 180f;
                float aim = MathF.Atan2(way.Y, way.X);

                for (int ring = 0; ring < DanmakusuBalance.MEILING_IMPACT_RINGS; ring++)
                {
                    double fired = time + ring * ringGap;

                    for (int piece = 0; piece < DanmakusuBalance.MEILING_IMPACT_BULLETS; piece++)
                    {
                        float angle = aim + ring * twist + MathF.Tau * piece / DanmakusuBalance.MEILING_IMPACT_BULLETS;
                        Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        Vector2 velocity = outward * (baseSpeed * DanmakusuBalance.MOKOU_RING_SPEED_FACTOR);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = at,
                            V = velocity,
                            Radius = baseRadius,
                            Hit = baseRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = ring % colours,
                            Shape = BulletShape.Circle,
                            Until = fired + f.stageLife(velocity),
                        });
                    }
                }
            }

            /// <summary>0..1: the screen shakes for a moment at every impact, as at Mokou's.</summary>
            public float ShakeAt(double now)
            {
                float shake = 0f;

                foreach (var (time, _, _) in impacts)
                {
                    if (now >= time && now - time <= DanmakusuBalance.MOKOU_SHAKE_MS)
                        shake = Math.Max(shake, DanmakusuBalance.MOKOU_SHAKE * (1f - (float)((now - time) / DanmakusuBalance.MOKOU_SHAKE_MS)));
                }

                return shake;
            }

            private bool consumed(BulletField f, Throw t) => DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + t.Index);

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                if (f.failed || t < Start)
                    return;

                int k = cycleAt(t);

                if (k >= 0 && k < cycleCount && t < End)
                {
                    float r = ballRadius(f, k, t);

                    if (r >= 1f)
                        into.Add(new StageDim { Centre = centre(f), Radius = r, Strength = DanmakusuBalance.MOKOU_DIM * Math.Min(r / f.circleRadius, 1f) });
                }

                foreach (var ball in throws)
                {
                    if (t < ball.Time || t > Bullets[ball.Index].Until || consumed(f, ball))
                        continue;

                    if (f.stageBulletAt(Bullets[ball.Index], t, out Vector2 dimAt))
                        into.Add(new StageDim { Centre = dimAt, Radius = thrownRadius(f, ball, t), Strength = DanmakusuBalance.MOKOU_DIM });
                }
            }

            /// <summary>The ball that is growing on her hurts like a slider body for as long as the player is in it (like Mokou's big circle); the one that has been thrown is a bullet (see <see cref="Late"/>).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (now < Start || now >= End)
                    return false;

                int k = cycleAt(now);

                if (k < 0 || k >= cycleCount)
                    return false;

                float r = ballRadius(f, k, now);

                return r >= 1f && Vector2.Distance(f.characterPosition, centre(f)) <= r * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Period.Start || f.palette.Length == 0)
                    return;

                // the area she draws in: a smooth band for each of the combo colours (her colours), one inside the other, as Nazrin's
                float range = reach(f);
                float halfWidth = Math.Max(f.circleRadius * 0.15f, 2f);
                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);
                Vector2 middle = centre(f);

                // (a ball that is still flying when she has gone goes on: only her ring goes with her)
                for (int i = 0; presence > 0f && i < f.palette.Length && i < 5; i++)
                    f.stageRipple.AddRing(middle, range - i * halfWidth * 1.4f, halfWidth, DanmakusuBalance.NAZRIN_RIPPLE_ALPHA * presence * (1f - 0.12f * i), f.palette[i]);

                foreach (var (time, at, colour) in impacts)
                    mokouFlash(f, at, t - time, colour);

                // the ball that is growing on her (the colour is the average of what has been fed into it so far)
                int k = cycleAt(t);

                if (k >= 0 && k < cycleCount && t >= Start && t < End && arrivals.TryGetValue(k, out var list))
                {
                    float radius = ballRadius(f, k, t);

                    if (radius >= 1f)
                    {
                        float r = 0f, g = 0f, b = 0f;
                        int fed = 0;

                        foreach (var arrival in list)
                        {
                            if (t < arrival.Time)
                                continue;

                            r += arrival.Colour.R;
                            g += arrival.Colour.G;
                            b += arrival.Colour.B;
                            fed++;
                        }

                        if (fed > 0)
                            mokouOrb(f, middle, radius, f.palette[0], false, new Color4(r / fed, g / fed, b / fed, 1f));
                    }
                }

                // the balls that have been thrown
                foreach (var ball in throws)
                {
                    if (t < ball.Time || t > ball.Landing + DanmakusuBalance.MEILING_FADE_MS || consumed(f, ball))
                        continue;

                    // (it stops at the second wall and fades out there)
                    if (t >= ball.Landing)
                        mokouOrb(f, ball.LandingAt, thrownRadius(f, ball, t) * (1f - smoothStep((t - ball.Landing) / DanmakusuBalance.MEILING_FADE_MS)), f.palette[0], false, ball.Colour);
                    else if (f.stageBulletAt(Bullets[ball.Index], t, out Vector2 shownAt))
                        mokouOrb(f, shownAt, thrownRadius(f, ball, t), f.palette[0], false, ball.Colour);
                }
            }
        }
    }
}
