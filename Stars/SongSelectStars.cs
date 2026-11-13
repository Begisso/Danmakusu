using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Danmakusu.Stars
{
    /// <summary>
    /// Song select sorts by difficulty, groups by difficulty and filters by stars ("stars>5", the star range of the settings) with the star rating osu! keeps in its database: for an osu! map osu!standard's,
    /// for a mania map mania's. While Danmakusu is the mode, these three use the Danmakusu stars instead (<see cref="DanmakusuStars"/>; a map not rated yet keeps osu!'s number until it is), and song select
    /// sorts again now and then while the background rating brings new ones in (<see cref="Resort"/>). The other modes, the database and everything else of song select stay as they are. Done with Harmony,
    /// like <see cref="OtherModesInSongSelect"/>: if osu! has changed where these are, only this is lost, and the log says so.
    /// </summary>
    public static class SongSelectStars
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
                Logger.Log(Status = "Danmakusu: song select sorts, groups and filters Danmakusu by its own stars");
            }
            catch (Exception e)
            {
                Logger.Log(Status = $"Danmakusu: song select cannot sort by Danmakusu's stars ({e.GetType().Name}: {e.Message}) {e.InnerException?.Message}");
            }
        }

        // ---- what osu! has, found once ----

        private const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static MethodInfo itemsCountSetter = null!;
        private static MethodInfo getGroupsBy = null!;
        private static MethodInfo defineGroupByStars = null!;
        private static MethodInfo memberwiseClone = null!;

        private static MethodInfo find(Type type, string name)
            => type.GetMethod(name, any) ?? throw new MissingMethodException(type.Name, name);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void patch()
        {
            itemsCountSetter = typeof(BeatmapCarouselFilterSorting).GetProperty(nameof(BeatmapCarouselFilterSorting.BeatmapItemsCount), any)?.GetSetMethod(true)
                               ?? throw new MissingMethodException(nameof(BeatmapCarouselFilterSorting), "set_BeatmapItemsCount");
            getGroupsBy = find(typeof(BeatmapCarouselFilterGrouping), "getGroupsBy");
            defineGroupByStars = find(typeof(BeatmapCarouselFilterGrouping), "defineGroupByStars");
            memberwiseClone = find(typeof(object), nameof(MemberwiseClone));

            var sortRun = find(typeof(BeatmapCarouselFilterSorting), nameof(BeatmapCarouselFilterSorting.Run));
            var getGroups = find(typeof(BeatmapCarouselFilterGrouping), "getGroups");
            var groupTogether = find(typeof(BeatmapCarouselFilterGrouping), nameof(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether));
            var match = find(typeof(BeatmapCarouselFilterMatching), nameof(BeatmapCarouselFilterMatching.CheckCriteriaMatch));
            var carouselMade = typeof(BeatmapCarousel).GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException(nameof(BeatmapCarousel), ".ctor");
            var requiresSorting = find(typeof(FilterCriteria), nameof(FilterCriteria.RequiresSorting));

            // (the sort dropdown: osu!'s Dropdown of SortMode, a value type, so these are its own code, not every dropdown's)
            var setItems = typeof(Dropdown<SortMode>).GetProperty(nameof(Dropdown<SortMode>.Items))?.GetSetMethod() ?? throw new MissingMethodException("Dropdown<SortMode>", "set_Items");
            var itemText = find(typeof(Dropdown<SortMode>), "GenerateItemText");

            // (everything is looked up before anything is patched: either all of it, or nothing)
            var harmony = new HarmonyLib.Harmony("osu.Game.Rulesets.Danmakusu.SongSelectStars");
            harmony.Patch(sortRun, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(sortPrefix)));
            harmony.Patch(getGroups, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(groupsPrefix)));
            harmony.Patch(groupTogether, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(groupTogetherPrefix)));
            harmony.Patch(match, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(matchPrefix)));
            harmony.Patch(carouselMade, postfix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(carouselPostfix)));
            harmony.Patch(requiresSorting, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(requiresSortingPrefix)));
            harmony.Patch(setItems, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(sortItemsPrefix)));
            harmony.Patch(itemText, prefix: new HarmonyLib.HarmonyMethod(typeof(SongSelectStars), nameof(sortTextPrefix)));
        }

        // ---- the sorts of our own: by the counts that are kept of a map, in every mode (they are the map's) ----

        /// <summary>
        /// Sorts that osu! does not have (numbers past its own, kept in its settings as numbers): by how many bullets, how many beams the map makes, and how many bosses it has room for. Their names say
        /// they are Danmakusu's (they are in every mode's dropdown).
        /// </summary>
        public const SortMode BULLETS = (SortMode)200, BEAMS = (SortMode)201, BOSSES = (SortMode)202;

        private static readonly Dictionary<SortMode, string> sort_names = new Dictionary<SortMode, string>
        {
            [BULLETS] = "Bullets - Danmakusu",
            [BEAMS] = "Beams - Danmakusu",
            [BOSSES] = "Most bosses - Danmakusu",
        };

        public static bool IsOurSort(SortMode sort) => sort_names.ContainsKey(sort);

        /// <summary>(the sort dropdown is given osu!'s sorts) ours come after them.</summary>
        private static void sortItemsPrefix(ref IEnumerable<SortMode> value)
        {
            var given = value.ToList();

            if (given.Contains(SortMode.Difficulty) && !given.Contains(BULLETS))
                value = given.Concat(sort_names.Keys).ToArray();
        }

        /// <summary>(the dropdown names an item) ours by their names; osu!'s would show a number.</summary>
        private static bool sortTextPrefix(SortMode item, ref osu.Framework.Localisation.LocalisableString __result)
        {
            if (!sort_names.TryGetValue(item, out string? name))
                return true;

            __result = name;
            return false;
        }

        /// <summary>(would song select keep a set's difficulties together, under the set's panel?) Not for ours: as for osu!'s sort by difficulty, every difficulty has a panel of its own.</summary>
        private static bool groupTogetherPrefix(FilterCriteria criteria, ref bool __result)
        {
            if (!IsOurSort(criteria.Sort))
                return true;

            __result = false;
            return false;
        }

        /// <summary>(osu! throws for a sort it does not know) ours always sort again, as its own by numbers do.</summary>
        private static bool requiresSortingPrefix(FilterCriteria __instance, ref bool __result)
        {
            if (!IsOurSort(__instance.Sort))
                return true;

            __result = true;
            return false;
        }

        /// <summary>The count a sort of ours goes by; a map whose count is not known yet goes last.</summary>
        private static double countOf(BeatmapInfo beatmap, SortMode sort)
        {
            if (!DanmakusuStars.TryGetRating(beatmap.MD5Hash, out var rating))
                return double.PositiveInfinity;

            return sort switch
            {
                BULLETS => rating.Bullets >= 0 ? rating.Bullets : double.PositiveInfinity,
                BEAMS => rating.Beams >= 0 ? rating.Beams : double.PositiveInfinity,
                _ => rating.Bosses > 0 ? rating.Bosses : double.PositiveInfinity,
            };
        }

        /// <summary>Is song select showing Danmakusu?</summary>
        private static bool ours(FilterCriteria? criteria) => criteria?.Ruleset?.ShortName == DanmakusuRuleset.SHORT_NAME;

        /// <summary>The stars song select goes by in Danmakusu: Danmakusu's, or osu!'s number while the map has not been rated yet.</summary>
        public static double StarsOf(BeatmapInfo beatmap) => DanmakusuStars.TryGet(beatmap.MD5Hash, out double stars) ? stars : beatmap.StarRating;

        // ---- sorting by difficulty ----

        /// <summary>
        /// (instead of <see cref="BeatmapCarouselFilterSorting.Run"/>, when Danmakusu sorts by difficulty, and for our sorts in any mode) the same as osu!'s, by the Danmakusu stars or by the count: one
        /// difficulty after another (our sorts never keep the sets together: <see cref="groupTogetherPrefix"/>); with the sets kept together, a set's difficulties by mode and then by stars. Ties as osu!'s:
        /// by title, newest set first, by the set.
        /// </summary>
        private static bool sortPrefix(BeatmapCarouselFilterSorting __instance, IEnumerable<CarouselItem> items, CancellationToken cancellationToken, Func<FilterCriteria> ___getCriteria,
                                       ref Task<List<CarouselItem>> __result)
        {
            var criteria = ___getCriteria();
            bool ourSort = IsOurSort(criteria.Sort);

            if (!ourSort && (!ours(criteria) || criteria.Sort != SortMode.Difficulty))
                return true;

            __result = Task.Run(() =>
            {
                bool groupedSets = BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(criteria);
                var list = items.ToList();
                itemsCountSetter.Invoke(__instance, new object[] { list.Count });

                // (each map's stars, and what a sort of ours goes by, looked up once, not at every comparison; in another mode a set's difficulties keep osu!'s stars)
                bool danmakusu = ours(criteria);
                var starsOf = new Dictionary<BeatmapInfo, double>(list.Count, ReferenceEqualityComparer.Instance);
                var keyOf = new Dictionary<BeatmapInfo, double>(list.Count, ReferenceEqualityComparer.Instance);

                foreach (var item in list)
                {
                    var beatmap = (BeatmapInfo)item.Model;
                    starsOf[beatmap] = danmakusu ? StarsOf(beatmap) : beatmap.StarRating;
                    keyOf[beatmap] = ourSort ? countOf(beatmap, criteria.Sort) : starsOf[beatmap];
                }

                return list.Order(Comparer<CarouselItem>.Create((x, y) =>
                {
                    var a = (BeatmapInfo)x.Model;
                    var b = (BeatmapInfo)y.Model;

                    if (groupedSets && a.BeatmapSet!.Equals(b.BeatmapSet))
                    {
                        int byMode = a.Ruleset.CompareTo(b.Ruleset);
                        return byMode != 0 ? byMode : starsOf[a].CompareTo(starsOf[b]);
                    }

                    int result = keyOf[a].CompareTo(keyOf[b]);

                    if (result == 0)
                        result = OrdinalSortByCaseStringComparer.DEFAULT.Compare(a.BeatmapSet!.Metadata.Title, b.BeatmapSet!.Metadata.Title);

                    if (result == 0)
                        result = b.BeatmapSet!.DateAdded.CompareTo(a.BeatmapSet!.DateAdded);

                    if (result == 0)
                        result = b.BeatmapSet!.ID.CompareTo(a.BeatmapSet!.ID);

                    return result;
                })).ToList();
            }, cancellationToken);

            return false;
        }

        // ---- grouping by difficulty ----

        /// <summary>(instead of osu!'s grouping, when Danmakusu groups by difficulty) the same groups (below 1 star, 1, 2, ... 15 and over), by the Danmakusu stars.</summary>
        private static bool groupsPrefix(BeatmapCarouselFilterGrouping __instance, List<CarouselItem> items, FilterCriteria criteria, ref object __result)
        {
            if (!ours(criteria) || criteria.Group != GroupMode.Difficulty)
                return true;

            Func<BeatmapInfo, IEnumerable<GroupDefinition>> define = beatmap => (IEnumerable<GroupDefinition>)defineGroupByStars.Invoke(__instance, new object[] { StarsOf(beatmap) })!;
            __result = getGroupsBy.Invoke(__instance, new object[] { define, items })!;
            return false;
        }

        // ---- filtering by stars ----

        [ThreadStatic]
        private static bool matching;

        // (the criteria without their star filters, made once for each criteria osu! filters by)
        private static readonly ConditionalWeakTable<FilterCriteria, FilterCriteria> withoutStars = new ConditionalWeakTable<FilterCriteria, FilterCriteria>();

        /// <summary>(instead of osu!'s check, when Danmakusu filters by stars) the stars are checked as osu! checks them, by the Danmakusu stars; everything else by osu!'s own check, with its star filters left out.</summary>
        private static bool matchPrefix(BeatmapInfo beatmap, FilterCriteria criteria, ref bool __result)
        {
            // (a song without a map of its own - a placeholder - is shown only in Danmakusu with Map Generation on, which makes its map; never in osu!'s own modes)
            if (!matching && Generation.Placeholders.IsPlaceholder(beatmap) && !(ours(criteria) && Generation.Placeholders.MapGenerationOn))
            {
                __result = false;
                return false;
            }

            if (matching || !ours(criteria) || (!criteria.StarDifficulty.HasFilter && !criteria.UserStarDifficulty.HasFilter))
                return true;

            double stars = StarsOf(beatmap);

            if ((criteria.StarDifficulty.HasFilter && !criteria.StarDifficulty.IsInRange(Math.Floor(stars * 100) / 100))
                || (criteria.UserStarDifficulty.HasFilter && !criteria.UserStarDifficulty.IsInRange(stars)))
            {
                __result = false;
                return false;
            }

            var plain = withoutStars.GetValue(criteria, original =>
            {
                var copy = (FilterCriteria)memberwiseClone.Invoke(original, null)!;
                copy.StarDifficulty = default;
                copy.UserStarDifficulty = default;
                return copy;
            });

            matching = true;

            try
            {
                __result = BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, plain);
            }
            finally
            {
                matching = false;
            }

            return false;
        }

        // ---- sorting again as new ratings come in ----

        private static WeakReference<BeatmapCarousel>? carousel;

        private static void carouselPostfix(BeatmapCarousel __instance) => carousel = new WeakReference<BeatmapCarousel>(__instance);

        /// <summary>(on the update thread) Song select filters again whatever it goes by (Map Generation was turned on or off: the songs without maps come or go).</summary>
        public static void Refilter()
        {
            if (carousel == null || !carousel.TryGetTarget(out var shown))
                return;

            try
            {
                if (shown.Criteria != null && shown.LoadState == LoadState.Loaded && shown.Parent != null)
                    shown.Filter(shown.Criteria);
            }
            catch (Exception e)
            {
                Logger.Log($"Danmakusu: song select could not filter again ({e.Message})");
            }
        }

        /// <summary>
        /// (on the update thread) Song select sorts, groups and filters again, if it shows Danmakusu and goes by stars in any of the three. True if it did. A filter already under way is left to finish.
        /// </summary>
        public static bool Resort()
        {
            if (carousel == null || !carousel.TryGetTarget(out var shown))
                return false;

            try
            {
                var criteria = shown.Criteria;

                if (criteria == null || shown.LoadState != LoadState.Loaded || shown.Parent == null || shown.IsFiltering)
                    return false;

                // (our sorts go by what is kept of the maps in any mode; the stars only in Danmakusu)
                bool byStars = ours(criteria) && (criteria.Sort == SortMode.Difficulty || criteria.Group == GroupMode.Difficulty || criteria.StarDifficulty.HasFilter || criteria.UserStarDifficulty.HasFilter);

                if (!byStars && !IsOurSort(criteria.Sort))
                    return false;

                shown.Filter(criteria);
                return true;
            }
            catch (Exception e)
            {
                // (song select was on its way out)
                Logger.Log($"Danmakusu: song select could not sort again ({e.Message})");
                return false;
            }
        }
    }
}
