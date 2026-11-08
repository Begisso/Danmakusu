using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Shinmyoumaru Sukuna: in her attacks every bullet and every beam is as big as its distance from her says: half its size at the edge of her magic circle (and inside it), gradually up to twice its size at the edge of the screen (and
        // beyond it), by how far it is from her against how far from the nearest edge, and the further, the more it glows. The same goes for the hitboxes. During the warning before an attack the sizes and the glow change to those numbers (smoothly) and everything shakes (1-2 px);
        // when the attack is over they change back over as long. A sound (Kyouko's walls) plays at the start of every attack. The act only says how strong it is (BulletField.sukunaLevel); the field sizes what it draws and hits.
        // ======================================================================================================================================================

        private sealed class InchlingAct : StageAct
        {
            private readonly (double Start, double End)[] attacks;
            private readonly double warn;

            public InchlingAct(BossPeriod period)
            {
                Period = period;
                Start = period.Start;
                End = period.End;

                var list = new System.Collections.Generic.List<(double, double)>();

                foreach (var attack in BossOps.Attacks(period))
                {
                    if (attack.End > attack.Start)
                        list.Add(attack);
                }

                attacks = list.ToArray();
                warn = Math.Max(period.ShootStart - period.Start, 300); // (as long as the warning before her first attack: the same before every other)
            }

            /// <summary>How strongly the sizes apply at <paramref name="t"/> (0..1) and whether everything shakes (the warning before an attack).</summary>
            private float levelAt(double t, out bool shaking)
            {
                float best = 0f;
                shaking = false;

                foreach (var (start, end) in attacks)
                {
                    float up = smoothStep((t - (start - warn)) / warn);
                    float down = 1f - smoothStep((t - end) / warn);

                    best = Math.Max(best, Math.Min(up, down));

                    if (t >= start - warn && t < start)
                        shaking = true;
                }

                return best;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                float level = levelAt(now, out bool shaking);

                if (level <= f.sukunaLevel && !shaking)
                    return;

                f.sukunaLevel = Math.Max(f.sukunaLevel, level);
                f.sukunaShaking |= shaking;
                f.sukunaCentre = f.bossPositionOf(Period);
            }
        }

        /// <summary>0..1: how strongly Sukuna's sizes apply right now (0 = not at all), where she stands, and whether everything shakes.</summary>
        private float sukunaLevel;

        private Vector2 sukunaCentre;
        private bool sukunaShaking;

        /// <summary>How many times as big a bullet or a beam is at <paramref name="p"/> right now: 0.5 at the edge of her magic circle to 2 at the edge of the screen, blended in by how strong the effect is.</summary>
        private float sukunaGrow(Vector2 p)
        {
            float u = sukunaDistance(p);
            float size = DanmakusuBalance.SUKUNA_MIN_SIZE + (DanmakusuBalance.SUKUNA_MAX_SIZE - DanmakusuBalance.SUKUNA_MIN_SIZE) * u;

            return 1f + (size - 1f) * sukunaLevel;
        }

        /// <summary>How much a bullet or a beam glows at <paramref name="p"/> right now: nothing at the edge of her magic circle, more and more with the distance.</summary>
        private float sukunaGlowAt(Vector2 p)
            => DanmakusuBalance.SUKUNA_BLOOM * sukunaLevel * sukunaDistance(p);

        /// <summary>
        /// 0 at the edge of her magic circle (and inside it), 1 at the edge of the screen (and beyond it), and in between by how far it is from her against how far from the nearest edge: halfway between the two is 0.5,
        /// wherever she stands.
        /// </summary>
        private float sukunaDistance(Vector2 p)
        {
            float fromHer = Math.Max(Vector2.Distance(p, sukunaCentre) - DanmakusuBalance.MAGIC_CIRCLE_RADIUS, 0f);
            float toEdge = Math.Max(Math.Min(Math.Min(p.X - stageMin.X, stageMax.X - p.X), Math.Min(p.Y - stageMin.Y, stageMax.Y - p.Y)), 0f);

            return fromHer + toEdge > 1e-3f ? fromHer / (fromHer + toEdge) : 1f;
        }
    }
}
