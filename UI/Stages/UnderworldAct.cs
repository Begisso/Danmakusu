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
        // Kikuri (2026-11-27): she always stands in the middle of the screen. As each of her attacks begins everything turns to fly straight out from the middle, and so does what is fired in it (BossOps:
        // TrajectoryOpKind.FaceOut); for KIKURI_WARN_BEATS before, KIKURI_WARN_ARROWS big red arrows round her point out, blinking faster. In her attacks every KIKURI_RING_EVERY_BEATS a ring of
        // KIKURI_RING_BEAMS beams waves out of her as Shou's do (SHOU_WAVE_*), hurting like a slider body. Every KIKURI_RESHOOT_EVERY-th bullet of a ring that leaves the screen (by a hash: anyone's) is
        // shot again from her at the player, at the speed it had. Where the player is is taken as each is shot again.
        // ======================================================================================================================================================

        private sealed class UnderworldAct : StageAct
        {
            private const int wave_points = 14;

            private readonly double beat;
            private readonly (double Start, double End)[] attacks;
            private readonly double[] rings;
            private readonly List<Vector2> line = new List<Vector2>(wave_points);
            private readonly List<float> widths = new List<float>(wave_points);

            public UnderworldAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                attacks = BossOps.Attacks(period).FindAll(a => a.End > a.Start).ToArray();

                var list = new List<double>();

                foreach (var (start, end) in attacks)
                {
                    for (double t = Math.Max(start, period.ShootStart); t < end && list.Count < 10000; t += beat * DanmakusuBalance.KIKURI_RING_EVERY_BEATS)
                        list.Add(t);
                }

                rings = list.ToArray();
                Start = period.ShootStart - beat * DanmakusuBalance.KIKURI_WARN_BEATS;
                End = period.End + beat * 16;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            private static Vector2 middle(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            public override void Reset(BulletField f, double time) => ClearBullets();

            private static bool outside(BulletField f, Vector2 p) => p.X < f.stageMin.X || p.X > f.stageMax.X || p.Y < f.stageMin.Y || p.Y > f.stageMax.Y;

            /// <summary>Every fourth bullet of a ring that leaves the screen is shot again from her at the player.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Homing || !outside(f, to) || outside(f, from))
                    return false;

                if (hash01(Id, (int)(Math.Abs((long)emitter.Time) % 1000000) * 64 + bullet, 701) * DanmakusuBalance.KIKURI_RESHOOT_EVERY >= 1f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);

                if (speed < 1e-4f)
                    return false;

                Vector2 at = f.bossPositionOf(Period);
                Vector2 way = f.characterPosition - at;
                way = way.LengthSquared > 1e-6f ? Vector2.Normalize(way) : Vector2.UnitY;

                int count = Bullets.Count;
                AddBullet(f.makeStageBullet(stored, emitter, bullet, t, at, way * speed));
                return Bullets.Count > count;
            }

            // ---- the waving beams (as Shou's) ----

            private float waveSpeed => stageBulletSpeed(Period);

            private float waveHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.SHOU_WAVE_WIDTH;

            private Vector2 waveHead(BulletField f, int ring, int beam, double age)
            {
                float angle = ring * 2.3999632f + beam * MathF.Tau / DanmakusuBalance.KIKURI_RING_BEAMS;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 across = new Vector2(-way.Y, way.X);
                float a = (float)Math.Max(age, 0);
                float swing = f.circleRadius * DanmakusuBalance.SHOU_WAVE_CIRCLES * MathF.Sin(wavePhase(f, a)) * smoothStep(a / (beat * 0.5));
                return middle(f) + way * (waveSpeed * a) + across * (swing * (beam % 2 == 0 ? 1f : -1f));
            }

            private float wavePhase(BulletField f, float age)
            {
                float far = Vector2.Distance(f.stageMin, f.stageMax) * 0.5f / Math.Max(waveSpeed, 1e-3f);
                float gain = DanmakusuBalance.SHOU_WAVE_FASTEST - 1f;
                float counted = age <= far ? age + gain * age * age / (2f * far) : far + gain * far / 2f + DanmakusuBalance.SHOU_WAVE_FASTEST * (age - far);
                return counted / (float)(beat * DanmakusuBalance.SHOU_WAVE_BEATS) * MathF.Tau;
            }

            private double waveLife(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) + waveSpeed * beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS) / Math.Max(waveSpeed, 1e-3f) + beat;

            private bool wave(BulletField f, int ring, int beam, double t, float half)
            {
                double age = t - rings[ring];
                double tail = beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS;

                line.Clear();
                widths.Clear();

                if (age <= 0)
                    return false;

                for (int i = 0; i < wave_points; i++)
                {
                    double back = tail * i / (wave_points - 1);

                    if (age - back < 0 && i > 0)
                        break;

                    line.Add(waveHead(f, ring, beam, age - back));
                    widths.Add(half * (1f - 0.7f * i / (wave_points - 1)));
                }

                return line.Count >= 2;
            }

            /// <summary>The rings that can be on the screen at <paramref name="t"/>: from the first fired no longer ago than a beam lives.</summary>
            private int firstRing(BulletField f, double t)
            {
                double earliest = t - waveLife(f);
                int lo = 0, hi = rings.Length;

                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;

                    if (rings[mid] < earliest)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                return lo;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0 || rings.Length == 0)
                    return;

                int style = f.comboStyle(2, true, Period.Rank);
                float half = waveHalf(f);

                for (int ring = firstRing(f, t); ring < rings.Length && rings[ring] <= t; ring++)
                {
                    for (int beam = 0; beam < DanmakusuBalance.KIKURI_RING_BEAMS; beam++)
                    {
                        if (wave(f, ring, beam, t, half))
                            f.addPlainBeam(line, half, f.palette[style % f.palette.Length], f.borderPalette[style % f.borderPalette.Length], ref beams, ref pointTotal, widths);
                    }
                }
            }

            /// <summary>For KIKURI_WARN_BEATS before each attack, big red arrows round her point out: everything will fly that way.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                double warn = beat * DanmakusuBalance.KIKURI_WARN_BEATS;

                foreach (var (start, _) in attacks)
                {
                    if (t >= start || t < start - warn)
                        continue;

                    double left = (start - t) / warn;
                    float level = smoothStep((1 - left) / 0.25) * (0.6f + 0.4f * MathF.Cos((float)(1 - left) * MathF.PI * 6f));
                    float arrow = f.circleRadius * DanmakusuBalance.KIKURI_WARN_ARROW_SIZE;
                    float reach = DanmakusuBalance.MAGIC_CIRCLE_RADIUS + arrow;
                    var red = new Color4(1f, 0.2f, 0.15f, 1f);

                    for (int a = 0; a < DanmakusuBalance.KIKURI_WARN_ARROWS; a++)
                    {
                        float angle = a * MathF.Tau / DanmakusuBalance.KIKURI_WARN_ARROWS;
                        Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        f.stageRipple.Add(StageTexture.SeijaArrow, middle(f) + outward * reach, new Vector2(arrow), angle * 180f / MathF.PI, level, red);
                    }

                    break;
                }
            }

            /// <summary>Her waving beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || rings.Length == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float half = waveHalf(f);
                float room = f.circleRadius * DanmakusuBalance.SHOU_WAVE_CIRCLES + half * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + 1f;
                double tail = beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS;
                Vector2 off = player - middle(f);
                float distance = off.Length;

                for (int ring = firstRing(f, now); ring < rings.Length && rings[ring] <= now; ring++)
                {
                    double age = now - rings[ring];

                    if (distance > waveSpeed * (float)age + room || distance < waveSpeed * (float)(age - tail) - room)
                        continue;

                    float phase = ring * 2.3999632f;

                    for (int beam = 0; beam < DanmakusuBalance.KIKURI_RING_BEAMS; beam++)
                    {
                        float angle = phase + beam * MathF.Tau / DanmakusuBalance.KIKURI_RING_BEAMS;
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        if (Vector2.Dot(off, way) < -room || MathF.Abs(off.X * way.Y - off.Y * way.X) > room)
                            continue;

                        if (!wave(f, ring, beam, now, half))
                            continue;

                        for (int i = 0; i + 1 < line.Count; i++)
                        {
                            if (distanceToSegment(player, line[i], line[i + 1]) <= widths[i] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
