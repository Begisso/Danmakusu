namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>The sounds that belong to a boss's mechanic (the samples are embedded in Resources/Samples).</summary>
    public static class BossSounds
    {
        /// <summary>The sample that leads into every attack of a boss without lasers: power_up unless the boss has a sound of her own.</summary>
        public static string WarningOf(BossMechanic mechanic) => mechanic switch
        {
            BossMechanic.Unconscious => "koishi_ring", // (an old round telephone rings)
            _ => "power_up",
        };
    }
}
