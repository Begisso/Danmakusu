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
        // Sara (2026-11-25): every SARA_EVERY_BEATS of her section two thick beams (SARA_BEAM_CIRCLES circle radii from their middle line to each side) from her, one to her left and one to her right - square
        // to the line from her to the player as it was when their preview came - after a see-through preview of SARA_PREVIEW_BEATS. They come at once, whole, out past the edge of the screen, with the Master
        // Spark's sound and a shake (SARA_SHAKE), and close in towards the player evenly over SARA_CLOSE_BEATS until they are SARA_CLOSED_DEGREES apart; then they fade out over SARA_FADE_BEATS, harmless
        // by then. They hurt like a slider body. She stands still from the preview until they have faded, and a pair always goes on to its end, also after her section.
        // That every bullet of her section flies twice as fast by her and slows to half its speed at the edge of the screen is the planner's (BulletEmitter.SlowsFromBoss), and so are her gapless rings.
        // Where she stands and where the player is are taken when a preview comes, and kept.
        // ======================================================================================================================================================

        private sealed class GateAct : StageAct
        {
            private readonly double beat;

            // (her pairs of beams: when each preview comes, and from where and at whom, taken then)
            private readonly double[] strikes;
            private readonly Vector2[] from;
            private readonly Vector2[] aim;
            private readonly bool[] known;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public GateAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.SARA_EVERY_BEATS;

                // (a pair whose preview is over in her section: it goes on to its end after it)
                for (double t = period.ShootStart; t + beat * DanmakusuBalance.SARA_PREVIEW_BEATS < period.End && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                from = new Vector2[strikes.Length];
                aim = new Vector2[strikes.Length];
                known = new bool[strikes.Length];

                Start = period.ShootStart;
                End = strikes.Length > 0 ? goneAt(strikes.Length - 1) : period.End;
            }

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.SARA_PREVIEW_BEATS;

            private double closedAt(int k) => solidAt(k) + beat * DanmakusuBalance.SARA_CLOSE_BEATS;

            private double goneAt(int k) => closedAt(k) + beat * DanmakusuBalance.SARA_FADE_BEATS;

            /// <summary>She stays until her last pair has faded.</summary>
            public override double MechanicsEnd => strikes.Length > 0 ? goneAt(strikes.Length - 1) : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.SARA_BEAM_CIRCLES;

            /// <summary>The pairs that can be there at <paramref name="t"/>: from the one whose time is not over yet to the last whose preview has come.</summary>
            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.SARA_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.SARA_PREVIEW_BEATS + DanmakusuBalance.SARA_CLOSE_BEATS + DanmakusuBalance.SARA_FADE_BEATS)) / every));
                return (first, last);
            }

            /// <summary>How far each beam of pair <paramref name="k"/> is turned away from the line at the player at <paramref name="t"/> (radians): square to it until it comes, then closing in evenly.</summary>
            private float openAt(int k, double t)
            {
                float open = MathF.PI / 2f;
                float closed = DanmakusuBalance.SARA_CLOSED_DEGREES * MathF.PI / 360f;
                float u = (float)Math.Clamp((t - solidAt(k)) / (beat * DanmakusuBalance.SARA_CLOSE_BEATS), 0, 1);
                return open + (closed - open) * u;
            }

            /// <summary>The way beam <paramref name="side"/> (-1 her left, 1 her right) of pair <paramref name="k"/> points at <paramref name="t"/>.</summary>
            private Vector2 wayOf(int k, int side, double t)
            {
                float angle = MathF.Atan2(aim[k].Y, aim[k].X) + side * openAt(k, t);
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every pair whose preview has come takes where she is and where the player is, once)
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] || strikes[k] > now)
                        continue;

                    known[k] = true;
                    from[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from[k];
                    aim[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                }
            }

            /// <summary>She stands where she was when a preview came, until that pair has faded.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                if (strikes.Length == 0 || now < Start)
                    return false;

                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && now >= strikes[k] && now < goneAt(k))
                    {
                        position = from[k];
                        return true;
                    }
                }

                return false;
            }

            /// <summary>The pair that is solid at <paramref name="t"/>, or -1.</summary>
            private int solidOne(double t)
            {
                if (strikes.Length == 0 || t < Start)
                    return -1;

                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && t >= solidAt(k) && t < closedAt(k))
                        return k;
                }

                return -1;
            }

            /// <summary>The Master Spark's sound plays while a pair is solid.</summary>
            public bool Firing(double now) => solidOne(now) >= 0;

            /// <summary>The screen shakes while a pair is solid.</summary>
            public float ShakeAt(double now) => solidOne(now) >= 0 ? DanmakusuBalance.SARA_SHAKE : 0f;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var core = new Color4(1f, 0.9f, 0.93f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.85f, 0.19f, 0.35f, 1f));
                float half = halfWidth(f);
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= goneAt(k))
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? smoothStep((t - solidAt(k)) / 40.0) * (1f - smoothStep((t - closedAt(k)) / (beat * DanmakusuBalance.SARA_FADE_BEATS)))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        line.Clear();
                        line.Add(from[k]);
                        line.Add(beamRayEnd(f, from[k], wayOf(k, side, t), half));
                        fades.Clear();
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, solid ? half : half * 0.7f, core, border, ref beams, ref pointTotal, null, fades);
                    }
                }
            }

            /// <summary>A solid pair hurts like a slider body (not while it fades).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                int k = solidOne(now);

                if (f.failed || k < 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int side = -1; side <= 1; side += 2)
                {
                    if (distanceToSegment(player, from[k], beamRayEnd(f, from[k], wayOf(k, side, now), reach)) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
