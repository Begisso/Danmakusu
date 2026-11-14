using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// What osu!mania makes of a map converted to a number of keys (Yatsuhashi's falling beams, 2026-11-14): osu!mania's own converter, as lazer converts an osu!standard map - the same patterns, the same seed.
    /// The map is always handed over as an osu!standard map (a map of another mode played here is too: its objects are Danmakusu's by then), its spinners as the legacy spinners the converter knows (osu!'s own
    /// spinner would be read as a mania hold note). Only this class touches osu!mania's assembly (lazer ships it), so nothing else loads it.
    /// </summary>
    public static class ManiaNotes
    {
        /// <summary>The notes of the map converted to <paramref name="columns"/> keys, in time order: the lane, when the note is and when it ends (its own time for a tap). None when the converter fails.</summary>
        public static List<(int Column, double Start, double End)> Of(IBeatmap beatmap, int columns)
        {
            var notes = new List<(int Column, double Start, double End)>();

            try
            {
                var objects = new List<HitObject>(beatmap.HitObjects.Count);

                foreach (var hitObject in beatmap.HitObjects)
                {
                    if (hitObject is Spinner spinner)
                        objects.Add(new LegacySpinner { StartTime = spinner.StartTime, Duration = spinner.Duration, Samples = spinner.Samples });
                    else
                        objects.Add(hitObject);
                }

                var difficulty = beatmap.Difficulty.Clone();
                var source = new Beatmap<HitObject>
                {
                    BeatmapInfo = new BeatmapInfo(new OsuRuleset().RulesetInfo, difficulty),
                    Difficulty = difficulty,
                    ControlPointInfo = beatmap.ControlPointInfo,
                    HitObjects = objects,
                };

                foreach (var pause in beatmap.Breaks)
                    source.Breaks.Add(pause);

                var converter = new ManiaBeatmapConverter(source, new ManiaRuleset()) { TargetColumns = columns };

                foreach (var hitObject in converter.Convert().HitObjects)
                {
                    if (hitObject is not ManiaHitObject note)
                        continue;

                    double end = note is HoldNote hold ? Math.Max(hold.EndTime, hold.StartTime) : note.StartTime;
                    notes.Add((Math.Clamp(note.Column, 0, columns - 1), note.StartTime, end));
                }
            }
            catch (Exception e)
            {
                notes.Clear(); // (a map the converter cannot take: no notes, rather than no game)
                osu.Framework.Logging.Logger.Log($"Danmakusu: the osu!mania convert failed ({e.GetType().Name}: {e.Message}); no falling beams.");
            }

            notes.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Column.CompareTo(b.Column));
            return notes;
        }

        /// <summary>A spinner as the decoder makes it from a .osu file: what osu!mania's converter turns into its spinner pattern.</summary>
        private sealed class LegacySpinner : HitObject, IHasLegacyHitObjectType, IHasDuration, IHasPosition
        {
            public LegacyHitObjectType LegacyType => LegacyHitObjectType.Spinner;

            public double Duration { get; set; }

            public double EndTime
            {
                get => StartTime + Duration;
                set => Duration = value - StartTime;
            }

            // (in the middle of the playfield, as osu!mania places a spinner)
            public Vector2 Position { get; set; } = new Vector2(256, 192);

            public float X
            {
                get => Position.X;
                set => Position = new Vector2(value, Position.Y);
            }

            public float Y
            {
                get => Position.Y;
                set => Position = new Vector2(Position.X, value);
            }
        }
    }
}
