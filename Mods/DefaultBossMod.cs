using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// Turns the Boss Selection mod (one boss) on when this ruleset becomes the selected one (when osu! starts with it, and every time the player switches to it), unless a boss mod is on already.
    /// osu! has no way for a ruleset to say which mods are on by default, so this rides along in the ruleset's icon (osu! loads the icons when it starts: the toolbar shows them all).
    /// Several icons can be alive at once: they share <see cref="wasSelected"/>, so the mod is turned on once per switch.
    /// </summary>
    public partial class DefaultBossMod : Component
    {
        private static bool wasSelected;

        private readonly Bindable<RulesetInfo> ruleset = new Bindable<RulesetInfo>();
        private readonly Bindable<IReadOnlyList<Mod>> mods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());
        private bool bound;

        public DefaultBossMod()
        {
            AlwaysPresent = true; // (it must be updated to run what it has scheduled)
        }

        [BackgroundDependencyLoader(true)]
        private void load(Bindable<RulesetInfo>? gameRuleset, Bindable<IReadOnlyList<Mod>>? gameMods)
        {
            if (gameRuleset == null || gameMods == null)
                return;

            ruleset.BindTo(gameRuleset);
            mods.BindTo(gameMods);
            bound = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (bound)
                ruleset.BindValueChanged(_ => Scheduler.AddOnce(check), true);
        }

        private void check()
        {
            bool selected = ruleset.Value?.ShortName == DanmakusuRuleset.SHORT_NAME;
            bool switchedTo = selected && !wasSelected;
            wasSelected = selected;

            // (not while the mods may not change: in a play, for instance)
            if (!switchedTo || mods.Disabled)
                return;

            var current = mods.Value ?? Array.Empty<Mod>();

            // (the player has chosen how the bosses are chosen already)
            if (current.Any(mod => mod is IBossSelection))
                return;

            mods.Value = current.Append(new DanmakusuModBossSelection()).ToArray();
        }
    }
}
