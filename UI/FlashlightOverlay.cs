using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// A flashlight like the one of osu!'s Flashlight mod: everything is black except a circle round the cursor, with a soft edge.
    /// (Drawn by the ruleset itself, so it does not depend on the mod: the mod's own drawable belongs to the player's mod list.)
    /// Used by the bosses' mechanics (Mystia). It lies above everything of the playfield.
    /// </summary>
    public partial class FlashlightOverlay : Container
    {
        private const int texture_size = 256;

        /// <summary>The picture is clear up to this fraction of its radius and black from 1; the edge between them is soft.</summary>
        private const float clear_fraction = 0.8f;

        private const float outside = 4000f;

        private readonly Sprite hole;
        private readonly Box left, right, top, bottom;

        public FlashlightOverlay()
        {
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                hole = new Sprite { Origin = Anchor.Centre },
                left = new Box { Colour = Color4.Black, Origin = Anchor.CentreRight, Size = new Vector2(outside, outside * 2) },
                right = new Box { Colour = Color4.Black, Origin = Anchor.CentreLeft, Size = new Vector2(outside, outside * 2) },
                top = new Box { Colour = Color4.Black, Origin = Anchor.BottomCentre },
                bottom = new Box { Colour = Color4.Black, Origin = Anchor.TopCentre },
            };
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            var image = new Image<Rgba32>(texture_size, texture_size);
            float half = texture_size / 2f;

            for (int y = 0; y < texture_size; y++)
            {
                for (int x = 0; x < texture_size; x++)
                {
                    float r = MathF.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float t = Math.Clamp((r - clear_fraction) / (1f - clear_fraction), 0f, 1f);
                    float alpha = t * t * (3f - 2f * t);
                    image[x, y] = new Rgba32(0, 0, 0, (byte)Math.Round(alpha * 255f));
                }
            }

            var texture = renderer.CreateTexture(texture_size, texture_size);
            texture.SetData(new TextureUpload(image));
            hole.Texture = texture;
        }

        /// <summary>Shows the flashlight (<paramref name="radius"/>: how far the player can see, playfield pixels) round <paramref name="centre"/> (playfield coordinates); strength 0 hides it.</summary>
        public void SetState(float strength, float radius, Vector2 centre)
        {
            Alpha = strength;

            if (strength <= 0)
                return;

            // (the middle of the soft edge is where the radius is)
            float pictureRadius = radius / ((clear_fraction + 1f) / 2f);

            Position = centre;
            hole.Size = new Vector2(pictureRadius * 2);

            left.X = -pictureRadius + 0.5f;
            right.X = pictureRadius - 0.5f;

            top.Y = -pictureRadius + 0.5f;
            top.Size = new Vector2(pictureRadius * 2, outside);

            bottom.Y = pictureRadius - 0.5f;
            bottom.Size = new Vector2(pictureRadius * 2, outside);
        }
    }
}
