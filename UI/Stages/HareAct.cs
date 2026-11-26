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
        // Tewi Inaba (2026-11-25): every TEWI_EVERY_BEATS of her section four big beams (TEWI_BEAM_CIRCLES circle radii from their middle line to each side) from her, two to her left and two to her right:
        // an X round the line from her to the player as it was when their see-through preview came (TEWI_PREVIEW_BEATS), each TEWI_SPREAD_DEGREES from it, ahead and behind. Solid, with Byakuren's beam
        // sound, the two of each side turn and slide out over TEWI_SWING_BEATS until they are one beam TEWI_OFFSET_CIRCLES circle radii to her side, along that line: two parallel beams across the whole
        // screen, her between them. They are solid TEWI_LIVE_BEATS in all (the swing too), then fade over TEWI_FADE_BEATS, harmless by then; they hurt like a slider body. She stays where she is from the
        // preview until they have faded, and a set always goes on to its end, also after her section. Where she stands and where the player is are taken when a preview comes, and kept.
        // ======================================================================================================================================================

        private sealed class HareAct : StageAct
        {
            private readonly double beat;

            // (her sets: when each preview comes, and from where and at whom, taken then)
            private readonly double[] strikes;
            private readonly Vector2[] from;
            private readonly Vector2[] aim;
            private readonly bool[] known;
            private int sounded = -1;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public HareAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.TEWI_EVERY_BEATS;

                // (a set whose preview is over in her section: it goes on to its end after it)
                for (double t = period.ShootStart; t + beat * DanmakusuBalance.TEWI_PREVIEW_BEATS < period.End && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                from = new Vector2[strikes.Length];
                aim = new Vector2[strikes.Length];
                known = new bool[strikes.Length];

                Start = period.ShootStart;
                End = strikes.Length > 0 ? goneAt(strikes.Length - 1) : period.End;
            }

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.TEWI_PREVIEW_BEATS;

            private double fadeAt(int k) => solidAt(k) + beat * DanmakusuBalance.TEWI_LIVE_BEATS;

            private double goneAt(int k) => fadeAt(k) + beat * DanmakusuBalance.TEWI_FADE_BEATS;

            /// <summary>She stays until her last set has faded.</summary>
            public override double MechanicsEnd => strikes.Length > 0 ? goneAt(strikes.Length - 1) : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.TEWI_BEAM_CIRCLES;

            /// <summary>The sets that can be there at <paramref name="t"/>.</summary>
            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.TEWI_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.TEWI_PREVIEW_BEATS + DanmakusuBalance.TEWI_LIVE_BEATS + DanmakusuBalance.TEWI_FADE_BEATS)) / every));
                return (first, last);
            }

            /// <summary>
            /// Beam <paramref name="end"/> (1 ahead, -1 behind) of side <paramref name="side"/> (-1, 1) of set <paramref name="k"/> at <paramref name="t"/>: where it starts and which way it goes. An X round
            /// the line at the player until it is solid; then over the swing the one ahead turns to the line and the one behind to the line's way back, and both slide out to their side.
            /// </summary>
            private void rayOf(BulletField f, int k, int side, int end, double t, out Vector2 origin, out Vector2 way)
            {
                float x = smoothStep((t - solidAt(k)) / (beat * DanmakusuBalance.TEWI_SWING_BEATS));
                Vector2 u = aim[k];
                Vector2 across = new Vector2(-u.Y, u.X);
                float spread = DanmakusuBalance.TEWI_SPREAD_DEGREES * MathF.PI / 180f;
                float begin = side * (end > 0 ? spread : MathF.PI - spread);
                float finish = side * (end > 0 ? 0f : MathF.PI);
                float angle = MathF.Atan2(u.Y, u.X) + begin + (finish - begin) * x;

                way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                origin = from[k] + across * (side * f.circleRadius * DanmakusuBalance.TEWI_OFFSET_CIRCLES * x);
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;
                }

                sounded = -1;

                for (int k = 0; k < strikes.Length && solidAt(k) <= time; k++)
                    sounded = k;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] && strikes[k] <= now)
                    {
                        known[k] = true;
                        from[k] = f.bossPositionOf(Period);
                        Vector2 toPlayer = f.characterPosition - from[k];
                        aim[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                    }

                    // (they come with Byakuren's beam sound)
                    if (k > sounded && known[k] && now >= solidAt(k))
                    {
                        sounded = k;

                        if (now - solidAt(k) < 250)
                            f.playStageSound("touhou_strike");
                    }
                }
            }

            /// <summary>She stands where she was when a preview came, until that set has faded.</summary>
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

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var core = new Color4(1f, 0.92f, 0.94f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.85f, 0.25f, 0.4f, 1f)); // (her pink and red)
                float half = halfWidth(f);
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= goneAt(k))
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? smoothStep((t - solidAt(k)) / 40.0) * (1f - smoothStep((t - fadeAt(k)) / (beat * DanmakusuBalance.TEWI_FADE_BEATS)))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    // (the two of a side as one beam through where they meet, from the end of one to the end of the other: seamless there - 2026-11-26, "for Tewi too")
                    for (int side = -1; side <= 1; side += 2)
                    {
                        rayOf(f, k, side, -1, t, out Vector2 origin, out Vector2 behind);
                        rayOf(f, k, side, 1, t, out _, out Vector2 ahead);
                        line.Clear();
                        line.Add(beamRayEnd(f, origin, behind, half));
                        line.Add(origin);
                        line.Add(beamRayEnd(f, origin, ahead, half));
                        fades.Clear();
                        fades.Add(alpha);
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, solid ? half : half * 0.7f, core, border, ref beams, ref pointTotal, null, fades);
                    }
                }
            }

            /// <summary>A solid set hurts like a slider body (not while it fades).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || strikes.Length == 0 || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || now < solidAt(k) || now >= fadeAt(k))
                        continue;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        for (int end = -1; end <= 1; end += 2)
                        {
                            rayOf(f, k, side, end, now, out Vector2 origin, out Vector2 way);

                            if (distanceToSegment(player, origin, beamRayEnd(f, origin, way, reach)) <= reach)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
