using System;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The body of a slider as a polyline with a radius. While the slider is active (StartTime..EndTime)
    /// the player takes damage periodically for touching it.
    /// </summary>
    public sealed class SliderZone
    {
        /// <summary>The slider itself.</summary>
        public OsuHitObject? Source { get; init; }

        public double StartTime { get; init; }
        public double EndTime { get; init; }

        /// <summary>Slider body centre line in playfield coordinates (stack offset applied).</summary>
        public Vector2[] Points { get; init; } = [];

        /// <summary>Half width of the slider body (the circle radius).</summary>
        public float Radius { get; init; }

        /// <summary>
        /// (osu!catch) the body falls straight down at this speed (px per ms), its points being where they are at <see cref="LandsAt"/> (its head on the bottom), from when it comes in at the top
        /// (<see cref="StartTime"/>) to <see cref="EndTime"/>; a falling circle is a zone of one point. 0: it stays where it is.
        /// </summary>
        public float FallSpeed { get; init; }

        public double LandsAt { get; init; }

        /// <summary>
        /// (Kogasa's, 2026-11-23) the body turns round the middle of the playfield as its object does (<see cref="SurpriseOrbit"/>; its head at <see cref="Head"/>), and it stays to the end of the object's
        /// stay (this zone's <see cref="EndTime"/>): a circle of hers has a zone of one point then.
        /// </summary>
        public SurpriseOrbit.Turn? Orbit { get; init; }

        public Vector2 Head { get; init; }

        /// <summary>How far down from its points the body is at <paramref name="time"/> (above them before it lands).</summary>
        public Vector2 FallOffset(double time)
            => FallSpeed > 0f ? new Vector2(0f, (float)(time - LandsAt) * FallSpeed) : Vector2.Zero;

        /// <summary>
        /// Where <paramref name="position"/> is among the zone's own points at <paramref name="time"/> (for <see cref="Touches"/>): above them as far as it has fallen, and - a body of Kogasa's, which turns
        /// round the middle as one piece, itself turning (2026-11-25) - turned back as far as it has turned.
        /// </summary>
        public Vector2 Local(Vector2 position, double time)
            => (Orbit is SurpriseOrbit.Turn turn ? turn.Unplace(position, time) : position) - FallOffset(time);

        public bool Touches(Vector2 position, float extraRadius)
        {
            float reach = Radius + extraRadius;
            float reachSquared = reach * reach;

            if (Points.Length == 1)
                return (Points[0] - position).LengthSquared <= reachSquared;

            for (int i = 1; i < Points.Length; i++)
            {
                if (distanceSquaredToSegment(position, Points[i - 1], Points[i]) <= reachSquared)
                    return true;
            }

            return false;
        }

        private static float distanceSquaredToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 1e-6f)
                return (point - a).LengthSquared;

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return (point - (a + ab * t)).LengthSquared;
        }
    }
}
