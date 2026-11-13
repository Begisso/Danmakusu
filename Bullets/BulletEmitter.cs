using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Where the bullets of a ring were, how they were going and how they looked at <see cref="Time"/> (on the ring's own clock), as the field last worked them out. A ring with ops costs a trace per
    /// bullet: the collision check of the next frame starts from here instead of tracing the frame before again, and the renderer draws what the collision check of the same frame found.
    /// </summary>
    public sealed class RingSnapshot
    {
        public double Time = double.NaN;
        public readonly Vector2[] Position = new Vector2[64];
        public readonly Vector2[] Direction = new Vector2[64];
        public readonly BulletLook?[] Look = new BulletLook?[64];

        /// <summary>Until when each bullet stays exactly as it is here (frozen; NaN = it moves): until then it does not need to be worked out again.</summary>
        public readonly double[] StillUntil = new double[64];

        /// <summary>Which bullets were alive then (bit per bullet).</summary>
        public ulong Alive;

        /// <summary>Which bullets were worked out then (the others were not looked at: they had been used up, taken in...).</summary>
        public ulong Known;
    }

    /// <summary>
    /// One ring of bullets, fired from a hit circle / slider head at the moment its approach circle closes.
    /// A bullet's position is a pure function of time, which keeps gameplay and replays deterministic.
    /// </summary>
    public sealed class BulletEmitter
    {
        public double Time { get; init; }
        public Vector2 Origin { get; set; }

        /// <summary>(osu!catch) the ring comes out of a wall of the screen: 1 the bottom one, -1 the top one, straight below or above <see cref="Origin"/> (the field puts it there when the ring comes, as the walls are where the window has them). 0: from <see cref="Origin"/>.</summary>
        public int WallSide { get; set; }

        /// <summary>
        /// Junko's ring of the bullets she has counted, shot out again (dozens at once, slowly, from where she stands - on the player when the player is in the middle): Wakasagihime does not make it swim,
        /// so it has no traces (a knot of looping traces round the player otherwise, hurting like slider bodies).
        /// </summary>
        public bool Reshot { get; set; }

        /// <summary>The boss fires this ring: the field puts <see cref="Origin"/> at the boss's position at the moment the ring fires.</summary>
        public bool FollowsBoss { get; init; }

        /// <summary>Which of the bosses on the screen the ring comes from (see <see cref="BossPeriod.Rank"/>); only means something with <see cref="FollowsBoss"/>.</summary>
        public int BossRank { get; init; }

        /// <summary>Radius at which bullets appear (the edge of the hit circle).</summary>
        public float SpawnRadius { get; set; }

        /// <summary>Pixels per millisecond.</summary>
        public float Speed { get; init; }

        /// <summary>Combo index (with offsets) of the circle this ring belongs to; the field maps it to a skin combo colour.</summary>
        public int ComboIndex { get; init; }

        /// <summary>Bounding radius of the drawn bullet (the long half-axis of an ellipse, the corner distance of a triangle).</summary>
        public float VisualRadius { get; set; }

        public float HitRadius { get; set; }

        public BulletShape Shape { get; init; } = BulletShape.Circle;

        /// <summary>
        /// When the ring fires, the field rotates it so that its first bullet points at the player.
        /// Because of that <see cref="Directions"/> is only final once the ring has fired.
        /// </summary>
        public bool AimAtPlayer { get; init; }

        /// <summary>When the ring aims, its first bullet points this many radians away from the player (the second half of a doubled ring is turned half a step against the first).</summary>
        public float AimOffset { get; init; }

        /// <summary>A mirrored copy (Doremy's): it aims at the mirror image of the player (through the middle of the playfield, <see cref="MirrorSize"/>) instead of at the player.</summary>
        public bool AimMirrored { get; init; }

        /// <summary>The size of the playfield that <see cref="AimMirrored"/> mirrors through (the mirror image of p is MirrorSize - p).</summary>
        public Vector2 MirrorSize { get; init; }

        /// <summary>A boss's own shot (not a bullet of a hit object): Patchouli's earth turns every twentieth of them into a stone.</summary>
        public bool OwnShot { get; set; }

        /// <summary>How many of the own shots of earth had been seen before this ring (set by Patchouli's act); -1 = not seen yet.</summary>
        public int EarthBase = -1;

        /// <summary>Koakuma's own shots: every bullet of this ring is turned into a bat (the other rings' every tenth is).</summary>
        public bool Bats { get; set; }

        /// <summary>Wakasagihime's own shots: every bullet of this ring is a hollow bubble from the start (her act's).</summary>
        public bool Bubbles { get; set; }

        /// <summary>How many bullets of the other rings had been seen before this one (set by Koakuma's act: every tenth bullet is a bat); -1 = not seen yet.</summary>
        public int BatBase = -1;

        /// <summary>Bit n set = bullet n has flown into the moon and disappeared.</summary>
        public ulong AbsorbedMask;

        /// <summary>Bit n set = bullet n is shown golden and glowing (Toyohime has pulled it towards the player).</summary>
        public ulong GoldMask;

        /// <summary>How far (game time) the field has checked this ring's bullets against the moon; NaN = not at all.</summary>
        public double MoonScanned = double.NaN;

        /// <summary>(the field's, for rings with ops, whose bullets cost a trace each: where they were when it last worked them out, see <see cref="RingSnapshot"/>)</summary>
        public RingSnapshot? Seen;

        /// <summary>How far (game time) the field has checked this ring's bullets against the things of a boss's stage that take bullets away (Hecatia's planets, Nue's UFOs, the black hole); NaN = not at all.</summary>
        public double StageScanned = double.NaN;

        /// <summary>Time went backwards: the bullets have not flown into the moon yet.</summary>
        public void ResetMoon()
        {
            AbsorbedMask = 0;
            MoonScanned = double.NaN;
            StageScanned = double.NaN;
        }

        /// <summary>0 = the bullets have their own colour; otherwise one of the BulletRenderer.TINT_ colours, shown <see cref="TintAmount"/> of the way over it.</summary>
        public int Tint { get; set; }

        public float TintAmount { get; init; } = 1f;

        /// <summary>When above 0 every second bullet of the ring (the odd ones) is shown in this tint instead of <see cref="Tint"/>, as much (Doremy's copies: white and blue).</summary>
        public int AltTint { get; set; }

        /// <summary>The ring keeps all its bullets in a section of two bosses (where every other ring has half: Utsuho's six big bullets).</summary>
        public bool KeepCount { get; set; }

        /// <summary>The ring's bullets hurt like a slider body: not used up by a hit, they cost the slider damage every so often while the character is in them (Utsuho's big balls).</summary>
        public bool HurtsLikeSlider { get; set; }

        /// <summary>When above 0 the ring's bullets fade in over this long (ms) after they are fired (only the picture: Nemuno's rings round the player).</summary>
        public double FadeInMs { get; set; }

        /// <summary>The round bullets of the ring are big and are drawn as sharp as Mokou's ball, from a big picture (Utsuho's), instead of by the bullet renderer.</summary>
        public bool Sharp { get; set; }

        /// <summary>0 = the bullets do not glow; otherwise they glow this much (see UI.BulletInstance.Bloom).</summary>
        public float Bloom { get; set; }

        /// <summary>0 = the bullets are as bright as they are, 1 = black (Ringo's thrown bullets are darker).</summary>
        public float Shade { get; init; }

        /// <summary>Knives: how many blades every knife has (1 = a plain knife, 2 = double-edged, 3 = three-edged, 4 = a cross: blades joined at their handles).</summary>
        public int Blades { get; set; } = 1;

        /// <summary>Remilia's spinner knives: they never get more blades (and never spin).</summary>
        public bool PlainKnife { get; set; }

        /// <summary>Knives with a yellow handle (Yumeko's).</summary>
        public bool YellowHandle { get; set; }

        /// <summary>Remilia's attack: how many bullets (not knives) were thrown in the attack before this ring; -1 = none of this ring's bullets is ever a big one.</summary>
        public int BigBase { get; set; } = -1;

        /// <summary>With <see cref="BigBase"/>: every this many-th bullet is <see cref="BigFactor"/> times as big (Remilia's by default; Gengetsu's every 4th 1.2x).</summary>
        public int BigEvery { get; set; } = DanmakusuBalance.REMILIA_BIG_EVERY;

        public float BigFactor { get; set; } = DanmakusuBalance.REMILIA_BIG_SIZE;

        /// <summary>How many times as big bullet <paramref name="bullet"/> is: every <see cref="BigEvery"/>-th bullet counted from <see cref="BigBase"/> is <see cref="BigFactor"/> times as big.</summary>
        public float BigScale(int bullet)
            => !KeepsSize && BigBase >= 0 && (BigBase + bullet + 1) % BigEvery == 0 ? BigFactor : 1f; // (Utsuho's big balls and the trains keep their size)

        /// <summary>The biggest <see cref="BigScale"/> any bullet of this ring can have.</summary>
        public float BigScaleMax => !KeepsSize && BigBase >= 0 && Count + BigBase >= BigEvery ? Math.Max(BigFactor, 1f) : 1f;

        /// <summary>No other boss's mechanic changes the size of these: Utsuho's big balls (<see cref="Sharp"/>) and special things like Yukari's trains.</summary>
        public bool KeepsSize => Sharp || Shape == BulletShape.Train;

        /// <summary>Above 0: every eighth bullet of the ring has a beam for a tail, this many ms of its way long (Byakuren's passive, see BulletField.Tails).</summary>
        public double TailMs { get; set; }

        /// <summary>Every this many-th bullet has a tail (0 = every as many as Byakuren's: <see cref="DanmakusuBalance.BYAKUREN_STAR_TAIL_EVERY"/>).</summary>
        public int TailEvery { get; set; }

        /// <summary>When above 1 the bullets speed up evenly from their own speed to this many times it over <see cref="SpeedRampMs"/> after they were fired, and keep that speed (Gengetsu's).</summary>
        public float SpeedRampTo { get; set; } = 1f;

        public double SpeedRampMs { get; set; } = 1;

        /// <summary>When not 0 the bullets curl as they fly out: they go round the ring's centre at this share of their speed outwards, the same speed however far out they are, on a spiral: the even ones one way, the odd ones the other.</summary>
        public float Curl { get; set; }

        /// <summary>Mugetsu's vortex: in her attacks the bullets go round her (null = the ring's life meets none of them). See <see cref="Bullets.Swirl"/>.</summary>
        public Swirl? Swirl { get; set; }

        /// <summary>Mugetsu: where she stood when this ring was fired in an attack of hers (its bullets go round there, wherever she goes after); not known for a ring fired outside her attacks.</summary>
        public Vector2 SwirlCentre { get; private set; }

        public bool SwirlCentreKnown { get; private set; }

        /// <summary>The ring is fired now: if that is in an attack of Mugetsu's, it takes where she is now as the middle it goes round.</summary>
        public void SnapSwirl()
        {
            SwirlCentreKnown = Swirl != null && Swirl.CentreAt(Time, out var centre) && (SwirlCentre = centre) == centre;
        }

        /// <summary>All the bullets curl the same way (<see cref="Curl"/>'s sign is the way round): the ring turns as it flies (Kaguya's).</summary>
        public bool CurlsTogether { get; set; }

        private float curlOf(int bullet) => CurlsTogether || bullet % 2 == 0 ? Curl : -Curl;

        /// <summary>How far (in ms at the ring's own speed) a bullet has got <paramref name="age"/> ms after it was fired: more than its age when it speeds up (see <see cref="SpeedRampTo"/>).</summary>
        public double FlownAge(double age)
        {
            if (SpeedRampTo <= 1f || age <= 0)
                return age;

            double ramp = Math.Max(SpeedRampMs, 1);
            double extra = SpeedRampTo - 1;

            return age <= ramp ? age + extra * age * age / (2 * ramp) : ramp + extra * ramp / 2 + SpeedRampTo * (age - ramp);
        }

        /// <summary>When above 0 the bullets swim: they go this many px to each side of their way and back, the even ones one way and the odd ones the other, <see cref="WaveRate"/> radians per ms (Wakasagihime's).</summary>
        public float WaveSize { get; set; }

        public float WaveRate { get; set; }

        /// <summary>
        /// The bullets really swim: the wave is only in the closed form of a bullet that flies on its own (see evaluateOwn): not one the ops of another boss steer, not one that wraps round the screen (Daiyousei's: its
        /// place is worked out again), nor one that ricochets or homes. Only these leave Wakasagihime's traces.
        /// </summary>
        public bool Waves => WaveSize > 0f && !Homing && !Ricochet && !(Ops != null && OpsArea != null) && !WrapsOnce;

        /// <summary>The bullets change speed as they go, evenly with the distance, from <see cref="SlowFrom"/> times their speed where they start to <see cref="SlowTo"/> times it at the wall ahead of them (or <see cref="SlowOver"/> px out), and keep that (Futo's and Ran's; <see cref="Area"/> is the screen).</summary>
        public bool SlowsToWall { get; set; }

        public float SlowFrom { get; set; } = 1f;
        public float SlowTo { get; set; } = 1f;
        public float SlowOver { get; set; }

        /// <summary>
        /// The bullets go round once: one that goes out of the screen comes in again where the line it flies along comes into the screen, the same way, and is gone when it goes out a second time. Nothing can destroy them: what would
        /// take one takes a copy of it, once, and the bullet flies on (Daiyousei's; <see cref="Area"/> is the screen).
        /// </summary>
        public bool WrapsOnce { get; set; }

        /// <summary>How far it is from <paramref name="from"/> along <paramref name="way"/> to the edge of the screen.</summary>
        private float toEdge(Vector2 from, Vector2 way)
        {
            var area = Area ?? new RicochetArea();
            float toX = way.X > 1e-6f ? (area.Max.X - from.X) / way.X : way.X < -1e-6f ? (area.Min.X - from.X) / way.X : float.PositiveInfinity;
            float toY = way.Y > 1e-6f ? (area.Max.Y - from.Y) / way.Y : way.Y < -1e-6f ? (area.Min.Y - from.Y) / way.Y : float.PositiveInfinity;

            return Math.Max(Math.Min(toX, toY), 1f);
        }

        /// <summary>Where a bullet is that has come <paramref name="reach"/> from <paramref name="spawn"/> and goes round once (it leaves and comes in with all of it outside the screen).</summary>
        /// <summary>How many times its radii (drawn and hit) were made bigger by Daiyousei's buff (1 = not at all).</summary>
        public float VerdantScale { get; set; } = 1f;

        /// <summary>
        /// Bullet <paramref name="bullet"/> (of a ring that goes round once, Daiyousei's) has left the screen at <paramref name="time"/> (its own clock) and come in again from the opposite wall: it has lost her buff then (not bigger,
        /// not green, does not glow, is used up by a hit, can be taken). As the closed form works it out (see wrappedOnce); a ring that something else steers does not go round, nor lose it.
        /// </summary>
        public bool LostVerdant(int bullet, double time)
        {
            if (!WrapsOnce || Homing || Ricochet || (Ops != null && OpsArea != null) || time <= Time || bullet >= Directions.Length)
                return false;

            Vector2 d = Directions[bullet];
            float scale = RadialScales == null ? 1f : RadialScales[bullet];
            Vector2 spawn = Origins != null ? Origins[bullet] : Origin;
            Vector2 way = curled(d, bullet, time - Time);
            float reach = scale * RadiusAt(time);

            if (SlowsToWall)
                reach = slowedReach(spawn, way, reach);

            return reach > toEdge(spawn, way) + VisualRadius;
        }

        private Vector2 wrappedOnce(Vector2 spawn, Vector2 way, float reach, out bool alive)
        {
            float ahead = toEdge(spawn, way) + VisualRadius;
            alive = true;

            if (reach <= ahead)
                return spawn + way * reach;

            float behind = toEdge(spawn, -way) + VisualRadius;
            float again = reach - ahead;

            alive = again <= ahead + behind;
            return spawn - way * behind + way * again;
        }

        /// <summary>How far a bullet that would be <paramref name="reach"/> from <paramref name="spawn"/> has got when it changes speed so: going a distance at a speed that falls evenly with it is an exponential; past the end it goes on at the last speed.</summary>
        private float slowedReach(Vector2 spawn, Vector2 way, float reach)
        {
            float length = SlowOver > 0f ? SlowOver : toEdge(spawn, way);
            float lost = SlowFrom - SlowTo;
            float atEnd = MathF.Log(SlowFrom / SlowTo) / lost; // (how far it would have gone at its own speed by the time it gets to the end, in those lengths)
            float x = reach / length;

            return x < atEnd ? length * SlowFrom / lost * (1f - MathF.Exp(-lost * x)) : length + (reach - atEnd * length) * SlowTo;
        }

        /// <summary>A curling bullet is counted from this far out (px): right at the centre the same speed round it would be endlessly fast.</summary>
        private const float curl_near = 16f;

        /// <summary>The way bullet <paramref name="bullet"/> points <paramref name="age"/> ms after it was fired, curled (see <see cref="Curl"/>).</summary>
        private Vector2 curled(Vector2 d, int bullet, double age)
        {
            if (Curl == 0f || age <= 0)
                return d;

            // (at an even speed round the centre the angle grows ever more slowly as the distance grows: the sum of curl * speed / distance over the flight)
            float near = Math.Max(SpawnRadius, curl_near);
            float angle = curlOf(bullet) * MathF.Log(1f + Speed * (float)age / near);
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
        }

        /// <summary>Knives with more than one blade: how fast (radians per ms, the sign is the way round) they turn round their handle; 0 = they do not.</summary>
        public float BladeSpin { get; set; }

        /// <summary>How far the blades of bullet <paramref name="bullet"/> are turned at <paramref name="time"/> (a knife that lies still does not spin).</summary>
        public float BladeAngle(int bullet, double time)
        {
            if (BladeSpin == 0f)
                return 0f;

            if (Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck && time >= lying.Time)
                time = lying.Time;

            return (float)(BladeSpin * (time - Time));
        }

        /// <summary>The bullets are drawn as this shape instead of their own (null = their own); they still collide as <see cref="Shape"/>.</summary>
        public BulletShape? DrawShape { get; set; }

        /// <summary>When above 0 the bullets fade out from this moment on, over <see cref="DanmakusuBalance.LIFE_FADE_MS"/> (64 beats after they came: <see cref="DanmakusuBalance.LIFE_MOST_BEATS"/>).</summary>
        public double FadesFrom { get; set; }

        /// <summary>When set the bullets are drawn as these shapes in turn (Kaguya's, when she has come back).</summary>
        public BulletShape[]? MixedShapes { get; set; }

        /// <summary>What bullet <paramref name="bullet"/> is drawn as.</summary>
        public BulletShape ShapeOf(int bullet) => MixedShapes != null ? MixedShapes[bullet % MixedShapes.Length] : DrawShape ?? Shape;

        /// <summary>How many times as big a bullet drawn as a flower is than the flowers of Yuyuko's tree that fall (Lily White's flowers).</summary>
        public float FlowerScale { get; set; } = 1f;

        /// <summary>When above 0 the whole ring is gone this long after it was fired (Lily White's flowers burst into petals then: those are another ring, see <see cref="DeriveKind.Explode"/>).</summary>
        public double RemoveAfter { get; set; }

        /// <summary>The bullets flash (their glow pulses, faster and brighter towards <see cref="RemoveAfter"/>).</summary>
        public bool Flashes { get; set; }

        /// <summary>Unit direction of every bullet in the ring.</summary>
        public Vector2[] Directions { get; init; } = [];

        /// <summary>Time until every bullet has left the playfield.</summary>
        public double Lifetime { get; set; }

        /// <summary>Bit n set = bullet n has already hit the player and disappeared.</summary>
        public ulong ConsumedMask;

        public int Count => Directions.Length;

        /// <summary>
        /// Set by the field once the whole ring is gone for good (expired, or farther from its centre than any point of the screen),
        /// so it is not looked at again. Cleared when time goes backwards.
        /// </summary>
        public bool Retired;

        /// <summary>
        /// Optional multiplier (0..1] of every bullet's distance from the centre, and so of its speed: rings that are not circles
        /// (square, triangle, star) have bullets that start and fly at different distances so the outline keeps its shape. Null = a circle.
        /// <see cref="Speed"/> is then the speed of the farthest corner.
        /// </summary>
        public float[]? RadialScales { get; init; }

        /// <summary>The smallest value in <see cref="RadialScales"/> (1 when there are none).</summary>
        public float MinRadialScale { get; init; } = 1f;

        /// <summary>Bullets bounce off the border of the screen (once at most; at the second wall they fly out of the screen).</summary>
        public bool Ricochet { get; set; }

        /// <summary>Ricocheting bullets bounce this many times more than <see cref="DanmakusuBalance.MAX_RICOCHETS"/> (Toyohime's pause bullets).</summary>
        public int ExtraRicochets { get; set; }

        private int bounceLimit => DanmakusuBalance.MAX_RICOCHETS + ExtraRicochets;

        /// <summary>The area a ricocheting bullet stays in (shared by all rings; the field keeps it up to date).</summary>
        public RicochetArea? Area { get; set; }

        /// <summary>Ellipse (hitclap) bullets fall: gravity pulls them down from the moment they are fired, on top of their straight flight.</summary>
        public bool Falling => Shape == BulletShape.Ellipse || FallsToPlayer;

        /// <summary>The bullets fall like ellipses whatever their shape (Nemuno's: the rings of every fifth object, fired where the player is).</summary>
        public bool FallsToPlayer { get; set; }

        /// <summary>The ring is fired from where the player is when it fires (not from <see cref="Origin"/>); a falling one is pulled the way from <see cref="Origin"/> to the player then.</summary>
        public bool FromPlayer { get; set; }

        /// <summary>Knives: the picture is this many times as long (Nemuno's knives have blades twice as long).</summary>
        public float KnifeStretch { get; set; } = 1f;

        /// <summary>Gravity of a falling bullet, pixels per ms squared (scales with the circle size).</summary>
        public float Gravity { get; set; }

        /// <summary>
        /// The way a falling bullet is pulled: from the ring's centre towards the player at the moment the ring fires, and then never
        /// changes (the field sets it when the ring fires). Down until then.
        /// </summary>
        public Vector2 GravityDirection = new Vector2(0, 1);

        /// <summary>
        /// Arrowhead (hitwhistle) bullets fall too, but the pull always points at the player's current position, whatever the player does.
        /// The moment a bullet points at the player (within <see cref="DanmakusuBalance.HOMING_AIMED_ANGLE_DEGREES"/>) it stops turning for good and flies
        /// in a straight line. Arrowheads have no acceleration at all (constant speed). It disappears for good when it leaves the screen (<see cref="Area"/>).
        /// That cannot be written as a function of time, so these bullets are simulated step by step (see <see cref="StepHoming"/>).
        /// </summary>
        public bool Homing => Shape == BulletShape.Triangle && !Straight;

        /// <summary>An arrowhead that only looks like one: it flies in a straight line like a circle bullet (no pull, no homing).</summary>
        public bool Straight { get; init; }

        /// <summary>
        /// A pulled bullet that comes this close to the player (osu!pixels) is not pulled any more and flies straight on at <see cref="Speed"/>,
        /// the speed of a plain circle bullet of its ring: it neither turns nor accelerates.
        /// </summary>
        public float ReleaseRadius { get; init; }

        /// <summary>True for rings whose bullets fly in straight lines away from the centre (so the whole ring can be judged by its radius).</summary>
        public bool IsRadial => !Ricochet && !Falling && !Homing && !SlowsToWall && !WrapsOnce && WaveSize <= 0f && Ops == null && Origins == null && Swirl == null && Deflections is not { Count: > 0 };

        /// <summary>Like <see cref="IsRadial"/>, but every bullet flies straight out from a place of its own (<see cref="Origins"/>: the bullets a burst throws out, Cirno's).</summary>
        public bool IsStraightFromOrigins => !Ricochet && !Falling && !Homing && !SlowsToWall && !WrapsOnce && WaveSize <= 0f && Ops == null && Origins != null && Origins.Length > 0 && Swirl == null && Deflections is not { Count: > 0 };

        /// <summary>
        /// Every bullet starts from a place of its own (instead of all from <see cref="Origin"/>): the rings that the field makes while the game runs (what a bullet
        /// turns into when it explodes, what a boss shoots back out) have them. Null = all from <see cref="Origin"/>.
        /// </summary>
        public Vector2[]? Origins { get; set; }

        /// <summary>This ring was made from other bullets (it is not frozen, mirrored... like the first bullets are).</summary>
        public bool Derived { get; init; }

        /// <summary>Which piece of something bigger the ring is (Mima's stars: a ring for each edge of her moon, 0 the outer one).</summary>
        public int Piece { get; init; }

        /// <summary>
        /// What the field fills in when this ring fires (before that its <see cref="Directions"/> and <see cref="Origins"/> are empty): the rings that are made out of
        /// other bullets (an explosion, a throw back, a release of knives). <see cref="Count"/> is already right.
        /// </summary>
        public DeriveKind Derive { get; init; }

        /// <summary>Echo: which bullets of the source ring are thrown out again (this ring has one bullet for each, in this order).</summary>
        public int[]? EchoIndices { get; init; }

        /// <summary>The bullets of this ring turn the bullets they touch (see <see cref="Deflections"/>): Ringo's big bullets.</summary>
        public bool Deflector { get; set; }

        /// <summary>
        /// Ringo: the three this ring is part of (a ring of her attacks and the two copies right behind it; 0 = none) and which of them it is (0 the first, 1 and 2 the copies behind it). A ring made in its place keeps it
        /// (halved with another boss on the screen, scaled, shot from the boss), so her act finds the three whatever has been done to them.
        /// </summary>
        public int SkewerId { get; set; }

        public int SkewerPart { get; set; }

        private BulletEmitter keepSkewer(BulletEmitter made)
        {
            made.SkewerId = SkewerId;
            made.SkewerPart = SkewerPart;
            return made;
        }

        /// <summary>
        /// Bullets that a deflector has touched: from that moment (and place) on each flies in the direction it was given, at its own speed. The field records them as they
        /// happen and takes them back when time goes backwards, so the flight stays a closed form of time. Null until the first one.
        /// </summary>
        public System.Collections.Generic.Dictionary<int, Deflection>? Deflections { get; private set; }

        /// <summary><see cref="Speed"/> is in px per ms; 0 = the bullet's own speed.</summary>
        public readonly record struct Deflection(double Time, Vector2 Position, Vector2 Direction, float Speed = 0f, int BouncesLeft = 0, int WallBounces = 0, bool Stuck = false, double Gone = double.PositiveInfinity, int WallSet = 0, float Gravity = 0f, int By = -1);

        /// <summary>Is bullet <paramref name="bullet"/> lying where it stopped (Reimu's pile at the border)?</summary>
        public bool IsStuck(int bullet)
            => (Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck) || (stuckMask & (1UL << bullet)) != 0;

        /// <summary>
        /// Bullet <paramref name="bullet"/> stops at <paramref name="position"/> at <paramref name="time"/> and stays there, facing <paramref name="direction"/>, until <paramref name="goneTime"/> (the ring's own clock),
        /// when it disappears. The ring must live on until then.
        /// </summary>
        public void Stick(int bullet, double time, Vector2 position, Vector2 direction, double goneTime)
        {
            Deflections ??= new System.Collections.Generic.Dictionary<int, Deflection>();
            Deflections[bullet] = new Deflection(time, position, direction, 0f, 0, WallBounces(bullet), true, goneTime, WallSetOf(bullet));
            Lifetime = Math.Max(Lifetime, goneTime - Time + 1);
        }

        /// <summary>When (the ring's own clock) a bullet that lies still disappears; infinity if it does not lie still.</summary>
        public double StuckGoneTime(int bullet)
        {
            if (Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck)
                return lying.Gone;

            if ((stuckMask & (1UL << bullet)) != 0 && stuckGone != null)
                return stuckGone[bullet];

            return double.PositiveInfinity;
        }

        /// <summary>When (the ring's own clock) a bullet came to lie still; NaN if it does not lie still (or is a pulled one: those are not recorded).</summary>
        public double StuckSinceTime(int bullet)
            => Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck ? lying.Time : double.NaN;

        private ulong stuckMask; // bit n: the (pulled) bullet n lies where it stopped
        private double[]? stuckGone;

        /// <summary>A pulled bullet stops at <paramref name="position"/> and stays there until <paramref name="goneTime"/> (the ring's own clock).</summary>
        public void StickHoming(int bullet, Vector2 position, double goneTime)
        {
            ensureHoming();
            stuckGone ??= new double[Count];
            stuckGone[bullet] = goneTime;
            stuckMask |= 1UL << bullet;
            homingPosition![bullet] = position;
            homingPrevious![bullet] = position;
            Lifetime = Math.Max(Lifetime, goneTime - Time + 1);
        }

        /// <summary>Can this ring's bullets be pushed / turned (a closed-form flight: not falling, homing, with ops, or a deflector itself; ricocheting ones can, and keep their ricochets)?</summary>
        public bool CanBeDeflected => !Falling && !Homing && Ops == null && !Deflector;

        private TrajectoryOp[]? fallFreezes;
        private double fallFreezeSpan;

        /// <summary>
        /// The freezes of the bosses (sorted by time), for the bullets that have been dropped and fall freely after being turned (Zanmu's, Toyohime's): they stand still for as long as a freeze that begins after
        /// they were dropped lasts, and go on falling when it ends. (The other things the bosses do to flying bullets do not reach them.)
        /// </summary>
        public TrajectoryOp[]? FallFreezes
        {
            get => fallFreezes;
            set
            {
                fallFreezes = value;
                fallFreezeSpan = 0;

                if (value == null)
                    return;

                foreach (var op in value)
                {
                    if (op.Kind == TrajectoryOpKind.Freeze && !op.Slides)
                        fallFreezeSpan += op.End - op.Time;
                }
            }
        }

        /// <summary>How long between <paramref name="from"/> and <paramref name="to"/> the freezes of <see cref="FallFreezes"/> that began after <paramref name="from"/> have stopped a dropped bullet; <paramref name="look"/> is its look if it is standing still at <paramref name="to"/>.</summary>
        private double frozenBetween(double from, double to, out BulletLook? look)
        {
            look = null;
            double total = 0;
            double covered = from;

            if (fallFreezes == null)
                return 0;

            foreach (var op in fallFreezes)
            {
                if (op.Kind != TrajectoryOpKind.Freeze || op.Slides || op.Time <= from)
                    continue;

                if (op.Time >= to)
                    break;

                double a = Math.Max(op.Time, covered), b = Math.Min(op.End, to);

                if (b > a)
                {
                    total += b - a;

                    if (to < op.End)
                        look = op.During;
                }

                covered = Math.Max(covered, op.End);
            }

            return total;
        }

        /// <summary>Records that bullet <paramref name="bullet"/> was turned at <paramref name="time"/>; the ring must be allowed to live on for its longer way.</summary>
        public void Deflect(int bullet, double time, Vector2 position, Vector2 direction, double newLifetime, float speed = 0f, int ricochetsLeft = 0, int wallBounces = -1, int wallSet = 0, float gravity = 0f, int by = -1)
        {
            Deflections ??= new System.Collections.Generic.Dictionary<int, Deflection>();

            // (a negative number keeps the count of walls it has bounced off, and the set of walls they were; `by` is the stage act that turned it, if one did)
            Deflections[bullet] = new Deflection(time, position, direction, speed, ricochetsLeft, wallBounces >= 0 ? wallBounces : WallBounces(bullet), false, double.PositiveInfinity, wallBounces >= 0 ? wallSet : WallSetOf(bullet), gravity, by);
            Lifetime = Math.Max(Lifetime, newLifetime + (gravity > 0f ? fallFreezeSpan : 0));
        }

        /// <summary>How many times bullet <paramref name="bullet"/> has bounced off the set of Kyouko's glass walls <paramref name="set"/> so far (the walls of another attack are new walls: nothing counted).</summary>
        public int WallBouncesOf(int bullet, int set) => Deflections != null && Deflections.TryGetValue(bullet, out var known) && known.WallSet == set ? known.WallBounces : 0;

        /// <summary>How many times it bounced off the last set of walls (whichever it was).</summary>
        private int WallBounces(int bullet) => Deflections != null && Deflections.TryGetValue(bullet, out var known) ? known.WallBounces : 0;

        private int WallSetOf(int bullet) => Deflections != null && Deflections.TryGetValue(bullet, out var known) ? known.WallSet : 0;

        /// <summary>Kyouko's glass walls, for the bullets that are simulated step by step (arrowheads; the others are checked by the field, see <see cref="Deflect"/>). Null = there are none.</summary>
        public IGlassWalls? Glass { get; set; }

        private int[]? wallBounces; // (arrowheads: how often each has bounced off the walls of the set it last touched)
        private int[]? wallSets;

        /// <summary>Takes back a turn (time went backwards).</summary>
        public void Undeflect(int bullet) => Deflections?.Remove(bullet);

        /// <summary>Takes back a turn (time went backwards): the bullet flies as <paramref name="before"/> says again, the turn it had before this one (Ran's beams bounce a bullet twice), or its own way if it had none.</summary>
        public void Undeflect(int bullet, Deflection? before)
        {
            if (before is Deflection earlier && Deflections != null)
                Deflections[bullet] = earlier;
            else
                Undeflect(bullet);
        }

        /// <summary>The last turn of bullet <paramref name="bullet"/>, if it has been turned (null: it flies its own way).</summary>
        public Deflection? DeflectionOf(int bullet) => Deflections != null && Deflections.TryGetValue(bullet, out var known) ? known : null;

        /// <summary>(The field's: the palette slot of the colour its circle or slider has in its boss section, -1 none, -2 not worked out yet.)</summary>
        public int SectionStyle = -2;

        /// <summary>Explode / Echo: the ring whose bullets this ring is made from (the planner points it at what replaced that ring, see <see cref="Lineage"/>).</summary>
        public BulletEmitter? Source { get; set; }

        /// <summary>
        /// The ring this one was first made from by copying it (halved, a third of it, bigger, rebuilt, turned...; null: it is the first itself). A ring made out of another one (Cirno's bursts) that was planned before
        /// that one was replaced by such a copy follows it to the copy, see the end of BulletPlanner.Plan.
        /// </summary>
        public BulletEmitter? Lineage { get; private init; }

        /// <summary>A burst of only the bullets of the source that were frozen: every second one (Cirno's).</summary>
        public bool BurstsEverySecond { get; init; }

        /// <summary>Dissipate: the beam a slice of which turns into this ring's bullets, and when (game time) that slice was fired.</summary>
        public LaserEmitter? SourceLaser { get; init; }

        public double SliceTime { get; init; }

        /// <summary>Explode: the bullet flies this many radians away from the direction its source bullet was flying in.</summary>
        public float FanOffset { get; init; }

        /// <summary>Held: where each knife hangs round the boss until the ring fires, and when (game time) it appeared.</summary>
        public Vector2[]? HeldOffsets { get; init; }

        public double[]? HeldFrom { get; init; }

        /// <summary>Bit n set = bullet n does not exist (a derived ring has nothing to make it from there). Set again every time the ring fires.</summary>
        public ulong HiddenMask;

        /// <summary>The shape the bullets are arranged in and the angle of the first one, when the ring was made by <see cref="RingLayout"/> (null = some other arrangement).</summary>
        public RingShape? Layout { get; set; }

        public float LayoutPhase { get; set; }

        private BulletEmitter copy(Vector2[] directions, float[]? scales, float minScale, float aimOffset, bool? followsBoss = null, double timeShift = 0, float sizeFactor = 1f, int? bossRank = null,
                                   float speedFactor = 1f, Vector2? origin = null, float? shade = null, Vector2? mirrorSize = null, float phaseShift = 0f, int? tint = null, bool? aimAtPlayer = null)
        {
            return new BulletEmitter
            {
                Lineage = Lineage ?? this,
                Time = Time + timeShift,
                Origin = origin ?? Origin,
                AimMirrored = mirrorSize != null || AimMirrored,
                MirrorSize = mirrorSize ?? MirrorSize,
                FollowsBoss = followsBoss ?? FollowsBoss,
                BossRank = bossRank ?? BossRank,
                SpawnRadius = SpawnRadius,
                Speed = Speed * speedFactor,
                ComboIndex = ComboIndex,
                VisualRadius = VisualRadius * sizeFactor,
                HitRadius = HitRadius * sizeFactor,
                Shape = Shape,
                AimAtPlayer = aimAtPlayer ?? AimAtPlayer,
                AimOffset = aimOffset,
                Directions = directions,
                Lifetime = Lifetime / speedFactor,
                RadialScales = scales,
                MinRadialScale = minScale,
                Ricochet = Ricochet,
                ExtraRicochets = ExtraRicochets,
                Area = Area,
                Gravity = Gravity,
                Straight = Straight,
                ReleaseRadius = ReleaseRadius,
                Derived = Derived,
                Tint = tint ?? Tint,
                TintAmount = tint != null ? 1f : TintAmount,
                AltTint = tint != null ? 0 : AltTint,
                Bloom = Bloom,
                Sharp = Sharp,
                FadeInMs = FadeInMs,
                HurtsLikeSlider = HurtsLikeSlider,
                VerdantScale = VerdantScale,
                SpeedRampTo = SpeedRampTo,
                SpeedRampMs = SpeedRampMs,
                Curl = Curl,
                CurlsTogether = CurlsTogether,
                Swirl = Swirl,
                FadesFrom = FadesFrom,
                MixedShapes = MixedShapes,
                BigEvery = BigEvery,
                BigFactor = BigFactor,
                KnifeStretch = KnifeStretch,
                FallsToPlayer = FallsToPlayer,
                FromPlayer = FromPlayer,
                Shade = shade ?? Shade,
                DrawShape = DrawShape,
                FlowerScale = FlowerScale,
                RemoveAfter = RemoveAfter,
                Flashes = Flashes,
                Blades = Blades,
                PlainKnife = PlainKnife,
                YellowHandle = YellowHandle,
                BladeSpin = BladeSpin,
                BigBase = BigBase,
                Layout = Layout,
                LayoutPhase = LayoutPhase + phaseShift,
                OwnShot = OwnShot,
            };
        }

        /// <summary>
        /// The ring coming in from outside the screen instead (Utsuho's black hole draws everything in): every bullet starts just beyond the edge of a screen <paramref name="halfScreen"/> wide and high either side of
        /// <paramref name="middle"/>, the way it would have flown out from the middle, and flies straight in at it. It does not aim at the player (its bullets keep the ways they had round the middle).
        /// </summary>
        public BulletEmitter FromOutside(Vector2 middle, Vector2 halfScreen)
        {
            var inward = new Vector2[Directions.Length];
            var origins = new Vector2[Directions.Length];
            float beyond = VisualRadius * 2f;

            for (int i = 0; i < inward.Length; i++)
            {
                Vector2 d = Directions[i].LengthSquared > 1e-8f ? Vector2.Normalize(Directions[i]) : new Vector2(0f, -1f);

                // (where the way out from the middle leaves the screen, and a little further)
                float reachX = Math.Abs(d.X) > 1e-6f ? halfScreen.X / Math.Abs(d.X) : float.PositiveInfinity;
                float reachY = Math.Abs(d.Y) > 1e-6f ? halfScreen.Y / Math.Abs(d.Y) : float.PositiveInfinity;
                float reach = Math.Min(reachX, reachY) + beyond;

                inward[i] = -d;
                origins[i] = middle + d * reach;
            }

            var made = copy(inward, null, 1f, 0f, aimAtPlayer: false);
            made.Origins = origins;
            made.SpawnRadius = 0f;
            made.Lifetime = Math.Max(made.Lifetime, (halfScreen.Length + beyond) * 2.5f / Math.Max(Speed, 1e-3f));
            return made;
        }

        /// <summary>
        /// Sagume's copy of the ring: the same ring fired from the same place the same way, but at <paramref name="time"/>, <paramref name="sizeFactor"/> times as big, <paramref name="speedFactor"/>
        /// times as fast, all in tint <paramref name="tint"/>.
        /// </summary>
        public BulletEmitter Replayed(double time, float sizeFactor, float speedFactor, int tint)
            => copy((Vector2[])Directions.Clone(), RadialScales, MinRadialScale, AimOffset, timeShift: time - Time, sizeFactor: sizeFactor, speedFactor: speedFactor, tint: tint);

        /// <summary>
        /// Doremy's copy of the ring: fired from the mirror image of where it is fired (through the middle of a playfield of <paramref name="playfield"/>), every bullet the opposite way
        /// (a ring that aims, aims at the mirror image of the player), <paramref name="sizeFactor"/> times as big, <paramref name="speedFactor"/> times as fast, at least <paramref name="shade"/> darker.
        /// </summary>
        public BulletEmitter Mirrored(Vector2 playfield, float sizeFactor, float speedFactor, float shade)
        {
            var directions = new Vector2[Directions.Length];

            for (int i = 0; i < directions.Length; i++)
                directions[i] = -Directions[i];

            return copy(directions, RadialScales, MinRadialScale, AimOffset, sizeFactor: sizeFactor, speedFactor: speedFactor, origin: playfield - Origin,
                shade: Math.Max(Shade, shade), mirrorSize: playfield, phaseShift: MathF.PI);
        }

        /// <summary>The same ring, turned by <paramref name="radians"/> (bullets that aim turn their aim): used for the extra bullets at the side of every bullet.</summary>
        public BulletEmitter Rotated(float radians)
        {
            float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
            var turned = new Vector2[Directions.Length];

            for (int i = 0; i < turned.Length; i++)
            {
                Vector2 d = Directions[i];
                turned[i] = AimAtPlayer ? d : new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
            }

            return copy(turned, RadialScales, MinRadialScale, AimAtPlayer ? AimOffset + radians : AimOffset);
        }

        /// <summary>The same ring, fired <paramref name="milliseconds"/> later (the bullets that follow behind another ring).</summary>
        public BulletEmitter Delayed(double milliseconds) => copy((Vector2[])Directions.Clone(), RadialScales, MinRadialScale, AimOffset, null, milliseconds);

        /// <summary>The same ring with bullets <paramref name="factor"/> times as big.</summary>
        public BulletEmitter Scaled(float factor) => KeepsSize ? this : keepSkewer(copy((Vector2[])Directions.Clone(), RadialScales, MinRadialScale, AimOffset, null, 0, factor)); // (no other boss's mechanic resizes Utsuho's big balls or the trains)

        /// <summary>The same ring with every other bullet gone (half as many, rounded up; a ring of one stays).</summary>
        public BulletEmitter Halved()
        {
            int kept = (Directions.Length + 1) / 2;
            var directions = new Vector2[kept];
            float[]? scales = RadialScales == null ? null : new float[kept];
            float min = 1f;

            for (int k = 0; k < kept; k++)
            {
                directions[k] = Directions[2 * k];

                if (scales != null)
                {
                    scales[k] = RadialScales![2 * k];
                    min = Math.Min(min, scales[k]);
                }
            }

            return keepSkewer(copy(directions, scales, scales == null ? 1f : min, AimOffset));
        }

        /// <summary>The same ring with only every third bullet left (a third as many, rounded up; a ring of one stays).</summary>
        public BulletEmitter Thirded()
        {
            int kept = (Directions.Length + 2) / 3;
            var directions = new Vector2[kept];
            float[]? scales = RadialScales == null ? null : new float[kept];
            float min = 1f;

            for (int k = 0; k < kept; k++)
            {
                directions[k] = Directions[3 * k];

                if (scales != null)
                {
                    scales[k] = RadialScales![3 * k];
                    min = Math.Min(min, scales[k]);
                }
            }

            return keepSkewer(copy(directions, scales, scales == null ? 1f : min, AimOffset));
        }

        /// <summary>The same ring, fired by the boss: its centre is where the boss stands when it fires.</summary>
        public BulletEmitter ShotFromBoss(int bossRank = 0) => keepSkewer(copy((Vector2[])Directions.Clone(), RadialScales, MinRadialScale, AimOffset, true, bossRank: bossRank));

        /// <summary>The same ring with another number of bullets, turned further by <paramref name="extraPhase"/> radians (only rings that know their <see cref="Layout"/>; others are returned as they are).</summary>
        public BulletEmitter Rebuilt(int count, float extraPhase) => Layout == null ? this : RebuiltAs(Layout.Value, count, extraPhase);

        /// <summary>The same ring in another outline (<paramref name="shape"/>) with <paramref name="count"/> bullets, turned further by <paramref name="extraPhase"/> radians (only rings that know their <see cref="Layout"/>).</summary>
        public BulletEmitter RebuiltAs(RingShape shape, int count, float extraPhase)
        {
            if (Layout == null || count < 1)
                return this;

            float phase = LayoutPhase + extraPhase;
            var directions = RingLayout.Create(shape, count, phase, out var scales);
            float min = 1f;

            if (scales != null)
            {
                foreach (float value in scales)
                    min = Math.Min(min, value);
            }

            var rebuilt = copy(directions, scales, min, AimOffset);
            rebuilt.LayoutPhase = phase;
            rebuilt.Layout = shape;

            // (an outline whose nearest points are nearer than the old one's: those bullets are slower, and live longer)
            if (min < MinRadialScale)
                rebuilt.Lifetime = Lifetime * MinRadialScale / Math.Max(min, 0.05f);

            return rebuilt;
        }

        /// <summary>
        /// The things that happen to the bullets of this ring while they fly (see <see cref="TrajectoryOp"/>): null = nothing. Only the ops that begin while the ring is
        /// alive and that touch things fired when this ring was fired are kept (see <see cref="ConfigureOps"/>).
        /// </summary>
        public TrajectoryOp[]? Ops { get; private set; }

        /// <summary>The screen the ops work with (bouncing, the top it falls from).</summary>
        public RicochetArea? OpsArea { get; private set; }

        /// <summary>
        /// Gives the ring the ops (sorted by time) that begin while it is alive and touch things that were fired at its time. The ring lives longer for them
        /// (frozen time, a bounce that sends a bullet back across the screen).
        /// </summary>
        public void ConfigureOps(TrajectoryOp[] ops, RicochetArea area)
        {
            // (no boss's op touches a train: frozen on its way, it would never get there, however loud its horn)
            if (Shape == BulletShape.Train)
                return;

            var list = new System.Collections.Generic.List<TrajectoryOp>();
            double frozen = 0;

            foreach (var op in ops)
            {
                // (what a freeze has stopped lives on for as long as it is stopped: the op that comes after the freeze still finds it. The ops come in the order of their times)
                if ((op.Time <= Time && !op.Window) || Time >= op.BornBefore || op.Time >= Time + Lifetime + frozen || (op.Window && Time > op.End))
                    continue;

                if (op.SkipBursts && Derive == DeriveKind.Explode)
                    continue;

                if (op.SkipSharp && Sharp)
                    continue;

                if (op.SkipPetals && Shape is BulletShape.Petal or BulletShape.Flower)
                    continue;



                if (op.Kind == TrajectoryOpKind.Remove && Sharp)
                    continue; // (Utsuho's big balls cannot be destroyed)

                list.Add(op);

                if (op.Kind == TrajectoryOpKind.Freeze)
                    frozen += op.End - op.Time;
            }

            if (list.Count == 0)
                return;

            // (the glass walls are checked by the field for the bullets that nothing else steers; a ring that other ops steer takes them into its own flight)
            if (list.TrueForAll(op => op.Kind == TrajectoryOpKind.Glass))
                return;

            // (an op with a window takes effect when a bullet reaches the top of the screen, say: it only needs the bullets to live a little longer, not until its window ends)
            double reach = 0;
            bool windowed = false;

            foreach (var op in list)
            {
                if (op.Window)
                    windowed = true;
                else
                    reach = Math.Max(reach, op.End - Time);
            }

            Ops = list.ToArray();
            OpsArea = area;
            Lifetime = Math.Max(Lifetime, reach) + Lifetime * 2 + frozen + 2000 + (windowed ? 6000 : 0);

            // (a ring that its ops only stop - and slide - flies straight away from its centre again once the last of them is over; one that leaves the top and falls as flowers
            // (Yuyuko) is gone once the flowers have had the time to fall through the screen: see GoneAfterFreezes)
            onlyFreezes = list.TrueForAll(op => (op.Kind == TrajectoryOpKind.Freeze && !op.Window) || op.Kind == TrajectoryOpKind.Fall);
            frozenTotal = frozen;
            freezesOver = double.NegativeInfinity;
            fallDelay = 0;

            foreach (var op in list)
            {
                if (op.Kind == TrajectoryOpKind.Fall)
                    fallDelay = Math.Max(fallDelay, (area.Max.Y - area.Min.Y) * 1.5 / Math.Max(op.FallSpeed, 1e-4f) + 500);
                else
                    freezesOver = Math.Max(freezesOver, op.End);
            }

            opsOver = Time;
            opsFall = false;
            removedAt = double.PositiveInfinity;

            foreach (var op in list)
            {
                opsOver = Math.Max(opsOver, Math.Max(op.Time, op.End));
                opsFall |= op.Kind == TrajectoryOpKind.Fall;

                // (what takes every second bullet does not take the ring: the trace takes those it takes. A ring that goes by its own clock goes as a whole, or stays as a whole)
                if (op.Kind == TrajectoryOpKind.Remove && (Homing || Ricochet ? !(op.OrEverySecondRing && secondRing) : !op.EveryOther))
                    removedAt = Math.Min(removedAt, op.Time);
            }
        }

        private bool onlyFreezes;
        private double frozenTotal, freezesOver, fallDelay;

        /// <summary>
        /// A ring that its ops only stop (and slide) is radial again once the last of them is over: its bullets have lost the frozen time and are at most the slide distance off
        /// their lines. Then (and only then) this says how far out its slowest bullet surely is (<paramref name="radius"/>), so the field can drop it once that is past the screen.
        /// </summary>
        public bool GoneAfterFreezes(double ringTime, out float radius)
        {
            radius = 0f;

            if (Ops == null || !onlyFreezes || ringTime < freezesOver || Ricochet || Falling || Homing || Origins != null || Deflections is { Count: > 0 })
                return false;

            float drift = 0f;

            foreach (var op in Ops)
            {
                if (op.Slides)
                    drift = Math.Max(drift, op.SlideDistance);
            }

            // (where it surely was a fall's time ago: what left the top then has fallen out by now)
            radius = SlowestRadiusAt(ringTime - frozenTotal - fallDelay) - drift;
            return true;
        }

        private double opsGoneAt = double.NaN;
        private double opsOver = double.NegativeInfinity; // (when the last op of the ring is over)
        private bool opsFall; // (one of its ops makes things fall as petals)
        private Vector2 opsGoneMin, opsGoneMax;

        /// <summary>
        /// A ring with ops of every kind (mirrored, frozen, bounced, turned...): once the last of its ops is over, every bullet flies straight on from where the ops have left it. The first time it is
        /// looked at after that, the moment its last bullet leaves the screen (<paramref name="screenMin"/>..<paramref name="screenMax"/>) is worked out once, from each bullet's place and way then;
        /// after that moment the ring is gone for good. (Before this, a ring with ops was kept for its whole stretched lifetime and every bullet traced through its ops every frame.)
        /// Not for rings that bounce, fall, are pulled, have been turned by a stage, or leave the top as petals (those do not fly straight afterwards).
        /// </summary>
        public bool GoneAfterOps(double time, Vector2 screenMin, Vector2 screenMax)
        {
            if (Ops == null || OpsArea == null || Ricochet || Falling || Homing || Deflections is { Count: > 0 } || opsFall)
                return false;

            double over = opsOver;

            if (time <= over)
                return false;

            if (double.IsNaN(opsGoneAt) || opsGoneMin != screenMin || opsGoneMax != screenMax)
            {
                opsGoneMin = screenMin;
                opsGoneMax = screenMax;
                opsGoneAt = over;

                Vector2 low = screenMin - new Vector2(VisualRadius * 2f), high = screenMax + new Vector2(VisualRadius * 2f);
                Vector2 limitMin = OpsArea.Min + new Vector2(VisualRadius);
                Vector2 limitMax = OpsArea.Max - new Vector2(VisualRadius);

                for (int b = 0; b < Count; b++)
                {
                    Vector2 d = Directions[b];
                    float scale = RadialScales == null ? 1f : RadialScales[b];
                    Vector2 spawn = Origins != null ? Origins[b] : Origin;

                    Trajectory.Trace(spawn + d * (scale * SpawnRadius), d * (Speed * scale), Vector2.Zero, Time, over, Ops, limitMin, limitMax, OpsArea.Min.Y, out TrajectoryState state, b);

                    if (!state.Alive)
                        continue;

                    opsGoneAt = Math.Max(opsGoneAt, over + leavesBox(state.Position, state.Velocity, low, high));
                }
            }

            return time > opsGoneAt;
        }

        /// <summary>Time went back: <see cref="GoneAfterOps"/> works it out again (the ring may be aimed another way).</summary>
        public void ForgetOpsGone() => opsGoneAt = double.NaN;

        /// <summary>How long something at <paramref name="p"/> flying with <paramref name="v"/> (straight) is still in the box (0 if it is not in it and never gets in; infinity if it never leaves).</summary>
        private static double leavesBox(Vector2 p, Vector2 v, Vector2 low, Vector2 high)
        {
            double enter = 0, leave = double.PositiveInfinity;

            for (int axis = 0; axis < 2; axis++)
            {
                float position = axis == 0 ? p.X : p.Y, speed = axis == 0 ? v.X : v.Y;
                float lo = axis == 0 ? low.X : low.Y, hi = axis == 0 ? high.X : high.Y;

                if (Math.Abs(speed) < 1e-9f)
                {
                    if (position < lo || position > hi)
                        return 0;

                    continue;
                }

                double a = (lo - position) / speed, b = (hi - position) / speed;
                enter = Math.Max(enter, Math.Min(a, b));
                leave = Math.Min(leave, Math.Max(a, b));
            }

            return leave < enter ? 0 : Math.Max(leave, 0);
        }

        /// <summary>Pulled bullets and bouncing ones do not follow <see cref="Trajectory"/>: they just stop while a freeze lasts. This takes the time of a freeze out of <paramref name="time"/>.</summary>
        public double WithoutFrozenTime(double time)
        {
            if (Ops == null || (!Homing && !Ricochet))
                return time;

            double removed = 0;

            foreach (var op in Ops)
            {
                if (op.Kind != TrajectoryOpKind.Freeze || time <= op.Time || (op.OrEverySecondRing && secondRing))
                    continue;

                removed += Math.Min(time, op.End) - op.Time;
            }

            return time - removed;
        }

        /// <summary>A pulled or bouncing ring that an op with <see cref="TrajectoryOp.OrEverySecondRing"/> leaves alone (by its time: the same every time).</summary>
        private bool secondRing => (Homing || Ricochet) && (long)Math.Abs(Time) % 2 == 1;

        /// <summary>Is this bullet one of those an every-second op leaves alone?</summary>
        public bool LeftAlone(int bullet) => Homing || Ricochet ? secondRing : bullet % 2 == 1;

        /// <summary>Has something taken the whole ring away by <paramref name="realTime"/> (it burst into other bullets)? The ops only touch a ring as a whole.</summary>
        public bool RemovedAt(double realTime) => (Ops != null && realTime > removedAt) || (RemoveAfter > 0 && realTime > Time + RemoveAfter);

        private double removedAt = double.PositiveInfinity; // (the first op that takes the ring away: worked out with the ops)

        /// <summary>The look of a pulled or bouncing bullet while a freeze is on at <paramref name="realTime"/> (null if there is none).</summary>
        public BulletLook? FreezeLookAt(double realTime)
        {
            if (Ops == null || (!Homing && !Ricochet))
                return null;

            foreach (var op in Ops)
            {
                if (op.Kind == TrajectoryOpKind.Freeze && realTime > op.Time && realTime <= op.End && !(op.OrEverySecondRing && secondRing))
                    return op.Slides ? BossOps.ReisenStill : op.During;
            }

            return null;
        }

        private ulong bouncedMask; // bit n: the (pulled) bullet n has bounced
        private readonly System.Collections.Generic.Dictionary<int, int> bounceWindowOf = new();

        private Vector2[]? homingPosition;
        private Vector2[]? homingVelocity;
        private Vector2[]? homingPrevious;
        private double homingTime = double.NaN;
        private Vector2 homingPlayer; // where the player was when the bullets were last stepped
        private ulong lockedMask; // bit n: bullet n has pointed at the player once and no longer turns
        private ulong calmMask; // bit n: bullet n has come close to the player once: straight line at the circle bullet speed
        private ulong goneMask; // bit n: bullet n has left the screen

        /// <summary>Forget the simulated state (time went backwards); it is built again from the moment the ring fired.</summary>
        public void ResetHoming()
        {
            homingTime = double.NaN;
            lockedMask = calmMask = goneMask = 0;
            stuckMask = 0;
            bouncedMask = 0;
            bounceWindowOf.Clear();
        }

        /// <summary>Every bullet of a pulled ring is gone (left the screen or hit the player), so the ring can be dropped.</summary>
        public bool HomingFinished
        {
            get
            {
                if (!Homing || Count == 0 || double.IsNaN(homingTime))
                    return false;

                ulong all = Count >= 64 ? ulong.MaxValue : (1UL << Count) - 1;
                return ((goneMask | ConsumedMask | AbsorbedMask) & all) == all;
            }
        }

        private void ensureHoming()
        {
            if (!double.IsNaN(homingTime))
                return;

            homingPosition = new Vector2[Count];
            homingVelocity = new Vector2[Count];
            homingPrevious = new Vector2[Count];
            wallBounces = new int[Count];
            wallSets = new int[Count];

            for (int b = 0; b < Count; b++)
            {
                float scale = RadialScales == null ? 1f : RadialScales[b];
                homingPosition[b] = homingPrevious[b] = (Origins != null ? Origins[b] : Origin) + Directions[b] * (SpawnRadius * scale);
                homingVelocity[b] = Directions[b] * (Speed * scale);
            }

            homingTime = Time;
            homingPlayerKnown = false;
        }

        private bool homingPlayerKnown;

        /// <summary>
        /// Does the segment <paramref name="a"/>→<paramref name="b"/> (the player's path relative to the bullet) touch the cone that opens from the bullet
        /// along <paramref name="heading"/> with a half angle whose tangent is <paramref name="tangent"/>? The cone is convex, so the path touches it
        /// exactly when an end is inside or the path crosses one of its two edges - this also catches a cursor that flicks through the cone between two steps.
        /// </summary>
        private static bool crossesCone(Vector2 a, Vector2 b, Vector2 heading, float tangent)
        {
            var side = new Vector2(-heading.Y, heading.X);
            var p = new Vector2(Vector2.Dot(a, heading), Vector2.Dot(a, side));
            var q = new Vector2(Vector2.Dot(b, heading), Vector2.Dot(b, side));

            if (inCone(p, tangent) || inCone(q, tangent))
                return true;

            // the edges are the rays (1, +tangent) and (1, -tangent) from the origin; w -/+ tangent*u changes sign along the path when it crosses the edge's line
            float pPlus = p.Y - tangent * p.X, qPlus = q.Y - tangent * q.X;
            float pMinus = p.Y + tangent * p.X, qMinus = q.Y + tangent * q.X;

            return crossesEdge(p, q, pPlus, qPlus) || crossesEdge(p, q, pMinus, qMinus);
        }

        private static float distanceToSegment(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 1e-9f)
                return a.Length;

            float t = Math.Clamp(-Vector2.Dot(a, ab) / lengthSquared, 0f, 1f);
            return (a + ab * t).Length;
        }

        private static bool inCone(Vector2 p, float tangent) => p.X > 0 && MathF.Abs(p.Y) <= p.X * tangent;

        private static bool crossesEdge(Vector2 p, Vector2 q, float pValue, float qValue)
        {
            if ((pValue > 0) == (qValue > 0))
                return false;

            // where the path meets the edge's line; it only counts on the edge itself (in front of the bullet), not on the mirrored half
            float t = pValue / (pValue - qValue);
            return p.X + (q.X - p.X) * t > 0;
        }

        /// <summary>
        /// Moves every bullet on (in fixed steps) up to <paramref name="target"/>: each step pulls the bullet towards <paramref name="player"/>
        /// with <see cref="Gravity"/>. After this call <see cref="PreviousPosition"/> is where the bullet was one step ago.
        /// </summary>
        public void StepHoming(double target, Vector2 player, bool sweepPlayer = true)
        {
            if (!Homing || Count == 0)
                return;

            ensureHoming();

            // a jump of the character (a freeze teleport) is not a path: nothing can be crossed on the way, only the new position counts
            if (!homingPlayerKnown || !sweepPlayer)
            {
                homingPlayer = player;
                homingPlayerKnown = true;
            }

            Vector2 playerFrom = homingPlayer;
            double frameStart = homingTime;
            double limit = Math.Min(target, Time + Lifetime);

            // normal frames are a few steps; after a seek the gap can be huge, then the steps get coarser (at most ~300 of them)
            double stepMs = Math.Max(DanmakusuBalance.HOMING_STEP_MS, (limit - homingTime) / 300);
            float step = (float)stepMs;
            bool moved = false;

            while (homingTime + stepMs <= limit)
            {
                moved = true;

                // the player moved from playerFrom to player over the whole call: this step sees the part of that path that falls into it
                double span = limit - frameStart;
                Vector2 stepFrom = span > 0 ? Vector2.Lerp(playerFrom, player, (float)((homingTime - frameStart) / span)) : player;
                Vector2 stepTo = span > 0 ? Vector2.Lerp(playerFrom, player, (float)((homingTime + stepMs - frameStart) / span)) : player;

                for (int b = 0; b < Count; b++)
                {
                    homingPrevious![b] = homingPosition![b];

                    ulong bit = 1UL << b;

                    if (((ConsumedMask | AbsorbedMask) & bit) != 0 || (goneMask & bit) != 0)
                        continue;

                    if ((stuckMask & bit) != 0)
                    {
                        // (lying at the border: it goes when its time is up)
                        if (stuckGone != null && homingTime + stepMs >= stuckGone[b])
                            goneMask |= bit;

                        continue;
                    }

                    {
                        Vector2 velocity = homingVelocity![b];
                        float speedSquared = velocity.LengthSquared;

                        if (speedSquared > 1e-12f)
                        {
                            Vector2 heading = velocity / MathF.Sqrt(speedSquared);

                            if ((calmMask & bit) == 0)
                            {
                                // the player's path during this step, seen from the bullet (which moves on straight ahead meanwhile)
                                Vector2 ahead = homingPosition[b] + velocity * step;
                                Vector2 relativeFrom = stepFrom - homingPosition[b];
                                Vector2 relativeTo = stepTo - ahead;

                                if (distanceToSegment(relativeFrom, relativeTo) <= ReleaseRadius)
                                {
                                    // close to the player: no more pull, no more acceleration, straight on at the circle bullet speed
                                    calmMask |= bit;
                                    lockedMask |= bit;
                                }
                                else if ((lockedMask & bit) == 0 && crossesCone(relativeFrom, relativeTo, heading, DanmakusuBalance.HOMING_AIMED_TANGENT))
                                {
                                    // the 1 degree cone in front of the bullet: a cursor that crosses it between two steps counts as pointed at too
                                    lockedMask |= bit;
                                }
                            }

                            if ((calmMask & bit) != 0)
                            {
                                homingVelocity[b] = heading * Speed;
                            }
                            else if ((lockedMask & bit) == 0)
                            {
                                // (a bullet that has pointed at the player once keeps its velocity: straight ahead, constant speed)
                                Vector2 toPlayer = stepFrom - homingPosition[b];
                                float length = toPlayer.Length;

                                if (length > 1e-3f)
                                {
                                    // only the part of the pull across the flight is used: it turns the bullet, and the speed stays exactly what it was
                                    float speed = MathF.Sqrt(speedSquared);
                                    Vector2 pull = toPlayer * (Gravity * step / length);
                                    Vector2 across = pull - heading * Vector2.Dot(pull, heading);
                                    Vector2 turned = velocity + across * DanmakusuBalance.HOMING_TURN_SCALE;
                                    float turnedLength = turned.Length;

                                    if (turnedLength > 1e-9f)
                                        homingVelocity[b] = turned * (speed / turnedLength);
                                }
                            }
                        }
                    }

                    homingPosition[b] += homingVelocity![b] * step;

                    if (Glass != null)
                        bounceOffGlass(b, homingTime, homingTime + stepMs);

                    if (Ops != null && OpsArea != null)
                        bounceHoming(b, bit, homingTime + stepMs);

                    if (Area != null)
                    {
                        Vector2 p = homingPosition[b];
                        float margin = VisualRadius;

                        if (p.X < Area.Min.X - margin || p.X > Area.Max.X + margin || p.Y < Area.Min.Y - margin || p.Y > Area.Max.Y + margin)
                            goneMask |= bit;
                    }
                }

                homingTime += stepMs;
            }

            if (!moved)
            {
                for (int b = 0; b < Count; b++)
                    homingPrevious![b] = homingPosition![b];
            }
            else
                homingPlayer = player;
        }

        /// <summary>A pulled bullet that touches one of Kyouko's glass walls in this step bounces off it (the way across the wall is turned round; the speed stays), twice at most.</summary>
        private void bounceOffGlass(int b, double from, double to)
        {
            Vector2 start = homingPrevious![b], end = homingPosition![b];

            if (!Glass!.Hit(from, to, start, end, VisualRadius * DanmakusuBalance.KYOUKO_CONTACT, out float fraction, out bool acrossX, out int set))
                return;

            // (once off every set of walls: new walls are new bounces)
            if (wallSets![b] != set)
            {
                wallSets[b] = set;
                wallBounces![b] = 0;
            }

            if (wallBounces![b] >= DanmakusuBalance.KYOUKO_BOUNCES)
                return;

            Vector2 velocity = homingVelocity![b];
            Vector2 turned = acrossX ? new Vector2(-velocity.X, velocity.Y) : new Vector2(velocity.X, -velocity.Y);

            homingPosition[b] = start + (end - start) * fraction + turned * (float)((1f - fraction) * (to - from)); // (it flies on from the wall for the rest of the step)
            homingVelocity[b] = turned;
            wallBounces[b]++;
        }

        /// <summary>A pulled bullet that touches the border of the screen during a bounce window turns round there (once per window).</summary>
        private void bounceHoming(int b, ulong bit, double time)
        {
            int window = -1;

            for (int i = 0; i < Ops!.Length; i++)
            {
                if (Ops[i].Kind == TrajectoryOpKind.Bounce && time > Ops[i].Time && time <= Ops[i].End)
                    window = i;
            }

            if (window < 0 || (bounceWindowOf.TryGetValue(b, out int done) && done == window))
                return;

            Vector2 min = OpsArea!.Min + new Vector2(VisualRadius);
            Vector2 max = OpsArea.Max - new Vector2(VisualRadius);
            Vector2 previous = homingPrevious![b];
            Vector2 p = homingPosition![b];

            // only a bullet that was inside the screen and crosses the border now bounces (one that was outside when the window began stays out)
            if (previous.X < min.X || previous.X > max.X || previous.Y < min.Y || previous.Y > max.Y)
                return;

            Vector2 velocity = homingVelocity![b];
            bool hit = false;

            if (p.X < min.X || p.X > max.X)
            {
                velocity.X = -velocity.X;
                hit = true;
            }

            if (p.Y < min.Y || p.Y > max.Y)
            {
                velocity.Y = -velocity.Y;
                hit = true;
            }

            if (!hit)
                return;

            homingVelocity[b] = velocity;
            homingPosition[b] = Vector2.Clamp(p, min, max);
            bouncedMask |= bit;
            bounceWindowOf[b] = window;

            // (it flashes where it bounced, as everything that bounces off the border does)
            var op = Ops[window];

            if (op.Bounces != null && op.Bounces.Count < 512)
                op.Bounces.TryAdd(((int)Math.Floor(time / 25), (int)MathF.Floor(homingPosition[b].X / 8f), (int)MathF.Floor(homingPosition[b].Y / 8f)), (time, homingPosition[b]));
        }

        /// <summary>Where a pulled bullet was one step ago (for the swept collision test).</summary>
        public Vector2 PreviousPosition(int bullet)
        {
            ensureHoming();
            return homingPrevious![bullet];
        }

        /// <summary>The velocity (pixels per ms) of a bullet at <paramref name="time"/>; <paramref name="direction"/> is what <see cref="Evaluate"/> gave.</summary>
        public Vector2 VelocityAt(int bullet, double time, Vector2 direction)
        {
            if (Homing)
            {
                ensureHoming();
                return homingVelocity![bullet];
            }

            if (Deflections != null && Deflections.TryGetValue(bullet, out var rested) && rested.Stuck && time >= rested.Time)
                return Vector2.Zero;

            if (!Falling)
                return direction * SpeedOf(bullet);

            // a falling bullet that a glass wall has turned: the way it was sent, and the fall goes on from there
            if (Deflections != null && Deflections.TryGetValue(bullet, out var turned) && time >= turned.Time)
                return turned.Direction * (turned.Speed > 0f ? turned.Speed : SpeedOf(bullet)) + GravityDirection * (Gravity * (float)(time - turned.Time));

            float age = (float)Math.Max(time - Time, 0);
            return Directions[bullet] * SpeedOf(bullet) + GravityDirection * (Gravity * age);
        }

        /// <summary>How far the farthest bullet of the ring is from the centre (ignoring bounces).</summary>
        public float RadiusAt(double time) => SpawnRadius + Speed * (float)FlownAge(time - Time);

        /// <summary>How far the closest bullet of the ring is from the centre (ignoring bounces).</summary>
        public float SlowestRadiusAt(double time) => MinRadialScale * RadiusAt(time);

        public float SpeedOf(int bullet) => RadialScales == null ? Speed : Speed * RadialScales[bullet];

        /// <summary>
        /// Everything about one bullet at one moment: where it is, which way it flies, and whether it still exists (a bouncing bullet is
        /// gone after its second wall). Pure function of time, like everything else here (except pulled arrowheads: those are wherever the simulation is).
        /// </summary>
        public void Evaluate(int bullet, double time, out Vector2 position, out Vector2 direction, out bool alive)
            => Evaluate(bullet, time, out position, out direction, out alive, out _);

        /// <summary>The same, and <paramref name="look"/> is how the bullet looks after what has happened to it (null = its own, see <see cref="TrajectoryOp"/>).</summary>
        public void Evaluate(int bullet, double time, out Vector2 position, out Vector2 direction, out bool alive, out BulletLook? look)
            => Evaluate(bullet, time, out position, out direction, out alive, out look, out _);

        /// <summary>
        /// The same, and <paramref name="stillUntil"/> says until when the bullet stays exactly like this (it is frozen and nothing about it changes until the freeze ends); NaN when that is not known.
        /// </summary>
        public void Evaluate(int bullet, double time, out Vector2 position, out Vector2 direction, out bool alive, out BulletLook? look, out double stillUntil)
        {
            evaluateOwn(bullet, time, out position, out direction, out alive, out look, out stillUntil);

            // (Mugetsu's vortex turns what the bullet does on its own round her)
            if (Swirl != null && !Homing)
            {
                Vector2 d = Directions[bullet];
                float scale = RadialScales == null ? 1f : RadialScales[bullet];
                Vector2 spawn = Origins != null ? Origins[bullet] : Origin;

                if (Swirl.Apply(Time, spawn + d * (scale * SpawnRadius), d * (Speed * scale), bullet, time, ref position, ref direction, SwirlCentre, SwirlCentreKnown))
                    stillUntil = double.NaN;
            }
        }

        private void evaluateOwn(int bullet, double time, out Vector2 position, out Vector2 direction, out bool alive, out BulletLook? look, out double stillUntil)
        {
            stillUntil = double.NaN;
            Vector2 d = Directions[bullet];
            float scale = RadialScales == null ? 1f : RadialScales[bullet];
            look = null;

            if (Homing)
            {
                // wherever the simulation has got to (the field steps it every frame); the bullet faces the way it moves
                ensureHoming();
                position = homingPosition![bullet];
                Vector2 velocity = homingVelocity![bullet];
                direction = velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : d;
                alive = (goneMask & (1UL << bullet)) == 0;

                if ((bouncedMask & (1UL << bullet)) != 0 && Ops != null)
                {
                    foreach (var op in Ops)
                    {
                        if (op.Kind == TrajectoryOpKind.Bounce)
                        {
                            look = op.After;
                            break;
                        }
                    }
                }

                return;
            }

            Vector2 spawn = Origins != null ? Origins[bullet] : Origin;

            if (!Ricochet && Ops != null && OpsArea != null)
            {
                // (one that lies still in Reimu's pile lies there, whatever the ops would have done to it from then on)
                if (Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck && time >= lying.Time)
                {
                    position = lying.Position;
                    direction = lying.Direction;
                    alive = time < lying.Gone;
                    return;
                }

                // flies straight (or falls) and the ops of the ring act on it: frozen, mirrored, bounced off the screen...
                Vector2 limitMin = OpsArea.Min + new Vector2(VisualRadius);
                Vector2 limitMax = OpsArea.Max - new Vector2(VisualRadius);
                Vector2 startPosition = spawn + d * (scale * SpawnRadius);
                Vector2 velocity0 = d * (Speed * scale);
                Vector2 pull = Falling ? GravityDirection * Gravity : Vector2.Zero;

                Trajectory.Trace(startPosition, velocity0, pull, Time, Math.Max(time, Time), Ops, limitMin, limitMax, OpsArea.Min.Y, out TrajectoryState state, bullet);
                position = state.Position;
                alive = state.Alive;
                look = state.Look;
                direction = state.Facing.LengthSquared > 1e-12f ? state.Facing : state.Velocity.LengthSquared > 1e-12f ? Vector2.Normalize(state.Velocity) : d;
                stillUntil = time >= Time ? state.StillUntil : double.NaN;
                return;
            }

            if (!Ricochet)
            {
                // (a curling bullet's way turns as it flies: it faces along its spiral, roughly outwards and a little round)
                Vector2 way = curled(d, bullet, time - Time);
                float reach = scale * RadiusAt(time);

                if (SlowsToWall)
                    reach = slowedReach(spawn, way, reach);

                position = spawn + way * reach;

                if (WaveSize > 0f)
                    position += new Vector2(-way.Y, way.X) * (WaveSize * MathF.Sin((float)(time - Time) * WaveRate) * (bullet % 2 == 0 ? 1f : -1f));
                direction = d;

                if (Curl != 0f)
                {
                    // (its velocity: outwards at its speed, and round at its share of that)
                    Vector2 round = new Vector2(-way.Y, way.X) * (curlOf(bullet) * Speed * scale);
                    Vector2 moving = way * (Speed * scale) + round;
                    direction = moving.LengthSquared > 1e-12f ? Vector2.Normalize(moving) : way;
                }

                alive = true;

                if (WrapsOnce)
                    position = wrappedOnce(spawn, way, reach, out alive);

                // a bullet that something has turned flies on from where it was turned (a falling one, which only Kyouko's glass walls turn, keeps falling)
                if (Deflections != null && Deflections.TryGetValue(bullet, out var turned) && time >= turned.Time)
                {
                    if (turned.Stuck)
                    {
                        position = turned.Position;
                        direction = turned.Direction;
                        alive = time < turned.Gone;
                        return;
                    }

                    // (a bullet that has been dropped stands still while another boss freezes everything)
                    double elapsedMs = time - turned.Time;
                    BulletLook? standingLook = null;

                    if (turned.Gravity > 0f && fallFreezes != null)
                        elapsedMs -= frozenBetween(turned.Time, time, out standingLook);

                    float elapsed = (float)elapsedMs;
                    float flown = turned.Speed > 0f ? turned.Speed : Speed * scale;
                    position = turned.Position + turned.Direction * (flown * elapsed);
                    direction = turned.Direction;

                    if (turned.Gravity > 0f)
                    {
                        // (it has been dropped and falls freely from where it was turned: Toyohime's bullets that left the top of the screen)
                        position.Y += 0.5f * turned.Gravity * elapsed * elapsed;

                        Vector2 dropping = turned.Direction * flown + new Vector2(0f, turned.Gravity * elapsed);
                        direction = dropping.LengthSquared > 1e-12f ? Vector2.Normalize(dropping) : turned.Direction;
                    }

                    if (Falling)
                    {
                        position += GravityDirection * (0.5f * Gravity * elapsed * elapsed);

                        Vector2 velocity = turned.Direction * flown + GravityDirection * (Gravity * elapsed);
                        direction = velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : turned.Direction;
                    }

                    look = standingLook;
                    return;
                }

                if (Falling)
                {
                    // the flight is a straight line plus the fall; the bullet faces the way it moves
                    float age = (float)Math.Max(time - Time, 0);
                    position += GravityDirection * (0.5f * Gravity * age * age);

                    Vector2 velocity = d * (Speed * scale) + GravityDirection * (Gravity * age);
                    direction = velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : d;
                }

                return;
            }

            // unfold the bounces: the bullet flies straight through mirrored copies of the area, and the copy it is in tells how
            // many walls it has touched and which way it is mirrored. The bullet bounces when its centre reaches the wall.
            var area = Area ?? new RicochetArea();
            Vector2 min = area.Min;
            Vector2 size = Vector2.ComponentMax(area.Max - min, new Vector2(1f));

            // a bullet that something has pushed flies on from where it was pushed, in the direction it was given, and keeps the ricochets it had left
            Vector2 flyFrom = spawn + d * (SpawnRadius * scale);
            float travelled = Speed * scale;
            double flightAge = time - Time;
            int maxBounces = bounceLimit;

            if (Deflections != null && Deflections.TryGetValue(bullet, out var pushed) && time >= pushed.Time)
            {
                if (pushed.Stuck)
                {
                    position = pushed.Position;
                    direction = pushed.Direction;
                    alive = time < pushed.Gone;
                    return;
                }

                flyFrom = pushed.Position;
                d = pushed.Direction;
                travelled = pushed.Speed > 0f ? pushed.Speed : travelled;
                flightAge = time - pushed.Time;
                maxBounces = pushed.BouncesLeft;
            }

            Vector2 start = Vector2.Clamp(flyFrom, min, min + size) - min;

            // when the bullet touches the wall after its last bounce it does not bounce again: it flies on through the wall, out of the screen
            double passAge = 0;
            int bouncesX = 0, bouncesY = 0, nextX = 1, nextY = 1;
            float vx = d.X * travelled, vy = d.Y * travelled;

            for (int i = 0; i <= maxBounces; i++)
            {
                double tx = wallAge(start.X, vx, size.X, nextX), ty = wallAge(start.Y, vy, size.Y, nextY);
                bool onX = tx <= ty;
                passAge = onX ? tx : ty;

                if (i < maxBounces)
                {
                    if (onX)
                        bouncesX++;
                    else
                        bouncesY++;
                }

                if (onX)
                    nextX++;
                else
                    nextY++;
            }

            if (flightAge < passAge)
            {
                Vector2 u = start + d * (travelled * (float)flightAge);

                int wallsX = walls(u.X, size.X);
                int wallsY = walls(u.Y, size.Y);

                position = min + new Vector2(fold(u.X, size.X), fold(u.Y, size.Y));
                direction = new Vector2((wallsX & 1) != 0 ? -d.X : d.X, (wallsY & 1) != 0 ? -d.Y : d.Y);
                alive = true;
                return;
            }

            // (the wall it passes through: where it touched it, and on in the direction it was flying in)
            Vector2 atWall = start + d * (travelled * (float)passAge);
            direction = new Vector2((bouncesX & 1) != 0 ? -d.X : d.X, (bouncesY & 1) != 0 ? -d.Y : d.Y);
            position = min + new Vector2(fold(atWall.X, size.X), fold(atWall.Y, size.Y)) + direction * (travelled * (float)(flightAge - passAge));

            // it is gone once it has left the screen altogether
            float margin = VisualRadius * 3f;
            alive = position.X > area.Min.X - margin && position.X < area.Max.X + margin && position.Y > area.Min.Y - margin && position.Y < area.Max.Y + margin;
        }

        /// <summary>How many ricochets a bullet of a ricocheting ring has left at <paramref name="time"/> (a bullet that was pushed keeps what it had left: being pushed is not a ricochet).</summary>
        public int RicochetsLeft(int bullet, double time)
        {
            Vector2 d = Directions[bullet];
            float scale = RadialScales == null ? 1f : RadialScales[bullet];
            var area = Area ?? new RicochetArea();
            Vector2 min = area.Min;
            Vector2 size = Vector2.ComponentMax(area.Max - min, new Vector2(1f));

            Vector2 flyFrom = (Origins != null ? Origins[bullet] : Origin) + d * (SpawnRadius * scale);
            float travelled = Speed * scale;
            double age = time - Time;
            int max = bounceLimit;

            if (Deflections != null && Deflections.TryGetValue(bullet, out var pushed) && time >= pushed.Time)
            {
                flyFrom = pushed.Position;
                d = pushed.Direction;
                travelled = pushed.Speed > 0f ? pushed.Speed : travelled;
                age = time - pushed.Time;
                max = pushed.BouncesLeft;
            }

            Vector2 start = Vector2.Clamp(flyFrom, min, min + size) - min;
            float vx = d.X * travelled, vy = d.Y * travelled;
            int made = 0, nextX = 1, nextY = 1;

            for (int i = 0; i < max; i++)
            {
                double tx = wallAge(start.X, vx, size.X, nextX), ty = wallAge(start.Y, vy, size.Y, nextY);
                bool onX = tx <= ty;

                if ((onX ? tx : ty) > age)
                    break;

                made++;

                if (onX)
                    nextX++;
                else
                    nextY++;
            }

            return Math.Max(0, max - made);
        }

        /// <summary>The moments (on the ring's clock) between <paramref name="from"/> and <paramref name="to"/> when a bullet of a ricocheting ring changes its way: it touches a wall, or something pushes it. Between them it flies straight.</summary>
        public void TurnsBetween(int bullet, double from, double to, List<double> into)
        {
            Vector2 d = Directions[bullet];
            float scale = RadialScales == null ? 1f : RadialScales[bullet];
            Vector2 flyFrom = (Origins != null ? Origins[bullet] : Origin) + d * (SpawnRadius * scale);
            float travelled = Speed * scale;

            if (Deflections != null && Deflections.TryGetValue(bullet, out var pushed))
            {
                if (Ricochet)
                    wallsBetween(flyFrom, d, travelled, Time, bounceLimit, from, Math.Min(to, pushed.Time), into);

                if (pushed.Time > from && pushed.Time < to)
                    into.Add(pushed.Time);

                if (Ricochet && !pushed.Stuck)
                    wallsBetween(pushed.Position, pushed.Direction, pushed.Speed > 0f ? pushed.Speed : travelled, pushed.Time, pushed.BouncesLeft, from, to, into);

                return;
            }

            if (Ricochet)
                wallsBetween(flyFrom, d, travelled, Time, bounceLimit, from, to, into);
        }

        /// <summary>When a bullet that starts at <paramref name="flyFrom"/> at <paramref name="start"/> touches the walls it bounces off, from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private void wallsBetween(Vector2 flyFrom, Vector2 d, float travelled, double start, int max, double from, double to, List<double> into)
        {
            var area = Area ?? new RicochetArea();
            Vector2 min = area.Min;
            Vector2 size = Vector2.ComponentMax(area.Max - min, new Vector2(1f));
            Vector2 inside = Vector2.Clamp(flyFrom, min, min + size) - min;
            float vx = d.X * travelled, vy = d.Y * travelled;
            int nextX = 1, nextY = 1;

            for (int i = 0; i < max; i++)
            {
                double tx = wallAge(inside.X, vx, size.X, nextX), ty = wallAge(inside.Y, vy, size.Y, nextY);
                bool onX = tx <= ty;
                double at = start + (onX ? tx : ty);

                if (at >= to)
                    break;

                if (at > from)
                    into.Add(at);

                if (onX)
                    nextX++;
                else
                    nextY++;
            }
        }

        /// <summary>Has this bullet of a ricocheting ring bounced by <paramref name="time"/>?</summary>
        public bool HasRicocheted(int bullet, double time) => Ricochet && RicochetsLeft(bullet, time) < (Deflections != null && Deflections.TryGetValue(bullet, out var pushed) && time >= pushed.Time ? pushed.BouncesLeft : bounceLimit);

        /// <summary>After how many ms the bullet (at <paramref name="start"/> in 0..size, flying at <paramref name="velocity"/>) touches the wall of this axis for the <paramref name="k"/>th time (infinity if never).</summary>
        private static double wallAge(float start, float velocity, float size, int k)
        {
            if (Math.Abs(velocity) < 1e-9f)
                return double.PositiveInfinity;

            return velocity > 0 ? (k * size - start) / velocity : ((k - 1) * size + start) / -velocity;
        }

        /// <summary>
        /// The way a star bullet is turned at <paramref name="time"/>: the direction it was fired in, turned further and further with time.
        /// It does not depend on bounces, so a star keeps spinning smoothly when it ricochets. The hitbox uses the same orientation.
        /// </summary>
        public Vector2 Orientation(int bullet, double time)
        {
            // (a bullet that lies still does not spin)
            if (Deflections != null && Deflections.TryGetValue(bullet, out var lying) && lying.Stuck && time >= lying.Time)
                time = lying.Time;

            double angle = DanmakusuBalance.STAR_SPIN_RATE * (time - Time);
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            Vector2 d = Directions[bullet];

            return new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
        }

        /// <summary>How many walls a bullet that is at unfolded coordinate <paramref name="u"/> has touched along this axis.</summary>
        private static int walls(float u, float size)
        {
            int copy = (int)MathF.Floor(u / size);
            return u >= 0 ? copy : -copy;
        }

        /// <summary>Folds an unfolded coordinate back into 0..size (a triangle wave).</summary>
        private static float fold(float u, float size)
        {
            float m = u % (2 * size);
            if (m < 0)
                m += 2 * size;

            return m <= size ? m : 2 * size - m;
        }
    }
}
