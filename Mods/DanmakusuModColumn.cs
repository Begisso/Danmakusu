using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The column of the mod select screen with this ruleset's own mods (the boss selections), before osu!'s Difficulty Reduction, in a colour of its own. The mods stay what they are to osu! (of the
    /// Difficulty Reduction type: nothing else changes for them), so this column is a Difficulty Reduction column that shows only ours, and the real one shows the rest (<see cref="ModsColumnInSongSelect"/>).
    /// It has no hotkeys: the keys of Difficulty Reduction are osu!'s own again (Easy, No Fail, Half Time...).
    /// </summary>
    public partial class DanmakusuModColumn : ModColumn
    {
        /// <summary>Purple (was crimson): a redder one than osu!'s Conversion column (#8c66ff), so that the two are told apart.</summary>
        public static readonly Color4 ACCENT = new Color4(0.61f, 0.32f, 0.88f, 1f);

        public DanmakusuModColumn()
            : base(ModType.DifficultyReduction, false)
        {
            HeaderText = "Danmakusu";
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AccentColour = ACCENT;
        }

        protected override bool OnKeyDown(KeyDownEvent e) => false;

        /// <summary>(as song select's own columns: a mod that does not go with the chosen ones is shown so; purple: <see cref="PurpleMods"/>)</summary>
        protected override ModPanel CreateModPanel(ModState mod) => new IncompatibilityDisplayingModPanel(mod);

        /// <summary>Is this one of this ruleset's own mods?</summary>
        public static bool IsOurs(Mod mod) => mod.GetType().Assembly == typeof(DanmakusuRuleset).Assembly;
    }

    /// <summary>
    /// Puts <see cref="DanmakusuModColumn"/> into the mod select screen of song select (not into multiplayer's free mods, which keep osu!'s columns): right before Difficulty Reduction, which from then on
    /// shows all its mods but ours. In another ruleset the column has nothing and is not shown. Done with Harmony, like <see cref="OtherModesInSongSelect"/>: if osu! has changed where these are, only this
    /// is lost (the boss selections stay first in Difficulty Reduction), and the log says so.
    /// </summary>
    public static class ModsColumnInSongSelect
    {
        private static readonly object gate = new object();
        private static bool tried;

        /// <summary>(for the tools) what came of it.</summary>
        public static string Status { get; private set; } = "not tried";

        private static ConstructorInfo dimMade = null!;
        private static PropertyInfo dimColumn = null!;
        private static Type dimType = null!;

        // (the Difficulty Reduction columns that have a Danmakusu column beside them)
        private static readonly ConditionalWeakTable<ModColumn, DanmakusuModColumn> split = new ConditionalWeakTable<ModColumn, DanmakusuModColumn>();

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
                Logger.Log(Status = "Danmakusu: its mods have a column of their own");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: its mods stay in Difficulty Reduction ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        private const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var createColumns = typeof(ModSelectOverlay).GetMethod("createColumns", any) ?? throw new MissingMethodException(nameof(ModSelectOverlay), "createColumns");
            dimType = typeof(ModSelectOverlay).GetNestedType("ColumnDimContainer", any) ?? typeof(ModSelectOverlay).Assembly.GetType("osu.Game.Overlays.Mods.ModSelectOverlay+ColumnDimContainer")
                ?? throw new TypeLoadException("ModSelectOverlay.ColumnDimContainer");
            dimMade = dimType.GetConstructor(any, new[] { typeof(ModSelectColumn) }) ?? throw new MissingMethodException(dimType.Name, ".ctor");
            dimColumn = dimType.GetProperty("Column", any) ?? throw new MissingMemberException(dimType.Name, "Column");
            var setAvailable = typeof(ModColumn).GetProperty(nameof(ModColumn.AvailableMods))?.GetSetMethod() ?? throw new MissingMethodException(nameof(ModColumn), "set_AvailableMods");

            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.ModsColumn");
            harmony.Patch(createColumns, postfix: new HarmonyLib.HarmonyMethod(typeof(ModsColumnInSongSelect), nameof(columnsPostfix)));
            harmony.Patch(setAvailable, prefix: new HarmonyLib.HarmonyMethod(typeof(ModsColumnInSongSelect), nameof(availablePrefix)));
        }

        /// <summary>(after osu! has made the columns) ours before Difficulty Reduction.</summary>
        private static void columnsPostfix(ModSelectOverlay __instance, ref object __result)
        {
            // (multiplayer's free mods: osu!'s columns, which may pick mods that do not go together)
            if (__instance.GetType().Name.Contains("FreeMod", StringComparison.Ordinal))
                return;

            var columns = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(dimType))!;
            bool placed = false;

            foreach (object dim in (IEnumerable)__result)
            {
                if (!placed && dimColumn.GetValue(dim) is ModColumn { ModType: ModType.DifficultyReduction } reduction)
                {
                    var ours = new DanmakusuModColumn { Margin = new MarginPadding { Right = 10 } };
                    columns.Add(dimMade.Invoke(new object[] { ours }));
                    split.AddOrUpdate(reduction, ours);
                    placed = true;
                }

                columns.Add(dim);
            }

            __result = columns;
        }

        /// <summary>(a column is given its mods) ours get only ours, the Difficulty Reduction beside them all but ours.</summary>
        private static void availablePrefix(ModColumn __instance, ref IReadOnlyList<ModState> value)
        {
            if (__instance is DanmakusuModColumn)
                value = value.Where(state => DanmakusuModColumn.IsOurs(state.Mod)).ToArray();
            else if (split.TryGetValue(__instance, out _))
                value = value.Where(state => !DanmakusuModColumn.IsOurs(state.Mod)).ToArray();
        }
    }
}
