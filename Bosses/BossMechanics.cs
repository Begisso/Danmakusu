using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The place where the special mechanic of each boss goes (see <see cref="BossMechanic"/>): called every frame while one of the boss's sections is on.
    /// Every boss has the same interface (colour signature, sprite, patterns, mechanic); the mechanics themselves are stubs that are filled in boss by boss.
    /// </summary>
    public static class BossMechanics
    {
        public static void Update(BossMechanic mechanic, double now, BossPeriod period)
        {
            switch (mechanic)
            {
                // (Seija Kijin's mirrored screen is done by DanmakusuPlayfield (it turns) and BulletField (MirrorAngleAt))
                default:
                    break;
            }
        }
    }
}
