using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Scoring;

namespace osu.Game.Rulesets.Danmakusu.Scoring
{
    /// <summary>
    /// Judgements (auto-hit circles, sliders, spinners) give no health at all, and time no longer drains HP: it regenerates it, at the same
    /// speed on every map (<see cref="DanmakusuBalance.HP_REGENERATION_PER_SECOND"/>). Bullet damage is applied separately by <c>BulletField</c>.
    /// </summary>
    public partial class DanmakusuHealthProcessor : OsuHealthProcessor
    {
        private double gameplayEndTime = double.MaxValue;

        public DanmakusuHealthProcessor(double drainStartTime)
            : base(drainStartTime, drainLenience: 1) // 1 = the built-in drain is switched off
        {
        }

        public override void ApplyBeatmap(IBeatmap beatmap)
        {
            if (beatmap.HitObjects.Count > 0)
                gameplayEndTime = beatmap.HitObjects[^1].GetEndTime();

            base.ApplyBeatmap(beatmap);
        }

        /// <summary>
        /// Every object is auto-hit, so judgements must never give (or take) health. Only time-based regeneration restores HP.
        /// </summary>
        protected override double GetHealthIncreaseFor(JudgementResult result) => 0;

        protected override void Update()
        {
            base.Update();

            if (HasFailed)
                return;

            double last = Math.Clamp(Time.Current - Time.Elapsed, DrainStartTime, gameplayEndTime);
            double current = Math.Clamp(Time.Current, DrainStartTime, gameplayEndTime);

            if (current > last)
                Health.Value += DanmakusuBalance.HP_REGENERATION_PER_SECOND / 1000 * (current - last);
        }
    }
}
