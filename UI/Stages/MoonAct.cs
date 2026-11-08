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
        // Kaguya Houraisan: she stands at the top of the screen and never pauses, with her magic circle clear of the edge. The background turns dark blue, darker towards the bottom, and a giant moon comes slowly down from above, with Yuyuko's tree
        // sound, until its bottom shows at the top of the screen. Every eighth bullet that goes out of the top comes back as one of Marisa's stars, falling three degrees to the left or to the right of straight down by turns, and
        // leaves a beam behind it that narrows to a point, whose slices live for four beats. A slider's beam that leaves the top falls back in short purple pieces (an op, see BossOps). Her own rings and how everything turns in her sections are the planner's. Once all the boss sections are over she comes back eight beats
        // later until the song ends (BossPeriod.Revival): the slices live for eight beats, every fourth bullet falls, and the planner changes the shapes of the bullets.
        // ======================================================================================================================================================

        private sealed class MoonAct : StageAct
        {
            private struct Fall
            {
                public double Time;
                public Vector2 Start, Way;
                public float Speed;
                public int Style;
            }

            private static readonly Color4 glow = new Color4(1f, 0.93f, 0.72f, 1f);

            // (the part of the moon that the picture of its bottom covers, in radii: see StageTexture.KaguyaMoonCap)
            private const float moon_cap_half_width = 0.72f;
            private const float moon_cap_depth = 0.3f;

            private readonly double beat;
            private readonly List<Fall> falls = new List<Fall>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private int crossings, firstFall;
            private bool risen;

            public MoonAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private double traceMs => beat * (Period.Revival ? DanmakusuBalance.KAGUYA_TRACE_AGAIN_BEATS : DanmakusuBalance.KAGUYA_TRACE_BEATS);

            private float width(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private static Vector2 head(in Fall fall, double t) => fall.Start + fall.Way * (fall.Speed * (float)(t - fall.Time));

            /// <summary>The old end of the beam: where the star was as long ago as a slice lives.</summary>
            private Vector2 tail(in Fall fall, double t) => fall.Start + fall.Way * (fall.Speed * (float)Math.Max(t - fall.Time - traceMs, 0.0));

            public override void Reset(BulletField f, double time)
            {
                falls.Clear();
                crossings = 0;
                firstFall = 0;
                risen = false;
                ClearBullets();
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = new Vector2((f.stageMin.X + f.stageMax.X) / 2f, f.stageMin.Y + DanmakusuBalance.MAGIC_CIRCLE_RADIUS + DanmakusuBalance.KAGUYA_TOP_GAP);
                return now >= Period.Start && now <= Period.End;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                if (!risen && now >= Start)
                {
                    risen = true;

                    if (now - Start < 250)
                        f.playStageSound("yuyuko_tree");
                }

                while (firstFall < falls.Count && tail(falls[firstFall], now).Y > f.stageMax.Y)
                    firstFall++;
            }

            /// <summary>Every eighth bullet that goes out of the top comes back as a star.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || from.Y < f.stageMin.Y || to.Y >= f.stageMin.Y || to.X < f.stageMin.X || to.X > f.stageMax.X)
                    return false;

                if (crossings++ % (Period.Revival ? DanmakusuBalance.KAGUYA_EVERY_AGAIN : DanmakusuBalance.KAGUYA_EVERY) != 0)
                    return false;

                float speed = stageBulletSpeed(Period);
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.FINISH_BULLET_SCALE;
                int style = f.makeStored(emitter, t).Style;
                float lean = DanmakusuBalance.KAGUYA_FALL_DEGREES * MathF.PI / 180f * (falls.Count % 2 == 0 ? 1f : -1f);
                Vector2 way = new Vector2(MathF.Sin(lean), MathF.Cos(lean));
                Vector2 start = new Vector2(to.X, f.stageMin.Y);

                falls.Add(new Fall { Time = t, Start = start, Way = way, Speed = speed, Style = style });

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = start,
                    V = way * speed,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = style,
                    Shape = BulletShape.Star,
                    Until = t + (f.stageMax.Y - f.stageMin.Y + radius) / (speed * way.Y),
                });

                return true;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                double over = beat * DanmakusuBalance.KAGUYA_MOON_LEAVE_BEATS;
                float level = smoothStep((t - Start) / over) * (1f - smoothStep((t - End) / over));

                if (level <= 0.002f)
                    return;

                if (f.hasScreenBounds)
                {
                    f.kaguyaBackground.Position = f.screenMin;
                    f.kaguyaBackground.Size = f.screenMax - f.screenMin;
                }

                f.kaguyaBackground.Alpha = Math.Max(f.kaguyaBackground.Alpha, level * DanmakusuBalance.KAGUYA_BACKGROUND_ALPHA);

                // (it comes down slowly from above the screen, and only its bottom is ever in it; it goes back up in the time the sky takes to clear)
                float radius = (f.stageMax.X - f.stageMin.X) * DanmakusuBalance.KAGUYA_MOON_SIZE;
                float shown = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.KAGUYA_MOON_SHOWN;
                float down = smoothStep((t - Start) / (beat * DanmakusuBalance.KAGUYA_MOON_BEATS)) * (1f - smoothStep((t - End) / over));
                Vector2 centre = new Vector2((f.stageMin.X + f.stageMax.X) / 2f, f.stageMin.Y - radius + shown * down);

                f.stageBack.Add(StageTexture.Glow, centre, new Vector2(radius * 3.2f), 0f, level * 0.8f, glow, true);

                // (only its bottom shows: that part is a picture of its own, sharp at this size; the whole moon only if the screen is so tall that more of it shows)
                if (shown <= radius * moon_cap_depth)
                {
                    var capCentre = centre + new Vector2(0f, radius * (1f - moon_cap_depth / 2f));
                    f.stageBack.Add(StageTexture.KaguyaMoonCap, capCentre, new Vector2(radius * 2f * moon_cap_half_width, radius * moon_cap_depth), 0f, level, Color4.White);
                }
                else
                    f.stageBack.Add(StageTexture.KaguyaMoon, centre, new Vector2(radius * 2f), 0f, level, Color4.White);

                f.stageBack.Add(StageTexture.Glow, centre, new Vector2(radius * 2.4f), 0f, level * 0.35f, glow, true);
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                float half = width(f);

                for (int i = firstFall; i < falls.Count; i++)
                {
                    var fall = falls[i];
                    Vector2 from = tail(fall, t), to = head(fall, t);

                    if (t < fall.Time || (to - from).LengthSquared < 1f)
                        continue;

                    line.Clear();
                    line.Add(from);
                    line.Add(to);

                    // (it narrows away to a point at its old end, as Byakuren's do)
                    widths.Clear();
                    widths.Add(0.5f);
                    widths.Add(half);

                    f.addPlainBeam(line, half, f.palette[fall.Style], f.borderPalette[fall.Style % f.borderPalette.Length], ref beams, ref pointTotal, widths);
                }
            }

            /// <summary>The beams the stars leave hurt like a slider body, as wide as they are at that place.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float half = width(f) * DanmakusuBalance.BULLET_HITBOX_SCALE;
                Vector2 player = f.characterPosition;

                for (int i = firstFall; i < falls.Count; i++)
                {
                    var fall = falls[i];

                    if (now < fall.Time)
                        continue;

                    Vector2 from = tail(fall, now), to = head(fall, now);
                    float length = (to - from).Length;

                    if (length < 1f)
                        continue;

                    float along = Math.Clamp(Vector2.Dot(player - from, fall.Way) / length, 0f, 1f);

                    if (distanceToSegment(player, from, to) <= half * along + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }
        }
    }
}
