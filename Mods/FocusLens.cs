using System;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Danmakusu.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Focus Lens (2026-11-21): the camera follows the player: the character (or, while it is frozen or stunned, its ghost where the mouse is) always stays in the middle of the screen and the playfield moves
    /// round it instead, looking as it always does (2026-11-22, the user: "Playfield visually remains the same, it moves instead of a centered player, that's it"; it was shown 2x - 10x as big, the "Zoom"
    /// setting). Its edges are a thin white line, and nothing past them is seen; the character stays inside them. Only the look otherwise: the character moves as it always does (see
    /// DanmakusuPlayfield.updateFocus).
    /// </summary>
    public class DanmakusuModFocusLens : Mod, IApplicableToDrawableRuleset<OsuHitObject>
    {
        public override string Name => "Focus Lens";
        public override string Acronym => "FS";
        public override LocalisableString Description => "The camera follows you: you stay in the middle of the screen and the playfield moves round you. Nothing is seen past its edges.";

        [Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;

        public override ModType Type => ModType.DifficultyReduction; // (so it is in Danmakusu's own column of the mod screen, as the others: see DanmakusuModColumn)

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            if (drawableRuleset.Playfield is DanmakusuPlayfield playfield)
                playfield.FocusZoom = 1f; // (as near as without it: the playfield only moves)
        }
    }
}
