using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>The pictures that the stages of some bosses are drawn with (the first ones are files in Resources/Textures, the last ones are made here).</summary>
    public enum StageTexture
    {
        Earth,

        /// <summary>The Earth big enough for the World's Ender, with holes where its small chunks sit.</summary>
        EarthBig,

        /// <summary>The very big chunks its halves break into at the split, the thin cracks that warn of them, and the molten edges of the holes at each end of the cut.</summary>
        EarthChunk0,
        EarthChunk1,
        EarthChunk2,
        EarthChunk3,
        EarthChunk4,
        EarthChunk5,
        EarthChunk6,
        EarthChunk7,
        EarthChunk8,
        EarthChunk9,
        EarthChunk10,
        EarthChunk11,
        EarthDebris0,
        EarthDebris1,
        EarthDebris2,
        EarthDebris3,
        EarthDebris4,
        EarthDebris5,
        EarthDebris6,
        EarthDebris7,
        EarthDebris8,
        EarthDebris9,
        EarthDebris10,
        EarthDebris11,
        EarthDebris12,
        EarthDebris13,
        EarthDebris14,
        EarthDebris15,
        EarthDebris16,
        EarthDebris17,
        EarthDebris18,
        EarthDebris19,
        EarthDebris20,
        EarthDebris21,
        EarthDebris22,
        EarthDebris23,
        EarthDebris24,
        EarthDebris25,
        EarthDebris26,
        EarthDebris27,
        EarthDebris28,
        EarthDebris29,
        EarthDebris30,
        EarthDebris31,
        EarthDebris32,
        EarthDebris33,
        EarthDebris34,
        EarthDebris35,
        EarthDebris36,
        EarthDebris37,
        EarthDebris38,
        EarthDebris39,
        EarthDebris40,
        EarthDebris41,
        EarthDebris42,
        EarthDebris43,
        EarthDebris44,
        EarthDebris45,
        EarthDebris46,
        EarthDebris47,
        EarthCracks,
        EarthHoleGlow0,
        EarthHoleGlow1,

        /// <summary>The small chunks as they fly, their broken edges molten.</summary>
        EarthPieceHot0,
        EarthPieceHot1,
        EarthPieceHot2,
        EarthPieceHot3,
        EarthPieceHot4,
        EarthPieceHot5,
        EarthPieceHot6,
        EarthPieceHot7,
        EarthPieceHot8,
        EarthPieceHot9,
        EarthPieceHot10,
        EarthPieceHot11,
        EarthPieceHot12,
        EarthPieceHot13,
        EarthPieceHot14,
        EarthPieceHot15,

        /// <summary>The lit side of a ball, sharp: the white arc the World's Ender's impact frame puts on the side of a bullet that faces the middle (it faces the right edge of the picture).</summary>
        LitArc0,
        LitArc1,
        LitArc2,
        LitArc3,
        LitArc4,
        LitArc5,
        LitArc6,
        LitArc7,

        /// <summary>The chunks that break away from the Earth in the World's Ender (see EarthPieces).</summary>
        EarthPiece0,
        EarthPiece1,
        EarthPiece2,
        EarthPiece3,
        EarthPiece4,
        EarthPiece5,
        EarthPiece6,
        EarthPiece7,
        EarthPiece8,
        EarthPiece9,
        EarthPiece10,
        EarthPiece11,
        EarthPiece12,
        EarthPiece13,
        EarthPiece14,
        EarthPiece15,
        Moon,
        Sun,
        Ufo,
        Taiko,
        BlackHole,

        /// <summary>A wedge that fades out along its length (the apex is at the left edge, the wedge widens to the right): Nue's suction cone.</summary>
        Cone,

        /// <summary>A wedge that fades out along its length, from its apex (at the left edge, where it is brightest) to the right edge, where it is as high as the picture: the cone of light that shows where Utsuho's jets go.</summary>
        Wedge,

        /// <summary>A column of light: as wide as the picture is high, fading out along its length (the bright end is at the left edge).</summary>
        Light,

        /// <summary>A thin soft ring touching the edges of the picture: a ripple.</summary>
        Ring,

        /// <summary>A soft round glow.</summary>
        Glow,

        /// <summary>A pane of glass seen from the side: a thin strip (long side along the picture's width) with bright edges and a few faint glints.</summary>
        Pane,

        /// <summary>A yin-yang orb (red and white with a dark rim), upright.</summary>
        YinYang,

        /// <summary>A white disc with a sharp edge (to be tinted), baked large so that it stays sharp when it is drawn huge: the border and the body of Mokou's big circle.</summary>
        Orb,

        /// <summary>The white glow of a round bullet's body: opaque in the middle and fading out to nothing at the edge (the same curve as the small bullets').</summary>
        OrbGlow,

        /// <summary>A skull (white, with the dark parts of the picture as its opaque parts: to be tinted): Zanmu's.</summary>
        Skull,

        /// <summary>A red circle with a rim, with an eye in it (white lines): Flandre's eye; the circle is as big as her explosion. (Drawn from shapes; there is a vector file of the same picture.)</summary>
        Eye,

        /// <summary>The frames of a flapping bat (black, with a white outline that fades inwards), Koakuma's: <see cref="StageLayer.BatFrames"/> of them, one after the other; the bat is as wide as the picture over <see cref="StageLayer.BatExtent"/> (see <see cref="StageLayer.BatFrame"/>).</summary>
        Bat0,
        Bat1,
        Bat2,
        Bat3,
        Bat4,
        Bat5,
        Bat6,
        Bat7,

        /// <summary>A pane of glass in the shape of an equilateral triangle (Sunny Milk's prism): its corners are <see cref="StageLayer.PrismCorner"/> of the way from the middle to the edge of the picture, one pointing up.</summary>
        Prism,

        /// <summary>Like <see cref="Cone"/>, for the wider cone (<see cref="DanmakusuBalance.NUE_SHOOT_CONE_HALF_ANGLE_DEGREES"/>) that shows where Nue's UFOs shoot.</summary>
        ShootCone,

        /// <summary>A pool of blood (Chiyari's): an uneven dark red shape with a bright edge, and a vampire's jaw of eight teeth in it (two long fangs).</summary>
        BloodPool,

        /// <summary>Kaguya's giant moon: a big copy of Hecatia's picture.</summary>
        KaguyaMoon,

        /// <summary>The bottom of Kaguya's moon, the only part of it that shows, at a high resolution (made by Tools/MoonCap: the shapes of Hecatia's picture with fine craters of its own): 0.72 of its radius to either side of the middle, the bottom 0.3 of its radius.</summary>
        KaguyaMoonCap,


        /// <summary>A magic circle (white, to be tinted): a thick outer ring touching the edge of the picture, a band of rune marks inside it, a thin inner ring and an eight-pointed star: Toyohime's.</summary>
        MagicCircle,

        /// <summary>A chevron (white, to be tinted) pointing right: an arrow that shows which way something goes.</summary>
        Chevron,

        /// <summary>The big star that sits on a boss while she fires (see <see cref="SoftStar.CreateBig"/>), in Byakuren's colours, and its glow.</summary>
        ByakurenStar,

        ByakurenStarGlow,

        /// <summary>The pictures of Sekibanki's flying head, one after the other (Resources/Textures/sekibanki_head_N.png): <see cref="SekibankiHead0"/> + N.</summary>
        SekibankiHead0,
        SekibankiHead1,
        SekibankiHead2,
        SekibankiHead3,
        SekibankiHead4,
        SekibankiHead5,
        SekibankiHead6,
        SekibankiHead7,
        SekibankiHead8,
        SekibankiHead9,
        SekibankiHead10,
        SekibankiHead11,

        /// <summary>Unzan, Ichirin's cloud (Resources/Textures/unzan_*.png): the head behind her and her two hands.</summary>
        UnzanHead,
        UnzanHandLeft,
        UnzanHandRight,

        /// <summary>Enoko's bear trap seen from above, open (a ring of jaws with teeth pointing in, the brass plate in the middle) and shut (the jaws closed across the middle).</summary>
        BearTrapOpen,
        BearTrapClosed,

        /// <summary>A big five-pointed star in white (to be tinted): Sanae's, behind her attacks.</summary>
        BigStar,

        /// <summary>The outline of a horse's head and neck in white (to be tinted), an eye left open in it: Saki's, behind her attacks.</summary>
        HorseHead,

        /// <summary>Futo's boat seen from above (Resources/Textures/futo_boat.png, the user's picture, made with Tools/Bake `boat`): a wooden ship, its stern on the left and its bow on the right, 1352 x 404.</summary>
        Boat,

        /// <summary>The arrow on Seija's dress, pointing right: a triangular head on a straight shaft, white (to be tinted) with a dark outline.</summary>
        SeijaArrow,

        /// <summary>The cracks of the Earth (<see cref="EarthCracks"/>) all white, as they are seen in the World's Ender's impact frame.</summary>
        EarthCracksWhite,

        /// <summary>Murasa's anchor (Resources/Textures/murasa_anchor.png, the user's picture, made with Tools/Bake `anchor`): silver, its ring on the left and its crown on the right, 512 x 433.</summary>
        Anchor,

        /// <summary>Eternity Larva's butterfly seen from above (Resources/Textures/larva_butterfly.png, the user's picture, its white background taken away with Tools/Bake `cutout`): its head at the top, its wings across, 512 x 331.</summary>
        Butterfly,
    }

    /// <summary>
    /// A pile of sprites that the field refills every frame (the things of a boss's stage: planets, UFOs, drums, ripples, a tree, a black hole). Positions are the field's.
    /// A layer made <c>clipped</c> only shows what is inside the screen (a ripple is only seen inside the screen).
    /// </summary>
    public partial class StageLayer : CompositeDrawable
    {
        private Sprite[] pool;
        private readonly StageTexture[] wanted;
        private readonly Dictionary<StageTexture, Texture?> textures = new Dictionary<StageTexture, Texture?>();
        private readonly Container? clip;
        private readonly Container? content;
        private int used;
        private readonly CircularProgress[]? arcs;
        private int arcUsed;

        public StageLayer(int capacity, bool clipped, params StageTexture[] textures)
        {
            Origin = Anchor.TopLeft; // positioned manually: children use the field's coordinates
            wanted = textures;
            pool = new Sprite[capacity];

            // (a layer with rings also has a few arcs for the rings that must be exact: AddSolidRing)
            if (Array.IndexOf(textures, StageTexture.Ring) >= 0)
            {
                arcs = new CircularProgress[max_arcs];

                for (int i = 0; i < max_arcs; i++)
                {
                    arcs[i] = new CircularProgress { Origin = Anchor.Centre, Alpha = 0, Progress = 1 };
                }
            }

            for (int i = 0; i < capacity; i++)
                pool[i] = new Sprite { Origin = Anchor.Centre, Alpha = 0 };

            if (clipped)
            {
                content = new Container();

                foreach (var sprite in pool)
                    content.Add(sprite);

                if (arcs != null)
                {
                    foreach (var arc in arcs)
                        content.Add(arc);
                }

                clip = new Container { Masking = true, Child = content };
                AddInternal(clip);
            }
            else
            {
                foreach (var sprite in pool)
                    AddInternal(sprite);

                if (arcs != null)
                {
                    foreach (var arc in arcs)
                        AddInternal(arc);
                }
            }
        }

        private const int max_arcs = 64;

        /// <summary>The layer takes as many sprites as it is given (otherwise what does not fit is left out).</summary>
        public bool Grows { get; init; }

        private void grow()
        {
            int had = pool.Length;

            Array.Resize(ref pool, had * 2);

            for (int i = had; i < pool.Length; i++)
            {
                pool[i] = new Sprite { Origin = Anchor.Centre, Alpha = 0 };

                if (content != null)
                    content.Add(pool[i]);
                else
                    AddInternal(pool[i]);
            }
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            foreach (var kind in wanted)
            {
                textures[kind] = kind switch
                {
                    StageTexture.Earth => BossCharacter.LoadResource(renderer, "Textures.earth.png"),
                    StageTexture.EarthBig => BossCharacter.LoadResource(renderer, "Textures.earth_big.webp"),
                    StageTexture.EarthChunk0 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_0.webp"),
                    StageTexture.EarthChunk1 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_1.webp"),
                    StageTexture.EarthChunk2 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_2.webp"),
                    StageTexture.EarthChunk3 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_3.webp"),
                    StageTexture.EarthChunk4 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_4.webp"),
                    StageTexture.EarthChunk5 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_5.webp"),
                    StageTexture.EarthChunk6 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_6.webp"),
                    StageTexture.EarthChunk7 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_7.webp"),
                    StageTexture.EarthChunk8 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_8.webp"),
                    StageTexture.EarthChunk9 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_9.webp"),
                    StageTexture.EarthChunk10 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_10.webp"),
                    StageTexture.EarthChunk11 => BossCharacter.LoadResource(renderer, "Textures.earth_chunk_11.webp"),
                    StageTexture.EarthDebris0 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_0.webp"),
                    StageTexture.EarthDebris1 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_1.webp"),
                    StageTexture.EarthDebris2 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_2.webp"),
                    StageTexture.EarthDebris3 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_3.webp"),
                    StageTexture.EarthDebris4 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_4.webp"),
                    StageTexture.EarthDebris5 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_5.webp"),
                    StageTexture.EarthDebris6 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_6.webp"),
                    StageTexture.EarthDebris7 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_7.webp"),
                    StageTexture.EarthDebris8 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_8.webp"),
                    StageTexture.EarthDebris9 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_9.webp"),
                    StageTexture.EarthDebris10 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_10.webp"),
                    StageTexture.EarthDebris11 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_11.webp"),
                    StageTexture.EarthDebris12 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_12.webp"),
                    StageTexture.EarthDebris13 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_13.webp"),
                    StageTexture.EarthDebris14 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_14.webp"),
                    StageTexture.EarthDebris15 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_15.webp"),
                    StageTexture.EarthDebris16 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_16.webp"),
                    StageTexture.EarthDebris17 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_17.webp"),
                    StageTexture.EarthDebris18 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_18.webp"),
                    StageTexture.EarthDebris19 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_19.webp"),
                    StageTexture.EarthDebris20 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_20.webp"),
                    StageTexture.EarthDebris21 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_21.webp"),
                    StageTexture.EarthDebris22 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_22.webp"),
                    StageTexture.EarthDebris23 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_23.webp"),
                    StageTexture.EarthDebris24 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_24.webp"),
                    StageTexture.EarthDebris25 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_25.webp"),
                    StageTexture.EarthDebris26 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_26.webp"),
                    StageTexture.EarthDebris27 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_27.webp"),
                    StageTexture.EarthDebris28 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_28.webp"),
                    StageTexture.EarthDebris29 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_29.webp"),
                    StageTexture.EarthDebris30 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_30.webp"),
                    StageTexture.EarthDebris31 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_31.webp"),
                    StageTexture.EarthDebris32 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_32.webp"),
                    StageTexture.EarthDebris33 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_33.webp"),
                    StageTexture.EarthDebris34 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_34.webp"),
                    StageTexture.EarthDebris35 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_35.webp"),
                    StageTexture.EarthDebris36 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_36.webp"),
                    StageTexture.EarthDebris37 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_37.webp"),
                    StageTexture.EarthDebris38 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_38.webp"),
                    StageTexture.EarthDebris39 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_39.webp"),
                    StageTexture.EarthDebris40 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_40.webp"),
                    StageTexture.EarthDebris41 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_41.webp"),
                    StageTexture.EarthDebris42 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_42.webp"),
                    StageTexture.EarthDebris43 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_43.webp"),
                    StageTexture.EarthDebris44 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_44.webp"),
                    StageTexture.EarthDebris45 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_45.webp"),
                    StageTexture.EarthDebris46 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_46.webp"),
                    StageTexture.EarthDebris47 => BossCharacter.LoadResource(renderer, "Textures.earth_debris_47.webp"),
                    StageTexture.EarthPieceHot0 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_0.webp"),
                    StageTexture.EarthPieceHot1 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_1.webp"),
                    StageTexture.EarthPieceHot2 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_2.webp"),
                    StageTexture.EarthPieceHot3 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_3.webp"),
                    StageTexture.EarthPieceHot4 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_4.webp"),
                    StageTexture.EarthPieceHot5 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_5.webp"),
                    StageTexture.EarthPieceHot6 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_6.webp"),
                    StageTexture.EarthPieceHot7 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_7.webp"),
                    StageTexture.EarthPieceHot8 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_8.webp"),
                    StageTexture.EarthPieceHot9 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_9.webp"),
                    StageTexture.EarthPieceHot10 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_10.webp"),
                    StageTexture.EarthPieceHot11 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_11.webp"),
                    StageTexture.EarthPieceHot12 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_12.webp"),
                    StageTexture.EarthPieceHot13 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_13.webp"),
                    StageTexture.EarthPieceHot14 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_14.webp"),
                    StageTexture.EarthPieceHot15 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_hot_15.webp"),
                    StageTexture.EarthCracks => BossCharacter.LoadResource(renderer, "Textures.earth_cracks.webp"),
                    StageTexture.EarthCracksWhite => whitened(renderer, "Textures.earth_cracks.webp"),
                    StageTexture.EarthHoleGlow0 => BossCharacter.LoadResource(renderer, "Textures.earth_hole_glow_0.webp"),
                    StageTexture.EarthHoleGlow1 => BossCharacter.LoadResource(renderer, "Textures.earth_hole_glow_1.webp"),
                    StageTexture.Moon => BossCharacter.LoadResource(renderer, "Textures.moon.png"),
                    StageTexture.KaguyaMoon => BossCharacter.LoadResource(renderer, "Textures.moon_big.webp"),
                    StageTexture.KaguyaMoonCap => BossCharacter.LoadResource(renderer, "Textures.moon_cap.webp"),
                    StageTexture.Sun => BossCharacter.LoadResource(renderer, "Textures.sun.png"),
                    StageTexture.Ufo => BossCharacter.LoadResource(renderer, "Textures.ufo.png"),
                    StageTexture.Taiko => BossCharacter.LoadResource(renderer, "Textures.taiko.png"),
                    StageTexture.Skull => BossCharacter.LoadResource(renderer, "Textures.skull.png"),
                    StageTexture.UnzanHead => BossCharacter.LoadResource(renderer, "Textures.unzan_head.png"),
                    StageTexture.UnzanHandLeft => BossCharacter.LoadResource(renderer, "Textures.unzan_hand_left.png"),
                    StageTexture.UnzanHandRight => BossCharacter.LoadResource(renderer, "Textures.unzan_hand_right.png"),
                    StageTexture.BlackHole => BossCharacter.LoadResource(renderer, "Textures.black_hole.png"),
                    StageTexture.Cone => bake(renderer, makeCone(ConeStartFor(DanmakusuBalance.NUE_CONE_HALF_ANGLE_DEGREES))),
                    StageTexture.ShootCone => bake(renderer, makeCone(ConeStartFor(DanmakusuBalance.NUE_SHOOT_CONE_HALF_ANGLE_DEGREES))),
                    StageTexture.Wedge => bake(renderer, makeWedge()),
                    StageTexture.BloodPool => bake(renderer, makeBloodPool()),
                    StageTexture.Light => bake(renderer, makeLight()),
                    StageTexture.Ring => bakeRings(renderer),
                    StageTexture.Glow => bake(renderer, makeGlow()),
                    StageTexture.Pane => bake(renderer, makePane()),
                    StageTexture.YinYang => bake(renderer, makeYinYang()),
                    StageTexture.Orb => bake(renderer, makeOrb()),
                    StageTexture.OrbGlow => bake(renderer, makeOrbGlow()),
                    StageTexture.Eye => bake(renderer, makeEye()),
                    StageTexture.BigStar => bake(renderer, SoftStar.CreateBig(256, 0.6f, Color4.White)), // (to be tinted: Sanae's star behind her attacks)
                    StageTexture.HorseHead => bake(renderer, makeHorseHead()),
                    StageTexture.Boat => BossCharacter.LoadResource(renderer, "Textures.futo_boat.png"),
                    StageTexture.BearTrapOpen => bake(renderer, makeBearTrap(false)),
                    StageTexture.BearTrapClosed => bake(renderer, makeBearTrap(true)),
                    StageTexture.Prism => bake(renderer, makePrism()),
                    StageTexture.MagicCircle => bake(renderer, makeMagicCircle()),
                    StageTexture.Chevron => bake(renderer, makeChevron()),
                    StageTexture.SeijaArrow => bake(renderer, makeSeijaArrow()),
                    StageTexture.Anchor => BossCharacter.LoadResource(renderer, "Textures.murasa_anchor.png"), StageTexture.Butterfly => BossCharacter.LoadResource(renderer, "Textures.larva_butterfly.png"),
                    StageTexture.LitArc0 => bake(renderer, makeLitArc(1f - (0 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc1 => bake(renderer, makeLitArc(1f - (1 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc2 => bake(renderer, makeLitArc(1f - (2 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc3 => bake(renderer, makeLitArc(1f - (3 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc4 => bake(renderer, makeLitArc(1f - (4 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc5 => bake(renderer, makeLitArc(1f - (5 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc6 => bake(renderer, makeLitArc(1f - (6 + 0.5f) / LIT_ARCS)),
                    StageTexture.LitArc7 => bake(renderer, makeLitArc(1f - (7 + 0.5f) / LIT_ARCS)),
                    StageTexture.EarthPiece0 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_0.webp"),
                    StageTexture.EarthPiece1 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_1.webp"),
                    StageTexture.EarthPiece2 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_2.webp"),
                    StageTexture.EarthPiece3 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_3.webp"),
                    StageTexture.EarthPiece4 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_4.webp"),
                    StageTexture.EarthPiece5 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_5.webp"),
                    StageTexture.EarthPiece6 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_6.webp"),
                    StageTexture.EarthPiece7 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_7.webp"),
                    StageTexture.EarthPiece8 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_8.webp"),
                    StageTexture.EarthPiece9 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_9.webp"),
                    StageTexture.EarthPiece10 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_10.webp"),
                    StageTexture.EarthPiece11 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_11.webp"),
                    StageTexture.EarthPiece12 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_12.webp"),
                    StageTexture.EarthPiece13 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_13.webp"),
                    StageTexture.EarthPiece14 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_14.webp"),
                    StageTexture.EarthPiece15 => BossCharacter.LoadResource(renderer, "Textures.earth_piece_15.webp"),
                    StageTexture.ByakurenStar => bake(renderer, SoftStar.CreateBig(256, 0.6f, byakurenColour(1, 0.45f))), // (her purple, lighter)
                    StageTexture.ByakurenStarGlow => bake(renderer, SoftStar.CreateGlow(256, 0.6f, byakurenColour(2, 0f))), // (her peach)
                    >= StageTexture.Bat0 and <= StageTexture.Bat7 => bake(renderer, makeBat(kind - StageTexture.Bat0)),
                    >= StageTexture.SekibankiHead0 and <= StageTexture.SekibankiHead11 => BossCharacter.LoadResource(renderer, $"Textures.sekibanki_head_{kind - StageTexture.SekibankiHead0}.png"),
                    _ => null,
                };
            }
        }

        // The rings of ripples keep their width (in pixels) however big they get: a family of ring pictures, each a smooth bump whose width is a different share of
        // the radius (every one a step of ring_ratio thinner than the one before); a ring is drawn with the two that its width falls between, faded into each other.
        private const int ring_levels = 10;
        private const float ring_widest = 0.9f;
        private const float ring_ratio = 1.5f;
        private Texture[]? ringFamily;

        private Texture bakeRings(IRenderer renderer)
        {
            ringFamily = new Texture[ring_levels];

            for (int i = 0; i < ring_levels; i++)
                ringFamily[i] = bake(renderer, makeRing(ring_widest / MathF.Pow(ring_ratio, i)));

            return ringFamily[ring_levels / 2];
        }

        /// <summary>
        /// A solid ring (Raiko's ripples): its outer edge at <paramref name="radius"/>, <paramref name="thickness"/> pixels thick however big it is, with a sharp edge. Drawn as an arc by the framework, so it
        /// is exact at every size (no picture that has to be stretched, and nothing fading between two of them).
        /// </summary>
        public void AddSolidRing(Vector2 centre, float radius, float thickness, float alpha, Color4 colour)
        {
            if (arcs == null || arcUsed >= arcs.Length || radius < 1f || alpha <= 0.002f)
                return;

            var arc = arcs[arcUsed++];
            arc.Position = centre;
            arc.Size = new Vector2(2f * radius);
            arc.InnerRadius = Math.Clamp(thickness / radius, 0.001f, 1f);
            arc.Alpha = Math.Min(alpha, 1f);
            arc.Colour = colour;
            arc.Blending = BlendingParameters.Additive;
        }

        /// <summary>A solid ring whose edges are softened over <paramref name="soft"/> pixels on each side: three solid rings over each other, each wider than the last, a third as bright (they add up: full in the middle).</summary>
        public void AddSoftRing(Vector2 centre, float radius, float thickness, float soft, float alpha, Color4 colour)
        {
            for (int i = 0; i < 3; i++)
            {
                float wider = soft * i / 2f;
                AddSolidRing(centre, radius + wider, thickness + 2f * wider, alpha / 3f, colour);
            }
        }

        /// <summary>A ring whose outer edge is at <paramref name="radius"/> and whose bright band is <paramref name="halfWidth"/> either side of its middle, a smooth bump however big it is.</summary>
        public void AddRing(Vector2 centre, float radius, float halfWidth, float alpha, Color4 colour)
        {
            if (ringFamily == null || radius < 1f)
                return;

            float fraction = Math.Clamp(halfWidth / radius, ring_widest / MathF.Pow(ring_ratio, ring_levels - 1), ring_widest);
            float level = MathF.Log(ring_widest / fraction) / MathF.Log(ring_ratio);
            int low = Math.Clamp((int)MathF.Floor(level), 0, ring_levels - 1);
            int high = Math.Min(low + 1, ring_levels - 1);
            float share = low == high ? 0f : level - low;
            var size = new Vector2(2f * radius);

            addSprite(ringFamily[low], centre, size, alpha * (1f - share), colour);

            if (share > 0.01f)
                addSprite(ringFamily[high], centre, size, alpha * share, colour);
        }

        private void addSprite(Texture? texture, Vector2 position, Vector2 size, float alpha, Color4 colour)
        {
            if (alpha <= 0.002f)
                return;

            if (used >= pool.Length)
            {
                if (!Grows)
                    return;

                grow();
            }

            var sprite = pool[used++];
            sprite.Texture = texture;
            sprite.Position = position;
            sprite.Size = size;
            sprite.Rotation = 0f;
            sprite.Alpha = Math.Min(alpha, 1f);
            sprite.Colour = colour;
            sprite.Blending = BlendingParameters.Additive;
        }

        /// <summary>A puff of cloud (white, to be tinted): like the glow, but its edge is half as wide. The bullet renderer draws these itself (Tojiko's clouds go between the beams and the bullets).</summary>
        public static Texture BakeCloud(IRenderer renderer) => bake(renderer, makeCloud());

        /// <summary>The soft light of <see cref="StageTexture.Glow"/>, for the flashes in the clouds.</summary>
        public static Texture BakeGlow(IRenderer renderer) => bake(renderer, makeGlow());

        private static Texture bake(IRenderer renderer, Image<Rgba32> image)
        {
            var texture = renderer.CreateTexture(image.Width, image.Height);
            texture.SetData(new TextureUpload(image));
            return texture;
        }

        /// <summary>A picture of the resources made all white, keeping how see-through it is everywhere (null if it is not there).</summary>
        private static Texture? whitened(IRenderer renderer, string path)
        {
            try
            {
                using var stream = typeof(StageLayer).Assembly.GetManifestResourceStream($"osu.Game.Rulesets.Danmakusu.Resources.{path}");

                if (stream == null)
                    return null;

                var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);

                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                        image[x, y] = new Rgba32(255, 255, 255, image[x, y].A);
                }

                return bake(renderer, image);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A smooth bump (no hard edge on either side) with its outer end at the picture's edge and half <paramref name="fraction"/> of the radius either side of its middle.</summary>
        private static Image<Rgba32> makeRing(float fraction)
        {
            const int size = 512;
            float middle = 1f - fraction;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float rho = MathF.Sqrt(dx * dx + dy * dy);
                    float t = Math.Clamp(MathF.Abs(rho - middle) / fraction, 0f, 1f);
                    float a = 1f - t * t * (3f - 2f * t);
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>Colour <paramref name="index"/> of Byakuren's signature, <paramref name="lighter"/> of the way to white.</summary>
        private static Color4 byakurenColour(int index, float lighter)
        {
            Color4[]? colours = null;

            foreach (var boss in Bosses.BossRoster.All)
            {
                if (boss.Id == "byakuren_hijiri")
                    colours = boss.Colours;
            }

            Color4 colour = colours != null && colours.Length > 0 ? colours[Math.Min(index, colours.Length - 1)] : new Color4(0.6f, 0.45f, 0.75f, 1f);
            return BossHud.Interpolate(colour, Color4.White, lighter);
        }

        /// <summary>How much of a line of half width <paramref name="halfWidth"/> covers a pixel <paramref name="distance"/> from its middle (soft over <paramref name="aa"/>).</summary>
        private static float line(float distance, float halfWidth, float aa) => Math.Clamp(1f - (distance - halfWidth) / aa, 0f, 1f);

        private static float segmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float u = Math.Clamp(Vector2.Dot(p - a, ab) / Math.Max(ab.LengthSquared, 1e-9f), 0f, 1f);
            return (p - (a + ab * u)).Length;
        }

        /// <summary>
        /// A magic circle (white, to be tinted): a thick outer ring at the edge, a thin ring just inside it, a band of rune marks (bars, diamonds and dashes in turn) between them, a thin
        /// inner ring and an eight-pointed star (two squares) inside that, fainter, so the middle (where the player is) stays clear to see.
        /// </summary>
        private static Image<Rgba32> makeMagicCircle()
        {
            const int size = 1024;
            const int runes = 48;
            float aa = 2.5f / size;
            var image = new Image<Rgba32>(size, size);
            var star = new Vector2[8];

            for (int i = 0; i < 8; i++)
                star[i] = new Vector2(MathF.Cos(i * MathF.PI / 4f - MathF.PI / 2f), MathF.Sin(i * MathF.PI / 4f - MathF.PI / 2f)) * 0.78f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f - size / 2f) / (size / 2f), (y + 0.5f - size / 2f) / (size / 2f));
                    float rho = p.Length;

                    if (rho > 1.01f)
                    {
                        image[x, y] = new Rgba32(255, 255, 255, 0);
                        continue;
                    }

                    float a = 0f;

                    // the rings: thick at the edge, thin inside it, thin round the star
                    a = Math.Max(a, line(MathF.Abs(rho - 0.975f), 0.012f, aa));
                    a = Math.Max(a, line(MathF.Abs(rho - 0.895f), 0.004f, aa));
                    a = Math.Max(a, 0.8f * line(MathF.Abs(rho - 0.80f), 0.003f, aa));

                    // the runes, in the band between the outer rings
                    if (rho > 0.88f && rho < 0.99f)
                    {
                        float angle = MathF.Atan2(p.Y, p.X);
                        float step = MathF.Tau / runes;
                        int k = (int)MathF.Round(angle / step);
                        float across = (angle - k * step) * rho;
                        float along = rho - 0.935f;
                        int kind = ((k % 3) + 3) % 3;

                        float rune = kind switch
                        {
                            0 => Math.Min(line(MathF.Abs(across), 0.004f, aa), line(MathF.Abs(along), 0.022f, aa)), // (a bar across the band)
                            1 => line(MathF.Abs(across) / 0.014f + MathF.Abs(along) / 0.024f, 1f, aa * 60f), // (a diamond)
                            _ => Math.Max(Math.Min(line(MathF.Abs(along), 0.004f, aa), line(MathF.Abs(across), 0.014f, aa)), line(new Vector2(across, along - 0.016f).Length, 0.005f, aa)), // (a dash with a dot)
                        };

                        a = Math.Max(a, rune);
                    }

                    // the eight-pointed star: two squares, fainter
                    if (rho < 0.82f)
                    {
                        float d = float.MaxValue;

                        for (int i = 0; i < 8; i++)
                            d = Math.Min(d, segmentDistance(p, star[i], star[(i + 2) % 8]));

                        a = Math.Max(a, 0.55f * line(d, 0.003f, aa));
                    }

                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>A chevron pointing right (white, to be tinted), with soft edges.</summary>
        private static Image<Rgba32> makeChevron()
        {
            const int size = 128;
            var image = new Image<Rgba32>(size, size);
            Vector2 top = new Vector2(0.22f, 0.12f), tip = new Vector2(0.78f, 0.5f), bottom = new Vector2(0.22f, 0.88f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float d = Math.Min(segmentDistance(p, top, tip), segmentDistance(p, tip, bottom));
                    float a = line(d, 0.075f, 2f / size);
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>
        /// The arrow on Seija's dress, pointing right: a triangular head (two fifths of its length, as wide as it is long) on a straight shaft two fifths as wide as the head, white inside (to be tinted) with a thin dark outline,
        /// soft at its edge.
        /// </summary>
        private static Image<Rgba32> makeSeijaArrow()
        {
            const int size = 256;
            const float shaft = 0.18f, head = 0.46f, back = 0.04f, neck = 0.56f, tip = 0.96f, rim = 0.011f;
            var image = new Image<Rgba32>(size, size);
            Vector2[] outline =
            {
                new Vector2(back, 0.5f - shaft), new Vector2(neck, 0.5f - shaft), new Vector2(neck, 0.5f - head), new Vector2(tip, 0.5f),
                new Vector2(neck, 0.5f + head), new Vector2(neck, 0.5f + shaft), new Vector2(back, 0.5f + shaft),
            };
            float aa = 1.5f / size;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float d = float.MaxValue;
                    bool inside = false;

                    for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                    {
                        d = Math.Min(d, segmentDistance(p, outline[j], outline[i]));

                        if ((outline[i].Y > p.Y) != (outline[j].Y > p.Y) && p.X < outline[j].X + (outline[i].X - outline[j].X) * (p.Y - outline[j].Y) / (outline[i].Y - outline[j].Y))
                            inside = !inside;
                    }

                    // (the outline is dark, the inside white; outside, the edge fades over a pixel and a half)
                    float alpha = inside ? 1f : Math.Clamp(1f - d / aa, 0f, 1f);
                    float dark = inside ? Math.Clamp(1f - (d - rim) / aa, 0f, 1f) : 1f;
                    byte shade = (byte)Math.Clamp((int)((1f - 0.8f * dark) * 255f), 0, 255);

                    image[x, y] = new Rgba32(shade, shade, shade, (byte)Math.Clamp((int)(alpha * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>
        /// Murasa's anchor picture as a shape to touch (made once, from the picture): for every 4 x 4 pixels of it, how far (pixels of the picture) it is to the nearest place where the picture is not
        /// see-through (by the middle pixel of each 4 x 4). Empty when the picture is missing (nothing to touch then).
        /// </summary>
        private static readonly Lazy<(float[] Distance, int Columns, int Rows, int Width, int Height)> anchor_shape = new Lazy<(float[], int, int, int, int)>(makeAnchorShape);

        private const int anchor_cell = 4;

        /// <summary>Makes the shape of the anchor now (at load), so that the first anchor thrown does not make a frame wait.</summary>
        public static void PrepareAnchorShape() => _ = anchor_shape.Value;

        /// <summary>The size of the anchor picture in pixels (along it, across it).</summary>
        public static (int Width, int Height) AnchorPictureSize => (anchor_shape.Value.Width, anchor_shape.Value.Height);

        /// <summary>How far (pixels of the picture) the point (<paramref name="x"/> along the picture from its ring end, <paramref name="y"/> across it from its top) is from the anchor: 0 on it.</summary>
        public static float AnchorDistance(float x, float y)
        {
            var (distance, columns, rows, width, height) = anchor_shape.Value;

            if (columns == 0)
                return float.PositiveInfinity;

            // (outside the picture: as far as from the nearest point of the picture, and that point's distance on top)
            float cx = Math.Clamp(x, 0f, width - 1f), cy = Math.Clamp(y, 0f, height - 1f);
            float outside = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            int column = Math.Min((int)(cx / anchor_cell), columns - 1), row = Math.Min((int)(cy / anchor_cell), rows - 1);
            return outside + distance[row * columns + column];
        }

        private static (float[], int, int, int, int) makeAnchorShape()
        {
            try
            {
                using var stream = typeof(StageLayer).Assembly.GetManifestResourceStream("osu.Game.Rulesets.Danmakusu.Resources.Textures.murasa_anchor.png");

                if (stream == null)
                    return (Array.Empty<float>(), 0, 0, 0, 0);

                using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                int columns = (image.Width + anchor_cell - 1) / anchor_cell, rows = (image.Height + anchor_cell - 1) / anchor_cell;
                var solid = new bool[columns * rows];

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                        solid[r * columns + c] = image[Math.Min(c * anchor_cell + anchor_cell / 2, image.Width - 1), Math.Min(r * anchor_cell + anchor_cell / 2, image.Height - 1)].A > 128;
                }

                // (the nearest solid cell of every cell is on the edge of the solid part: only those are looked at)
                var edge = new List<(int C, int R)>();

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        if (!solid[r * columns + c])
                            continue;

                        bool inner = c > 0 && r > 0 && c < columns - 1 && r < rows - 1 && solid[r * columns + c - 1] && solid[r * columns + c + 1] && solid[(r - 1) * columns + c] && solid[(r + 1) * columns + c];

                        if (!inner)
                            edge.Add((c, r));
                    }
                }

                var distance = new float[columns * rows];

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        if (solid[r * columns + c])
                            continue;

                        float best = float.PositiveInfinity;

                        foreach (var (ec, er) in edge)
                            best = Math.Min(best, (ec - c) * (ec - c) + (er - r) * (er - r));

                        distance[r * columns + c] = MathF.Sqrt(best) * anchor_cell;
                    }
                }

                return (distance, columns, rows, image.Width, image.Height);
            }
            catch
            {
                return (Array.Empty<float>(), 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Futo's boat as a shape to touch and to push out of (made once, from its picture): its outline as a loop of points, x from -1 (the stern) to 1 (the bow), y from -1 (the top of the picture) to 1 (its bottom), along
        /// the top from the stern to the bow and back along the bottom; and how many times as long as it is wide the picture is. A plain pointed hull of 3.35 when the picture is missing.
        /// </summary>
        private static readonly Lazy<(Vector2[] Outline, float Aspect)> boat_shape = new Lazy<(Vector2[], float)>(makeBoatShape);

        /// <summary>Makes the shape of the boat now (at load), so that the first boat thrown does not make a frame wait.</summary>
        public static void PrepareBoatShape() => _ = boat_shape.Value;

        public static Vector2[] BoatOutline => boat_shape.Value.Outline;

        public static float BoatAspect => boat_shape.Value.Aspect;

        private const int boat_columns = 48;

        private static (Vector2[], float) makeBoatShape()
        {
            try
            {
                using var stream = typeof(StageLayer).Assembly.GetManifestResourceStream("osu.Game.Rulesets.Danmakusu.Resources.Textures.futo_boat.png");

                if (stream != null)
                {
                    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
                    var top = new List<Vector2>();
                    var bottom = new List<Vector2>();

                    // (where the picture is solid in every column it is looked at, from the first that has any of it to the last: the hull's top edge and its bottom edge there)
                    for (int k = 0; k <= boat_columns; k++)
                    {
                        int x = Math.Min((int)Math.Round(k / (float)boat_columns * (image.Width - 1)), image.Width - 1);
                        int first = -1, last = -1;

                        for (int y = 0; y < image.Height; y++)
                        {
                            if (image[x, y].A <= 128)
                                continue;

                            if (first < 0)
                                first = y;

                            last = y;
                        }

                        if (first < 0)
                            continue;

                        float u = (x + 0.5f) / image.Width * 2f - 1f;
                        top.Add(new Vector2(u, (first + 0.5f) / image.Height * 2f - 1f));
                        bottom.Add(new Vector2(u, (last + 0.5f) / image.Height * 2f - 1f));
                    }

                    if (top.Count >= 3)
                    {
                        bottom.Reverse();
                        top.AddRange(bottom);
                        return (top.ToArray(), image.Width / (float)image.Height);
                    }
                }
            }
            catch
            {
            }

            return (new[] { new Vector2(-1f, -0.7f), new Vector2(0.3f, -1f), new Vector2(1f, 0f), new Vector2(0.3f, 1f), new Vector2(-1f, 0.7f) }, 3.35f);
        }

        /// <summary>A yin-yang: a dark rim, a red and a white half, a small circle of the other colour (with a dot of its own) on each, and the picture is round (clear outside).</summary>
        /// <summary>How many pictures of the lit side of a ball there are, from next to nothing to half of it.</summary>
        public const int LIT_ARCS = 8;

        /// <summary>A ball lit from the right: what lies between its edge and the line between light and dark (half an ellipse, as on the moon, <paramref name="line"/> of the radius out from the middle at its widest: 1 next to nothing is lit, 0 half of it), with sharp edges.</summary>
        private static Image<Rgba32> makeLitArc(float line)
        {
            const int size = 128, samples = 4;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float px = (x + (sx + 0.5f) / samples) / size * 2f - 1f, py = (y + (sy + 0.5f) / samples) / size * 2f - 1f;
                            float rest = 1f - py * py;

                            if (rest > 0f && px * px + py * py <= 1f && px >= line * MathF.Sqrt(rest))
                                inside++;
                        }
                    }

                    image[x, y] = new Rgba32((byte)255, (byte)255, (byte)255, (byte)(255 * inside / (samples * samples)));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeYinYang()
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float rho = MathF.Sqrt(dx * dx + dy * dy);

                    if (rho >= 1f)
                    {
                        image[x, y] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    float top = MathF.Sqrt(dx * dx + (dy + 0.5f) * (dy + 0.5f));
                    float bottom = MathF.Sqrt(dx * dx + (dy - 0.5f) * (dy - 0.5f));
                    bool red = dx > 0f;

                    if (top < 0.5f)
                        red = true;

                    if (bottom < 0.5f)
                        red = false;

                    if (top < 0.14f)
                        red = false;

                    if (bottom < 0.14f)
                        red = true;

                    float rim = Math.Clamp((rho - 0.92f) / 0.04f, 0f, 1f);
                    float edge = Math.Clamp((1f - rho) * size / 2f, 0f, 1f); // (a soft outer edge, one pixel wide)
                    float r = red ? 214f : 248f, g = red ? 44f : 246f, b = red ? 50f : 240f;

                    r += (30f - r) * rim;
                    g += (24f - g) * rim;
                    b += (26f - b) * rim;

                    image[x, y] = new Rgba32((byte)r, (byte)g, (byte)b, (byte)(edge * 255f));
                }
            }

            return image;
        }

        // The eye (in the units of the 512-pixel picture of the eye, centred, so 1 = 256 px): a pointed oval (two circle arcs: half the width a, half the height h) with an oval hole, a ring (the iris) and a dot (the pupil).
        // The vector file flandre_eye.svg is made of exactly these shapes.
        private const float eye_outer_a = 254f / 256f, eye_outer_h = 139f / 256f, eye_inner_a = 226f / 256f, eye_inner_h = 107f / 256f;
        private const float eye_iris_outer = 122f / 256f, eye_iris_inner = 75f / 256f, eye_pupil = 25f / 256f;
        private const float eye_in_circle = 0.74f; // (how much of the circle the picture of the eye is wide)
        private const float eye_rim = 0.93f;

        private static bool inLens(float x, float y, float a, float h)
        {
            float rho = (a * a + h * h) / (2f * h);
            float c = rho - h;
            float d = MathF.Abs(y) + c;

            return x * x + d * d <= rho * rho;
        }

        private static bool eyeInk(float x, float y)
        {
            if (!inLens(x, y, eye_outer_a, eye_outer_h))
                return false;

            if (!inLens(x, y, eye_inner_a, eye_inner_h))
                return true;

            float iris = MathF.Sqrt(x * x + y * y);

            if (iris <= eye_iris_outer && iris >= eye_iris_inner)
                return true;

            float dx = x + 25f / 256f, dy = y + 20f / 256f;
            return dx * dx + dy * dy <= eye_pupil * eye_pupil;
        }

        /// <summary>A red circle with a dark rim and the eye in it in white: Flandre's eye (the circle is what her explosion reaches).</summary>
        private static Image<Rgba32> makeEye()
        {
            const int size = 512;
            const int samples = 3;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples - size / 2f) / (size / 2f);
                            float v = (y + (sy + 0.5f) / samples - size / 2f) / (size / 2f);
                            float rho = MathF.Sqrt(u * u + v * v);

                            if (rho > 1f)
                                continue;

                            a += 1f;

                            if (rho > eye_rim)
                            {
                                r += 120f;
                                g += 0f;
                                b += 10f;
                            }
                            else if (eyeInk(u / eye_in_circle, v / eye_in_circle))
                            {
                                r += 255f;
                                g += 236f;
                                b += 236f;
                            }
                            else
                            {
                                r += 215f;
                                g += 24f;
                                b += 32f;
                            }
                        }
                    }

                    if (a <= 0f)
                    {
                        image[x, y] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    image[x, y] = new Rgba32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / (samples * samples) * 255f));
                }
            }

            return image;
        }


        // ---- Enoko's bear trap ----

        private const int trap_teeth = 14;

        /// <summary>
        /// A bear trap seen from above (white-ish steel, to be drawn as it is): open, a ring of two jaws with teeth pointing in and the round brass plate in the middle; <paramref name="closed"/>, the two jaws shut:
        /// a narrow band across the picture with the teeth meshed along its middle.
        /// </summary>
        private static Image<Rgba32> makeBearTrap(bool closed)
        {
            const int size = 512;
            const int samples = 3;
            var image = new Image<Rgba32>(size, size);
            var steel = new Vector3(150, 156, 168);
            var dark = new Vector3(62, 64, 72);
            var tooth = new Vector3(214, 218, 228);
            var brass = new Vector3(196, 152, 70);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector3 sum = Vector3.Zero;
                    float a = 0f;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples - size / 2f) / (size / 2f);
                            float v = (y + (sy + 0.5f) / samples - size / 2f) / (size / 2f);
                            Vector3? colour = closed ? closedTrapAt(u, v, steel, dark, tooth) : openTrapAt(u, v, steel, dark, tooth, brass);

                            if (colour == null)
                                continue;

                            sum += colour.Value;
                            a += 1f;
                        }
                    }

                    if (a <= 0f)
                    {
                        image[x, y] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    sum /= a;
                    image[x, y] = new Rgba32((byte)sum.X, (byte)sum.Y, (byte)sum.Z, (byte)(a / (samples * samples) * 255f));
                }
            }

            return image;
        }

        private static Vector3? openTrapAt(float u, float v, Vector3 steel, Vector3 dark, Vector3 tooth, Vector3 brass)
        {
            float rho = MathF.Sqrt(u * u + v * v);

            // the springs: two flat bars out to the left and the right
            if (MathF.Abs(v) < 0.06f && MathF.Abs(u) > 0.9f && MathF.Abs(u) < 1f)
                return MathF.Abs(v) > 0.04f ? dark : steel;

            if (rho > 0.92f)
                return null;

            // the jaws: a steel ring with dark edges, the hinges (left and right) a little wider
            if (rho > 0.8f)
                return rho > 0.89f || rho < 0.83f ? dark : steel;

            // the teeth, pointing in from the jaws
            float angle = MathF.Atan2(v, u);
            float step = MathF.Tau / trap_teeth;
            float local = angle - MathF.Round(angle / step) * step;
            float depth = (0.8f - rho) / 0.22f; // (0 at the jaw, 1 at the tips)

            if (depth >= 0f && depth <= 1f && MathF.Abs(local) < step * 0.42f * (1f - depth))
                return MathF.Abs(local) > step * 0.32f * (1f - depth) ? dark : tooth;

            // the pressure plate in the middle
            if (rho < 0.3f)
                return rho > 0.26f ? dark * 1.2f : brass;

            // the cross bar under the plate
            if (MathF.Abs(v) < 0.035f && rho < 0.8f)
                return dark;

            return null;
        }

        private static Vector3? closedTrapAt(float u, float v, Vector3 steel, Vector3 dark, Vector3 tooth)
        {
            if (MathF.Abs(u) > 0.95f)
                return MathF.Abs(v) < 0.06f && MathF.Abs(u) < 1f ? dark : null;

            // the two shut jaws: a band, thickest in the middle
            float half = 0.26f * MathF.Sqrt(Math.Max(1f - u * u / 0.9f, 0f));

            if (MathF.Abs(v) > half)
                return null;

            if (MathF.Abs(v) > half - 0.035f)
                return dark;

            // the teeth meshed along the middle: a zigzag line
            float zig = 0.07f * (MathF.Abs(((u * trap_teeth * 0.5f) % 2f + 2f) % 2f - 1f) * 2f - 1f);

            if (MathF.Abs(v - zig) < 0.03f)
                return tooth;

            return steel;
        }

        // ---- Koakuma's bat ----

        /// <summary>How many pictures a bat has in one flap.</summary>
        public const int BatFrames = 8;

        /// <summary>The picture is this many times as wide as half the bat's wingspan: the wings never reach its edge when they are raised, and there is room for the faint glow round it.</summary>
        public const float BatExtent = 1.3f;

        public static StageTexture BatFrame(int frame) => StageTexture.Bat0 + Math.Clamp(frame, 0, BatFrames - 1);

        // The outline of a bat with its wings spread (traced from the picture that was asked for): x runs from -1 to 1 over the wingspan, y downwards, the head with its two ears is at the top.
        private static readonly float[] bat_outline =
        {
            -1.000f, -0.585f, -0.949f, -0.324f, -0.801f, 0.085f, -0.710f, 0.102f, -0.614f, 0.148f, -0.523f, 0.239f, -0.483f, 0.324f, -0.409f, 0.295f, -0.341f, 0.290f, -0.267f, 0.307f, -0.182f, 0.352f, -0.148f, 0.381f, -0.085f, 0.466f, -0.068f, 0.472f, -0.006f, 0.580f, 0.017f, 0.574f, 0.045f, 0.511f, 0.102f, 0.432f, 0.216f, 0.330f, 0.330f, 0.290f, 0.398f, 0.295f, 0.477f, 0.324f, 0.511f, 0.244f, 0.602f, 0.153f, 0.705f, 0.102f, 0.795f, 0.091f, 0.932f, -0.290f, 0.994f, -0.585f, 0.778f, -0.528f, 0.557f, -0.432f, 0.341f, -0.295f, 0.364f, -0.284f, 0.364f, -0.250f, 0.312f, -0.131f, 0.233f, -0.028f, 0.165f, 0.011f, 0.114f, 0.017f, 0.131f, -0.017f, 0.102f, -0.188f, 0.074f, -0.085f, 0.057f, -0.062f, 0.017f, -0.074f, -0.062f, -0.062f, -0.080f, -0.085f, -0.108f, -0.188f, -0.136f, -0.017f, -0.114f, 0.017f, -0.125f, 0.023f, -0.205f, -0.006f, -0.284f, -0.080f, -0.358f, -0.210f, -0.375f, -0.267f, -0.358f, -0.301f, -0.375f, -0.318f, -0.523f, -0.409f, -0.688f, -0.489f, -0.875f, -0.557f
        };

        private const float bat_shoulder_x = 0.15f;
        private const float bat_shoulder_y = 0.10f;

        /// <summary>The outline of the bat in frame <paramref name="frame"/>: each wing turns about its shoulder, up and then down again.</summary>
        private static void batPolygon(int frame, out float[] xs, out float[] ys)
        {
            int n = bat_outline.Length / 2;
            xs = new float[n];
            ys = new float[n];

            float flap = MathF.Cos(frame * MathF.Tau / BatFrames);
            float angle = flap > 0f ? flap * 0.55f : flap * 0.4f;

            for (int i = 0; i < n; i++)
            {
                float x = bat_outline[i * 2], y = bat_outline[i * 2 + 1];
                float d = MathF.Abs(x);
                float sign = x < 0f ? -1f : 1f;
                float w = Math.Clamp((d - 0.12f) / 0.33f, 0f, 1f);
                w = w * w * (3f - 2f * w); // (the part near the body stays, the tips turn the most)
                float a = angle * w;
                float dx = d - bat_shoulder_x, dy = y - bat_shoulder_y;
                float c = MathF.Cos(a), s = MathF.Sin(a);

                xs[i] = sign * (bat_shoulder_x + dx * c + dy * s);
                ys[i] = bat_shoulder_y - dx * s + dy * c;
            }
        }

        private static Image<Rgba32> makeBat(int frame)
        {
            const int size = 256;
            batPolygon(frame, out var xs, out var ys);
            int n = xs.Length;
            float pixel = 2f * BatExtent / size;
            var image = new Image<Rgba32>(size, size);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = ((px + 0.5f) / size * 2f - 1f) * BatExtent;
                    float y = ((py + 0.5f) / size * 2f - 1f) * BatExtent;
                    bool inside = false;
                    float best = float.MaxValue;

                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        float ax = xs[j], ay = ys[j], bx = xs[i], by = ys[i];

                        if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax)
                            inside = !inside;

                        float ex = bx - ax, ey = by - ay;
                        float t = Math.Clamp(((x - ax) * ex + (y - ay) * ey) / Math.Max(ex * ex + ey * ey, 1e-9f), 0f, 1f);
                        float qx = ax + ex * t - x, qy = ay + ey * t - y;
                        best = Math.Min(best, qx * qx + qy * qy);
                    }

                    float sd = MathF.Sqrt(best) * (inside ? -1f : 1f); // (negative inside)
                    float coverage = Math.Clamp(0.5f - sd / pixel, 0f, 1f);
                    float depth = -sd;

                    // inside: a white outline, then white that fades away towards the black middle; outside: a faint white glow
                    const float line = 0.04f;
                    float inner = Math.Clamp((depth - line) / 0.30f, 0f, 1f);
                    inner = inner * inner * (3f - 2f * inner);
                    float body = 0.55f * MathF.Pow(1f - inner, 1.5f);
                    float outline = Math.Clamp((line - depth) / pixel + 0.5f, 0f, 1f);
                    float level = body + (1f - body) * outline;
                    float halo = sd > 0f ? 0.3f * MathF.Exp(-sd / 0.04f) : 0f;
                    float alpha = coverage + (1f - coverage) * halo;
                    float premultiplied = level * coverage + halo * (1f - coverage);
                    byte grey = (byte)(Math.Clamp(alpha > 1e-4f ? premultiplied / alpha : 0f, 0f, 1f) * 255f);

                    image[px, py] = new Rgba32(grey, grey, grey, (byte)(alpha * 255f));
                }
            }

            return image;
        }


        // ---- Sunny Milk's prism ----

        /// <summary>How far from the middle of the prism picture its corners are, as a share of half the picture.</summary>
        public const float PrismCorner = 0.94f;

        private static Image<Rgba32> makePrism()
        {
            const int size = 512;
            var image = new Image<Rgba32>(size, size);
            float circumradius = PrismCorner;
            float inradius = circumradius * 0.5f;
            float pixel = 2f / size;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size * 2f - 1f;
                    float y = (py + 0.5f) / size * 2f - 1f;
                    float depth = float.MaxValue; // (how far inside the triangle: the distance to the nearest side)

                    for (int k = 0; k < 3; k++)
                    {
                        float a = -MathF.PI / 2f + k * MathF.Tau / 3f + MathF.PI / 3f;
                        depth = Math.Min(depth, inradius - (x * MathF.Cos(a) + y * MathF.Sin(a)));
                    }

                    float coverage = Math.Clamp(0.5f + depth / pixel, 0f, 1f);

                    if (coverage <= 0f)
                    {
                        image[px, py] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    // glass: clearer in the middle, brighter towards the sides, with a bright edge and a soft glint across it
                    float rim = 1f - Math.Clamp(depth / 0.035f, 0f, 1f);
                    float edge = Math.Clamp(1f - depth / 0.35f, 0f, 1f);
                    float glint = MathF.Max(0f, 1f - MathF.Abs((x + y * 0.6f) + 0.15f) / 0.12f) * 0.25f;
                    float alpha = Math.Clamp((0.16f + 0.30f * edge * edge + glint) * coverage + rim * coverage, 0f, 1f);
                    float whiteness = Math.Clamp(0.70f + 0.30f * rim + 0.2f * edge, 0f, 1f);

                    image[px, py] = new Rgba32((byte)(255 * (0.78f + 0.22f * whiteness)), (byte)(255 * (0.90f + 0.10f * whiteness)), 255, (byte)(alpha * 255f));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeOrb()
        {
            const int size = 512;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float rho = MathF.Sqrt(dx * dx + dy * dy);
                    float edge = Math.Clamp((1f - rho) * size / 2f / 1.5f, 0f, 1f); // (a sharp edge: a pixel and a half wide)

                    image[x, y] = new Rgba32(255, 255, 255, (byte)(edge * 255f));
                }
            }

            return image;
        }

        /// <summary>The glow of a small bullet's body: its alpha is <c>(1 - distance) ^ 1.4</c> (distance as a share of the radius).</summary>
        private static Image<Rgba32> makeOrbGlow()
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float rho = MathF.Sqrt(dx * dx + dy * dy);
                    float a = rho >= 1f ? 0f : MathF.Pow(1f - rho, 1.4f);

                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f + 0.5f), 0, 255));
                }
            }

            return image;
        }

        private static Image<Rgba32> makePane()
        {
            const int width = 128, height = 32;
            var image = new Image<Rgba32>(width, height);
            float[] glints = { 0.14f, 0.5f, 0.83f };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    float edge = MathF.Exp(-MathF.Pow(Math.Min(v, 1f - v) / 0.09f, 2f)); // (bright along both long edges)
                    float glint = 0f;

                    foreach (float centre in glints)
                    {
                        float away = Math.Clamp(MathF.Abs(u + 0.3f * (v - 0.5f) - centre) / 0.045f, 0f, 1f);
                        glint = Math.Max(glint, 1f - away * away * (3f - 2f * away));
                    }

                    float a = 0.2f + 0.55f * edge + 0.22f * glint;
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>The outline of a horse's head (looking right, from the neck at the bottom left over the ear to the muzzle at the right), with a soft edge and an eye left open.</summary>
        private static Image<Rgba32> makeHorseHead()
        {
            const int size = 512;
            var image = new Image<Rgba32>(size, size);

            Vector2[] outline =
            {
                new Vector2(-0.95f, 0.98f), new Vector2(-0.88f, 0.48f), new Vector2(-0.72f, 0.06f), new Vector2(-0.52f, -0.28f), new Vector2(-0.40f, -0.44f), new Vector2(-0.31f, -0.93f),
                new Vector2(-0.14f, -0.52f), new Vector2(0.06f, -0.42f), new Vector2(0.30f, -0.22f), new Vector2(0.60f, 0.08f), new Vector2(0.82f, 0.30f), new Vector2(0.93f, 0.42f),
                new Vector2(0.96f, 0.58f), new Vector2(0.88f, 0.70f), new Vector2(0.72f, 0.72f), new Vector2(0.62f, 0.80f), new Vector2(0.50f, 0.74f), new Vector2(0.30f, 0.52f),
                new Vector2(0.10f, 0.42f), new Vector2(-0.05f, 0.50f), new Vector2(-0.15f, 0.62f), new Vector2(-0.10f, 0.80f), new Vector2(-0.05f, 0.98f),
            };

            Vector2 eye = new Vector2(0.16f, -0.14f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f - size / 2f) / (size / 2f), (y + 0.5f - size / 2f) / (size / 2f));
                    float inside = Math.Clamp(-Bullets.PolygonShape.SignedDistance(p, outline) / 0.015f, 0f, 1f);
                    float open = Math.Clamp((0.055f - (p - eye).Length) / 0.015f, 0f, 1f);

                    image[x, y] = new Rgba32(255, 255, 255, (byte)(255f * inside * (1f - open)));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeGlow()
        {
            const int size = 128;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float rho = MathF.Sqrt(dx * dx + dy * dy);
                    float a = rho >= 1f ? 0f : (1f - rho) * (1f - rho);
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>
        /// The apex of the wedge is at the left edge; the wedge fills the picture's height at the right edge. The part before <paramref name="start"/> (where the cone really starts: the
        /// UFO's width wide, see <see cref="ConeStartFor"/>) is empty, from there the wedge fades out towards the far end.
        /// </summary>
        private static Image<Rgba32> makeCone(float start)
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = ((y + 0.5f) / size - 0.5f) * 2f;
                    float a = 0f;

                    if (u >= start)
                    {
                        float inside = Math.Clamp((u - MathF.Abs(v)) * 14f, 0f, 1f);
                        float along = 1f - (u - start) / (1f - start);
                        a = inside * 0.6f * MathF.Pow(Math.Clamp(along, 0f, 1f), 1.3f);
                    }

                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeCloud()
        {
            const int size = 128;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float edge = Math.Clamp((1f - MathF.Sqrt(dx * dx + dy * dy)) * 2f, 0f, 1f);

                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(edge * edge * 255f), 0, 255));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeBloodPool()
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            // (a vampire's jaw: two long fangs above with two small teeth between them, and four shorter fangs below pointing up between and beside them; all well inside the pool, which is narrow in places)
            Vector2[] tooth(float x, float halfWidth, float baseY, float tipY) => new[] { new Vector2(x - halfWidth, baseY), new Vector2(x + halfWidth, baseY), new Vector2(x, tipY) };

            Vector2[][] teeth =
            {
                tooth(-0.24f, 0.10f, -0.40f, 0.18f), tooth(0.24f, 0.10f, -0.40f, 0.18f),
                tooth(-0.07f, 0.06f, -0.40f, -0.24f), tooth(0.07f, 0.06f, -0.40f, -0.24f),
                tooth(-0.12f, 0.07f, 0.42f, 0.10f), tooth(0.12f, 0.07f, 0.42f, 0.10f),
                tooth(-0.36f, 0.07f, 0.36f, 0.08f), tooth(0.36f, 0.07f, 0.36f, 0.08f),
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f - size / 2f) / (size / 2f), (y + 0.5f - size / 2f) / (size / 2f));
                    float angle = MathF.Atan2(p.Y, p.X);

                    // (not a circle: the edge goes in and out, in waves of different lengths)
                    float edge = 0.8f + 0.09f * MathF.Sin(angle * 3f + 0.7f) + 0.06f * MathF.Sin(angle * 5f + 2.1f) + 0.04f * MathF.Sin(angle * 9f + 4f);
                    float inside = edge - p.Length;

                    if (inside <= 0f)
                        continue;

                    float fang = float.MaxValue;

                    foreach (var one in teeth)
                        fang = Math.Min(fang, Bullets.PolygonShape.SignedDistance(p, one));

                    Rgba32 colour = inside < 0.07f ? new Rgba32(235, 30, 40, 255) : new Rgba32((byte)(120f - 60f * inside), 4, 12, 255);

                    if (fang < 0f)
                        colour = fang > -0.025f ? new Rgba32(120, 20, 30, 255) : new Rgba32(245, 240, 232, 255);

                    colour.A = (byte)(255f * Math.Clamp(inside * size * 0.5f, 0f, 1f));
                    image[x, y] = colour;
                }
            }

            return image;
        }

        private static Image<Rgba32> makeWedge()
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = ((y + 0.5f) / size - 0.5f) * 2f;
                    float inside = Math.Clamp((u - MathF.Abs(v)) * 14f, 0f, 1f);
                    float a = inside * 0.5f * MathF.Pow(Math.Clamp(1f - u, 0f, 1f), 0.8f);
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        private static Image<Rgba32> makeLight()
        {
            const int size = 256;
            var image = new Image<Rgba32>(size, size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = ((y + 0.5f) / size - 0.5f) * 2f;
                    float across = Math.Clamp((1f - MathF.Abs(v)) * 10f, 0f, 1f);
                    float a = across * 0.6f * MathF.Pow(1f - u, 1.3f);
                    image[x, y] = new Rgba32(255, 255, 255, (byte)Math.Clamp((int)(a * 255f), 0, 255));
                }
            }

            return image;
        }

        /// <summary>Where along the picture of a cone with this half angle (0..1) the cone itself starts: the distance of the apex from the UFO as a part of the whole length of the picture.</summary>
        public static float ConeStartFor(float halfAngleDegrees)
        {
            float apex = DanmakusuBalance.NUE_UFO_WIDTH * 0.5f / MathF.Tan(halfAngleDegrees * MathF.PI / 180f);
            return apex / (apex + DanmakusuBalance.NUE_CONE_LENGTH);
        }

        /// <summary>Only what is inside the rectangle <paramref name="min"/>..<paramref name="max"/> (the field's coordinates) is shown (a clipped layer only).</summary>
        public void SetClip(Vector2 min, Vector2 max)
        {
            if (clip == null || content == null)
                return;

            clip.Position = min;
            clip.Size = Vector2.ComponentMax(max - min, Vector2.Zero);
            content.Position = -min;
        }

        /// <summary>Starts a new frame: every sprite that is not added again is hidden.</summary>
        public void Begin()
        {
            used = 0;
            arcUsed = 0;
        }

        public void Add(StageTexture texture, Vector2 position, Vector2 size, float degrees, float alpha, Color4 colour, bool additive = false)
        {
            if (alpha <= 0.002f)
                return;

            if (used >= pool.Length)
            {
                if (!Grows)
                    return;

                grow();
            }

            var sprite = pool[used++];
            sprite.Texture = textures.TryGetValue(texture, out var picture) ? picture : null;
            sprite.Position = position;
            sprite.Size = size;
            sprite.Rotation = degrees;
            sprite.Alpha = Math.Min(alpha, 1f);
            sprite.Colour = colour;
            sprite.Blending = additive ? BlendingParameters.Additive : BlendingParameters.Inherit;
        }

        /// <summary>Ends the frame.</summary>
        public void End()
        {
            for (int i = used; i < pool.Length; i++)
                pool[i].Alpha = 0;

            if (arcs != null)
            {
                for (int i = arcUsed; i < arcs.Length; i++)
                    arcs[i].Alpha = 0;
            }
        }
    }
}
