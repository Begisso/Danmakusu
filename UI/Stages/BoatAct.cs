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
        // (2026-11-27, "flash on boats spawn position, before it": the boats are made FUTO_WARN_BEATS before they come, and a flash on the edge of the screen shows where each will come in meanwhile.)
        // Mononobe no Futo: everything fired in her section loses speed the nearer it gets to the wall ahead of it, down to 0.3 of its speed at the wall (BulletEmitter.SlowsToWall). When a pause starts she throws a boat at the
        // player: the user's picture of a wooden ship seen from above, twice FUTO_BOAT_CIRCLES circle radii long (ten since 2026-11-22; twenty before, so it is out of the wall twice as soon) and as wide
        // as the picture is for that. It comes out of the wall nearest her, from behind her (from just out of the screen straight behind that wall from where she is), and flies straight at where the player
        // is then, bow first; two more fly with it, the same way, one to each side of it (FUTO_FOLLOW_ASIDE of its width apart) and FUTO_FOLLOW_BEHIND of its length behind (2026-11-22), and since 2026-11-24 two
        // more beyond those, as far again to the side and behind (five a throw), all twice as fast (FUTO_BOAT_SPEED). The slices of beams are pushed out of the boats too (2026-11-24, IBeamPusher): a slice that
        // would be in a boat is at the nearest edge of its hull then - the beam bends round the boat as it goes by.
        // It hurts like a slider body where the picture is. Nothing stays in it: every bullet whose body is in
        // the hull (its outline, read from the picture) slides out to the nearest edge, fast (twice the boat's and its own speed together) but smoothly, a little every step; the first time the boat has it, it is turned
        // to fly straight out of the boat from there, as fast, and after that it keeps its way (once per bullet).
        // ======================================================================================================================================================

        private sealed class BoatAct : StageAct, IBeamPusher
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
                Start = throws[0] - Math.Max(period.BeatLength, 1) * DanmakusuBalance.FUTO_WARN_BEATS;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + 60000;
                StageLayer.PrepareBoatShape();
            }

            /// <summary>The bullets a boat has turned already (each is turned out of the boat once).</summary>
            private readonly HashSet<(BulletEmitter, int)> turned = new HashSet<(BulletEmitter, int)>();

            public override void Reset(BulletField f, double time)
            {
                heard = 0;
                boats.Clear();
                turned.Clear();
            }

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
                if (pushCircle != f.circleRadius)
                    Prepare(f);

                if (f.failed)
                    return;

                // (five boats a throw: boats.Count / 5 is the throw - three until 2026-11-24. Made FUTO_WARN_BEATS before they come, 2026-11-27: a flash shows where, meanwhile)
                double warn = Math.Max(Period.BeatLength, 1) * DanmakusuBalance.FUTO_WARN_BEATS;

                while (boats.Count / 5 < throws.Count && throws[boats.Count / 5] - warn <= now)
                {
                    // (it comes out of the wall nearest her, from behind her: from just out of the screen straight behind that wall from where she is)
                    Vector2 her = f.bossVisualOf(Period);
                    float toLeft = her.X - f.stageMin.X, toRight = f.stageMax.X - her.X, toTop = her.Y - f.stageMin.Y, toBottom = f.stageMax.Y - her.Y;
                    float nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));
                    float outside = halfLength(f) * 0.95f;
                    Vector2 from = nearest == toLeft ? new Vector2(f.stageMin.X - outside, her.Y)
                        : nearest == toRight ? new Vector2(f.stageMax.X + outside, her.Y)
                        : nearest == toTop ? new Vector2(her.X, f.stageMin.Y - outside)
                        : new Vector2(her.X, f.stageMax.Y + outside);
                    Vector2 aim = f.characterPosition - from;
                    Vector2 way = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;
                    float speed = stageBulletSpeed(Period) * DanmakusuBalance.FUTO_BOAT_SPEED;

                    // (it is looked at until all of it has left the screen, however long it is)
                    double time = throws[boats.Count / 5];
                    double until = time + (Vector2.Distance(f.stageMin, f.stageMax) + 3f * halfLength(f) + 2f * followBehind(f)) / Math.Max(speed, 1e-4f);
                    Vector2 side = new Vector2(-way.Y, way.X);

                    // (two more follow it, one to its left and one to its right, a little behind: 2026-11-22)
                    boats.Add(new Boat { Time = time, Until = until, From = from, Velocity = way * speed });
                    boats.Add(new Boat { Time = time, Until = until, From = from + side * followAside(f) - way * followBehind(f), Velocity = way * speed });
                    boats.Add(new Boat { Time = time, Until = until, From = from - side * followAside(f) - way * followBehind(f), Velocity = way * speed });

                    // (two more beyond those, as far again to the side and behind: 2026-11-24, "another two boats on side")
                    boats.Add(new Boat { Time = time, Until = until, From = from + side * (2f * followAside(f)) - way * (2f * followBehind(f)), Velocity = way * speed });
                    boats.Add(new Boat { Time = time, Until = until, From = from - side * (2f * followAside(f)) - way * (2f * followBehind(f)), Velocity = way * speed });
                }

                // (heard as they come in)
                for (; heard < boats.Count / 5 && throws[heard] <= now; heard++)
                {
                    if (now - throws[heard] < 250)
                        f.playStageSound("mokou_wall_hit");
                }
            }

            private int heard;

            /// <summary>How far the two that follow the first boat are to its side, and behind it.</summary>
            private static float followAside(BulletField f) => halfWidth(f) * 2f * DanmakusuBalance.FUTO_FOLLOW_ASIDE;

            private static float followBehind(BulletField f) => halfLength(f) * 2f * DanmakusuBalance.FUTO_FOLLOW_BEHIND;

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

                    // (out to the edge the shortest way, but not at once: a little every step, fast enough that the boat never catches up with it. The first time the boat has it, it is turned to fly straight out
                    // of the boat from there, as fast (2026-11-14); after that it keeps the way it flies)
                    float own = emitter.SpeedOf(bullet);
                    float pushSpeed = DanmakusuBalance.FUTO_PUSH_SPEED * (boat.Velocity.Length + own);
                    float move = Math.Min(depth, pushSpeed * (float)Math.Max(t - ScanPrevious, 1.0));
                    Vector2 step = to - from;
                    Vector2 outWorld = forward * outward.X + side * outward.Y;

                    position = to + outWorld * move;
                    direction = turned.Add((emitter, bullet)) || step.LengthSquared <= 1e-8f ? outWorld : Vector2.Normalize(step);
                    speed = own;
                    return true;
                }

                return false;
            }

            private float pushCircle = -1f; // (the circle radius the hull was made for, kept for the beams' pushes: they do not know the field)
            private Vector2[] pushHull = Array.Empty<Vector2>();
            private Vector2 pushStageMin, pushStageMax;

            /// <summary>Takes what the beams' pushes need of the field (they are asked without it).</summary>
            public void Prepare(BulletField f)
            {
                pushHull = hullOf(f);
                pushCircle = f.circleRadius;
                pushStageMin = f.stageMin;
                pushStageMax = f.stageMax;
            }

            public bool CanPush(double time)
            {
                if (pushCircle <= 0f)
                    return false;

                foreach (var boat in boats)
                {
                    if (time >= boat.Time && time <= boat.Until)
                        return true;
                }

                return false;
            }

            /// <summary>A slice of a beam that would be in a boat is at the nearest edge of its hull (and a little out).</summary>
            public bool Push(Vector2 position, double time, out Vector2 pushed)
            {
                pushed = position;

                if (pushCircle <= 0f)
                    return false;

                float along = pushCircle * DanmakusuBalance.FUTO_BOAT_CIRCLES, across = along / StageLayer.BoatAspect;

                foreach (var boat in boats)
                {
                    if (time < boat.Time || time > boat.Until)
                        continue;

                    Vector2 p = local(boat, time, position, out Vector2 forward, out Vector2 side);

                    if (MathF.Abs(p.X) > along || MathF.Abs(p.Y) > across || !PolygonShape.Contains(p, pushHull))
                        continue;

                    nearestEdge(pushHull, p, out Vector2 nearest, out float distance, out Vector2 edgeOut);
                    Vector2 outward = distance > 1e-3f ? (nearest - p) / distance : edgeOut;
                    Vector2 edge = nearest + outward * 2f;
                    pushed = at(boat, time) + forward * edge.X + side * edge.Y;
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

                double warn = Math.Max(Period.BeatLength, 1) * DanmakusuBalance.FUTO_WARN_BEATS;

                foreach (var boat in boats)
                {
                    // (before it comes: a flash where it will come in, on the edge of the screen - 2026-11-27)
                    if (t < boat.Time)
                    {
                        double u = 1 - (boat.Time - t) / Math.Max(warn, 1);

                        if (u >= 0)
                        {
                            float level = smoothStep(u / 0.25) * (0.55f + 0.45f * MathF.Cos((float)u * MathF.PI * 6f));
                            Vector2 edge = Vector2.Clamp(boat.From, f.stageMin, f.stageMax);
                            f.stageFront.Add(StageTexture.Glow, edge, new Vector2(halfLength(f) * 2.2f), 0f, 0.8f * level, new Color4(0.8f, 0.6f, 1f, 1f), true);
                        }

                        continue;
                    }

                    if (gone(f, boat, t))
                        continue;

                    f.stageBack.Add(StageTexture.Boat, at(boat, t), size, MathF.Atan2(boat.Velocity.Y, boat.Velocity.X) * 180f / MathF.PI, 1f, Color4.White);
                }
            }
        }
    }
}
