using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The rectangle hitfinish bullets bounce around in: the whole visible screen, in playfield coordinates. The field updates it from the
    /// real window; until it knows the screen it is the 512x384 playfield.
    /// </summary>
    public sealed class RicochetArea
    {
        public Vector2 Min = Vector2.Zero;
        public Vector2 Max = new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT);
    }
}
