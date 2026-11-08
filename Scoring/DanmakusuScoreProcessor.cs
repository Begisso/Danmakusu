using System;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Danmakusu.Scoring
{
    /// <summary>
    /// Every object is auto-hit, so the only way to lose score quality is getting hit by a bullet (or a slider).
    ///  - every damaging hit counts as a miss and breaks the combo;
    ///  - accuracy = (all standard osu! objects - misses) / all standard osu! objects, never below 0;
    ///  - the rank letter uses osu!catch's accuracy thresholds (X 100%, S 98%, A 94%, B 90%, C 85%, D below).
    /// The base processor still runs normally; its accuracy/rank are overwritten every frame.
    /// </summary>
    public partial class DanmakusuScoreProcessor : OsuScoreProcessor
    {
        private int totalObjects;
        private int misses;

        public int Misses => misses;

        public override void ApplyBeatmap(IBeatmap beatmap)
        {
            totalObjects = beatmap.HitObjects.Count;
            base.ApplyBeatmap(beatmap);
        }

        /// <summary>A bullet (or slider) hit the player: counts as a miss and breaks the combo.</summary>
        public void AddMiss()
        {
            misses++;
            Combo.Value = 0;
        }

        /// <summary>Undoes <see cref="AddMiss"/> (replay seeks back past a hit). The combo is not restored.</summary>
        public void RemoveMiss()
        {
            if (misses > 0)
                misses--;
        }

        protected override void Update()
        {
            base.Update();

            // once failed, keep the F the base processor assigned.
            if (Rank.Value == ScoreRank.F)
                return;

            double accuracy = currentAccuracy();

            if (Accuracy.Value != accuracy)
                Accuracy.Value = accuracy;

            var rank = currentRank(accuracy);

            if (Rank.Value != rank)
                setRank(rank);
        }

        private double currentAccuracy() => totalObjects <= 0 ? 1 : Math.Max(0, (totalObjects - misses) / (double)totalObjects);

        private ScoreRank currentRank(double accuracy)
        {
            var rank = rankFromAccuracy(accuracy);

            foreach (var mod in Mods.Value.OfType<IApplicableToScoreProcessor>())
                rank = mod.AdjustRank(rank, accuracy);

            return rank;
        }

        /// <summary>
        /// The score that goes to the results screen and the database. The base processor only ever saw "Great" judgements (everything is
        /// auto-hit), so left alone it reports 100% accuracy and an SS no matter how many bullets hit; and it is also the last to touch
        /// accuracy and rank when the final judgement lands, which can be after <see cref="Update"/>. So the real values are written here,
        /// and the bullet hits are moved from the Great count to the Miss count of the statistics, so anything calculated from those agrees.
        /// </summary>
        public override void PopulateScore(ScoreInfo score)
        {
            base.PopulateScore(score);

            if (misses > 0)
            {
                score.Statistics.TryGetValue(HitResult.Great, out int great);
                int moved = Math.Min(misses, great);

                if (moved > 0)
                {
                    score.Statistics[HitResult.Great] = great - moved;
                    score.Statistics.TryGetValue(HitResult.Miss, out int missed);
                    score.Statistics[HitResult.Miss] = missed + moved;
                }
            }

            double accuracy = currentAccuracy();
            score.Accuracy = accuracy;

            // a failed score stays an F
            if (score.Rank != ScoreRank.F && Rank.Value != ScoreRank.F)
                score.Rank = currentRank(accuracy);
        }

        /// <summary>
        /// <c>Rank</c> is exposed read-only (<see cref="IBindable{T}"/>), but it is the base processor's own bindable underneath,
        /// so write to that: directly if it is a plain <see cref="Bindable{T}"/>, otherwise through its backing field.
        /// </summary>
        private void setRank(ScoreRank rank)
        {
            if (Rank is Bindable<ScoreRank> bindable)
            {
                bindable.Value = rank;
                return;
            }

            for (var type = GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.GetValue(this) is Bindable<ScoreRank> candidate)
                    {
                        candidate.Value = rank;
                        return;
                    }
                }
            }
        }

        /// <summary>osu!catch's thresholds.</summary>
        private static ScoreRank rankFromAccuracy(double accuracy)
        {
            if (accuracy >= 1)
                return ScoreRank.X;

            if (accuracy >= 0.98)
                return ScoreRank.S;

            if (accuracy >= 0.94)
                return ScoreRank.A;

            if (accuracy >= 0.90)
                return ScoreRank.B;

            if (accuracy >= 0.85)
                return ScoreRank.C;

            return ScoreRank.D;
        }
    }
}
