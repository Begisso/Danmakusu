using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// A mod that chooses the bosses of the map itself. With neither boss mod on there is no boss at all: no boss sections, attacks or bars, only the bullets of the hit objects and the spinners (there is no No Boss mod
    /// any more: no mod is no boss). Boss Selection is put on when the ruleset is selected (see <see cref="DefaultBossMod"/>).
    /// </summary>
    public interface IBossSelection
    {
        /// <summary>The chosen bosses (<paramref name="fromColours"/> are the ones the combo colours would choose, they fill the places that have not been chosen).</summary>
        BossInfo[]? Choose(BossInfo[] fromColours);

        /// <summary>More than one boss is on the screen at once in every section: two, with half the bullets each, or (<see cref="Triple"/>) three.</summary>
        bool Double { get; }

        /// <summary>Three bosses are on the screen at once in every section, with a third of the bullets each.</summary>
        bool Triple { get; }
    }

    /// <summary>
    /// How many bosses the map has and who they are, chosen by the player (saved with the score, so a replay has the same bosses). They take the sections of the map in turn, one at a time; a section is cut when there are more
    /// bosses than sections. The slider goes as far as the map has room for one attack and one pause for every boss (see <see cref="BossPicker"/>, where the slider is: it knows the map).
    /// </summary>
    public class DanmakusuModBossSelection : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableToHUD, IBossSelection
    {
        public override string Name => "Boss Selection";
        public override string Acronym => "BS";
        public override LocalisableString Description => "Choose how many bosses the map has, and who they are, yourself instead of the combo colours choosing.";
        [System.Obsolete("Score multipliers are not used any more (the base member is obsolete); kept because the base class asks for it.")]
        public override double ScoreMultiplier => 1;
        public override ModType Type => ModType.DifficultyReduction;
        public override Type[] IncompatibleMods => [typeof(DanmakusuModDoubleBossSelection), typeof(DanmakusuModTripleBossSelection)];

        [SettingSource("Bosses", "The slider says how many bosses the map has. Click the heads to check them (the first one clicked is the first boss); click one again to take the check away.", SettingControlType = typeof(BossCountPicker))]
        public Bindable<string> Bosses { get; } = new Bindable<string>(BossPicker.ValueOf(1, []));

        public virtual bool Double => false;

        public virtual bool Triple => false;

        /// <summary>How few bosses there can be.</summary>
        protected virtual int Least => 1;

        public BossInfo[]? Choose(BossInfo[] fromColours) => ChooseFrom(BossPicker.Bosses(Bosses.Value), fromColours, Math.Max(BossPicker.CountOf(Bosses.Value), Least));

        /// <summary>The <paramref name="count"/> bosses of the map when <paramref name="picked"/> are checked (also for the HP bar preview in the list): what is not checked is what the combo colours choose, or else the next ones in the list.</summary>
        public static BossInfo[] ChooseFrom(IReadOnlyList<BossInfo> picked, BossInfo[] fromColours, int count)
        {
            var bosses = picked.Take(count).ToList();

            foreach (var boss in fromColours)
            {
                if (bosses.Count < count && !bosses.Contains(boss))
                    bosses.Add(boss);
            }

            int next = bosses.Count > 0 ? bosses[^1].Index : 0;

            while (bosses.Count < Math.Min(count, BossRoster.All.Count))
            {
                next = (next + 1) % BossRoster.All.Count;

                if (!bosses.Contains(BossRoster.All[next]))
                    bosses.Add(BossRoster.All[next]);
            }

            return bosses.ToArray();
        }

        private DrawableRuleset<OsuHitObject>? drawableRuleset;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset) => this.drawableRuleset = drawableRuleset;

        // (the map waits behind the boss menu until the player presses Start - unless at least one boss is checked in the mod's settings already: then the map starts at once with them, the user 2026-11-13.
        // How the player moves stays as it was last chosen)
        public void ApplyToHUD(HUDOverlay overlay)
        {
            if (BossPicker.Bosses(Bosses.Value).Count > 0)
                return;

            BossMenu.Attach(overlay, drawableRuleset, this, Bosses);
        }
    }

    /// <summary>Like <see cref="DanmakusuModBossSelection"/>, but two bosses attack together in every section, with half the bullets each: so the map has room for twice as many.</summary>
    public class DanmakusuModDoubleBossSelection : DanmakusuModBossSelection
    {
        public override string Name => "Double Boss Selection";
        public override string Acronym => "DS";
        public override LocalisableString Description => "Choose how many bosses the map has, and who they are: two of them attack together in every section, with half the bullets each.";
        public override Type[] IncompatibleMods => [typeof(DanmakusuModBossSelection)];
        public override bool Double => true;
        protected override int Least => 2;

        public DanmakusuModDoubleBossSelection()
        {
            // (the picker reads how few there can be from the default)
            Bosses.Default = Bosses.Value = BossPicker.ValueOf(2, []);
        }
    }

    /// <summary>Like <see cref="DanmakusuModBossSelection"/>, but three bosses attack together in every section, with a third of the bullets each: so the map has room for three times as many.</summary>
    public class DanmakusuModTripleBossSelection : DanmakusuModBossSelection
    {
        public override string Name => "Triple Boss Selection";
        public override string Acronym => "TS";
        public override LocalisableString Description => "Choose how many bosses the map has, and who they are: three of them attack together in every section, with a third of the bullets each.";
        public override Type[] IncompatibleMods => [typeof(DanmakusuModBossSelection)];
        public override bool Double => true;
        public override bool Triple => true;
        protected override int Least => 3;

        public DanmakusuModTripleBossSelection()
        {
            // (the picker reads how few there can be from the default)
            Bosses.Default = Bosses.Value = BossPicker.ValueOf(3, []);
        }
    }
}
