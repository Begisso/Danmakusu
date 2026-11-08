using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The dodging character: a ring with a visible hitbox core. While frozen it turns blue and a tether
    /// shows where it will teleport to when the freeze key is released.
    /// Swap the children for your own art if you want a real sprite.
    /// </summary>
    public partial class PlayerCharacter : CompositeDrawable
    {
        private readonly CircularContainer ring;
        private readonly CircularContainer core;
        private readonly Box tether;

        public PlayerCharacter()
        {
            Origin = Anchor.TopLeft; // positioned manually, children are centred on (0,0)

            InternalChildren = new Drawable[]
            {
                tether = new Box
                {
                    Origin = Anchor.CentreLeft,
                    Height = 1f,
                    Width = 0,
                    Alpha = 0,
                    Colour = new Color4(120, 190, 255, 255),
                },
                ring = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(DanmakusuBalance.CHARACTER_RING_SIZE),
                    Masking = true,
                    BorderThickness = 1,
                    BorderColour = Color4.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0.001f },
                },
                core = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(DanmakusuBalance.PLAYER_HITBOX_RADIUS * 2),
                    Masking = true,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
                },
            };
        }

        public void UpdateState(bool frozen, Vector2 cursorOffsetFromCharacter)
        {
            ring.Colour = frozen ? new Color4(120, 190, 255, 255) : Color4.White;
            ring.Alpha = 0.9f;
            core.Alpha = 1f;

            tether.Alpha = frozen ? 0.6f : 0;
            tether.Width = cursorOffsetFromCharacter.Length;
            tether.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(cursorOffsetFromCharacter.Y, cursorOffsetFromCharacter.X));
        }
    }
}
