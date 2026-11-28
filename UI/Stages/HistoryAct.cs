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
        // Keine Kamishirasawa (2026-11-28, "She shoots 8 beat long laserbeam streams from 3 points on magic circle. Mechanics similar to byakuren - records 10 osu objects every 16 beats, 5 of them shoot
        // 5 straight long laserbeams with preview that lives for 8 beats, 5 of them shoot ring of 5 3x size bullets that spread into 5 bullets once hit wall, 1.5x size instead, then those bullets each
        // separately spread into 5 1x size bullets after hitting a wall and behave normally. Red circle flash before each point shoots").
        // Her own shots: every KEINE_STREAM_EVERY_BEATS of her attacks (in a spinner too) a red circle flashes for KEINE_FLASH_BEATS on KEINE_POINTS points evenly round the edge of her magic circle (the
        // first towards the player), then each lets a beam out straight away from her middle for KEINE_STREAM_BEATS: its front flies out at KEINE_STREAM_SPEED times a plain bullet's speed, and when the
        // point stops its back flies after it, out of the screen. As thick as a slider's beam, hurting like a slider body. Where she is and where the player is are taken as the flash begins.
        // Every KEINE_RECORD_EVERY_BEATS of her section she records KEINE_RECORDS of the circles and sliders that start in it (spread evenly over them; where a slider starts); as the stretch ends a red
        // circle flashes on all of them for KEINE_WARN_BEATS, and then they all shoot at once (since 2026-11-28, "10 recorded points shoot at the same time": each shot 16 beats after its own object
        // before), half of them each way, in turn:
        // - KEINE_BEAMS long straight beams evenly round it, the first at the player, see-through during the flash, then solid for KEINE_BEAM_BEATS, hurting like a slider body; or
        // - a ring of KEINE_RING bullets KEINE_BIG times as big as a plain one, the first at the player; each splits where it gets to the edge of the screen into KEINE_SPLIT bullets KEINE_MIDDLE times as
        //   big, fanned out over KEINE_SPLIT_FAN_DEGREES round the way it would bounce, and each of those splits so again at the next edge into plain bullets, which fly on and out.
        // The rings are straight lines worked out as they are shot (the bosses' ops leave them alone), the player taken as the flash begins; a seek back shoots them again from where they were.
        // ======================================================================================================================================================

        private sealed class HistoryAct : StageAct
        {
            private sealed class Volley
            {
                public double T;
                public Vector2 Centre;
                public float Aim;
                public bool Known;
            }

            private sealed class Record
            {
                public double Fires;
                public Vector2 Place;
                public bool Beams;
                public int Number;
                public float Aim;
                public bool Known, Fired;
            }

            private readonly double beat;
            private readonly Volley[] volleys;
            private readonly Record[] records;
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public HistoryAct(BulletField f, BossPeriod period, CircleZone[] circles, SliderZone[] sliders)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<Volley>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = Math.Max(start, period.ShootStart); t < end && list.Count < 4000; t += beat * DanmakusuBalance.KEINE_STREAM_EVERY_BEATS)
                        list.Add(new Volley { T = t });
                }

                volleys = list.ToArray();

                // what she records: in every stretch of her section, so many of the circles and sliders that start in it, spread evenly over them, beams and rings in turn
                var objects = new List<(double Time, Vector2 Place)>();

                foreach (var circle in circles)
                {
                    if (circle.Time >= period.ShootStart && circle.Time < period.End)
                        objects.Add((circle.Time, circle.Position));
                }

                foreach (var slider in sliders)
                {
                    if (slider.FallSpeed <= 0f && slider.StartTime >= period.ShootStart && slider.StartTime < period.End && slider.Points.Length > 0)
                        objects.Add((slider.StartTime, slider.Points[0]));
                }

                objects.Sort((a, b) => a.Time.CompareTo(b.Time));

                var recorded = new List<Record>();
                double stretch = beat * DanmakusuBalance.KEINE_RECORD_EVERY_BEATS;
                int first = 0, number = 0;

                for (double w = period.ShootStart; w < period.End && recorded.Count < 4000; w += stretch)
                {
                    while (first < objects.Count && objects[first].Time < w)
                        first++;

                    int last = first;

                    while (last < objects.Count && objects[last].Time < w + stretch)
                        last++;

                    int n = last - first, take = Math.Min(DanmakusuBalance.KEINE_RECORDS, n);

                    for (int i = 0; i < take; i++)
                    {
                        // (all of a stretch shoot at once: as it ends they flash, then shoot - since 2026-11-28, "10 recorded points shoot at the same time"; each shot 16 beats after its own object before)
                        var o = objects[first + (int)((i + 0.5) * n / take)];
                        recorded.Add(new Record { Fires = w + stretch + beat * DanmakusuBalance.KEINE_WARN_BEATS, Place = o.Place, Beams = i % 2 == 0, Number = number++ });
                    }
                }

                recorded.Sort((a, b) => a.Fires.CompareTo(b.Fires));
                records = recorded.ToArray();

                double lastStream = volleys.Length > 0 ? streamStop(volleys[^1]) + far(f) / Math.Max(streamSpeed, 1e-4f) : period.End;
                double lastRecord = records.Length > 0 ? records[^1].Fires + beat * DanmakusuBalance.KEINE_BEAM_BEATS + treeLife(f) : period.End;
                Start = period.ShootStart;
                End = Math.Max(period.End + beat * 8, Math.Max(lastStream, lastRecord));
            }

            private float streamSpeed => stageBulletSpeed(Period) * DanmakusuBalance.KEINE_STREAM_SPEED;

            private double streamStart(Volley v) => v.T + beat * DanmakusuBalance.KEINE_FLASH_BEATS;

            private double streamStop(Volley v) => streamStart(v) + beat * DanmakusuBalance.KEINE_STREAM_BEATS;

            private double warnAt(Record r) => r.Fires - beat * DanmakusuBalance.KEINE_WARN_BEATS;

            private double beamsOver(Record r) => r.Fires + beat * DanmakusuBalance.KEINE_BEAM_BEATS;

            /// <summary>How far a stream goes before it is gone: past the farthest corner of the screen from anywhere.</summary>
            private static float far(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) * 1.2f;

            /// <summary>How long a ring and what it splits into can be on the screen at the most.</summary>
            private double treeLife(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) * 3.2f / Math.Max(stageBulletSpeed(Period), 1e-4f);

            private float half(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private static Vector2 way(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            private static float angleTo(Vector2 from, Vector2 to)
            {
                Vector2 d = to - from;
                return d.LengthSquared > 1f ? MathF.Atan2(d.Y, d.X) : MathF.PI / 2f;
            }

            public override void Reset(BulletField f, double time)
            {
                foreach (var v in volleys)
                {
                    if (v.T > time)
                        v.Known = false;
                }

                foreach (var r in records)
                {
                    if (warnAt(r) > time)
                        r.Known = false;

                    r.Fired = false; // (the rings that still fly are shot again, from when they were shot)
                }

                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var v in volleys)
                {
                    if (v.T > now)
                        break;

                    if (!v.Known)
                    {
                        v.Known = true;
                        v.Centre = f.bossPositionOf(Period);
                        v.Aim = angleTo(v.Centre, f.characterPosition);
                    }
                }

                double life = treeLife(f);

                foreach (var r in records)
                {
                    if (warnAt(r) > now)
                        break;

                    if (!r.Known)
                    {
                        r.Known = true;
                        r.Aim = angleTo(r.Place, f.characterPosition);
                    }

                    // (a ring is made as its flash begins, coming out when it fires: so it is there to be foreseen)
                    if (!r.Fired)
                    {
                        r.Fired = true;

                        if (!r.Beams && now - r.Fires < life)
                            shootRing(f, r);
                    }
                }
            }

            private void shootRing(BulletField f, Record r)
            {
                float speed = stageBulletSpeed(Period);

                for (int j = 0; j < DanmakusuBalance.KEINE_RING; j++)
                    split(f, r.Fires, r.Place, way(r.Aim + j * MathF.Tau / DanmakusuBalance.KEINE_RING) * speed, 0, r.Number);

                f.addFlash(r.Fires, r.Place, f.stageBaseRadius(Period) * DanmakusuBalance.KEINE_BIG * DanmakusuBalance.RICOCHET_FLASH_SIZE);
            }

            /// <summary>A bullet of generation <paramref name="generation"/> (0: the big ones, 1: the middle ones, 2: plain) from <paramref name="from"/> at <paramref name="t0"/>; the first two split at the edge of the screen.</summary>
            private void split(BulletField f, double t0, Vector2 from, Vector2 velocity, int generation, int number)
            {
                float plain = f.stageBaseRadius(Period);
                float radius = plain * (generation == 0 ? DanmakusuBalance.KEINE_BIG : generation == 1 ? DanmakusuBalance.KEINE_MIDDLE : 1f);

                var bullet = new StageBullet
                {
                    T0 = t0,
                    P0 = from,
                    V = velocity,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = f.comboStyle(number * 3 + generation, true, Period.Rank),
                    Shape = BulletShape.Circle,
                    Untraced = true,
                    Until = t0 + f.stageLife(velocity),
                };

                // (a plain one flies on and out; so does one that is at the edge already)
                if (generation >= 2 || velocity.LengthSquared < 1e-10f)
                {
                    AddBullet(bullet);
                    return;
                }

                double across = f.toWall(from, velocity, out bool sideways);

                if (across <= 0.5)
                {
                    AddBullet(bullet);
                    return;
                }

                bullet.Until = t0 + across;
                AddBullet(bullet);

                // (where it gets to the edge it splits: fanned out round the way it would bounce, all of them back into the screen)
                Vector2 at = Vector2.Clamp(from + velocity * (float)across, f.stageMin, f.stageMax);
                Vector2 outward = sideways ? new Vector2(MathF.Sign(velocity.X), 0f) : new Vector2(0f, MathF.Sign(velocity.Y));
                Vector2 back = sideways ? new Vector2(-velocity.X, velocity.Y) : new Vector2(velocity.X, -velocity.Y);
                float speed = velocity.Length, middle = MathF.Atan2(back.Y, back.X);
                float fan = DanmakusuBalance.KEINE_SPLIT_FAN_DEGREES * MathF.PI / 180f;
                int count = DanmakusuBalance.KEINE_SPLIT;

                for (int s = 0; s < count; s++)
                {
                    Vector2 d = way(middle + (s - (count - 1) * 0.5f) * fan / Math.Max(count - 1, 1));

                    if (Vector2.Dot(d, outward) > -0.15f)
                        d = Vector2.Normalize(d - outward * (Vector2.Dot(d, outward) + 0.3f)); // (one that would go out, or along the edge, is turned back in)

                    split(f, t0 + across, at - outward * 0.5f, d * speed, generation + 1, number);
                }
            }

            /// <summary>Her streams, and the beams of the places she recorded (see-through during their flash, then solid).</summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                var core = new Color4(0.97f, 0.98f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.36f, 0.4f, 0.78f, 1f)); // (her blue)
                float width = half(f), circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS, reach = far(f), length = Vector2.Distance(f.stageMin, f.stageMax) * 1.5f;

                foreach (var v in volleys)
                {
                    if (v.T > t)
                        break;

                    if (!v.Known || t < streamStart(v) || !streamAt(v, t, out float back, out float front, reach))
                        continue;

                    for (int i = 0; i < DanmakusuBalance.KEINE_POINTS; i++)
                    {
                        Vector2 d = way(v.Aim + i * MathF.Tau / DanmakusuBalance.KEINE_POINTS);
                        Vector2 point = v.Centre + d * circle;
                        line.Clear();
                        line.Add(point + d * back);
                        line.Add(point + d * front);
                        f.addPlainBeam(line, width, core, border, ref beams, ref pointTotal, null, null, null, 0.4f);
                    }
                }

                foreach (var r in records)
                {
                    if (warnAt(r) > t)
                        break;

                    if (!r.Beams || !r.Known || t >= beamsOver(r))
                        continue;

                    bool solid = t >= r.Fires;
                    float alpha = solid
                        ? 1f - smoothStep((t - (beamsOver(r) - beat * 0.25)) / (beat * 0.25))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - warnAt(r)) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int j = 0; j < DanmakusuBalance.KEINE_BEAMS; j++)
                    {
                        Vector2 d = way(r.Aim + j * MathF.Tau / DanmakusuBalance.KEINE_BEAMS);
                        line.Clear();
                        fades.Clear();
                        line.Add(r.Place);
                        line.Add(r.Place + d * length);
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, width, core, border, ref beams, ref pointTotal, null, fades, null, solid ? 0.4f : 0f);
                    }
                }
            }

            /// <summary>How far out a stream's back and front are at <paramref name="t"/> (from its point), false once it is gone.</summary>
            private bool streamAt(Volley v, double t, out float back, out float front, float reach)
            {
                front = Math.Min(streamSpeed * (float)(t - streamStart(v)), reach);
                back = Math.Max(0f, streamSpeed * (float)(t - streamStop(v)));
                return t >= streamStart(v) && back < reach && front > back;
            }

            /// <summary>The red circles that flash before a point shoots: on her magic circle before her streams, on a recorded place before it shoots.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                var red = new Color4(1f, 0.18f, 0.15f, 1f);
                float size = f.circleRadius * 1.2f, line_ = Math.Max(1.5f, f.circleRadius * 0.14f);

                foreach (var v in volleys)
                {
                    if (v.T > t)
                        break;

                    if (!v.Known || t >= streamStart(v))
                        continue;

                    float level = flash(t, v.T, streamStart(v));

                    for (int i = 0; i < DanmakusuBalance.KEINE_POINTS; i++)
                        f.stageRipple.AddRing(v.Centre + way(v.Aim + i * MathF.Tau / DanmakusuBalance.KEINE_POINTS) * DanmakusuBalance.MAGIC_CIRCLE_RADIUS, size * 0.8f, line_, level, red);
                }

                foreach (var r in records)
                {
                    if (warnAt(r) > t)
                        break;

                    if (t >= r.Fires)
                        continue;

                    f.stageRipple.AddRing(r.Place, size, line_, flash(t, warnAt(r), r.Fires), red);
                }
            }

            /// <summary>How bright a red circle that flashes from <paramref name="from"/> to <paramref name="to"/> is at <paramref name="t"/>: it comes in quickly and flashes three times.</summary>
            private static float flash(double t, double from, double to)
            {
                double u = (t - from) / Math.Max(to - from, 1);
                return smoothStep(u / 0.15) * (0.6f + 0.4f * MathF.Cos((float)u * MathF.PI * 6f));
            }

            /// <summary>Her streams and the solid beams of the recorded places hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = half(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS, far_ = far(f), length = Vector2.Distance(f.stageMin, f.stageMax) * 1.5f;

                foreach (var v in volleys)
                {
                    if (v.T > now)
                        break;

                    if (!v.Known || !streamAt(v, now, out float back, out float front, far_))
                        continue;

                    for (int i = 0; i < DanmakusuBalance.KEINE_POINTS; i++)
                    {
                        Vector2 d = way(v.Aim + i * MathF.Tau / DanmakusuBalance.KEINE_POINTS);
                        Vector2 point = v.Centre + d * circle;

                        if (distanceToSegment(player, point + d * back, point + d * front) <= reach)
                            return true;
                    }
                }

                foreach (var r in records)
                {
                    if (r.Fires > now)
                        break;

                    if (!r.Beams || !r.Known || now >= beamsOver(r))
                        continue;

                    for (int j = 0; j < DanmakusuBalance.KEINE_BEAMS; j++)
                    {
                        if (distanceToSegment(player, r.Place, r.Place + way(r.Aim + j * MathF.Tau / DanmakusuBalance.KEINE_BEAMS) * length) <= reach)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
