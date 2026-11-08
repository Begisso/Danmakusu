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
        // Mononobe no Futo: everything fired in her section loses speed the nearer it gets to the wall ahead of it, down to 0.3 of its speed at the wall (BulletEmitter.SlowsToWall). When a pause starts she throws a boat at the
        // player: the user's picture of a wooden ship seen from above, twenty circle radii long and as wide as the picture is for that, flying straight at where they are, bow first. It hurts like a slider body where the picture
        // is. Nothing stays in it: every bullet whose body is in the hull (its outline, read from the picture) slides out to the nearest edge, fast (twice the boat's and its own speed together) but smoothly, a little every
        // step, and flies on from there the way it was flying, as fast (so the boat ploughs through them and they slide off its sides).
        // ======================================================================================================================================================

        private sealed class BoatAct : StageAct
        {
            private sealed class Boat
            {
                public double Time, Until;
                public Vector2 From;
                public Vector2 Velocity;
            }

            private readonly List<double> throws;
            private readonly List<Boat> boats = new List<Boat>();

            /// <summary>The hull in the boat's own frame, in pixels (x along it to the bow, y across it), made for the circle size it was made for.</summary>
            private Vector2[] hull = Array.Empty<Vector2>();

            private float hullFor = -1f;

            public BoatAct(BossPeriod period, List<double> throws)
            {
                Period = period;
                this.throws = throws;
                Start = throws[0];
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + 60000;
                StageLayer.PrepareBoatShape();
            }

            public override void Reset(BulletField f, double time) => boats.Clear();

            /// <summary>The bullets are only looked at while a boat flies.</summary>
            public override bool Absorbs(double t) => AbsorbsBetween(t, t);

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var boat in boats)
                {
                    if (boat.Time <= to && boat.Until >= from)
                        return true;
                }

                return false;
            }

            private static float halfLength(BulletField f) => f.circleRadius * DanmakusuBalance.FUTO_BOAT_CIRCLES;

            private static float halfWidth(BulletField f) => halfLength(f) / StageLayer.BoatAspect;

            private Vector2[] hullOf(BulletField f)
            {
                if (hullFor != f.circleRadius)
                {
                    var outline = StageLayer.BoatOutline;
                    float along = halfLength(f), across = halfWidth(f);

                    hull = new Vector2[outline.Length];

                    for (int i = 0; i < outline.Length; i++)
                        hull[i] = new Vector2(outline[i].X * along, outline[i].Y * across);

                    hullFor = f.circleRadius;
                }

                return hull;
            }

            private static Vector2 at(Boat boat, double t) => boat.From + boat.Velocity * (float)(t - boat.Time);

            /// <summary>A place in the boat's own frame: along it to the bow, and across it the way the picture's bottom lies.</summary>
            private static Vector2 local(Boat boat, double t, Vector2 place, out Vector2 forward, out Vector2 side)
            {
                forward = boat.Velocity.LengthSquared > 1e-12f ? Vector2.Normalize(boat.Velocity) : Vector2.UnitX;
                side = new Vector2(-forward.Y, forward.X);
                Vector2 offset = place - at(boat, t);
                return new Vector2(Vector2.Dot(offset, forward), Vector2.Dot(offset, side));
            }

            /// <summary>The nearest point of the hull's outline to <paramref name="p"/> (the boat's own frame), how far it is, and the outline's outward way there.</summary>
            private static void nearestEdge(Vector2[] hull, Vector2 p, out Vector2 nearest, out float distance, out Vector2 outward)
            {
                nearest = p;
                outward = Vector2.UnitY;
                float best = float.MaxValue;

                for (int i = 0; i < hull.Length; i++)
                {
                    Vector2 a = hull[i], b = hull[(i + 1) % hull.Length];
                    Vector2 ab = b - a;
                    float lengthSquared = ab.LengthSquared;
                    float u = lengthSquared > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
                    Vector2 q = a + ab * u;
                    float d = (p - q).LengthSquared;

                    if (d >= best)
                        continue;

                    best = d;
                    nearest = q;

                    // (the loop goes along the top from the stern to the bow and back along the bottom: clockwise on the screen, so the outside is on the left of the way it goes)
                    outward = lengthSquared > 1e-9f ? Vector2.Normalize(new Vector2(ab.Y, -ab.X)) : Vector2.UnitY;
                }

                distance = MathF.Sqrt(best);
            }

            private bool gone(BulletField f, Boat boat, double t)
            {
                Vector2 place = at(boat, t);
                float r = halfLength(f);

                return place.X < f.stageMin.X - r || place.X > f.stageMax.X + r || place.Y < f.stageMin.Y - r || place.Y > f.stageMax.Y + r;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                while (boats.Count < throws.Count && throws[boats.Count] <= now)
                {
                    Vector2 from = f.bossVisualOf(Period);
                    Vector2 aim = f.characterPosition - from;
                    Vector2 way = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;
                    float speed = stageBulletSpeed(Period) * DanmakusuBalance.FUTO_BOAT_SPEED;

                    // (it is looked at until all of it has left the screen, however long it is)
                    double until = throws[boats.Count] + (Vector2.Distance(f.stageMin, f.stageMax) + 2f * halfLength(f)) / Math.Max(speed, 1e-4f);

                    boats.Add(new Boat { Time = throws[boats.Count], Until = until, From = from, Velocity = way * speed });
                    f.playStageSound("mokou_wall_hit");
                }
            }

            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitY;
                speed = 0f;

                if (f.failed || !emitter.CanBeDeflected)
                    return false;

                // (nothing of the bullet's body may be in the boat's)
                float body = emitter.VisualRadius * emitter.BigScale(bullet);
                var shape = hullOf(f);
                float along = halfLength(f), across = halfWidth(f);

                foreach (var boat in boats)
                {
                    if (t < boat.Time || t > boat.Until || gone(f, boat, t))
                        continue;

                    Vector2 p = local(boat, t, to, out Vector2 forward, out Vector2 side);

                    if (MathF.Abs(p.X) > along + body || MathF.Abs(p.Y) > across + body)
                        continue;

                    nearestEdge(shape, p, out Vector2 nearest, out float distance, out Vector2 edgeOut);
                    bool inside = PolygonShape.Contains(p, shape);
                    Vector2 outward;
                    float depth;

                    if (inside)
                    {
                        outward = distance > 1e-3f ? (nearest - p) / distance : edgeOut;
                        depth = distance + body;
                    }
                    else
                    {
                        if (distance >= body)
                            continue;

                        outward = distance > 1e-3f ? (p - nearest) / distance : edgeOut;
                        depth = body - distance;
                    }

                    // (close enough to the edge: no end of tiny pushes)
                    if (depth < 0.25f)
                        continue;

                    // (out to the edge the shortest way, but not at once: a little every step, fast enough that the boat never catches up with it; it flies on the way it was flying, as fast)
                    float own = emitter.SpeedOf(bullet);
                    float pushSpeed = DanmakusuBalance.FUTO_PUSH_SPEED * (boat.Velocity.Length + own);
                    float move = Math.Min(depth, pushSpeed * (float)Math.Max(t - ScanPrevious, 1.0));
                    Vector2 step = to - from;

                    position = to + (forward * outward.X + side * outward.Y) * move;
                    direction = step.LengthSquared > 1e-8f ? Vector2.Normalize(step) : forward * outward.X + side * outward.Y;
                    speed = own;
                    return true;
                }

                return false;
            }

            /// <summary>(being pushed out of the boat is no bounce: a ricochet bullet keeps the ricochets it had)</summary>
            public override bool PushIsNoBounce => true;

            /// <summary>It hurts where the picture is (its outline), as Murasa's anchor does.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                var shape = hullOf(f);
                float along = halfLength(f), across = halfWidth(f);

                foreach (var boat in boats)
                {
                    if (now < boat.Time || gone(f, boat, now))
                        continue;

                    Vector2 p = local(boat, now, f.characterPosition, out _, out _);
                    float reach = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    if (MathF.Abs(p.X) > along + reach || MathF.Abs(p.Y) > across + reach)
                        continue;

                    if (PolygonShape.Contains(p, shape) || PolygonShape.DistanceToOutline(p, shape) <= reach)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                Vector2 size = new Vector2(2f * halfLength(f), 2f * halfWidth(f));

                foreach (var boat in boats)
                {
                    if (t < boat.Time || gone(f, boat, t))
                        continue;

                    f.stageBack.Add(StageTexture.Boat, at(boat, t), size, MathF.Atan2(boat.Velocity.Y, boat.Velocity.X) * 180f / MathF.PI, 1f, Color4.White);
                }
            }
        }
    }
}
