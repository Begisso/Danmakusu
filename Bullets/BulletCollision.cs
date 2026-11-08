using System;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Shape-accurate bullet vs. player tests. The player is a small disc moving along a straight segment; seen from the bullet
    /// (which does not rotate while it flies) that is a segment in the bullet's own frame, with axis X pointing along its direction of travel.
    /// </summary>
    public static class BulletCollision
    {
        /// <summary>The direction of blade <paramref name="index"/> of a knife with <paramref name="blades"/> blades (the first is the knife's own, the others are evenly turned round the handle).</summary>
        public static Vector2 BladeDirection(int blades, int index, Vector2 direction, float spin = 0f)
        {
            float angle = spin + MathF.Tau * index / Math.Max(blades, 1);
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

            return new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
        }

        /// <summary>Where the middle of blade <paramref name="index"/> is, relative to the middle of the knife's own blade: the blades all start at the handle end, <paramref name="half"/> being the half length of a knife.</summary>
        public static Vector2 BladeOffset(int blades, int index, Vector2 direction, float half, float spin = 0f)
            => (BladeDirection(blades, index, direction, spin) - direction) * (half * DanmakusuBalance.KNIFE_PIVOT);

        /// <summary>
        /// Does the player touch a bullet shape somewhere along the segment?
        /// </summary>
        /// <param name="shape">Ellipse, arrowhead or star (circles are tested by the caller).</param>
        /// <param name="size">Bounding radius of the hit shape (the long half-axis / the corner distance).</param>
        /// <param name="direction">Unit direction of travel of the bullet.</param>
        /// <param name="start">Start of the player's path relative to the bullet centre, in world axes.</param>
        /// <param name="end">End of the player's path relative to the bullet centre, in world axes.</param>
        /// <param name="playerRadius">Radius of the player's hitbox.</param>
        public static bool Hits(BulletShape shape, float size, Vector2 direction, Vector2 start, Vector2 end, float playerRadius)
        {
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var a = new Vector2(Vector2.Dot(start, direction), Vector2.Dot(start, perpendicular));
            var b = new Vector2(Vector2.Dot(end, direction), Vector2.Dot(end, perpendicular));

            switch (shape)
            {
                case BulletShape.Ellipse:
                {
                    // the ellipse grown by the player's radius is approximated by an ellipse with every half-axis grown by it;
                    // squash that into a unit circle and the test becomes a segment-vs-circle test.
                    float semiAlong = size + playerRadius;
                    float semiAcross = size * DanmakusuBalance.ELLIPSE_ASPECT + playerRadius;

                    return distanceToSegment(Vector2.Zero, new Vector2(a.X / semiAlong, a.Y / semiAcross), new Vector2(b.X / semiAlong, b.Y / semiAcross)) <= 1f;
                }

                case BulletShape.Triangle:
                    // the player is a disc: it touches the arrowhead when its centre path comes within its radius of the outline
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.ARROWHEAD, size) <= playerRadius;

                case BulletShape.Star:
                case BulletShape.SoftStar:
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.STAR, size) <= playerRadius;

                case BulletShape.Heart:
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.HEART, size) <= playerRadius;

                case BulletShape.Petal:
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.PETAL, size) <= playerRadius;

                case BulletShape.Maple:
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.MAPLE, size) <= playerRadius;

                case BulletShape.Slice:
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.SLICE, size) <= playerRadius;

                case BulletShape.Train:
                    // size is the half length of the carriage; its half height is a fixed part of it
                    return PolygonShape.SegmentDistance(a, b, DanmakusuBalance.TRAIN, size) <= playerRadius;

                default:
                    return distanceToSegment(Vector2.Zero, a, b) <= size + playerRadius;
            }
        }

        /// <summary>How far the body of a bullet reaches from its middle the way of <paramref name="way"/> (a unit vector). <paramref name="size"/> is its bounding radius, <paramref name="direction"/> the way it faces.</summary>
        public static float Reach(BulletShape shape, float size, Vector2 direction, Vector2 way)
        {
            var seen = new Vector2(Vector2.Dot(way, direction), way.Y * direction.X - way.X * direction.Y);

            switch (shape)
            {
                case BulletShape.Ellipse:
                {
                    float along = seen.X / size, across = seen.Y / (size * DanmakusuBalance.ELLIPSE_ASPECT);

                    return 1f / MathF.Sqrt(along * along + across * across);
                }

                case BulletShape.Triangle:
                    return polygonReach(DanmakusuBalance.ARROWHEAD, size, seen);

                case BulletShape.Star:
                case BulletShape.SoftStar:
                    return polygonReach(DanmakusuBalance.STAR, size, seen);

                case BulletShape.Heart:
                    return polygonReach(DanmakusuBalance.HEART, size, seen);

                case BulletShape.Petal:
                    return polygonReach(DanmakusuBalance.PETAL, size, seen);

                case BulletShape.Maple:
                    return polygonReach(DanmakusuBalance.MAPLE, size, seen);

                default:
                    return size;
            }
        }

        /// <summary>Where a ray from the middle of a polygon, in the polygon's own axes, last leaves it.</summary>
        private static float polygonReach(Vector2[] polygon, float size, Vector2 way)
        {
            float best = 0f;

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 from = polygon[i], edge = polygon[(i + 1) % polygon.Length] - from;
                float cross = way.X * edge.Y - way.Y * edge.X;

                if (MathF.Abs(cross) < 1e-6f)
                    continue;

                float along = (from.X * edge.Y - from.Y * edge.X) / cross, at = (from.X * way.Y - from.Y * way.X) / cross;

                if (at >= 0f && at <= 1f)
                    best = Math.Max(best, along);
            }

            return best * size;
        }

        /// <summary>
        /// A falling flower (five petals round a small middle, see the renderer): the player touches it when its path comes within reach of
        /// the middle disc or of the outline of any of the petals. <paramref name="radius"/> is the flower's drawn radius, <paramref name="direction"/> the way it is turned.
        /// </summary>
        public static bool HitsFlower(float radius, Vector2 direction, Vector2 start, Vector2 end, float playerRadius)
        {
            float petal = radius / (2f * DanmakusuBalance.YUYUKO_PETAL_REACH + 1f);

            if (distanceToSegment(Vector2.Zero, start, end) <= petal * 0.5f + playerRadius)
                return true;

            float turned = MathF.Atan2(direction.Y, direction.X);

            for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
            {
                float angle = turned + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 centre = outward * (2f * DanmakusuBalance.YUYUKO_PETAL_REACH * petal);

                if (Hits(BulletShape.Petal, petal, -outward, start - centre, end - centre, playerRadius))
                    return true;
            }

            return false;
        }

        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 1e-9f)
                return Vector2.Distance(point, a);

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
