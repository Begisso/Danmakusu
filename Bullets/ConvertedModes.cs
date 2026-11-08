using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Maps of the other modes, played as Danmakusu (<see cref="DanmakusuBeatmapConverter"/> makes them). What every object came from is kept here, by object:
    ///  - osu!mania: the objects stand on a ring round the middle of the playfield, the first column in the middle and the others evenly round it, the ring as big as the keys are many, at circle size 4; it turns one way for 32 beats,
    ///    then back the other way, and so on, and is twice as big in a boss section (<see cref="Arrange"/>). A long note is a slider that does not move, looking like a circle, with a whistle on its head:
    ///    its beams are the spinner's, turning towards the player as they fire (<see cref="Info.Static"/>).
    ///  - osu!taiko: the same, always as four keys (kat, don, don, kat): the dons take turns on the two in the middle, the kats on the two at the ends. A drumroll is a long note, on the don places. A big note
    ///    shoots one ring of Luna Child's arc beams instead of its bullets, as many arcs as a plain ring has bullets (<see cref="Info.Big"/>; BulletField.Drums).
    ///  - osu!catch: the fruits and the juice streams are circles and sliders that fall straight down, evenly, landing on the bottom wall of the screen when they are due (their approach circles stand on the top wall above them) (a stream is its trail:
    ///    every point of it lands when the stream is there), hurting like a slider body all the way; what lands shoots the bullets and beams it would shoot, half out of the bottom wall spread over 150 degrees up, half out of the top wall above it, down. A banana shower is a spinner.
    /// </summary>
    public static class ConvertedModes
    {
        public const int STANDARD = 0, TAIKO = 1, CATCH = 2, MANIA = 3;

        public sealed class Info
        {
            public int Mode;

            /// <summary>(mania, taiko) which way from the middle of the ring it stands (zero: in the middle), and how far outside the boss sections (osu! px).</summary>
            public Vector2 Way;

            public float Radius;

            /// <summary>A long note or a drumroll: a slider that does not move.</summary>
            public bool Static;

            /// <summary>(taiko) a big note: a ring of arc beams instead of its rings of bullets.</summary>
            public bool Big;

            /// <summary>(catch) how fast it falls (px per ms): its head lands on <see cref="DanmakusuBalance.CATCH_BOTTOM"/> at its start.</summary>
            public float FallSpeed;

            /// <summary>(catch) how long its approach circle warns, on the top wall, before it comes in there: its approach time (DanmakusuBeatmapProcessor).</summary>
            public double WarnMs;
        }

        private static readonly ConditionalWeakTable<HitObject, Info> infos = new ConditionalWeakTable<HitObject, Info>();

        public static Info? Of(HitObject hitObject) => infos.TryGetValue(hitObject, out var info) ? info : null;

        internal static void Set(HitObject hitObject, Info info) => infos.AddOrUpdate(hitObject, info);

        /// <summary>The mode a map was converted from (<see cref="STANDARD"/> for an osu! map).</summary>
        public static int ModeOf(IBeatmap beatmap)
        {
            foreach (var hitObject in beatmap.HitObjects)
            {
                if (Of(hitObject) is Info info)
                    return info.Mode;
            }

            return STANDARD;
        }

        /// <summary>(catch) how far a falling object is from where it lands, at <paramref name="time"/>: above it before, below it after.</summary>
        public static Vector2 FallOffset(Info info, double landsAt, double time) => new Vector2(0f, (float)(time - landsAt) * info.FallSpeed);

        /// <summary>The mode of the map being converted: its own ruleset's (the game knows it), or, for a map read without the game, a mania hold note says mania.</summary>
        public static int SourceMode(IBeatmap original)
        {
            int id = original.BeatmapInfo?.Ruleset?.OnlineID ?? 0;

            if (id is TAIKO or CATCH or MANIA)
                return id;

            return original.HitObjects.Any(h => h.GetType().Name == "ConvertHold") ? MANIA : STANDARD;
        }

        /// <summary>
        /// (for the tools, which read a map without the game) the decoder leaves the mode out without the game's rulesets: it is read from the file's "Mode:" line, and the map is given a ruleset of that
        /// number, so that it is converted as the game converts it. False for an osu! map (nothing is set then).
        /// </summary>
        public static bool KeepMode(Beatmap decoded, string path)
        {
            int mode = STANDARD;

            foreach (string line in File.ReadLines(path).Take(60))
            {
                if (line.StartsWith("Mode:", StringComparison.Ordinal) && int.TryParse(line.Substring(5).Trim(), out int read))
                {
                    mode = read;
                    break;
                }
            }

            if (mode is not (TAIKO or CATCH or MANIA))
                return false;

            string name = mode == TAIKO ? "taiko" : mode == CATCH ? "fruits" : "mania";
            decoded.BeatmapInfo.Ruleset = new RulesetInfo(name, name, string.Empty, mode);
            return true;
        }

        private static readonly Vector2 middle = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>The ring's radius for so many keys, outside the boss sections (osu! px).</summary>
        public static float RingRadius(int keys) => Math.Min(DanmakusuBalance.CONVERT_RING_RADIUS + DanmakusuBalance.CONVERT_RING_PER_KEY * keys, DanmakusuBalance.CONVERT_RING_MOST);

        /// <summary>Which way from the middle column <paramref name="column"/> of <paramref name="keys"/> stands <paramref name="beats"/> beats into the map (the first column: in the middle, zero).</summary>
        public static Vector2 RingWay(int column, int keys, double beats)
        {
            if (column <= 0 || keys <= 1)
                return Vector2.Zero;

            // (one way for 32 beats, the same back the other way for the next 32, and so on)
            double turns = beats / DanmakusuBalance.CONVERT_RING_REVERSE_BEATS;
            double whole = Math.Floor(turns);
            double part = turns - whole;
            float turned = DanmakusuBalance.CONVERT_RING_TURN * (float)(((long)whole % 2 == 0) ? part : 1 - part);
            float angle = -MathF.PI / 2f + MathF.Tau * (column - 1) / (keys - 1) + turned;

            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        /// <summary>How many beats into the map <paramref name="time"/> is, by its timing points.</summary>
        public static double BeatsAt(IBeatmap beatmap, double time)
        {
            var points = beatmap.ControlPointInfo.TimingPoints;

            if (points.Count == 0)
                return time / 500;

            double beats = 0;

            for (int i = 0; i < points.Count; i++)
            {
                double from = i == 0 ? Math.Min(points[0].Time, time) : points[i].Time;
                double to = i + 1 < points.Count ? Math.Min(points[i + 1].Time, time) : time;

                if (to <= from)
                {
                    if (from >= time)
                        break;

                    continue;
                }

                beats += (to - from) / Math.Max(points[i].BeatLength, 1);
            }

            return beats;
        }

        /// <summary>Where an object of the ring stands, outside the boss sections (also for the measures of the sections, which must not depend on them).</summary>
        public static Vector2 BaseStart(OsuHitObject hitObject)
            => Of(hitObject) is { Mode: MANIA or TAIKO } info ? middle + info.Way * info.Radius : hitObject.StackedPosition;

        public static Vector2 BaseEnd(OsuHitObject hitObject)
            => Of(hitObject) is { Mode: MANIA or TAIKO } info ? middle + info.Way * info.Radius : hitObject.StackedEndPosition;

        /// <summary>
        /// Puts every object of a converted map where it stands now (again: nothing adds up): on the ring, twice as far out in <paramref name="bossSections"/> (none: no bosses). A catch map that Hard Rock
        /// turned upside down is turned back (what falls lands on the bottom).
        /// </summary>
        public static void Arrange(IBeatmap beatmap, IReadOnlyList<(double Start, double End)>? bossSections)
        {
            foreach (var hitObject in beatmap.HitObjects)
            {
                if (hitObject is not OsuHitObject osuObject || Of(hitObject) is not Info info)
                    continue;

                switch (info.Mode)
                {
                    case MANIA:
                    case TAIKO:
                    {
                        bool boss = bossSections != null && bossSections.Any(section => osuObject.StartTime >= section.Start && osuObject.StartTime < section.End);
                        Vector2 at = middle + info.Way * info.Radius * (boss ? DanmakusuBalance.CONVERT_BOSS_RING : 1f);

                        if (osuObject.Position != at)
                            moveTo(osuObject, at);

                        break;
                    }

                    case CATCH:
                        if (osuObject.Position.Y < DanmakusuBalance.CATCH_BOTTOM - 1f)
                        {
                            if (osuObject is Slider slider)
                            {
                                foreach (var point in slider.Path.ControlPoints)
                                    point.Position = new Vector2(point.Position.X, -point.Position.Y);
                            }

                            moveTo(osuObject, new Vector2(osuObject.Position.X, DanmakusuBalance.CATCH_BOTTOM));
                        }

                        break;
                }
            }
        }

        /// <summary>(a slider's head, ticks and tail go with it)</summary>
        private static void moveTo(OsuHitObject hitObject, Vector2 at)
        {
            hitObject.Position = at;

            if (hitObject is not Slider slider)
                return;

            foreach (var nested in slider.NestedHitObjects)
            {
                switch (nested)
                {
                    case SliderHeadCircle head:
                        head.Position = at;
                        break;

                    case SliderTailCircle tail:
                        tail.Position = slider.EndPosition;
                        break;

                    case SliderRepeat repeat:
                        repeat.Position = at + slider.Path.PositionAt(repeat.PathProgress);
                        break;

                    case SliderTick tick:
                        tick.Position = at + slider.Path.PositionAt(tick.PathProgress);
                        break;
                }
            }
        }

        /// <summary>(taiko) the big notes of a map: when, where and how many arcs (as many as a plain ring of that moment has bullets).</summary>
        public static (double Time, Vector2 Centre, int Arcs, int ComboIndex)[] DrumRings(IBeatmap beatmap, Func<double, int> arcsAt)
        {
            var rings = new List<(double, Vector2, int, int)>();

            foreach (var hitObject in beatmap.HitObjects)
            {
                if (hitObject is OsuHitObject osuObject && Of(hitObject) is { Big: true })
                    rings.Add((osuObject.StartTime, osuObject.StackedPosition, Math.Max(arcsAt(osuObject.StartTime), 1), osuObject.ComboIndexWithOffsets));
            }

            return rings.ToArray();
        }
    }
}
