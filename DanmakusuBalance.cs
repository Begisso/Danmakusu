using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// Every difficulty -> bullet mapping lives here so the whole ruleset can be tuned in one place.
    /// All distances are osu!pixels (playfield is 512x384), all speeds are pixels per second.
    /// </summary>
    public static class DanmakusuBalance
    {
        /// <summary>
        /// OD 0..10 -> bullets per ring (hit circles and slider heads), depending on the hitsound class:
        /// normal 1..20, clap (ellipses) 1..15, whistle (homing arrowheads) 1..5, finish (stars) 1..10.
        /// </summary>
        public static int BulletsPerRing(double overallDifficulty, BulletKind kind, bool kiai = false)
        {
            // in kiai the part of the count that grows with the OD grows half as fast (the minimum stays)
            double t = Math.Clamp(overallDifficulty, 0, 10) / 10 * (kiai ? KIAI_COUNT_SCALING_FACTOR : 1.0);

            return kind switch
            {
                BulletKind.Normal => (int)Math.Round(1 + 19 * t),
                BulletKind.Clap => (int)Math.Round(1 + 14 * t),
                BulletKind.Whistle => (int)Math.Round(1 + 4 * t),
                _ => (int)Math.Round(1 + 9 * t),
            };
        }

        /// <summary>During kiai the OD scaling of the number of bullets of a ring (normal 1..20, clap 1..15, whistle 1..5, finish 1..10) is multiplied by this.</summary>
        public const double KIAI_COUNT_SCALING_FACTOR = 0.5;

        /// <summary>The most laser beams a slider can fire.</summary>
        public const int MAX_BEAMS_PER_LASER = 3;

        /// <summary>OD 0..10 -> number of laser beams a whistled slider fires (half of the original 1..10, capped at 3).</summary>
        public static int BeamsPerLaser(double overallDifficulty)
            => Math.Clamp((int)Math.Round((1 + 9 * Math.Clamp(overallDifficulty, 0, 10) / 10) / 2), 1, MAX_BEAMS_PER_LASER);

        /// <summary>
        /// Hitsound volume (0..100%) -> multiplier (0.1x..1x) on the number of bullets of a ring and on their speed.
        /// </summary>
        public static float VolumeScale(int volumePercent)
            => 0.1f + 0.9f * Math.Clamp(volumePercent, 0, 100) / 100f;

        /// <summary>Hitsound volume (0..100%) -> multiplier (0.5x..1x) on the size of the bullets of a ring.</summary>
        public static float VolumeSizeScale(int volumePercent)
            => 0.5f + 0.5f * Math.Clamp(volumePercent, 0, 100) / 100f;

        /// <summary>Slider ticks, arrows and ends fire half as many bullets as a normal ring (1..10).</summary>
        public static int BulletsPerSliderTick(double overallDifficulty, bool kiai = false)
            => Math.Max(1, (int)Math.Round(BulletsPerRing(overallDifficulty, BulletKind.Normal, kiai) / 2.0, MidpointRounding.AwayFromZero));

        /// <summary>OD -> number of spiral arms of a spinner (2..5). Unchanged by the ring counts above.</summary>
        public static int SpiralArms(double overallDifficulty)
            => Math.Clamp((int)Math.Round((5 + Math.Clamp(overallDifficulty, 0, 10) * 2) / 5.0), 2, 5);

        /// <summary>
        /// Time between two spiral shots (the rings of stars of spinners and the boss): OD 0 = every beat (1/1), OD 10 = every 1/8 beat. The shots
        /// per beat grow linearly with OD (1..8). Never shorter than 8 ms.
        /// </summary>
        public static double SpiralInterval(double beatLength, double overallDifficulty)
            => Math.Max(8, beatLength / (1 + 7 * Math.Clamp(overallDifficulty, 0, 10) / 10));

        /// <summary>Angle (radians) the spiral turns between two consecutive shots.</summary>
        public const float SPIRAL_STEP = 0.28f;

        /// <summary>Spiral bullets appear this far (osu!pixels) from the spinner centre.</summary>
        public const float SPIRAL_SPAWN_RADIUS = 14f;

        /// <summary>Hitfinish bullets bounce off the border of the screen... (this is the area until the real screen is known: the osu! playfield)</summary>
        public const float RICOCHET_WIDTH = 512f;

        public const float RICOCHET_HEIGHT = 384f;

        /// <summary>...this many times; after that they fly on through the next wall they touch and are gone when they have left the screen.</summary>
        public const int MAX_RICOCHETS = 1;

        /// <summary>A ricochet star that has bounced is drawn this much darker (0.5: half as bright).</summary>
        public const float RICOCHET_SHADE = 0.5f;

        /// <summary>
        /// Bullets are simulated this far (osu!pixels) beyond the 512x384 playfield, so they keep flying until they have
        /// really left the screen (the screen is wider/taller than the playfield). Drawing is culled to the real screen.
        /// </summary>
        public const float OFFSCREEN_MARGIN = 1000f;

        /// <summary>true = a bullet vanishes when it hits the player; false = it keeps flying (it just cannot hit again).</summary>
        public const bool HIDE_BULLETS_AFTER_HIT = true;

        /// <summary>Slider tick rings spawn at this fraction of the slider's circle radius.</summary>
        public const float TICK_SPAWN_RADIUS_FACTOR = 0.25f;

        /// <summary>Bullets darker than this perceived brightness are blended towards gray so they stay visible.</summary>
        private const float min_brightness = 0.35f;

        private const float gray_level = 0.65f;

        /// <summary>Border colour that contrasts with the bullet: dark for bright bullets, near-white for dark ones.</summary>
        public static Color4 BorderColour(Color4 bullet)
        {
            float brightness = 0.2126f * bullet.R + 0.7152f * bullet.G + 0.0722f * bullet.B;

            if (brightness > 0.5f)
                return new Color4(bullet.R * 0.3f, bullet.G * 0.3f, bullet.B * 0.3f, 1f);

            return new Color4(bullet.R + (1f - bullet.R) * 0.8f, bullet.G + (1f - bullet.G) * 0.8f, bullet.B + (1f - bullet.B) * 0.8f, 1f);
        }

        /// <summary>Combo colour -> bullet colour. (Near) black becomes gray, dark colours are lifted towards gray.</summary>
        public static Color4 BulletColour(Color4 comboColour)
        {
            float brightness = 0.2126f * comboColour.R + 0.7152f * comboColour.G + 0.0722f * comboColour.B;

            if (brightness >= min_brightness)
                return new Color4(comboColour.R, comboColour.G, comboColour.B, 1f);

            float t = 1f - brightness / min_brightness;
            return new Color4(
                comboColour.R + (gray_level - comboColour.R) * t,
                comboColour.G + (gray_level - comboColour.G) * t,
                comboColour.B + (gray_level - comboColour.B) * t,
                1f);
        }

        /// <summary>CS 0..10 -> bullet radius 4..11. Higher CS = bigger bullets (the opposite of standard hit circles).</summary>
        /// <summary>
        /// Gengetsu: her own double rings (this many basic bullets, every this many beats, the inner ring this much slower). In her attacks every bullet on the screen speeds up evenly to this many times its speed over the attack (back to its own when it ends);
        /// every ring is shot again this many beats later; every this many-th bullet is this many times as big; every this many-th beam is this many times as thick.
        /// </summary>
        public const int GENGETSU_RING_BULLETS = 16;

        public const double GENGETSU_RING_EVERY_BEATS = 1;
        public const float GENGETSU_INNER_SPEED = 0.75f;
        public const float GENGETSU_SPEED_UP = 2.5f;

        /// <summary>
        /// Gengetsu's attacks, growing evenly over each: the background dims grey up to this much; every bullet blurs towards the middle of the screen with this many faint copies, the last this share of
        /// the way there, the first this visible; a quiet riser plays from this pitch and loudness to these.
        /// </summary>
        public const float GENGETSU_DIM = 0.5f;

        public const int GENGETSU_BLUR_COPIES = 1;
        public const float GENGETSU_BLUR_REACH = 0.06f;
        public const float GENGETSU_BLUR_ALPHA = 0.0875f;
        public const double GENGETSU_RISER_PITCH_FROM = 0.6, GENGETSU_RISER_PITCH_TO = 1.8;
        public const double GENGETSU_RISER_VOLUME_FROM = 0.15, GENGETSU_RISER_VOLUME_TO = 0.7;
        public const int GENGETSU_SPEED_LINES = 80; // (at the end of an attack; fewer before)
        public const double GENGETSU_SPEED_LINE_MS = 50; // (new lines this often)
        public const float GENGETSU_SPEED_LINE_ALPHA = 0.4f;
        public const double GENGETSU_AGAIN_BEATS = 0.125;
        public const int GENGETSU_BIG_EVERY = 4;
        public const float GENGETSU_BIG_SIZE = 1.2f;
        public const int GENGETSU_THICK_EVERY = 4;
        public const float GENGETSU_THICK = 2f;

        /// <summary>Mugetsu: her own rings of this many arrowheads (flying straight) every this many beats; in her attacks every this many-th ring is this many times as dense (and every bullet goes round her: see <see cref="MUGETSU_SWIRL"/>).</summary>
        /// <summary>
        /// A beam that the bosses steer gets a point between two of its points wherever the slice halfway between them is more than this many px off the line between them, or the two are further apart than this many times as
        /// far as they would be if nothing had happened to them (plus those px); halved again and again, at most this many times (LaserEmitter.refineBends): it bends where its slices go instead of being drawn straight across.
        /// </summary>
        public const float BEAM_BEND_PX = 6f;

        public const float BEAM_BEND_STRETCH = 1.5f;
        public const int BEAM_BEND_DEPTH = 4;


        /// <summary>
        /// Kaguya: every this many-th bullet out of the top falls as a star, its beam's slices living this many beats (when she has come back: the two _AGAIN values). Every this many beats this many rings of this many bullets this size, this
        /// many beats apart; rings go round their centre at this share of their speed. When she has come back the shapes change every this many beats, and every this many-th bullet is this many times as big. She stands this far from the top
        /// (from the edge of her magic circle). The moon's radius is this share of the screen's width and this share of the screen's height shows, coming down over this many beats; the background is this dark blue.
        /// </summary>
        public const int KAGUYA_EVERY = 4;

        public const int KAGUYA_EVERY_AGAIN = 2;
        public const double KAGUYA_TRACE_BEATS = 4;
        public const double KAGUYA_TRACE_AGAIN_BEATS = 8;
        public const double KAGUYA_BURST_EVERY_BEATS = 2;
        public const int KAGUYA_RINGS = 5; // (a ring that would come with the first of the next burst is left out: 4 a burst at 2 beats)
        public const int KAGUYA_RING_BULLETS = 20;
        public const double KAGUYA_RING_GAP_BEATS = 0.5; // (a ring every half beat: half as often as the quarter beat it was)
        public const float KAGUYA_RING_TURN_DEGREES = 6f; // (every ring of hers is turned this much further than the one before, counted over her section: the bursts spin; a third of the gap between two of 20)
        public const float KAGUYA_SPIN = 0.5f;
        public const double KAGUYA_SHAPE_BEATS = 4;
        public const int KAGUYA_BIG_EVERY = 20;
        public const float KAGUYA_BIG = 3f;
        public const float KAGUYA_TOP_GAP = 16f;
        public const float KAGUYA_MOON_SIZE = 0.9f;
        public const float KAGUYA_MOON_SHOWN = 0.2f;
        public const double KAGUYA_MOON_BEATS = 16; // (how long it takes to come down)
        public const double KAGUYA_MOON_LEAVE_BEATS = 2; // (how long the sky takes to turn dark, and to clear when she goes)
        public const float KAGUYA_FALL_DEGREES = 3f;
        public const float KAGUYA_BACKGROUND_ALPHA = 0.8f;

        /// <summary>
        /// Miko: a set of this many cones every this many beats, taking bullets for this many beats, lit this many beats (this visible at first, brightening over the last this many), then beams for this many, fading over this many; what
        /// was pulled in comes out as this many rings, this many beats apart, of at most this many bullets each. Every this many beats this many bullets this many times the size. Pulled and big bullets bloom this much; while the beams are
        /// there the background is this dark and the bullets this dim, and the glow round a beam is this many times its width.
        /// </summary>
        public const int MIKO_CONES = 30;

        public const double MIKO_EVERY_BEATS = 8;
        public const double MIKO_TAKE_BEATS = 6;
        public const double MIKO_CONE_BEATS = 8;
        public const float MIKO_CONE_ALPHA = 0.175f;

        /// <summary>Miko: a cone comes up this visible (half of <see cref="MIKO_CONE_ALPHA"/>) and grows back to that, evenly, by the time it starts to brighten.</summary>
        public const float MIKO_CONE_START_ALPHA = MIKO_CONE_ALPHA / 2f;
        public const double MIKO_BRIGHTEN_BEATS = 2;
        public const double MIKO_BEAM_BEATS = 4;
        public const double MIKO_FADE_BEATS = 1;
        public const int MIKO_RINGS = 5;
        public const double MIKO_RING_GAP_BEATS = 0.5;
        public const int MIKO_RING_MOST = 64;
        public const double MIKO_BIG_EVERY_BEATS = 4;
        public const int MIKO_BIG_BULLETS = 16;
        public const float MIKO_BIG = 2f;
        public const float MIKO_BLOOM = 0.8f;
        public const float MIKO_BACKGROUND_ALPHA = 0.5f;
        public const float MIKO_DIM = 0.35f;
        public const float MIKO_HALO = 5f;

        /// <summary>
        /// Youmu: every this many-th bullet that leaves the screen is remembered; a slash every this many beats of an attack, see-through (this visible) for this many beats, solid and this many times as thick for this many, fading over
        /// this many; what it cuts falls at this many times less than Zanmu's pull; a ring of this many straight beams every this many beats, each this many circle radii long, at this many times a bullet's speed.
        /// </summary>
        public const int YOUMU_EVERY = 16;

        /// <summary>Youmu: the bullets her slashes go through are in this share of the screen, round its middle, and at least this many circle radii apart.</summary>
        public const float YOUMU_INNER = 0.85f;

        public const float YOUMU_APART_CIRCLES = 2f;

        public const double YOUMU_SLASH_EVERY_BEATS = 2;
        public const int YOUMU_MORE_SLASHES = 17; // (at every slash this many more, each as unlike the ones before it as the bullets allow)
        public const int YOUMU_WATCHED_MOST = 48; // (of the bullets she watches, this many nearest to the middle are looked at, two by two)
        public const float YOUMU_LINES_APART = 0.05f; // (a line so like one chosen already is not taken: there are none left that are different)
        public const float YOUMU_SLASH_WIDTH = 0.5f; // (how thin a slash is, of the beams of her rings)
        public const double YOUMU_SLASH_GHOST_BEATS = 4;
        public const float YOUMU_GHOST_ALPHA = 0.3f;
        public const double YOUMU_SLASH_SOLID_BEATS = 2;
        public const float YOUMU_SLASH_THICK = 2f;
        public const double YOUMU_SLASH_FADE_BEATS = 1;
        public const float YOUMU_PETAL_FALL = 8f;
        public const int YOUMU_RAYS = 16;
        public const double YOUMU_RAY_EVERY_BEATS = 2;
        public const float YOUMU_RAY_CIRCLES = 2f;
        public const float YOUMU_RAY_SPEED = 1f;

        /// <summary>
        /// Daiyousei: the background dims this much and the bullets glow this much; a ring that goes round lives this many times as long; a bullet that moves further than this in one step has come in from the other wall (it has not flown across).
        /// </summary>
        public const float DAIYOUSEI_BACKGROUND_ALPHA = 0.5f;

        public const float DAIYOUSEI_BLOOM = 0.7f;
        public const float DAIYOUSEI_SIZE = 1.25f; // (what is green is this many times as big)
        public const float DAIYOUSEI_LIFE = 3f;
        public const float DAIYOUSEI_JUMP = 150f;

        /// <summary>
        /// Wakasagihime: stars this much of their size, this share of the usual time apart; the background dims this much and everything turns this blue; what is fired swims this many circle radii to each side, once to and fro in
        /// this many beats; every this many-th bullet is a bubble this many times as big, this visible, at this share of its speed, that pops after this many beats into this many bullets; every this many-th bullet that leaves
        /// through the bottom grows seaweed this share of the screen high, swaying this many circle radii every this many beats, that comes off after this many beats and is gone this long after.
        /// </summary>
        public const float WAKASAGI_STAR_SIZE = 0.5f;

        public const double WAKASAGI_STAR_INTERVAL = 0.5;
        public const float WAKASAGI_BACKGROUND_ALPHA = 0.32f;
        public const float WAKASAGI_TINT = 0.6f;
        public const float WAKASAGI_WAVE_CIRCLES = 1f;
        public const double WAKASAGI_WAVE_BEATS = 4;

        /// <summary>What swims leaves a trace of beam behind it, this many beats of its way long, drawn through this many points, this much of a slider beam's width where it is, narrowing to this share of that at its end; it hurts like a slider body.</summary>
        public const double WAKASAGI_TRACE_BEATS = 2;
        public const int WAKASAGI_TRACE_STEPS = 16; // (a point every eighth of a beat: thirty-two to a wave of the swimming, so the trace follows it smoothly; its end glides, see TideAct.traceNow)
        public const float WAKASAGI_TRACE_WIDTH = 0.5f;
        public const float WAKASAGI_TRACE_END = 1f; // (as wide at its end as at the bullet)
        public const int WAKASAGI_EVERY = 8; // (every this many-th bullet out of the bottom grows seaweed)
        public const int WAKASAGI_BUBBLE_EVERY = 16;
        public const double WAKASAGI_BUBBLE_WARN_BEATS = 4;
        public const float WAKASAGI_BUBBLE_SIZE = 2f;
        public const float WAKASAGI_BUBBLE_SPEED = 0.5f;
        public const double WAKASAGI_BUBBLE_BEATS = 16;
        public const int WAKASAGI_POP_BULLETS = 8;
        public const float WAKASAGI_POP_SIZE = 0.5f; // (of the bullet the bubble was, hitbox too)
        public const float WAKASAGI_BLADE_HEIGHT = 0.25f;
        public const float WAKASAGI_BLADE_SHORTEST = 0.5f; // (every blade grows to its own length, between these shares of the height above, over this many beats)
        public const float WAKASAGI_BLADE_LONGEST = 1.5f;
        public const double WAKASAGI_BLADE_GROW_BEATS = 2;
        public const float WAKASAGI_BLADE_WIDTH = 2f; // (of a slider's beam)
        public const float WAKASAGI_BLADE_SWAY_CIRCLES = 0.8f;
        public const double WAKASAGI_BLADE_SWAY_BEATS = 4;
        public const double WAKASAGI_BLADE_BEATS = 8;
        public const double WAKASAGI_BLADE_GONE_MS = 4000;
        public const int WAKASAGI_PETALS = 6; // (green petals on every blade, on both sides by turns, from a fifth of the way up to near its tip: they hurt while on it, by their shape, and are not used up; bullets once it comes off the ground)
        public const float WAKASAGI_PETAL_SIZE = 1f; // (times a plain bullet)
        public const float WAKASAGI_PETAL_SPEED = 0.6f; // (times a plain bullet, each a little faster or slower)
        public const float WAKASAGI_PETAL_SWAY_CIRCLES = 0.3f; // (they flutter from side to side of their way by this many circle radii)

        /// <summary>
        /// Tojiko: every this many-th bullet that leaves through the top falls back this much of its size at this many times less than Zanmu's pull; a bolt every this many beats grows over this many, is see-through for this many
        /// (this visible) and solid for this many; hers come every this many beats of an attack and are solid for this many; a bolt has this many pieces; every this many-th point of a
        /// solid bolt dims the bullets this many circle radii round it, this much; this many clouds, this visible; the background dims this much.
        /// </summary>
        public const int TOJIKO_EVERY = 1;

        public const float TOJIKO_ELLIPSE_SIZE = 0.5f;
        public const float TOJIKO_FALL = 2f;
        public const float TOJIKO_RAIN_BRIGHT = 2f; // (the ellipses are this many times as bright as the bullet was)
        public const int TOJIKO_RAIN_DROPS = 1; // (every bullet that goes out of the top falls back as this many ellipses: one where it left, the others elsewhere along the top)
        public const int TOJIKO_IMPACT_BULLETS = 5; // (a bolt that turns solid throws this many bullets back from where it ends, over this many degrees, this much of a plain bullet's size)
        public const float TOJIKO_IMPACT_DEGREES = 120f;
        public const float TOJIKO_IMPACT_SIZE = 0.5f;
        public const double TOJIKO_BOLT_EVERY_BEATS = 2;
        public const double TOJIKO_GROW_BEATS = 1;
        public const double TOJIKO_GHOST_BEATS = 8;
        public const float TOJIKO_GHOST_ALPHA = 0.15f;
        public const double TOJIKO_SOLID_BEATS = 4;
        public const double TOJIKO_THUNDER_FADE_MS = 150; // (the thunder is heard only while its bolt is solid: it fades out over this long as the bolt stops hurting)
        public const double TOJIKO_HER_BOLT_EVERY_BEATS = 4;
        public const double TOJIKO_HER_SOLID_BEATS = 1;
        public const int TOJIKO_BOLT_POINTS = 7;
        public const float TOJIKO_ZIG_DEGREES = 20f; // (how far every stretch of a zigzag leans off the line, however long it is, give or take this share)
        public const float TOJIKO_ZIG_SPREAD = 0.5f;
        public const int TOJIKO_JAG_LEVELS = 2; // (every stretch of a bolt is broken in two this many times over, the new point thrown up to this much of the stretch to the side: the small kinks of real lightning)
        public const float TOJIKO_KINK = 0.18f;
        public const float TOJIKO_BRANCH_WIDTH = 0.35f; // (a branch is this much of the bolt's thickness, and this visible)
        public const float TOJIKO_BRANCH_ALPHA = 0.6f;
        public const float TOJIKO_BLOOM = 0.6f; // (how much the bolt glows)
        public const float TOJIKO_SHORTEST_ZIG = 0.1f; // (how long a stretch of a zigzag can be, before they are fitted to the bolt: most are short, a few long)
        public const float TOJIKO_LONGEST_ZIG = 3.7f;
        public const float TOJIKO_SOLID_WIDTH = 1.5f; // (a bolt that hurts is this many times as thick)
        public const int TOJIKO_BRANCHES = 5;
        public const int TOJIKO_SUB_BRANCHES = 1; // (off every branch)
        public const float TOJIKO_SUB_LENGTH = 0.5f; // (of the branch)
        public const int TOJIKO_SUB_POINTS = 3;
        public const float TOJIKO_BRANCH_LENGTH = 0.22f; // (of the bolt)
        public const float TOJIKO_BRANCH_DEGREES = 40f;
        public const int TOJIKO_BRANCH_POINTS = 5;
        public const int TOJIKO_DIM_EVERY = 4;
        public const float TOJIKO_DIM_CIRCLES = 1.5f;
        public const float TOJIKO_DIM = 0.5f;
        public const int TOJIKO_CLOUDS = 9;
        public const float TOJIKO_CLOUD_ALPHA = 0.95f;
        public const float TOJIKO_BACKGROUND_ALPHA = 0.55f;

        /// <summary>
        /// Yumemi: every this many-th bullet of an attack has a tail this many beats long; a pause pulls everything away from her this many times as hard as Zanmu's pull; a flower of this many petals every this many beats, the
        /// petals bulging out this share of its radius and taking this share of their part of the circle (the rest is a gap).
        /// </summary>
        public const int YUMEMI_TAIL_EVERY = 10;

        public const double YUMEMI_TAIL_BEATS = 4;
        public const float YUMEMI_PULL = 8f;
        public const int YUMEMI_PETALS = 5;
        public const double YUMEMI_FLOWER_EVERY_BEATS = 2;
        public const double YUMEMI_WARN_BEATS = 2; // (the big flash before she pushes everything away)
        public const float YUMEMI_WAVE_SPEED = 3f;
        public const float YUMEMI_WAVE_HALF_WIDTH = 68f;
        public const float YUMEMI_WAVE_ALPHA = 0.7f;
        public const float YUMEMI_PETAL_DEPTH = 0.35f;
        public const float YUMEMI_PETAL_SHARE = 0.85f;

        /// <summary>
        /// Eika: rings of this many ellipses every this many beats; a push every this many beats; at most this many bullets in a clump; a bullet can stick this long after it was fired, not before (they all start in one place);
        /// the cells of the grid are this many plain bullets wide.
        /// </summary>
        public const int EIKA_RING_BULLETS = 20;

        public const double EIKA_RING_EVERY_BEATS = 2;
        public const double EIKA_PUSH_EVERY_BEATS = 4;
        public const int EIKA_MAX_CLUMP = 8;
        public const double EIKA_ARM_MS = 300;
        public const float EIKA_CELL = 3f;

        /// <summary>A clump flies this many times as fast as the law of impulse gives it.</summary>
        public const float EIKA_CLUMP_SPEED = 1.5f;

        /// <summary>
        /// Ran: this many spiral beams, growing at this many times a bullet's speed, a point of the beam every this many px; rings of this many talismans every this many beats, at this many times their speed where they start and this
        /// many a playfield's length out.
        /// </summary>
        public const int RAN_BEAMS = 8;

        public const double RAN_FAN_EVERY_BEATS = 8; // (a new fan of spirals, each its own, every this many beats of an attack, from its start)
        public const float RAN_BEAM_SPEED = 6f;
        public const float RAN_BEAM_POINT_GAP = 24f;
        public const int RAN_BOUNCES = 2; // (a bullet bounces off her spirals this many times at most)
        public const int RAN_TALISMANS = 8;
        public const double RAN_TALISMAN_EVERY_BEATS = 1;
        public const float RAN_TALISMAN_FAST = 2f;
        public const float RAN_TALISMAN_SLOW = 0.25f;

        /// <summary>
        /// Futo: at the wall a bullet has this share of its speed; her boat (the user's picture, Resources/Textures/futo_boat.png: as wide as its picture is for its length) is this many circle radii from middle to tip and flies at
        /// this many times a bullet's speed. What gets into it slides out to its nearest edge at this many times the boat's and the bullet's own speed together (fast, but not at once), and flies on as fast as it flew.
        /// </summary>
        public const float FUTO_SPEED_AT_WALL = 0.3f;

        public const float FUTO_BOAT_CIRCLES = 10f; // (half its length: twice the 5 it was)
        public const float FUTO_BOAT_SPEED = 1.5f;
        public const float FUTO_PUSH_SPEED = 2f;

        /// <summary>
        /// Shion: everything turns round every this many beats, with a warning this many beats before: a red arrow this big (of the bullet's radius) on every this many-th bullet of a ring; hexagons of this many talismans every this many beats.
        /// </summary>
        public const double SHION_EVERY_BEATS = 8;

        public const double SHION_WARN_BEATS = 4;
        public const int SHION_ARROW_EVERY = 2;
        public const float SHION_ARROW_SIZE = 0.5f;
        public const double SHION_FLASH_BEATS = 0.5; // (one flash of her arrows is this many beats)
        public const int SHION_RING_BULLETS = 36;
        public const double SHION_RING_EVERY_BEATS = 2;

        /// <summary>
        /// Chimata: walls every this many beats, of talismans this many times as big as a plain bullet, this many of their drawn widths apart (1: side by side, the pictures touching); her rings' talismans this many times as big; a ring
        /// of this many talismans this many circle radii round the player every this many beats (half that in a spinner); what comes within this many circle radii of the player is a knife that creeps at this share of its speed.
        /// </summary>
        public const double CHIMATA_WALL_EVERY_BEATS = 8;

        public const float CHIMATA_WALL_TALISMAN_SIZE = 3f;
        public const float CHIMATA_WALL_SPACING = 1f;
        public const float CHIMATA_WALL_SPEED = 0.5f; // (her walls come in at this much of a plain bullet's speed)
        public const float CHIMATA_TALISMAN_SIZE = 1.5f;
        public const int CHIMATA_RING_BULLETS = 10;
        public const float CHIMATA_RING_CIRCLES = 5f;
        public const double CHIMATA_RING_EVERY_BEATS = 4;
        public const float CHIMATA_RING_SPEED = 0.5f; // (of a plain bullet's)
        public const float CHIMATA_NEAR_CIRCLES = 4f;
        public const float CHIMATA_KNIFE_SIZE = 0.5f;
        public const float CHIMATA_KNIFE_SPEED = 0.25f;

        /// <summary>
        /// Eiki: rings of this many talismans every this many beats; her circles (the one round the player and its mirror image through the middle of the screen) are this many circle radii from middle to edge, their lines
        /// this bright; what is sent at a middle flies at this share of its speed; bullets outside the player's are this dim.
        /// </summary>
        public const int EIKI_RING_BULLETS = 48;

        public const double EIKI_RING_EVERY_BEATS = 2;
        public const float EIKI_RADIUS_CIRCLES = 4.8f;
        public const float EIKI_RING_ALPHA = 0.7f;
        public const float EIKI_PULL_SPEED = 0.5f;
        public const float EIKI_DIM = 0.45f;

        /// <summary>
        /// Tenshi: every this many-th bullet that leaves through the bottom comes back as this many bullets, this much of its size, fanned out over this many degrees; a shockwave every this many beats, this many times as fast as
        /// Raiko's ripples, this many px to each side of its line, this bright.
        /// </summary>
        public const int TENSHI_EVERY = 16;

        public const int TENSHI_PIECES = 3;
        public const float TENSHI_SIZE = 0.5f;
        public const float TENSHI_FAN_DEGREES = 120f;
        public const double TENSHI_WAVE_EVERY_BEATS = 8;
        public const int TENSHI_WAVE_EVERY = 8; // (while a wave is on the screen every this many-th bullet that leaves where it has been comes back)
        public const float TENSHI_WAVE_SPEED = 2f;
        public const float TENSHI_WAVE_HALF_WIDTH = 16f;
        public const float TENSHI_WAVE_ALPHA = 0.55f;

        /// <summary>
        /// Hisami: this many rings at once of this many bullets, every this many beats; every this many beats of an attack everything within this many circle radii of the player is recorded; a preview is this visible and
        /// flies off this many beats later; a faint ring this bright shows the place; a slider's beam is fired again this many beats later.
        /// </summary>
        public const int HISAMI_RING_LAYERS = 4;

        public const int HISAMI_RING_BULLETS = 12;
        public const double HISAMI_RING_EVERY_BEATS = 2;
        public const double HISAMI_RECORD_EVERY_BEATS = 4;
        public const float HISAMI_REACH_CIRCLES = 5.66f;
        public const float HISAMI_PREVIEW_ALPHA = 0.35f;
        public const double HISAMI_PREVIEW_BEATS = 2;
        public const float HISAMI_RING_ALPHA = 0.9f;
        public const double HISAMI_RING_BEATS = 4; // (how long the ring stays)
        public const double HISAMI_RING_FADE_BEATS = 1; // (how long it takes to fade in)
        public const double HISAMI_BEAM_AGAIN_BEATS = 4;

        /// <summary>
        /// Chiyari: this many rings of pools of blood, this many pools a ring, the first ring this many circle radii from her and every next this many further; a pool's radius, in circle radii; a turn takes this many beats.
        /// </summary>
        public const int CHIYARI_RINGS = 3;

        public const int CHIYARI_POOLS = 3;
        public const float CHIYARI_RING_FIRST = 3f;
        public const float CHIYARI_RING_GAP = 2f;
        public const float CHIYARI_POOL_RADIUS = 1f;
        public const float CHIYARI_POOL_ALPHA = 1f;
        public const double CHIYARI_TURN_BEATS = 16;
        public const float CHIYARI_BAT_SPEED = 0.5f; // (her bats, which do not home, fly this many times as fast as what they were)

        /// <summary>
        /// Yuugi: a ring every this many beats, this many px to each side of its line (it moves at Raiko's ripple speed), this bright; what it touches goes this many times its speed right by her and this many at the distance of the
        /// wall, glowing this much right by her (less further out); her own bullets are this many times as big as plain ones.
        /// </summary>
        public const double YUUGI_RING_EVERY_BEATS = 8;

        public const float YUUGI_RING_HALF_WIDTH = 14f;
        public const float YUUGI_RING_ALPHA = 0.6f;
        public const float YUUGI_PUSH_NEAR = 2.5f;
        public const float YUUGI_PUSH_FAR = 1.2f;
        public const float YUUGI_BLOOM = 1f;
        public const float YUUGI_SIZE = 2f;

        /// <summary>
        /// Joon: every this many beats every this many-th bullet turns gold (at the end of an attack every this many-th), over this long; it bounces this many times, keeping this share of its energy; her own bullets are pulled at this many
        /// times less than Zanmu's pull. (Each bullet's gravity points the way it was going when it turned, or when she threw it.)
        /// </summary>
        public const double JOON_EVERY_BEATS = 2;
        public const double JOON_OWN_INTERVAL = 4; // (her own golden bullets come this many times as far apart as a boss's stars: a quarter as often; in a spinner every fourth star of it)

        public const int JOON_EVERY = 10;
        public const int JOON_EVERY_LAST = 4;
        public const double JOON_TURN_MS = 200;
        public const int JOON_BOUNCES = 2;
        public const float JOON_ENERGY_KEPT = 0.7f;
        public const float JOON_OWN_FALL = 4f;

        /// <summary>
        /// Yuuka: every this many-th bullet that leaves through the bottom grows a sunflower (at most this many stand at a time) that stands this many beats, grows and shrinks over this long, is this share of the screen's height
        /// tall (at random between the two), leans and bends up to half this share of it, has a head this many circle radii big and this many leaves, a stem this many circle radii thick, and flies up this many degrees to the side.
        /// </summary>
        public const int YUUKA_EVERY = 4;

        public const int YUUKA_MAX_FLOWERS = 48;
        public const double YUUKA_LIFE_BEATS = 8;
        public const double YUUKA_GROW_MS = 700;
        public const float YUUKA_HEIGHT_MIN = 0.16f;
        public const float YUUKA_HEIGHT_MAX = 0.36f;
        public const float YUUKA_LEAN = 0.07f;
        public const float YUUKA_FLOWER_CIRCLES = 0.8f;
        public const int YUUKA_PETALS = 16;
        public const float YUUKA_PETAL_SPEED = 0.5f;
        public const double YUUKA_WARN_BEATS = 1;
        public const float YUUKA_HEAD_SIZE = 1.1f; // (the head's radius, in petals)
        public const int YUUKA_LEAVES = 5;
        public const float YUUKA_STEM_WIDTH = 0.08f;
        public const float YUUKA_FLY_DEGREES = 5f;
        public const float YUUKA_SWAY_CIRCLES = 0.6f; // (how far the head of a sunflower sways to each side)
        public const double YUUKA_SWAY_BEATS = 8; // (once to the left and back to the right)

        /// <summary>
        /// Shizuha: every this many-th bullet of a ring turns; its leaf is this many times as big, falls at this many times less than Zanmu's pull, swaying about this many circle radii to each side, slides to its place at the
        /// bottom over this long and falls upwards this many degrees to the side.
        /// </summary>
        public const int SHIZUHA_EVERY = 2;

        public const float SHIZUHA_LEAF_SIZE = 1.2f;
        public const float SHIZUHA_FALL = 8f;
        public const float SHIZUHA_SWAY_CIRCLES = 0.5f;
        public const int SHIZUHA_FLOOR_LEAVES = 2; // (a leaf that gets to the bottom is this many lying there)
        public const double SHIZUHA_FLOOR_FADE_MS = 500; // (it fades out as it lands, and they fade in)
        public const float SHIZUHA_RISE_DEGREES = 5f;
        public const float SHIZUHA_TURN = 0.0012f; // (how fast a leaf turns as it falls, radians per ms; when it falls up each turns one to four times as fast as that)
        public const double SHIZUHA_RISE_SPREAD_BEATS = 8; // (they start to fall up within this many beats of each other)

        /// <summary>
        /// Saki: every bullet speeds up to this many times its speed over an attack; a ring of this many arcs, each this share of its part of the circle, every this many beats; the background dims brown this much,
        /// and the horse's head is this many screen heights big and this visible.
        /// </summary>
        public const float SAKI_SPEED_UP = 1.2f;

        /// <summary>Every this many beats of her attacks every this many-th bullet on the screen shoots two bullets this many times its size, one to its left and one to its right.</summary>
        public const double SAKI_SIDE_EVERY_BEATS = 4;

        public const int SAKI_SIDE_EVERY = 4;

        public const float SAKI_SIDE_SIZE = 0.5f;

        public const int SAKI_RING_ARCS = 6;
        public const float SAKI_RING_ARC_SHARE = 0.85f;
        public const double SAKI_RING_EVERY_BEATS = 4;
        public const float SAKI_BACKGROUND_ALPHA = 0.6f;
        public const float SAKI_HORSE_SCREEN = 1.15f;
        public const float SAKI_HORSE_ALPHA = 0.3f;

        /// <summary>
        /// Wriggle: every this many-th bullet is a firefly, this many times as big, flying on at this share of its speed, wandering about this many circle radii to each side (growing in over this long); it turns over this long,
        /// with this much bloom and a glow this bright and this many times as big as the bullet, which pulses (radians per ms).
        /// </summary>
        public const int WRIGGLE_EVERY = 4;

        public const float WRIGGLE_SIZE = 2f;
        public const float WRIGGLE_SPEED = 0.4f;
        public const float WRIGGLE_WANDER_CIRCLES = 0.75f;
        public const float WRIGGLE_WANDER_PACE = 2f; // (its waves go this many times as fast as a petal's sway: twice as restless)
        public const double WRIGGLE_WANDER_RISE_MS = 1000;
        public const double WRIGGLE_TURN_MS = 300;
        public const float WRIGGLE_BLOOM = 0.9f;
        public const float WRIGGLE_GLOW = 0.8f;
        public const float WRIGGLE_GLOW_SIZE = 3f;
        public const float WRIGGLE_PULSE = 0.006f;

        public const int MUGETSU_RING_BULLETS = 16;

        public const double MUGETSU_RING_EVERY_BEATS = 1;
        public const int MUGETSU_EVERY = 4;
        public const int MUGETSU_DENSITY = 3;

        /// <summary>
        /// Mugetsu's vortex: in her attacks every bullet goes round her at this share of its own speed (1: as fast round her as it flies), at any distance (Bullets/Swirl.cs); closer than this many px it is counted as this far
        /// (right by her the same speed round would be endlessly fast). The rings that meet her attacks live this many screen diagonals longer at their speed (going round they take longer to leave).
        /// </summary>
        public const float MUGETSU_SWIRL = 1f;

        public const float MUGETSU_SWIRL_NEAR = 16f;
        public const float MUGETSU_SWIRL_LONGER = 1f;

        /// <summary>Mokou comes back this many beats after the last boss section is over (if the map goes on for at least this many beats more), with her ball and her landings' bullets this many times as big.</summary>
        public const double REVIVAL_DELAY_BEATS = 8;

        public const double REVIVAL_MIN_BEATS = 8;

        /// <summary>A last section that goes on to the end of the map stops this many beats (and the beats before they come back) before it, if it can, for the boss who comes back.</summary>
        public const double REVIVAL_ROOM_BEATS = 32;

        /// <summary>
        /// Every bullet (of a ring or of an act) and every beam is gone this many beats after it came, whatever has slowed it or kept it on the screen (a bullet fades out over the last <see cref="LIFE_FADE_MS"/>).
        /// It took the place of "what crawls fades" (bullets slower than a tenth of a plain one faded after 32 beats) on 2026-11-08; what leaves the screen is gone when it has left it, as ever.
        /// </summary>
        public const double LIFE_MOST_BEATS = 64;

        public const double LIFE_FADE_MS = 500;
        public const float MOKOU_REVIVAL_SIZE = 2f;

        /// <summary>Luna Child's imitated Hidden: a circle fades out from this share of its approach on, over this share of it (as the Hidden mod's: 0.4 and 0.3).</summary>
        public const double LUNA_HIDDEN_FADE_IN_SHARE = 0.4;

        public const double LUNA_HIDDEN_FADE_OUT_SHARE = 0.3;

        /// <summary>Before an attack that is not where a boss appears there is no warning: over as long as the warning was, every bullet's glow pulses this many times (faster and faster), up to this much.</summary>
        public const double ATTACK_FLASHES = 6;

        public const float ATTACK_FLASH_BLOOM = 0.85f;

        /// <summary>
        /// Byakuren's homing beams: this many times as wide as a slider's beam, their heads this many times as fast as her plain bullets, turning towards the player for this many beats only (then straight on).
        /// A head that points away from the player turns at least this share of the full turn (the pull of a homing thing goes to nothing at exactly behind it, so it would fly straight on and off the screen).
        /// The star at a beam's tip is this many times a spinner star's size (hitbox too).
        /// </summary>
        public const float BYAKUREN_TRAIL_WIDTH = 1f;

        public const float BYAKUREN_TRAIL_SPEED = 2f;
        public const double BYAKUREN_HOMING_BEATS_LIMIT = 16;
        public const float BYAKUREN_TURN_FLOOR = 0.5f;
        public const float BYAKUREN_TIP_STAR_SIZE = 0.75f;

        /// <summary>The red arrow on the edge of the screen that points at a star of hers that is outside it: this many times as big as a plain bullet.</summary>
        public const float BYAKUREN_ARROW_SIZE = 1f;

        /// <summary>That arrow is this many times as big when the star is at the wall, growing over this share of the screen's height as it comes nearer, and it beats this many times a beat.</summary>
        public const float BYAKUREN_ARROW_NEAR_SIZE = 2f;

        public const float BYAKUREN_ARROW_FAR = 0.4f;
        public const float BYAKUREN_ARROW_BEATS = 2f;

        /// <summary>Every slice of her homing beams narrows to nothing over this last share of its time, so the beam ends in a point.</summary>
        public const double BYAKUREN_TRAIL_NARROW_SHARE = 0.5;

        /// <summary>Her passive: every this many-th ricochet star fired in her sections has a beam for a tail, this many beats long, this share of the star's radius wide where it meets the star, narrowing to nothing.</summary>
        public const int BYAKUREN_STAR_TAIL_EVERY = 4;

        public const double BYAKUREN_STAR_TAIL_BEATS = 8;
        public const float BYAKUREN_STAR_TAIL_WIDTH = 0.4f;

        /// <summary>
        /// Marisa's Master Spark: from this share of an osu! circle's radius at her it widens (an outer curve, ever more slowly) to this many circle radii from its middle to each side over the first this many circle radii
        /// of its length, and from there on it is straight; it turns at this share of a spinner beam's speed (0: not at all, it stays where it was aimed), staying a straight line. She stands still while it fires.
        /// While it grows it follows the player: the way it goes is chosen once it has grown (since 2026-11-08; it was aimed as it started). From then on it turns slowly towards the player, this many degrees a second,
        /// always the same way round (the way that was nearer when it began to turn). It comes out this many times as fast as a spinner's beam (so it has grown in half the time it took).
        /// </summary>
        public const float MARISA_SPARK_THICKNESS = 0.864f;

        public const float MARISA_SPARK_WIDE = 5.4f;
        public const float MARISA_SPARK_GROW = 10f;
        public const float MARISA_SPARK_SPIN = 0f;
        public const float MARISA_SPARK_TURN_DEGREES = 5f;
        public const float MARISA_SPARK_SPEED = 2f;

        /// <summary>Marisa: in her attacks every this many-th bullet is a star that ricochets.</summary>
        public const int MARISA_STAR_EVERY = 3;

        /// <summary>Marisa stands where a boss stands (opposite the player, or in the middle), but this share of the way from the middle of the screen (half as far).</summary>
        public const float MARISA_PLACE_SCALE = 0.5f;

        /// <summary>
        /// Enoko's traps: this many circle radii wide (they come like Flandre's eyes, three at a time, but this many times more rarely); a player within this share of a trap's radius (plus the hitbox)
        /// of an armed one is caught and held for this many beats; at most this many on the map; a trap that shuts or is pushed out fades over this long (ms).
        /// </summary>
        public const float ENOKO_TRAP_RADIUS = 2.25f;

        public const double ENOKO_RARER = 2;
        public const double ENOKO_TRAP_GAP_BEATS = 2; // (between the three traps of a set)
        public const double ENOKO_TRAP_LIFE_BEATS = 32; // (after this long from when it was put down a trap nobody stepped into snaps shut by itself)
        public const float ENOKO_TRAP_TRIGGER = 0.8f;
        public const double ENOKO_HOLD_BEATS = 2;
        public const int ENOKO_MAX_TRAPS = 6;
        public const double ENOKO_FADE_MS = 600;

        /// <summary>While a trap holds the character it has a red glow round it: this wide (px), this strong (0..1), coming and going over this long (ms).</summary>
        public const float ENOKO_HELD_GLOW_SIZE = 64f;

        public const float ENOKO_HELD_GLOW_ALPHA = 0.9f;
        public const double ENOKO_HELD_GLOW_FADE_MS = 120;

        /// <summary>
        /// Nemuno: rings of this many knives every this many beats, their blades this many times as long; in her attacks the bullets of every this many-th object come out where the player is and fall this many times
        /// more gently than ellipses. Every this many-th slider beam of her section is hers: it turns to the player over this many beats, its copies (this many on each side, this many half widths apart) slide out over
        /// this many beats, and this many beats later all of them fall.
        /// </summary>
        public const int NEMUNO_RING_BULLETS = 16;

        public const double NEMUNO_RING_EVERY_BEATS = 2;
        public const float NEMUNO_BLADE_STRETCH = 2f;
        public const int NEMUNO_OBJECT_EVERY = 5;
        public const float NEMUNO_FALL_GENTLER = 8f;
        public const int NEMUNO_BEAM_EVERY = 10;
        public const int NEMUNO_BEAMS_OF_EVERY = 4; // (this many of every NEMUNO_BEAM_EVERY slider beams are hers, spread evenly)
        public const double NEMUNO_AIM_BEATS = 1;
        public const int NEMUNO_COPIES_EACH_SIDE = 2;
        public const float NEMUNO_COPY_GAP = 10f;
        public const double NEMUNO_SPLIT_BEATS = 1;
        public const double NEMUNO_HOLD_BEATS = 2;

        /// <summary>Nemuno: when the copies have grown, every beam turns to aim at the player over this many beats (then flashes until it is thrown); the rings fired round the player start this many times as far out and fade in over this long (ms).</summary>
        public const double NEMUNO_REAIM_BEATS = 1;

        public const float NEMUNO_SPAWN_WIDER = 2f;
        public const double NEMUNO_FADE_IN_MS = 300;

        /// <summary>
        /// Sanae: in her attacks every ring has this many times as many bullets (64 at most), this much of their size, and a round one is a five-pointed star. Her own rings: this many ricochet stars, this much of a
        /// finish's size, in a star outline, every this many beats, the outline turning this many degrees a second (every second ring the other way). The background is dimmed this much green in her attacks, with a
        /// giant see-through star as high as the screen times this, this opaque.
        /// </summary>
        public const int SANAE_COUNT_FACTOR = 2;

        public const float SANAE_SIZE_FACTOR = 0.5f;
        public const int SANAE_RING_BULLETS = 30;
        public const float SANAE_STAR_SIZE = 0.5f;
        public const double SANAE_RING_EVERY_BEATS = 2;
        public const float SANAE_SPIN_DEGREES_PER_SECOND = 30f;
        public const float SANAE_BACKGROUND_ALPHA = 0.35f;
        public const float SANAE_STAR_SCREEN = 1.8f;
        public const float SANAE_STAR_ALPHA = 0.09f;

        /// <summary>Keyboard controls: the character moves this fast (playfield px per ms), this much of it while Shift is held.</summary>
        public const float KEYBOARD_SPEED = 0.4f;

        public const float KEYBOARD_SLOW = 0.5f;

        /// <summary>Every beat the game counts with is at most this many beats per minute.</summary>
        /// <summary>The fastest and the slowest tempo a boss counts with: her attacks and pauses and every mechanic of hers (see <see cref="BossBeat"/>).</summary>
        public const double BOSS_MAX_BPM = 240, BOSS_MIN_BPM = 120;

        /// <summary>
        /// The beat length (ms) a boss counts with: the tempo is halved as often as it is above <see cref="BOSS_MAX_BPM"/> and doubled as often as it is below <see cref="BOSS_MIN_BPM"/>, so it stays in proportion to the music
        /// (300 BPM is counted as 150, 250 as 125, 100 as 200, 60 as 120).
        /// </summary>
        public static double BossBeat(double beatLength)
        {
            if (!(beatLength > 0) || double.IsInfinity(beatLength))
                return beatLength;

            double shortest = 60000 / BOSS_MAX_BPM, longest = 60000 / BOSS_MIN_BPM;

            for (int guard = 0; beatLength < shortest - 1e-9 && guard < 32; guard++)
                beatLength *= 2;

            for (int guard = 0; beatLength > longest + 1e-9 && guard < 32; guard++)
                beatLength /= 2;

            return beatLength;
        }

        public const double MAX_BPM = 480;

        /// <summary>
        /// The beat length (ms) of a timing point as the game counts with it: a tempo above <see cref="MAX_BPM"/> is halved (as often as needed) until it fits, so it stays in proportion to the music
        /// (960 BPM is counted as 480, 600 as 300).
        /// </summary>
        public static double CapBeat(double beatLength)
        {
            if (!(beatLength > 0) || double.IsInfinity(beatLength))
                return beatLength;

            double least = 60000 / MAX_BPM;

            for (int guard = 0; beatLength < least - 1e-9 && guard < 32; guard++)
                beatLength *= 2;

            return beatLength;
        }

        public static float BulletRadius(double circleSize)
            => 4f + (float)Math.Clamp(circleSize, 0, 10) * 0.7f;

        /// <summary>AR 0..10 -> bullet speed. Higher AR = faster bullets. Piecewise-linear through AR5 like osu!'s DifficultyRange.</summary>
        public static float BaseBulletSpeed(double approachRate)
        {
            const float min = 90, mid = 200, max = 420;
            double ar = Math.Clamp(approachRate, 0, 10);
            return BULLET_SPEED_SCALE * (float)(ar < 5 ? min + (mid - min) * ar / 5 : mid + (max - mid) * (ar - 5) / 5);
        }

        /// <summary>
        /// Slider speed (the map's SliderMultiplier x the local SV multiplier) -> bullet speed multiplier: the square root of the slider
        /// speed relative to a standard map, clamped to 0.36..3.24. Standard map = 1.4 x 1.0 -> 1.0.
        /// </summary>
        public static float SliderSpeedFactor(double sliderMultiplier, double sliderVelocity)
            => (float)Math.Clamp(Math.Sqrt(Math.Max(sliderMultiplier * sliderVelocity, 0) / 1.4), 0.36, 3.24);

        /// <summary>
        /// The reference BPM is the map's own standard (most common) BPM, so AR gives the same base speed on every map.
        /// Sections faster/slower than that BPM change bullet speed with the square root of the ratio, clamped to 0.25..4.
        /// </summary>
        public static float LocalBpmFactor(double localBpm, double mainBpm)
        {
            double ratio = localBpm / Math.Max(mainBpm, 1);
            return (float)Math.Clamp(Math.Sqrt(ratio), 0.25, 4.0);
        }

        /// <summary>
        /// Fly-bys: a bullet within this many circle radii of the player flying at least this many times as fast as a plain bullet whooshes past (not one faster than the next number: that is a jump, not a flight), one of
        /// this many whooshes (Resources/Samples/fly_by_N.wav, cut from the "Fast Fly By" recording), never the same twice in a row, at most one every this many ms, played this many times at once (twice: twice as loud).
        /// </summary>
        public const float FLYBY_CIRCLES = 1.5f;

        public const float FLYBY_SPEED_FACTOR = 3f;
        public const float FLYBY_JUMP_FACTOR = 20f;
        public const int FLYBY_SOUNDS = 12; // (fly_by_0..6 from "Fast Fly By", 7..11 from "Whoosh Sound Effects - 2")
        public const double FLYBY_COOLDOWN_MS = 300;
        public const int FLYBY_COPIES = 2;

        /// <summary>A full bar of health is gone after this many bullets, or this many ticks of a slider body (every <see cref="SLIDER_DAMAGE_INTERVAL_MS"/>), whatever the map's HP drain.</summary>
        public const int DAMAGE_BULLET_HITS = 12;

        public const int DAMAGE_SLIDER_HITS = 8;

        /// <summary>The health a bullet that hits the player takes: a twelfth of the bar, on every map (the map's HP drain no longer counts; the parameter is kept for the callers).</summary>
        public static double DamagePerBullet(double drainRate) => 1.0 / DAMAGE_BULLET_HITS;

        /// <summary>The health a tick of a slider body (and of what hurts like one) takes: an eighth of the bar, on every map (a beam alone half of it, <see cref="LASER_DAMAGE_SCALE"/>).</summary>
        public static double DamagePerSliderTick(double drainRate) => 1.0 / DAMAGE_SLIDER_HITS;

        /// <summary>Whether slider contact damage also counts as a miss (combo break, accuracy loss, miss animation).</summary>
        public const bool SLIDER_DAMAGE_COUNTS_AS_MISS = true;

        /// <summary>Bullet size multiplier for objects with a finish hitsound and for the last object of a combo (stars).</summary>
        public const float FINISH_BULLET_SCALE = 2f;

        /// <summary>Size multiplier of the spinner's star bullets.</summary>
        public const float SPINNER_STAR_SCALE = 2f;

        /// <summary>The stars of spinners and bosses are drawn (and hit) at this fraction of <see cref="SPINNER_STAR_SCALE"/> (the laser beams keep their width).</summary>
        public const float SPINNER_STAR_SIZE_FACTOR = 0.75f;

        /// <summary>Bullet size multiplier for objects with a hitclap (drawn as an ellipse).</summary>
        public const float CLAP_BULLET_SCALE = 1.33f;

        /// <summary>Bullet size multiplier for objects with a hitwhistle (drawn as an arrowhead triangle).</summary>
        public const float WHISTLE_BULLET_SCALE = 1.66f;

        /// <summary>Short half-axis / long half-axis of a hitclap ellipse.</summary>
        public const float ELLIPSE_ASPECT = 0.65f;

        /// <summary>Star bullets (hitfinish and spinner) spin at this many radians per millisecond (about 0.8 turns per second).</summary>
        public const float STAR_SPIN_RATE = 0.005f;

        /// <summary>Every ring is rotated by this much (radians, 1 degree) per 1/8 of a beat since the first timing point; never reset.</summary>
        public const double RING_ANGLE_PER_EIGHTH = Math.PI / 180;

        /// <summary>
        /// Hitwhistle bullet outline: the classic Touhou arrowhead, traced from the reference sprite with its hollow inside filled in.
        /// A sharp tip, sides that swell out in a pointed arch, straight flanks, and two sharp shard-like back corners with a shallow
        /// notch between them. Corners in the bullet's own frame (x = direction of travel, y = across), counter-clockwise starting at
        /// the tip, scaled so the farthest corner is at distance 1 from the centre. The drawn texture and the hitbox both use this outline.
        /// </summary>
        public static readonly Vector2[] ARROWHEAD = createArrowhead();

        /// <summary>Hitfinish bullet outline: a sharp five-pointed star (a regular pentagram), one tip on +x, farthest tip at distance 1.</summary>
        public static readonly Vector2[] STAR = RingLayout.Corners(RingShape.Star);

        /// <summary>Half height / half length of the train carriage picture (320 x 89).</summary>
        public const float TRAIN_ASPECT = 89f / 320f;

        /// <summary>Half height / half length of the knife picture (256 x 69).</summary>
        public const float KNIFE_ASPECT = 69f / 256f;

        /// <summary>The rectangle of a train carriage in its own frame (x = the way it travels), half length 1.</summary>
        public static readonly Vector2[] TRAIN =
        {
            new Vector2(-1f, -TRAIN_ASPECT),
            new Vector2(1f, -TRAIN_ASPECT),
            new Vector2(1f, TRAIN_ASPECT),
            new Vector2(-1f, TRAIN_ASPECT),
        };

        /// <summary>Remilia: at the end of an attack the rings have this many times as many bullets, and turn this many times as fast.</summary>
        public static float REMILIA_MAX_FACTOR => Bosses.BossTuning.RemiliaMaxFactor.Value;

        /// <summary>Remilia: how much of the red the bullets show at the end of an attack.</summary>
        public const float REMILIA_BULLET_RED = 0.9f;

        /// <summary>Remilia: how much every bullet glows at the end of an attack (1 = the strongest bloom), and how long (ms) everything takes to go back to normal after it.</summary>
        public static float REMILIA_BLOOM => Bosses.BossTuning.RemiliaBloom.Value;

        public const double REMILIA_FADE_OUT_MS = 1500;

        /// <summary>Remilia: the screen shakes more and more during an attack, up to this many px at its end (and calms down again as everything else goes back to normal).</summary>
        public const float REMILIA_SHAKE_PX = 2f;

        /// <summary>Remilia: how visible the giant red moon in the background is at the end of an attack (opacity), and how big it is (a share of the screen's height).</summary>
        public const float REMILIA_MOON_ALPHA = 0.3f;

        public const float REMILIA_MOON_SIZE = 1.2f;

        /// <summary>Clownpiece's stars (and everything she shoots) are this many times as big as a spinner's.</summary>
        public const float CLOWNPIECE_SIZE_FACTOR = 1.5f;

        /// <summary>Sakuya: a knife fades in over this long (ms) when it appears round her.</summary>
        public const double SAKUYA_KNIFE_FADE_IN_MS = 250;

        /// <summary>Remilia: how dark red the background gets at the end of an attack (opacity).</summary>
        public const float REMILIA_BACKGROUND_ALPHA = 0.45f;

        /// <summary>Yukari: the extra bullets beside every bullet are turned this many degrees away from it.</summary>
        public const float YUKARI_SIDE_ANGLE_DEGREES = 10f;

        /// <summary>In her pauses this many bullets of every eight freeze.</summary>
        public const int YUKARI_FREEZE_OF_EIGHT = 3;

        /// <summary>Yukari: half the length of her train, in bullet radii, and its speed as a multiple of the bullet speed.</summary>
        public const float YUKARI_TRAIN_LENGTH_RADII = 56f;

        public const float YUKARI_TRAIN_SPEED_FACTOR = 6f;

        /// <summary>Yukari: her own patterns have this many times the usual bullets per ring and are shot this many times as often.</summary>
        public const float YUKARI_OWN_FACTOR = 0.5f;

        /// <summary>Yukari: how long (ms) a bullet takes to turn into a talisman when it freezes, and back into a bullet when it is let go.</summary>
        public const double YUKARI_MORPH_MS = 600;


        /// <summary>Yukari: the screen shakes while her train is on it (0..1, see BEAM_SHAKE_MIN / MAX: 0.5 is 1.5 px).</summary>
        public const float YUKARI_TRAIN_SHAKE = 0.5f;

        /// <summary>Yukari's train horn (the sample): how long after its start the train passes (where the pitch falls).</summary>
        public const double TRAIN_HORN_PASS_MS = 2900;

        /// <summary>Ringo: her own bullets are this much smaller (and twice as many).</summary>
        public const float RINGO_OWN_SIZE = 0.5f;

        /// <summary>Ringo: the two bullets behind every bullet are this far behind it (in bullet radii).</summary>
        public const float RINGO_BEHIND_SPACING = 2.4f;

        /// <summary>
        /// Ringo: every this many-th three (a bullet and the two behind it: every one) is joined by a beam this many bullet radii wide on each side, while all three of it are there, which sticks out this many bullet radii in front of the
        /// middle of the first and behind the middle of the last (the handle): out of the bullets, where it is seen (they are so close that between them only 0.4 radii of it shows).
        /// </summary>
        public const int RINGO_SKEWER_EVERY = 1;

        public const float RINGO_SKEWER_WIDTH = 0.3f;
        public const float RINGO_SKEWER_TIP = 1.8f;
        public const float RINGO_SKEWER_HANDLE = 3.5f;

        /// <summary>Ringo: a three is only on a skewer while no two of its neighbours are further apart than this many times the gap they were fired with (what has come apart has none: it ran across the screen).</summary>
        public const float RINGO_SKEWER_APART = 2.5f;

        /// <summary>Seiran: how many times a bullet is shot in a row, how far (degrees) the bullets at its sides are turned from it (one step per bullet), and how far apart the shots are (bullet radii, as Ringo's copies).</summary>
        public static int SEIRAN_SHOTS => Bosses.BossTuning.SeiranShots.Whole;

        /// <summary>Seiran's own bullets (arrowheads that fly straight) are this much of the size of a star: as big as a plain bullet.</summary>
        public const float SEIRAN_SIZE_FACTOR = 1f / (SPINNER_STAR_SCALE * SPINNER_STAR_SIZE_FACTOR);

        /// <summary>Yorihime: she repeats another boss for this many beats, after a gap of this many.</summary>
        public const double YORIHIME_REPEAT_BEATS = 16;

        public const double YORIHIME_REPEAT_GAP_BEATS = 4;

        /// <summary>Yorihime: what is shot again from her is this much of the size and speed of what left the screen; a beam is looked at for slices this often (ms; every second one comes back).</summary>
        public static float YORIHIME_RETURN_FACTOR => Bosses.BossTuning.YorihimeReturn.Value;

        public const double YORIHIME_SLICE_MS = 80;

        // Flandre: three clones turn round her; a red eye is put on the player's place and explodes eight beats later; what it blew up is shot again from the clones.

        /// <summary>The distance of her clones from her (circle radii), and how many beats they take for one turn; they turn the other way every <see cref="FLANDRE_TURN_EVERY_BEATS"/> beats.</summary>
        public const float FLANDRE_CLONE_DISTANCE = 2.5f;

        public const double FLANDRE_CLONE_TURN_BEATS = 8;

        public const double FLANDRE_TURN_EVERY_BEATS = 18;

        public const int FLANDRE_CLONES = 3;

        /// <summary>The eye explodes this many beats after it was put down; its explosion reaches this many circle radii.</summary>
        public static double FLANDRE_EYE_BEATS => Bosses.BossTuning.FlandreEyeBeats.Current;

        public static float FLANDRE_EYE_RADIUS => Bosses.BossTuning.FlandreEyeRadius.Value;

        /// <summary>Every this many beats she throws this many eyes, this many beats after each other (each is put where the player is when it is put down, and goes off <see cref="FLANDRE_EYE_BEATS"/> later).</summary>
        public const double FLANDRE_THROW_EVERY_BEATS = 8;

        public const int FLANDRE_EYES_PER_THROW = 3;
        public const double FLANDRE_EYE_GAP_BEATS = 2;

        /// <summary>What was blown up is shot again, once from every clone, this many beats after the shot of the clone before; at this much of the speed and the size it had.</summary>
        public static double FLANDRE_CLONE_ASYNC_BEATS => Bosses.BossTuning.FlandreAsyncBeats.Current;

        public static float FLANDRE_RESHOT_SPEED => Bosses.BossTuning.FlandreSpeed.Value;

        public static float FLANDRE_RESHOT_SIZE => Bosses.BossTuning.FlandreSize.Value;

        /// <summary>Every this many-th thing that was blown up also sends four bullets out of the place it was at (right, down, left, up).</summary>
        public static int FLANDRE_CROSS_EVERY => Bosses.BossTuning.FlandreCross.Whole;

        /// <summary>How long (ms) the explosion's flash lasts, how often (ms) a beam is looked at for slices that the explosion has to take, and how long the eye takes to appear.</summary>
        public const double FLANDRE_BLAST_MS = 450;

        public const double FLANDRE_SLICE_MS = 40;

        public const double FLANDRE_EYE_IN_MS = 300;

        /// <summary>The approach circle of an eye starts this many times its radius further out (as a hit circle's approach circle starts at 4x: 1 + 3).</summary>
        public const float FLANDRE_APPROACH_SCALE = 3f;

        /// <summary>How many bullets the explosions of one act may shoot again (the rest are only blown up).</summary>
        public const int FLANDRE_MAX_SHOTS = 50000;

        // Rumia: she throws a ring of invisible circles every 8 beats (like Sannyo's smoke) that hide what is inside them; her own bullets are rings of plain bullets that turn at a fixed speed.

        /// <summary>How many circles a ring has, how big each is (circle radii), how often a ring is thrown (beats), and how fast the circles fly (Raiko's ripples).</summary>
        public const int RUMIA_CIRCLES = 10;

        public const float RUMIA_CIRCLE_RADIUS = 2.25f;
        public const double RUMIA_EVERY_BEATS = SANNYO_SMOKE_EVERY_BEATS;
        public const float RUMIA_CIRCLE_SPEED = RAIKO_RIPPLE_SPEED;

        /// <summary>A circle comes in over this long (ms), and over its edge what is under it is half see-through, the change being spread over a band this wide (circle radii).</summary>
        public const double RUMIA_FADE_IN_MS = 400;

        public const float RUMIA_EDGE_CIRCLES = 1.5f;

        /// <summary>Rumia's own rings: bullets in a ring, one ring every this many beats, and every ring is turned this many degrees per second further than the one before in time.</summary>
        public const int RUMIA_RING_BULLETS = 10;

        public const double RUMIA_RING_EVERY_BEATS = 1;
        public const float RUMIA_SPIN_DEGREES_PER_SECOND = 30f;

        // Hong Meiling: she draws in every bullet within this many circle radii of her, in pulls of this many beats; a bullet is drawn in at this many times its own speed (never slower than the minimum, px per ms);
        // what has been drawn in grows into the ball over this long (ms). Her own shots: a fan of this many bullets spread over this many degrees (aimed at the player), one every this many beats.
        public const float MEILING_REACH_CIRCLES = 5f;
        public const double MEILING_PULL_BEATS = 16;
        public const float MEILING_PULL_SPEED_FACTOR = 3f;
        public const float MEILING_PULL_MIN_SPEED = 0.25f;
        public const double MEILING_RAMP_MS = 150;
        public const float MEILING_BALL_SPEED = 2f; // (times the average speed of what was fed into the ball)
        public const float MEILING_MAX_RADIUS = 1.2f; // (no ball grows past this share of the playfield's height, growing on her or thrown)
        public const int MEILING_FAN_BULLETS = 10;
        public const float MEILING_FAN_SPREAD_DEGREES = 90f;
        public const double MEILING_FAN_EVERY_BEATS = 1;

        /// <summary>Every time the middle of a thrown ball reaches the wall it fires this many of Mokou's rings, of this many bullets each (twice Mokou's); at the second wall it stops and fades out over this long (ms).</summary>
        public const int MEILING_IMPACT_RINGS = 5;

        public const int MEILING_IMPACT_BULLETS = MOKOU_RING_BULLETS * 2;

        public const double MEILING_FADE_MS = 300;

        // Koakuma: every this-many-th bullet that flies while she attacks turns into a bat (what she shoots herself is bats from the start). A bat is drawn this many times as wide (half the wingspan, in radii of the bullet it was) as
        // that bullet, flaps once in this long (ms), turns towards the player this many times as fast as an arrowhead, and fades out (taking this long) once it has been within this many circle radii of them for this many beats in
        // all. It dims the bullets within this many circle radii of it, that strongly, and is gone for good after this long (ms) however far it is.
        public const int KOAKUMA_BAT_EVERY = 20;
        public const float KOAKUMA_BAT_SIZE_FACTOR = 1.1f;
        public const double KOAKUMA_BAT_FLAP_MS = 520;
        public const float KOAKUMA_BAT_TURN_FACTOR = 2f;
        public const float KOAKUMA_BAT_FADE_RADIUS_CIRCLES = 2f; // (Byakuren's homing beams too)
        public const double KOAKUMA_BAT_FADE_BEATS = 8;

        /// <summary>A bat homes for this many beats, then flies straight on; pointing away from the player it still turns at this share of the full turn (as Byakuren's beams).</summary>
        public const double KOAKUMA_HOMING_BEATS = BYAKUREN_HOMING_BEATS_LIMIT / 2;

        public const float KOAKUMA_TURN_FLOOR = BYAKUREN_TURN_FLOOR;
        public const double KOAKUMA_BAT_FADE_MS = 400;
        public const float KOAKUMA_BAT_DIM_RADIUS_CIRCLES = 1f;
        public const float KOAKUMA_BAT_DIM = 0.6f;
        public const double KOAKUMA_BAT_LIFETIME_MS = 60000;

        /// <summary>Koakuma's own bats live half as long as Byakuren's beams (which have the two values above): they fade after this many beats near the player, and are gone after this long (ms) at the most.</summary>
        public const double KOAKUMA_BAT_NEAR_BEATS = KOAKUMA_BAT_FADE_BEATS / 2;

        public const double KOAKUMA_BAT_LIFE_MS = KOAKUMA_BAT_LIFETIME_MS / 2;

        // Patchouli: every this many beats the element changes (fire, water, wood, metal, earth). Her ripples: a fire and a water ripple every this many beats, with these speeds (px per ms), half widths (px; the band is twice that)
        // and brightness. Fire: what it touches turns orange-red with a red ring (radius in osu! circle radii), eight beats later grows to the ring (shaking) over so long, glows, and fades out after holding so long.
        // Water: what it touches is this many times as slow and gets its speed back over this many beats. Metal: what it touches gets a knife on top of it (the bullet flies on), frozen for this many beats.
        // Wood: every this-many-th bullet that leaves the playfield grows a tree over this many beats (length in circle radii) with this many leaves on each tip, this big (a share of the bullet's size), thrown in this spread (degrees)
        // at this share of the bullet's speed;
        // the brown edges of the playfield are this deep (a share of the smaller side) and this strong.
        public const double PATCHOULI_PHASE_BEATS = 8;
        public const double PATCHOULI_RIPPLE_EVERY_BEATS = 4;
        public const float PATCHOULI_FIRE_SPEED = ZANMU_RIPPLE_SPEED;
        public const float PATCHOULI_FIRE_HALF_WIDTH = RAIKO_RIPPLE_HALF_WIDTH;
        public const float PATCHOULI_WATER_SPEED = RAIKO_RIPPLE_SPEED;
        public const float PATCHOULI_WATER_HALF_WIDTH = RAIKO_RIPPLE_HALF_WIDTH * 4f;
        public const float PATCHOULI_METAL_SPEED = ZANMU_RIPPLE_SPEED;
        public const float PATCHOULI_METAL_HALF_WIDTH = ZANMU_RIPPLE_HALF_WIDTH;
        public const float PATCHOULI_RIPPLE_ALPHA = 0.45f;
        public const float PATCHOULI_FIRE_RING_CIRCLES = 0.75f;
        public const double PATCHOULI_FIRE_DELAY_BEATS = 8;

        /// <summary>Over this many beats before a burning bullet goes off its red ring flashes, this many times (faster and faster).</summary>
        public const double PATCHOULI_FIRE_FLASH_BEATS = 2;

        public const double PATCHOULI_FIRE_FLASHES = 8;
        public const double PATCHOULI_FIRE_GROW_MS = 500;
        public const double PATCHOULI_FIRE_HOLD_MS = 500;
        public const double PATCHOULI_FIRE_FADE_MS = 600;
        public const float PATCHOULI_FIRE_BLOOM = 1.2f;
        public const int PATCHOULI_FIRE_MAX_RINGS = 150;
        public const float PATCHOULI_WATER_SLOWDOWN = 8f;
        public const double PATCHOULI_WATER_RECOVER_BEATS = 16;
        public const double PATCHOULI_METAL_HOLD_BEATS = 8;
        public const int PATCHOULI_TREE_EVERY = 10;
        public const double PATCHOULI_TREE_GROW_BEATS = 8;
        public const float PATCHOULI_TREE_LENGTH_CIRCLES = 4f;
        public const float PATCHOULI_LEAF_SIZE = 0.9f;
        public const float PATCHOULI_LEAF_SPREAD_DEGREES = 90f;
        public const int PATCHOULI_LEAVES_PER_TIP = 2;

        /// <summary>Patchouli's trees (their wood and leaves) glow, with a warm core, and bloom this much.</summary>
        public const float PATCHOULI_TREE_BLOOM = 0.9f;

        /// <summary>Water: the clone a caught bullet gets on the ripple is this visible and harmless for this many beats, while the bullet goes on as it was; then it takes the bullet's place.</summary>
        public const double PATCHOULI_RIDE_GHOST_BEATS = 4;

        /// <summary>A rider stays on its ring for this many beats after it has turned solid, then flies on the way and as fast as the bullet it took the place of.</summary>
        public const double PATCHOULI_RIDE_STAY_BEATS = 4;

        public const float PATCHOULI_RIDE_GHOST_VISIBLE = 0.35f;

        /// <summary>Water: the riders of a ripple go on this many rings in turn, side by side across its band, this share of its half width apart (each ring has an even spread of its own).</summary>
        public const int PATCHOULI_WATER_RINGS = 3;

        public const float PATCHOULI_WATER_RING_GAP = 0.6f;
        public const float PATCHOULI_LEAF_SPEED = 0.5f;
        public const float PATCHOULI_EDGE_DEPTH = 0.18f;
        public const float PATCHOULI_EDGE_ALPHA = 0.8f;
        // Earth: every this-many-th of her own bullets (thrown while it lasts) is a brown stone that glows and blooms (this much); where it hits the wall of the playfield it bursts into this many bullets of the same kind and size as the bullet it was,
        // spread over this many degrees away from the wall, and an orange flash (this many times the stone's radius, lasting this long) shows where.
        public const int PATCHOULI_EARTH_EVERY = 20;
        public const float PATCHOULI_EARTH_SIZE = 1f; // (in osu! circle radii)
        public const float PATCHOULI_WATER_SIZE = 1.5f;
        public const double PATCHOULI_WATER_SWELL_MS = 250;
        public const float PATCHOULI_EARTH_BLOOM = 0.9f;
        public const int PATCHOULI_EARTH_CHILDREN = 5;
        public const float PATCHOULI_EARTH_SPREAD_DEGREES = 160f;
        public const float PATCHOULI_EARTH_FLASH_RADIUS = 2f;
        public const double PATCHOULI_EARTH_FLASH_MS = 350;
        public const int PATCHOULI_SQUARE_BULLETS = 16;
        public const double PATCHOULI_SQUARE_EVERY_BEATS = 1;

        // Sunny Milk: her shots are triangles of this many plain bullets, one every this many beats. The prism in the middle of the playfield: its corners are this far from its centre (osu! circle radii), it turns this share of the
        // speed of the spinners' beams, the other way every this many beats; the rainbow halo is this much wider than the prism and this strong; a bullet that leaves the prism becomes this many bullets in this spread (degrees),
        // each this much smaller; a bullet that goes in or out lights the halo there for this long (ms).
        public const int KAKU_ELLIPSE_BULLETS = 24;
        public const double KAKU_ELLIPSE_EVERY_BEATS = 1;
        public const float KAKU_RETURN_SPEED_DIVISOR = 4f;
        public const int KAKU_RETURN_EVERY = 2; // (every this many-th bullet comes back)
        public const double KAKU_RETURN_BEATS = 16;
        public const double KAKU_RETURN_FADE_MS = 600;
        public const float KAKU_DARKEN = 0.5f;
        public const float KAKU_TALISMAN_FIT = 0.92f;
        public const int SUNNY_TRIANGLE_BULLETS = 12;
        public const double SUNNY_TRIANGLE_EVERY_BEATS = 1;
        public const float SUNNY_PRISM_RADIUS_CIRCLES = 3f;        public const float SUNNY_TURN_SHARE = 0.5f;
        public const double SUNNY_TURN_BEATS = 16;
        public const float SUNNY_HALO_SCALE = 1.45f;
        public const float SUNNY_HALO_ALPHA = 0.32f;
        public const int SUNNY_SPLIT_BULLETS = 7;
        public const float SUNNY_SPLIT_SPREAD_DEGREES = 120f;
        public const float SUNNY_SPLIT_SIZE_DIVISOR = 4f;
        public const double SUNNY_FLASH_MS = 450;
        public const int SUNNY_HALO_RINGS = 36;
        public const float SUNNY_WARNING_SHAKE = 0.5f;

        // Satori: once a beat she looks at which way the player moved (from where they were a beat ago to where they are); bullets in a cone of this many degrees round that way, up to this many circle radii from the player,
        // turn to that way, over this many ms.
        public const float SATORI_CONE_DEGREES = 90f;
        public const double SATORI_EVERY_BEATS = 2;
        public const float SATORI_REACH_CIRCLES = 5f;
        public const double SATORI_TURN_MS = 1200; // (eight times slower than the 150 ms it was)
        public const float SATORI_MIN_MOVE_PX = 3f;
        public const double SATORI_WINDOW_MS = 100;

        /// <summary>Satori's third eyes round the player before every reading (this many, evenly round them, the first in front, each over a cone of its own): this big (circle radii), this far from them (circle radii), this opaque at most, and they close this fast after the reading (ms).</summary>
        public const int SATORI_EYES = 3;

        public const float SATORI_EYE_SIZE = 3.2f;

        public const float SATORI_EYE_DISTANCE = 3f; // (the eye is twice as big as it was: its near edge is as far from the player as it was)
        public const float SATORI_EYE_ALPHA = 0.75f;
        public const double SATORI_EYE_FADE_OUT_MS = 300;

        public const float SEIRAN_SIDE_ANGLE_DEGREES = 5f / 3f;
        public const float SEIRAN_SHOT_SPACING = RINGO_BEHIND_SPACING;

        /// <summary>Ringo: the big bullets (a half circle of them) come every this many beats, are this many times as big and as fast as ordinary bullets.</summary>
        public const double RINGO_VOLLEY_BEATS = 4;

        public const float RINGO_VOLLEY_SIZE = 3f;
        public const float RINGO_VOLLEY_SPEED_FACTOR = 1.5f;

        /// <summary>Chen: her throw is this many times as fast as an ordinary bullet; she stays this far from the border of the screen.</summary>
        public const float CHEN_SPEED_FACTOR = 4f;

        public const float CHEN_WALL_MARGIN = 24f;

        /// <summary>Koishi: bullets and beams vanish for this many beats; the glitch out of it and into it last this long (ms).</summary>
        public const double KOISHI_HIDE_BEATS = 4;

        /// <summary>Koishi (and Satori): her hearts are this many times bigger than a boss's stars usually are, and shot this many times less often (1: as often as the stars; in a spinner every star is a heart).</summary>
        public const float KOISHI_HEART_SIZE_FACTOR = 2f;

        public const double KOISHI_HEART_INTERVAL_FACTOR = 1;

        /// <summary>Koishi and Satori (both Komeiji): every ring of their hearts has this many times the bullets of a boss's stars, in their attacks and in a spinner (where every star is a heart).</summary>
        public const float KOISHI_HEART_COUNT_FACTOR = 2f;

        /// <summary>Koishi vanishes again every this many beats for as long as her attack lasts (the first time is when the attack starts): a hide of <see cref="KOISHI_HIDE_BEATS"/> and then 12 beats of seeing everything again.</summary>
        public const double KOISHI_HIDE_EVERY_BEATS = 16;

        /// <summary>
        /// Koishi: while everything is invisible, the bullets close to the player are shown anyway, round the player: fully (as far as <see cref="KOISHI_REVEAL_NEAR"/> osu! circle
        /// radii) and then less and less, until <see cref="KOISHI_REVEAL_FAR"/>; at most this visible.
        /// </summary>
        public const float KOISHI_REVEAL_NEAR = 3f;

        public const float KOISHI_REVEAL_FAR = 6f;

        public const float KOISHI_REVEAL_VISIBILITY = 0.375f;

        public const double KOISHI_GLITCH_OUT_MS = 250;

        /// <summary>Koishi: how long (ms) a flickering bullet stays on or off before it is drawn again at random.</summary>
        public const double KOISHI_FLICKER_STEP_MS = 45;

        public const double KOISHI_GLITCH_IN_MS = 450;

        /// <summary>Koishi: a bullet is fully invisible this close to the player (osu! circle radii) and fully visible from <see cref="KOISHI_FADE_FAR"/> on.</summary>
        public const float KOISHI_FADE_NEAR = 1f;

        public const float KOISHI_FADE_FAR = 3f;

        /// <summary>The farthest tip of the snowflake in a frozen bullet reaches this far (0..1) out to the edge of the biggest disc that fits into the bullet's body.</summary>
        public const float SNOWFLAKE_SIZE_FACTOR = 0.92f;

        /// <summary>The distance (in pixels of the picture) from the middle of the snowflake picture to its farthest tip.</summary>
        public const float SNOWFLAKE_TIP_PIXELS = 80.5f;

        /// <summary>The snowflake in a frozen bullet is a little see-through (1 = opaque); the bullet itself is not.</summary>
        public const float SNOWFLAKE_ALPHA = 0.75f;

        /// <summary>Cirno: when she freezes everything the background turns dark blue (this opaque at most) within CIRNO_FLASH_IN_MS and has faded out again after CIRNO_FLASH_MS.</summary>
        public const float CIRNO_BACKGROUND_ALPHA = 0.5f;

        public const double CIRNO_FLASH_IN_MS = 120;
        public const double CIRNO_FLASH_MS = 1500;

        /// <summary>Cirno: the bullets a frozen bullet bursts into are this much smaller; they fan out CIRNO_FAN_DEGREES apart (CIRNO_FAN_COUNT of them).</summary>
        public static float CIRNO_CHILD_SIZE => Bosses.BossTuning.CirnoChildSize.Value;

        /// <summary>Cirno: the bullets that burst out of the frozen ones fly at this multiple of the speed the frozen bullet had.</summary>
        public static float CIRNO_BURST_SPEED_FACTOR => Bosses.BossTuning.CirnoBurstSpeed.Value;

        /// <summary>How many bullets come out of one frozen bullet when it thaws (a fan, centred on the way the frozen bullet was flying).</summary>
        public const int CIRNO_FAN_COUNT = 3;

        /// <summary>The angle between neighbouring bullets of the fan (3 bullets 120 degrees apart: evenly round the circle).</summary>
        public const float CIRNO_FAN_DEGREES = 120f;

        /// <summary>Sakuya: spirals of knives (arms), how far from her the first knife of a spiral hangs and how much further each next one does (bullet radii), and the turn (radians) from one to the next.</summary>
        public const int SAKUYA_KNIFE_ARMS = 4;

        public const float SAKUYA_KNIFE_RADIUS_START = 10f;
        public const float SAKUYA_KNIFE_RADIUS_STEP = 1.5f;
        public const float SAKUYA_KNIFE_TWIST = 0.45f;

        /// <summary>Sakuya's time stop: the dark brown background (this opaque at most) and the faint clock come in within SAKUYA_STOP_IN_MS and go within SAKUYA_STOP_OUT_MS after the swap.</summary>
        public const float SAKUYA_BACKGROUND_ALPHA = 0.55f;

        public const float SAKUYA_CLOCK_ALPHA = 0.2f;
        public const double SAKUYA_STOP_IN_MS = 150;
        public const double SAKUYA_STOP_OUT_MS = 200;

        /// <summary>Reisen's freeze: the dim purple background (this opaque at most), how fast it and the transparency come in, how long the background fades out after the slide.</summary>
        public const float REISEN_BACKGROUND_ALPHA = 0.5f;

        public const double REISEN_IN_MS = 150;
        public const double REISEN_BACKGROUND_OUT_MS = 400;

        /// <summary>How much of a frozen bullet can be seen (1 = all of it), and how much of its preview (where it will have slid to).</summary>
        public const float REISEN_VISIBLE = 0.4f;

        public const float REISEN_PREVIEW_VISIBLE = 0.1125f;

        /// <summary>How visible the green copy of what Sakuya's mirroring will move is.</summary>
        public const float SAKUYA_PREVIEW_VISIBLE = 0.05625f;

        /// <summary>Sakuya: a green copy shows only where the mirroring puts a thing within this many osu! circle radii of the player (the rest would only cost).</summary>
        public const float SAKUYA_PREVIEW_CIRCLES = 4f;

        /// <summary>
        /// Sakuya: the sideways blur of her time stop (four faint copies of everything) is drawn only within this many osu! circle radii of the player, where the player is in every frame, and fades out over the last
        /// one: over the whole screen it was five times as much to draw, and that was her lag.
        /// </summary>
        public const float SAKUYA_BLUR_CIRCLES = SAKUYA_PREVIEW_CIRCLES;

        /// <summary>The frozen bullets slide this many osu! circles (diameters) to the left or the right (or up or down) while the attack lasts; an attack and a pause last this many beats.</summary>
        public static float REISEN_SLIDE_CIRCLES => Bosses.BossTuning.ReisenSlide.Value;

        public static double REISEN_ATTACK_BEATS => Bosses.BossTuning.ReisenAttackBeats.Current;
        public static double REISEN_PAUSE_BEATS => Bosses.BossTuning.ReisenPauseBeats.Current;

        /// <summary>Her beams are this short (ms of firing), this many in a ring, and a ring comes this many beats after the last started.</summary>
        public const double REISEN_BEAM_MS = 75;

        public const int REISEN_BEAMS = 25;

        public const double REISEN_BEAM_GAP_BEATS = 0.5;

        /// <summary>Nazrin: every this many beats every bullet within this many osu! circles (diameters) of her turns into an arrowhead of the same size that is pulled towards the player.</summary>
        public const double NAZRIN_CONVERT_BEATS = 16;

        /// <summary>The bullets she turns fly on this many times as fast as they flew.</summary>
        public const float NAZRIN_CONVERTED_SPEED = 2f;

        /// <summary>The circle that marks the area (in the map's combo colours) stays round her while she is there, and flares up for this long when the bullets in it are turned.</summary>
        public const double NAZRIN_RIPPLE_FADE_MS = 300;
        public const float NAZRIN_RIPPLE_ALPHA = 0.55f;

        /// <summary>The bullets she has turned glow (bloom, 1 = the strongest), and the circle flares with a glow this bright.</summary>
        public const float NAZRIN_BLOOM = 0.8f;

        public const float NAZRIN_FLASH_ALPHA = 0.5f;

        /// <summary>Zanmu: a ripple every this many beats (from the start of her section); it is this many times as fast as Raiko's, a smooth band this wide (px, half of it either side of its middle) and this bright (0..1).</summary>
        public static double ZANMU_RIPPLE_BEATS => Bosses.BossTuning.ZanmuRippleBeats.Current;

        /// <summary>What a ripple has stopped falls this many beats after the ripple started (at the latest when her section ends).</summary>
        public static double ZANMU_HOLD_BEATS => Bosses.BossTuning.ZanmuHoldBeats.Current;

        public const float ZANMU_RIPPLE_SPEED_FACTOR = 2f;
        public const float ZANMU_RIPPLE_SPEED = RAIKO_RIPPLE_SPEED * ZANMU_RIPPLE_SPEED_FACTOR;
        public const float ZANMU_RIPPLE_HALF_WIDTH = 14f;
        public const float ZANMU_RIPPLE_ALPHA = 0.45f;

        /// <summary>What has been stopped is this much grey (0..1); when it falls it darkens (to this much black, 0..1) over this long (ms).</summary>
        public const float ZANMU_FROZEN_GREY = 0.85f;

        public const float ZANMU_FALL_SHADE = 0.4f;
        public const double ZANMU_FALL_DARKEN_MS = 2500;

        /// <summary>What she has stopped falls this much slower than things fall when the player dies; whatever falls off the bottom comes in from the top once, and is gone when it falls off the bottom again.</summary>
        public const float ZANMU_GRAVITY = DEATH_GRAVITY / 16f;

        /// <summary>A falling thing that is this far (px) below the screen comes back in this far above it (once).</summary>
        public const float ZANMU_WRAP_MARGIN = 32f;

        /// <summary>The background is dimmed grey this much (0..1) while her section lasts; the skull is as high as the screen times its size, this opaque (0..1), grey (0..1), and grows by this share over the section.</summary>
        public const float ZANMU_BACKGROUND_ALPHA = 0.45f;

        public const float ZANMU_SKULL_SIZE = 1.1f;
        public const float ZANMU_SKULL_ALPHA = 0.22f;
        public const float ZANMU_SKULL_GREY = 0.62f;
        public const float ZANMU_SKULL_GROW = 0.12f;

        /// <summary>The small skull in the body of a bullet that has been stopped: its farthest point reaches this far (0..1) out to the edge of the biggest disc that fits into the body, and it is this opaque (0..1).</summary>
        public const float ZANMU_BULLET_SKULL_SIZE = 0.92f;

        /// <summary>The distance (in pixels of the picture) from the middle of the skull picture (Textures/skull_decal.png, cropped to the skull) to the farthest point of the skull.</summary>
        public const float ZANMU_SKULL_TIP_PIXELS = 315.85f;

        /// <summary>A bullet that a ripple has caught slows down to a stop evenly over this many beats; the grey and the skull fade in over the same time.</summary>
        public const double ZANMU_SLOWDOWN_BEATS = 1;

        /// <summary>A slice of a beam turns grey in this many steps while it slows down (a beam has one colour per stretch of it).</summary>
        public const int ZANMU_FADE_STEPS = 8;

        public const float ZANMU_BULLET_SKULL_ALPHA = 0.55f;

        /// <summary>What has been frozen (Cirno's, Zanmu's) shakes for this many beats before it is let go / falls: every frame it is moved this far (px, between the two values) in a new direction (a new one every <see cref="FROZEN_SHAKE_STEP_MS"/> ms).</summary>
        public const double FROZEN_SHAKE_BEATS = 2;

        public const float FROZEN_SHAKE_MIN_PX = 1f;
        public const float FROZEN_SHAKE_MAX_PX = 2f;
        public const double FROZEN_SHAKE_STEP_MS = 33;

        public const float NAZRIN_REACH_CIRCLES = 4f;

        /// <summary>The clock's width as a fraction of the screen's height when the stop starts, and when it ends (it grows all the time).</summary>
        public const float SAKUYA_CLOCK_START_SIZE = 0.5f;

        public const float SAKUYA_CLOCK_END_SIZE = 2f;

        /// <summary>
        /// Letty: every slice of a slider's beam lives this many beats and then dissipates into bullets: one every LETTY_BULLET_INTERVAL_BEATS (but at most LETTY_MAX_SLICES per slider)
        /// along the beam, of this size (a multiple of the normal bullet) and speed (a multiple of the normal bullet speed).
        /// </summary>
        public static double LETTY_DISSIPATE_BEATS => Bosses.BossTuning.LettyDissipateBeats.Current;

        public const double LETTY_BULLET_INTERVAL_BEATS = 0.125;
        public const double LETTY_MAX_SLICES = 48;

        /// <summary>Letty: a slice of a beam dissipates into this many bullets for every beam, spread evenly over a half circle behind it (against the way the slider goes).</summary>
        public static int LETTY_RING_COUNT => Bosses.BossTuning.LettyRing.Whole;
        public const float LETTY_BULLET_SIZE = 0.5f;
        public const float LETTY_BULLET_SPEED_FACTOR = 0.5f;

        /// <summary>Toyohime: bullets that leave the top of the screen during an attack fall with this pull (px per ms squared: the same as when the player dies).</summary>
        public const float TOYOHIME_GRAVITY = DEATH_GRAVITY;

        /// <summary>
        /// Toyohime stands where the boss beside her does not (in the middle when that one stands opposite the player, and the other way round; with two others always in the middle), and alone on the map she plays another boss in
        /// every attack. (Off: she stands opposite the player and does only her own, as before.)
        /// </summary>
        public const bool TOYOHIME_REWORK = true;

        /// <summary>Toyohime: a bullet this many circle radii from the player (or closer) is pulled towards the player, turning at TOYOHIME_TURN_RATE radians per ms at most.</summary>
        public static float TOYOHIME_PULL_RADIUS => Bosses.BossTuning.ToyohimeRadius.Value;

        public static float TOYOHIME_TURN_RATE => Bosses.BossTuning.ToyohimeTurn.Value;

        /// <summary>Toyohime pulls only every this many-th bullet of a ring.</summary>
        public const int TOYOHIME_PULL_EVERY = 2;

        /// <summary>Toyohime: the ring that shows how far the pull reaches is a Raiko ripple with an edge half as thick.</summary>
        public const float TOYOHIME_RING_LINE = RAIKO_RIPPLE_LINE / 2f;

        /// <summary>Toyohime's golden magic circle (round the player, as big as the pull): how opaque; how fast it turns (degrees per ms).</summary>
        public const float TOYOHIME_CIRCLE_ALPHA = 0.7f;


        public const float TOYOHIME_CIRCLE_SPIN = 0.012f;

        /// <summary>Toyohime's arrows on the circle that point at the player: how many; how big (circle radii); how opaque; how many beats one takes to slide in.</summary>
        public const int TOYOHIME_ARROWS = 8;

        public const float TOYOHIME_ARROW_SIZE = 0.6f;

        /// <summary>Toyohime's arrows slide in from this many circle radii from the player to this many (close round the player).</summary>
        public const float TOYOHIME_ARROW_FAR = 2.2f;

        public const float TOYOHIME_ARROW_NEAR = 1.1f;

        /// <summary>A bullet Toyohime pulls is shown golden (this much of the way to gold) and glows this much.</summary>
        public const float TOYOHIME_GOLD_TINT = 0.85f;

        public const float TOYOHIME_GOLD_BLOOM = 0.8f;
        public const float TOYOHIME_ARROW_ALPHA = 0.85f;
        public const double TOYOHIME_ARROW_BEATS = 2;

        /// <summary>Sakuya: when everything is swapped the knives are placed this many times as far from the player as they hung from her (they fly at the player from there).</summary>
        public const float SAKUYA_RELEASE_DISTANCE_FACTOR = 6.4f;

        /// <summary>Sakuya: her knives (the ones she shoots and the ones that hang round her) are this many times as big as a bullet.</summary>
        public const float SAKUYA_KNIFE_SIZE_FACTOR = 1f;

        /// <summary>Sakuya: the knives are thrown out at this multiple of the bullet speed.</summary>
        public const float SAKUYA_KNIFE_SPEED_FACTOR = 0.9f;

        /// <summary>How long (half length) a knife is drawn, as a multiple of the bullet's radius.</summary>
        public const float KNIFE_LENGTH_FACTOR = 1.7f;

        /// <summary>Remilia's knives get more and more blades joined at their handles as her attack goes on: a double-edged knife from this share of the attack, a three-edged one from the next, a cross of four from the last (it stays until the attack ends).</summary>
        public const float REMILIA_DOUBLE_EDGED_AT = 0.2f;

        public const float REMILIA_THREE_EDGED_AT = 0.4f;
        public const float REMILIA_CROSS_AT = 0.6f;

        /// <summary>During Remilia's attack every this many-th bullet that is thrown (knives not counted) is REMILIA_BIG_SIZE times as big.</summary>
        public const int REMILIA_BIG_EVERY = 40;

        public const float REMILIA_BIG_SIZE = 3f;

        /// <summary>How fast a many-bladed knife turns round its handle (radians per ms); every next ring of them turns the other way.</summary>
        public const float KNIFE_SPIN_RATE = 0.002f;

        /// <summary>How far from the middle of a knife its handle end (where the blades of a many-edged knife meet) lies, as a share of its half length.</summary>
        public const float KNIFE_PIVOT = 0.8f;

        /// <summary>The halo of a knife (bloom) is this many times as wide as it is long.</summary>
        public const float KNIFE_HALO_ASPECT = 0.45f;

        /// <summary>How long (half length) a bullet that has become a train carriage is drawn, as a multiple of the bullet's radius.</summary>
        public const float TRAIN_LENGTH_FACTOR = 1.7f;

        /// <summary>A talisman: half height / half length of the picture (512 x 294), and its half length as a multiple of the bullet's radius (it is as big as the bullet).</summary>
        public const float TALISMAN_ASPECT = 294f / 512f;

        public const float TALISMAN_LENGTH_FACTOR = 0.7f;

        /// <summary>How bright the coloured body of a talisman is (1 = the bullet's colour), and how bright its white border and writing are: dark body, bright writing.</summary>
        public const float TALISMAN_BODY_LEVEL = 0.5f;

        public const float TALISMAN_LINE_LEVEL = 0.9f;

        /// <summary>Yukari's train is smeared along its way: the blur is as long as the train flies in this many ms, drawn as this many copies, each this opaque.</summary>
        public const float TRAIN_BLUR_MS = 90f;

        public const int TRAIN_BLUR_COPIES = 9;

        public const float TRAIN_BLUR_ALPHA = 0.4f;

        /// <summary>A heart in the bullet's own frame (the tip on +x, farthest point at distance 1).</summary>
        public static readonly Vector2[] HEART = createHeart();

        /// <summary>A flower petal in the bullet's own frame (the tip on +x, a notch at the back, farthest point at distance 1).</summary>
        public static readonly Vector2[] PETAL = createPetal();

        /// <summary>The maple leaf of the flag of Canada in the bullet's own frame (the top on +x, the stem on -x, farthest point at distance 1).</summary>
        public static readonly Vector2[] MAPLE = createMaple();

        private static Vector2[] createMaple()
        {
            // (half of the leaf as the flag draws it, from the top down to the stem, with the top pointing up: the other half is its mirror image)
            (float X, float Y)[] half =
            {
                (0, -2000), (332, -1348), (423, -1321), (750, -1510), (546, -458), (657, -401), (1080, -855), (1185, -608), (1258, -570), (1800, -685), (1614, -113), (1648, -34), (1860, 65), (919, 827), (899, 900), (1015, 1220), (156, 1069),
                (45, 1167), (90, 2030),
            };

            var points = new Vector2[half.Length * 2 - 1];
            float far = 0f;

            foreach (var (x, y) in half)
                far = Math.Max(far, MathF.Sqrt(x * x + y * y));

            for (int i = 0; i < half.Length; i++)
                points[i] = new Vector2(-half[i].Y, half[i].X) / far;

            for (int i = 1; i < half.Length; i++)
                points[half.Length - 1 + i] = new Vector2(-half[half.Length - i].Y, -half[half.Length - i].X) / far;

            return points;
        }

        /// <summary>A slice of a laser beam in the bullet's own frame: a thin blade, pointed at both ends, along the x axis (farthest points at distance 1).</summary>
        public static readonly Vector2[] SLICE = createSlice();

        /// <summary>Junko: a slice is this many times as long (half length) as the bullet it replaces is big.</summary>
        public const float SLICE_LENGTH_FACTOR = 2.4f;

        private static Vector2[] createSlice()
        {
            const int steps = 16;
            var points = new Vector2[steps * 2];

            float width(float x) => 0.3f * MathF.Pow(Math.Max(0f, 1f - MathF.Pow(Math.Abs(x), 1.6f)), 0.9f);

            for (int i = 0; i < steps; i++)
            {
                float x = 1f - 2f * i / steps;
                points[i] = new Vector2(x, width(x));
                points[steps + i] = new Vector2(-x, -width(-x));
            }

            return points;
        }

        private static Vector2[] createHeart()
        {
            const int count = 48;
            var points = new Vector2[count];
            float far = 0f;

            for (int i = 0; i < count; i++)
            {
                double t = Math.PI * 2 * i / count;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t) + 2.5; // (centred on the middle of its height)

                // the heart's tip (the bottom of the classic picture) points forward
                points[i] = new Vector2((float)-y, (float)x);
                far = Math.Max(far, points[i].Length);
            }

            for (int i = 0; i < count; i++)
                points[i] /= far;

            return points;
        }

        private static Vector2[] createPetal()
        {
            const int steps = 12;
            var points = new System.Collections.Generic.List<Vector2>();

            float width(float x) => 0.6f * MathF.Sqrt(Math.Max(0f, 1f - MathF.Pow((x + 0.1f) / 1.1f, 2f)));

            for (int i = 0; i <= steps; i++)
            {
                float x = 1f - 2f * i / steps;
                points.Add(new Vector2(x, width(x)));
            }

            points.Add(new Vector2(-0.7f, 0f)); // the notch at the back

            for (int i = steps; i >= 0; i--)
            {
                float x = 1f - 2f * i / steps;
                points.Add(new Vector2(x, -width(x)));
            }

            float far = 0f;

            foreach (var p in points)
                far = Math.Max(far, p.Length);

            for (int i = 0; i < points.Count; i++)
                points[i] /= far;

            return points.ToArray();
        }

        private static Vector2[] createArrowhead()
        {
            // one side of the sprite as (half width, row) in the reference image's pixels, tip (top) to the back corner (bottom)
            float[,] side = { { 4.5f, 10f }, { 9.5f, 14.5f }, { 12.5f, 19.5f }, { 14.5f, 25f }, { 14.5f, 34f }, { 10.5f, 46.5f } };
            const float tip_row = 5.5f;
            const float notch_row = 38f; // the middle of the back edge, between the two shards
            const float centre_row = 26f; // halfway between the tip and the back corners

            var outline = new System.Collections.Generic.List<Vector2> { new Vector2(centre_row - tip_row, 0f) };

            for (int i = 0; i < side.GetLength(0); i++)
                outline.Add(new Vector2(centre_row - side[i, 1], side[i, 0]));

            outline.Add(new Vector2(centre_row - notch_row, 0f));

            for (int i = side.GetLength(0) - 1; i >= 0; i--)
                outline.Add(new Vector2(centre_row - side[i, 1], -side[i, 0]));

            float farthest = 0;
            foreach (var corner in outline)
                farthest = Math.Max(farthest, corner.Length);

            var result = new Vector2[outline.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = outline[i] / farthest;

            return result;
        }

        /// <summary>Lasers (slider whistle) fly at this fraction of the speed other bullets would have at that moment.</summary>
        public const float LASER_SPEED_FACTOR = 0.5f;

        /// <summary>A spinner fires this many beams, evenly spread around it.</summary>
        public const int SPINNER_LASER_BEAMS = 5;

        /// <summary>OD 0..10 -> how fast the spinner's beams turn: 6..60 degrees per second, returned in radians per millisecond.</summary>
        public static float SpinnerLaserAngularSpeed(double overallDifficulty)
            => (6f + 54f * (float)Math.Clamp(overallDifficulty, 0, 10) / 10f) * MathF.PI / 180f / 1000f;

        /// <summary>OD 0..10 -> half width of a spinner's (and the boss's) laser beam as a multiple of the radius of a spinner bullet (1x..4x).</summary>
        public static float SpinnerLaserSizeScale(double overallDifficulty)
            => 1f + 3f * (float)Math.Clamp(overallDifficulty, 0, 10) / 10f;

        // ---- the boss of the breaks ----

        /// <summary>
        /// Spinner and boss beams start as wide as an osu! hit circle and grow like a parabola on their way out (fast at first, then flattening),
        /// the widest point is this many times the width the OD scaling gives (<see cref="SpinnerLaserSizeScale"/>), i.e. slightly above it
        /// (and never less than the start width).
        /// </summary>
        public const float LASER_PEAK_FACTOR = 1.15f;

        /// <summary>The beams of the bosses widen with this power (a spinner's: 2): 3 = 50% faster at first, then ever more slowly down to nothing.</summary>
        public const float BOSS_LASER_GROWTH_POWER = 3f;

        /// <summary>The widest a boss's beam gets, relative to a spinner's: 20% more.</summary>
        public const float BOSS_LASER_WIDTH_FACTOR = 1.2f;

        /// <summary>The distance (playfield pixels) from the origin at which a spinner / boss beam has reached its full width.</summary>
        public const float LASER_GROWTH_DISTANCE = 500f;

        /// <summary>While a spinner or boss beam fires, the bullets are drawn this much darker (0 = not at all), so the beam stands out.</summary>
        public const float BEAM_BULLET_DIMMING = 0.375f;

        /// <summary>The boss's target position is read at most this often (125 Hz).</summary>
        public const double BOSS_UPDATE_INTERVAL_MS = 1000.0 / 125;

        /// <summary>Time constant (ms) of the boss's gliding to his target: he covers about 63% of the distance in this time.</summary>
        public const double BOSS_SMOOTHING_MS = 70;

        /// <summary>The section of two bosses: when they stand on each other they circle round their place on opposite sides (each with a magic circle of its own), this far from it (hit circle radii) and this slowly (ms for one turn).</summary>
        public const float DOUBLE_BOSS_ORBIT_RADIUS = 0.6f;

        public const double DOUBLE_BOSS_ORBIT_PERIOD_MS = 3000;

        /// <summary>Spinner and boss beams are this much wider than the OD scaling alone gives (+20%).</summary>
        public const float SPINNER_LASER_WIDTH_FACTOR = 1.2f;

        /// <summary>The kiai boss fires the same spiral of stars as the break boss, but at half the frequency (twice the interval).</summary>
        public const double KIAI_SPIRAL_INTERVAL_FACTOR = 2;

        /// <summary>Screen shake (playfield pixels) while a spinner or boss beam fires: 1 px when it starts and ends, 2 px at its strongest.</summary>
        public const float BEAM_SHAKE_MIN = 1f;

        public const float BEAM_SHAKE_MAX = 2f;

        /// <summary>The boss of a break is gone this many beats (4/1) before a spinner that follows it, and does not come back until this long after a spinner that came before it.</summary>
        public const double BOSS_SPINNER_CLEARANCE_BEATS = 4;

        /// <summary>A break gets a boss only if it lasts at least this many beats (4/1).</summary>
        public const double BOSS_MIN_BREAK_BEATS = 4;

        /// <summary>The boss's lasers pause between two bursts for as long as a burst lasts...</summary>
        public const double BOSS_LASER_PAUSE_BEATS = BOSS_LASER_BURST_BEATS;

        /// <summary>...and fire for this many beats (16/1) in a burst.</summary>
        public const double BOSS_LASER_BURST_BEATS = 16;

        /// <summary>Length (ms) of the power-up sound that leads into every spinner / boss laser (Resources/Samples/power_up.wav), used if the sample's own length is unknown.</summary>
        public const double POWER_UP_LENGTH_MS = 1900;

        /// <summary>Radius of the boss's magic circle (playfield pixels). The dark edges of spinner and boss beams fade in between this distance and twice it.</summary>
        public const float MAGIC_CIRCLE_RADIUS = 95f;

        /// <summary>
        /// The player's own magic circle, besides the bosses' (theirs are unchanged): their picture and glow, always on the player, this share of their size (a quarter), as see-through as theirs, turning
        /// the other way at their speed (degrees per second; theirs is +24).
        /// </summary>
        public const float PLAYER_CIRCLE_SIZE = 0.25f;

        // ---- maps of the other modes (DanmakusuBeatmapConverter, Bullets/ConvertedModes.cs) ----

        /// <summary>
        /// osu!mania and osu!taiko: the objects stand on a ring round the middle of the playfield, the first column in the middle and the others evenly round it. Its radius (osu! px) grows with the
        /// keys: this much, and this much more per key, at most this much (twice that in a boss section still fits the playfield's height).
        /// </summary>
        public const float CONVERT_RING_RADIUS = 40f;

        public const float CONVERT_RING_PER_KEY = 8f;
        public const float CONVERT_RING_MOST = 96f;

        /// <summary>In a boss section the ring is this many times as big.</summary>
        public const float CONVERT_BOSS_RING = 2f;

        /// <summary>The ring turns this far (radians: a whole turn) one way in this many beats, then the same back the other way, and so on.</summary>
        public const float CONVERT_RING_TURN = MathF.Tau;

        public const double CONVERT_RING_REVERSE_BEATS = 32;

        /// <summary>osu!taiko is played as four keys: kat, don, don, kat (the dons take turns on the two in the middle of a 4K row, the kats on the two at its ends).</summary>
        public const int CONVERT_TAIKO_KEYS = 4;

        /// <summary>
        /// osu!mania and osu!taiko: every bullet flies this many times as fast - the map's circles', sliders', ticks' and slider ends', the spinners' stars and the bosses' own (their rings and their acts'
        /// bullets, since 2026-11-08) - but not the beams, nor a big note's arcs (BulletPlanner.speedAt / beamSpeedAt); a whoosh as one flies past then takes this many times the speed it takes elsewhere.
        /// </summary>
        public const float CONVERT_RING_BULLET_SPEED = 2f;

        /// <summary>A mania or taiko map's circle size is its key count or means nothing: every one is played at this circle size.</summary>
        public const float CONVERT_CS = 4f;

        /// <summary>A long note (and a drumroll) is a slider that does not move: a path that only goes back and forth by a pixel, as long as the note at this slider velocity (the least osu! allows).</summary>
        public const double CONVERT_HOLD_VELOCITY = 0.1;

        /// <summary>osu!catch: what falls lands on this line (the bottom of the playfield) when it is due, falling evenly from this one (its top) over its approach time.</summary>
        public const float CATCH_BOTTOM = 384f;

        public const float CATCH_TOP = 0f;

        /// <summary>
        /// osu!catch: what falls goes this far (playfield px: from wall to wall of a screen wider than 4:3, where the playfield is 0.8 of its height) in its approach time: it comes in through the top
        /// wall an approach time before it lands on the bottom wall, and its approach circle warns of that over the approach time before.
        /// </summary>
        public const float CATCH_FALL_HEIGHT = 480f;

        /// <summary>osu!catch: what lands shoots its rings and beams spread evenly over this many degrees, up.</summary>
        public const float CATCH_FAN_DEGREES = 150f;

        /// <summary>osu!catch: the trail of a juice stream (a slider whose every point lands when the stream is there) is a point every this many ms.</summary>
        public const double CATCH_TRAIL_STEP_MS = 16;

        public const float PLAYER_CIRCLE_ALPHA = 0.55f;
        public const float PLAYER_CIRCLE_GLOW_ALPHA = 0.6f;
        public const float PLAYER_CIRCLE_SPIN = -24f;

        /// <summary>Seija's screen turns round (and back) in this long (ms) at the start (end) of each of her attacks (0 = at once, no transition).</summary>
        public const double MIRROR_TURN_MS = 0;

        /// <summary>Seija: after every turn of the screen a red circle shows where the player is, for this many beats, starting this many circle radii wide.</summary>
        public const double SEIJA_CIRCLE_BEATS = 2;

        public const float SEIJA_CIRCLE_RADII = 6f;

        /// <summary>While the screen is upside down a red glow lies round the player, this many circle radii out, this strong (0..1, see-through).</summary>
        public const float SEIJA_GLOW_RADII = 8f;

        public const float SEIJA_GLOW_ALPHA = 0.35f;

        /// <summary>
        /// Seija warns of every turn of the screen this many beats before it: her clone where she will be seen after it, at most this visible; and this many red arrows round the player, this many circle radii away,
        /// this big (circle radii), this opaque, flashing this many times (faster and faster), every one pointing at her.
        /// </summary>
        public const double SEIJA_WARN_BEATS = 4;

        public const float SEIJA_CLONE_ALPHA = 0.35f;
        public const int SEIJA_ARROWS = 8;
        public const float SEIJA_ARROW_RADII = 2.2f;
        public const float SEIJA_ARROW_SIZE = 1f; // (the arrows of her dress, as the grid's)
        public const float SEIJA_ARROW_ALPHA = 2f * SEIJA_GRID_ALPHA; // (half as see-through as the grid's)
        public const float SEIJA_ARROW_FLASHES = 6f;

        /// <summary>Seija's warning, also: this many of the same red arrows in a circle round her, this many circle radii out from the edge of her magic circle, all pointing in at her, flashing as the ones round the player do.</summary>
        public const int SEIJA_HER_ARROWS = 8;

        public const float SEIJA_HER_ARROW_GAP_RADII = 0.8f;

        /// <summary>
        /// Seija's warning, also: big see-through arrows (the ones on her dress) in a grid over the whole screen, this many rows of them (red ones in the middles of the cells, blue ones at their corners), each this share of a cell long,
        /// this opaque at most, fading in over this many beats and out over this many before the turn, flowing this many cells a beat: the red ones sideways, from the side the player is nearer to the other, the blue ones from the top
        /// or the bottom, whichever is nearer, to the other (chosen as the warning starts).
        /// </summary>
        public const int SEIJA_GRID_ROWS = 3;

        public const float SEIJA_GRID_ARROW_SIZE = 0.75f;
        public const float SEIJA_GRID_ALPHA = 0.175f;
        public const double SEIJA_GRID_FADE_IN_BEATS = 1;
        public const double SEIJA_GRID_FADE_OUT_BEATS = 0.5;
        public const float SEIJA_GRID_CELLS_PER_BEAT = 0.5f;

        /// <summary>Seija: the red circle that closes in on the player after the screen turns upside down stays round them until it turns back, then fades out over this many beats.</summary>
        public const double SEIJA_CIRCLE_GONE_BEATS = 0.5;

        /// <summary>Seija's picture turns from mirrored (both axes) to normal in this long (ms) when her attack starts (0 = at once).</summary>
        public const double MIRROR_ART_TURN_MS = 0;

        /// <summary>Clownpiece's moon is this many times as big (radius) as an osu! hit circle.</summary>
        public const float MOON_RADIUS_FACTOR = 2.5f;

        /// <summary>The moon and the laser source circle the screen's centre; the outer edge of that system (moon included) is this fraction of the screen's height.</summary>
        public const float MOON_ORBIT_EXTENT = 0.95f;

        /// <summary>The beam sound of Clownpiece's moon laser is this loud (1 = like any other beam).</summary>
        public const double MOON_SOUND_VOLUME = 0.5;

        /// <summary>The moon (and the laser source opposite it) needs this many beats (4/1) for one turn.</summary>
        public const double MOON_ORBIT_BEATS = 32;

        /// <summary>The moon's laser has this many beams, spread evenly; they turn as fast as the moon circles.</summary>
        public const int MOON_BEAM_COUNT = 3;

        /// <summary>The lines of stars of the moon's laser source fly this much faster than other bullets.</summary>
        public const float MOON_STREAM_SPEED_FACTOR = 2f;

        /// <summary>The stars of a line are this fraction of a star radius apart (half as dense as they once were: 0.55 became 1.1, so they no longer overlap into a continuous line).</summary>
        public const float MOON_STREAM_SPACING_FACTOR = 1.1f;

        /// <summary>
        /// A moon beam widens like a parabola (a U): its half width (in osu! hit circle radii) is the first value at the laser source and the second one a screen height away,
        /// and widens like a parabola in between (fast at first, ever slower; it stops widening after that).
        /// </summary>
        public const float MOON_BEAM_START_HALF_WIDTH_FACTOR = 0.5f;

        public const float MOON_BEAM_END_HALF_WIDTH_FACTOR = 2f;

        /// <summary>A moon beam is drawn as a polyline with this many points (so the curve of its edges is smooth).</summary>
        public const int MOON_BEAM_POINTS = 24;

        /// <summary>The moon grows to its full size during the warning before the attack (no damage then, but it already stops bullets); its beams grow in over this long (ms) when the attack starts.
        /// Both shrink away over MOON_FADE_OUT_MS after it ends.</summary>
        public const double MOON_FADE_IN_MS = 400;

        public const double MOON_FADE_OUT_MS = 300;

        /// <summary>Bullets are checked against the moon this often (ms of game time; coarser after a seek).</summary>
        public const double MOON_SCAN_STEP_MS = 10;

        /// <summary>A ring is looked at in at most this many steps a frame, however long the frame took: if a long frame cost more than a short one, the next frame would be longer still, and so on.</summary>
        public const int SCAN_MOST_STEPS = 4;

        /// <summary>The boss's approach circle lasts this many times as long as a hit circle's.</summary>
        public const double BOSS_APPROACH_DURATION_FACTOR = 2;

        /// <summary>The time (ms) a hit circle's approach circle takes at this approach rate: osu!'s 1800 / 1200 / 450 range.</summary>
        public static double ApproachPreempt(double approachRate)
        {
            double ar = Math.Clamp(approachRate, 0, 11);
            return ar < 5 ? 1800 - 600 * ar / 5 : 1200 - 750 * (ar - 5) / 5;
        }

        /// <summary>A spinner's laser flies this many times as fast as the spinner's bullets.</summary>
        public const float SPINNER_LASER_SPEED_FACTOR = 2f;

        /// <summary>HP regenerates this share of the whole bar per second, whatever the map's HP.</summary>
        public const double HP_REGENERATION_PER_SECOND = 0.1;

        /// <summary>Touching a laser (and no slider body) costs this fraction of the slider damage tick.</summary>
        public const double LASER_DAMAGE_SCALE = 0.5;

        /// <summary>A laser is this fraction as wide as a bullet of the same ring (drawn and hurting).</summary>
        public const float LASER_SIZE_SCALE = 0.5f;

        /// <summary>The slider path is sampled this often (osu!pixels) to build a laser's curve, before it is simplified.</summary>
        public const float LASER_SAMPLE_SPACING = 3f;

        /// <summary>
        /// The sampled slider path is simplified until it is at most this far (osu!pixels) from the original curve.
        /// Straight parts collapse into one segment, which is what keeps lasers cheap to draw and to collide.
        /// </summary>
        public const float LASER_SIMPLIFY_TOLERANCE = 0.4f;

        /// <summary>When the player dies, bullets fall with this gravity (osu!pixels per ms squared, 1000 px/s^2).</summary>
        public const float DEATH_GRAVITY = 0.001f;

        /// <summary>Hitclap (ellipse) bullets fall with a thirty-second of the death gravity (1000 / 32 = 31.25 px/s^2, arrowheads too) on a map with the reference circle size...</summary>
        public const float ELLIPSE_GRAVITY = DEATH_GRAVITY / 32f;

        /// <summary>
        /// Hitwhistle (arrowhead) bullets fall like ellipse ones (same gravity), but the pull always points at where the player is right now,
        /// until the bullet has passed the player; then it flies straight on, and it is removed when it leaves the screen. This is only a
        /// safety limit for a bullet that never gets away (it keeps circling the player): effectively no lifetime cap.
        /// </summary>
        public const double HOMING_LIFETIME_MS = 120000;

        /// <summary>
        /// A pulled arrowhead stops turning for good the first time it points at the player, i.e. its heading is within this angle (degrees) of the
        /// direction to the player. It then flies straight on at constant speed.
        /// </summary>
        public const float HOMING_AIMED_ANGLE_DEGREES = 1f;

        public static readonly float HOMING_AIMED_COSINE = MathF.Cos(HOMING_AIMED_ANGLE_DEGREES * MathF.PI / 180f);

        public static readonly float HOMING_AIMED_TANGENT = MathF.Tan(HOMING_AIMED_ANGLE_DEGREES * MathF.PI / 180f);

        /// <summary>
        /// A pulled arrowhead that gets within this many osu! circle radii of the player is not pulled any more: it flies straight on and its speed
        /// becomes that of a circle bullet of its ring (no acceleration either).
        /// </summary>
        public const float HOMING_RELEASE_RADIUS_FACTOR = 3f;

        /// <summary>CS -> radius (osu!pixels) of a standard hit circle on that map: 64 x scale, scale = (1 - 0.7 (CS - 5) / 5) / 2.</summary>
        public static float OsuCircleRadius(double circleSize)
            => 64f * (1f - 0.7f * ((float)circleSize - 5f) / 5f) / 2f;

        /// <summary>The radius of a plain bullet of the map whose osu! circles have this radius (<see cref="OsuCircleRadius"/> turned round, then <see cref="BulletRadius"/>).</summary>
        public static float BulletRadiusForCircle(float circleRadius)
            => BulletRadius(5.0 + (1.0 - circleRadius / 32.0) / 0.14);

        /// <summary>
        /// A round bullet drawn at least this many times as big as a plain bullet is drawn as sharp as Mokou's ball (from a big picture) instead of by the bullet renderer, however many of them there are.
        /// </summary>
        public const float SHARP_BULLET_SIZE = 1.5f;

        /// <summary>
        /// Song select's bars beside the counts of a map (<see cref="Stars.DanmakusuBeatmap"/>): full at this many bullets a second, this many beams a minute (from its first object to its last), and this many
        /// bosses it has room for. (The user's hard example maps: 46-118 bullets a second, 46-154 beams a minute, room for 11-26 bosses.)
        /// </summary>
        public const double STAT_BULLETS_FULL = 120;

        public const double STAT_BEAMS_FULL = 160;
        public const int STAT_BOSSES_FULL = 30;

        /// <summary>No bullet is drawn smaller than this many times a plain bullet, whatever has been done to it.</summary>
        public const float BULLET_SMALLEST = 0.2f;

        /// <summary>In play (not a replay), time going back by less than this (ms) is the clock slipping after a heavy frame, not a seek: the field holds still until it has caught up instead of rewinding.</summary>
        public const double CLOCK_SLIP_MS = 1500;

        /// <summary>Something flying straight that a pull turns back (Utsuho's black hole: the moon's stars, the stage's bullets) lives this much longer besides twice its own life (it flies a longer way, more slowly).</summary>
        public const double PULLED_EXTRA_LIFE_MS = 2000;

        /// <summary>
        /// A frame of the field slower than this (ms), or a wait twice as long between two frames, is written to osu!'s log. When gameplay time has stood still this long (ms) with nothing paused and the
        /// clock further away from it than this (ms; the limit osu! itself goes by, past it gameplay would stand for good), the clock is put back on gameplay.
        /// </summary>
        public const double SLOW_FRAME_LOG_MS = 150;

        /// <summary>A second of the map in which the field took this many ms a frame on average is written to the log.</summary>
        public const double SLOW_SECOND_LOG_MS = 12;

        public const double CLOCK_STUCK_MS = 100;
        public const double CLOCK_LOCK_MS = 500;

        /// <summary>
        /// Arrowheads never speed up or slow down: only the part of the pull across their flight is used, multiplied by this, so they turn eight times
        /// as fast as plain gravity would turn them, at constant speed.
        /// </summary>
        public const float HOMING_TURN_SCALE = 8f;

        /// <summary>Pulled bullets are simulated in steps this long (ms), so the result does not depend on the frame rate.</summary>
        public const double HOMING_STEP_MS = 8;

        /// <summary>...CS 5. Other circle sizes scale the gravity the way they scale the bullet radius.</summary>
        public const float ELLIPSE_GRAVITY_REFERENCE_CS = 5f;

        /// <summary>CS 0..10 -> gravity (px/ms^2) of falling bullets: proportional to the bullet radius (0.53x..1.47x of <see cref="ELLIPSE_GRAVITY"/>).</summary>
        public static float EllipseGravity(double circleSize)
            => ELLIPSE_GRAVITY * BulletRadius(circleSize) / BulletRadius(ELLIPSE_GRAVITY_REFERENCE_CS);

        /// <summary>true = bullets keep the velocity they had when the player died (they fall in an arc); false = they drop straight down.</summary>
        public const bool DEATH_BULLETS_KEEP_VELOCITY = true;

        /// <summary>Minimum time between two miss sounds.</summary>
        public const double MISS_SOUND_COOLDOWN_MS = 1000;

        /// <summary>Bullets whose colour has every channel at least this bright count as white and get the dark-centre gradient.</summary>
        private const float white_channel_min = 0.85f;

        public static bool IsWhiteBullet(Color4 colour)
            => colour.R >= white_channel_min && colour.G >= white_channel_min && colour.B >= white_channel_min;

        /// <summary>Colour (and strength, via alpha) of the gradient in the centre of white bullets: dark in the middle, fading out to the white body.</summary>
        public static readonly Color4 WHITE_BULLET_CENTRE_COLOUR = new Color4(0.08f, 0.08f, 0.1f, 0.9f);

        /// <summary>Fraction of the drawn bullet radius that actually collides.</summary>
        public const float BULLET_HITBOX_SCALE = 0.7f;

        /// <summary>Global multiplier on every base bullet speed (AR 0 / 5 / 10 -> 22.5 / 50 / 105 px/s at 0.25).</summary>
        public const float BULLET_SPEED_SCALE = 0.25f;

        /// <summary>Size multiplier of the character (ring and hitbox).</summary>
        public const float CHARACTER_SCALE = 0.25f;

        /// <summary>Size multiplier of the osu! cursor (and its trail), applied on top of the user's own cursor size setting.</summary>
        public const float CURSOR_SCALE = 0.25f;

        /// <summary>Diameter of the character's visible ring, in osu!pixels.</summary>
        public const float CHARACTER_RING_SIZE = 26f * CHARACTER_SCALE;

        /// <summary>Radius of the character's hitbox, in osu!pixels (independent of the character and cursor size).</summary>
        public const float PLAYER_HITBOX_RADIUS = 1f;

        /// <summary>While the character touches an active slider it takes the standard bullet damage this often.</summary>
        public const double SLIDER_DAMAGE_INTERVAL_MS = 500;

        // ---- the star rating by bullet density (see BulletDensity.cs) ----

        /// <summary>The map is measured in steps this long (ms of real time).</summary>
        public const double DENSITY_STEP_MS = 50;

        /// <summary>The density at a moment is that of about the last this many ms (each step counts for less the older it is).</summary>
        public const double DENSITY_MEMORY_MS = 1000;

        /// <summary>The map is cut into parts this long (ms); the densest moment of each part is its value, and the parts count from the densest down, each this times as much as the one before.</summary>
        public const double DENSITY_SECTION_MS = 400;

        public const double DENSITY_SECTION_WEIGHT = 0.9;

        /// <summary>A beam is as dangerous as bullets that cut through the stretch it covers once every this many ms.</summary>
        public const double DENSITY_BEAM_SWEEP_MS = 125;

        /// <summary>
        /// Stars = scale x value ^ power (value: the weighted parts, in playfields cut through per second). Chosen so that the stars come out closest to osu!'s own on the example maps (the order of
        /// the maps comes from the density alone; `sectioncheck stars` prints the fit).
        /// </summary>
        public const double DENSITY_STAR_SCALE = 1.22;

        public const double DENSITY_STAR_POWER = 0.43;

        // ---- Kagerou's and Elly's claws (see ClawAct.cs) ----

        /// <summary>
        /// Kagerou: every this many beats she throws herself at where the player is, this many times as fast as a bullet, and stays this many beats; only while she flies she shoots rings of this many bright white bullets (this much glow), one every this many beats;
        /// while she stays a claw every this many beats: this many arcs round her, this many circle radii apart and from their middle to each side, this many degrees to each side of the player, at least this many circle radii from her.
        /// </summary>
        public const double KAGEROU_EVERY_BEATS = 16;

        public const float KAGEROU_MOVE_SPEED = CHEN_SPEED_FACTOR;

        /// <summary>Kagerou: how many recorded swings her claws sound with (Resources/Samples/kagerou_swing_N.wav, the first four seconds of the user's "Various Swing Sound Effect", cut one by one).</summary>
        public const int KAGEROU_SWINGS = 10;

        /// <summary>Her rings' bullets fly this many times as fast as a plain bullet. Her claws grow, see-through, for this many beats and are solid for this many (Elly's go by CLAW_* and ELLY_SLOWER).</summary>
        public const float KAGEROU_RING_SPEED = 2f;
        public const float KAGEROU_STAY_RING_SPEED = 0.5f; // (while she stays her rings fly this much as fast)
        public const float KAGEROU_CLAW_TILT_DEGREES = 45f; // (each claw is turned this far round where it goes through the player, one way and the next the other: cross swings)

        public const double KAGEROU_GROW_BEATS = 2;
        public const double KAGEROU_SOLID_BEATS = 1;

        /// <summary>Of her claw's preview: it has grown from tip to tip after this many beats (and stays so until it lights up), and is this visible.</summary>
        public const double KAGEROU_SPREAD_BEATS = 0.5;

        public const float KAGEROU_GHOST_ALPHA = MIMA_GHOST_ALPHA * 0.5f;

        public const double KAGEROU_STAY_BEATS = 8;
        public const int KAGEROU_RING_BULLETS = 16;
        public const float KAGEROU_RING_BLOOM = 0.8f;
        public const double KAGEROU_RING_EVERY_BEATS = 0.5;
        public const int KAGEROU_ARCS = 3;
        public const float KAGEROU_ARC_GAP_CIRCLES = 2.8f;
        public const float KAGEROU_ARC_HALF_WIDTH_CIRCLES = 0.1125f;
        public const float KAGEROU_ARC_DEGREES = 16.25f;
        public const float KAGEROU_SMALLEST_RADIUS_CIRCLES = 3f;

        /// <summary>Every this many-th claw of hers is heard, as it lights up.</summary>
        public const int KAGEROU_SOUND_EVERY = 2;

        /// <summary>A claw comes as Mima's moon comes, four times as fast: it grows for this many beats, lights up over this many, is solid for this many after it has grown, and fades over this many. What is in it when it lights up is cut for this many ms; a piece hits this much of what the whole did (every piece on the screen is drawn); a beam is cut into pieces this many ms of its firing long.</summary>
        public const double CLAW_GROW_BEATS = 1;

        public const double CLAW_LIGHT_BEATS = 0.25;
        public const double CLAW_SOLID_BEATS = 2;
        public const double CLAW_FADE_BEATS = 0.25;
        public const double CLAW_CUT_MS = 60;
        public const float CLAW_PIECE_HIT = 0.7f;

        /// <summary>A cut lies where the arc runs through the bullet, but the smaller piece is never less than this share of the whole (0.2: 8 to 2 at the most uneven).</summary>
        public const float CLAW_CUT_SMALLEST = 0.2f;

        /// <summary>A beam comes apart where the claw runs through it, but the longer part is never more than this many times the shorter.</summary>
        public const double CLAW_BEAM_UNEVEN = 20;

        public const double CLAW_BEAM_PIECE_MS = 60;

        /// <summary>
        /// Elly: everything of her claw takes this many times as long; one every this many beats, this share of the playfield's height from her, this many circle radii from its middle to each side, this many degrees to each side of the player. She slides after the player at this many times a bullet's speed
        /// until they are this share of her reach away. Her scythe is only there with a blow: it swings at this many degrees a beat with the arc lighting up and fades over this many beats, its blade this many radians ahead of the shaft, curving in by this share of her reach and this many circle radii wide where it starts.
        /// </summary>
        public const double ELLY_SLOWER = 2;

        public const double ELLY_SLASH_EVERY_BEATS = 2;
        public const float ELLY_RADIUS = 0.5f;
        public const float ELLY_ARC_HALF_WIDTH_CIRCLES = 0.45f;
        public const float ELLY_ARC_DEGREES = 80f;
        public const float ELLY_SLIDE_SPEED = 6f;
        public const float ELLY_BAT_SPEED = 1f; // (her bats, which do not home, fly as fast as what they were)
        public const float ELLY_BAT_COUNT_FACTOR = 2f; // (twice as many in every ring of hers as a boss's stars)
        public const float ELLY_NEAR = 0.95f;
        public const float ELLY_SWING_DEGREES_PER_BEAT = 560f;
        public const double ELLY_SCYTHE_FADE_BEATS = 0.5;
        public const float ELLY_BLADE_LENGTH = 0.4f;
        public const float ELLY_BLADE_CURVE = 0.06f;
        public const float ELLY_BLADE_WIDTH_CIRCLES = 0.35f;
        public const float ELLY_SHAFT_HALF_WIDTH_CIRCLES = 0.28f; // (the shaft, from its middle to its edge: it hurts like the blade while it swings)

        // ---- Hecatia's World's Ender (see WorldEnderAct.cs) ----

        /// <summary>
        /// The Earth grows over this many beats, the screen shaking this much (and again from the cut until the split), to this share of the playfield's height across; the big picture has come up over the small one after this many beats. This many beats after it has grown the Sun, this share of its size, crosses it in this many beats; this many beats after the cut the Earth splits, and the impact frame is the
        /// last this many ms before that. The explosion takes this many beats. The halves drift off at this many Earth radii a beat, for this many beats. The Moon goes round once in this many beats, its outer edge just touching the left and right walls. Each end of the line throws this many rings of this many golden bullets over this many degrees, this many beats apart, and this many pieces of the Earth at this share of a
        /// bullet's speed, starting this many circle radii inside the screen, this many times as big at the split; along the line this many places throw this many bullets to each side. The impact frame has this many sharp lines, new every this many ms, this many stars, new every this many ms, and streaks and arcs on this many bullets at most, a streak reaching this share of the way to the middle.
        /// </summary>
        public const double ENDER_GROW_BEATS = 4;

        public const float ENDER_SHAKE = 0.5f;
        public const float ENDER_EARTH_SIZE = 1f;
        public const double ENDER_PICTURE_BEATS = 1;
        public const double ENDER_WAIT_BEATS = 4;
        public const float ENDER_SUN_SIZE = 0.4f;
        public const double ENDER_SWEEP_BEATS = 0.5;
        public const double ENDER_SPLIT_DELAY_BEATS = 2;
        public const double ENDER_IMPACT_MS = 200; // (in ms, whatever the map's tempo)
        public const double ENDER_EXPLOSION_BEATS = 1;
        public const float ENDER_HALVES_SPEED = 0.2f;
        public const double ENDER_APART_BEATS = 6;

        /// <summary>What the Earth breaks into at the split spreads over this many degrees, on each side of the cut: the further along the cut it was, the further that way it goes. The very big chunks turn up to this many degrees a beat.</summary>
        public const float ENDER_BIG_FAN_DEGREES = 120f;

        public const float ENDER_BIG_SPIN = 3f;
        public const double ENDER_SUN_FADE_BEATS = 1;

        /// <summary>One comes every this many beats of an attack of hers; an attack shorter than that has none.</summary>
        public const double ENDER_EVERY_BEATS = 32;

        /// <summary>The explosion shakes the screen this hard, dying away over this many beats.</summary>
        public const float ENDER_AFTER_SHAKE = 1f;

        public const double ENDER_AFTER_SHAKE_BEATS = 8;

        /// <summary>The bullets from the ends of the line fly at this much of a bullet's speed at the slowest and at the fastest.</summary>
        public const float ENDER_BURST_SLOWEST = 0.5f;

        public const float ENDER_BURST_FASTEST = 1.5f;

        /// <summary>After the explosion the bullets blur towards the middle as in Gengetsu's attacks, this many times as strongly and with this many echoes, fading out over this many beats.</summary>
        public const float ENDER_AFTER_BLUR_STRENGTH = 2f;

        public const int ENDER_AFTER_BLUR_COPIES = 5;

        public const double ENDER_AFTER_BLUR_BEATS = 8;
        public const double ENDER_MOON_BEATS = 24;

        /// <summary>The links of her chains fly off as bullets when the World's Ender takes over, at this much of a bullet's speed at the slowest and at the fastest.</summary>
        public const float ENDER_CHAIN_SLOWEST = 0.6f;

        public const float ENDER_CHAIN_FASTEST = 1.1f;
        public const int ENDER_RINGS = 5;
        public const int ENDER_RING_BULLETS = 12;
        public const float ENDER_SPREAD_DEGREES = 90f;
        public const double ENDER_RING_GAP_BEATS = 0.125;
        public const float ENDER_SHARD_SPEED = 0.5f;
        public const float ENDER_PIECE_HIT = 0.3f;
        public const float ENDER_PIECE_SPIN = 30f;
        public const int ENDER_LINE_POINTS = 24;
        public const int ENDER_LINE_BULLETS = 3;
        public const int ENDER_PEN_LINES = 36;
        public const double ENDER_PEN_LINE_MS = 30;
        public const double ENDER_PEN_WOBBLE_MS = 16; // (between two new sets the lines still waver: a little turn and another width this often)
        public const float ENDER_PEN_WOBBLE = 0.35f; // (of its width)
        public const float ENDER_PEN_TURN_DEGREES = 2f;
        public const float ENDER_PEN_JITTER = 0.8f; // (the lines stand evenly round the middle of the impact, each off its place by up to this share of the gap between two)
        public const double ENDER_CRACKS_BEATS = 0.25; // (the cracks come up this fast once the Sun begins to cut)
        public const int ENDER_CRACK_BLUR_COPIES = 4; // (in the impact frame the white cracks are blurred out from the middle of the screen: this many fainter copies, the furthest this share bigger and further out, the nearest this opaque)
        public const float ENDER_CRACK_BLUR_SPREAD = 0.15f;
        public const float ENDER_CRACK_BLUR_ALPHA = 0.5f;
        public const float ENDER_BULLET_SIZE = 0.5f; // (the golden bullets of the cut and the split, of a plain bullet)
        public const float ENDER_BULLET_BLOOM = 1f;
        public const float ENDER_BULLET_GLOW = 4f; // (a soft orange light under each, this many times its size across)
        public const float ENDER_EARTH_HIT = 0.95f; // (the grown Earth hurts like a slider body within this share of its radius)
        public const float ENDER_CHUNK_HIT = 0.42f; // (a very big chunk of the halves hurts within an ellipse this share of its picture's width and height)
        public const int ENDER_IMPACT_STARS = 5;
        public const double ENDER_STAR_MS = 45;
        public const double ENDER_STAR_WOBBLE_MS = 16;
        public const float ENDER_STAR_SHORTEST = 12f;
        public const float ENDER_STAR_LONGEST = 28f;
        public const float ENDER_STAR_UNSTABLE = 0.12f;
        public const float ENDER_STAR_CURVE = 3.5f; // (how deep the curves between the tails are: 1 is a plain diamond)
        public const float ENDER_STAR_NEEDLE = 0.01f; // (half the thickness of a tail where it starts, of the shorter reach: what is left of it near the tip)
        public const int ENDER_STAR_STEPS = 10; // (pieces in a tail, so corners in its edges)
        public const float ENDER_STAR_JAG = 0.5f; // (how far a corner sits in or out, of the tail's half thickness there)
        public const float ENDER_STAR_APART_CIRCLES = 1.5f;
        public const int ENDER_STAR_ATTEMPTS = 40;
        public const int ENDER_STAR_ECHOES = 2;
        public const float ENDER_STAR_ECHO_SHARE = 0.07f;
        public const int ENDER_WHITE_SPOTS = 500;
        public const float ENDER_STREAK_SHARE = 0.22f;

        /// <summary>A streak's lines: this many circle radii from their middle to each side, and one for every this many circle radii across the thing (three at least).</summary>
        public const float ENDER_STREAK_HALF_WIDTH = 0.045f;

        public const float ENDER_STREAK_LINE_GAP = 0.3f;

        // ---- the star rating by how much the map makes the player move (see ForcedMovement.cs) ----

        /// <summary>The lazy player decides every this many ms; they look this many ms ahead, at a place every this many ms along the way.</summary>
        public const double FORCED_STEP_MS = 100;

        public const double FORCED_HORIZON_MS = 400;
        public const double FORCED_SAMPLE_MS = 50;

        /// <summary>They walk in this many ways, and keep this many px more than they have to from what would hit them; the smallest step aside they take (px).</summary>
        public const int FORCED_WAYS = 12;

        public const float FORCED_MARGIN = 3f;
        public const float FORCED_SMALLEST_DODGE = 16f;

        /// <summary>A new way within this (cos) of the one they were going is still the same dodge: about 60 degrees.</summary>
        public const float FORCED_SAME_DODGE_COS = 0.5f;

        /// <summary>When no walk saves them they teleport to the nearest of the middles of this many by this many parts of the playfield.</summary>
        public const int FORCED_GRID_X = 16;

        public const int FORCED_GRID_Y = 12;

        /// <summary>A beam is looked at as pieces this many ms of its firing long.</summary>
        public const double FORCED_BEAM_SLICE_MS = 100;

        /// <summary>What a moment weighs: per playfield diagonal walked a second, per dodge started a second, per teleport a second.</summary>
        public const double FORCED_WALK_WEIGHT = 4;

        public const double FORCED_DODGE_WEIGHT = 0.25;
        public const double FORCED_TELEPORT_WEIGHT = 1;

        /// <summary>As in the density's rating: about the last this many ms count for a moment; the hardest moment of every part this long is its value; from the hardest down each part counts this times as much as the one before.</summary>
        public const double FORCED_MEMORY_MS = 1000;

        public const double FORCED_SECTION_MS = 400;
        public const double FORCED_SECTION_WEIGHT = 0.9;

        /// <summary>Stars = scale x value ^ power. Chosen so that the stars come out closest to osu!'s own on the example maps (the order of the maps comes from the movement alone; `sectioncheck stars` prints the fit).</summary>
        public const double FORCED_STAR_SCALE = 0.034;

        public const double FORCED_STAR_POWER = 1.519;

        /// <summary>
        /// Played faster (DT) a map is never rated below its value at the normal speed times the rate to this power, and slower (HT) never above it: the lazy player looks as far ahead in real time, which at DT covers more of the
        /// map, so on a sparse map it found places that stay safe longer and dodged less (15 % of 61 maps came out easier with DT). 0.25 is the median of what DT does to the value (x1.10 on those maps).
        /// </summary>
        public const double FORCED_RATE_POWER = 0.25;

        /// <summary>
        /// The stars (Bullets/StarRating.cs) = scale x density value ^ a x movement value ^ b: the bullet density (<see cref="DENSITY_STAR_SCALE"/>'s value) and the movement to safer places (<see cref="FORCED_STAR_SCALE"/>'s value)
        /// together, fitted to osu!'s own stars over 151 of the downloaded maps (every 70th .osu of the library). Fitted freely density got next to nothing (power 0.06: alone it follows osu!'s order far less well,
        /// rank 0.57 against movement's 0.87), so its share is fixed: a quarter of the influence (on the spread of each), which follows osu!'s stars as well as movement alone (rank 0.872 against 0.873, 0.78 stars off
        /// on average); half would cost (rank 0.82, 0.90 stars off).
        /// </summary>
        public const double STAR_SCALE = 0.0595;

        public const double STAR_DENSITY_POWER = 0.157;
        public const double STAR_MOVEMENT_POWER = 1.199;

        // ---- the stages of Hecatia, Nue, Raiko, Yuyuko and Utsuho (see BulletField.Stage.cs) ----

        /// <summary>Okina's portals: how much of the wall they cover (1 = all of it), how thick they are drawn (px), and how opaque they are.</summary>
        public const float OKINA_PORTAL_LENGTH = 0.45f;

        public const float OKINA_PORTAL_THICKNESS = 11f;

        /// <summary>The whole screen shakes this much (px) while the warning before an attack of Okina's is on (the portals grow meanwhile).</summary>
        public const float OKINA_PORTAL_SHAKE_PX = 1f;

        /// <summary>A portal slides along the border of the screen (round the corners) at most this fast (px per ms) to where it wants to be: the wall Okina is nearest to (the other one: across from it, behind the player).</summary>
        public const float OKINA_PORTAL_SLIDE_SPEED = 1.2f;

        /// <summary>The indicators round a portal: how far into the screen they reach (px), how many particles there are, how long one takes to go in or come out (ms), how big they are (px), how bright the cone is.</summary>
        public const float OKINA_PORTAL_REACH = 70f;

        public const int OKINA_PORTAL_PARTICLES = 14;
        public const double OKINA_PORTAL_PARTICLE_MS = 900;
        public const float OKINA_PORTAL_PARTICLE_SIZE = 7f;
        public const float OKINA_PORTAL_CONE_ALPHA = 0.5f;
        public const float OKINA_PORTAL_ALPHA = 0.9f;

        /// <summary>Everything of a stage appears over this long and goes away over <see cref="STAGE_FADE_OUT_MS"/>.</summary>
        public const double STAGE_FADE_IN_MS = 400;

        public const double STAGE_FADE_OUT_MS = 300;

        /// <summary>Hecatia: how long the worlds take to move over from one attack to the next (the Earth and the Moon settle into their orbits, the Sun grows out of her or shrinks back into her).</summary>
        public const double HECATIA_TRANSITION_MS = 2000;

        /// <summary>Hecatia: attacks of two different sections (a spinner, a break, the next section) that are at most this far apart (ms) go on from each other: the worlds are not reset.</summary>
        public const double HECATIA_JOIN_MS = 8000;

        /// <summary>Hecatia: how long the Earth and the Moon take to return from a throw into their orbits round her (half of what it was).</summary>
        public const double HECATIA_RETURN_MS = 1000;

        /// <summary>Hecatia: every attack (throwing the Earth and the Moon, or turning the worlds) lasts this many times as long as the usual 16 beats.</summary>
        public const double HECATIA_PHASE_FACTOR = 2.0;

        /// <summary>Hecatia: the Sun's own rings have this many times the usual number of bullets.</summary>
        public const float HECATIA_SUN_COUNT_FACTOR = 0.3f;

        /// <summary>Hecatia: a world that comes out of a throw keeps flying the way it flew and slows down (its speed falls off over this long, ms) while it is drawn into its orbit.</summary>
        public const double HECATIA_COAST_MS = 350;

        /// <summary>The pre-simulations (Hecatia's planets and chains) advance in steps this long (ms).</summary>
        public const double STAGE_STEP_MS = 4;

        /// <summary>Hecatia's Earth, Moon and Sun: radius as a multiple of a hit circle's radius.</summary>
        public const float HECATIA_EARTH_RADIUS = 2.25f;

        public const float HECATIA_MOON_RADIUS = 1.35f;
        public const float HECATIA_SUN_RADIUS = 2.7f;

        /// <summary>Radius of the circle the three worlds turn on round her (multiple of a hit circle's radius), and how many beats one turn takes.</summary>
        public const float HECATIA_ORBIT_RADIUS = 3.15f;

        public const double HECATIA_ORBIT_BEATS = 32;

        /// <summary>The planets are thrown at this multiple of an ordinary bullet's speed (the Moon a bit faster), this many radians to either side of the player.</summary>
        public const float HECATIA_THROW_SPEED = 2.2f;

        public const float HECATIA_MOON_SPEED_FACTOR = 1.15f;
        public const float HECATIA_THROW_SPREAD = 0.22f;

        /// <summary>When a planet ricochets it throws this many bullets to each side, at this multiple of an ordinary bullet's speed.</summary>
        public const int HECATIA_SIDE_BULLETS = 12;

        /// <summary>The bullets to one side of a hit are spread over this many radians in all.</summary>
        public const float HECATIA_SIDE_FAN = 0.6f;

        /// <summary>The spot of the Earth / Moon that hit something glows orange for this long (ms).</summary>
        public const double HECATIA_GLOW_MS = 1000;

        public const float HECATIA_SIDE_SPEED = 1.3f;

        /// <summary>The Earth and the Moon push the bullets they touch away: the bullet flies on at this multiple of its own speed (or this multiple of the world's own speed, if that is more, so it gets away from it), starting this far (px) outside the world.</summary>
        public const float HECATIA_PUSH_SPEED_FACTOR = 1.8f;

        public const float HECATIA_PUSH_OUTRUN = 1.25f;
        public const float HECATIA_PUSH_CLEARANCE = 1f;
        public const double HECATIA_PUSH_FLASH_MS = 220; // (the flash where a bullet bounces off the Earth or the Moon)
        public const float HECATIA_PUSH_FLASH_SIZE = 1.6f; // (circle radii)

        /// <summary>The Sun shoots stars this much more often than an ordinary boss would (the stars come at this multiple of the usual interval).</summary>
        public const double HECATIA_SUN_INTERVAL_FACTOR = 0.5;

        /// <summary>Every world hangs on a chain of circle bullets that starts at Hecatia: the chain is simulated with this many beads' worth of links (they behave like a chain; the beads are drawn along it without gaps).</summary>
        public const int HECATIA_CHAIN_BEADS = 38;

        /// <summary>The chain is this much longer than the distance from Hecatia to its world when it has caught up (so it hangs a little slack), and it catches up with a delay: this is how long (ms) it takes to cover most of the difference, both when it grows and when it shortens.</summary>
        public const float HECATIA_CHAIN_SLACK = 0.1f;

        public const double HECATIA_CHAIN_LAG_MS = 80;

        /// <summary>How much of its speed a link of the chain keeps every <see cref="STAGE_STEP_MS"/>.</summary>
        public const float HECATIA_CHAIN_DAMPING = 0.992f;

        public const float HECATIA_BEAD_RADIUS = 0.2f;

        /// <summary>
        /// While she throws the Earth and the Moon, each gathers what it touches (at most this many at once; more are taken away or pushed as before), unseen inside it (<see cref="GATHER_SHOW_HELD"/>: true
        /// draws them under it, spread evenly over a disc this much bigger than the world), and shoots it all out at its next impact. Unzan's hands do the same (<see cref="ICHIRIN_GATHER_MAX"/>).
        /// </summary>
        public const int HECATIA_GATHER_MAX = 64;

        public const bool GATHER_SHOW_HELD = false;
        public const float GATHER_SPREAD = 1.15f;

        /// <summary>
        /// What was gathered is dealt out into this many rings, each spread evenly over this many degrees away from the wall (or what was hit), shot from the holder's middle one after the other this many
        /// beats apart (every second turned half a step), at this times the stage's bullet speed.
        /// </summary>
        public const int GATHER_RINGS = 5;

        public const float GATHER_SPREAD_DEGREES = 180f;
        public const double GATHER_RING_GAP_BEATS = 0.25;
        public const float GATHER_SPEED = 1f;

        /// <summary>A throw of the Earth and the Moon is never cut short: when the attack ends each flies on to its next impact (at most this many beats) and only then fades or goes back to its orbit.</summary>
        public const double HECATIA_OVERRUN_BEATS = 8;

        /// <summary>Nue's UFOs: width as a multiple of a hit circle's radius; the half angle of the cone that sucks the bullets in; how far the cone reaches (multiple of a hit circle's radius).</summary>
        public const float NUE_UFO_WIDTH = 2.4f;

        public static float NUE_CONE_HALF_ANGLE_DEGREES => Bosses.BossTuning.NueCone.Value;
        public const float NUE_CONE_LENGTH = 16f;

        /// <summary>While the UFOs shoot, their cones (where the bullets go) are this many times as wide as the ones that pull.</summary>
        public const float NUE_SHOOT_CONE_FACTOR = 1f;

        /// <summary>Nue's UFOs shoot out what they took in this many times as fast as the time between her attacks would let them (the shooting ends that much sooner).</summary>
        public const double NUE_SHOOT_RATE = 0.5;

        public static float NUE_SHOOT_CONE_HALF_ANGLE_DEGREES => NUE_CONE_HALF_ANGLE_DEGREES * NUE_SHOOT_CONE_FACTOR;

        /// <summary>At most this many UFOs per attack (one every second beat).</summary>
        public const int NUE_MAX_UFOS = 20;

        /// <summary>How long (beats) it takes the UFOs to gather in the middle; the shooting starts after this (at most a third of the gap to her next attack).</summary>
        public static double NUE_GATHER_BEATS => Bosses.BossTuning.NueGatherBeats.Current;

        /// <summary>How long the UFOs shoot when no attack follows (beats); the stored bullets come out at this multiple of their speed; the UFO turns this far (degrees) towards the player while it shoots.</summary>
        public const double NUE_SHOOT_BEATS = 8;

        public static float NUE_SHOOT_SPEED_FACTOR => Bosses.BossTuning.NueShootSpeed.Value;

        /// <summary>Nue: a bullet that enters a UFO's cone is pulled straight to the UFO at this multiple of its own speed (the speed does not change on the way).</summary>
        public static float NUE_PULL_SPEED_FACTOR => Bosses.BossTuning.NuePull.Value;

        /// <summary>Nue's UFOs pull only every this many-th bullet of a ring; the others fly through the cones. Every bullet they took in is shot out this many times.</summary>
        public const int NUE_PULL_EVERY = 4;

        public const int NUE_SHOT_COPIES = 4;

        /// <summary>Nue's UFOs turn round the middle on their circle while they shoot at this share of a spinner's beams' rate.</summary>
        public const float NUE_TURN_SHARE = 0.125f;

        /// <summary>How many UFOs Nue sends in every attack.</summary>
        public const int NUE_UFOS = 8;

        /// <summary>How opaque the UFOs' cones are drawn (the pulling ones and the shooting ones).</summary>
        public const float NUE_CONE_ALPHA = 0.5f;

        /// <summary>Nue's cones are on the UFOs from when they are sent; while they only warn (the UFOs still flying to their places) they are this share as bright as when they pull or shoot.</summary>
        public const float NUE_CONE_WARN_SHARE = 0.4f;
        public const float NUE_SWEEP_DEGREES = 60f;

        /// <summary>Nue's UFOs, as they shoot, turn from NUE_SWEEP_DEGREES off to their even angles (straight out from the middle) over this share of the shooting, and shoot straight out for the rest.</summary>
        public const double NUE_SWEEP_SHARE = 0.5;

        /// <summary>Nue: the playfield shakes (0..1) while the UFOs pull and while they shoot; the place round every UFO is darkened (this alpha, this many UFO widths across) meanwhile; the shot bullets glow this much.</summary>
        public const float NUE_SHAKE = 0.5f;

        public const float NUE_DIM_ALPHA = 0.6f;
        public const float NUE_DIM_SIZE = 4f;
        public const float NUE_SHOT_BLOOM = 0.9f;

        /// <summary>Raiko's taikos: how many; the radius of the circle they turn on (multiple of a hit circle's radius, at least <see cref="RAIKO_ORBIT_MIN"/> pixels); size; beats per turn.</summary>
        public const int RAIKO_TAIKO_COUNT = 5;

        public const float RAIKO_ORBIT_RADIUS = 2.25f;
        public const float RAIKO_ORBIT_MIN = 55f;
        public const float RAIKO_TAIKO_SIZE = 2.6f;
        public const double RAIKO_ORBIT_BEATS = 16;

        /// <summary>Beats between two hits of Raiko's drums in a row of the pattern (the drum after the second stick comes half of it later, and the next round half of it after that).</summary>
        public const double RAIKO_SLAM_BEATS = 4;

        /// <summary>Only every this many-th hit of the drums sends a ripple out (so a ripple comes every <see cref="RAIKO_SLAM_BEATS"/> x this many beats), and how bright a ripple is at the start (0..1).</summary>
        public const int RAIKO_RIPPLE_EVERY = 1;

        public const float RAIKO_RIPPLE_ALPHA = 0.3f;

        /// <summary>A ripple: its speed (pixels per ms), how deep the part that makes things bigger is (ms of its travel), a little extra width (pixels) and how much bigger things get.</summary>
        public const float RAIKO_RIPPLE_SPEED = 0.1375f;

        public const double RAIKO_RIPPLE_DEPTH_MS = 280;

        /// <summary>The bright band of a ripple is this wide (half of it on either side of its middle, in pixels): the depth that bullets are affected over, and it stays the same however far the ripple has got.</summary>
        public const float RAIKO_RIPPLE_HALF_WIDTH = (float)(RAIKO_RIPPLE_SPEED * RAIKO_RIPPLE_DEPTH_MS * 0.5);
        public const float RAIKO_RIPPLE_MARGIN = 4f;
        /// <summary>Junko: her lasers (the sliders' ones) have this many times as many beams as an ordinary slider laser has.</summary>
        public static int JUNKO_BEAM_FACTOR => Bosses.BossTuning.JunkoBeams.Whole;

        /// <summary>Junko: her beams are this much of the width of a slider's beam, and turn (the path of the slider is gone along) this many times as fast.</summary>
        public static float JUNKO_BEAM_SIZE => Bosses.BossTuning.JunkoBeamSize.Value;

        /// <summary>Junko: her slices fly this many times as fast as a slider's (twice as fast as the last build: it was 0.5).</summary>
        public const float JUNKO_BEAM_SPEED = 1f;

        /// <summary>Junko: the path of the slider is gone along, and a slice lives, this many times as long (on top of the turn speed).</summary>
        public const double JUNKO_TIME_STRETCH = 2;

        /// <summary>Junko: a slice that is not ready yet (see-through and harmless) is this visible.</summary>
        public const float JUNKO_GHOST_ALPHA = 0.15f;

        /// <summary>
        /// When a map's breaks, kiai sections and spinners give the bosses less than this share of the song (or none), the intense parts of the song become boss sections too (see
        /// BulletPlanner.IntenseParts): measured in stretches of <see cref="BOSS_FILL_STEP_BEATS"/> beats, judged in phrases of <see cref="BOSS_FILL_PHRASE_BEATS"/> beats on the beat
        /// grid; gaps of at most <see cref="BOSS_FILL_HOLE_PHRASES"/> phrases between intense parts are filled, and a part is at least <see cref="BOSS_FILL_MIN_PHRASES"/> phrases long.
        /// </summary>
        public const double BOSS_MIN_SONG_SHARE = 0.5;

        public const double BOSS_FILL_STEP_BEATS = 4;
        public const double BOSS_FILL_PHRASE_BEATS = 16;
        public const int BOSS_FILL_HOLE_PHRASES = 2;
        public const int BOSS_FILL_MIN_PHRASES = 2;

        /// <summary>The build-up before an intense part (the phrases of the middle group right before it) becomes a spinner section of the boss when it is at least this many beats long.</summary>
        public const double BOSS_FILL_BUILD_UP_MIN_BEATS = 16;

        /// <summary>
        /// How much each measure counts when the intense parts are judged (a negative weight counts against a stretch): hitsound loudness most; the song's bass and the bass's share of
        /// its level; against: its drums, and how much louder it is than the 16 beats before (a build-up gets louder as it goes, a drop is loud from its start). Found with
        /// Tools/SectionCheck (climb) on every difficulty of Camellia - Chirality, USAO - Cyaegha and USAO - Blows Up Everything against the sections wanted there, choosing the set that
        /// gives the same sections on every difficulty of a song; the other measures count for nothing but are kept to be tuned.
        /// </summary>
        public const double BOSS_FILL_HITSOUND_WEIGHT = 4;

        public const double BOSS_FILL_LEVEL_WEIGHT = 1;
        public const double BOSS_FILL_TREBLE_WEIGHT = 1;
        public const double BOSS_FILL_MID_WEIGHT = -2;
        public const double BOSS_FILL_BASS_WEIGHT = 3;
        public const double BOSS_FILL_SLIDER_WEIGHT = 1;
        public const double BOSS_FILL_DENSITY_WEIGHT = 2;
        public const double BOSS_FILL_DISTANCE_WEIGHT = 2;
        public const double BOSS_FILL_DRUM_WEIGHT = -1;
        public const double BOSS_FILL_ADDITIONS_WEIGHT = 2;
        public const double BOSS_FILL_FINISHES_WEIGHT = -2;
        public const double BOSS_FILL_STREAMS_WEIGHT = -1;
        public const double BOSS_FILL_SLIDER_SHARE_WEIGHT = 1;
        public const double BOSS_FILL_KIAI_WEIGHT = 0;
        public const double BOSS_FILL_BASS_SHARE_WEIGHT = 0;
        public const double BOSS_FILL_RISE_WEIGHT = -2;

        /// <summary>A gap between intense parts is only filled when every phrase in it scores at least this share of the threshold (a real pause is lower; a short dip is not).</summary>
        public const double BOSS_FILL_HOLE_SHARE = 0.9;

        /// <summary>
        /// A song of which the score above leaves less than this share for the bosses also gets what is loud against its averages (its hitsounds and the song itself, alike): what is at least the average, and then, this much less at a time, down to this many times the
        /// average, until the share is reached. What is this many times the part's own line can be a build-up.
        /// </summary>
        public const double BOSS_FILL_FLOOR_SHARE = 0.5;

        public const double BOSS_FILL_FLOOR_STEP = 0.05;
        public const double BOSS_FILL_FLOOR_LOWEST = 0.8;
        public const double BOSS_FILL_FLOOR_CALM = 0.85;

        /// <summary>Sagume's replayed copies shoot their original's bullets this many times as big and this many times as fast (white).</summary>
        public const float SAGUME_COPY_SIZE = 1.5f;

        public const float SAGUME_COPY_SPEED = 0.5f;

        /// <summary>Sagume's replayed circles and sliders have this combo colour.</summary>
        public static readonly osuTK.Graphics.Color4 SAGUME_COPY_COLOUR = new osuTK.Graphics.Color4(0.96f, 0.96f, 0.98f, 1f);

        /// <summary>Sekibanki throws a head every this many beats of an attack; it is this many times as big as her plain bullets and lives this many beats, then fades out over this long (ms).</summary>
        public const double SEKIBANKI_THROW_BEATS = 8;

        public const float SEKIBANKI_HEAD_SIZE = 2f;
        public const double SEKIBANKI_HEAD_LIFE_BEATS = 64;
        public const double SEKIBANKI_FADE_MS = 500;

        /// <summary>The picture of the head is this many times as wide as the head's bullet (the face fills the bullet; the bow and hair stick out).</summary>
        public const float SEKIBANKI_PICTURE_SCALE = 1.3f;

        /// <summary>
        /// The beam behind a head: this many times as wide as a slider's; a slice is laid every this many steps of the head; a slice stays where it was laid for this many beats, then flies
        /// on the way the head went there (as fast as a slider's beam).
        /// </summary>
        public const float SEKIBANKI_TRAIL_WIDTH = 2f;

        public const int SEKIBANKI_TRAIL_SLICE_STEPS = 4;
        public const double SEKIBANKI_TRAIL_STAY_BEATS = 16;

        /// <summary>Doremy's mirrored copies shoot their original's bullets this many times as big, this many times as fast and at least this dark (0 = as bright, 1 = black).</summary>
        public const float DOREMY_COPY_SIZE = 0.75f;

        public const float DOREMY_COPY_SPEED = 2f;
        public const float DOREMY_COPY_SHADE = 0f;

        /// <summary>The copied bullets are white and blue in turn and glow this much (0..1).</summary>
        public const float DOREMY_COPY_BLOOM = 0.8f;

        /// <summary>Doremy's mirrored circles and sliders have this combo colour.</summary>
        public static readonly osuTK.Graphics.Color4 DOREMY_COPY_COLOUR = new osuTK.Graphics.Color4(0.1f, 0.16f, 0.6f, 1f);

        /// <summary>Doremy's own shots: two chains of rings of this many plain bullets.</summary>
        public const int DOREMY_CHAIN_BULLETS = 3;

        /// <summary>Byakuren: her own beams come from this many points round her, this far (playfield pixels) from her; in her attacks they turn round to the player (from pointing away) in this many beats.</summary>
        public const int BYAKUREN_BEAM_POINTS = 5;

        public const float BYAKUREN_POINT_RADIUS = 40f;
        public const double BYAKUREN_HOMING_BEATS = 2;

        /// <summary>Byakuren's beams in her attacks (homing like Sekibanki's heads, without one): each is laid for this many beats, and every slice of it is there for this many beats, then gone.</summary>
        public const double BYAKUREN_TRAIL_LIFE_BEATS = 16;

        public const double BYAKUREN_TRAIL_STAY_BEATS = 4;

        /// <summary>
        /// Byakuren remembers where every this many-th circle or slider of her section starts; this many beats later a beam (this share as wide as Marisa's) is fired from there at the player
        /// for this many beats; the beams follow each other at least this many beats apart. She holds this many spots at a time: with as many waiting to fire, the next one is not remembered.
        /// </summary>
        public const int BYAKUREN_RECORD_EVERY = 5;

        public const int BYAKUREN_MAX_SPOTS = 5;
        public const double BYAKUREN_DELAY_BEATS = 32;
        public const float BYAKUREN_SPOT_BEAM_SIZE = 0.25f;
        public const double BYAKUREN_BEAM_BEATS = 8;
        public const double BYAKUREN_SHOT_GAP_BEATS = 2;
        public const double BYAKUREN_SOUND_EVERY_BEATS = 8; // (her throws are heard at most this often)

        /// <summary>The big star in Byakuren's colours where a remembered spot throws its beams: fades in and out over this long (ms) and stays this many beats in between (it hurts while it is there); it is this many times its first size (picture and what hurts).</summary>
        public const double BYAKUREN_STAR_FADE_MS = 250;

        public const float BYAKUREN_SPOT_STAR_SIZE = 2f;

        public const double BYAKUREN_SPOT_STAR_BEATS = 1;

        /// <summary>Junko: every this many beats she shoots every this many-th bullet that appeared in them out again, as one ring (at their average speed).</summary>
        public const double JUNKO_RECORD_BEATS = 8;

        public const int JUNKO_RECORD_EVERY = 4;

        /// <summary>The most bullets one ring can have (what a ring has hit is kept in 64 bits): a bigger ring is made of several.</summary>
        public const int MAX_RING_BULLETS = 64;

        public static double JUNKO_TURN_FACTOR => Bosses.BossTuning.JunkoTurn.Current;

        public const float RAIKO_RIPPLE_GROW = 2f;

        /// <summary>A bullet that a ripple has swollen by at least this share of the swell (0..1: half way to <see cref="RAIKO_RIPPLE_GROW"/>) hurts like a slider body (not used up), as one in Sannyo's smoke does.</summary>
        public const float RAIKO_SLIDER_FROM = 0.5f;

        /// <summary>Things grow (and shrink again) smoothly over this part of the depth of a ripple's band, at its front and at its back.</summary>
        public const float RAIKO_RIPPLE_RAMP = 0.3f;

        /// <summary>The shake of a slam lasts this long (ms) and is this strong at first (0..1, see BEAM_SHAKE_MIN / MAX).</summary>
        public const double RAIKO_SHAKE_MS = 1000;

        /// <summary>The drum that is struck swells by this share of its size at the moment of the hit, and the swelling dies away with this time constant (ms).</summary>
        public const float RAIKO_DRUM_GROW = 0.45f;

        public const double RAIKO_DRUM_GROW_MS = 160;

        /// <summary>The line of a ripple is this thick (pixels), its edges softened over this many pixels on each side.</summary>
        public const float RAIKO_RIPPLE_LINE = 8f;

        public const float RAIKO_RIPPLE_SOFT = 4f;

        /// <summary>
        /// The hits with the sticks (every fourth hit of the pattern): the bullets closer to Raiko than this many hit circles are pushed away, at this multiple of the speed they had (at least
        /// <see cref="RAIKO_PUSH_MIN_SPEED"/> pixels per ms). The push is made over a moment this long (ms; it is made once), and the ring that shows it takes <see cref="RAIKO_PUSH_RING_MS"/> to grow to the full distance.
        /// </summary>
        public const float RAIKO_PUSH_RADIUS = 3f;

        public const float RAIKO_PUSH_SPEED_FACTOR = 2.5f;
        public const float RAIKO_PUSH_MIN_SPEED = 0.3f;
        public const double RAIKO_PUSH_WINDOW_MS = 12;
        public const double RAIKO_PUSH_RING_MS = 260;

        /// <summary>Sannyo's smoke: clouds in a ring (this many), a ring every this many beats (8); the clouds fly at this speed (px/ms: half a ripple's), start this big and swell to this size (hit circle radii, over ~this many ms), and sway a little.</summary>
        public const int SANNYO_SMOKE_COUNT = 8;

        public const double SANNYO_SMOKE_EVERY_BEATS = 8;
        public const float SANNYO_SMOKE_SPEED = RAIKO_RIPPLE_SPEED / 2f;
        public const float SANNYO_SMOKE_RADIUS_START = 1.8f;
        public const float SANNYO_SMOKE_RADIUS_END = 4.5f;
        public const double SANNYO_SMOKE_GROW_MS = 3000;
        public const float SANNYO_SMOKE_SWAY = 0.6f;
        public const double SANNYO_SMOKE_SWAY_PERIOD_MS = 2600;

        /// <summary>A cloud rises a little (px/ms) and does not fade: it goes on until it has left the playfield. It is drawn with this many puffs of this opacity (0..1) each.</summary>
        public const float SANNYO_SMOKE_RISE = 0.004f;

        public const int SANNYO_SMOKE_PUFFS = 5;
        public const float SANNYO_SMOKE_ALPHA = 0.22f;

        /// <summary>Bullets in smoke: this many times bigger (hitbox too), and this see-through (0 = not at all, 1 = invisible).</summary>
        public const float SANNYO_SMOKE_SIZE = 2f;

        public const float SANNYO_SMOKE_TRANSPARENCY = 0.5f;

        /// <summary>
        /// Kyouko's glass walls: how far in from its edge each wall stops (a part of the playfield's height / width), the part of the attack that they take to get there, how many times a bullet bounces off
        /// them (then it passes through), how close (a part of the bullet's drawn radius) the bullet's middle gets to a wall before it bounces, and how a wall looks: fade in / out (ms), opacity, thickness (at least, pixels).
        /// </summary>
        public const float KYOUKO_REACH = 0.25f;

        public const double KYOUKO_CLOSE_SHARE = 0.5;
        public const int KYOUKO_BOUNCES = 1; // (per set of walls: a bullet that has bounced off one can bounce again when new walls come)
        public const float KYOUKO_CONTACT = 0.7f;

        /// <summary>Kyouko's own shots: talismans in rings of this many bullets...</summary>
        public const int KYOUKO_RING_BULLETS = 2;

        /// <summary>...shot this many times as often as Yukari's (whose pattern is the base of hers)...</summary>
        public const double KYOUKO_FREQUENCY_FACTOR = 4;

        /// <summary>...and every ring is turned this far (radians) from the one before; the turning direction flips every time new walls appear.</summary>
        public const float KYOUKO_SPIN_STEP = SPIRAL_STEP / (float)KYOUKO_FREQUENCY_FACTOR;
        public const double KYOUKO_FADE_IN_MS = 250;
        public const double KYOUKO_FADE_OUT_MS = 150;
        public const float KYOUKO_GLASS_ALPHA = 1f;

        /// <summary>The walls also glow: a soft band of light (this wide as a multiple of the wall's thickness, this strong) along every wall.</summary>
        public const float KYOUKO_GLOW_WIDTH = 12f;

        public const float KYOUKO_GLOW_ALPHA = 0.45f;
        public const float KYOUKO_WALL_THICKNESS = 3f;

        /// <summary>
        /// Mokou: she jumps at the player in a straight line (at <see cref="MOKOU_JUMP_SPEED_FACTOR"/> times the bullet speed, never slower / faster than the limits, px per ms) until she is
        /// <see cref="MOKOU_WALL_MARGIN"/> (osu! circle radii) from the edge of the screen; there she stays for <see cref="MOKOU_HOLD_BEATS"/> beats. Touching her while she jumps hurts like a slider (her body is
        /// <see cref="MOKOU_BODY_RADIUS"/> circle radii).
        /// </summary>
        public const float MOKOU_JUMP_SPEED_FACTOR = 5f;

        public const float MOKOU_JUMP_MIN_SPEED = 0.45f;
        public const float MOKOU_JUMP_MAX_SPEED = 1.2f;
        public const float MOKOU_WALL_MARGIN = 0.9f;
        public const float MOKOU_BODY_RADIUS = 0.9f;
        public const double MOKOU_HOLD_BEATS = 8;

        /// <summary>
        /// Mokou's big circle: it grows out of her over this long (ms) when she lands, is this many times the radius of an osu! circle, and is gone over the last <see cref="MOKOU_BIG_FADE_MS"/> of her stay.
        /// A break's and a spinner's is thrown at the player (as half of this size, see MOKOU_THROW_SIZE_FACTOR) every <see cref="MOKOU_THROW_BEATS"/> beats at this many times the bullet speed (at least <see cref="MOKOU_THROW_MIN_SPEED"/> px per ms).
        /// </summary>
        public const float MOKOU_BIG_SIZE = 5f;

        public const double MOKOU_BIG_GROW_MS = 250;

        /// <summary>Every landing of Mokou shakes the screen (0..1, see BEAM_SHAKE_MIN / MAX: 1 is 2 px, the shake dies down over this long, ms).</summary>
        public const float MOKOU_SHAKE = 1f;


        /// <summary>
        /// Mokou's big circle glows and darkens the bullets round it, as a beam of Marisa does: as dark as <see cref="MOKOU_DIM"/> up to its edge, fading out to this many radii from its middle.
        /// </summary>
        public const float MOKOU_DIM_REACH = 3.6f;

        /// <summary>How dark the bullets are at the ball's edge and inside the dark place (0..1; <see cref="BEAM_BULLET_DIMMING"/> is a beam's).</summary>
        public const float MOKOU_DIM = 0.7f;
        public const double MOKOU_SHAKE_MS = 520;
        public const double MOKOU_BIG_FADE_MS = 120;
        public const double MOKOU_THROW_BEATS = 16;

        /// <summary>A ball that Mokou throws (in a break and in a spinner) is this share of the big ball's size while it flies; when it hits the wall it grows to the big ball's size and behaves like it.</summary>
        public const float MOKOU_THROW_SIZE_FACTOR = 0.5f;
        public const float MOKOU_THROW_SPEED_FACTOR = 2f;
        public const float MOKOU_THROW_MIN_SPEED = 0.1f;

        /// <summary>
        /// Mokou's landing: this many rings of circle bullets of this many bullets each come out of her in a spiral (one every <see cref="MOKOU_RING_GAP_BEATS"/> beats, every one turned by
        /// <see cref="MOKOU_RING_TWIST_DEGREES"/> more than the one before) at this many times the bullet speed.
        /// </summary>
        public const int MOKOU_RINGS = 20;

        public const int MOKOU_RING_BULLETS = 8;
        public const double MOKOU_RING_GAP_BEATS = 0.25;
        public const float MOKOU_RING_TWIST_DEGREES = 9f;
        public const float MOKOU_RING_SPEED_FACTOR = 1f;

        /// <summary>How long (ms) the flash of a jump's start / landing lasts, and how far (circle radii) it spreads.</summary>
        public const double MOKOU_FLASH_MS = 320;

        public const float MOKOU_FLASH_RADIUS = 4f;

        /// <summary>
        /// Reimu's yin-yang orb: its radius (hit circle radii) and how much of it hurts (a share of that), how long it flies (it ricochets off the border and the other orbs), how often she throws one
        /// (beats), its speed (times an ordinary bullet's, at least <see cref="REIMU_ORB_MIN_SPEED"/> px per ms), how fast it spins (degrees per ms) and how long it takes to grow out of her (ms).
        /// </summary>
        public const float REIMU_ORB_RADIUS = 1f;
        public const int REIMU_ORB_RING = 3; // (this many in a throw, in a ring, the first at the player)

        public const float REIMU_ORB_HIT = 0.95f;

        /// <summary>Reimu's pile: a bullet never lands closer than this (px, beyond touching) to the player.</summary>
        public const float REIMU_PILE_PLAYER_GAP = 30f;

        /// <summary>The longest an orb flies (beats; shorter if the pause it was thrown in ends first).</summary>
        public const double REIMU_ORB_LIFE_BEATS = 16;

        public const double REIMU_ORB_EVERY_BEATS = 4;
        public const float REIMU_ORB_SPEED_FACTOR = 1.27f;
        public const float REIMU_ORB_MIN_SPEED = 0.12f;
        public const float REIMU_ORB_SPIN = 0.18f;
        public const double REIMU_ORB_POP_MS = 200;

        /// <summary>The most bullets that can lie in Reimu's pile at the border at once (more pass through).</summary>
        public const int REIMU_PILE_MAX = 8000;

        /// <summary>Reimu: every this many-th bullet stops at the border and piles up in her attacks (by its ring's time and its number); the others leave the screen as ever.</summary>
        public const int REIMU_PILE_EVERY = 2;

        /// <summary>How long (ms) the bullets of the pile fade out before they are gone, and fade in when they land.</summary>
        public const double REIMU_PILE_FADE_MS = 400;

        public const double REIMU_PILE_FADE_IN_MS = 250;

        /// <summary>Yuyuko's tree: its height as a part of the screen's, how long it takes to grow (beats; it takes as long to grow backwards when she goes), and how big a gap (ms) between two of her sections still keeps the tree.</summary>
        public const float YUYUKO_TREE_HEIGHT = 0.9f;

        public const double YUYUKO_TREE_GROW_BEATS = 16;
        public const double YUYUKO_TREE_KEEP_GAP_MS = 1500;

        /// <summary>The wood is grown by this part of the growth time (the rest is for the first petals to come); the screen shakes this much while the tree grows (0..1, see BEAM_SHAKE_MIN / MAX: 0.5 is 1.5 px).</summary>
        public const float YUYUKO_WOOD_DONE = 0.88f;

        public const float YUYUKO_SHAKE = 0.5f;

        /// <summary>The sakura flowers of Yuyuko's tree: the tree grows its own (below) in every attack and one more for every bullet that leaves the screen at the bottom (no limit); each is a star of five petals and when the attack ends every petal flies away as a petal bullet. How many flowers there have to be (a quarter of them in bloom) for the bullets round the tree to be dimmed fully:</summary>
        public const int YUYUKO_DIM_FLOWERS = 384;

        /// <summary>The flowers the tree grows by itself in every attack, whatever the bullets do (so that the tree is never bare: the others are brought by bullets).</summary>
        public const int YUYUKO_OWN_FLOWERS = 32;

        public const int YUYUKO_PETALS_PER_FLOWER = 5;

        /// <summary>Of the five petals of a flower on the tree only this many are shot out (as petal bullets) when the attack ends; the others fall straight down.</summary>
        public const int YUYUKO_SHOT_PETALS = 3;
        public const float YUYUKO_PETAL_SIZE = 0.025f;
        public const float YUYUKO_PETAL_REACH = 0.55f;
        public const float YUYUKO_PETAL_SCATTER = 0.07f;
        public const double YUYUKO_PETAL_GROW_BEATS = 2;
        public const double YUYUKO_PETAL_SPREAD_BEATS = 1.5;

        /// <summary>The bloom of the flowers: a soft glow this many times as long as a petal, this bright (0..1); and the bullets round the flowers are darkened out to this many times this part of the tree's height (strength 0..1).</summary>
        public const float YUYUKO_BLOOM_SIZE = 5.5f;

        /// <summary>
        /// What leaves the top of a beam in Kaguya's sections falls as separate pieces, not as one beam (which would be a line from the top down across the screen): a piece and the gap after it are this long (px of the beam as it was fired),
        /// and this share of it is the piece.
        /// </summary>
        public const float KAGUYA_BEAM_PIECE_PX = 60f;

        public const double KAGUYA_BEAM_PIECE_SHARE = 0.6;

        /// <summary>A piece is at most this long in fire time (ms), and two fallen points further apart than this (px, besides the beam's width) are never joined.</summary>
        public const double KAGUYA_BEAM_PIECE_MAX_MS = 200;

        public const float KAGUYA_BEAM_PIECE_REACH = 90f;

        /// <summary>The bullets that leave the top of the screen turn into flowers this size compared to the flowers of the tree.</summary>
        public const float YUYUKO_FALL_FLOWER_SCALE = 0.5f;

        /// <summary>The falling flowers lean this many degrees off straight down: half of them to the left, half to the right.</summary>
        public const float YUYUKO_FALL_TILT_DEGREES = 5f;

        /// <summary>Which part of the bullets that leave the top turn into falling flowers (the rest just fly away), and which part of the bullets that leave the bottom grow a flower on the tree.</summary>
        public static float YUYUKO_FALL_SHARE => Bosses.BossTuning.YuyukoFall.Value;

        public static float YUYUKO_SOW_SHARE => Bosses.BossTuning.YuyukoSow.Value;

        /// <summary>The sunbeam that falls onto the tree: how wide it is at the bottom of the screen (a multiple of the tree's width) and how strong.</summary>
        public const float YUYUKO_BEAM_WIDTH = 2.2f;

        public const float YUYUKO_BEAM_ALPHA = 0.5f;

        /// <summary>How long (ms) the sunbeam takes to fade out when an attack ends and to come back when the next starts.</summary>
        public const double YUYUKO_BEAM_FADE_MS = 400;

        public const float YUYUKO_BLOOM_ALPHA = 0.28f;
        public const float YUYUKO_DIM_RADIUS = 0.2f;
        public const float YUYUKO_DIM = 0.5f;

        /// <summary>The petals fall this multiple of an ordinary bullet's speed (at least <see cref="YUYUKO_FALL_MIN_SPEED"/> px/ms) and swing this many star radii to either side, once per this many ms.</summary>
        public const float YUYUKO_FALL_SPEED = 0.9f;

        public const float YUYUKO_FALL_MIN_SPEED = 0.03f;

        /// <summary>Yuyuko: besides her ordinary rings she shoots rings of petals arranged as a sakura flower: this many bullets, this many times rarer than the ordinary rings of the other bosses (these are all she shoots of her own).</summary>
        public const int YUYUKO_FLOWER_RING_COUNT = 30;

        public const double YUYUKO_FLOWER_RING_RARITY = 4;

        /// <summary>
        /// Lily White: her branches are as big as a sub-branch of Yuyuko's tree (a share of the tree's height: the length of a branch two levels below the trunk and how thick it is at its foot); a flower on them
        /// grows when a bullet leaves the screen at a side (this share as often as Yuyuko's, see <see cref="YUYUKO_SOW_SHARE"/>, and as fast as hers, see <see cref="YUYUKO_PETAL_GROW_BEATS"/>) and bursts this many beats later.
        /// </summary>
        public const double LILY_FLOWER_BEATS = 8;

        public const float LILY_SOW_FACTOR = 0.5f;

        public const float LILY_BRANCH_LENGTH = 0.24f;
        public const float LILY_BRANCH_WIDTH = 0.028f;

        /// <summary>How many places on each branch a flower can sit (of this many tried places, the one farthest from the flowers that are there is taken), and the most flowers one section grows.</summary>
        public const int LILY_FLOWER_CANDIDATES = 8;

        public const int LILY_MAX_FLOWERS = 20000;

        /// <summary>Her own bullets (and the spinner's): rings of flowers, this many in a ring, this many times as big as the flowers that fall from Yuyuko's top, every this many beats; they flash (this bright, 0..1) and burst into this many petals after <see cref="LILY_FLOWER_BEATS"/> beats, flying at this multiple of the flower's speed.</summary>
        public const int LILY_RING_BULLETS = 12;

        /// <summary>In a spinner every shot of the spiral would be a ring of her flowers: a ring has this many flowers (half as many), and only every this many-th shot is one.</summary>
        public const int LILY_SPINNER_RING_BULLETS = 6;

        public const int LILY_SPINNER_SHOT_EVERY = 2;

        public const float LILY_FLOWER_SIZE = 2f;
        public const double LILY_RING_EVERY_BEATS = 2;
        public const float LILY_FLASH_BLOOM = 0.9f;
        public const int LILY_BURST_PETALS = 5;
        public const float LILY_PETAL_SPEED = 0.8f;

        /// <summary>
        /// The real screen's height in osu!pixels (osu! fits the 384 px playfield into 80 % of it), for sizes worked out before the field knows the screen: the petals her flower bullets burst into are as
        /// big as the petals of Yuyuko's tree (on average; the flowers on her branches burst into petals exactly as big as theirs), hitbox too.
        /// </summary>
        public const float SCREEN_HEIGHT = 384f / 0.8f;

        public const float LILY_PETAL_RADIUS = YUYUKO_PETAL_SIZE * 0.5f * YUYUKO_TREE_HEIGHT * SCREEN_HEIGHT;

        /// <summary>
        /// Urumi Ushizaki: every this many beats a push is announced; four beats later (<see cref="URUMI_PREVIEW_BEATS"/>) everything on the screen gets an extra straight speed (this multiple of the section's bullet speed) away from her, along the line
        /// to the player. Until then it glows this much, is this grey (0..1) and shakes, and every this many-th bullet is this many times as big. Her own bullets are ellipses in rings of <see cref="URUMI_RING_BULLETS"/>, one every this many beats.
        /// </summary>
        public const double URUMI_CYCLE_BEATS = 8;

        public const double URUMI_PREVIEW_BEATS = 4;
        public static float URUMI_PUSH_SPEED => Bosses.BossTuning.UrumiPush.Value;
        public const float URUMI_BLOOM = 0.4f;
        public const float URUMI_SHAKE = 0.5f; // (of what is frozen shakes: FROZEN_SHAKE_MIN_PX, _MAX_PX)
        public const float URUMI_GREY = 0.75f;
        public const int URUMI_SWELL_EVERY = 8;
        public const float URUMI_SIZE = 1.5f;
        public const double URUMI_SWELL_MS = 300;
        public const int URUMI_RING_BULLETS = 20;
        public const double URUMI_RING_EVERY_BEATS = 2;

        /// <summary>
        /// In her section every circle that is hit and every slider that is over falls on the player, in this combo colour: for this long (ms) it does not hurt yet (standing in a circle as it is hit does not
        /// cost twice), it is gone this long (ms) after it began to fall (by then it has left the screen), and its glow is this strong and this many times its size.
        /// </summary>
        public static readonly osuTK.Graphics.Color4 URUMI_FALL_COLOUR = new osuTK.Graphics.Color4(0.55f, 0.55f, 0.58f, 1f);

        public const double URUMI_FALL_GRACE_MS = 300;
        public const double URUMI_FALL_MS = 7000;
        public const float URUMI_FALL_GLOW = 0.5f;
        public const float URUMI_FALL_GLOW_SIZE = 2.8f;

        /// <summary>
        /// Ichirin Kumoi and Unzan: in an attack, every this many beats one hand (the left and the right in turn) is thrown at the player along an outer arc (the arc bulges this part of the way's length, it speeds up: the way goes with this power of the time)
        /// and takes this many beats; it stays at the wall behind the player this many beats and returns in a straight line in this many beats. Hands are this many circle radii tall and hurt within this many radii (the head is this wide);
        /// a returned hand rests this many radii to the side of her and this many below her. In a pause both hands clap this many radii in front of her (one beat after they have come together they have gone back) and the clap sends out this many rings of this many ellipses
        /// arranged as the tip of an arrowhead, one ring every this many beats, at this multiple of the bullet speed. Every hit and clap shakes the field (0..1) for this many beats.
        /// </summary>
        public const double ICHIRIN_THROW_EVERY_BEATS = 8;

        public const double ICHIRIN_THROW_BEATS = 8;
        public const double ICHIRIN_HOLD_BEATS = 2;
        public const double ICHIRIN_RETURN_BEATS = 3;

        /// <summary>A hand rests at least this many beats at home before it is drawn back again; the head's middle is this part of its width behind her (away from the player).</summary>
        public const double ICHIRIN_REST_BEATS = 1;

        public const float ICHIRIN_HEAD_BEHIND = 0.3f;

        /// <summary>Before a throw the hand is drawn slowly back along the line it will fly on, this many beats and this many circle radii, so the player is warned (a hand's whole round is then 16 beats: the next throw of the same hand).</summary>
        public const double ICHIRIN_WINDUP_BEATS = 1.5;

        public const float ICHIRIN_PULLBACK = 4f;

        /// <summary>The rings of the impact (Mokou's landing has 20 of 8 bullets): this many.</summary>
        public const int ICHIRIN_IMPACT_RINGS = 10;
        public const float ICHIRIN_ARC = 1.2f;
        public const float ICHIRIN_ACCEL_POWER = 1.6f;
        public const float ICHIRIN_HAND_HEIGHT = 8.4f;
        public const float ICHIRIN_HAND_HIT = 2.6f;

        /// <summary>A hand that is out (thrown, holding or coming back) gathers the bullets it touches, at most this many at once, and shoots them out at its next impact or clap (like Hecatia's worlds, see <see cref="HECATIA_GATHER_MAX"/>).</summary>
        public const int ICHIRIN_GATHER_MAX = 64;
        public const float ICHIRIN_HEAD_WIDTH = 9.2f;
        public const float ICHIRIN_HOME_X = 4.2f;

        /// <summary>Where the hands stay before the first attack, close to the head (circle radii to the side and below her): they come out to their places in the warning.</summary>
        public const float ICHIRIN_NEAR_X = 2.2f;

        public const float ICHIRIN_NEAR_Y = 1f;
        public const float ICHIRIN_HOME_Y = 3f;
        public const float ICHIRIN_CLAP_DISTANCE = 6f;
        public const int ICHIRIN_CLAP_RINGS = 4;
        public const int ICHIRIN_CLAP_BULLETS = 28;

        /// <summary>The outline of the clap's rings is an arrowhead bullet: how far the back is from the middle compared to the tip, and how sharp the tip is (the higher, the narrower the point and the rounder the sides).</summary>
        public const float ICHIRIN_ARROW_BACK = 0.55f;

        public const float ICHIRIN_ARROW_SHARPNESS = 2f;
        public const double ICHIRIN_CLAP_RING_GAP_BEATS = 0.5;
        public const float ICHIRIN_CLAP_SPEED = 0.9f;

        /// <summary>The hands take this many beats to come together on the place where the player was when they set off, and as many to go home again.</summary>
        public const double ICHIRIN_CLAP_BEATS = 4;

        /// <summary>The hands stay together this many beats after they have met.</summary>
        public const double ICHIRIN_CLAP_HOLD_BEATS = 2;

        /// <summary>The hands go to the clap along an outer arc (bulging this part of the way's length) and speed up with this power of the time (the throw's is 1.6).</summary>
        public const float ICHIRIN_CLAP_ARC = 1f;

        public const float ICHIRIN_CLAP_ACCEL_POWER = 1.5f;
        public const float ICHIRIN_SHAKE = 0.5f;
        public const double ICHIRIN_SHAKE_BEATS = 4;
        /// <summary>A hand that is home swings a little: this many circle radii, once per this many beats, and turns this many degrees.</summary>
        public const float ICHIRIN_SWING = 0.1f;

        public const double ICHIRIN_SWING_BEATS = 3;
        public const float ICHIRIN_SWING_DEGREES = 2f;

        /// <summary>Luna Child: a ring of this many arc beams (with as many gaps; an arc is this part of its share of the circle) every this many beats, flying at this multiple of the section's bullet speed. In her attacks everything is smoothed: soft copies of every bullet this many times as big and this visible, and this much glow (0..1); it comes and goes over this long (ms).</summary>
        public const int LUNA_ARCS = 10;

        public const float LUNA_ARC_SHARE = 0.15f; // (of its share of the circle: with half as many arcs as there were each is as long as it was, and the gaps twice as wide)
        public const double LUNA_RING_EVERY_BEATS = 2;
        public const float LUNA_RING_SPEED = 1f;

        /// <summary>
        /// Luna Child: the outline of every slider that starts in her attacks grows out from its path as a beam, at this multiple of the section's bullet speed; the path it grows from has a point every this many px (fewer
        /// than the slider's own), and the beam a point every this many px round its bends.
        /// </summary>
        public const float LUNA_OUTLINE_SPEED = 1f;

        public const float LUNA_OUTLINE_STEP = 20f;
        public const float LUNA_OUTLINE_POINT_GAP = 12f;

        /// <summary>Luna Child: an outline goes round the ends and bends of its slider a point every this many radians, whatever its distance (so the pieces on it do not jump as it grows); an arc of hers is drawn through this many points.</summary>
        public const float LUNA_OUTLINE_TURN = 0.06f;

        public const int LUNA_ARC_POINTS = 24;

        /// <summary>Luna Child: an outline is cut into this many pieces all round (by its length, so they grow with it), each this share of it beam and the rest a gap, as her rings of arcs are.</summary>
        public const int LUNA_OUTLINE_PIECES = 20;

        public const float LUNA_OUTLINE_SHARE = 0.3f;
        /// <summary>Luna Child: how fast the softness comes with the distance (it is full from 1 / this of the way out).</summary>
        public const float LUNA_SOFT_RAMP = 2f;
        public const float LUNA_BLOOM = 0.7f;
        public const double LUNA_FADE_MS = 300;

        /// <summary>Shinmyoumaru Sukuna: in her attacks a bullet or beam is this many times its size at the edge of her magic circle, growing gradually to this many times at the edge of the screen; the glow (0..1) there.</summary>
        public const float SUKUNA_MIN_SIZE = 0.5f;

        public const float SUKUNA_MAX_SIZE = 1.5f;
        public const float SUKUNA_BLOOM = 0.9f;

        /// <summary>Utsuho: a ring of this many bullets every this many beats, this many times as big as the basic bullet, at this multiple of the usual speed.</summary>
        public const int UTSUHO_RING_COUNT = 6;

        public static double UTSUHO_RING_EVERY_BEATS => Bosses.BossTuning.UtsuhoRingBeats.Current;
        public static float UTSUHO_RING_SCALE => Bosses.BossTuning.UtsuhoRingScale.Value; // (adjustable in the boss menu; other bosses' mechanics do not change it, see BulletEmitter.Sharp)
        public const float UTSUHO_RING_SPEED = 0.7f;

        /// <summary>The black hole: radius of its dark core (multiple of a hit circle's radius), how long it takes to appear, how much wider its picture is than the core, and how early in the pause it starts pulling (ms).</summary>
        public static float UTSUHO_HOLE_RADIUS => Bosses.BossTuning.UtsuhoHole.Value;

        /// <summary>While the black hole is there, what the circles and sliders fire comes in from just beyond the edge of a screen this many px wide and high either side of the middle of the playfield (a 16:9 screen round it).</summary>
        public const float UTSUHO_INWARD_HALF_WIDTH = 426f;

        public const float UTSUHO_INWARD_HALF_HEIGHT = 240f;

        public const double UTSUHO_HOLE_FADE_MS = 500;
        public const float UTSUHO_HOLE_PICTURE_FACTOR = 5f;
        public const double UTSUHO_PULL_DELAY_MS = 400;

        /// <summary>What comes out of the black hole: the narrow cone (radians to either side), the speed (multiple of the speed it went in at), the most bullets per pause, the size of the galactic ellipses (multiple of the hole's radius), their flatness and how long they take to come out (ms).</summary>
        public const float UTSUHO_JET_SPREAD = 0.18f;

        /// <summary>Where a ray shoots a bullet out, a flash of light (this long, ms; this many times the bullet's size).</summary>
        public const double UTSUHO_JET_FLASH_MS = 180;

        public const float UTSUHO_JET_FLASH_SIZE = 5f;

        /// <summary>The rays' bullets start this far out (hole radii: just outside the dark, where they flash) and are drawn this many times as long along their way (and thinner: spaghettified).</summary>
        public const float UTSUHO_JET_OUT = 1.15f;

        public const float UTSUHO_JET_STRETCH = 2f;

        public static float UTSUHO_JET_SPEED => Bosses.BossTuning.UtsuhoJetSpeed.Value;
        public const int UTSUHO_MAX_OUTPUT = 1200; // (two out for every one taken in: 600 taken in at most)
        public const float UTSUHO_GALAXY_MIN = 1.5f;
        public const float UTSUHO_GALAXY_MAX = 5f;
        public const float UTSUHO_GALAXY_FLATNESS = 0.35f;
        public const double UTSUHO_EMERGE_MS = 300;

        /// <summary>How far the jets may turn in all (radians) and the fraction of a spinner beam's turning speed they use.</summary>
        public const float UTSUHO_JET_MAX_TURN = MathF.PI / 2f;

        public const float UTSUHO_JET_TURN_FACTOR = 0.125f;

        /// <summary>
        /// Before every black hole everything slows down evenly to a stop over this many beats (the end of the attack before), stands still until the pull begins, and gets its speed back over this long (ms);
        /// the pull is this much of a bullet's own speed. The loop of the black hole plays from when the slowing down begins until the hole is gone.
        /// </summary>
        public const double UTSUHO_STOP_BEATS = 2;

        public const double UTSUHO_RESUME_MS = 300;
        public const float UTSUHO_PULL_SPEED = 0.5f;

        /// <summary>On its way into the black hole a pulled bullet speeds up evenly from the pull's speed to this many times it, where it arrives (the closer, the faster).</summary>
        public const float UTSUHO_PULL_SPEED_UP = 2f;

        /// <summary>How much wider the lens (the bending of the light) is than the dark of the hole.</summary>
        public const float UTSUHO_LENS_FACTOR = 1.1f;

        /// <summary>A bullet that touches the black hole shrinks to nothing over this long (ms), flying on, before it is gone; her big balls grow from nothing to their size over this long (ms) when they are shot.</summary>
        public const double UTSUHO_SWALLOW_MS = 160;

        public const double UTSUHO_BALL_GROW_MS = 220;

        /// <summary>Every ring of Utsuho's big balls is turned this much further than the one before (half the gap of a ring of six: they fly between each other).</summary>
        public const float UTSUHO_RING_TURN_DEGREES = 30f;

        /// <summary>
        /// The black hole's light bending: a bullet near it is drawn stretched along the circle round it (at most this many times) and has a faint second image on the other side, squeezed against the edge (this
        /// visible, up to this many lens radii away); round the hole this many faint stars of the background are drawn bent into arcs, out to this many lens radii, swirling this fast (radians per ms at the edge).
        /// </summary>
        public const float UTSUHO_LENS_MAX_STRETCH = 5f;

        /// <summary>A bullet the lens stretches less than this is drawn as it is (only pushed out): a stretched one is bent, which costs several pictures.</summary>
        public const float UTSUHO_LENS_MIN_STRETCH = 1.04f;

        public const float UTSUHO_LENS_SECOND_ALPHA = 0.55f;
        public const float UTSUHO_LENS_SECOND_REACH = 3f;
        public const int UTSUHO_LENS_STARS = 120;
        public const float UTSUHO_LENS_STAR_REACH = 5f;
        public const float UTSUHO_LENS_SWIRL = 0.0004f;

        /// <summary>Utsuho's attacks and pauses last this many times as long as other bosses'; Gengetsu's attacks too.</summary>
        public const double UTSUHO_PHASE_FACTOR = 2.0;

        public const double GENGETSU_PHASE_FACTOR = 2.0;

        /// <summary>Gengetsu's attacks last this many times as long again (she has no pauses).</summary>
        public const double GENGETSU_ATTACK_FACTOR = 4.0;

        /// <summary>
        /// Mima: a moon every this many beats of her section; it grows for this many, lights up over this many and is solid until this many beats after it stopped growing. Its outer radius is this much of the playfield's height,
        /// its hollow a circle this much of that, set off this much of that towards the middle of the playfield. It is drawn in this many pieces, this visible while it grows, with a grey outline this many px wide; the screen shakes this much while it grows. When it goes, this many stars fly off its edges, with tails this many beats long. Where something bounces off it a flash shows for this many ms, this many circle radii across. A soft light round it is this visible and reaches this much of its half thickness out from its edges; over its last this many beats it reaches out to this much and back (a flash), it fades out over this many after, and its stars are this much of a finish's size, half of them on each edge; her own stars have tails this many beats long.
        /// </summary>
        public const double MIMA_EVERY_BEATS = 16;

        public const double MIMA_GROW_BEATS = 4;
        public const double MIMA_LIGHT_BEATS = 1;
        public const double MIMA_SOLID_BEATS = 8;
        public const float MIMA_RADIUS = 0.6f;
        public const float MIMA_HOLLOW = 0.825f;
        public const float MIMA_HOLLOW_OFFSET = 0.325f;
        public const int MIMA_POINTS = 72;
        public const float MIMA_GHOST_ALPHA = 0.35f;
        public const float MIMA_OUTLINE = 1.5f;
        public const float MIMA_SHAKE = 0.5f;
        public const int MIMA_EDGE_STARS = 96;
        public const double MIMA_TAIL_BEATS = 2;
        public const double MIMA_FLASH_MS = 200;
        public const float MIMA_FLASH_SIZE = 1.5f;

        /// <summary>Every ricochet off a wall flashes like a bounce off Mima's moon: over this long, this many bullet radii across at its biggest; at most this many flashes at once.</summary>
        public const double RICOCHET_FLASH_MS = MIMA_FLASH_MS;

        public const float RICOCHET_FLASH_SIZE = 4f;
        public const int RICOCHET_FLASH_MOST = 64;
        public const double MIMA_FLASH_BEATS = 1;
        public const float MIMA_GLOW_ALPHA = 0.3f;
        public const float MIMA_GLOW_REACH = 0.375f;
        public const float MIMA_FLASH_REACH = 0.75f;
        public const double MIMA_FADE_BEATS = 1;
        public const float MIMA_STAR_SIZE = 1f;
        public const double MIMA_OWN_TAIL_BEATS = 4;

        /// <summary>The shake of Utsuho (0..1, see BEAM_SHAKE_MIN / MAX): every ring of big balls jolts the screen this much, dying away over this long (ms).</summary>
        public const float UTSUHO_SHAKE = 0.5f;

        public const double UTSUHO_SHOT_SHAKE_MS = 300;

        // ---- Minamitsu Murasa (BossMechanic.Anchors, UI/Stages/AnchorAct.cs) ----

        /// <summary>Murasa: every this many beats a sequence of anchors starts: this many of them, thrown one after another over this many beats, round her (evenly; every next sequence turned by half a step).</summary>
        public const double MURASA_SEQUENCE_BEATS = 16;

        public const int MURASA_ANCHORS = 8;
        public const double MURASA_THROW_SPREAD_BEATS = 4;

        /// <summary>Murasa: on top of them, every this many beats an anchor thrown at the player (her own: half as often as it was).</summary>
        public const double MURASA_AIMED_EVERY_BEATS = 8;

        /// <summary>Murasa: an anchor flies to the wall in this many beats, stays there (chained) this many beats, and then sinks: fades out and shrinks to this share of its size over this many beats.</summary>
        public const double MURASA_FLIGHT_BEATS = 4; // (the one at the player and the ones round her alike)

        public const double MURASA_HOLD_BEATS = 8;
        public const double MURASA_SINK_BEATS = 4;
        public const float MURASA_SINK_SIZE = 0.6f;

        /// <summary>Murasa: while an unchained anchor sinks it crawls on out of the playfield, the way it flew, at an even pace: this many of its lengths over the sink (one: its ring ends past the wall).</summary>
        public const float MURASA_SINK_CRAWL = 1f;

        /// <summary>Murasa: how long an anchor is (circle radii, from its ring to its crown: the picture's length), and how far past the wall its crown sticks out (a share of its length).</summary>
        public const float MURASA_ANCHOR_CIRCLES = 4.5f;

        public const float MURASA_ANCHOR_OUT = 0.25f;

        /// <summary>Murasa: where along the anchor's picture (0 = its ring end, 1 = its crown) the middle of its ring is: the chain is fastened there.</summary>
        public const float MURASA_RING_AT = 0.115f;

        /// <summary>Murasa: the chain is Wakasagihime's bubbles, this many times as big as a plain bullet, this many plain bullets' radii apart (middle to middle); unchained, each pops (a ring that swells by half and fades, over this long, ms).</summary>
        public const float MURASA_BUBBLE_SIZE = WAKASAGI_BUBBLE_SIZE;

        public const float MURASA_LINK_GAP_RADII = 5f;
        public const double MURASA_POP_MS = 150;

        /// <summary>Murasa: a bubble pops into a ring of this many bullets, this big (of a plain bullet), as fast as this (of a plain bullet).</summary>
        public const int MURASA_RING_BULLETS = 6;

        public const float MURASA_RING_SIZE = 0.4f;
        public const float MURASA_RING_SPEED = 0.6f;

        /// <summary>Murasa: an unchained chain pops from her out to the anchor over this many beats, evenly (silently).</summary>
        public const double MURASA_POP_SPREAD_BEATS = 2;

        /// <summary>Murasa: at most this many clangs sound at once (what would be one more is not heard); each is played this many times at once (stacked: twice as loud).</summary>
        public const int MURASA_SOUNDS_AT_ONCE = 3;

        public const int MURASA_SOUND_COPIES = 2;

        /// <summary>Murasa: an anchor that hits the wall shakes the screen (0..1, see BEAM_SHAKE_MIN / MAX: 2 px dying down to 1 px) for this long (ms).</summary>
        public const double MURASA_SHAKE_MS = 150;

        /// <summary>Murasa: every anchor that hits the wall clangs (Resources/Samples/murasa_anchor.wav, the user's "Big Hammer Hit") this loud; one that hit more than this long (ms) ago, after a jump in time, is not heard.</summary>
        public const double MURASA_SOUND_VOLUME = 0.7;

        public const double MURASA_SOUND_LATE_MS = 100;
    }
}
