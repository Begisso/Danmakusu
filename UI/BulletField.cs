using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Screens.Play;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Simulates, draws and collides all bullets, and owns the player character.
    ///
    /// Hits on the player: a bullet touching the character (shape-accurate hitboxes), an active slider body, and a hit circle that
    /// is auto-hit while the character is inside it (one hit). When the player dies every bullet falls with gravity.
    ///
    /// Character rules:
    ///  - normally the character sits on the cursor and collision is swept along its path (no tunnelling through bullets);
    ///  - while key 1 or key 2 (osu!'s two action buttons: Z / X, the mouse buttons, and the number keys 1 / 2) is held, the character
    ///    stays put and only the cursor moves;
    ///  - pressing the second key while the first is still held moves the freeze point to the cursor (a teleport);
    ///  - the freeze ends when both keys are released: the character teleports to the cursor. Teleport paths are NOT collision checked
    ///    (only the destination is, from the next sweep on).
    /// </summary>
    public partial class BulletField : CompositeDrawable, IKeyBindingHandler<OsuAction>
    {
        /// <summary>Gaps larger than this (seeks, lag spikes) are not swept, otherwise a seek would deal phantom damage.</summary>
        private const double max_sweep_gap_ms = 500;

        private readonly BulletEmitter[] emitters;
        private readonly BulletEmitter[] trainRings; // Yukari's trains (the screen shakes while one is on it)
        private readonly LaserEmitter[] lasers;
        private double maxLaserSpan;
        private Vector2[] laserBuffer;
        private float[] laserWidthBuffer; // half width of the beam at every point of laserBuffer
        private float[] laserWidthPointBuffer = new float[4096];
        private float[] laserDistanceBuffer; // how far the slice of the beam at every point of laserBuffer has flown
        private BulletLook?[] laserLookBuffer; // how the slice at every point of laserBuffer looks (null = its own colour): red after Seija's bounce, blue while frozen...
        private float[] laserDistancePointBuffer = new float[4096];
        private float[] laserFadePointBuffer = new float[4096];
        private float[] laserFadeBuffer = new float[1024]; // how much of its colour the beam shows at every point of laserBuffer (1 = all)
        private Vector2[] laserPointBuffer = new Vector2[4096];
        private LaserInstance[] laserInstances = new LaserInstance[32];
        private int nextLaserAim;
        private bool[] sliceGone = new bool[4096]; // for the beam being looked at: which of its points have flown into the moon
        private readonly int[] rotatingLasers;
        private readonly int[] spinnerLasers;
        private readonly BossPeriod[] bosses;

        /// <summary>Clownpiece's sections: the moon, the laser source opposite it and the triple beam (see <see cref="MoonWindow"/>).</summary>
        private readonly MoonWindow[] moons;
        private MoonLayer moonLayer = null!;
        private MoonLayer moonTopLayer = null!;
        private readonly List<MoonWarning> moonWarnings = new List<MoonWarning>();

        /// <summary>What the bosses' mechanics do to the whole field (slowed bullets, the flashlight).</summary>
        private readonly BossEffects effects;
        private readonly float circleRadius;

        /// <summary>From this radius on a round bullet is drawn sharp, from a big picture (see <see cref="DanmakusuBalance.SHARP_BULLET_SIZE"/>).</summary>
        private float sharpBulletRadius => DanmakusuBalance.BulletRadiusForCircle(circleRadius) * DanmakusuBalance.SHARP_BULLET_SIZE;

        /// <summary>No bullet is drawn smaller than this.</summary>
        private float leastBulletRadius => DanmakusuBalance.BulletRadiusForCircle(circleRadius) * DanmakusuBalance.BULLET_SMALLEST;
        private BossCharacter bossCharacter = null!;
        private BossCharacter bossCharacter2 = null!; // (the second boss of the section of two bosses)
        private BossCharacter bossCharacter3 = null!; // (the third boss of the section of three)
        private BossCharacter lingeringBoss = null!, lingeringBoss2 = null!, lingeringBoss3 = null!; // (a boss of rank 0 / 1 / 2 who has left while a mechanic of hers goes on, where she stood)

        /// <summary>Seija's clone, which warns of a turn of the screen: how visible it is this frame (0: not there; her act sets it), where, whose picture, and when the turn comes (its stars fly in until then).</summary>
        private BossCharacter seijaClone = null!;

        private float seijaCloneLevel;
        private Vector2 seijaCloneAt;
        private int seijaCloneSlot;
        private double seijaCloneTurn;
        private Vector2 lingerAt, lingerAt2, lingerAt3;
        private BossHud bossHud = null!;

        /// <summary>The map's bosses by slot (A, B); empty if the map has no boss section.</summary>
        private readonly BossInfo[] slotBosses;
        private Vector2 bossPosition;
        private Vector2 bossTarget;
        private Vector2 bossVisualPosition; // where Marisa, her star and her magic circle are drawn: follows the target every frame (no 125 Hz limit)
        private double bossSampleTime = double.MinValue;
        private double bossSmoothTime = double.MinValue;

        // the second boss of the section of two (BossPeriod.Rank 1): the same, for her
        private Vector2 bossPosition2;
        private Vector2 bossTarget2;
        private Vector2 bossVisualPosition2;
        private double bossSampleTime2 = double.MinValue;
        private double bossSmoothTime2 = double.MinValue;

        // the third boss of the section of three (BossPeriod.Rank 2): the same, for her
        private Vector2 bossPosition3;
        private Vector2 bossTarget3;
        private Vector2 bossVisualPosition3;
        private double bossSampleTime3 = double.MinValue;
        private double bossSmoothTime3 = double.MinValue;

        // when the two bosses stand on each other they circle round their place: how far the first one is from it now (smoothed like the boss himself, the second is on the other side)
        private Vector2 orbitShown;
        private double orbitTime = double.MinValue;
        private readonly SliderZone[] sliderZones;
        private readonly CircleZone[] circleZones;
        private readonly int[] aimEmitters;
        private readonly double maxZoneDuration;
        private readonly double damagePerBullet;
        private readonly double damagePerSliderTick;
        private readonly Func<Vector2?> cursorScreenPosition;
        private readonly RicochetArea? ricochetArea;

        /// <summary>The screen that bullets and beams bounce off during Seija's reversal (see <see cref="TrajectoryOp"/>); kept up to date like <see cref="ricochetArea"/>.</summary>
        private readonly RicochetArea bounceArea = new RicochetArea();

        private BulletRenderer renderer = null!;
        private Box remiliaBackground = null!;
        private MoonSprite remiliaMoon = null!;
        private Box cirnoBackground = null!;
        private Box sakuyaBackground = null!;
        private Box reisenBackground = null!;
        private Box deathBackground = null!; // (Zanmu's section: a dimmed grey)
        private Box miracleBackground = null!; // (Sanae's attacks: a dim green)
        private Box hasteBackground = null!; // (Gengetsu's attacks: a grey dim, see hasteLevel)
        private Box stampedeBackground = null!; // (Saki's attacks: a brown dim)
        private Box stormBackground = null!; // (Tojiko's sections: a dark grey dim)
        private Box tideBackground = null!; // (Wakasagihime's attacks: an ocean blue dim)
        private Box mikoBackground = null!; // (Miko's beams: the screen dims round them)
        private Box kaguyaBackground = null!; // (Kaguya's sections: a night of dark blue)
        private Box verdantBackground = null!; // (Daiyousei's attacks: a dark warm green dim)
        private ClockSprite sakuyaClock = null!;
        private readonly PlayerCharacter character = new PlayerCharacter();
        private readonly PlayerCircle playerCircle = new PlayerCircle(); // (the player's own magic circle, under the bullets)
        private BulletInstance[] instanceBuffer = new BulletInstance[1024];

        private readonly List<HitRecord> hits = new List<HitRecord>();

        private bool initialised;
        private int key1Held; // number of key 1 bindings held (Z and the left mouse button can both be down)
        private int key2Held;
        private bool frozen => !keyboardControls && (key1Held > 0 || key2Held > 0); // (no freeze when the player moves with the keyboard)
        private bool resetFreezePending;
        private bool teleportPending;
        private Vector2 characterPosition;
        private Vector2 previousCharacterPosition;
        private double previousTime;
        private double nextSliderDamageTime = double.NegativeInfinity;
        private double lastMissSoundTime = double.NegativeInfinity;
        private int nextAim;
        private int nextSwirl; // (Mugetsu: the rings of her vortex, which take where she is as they are fired)
        private int[] swirlRings = [];

        // The rings that are alive: spawned (their time has come) and not retired. Kept in draw order, slowest first, so faster bullets are drawn above slower ones.
        private int[] active = new int[256];
        private int activeCount;
        private int nextSpawn;

        // death: bullets freeze where they were and fall in real time (the gameplay clock slows down / stops while the fail animation plays).
        private bool failed;
        private double failTime;
        private long failTimestamp;

        private const int slider_hit = -1;
        private const int circle_hit = -2;

        [Resolved]
        private HealthProcessor? healthProcessor { get; set; }

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

        [Resolved]
        private ScoreProcessor? scoreProcessor { get; set; }

        /// <summary>The beatmap being played; its background picture gives the colour of spinner and boss beams and stars.</summary>
        [Resolved(CanBeNull = true)]
        private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

        /// <summary>The colour of the map's background (null if there is none or it could not be read: the combo colours are used then).</summary>
        private Color4? backgroundColour;

        /// <summary>The style (palette slot) of the background colour, behind the combo colours; -1 = none.</summary>
        private int backgroundStyle = -1;

        private SkinnableSound comboBreakSound = null!;

        /// <summary>Raised (with the position in playfield coordinates) whenever the player takes damage.</summary>
        public event Action<Vector2>? PlayerHit;

        /// <summary>What hurt the player last (shown under the miss marker, so a hit that looks like it came from nowhere can be traced).</summary>
        public string LastHitCause { get; private set; } = string.Empty;

        /// <summary>osu! itself plays the combo break sound from this combo upwards, so we only add our own below it.</summary>
        private const int built_in_combo_break_threshold = 20;

        /// <summary>Skin combo colours (already made visible, see <see cref="DanmakusuBalance.BulletColour"/>).</summary>
        private Color4[] palette = [new Color4(1f, 0.35f, 0.5f, 1f)];
        private Color4[] borderPalette = [DanmakusuBalance.BorderColour(new Color4(1f, 0.35f, 0.5f, 1f))];

        private InputManager? inputManager;

        /// <summary>The attacks of Seija (start, end): the screen is turned round during each.</summary>
        private readonly (double Start, double End)[] mirrorWindows;

        /// <summary>The bullets' and beams' ops (see <see cref="BossOps"/>); the ones that mirror or send to a point are kept at the centre of the screen.</summary>
        private readonly TrajectoryOp[] bulletOps;

        /// <summary>The attacks of Remilia (start, end): bullets and background turn red during each.</summary>
        private readonly (double Start, double End)[] remiliaWindows;
        private readonly (double Start, double End)[] stampedeWindows;

        /// <summary>Which rings are knives of Remilia (red handle).</summary>
        private readonly bool[] redHandles;

        /// <summary>The moments Cirno freezes everything (the start of each of her attacks).</summary>
        private readonly double[] cirnoFreezes;

        /// <summary>The moments Sakuya swaps everything (the end of each of her attacks): nothing hurts in the frame that holds one.</summary>
        private readonly double[] sakuyaSwaps;

        /// <summary>Sakuya's time stops (start, end): dark brown background, sideways blur and the faint clock.</summary>
        private readonly (double Start, double End)[] sakuyaStops;

        /// <summary>Reisen's attacks: everything freezes in them, and slides sideways once they are over.</summary>
        private readonly (double Start, double End)[] reisenWindows;

        /// <summary>Sakuya's knives that hang round her until they are thrown.</summary>
        private readonly BulletEmitter[] heldEmitters;

        /// <summary>One throw of Chen (an attack of hers): where she starts and which way she flies are caught when it begins (and forgotten when time goes back before it).</summary>
        private sealed class ChenThrow
        {
            public BossPeriod Period = null!;
            public double Start;
            public double End;
            public float Speed;
            public bool Known;
            public double CaughtAt;
            public Vector2 From;
            public Vector2 Direction;
        }

        private readonly ChenThrow[] chenThrows;

        /// <summary>The moments (game time) when Koishi's bullets and beams vanish, and how long a beat is there.</summary>
        private readonly (double Time, double Beat)[] koishiMarks;

        /// <summary>Some ring has big bullets that turn the bullets they touch (Ringo's).</summary>
        private readonly bool hasDeflectors;

        /// <summary>The turns that have happened (the ring, the bullet, game time), so time going back can take them back.</summary>
        private readonly List<(BulletEmitter Emitter, int Bullet, double Time, BulletEmitter.Deflection? Before)> deflectLog = new List<(BulletEmitter, int, double, BulletEmitter.Deflection?)>(); // (Before: the turn the bullet had until then, put back when time goes back over this one)

        private bool koishiHideAll;
        private float koishiFlicker;
        private int koishiPeriods; // how many sections Koishi has

        /// <summary>How far the screen is turned now, in radians (see <see cref="MirrorAngleAt"/>); set at the start of every frame.</summary>
        private float mirrorRadians, previousMirrorRadians;
        private bool hasScreenBounds;
        private Vector2 screenMin, screenMax;

        public BulletField(BulletEmitter[] emitters, LaserEmitter[] lasers, BossPeriod[] bosses, float circleRadius, SliderZone[] sliderZones, CircleZone[] circleZones, double damagePerBullet, double damagePerSliderTick, Func<Vector2?> cursorScreenPosition)
        {
            this.emitters = emitters;
            this.lasers = lasers;
            this.bosses = bosses;
            effects = BossEffects.Build(bosses);

            // Seija turns the screen round during every attack of hers
            var mirrorList = new List<(double, double)>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ScreenMirror)
                    continue;

                int attacks = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);

                for (int i = 0; i < attacks; i++)
                    mirrorList.Add((period.LaserStarts[i], period.LaserEnds[i]));
            }

            mirrorList.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            mirrorWindows = mirrorList.ToArray();

            // what the bosses' mechanics do to the bullets and beams that are flying: Seija's bounce, Cirno's freeze, Yukari's pause, Sakuya's time stop (see BossOps)
            BossOps.Build(bosses, out bulletOps, out var laserOps);

            if (bulletOps.Length > 0)
            {
                foreach (var emitter in emitters)
                    emitter.ConfigureOps(bulletOps, bounceArea);

                // (the bullets that Zanmu and Toyohime drop are stopped by the freezes of the other bosses too)
                var freezes = bulletOps.Where(op => op.Kind == TrajectoryOpKind.Freeze && !op.Slides).ToArray();

                if (freezes.Length > 0)
                {
                    foreach (var emitter in emitters)
                        emitter.FallFreezes = freezes;
                }
            }

            if (laserOps.Length > 0)
            {
                foreach (var laser in lasers)
                    laser.ConfigureOps(laserOps, bounceArea);
            }

            // Remilia's attacks (the colour of the bullets and of the background follows how far they are) and Sakuya's knives that hang round her
            var remiliaList = new List<(double Start, double End)>();
            var stampedeList = new List<(double Start, double End)>();
            var heldList = new List<BulletEmitter>();
            var freezeList = new List<double>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.FateSpear)
                    remiliaList.AddRange(BossOps.Attacks(period));

                if (period.Boss?.Mechanic == BossMechanic.Stampede)
                    stampedeList.AddRange(BossOps.Attacks(period));

                if (period.Boss?.Mechanic == BossMechanic.Freeze)
                {
                    foreach (var (start, end) in BossOps.Attacks(period))
                    {
                        if (end > start)
                            freezeList.Add(start);
                    }
                }
            }

            freezeList.Sort();
            cirnoFreezes = freezeList.ToArray();

            var swapList = new List<double>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.TimeStop)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                        swapList.Add(end);
                }
            }

            swapList.Sort();
            sakuyaSwaps = swapList.ToArray();

            var stopList = new List<(double Start, double End)>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.TimeStop)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                        stopList.Add((start, end));
                }
            }

            stopList.Sort((a, b) => a.Start.CompareTo(b.Start));
            sakuyaStops = stopList.ToArray();

            var reisenList = new List<(double Start, double End)>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Lunatic)
                    continue;

                // (only the attacks she slides in: their dim, warning and blur)
                reisenList.AddRange(BossOps.ReisenSlides(period));
            }

            reisenList.Sort((a, b) => a.Start.CompareTo(b.Start));
            reisenWindows = reisenList.ToArray();

            // (they slide three circles: the ops do not know how big a circle is)
            foreach (var op in bulletOps)
            {
                if (op.Slides)
                    op.SlideDistance = reisenDistance(circleRadius);
            }

            foreach (var op in laserOps)
            {
                if (op.Slides)
                    op.SlideDistance = reisenDistance(circleRadius);
            }

            foreach (var emitter in emitters)
            {
                if (emitter.Derive == DeriveKind.Held)
                    heldList.Add(emitter);
            }

            remiliaWindows = remiliaList.ToArray();
            stampedeWindows = stampedeList.ToArray();

            // the knives of Remilia have a red handle (Sakuya's and the others' keep the green one)
            redHandles = new bool[emitters.Length];

            for (int e = 0; e < emitters.Length; e++)
            {
                if ((emitters[e].DrawShape ?? emitters[e].Shape) != BulletShape.Knife)
                    continue;

                foreach (var period in bosses)
                {
                    if ((period.Boss?.Mechanic == BossMechanic.FateSpear || period.Boss?.Mechanic == BossMechanic.Destruction) && emitters[e].Time >= period.Start - 1 && emitters[e].Time <= period.End + 1)
                    {
                        redHandles[e] = true;
                        break;
                    }
                }
            }
            heldEmitters = heldList.ToArray();

            // Chen throws herself at the player at the start of every attack
            var throwList = new List<ChenThrow>();
            // Koishi: at the start of every attack, and then every few beats until it ends, the bullets and beams vanish for two beats
            var markList = new List<(double Time, double Beat)>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.Shikigami)
                {
                    foreach (var (start, end) in BossOps.Attacks(period))
                        throwList.Add(new ChenThrow { Start = start, End = end, Period = period, Speed = (period.BulletSpeed > 0 ? period.BulletSpeed : 0.3f) * DanmakusuBalance.CHEN_SPEED_FACTOR });
                }
                else if (period.Boss?.Mechanic == BossMechanic.Unconscious)
                {
                    // she vanishes again and again, at an even rhythm, for the whole section (not only in the laser-burst windows, which have gaps between them): the first time is when it starts,
                    // and the last one has to fit in before it ends
                    double koishiHide = period.BeatLength * DanmakusuBalance.KOISHI_HIDE_BEATS;
                    double koishiEvery = Math.Max(period.BeatLength * DanmakusuBalance.KOISHI_HIDE_EVERY_BEATS, koishiHide + 1);

                    for (double hideAt = period.ShootStart; hideAt + koishiHide <= period.End + 1; hideAt += koishiEvery)
                        markList.Add((hideAt, period.BeatLength));
                }
            }

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.Unconscious)
                    koishiPeriods++;
            }

            foreach (var emitter in emitters)
            {
                if (emitter.Deflector)
                    hasDeflectors = true;
            }

            throwList.Sort((a, b) => a.Start.CompareTo(b.Start));
            markList.Sort((a, b) => a.Time.CompareTo(b.Time));
            chenThrows = throwList.ToArray();
            koishiMarks = markList.ToArray();
            this.sliderZones = sliderZones; // (before the stage: Urumi's act takes the objects from them)
            this.circleZones = circleZones;
            buildStage();
            buildCues();

            var moonList = new List<MoonWindow>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Moon)
                    continue;

                moonList.Add(new MoonWindow
                {
                    Period = period,
                    Start = period.ShootStart,
                    End = period.End,
                    Direction = moonList.Count % 2 == 0 ? 1f : -1f, // (they turn the other way round in every section)
                    AngularSpeed = (float)(Math.Tau / (DanmakusuBalance.MOON_ORBIT_BEATS * Math.Max(period.BeatLength, 1))),
                    Angle0 = -MathF.PI / 2,
                    StarRadius = period.StarRadius > 0 ? period.StarRadius : circleRadius * 0.5f,
                    StarSpeed = (period.BulletSpeed > 0 ? period.BulletSpeed : 0.3f) * DanmakusuBalance.MOON_STREAM_SPEED_FACTOR,
                });
            }

            moons = moonList.ToArray();

            var slots = new List<BossInfo?>();

            foreach (var period in bosses)
            {
                while (slots.Count <= period.BossSlot)
                    slots.Add(null);

                slots[period.BossSlot] ??= period.Boss;
            }

            // (a map whose sections all belong to the first bosses has none of the others on the HUD; a place nobody has had is filled, so that the numbers stay right)
            var anyBoss = slots.Find(boss => boss != null);
            slotBosses = anyBoss == null ? System.Array.Empty<BossInfo>() : slots.Select(boss => boss ?? anyBoss).ToArray();
            this.circleRadius = circleRadius;
            this.damagePerBullet = damagePerBullet;
            this.damagePerSliderTick = damagePerSliderTick;
            this.cursorScreenPosition = cursorScreenPosition;

            var aimed = new List<int>();

            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i].AimAtPlayer || emitters[i].Falling || emitters[i].FollowsBoss || emitters[i].FromPlayer || emitters[i].Derive != DeriveKind.None)
                    aimed.Add(i);
            }

            var rotating = new List<int>();

            for (int i = 0; i < lasers.Length; i++)
            {
                if (lasers[i].Rotates)
                    rotating.Add(i);
            }

            rotatingLasers = rotating.ToArray(); // lasers are sorted by time, so this is too

            // (of those, the spinners' and the bosses': they have the warning, the beam sound, the shake and the dimming; the beams of a mania long note or a taiko drumroll turn too, but are a slider's)
            spinnerLasers = rotating.Where(index => lasers[index].SpinnerLike).ToArray();

            // the power-up warning leads into every spinner / boss laser, and into every attack of a boss without lasers (Mystia's)
            var warnings = new List<(double Time, string Name)>();

            foreach (int index in spinnerLasers)
            {
                if (!lasers[index].KeepsOrigin) // (Reisen's short beams come every half beat: her warning is the one before her freeze)
                    warnings.Add((lasers[index].StartTime, "power_up"));
            }

            foreach (var (reisenStart, _) in reisenWindows)
                warnings.Add((reisenStart, "power_up"));

            // Nazrin: the power-up leads into every conversion; Zanmu's into every ripple
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Dowsing)
                    continue;

                foreach (double mark in BossOps.ConversionMarks(period))
                    warnings.Add((mark, "power_up"));
            }

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Death)
                    continue;

                foreach (double mark in BossOps.RippleMarks(period))
                    warnings.Add((mark, "power_up"));
            }

            foreach (var period in bosses)
            {
                if (period.HasLasers || period.Boss?.Mechanic == BossMechanic.Dowsing || period.Boss?.Mechanic == BossMechanic.Death || period.Boss?.Mechanic == BossMechanic.Lunatic) // (Nazrin's warnings are the ones before her conversions, added above; Zanmu's are the ones before her ripples; Reisen's the ones before her slides)
                    continue;

                string name = BossSounds.WarningOf(period.Boss?.Mechanic ?? BossMechanic.None);

                // (Mokou's later warnings are the ones before her jumps, see MokouAct)
                bool once = period.Boss?.Mechanic is BossMechanic.Rebirth or BossMechanic.Cloud; // (Mokou's and Ichirin's: one warning, the one that brings them in)

                foreach (double start in period.LaserStarts)
                {
                    warnings.Add((start, name));

                    if (once)
                        break;
                }
            }

            // Hecatia: the power-up leads into every World's Ender (which comes when a throw ends), also the one after her section
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ThreeWorlds)
                    continue;

                worldEnderPlan ??= planWorldEnders(bosses);

                foreach (var (enderPeriod, enderStart) in worldEnderPlan)
                {
                    if (enderPeriod == period && !warnings.Exists(warning => Math.Abs(warning.Time - enderStart) < 1))
                        warnings.Add((enderStart, "power_up"));
                }
            }

            // Reimu (kiai): the power-up leads into the first orb of every pause
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.FantasySeal || period.Kind != BossKind.Kiai)
                    continue;

                foreach (var pause in BossOps.PausesWithTail(period))
                    warnings.Add((pause.Start, "power_up"));
            }

            // Reisen: one warning before each attack, hers (another one that would lead into the same attack is dropped)
            if (reisenWindows.Length > 0)
            {
                warnings.RemoveAll(w => w.Name == "power_up" && !reisenWindows.Any(r => Math.Abs(r.Start - w.Time) < 1) && reisenWindows.Any(r => w.Time > r.Start - DanmakusuBalance.POWER_UP_LENGTH_MS && w.Time < r.Start + 60));
            }

            warnings.Sort((a, b) => a.Time.CompareTo(b.Time));

            // two warnings at the same moment (a spinner's laser and a boss's attack) are one: the boss's own sound wins
            var distinct = new List<(double Time, string Name)>();

            foreach (var warning in warnings)
            {
                int last = distinct.Count - 1;

                if (last >= 0 && warning.Time - distinct[last].Time < 60)
                {
                    if (distinct[last].Name == "power_up")
                        distinct[last] = warning;

                    continue;
                }

                distinct.Add(warning);
            }

            // Hecatia's turning attacks have the reversed warning (see buildCues) instead of the power-up one
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ThreeWorlds)
                    continue;

                var hecatiaTurns = BossOps.Attacks(period);

                for (int i = 1; i < hecatiaTurns.Count; i += 2)
                {
                    double turnStart = hecatiaTurns[i].Start;
                    distinct.RemoveAll(w => Math.Abs(w.Time - turnStart) < 60);
                }
            }

            // a warning is only there when a boss appears (the start of a section); before every other attack the bullets flash instead (see flashLevel)
            var kept = new List<(double Time, string Name)>();

            foreach (var warning in distinct)
            {
                if (Array.Exists(bosses, period => Math.Abs(period.ShootStart - warning.Time) < 60))
                    kept.Add(warning);
                else
                    flashTimes.Add(warning.Time);
            }

            warningTimes = kept.ConvertAll(w => w.Time).ToArray();
            warningNames = kept.ConvertAll(w => w.Name).ToArray();

            int laserCapacity = Math.Max(2, DanmakusuBalance.MOON_BEAM_POINTS + 2);

            foreach (var laser in lasers)
            {
                maxLaserSpan = Math.Max(maxLaserSpan, laser.Span);
                laserCapacity = Math.Max(laserCapacity, laser.PointCapacity);
            }

            laserBuffer = new Vector2[laserCapacity];
            laserWidthBuffer = new float[laserCapacity];
            laserDistanceBuffer = new float[laserCapacity];
            laserLookBuffer = new BulletLook?[laserCapacity];
            sliceGone = new bool[Math.Max(laserCapacity, 8)];

            foreach (var emitter in emitters)
            {
                if (emitter.Area != null)
                {
                    ricochetArea = emitter.Area;
                    break;
                }
            }

            aimEmitters = aimed.ToArray(); // emitters are sorted by time, so this is too
            swirlRings = Enumerable.Range(0, emitters.Length).Where(i => emitters[i].Swirl != null).ToArray();
            var trainList = new List<BulletEmitter>();

            foreach (var ring in emitters)
            {
                if (ring.Derive == DeriveKind.Train)
                    trainList.Add(ring);
            }

            trainRings = trainList.ToArray();

            foreach (var z in sliderZones)
                maxZoneDuration = Math.Max(maxZoneDuration, z.EndTime - z.StartTime);

            RelativeSizeAxes = Axes.Both;
        }

        // the sound of a spinner's laser beams (embedded resource Resources/Samples/master_spark.wav): looped while a beam is being fired
        private ISampleStore? sampleStore;
        private Sample? masterSpark;
        private SampleChannel? masterSparkChannel;

        // the beam sound has a start (played once), a steady part that is looped after it, and an end (played once when the beam stops)
        private Sample? masterSparkStart;
        private Sample? masterSparkEnd;
        private SampleChannel? masterSparkStartChannel;
        private long masterSparkStartTimestamp;
        private readonly List<(SampleChannel Channel, long Timestamp, double Gain, double FadeMs)> fadingChannels = new();

        /// <summary>How loud the beam sound is (1, or half of that for Clownpiece's moon laser).</summary>
        private double sparkGain = 1.0;
        private const double sound_fade_ms = 40; // (the start of the beam sound fades out while the loop fades in over this long, so there is no gap between them; the loop file begins this much early)
        private long masterSparkLoopFadeInTimestamp;

        // the power-up that leads into every spinner / boss laser (Resources/Samples/power_up.wav): it ends when the laser starts
        private Sample? powerUp;
        private SampleChannel? powerUpChannel;
        private bool[] powerUpStarted = [];
        private readonly double[] warningTimes;
        private readonly string[] warningNames; // which sample leads into each: power_up unless the boss has a sound of her own

        /// <summary>The death explosion of the boss of the last section (Resources/Samples/boss_explosion.wav).</summary>
        private Sample? explosionSound;
        private bool explosionPlayed;

        /// <summary>The short sound of Seija's reversal (Resources/Samples/seija_flip.wav): played once when the screen is reversed and once when it returns.</summary>
        private Sample? mirrorFlipSound;
        private bool[] mirrorFlipPlayed = [];
        private double soundTime = double.NaN;
        private int soundStillFrames;

        /// <summary>When the song ends (ms); from then on the player cannot be hit.</summary>
        private double songEndTime = double.PositiveInfinity;

        /// <summary>Used as the end of the song when the length of the track cannot be read: the end of the last hit object.</summary>
        public double FallbackSongEndTime { get; set; } = double.PositiveInfinity;

        /// <summary>
        /// A field that only shows an attack (the preview in the boss menu): it makes no sound, is not connected to the health and the score of the play, takes the colours of <see cref="PaletteOverride"/>,
        /// and does not read the length or the background of the real map.
        /// </summary>
        public bool Preview { get; init; }

        /// <summary>What counts as the screen of the field (its edges are where bullets stop, bounce and leave): this drawable; null = the whole window.</summary>
        public Drawable? ScreenBounds { get; init; }

        /// <summary>The combo colours to use instead of the ones of the skin (null = the skin's).</summary>
        public Color4[]? PaletteOverride { get; init; }

        /// <summary>The length of the beatmap's track in ms (read by reflection, like the background; null if it cannot be found).</summary>
        private static double? songLength(object? workingBeatmap)
        {
            if (workingBeatmap == null)
                return null;

            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object? track = workingBeatmap.GetType().GetProperty("Track", flags)?.GetValue(workingBeatmap);
                return track?.GetType().GetProperty("Length", flags)?.GetValue(track) as double?;
            }
            catch
            {
                return null;
            }
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            if (Preview)
            {
                healthProcessor = null;
                scoreProcessor = null;
            }

            backgroundColour = Preview ? null : BackgroundColour.Of(workingBeatmap?.Value);
            songEndTime = !Preview && songLength(workingBeatmap?.Value) is double length && length > 0 ? length : FallbackSongEndTime;
            try
            {
                var store = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(BulletField).Assembly), "Resources/Samples");
                sampleStore = audio.GetSampleStore(store);
                masterSparkStart = sampleStore.Get("master_spark_start");
                masterSpark = sampleStore.Get("master_spark_loop");
                masterSparkEnd = sampleStore.Get("master_spark_end");
                powerUp = sampleStore.Get("power_up");
                explosionSound = sampleStore.Get("boss_explosion");
                mirrorFlipSound = sampleStore.Get("seija_flip");

                foreach (string name in cue_names)
                    cueSamples[name] = sampleStore.Get(cue_sample_aliases.TryGetValue(name, out string? shared) ? shared : name);
            }
            catch
            {
                masterSpark = null; // no sound, but the game goes on
            }

            InternalChildren = new Drawable[]
            {
                moonLayer = new MoonLayer(false) { BaseColour = backgroundColour },
                remiliaBackground = new Box { Colour = new Color4(0.5f, 0.02f, 0.06f, 1f), Alpha = 0f }, // (Remilia's attack: the background turns dark red)
                remiliaMoon = new MoonSprite { Colour = new Color4(1f, 0.1f, 0.08f, 1f), Alpha = 0f, Anchor = Anchor.TopLeft, Origin = Anchor.Centre }, // (and a giant red moon shows through it)
                cirnoBackground = new Box { Colour = new Color4(0.02f, 0.08f, 0.45f, 1f), Alpha = 0f }, // (Cirno freezes: the background flashes dark blue and fades out)
                sakuyaBackground = new Box { Colour = new Color4(0.16f, 0.08f, 0.03f, 1f), Alpha = 0f }, // (Sakuya's time stop: dark brown)
                sakuyaClock = new ClockSprite { Alpha = 0f, Anchor = Anchor.TopLeft, Origin = Anchor.Centre }, // (and a faint clock that grows away)
                reisenBackground = new Box { Colour = new Color4(0.22f, 0.04f, 0.38f, 1f), Alpha = 0f }, // (Reisen's freeze: a dim purple)
                deathBackground = new Box { Colour = new Color4(0.30f, 0.30f, 0.32f, 1f), Alpha = 0f }, // (Zanmu: a dimmed grey, with her skull on it)
                miracleBackground = new Box { Colour = new Color4(0.03f, 0.22f, 0.08f, 1f), Alpha = 0f }, // (Sanae's attacks: a dim green, with her star on it)
                hasteBackground = new Box { Colour = new Color4(0.44f, 0.41f, 0.32f, 1f), Alpha = 0f }, // (Gengetsu's attacks: a light grey dim with a touch of gold, deeper and deeper)
                stampedeBackground = new Box { Colour = new Color4(0.24f, 0.13f, 0.05f, 1f), Alpha = 0f }, // (Saki's attacks: a brown dim, deeper and deeper, with the horse's head on it)
                stormBackground = new Box { Colour = new Color4(0.1f, 0.11f, 0.13f, 1f), Alpha = 0f }, // (Tojiko's sections: a dark grey dim, under her clouds)
                tideBackground = new Box { Colour = new Color4(0.03f, 0.24f, 0.46f, 1f), Alpha = 0f }, // (Wakasagihime's attacks: an ocean blue dim)
                mikoBackground = new Box { Colour = Color4.Black, Alpha = 0f }, // (Miko's beams: the screen dims round them)
                kaguyaBackground = new Box { Colour = ColourInfo.GradientVertical(new Color4(0.02f, 0.04f, 0.2f, 1f), new Color4(0f, 0.005f, 0.04f, 1f)), Alpha = 0f }, // (Kaguya's sections: a night of dark blue, darker towards the bottom)
                impactBackground = new Box { Colour = Color4.White, Alpha = 0f }, // (the World's Ender's impact frame: a white screen, under everything that goes black)
                verdantBackground = new Box { Colour = new Color4(0.12f, 0.2f, 0.05f, 1f), Alpha = 0f }, // (Daiyousei's attacks: a dark warm green dim)
                stageBack = new StageLayer(1024, false, StageTexture.Cone, StageTexture.Wedge, StageTexture.Light, StageTexture.Glow, StageTexture.Pane, StageTexture.Taiko, StageTexture.Skull, StageTexture.Eye, StageTexture.BearTrapOpen, StageTexture.BearTrapClosed, StageTexture.BigStar, StageTexture.HorseHead, StageTexture.Boat, StageTexture.Prism, StageTexture.ShootCone, StageTexture.KaguyaMoon, StageTexture.KaguyaMoonCap, StageTexture.BloodPool, StageTexture.SeijaArrow, StageTexture.Anchor) { Grows = true }, // (Sunny Milk's prism, Flandre's eyes, Nue's cones, Raiko's drums, Sannyo's smoke, Okina's light, Seija's arrows, Murasa's anchors: under the bullets)
                stageEnder = Array.Exists(bosses, period => period.Boss?.Mechanic == BossMechanic.ThreeWorlds) ? new StageLayer(220, false, StageTexture.EarthChunk8, StageTexture.EarthChunk9, StageTexture.EarthChunk10, StageTexture.EarthChunk11, StageTexture.EarthDebris0, StageTexture.EarthDebris1, StageTexture.EarthDebris2, StageTexture.EarthDebris3, StageTexture.EarthDebris4, StageTexture.EarthDebris5, StageTexture.EarthDebris6, StageTexture.EarthDebris7, StageTexture.EarthDebris8, StageTexture.EarthDebris9, StageTexture.EarthDebris10, StageTexture.EarthDebris11, StageTexture.EarthDebris12, StageTexture.EarthDebris13, StageTexture.EarthDebris14, StageTexture.EarthDebris15, StageTexture.EarthDebris16, StageTexture.EarthDebris17, StageTexture.EarthDebris18, StageTexture.EarthDebris19, StageTexture.EarthDebris20, StageTexture.EarthDebris21, StageTexture.EarthDebris22, StageTexture.EarthDebris23, StageTexture.EarthDebris24, StageTexture.EarthDebris25, StageTexture.EarthDebris26, StageTexture.EarthDebris27, StageTexture.EarthDebris28, StageTexture.EarthDebris29, StageTexture.EarthDebris30, StageTexture.EarthDebris31, StageTexture.EarthDebris32, StageTexture.EarthDebris33, StageTexture.EarthDebris34, StageTexture.EarthDebris35, StageTexture.EarthDebris36, StageTexture.EarthDebris37, StageTexture.EarthDebris38, StageTexture.EarthDebris39, StageTexture.EarthDebris40, StageTexture.EarthDebris41, StageTexture.EarthDebris42, StageTexture.EarthDebris43, StageTexture.EarthDebris44, StageTexture.EarthDebris45, StageTexture.EarthDebris46, StageTexture.EarthDebris47, StageTexture.Earth, StageTexture.EarthBig, StageTexture.EarthCracks, StageTexture.EarthCracksWhite, StageTexture.EarthHoleGlow0, StageTexture.EarthHoleGlow1, StageTexture.EarthChunk0, StageTexture.EarthChunk1, StageTexture.EarthChunk2, StageTexture.EarthChunk3, StageTexture.EarthChunk4, StageTexture.EarthChunk5, StageTexture.EarthChunk6, StageTexture.EarthChunk7, StageTexture.EarthPieceHot0, StageTexture.EarthPieceHot1, StageTexture.EarthPieceHot2, StageTexture.EarthPieceHot3, StageTexture.EarthPieceHot4, StageTexture.EarthPieceHot5, StageTexture.EarthPieceHot6, StageTexture.EarthPieceHot7, StageTexture.EarthPieceHot8, StageTexture.EarthPieceHot9, StageTexture.EarthPieceHot10, StageTexture.EarthPieceHot11, StageTexture.EarthPieceHot12, StageTexture.EarthPieceHot13, StageTexture.EarthPieceHot14, StageTexture.EarthPieceHot15, StageTexture.Glow, StageTexture.EarthPiece0, StageTexture.EarthPiece1, StageTexture.EarthPiece2, StageTexture.EarthPiece3, StageTexture.EarthPiece4, StageTexture.EarthPiece5, StageTexture.EarthPiece6, StageTexture.EarthPiece7, StageTexture.EarthPiece8, StageTexture.EarthPiece9, StageTexture.EarthPiece10, StageTexture.EarthPiece11, StageTexture.EarthPiece12, StageTexture.EarthPiece13, StageTexture.EarthPiece14, StageTexture.EarthPiece15) { Grows = true } : new StageLayer(1, false), // (the World's Ender's Earth: under the bullets; its big pictures only when Hecatia is on the map)
                stageRipple = new StageLayer(400, true, StageTexture.Ring, StageTexture.MagicCircle, StageTexture.Chevron, StageTexture.SeijaArrow) { Grows = true }, // (Raiko's ripples, Toyohime's circle, Wakasagihime's bubbles, Seija's arrows round the player and round her: only inside the screen; as many as there are)
                stageOrbs = new StageLayer(1300, false, StageTexture.Orb, StageTexture.OrbGlow, StageTexture.Glow) { Grows = true }, // (the big round bullets, drawn as sharp as Mokou's ball, under the bullets: five pictures a ball, as many as there are; and the circles that fall in Urumi's section)
                playerCircle, // (the player's magic circle: under the bullets, so that it hides none of them)
                renderer = new BulletRenderer(),
                new Container { Children = portalPieces }, // (Okina's portals: rounded bars, above the bullets)
                stageTree = new StageLayer(1024, false, StageTexture.Orb, StageTexture.Glow, StageTexture.Light, StageTexture.LitArc0, StageTexture.LitArc1, StageTexture.LitArc2, StageTexture.LitArc3, StageTexture.LitArc4, StageTexture.LitArc5, StageTexture.LitArc6, StageTexture.LitArc7) { Grows = true }, // (Yuyuko's petals and their bloom: above the bullets)
                moonTopLayer = new MoonLayer(true) { BaseColour = backgroundColour }, // (the big star where the moon beams start: above them)
                circleSlot = new Container(), // (Ichirin's magic circle is drawn here, under her cloud: see the constructor)
                stageCloud = new StageLayer(8, false, StageTexture.UnzanHead, StageTexture.UnzanHandLeft, StageTexture.UnzanHandRight), // (Ichirin's cloud, the hands and then the head: right under her, above her magic circle and the bullets)
                lingeringBoss = new BossCharacter(slotBosses) { BaseColour = backgroundColour, Lingering = true }, // (a boss who has left while a mechanic of hers goes on)
                lingeringBoss2 = new BossCharacter(slotBosses, 1) { BaseColour = backgroundColour, Lingering = true },
                lingeringBoss3 = new BossCharacter(slotBosses, 2) { BaseColour = backgroundColour, Lingering = true },
                seijaClone = new BossCharacter(slotBosses) { BaseColour = backgroundColour, Ghost = true }, // (Seija's see-through clone before the screen turns: where she will be seen after it)
                bossCharacter = new BossCharacter(slotBosses) { BaseColour = backgroundColour },
                bossCharacter2 = new BossCharacter(slotBosses, 1) { BaseColour = backgroundColour },
                bossCharacter3 = new BossCharacter(slotBosses, 2) { BaseColour = backgroundColour },
                stageBulletsUnder = new StageLayer(96, false, StageTexture.Orb, StageTexture.OrbGlow, StageTexture.Glow) { Grows = true }, // (the balls Mokou throws in a break and in a spinner: above the bullets and above her)
                stageBullets = new StageLayer(96, false, StageTexture.Orb, StageTexture.OrbGlow, StageTexture.Glow) { Grows = true }, // (Mokou's big circles: above the bullets and above her, over the balls she throws)
                stageBats = new StageLayer(512, false, StageTexture.Bat0, StageTexture.Bat1, StageTexture.Bat2, StageTexture.Bat3, StageTexture.Bat4, StageTexture.Bat5, StageTexture.Bat6, StageTexture.Bat7) { Grows = true }, // (Koakuma's bats: above the bullets)
                stageFront = new StageLayer(160, false, StageTexture.Earth, StageTexture.Moon, StageTexture.Sun, StageTexture.Ufo, StageTexture.Glow, StageTexture.YinYang, StageTexture.ByakurenStar, StageTexture.ByakurenStarGlow,
                    StageTexture.SekibankiHead0, StageTexture.SekibankiHead1, StageTexture.SekibankiHead2, StageTexture.SekibankiHead3, StageTexture.SekibankiHead4, StageTexture.SekibankiHead5,
                    StageTexture.SekibankiHead6, StageTexture.SekibankiHead7, StageTexture.SekibankiHead8, StageTexture.SekibankiHead9, StageTexture.SekibankiHead10, StageTexture.SekibankiHead11) { Grows = true }, // (planets, UFOs, Reimu's orbs, Byakuren's stars, Sekibanki's heads)
                stageTop = new StageLayer(96, false, StageTexture.Orb, StageTexture.Glow, StageTexture.Ring, StageTexture.Wedge, StageTexture.LitArc0, StageTexture.LitArc1, StageTexture.LitArc2, StageTexture.LitArc3, StageTexture.LitArc4, StageTexture.LitArc5, StageTexture.LitArc6, StageTexture.LitArc7) { Grows = true }, // (the black hole - a dark circle with the light it bends round it, its rays and their flashes: above everything except the HUD)
                impactStars = new ImpactStarLayer(), // (the World's Ender's impact frame: its stars turn what is under them inside out)
                bossHud = new BossHud(Array.FindAll(bosses, period => !period.Borrowed), slotBosses, mapStartTime(sliderZones, circleZones), mapEndTime(sliderZones, circleZones)), // (every section is a piece of the one health bar; where two bosses are on, two lines)
                character,
                comboBreakSound = new SkinnableSound(new SampleInfo("Gameplay/combobreak")),
            };

            // Ichirin's cloud (the hands and the head) is above her magic circle and under her: the circle is drawn below the layer of the cloud instead of in her own drawable
            if (Array.Exists(bosses, period => period.Boss?.Mechanic == BossMechanic.Cloud))
            {
                circleSlot.Add(bossCharacter.CreateCircleProxy());
                circleSlot.Add(bossCharacter2.CreateCircleProxy());
                circleSlot.Add(bossCharacter3.CreateCircleProxy());
            }
        }

        private Container circleSlot = null!;

        /// <summary>When the map's first circle or slider starts (NaN if it has none): the boss bar starts there.</summary>
        private static double mapStartTime(SliderZone[] sliders, CircleZone[] circles)
        {
            double start = double.NaN;

            foreach (var slider in sliders)
                start = double.IsNaN(start) ? slider.StartTime : Math.Min(start, slider.StartTime);

            foreach (var circle in circles)
                start = double.IsNaN(start) ? circle.Time : Math.Min(start, circle.Time);

            return start;
        }

        /// <summary>When the map's last circle or slider ends (NaN if it has none): the boss bar is empty there.</summary>
        private static double mapEndTime(SliderZone[] sliders, CircleZone[] circles)
        {
            double end = double.NaN;

            foreach (var slider in sliders)
                end = double.IsNaN(end) ? slider.EndTime : Math.Max(end, slider.EndTime);

            foreach (var circle in circles)
                end = double.IsNaN(end) ? circle.Time : Math.Max(end, circle.Time);

            return end;
        }

        /// <summary>
        /// The field sits behind the hit objects (so sliders and circles draw above bullets); this proxy draws the
        /// character on top of everything again so it never disappears under a slider body.
        /// </summary>
        public Drawable CreateCharacterProxy() => character.CreateProxy();

        /// <summary>This map has a boss with a moon (Clownpiece).</summary>
        public bool HasMoon => moons.Length > 0;

        /// <summary>The moon (and its warning stars) again, to be drawn above the hit objects: it is above everything except the boss, the character and the HUD.</summary>
        public Drawable CreateMoonProxy() => moonLayer.CreateProxy();

        /// <summary>The boss again, to be drawn above the moon.</summary>
        public Drawable CreateBossProxy() => bossCharacter.CreateProxy();

        /// <summary>The second boss of the section of two, again, to be drawn above the moon.</summary>
        public Drawable CreateSecondBossProxy() => bossCharacter2.CreateProxy();

        public Drawable CreateThirdBossProxy() => bossCharacter3.CreateProxy();

        /// <summary>Mokou's balls, above the bosses when those are drawn over the field (see <see cref="CreateBossProxy"/>), as they are in the field.</summary>
        public Drawable CreateBallsProxy() => new Container { Children = new Drawable[] { stageBulletsUnder.CreateProxy(), stageBullets.CreateProxy() } };

        /// <summary>The boss HUD again, to be drawn above a flashlight (the boss names and the health bar are never in the dark).</summary>
        public Drawable CreateHudProxy() => bossHud.CreateProxy();

        protected override void LoadComplete()
        {
            base.LoadComplete();

            refreshPalette();
            skin.SourceChanged += refreshPalette;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (skin != null)
                skin.SourceChanged -= refreshPalette;

            stopMasterSpark();
            powerUpChannel?.Stop();
            treeLoopChannel?.Stop();
            holeLoopChannel?.Stop();
            hasteRiserChannel?.Stop();
            sampleStore?.Dispose();
        }

        /// <summary>Reads the current skin's (or beatmap's) combo colours, the same list the hit circles are coloured from.</summary>
        private void refreshPalette()
        {
            IReadOnlyList<Color4>? comboColours = PaletteOverride ?? skin.GetConfig<GlobalSkinColours, IReadOnlyList<Color4>>(GlobalSkinColours.ComboColours)?.Value;

            if (comboColours != null && comboColours.Count > 0)
            {
                var colours = new Color4[comboColours.Count];
                var borders = new Color4[comboColours.Count];

                for (int i = 0; i < colours.Length; i++)
                {
                    colours[i] = DanmakusuBalance.BulletColour(comboColours[i]);
                    borders[i] = DanmakusuBalance.BorderColour(colours[i]);
                }

                palette = colours;
                borderPalette = borders;
            }

            // the bullet sprites are rendered from the palette once, here; the background colour (for spinner / boss stars) is one more entry behind the combo colours
            var bodies = palette;
            var borders2 = borderPalette;
            backgroundStyle = -1;

            if (backgroundColour is Color4 background)
            {
                int keep = Math.Min(palette.Length, 31);
                bodies = new Color4[keep + 1];
                borders2 = new Color4[keep + 1];
                Array.Copy(palette, bodies, keep);
                Array.Copy(borderPalette, borders2, keep);
                bodies[keep] = background;
                borders2[keep] = DanmakusuBalance.BorderColour(background);
                backgroundStyle = keep;
            }

            renderer.SetPalette(bodies, borders2);
        }

        #region Freeze input

        public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
        {
            switch (e.Action)
            {
                case OsuAction.LeftButton:
                    if (key2Held > 0)
                        resetFreezePending = true; // second key pressed while the first is held: the freeze point moves to the cursor

                    key1Held++;
                    break;

                case OsuAction.RightButton:
                    if (key1Held > 0)
                        resetFreezePending = true;

                    key2Held++;
                    break;
            }

            return false; // never consumed: the keys still do everything they did before
        }

        public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
        {
            switch (e.Action)
            {
                case OsuAction.LeftButton:
                    key1Held = Math.Max(0, key1Held - 1);
                    break;

                case OsuAction.RightButton:
                    key2Held = Math.Max(0, key2Held - 1);
                    break;

                default:
                    return;
            }

            if (!frozen)
                teleportPending = true; // both keys are up: the freeze is over
        }

        #endregion

        #region Keyboard controls

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.UI.DrawableRuleset? drawableRuleset { get; set; }

        /// <summary>The player moves with the keyboard (<see cref="ControlMode.Keyboard"/>); not in a replay, which only has the cursor (the character follows it there).</summary>
        private bool keyboardControls => !Preview && Controls.Mode == ControlMode.Keyboard && drawableRuleset?.HasReplayLoaded.Value != true && cursorScreenPosition() != null; // (the attack preview's character dodges by itself)

        private bool up, down, left, right, spaceHeld, shiftHeld, spacePressedPending;

        /// <summary>The character is held where it is from <see cref="forcedFreezeFrom"/> until <see cref="forcedFreezeUntil"/> (game time; Enoko's trap has caught it).</summary>
        private double forcedFreezeFrom = double.PositiveInfinity, forcedFreezeUntil = double.NegativeInfinity;

        private bool forcedHeld;

        /// <summary>Holds the character where it is from <paramref name="from"/> for <paramref name="length"/> ms (a later hold that overlaps lengthens it).</summary>
        private void holdCharacter(double from, double length)
        {
            if (from <= forcedFreezeUntil && from >= forcedFreezeFrom)
                forcedFreezeUntil = Math.Max(forcedFreezeUntil, from + length);
            else
            {
                forcedFreezeFrom = from;
                forcedFreezeUntil = from + length;
            }
        }

        private Vector2 keyboardDirection => new Vector2((right ? 1f : 0f) - (left ? 1f : 0f), (down ? 1f : 0f) - (up ? 1f : 0f));

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!keyboardControls)
                return false;

            switch (e.Key)
            {
                case osuTK.Input.Key.W:
                case osuTK.Input.Key.Up:
                    up = true;
                    break;

                case osuTK.Input.Key.S:
                case osuTK.Input.Key.Down:
                    down = true;
                    break;

                case osuTK.Input.Key.A:
                case osuTK.Input.Key.Left:
                    left = true;
                    break;

                case osuTK.Input.Key.D:
                case osuTK.Input.Key.Right:
                    right = true;
                    break;

                case osuTK.Input.Key.Space:
                    if (!spaceHeld)
                        spacePressedPending = true;

                    spaceHeld = true;
                    break;

                case osuTK.Input.Key.LShift:
                case osuTK.Input.Key.RShift:
                    shiftHeld = true;
                    break;
            }

            return false; // (never consumed: osu!'s own keys still work)
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.W:
                case osuTK.Input.Key.Up:
                    up = false;
                    break;

                case osuTK.Input.Key.S:
                case osuTK.Input.Key.Down:
                    down = false;
                    break;

                case osuTK.Input.Key.A:
                case osuTK.Input.Key.Left:
                    left = false;
                    break;

                case osuTK.Input.Key.D:
                case osuTK.Input.Key.Right:
                    right = false;
                    break;

                case osuTK.Input.Key.Space:
                    spaceHeld = false;
                    break;

                case osuTK.Input.Key.LShift:
                case osuTK.Input.Key.RShift:
                    shiftHeld = false;
                    break;
            }

            base.OnKeyUp(e);
        }

        #endregion

        /// <summary>(for the benchmark tool, Tools/SectionCheck: when it is set, Update adds the time each of its steps takes here, in Stopwatch ticks, in the order of <see cref="PROFILE_STEPS"/>)</summary>
        public static long[]? ProfileTicks;

        public static readonly string[] PROFILE_STEPS =
        {
            "spawn", "boss move", "stage", "aim and steer", "homing", "deflections", "moon", "absorb", "late stage", "collisions", "stage hits", "zones", "sounds", "boss", "screen bounds",
            "retire and sort", "ring bullets", "mechanic bullets", "draw stage", "moon layer", "lasers",
        };

        /// <summary>(for the benchmark tool too: how many beams were read and how many points they had, while <see cref="ProfileTicks"/> is set)</summary>
        public static long ProfileBeams, ProfilePoints, ProfileRings, ProfileBullets, ProfileOutlinePieces, ProfileInstances; // (instances: what the renderer is handed to draw, copies and previews included)

        private static long profileMark;

        /// <summary>How long each step of this frame's Update took (Stopwatch ticks): a frame that takes too long is written to osu!'s log with them (see <see cref="logSlowFrame"/>).</summary>
        private static readonly long[] frameTicks = new long[PROFILE_STEPS.Length];

        private static void profile(int step)
        {
            long now = Stopwatch.GetTimestamp();

            if (step >= 0)
            {
                long spent = now - profileMark;
                frameTicks[step] += spent;

                if (ProfileTicks != null)
                    ProfileTicks[step] += spent;
            }

            profileMark = now;
        }

        private long lastUpdateStamp;

        /// <summary>
        /// (a frame of the field that took longer than <see cref="DanmakusuBalance.SLOW_FRAME_LOG_MS"/> is written to osu!'s runtime log with its slowest steps, and so is a long wait between two frames
        /// (the game or its audio stalled, not the field): when osu!'s clock locks up after a stall, the log says where the time went)
        /// </summary>
        private bool bossesLogged;
        private readonly long[] secondTicks = new long[PROFILE_STEPS.Length];
        private int secondFrames;
        private double secondStart = double.PositiveInfinity;

        private void logSlowFrame(double now)
        {
            long stamp = Stopwatch.GetTimestamp();
            double gap = lastUpdateStamp == 0 ? 0 : (stamp - lastUpdateStamp) * 1000.0 / Stopwatch.Frequency;
            lastUpdateStamp = stamp;

            long total = 0;

            foreach (long ticks in frameTicks)
                total += ticks;

            double spent = total * 1000.0 / Stopwatch.Frequency;

            // (which bosses these are, once; and every second of the map in which the field was slow on average: many frames of 30 ms hurt as much as one of 300, and no single one of them is worth a line)
            if (!bossesLogged && bosses.Length > 0)
            {
                bossesLogged = true;
                Logger.Log($"Danmakusu: bosses ({(bosses.Any(period => period.Rank == 2 && !period.Borrowed) ? "triple" : bosses.Any(period => period.Rank == 1 && !period.Borrowed) ? "double" : "single")}) {string.Join(", ", bosses.Where(period => !period.Borrowed).Select(period => period.Boss?.Id).Distinct())}");
            }

            for (int i = 0; i < frameTicks.Length; i++)
                secondTicks[i] += frameTicks[i];

            secondFrames++;

            if (now < secondStart || now - secondStart >= 1000)
            {
                long secondTotal = secondTicks.Sum();
                double average = secondFrames > 0 ? secondTotal * 1000.0 / Stopwatch.Frequency / secondFrames : 0;

                if (now >= secondStart && average >= DanmakusuBalance.SLOW_SECOND_LOG_MS)
                {
                    var worst = Enumerable.Range(0, secondTicks.Length).OrderByDescending(i => secondTicks[i]).Take(4).Where(i => secondTicks[i] > 0).Select(i => $"{PROFILE_STEPS[i]} {secondTicks[i] * 1000.0 / Stopwatch.Frequency / secondFrames:F1}");
                    Logger.Log($"Danmakusu: slow second at {now:F0} ms: {average:F1} ms a frame over {secondFrames} frames ({string.Join(", ", worst)}); rings {activeCount}");
                }

                Array.Clear(secondTicks);
                secondFrames = 0;
                secondStart = now;
            }

            if (spent > DanmakusuBalance.SLOW_FRAME_LOG_MS || gap > DanmakusuBalance.SLOW_FRAME_LOG_MS * 2)
            {
                var steps = new List<string>();

                for (int i = 0; i < frameTicks.Length; i++)
                {
                    double ms = frameTicks[i] * 1000.0 / Stopwatch.Frequency;

                    if (ms >= 5)
                        steps.Add($"{PROFILE_STEPS[i]} {ms:F0}");
                }

                Logger.Log($"Danmakusu: slow frame at {now:F0} ms: the field took {spent:F0} ms, {gap:F0} ms since the frame before ({string.Join(", ", steps)}); rings {activeCount}");
            }

            Array.Clear(frameTicks);
        }

        // (osu!'s gameplay clock can lock up half a second behind the music: DrawableDanmakusuRuleset puts it back, since the field is not updated while gameplay time stands still)

        /// <summary>Where the real cursor is (the character stays where it froze while the cursor is frozen: this is where the mouse is).</summary>
        private Vector2 cursorPosition;

        /// <summary>Where bosses aim at: the real cursor, also while the character is frozen (before the first frame: the character).</summary>
        private Vector2 aimPosition => initialised ? cursorPosition : characterPosition;

        protected override void Update()
        {
            base.Update();

            logSlowFrame(Time.Current); // (the frame before, if it was slow)
            profile(-1);
            double now = Time.Current;
            mirrorRadians = MirrorAngleAt(now) * MathF.PI / 180f;

            // (the field is shaken a little while a beam fires: Position is that shake, and the simulation does not see it)
            Vector2? cursorScreen = cursorScreenPosition();
            // (the field's own coordinates already undo the turn of the playfield: the cursor is drawn at the mirror image of the mouse, and this is where it is without the turn)
            Vector2 cursor = CursorProbe != null ? CursorProbe(now) : cursorScreen != null ? ToLocalSpace(cursorScreen.Value) + Position : characterPosition;
            cursorPosition = cursor;

            if (!initialised)
            {
                characterPosition = previousCharacterPosition = cursor;
                previousTime = now;
                initialised = true;
            }

            if (now < previousTime - 1)
            {
                // (in play, a clock that slips back a little after a heavy frame - "invalid time value provided by BASS" - is not a seek: the field waits where it is until time has caught up. Rewinding
                // there threw away what the black hole and the moon had taken and worked it all out again, which made the next frame heavier still, and the game never caught up again. A replay's seeks,
                // and any big jump back, still rewind)
                if (drawableRuleset?.HasReplayLoaded.Value != true && previousTime - now < DanmakusuBalance.CLOCK_SLIP_MS)
                    now = previousTime;
                else
                    rewindTo(now);
            }

            if (!failed)
                spawnUpTo(now);

            profile(0);
            bool sweep = true;

            if (now < forcedFreezeUntil && now >= forcedFreezeFrom)
            {
                // a trap (Enoko's) holds the character: it stays where it is, whatever the cursor or the keys do; when it is let go it is put on the cursor again (no path check)
                forcedHeld = true;
            }
            else if (forcedHeld && !keyboardControls)
            {
                forcedHeld = false;
                characterPosition = cursor;
                previousCharacterPosition = cursor;
                sweep = false;
            }
            else if (keyboardControls)
            {
                forcedHeld = false;

                // W A S D (or the arrows) move the character, Shift at half the speed; Space puts it on the cursor, and while Space is held it stays there
                double dt = Math.Clamp(now - previousTime, 0, 100);

                if (spaceHeld)
                {
                    characterPosition = cursor;

                    if (spacePressedPending)
                    {
                        previousCharacterPosition = cursor;
                        sweep = false; // (a teleport: no path check)
                    }
                }
                else if (keyboardDirection.LengthSquared > 0f)
                {
                    float speed = DanmakusuBalance.KEYBOARD_SPEED * (float)Controls.KeyboardSpeed * (shiftHeld ? DanmakusuBalance.KEYBOARD_SLOW : 1f); // (the slider in the boss menu)
                    Vector2 low = stageMin, high = stageMax;
                    characterPosition = Vector2.Clamp(characterPosition + Vector2.Normalize(keyboardDirection) * (speed * (float)dt), low, high);
                }

                spacePressedPending = false;

                // (the bosses aim at the character: the cursor is only where Space puts it)
                cursorPosition = characterPosition;
            }
            else if (frozen)
            {
                if (resetFreezePending)
                {
                    // the second key was pressed: the character jumps to the cursor and stays frozen there (no path check)
                    characterPosition = cursor;
                    previousCharacterPosition = cursor;
                    sweep = false;
                }
                // otherwise stay where we are
            }
            else if (teleportPending)
            {
                // teleport: jump straight to the cursor, skip the path check entirely.
                characterPosition = cursor;
                previousCharacterPosition = cursor;
                sweep = false;
            }
            else
                characterPosition = cursor;

            // (Eiki's circle: the character cannot leave it, by any way of moving)
            foreach (var judgement in judgementActs)
                characterPosition = judgement.Limit(this, characterPosition, now);

            foreach (var act in stageActs)
                act.Early(this, now);

            // the screen was turned or turned back: the character is where it was (the real mouse), but it must never be swept along a path through that frame
            if (Math.Abs(mirrorRadians - previousMirrorRadians) > 1e-4f)
                sweep = false;

            previousMirrorRadians = mirrorRadians;

            resetFreezePending = false;

            if (!frozen)
                teleportPending = false;

            // the boss stands opposite the character: mirrored through the centre of the screen. (Only the boss of a spinner stands in the middle.)
            Vector2 screenCentre = hasScreenBounds ? (screenMin + screenMax) / 2 : new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT) / 2;
            var currentBoss = latestBoss(now);
            var secondBoss = currentBoss != null ? latestBoss(now, 1) : null;
            var thirdBoss = currentBoss != null ? latestBoss(now, 2) : null;

            shownAt[0] = currentBoss;
            shownAt[1] = latestBoss(now, 1);
            shownAt[2] = latestBoss(now, 2);
            shownTime = now;
            // (Marisa stands there too, but half as far from the middle: before the bosses are spread round each other, so that they are spread by where she really stands)
            Vector2 bossWanted = marisaPlace(currentBoss, currentBoss?.Kind == BossKind.Spinner || (currentBoss != null && bossAtCentre(currentBoss, now)) ? screenCentre : screenCentre * 2 - characterPosition, screenCentre);
            Vector2 bossWanted2 = secondBoss == null ? bossWanted : marisaPlace(secondBoss, secondBoss.Kind == BossKind.Spinner || bossAtCentre(secondBoss, now) ? screenCentre : screenCentre * 2 - characterPosition, screenCentre);
            Vector2 bossWanted3 = thirdBoss == null ? bossWanted : marisaPlace(thirdBoss, thirdBoss.Kind == BossKind.Spinner || bossAtCentre(thirdBoss, now) ? screenCentre : screenCentre * 2 - characterPosition, screenCentre);
            // (a boss that her act or her throw puts somewhere is not where this says: two bosses only share a circle when they really stand on each other, not when they would have if they had stayed)
            bool placed = secondBoss != null && currentBoss != null
                          && (stageBossPosition(currentBoss, now, out _) || stageBossPosition(secondBoss, now, out _) || (chenThrows.Length > 0 && (chenPosition(currentBoss, now, out _) || chenPosition(secondBoss, now, out _))));

            Array.Fill(circleGroup, -1);

            if (thirdBoss != null)
                spreadThree(now, currentBoss, secondBoss, thirdBoss, ref bossWanted, ref bossWanted2, ref bossWanted3);
            else
            {
                updateBossOrbit(now, secondBoss != null && !placed,
                    currentBoss != null && (currentBoss.Kind == BossKind.Spinner || bossAtCentre(currentBoss, now)),
                    secondBoss != null && (secondBoss.Kind == BossKind.Spinner || bossAtCentre(secondBoss, now)),
                    ref bossWanted, ref bossWanted2);
            }

            // (Marisa does not move while her Master Spark fires)
            bossWanted = holdStill(currentBoss, now, bossWanted, 0);
            bossWanted2 = holdStill(secondBoss, now, bossWanted2, 1);
            bossWanted3 = holdStill(thirdBoss, now, bossWanted3, 2);
            if (currentBoss != null && chenThrows.Length > 0 && chenPosition(currentBoss, now, out Vector2 chen))
            {
                // Chen is thrown: she is wherever her flight (and her stillness in the pauses) puts her, with no gliding
                bossPosition = bossTarget = bossVisualPosition = chen;
                bossSampleTime = bossSmoothTime = now;
            }
            else if (currentBoss != null && stageActs.Count > 0 && stageBossPosition(currentBoss, now, out Vector2 scripted))
            {
                // Mokou is wherever her jump puts her (and where it ended, in her stay), with no gliding
                bossPosition = bossTarget = bossVisualPosition = scripted;
                bossSampleTime = bossSmoothTime = now;
            }
            else
                moveBoss(now, currentBoss == null, bossWanted);

            if (secondBoss != null && chenThrows.Length > 0 && chenPosition(secondBoss, now, out Vector2 chen2))
            {
                bossPosition2 = bossTarget2 = bossVisualPosition2 = chen2;
                bossSampleTime2 = bossSmoothTime2 = now;
            }
            else if (secondBoss != null && stageActs.Count > 0 && stageBossPosition(secondBoss, now, out Vector2 scripted2))
            {
                bossPosition2 = bossTarget2 = bossVisualPosition2 = scripted2;
                bossSampleTime2 = bossSmoothTime2 = now;
            }
            else
                moveBoss2(now, secondBoss == null, bossWanted2);

            if (thirdBoss != null && chenThrows.Length > 0 && chenPosition(thirdBoss, now, out Vector2 chen3))
            {
                bossPosition3 = bossTarget3 = bossVisualPosition3 = chen3;
                bossSampleTime3 = bossSmoothTime3 = now;
            }
            else if (thirdBoss != null && stageActs.Count > 0 && stageBossPosition(thirdBoss, now, out Vector2 scripted3))
            {
                bossPosition3 = bossTarget3 = bossVisualPosition3 = scripted3;
                bossSampleTime3 = bossSmoothTime3 = now;
            }
            else
                moveBoss3(now, thirdBoss == null, bossWanted3);

            if (failed && healthProcessor?.HasFailed != true)
                failed = false;

            profile(1);
            updateStage(now);
            profile(2);

            double gap = now - previousTime;

            if (!failed)
            {
                aimRings(now);
                aimLasers(now);
                steerLasers(now);
                profile(3);
                stepHoming(now, sweep);
                profile(4);
                updateDeflections(now);
                profile(5);
                aimMoons(now);
                absorbIntoMoon(now);
                profile(6);
                absorbIntoStage(now);
                profile(7);
                lateStage(now);
                profile(8);

                // (the frame in which Sakuya swaps everything hurts nowhere: what is mirrored must not hit the player on the way)
                bool swapping = false;

                foreach (double swap in sakuyaSwaps)
                {
                    if (swap > now)
                        break;

                    if (swap > previousTime)
                    {
                        swapping = true;
                        break;
                    }
                }

                if (gap > 0 && gap <= max_sweep_gap_ms && !swapping)
                {
                    Vector2 start = sweep ? previousCharacterPosition : characterPosition;

                    checkCollisions(previousTime, now, start, characterPosition, now);
                    profile(9);
                    checkStageBullets(previousTime, now, start, characterPosition, now);
                    profile(10);
                    checkCircleZones(previousTime, now, start, characterPosition, now);
                    checkSliderZones(now);
                    profile(11);
                }

                if (healthProcessor?.HasFailed == true)
                {
                    failed = true;
                    failTime = now;
                    failTimestamp = Stopwatch.GetTimestamp();

                    // bullets fall now, so rings that had flown off screen may come back: look at every ring again
                    resetRings();
                    spawnUpTo(failTime);
                }
            }

            previousTime = now;
            previousCharacterPosition = characterPosition;

            updateMasterSpark(now);
            updateExplosionSound(now);
            updateMirrorSound(now);
            updateCues(now);
            updateTreeSound(now);
            updateDeathSound(now);
            updateHoleSound(now);
            updateHasteSound(now);
            updateBeamEffects(now);
            profile(12);
            updateBoss(now);
            profile(13);
            updateRenderer(now);
            profile(20);
            updateRemiliaBackground(failed ? failTime : now);
            updateCirnoBackground(failed ? failTime : now);
            updateSakuyaBackground(failed ? failTime : now);
            updateReisenBackground(failed ? failTime : now);

            // (held away from the mouse - frozen, in Enoko's trap, at the edge of Eiki's circle - the two swap places, only to look at: the skinned cursor is drawn where the character is (the playfield
            // moves it, see DanmakusuPlayfield.swapCursor), and the character's ring, as the ghost, where the mouse is; everything still goes by where the character is)
            CursorSwapped = !keyboardControls && !Preview && !failed && Vector2.DistanceSquared(cursor, characterPosition) > 1f;
            character.Position = CursorSwapped ? cursor : characterPosition;
            character.UpdateState(frozen, CursorSwapped ? characterPosition - cursor : cursor - characterPosition);

            // (the player's magic circle is where the player really is, under the hitbox; in the World's Ender's impact frame it goes black and white as the character does)
            playerCircle.Position = characterPosition;
            playerCircle.Colour = character.Colour;
        }

        /// <summary>(Tools/SectionCheck, TALISMAN_PROBE: the talismans handed to the renderer that stand still - drawn where they were the frame before - and how many of them do not face the middle of the screen, by more than 10 degrees)</summary>
        public static long TalismanProbeStill, TalismanProbeOff;

        public static bool TalismanProbeOn;
        private readonly Dictionary<long, Vector2> talismanProbeLast = new Dictionary<long, Vector2>();

        private void probeTalismans(int count)
        {
            var seen = new Dictionary<long, Vector2>();

            for (int i = 0; i < count; i++)
            {
                ref var b = ref instanceBuffer[i];

                if (b.Shape != BulletShape.Talisman)
                    continue;

                // (by where it is: one that is where a talisman was the frame before stands still)
                long key = ((long)MathF.Round(b.Position.X * 4f) << 32) ^ (long)MathF.Round(b.Position.Y * 4f);
                seen[key] = b.Direction;

                if (!talismanProbeLast.ContainsKey(key))
                    continue;

                TalismanProbeStill++;
                Vector2 toward = facingMiddle(b.Position);
                float cos = Vector2.Dot(toward, b.Direction.LengthSquared > 1e-6f ? Vector2.Normalize(b.Direction) : Vector2.UnitX);

                if (cos < MathF.Cos(10f * MathF.PI / 180f))
                    TalismanProbeOff++;
            }

            talismanProbeLast.Clear();

            foreach (var (key, direction) in seen)
                talismanProbeLast[key] = direction;
        }

        /// <summary>The character is held away from the mouse (frozen, Enoko's trap, Eiki's circle): the skinned cursor is drawn where the character is and the character's ring where the mouse is (see Update).</summary>
        public bool CursorSwapped { get; private set; }

        /// <summary>Where the character is on the screen (the place the skinned cursor is drawn at while <see cref="CursorSwapped"/>).</summary>
        public Vector2 CharacterScreenPosition => ToScreenSpace(characterPosition - Position);

        /// <summary>
        /// 0..1: how strongly a spinner or boss beam is firing right now (the widest beam counts; the beams grow like a parabola over their time).
        /// It drives the screen shake and the dimming of the bullets.
        /// </summary>
        private float beamIntensity(double now)
        {
            float intensity = 0;

            foreach (int index in spinnerLasers)
            {
                var laser = lasers[index];

                if (laser.NoShake || now < laser.StartTime || now > laser.EndTime || laser.EndTime <= laser.StartTime)
                    continue;

                float u = (float)((now - laser.StartTime) / (laser.EndTime - laser.StartTime));
                intensity = Math.Max(intensity, 4 * u * (1 - u));
            }

            return Math.Clamp(intensity, 0f, 1f);
        }

        /// <summary>
        /// 0..1: the shake of Clownpiece's moon: while it grows during the warning it goes from 1 px to 2 px, it stays at 2 px for the whole section, and it dies down
        /// while the moon shrinks away after the section.
        /// </summary>
        private float moonGrowIntensity(double now)
        {
            foreach (var window in moons)
            {
                if (now >= window.Period.Start && now < window.Start)
                    return (float)Math.Clamp((now - window.Period.Start) / Math.Max(window.Start - window.Period.Start, 1), 0.01, 1);

                if (now >= window.Start && now <= window.End)
                    return 1f;

                if (now > window.End && now < window.End + DanmakusuBalance.MOON_FADE_OUT_MS)
                    return (float)(1 - (now - window.End) / DanmakusuBalance.MOON_FADE_OUT_MS);
            }

            return 0f;
        }

        /// <summary>Darkens the bullets and shakes the field a very little while a spinner or boss beam fires.</summary>
        private void updateBeamEffects(double now)
        {
            bool silent = failed || soundStillFrames >= 8;
            float intensity = silent ? 0f : Math.Max(Math.Max(beamIntensity(now), moonGrowIntensity(now)), stageShake(now));

            renderer.SetBrightness(1f - DanmakusuBalance.BEAM_BULLET_DIMMING * (silent ? 0f : dimLevel(now)));

            // (the shake of the beams and stages is 1..2 px; Remilia's starts at 2 px with her attack and dies down to nothing)
            float amplitude = intensity > 0 ? DanmakusuBalance.BEAM_SHAKE_MIN + (DanmakusuBalance.BEAM_SHAKE_MAX - DanmakusuBalance.BEAM_SHAKE_MIN) * intensity : 0f;

            if (!silent)
            {
                amplitude = Math.Max(amplitude, remiliaShake(now));
                amplitude = Math.Max(amplitude, okinaShake(now));
            }

            if (amplitude <= 0.01f)
            {
                Position = Vector2.Zero;
                return;
            }

            // two quick wobbles per axis that do not line up: reads as a rumble, not as a sway
            float x = (float)(Math.Sin(now * 0.137) * 0.6 + Math.Sin(now * 0.311 + 0.7) * 0.4);
            float y = (float)(Math.Sin(now * 0.163 + 2.0) * 0.6 + Math.Sin(now * 0.271 + 1.1) * 0.4);
            Position = new Vector2(x, y) * amplitude;
        }

        /// <summary>
        /// 0..1: how dark the bullets are kept. They stay dark for as long as there is something to see of a spinner or boss beam: from when a
        /// laser starts until its last slice has left the whole screen (only then the darkness fades out), and for the whole time a boss is attacking
        /// (also in the pauses between his bursts).
        /// </summary>
        private float dimLevel(double now)
        {
            const double fade_ms = 200;
            float level = 0;

            foreach (int index in spinnerLasers)
            {
                var laser = lasers[index];

                if (laser.KeepsOrigin)
                    continue; // (Reisen's short beams do not darken the bullets)

                double end = hasScreenBounds ? rotatingGoneTime(laser) : laser.EndTime + laser.Lifetime;

                if (now >= laser.StartTime && now <= end + fade_ms)
                    level = Math.Max(level, (float)Math.Clamp(Math.Min((now - laser.StartTime) / fade_ms, (end + fade_ms - now) / fade_ms), 0, 1));
            }

            foreach (var period in bosses)
            {
                if (period.Kind != BossKind.Spinner && period.HasLasers && now >= period.ShootStart && now <= period.End + fade_ms)
                    level = Math.Max(level, (float)Math.Clamp(Math.Min((now - period.ShootStart) / fade_ms, (period.End + fade_ms - now) / fade_ms), 0, 1));
            }

            return level;
        }

        /// <summary>Rings that aim at the player are turned, the moment they fire, so that their first bullet points at the character.</summary>
        private void aimRings(double now)
        {
            while (nextAim < aimEmitters.Length && emitters[aimEmitters[nextAim]].Time <= now)
                aim(emitters[aimEmitters[nextAim++]]);

            // (Mugetsu: a ring fired in an attack of hers goes round where she is now, not where she goes after)
            while (nextSwirl < swirlRings.Length && emitters[swirlRings[nextSwirl]].Time <= now)
                emitters[swirlRings[nextSwirl++]].SnapSwirl();
        }

        private void aim(BulletEmitter emitter)
        {
            if (emitter.Count == 0)
                return;

            if (emitter.Derive != DeriveKind.None)
            {
                derive(emitter);
                return;
            }

            // the boss's rings come out of the boss (Utsuho's big balls out of where she is drawn: they are too big to come from just behind her)
            if (emitter.FollowsBoss)
            {
                emitter.Origin = emitter.Sharp ? bossVisualOf(emitter.BossRank) : stageShotOrigin(emitter.Time, emitter.BossRank);
                emitter.SpawnRadius = Math.Max(emitter.SpawnRadius, stageShotRadius(emitter.Time, emitter.BossRank));
            }

            // (Nemuno: the ring is fired where the player is, and what falls is pulled the way from where it would have been fired to the player)
            if (emitter.FromPlayer)
            {
                Vector2 way = characterPosition - emitter.Origin;
                emitter.GravityDirection = way.LengthSquared > 1e-6f ? Vector2.Normalize(way) : new Vector2(0f, 1f);
                emitter.Origin = characterPosition;

                if (!emitter.AimAtPlayer)
                    return;
            }

            // (Doremy's mirrored copies aim at the mirror image of the player)
            Vector2 toPlayer = (emitter.AimMirrored ? emitter.MirrorSize - characterPosition : characterPosition) - emitter.Origin;
            if (toPlayer.LengthSquared < 1e-6f)
                return;

            // falling bullets are pulled towards where the player is when the ring fires, and that never changes
            if (emitter.Falling)
                emitter.GravityDirection = Vector2.Normalize(toPlayer);

            if (!emitter.AimAtPlayer)
                return;

            Vector2 first = emitter.Directions[0];
            float delta = MathF.Atan2(toPlayer.Y, toPlayer.X) - MathF.Atan2(first.Y, first.X) + emitter.AimOffset;
            float cos = MathF.Cos(delta), sin = MathF.Sin(delta);

            for (int i = 0; i < emitter.Count; i++)
            {
                Vector2 d = emitter.Directions[i];
                emitter.Directions[i] = new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
            }
        }

        /// <summary>
        /// A ring that is made out of other bullets (see <see cref="DeriveKind"/>) fires: its bullets get their places and directions from what is there at this moment.
        /// Bullets that have nothing to be made from are hidden (the mask is set again every time, so a rewind and a replay give the same).
        /// </summary>
        private void derive(BulletEmitter emitter)
        {
            ulong hidden = 0;
            Vector2 centre = fieldCentre;

            switch (emitter.Derive)
            {
                case DeriveKind.Explode:
                {
                    var source = emitter.Source!;
                    double sourceTime = source.WithoutFrozenTime(effects.Effective(source, emitter.Time));

                    // (a pulled ring is simulated: make sure it has got as far as this moment)
                    if (source.Homing)
                        source.StepHoming(sourceTime, characterPosition, false);

                    float cos = MathF.Cos(emitter.FanOffset), sin = MathF.Sin(emitter.FanOffset);

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        ulong bit = 1UL << b;

                        if (b >= source.Count || ((source.ConsumedMask | source.AbsorbedMask | source.HiddenMask) & bit) != 0 || (emitter.BurstsEverySecond && source.LeftAlone(b)))
                        {
                            hidden |= bit;
                            continue;
                        }

                        source.Evaluate(b, sourceTime, out Vector2 position, out Vector2 direction, out bool alive, out _);

                        if (!alive || (hasScreenBounds && !isOnScreen(position, emitter.VisualRadius)))
                        {
                            hidden |= bit;
                            continue;
                        }

                        emitter.Origins![b] = position;
                        emitter.Directions[b] = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
                    }

                    break;
                }

                case DeriveKind.Echo:
                {
                    // some bullets of the source ring are thrown out again from the boss, away from the middle of the playfield (as seen from where they were fired)
                    var source = emitter.Source!;
                    var indices = emitter.EchoIndices!;

                    for (int i = 0; i < emitter.Count; i++)
                    {
                        int b = indices[i];
                        float scale = source.RadialScales == null ? 1f : source.RadialScales[b];
                        Vector2 spawn = (source.Origins != null ? source.Origins[b] : source.Origin) + source.Directions[b] * (source.SpawnRadius * scale);
                        Vector2 away = spawn - centre;

                        emitter.Origins![i] = bossPositionOf(emitter.BossRank);
                        emitter.Directions[i] = away.LengthSquared > 1e-4f ? Vector2.Normalize(away) : source.Directions[b];
                    }

                    break;
                }

                case DeriveKind.Held:
                {
                    // the knives that hung round the boss are mirrored through the centre: they are now round the player's side and fly at the player
                    Vector2 boss = bossPositionOf(emitter.BossRank);

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        Vector2 offset = emitter.HeldOffsets![b];
                        emitter.Origins![b] = centre * 2f - boss - offset * DanmakusuBalance.SAKUYA_RELEASE_DISTANCE_FACTOR; // (placed further from the player than they hung from her)
                        emitter.Directions[b] = offset.LengthSquared > 1e-6f ? Vector2.Normalize(offset) : new Vector2(1, 0);
                    }

                    break;
                }

                case DeriveKind.Dissipate:
                {
                    // Letty's beam dissipates: the slice that was fired at SliceTime has flown on for as long as it lives; its bullets leave from where it is, against the way the slider goes
                    var laser = emitter.SourceLaser!;

                    int ring = DanmakusuBalance.LETTY_RING_COUNT;

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        int beam = b / ring, place = b % ring;

                        if (beam >= laser.BeamCount)
                        {
                            hidden |= 1UL << b;
                            continue;
                        }

                        Vector2 position = laser.SlicePosition(emitter.SliceTime, emitter.Time, beam);

                        if (hasScreenBounds && !isOnScreen(position, emitter.VisualRadius))
                        {
                            hidden |= 1UL << b;
                            continue;
                        }

                        // (the bullets of a slice are spread evenly over a half circle, round the way back along the slider's path)
                        Vector2 course = laser.PathDirection(emitter.SliceTime, beam);
                        Vector2 back = course.LengthSquared > 1e-6f ? -course : -laser.Directions[beam];
                        float turn = (place + 0.5f) / ring * MathF.PI - MathF.PI / 2f;
                        float cosTurn = MathF.Cos(turn), sinTurn = MathF.Sin(turn);

                        emitter.Origins![b] = position;
                        emitter.Directions[b] = new Vector2(back.X * cosTurn - back.Y * sinTurn, back.X * sinTurn + back.Y * cosTurn);
                    }

                    break;
                }

                case DeriveKind.Train:
                {
                    // (aimed once, when it comes: the rings are started over now and then - the window resized, the player died - and a train aimed again then jumped, turned or was gone, as the player had moved
                    // meanwhile; only time going back before it came forgets the aim, see rewindTo)
                    if (trainAims.TryGetValue(emitter, out var aimed))
                    {
                        emitter.Origins![0] = aimed.Origin;
                        emitter.Directions[0] = aimed.Direction;
                        break;
                    }

                    // from the far side of the screen (the other side of the centre from the player) at the player
                    Vector2 toCentre = centre - characterPosition;
                    Vector2 away = toCentre.LengthSquared > 1f ? Vector2.Normalize(toCentre) : new Vector2(1, 0);
                    float reach = (hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 1000f) + emitter.HitRadius * 2f;

                    emitter.Origins![0] = characterPosition + away * reach;
                    emitter.Directions[0] = -away;
                    trainAims[emitter] = (emitter.Origins[0], emitter.Directions[0]);
                    break;
                }

                case DeriveKind.MoonEdges:
                {
                    bool placed = false;

                    foreach (var act in halfMoonActs)
                    {
                        if (act.Edges(emitter.Time, emitter.Piece, emitter.Origins!, emitter.Directions))
                        {
                            placed = true;
                            break;
                        }
                    }

                    // (no moon was placed: after a seek past it, say)
                    if (!placed)
                        hidden = emitter.Count >= 64 ? ulong.MaxValue : (1UL << emitter.Count) - 1;

                    break;
                }
            }

            emitter.HiddenMask = hidden;

            // (falling bullets are pulled towards where the player is when the ring fires)
            if (emitter.Falling && emitter.Count > 0)
            {
                Vector2 toPlayer = characterPosition - emitter.Origins![0];

                if (toPlayer.LengthSquared > 1e-6f)
                    emitter.GravityDirection = Vector2.Normalize(toPlayer);
            }
        }

        /// <summary>
        /// Chen in her section: from the start of every attack she flies straight (at the player's place at that moment) at a multiple of the bullet speed, and keeps
        /// bouncing off the border of the screen until it ends; in the pause she stands still where she stopped. False before her first attack (she stands where bosses stand).
        /// </summary>
        private bool chenPosition(BossPeriod period, double now, out Vector2 position)
        {
            position = default;
            ChenThrow? current = null;

            foreach (var thrown in chenThrows)
            {
                if (thrown.Period != period)
                    continue;

                if (now < thrown.Start)
                {
                    thrown.Known = false; // (time went back, or it is not time yet)
                    continue;
                }

                current = thrown;
            }

            if (current == null)
                return false;

            if (!current.Known)
            {
                Vector2 chenAt = bossPositionOf(period.Rank);
                Vector2 toPlayer = characterPosition - chenAt;
                current.From = chenAt;
                current.Direction = toPlayer.LengthSquared > 1e-4f ? Vector2.Normalize(toPlayer) : new Vector2(1, 0);
                current.CaughtAt = now;
                current.Known = true;
            }

            double flown = Math.Max(Math.Min(now, current.End) - current.CaughtAt, 0);
            Vector2 unfolded = current.From + current.Direction * (float)(current.Speed * flown);
            Vector2 low = bounceArea.Min + new Vector2(DanmakusuBalance.CHEN_WALL_MARGIN);
            Vector2 high = bounceArea.Max - new Vector2(DanmakusuBalance.CHEN_WALL_MARGIN);

            position = new Vector2(foldAxis(unfolded.X, low.X, high.X), foldAxis(unfolded.Y, low.Y, high.Y));
            return true;
        }

        /// <summary>A coordinate that has flown on in a straight line, folded back into [low, high] as if it bounced off both ends.</summary>
        private static float foldAxis(float value, float low, float high)
        {
            float size = high - low;

            if (size <= 1f)
                return (low + high) / 2f;

            float m = (value - low) % (2f * size);

            if (m < 0f)
                m += 2f * size;

            return low + (m <= size ? m : 2f * size - m);
        }

        /// <summary>
        /// The beams of a laser are spread evenly around a circle, the first one aimed at the player, at the moment the slider
        /// starts (like finish rings). The directions then stay for the whole slider.
        /// </summary>
        private void aimLasers(double now)
        {
            while (nextLaserAim < lasers.Length && lasers[nextLaserAim].StartTime <= now)
            {
                var laser = lasers[nextLaserAim++];

                if (laser.FollowsBoss)
                    laser.Origin = bossPositionOf(laser.BossRank) + laser.BossOffset;

                // (osu!catch: a stream's beams come out of a wall of the screen, where the window has it now)
                if (laser.WallSide != 0)
                {
                    float wall = laser.WallSide > 0 ? bounceArea.Max.Y : bounceArea.Min.Y;

                    for (int p = 0; p < laser.BallPositions.Length; p++)
                        laser.BallPositions[p] = new Vector2(laser.BallPositions[p].X, wall);

                    laser.Origin = new Vector2(laser.Origin.X, wall);
                }

                Vector2 toPlayer = characterPosition - laser.Origin;

                if (toPlayer.LengthSquared < 1e-6f)
                    toPlayer = new Vector2(1, 0);

                float first = (laser.AimOutward && laser.BossOffset.LengthSquared > 1e-6f ? MathF.Atan2(laser.BossOffset.Y, laser.BossOffset.X) : MathF.Atan2(toPlayer.Y, toPlayer.X)) + laser.AimOffset;

                if (laser.Rotates)
                    laser.StartRotation(now, first);

                // (osu!catch: the beams are spread evenly inside the fan, each in the middle of its share of it - none along the wall - and the fan is turned towards the player as far as it can turn and keep
                // them all in it: one beam alone points at the player, if the player is in the fan)
                float fanTurn = 0f;

                if (laser.Fan > 0f && !laser.Rotates)
                {
                    float share = laser.Fan / laser.BeamCount; // (each beam's share of the fan: turned by more than half of it, the outermost would leave the fan)
                    fanTurn = Math.Clamp(MathF.IEEERemainder(MathF.Atan2(toPlayer.Y, toPlayer.X) - laser.FanCentre, MathF.Tau), -share / 2f, share / 2f);
                }

                for (int b = 0; b < laser.BeamCount; b++)
                {
                    float angle = laser.Fan > 0f && !laser.Rotates
                        ? laser.FanCentre + fanTurn + laser.Fan * ((b + 0.5f) / laser.BeamCount - 0.5f)
                        : first + b * MathF.Tau / laser.BeamCount;

                    laser.Directions[b] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }
            }
        }

        /// <summary>
        /// The beams of a spinner turn towards the player: the beam that is angularly closest to the player turns the short way towards
        /// them, at the laser's angular speed (without overshooting), so the direction (clockwise or counter-clockwise) depends on where
        /// the player is. The angle is logged, because every slice keeps the direction it was fired in.
        /// </summary>
        private void steerLasers(double now)
        {
            double dt = Math.Clamp(now - previousTime, 0, 100);

            foreach (int index in rotatingLasers)
            {
                if (index >= nextLaserAim)
                    break; // not started yet (they are in time order)

                var laser = lasers[index];

                if (now < laser.StartTime || now > laser.EndTime)
                    continue;

                if (laser.FollowsBoss && !laser.KeepsOrigin)
                    laser.Origin = bossPositionOf(laser.BossRank) + laser.BossOffset; // (logged with the angle: every slice keeps the origin it was fired from)

                Vector2 toPlayer = characterPosition - laser.Origin;
                float angle = laser.CurrentAngle;

                // (Byakuren's spinner beams just spin, whichever way the player is)
                if (laser.Spins)
                {
                    angle += laser.AngularSpeed * (float)dt;

                    // (Marisa's Master Spark: it goes the one way it was aimed while it grows; once it has grown it turns slowly towards the player, always the same way round - the way that was nearer
                    // right then - and never back; it rests while the player is in it)
                    if (now >= laser.TurnsFrom && toPlayer.LengthSquared > 1e-6f)
                    {
                        float target = MathF.Atan2(toPlayer.Y, toPlayer.X);
                        float inside = MathF.Atan2(laser.HalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR, Math.Max(toPlayer.Length, 1f)); // (how far round from a beam's middle the player is still in it)
                        float nearest = float.MaxValue, ahead = float.MaxValue;

                        for (int b = 0; b < laser.BeamCount; b++)
                        {
                            float delta = target - (angle + b * MathF.Tau / laser.BeamCount);
                            delta -= MathF.Tau * MathF.Round(delta / MathF.Tau); // -pi..pi

                            if (Math.Abs(delta) < Math.Abs(nearest))
                                nearest = delta;
                        }

                        if (laser.TurnWay == 0)
                            laser.TurnWay = nearest >= 0f ? 1 : -1;

                        // (how far round the beam that gets there first has to turn, the way it turns: a player behind it is reached by going on round)
                        for (int b = 0; b < laser.BeamCount; b++)
                        {
                            float delta = target - (angle + b * MathF.Tau / laser.BeamCount);
                            float way = laser.TurnWay * (delta - MathF.Tau * MathF.Round(delta / MathF.Tau));
                            ahead = Math.Min(ahead, way < 0f ? way + MathF.Tau : way);
                        }

                        if (Math.Abs(nearest) > inside)
                            angle += laser.TurnWay * Math.Min(ahead, laser.TurnSpeed * (float)dt);
                    }
                }
                else if (toPlayer.LengthSquared > 1e-6f)
                {
                    float target = MathF.Atan2(toPlayer.Y, toPlayer.X);
                    float best = float.MaxValue;

                    for (int b = 0; b < laser.BeamCount; b++)
                    {
                        float delta = target - (angle + b * MathF.Tau / laser.BeamCount);
                        delta -= MathF.Tau * MathF.Round(delta / MathF.Tau); // -pi..pi

                        if (Math.Abs(delta) < Math.Abs(best))
                            best = delta;
                    }

                    float step = laser.AngularSpeed * (float)dt;
                    angle += Math.Clamp(best, -step, step);
                }

                laser.SetAngle(now, angle);

                for (int b = 0; b < laser.BeamCount; b++)
                {
                    float a = angle + b * MathF.Tau / laser.BeamCount;
                    laser.Directions[b] = new Vector2(MathF.Cos(a), MathF.Sin(a));
                }
            }
        }

        /// <summary>
        /// The big bullets of Ringo turn the bullets they touch: a bullet that a deflector's path (this frame) came within reach of flies on in the direction of the deflector
        /// from where it was touched, once (see <see cref="BulletEmitter.Deflections"/>).
        /// </summary>
        private void updateDeflections(double now)
        {
            if (!hasDeflectors || now <= previousTime)
                return;

            float diagonal = hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 1500f;

            for (int k = 0; k < activeCount; k++)
            {
                var deflector = emitters[active[k]];

                if (!deflector.Deflector || deflector.Retired || deflector.Count == 0)
                    continue;

                double deflectorNow = effects.Effective(deflector, now);
                double deflectorBefore = Math.Max(effects.Effective(deflector, previousTime), deflector.Time);

                if (deflectorNow <= deflector.Time)
                    continue;

                for (int d = 0; d < deflector.Count; d++)
                {
                    ulong dBit = 1UL << d;

                    if (((deflector.ConsumedMask | deflector.AbsorbedMask | deflector.HiddenMask) & dBit) != 0)
                        continue;

                    deflector.Evaluate(d, deflectorBefore, out Vector2 from, out _, out _);
                    deflector.Evaluate(d, deflectorNow, out Vector2 to, out Vector2 heading, out bool deflectorAlive);

                    if (!deflectorAlive)
                        continue;

                    float travelled = Vector2.Distance(from, to);

                    for (int j = 0; j < activeCount; j++)
                    {
                        var other = emitters[active[j]];

                        if (ReferenceEquals(other, deflector) || other.Deflector || other.Retired || other.Count == 0 || other.Falling || other.Homing || other.Ricochet || other.Ops != null)
                            continue;

                        double otherNow = other.WithoutFrozenTime(effects.Effective(other, now));

                        if (otherNow < other.Time || otherNow - other.Time > other.Lifetime)
                            continue;

                        float reach = deflector.HitRadius + other.HitRadius;

                        // (the ring is a circle of bullets that grows round its centre: the deflector must be in the band between its slowest and its fastest bullet)
                        if (other.IsRadial)
                        {
                            float distance = Vector2.Distance(other.Origin, to);

                            if (distance < other.SlowestRadiusAt(otherNow) - reach - travelled || distance > other.RadiusAt(otherNow) + reach + travelled)
                                continue;
                        }

                        for (int b = 0; b < other.Count; b++)
                        {
                            ulong bit = 1UL << b;

                            if (((other.ConsumedMask | other.AbsorbedMask | other.HiddenMask) & bit) != 0)
                                continue;

                            if (other.Deflections != null && other.Deflections.ContainsKey(b))
                                continue; // (once)

                            other.Evaluate(b, otherNow, out Vector2 position, out _, out bool alive);

                            if (!alive || distanceToSegment(position, from, to) > reach)
                                continue;

                            double longer = (otherNow - other.Time) + (diagonal + other.VisualRadius * 4f) / Math.Max(other.Speed * other.MinRadialScale, 1e-4f);
                            other.Deflect(b, otherNow, position, heading, longer);
                            deflectLog.Add((other, b, now, null));
                        }
                    }
                }
            }
        }

        /// <summary>Is this place on the screen, give or take <paramref name="margin"/>?</summary>
        private bool onTheScreen(Vector2 place, float margin)
            => place.X >= screenMin.X - margin && place.X <= screenMax.X + margin && place.Y >= screenMin.Y - margin && place.Y <= screenMax.Y + margin;

        /// <summary>
        /// A bullet that is outside the screen altogether: has it left for good? Nothing brings it back in then, so it comes in from nowhere. What goes round by itself (Daiyousei's) is not gone, nor what bounces at the wall, nor
        /// what has not been on the screen yet (a train), nor the knives that hang round Sakuya. What is taken at a wall (portals, what falls back or comes back) has been taken by then: the bullet is out of the screen altogether only later.
        /// </summary>
        private bool leftForGood(BulletEmitter emitter, int bullet, float margin)
            => !failed && !emitter.Ricochet && !emitter.WrapsOnce && !emitter.Homing && emitter.Derive is not (DeriveKind.Held or DeriveKind.Train) && emitter.Shape != BulletShape.Train
               && onTheScreen(emitter.Origins != null ? emitter.Origins[bullet] : emitter.Origin, margin);

        /// <summary>Arrowhead bullets are pulled towards the player: move them on to <paramref name="now"/>.</summary>
        private void stepHoming(double now, bool sweep)
        {
            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];

                double effectiveNow = emitter.WithoutFrozenTime(effects.Effective(emitter, now));

                if (emitter.Homing && !emitter.Retired && effectiveNow <= emitter.Time + emitter.Lifetime && !emitter.HomingFinished && !emitter.RemovedAt(now))
                    emitter.StepHoming(effectiveNow, characterPosition, sweep);
            }
        }

        /// <summary>Time moved backwards (replay seek): give back bullets and health that were taken after <paramref name="time"/>.</summary>
        private void rewindTo(double time)
        {
            moonSlices.Clear(); // (the moon goes where the player is when it appears: after a seek it may go elsewhere)
            borrowedBullets.Clear(); // (the copies of Daiyousei's bullets are made again)

            // (a trap that caught the character after this moment has not caught it yet)
            if (forcedFreezeFrom > time)
            {
                forcedFreezeFrom = double.PositiveInfinity;
                forcedFreezeUntil = double.NegativeInfinity;
            }

            foreach (var emitter in emitters)
            {
                dropSnapshot(emitter);
                emitter.ForgetOpsGone(); // (a ring that aims at the player may be aimed elsewhere this time)
            }

            portalBridge?.Forget(); // (the portals may go another way this time)

            while (nextAim > 0 && emitters[aimEmitters[nextAim - 1]].Time > time)
                nextAim--;

            while (nextSwirl > 0 && emitters[swirlRings[nextSwirl - 1]].Time > time)
                nextSwirl--;

            while (nextLaserAim > 0 && lasers[nextLaserAim - 1].StartTime > time)
                nextLaserAim--;

            while (hits.Count > 0 && hits[^1].Time > time)
            {
                var hit = hits[^1];
                hits.RemoveAt(hits.Count - 1);

                if (hit.Emitter >= 0)
                    emitters[hit.Emitter].ConsumedMask &= ~(1UL << hit.Bullet);
                else if (hit.Emitter == stage_hit)
                    stageConsumed.Remove(hit.Bullet);

                if (hit.Damage > 0 && healthProcessor != null && !healthProcessor.HasFailed)
                {
                    healthProcessor.Health.Value += hit.Damage;

                    if ((hit.Emitter != slider_hit || DanmakusuBalance.SLIDER_DAMAGE_COUNTS_AS_MISS) && scoreProcessor is DanmakusuScoreProcessor danmakusuScore)
                        danmakusuScore.RemoveMiss();
                }
            }

            // the bullets that a deflector turned after this moment have not been turned yet
            while (deflectLog.Count > 0 && deflectLog[^1].Time > time)
            {
                var (turnedRing, turnedBullet, _, before) = deflectLog[^1];
                deflectLog.RemoveAt(deflectLog.Count - 1);
                turnedRing.Undeflect(turnedBullet, before);
            }

            foreach (var laser in lasers)
            {
                laser.ResetFinished();

                if (laser.Rotates)
                    laser.TruncateRotation(time);
            }

            // (a train that has not come yet at this moment is aimed again when it comes)
            foreach (var emitter in emitters)
            {
                if (emitter.Derive == DeriveKind.Train && emitter.Time > time)
                    trainAims.Remove(emitter);
            }

            // pulled bullets depend on where the player was, which is not known any more: they start over from when they fired
            foreach (var emitter in emitters)
            {
                emitter.ResetMoon(); // (the bullets have not flown into the moon yet)

                if (emitter.Homing)
                    emitter.ResetHoming();
            }

            resetRings();
            rewindStage(time);

            nextSliderDamageTime = double.NegativeInfinity;
            lastMissSoundTime = double.NegativeInfinity;

            for (int i = hits.Count - 1; i >= 0; i--)
            {
                if (hits[i].Damage > 0 && (hits[i].Emitter != slider_hit || DanmakusuBalance.SLIDER_DAMAGE_COUNTS_AS_MISS))
                {
                    lastMissSoundTime = hits[i].Time;
                    break;
                }
            }

            for (int i = hits.Count - 1; i >= 0; i--)
            {
                if (hits[i].Emitter == slider_hit)
                {
                    nextSliderDamageTime = hits[i].Time + DanmakusuBalance.SLIDER_DAMAGE_INTERVAL_MS;
                    break;
                }
            }

            previousTime = time;
        }

        /// <summary>The real screen (not just the playfield) in this field's coordinates; bullets outside it are skipped when drawing.</summary>
        private void updateScreenBounds()
        {
            inputManager ??= GetContainingInputManager();

            if (inputManager == null && ScreenBounds == null)
            {
                hasScreenBounds = false;
                return;
            }

            var quad = ScreenBounds != null ? ScreenBounds.ScreenSpaceDrawQuad : inputManager!.ScreenSpaceDrawQuad;
            // (in the space of the field's parent: the field's Position is only the shake, and the screen does not shake with it. Taken as the field's own space plus the shake, it moved whenever the shake was
            // not yet in what ToLocalSpace knew - every bounce of the shake looked like a resized window, and every ring was started over, the trains aimed again at the player: several times a frame)
            Vector2 a = unturn(Parent != null ? Parent.ToLocalSpace(quad.TopLeft) : ToLocalSpace(quad.TopLeft));
            Vector2 b = unturn(Parent != null ? Parent.ToLocalSpace(quad.BottomRight) : ToLocalSpace(quad.BottomRight));

            Vector2 newMin = Vector2.ComponentMin(a, b);
            Vector2 newMax = Vector2.ComponentMax(a, b);

            // the window was resized: beams that were off screen before might be on screen now
            if (hasScreenBounds && (Vector2.Distance(newMin, screenMin) > 1f || Vector2.Distance(newMax, screenMax) > 1f))
            {
                if (TrainProbeOn && BoundsProbe.Count < 8)
                    BoundsProbe.Add($"bounds moved at {Time.Current:0}: min {screenMin} -> {newMin}, max {screenMax} -> {newMax}, field position {Position}, mirror {mirrorRadians:0.000}");

                foreach (var laser in lasers)
                    laser.ResetFinished();

                screenMin = newMin;
                screenMax = newMax;

                resetRings();
                spawnUpTo(failed ? failTime : previousTime);
            }

            screenMin = newMin;
            screenMax = newMax;
            hasScreenBounds = true;

            // a beam must last until its slices have left the whole screen (not just the osu! playfield, which is smaller): from anywhere on the screen a slice needs at most the screen's diagonal (plus the beam's
            // width) to get out. (Slider beams too, since 2026-11-08: one that had flown into the side of the screen beyond the playfield vanished there all at once, as its time was up; a beam that cannot reach the
            // screen any more is let go before that anyway, see MarkFinished)
            float diagonal = Vector2.Distance(newMin, newMax);

            foreach (var laser in lasers)
            {
                if (laser.Speed <= 0f)
                    continue;

                double needed = Math.Min((diagonal + laser.MaxHalfWidth * 2f) / laser.Speed, laser.MostLifetime); // (never past 64 beats)

                if (needed > laser.Lifetime)
                {
                    laser.Lifetime = needed;
                    maxLaserSpan = Math.Max(maxLaserSpan, laser.Span);
                }
            }

            bounceArea.Min = newMin;
            bounceArea.Max = newMax;

            // (what is mirrored through the centre, or sent to it, uses the centre of the screen)
            Vector2 middle = (newMin + newMax) / 2f;

            foreach (var op in bulletOps)
            {
                if (op.Kind is TrajectoryOpKind.Mirror or TrajectoryOpKind.Redirect or TrajectoryOpKind.Outward || op.FacesPoint)
                {
                    // (Eiki's and Yumemi's acts say where theirs go; the middle only until they have)
                    if (!op.PointKept || op.Point == default)
                        op.Point = middle;
                }
                else if (op.Kind == TrajectoryOpKind.Glass)
                {
                    op.GlassMin = newMin;
                    op.GlassMax = newMax;
                }
            }

            // bouncing bullets stay inside the visible screen
            if (ricochetArea != null)
            {
                ricochetArea.Min = newMin;
                ricochetArea.Max = newMax;
            }
        }

        private void updateRenderer(double now)
        {
            updateScreenBounds();
            profile(14);

            // (while osu! runs several steps of the game in one frame to catch up with the music, only the last one is ever seen: the picture is not made for the others. Making it for every step made
            // a crowded moment cost more than a step's 16.7 ms, the game fell further and further behind the music, and osu! locked its clock half a second behind it)
            if (!failed && !Preview && drawableRuleset?.FrameStableClock.IsCatchingUp.Value == true)
                return;

            // after death the bullets are drawn as they were at that moment, plus the fall since then.
            double drawTime = failed ? failTime : now;
            float fall = failed ? (float)((Stopwatch.GetTimestamp() - failTimestamp) * 1000.0 / Stopwatch.Frequency) : 0f;

            // 1) retire rings that are gone for good (not while dead: then everything falls and may come back). Whether a ring is gone is worked out for every ring on its own, so the rings are shared
            // out between the cores; then, one after the other, the gone ones are retired
            if (!failed)
            {
                if (goneFlags.Length < activeCount)
                    goneFlags = new bool[Math.Max(activeCount, goneFlags.Length * 2)];

                if (activeCount >= parallel_min_rings)
                    System.Threading.Tasks.Parallel.For(0, activeCount, parallelOptions, k => goneFlags[k] = ringGone(emitters[active[k]], drawTime));
                else
                {
                    for (int k = 0; k < activeCount; k++)
                        goneFlags[k] = ringGone(emitters[active[k]], drawTime);
                }

                int kept = 0;

                for (int k = 0; k < activeCount; k++)
                {
                    int index = active[k];

                    if (goneFlags[k])
                    {
                        emitters[index].Retired = true;
                        dropSnapshot(emitters[index]);
                        continue;
                    }

                    active[kept++] = index;
                }

                activeCount = kept;
            }

            if (ProfileTicks != null)
            {
                ProfileRings += activeCount;

                for (int k = 0; k < activeCount; k++)
                    ProfileBullets += emitters[active[k]].Count;
            }

            // 2) slowest first (nearly sorted already, so this is cheap); equal speeds keep their time order, later on top
            sortActive();
            profile(15);

            // 3) the bullets of the live rings
            int count = 0;
            float redLevel = failed ? 0f : remiliaLevel(drawTime); // (how far Remilia's attack has got: the bullets are that red, and glow that much)
            float autumnLevel = failed || autumnWindows.Count == 0 ? 0f : attackLevel(autumnWindows.ToArray(), drawTime);
            float turnLevel = failed || reverseMarks.Count == 0 ? 0f : reverseLevel(drawTime) * reverseFlash(drawTime);
            double shionMark = turnLevel > 0f ? warnedReverse(drawTime) : double.NaN;
            shionArrows.Clear();
            float tideLevel = failed || tideWindows.Count == 0 ? 0f : attackPresence(tideWindows, drawTime);
            float verdantLevel = failed || verdantWindows.Count == 0 ? 0f : attackPresence(verdantWindows, drawTime);
            float bloomLevel = (failed ? redLevel : Math.Max(redLevel, attackLevel(stampedeWindows, drawTime))) * DanmakusuBalance.REMILIA_BLOOM; // (Saki's attacks glow too, without the red)
            float verdantBloom = verdantLevel * DanmakusuBalance.DAIYOUSEI_BLOOM; // (Daiyousei's green ones as well: not what has lost her buff)

            // Koishi: bullets vanish near the player, and everything vanishes (with a glitch) again and again during her attacks
            bool koishiActive = !failed && koishiMarks.Length + koishiPeriods > 0 && koishiOn(drawTime);
            koishiHideAll = false;
            koishiFlicker = 0f;

            if (koishiActive)
                koishiGlitch(drawTime, out koishiHideAll, out koishiFlicker);

            int flickerBucket = (int)(drawTime / DanmakusuBalance.KOISHI_FLICKER_STEP_MS);

            // Reisen: what she has frozen is see-through (and shows where it will slide to)
            float reisenSlide = 0f;
            float reisenAlpha = failed || reisenWindows.Length == 0 ? 0f : reisenLevel(drawTime, false, out reisenSlide);
            float reisenShift = reisenDistance(circleRadius);

            // Sakuya: what her time stop holds is gray, and a green copy shows where the mirroring at the end of it will put it, if that is near the player (nearSakuyaPreview)
            float sakuyaAlpha = 0f;
            double sakuyaFrom = 0;

            if (!failed && sakuyaStops.Length > 0)
            {
                sakuyaAlpha = sakuyaLevel(drawTime, false, out _);

                if (sakuyaAlpha > 0f)
                {
                    foreach (var (stopStart, stopEnd) in sakuyaStops)
                    {
                        if (drawTime >= stopStart && drawTime <= stopEnd)
                            sakuyaFrom = stopStart;
                    }
                }

                if (sakuyaFrom <= 0)
                    sakuyaAlpha = 0f;
            }

            Vector2 sakuyaCentre = (bounceArea.Min + bounceArea.Max) / 2f;
            sharpOrbs.Clear();
            beginFlyBys(drawTime);

            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];
                double ringTime = emitter.WithoutFrozenTime(effects.Effective(emitter, drawTime));
                double age = ringTime - emitter.Time;

                if (age < 0 || age > emitter.Lifetime || emitter.RemovedAt(drawTime))
                    continue;

                int style = styleOf(emitter);
                float margin = emitter.VisualRadius * 1.8f; // (knives and carriages are drawn longer than the bullet is wide)
                BulletLook? frozenLook = emitter.FreezeLookAt(drawTime);
                bool train = emitter.Shape == BulletShape.Train; // (Yukari's: the other bosses do not hide it, see BulletInstance.Immune)

                // (what the collision check of this frame worked out for a ring with ops is drawn as it is)
                var worked = !failed && emitter.Seen is RingSnapshot snapshot && snapshot.Time == ringTime ? snapshot : null;

                for (int b = 0; b < emitter.Count; b++)
                {
                    ulong bit = 1UL << b;

                    if (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && (emitter.ConsumedMask & bit) != 0)
                        continue;

                    if (((emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                        continue; // (gone into the moon, or never there)

                    Vector2 position, direction;
                    bool alive;
                    BulletLook? look;

                    if (worked != null && (worked.Known & bit) != 0)
                    {
                        position = worked.Position[b];
                        direction = worked.Direction[b];
                        alive = (worked.Alive & bit) != 0;
                        look = worked.Look[b];
                    }
                    else
                        emitter.Evaluate(b, ringTime, out position, out direction, out alive, out look);

                    if (!alive)
                        continue;

                    look ??= frozenLook;

                    // (Yukari's talismans face the middle of the screen while they stand still: a pulled or bouncing one too, whose freeze is not traced and which kept the way it flew)
                    if (frozenLook != null && ReferenceEquals(look, frozenLook) && frozenLook.Shape == BulletShape.Talisman)
                        direction = facingMiddle(position);

                    if (failed)
                    {
                        // free fall: constant gravity downwards on top of the velocity the bullet had (if it keeps it).
                        Vector2 velocity = DanmakusuBalance.DEATH_BULLETS_KEEP_VELOCITY ? emitter.VelocityAt(b, ringTime, direction) : Vector2.Zero;
                        position += velocity * fall + new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);

                        Vector2 current = velocity + new Vector2(0, DanmakusuBalance.DEATH_GRAVITY * fall);
                        if (current.LengthSquared > 1e-9f)
                            direction = Vector2.Normalize(current);
                    }

                    // what is frozen shakes a little before it is let go (Cirno's bullets, Zanmu's go through the act)
                    if (!failed && look != null && look.ShakeFrom > 0 && drawTime >= look.ShakeFrom && drawTime < look.ShakeTo)
                        position += shakeOffset(active[k], b, drawTime);
                    else if (sukunaShaking && !failed)
                        position += shakeOffset(active[k], b, drawTime); // (Sukuna's warning: everything shakes)

                    // a bullet that is out of the screen at the bottom grows a flower on Yuyuko's tree (also her own petals, the flowers of her spinner and the bullets that fell from the top as flowers; the petals that fly off the tree are not bullets of a ring, so they do not)
                    if (treeActs.Count > 0 && position.Y > stageMax.Y)
                    {
                        foreach (var tree in treeActs)
                            tree.Sow(active[k], b, drawTime);
                    }

                    // a bullet that is out of the screen at a side grows a flower on Lily White's branches (in her attacks)
                    if (lilyActs.Count > 0 && hasScreenBounds && (position.X < stageMin.X || position.X > stageMax.X || position.Y < stageMin.Y || position.Y > stageMax.Y))
                    {
                        foreach (var lily in lilyActs)
                            lily.Sow(this, active[k], b, drawTime, position);
                    }

                    if (hasScreenBounds && (position.X < screenMin.X - margin || position.X > screenMax.X + margin
                                            || position.Y < screenMin.Y - margin || position.Y > screenMax.Y + margin))
                    {
                        if (leftForGood(emitter, b, margin))
                            emitter.AbsorbedMask |= bit;

                        continue;
                    }

                    // (a fast bullet that flies past the player whooshes)
                    if (!failed)
                        flyBy((long)active[k] << 6 | (long)b, position, drawTime);

                    // (a ring that fades in when it is fired: Nemuno's round the player)
                    float fade = emitter.FadeInMs > 0 ? 1f - smoothStep(age / emitter.FadeInMs) : 0f;

                    if (emitter.FadesFrom > 0)
                        fade = Math.Max(fade, smoothStep((drawTime - emitter.FadesFrom) / DanmakusuBalance.LIFE_FADE_MS));

                    if (koishiActive && !train)
                    {
                        if (koishiHideAll)
                        {
                            // everything is invisible, except in the dim light round the player
                            float lit = Math.Clamp((Vector2.Distance(position, characterPosition) - circleRadius * DanmakusuBalance.KOISHI_REVEAL_NEAR) / Math.Max(circleRadius * (DanmakusuBalance.KOISHI_REVEAL_FAR - DanmakusuBalance.KOISHI_REVEAL_NEAR), 1f), 0f, 1f);
                            float seen = (1f - lit * lit * (3f - 2f * lit)) * DanmakusuBalance.KOISHI_REVEAL_VISIBILITY;

                            if (seen < 0.02f)
                                continue; // (the hitbox is still there: only the picture goes)

                            fade = 1f - seen;
                        }
                        else if (koishiFlicker > 0f && hash01(active[k], b, flickerBucket) < koishiFlicker)
                            continue;
                        else
                        {
                            float near = circleRadius * DanmakusuBalance.KOISHI_FADE_NEAR, far = circleRadius * DanmakusuBalance.KOISHI_FADE_FAR;
                            float u = Math.Clamp((Vector2.Distance(position, characterPosition) - near) / Math.Max(far - near, 1f), 0f, 1f);
                            fade = 1f - u * u * (3f - 2f * u); // (more and more invisible the closer to the player)

                            if (koishiFlicker > 0f)
                                position += new Vector2((hash01(b, flickerBucket, 7) - 0.5f) * 10f * koishiFlicker, (hash01(b, flickerBucket, 11) - 0.5f) * 4f * koishiFlicker);
                        }
                    }

                    // Reimu's pile: a bullet fades in where it lands and fades out before it is gone
                    if (hasSeal)
                    {
                        double leaving = (emitter.StuckGoneTime(b) - ringTime) / DanmakusuBalance.REIMU_PILE_FADE_MS;

                        if (leaving < 1.0)
                            fade = Math.Max(fade, 1f - smoothStep(leaving));

                        double landed = (ringTime - emitter.StuckSinceTime(b)) / DanmakusuBalance.REIMU_PILE_FADE_IN_MS; // (NaN when it does not lie still: false both ways)

                        if (landed >= 0.0 && landed < 1.0)
                            fade = Math.Max(fade, 1f - smoothStep(landed));
                    }

                    // in smoke a bullet is half see-through and twice as big
                    float smoke = stageSmokeCount > 0 ? smokeAt(position) : 0f;

                    if (smoke > 0f && !train)
                        fade = Math.Max(fade, DanmakusuBalance.SANNYO_SMOKE_TRANSPARENCY * smoke);

                    if (count == instanceBuffer.Length)
                        Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                    BulletShape drawShape = look?.Shape ?? emitter.ShapeOf(b);
                    bool ownLook = look != null && look.Overlay > 0;

                    // (one of Daiyousei's that has left the screen and come in again has lost her buff: its own size, not green, no glow of hers)
                    bool plain = emitter.WrapsOnce && emitter.LostVerdant(b, ringTime);
                    float ownRadius = plain ? emitter.VisualRadius / emitter.VerdantScale : emitter.VisualRadius;
                    int overlay = ownLook ? look!.Overlay : emitter.AltTint > 0 && b % 2 == 1 ? emitter.AltTint : emitter.Tint;
                    float overlayAmount = ownLook ? look!.OverlayAmount : emitter.TintAmount;

                    if (redLevel > 0f && overlay == 0)
                    {
                        overlay = BulletRenderer.TINT_RED;
                        overlayAmount = redLevel * DanmakusuBalance.REMILIA_BULLET_RED;
                    }

                    // (Daiyousei's attacks: everything is green, but what has lost her buff)
                    if (verdantLevel > 0f && overlay == 0 && !plain)
                    {
                        overlay = BulletRenderer.TINT_GREEN;
                        overlayAmount = verdantLevel;
                    }

                    // (Wakasagihime's attacks: everything is blue)
                    if (tideLevel > 0f && overlay == 0)
                    {
                        overlay = BulletRenderer.TINT_BLUE;
                        overlayAmount = tideLevel * DanmakusuBalance.WAKASAGI_TINT;
                    }

                    // (Shizuha's attacks: every second bullet turns orange, more and more)
                    if (autumnLevel > 0f && overlay == 0 && b % DanmakusuBalance.SHIZUHA_EVERY == 1)
                    {
                        overlay = BulletRenderer.TINT_ORANGE;
                        overlayAmount = autumnLevel;
                    }

                    if (look != null && reisenAlpha > 0f && look.Alpha < 1f)
                        fade = 1f - (1f - fade) * (1f - (1f - look.Alpha) * reisenAlpha);

                    // (what Toyohime has pulled is golden and glows)
                    bool golden = emitter.GoldMask != 0 && b < 64 && (emitter.GoldMask & (1UL << b)) != 0;

                    if (golden)
                    {
                        overlay = BulletRenderer.TINT_YELLOW;
                        overlayAmount = DanmakusuBalance.TOYOHIME_GOLD_TINT;
                    }

                    // (what Sakuya's mirroring will move gets a green copy where it will be put, below; the bullet itself keeps its own colour. What was fired while time stands still flies on, and is mirrored too: its copy flies with it)
                    bool sakuyaMarked = sakuyaAlpha > 0f && (emitter.Time < sakuyaFrom || emitter.Ops != null) && !(look?.Harmless ?? false);

                    // a big round bullet (Utsuho's, or any at least twice as big as a plain one) is drawn as Mokou's ball is, from a big picture (see drawStage), not by the renderer, under the bullets, so it
                    // stays sharp; Utsuho's grow from nothing to their size over a moment when they are shot (only the picture: the hitbox is whole from the start)
                    float drawnRadius = ownRadius * (emitter.KeepsSize ? 1f : (look?.Scale ?? 1f)) * emitter.BigScale(b);

                    if ((emitter.Sharp || drawnRadius >= sharpBulletRadius) && drawShape == BulletShape.Circle && fade < 0.5f && !sakuyaMarked)
                    {
                        float grown = emitter.Sharp ? smoothStep(age / DanmakusuBalance.UTSUHO_BALL_GROW_MS) : 1f;
                        Color4 body = overlay > 0 && overlayAmount >= 0.5f ? BulletRenderer.SpecialBodies[Math.Min(overlay, BulletRenderer.SpecialBodies.Length) - 1] : palette[style % palette.Length];
                        sharpOrbs.Add((position, drawnRadius * grown, body));
                        continue;
                    }

                    instanceBuffer[count++] = new BulletInstance
                    {
                        Position = position,
                        Radius = emitter.KeepsSize // (Utsuho's big balls and the trains: no other boss's mechanic changes their size)
                            ? emitter.VisualRadius
                            : Math.Max(leastBulletRadius, (drawShape == BulletShape.Flower ? fallFlowerRadius * emitter.FlowerScale : ownRadius * (look?.Scale ?? 1f)) * emitter.BigScale(b) * (stageRippleCount > 0 ? rippleGrow(position) : 1f) * (smoke > 0f ? smokeGrow(smoke) : 1f) * (sukunaLevel > 0f ? sukunaGrow(position) : 1f) * (verdantLevel > 0f && !emitter.HurtsLikeSlider && !plain ? 1f + (DanmakusuBalance.DAIYOUSEI_SIZE - 1f) * verdantLevel : 1f)),
                        Style = style,
                        Direction = drawShape is BulletShape.Star or BulletShape.SoftStar ? emitter.Orientation(b, ringTime + fall) : direction, // stars spin
                        Shape = drawShape,
                        Speed = emitter.Speed, // the ring's speed: the list is ordered by it
                        Overlay = overlay, // (a bullet that is frozen, bounced, turned red... shows that colour)
                        OverlayAmount = overlayAmount,
                        Shade = drawShape is BulletShape.Star or BulletShape.SoftStar && emitter.HasRicocheted(b, ringTime) ? 1f - (1f - emitter.Shade) * (1f - DanmakusuBalance.RICOCHET_SHADE) : emitter.Shade, // (a star that has bounced is darker)
                        Fade = fade,
                        Decal = look?.Decal ?? BulletDecal.None,
                        RedHandle = redHandles[active[k]],
                        Blades = drawShape == BulletShape.Knife ? emitter.Blades : 1,
                        BladeAngle = drawShape == BulletShape.Knife ? emitter.BladeAngle(b, ringTime + fall) : 0f,
                        Stretch = emitter.KnifeStretch,
                        Smile = koishiHideAll && !train,
                        Immune = train,
                        Unbuffed = plain,
                        Morph = look?.Morph ?? 1f,
                        Under = emitter.DrawShape ?? emitter.Shape,
                        Bloom = Math.Max(Math.Max(golden ? DanmakusuBalance.TOYOHIME_GOLD_BLOOM : emitter.Flashes ? flashBloom(ringTime - emitter.Time, emitter.RemoveAfter) : 0f, sukunaLevel > 0f ? sukunaGlowAt(position) : 0f), emitter.Bloom),
                    };

                    // (Shion's warning: beside every second bullet a small red arrow of Seija's dress, not see-through, flashes, pointing the way it will fly when it turns: back the way it came. Only beside what her turn will
                    // really turn round: not what she has turned already, nor what cannot be turned at all)
                    if (turnLevel > 0f && b % DanmakusuBalance.SHION_ARROW_EVERY == 0 && look is not { Overlay: BulletRenderer.TINT_DARK_BLUE } && willReverse(emitter, b, ringTime, drawTime, shionMark))
                    {
                        float arrow = emitter.VisualRadius * DanmakusuBalance.SHION_ARROW_SIZE;
                        Vector2 back = direction.LengthSquared > 1e-6f ? -direction.Normalized() : new Vector2(0f, 1f);

                        shionArrows.Add((position + back * (emitter.VisualRadius + arrow), arrow * 2.2f, MathF.Atan2(back.Y, back.X) * 180f / MathF.PI));
                    }

                    if (look != null && look.SlideSign != 0 && reisenAlpha > 0f)
                    {
                        // the preview: a red copy where the bullet will have slid to (where it was is not shown)
                        if (count == instanceBuffer.Length)
                            Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                        var ghost = instanceBuffer[count - 1];
                        float ahead = look.SlideSign * reisenShift * (1f - reisenSlide);

                        ghost.Decal = BulletDecal.None;
                        ghost.OverlayAmount = 1f;
                        ghost.Fade = 1f - DanmakusuBalance.REISEN_PREVIEW_VISIBLE * reisenAlpha;
                        ghost.Position += look.SlideVertical ? new Vector2(0f, ahead) : new Vector2(ahead, 0f);
                        ghost.Overlay = BulletRenderer.TINT_RED;
                        instanceBuffer[count++] = ghost;
                    }

                    if (sakuyaMarked && nearSakuyaPreview(sakuyaCentre * 2f - position))
                    {
                        if (count == instanceBuffer.Length)
                            Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                        var ghost = instanceBuffer[count - 1];
                        ghost.Position = sakuyaCentre * 2f - position;
                        ghost.Overlay = BulletRenderer.TINT_GREEN;
                        ghost.OverlayAmount = 1f;
                        ghost.Decal = BulletDecal.None;
                        ghost.Fade = 1f - DanmakusuBalance.SAKUYA_PREVIEW_VISIBLE * sakuyaAlpha;
                        instanceBuffer[count++] = ghost;
                    }
                }
            }

            profile(16);
            int mechanicsFrom = count;
            count = addHeldKnives(drawTime, count, sakuyaAlpha);
            stageSakuyaAlpha = sakuyaAlpha;
            count = addStreamStars(drawTime, fall, count);
            count = addStageBullets(drawTime, fall, count);
            endFlyBys();
            applyStageDims(drawTime, count);
            applyStageHides(count);

            foreach (var tree in treeActs)
                count = tree.AddPetals(this, drawTime, count);

            foreach (var lily in lilyActs)
                count = lily.AddPetals(this, drawTime, count);

            // Koishi hides the bullets of the mechanics (knives that hang round Sakuya, the stars of the moon, the bullets of the acts, the petals of the tree) the way she hides the bullets of the rings
            count = koishiHideMechanics(mechanicsFrom, count, koishiActive, flickerBucket);

            count = blurStopped(drawTime, count);
            count = softenBullets(count);

            if (TalismanProbeOn)
                probeTalismans(count);

            probeTrains(drawTime);

            impactBullets(drawTime, count); // (Hecatia's impact frame: black, white towards the middle)
            count = hasteBlur(drawTime, count); // (Gengetsu: the blur towards the middle)

            // (before an attack every bullet flashes: that is its warning)
            bloomLevel = Math.Max(bloomLevel, failed ? 0f : flashLevel(drawTime));

            // Remilia's attack: everything on the screen glows, more and more (and in Daiyousei's, everything that has her buff)
            if (bloomLevel > 0.002f || verdantBloom > 0.002f)
            {
                for (int i = 0; i < count; i++)
                    instanceBuffer[i].Bloom = Math.Max(instanceBuffer[i].Bloom, instanceBuffer[i].Unbuffed ? bloomLevel : Math.Max(bloomLevel, verdantBloom));
            }

            // the black hole bends the light: everything that is seen near it is pushed out of it and stretched into an arc round it, and what is close behind it is seen a second time on the other
            // side, faint, squeezed against its edge
            if (lensOn)
            {
                int seen = count;

                for (int i = 0; i < seen; i++)
                {
                    var b = instanceBuffer[i];
                    Vector2 d = b.Position - lensCentre;
                    float r = d.Length;

                    if (r < 1e-3f)
                        continue;

                    Vector2 outward = d / r;
                    Vector2 around = new Vector2(-outward.Y, outward.X);
                    float root = MathF.Sqrt(r * r + 4f * lensRadius * lensRadius);
                    float primary = lensDistance(r);

                    instanceBuffer[i].Position = lensCentre + outward * primary;
                    // (a stretch too small to be seen is not drawn: a bent bullet costs several pictures, and far from the hole nearly every bullet would be a hair stretched)
                    float stretch = Math.Min(primary / r, DanmakusuBalance.UTSUHO_LENS_MAX_STRETCH);

                    if (stretch >= DanmakusuBalance.UTSUHO_LENS_MIN_STRETCH)
                    {
                        instanceBuffer[i].LensAxis = around;
                        instanceBuffer[i].LensCentre = lensCentre;
                        instanceBuffer[i].LensStretch = stretch;
                    }

                    // (no second images while the hole comes or goes: they would pop up round a dark that is not there yet)
                    if (r > lensRadius * DanmakusuBalance.UTSUHO_LENS_SECOND_REACH || b.Fade > 0.9f || lensPresence < 0.5f)
                        continue;

                    // (the second image: on the other side, in a thin band that hugs the edge of the dark (the ring of light round it), the more stretched the closer to the hole; the lens is a tenth wider than the dark)
                    float secondary = (root - r) * 0.5f;
                    float edge = lensEdge;
                    float band = edge * (1.02f + 0.18f * Math.Clamp(secondary / lensRadius, 0f, 1f));

                    if (count == instanceBuffer.Length)
                        Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                    var image = b;
                    image.Position = lensCentre - outward * band;
                    image.LensAxis = around;
                    image.LensCentre = lensCentre;
                    image.LensStretch = Math.Clamp(secondary / r, 1.5f, DanmakusuBalance.UTSUHO_LENS_MAX_STRETCH);
                    image.Radius *= Math.Clamp(secondary / Math.Max(primary, 1f), 0.35f, 1f);
                    image.Fade = 1f - (1f - b.Fade) * DanmakusuBalance.UTSUHO_LENS_SECOND_ALPHA * (1f - r / (lensRadius * DanmakusuBalance.UTSUHO_LENS_SECOND_REACH)) * (lensPresence - 0.5f) * 2f;
                    instanceBuffer[count++] = image;
                }
            }

            ProfileInstances += count;
            renderer.SetInstances(instanceBuffer, count);
            profile(17);
            drawStage(drawTime);
            profile(18);
            updateMoonLayer(drawTime, fall);
            profile(19);
            updateLasers(drawTime, fall);
        }

        private static readonly Comparer<LaserInstance> laserSpeedComparer = Comparer<LaserInstance>.Create((a, b) =>
        {
            int c = a.Speed.CompareTo(b.Speed);
            return c != 0 ? c : a.Start.CompareTo(b.Start);
        });

        /// <summary>Puts the live rings in draw order: slowest first, ties by time. Insertion sort: the list is nearly sorted from frame to frame.</summary>
        private void sortActive()
        {
            for (int i = 1; i < activeCount; i++)
            {
                int value = active[i];
                float speed = emitters[value].Speed;
                int j = i - 1;

                while (j >= 0)
                {
                    float other = emitters[active[j]].Speed;

                    if (other < speed || (other == speed && active[j] < value))
                        break;

                    active[j + 1] = active[j];
                    j--;
                }

                active[j + 1] = value;
            }
        }

        /// <summary>Adds the rings whose time has come to the live list (emitters are sorted by time).</summary>
        private void spawnUpTo(double time)
        {
            while (nextSpawn < emitters.Length && emitters[nextSpawn].Time <= time)
            {
                // (osu!catch: a ring that comes out of a wall of the screen comes out of it where the window has it now)
                var coming = emitters[nextSpawn];

                if (coming.WallSide != 0)
                    coming.Origin = new Vector2(coming.Origin.X, coming.WallSide > 0 ? bounceArea.Max.Y : bounceArea.Min.Y);

                if (activeCount == active.Length)
                    Array.Resize(ref active, active.Length * 2);

                active[activeCount++] = nextSpawn++;
            }
        }

        /// <summary>Forgets which rings were retired / spawned; <see cref="spawnUpTo"/> builds the live list again.</summary>
        private void resetRings()
        {
            ringResets++;

            foreach (var emitter in emitters)
                emitter.Retired = false;

            activeCount = 0;
            nextSpawn = 0;
        }

        /// <summary>
        /// A ring only grows. When its radius is bigger than the distance from its centre to the farthest corner of the screen
        /// (plus the bullet size), none of its bullets can be on screen, or touch the player, ever again.
        /// </summary>
        /// <summary>Is a ring gone for good at <paramref name="drawTime"/>? (It only reads the field and changes nothing but the ring's own memory: rings are looked at on several cores at once.)</summary>
        private bool ringGone(BulletEmitter emitter, double drawTime)
        {
            double ringTime = effects.Effective(emitter, drawTime); // (on the bullets' own clock)
            double age = ringTime - emitter.Time;

            // (a ring that was only frozen - Reisen, Cirno - is judged by its radius again once the freezes are over, instead of being kept for its whole long lifetime)
            return age > emitter.Lifetime || emitter.HomingFinished || emitter.RemovedAt(drawTime) || ((emitter.IsRadial || emitter.IsStraightFromOrigins) && isRingGone(emitter, emitter.SlowestRadiusAt(ringTime)))
                   || (emitter.GoneAfterFreezes(ringTime, out float settled) && isRingGone(emitter, settled))
                   || (hasScreenBounds && emitter.GoneAfterOps(ringTime, screenMin, screenMax));
        }

        private bool[] goneFlags = new bool[256];

        /// <summary>Work that is shared out between the cores goes to at most this many (some are left for osu!'s drawing and sound), and only when there are this many rings (fewer are not worth it).</summary>
        private static readonly System.Threading.Tasks.ParallelOptions parallelOptions = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 3, 1, 8) };

        private const int parallel_min_rings = 64;

        private bool isRingGone(BulletEmitter emitter, float radius)
        {
            if (!hasScreenBounds)
                return false;

            Vector2 low = emitter.Origin, high = emitter.Origin;

            // (a ring whose bullets start from places of their own: the farthest any of them can be from a corner of the screen)
            if (emitter.Origins != null)
            {
                low = high = emitter.Origins[0];

                for (int b = 1; b < emitter.Count && b < emitter.Origins.Length; b++)
                {
                    low = Vector2.ComponentMin(low, emitter.Origins[b]);
                    high = Vector2.ComponentMax(high, emitter.Origins[b]);
                }
            }

            float dx = Math.Max(Math.Abs(high.X - screenMin.X), Math.Abs(screenMax.X - low.X));
            float dy = Math.Max(Math.Abs(high.Y - screenMin.Y), Math.Abs(screenMax.Y - low.Y));

            return radius - emitter.VisualRadius > MathF.Sqrt(dx * dx + dy * dy);
        }

        private readonly record struct HitRecord(double Time, int Emitter, int Bullet, double Damage);
    }
}
