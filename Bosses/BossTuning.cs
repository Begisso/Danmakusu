using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The numbers of the bosses' mechanics that the player can change in the "Selected Characters Adjustments" window of the boss menu. Every number has its own default (the value the game is balanced for);
    /// <see cref="DanmakusuBalance"/> hands out the current value. The adjustments are kept until the map is closed or finished (a restart in the game keeps them).
    /// </summary>
    public static class BossTuning
    {
        /// <summary>One adjustable number.</summary>
        public sealed class Parameter
        {
            public Parameter(string key, BossMechanic mechanic, string label, double defaultValue, double min, double max, bool interval, bool whole)
            {
                Key = key;
                Mechanic = mechanic;
                Label = label;
                Default = defaultValue;
                Min = min;
                Max = max;
                IsInterval = interval;
                IsWhole = whole;
                Current = defaultValue;
            }

            public string Key { get; }

            public BossMechanic Mechanic { get; }

            public string Label { get; }

            public double Default { get; }

            public double Min { get; }

            public double Max { get; }

            /// <summary>A time interval (a number of beats): for these, a higher number is the gentler one (it is shown green and a lower one yellow, the other way round than for the other numbers).</summary>
            public bool IsInterval { get; }

            /// <summary>Only whole numbers.</summary>
            public bool IsWhole { get; }

            public double Current { get; private set; }

            public float Value => (float)Current;

            public int Whole => (int)Math.Round(Current);

            public bool IsAdjusted => Math.Abs(Current - Default) > 1e-9;

            public void Set(double value)
            {
                value = Math.Clamp(value, Min, Max);

                if (IsWhole)
                    value = Math.Round(value);

                Current = value;
            }

            public void Reset() => Current = Default;

            /// <summary>-1: shown green, 1: shown yellow, 0: it is the default.</summary>
            public int Direction
            {
                get
                {
                    if (!IsAdjusted)
                        return 0;

                    bool higher = Current > Default;
                    return higher != IsInterval ? 1 : -1;
                }
            }
        }

        private static readonly List<Parameter> all = new List<Parameter>();

        private static Parameter add(BossMechanic mechanic, string key, string label, double defaultValue, double min, double max, bool interval = false, bool whole = false)
        {
            var parameter = new Parameter(key, mechanic, label, defaultValue, min, max, interval, whole);
            all.Add(parameter);
            return parameter;
        }

        // Cirno
        public static readonly Parameter CirnoBurstSpeed = add(BossMechanic.Freeze, "cirno_burst_speed", "Burst bullets speed", 0.5, 0.05, 2);
        public static readonly Parameter CirnoChildSize = add(BossMechanic.Freeze, "cirno_child_size", "Burst bullets size", 0.5, 0.1, 2);

        // Remilia
        public static readonly Parameter RemiliaMaxFactor = add(BossMechanic.FateSpear, "remilia_max_factor", "Final bullets / turn multiplier", 2, 1, 4);
        public static readonly Parameter RemiliaBloom = add(BossMechanic.FateSpear, "remilia_bloom", "Glow strength", 1, 0, 2);

        // Seiran
        public static readonly Parameter SeiranShots = add(BossMechanic.Volley, "seiran_volley", "Shots in a row", 3, 1, 12, whole: true);

        // Reisen
        public static readonly Parameter ReisenSlide = add(BossMechanic.Lunatic, "reisen_slide", "Slide distance (circles)", 3, 0.5, 8);
        public static readonly Parameter ReisenAttackBeats = add(BossMechanic.Lunatic, "reisen_attack_beats", "Attack length (beats)", 4, 1, 16, interval: true);
        public static readonly Parameter ReisenPauseBeats = add(BossMechanic.Lunatic, "reisen_pause_beats", "Pause length (beats)", 4, 1, 16, interval: true);

        // Junko
        public static readonly Parameter JunkoBeamSize = add(BossMechanic.Purification, "junko_beam_size", "Beam size", 0.5, 0.1, 2);
        public static readonly Parameter JunkoTurn = add(BossMechanic.Purification, "junko_turn", "Beam turn speed", 2, 0.25, 6);
        public static readonly Parameter JunkoBeams = add(BossMechanic.Purification, "junko_beams", "Beams multiplier", 3, 1, 4, whole: true);

        // Yorihime
        public static readonly Parameter YorihimeReturn = add(BossMechanic.Return, "yorihime_return", "Returned bullets size / speed", 0.5, 0.1, 2);

        // Utsuho
        public static readonly Parameter UtsuhoRingScale = add(BossMechanic.Nuclear, "utsuho_ring_scale", "Big bullets size", 10.8, 1, 24);
        public static readonly Parameter UtsuhoHole = add(BossMechanic.Nuclear, "utsuho_hole", "Black hole size", 1.3, 0.3, 5);
        public static readonly Parameter UtsuhoJetSpeed = add(BossMechanic.Nuclear, "utsuho_jet_speed", "Gamma ray speed", 6, 0.1, 12);
        public static readonly Parameter UtsuhoRingBeats = add(BossMechanic.Nuclear, "utsuho_ring_beats", "Big bullets interval (beats)", 4, 1, 16, interval: true);

        // Nue
        public static readonly Parameter NuePull = add(BossMechanic.Unknown, "nue_pull", "UFO pull force", 1.875, 0.1, 5);
        public static readonly Parameter NueCone = add(BossMechanic.Unknown, "nue_cone", "UFO cone half angle (degrees)", 7.5, 2, 30);
        public static readonly Parameter NueShootSpeed = add(BossMechanic.Unknown, "nue_shoot_speed", "UFO shot speed", 1, 0.1, 6);
        public static readonly Parameter NueGatherBeats = add(BossMechanic.Unknown, "nue_gather_beats", "UFOs gathering time (beats)", 1, 0.5, 8, interval: true);

        // Toyohime
        public static readonly Parameter ToyohimeTurn = add(BossMechanic.FreeFall, "toyohime_turn", "Pull force (turn rate)", 0.0001875, 0.00005, 0.005);
        public static readonly Parameter ToyohimeRadius = add(BossMechanic.FreeFall, "toyohime_radius", "Pull radius (circles)", 5, 1, 15);

        // Letty
        public static readonly Parameter LettyDissipateBeats = add(BossMechanic.Winter, "letty_dissipate_beats", "Beam lifetime (beats)", 4, 1, 16, interval: true);
        public static readonly Parameter LettyRing = add(BossMechanic.Winter, "letty_ring", "Bullets per dissipating slice", 4, 1, 5, whole: true);

        // Zanmu
        public static readonly Parameter ZanmuRippleBeats = add(BossMechanic.Death, "zanmu_ripple_beats", "Ripple interval (beats)", 16, 4, 64, interval: true);
        public static readonly Parameter ZanmuHoldBeats = add(BossMechanic.Death, "zanmu_hold_beats", "Hold before the fall (beats)", 8, 1, 32, interval: true);

        // Yuyuko
        public static readonly Parameter YuyukoSow = add(BossMechanic.ButterflyFans, "yuyuko_sow", "Tree flowers share", 0.25, 0.01, 1);
        public static readonly Parameter YuyukoFall = add(BossMechanic.ButterflyFans, "yuyuko_fall", "Falling flowers share", 0.25, 0.05, 1);

        // Urumi
        public static readonly Parameter UrumiPush = add(BossMechanic.Momentum, "urumi_push", "Push speed (x bullet speed)", 0.5, 0.1, 3);

        // Flandre
        public static readonly Parameter FlandreEyeBeats = add(BossMechanic.Destruction, "flandre_eye_beats", "Eye explodes after (beats)", 4, 1, 32, interval: true);
        public static readonly Parameter FlandreEyeRadius = add(BossMechanic.Destruction, "flandre_eye_radius", "Explosion radius (circles)", 3.375, 1, 8);
        public static readonly Parameter FlandreAsyncBeats = add(BossMechanic.Destruction, "flandre_async_beats", "Clone shots apart (beats)", 2, 0, 8, interval: true);
        public static readonly Parameter FlandreSpeed = add(BossMechanic.Destruction, "flandre_speed", "Shot again: speed", 2, 0.5, 4);
        public static readonly Parameter FlandreSize = add(BossMechanic.Destruction, "flandre_size", "Shot again: size", 0.5, 0.2, 2);
        public static readonly Parameter FlandreCross = add(BossMechanic.Destruction, "flandre_cross", "Every n-th makes a cross", 4, 1, 16, interval: true, whole: true);

        public static IReadOnlyList<Parameter> All => all;

        /// <summary>The numbers that can be adjusted for the bosses with these mechanics (each mechanic once).</summary>
        public static List<Parameter> For(IEnumerable<BossMechanic> mechanics)
        {
            var wanted = new HashSet<BossMechanic>(mechanics);
            return all.Where(parameter => wanted.Contains(parameter.Mechanic)).ToList();
        }

        /// <summary>Counts every change, so that the map is planned again when it has changed.</summary>
        public static int Version { get; private set; }

        public static bool AnyAdjusted => all.Any(parameter => parameter.IsAdjusted);

        public static void Changed() => Version++;

        public static void ResetAll()
        {
            bool any = AnyAdjusted;

            foreach (var parameter in all)
                parameter.Reset();

            if (any)
                Version++;
        }

        // ---- when the adjustments are let go: when the map is closed or finished, but not when it is restarted ----

        private static string? lastMap;
        private static DateTime lastClosed = DateTime.MinValue;
        private static readonly Dictionary<string, int> alive = new Dictionary<string, int>();

        /// <summary>How long after the playfield of a map has gone the same map may be started again (a restart) and keep the adjustments.</summary>
        private static readonly TimeSpan restart_window = TimeSpan.FromSeconds(20);

        /// <summary>A map is started: the adjustments stay if it is the same map that was just left (a restart; the old playfield may not be gone yet), and are reset otherwise.</summary>
        public static void MapOpened(string mapId)
        {
            alive.TryGetValue(mapId, out int others);
            bool restart = others > 0 || (lastMap == mapId && DateTime.UtcNow - lastClosed < restart_window);

            if (!restart)
                ResetAll();

            alive[mapId] = others + 1;
            lastMap = mapId;
        }

        /// <summary>The playfield of the map has gone (closed, finished or restarted: the next <see cref="MapOpened"/> tells which).</summary>
        public static void MapLeft(string mapId)
        {
            if (alive.TryGetValue(mapId, out int count))
                alive[mapId] = Math.Max(count - 1, 0);

            lastMap = mapId;
            lastClosed = DateTime.UtcNow;
        }
    }
}
