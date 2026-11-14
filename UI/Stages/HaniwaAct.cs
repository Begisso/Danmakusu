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
        // Keiki Haniyasushin: in her section the rings come half as often with twice as many bullets, all of them squares, and she shoots very dense squares of plain bullets every four beats (the planner's).
        // Here: every fifth bullet that goes out of the screen in her section (anyone's; chosen by its ring's time and its place in the ring, as Seiga's) comes back the way it came as a beam: from where it
        // went out it runs back along the same line at the bullet's speed, growing until it is as long as KEIKI_BEAM_BEATS beats of that way, and goes on so until it has left the screen on the other side.
        // It hurts like a slider body all the way. (A straight line from the wall: where every beam is follows from the time alone once it is made.)
        // ======================================================================================================================================================

        private sealed class HaniwaAct : StageAct
        {
            private const int most_beams = 60000;

            private struct Beam
            {
                public Vector2 Wall; // (where the bullet went out)
                public Vector2 Way; // (back the way it came)
                public float Speed; // (pixels a ms)
                public double Crossed;
                public float Across; // (from the wall to the other side of the screen, along the way back)
                public double Gone; // (its back end has left the screen)
            }

            private readonly double beat;
            private readonly double lengthMs;

            // (in the order they went out; the ones before firstAlive are gone)
            private readonly List<Beam> beams = new List<Beam>();
            private int firstAlive;

            private readonly MomentShapes shapes = new MomentShapes(); // (the beams at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> line = new List<Vector2>(2);

            public HaniwaAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                lengthMs = beat * DanmakusuBalance.KEIKI_BEAM_BEATS;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            /// <summary>Time went back: the beams of the bullets that went out after <paramref name="time"/> are not there yet.</summary>
            public override void Reset(BulletField f, double time)
            {
                beams.RemoveAll(beam => beam.Crossed > time);
                firstAlive = 0;
                shapes.Forget();
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the beams move on)

                while (firstAlive < beams.Count && beams[firstAlive].Gone < now)
                    firstAlive++;
            }

            private static bool outside(BulletField f, Vector2 p) => p.X < f.stageMin.X || p.X > f.stageMax.X || p.Y < f.stageMin.Y || p.Y > f.stageMax.Y;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train || beams.Count >= most_beams)
                    return false;

                if (!outside(f, to) || outside(f, from))
                    return false;

                if ((Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.KEIKI_RETURN_EVERY != 0)
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = Math.Max(seenSpeed(stored, emitter, from, to, ScanPrevious, t), 0.02f);
                Vector2 direction = Vector2.Normalize(delta);

                // where (and when) it went through the wall: the first wall it got past
                Vector2 low = f.stageMin, high = f.stageMax;
                float s = 1f;

                if (to.X < low.X && Math.Abs(delta.X) > 1e-6f)
                    s = Math.Min(s, (low.X - from.X) / delta.X);

                if (to.X > high.X && Math.Abs(delta.X) > 1e-6f)
                    s = Math.Min(s, (high.X - from.X) / delta.X);

                if (to.Y < low.Y && Math.Abs(delta.Y) > 1e-6f)
                    s = Math.Min(s, (low.Y - from.Y) / delta.Y);

                if (to.Y > high.Y && Math.Abs(delta.Y) > 1e-6f)
                    s = Math.Min(s, (high.Y - from.Y) / delta.Y);

                s = Math.Clamp(s, 0f, 1f);
                Vector2 wall = from + delta * s;
                double crossed = t - (1f - s) * Math.Clamp(t - ScanPrevious, 0, 50);
                Vector2 way = -direction;

                // (how far back across the screen it goes before it is out on the other side)
                float across = float.PositiveInfinity;

                if (way.X > 1e-6f)
                    across = Math.Min(across, (high.X - wall.X) / way.X);
                else if (way.X < -1e-6f)
                    across = Math.Min(across, (low.X - wall.X) / way.X);

                if (way.Y > 1e-6f)
                    across = Math.Min(across, (high.Y - wall.Y) / way.Y);
                else if (way.Y < -1e-6f)
                    across = Math.Min(across, (low.Y - wall.Y) / way.Y);

                if (float.IsInfinity(across) || across < 0f)
                    across = 0f;

                across += halfWidth(f);

                var beam = new Beam
                {
                    Wall = wall,
                    Way = way,
                    Speed = speed,
                    Crossed = crossed,
                    Across = across,
                    Gone = crossed + across / speed + lengthMs,
                };

                // (kept in the order they went out: almost always the last)
                int at = beams.Count;

                while (at > 0 && beams[at - 1].Crossed > crossed)
                    at--;

                beams.Insert(at, beam);

                if (at < firstAlive)
                    firstAlive = at;

                shapes.Forget();
                return true;
            }

            /// <summary>Where a beam is at <paramref name="t"/>: its back end and its front end (the front runs ahead from the wall, the back follows four beats behind; both stop just past the other side).</summary>
            private (Vector2 Back, Vector2 Front) span(in Beam beam, double t)
            {
                float run = beam.Speed * (float)(t - beam.Crossed);
                float front = Math.Min(run, beam.Across);
                float back = Math.Clamp(run - beam.Speed * (float)lengthMs, 0f, beam.Across);
                return (beam.Wall + beam.Way * back, beam.Wall + beam.Way * front);
            }

            public override void DrawBeams(BulletField f, double t, ref int drawn, ref int pointTotal)
            {
                if (f.failed || beams.Count == 0)
                    return;

                var core = new Color4(0.94f, 0.98f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(58 / 255f, 154 / 255f, 216 / 255f, 1f));
                float half = halfWidth(f);

                for (int k = firstAlive; k < beams.Count && beams[k].Crossed <= t; k++)
                {
                    var beam = beams[k];

                    if (beam.Gone < t)
                        continue;

                    var (back, front) = span(beam, t);

                    if ((front - back).LengthSquared < 0.25f)
                        continue;

                    line.Clear();
                    line.Add(back);
                    line.Add(front);
                    f.addPlainBeam(line, half, core, border, ref drawn, ref pointTotal);
                }
            }

            /// <summary>The beams hurt like a slider body all the way.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || beams.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the beams of that moment are worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    for (int k = firstAlive; k < beams.Count && beams[k].Crossed <= now; k++)
                    {
                        if (beams[k].Gone < now)
                            continue;

                        var (back, front) = span(beams[k], now);
                        fresh.Point(back, reach);
                        fresh.Point(front, reach);
                        fresh.EndRun();
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                for (int k = firstAlive; k < beams.Count && beams[k].Crossed <= now; k++)
                {
                    if (beams[k].Gone < now)
                        continue;

                    var (back, front) = span(beams[k], now);

                    if (distanceToSegment(player, back, front) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
