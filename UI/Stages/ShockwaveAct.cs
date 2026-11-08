using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Yuugi Hoshiguma: every eight beats of her section she stamps: a smooth ring like Raiko's ripples goes out from where she stands and the screen shakes. A bullet the ring touches is thrown outwards, away from where she
        // stamped, faster than it flew: two and a half times its speed right by her, less and less the further from her, down to 1.2 times at the distance of the wall, and it glows the more the faster it was thrown.
        // She shoots plain bullets twice as big as usual (the planner's).
        // ======================================================================================================================================================

        private sealed class ShockwaveAct : StageAct
        {
            private readonly double[] marks;
            private readonly Vector2[] centres;
            private readonly bool[] known;

            public ShockwaveAct(BossPeriod period, double[] marks)
            {
                Period = period;
                this.marks = marks;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                Start = marks[0];
                End = period.End;
            }

            private static double lifeOf(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / DanmakusuBalance.RAIKO_RIPPLE_SPEED;

            public override void Init(BulletField f)
            {
                AbsorbFrom = Start;
                AbsorbTo = marks[^1] + lifeOf(f);
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    if (known[k])
                        continue;

                    known[k] = true;
                    centres[k] = f.bossVisualOf(Period);

                    if (now - marks[k] < 250)
                        f.playStageSound("mokou_wall_hit");
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Deflector || emitter.IsStuck(bullet))
                    return false;

                double life = lifeOf(f);

                for (int k = marks.Length - 1; k >= 0; k--)
                {
                    double age = t - marks[k];

                    if (age < 0 || !known[k])
                        continue;

                    if (age > life)
                        break;

                    Vector2 away = to - centres[k];
                    float distance = away.Length;

                    if (MathF.Abs(distance - DanmakusuBalance.RAIKO_RIPPLE_SPEED * (float)age) > DanmakusuBalance.YUUGI_RING_HALF_WIDTH)
                        continue;

                    Vector2 outward = distance > 1e-3f ? away / distance : Vector2.UnitY;
                    float wall = Math.Max((f.stageMax.X - f.stageMin.X) * 0.5f, 1f);
                    float near = 1f - Math.Clamp(distance / wall, 0f, 1f); // (1 right by her, 0 at the distance of the wall)
                    float push = DanmakusuBalance.YUUGI_PUSH_FAR + (DanmakusuBalance.YUUGI_PUSH_NEAR - DanmakusuBalance.YUUGI_PUSH_FAR) * near;
                    var stored = f.makeStored(emitter, t);
                    float big = emitter.BigScale(bullet);
                    Vector2 velocity = outward * (stored.Speed * push);
                    int before = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = to,
                        V = velocity,
                        Face = outward,
                        Radius = stored.Radius * big,
                        Hit = stored.Hit * big,
                        Style = stored.Style,
                        Shape = stored.Shape,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Bloom = DanmakusuBalance.YUUGI_BLOOM * near,
                        Until = t + f.stageLife(velocity),
                    });

                    return Bullets.Count > before;
                }

                return false;
            }

            /// <summary>0..1: the screen shakes for a moment at every stamp.</summary>
            public float ShakeAt(double now)
            {
                float shake = 0f;

                foreach (double mark in marks)
                {
                    if (mark > now)
                        break;

                    if (now - mark < DanmakusuBalance.RAIKO_SHAKE_MS)
                        shake = Math.Max(shake, 1f - (float)((now - mark) / DanmakusuBalance.RAIKO_SHAKE_MS));
                }

                return shake;
            }

            public override void Draw(BulletField f, double t)
            {
                double life = lifeOf(f);

                for (int k = marks.Length - 1; k >= 0; k--)
                {
                    double age = t - marks[k];

                    if (age < 0 || !known[k])
                        continue;

                    if (age > life)
                        break;

                    float r = DanmakusuBalance.RAIKO_RIPPLE_SPEED * (float)age;
                    f.stageRipple.AddRing(centres[k], r + DanmakusuBalance.YUUGI_RING_HALF_WIDTH, DanmakusuBalance.YUUGI_RING_HALF_WIDTH, DanmakusuBalance.YUUGI_RING_ALPHA * smoothStep(age / 60.0), new Color4(1f, 0.72f, 0.42f, 1f));
                }
            }
        }
    }
}
