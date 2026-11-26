using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Orange (2026-11-25): she stands in the middle of the screen. In her warning a big orange - the user's orange.png, ORANGE_RADIUS_CIRCLES circle radii from its middle to its rind ("4x osu
        // circle"), in ORANGE_SLICES slices - grows there, see-through (over ORANGE_GROW_BEATS when there is no warning), and stands ORANGE_HOLD_BEATS; then it splits: its slices fly straight out from
        // the middle, each its own way, out past the farthest corner of the screen (ORANGE_BEYOND_CIRCLES more), and come back the same way, over ORANGE_FLIGHT_BEATS (fast as they go, slow far out),
        // meet as the orange again and fly out again at once, and so on. While the first one's slices fly out a second orange grows in the middle over half a flight; it splits as they turn back, so
        // from then on one orange's slices come back as the other's go, and an orange is whole in the middle every half flight. For ORANGE_WARN_BEATS before every split a red arrow on every slice points
        // the way it will fly. The slices (and a whole orange) hurt like a slider body; one that is growing does not. When her section is over the slices go on out and are gone.
        // Everything is a closed form of time.
        // ======================================================================================================================================================

        private sealed class CitrusAct : StageAct
        {
            private readonly double beat;
            private readonly double grownAt; // (the first orange has grown)
            private readonly double firstSplit;
            private readonly double ends; // (her section is over: what flies out goes on out)

            public CitrusAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                // (it grows in her warning; without one, over ORANGE_GROW_BEATS as she starts)
                double growFrom = period.Start < period.ShootStart - beat ? period.Start : period.ShootStart;
                grownAt = Math.Max(period.ShootStart, growFrom + beat * DanmakusuBalance.ORANGE_GROW_BEATS);
                firstSplit = grownAt + beat * DanmakusuBalance.ORANGE_HOLD_BEATS;
                ends = period.End;

                Start = growFrom;
                End = period.End + flightMs;
            }

            private double flightMs => beat * DanmakusuBalance.ORANGE_FLIGHT_BEATS;

            /// <summary>It stays until what was flying out when her section ended has left the screen.</summary>
            public override double MechanicsEnd => End;

            private float radius(BulletField f) => f.circleRadius * DanmakusuBalance.ORANGE_RADIUS_CIRCLES;

            private static Vector2 middle(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            /// <summary>How far out a slice flies: past the farthest corner of the screen.</summary>
            private float farOut(BulletField f)
            {
                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                return Vector2.Distance(low, high) * 0.5f + radius(f) + f.circleRadius * DanmakusuBalance.ORANGE_BEYOND_CIRCLES;
            }

            /// <summary>The way slice <paramref name="s"/> points and flies (its middle): straight up for the first, then round.</summary>
            private static Vector2 wayOf(int s)
            {
                float angle = -MathF.PI / 2f + s * MathF.Tau / DanmakusuBalance.ORANGE_SLICES;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            /// <summary>
            /// Orange <paramref name="j"/> (0 the first, 1 the second) at <paramref name="t"/>: how far its slices are from the middle (px), how big it is (0..1, growing), whether it is there and
            /// whether it is solid. Each flies out and back over a flight from its first split on, again and again; after her section what is flying out goes on out, and what was coming back turns out.
            /// </summary>
            private bool stateOf(BulletField f, int j, double t, out float distance, out float size, out bool solid)
            {
                distance = 0f;
                size = 1f;
                solid = true;

                double split = j == 0 ? firstSplit : firstSplit + flightMs * 0.5;
                double growFrom = j == 0 ? Start : firstSplit;
                double grown = j == 0 ? grownAt : split;

                if (t < growFrom)
                    return false;

                float far = farOut(f);

                // (after her section: what had split goes on out from where it was, at the speed it had - what was coming back turns out - and is gone once out; what had not split yet goes with her)
                if (t > ends)
                {
                    if (split >= ends)
                        return false;

                    double atEnd = (ends - split) % flightMs;
                    float outAtEnd = far * MathF.Sin(MathF.PI * (float)(atEnd / flightMs));
                    float speed = far * MathF.PI / (float)flightMs * MathF.Abs(MathF.Cos(MathF.PI * (float)(atEnd / flightMs)));
                    distance = outAtEnd + Math.Max(speed, far / (float)flightMs) * (float)(t - ends);
                    return distance < far * 1.5f;
                }

                if (t < grown)
                {
                    size = smoothStep((t - growFrom) / Math.Max(grown - growFrom, 1));
                    solid = false;
                    return size > 0.01f;
                }

                if (t < split)
                    return true;

                distance = far * MathF.Sin(MathF.PI * (float)((t - split) % flightMs / flightMs));
                return true;
            }

            /// <summary>The split of either orange that comes next after <paramref name="t"/> (or NaN).</summary>
            private double nextSplit(double t)
            {
                double half = flightMs * 0.5;

                if (t < firstSplit)
                    return firstSplit;

                double k = Math.Ceiling((t - firstSplit) / half);
                double at = firstSplit + k * half;
                return at < ends ? at : double.NaN;
            }

            /// <summary>Which orange splits at <paramref name="at"/> (a time of <see cref="nextSplit"/>).</summary>
            private int splitting(double at) => (int)Math.Round((at - firstSplit) / (flightMs * 0.5)) % 2;

            /// <summary>She stands in the middle.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = middle(f);
                return now >= Start && now <= ends;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End)
                    return;

                Vector2 centre = middle(f);
                float r = radius(f);
                float picture = r * 2f; // (the picture is the whole orange's square)

                for (int j = 0; j < 2; j++)
                {
                    if (!stateOf(f, j, t, out float distance, out float size, out bool solid))
                        continue;

                    float alpha = solid ? 1f : DanmakusuBalance.ORANGE_PREVIEW_ALPHA * size + (1f - DanmakusuBalance.ORANGE_PREVIEW_ALPHA) * smoothStep((size - 0.9f) / 0.1f);

                    for (int s = 0; s < DanmakusuBalance.ORANGE_SLICES; s++)
                    {
                        float degrees = s * 360f / DanmakusuBalance.ORANGE_SLICES;
                        f.stageBack.Add(StageTexture.OrangeSlice, centre + wayOf(s) * distance, new Vector2(picture * size), degrees, alpha, Color4.White);
                    }
                }

                // (the warning of the next split: a red arrow on every slice of the orange about to split, pointing the way it will fly, blinking faster as it comes)
                double next = nextSplit(t);

                if (!double.IsNaN(next) && next - t < beat * DanmakusuBalance.ORANGE_WARN_BEATS)
                {
                    int j = splitting(next);

                    if (stateOf(f, j, t, out float distance, out _, out _))
                    {
                        double left = (next - t) / (beat * DanmakusuBalance.ORANGE_WARN_BEATS);
                        float level = smoothStep((1 - left) / 0.25) * (0.6f + 0.4f * MathF.Cos((float)(1 - left) * MathF.PI * 6f));
                        float arrow = f.circleRadius * 1.2f;
                        var red = new Color4(1f, 0.2f, 0.15f, 1f);

                        for (int s = 0; s < DanmakusuBalance.ORANGE_SLICES; s++)
                        {
                            Vector2 way = wayOf(s);
                            f.stageRipple.Add(StageTexture.SeijaArrow, centre + way * (distance + r * 0.62f), new Vector2(arrow * 2.2f), MathF.Atan2(way.Y, way.X) * 180f / MathF.PI, level, red);
                        }
                    }
                }
            }

            /// <summary>A slice hurts like a slider body (its wedge, as far out as the rind; a growing orange does not).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start || now > End)
                    return false;

                Vector2 player = f.characterPosition, centre = middle(f);
                float r = radius(f), margin = DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float half = MathF.PI / DanmakusuBalance.ORANGE_SLICES;

                for (int j = 0; j < 2; j++)
                {
                    if (!stateOf(f, j, now, out float distance, out _, out bool solid) || !solid)
                        continue;

                    for (int s = 0; s < DanmakusuBalance.ORANGE_SLICES; s++)
                    {
                        Vector2 way = wayOf(s);
                        Vector2 apex = centre + way * distance;
                        Vector2 to = player - apex;
                        float length = to.Length;

                        if (length > r + margin)
                            continue;

                        if (length <= margin)
                            return true;

                        // (within the wedge's angle, or within the hitbox of its sides)
                        float off = MathF.Acos(Math.Clamp(Vector2.Dot(to / length, way), -1f, 1f));

                        if (off <= half || length * MathF.Sin(off - half) <= margin)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
