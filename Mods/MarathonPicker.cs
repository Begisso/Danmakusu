using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Danmakusu.Stars;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The maps of a marathon in the settings of <see cref="DanmakusuModMarathon"/> (2026-11-27): a search box that searches the library as song select's does (osu!'s own query parser and matching, for
    /// Danmakusu, so with its stars and converted maps), and a list of the maps it finds - the checked ones first, in their order - where a click checks a map or takes it out again. A checked map shows
    /// the number of its place in the marathon where an unchecked one shows an empty box. Under the search (2026-11-27): four checks for the modes whose maps are found (osu!standard alone at first),
    /// how many maps Random checks (5 at first), Random - that many more at random among everything found, after what is checked already (which it leaves alone since 2026-11-28) - and Clear. When Marathon is first put on with nothing checked, the
    /// map selected in song select is checked as its first.
    /// </summary>
    public partial class MarathonPicker : SettingsItem<string>
    {
        protected override Drawable CreateControl() => new PickerControl();

        public partial class PickerControl : CompositeDrawable, IHasCurrentValue<string>
        {
            private const int most_shown = 200;
            private const float row_height = 30;

            private readonly BindableWithCurrent<string> current = new BindableWithCurrent<string>(string.Empty);

            public Bindable<string> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            [Resolved(CanBeNull = true)]
            private BeatmapStore? store { get; set; }

            [Resolved(CanBeNull = true)]
            private IRulesetStore? rulesets { get; set; }

            // (the map selected in song select: checked as the first map when Marathon is first put on, 2026-11-27)
            [Resolved(CanBeNull = true)]
            private IBindable<WorkingBeatmap>? selectedMap { get; set; }

            // (the Marathon settings whose list has been started so: a list cleared later stays clear)
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bindable<string>, object> started = new System.Runtime.CompilerServices.ConditionalWeakTable<Bindable<string>, object>();

            private readonly OsuTextBox search;
            private readonly SpriteText info;
            private readonly FillFlowContainer list;

            private List<BeatmapInfo>? library;
            private Dictionary<string, BeatmapInfo> byHash = new Dictionary<string, BeatmapInfo>();
            private List<BeatmapInfo> found = new List<BeatmapInfo>();
            private ScheduledDelegate? pendingSearch;
            private CancellationTokenSource? searching;

            private static readonly MethodInfo? apply_queries = typeof(FilterQueryParser).GetMethod("ApplyQueries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // (which modes' maps are found - osu!standard alone at first - and how many maps Random checks: kept while the game runs)
            private static readonly bool[] modes_on = { true, false, false, false };
            private static int random_amount = 5;

            private readonly OsuNumberBox amountBox;

            /// <summary>Is a map of a mode that is checked?</summary>
            private static bool modeOn(BeatmapInfo beatmap, bool[] on) => beatmap.Ruleset.OnlineID is >= 0 and <= 3 && on[beatmap.Ruleset.OnlineID];

            /// <summary>
            /// Random: as many maps as the box says, at random among all that are found (not only those shown), added after the ones checked already - those stay as they are, in their order (since 2026-11-28,
            /// "Already Selected songs are not affected by random": what was checked before was replaced).
            /// </summary>
            private void pickRandom()
            {
                if (int.TryParse(amountBox.Text, out int typed))
                    random_amount = Math.Clamp(typed, 1, 999);

                amountBox.Text = random_amount.ToString();

                var picked = DanmakusuModMarathon.MapsOf(current.Value).ToList();
                var already = new HashSet<string>(picked);
                var pool = found.Select(beatmap => beatmap.MD5Hash).Distinct().Where(md5 => !already.Contains(md5)).ToList();
                int wanted = picked.Count + random_amount;

                while (picked.Count < wanted && pool.Count > 0)
                {
                    int index = Random.Shared.Next(pool.Count);
                    picked.Add(pool[index]);
                    pool.RemoveAt(index);
                }

                current.Value = DanmakusuModMarathon.ValueOf(picked);
            }

            /// <summary>What song select would make of <paramref name="text"/> in Danmakusu: osu!'s own query parser (words, stars&gt;4, artist=..., length&lt;180, ...), converted maps shown.</summary>
            public static FilterCriteria CriteriaFor(RulesetInfo? danmakusu, string text)
            {
                var criteria = new FilterCriteria
                {
                    Ruleset = danmakusu,
                    AllowConvertedBeatmaps = true,
                    Mods = Array.Empty<Mod>(),
                };

                if (apply_queries != null)
                    apply_queries.Invoke(null, new object[] { criteria, text });
                else
                    criteria.SearchText = text;

                return criteria;
            }

            public PickerControl()
            {
                RelativeSizeAxes = Axes.X;
                Height = 460;

                search = new OsuTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    PlaceholderText = "Search (as in song select: words, stars>4, artist=..., length<180...)",
                };

                info = new SpriteText { Font = FontUsage.Default.With(size: 14), Y = 70 };

                // (under the search: the modes whose maps are found - osu!standard alone at first - and on the right how many random maps, Random and Clear: 2026-11-27)
                var modeRow = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Y = 38,
                };

                string[] modeNames = { "osu!", "taiko", "catch", "mania" };

                for (int mode = 0; mode < modeNames.Length; mode++)
                {
                    int which = mode;
                    modeRow.Add(new ModeCheck(modeNames[mode], () => modes_on[which], () =>
                    {
                        modes_on[which] = !modes_on[which];
                        runSearch();
                    }));
                }

                var clear = new RoundedButton
                {
                    Width = 80,
                    Height = 24,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Y = 38,
                    Text = "Clear",
                    Action = () => current.Value = string.Empty,
                };

                var random = new RoundedButton
                {
                    Width = 90,
                    Height = 24,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -86,
                    Y = 38,
                    Text = "Random",
                    Action = pickRandom,
                };

                amountBox = new OsuNumberBox
                {
                    Width = 56,
                    Height = 24,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -182,
                    Y = 38,
                    CommitOnFocusLost = true,
                    Text = random_amount.ToString(),
                };

                amountBox.OnCommit += (box, _) =>
                {
                    if (int.TryParse(box.Text, out int typed))
                        random_amount = Math.Clamp(typed, 1, 999);

                    box.Text = random_amount.ToString();
                };

                var amountLabel = new SpriteText
                {
                    Text = "Random maps:",
                    Font = FontUsage.Default.With(size: 14),
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -244,
                    Y = 42,
                };

                list = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                };

                InternalChildren = new Drawable[]
                {
                    search,
                    modeRow,
                    amountLabel,
                    amountBox,
                    random,
                    clear,
                    info,
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = 92 },
                        Child = new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = list },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // (Marathon just put on, with nothing checked: the map selected in song select is its first map - once; what is cleared later stays clear)
                var settings = current.Current;

                if (!started.TryGetValue(settings, out _))
                {
                    started.Add(settings, new object());
                    string? selected = selectedMap?.Value?.BeatmapInfo?.MD5Hash;

                    if (string.IsNullOrEmpty(settings.Value) && !string.IsNullOrEmpty(selected) && selectedMap?.Value is not DummyWorkingBeatmap)
                        settings.Value = selected;
                }

                current.BindValueChanged(_ => rebuild());
                search.Current.BindValueChanged(_ =>
                {
                    pendingSearch?.Cancel();
                    pendingSearch = Scheduler.AddDelayed(runSearch, 200);
                });

                info.Text = "Reading the library...";

                var source = store;

                Task.Run(() =>
                {
                    var maps = new List<BeatmapInfo>();

                    try
                    {
                        foreach (var set in source?.GetBeatmapSets(null) ?? Enumerable.Empty<BeatmapSetInfo>())
                        {
                            if (set.DeletePending)
                                continue;

                            foreach (var beatmap in set.Beatmaps)
                            {
                                if (!beatmap.Hidden && !string.IsNullOrEmpty(beatmap.MD5Hash))
                                    maps.Add(beatmap);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Danmakusu: Marathon could not read the library ({e.Message})", level: LogLevel.Important);
                    }

                    var hashes = new Dictionary<string, BeatmapInfo>();

                    foreach (var beatmap in maps)
                        hashes.TryAdd(beatmap.MD5Hash, beatmap);

                    Schedule(() =>
                    {
                        library = maps;
                        byHash = hashes;
                        runSearch();
                    });
                });
            }

            private FilterCriteria criteriaFor(string text) => CriteriaFor(rulesets?.GetRuleset(DanmakusuRuleset.SHORT_NAME) as RulesetInfo, text);

            /// <summary>Finds the maps the search says, in the background, as song select would.</summary>
            private void runSearch()
            {
                if (library == null)
                    return;

                searching?.Cancel();
                var cancel = searching = new CancellationTokenSource();
                var maps = library;
                bool[] on = (bool[])modes_on.Clone();
                FilterCriteria criteria;

                try
                {
                    criteria = criteriaFor(search.Current.Value ?? string.Empty);
                }
                catch (Exception e)
                {
                    info.Text = $"The search could not be read ({e.Message})";
                    return;
                }

                Task.Run(() =>
                {
                    var matched = new List<BeatmapInfo>();

                    foreach (var beatmap in maps)
                    {
                        if (cancel.IsCancellationRequested)
                            return;

                        if (!modeOn(beatmap, on))
                            continue;

                        try
                        {
                            if (BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, criteria))
                                matched.Add(beatmap);
                        }
                        catch (Exception)
                        {
                            // (a map osu! cannot match is left out)
                        }
                    }

                    var ordered = matched.OrderBy(b => b.Metadata.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Metadata.Title, StringComparer.OrdinalIgnoreCase)
                                         .ThenBy(SongSelectStars.StarsOf).ToList();

                    Schedule(() =>
                    {
                        if (cancel.IsCancellationRequested)
                            return;

                        found = ordered;
                        rebuild();
                    });
                }, cancel.Token);
            }

            private void toggle(BeatmapInfo beatmap)
            {
                var chosen = DanmakusuModMarathon.MapsOf(current.Value).ToList();

                if (!chosen.Remove(beatmap.MD5Hash))
                    chosen.Add(beatmap.MD5Hash);

                current.Value = DanmakusuModMarathon.ValueOf(chosen);
            }

            /// <summary>The checked maps first, in their order, then what the search found.</summary>
            private void rebuild()
            {
                var chosen = DanmakusuModMarathon.MapsOf(current.Value);
                var order = new Dictionary<string, int>();

                for (int i = 0; i < chosen.Length; i++)
                    order.TryAdd(chosen[i], i);

                var rows = new List<Drawable>();

                for (int i = 0; i < chosen.Length; i++)
                {
                    if (byHash.TryGetValue(chosen[i], out var beatmap))
                        rows.Add(new Row(beatmap, i, toggle));
                    else if (library != null)
                    {
                        string gone = chosen[i];
                        rows.Add(new MissingRow(gone, i, () => current.Value = DanmakusuModMarathon.ValueOf(DanmakusuModMarathon.MapsOf(current.Value).Where(h => h != gone))));
                    }
                }

                int shown = 0;

                foreach (var beatmap in found)
                {
                    if (order.ContainsKey(beatmap.MD5Hash))
                        continue;

                    if (shown++ >= most_shown)
                        break;

                    rows.Add(new Row(beatmap, -1, toggle));
                }

                list.Clear();
                list.AddRange(rows);

                if (library == null)
                    return;

                string maps = chosen.Length == 1 ? "1 map" : $"{chosen.Length} maps";
                info.Text = $"{maps} checked - {found.Count} found" + (found.Count - chosen.Count(order.ContainsKey) > most_shown ? $" (the first {most_shown} shown: search for more)" : string.Empty);
            }

            /// <summary>A map of the list: its box (its number when it is checked), its name, its mode and its stars.</summary>
            private partial class Row : CompositeDrawable
            {
                private readonly BeatmapInfo beatmap;
                private readonly Action<BeatmapInfo> toggle;
                private readonly Box background;
                private readonly bool chosen;
                private readonly SpriteText name;

                public Row(BeatmapInfo beatmap, int order, Action<BeatmapInfo> toggle)
                {
                    this.beatmap = beatmap;
                    this.toggle = toggle;
                    chosen = order >= 0;

                    RelativeSizeAxes = Axes.X;
                    Height = row_height;
                    Masking = true;
                    CornerRadius = 4;

                    string mode = beatmap.Ruleset.OnlineID switch
                    {
                        0 => string.Empty,
                        1 => "taiko",
                        2 => "catch",
                        3 => "mania",
                        _ => beatmap.Ruleset.ShortName,
                    };

                    InternalChildren = new Drawable[]
                    {
                        background = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = chosen ? 0.85f : 0.45f },
                        // (the box: empty, or the number of its place)
                        new Container
                        {
                            Size = new Vector2(24, 22),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = 4,
                            Masking = true,
                            CornerRadius = 4,
                            BorderThickness = 2,
                            BorderColour = chosen ? new Color4(0.55f, 0.85f, 1f, 1f) : new Color4(1f, 1f, 1f, 0.5f),
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = chosen ? new Color4(0f, 0f, 0f, 0.85f) : new Color4(0f, 0f, 0f, 0f), AlwaysPresent = true },
                                new SpriteText
                                {
                                    Text = chosen ? (order + 1).ToString() : string.Empty,
                                    Font = FontUsage.Default.With(size: order >= 99 ? 11 : 15, weight: "Bold"),
                                    Colour = new Color4(1f, 0.92f, 0.35f, 1f),
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        name = new SpriteText
                        {
                            Text = $"{beatmap.Metadata.Artist} - {beatmap.Metadata.Title} [{beatmap.DifficultyName}]",
                            Font = FontUsage.Default.With(size: 15),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = 36,
                            Truncate = true,
                        },
                        new SpriteText
                        {
                            Text = (mode.Length > 0 ? mode + "  " : string.Empty) + $"{SongSelectStars.StarsOf(beatmap):0.00}★",
                            Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                            Colour = new Color4(1f, 0.85f, 0.4f, 1f),
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            X = -8,
                        },
                    };
                }

                protected override void Update()
                {
                    base.Update();
                    name.MaxWidth = Math.Max(DrawWidth - 36 - 120, 40);
                }

                protected override bool OnHover(HoverEvent e)
                {
                    background.Alpha = chosen ? 0.95f : 0.65f;
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e) => background.Alpha = chosen ? 0.85f : 0.45f;

                protected override bool OnClick(ClickEvent e)
                {
                    toggle(beatmap);
                    return true;
                }
            }

            /// <summary>A mode whose maps are found or not: a box with its name, lit when it is checked.</summary>
            private partial class ModeCheck : CompositeDrawable
            {
                private readonly Func<bool> isOn;
                private readonly Action toggle;
                private readonly Box background;
                private readonly Container frame;

                public ModeCheck(string name, Func<bool> isOn, Action toggle)
                {
                    this.isOn = isOn;
                    this.toggle = toggle;
                    AutoSizeAxes = Axes.X;
                    Height = 24;

                    InternalChild = frame = new Container
                    {
                        AutoSizeAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        Masking = true,
                        CornerRadius = 4,
                        BorderThickness = 2,
                        Children = new Drawable[]
                        {
                            background = new Box { RelativeSizeAxes = Axes.Both },
                            new SpriteText
                            {
                                Text = name,
                                Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Horizontal = 10 },
                            },
                        },
                    };
                }

                protected override void Update()
                {
                    base.Update();

                    bool on = isOn();
                    background.Colour = on ? new Color4(0.25f, 0.55f, 0.8f, 1f) : new Color4(0f, 0f, 0f, 0.5f);
                    frame.BorderColour = on ? new Color4(0.55f, 0.85f, 1f, 1f) : new Color4(1f, 1f, 1f, 0.35f);
                }

                protected override bool OnClick(ClickEvent e)
                {
                    toggle();
                    return true;
                }
            }

            /// <summary>A checked map that is no longer in the library: a click takes it out.</summary>

            private partial class MissingRow : CompositeDrawable
            {
                private readonly Action remove;

                public MissingRow(string md5, int order, Action remove)
                {
                    this.remove = remove;
                    RelativeSizeAxes = Axes.X;
                    Height = row_height;
                    Masking = true;
                    CornerRadius = 4;

                    InternalChildren = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.3f, 0f, 0f, 0.7f) },
                        new SpriteText
                        {
                            Text = $"{order + 1}.  A map that is not in the library ({md5[..Math.Min(8, md5.Length)]}): it is left out. Click to take it off the list.",
                            Font = FontUsage.Default.With(size: 14),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = 8,
                        },
                    };
                }

                protected override bool OnClick(ClickEvent e)
                {
                    remove();
                    return true;
                }
            }
        }
    }
}
