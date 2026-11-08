using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Turns a playable (mod-adjusted) osu! beatmap into a list of bullet emitters:
    ///  - hit circles and slider heads fire a full ring. Bullet size, shape and count depend on the hitsounds (OD 0..10):
    ///    normal 10..40 bullets; clap 1.33x ellipses, 10..30; whistle 1.66x arrowheads, 3..5;
    ///    finish or last of its combo 2x spinning stars, always 5, ricocheting once, and the ring is aimed so its first bullet flies at the player;
    ///  - slider ticks, slider repeats (arrows) and slider ends fire a half-size ring (all the same);
    ///  - spinners fire a spiral (a stream of small rotating rings) of spinning stars (2x size) for as long as the spinner lasts, and 5 laser
    ///    beams (1x..4x the bullet radius wide by OD, 2x the bullet speed), the first aimed at the player when the spinner starts, which then turn
    ///    slowly (6..60 degrees per second by OD) towards the player.
    /// Sliders with a whistle at their start also fire lasers from the slider ball: OD 0..10 -> 1..3 beams, the first aimed at the player (see <see cref="LaserEmitter"/>).
    /// An object fires one ring per hitsound (whistle, clap, finish; plain objects fire a normal ring); the volume (0..100%) of that
    /// hitsound scales the ring's bullet count and its speed by 0.1x..1x and its bullet size by 0.5x..1x.
    /// Every ring is rotated by 1 degree per 1/8 beat elapsed since the first timing point, continuously until the song ends.
    /// <see cref="PlanSliderZones"/> builds the damaging slider bodies. To change that, edit <see cref="Plan"/>.
    /// </summary>
    public static class BulletPlanner
    {
        private const float golden_angle = 2.3999632f;

        // ---- which bosses do what: the long lists of the planner in one place (a new boss is an entry here, not another "|| ..." in a condition) ----

        /// <summary>Bosses whose attacks follow each other without a pause (Hecatia's, Raiko's, Mokou's, Letty's...).</summary>
        private static readonly HashSet<BossMechanic> no_pauses = new HashSet<BossMechanic>
        {
            BossMechanic.Dream, BossMechanic.Rewind, BossMechanic.ThreeWorlds, BossMechanic.Taiko, BossMechanic.Rebirth, BossMechanic.Winter, BossMechanic.Dowsing, BossMechanic.Death, BossMechanic.Destruction,
            BossMechanic.Darkness, BossMechanic.Qi, BossMechanic.SevenElements, BossMechanic.Momentum, BossMechanic.Rays, BossMechanic.ImpossibleRequests, BossMechanic.Haste, BossMechanic.HalfMoon, BossMechanic.Claws, BossMechanic.Reaping,
            BossMechanic.Anchors,
        };

        /// <summary>Bosses whose spinners shoot their own bullets instead of the spinner's stars (what they turn into: the sets below; talismans otherwise).</summary>
        private static readonly HashSet<BossMechanic> own_spinner_bullets = new HashSet<BossMechanic>
        {
            BossMechanic.FantasySeal, BossMechanic.Rebirth, BossMechanic.Echo, BossMechanic.Purification, BossMechanic.TimeStop, BossMechanic.FateSpear, BossMechanic.Doors, BossMechanic.Return, BossMechanic.Volley,
            BossMechanic.Dowsing, BossMechanic.Winter, BossMechanic.FreeFall, BossMechanic.ButterflyFans, BossMechanic.Death, BossMechanic.Destruction, BossMechanic.Darkness, BossMechanic.Qi, BossMechanic.SevenElements,
            BossMechanic.Prism, BossMechanic.Unconscious, BossMechanic.Recollection, BossMechanic.Necromancy, BossMechanic.Dream, BossMechanic.Spring, BossMechanic.Momentum, BossMechanic.Silence, BossMechanic.Stampede,
            BossMechanic.Autumn, BossMechanic.Fortune, BossMechanic.Shockwave, BossMechanic.Recording, BossMechanic.Earthquake, BossMechanic.Judgement, BossMechanic.Market, BossMechanic.Misfortune, BossMechanic.Foxes,
            BossMechanic.Stacking, BossMechanic.Possibility, BossMechanic.Storm, BossMechanic.Slash, BossMechanic.Rays, BossMechanic.ImpossibleRequests, BossMechanic.Cloud, BossMechanic.Haste, BossMechanic.Crescent,
            BossMechanic.Blades, BossMechanic.Miracle, BossMechanic.Claws, BossMechanic.Anchors,
        };

        /// <summary>Of those, the ones whose spinner stars become round bullets, knives, arrowheads, ellipses (hearts: Koishi and Satori; stars: Tenshi; maple leaves: Shizuha).</summary>
        private static readonly HashSet<BossMechanic> round_spinner_bullets = new HashSet<BossMechanic>
        {
            BossMechanic.Fortune, BossMechanic.Shockwave, BossMechanic.Recording, BossMechanic.Spring, BossMechanic.Rebirth, BossMechanic.Purification, BossMechanic.ButterflyFans, BossMechanic.Death, BossMechanic.Darkness,
            BossMechanic.Qi, BossMechanic.SevenElements, BossMechanic.Prism, BossMechanic.Dream, BossMechanic.Haste,
        };

        private static readonly HashSet<BossMechanic> knife_spinner_bullets = new HashSet<BossMechanic> { BossMechanic.TimeStop, BossMechanic.FateSpear, BossMechanic.Destruction, BossMechanic.Blades };
        private static readonly HashSet<BossMechanic> heart_spinner_bullets = new HashSet<BossMechanic> { BossMechanic.Unconscious, BossMechanic.Recollection };
        private static readonly HashSet<BossMechanic> arrowhead_spinner_bullets = new HashSet<BossMechanic> { BossMechanic.Winter, BossMechanic.Volley, BossMechanic.Dowsing, BossMechanic.Crescent };
        private static readonly HashSet<BossMechanic> ellipse_spinner_bullets = new HashSet<BossMechanic> { BossMechanic.FreeFall, BossMechanic.Momentum, BossMechanic.Stacking };

        /// <summary>Bosses whose spinners shoot none of the spinner's stars: they shoot nothing but their own (Luna Child's and Saki's arcs, Chimata's talismans, Yumemi's flowers...).</summary>
        private static readonly HashSet<BossMechanic> no_spinner_stars = new HashSet<BossMechanic>
        {
            BossMechanic.Silence, BossMechanic.Stampede, BossMechanic.Market, BossMechanic.Possibility, BossMechanic.Storm, BossMechanic.Slash, BossMechanic.Rays, BossMechanic.ImpossibleRequests, BossMechanic.Cloud, BossMechanic.Claws,
            BossMechanic.Anchors,
        };

        /// <summary>Bosses with no big beams of their own, in their sections or their spinners (Nazrin, Zanmu, Reisen, Rumia, Flandre; also Doremy and the Akis, by id).</summary>
        private static readonly HashSet<BossMechanic> no_big_beams = new HashSet<BossMechanic>
        {
            BossMechanic.Dowsing, BossMechanic.Death, BossMechanic.Lunatic, BossMechanic.Darkness, BossMechanic.Destruction, BossMechanic.Claws, BossMechanic.Reaping, BossMechanic.Anchors,
        };

        private static bool has(IList<HitSampleInfo> samples, string name)
            => samples.Any(sample => sample.Name == name);

        /// <summary>The volume (0..100) of the hitsound with this name, or the loudest one when <paramref name="name"/> is null or not there.</summary>
        private static int volumeOf(IList<HitSampleInfo> samples, string? name)
        {
            if (samples.Count == 0)
                return 100;

            int best = -1;

            foreach (var sample in samples)
            {
                if (name == null || sample.Name == name)
                    best = Math.Max(best, sample.Volume);
            }

            return best >= 0 ? best : samples.Max(sample => sample.Volume);
        }

        /// <summary>The loudest hitsound of an object as a multiplier (0.1x..1x), for the rings of ticks, arrows and slider ends.</summary>
        private static float volumeScale(IList<HitSampleInfo>? samples)
            => DanmakusuBalance.VolumeScale(samples == null ? 100 : volumeOf(samples, null));

        /// <summary>The loudest hitsound of an object as a bullet size multiplier (0.5x..1x).</summary>
        private static float volumeSizeScale(IList<HitSampleInfo>? samples)
            => DanmakusuBalance.VolumeSizeScale(samples == null ? 100 : volumeOf(samples, null));

        /// <summary>Size and shape of the bullets of a ring: normal = 1x circle, clap = 1.33x ellipse, whistle = 1.66x arrowhead, finish = 2x star.</summary>
        private static (float scale, BulletShape shape) styleOf(BulletKind kind) => kind switch
        {
            BulletKind.Clap => (DanmakusuBalance.CLAP_BULLET_SCALE, BulletShape.Ellipse),
            BulletKind.Whistle => (DanmakusuBalance.WHISTLE_BULLET_SCALE, BulletShape.Triangle),
            BulletKind.Finish => (DanmakusuBalance.FINISH_BULLET_SCALE, BulletShape.Star),
            _ => (1f, BulletShape.Circle),
        };

        public static BulletEmitter[] Plan(IBeatmap beatmap) => Plan(beatmap, out _, out _);

        /// <summary>
        /// Every section with a boss (spinners, breaks and kiai sections), in the order of the map. They do not depend on the bosses (the boss HP bar preview uses them too).
        /// When the map has none, or they cover less than half of the song, more are chosen where the map is most intense (see <see cref="fillSections"/>); <paramref name="bands"/>
        /// gives the song's treble, bass and mid for that (it is only asked for then: reading it decodes the whole track).
        /// </summary>
        public static List<(double start, double end, BossKind kind, bool restartKiaiCount)> Sections(IBeatmap beatmap, Func<SongBands?>? bands = null)
        {
            var sections = mapSections(beatmap);
            fillSections(beatmap, sections, bands);

            // (a break never covers circles or sliders either: cut round them, as the made-up spinners are)
            var objects = beatmap.HitObjects.Where(h => h is not Spinner).Select(h => (Start: h.StartTime - 1, End: h.GetEndTime() + 1)).ToList();
            var result = new List<(double start, double end, BossKind kind, bool restartKiaiCount)>();

            foreach (var section in sections)
            {
                if (section.kind != BossKind.Break)
                {
                    result.Add(section);
                    continue;
                }

                var pieces = new List<(double Start, double End)> { (section.start, section.end) };

                foreach (var (oStart, oEnd) in objects)
                {
                    if (oEnd <= section.start || oStart >= section.end)
                        continue;

                    var next = new List<(double Start, double End)>();

                    foreach (var (start, end) in pieces)
                    {
                        if (oEnd <= start || oStart >= end)
                        {
                            next.Add((start, end));
                            continue;
                        }

                        if (oStart > start)
                            next.Add((start, oStart));

                        if (oEnd < end)
                            next.Add((oEnd, end));
                    }

                    pieces = next;
                }

                foreach (var (start, end) in pieces)
                {
                    if (end - start >= Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(start).BeatLength), 1) * DanmakusuBalance.BOSS_MIN_BREAK_BEATS)
                        result.Add((start, end, section.kind, section.restartKiaiCount));
                }
            }

            return continued(beatmap, result.OrderBy(section => section.start).ToList());
        }

        /// <summary>
        /// A section too short for an attack and a pause (a spinner and a break just as well) has no boss of its own: it is a part of the section before it, or at the start of the map of the one after it, and the boss goes on through it
        /// as if nothing had happened. One that stands far from any other stays as it is, and has the boss of the section before it (see <see cref="bossSlots"/>). Only a section that can be made as long as an attack and a pause is
        /// prolonged instead (see <see cref="prolonged"/>) and is a section of its own.
        /// </summary>
        private static List<(double start, double end, BossKind kind, bool restartKiaiCount)> continued(IBeatmap beatmap, List<(double start, double end, BossKind kind, bool restartKiaiCount)> sections)
        {
            var kept = new List<(double start, double end, BossKind kind, bool restartKiaiCount)>();
            double mapStart = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Min(h => h.StartTime) : 0;
            double mapEnd = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Max(h => h.GetEndTime()) : 0;

            for (int i = 0; i < sections.Count; i++)
            {
                var section = sections[i];

                if (!holdsABoss(beatmap, section.start, section.end))
                    section = prolonged(beatmap, sections, kept, ref i, mapStart, mapEnd);

                if (kept.Count > 0 && section.start - kept[^1].end <= shortestPiece(beatmap, kept[^1].end))
                {
                    var last = kept[^1];

                    if (!holdsABoss(beatmap, section.start, section.end))
                    {
                        kept[^1] = (last.start, Math.Max(last.end, section.end), last.kind, last.restartKiaiCount);
                        continue;
                    }

                    if (!holdsABoss(beatmap, last.start, last.end))
                    {
                        kept[^1] = (last.start, Math.Max(last.end, section.end), section.kind, section.restartKiaiCount);
                        continue;
                    }
                }

                kept.Add(section);
            }

            return kept;
        }

        /// <summary>
        /// The short section at <paramref name="index"/> made just as long as an attack and a pause (a spinner becomes a break: it shoots nothing after it ends). Free time is used first, after it and then before it; what is too short for a boss
        /// of its own is taken in (<paramref name="index"/> goes past it). When there is not enough of that, time is taken from the section after it, and failing that from the one before it, if what is left of that still holds an attack and a pause
        /// (a spinner gives nothing: it is not cut). When nothing can give it, it stays as it is, and goes on with the section before it.
        /// </summary>
        private static (double start, double end, BossKind kind, bool restartKiaiCount) prolonged(IBeatmap beatmap, List<(double start, double end, BossKind kind, bool restartKiaiCount)> sections,
                                                                                                  List<(double start, double end, BossKind kind, bool restartKiaiCount)> kept, ref int index, double mapStart, double mapEnd)
        {
            var section = sections[index];
            var kind = section.kind == BossKind.Spinner ? BossKind.Break : section.kind;
            bool restart = section.kind != BossKind.Spinner && section.restartKiaiCount;
            double need = shortestPiece(beatmap, section.start);
            double end = section.start + need;
            int next = index + 1;

            while (next < sections.Count && sections[next].start < end && !holdsABoss(beatmap, sections[next].start, sections[next].end))
            {
                end = Math.Max(end, sections[next].end); // (what is taken in is taken in whole)
                next++;
            }

            double roomFrom = kept.Count > 0 ? kept[^1].end : mapStart;
            double roomTo = next < sections.Count ? sections[next].start : mapEnd;

            if (end <= roomTo)
            {
                index = next - 1;
                return (section.start, end, kind, restart);
            }

            double start = roomTo - need;

            if (start >= roomFrom)
            {
                index = next - 1;
                return (start, roomTo, kind, restart);
            }

            if (next < sections.Count && sections[next].kind != BossKind.Spinner && holdsABoss(beatmap, end, sections[next].end))
            {
                sections[next] = (end, sections[next].end, sections[next].kind, sections[next].restartKiaiCount);
                index = next - 1;
                return (section.start, end, kind, restart);
            }

            if (kept.Count > 0 && kept[^1].kind != BossKind.Spinner && holdsABoss(beatmap, kept[^1].start, start))
            {
                kept[^1] = (kept[^1].start, start, kept[^1].kind, kept[^1].restartKiaiCount);
                index = next - 1;
                return (start, roomTo, kind, restart);
            }

            return section;
        }

        /// <summary>
        /// The map has too few sections for a boss (they cover less than <see cref="DanmakusuBalance.BOSS_MIN_SONG_SHARE"/> of it, from its first object to the end of its last): the
        /// intense parts of the song (see <see cref="IntenseParts"/>) become kiai sections too. A kiai section of the map that one overlaps or touches is one section with it; the map's
        /// breaks and spinners keep their own bosses, and an intense part is cut round them.
        /// </summary>
        private static void fillSections(IBeatmap beatmap, List<(double start, double end, BossKind kind, bool restartKiaiCount)> sections, Func<SongBands?>? bands)
        {
            if (beatmap.HitObjects.Count == 0)
                return;

            double songStart = beatmap.HitObjects.Min(h => h.StartTime);
            double songEnd = beatmap.HitObjects.Max(h => h.GetEndTime());
            double covered = sections.Sum(section => section.end - section.start);

            if (songEnd <= songStart || covered >= (songEnd - songStart) * DanmakusuBalance.BOSS_MIN_SONG_SHARE)
                return;

            var intense = IntenseParts(beatmap, bands?.Invoke(), out var buildUps);

            if (intense.Count == 0)
                return;

            double beatAt(double time) => Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(time).BeatLength), 1);

            // a kiai section of the map that an intense part overlaps (or touches, within a beat) becomes one section with it
            for (int p = 0; p < intense.Count; p++)
            {
                var (start, end) = intense[p];

                for (int s = sections.Count - 1; s >= 0; s--)
                {
                    var section = sections[s];

                    // (a kiai section it overlaps or touches is one with it; so is a spinner of the map that it overlaps: as in a kiai section of the map, the boss of the part stays through it
                    // instead of the part being cut into pieces round a short spinner)
                    bool kiai = section.kind == BossKind.Kiai && section.start <= end + beatAt(end) && section.end >= start - beatAt(start);
                    bool spinner = section.kind == BossKind.Spinner && !section.restartKiaiCount && section.start < end && section.end > start;

                    if (!kiai && !spinner)
                        continue;

                    start = Math.Min(start, section.start);
                    end = Math.Max(end, section.end);
                    sections.RemoveAt(s);
                }

                intense[p] = (start, end);
            }

            // (parts that have grown into each other are one)
            intense.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(double Start, double End)>();

            foreach (var part in intense)
            {
                if (merged.Count > 0 && part.Start <= merged[^1].End + 1)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, part.End));
                else
                    merged.Add(part);
            }

            // the map's breaks and spinners keep their own bosses: an intense part is cut round them (a piece too short for a boss is left out)
            foreach (var part in merged)
            {
                var pieces = new List<(double Start, double End)> { part };

                foreach (var section in sections)
                {
                    var next = new List<(double Start, double End)>();

                    foreach (var (start, end) in pieces)
                    {
                        if (section.end <= start || section.start >= end)
                        {
                            next.Add((start, end));
                            continue;
                        }

                        if (section.start > start)
                            next.Add((start, section.start));

                        if (section.end < end)
                            next.Add((section.end, end));
                    }

                    pieces = next;
                }

                foreach (var (start, end) in pieces)
                {
                    if (end - start >= beatAt(start) * DanmakusuBalance.BOSS_MIN_BREAK_BEATS)
                        sections.Add((start, end, BossKind.Kiai, false));
                }
            }

            // the build-up before an intense part is a spinner section where it has no objects: the boss is there in the middle and does what she does in a spinner (cut round every
            // section there is by now, and round every circle and slider: a made-up spinner never covers objects)
            var objects = beatmap.HitObjects.Where(h => h is not Spinner).Select(h => (Start: h.StartTime, End: h.GetEndTime())).ToList();

            foreach (var buildUp in buildUps)
            {
                var pieces = new List<(double Start, double End)> { buildUp };
                var blockers = new List<(double start, double end)>();
                blockers.AddRange(sections.Select(s => (s.start, s.end)));
                blockers.AddRange(objects.Select(o => (o.Start - 1, o.End + 1)));

                foreach (var section in blockers)
                {
                    var next = new List<(double Start, double End)>();

                    foreach (var (start, end) in pieces)
                    {
                        if (section.end <= start || section.start >= end)
                        {
                            next.Add((start, end));
                            continue;
                        }

                        if (section.start > start)
                            next.Add((start, section.start));

                        if (section.end < end)
                            next.Add((section.end, end));
                    }

                    pieces = next;
                }

                foreach (var (start, end) in pieces)
                {
                    // (marked as a build-up - for a spinner the kiai flag means nothing else - so that it has the boss of the part that follows it)
                    if (end - start >= beatAt(start) * DanmakusuBalance.BOSS_MIN_BREAK_BEATS)
                        sections.Add((start, end, BossKind.Spinner, true));
                }
            }
        }

        /// <summary>
        /// The intense parts of a song, where a boss belongs: the song cut into phrases of <see cref="DanmakusuBalance.BOSS_FILL_PHRASE_BEATS"/> beats on its beat grid, each scored (the
        /// average score of its stretches, see <see cref="AnalyseStretches"/>), and split by their scores into three groups - calm, build-up and intense - with the two thresholds that make
        /// the groups differ the most (Otsu's method): the song decides where the lines are, not a fixed share of it. The intense phrases are kept; a gap of at most
        /// <see cref="DanmakusuBalance.BOSS_FILL_HOLE_PHRASES"/> phrases between two intense parts is filled, and a part shorter than <see cref="DanmakusuBalance.BOSS_FILL_MIN_PHRASES"/>
        /// phrases is dropped. (Public so that it can be looked at for a map.)
        /// </summary>
        public static List<(double Start, double End)> IntenseParts(IBeatmap beatmap, SongBands? song) => IntenseParts(beatmap, song, out _);

        /// <summary>As <see cref="IntenseParts(IBeatmap, SongBands?)"/>, and the build-ups right before them.</summary>
        public static List<(double Start, double End)> IntenseParts(IBeatmap beatmap, SongBands? song, out List<(double Start, double End)> buildUps)
        {
            return PickWithFloor(beatmap, AnalyseStretches(beatmap, song), out buildUps);
        }

        /// <summary>
        /// What is intense by the score (<see cref="PickIntense(IBeatmap, List{ValueTuple{double, double}}, double[], int, double, out List{ValueTuple{double, double}})"/>); and a song of which that leaves less than
        /// <see cref="DanmakusuBalance.BOSS_FILL_FLOOR_SHARE"/> gets what is loud against its averages too (<see cref="AverageScore"/>: its hitsounds and the song itself), from what is above average down, as far down as it
        /// takes to get there, but no further than <see cref="DanmakusuBalance.BOSS_FILL_FLOOR_LOWEST"/> times the average: no song has so little for its bosses, and no boss comes where it is really quiet.
        /// </summary>
        public static List<(double Start, double End)> PickWithFloor(IBeatmap beatmap, StretchAnalysis analysis, out List<(double Start, double End)> buildUps)
        {
            var parts = PickIntense(beatmap, analysis.Stretches, analysis.Score, DanmakusuBalance.BOSS_FILL_HOLE_PHRASES, DanmakusuBalance.BOSS_FILL_HOLE_SHARE, out buildUps);

            if (analysis.Stretches.Count == 0)
                return parts;

            double song = analysis.Stretches[^1].End - analysis.Stretches[0].Start;
            double wanted = song * DanmakusuBalance.BOSS_FILL_FLOOR_SHARE;

            static double coveredBy(List<(double Start, double End)> list) => list.Sum(part => part.End - part.Start);

            if (coveredBy(parts) >= wanted)
                return parts;

            var score = AverageScore(analysis);

            for (double above = 1.0; above >= DanmakusuBalance.BOSS_FILL_FLOOR_LOWEST - 1e-6; above -= DanmakusuBalance.BOSS_FILL_FLOOR_STEP)
            {
                var more = PickAboveAverage(beatmap, analysis.Stretches, score, above, above * DanmakusuBalance.BOSS_FILL_FLOOR_CALM, DanmakusuBalance.BOSS_FILL_HOLE_PHRASES, DanmakusuBalance.BOSS_FILL_HOLE_SHARE, out var moreBuildUps);
                var joined = parts.Concat(more).OrderBy(part => part.Start).ToList();
                var merged = new List<(double Start, double End)>();

                foreach (var part in joined)
                {
                    if (merged.Count > 0 && part.Start <= merged[^1].End + 1)
                        merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, part.End));
                    else
                        merged.Add(part);
                }

                if (coveredBy(merged) >= wanted || above - DanmakusuBalance.BOSS_FILL_FLOOR_STEP < DanmakusuBalance.BOSS_FILL_FLOOR_LOWEST - 1e-6)
                {
                    // (a build-up that has now become part of a section falls away where the sections are cut: see fillSections)
                    buildUps = buildUps.Concat(moreBuildUps).ToList();
                    return merged;
                }
            }

            return parts;
        }

        /// <summary>The stretches grouped into phrases of the beat grid (a scrap of a phrase, where the tempo changes every few beats, goes on into the next), each with its average score.</summary>
        public static List<(double Start, double End, double Score, double Beats)> Phrases(IBeatmap beatmap, List<(double Start, double End)> stretches, double[] score)
        {
            var phrases = new List<(double Start, double End, double Score, double Beats)>();
            double weighted = 0, length = 0;
            object? key = null;

            void close()
            {
                if (length > 0)
                    phrases[^1] = (phrases[^1].Start, phrases[^1].End, weighted / length, phrases[^1].Beats);
            }

            for (int i = 0; i < stretches.Count; i++)
            {
                var (start, end) = stretches[i];
                var timing = beatmap.ControlPointInfo.TimingPointAt(start);
                double beat = Math.Max(DanmakusuBalance.CapBeat(timing.BeatLength), 1);
                double phraseLength = beat * DanmakusuBalance.BOSS_FILL_PHRASE_BEATS;
                var phraseKey = (timing.Time, (long)Math.Floor((start - timing.Time) / phraseLength + 1e-6));

                if (!phraseKey.Equals(key) && (phrases.Count == 0 || phrases[^1].Beats >= DanmakusuBalance.BOSS_FILL_PHRASE_BEATS / 2))
                {
                    close();
                    phrases.Add((start, end, 0, 0));
                    weighted = length = 0;
                }

                key = phraseKey;
                phrases[^1] = (phrases[^1].Start, end, 0, phrases[^1].Beats + (end - start) / beat);
                weighted += score[i] * (end - start);
                length += end - start;
            }

            close();
            return phrases;
        }

        /// <summary>
        /// Otsu's method with <paramref name="groups"/> (2 or 3) groups over <paramref name="values"/>: the thresholds that spread the groups' averages the most round the average of all.
        /// Returns the lowest threshold (the top of the lowest group) and the highest one (the bottom of the highest group); with two groups they are the same.
        /// </summary>
        public static (double Low, double High) Otsu(IEnumerable<double> values, int groups)
        {
            var sorted = values.OrderBy(value => value).ToArray();

            if (sorted.Length < groups)
                return (double.PositiveInfinity, double.PositiveInfinity);

            var sums = new double[sorted.Length + 1];

            for (int i = 0; i < sorted.Length; i++)
                sums[i + 1] = sums[i] + sorted[i];

            double mean = sums[^1] / sorted.Length, best = -1;
            (double, double) found = (double.PositiveInfinity, double.PositiveInfinity);
            double spread(int from, int to) => to > from ? (to - from) * Math.Pow((sums[to] - sums[from]) / (to - from) - mean, 2) : 0;

            if (groups == 2)
            {
                for (int c = 1; c < sorted.Length; c++)
                {
                    double between = spread(0, c) + spread(c, sorted.Length);

                    if (between > best)
                    {
                        best = between;
                        double t = (sorted[c - 1] + sorted[c]) / 2;
                        found = (t, t);
                    }
                }

                return found;
            }

            for (int low = 1; low < sorted.Length - 1; low++)
            {
                for (int high = low + 1; high < sorted.Length; high++)
                {
                    double between = spread(0, low) + spread(low, high) + spread(high, sorted.Length);

                    if (between > best)
                    {
                        best = between;
                        found = ((sorted[low - 1] + sorted[low]) / 2, (sorted[high - 1] + sorted[high]) / 2);
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Another way to find the intense parts (by the song's shape): the calm phrases are the lowest of <paramref name="groups"/> Otsu groups; every run of phrases between calm ones is
        /// a part of the song that builds up and then drops. In each the drop begins where the score jumps the most (the average from there on minus the average before it); when the
        /// jump is less than <paramref name="jump"/> (as a share of the scores' range) the run has no build-up and is all drop. Drops of fewer than 32 beats are left out.
        /// </summary>
        public static List<(double Start, double End)> PickRegions(IBeatmap beatmap, List<(double Start, double End)> stretches, double[] score, int groups, double jump)
        {
            var result = new List<(double Start, double End)>();
            var phrases = Phrases(beatmap, stretches, score);

            if (phrases.Count < 3)
                return result;

            var (calm, _) = Otsu(phrases.Select(p => p.Score), groups);
            double range = Math.Max(phrases.Max(p => p.Score) - phrases.Min(p => p.Score), 1e-9);

            for (int i = 0; i < phrases.Count;)
            {
                if (phrases[i].Score < calm)
                {
                    i++;
                    continue;
                }

                int j = i;

                while (j < phrases.Count && phrases[j].Score >= calm)
                    j++;

                // (the run i..j-1: where does the drop begin?)
                int drop = i;
                double bestJump = 0;

                for (int k = i + 1; k < j; k++)
                {
                    double before = 0, after = 0;

                    for (int m = i; m < k; m++)
                        before += phrases[m].Score;

                    for (int m = k; m < j; m++)
                        after += phrases[m].Score;

                    double rise = after / (j - k) - before / (k - i);

                    if (rise > bestJump)
                    {
                        bestJump = rise;
                        drop = k;
                    }
                }

                if (bestJump < jump * range)
                    drop = i;

                double beats = 0;

                for (int m = drop; m < j; m++)
                    beats += phrases[m].Beats;

                if (beats >= DanmakusuBalance.BOSS_FILL_MIN_PHRASES * DanmakusuBalance.BOSS_FILL_PHRASE_BEATS - 0.5)
                    result.Add((phrases[drop].Start, phrases[j - 1].End));

                i = j;
            }

            return result;
        }

        /// <summary>
        /// The steps of <see cref="IntenseParts"/> for stretches that have been scored (public so that other scores can be tried with them): the phrases, the three groups, the filled gaps
        /// (at most <paramref name="holeMax"/> phrases, each scoring at least <paramref name="holeShare"/> of the threshold) and the parts that are long enough.
        /// </summary>
        public static List<(double Start, double End)> PickIntense(IBeatmap beatmap, List<(double Start, double End)> stretches, double[] score, int holeMax, double holeShare)
            => PickIntense(beatmap, stretches, score, holeMax, holeShare, out _);

        /// <summary>
        /// As <see cref="PickIntense(IBeatmap, List{ValueTuple{double, double}}, double[], int, double)"/>, and the build-ups (<paramref name="buildUps"/>): the build-up phrases (the
        /// middle group) right before an intense part, at least <see cref="DanmakusuBalance.BOSS_FILL_BUILD_UP_MIN_BEATS"/> long.
        /// </summary>
        public static List<(double Start, double End)> PickIntense(IBeatmap beatmap, List<(double Start, double End)> stretches, double[] score, int holeMax, double holeShare,
                                                                   out List<(double Start, double End)> buildUps)
        {
            // (the phrases: the stretches grouped by which phrase of the beat grid of their timing they start in; a stretch counts by how long it is)
            var phrases = Phrases(beatmap, stretches, score);

            if (phrases.Count < 3)
            {
                buildUps = new List<(double Start, double End)>();
                return new List<(double Start, double End)>();
            }

            // (Otsu with three groups - calm, build-up, intense: only the top one is wanted)
            var (calm, threshold) = Otsu(phrases.Select(phrase => phrase.Score), 3);
            return pickPhrases(phrases, calm, threshold, holeMax, holeShare, out buildUps);
        }

        /// <summary>
        /// Every stretch against the map's averages: how loud its hitsounds are against their average and how loud the song is against its average, the two counting alike (without the song, the hitsounds alone). 1 = an
        /// average stretch; the averages go by how long the stretches are.
        /// </summary>
        public static double[] AverageScore(StretchAnalysis analysis)
        {
            int n = analysis.Stretches.Count;
            double seconds = 0, volumeSum = 0, levelSum = 0;

            for (int i = 0; i < n; i++)
            {
                double length = analysis.Stretches[i].End - analysis.Stretches[i].Start;
                seconds += length;
                volumeSum += analysis.Volume[i] * length;
                levelSum += analysis.Level[i] * length;
            }

            double volumeAverage = seconds > 0 ? volumeSum / seconds : 0, levelAverage = seconds > 0 ? levelSum / seconds : 0;
            var score = new double[n];

            for (int i = 0; i < n; i++)
            {
                double volume = volumeAverage > 1e-9 ? analysis.Volume[i] / volumeAverage : 1;
                score[i] = levelAverage > 1e-9 ? (volume + analysis.Level[i] / levelAverage) * 0.5 : volume;
            }

            return score;
        }

        /// <summary>
        /// The steps of <see cref="IntenseParts"/> for phrases above an average (see <see cref="AverageScore"/>): what is at least <paramref name="above"/> times the average is intense, what is at least
        /// <paramref name="calm"/> times it can be a build-up.
        /// </summary>
        public static List<(double Start, double End)> PickAboveAverage(IBeatmap beatmap, List<(double Start, double End)> stretches, double[] score, double above, double calm, int holeMax, double holeShare,
                                                                        out List<(double Start, double End)> buildUps)
        {
            var phrases = Phrases(beatmap, stretches, score);

            if (phrases.Count < 3)
            {
                buildUps = new List<(double Start, double End)>();
                return new List<(double Start, double End)>();
            }

            return pickPhrases(phrases, calm, above, holeMax, holeShare, out buildUps);
        }

        private static List<(double Start, double End)> pickPhrases(List<(double Start, double End, double Score, double Beats)> phrases, double calm, double threshold, int holeMax, double holeShare,
                                                                    out List<(double Start, double End)> buildUps)
        {
            var result = new List<(double Start, double End)>();
            buildUps = new List<(double Start, double End)>();
            var chosen = phrases.Select(phrase => phrase.Score >= threshold).ToArray();

            // (a gap of a few phrases between intense parts is filled: a short dip in the middle of a drop)
            for (int i = 1; i < chosen.Length; i++)
            {
                if (chosen[i] || !chosen[i - 1])
                    continue;

                int j = i;

                while (j < chosen.Length && !chosen[j])
                    j++;

                bool shallow = true;

                for (int k = i; k < j; k++)
                    shallow &= phrases[k].Score >= threshold * holeShare;

                if (j < chosen.Length && j - i <= holeMax && shallow)
                {
                    for (int k = i; k < j; k++)
                        chosen[k] = true;
                }

                i = j;
            }

            // (the parts; a part that is too short is dropped - counted in beats, as the phrases of a tempo change are of other lengths)
            for (int i = 0; i < chosen.Length;)
            {
                int j = i;
                double beats = 0;

                while (j < chosen.Length && chosen[j] == chosen[i])
                    beats += phrases[j++].Beats;

                if (chosen[i] && beats >= DanmakusuBalance.BOSS_FILL_MIN_PHRASES * DanmakusuBalance.BOSS_FILL_PHRASE_BEATS - 0.5)
                {
                    result.Add((phrases[i].Start, phrases[j - 1].End));

                    // (the build-up before it: the build-up phrases right before it, up to a calm one)
                    int from = i;
                    double upBeats = 0;

                    while (from > 0 && !chosen[from - 1] && phrases[from - 1].Score >= calm)
                        upBeats += phrases[--from].Beats;

                    if (from < i && upBeats >= DanmakusuBalance.BOSS_FILL_BUILD_UP_MIN_BEATS - 0.5)
                        buildUps.Add((phrases[from].Start, phrases[i].Start));
                }

                i = j;
            }

            return result;
        }

        /// <summary>What <see cref="AnalyseStretches"/> measured: every stretch, every measure of it (each a share of the most in the map, 0..1) and its score.</summary>
        public sealed class StretchAnalysis
        {
            public List<(double Start, double End)> Stretches = new List<(double Start, double End)>();
            public double[] Volume = [], Treble = [], Bass = [], Mid = [], Slider = [], Density = [], Drums = [], HalfTime = [], Distance = [], Level = [], Score = [];

            /// <summary>
            /// More measures, only to be looked at (they do not count yet): how many hitsound additions (whistles, claps, finishes) an object has on average; how many finishes there
            /// are per ms; the share of objects that come at most a quarter of a beat after the one before (streams); the share of objects that are sliders; whether the map has kiai
            /// there (0..1); the bass's share of the song's level there; and how much louder the song is there than in the 16 beats before (0.5 = as loud, more = louder).
            /// </summary>
            public double[] Additions = [], Finishes = [], Streams = [], SliderShare = [], Kiai = [], BassShare = [], Rise = [];
        }

        /// <summary>
        /// The song (from its first object to the end of its last) cut into stretches of <see cref="DanmakusuBalance.BOSS_FILL_STEP_BEATS"/> beats on its beat grid, each measured: how loud
        /// its hitsounds are (the loudest one of every object, on average), how loud the song is there (all three bands together), how strong its treble, mid and bass are, how fast its
        /// fastest slider is and how many objects it has for its length (and, only to be looked at, its drums, half-time drums and how far apart its objects are) - each as a share of the
        /// most in the map - and scored by the BOSS_FILL_*_WEIGHT values (public so that the measures can be looked at for a map).
        /// </summary>
        public static StretchAnalysis AnalyseStretches(IBeatmap beatmap, SongBands? song)
        {
            var result = new StretchAnalysis();

            if (beatmap.HitObjects.Count == 0)
                return result;

            double songStart = beatmap.HitObjects.Min(h => h.StartTime);
            double songEnd = beatmap.HitObjects.Max(h => h.GetEndTime());

            // the stretches, one after the other, each as many beats long as the timing where it starts says, on the beat grid of the timing (they start where a bar of the
            // timing starts, so that sections begin and end where the music's phrases do)
            var stretches = result.Stretches;
            var firstTiming = beatmap.ControlPointInfo.TimingPointAt(songStart);
            double firstLength = Math.Max(DanmakusuBalance.CapBeat(firstTiming.BeatLength), 1) * DanmakusuBalance.BOSS_FILL_STEP_BEATS;
            double gridStart = firstTiming.Time + Math.Floor((songStart - firstTiming.Time) / firstLength) * firstLength;

            for (double t = gridStart; t < songEnd;)
            {
                var timing = beatmap.ControlPointInfo.TimingPointAt(t);
                double length = Math.Max(DanmakusuBalance.CapBeat(timing.BeatLength), 1) * DanmakusuBalance.BOSS_FILL_STEP_BEATS;

                // (a new timing point starts its own grid)
                var next = beatmap.ControlPointInfo.TimingPoints.FirstOrDefault(point => point.Time > t + 1 && point.Time < t + length - 1);
                double end = next?.Time ?? t + length;

                stretches.Add((t, Math.Min(end, songEnd)));
                t = end;
            }

            int n = stretches.Count;
            var volume = new double[n];
            var treble = new double[n];
            var bass = new double[n];
            var mid = new double[n];
            var slider = new double[n];
            var density = new double[n];
            var drumRate = new double[n];
            var drumStrength = new double[n];
            var halfRate = new double[n];
            var halfStrength = new double[n];
            var distance = new double[n];
            var additions = new double[n];
            var finishes = new double[n];
            var streams = new double[n];
            var sliderShare = new double[n];
            var kiai = new double[n];

            for (int i = 0; i < n; i++)
            {
                var (start, end) = stretches[i];
                int objects = 0, jumps = 0, sliders = 0, quick = 0;
                OsuHitObject? before = null;
                double beatLength = Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(start).BeatLength), 1);
                kiai[i] = beatmap.ControlPointInfo.EffectPointAt((start + end) / 2).KiaiMode ? 1 : 0;

                foreach (var hitObject in beatmap.HitObjects)
                {
                    if (hitObject.StartTime >= end)
                        break;

                    // (how far apart the objects are: from where the one before ends to where this one starts; spinners do not count)
                    if (hitObject is OsuHitObject osuObject && hitObject is not Spinner)
                    {
                        if (hitObject.StartTime >= start && before != null)
                        {
                            distance[i] += Vector2.Distance(ConvertedModes.BaseEnd(before), ConvertedModes.BaseStart(osuObject)); // (a mania or taiko ring as it is outside the boss sections: the sections must not depend on themselves)
                            jumps++;

                            if (hitObject.StartTime - before.GetEndTime() <= beatLength / 4 + 1)
                                quick++;
                        }

                        before = osuObject;
                    }

                    if (hitObject.StartTime < start)
                        continue;

                    objects++;
                    volume[i] += hitObject.Samples.Count > 0 ? hitObject.Samples.Max(sample => sample.Volume) : 100;
                    additions[i] += hitObject.Samples.Count(sample => sample.Name != HitSampleInfo.HIT_NORMAL);
                    finishes[i] += hitObject.Samples.Count(sample => sample.Name == HitSampleInfo.HIT_FINISH);

                    if (hitObject is Slider s)
                    {
                        slider[i] = Math.Max(slider[i], s.Velocity);
                        sliders++;
                    }
                }

                volume[i] = objects > 0 ? volume[i] / objects : 0;
                additions[i] = objects > 0 ? additions[i] / objects : 0;
                finishes[i] /= Math.Max(end - start, 1);
                streams[i] = jumps > 0 ? (double)quick / jumps : 0;
                sliderShare[i] = objects > 0 ? (double)sliders / objects : 0;
                distance[i] = jumps > 0 ? distance[i] / jumps : 0;
                treble[i] = song?.Treble(start, end) ?? 0;
                bass[i] = song?.Bass(start, end) ?? 0;
                mid[i] = song?.Mid(start, end) ?? 0;
                density[i] = objects / Math.Max(end - start, 1); // (objects per ms: the last stretch can be shorter)

                if (song != null)
                {
                    song.Drums(start, end, out drumRate[i], out drumStrength[i]);
                    song.HalfTimeDrums(start, end, out halfRate[i], out halfStrength[i]);
                }
            }

            // (how loud the song is there: all three bands together)
            var level = new double[n];

            for (int i = 0; i < n; i++)
                level[i] = bass[i] + mid[i] + treble[i];

            // (the bass's share of the level, and how much louder than the 16 beats before; both before the measures become shares of the most)
            var bassShare = new double[n];
            var rise = new double[n];
            int back = (int)Math.Round(DanmakusuBalance.BOSS_FILL_PHRASE_BEATS / DanmakusuBalance.BOSS_FILL_STEP_BEATS);

            for (int i = 0; i < n; i++)
            {
                bassShare[i] = level[i] > 1e-9 ? bass[i] / level[i] : 0;
                double before = 0;
                int count = 0;

                for (int k = Math.Max(0, i - back); k < i; k++)
                {
                    before += level[k];
                    count++;
                }

                double ratio = count > 0 && before > 1e-9 ? level[i] / (before / count) : 1;
                rise[i] = Math.Clamp(ratio / 2, 0, 1); // (0.5 = as loud as before, 1 = twice as loud or more)
            }

            // (every measure as a share of the most there is of it in the map)
            static void normalise(double[] values)
            {
                double most = values.Length > 0 ? values.Max() : 0;

                for (int i = 0; i < values.Length; i++)
                    values[i] = most > 0 ? values[i] / most : 0;
            }

            foreach (var values in new[] { volume, treble, bass, mid, slider, density, drumRate, drumStrength, halfRate, halfStrength, distance, level, additions, finishes, bassShare })
                normalise(values);

            result.Additions = additions;
            result.Finishes = finishes;
            result.Streams = streams;
            result.SliderShare = sliderShare;
            result.Kiai = kiai;
            result.BassShare = bassShare;
            result.Rise = rise;

            // (the drums, and the drums at half their usual frequency (a dubstep drop): how often they hit and how hard, half and half)
            var drums = new double[n];
            var halfTime = new double[n];

            for (int i = 0; i < n; i++)
            {
                drums[i] = (drumRate[i] + drumStrength[i]) / 2;
                halfTime[i] = (halfRate[i] + halfStrength[i]) / 2;
            }

            // (what counts: see the BOSS_FILL_*_WEIGHT values, a negative one counts against; without the song its measures count for nothing. The rest is measured, to be looked at)
            double audio = song != null ? 1 : 0;
            var weighted = new (double[] Values, double Weight)[]
            {
                (volume, DanmakusuBalance.BOSS_FILL_HITSOUND_WEIGHT),
                (slider, DanmakusuBalance.BOSS_FILL_SLIDER_WEIGHT),
                (density, DanmakusuBalance.BOSS_FILL_DENSITY_WEIGHT),
                (distance, DanmakusuBalance.BOSS_FILL_DISTANCE_WEIGHT),
                (additions, DanmakusuBalance.BOSS_FILL_ADDITIONS_WEIGHT),
                (finishes, DanmakusuBalance.BOSS_FILL_FINISHES_WEIGHT),
                (streams, DanmakusuBalance.BOSS_FILL_STREAMS_WEIGHT),
                (sliderShare, DanmakusuBalance.BOSS_FILL_SLIDER_SHARE_WEIGHT),
                (kiai, DanmakusuBalance.BOSS_FILL_KIAI_WEIGHT),
                (level, DanmakusuBalance.BOSS_FILL_LEVEL_WEIGHT * audio),
                (treble, DanmakusuBalance.BOSS_FILL_TREBLE_WEIGHT * audio),
                (mid, DanmakusuBalance.BOSS_FILL_MID_WEIGHT * audio),
                (bass, DanmakusuBalance.BOSS_FILL_BASS_WEIGHT * audio),
                (bassShare, DanmakusuBalance.BOSS_FILL_BASS_SHARE_WEIGHT * audio),
                (drums, DanmakusuBalance.BOSS_FILL_DRUM_WEIGHT * audio),
                (rise, DanmakusuBalance.BOSS_FILL_RISE_WEIGHT * audio),
            };

            double total = weighted.Sum(w => w.Weight);
            total = Math.Abs(total) > 1e-9 ? total : 1;
            var score = new double[n];

            for (int i = 0; i < n; i++)
            {
                double sum = 0;

                foreach (var (values, weight) in weighted)
                    sum += values[i] * weight;

                score[i] = sum / total;
            }

            result.Volume = volume;
            result.Treble = treble;
            result.Bass = bass;
            result.Mid = mid;
            result.Slider = slider;
            result.Density = density;
            result.Drums = drums;
            result.HalfTime = halfTime;
            result.Distance = distance;
            result.Level = level;
            result.Score = score;
            return result;
        }

        /// <summary>The sections the map itself gives a boss: its breaks, kiai sections and spinners.</summary>
        private static List<(double start, double end, BossKind kind, bool restartKiaiCount)> mapSections(IBeatmap beatmap)
        {
            bool longEnough(double start, double end)
                => end - start >= Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(start).BeatLength), 1) * DanmakusuBalance.BOSS_MIN_BREAK_BEATS;

            var sections = new List<(double start, double end, BossKind kind, bool restartKiaiCount)>();
            var breakBosses = new List<(double start, double end)>();
            var kiaiBosses = new List<(double start, double end)>();
            double lastKiaiStart = double.MinValue;

            foreach (var breakPeriod in beatmap.Breaks)
            {
                double bossStart = breakPeriod.StartTime, bossEnd = breakPeriod.EndTime;

                // the boss of a break keeps 4 beats away from spinners: he is gone 4 beats before a spinner that follows, and
                // does not come back until 4 beats after a spinner that came before
                foreach (var spinnerObject in beatmap.HitObjects.OfType<Spinner>())
                {
                    double clearance = Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(spinnerObject.StartTime).BeatLength), 1) * DanmakusuBalance.BOSS_SPINNER_CLEARANCE_BEATS;

                    if (spinnerObject.StartTime >= breakPeriod.StartTime)
                        bossEnd = Math.Min(bossEnd, spinnerObject.StartTime - clearance);
                    else if (spinnerObject.EndTime <= breakPeriod.EndTime)
                        bossStart = Math.Max(bossStart, spinnerObject.EndTime + clearance);
                }

                // (the whole break stays the break boss's, even where he is not there: the kiai boss does not take over)
                breakBosses.Add((breakPeriod.StartTime, breakPeriod.EndTime));

                if (!longEnough(bossStart, bossEnd))
                    continue;

                sections.Add((bossStart, bossEnd, BossKind.Break, false));
            }

            // kiai sections: from a control point with kiai on to the next one (the last one lasts until the last object ends)
            double lastObjectEnd = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Max(h => h.GetEndTime()) : 0;
            var effectPoints = beatmap.ControlPointInfo.EffectPoints;

            for (int i = 0; i < effectPoints.Count; i++)
            {
                if (!effectPoints[i].KiaiMode)
                    continue;

                double kiaiStart = effectPoints[i].Time;
                double kiaiEnd = i + 1 < effectPoints.Count ? effectPoints[i + 1].Time : lastObjectEnd;

                // cut out the parts that belong to the boss of a break
                var pieces = new List<(double start, double end)> { (kiaiStart, kiaiEnd) };

                foreach (var (breakStart, breakEnd) in breakBosses)
                {
                    var next = new List<(double start, double end)>();

                    foreach (var (start, end) in pieces)
                    {
                        if (breakEnd <= start || breakStart >= end)
                        {
                            next.Add((start, end));
                            continue;
                        }

                        if (breakStart > start)
                            next.Add((start, breakStart));

                        if (breakEnd < end)
                            next.Add((breakEnd, end));
                    }

                    pieces = next;
                }

                foreach (var (start, end) in pieces)
                {
                    if (!longEnough(start, end))
                        continue;

                    // a break in between starts the kiai beam count over (1 beam again)
                    bool restartCount = false;

                    foreach (var passedBreak in beatmap.Breaks)
                    {
                        if (passedBreak.EndTime > lastKiaiStart && passedBreak.StartTime <= start)
                            restartCount = true;
                    }

                    lastKiaiStart = start;

                    kiaiBosses.Add((start, end));
                    sections.Add((start, end, BossKind.Kiai, restartCount));
                }
            }

            // spinners get a boss too, unless the boss of a kiai section is already there
            foreach (var spinnerObject in beatmap.HitObjects.OfType<Spinner>())
            {
                bool inKiai = false;

                foreach (var (start, end) in kiaiBosses)
                {
                    if (spinnerObject.StartTime >= start && spinnerObject.StartTime <= end)
                        inKiai = true;
                }

                if (!inKiai && spinnerObject.EndTime > spinnerObject.StartTime)
                    sections.Add((spinnerObject.StartTime, spinnerObject.EndTime, BossKind.Spinner, false));
            }

            return sections;
        }

        /// <summary>
        /// The boss periods of a map as far as the HP bar needs them (when each section starts and ends, and which boss slot has it), without planning any bullets: the sections, cut for the bosses if there are
        /// more of them than sections, are taken in turn by the bosses; with <paramref name="alwaysDouble"/> two of them have every section, with <paramref name="alwaysTriple"/> three.
        /// </summary>
        public static BossPeriod[] BarPeriods(IBeatmap beatmap, int bossCount, bool alwaysDouble = false, Func<SongBands?>? bands = null, bool alwaysTriple = false)
        {
            int atOnce = alwaysTriple && bossCount >= 3 ? 3 : alwaysDouble && bossCount >= 2 ? 2 : 1;
            var ordered = cutForBosses(beatmap, Sections(beatmap, bands), (bossCount + atOnce - 1) / atOnce);
            var periods = new List<BossPeriod>();
            int[] slots = bossSlots(beatmap, ordered, bossCount, atOnce);

            for (int i = 0; i < ordered.Count && bossCount > 0; i++)
            {
                var (start, end, kind, _) = ordered[i];
                int slot = slots[i];

                periods.Add(new BossPeriod { Start = start, ShootStart = start, End = end, Kind = kind, BossSlot = slot });

                for (int rank = 1; rank < atOnce; rank++)
                    periods.Add(new BossPeriod { Start = start, ShootStart = start, End = end, Kind = kind, BossSlot = (slot + rank) % bossCount, Rank = rank });
            }

            return periods.ToArray();
        }

        /// <summary>How many places in the list of bosses the next section is on from this one: two or three bosses have a section, so with more than that the next section has the next ones.</summary>
        private static int bossStep(int bossCount, int atOnce) => bossCount > atOnce ? atOnce : 1;

        /// <summary>The shortest piece of a section at <paramref name="time"/> that is worth a boss: one attack and one pause.</summary>
        private static double shortestPiece(IBeatmap beatmap, double time)
            => Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(time).BeatLength), 1) * (DanmakusuBalance.BOSS_LASER_BURST_BEATS + DanmakusuBalance.BOSS_LASER_PAUSE_BEATS);

        /// <summary>
        /// How many bosses a map has room for, one at a time, with an attack and a pause of the default length each, whoever they are: a section holds as many as fit, a spinner one when it is long enough, as it is not cut, and a build-up none, as it
        /// leads into the section after it (at least one boss, whatever the map).
        /// </summary>
        public static int MostBosses(IBeatmap beatmap, Func<SongBands?>? bands = null)
        {
            int most = 0;

            foreach (var (start, end, kind, restartKiaiCount) in Sections(beatmap, bands))
            {
                if (kind != BossKind.Spinner)
                    most += (int)((end - start + 1) / shortestPiece(beatmap, start));
                else if (!restartKiaiCount && holdsABoss(beatmap, start, end))
                    most++;
            }

            return Math.Clamp(most, 1, BossRoster.All.Count);
        }

        /// <summary>Does this piece of a map hold an attack and a pause of the default length?</summary>
        private static bool holdsABoss(IBeatmap beatmap, double start, double end) => end - start >= shortestPiece(beatmap, start) - 1;

        /// <summary>
        /// Which boss has each piece. The pieces that hold an attack and a pause go round the bosses in turn, so as long as there are as many of those as bosses every boss gets one; a piece too short for that goes on with the boss of the piece before
        /// it, and a made-up spinner before a part of the song (a build-up: a spinner marked with the kiai flag) leads into the piece after it. (A map with too few of those pieces for its bosses: the short ones take turns as well.)
        /// </summary>
        private static int[] bossSlots(IBeatmap beatmap, List<(double start, double end, BossKind kind, bool restartKiaiCount)> ordered, int bossCount, int atOnce)
        {
            var slots = new int[ordered.Count];
            int count = Math.Max(bossCount, 1);
            int step = bossStep(bossCount, atOnce);
            int needed = (bossCount + atOnce - 1) / atOnce;

            bool buildUp(int i) => ordered[i].kind == BossKind.Spinner && ordered[i].restartKiaiCount;
            bool strict = Enumerable.Range(0, ordered.Count).Count(i => !buildUp(i) && holdsABoss(beatmap, ordered[i].start, ordered[i].end)) >= needed;

            int turn = 0, current = -1;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (buildUp(i))
                    slots[i] = -1;
                else if (strict && !holdsABoss(beatmap, ordered[i].start, ordered[i].end))
                    slots[i] = current;
                else
                    slots[i] = current = turn++ * step % count;
            }

            // (what has nothing before it to go on with, and the build-ups: as the piece after it)
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                if (slots[i] < 0)
                    slots[i] = i + 1 < ordered.Count ? slots[i + 1] : Math.Max(current, 0);
            }

            return slots;
        }

        /// <summary>
        /// The sections of a map with <paramref name="bosses"/> taking them in turn: as long as there are fewer pieces that hold an attack and a pause than bosses, the sections are cut into even pieces, the longest pieces first, down to one
        /// attack and one pause. (A spinner is not cut, and a build-up takes no turn.)
        /// </summary>
        private static List<(double start, double end, BossKind kind, bool restartKiaiCount)> cutForBosses(IBeatmap beatmap, List<(double start, double end, BossKind kind, bool restartKiaiCount)> sections, int bosses)
        {
            var pieces = new int[sections.Count];
            Array.Fill(pieces, 1);

            bool buildUp(int i) => sections[i].kind == BossKind.Spinner && sections[i].restartKiaiCount;
            int enough() => Enumerable.Range(0, sections.Count).Sum(i => !buildUp(i) && holdsABoss(beatmap, sections[i].start, sections[i].start + (sections[i].end - sections[i].start) / pieces[i]) ? pieces[i] : 0);

            if (enough() >= bosses)
                return sections;

            while (enough() < bosses)
            {
                int best = -1;
                double longest = 0;

                for (int i = 0; i < sections.Count; i++)
                {
                    double piece = (sections[i].end - sections[i].start) / (pieces[i] + 1);

                    if (sections[i].kind != BossKind.Spinner && holdsABoss(beatmap, sections[i].start, sections[i].start + piece) && piece > longest)
                    {
                        best = i;
                        longest = piece;
                    }
                }

                if (best < 0)
                    break;

                pieces[best]++;
            }

            var cut = new List<(double start, double end, BossKind kind, bool restartKiaiCount)>();

            for (int i = 0; i < sections.Count; i++)
            {
                var (start, end, kind, restart) = sections[i];
                double length = (end - start) / pieces[i];

                for (int k = 0; k < pieces[i]; k++)
                    cut.Add((start + k * length, start + (k + 1) * length, kind, restart && k == 0));
            }

            return cut;
        }

        /// <summary>The rings, and (out) the lasers of the sliders that have a body whistle.</summary>
        public static BulletEmitter[] Plan(IBeatmap beatmap, out LaserEmitter[] lasers, out BossPeriod[] bosses, IReadOnlyList<BossInfo>? matchedBosses = null, bool alwaysDouble = false, bool noBoss = false,
                                           Func<SongBands?>? bands = null, bool alwaysTriple = false)
        {
            var difficulty = beatmap.Difficulty;

            bool inKiaiAt(double time) => beatmap.ControlPointInfo.EffectPointAt(time).KiaiMode;
            int tickCountAt(double time) => DanmakusuBalance.BulletsPerSliderTick(difficulty.OverallDifficulty, inKiaiAt(time));
            int spiralArms = DanmakusuBalance.SpiralArms(difficulty.OverallDifficulty);
            float baseSpeed = DanmakusuBalance.BaseBulletSpeed(difficulty.ApproachRate) / 1000f; // px/ms
            float radius = DanmakusuBalance.BulletRadius(difficulty.CircleSize);
            float bossCountFactor = 1f; // (the boss's own patterns have this many times the usual number of bullets per ring: Yukari's half)
            bool bossStraight = false; // (the boss's own arrowheads fly straight like ordinary bullets instead of being pulled towards the player: Cirno's)
            float starSizeBoost = 1f; // (the boss's own stars / hearts are this many times bigger than the usual ones: Koishi's hearts)
            int currentRank = 0; // (which boss of the section is being planned: 1, 2 = the second and third boss of the section that has more than one)
            bool halveCounts = false; // (the section of two bosses: every beam burst has half as many beams)
            bool thirdCounts = false; // (...and of three: a third as many, when halveCounts is on too)
            float ellipseGravity = DanmakusuBalance.EllipseGravity(difficulty.CircleSize); // falling hitclap bullets: scales like the circle size
            float homingReleaseRadius = DanmakusuBalance.HOMING_RELEASE_RADIUS_FACTOR * DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize); // arrowheads stop homing this close to the player

            double mainBpm = 60000 / Math.Max(DanmakusuBalance.CapBeat(beatmap.GetMostCommonBeatLength()), 1);

            // (a map of another mode, see ConvertedModes: catch objects shoot from where they land and from the top above it, in fans; a taiko big note shoots arcs, not bullets)
            int convertedFrom = ConvertedModes.ModeOf(beatmap);
            bool catchMap = convertedFrom == ConvertedModes.CATCH;
            bool landing = false; // (the rings being added are those of a catch object that lands)
            bool ringMap = convertedFrom is ConvertedModes.MANIA or ConvertedModes.TAIKO;
            float bulletFactor = ringMap ? DanmakusuBalance.CONVERT_RING_BULLET_SPEED : 1f; // (every bullet of a mania or taiko map flies this many times as fast, the objects' and the bosses' alike: see speedAt)

            var legacyControlPoints = beatmap.ControlPointInfo as LegacyControlPointInfo;
            // a bullet lives until it is past the far corner of the playfield plus a large margin, i.e. off the real screen.
            const float m = DanmakusuBalance.OFFSCREEN_MARGIN;
            var corners = new[]
            {
                new Vector2(-m, -m),
                new Vector2(OsuPlayfield.BASE_SIZE.X + m, -m),
                new Vector2(-m, OsuPlayfield.BASE_SIZE.Y + m),
                OsuPlayfield.BASE_SIZE + new Vector2(m, m),
            };

            var emitters = new List<BulletEmitter>();
            var ricochetArea = new RicochetArea(); // shared by all bouncing rings; the field sets it to the real screen
            var laserList = new List<LaserEmitter>();
            int spinnerIndex = 0;
            var spinnerStars = new List<BulletEmitter>(); // the spiral rings of the spinner objects (a boss's mechanic may change what they look like)

            // 1/8-beat clock: number of eighth notes since the first timing point, accumulated across BPM changes.
            var timingPoints = beatmap.ControlPointInfo.TimingPoints;
            double mainBeatLength = Math.Max(DanmakusuBalance.CapBeat(beatmap.GetMostCommonBeatLength()), 1);
            var eighthsAtTimingPoint = new double[timingPoints.Count];

            for (int i = 1; i < timingPoints.Count; i++)
                eighthsAtTimingPoint[i] = eighthsAtTimingPoint[i - 1] + (timingPoints[i].Time - timingPoints[i - 1].Time) / Math.Max(DanmakusuBalance.CapBeat(timingPoints[i - 1].BeatLength), 1) * 8;

            double eighthsAt(double time)
            {
                if (timingPoints.Count == 0)
                    return time / mainBeatLength * 8;

                int index = 0;
                int lo = 0, hi = timingPoints.Count - 1;

                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;

                    if (timingPoints[mid].Time <= time)
                    {
                        index = mid;
                        lo = mid + 1;
                    }
                    else
                        hi = mid - 1;
                }

                return eighthsAtTimingPoint[index] + (time - timingPoints[index].Time) / Math.Max(DanmakusuBalance.CapBeat(timingPoints[index].BeatLength), 1) * 8;
            }

            // AR sets the base; slider speed (SM x SV) and BPM (local BPM relative to the map's standard BPM) scale it.
            float speedAt(double time)
            {
                double localBpm = 60000 / Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(time).BeatLength), 1);
                double sliderVelocity = legacyControlPoints?.DifficultyPointAt(time).SliderVelocity ?? 1;

                return baseSpeed
                       * DanmakusuBalance.SliderSpeedFactor(difficulty.SliderMultiplier, sliderVelocity)
                       * DanmakusuBalance.LocalBpmFactor(localBpm, mainBpm)
                       * bulletFactor;
            }

            // (the beams fly as fast as ever in mania and taiko too: only the bullets are faster)
            float beamSpeedAt(double time) => speedAt(time) / bulletFactor;

            BulletEmitter createEmitter(double time, Vector2 origin, float spawnRadius, float speed, Vector2[] directions, int comboIndex, float bulletRadius, BulletShape shape = BulletShape.Circle, bool aimAtPlayer = false, float[]? radialScales = null, float minRadialScale = 1f, bool ricochet = false, bool followsBoss = false, bool straight = false, float aimOffset = 0f)
            {
                if (shape == BulletShape.Slice)
                    bulletRadius *= DanmakusuBalance.SLICE_LENGTH_FACTOR; // (the radius of a slice is its half length)

                float farthestCorner = 0;
                foreach (var corner in corners)
                    farthestCorner = Math.Max(farthestCorner, Vector2.Distance(origin, corner));

                double lifetime = ricochet ? 6000f / (speed * minRadialScale) : (farthestCorner + bulletRadius * 2 - spawnRadius) / (speed * minRadialScale);

                if (shape == BulletShape.Ellipse)
                {
                    // falling bullets: the pull is towards the player, whichever way that is. Even a bullet shot straight against it comes back
                    // and, measured along the pull, is past every point of the simulated area after this long.
                    float g = ellipseGravity;
                    float reachAlongPull = farthestCorner + bulletRadius * 2 + spawnRadius;
                    lifetime = Math.Min(lifetime, (speed + Math.Sqrt(speed * speed + 2 * g * reachAlongPull)) / g);
                }
                else if (shape == BulletShape.Triangle && !straight)
                {
                    // pulled bullets leave when they have passed the player and crossed the screen border; this is just a safety limit
                    lifetime = DanmakusuBalance.HOMING_LIFETIME_MS;
                }

                return new BulletEmitter
                {
                    Time = time,
                    Origin = origin,
                    SpawnRadius = spawnRadius,
                    Speed = speed,
                    ComboIndex = comboIndex,
                    VisualRadius = bulletRadius,
                    Shape = shape,
                    AimAtPlayer = aimAtPlayer,
                    AimOffset = aimOffset,
                    HitRadius = bulletRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Directions = directions,
                    RadialScales = radialScales,
                    MinRadialScale = minRadialScale,
                    Ricochet = ricochet,
                    FollowsBoss = followsBoss,
                    BossRank = followsBoss ? currentRank : 0,
                    Straight = straight,
                    Gravity = shape == BulletShape.Ellipse || (shape == BulletShape.Triangle && !straight) ? ellipseGravity : 0f,
                    ReleaseRadius = shape == BulletShape.Triangle && !straight ? homingReleaseRadius : 0f,
                    Area = ricochet || (shape == BulletShape.Triangle && !straight) ? ricochetArea : null, // the screen: where bouncing bullets bounce and pulled bullets disappear
                    // (the screen can be big: two walls are at most ~ 2 screen widths of flight away, generously 6000)
                    Lifetime = lifetime,
                };
            }

            // a slider with a whistle on its body: a laser from the slider ball for as long as the slider lasts.
            // (Junko's: more beams, from the boss, as wide as a slider's beam, and each slice narrows to nothing again once it is as old as the slider is long)
            static double GhostMsOf(double beatMs) => beatMs * 4; // (Junko's slices are see-through and harmless for their first four beats)

            void addLaser(Slider slider, int whistleVolume, bool purified = false, bool dissipates = false)
            {
                double duration = slider.EndTime - slider.StartTime;
                if (duration <= 0)
                    return;

                float speed = beamSpeedAt(slider.StartTime) * DanmakusuBalance.LASER_SPEED_FACTOR;
                float halfWidth = radius * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(whistleVolume); // Junko's beams are as wide as a slider's own // the whistle's volume scales the width like it does for rings (0.5x..1x)
                int beams = Math.Max(1, DanmakusuBalance.BeamsPerLaser(difficulty.OverallDifficulty)) * (purified ? DanmakusuBalance.JUNKO_BEAM_FACTOR : 1);

                if (purified)
                {
                    halfWidth *= DanmakusuBalance.JUNKO_BEAM_SIZE; // (Junko's beams are half as wide as a slider's)
                    speed *= DanmakusuBalance.JUNKO_BEAM_SPEED; // (her slices fly as fast as a slider's; the slider's path is gone along, and a slice lives, twice as long as the slider says)
                }

                // a long note of mania or a drumroll of taiko (a slider that does not move): its beams are a slider's (as many, as wide, as fast), but turn as a spinner's do, towards the player as they fire, so
                // they bend
                if (!purified && ConvertedModes.Of(slider) is { Static: true })
                {
                    float farthestCorner = corners.Max(corner => Vector2.Distance(slider.StackedPosition, corner));

                    laserList.Add(new LaserEmitter
                    {
                        StartTime = slider.StartTime,
                        EndTime = slider.EndTime,
                        ComboIndex = slider.ComboIndexWithOffsets,
                        Origin = slider.StackedPosition,
                        HalfWidth = halfWidth,
                        HitHalfWidth = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Speed = speed,
                        BeamCount = beams,
                        Directions = new Vector2[beams],
                        Rotates = true,
                        HoldBeam = true,
                        AngularSpeed = DanmakusuBalance.SpinnerLaserAngularSpeed(difficulty.OverallDifficulty),
                        GrowthPower = 2f,
                        PeakScale = 1f,
                        Lifetime = (farthestCorner + halfWidth) / speed,
                    });

                    return;
                }

                // the ball moves at a constant speed along the path (and back and forth for repeats), so equal steps of progress are equal steps of time.
                // every span (one trip along the path) gets the same number of steps, so the turning points are samples.
                int stepsPerSpan = Math.Clamp((int)Math.Ceiling(slider.Path.Distance / DanmakusuBalance.LASER_SAMPLE_SPACING), 2, 400);
                int steps = stepsPerSpan * (slider.RepeatCount + 1);
                var sampled = new Vector2[steps + 1];

                for (int i = 0; i <= steps; i++)
                    sampled[i] = slider.StackedPosition + slider.CurvePositionAt(i / (double)steps);

                // keep only the points that matter: a straight part of the path is a straight part of the beam, whatever the speed.
                // (each trip is simplified on its own, so the turning points of a repeating slider always stay)
                var keptList = new List<int>();

                for (int span = 0; span <= slider.RepeatCount; span++)
                    simplify(sampled, span * stepsPerSpan, (span + 1) * stepsPerSpan, DanmakusuBalance.LASER_SIMPLIFY_TOLERANCE, keptList);

                int[] kept = keptList.ToArray();
                var ball = new Vector2[kept.Length];
                var ballTimes = new double[kept.Length];

                for (int i = 0; i < kept.Length; i++)
                {
                    ball[i] = sampled[kept[i]];
                    ballTimes[i] = slider.StartTime + duration * kept[i] / steps / (purified ? DanmakusuBalance.JUNKO_TURN_FACTOR / DanmakusuBalance.JUNKO_TIME_STRETCH : 1.0); // (Junko's beams turn faster: the ball goes along the path that many times as fast)
                }

                // long enough for every slice to cross the largest area a slice can be in
                float reach = (OsuPlayfield.BASE_SIZE + new Vector2(m * 2, m * 2)).Length;

                // Letty's beams: every slice lives 4 beats, then narrows away and turns into bullets (see below); the beam keeps its width until then
                double beatMs = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(slider.StartTime).BeatLength), 1);
                double dissipateAfter = beatMs * DanmakusuBalance.LETTY_DISSIPATE_BEATS;

                var sliderLaser = new LaserEmitter
                {
                    StartTime = slider.StartTime,
                    EndTime = slider.EndTime,
                    ComboIndex = slider.ComboIndexWithOffsets,
                    Origin = slider.StackedPosition,
                    HalfWidth = halfWidth,
                    HitHalfWidth = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Speed = speed,
                    BeamCount = beams,
                    Directions = new Vector2[beams],
                    BallPositions = ball,
                    BallTimes = ballTimes,
                    FollowsBoss = purified,
                    BossRank = purified ? currentRank : 0,
                    PathFromOrigin = purified,
                    Grows = purified || dissipates,
                    StartHalfWidth = purified || dissipates ? halfWidth * DanmakusuBalance.LASER_PEAK_FACTOR : 0f, // full width from the start; they only narrow at the end
                    GrowthPower = 2f,
                    PeakScale = 1f,
                    GhostMs = purified ? GhostMsOf(beatMs) : 0, // (every slice of Junko's beams is see-through and harmless for its first four beats)
                    CutAge = dissipates ? dissipateAfter : purified ? 1e12 : -1, // (Junko's slices are never cut: they stay until they have flown out of the screen) // (a slice does not narrow: Letty's is gone the moment it turns into bullets, Junko's when it is that old)
                    CutStretch = dissipates ? 2 : 1, // (Letty: a slice does not narrow, it is gone the moment it turns into bullets, the earliest first)
                    ShrinkDelay = dissipates ? dissipateAfter : purified ? 1e12 : -1,
                    Lifetime = dissipates ? dissipateAfter + (slider.EndTime - slider.StartTime) + DanmakusuBalance.LASER_GROWTH_DISTANCE / speed : purified ? reach / speed : reach / speed,
                };

                // osu!catch: the stream's trail lands along the bottom: half of its beams come from where it lands, spread over 150 degrees up, the other half from the top straight above it, down
                if (catchMap && !purified && ConvertedModes.Of(slider) is { Mode: ConvertedModes.CATCH })
                {
                    float fan = DanmakusuBalance.CATCH_FAN_DEGREES * MathF.PI / 180f;

                    for (int half = 0; half < 2; half++)
                    {
                        int n = (beams - half + 1) / 2;

                        if (n <= 0)
                            continue;

                        float line = half == 0 ? DanmakusuBalance.CATCH_BOTTOM : DanmakusuBalance.CATCH_TOP;
                        var along = ball.Select(point => new Vector2(point.X, line)).ToArray();

                        laserList.Add(new LaserEmitter
                        {
                            StartTime = slider.StartTime,
                            EndTime = slider.EndTime,
                            ComboIndex = slider.ComboIndexWithOffsets,
                            Origin = along[0],
                            HalfWidth = halfWidth,
                            HitHalfWidth = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Speed = speed,
                            BeamCount = n,
                            Directions = new Vector2[n],
                            BallPositions = along,
                            BallTimes = ballTimes,
                            Fan = fan,
                            FanCentre = half == 0 ? -MathF.PI / 2f : MathF.PI / 2f,
                            WallSide = half == 0 ? 1 : -1,
                            CutAge = -1,
                            CutStretch = 1,
                            ShrinkDelay = -1,
                            GrowthPower = 2f,
                            PeakScale = 1f,
                            Lifetime = reach / speed,
                        });
                    }

                    return;
                }

                laserList.Add(sliderLaser);

                if (!dissipates)
                    return;

                // ... into bullets: every slice of the beam (one every step) turns into one bullet per beam, flying against the way the slider's path goes there
                double step = Math.Max(beatMs * DanmakusuBalance.LETTY_BULLET_INTERVAL_BEATS, duration / DanmakusuBalance.LETTY_MAX_SLICES) * (purified ? 2 : 1);
                int ringSize = DanmakusuBalance.LETTY_RING_COUNT;
                float bulletRadius = radius * DanmakusuBalance.LETTY_BULLET_SIZE;

                for (double s = slider.StartTime; s <= slider.EndTime + 0.5; s += step)
                {
                    double fired = Math.Min(s, slider.EndTime);
                    double at = fired + dissipateAfter + (fired - slider.StartTime); // (the beam is eaten away from its oldest end at half the speed it grew: a slice lives its time and then as long again as it took to fire)
                    float bulletSpeed = speedAt(at) * DanmakusuBalance.LETTY_BULLET_SPEED_FACTOR;
                    var proto = createEmitter(at, Vector2.Zero, 0f, bulletSpeed, new Vector2[beams * ringSize], slider.ComboIndexWithOffsets, bulletRadius, BulletShape.Circle);

                    emitters.Add(new BulletEmitter
                    {
                        Time = at,
                        SpawnRadius = 0f,
                        Speed = bulletSpeed,
                        ComboIndex = slider.ComboIndexWithOffsets,
                        VisualRadius = proto.VisualRadius,
                        HitRadius = proto.HitRadius,
                        Shape = BulletShape.Circle,
                        Straight = true,
                        Directions = new Vector2[beams * ringSize],
                        Origins = new Vector2[beams * ringSize],
                        Derived = true,
                        Derive = DeriveKind.Dissipate,
                        SourceLaser = sliderLaser,
                        SliceTime = fired,
                        Lifetime = reach / bulletSpeed,
                    });
                }
            }

            // a spinner's lasers: SPINNER_LASER_BEAMS beams spread evenly around the spinner centre, the first aimed at the player when the
            // spinner starts. They then slowly turn (the field steers them towards the player, 6..60 degrees per second by OD), for as long as
            // the spinner lasts. A slice keeps flying in the direction it was fired in.
            // (`spark`: Marisa's Master Spark - from an osu! circle's radius at her it widens along an outer curve to 4 circle radii over the first 4 circle radii, then opens 5 degrees to each side, almost straight, to the end;
            // it spins at an eighth of a spinner beam's speed, always the same way, instead of turning towards the player, and stays a straight line as it turns; its edges are thin)
            void addRotatingLaser(Vector2 origin, double startTime, double endTime, int comboIndex, bool followsBoss, int beams = DanmakusuBalance.SPINNER_LASER_BEAMS, bool straight = false, bool spark = false)
            {
                double duration = endTime - startTime;
                if (duration <= 0)
                    return;

                float speed = beamSpeedAt(startTime) * DanmakusuBalance.SPINNER_LASER_SPEED_FACTOR;
                float halfWidth = radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SpinnerLaserSizeScale(difficulty.OverallDifficulty) * DanmakusuBalance.SPINNER_LASER_WIDTH_FACTOR; // 1x..4x, and 20% wider the radius of a spinner bullet, by OD

                // (Reisen's short straight beams are as thick as the beam of a slider, and keep that thickness: they do not widen)
                if (straight)
                    halfWidth = radius * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

                float startHalfWidth = DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize); // a beam starts as wide as an osu! hit circle

                float farthest = 0;
                foreach (var corner in corners)
                    farthest = Math.Max(farthest, Vector2.Distance(origin, corner));

                laserList.Add(new LaserEmitter
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    ComboIndex = comboIndex,
                    Origin = origin,
                    FollowsBoss = followsBoss,
                    KeepsOrigin = straight,
                    BossRank = followsBoss ? currentRank : 0,
                    HalfWidth = halfWidth,
                    HitHalfWidth = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Speed = speed,
                    BeamCount = beams,
                    Directions = new Vector2[beams],
                    Rotates = true,
                    AngularSpeed = straight ? 0f : DanmakusuBalance.SpinnerLaserAngularSpeed(difficulty.OverallDifficulty),
                    Grows = !straight,
                    StartHalfWidth = straight ? halfWidth : startHalfWidth,
                    // the beams of the bosses (kiai and break) widen 50% faster at first, ever more slowly, and end up 20% wider than the spinner's
                    GrowthPower = followsBoss ? DanmakusuBalance.BOSS_LASER_GROWTH_POWER : 2f,
                    PeakScale = followsBoss && !straight ? DanmakusuBalance.BOSS_LASER_WIDTH_FACTOR : 1f,
                    Lifetime = straight ? (farthest + halfWidth) / speed : (farthest + Math.Max(halfWidth * DanmakusuBalance.LASER_PEAK_FACTOR * (followsBoss ? DanmakusuBalance.BOSS_LASER_WIDTH_FACTOR : 1f), startHalfWidth)) / speed,
                });

                if (!spark)
                    return;

                // Marisa's Master Spark (see above)
                float circle = DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize);
                var sparkLaser = laserList[^1];
                laserList[^1] = new LaserEmitter
                {
                    StartTime = sparkLaser.StartTime,
                    EndTime = sparkLaser.EndTime,
                    ComboIndex = sparkLaser.ComboIndex,
                    Origin = sparkLaser.Origin,
                    FollowsBoss = sparkLaser.FollowsBoss,
                    BossRank = sparkLaser.BossRank,
                    HalfWidth = circle * DanmakusuBalance.MARISA_SPARK_WIDE / DanmakusuBalance.LASER_PEAK_FACTOR,
                    HitHalfWidth = circle * DanmakusuBalance.MARISA_SPARK_WIDE / DanmakusuBalance.LASER_PEAK_FACTOR * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Speed = speed * DanmakusuBalance.MARISA_SPARK_SPEED, // (it comes out twice as fast: it has grown in half the time)
                    BeamCount = beams,
                    Directions = new Vector2[beams],
                    Rotates = true,
                    Spins = true,
                    Rigid = true, // (it stays a straight line as it turns)
                    ThinOutlines = true,
                    AngularSpeed = DanmakusuBalance.SpinnerLaserAngularSpeed(difficulty.OverallDifficulty) * DanmakusuBalance.MARISA_SPARK_SPIN,
                    Grows = true,
                    StartHalfWidth = circle * DanmakusuBalance.MARISA_SPARK_THICKNESS,
                    GrowthPower = 2f,
                    PeakScale = 1f,
                    GrowthDistance = circle * DanmakusuBalance.MARISA_SPARK_GROW, // (it widens ever more slowly, so it runs smoothly into the straight beam)
                    Lifetime = sparkLaser.Lifetime,
                    TurnsFrom = sparkLaser.StartTime + circle * DanmakusuBalance.MARISA_SPARK_GROW / Math.Max(speed * DanmakusuBalance.MARISA_SPARK_SPEED, 1e-4f), // (once it has grown: its widening part has come out)
                    TurnSpeed = DanmakusuBalance.MARISA_SPARK_TURN_DEGREES * MathF.PI / 180f / 1000f,
                    KeepsSize = true,
                    Immune = true,
                };
            }

            // Doremy's own shots: two continuous chains of plain bullets, each a ring of three fired again and again, turning a step further every time; the two chains turn opposite ways
            void addDoremyChains(Vector2 origin, double startTime, double endTime, double intervalFactor)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double interval = DanmakusuBalance.SpiralInterval(beatLength, difficulty.OverallDifficulty) * intervalFactor;
                int count = DanmakusuBalance.DOREMY_CHAIN_BULLETS;
                int shot = 0;

                for (double t = startTime; t < endTime; t += interval, shot++)
                {
                    foreach (float way in new[] { 1f, -1f })
                    {
                        var directions = RingLayout.Create(RingShape.Circle, count, way * shot * DanmakusuBalance.SPIRAL_STEP, out _);
                        var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, radius, BulletShape.Circle, followsBoss: true, straight: true);
                        ring.OwnShot = true;
                        emitters.Add(ring);
                    }
                }
            }

            // (a ring that flew straight bounces off the walls from now on)
            void makeRicochet(BulletEmitter ring)
            {
                ring.Ricochet = true;
                ring.Area = ricochetArea;
                ring.Lifetime = Math.Max(ring.Lifetime, 6000f / (ring.Speed * ring.MinRadialScale));
            }

            // Gengetsu's own shots (in a break and a kiai; in a spinner it is the spinner's stars, as basic bullets): double rings of basic bullets - two rings at once, the inner one slower and turned half a step - every beat
            // (Hisami's are the same with four rings at once, every next one slower again)
            void addDoubleRings(Vector2 origin, double startTime, double endTime, int layers = 2, double everyBeats = DanmakusuBalance.GENGETSU_RING_EVERY_BEATS, int count = DanmakusuBalance.GENGETSU_RING_BULLETS)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double step = beatLength * everyBeats;
                int shot = 0;

                for (double t = startTime; t < endTime; t += step, shot++)
                {
                    for (int layer = 0; layer < layers; layer++)
                    {
                        float phase = (float)(shot * golden_angle % MathF.Tau) + layer * MathF.PI / count;
                        var directions = RingLayout.Create(RingShape.Circle, count, phase, out _);
                        var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t) * MathF.Pow(DanmakusuBalance.GENGETSU_INNER_SPEED, layer), directions, 0, radius, BulletShape.Circle, followsBoss: true, straight: true);
                        ring.Layout = RingShape.Circle;
                        ring.LayoutPhase = phase;
                        ring.OwnShot = true;
                        emitters.Add(ring);
                    }
                }
            }

            // Sanae's own ring (also what her spinner shoots): 30 stars that ricochet, half the size of a finish's, in the outline of a five-pointed star; the star of every ring is turned 30 degrees a second
            // further since her section began, and every second ring turns the other way
            BulletEmitter sanaeRing(double t, Vector2 origin, double sectionStart, int shot, bool followsBoss)
            {
                float turn = (float)((t - sectionStart) / 1000.0 * DanmakusuBalance.SANAE_SPIN_DEGREES_PER_SECOND * Math.PI / 180.0) * (shot % 2 == 0 ? 1f : -1f);
                var directions = RingLayout.Create(RingShape.Star, DanmakusuBalance.SANAE_RING_BULLETS, turn, out var scales);
                var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, radius * DanmakusuBalance.FINISH_BULLET_SCALE * DanmakusuBalance.SANAE_STAR_SIZE, BulletShape.Star,
                    radialScales: scales, minRadialScale: scales?.Min() ?? 1f, ricochet: true, followsBoss: followsBoss);
                ring.Layout = RingShape.Star;
                ring.LayoutPhase = turn;
                ring.OwnShot = true;
                ring.KeepCount = true; // (her own rings are not made denser by her own mechanic either: see planMechanics)
                return ring;
            }

            void addSanaeRings(Vector2 origin, double startTime, double endTime)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                int shot = 0;

                for (double t = startTime; t < endTime; t += beatLength * DanmakusuBalance.SANAE_RING_EVERY_BEATS, shot++)
                    emitters.Add(sanaeRing(t, origin, startTime, shot, true));
            }

            // Byakuren's own beams: one from each of five points round her (a pentagon), as thin as a slider's beam. In her attacks every one starts pointing straight away from the player
            // and is steered round towards the player (homing: the slices keep the way they were fired, so the beams curl in); in her spinner they point straight out from her and spin.
            void addByakurenBeams(double startTime, double endTime, double beatMs, bool spins)
            {
                if (endTime <= startTime)
                    return;

                float speed = beamSpeedAt(startTime) * DanmakusuBalance.LASER_SPEED_FACTOR;
                float halfWidth = radius * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);
                float reach = (OsuPlayfield.BASE_SIZE + new Vector2(m * 2, m * 2)).Length;
                int points = DanmakusuBalance.BYAKUREN_BEAM_POINTS;

                for (int k = 0; k < points; k++)
                {
                    float at = -MathF.PI / 2f + k * MathF.Tau / points;
                    Vector2 outward = new Vector2(MathF.Cos(at), MathF.Sin(at));

                    laserList.Add(new LaserEmitter
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Origin = Vector2.Zero, // (the field puts it where she is, plus the point's offset)
                        FollowsBoss = true,
                        BossRank = currentRank,
                        BossOffset = outward * DanmakusuBalance.BYAKUREN_POINT_RADIUS,
                        AimOutward = spins,
                        AimOffset = spins ? 0f : MathF.PI,
                        HalfWidth = halfWidth,
                        HitHalfWidth = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        StartHalfWidth = halfWidth,
                        Speed = speed,
                        BeamCount = 1,
                        Directions = new Vector2[1],
                        Rotates = true,
                        Spins = spins,
                        NoShake = true, // (Byakuren's beams do not shake the screen, and make no sound)
                        Silent = true,
                        AngularSpeed = spins ? DanmakusuBalance.SpinnerLaserAngularSpeed(difficulty.OverallDifficulty) : (float)(MathF.PI / (DanmakusuBalance.BYAKUREN_HOMING_BEATS * beatMs)),
                        Lifetime = (reach + halfWidth) / speed,
                    });
                }
            }

            // Byakuren remembers where every fifth circle or slider of her section starts; 32 beats later five homing beams like her own (see HeadAct.Beams) start from round that place. The spots
            // follow each other at least 2 beats apart (0, 2, 4, ...): a spot waits for the one before it. She holds 5 spots at a time, over the whole map: with as many waiting to fire the next
            // one is not remembered, and when one has fired there is room again.
            var byakurenSpots = new List<double>(); // (when the spots remembered so far fire)

            void addRememberedBeams(BossPeriod period, BossPeriod[] allPeriods)
            {
                double beat = Math.Max(period.BeatLength, 1);
                // (Doremy's and Sagume's copies of the circles and sliders count too, when one of them is on the screen with her)
                var objects = beatmap.HitObjects.OfType<OsuHitObject>().Concat(UI.CloneLayer.MakeCopies(beatmap, allPeriods).Select(copy => copy.Copy))
                                     .Where(h => (h is HitCircle || h is Slider) && h.StartTime >= period.ShootStart && h.StartTime < period.End)
                                     .OrderBy(h => h.StartTime).ToList();
                double lastShot = double.NegativeInfinity;

                for (int i = DanmakusuBalance.BYAKUREN_RECORD_EVERY - 1; i < objects.Count; i += DanmakusuBalance.BYAKUREN_RECORD_EVERY)
                {
                    double seen = objects[i].StartTime;

                    if (byakurenSpots.Count(fires => fires > seen) >= DanmakusuBalance.BYAKUREN_MAX_SPOTS)
                        continue;

                    double shot = Math.Max(seen + DanmakusuBalance.BYAKUREN_DELAY_BEATS * beat, lastShot + DanmakusuBalance.BYAKUREN_SHOT_GAP_BEATS * beat);
                    lastShot = shot;
                    byakurenSpots.Add(shot);
                    period.SpotThrows.Add((shot, objects[i].StackedPosition));
                }
            }

            // the spinner's spiral: a stream of small rotating rings of spinning stars from `origin` between startTime and endTime.
            // Used for spinners and for the boss of the breaks (whose origin the field puts at the boss's position when each ring fires).
            void addSpiral(Vector2 origin, double startTime, double endTime, int comboIndex, int index, bool followsBoss, double intervalFactor = 1, BulletShape starShape = BulletShape.SoftStar)
            {
                double beatLength = DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength);
                double interval = DanmakusuBalance.SpiralInterval(beatLength, difficulty.OverallDifficulty) * intervalFactor;

                // alternate the turning direction between spirals; start each one at a different angle.
                float direction = index % 2 == 0 ? 1f : -1f;
                float startAngle = index * golden_angle;

                int shot = 0;
                const float starScale = DanmakusuBalance.SPINNER_STAR_SCALE; // spinner bullets are spinning stars like hitfinish ones (but they do not ricochet)

                for (double t = startTime; t < endTime; t += interval, shot++)
                {
                    int arms = Math.Max(1, (int)Math.Ceiling(spiralArms * (followsBoss ? bossCountFactor : 1f)));
                    var directions = new Vector2[arms];
                    float baseAngle = startAngle + direction * shot * DanmakusuBalance.SPIRAL_STEP;

                    for (int arm = 0; arm < arms; arm++)
                    {
                        float angle = baseAngle + arm * MathF.Tau / arms;
                        directions[arm] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    }

                    var star = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, comboIndex, radius * starScale * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR * starSizeBoost, starShape, followsBoss: followsBoss, straight: followsBoss && bossStraight);
                    star.Layout = RingShape.Circle;
                    star.LayoutPhase = baseAngle;
                    emitters.Add(star);

                    if (!followsBoss)
                        spinnerStars.Add(star);
                }
            }

            // a ring of `count` bullets. Its rotation is 1 degree per 1/8 beat since the start of the song, so every ring
            // (at a different time) points a different way and the pattern slowly turns for the whole song.
            void addRing(double time, Vector2 position, int count, float spawnRadius, int comboIndex, float scale = 1f, BulletShape shape = BulletShape.Circle, bool aimAtPlayer = false, float countScale = 1f, float speedScale = 1f, float angleOffset = 0f, float sizeScale = 1f, RingShape ringShape = RingShape.Circle, bool ricochet = false)
            {
                count = Math.Max(1, (int)Math.Round(count * countScale));

                float phase = (float)((eighthsAt(time) * DanmakusuBalance.RING_ANGLE_PER_EIGHTH + angleOffset) % MathF.Tau);

                if (landing)
                {
                    addLandingHalves(time, position, count, spawnRadius, speedAt(time) * speedScale, comboIndex, radius * scale * sizeScale, shape, ricochet);
                    return;
                }

                var directions = RingLayout.Create(ringShape, count, phase, out var radialScales);
                float minScale = 1f;

                if (radialScales != null)
                {
                    foreach (float value in radialScales)
                        minScale = Math.Min(minScale, value);
                }

                float ringSpeed = speedAt(time) * speedScale;

                var ring = createEmitter(time, position, spawnRadius, ringSpeed, directions, comboIndex, radius * scale * sizeScale, shape, aimAtPlayer, radialScales, minScale, ricochet);
                ring.Layout = ringShape;
                ring.LayoutPhase = phase;
                emitters.Add(ring);
            }

            // osu!catch: what lands shoots its ring in two halves (every second bullet each), each spread evenly over 150 degrees: one from where it landed, up, the other from the top of the playfield straight
            // above it, down (so that the bullets cross the bottom of the screen too). Neither aims at the player.
            void addLandingHalves(double time, Vector2 position, int count, float spawnRadius, float speed, int comboIndex, float bulletRadius, BulletShape shape, bool ricochet)
            {
                float fan = DanmakusuBalance.CATCH_FAN_DEGREES * MathF.PI / 180f;

                for (int half = 0; half < 2; half++)
                {
                    int n = (count - half + 1) / 2;

                    if (n <= 0)
                        continue;

                    float centre = half == 0 ? -MathF.PI / 2f : MathF.PI / 2f;
                    var directions = new Vector2[n];

                    for (int i = 0; i < n; i++)
                    {
                        float angle = n == 1 ? centre : centre - fan / 2f + fan * i / (n - 1);
                        directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    }

                    Vector2 from = new Vector2(position.X, half == 0 ? DanmakusuBalance.CATCH_BOTTOM : DanmakusuBalance.CATCH_TOP);
                    var made = createEmitter(time, from, spawnRadius, speed, directions, comboIndex, bulletRadius, shape, ricochet: ricochet);

                    // (out of the bottom wall of the screen and the top one: the field puts it there when it comes, as the window has them; a little longer to live, as a wall is further out than the playfield)
                    made.WallSide = half == 0 ? 1 : -1;
                    made.Lifetime *= 1.25;
                    emitters.Add(made);
                }
            }

            // One ring per hitsound of the object: whistle, clap and finish (or last of a combo) each fire their own ring, and a plain
            // object (no additions) fires a normal one. Each ring uses its own hitsound's volume (0..100% -> 0.1x..1x) for its
            // bullet count and speed. Rings of the same object are turned against each other so they do not stack.
            void addObjectRings(OsuHitObject hitObject, IList<HitSampleInfo> samples, float spawnRadius)
            {
                var kinds = new List<(BulletKind kind, int volume)>();

                if (has(samples, HitSampleInfo.HIT_WHISTLE))
                    kinds.Add((BulletKind.Whistle, volumeOf(samples, HitSampleInfo.HIT_WHISTLE)));

                if (has(samples, HitSampleInfo.HIT_CLAP))
                    kinds.Add((BulletKind.Clap, volumeOf(samples, HitSampleInfo.HIT_CLAP)));

                if (hitObject.LastInCombo || has(samples, HitSampleInfo.HIT_FINISH))
                    kinds.Add((BulletKind.Finish, volumeOf(samples, has(samples, HitSampleInfo.HIT_FINISH) ? HitSampleInfo.HIT_FINISH : null)));

                if (kinds.Count == 0)
                    kinds.Add((BulletKind.Normal, volumeOf(samples, HitSampleInfo.HIT_NORMAL)));

                for (int i = 0; i < kinds.Count; i++)
                {
                    var (kind, volume) = kinds[i];
                    var (scale, shape) = styleOf(kind);

                    // the ring itself: clap = square, whistle = triangle (not on sliders), the rest (finish too) = circle
                    var ringShape = hitObject is Slider ? RingShape.Circle
                        : kind == BulletKind.Clap ? RingShape.Square
                        : kind == BulletKind.Whistle ? RingShape.Triangle
                        : RingShape.Circle;

                    float volumeFactor = DanmakusuBalance.VolumeScale(volume);

                    addRing(hitObject.StartTime, hitObject.StackedPosition, DanmakusuBalance.BulletsPerRing(difficulty.OverallDifficulty, kind, inKiaiAt(hitObject.StartTime)), spawnRadius, hitObject.ComboIndexWithOffsets,
                        scale, shape, kind == BulletKind.Finish, volumeFactor, volumeFactor, i * golden_angle, DanmakusuBalance.VolumeSizeScale(volume), ringShape, kind == BulletKind.Finish);
                }
            }

            foreach (var hitObject in beatmap.HitObjects)
            {
                landing = catchMap && hitObject is HitCircle or Slider;

                switch (hitObject)
                {
                    case HitCircle circle:
                    {
                        // (a taiko big note shoots a ring of arc beams instead: BulletField.Drums)
                        if (ConvertedModes.Of(circle) is { Big: true })
                            break;

                        addObjectRings(circle, circle.Samples, OsuHitObject.OBJECT_RADIUS * circle.Scale);
                        break;
                    }

                    case Slider slider:
                    {
                        addObjectRings(slider, slider.HeadCircle?.Samples ?? slider.Samples, OsuHitObject.OBJECT_RADIUS * slider.Scale);

                        float tickSpawnRadius = OsuHitObject.OBJECT_RADIUS * slider.Scale * DanmakusuBalance.TICK_SPAWN_RADIUS_FACTOR;

                        // ticks and slider arrows (repeats) are nested objects...
                        foreach (var nested in slider.NestedHitObjects)
                        {
                            if (nested is SliderTick or SliderRepeat)
                                addRing(nested.StartTime, ((OsuHitObject)nested).StackedPosition, tickCountAt(nested.StartTime), tickSpawnRadius, slider.ComboIndexWithOffsets, countScale: volumeScale(nested.Samples), speedScale: volumeScale(nested.Samples), sizeScale: volumeSizeScale(nested.Samples));
                        }

                        // ...the slider end is the last node of the path, at the slider's end time. Same ring as a tick.
                        addRing(slider.EndTime, slider.StackedEndPosition, tickCountAt(slider.EndTime), tickSpawnRadius, slider.ComboIndexWithOffsets, countScale: volumeScale(slider.TailCircle?.Samples ?? slider.Samples), speedScale: volumeScale(slider.TailCircle?.Samples ?? slider.Samples), sizeScale: volumeSizeScale(slider.TailCircle?.Samples ?? slider.Samples));

                        // a whistle on the slider's start (its head)
                        var headSamples = slider.HeadCircle?.Samples ?? slider.Samples;

                        if (has(headSamples, HitSampleInfo.HIT_WHISTLE))
                            addLaser(slider, volumeOf(headSamples, HitSampleInfo.HIT_WHISTLE));

                        break;
                    }

                    case Spinner spinner:
                    {
                        addSpiral(spinner.Position, spinner.StartTime, spinner.EndTime, spinner.ComboIndexWithOffsets, spinnerIndex++, false);
                        addRotatingLaser(spinner.Position, spinner.StartTime, spinner.EndTime, spinner.ComboIndexWithOffsets, false);

                        break;
                    }
                }
            }

            landing = false;

            // The boss: another "player", who appears (when the warning sound starts, and so a power-up length before the thing it belongs to starts):
            //  - in a break of at least 4 beats: opposite the real player, firing the spinner pattern (the spiral of stars all the time, the 5 lasers in
            //    bursts with a pause between), the first laser exactly when the break starts;
            //  - in a kiai section of at least 4 beats (the parts that overlap a boss of a break are left to that boss): opposite the player too, firing one
            //    laser beam aimed at the player in the same bursts and the same stars at half the frequency, the first one exactly when the kiai section starts;
            //  - in a spinner outside kiai (where the boss is already there): in the middle of the screen, doing nothing, the spinner attacks anyway.
            // His approach circle (twice as big and twice as long as a hit circle's, but never longer than the warning) warns about him.
            var bossList = new List<BossPeriod>();
            var bossPlanOrigin = new Vector2(-OsuPlayfield.BASE_SIZE.X / 2, -OsuPlayfield.BASE_SIZE.Y / 2); // (placeholder: the field puts the boss's real position here)
            double warningLength = DanmakusuBalance.POWER_UP_LENGTH_MS;
            double approachDuration = Math.Min(DanmakusuBalance.ApproachPreempt(difficulty.ApproachRate) * DanmakusuBalance.BOSS_APPROACH_DURATION_FACTOR, warningLength);

            int kyoukoSpins = 0; // wall sets of Kyouko so far over the whole map: her rings turn the other way with every one
            int chenArrowheads = 0; // Chen's own bullets so far over the whole map: every second one is a basic bullet
            bool planningRevival = false; // (the section being planned is the one where Mokou comes back after the last one)
            bool planningBorrowed = false; // (the section being planned is a piece of Yorihime's, where she repeats another boss)
            int guestSlot = -1; // (Toyohime alone plays another boss in an attack: the piece is hers, of her place in the list of the map's bosses, see the end of the planning)
            int utsuhoRings = 0; // (Utsuho's rings of big balls so far over the whole map: each is turned further)
            int ringoThrees = 0; // (Ringo's threes so far over the whole map: each has a number of its own, see BulletEmitter.SkewerId)
            double utsuhoLastBig = double.NegativeInfinity; // (when her last ring of big balls was shot: the next comes an interval later at the soonest, whatever attack or section it is in)

            // Takes the bullets of `ring` for which `moved` says so out into a ring of their own (of `shape` and `bulletRadius`, ricocheting if `ricochet`, flying straight), which is returned; `rest` is the ring of the others
            // (null if none are left). The two rings are fired as the old one was: from the same place, the same way (a ring that aims keeps every bullet where it was against the aim), keeping the outline's distances.
            BulletEmitter? splitOff(BulletEmitter ring, Func<int, bool> moved, BulletShape shape, float bulletRadius, bool ricochet, out BulletEmitter? rest)
            {
                rest = ring;
                var keptDirections = new List<Vector2>();
                var movedDirections = new List<Vector2>();
                var keptScales = new List<float>();
                var movedScales = new List<float>();

                for (int b = 0; b < ring.Count; b++)
                {
                    bool goes = moved(b);
                    (goes ? movedDirections : keptDirections).Add(ring.Directions[b]);
                    (goes ? movedScales : keptScales).Add(ring.RadialScales?[b] ?? 1f);
                }

                if (movedDirections.Count == 0)
                    return null;

                float offsetOf(Vector2 first) => ring.AimAtPlayer ? ring.AimOffset + MathF.Atan2(first.Y, first.X) - MathF.Atan2(ring.Directions[0].Y, ring.Directions[0].X) : 0f;

                BulletEmitter make(List<Vector2> directions, List<float> scales, BulletShape drawn, float size, bool bounces, bool straight)
                {
                    float[]? radial = ring.RadialScales == null ? null : scales.ToArray();
                    var made = createEmitter(ring.Time, ring.Origin, ring.SpawnRadius, ring.Speed, directions.ToArray(), ring.ComboIndex, size, drawn, aimAtPlayer: ring.AimAtPlayer, radialScales: radial, minRadialScale: radial?.Min() ?? 1f,
                        ricochet: bounces, followsBoss: ring.FollowsBoss, straight: straight, aimOffset: offsetOf(directions[0]));
                    made.OwnShot = ring.OwnShot;
                    made.KeepCount = ring.KeepCount;
                    made.Tint = ring.Tint;
                    made.AltTint = ring.AltTint;
                    made.Bloom = ring.Bloom;
                    made.Sharp = ring.Sharp; // (Utsuho's big ones stay sharp: only the round ones are drawn so)
                    made.HurtsLikeSlider = ring.HurtsLikeSlider;
                    return made;
                }

                rest = keptDirections.Count == 0 ? null : make(keptDirections, keptScales, ring.Shape, ring.VisualRadius, ring.Ricochet, ring.Straight);
                return make(movedDirections, movedScales, shape, bulletRadius, ricochet, true);
            }

            // The boss's rings from `from` on: every second bullet of them (counted on from `counter`, over all those rings in order) is taken out into a ring of its own, of `shape` and `bulletRadius`, flying straight.
            void splitEverySecond(int from, BulletShape shape, float bulletRadius, ref int counter)
            {
                int end = emitters.Count;

                for (int e = from; e < end; e++)
                {
                    var ring = emitters[e];

                    if (!ring.FollowsBoss || ring.Derived)
                        continue;

                    var kept = new List<Vector2>();
                    var moved = new List<Vector2>();

                    // (a ring that aims keeps its first bullet on the player: the moved ring aims by the same rule, with its first bullet's offset from the first one)
                    for (int b = 0; b < ring.Count; b++, counter++)
                        (counter % 2 == 0 ? kept : moved).Add(ring.Directions[b]);

                    if (moved.Count == 0)
                        continue;

                    float movedOffset = ring.AimAtPlayer ? ring.AimOffset + MathF.Atan2(moved[0].Y, moved[0].X) - MathF.Atan2(ring.Directions[0].Y, ring.Directions[0].X) : 0f;
                    var basic = createEmitter(ring.Time, ring.Origin, ring.SpawnRadius, ring.Speed, moved.ToArray(), ring.ComboIndex, bulletRadius, shape, aimAtPlayer: ring.AimAtPlayer, followsBoss: true, straight: true, aimOffset: movedOffset);
                    basic.OwnShot = ring.OwnShot;
                    emitters.Add(basic);

                    if (kept.Count == 0)
                    {
                        emitters[e] = basic;
                        emitters.RemoveAt(emitters.Count - 1);
                        continue;
                    }

                    float keptOffset = ring.AimAtPlayer ? ring.AimOffset + MathF.Atan2(kept[0].Y, kept[0].X) - MathF.Atan2(ring.Directions[0].Y, ring.Directions[0].X) : 0f;
                    var arrows = createEmitter(ring.Time, ring.Origin, ring.SpawnRadius, ring.Speed, kept.ToArray(), ring.ComboIndex, ring.VisualRadius, ring.Shape, aimAtPlayer: ring.AimAtPlayer, followsBoss: true, straight: ring.Straight, aimOffset: keptOffset);
                    arrows.OwnShot = ring.OwnShot;
                    emitters[e] = arrows;
                }
            }
            int kiaiBurstCount = 0; // laser bursts of kiai bosses so far (they are added in the order of the map)

            var bossInfos = (matchedBosses is { Count: > 0 } ? matchedBosses : new[] { BossRoster.Default }).ToList(); // (a list: Toyohime alone adds the boss she plays for a moment, see the end of the planning)

            // The placeholder attack of a boss (every boss has his own real patterns later): which of a few templates it is follows from the boss's index.
            //  0, 1: the spiral of stars that the spinner fires;  2: two spirals turning against each other;  3: a fan of 5 stars aimed at the player;
            //  4: a flower: rings of stars that turn by half a step each time;  5: a wave: rings that swing to and fro.
            void addBossPattern(int template, Vector2 origin, double startTime, double endTime, int index, double intervalFactor, BulletShape starShape = BulletShape.SoftStar)
            {
                double beatLength = DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength);
                double interval = DanmakusuBalance.SpiralInterval(beatLength, difficulty.OverallDifficulty) * intervalFactor;
                float starRadius = radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR * starSizeBoost;

                switch (template)
                {
                    case 2:
                        addSpiral(origin, startTime, endTime, 0, index, true, intervalFactor * 2, starShape);
                        addSpiral(origin, startTime, endTime, 0, index + 1, true, intervalFactor * 2, starShape);
                        break;

                    case 3:
                        for (double t = startTime; t < endTime; t += interval * 2)
                        {
                            // (the middle star first: the field turns the fan so that its first star points at the player)
                            float[] offsets = { 0f, -0.28f, 0.28f, -0.56f, 0.56f };
                            var fan = new Vector2[Math.Clamp((int)Math.Ceiling(offsets.Length * bossCountFactor), 1, offsets.Length)];

                            for (int i = 0; i < fan.Length; i++)
                                fan[i] = new Vector2(MathF.Cos(offsets[i]), MathF.Sin(offsets[i]));

                            emitters.Add(createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), fan, 0, starRadius, starShape, aimAtPlayer: true, followsBoss: true, straight: bossStraight));
                        }

                        break;

                    case 4:
                    case 5:
                    {
                        int shot = 0;
                        int petals = Math.Max(2, (int)Math.Ceiling(14 * bossCountFactor));

                        for (double t = startTime; t < endTime; t += interval * 3, shot++)
                        {
                            float baseAngle = template == 4 ? shot * (MathF.PI / petals) : 0.7f * MathF.Sin(shot * 0.4f) + index * golden_angle;
                            var ring = new Vector2[petals];

                            for (int i = 0; i < petals; i++)
                            {
                                float angle = baseAngle + i * MathF.Tau / petals;
                                ring[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                            }

                            var petalRing = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), ring, 0, starRadius, starShape, followsBoss: true, straight: bossStraight);
                            petalRing.Layout = RingShape.Circle;
                            petalRing.LayoutPhase = baseAngle;
                            emitters.Add(petalRing);
                        }

                        break;
                    }

                    default:
                        addSpiral(origin, startTime, endTime, 0, index, true, intervalFactor, starShape);
                        break;
                }
            }

            // Mystia's attack: rings of the ordinary circle bullet (as many as a plain hit circle fires, half of that in kiai), drawn as arrowheads that fly straight
            // like circle bullets, one ring every beat.
            // Seija's attack (BossMechanic.ScreenMirror) is the same, but with the plain round bullets themselves (the base bullet size) instead of arrowheads.
            void addBlindnessRings(Vector2 origin, double startTime, double endTime, bool kiai, bool plainBullets = false)
            {
                // (in a break the arrowheads are as big as the stars that they replace, in kiai as big as arrowheads are)
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                int count = DanmakusuBalance.BulletsPerRing(difficulty.OverallDifficulty, BulletKind.Normal, kiai);
                float arrowRadius = kiai ? radius * DanmakusuBalance.WHISTLE_BULLET_SCALE : radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR;

                if (plainBullets)
                    arrowRadius = radius;

                for (double t = startTime; t < endTime; t += beatLength)
                {
                    float phase = (float)((eighthsAt(t) * DanmakusuBalance.RING_ANGLE_PER_EIGHTH) % MathF.Tau);
                    var directions = RingLayout.Create(RingShape.Circle, count, phase, out _);

                    emitters.Add(createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, arrowRadius, plainBullets ? BulletShape.Circle : BulletShape.Triangle, followsBoss: true, straight: true));
                }
            }

            // Rumia's own shots (in a break and a kiai; in a spinner it is the spinner's stars, as plain bullets): a ring of plain bullets every beat, turned 30 degrees further every second (counted from the start of the map, so that it goes on from one section to the next)
            void addRumiaRings(Vector2 origin, double startTime, double endTime)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double step = beatLength * DanmakusuBalance.RUMIA_RING_EVERY_BEATS;

                for (double t = startTime; t < endTime; t += step)
                {
                    float phase = (float)(t / 1000.0 * DanmakusuBalance.RUMIA_SPIN_DEGREES_PER_SECOND * Math.PI / 180.0 % Math.Tau);
                    var directions = RingLayout.Create(RingShape.Circle, DanmakusuBalance.RUMIA_RING_BULLETS, phase, out _);

                    emitters.Add(createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, radius, BulletShape.Circle, followsBoss: true, straight: true));
                }
            }

            // Hong Meiling's own shots (in a break and a kiai; in a spinner it is the spinner's stars, as plain bullets): a fan of 10 plain bullets spread over 90 degrees, aimed at the player, one every beat
            void addMeilingFan(Vector2 origin, double startTime, double endTime)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double step = beatLength * DanmakusuBalance.MEILING_FAN_EVERY_BEATS;
                int count = DanmakusuBalance.MEILING_FAN_BULLETS;
                float spread = DanmakusuBalance.MEILING_FAN_SPREAD_DEGREES * MathF.PI / 180f;
                float between = count > 1 ? spread / (count - 1) : 0f;

                for (double t = startTime; t < endTime; t += step)
                {
                    // (the field turns the fan so that its first bullet points at the player, and then back by the offset: the fan is centred on the player)
                    var fan = new Vector2[count];

                    for (int i = 0; i < count; i++)
                        fan[i] = new Vector2(MathF.Cos(i * between), MathF.Sin(i * between));

                    emitters.Add(createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), fan, 0, radius, BulletShape.Circle, aimAtPlayer: true, followsBoss: true, straight: true, aimOffset: -spread / 2f));
                }
            }

            // Patchouli's own shots (squares), Sunny Milk's (triangles) and Kaku's (talismans in ellipses): rings of plain bullets in that shape, one every few beats, aimed at nothing (in a spinner it is the spinner's stars, as plain bullets in the same shape)
            void addShapeRings(Vector2 origin, double startTime, double endTime, RingShape shape, int count, double everyBeats, BulletShape bulletShape = BulletShape.Circle)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double step = beatLength * everyBeats;

                for (double t = startTime; t < endTime; t += step)
                {
                    var directions = RingLayout.Create(shape, count, 0f, out var scales);
                    float minScale = 1f;

                    if (scales != null)
                    {
                        foreach (float value in scales)
                            minScale = Math.Min(minScale, value);
                    }

                    var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, radius, bulletShape, followsBoss: true, straight: true, radialScales: scales, minRadialScale: minScale);
                    ring.Layout = shape;
                    ring.LayoutPhase = 0f;
                    ring.OwnShot = true;
                    emitters.Add(ring);
                }
            }

            // Kaguya: every two beats a ring of twenty bullets followed by three more, half a beat apart, each turned a little further than the one before, so that they spin (that they turn as they fly is seen to at
            // the end of Plan, with everything else that is fired in her sections)
            void addKaguyaRings(Vector2 origin, double startTime, double endTime)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double every = beatLength * DanmakusuBalance.KAGUYA_BURST_EVERY_BEATS;
                int fired = 0;

                for (double burst = startTime; burst < endTime; burst += every)
                {
                    for (int k = 0; k < DanmakusuBalance.KAGUYA_RINGS; k++)
                    {
                        double t = burst + k * beatLength * DanmakusuBalance.KAGUYA_RING_GAP_BEATS;

                        // (a ring that would come with the first of the next burst is that one: the same ring twice over)
                        if (t >= endTime || (k > 0 && t >= burst + every - 1))
                            break;

                        float phase = fired++ * DanmakusuBalance.KAGUYA_RING_TURN_DEGREES * MathF.PI / 180f;
                        var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), RingLayout.Create(RingShape.Circle, DanmakusuBalance.KAGUYA_RING_BULLETS, phase, out _), 0, radius, BulletShape.Circle, followsBoss: true, straight: true);
                        ring.Layout = RingShape.Circle;
                        ring.LayoutPhase = phase;
                        ring.OwnShot = true;
                        emitters.Add(ring);
                    }
                }
            }

            // Lily White's flower: a bullet drawn (and hit) as a flower, twice the size of the ones that fall from Yuyuko's top, pink, flashing; it is gone after eight beats and bursts into five petals then
            // (the petals are another ring made out of this one, see DeriveKind.Explode)
            void makeLilyFlower(BulletEmitter flower)
            {
                double beat = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(flower.Time).BeatLength), 1);
                double life = beat * DanmakusuBalance.LILY_FLOWER_BEATS;

                flower.DrawShape = BulletShape.Flower;
                flower.Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_PINK;
                flower.FlowerScale = DanmakusuBalance.LILY_FLOWER_SIZE;
                flower.Flashes = true;
                flower.RemoveAfter = life;
                flower.OwnShot = true;

                float petalRadius = DanmakusuBalance.LILY_PETAL_RADIUS; // (as small as the petals of Yuyuko's tree)
                float petalSpeed = flower.Speed * DanmakusuBalance.LILY_PETAL_SPEED;
                var proto = createEmitter(flower.Time + life, Vector2.Zero, 0f, petalSpeed, new Vector2[flower.Count], flower.ComboIndex, petalRadius, BulletShape.Petal, straight: true);

                for (int petal = 0; petal < DanmakusuBalance.LILY_BURST_PETALS; petal++)
                {
                    emitters.Add(new BulletEmitter
                    {
                        Time = flower.Time + life,
                        SpawnRadius = 0f,
                        Speed = petalSpeed,
                        ComboIndex = flower.ComboIndex,
                        VisualRadius = proto.VisualRadius,
                        HitRadius = proto.HitRadius,
                        Shape = BulletShape.Petal,
                        Straight = true,
                        Directions = new Vector2[flower.Count],
                        Origins = new Vector2[flower.Count],
                        Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_PINK,
                        Derived = true,
                        Derive = DeriveKind.Explode,
                        Source = flower,
                        FanOffset = petal * MathF.Tau / DanmakusuBalance.LILY_BURST_PETALS,
                        Lifetime = proto.Lifetime,
                    });
                }
            }

            // Lily White's own shots (in a break and a kiai; in a spinner it is the spinner's stars, as the same flowers): a ring of 12 flowers, every second ring turned half a step
            void addLilyRings(Vector2 origin, double startTime, double endTime)
            {
                double beatLength = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength), 1);
                double step = beatLength * DanmakusuBalance.LILY_RING_EVERY_BEATS;
                int shot = 0;

                for (double t = startTime; t < endTime; t += step, shot++)
                {
                    float phase = shot % 2 * MathF.PI / DanmakusuBalance.LILY_RING_BULLETS;
                    var directions = RingLayout.Create(RingShape.Circle, DanmakusuBalance.LILY_RING_BULLETS, phase, out _);
                    var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR, BulletShape.Circle, followsBoss: true, straight: true);
                    ring.Layout = RingShape.Circle;
                    ring.LayoutPhase = phase;
                    makeLilyFlower(ring);
                    emitters.Add(ring);
                }
            }

            // Kyouko's own shots (in a break and a kiai; in a spinner it is the spinner's stars, as talismans): talismans in rings of two, many times as often as Yukari's. A new set of walls comes at the start of every attack (`attackStarts`),
            // and every time her rings start turning the other way.
            void addKyoukoRings(Vector2 origin, double startTime, double endTime, BossKind kind, List<double> attackStarts)
            {
                double beatLength = DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength);
                double interval = DanmakusuBalance.SpiralInterval(beatLength, difficulty.OverallDifficulty) * (kind == BossKind.Kiai ? DanmakusuBalance.KIAI_SPIRAL_INTERVAL_FACTOR : 1) / DanmakusuBalance.YUKARI_OWN_FACTOR / DanmakusuBalance.KYOUKO_FREQUENCY_FACTOR;
                float starRadius = radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR;
                int next = 0; // (the next attack that has not started yet)
                float turning = 0f;
                float angle = kyoukoSpins * golden_angle;

                for (double t = startTime; t < endTime; t += interval)
                {
                    while (next < attackStarts.Count && attackStarts[next] <= t + 1e-6)
                    {
                        turning = kyoukoSpins % 2 == 0 ? 1f : -1f;
                        kyoukoSpins++;
                        next++;
                    }

                    if (turning == 0f)
                        turning = 1f;

                    angle += turning * DanmakusuBalance.KYOUKO_SPIN_STEP;

                    var directions = new Vector2[DanmakusuBalance.KYOUKO_RING_BULLETS];

                    for (int arm = 0; arm < directions.Length; arm++)
                    {
                        float armAngle = angle + arm * MathF.Tau / directions.Length;
                        directions[arm] = new Vector2(MathF.Cos(armAngle), MathF.Sin(armAngle));
                    }

                    var ring = createEmitter(t, origin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), directions, 0, starRadius, BulletShape.Talisman, followsBoss: true);
                    ring.Layout = RingShape.Circle;
                    ring.LayoutPhase = angle;
                    emitters.Add(ring);
                }
            }

            void addBoss(double attackStart, double end, BossKind kind, int slot, BossInfo boss, bool isLast)
            {
                bool moon = boss.Mechanic == BossMechanic.Moon;
                bool ringBoss = boss.Mechanic == BossMechanic.Blindness || boss.Mechanic == BossMechanic.ScreenMirror; // (their attack is a ring every beat)
                bool shootsLasers = !ringBoss && !moon && !dropsPlaceholderLasers(boss.Mechanic); // (Clownpiece's moon laser is the field's: it is not a laser burst)
                BulletShape bossShape = boss.Mechanic switch
                {
                    BossMechanic.Freeze => BulletShape.Triangle, // Cirno shoots arrowheads that pull towards the player like arrowheads do
                    BossMechanic.FateSpear => BulletShape.Knife, // Remilia throws knives
                    BossMechanic.Destruction => BulletShape.Knife, // Flandre too
                    BossMechanic.Winter => BulletShape.Triangle, // Letty shoots arrowheads (they fly straight, see below)
                    BossMechanic.Volley => BulletShape.Triangle, // Seiran too (straight)
                    BossMechanic.Dowsing => BulletShape.Triangle, // Nazrin too (straight, the size of the stars)
                    BossMechanic.FreeFall => BulletShape.Ellipse, // Toyohime shoots ellipses (which fall)
                    BossMechanic.TimeStop => BulletShape.Knife, // Sakuya too (in a spinner the stars of the spinner become knives, see below)
                    BossMechanic.Echo => BulletShape.Talisman, // Kyouko shoots like Yukari, but paper talismans
                    BossMechanic.Necromancy => BulletShape.Talisman, // Kaku shoots talismans too (in ellipses)
                    BossMechanic.Doors => BulletShape.Talisman, // Okina shoots nothing but talismans
                    BossMechanic.Return => BulletShape.Talisman, // Yorihime too (in pentagon rings)
                    BossMechanic.FantasySeal => BulletShape.Talisman, // Reimu shoots nothing but talismans (in a spinner the stars of the spinner become talismans, see below)
                    BossMechanic.Shikigami => BulletShape.Triangle, // Chen's arrowheads pull towards the player too
                    BossMechanic.Unconscious => BulletShape.Heart, // Koishi's hearts
                    BossMechanic.Recollection => BulletShape.Heart, // Satori shoots the same
                    BossMechanic.Dango => BulletShape.Circle, // Ringo's round bullets
                    BossMechanic.ButterflyFans => BulletShape.Petal, // Yuyuko shoots petals instead of stars
                    BossMechanic.Autumn => BulletShape.Maple, // Shizuha's autumn leaves (orange, see planMechanics)
                    BossMechanic.Fortune => BulletShape.Circle, // Joon's are round (golden, and they fall: see planMechanics)
                    BossMechanic.Shockwave => BulletShape.Circle, // Yuugi's are basic bullets, twice as big (see planMechanics)
                    BossMechanic.Earthquake => BulletShape.Star, // Tenshi's are ricochet stars (see planMechanics)
                    BossMechanic.Judgement => BulletShape.Talisman, // Eiki's talismans, in very dense rings
                    BossMechanic.Misfortune => BulletShape.Talisman, // Shion's talismans, in hexagons
                    BossMechanic.Foxes => BulletShape.Talisman, // Ran's talismans, which slow down as they fly
                    BossMechanic.Stacking => BulletShape.Ellipse, // Eika's ellipses, in rings
                    BossMechanic.Nuclear => BulletShape.Circle, // Utsuho shoots basic bullets (see planMechanics)
                    BossMechanic.Death => BulletShape.Circle, // Zanmu too (in a spinner the stars of the spinner become basic bullets, see below)
                    // Junko: her sliders' beams are her attack; where there are none she throws slices of beam instead of stars
                    BossMechanic.Purification => beatmap.HitObjects.OfType<Slider>().Any(s => s.StartTime >= attackStart && s.StartTime < end) ? BulletShape.Circle : BulletShape.Slice,

                    _ => BulletShape.SoftStar,
                };
                double bossBeat = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(attackStart).BeatLength), 1);

                // every laser burst of the boss (the first one is when the warning ends); each gets its own warning, with stars flying in
                var laserStarts = new List<double> { attackStart };
                var laserEnds = new List<double> { end }; // (the first burst ends where the spinner / the only burst ends, corrected below)
                double fullBurst = 0, fullPause = 0; // (a spinner is one attack: it has no sequence to finish)

                if (moon)
                {
                    // Clownpiece never pauses: her stars fly the whole section (also in a spinner), and the one attack window is the whole section
                    addBossPattern(boss.Template, bossPlanOrigin, attackStart, end, spinnerIndex++, kind == BossKind.Break ? 1 : DanmakusuBalance.KIAI_SPIRAL_INTERVAL_FACTOR);
                }
                else if (kind == BossKind.Spinner && boss.Mechanic == BossMechanic.Scrolls)
                {
                    // Byakuren's spinner: instead of the spinner's beams, her five beams from round her, pointing straight out and spinning (not homing)
                    addByakurenBeams(attackStart, end, bossBeat, true);
                }
                else if (kind == BossKind.Spinner && boss.Mechanic == BossMechanic.ImpossibleRequests)
                {
                    // Kaguya's spinner: her own rings in place of the spinner's stars
                    addKaguyaRings(bossPlanOrigin, attackStart, end);
                }
                else if (kind != BossKind.Spinner) // (the spinner does the shooting)
                {
                    // the break boss fires the spinner's spiral of stars, the kiai boss the same at half the frequency
                    if (ringBoss)
                        addBlindnessRings(bossPlanOrigin, attackStart, end, kind == BossKind.Kiai, boss.Mechanic == BossMechanic.ScreenMirror);
                    else if (boss.Mechanic == BossMechanic.Darkness)
                        addRumiaRings(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.Qi)
                        addMeilingFan(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.SevenElements)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Square, DanmakusuBalance.PATCHOULI_SQUARE_BULLETS, DanmakusuBalance.PATCHOULI_SQUARE_EVERY_BEATS);
                    else if (boss.Mechanic == BossMechanic.Momentum)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.URUMI_RING_BULLETS, DanmakusuBalance.URUMI_RING_EVERY_BEATS, BulletShape.Ellipse);
                    else if (boss.Mechanic == BossMechanic.Spring)
                        addLilyRings(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.Prism)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Triangle, DanmakusuBalance.SUNNY_TRIANGLE_BULLETS, DanmakusuBalance.SUNNY_TRIANGLE_EVERY_BEATS);
                    else if (boss.Mechanic == BossMechanic.Necromancy)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Ellipse, DanmakusuBalance.KAKU_ELLIPSE_BULLETS, DanmakusuBalance.KAKU_ELLIPSE_EVERY_BEATS, BulletShape.Talisman);
                    else if (boss.Mechanic == BossMechanic.Dream)
                        addDoremyChains(bossPlanOrigin, attackStart, end, kind == BossKind.Kiai ? DanmakusuBalance.KIAI_SPIRAL_INTERVAL_FACTOR : 1);
                    else if (boss.Mechanic == BossMechanic.Haste)
                        addDoubleRings(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.Stacking)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.EIKA_RING_BULLETS, DanmakusuBalance.EIKA_RING_EVERY_BEATS, BulletShape.Ellipse);
                    else if (boss.Mechanic == BossMechanic.Foxes)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.RAN_TALISMANS, DanmakusuBalance.RAN_TALISMAN_EVERY_BEATS, BulletShape.Talisman);
                    else if (boss.Mechanic == BossMechanic.Misfortune)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Hexagon, DanmakusuBalance.SHION_RING_BULLETS, DanmakusuBalance.SHION_RING_EVERY_BEATS, BulletShape.Talisman);
                    else if (boss.Mechanic == BossMechanic.Judgement)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.EIKI_RING_BULLETS, DanmakusuBalance.EIKI_RING_EVERY_BEATS, BulletShape.Talisman);
                    else if (boss.Mechanic == BossMechanic.Recording)
                        addDoubleRings(bossPlanOrigin, attackStart, end, DanmakusuBalance.HISAMI_RING_LAYERS, DanmakusuBalance.HISAMI_RING_EVERY_BEATS, DanmakusuBalance.HISAMI_RING_BULLETS);
                    else if (boss.Mechanic == BossMechanic.Crescent)
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.MUGETSU_RING_BULLETS, DanmakusuBalance.MUGETSU_RING_EVERY_BEATS, BulletShape.Triangle);
                    else if (boss.Mechanic == BossMechanic.Miracle)
                        addSanaeRings(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.ImpossibleRequests)
                        addKaguyaRings(bossPlanOrigin, attackStart, end);
                    else if (boss.Mechanic == BossMechanic.Blades)
                    {
                        // Nemuno: rings of Remilia's knives with blades twice as long
                        int knivesFrom = emitters.Count;
                        addShapeRings(bossPlanOrigin, attackStart, end, RingShape.Circle, DanmakusuBalance.NEMUNO_RING_BULLETS, DanmakusuBalance.NEMUNO_RING_EVERY_BEATS, BulletShape.Knife);

                        for (int k = knivesFrom; k < emitters.Count; k++)
                        {
                            emitters[k].KnifeStretch = DanmakusuBalance.NEMUNO_BLADE_STRETCH;
                            emitters[k].PlainKnife = true;
                        }
                    }
                    else if (boss.Mechanic != BossMechanic.Rebirth && boss.Mechanic != BossMechanic.Silence && boss.Mechanic != BossMechanic.Stampede && boss.Mechanic != BossMechanic.Market && boss.Mechanic != BossMechanic.Possibility && boss.Mechanic != BossMechanic.Storm && boss.Mechanic != BossMechanic.Slash && boss.Mechanic != BossMechanic.Rays && boss.Mechanic != BossMechanic.Cloud && boss.Mechanic != BossMechanic.Anchors && !(boss.Mechanic == BossMechanic.FantasySeal && kind == BossKind.Kiai)) // (Mokou shoots nothing of her own: what she throws is the field's, see MokouAct; nor does Reimu in a kiai section: only her orb; nor Murasa: her anchors are all, see AnchorAct)
                    {
                        // Koishi's hearts: twice as big, shot as often as the stars (KOISHI_HEART_INTERVAL_FACTOR), twice as many in a ring (KOISHI_HEART_COUNT_FACTOR, below)
                        bool hearts = boss.Mechanic == BossMechanic.Unconscious || boss.Mechanic == BossMechanic.Recollection;
                        starSizeBoost = hearts ? DanmakusuBalance.KOISHI_HEART_SIZE_FACTOR : boss.Mechanic == BossMechanic.TimeStop ? DanmakusuBalance.SAKUYA_KNIFE_SIZE_FACTOR : boss.Mechanic == BossMechanic.Volley ? DanmakusuBalance.SEIRAN_SIZE_FACTOR : boss.Mechanic == BossMechanic.Tide ? DanmakusuBalance.WAKASAGI_STAR_SIZE : 1f;
                        bossStraight = boss.Mechanic == BossMechanic.Freeze || boss.Mechanic == BossMechanic.Winter || boss.Mechanic == BossMechanic.Volley || boss.Mechanic == BossMechanic.Dowsing;
                        double sparse = hearts ? DanmakusuBalance.KOISHI_HEART_INTERVAL_FACTOR : boss.Mechanic == BossMechanic.Tide ? DanmakusuBalance.WAKASAGI_STAR_INTERVAL : boss.Mechanic == BossMechanic.Fortune ? DanmakusuBalance.JOON_OWN_INTERVAL : 1; // (Wakasagihime's stars are small and come twice as often)

                        // Yukari's own bullets: half as many, half as often (Kyouko's are her own, see addKyoukoRings)
                        if (boss.Mechanic == BossMechanic.Gaps)
                        {
                            bossCountFactor = DanmakusuBalance.YUKARI_OWN_FACTOR;
                            sparse /= DanmakusuBalance.YUKARI_OWN_FACTOR;
                        }

                        // Hecatia's Sun shoots twice as often
                        if (boss.Mechanic == BossMechanic.ThreeWorlds)
                        {
                            sparse *= DanmakusuBalance.HECATIA_SUN_INTERVAL_FACTOR;
                            bossCountFactor = DanmakusuBalance.HECATIA_SUN_COUNT_FACTOR; // (half as many bullets in every ring)
                        }

                        // Elly: a lot of bats all round her, twice as many in every ring (they are bats: see the bat windows)
                        if (boss.Mechanic == BossMechanic.Reaping)
                            bossCountFactor = DanmakusuBalance.ELLY_BAT_COUNT_FACTOR;

                        // Koishi and Satori: twice as many hearts in every ring
                        if (hearts)
                            bossCountFactor = DanmakusuBalance.KOISHI_HEART_COUNT_FACTOR;

                        int patternFrom = emitters.Count;

                        if (boss.Mechanic == BossMechanic.Echo || boss.Mechanic == BossMechanic.ButterflyFans)
                            spinnerIndex++; // (Kyouko's rings are made below, when the attacks are known; Yuyuko shoots nothing continuously, only her flower rings, see planMechanics)
                        else if (kind == BossKind.Break)
                            addBossPattern(boss.Template, bossPlanOrigin, attackStart, end, spinnerIndex++, sparse, bossShape);
                        else if (kind == BossKind.Kiai)
                            addBossPattern(boss.Template, bossPlanOrigin, attackStart, end, spinnerIndex++, DanmakusuBalance.KIAI_SPIRAL_INTERVAL_FACTOR * sparse, bossShape);

                        // Chen: every second of her arrowheads (counted over all her shots) is a basic bullet that flies straight
                        if (boss.Mechanic == BossMechanic.Shikigami)
                            splitEverySecond(patternFrom, BulletShape.Circle, radius, ref chenArrowheads);

                        starSizeBoost = 1f;
                        bossStraight = false;
                        bossCountFactor = 1f;
                    }

                    double burst = bossBeat * DanmakusuBalance.BOSS_LASER_BURST_BEATS * (boss.Mechanic == BossMechanic.ThreeWorlds ? DanmakusuBalance.HECATIA_PHASE_FACTOR : 1.0); // (Hecatia's attacks last twice as long)

                    if (boss.Mechanic == BossMechanic.Lunatic)
                        burst = bossBeat * DanmakusuBalance.REISEN_ATTACK_BEATS; // (Reisen's attacks and pauses are four beats each)

                    // (Utsuho's attacks and pauses, the black hole's, last twice as long; Gengetsu's attacks too, and four times as long again: she has no pauses)
                    double phaseFactor = boss.Mechanic == BossMechanic.Nuclear ? DanmakusuBalance.UTSUHO_PHASE_FACTOR : boss.Mechanic == BossMechanic.Haste ? DanmakusuBalance.GENGETSU_PHASE_FACTOR : 1.0;
                    burst *= phaseFactor * (boss.Mechanic == BossMechanic.Haste ? DanmakusuBalance.GENGETSU_ATTACK_FACTOR : 1.0);

                    double pause = phaseFactor * (no_pauses.Contains(boss.Mechanic) ? 0 : boss.Mechanic == BossMechanic.Lunatic ? bossBeat * DanmakusuBalance.REISEN_PAUSE_BEATS : bossBeat * DanmakusuBalance.BOSS_LASER_PAUSE_BEATS); // (see no_pauses)
                    bool first = true;
                    laserEnds.Clear();

                    // (a boss who never pauses has one long attack in place of an attack and a pause: the same time, whoever it is)
                    if (pause <= 0)
                        burst = Math.Max(burst, bossBeat * (DanmakusuBalance.BOSS_LASER_BURST_BEATS + DanmakusuBalance.BOSS_LASER_PAUSE_BEATS));

                    // (what a whole attack and pause are, before a short section squeezes them: a boss finishes them after her section, see finishSequences)
                    fullBurst = burst;
                    fullPause = pause;

                    // (in a section too short for a boss's own attack and pause the attack comes first: the pause gives way down to the default one, and only then the attack, down to the default one; shorter than that the attack is whole and the pause has what is left)
                    double room = end - attackStart;

                    if (pause > 0 && room < burst + pause)
                    {
                        double leastPause = Math.Min(pause, bossBeat * DanmakusuBalance.BOSS_LASER_PAUSE_BEATS);

                        pause = Math.Max(room - burst, Math.Min(leastPause, Math.Max(room - Math.Min(burst, bossBeat * DanmakusuBalance.BOSS_LASER_BURST_BEATS), 0)));
                        burst = room - pause;
                    }

                    // (two bosses attack at the same time: the second one's attacks are no longer half an attack phase after the first one's)
                    double turnOffset = 0;
                    laserStarts[0] = attackStart + turnOffset;
                    bool more = true;

                    for (double t = attackStart + turnOffset; more && t < end; t += burst + pause)
                    {
                        // (attacks are whole. When there is no room for a whole one after this one, what is left goes to this one: the section ends in a whole pause, or in this attack when a whole pause does not fit)
                        double rest = end - t;
                        more = rest - burst - pause >= burst;
                        double length = more ? burst : rest - pause >= burst ? rest - pause : rest;

                        if (!first)
                            laserStarts.Add(t);

                        first = false;
                        // (Nue's UFOs pull, and Reisen's and Sakuya's freezes slide, for the whole attack: the end of the section does not cut it short)
                        bool uncut = boss.Mechanic is BossMechanic.Unknown or BossMechanic.Lunatic or BossMechanic.TimeStop;
                        laserEnds.Add(uncut ? t + Math.Max(length, burst) : Math.Min(t + length, end));

                        if (!shootsLasers && boss.Mechanic != BossMechanic.Lunatic)
                            continue; // (Byakuren's beams in her attacks are the field's: see HeadAct) // (Reisen's short beams are the exception; the attack windows are still there: they are what the boss's mechanic works with)

                        // the kiai boss fires 1 beam, then 2, 3, 4, 5, then 1 again and so on - counted over the whole map, not per kiai section; the break boss always 5
                        int burstBeams = kind == BossKind.Kiai ? kiaiBurstCount++ % DanmakusuBalance.SPINNER_LASER_BEAMS + 1 : DanmakusuBalance.SPINNER_LASER_BEAMS;

                        if (halveCounts && boss.Mechanic != BossMechanic.MasterSpark) // (Marisa keeps all her beams with another boss on the screen, as Utsuho her big bullets)
                            burstBeams = thirdCounts ? Math.Max(1, (burstBeams + 2) / 3) : Math.Max(1, (burstBeams + 1) / 2);

                        if (boss.Mechanic == BossMechanic.Lunatic)
                        {
                            // Reisen: rings of very short straight beams, one after the other for as long as the burst lasts (halved with another boss on the screen, as everyone's)
                            double gap = Math.Max(bossBeat * DanmakusuBalance.REISEN_BEAM_GAP_BEATS, DanmakusuBalance.REISEN_BEAM_MS * 2);
                            int ringBeams = !halveCounts ? DanmakusuBalance.REISEN_BEAMS : thirdCounts ? (DanmakusuBalance.REISEN_BEAMS + 2) / 3 : (DanmakusuBalance.REISEN_BEAMS + 1) / 2;

                            for (double shot = t; shot < Math.Min(t + length, end); shot += gap)
                                addRotatingLaser(bossPlanOrigin, shot, shot + DanmakusuBalance.REISEN_BEAM_MS, 0, true, ringBeams, true);
                        }
                        else
                            addRotatingLaser(bossPlanOrigin, t, Math.Min(t + length, end), 0, true, burstBeams, spark: boss.Mechanic == BossMechanic.MasterSpark);
                    }
                }

                if (boss.Mechanic == BossMechanic.Echo)
                {
                    if (kind == BossKind.Spinner)
                        kyoukoSpins += laserStarts.Count; // (the stars of a spinner are the spinner's, but its walls count: the next rings turn the other way)
                    else
                        addKyoukoRings(bossPlanOrigin, attackStart, end, kind, laserStarts);
                }

                bossList.Add(new BossPeriod
                {
                    Start = attackStart - warningLength,
                    ShootStart = attackStart,
                    End = end,
                    Kind = kind,
                    BossSlot = guestSlot >= 0 ? guestSlot : slot,
                    Boss = boss,
                    IsLast = isLast,
                    Rank = currentRank,
                    HasLasers = shootsLasers,
                    ApproachDuration = approachDuration,
                    Preempt = DanmakusuBalance.ApproachPreempt(difficulty.ApproachRate),
                    BeatLength = bossBeat,
                    StarRadius = radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR * (moon ? DanmakusuBalance.CLOWNPIECE_SIZE_FACTOR : 1f),
                    BulletSpeed = speedAt(attackStart),
                    AngularSpeed = DanmakusuBalance.SpinnerLaserAngularSpeed(difficulty.OverallDifficulty),
                    ArrowGravity = ellipseGravity,
                    LaserStarts = laserStarts.ToArray(),
                    LaserEnds = laserEnds.ToArray(),
                    FullBurst = fullBurst,
                    FullPause = fullPause,
                    Revival = planningRevival,
                    Borrowed = planningBorrowed,
                });
            }

            // A boss whose section ends before her last attack and the pause after it are over goes through the whole of them: the attack is whole, the pause after it too, and she stays where she stood
            // until it is over (the next boss comes on time all the same). Not a boss who never pauses (she would never stop), not the last boss (she explodes), not where her own next section carries on.
            void finishSequences()
            {
                foreach (var period in bossList)
                {
                    int count = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);

                    if (count == 0 || period.FullPause <= 0 || period.IsLast || period.Revival || period.Borrowed || no_pauses.Contains(period.Boss.Mechanic))
                        continue;

                    double attackEnd = Math.Max(period.LaserEnds[count - 1], period.LaserStarts[count - 1] + period.FullBurst);
                    double sequenceEnd = attackEnd + period.FullPause;

                    if (sequenceEnd <= period.End + 1 && attackEnd <= period.LaserEnds[count - 1] + 1)
                        continue; // (it is whole already)

                    if (bossList.Any(other => other != period && other.Rank == period.Rank && other.Boss == period.Boss && !other.Borrowed && other.ShootStart >= period.End - 1 && other.Start < sequenceEnd))
                        continue;

                    period.LaserEnds[count - 1] = attackEnd;
                    period.SequenceEnd = sequenceEnd;
                    period.MechanicsEnd = Math.Max(period.MechanicsEnd, sequenceEnd);
                }
            }

            // Remilia: in her attacks every ring gets more bullets (1x -> 2x) and turns faster (1x -> 2x) as the attack goes on.
            // Yukari: every bullet gets one more beside it on either side in her attacks; a train comes when an attack begins and in every pause.
            // Cirno: everything that exists when her attack starts is frozen, and bursts into five small bullets each when it ends (the rings of those are made here, the field fills them in).
            // Sakuya: spirals of knives hang round her during an attack and are thrown out at the player, mirrored, when it ends.
            void planMechanics()
            {
                // (first the ones that change rings, then the ones that make rings out of the rings that are there by then)
                for (int pass = 0; pass < 2; pass++)
                foreach (var period in bossList.OrderBy(p => p.Start).ToList()) // (in the order they come in the map: what is counted over the whole map - Utsuho's last ring of big balls, Marisa's stars... - goes forward in time)
                {
                    currentRank = period.Rank; // (what a boss's mechanic makes comes from that boss)

                    // the bosses that have no beams (in their sections): the beams of another boss on the screen are like any other beam to them, and go too (the planning of a section only saw the ones that were there then)
                    if (pass == 0 && bossList.Any(other => other.Rank >= 1))
                    {
                        var mechanic = period.Boss.Mechanic;
                        bool every = mechanic == BossMechanic.ButterflyFans; // (Yuyuko: no beams of her own or of a spinner; the beams of the map's sliders stay)
                        bool keepsOwnOrigin = mechanic is BossMechanic.Dowsing or BossMechanic.Death or BossMechanic.Lunatic || period.Boss.Id is "minoriko_aki" or "shizuha_aki"; // (Reisen's short beams stay)
                        bool noBigBeams = every || keepsOwnOrigin || mechanic is BossMechanic.Blindness or BossMechanic.ScreenMirror or BossMechanic.Necromancy or BossMechanic.Prism or BossMechanic.SevenElements or BossMechanic.Bats; // (Kaku, Sunny Milk, Patchouli and Koakuma: no spinner beams of anyone; the beams of sliders stay)

                        if (noBigBeams)
                        {
                            laserList.RemoveAll(laser =>
                            {
                                if (laser.FollowsBoss && laser.BossRank == period.Rank)
                                    return false;

                                if (every && laser.FollowsBoss)
                                    return false; // (Yuyuko has no beams, but the other boss's own beams are hers to keep: Junko's)

                                if (laser.StartTime < period.Start - 1 || laser.StartTime > period.End)
                                    return false;

                                // (Marisa's beams stay)
                                if (bossList.Any(spark => spark.Boss.Mechanic == BossMechanic.MasterSpark && laser.StartTime >= spark.Start - 1 && laser.StartTime <= spark.End))
                                    return false;

                                return laser.SpinnerLike && !(keepsOwnOrigin && laser.KeepsOrigin);
                            });
                        }
                    }

                    switch (period.Boss.Mechanic)
                    {
                        // Marisa: in her attacks every third bullet of every ring (anyone's; counted on over all of them in the order they are fired) is a star that ricochets, as big as a finish's
                        case BossMechanic.MasterSpark when pass == 1:
                        {
                            long counted = 0;
                            float starRadius = radius * DanmakusuBalance.FINISH_BULLET_SCALE;

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                var inside = Enumerable.Range(0, emitters.Count).Where(i => emitters[i].Time >= start && emitters[i].Time < end && !emitters[i].Derived && emitters[i].Derive == DeriveKind.None && emitters[i].Origins == null && emitters[i].Count > 0)
                                                       .OrderBy(i => emitters[i].Time).ToList();
                                var removed = new List<int>();

                                foreach (int i in inside)
                                {
                                    var ring = emitters[i];

                                    if (ring.Shape == BulletShape.Star && ring.Ricochet)
                                    {
                                        counted += ring.Count; // (they are ricochet stars already)
                                        continue;
                                    }

                                    long first = counted;
                                    counted += ring.Count;
                                    var stars = splitOff(ring, b => (first + b) % DanmakusuBalance.MARISA_STAR_EVERY == DanmakusuBalance.MARISA_STAR_EVERY - 1, BulletShape.Star, starRadius, true, out var rest);

                                    if (stars == null)
                                        continue;

                                    emitters.Add(stars);

                                    if (rest != null)
                                        emitters[i] = rest;
                                    else
                                        removed.Add(i);
                                }

                                foreach (int i in removed.OrderByDescending(i => i))
                                    emitters.RemoveAt(i);
                            }

                            break;
                        }

                        // Gengetsu: in her attacks every ring (anyone's) is shot twice, right after each other (every bullet on the screen speeds up from 1x to 2x meanwhile: see BossEffects); every fourth bullet
                        // (counted on over all of them) is 1.2 times as big; and every fourth beam that starts in her section is twice as thick
                        case BossMechanic.Haste when pass == 0:
                        {
                            double beat = Math.Max(period.BeatLength, 1);
                            long counted = 0;
                            var again = new List<BulletEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                foreach (var ring in emitters.Where(r => r.Time >= start && r.Time < end && !r.Derived && r.Derive == DeriveKind.None && r.Count > 0).OrderBy(r => r.Time).ToList())
                                {
                                    // (the speed-up of everything in her attacks is not here: it is a window of BossEffects, a clock of the bullets that runs faster and faster)
                                    if (ring.BigBase < 0)
                                    {
                                        ring.BigBase = (int)(counted % DanmakusuBalance.GENGETSU_BIG_EVERY);
                                        ring.BigEvery = DanmakusuBalance.GENGETSU_BIG_EVERY;
                                        ring.BigFactor = DanmakusuBalance.GENGETSU_BIG_SIZE;
                                    }

                                    counted += ring.Count;

                                    // (the second shot: the same ring a moment later, also speeding up and with its own big bullets)
                                    var twice = ring.Delayed(beat * DanmakusuBalance.GENGETSU_AGAIN_BEATS);
                                    twice.BigBase = (int)(counted % DanmakusuBalance.GENGETSU_BIG_EVERY);
                                    counted += twice.Count;
                                    again.Add(twice);
                                }
                            }

                            emitters.AddRange(again);

                            int beam = 0;

                            foreach (var laser in laserList.Where(l => l.StartTime >= period.ShootStart && l.StartTime < period.End).OrderBy(l => l.StartTime))
                            {
                                if (++beam % DanmakusuBalance.GENGETSU_THICK_EVERY != 0 || laser.KeepsSize)
                                    continue;

                                laser.HalfWidth *= DanmakusuBalance.GENGETSU_THICK;
                                laser.StartHalfWidth *= DanmakusuBalance.GENGETSU_THICK;
                                laser.HitHalfWidth *= DanmakusuBalance.GENGETSU_THICK;
                            }

                            break;
                        }

                        // Mugetsu: in her attacks every fourth ring (anyone's, that knows its outline) is three times as dense (that every bullet goes round her is seen to at the end of Plan, after everything else has been done to the rings)
                        case BossMechanic.Crescent when pass == 0:
                        {
                            int seen = 0;

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                var inside = Enumerable.Range(0, emitters.Count).Where(i => emitters[i].Time >= start && emitters[i].Time < end && !emitters[i].Derived && emitters[i].Derive == DeriveKind.None && emitters[i].Layout != null
                                                                                               && emitters[i].Origins == null && !emitters[i].Ricochet && !emitters[i].Falling && !emitters[i].Homing && emitters[i].Count > 0 && !emitters[i].KeepsSize)
                                                       .OrderBy(i => emitters[i].Time).ToList();

                                foreach (int i in inside)
                                {
                                    if (++seen % DanmakusuBalance.MUGETSU_EVERY != 0)
                                        continue;

                                    emitters[i] = emitters[i].Rebuilt(Math.Min(emitters[i].Count * DanmakusuBalance.MUGETSU_DENSITY, DanmakusuBalance.MAX_RING_BULLETS), 0f);
                                }
                            }

                            break;
                        }

                        // Sanae: in her attacks every ring (anyone's, but not her own) has twice as many bullets, half as big, and a round one is a five-pointed star
                        case BossMechanic.Miracle when pass == 0:
                        {
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (ring.Time < start || ring.Time >= end || ring.Derived || ring.Derive != DeriveKind.None || ring.Count == 0 || (ring.OwnShot && ring.Layout == RingShape.Star && ring.Ricochet) || ring.KeepsSize)
                                        continue; // (Utsuho's big balls and the trains are left as they are)

                                    var made = ring.Layout != null
                                        ? ring.RebuiltAs(ring.Layout == RingShape.Circle ? RingShape.Star : ring.Layout.Value, Math.Min(ring.Count * DanmakusuBalance.SANAE_COUNT_FACTOR, DanmakusuBalance.MAX_RING_BULLETS), 0f)
                                        : ring;

                                    emitters[i] = made.Scaled(DanmakusuBalance.SANAE_SIZE_FACTOR);
                                }
                            }

                            break;
                        }

                        // Nemuno: in her attacks the rings of every fifth circle or slider (counted over her section) are fired where the player is and fall the way from the object to the player,
                        // eight times more gently than ellipses; and every tenth slider beam of her section is hers when its slider ends (see NemunoAct)
                        case BossMechanic.Blades when pass == 0:
                        {
                            var attacksOfHers = BossOps.Attacks(period);
                            var objects = beatmap.HitObjects.OfType<OsuHitObject>().Where(h => (h is HitCircle || h is Slider) && attacksOfHers.Any(a => h.StartTime >= a.Start && h.StartTime < a.End)).OrderBy(h => h.StartTime).ToList();

                            for (int k = DanmakusuBalance.NEMUNO_OBJECT_EVERY - 1; k < objects.Count; k += DanmakusuBalance.NEMUNO_OBJECT_EVERY)
                            {
                                var hitObject = objects[k];

                                foreach (var ring in emitters)
                                {
                                    if (ring.FollowsBoss || ring.Derived || ring.Derive != DeriveKind.None || ring.Origins != null || Math.Abs(ring.Time - hitObject.StartTime) > 0.5 || Vector2.Distance(ring.Origin, hitObject.StackedPosition) > 1f)
                                        continue;

                                    float gravity = ellipseGravity / DanmakusuBalance.NEMUNO_FALL_GENTLER;
                                    float reachAlongPull = (OsuPlayfield.BASE_SIZE + new Vector2(m * 2, m * 2)).Length * 2f;

                                    ring.FromPlayer = true;
                                    ring.SpawnRadius *= DanmakusuBalance.NEMUNO_SPAWN_WIDER; // (the ring starts twice as far round the player)
                                    ring.FadeInMs = DanmakusuBalance.NEMUNO_FADE_IN_MS; // (and fades in)
                                    ring.FallsToPlayer = true;
                                    ring.Ricochet = false;
                                    ring.Gravity = gravity;
                                    ring.Lifetime = Math.Max(ring.Lifetime, (ring.Speed + Math.Sqrt(ring.Speed * ring.Speed + 2 * gravity * reachAlongPull)) / gravity);
                                }
                            }

                            int beams = 0;

                            foreach (var laser in laserList.Where(l => !l.Rotates && l.StartTime >= period.ShootStart && l.StartTime < period.End).OrderBy(l => l.StartTime)) // (a long note's beams have no path to take: they stay)
                            {
                                if (++beams * DanmakusuBalance.NEMUNO_BEAMS_OF_EVERY % DanmakusuBalance.NEMUNO_BEAM_EVERY >= DanmakusuBalance.NEMUNO_BEAMS_OF_EVERY)
                                    continue; // (4 of every 10: the 3rd, 5th, 8th and 10th)

                                laser.VanishAt = laser.EndTime;
                                period.TakenBeams.Add(laser);
                            }

                            break;
                        }

                        case BossMechanic.Scrolls when pass == 0:
                            addRememberedBeams(period, bossList.ToArray());
                            break;

                        // Doremy: in her attacks (in a spinner, the whole spinner) every ring of the map's circles, sliders and spinner gets a copy that is fired from the mirror image of where
                        // it is fired (through the middle of the playfield) the opposite way: three quarters as big, twice as fast. The field shows the mirrored circles and sliders themselves.
                        // Sagume: every attack is played again backwards as soon as it ends: a ring fired t ms before the end is fired again t ms after it, from the same place the same way,
                        // white, one and a half times as big and half as fast (the field shows the circles and sliders again in that order, see CloneLayer)
                        case BossMechanic.Rewind when pass == 1:
                        {
                            var copies = new List<BulletEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                foreach (var ring in emitters)
                                {
                                    if (ring.Time < start || ring.Time >= end || ring.FollowsBoss || ring.Derived || ring.Derive != DeriveKind.None || ring.Origins != null || ring.Count == 0)
                                        continue;

                                    copies.Add(ring.Replayed(2 * end - ring.Time, DanmakusuBalance.SAGUME_COPY_SIZE, DanmakusuBalance.SAGUME_COPY_SPEED, UI.BulletRenderer.TINT_WHITE).Halved().Halved()); // (a quarter as many bullets)
                                }
                            }

                            emitters.AddRange(copies);
                            break;
                        }

                        case BossMechanic.Dream when pass == 1:
                        {
                            var copies = new List<BulletEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                foreach (var ring in emitters)
                                {
                                    if (ring.Time < start || ring.Time >= end || ring.FollowsBoss || ring.Derived || ring.Derive != DeriveKind.None || ring.Origins != null || ring.Count == 0)
                                        continue;

                                    // (half as many bullets; white and blue in turn, glowing)
                                    var copy = ring.Mirrored(OsuPlayfield.BASE_SIZE, DanmakusuBalance.DOREMY_COPY_SIZE, DanmakusuBalance.DOREMY_COPY_SPEED, DanmakusuBalance.DOREMY_COPY_SHADE).Halved();
                                    copy.Tint = UI.BulletRenderer.TINT_WHITE;
                                    copy.AltTint = UI.BulletRenderer.TINT_BLUE;
                                    copy.Bloom = DanmakusuBalance.DOREMY_COPY_BLOOM;
                                    copies.Add(copy);
                                }
                            }

                            emitters.AddRange(copies);
                            break;
                        }

                        // Junko: every 8 beats of her section she counts every fourth bullet that appeared in them (anyone's, but not her own rings of this kind; counted on over the windows) and shoots them all out again
                        // as one ring from where she is, at the average speed of what she counted
                        case BossMechanic.Purification when pass == 1:
                        {
                            double window = Math.Max(period.BeatLength, 1) * DanmakusuBalance.JUNKO_RECORD_BEATS;
                            var reshots = new List<BulletEmitter>();
                            long seen = 0;

                            for (double from = period.ShootStart; from + window <= period.End + 1; from += window)
                            {
                                double to = from + window;
                                long seenBefore = seen;
                                double speedSum = 0;
                                long all = 0;

                                foreach (var ring in emitters)
                                {
                                    if (ring.Time < from || ring.Time >= to || ring.Count == 0)
                                        continue;

                                    seen += ring.Count;
                                    all += ring.Count;
                                    speedSum += (double)ring.Speed * ring.Count;
                                }

                                // (the bullets number seenBefore + 1 .. seen of all she has counted: every JUNKO_RECORD_EVERY-th of them is recorded)
                                int every = DanmakusuBalance.JUNKO_RECORD_EVERY;
                                int count = (int)(seen / every - seenBefore / every);

                                if (count == 0 || all == 0)
                                    continue;

                                speedSum *= (double)count / all;

                                float speed = (float)(speedSum / count);
                                var directions = RingLayout.Create(RingShape.Circle, count, (float)(reshots.Count * golden_angle % MathF.Tau), out _);

                                // (a ring holds at most 64 bullets: a bigger one is several rings, each a run of the directions)
                                for (int first = 0; first < count; first += DanmakusuBalance.MAX_RING_BULLETS)
                                {
                                    var part = directions.Skip(first).Take(DanmakusuBalance.MAX_RING_BULLETS).ToArray();
                                    var reshot = createEmitter(to, bossPlanOrigin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speed, part, 0, radius, BulletShape.Circle, followsBoss: true, straight: true);
                                    reshot.OwnShot = true;
                                    reshot.Reshot = true; // (Wakasagihime does not make it swim)
                                    reshots.Add(reshot);
                                }
                            }

                            emitters.AddRange(reshots);
                            break;
                        }

                        case BossMechanic.FateSpear when pass == 0:
                        case BossMechanic.Stampede when pass == 0: // (Saki's attacks are like Remilia's)
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                if (end <= start)
                                    continue;

                                double eighthsSpan = eighthsAt(end) - eighthsAt(start);

                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (ring.Time < start || ring.Time >= end || ring.Derived || ring.Layout == null)
                                        continue;

                                    double u = (ring.Time - start) / (end - start);
                                    int count = Math.Clamp((int)Math.Round(ring.Count * (1 + (DanmakusuBalance.REMILIA_MAX_FACTOR - 1) * u)), 1, 64);
                                    float extra = (float)(DanmakusuBalance.RING_ANGLE_PER_EIGHTH * (DanmakusuBalance.REMILIA_MAX_FACTOR - 1) * eighthsSpan * u * u / 2);
                                    emitters[i] = ring.Rebuilt(count, extra);
                                }

                                // (and her knives get more blades the later in the attack they are thrown: double-edged, three-edged, then a cross; the ones already flying keep what they have)
                                float spinWay = 1f;
                                int thrown = 0;

                                foreach (var knifeRing in emitters.OrderBy(knifeOrder => knifeOrder.Time).ToList())
                                {
                                    if (knifeRing.Time < start || knifeRing.Time >= end || knifeRing.Derived)
                                        continue;

                                    if (knifeRing.PlainKnife)
                                        continue;

                                    if ((knifeRing.DrawShape ?? knifeRing.Shape) != BulletShape.Knife)
                                    {
                                        // (every 40th bullet that is not a knife is three times as big)
                                        knifeRing.BigBase = thrown;
                                        thrown += knifeRing.Count;
                                        continue;
                                    }

                                    float knifeProgress = (float)((knifeRing.Time - start) / (end - start));

                                    knifeRing.Blades = knifeProgress >= DanmakusuBalance.REMILIA_CROSS_AT ? 4
                                        : knifeProgress >= DanmakusuBalance.REMILIA_THREE_EDGED_AT ? 3
                                        : knifeProgress >= DanmakusuBalance.REMILIA_DOUBLE_EDGED_AT ? 2
                                        : 1;

                                    if (knifeRing.Blades > 1)
                                    {
                                        knifeRing.BladeSpin = spinWay * DanmakusuBalance.KNIFE_SPIN_RATE; // (every next ring turns the other way round)
                                        spinWay = -spinWay;
                                    }
                                }

                                // (the beams of her attack grow the same way: each is as many times as thick as the rings are numerous at the moment it is fired)
                                foreach (var remiliaBeam in laserList)
                                {
                                    if (!remiliaBeam.FollowsBoss || !remiliaBeam.Grows || remiliaBeam.KeepsSize || remiliaBeam.BossRank != period.Rank || remiliaBeam.StartTime < start || remiliaBeam.StartTime >= end)
                                        continue;

                                    float remiliaScale = 1f + (DanmakusuBalance.REMILIA_MAX_FACTOR - 1f) * (float)((remiliaBeam.StartTime - start) / (end - start));
                                    remiliaBeam.Lifetime += remiliaBeam.HalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR * remiliaBeam.PeakScale * (remiliaScale - 1f) / Math.Max(remiliaBeam.Speed, 1e-6f);
                                    remiliaBeam.PeakScale *= remiliaScale;
                                    remiliaBeam.HitHalfWidth *= remiliaScale;
                                }
                            }

                            break;

                        case BossMechanic.FreeFall when pass == 0:
                        {
                            // every bullet that is fired during a pause ricochets once more (once, if it did not at all); the ones that fall or are pulled by themselves keep what they do
                            foreach (var (start, end) in BossOps.PausesWithTail(period))
                            {
                                foreach (var ring in emitters)
                                {
                                    if (ring.Time < start || ring.Time >= end || ring.Derived || ring.Derive != DeriveKind.None || ring.Falling || ring.Homing)
                                        continue;

                                    bool wasBouncing = ring.Ricochet;

                                    if (!wasBouncing)
                                    {
                                        ring.Ricochet = true;
                                        ring.Area = ricochetArea;
                                    }
                                    else
                                        ring.ExtraRicochets = 1;

                                    ring.Lifetime = Math.Max(ring.Lifetime, 6000f / (ring.Speed * ring.MinRadialScale) * (wasBouncing ? 2 : 1));
                                }
                            }

                            break;
                        }

                        case BossMechanic.TimeStop when pass == 0:
                        {
                            // her own knives leave from the edge of her magic circle
                            for (int i = 0; i < emitters.Count; i++)
                            {
                                var own = emitters[i];

                                if (!own.FollowsBoss || own.BossRank != period.Rank || own.Derived || own.Derive != DeriveKind.None || own.Time < period.ShootStart || own.Time >= period.End)
                                    continue;

                                own.SpawnRadius = DanmakusuBalance.MAGIC_CIRCLE_RADIUS;
                            }

                            break;
                        }

                        case BossMechanic.Dango when pass == 0:
                        {
                            // her own bullets: round, twice as many, half the size
                            for (int i = 0; i < emitters.Count; i++)
                            {
                                var own = emitters[i];

                                if (!own.FollowsBoss || own.BossRank != period.Rank || own.Derived || own.Derive != DeriveKind.None || own.Time < period.ShootStart || own.Time >= period.End)
                                    continue;

                                var doubled = own.Layout != null ? own.Rebuilt(Math.Min(64, own.Count * 2), 0f) : own;
                                emitters[i] = doubled.Scaled(DanmakusuBalance.RINGO_OWN_SIZE);
                            }

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                // every bullet that is fired during an attack has two more of its kind right behind it
                                var inside = emitters.Where(ring => ring.Time >= start && ring.Time < end && !ring.Derived && ring.Derive == DeriveKind.None && !ring.Deflector).OrderBy(ring => ring.Time).ToList();
                                var behind = new List<BulletEmitter>();

                                foreach (var ring in inside)
                                {
                                    double gap = ring.VisualRadius * DanmakusuBalance.RINGO_BEHIND_SPACING / Math.Max(ring.Speed, 1e-4f);
                                    var second = ring.Delayed(gap);
                                    var third = ring.Delayed(gap * 2);

                                    behind.Add(second);
                                    behind.Add(third);

                                    // (every fourth three is joined by a beam: RingoAct. The rings are marked, not remembered: with another boss on the screen every ring is halved later, a new ring in its place)
                                    int three = ++ringoThrees;
                                    ring.SkewerId = second.SkewerId = third.SkewerId = three;
                                    ring.SkewerPart = 0;
                                    second.SkewerPart = 1;
                                    third.SkewerPart = 2;
                                    period.Skewers.Add(three);
                                }


                                emitters.AddRange(behind);
                            }

                            // from time to time (in the pauses too) she shoots big round bullets in a half circle at the player; they turn the bullets they touch
                            double beat = Math.Max(period.BeatLength, 1);
                            float[] fan = { 0f, -1f, 1f, -2f, 2f, -3f, 3f };

                            for (double t = period.ShootStart; t < period.End; t += beat * DanmakusuBalance.RINGO_VOLLEY_BEATS)
                            {
                                var directions = new Vector2[fan.Length];

                                for (int i = 0; i < fan.Length; i++)
                                {
                                    float angle = fan[i] * (MathF.PI / 6f); // (the half circle in 30 degree steps; the field turns the middle one to the player)
                                    directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                                }

                                var volley = createEmitter(t, bossPlanOrigin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t) * DanmakusuBalance.RINGO_VOLLEY_SPEED_FACTOR, directions, 0, radius * DanmakusuBalance.RINGO_VOLLEY_SIZE, BulletShape.Circle, aimAtPlayer: true, followsBoss: true);
                                volley.Deflector = true;
                                emitters.Add(volley);
                            }

                            break;
                        }

                        case BossMechanic.Nuclear when pass == 0:
                        {
                            // Utsuho's black hole: what the circles and sliders fire while it is there does not come out of them, it comes in from outside the screen, from just beyond its edge the way it would have
                            // flown out from the middle, straight at the hole (which takes it). Not what is pulled or bounces (those find their own ways), nor a train
                            foreach (var (start, end) in BossOps.PausesWithTail(period))
                            {
                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (ring.FollowsBoss || ring.Derived || ring.Derive != DeriveKind.None || ring.Origins != null || ring.Homing || ring.Ricochet || ring.Sharp || ring.Shape == BulletShape.Train
                                        || ring.Time < start || ring.Time >= end || ring.Count == 0)
                                        continue;

                                    emitters[i] = ring.FromOutside(new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT) / 2f,
                                        new Vector2(DanmakusuBalance.UTSUHO_INWARD_HALF_WIDTH, DanmakusuBalance.UTSUHO_INWARD_HALF_HEIGHT));
                                }
                            }

                            // Utsuho: her own bullets are the basic bullet (not stars); every few beats of an attack she also throws a ring of six very big ones
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                if (end <= start)
                                    continue;

                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var own = emitters[i];

                                    if (!own.FollowsBoss || own.BossRank != period.Rank || own.Derived || own.Derive != DeriveKind.None || own.Time < start || own.Time >= end)
                                        continue;

                                    emitters[i] = own.Scaled(radius / Math.Max(own.VisualRadius, 1e-3f));
                                }

                                double step = Math.Max(period.BeatLength, 1) * DanmakusuBalance.UTSUHO_RING_EVERY_BEATS;

                                // (never sooner than the interval after the last ring, also when an attack or a section starts again just after it: the series goes on rather than starting over)
                                for (double t = Math.Max(start, utsuhoLastBig + step); t < end; t += step)
                                {
                                    utsuhoLastBig = t;
                                    // (every ring is turned further than the one before, so the balls of one fly between those of the last)
                                    float turn = utsuhoRings++ * DanmakusuBalance.UTSUHO_RING_TURN_DEGREES * MathF.PI / 180f;
                                    var big = RingLayout.Create(RingShape.Circle, DanmakusuBalance.UTSUHO_RING_COUNT, 0f, out _);
                                    var bigRing = createEmitter(t, bossPlanOrigin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t) * DanmakusuBalance.UTSUHO_RING_SPEED, big, 0, radius * DanmakusuBalance.UTSUHO_RING_SCALE, BulletShape.Circle, aimAtPlayer: true, followsBoss: true, straight: true, aimOffset: turn);
                                    bigRing.KeepCount = true; // (six with another boss on the screen too)
                                    bigRing.Sharp = true; // (drawn as sharp as Mokou's ball)
                                    bigRing.HurtsLikeSlider = true; // (and hurts like a slider body, as Mokou's ball does)
                                    emitters.Add(bigRing);
                                }
                            }

                            break;
                        }

                        case BossMechanic.Earthquake when pass == 0:
                            // Tenshi's own bullets are ricochet stars
                            foreach (var own in emitters)
                            {
                                if (IsOkinaRing(own, period, period.ShootStart, period.End))
                                    makeRicochet(own);
                            }

                            break;

                        case BossMechanic.Recording when pass == 0:
                        {
                            // Hisami: every slider's beam fired in her attacks is fired again four beats later, from the same place
                            double later = Math.Max(period.BeatLength, 1) * DanmakusuBalance.HISAMI_BEAM_AGAIN_BEATS;
                            var again = new List<LaserEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                foreach (var beam in laserList)
                                {
                                    if (!beam.SpinnerLike && !beam.FollowsBoss && beam.StartTime >= start && beam.StartTime < end)
                                        again.Add(beam.Later(later));
                                }
                            }

                            laserList.AddRange(again);
                            break;
                        }

                        case BossMechanic.Shockwave when pass == 0:
                            // Yuugi's own bullets are the basic bullet, twice as big
                            for (int i = 0; i < emitters.Count; i++)
                            {
                                if (IsOkinaRing(emitters[i], period, period.ShootStart, period.End))
                                    emitters[i] = emitters[i].Scaled(radius * DanmakusuBalance.YUUGI_SIZE / Math.Max(emitters[i].VisualRadius, 1e-3f));
                            }

                            break;

                        case BossMechanic.Fortune when pass == 0:
                            // Joon's own bullets are golden and fall, at a quarter of Zanmu's pull
                            foreach (var own in emitters)
                            {
                                if (IsOkinaRing(own, period, period.ShootStart, period.End))
                                    makeJoonBullets(own);
                            }

                            break;

                        case BossMechanic.Autumn when pass == 0:
                            // Shizuha's own bullets are autumn leaves: orange petals
                            foreach (var own in emitters)
                            {
                                if (IsOkinaRing(own, period, period.ShootStart, period.End))
                                    own.Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_ORANGE;
                            }

                            break;

                        case BossMechanic.ButterflyFans when pass == 0:
                        {
                            // Yuyuko: between her ordinary rings she also shoots rings of petals whose outline is a sakura flower, at 2x the interval of the ordinary rings of other bosses (the only bullets she shoots of her own)
                            double flowerStep = DanmakusuBalance.SpiralInterval(Math.Max(period.BeatLength, 1), difficulty.OverallDifficulty)
                                                * (period.Kind == BossKind.Kiai ? DanmakusuBalance.KIAI_SPIRAL_INTERVAL_FACTOR : 1) * 3 * DanmakusuBalance.YUYUKO_FLOWER_RING_RARITY;
                            float flowerRadius = radius * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR;
                            int flowerShot = 0;

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                if (end <= start)
                                    continue;

                                for (double t = start; t < end; t += flowerStep, flowerShot++)
                                {
                                    float flowerPhase = flowerShot * golden_angle;
                                    var flowerDirs = RingLayout.Create(RingShape.Flower, DanmakusuBalance.YUYUKO_FLOWER_RING_COUNT, flowerPhase, out var flowerScales);
                                    float flowerMin = 1f;

                                    if (flowerScales != null)
                                    {
                                        foreach (float value in flowerScales)
                                            flowerMin = Math.Min(flowerMin, value);
                                    }

                                    var flowerRing = createEmitter(t, bossPlanOrigin, DanmakusuBalance.SPIRAL_SPAWN_RADIUS, speedAt(t), flowerDirs, 0, flowerRadius, BulletShape.Petal, radialScales: flowerScales, minRadialScale: flowerMin, followsBoss: true);
                                    flowerRing.Layout = RingShape.Flower;
                                    flowerRing.LayoutPhase = flowerPhase;
                                    emitters.Add(flowerRing);
                                }
                            }

                            break;
                        }

                        case BossMechanic.Shikigami when pass == 0:
                            // while Chen attacks every ring of the map (not beams) is shot from her: the rings of the map, and the own rings of another boss on the screen too (they are like any other ring to her; only Utsuho's big balls are always shot from Utsuho)
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (ring.Time >= start && ring.Time < end && !(ring.FollowsBoss && ring.BossRank == period.Rank) && !ring.Sharp && !ring.Derived && ring.Derive == DeriveKind.None)
                                        emitters[i] = ring.ShotFromBoss(period.Rank);
                                }
                            }

                            break;

                        case BossMechanic.Claws when pass == 0:
                            // Kagerou shoots nothing of her own but what she shoots as she flies (the act's)
                            emitters.RemoveAll(own => IsOkinaRing(own, period, period.ShootStart, period.End));
                            break;

                        case BossMechanic.Return when pass == 0:
                            // she has no big beams of her own (in no section, and not in a spinner either)
                            laserList.RemoveAll(beam => beam.FollowsBoss && beam.BossRank == period.Rank && beam.StartTime >= period.ShootStart - 1 && beam.StartTime < period.End);

                            // Yorihime's rings (her own, and the stars of her spinner) are pentagons
                            for (int i = 0; i < emitters.Count; i++)
                            {
                                var own = emitters[i];

                                if (own.Derived || own.Derive != DeriveKind.None || own.Layout == null || own.Time < period.ShootStart || own.Time >= period.End)
                                    continue;

                                if (!(own.FollowsBoss && own.BossRank == period.Rank) && period.Kind != BossKind.Spinner)
                                    continue;

                                own.Layout = RingShape.Pentagon;
                                emitters[i] = own.Rebuilt(Math.Clamp((own.Count + 4) / 5 * 5, 10, 60), 0f);
                            }

                            break;

                        case BossMechanic.Doors when pass == 0:
                        {
                            // Okina: in an attack she shoots backwards (away from the player, at the portal in the wall behind her), half as often; in the pauses she shoots forwards as she did
                            var dropped = new HashSet<BulletEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                var times = new SortedSet<double>();

                                foreach (var ring in emitters)
                                {
                                    if (IsOkinaRing(ring, period, start, end))
                                        times.Add(ring.Time);
                                }

                                var skipped = new HashSet<double>();
                                bool odd = false;

                                foreach (double time in times)
                                {
                                    if (odd)
                                        skipped.Add(time);

                                    odd = !odd;
                                }

                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (!IsOkinaRing(ring, period, start, end))
                                        continue;

                                    if (skipped.Contains(ring.Time))
                                        dropped.Add(ring);
                                    else
                                        emitters[i] = ring.Rotated(MathF.PI);
                                }
                            }

                            emitters.RemoveAll(dropped.Contains);
                            break;
                        }

                        case BossMechanic.Volley when pass == 0:
                        {
                            // Seiran: a quarter of the bullets in every ring of an attack, each shot six times in a row (one bullet; then with one more at each side; then with two more at each side, four times)
                            float volleySide = DanmakusuBalance.SEIRAN_SIDE_ANGLE_DEGREES * MathF.PI / 180f;
                            var volley = new List<BulletEmitter>();

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                for (int i = 0; i < emitters.Count; i++)
                                {
                                    var ring = emitters[i];

                                    if (ring.Time < start || ring.Time >= end || ring.Derived || ring.Derive != DeriveKind.None || ring.Deflector)
                                        continue;

                                    var quarter = ring.Halved().Halved();
                                    emitters[i] = quarter;

                                    double shotGap = quarter.VisualRadius * DanmakusuBalance.SEIRAN_SHOT_SPACING / Math.Max(quarter.Speed, 1e-4f);

                                    for (int shot = 1; shot < DanmakusuBalance.SEIRAN_SHOTS; shot++)
                                    {
                                        var later = quarter.Delayed(shotGap * shot);
                                        volley.Add(later);

                                        for (int step = 1; step <= (shot == 1 ? 1 : 2); step++)
                                        {
                                            volley.Add(later.Rotated(volleySide * step));
                                            volley.Add(later.Rotated(-volleySide * step));
                                        }
                                    }
                                }
                            }

                            emitters.AddRange(volley);
                            break;
                        }

                        case BossMechanic.Gaps when pass == 0:
                        {
                            var sides = new List<BulletEmitter>();
                            float sideAngle = DanmakusuBalance.YUKARI_SIDE_ANGLE_DEGREES * MathF.PI / 180f;

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                foreach (var ring in emitters)
                                {
                                    if (ring.Time < start || ring.Time >= end || ring.Derived || ring.Derive != DeriveKind.None)
                                        continue;

                                    sides.Add(ring.Rotated(sideAngle)); // (every ring of her attacks is two)
                                }
                            }

                            emitters.AddRange(sides);

                            float halfLength = radius * DanmakusuBalance.YUKARI_TRAIN_LENGTH_RADII;

                            void addTrain(double time, float speed) => emitters.Add(new BulletEmitter
                            {
                                Time = time,
                                Speed = speed,
                                VisualRadius = halfLength / DanmakusuBalance.TRAIN_LENGTH_FACTOR,
                                HitRadius = halfLength * 0.9f,
                                Shape = BulletShape.Train,
                                Directions = new Vector2[1],
                                Origins = new Vector2[1],
                                Derive = DeriveKind.Train,
                                Derived = true,
                                Lifetime = OsuPlayfield.BASE_SIZE.Length * 6f / speed,
                            });

                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                if (end > start)
                                    addTrain(start, speedAt(start) * DanmakusuBalance.YUKARI_TRAIN_SPEED_FACTOR);
                            }

                            foreach (var (start, end) in BossOps.PausesWithTail(period))
                            {
                                float speed = speedAt((start + end) / 2) * DanmakusuBalance.YUKARI_TRAIN_SPEED_FACTOR;

                                // the train is timed to be gone (about) when the pause ends, which is when the frozen bullets are let go: from far beyond the far side of
                                // the screen, past the player, to the border again
                                double travel = (OsuPlayfield.BASE_SIZE.Length * 1.5f + halfLength * 4f) / speed;

                                addTrain(Math.Max(start, end - travel), speed);
                            }

                            break;
                        }

                        // Mima: when a moon of hers is gone, stars with short tails fly off all along its edges (the field puts them there, see HalfMoonAct.Edges)
                        case BossMechanic.HalfMoon when pass == 1:
                        {
                            double moonBeat = Math.Max(period.BeatLength, 1);
                            float edgeRadius = radius * DanmakusuBalance.FINISH_BULLET_SCALE * DanmakusuBalance.MIMA_STAR_SIZE;

                            // her own stars (her spinner's too) have tails of beams four beats long, narrowing to a point
                            double ownTail = moonBeat * DanmakusuBalance.MIMA_OWN_TAIL_BEATS;

                            foreach (var ring in emitters)
                            {
                                bool spinnerStar = period.Kind == BossKind.Spinner && !ring.FollowsBoss && !ring.Derived && ring.Derive == DeriveKind.None && ring.Time >= period.ShootStart && ring.Time < period.End;

                                if (ring.TailMs <= 0 && (ring.DrawShape ?? ring.Shape) is BulletShape.SoftStar or BulletShape.Star && (IsOkinaRing(ring, period, period.ShootStart, period.End) || spinnerStar))
                                {
                                    ring.TailMs = ownTail;
                                    ring.TailEvery = 1;
                                    ring.Lifetime += ownTail;
                                }
                            }

                            foreach (double mark in BossOps.MoonMarks(period))
                            {
                                double gone = mark + moonBeat * (DanmakusuBalance.MIMA_GROW_BEATS + DanmakusuBalance.MIMA_SOLID_BEATS);
                                float edgeSpeed = speedAt(gone);
                                double tail = moonBeat * DanmakusuBalance.MIMA_TAIL_BEATS;

                                // (a ring for each edge: a ring holds at most 64)
                                for (int piece = 0; piece < 2; piece++)
                                {
                                    emitters.Add(new BulletEmitter
                                    {
                                        Time = gone,
                                        Speed = edgeSpeed,
                                        VisualRadius = edgeRadius,
                                        HitRadius = edgeRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                        Shape = BulletShape.Star,
                                        Tint = UI.BulletRenderer.TINT_WHITE,
                                        Directions = new Vector2[DanmakusuBalance.MIMA_EDGE_STARS / 2],
                                        Origins = new Vector2[DanmakusuBalance.MIMA_EDGE_STARS / 2],
                                        Derive = DeriveKind.MoonEdges,
                                        Derived = true,
                                        Piece = piece,
                                        TailMs = tail,
                                        TailEvery = 1,
                                        Lifetime = OsuPlayfield.BASE_SIZE.Length * 2f / Math.Max(edgeSpeed, 1e-3f) + tail,
                                    });
                                }
                            }

                            break;
                        }

                        case BossMechanic.Freeze when pass == 1:
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                // (Utsuho's big balls cannot be destroyed: they are frozen too, but do not burst)
                                var parents = emitters.Where(ring => !ring.Derived && ring.Derive == DeriveKind.None && !ring.Sharp && ring.Time < start && ring.Time + Math.Min(ring.Lifetime, 15000) > start).ToList();

                                foreach (var parent in parents)
                                {
                                    for (int fanIndex = 0; fanIndex < DanmakusuBalance.CIRNO_FAN_COUNT; fanIndex++)
                                    {
                                        int fan = fanIndex - DanmakusuBalance.CIRNO_FAN_COUNT / 2; // (centred: -1, 0, 1)
                                        float burstSpeed = parent.Speed * DanmakusuBalance.CIRNO_BURST_SPEED_FACTOR;
                                        var proto = createEmitter(end, Vector2.Zero, 0f, burstSpeed, new Vector2[parent.Count], parent.ComboIndex, parent.VisualRadius * DanmakusuBalance.CIRNO_CHILD_SIZE, parent.Shape, false, parent.RadialScales, parent.MinRadialScale, false, false, parent.Shape == BulletShape.Triangle);

                                        emitters.Add(new BulletEmitter
                                        {
                                            Time = end,
                                            SpawnRadius = 0f,
                                            Speed = burstSpeed,
                                            ComboIndex = parent.ComboIndex,
                                            VisualRadius = proto.VisualRadius,
                                            HitRadius = proto.HitRadius,
                                            Shape = parent.Shape,
                                            Straight = parent.Shape == BulletShape.Triangle,
                                            Gravity = parent.Shape == BulletShape.Ellipse ? parent.Gravity : 0f,
                                            Directions = new Vector2[parent.Count],
                                            Origins = new Vector2[parent.Count],
                                            RadialScales = parent.RadialScales,
                                            MinRadialScale = parent.MinRadialScale,
                                            Derived = true,
                                            Derive = DeriveKind.Explode,
                                            Source = parent,
                                            BurstsEverySecond = true,
                                            FanOffset = fan * DanmakusuBalance.CIRNO_FAN_DEGREES * MathF.PI / 180f,
                                            Lifetime = proto.Lifetime,
                                        });
                                    }
                                }
                            }

                            break;

                        case BossMechanic.TimeStop when pass == 1:
                            foreach (var (start, end) in BossOps.Attacks(period))
                            {
                                if (end <= start)
                                    continue;

                                double beat = Math.Max(period.BeatLength, 1);
                                int arms = DanmakusuBalance.SAKUYA_KNIFE_ARMS;
                                int perArm = Math.Clamp((int)((end - start) / (beat * 0.5) / arms), 3, 64 / arms);
                                var offsets = new Vector2[arms * perArm];
                                var appeared = new double[arms * perArm];
                                int n = 0;

                                for (int j = 0; j < perArm; j++)
                                {
                                    for (int a = 0; a < arms; a++)
                                    {
                                        float r = radius * (DanmakusuBalance.SAKUYA_KNIFE_RADIUS_START + DanmakusuBalance.SAKUYA_KNIFE_RADIUS_STEP * j);
                                        float theta = a * MathF.Tau / arms + j * DanmakusuBalance.SAKUYA_KNIFE_TWIST;
                                        offsets[n] = new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * r;
                                        appeared[n] = start + (end - start) * 0.85 * n / (arms * perArm);
                                        n++;
                                    }
                                }

                                float knifeSpeed = speedAt(end) * DanmakusuBalance.SAKUYA_KNIFE_SPEED_FACTOR;
                                float knifeRadius = radius * DanmakusuBalance.SAKUYA_KNIFE_SIZE_FACTOR;
                                var proto = createEmitter(end, Vector2.Zero, 0f, knifeSpeed, new Vector2[n], 0, knifeRadius, BulletShape.Knife);

                                emitters.Add(new BulletEmitter
                                {
                                    Time = end,
                                    Speed = knifeSpeed,
                                    VisualRadius = knifeRadius,
                                    HitRadius = knifeRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                    Shape = BulletShape.Knife,
                                    Directions = new Vector2[n],
                                    Origins = new Vector2[n],
                                    Derived = true,
                                    BossRank = period.Rank, // (they hang round her, not round the other boss)
                                    Derive = DeriveKind.Held,
                                    HeldOffsets = offsets,
                                    HeldFrom = appeared,
                                    Lifetime = proto.Lifetime * DanmakusuBalance.SAKUYA_RELEASE_DISTANCE_FACTOR, // (they start further out, so they fly longer)
                                });
                            }

                            break;
                    }
                }
            }

            // the bosses take the sections in turn: A, B, A, B... (one boss takes all of them)
            int atOnce = alwaysTriple && bossInfos.Count >= 3 ? 3 : alwaysDouble && bossInfos.Count >= 2 ? 2 : 1; // (how many bosses have every section)
            bool together = atOnce > 1;
            var ordered = cutForBosses(beatmap, Sections(beatmap, bands), (bossInfos.Count + atOnce - 1) / atOnce);
            int lastSection = -1;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (lastSection < 0 || ordered[i].end >= ordered[lastSection].end)
                    lastSection = i;
            }

            // Mokou and Kaguya come back after the last section: a last section that goes on to the end of the map stops in time to leave them room, and a spinner there, that cannot be cut short, is theirs to take over
            if (ordered.Count > 0 && lastSection >= 0 && bossInfos.Any(boss => boss.Mechanic is BossMechanic.Rebirth or BossMechanic.ImpossibleRequests) && beatmap.HitObjects.Count > 0)
            {
                double finish = beatmap.HitObjects.Max(h => h.GetEndTime());
                double stopFor(double end) => finish - Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(end).BeatLength), 1) * (DanmakusuBalance.REVIVAL_DELAY_BEATS + DanmakusuBalance.REVIVAL_ROOM_BEATS);

                while (ordered.Count > 1 && ordered[lastSection].kind == BossKind.Spinner && ordered[lastSection].end > stopFor(ordered[lastSection].end))
                {
                    ordered.RemoveAt(lastSection);
                    lastSection = 0;

                    for (int i = 1; i < ordered.Count; i++)
                    {
                        if (ordered[i].end >= ordered[lastSection].end)
                            lastSection = i;
                    }
                }

                var (roomStart, roomEnd, roomKind, roomRestart) = ordered[lastSection];
                double stopAt = stopFor(roomEnd);

                if (roomEnd > stopAt && roomKind != BossKind.Spinner && holdsABoss(beatmap, roomStart, stopAt))
                    ordered[lastSection] = (roomStart, stopAt, roomKind, roomRestart);
            }

            // with the Double Boss Selection mod every section has two bosses on the screen at once, with the Triple one three (the others are planned like the first, with another boss); all the counts of bullets and beams are halved in it, or divided by three
            var doubleSections = new HashSet<int>();
            int revivalSection = -1; // (Mokou and Kaguya back together are two, whatever the mod)

            if (together)
            {
                for (int i = 0; i < ordered.Count; i++)
                    doubleSections.Add(i);
            }

            // (is there a boss with this mechanic beside this one in the section: those who share it are the next ones in the list, counted from the one of rank 0)
            bool besideHer(int i, int slot, int rank, BossMechanic mechanic)
            {
                if (!doubleSections.Contains(i) || i == revivalSection)
                    return false;

                for (int k = 0; k < atOnce; k++)
                {
                    if (k != rank && bossInfos[(slot + k - rank + bossInfos.Count * 2) % bossInfos.Count].Mechanic == mechanic)
                        return true;
                }

                return false;
            }

            void planSection(int i, int slot, int rank)
            {
                var (sectionStart, sectionEnd, sectionKind, _) = ordered[i];
                currentRank = rank;

                // Marisa is the other boss of this section: her beams (and the beams of the spinner that she keeps) stay, whatever this boss's mechanic does to beams
                bool marisaPartner = besideHer(i, slot, rank, BossMechanic.MasterSpark);

                // the spinner's own beams are only for Marisa: every other boss takes them away (Clownpiece's moon laser takes their place)
                if (bossInfos[slot].Mechanic != BossMechanic.MasterSpark)
                    if (!marisaPartner)
                        laserList.RemoveAll(laser => laser.SpinnerLike && !laser.FollowsBoss && laser.StartTime >= sectionStart - 1 && laser.StartTime <= sectionEnd);

                // Nazrin, Reisen (her short straight beams are made later and stay), Rumia, Flandre, Doremy and both of the Akis have no big beams of their own either: not in their sections, and not in their spinners
                if (no_big_beams.Contains(bossInfos[slot].Mechanic) || bossInfos[slot].Id is "minoriko_aki" or "shizuha_aki" or "doremy_sweet")
                    if (!marisaPartner)
                        laserList.RemoveAll(laser => laser.SpinnerLike && !laser.KeepsOrigin && laser.StartTime >= sectionStart - 1 && laser.StartTime <= sectionEnd);

                // Wakasagihime: no beams of the map's sliders in her section (they were straight bars among the traces of what swims, blue like them); a boss's own beams stay
                if (bossInfos[slot].Mechanic == BossMechanic.Tide)
                    laserList.RemoveAll(laser => !laser.SpinnerLike && !laser.FollowsBoss && laser.StartTime >= sectionStart && laser.StartTime < sectionEnd);

                // Mystia has no laser beams at all: not her own, and not the one of a spinner she is there for (the spinner's stars stay)
                if (bossInfos[slot].Mechanic == BossMechanic.Blindness)
                {
                    if (!marisaPartner)
                        laserList.RemoveAll(laser => laser.SpinnerLike && !laser.FollowsBoss && laser.StartTime >= sectionStart - 1 && laser.StartTime <= sectionEnd);

                    // ...and the stars of that spinner are arrowheads that fly like circle bullets (as all of her bullets)
                    foreach (var star in spinnerStars)
                    {
                        if (star.Time < sectionStart - 1 || star.Time > sectionEnd)
                            continue;

                        int at = emitters.IndexOf(star);

                        if (at >= 0)
                            emitters[at] = createEmitter(star.Time, star.Origin, star.SpawnRadius, star.Speed, star.Directions, star.ComboIndex, star.VisualRadius, BulletShape.Triangle, straight: true); // (they keep the size of the stars)
                    }
                }

                // Seija has no laser beams either, and her bullets (also the spinner's stars of a spinner she is there for) are the plain round ones
                if (bossInfos[slot].Mechanic == BossMechanic.ScreenMirror)
                {
                    if (!marisaPartner)
                        laserList.RemoveAll(laser => laser.SpinnerLike && !laser.FollowsBoss && laser.StartTime >= sectionStart - 1 && laser.StartTime <= sectionEnd);

                    foreach (var star in spinnerStars)
                    {
                        if (star.Time < sectionStart - 1 || star.Time > sectionEnd)
                            continue;

                        int at = emitters.IndexOf(star);

                        if (at >= 0)
                            emitters[at] = createEmitter(star.Time, star.Origin, star.SpawnRadius, star.Speed, star.Directions, star.ComboIndex, radius, BulletShape.Circle);
                    }
                }

                // Reimu shoots nothing but talismans in a spinner: the stars of the spinner are talismans
                // (in a spinner the stars of Reimu become talismans, the stars of Mokou and Junko basic bullets and the stars of Sakuya knives; a short spinner that goes with the section round it too, whatever kind that is)
                var slotMechanic = bossInfos[slot].Mechanic;

                if (own_spinner_bullets.Contains(slotMechanic))
                {
                    bool knifeBoss = knife_spinner_bullets.Contains(slotMechanic); // (Sakuya, Remilia, Flandre and Nemuno: the stars of a spinner are knives)

                    bool heartBoss = heart_spinner_bullets.Contains(slotMechanic); // (Koishi and Satori: the stars of a spinner are hearts too, twice as big, twice as many in a ring, as in a break)
                    int heartCount = 0;
                    int lilyShots = 0;
                    int joonShots = 0;
                    int sanaeShots = 0;
                    double sanaeNext = double.NegativeInfinity;

                    var replacement = heartBoss ? BulletShape.Heart
                        : slotMechanic == BossMechanic.Earthquake ? BulletShape.Star
                        : slotMechanic == BossMechanic.Autumn ? BulletShape.Maple
                        : round_spinner_bullets.Contains(slotMechanic) ? BulletShape.Circle
                        : knifeBoss ? BulletShape.Knife
                        : arrowhead_spinner_bullets.Contains(slotMechanic) ? BulletShape.Triangle
                        : ellipse_spinner_bullets.Contains(slotMechanic) ? BulletShape.Ellipse
                        : BulletShape.Talisman;

                    foreach (var star in spinnerStars)
                    {
                        if (star.Time < sectionStart - 1 || star.Time > sectionEnd)
                            continue;

                        int at = emitters.IndexOf(star);

                        if (at < 0)
                            continue;

                        // (Sanae's spinner shoots her rings of ricochet stars)
                        if (bossInfos[slot].Mechanic == BossMechanic.Miracle)
                        {
                            // (one ring every two beats, as in her attacks: the stars between are gone)
                            if (star.Time >= sanaeNext)
                            {
                                emitters[at] = sanaeRing(star.Time, star.Origin, sectionStart, sanaeShots++, false);
                                sanaeNext = star.Time + Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(star.Time).BeatLength), 1) * DanmakusuBalance.SANAE_RING_EVERY_BEATS;
                            }
                            else
                                emitters.RemoveAt(at);

                            continue;
                        }

                        // (Luna Child and Saki shoot nothing but their arcs, Chimata nothing but her talismans, Yumemi nothing but her flowers: the spinner's stars are gone; of Lily White's only every second one becomes a ring of flowers)
                        if (no_spinner_stars.Contains(slotMechanic) || (heartBoss && heartCount++ % (int)Math.Round(DanmakusuBalance.KOISHI_HEART_INTERVAL_FACTOR) != 0)
                            || (bossInfos[slot].Mechanic == BossMechanic.Spring && lilyShots++ % DanmakusuBalance.LILY_SPINNER_SHOT_EVERY != 0)
                            || (bossInfos[slot].Mechanic == BossMechanic.Fortune && joonShots++ % (int)DanmakusuBalance.JOON_OWN_INTERVAL != 0))
                        {
                            emitters.RemoveAt(at);
                            continue;
                        }

                        // Patchouli's, Sunny Milk's and Kaku's bullets stay in squares, triangles and ellipses in a spinner too
                        RingShape? ownShape = bossInfos[slot].Mechanic == BossMechanic.SevenElements ? RingShape.Square : bossInfos[slot].Mechanic == BossMechanic.Prism ? RingShape.Triangle : bossInfos[slot].Mechanic == BossMechanic.Necromancy ? RingShape.Ellipse : bossInfos[slot].Mechanic == BossMechanic.Misfortune ? RingShape.Hexagon : null;
                        var swappedDirections = star.Directions;
                        float[]? swappedScales = null;
                        float swappedMin = 1f;

                        // (Lily White's flowers come in rings of 6 here (12 elsewhere), Urumi's ellipses in rings of 20)
                        if (bossInfos[slot].Mechanic == BossMechanic.Spring)
                            swappedDirections = RingLayout.Create(RingShape.Circle, DanmakusuBalance.LILY_SPINNER_RING_BULLETS, star.LayoutPhase, out _);
                        else if (bossInfos[slot].Mechanic == BossMechanic.Momentum)
                            swappedDirections = RingLayout.Create(RingShape.Circle, DanmakusuBalance.URUMI_RING_BULLETS, star.LayoutPhase, out _);
                        else if (heartBoss) // (Koishi's and Satori's hearts: twice as many in every ring as the spinner has stars)
                            swappedDirections = RingLayout.Create(RingShape.Circle, Math.Min(DanmakusuBalance.MAX_RING_BULLETS, (int)Math.Round(star.Directions.Length * DanmakusuBalance.KOISHI_HEART_COUNT_FACTOR)), star.LayoutPhase, out _);

                        if (ownShape != null)
                        {
                            swappedDirections = RingLayout.Create(ownShape.Value, Math.Max(star.Directions.Length, 12), star.LayoutPhase, out swappedScales);

                            if (swappedScales != null)
                            {
                                foreach (float value in swappedScales)
                                    swappedMin = Math.Min(swappedMin, value);
                            }
                        }

                        var swapped = createEmitter(star.Time, star.Origin, knifeBoss ? DanmakusuBalance.MAGIC_CIRCLE_RADIUS : star.SpawnRadius, star.Speed, swappedDirections, star.ComboIndex, knifeBoss ? star.VisualRadius * DanmakusuBalance.SAKUYA_KNIFE_SIZE_FACTOR : bossInfos[slot].Mechanic == BossMechanic.Volley ? star.VisualRadius * DanmakusuBalance.SEIRAN_SIZE_FACTOR : heartBoss ? star.VisualRadius * DanmakusuBalance.KOISHI_HEART_SIZE_FACTOR : star.VisualRadius, replacement, straight: replacement == BulletShape.Triangle, radialScales: swappedScales, minRadialScale: swappedMin);
                        swapped.PlainKnife = bossInfos[slot].Mechanic == BossMechanic.FateSpear || bossInfos[slot].Mechanic == BossMechanic.Destruction || bossInfos[slot].Mechanic == BossMechanic.Blades;
                        swapped.KnifeStretch = bossInfos[slot].Mechanic == BossMechanic.Blades ? DanmakusuBalance.NEMUNO_BLADE_STRETCH : 1f; // (Nemuno's blades are twice as long) // (Remilia's and Flandre's spinner knives stay single-bladed)
                        swapped.OwnShot = true;
                        swapped.Layout = ownShape ?? star.Layout;
                        swapped.LayoutPhase = star.LayoutPhase;

                        if (bossInfos[slot].Mechanic == BossMechanic.ButterflyFans)
                        {
                            // Yuyuko's spinner shoots flowers (round to the hitbox, drawn and hit as the flowers of her tree)
                            swapped.DrawShape = BulletShape.Flower;
                            swapped.Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_PINK;
                        }

                        if (bossInfos[slot].Mechanic == BossMechanic.Spring)
                            makeLilyFlower(swapped); // (Lily White's spinner shoots her flowers too, which burst into petals)

                        if (bossInfos[slot].Mechanic == BossMechanic.Autumn)
                            swapped.Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_ORANGE; // (Shizuha's spinner shoots autumn leaves)

                        if (bossInfos[slot].Mechanic == BossMechanic.Fortune)
                            makeJoonBullets(swapped); // (Joon's spinner shoots her golden falling bullets)

                        if (bossInfos[slot].Mechanic == BossMechanic.Earthquake)
                            makeRicochet(swapped); // (Tenshi's spinner shoots her ricochet stars)

                        if (bossInfos[slot].Mechanic == BossMechanic.Shockwave)
                            swapped = swapped.Scaled(radius * DanmakusuBalance.YUUGI_SIZE / Math.Max(swapped.VisualRadius, 1e-3f)); // (Yuugi's spinner shoots her big basic bullets)

                        emitters[at] = swapped;
                    }
                }

                addBoss(sectionStart, sectionEnd, sectionKind, slot, bossInfos[slot], i == lastSection);

                // Junko: ALL the sliders of her section are sliderwhistles, but they do not shoot from where they are: every one is a laser (with the path of the slider, as if the slider were where Junko is) fired from her.
                // Whatever a slider would have shot itself (a whistled one) is replaced.
                // Letty on the screen too: the beams of a boss dissipate into bullets (hers and Junko's)
                bool lettyIsHere = besideHer(i, slot, rank, BossMechanic.Winter);
                bool junkoIsHere = besideHer(i, slot, rank, BossMechanic.Purification);

                if (bossInfos[slot].Mechanic == BossMechanic.Purification)
                {
                    foreach (var slider in beatmap.HitObjects.OfType<Slider>())
                    {
                        if (slider.StartTime < sectionStart || slider.StartTime >= sectionEnd)
                            continue;

                        laserList.RemoveAll(laser => !laser.SpinnerLike && Math.Abs(laser.StartTime - slider.StartTime) < 0.5 && Math.Abs(laser.EndTime - slider.EndTime) < 0.5);
                        addLaser(slider, 100, true, lettyIsHere);
                    }
                }

                // Kaku, Sunny Milk, Patchouli and Koakuma: no beams of their own and none of a spinner; the beams of sliders (with a whistle) stay
                if (bossInfos[slot].Mechanic is BossMechanic.Necromancy or BossMechanic.Prism or BossMechanic.SevenElements or BossMechanic.Bats or BossMechanic.ButterflyFans)
                    if (!marisaPartner)
                        laserList.RemoveAll(laser => laser.SpinnerLike && laser.StartTime >= sectionStart && laser.StartTime < sectionEnd && !(laser.FollowsBoss && laser.BossRank != rank)); // (the beams of the other boss on the screen stay: Junko's)

                // Letty: ALL the sliders of her section are sliderwhistles (they shoot their beams from where they are, as usual); the beam dissipates into bullets, see addLaser
                if (bossInfos[slot].Mechanic == BossMechanic.Winter && !junkoIsHere) // (with Junko the sliders are hers: her beams dissipate, see above)
                {
                    foreach (var slider in beatmap.HitObjects.OfType<Slider>())
                    {
                        if (slider.StartTime < sectionStart || slider.StartTime >= sectionEnd)
                            continue;

                        laserList.RemoveAll(laser => !laser.SpinnerLike && Math.Abs(laser.StartTime - slider.StartTime) < 0.5 && Math.Abs(laser.EndTime - slider.EndTime) < 0.5);
                        addLaser(slider, 100, false, true);
                    }
                }
            
                currentRank = 0;
            }

            // (the No Boss mod: no boss in any section - the sections are not planned at all, so there are no boss periods, attacks or bars)
            int plannedSections = noBoss ? 0 : ordered.Count;

            // (the bosses take the sections in turn, see bossSlots)
            bool isBuildUp(int i) => ordered[i].kind == BossKind.Spinner && ordered[i].restartKiaiCount;
            int[] slots = bossSlots(beatmap, ordered, bossInfos.Count, atOnce);

            for (int i = 0; i < plannedSections; i++)
            {
                if (ordered[i].restartKiaiCount && !isBuildUp(i))
                    kiaiBurstCount = 0;

                int slot = slots[i];
                halveCounts = doubleSections.Contains(i);
                thirdCounts = halveCounts && atOnce == 3;
                planSection(i, slot, 0);

                for (int rank = 1; rank < atOnce && doubleSections.Contains(i); rank++)
                    planSection(i, (slot + rank) % bossInfos.Count, rank);

                halveCounts = false;
                thirdCounts = false;
            }

            // Yorihime repeats the other bosses of the map, and she does it herself: what they do comes from her and moves her (BossPeriod.Borrowed), with half as many bullets and beams. In a section of her own the others take turns, the
            // last one first and so on backwards, each after a gap of four beats. In a section she shares she repeats the boss or bosses beside her, all of it, four beats behind. (Only in a section too short for a piece, or on a map
            // with nobody else, she shoots back what leaves the screen, see ReturnAct.)
            var repeatRings = new HashSet<BulletEmitter>();

            void repeatBoss(int other, double from, double until, BossKind kind, int rank, bool halve)
            {
                int ringsBefore = emitters.Count;

                ordered.Add((from, until, kind, false));
                planningBorrowed = true;
                halveCounts = true;
                thirdCounts = atOnce == 3;
                planSection(ordered.Count - 1, other, rank);
                halveCounts = false;
                thirdCounts = false;
                planningBorrowed = false;

                for (int k = ringsBefore; halve && k < emitters.Count; k++)
                    repeatRings.Add(emitters[k]);
            }

            if (plannedSections > 0 && bossInfos.Count >= 2)
            {
                int yorihime = -1;

                for (int slot = 0; slot < bossInfos.Count; slot++)
                {
                    if (bossInfos[slot].Mechanic == BossMechanic.Return)
                        yorihime = slot;
                }

                int repeated = 0;

                for (int i = 0; yorihime >= 0 && i < plannedSections; i++)
                {
                    bool shared = doubleSections.Contains(i);
                    int rank = -1;

                    for (int k = 0; k < (shared ? atOnce : 1); k++)
                    {
                        if ((slots[i] + k) % bossInfos.Count == yorihime)
                            rank = k;
                    }

                    if (rank < 0)
                        continue;

                    var (pieceStart, pieceEnd, pieceKind, _) = ordered[i];
                    double pieceBeat = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(pieceStart).BeatLength), 1);
                    double gap = pieceBeat * DanmakusuBalance.YORIHIME_REPEAT_GAP_BEATS;
                    double length = pieceBeat * DanmakusuBalance.YORIHIME_REPEAT_BEATS;

                    if (shared)
                    {
                        // (the rings of a shared section are halved anyway)
                        for (int k = 0; k < atOnce && pieceEnd - pieceStart >= gap * 2; k++)
                        {
                            if (k != rank)
                                repeatBoss((slots[i] + k) % bossInfos.Count, pieceStart + gap, pieceEnd, pieceKind, rank, false);
                        }

                        continue;
                    }

                    for (double at = pieceStart + gap; pieceEnd - at >= length * 0.5; at += length + gap)
                    {
                        int other = (yorihime + bossInfos.Count - 1 - repeated++ % (bossInfos.Count - 1)) % bossInfos.Count;

                        repeatBoss(other, at, Math.Min(at + length, pieceEnd), pieceKind, rank, true);
                    }
                }
            }

            // Toyohime alone on the map: in every attack of hers she does what another boss does, one of the whole roster with a mechanic of her own, chosen by the attack (never the same twice in a row), herself, as Yorihime
            // repeats: it comes from her and moves her, with half its bullets and beams. Her own drop and pull are not there in those attacks (see BulletField.buildStage); her own bullets and her pauses are.
            if (DanmakusuBalance.TOYOHIME_REWORK && plannedSections > 0 && bossInfos.Count == 1 && bossInfos[0].Mechanic == BossMechanic.FreeFall)
            {
                var guests = BossRoster.All.Where(boss => boss.IsModified && boss.Mechanic is not (BossMechanic.FreeFall or BossMechanic.Return)).ToArray();
                int played = 0, last = -1;

                foreach (var period in bossList.Where(period => period.Boss.Mechanic == BossMechanic.FreeFall && !period.Borrowed && !period.Revival).ToList())
                {
                    foreach (var (start, end) in BossOps.Attacks(period))
                    {
                        if (end - start < 1 || guests.Length == 0)
                            continue;

                        // (chosen by the attack's time and number, well mixed: the same on every play of the map)
                        ulong mixed = (ulong)Math.Abs((long)Math.Round(start)) * 0x9E3779B97F4A7C15UL ^ (ulong)(played++ + 1) * 0xBF58476D1CE4E5B9UL;
                        mixed = (mixed ^ (mixed >> 31)) * 0x94D049BB133111EBUL;
                        mixed ^= mixed >> 29;
                        int pick = (int)(mixed % (ulong)guests.Length);

                        if (pick == last && guests.Length > 1)
                            pick = (pick + 1) % guests.Length;

                        last = pick;
                        bossInfos.Add(guests[pick]);
                        guestSlot = period.BossSlot;
                        repeatBoss(bossInfos.Count - 1, start, end, period.Kind, period.Rank, true);
                        guestSlot = -1;
                        bossInfos.RemoveAt(bossInfos.Count - 1);
                    }
                }
            }

            // Mokou and Kaguya are immortal: when every boss section of the map is over they come back 8 beats later and stay until the map ends, as kiai bosses (see BossPeriod.Revival; the last section left them room for it, above), both
            // at once when both are on the map. Only when the map goes on long enough after its last section.
            if (plannedSections > 0)
            {
                double lastEnd = ordered.Take(plannedSections).Max(section => section.end);
                double mapEnd = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Max(h => h.GetEndTime()) : lastEnd;
                double revivalBeat = Math.Max(DanmakusuBalance.BossBeat(beatmap.ControlPointInfo.TimingPointAt(lastEnd).BeatLength), 1);
                double revivalStart = lastEnd + revivalBeat * DanmakusuBalance.REVIVAL_DELAY_BEATS;
                var immortals = Enumerable.Range(0, bossInfos.Count).Where(slot => bossInfos[slot].Mechanic is BossMechanic.Rebirth or BossMechanic.ImpossibleRequests).ToList();

                if (immortals.Count > 0 && mapEnd - revivalStart >= revivalBeat * DanmakusuBalance.REVIVAL_MIN_BEATS)
                {
                    ordered.Add((revivalStart, mapEnd, BossKind.Kiai, false));
                    lastSection = ordered.Count - 1;
                    planningRevival = true;

                    // (two of them: a section of two bosses)
                    if (immortals.Count > 1)
                    {
                        doubleSections.Add(lastSection);
                        revivalSection = lastSection;
                    }

                    halveCounts = immortals.Count > 1;

                    for (int rank = 0; rank < immortals.Count; rank++)
                        planSection(lastSection, immortals[rank], rank);

                    halveCounts = false;
                    planningRevival = false;
                }
            }

            bosses = bossList.OrderBy(boss => boss.Start).ToArray();

            // ---- what the bosses' mechanics do to the rings that are planned (the field does the rest while the game runs, see BossOps) ----
            finishSequences();
            planMechanics();
            currentRank = 0;

            // the section of two bosses: half as many bullets in every ring; of three: a third as many
            foreach (int doubleSection in doubleSections)
            {
                var (halfStart, halfEnd, _, _) = ordered[doubleSection];

                for (int i = 0; i < emitters.Count; i++)
                {
                    var ring = emitters[i];

                    if (ring.Time >= halfStart && ring.Time < halfEnd && !ring.Derived && ring.Derive == DeriveKind.None && ring.Origins == null && ring.Count > 1 && !ring.KeepCount)
                        emitters[i] = atOnce == 3 && doubleSection != revivalSection ? ring.Thirded() : ring.Halved();
                }
            }

            // what Yorihime repeats has half as many bullets as the real thing (the map's own rings keep theirs)
            for (int i = 0; i < emitters.Count; i++)
            {
                var ring = emitters[i];

                if (repeatRings.Contains(ring) && !ring.Derived && ring.Derive == DeriveKind.None && ring.Origins == null && ring.Count > 1 && !ring.KeepCount)
                    emitters[i] = ring.Halved();
            }

            // Clownpiece's sections: every bullet is twice as big (their count stays the same)
            var moonWindows = bossList.Where(boss => boss.Boss.Mechanic == BossMechanic.Moon).Select(boss => (start: boss.ShootStart, end: boss.End)).ToList();

            if (moonWindows.Count > 0)
            {
                var doubled = new List<BulletEmitter>(emitters.Count * 2);

                foreach (var emitter in emitters)
                {
                    bool inside = false;

                    foreach (var (start, end) in moonWindows)
                    {
                        if (emitter.Time >= start && emitter.Time < end)
                            inside = true;
                    }

                    if (inside && emitter.Derive == DeriveKind.None && emitter.Origins == null && !emitter.KeepsSize) // (Utsuho's big balls and the trains keep their size)
                        doubleRing(emitter, doubled);
                    else
                        doubled.Add(emitter);
                }

                emitters = doubled;
            }

            // Koakuma's own shots are bats: her stars (and in a spinner the stars of the spinner) while she attacks; Chiyari's and Elly's too
            var batWindows = bossList.Where(boss => boss.Boss.Mechanic is BossMechanic.Bats or BossMechanic.Bloodlust or BossMechanic.Reaping).ToList();

            if (batWindows.Count > 0)
            {
                foreach (var emitter in emitters)
                {
                    if (emitter.Shape != BulletShape.SoftStar || emitter.Derived || emitter.Derive != DeriveKind.None)
                        continue;

                    foreach (var window in batWindows)
                    {
                        if (emitter.Time >= window.ShootStart && emitter.Time < window.End && (!emitter.FollowsBoss || emitter.BossRank == window.Rank))
                            emitter.Bats = true;
                    }
                }
            }

            // Wakasagihime's own shots are bubbles, a quarter as many (in a spinner the spinner's stars too): her act makes them bubbles as they are fired. (Here, after the halving of the sections of two or three, which makes new rings.)
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Tide && !period.Borrowed))
            {
                for (int i = 0; i < emitters.Count; i++)
                {
                    var ring = emitters[i];
                    bool own = IsOkinaRing(ring, period, period.ShootStart, period.End)
                               || (period.Kind == BossKind.Spinner && !ring.FollowsBoss && !ring.Derived && ring.Derive == DeriveKind.None && ring.Shape == BulletShape.SoftStar && ring.Time >= period.ShootStart && ring.Time < period.End);

                    if (!own || ring.Bubbles)
                        continue;

                    // (a quarter as many: halved, and halved again)
                    var fewer = ring.Count > 1 && !ring.KeepCount ? ring.Halved() : ring;
                    fewer = fewer.Count > 1 && !fewer.KeepCount ? fewer.Halved() : fewer;
                    fewer.Bubbles = true;
                    emitters[i] = fewer;
                }
            }

            // the field binary-searches this list by time, so it must be sorted (ticks interleave with later circles).
            // (stable: rings of the same time keep the order they were added in, which is also their draw order)
            // Daiyousei: what is fired in her attacks is green, hurts like a slider body, goes round once and cannot be destroyed
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Verdant))
            {
                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    foreach (var ring in emitters)
                    {
                        if (ring.Time < start || ring.Time >= end || ring.Derive != DeriveKind.None || ring.Sharp)
                            continue;

                        ring.HurtsLikeSlider = true;

                        if (!ring.KeepsSize)
                        {
                            ring.VisualRadius *= DanmakusuBalance.DAIYOUSEI_SIZE;
                            ring.HitRadius *= DanmakusuBalance.DAIYOUSEI_SIZE;
                            ring.VerdantScale = DanmakusuBalance.DAIYOUSEI_SIZE; // (what has left the screen and come in again is its own size again: BulletEmitter.LostVerdant)
                        }

                        if (!ring.Ricochet && !ring.Falling && !ring.Homing)
                        {
                            ring.WrapsOnce = true;
                            ring.Area = ricochetArea;
                            ring.Lifetime *= DanmakusuBalance.DAIYOUSEI_LIFE;
                        }
                    }
                }
            }

            // Wakasagihime: what is fired in her attacks swims
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Tide))
            {
                float size = DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize) * DanmakusuBalance.WAKASAGI_WAVE_CIRCLES;
                float rate = (float)(Math.Tau / (Math.Max(period.BeatLength, 1) * DanmakusuBalance.WAKASAGI_WAVE_BEATS));

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    foreach (var ring in emitters)
                    {
                        // (not what wraps round the screen, Daiyousei's: its place is worked out again and it would not swim, see BulletEmitter.Waves; nor Junko's rings of what she counted, BulletEmitter.Reshot)
                        if (ring.Time >= start && ring.Time < end && !ring.Ricochet && !ring.Falling && !ring.Homing && !ring.WrapsOnce && !ring.Sharp && !ring.Reshot && ring.Derive == DeriveKind.None)
                        {
                            ring.WaveSize = size;
                            ring.WaveRate = rate;
                            ring.HurtsLikeSlider = true; // (what swims hurts like a slider body, as its trace does: it is not used up by a hit)
                        }
                    }
                }
            }

            // Yumemi: in her attacks every tenth bullet has a beam for a tail (see BulletField.Tails)
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Possibility))
            {
                double tail = Math.Max(period.BeatLength, 1) * DanmakusuBalance.YUMEMI_TAIL_BEATS;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    foreach (var ring in emitters)
                    {
                        if (ring.Time >= start && ring.Time < end && !ring.Derived && ring.Derive == DeriveKind.None && ring.TailMs <= 0)
                        {
                            ring.TailMs = tail;
                            ring.TailEvery = DanmakusuBalance.YUMEMI_TAIL_EVERY;
                            ring.Lifetime += tail;
                        }
                    }
                }
            }

            // Futo: everything fired in her sections flies slower and slower towards the wall
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Drift))
            {
                foreach (var ring in emitters)
                {
                    if (ring.Time >= period.ShootStart && ring.Time < period.End && !ring.Ricochet && !ring.Falling && !ring.Homing && ring.Derive == DeriveKind.None && !ring.Sharp)
                    {
                        ring.SlowsToWall = true;
                        ring.SlowTo = DanmakusuBalance.FUTO_SPEED_AT_WALL;
                        ring.Area = ricochetArea;
                        ring.Lifetime /= DanmakusuBalance.FUTO_SPEED_AT_WALL;
                    }
                }
            }

            // Ran's own talismans start twice as fast and slow down to a quarter of their speed a playfield's length out
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Foxes))
            {
                foreach (var ring in emitters)
                {
                    if (IsOkinaRing(ring, period, period.ShootStart, period.End))
                    {
                        ring.SlowsToWall = true;
                        ring.SlowFrom = DanmakusuBalance.RAN_TALISMAN_FAST;
                        ring.SlowTo = DanmakusuBalance.RAN_TALISMAN_SLOW;
                        ring.SlowOver = OsuPlayfield.BASE_SIZE.X;
                        ring.Lifetime /= DanmakusuBalance.RAN_TALISMAN_SLOW;
                    }
                }
            }

            // Byakuren's passive: the ricochet stars fired in her sections, whoever fires them (every fourth has a tail, see BulletField.Tails; the ring stays until the tails have followed the stars out)
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Scrolls))
            {
                double tail = Math.Max(period.BeatLength, 1) * DanmakusuBalance.BYAKUREN_STAR_TAIL_BEATS;

                foreach (var ring in emitters)
                {
                    if (ring.Ricochet && ring.Time >= period.ShootStart && ring.Time < period.End && (ring.DrawShape ?? ring.Shape) is BulletShape.Star or BulletShape.SoftStar)
                    {
                        ring.Lifetime += tail - ring.TailMs;
                        ring.TailMs = tail;
                    }
                }
            }

            // Kaguya: every ring fired in her sections turns round its centre as it flies, the other way from the ring before. When she has come back the bullets of each four beats are ellipses, stars, talismans, then all
            // of those mixed, and so on until the song ends, and every twentieth bullet (counted on over all the rings) is three times as big
            foreach (var period in bossList.Where(period => period.Boss?.Mechanic == BossMechanic.ImpossibleRequests))
            {
                int turning = 0;
                long counted = 0;
                double shapeMs = Math.Max(period.BeatLength, 1) * DanmakusuBalance.KAGUYA_SHAPE_BEATS;

                foreach (var ring in emitters.Where(r => r.Time >= period.ShootStart && r.Time < period.End && !r.Derived && r.Derive == DeriveKind.None && !r.Sharp && r.Count > 0).OrderBy(r => r.Time))
                {
                    if (!ring.Ricochet && !ring.Homing)
                    {
                        ring.Curl = turning++ % 2 == 0 ? DanmakusuBalance.KAGUYA_SPIN : -DanmakusuBalance.KAGUYA_SPIN;
                        ring.CurlsTogether = true;
                    }

                    if (!period.Revival || ring.KeepsSize)
                        continue;

                    switch ((int)((ring.Time - period.ShootStart) / shapeMs) % 4)
                    {
                        case 0:
                            ring.DrawShape = BulletShape.Ellipse;
                            break;

                        case 1:
                            ring.DrawShape = BulletShape.Star;
                            break;

                        case 2:
                            ring.DrawShape = BulletShape.Talisman;
                            break;

                        default:
                            ring.MixedShapes = new[] { BulletShape.Ellipse, BulletShape.Star, BulletShape.Talisman };
                            break;
                    }

                    ring.BigBase = (int)(counted % DanmakusuBalance.KAGUYA_BIG_EVERY);
                    ring.BigEvery = DanmakusuBalance.KAGUYA_BIG_EVERY;
                    ring.BigFactor = DanmakusuBalance.KAGUYA_BIG;
                    counted += ring.Count;
                }
            }

            // Mugetsu's vortex: every ring that is on the screen during one of her attacks (anyone's, fired before it or in it) goes round her then; one for the whole map, the field puts her place for each attack
            var swirlAttacks = bossList.Where(period => period.Boss?.Mechanic == BossMechanic.Crescent).SelectMany(period => BossOps.Attacks(period).Where(a => a.End > a.Start).Select(a => (a.Start, a.End, period.Rank)))
                                       .OrderBy(a => a.Start).ToList();

            if (swirlAttacks.Count > 0)
            {
                var swirl = new Swirl
                {
                    Starts = swirlAttacks.Select(a => a.Start).ToArray(),
                    Ends = swirlAttacks.Select(a => a.End).ToArray(),
                    Ranks = swirlAttacks.Select(a => a.Rank).ToArray(),
                    Centres = Enumerable.Repeat(OsuPlayfield.BASE_SIZE / 2f, swirlAttacks.Count).ToArray(),
                    Grabbed = Enumerable.Repeat(OsuPlayfield.BASE_SIZE / 2f, swirlAttacks.Count).ToArray(),
                    Placed = new bool[swirlAttacks.Count],
                };

                float longer = OsuPlayfield.BASE_SIZE.Length * DanmakusuBalance.MUGETSU_SWIRL_LONGER;

                foreach (var ring in emitters)
                {
                    // (not the homing arrowheads, which the field steps, nor the ricochets, whose bounces off the walls would be turned away from the walls, nor a train)
                    if (ring.Count == 0 || ring.Homing || ring.Ricochet || ring.Derive == DeriveKind.Train || !swirl.Meets(ring.Time, ring.Lifetime))
                        continue;

                    ring.Swirl = swirl;
                    ring.Lifetime += longer / Math.Max(ring.Speed, 1e-3f); // (going round her it takes longer to leave the screen; what has left it is gone anyway)
                }
            }

            // (nothing is smaller than this, whatever has been done to it)
            float smallest = radius * DanmakusuBalance.BULLET_SMALLEST;

            foreach (var ring in emitters)
            {
                if (!ring.KeepsSize && ring.VisualRadius > 0f && ring.VisualRadius < smallest)
                {
                    ring.HitRadius *= smallest / ring.VisualRadius;
                    ring.VisualRadius = smallest;
                }
            }

            // A ring made out of another one (Cirno's bursts, Kyouko's echoes) was planned when that one was still itself; if it has been replaced since (halved in a section of two bosses, a third of it in one of three, made
            // bigger, rebuilt...), the burst follows it to what stands in its place now, the copy that fires when it did (it was made from a ring that is not in play any more: never frozen, never pulled, the bursts came out
            // where that one would have flown to)
            var inPlay = new HashSet<BulletEmitter>(emitters, ReferenceEqualityComparer.Instance);
            var standsFor = new Dictionary<BulletEmitter, BulletEmitter>(ReferenceEqualityComparer.Instance);

            foreach (var ring in emitters)
            {
                var first = ring.Lineage;

                if (first != null && !inPlay.Contains(first) && (!standsFor.TryGetValue(first, out var known) || (known.Time != first.Time && ring.Time == first.Time)))
                    standsFor[first] = ring;
            }

            foreach (var ring in emitters)
            {
                if (ring.Source is BulletEmitter source && !inPlay.Contains(source) && standsFor.TryGetValue(source.Lineage ?? source, out var copy))
                    ring.Source = copy;
            }

            // every bullet and beam is gone 64 beats after it came, whatever slowed it or kept it on the screen (a bullet fades out over the last half second; LIFE_MOST_BEATS: it took the place of "what crawls fades")
            double mostLife(double time) => DanmakusuBalance.LIFE_MOST_BEATS * Math.Max(DanmakusuBalance.CapBeat(beatmap.ControlPointInfo.TimingPointAt(time).BeatLength), 1);

            foreach (var ring in emitters)
            {
                double most = mostLife(ring.Time);

                if (ring.Lifetime > most)
                {
                    ring.Lifetime = most;
                    ring.FadesFrom = ring.FadesFrom > 0 ? Math.Min(ring.FadesFrom, ring.Time + most - DanmakusuBalance.LIFE_FADE_MS) : ring.Time + most - DanmakusuBalance.LIFE_FADE_MS;
                }
            }

            foreach (var laser in laserList)
            {
                laser.MostLifetime = Math.Max(mostLife(laser.StartTime) - (laser.EndTime - laser.StartTime), 0);
                laser.Lifetime = Math.Min(laser.Lifetime, laser.MostLifetime);
            }

            var sortedEmitters = emitters.OrderBy(knifeOrder => knifeOrder.Time).ToArray();
            laserList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            lasers = laserList.ToArray();
            return sortedEmitters;
        }

        /// <summary>Joon's own bullets: golden, glowing, pulled the way they were thrown at a quarter of Zanmu's pull (her act takes them over as they are thrown: its flight is what they fly).</summary>
        private static void makeJoonBullets(BulletEmitter ring)
        {
            float gravity = DanmakusuBalance.ZANMU_GRAVITY / DanmakusuBalance.JOON_OWN_FALL;

            ring.Tint = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_YELLOW;
            ring.Bloom = DanmakusuBalance.TOYOHIME_GOLD_BLOOM;
            ring.Gravity = gravity;
            ring.Lifetime = Math.Max(ring.Lifetime, (ring.Speed + Math.Sqrt(ring.Speed * ring.Speed + 2 * gravity * OsuPlayfield.BASE_SIZE.Length * 2f)) / gravity);
        }

        /// <summary>The bosses whose spinner-style laser bursts have been taken away: all of them but Marisa (her Master Spark keeps the beams).</summary>
        private static bool dropsPlaceholderLasers(BossMechanic mechanic) => mechanic != BossMechanic.MasterSpark;

        /// <summary>Is <paramref name="ring"/> one that the boss of <paramref name="period"/> shoots herself between <paramref name="start"/> and <paramref name="end"/>?</summary>
        private static bool IsOkinaRing(BulletEmitter ring, BossPeriod period, double start, double end)
            => ring.FollowsBoss && ring.BossRank == period.Rank && !ring.Derived && ring.Derive == DeriveKind.None && ring.Time >= start && ring.Time < end;

        /// <summary>
        /// Twice the size: the same ring (same bullets, same count) with bigger bullets, added to <paramref name="result"/>.
        /// </summary>
        private static void doubleRing(BulletEmitter e, List<BulletEmitter> result)
        {
            const float factor = 2f;

            BulletEmitter copy(Vector2[] directions, float[]? scales, float aimOffset)
            {
                float min = 1f;

                if (scales != null)
                {
                    foreach (float value in scales)
                        min = Math.Min(min, value);
                }

                return new BulletEmitter
                {
                    Time = e.Time,
                    Origin = e.Origin,
                    SpawnRadius = e.SpawnRadius,
                    Speed = e.Speed,
                    ComboIndex = e.ComboIndex,
                    VisualRadius = e.VisualRadius * factor,
                    HitRadius = e.HitRadius * factor,
                    Shape = e.Shape,
                    AimAtPlayer = e.AimAtPlayer,
                    AimOffset = aimOffset,
                    Directions = directions,
                    RadialScales = scales,
                    MinRadialScale = scales == null ? 1f : min,
                    Ricochet = e.Ricochet,
                    FollowsBoss = e.FollowsBoss,
                    BossRank = e.BossRank,
                    Straight = e.Straight,
                    Gravity = e.Gravity,
                    ReleaseRadius = e.ReleaseRadius,
                    Area = e.Area,
                    Lifetime = e.Lifetime,
                    KeepCount = e.KeepCount, // (Utsuho's six big ones stay six and sharp)
                    Sharp = e.Sharp,
                };
            }

            result.Add(copy((Vector2[])e.Directions.Clone(), (float[]?)e.RadialScales?.Clone(), e.AimOffset));
        }

        /// <summary>
        /// Douglas-Peucker on points[<paramref name="start"/>..<paramref name="end"/>]: appends to <paramref name="result"/> the indices to keep so that
        /// the polyline through them is nowhere farther than <paramref name="tolerance"/> from the original. Both ends are kept
        /// (the first only if the result is still empty, so consecutive ranges join up).
        /// </summary>
        private static void simplify(Vector2[] points, int start, int end, float tolerance, List<int> result)
        {
            var keep = new bool[end - start + 1];
            keep[0] = keep[^1] = true;

            var stack = new Stack<(int first, int last)>();
            stack.Push((0, keep.Length - 1));

            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float farthest = tolerance;
                int index = -1;

                for (int i = first + 1; i < last; i++)
                {
                    float d = distanceToSegment(points[start + i], points[start + first], points[start + last]);

                    if (d > farthest)
                    {
                        farthest = d;
                        index = i;
                    }
                }

                if (index < 0)
                    continue;

                keep[index] = true;
                stack.Push((first, index));
                stack.Push((index, last));
            }

            for (int i = result.Count == 0 ? 0 : 1; i < keep.Length; i++)
            {
                if (keep[i])
                    result.Add(start + i);
            }
        }

        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 1e-9f)
                return Vector2.Distance(point, a);

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>One zone per hit circle: standing inside it when it is auto-hit costs one hit. <paramref name="copies"/> (Doremy's and Sagume's copies) have zones too.</summary>
        public static CircleZone[] PlanCircleZones(IBeatmap beatmap, IEnumerable<HitObject>? copies = null)
        {
            var zones = new List<CircleZone>();

            foreach (var hitObject in copies == null ? beatmap.HitObjects : beatmap.HitObjects.Concat(copies))
            {
                if (hitObject is not HitCircle circle)
                    continue;

                // (a catch circle hurts like a slider body all the way down instead: PlanSliderZones)
                if (ConvertedModes.Of(circle) is { Mode: ConvertedModes.CATCH })
                    continue;

                zones.Add(new CircleZone
                {
                    Source = circle,
                    Time = circle.StartTime,
                    Position = circle.StackedPosition,
                    Radius = OsuHitObject.OBJECT_RADIUS * circle.Scale,
                });
            }

            zones.Sort((a, b) => a.Time.CompareTo(b.Time));
            return zones.ToArray();
        }

        /// <summary>One zone per slider: the damaging body, active from the slider's start to its end. <paramref name="copies"/> (Doremy's and Sagume's copies) have zones too.</summary>
        public static SliderZone[] PlanSliderZones(IBeatmap beatmap, IEnumerable<HitObject>? copies = null)
        {
            var zones = new List<SliderZone>();

            foreach (var hitObject in copies == null ? beatmap.HitObjects : beatmap.HitObjects.Concat(copies))
            {
                // osu!catch: circles and sliders hurt like a slider body all the way down, from when they come in at the top until they have landed (a slider: until its end has)
                if (hitObject is OsuHitObject falling && hitObject is HitCircle or Slider && ConvertedModes.Of(hitObject) is { Mode: ConvertedModes.CATCH } fall)
                {
                    var fallPoints = new List<Vector2>();

                    if (falling is Slider stream)
                    {
                        double length = stream.Path.Distance;
                        int pieces = Math.Max(1, (int)Math.Ceiling(length / Math.Max(2f, OsuHitObject.OBJECT_RADIUS * stream.Scale * 0.5f)));

                        for (int i = 0; i <= pieces; i++)
                            fallPoints.Add(stream.StackedPosition + stream.Path.PositionAt(i / (double)pieces));
                    }
                    else
                        fallPoints.Add(falling.StackedPosition);

                    zones.Add(new SliderZone
                    {
                        Source = falling,
                        StartTime = falling.StartTime - falling.TimePreempt,
                        EndTime = falling.GetEndTime(),
                        Points = fallPoints.ToArray(),
                        Radius = OsuHitObject.OBJECT_RADIUS * falling.Scale,
                        FallSpeed = fall.FallSpeed,
                        LandsAt = falling.StartTime,
                    });

                    continue;
                }

                if (hitObject is not Slider slider)
                    continue;

                float radius = OsuHitObject.OBJECT_RADIUS * slider.Scale;
                double distance = slider.Path.Distance;
                int segments = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(2f, radius * 0.5f)));
                var points = new Vector2[segments + 1];

                for (int i = 0; i <= segments; i++)
                    points[i] = slider.StackedPosition + slider.Path.PositionAt(i / (double)segments);

                zones.Add(new SliderZone
                {
                    Source = slider,
                    StartTime = slider.StartTime,
                    EndTime = slider.EndTime,
                    Points = points,
                    Radius = radius,
                });
            }

            zones.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            return zones.ToArray();
        }
    }
}
