using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>Kyouko's glass walls, as the bullets that are simulated step by step (arrowheads) see them.</summary>
    public interface IGlassWalls
    {
        /// <summary>
        /// Does a bullet whose middle flew from <paramref name="start"/> (at game time <paramref name="from"/>) to <paramref name="end"/> (at <paramref name="to"/>) touch a wall (the bullet's middle gets
        /// <paramref name="reach"/> close to it)? Then <paramref name="fraction"/> is how far along the way (0..1) it touches, <paramref name="acrossX"/> is whether the wall is crossed along x (otherwise along y)
        /// and <paramref name="set"/> says which set of walls it is (a bullet bounces off every set once).
        /// </summary>
        bool Hit(double from, double to, Vector2 start, Vector2 end, float reach, out float fraction, out bool acrossX, out int set);
    }
}
