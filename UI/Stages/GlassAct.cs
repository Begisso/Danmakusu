using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Kyouko: during an attack two transparent glass walls close in - from the top and the bottom in the first attack, from the left and the right in the next, and so on - until each is a quarter
        // of the playfield from its edge (within the first half of the attack), and then they stay where they are until the pause starts, when they are gone. They do nothing to the player.
        // Every bullet bounces off them twice (the way a light ray does off a mirror: the speed does not change, however fast a wall is closing in), and then passes through.
        // ======================================================================================================================================================

        private sealed class GlassAct : StageAct
        {
            private readonly BulletField field;

            /// <summary>The walls come from the top and the bottom (otherwise from the left and the right).</summary>
            public readonly bool Horizontal;

            public GlassAct(BulletField field, BossPeriod period, double start, double end, bool horizontal)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                Horizontal = horizontal;
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            // (asked by the field right after Pushes: being carried along by a wall is not a bounce off it)
            private bool lastPushWasCarry;

            public override bool CountsAsWallBounce => !lastPushWasCarry;

            private sealed class Release
            {
                public BulletEmitter Emitter = null!;
                public int Bullet;
                public double CarryTime; // (the bullet's own time of the turn that made it ride along: if that record is gone, the carry has been taken back)
                public double Game; // (when the walls stop: game time)
                public double Own; // (the same on the bullet's own clock)
                public Vector2 Position; // (where it is then)
                public Vector2 Velocity; // (the way it flew before, which it flies on)
            }

            private readonly Dictionary<(BulletEmitter, int), Release> carried = new Dictionary<(BulletEmitter, int), Release>();

            /// <summary>How fast the walls come in (pixels per ms) while they are closing.</summary>
            private float wallSpeedNow()
            {
                float extent = Horizontal ? field.stageMax.Y - field.stageMin.Y : field.stageMax.X - field.stageMin.X;
                return extent * DanmakusuBalance.KYOUKO_REACH / (float)Math.Max((End - Start) * DanmakusuBalance.KYOUKO_CLOSE_SHARE, 1);
            }

            /// <summary>The bullets that a wall has caught up with fly on at their own pace when the walls stop.</summary>
            public override void Update(BulletField f, double now)
            {
                if (carried.Count == 0)
                    return;

                List<(BulletEmitter, int)>? done = null;

                foreach (var pair in carried)
                {
                    var release = pair.Value;

                    if (now < release.Game)
                        continue;

                    // (time went back over the moment the wall caught it and the field took that turn back: there is nothing to let go of)
                    if (release.Emitter.Deflections == null || !release.Emitter.Deflections.TryGetValue(release.Bullet, out var carriedRecord) || Math.Abs(carriedRecord.Time - release.CarryTime) > 1e-6)
                    {
                        (done ??= new List<(BulletEmitter, int)>()).Add(pair.Key);
                        continue;
                    }

                    float speed = release.Velocity.Length;
                    Vector2 direction = speed > 1e-9f ? release.Velocity / speed : Vector2.UnitX;
                    int ricochetsLeft = release.Emitter.Ricochet ? release.Emitter.RicochetsLeft(release.Bullet, release.Own) : 0;
                    float diagonal = f.hasScreenBounds ? Vector2.Distance(f.screenMin, f.screenMax) : 1500f;
                    double longer = (release.Own - release.Emitter.Time) + (diagonal * (ricochetsLeft + 1) + release.Emitter.VisualRadius * 4f) / Math.Max(speed, 1e-4f);

                    var before = release.Emitter.DeflectionOf(release.Bullet);
                    release.Emitter.Deflect(release.Bullet, release.Own, release.Position, direction, longer, speed, ricochetsLeft);
                    f.deflectLog.Add((release.Emitter, release.Bullet, release.Game, before));
                    (done ??= new List<(BulletEmitter, int)>()).Add(pair.Key);
                }

                if (done != null)
                {
                    foreach (var key in done)
                        carried.Remove(key);
                }
            }

            public override void Reset(BulletField f, double time)
            {
                // (the field makes every ring scan again from its start: the bullets are caught again)
                carried.Clear();
            }

            /// <summary>How far (pixels) the walls have come in from the edges of the playfield at <paramref name="t"/>.</summary>
            private float inset(double t)
            {
                float extent = Horizontal ? field.stageMax.Y - field.stageMin.Y : field.stageMax.X - field.stageMin.X;
                double closing = Math.Max((End - Start) * DanmakusuBalance.KYOUKO_CLOSE_SHARE, 1);

                return extent * DanmakusuBalance.KYOUKO_REACH * (float)Math.Clamp((t - Start) / closing, 0.0, 1.0);
            }

            /// <summary>Where a wall is: its y (walls from the top and the bottom) or its x (from the left and the right); wall 0 comes from the top / left, wall 1 from the bottom / right.</summary>
            private float wallAt(int wall, double t)
            {
                float come = inset(t);

                if (Horizontal)
                    return wall == 0 ? field.stageMin.Y + come : field.stageMax.Y - come;

                return wall == 0 ? field.stageMin.X + come : field.stageMax.X - come;
            }

            /// <summary>
            /// Does a bullet whose middle flew from <paramref name="start"/> (at game time <paramref name="t0"/>) to <paramref name="end"/> (at <paramref name="t1"/>) touch a wall (its middle comes
            /// <paramref name="reach"/> close)? Then <paramref name="fraction"/> is how far along the way it does. The walls move meanwhile, but that only decides where they are: it is never added to a bullet.
            /// </summary>
            public bool Touch(double t0, double t1, Vector2 start, Vector2 end, float reach, out float fraction, out bool acrossX)
                => touch(t0, t1, start, end, reach, out fraction, out acrossX, out _);

            /// <summary>As <see cref="Touch"/>; <paramref name="side"/> is +1 when the bullet comes from the higher coordinate (it is on the far side of the wall that comes from the top / left), otherwise -1.</summary>
            private bool touch(double t0, double t1, Vector2 start, Vector2 end, float reach, out float fraction, out bool acrossX, out float side)
            {
                fraction = 0f;
                acrossX = !Horizontal;
                side = 1f;

                if (t1 < Start || t1 > End)
                    return false;

                float a0 = Horizontal ? start.Y : start.X, a1 = Horizontal ? end.Y : end.X; // (across the walls)
                float o0 = Horizontal ? start.X : start.Y, o1 = Horizontal ? end.X : end.Y; // (along them)
                float lowest = Horizontal ? field.stageMin.X : field.stageMin.Y, highest = Horizontal ? field.stageMax.X : field.stageMax.Y;

                for (int wall = 0; wall < 2; wall++)
                {
                    float d0 = a0 - wallAt(wall, t0);
                    float d1 = a1 - wallAt(wall, t1);

                    if (Math.Abs(d0) < reach - 0.05f)
                        continue; // (it is already in the glass, e.g. it was fired there: it goes through)

                    float wallSide = d0 > 0f ? 1f : -1f; // (the side of the wall that it comes from)

                    // the walls are glass for what is outside of them (between them and the edge of the screen): only what is inside bounces (the wall from the top / left has the field on its far side)
                    if (wall == 0 ? wallSide < 0f : wallSide > 0f)
                        continue;

                    if (wallSide * d1 > reach)
                        continue; // (it does not touch the wall in this step)

                    float u = Math.Abs(d1 - d0) > 1e-6f ? Math.Clamp((wallSide * reach - d0) / (d1 - d0), 0f, 1f) : 1f;
                    float along = o0 + (o1 - o0) * u;

                    if (along < lowest || along > highest)
                        continue;

                    fraction = u;
                    side = wallSide;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// A bullet that flew from <paramref name="from"/> to <paramref name="to"/> in the last step bounces off a wall it touched: the way across the wall is turned round, the way along it
            /// and the speed stay (the walls' own speed is never added). Every bullet bounces, also the ones that fall (the fall goes on from the wall). Not checked here: the arrowheads (simulated step by step,
            /// see <see cref="IGlassWalls"/>), and the rings that other ops steer and the beams (those have the walls as an op of their own, see <see cref="TrajectoryOpKind.Glass"/>).
            /// </summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                lastPushWasCarry = false;

                if (t < Start || t > End || emitter.Homing || emitter.Ops != null || emitter.Deflector)
                    return false;

                if (carried.Count > 0 && carried.TryGetValue((emitter, bullet), out var holding))
                {
                    // still carried (its record is the one that the wall made); if time went back and that record was taken back, the wall catches it again
                    if (emitter.Deflections != null && emitter.Deflections.TryGetValue(bullet, out var current) && Math.Abs(current.Time - holding.CarryTime) <= 1e-6)
                        return false;

                    carried.Remove((emitter, bullet));
                }

                // (the bullet's own clock: it can be frozen)
                double ea = emitter.WithoutFrozenTime(f.effects.Effective(emitter, ScanPrevious));
                double eb = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));
                double dt = eb - ea;

                if (dt <= 1e-6 || !touch(ScanPrevious, t, from, to, emitter.VisualRadius * DanmakusuBalance.KYOUKO_CONTACT, out float u, out bool acrossX, out float side))
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-12f)
                    return false;

                // a wall that comes in faster than a bullet flies away from it catches it up from behind: the bullet is carried along by the wall (it keeps its way along the wall) until the walls stop,
                // and then flies on as it did (a falling bullet bounces instead)
                double stopsAt = Start + Math.Max((End - Start) * DanmakusuBalance.KYOUKO_CLOSE_SHARE, 1);

                if (!emitter.Falling && side * (acrossX ? delta.X : delta.Y) >= 0f && t < stopsAt)
                {
                    Vector2 flying = delta / (float)dt;
                    float wallSpeed = wallSpeedNow();
                    Vector2 carriedVelocity = acrossX ? new Vector2(side * wallSpeed, flying.Y) : new Vector2(flying.X, side * wallSpeed);
                    Vector2 touching = from + delta * u;
                    float after = (float)((1f - u) * dt);
                    double stopOwn = emitter.WithoutFrozenTime(f.effects.Effective(emitter, stopsAt));
                    double touchOwn = ea + u * dt;

                    carried[(emitter, bullet)] = new Release
                    {
                        Emitter = emitter,
                        Bullet = bullet,
                        CarryTime = eb,
                        Game = stopsAt,
                        Own = stopOwn,
                        Position = touching + carriedVelocity * (float)Math.Max(stopOwn - touchOwn, 0.0),
                        Velocity = flying,
                    };

                    lastPushWasCarry = true;
                    speed = carriedVelocity.Length;
                    direction = carriedVelocity / speed;
                    position = touching + carriedVelocity * after;
                    return true;
                }

                if (emitter.WallBouncesOf(bullet, Id) >= DanmakusuBalance.KYOUKO_BOUNCES)
                    return false;

                Vector2 contact = from + delta * u;
                float rest = (float)((1f - u) * dt); // (the rest of the step, flown after the wall)

                if (emitter.Falling)
                {
                    // the velocity at the wall (the fall included) is turned across the wall, and the fall goes on from there
                    Vector2 velocity = emitter.VelocityAt(bullet, ea + u * dt, Vector2.Zero);
                    Vector2 turnedVelocity = acrossX ? new Vector2(-velocity.X, velocity.Y) : new Vector2(velocity.X, -velocity.Y);
                    Vector2 pull = emitter.GravityDirection * emitter.Gravity;
                    Vector2 endVelocity = turnedVelocity + pull * rest;

                    position = contact + turnedVelocity * rest + pull * (0.5f * rest * rest);
                    speed = endVelocity.Length;
                    direction = speed > 1e-9f ? endVelocity / speed : Vector2.Normalize(turnedVelocity);
                    return true;
                }

                Vector2 turned = acrossX ? new Vector2(-delta.X, delta.Y) : new Vector2(delta.X, -delta.Y);
                float length = delta.Length;

                direction = turned / length;
                speed = length / (float)dt;
                position = contact + direction * (speed * rest); // (where it is at the end of the step: it has been flying on since it touched the wall)
                return true;
            }

            public override void Draw(BulletField f, double t)
            {
                if (t < Start || t > End + DanmakusuBalance.KYOUKO_FADE_OUT_MS)
                    return;

                float alpha = DanmakusuBalance.KYOUKO_GLASS_ALPHA * smoothStep((t - Start) / DanmakusuBalance.KYOUKO_FADE_IN_MS) * (1f - smoothStep((t - End) / DanmakusuBalance.KYOUKO_FADE_OUT_MS));
                float thickness = DanmakusuBalance.KYOUKO_WALL_THICKNESS;
                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                var colour = new Color4(0.8f, 0.96f, 1f, 1f);

                for (int wall = 0; wall < 2; wall++)
                {
                    float at = wallAt(wall, t);
                    float length = Horizontal ? f.stageMax.X - f.stageMin.X : f.stageMax.Y - f.stageMin.Y;
                    Vector2 centre = Horizontal ? new Vector2(middle.X, at) : new Vector2(at, middle.Y);
                    float degrees = Horizontal ? 0f : 90f;

                    // (the glass, the same added over it so that it is brighter, and a soft band of light round it)
                    f.stageBack.Add(StageTexture.Pane, centre, new Vector2(length, thickness), degrees, alpha, colour);
                    f.stageBack.Add(StageTexture.Pane, centre, new Vector2(length, thickness), degrees, alpha * 0.8f, colour, true);
                    f.stageBack.Add(StageTexture.Glow, centre, new Vector2(length, thickness * DanmakusuBalance.KYOUKO_GLOW_WIDTH), degrees, alpha * DanmakusuBalance.KYOUKO_GLOW_ALPHA, colour, true);
                }
            }
        }

        /// <summary>All of Kyouko's glass walls, as the arrowheads see them.</summary>
        private sealed class GlassWalls : IGlassWalls
        {
            private readonly List<GlassAct> acts = new List<GlassAct>();

            public void Add(GlassAct act) => acts.Add(act);

            public bool Hit(double from, double to, Vector2 start, Vector2 end, float reach, out float fraction, out bool acrossX, out int set)
            {
                foreach (var act in acts)
                {
                    if (act.Touch(from, to, start, end, reach, out fraction, out acrossX))
                    {
                        set = act.Id;
                        return true;
                    }
                }

                fraction = 0f;
                acrossX = false;
                set = 0;
                return false;
            }
        }
    }
}
