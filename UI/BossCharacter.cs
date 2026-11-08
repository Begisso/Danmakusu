using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The boss (a character of the roster, Marisa if his picture is missing) who stands opposite the real player (or, for a spinner, in the middle of the screen), gently bobbing as if idling.
    /// Behind her is a glowing, see-through magic circle that is always there, and, while her laser beams fire (when she herself is gone), a big glowing star that hides where they start; both spin slowly.
    /// While a warning lasts (before her first laser and before each later burst) a crowd of see-through, coloured like the beams and the spinner / boss bullets (the map
    /// background's colour) and sized decorative stars flies in to her from outside a circle twice as big as the magic circle. The stars do nothing:
    /// they cannot hurt, and they only vanish when they reach her centre.
    /// </summary>
    public partial class BossCharacter : CompositeDrawable
    {
        /// <summary>Height of the Marisa sprite (playfield pixels).</summary>
        private const float marisa_height = 32f;

        /// <summary>Diameter of the magic circle (playfield pixels).</summary>
        private const float circle_diameter = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f;

        /// <summary>Alpha of the magic circle ("slightly transparent").</summary>
        private const float circle_alpha = 0.55f;

        /// <summary>Degrees per second the magic circle spins at.</summary>
        private const float circle_spin = 24f;

        /// <summary>The decorative stars start on a circle with a radius of twice the magic circle's.</summary>
        private const float star_start_radius = circle_diameter;

        /// <summary>Every star has its own random radius (playfield pixels) between these two - bigger than the old, fixed 7.</summary>
        private const float star_radius_min = 11f;

        private const float star_radius_max = 26f;

        /// <summary>Every star has its own random flight time (ms) from the outer circle to the boss's centre, between these two (its "appearance speed").</summary>
        private const double star_flight_min_ms = 500;

        private const double star_flight_max_ms = 1700;

        /// <summary>A new star starts every this many ms during a warning.</summary>
        private const double star_interval_ms = 20;

        /// <summary>Stars get more see-through on their way in: 0% transparent at the outer circle, this much at the boss.</summary>
        private const float star_transparency_at_boss = 0.7f;

        private const int star_pool = 160;

        /// <summary>The stars come in this many colours (variations of <see cref="BaseColour"/>); each star picks one at random.</summary>
        private const int star_variants = 12;

        private readonly Texture?[] starTextures = new Texture?[star_variants];
        private IRenderer? renderer;
        private Color4? baseColour;

        /// <summary>The colour of the map's background; the stars are variations of it (null: pink).</summary>
        public Color4? BaseColour
        {
            get => baseColour;
            set
            {
                baseColour = value;

                if (renderer != null)
                    bakeStarTextures();
            }
        }

        /// <summary>The tips of the big star under the boss reach this many hit circle radii: its inner corners then still cover where the beams start.</summary>
        private const float big_star_radius_factor = 2.1f;

        /// <summary>The big star's picture is this much bigger than the star itself (room for the glow).</summary>
        private const float big_star_fraction = 0.6f;

        /// <summary>Degrees per second the big star spins at (the other way round than the magic circle).</summary>
        private const float big_star_spin = 40f;

        /// <summary>The picture of the glow of the magic circle has this much more room round the circle than the circle's own picture.</summary>
        private const float circle_glow_scale = 640f / 512f;

        private readonly Sprite magicCircle;
        private readonly Sprite magicGlow;
        private readonly Sprite bigStar;
        private readonly Sprite bigStarGlow;
        private readonly Sprite marisa;
        private readonly Sprite[] stars = new Sprite[star_pool];

        /// <summary>Flandre's clones: copies of the boss's picture that the field puts round her every frame (<see cref="SetClones"/>).</summary>
        private readonly Sprite[] clones = new Sprite[clone_pool];

        private readonly Vector2[] cloneOffsets = new Vector2[clone_pool];
        private float cloneAlpha;
        private int cloneCount;
        private const int clone_pool = 4;

        /// <summary>Moments (game time) that the stars fly in to the boss for as long as <see cref="DanmakusuBalance.POWER_UP_LENGTH_MS"/> before, as in a warning (set by the field every frame: Mokou's jumps).</summary>
        public readonly List<double> ExtraWarnings = new List<double>();
        private readonly Sprite[] blast = new Sprite[blast_pool];

        /// <summary>The bosses of the map by slot (A, B); their pictures are what the sprite shows.</summary>
        private readonly BossInfo[] slotBosses;
        private readonly Texture?[] slotTextures;
        private Texture? fallbackTexture;

        /// <summary>The death explosion (after the last section) lasts this long (ms).</summary>
        private const double explosion_ms = 1100;

        private const int blast_pool = 36;

        /// <summary>Where the boss stands: set by the field every frame (the explosion stays where the boss died).</summary>
        public Vector2 TargetPosition { get; set; }

        /// <summary>
        /// The boss's picture is turned against what the field does to the screen: <see cref="ArtRotation"/> (degrees) is added to its rotation (Seija's screen turns round,
        /// her picture does not - except while she warns, when it is mirrored along both axes: turned by 180 degrees).
        /// </summary>
        public float ArtRotation { get; set; }

        /// <summary>0..1: how much of the boss something hides (Utsuho's black hole): she is that much more see-through.</summary>
        public float HideAmount { get; set; }

        private bool explosionAnchored;
        private Vector2 explosionAnchor;

        /// <summary>The boss of the periods with this <see cref="BossPeriod.Rank"/> is the one drawn (the second boss of a section of two has its own character).</summary>
        private readonly int rank;

        /// <summary>The magic circle is not drawn (the boss shares the circle of the other one that is standing where it stands).</summary>
        public bool HideCircle { get; set; }

        /// <summary>
        /// This one only shows a boss who has left her section while a mechanic of hers still goes on (<see cref="BossPeriod.MechanicsEnd"/>: Miko's rays, Mima's moon), from as she leaves until it is over, at
        /// <see cref="TargetPosition"/> (where she stood): the next boss of her rank is shown by the other one meanwhile.
        /// </summary>
        public bool Lingering { get; init; }

        /// <summary>
        /// This one is a see-through copy of a boss that warns of something (Seija's clone before the screen turns): it shows the picture of <see cref="GhostSlot"/>'s boss with her magic circle at <see cref="TargetPosition"/>,
        /// <see cref="GhostAlpha"/> visible, and only the stars of <see cref="ExtraWarnings"/> fly in to it.
        /// </summary>
        public bool Ghost { get; init; }

        public float GhostAlpha { get; set; }
        public int GhostSlot { get; set; }

        /// <summary>0..1: how much of the magic circle has faded away because its boss shares the circle of the other one (they stand on each other and circle round).</summary>
        public float CircleFade { get; set; }

        /// <summary>Where the magic circle is drawn, relative to the boss (the circle that two bosses share stays where they circle round).</summary>
        public Vector2 CircleOffset { get; set; }

        /// <summary>The magic circle (and its glow) again, to be drawn somewhere else (under the cloud of Ichirin): it is then no longer drawn in the boss.</summary>
        public Drawable CreateCircleProxy() => new Container { Children = new Drawable[] { magicGlow.CreateProxy(), magicCircle.CreateProxy() } };

        public BossCharacter(BossInfo[] slotBosses, int rank = 0)
        {
            this.rank = rank;
            this.slotBosses = slotBosses;
            slotTextures = new Texture?[slotBosses.Length];

            Origin = Anchor.TopLeft; // positioned manually, children are centred on (0,0)
            Alpha = 0;

            var children = new List<Drawable>();

            children.Add(magicGlow = new Sprite
            {
                Origin = Anchor.Centre,
                Size = new Vector2(circle_diameter * circle_glow_scale),
                Alpha = 0.6f,
                Blending = BlendingParameters.Additive,
            });

            children.Add(magicCircle = new Sprite
            {
                Origin = Anchor.Centre,
                Size = new Vector2(circle_diameter),
                Alpha = circle_alpha,
            });

            for (int i = 0; i < star_pool; i++)
            {
                children.Add(stars[i] = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(star_radius_max * 2 / SoftStar.StarFraction), // (the soft star's picture has room for its glow)
                    Alpha = 0,
                });
            }

            for (int i = 0; i < blast_pool; i++)
            {
                children.Add(blast[i] = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(star_radius_max * 2 / SoftStar.StarFraction),
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                });
            }

            children.Add(bigStarGlow = new Sprite
            {
                Origin = Anchor.Centre,
                Blending = BlendingParameters.Additive, // (the glow only brightens, so there is no darker rim round the star)
            });

            children.Add(bigStar = new Sprite
            {
                Origin = Anchor.Centre,
            });

            for (int i = 0; i < clone_pool; i++)
            {
                children.Add(clones[i] = new Sprite
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(marisa_height * 172f / 193f, marisa_height),
                    Alpha = 0,
                });
            }

            children.Add(marisa = new Sprite
            {
                Origin = Anchor.Centre,
                Size = new Vector2(marisa_height * 172f / 193f, marisa_height),
            });

            InternalChildren = children;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            this.renderer = renderer;

            magicCircle.Texture = loadTexture(renderer, "magic_circle.png");
            magicGlow.Texture = loadTexture(renderer, "magic_circle_glow.png");
            fallbackTexture = loadTexture(renderer, "marisa.png");
            marisa.Texture = fallbackTexture;

            for (int i = 0; i < slotBosses.Length; i++)
                slotTextures[i] = LoadResource(renderer, $"Bosses.Sprites.{slotBosses[i].Id}.png") ?? fallbackTexture;

            bakeStarTextures();
        }

        /// <summary>One star picture per colour variant: the soft, glowing star shared with the spinner and boss bullets.</summary>
        private void bakeStarTextures()
        {
            if (renderer == null)
                return;

            Color4 baseColor = baseColour ?? new Color4(1f, 0.75f, 0.85f, 1f);

            var bigImage = SoftStar.CreateBig(256, big_star_fraction, baseColor);
            var bigTexture = renderer.CreateTexture(bigImage.Width, bigImage.Height);
            bigTexture.SetData(new TextureUpload(bigImage));
            bigStar.Texture = bigTexture;

            var glowImage = SoftStar.CreateGlow(256, big_star_fraction, baseColor);
            var glowTexture = renderer.CreateTexture(glowImage.Width, glowImage.Height);
            glowTexture.SetData(new TextureUpload(glowImage));
            bigStarGlow.Texture = glowTexture;

            // the warning stars have exactly the colour of the beams and of the spinner / boss bullets (the map background's colour), like the big star
            var image = new Image<Rgba32>(SoftStar.CELL, SoftStar.CELL);
            SoftStar.Bake(image, 0, 0, baseColor, 0.07f); // (a softer edge: the decorative stars are smoother)

            var texture = renderer.CreateTexture(SoftStar.CELL, SoftStar.CELL);
            texture.SetData(new TextureUpload(image));

            for (int v = 0; v < star_variants; v++)
                starTextures[v] = texture;

            foreach (var sprite in blast)
                sprite.Texture = texture;
        }

        /// <summary>Reads a picture that is embedded in the ruleset (Resources/Textures); null if it is missing, then it is just not drawn.</summary>
        private static Texture? loadTexture(IRenderer renderer, string name)
            => LoadResource(renderer, $"Textures.{name}");

        /// <summary>Reads a picture that is embedded in the ruleset (<paramref name="path"/> is relative to Resources, with dots instead of slashes); null if it is missing.</summary>
        public static Texture? LoadResource(IRenderer renderer, string path)
        {
            try
            {
                using Stream? stream = typeof(BossCharacter).Assembly.GetManifestResourceStream($"osu.Game.Rulesets.Danmakusu.Resources.{path}");

                if (stream == null)
                    return null;

                // (not disposed here: the upload still needs the picture on the draw thread, and it disposes it itself afterwards)
                var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                var texture = renderer.CreateTexture(image.Width, image.Height);
                texture.SetData(new TextureUpload(image));
                return texture;
            }
            catch
            {
                return null;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (Alpha <= 0)
                return;

            double t = Time.Current / 1000.0;

            // idling: a slow bob with a little squash and sway
            marisa.Y = (float)(Math.Sin(t * 2.4) * 3.0) - 2f;
            marisa.Scale = new Vector2(1f + (float)Math.Sin(t * 2.4 + 1.2) * 0.012f, 1f + (float)Math.Sin(t * 2.4) * 0.02f);
            marisa.Rotation = (float)(Math.Sin(t * 1.2) * 2.0) + ArtRotation;

            // (the clones bob with her)
            for (int i = 0; i < clone_pool; i++)
            {
                var clone = clones[i];

                if (i >= cloneCount || cloneAlpha <= 0f)
                {
                    clone.Alpha = 0f;
                    continue;
                }

                clone.Texture = marisa.Texture;
                clone.Position = cloneOffsets[i] + new Vector2(0f, marisa.Y);
                clone.Scale = marisa.Scale;
                clone.Rotation = marisa.Rotation;
                clone.Alpha = cloneAlpha * marisa.Alpha;
            }

            magicCircle.Rotation = (float)(t * circle_spin);
            magicGlow.Rotation = magicCircle.Rotation;
            bigStar.Rotation = (float)(-t * big_star_spin);
            bigStarGlow.Rotation = bigStar.Rotation;
        }

        /// <summary>Hides the boss.</summary>
        public void HideBoss()
        {
            Alpha = 0;
            cloneCount = 0;
        }

        /// <summary>The clones that stand round the boss this frame (positions relative to her), that much there (0..1). Called every frame that there are any.</summary>
        public void SetClones(Vector2[] offsets, float alpha)
        {
            cloneCount = Math.Min(offsets.Length, clone_pool);
            cloneAlpha = alpha;

            for (int i = 0; i < cloneCount; i++)
                cloneOffsets[i] = offsets[i];
        }

        /// <summary>No clones this frame.</summary>
        public void ClearClones()
        {
            cloneCount = 0;
            cloneAlpha = 0f;
        }

        /// <summary>
        /// Shows the boss at <paramref name="now"/> if one of the <paramref name="periods"/> is on (they can overlap, then she stays);
        /// <paramref name="circleRadius"/> is the radius of a hit circle (playfield pixels).
        /// </summary>
        public void UpdateState(double now, BossPeriod[] periods, float circleRadius)
        {
            const double fade = 300;
            const double fade_in_for_laser = 250; // the big star fades in when a laser burst starts, Marisa fades out...
            const double fade_out_after_laser = 300; // ...and back the other way when it is over

            if (Ghost)
            {
                // (a see-through copy where the field puts it: the picture and the magic circle, and the stars of its warning)
                Alpha = Math.Clamp(GhostAlpha, 0f, 1f);

                if (GhostSlot >= 0 && GhostSlot < slotTextures.Length)
                    marisa.Texture = slotTextures[GhostSlot] ?? fallbackTexture;

                explosionAnchored = false;
                Position = TargetPosition;
                resetExplosion();
                magicCircle.Position = Vector2.Zero;
                magicGlow.Position = Vector2.Zero;
                marisa.Alpha = 1f;
                bigStar.Alpha = 0f;
                bigStarGlow.Alpha = 0f;
                updateStars(now, periods);
                return;
            }

            float alpha = 0;
            float laserPhase = 0; // 0 = a pause, 1 = a laser burst is on
            BossPeriod? latest = null;
            BossPeriod? dying = null;

            foreach (var period in periods)
            {
                if (period.Rank != rank || period.Borrowed)
                    continue;

                if (Lingering)
                {
                    // (a boss who leaves her section while a mechanic of hers goes on: from as she leaves until it is over, where she stood)
                    double from = period.End - fade;

                    if (period.IsLast || period.MechanicsEnd <= period.End || now < from || now > period.MechanicsEnd)
                        continue;

                    if (latest == null || period.Start >= latest.Start)
                        latest = period;

                    alpha = Math.Max(alpha, (float)Math.Clamp(Math.Min((now - from) / fade, (period.MechanicsEnd - now) / fade), 0, 1));
                    continue;
                }

                if (period.IsLast && now > period.End && now <= period.End + explosion_ms)
                    dying = period;

                if (now < period.Start || now > period.End)
                    continue;

                // (while the next boss only gives her warning the one before her still shoots: she is the one drawn, so that her shots do not come out of the next one)
                if (latest == null || BulletField.Supersedes(period, latest, now))
                    latest = period;

                // (the boss of the last section does not fade out: he explodes when it ends)
                double fadeOut = period.IsLast ? 1 : (period.End - now) / fade;
                alpha = Math.Max(alpha, (float)Math.Clamp(Math.Min((now - period.Start) / fade, fadeOut), 0, 1));

                for (int i = 0; period.HasLasers && i < period.LaserStarts.Length && i < period.LaserEnds.Length; i++)
                {
                    double start = period.LaserStarts[i], end = period.LaserEnds[i];

                    if (now < start || now > end + fade_out_after_laser)
                        continue;

                    float phase = now <= end
                        ? (float)Math.Clamp((now - start) / fade_in_for_laser, 0, 1)
                        : (float)Math.Clamp(1 - (now - end) / fade_out_after_laser, 0, 1);

                    laserPhase = Math.Max(laserPhase, phase);
                }
            }

            // the magic circle (and the stars flying in) are always there; Marisa only in the pauses between bursts, the big star only while a burst is on
            Alpha = alpha * (1f - Math.Clamp(HideAmount, 0f, 1f));

            if (latest != null && latest.BossSlot < slotTextures.Length)
                marisa.Texture = slotTextures[latest.BossSlot] ?? fallbackTexture;

            if (dying != null)
            {
                if (!explosionAnchored)
                {
                    explosionAnchored = true;
                    explosionAnchor = TargetPosition;
                }

                Position = explosionAnchor;
                Alpha = 1;
                updateExplosion((float)((now - dying.End) / explosion_ms));
                return;
            }

            explosionAnchored = false;
            Position = TargetPosition;
            resetExplosion();

            magicCircle.Position = CircleOffset;
            magicGlow.Position = CircleOffset;

            if (HideCircle)
            {
                magicCircle.Alpha = 0f;
                magicGlow.Alpha = 0f;
            }
            else if (CircleFade > 0.001f)
            {
                magicCircle.Alpha *= 1f - Math.Clamp(CircleFade, 0f, 1f);
                magicGlow.Alpha *= 1f - Math.Clamp(CircleFade, 0f, 1f);
            }

            marisa.Alpha = 1f - laserPhase;
            bigStar.Alpha = laserPhase;
            bigStarGlow.Alpha = laserPhase;

            float starSize = 2f * circleRadius * big_star_radius_factor / big_star_fraction;
            bigStar.Size = new Vector2(starSize);
            bigStarGlow.Size = new Vector2(starSize);

            updateStars(now, periods);
        }

        /// <summary>The boss dies: a flash, a ring that spreads and stars that fly out, while the sprite and the magic circle are gone (u: 0..1).</summary>
        private void updateExplosion(float u)
        {
            float fadeOut = 1f - u;
            marisa.Alpha = u < 0.04f ? 1f : 0f; // (a last frame of the boss)
            bigStar.Alpha = 0;

            magicCircle.Alpha = circle_alpha * fadeOut * fadeOut;
            magicCircle.Scale = new Vector2(1f + u * 0.8f);
            magicGlow.Alpha = 0.9f * fadeOut;
            magicGlow.Scale = new Vector2(0.4f + u * 3f);

            bigStarGlow.Alpha = fadeOut * fadeOut;
            bigStarGlow.Scale = new Vector2(0.5f + u * 2.5f);

            for (int i = 0; i < blast_pool; i++)
            {
                double angle = i * 2.3999632 + random01(i, 77, 5) * 0.5;
                float reach = 28f + 90f * (float)random01(i, 77, 6);
                float eased = 1f - (1f - u) * (1f - u);
                var sprite = blast[i];

                sprite.Position = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (reach * eased);
                sprite.Scale = new Vector2((0.35f + 0.65f * (float)random01(i, 77, 7)) * (1f - 0.5f * u));
                sprite.Alpha = fadeOut;
                sprite.Rotation = (float)(angle * 57.3 + u * 180);
            }

            for (int i = 0; i < star_pool; i++)
                stars[i].Alpha = 0;
        }

        private void resetExplosion()
        {
            magicCircle.Scale = Vector2.One;
            magicCircle.Alpha = circle_alpha;
            magicGlow.Scale = Vector2.One;
            magicGlow.Alpha = 0.6f;
            bigStarGlow.Scale = Vector2.One;

            foreach (var sprite in blast)
                sprite.Alpha = 0;
        }

        /// <summary>
        /// The decorative stars of the warnings: before the boss's first laser (the warning that starts when she appears) and before each later laser burst.
        /// A star is a pure function of time: star i of a warning starts at its start + i x 20 ms, somewhere on the outer circle, and flies straight to the
        /// boss's centre, getting more transparent on the way. Its colour, size and flight time (speed) are random, but fixed by its index.
        /// </summary>
        private void updateStars(double now, BossPeriod[] periods)
        {
            int used = 0;

            // (a boss who lingers has no warnings: the stars of the next boss of her rank are the other one's)
            if (Lingering)
            {
                for (int i = 0; i < star_pool; i++)
                    stars[i].Alpha = 0;

                return;
            }

            // the stars of one warning that starts at windowStart and ends at windowEnd (nothing for a warning that starts after limit)
            void warningStars(double windowStart, double windowEnd, double limit)
            {
                if (now < windowStart || now > windowEnd + star_flight_max_ms || windowStart >= limit)
                    return;

                int count = (int)Math.Floor((windowEnd - windowStart) / star_interval_ms);
                int first = Math.Max(0, (int)Math.Floor((now - star_flight_max_ms - windowStart) / star_interval_ms) + 1);
                int last = Math.Min(count - 1, (int)Math.Floor((now - windowStart) / star_interval_ms));
                uint seed = (uint)(Math.Round(windowStart) % 100000 + 1);

                for (int i = first; i <= last && used < star_pool; i++)
                {
                    double flight = star_flight_min_ms + (star_flight_max_ms - star_flight_min_ms) * random01(i, seed, 1);
                    float progress = (float)((now - (windowStart + i * star_interval_ms)) / flight);

                    if (progress < 0 || progress >= 1)
                        continue;

                    float angle = (float)(random01(i, seed, 2) * Math.PI * 2);
                    float radius = star_radius_min + (star_radius_max - star_radius_min) * (float)random01(i, seed, 3);

                    var sprite = stars[used++];
                    sprite.Position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (star_start_radius * (1f - progress));
                    sprite.Scale = new Vector2(radius / star_radius_max);
                    sprite.Texture = starTextures[(int)(random01(i, seed, 4) * star_variants) % star_variants];
                    sprite.Alpha = 1f - star_transparency_at_boss * progress;
                    sprite.Rotation = (float)(now * 0.18 + i * 37);
                }
            }

            foreach (var period in periods)
            {
                // (a ghost has only the warnings it is given)
                if (period.Rank != rank || period.Borrowed || Ghost)
                    continue;

                double warningLength = period.ShootStart - period.Start;
                var laserStarts = period.LaserStarts.Length > 0 ? period.LaserStarts : new[] { period.ShootStart };

                // (every boss has the one warning of her section here, when she appears: before her other attacks the bullets flash instead, see BulletField.flashTimes)
                int windows = Math.Min(1, laserStarts.Length);

                for (int w = 0; w < windows; w++)
                {
                    // (Reisen's stars fly in only before the attacks she slides in: every second one, from the first)
                    if (period.Boss?.Mechanic == BossMechanic.Lunatic && w % 2 == 1)
                        continue;

                    warningStars(w == 0 ? period.Start : laserStarts[w] - warningLength, laserStarts[w], period.End);
                }
            }

            foreach (double end in ExtraWarnings)
                warningStars(end - DanmakusuBalance.POWER_UP_LENGTH_MS, end, double.PositiveInfinity);

            for (int i = used; i < star_pool; i++)
                stars[i].Alpha = 0;
        }

        /// <summary>A deterministic "random" number in [0, 1) from a star's index, the warning's seed and a salt.</summary>
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
