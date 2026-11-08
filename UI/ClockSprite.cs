using osu.Framework.Allocation;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>The old clock (Resources/Textures/clock.png) that shows up, very faint and growing, behind Sakuya's time stop. Not drawn if the picture is missing.</summary>
    public partial class ClockSprite : Sprite
    {
        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            Texture = BossCharacter.LoadResource(renderer, "Textures.clock.png");
        }
    }
}
