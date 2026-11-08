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
        // Sanae Kochiya: what she does to the rings (twice as many bullets, half as big, round rings turned into stars) and her own rings of ricochet stars are the planner's. Here: while she attacks the
        // background is dimmed green and a giant see-through star turns slowly behind everything (fading in and out with each attack).
        // ======================================================================================================================================================

        private sealed class MiracleAct : StageAct
        {
            private readonly List<(double Start, double End)> attacks;

            public MiracleAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                Start = period.ShootStart;
                End = period.End;
            }

            private float presence(double t)
            {
                float level = 0f;

                foreach (var (start, end) in attacks)
                {
                    if (end <= start || t < start || t > end + DanmakusuBalance.STAGE_FADE_OUT_MS)
                        continue;

                    level = Math.Max(level, smoothStep((t - start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((end + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS));
                }

                return level;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float level = presence(t);

                if (level <= 0.002f)
                    return;

                if (f.hasScreenBounds)
                {
                    f.miracleBackground.Position = f.screenMin;
                    f.miracleBackground.Size = f.screenMax - f.screenMin;
                }

                f.miracleBackground.Alpha = Math.Max(f.miracleBackground.Alpha, level * DanmakusuBalance.SANAE_BACKGROUND_ALPHA);

                float size = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.SANAE_STAR_SCREEN;
                float turn = (float)(t / 1000.0 * DanmakusuBalance.SANAE_SPIN_DEGREES_PER_SECOND * 0.25);
                f.stageBack.Add(StageTexture.BigStar, (f.stageMin + f.stageMax) * 0.5f, new Vector2(size), turn, DanmakusuBalance.SANAE_STAR_ALPHA * level, new Color4(0.45f, 1f, 0.55f, 1f));
            }
        }
    }
}
