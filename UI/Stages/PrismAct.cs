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
        // Sunny Milk: during her attacks a big prism (an equilateral triangle) with a rainbow halo turns in the middle of the playfield, at half the speed of a spinner's beams, the other way every sixteen beats. A bullet that
        // crosses it turns parallel to the side that is nearest its way (it flies on at the same speed, along that side); when it leaves the prism its way is mirrored across the line that stands on that side, and it splits into seven
        // bullets, spread over 120 degrees round that way, each a third of the size and in another colour of the rainbow (the colours start from the colour of the bullet that went in); every second bullet of a ring
        // is not split: it comes out the mirrored way as it went in. The prism hurts like a slider body.
        // Where a bullet goes in or comes out the halo glows a little brighter for a moment. Like Nazrin's arrowheads the bullets in the prism are simulated step by step (they only fly straight on, but the prism turns under them).
        // ======================================================================================================================================================

        private sealed class PrismAct : StageAct
        {
            private const double trail_step_ms = DanmakusuBalance.HOMING_STEP_MS;
            private const int max_flashes = 240;

            private sealed class Crossing
            {
                public int Index; // (in Bullets)
                public Vector2 Position;
                public Vector2 Velocity;
                public Vector2 Along; // (the way of the side it flies along)
                public Vector2 Across; // (the line that stands on that side)
                public Vector2 Entered; // (the unit way it came in)
                public double Time;
                public float Speed;
                public float Radius;
                public float Hit;
                public int Style;
                public bool Whole; // (every second bullet of a ring comes out as it went in, one bullet, not split)
                public bool Gone;
                public List<Vector2> Trail = null!;
            }

            private struct Flash
            {
                public double Time;
                public Vector2 Position;
                public Color4 Colour;
            }

            private static readonly int[] rainbow = { BulletRenderer.TINT_RED, BulletRenderer.TINT_ORANGE, BulletRenderer.TINT_YELLOW, BulletRenderer.TINT_GREEN, BulletRenderer.TINT_BLUE, BulletRenderer.TINT_PURPLE, BulletRenderer.TINT_PINK };

            private readonly BulletField field;
            private readonly double beat;
            private readonly double segment;
            private readonly float omega;
            private readonly List<Crossing> crossings = new List<Crossing>();
            private readonly List<Flash> flashes = new List<Flash>();

            public PrismAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                segment = beat * DanmakusuBalance.SUNNY_TURN_BEATS;
                omega = Math.Max(period.AngularSpeed, 1e-6f) * DanmakusuBalance.SUNNY_TURN_SHARE;
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            private static Vector2 centreOf(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            private static float radiusOf(BulletField f) => f.circleRadius * DanmakusuBalance.SUNNY_PRISM_RADIUS_CIRCLES;

            /// <summary>How far the prism has turned at <paramref name="t"/> (radians, clockwise): it turns one way for sixteen beats, then the other.</summary>
            private float angleAt(double t)
            {
                double age = Math.Max(t - Start, 0.0);
                long k = (long)Math.Floor(age / segment);
                double within = age - k * segment;
                double sum = k % 2 == 0 ? within : segment - within;

                // (every pair of sixteen-beat stretches comes back to where it started)
                return (float)(omega * sum);
            }

            private static float vertexAngle(float angle, int k) => angle - MathF.PI / 2f + k * MathF.Tau / 3f;

            /// <summary>Is the point <paramref name="q"/> (from the centre) inside the prism when it has turned <paramref name="angle"/>, or within <paramref name="grow"/> pixels of it?</summary>
            private static bool inside(Vector2 q, float angle, float radius, float grow)
            {
                float inner = radius * 0.5f + grow;

                for (int k = 0; k < 3; k++)
                {
                    float a = vertexAngle(angle, k) + MathF.PI / 3f; // (the middle of the side that starts at corner k)

                    if (q.X * MathF.Cos(a) + q.Y * MathF.Sin(a) > inner)
                        return false;
                }

                return true;
            }

            /// <summary>0..1: the playfield shakes (1..2 px) while the prism grows in the warning before the attack.</summary>
            public float ShakeAt(double now) => now >= Period.Start && now < Start ? DanmakusuBalance.SUNNY_WARNING_SHAKE : 0f;

            public override void Reset(BulletField f, double time)
            {
                crossings.Clear();
                flashes.Clear();
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                float radius = radiusOf(f);
                Vector2 q = to - centreOf(f);

                if (q.LengthSquared > radius * radius || !inside(q, angleAt(t), radius, 0f))
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                Vector2 entered = Vector2.Normalize(delta);
                float angle = angleAt(t);

                // the side that is nearest its way: the one whose line makes the smallest angle with it
                Vector2 along = Vector2.UnitX;
                float best = -1f;

                for (int k = 0; k < 3; k++)
                {
                    float a = vertexAngle(angle, k) + MathF.PI / 3f + MathF.PI / 2f; // (the way of the side that starts at corner k)
                    Vector2 side = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    float alignment = Vector2.Dot(entered, side);

                    if (MathF.Abs(alignment) > best)
                    {
                        best = MathF.Abs(alignment);
                        along = alignment >= 0f ? side : -side;
                    }
                }

                Vector2 across = new Vector2(-along.Y, along.X);
                var b = f.makeStageBullet(stored, emitter, bullet, t, to, along * speed);
                b.Trail = new List<Vector2>(32) { to };
                b.TrailStep = trail_step_ms;
                b.Until = t + 20000;

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                crossings.Add(new Crossing
                {
                    Index = index,
                    Position = to,
                    Velocity = along * speed,
                    Along = along,
                    Across = across,
                    Entered = entered,
                    Time = t,
                    Speed = speed,
                    Radius = b.Radius,
                    Hit = b.Hit,
                    Style = stored.Style,
                    Whole = (bullet & 1) == 1,
                    Trail = Bullets[index].Trail!,
                });

                flash(t, to, f.palette[((stored.Style % f.palette.Length) + f.palette.Length) % f.palette.Length]);
                return true;
            }

            private void flash(double time, Vector2 at, Color4 colour)
            {
                if (flashes.Count >= max_flashes)
                    flashes.RemoveAt(0);

                flashes.Add(new Flash { Time = time, Position = at, Colour = colour });
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || crossings.Count == 0)
                    return;

                float radius = radiusOf(f);
                Vector2 centre = centreOf(f);
                float step = (float)trail_step_ms;

                foreach (var crossing in crossings)
                {
                    int guard = 0;

                    while (!crossing.Gone && crossing.Time + trail_step_ms <= now && guard++ < 1200)
                    {
                        crossing.Position += crossing.Velocity * step;
                        crossing.Time += trail_step_ms;
                        crossing.Trail.Add(crossing.Position);

                        if (!inside(crossing.Position - centre, angleAt(crossing.Time), radius, 0f) || guard >= 1199)
                            leave(f, crossing);
                    }
                }
            }

            /// <summary>The bullet has come out of the prism: its way is mirrored across the line that stands on the side it flew along, and it splits into seven.</summary>
            private void leave(BulletField f, Crossing crossing)
            {
                crossing.Gone = true;

                var inBullet = Bullets[crossing.Index];
                inBullet.Until = crossing.Time;
                Bullets[crossing.Index] = inBullet;

                float along = Vector2.Dot(crossing.Entered, crossing.Along);
                float across = Vector2.Dot(crossing.Entered, crossing.Across);
                // (it goes on the way it went along the side, out of the prism: mirrored across the side's line; the opposite of this sent it back the way it came, into the prism)
                Vector2 mirrored = along * crossing.Along - across * crossing.Across;
                float length = mirrored.Length;
                mirrored = length > 1e-5f ? mirrored / length : crossing.Along;

                // (the rainbow goes out of the prism, never back into it: a way that points in is turned back out, as off a mirror where it leaves)
                // (the outward normal of the side it left through: the one it is furthest beyond)
                Vector2 q = crossing.Position - centreOf(f);
                float prismAngle = angleAt(crossing.Time);
                Vector2 outward = mirrored;
                float furthest = float.NegativeInfinity;

                for (int k = 0; k < 3; k++)
                {
                    float a = vertexAngle(prismAngle, k) + MathF.PI / 3f;
                    Vector2 normal = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    float beyond = Vector2.Dot(q, normal);

                    if (beyond > furthest)
                    {
                        furthest = beyond;
                        outward = normal;
                    }
                }

                static Vector2 outOf(Vector2 way, Vector2 normal) => Vector2.Dot(way, normal) < 0f ? way - 2f * Vector2.Dot(way, normal) * normal : way;

                mirrored = outOf(mirrored, outward);

                if (crossing.Whole)
                {
                    // (it comes out as it went in: one bullet, its own size, colour and look, the mirrored way)
                    Vector2 way = mirrored * crossing.Speed;

                    AddBullet(new StageBullet
                    {
                        T0 = crossing.Time,
                        P0 = crossing.Position + outward * crossing.Radius, // (just outside the glass)
                        V = way,
                        Radius = crossing.Radius,
                        Hit = crossing.Hit,
                        Style = crossing.Style,
                        Shape = inBullet.Shape,
                        Tint = inBullet.Tint,
                        TintAmount = inBullet.TintAmount,
                        Shade = inBullet.Shade,
                        Blades = inBullet.Blades,
                        Decal = inBullet.Decal,
                        Bloom = inBullet.Bloom,
                        Until = crossing.Time + f.stageLife(way),
                    });

                    flash(crossing.Time, crossing.Position, f.palette[((crossing.Style % f.palette.Length) + f.palette.Length) % f.palette.Length]);
                    return;
                }

                float baseAngle = MathF.Atan2(mirrored.Y, mirrored.X);
                float spread = DanmakusuBalance.SUNNY_SPLIT_SPREAD_DEGREES * MathF.PI / 180f;
                int count = DanmakusuBalance.SUNNY_SPLIT_BULLETS;
                int offset = ((crossing.Style % rainbow.Length) + rainbow.Length) % rainbow.Length;
                float size = DanmakusuBalance.SUNNY_SPLIT_SIZE_DIVISOR;

                for (int i = 0; i < count; i++)
                {
                    float angle = baseAngle + (count > 1 ? (i / (count - 1f) - 0.5f) * spread : 0f);
                    Vector2 velocity = outOf(new Vector2(MathF.Cos(angle), MathF.Sin(angle)), outward) * crossing.Speed;
                    int tint = rainbow[(i + offset) % rainbow.Length];

                    AddBullet(new StageBullet
                    {
                        T0 = crossing.Time,
                        P0 = crossing.Position + outward * (crossing.Radius / size), // (just outside the glass)
                        V = velocity,
                        Radius = crossing.Radius / size,
                        Hit = crossing.Hit / size,
                        Style = crossing.Style,
                        Shape = BulletShape.Circle,
                        Tint = tint,
                        TintAmount = 1f,
                        Until = crossing.Time + f.stageLife(velocity),
                    });
                }

                flash(crossing.Time, crossing.Position, BulletRenderer.SpecialBodies[rainbow[(3 + offset) % rainbow.Length] - 1]);
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (now < Start || now >= End)
                    return false;

                float radius = radiusOf(f);
                Vector2 q = f.characterPosition - centreOf(f);

                return q.LengthSquared <= (radius + 4f) * (radius + 4f) && inside(q, angleAt(now), radius, DanmakusuBalance.PLAYER_HITBOX_RADIUS);
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                // (the prism grows to its size during the warning before the attack, and is there for good when the attack starts)
                double warnStart = Math.Min(Period.Start, Start);
                float grow = t < Start ? smoothStep((t - warnStart) / Math.Max(Start - warnStart, 1.0)) : 1f;
                float presence = smoothStep((t - warnStart) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (presence <= 0.002f || grow <= 0.002f)
                    return;

                float radius = radiusOf(f) * grow;
                Vector2 centre = centreOf(f);
                float angle = angleAt(t);

                // the prism: a pane of glass in the shape of a triangle (the picture's corner points up when it has not turned)
                float side = radius * 2f / StageLayer.PrismCorner;
                f.stageBack.Add(StageTexture.Prism, centre, new Vector2(side), angle * 180f / MathF.PI, 0.95f * presence, Color4.White);

                // the rainbow halo: many thin rings that overlap, the colour running through the rainbow from the red outside to the pink inside, and the ends soft: it reads as one smooth band
                int rings = DanmakusuBalance.SUNNY_HALO_RINGS;
                float outer = radius * DanmakusuBalance.SUNNY_HALO_SCALE;
                float depth = radius * 0.05f * 1.5f * (rainbow.Length - 1) + radius * 0.05f * 2f;
                float spacing = depth / (rings - 1);
                float ringHalf = spacing * 1.1f;

                for (int i = 0; i < rings; i++)
                {
                    float u = i / (rings - 1f);
                    float position = u * (rainbow.Length - 1);
                    int low = Math.Min((int)position, rainbow.Length - 2);
                    float mix = position - low;
                    var from = BulletRenderer.SpecialBodies[rainbow[low] - 1];
                    var to = BulletRenderer.SpecialBodies[rainbow[low + 1] - 1];
                    var colour = new Color4(from.R + (to.R - from.R) * mix, from.G + (to.G - from.G) * mix, from.B + (to.B - from.B) * mix, 1f);
                    float soft = smoothStep(u / 0.12f) * smoothStep((1f - u) / 0.12f);
                    float ring = outer - i * spacing;

                    f.stageRipple.AddRing(centre, ring + ringHalf, ringHalf, DanmakusuBalance.SUNNY_HALO_ALPHA * 0.55f * presence * (0.25f + 0.75f * soft), colour);
                }

                // where a bullet has gone in or come out the halo is brighter for a moment
                foreach (var spark in flashes)
                {
                    double age = t - spark.Time;

                    if (age < 0 || age > DanmakusuBalance.SUNNY_FLASH_MS)
                        continue;

                    float u = (float)(age / DanmakusuBalance.SUNNY_FLASH_MS);
                    float level = smoothStep(age / 70.0) * (1f - smoothStep(u));

                    f.stageBack.Add(StageTexture.Glow, spark.Position, new Vector2(radius * 1.1f), 0f, 0.6f * level * presence, spark.Colour, true);
                }
            }
        }
    }
}
