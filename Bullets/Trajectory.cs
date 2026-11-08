using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>What happens to the flight of a bullet (or of a slice of a beam) at some moment (see <see cref="TrajectoryOp"/>).</summary>
    public enum TrajectoryOpKind
    {
        /// <summary>In the window [Time, End] the thing bounces once off the border of the screen (the first time it touches it).</summary>
        Bounce,

        /// <summary>From Time to End the thing stands still (only things that existed at Time), then it flies on as if nothing had happened.</summary>
        Freeze,

        /// <summary>At Time the thing is mirrored through Point (along both axes), and so is its way.</summary>
        Mirror,

        /// <summary>At Time the thing turns and flies straight at Point at its own speed; it is gone when it gets there.</summary>
        Redirect,

        /// <summary>At Time the thing is gone (something else takes its place).</summary>
        Remove,

        /// <summary>In the window [Time, End] the thing bounces off Kyouko's two glass walls (the way across a wall is turned round, the speed stays), twice at most (see <see cref="TrajectoryOp.Horizontal"/>).</summary>
        Glass,

        /// <summary>In the window [Time, End] the first time the thing leaves the top of the screen it falls on like a petal (see <see cref="TrajectoryOp.FallSpeed"/>).</summary>
        Fall,

        /// <summary>At Time the thing starts to be pulled straight away from Point, at <see cref="TrajectoryOp.Push"/> (the way as it is then; see <see cref="Coast"/>).</summary>
        Outward,

        /// <summary>At Time what pulled the thing is over: it flies on at the speed it has got.</summary>
        Coast,

        /// <summary>At Time the thing turns round: it flies back the way it came, at the speed it had.</summary>
        Reverse,

        /// <summary>
        /// In the window [Time, End] the first time the thing flies into Mima's moon (the disc of <see cref="TrajectoryOp.Radius"/> round Point, less the disc of <see cref="TrajectoryOp.InnerRadius"/> round
        /// <see cref="TrajectoryOp.InnerPoint"/>) it bounces off its edge. What is in its body already when the window begins, or is fired there, flies through.
        /// </summary>
        Crescent,
    }

    /// <summary>How a ring that is made out of other bullets gets its bullets (the field does it when the ring fires, see <see cref="BulletEmitter.Derive"/>).</summary>
    public enum DeriveKind
    {
        None,

        /// <summary>Every bullet of the source ring (where it is, frozen, at this moment) bursts: this ring has one bullet for each, flying on in the source bullet's direction turned by FanOffset.</summary>
        Explode,

        /// <summary>The knives that hung round the boss fly out at the player, mirrored through the centre of the screen (see HeldOffsets).</summary>
        Held,

        /// <summary>Some bullets of the source ring (EchoIndices) are thrown out again from the boss, flying away from the middle of the playfield.</summary>
        Echo,

        /// <summary>A train from the other side of the screen to where the player is.</summary>
        Train,

        /// <summary>Letty: a slice of a beam (SourceLaser, fired at SliceTime) dissipates into bullets, one for every beam, flying against the way the slider's path goes.</summary>
        Dissipate,

        /// <summary>Mima's moon is gone: its stars start all along its edges and fly off them (the field asks her act where).</summary>
        MoonEdges,
    }

    /// <summary>A picture that is drawn on a bullet: a snowflake in its body.</summary>
    public enum BulletDecal
    {
        None,
        Snowflake,

        /// <summary>Zanmu's: a skull that fits into the body.</summary>
        Skull,
    }

    /// <summary>How a bullet looks once something has happened to it: its shape changes, a colour is shown over its own, a decal is put on it, it gets smaller or bigger.</summary>
    public sealed class BulletLook
    {
        /// <summary>The bullet is drawn as this shape (null = its own).</summary>
        public BulletShape? Shape { get; init; }

        /// <summary>0 = its own colour; otherwise one of the BulletRenderer.TINT_ colours, shown <see cref="OverlayAmount"/> of the way over it.</summary>
        public int Overlay { get; init; }

        public float OverlayAmount { get; init; } = 1f;

        public BulletDecal Decal { get; init; }

        /// <summary>The bullet is drawn this much bigger (its hitbox keeps its size).</summary>
        public float Scale { get; init; } = 1f;

        /// <summary>How far the bullet has turned into <see cref="Shape"/> (0 = still its own shape, 1 = all of the way): the two are drawn faded into each other.</summary>
        public float Morph { get; init; } = 1f;

        /// <summary>How much of the bullet can be seen while the freeze is on (1 = all of it, see Reisen).</summary>
        public float Alpha { get; init; } = 1f;

        /// <summary>The bullet does not hurt (a bullet of Reisen's that slides).</summary>
        public bool Harmless { get; init; }

        /// <summary>-1 / 1: the thing is going to slide (or is sliding) to the left / right, 0 = it does not.</summary>
        public int SlideSign { get; init; }

        /// <summary>The slide is up (-1) / down (1) instead of to the left / the right.</summary>
        public bool SlideVertical { get; init; }

        /// <summary>Zanmu: when above 0 the thing has been stopped by her ripple and falls from this game time on, getting darker as it falls.</summary>
        public double FallsAt { get; init; }

        /// <summary>When above 0 the thing shakes (1-2 px) from this game time until <see cref="ShakeTo"/> (Cirno's frozen bullets before they burst, Zanmu's before they fall).</summary>
        public double ShakeFrom { get; init; }

        public double ShakeTo { get; init; }

        /// <summary>Zanmu: a beam slice that is slowing down turns grey in steps: this is how far it has got (0..<see cref="DanmakusuBalance.ZANMU_FADE_STEPS"/>, a thing that is all the way grey has the last); -1 = not a step of it.</summary>
        public int Level { get; init; } = -1;

        /// <summary>Zanmu: this slice of a beam is shaking (1-2 px) just now, and how dark (0..1) it is from falling.</summary>
        public bool Shaking { get; init; }

        public float Dark { get; init; }

        /// <summary>How many portals (Okina's) the thing has been through: a beam is cut where two neighbouring slices have not been through the same number.</summary>
        public int Portal { get; init; }

        /// <summary>Flandre's eye has blown this slice of a beam up: it is gone (the beam has a gap there, or ends).</summary>
        public bool Gone { get; init; }

        /// <summary>How much can be seen of a piece of a beam that something has stretched far past its length (1: all of it; it fades out, and hurts no more once it is half gone: see <see cref="LaserEmitter"/>).</summary>
        public float StretchVisible { get; init; } = 1f;

        private static readonly Dictionary<(BulletLook?, int), BulletLook> stretched = new Dictionary<(BulletLook?, int), BulletLook>();

        /// <summary>The same look (null = no look) for a piece of a beam that is <paramref name="step"/> / <paramref name="steps"/> visible. The same look and step always give the same object (pieces are told apart by reference).</summary>
        public static BulletLook Stretched(BulletLook? look, int step, int steps)
        {
            lock (stretched)
            {
                if (stretched.TryGetValue((look, step), out var known))
                    return known;

                float visible = Math.Clamp(step / (float)steps, 0f, 1f);
                var made = look == null
                    ? new BulletLook { StretchVisible = visible }
                    : new BulletLook
                    {
                        Shape = look.Shape, Overlay = look.Overlay, OverlayAmount = look.OverlayAmount, Decal = look.Decal, Scale = look.Scale, Morph = look.Morph, Alpha = look.Alpha,
                        Harmless = look.Harmless, SlideSign = look.SlideSign, SlideVertical = look.SlideVertical, FallsAt = look.FallsAt, ShakeFrom = look.ShakeFrom, ShakeTo = look.ShakeTo,
                        Level = look.Level, Shaking = look.Shaking, Dark = look.Dark, Portal = look.Portal, Gone = look.Gone, StretchVisible = visible,
                    };

                stretched[(look, step)] = made;
                return made;
            }
        }

        private static readonly Dictionary<BulletLook, BulletLook> blasted = new Dictionary<BulletLook, BulletLook>();

        private static readonly BulletLook gone_only = new BulletLook { Gone = true };

        /// <summary>The same look (null = no look) for a slice that an explosion has taken. The same look always gives the same object, so that neighbours can be told apart by reference.</summary>
        public static BulletLook Blasted(BulletLook? look)
        {
            if (look == null)
                return gone_only;

            if (look.Gone)
                return look;

            lock (blasted)
            {
                if (blasted.TryGetValue(look, out var known))
                    return known;

                var made = new BulletLook
                {
                    Shape = look.Shape, Overlay = look.Overlay, OverlayAmount = look.OverlayAmount, Decal = look.Decal, Scale = look.Scale, Morph = look.Morph, Alpha = look.Alpha,
                    Harmless = look.Harmless, SlideSign = look.SlideSign, SlideVertical = look.SlideVertical, FallsAt = look.FallsAt, ShakeFrom = look.ShakeFrom, ShakeTo = look.ShakeTo,
                    Level = look.Level, Shaking = look.Shaking, Dark = look.Dark, Portal = look.Portal, Gone = true, StretchVisible = look.StretchVisible,
                };

                blasted[look] = made;
                return made;
            }
        }

        private static readonly Dictionary<(BulletLook?, int), BulletLook> portaled = new Dictionary<(BulletLook?, int), BulletLook>();

        /// <summary>The same look (null = no look) for something that has been through <paramref name="times"/> portals. The same look and number always give the same object, so that neighbours can be told apart by reference.</summary>
        public static BulletLook? Portaled(BulletLook? look, int times)
        {
            if (times <= 0)
                return look;

            lock (portaled)
            {
                if (portaled.TryGetValue((look, times), out var known))
                    return known;

                var made = look == null
                    ? new BulletLook { Portal = times }
                    : new BulletLook
                    {
                        Shape = look.Shape, Overlay = look.Overlay, OverlayAmount = look.OverlayAmount, Decal = look.Decal, Scale = look.Scale, Morph = look.Morph, Alpha = look.Alpha,
                        Harmless = look.Harmless, SlideSign = look.SlideSign, SlideVertical = look.SlideVertical, FallsAt = look.FallsAt, ShakeFrom = look.ShakeFrom, ShakeTo = look.ShakeTo,
                        Level = look.Level, Shaking = look.Shaking, Dark = look.Dark, Portal = times, StretchVisible = look.StretchVisible,
                    };

                portaled[(look, times)] = made;
                return made;
            }
        }
    }

    /// <summary>
    /// One thing that happens to every bullet of the field that exists at the right moment. The ops are shared by all rings of a field and sorted by time; a ring only gets
    /// the ones that begin while its bullets are alive (see <see cref="BulletEmitter.ConfigureOps"/>).
    /// </summary>
    public sealed class TrajectoryOp
    {
        public TrajectoryOpKind Kind { get; init; }

        public double Time { get; init; }

        public double End { get; init; }

        /// <summary>Only things that were fired before this are touched (always also before <see cref="Time"/>).</summary>
        public double BornBefore { get; init; } = double.PositiveInfinity;

        /// <summary>The centre of a mirroring, the place a redirected bullet flies to (the field keeps it up to date: it is the centre of the screen).</summary>
        public Vector2 Point { get; set; }

        /// <summary>How the thing looks while the op is on (a frozen bullet).</summary>
        public BulletLook? During { get; init; }

        /// <summary>
        /// Freeze: while the thing stands still it slides by <see cref="SlideDistance"/> px (eased, over the whole freeze), half of them to the left (or up) and half to the right (or down) (see
        /// <see cref="SlideByDirection"/>), and then flies on from there; <see cref="During"/> / <see cref="DuringRight"/> is how it looks for the one side / the other.
        /// </summary>
        public bool Slides { get; init; }

        public float SlideDistance { get; set; }

        /// <summary>The slide is up and down instead of to the left and the right.</summary>
        public bool SlideVertical { get; init; }

        public BulletLook? DuringRight { get; init; }

        /// <summary>Which way a thing slides depends on the way it flies (so that all slices of a beam go the same way) instead of on where it stands.</summary>
        public bool SlideByDirection { get; init; }

        /// <summary>The op leaves every second bullet of a ring alone (the odd ones: Reisen's slide). Things that are not bullets of a ring are all touched.</summary>
        public bool EveryOther { get; init; }

        /// <summary>With <see cref="EveryOther"/>: bullets that go by the clock of their ring (pulled and bouncing ones) cannot be told apart, so every second ring of those is left alone instead (Cirno's).</summary>
        public bool OrEverySecondRing { get; init; }

        /// <summary>When above 0 only this many bullets of every eight (an odd number: they are spread evenly) are touched.</summary>
        public int OfEight { get; init; }

        /// <summary>How the thing looks from the op on (from its end, for a window).</summary>
        public BulletLook? After { get; init; }

        /// <summary>
        /// A freeze with a slow change of look: the looks <see cref="During"/> goes through (the first is none, the last is the whole look; see <see cref="BulletLook.Morph"/>),
        /// shown over <see cref="Blend"/> ms when the freeze begins and the other way round when it ends.
        /// </summary>
        public BulletLook[]? Steps { get; init; }

        /// <summary>Freeze with <see cref="Steps"/>: ms over which the look changes. Redirect: ms over which the thing turns to face the way it now flies (0 = at once).</summary>
        public double Blend { get; init; }

        /// <summary>Fall: pixels per ms.</summary>
        public float FallSpeed { get; init; }

        /// <summary>Fall: how far to either side the thing swings while it falls (0 = straight down) and how long (ms) one swing to and fro takes.</summary>
        public float ZigzagWidth { get; init; }

        public double ZigzagPeriod { get; init; } = 1000;

        /// <summary>Fall: the angle (degrees) off straight down that the thing falls at; half of the things fall this far to the left and half to the right (which one is decided by where and when each leaves the top, so it is always the same).</summary>
        public float FallTilt { get; init; }

        /// <summary>Fall: which part of the things that leave the top (0..1, decided by where and when each leaves) falls on; the others just fly away.</summary>
        public float FallShare { get; init; } = 1f;

        /// <summary>
        /// The op also touches things that are fired while it is on (until <see cref="End"/>): they take part from the moment they are fired. (Without this, an op only
        /// touches what exists when it begins.)
        /// </summary>
        public bool Window { get; init; }

        /// <summary>The op does not touch the rings that were thrown out of a burst (Cirno's thawed bullets: once they are free they cannot be frozen again).</summary>
        public bool SkipBursts { get; init; }

        /// <summary>The op does not touch the big sharp balls (<see cref="BulletEmitter.Sharp"/>: Utsuho's, which her own black hole leaves alone).</summary>
        public bool SkipSharp { get; init; }

        /// <summary>Yuyuko's petals and flowers are left alone (the pull of Utsuho's black hole).</summary>
        public bool SkipPetals { get; init; }

        /// <summary><see cref="Point"/> is not the middle of the screen: an act puts it where it wants it when the op begins (Eiki's circle, Yumemi).</summary>
        public bool PointKept { get; init; }

        /// <summary>Outward: how hard it pulls (px per ms squared).</summary>
        public float Push { get; init; }

        /// <summary>A redirect: the bullet flies at the point at this much of the speed it had (1 = as fast).</summary>
        public float RedirectSpeed { get; init; } = 1f;

        /// <summary>
        /// A redirect: when above 0 it does not happen at <see cref="TrajectoryOp.Time"/> but the moment the thing enters this circle round <see cref="Point"/> (the first time in the window; what is already inside goes at once).
        /// It flies on through the point, and it has its speed back at <see cref="RestoreAt"/> (Eiki's circle: her act says how big it is). Crescent: the radius of Mima's moon (her act sets it).
        /// </summary>
        public float Radius { get; set; }

        /// <summary>A redirect with a <see cref="Radius"/>: a second circle as big round this point (Eiki's mirrored one, set by her act); a thing is taken by the one it enters first, once.</summary>
        public Vector2? SecondPoint { get; set; }

        public double RestoreAt { get; init; } = double.PositiveInfinity;

        /// <summary>Crescent: the middle and the radius of the circle cut out of the moon (its hollow).</summary>
        public Vector2 InnerPoint { get; set; }

        public float InnerRadius { get; set; }

        /// <summary>Crescent: where and when things have bounced off the moon lately (by the moment and the place, roughly, so that what is traced again is not counted twice). The trace adds to it, from any thread; the act draws a flash there and forgets it.</summary>
        public System.Collections.Concurrent.ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>? Bounces { get; init; }

        /// <summary>A redirect: on its way to the point the bullet speeds up evenly to this many times the speed it was sent at, reaching it where it arrives (1 = it keeps its speed).</summary>
        public float RedirectSpeedUp { get; init; } = 1f;

        /// <summary>A redirect: a bullet that would reach the point after this moment (on its own clock) is not taken there: what pulled it is gone by then, and it flies on through (Utsuho's black hole).</summary>
        public double ReleaseAt { get; init; } = double.PositiveInfinity;

        /// <summary>A redirect that takes nothing: what it sends to the point flies on through it and out of the screen (Yukari's talismans).</summary>
        public bool Through { get; init; }

        /// <summary>A freeze whose things face the point (<see cref="Point"/>, the middle of the screen, kept up to date by the field) while they stand still (Yukari's talismans).</summary>
        public bool FacesPoint { get; init; }

        /// <summary>The op is not for the beams that turn (a spinner's, a boss's), only for the others (a slider's).</summary>
        public bool SkipRotating { get; init; }

        /// <summary>Glass: the walls come from the top and the bottom (otherwise from the left and the right).</summary>
        public bool Horizontal { get; init; }

        /// <summary>Glass: the playfield the walls stand in (the field keeps it up to date: it is the screen).</summary>
        public Vector2 GlassMin { get; set; }

        public Vector2 GlassMax { get; set; }

        /// <summary>Glass: how the thing looks after its second bounce (after its first it has <see cref="After"/>); the looks only differ from the thing's own in being other objects, so that a beam gets a corner where it bounced.</summary>
        public BulletLook? AfterSecond { get; init; }
    }

    /// <summary>Where a thing is, and what state it is in, after its flight has been worked out by <see cref="Trajectory.Trace"/>.</summary>
    public struct TrajectoryState
    {
        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>False once the thing is gone (it has been removed, or it has got where it was sent).</summary>
        public bool Alive;

        public bool Frozen;

        /// <summary>Until when (the time asked about or later) the thing stays exactly as it is now: where it stands, looking the same (a freeze that does not slide or blend its look). NaN: not known to stay.</summary>
        public double StillUntil;

        /// <summary>The look the thing has now (null = its own).</summary>
        public BulletLook? Look;

        /// <summary>The way the thing faces if that is not the way it flies (it is still turning after a redirect); zero = the way it flies.</summary>
        public Vector2 Facing;
    }

    /// <summary>
    /// Works out where a thing is at a time, when its flight is a straight line (or a fall with a constant pull) that the ops of a ring change: it is frozen for a while,
    /// mirrored, sent to a point, bounced off the border of the screen, turned into a petal... Everything is a closed form of time (so replays and seeks are exact).
    /// </summary>
    public static class Trajectory
    {
        /// <summary>
        /// Something bounced off what the op is (the screen's border for Seija, Kyouko's glass, Mima's moon) at <paramref name="now"/> at <paramref name="position"/>: the op keeps it for the field to flash there, if it is
        /// recent enough to be seen at <paramref name="time"/> (the same bounce worked out again is kept once).
        /// </summary>
        private static void recordBounce(TrajectoryOp op, double now, Vector2 position, double time)
        {
            if (op.Bounces != null && time - now < DanmakusuBalance.RICOCHET_FLASH_MS && op.Bounces.Count < 512)
                op.Bounces.TryAdd(((int)Math.Floor(now / 25), (int)MathF.Floor(position.X / 8f), (int)MathF.Floor(position.Y / 8f)), (now, position));
        }

        /// <param name="bullet">Which bullet of its ring the thing is (-1: not a bullet of a ring), for the ops that leave every second one alone.</param>
        public static void Trace(Vector2 start, Vector2 velocity, Vector2 acceleration, double t0, double time, TrajectoryOp[] ops, Vector2 min, Vector2 max, float top, out TrajectoryState result,
                                 int bullet = -1)
        {
            Vector2 position = start;
            Vector2 v = velocity;
            Vector2 acc = acceleration;
            double now = t0; // the moment position and v belong to
            BulletLook? look = null;
            double vanish = double.PositiveInfinity;
            Vector2 turnFrom = default, turnTo = default; // a redirect that turns the thing slowly: from which way to which
            double turnAt = 0, turnBlend = 0;
            TrajectoryOp? releasedFrom = null; // the last freeze that changes the look slowly and has ended
            bool pulled = false; // (a black hole has pulled it already: no later one does)
            bool reversed = false; // (Shion has turned it round already: no later turn of hers does)
            double restoreAt = double.PositiveInfinity; // (when what slowed it down is gone, and how many times as fast it is then)
            float restoreGain = 1f;

            result = new TrajectoryState { Alive = true, StillUntil = double.NaN };

            // a thing that has left the top of the screen and falls on (Yuyuko's petals): where it is is a closed form of its age, and the freezes of the bosses that come later stop it for as long as they last
            TrajectoryOp? fallOp = null;
            double fallStart = 0, fallLost = 0, fallFrozenUntil = double.NegativeInfinity;
            float fallX = 0f, fallLean = 0f;

            void fallAt(double age, out Vector2 where, out Vector2 how)
            {
                float a = (float)Math.Max(age, 0);
                where = new Vector2(fallX + zigzag(fallOp!, a) + fallLean * a, top + fallOp!.FallSpeed * a);
                how = new Vector2(zigzag(fallOp, a + 1f) - zigzag(fallOp, a) + fallLean, fallOp.FallSpeed);
            }

            foreach (var op in ops)
            {
                if ((op.Time <= t0 && !op.Window) || t0 >= op.BornBefore)
                    continue; // it did not exist yet when this began

                if (op.Window && t0 > op.End)
                    continue; // (it was fired after the window)

                if (op.OfEight > 0 && bullet >= 0 && bullet * op.OfEight % 8 >= op.OfEight)
                    continue;

                if (op.EveryOther && bullet >= 0 && bullet % 2 == 1)
                    continue; // (every second bullet is left alone)

                if (op.Time > time)
                    break;

                if (fallOp != null)
                {
                    // (it is falling: only a freeze that begins after it has left the top acts on it; the others leave it alone)
                    if (op.Kind == TrajectoryOpKind.Freeze && op.Time > fallStart)
                    {
                        double begins = Math.Max(op.Time, fallFrozenUntil);

                        if (op.End > begins)
                        {
                            if (time <= op.End)
                            {
                                fallAt(begins - fallStart - fallLost, out Vector2 stoppedAt, out _);
                                result.Position = stoppedAt;
                                result.Velocity = Vector2.Zero;
                                result.Frozen = true;
                                result.Look = op.During ?? look;
                                result.StillUntil = op.End;
                                return;
                            }

                            fallLost += op.End - begins;
                            fallFrozenUntil = op.End;
                            look = op.After ?? look;
                        }
                    }

                    continue;
                }

                if (restoreAt <= op.Time)
                    regain(ref position, ref v, acc, ref now, ref restoreAt, restoreGain);

                switch (op.Kind)
                {
                    case TrajectoryOpKind.Freeze:
                    {
                        // (a thing that another boss's freeze already holds when a slide begins has nothing to slide: it stays as that freeze says, and is let go or taken by it - Cirno's bursts)
                        if (op.Slides && op.Time < now - 1e-6)
                            break;

                        step(ref position, ref v, acc, ref now, op.Time);
                        // (a beam's slices slide by the way they were fired, which they all share: by the way they fly now, after Eiki's circle has turned each to its middle a little differently, every one went its own way, the
                        // beam a zigzag of slices half a screen apart)
                        int side = op.Slides ? slideSide(op, position, op.SlideByDirection ? velocity : v) : 0;

                        // (a slide runs to its end: nothing that begins meanwhile cuts it short)
                        double ends = op.End;
                        double slideSpan = Math.Max(op.End - op.Time, 1e-6);

                        if (time <= ends)
                        {
                            result.Position = position;

                            if (side != 0)
                                result.Position += slideOffset(op, side, (time - op.Time) / slideSpan);

                            result.Velocity = v;
                            result.Frozen = true;
                            result.Look = op.Steps != null && op.Blend > 0 ? StepLook(op, (time - op.Time) / op.Blend) : (side > 0 ? op.DuringRight ?? op.During : op.During) ?? look;

                            // (Yukari's talismans face the middle of the screen while they stand still: where they will fly. The velocity it stands with points there too, as fast as it flew: what goes by the way a
                            // thing flies - the bullets of the acts, drawn and hit by it - had them face the way they had come, lying flat when they had none)
                            if (op.FacesPoint && (op.Point - result.Position).LengthSquared > 1e-6f)
                            {
                                result.Facing = Vector2.Normalize(op.Point - result.Position);
                                result.Velocity = result.Facing * Math.Max(v.Length, 1e-3f);
                            }

                            // (nothing that comes later in the freeze is looked at: it stands like this to the end, unless it slides or its look changes step by step)
                            if (side == 0 && !(op.Steps != null && op.Blend > 0))
                                result.StillUntil = ends;

                            return;
                        }

                        if (side != 0)
                            position += slideOffset(op, side, (ends - op.Time) / slideSpan); // (it has slid, and flies on from there)

                        now = Math.Max(now, ends); // (the time of the freeze is skipped)
                        look = op.After ?? look;

                        if (op.Steps != null && op.Blend > 0)
                            releasedFrom = op;

                        break;
                    }

                    case TrajectoryOpKind.Mirror:
                        step(ref position, ref v, acc, ref now, op.Time);
                        position = op.Point * 2f - position;
                        v = -v;
                        acc = -acc;
                        look = op.After ?? look;
                        break;

                    case TrajectoryOpKind.Reverse:
                        if (reversed)
                            break;

                        reversed = true;
                        step(ref position, ref v, acc, ref now, op.Time);
                        v = -v;
                        acc = -acc;
                        look = op.After ?? look;
                        break;

                    case TrajectoryOpKind.Outward:
                    {
                        step(ref position, ref v, acc, ref now, op.Time);
                        Vector2 away = position - op.Point;

                        if (away.LengthSquared > 1e-6f)
                            acc = Vector2.Normalize(away) * op.Push;

                        break;
                    }

                    case TrajectoryOpKind.Coast:
                        step(ref position, ref v, acc, ref now, op.Time);
                        acc = Vector2.Zero;
                        break;

                    case TrajectoryOpKind.Redirect:
                    {
                        if (op.Radius > 0f)
                        {
                            double from = Math.Max(op.Time, now);
                            step(ref position, ref v, acc, ref now, from);
                            double enters = entersCircle(position, v, acc, op.Point, op.Radius, Math.Min(op.End, time) - from);
                            Vector2 middle = op.Point;

                            if (op.SecondPoint is Vector2 second)
                            {
                                double entersSecond = entersCircle(position, v, acc, second, op.Radius, Math.Min(op.End, time) - from);

                                if (entersSecond < enters)
                                {
                                    enters = entersSecond;
                                    middle = second;
                                }
                            }

                            if (double.IsPositiveInfinity(enters))
                                break;

                            step(ref position, ref v, acc, ref now, from + enters);
                            Vector2 inwards = middle - position;
                            float across = inwards.Length;

                            v = across > 1e-3f ? inwards / across * (v.Length * op.RedirectSpeed) : v * op.RedirectSpeed;
                            acc = Vector2.Zero;
                            restoreAt = op.RestoreAt;
                            restoreGain = 1f / op.RedirectSpeed;
                            look = op.After ?? look;
                            break;
                        }

                        // (Utsuho's pulls - the ones with a release - take a thing once: what one black hole let go flies on past the next)
                        if (op.ReleaseAt < double.PositiveInfinity)
                        {
                            if (pulled)
                                break;

                            pulled = true;
                        }

                        step(ref position, ref v, acc, ref now, op.Time);
                        float speed = v.Length * op.RedirectSpeed;
                        Vector2 toTarget = op.Point - position;
                        float distance = toTarget.Length;

                        if (speed < 1e-6f || distance < 1e-3f)
                            vanish = Math.Min(vanish, now);
                        else
                        {
                            if (op.Blend > 0)
                            {
                                turnFrom = v / speed;
                                turnTo = toTarget / distance;
                                turnAt = now;
                                turnBlend = op.Blend;
                            }

                            Vector2 way = toTarget / distance;
                            v = way * speed;
                            acc = Vector2.Zero;

                            // (it is taken where it arrives, unless the pull is over by then: then it flies on through at the speed it has, see ReleaseAt)
                            double arrives = distance / speed;

                            if (op.RedirectSpeedUp > 1f)
                            {
                                // (it speeds up evenly on its way, from its speed to RedirectSpeedUp times it where it arrives: the closer, the faster)
                                double speeding = distance / (speed * (1.0 + op.RedirectSpeedUp) * 0.5);

                                if (now + speeding <= op.ReleaseAt)
                                {
                                    arrives = speeding;
                                    acc = way * (float)(speed * (op.RedirectSpeedUp - 1f) / speeding);
                                }
                            }

                            if (now + arrives <= op.ReleaseAt && !op.Through)
                                vanish = Math.Min(vanish, now + arrives);
                            look = op.After ?? look;
                        }

                        break;
                    }

                    case TrajectoryOpKind.Crescent:
                    {
                        // (Mima's moon, once her act has placed it)
                        if (op.Radius <= 0f)
                            break;

                        double from = Math.Max(op.Time, now);
                        step(ref position, ref v, acc, ref now, from);
                        double limit = Math.Min(op.End, time) - from;

                        // (what is in its body already flies through)
                        if (limit <= 0 || crescentGap(op, position, out _) < 0f)
                            break;

                        double hit = entersCrescent(op, position, v, acc, limit, out bool inner);

                        if (double.IsPositiveInfinity(hit))
                            break;

                        step(ref position, ref v, acc, ref now, from + hit);
                        Vector2 normal = position - (inner ? op.InnerPoint : op.Point);

                        if (normal.LengthSquared > 1e-6f)
                        {
                            normal = Vector2.Normalize(normal);
                            v -= normal * (2f * Vector2.Dot(v, normal));
                        }

                        recordBounce(op, now, position, time);

                        look = op.After ?? look;
                        break;
                    }

                    case TrajectoryOpKind.Remove:
                        result.Alive = false;
                        result.Position = position;
                        return;

                    case TrajectoryOpKind.Bounce:
                    {
                        double from = Math.Max(op.Time, now);
                        step(ref position, ref v, acc, ref now, from);

                        if (position.X < min.X || position.X > max.X || position.Y < min.Y || position.Y > max.Y)
                            break; // it was already outside the screen

                        double limit = Math.Min(op.End, time) - from;
                        double tx = leave(position.X, v.X, acc.X, min.X, max.X, limit);
                        double ty = leave(position.Y, v.Y, acc.Y, min.Y, max.Y, limit);
                        double hit = Math.Min(tx, ty);

                        if (double.IsPositiveInfinity(hit))
                            break;

                        float h = (float)hit;
                        Vector2 hp = Vector2.Clamp(position + v * h + acc * (0.5f * h * h), min, max);
                        Vector2 hv = v + acc * h;

                        if (tx <= hit + 1e-6)
                            hv.X = -hv.X;

                        if (ty <= hit + 1e-6)
                            hv.Y = -hv.Y;

                        position = hp;
                        v = hv;
                        now = from + hit;
                        look = op.After ?? look;
                        recordBounce(op, now, position, time);
                        break;
                    }

                    case TrajectoryOpKind.Glass:
                    {
                        step(ref position, ref v, acc, ref now, Math.Max(op.Time, now));

                        for (int bounce = 0; bounce < DanmakusuBalance.KYOUKO_BOUNCES; bounce++)
                        {
                            double limit = Math.Min(op.End, time) - now;

                            if (limit <= 1e-9)
                                break;

                            double hit = glassContact(op, position, v, acc, now, limit);

                            if (double.IsPositiveInfinity(hit))
                                break;

                            float h = (float)hit;
                            Vector2 hp = position + v * h + acc * (0.5f * h * h);
                            Vector2 hv = v + acc * h;

                            if (op.Horizontal)
                            {
                                hv.Y = -hv.Y;
                                hp.Y += hv.Y > 0f ? 0.01f : -0.01f; // (a hair off the wall, so that it is not found on it again)
                            }
                            else
                            {
                                hv.X = -hv.X;
                                hp.X += hv.X > 0f ? 0.01f : -0.01f;
                            }

                            position = hp;
                            v = hv;
                            now += hit;
                            look = (bounce == 0 ? op.After : op.AfterSecond) ?? look;
                            recordBounce(op, now, position, time);
                        }

                        break;
                    }

                    case TrajectoryOpKind.Fall:
                    {
                        double from = Math.Max(op.Time, now);
                        step(ref position, ref v, acc, ref now, from);

                        if (position.Y < top)
                            break; // it is already above the screen

                        double limit = Math.Min(op.End, time) - from;
                        double crossing = leave(position.Y, v.Y, acc.Y, top, float.MaxValue, limit);

                        if (double.IsPositiveInfinity(crossing))
                            break;

                        // it has left the top of the screen at `crossing`: from there it falls on
                        float c = (float)crossing;
                        float x = position.X + v.X * c + acc.X * (0.5f * c * c);
                        double tc = from + crossing;

                        float lean = 0f;

                        uint h = (uint)BitConverter.SingleToInt32Bits(x) * 2654435761u ^ (uint)(int)Math.Round(tc) * 40503u;
                        h ^= h >> 16;
                        h *= 0x45d9f3bu;
                        h ^= h >> 16;

                        // (only part of the things turn into falling flowers; the others fly out of the screen as they would)
                        if (op.FallShare < 1f && ((h >> 8) & 1023u) >= (uint)(op.FallShare * 1024f))
                            break;

                        if (op.FallTilt != 0f)
                            lean = ((h & 1u) == 0 ? -1f : 1f) * MathF.Tan(op.FallTilt * MathF.PI / 180f) * op.FallSpeed;

                        fallOp = op;
                        fallStart = tc;
                        fallX = x;
                        fallLean = lean;
                        look = op.After ?? look;
                        break;
                    }
                }
            }

            if (fallOp != null)
            {
                fallAt(time - fallStart - fallLost, out Vector2 fallingAt, out Vector2 fallingHow);
                result.Position = fallingAt;
                result.Velocity = fallingHow;
                result.Look = look;
                return;
            }

            if (restoreAt <= time)
                regain(ref position, ref v, acc, ref now, ref restoreAt, restoreGain);

            step(ref position, ref v, acc, ref now, time);

            result.Position = position;
            result.Velocity = v;
            result.Look = look;

            if (turnBlend > 0 && time < turnAt + turnBlend)
            {
                // (still turning: by the short way round, eased at both ends)
                double u = Math.Clamp((time - turnAt) / turnBlend, 0, 1);
                float t = (float)(u * u * (3 - 2 * u));
                float from = MathF.Atan2(turnFrom.Y, turnFrom.X);
                float turn = MathF.Atan2(turnTo.Y, turnTo.X) - from;

                while (turn > MathF.PI)
                    turn -= MathF.Tau;

                while (turn < -MathF.PI)
                    turn += MathF.Tau;

                float angle = from + turn * t;
                result.Facing = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            if (releasedFrom != null && look == null)
            {
                // (it is turning back into what it was)
                double back = (time - releasedFrom.End) / releasedFrom.Blend;

                if (back < 1)
                    result.Look = StepLook(releasedFrom, 1 - back);
            }

            if (time >= vanish)
                result.Alive = false;
        }

        /// <summary>How far a thing that slides has got <paramref name="amount"/> (0..1) of the way (eased at both ends): to the left or the right (<paramref name="side"/> -1 / 1), or up or down for a vertical slide.</summary>
        private static Vector2 slideOffset(TrajectoryOp op, int side, double amount)
        {
            double x = Math.Clamp(amount, 0, 1);
            float distance = side * op.SlideDistance * (float)(x * x * (3 - 2 * x));
            return op.SlideVertical ? new Vector2(0f, distance) : new Vector2(distance, 0f);
        }

        /// <summary>-1 / 1: the side a frozen thing slides to (a hash of where it stands, or of the way it flies), about half of them each.</summary>
        private static int slideSide(TrajectoryOp op, Vector2 position, Vector2 v)
        {
            unchecked
            {
                uint h = (uint)(op.Time * 0.37);

                if (op.SlideByDirection)
                    h = h * 2654435761u + (uint)(int)(MathF.Atan2(v.Y, v.X) * 1000f);
                else
                    h = (h * 2654435761u + (uint)(int)(position.X * 7.3f)) * 2246822519u + (uint)(int)(position.Y * 5.9f);

                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0x100) != 0 ? 1 : -1;
            }
        }

        /// <summary>The look of a slow change (see <see cref="TrajectoryOp.Steps"/>) <paramref name="amount"/> (0..1) of the way, eased at both ends.</summary>
        public static BulletLook StepLook(TrajectoryOp op, double amount)
        {
            double x = Math.Clamp(amount, 0, 1);
            x = x * x * (3 - 2 * x);

            return op.Steps![(int)Math.Round(x * (op.Steps.Length - 1))];
        }

        /// <summary>The sideways swing of a falling petal after <paramref name="age"/> ms: a zigzag (a triangle wave) of the op's width.</summary>
        private static float zigzag(TrajectoryOp op, float age)
        {
            if (op.ZigzagWidth <= 0f || op.ZigzagPeriod <= 0)
                return 0f;

            return op.ZigzagWidth * (2f / MathF.PI) * MathF.Asin(MathF.Sin((float)(MathF.Tau * age / op.ZigzagPeriod)));
        }

        /// <summary>Flies on from <paramref name="now"/> to <paramref name="to"/> (nothing happens if that is not later).</summary>
        private static void step(ref Vector2 position, ref Vector2 v, Vector2 acc, ref double now, double to)
        {
            float dt = (float)(to - now);

            if (dt <= 0f)
                return;

            position += v * dt + acc * (0.5f * dt * dt);
            v += acc * dt;
            now = to;
        }

        /// <summary>How far a point is from Mima's moon (at least that far: 0 or less in its body), and whether the edge it is nearest is the hollow's.</summary>
        private static float crescentGap(TrajectoryOp op, Vector2 point, out bool inner)
        {
            float outside = (point - op.Point).Length - op.Radius;
            float hollow = op.InnerRadius - (point - op.InnerPoint).Length;
            inner = hollow > outside;
            return Math.Max(outside, hollow);
        }

        /// <summary>How long (ms, at most <paramref name="limit"/>) until a thing at <paramref name="position"/> that flies with <paramref name="v"/> and <paramref name="acc"/> touches the moon's body, and at which edge; infinity if it does not.</summary>
        private static double entersCrescent(TrajectoryOp op, Vector2 position, Vector2 v, Vector2 acc, double limit, out bool inner)
        {
            inner = false;

            // (a straight way that never comes within the outer circle cannot touch it: most of them)
            if (acc == Vector2.Zero)
            {
                Vector2 away = position - op.Point;
                float squared = v.LengthSquared;
                float nearest = squared > 1e-12f ? Math.Clamp(-Vector2.Dot(away, v) / squared, 0f, (float)limit) : 0f;

                if ((away + v * nearest).LengthSquared > op.Radius * op.Radius)
                    return double.PositiveInfinity;
            }

            // (it cannot get there sooner than at the most it can fly by then, so that long is skipped, again and again)
            double t = 0;
            float pull = acc.Length;

            for (int i = 0; i < 48 && t <= limit; i++)
            {
                float s = (float)t;
                float gap = crescentGap(op, position + v * s + acc * (0.5f * s * s), out inner);

                if (gap < 0.5f)
                    return t;

                t += gap / ((v + acc * s).Length + pull * (float)(limit - t) + 1e-3f);
            }

            return double.PositiveInfinity;
        }

        /// <summary>What slowed the thing down is gone: from that moment it has its speed back.</summary>
        private static void regain(ref Vector2 position, ref Vector2 v, Vector2 acc, ref double now, ref double at, float gain)
        {
            step(ref position, ref v, acc, ref now, at);
            v *= gain;
            at = double.PositiveInfinity;
        }

        /// <summary>How long (ms, at most <paramref name="limit"/>) until a thing at <paramref name="position"/> that flies with <paramref name="v"/> and <paramref name="acc"/> is inside the circle (0: it is already); infinity if it does not get there.</summary>
        private static double entersCircle(Vector2 position, Vector2 v, Vector2 acc, Vector2 centre, float radius, double limit)
        {
            if (limit < 0)
                return double.PositiveInfinity;

            Vector2 from = position - centre;
            float outside = from.LengthSquared - radius * radius;

            if (outside <= 0f)
                return 0;

            if (acc == Vector2.Zero)
            {
                float a = v.LengthSquared, b = Vector2.Dot(from, v), disc = b * b - a * outside;

                if (b >= 0f || disc < 0f || a < 1e-12f)
                    return double.PositiveInfinity;

                double straight = (-b - MathF.Sqrt(disc)) / a;

                return straight <= limit ? straight : double.PositiveInfinity;
            }

            // (a thing that falls: it cannot get there sooner than at the most it can fly by then, so that long is skipped, again and again)
            double t = 0;
            float pull = acc.Length;

            for (int i = 0; i < 24 && t <= limit; i++)
            {
                float s = (float)t;
                float gap = (from + v * s + acc * (0.5f * s * s)).Length - radius;

                if (gap < 1f)
                    return t;

                t += gap / ((v + acc * s).Length + pull * (float)(limit - t) + 1e-3f);
            }

            return double.PositiveInfinity;
        }

        /// <summary>
        /// How long (in (0, <paramref name="limit"/>] ms) from <paramref name="now"/> until a thing at <paramref name="position"/> that flies with <paramref name="v"/> and <paramref name="acc"/> meets
        /// one of the glass walls of <paramref name="op"/> (the walls close in over the first part of the op, and then stand); infinity if it does not.
        /// </summary>
        private static double glassContact(TrajectoryOp op, Vector2 position, Vector2 v, Vector2 acc, double now, double limit)
        {
            bool horizontal = op.Horizontal;
            float p = horizontal ? position.Y : position.X, pv = horizontal ? v.Y : v.X, pa = horizontal ? acc.Y : acc.X; // (across the walls)
            float o = horizontal ? position.X : position.Y, ov = horizontal ? v.X : v.Y, oa = horizontal ? acc.X : acc.Y; // (along them)
            float low = horizontal ? op.GlassMin.Y : op.GlassMin.X, high = horizontal ? op.GlassMax.Y : op.GlassMax.X;
            float alongLow = horizontal ? op.GlassMin.X : op.GlassMin.Y, alongHigh = horizontal ? op.GlassMax.X : op.GlassMax.Y;
            double closing = Math.Max((op.End - op.Time) * DanmakusuBalance.KYOUKO_CLOSE_SHARE, 1);
            float travel = (high - low) * DanmakusuBalance.KYOUKO_REACH; // (how far in the walls get)
            float slope = (float)(travel / closing); // (pixels per ms while they close in)
            float inset = (float)(travel * Math.Clamp((now - op.Time) / closing, 0.0, 1.0));
            double moving = Math.Clamp(op.Time + closing - now, 0.0, limit); // (how long they are still closing in)
            double best = double.PositiveInfinity;

            for (int wall = 0; wall < 2; wall++)
            {
                float edge = wall == 0 ? low + inset : high - inset; // (where the wall is now)
                float wallSpeed = wall == 0 ? slope : -slope;
                double found = double.PositiveInfinity;

                // (what is outside of a wall, between it and the edge of the screen, goes through it)
                if (wall == 0 ? p - edge < 0f : p - edge > 0f)
                    continue;

                // while the walls move
                if (moving > 0)
                    consider(p - edge, pv - wallSpeed, pa, moving, ref found);

                // and once they stand
                if (double.IsPositiveInfinity(found) && moving < limit)
                {
                    float m = (float)moving;
                    float standing = edge + wallSpeed * m;
                    float p2 = p + pv * m + 0.5f * pa * m * m, v2 = pv + pa * m;
                    double later = double.PositiveInfinity;

                    consider(p2 - standing, v2, pa, limit - moving, ref later);

                    if (!double.IsPositiveInfinity(later))
                        found = moving + later;
                }

                if (double.IsPositiveInfinity(found) || found >= best)
                    continue;

                float f = (float)found;
                float along = o + ov * f + 0.5f * oa * f * f;

                if (along >= alongLow && along <= alongHigh)
                    best = found;
            }

            return best;
        }

        /// <summary>The earliest moment (in (0, <paramref name="limit"/>] ms) at which p + v t + a t^2 / 2 leaves [lo, hi], starting inside; infinity if it does not.</summary>
        private static double leave(float p, float v, float acc, float lo, float hi, double limit)
        {
            double best = double.PositiveInfinity;
            consider(p - hi, v, acc, limit, ref best);
            consider(p - lo, v, acc, limit, ref best);
            return best;
        }

        private static void consider(double offset, double v, double acc, double limit, ref double best)
        {
            double qa = 0.5 * acc;

            if (Math.Abs(qa) < 1e-12)
            {
                if (Math.Abs(v) < 1e-12)
                    return;

                double t = -offset / v;

                if (t > 1e-9 && t <= limit && t < best)
                    best = t;

                return;
            }

            double disc = v * v - 4 * qa * offset;

            if (disc < 0)
                return;

            double root = Math.Sqrt(disc);
            double r1 = (-v - root) / (2 * qa);
            double r2 = (-v + root) / (2 * qa);

            if (r1 > 1e-9 && r1 <= limit && r1 < best)
                best = r1;

            if (r2 > 1e-9 && r2 <= limit && r2 < best)
                best = r2;
        }
    }
}
