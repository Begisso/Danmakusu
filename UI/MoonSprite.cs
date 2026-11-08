using osu.Framework.Allocation;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>The moon (Resources/Textures/moon.png) as a plain sprite: tinted red and see-through it hangs, giant, in the background of Remilia's attack. Not drawn if the picture is missing.</summary>
    public partial class MoonSprite : Sprite
    {
        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            Texture = BossCharacter.LoadResource(renderer, "Textures.moon.png");
        }
    }
}
