using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>A hit circle at the moment it is auto-hit: the player standing inside it at that moment takes one hit.</summary>
    public sealed class CircleZone
    {
        /// <summary>The circle itself.</summary>
        public OsuHitObject? Source { get; init; }

        public double Time { get; init; }
        public Vector2 Position { get; init; }
        public float Radius { get; init; }
    }
}
