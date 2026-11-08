using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// A four-cornered piece of a flat shape, drawn over the beams and under the bullets: <see cref="A"/> and <see cref="B"/> along one side in <see cref="Near"/>, <see cref="C"/> and <see cref="D"/> along the other in
    /// <see cref="Far"/> (Mima's moon: its pieces lie side by side, so nothing is drawn twice where it is see-through).
    /// </summary>
    public struct MeshQuad
    {
        public Vector2 A, B, C, D;
        public Color4 Near, Far;
    }

    /// <summary>A picture drawn between the beams and the bullets (Tojiko's clouds: over her lightning and her rain, under every other bullet).</summary>
    public struct CloudSprite
    {
        public Vector2 Position;
        public Vector2 Size;
        public Color4 Colour;

        /// <summary>It lights up what is under it.</summary>
        public bool Additive;

        /// <summary>The soft light of a flash instead of a puff of cloud.</summary>
        public bool Flash;
    }

    public struct BulletInstance
    {
        public Vector2 Position;
        public float Radius;

        /// <summary>Which colour of the palette the bullet has (a row of the baked texture sheet).</summary>
        public int Style;

        /// <summary>Unit direction of travel; ellipses and triangles point along it.</summary>
        public Vector2 Direction;

        public BulletShape Shape;

        /// <summary>Pixels per ms. The list is sorted by this, slowest first, so faster bullets are drawn above slower ones.</summary>
        public float Speed;

        /// <summary>0 = the bullet has its own colour; otherwise one of the BulletRenderer.TINT_ colours, shown <see cref="OverlayAmount"/> (0..1) of the way over the bullet's own.</summary>
        public int Overlay;

        public float OverlayAmount;

        /// <summary>0 = fully visible, 1 = invisible.</summary>
        public float Fade;

        /// <summary>0 = as bright as it is, 1 = black; below 0 it is brighter.</summary>
        public float Shade;

        /// <summary>Nothing dims it: neither the dark places of the acts nor the beams (Tojiko's rain, and what her lightning throws back).</summary>
        public bool Undimmed;

        /// <summary>It is drawn under Tojiko's clouds (her rain, which falls out of them).</summary>
        public bool UnderClouds;

        /// <summary>A picture drawn on the bullet (a snowflake in its body).</summary>
        public BulletDecal Decal;

        /// <summary>0 = the decal is fully there, 1 = not at all (it fades in).</summary>
        public float DecalFade;

        /// <summary>A smiling face drawn over the bullet's body, always upright (Koishi's bullets while everything is invisible).</summary>
        public bool Smile;

        /// <summary>
        /// It is part of a boss's own thing, not a bullet that flies (Yukari's trains, the beads of the acts: chains, what the worlds and hands hold, leaves, petals and heads on stems and blades; the flowers on Yuyuko's tree and Lily's branches):
        /// the hiding of the other bosses (Rumia's circles, Koishi's vanishing, Sannyo's smoke making it see-through) leaves it as it is.
        /// </summary>
        public bool Immune;

        /// <summary>One of Daiyousei's bullets that has left the screen and come in again: it has lost her buff, so her glow is not put on it (see BulletField's bloom).</summary>
        public bool Unbuffed;

        /// <summary>A talisman over the bullet, fitted inside its body, with its long side along <see cref="TalismanFace"/> (Seiga Kaku's returning bullets).</summary>
        public bool Talisman;

        public Vector2 TalismanFace;

        /// <summary>For a talisman: how much of the way from the bullet's own shape (<see cref="Under"/>) it has got (0..1).</summary>
        public float Morph;

        /// <summary>The bullet's own shape, shown faded out under a talisman that it is turning into (or out of).</summary>
        public BulletShape Under;

        /// <summary>0 = no bloom, 1 = the strongest: a soft light spreads round the bullet and over it (Remilia's attack).</summary>
        public float Bloom;

        /// <summary>A knife with a red handle instead of the green one (Remilia's).</summary>
        public bool RedHandle;

        /// <summary>A knife with a blue handle (Chimata's).</summary>
        public bool BlueHandle;

        /// <summary>0 = sharp; 1 = the bullet is drawn from the soft sheet (the same picture, blurred, so its edge cannot be seen at all: Luna Child's); in between the two are faded into each other.</summary>
        public float Soft;

        /// <summary>How many blades the knife has (joined at their handles; 0 and 1 = a plain knife).</summary>
        public int Blades;

        /// <summary>How far the blades of a many-bladed knife are turned (it spins round its handle).</summary>
        public float BladeAngle;

        /// <summary>A knife's picture is this many times as long (0 and 1 = its own length: Nemuno's knives have blades twice as long).</summary>
        public float Stretch;

        /// <summary>The black hole bends the light: the whole picture is drawn this many times as long along <see cref="LensAxis"/> (the circle round the hole) and squeezed across it (1 or less = not at all).</summary>
        public float LensStretch;

        public Vector2 LensAxis;

        /// <summary>The middle of the black hole: a stretched bullet is also bent round it (its picture laid along the circle through its middle).</summary>
        public Vector2 LensCentre;

        /// <summary>When above 1 the bullet is drawn this many times as long along its <see cref="Direction"/> and thinner across it (Utsuho's rays: spaghettified).</summary>
        public float Elongate;
    }

    /// <summary>One laser beam: its centre line is <see cref="Count"/> points of the shared point buffer, starting at <see cref="Start"/>.</summary>
    public struct LaserInstance
    {
        public int Start;
        public int Count;

        public float HalfWidth;

        /// <summary>0..1: how much light spreads round the beam (Remilia's attack).</summary>
        public float Bloom;

        /// <summary>A spinner / boss beam (no longer drawn any differently).</summary>
        public bool Glow;

        /// <summary>The dark edge and the coloured layer round the core are half as thick (Marisa's Master Spark: a wide white beam).</summary>
        public bool Thin;

        /// <summary>
        /// From this distance (flown from where a slice was fired) the dark edge of the beam starts to change from the colour of the beam to the
        /// dark <see cref="BorderColour"/>, which it has fully at <see cref="BorderFadeEnd"/>. Both 0 = the edge is dark all along.
        /// </summary>
        public float BorderFadeStart;

        public float BorderFadeEnd;

        public Color4 Colour;
        public Color4 BorderColour;

        /// <summary>A branch of Yuyuko's tree: all of them are drawn layer by layer together (every border, then every body, then every core), so they are one piece of wood with no seams; <see cref="CoreColour"/> is the lighter middle.</summary>
        public bool Wood;

        /// <summary>No boss's mechanic changes how it looks (Marisa's Master Spark: no blue of Wakasagihime's, no glow of Remilia's).</summary>
        public bool Immune;

        /// <summary>(Of a beam that is not wood: its core is this colour when one is given, alpha above 0; otherwise it is white, or dark in a white beam.)</summary>
        public Color4 CoreColour;

        /// <summary>Beams handed over one after the other with the same number (above 0) are drawn layer by layer together like wood (every edge, then every body, then every core), so they are one piece with no seams where they meet: Tojiko's bolt and its branches.</summary>
        public int Piece;

        /// <summary>Pixels per ms. Sorted slowest first, so faster beams are drawn above slower ones (and above slower bullets).</summary>
        public float Speed;
    }

    /// <summary>
    /// Draws every live bullet and laser with a single draw node (no per-bullet Drawable).
    /// Each bullet is ONE quad of a pre-rendered texture sheet (<see cref="SetPalette"/>): a contrasting border shape, the combo-coloured body,
    /// and a radial gradient that fades from a white centre into the body colour (for white bullets the centre is dark instead, so it stays visible),
    /// already composited. The shape is a disc, an ellipse (the disc cell stretched) or an arrowhead (its own cell).
    /// </summary>
    public partial class BulletRenderer : Sprite
    {
        /// <summary>Fraction of the bullet radius covered by the body (the rest is the border).</summary>
        private const float body_scale = 0.84f;

        /// <summary>Fraction of a laser's half width covered by its bright core.</summary>
        private const float laser_core_scale = 0.4f;

        /// <summary>The same for spinner / boss beams: the coloured layer between the core and the edge is half as thick as it is for slider beams.</summary>
        private const float glow_core_scale = body_scale - (body_scale - laser_core_scale) / 2f;

        /// <summary>A thin-edged beam (see <see cref="LaserInstance.Thin"/>): the edge and the coloured layer are half as thick as a boss beam's.</summary>
        private const float thin_body_scale = 1f - (1f - body_scale) / 2f;

        private const float thin_core_scale = thin_body_scale - (body_scale - glow_core_scale) / 2f;

        private BulletInstance[] instances = new BulletInstance[256];
        private int count;
        private Vector2[] laserPoints = new Vector2[1024];
        private float[] laserWidths = new float[1024]; // half width of the beam at every point
        private float[] laserDistances = new float[1024]; // how far the slice at every point has flown
        private float[] laserFades = new float[1024]; // how much of its colour the beam shows at every point (1 = all; a beam in smoke is half see-through)
        private float brightness = 1f;
        private int laserPointCount;
        private LaserInstance[] lasers = new LaserInstance[16];
        private int laserCount;
        private IRenderer? renderer;
        private Texture? sheet;
        private Texture? softSheet; // (the same sheet, every cell's picture at half its size in the middle of its cell and blurred: drawn twice as big it is the bullet with an edge that fades into nothing)
        private int sheetStyles;
        private int sheetBase; // the rows of the palette; the special colours (TINT_) are the rows after them
        private Texture? knifeTexture, knifeRedTexture, knifeBlueTexture, trainTexture, snowflakeTexture, haloTexture; // pictures that are drawn as they are (Resources/Textures); null if missing
        private Texture? smileTexture, skullTexture;
        private Texture? cloudTexture, flashTexture;
        private CloudSprite[] clouds = new CloudSprite[64];
        private int cloudCount;
        private MeshQuad[] meshes = new MeshQuad[64];
        private int meshCount;
        private Texture? talismanBack, talismanLines; // the talisman: the body (white: tinted with the bullet's colour) and the border and writing over it (white)
        private Color4[] bodyColours = [];

        /// <summary>The tint colours a bullet can be shown in instead of its own (<see cref="BulletInstance.Overlay"/> = 1 + index): red, blue, purple, light orange, sakura pink.</summary>
        public static readonly Color4[] SpecialBodies =
        {
            new Color4(0.92f, 0.16f, 0.18f, 1f),
            new Color4(0.38f, 0.68f, 1f, 1f),
            new Color4(0.66f, 0.33f, 0.9f, 1f),
            new Color4(1f, 0.74f, 0.4f, 1f),
            new Color4(1f, 0.55f, 0.70f, 1f),
            new Color4(0.58f, 0.58f, 0.60f, 1f),
            new Color4(0.30f, 0.85f, 0.38f, 1f),
            new Color4(1f, 0.90f, 0.22f, 1f),
            new Color4(1f, 0.36f, 0.08f, 1f),
            new Color4(0.55f, 0.33f, 0.14f, 1f),
            new Color4(0.96f, 0.96f, 0.98f, 1f),
            new Color4(0.12f, 0.18f, 0.55f, 1f),
            new Color4(0.75f, 0.85f, 1f, 1f),
            new Color4(0.2f, 0.16f, 0.3f, 1f),
            new Color4(0.08f, 0.32f, 0.16f, 1f), // (the dark green of Wakasagihime's seaweed)
        };

        public const int TINT_RED = 1;
        public const int TINT_BLUE = 2;
        public const int TINT_PURPLE = 3;
        public const int TINT_ORANGE = 4; // (light orange: the bullets thrown by Hecatia's worlds when they hit something)
        public const int TINT_PINK = 5; // (sakura pink: Yuyuko's petals)
        public const int TINT_GRAY = 6; // (Zanmu's ripple has stopped it)
        public const int TINT_GREEN = 7; // (where Sakuya's mirroring is going to put a bullet)
        public const int TINT_YELLOW = 8; // (Patchouli's fire when it is about to go out)
        public const int TINT_BROWN = 10; // (Patchouli's stones)
        public const int TINT_FIRE = 9; // (an orange-red: what Patchouli's fire has touched)
        public const int TINT_WHITE = 11; // (Sagume's replayed copies)
        public const int TINT_DARK_BLUE = 12; // (what Shion has turned round)
        public const int TINT_LIGHTNING = 13; // (Tojiko's bolts, and what they throw back)
        public const int TINT_HISAMI_HAIR = 14; // (black with a violet sheen: the copies Hisami records)
        public const int TINT_SEAWEED = 15; // (the dark green of Wakasagihime's seaweed: the petals on it, Yuuka's leaves)

        // layout of the sheet: one row per palette colour, a disc cell, an arrowhead cell and a star cell per row, with gaps so mipmaps never mix cells.
        private const int max_styles = 40;
        private const int cell_gap = 8;
        private const int circle_cell = 128; // (as big as the other cells: round bullets and ellipses stay sharp up to about twice a plain bullet's size; bigger round ones are drawn from a big picture by the field)
        private const int triangle_cell = 128;
        private const int row_height = triangle_cell + cell_gap * 2;
        private const int circle_x = cell_gap;
        private const int triangle_x = circle_x + circle_cell + cell_gap * 2;
        private const int star_x = triangle_x + triangle_cell + cell_gap * 2;
        private const int soft_star_x = star_x + triangle_cell + cell_gap * 2;
        private const int heart_x = soft_star_x + SoftStar.CELL + cell_gap * 2;
        private const int petal_x = heart_x + triangle_cell + cell_gap * 2;
        private const int slice_x = petal_x + triangle_cell + cell_gap * 2;
        private const int maple_x = slice_x + triangle_cell + cell_gap * 2;
        private const int sheet_width = maple_x + triangle_cell + cell_gap;

        public BulletRenderer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            this.renderer = renderer;
            Texture = createTexture(renderer, false); // the disc that joins the pieces of a laser beam
            knifeTexture = BossCharacter.LoadResource(renderer, "Textures.knife.png");
            knifeRedTexture = BossCharacter.LoadResource(renderer, "Textures.knife_red.png");
            knifeBlueTexture = blueKnife(renderer);
            haloTexture = createTexture(renderer, true); // (the soft light of the bloom)
            trainTexture = BossCharacter.LoadResource(renderer, "Textures.train.png");
            snowflakeTexture = BossCharacter.LoadResource(renderer, "Textures.snowflake.png");
            smileTexture = BossCharacter.LoadResource(renderer, "Textures.smile.png");
            skullTexture = BossCharacter.LoadResource(renderer, "Textures.skull_decal.png");
            talismanBack = BossCharacter.LoadResource(renderer, "Textures.talisman_back.png");
            talismanLines = BossCharacter.LoadResource(renderer, "Textures.talisman_lines.png");
            cloudTexture = StageLayer.BakeCloud(renderer);
            flashTexture = StageLayer.BakeGlow(renderer);

            var fallback = new Color4(1f, 0.35f, 0.5f, 1f);
            SetPalette([fallback], [DanmakusuBalance.BorderColour(fallback)]);
        }

        /// <summary>
        /// Renders the bullet sprites for a palette (body colours and matching border colours) into one texture:
        /// border, body and glow are composited here once, so drawing a bullet is a single quad.
        /// </summary>
        public void SetPalette(Color4[] bodies, Color4[] borders)
        {
            if (renderer == null || bodies.Length == 0)
                return;

            int baseCount = Math.Min(bodies.Length, max_styles - SpecialBodies.Length);
            int styles = baseCount + SpecialBodies.Length;
            var image = new Image<Rgba32>(sheet_width, styles * row_height); // starts fully transparent

            ensureFields();

            for (int style = 0; style < styles; style++)
            {
                Color4 body = style < baseCount ? bodies[style] : SpecialBodies[style - baseCount];
                Color4 border = style < baseCount ? borders[Math.Min(style, borders.Length - 1)] : DanmakusuBalance.BorderColour(body);
                Color4 glow = DanmakusuBalance.IsWhiteBullet(body) ? DanmakusuBalance.WHITE_BULLET_CENTRE_COLOUR : Color4.White;
                int top = style * row_height + cell_gap;

                // disc: the border covers the whole cell, the body and the glow are body_scale as big (the same as three stacked quads).
                for (int y = 0; y < circle_cell; y++)
                {
                    for (int x = 0; x < circle_cell; x++)
                    {
                        float dx = (x + 0.5f - circle_cell / 2f) / (circle_cell / 2f);
                        float dy = (y + 0.5f - circle_cell / 2f) / (circle_cell / 2f);
                        float d = MathF.Sqrt(dx * dx + dy * dy);
                        float inner = d / body_scale;

                        float borderAlpha = Math.Clamp((1f - d) / 0.08f, 0f, 1f);
                        float bodyAlpha = Math.Clamp((1f - inner) / 0.08f, 0f, 1f);
                        float glowAlpha = bodyAlpha * MathF.Pow(Math.Clamp(1f - inner, 0f, 1f), 1.4f);

                        image[circle_x + x, top + y] = composite(border, borderAlpha, body, bodyAlpha, glow, glowAlpha);
                    }
                }

                // arrowhead and star, from the signed distances that are computed once
                bakePolygon(image, arrowheadField!, triangle_x, top, border, body, glow);
                bakePolygon(image, starField!, star_x, top, border, body, glow);
                bakePolygon(image, heartField!, heart_x, top, border, body, glow);
                bakePolygon(image, petalField!, petal_x, top, border, body, glow);
                bakePolygon(image, sliceField!, slice_x, top, border, body, glow);
                bakePolygon(image, mapleField!, maple_x, top, border, body, glow);
                SoftStar.Bake(image, soft_star_x, top, body);
            }

            var softImage = makeSoft(image, styles);

            if (sheet == null || sheetStyles != styles)
                sheet = renderer.CreateTexture(sheet_width, styles * row_height);

            if (softSheet == null || sheetStyles != styles)
                softSheet = renderer.CreateTexture(sheet_width, styles * row_height);

            softSheet.SetData(new TextureUpload(softImage));

            sheetStyles = styles;
            sheetBase = baseCount;
            bodyColours = bodies;
            sheet.SetData(new TextureUpload(image));
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>The knife with its green handle turned blue (null if the picture is missing).</summary>
        private static Texture? blueKnife(IRenderer renderer)
        {
            using var stream = typeof(BulletRenderer).Assembly.GetManifestResourceStream("osu.Game.Rulesets.Danmakusu.Resources.Textures.knife.png");

            if (stream == null)
                return null;

            // (not disposed here: the upload still needs the picture on the draw thread, and it disposes it itself afterwards)
            var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image[x, y];

                    if (pixel.G > pixel.R + 12 && pixel.G > pixel.B + 12)
                        image[x, y] = new Rgba32(pixel.R, pixel.B, pixel.G, pixel.A);
                }
            }

            var texture = renderer.CreateTexture(image.Width, image.Height);
            texture.SetData(new TextureUpload(image));
            return texture;
        }

        /// <summary>How much a soft cell is blurred, as a part of the cell's size (the picture in it is half the cell).</summary>
        private const float soft_blur = 0.06f;

        /// <summary>
        /// The soft sheet: every cell of every row gets its picture shrunk to half its size in the middle of the cell, then blurred, so that the blur has room to fade out to nothing inside the cell. A bullet drawn
        /// from it twice as big is as big as the bullet, but with no edge to be seen.
        /// </summary>
        private static Image<Rgba32> makeSoft(Image<Rgba32> sharp, int styles)
        {
            var soft = new Image<Rgba32>(sharp.Width, sharp.Height);
            var cells = new (int X, int Size)[] { (circle_x, circle_cell), (triangle_x, triangle_cell), (star_x, triangle_cell), (soft_star_x, SoftStar.CELL), (heart_x, triangle_cell), (petal_x, triangle_cell), (slice_x, triangle_cell), (maple_x, triangle_cell) };

            for (int style = 0; style < styles; style++)
            {
                int top = style * row_height + cell_gap;

                foreach (var (x0, size) in cells)
                {
                    using var piece = sharp.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(x0, top, size, size)).Resize(size / 2, size / 2));
                    using var canvas = new Image<Rgba32>(size, size);
                    canvas.Mutate(ctx => ctx.DrawImage(piece, new SixLabors.ImageSharp.Point(size / 4, size / 4), 1f).GaussianBlur(size * soft_blur));

                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            soft[x0 + x, top + y] = canvas[x, y];
                }
            }

            return soft;
        }

        /// <summary>Source-over of border, body and glow, returned as a straight (non-premultiplied) colour.</summary>
        private static Rgba32 composite(Color4 border, float borderAlpha, Color4 body, float bodyAlpha, Color4 glow, float glowAlpha)
        {
            float a = borderAlpha;
            float r = border.R * borderAlpha, g = border.G * borderAlpha, b = border.B * borderAlpha;

            r = body.R * bodyAlpha + r * (1 - bodyAlpha);
            g = body.G * bodyAlpha + g * (1 - bodyAlpha);
            b = body.B * bodyAlpha + b * (1 - bodyAlpha);
            a = bodyAlpha + a * (1 - bodyAlpha);

            r = glow.R * glowAlpha + r * (1 - glowAlpha);
            g = glow.G * glowAlpha + g * (1 - glowAlpha);
            b = glow.B * glowAlpha + b * (1 - glowAlpha);
            a = glowAlpha + a * (1 - glowAlpha);

            if (a < 1e-4f)
                return new Rgba32(toByte(border.R), toByte(border.G), toByte(border.B), 0);

            return new Rgba32(toByte(r / a), toByte(g / a), toByte(b / a), toByte(a));
        }

        private static byte toByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

        /// <summary>Signed distance to a polygon for every pixel of its cell, and the same at 1/body_scale (the body is a smaller copy of the shape).</summary>
        private sealed class PolygonField
        {
            public float[] Outer = [];
            public float[] Inner = [];
            public float Depth = 1f;
        }

        private static PolygonField? arrowheadField;
        private static PolygonField? starField;
        private static PolygonField? heartField;
        private static PolygonField? petalField;
        private static PolygonField? sliceField;
        private static PolygonField? mapleField;

        private static void ensureFields()
        {
            arrowheadField ??= createField(DanmakusuBalance.ARROWHEAD);
            starField ??= createField(DanmakusuBalance.STAR);
            heartField ??= createField(DanmakusuBalance.HEART);
            petalField ??= createField(DanmakusuBalance.PETAL);
            sliceField ??= createField(DanmakusuBalance.SLICE);
            mapleField ??= createField(DanmakusuBalance.MAPLE);
        }

        private static PolygonField createField(Vector2[] outline)
        {
            var outer = new float[triangle_cell * triangle_cell];
            var inner = new float[triangle_cell * triangle_cell];
            float deepest = 0;

            for (int y = 0; y < triangle_cell; y++)
            {
                for (int x = 0; x < triangle_cell; x++)
                {
                    var p = new Vector2((x + 0.5f - triangle_cell / 2f) / (triangle_cell / 2f), (y + 0.5f - triangle_cell / 2f) / (triangle_cell / 2f));

                    outer[y * triangle_cell + x] = PolygonShape.SignedDistance(p, outline);
                    inner[y * triangle_cell + x] = PolygonShape.SignedDistance(p / body_scale, outline);
                    deepest = Math.Max(deepest, -outer[y * triangle_cell + x]);
                }
            }

            return new PolygonField { Outer = outer, Inner = inner, Depth = Math.Max(deepest, 1e-4f) };
        }

        private static void bakePolygon(Image<Rgba32> image, PolygonField field, int left, int top, Color4 border, Color4 body, Color4 glow)
        {
            for (int y = 0; y < triangle_cell; y++)
            {
                for (int x = 0; x < triangle_cell; x++)
                {
                    float outer = field.Outer[y * triangle_cell + x];
                    float inner = field.Inner[y * triangle_cell + x];

                    float borderAlpha = Math.Clamp(-outer / 0.06f, 0f, 1f);
                    float bodyAlpha = Math.Clamp(-inner / 0.06f, 0f, 1f);
                    float glowAlpha = bodyAlpha * MathF.Pow(Math.Clamp(-inner / field.Depth, 0f, 1f), 1.4f);

                    image[left + x, top + y] = composite(border, borderAlpha, body, bodyAlpha, glow, glowAlpha);
                }
            }
        }

        private static RectangleF cellRect(int style, BulletShape shape)
        {
            int top = style * row_height + cell_gap;

            return shape switch
            {
                BulletShape.Triangle => new RectangleF(triangle_x, top, triangle_cell, triangle_cell),
                BulletShape.Star => new RectangleF(star_x, top, triangle_cell, triangle_cell),
                BulletShape.SoftStar => new RectangleF(soft_star_x, top, SoftStar.CELL, SoftStar.CELL),
                BulletShape.Heart => new RectangleF(heart_x, top, triangle_cell, triangle_cell),
                BulletShape.Petal => new RectangleF(petal_x, top, triangle_cell, triangle_cell),
                BulletShape.Maple => new RectangleF(maple_x, top, triangle_cell, triangle_cell),
                BulletShape.Slice => new RectangleF(slice_x, top, triangle_cell, triangle_cell),
                _ => new RectangleF(circle_x, top, circle_cell, circle_cell),
            };
        }

        /// <summary>Hands the renderer this frame's bullets. The array is copied when the draw node is updated.</summary>
        public void SetInstances(BulletInstance[] buffer, int bulletCount)
        {
            instances = buffer;
            count = bulletCount;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>Hands the renderer this frame's lasers (a shared buffer of centre line points, and one entry per laser).</summary>
        public void SetLasers(Vector2[] points, float[] widths, float[] distances, float[] fades, int pointCount, LaserInstance[] beams, int beamCount)
        {
            laserPoints = points;
            laserWidths = widths;
            laserDistances = distances;
            laserFades = fades;
            laserPointCount = pointCount;
            lasers = beams;
            laserCount = beamCount;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>Hands the renderer this frame's flat shapes (Mima's moon).</summary>
        public void SetMeshes(System.Collections.Generic.List<MeshQuad> pieces)
        {
            if (meshCount == 0 && pieces.Count == 0)
                return;

            if (meshes.Length < pieces.Count)
                meshes = new MeshQuad[pieces.Count * 2];

            pieces.CopyTo(meshes);
            meshCount = pieces.Count;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>Hands the renderer this frame's clouds.</summary>
        public void SetClouds(System.Collections.Generic.List<CloudSprite> sprites)
        {
            if (cloudCount == 0 && sprites.Count == 0)
                return;

            if (clouds.Length < sprites.Count)
                clouds = new CloudSprite[sprites.Count * 2];

            sprites.CopyTo(clouds);
            cloudCount = sprites.Count;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>How bright the bullets are drawn (1 = as they are); the beams are not affected.</summary>
        public void SetBrightness(float value)
        {
            if (Math.Abs(value - brightness) < 0.002f)
                return;

            brightness = value;
            Invalidate(Invalidation.DrawNode);
        }

        /// <summary>White texture: either a solid disc with a soft edge, or a radial gradient (opaque centre -> transparent edge).</summary>
        private static Texture createTexture(IRenderer renderer, bool gradient)
        {
            const int size = 64;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float d = MathF.Sqrt(dx * dx + dy * dy);

                    float alpha = Math.Clamp((1f - d) / 0.08f, 0f, 1f);

                    if (gradient)
                        alpha *= MathF.Pow(Math.Clamp(1f - d, 0f, 1f), 1.4f);

                    image[x, y] = new Rgba32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            var texture = renderer.CreateTexture(size, size);
            texture.SetData(new TextureUpload(image));
            return texture;
        }

        /// <summary>
        /// The classic Touhou arrowhead (<see cref="DanmakusuBalance.ARROWHEAD"/>), tip pointing along +x, filled solid.
        /// The gradient variant fades from opaque deep inside the shape to transparent at its edge.
        /// </summary>
        public static Texture CreateArrowheadTexture(IRenderer renderer, bool gradient)
        {
            const int size = 128;
            var outline = DanmakusuBalance.ARROWHEAD;

            float[] sdf = new float[size * size];
            float deepest = 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f - size / 2f) / (size / 2f), (y + 0.5f - size / 2f) / (size / 2f));
                    float value = PolygonShape.SignedDistance(p, outline);

                    sdf[y * size + x] = value;
                    deepest = Math.Max(deepest, -value);
                }
            }

            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float value = sdf[y * size + x];
                    float alpha = Math.Clamp(-value / 0.06f, 0f, 1f);

                    if (gradient)
                        alpha *= MathF.Pow(Math.Clamp(-value / deepest, 0f, 1f), 1.4f);

                    image[x, y] = new Rgba32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            var texture = renderer.CreateTexture(size, size);
            texture.SetData(new TextureUpload(image));
            return texture;
        }

        protected override DrawNode CreateDrawNode() => new BulletDrawNode(this);

        private class BulletDrawNode : SpriteDrawNode
        {
            private BulletInstance[] buffer = new BulletInstance[256];
            private int count;
            private Texture? sheet;
            private Texture? softSheet;
            private int sheetStyles;
            private int sheetBase;
            private Texture? knifeTexture, knifeRedTexture, knifeBlueTexture, haloTexture, trainTexture, snowflakeTexture, talismanBack, talismanLines, smileTexture, skullTexture;
            private Color4[] bodyColours = [];
            private Vector2[] drawPoints = new Vector2[1024];
            private float[] drawWidths = new float[1024];
            private float[] drawDistances = new float[1024];
            private float[] drawFades = new float[1024];
            private float drawBrightness = 1f;
            private Texture? cloudTexture, flashTexture;
            private CloudSprite[] clouds = new CloudSprite[64];
            private int cloudCount;
            private MeshQuad[] meshes = new MeshQuad[64];
            private int meshCount;
            private int pointCount;
            private LaserInstance[] drawLasers = new LaserInstance[16];
            private int laserCount;

            protected new BulletRenderer Source => (BulletRenderer)base.Source;

            public BulletDrawNode(BulletRenderer source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                count = Source.count;
                sheet = Source.sheet;
                softSheet = Source.softSheet;
                sheetStyles = Source.sheetStyles;
                sheetBase = Source.sheetBase;
                knifeTexture = Source.knifeTexture;
                knifeRedTexture = Source.knifeRedTexture;
                knifeBlueTexture = Source.knifeBlueTexture;
                haloTexture = Source.haloTexture;
                trainTexture = Source.trainTexture;
                snowflakeTexture = Source.snowflakeTexture;
                talismanBack = Source.talismanBack;
                smileTexture = Source.smileTexture;
                skullTexture = Source.skullTexture;
                talismanLines = Source.talismanLines;
                bodyColours = Source.bodyColours;

                if (buffer.Length < count)
                    buffer = new BulletInstance[Source.instances.Length];

                Array.Copy(Source.instances, buffer, count);

                pointCount = Source.laserPointCount;
                laserCount = Source.laserCount;

                if (drawPoints.Length < pointCount)
                {
                    drawPoints = new Vector2[Source.laserPoints.Length];
                    drawWidths = new float[Source.laserPoints.Length];
                    drawDistances = new float[Source.laserPoints.Length];
                    drawFades = new float[Source.laserPoints.Length];
                }

                drawBrightness = Source.brightness;
                cloudTexture = Source.cloudTexture;
                flashTexture = Source.flashTexture;
                cloudCount = Source.cloudCount;

                if (clouds.Length < cloudCount)
                    clouds = new CloudSprite[Source.clouds.Length];

                Array.Copy(Source.clouds, clouds, cloudCount);

                meshCount = Source.meshCount;

                if (meshes.Length < meshCount)
                    meshes = new MeshQuad[Source.meshes.Length];

                Array.Copy(Source.meshes, meshes, meshCount);

                if (drawLasers.Length < laserCount)
                    drawLasers = new LaserInstance[Source.lasers.Length];

                Array.Copy(Source.laserPoints, drawPoints, pointCount);
                Array.Copy(Source.laserWidths, drawWidths, pointCount);
                Array.Copy(Source.laserDistances, drawDistances, pointCount);
                Array.Copy(Source.laserFades, drawFades, pointCount);
                Array.Copy(Source.lasers, drawLasers, laserCount);
            }

            protected override void Blit(IRenderer renderer)
            {
                if (Texture == null || sheet == null || sheetStyles == 0)
                    return;

                // (the bullets can be dimmed while a big beam fires; the beams themselves are not)
                var pixel = renderer.WhitePixel;
                var pixelCoords = new RectangleF(0, 0, pixel.DisplayWidth, pixel.DisplayHeight);

                // the wood of a tree first, layer by layer over all its branches (one piece, no seams where they meet)
                bool anyWood = false;

                for (int beam = 0; beam < laserCount && !anyWood; beam++)
                    anyWood = drawLasers[beam].Wood;

                if (anyWood)
                {
                    // (wood that blooms - Patchouli's trees - has its light under it, round every branch)
                    for (int beam = 0; beam < laserCount; beam++)
                    {
                        if (drawLasers[beam].Wood && drawLasers[beam].Bloom > 0.002f)
                            drawBeamBloom(renderer, drawLasers[beam], pixel, pixelCoords);
                    }

                    for (int layer = 0; layer < 3; layer++)
                    {
                        float scale = layer == 0 ? 1f : layer == 1 ? body_scale : laser_core_scale;

                        for (int beam = 0; beam < laserCount; beam++)
                        {
                            var wood = drawLasers[beam];

                            if (!wood.Wood)
                                continue;

                            Color4 colour = layer == 0 ? wood.BorderColour : layer == 1 ? wood.Colour : wood.CoreColour;
                            drawBeamLayer(renderer, wood, scale, 0f, colour, pixel, pixelCoords);
                        }
                    }
                }

                // all laser beams are drawn first, so every bullet is above every beam (the bullets stay visible on top of them)
                for (int beam = 0; beam < laserCount; beam++)
                {
                    if (drawLasers[beam].Bloom > 0.002f && !drawLasers[beam].Wood)
                        drawBeamBloom(renderer, drawLasers[beam], pixel, pixelCoords);
                }

                for (int beam = 0; beam < laserCount; beam++)
                {
                    if (drawLasers[beam].Wood)
                        continue;

                    int piece = drawLasers[beam].Piece;

                    if (piece <= 0)
                    {
                        drawBeam(renderer, drawLasers[beam], pixel, pixelCoords);
                        continue;
                    }

                    // (the beams of one piece, one after the other: layer by layer together)
                    int last = beam;

                    while (last + 1 < laserCount && drawLasers[last + 1].Piece == piece)
                        last++;

                    for (int layer = 0; layer < 3; layer++)
                    {
                        for (int j = beam; j <= last; j++)
                            drawBeam(renderer, drawLasers[j], pixel, pixelCoords, layer);
                    }

                    beam = last;
                }

                // (the flat shapes of the stages, over the beams and under the bullets: Mima's moon)
                for (int i = 0; i < meshCount; i++)
                {
                    var piece = meshes[i];
                    var corners = new Quad(Vector2Extensions.Transform(piece.A, DrawInfo.Matrix), Vector2Extensions.Transform(piece.B, DrawInfo.Matrix), Vector2Extensions.Transform(piece.C, DrawInfo.Matrix),
                        Vector2Extensions.Transform(piece.D, DrawInfo.Matrix));

                    renderer.DrawQuad(pixel, corners, ColourInfo.GradientVertical(piece.Near, piece.Far), textureCoords: pixelCoords);
                }

                // (Tojiko's clouds are over her lightning and the rain that falls out of them, and under every other bullet)
                if (cloudCount > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (buffer[i].UnderClouds)
                            drawBullet(renderer, buffer[i]);
                    }

                    drawClouds(renderer);
                }

                int bullet = 0;

                while (bullet < count)
                {
                    var b = buffer[bullet++];

                    if (b.Shape != BulletShape.Train && !(b.UnderClouds && cloudCount > 0)) // (the trains are drawn after all the others: they pass over every bullet)
                        drawBullet(renderer, b);
                }

                for (int i = 0; i < count; i++)
                {
                    var train = buffer[i];

                    if (train.Shape == BulletShape.Train && train.Fade < 0.999f)
                        drawPicture(renderer, train, drawBrightness * (1f - train.Shade), 1f - train.Fade);
                }

                drawSmiles(renderer);
                drawTalismanMarks(renderer);
            }

            private void drawBullet(IRenderer renderer, in BulletInstance b)
            {
                // (the black hole's stretch, for every quad of this bullet)
                lensStretch = b.LensStretch;
                lensAxis = b.LensAxis;
                lensCentre = b.LensCentre;

                drawBloom(renderer, b, false);
                drawBody(renderer, b);
                drawBloom(renderer, b, true);

                lensStretch = 1f;
            }

            private void drawClouds(IRenderer renderer)
            {
                for (int i = 0; i < cloudCount; i++)
                {
                    var cloud = clouds[i];
                    Texture? texture = cloud.Flash ? flashTexture : cloudTexture;

                    if (texture == null)
                        continue;

                    renderer.SetBlend(cloud.Additive ? BlendingParameters.Additive : DrawColourInfo.Blending);
                    drawShape(renderer, texture, new RectangleF(0, 0, texture.DisplayWidth, texture.DisplayHeight), cloud.Position, new Vector2(1, 0), cloud.Size.X * 0.5f, cloud.Size.Y * 0.5f, ColourInfo.SingleColour(cloud.Colour));
                }

                renderer.SetBlend(DrawColourInfo.Blending);
            }

            /// <summary>The bloom of a bullet (Remilia's attack): a soft light of its colour spreads round it (behind it), and a faint white one over it.</summary>
            private void drawBloom(IRenderer renderer, BulletInstance b, bool front)
            {
                if (b.Bloom <= 0.002f || haloTexture == null || b.Fade >= 0.999f)
                    return;

                int own = b.Style % Math.Max(sheetBase, 1);
                Color4 body = own >= 0 && own < bodyColours.Length ? bodyColours[own] : new Color4(1f, 0.35f, 0.5f, 1f);

                // (a bullet that is shown in a colour over its own glows in that colour, and its halo is of that colour's picture: in its own the halo was a thick outline of the old colour round it -
                // Wriggle's green fireflies in rings of grey and orange, Cirno's blue ones)
                if (b.Overlay > 0 && b.OverlayAmount > 0f)
                {
                    Color4 tint = SpecialBodies[Math.Min(b.Overlay, SpecialBodies.Length) - 1];
                    float amount = Math.Min(b.OverlayAmount, 1f);
                    body = new Color4(body.R + (tint.R - body.R) * amount, body.G + (tint.G - body.G) * amount, body.B + (tint.B - body.B) * amount, 1f);

                    if (amount >= 0.5f)
                        own = Math.Min(sheetBase + b.Overlay - 1, sheetStyles - 1);
                }

                float alpha = (1f - b.Fade) * b.Bloom;
                float reach = b.Shape == BulletShape.Knife ? b.Radius * DanmakusuBalance.KNIFE_LENGTH_FACTOR * (b.Stretch > 1f ? b.Stretch : 1f) : b.Radius;
                var coords = new RectangleF(0, 0, haloTexture.DisplayWidth, haloTexture.DisplayHeight);

                float size = reach * (front ? 0.4f : 0.8f);
                var light = new Color4(body.R * 0.65f + 0.35f, body.G * 0.65f + 0.35f, body.B * 0.65f + 0.35f, 1f);
                var colour = front ? ColourInfo.SingleColour(new Color4(1f, 1f, 1f, alpha * 0.7f)) : ColourInfo.SingleColour(new Color4(light.R, light.G, light.B, alpha * 0.6f));

                if (b.Shape != BulletShape.Knife)
                {
                    if (front || sheet == null || b.Shape is BulletShape.Flower or BulletShape.Talisman or BulletShape.Train)
                    {
                        drawShape(renderer, haloTexture, coords, b.Position, new Vector2(1, 0), size, size, colour);
                        return;
                    }

                    // (the light round it is the bullet itself, a little bigger every time and fainter: the halo has the bullet's shape)
                    float shapeAlong = b.Radius, shapeAcross = b.Shape == BulletShape.Ellipse ? b.Radius * DanmakusuBalance.ELLIPSE_ASPECT : b.Radius;

                    if (b.Shape == BulletShape.SoftStar)
                    {
                        shapeAlong /= SoftStar.StarFraction;
                        shapeAcross /= SoftStar.StarFraction;
                    }

                    for (int layer = 0; layer < 3; layer++)
                    {
                        float grow = 1f + 0.25f * (layer + 1);
                        var layerColour = ColourInfo.SingleColour(new Color4(light.R, light.G, light.B, alpha * 0.6f / (layer + 1.5f)));

                        bool softHalo = b.Soft > 0.01f && softSheet != null;
                        drawShape(renderer, softHalo ? softSheet! : sheet, cellRect(own, b.Shape), b.Position, b.Direction, shapeAlong * grow * (softHalo ? 2f : 1f), shapeAcross * grow * (softHalo ? 2f : 1f), layerColour);
                    }

                    return;
                }

                // a knife's halo is the shape of the knife (one for each of its blades)
                Texture? picture = b.BlueHandle && knifeBlueTexture != null ? knifeBlueTexture : b.RedHandle && knifeRedTexture != null ? knifeRedTexture : knifeTexture;
                var pictureCoords = picture == null ? default : new RectangleF(0, 0, picture.DisplayWidth, picture.DisplayHeight);
                float half = b.Radius * DanmakusuBalance.KNIFE_LENGTH_FACTOR;
                int blades = Math.Max(b.Blades, 1);

                for (int blade = 0; blade < blades; blade++)
                {
                    Vector2 bladeDirection = blades > 1 ? BulletCollision.BladeDirection(blades, blade, b.Direction, b.BladeAngle) : b.Direction;
                    Vector2 bladeCentre = blades > 1 ? b.Position + BulletCollision.BladeOffset(blades, blade, b.Direction, half, b.BladeAngle) : b.Position;

                    if (front || picture == null)
                    {
                        drawShape(renderer, haloTexture, coords, bladeCentre, bladeDirection, size, size * DanmakusuBalance.KNIFE_HALO_ASPECT, colour);
                        continue;
                    }

                    // (the light round it is the knife itself, a little bigger every time and fainter: the halo has the knife's shape)
                    float bladeAcross = bladeDirection.X < 0f ? -half * DanmakusuBalance.KNIFE_ASPECT : half * DanmakusuBalance.KNIFE_ASPECT;

                    for (int layer = 0; layer < 3; layer++)
                    {
                        float grow = 1f + 0.12f * (layer + 1);
                        float widen = 1f + 0.9f * (layer + 1);
                        var layerColour = ColourInfo.SingleColour(new Color4(light.R, light.G, light.B, alpha * 0.6f / (layer + 1.5f)));

                        drawShape(renderer, picture, pictureCoords, bladeCentre, bladeDirection, half * grow, bladeAcross * widen, layerColour);
                    }
                }
            }

            /// <summary>One bullet: its body in its shape and colour (a talisman is faded into the shape it is turning into or out of).</summary>
            private void drawBody(IRenderer renderer, BulletInstance b)
            {
                // half extents along / across the direction of travel
                float along = b.Radius;
                float across = b.Shape == BulletShape.Ellipse ? b.Radius * DanmakusuBalance.ELLIPSE_ASPECT : b.Radius;

                // (spaghettified: long along its way, thin across it)
                if (b.Elongate > 1f)
                {
                    along *= b.Elongate;
                    across /= MathF.Sqrt(b.Elongate);
                }

                if (b.Shape == BulletShape.SoftStar)
                {
                    // the baked cell has room for the glow round the star: draw it bigger so the star itself keeps the bullet's size
                    along /= SoftStar.StarFraction;
                    across /= SoftStar.StarFraction;
                }

                if (b.Fade >= 0.999f)
                    return;

                float shade = 1f - b.Shade;
                float alpha = 1f - b.Fade;
                // (the stars of spinners and bosses are not dimmed by the beams)
                float bright = (b.Shape == BulletShape.SoftStar || b.Undimmed ? 1f : drawBrightness) * shade;
                int own = b.Style % Math.Max(sheetBase, 1);

                if (b.Shape == BulletShape.Flower)
                {
                    drawFlower(renderer, b, bright, alpha);
                    return;
                }

                if (b.Shape == BulletShape.Talisman && talismanBack != null && talismanLines != null)
                {
                    if (b.Morph < 0.999f && b.Under != BulletShape.Talisman)
                    {
                        // still (or already again) partly the bullet: its own shape fades out as the talisman fades in
                        var under = b;
                        under.Shape = b.Under;
                        under.Fade = 1f - (1f - b.Fade) * (1f - b.Morph);
                        drawBody(renderer, under);
                    }

                    if (b.Morph > 0.01f)
                        drawTalisman(renderer, b, bright, alpha * Math.Min(1f, b.Morph * 1.5f), own);

                    return;
                }

                if (b.Shape is BulletShape.Knife or BulletShape.Train)
                {
                    drawPicture(renderer, b, bright, alpha);
                    return;
                }

                if (b.Overlay <= 0 || b.OverlayAmount <= 0f)
                {
                    drawCell(renderer, b, own, along, across, bright, alpha);
                    drawDecal(renderer, b, bright, alpha);
                    return;
                }

                float amount = Math.Min(b.OverlayAmount, 1f);
                int overlayRow = Math.Min(sheetBase + b.Overlay - 1, sheetStyles - 1);

                // (the own colour underneath, the tint over it: shows the tint by the given amount)
                if (amount < 1f)
                    drawCell(renderer, b, own, along, across, bright, alpha);

                drawCell(renderer, b, overlayRow, along, across, bright, alpha * amount);
                drawDecal(renderer, b, bright, alpha);
            }

            /// <summary>One cell of the sheet for a bullet: sharp, or soft (from the soft sheet, twice as big: the same size with an edge that fades into nothing), or the two faded into each other.</summary>
            private void drawCell(IRenderer renderer, BulletInstance b, int row, float along, float across, float bright, float alpha)
            {
                float soft = softSheet == null ? 0f : Math.Clamp(b.Soft, 0f, 1f);

                if (soft < 0.999f)
                    drawShape(renderer, sheet!, cellRect(row, b.Shape), b.Position, b.Direction, along, across, ColourInfo.SingleColour(new Color4(bright, bright, bright, alpha * (1f - soft))));

                if (soft > 0.001f)
                    drawShape(renderer, softSheet!, cellRect(row, b.Shape), b.Position, b.Direction, along * 2f, across * 2f, ColourInfo.SingleColour(new Color4(bright, bright, bright, alpha * soft)));
            }

            /// <summary>A sakura flower: five petals (the petal shape, tips at the middle) in a star round a darker middle, the star turning with the direction of travel. <c>Radius</c> is the radius of the whole flower.</summary>
            private void drawFlower(IRenderer renderer, BulletInstance b, float bright, float alpha)
            {
                int row = b.Overlay > 0 ? Math.Min(sheetBase + b.Overlay - 1, sheetStyles - 1) : b.Style % Math.Max(sheetBase, 1);
                float petal = b.Radius / (2f * DanmakusuBalance.YUYUKO_PETAL_REACH + 1f); // (the half length of one petal: the same layout as the flowers of the tree)
                var colour = ColourInfo.SingleColour(new Color4(bright, bright, bright, alpha));
                float start = MathF.Atan2(b.Direction.Y, b.Direction.X);

                for (int k = 0; k < DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER; k++)
                {
                    float angle = start + k * MathF.Tau / DanmakusuBalance.YUYUKO_PETALS_PER_FLOWER;
                    var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    drawShape(renderer, sheet!, cellRect(row, BulletShape.Petal), b.Position + outward * (2f * DanmakusuBalance.YUYUKO_PETAL_REACH * petal), -outward, petal, petal, colour);
                }

                var middle = ColourInfo.SingleColour(new Color4(bright * 0.85f, bright * 0.35f, bright * 0.55f, alpha));

                drawShape(renderer, sheet!, cellRect(row, BulletShape.Circle), b.Position, new Vector2(1, 0), petal * 0.5f, petal * 0.5f, middle);
            }

            private static float insideOf(Vector2[] polygon)
            {
                float best = float.MaxValue;

                for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                {
                    Vector2 a = polygon[j], c = polygon[i], ab = c - a;
                    float lengthSquared = ab.LengthSquared;
                    float t = lengthSquared < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(-a, ab) / lengthSquared, 0f, 1f);
                    best = Math.Min(best, (a + ab * t).Length);
                }

                return best;
            }

            private static readonly float arrowheadInside = insideOf(DanmakusuBalance.ARROWHEAD);
            private static readonly float starInside = insideOf(DanmakusuBalance.STAR);
            private static readonly float heartInside = insideOf(DanmakusuBalance.HEART);
            private static readonly float petalInside = insideOf(DanmakusuBalance.PETAL);
            private static readonly float mapleInside = insideOf(DanmakusuBalance.MAPLE);
            private static readonly float sliceInside = insideOf(DanmakusuBalance.SLICE);

            /// <summary>The radius of the biggest disc that fits into a bullet's body, as a fraction of the body of a round bullet (the shapes are smaller than their cell).</summary>
            private static float bodyInside(BulletShape shape) => shape switch
            {
                BulletShape.Circle => 1f,
                BulletShape.Ellipse => DanmakusuBalance.ELLIPSE_ASPECT,
                BulletShape.Triangle => arrowheadInside,
                BulletShape.Star or BulletShape.SoftStar => starInside,
                BulletShape.Heart => heartInside,
                BulletShape.Petal => petalInside,
                BulletShape.Maple => mapleInside,
                BulletShape.Slice => sliceInside,
                _ => 0.4f,
            };

            /// <summary>The smiling faces, over every bullet that has one (upright whatever way the bullet spins or flies; the picture fits inside the bullet's body).</summary>
            private void drawSmiles(IRenderer renderer)
            {
                if (smileTexture == null)
                    return;

                var coords = new RectangleF(0, 0, smileTexture.DisplayWidth, smileTexture.DisplayHeight);

                for (int i = 0; i < count; i++)
                {
                    var b = buffer[i];

                    if (!b.Smile || b.Fade >= 0.999f)
                        continue;

                    // the face has to fit inside the body: the biggest disc that fits into the body's shape (the shapes are smaller than their cell), a little smaller still
                    float inside = bodyInside(b.Shape);

                    float half = b.Radius * body_scale * inside * 0.92f / 0.96f; // (the picture's content reaches 0.96 of its half size)

                    // the face is not dimmed like the bullet under it: full brightness, and only as transparent as the bullet is at its clearest
                    float seen = Math.Clamp((1f - b.Fade) / DanmakusuBalance.KOISHI_REVEAL_VISIBILITY, 0f, 1f);
                    drawShape(renderer, smileTexture, coords, b.Position, new Vector2(1, 0), half, half, ColourInfo.SingleColour(new Color4(1f, 1f, 1f, seen)));
                }
            }

            /// <summary>The talismans over the bullets that have one (paper with red writing, the whole of it inside the body, facing the way the bullet's act says).</summary>
            private void drawTalismanMarks(IRenderer renderer)
            {
                if (talismanBack == null || talismanLines == null)
                    return;

                var back = new RectangleF(0, 0, talismanBack.DisplayWidth, talismanBack.DisplayHeight);
                var lines = new RectangleF(0, 0, talismanLines.DisplayWidth, talismanLines.DisplayHeight);
                float corner = MathF.Sqrt(1f + DanmakusuBalance.TALISMAN_ASPECT * DanmakusuBalance.TALISMAN_ASPECT);

                for (int i = 0; i < count; i++)
                {
                    var b = buffer[i];

                    if (!b.Talisman || b.Fade >= 0.999f)
                        continue;

                    // the corners of the paper touch the biggest disc that fits into the body (a little smaller)
                    float inside = bodyInside(b.Shape);
                    float along = b.Radius * body_scale * inside * DanmakusuBalance.KAKU_TALISMAN_FIT / corner;
                    float across = along * DanmakusuBalance.TALISMAN_ASPECT;
                    Vector2 face = b.TalismanFace.LengthSquared > 1e-6f ? b.TalismanFace : new Vector2(1, 0);
                    float alpha = 1f - b.Fade;

                    drawShape(renderer, talismanBack, back, b.Position, face, along, across, ColourInfo.SingleColour(new Color4(0.93f, 0.9f, 0.76f, alpha)));
                    drawShape(renderer, talismanLines, lines, b.Position, face, along, across, ColourInfo.SingleColour(new Color4(0.62f, 0.1f, 0.12f, alpha)));
                }
            }

            /// <summary>A talisman: the body in the colour the bullet has (or the colour shown over it), the white border and writing over it.</summary>
            private void drawTalisman(IRenderer renderer, BulletInstance b, float bright, float alpha, int own)
            {
                float along = b.Radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * (0.55f + 0.45f * Math.Clamp(b.Morph, 0f, 1f)); // (it grows into its size)
                float across = along * DanmakusuBalance.TALISMAN_ASPECT; // (never turned over: a talisman that turns on its way turns smoothly, its picture is never flipped)

                Color4 body = own >= 0 && own < bodyColours.Length ? bodyColours[own] : new Color4(1f, 0.35f, 0.5f, 1f);

                if (b.Overlay > 0 && b.OverlayAmount > 0f)
                {
                    Color4 tint = SpecialBodies[Math.Min(b.Overlay, SpecialBodies.Length) - 1];
                    float amount = Math.Min(b.OverlayAmount, 1f);
                    body = new Color4(body.R + (tint.R - body.R) * amount, body.G + (tint.G - body.G) * amount, body.B + (tint.B - body.B) * amount, 1f);
                }

                var back = new RectangleF(0, 0, talismanBack!.DisplayWidth, talismanBack.DisplayHeight);
                var lines = new RectangleF(0, 0, talismanLines!.DisplayWidth, talismanLines.DisplayHeight);

                // (a dark body under bright writing: the picture is dimmer and has more contrast)
                float bodyLevel = bright * DanmakusuBalance.TALISMAN_BODY_LEVEL;
                float lineLevel = bright * DanmakusuBalance.TALISMAN_LINE_LEVEL;

                drawShape(renderer, talismanBack, back, b.Position, b.Direction, along, across, ColourInfo.SingleColour(new Color4(body.R * bodyLevel, body.G * bodyLevel, body.B * bodyLevel, alpha)));
                drawShape(renderer, talismanLines, lines, b.Position, b.Direction, along, across, ColourInfo.SingleColour(new Color4(lineLevel, lineLevel, lineLevel, alpha)));
            }

            /// <summary>A knife or a train carriage: its picture, long side along the way it flies (never upside down), tinted if a colour is shown over it.</summary>
            private void drawPicture(IRenderer renderer, BulletInstance b, float bright, float alpha)
            {
                bool train = b.Shape == BulletShape.Train;
                Texture? picture = train ? trainTexture : b.BlueHandle && knifeBlueTexture != null ? knifeBlueTexture : b.RedHandle && knifeRedTexture != null ? knifeRedTexture : knifeTexture;
                float along = b.Radius * (train ? DanmakusuBalance.TRAIN_LENGTH_FACTOR : DanmakusuBalance.KNIFE_LENGTH_FACTOR * (b.Stretch > 0f ? b.Stretch : 1f));
                float across = along * (train ? DanmakusuBalance.TRAIN_ASPECT : DanmakusuBalance.KNIFE_ASPECT);

                if (b.Direction.X < 0f)
                    across = -across; // (turned over, so the picture stays the right way up)

                Color4 colour = new Color4(bright, bright, bright, alpha);

                if (b.Overlay > 0 && b.OverlayAmount > 0f)
                {
                    Color4 tint = SpecialBodies[Math.Min(b.Overlay, SpecialBodies.Length) - 1];
                    float amount = Math.Min(b.OverlayAmount, 1f);
                    colour = new Color4(bright * (1f + (tint.R - 1f) * amount), bright * (1f + (tint.G - 1f) * amount), bright * (1f + (tint.B - 1f) * amount), alpha);
                }

                if (picture == null)
                {
                    // (the picture is missing: a plain disc)
                    drawShape(renderer, sheet!, cellRect(Math.Max(b.Style, 0) % Math.Max(sheetBase, 1), BulletShape.Circle), b.Position, b.Direction, b.Radius, b.Radius, ColourInfo.SingleColour(colour));
                    return;
                }

                var pictureCoords = new RectangleF(0, 0, picture.DisplayWidth, picture.DisplayHeight);

                if (!train && b.Blades > 1)
                {
                    // a knife with more blades: every blade is a knife of its own, all of them start at the handle end of the first one
                    for (int blade = 0; blade < b.Blades; blade++)
                    {
                        Vector2 bladeDirection = BulletCollision.BladeDirection(b.Blades, blade, b.Direction, b.BladeAngle);
                        Vector2 bladeCentre = b.Position + BulletCollision.BladeOffset(b.Blades, blade, b.Direction, along, b.BladeAngle);
                        float bladeAcross = bladeDirection.X < 0f ? -Math.Abs(across) : Math.Abs(across);

                        drawShape(renderer, picture, pictureCoords, bladeCentre, bladeDirection, along, bladeAcross, ColourInfo.SingleColour(colour));
                    }

                    return;
                }

                if (train && b.Speed > 0f)
                {
                    // motion blur: the train drawn many times along its way, each copy faint (where they all overlap it is solid; its ends are smeared)
                    float blur = b.Speed * DanmakusuBalance.TRAIN_BLUR_MS;
                    int copies = DanmakusuBalance.TRAIN_BLUR_COPIES;
                    var faint = ColourInfo.SingleColour(new Color4(colour.R, colour.G, colour.B, colour.A * DanmakusuBalance.TRAIN_BLUR_ALPHA));

                    for (int copy = 0; copy < copies; copy++)
                        drawShape(renderer, picture, pictureCoords, b.Position + b.Direction * (blur * (copy / (float)(copies - 1) - 0.5f)), b.Direction, along, across, faint);

                    return;
                }

                drawShape(renderer, picture, pictureCoords, b.Position, b.Direction, along, across, ColourInfo.SingleColour(colour));
            }

            /// <summary>A snowflake in the body of a frozen bullet.</summary>
            private void drawDecal(IRenderer renderer, BulletInstance b, float bright, float alpha)
            {
                if (b.Decal == BulletDecal.Skull)
                {
                    if (skullTexture == null || b.DecalFade >= 0.999f)
                        return;

                    // the skull fits into the body: the farthest point of it reaches (almost) the edge of the biggest disc that fits into the body (its own proportions, not squashed)
                    float skullReach = b.Radius * body_scale * bodyInside(b.Shape) * DanmakusuBalance.ZANMU_BULLET_SKULL_SIZE;
                    float skullUnit = skullReach / DanmakusuBalance.ZANMU_SKULL_TIP_PIXELS;
                    var skullColour = new Color4(bright * 0.92f, bright * 0.92f, bright * 0.95f, alpha * DanmakusuBalance.ZANMU_BULLET_SKULL_ALPHA * (1f - b.DecalFade));

                    drawShape(renderer, skullTexture, new RectangleF(0, 0, skullTexture.DisplayWidth, skullTexture.DisplayHeight), b.Position, new Vector2(1, 0), skullUnit * skullTexture.Width / 2f, skullUnit * skullTexture.Height / 2f, ColourInfo.SingleColour(skullColour));
                    return;
                }

                if (b.Decal != BulletDecal.Snowflake || snowflakeTexture == null)
                    return;

                // the snowflake's farthest tip has to stay inside the body: it is scaled so that the tip reaches (almost) the edge of the biggest disc that fits into the body
                float reach = b.Radius * body_scale * bodyInside(b.Shape) * DanmakusuBalance.SNOWFLAKE_SIZE_FACTOR;
                float unit = reach / DanmakusuBalance.SNOWFLAKE_TIP_PIXELS;
                float halfWidth = unit * snowflakeTexture.Width / 2f, halfHeight = unit * snowflakeTexture.Height / 2f; // (its own proportions, not squashed into a square)
                var colour = new Color4(bright * 0.9f, bright * 0.97f, bright, alpha * DanmakusuBalance.SNOWFLAKE_ALPHA);

                drawShape(renderer, snowflakeTexture, new RectangleF(0, 0, snowflakeTexture.DisplayWidth, snowflakeTexture.DisplayHeight), b.Position, new Vector2(1, 0), halfWidth, halfHeight, ColourInfo.SingleColour(colour));
            }

            /// <summary>
            /// A laser beam is drawn layer by layer (border, body, bright core) so the overlapping pieces of the beam do not show.
            /// Each layer is pure geometry: a strip along every piece of the centre line (every point has its own width, so a beam can swell and
            /// thin out), a round cap at both ends and a fan of triangles on the outer side of every bend. The round parts have as many corners
            /// as their size needs, so they stay smooth however thick the beam is, and they meet the strips exactly.
            /// </summary>
            /// <summary>The light round a beam (Remilia's attack): the beam itself, a little wider every time and fainter, behind it: the halo has the beam's shape.</summary>
            private void drawBeamBloom(IRenderer renderer, LaserInstance beam, Texture pixel, RectangleF pixelCoords)
            {
                var light = new Color4(beam.Colour.R * 0.65f + 0.35f, beam.Colour.G * 0.65f + 0.35f, beam.Colour.B * 0.65f + 0.35f, 1f);

                for (int layer = 2; layer >= 0; layer--)
                {
                    float scale = 1f + 0.25f * (layer + 1);
                    var colour = new Color4(light.R, light.G, light.B, beam.Bloom * 0.6f / (layer + 1.5f));

                    drawBeamLayer(renderer, beam, scale, 0f, colour, pixel, pixelCoords);
                }
            }

            /// <summary>A beam, or only one <paramref name="only"/> of its layers (0 the edge, 1 the body, 2 the core).</summary>
            private void drawBeam(IRenderer renderer, LaserInstance beam, Texture pixel, RectangleF pixelCoords, int only = -1)
            {
                for (int layer = 0; layer < 3; layer++)
                {
                    if (only >= 0 && layer != only)
                        continue;

                    // (the white core of a spinner / boss beam is wider: its coloured middle layer is half as thick)
                    float scale = layer == 0 ? 1f : layer == 1 ? body_scale : beam.Glow ? glow_core_scale : laser_core_scale;

                    // (a thin-edged beam: the edge and the coloured layer are half as thick as they are on a spinner / boss beam)
                    if (beam.Thin)
                        scale = layer == 0 ? 1f : layer == 1 ? thin_body_scale : thin_core_scale;

                    Color4 colour = layer == 0 ? beam.BorderColour
                        : layer == 1 ? beam.Colour
                        : beam.CoreColour.A > 0f ? beam.CoreColour
                        : DanmakusuBalance.IsWhiteBullet(beam.Colour) ? DanmakusuBalance.WHITE_BULLET_CENTRE_COLOUR : Color4.White;

                    drawBeamLayer(renderer, beam, scale, 0f, colour, pixel, pixelCoords, layer == 0 && beam.BorderFadeEnd > beam.BorderFadeStart);
                }
            }

            /// <summary>The colour of the dark edge of a spinner / boss beam at point <paramref name="index"/>: the beam's own colour near where it starts, dark further out.</summary>
            private Color4 borderAt(LaserInstance beam, int index)
            {
                float t = Math.Clamp((drawDistances[beam.Start + index] - beam.BorderFadeStart) / (beam.BorderFadeEnd - beam.BorderFadeStart), 0f, 1f);
                t = t * t * (3f - 2f * t);

                return new Color4(
                    beam.Colour.R + (beam.BorderColour.R - beam.Colour.R) * t,
                    beam.Colour.G + (beam.BorderColour.G - beam.Colour.G) * t,
                    beam.Colour.B + (beam.BorderColour.B - beam.Colour.B) * t,
                    1f);
            }

            /// <summary>One layer of a beam: its half width at every point is the point's width times <paramref name="scale"/> plus <paramref name="extra"/>.</summary>
            private void drawBeamLayer(IRenderer renderer, LaserInstance beam, float scale, float extra, Color4 colour, Texture pixel, RectangleF pixelCoords, bool fade = false)
            {
                if (beam.Count <= 0)
                    return;

                // the colour at a point: the layer's (or the fading edge's), as much of it as the beam shows there (a beam in smoke is half see-through, smoothly: the colour changes along every strip)
                Color4 colourAt(int index)
                {
                    Color4 c = fade ? borderAt(beam, index) : colour;
                    float shown = drawFades[beam.Start + index];

                    return shown >= 0.999f ? c : new Color4(c.R, c.G, c.B, c.A * shown);
                }

                bool hasPrevious = false;
                Vector2 previousNormal = Vector2.UnitY;
                Vector2 lastPoint = drawPoints[beam.Start];
                float lastWidth = drawWidths[beam.Start] * scale + extra;
                float previousWidth = lastWidth;
                int lastIndex = 0;

                for (int i = 0; i + 1 < beam.Count; i++)
                {
                    Vector2 p = drawPoints[beam.Start + i];
                    Vector2 q = drawPoints[beam.Start + i + 1];
                    float wp = drawWidths[beam.Start + i] * scale + extra;
                    float wq = drawWidths[beam.Start + i + 1] * scale + extra;
                    Vector2 along = q - p;
                    float length = along.Length;

                    if (length < 1e-4f)
                        continue;

                    Vector2 direction = along / length;
                    Vector2 normal = new Vector2(-direction.Y, direction.X);

                    var quad = new Quad(
                        Vector2Extensions.Transform(p - normal * wp, DrawInfo.Matrix),
                        Vector2Extensions.Transform(q - normal * wq, DrawInfo.Matrix),
                        Vector2Extensions.Transform(p + normal * wp, DrawInfo.Matrix),
                        Vector2Extensions.Transform(q + normal * wq, DrawInfo.Matrix));
                    // (a fading layer changes colour along the beam: one colour at each end of the strip)
                    ColourInfo here = ColourInfo.SingleColour(colourAt(i));
                    renderer.DrawQuad(pixel, quad, ColourInfo.GradientHorizontal(colourAt(i), colourAt(i + 1)), textureCoords: pixelCoords);

                    if (!hasPrevious)
                    {
                        // the round start of the beam: half a turn from one side, round the back, to the other side
                        drawFan(renderer, pixel, pixelCoords, p, normal, MathF.PI, wp, here);
                    }
                    else
                    {
                        // a bend: fill the gap on its outer side
                        float cross = previousNormal.X * normal.Y - previousNormal.Y * normal.X;
                        float dot = Vector2.Dot(previousNormal, normal);
                        float turn = MathF.Atan2(cross, dot);

                        if (MathF.Abs(turn) > 1e-3f)
                            drawFan(renderer, pixel, pixelCoords, p, turn > 0 ? -previousNormal : previousNormal, turn, wp, here);
                    }

                    hasPrevious = true;
                    previousNormal = normal;
                    lastPoint = q;
                    lastWidth = wq;
                    lastIndex = i + 1;
                }

                ColourInfo end = ColourInfo.SingleColour(colourAt(lastIndex));

                if (hasPrevious)
                    drawFan(renderer, pixel, pixelCoords, lastPoint, previousNormal, -MathF.PI, lastWidth, end); // the round end
                else
                {
                    // a beam that is a single point: a disc
                    drawFan(renderer, pixel, pixelCoords, lastPoint, Vector2.UnitY, MathF.PI, previousWidth, end);
                    drawFan(renderer, pixel, pixelCoords, lastPoint, -Vector2.UnitY, MathF.PI, previousWidth, end);
                }
            }

            /// <summary>
            /// Fills the circular sector of radius <paramref name="w"/> round <paramref name="centre"/> that starts at the unit vector
            /// <paramref name="from"/> and turns by <paramref name="sweep"/> radians, with a fan of triangles fine enough that the arc looks round.
            /// </summary>
            private void drawFan(IRenderer renderer, Texture pixel, RectangleF pixelCoords, Vector2 centre, Vector2 from, float sweep, float w, ColourInfo colour)
            {
                // the arc may be at most ~0.15 px off the true circle
                float stepAngle = 2f * MathF.Sqrt(0.15f / w);
                int steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / Math.Max(stepAngle, 0.02f)), 1, 96);
                float start = MathF.Atan2(from.Y, from.X);

                Vector2 previous = centre + from * w;
                Vector2 c = Vector2Extensions.Transform(centre, DrawInfo.Matrix);

                for (int k = 1; k <= steps; k++)
                {
                    float a = start + sweep * k / steps;
                    Vector2 next = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * w;

                    // a triangle is a quad with two equal corners
                    Vector2 pa = Vector2Extensions.Transform(previous, DrawInfo.Matrix);
                    Vector2 pb = Vector2Extensions.Transform(next, DrawInfo.Matrix);
                    renderer.DrawQuad(pixel, new Quad(c, pa, pb, pb), colour, textureCoords: pixelCoords);

                    previous = next;
                }
            }

            /// <summary>Draws a texture as a rectangle of half extents (along, across), rotated so "along" points in <paramref name="direction"/>.</summary>
            /// <summary>The black hole's stretch of the bullet being drawn (see <see cref="BulletInstance.LensStretch"/>).</summary>
            private float lensStretch = 1f;

            private Vector2 lensAxis, lensCentre;

            /// <summary>How many strips a bent picture is laid along its arc in.</summary>
            private const int bend_strips = 8;

            private void drawShape(IRenderer renderer, Texture texture, RectangleF coords, Vector2 centre, Vector2 direction, float along, float across, ColourInfo colour)
            {
                Vector2 u = direction * along;
                Vector2 v = new Vector2(-direction.Y, direction.X) * across;

                if (lensStretch > 1.001f)
                {
                    // longer along the circle round the hole, thinner across it, and bent along that circle (the light that passes it is bent into an arc)
                    Vector2 normal = new Vector2(-lensAxis.Y, lensAxis.X);
                    float squeeze = 1f / MathF.Sqrt(lensStretch);
                    u = lensAxis * (Vector2.Dot(u, lensAxis) * lensStretch) + normal * (Vector2.Dot(u, normal) * squeeze);
                    v = lensAxis * (Vector2.Dot(v, lensAxis) * lensStretch) + normal * (Vector2.Dot(v, normal) * squeeze);

                    if (drawBent(renderer, texture, coords, centre, u, v, colour))
                        return;
                }

                var quad = new Quad(
                    Vector2Extensions.Transform(centre - u - v, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre + u - v, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre - u + v, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre + u + v, DrawInfo.Matrix));

                renderer.DrawQuad(texture, quad, colour, textureCoords: coords);
            }

            /// <summary>
            /// A picture (the rectangle centre ± u ± v, u along the texture's width) bent round the black hole: it is cut into strips across the side that runs most along the circle round the hole, and every
            /// corner is put on the circle at its distance from the hole's middle, as far round it as it was along it. False when there is nothing to bend round.
            /// </summary>
            private bool drawBent(IRenderer renderer, Texture texture, RectangleF coords, Vector2 centre, Vector2 u, Vector2 v, ColourInfo colour)
            {
                Vector2 fromHole = centre - lensCentre;
                float middle = fromHole.Length;

                if (middle < 1f)
                    return false;

                Vector2 outward = fromHole / middle;
                Vector2 around = new Vector2(-outward.Y, outward.X);
                float start = MathF.Atan2(outward.Y, outward.X);

                Vector2 bend(Vector2 p)
                {
                    Vector2 off = p - centre;
                    float distance = Math.Max(middle + Vector2.Dot(off, outward), 1f);
                    float angle = start + Vector2.Dot(off, around) / middle;
                    return lensCentre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                }

                Vector2 transform(Vector2 p) => Vector2Extensions.Transform(bend(p), DrawInfo.Matrix);

                // (the strips go along u when u runs round the hole more than v does; the texture's coordinates follow them. As many as the bend needs: how far round the hole the picture reaches)
                bool alongU = MathF.Abs(Vector2.Dot(u, around)) >= MathF.Abs(Vector2.Dot(v, around));
                float reach = 2f * Math.Max(MathF.Abs(Vector2.Dot(u, around)), MathF.Abs(Vector2.Dot(v, around))) / middle; // (radians)
                int strips = Math.Clamp((int)MathF.Ceiling(reach / 0.12f), 1, bend_strips);

                if (strips == 1)
                    return false; // (it hardly bends: one straight picture)

                for (int i = 0; i < strips; i++)
                {
                    float s0 = -1f + 2f * i / strips, s1 = -1f + 2f * (i + 1) / strips;

                    if (alongU)
                    {
                        var part = new RectangleF(coords.X + coords.Width * i / strips, coords.Y, coords.Width / strips, coords.Height);
                        var quad = new Quad(transform(centre + u * s0 - v), transform(centre + u * s1 - v), transform(centre + u * s0 + v), transform(centre + u * s1 + v));
                        renderer.DrawQuad(texture, quad, colour, textureCoords: part);
                    }
                    else
                    {
                        var part = new RectangleF(coords.X, coords.Y + coords.Height * i / strips, coords.Width, coords.Height / strips);
                        var quad = new Quad(transform(centre - u + v * s0), transform(centre + u + v * s0), transform(centre - u + v * s1), transform(centre + u + v * s1));
                        renderer.DrawQuad(texture, quad, colour, textureCoords: part);
                    }
                }

                return true;
            }

            protected override bool CanDrawOpaqueInterior => false;
        }
    }
}
