using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Watatsuki no Yorihime, when there is no other boss for her to repeat (see BulletPlanner: what she repeats she does herself, on rings and acts of her own rank): during an attack every second bullet that has passed all the way through the border of the screen is shot again from her,
        // half as big and half as fast, towards the place where it left the screen. Every slider's beam that reaches the border is copied too: from where she is, towards the place it got to, a straight
        // beam half as wide, at the beam's own speed, fired for as long as the original was. It hurts like a slider body. Beams that turn are not copied.
        // ======================================================================================================================================================

        private sealed class ReturnAct : StageAct
        {
            /// <summary>A beam she has copied.</summary>
            private sealed class Copy
            {
                public double Time; // (when the front of the original got to the border)
                public double Fires; // (how long it is fired for)
                public Vector2 From;
                public Vector2 Way;
                public float Speed;
                public float HalfWidth;
                public int Style;
            }

            private const float copy_reach = 2000f; // (px: past any screen)

            private readonly BulletField field;
            private int crossings; // (the bullets that have gone out: every second one comes back)
            private readonly List<LaserEmitter> originals = new List<LaserEmitter>();
            private readonly HashSet<(LaserEmitter, int)> looked = new HashSet<(LaserEmitter, int)>();
            private readonly List<Copy> copies = new List<Copy>();
            private readonly List<Vector2> copyPoints = new List<Vector2>();

            public ReturnAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                AbsorbFrom = start;
                AbsorbTo = end;

                foreach (var laser in field.lasers)
                {
                    if (!laser.Rotates && !laser.FollowsBoss && laser.Speed > 0f && laser.BallPositions.Length > 0 && laser.StartTime < end && laser.EndTime + laser.Lifetime >= start)
                        originals.Add(laser);
                }
            }

            private Vector2 bossNow(BulletField f) => f.bossPositionOf(Period);

            public override void Reset(BulletField f, double time)
            {
                crossings = 0;
                looked.Clear();
                copies.Clear();
                ClearBullets();
            }

            /// <summary>Beams whose front has got to the border during the attack are copied.</summary>
            private void copyBeams(BulletField f, double now)
            {
                foreach (var laser in originals)
                {
                    if (now < laser.StartTime)
                        continue;

                    for (int beam = 0; beam < Math.Min(laser.BeamCount, laser.Directions.Length); beam++)
                    {
                        if (looked.Contains((laser, beam)))
                            continue;

                        Vector2 start = Vector2.Clamp(laser.BallPositions[0], f.stageMin, f.stageMax);
                        Vector2 way = laser.Directions[beam];
                        float distance = (float)f.toWall(start, way, out _);
                        double reaches = laser.StartTime + distance / laser.Speed;

                        if (now < reaches)
                            continue;

                        looked.Add((laser, beam));

                        if (reaches < Start || reaches > End)
                            continue;

                        Vector2 from = bossNow(f);
                        Vector2 aim = start + way * distance - from;

                        if (aim.LengthSquared < 1f)
                            continue;

                        copies.Add(new Copy
                        {
                            Time = reaches,
                            Fires = laser.EndTime - laser.StartTime,
                            From = from,
                            Way = Vector2.Normalize(aim),
                            Speed = laser.Speed,
                            HalfWidth = laser.HalfWidth * DanmakusuBalance.YORIHIME_RETURN_FACTOR,
                            Style = f.comboStyle(laser.ComboIndex, false, laser.BossRank),
                        });
                    }
                }
            }

            /// <summary>The ends of a copied beam at <paramref name="t"/>: it grows out of her while it is fired, and then flies on off the screen; false when it is not there.</summary>
            private static bool endsOf(Copy copy, double t, out Vector2 back, out Vector2 front)
            {
                double age = t - copy.Time;
                float behind = (float)Math.Max(age - copy.Fires, 0.0) * copy.Speed;
                back = copy.From + copy.Way * behind;
                front = copy.From + copy.Way * Math.Min((float)age * copy.Speed, copy_reach);
                return age > 0 && behind < copy_reach;
            }

            /// <summary>The beams she has copied, drawn like the other beams (the field calls this while it hands the beams to the renderer).</summary>
            public void AddBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                foreach (var copy in copies)
                {
                    if (!endsOf(copy, t, out Vector2 back, out Vector2 front))
                        continue;

                    copyPoints.Clear();
                    copyPoints.Add(back);
                    copyPoints.Add(front);
                    f.addPlainBeam(copyPoints, copy.HalfWidth, f.palette[copy.Style % f.palette.Length], f.borderPalette[copy.Style % f.borderPalette.Length], ref beams, ref pointTotal);
                }
            }

            /// <summary>The copied beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                foreach (var copy in copies)
                {
                    if (endsOf(copy, now, out Vector2 back, out Vector2 front)
                        && distanceToSegment(f.characterPosition, back, front) <= copy.HalfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                {
                    if (now < Start)
                        Reset(f, now);

                    return;
                }

                if (now > End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    return;

                copyBeams(f, now);
            }

            /// <summary>The velocity of a bullet that is shot from her at the place where something left the screen, at <paramref name="speed"/>.</summary>
            private Vector2 aimFrom(BulletField f, Vector2 contact, float speed)
            {
                Vector2 aim = Vector2.Clamp(contact, f.stageMin, f.stageMax) - bossNow(f);

                if (aim.LengthSquared < 1f)
                    aim = new Vector2(0f, 1f);

                return Vector2.Normalize(aim) * Math.Max(speed, 0.02f);
            }

            /// <summary>Every second bullet that has gone all the way out of the screen is shot again from her, half as big and half as fast, at the place where it went out.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (t < Start || t > End || emitter.Shape == BulletShape.Train)
                    return false;

                float r = emitter.VisualRadius;
                Vector2 low = f.stageMin - new Vector2(r), high = f.stageMax + new Vector2(r); // (the centre of a bullet that is all the way out is beyond these)
                bool wasIn = from.X >= low.X && from.X <= high.X && from.Y >= low.Y && from.Y <= high.Y;
                bool isOut = to.X < low.X || to.X > high.X || to.Y < low.Y || to.Y > high.Y;

                if (!wasIn || !isOut)
                    return false;

                if (crossings++ % 2 != 0)
                    return false;

                Vector2 delta = to - from;
                float u = 1f;

                if (delta.X > 1e-6f && to.X > high.X)
                    u = Math.Min(u, (high.X - from.X) / delta.X);
                else if (delta.X < -1e-6f && to.X < low.X)
                    u = Math.Min(u, (low.X - from.X) / delta.X);

                if (delta.Y > 1e-6f && to.Y > high.Y)
                    u = Math.Min(u, (high.Y - from.Y) / delta.Y);
                else if (delta.Y < -1e-6f && to.Y < low.Y)
                    u = Math.Min(u, (low.Y - from.Y) / delta.Y);

                var stored = f.makeStored(emitter, t);
                Vector2 velocity = aimFrom(f, from + delta * Math.Clamp(u, 0f, 1f), stored.Speed * DanmakusuBalance.YORIHIME_RETURN_FACTOR);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = bossNow(f),
                    V = velocity,
                    Radius = stored.Radius * DanmakusuBalance.YORIHIME_RETURN_FACTOR,
                    Hit = stored.Hit * DanmakusuBalance.YORIHIME_RETURN_FACTOR,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = t + f.stageLife(velocity),
                });

                return true;
            }
        }
    }
}
