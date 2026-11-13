using System.Collections.Generic;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.UI;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Danmakusu's Autoplay (in osu!'s place, same name and acronym): the map plays itself as it always does, and the character dodges by itself as little as it can (see <c>BulletField.AutoPilot</c>):
    /// it stands still while nothing would touch it, walks the shortest safe way when something would (with keyboard controls at the keyboard's speed), and freezes and teleports - Space with the keyboard -
    /// only when no walk is safe. Its replay is only a cursor that stays in the middle: the field moves the character, and the skinned cursor is drawn on it.
    /// </summary>
    public class DanmakusuModAutoplay : OsuModAutoplay
    {
        public override LocalisableString Description => "Watch the character dodge by itself: it moves as little as it can, and freezes and teleports only when it has to.";

        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(new Replay { Frames = { new OsuReplayFrame(0, OsuPlayfield.BASE_SIZE / 2) } }, new ModCreatedUser { Username = "Autoplay" });
    }
}
