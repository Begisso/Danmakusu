using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
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

        // (how much each piece and each outline turns what is under it, 0..1: what Strength was as it was added)
        private float[] pieceStrengths = new float[256];
        private float[] runStrengths = new float[64];

        /// <summary>
        /// How much what is added from now on turns what is under it inside out (1 wholly; a half: half way, as if half see-through). Sumireko's frame before her beam turns solid is a half (2026-11-27,
        /// "transparency doubled"); whoever sets it puts it back to 1.
        /// </summary>
        public float Strength { get; set; } = 1f;

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

        // (the outlines of the shapes, each a closed run of points, for their soft edges)
        private Vector2[] outline = new Vector2[1024];
        private int outlineCount;
        private int[] runStarts = new int[64], runCounts = new int[64];
        private float[] runFeathers = new float[64];
        private int runCount;

        public void ClearShapes()
        {
            if (cornerCount == 0 && outlineCount == 0)
                return;

            cornerCount = 0;
            outlineCount = 0;
            runCount = 0;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>
        /// The outline of a shape made of pieces (its points round it, either way round, without its first point again at the end): a soft edge is drawn just outside it, a pixel and a half wide on the
        /// screen, half turned inside out at its middle - so that the shape's edge is smooth rather than in steps (2026-11-25, Suika's impact frame: "edges are not smooth as asked several times").
        /// <paramref name="featherPx"/> is how wide (screen px): the same day, wider - "fix until impact frame white area edges are gradually smooth".
        /// </summary>
        public void AddOutline(System.Collections.Generic.List<Vector2> points, float featherPx = 1.5f)
        {
            if (points.Count < 3)
                return;

            if (outlineCount + points.Count > outline.Length)
                Array.Resize(ref outline, Math.Max(outline.Length * 2, outlineCount + points.Count));

            if (runCount == runStarts.Length)
            {
                Array.Resize(ref runStarts, runStarts.Length * 2);
                Array.Resize(ref runCounts, runCounts.Length * 2);
                Array.Resize(ref runFeathers, runFeathers.Length * 2);
                Array.Resize(ref runStrengths, runStrengths.Length * 2);
            }

            runStrengths[runCount] = Strength;
            runFeathers[runCount] = featherPx;
            runStarts[runCount] = outlineCount;
            runCounts[runCount++] = points.Count;

            foreach (var p in points)
                outline[outlineCount++] = p;
        }

        /// <summary>One piece: <paramref name="a"/> to <paramref name="b"/> along one side and <paramref name="c"/> to <paramref name="d"/> along the other.</summary>
        public void AddPiece(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (cornerCount + 4 > corners.Length)
                Array.Resize(ref corners, corners.Length * 2);

            if (cornerCount / 4 >= pieceStrengths.Length)
                Array.Resize(ref pieceStrengths, pieceStrengths.Length * 2);

            pieceStrengths[cornerCount / 4] = Strength;
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
            private float[] pieceStrengths = new float[256];
            private float[] runStrengths = new float[64];

            protected new ImpactStarLayer Source => (ImpactStarLayer)base.Source;

            public ImpactDrawNode(ImpactStarLayer source)
                : base(source)
            {
            }

            private Vector2[] outline = new Vector2[1024];
            private int outlineCount;
            private int[] runStarts = new int[64], runCounts = new int[64];
            private float[] runFeathers = new float[64];
            private int runCount;
            private Vector2[] screen = new Vector2[256], normals = new Vector2[256];

            public override void ApplyState()
            {
                base.ApplyState();

                count = Source.cornerCount;

                if (corners.Length < count)
                    corners = new Vector2[Source.corners.Length];

                Array.Copy(Source.corners, corners, count);

                if (pieceStrengths.Length < Source.pieceStrengths.Length)
                    pieceStrengths = new float[Source.pieceStrengths.Length];

                Array.Copy(Source.pieceStrengths, pieceStrengths, Math.Min(count / 4, Source.pieceStrengths.Length));

                outlineCount = Source.outlineCount;
                runCount = Source.runCount;

                if (outline.Length < outlineCount)
                    outline = new Vector2[Source.outline.Length];

                if (runStarts.Length < runCount)
                {
                    runStarts = new int[Source.runStarts.Length];
                    runCounts = new int[Source.runCounts.Length];
                    runFeathers = new float[Source.runFeathers.Length];
                }

                if (runStrengths.Length < Source.runStrengths.Length)
                    runStrengths = new float[Source.runStrengths.Length];

                Array.Copy(Source.runStrengths, runStrengths, Math.Min(runCount, Source.runStrengths.Length));

                Array.Copy(Source.outline, outline, outlineCount);
                Array.Copy(Source.runStarts, runStarts, runCount);
                Array.Copy(Source.runCounts, runCounts, runCount);
                Array.Copy(Source.runFeathers, runFeathers, runCount);
            }

            protected override void Blit(IRenderer renderer)
            {
                var pixel = renderer.WhitePixel;
                var pixelCoords = new RectangleF(0, 0, pixel.DisplayWidth, pixel.DisplayHeight);

                for (int i = 0; i + 3 < count; i += 4)
                {
                    var piece = new Quad(Vector2Extensions.Transform(corners[i], DrawInfo.Matrix), Vector2Extensions.Transform(corners[i + 1], DrawInfo.Matrix), Vector2Extensions.Transform(corners[i + 2], DrawInfo.Matrix),
                        Vector2Extensions.Transform(corners[i + 3], DrawInfo.Matrix));

                    float strength = pieceStrengths[i / 4];
                    renderer.DrawQuad(pixel, piece, new Color4(strength, strength, strength, strength), textureCoords: pixelCoords);
                }

                // (the soft edges: a strip outside every outline, fully turned at the shape's edge and not at all at its outer edge. What is drawn here is not premultiplied by the renderer, and the
                // blending turns by the colour and keeps by the alpha, so both go down together: half way out, half of what is under it shows turned)
                for (int r = 0; r < runCount; r++)
                {
                    float strength = runStrengths[r];
                    var edge = ColourInfo.GradientVertical(new Color4(strength, strength, strength, strength), new Color4(0f, 0f, 0f, 0f));
                    int start = runStarts[r], n = runCounts[r];

                    if (screen.Length < n)
                    {
                        screen = new Vector2[n * 2];
                        normals = new Vector2[n * 2];
                    }

                    float area = 0f;

                    for (int i = 0; i < n; i++)
                        screen[i] = Vector2Extensions.Transform(outline[start + i], DrawInfo.Matrix);

                    for (int i = 0; i < n; i++)
                    {
                        Vector2 p = screen[i], q = screen[(i + 1) % n];
                        area += p.X * q.Y - q.X * p.Y;
                    }

                    float outward = area > 0f ? 1f : -1f;

                    // (at every point, out between its two edges, longer at a corner so that the strip keeps its width - at most twice)
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 before = screen[i] - screen[(i + n - 1) % n], after = screen[(i + 1) % n] - screen[i];
                        Vector2 nb = before.LengthSquared > 1e-6f ? Vector2.Normalize(new Vector2(before.Y, -before.X)) * outward : Vector2.Zero;
                        Vector2 na = after.LengthSquared > 1e-6f ? Vector2.Normalize(new Vector2(after.Y, -after.X)) * outward : nb;

                        if (nb == Vector2.Zero)
                            nb = na;

                        Vector2 normal = nb + na;
                        normal = normal.LengthSquared > 1e-6f ? Vector2.Normalize(normal) : na;
                        float stretch = Math.Min(1f / Math.Max(Vector2.Dot(normal, na), 0.5f), 2f);
                        normals[i] = normal * (runFeathers[r] * stretch);
                    }

                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;

                        if ((screen[j] - screen[i]).LengthSquared < 1e-6f)
                            continue;

                        renderer.DrawQuad(pixel, new Quad(screen[i], screen[j], screen[i] + normals[i], screen[j] + normals[j]), edge, textureCoords: pixelCoords);
                    }
                }
            }

            // (it is never opaque, whatever the white pixel says of itself: what is under it must be there to be turned inside out)
            protected override void BlitOpaqueInterior(IRenderer renderer)
            {
            }
        }
    }
}
