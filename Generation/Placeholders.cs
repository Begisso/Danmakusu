using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.Generation
{
    /// <summary>
    /// Songs without a map (the user, 2026-11-13: "songs without maps at all, just songs in folder"): every song file put into <see cref="Folder"/> is given an empty osu!standard map of its own - its tempo
    /// found from the song (<see cref="Tempo"/>), its artist and title from the file's name - and imported into osu! as a set of its own, once (a placeholder deleted in osu! is not made again). Song select
    /// shows a placeholder only in Danmakusu with Map Generation on (<see cref="Stars.SongSelectStars"/>), which makes its map. Done in the background when osu! starts and every two minutes after
    /// (<see cref="Stars.StarFiller"/>).
    /// </summary>
    public static class Placeholders
    {
        /// <summary>The folder for songs without a map.</summary>
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu", "danmakusu songs");

        /// <summary>The tag every placeholder has (how song select tells them).</summary>
        public const string TAG = "danmakusu-placeholder";

        private static readonly string[] song_files = { ".mp3", ".ogg", ".wav", ".flac", ".m4a" };

        /// <summary>(set from what is selected: song select shows the placeholders then) Is Map Generation on?</summary>
        public static volatile bool MapGenerationOn;

        public static bool IsPlaceholder(BeatmapInfo beatmap) => beatmap.Metadata?.Tags?.Contains(TAG, StringComparison.Ordinal) == true;

        /// <summary>
        /// Imports a placeholder for every song in <see cref="Folder"/> not done yet (the ones done are listed in <paramref name="doneFile"/>, by name, size and time, so that one changed is done again).
        /// Runs on a background thread; returns how many were imported.
        /// </summary>
        public static int ImportNew(BeatmapManager manager, string doneFile)
        {
            try
            {
                Directory.CreateDirectory(Folder);
            }
            catch (Exception e)
            {
                Logger.Log($"Danmakusu: the folder for songs without maps could not be made ({e.Message})");
                return 0;
            }

            var done = new HashSet<string>(File.Exists(doneFile) ? File.ReadAllLines(doneFile) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            int imported = 0;

            foreach (string path in Directory.EnumerateFiles(Folder).Where(p => song_files.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                string key = $"{info.Name}\t{info.Length}\t{info.LastWriteTimeUtc.Ticks}";

                if (done.Contains(key))
                    continue;

                try
                {
                    byte[] osz = Make(path, out string name, out string report);

                    using (var stream = new MemoryStream(osz))
                        manager.Import(new ImportTask(stream, name + ".osz")).GetAwaiter().GetResult();

                    Logger.Log($"Danmakusu: a placeholder for {info.Name}: {report}");
                    imported++;
                }
                catch (Exception e)
                {
                    Logger.Log($"Danmakusu: no placeholder for {info.Name} ({e.Message})", level: LogLevel.Important);
                }

                // (tried: not tried again, whatever came of it, until the file changes)
                done.Add(key);
                File.AppendAllText(doneFile, key + Environment.NewLine);
            }

            return imported;
        }

        /// <summary>The .osz of a song's placeholder: the song and one empty map with the song's tempo. <paramref name="name"/>: the set's name; <paramref name="report"/>: the tempo found.</summary>
        public static byte[] Make(string songPath, out string name, out string report)
        {
            string file = Path.GetFileName(songPath);
            string stem = Path.GetFileNameWithoutExtension(songPath);

            // (a tempo in the file's name - "Artist - Title [180].mp3", "[180 BPM]", or with where a bar starts in ms, "[180 1234]" - is taken instead of the one found; the beats are then placed by the
            // drums unless that is given too)
            double? namedBpm = null, namedOffset = null;
            var named = System.Text.RegularExpressions.Regex.Match(stem, @"\s*\[\s*(\d+(?:[.,]\d+)?)\s*(?:bpm)?\s*(?:[\s,;@]\s*(-?\d+(?:[.,]\d+)?)\s*(?:ms)?)?\s*\]\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (named.Success)
            {
                namedBpm = double.Parse(named.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

                if (named.Groups[2].Success)
                    namedOffset = double.Parse(named.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

                stem = stem[..named.Index].TrimEnd();

                if (namedBpm < 20 || namedBpm > 1000)
                    namedBpm = null;
            }

            // (the artist and the title from "Artist - Title")
            int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
            string artist = dash > 0 ? stem[..dash].Trim() : "Unknown";
            string title = dash > 0 ? stem[(dash + 3)..].Trim() : stem.Trim();
            name = $"{artist} - {title}";

            var song = SongBands.FromFile(songPath);

            double offset = 0, beatLength = 500;
            bool found;

            if (namedBpm != null)
            {
                beatLength = 60000 / namedBpm.Value;
                offset = namedOffset ?? (song != null ? Tempo.Place(song, beatLength) : 0);
                found = true;
                report = $"{namedBpm:0.##} BPM from the file's name, from {offset:F0} ms{(namedOffset != null ? " (given)" : string.Empty)}";
            }
            else
            {
                found = song != null && Tempo.Find(song, out offset, out beatLength);
                report = found ? $"{60000 / beatLength:F2} BPM from {offset:F0} ms, {song!.Length / 1000:F0} s" : "no tempo could be found: 120 BPM";
            }

            string map = text(file, artist, title, offset, beatLength);

            using var output = new MemoryStream();

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                using (var entry = zip.CreateEntry(file, CompressionLevel.NoCompression).Open())
                using (var input = File.OpenRead(songPath))
                    input.CopyTo(entry);

                using (var writer = new StreamWriter(zip.CreateEntry(clean($"{artist} - {title} (Danmakusu) [Map Generation].osu")).Open(), new UTF8Encoding(false)))
                    writer.Write(map);
            }

            return output.ToArray();
        }

        private static string clean(string fileName) => string.Concat(fileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static string text(string audio, string artist, string title, double offset, double beatLength)
        {
            string number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

            return string.Join("\r\n", new[]
            {
                "osu file format v14",
                "",
                "[General]",
                $"AudioFilename: {audio}",
                "AudioLeadIn: 0",
                "PreviewTime: -1",
                "Countdown: 0",
                "SampleSet: Normal",
                "StackLeniency: 0.7",
                "Mode: 0",
                "",
                "[Metadata]",
                $"Title:{title}",
                $"TitleUnicode:{title}",
                $"Artist:{artist}",
                $"ArtistUnicode:{artist}",
                "Creator:Danmakusu",
                "Version:Map Generation",
                "Source:",
                $"Tags:{TAG}",
                "BeatmapID:0",
                "BeatmapSetID:-1",
                "",
                "[Difficulty]",
                "HPDrainRate:5",
                "CircleSize:4",
                "OverallDifficulty:7",
                "ApproachRate:8",
                "SliderMultiplier:1.4",
                "SliderTickRate:1",
                "",
                "[Events]",
                "//Background and Video events",
                "//Break Periods",
                "",
                "[TimingPoints]",
                $"{number(offset)},{number(beatLength)},4,1,0,70,1,0",
                "",
                "[HitObjects]",
                "",
            });
        }
    }
}
