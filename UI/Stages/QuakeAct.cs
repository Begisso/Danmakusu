using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Tenshi Hinanawi: all through her section every eighth bullet that goes out of the bottom of the screen is shot back up as three bullets half its size, fanned out over 120 degrees. Every eight beats she sends out a
        // shockwave, twice as fast as Raiko's ripples, and the screen shakes for as long as it is on it. While it is, every fourth bullet that goes out of a wall where the wave has already got to comes back in the same way,
        // not only at the bottom. Her own bullets are ricochet stars (the planner's).
        // ======================================================================================================================================================

        private sealed class QuakeAct : StageAct
        {
            private readonly double[] marks;
            private readonly Vector2[] centres;
            private readonly bool[] known;
            private int crossings, waveCrossings;

            public QuakeAct(BossPeriod period, double[] marks)
            {
                Period = period;
                this.marks = marks;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                Start = period.ShootStart;
                End = period.End;
            }

            private static float speed => DanmakusuBalance.RAIKO_RIPPLE_SPEED * DanmakusuBalance.TENSHI_WAVE_SPEED;

            private static double lifeOf(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / speed;

            public override void Init(BulletField f)
            {
                AbsorbFrom = Start;
                AbsorbTo = marks[^1] + lifeOf(f);
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                crossings = waveCrossings = 0;
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

            /// <summary>Has a wave that is still on the screen got to <paramref name="place"/> by <paramref name="t"/>?</summary>
            private bool waveAt(BulletField f, Vector2 place, double t)
            {
                double life = lifeOf(f);

                for (int k = marks.Length - 1; k >= 0; k--)
                {
                    double age = t - marks[k];

                    if (age < 0 || !known[k])
                        continue;

                    if (age > life)
                        return false;

                    if (Vector2.Distance(place, centres[k]) <= speed * (float)age)
                        return true;
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Deflector)
                    return false;

                Vector2 low = f.stageMin, high = f.stageMax;
                bool wasIn = from.X >= low.X && from.X <= high.X && from.Y >= low.Y && from.Y <= high.Y;
                bool isOut = to.X < low.X || to.X > high.X || to.Y < low.Y || to.Y > high.Y;

                if (!wasIn || !isOut)
                    return false;

                // (where it went out, and the way back in)
                Vector2 delta = to - from;
                float u = 1f;
                Vector2 inward = -Vector2.UnitY;

                if (to.Y > high.Y)
                    u = Math.Min(u, (high.Y - from.Y) / delta.Y);

                if (to.Y < low.Y && (low.Y - from.Y) / delta.Y < u)
                {
                    u = (low.Y - from.Y) / delta.Y;
                    inward = Vector2.UnitY;
                }

                if (to.X > high.X && (high.X - from.X) / delta.X < u)
                {
                    u = (high.X - from.X) / delta.X;
                    inward = -Vector2.UnitX;
                }

                if (to.X < low.X && (low.X - from.X) / delta.X < u)
                {
                    u = (low.X - from.X) / delta.X;
                    inward = Vector2.UnitX;
                }

                Vector2 contact = from + delta * Math.Clamp(u, 0f, 1f);
                bool bottom = inward.Y < -0.5f;
                bool every = bottom && t >= Start && t < End && crossings++ % DanmakusuBalance.TENSHI_EVERY == 0;

                if (!every && (!waveAt(f, contact, t) || waveCrossings++ % DanmakusuBalance.TENSHI_WAVE_EVERY != 0))
                    return false;

                // (it comes back off the wall: a ricochet, it flashes there)
                f.addFlash(t, contact, emitter.VisualRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE);

                var stored = f.makeStored(emitter, t);
                float size = emitter.BigScale(bullet) * DanmakusuBalance.TENSHI_SIZE;
                float fan = DanmakusuBalance.TENSHI_FAN_DEGREES * MathF.PI / 180f;

                for (int piece = 0; piece < DanmakusuBalance.TENSHI_PIECES; piece++)
                {
                    Vector2 way = turned(inward, fan * (piece / (DanmakusuBalance.TENSHI_PIECES - 1f) - 0.5f));
                    Vector2 velocity = way * stored.Speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = contact,
                        V = velocity,
                        Face = way,
                        Radius = stored.Radius * size,
                        Hit = stored.Hit * size,
                        Style = stored.Style,
                        Shape = stored.Shape,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Until = t + f.stageLife(velocity),
                    });
                }

                return true;
            }

            /// <summary>0..1: the screen shakes for as long as a wave is on it.</summary>
            public float ShakeAt(BulletField f, double now)
            {
                double life = lifeOf(f);

                foreach (double mark in marks)
                {
                    if (now >= mark && now - mark < life)
                        return 0.5f;
                }

                return 0f;
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

                    float r = speed * (float)age;
                    f.stageRipple.AddRing(centres[k], r + DanmakusuBalance.TENSHI_WAVE_HALF_WIDTH, DanmakusuBalance.TENSHI_WAVE_HALF_WIDTH, DanmakusuBalance.TENSHI_WAVE_ALPHA * smoothStep(age / 60.0), new Color4(0.86f, 0.72f, 0.5f, 1f));
                }
            }
        }
    }
}
