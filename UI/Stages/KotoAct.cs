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
        // Yatsuhashi Tsukumo: no pauses. Her section is an 18-key osu!mania convert of the map (osu!mania's own converter, see ManiaNotes; the planner keeps the notes of her section in
        // BossPeriod.FallingNotes). The field (the whole screen, where the player can go) is cut into YATSUHASHI_COLUMNS lanes; every note is a beam YATSUHASHI_BEAM_WIDTH times as thick as a normal one that falls straight down its lane from the top
        // of the screen, its front reaching the bottom on the note, in YATSUHASHI_FALL_BEATS beats from top to bottom (osu!mania's notes come down to the hit line the same way). A tap is a short beam (as long
        // as YATSUHASHI_TAP_BEATS of its fall), a held note one as long as it is held. They hurt like a slider body all the way down. Before a beam starts falling, an approach circle closes onto the top of the
        // screen where it will come in, over the map's approach time. The map's own circles and sliders stay, and shoot as always.
        // (Nothing in it changes at runtime: where every beam is comes from the time alone, so the autoplay sees them all coming.)
        // ======================================================================================================================================================

        private sealed class KotoAct : StageAct
        {
            private readonly double beat;
            private readonly double fall;
            private readonly double preempt;
            private readonly int columns = DanmakusuBalance.YATSUHASHI_COLUMNS;

            // (each lane's notes in time order, and the latest end so far, to find the first one not over yet)
            private readonly (double Start, double End)[][] lanes;
            private readonly double[][] endsSoFar;
            private readonly List<Vector2> line = new List<Vector2>(2);

            public KotoAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                fall = beat * DanmakusuBalance.YATSUHASHI_FALL_BEATS;
                preempt = period.Preempt > 1 ? period.Preempt : 1200; // (the map's approach time)
                Start = period.ShootStart;
                End = period.End;

                double tap = beat * DanmakusuBalance.YATSUHASHI_TAP_BEATS;
                var perLane = new List<(double Start, double End)>[columns];

                for (int c = 0; c < columns; c++)
                    perLane[c] = new List<(double Start, double End)>();

                foreach (var (column, start, end) in period.FallingNotes)
                    perLane[Math.Clamp(column, 0, columns - 1)].Add((start, Math.Max(end, start + tap)));

                lanes = new (double Start, double End)[columns][];
                endsSoFar = new double[columns][];

                for (int c = 0; c < columns; c++)
                {
                    perLane[c].Sort((a, b) => a.Start.CompareTo(b.Start));
                    lanes[c] = perLane[c].ToArray();
                    endsSoFar[c] = new double[lanes[c].Length];
                    double latest = double.MinValue;

                    for (int k = 0; k < lanes[c].Length; k++)
                        endsSoFar[c][k] = latest = Math.Max(latest, lanes[c][k].End);
                }
            }

            public override void Reset(BulletField f, double time)
            {
            }

            // ---- the approach circles ----

            /// <summary>The first note of a lane that comes after <paramref name="time"/>.</summary>
            private int firstAfter(int column, double time)
            {
                var lane = lanes[column];
                int low = 0, high = lane.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (lane[middle].Start <= time)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            /// <summary>
            /// Before every beam starts falling an approach circle closes onto the top of the screen where it will come in (over its lane), as osu!'s approach circles do: over the map's approach time, from
            /// YATSUHASHI_APPROACH_SCALE times its size (the map's circle, or half a lane if that is smaller) down to it, fading in first; it is gone as the beam comes in. (On the top layer, so nothing hides it.)
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start - fall - preempt || t > End)
                    return;

                float width = (f.stageMax.X - f.stageMin.X) / columns;
                float closed = Math.Min(f.circleRadius, width * 0.5f);
                float lineHalf = Math.Max(1f, closed * DanmakusuBalance.YATSUHASHI_APPROACH_LINE);
                double fadeIn = Math.Min(400, preempt * 2.0 / 3.0);
                float y = top(f) + closed;
                var colour = new Color4(0.86f, 0.74f, 1f, 1f);

                for (int c = 0; c < columns; c++)
                {
                    var lane = lanes[c];
                    float x = laneX(f, c);

                    // (the notes whose beams start falling in the next approach time)
                    for (int k = firstAfter(c, t + fall); k < lane.Length && lane[k].Start - fall - preempt <= t; k++)
                    {
                        double left = lane[k].Start - fall - t;
                        float radius = closed * (1f + (DanmakusuBalance.YATSUHASHI_APPROACH_SCALE - 1f) * (float)(left / preempt));
                        float alpha = (float)Math.Clamp((preempt - left) / fadeIn, 0, 1) * DanmakusuBalance.YATSUHASHI_APPROACH_ALPHA;
                        f.stageTop.AddRing(new Vector2(x, y), radius, lineHalf, alpha, colour);
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
            }

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.YATSUHASHI_BEAM_WIDTH;

            private float top(BulletField f) => f.hasScreenBounds ? f.screenMin.Y : f.stageMin.Y;

            private float bottom(BulletField f) => f.hasScreenBounds ? f.screenMax.Y : f.stageMax.Y;

            private float laneX(BulletField f, int column) => f.stageMin.X + (column + 0.5f) * (f.stageMax.X - f.stageMin.X) / columns;

            /// <summary>Where a note's beam is at <paramref name="t"/>: its front (lower end) and its back (upper end); the front reaches the bottom of the screen on the note.</summary>
            private (float Front, float Back) span(BulletField f, (double Start, double End) note, double t)
            {
                float speed = (float)((bottom(f) - top(f)) / fall);
                return (bottom(f) - (float)(note.Start - t) * speed, bottom(f) - (float)(note.End - t) * speed);
            }

            /// <summary>The first note of a lane whose beam has not gone past the bottom by <paramref name="t"/>.</summary>
            private int firstLeft(int column, double t)
            {
                double[] ends = endsSoFar[column];
                int low = 0, high = ends.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (ends[middle] < t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || t < Start - fall || t > End + fall * 4)
                    return;

                var core = new Color4(0.97f, 0.93f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.71f, 0.54f, 0.82f, 1f));
                float half = halfWidth(f);
                float upper = top(f) - half, lower = bottom(f) + half;

                for (int c = 0; c < columns; c++)
                {
                    var lane = lanes[c];
                    float x = laneX(f, c);

                    for (int k = firstLeft(c, t); k < lane.Length && lane[k].Start - fall <= t; k++)
                    {
                        var (front, back) = span(f, lane[k], t);
                        front = Math.Min(front, lower);
                        back = Math.Max(back, upper);

                        if (front <= back)
                            continue;

                        line.Clear();
                        line.Add(new Vector2(x, back));
                        line.Add(new Vector2(x, front));
                        f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal);
                    }
                }
            }

            /// <summary>A beam hurts like a slider body all the way down (only the lanes next to the player can).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start - fall || now > End + fall * 4)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;                float width = (f.stageMax.X - f.stageMin.X) / columns;
                int from = Math.Max(0, (int)MathF.Floor((player.X - reach - f.stageMin.X) / width));
                int to = Math.Min(columns - 1, (int)MathF.Floor((player.X + reach - f.stageMin.X) / width));

                for (int c = from; c <= to; c++)
                {
                    if (MathF.Abs(player.X - laneX(f, c)) > reach)
                        continue;

                    var lane = lanes[c];

                    for (int k = firstLeft(c, now); k < lane.Length && lane[k].Start - fall <= now; k++)
                    {
                        var (front, back) = span(f, lane[k], now);

                        if (player.Y <= front + reach && player.Y >= back - reach)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
