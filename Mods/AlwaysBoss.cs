using System;
using System.Runtime.CompilerServices;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Always Boss: boss sections all through the song, from its first object to its last: the breaks and the spinners keep their own sections as they are, and everything between them is a section (a kiai's).
    /// It only marks the map (<see cref="SectionMarks"/>); the planner's <see cref="BulletPlanner.Sections"/> does the rest. Without a boss selection mod there are no bosses, as ever.
    /// </summary>
    public class DanmakusuModAlwaysBoss : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Always Boss";
        public override string Acronym => "AB";
        public override LocalisableString Description => "A boss all through the song: breaks and spinners keep their own sections, and everything between them is a boss section.";
        [Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.DifficultyReduction;

        public void ApplyToBeatmap(IBeatmap beatmap) => SectionMarks.MarkAlwaysBoss(beatmap);
    }

    /// <summary>What the Danmakusu mods have marked on a map (the map itself, as long as it lives).</summary>
    public static class SectionMarks
    {
        private static readonly ConditionalWeakTable<IBeatmap, object> always_boss = new ConditionalWeakTable<IBeatmap, object>();

        public static void MarkAlwaysBoss(IBeatmap beatmap) => always_boss.AddOrUpdate(beatmap, true);

        /// <summary>Is the Always Boss mod on for this map?</summary>
        public static bool AlwaysBoss(IBeatmap beatmap) => always_boss.TryGetValue(beatmap, out _);

        /// <summary>
        /// What the Danmakusu mods have changed of this map, as a few letters ("AB"), or null when it is the map as it is. What is counted of a changed map is shown while it is changed and never kept under
        /// the map's own MD5 (<see cref="Stars.DanmakusuStars"/>): with the mods off, song select shows the map's own counts and stars again.
        /// </summary>
        public static string? ChangedBy(IBeatmap beatmap) => AlwaysBoss(beatmap) ? "AB" : null;
    }
}
