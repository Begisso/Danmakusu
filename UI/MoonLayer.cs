using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>Where the moon and the laser source of a Clownpiece section will appear, and when the warning for it starts and ends.</summary>
    public readonly record struct MoonWarning(double Start, double End, Vector2 Moon, float MoonRadius, Vector2 Source);

    /// <summary>
    /// What Clownpiece's attack looks like besides her bullets: the moon (Resources/Textures/moon.png), the big glowing star where the beams of the moon laser start
    /// (the same one that hides the start of a boss's beams), and the warning that leads into it: the same see-through stars that fly in to a boss when she appears,
    /// here flying in to the place where the moon and the laser source are about to appear. The beams themselves are drawn by the bullet renderer.
    /// Positions are in the field's coordinates; the field sets them every frame.
    /// </summary>
    public partial class MoonLayer : CompositeDrawable
    {
        /// <summary>The warning stars start on a circle this big (playfield pixels), like the boss's: twice the magic circle's radius.</summary>
        private const float star_start_radius = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f;

        private const float star_radius_min = 11f;
        private const float star_radius_max = 26f;
        private const double star_flight_min_ms = 500;
        private const double star_flight_max_ms = 1700;
        private const double star_interval_ms = 20;
        private const float star_transparency_at_target = 0.7f;
        private const int star_pool = 220;

        /// <summary>The picture of the moon fills this much of its width (the circle touches the picture's edges).</summary>
        private const float moon_picture_fill = 319f / 320f;

        private const float big_star_radius_factor = 2.1f;
        private const float big_star_fraction = 0.6f;
        private const float big_star_spin = 40f;

        private readonly Sprite moon;
        private readonly Sprite sourceStar;
        private readonly Sprite sourceGlow;
        private readonly Sprite[] stars = new Sprite[star_pool];

        private IRenderer? renderer;
        private Texture? starTexture;
        private Color4? baseColour;

        /// <summary>The colour of the map's background, which the stars and the big star are made in (null: pink).</summary>
        public Color4? BaseColour
        {
            get => baseColour;
            set
            {
                baseColour = value;

                if (renderer != null)
                    bakeTextures();
            }
        }

        /// <param name="above">
        /// false: the moon and the warning stars (drawn under the bullets and beams); true: the big star where the beams start (drawn above the beams, so it hides their start).
        /// </param>
        public MoonLayer(bool above)
        {
            Origin = Anchor.TopLeft; // positioned manually: children use the field's coordinates

            moon = new Sprite { Origin = Anchor.Centre, Alpha = 0 };
            sourceGlow = new Sprite { Origin = Anchor.Centre, Alpha = 0, Blending = BlendingParameters.Additive };
            sourceStar = new Sprite { Origin = Anchor.Centre, Alpha = 0 };

            if (above)
            {
                AddInternal(sourceGlow);
                AddInternal(sourceStar);
            }
            else
                AddInternal(moon);

            for (int i = 0; i < star_pool; i++)
            {
                stars[i] = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(star_radius_max * 2 / SoftStar.StarFraction),
                    Alpha = 0,
                };

                if (!above)
                    AddInternal(stars[i]);
            }
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            this.renderer = renderer;
            moon.Texture = BossCharacter.LoadResource(renderer, "Textures.moon.png");
            bakeTextures();
        }

        private void bakeTextures()
        {
            if (renderer == null)
                return;

            Color4 colour = baseColour ?? new Color4(1f, 0.75f, 0.85f, 1f);

            var bigImage = SoftStar.CreateBig(256, big_star_fraction, colour);
            var bigTexture = renderer.CreateTexture(bigImage.Width, bigImage.Height);
            bigTexture.SetData(new TextureUpload(bigImage));
            sourceStar.Texture = bigTexture;

            var glowImage = SoftStar.CreateGlow(256, big_star_fraction, colour);
            var glowTexture = renderer.CreateTexture(glowImage.Width, glowImage.Height);
            glowTexture.SetData(new TextureUpload(glowImage));
            sourceGlow.Texture = glowTexture;

            var image = new Image<Rgba32>(SoftStar.CELL, SoftStar.CELL);
            SoftStar.Bake(image, 0, 0, colour, 0.07f);

            starTexture = renderer.CreateTexture(SoftStar.CELL, SoftStar.CELL);
            starTexture.SetData(new TextureUpload(image));
        }

        /// <summary>The moon and the laser source (each layer shows its own part), <paramref name="presence"/> 0..1 (0 = not there); <paramref name="moonRadius"/> is already scaled by it.</summary>
        public void SetState(float presence, Vector2 moonPosition, float moonRadius, float angle, Vector2 sourcePosition, float circleRadius)
        {
            if (presence <= 0)
            {
                moon.Alpha = 0;
                sourceStar.Alpha = 0;
                sourceGlow.Alpha = 0;
                return;
            }

            moon.Position = moonPosition;
            moon.Size = new Vector2(moonRadius * 2f / moon_picture_fill);
            moon.Rotation = angle * 180f / MathF.PI;
            moon.Alpha = presence;

            float size = 2f * circleRadius * big_star_radius_factor / big_star_fraction * presence;
            sourceStar.Position = sourcePosition;
            sourceGlow.Position = sourcePosition;
            sourceStar.Size = new Vector2(size);
            sourceGlow.Size = new Vector2(size);
            sourceStar.Rotation = (float)(-Time.Current / 1000.0 * big_star_spin);
            sourceGlow.Rotation = sourceStar.Rotation;
            sourceStar.Alpha = presence;
            sourceGlow.Alpha = presence;
        }

        /// <summary>
        /// The warning stars of every warning that is on at <paramref name="now"/>: star i of a warning starts at its start + i x 20 ms somewhere on the outer circle round the target
        /// (the moon, or the laser source) and flies straight to its centre, getting more see-through. Pure functions of time, like the boss's stars.
        /// </summary>
        public void UpdateWarnings(double now, System.Collections.Generic.IReadOnlyList<MoonWarning> warnings)
        {
            int used = 0;

            foreach (var warning in warnings)
            {
                for (int target = 0; target < 2; target++)
                {
                    Vector2 centre = target == 0 ? warning.Moon : warning.Source;
                    float startRadius = target == 0 ? Math.Max(star_start_radius, warning.MoonRadius * 2f) : star_start_radius;
                    double start = warning.Start, end = warning.End;

                    if (now < start || now > end + star_flight_max_ms)
                        continue;

                    int count = (int)Math.Floor((end - start) / star_interval_ms);
                    int first = Math.Max(0, (int)Math.Floor((now - star_flight_max_ms - start) / star_interval_ms) + 1);
                    int last = Math.Min(count - 1, (int)Math.Floor((now - start) / star_interval_ms));
                    uint seed = (uint)(Math.Round(start) % 100000 + 1 + target * 7919);

                    for (int i = first; i <= last && used < star_pool; i++)
                    {
                        double flight = star_flight_min_ms + (star_flight_max_ms - star_flight_min_ms) * random01(i, seed, 1);
                        float progress = (float)((now - (start + i * star_interval_ms)) / flight);

                        if (progress < 0 || progress >= 1)
                            continue;

                        float angle = (float)(random01(i, seed, 2) * Math.PI * 2);
                        float radius = star_radius_min + (star_radius_max - star_radius_min) * (float)random01(i, seed, 3);

                        var sprite = stars[used++];
                        sprite.Texture = starTexture;
                        sprite.Position = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (startRadius * (1f - progress));
                        sprite.Scale = new Vector2(radius / star_radius_max);
                        sprite.Alpha = 1f - star_transparency_at_target * progress;
                        sprite.Rotation = (float)(now * 0.18 + i * 37);
                    }
                }
            }

            for (int i = used; i < star_pool; i++)
                stars[i].Alpha = 0;
        }

        private static double random01(int index, uint seed, uint salt)
        {
            unchecked
            {
                uint h = (uint)index * 747796405u + seed * 2891336453u + salt * 277803737u + 1013904223u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
