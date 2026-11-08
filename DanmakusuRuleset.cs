using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Mods;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Game.Rulesets.Danmakusu.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// Danmakusu: osu!standard gameplay + bullet rings.
    ///
    /// This deliberately wraps <see cref="OsuRuleset"/> instead of inheriting it: OsuRuleset implements ILegacyRuleset
    /// with LegacyID 0, so a subclass would be registered as a second copy of osu!standard (online id 0).
    /// </summary>
    public partial class DanmakusuRuleset : Ruleset
    {
        private readonly OsuRuleset osu = new OsuRuleset();

        public DanmakusuRuleset()
        {
            // (maps of mania, taiko and catch are offered in Danmakusu too: osu! offers only osu! maps to convert. Once, when osu! first loads the ruleset, before song select asks)
            OtherModesInSongSelect.Apply();

            // (and song select sorts, groups and filters Danmakusu by its own stars, and by bullets, beams and most bosses: Stars/SongSelectStars.cs)
            Stars.SongSelectStars.Apply();

            // (and shows the counts of bullets, beams and bosses as bars, counted in the background: Stars/SongSelectCounts.cs)
            Stars.SongSelectCounts.Apply();

            // (and the mod select screen has a column of Danmakusu's own mods: Mods/DanmakusuModColumn.cs)
            ModsColumnInSongSelect.Apply();

            // (and its mods are purple wherever osu! draws them, not Difficulty Reduction's green: Mods/PurpleMods.cs)
            PurpleMods.Apply();
        }

        public override string Description => "Danmakusu";
        public const string SHORT_NAME = "danmakusu";

        // (osu! stores the ID as a database key: changing it makes osu! see a new ruleset, and the scores, key bindings and settings of the old ID stay behind with it. It was "osutouhou" before.)
        public override string ShortName => SHORT_NAME;
        public override string PlayingVerb => "Dodging bullets";

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            => new DrawableDanmakusuRuleset(this, beatmap, mods);

        // --- everything below is osu!standard's, so judgement windows (OD), hit results, HP drain and scoring are identical ---
        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new DanmakusuBeatmapConverter(beatmap, this); // (osu!'s for osu! maps; mania, taiko and catch maps are made into circles and sliders of their own)
        public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap) => new DanmakusuBeatmapProcessor(beatmap); // (osu!'s; a catch map's falling objects are kept longer, see there)
        public override ScoreProcessor CreateScoreProcessor() => new DanmakusuScoreProcessor();
        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new DanmakusuHealthProcessor(drainStartTime);
        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new DanmakusuDifficultyCalculator(RulesetInfo, beatmap); // (the stars by bullet density, the rest osu!'s)
        public override PerformanceCalculator CreatePerformanceCalculator() => new OsuPerformanceCalculator();
        public override IEnumerable<Mod> GetModsFor(ModType type)
            => type == ModType.DifficultyReduction ? new Mod[] { new DanmakusuModBossSelection(), new DanmakusuModDoubleBossSelection(), new DanmakusuModTripleBossSelection() }.Concat(osu.GetModsFor(type)) : osu.GetModsFor(type); // (the mods of this ruleset are of the Difficulty Reduction type, first there; the mod select screen shows them in a column of their own, see DanmakusuModColumn)
        public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => new DanmakusuSkinTransformer(osu.CreateSkinTransformer(skin, beatmap) ?? skin, beatmap); // skinned cursor, approach circles, hit circles...
        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new OsuReplayFrame();
        public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new OsuRulesetConfigManager(settings, RulesetInfo);
        public override RulesetSettingsSubsection CreateSettings() => new OsuSettingsSubsection(this);
        public override IEnumerable<HitResult> GetValidHitResults() => osu.GetValidHitResults();
        public override LocalisableString GetDisplayNameForHitResult(HitResult result) => osu.GetDisplayNameForHitResult(result);
        public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) => osu.CreateStatisticsForScore(score, playableBeatmap);

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0)
        {
            var bindings = osu.GetDefaultKeyBindings(variant);

            // Key 1 / key 2 (the two action buttons: Z / X and the mouse buttons by default) are the freeze keys in this ruleset.
            // The number keys 1 and 2 are added as well. (Defaults only apply to a fresh key binding database; existing bindings are kept.)
            return variant == 0
                ? bindings.Concat(new[]
                {
                    new KeyBinding(InputKey.Number1, OsuAction.LeftButton),
                    new KeyBinding(InputKey.Number2, OsuAction.RightButton),
                })
                : bindings;
        }

        public override Drawable CreateIcon() => new Icon();

        /// <summary>
        /// The ruleset icon: the usual outlined circle with danmaku inside: a big centre bullet and a ring of five arrowhead bullets
        /// (the same shape the hitwhistle bullets have in this ruleset) pointing outwards.
        /// It needs a real size: osu! scales whatever icon it gets to fit the slot it is shown in (ConstrainedIconContainer), which only
        /// works if the icon reports a non-zero size. Like the built-in icons it is a square that the outline fills completely.
        /// </summary>
        public partial class Icon : CompositeDrawable
        {
            private const float size = 32;

            /// <summary>Outline thickness relative to the icon size, close to the built-in ruleset icons.</summary>
            private const float outline_thickness = 3;

            private const int ring_bullets = 5;
            private const float ring_radius = 9.2f;

            /// <summary>Bounding radius of one arrowhead (its tip is a bit less than this far from its centre).</summary>
            private const float arrowhead_radius = 2.6f;

            public Icon()
            {
                Size = new Vector2(size);

                var children = new List<Drawable>
                {
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderColour = Color4.White,
                        BorderThickness = outline_thickness,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(6.8f),
                        Colour = Color4.White,
                    },
                };

                // one ring of arrowheads, the first pointing straight up
                for (int i = 0; i < ring_bullets; i++)
                {
                    float angle = -MathF.PI / 2 + i * MathF.Tau / ring_bullets;

                    children.Add(new ArrowheadSprite
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(arrowhead_radius * 2),
                        Position = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring_radius,
                        Rotation = angle * 180f / MathF.PI, // the texture's tip points along +x
                        Colour = Color4.White,
                    });
                }

                // (not drawn: turns the One Boss Selection mod on when this ruleset is selected)
                children.Add(new DefaultBossMod());

                // (not drawn: rates the library with the Danmakusu stars in the background, for song select's sort)
                children.Add(new Stars.StarFiller());

                InternalChildren = children.ToArray();
            }

            /// <summary>The arrowhead texture of the bullets, solid white.</summary>
            private partial class ArrowheadSprite : Sprite
            {
                [BackgroundDependencyLoader]
                private void load(IRenderer renderer)
                {
                    Texture = BulletRenderer.CreateArrowheadTexture(renderer, false);
                }
            }
        }

        // Leave this line intact. It bakes the correct ruleset API version into the dll on each build.
        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;
    }
}
