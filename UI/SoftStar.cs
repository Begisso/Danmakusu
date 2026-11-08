using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The star of spinners and bosses: a fat five-pointed star (slightly rounded tips): the bullets are white and only the gradient at their tips has the colour
    /// and a slight glow. It is baked into a square cell of a texture; the star itself fills <see cref="StarFraction"/> of the
    /// cell, so draw the cell 1 / <see cref="StarFraction"/> times as big as the star. The big star under the boss is baked with a glow round it.
    /// </summary>
    public static class SoftStar
    {
        /// <summary>Size of a baked cell in pixels.</summary>
        public const int CELL = 128;

        /// <summary>The tips of the star reach this fraction of the cell's half size.</summary>
        public const float StarFraction = 0.8f;

        /// <summary>Radius of the inner corners of the star relative to its tips (a regular pentagram has 0.38; this star is quite fat).</summary>
        public const float InnerRatio = 0.62f;

        /// <summary>The outline is shrunk to this and then grown back by the same amount, so the tips and corners are slightly round.</summary>
        private const float core = 0.97f;

        private static float[]? field;
        private static float depth = 1f;

        /// <summary>The corners of the star: tips at radius <paramref name="scale"/>, the first one on the +x axis.</summary>
        private static Vector2[] outline(float scale)
        {
            var corners = new Vector2[10];

            for (int i = 0; i < corners.Length; i++)
            {
                float angle = i * MathF.Tau / corners.Length;
                float length = (i % 2 == 0 ? 1f : InnerRatio) * scale;
                corners[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
            }

            return corners;
        }

        /// <summary>Signed distance to the star (negative inside) for every pixel of a cell; the star's tips are at 1 in these units.</summary>
        private static float[] computeField(int cell, float starFraction, out float deepestInside)
        {
            var corners = outline(core);
            var values = new float[cell * cell];
            float deepest = 0;

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    var p = new Vector2((x + 0.5f - cell / 2f) / (cell / 2f), (y + 0.5f - cell / 2f) / (cell / 2f)) / starFraction;
                    float d = PolygonShape.SignedDistance(p, corners) - (1f - core);

                    values[y * cell + x] = d;
                    deepest = Math.Max(deepest, -d);
                }
            }

            deepestInside = Math.Max(deepest, 1e-4f);
            return values;
        }

        /// <summary>Paints the star of the given body colour into the cell whose top left corner is (<paramref name="left"/>, <paramref name="top"/>).</summary>
        public static void Bake(Image<Rgba32> image, int left, int top, Color4 body, float edgeSoftness = 0.035f)
        {
            if (field == null)
                field = computeField(CELL, StarFraction, out depth);

            paint(image, left, top, CELL, StarFraction, field, body, true, 0.75f, 0.14f, edgeSoftness, rim_width);
        }

        /// <summary>Width (star units) of the thin white outline of the bullet stars.</summary>
        private const float rim_width = 0.055f;

        /// <summary>A square picture (<paramref name="size"/> pixels) of the big star in its body colour, with a very soft edge and no outline; the tips reach <paramref name="starFraction"/> of its half size.</summary>
        public static Image<Rgba32> CreateBig(int size, float starFraction, Color4 body)
        {
            var image = new Image<Rgba32>(size, size);
            var values = computeField(size, starFraction, out _);

            paint(image, 0, 0, size, starFraction, values, body, false, 0f, 0.1f, 0.07f, 0f);
            return image;
        }

        /// <summary>
        /// The glow of the big star on its own: the star's shape, blurred out, for drawing with additive blending behind it (so the glow only ever
        /// brightens what is behind it - it never makes a darker rim round the star).
        /// </summary>
        public static Image<Rgba32> CreateGlow(int size, float starFraction, Color4 body)
        {
            var image = new Image<Rgba32>(size, size);
            var values = computeField(size, starFraction, out _);
            Color4 colour = mix(body, Color4.White, 0.2f);
            float reach = 1f / starFraction - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outside = Math.Max(values[y * size + x], 0f);
                    float alpha = 0.55f * MathF.Exp(-outside / 0.22f) * (1f - smoothstep(0.5f * reach, 0.95f * reach, outside));

                    image[x, y] = new Rgba32(toByte(colour.R), toByte(colour.G), toByte(colour.B), toByte(alpha));
                }
            }

            return image;
        }

        /// <summary>
        /// Paints a baked star: flat body colour, brightening towards the tips (that is the only gradient), a soft glow round it.
        /// <paramref name="glowStrength"/> / <paramref name="glowWidth"/> are the opacity at the star's edge and how fast the glow dies out (star units),
        /// <paramref name="edgeSoftness"/> how wide the soft edge of the star is, <paramref name="rimWidth"/> the width of a thin white outline (0 = none). With <paramref name="whiteBody"/> the star is white and only the tips are coloured.
        /// </summary>
        private static void paint(Image<Rgba32> image, int left, int top, int cell, float starFraction, float[] values, Color4 body, bool whiteBody, float glowStrength, float glowWidth, float edgeSoftness, float rimWidth)
        {
            // bullets are white and take the colour only in the gradient at the tips; the big star is the colour and gets a lighter tip
            Color4 flat = whiteBody ? Color4.White : body;
            Color4 tip = whiteBody ? body : Color4.White;
            Color4 glowColour = whiteBody ? body : mix(body, Color4.White, 0.2f); // (the glow of a bullet has its colour, that is what makes it stand out)
            float reach = 1f / starFraction - 1f; // how far the picture reaches beyond the tips

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    float d = values[y * cell + x];
                    float px = (x + 0.5f - cell / 2f) / (cell / 2f) / starFraction;
                    float py = (y + 0.5f - cell / 2f) / (cell / 2f) / starFraction;
                    float radius = MathF.Sqrt(px * px + py * py); // 1 = the tips

                    float shapeAlpha = Math.Clamp(-d / edgeSoftness, 0f, 1f);

                    // the colour is the same everywhere, only the tips get brighter
                    Color4 colour = mix(flat, tip, smoothstep(0.1f, 0.95f, radius));

                    // the thin white outline
                    if (rimWidth > 0f)
                        colour = mix(colour, Color4.White, 1f - smoothstep(0.35f * rimWidth, rimWidth, -d));

                    float haloAlpha = 0f;

                    if (d > 0f)
                        haloAlpha = glowStrength * MathF.Exp(-d / glowWidth) * (1f - smoothstep(0.5f * reach, 0.95f * reach, d)); // (gone before the edge of the picture)

                    float alpha = shapeAlpha + (1f - shapeAlpha) * haloAlpha;

                    if (alpha < 1e-4f)
                    {
                        image[left + x, top + y] = new Rgba32(toByte(body.R), toByte(body.G), toByte(body.B), 0);
                        continue;
                    }

                    float haloShare = (1f - shapeAlpha) * haloAlpha;
                    float r = (colour.R * shapeAlpha + glowColour.R * haloShare) / alpha;
                    float g = (colour.G * shapeAlpha + glowColour.G * haloShare) / alpha;
                    float b = (colour.B * shapeAlpha + glowColour.B * haloShare) / alpha;

                    image[left + x, top + y] = new Rgba32(toByte(r), toByte(g), toByte(b), toByte(alpha));
                }
            }
        }

        private static Color4 mix(Color4 a, Color4 b, float t)
            => new Color4(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);

        private static float smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static byte toByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
    }
}
