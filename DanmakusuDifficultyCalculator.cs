using System;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.Mods;
using osu.Game.Rulesets.Danmakusu.Stars;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// The star rating is how dense the bullets are together with how much they make the player move to safer places (<see cref="StarRating"/>: what the map's own circles, sliders and spinners shoot, without bosses),
    /// at the speed the mods play it, and with the circle size and approach rate they give. Everything else (aim, speed... and so the pp, which the performance calculator works out from them) is osu!standard's.
    /// </summary>
    public class DanmakusuDifficultyCalculator : OsuDifficultyCalculator
    {
        public DanmakusuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            var attributes = base.CreateDifficultyAttributes(beatmap, mods, skills);

            // (only for the whole map: the rating as the map goes along, object by object, for a pp counter, would plan the map again for every object; it keeps osu!'s)
            if (!ReferenceEquals(beatmap, Beatmap) || beatmap.HitObjects.Count == 0)
                return attributes;

            try
            {
                var rated = StarRating.Rate(beatmap, ModUtils.CalculateRateWithMods(mods));
                attributes.StarRating = rated.Stars;

                // (song select sorts by it and shows the counts: kept when no mod changes it - the boss selection mods do not. How many bosses it has room for is left to the background rating, which reads the song)
                if (mods.All(mod => mod is IBossSelection))
                    DanmakusuStars.Put(beatmap.BeatmapInfo?.MD5Hash, new DanmakusuStars.Rating(rated.Stars, rated.Bullets, rated.Beams, 0));
            }
            catch (Exception e)
            {
                // (a map the planner cannot take keeps osu!'s stars)
                Logger.Log($"Danmakusu: no movement stars for {beatmap.BeatmapInfo}: {e.Message}", level: LogLevel.Important);
            }

            return attributes;
        }
    }
}
