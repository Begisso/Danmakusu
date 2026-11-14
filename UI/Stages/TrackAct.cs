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
        // Satori Komeiji: every two beats she reads which way the player has moved: from where they were two beats ago to where they are now. Three eyes open round the player before it, evenly, the first in front (along
        // that way), each over a cone of 90 degrees (its tip is the player, it reaches five osu! circle radii). The bullets that are inside one of the cones turn to fly perpendicular to the line that the player traced (the line
        // through them along that way), towards it, from the side it is on, smoothly and slowly (the speed stays): a bullet becomes a bullet of the act that turns at a steady rate and then flies straight on. What she
        // turns is red.
        // Besides (2026-11-13), the player leaves a red beam where they go, all through her section: every slice of it is see-through and harmless for four beats, then hurts for eight, fading out over the last
        // two (once half faded it no longer hurts). Standing still, the beam under the player comes to hurt after four beats. Where they are is taken at the moments of a grid, a sixteenth of a beat apart
        // (between the frames round it), so a replay lays the same beam.
        // ======================================================================================================================================================

        private sealed class TrackAct : StageAct
        {
            private readonly BulletField field;
            private readonly double beat;
            private readonly int marks;
            private readonly Vector2[] player;
            private readonly bool[] have;
            private readonly bool[] known;
            private readonly float cosHalf;

            // (the trail: every point where the player was, oldest first, and whether it is joined to the one before)
            private struct TrailPoint
            {
                public Vector2 Position;
                public double Time;
                public bool Joined;
            }

            private readonly List<TrailPoint> trail = new List<TrailPoint>();
            private readonly double trailBeat;
            private double trailNext = double.NaN; // (the next moment of the grid a point is laid at)
            private readonly MomentShapes shapes = new MomentShapes(); // (the trail at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> trailPoints = new List<Vector2>();
            private readonly List<float> trailFades = new List<float>();

            public TrackAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                trailBeat = Math.Max(period.BeatLength, 1);
                beat = Math.Max(period.BeatLength, 1) * DanmakusuBalance.SATORI_EVERY_BEATS; // (the time between two readings)
                marks = Math.Max((int)Math.Ceiling((end - start) / beat - 1e-9), 0);
                player = new Vector2[marks];
                have = new bool[marks];
                known = new bool[marks];
                cosHalf = MathF.Cos(DanmakusuBalance.SATORI_CONE_DEGREES * 0.5f * MathF.PI / 180f);
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            private int markAt(double t) => (int)Math.Floor((t - Start) / beat + 1e-9);

            /// <summary>The way the player moved at mark <paramref name="k"/>, if they did (a unit vector).</summary>
            private bool direction(int k, out Vector2 way)
            {
                way = default;

                if (k < 1 || k >= marks || !have[k] || !have[k - 1])
                    return false;

                Vector2 moved = player[k] - player[k - 1];

                if (moved.Length < DanmakusuBalance.SATORI_MIN_MOVE_PX)
                    return false;

                way = Vector2.Normalize(moved);
                return true;
            }

            /// <summary>The way eye <paramref name="eye"/> looks when the player moved <paramref name="way"/>: the first along it, the others evenly round.</summary>
            private static Vector2 eyeWay(Vector2 way, int eye)
            {
                float a = eye * MathF.Tau / DanmakusuBalance.SATORI_EYES;
                float cos = MathF.Cos(a), sin = MathF.Sin(a);
                return new Vector2(way.X * cos - way.Y * sin, way.X * sin + way.Y * cos);
            }

            /// <summary>Is the way <paramref name="direction"/> (a unit vector from the player) inside the cone of one of the eyes?</summary>
            private bool inACone(Vector2 direction, Vector2 way)
            {
                for (int eye = 0; eye < DanmakusuBalance.SATORI_EYES; eye++)
                {
                    if (Vector2.Dot(direction, eyeWay(way, eye)) >= cosHalf)
                        return true;
                }

                return false;
            }

            public override bool Absorbs(double t)
            {
                int k = markAt(t);
                return k >= 0 && k < marks && t - (Start + k * beat) < DanmakusuBalance.SATORI_WINDOW_MS;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                int k = markAt(to);

                if (k < 0 || k >= marks)
                    return false;

                return (Start + k * beat) + DanmakusuBalance.SATORI_WINDOW_MS >= from;
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                Array.Clear(have, 0, have.Length);
                ClearBullets();
                trail.Clear();
                trailNext = double.NaN;
                shapes.Forget();
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the trail moves on)

                if (f.failed)
                    return;

                layTrail(f, now);

                if (marks == 0)
                    return;

                int k = markAt(now);

                if (k < 0)
                    return;

                // where the player is at the moment each mark comes (taken in the first frame after it; a mark that was skipped by a seek has no place)
                for (int i = Math.Max(k - 2, 0); i <= Math.Min(k, marks - 1); i++)
                {
                    if (known[i] || now < Start + i * beat)
                        continue;

                    known[i] = true;

                    if (now - (Start + i * beat) < 250)
                    {
                        player[i] = f.characterPosition;
                        have[i] = true;
                    }
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                int k = markAt(t);

                if (k < 1 || k >= marks || t - (Start + k * beat) >= DanmakusuBalance.SATORI_WINDOW_MS || !direction(k, out Vector2 way))
                    return false;

                Vector2 apex = player[k];
                Vector2 rel = to - apex;
                float reach = f.circleRadius * DanmakusuBalance.SATORI_REACH_CIRCLES;
                float length = rel.Length;

                if (length > reach || (length > 1f && !inACone(rel / length, way)))
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                Vector2 velocity = Vector2.Normalize(delta) * speed;
                float now = MathF.Atan2(velocity.Y, velocity.X);
                // (it turns to fly straight at the line the player traced - the line through the player along the way they moved: perpendicular to it, from the side it is on)
                Vector2 across = new Vector2(-way.Y, way.X);
                float side = way.X * rel.Y - way.Y * rel.X;
                Vector2 inwards = side > 0f ? -across : across;
                float wanted = MathF.Atan2(inwards.Y, inwards.X);
                float turn = wanted - now;

                while (turn > MathF.PI)
                    turn -= MathF.Tau;

                while (turn < -MathF.PI)
                    turn += MathF.Tau;

                if (MathF.Abs(turn) < 0.02f)
                    return false; // (it flies that way already)

                var b = f.makeStageBullet(stored, emitter, bullet, t, to, velocity);
                b.TurnT = DanmakusuBalance.SATORI_TURN_MS;
                b.TurnOmega = turn / (float)DanmakusuBalance.SATORI_TURN_MS;
                b.Tint = BulletRenderer.TINT_RED; // (what she has read turns red)
                b.TintAmount = 1f;

                int index = Bullets.Count;
                AddBullet(b);
                return Bullets.Count > index;
            }

            /// <summary>
            /// Her third eyes round the player, the first in front (along the way they have moved since the last reading, which is where the first cone of the next one will point): they fade in over the two beats before
            /// every reading, are fully open at the reading and fade out quickly after it. There are none while the player has not moved enough (no bullet would turn).
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || marks == 0 || t < Start || t >= End)
                    return;

                int k = markAt(t);

                // (just after a reading: the eye of that reading closes, where it was)
                double since = t - (Start + k * beat);

                if (k >= 1 && k < marks && since < DanmakusuBalance.SATORI_EYE_FADE_OUT_MS && direction(k, out Vector2 read))
                    drawEye(f, player[k], read, DanmakusuBalance.SATORI_EYE_ALPHA * (1f - (float)(since / DanmakusuBalance.SATORI_EYE_FADE_OUT_MS)));

                // (before the next reading: it opens in front of where the player is going)
                int next = k + 1;

                if (next < 1 || next >= marks || !have[k])
                    return;

                Vector2 moved = f.characterPosition - player[k];

                if (moved.Length < DanmakusuBalance.SATORI_MIN_MOVE_PX)
                    return;

                float open = smoothStep(since / beat);
                drawEye(f, f.characterPosition, Vector2.Normalize(moved), DanmakusuBalance.SATORI_EYE_ALPHA * open);
            }

            // ---- the trail ----

            private double trailHarmless => trailBeat * DanmakusuBalance.SATORI_TRAIL_HARMLESS_BEATS;
            private double trailLife => trailBeat * (DanmakusuBalance.SATORI_TRAIL_HARMLESS_BEATS + DanmakusuBalance.SATORI_TRAIL_ACTIVE_BEATS);
            private float trailHalfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.SATORI_TRAIL_WIDTH;

            /// <summary>
            /// How visible a slice laid <paramref name="age"/> ms ago is: see-through while it is harmless, coming in over 250 ms when it starts to hurt (as Junko's), and fading out over the last beats of its
            /// time; 0 before it is laid and once it is gone.
            /// </summary>
            private float trailVisibility(double age)
            {
                if (age < 0 || age >= trailLife)
                    return 0f;

                if (age < trailHarmless)
                    return DanmakusuBalance.SATORI_TRAIL_GHOST_ALPHA;

                double fade = trailBeat * DanmakusuBalance.SATORI_TRAIL_FADE_BEATS;

                if (age > trailLife - fade)
                    return (float)((trailLife - age) / fade);

                float u = (float)Math.Min((age - trailHarmless) / 250.0, 1.0);
                return DanmakusuBalance.SATORI_TRAIL_GHOST_ALPHA + (1f - DanmakusuBalance.SATORI_TRAIL_GHOST_ALPHA) * u * u * (3f - 2f * u);
            }

            /// <summary>Does a slice laid <paramref name="age"/> ms ago hurt? From four beats on, until it has faded halfway.</summary>
            private bool trailHurts(double age) => age >= trailHarmless && age < trailLife - trailBeat * DanmakusuBalance.SATORI_TRAIL_FADE_BEATS / 2;

            /// <summary>
            /// Lays the points of the trail whose moments of the grid have come since the last frame, where the player was then (between where they were the frame before and now); forgets the ones whose time is
            /// over. Only within her section; after a seek or a long pause it starts again from where the player is (not joined).
            /// </summary>
            private void layTrail(BulletField f, double now)
            {
                int gone = 0;

                while (gone < trail.Count && now - trail[gone].Time >= trailLife)
                    gone++;

                if (gone > 0)
                    trail.RemoveRange(0, gone);

                if (now < Start)
                    return;

                double step = trailBeat * DanmakusuBalance.SATORI_TRAIL_STEP_BEATS;
                bool broken = false;

                if (double.IsNaN(trailNext) || now - trailNext > 1000)
                {
                    trailNext = Start + Math.Floor((now - Start) / step) * step;
                    broken = true;
                }

                double before = f.previousTime;
                bool between = now > before && now - before <= 250;

                while (trailNext <= now && trailNext < End)
                {
                    Vector2 at = between && trailNext > before ? Vector2.Lerp(f.previousCharacterPosition, f.characterPosition, (float)((trailNext - before) / (now - before))) : f.characterPosition;
                    bool joined = !broken && trail.Count > 0 && Vector2.Distance(trail[^1].Position, at) <= DanmakusuBalance.SATORI_TRAIL_JUMP_PX;

                    trail.Add(new TrailPoint { Position = at, Time = trailNext, Joined = joined });
                    broken = false;
                    trailNext += step;
                }
            }

            /// <summary>The trail hurts like a beam where it hurts: a run of joined slices that hurt, the first as a point, the others along the way from the one before.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || trail.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = trailHalfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the trail of that moment is worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    foreach (var point in trail)
                    {
                        bool hurts = trailHurts(now - point.Time);

                        if (!hurts || !point.Joined)
                            fresh.EndRun();

                        if (hurts)
                            fresh.Point(point.Position, reach);
                    }

                    fresh.EndRun();
                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                bool before = false;

                for (int i = 0; i < trail.Count; i++)
                {
                    var point = trail[i];
                    bool hurts = trailHurts(now - point.Time);

                    if (hurts && (before && point.Joined ? distanceToSegment(player, trail[i - 1].Position, point.Position) <= reach : Vector2.Distance(player, point.Position) <= reach))
                        return true;

                    before = hurts;
                }

                return false;
            }

            /// <summary>The trail, red, see-through where it does not hurt yet and fading out at its end; the newest part runs on to where the player is.</summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || trail.Count == 0)
                    return;

                float halfWidth = trailHalfWidth(f);
                var red = new Color4(0.95f, 0.1f, 0.16f, 1f);
                var border = DanmakusuBalance.BorderColour(red);

                trailPoints.Clear();
                trailFades.Clear();

                for (int i = 0; i < trail.Count; i++)
                {
                    var point = trail[i];
                    float visible = trailVisibility(t - point.Time);

                    if (visible <= 0f || !point.Joined)
                        flushTrail(f, halfWidth, red, border, ref beams, ref pointTotal);

                    if (visible <= 0f)
                        continue;

                    // (standing still lays many points in one place: one is enough to draw - the oldest, which hurts first)
                    if (trailPoints.Count > 0 && Vector2.DistanceSquared(trailPoints[^1], point.Position) < 0.25f)
                        continue;

                    trailPoints.Add(point.Position);
                    trailFades.Add(visible);
                }

                // (on to where the player is now: the beam they are laying)
                if (t >= Start && t < End && trailPoints.Count > 0 && Vector2.Distance(trail[^1].Position, f.characterPosition) <= DanmakusuBalance.SATORI_TRAIL_JUMP_PX
                    && Vector2.DistanceSquared(trailPoints[^1], f.characterPosition) >= 0.25f)
                {
                    trailPoints.Add(f.characterPosition);
                    trailFades.Add(DanmakusuBalance.SATORI_TRAIL_GHOST_ALPHA);
                }

                flushTrail(f, halfWidth, red, border, ref beams, ref pointTotal);
            }

            private void flushTrail(BulletField f, float halfWidth, Color4 colour, Color4 border, ref int beams, ref int pointTotal)
            {
                if (trailPoints.Count >= 2)
                    f.addPlainBeam(trailPoints, halfWidth, colour, border, ref beams, ref pointTotal, null, trailFades);

                trailPoints.Clear();
                trailFades.Clear();
            }

            private static void drawEye(BulletField f, Vector2 at, Vector2 way, float alpha)
            {
                if (alpha <= 0.005f)
                    return;

                float size = DanmakusuBalance.SATORI_EYE_SIZE * f.circleRadius;

                for (int eye = 0; eye < DanmakusuBalance.SATORI_EYES; eye++)
                {
                    Vector2 looks = eyeWay(way, eye);
                    Vector2 centre = at + looks * (DanmakusuBalance.SATORI_EYE_DISTANCE * f.circleRadius);

                    f.stageBack.Add(StageTexture.Glow, centre, new Vector2(size * 1.6f), 0f, alpha * 0.5f, new Color4(1f, 0.45f, 0.7f, 1f), true);
                    f.stageBack.Add(StageTexture.Eye, centre, new Vector2(size), MathF.Atan2(looks.Y, looks.X) * 180f / MathF.PI, alpha, new Color4(1f, 0.55f, 0.8f, 1f));
                }
            }
        }
    }
}
