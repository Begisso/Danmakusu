using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // The bullets of a boss that are not rings (the stars of Clownpiece's lines of stars, the straight bullets that the acts of a stage make) are bullets like any other for the OTHER bosses: what the mechanic of another
        // boss does to every bullet (freezes it, bounces it off its glass walls or the border, mirrors it, pulls it in, slows it down) happens to them too. (What the boss does with his own bullets is the same as before.)
        // ======================================================================================================================================================

        /// <summary>Is there anything that the bosses do to the flight of bullets (ops, slowdowns) while the screen is known?</summary>
        private bool otherOpsOn => hasScreenBounds && (bulletOps.Length > 0 || effects.Any || portalActs.Count > 0);

        /// <summary>Is there a pull among the ops (Utsuho's black hole), which makes what it turns back live longer?</summary>
        private bool pullOpsOn => pullOps ??= Array.Exists(bulletOps, op => op.Kind == TrajectoryOpKind.Redirect);

        private bool? pullOps;

        /// <summary>How long the freezes that begin between <paramref name="from"/> and <paramref name="to"/> last in all (a frozen bullet lives that much longer).</summary>
        private double freezeSpan(double from, double to)
        {
            double total = 0;

            foreach (var op in bulletOps)
            {
                if (op.Kind == TrajectoryOpKind.Freeze && op.Time >= from && op.Time <= to)
                    total += op.End - op.Time;
            }

            return total;
        }

        /// <summary>
        /// Where a bullet is at <paramref name="now"/> that was made at <paramref name="t0"/> (at <paramref name="start"/>, flying with <paramref name="velocity"/> and a constant <paramref name="pull"/>) and goes
        /// until <paramref name="until"/> if nothing happens to it, when the ops of the bosses act on it like they do on a ring's bullets. False when it is gone (it has lived its time, or something took it away).
        /// </summary>
        private bool flyWithOps(Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look)
        {
            position = start;
            speed = velocity;
            look = null;

            // (a frozen bullet lives as long as it is frozen longer; one that a pull turns back - Utsuho's black hole - flies a longer way, and more slowly: it lives as long again twice over,
            // and is gone where the pull takes it or when it leaves the screen. Without that the moon's stars that the hole pulled back vanished in the middle of the screen)
            double life = until;
            bool pulled = false;

            foreach (var op in bulletOps)
            {
                if ((op.Kind != TrajectoryOpKind.Freeze && op.Kind != TrajectoryOpKind.Redirect) || op.Time >= life)
                    continue;

                if ((op.Time <= t0 && !op.Window) || t0 >= op.BornBefore || (op.Window && t0 > op.End))
                    continue;

                if (op.Kind == TrajectoryOpKind.Redirect)
                    pulled = true;
                else
                    life += op.End - op.Time;
            }

            if (pulled)
                life += (until - t0) * 2 + DanmakusuBalance.PULLED_EXTRA_LIFE_MS;

            double clock = effects.Effective(t0, now); // (on the bullets' own clock: slower while a boss slows everything down)

            if (clock > life)
                return false;

            Vector2 limitMin = bounceArea.Min + new Vector2(radius);
            Vector2 limitMax = bounceArea.Max - new Vector2(radius);

            Trajectory.Trace(start, velocity, pull, t0, Math.Max(clock, t0), bulletOps, limitMin, limitMax, bounceArea.Min.Y, out TrajectoryState state);

            if (!state.Alive)
                return false;

            position = state.Position;
            speed = state.Velocity;
            look = state.Look;
            return true;
        }

        private readonly List<PortalAct> portalActs = new List<PortalAct>();
        private PortalBridge? portalBridge;

        /// <summary>Carries the slices of the beams through Okina's portals (see <see cref="IBeamPortals"/>).</summary>
        private sealed class PortalBridge : IBeamPortals
        {
            /// <summary>Slices looked at between two kept points are new ones while a slider is being fired: this many are remembered, then it starts over.</summary>
            private const int most_marks = 20000;

            private readonly BulletField field;

            /// <summary>How far each slice of a beam that the ops steer has been followed: every frame asks for all of them, twice, and only has to go on from there.</summary>
            private readonly Dictionary<(LaserEmitter Laser, double Fired, Vector2 Start), CarryMark> marks = new Dictionary<(LaserEmitter, double, Vector2), CarryMark>();

            public PortalBridge(BulletField field) => this.field = field;

            public void Forget() => marks.Clear();

            public void Carry(LaserEmitter laser, Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out BulletLook? look, out int through)
            {
                float radius = Math.Max(laser.HalfWidth, 1f);

                if (laser.Ops == null || laser.OpsArea == null)
                {
                    field.carry(laser, start, velocity, Vector2.Zero, fired, fired + 60000, time, radius, out position, out _, out look, out through);
                    return;
                }

                var key = (laser, fired, start);
                marks.TryGetValue(key, out CarryMark mark);
                field.carry(laser, start, velocity, Vector2.Zero, fired, fired + 60000, time, radius, out position, out _, out look, out through, ref mark);

                if (marks.Count >= most_marks)
                    marks.Clear();

                marks[key] = mark;
            }
        }

        /// <summary>
        /// Like <see cref="flyWithOps"/>, and Okina's portals take what flies into the first one and let it out of the second. While a portal is open the flight is followed in small steps (the portals move); before and after it is closed form.
        /// </summary>
        private bool flyTraced(Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look)
        {
            CarryMark none = default;
            return flyTraced(start, velocity, pull, t0, until, now, radius, out position, out speed, out look, ref none);
        }

        private bool flyTraced(Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look, ref CarryMark mark)
        {
            if (!carry(null, start, velocity, pull, t0, until, now, radius, out position, out speed, out look, out int through, ref mark))
                return false;

            look = BulletLook.Portaled(look, through);
            return true;
        }

        private static int portalOf(BulletLook? look) => look?.Portal ?? 0;

        /// <summary>
        /// One of two neighbouring slices of a beam has left the top of the screen and fallen (Yuyuko's) and the other has not, or they belong to two different pieces of what has fallen: they are far apart by now,
        /// and the beam is not joined between them (no line across the screen).
        /// </summary>
        private static bool fallenApart(BulletLook? a, BulletLook? b)
            => BossOps.IsFallenBeam(a) != BossOps.IsFallenBeam(b) || (BossOps.IsFallenBeam(a) && !ReferenceEquals(a, b)) || (isFalling(a) && b is not { Level: >= 0 }) || (isFalling(b) && a is not { Level: >= 0 });

        /// <summary>Two neighbouring points of a beam that are both fallen pieces, further apart than a piece can be: they fell at very different moments and are not joined.</summary>
        private static bool fallenTooFar(BulletLook? a, BulletLook? b, Vector2 pa, Vector2 pb, float width)
            => BossOps.IsFallenBeam(a) && BossOps.IsFallenBeam(b) && Vector2.DistanceSquared(pa, pb) > MathF.Pow(DanmakusuBalance.KAGUYA_BEAM_PIECE_REACH + width * 2f, 2);

        /// <summary>Is this the look of a slice that Zanmu has stopped and that is now falling (it has left the beam: what is still flying is not joined to it)?</summary>
        private static bool isFalling(BulletLook? look) => look is { Level: >= 0 } && (look.Level > DanmakusuBalance.ZANMU_FADE_STEPS || look.Dark > 0f);

        private bool tracePiece(LaserEmitter? laser, Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double time, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look)
        {
            if (laser != null)
            {
                laser.TraceSlice(start, velocity, t0, time, out position, out look);
                speed = velocity;
                return true;
            }

            return flyWithOps(start, velocity, pull, t0, until, time, radius, out position, out speed, out look);
        }

        /// <summary>The longest step of <see cref="carry"/> (ms): what steers a thing can change its speed, and this is as far as it is taken on trust.</summary>
        private const double max_wall_step_ms = 1000;

        /// <summary>How far <paramref name="at"/> is from the nearest of the lines where the portals take something of this radius (a radius outside each wall), on either side of it.</summary>
        private float wallGap(Vector2 at, float radius)
        {
            float x = Math.Min(Math.Abs(at.X - (stageMin.X - radius)), Math.Abs(at.X - (stageMax.X + radius)));
            float y = Math.Min(Math.Abs(at.Y - (stageMin.Y - radius)), Math.Abs(at.Y - (stageMax.Y + radius)));

            return Math.Min(x, y);
        }

        /// <summary>How far <see cref="carry"/> has followed a stage bullet (0 time = not at all): the next frame goes on from there instead of going over its whole flight again.</summary>
        private struct CarryMark
        {
            public double Time;
            public int Through;
            public Vector2 PieceStart, PieceVelocity;
            public double PieceT0, PieceUntil;
        }

        private bool carry(LaserEmitter? laser, Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look, out int through)
        {
            CarryMark none = default;
            return carry(laser, start, velocity, pull, t0, until, now, radius, out position, out speed, out look, out through, ref none);
        }

        private bool carry(LaserEmitter? laser, Vector2 start, Vector2 velocity, Vector2 pull, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look, out int through, ref CarryMark mark)
        {
            through = 0;

            double from = double.PositiveInfinity, to = double.NegativeInfinity;

            foreach (var act in portalActs)
            {
                if (!act.OpenBetween(t0, now))
                    continue;

                from = Math.Min(from, act.Start);
                to = Math.Max(to, act.End);
            }

            if (from > to)
                return tracePiece(laser, start, velocity, pull, t0, until, now, radius, out position, out speed, out look);

            // (nothing else steers it: it flies straight, so the one moment it can go into a portal is when it reaches the wall)
            if (laser != null ? laser.Ops == null || laser.OpsArea == null : pull == Vector2.Zero && bulletOps.Length == 0 && !effects.Any)
                return carryStraight(laser, start, velocity, t0, until, now, radius, out position, out speed, out look, out through);

            const double step = 24;
            Vector2 pieceStart = start, pieceVelocity = velocity;
            double pieceT0 = t0, pieceUntil = until;
            double t = Math.Min(Math.Max(t0, from - step), now);
            double last = Math.Min(now, to + step);

            // (what has been followed already is not gone over again)
            if (mark.Time > t && mark.Time <= now)
            {
                t = mark.Time;
                through = mark.Through;

                if (through > 0)
                {
                    pieceStart = mark.PieceStart;
                    pieceVelocity = mark.PieceVelocity;
                    pieceT0 = mark.PieceT0;
                    pieceUntil = mark.PieceUntil;
                }
            }

            // (the way to the first moment a portal can take it is closed form)
            if (!tracePiece(laser, pieceStart, pieceVelocity, pull, pieceT0, pieceUntil, t, radius, out Vector2 before, out speed, out look))
            {
                position = before;
                return false;
            }

            position = before;
            int guard = 0;
            float pace = velocity.Length;
            bool fallen = laser != null && BossOps.IsFallenBeam(look); // (a slice of a beam that has fallen from the top, Kaguya's, goes past the portals)

            // (a step never goes all the way to the nearest wall, so it can be long while the thing is far from one: every step is worked out from where it started, and a beam asks this for every slice, many times a frame)
            while (through < 1 && !fallen && t < last && guard++ < 2000)
            {
                double next = Math.Min(t + Math.Max(step, Math.Min(wallGap(before, radius) * 0.5 / Math.Max(pace, 1e-3f), max_wall_step_ms)), last);

                if (!tracePiece(laser, pieceStart, pieceVelocity, pull, pieceT0, pieceUntil, next, radius, out Vector2 after, out Vector2 flying, out BulletLook? lookAfter))
                    return false;

                bool sent = false;

                foreach (var act in portalActs)
                {
                    if (!act.Transfer(this, next, before, after, radius, out Vector2 exitPoint, out Vector2 exitDirection, out float fraction, out int teleport, laser))
                        continue;

                    double crossed = t + (next - t) * fraction;
                    float length = laser != null ? laser.Speed : Math.Max(flying.Length, 1e-6f);

                    pieceStart = exitPoint;
                    pieceVelocity = exitDirection * length;
                    pieceT0 = crossed;
                    pieceUntil = Math.Max(pieceUntil, crossed + 60000);
                    through = teleport;
                    sent = true;
                    break;
                }

                // (whatever has gone through a portal does not go into one again: nothing more can happen to it here)
                if (sent)
                    break;

                fallen = laser != null && BossOps.IsFallenBeam(lookAfter);
                pace = Math.Max(velocity.Length, Vector2.Distance(before, after) / (float)Math.Max(next - t, 1e-3));
                before = after;
                t = next;
            }

            mark = new CarryMark { Time = t, Through = through, PieceStart = pieceStart, PieceVelocity = pieceVelocity, PieceT0 = pieceT0, PieceUntil = pieceUntil };

            // ...and after the last moment a portal can take it, closed form again
            if (!tracePiece(laser, pieceStart, pieceVelocity, pull, pieceT0, pieceUntil, now, radius, out position, out speed, out look))
                return false;

            // (the slices that have come out of the second portal fly on their own way from where they came out: they do not move along with the portal when it slides)

            return true;
        }

        /// <summary>
        /// <see cref="carry"/> for something that flies in a straight line (no ops steer it): it can only go into a portal when its centre is a radius outside the wall, so the portals are asked once,
        /// about that moment, instead of following the flight step by step for as long as a portal is open (that cost a lot for every slice of every beam, every frame).
        /// </summary>
        private bool carryStraight(LaserEmitter? laser, Vector2 start, Vector2 velocity, double t0, double until, double now, float radius, out Vector2 position, out Vector2 speed, out BulletLook? look, out int through)
        {
            through = 0;
            look = null;
            speed = velocity;
            position = start + velocity * (float)(now - t0);

            if (laser == null && now > until)
                return false;

            double reaches = wallTime(start, velocity, t0, radius);

            if (reaches > now)
                return true;

            const double probe = 0.5; // (ms either side of the moment it reaches the wall)
            Vector2 before = start + velocity * (float)(reaches - probe - t0);
            Vector2 after = start + velocity * (float)(reaches + probe - t0);

            foreach (var act in portalActs)
            {
                if (!act.Transfer(this, reaches, before, after, radius, out Vector2 exitPoint, out Vector2 exitDirection, out float fraction, out int teleport, laser))
                    continue;

                double crossed = reaches - probe + 2 * probe * fraction;
                float length = laser != null ? laser.Speed : Math.Max(velocity.Length, 1e-6f);

                through = teleport;
                speed = exitDirection * length;
                position = exitPoint + speed * (float)(now - crossed); // (a slice that came out flies its own way: it does not move with the portal)

                if (laser == null && now > Math.Max(until, crossed + 60000))
                    return false;

                return true;
            }

            return true;
        }

        /// <summary>When something flying straight from <paramref name="start"/> (at <paramref name="t0"/>) gets its centre a radius outside a wall of the playfield; infinity if it never does (or starts outside).</summary>
        private double wallTime(Vector2 start, Vector2 velocity, double t0, float radius)
        {
            Vector2 low = stageMin - new Vector2(radius), high = stageMax + new Vector2(radius);

            if (start.X < low.X || start.X > high.X || start.Y < low.Y || start.Y > high.Y)
                return double.PositiveInfinity;

            double tx = velocity.X > 1e-9f ? (high.X - start.X) / velocity.X : velocity.X < -1e-9f ? (low.X - start.X) / velocity.X : double.PositiveInfinity;
            double ty = velocity.Y > 1e-9f ? (high.Y - start.Y) / velocity.Y : velocity.Y < -1e-9f ? (low.Y - start.Y) / velocity.Y : double.PositiveInfinity;

            return t0 + Math.Min(tx, ty);
        }

        /// <summary>Does the ops layer take care of this stage bullet? Only the ones that fly straight (or fall with a constant pull): the ones that an act moves itself keep doing what the act says.</summary>
        private bool stageBulletTraced(in StageBullet b)
            => otherOpsOn && !b.Untraced && b.Trail == null && b.A <= 0f && b.FallAt <= 0 && b.StopAfter <= 0 && b.WrapHeight <= 0f && !b.Slider && (!b.Hidden || b.Ops) && !b.Harmless && b.Switch <= 0 && b.TurnT <= 0 && b.Pushes == null && b.Wander <= 0f;

        /// <summary>Where a stage bullet is at <paramref name="t"/>: where the ops of the other bosses (walls that bounce, freezing, portals) leave it, or on its own straight line.</summary>
        private bool stageBulletAt(in StageBullet b, double t, out Vector2 position)
        {
            if (stageBulletTraced(b))
                return flyTraced(b.P0, b.V, b.Acc, b.T0, b.Until, t, b.Radius, out position, out _, out _);

            position = stageBulletPosition(b, stageClock(b, t));
            return true;
        }

        /// <summary>
        /// The moment a stage bullet that nothing traces has got to at <paramref name="t"/>: with Gengetsu or Saki on the field it flies on the bullets' own clock too, as their speed-up is on every bullet on the screen.
        /// (Not what has been laid down step by step, or orbits something: a star at the tip of a beam stays there.)
        /// </summary>
        private double stageClock(in StageBullet b, double t) => stageOnClock && b.Trail == null && b.A <= 0f ? effects.Effective(b.T0, t) : t;

        private bool stageOnClock;

        /// <summary>How long after its last bullet is gone (without ops) an act can still have bullets that are alive: they may be frozen or slowed down.</summary>
        private double stageSlack => otherOpsOn ? 60000 + freezeSpan(double.NegativeInfinity, double.PositiveInfinity) : 0;
    }
}
