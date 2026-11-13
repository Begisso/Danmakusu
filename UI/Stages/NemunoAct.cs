using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        private readonly List<NemunoAct> nemunoActs = new List<NemunoAct>();

        // ======================================================================================================================================================
        // Nemuno Sakata: every tenth slider beam of her section is hers when its slider ends (the planner takes it out of the field from then on: LaserEmitter.VanishAt). It stops where it is, all of it, and
        // over a beat turns round its middle until it points at the player, straightening smoothly into a line as long as it was (its curve is laid out along the way it points); then over a beat two copies
        // of it grow out of it on each side, sliding to their places and widening from nothing (five straight beams side by side, far apart). Then the row of five turns as one round the middle beam until
        // that one aims at where the player is at that moment (they stay parallel), they all flash (faster and brighter), and two beats after the split they are thrown that way: all five fall the same way,
        // from the middle beam towards that place (as Zanmu's stopped things fall), side by side, getting darker, and each is gone once it is off the screen. They hurt like a slider body all along (a copy
        // as wide as it has grown).
        // ======================================================================================================================================================

        private sealed class NemunoAct : StageAct
        {
            private sealed class Taken
            {
                public LaserEmitter Laser = null!;
                public bool Known;
                public Vector2 Centre;
                public List<Vector2> Shape = new List<Vector2>(); // (the beam as it was when it stopped, round its middle)
                public List<Vector2> Straight = new List<Vector2>(); // (the same points laid out on a straight line through its middle, along its ends, as far apart as along the beam)
                public float FromAngle, ToAngle;
                public bool Falling;
                public Vector2 FallWay;

                // (where the player was when the copies had grown: every beam aims there and is thrown there)
                public bool AimKnown;
                public Vector2 AimPoint;
            }

            private readonly List<Taken> taken = new List<Taken>();
            private readonly double beat;
            private readonly List<Vector2> points = new List<Vector2>();
            private Vector2[] buffer = new Vector2[256];

            public NemunoAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                foreach (var laser in period.TakenBeams)
                    taken.Add(new Taken { Laser = laser });

                Start = taken.Count > 0 ? taken[0].Laser.EndTime : period.End;
                End = Start;

                foreach (var t in taken)
                    End = Math.Max(End, fallsAt(t) + 20000);
            }

            private double turnedAt(Taken t) => t.Laser.EndTime + beat * DanmakusuBalance.NEMUNO_AIM_BEATS;

            private double splitAt(Taken t) => turnedAt(t) + beat * DanmakusuBalance.NEMUNO_SPLIT_BEATS;

            private double fallsAt(Taken t) => splitAt(t) + beat * DanmakusuBalance.NEMUNO_HOLD_BEATS;

            private float halfWidth(Taken t) => Math.Max(t.Laser.HalfWidth, 1f);

            private static float angleOf(Vector2 v) => MathF.Atan2(v.Y, v.X);

            public override void Reset(BulletField f, double time)
            {
                foreach (var t in taken)
                {
                    if (t.Laser.EndTime > time)
                        t.Known = false;

                    if (fallsAt(t) > time)
                        t.Falling = false;

                    if (splitAt(t) > time)
                        t.AimKnown = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var t in taken)
                {
                    // the beam stops when its slider ends: it is taken as it is then, and it will point at where the player is now
                    if (!t.Known && now >= t.Laser.EndTime && now < t.Laser.EndTime + 250)
                        capture(f, t);

                    // (when the copies have grown, the place they will aim at and be thrown at: where the player is then)
                    if (t.Known && !t.AimKnown && now >= splitAt(t))
                    {
                        t.AimPoint = f.characterPosition;
                        t.AimKnown = true;
                    }

                    if (t.Known && !t.Falling && now >= fallsAt(t))
                    {
                        Vector2 line = f.characterPosition - f.bossPositionOf(Period);
                        t.FallWay = line.LengthSquared > 1e-4f ? Vector2.Normalize(line) : Vector2.UnitY;
                        t.Falling = true;
                    }
                }
            }

            private void capture(BulletField f, Taken t)
            {
                var laser = t.Laser;
                double at = laser.EndTime - 0.5;

                if (buffer.Length < laser.PointCapacity + 8)
                    buffer = new Vector2[laser.PointCapacity + 8];

                t.Shape.Clear();

                for (int beam = 0; beam < laser.BeamCount; beam++)
                {
                    int n = laser.GetPoints(at, buffer, beam);

                    for (int i = 0; i < n; i++)
                        t.Shape.Add(buffer[i]);

                    if (n > 0)
                        break; // (one beam of the slider: the first that has points)
                }

                if (t.Shape.Count < 2)
                    return;

                Vector2 centre = Vector2.Zero;

                foreach (var p in t.Shape)
                    centre += p;

                centre /= t.Shape.Count;

                for (int i = 0; i < t.Shape.Count; i++)
                    t.Shape[i] -= centre;

                t.Centre = centre;
                t.FromAngle = angleOf(t.Shape[^1] - t.Shape[0]);

                // the line it straightens into: through the middle of its ends, along them, every point as far along as it was along the beam
                float length = 0f;

                for (int i = 1; i < t.Shape.Count; i++)
                    length += Vector2.Distance(t.Shape[i - 1], t.Shape[i]);

                Vector2 along = new Vector2(MathF.Cos(t.FromAngle), MathF.Sin(t.FromAngle));
                Vector2 middle = (t.Shape[0] + t.Shape[^1]) * 0.5f;
                float walked = 0f;
                t.Straight.Clear();

                for (int i = 0; i < t.Shape.Count; i++)
                {
                    if (i > 0)
                        walked += Vector2.Distance(t.Shape[i - 1], t.Shape[i]);

                    t.Straight.Add(middle + along * (walked - length * 0.5f));
                }

                Vector2 toPlayer = f.characterPosition - centre;
                t.ToAngle = toPlayer.LengthSquared > 1e-4f ? angleOf(toPlayer) : t.FromAngle;
                t.Known = true;
            }

            /// <summary>
            /// The five beams at <paramref name="time"/>: copy <paramref name="copy"/> (-2..2, 0 is the beam itself) into <see cref="points"/>, with how wide it is (a copy grows) and how much it flashes;
            /// false when it is not there (yet, or any more).
            /// </summary>
            private bool beamAt(BulletField f, Taken t, int copy, double time, out float darkness, out float width, out float flash)
            {
                darkness = 0f;
                flash = 0f;
                width = halfWidth(t);
                points.Clear();

                if (!t.Known || time < t.Laser.EndTime)
                    return false;

                // (it turns round its middle by the short way until it points at the player, and straightens out as it turns)
                float turning = smoothStep((time - t.Laser.EndTime) / (turnedAt(t) - t.Laser.EndTime));
                float turn = MathF.IEEERemainder(t.ToAngle - t.FromAngle, MathF.Tau) * turning;
                float cos = MathF.Cos(turn), sin = MathF.Sin(turn);

                // (the copies grow out of it: they slide out across the way it points to their places, far apart, widening from nothing)
                float spread = smoothStep((time - turnedAt(t)) / (splitAt(t) - turnedAt(t)));
                float across = halfWidth(t) * DanmakusuBalance.NEMUNO_COPY_GAP * copy * spread;

                if (copy != 0)
                {
                    if (spread <= 0.001f)
                        return false;

                    width *= spread;
                }

                Vector2 aim = new Vector2(MathF.Cos(t.ToAngle), MathF.Sin(t.ToAngle));
                Vector2 side = new Vector2(-aim.Y, aim.X);
                Vector2 middle = t.Centre + side * across;

                // (then the row turns as one round the middle beam until that one aims at where the player was when they had grown, over a beat: they stay parallel, side by side, and flash until they are thrown,
                // all the same way)
                float aimTurn = 0f;
                Vector2 fallWay = t.FallWay;

                if (t.AimKnown && time > splitAt(t))
                {
                    Vector2 toAim = t.AimPoint - t.Centre;
                    float wanted = toAim.LengthSquared > 1e-4f ? angleOf(toAim) : t.ToAngle;
                    aimTurn = MathF.IEEERemainder(wanted - t.ToAngle, MathF.Tau) * smoothStep((time - splitAt(t)) / (beat * DanmakusuBalance.NEMUNO_REAIM_BEATS));
                    fallWay = toAim.LengthSquared > 1e-4f ? Vector2.Normalize(toAim) : t.FallWay;

                    Vector2 fromCentre = middle - t.Centre;
                    float rowCos = MathF.Cos(aimTurn), rowSin = MathF.Sin(aimTurn);
                    middle = t.Centre + new Vector2(fromCentre.X * rowCos - fromCentre.Y * rowSin, fromCentre.X * rowSin + fromCentre.Y * rowCos);

                    double hold = fallsAt(t) - splitAt(t);

                    if (time <= fallsAt(t))
                        flash = flashAt(time, fallsAt(t), hold);
                }

                Vector2 offset = Vector2.Zero;

                if (t.Falling && time > fallsAt(t))
                {
                    float fallen = (float)(time - fallsAt(t));
                    offset += fallWay * (0.5f * DanmakusuBalance.ZANMU_GRAVITY * fallen * fallen);
                    darkness = DanmakusuBalance.ZANMU_FALL_SHADE * smoothStep(fallen / DanmakusuBalance.ZANMU_FALL_DARKEN_MS);
                }

                float aimCos = MathF.Cos(aimTurn), aimSin = MathF.Sin(aimTurn);
                bool any = false;
                float margin = halfWidth(t) * 2f;

                for (int i = 0; i < t.Shape.Count; i++)
                {
                    Vector2 p = Vector2.Lerp(t.Shape[i], t.Straight[i], turning);
                    Vector2 r = new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
                    Vector2 q = middle + new Vector2(r.X * aimCos - r.Y * aimSin, r.X * aimSin + r.Y * aimCos) + offset;
                    points.Add(q);

                    if (q.X > f.stageMin.X - margin && q.X < f.stageMax.X + margin && q.Y > f.stageMin.Y - margin && q.Y < f.stageMax.Y + margin)
                        any = true;
                }

                return any || !t.Falling; // (a fallen beam that is all off the screen is gone)
            }

            /// <summary>The five beams of every taken beam, drawn like the other beams (the field calls this while it hands the beams to the renderer).</summary>
            public void AddBeams(BulletField f, double time, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                foreach (var t in taken)
                {
                    int style = f.comboStyle(t.Laser.ComboIndex, t.Laser.FollowsBoss, t.Laser.BossRank);
                    Color4 colour = f.palette[style];
                    Color4 border = f.borderPalette[style % f.borderPalette.Length];

                    for (int copy = -DanmakusuBalance.NEMUNO_COPIES_EACH_SIDE; copy <= DanmakusuBalance.NEMUNO_COPIES_EACH_SIDE; copy++)
                    {
                        if (!beamAt(f, t, copy, time, out float darkness, out float width, out float flash))
                            continue;

                        // (darker as it falls; whiter as it flashes before it is thrown)
                        var shaded = new Color4(colour.R * (1f - darkness), colour.G * (1f - darkness), colour.B * (1f - darkness), 1f);
                        shaded = new Color4(shaded.R + (1f - shaded.R) * flash, shaded.G + (1f - shaded.G) * flash, shaded.B + (1f - shaded.B) * flash, 1f);
                        f.addPlainBeam(points, width, shaded, border, ref beams, ref pointTotal);
                    }
                }
            }

            /// <summary>The five beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;

                foreach (var t in taken)
                {
                    for (int copy = -DanmakusuBalance.NEMUNO_COPIES_EACH_SIDE; copy <= DanmakusuBalance.NEMUNO_COPIES_EACH_SIDE; copy++)
                    {
                        if (!beamAt(f, t, copy, now, out _, out float width, out _))
                            continue;

                        float reach = width * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                        for (int i = 0; i + 1 < points.Count; i++)
                        {
                            if (distanceToSegment(player, points[i], points[i + 1]) <= reach)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
