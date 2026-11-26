using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Mugetsu: in her attacks every bullet on the screen goes round her, as fast round her as it flies, the even bullets of a ring one way and the odd ones the other (Bullets/Swirl.cs works out where they are).
        // Every bullet goes round a middle that does not move: where she stood when it was fired in the attack, or, for what was on the screen already, where she stood when the attack began. The act takes that place as
        // an attack begins and keeps where she is up to date while it is on (what is fired takes it then); when time goes back before an attack, its place is taken again.
        // Since 2026-11-25 for MUGETSU_WARN_BEATS before each attack a gold arrow beside half the bullets of the rings shows the way each will go round her (worked out with the bullets: addMugetsuArrow).
        // ======================================================================================================================================================

        private sealed class SwirlAct : StageAct
        {
            private readonly Swirl swirl;
            private readonly int[] attacks; // (hers: the ones of the vortex that are in this section)

            public SwirlAct(BossPeriod period, Swirl swirl)
            {
                Period = period;
                this.swirl = swirl;
                Start = period.ShootStart;
                End = period.End;

                var list = new List<int>();

                for (int k = 0; k < swirl.Starts.Length; k++)
                {
                    if (swirl.Ranks[k] == period.Rank && swirl.Starts[k] >= period.ShootStart - 1 && swirl.Starts[k] < period.End)
                        list.Add(k);
                }

                attacks = list.ToArray();
            }

            public override void Reset(BulletField f, double time)
            {
                foreach (int k in attacks)
                {
                    if (swirl.Starts[k] > time)
                        swirl.Placed[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                double beat = System.Math.Max(Period.BeatLength, 1);
                double warn = beat * DanmakusuBalance.MUGETSU_WARN_BEATS;

                foreach (int k in attacks)
                {
                    // (the warning before it: arrows beside the bullets show which way each will go round her - 2026-11-25, "arrows warn about spin". They come in over half a beat and blink over the last)
                    if (now >= swirl.Starts[k] - warn && now < swirl.Starts[k])
                    {
                        float level = smoothStep((now - (swirl.Starts[k] - warn)) / (beat * 0.5));

                        if (swirl.Starts[k] - now < beat)
                            level *= 0.6f + 0.4f * osuTK.MathHelper.Clamp((float)System.Math.Cos((swirl.Starts[k] - now) / beat * System.Math.PI * 4), -1f, 1f);

                        f.warnMugetsu(now, level, f.bossVisualOf(Period));
                    }

                    if (now < swirl.Starts[k])
                        continue;

                    // (as it begins: what is on the screen already goes round where she stands now)
                    if (!swirl.Placed[k])
                    {
                        swirl.Grabbed[k] = f.bossVisualOf(Period);
                        swirl.Placed[k] = true;
                    }

                    // (while it is on: what is fired goes round where she is then)
                    if (now < swirl.Ends[k])
                        swirl.Centres[k] = f.bossVisualOf(Period);
                }
            }
        }

        // ---- the arrows that warn of her vortex (2026-11-25): worked out with the bullets, as Kokoro's ----

        private double mugetsuWarnTime = double.NaN;
        private float mugetsuWarnLevel;
        private osuTK.Vector2 mugetsuWarnCentre;
        private readonly List<(osuTK.Vector2 At, float Size, float Degrees)> mugetsuArrows = new List<(osuTK.Vector2, float, float)>();

        /// <summary>A warning of her vortex is on at <paramref name="now"/>, <paramref name="level"/> strong, round <paramref name="centre"/> (where she stands).</summary>
        private void warnMugetsu(double now, float level, osuTK.Vector2 centre)
        {
            if (mugetsuWarnTime == now && mugetsuWarnLevel >= level)
                return;

            mugetsuWarnTime = now;
            mugetsuWarnLevel = level;
            mugetsuWarnCentre = centre;
        }

        /// <summary>How strong the warning of her vortex is at <paramref name="t"/> (0: none).</summary>
        private float mugetsuWarning(double t) => !failed && System.Math.Abs(t - mugetsuWarnTime) < 1 ? mugetsuWarnLevel : 0f;

        /// <summary>An arrow beside bullet <paramref name="bullet"/> of a ring at <paramref name="position"/>, flying <paramref name="direction"/>: the way it will go once the vortex has it (its own way and round her, the even ones of a ring one way, the odd ones the other - as Swirl.Apply).</summary>
        private void addMugetsuArrow(osuTK.Vector2 position, osuTK.Vector2 direction, int bullet, float radius)
        {
            osuTK.Vector2 outward = position - mugetsuWarnCentre;
            float distance = outward.Length;

            if (distance < 1e-3f)
                return;

            float sign = bullet % 2 == 0 ? DanmakusuBalance.MUGETSU_SWIRL : -DanmakusuBalance.MUGETSU_SWIRL;
            osuTK.Vector2 way = direction + new osuTK.Vector2(-outward.Y, outward.X) / distance * sign;

            if (way.LengthSquared < 1e-6f)
                return;

            way.Normalize();
            float arrow = System.Math.Max(radius, 3f) * DanmakusuBalance.KOKORO_ARROW_SIZE;
            mugetsuArrows.Add((position + way * (radius + arrow), arrow * 2.2f, System.MathF.Atan2(way.Y, way.X) * 180f / System.MathF.PI));
        }

        private void drawMugetsuArrows(double t)
        {
            if (mugetsuArrows.Count == 0)
                return;

            float level = mugetsuWarning(t);
            var gold = new osuTK.Graphics.Color4(1f, 0.84f, 0.36f, 1f); // (her pale gold)

            foreach (var (at, size, degrees) in mugetsuArrows)
                stageRipple.Add(StageTexture.SeijaArrow, at, new osuTK.Vector2(size), degrees, level, gold);
        }
    }
}
