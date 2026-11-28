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
        // Konngara (2026-11-27): every KONNGARA_EVERY_BEATS of her section an endless straight beam, as thin as a slider's, from her straight up: a see-through preview for KONNGARA_PREVIEW_BEATS, then it
        // sweeps once round her, clockwise, over KONNGARA_SPIN_BEATS (Byakuren's beam sound as it starts, the planner's cue), hurting like a slider body, and fades over KONNGARA_FADE_BEATS, harmless. Every
        // bullet of a ring it sweeps over is cut in two along it, and the two pieces fly apart across it (square to the line from her). Where she is is taken as each sweep comes, and kept.
        // Since 2026-11-28 ("spin speed halved. Laserbeam is white body") it sweeps twice as slowly (KONNGARA_SPIN_BEATS 32, 16 before; KONNGARA_EVERY_BEATS 44, so the next still comes eight beats
        // after), and its body is white.
        // ======================================================================================================================================================

        private sealed class SweepAct : CuttingAct
        {
            private const double sweep_ms = 24; // (how far back what the beam has just gone over still hurts: about a frame)

            private readonly double beat;
            private readonly double[] sweeps;
            private readonly Vector2[] from;
            private readonly bool[] known;
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public SweepAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.KONNGARA_PREVIEW_BEATS < period.End && list.Count < 10000; t += beat * DanmakusuBalance.KONNGARA_EVERY_BEATS)
                    list.Add(t);

                sweeps = list.ToArray();
                from = new Vector2[sweeps.Length];
                known = new bool[sweeps.Length];
                Start = period.ShootStart;
                End = sweeps.Length > 0 ? goneAt(sweeps.Length - 1) + beat * 16 : period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private double spinAt(int k) => sweeps[k] + beat * DanmakusuBalance.KONNGARA_PREVIEW_BEATS;

            private double spunAt(int k) => spinAt(k) + beat * DanmakusuBalance.KONNGARA_SPIN_BEATS;

            private double goneAt(int k) => spunAt(k) + beat * DanmakusuBalance.KONNGARA_FADE_BEATS;

            /// <summary>The times Byakuren's beam sound plays: as every sweep starts.</summary>
            public static IEnumerable<double> SpinTimes(BossPeriod period)
            {
                double beat = Math.Max(period.BeatLength, 1);

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.KONNGARA_PREVIEW_BEATS < period.End; t += beat * DanmakusuBalance.KONNGARA_EVERY_BEATS)
                    yield return t + beat * DanmakusuBalance.KONNGARA_PREVIEW_BEATS;
            }

            public override double MechanicsEnd => sweeps.Length > 0 ? goneAt(sweeps.Length - 1) : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.KONNGARA_BEAM_WIDTH;

            /// <summary>The sweep there is at <paramref name="t"/> (from its preview until it has faded), or -1.</summary>
            private int sweepAt(double t)
            {
                if (sweeps.Length == 0 || t < sweeps[0])
                    return -1;

                int k = Math.Min(sweeps.Length - 1, (int)Math.Floor((t - sweeps[0]) / (beat * DanmakusuBalance.KONNGARA_EVERY_BEATS)));
                return t < goneAt(k) ? k : -1;
            }

            /// <summary>Which way sweep <paramref name="k"/>'s beam points at <paramref name="t"/> (radians): straight up, then once round, clockwise on the screen.</summary>
            private float angleOf(int k, double t)
            {
                double u = Math.Clamp((t - spinAt(k)) / (beat * DanmakusuBalance.KONNGARA_SPIN_BEATS), 0, 1);
                return -MathF.PI / 2f + (float)(smoothStep(u) * Math.Tau);
            }

            public override void Reset(BulletField f, double time)
            {
                ClearPieces();

                for (int k = 0; k < sweeps.Length; k++)
                {
                    if (sweeps[k] > time)
                        known[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < sweeps.Length && sweeps[k] <= now; k++)
                {
                    if (!known[k])
                    {
                        known[k] = true;
                        from[k] = f.bossPositionOf(Period);
                    }
                }
            }

            /// <summary>A bullet of a ring that the sweeping beam goes over is cut along it; the pieces fly apart across it.</summary>
            public override bool Takes(BulletField f, double t, Vector2 at, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                int k = sweepAt(t);

                if (k < 0 || !known[k] || t < spinAt(k) || t > spunAt(k))
                    return false;

                double before = Math.Max(ScanPrevious, spinAt(k));
                float a0 = angleOf(k, before), a1 = angleOf(k, t);

                if (a1 <= a0)
                    return false;

                Vector2 middle = from[k];
                Vector2 off = to - middle;

                if (off.LengthSquared < 1f)
                    return false;

                // (has the beam gone over it in this step: its way from her between where the beam was and where it is)
                float angle = MathF.Atan2(off.Y, off.X);
                float into = angle - a0;
                into -= MathF.Floor(into / MathF.Tau) * MathF.Tau;
                float size = SizeOf(emitter, bullet);
                float slack = (size + halfWidth(f)) / off.Length;

                if (into > a1 - a0 + slack && into < MathF.Tau - slack)
                    return false;

                // (cut along the beam where it is now; the pieces fly apart square to it)
                Vector2 way = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
                Vector2 normal = new Vector2(-way.Y, way.X);
                float along = Vector2.Dot(off, normal);
                CutBullet(f, t, at, to, emitter, bullet, normal, -along);
                return true;
            }

            public override void Draw(BulletField f, double t)
            {
                if (!f.failed)
                    DrawPieces(f, t);
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                int k = sweepAt(t);

                if (f.failed || k < 0 || !known[k])
                    return;

                float alpha = t < spinAt(k)
                    ? DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - sweeps[k]) / (beat * 0.25))
                    : t < spunAt(k) ? 1f : 1f - smoothStep((t - spunAt(k)) / (beat * DanmakusuBalance.KONNGARA_FADE_BEATS));

                if (alpha < 0.01f)
                    return;

                float angle = angleOf(k, t);
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float far = Vector2.Distance(f.stageMin, f.stageMax) + 100f;

                line.Clear();
                fades.Clear();
                line.Add(from[k]);
                line.Add(from[k] + way * far);
                fades.Add(alpha);
                fades.Add(alpha);

                var body = Color4.White; // (white all through since 2026-11-28, "laserbeam is white body": a pale pink before)
                var border = DanmakusuBalance.BorderColour(new Color4(0.85f, 0.12f, 0.14f, 1f)); // (her red)
                f.addPlainBeam(line, halfWidth(f), body, border, ref beams, ref pointTotal, null, fades, Color4.White, t >= spinAt(k) && t < spunAt(k) ? 0.5f : 0f);
            }

            /// <summary>The beam hurts like a slider body while it sweeps.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                int k = sweepAt(now);

                if (f.failed || k < 0 || !known[k] || now < spinAt(k) || now > spunAt(k))
                    return false;

                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                Vector2 off = f.characterPosition - from[k];
                float distance = off.Length;

                if (distance <= reach)
                    return true;

                // (what it has gone over in the last moment: far from her it turns many times its width in a frame, and would jump over the character)
                double back = Math.Max(spinAt(k), now - sweep_ms);
                float a0 = angleOf(k, back), a1 = angleOf(k, now);
                float into = MathF.Atan2(off.Y, off.X) - a0;
                into -= MathF.Floor(into / MathF.Tau) * MathF.Tau;
                float slack = MathF.Asin(Math.Min(reach / distance, 1f));

                return into <= a1 - a0 + slack || into >= MathF.Tau - slack;
            }
        }
    }
}
