using System;
using System.IO;
using System.Reflection;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The colour of the map's background picture: the beams and stars of spinners and bosses are drawn in it.
    /// It is the average of the picture's pixels, with the more colourful ones counting more; a dull colour is lifted to a clear one and a
    /// (nearly) black or dark grey one becomes gray, like <see cref="DanmakusuBalance.BulletColour"/> does for the combo colours of the bullets.
    /// </summary>
    public static class BackgroundColour
    {
        private const int samples = 32;

        /// <summary>
        /// Reads the background of a working beatmap (the object osu! gives to gameplay). The beatmap storage is reached by reflection, so this
        /// does not depend on the exact shape of osu!'s API: null whenever the picture cannot be found or read (the combo colours are used then).
        /// </summary>
        public static Color4? Of(object? workingBeatmap)
        {
            if (workingBeatmap == null)
                return null;

            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var type = workingBeatmap.GetType();

                object? info = type.GetProperty("BeatmapInfo", flags)?.GetValue(workingBeatmap);
                object? metadata = info?.GetType().GetProperty("Metadata", flags)?.GetValue(info);
                string? file = metadata?.GetType().GetProperty("BackgroundFile", flags)?.GetValue(metadata) as string;

                if (string.IsNullOrEmpty(file))
                    return null;

                object? set = type.GetProperty("BeatmapSetInfo", flags)?.GetValue(workingBeatmap) ?? info?.GetType().GetProperty("BeatmapSet", flags)?.GetValue(info);
                string? path = set?.GetType().GetMethod("GetPathForFile", flags, null, [typeof(string)], null)?.Invoke(set, [file]) as string;

                if (string.IsNullOrEmpty(path))
                    return null;

                using Stream? stream = type.GetMethod("GetStream", flags, null, [typeof(string)], null)?.Invoke(workingBeatmap, [path]) as Stream;

                if (stream == null)
                    return null;

                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                return FromImage(image);
            }
            catch
            {
                return null;
            }
        }

        public static Color4? FromImage(Image<Rgba32> image)
        {
            if (image.Width == 0 || image.Height == 0)
                return null;

            double sumR = 0, sumG = 0, sumB = 0, sumWeight = 0;

            for (int sy = 0; sy < samples; sy++)
            {
                for (int sx = 0; sx < samples; sx++)
                {
                    var pixel = image[Math.Min(image.Width - 1, (sx * image.Width + image.Width / 2) / samples), Math.Min(image.Height - 1, (sy * image.Height + image.Height / 2) / samples)];
                    double r = pixel.R / 255.0, g = pixel.G / 255.0, b = pixel.B / 255.0;
                    double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    double saturation = max <= 0 ? 0 : (max - min) / max;
                    double weight = 0.05 + saturation * max; // colourful pixels count more

                    sumR += r * weight;
                    sumG += g * weight;
                    sumB += b * weight;
                    sumWeight += weight;
                }
            }

            if (sumWeight <= 0)
                return null;

            float cr = (float)(sumR / sumWeight), cg = (float)(sumG / sumWeight), cb = (float)(sumB / sumWeight);
            float top = Math.Max(cr, Math.Max(cg, cb)), bottom = Math.Min(cr, Math.Min(cg, cb));
            float colourfulness = top <= 0 ? 0 : (top - bottom) / top;

            // a clearly coloured background gives a clear colour, however dark the picture is
            if (colourfulness > 0.2f && top > 0.02f)
            {
                float lift = 0.9f / top;
                cr = Math.Min(1f, cr * lift);
                cg = Math.Min(1f, cg * lift);
                cb = Math.Min(1f, cb * lift);
            }

            // (near) black / dark grey stays visible as gray, like the bullets
            return DanmakusuBalance.BulletColour(new Color4(cr, cg, cb, 1f));
        }
    }
}
