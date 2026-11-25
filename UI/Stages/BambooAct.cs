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
        // Mai Teireida (2026-11-25): no pauses. Every TEIREIDA_EVERY_BEATS TEIREIDA_BEAMS beams as thin as a slider's from where she is out past the edge of the screen, evenly round her, with the player (as
        // they were when the preview came) in the middle of a gap: a see-through preview for TEIREIDA_PREVIEW_BEATS, while she stands still; then they are there at once, whole, with Byakuren's beam sound,
        // and hurt like a slider body for TEIREIDA_LIVE_BEATS, fading out over TEIREIDA_FADE_BEATS (harmless by then) as the sequence ends. Once they are shot she moves on, and they stay where they
        // were shot. A set always goes on to its end, also after her section.
        // Her rings of talismans are the planner's; that every bullet of her section moves in steps of four beats, 45 degrees to either side of its way, with a red arrow before every move after a stop, is
        // BossOps' (TrajectoryOpKind.Zigzag) and the field's.
        // Where she stands and where the player is when a preview comes are taken then and kept.
        // (Until 2026-11-25, the same day: she stayed where she was when her section began, the beams always from there, without a sound.)
        // ======================================================================================================================================================

        private sealed class BambooAct : StageAct
        {
            private readonly double beat;
            private readonly double[] strikes; // (when each preview comes)
            private readonly Vector2[] from; // (where she was then: the beams come out of there)
            private readonly float[] turns; // (the angle of the first beam of each set)
            private readonly bool[] known;
            private int soundedStrike = -1;
            private double resetTime = double.NegativeInfinity;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public BambooAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.TEIREIDA_EVERY_BEATS;

                // (a set whose preview is over in her section: it goes on to its end after it)
                for (double t = period.ShootStart; t + beat * DanmakusuBalance.TEIREIDA_PREVIEW_BEATS < period.End && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                from = new Vector2[strikes.Length];
                turns = new float[strikes.Length];
                known = new bool[strikes.Length];

                Start = period.ShootStart;
                End = Math.Max(period.End, strikes.Length > 0 ? goneAt(strikes.Length - 1) : period.End);
            }

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.TEIREIDA_PREVIEW_BEATS;

            private double liveEnd(int k) => solidAt(k) + beat * DanmakusuBalance.TEIREIDA_LIVE_BEATS;

            private double goneAt(int k) => liveEnd(k) + beat * DanmakusuBalance.TEIREIDA_FADE_BEATS;

            /// <summary>She stays until her last set has faded.</summary>
            public override double MechanicsEnd => strikes.Length > 0 ? goneAt(strikes.Length - 1) : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private static float step => MathF.Tau / DanmakusuBalance.TEIREIDA_BEAMS;

            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.TEIREIDA_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.TEIREIDA_PREVIEW_BEATS + DanmakusuBalance.TEIREIDA_LIVE_BEATS + DanmakusuBalance.TEIREIDA_FADE_BEATS)) / every));
                return (first, last);
            }

            public override void Reset(BulletField f, double time)
            {
                resetTime = time;
                soundedStrike = -1;

                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;
                    else if (solidAt(k) <= time)
                        soundedStrike = k;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every set whose preview has come takes where she is, and is turned so that the player, as they are then, stands in the middle of a gap)
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] || strikes[k] > now)
                        continue;

                    known[k] = true;
                    from[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from[k];
                    turns[k] = (toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f) + step / 2f;
                }

                // (each set comes with Byakuren's beam sound)
                for (; soundedStrike + 1 < strikes.Length && solidAt(soundedStrike + 1) <= now; soundedStrike++)
                {
                    double solid = solidAt(soundedStrike + 1);

                    if (now - solid < 250 && solid > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            /// <summary>She stands still while a set is shown, until it is shot.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                if (strikes.Length == 0 || now < Start)
                    return false;

                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && now >= strikes[k] && now < solidAt(k))
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

                var core = new Color4(0.9f, 1f, 0.88f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.18f, 0.54f, 0.29f, 1f));
                float half = halfWidth(f);
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= goneAt(k))
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? smoothStep((t - solidAt(k)) / 40.0) * (1f - smoothStep((t - liveEnd(k)) / (beat * DanmakusuBalance.TEIREIDA_FADE_BEATS)))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int n = 0; n < DanmakusuBalance.TEIREIDA_BEAMS; n++)
                    {
                        float angle = turns[k] + n * step;
                        var way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        line.Clear();
                        line.Add(from[k]);
                        line.Add(beamRayEnd(f, from[k], way, half));
                        fades.Clear();
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, solid ? half : half * 0.6f, core, border, ref beams, ref pointTotal, null, fades);
                    }
                }
            }

            /// <summary>
            /// A solid set hurts like a slider body (not while it fades): every beam goes from where it was shot out past the edge, so the one nearest the player's angle round that place is the only one to
            /// look at - how far the player is from its line.
            /// </summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || strikes.Length == 0 || now < Start)
                    return false;

                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || now < solidAt(k) || now >= liveEnd(k))
                        continue;

                    Vector2 off = f.characterPosition - from[k];
                    float distance = off.Length;

                    if (distance <= reach)
                        return true;

                    // (the angle to the nearest beam, -step/2..step/2)
                    float into = (MathF.Atan2(off.Y, off.X) - turns[k]) / step;
                    float aside = (into - MathF.Round(into)) * step;

                    if (distance * MathF.Abs(MathF.Sin(aside)) <= reach)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Where a beam from <paramref name="p"/> along <paramref name="d"/> leaves the screen, <paramref name="pad"/> px further out (Sara's and Mai Teireida's beams).</summary>
        private static Vector2 beamRayEnd(BulletField f, Vector2 p, Vector2 d, float pad)
        {
            Vector2 low = f.stageMin - new Vector2(pad), high = f.stageMax + new Vector2(pad);
            float most = float.PositiveInfinity;

            if (d.X > 1e-6f)
                most = Math.Min(most, (high.X - p.X) / d.X);
            else if (d.X < -1e-6f)
                most = Math.Min(most, (low.X - p.X) / d.X);

            if (d.Y > 1e-6f)
                most = Math.Min(most, (high.Y - p.Y) / d.Y);
            else if (d.Y < -1e-6f)
                most = Math.Min(most, (low.Y - p.Y) / d.Y);

            return p + d * Math.Max(float.IsInfinity(most) ? 0f : most, 0f);
        }
    }
}
