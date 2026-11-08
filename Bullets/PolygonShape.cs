using System;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Distance helpers for a closed polygon (convex or not) given as a vertex loop, uniformly scaled around the origin.
    /// Used for the arrowhead's drawn texture and its hitbox, so both always agree.
    /// </summary>
    public static class PolygonShape
    {
        /// <summary>Is the point inside the polygon (even-odd rule)?</summary>
        public static bool Contains(Vector2 point, Vector2[] polygon, float scale = 1f)
        {
            bool inside = false;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i] * scale, b = polygon[j] * scale;

                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>Distance from the point to the polygon's outline.</summary>
        public static float DistanceToOutline(Vector2 point, Vector2[] polygon, float scale = 1f)
        {
            float best = float.MaxValue;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                best = Math.Min(best, distanceToSegment(point, polygon[i] * scale, polygon[j] * scale));

            return best;
        }

        /// <summary>Negative inside the polygon, positive outside.</summary>
        public static float SignedDistance(Vector2 point, Vector2[] polygon, float scale = 1f)
        {
            float distance = DistanceToOutline(point, polygon, scale);
            return Contains(point, polygon, scale) ? -distance : distance;
        }

        /// <summary>Shortest distance between the segment a-b and the filled polygon (0 when they touch or overlap).</summary>
        public static float SegmentDistance(Vector2 a, Vector2 b, Vector2[] polygon, float scale = 1f)
        {
            // either the segment starts inside, or it crosses the outline (distance 0 below), or it stays completely outside
            if (Contains(a, polygon, scale))
                return 0;

            float best = float.MaxValue;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                best = Math.Min(best, segmentToSegmentDistance(a, b, polygon[i] * scale, polygon[j] * scale));

            return best;
        }

        private static float cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

        private static float segmentToSegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // proper crossing: each segment's end points lie on opposite sides of the other segment
            float d1 = cross(b - a, c - a), d2 = cross(b - a, d - a);
            float d3 = cross(d - c, a - c), d4 = cross(d - c, b - c);

            if (d1 * d2 < 0 && d3 * d4 < 0)
                return 0;

            return Math.Min(
                Math.Min(distanceToSegment(a, c, d), distanceToSegment(b, c, d)),
                Math.Min(distanceToSegment(c, a, b), distanceToSegment(d, a, b)));
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
