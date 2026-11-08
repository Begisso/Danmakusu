using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// What an anime impact frame (the World's Ender's) has over everything: its stars and its sharp lines. They turn what is under them inside out, whatever it is, by how they are blended with it. They are real shapes rather than pictures, so their edges stay as sharp as the screen
    /// allows however big they are. Pieces must not overlap: a place covered twice turns back again.
    /// </summary>
    public partial class ImpactStarLayer : Sprite
    {
        // (four corners to a piece, as a Quad wants them: the first two along one side, the last two along the other)
        private Vector2[] corners = new Vector2[1024];
        private int cornerCount;

        public ImpactStarLayer()
        {
            RelativeSizeAxes = Axes.Both;

            // (what is drawn turns what is under it inside out; the alpha of what is there stays)
            Blending = new BlendingParameters
            {
                Source = BlendingType.OneMinusDstColor,
                Destination = BlendingType.OneMinusSrcAlpha,
                SourceAlpha = BlendingType.Zero,
                DestinationAlpha = BlendingType.One,
                RGBEquation = BlendingEquation.Add,
                AlphaEquation = BlendingEquation.Add,
            };
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            Texture = renderer.WhitePixel;
        }

        public void ClearShapes()
        {
            if (cornerCount == 0)
                return;

            cornerCount = 0;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>One piece: <paramref name="a"/> to <paramref name="b"/> along one side and <paramref name="c"/> to <paramref name="d"/> along the other.</summary>
        public void AddPiece(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (cornerCount + 4 > corners.Length)
                Array.Resize(ref corners, corners.Length * 2);

            corners[cornerCount++] = a;
            corners[cornerCount++] = b;
            corners[cornerCount++] = c;
            corners[cornerCount++] = d;
        }

        /// <summary>After the pieces of this frame.</summary>
        public void ShapesDone() => Invalidate(Invalidation.DrawNode);

        protected override DrawNode CreateDrawNode() => new ImpactDrawNode(this);

        private class ImpactDrawNode : SpriteDrawNode
        {
            private Vector2[] corners = new Vector2[1024];
            private int count;

            protected new ImpactStarLayer Source => (ImpactStarLayer)base.Source;

            public ImpactDrawNode(ImpactStarLayer source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                count = Source.cornerCount;

                if (corners.Length < count)
                    corners = new Vector2[Source.corners.Length];

                Array.Copy(Source.corners, corners, count);
            }

            protected override void Blit(IRenderer renderer)
            {
                var pixel = renderer.WhitePixel;
                var pixelCoords = new RectangleF(0, 0, pixel.DisplayWidth, pixel.DisplayHeight);

                for (int i = 0; i + 3 < count; i += 4)
                {
                    var piece = new Quad(Vector2Extensions.Transform(corners[i], DrawInfo.Matrix), Vector2Extensions.Transform(corners[i + 1], DrawInfo.Matrix), Vector2Extensions.Transform(corners[i + 2], DrawInfo.Matrix),
                        Vector2Extensions.Transform(corners[i + 3], DrawInfo.Matrix));

                    renderer.DrawQuad(pixel, piece, Color4.White, textureCoords: pixelCoords);
                }
            }

            // (it is never opaque, whatever the white pixel says of itself: what is under it must be there to be turned inside out)
            protected override void BlitOpaqueInterior(IRenderer renderer)
            {
            }
        }
    }
}
