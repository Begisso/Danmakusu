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
        // Kisume (2026-11-27): all through her section she stays at the top of the screen (her magic circle's edge on it) and goes along it over the player at KISUME_MOVE_SPEED times a bullet's speed. Every
        // KISUME_EVERY_BEATS she stops for KISUME_STOP_BEATS: three thick beams down from her - the middle one straight down, the others KISUME_BEAM_FAN_DEGREES left and right of it (side by side until the same day) - KISUME_BEAM_HALF_CIRCLES circle radii half wide, show see-through for
        // KISUME_PREVIEW_BEATS, then are solid for the rest of the stop with Byakuren's beam sound, hurting like a slider body, and go. Everything fired in her section comes from the top and falls
        // (BossOps: TrajectoryOpKind.FromTop). How far she has gone is worked out frame by frame (the player is where they are then); a seek back starts her again over the player.
        // ======================================================================================================================================================

        private sealed class BucketAct : StageAct
        {
            private readonly double beat;
            private readonly double[] stops;
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);
            private double simTime = double.NaN;
            private float x;
            private int heard = -1;
            private double resetTime = double.NegativeInfinity;

            public BucketAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.KISUME_EVERY_BEATS;

                for (double t = period.ShootStart + every - beat * DanmakusuBalance.KISUME_STOP_BEATS; t < period.End && list.Count < 20000; t += every)
                    list.Add(t);

                stops = list.ToArray();
                Start = period.Start;
                End = period.End + beat * 2;
            }

            private double stopMs => beat * DanmakusuBalance.KISUME_STOP_BEATS;

            private double fireAt(int k) => stops[k] + beat * DanmakusuBalance.KISUME_PREVIEW_BEATS;

            /// <summary>The stop <paramref name="t"/> is in, or -1.</summary>
            private int stopAt(double t)
            {
                if (stops.Length == 0 || t < stops[0])
                    return -1;

                int k = Math.Min(stops.Length - 1, (int)((t - stops[0]) / (beat * DanmakusuBalance.KISUME_EVERY_BEATS)));
                return t < stops[k] + stopMs ? k : -1;
            }

            private static float top(BulletField f) => f.stageMin.Y + DanmakusuBalance.MAGIC_CIRCLE_RADIUS;

            public override void Reset(BulletField f, double time)
            {
                simTime = double.NaN;
                resetTime = time;
                heard = -1;
            }

            /// <summary>Goes along the top towards the player, up to <paramref name="now"/> (not in a stop).</summary>
            private void follow(BulletField f, double now)
            {
                if (double.IsNaN(simTime) || now < simTime - 1)
                {
                    x = Math.Clamp(f.characterPosition.X, f.stageMin.X, f.stageMax.X);
                    simTime = now;
                    return;
                }

                // (in a stop she does not move; a frame is short next to a stop)
                double moving = stopAt(now) >= 0 ? 0 : now - simTime;
                simTime = now;

                if (moving <= 0)
                    return;

                float speed = stageBulletSpeed(Period) * DanmakusuBalance.KISUME_MOVE_SPEED;
                float wanted = Math.Clamp(f.characterPosition.X, f.stageMin.X, f.stageMax.X);
                float most = speed * (float)moving;
                x += Math.Clamp(wanted - x, -most, most);
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                if (f.failed || now < Period.Start || now > Period.End)
                    return false;

                follow(f, now);
                position = new Vector2(x, top(f));
                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                int k = stopAt(now);

                // (Byakuren's beam sound as the beams turn solid)
                if (k >= 0 && k != heard && now >= fireAt(k))
                {
                    heard = k;

                    if (now - fireAt(k) < 250 && fireAt(k) > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.KISUME_BEAM_HALF_CIRCLES;

            /// <summary>Which way beam <paramref name="beam"/> (0..2) goes from her: the middle one straight down, the others KISUME_BEAM_FAN_DEGREES left and right of it (2026-11-27; side by side, KISUME_BEAM_GAP_CIRCLES apart, before).</summary>
            private static Vector2 beamWay(int beam)
            {
                float angle = (beam - 1) * DanmakusuBalance.KISUME_BEAM_FAN_DEGREES * MathF.PI / 180f;
                return new Vector2(-MathF.Sin(angle), MathF.Cos(angle));
            }

            private static float reach(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) + 100f;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                int k = stopAt(t);

                if (f.failed || k < 0 || double.IsNaN(simTime))
                    return;

                bool solid = t >= fireAt(k);
                double end = stops[k] + stopMs;
                float alpha = solid
                    ? 1f - smoothStep((t - (end - beat * 0.25)) / (beat * 0.25))
                    : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - stops[k]) / (beat * 0.25));

                if (alpha < 0.01f)
                    return;

                var core = new Color4(0.85f, 1f, 0.9f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.2f, 0.7f, 0.5f, 1f)); // (her green)
                float y = top(f);

                for (int beam = 0; beam < 3; beam++)
                {
                    line.Clear();
                    fades.Clear();
                    line.Add(new Vector2(x, y));
                    line.Add(new Vector2(x, y) + beamWay(beam) * reach(f));
                    fades.Add(alpha);
                    fades.Add(alpha);
                    f.addPlainBeam(line, halfWidth(f), core, border, ref beams, ref pointTotal, null, fades, null, solid ? 0.6f : 0f);
                }
            }

            /// <summary>The beams hurt like a slider body while they are solid.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                int k = stopAt(now);

                if (f.failed || k < 0 || now < fireAt(k) || double.IsNaN(simTime))
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                if (player.Y < top(f) - reach)
                    return false;

                Vector2 from = new Vector2(x, top(f));

                for (int beam = 0; beam < 3; beam++)
                {
                    if (distanceToSegment(player, from, from + beamWay(beam) * BucketAct.reach(f)) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
