using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// osu! offers a map in a ruleset only if it is that ruleset's own or an osu!standard map to convert (<see cref="BeatmapInfoExtensions.AllowGameplayWithRuleset"/>: song select's list, the difficulties
    /// of a set, and whether picking a map switches to its own ruleset all ask it), so maps of mania, taiko and catch never showed in Danmakusu (they did only while an import had wrongly filed them as osu!
    /// maps). The answer is changed for Danmakusu alone, while osu! runs: with "show converted beatmaps" on, a mania, taiko or catch map is offered too, to be converted by
    /// <see cref="DanmakusuBeatmapConverter"/> (see <see cref="ConvertedModes"/>). Nothing else changes: not the maps, not where osu! files them, not what any other ruleset is offered.
    /// Done with Harmony (its dll is embedded in this one, as osu! loads only the ruleset's): if that fails, only this is lost, and the log says so.
    /// </summary>
    public static class OtherModesInSongSelect
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
                EnsureHarmony();
                patch();
                Logger.Log(Status = "Danmakusu: maps of mania, taiko and catch are offered in Danmakusu");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: maps of the other modes cannot be offered in Danmakusu ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        // (in a method of its own, so that Harmony is only looked for once the embedded copy can be found)
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            var target = typeof(BeatmapInfoExtensions).GetMethod(nameof(BeatmapInfoExtensions.AllowGameplayWithRuleset), BindingFlags.Public | BindingFlags.Static)
                         ?? throw new MissingMethodException(nameof(BeatmapInfoExtensions), nameof(BeatmapInfoExtensions.AllowGameplayWithRuleset));

            var after = typeof(OtherModesInSongSelect).GetMethod(nameof(allowOtherModes), BindingFlags.NonPublic | BindingFlags.Static);
            new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.OtherModes").Patch(target, postfix: new HarmonyLib.HarmonyMethod(after));
        }

        /// <summary>(after osu!'s own answer) a mania, taiko or catch map may be played in Danmakusu, converted, when converted maps are shown at all.</summary>
        private static void allowOtherModes(IBeatmapInfo beatmap, RulesetInfo ruleset, bool allowConversion, ref bool __result)
        {
            if (__result || !allowConversion || ruleset?.ShortName != DanmakusuRuleset.SHORT_NAME)
                return;

            if (beatmap.Ruleset.OnlineID is ConvertedModes.TAIKO or ConvertedModes.CATCH or ConvertedModes.MANIA)
                __result = true;
        }

        private static bool resolving;

        /// <summary>Harmony is found in the embedded copy from now on (every patch of this ruleset calls this before it touches Harmony).</summary>
        internal static void EnsureHarmony()
        {
            lock (gate)
            {
                if (resolving)
                    return;

                resolving = true;
            }

            AppDomain.CurrentDomain.AssemblyResolve += resolveHarmony;
        }

        private static Assembly? resolveHarmony(object? sender, ResolveEventArgs args)
        {
            if (new AssemblyName(args.Name).Name != "0Harmony")
                return null;

            using var stream = typeof(OtherModesInSongSelect).Assembly.GetManifestResourceStream("0Harmony.dll");

            if (stream == null)
                return null;

            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);

            // (Harmony cannot work loaded from memory - what it builds refers to it by its file - so the embedded copy is written out once, under the temp folder, and loaded from there)
            string folder = Path.Combine(Path.GetTempPath(), "osu.Game.Rulesets.Danmakusu");
            string file = Path.Combine(folder, $"0Harmony-{bytes.Length}.dll");

            if (!File.Exists(file) || new FileInfo(file).Length != bytes.Length)
            {
                Directory.CreateDirectory(folder);
                string part = file + $".{Environment.ProcessId}.part";
                File.WriteAllBytes(part, bytes.ToArray());
                File.Move(part, file, true);
            }

            return Assembly.LoadFrom(file);
        }
    }
}
