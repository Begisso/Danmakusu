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
        // Ran Yakumo: when an attack begins, and every eight beats of it, eight beams grow out of where she stands (a fan of its own each time), each along a golden spiral (it widens by the golden ratio every quarter of a turn), evenly round her; where they start stays where it is.
        // Once all eight have got past the edge of the screen they go from the end they started at, as fast as they grew. All that time, while they grow too, a bullet that crosses one bounces off it, twice at most. The beams hurt like a slider body.
        // While they grow their tips cut the bullets on their way in two, along the spiral (see CuttingAct).
        // She throws rings of eight talismans that start at twice their speed and slow down to a quarter of it a playfield's length out (the planner's).
        // A spiral is a formula: where a point is against the spirals is worked out from its angle and distance, whatever the length of the beams.
        // ======================================================================================================================================================

        private sealed class FoxAct : CuttingAct
        {
            private sealed class Fan
            {
                public double Time;
                public double Full; // (how long it takes them to grow past the edge of the screen)
                public Vector2 Centre;
            }

            private const float start_radius = 24f;

            /// <summary>The growth of the spiral: its radius is start_radius * e^(this * angle): the golden ratio every quarter of a turn.</summary>
            private static readonly float growth = MathF.Log(1.618034f) / (MathF.PI / 2f);

            /// <summary>The length of the spiral per px of radius.</summary>
            private static readonly float lengthPerRadius = MathF.Sqrt(1f + 1f / (growth * growth));

            private static readonly float step = MathF.Tau / DanmakusuBalance.RAN_BEAMS;

            private readonly List<double> throws;
            private readonly List<Fan> fans = new List<Fan>();
            private readonly List<Vector2> points = new List<Vector2>();

            public FoxAct(BossPeriod period, List<double> throws)
            {
                Period = period;
                this.throws = throws;
                Start = throws[0];
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + 60000;
            }

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.RAN_BEAM_SPEED;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            public override void Reset(BulletField f, double time)
            {
                while (fans.Count > 0 && fans[^1].Time > time)
                    fans.RemoveAt(fans.Count - 1);

                ClearPieces();
            }

            public override void Draw(BulletField f, double t)
            {
                if (!f.failed)
                    DrawPieces(f, t);
            }

            /// <summary>The tip of a spiral that is still growing cuts what it has run into since the last step, along the spiral.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                foreach (var fan in fans)
                {
                    if (t < fan.Time || t > fan.Time + fan.Full)
                        continue;

                    Vector2 offset = to - fan.Centre;
                    float distance = offset.Length;
                    float reach = halfWidth(f) + SizeOf(emitter, bullet);
                    float at = along(distance);
                    float lengthways = reach * lengthPerRadius;

                    if (at > speed * (float)(t - fan.Time) + lengthways || at < speed * (float)(ScanPrevious - fan.Time) - lengthways)
                        continue;

                    float phase = phaseOf(offset, distance);
                    float sideways = Math.Min(phase, step - phase) * distance * growth / MathF.Sqrt(1f + growth * growth);

                    if (sideways > reach)
                        continue;

                    Vector2 outward = distance > 1e-3f ? offset / distance : Vector2.UnitX;
                    Vector2 normal = Vector2.Normalize(outward - new Vector2(-outward.Y, outward.X) * growth);

                    // (just past a spiral in the way the angle goes it lies behind the bullet along the normal, just before the next one ahead of it)
                    CutBullet(f, t, from, to, emitter, bullet, normal, phase < step - phase ? sideways : -sideways);
                    return true;
                }

                return false;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                while (fans.Count < throws.Count && throws[fans.Count] <= now)
                {
                    Vector2 centre = f.bossVisualOf(Period);
                    float far = Math.Max(Math.Max(Vector2.Distance(centre, f.stageMin), Vector2.Distance(centre, f.stageMax)), Math.Max(Vector2.Distance(centre, new Vector2(f.stageMin.X, f.stageMax.Y)), Vector2.Distance(centre, new Vector2(f.stageMax.X, f.stageMin.Y))));

                    if (now - throws[fans.Count] < 250)
                        f.playStageSound("touhou_strike");

                    fans.Add(new Fan { Time = throws[fans.Count], Centre = centre, Full = far * lengthPerRadius / speed });
                }
            }

            /// <summary>The part of the beams that is there at <paramref name="t"/>, as lengths along them from where they start.</summary>
            private void partOf(Fan fan, double t, out float from, out float to)
            {
                to = speed * (float)(t - fan.Time);
                from = speed * (float)Math.Max(t - fan.Time - fan.Full, 0.0);
            }

            private static bool over(Fan fan, double t) => t < fan.Time || t > fan.Time + fan.Full * 2.0;

            /// <summary>Where a point lies between two neighbouring spirals: 0..step, and 0 or step is on one.</summary>
            private static float phaseOf(Vector2 offset, float distance)
            {
                float phase = (MathF.Atan2(offset.Y, offset.X) - MathF.Log(Math.Max(distance, 1f) / start_radius) / growth) % step;
                return phase < 0f ? phase + step : phase;
            }

            private static float along(float distance) => (distance - start_radius) * lengthPerRadius;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = halfWidth(f);

                foreach (var fan in fans)
                {
                    if (over(fan, t))
                        continue;

                    partOf(fan, t, out float from, out float to);

                    for (int beam = 0; beam < DanmakusuBalance.RAN_BEAMS; beam++)
                    {
                        points.Clear();

                        for (float s = from; ; s += DanmakusuBalance.RAN_BEAM_POINT_GAP)
                        {
                            float last = Math.Min(s, to);
                            float distance = start_radius + last / lengthPerRadius;
                            float angle = beam * step + MathF.Log(distance / start_radius) / growth;

                            points.Add(fan.Centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance);

                            if (s >= to)
                                break;
                        }

                        f.addPlainBeam(points, width, colour, border, ref beams, ref pointTotal);
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float slope = growth / MathF.Sqrt(1f + growth * growth);

                foreach (var fan in fans)
                {
                    if (over(fan, now))
                        continue;

                    partOf(fan, now, out float from, out float to);
                    Vector2 offset = f.characterPosition - fan.Centre;
                    float distance = offset.Length;
                    float at = along(distance);

                    if (at < from || at > to)
                        continue;

                    float phase = phaseOf(offset, distance);

                    if (Math.Min(phase, step - phase) * distance * slope <= reach)
                        return true;
                }

                return false;
            }

            /// <summary>(a bullet bounces off a beam: a ricochet, it flashes)</summary>
            public override bool PushIsRicochet => true;

            /// <summary>(the bounces off her beams are counted, as the bounces off Kyouko's walls are: a bullet bounces twice at most)</summary>
            public override bool CountsAsWallBounce => true;

            /// <summary>While the beams grow and while they go a bullet that crosses what is there of one bounces off it, twice at most (not one that something else has turned).</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                if (f.failed || !emitter.CanBeDeflected)
                    return false;

                if (emitter.Deflections != null && emitter.Deflections.TryGetValue(bullet, out var turned) && (turned.By != Id || emitter.WallBouncesOf(bullet, Id) >= DanmakusuBalance.RAN_BOUNCES))
                    return false;

                foreach (var fan in fans)
                {
                    if (over(fan, t))
                        continue;

                    Vector2 before = from - fan.Centre, after = to - fan.Centre;
                    float distance = after.Length;
                    float phaseBefore = phaseOf(before, before.Length), phaseAfter = phaseOf(after, distance);

                    if (MathF.Abs(phaseBefore - phaseAfter) < step * 0.5f)
                        continue; // (it has not gone over one)

                    partOf(fan, t, out float near, out float far);
                    float at = along(distance);

                    if (at < near || at > far)
                        continue;

                    Vector2 outward = distance > 1e-3f ? after / distance : Vector2.UnitX;
                    Vector2 round = new Vector2(-outward.Y, outward.X);
                    Vector2 normal = Vector2.Normalize(outward - round * growth);
                    Vector2 way = to - from;

                    if (way.LengthSquared < 1e-8f)
                        return false;

                    direction = Vector2.Normalize(way - normal * (2f * Vector2.Dot(way, normal)));
                    speed = emitter.SpeedOf(bullet);

                    // (it flies on from the side it came from: where it got to past the beam, mirrored across it; from the far side it would cross the beam again at once and bounce straight back)
                    float past = Math.Min(phaseAfter, step - phaseAfter) * distance * growth / MathF.Sqrt(1f + growth * growth);
                    position = to - normal * (2f * past * MathF.Sign(Vector2.Dot(way, normal)));
                    return true;
                }

                return false;
            }
        }
    }
}
