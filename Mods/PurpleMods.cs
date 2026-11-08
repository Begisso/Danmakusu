using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Logging;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// This ruleset's own mods (the boss selections) are drawn in the purple of their column (<see cref="DanmakusuModColumn.ACCENT"/>) wherever osu! draws a mod: their panels in the mod screen, the badges
    /// on them, and their icons (the "incompatible with" of a tooltip, the chosen mods under song select, results, scores). osu! colours a mod by its type, and to osu! these stay Difficulty Reduction
    /// (green); only the colour is changed. Done with Harmony, like <see cref="ModsColumnInSongSelect"/>: if osu! has changed where these are, they stay green, and the log says so.
    /// </summary>
    public static class PurpleMods
    {
        private static readonly object gate = new object();
        private static bool tried;

        /// <summary>(for the tools) what came of it.</summary>
        public static string Status { get; private set; } = "not tried";

        public static void Apply()
        {
            lock (gate)
            {
                if (tried)
                    return;

                tried = true;
            }

            try
            {
                OtherModesInSongSelect.EnsureHarmony();
                patch();
                Logger.Log(Status = "Danmakusu: its mods are drawn in purple");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: its mods keep Difficulty Reduction's colour ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        private const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static MethodInfo find(Type type, string name)
            => type.GetMethod(name, declared, Type.EmptyTypes) ?? throw new MissingMethodException(type.Name, name);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var panel = find(typeof(ModPanel), "LoadComplete");
            var small = find(typeof(ModSwitchSmall), "LoadComplete");
            var tiny = find(typeof(ModSwitchTiny), "LoadComplete");
            var icon = find(typeof(ModIcon), "updateColour");

            // (what the patches are given of osu!'s, by name)
            foreach (var (type, field) in new[]
                     {
                         (typeof(ModSwitchSmall), "mod"), (typeof(ModSwitchSmall), "activeForegroundColour"), (typeof(ModSwitchSmall), "activeBackgroundColour"),
                         (typeof(ModSwitchTiny), "Mod"), (typeof(ModSwitchTiny), "activeForegroundColour"), (typeof(ModSwitchTiny), "activeBackgroundColour"),
                         (typeof(ModIcon), "mod"), (typeof(ModIcon), "backgroundColour"),
                     })
            {
                if (type.GetField(field, declared) == null)
                    throw new MissingFieldException(type.Name, field);
            }

            // (everything is looked up before anything is patched: either all of it, or nothing)
            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.PurpleMods");
            harmony.Patch(panel, prefix: new HarmonyLib.HarmonyMethod(typeof(PurpleMods), nameof(panelPrefix)));
            harmony.Patch(small, prefix: new HarmonyLib.HarmonyMethod(typeof(PurpleMods), nameof(smallPrefix)));
            harmony.Patch(tiny, prefix: new HarmonyLib.HarmonyMethod(typeof(PurpleMods), nameof(tinyPrefix)));
            harmony.Patch(icon, prefix: new HarmonyLib.HarmonyMethod(typeof(PurpleMods), nameof(iconPrefix)));
        }

        /// <summary>Is this one of this ruleset's own mods?</summary>
        private static bool ours(IMod? mod) => mod != null && mod.GetType().Assembly == typeof(DanmakusuRuleset).Assembly;

        // (the dark of a badge that is on: osu!'s, a tenth of the colour)
        private static Color4 dark => new Color4(DanmakusuModColumn.ACCENT.R * 0.1f, DanmakusuModColumn.ACCENT.G * 0.1f, DanmakusuModColumn.ACCENT.B * 0.1f, 1f);

        /// <summary>(a mod's panel, before it first shows itself) lit purple when on.</summary>
        private static void panelPrefix(ModPanel __instance)
        {
            if (ours(__instance.Mod))
                __instance.AccentColour = DanmakusuModColumn.ACCENT;
        }

        /// <summary>(the hexagon badge on a panel) lit purple when on, dark inside.</summary>
        private static void smallPrefix(IMod ___mod, ref Color4 ___activeForegroundColour, ref Color4 ___activeBackgroundColour)
        {
            if (!ours(___mod))
                return;

            ___activeForegroundColour = DanmakusuModColumn.ACCENT;
            ___activeBackgroundColour = dark;
        }

        /// <summary>(the small acronym badge: in the hexagon, the chosen mods, scores) filled purple when on, the acronym dark.</summary>
        private static void tinyPrefix(IMod ___Mod, ref Color4 ___activeForegroundColour, ref Color4 ___activeBackgroundColour)
        {
            if (!ours(___Mod))
                return;

            ___activeForegroundColour = dark;
            ___activeBackgroundColour = DanmakusuModColumn.ACCENT;
        }

        /// <summary>(a mod's icon, whenever it is coloured) purple.</summary>
        private static void iconPrefix(IMod ___mod, ref Color4 ___backgroundColour)
        {
            if (ours(___mod))
                ___backgroundColour = DanmakusuModColumn.ACCENT;
        }
    }
}
