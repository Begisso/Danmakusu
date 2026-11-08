using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Danmakusu.Stars
{
    /// <summary>
    /// What Danmakusu knows of every map that has been rated, by the map's MD5 (osu!'s own star number in its database is osu!standard's, or its own mode's, and is never touched): its Danmakusu stars, how many
    /// bullets and beams its own circles, sliders and spinners make (planned without bosses, as the stars are) and how many bosses it has room for. Song select sorts, groups and filters by them when Danmakusu
    /// is the mode (<see cref="SongSelectStars"/>) and shows the counts (<see cref="DanmakusuBeatmap"/>). Filled by the background rating of the whole library (<see cref="StarFiller"/>) and by every rating osu!
    /// has the calculator make without mods that change it (<see cref="DanmakusuDifficultyCalculator"/>: the panels' stars, without the bosses). Kept in danmakusu_stars.csv in osu!'s data folder, one line a
    /// map (md5, stars, <see cref="Bullets.StarRating.STORE_VERSION"/>, bullets, beams, bosses): a line of another version is rated again. Outside osu! (the tools) nothing is written, only kept while they run.
    /// </summary>
    public static class DanmakusuStars
    {
        /// <summary>(a map the planner could not rate: kept so that it is not tried again every time, but not a rating)</summary>
        public const double UNRATED = -1;

        /// <summary>What is known of a map. A count of -1 is not known yet; <see cref="Bosses"/> 0 is not known yet (a map has room for one at least).</summary>
        public readonly record struct Rating(double Stars, int Bullets, int Beams, int Bosses)
        {
            public bool Rated => Stars >= 0;

            /// <summary>Everything is known (or it is known that the map cannot be rated).</summary>
            public bool Complete => !Rated || (Bullets >= 0 && Beams >= 0 && Bosses > 0);
        }

        private static readonly ConcurrentDictionary<string, Rating> ratings = new ConcurrentDictionary<string, Rating>(StringComparer.OrdinalIgnoreCase);
        private static readonly object fileGate = new object();
        private static readonly List<string> pending = new List<string>();
        private static string? path;
        private static long added;

        /// <summary>How many ratings have come in since osu! started (song select sorts again when this has grown).</summary>
        public static long Added => Interlocked.Read(ref added);

        public static int Count => ratings.Count;

        /// <summary>Is everything known of this map (or that it cannot be rated)?</summary>
        public static bool Complete(string? md5) => !string.IsNullOrEmpty(md5) && ratings.TryGetValue(md5, out var rating) && rating.Complete;

        /// <summary>The Danmakusu stars of the map with this MD5, if it has been rated.</summary>
        public static bool TryGet(string? md5, out double stars)
        {
            if (TryGetRating(md5, out var rating))
            {
                stars = rating.Stars;
                return true;
            }

            stars = 0;
            return false;
        }

        /// <summary>What is known of the map with this MD5, if it has been rated.</summary>
        public static bool TryGetRating(string? md5, out Rating rating)
        {
            if (!string.IsNullOrEmpty(md5) && ratings.TryGetValue(md5, out rating) && rating.Rated)
                return true;

            rating = default;
            return false;
        }

        /// <summary>Keeps what is known of a map (what this does not know is kept from before), written to the file once osu! has said where it goes.</summary>
        public static void Put(string? md5, Rating rating)
        {
            if (string.IsNullOrEmpty(md5) || double.IsNaN(rating.Stars) || double.IsInfinity(rating.Stars))
                return;

            Rating merged = rating;

            if (ratings.TryGetValue(md5, out var had))
            {
                if (rating.Rated && had.Rated)
                {
                    merged = new Rating(rating.Stars, rating.Bullets >= 0 ? rating.Bullets : had.Bullets, rating.Beams >= 0 ? rating.Beams : had.Beams, rating.Bosses > 0 ? rating.Bosses : had.Bosses);
                }

                if (merged == had)
                    return;
            }

            ratings[md5] = merged;
            Interlocked.Increment(ref added);

            string line = lineOf(md5, merged);

            lock (fileGate)
            {
                if (path == null)
                {
                    pending.Add(line);
                    return;
                }

                try
                {
                    File.AppendAllText(path, line + "\n", Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Logger.Log($"Danmakusu: a star rating could not be saved ({e.Message})", level: LogLevel.Important);
                }
            }
        }

        public static void Put(string? md5, double stars) => Put(md5, new Rating(stars, -1, -1, 0));

        private static string lineOf(string md5, Rating rating)
            => string.Join(",", md5, rating.Stars.ToString("R", CultureInfo.InvariantCulture), Bullets.StarRating.STORE_VERSION.ToString(CultureInfo.InvariantCulture),
                rating.Bullets.ToString(CultureInfo.InvariantCulture), rating.Beams.ToString(CultureInfo.InvariantCulture), rating.Bosses.ToString(CultureInfo.InvariantCulture));

        private static bool tryParse(string line, out string md5, out Rating rating)
        {
            md5 = string.Empty;
            rating = default;
            string[] parts = line.Split(',');

            if (parts.Length != 6 || parts[0].Length != 32 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version != Bullets.StarRating.STORE_VERSION)
                return false;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double stars)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bullets)
                || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int beams)
                || !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bosses))
                return false;

            md5 = parts[0];
            rating = new Rating(stars, bullets, beams, bosses);
            return true;
        }

        /// <summary>osu! runs: the ratings are kept in <paramref name="file"/>. What it holds of this version is read; if it holds anything else (an older version, the same map twice) it is written again without that.</summary>
        public static void Enable(string file)
        {
            lock (fileGate)
            {
                if (path != null)
                    return;

                int kept = 0, dropped = 0;

                try
                {
                    if (File.Exists(file))
                    {
                        // (a map rated again later has a later line: the last one counts)
                        var fromFile = new Dictionary<string, Rating>(StringComparer.OrdinalIgnoreCase);

                        foreach (string line in File.ReadLines(file))
                        {
                            if (!tryParse(line, out string md5, out var rating))
                            {
                                dropped++;
                                continue;
                            }

                            if (fromFile.ContainsKey(md5))
                                dropped++;

                            fromFile[md5] = rating;
                        }

                        // (what this run has rated already is newer than the file)
                        foreach (var (md5, rating) in fromFile)
                            ratings.TryAdd(md5, rating);

                        kept = fromFile.Count;
                    }

                    if (dropped > 0)
                    {
                        var lines = new List<string>(ratings.Count);

                        foreach (var (md5, rating) in ratings)
                            lines.Add(lineOf(md5, rating));

                        string part = file + ".part";
                        File.WriteAllText(part, string.Join("\n", lines) + (lines.Count > 0 ? "\n" : string.Empty), Encoding.UTF8);
                        File.Move(part, file, true);
                        pending.Clear(); // (in the file now)
                    }

                    path = file;

                    if (pending.Count > 0)
                        File.AppendAllText(file, string.Join("\n", pending) + "\n", Encoding.UTF8);

                    pending.Clear();

                    // (song select may have sorted before they were read: it sorts again)
                    if (kept > 0)
                        Interlocked.Increment(ref added);

                    Logger.Log($"Danmakusu: {kept} star ratings read from {file}{(dropped > 0 ? $" ({dropped} of an older rating or twice left out)" : string.Empty)}");
                }
                catch (Exception e)
                {
                    path = file;
                    Logger.Log($"Danmakusu: the star ratings could not be read ({e.Message}); they are rated again", level: LogLevel.Important);
                }
            }
        }
    }
}
