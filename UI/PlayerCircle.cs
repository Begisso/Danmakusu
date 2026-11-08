using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The player's own magic circle, besides the bosses' (theirs stay as they are; nothing to do with Toyohime's): the bosses' picture and its glow, always there where the player is (where the hitbox is, also
    /// while the cursor and the character swap places), a quarter of their size (<see cref="DanmakusuBalance.PLAYER_CIRCLE_SIZE"/>), turning the other way at their speed. Under the bullets (the field puts
    /// it there), only to look at.
    /// </summary>
    public partial class PlayerCircle : CompositeDrawable
    {
        private const float diameter = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f * DanmakusuBalance.PLAYER_CIRCLE_SIZE;

        /// <summary>(the bosses' glow picture has this much more room round the circle than the circle's own picture)</summary>
        private const float glow_scale = 640f / 512f;

        private readonly Sprite circle;
        private readonly Sprite glow;

        public PlayerCircle()
        {
            Origin = Anchor.TopLeft; // positioned manually, children are centred on (0,0)

            InternalChildren = new Drawable[]
            {
                glow = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter * glow_scale),
                    Alpha = DanmakusuBalance.PLAYER_CIRCLE_GLOW_ALPHA,
                    Blending = BlendingParameters.Additive,
                },
                circle = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                    Alpha = DanmakusuBalance.PLAYER_CIRCLE_ALPHA,
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            circle.Texture = BossCharacter.LoadResource(renderer, "Textures.magic_circle.png");
            glow.Texture = BossCharacter.LoadResource(renderer, "Textures.magic_circle_glow.png");
        }

        protected override void Update()
        {
            base.Update();

            circle.Rotation = (float)(Time.Current / 1000.0 * DanmakusuBalance.PLAYER_CIRCLE_SPIN);
            glow.Rotation = circle.Rotation;
        }
    }
}
