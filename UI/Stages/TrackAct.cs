using System;
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
        // through them along that way), towards it, from the side it is on, smoothly and slowly (the speed stays): a bullet becomes a bullet of the act that turns at a steady rate and then flies straight on.
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

            public TrackAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
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
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || marks == 0)
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
