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
        // Nue: when an attack begins she sends all her UFOs at once (eight) to their places, spread evenly round the walls and aiming at the middle (see pullPlace), where they slide round the
        // walls and suck in every second bullet (not the beams, nor what the UFOs shot out) that flies through a narrow cone (where cones cross, the bullets are shared out evenly between them); the cones are on the UFOs
        // from when they are sent, dimmer while they only warn. The pulled bullets fly straight in at a speed that does not change, keep their size and still hurt on their way in; a bullet that could not get in before the pulling
        // ends is not pulled. Everything that got in is shot out again. When the attack ends the UFOs and Nue go to the middle, the UFOs spread evenly round a circle that turns at the same rate, and every one shoots out its share of what all the
        // UFOs took in (it is shared out evenly between them), turning 60 degrees to its even angle (straight out) over the first half of the shooting and shooting straight out for the rest. The turning goes the same way for both phases of an attack and the other way in the next attack (also when that is in the next section).
        // While the UFOs pull and shoot the playfield shakes and the place round them is darkened; the shot bullets glow. Bullets are pulled into, and shot out of, points spread evenly over the whole
        // start of the cone (the UFO's width). Each bullet flies straight on in the direction it was shot in. Going back in time does not reset any of the UFOs' phases.
        // ======================================================================================================================================================

        private sealed class UfoAct : StageAct
        {
            public readonly int Count;
            public readonly double Beat;
            public readonly double Flight;
            public readonly double ShootStart;
            public readonly double ShootEnd;

            private readonly float omega; // (radians per ms that the UFOs turn round the middle: a share of a spinner's beams, see NUE_TURN_SHARE)
            private readonly Vector2[] launchedFrom;
            private readonly bool[] known;
            private readonly List<StoredBullet>[] stored;
            private readonly HashSet<(BulletEmitter, int)> takenAlready = new HashSet<(BulletEmitter, int)>();
            private bool built;

            // the way the UFOs turn (1 = clockwise on the screen): the same for both phases of an attack (pulling and shooting), the other way in the next attack
            private readonly float turn;

            public UfoAct(BulletField f, BossPeriod period, double start, double end, double next, double nextAny, int attack)
            {
                turn = attack % 2 == 0 ? 1f : -1f;
                Period = period;
                Start = start;
                End = end;
                Beat = Math.Max(period.BeatLength, 1);
                Count = DanmakusuBalance.NUE_UFOS;
                Flight = Math.Clamp(Beat, 150, 1200);
                omega = Math.Max(period.AngularSpeed, 0f) * DanmakusuBalance.NUE_TURN_SHARE;

                double gap = double.IsNaN(next) ? double.PositiveInfinity : next - end;
                double gather = Math.Max(25, Math.Min(DanmakusuBalance.NUE_GATHER_BEATS * Beat, 0.175 * gap));
                ShootStart = end + gather;
                double shootEnd = double.IsNaN(next) ? Math.Max(ShootStart + Beat, end + DanmakusuBalance.NUE_SHOOT_BEATS * Beat) : Math.Max(ShootStart + Beat, next - 0.25 * Beat);
                ShootEnd = ShootStart + (shootEnd - ShootStart) / DanmakusuBalance.NUE_SHOOT_RATE; // (the shooting is over this many times sooner than the time it has)

                // (but it is over before her next attack sends out new UFOs, also when that attack is in the next section)
                if (!double.IsNaN(nextAny))
                    ShootEnd = Math.Max(ShootStart + 1, Math.Min(ShootEnd, nextAny - 0.25 * Beat));

                launchedFrom = new Vector2[Count];
                known = new bool[Count];
                stored = new List<StoredBullet>[Count];

                for (int i = 0; i < Count; i++)
                    stored[i] = new List<StoredBullet>();

                AbsorbFrom = start + Flight;
                AbsorbTo = end;
            }

            private double landed => Start + Flight; // (all the UFOs are sent at once, when the attack begins)

            /// <summary>0..1: the playfield shakes while the UFOs pull and while they shoot.</summary>
            public float ShakeAt(double now)
                => (now >= landed && now <= End) || (now >= ShootStart && now <= ShootEnd) ? DanmakusuBalance.NUE_SHAKE : 0f;

            private static float width(BulletField f) => DanmakusuBalance.NUE_UFO_WIDTH * f.circleRadius;

            private static float halfAngle => DanmakusuBalance.NUE_CONE_HALF_ANGLE_DEGREES * MathF.PI / 180f;

            private float gatherRadius(BulletField f)
            {
                Vector2 size = f.stageMax - f.stageMin;
                return Math.Clamp(Count * width(f) * 0.55f / MathF.Tau, 2.5f * f.circleRadius, 0.38f * Math.Min(size.X, size.Y));
            }

            // ---- the walls ----

            /// <summary>The rectangle the UFOs slide round: the playfield, less half a UFO on every side.</summary>
            private static void walls(BulletField f, out Vector2 low, out Vector2 high)
            {
                float half = width(f) * 0.5f;
                low = f.stageMin + new Vector2(half);
                high = Vector2.ComponentMax(low, f.stageMax - new Vector2(half));
            }

            // While they pull, the UFOs are on the walls, spread evenly round them (when they land, the first one in the middle of the top wall, the rest clockwise), each aiming at the middle.
            // They slide round the walls the way the circle in the middle turns when they shoot (one lap round the walls in the time the circle takes for one turn).

            /// <summary>Where UFO <paramref name="k"/> is on the walls at <paramref name="t"/> while it pulls (before it lands: where it lands; after the pulling: where it stopped).</summary>
            private Vector2 pullPlace(BulletField f, int k, double t)
            {
                walls(f, out var low, out var high);

                Vector2 size = high - low;
                float perimeter = 2f * (size.X + size.Y);

                if (perimeter < 1e-3f)
                    return low;

                float slid = turn * omega * (float)(Math.Clamp(t, landed, End) - landed) * perimeter / MathF.Tau;
                float s = size.X * 0.5f + perimeter * k / Count + slid;
                s = ((s % perimeter) + perimeter) % perimeter;

                if (s < size.X)
                    return new Vector2(low.X + s, low.Y);

                s -= size.X;

                if (s < size.Y)
                    return new Vector2(high.X, low.Y + s);

                s -= size.Y;

                if (s < size.X)
                    return new Vector2(high.X - s, high.Y);

                return new Vector2(low.X, high.Y - (s - size.X));
            }

            /// <summary>The way UFO <paramref name="k"/> points its cone while it pulls: at the middle.</summary>
            private float pullAim(BulletField f, int k, double t) => aimAtMiddle(f, pullPlace(f, k, t));

            private static float aimAtMiddle(BulletField f, Vector2 from)
            {
                walls(f, out var low, out var high);
                Vector2 d = (low + high) * 0.5f - from;
                return d.LengthSquared > 1e-6f ? MathF.Atan2(d.Y, d.X) : MathF.PI / 2f;
            }

            // ---- the circle in the middle ----

            /// <summary>The angle of UFO <paramref name="k"/> round the circle in the middle at <paramref name="t"/>: the circle turns from when the shooting starts.</summary>
            private float slotAngle(int k, double t)
                => -MathF.PI / 2f + MathF.Tau * k / Count + turn * omega * (float)Math.Max(t - ShootStart, 0);

            /// <summary>Where UFO <paramref name="k"/> stands on the circle in the middle at <paramref name="t"/>.</summary>
            private Vector2 slotAt(BulletField f, int k, double t)
            {
                float angle = slotAngle(k, t);
                return f.fieldCentre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * gatherRadius(f);
            }

            public Vector2 PositionOf(BulletField f, int k, double t)
            {
                if (t < landed)
                {
                    float rest = 1f - (float)Math.Max((t - Start) / Flight, 0);
                    return Vector2.Lerp(launchedFrom[k], pullPlace(f, k, t), 1f - rest * rest * rest);
                }

                if (t <= End)
                    return pullPlace(f, k, t);

                if (t < ShootStart)
                    return Vector2.Lerp(pullPlace(f, k, End), slotAt(f, k, ShootStart), smoothStep((t - End) / (ShootStart - End)));

                return slotAt(f, k, t);
            }

            /// <summary>
            /// The way UFO <paramref name="k"/> faces: while it flies to the walls it aims at the middle from where it is; see <see cref="pullAim"/> while it pulls; while it gathers it turns
            /// to the way it will shoot, and then it sweeps 60 degrees to its even angle over the first half of the shooting.
            /// </summary>
            private float facingOf(BulletField f, int k, double t)
            {
                if (t < landed)
                    return aimAtMiddle(f, PositionOf(f, k, t));

                if (t <= End)
                    return pullAim(f, k, t);

                float sign = k % 2 == 0 ? 1f : -1f;
                float offset = DanmakusuBalance.NUE_SWEEP_DEGREES * MathF.PI / 180f;

                if (t < ShootStart)
                {
                    // (it turns from the way it pulled to the way it will shoot: away from the middle)
                    float pulled = pullAim(f, k, End);
                    float turn = smoothStep((t - End) / (ShootStart - End));
                    float between = MathF.IEEERemainder(slotAngle(k, ShootStart) - pulled, MathF.Tau);
                    return pulled + between * turn + sign * offset * turn;
                }

                return slotAngle(k, t) + sign * offset * sweepLeft(t);
            }

            /// <summary>How much of the 60 degrees a UFO is still turned off its even angle while it shoots (1..0): it gets there in the first half of the shooting.</summary>
            private float sweepLeft(double t) => 1f - (float)Math.Clamp((t - ShootStart) / ((ShootEnd - ShootStart) * DanmakusuBalance.NUE_SWEEP_SHARE), 0.0, 1.0);

            private float alphaOf(double t) => smoothStep((t - Start) / 150.0) * (1f - smoothStep((t - ShootEnd) / 300.0));

            public override void Update(BulletField f, double now)
            {
                // (once the UFOs have been sent they stay: going back in time does not undo any of their phases)
                if (now < Start)
                    return;

                for (int k = 0; k < Count; k++)
                {
                    if (known[k])
                        continue;

                    known[k] = true;
                    launchedFrom[k] = f.bossPositionOf(Period);
                }
            }

            /// <summary>After the field has checked what was sucked in this frame: when the shooting starts, what every UFO has in it is shot out.</summary>
            public override void Late(BulletField f, double now)
            {
                if (now >= ShootStart && !built)
                    buildShot(f);
            }

            /// <summary>The UFOs' phases cannot be reset: what they took in and what they shot stays when time goes back.</summary>
            public override void Reset(BulletField f, double time)
            {
            }

            /// <summary>Where across the start of a cone (the UFO's width, -1..1) the <paramref name="j"/>th bullet goes: spread evenly over all of it.</summary>
            private static float acrossCone(int j, int k) => (float)((j * 0.6180339887 + k * 0.37) % 1.0) * 2f - 1f;

            private void buildShot(BulletField f)
            {
                built = true;

                float sweep = DanmakusuBalance.NUE_SWEEP_DEGREES * MathF.PI / 180f;
                double span = Math.Max(ShootEnd - ShootStart, 1);

                // what all the UFOs took in is shared out evenly between them (in the order it was taken, one to each UFO in turn), whichever UFO took it
                var all = new List<StoredBullet>();

                foreach (var list in stored)
                    all.AddRange(list);

                all.Sort((a, b) => a.Time.CompareTo(b.Time));

                for (int k = 0; k < Count; k++)
                {
                    var list = new List<StoredBullet>();

                    // (every bullet that was taken in comes out several times: the copies are spread over the cone and the shooting time like all the others)
                    for (int i = k; i < all.Count; i += Count)
                    {
                        for (int copy = 0; copy < DanmakusuBalance.NUE_SHOT_COPIES; copy++)
                            list.Add(all[i]);
                    }

                    int count = list.Count;

                    if (count == 0)
                        continue;

                    float sign = k % 2 == 0 ? 1f : -1f;

                    for (int j = 0; j < count; j++)
                    {
                        double fired = ShootStart + (j + 0.5) / count * span * 0.92;
                        // (the bullets come out evenly over the whole start of the cone, each flying away from the cone's apex so that together they fill it; the circle turns meanwhile)
                        float inCone = acrossCone(j, k);
                        float aim = slotAngle(k, fired) + sign * sweep * sweepLeft(fired);
                        Vector2 perpendicular = new Vector2(-MathF.Sin(aim), MathF.Cos(aim));
                        float angle = aim + inCone * DanmakusuBalance.NUE_SHOOT_CONE_HALF_ANGLE_DEGREES * MathF.PI / 180f;
                        Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        var taken = list[j];
                        Vector2 velocity = direction * (taken.Speed * DanmakusuBalance.NUE_SHOOT_SPEED_FACTOR);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = slotAt(f, k, fired) + perpendicular * (inCone * width(f) * 0.5f),
                            V = velocity,
                            Radius = taken.Radius,
                            Hit = taken.Hit,
                            Style = taken.Style,
                            Shape = taken.Shape,
                            Tint = taken.Tint,
                            TintAmount = taken.TintAmount,
                            Shade = taken.Shade,
                            Until = fired + f.stageLife(velocity),
                            Bloom = DanmakusuBalance.NUE_SHOT_BLOOM,
                        });
                    }
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (t < landed || t > End)
                    return false;

                // (only every fourth bullet of a ring is pulled: the others fly through the cones)
                if (bullet % DanmakusuBalance.NUE_PULL_EVERY != 0)
                    return false;

                float baseWidth = width(f);
                float length = DanmakusuBalance.NUE_CONE_LENGTH * f.circleRadius;
                float tangent = MathF.Tan(halfAngle);

                // the UFO that takes the bullet: of all the UFOs whose cones it is in, the one that has taken the fewest so far (so where cones cross, the bullets are shared out evenly between them)
                int chosen = -1;

                for (int k = 0; k < Count; k++)
                {
                    if (!known[k])
                        continue;

                    Vector2 position = pullPlace(f, k, t);
                    float aim = pullAim(f, k, t);
                    Vector2 axis = new Vector2(MathF.Cos(aim), MathF.Sin(aim));
                    Vector2 d = to - position;
                    float along = Vector2.Dot(d, axis);

                    if (along < 0f || along > length)
                        continue;

                    float across = MathF.Abs(d.X * axis.Y - d.Y * axis.X);

                    if (across > baseWidth * 0.5f + along * tangent + emitter.HitRadius)
                        continue;

                    if (chosen < 0 || stored[k].Count < stored[chosen].Count)
                        chosen = k;
                }

                if (chosen < 0)
                    return false;

                // (a bullet taken before time went back is not taken twice)
                if (takenAlready.Contains((emitter, bullet)))
                    return true;

                var taken = f.makeStored(emitter, t);
                float inCone = acrossCone(stored[chosen].Count, chosen);

                // it is pulled into the UFO (a copy that flies straight from where it was taken to the start of the cone, spread evenly over the UFO's width, at a speed that does not change,
                // keeping its size and still hurting on the way) instead of vanishing where it was taken; the UFO slides on meanwhile, so the copy flies to where the UFO will be when it gets there
                float speed = taken.Speed * DanmakusuBalance.NUE_PULL_SPEED_FACTOR;
                double flight = 0;
                Vector2 way = Vector2.Zero;

                for (int pass = 0; pass < 3; pass++)
                {
                    double arrival = t + flight;
                    float chosenAim = pullAim(f, chosen, arrival);
                    Vector2 target = pullPlace(f, chosen, arrival) + new Vector2(-MathF.Sin(chosenAim), MathF.Cos(chosenAim)) * (inCone * baseWidth * 0.5f);
                    way = target - to;
                    flight = way.Length / Math.Max(speed, 1e-5f);
                }

                float distance = way.Length;
                Vector2 velocity = distance > 1e-3f ? way / distance * speed : Vector2.Zero;

                // everything that is pulled gets into the UFO and is shot out again: a bullet that could not get there before the pulling ends is not pulled at all (it flies on)
                if (t + flight > End)
                    return false;

                takenAlready.Add((emitter, bullet));
                stored[chosen].Add(taken);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = taken.Radius,
                    Hit = taken.Hit,
                    Style = taken.Style,
                    Shape = taken.Shape,
                    Tint = taken.Tint,
                    TintAmount = taken.TintAmount,
                    Shade = taken.Shade,
                    Until = t + flight,
                });

                return true;
            }

            public override void Draw(BulletField f, double t)
            {
                if (t < Start)
                    return;

                float baseWidth = width(f);
                float length = DanmakusuBalance.NUE_CONE_LENGTH * f.circleRadius;
                float tangent = MathF.Tan(halfAngle);
                float apex = baseWidth * 0.5f / tangent;
                float total = apex + length;
                float alpha = alphaOf(t);

                if (alpha <= 0.002f)
                    return;

                // the cones are on the UFOs from when they are sent: dimmer while they only warn (the UFOs fly to the walls, or to the middle), brighter once they pull or shoot
                float warn = DanmakusuBalance.NUE_CONE_WARN_SHARE;
                float pullCone = DanmakusuBalance.NUE_CONE_ALPHA * smoothStep((t - Start) / 150.0) * (warn + (1f - warn) * smoothStep((t - landed) / 150.0)) * (1f - smoothStep((t - End) / 200.0));
                float shootCone = DanmakusuBalance.NUE_CONE_ALPHA * smoothStep((t - End - 100) / 150.0) * (warn + (1f - warn) * smoothStep((t - ShootStart) / 150.0)) * (1f - smoothStep((t - ShootEnd) / 200.0));

                // while they pull and while they shoot, the place round every UFO is darkened (drawn first: under the cones)
                float dim = DanmakusuBalance.NUE_DIM_ALPHA * Math.Max(
                    smoothStep((t - landed) / 200.0) * (1f - smoothStep((t - End) / 200.0)),
                    smoothStep((t - ShootStart) / 200.0) * (1f - smoothStep((t - ShootEnd) / 300.0)));

                if (dim > 0.002f)
                {
                    for (int k = 0; k < Count; k++)
                    {
                        if (known[k])
                            f.stageBack.Add(StageTexture.Glow, PositionOf(f, k, t), new Vector2(baseWidth * DanmakusuBalance.NUE_DIM_SIZE), 0f, dim * alpha, Color4.Black);
                    }
                }

                for (int k = 0; k < Count; k++)
                {
                    if (!known[k])
                        continue;

                    Vector2 p = PositionOf(f, k, t);
                    float facing = facingOf(f, k, t);

                    // the UFOs have the colours of the combos (all of them, in turn); the playfield shakes while they pull and while they shoot (see ShakeAt)
                    Color4 body = f.palette[k % f.palette.Length];
                    Color4 tint = new Color4(0.45f + 0.55f * body.R, 0.45f + 0.55f * body.G, 0.45f + 0.55f * body.B, 1f);

                    f.stageFront.Add(StageTexture.Ufo, p, new Vector2(baseWidth, baseWidth * 132f / 256f), facing * 180f / MathF.PI - 90f, alpha, tint);

                    if (pullCone > 0.002f)
                    {
                        Vector2 axis = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
                        f.stageBack.Add(StageTexture.Cone, p - axis * apex + axis * (total * 0.5f), new Vector2(total, 2f * total * tangent), facing * 180f / MathF.PI, pullCone, new Color4(0.8f, 0.95f, 1f, 1f));
                    }

                    if (shootCone > 0.002f)
                    {
                        // (the cone shows where the bullets go)
                        Vector2 axis = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
                        float coneAlpha = shootCone;
                        float shootTangent = MathF.Tan(DanmakusuBalance.NUE_SHOOT_CONE_HALF_ANGLE_DEGREES * MathF.PI / 180f);
                        float shootApex = baseWidth * 0.5f / shootTangent;
                        float shootTotal = shootApex + length;

                        f.stageBack.Add(StageTexture.ShootCone, p - axis * shootApex + axis * (shootTotal * 0.5f), new Vector2(shootTotal, 2f * shootTotal * shootTangent), facing * 180f / MathF.PI, coneAlpha, new Color4(1f, 0.85f, 0.7f, 1f));
                    }
                }
            }
        }
    }
}
