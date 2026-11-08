using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The combo colours that replace the ones of the map while its bosses are chosen by a boss selection mod: the colours of the chosen bosses (their colour signatures, one boss after the other), so that the hit circles and
    /// the bullets match the bosses. They are kept per beatmap; <see cref="DanmakusuSkinTransformer"/> hands them out in place of the skin's combo colours.
    /// </summary>
    public static class BossColours
    {
        private sealed class Box
        {
            public Color4[]? Colours;
            public Color4[]? Signatures;
            public Section[]? Sections;
        }

        /// <summary>A boss section of the map: from its warning to its end, which boss (its place in the list) and which place on the screen (rank).</summary>
        public readonly record struct Section(double Start, double End, int Rank, int Slot);

        private static readonly ConditionalWeakTable<IBeatmap, Box> boxes = new ConditionalWeakTable<IBeatmap, Box>();

        /// <summary>Uses these colours for the beatmap (null = the colours of the map and the skin again).</summary>
        public static void Set(IBeatmap beatmap, Color4[]? colours) => boxes.GetOrCreateValue(beatmap).Colours = colours is { Length: > 0 } ? colours : null;

        public static Color4[]? Get(IBeatmap beatmap) => boxes.TryGetValue(beatmap, out var box) ? box.Colours : null;

        /// <summary>The boss sections of the beatmap and the colour signatures of its bosses (four each, in the order of their places): a circle or slider in a section takes the colours of its boss.</summary>
        public static void SetSections(IBeatmap beatmap, Color4[] signatures, Section[] sections)
        {
            var box = boxes.GetOrCreateValue(beatmap);
            box.Signatures = signatures.Length > 0 ? signatures : null;
            box.Sections = sections.Length > 0 ? sections : null;
        }

        /// <summary>The colour a circle or slider of combo colour <paramref name="comboIndex"/> at <paramref name="time"/> has in its boss section (null: it is in none).</summary>
        public static Color4? InSection(IBeatmap beatmap, double time, int comboIndex)
        {
            if (!boxes.TryGetValue(beatmap, out var box) || box.Signatures == null || box.Sections == null)
                return null;

            int index = SectionColour(box.Sections, time, comboIndex);
            return index >= 0 && index < box.Signatures.Length ? box.Signatures[index] : null;
        }

        /// <summary>
        /// Which of the bosses' colours (their place times four, and one of their four) a thing of combo colour <paramref name="comboIndex"/> at <paramref name="time"/> takes: the colours of the boss of the section it is in, one after
        /// the other as the combos go; with two or three bosses on the screen the combos take turns between them. -1: it is in no section.
        /// </summary>
        public static int SectionColour(Section[] sections, double time, int comboIndex)
        {
            int slot0 = -1, slot1 = -1, slot2 = -1;

            foreach (var section in sections)
            {
                if (time < section.Start || time > section.End)
                    continue;

                switch (section.Rank)
                {
                    case 0: slot0 = section.Slot; break;
                    case 1: slot1 = section.Slot; break;
                    case 2: slot2 = section.Slot; break;
                }
            }

            int count = 0;
            Span<int> slots = stackalloc int[3];

            foreach (int slot in new[] { slot0, slot1, slot2 })
            {
                if (slot >= 0 && slots[..count].IndexOf(slot) < 0)
                    slots[count++] = slot;
            }

            if (count == 0)
                return -1;

            int index = Math.Abs(comboIndex);
            return slots[index % count] * BossMatcher.COLOURS_PER_BOSS + index / count % BossMatcher.COLOURS_PER_BOSS;
        }

        /// <summary>The colour signatures of these bosses, one after the other.</summary>
        public static Color4[] Of(IEnumerable<BossInfo> bosses)
        {
            var list = new List<Color4>();

            foreach (var boss in bosses)
                list.AddRange(boss.Colours);

            return list.ToArray();
        }
    }

    /// <summary>Takes the combo colours of the beatmap from <see cref="BossColours"/> (when it has some) and leaves everything else to the skin transformer of osu!standard.</summary>
    public sealed class DanmakusuSkinTransformer : SkinTransformer
    {
        private readonly IBeatmap beatmap;

        public DanmakusuSkinTransformer(ISkin skin, IBeatmap beatmap)
            : base(skin)
        {
            this.beatmap = beatmap;
        }

        public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        {
            // (a circle or slider in a boss section takes the colours of its boss)
            if (lookup is SkinComboColourLookup inSection && inSection.Combo is HitObject hitObject && BossColours.InSection(beatmap, hitObject.StartTime, inSection.ColourIndex) is Color4 own)
                return SkinUtils.As<TValue>(new Bindable<Color4>(own));

            var colours = BossColours.Get(beatmap);

            if (colours != null)
            {
                switch (lookup)
                {
                    case SkinComboColourLookup combo:
                        return SkinUtils.As<TValue>(new Bindable<Color4>(colours[combo.ColourIndex % colours.Length]));

                    case GlobalSkinColours global when global == GlobalSkinColours.ComboColours:
                        return SkinUtils.As<TValue>(new Bindable<IReadOnlyList<Color4>?>(colours));
                }
            }

            return base.GetConfig<TLookup, TValue>(lookup);
        }
    }
}
