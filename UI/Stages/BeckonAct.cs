using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Mike Goutokuji (2026-11-28, "Shoots ring of 8 3x size bullets every 4 beats. After 8 beats they separately explode into 1x ellipses - ring of 8 too. She also sends golden smooth wave that
        // explodes all big bullets into smaller golden rings. Ring size depends on how many of 1x size bullets can fit into big bullet. Wave does not affect any bullet that won't generate more than 3
        // bullets. Every 10th bullet on the screen is 3x size and golden").
        // Her rings of MIKE_RING_BULLETS big bullets (MIKE_BIG times a plain one) are the planner's (addMikeRings), and so is every MIKE_GOLD_EVERY-th bullet of her section being MIKE_GOLD_SIZE times as
        // big and golden (BulletEmitter.BigGolden). Here: each bullet of her rings bursts MIKE_BURST_BEATS after it was fired into a ring of MIKE_BURST_BULLETS plain ellipses, each its own way. Every
        // MIKE_WAVE_EVERY_BEATS of her section (from MIKE_WAVE_FIRST_BEATS in) she sends out a soft golden wave from where she is, growing at MIKE_WAVE_SPEED times a plain bullet's speed: every bullet of
        // a ring it goes over (anyone's) that is big enough for more than three plain bullets to fit in it (by their areas: twice a plain one's size at least) bursts into a golden ring of as many plain
        // bullets as fit (nine for one three times as big). The wave itself does not hurt.
        // Where a wave starts is taken when it is sent; how a ring bursts goes by its bullet, so it is the same when time goes back.
        // ======================================================================================================================================================

        private sealed class BeckonAct : StageAct
        {
            private readonly double beat;
            private readonly double[] waves;
            private readonly Vector2[] centres;
            private readonly bool[] known;

            public BeckonAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var list = new System.Collections.Generic.List<double>();

                for (double t = period.ShootStart + beat * DanmakusuBalance.MIKE_WAVE_FIRST_BEATS; t < period.End && list.Count < 4000; t += beat * DanmakusuBalance.MIKE_WAVE_EVERY_BEATS)
                    list.Add(t);

                waves = list.ToArray();
                centres = new Vector2[waves.Length];
                known = new bool[waves.Length];

                double lastWave = waves.Length > 0 ? waves[^1] + waveLife(f) : period.End;
                End = Math.Max(period.End + beat * (DanmakusuBalance.MIKE_BURST_BEATS + 32), lastWave + beat * 16);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = Math.Max(period.End + beat * DanmakusuBalance.MIKE_BURST_BEATS + 1000, lastWave);
            }

            private float waveSpeed => stageBulletSpeed(Period) * DanmakusuBalance.MIKE_WAVE_SPEED;

            /// <summary>How long a wave lasts: until it has gone past the farthest corner of the screen from anywhere.</summary>
            private double waveLife(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / Math.Max(waveSpeed, 1e-4f);

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < waves.Length; k++)
                {
                    if (waves[k] > time)
                        known[k] = false;
                }

                ClearBullets(); // (what burst is burst again as the rings are looked at again from when they were fired)
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < waves.Length && waves[k] <= now; k++)
                {
                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = f.bossVisualOf(Period);
                    }
                }
            }

            /// <summary>Is this one of her own rings of big bullets?</summary>
            private bool hers(BulletField f, BulletEmitter emitter)
                => emitter.OwnShot && emitter.BossRank == Period.Rank && emitter.Shape == BulletShape.Circle && emitter.Time >= Period.ShootStart - 1 && emitter.Time < Period.End
                   && emitter.VisualRadius >= f.stageBaseRadius(Period) * 2f;

            /// <summary>
            /// Her own big bullets burst when their time has come (into plain ellipses); any big enough bullet that her wave goes over bursts into a golden ring of as many plain bullets as fit in it.
            /// </summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Homing || emitter.KeepsSize)
                    return false;

                float plain = f.stageBaseRadius(Period);
                int seed = (int)(emitter.Time * 0.37) * 31 + bullet;

                // her own, MIKE_BURST_BEATS after they were fired
                if (hers(f, emitter) && t >= emitter.Time + beat * DanmakusuBalance.MIKE_BURST_BEATS)
                {
                    burst(f, t, to, DanmakusuBalance.MIKE_BURST_BULLETS, plain, BulletShape.Ellipse, false, seed);
                    return true;
                }

                // her waves: what is big enough for more than three plain bullets
                float size = emitter.VisualRadius * emitter.BigScale(bullet);
                float ratio = size / Math.Max(plain, 0.5f);
                int count = Math.Min((int)MathF.Floor(ratio * ratio + 1e-3f), 64);

                if (count <= 3)
                    return false;

                double before = Math.Min(ScanPrevious, t);
                float reach = Vector2.Distance(f.stageMin, f.stageMax) + 50f;

                for (int k = 0; k < waves.Length && waves[k] <= t; k++)
                {
                    if (!known[k])
                        continue;

                    float now = waveSpeed * (float)(t - waves[k]);
                    float then = waveSpeed * (float)Math.Max(before - waves[k], 0);

                    if (now > reach)
                        continue; // (long gone)

                    if (Vector2.Distance(to, centres[k]) > now || Vector2.Distance(from, centres[k]) <= then)
                        continue; // (not gone over in this step)

                    burst(f, t, to, count, plain, BulletShape.Circle, true, seed);
                    return true;
                }

                return false;
            }

            /// <summary>A ring of <paramref name="count"/> plain bullets out of <paramref name="at"/>, each its own way, at a plain bullet's speed (golden ones glow).</summary>
            private void burst(BulletField f, double t, Vector2 at, int count, float radius, BulletShape shape, bool golden, int seed)
            {
                float speed = stageBulletSpeed(Period);
                float phase = hash01(seed, count, 811) * MathF.Tau;

                for (int i = 0; i < count; i++)
                {
                    float angle = phase + i * MathF.Tau / count;
                    Vector2 v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = at,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(seed + i, true, Period.Rank),
                        Shape = shape,
                        Tint = golden ? BulletRenderer.TINT_YELLOW : 0,
                        TintAmount = golden ? DanmakusuBalance.TOYOHIME_GOLD_TINT : 0f,
                        Bloom = golden ? DanmakusuBalance.TOYOHIME_GOLD_BLOOM : 0f,
                        Untraced = true,
                        Until = t + f.stageLife(v),
                    });
                }

                f.addFlash(t, at, radius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 2f);
            }

            /// <summary>Her waves: a soft golden ring, wider and fainter at its edges, fading as it grows.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                var gold = new Color4(1f, 0.82f, 0.3f, 1f);
                float half = Math.Max(f.circleRadius * DanmakusuBalance.MIKE_WAVE_WIDTH, 2f);
                double life = waveLife(f);

                for (int k = 0; k < waves.Length && waves[k] <= t; k++)
                {
                    if (!known[k] || t > waves[k] + life)
                        continue;

                    float r = waveSpeed * (float)(t - waves[k]);
                    float alpha = 0.6f * (1f - smoothStep((t - waves[k]) / life)) * smoothStep((t - waves[k]) / 120.0);

                    if (alpha < 0.01f || r < half * 0.5f)
                        continue;

                    // (three rings over each other, each wider and fainter: smooth edges)
                    if (r > half * 2.2f)
                        f.stageRipple.AddRing(centres[k], r, half * 2.2f, alpha * 0.25f, gold);

                    if (r > half * 1.3f)
                        f.stageRipple.AddRing(centres[k], r, half * 1.3f, alpha * 0.4f, gold);

                    f.stageRipple.AddRing(centres[k], r, half * 0.5f, alpha * 0.8f, new Color4(1f, 0.95f, 0.7f, 1f));
                }
            }
        }
    }
}
