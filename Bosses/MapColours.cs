using System;
using System.Collections.Generic;
using System.Reflection;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The combo colours of the map itself (the [Colours] section of its .osu file), whatever the player's settings say:
    /// the beatmap's skin layer is asked directly, so "ignore beatmap skin / colours" and the skin of the player do not matter.
    /// Empty if the map has no colours of its own (or if they cannot be found: the beatmap's storage is reached by reflection in some places,
    /// so this does not depend on the exact shape of osu!'s API).
    /// </summary>
    public static class MapColours
    {
        public static IReadOnlyList<Color4> Read(object? workingBeatmap)
        {
            if (workingBeatmap == null)
                return [];

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = workingBeatmap.GetType();

            // 1. the beatmap's skin layer, asked for its combo colours like the game does for hit circles
            try
            {
                if (type.GetProperty("Skin", flags)?.GetValue(workingBeatmap) is ISkin skin)
                {
                    var colours = skin.GetConfig<GlobalSkinColours, IReadOnlyList<Color4>>(GlobalSkinColours.ComboColours)?.Value;

                    if (colours != null && colours.Count > 0)
                        return colours;

                    // 2. the same list straight from the skin's configuration
                    var fromConfiguration = fromObject(findMember(skin, "Configuration"));

                    if (fromConfiguration.Count > 0)
                        return fromConfiguration;
                }
            }
            catch
            {
                // (try the next way)
            }

            // 3. the decoded beatmap itself
            try
            {
                var beatmap = type.GetProperty("Beatmap", flags)?.GetValue(workingBeatmap);
                var fromBeatmap = fromObject(beatmap);

                if (fromBeatmap.Count > 0)
                    return fromBeatmap;
            }
            catch
            {
                // (no colours)
            }

            return [];
        }

        private static object? findMember(object? target, string name)
        {
            if (target == null)
                return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);

                if (property != null)
                    return property.GetValue(target);

                var field = t.GetField(name, flags | BindingFlags.DeclaredOnly);

                if (field != null)
                    return field.GetValue(target);
            }

            return null;
        }

        /// <summary>The list of colours an object keeps in a property called ComboColours / CustomComboColours, if any.</summary>
        private static IReadOnlyList<Color4> fromObject(object? target)
        {
            foreach (string name in new[] { "ComboColours", "CustomComboColours" })
            {
                try
                {
                    if (findMember(target, name) is IEnumerable<Color4> colours)
                    {
                        var list = new List<Color4>(colours);

                        if (list.Count > 0)
                            return list;
                    }
                }
                catch
                {
                    // (next name)
                }
            }

            return [];
        }
    }
}
