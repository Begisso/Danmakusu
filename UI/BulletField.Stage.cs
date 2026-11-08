using System;
using System.Collections.Generic;
using System.Diagnostics;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    // The stages of Hecatia (three worlds), Nue (UFOs), Raiko (taikos), Yuyuko (a tree) and Utsuho (a black hole): things that stand on the playfield besides the bullets.
    // This file is what they share; every boss has an "act" for each of her attacks (or pauses), in its own file. An act is a pure function of time as far as that is possible; what is
    // not (the planets, the chains, what a UFO or the black hole has taken in) is simulated from a fixed start and thrown away when time goes backwards, so a replay and a seek give the same.
    public partial class BulletField
    {
        private const int stage_hit = -3;

        /// <summary>A bullet of a stage: a closed form of time, either a straight line or (Utsuho's galactic bullets) an ellipse round the black hole that is left in a straight line.</summary>
#pragma warning disable CS0649 // (fields that only some acts set)
        private struct StageBullet
        {
            public double T0;
            public Vector2 P0;
            public Vector2 V;
            public float Radius;
            public float Hit;
            public int Style;
            public BulletShape Shape;
            public int Tint;
            public float TintAmount;
            public float Shade;

            /// <summary>After this it is gone for good (it has left the screen).</summary>
            public double Until;

            // an ellipse (when A > 0): half axes, the angle of the long one, radians per ms, where on it the bullet starts, its centre, and when it is let go
            public float A;
            public float B;
            public float Tilt;
            public float Omega;
            public float Phase;
            public Vector2 Centre;
            public double Release;

            /// <summary>When above 0 the bullet grows from nothing to its size over this long (ms) from <see cref="T0"/> (Mokou's big circle).</summary>
            public double Grow;

            /// <summary>When above 0 the bullet shrinks to nothing over this long (ms) before <see cref="Until"/>.</summary>
            public double Fade;

            /// <summary>The bullet hurts like a slider body (every so often for as long as the character is in it) instead of being used up by a hit (Mokou's big circle): the act's <c>Hurts</c> does it.</summary>
            public bool Slider;

            /// <summary>The bullet is only to look at: it cannot hit the player (a bullet that is being pulled into a UFO).</summary>
            public bool Harmless;

            /// <summary>The ops of the bosses do not act on it: it flies its own straight line (what Utsuho's rays shoot out, which her pull would turn straight back into the hole).</summary>
            public bool Untraced;

            /// <summary>When above 1 the bullet is drawn this many times as long along its way and thinner across it (Utsuho's rays: spaghettified).</summary>
            public float Elongate;

            /// <summary>
            /// Until this moment the bullet cannot hit the player, and is drawn only <see cref="GhostVisible"/> visible (it turns solid over 200 ms after it): Patchouli's water (the clone a ripple
            /// makes of what it catches).
            /// </summary>
            public double GhostUntil;

            public float GhostVisible;

            /// <summary>The act draws the bullet itself (as a picture of its own): the field does not draw it, but it is still there to look at and to hit.</summary>
            public bool Hidden;

            /// <summary>A hidden bullet (the act draws it) that the ops of the other bosses still act on, like on a plain bullet: it bounces, freezes... where they say (Meiling's big ball).</summary>
            public bool Ops;

            /// <summary>The bullet is simulated by its act: where it was every <see cref="TrailStep"/> ms from <see cref="T0"/> on (Nazrin's arrowheads, which are pulled towards the player).</summary>
            public List<Vector2>? Trail;

            public double TrailStep;

            /// <summary>A constant pull (px per ms squared) on top of the straight line: a falling bullet (an ellipse that Nazrin has made).</summary>
            public Vector2 Acc;

            /// <summary>0 = no bloom; otherwise the bullet glows (see <see cref="BulletInstance.Bloom"/>).</summary>
            public float Bloom;

            /// <summary>Nothing dims it: neither the dark places of the acts nor the beams (Tojiko's rain, and what her lightning throws back).</summary>
            public bool Undimmed;

            /// <summary>It is drawn under Tojiko's clouds (her rain, which falls out of them).</summary>
            public bool UnderClouds;

            /// <summary>When above 0 the bullet falls freely from this moment on (like when the player dies), and darkens towards <see cref="DarkTo"/> over <see cref="DarkenOver"/> ms (Zanmu's).</summary>
            public double FallAt;

            /// <summary>A knife with more than one blade (0 = a plain knife): it keeps its blades, turned <see cref="BladeAngle0"/> at <see cref="T0"/> and turning on at <see cref="BladeSpin"/> radians per ms (slowing down with the bullet when it stops).</summary>
            public int Blades;

            public float BladeSpin;
            public float BladeAngle0;

            /// <summary>The way a bullet that falls (see <see cref="FallAt"/>) is pulled: towards the player, as it was when the fall began (zero = straight down).</summary>
            public Vector2 FallDir;

            /// <summary>When <see cref="WrapHeight"/> is above 0, a falling bullet that gets below <see cref="WrapTop"/> + <see cref="WrapHeight"/> comes in from <see cref="WrapTop"/> again (once).</summary>
            public float WrapTop;

            public float WrapHeight;

            public double DarkenOver;
            public float DarkTo;

            /// <summary>When above 0 the bullet shakes (1-2 px) from this moment until <see cref="ShakeTo"/> (Zanmu's, before they fall).</summary>
            public double ShakeFrom;

            public double ShakeTo;

            /// <summary>When above 0 the bullet slows down evenly to a stop over this long from <see cref="T0"/> (its <see cref="V"/> and <see cref="Acc"/> are made for it), and faces <see cref="Face"/> once it has stopped.</summary>
            public double StopAfter;

            public Vector2 Face;

            /// <summary>The bullet is drawn turned <see cref="Face"/> all along, whichever way it moves (Yuyuko's petals that fall straight down keep the angle they had on the flower).</summary>
            public bool KeepFace;

            /// <summary>A picture in the bullet's body; when <see cref="FadeIn"/> is above 0 the tint and the picture fade in over that long (ms) from <see cref="T0"/>.</summary>
            public BulletDecal Decal;

            public double FadeIn;

            /// <summary>When above 0 the bullet changes its way at this moment: from then on it moves with <see cref="V2"/> and <see cref="Acc2"/> from where it has got to (Patchouli's: a bullet that has been slowed down gets its speed back, a frozen one is let go).</summary>
            public double Switch;

            public Vector2 V2;
            public Vector2 Acc2;

            /// <summary>When above 0 the bullet turns at a steady rate (<see cref="TurnOmega"/> radians per ms) for this long from <see cref="T0"/>, from the way <see cref="V"/> points, and then flies straight on (Satori's).</summary>
            public double TurnT;

            public float TurnOmega;

            /// <summary>When <see cref="ScaleOver"/> is above 0 the bullet (and its hitbox) goes from its own size to <see cref="ScaleTo"/> times that, over <see cref="ScaleOver"/> ms from <see cref="ScaleFrom"/> (Patchouli's fire).</summary>
            public double ScaleFrom;

            public double ScaleOver;
            public float ScaleTo;

            /// <summary>When <see cref="ScaleBackOver"/> is above 0 the size that <see cref="ScaleTo"/> gave goes back to the bullet's own over that long from <see cref="ScaleBackAt"/> (Patchouli's water).</summary>
            public double ScaleBackAt;

            public double ScaleBackOver;

            /// <summary>When <see cref="AlphaOver"/> is above 0 the bullet fades out to nothing over that long from <see cref="AlphaFrom"/>.</summary>
            public double AlphaFrom;

            public double AlphaOver;

            /// <summary>When above 0 the bullet fades in over this long (ms) from <see cref="T0"/> (Shizuha's leaves on the floor).</summary>
            public double AppearOver;

            /// <summary>When <see cref="Tint2At"/> is above 0 the bullet is shown in tint <see cref="Tint2"/> from then on (0 = its own colour), the amount going from what it was to <see cref="Tint2Amount"/> over <see cref="Tint2Over"/> ms.</summary>
            public double Tint2At;

            public int Tint2;
            public float Tint2Amount;
            public double Tint2Over;

            /// <summary>When above 0 the bullet starts to bloom (up to <see cref="BloomTo"/>) at this moment.</summary>
            public double BloomAt;

            public float BloomTo;

            /// <summary>A talisman is drawn over the bullet (only to look at), facing the middle of the playfield, fitted into the body (Seiga Kaku's).</summary>
            public bool Talisman;

            /// <summary>Urumi's pushes (from push number <see cref="PushFrom"/> on) are added to its straight flight; while one is announced it shakes.</summary>
            public PushPlan? Pushes;

            public int PushFrom;

            /// <summary>When above 0 the bullet wanders round its straight line, about this many px to each side, as a firefly does; <see cref="WanderPhase"/> makes it its own (Wriggle's).</summary>
            public float Wander;

            public float WanderPhase;

            /// <summary>When above 0 its wandering goes this many times as fast (Wriggle's fireflies).</summary>
            public float WanderPace;

            /// <summary>When above 0 the pull of <see cref="FallAt"/> is this share of Zanmu's (Shizuha's leaves).</summary>
            public float FallScale;

            /// <summary>A knife: its handle is blue (Chimata's).</summary>
            public bool BlueHandle;

            /// <summary>When not 0 the bullet is drawn turning at this many radians per ms, from <see cref="SpinPhase"/>, whichever way it moves (Shizuha's leaves); after <see cref="FallAt"/> at <see cref="FallSpin"/> instead.</summary>
            public float Spin;

            public float SpinPhase;
            public float FallSpin;
        }
#pragma warning restore CS0649

        /// <summary>The way something that is about to be let go shakes: 1-2 px in a direction that changes every few ms (a pure function of time, <paramref name="a"/> and <paramref name="b"/> tell one thing from another).</summary>
        private static Vector2 shakeOffset(int a, int b, double t)
        {
            int bucket = (int)Math.Floor(t / DanmakusuBalance.FROZEN_SHAKE_STEP_MS);
            float angle = hash01(a, b, bucket) * MathF.Tau;
            float amount = DanmakusuBalance.FROZEN_SHAKE_MIN_PX + (DanmakusuBalance.FROZEN_SHAKE_MAX_PX - DanmakusuBalance.FROZEN_SHAKE_MIN_PX) * hash01(b, a, bucket + 3);

            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * amount;
        }

        /// <summary>A round place where the bullets are drawn darker (<see cref="Strength"/> 0..1 as in <see cref="BulletInstance.Shade"/>), fully so inside <see cref="Radius"/>, fading out to twice that.</summary>
        private struct StageDim
        {
            public Vector2 Centre;
            public float Radius;
            public float Strength;

            /// <summary>It is the other way round: everything outside <see cref="Radius"/> is dim, and nothing inside (Eiki's circle).</summary>
            public bool Outside;

            /// <summary>With <see cref="Outside"/>: nothing inside a second circle as big round this is dim either (Eiki's mirrored one).</summary>
            public Vector2? Second;
        }

        private readonly List<StageDim> stageDims = new List<StageDim>();

        /// <summary>Darkens the bullets (the ones already in the buffer) that are in or near a dark place of an act.</summary>
        private void applyStageDims(double drawTime, int count)
        {
            if (stageActs.Count == 0)
                return;

            stageDims.Clear();

            foreach (var act in stageActs)
                act.Dims(this, drawTime, stageDims);

            if (stageDims.Count == 0)
                return;

            foreach (var dim in stageDims)
            {
                if (dim.Outside)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!instanceBuffer[i].Undimmed && dim.Strength > instanceBuffer[i].Shade && Vector2.DistanceSquared(instanceBuffer[i].Position, dim.Centre) > dim.Radius * dim.Radius
                            && (dim.Second is not Vector2 second || Vector2.DistanceSquared(instanceBuffer[i].Position, second) > dim.Radius * dim.Radius))
                            instanceBuffer[i].Shade = dim.Strength;
                    }

                    continue;
                }

                float outer = dim.Radius * DanmakusuBalance.MOKOU_DIM_REACH;

                if (outer < 1f)
                    continue;

                for (int i = 0; i < count; i++)
                {
                    float away = Vector2.Distance(instanceBuffer[i].Position, dim.Centre);

                    if (away >= outer || instanceBuffer[i].Undimmed)
                        continue;

                    float level = dim.Strength * (away <= dim.Radius ? 1f : 1f - smoothStep((away - dim.Radius) / Math.Max(outer - dim.Radius, 1f)));

                    if (level > instanceBuffer[i].Shade)
                        instanceBuffer[i].Shade = level;
                }
            }
        }

        /// <summary>A bullet that a UFO or the black hole has taken in: what it looked like, and how fast it flew.</summary>
        private struct StoredBullet
        {
            public double Time;
            public float Speed;
            public int Style;
            public BulletShape Shape;
            public float Radius;
            public float Hit;
            public int Tint;
            public float TintAmount;
            public float Shade;
            public int Blades;
            public float BladeSpin;
        }

        /// <summary>One bead of a chain (a circle bullet) in the frame that is being drawn.</summary>
        private struct StageBead
        {
            public Vector2 Position;
            public float Radius;
            public float Hit;
            public float Fade;
            public int Style;

            // (a bullet that Hecatia's worlds or Unzan's hands have gathered: it keeps its own look, faces its way, and cannot hit)
            public BulletShape Shape;
            public Vector2 Direction;
            public int Overlay;
            public float OverlayAmount;
            public bool Harmless;
        }

        /// <summary>One slam of a taiko: the ripple is a circle that grows from <see cref="Centre"/> from <see cref="Time"/> on (it does not bounce).</summary>
        private struct Ripple
        {
            public Vector2 Centre;
            public double Time;
        }

        /// <summary>One of Rumia's invisible circles: what is inside cannot be seen.</summary>
        private struct HideCircle
        {
            public Vector2 Centre;
            public float Radius;
            public float Presence;
        }

        private struct SmokeCloud
        {
            public Vector2 Centre;
            public float Radius;
            public float Presence;
        }

        /// <summary>Where a traced stage bullet was at <see cref="Time"/>, how fast it went and how it looked (NaN time = not known).</summary>
        private struct TracedAt
        {
            public double Time;
            public bool Alive;
            public Vector2 Position;
            public Vector2 Velocity;
            public BulletLook? Look;
            public CarryMark Carried;
        }

        /// <summary>The traced place of stage bullet <paramref name="index"/> of <paramref name="act"/> at <paramref name="t"/>: from what was kept if it was worked out for that very moment, otherwise traced (and kept).</summary>
        private bool tracedStageBullet(StageAct act, int index, in StageBullet bullet, double t, float radius, out Vector2 position, out Vector2 velocity, out BulletLook? look)
        {
            if (act.Traced.Length < act.Bullets.Count)
            {
                int old = act.Traced.Length;
                Array.Resize(ref act.Traced, Math.Max(act.Bullets.Count, old * 2));

                for (int i = old; i < act.Traced.Length; i++)
                    act.Traced[i].Time = double.NaN;
            }

            ref var kept = ref act.Traced[index];

            if (kept.Time == t)
            {
                position = kept.Position;
                velocity = kept.Velocity;
                look = kept.Look;
                return kept.Alive;
            }

            CarryMark carried = kept.Carried;
            bool alive = flyTraced(bullet.P0, bullet.V, bullet.Acc, bullet.T0, bullet.Until, t, radius, out position, out velocity, out look, ref carried);
            kept = new TracedAt { Time = t, Alive = alive, Position = position, Velocity = velocity, Look = look, Carried = carried };
            return alive;
        }

        /// <summary>Is a traced stage bullet surely gone at <paramref name="t"/>? (Its own clock is past its life plus every freeze there is: no op keeps it longer than that.)</summary>
        private bool tracedStageBulletGone(in StageBullet bullet, double t, double allFreezes)
            => effects.Effective(bullet.T0, t) > bullet.Until + allFreezes;

        private abstract class StageAct
        {
            public int Id;
            public BossPeriod Period = null!;
            public double Start;
            public double End;

            /// <summary>While <see cref="Absorbs"/> is true the act takes bullets away.</summary>
            public double AbsorbFrom = double.PositiveInfinity;

            public double AbsorbTo = double.NegativeInfinity;

            public readonly List<StageBullet> Bullets = new List<StageBullet>();
            public double FirstT0 = double.PositiveInfinity;
            public double LastUntil = double.NegativeInfinity;

            public virtual bool Absorbs(double t) => t >= AbsorbFrom && t <= AbsorbTo;

            /// <summary>Does the act take (or push) bullets at some moment between <paramref name="from"/> and <paramref name="to"/>?</summary>
            public virtual bool AbsorbsBetween(double from, double to) => AbsorbFrom <= to && AbsorbTo >= from;

            public void AddBullet(StageBullet bullet)
            {
                if (Bullets.Count >= stage_bullet_cap_per_act)
                    return;

                // (nothing is smaller than this, whatever has been done to it)
                float least = Period != null ? Period.StarRadius / (DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR) * DanmakusuBalance.BULLET_SMALLEST : 0f;

                if (bullet.Radius > 0f && bullet.Radius < least)
                {
                    bullet.Hit *= least / bullet.Radius;
                    bullet.Radius = least;
                }

                // (nothing stays longer than 64 beats, whatever slowed it or keeps it on the screen: it fades out over the last half second. LIFE_MOST_BEATS: it took the place of "what crawls fades". What leaves
                // the screen is gone when it has left it, see drawStageBullets)
                double most = bullet.T0 + DanmakusuBalance.LIFE_MOST_BEATS * Math.Max(Period?.BeatLength ?? 500, 1);

                if (bullet.Until > most)
                {
                    bullet.Until = most;

                    if (bullet.AlphaOver <= 0 || bullet.AlphaFrom + bullet.AlphaOver > most)
                    {
                        bullet.AlphaFrom = most - DanmakusuBalance.LIFE_FADE_MS;
                        bullet.AlphaOver = DanmakusuBalance.LIFE_FADE_MS;
                    }
                }

                Bullets.Add(bullet);
                FirstT0 = Math.Min(FirstT0, bullet.T0);
                LastUntil = Math.Max(LastUntil, bullet.Until);
            }

            /// <summary>
            /// (for the stage bullets that the ops of the other bosses steer, which cost a trace each: where each was when the hit check last worked it out, at <see cref="TracedAt.Time"/>. The next hit check
            /// starts from there and the renderer draws it, instead of tracing it again)
            /// </summary>
            public TracedAt[] Traced = [];

            public void ClearBullets()
            {
                Traced = [];
                Bullets.Clear();
                FirstT0 = double.PositiveInfinity;
                LastUntil = double.NegativeInfinity;
                DrawFrom = HitFrom = 0;
            }

            /// <summary>
            /// Where the field's loops over <see cref="Bullets"/> start (one for drawing, one for hitting): every bullet before it is gone for good by then. Bullets are added about in the
            /// order they go, so this skips the many that are long gone (Sunny Milk's split bullets pile up); when time goes back it starts again from the first.
            /// </summary>
            public int DrawFrom, HitFrom;

            public double DrawFromTime = double.NegativeInfinity, HitFromTime = double.NegativeInfinity;

            public int SkipGone(ref int from, ref double fromTime, double t, double slack)
            {
                if (t < fromTime)
                    from = 0;

                fromTime = t;

                while (from < Bullets.Count && Bullets[from].Until + slack < t)
                    from++;

                return from;
            }

            /// <summary>Called once, when <see cref="Start"/> and <see cref="End"/> are known: sets the window in which the act takes bullets.</summary>
            public virtual void Init(BulletField f)
            {
            }

            /// <summary>Called every frame (before anything is checked): captures what has to be captured, advances what is simulated.</summary>
            public virtual void Update(BulletField f, double now)
            {
            }

            /// <summary>Called every frame: adds the moments (game time) that the boss's decorative stars are flying in to, as in the warning before an attack (Mokou's jumps).</summary>
            public virtual void Warnings(BulletField f, double now, List<double> ends)
            {
            }

            /// <summary>Called every frame when the bullets are drawn: adds the places round which the bullets are drawn darker (Mokou's big circle).</summary>
            public virtual void Dims(BulletField f, double t, List<StageDim> into)
            {
            }

            /// <summary>Called every frame after the bullets that fly into the act have been taken in (what was taken in can be used now).</summary>
            public virtual void Late(BulletField f, double now)
            {
            }

            /// <summary>Puts the sprites of the act at time <paramref name="t"/> into the layers.</summary>
            public virtual void Draw(BulletField f, double t)
            {
            }

            /// <summary>Called every frame before anything has moved: an act that decides where an op acts from does it here, so that nothing is sent the wrong way.</summary>
            public virtual void Early(BulletField f, double now)
            {
            }

            /// <summary>Adds the beams of the act at time <paramref name="t"/> to the ones that are drawn (the field calls this while it hands the beams to the renderer).</summary>
            public virtual void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
            }

            /// <summary>Adds the beads of the chains at time <paramref name="t"/>.</summary>
            public virtual void Beads(BulletField f, double t, List<StageBead> into)
            {
            }

            /// <summary>Does a bullet that flew from <paramref name="from"/> to <paramref name="to"/> (at time <paramref name="t"/>) get taken in? The act keeps what it needs of it.</summary>
            public virtual bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet) => false;

            /// <summary>Does the act push a bullet that flew from <paramref name="from"/> to <paramref name="to"/> away? Then it flies on from <paramref name="position"/> in <paramref name="direction"/> at <paramref name="speed"/> (px per ms).</summary>
            public virtual bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;
                return false;
            }

            /// <summary>
            /// Does the act catch a bullet that flew from <paramref name="from"/> to <paramref name="to"/> (at time <paramref name="t"/>)? Then the bullet stays at <paramref name="position"/> (reached
            /// <paramref name="fraction"/> of the way along the step), facing <paramref name="direction"/>, until the game time <paramref name="gone"/>.
            /// </summary>
            public virtual bool Catches(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float fraction, out double gone)
            {
                position = to;
                direction = Vector2.UnitX;
                fraction = 1f;
                gone = double.PositiveInfinity;
                return false;
            }

            /// <summary>A push of this act is no bounce: a ricochet bullet that it sends on keeps the ricochets that it had before the step (Okina's portals).</summary>
            public virtual bool PushIsNoBounce => false;

            /// <summary>A push of this act drops the bullet: it falls freely from where it was pushed, with this pull (px per ms squared, downwards); 0 = it just flies on.</summary>
            public virtual float PushGravity => 0f;

            /// <summary>The game time of the step before the one that <see cref="Pushes"/> is asked about (set by the field: a push is the bullet's path from there to <c>t</c>).</summary>
            public double ScanPrevious;

            /// <summary>A push of this act is a bounce off a wall (the bullets only bounce a few times, see <see cref="BulletEmitter.WallBouncesOf"/>).</summary>
            public virtual bool CountsAsWallBounce => false;

            /// <summary>A push of this act is a ricochet (the bullet bounces off something): it flashes where it happens (see drawRicochetFlashes).</summary>
            public virtual bool PushIsRicochet => CountsAsWallBounce;

            /// <summary>Does the character hurt itself on the act (like on a slider) at <paramref name="now"/>?</summary>
            public virtual bool Hurts(BulletField f, double now) => false;

            /// <summary>When the last mechanic of the act's boss is over, if it goes on after her section (the boss stays until then: <see cref="BossPeriod.MechanicsEnd"/>).</summary>
            public virtual double MechanicsEnd => double.NegativeInfinity;

            /// <summary>Where the act puts the boss at <paramref name="now"/> (Mokou's jumps); false = the boss stands where bosses stand.</summary>
            public virtual bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;
                return false;
            }

            /// <summary>Time went back to <paramref name="time"/>.</summary>
            public virtual void Reset(BulletField f, double time)
            {
            }
        }

        private const int stage_bullet_cap_per_act = 60000; // (the key of a used-up bullet is act.Id * 65536 + its number: it must stay below 65536)

        private readonly List<StageAct> stageActs = new List<StageAct>();
        private readonly List<WorldsAct> worldsActs = new List<WorldsAct>();
        private readonly List<UfoAct> ufoActs = new List<UfoAct>();
        private readonly List<TaikoAct> taikoActs = new List<TaikoAct>();
        private readonly GlassWalls glassWalls = new GlassWalls();
        private readonly List<TreeAct> treeActs = new List<TreeAct>();
        private readonly List<LilyAct> lilyActs = new List<LilyAct>();
        private readonly Dictionary<int, IchirinAct> lastCloud = new Dictionary<int, IchirinAct>();
        private readonly List<ElementAct> woodActs = new List<ElementAct>();
        private readonly List<HoleAct> holeActs = new List<HoleAct>();
        private readonly List<HeadAct> headActs = new List<HeadAct>();
        private readonly List<ObjectFallAct> objectFallActs = new List<ObjectFallAct>();
        private readonly List<ReturnAct> returnActs = new List<ReturnAct>();
        private readonly List<SunflowerAct> sunflowerActs = new List<SunflowerAct>();
        private readonly List<(double Start, double End)> autumnWindows = new List<(double, double)>();
        private readonly List<(double Start, double End)> verdantWindows = new List<(double, double)>();
        private readonly HashSet<(int, BulletEmitter, int)> borrowedBullets = new HashSet<(int, BulletEmitter, int)>(); // (which act has had its copy of which of Daiyousei's bullets)

        /// <summary>After how long something at <paramref name="at"/> going at <paramref name="velocity"/> gets to the edge of the screen with its middle (<paramref name="side"/>: the left or the right one).</summary>
        private double toWall(Vector2 at, Vector2 velocity, out bool side)
        {
            double acrossX = velocity.X > 1e-6f ? (stageMax.X - at.X) / velocity.X : velocity.X < -1e-6f ? (stageMin.X - at.X) / velocity.X : double.PositiveInfinity;
            double acrossY = velocity.Y > 1e-6f ? (stageMax.Y - at.Y) / velocity.Y : velocity.Y < -1e-6f ? (stageMin.Y - at.Y) / velocity.Y : double.PositiveInfinity;
            side = acrossX <= acrossY;
            return double.IsInfinity(Math.Min(acrossX, acrossY)) ? 60.0 : Math.Max(Math.Min(acrossX, acrossY), 0.0);
        }

        /// <summary>Do circles and sliders fall in this map (Urumi's sections)?</summary>
        public bool HasFallingObjects => objectFallActs.Count > 0;

        /// <summary>A <see cref="FallLayer"/> shows the falling objects as what they are; without one (the boss menu's preview, the bench) the act draws them itself.</summary>
        public bool FallingObjectsShown { get; set; }

        /// <summary>What is falling at <paramref name="t"/>: the object, when it began to fall and how far it has got.</summary>
        public void FallingObjects(double t, List<(OsuHitObject Source, double Began, Vector2 By)> into)
        {
            if (failed)
                return;

            foreach (var act in objectFallActs)
                act.Collect(t, into);
        }
        private readonly List<BossPeriod> nuclearPeriods = new List<BossPeriod>();

        /// <summary>When Utsuho shoots her rings of big balls (sorted): each plays Mokou's impact and jolts the screen.</summary>
        private double[] bigShotTimes = [];
        private readonly HashSet<int> stageConsumed = new HashSet<int>();
        private readonly List<StageAct> stageTakers = new List<StageAct>();
        private readonly List<StageBead> stageBeads = new List<StageBead>();
        private Ripple[] stageRipples = new Ripple[64];
        private HideCircle[] stageHides = new HideCircle[256];
        private int stageHideCount;
        private SmokeCloud[] stageSmokes = new SmokeCloud[128];
        private int stageSmokeCount;
        private bool hasSeal; // (Reimu is in this field: her pile fades out)
        private int stageRippleCount;
        private double stageRippleTime;
        private bool stageAbsorbs; // some act takes bullets away
        private float stageHide; // 0..1: how much the black hole hides the boss

        private bool lensOn;
        private Vector2 lensCentre;
        private float lensRadius;
        private float lensEdge; // (the radius of the dark of the hole: the bent light hugs it)
        private float lensPresence; // (0..1: how much of the hole there is; the second images fade with it)

        private StageLayer stageBack = null!;

        /// <summary>The bars of Okina's portals (two portals of up to three pieces each: a portal bends round a corner), shown by the act that draws them.</summary>
        private readonly Container[] portalPieces = createPortalPieces();

        private static Container[] createPortalPieces()
        {
            var pieces = new Container[6];

            for (int i = 0; i < pieces.Length; i++)
                pieces[i] = new Container { Masking = true, Alpha = 0f, Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White } };

            return pieces;
        }
        private StageLayer stageBullets = null!;
        private StageLayer stageBulletsUnder = null!;

        /// <summary>Utsuho's big balls (drawn as Mokou's ball is): right under the bullets.</summary>
        private StageLayer stageOrbs = null!;
        private StageLayer stageBats = null!;
        private StageLayer stageCloud = null!;
        private StageLayer stageRipple = null!;
        private StageLayer stageFront = null!;
        private StageLayer stageTop = null!;
        private StageLayer stageTree = null!;

        /// <summary>This map has a boss with a stage (the front and top layers are drawn above the hit objects too).</summary>
        public bool HasStage => stageActs.Count > 0;

        /// <summary>The things that stand on the playfield (planets, UFOs, drums): above the hit objects, under the boss's HUD.</summary>
        public Drawable CreateStageFrontProxy() => stageFront.CreateProxy();

        /// <summary>The black hole: above everything except the HUD.</summary>
        public Drawable CreateStageTopProxy() => stageTop.CreateProxy();

        private Vector2 stageMin => hasScreenBounds ? screenMin : Vector2.Zero;

        private Vector2 stageMax => hasScreenBounds ? screenMax : new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT);

        /// <summary>The radius of the plain bullet of a section (the stars are 1.5 times as big).</summary>
        private float stageBaseRadius(BossPeriod period)
            => period.StarRadius > 0 ? period.StarRadius / (DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR) : circleRadius * 0.25f;

        /// <summary>The speed (pixels per ms) of an ordinary bullet of a section.</summary>
        private static float stageBulletSpeed(BossPeriod period) => period.BulletSpeed > 0 ? period.BulletSpeed : 0.05f;

        /// <summary>How long a bullet that flies at <paramref name="velocity"/> from inside the screen can still be seen.</summary>
        private double stageLife(Vector2 velocity)
        {
            float speed = velocity.Length;

            if (speed < 1e-5f)
                return 600000;

            float diagonal = hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 1000f;
            return Math.Min(600000, (diagonal * 1.3f + 200f) / speed);
        }

        private static float smoothStep(double x)
        {
            float u = (float)Math.Clamp(x, 0.0, 1.0);
            return u * u * (3f - 2f * u);
        }

        /// <summary>How much a flashing bullet (Lily White's flowers) glows <paramref name="age"/> ms after it was fired, <paramref name="life"/> ms before it bursts: it pulses sixteen times, brighter towards the end.</summary>
        private static float flashBloom(double age, double life)
        {
            float u = (float)Math.Clamp(age / Math.Max(life, 1.0), 0.0, 1.0);
            float pulse = 0.5f + 0.5f * MathF.Sin(u * 16f * MathF.Tau);

            return DanmakusuBalance.LILY_FLASH_BLOOM * (0.35f + 0.65f * pulse) * (0.4f + 0.6f * u);
        }

        private static Vector2 turned(Vector2 v, float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        private int styleOf(BulletEmitter emitter)
        {
            if (emitter.Shape == BulletShape.SoftStar && backgroundStyle >= 0)
                return backgroundStyle;

            // (what a circle or slider fires in a boss section has the colour the circle or slider has there: its boss's)
            if (!emitter.FollowsBoss && ColourSections != null)
            {
                if (emitter.SectionStyle == -2)
                    emitter.SectionStyle = Bosses.BossColours.SectionColour(ColourSections, emitter.Time, emitter.ComboIndex);

                if (emitter.SectionStyle >= 0 && palette.Length > 0)
                    return emitter.SectionStyle % palette.Length;
            }

            return comboStyle(emitter.ComboIndex, emitter.FollowsBoss, emitter.BossRank);
        }

        /// <summary>The boss sections of the map (only when the palette is the bosses' colours, four each in the order of their places): what the circles and sliders fire in one takes its boss's colours (see BossColours).</summary>
        public Bosses.BossColours.Section[]? ColourSections { get; init; }

        /// <summary>The palette slot of a slider's beam: in a boss section the colour its slider has there.</summary>
        private int laserStyle(LaserEmitter laser)
        {
            if (!laser.FollowsBoss && !laser.SpinnerLike && ColourSections != null)
            {
                if (laser.SectionStyle == -2)
                    laser.SectionStyle = Bosses.BossColours.SectionColour(ColourSections, laser.StartTime, laser.ComboIndex);

                if (laser.SectionStyle >= 0 && palette.Length > 0)
                    return laser.SectionStyle % palette.Length;
            }

            return comboStyle(laser.ComboIndex, laser.FollowsBoss, laser.BossRank);
        }

        /// <summary>The palette slot of a combo colour. With two bosses the map has eight combo colours: the first four are the first boss's, the other four the second boss's, so what a boss shoots takes its own four; with three bosses all on the screen at once twelve.</summary>
        private int comboStyle(int comboIndex, bool followsBoss, int bossRank)
            => followsBoss && (slotBosses.Length == 2 || threeAtOnce) && palette.Length >= slotBosses.Length * 4 ? Math.Min(bossRank, slotBosses.Length - 1) * 4 + comboIndex % 4 : comboIndex % palette.Length;

        private bool threeAtOnce => threeKnown ??= slotBosses.Length == 3 && Array.Exists(bosses, period => period.Rank == 2);

        private bool? threeKnown;

        private StoredBullet makeStored(BulletEmitter emitter, double time) => new StoredBullet
        {
            Time = time,
            Speed = Math.Max(emitter.Speed, 0.02f),
            Style = styleOf(emitter),
            Shape = emitter.DrawShape ?? emitter.Shape,
            Radius = emitter.VisualRadius,
            Hit = emitter.HitRadius,
            Tint = emitter.Tint,
            TintAmount = emitter.TintAmount,
            Shade = emitter.Shade,
            Blades = emitter.Blades,
            BladeSpin = emitter.BladeSpin,
        };

        /// <summary>How long a sound lasts (ms): the power-up of a warning is that long, and ends where the thing it warns about starts.</summary>
        private double stageSoundLength(string name)
            => cueSamples.TryGetValue(name, out var sample) && sample != null && sample.Length > 0 ? sample.Length : DanmakusuBalance.POWER_UP_LENGTH_MS;

        private void playStageSound(string name) => playHeldSound(name);

        /// <summary>Plays a stage sound and hands back its channel, so that the act can fade it out before it is over (<see cref="fadeOutSound"/>); null when nothing plays.</summary>
        private SampleChannel? playHeldSound(string name)
        {
            if (failed || soundStillFrames >= 8 || !cueSamples.TryGetValue(name, out var sample) || sample == null)
                return null;

            var channel = sample.GetChannel();
            channel.Volume.Value = 1.0;
            channel.Play();
            return channel;
        }

        /// <summary>Fades a sound that is playing out over <paramref name="ms"/> and stops it.</summary>
        private void fadeOutSound(SampleChannel channel, double ms) => fadingChannels.Add((channel, Stopwatch.GetTimestamp(), channel.Volume.Value, ms));

        /// <summary>Makes the acts of every boss that has a stage (called once, from the constructor).</summary>
        private void buildStage()
        {
            var pendingWorlds = new List<WorldsAct>();

            // Kyouko: her walls come from the top and the bottom in the first attack of a section and from the sides in the next, and so on; when a break and a spinner follow each other
            // (the one of her who ends where the other starts) the sequence goes on from one to the other instead of starting again
            var echoOffsets = new Dictionary<BossPeriod, int>();
            var ufoAttacks = new Dictionary<int, int>(); // (Nue: her attacks so far, per boss slot)
            BossPeriod? echoBefore = null;

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Echo)
                {
                    echoBefore = null;
                    continue;
                }

                bool goesOn = echoBefore != null && echoBefore.Kind != BossKind.Kiai && period.Kind != BossKind.Kiai && period.ShootStart - echoBefore.End < 1000;
                echoOffsets[period] = goesOn ? echoOffsets[echoBefore!] + BossOps.Attacks(echoBefore!).Count : 0;
                echoBefore = period;
            }

            foreach (var period in bosses)
            {
                var attacks = BossOps.Attacks(period);

                switch (period.Boss?.Mechanic)
                {
                    case BossMechanic.ThreeWorlds:
                    {
                        // (linked and registered below, once all her sections are known: a spinner or a new section must not reset the worlds)
                        // The World's Ender comes every so often (planWorldEnders): the throws and the turning worlds give way to it, whoever's it is, and after it she throws again
                        worldEnderPlan ??= planWorldEnders(bosses);

                        bool throwNext = true;
                        var worldsActs2 = new List<WorldsAct>();

                        foreach (var attack in attacks)
                        {
                            if (attack.End <= attack.Start)
                                continue;

                            var pieces = new List<(double Start, double End)> { (attack.Start, attack.End) };

                            foreach (var (enderPeriod, enderStart) in worldEnderPlan)
                            {
                                double enderEnd = enderStart + worldEnderLength(enderPeriod);
                                var next = new List<(double Start, double End)>();

                                foreach (var piece in pieces)
                                {
                                    if (enderEnd <= piece.Start || enderStart >= piece.End)
                                    {
                                        next.Add(piece);
                                        continue;
                                    }

                                    if (enderStart > piece.Start)
                                        next.Add((piece.Start, enderStart));

                                    if (enderEnd < piece.End)
                                        next.Add((enderEnd, piece.End));
                                }

                                pieces = next;
                            }

                            foreach (var piece in pieces)
                            {
                                // (what is too short for worlds to do anything is left out)
                                if (piece.End - piece.Start < Math.Max(period.BeatLength, 1) * DanmakusuBalance.BOSS_MIN_BREAK_BEATS)
                                    continue;

                                foreach (var (enderPeriod, enderStart) in worldEnderPlan)
                                {
                                    if (Math.Abs(enderStart + worldEnderLength(enderPeriod) - piece.Start) < 2)
                                        throwNext = true;
                                }

                                var worlds = new WorldsAct { Period = period, Start = piece.Start, End = piece.End, Throw = throwNext };
                                throwNext = !throwNext;
                                worldsActs2.Add(worlds);
                                pendingWorlds.Add(worlds);
                            }
                        }

                        foreach (var (enderPeriod, enderStart) in worldEnderPlan)
                        {
                            if (enderPeriod != period)
                                continue;

                            // (it takes the worlds over from the attack that ends as it begins)
                            var before = worldsActs2.Find(worlds => Math.Abs(worlds.End - enderStart) < 2);
                            var ender = new WorldEnderAct(period, enderStart, before);

                            if (before != null)
                                before.Ender = ender;

                            registerStage(ender);
                        }

                        break;
                    }

                    case BossMechanic.Unknown:
                        for (int i = 0; i < attacks.Count; i++)
                        {
                            if (attacks[i].End <= attacks[i].Start)
                                continue;

                            double next = i + 1 < attacks.Count ? attacks[i + 1].Start : double.NaN;
                            // (the way the UFOs turn changes with every attack, counted over the whole map: a new section goes on from the one before)
                            ufoAttacks.TryGetValue(period.Rank, out int sequence);
                            ufoAttacks[period.Rank] = sequence + 1;
                            registerStage(new UfoAct(this, period, attacks[i].Start, attacks[i].End, next, double.IsNaN(next) ? nextUfoAttack(period, attacks[i].End) : next, sequence));
                        }

                        break;

                    case BossMechanic.Taiko:
                    {
                        // Raiko attacks without a pause, so her attacks follow each other: they are one act (the drums go on from one attack to the next)
                        bool open = false;
                        double actStart = 0, actEnd = 0;

                        foreach (var (start, end) in attacks)
                        {
                            if (end <= start)
                                continue;

                            if (open && start - actEnd > 50)
                            {
                                registerStage(new TaikoAct(period, actStart, actEnd));
                                open = false;
                            }

                            if (!open)
                            {
                                actStart = start;
                                actEnd = end;
                                open = true;
                            }
                            else
                                actEnd = Math.Max(actEnd, end);
                        }

                        if (open)
                            registerStage(new TaikoAct(period, actStart, actEnd));

                        break;
                    }

                    case BossMechanic.FantasySeal:
                        registerStage(new SealAct(this, period));
                        hasSeal = true;
                        break;

                    case BossMechanic.Rebirth:
                    {
                        // Mokou never pauses: her attacks follow each other, so they are one act (the jumps go on from one attack to the next)
                        double jumpsFrom = double.PositiveInfinity, jumpsTo = double.NegativeInfinity;

                        foreach (var (start, end) in attacks)
                        {
                            if (end <= start)
                                continue;

                            jumpsFrom = Math.Min(jumpsFrom, start);
                            jumpsTo = Math.Max(jumpsTo, end);
                        }

                        if (jumpsTo > jumpsFrom)
                        {
                            // (in a kiai section she jumps from wall to wall; in a break and in a spinner she throws her big circle at the player instead)
                            if (period.Kind == BossKind.Kiai)
                                registerStage(new MokouAct(period, jumpsFrom, jumpsTo));
                            else
                                registerStage(new MokouThrowAct(period, jumpsFrom, jumpsTo));
                        }

                        break;
                    }

                    case BossMechanic.Echo:
                        // (the walls come from the top and the bottom in the first attack of a section, from the left and the right in the next, and so on)
                        for (int i = 0; i < attacks.Count; i++)
                        {
                            if (attacks[i].End > attacks[i].Start)
                            {
                                var glass = new GlassAct(this, period, attacks[i].Start, attacks[i].End, (echoOffsets.TryGetValue(period, out int echoOffset) ? echoOffset + i : i) % 2 == 0);
                                glassWalls.Add(glass);
                                registerStage(glass);
                            }
                        }

                        break;

                    case BossMechanic.Return:
                        // (every attack: what leaves the screen comes back, and she stands still; not in a section where she repeats the other bosses instead, see BulletPlanner)
                        if (Array.Exists(bosses, other => other.Borrowed && other.Rank == period.Rank && other.Start < period.End && other.End > period.Start))
                            break;

                        for (int i = 0; i < attacks.Count; i++)
                        {
                            if (attacks[i].End > attacks[i].Start)
                                registerStage(new ReturnAct(this, period, attacks[i].Start, attacks[i].End));
                        }

                        break;

                    case BossMechanic.Doors:
                        // (a portal in the wall behind her and one behind the player in every attack)
                        for (int i = 0; i < attacks.Count; i++)
                        {
                            if (attacks[i].End > attacks[i].Start)
                                registerStage(new PortalAct(this, period, attacks[i].Start, attacks[i].End));
                        }

                        break;

                    case BossMechanic.Dowsing:
                    {
                        var marks = BossOps.ConversionMarks(period);

                        if (marks.Count > 0)
                            registerStage(new DowsingAct(this, period, marks.ToArray()));

                        break;
                    }

                    case BossMechanic.Death:
                    {
                        var ripples = BossOps.RippleMarks(period);

                        if (ripples.Count > 0)
                        {
                            var death = new DeathAct(this, period, ripples.ToArray());
                            registerStage(death);

                            // (every beam that is alive while her section lasts is stopped by her ripples, and must last until what it has stopped has fallen away)
                            foreach (var laser in lasers)
                            {
                                if (laser.Immune || laser.EndTime + laser.Lifetime < death.Start || laser.StartTime > period.End)
                                    continue;

                                // (a beam that lives on into her next section is stopped by the ripples of both)
                                laser.Freezer = laser.Freezer == null ? death : new BeamFreezers(laser.Freezer, death);
                                laser.Lifetime = Math.Min(Math.Max(laser.Lifetime, period.End - laser.EndTime + death.KeepAfterEnd), laser.MostLifetime);
                            }
                        }

                        break;
                    }

                    case BossMechanic.FreeFall:
                        for (int i = 0; i < attacks.Count; i++)
                        {
                            // (alone on the map she plays another boss in every attack, see BulletPlanner: her own drop and pull are not there then)
                            var (attackStart, attackEnd) = attacks[i];

                            if (attackEnd > attackStart && !Array.Exists(bosses, other => other != period && other.Boss != period.Boss && other.Borrowed && !period.Borrowed && other.Rank == period.Rank && other.ShootStart < attackEnd && other.End > attackStart))
                                registerStage(new FreeFallAct(this, period, attackStart, attackEnd));
                        }

                        break;

                    case BossMechanic.Destruction:
                    {
                        // Flandre never pauses: her attacks follow each other, so they are one act (the eyes and the clones go on from one attack to the next)
                        double eyesFrom = double.PositiveInfinity, eyesTo = double.NegativeInfinity;

                        foreach (var (start, end) in attacks)
                        {
                            if (end <= start)
                                continue;

                            eyesFrom = Math.Min(eyesFrom, start);
                            eyesTo = Math.Max(eyesTo, end);
                        }

                        if (eyesTo > eyesFrom)
                            registerStage(new FlandreAct(this, period, eyesFrom, eyesTo));

                        break;
                    }

                    case BossMechanic.ScreenMirror:
                        // Seija: where the player is, after every turn of the screen
                        registerStage(new InversionAct(period, attacks));
                        break;

                    case BossMechanic.ImpossibleRequests:
                        // Kaguya: her moon, and what falls from it
                        registerStage(new MoonAct(period));
                        break;

                    case BossMechanic.Rays:
                    {
                        // Miko: a set of cones every eight beats, as long as cones and beams still fit into her section, and her big bullets every four
                        double conesLast = Math.Max(period.BeatLength, 1) * (DanmakusuBalance.MIKO_CONE_BEATS + DanmakusuBalance.MIKO_BEAM_BEATS);
                        var coneMarks = BossOps.LunaMarks(period, DanmakusuBalance.MIKO_EVERY_BEATS).FindAll(mark => mark + conesLast <= period.End);

                        registerStage(new RaysAct(period, coneMarks.ToArray(), BossOps.LunaMarks(period, DanmakusuBalance.MIKO_BIG_EVERY_BEATS).ToArray()));
                        break;
                    }

                    case BossMechanic.Slash:
                    {
                        // Youmu: a slash every four beats of an attack, and her rings of straight beams all the time her section lasts
                        var slashTimes = new List<double>();

                        foreach (var (start, end) in attacks)
                        {
                            for (double mark = start + Math.Max(period.BeatLength, 1) * DanmakusuBalance.YOUMU_SLASH_EVERY_BEATS; mark < end; mark += Math.Max(period.BeatLength, 1) * DanmakusuBalance.YOUMU_SLASH_EVERY_BEATS)
                                slashTimes.Add(mark);
                        }

                        var rayRings = BossOps.LunaMarks(period, DanmakusuBalance.YOUMU_RAY_EVERY_BEATS);

                        if (rayRings.Count > 0)
                            registerStage(new SlashAct(period, slashTimes.ToArray(), rayRings.ToArray()));

                        break;
                    }

                    case BossMechanic.Verdant:
                        // Daiyousei: the green of her attacks (what they do to the bullets is the planner's)
                        verdantWindows.AddRange(attacks);
                        break;

                    case BossMechanic.Tide:
                        // Wakasagihime's bubbles, seaweed and the ocean of her attacks
                        tideWindows.AddRange(attacks);

                        if (period.End > period.ShootStart)
                            registerStage(new TideAct(period, attacks));

                        break;

                    case BossMechanic.Storm:
                    {
                        // Tojiko: a bolt every two beats all the time her section lasts, and her own at the player every four beats of an attack
                        var skyBolts = BossOps.LunaMarks(period, DanmakusuBalance.TOJIKO_BOLT_EVERY_BEATS);
                        var herBolts = new List<double>();

                        foreach (var (start, end) in attacks)
                        {
                            for (double mark = start; mark < end; mark += Math.Max(period.BeatLength, 1) * DanmakusuBalance.TOJIKO_HER_BOLT_EVERY_BEATS)
                                herBolts.Add(mark);
                        }

                        if (skyBolts.Count > 0)
                            registerStage(new StormAct(period, skyBolts.ToArray(), herBolts.ToArray()));

                        break;
                    }

                    case BossMechanic.Possibility:
                    {
                        // Yumemi: her flowers of beams all the time her section lasts, and where the pull of a pause acts from
                        var flowers = BossOps.LunaMarks(period, DanmakusuBalance.YUMEMI_FLOWER_EVERY_BEATS);

                        if (flowers.Count > 0)
                            registerStage(new PossibilityAct(period, flowers.ToArray(), BossOps.PausesWithTail(period)));

                        break;
                    }

                    case BossMechanic.Stacking:
                    {
                        // Eika pushes everything every four beats of her section, and in her attacks what touches sticks together
                        var pushes = BossOps.LunaMarks(period, DanmakusuBalance.EIKA_PUSH_EVERY_BEATS);

                        if (pushes.Count > 0)
                            registerStage(new StackingAct(period, pushes.ToArray(), attacks));

                        break;
                    }

                    case BossMechanic.Foxes:
                    {
                        // Ran's spirals of beams, a fan of its own every eight beats of an attack, from its start
                        var fans = new List<double>();

                        foreach (var (start, end) in attacks)
                        {
                            for (double mark = start; mark < end; mark += Math.Max(period.BeatLength, 1) * DanmakusuBalance.RAN_FAN_EVERY_BEATS)
                                fans.Add(mark);
                        }

                        if (fans.Count > 0)
                            registerStage(new FoxAct(period, fans));

                        break;
                    }

                    case BossMechanic.Drift:
                    {
                        // Futo throws a boat when a pause starts (that everything slows down towards the wall is the planner's)
                        var boatThrows = new List<double>();

                        foreach (var (start, _) in BossOps.PausesWithTail(period))
                            boatThrows.Add(start);

                        if (boatThrows.Count > 0)
                            registerStage(new BoatAct(period, boatThrows));

                        break;
                    }

                    case BossMechanic.Misfortune:
                        // Shion: the arrows before everything turns round (that is an op), and her talismans that come back
                        reverseMarks.AddRange(BossOps.ReverseMarks(period));
                        reverseWarning = Math.Max(period.BeatLength, 1) * DanmakusuBalance.SHION_WARN_BEATS;

                        if (period.End > period.ShootStart)
                            registerStage(new MisfortuneAct(period));

                        break;

                    case BossMechanic.Market:
                    {
                        // Chimata's walls and her rings round the player, all the time her section lasts (the rings twice as often in a spinner)
                        var wallMarks = BossOps.LunaMarks(period, DanmakusuBalance.CHIMATA_WALL_EVERY_BEATS);
                        var ringMarks = BossOps.LunaMarks(period, period.Kind == BossKind.Spinner ? DanmakusuBalance.CHIMATA_RING_EVERY_BEATS / 2 : DanmakusuBalance.CHIMATA_RING_EVERY_BEATS);

                        if (wallMarks.Count > 0)
                            registerStage(new MarketAct(period, wallMarks.ToArray(), ringMarks.ToArray()));

                        break;
                    }

                    case BossMechanic.Judgement:
                        // Eiki's circle in her attacks (what it does to the bullets and beams is an op, see BossOps)
                        if (period.End > period.ShootStart)
                            registerStage(new JudgementAct(period, attacks));

                        break;

                    case BossMechanic.Anchors:
                        // Murasa's anchors, all through her section (she never pauses)
                        if (period.End > period.ShootStart)
                            registerStage(new AnchorAct(period));

                        break;

                    case BossMechanic.Claws:
                    case BossMechanic.Reaping:
                        registerStage(new ClawAct(period, period.Boss?.Mechanic == BossMechanic.Claws));

                        // (Elly's bats, which do not home, all the time her section lasts)
                        if (period.Boss?.Mechanic == BossMechanic.Reaping && period.End > period.ShootStart)
                            registerStage(new BatAct(this, period, period.ShootStart, period.End, true, DanmakusuBalance.ELLY_BAT_SPEED));

                        break;

                    case BossMechanic.HalfMoon:
                    {
                        // Mima's moons (what they do to what flies into them is an op, see BossOps)
                        var moonMarks = BossOps.MoonMarks(period);

                        if (moonMarks.Count > 0)
                            registerStage(new HalfMoonAct(period, moonMarks.ToArray()));

                        break;
                    }

                    case BossMechanic.Earthquake:
                    {
                        // Tenshi's bullets that come back up all through her section, and a shockwave every eight beats
                        var waves = BossOps.LunaMarks(period, DanmakusuBalance.TENSHI_WAVE_EVERY_BEATS);

                        if (waves.Count > 0)
                            registerStage(new QuakeAct(period, waves.ToArray()));

                        break;
                    }

                    case BossMechanic.Recording:
                    {
                        // Hisami records what is round the player every four beats of an attack
                        var recordings = new List<double>();

                        foreach (var (start, end) in attacks)
                        {
                            for (double mark = start; mark < end; mark += Math.Max(period.BeatLength, 1) * DanmakusuBalance.HISAMI_RECORD_EVERY_BEATS)
                                recordings.Add(mark);
                        }

                        if (recordings.Count > 0)
                            registerStage(new RecordAct(period, recordings.ToArray()));

                        break;
                    }

                    case BossMechanic.Bloodlust:
                        // Chiyari's bats (straight, all the time her section lasts) and the pools of blood of her attacks
                        if (period.End > period.ShootStart)
                        {
                            registerStage(new BatAct(this, period, period.ShootStart, period.End, true));
                            registerStage(new BloodAct(period, attacks));
                        }

                        break;

                    case BossMechanic.Shockwave:
                    {
                        // Yuugi stamps every eight beats all the time her section lasts
                        var stamps = BossOps.LunaMarks(period, DanmakusuBalance.YUUGI_RING_EVERY_BEATS);

                        if (stamps.Count > 0)
                            registerStage(new ShockwaveAct(period, stamps.ToArray()));

                        break;
                    }

                    case BossMechanic.Fortune:
                    {
                        // Joon: every four beats of her section part of the bullets turn gold and fall (her own golden falling bullets are the planner's)
                        var turns = BossOps.LunaMarks(period, DanmakusuBalance.JOON_EVERY_BEATS);

                        if (turns.Count > 0)
                            registerStage(new FortuneAct(period, turns.ToArray(), attacks));

                        break;
                    }

                    case BossMechanic.Flowers:
                        // Yuuka's sunflowers grow all through her section (what is growing at its end still flies off)
                        if (period.End > period.ShootStart)
                            registerStage(new SunflowerAct(period));

                        break;

                    case BossMechanic.Autumn:
                    {
                        // Shizuha: every second bullet turns orange in her attacks (see autumnLevel) and is a falling leaf in the pause after
                        foreach (var (start, end) in attacks)
                        {
                            if (end > start)
                                autumnWindows.Add((start, end));
                        }

                        var pauses = BossOps.PausesWithTail(period);

                        if (pauses.Count > 0)
                            registerStage(new LeavesAct(period, pauses));

                        break;
                    }

                    case BossMechanic.Stampede:
                    {
                        // Saki: the brown dim and the horse's head of her attacks, and her rings of beams all the time her section lasts (what she does to the rings is the planner's, her speed-up BossEffects')
                        stageOnClock = true;

                        if (period.End > period.ShootStart)
                            registerStage(new StampedeAct(period, attacks));

                        var rings = BossOps.LunaMarks(period, DanmakusuBalance.SAKI_RING_EVERY_BEATS);

                        if (rings.Count > 0)
                            registerStage(new LunaAct(period, rings.ToArray(), DanmakusuBalance.SAKI_RING_ARCS, DanmakusuBalance.SAKI_RING_ARC_SHARE, false));

                        break;
                    }

                    case BossMechanic.Swarm:
                        // Wriggle's fireflies: all through her section
                        if (period.End > period.ShootStart)
                            registerStage(new SwarmAct(period));

                        break;

                    case BossMechanic.Miracle:
                        // Sanae's attacks: the green dim and her star (what she does to the rings is the planner's)
                        if (period.End > period.ShootStart)
                            registerStage(new MiracleAct(period, attacks));

                        break;

                    case BossMechanic.Dango:
                        // Ringo's skewers and the sound of her big bullets (the copies, the throws and the big bullets are the planner's)
                        if (period.End > period.ShootStart)
                            registerStage(new RingoAct(period));

                        break;

                    case BossMechanic.Blades:
                        // Nemuno's taken beams (her knives and the bullets that come out where the player is are the planner's)
                        if (period.TakenBeams.Count > 0)
                            registerStage(new NemunoAct(period));

                        break;

                    case BossMechanic.Traps:
                    {
                        // Enoko's traps: one act for all her sections (a trap stays after its section, and there are never more than 24 on the map)
                        if (Array.Exists(stageActs.ToArray(), act => act is TrapAct))
                            break;

                        var hers = new List<BossPeriod>();

                        foreach (var other in bosses)
                        {
                            if (other.Boss?.Mechanic == BossMechanic.Traps && other.End > other.ShootStart)
                                hers.Add(other);
                        }

                        if (hers.Count > 0)
                            registerStage(new TrapAct(this, hers));

                        break;
                    }

                    case BossMechanic.Bats:
                        // Koakuma's bats are made while she attacks
                        if (period.End > period.ShootStart)
                            registerStage(new BatAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.SevenElements:
                        // Patchouli never pauses: the elements follow each other all the time her section lasts
                        if (period.End > period.ShootStart)
                            registerStage(new ElementAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Necromancy:
                        // Kaku's bullets come back from the walls while she attacks
                        if (period.End > period.ShootStart)
                            registerStage(new NecromancyAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Heads:
                        // Sekibanki throws her heads in her attacks
                        registerStage(HeadAct.Heads(period));
                        break;

                    case BossMechanic.Scrolls:
                        // Byakuren's beams in her attacks and from the places she remembered: homing like Sekibanki's heads, without a head (her spinner's beams are lasers, see BulletPlanner)
                        registerStage(HeadAct.Beams(period));
                        break;

                    case BossMechanic.Cloud:
                        // Ichirin's hands are thrown in her attacks and clap in her pauses all the time her section lasts
                        if (period.End > period.ShootStart)
                            registerStage(new IchirinAct(period, period.Start, period.End)); // (the cloud appears in the warning, and the first hand is already drawn back)

                        break;

                    case BossMechanic.Silence:
                    {
                        // Luna Child's arcs fly out every beat all the time her section lasts
                        var rings = BossOps.LunaMarks(period);

                        if (rings.Count > 0)
                            registerStage(new LunaAct(period, rings.ToArray()));

                        break;
                    }

                    case BossMechanic.Inchling:
                        // Sukuna's sizes apply in her attacks (and in the warning before them)
                        registerStage(new InchlingAct(period));
                        break;

                    case BossMechanic.Crescent:
                    {
                        // Mugetsu: her vortex (the planner gave every ring it meets the same one; every ring goes round where she stood when it was fired, or when the attack began)
                        Swirl? swirl = null;

                        foreach (var ring in emitters)
                        {
                            if (ring.Swirl != null)
                            {
                                swirl = ring.Swirl;
                                break;
                            }
                        }

                        if (swirl != null)
                            registerStage(new SwirlAct(period, swirl));

                        break;
                    }

                    case BossMechanic.Momentum:
                    {
                        // Urumi never pauses: a push is announced every eight beats all the time her section lasts
                        var pushes = BossOps.PushMarks(period);

                        if (pushes.Count > 0)
                            registerStage(new MomentumAct(this, period, pushes.ToArray()));

                        // (and what has been hit falls on the player)
                        registerStage(new ObjectFallAct(period, circleZones, sliderZones));
                        break;
                    }

                    case BossMechanic.Prism:
                        // Sunny Milk's prism turns while she attacks
                        if (period.End > period.ShootStart)
                            registerStage(new PrismAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Recollection:
                        // Satori reads the player's moves every beat while she attacks
                        if (period.End > period.ShootStart)
                            registerStage(new TrackAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Qi:
                        // Meiling never pauses: her pulls follow each other all the time her section lasts
                        if (period.End > period.ShootStart)
                            registerStage(new MeilingAct(this, period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Darkness:
                        // Rumia never pauses: she throws her invisible circles the whole time her section lasts
                        if (period.End > period.ShootStart)
                            registerStage(new RumiaAct(period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Smoke:
                        // she throws smoke the whole time her section lasts (attacks and pauses alike)
                        if (period.End > period.ShootStart)
                            registerStage(new SmokeAct(period, period.ShootStart, period.End));

                        break;

                    case BossMechanic.Haste:
                        registerHaste(period); // (Gengetsu's grey dim, blur and riser: BulletField.Haste)

                        break;

                    case BossMechanic.Nuclear:
                        nuclearPeriods.Add(period);

                        foreach (var (start, end) in BossOps.PausesWithTail(period))
                        {
                            if (end > start)
                                registerStage(new HoleAct { Period = period, Start = start, End = end });
                        }

                        break;
                }
            }

            var treeWindows = BossOps.TreeWindows(bosses);

            for (int w = 0; w < treeWindows.Count; w++)
            {
                var (start, end) = treeWindows[w];
                BossPeriod? owner = null;
                var treeAttacks = new List<(double Start, double End)>();

                foreach (var period in bosses)
                {
                    if (period.Boss?.Mechanic != BossMechanic.ButterflyFans || period.ShootStart < start - 1 || period.ShootStart > end)
                        continue;

                    owner ??= period;

                    foreach (var attack in BossOps.Attacks(period))
                    {
                        if (attack.End > attack.Start)
                            treeAttacks.Add(attack);
                    }
                }

                treeAttacks.Sort((x, y) => x.Start.CompareTo(y.Start));

                if (owner != null)
                    registerStage(new TreeAct(owner, start, end, treeAttacks, w + 1 < treeWindows.Count ? treeWindows[w + 1].Start : double.PositiveInfinity));
            }

            // Lily White: branches stand for as long as her sections last (sections close together share them), and grow flowers in her attacks
            var lilyWindows = BossOps.TreeWindows(bosses, BossMechanic.Spring);

            for (int w = 0; w < lilyWindows.Count; w++)
            {
                var (start, end) = lilyWindows[w];
                BossPeriod? owner = null;
                var lilyAttacks = new List<(double Start, double End)>();

                foreach (var period in bosses)
                {
                    if (period.Boss?.Mechanic != BossMechanic.Spring || period.ShootStart < start - 1 || period.ShootStart > end)
                        continue;

                    owner ??= period;

                    foreach (var attack in BossOps.Attacks(period))
                    {
                        if (attack.End > attack.Start)
                            lilyAttacks.Add(attack);
                    }
                }

                if (owner != null)
                    registerStage(new LilyAct(owner, start, end, lilyAttacks, w + 1 < lilyWindows.Count ? lilyWindows[w + 1].Start : double.PositiveInfinity));
            }

            // Hecatia: her attacks follow each other without a pause, also across sections (a spinner or the next section): the worlds go on from one to the next,
            // and where the attack before ends a moment early it goes on until the next one starts, so nothing is reset or fades
            pendingWorlds.Sort((a, b) => a.Start.CompareTo(b.Start));

            int turningAttacks = 0;
            float turnDirection = 1f;

            for (int i = 0; i < pendingWorlds.Count; i++)
            {
                var worlds = pendingWorlds[i];

                if (i > 0)
                {
                    var before = pendingWorlds[i - 1];
                    double gap = worlds.Start - before.End;

                    if (before.Ender == null && before.Period.Boss == worlds.Period.Boss && gap <= (before.Period == worlds.Period ? 50 : DanmakusuBalance.HECATIA_JOIN_MS))
                    {
                        before.End = worlds.Start; // (a gap is bridged; an overlap is cut off: one set of worlds, whichever way the sections change)

                        worlds.Prev = before;
                        before.Next = worlds;
                        worlds.AngleStart = before.SunAngle(before.End); // (the Sun goes on from where it was)
                    }
                }

                // every turning attack turns the worlds the other way round (the throws go on turning the way the attack before did)
                if (!worlds.Throw)
                    turnDirection = turningAttacks++ % 2 == 0 ? 1f : -1f;

                worlds.Direction = turnDirection;

                registerStage(worlds);
            }

            // Byakuren: a big star on every spot she fires a remembered beam from
            // (Byakuren's remembered spots flash a big star when they throw their beams)
            var byakuren = Array.FindAll(bosses, period => period.Boss?.Mechanic == BossMechanic.Scrolls && period.SpotThrows.Count > 0);

            if (byakuren.Length > 0)
                registerStage(new SpotStarAct(byakuren));

            foreach (var act in stageActs)
            {
                if (act.AbsorbTo >= act.AbsorbFrom)
                    stageAbsorbs = true;
            }

            // the arrowheads (simulated step by step) bounce off Kyouko's walls themselves
            if (stageActs.Exists(act => act is GlassAct))
            {
                foreach (var emitter in emitters)
                {
                    if (emitter.Homing)
                        emitter.Glass = glassWalls;
                }
            }
        }

        /// <summary>Nue: when her first attack after <paramref name="after"/> starts in a later section of the same boss slot (NaN = none).</summary>
        private double nextUfoAttack(BossPeriod period, double after)
        {
            double best = double.NaN;

            foreach (var other in bosses)
            {
                if (other == period || other.Rank != period.Rank || other.Boss?.Mechanic != BossMechanic.Unknown)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(other))
                {
                    if (end > start && start > after && (double.IsNaN(best) || start < best))
                        best = start;
                }
            }

            return best;
        }

        private void registerStage(StageAct act)
        {
            act.Id = stageActs.Count;
            act.Init(this);
            stageActs.Add(act);

            // (a boss whose mechanic goes on after her section stays until it is over)
            if (act.Period != null && act.MechanicsEnd > act.Period.End)
                act.Period.MechanicsEnd = Math.Max(act.Period.MechanicsEnd, act.MechanicsEnd);

            switch (act)
            {
                case WorldsAct worlds:
                    worldsActs.Add(worlds);
                    break;

                case UfoAct ufo:
                    ufoActs.Add(ufo);
                    break;

                case TaikoAct taiko:
                    taikoActs.Add(taiko);
                    break;

                case ElementAct elements:
                    woodActs.Add(elements);
                    break;

                case TreeAct tree:
                    treeActs.Add(tree);
                    break;

                case LilyAct lily:
                    lilyActs.Add(lily);
                    break;

                case IchirinAct cloud:
                    // (a cloud gives way to the next one of the same boss: they never show together)
                    if (lastCloud.TryGetValue(cloud.Period.Rank, out var earlier))
                        earlier.NextStart = Math.Min(earlier.NextStart, cloud.Start);

                    lastCloud[cloud.Period.Rank] = cloud;
                    break;

                case LunaAct luna:
                    lunaActs.Add(luna);
                    break;

                case SunflowerAct sunflowers:
                    sunflowerActs.Add(sunflowers);
                    break;

                case JudgementAct judgement:
                    judgementActs.Add(judgement);
                    break;

                case HalfMoonAct moon:
                    halfMoonActs.Add(moon);
                    break;

                case WorldEnderAct ender:
                    worldEnderActs.Add(ender);
                    break;

                case ClawAct claw:
                    clawActs.Add(claw);

                    // (what her claws cut out of the beams is gone from them)
                    foreach (var laser in lasers)
                    {
                        if (!laser.Immune)
                            laser.Blasts ??= flandreBridge ??= new FlandreBridge(this);
                    }

                    break;

                case SlashAct slash:
                    slashActs.Add(slash);

                    // (what her slashes cut out of the beams is gone from them)
                    foreach (var laser in lasers)
                    {
                        if (!laser.Rotates)
                            laser.Blasts ??= flandreBridge ??= new FlandreBridge(this);
                    }

                    break;

                case PortalAct portal:
                    portalActs.Add(portal);

                    // (the beams and the stars of the moon go through the portals too)
                    foreach (var laser in lasers)
                    {
                        if (!laser.Immune)
                            laser.Portals ??= portalBridge ??= new PortalBridge(this);
                    }

                    break;

                case HoleAct hole:
                    holeActs.Add(hole);
                    break;

                case HeadAct heads:
                    headActs.Add(heads);
                    break;

                case ObjectFallAct falling:
                    objectFallActs.Add(falling);
                    break;

                case ReturnAct returned:
                    returnActs.Add(returned);
                    break;

                case NemunoAct nemuno:
                    nemunoActs.Add(nemuno);
                    break;

                case FlandreAct flandre:
                    flandreActs.Add(flandre);

                    // (her eyes blow up the slices of every beam of the map)
                    foreach (var laser in lasers)
                    {
                        if (!laser.Immune)
                            laser.Blasts ??= flandreBridge ??= new FlandreBridge(this);
                    }

                    break;
            }
        }

        /// <summary>Every frame, before anything is checked: the acts capture and simulate, and the ripples that are on are collected.</summary>
        private void updateStage(double now)
        {
            if (stageActs.Count == 0)
                return;

            stageRippleCount = 0;
            stageSmokeCount = 0;
            stageHideCount = 0;
            stageRippleTime = now;
            stageHide = 0f;
            sukunaLevel = 0f;
            sukunaShaking = false;
            seijaCloneLevel = 0f;
            lunaLevel = 0f;
            lensOn = false;
            lensRadius = 0f;
            lensEdge = 0f;
            lensPresence = 0f;

            foreach (var act in stageActs)
                act.Update(this, now);

            stageBeads.Clear();

            foreach (var act in stageActs)
                act.Beads(this, now, stageBeads);
        }

        /// <summary>Every frame, after the bullets have been taken in: the acts use what they took.</summary>
        private void lateStage(double now)
        {
            foreach (var act in stageActs)
                act.Late(this, now);
        }

        private void addHide(Vector2 centre, float radius, float presence)
        {
            if (stageHideCount == stageHides.Length)
                Array.Resize(ref stageHides, stageHides.Length * 2);

            stageHides[stageHideCount++] = new HideCircle { Centre = centre, Radius = radius, Presence = presence };
        }

        /// <summary>0..1: how well hidden <paramref name="p"/> is right now (1 = well inside one of Rumia's circles, half at its edge, with a smooth ramp over a band across the edge).</summary>
        private float hideAt(Vector2 p)
        {
            float best = 0f;
            float band = Math.Max(circleRadius * DanmakusuBalance.RUMIA_EDGE_CIRCLES, 1f);

            for (int i = 0; i < stageHideCount; i++)
            {
                var circle = stageHides[i];
                float s = Math.Clamp((circle.Radius + band * 0.5f - Vector2.Distance(p, circle.Centre)) / band, 0f, 1f);

                best = Math.Max(best, s * s * (3f - 2f * s) * circle.Presence);
            }

            return best;
        }

        /// <summary>Makes the bullets (the ones already in the buffer) that are in or near one of Rumia's circles see-through.</summary>
        private void applyStageHides(int count)
        {
            if (stageHideCount == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                if (instanceBuffer[i].Immune)
                    continue; // (a boss's own thing: a train, a bead of an act)

                float hidden = hideAt(instanceBuffer[i].Position);

                if (hidden > 0.002f)
                    instanceBuffer[i].Fade = 1f - (1f - instanceBuffer[i].Fade) * (1f - hidden);
            }
        }

        private void addSmoke(Vector2 centre, float radius, float presence)
        {
            if (stageSmokeCount == stageSmokes.Length)
                Array.Resize(ref stageSmokes, stageSmokes.Length * 2);

            stageSmokes[stageSmokeCount++] = new SmokeCloud { Centre = centre, Radius = radius, Presence = presence };
        }

        /// <summary>0..1: how much in smoke <paramref name="p"/> is right now (1 = well inside a cloud, falling off over the outer quarter of its radius, and as the cloud fades).</summary>
        private float smokeAt(Vector2 p)
        {
            float best = 0f;

            for (int i = 0; i < stageSmokeCount; i++)
            {
                var cloud = stageSmokes[i];
                float edge = Math.Max(cloud.Radius * 0.25f, 1f);
                float s = Math.Clamp((cloud.Radius - Vector2.Distance(p, cloud.Centre)) / edge, 0f, 1f);

                best = Math.Max(best, s * s * (3f - 2f * s) * cloud.Presence);
            }

            return best;
        }

        /// <summary>How much bigger a bullet is that is <paramref name="strength"/> (0..1) in smoke.</summary>
        private static float smokeGrow(float strength) => 1f + (DanmakusuBalance.SANNYO_SMOKE_SIZE - 1f) * strength;

        private void addRipple(Vector2 centre, double time)
        {
            if (stageRippleCount == stageRipples.Length)
                Array.Resize(ref stageRipples, stageRipples.Length * 2);

            stageRipples[stageRippleCount++] = new Ripple { Centre = centre, Time = time };
        }

        /// <summary>Time went back: everything that was captured or taken in after <paramref name="time"/> is not there yet.</summary>
        private void rewindStage(double time)
        {
            foreach (var act in stageActs)
            {
                act.Traced = []; // (the portals, the freezes... may go another way this time)
                act.Reset(this, time);
            }
        }

        // ---- bullets that take other bullets away (planets, UFOs, the black hole) ----

        /// <summary>The acts that can take something this frame: the rest of them (with many bosses on a map there are hundreds) are not asked for every ring at every step.</summary>
        private readonly List<StageAct> frameTakers = new List<StageAct>();

        private bool stageAbsorbsBetween(double from, double to)
        {
            foreach (var act in frameTakers)
            {
                if (act.AbsorbsBetween(from, to))
                    return true;
            }

            return false;
        }

        /// <summary>Bullets that fly into the planets, the UFOs' cones or the black hole are taken (checked in small steps of game time, from where each ring was last checked).</summary>
        private void absorbIntoStage(double now)
        {
            if (!stageAbsorbs)
                return;

            // (the earliest moment any ring is looked at from this frame)
            double earliest = now;

            for (int k = 0; k < activeCount; k++)
            {
                var ring = emitters[active[k]];
                earliest = Math.Min(earliest, double.IsNaN(ring.StageScanned) ? ring.Time : Math.Max(ring.StageScanned, ring.Time));
            }

            frameTakers.Clear();

            foreach (var act in stageActs)
            {
                if (ScanLikeBefore || act.AbsorbsBetween(earliest, now))
                    frameTakers.Add(act);
            }

            // (where every bullet is at every small step is worked out first, ring by ring on several cores - it only reads the rings; then, one ring after the other in the same order as ever, the
            // acts take, push and catch, which changes their own state and the rings')
            if (scanBuffers.Length < activeCount)
                Array.Resize(ref scanBuffers, Math.Max(activeCount, scanBuffers.Length * 2));

            if (activeCount >= parallel_min_rings)
                System.Threading.Tasks.Parallel.For(0, activeCount, parallelOptions, k => precomputeScan(k, now));
            else
            {
                for (int k = 0; k < activeCount; k++)
                    precomputeScan(k, now);
            }

            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];

                // (Yukari's trains: no other boss's act takes, pushes or catches them - all of them at once, here, not each act by itself: Nue's UFOs swallowed one)
                if (emitter.Retired || emitter.Count == 0 || emitter.Shape == BulletShape.Train)
                    continue;

                if (emitter.Homing)
                    absorbStageHoming(emitter, now);
                else
                    scanStage(emitter, now, scanBuffers[k]);
            }
        }

        /// <summary>Where every bullet of a ring is at every step of its scan up to now, worked out ahead (see <see cref="precomputeScan"/>).</summary>
        private sealed class ScanBuffer
        {
            public bool Valid;
            public double From;
            public int Steps;
            public Vector2[] Position = new Vector2[256];
            public ulong[] Alive = new ulong[8];
        }

        private ScanBuffer?[] scanBuffers = new ScanBuffer?[256];

        /// <summary>The steps of a scan from <paramref name="from"/> to <paramref name="to"/>: the same moments <see cref="scanStage"/> goes through.</summary>
        private static double scanStep(double from, double to) => Math.Max(DanmakusuBalance.MOON_SCAN_STEP_MS, (to - from) / (ScanLikeBefore ? 40 : DanmakusuBalance.SCAN_MOST_STEPS));

        /// <summary>(Tools/SectionCheck: looks at the rings as it was done before steps were limited and every act was asked for every ring, to see what that was worth)</summary>
        public static bool ScanLikeBefore;

        /// <summary>
        /// Works out, for ring <paramref name="k"/>, where each of its bullets is (and whether it is there) at every moment its scan will look at: the start, and the end of every step. It only reads
        /// the ring and the field (it runs on any core); a bullet that an act turns meanwhile is worked out again as the scan goes (see <see cref="scanStage"/>).
        /// </summary>
        private void precomputeScan(int k, double now)
        {
            var buffer = scanBuffers[k] ??= new ScanBuffer();
            buffer.Valid = false;

            var emitter = emitters[active[k]];

            if (emitter.Retired || emitter.Count == 0 || emitter.Homing)
                return;

            double from = double.IsNaN(emitter.StageScanned) ? emitter.Time : Math.Max(emitter.StageScanned, emitter.Time);

            if (now <= from || !stageAbsorbsBetween(from, now))
                return;

            double step = scanStep(from, now);
            int steps = 0;

            for (double previous = from; previous < now; previous = Math.Min(previous + step, now))
                steps++;

            int count = emitter.Count;
            int needed = (steps + 1) * count;

            if (buffer.Position.Length < needed)
                buffer.Position = new Vector2[Math.Max(needed, buffer.Position.Length * 2)];

            if (buffer.Alive.Length < steps + 1)
                buffer.Alive = new ulong[Math.Max(steps + 1, buffer.Alive.Length * 2)];

            ulong skip = emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask;
            double moment = from;

            for (int j = 0; j <= steps; j++)
            {
                double e = emitter.WithoutFrozenTime(effects.Effective(emitter, moment));
                ulong alive = 0;

                for (int b = 0; b < count; b++)
                {
                    if ((skip & (1UL << b)) != 0)
                        continue;

                    emitter.Evaluate(b, e, out buffer.Position[j * count + b], out _, out bool there);

                    if (there)
                        alive |= 1UL << b;
                }

                buffer.Alive[j] = alive;
                moment = Math.Min(moment + step, now);
            }

            buffer.From = from;
            buffer.Steps = steps;
            buffer.Valid = true;
        }

        private void scanStage(BulletEmitter emitter, double now, ScanBuffer? ahead = null)
        {
            double from = double.IsNaN(emitter.StageScanned) ? emitter.Time : Math.Max(emitter.StageScanned, emitter.Time);
            double to = now;

            if (to <= from)
                return;

            if (!stageAbsorbsBetween(from, to))
            {
                emitter.StageScanned = to;
                return;
            }

            double step = scanStep(from, to);
            double previous = from;

            // (the places worked out ahead, for the bullets that nothing has turned in this scan; -1: none)
            bool useAhead = ahead is { Valid: true } && ahead.From == from;
            ulong changed = 0;
            int stepIndex = 0;

            // (where every bullet was at the end of the step before is where the next step starts: each place is worked out once. The first step starts where the collision check of the
            // last frame left the ring, if it looked at it then)
            bool carry = emitter.Count <= 64;
            Span<Vector2> carried = stackalloc Vector2[carry ? emitter.Count : 0];
            ulong carriedMask = 0;

            if (carry && emitter.Seen is RingSnapshot seen && seen.Time == emitter.WithoutFrozenTime(effects.Effective(emitter, previous)))
            {
                carriedMask = seen.Known & seen.Alive;

                for (int b = 0; b < emitter.Count; b++)
                    carried[b] = seen.Position[b];
            }

            while (previous < to)
            {
                double t = Math.Min(previous + step, to);
                ulong nextMask = 0;
                stepIndex++;
                stageTakers.Clear();

                foreach (var act in frameTakers)
                {
                    if (act.Absorbs(t))
                    {
                        act.ScanPrevious = previous;
                        stageTakers.Add(act);
                    }
                }

                if (stageTakers.Count > 0)
                {
                    double ea = emitter.WithoutFrozenTime(effects.Effective(emitter, previous)), eb = emitter.WithoutFrozenTime(effects.Effective(emitter, t));

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        ulong bit = 1UL << b;

                        if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                            continue;

                        // (turned later than this step began, by something that acts at its own time (the walls letting go of what they carried): before that moment the closed form only
                        // knows the path without that turn, so this step has nothing to look at)
                        if (emitter.Deflections != null && emitter.Deflections.TryGetValue(b, out var justTurned) && justTurned.Time > ea)
                            continue;

                        Vector2 pa, pb;
                        bool aliveA = true, aliveB;
                        bool fromAhead = useAhead && (changed & bit) == 0 && stepIndex <= ahead!.Steps;

                        if ((carriedMask & bit) != 0)
                            pa = carried[b];
                        else if (fromAhead)
                        {
                            pa = ahead!.Position[(stepIndex - 1) * emitter.Count + b];
                            aliveA = (ahead.Alive[stepIndex - 1] & bit) != 0;
                        }
                        else
                            emitter.Evaluate(b, ea, out pa, out _, out aliveA);

                        if (fromAhead)
                        {
                            pb = ahead!.Position[stepIndex * emitter.Count + b];
                            aliveB = (ahead.Alive[stepIndex] & bit) != 0;
                        }
                        else
                            emitter.Evaluate(b, eb, out pb, out _, out aliveB);

                        if (!aliveA || !aliveB)
                            continue;

                        if (carry)
                        {
                            carried[b] = pb;
                            nextMask |= bit; // (taken back below if an act does something to it: its way changes)
                        }

                        // (a bullet of Daiyousei's that has just come in from the other wall has not flown across the screen)
                        if (emitter.WrapsOnce && Vector2.DistanceSquared(pa, pb) > DanmakusuBalance.DAIYOUSEI_JUMP * DanmakusuBalance.DAIYOUSEI_JUMP)
                            continue;

                        // (one of Daiyousei's that has left the screen and come in again has lost her buff: it can be taken as any bullet)
                        bool verdant = emitter.WrapsOnce && !emitter.LostVerdant(b, eb);

                        foreach (var act in stageTakers)
                        {
                            // (Utsuho's big balls cannot be destroyed: nothing takes or catches them, they can only be pushed - bounced, sent through Okina's portals...)
                            // (nor can Daiyousei's: what takes one has made a copy of it, once, and the bullet flies on)
                            if (!emitter.Sharp && !(verdant && borrowedBullets.Contains((act.Id, emitter, b))) && act.Takes(this, t, pa, pb, emitter, b))
                            {
                                noteTrainTaker(emitter, act.GetType().Name);

                                if (verdant)
                                {
                                    borrowedBullets.Add((act.Id, emitter, b));
                                    continue;
                                }

                                emitter.AbsorbedMask |= bit;
                                nextMask &= ~bit;
                                break;
                            }

                            if (act.Pushes(this, t, pa, pb, emitter, b, out Vector2 pushedFrom, out Vector2 pushedTo, out float pushedSpeed))
                            {
                                float diagonal = hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 1500f;
                                int ricochetsLeft = emitter.Ricochet ? emitter.RicochetsLeft(b, act.PushIsNoBounce ? ea : eb) : 0; // (being pushed is not a ricochet: what it had left stays)
                                float pushedGravity = act.PushGravity;
                                double longer = pushedGravity > 0f
                                    ? (eb - emitter.Time) + 2.0 * Math.Sqrt(2.0 * (diagonal + emitter.VisualRadius * 4f) / pushedGravity) + 500 // (a bullet that falls is past the screen after this long)
                                    : (eb - emitter.Time) + (diagonal * (ricochetsLeft + 1) + emitter.VisualRadius * 4f) / Math.Max(pushedSpeed, 1e-4f);
                                var turnedBefore = emitter.DeflectionOf(b);
                                emitter.Deflect(b, eb, pushedFrom, pushedTo, longer, pushedSpeed, ricochetsLeft, act.CountsAsWallBounce ? emitter.WallBouncesOf(b, act.Id) + 1 : -1, act.Id, pushedGravity, act.Id);
                                deflectLog.Add((emitter, b, t, turnedBefore));
                                nextMask &= ~bit;
                                changed |= bit; // (its way is another one now: the places worked out ahead are not its)
                                break;
                            }

                            if (!emitter.Sharp && act.Catches(this, t, pa, pb, emitter, b, out Vector2 caughtAt, out Vector2 caughtFacing, out float caughtFraction, out double caughtUntil))
                            {
                                emitter.Stick(b, ea + (eb - ea) * caughtFraction, caughtAt, caughtFacing, emitter.WithoutFrozenTime(effects.Effective(emitter, caughtUntil)));
                                nextMask &= ~bit;
                                changed |= bit;
                                break;
                            }
                        }
                    }
                }

                carriedMask = nextMask; // (a step that nobody looked at carries nothing)
                previous = t;
            }

            emitter.StageScanned = to;
        }

        /// <summary>Pulled bullets (arrowheads) are wherever the simulation has put them: look at the step they just made.</summary>
        private void absorbStageHoming(BulletEmitter emitter, double now)
        {
            stageTakers.Clear();

            foreach (var act in frameTakers)
            {
                if (act.Absorbs(now))
                {
                    act.ScanPrevious = previousTime; // (the step they just made is the last frame's)
                    stageTakers.Add(act);
                }
            }

            if (stageTakers.Count == 0)
                return;

            for (int b = 0; b < emitter.Count; b++)
            {
                ulong bit = 1UL << b;

                if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                    continue;

                emitter.Evaluate(b, now, out Vector2 position, out _, out bool alive);

                if (!alive)
                    continue;

                foreach (var act in stageTakers)
                {
                    if (act.Takes(this, now, emitter.PreviousPosition(b), position, emitter, b))
                    {
                        emitter.AbsorbedMask |= bit;
                        break;
                    }

                    if (act.Catches(this, now, emitter.PreviousPosition(b), position, emitter, b, out Vector2 caughtAt, out _, out _, out double caughtUntil))
                    {
                        emitter.StickHoming(b, caughtAt, emitter.WithoutFrozenTime(effects.Effective(emitter, caughtUntil)));
                        break;
                    }
                }
            }
        }

        // ---- the bullets and chains of the stages: flying, drawn, hitting ----

        private static Vector2 orbitPoint(in StageBullet b, double t)
        {
            float age = (float)(t - b.T0);
            float emerge = smoothStep(age / DanmakusuBalance.UTSUHO_EMERGE_MS);
            float phi = b.Phase + b.Omega * age;
            float lx = b.A * MathF.Cos(phi) * emerge, ly = b.B * MathF.Sin(phi) * emerge;
            float c = MathF.Cos(b.Tilt), s = MathF.Sin(b.Tilt);

            return b.Centre + new Vector2(lx * c - ly * s, lx * s + ly * c);
        }

        /// <summary>A bullet that turns at a steady rate for <see cref="StageBullet.TurnT"/> ms (an arc of a circle) and then flies straight on.</summary>
        private static Vector2 turnedPoint(in StageBullet b, double t, out Vector2 velocity)
        {
            float speed = b.V.Length;

            if (speed < 1e-9f)
            {
                velocity = Vector2.Zero;
                return b.P0;
            }

            float theta0 = MathF.Atan2(b.V.Y, b.V.X);
            float omega = b.TurnOmega;
            float turning = (float)Math.Clamp(t - b.T0, 0.0, b.TurnT);
            float theta = theta0 + omega * turning;
            Vector2 point = MathF.Abs(omega) < 1e-7f
                ? b.P0 + b.V * turning
                : b.P0 + new Vector2(MathF.Sin(theta) - MathF.Sin(theta0), MathF.Cos(theta0) - MathF.Cos(theta)) * (speed / omega);
            Vector2 heading = new Vector2(MathF.Cos(theta), MathF.Sin(theta));

            velocity = heading * speed;
            return point + heading * (speed * (float)Math.Max(t - b.T0 - b.TurnT, 0.0));
        }

        /// <summary>How far a wandering bullet is from its straight line (see <see cref="StageBullet.Wander"/>): two slow waves across each other, growing in from nothing.</summary>
        private static Vector2 wanderOffset(in StageBullet b, double t)
        {
            float age = (float)Math.Max(t - b.T0, 0.0);
            float rise = smoothStep(age / DanmakusuBalance.WRIGGLE_WANDER_RISE_MS);
            float paced = age * (b.WanderPace > 0f ? b.WanderPace : 1f);
            float x = MathF.Sin(paced * 0.0017f + b.WanderPhase) + 0.6f * MathF.Sin(paced * 0.0043f + b.WanderPhase * 2.3f);
            float y = MathF.Cos(paced * 0.0013f + b.WanderPhase * 1.7f) + 0.6f * MathF.Sin(paced * 0.0037f + b.WanderPhase * 3.1f);

            return new Vector2(x, y) * (b.Wander * rise);
        }

        private static Vector2 stageBulletPosition(in StageBullet b, double t)
        {
            if (b.Trail != null)
            {
                double u = (t - b.T0) / b.TrailStep;
                int i = (int)Math.Floor(u);

                if (u <= 0 || b.Trail.Count == 1)
                    return b.Trail[0];

                if (i >= b.Trail.Count - 1)
                    return b.Trail[b.Trail.Count - 1];

                return Vector2.Lerp(b.Trail[i], b.Trail[i + 1], (float)(u - i));
            }

            if (b.A <= 0f && b.TurnT > 0)
                return turnedPoint(b, t, out _);

            if (b.A <= 0f && b.Switch > 0 && t > b.Switch)
            {
                float first = (float)(b.Switch - b.T0);
                Vector2 switched = b.P0 + b.V * first + b.Acc * (0.5f * first * first);
                float later = (float)(t - b.Switch);
                return switched + b.V2 * later + b.Acc2 * (0.5f * later * later);
            }

            if (b.A <= 0f)
            {
                float dt = (float)(t - b.T0);

                if (b.StopAfter > 0)
                    dt = Math.Min(dt, (float)b.StopAfter);

                Vector2 flown = b.P0 + b.V * dt + b.Acc * (0.5f * dt * dt);

                if (b.Pushes != null)
                    flown += b.Pushes.Offset(b.PushFrom, t);

                if (b.Wander > 0f)
                    flown += wanderOffset(b, t);

                if (b.FallAt > 0 && t > b.FallAt)
                {
                    float fallen = (float)(t - b.FallAt);
                    flown += (b.FallDir.LengthSquared > 0f ? b.FallDir : Vector2.UnitY) * (0.5f * DanmakusuBalance.ZANMU_GRAVITY * (b.FallScale > 0f ? b.FallScale : 1f) * fallen * fallen);

                    if (b.WrapHeight > 0f && flown.Y > b.WrapTop + b.WrapHeight)
                        flown.Y -= b.WrapHeight; // (once: the second time it falls out of the bottom it is gone, see Until)
                }

                return flown;
            }

            if (t <= b.Release)
                return orbitPoint(b, t);

            Vector2 velocity = (orbitPoint(b, b.Release + 1) - orbitPoint(b, b.Release - 1)) * 0.5f;
            return orbitPoint(b, b.Release) + velocity * (float)(t - b.Release);
        }

        /// <summary>Which way a bullet that turns by itself points at <paramref name="t"/> (see <see cref="StageBullet.Spin"/>).</summary>
        private static Vector2 stageSpun(in StageBullet b, double t)
        {
            float angle = b.SpinPhase + b.Spin * (float)(t - b.T0);

            if (b.FallAt > 0 && t > b.FallAt)
                angle += (b.FallSpin - b.Spin) * (float)(t - b.FallAt);

            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        /// <summary>1 normally; less while a bullet that grows out of nothing / shrinks away is still doing so (see <see cref="StageBullet.Grow"/>).</summary>
        private static float stageBulletScale(in StageBullet b, double t)
        {
            float scale = 1f;

            if (b.Grow > 0)
                scale = smoothStep((t - b.T0) / b.Grow);

            if (b.Fade > 0)
                scale *= 1f - smoothStep((t - (b.Until - b.Fade)) / b.Fade);

            if (b.ScaleOver > 0)
            {
                float swell = (b.ScaleTo - 1f) * smoothStep((t - b.ScaleFrom) / b.ScaleOver);

                if (b.ScaleBackOver > 0)
                    swell *= 1f - smoothStep((t - b.ScaleBackAt) / b.ScaleBackOver);

                scale *= 1f + swell;
            }

            return scale;
        }

        /// <summary>How much of its tint a stage bullet shows at <paramref name="t"/> (it fades in, and a second tint can take over).</summary>
        private static float stageTintAmount(in StageBullet b, double t)
        {
            float amount = b.FadeIn > 0 ? b.TintAmount * smoothStep((t - b.T0) / b.FadeIn) : b.TintAmount;

            if (b.Tint2At > 0 && t >= b.Tint2At)
                amount += (b.Tint2Amount - amount) * smoothStep((t - b.Tint2At) / Math.Max(b.Tint2Over, 1.0));

            return amount;
        }

        private static float stageBladeAngle(in StageBullet b, double t)
        {
            double dt = Math.Max(t - b.T0, 0.0);

            if (b.StopAfter > 0)
            {
                double s = Math.Min(dt, b.StopAfter);
                dt = s - s * s / (2.0 * b.StopAfter); // (it turns slower and slower as it stops)
            }

            return b.BladeAngle0 + b.BladeSpin * (float)dt;
        }

        private static Vector2 stageBulletVelocity(in StageBullet b, double t)
        {
            if (b.Trail != null)
            {
                if (b.Trail.Count < 2)
                    return b.V;

                int i = Math.Clamp((int)Math.Floor((t - b.T0) / b.TrailStep), 0, b.Trail.Count - 2);
                return (b.Trail[i + 1] - b.Trail[i]) / (float)b.TrailStep;
            }

            if (b.A <= 0f && b.TurnT > 0)
            {
                turnedPoint(b, t, out Vector2 turned);
                return turned;
            }

            if (b.A <= 0f && b.Switch > 0 && t > b.Switch)
                return b.V2 + b.Acc2 * (float)(t - b.Switch);

            if (b.A <= 0f)
            {
                double moving = b.StopAfter > 0 ? Math.Min(t - b.T0, b.StopAfter) : t - b.T0;
                Vector2 current = b.V + b.Acc * (float)moving;

                if (b.Pushes != null)
                    current += b.Pushes.Velocity(b.PushFrom, t);

                if (b.Wander > 0f)
                    current += (wanderOffset(b, t + 1) - wanderOffset(b, t - 1)) * 0.5f;

                if (b.FallAt > 0 && t > b.FallAt)
                    current += (b.FallDir.LengthSquared > 0f ? b.FallDir : Vector2.UnitY) * (DanmakusuBalance.ZANMU_GRAVITY * (b.FallScale > 0f ? b.FallScale : 1f) * (float)(t - b.FallAt));

                return current;
            }

            double at = Math.Min(t, b.Release);
            return (orbitPoint(b, at + 1) - orbitPoint(b, at - 1)) * 0.5f;
        }

        /// <summary>The way from <paramref name="position"/> to the middle of the playfield (a unit vector).</summary>
        private Vector2 facingMiddle(Vector2 position)
        {
            Vector2 toMiddle = (stageMin + stageMax) * 0.5f - position;
            return toMiddle.LengthSquared > 1e-6f ? Vector2.Normalize(toMiddle) : new Vector2(1, 0);
        }

        /// <summary>How much of Sakuya's preview shows in this frame (0 = none): what the ops of the bosses move is mirrored when her time stop ends, the bullets of the acts too, and they get the green copy as well.</summary>
        private float stageSakuyaAlpha;

        private int addStageBullets(double drawTime, float fall, int count)
        {
            if (stageActs.Count == 0)
                return count;

            Vector2 sakuyaCentre = (bounceArea.Min + bounceArea.Max) / 2f;

            double slack = stageSlack;
            double allFreezes = freezeSpan(double.NegativeInfinity, double.PositiveInfinity);

            foreach (var act in stageActs)
            {
                if (act.Bullets.Count == 0 || drawTime < act.FirstT0 || drawTime > act.LastUntil + slack)
                    continue;

                for (int i = act.SkipGone(ref act.DrawFrom, ref act.DrawFromTime, drawTime, slack); i < act.Bullets.Count; i++)
                {
                    var bullet = act.Bullets[i];
                    bool traced = stageBulletTraced(bullet);

                    if (drawTime < bullet.T0 || (!traced && drawTime > bullet.Until) || (traced && drawTime > bullet.Until + slack))
                        continue;

                    if (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && stageConsumed.Count > 0 && stageConsumed.Contains(act.Id * 65536 + i))
                        continue;

                    if (bullet.Hidden)
                        continue;

                    float grown = stageBulletScale(bullet, drawTime);

                    if (grown < 0.02f)
                        continue;

                    bullet.Radius *= grown;
                    Vector2 position;
                    Vector2 velocity;
                    BulletLook? look = null;

                    // (the ops of the other bosses act on the straight ones: frozen, bounced off the walls, mirrored...)
                    if (traced)
                    {
                        // (what the hit check of this frame worked out, if it did: see tracedStageBullet)
                        if (tracedStageBulletGone(bullet, drawTime, allFreezes) || !tracedStageBullet(act, i, act.Bullets[i], drawTime, act.Bullets[i].Radius, out position, out velocity, out look))
                            continue;
                    }
                    else
                    {
                        double own = stageClock(bullet, drawTime);
                        position = stageBulletPosition(bullet, own);
                        velocity = stageBulletVelocity(bullet, own);
                    }

                    if (failed)
                    {
                        Vector2 carried = DanmakusuBalance.DEATH_BULLETS_KEEP_VELOCITY ? velocity : Vector2.Zero;
                        position += carried * fall + new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);
                        velocity = carried + new Vector2(0, DanmakusuBalance.DEATH_GRAVITY * fall);
                    }

                    if (!failed && bullet.ShakeFrom > 0 && drawTime >= bullet.ShakeFrom && drawTime < bullet.ShakeTo)
                        position += shakeOffset(act.Id * 65536 + i, 7, drawTime);
                    else if (sukunaShaking && !failed)
                        position += shakeOffset(act.Id * 65536 + i, 13, drawTime);

                    if (sukunaLevel > 0f)
                        bullet.Radius = Math.Max(bullet.Radius * sukunaGrow(position), leastBulletRadius);

                    // Urumi: while a push is announced everything of hers shakes
                    bool previewing = !failed && bullet.Pushes != null && bullet.Pushes.Previewing(drawTime, out _);

                    if (previewing)
                        position += shakeOffset(act.Id * 65536 + i, 11, drawTime) * DanmakusuBalance.URUMI_SHAKE;

                    StageBulletProbe?.Invoke(act.GetType().Name, act.Id * 65536 + i, position, drawTime);

                    // (a fast one that flies past the player whooshes, as a ring's bullet does; what cannot hit does not)
                    if (!failed && !bullet.Harmless && !bullet.Hidden)
                        flyBy(1L << 62 | (long)(act.Id * 65536 + i), position, drawTime);

                    float margin = bullet.Radius * 1.8f;

                    if (hasScreenBounds && (position.X < screenMin.X - margin || position.X > screenMax.X + margin || position.Y < screenMin.Y - margin || position.Y > screenMax.Y + margin))
                    {
                        // (what has left the screen for good is gone, as a ring's bullets are; not what homes (Byakuren's, Koakuma's, what Nazrin pulls: those have a trail), what goes round by itself, or what started outside)
                        if (!failed && bullet.Trail == null && bullet.A <= 0f && bullet.WrapHeight <= 0f && onTheScreen(bullet.P0, margin))
                        {
                            var gone = act.Bullets[i];

                            gone.Until = Math.Min(gone.Until, drawTime);
                            gone.Untraced = true;
                            act.Bullets[i] = gone;
                        }

                        continue;
                    }

                    bool sakuyaMarked = traced && stageSakuyaAlpha > 0f && !failed && !(look?.Harmless ?? false);

                    // (a round one at least sharp enough to need it is drawn from a big picture, as the rings' are)
                    if ((look?.Shape ?? bullet.Shape) == BulletShape.Circle && bullet.Radius >= sharpBulletRadius && bullet.Elongate <= 1f && !previewing && bullet.GhostUntil <= drawTime && bullet.AlphaOver <= 0 && !sakuyaMarked)
                    {
                        int tint = look is { Overlay: > 0 } ? look.Overlay : bullet.Tint;
                        Color4 body = tint > 0 && stageTintAmount(bullet, drawTime) >= 0.5f ? BulletRenderer.SpecialBodies[Math.Min(tint, BulletRenderer.SpecialBodies.Length) - 1] : palette[((bullet.Style % palette.Length) + palette.Length) % palette.Length];
                        sharpOrbs.Add((position, bullet.Radius, body));
                        continue;
                    }

                    if (count == instanceBuffer.Length)
                        Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                    instanceBuffer[count++] = new BulletInstance
                    {
                        Position = position,
                        Radius = bullet.Radius,
                        Style = bullet.Style,
                        Direction = bullet.Spin != 0f ? stageSpun(bullet, drawTime) : bullet.KeepFace && bullet.Face.LengthSquared > 0f ? bullet.Face : velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : bullet.Face.LengthSquared > 0f ? bullet.Face : new Vector2(1, 0),
                        BlueHandle = bullet.BlueHandle,
                        Shape = look?.Shape ?? bullet.Shape,
                        Speed = velocity.Length,
                        Overlay = look is { Overlay: > 0 } ? look.Overlay : bullet.Tint2At > 0 && drawTime >= bullet.Tint2At ? bullet.Tint2 : bullet.Tint,
                        OverlayAmount = look is { Overlay: > 0 } ? look.OverlayAmount : stageTintAmount(bullet, drawTime),
                        Fade = Math.Max(Math.Max(Math.Max(look is { Alpha: < 1f } ? 1f - look.Alpha : 0f, bullet.AlphaOver > 0 ? smoothStep((drawTime - bullet.AlphaFrom) / bullet.AlphaOver) : 0f),
                            drawTime < bullet.GhostUntil ? 1f - bullet.GhostVisible : drawTime < bullet.GhostUntil + 200 ? (1f - bullet.GhostVisible) * (1f - smoothStep((drawTime - bullet.GhostUntil) / 200.0)) : 0f),
                            bullet.AppearOver > 0 ? 1f - smoothStep((drawTime - bullet.T0) / bullet.AppearOver) : 0f),
                        Decal = look != null && look.Decal != BulletDecal.None ? look.Decal : bullet.Decal,
                        DecalFade = bullet.FadeIn > 0 ? 1f - smoothStep((drawTime - bullet.T0) / bullet.FadeIn) : 0f,
                        Shade = bullet.FallAt > 0 && drawTime > bullet.FallAt ? bullet.Shade + (bullet.DarkTo - bullet.Shade) * smoothStep((drawTime - bullet.FallAt) / Math.Max(bullet.DarkenOver, 1.0)) : bullet.Shade,
                        Bloom = Math.Max(bullet.BloomAt > 0 && drawTime >= bullet.BloomAt ? Math.Max(bullet.Bloom, bullet.BloomTo * smoothStep((drawTime - bullet.BloomAt) / 300.0)) : bullet.Bloom, sukunaLevel > 0f ? sukunaGlowAt(position) : 0f),
                        Blades = bullet.Blades > 1 && (look?.Shape ?? bullet.Shape) == BulletShape.Knife ? bullet.Blades : 1,
                        BladeAngle = bullet.Blades > 1 ? stageBladeAngle(bullet, drawTime) : 0f,
                        Talisman = bullet.Talisman,
                        TalismanFace = bullet.Talisman ? facingMiddle(position) : Vector2.Zero,
                        Elongate = bullet.Elongate,
                        Morph = look?.Morph ?? 1f, // (without this a stage bullet that is a talisman is drawn as the plain bullet under it)
                        Under = bullet.Shape,
                        Undimmed = bullet.Undimmed,
                        UnderClouds = bullet.UnderClouds,
                    };

                    // (Sakuya's preview: where her mirroring will put it, as for the bullets of the rings, near the player only)
                    if (sakuyaMarked && nearSakuyaPreview(sakuyaCentre * 2f - position))
                    {
                        if (count == instanceBuffer.Length)
                            Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                        var ghost = instanceBuffer[count - 1];
                        ghost.Position = sakuyaCentre * 2f - position;
                        ghost.Overlay = BulletRenderer.TINT_GREEN;
                        ghost.OverlayAmount = 1f;
                        ghost.Decal = BulletDecal.None;
                        ghost.Fade = 1f - DanmakusuBalance.SAKUYA_PREVIEW_VISIBLE * stageSakuyaAlpha;
                        instanceBuffer[count++] = ghost;
                    }
                }
            }

            foreach (var bead in stageBeads)
            {
                Vector2 position = bead.Position;

                if (failed)
                    position += new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);

                if (hasScreenBounds && (position.X < screenMin.X - bead.Radius || position.X > screenMax.X + bead.Radius || position.Y < screenMin.Y - bead.Radius || position.Y > screenMax.Y + bead.Radius))
                    continue;

                if (count == instanceBuffer.Length)
                    Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                instanceBuffer[count++] = new BulletInstance
                {
                    Position = position,
                    Radius = bead.Radius,
                    Style = bead.Style,
                    Direction = bead.Direction.LengthSquared > 0f ? bead.Direction : new Vector2(1, 0),
                    Shape = bead.Shape,
                    Speed = 0f,
                    Fade = bead.Fade,
                    Overlay = bead.Overlay,
                    OverlayAmount = bead.OverlayAmount,
                    Morph = 1f,
                    Under = bead.Shape,
                    Immune = true, // (part of the act's own thing: the other bosses do not hide it)
                };
            }

            return count;
        }

        /// <summary>Does a stage bullet, that moved from <paramref name="rel0"/> to <paramref name="rel1"/> (relative to the character), touch the character?</summary>
        private static bool stageSegmentHits(in StageBullet bullet, Vector2 rel0, Vector2 rel1, Vector2 velocity, double t)
        {
            if (bullet.Blades > 1 && bullet.Shape == BulletShape.Knife)
            {
                // a knife with more blades hurts where any of its blades is (each blade is a circle round its middle), as it does before it is stopped
                Vector2 facing = velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : bullet.Face.LengthSquared > 0f ? Vector2.Normalize(bullet.Face) : new Vector2(1, 0);
                float half = bullet.Radius * DanmakusuBalance.KNIFE_LENGTH_FACTOR;
                float spin = stageBladeAngle(bullet, t);

                for (int blade = 0; blade < bullet.Blades; blade++)
                {
                    Vector2 offset = BulletCollision.BladeOffset(bullet.Blades, blade, facing, half, spin);

                    if (distanceToSegment(Vector2.Zero, rel0 + offset, rel1 + offset) <= bullet.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            float reach = bullet.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

            if (distanceToSegment(Vector2.Zero, rel0, rel1) > reach)
                return false;

            if (bullet.Shape == BulletShape.Circle)
                return true;

            Vector2 direction = bullet.KeepFace && bullet.Face.LengthSquared > 1e-12f ? Vector2.Normalize(bullet.Face) : velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : bullet.Face.LengthSquared > 1e-12f ? Vector2.Normalize(bullet.Face) : new Vector2(1, 0);
            return BulletCollision.Hits(bullet.Shape, bullet.Hit, direction, -rel0, -rel1, DanmakusuBalance.PLAYER_HITBOX_RADIUS);
        }

        /// <summary>The bullets of the stages hit the character like any bullet does (swept along the character's path).</summary>
        private void checkStageBullets(double t0, double t1, Vector2 p0, Vector2 p1, double now)
        {
            if (stageActs.Count == 0)
                return;

            double span = t1 - t0;

            if (span <= 0)
                return;

            double slack = stageSlack;
            double allFreezes = freezeSpan(double.NegativeInfinity, double.PositiveInfinity);

            foreach (var act in stageActs)
            {
                if (act.Bullets.Count == 0 || t1 < act.FirstT0 || t0 > act.LastUntil + slack)
                    continue;

                for (int i = act.SkipGone(ref act.HitFrom, ref act.HitFromTime, t0, slack); i < act.Bullets.Count; i++)
                {
                    var bullet = act.Bullets[i];
                    bool traced = stageBulletTraced(bullet);

                    if (bullet.Slider || bullet.Harmless || t1 < bullet.T0 || t1 < bullet.GhostUntil || t0 > bullet.Until + (traced ? slack : 0))
                        continue;

                    int key = act.Id * 65536 + i;

                    if (stageConsumed.Contains(key))
                        continue;

                    double ta = Math.Max(t0, Math.Max(bullet.T0, bullet.GhostUntil)); // (a see-through one only hurts from when it is solid)
                    Vector2 pa = Vector2.Lerp(p0, p1, (float)((ta - t0) / span));
                    Vector2 pb = p1;
                    Vector2 positionA, positionB, velocity;

                    if (traced)
                    {
                        // (the ops of the other bosses act on the straight ones; a bullet that is frozen in a see-through slide does not hurt. It bounces where it is drawn bouncing: at its own radius,
                        // like the bullets of the rings. Where it was at the end of the last frame is kept, and where it is now is kept for the renderer)
                        if (tracedStageBulletGone(bullet, ta, allFreezes)
                            || !tracedStageBullet(act, i, bullet, ta, bullet.Radius, out positionA, out _, out BulletLook? lookA)
                            || !tracedStageBullet(act, i, bullet, t1, bullet.Radius, out positionB, out velocity, out BulletLook? lookB)
                            || lookA is { Harmless: true } || lookB is { Harmless: true })
                            continue;
                    }
                    else
                    {
                        double ownEnd = stageClock(bullet, t1);
                        positionA = stageBulletPosition(bullet, stageClock(bullet, ta));
                        positionB = stageBulletPosition(bullet, ownEnd);
                        velocity = stageBulletVelocity(bullet, ownEnd);
                    }

                    Vector2 rel0 = positionA - pa;
                    Vector2 rel1 = positionB - pb;
                    float grown = stageBulletScale(bullet, t1);

                    if (grown < 0.02f)
                        continue;

                    bullet.Hit *= grown;

                    if (sukunaLevel > 0f)
                        bullet.Hit *= sukunaGrow(positionB);

                    // (a bullet that has fallen out of the bottom and come in from the top jumps by a whole screen between two checks: it has not flown through everything between the two places)
                    bool wrapped = bullet.WrapHeight > 0f && positionB.Y < positionA.Y - bullet.WrapHeight * 0.5f;
                    bool touches;

                    if (wrapped)
                    {
                        touches = stageSegmentHits(bullet, rel0, positionB + new Vector2(0f, bullet.WrapHeight) - pb, velocity, t1)
                                  || stageSegmentHits(bullet, new Vector2(positionB.X, bullet.WrapTop) - pb, rel1, velocity, t1);
                    }
                    else
                        touches = stageSegmentHits(bullet, rel0, rel1, velocity, t1);

                    if (!touches)
                        continue;

                    stageConsumed.Add(key);
                    // (how old it is, how far it was and whether it was falling: to find out how a bullet that is nowhere near the character can hit it)
                    LastHitCause = $"stage bullet {bullet.Shape} ({act.GetType().Name}, age {t1 - bullet.T0:0} ms, {distanceToSegment(Vector2.Zero, rel0, rel1):0} px, hit {bullet.Hit:0}, span {span:0} ms{(bullet.FallAt > 0 && t1 > bullet.FallAt ? ", falling" : string.Empty)})";
                    onDamaged(damagePerBullet, true, now);
                    hits.Add(new HitRecord(now, stage_hit, key, damagePerBullet));
                }
            }
        }

        /// <summary>Is the character on a planet, the tree, the black hole or a chain right now? (They hurt like a slider body.)</summary>
        private string lastStageHurt = string.Empty;

        private bool touchesStage(double now)
        {
            if (stageActs.Count == 0)
                return false;

            foreach (var act in stageActs)
            {
                if (act.Hurts(this, now))
                {
                    lastStageHurt = act.GetType().Name;
                    return true;
                }
            }

            foreach (var bead in stageBeads)
            {
                if (!bead.Harmless && bead.Fade < 0.5f && Vector2.Distance(characterPosition, bead.Position) <= bead.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                {
                    lastStageHurt = "bead";
                    return true;
                }
            }

            return false;
        }

        /// <summary>Yukari's train hurts like a slider body for as long as the player is in it (it is not used up by a hit).</summary>
        private bool touchesTrain(double now)
        {
            foreach (var train in trainRings)
            {
                if (now < train.Time || train.RemovedAt(now))
                    continue;

                double own = Math.Min(train.WithoutFrozenTime(effects.Effective(train, now)), train.Time + train.Lifetime);
                train.Evaluate(0, own, out Vector2 position, out Vector2 direction, out bool alive);

                if (!alive)
                    continue;

                Vector2 offset = characterPosition - position;

                if (offset.Length > train.HitRadius + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                    continue;

                if (BulletCollision.Hits(BulletShape.Train, train.HitRadius, direction, offset, offset, DanmakusuBalance.PLAYER_HITBOX_RADIUS))
                    return true;
            }

            return false;
        }

        // ---- the screen shake of the stages ----

        /// <summary>0..1: Raiko's slams shake the screen for a moment, and Utsuho shakes it for as long as she is there.</summary>
        private float stageShake(double now)
        {
            float shake = 0f;

            // (Utsuho: a short jolt every time she shoots a ring of big balls, dying away)
            if (bigShotTimes.Length > 0)
            {
                int last = Array.BinarySearch(bigShotTimes, now);

                if (last < 0)
                    last = ~last - 1;

                if (last >= 0 && now - bigShotTimes[last] < DanmakusuBalance.UTSUHO_SHOT_SHAKE_MS)
                    shake = Math.Max(shake, DanmakusuBalance.UTSUHO_SHAKE * (1f - (float)((now - bigShotTimes[last]) / DanmakusuBalance.UTSUHO_SHOT_SHAKE_MS)));
            }

            foreach (var act in taikoActs)
                shake = Math.Max(shake, act.ShakeAt(now));

            foreach (var act in ufoActs)
                shake = Math.Max(shake, act.ShakeAt(now));

            foreach (var act in treeActs)
                shake = Math.Max(shake, act.ShakeAt(now));

            foreach (var act in stageActs)
            {
                if (act is MokouAct mokou)
                    shake = Math.Max(shake, mokou.ShakeAt(now));
                else if (act is MokouThrowAct thrower)
                    shake = Math.Max(shake, thrower.ShakeAt(now));
                else if (act is MeilingAct meiling)
                    shake = Math.Max(shake, meiling.ShakeAt(now));
                else if (act is ShockwaveAct shockwave)
                    shake = Math.Max(shake, shockwave.ShakeAt(now));
                else if (act is QuakeAct quake)
                    shake = Math.Max(shake, quake.ShakeAt(this, now));
                else if (act is PossibilityAct possibility)
                    shake = Math.Max(shake, possibility.ShakeAt(now));
                else if (act is FlandreAct flandre)
                    shake = Math.Max(shake, flandre.ShakeAt(now));
                else if (act is IchirinAct ichirin)
                    shake = Math.Max(shake, ichirin.ShakeAt(now));
                else if (act is PrismAct prism)
                    shake = Math.Max(shake, prism.ShakeAt(now));
                else if (act is HalfMoonAct halfMoon)
                    shake = Math.Max(shake, halfMoon.ShakeAt(now));
                else if (act is WorldEnderAct ender)
                    shake = Math.Max(shake, ender.ShakeAt(now));
                else if (act is AnchorAct anchors)
                    shake = Math.Max(shake, anchors.ShakeAt(now));
            }

            return Math.Max(shake, trainShake(now));
        }

        /// <summary>Yukari's train shakes the screen for as long as any part of it is on the screen.</summary>
        private float trainShake(double now)
        {
            if (!hasScreenBounds)
                return 0f;

            foreach (var train in trainRings)
            {
                if (now < train.Time || now > train.Time + train.Lifetime)
                    continue;

                train.Evaluate(0, now, out Vector2 position, out Vector2 direction, out bool alive);

                if (!alive)
                    continue;

                float half = train.HitRadius;
                float width = half * DanmakusuBalance.TRAIN_ASPECT;

                for (int step = -4; step <= 4; step++)
                {
                    Vector2 point = position + direction * (half * step / 4f);

                    if (point.X > screenMin.X - width && point.X < screenMax.X + width && point.Y > screenMin.Y - width && point.Y < screenMax.Y + width)
                        return DanmakusuBalance.YUKARI_TRAIN_SHAKE;
                }
            }

            return 0f;
        }

        // ---- the positions that other things of the field have to know ----

        /// <summary>Where the boss shoots from at <paramref name="time"/>: the Sun, which turns round Hecatia in all her attacks; otherwise the boss herself.</summary>
        private Vector2 stageShotOrigin(double time, int rank = 0)
        {
            Vector2 boss = bossPositionOf(rank);

            foreach (var act in worldsActs)
            {
                if (time >= act.Start && time < act.End && act.Period.Rank == rank)
                    return act.OrbitBody(this, 2, time, boss);
            }

            return boss;
        }

        /// <summary>How big the thing the boss shoots from is at <paramref name="time"/> (Hecatia's Sun): her stars come out of its edge, not its middle.</summary>
        private float stageShotRadius(double time, int rank = 0)
        {
            foreach (var act in worldsActs)
            {
                if (time >= act.Start && time < act.End && act.Period.Rank == rank)
                    return act.RadiusAt(this, 2, time);
            }

            return 0f;
        }

        /// <summary>Does an act of the boss's section put her somewhere (Mokou's jumps) at <paramref name="now"/>?</summary>
        private bool stageBossPosition(BossPeriod period, double now, out Vector2 position)
        {
            foreach (var act in stageActs)
            {
                // (what Yorihime repeats, she does herself: Mokou's jumps are hers while she repeats them)
                bool hers = act.Period == period || (act.Period.Borrowed && act.Period.Rank == period.Rank && now >= act.Period.Start && now <= act.Period.End);

                if (hers && act.BossAt(this, now, out position))
                    return true;
            }

            position = default;
            return false;
        }

        /// <summary>Is the boss kept in the middle of the screen right now (Yukari and Utsuho during their attacks, Hecatia while her worlds turn, Nue while her UFOs gather)?</summary>
        private bool bossAtCentre(BossPeriod period, double now)
        {
            // (Yorihime stands where the boss she is repeating would stand)
            if (period.Boss?.Mechanic == BossMechanic.Return)
            {
                foreach (var other in bosses)
                {
                    if (other.Borrowed && other.Rank == period.Rank && now >= other.Start && now <= other.End && bossAtCentre(other, now))
                        return true;
                }
            }

            switch (period.Boss?.Mechanic)
            {
                case BossMechanic.Nuclear:
                case BossMechanic.FantasySeal:
                case BossMechanic.Rays: // nor does Miko
                case BossMechanic.Purification: // Junko never leaves the middle
                case BossMechanic.Anchors: // nor does Murasa
                    return true;

                case BossMechanic.Unknown:
                    foreach (var act in ufoActs)
                    {
                        if (act.Period == period && now >= act.End && now <= act.ShootEnd)
                            return true;
                    }

                    return false;

                case BossMechanic.ThreeWorlds:
                    // Hecatia goes to the middle while the worlds turn round her
                    foreach (var act in worldsActs)
                    {
                        if (act.Period == period && !act.Throw && now >= act.Start && now <= act.End)
                            return true;
                    }

                    return false;

                case BossMechanic.Gaps:
                    foreach (var (start, end) in BossOps.Attacks(period))
                    {
                        if (now >= start && now <= end)
                            return true;
                    }

                    return false;

                case BossMechanic.FreeFall:
                    return DanmakusuBalance.TOYOHIME_REWORK && toyohimeAtCentre(period, now);

                default:
                    return false;
            }
        }

        private bool askingForToyohime;

        /// <summary>
        /// Toyohime stands where the boss beside her does not: in the middle while that one stands opposite the player, opposite the player while that one is in the middle (or in a spinner); with two others on the screen always in the middle.
        /// Alone she stands opposite the player, unless the boss she is playing in an attack would stand in the middle (see the end of BulletPlanner.Plan).
        /// </summary>
        private bool toyohimeAtCentre(BossPeriod period, double now)
        {
            // (asked about her by the other boss: she is not in the middle for this, so that the two never ask each other round and round)
            if (askingForToyohime)
                return false;

            // (the boss she is playing in this attack: where that one would stand. Not when she is herself being repeated by Yorihime)
            if (!period.Borrowed)
            {
                foreach (var other in bosses)
                {
                    if (other != period && other.Borrowed && other.Rank == period.Rank && now >= other.Start && now <= other.End && bossAtCentre(other, now))
                        return true;
                }
            }

            int others = 0;
            bool otherInMiddle = false;
            askingForToyohime = true;

            try
            {
                for (int rank = 0; rank < 3; rank++)
                {
                    if (rank == period.Rank)
                        continue;

                    var other = latestBoss(now, rank);

                    if (other == null || other.Boss == period.Boss)
                        continue;

                    others++;
                    otherInMiddle |= other.Kind == BossKind.Spinner || bossAtCentre(other, now);
                }
            }
            finally
            {
                askingForToyohime = false;
            }

            return others >= 2 || (others == 1 && !otherInMiddle);
        }

        // ---- Raiko's ripples ----

        /// <summary>How much bigger things are at <paramref name="p"/> right now because a ripple has just gone over it (1 = not at all). Things grow as the front of the ripple reaches them and shrink again as its back leaves them, smoothly.</summary>
        private float rippleGrow(Vector2 p)
        {
            if (stageRippleCount == 0)
                return 1f;

            float speed = DanmakusuBalance.RAIKO_RIPPLE_SPEED;
            float depth = speed * (float)DanmakusuBalance.RAIKO_RIPPLE_DEPTH_MS;
            float best = 0f;

            for (int i = 0; i < stageRippleCount; i++)
            {
                var ripple = stageRipples[i];
                float r = speed * (float)(stageRippleTime - ripple.Time);

                if (r <= 0f)
                    continue;

                best = Math.Max(best, swathStrength(p, ripple.Centre, r, depth));

                if (best >= 1f)
                    break;
            }

            return 1f + (DanmakusuBalance.RAIKO_RIPPLE_GROW - 1f) * best;
        }

        /// <summary>0..1: how far into the band of a ripple (its outer edge at <paramref name="r"/>, <paramref name="depth"/> deep) a point is: rises over the front of the band and falls over its back.</summary>
        private static float swathStrength(Vector2 p, Vector2 centre, float r, float depth)
        {
            float d = Vector2.Distance(p, centre);
            float ramp = Math.Max(depth * DanmakusuBalance.RAIKO_RIPPLE_RAMP, 1f);
            float front = (r + DanmakusuBalance.RAIKO_RIPPLE_MARGIN - d) / ramp; // (0 where the ripple is yet to come, 1 once it has gone over)
            float back = (d - (r - depth - DanmakusuBalance.RAIKO_RIPPLE_MARGIN)) / ramp; // (1 until the band's back edge, 0 where it has left)
            float u = Math.Clamp(Math.Min(front, back), 0f, 1f);

            return u * u * (3f - 2f * u);
        }

        // ---- the black hole's light bending ----

        /// <summary>Where a thing at <paramref name="p"/> is seen when the black hole bends the light: pushed out of the hole (the apparent position of a source behind a point mass).</summary>
        private Vector2 lens(Vector2 p)
        {
            Vector2 d = p - lensCentre;
            float r = d.Length;

            if (r < 1e-3f)
                return lensCentre + new Vector2(lensDistance(0f), 0f);

            return lensCentre + d * (lensDistance(r) / r);
        }

        /// <summary>
        /// How far from the hole's middle a thing <paramref name="r"/> away from it is seen: pushed out as by a real lens (to (r + √(r² + 4L²)) / 2, L the lens's radius), but moved in again by what lies
        /// between the dark's edge and the lens's radius, less and less further out: what is right behind the hole is seen at the edge of the dark, with no empty ring round it.
        /// </summary>
        private float lensDistance(float r)
        {
            float primary = (r + MathF.Sqrt(r * r + 4f * lensRadius * lensRadius)) * 0.5f;
            float gap = Math.Max(lensRadius - Math.Min(lensEdge, lensRadius), 0f);
            return primary - gap * lensRadius / Math.Max(primary, 1e-3f);
        }

        // ---- beams: more points where something has to be seen to happen along them ----

        private Vector2[] refinePosition = new Vector2[512];
        private float[] refineWidth = new float[512];
        private float[] refineDistance = new float[512];
        private BulletLook?[] refineLook = new BulletLook?[512];

        /// <summary>Does anything that changes the width or the place of a beam along its length (a ripple, the black hole) need more points on it?</summary>
        private bool beamsNeedRefining => stageRippleCount > 0 || stageSmokeCount > 0 || sukunaLevel > 0f || stageHideCount > 0 || lensOn;

        /// <summary>
        /// Splits the long segments of the beam in the buffers (<paramref name="count"/> points) into pieces about 12 pixels long, so that a ripple can make it fat in one place
        /// and the black hole can bend it. The new points have the width, the distance flown and the look of the segment they are on. Returns the new count.
        /// </summary>
        private int refineBeam(int count)
        {
            const float piece = 12f;
            const int most_per_segment = 80;

            if (count < 2)
                return count;

            int total = 1;

            for (int s = 0; s + 1 < count; s++)
                total += Math.Clamp((int)MathF.Ceiling(Vector2.Distance(laserBuffer[s], laserBuffer[s + 1]) / piece), 1, most_per_segment);

            if (total == count || total > 3000)
                return count;

            if (laserBuffer.Length < total + 16)
            {
                // (the buffers that the beams are read into grow once, the first time a beam needs more points than it has)
                int size = total + 16;
                Array.Resize(ref laserBuffer, size);
                Array.Resize(ref laserWidthBuffer, size);
                Array.Resize(ref laserDistanceBuffer, size);
                Array.Resize(ref laserLookBuffer, size);
            }

            if (sliceGone.Length < total + 16)
                Array.Resize(ref sliceGone, total + 16);

            if (refinePosition.Length < total)
            {
                int size = Math.Max(total, refinePosition.Length * 2);
                Array.Resize(ref refinePosition, size);
                Array.Resize(ref refineWidth, size);
                Array.Resize(ref refineDistance, size);
                Array.Resize(ref refineLook, size);
            }

            int n = 0;

            for (int s = 0; s + 1 < count; s++)
            {
                int pieces = Math.Clamp((int)MathF.Ceiling(Vector2.Distance(laserBuffer[s], laserBuffer[s + 1]) / piece), 1, most_per_segment);

                for (int k = 0; k < pieces; k++)
                {
                    float u = k / (float)pieces;
                    refinePosition[n] = Vector2.Lerp(laserBuffer[s], laserBuffer[s + 1], u);
                    refineWidth[n] = laserWidthBuffer[s] + (laserWidthBuffer[s + 1] - laserWidthBuffer[s]) * u;
                    refineDistance[n] = laserDistanceBuffer[s] + (laserDistanceBuffer[s + 1] - laserDistanceBuffer[s]) * u;
                    refineLook[n] = laserLookBuffer[s];
                    n++;
                }
            }

            refinePosition[n] = laserBuffer[count - 1];
            refineWidth[n] = laserWidthBuffer[count - 1];
            refineDistance[n] = laserDistanceBuffer[count - 1];
            refineLook[n] = laserLookBuffer[count - 1];
            n++;

            Array.Copy(refinePosition, laserBuffer, n);
            Array.Copy(refineWidth, laserWidthBuffer, n);
            Array.Copy(refineDistance, laserDistanceBuffer, n);
            Array.Copy(refineLook, laserLookBuffer, n);

            return n;
        }

        // ---- drawing the stages ----

        /// <summary>The big round bullets drawn like Mokou's ball this frame (Utsuho's, see <see cref="BulletEmitter.Sharp"/>): where, how big, what colour.</summary>
        private readonly List<(Vector2 Position, float Radius, Color4 Colour)> sharpOrbs = new List<(Vector2, float, Color4)>();

        /// <summary>Tojiko's clouds this frame: the renderer draws them, between the beams and the bullets.</summary>
        private readonly List<CloudSprite> stormClouds = new List<CloudSprite>();

        /// <summary>The flat shapes of the stages this frame (Mima's moon): the renderer draws them over the beams and under the bullets.</summary>
        private readonly List<MeshQuad> stageMeshes = new List<MeshQuad>();

        private void drawStage(double t)
        {
            stageBack.Begin();
            stageOrbs.Begin();
            stageBulletsUnder.Begin();
            stageBullets.Begin();
            stageBats.Begin();
            stageCloud.Begin();
            stageEnder.Begin();
            stageRipple.Begin();
            stageFront.Begin();
            stageTop.Begin();
            stageTree.Begin();
            stageRipple.SetClip(stageMin, stageMax);

            foreach (var piece in portalPieces)
                piece.Alpha = 0f;

            deathBackground.Alpha = 0f;
            miracleBackground.Alpha = 0f;
            stampedeBackground.Alpha = 0f;
            stormBackground.Alpha = 0f;
            tideBackground.Alpha = 0f;
            mikoBackground.Alpha = 0f;
            kaguyaBackground.Alpha = 0f;

            if (hasScreenBounds)
            {
                verdantBackground.Position = screenMin;
                verdantBackground.Size = screenMax - screenMin;
            }

            verdantBackground.Alpha = failed || verdantWindows.Count == 0 ? 0f : attackPresence(verdantWindows, t) * DanmakusuBalance.DAIYOUSEI_BACKGROUND_ALPHA;
            drawHasteBackground(t);

            stormClouds.Clear();
            stageMeshes.Clear();
            drawImpactFrame(t);
            drawShionArrows(t);

            foreach (var act in stageActs)
                act.Draw(this, t);

            // (after the acts: what does not fit in the layer is a flash, not something of theirs)
            drawRicochetFlashes(t);

            renderer.SetClouds(stormClouds);
            renderer.SetMeshes(stageMeshes);

            foreach (var (position, radius, colour) in sharpOrbs)
            {
                // (the black hole bends their light too, as it does every bullet's: pushed out of it and stretched round it)
                Vector2 seen = position;
                float stretch = 1f, around = 0f;

                if (lensOn)
                {
                    Vector2 d = position - lensCentre;
                    float r = d.Length;

                    if (r > 1e-3f)
                    {
                        float primary = lensDistance(r);
                        seen = lensCentre + d * (primary / r);
                        stretch = Math.Min(primary / r, DanmakusuBalance.UTSUHO_LENS_MAX_STRETCH);
                        around = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI + 90f;
                    }
                }

                mokouOrb(this, seen, radius, colour, bodyColour: colour, into: stageOrbs, stretch: stretch, degrees: around);
            }

            stageBack.End();
            stageOrbs.End();
            stageBulletsUnder.End();
            stageBullets.End();
            stageBats.End();
            stageCloud.End();
            stageEnder.End();
            stageRipple.End();
            stageFront.End();
            stageTop.End();
            stageTree.End();
        }
    }
}
