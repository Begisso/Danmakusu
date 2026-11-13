using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.UI;
using osu.Game.Rulesets.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The chosen bosses in the settings of a boss selection mod: a slider for how many there are (as far as the map has room for them), and a list of the heads of all the bosses; a click on a head chooses that boss (the
    /// head dims and a check appears on it), a click on a chosen head takes the check away again. The value is how many bosses there are, a bar, and the ids of the chosen bosses in the order they were chosen, separated by commas.
    /// A second slider says how many attacks and pauses every boss has room for at least ("count;attacks|ids"; without the ";attacks" one, as values were saved before it): the more, the fewer bosses the map has room for.
    /// </summary>
    public abstract partial class BossPicker : SettingsItem<string>
    {
        protected override Drawable CreateControl() => new PickerControl();

        /// <summary>How many attacks and pauses a boss can be given room for at least, at most.</summary>
        public const int MOST_ATTACKS = 8;

        /// <summary>The value for <paramref name="count"/> bosses of which these are chosen, each with room for <paramref name="attacks"/> attacks and pauses.</summary>
        public static string ValueOf(int count, IEnumerable<string> ids, int attacks = 1)
            => attacks > 1 ? $"{count};{attacks}|{string.Join(",", ids)}" : $"{count}|{string.Join(",", ids)}";

        /// <summary>How many attacks and pauses every boss has room for at least, as a value says (1 when it does not say).</summary>
        public static int AttacksOf(string? value)
        {
            string text = value ?? string.Empty;
            int bar = text.IndexOf('|');
            string head = bar > 0 ? text[..bar] : text;
            int semicolon = head.IndexOf(';');

            return semicolon > 0 && int.TryParse(head[(semicolon + 1)..], out int attacks) ? Math.Clamp(attacks, 1, MOST_ATTACKS) : 1;
        }

        /// <summary>The ids in a value, in order (every id once).</summary>
        public static List<string> Ids(string? value)
            => (value ?? string.Empty)[((value ?? string.Empty).IndexOf('|') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

        /// <summary>How many bosses a value says there are (at least as many as are chosen in it; what was saved before there was a slider has no number).</summary>
        public static int CountOf(string? value)
        {
            string text = value ?? string.Empty;
            int bar = text.IndexOf('|');
            string head = bar > 0 ? text[..bar] : string.Empty;
            int semicolon = head.IndexOf(';');

            if (semicolon >= 0)
                head = head[..semicolon];

            return Math.Max(int.TryParse(head, out int count) ? count : 1, Ids(text).Count);
        }

        /// <summary>The chosen bosses in a value, in order (ids that are not in the roster any more are left out).</summary>
        public static List<BossInfo> Bosses(string? value)
        {
            var result = new List<BossInfo>();

            foreach (string id in Ids(value))
            {
                var boss = BossRoster.All.FirstOrDefault(b => b.Id == id);

                if (boss != null)
                    result.Add(boss);
            }

            return result;
        }

        public partial class PickerControl : CompositeDrawable, IHasCurrentValue<string>
        {
            private const float head_size = 40f;
            private const float tile_width = 168f;
            private const int columns = 4;

            private readonly BindableWithCurrent<string> current = new BindableWithCurrent<string>(string.Empty);

            public Bindable<string> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            private readonly Tile[] tiles;
            private readonly List<(SpriteText Header, Tile[] Tiles, string Title)> categories = new List<(SpriteText, Tile[], string)>();

            /// <summary>How many bosses there are now: as many can be checked.</summary>
            private int max => Math.Max(CountOf(current.Value), least);

            /// <summary>How few there can be: 1, or 2 or 3 when that many are on the screen at once (the mod's default says).</summary>
            private int least => CountOf(current.Default);

            private bool doubled => least > 1;

            private readonly BindableInt count = new BindableInt(1) { MinValue = 1, MaxValue = 2, Precision = 1 };
            private readonly RoundedSliderBar<int> countSlider;
            private readonly SpriteText countLabel;
            private readonly OsuNumberBox countBox;
            private readonly RoundedButton maxButton;
            private int mostOnMap = BossRoster.All.Count; // (as many bosses as the map has room for: known when the map has been read, so nothing is cut down before that)
            private bool mapRead;

            // (how many attacks and pauses every boss has room for at least: the more, the fewer bosses fit)
            private readonly BindableInt attacks = new BindableInt(1) { MinValue = 1, MaxValue = MOST_ATTACKS, Precision = 1 };
            private readonly RoundedSliderBar<int> attacksSlider;
            private readonly SpriteText attacksLabel;

            /// <summary>The value for <paramref name="count"/> bosses of which these are chosen, with the attacks per boss it has now.</summary>
            private string valueOf(int count, IEnumerable<string> ids) => ValueOf(count, ids, AttacksOf(current.Value));

            /// <summary>(read by the bar preview's background work: how many attacks and pauses per boss the map's room is counted for)</summary>
            internal int AttacksNow => attacks.Value;

            public PickerControl()
            {
                RelativeSizeAxes = Axes.X;
                Height = 286;

                tiles = BossRoster.All.Select(boss => new Tile(boss, this)).ToArray();

                // (two categories: the bosses with a mechanic of their own, and the ones that are still placeholders)
                var content = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                };

                foreach (var (title, modified) in new[] { ("Modified", true), ("Placeholders", false) })
                {
                    var group = tiles.Where(tile => tile.Boss.IsModified == modified).ToArray();
                    var header = new SpriteText
                    {
                        Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                        Colour = new Color4(1f, 1f, 1f, 0.8f),
                        Margin = new MarginPadding { Top = 4 },
                    };

                    categories.Add((header, group, title));
                    content.Add(header);
                    content.Add(new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = group, // (the tiles keep their gaps themselves: four of them fill a row)
                    });
                }

                updateHeaders();

                // (the search takes most of the row; on its right a narrow button that checks random bosses)
                var search = new OsuTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    PlaceholderText = "Search",
                };

                var searchRow = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Padding = new MarginPadding { Right = random_width + 6 },
                    Child = search,
                };

                var randomButton = new RoundedButton
                {
                    Width = random_width,
                    Height = 32,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Text = "Random",
                    Action = pickRandom,
                };

                // (the bosses whose names do not have the text in them are not shown, and the rest move together)
                search.Current.BindValueChanged(text => filter(text.NewValue));

                // under the search: how many bosses there are, as far as the map has room for an attack and a pause of each
                countLabel = new SpriteText { Font = FontUsage.Default.With(size: 14), Y = 38 };

                // (or the number is typed in, on the right of the label)
                countBox = new OsuNumberBox
                {
                    Width = 64,
                    Height = 22,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Y = 33,
                    CommitOnFocusLost = true,
                };

                countBox.OnCommit += (box, _) => typedCount(box.Text);

                // (on its left: as many as the map has room for)
                maxButton = new RoundedButton
                {
                    Width = 52,
                    Height = 22,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -70,
                    Y = 33,
                    Text = "Max",
                    Action = () => count.Value = Math.Max(mostOnMap, least),
                };

                countSlider = new RoundedSliderBar<int>
                {
                    RelativeSizeAxes = Axes.X,
                    Y = 56,
                    Current = count,
                    KeyboardStep = 1,
                };

                // under that: how many attacks and pauses every boss has room for at least
                attacksLabel = new SpriteText { Font = FontUsage.Default.With(size: 14), Y = 84 };

                attacksSlider = new RoundedSliderBar<int>
                {
                    RelativeSizeAxes = Axes.X,
                    Y = 102,
                    Current = attacks,
                    KeyboardStep = 1,
                };

                // under that: the HP bar of the map with the bosses that are checked now (without their names and heads)
                barPreview = new BarPreview(this)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = BarPreview.HEIGHT,
                    Y = bar_y,
                };

                InternalChildren = new Drawable[]
                {
                    searchRow,
                    randomButton,
                    countLabel,
                    countBox,
                    maxButton,
                    countSlider,
                    attacksLabel,
                    attacksSlider,
                    barPreview,
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = bar_y + BarPreview.HEIGHT + 6 },
                        Child = new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = content,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                count.BindValueChanged(changed => setCount(changed.NewValue));
                current.BindValueChanged(_ => refresh(), true);

                // (more attacks per boss: the map has room for fewer of them, counted again)
                attacks.BindValueChanged(changed =>
                {
                    if (changed.NewValue != AttacksOf(current.Value))
                        current.Value = ValueOf(max, Ids(current.Value), changed.NewValue);

                    barPreview.Recount(changed.NewValue);
                });
            }

            /// <summary>How wide the random button is (px): the search field takes the rest of the row.</summary>
            private const float random_width = 120f;

            private const float bar_y = 130f;

            /// <summary>The slider has been moved: what is checked stays, as far as it fits.</summary>
            private void setCount(int wanted)
            {
                if (wanted != max)
                    current.Value = valueOf(wanted, Ids(current.Value).Take(wanted));
            }

            /// <summary>A number has been typed: as far as the map has room for it (what cannot be read leaves the number as it was).</summary>
            private void typedCount(string text)
            {
                if (int.TryParse(text, out int typed))
                    count.Value = Math.Clamp(typed, least, Math.Max(mostOnMap, least));

                countBox.Text = count.Value.ToString();
            }

            /// <summary>The map has been read: this many bosses have an attack and a pause of their own on it (two or three times as many when that many are on the screen at once).</summary>
            private void setMostOnMap(int most)
            {
                mostOnMap = Math.Max(most * least, least);
                mapRead = true;
                refresh();
            }

            private void refreshCount()
            {
                int now = Math.Min(max, mostOnMap);

                // (a map with room for fewer than the mod says: the slider comes down to it)
                if (now != max)
                {
                    current.Value = valueOf(now, Ids(current.Value).Take(now));
                    return;
                }

                // (the top end first: the bottom end may not go past it, and with three bosses at once it starts above where the top end starts)
                count.MaxValue = Math.Max(mostOnMap, least + 1); // (a slider needs two ends: it is not shown when there is only one choice)
                count.MinValue = least;
                count.Value = now;
                countSlider.Alpha = mostOnMap > least ? 1f : 0f;
                maxButton.Alpha = mostOnMap > least ? 1f : 0f;
                countLabel.Text = (mapRead ? $"Bosses: {now} (up to {mostOnMap} on this map)" : $"Bosses: {now}") + $", {Ids(current.Value).Count} chosen";

                if (!countBox.HasFocus)
                    countBox.Text = now.ToString();
            }

            /// <summary>Checks as many bosses as can be checked, at random, among those with a mechanic of their own that the search shows (all of those if it shows none); what was checked before is replaced.</summary>
            private void pickRandom()
            {
                var pool = tiles.Where(tile => tile.Boss.IsModified && tile.Alpha > 0f).Select(tile => tile.Boss.Id).ToList();

                if (pool.Count == 0)
                    pool = tiles.Where(tile => tile.Boss.IsModified).Select(tile => tile.Boss.Id).ToList();

                var picked = new List<string>();

                while (picked.Count < max && pool.Count > 0)
                {
                    int index = Random.Shared.Next(pool.Count);
                    picked.Add(pool[index]);
                    pool.RemoveAt(index);
                }

                current.Value = valueOf(max, picked);
            }

            private void filter(string text)
            {
                text = (text ?? string.Empty).Trim();

                foreach (var tile in tiles)
                    tile.Alpha = text.Length == 0 || tile.Boss.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || tile.Boss.Id.Contains(text.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase) ? 1f : 0f;

                updateHeaders();
            }

            /// <summary>The title of a category shows how many bosses it has (that match the search); a category with none is not shown at all.</summary>
            private void updateHeaders()
            {
                foreach (var (header, group, title) in categories)
                {
                    int shown = group.Count(tile => tile.Alpha > 0f);

                    header.Text = $"{title} ({shown})";
                    header.Alpha = shown > 0 ? 1f : 0f;
                }
            }

            private void refresh()
            {
                attacks.Value = AttacksOf(current.Value);
                attacksLabel.Text = $"Attacks and pauses per boss: at least {attacks.Value}";
                refreshCount();

                var ids = Ids(current.Value);

                bool full = ids.Count >= max;

                foreach (var tile in tiles)
                    tile.SetOrder(ids.IndexOf(tile.Boss.Id), full);

                barPreview.ShowPicked(Bosses(current.Value));
            }

            private readonly BarPreview barPreview;

            /// <summary>
            /// The boss HP bar of the selected map with the bosses that would be played (what is checked; the combo colours fill the rest, as in the mods), as it is when the map starts:
            /// only the bar, without the bosses' names and heads. Nothing is shown when the map cannot be read as a Danmakusu map.
            /// </summary>
            private partial class BarPreview : CompositeDrawable
            {
                public const float HEIGHT = 10f;

                private readonly PickerControl picker;

                [Resolved(CanBeNull = true)]
                private IBindable<WorkingBeatmap>? beatmap { get; set; }

                // (the chosen mods: with Always Boss or Map Generation on, the sections are theirs)
                [Resolved(CanBeNull = true)]
                private IBindable<IReadOnlyList<osu.Game.Rulesets.Mods.Mod>>? mods { get; set; }

                private IBeatmap? playable;
                private SongBands? bands;
                private int recountVersion;

                /// <summary>Counts again how many bosses the map has room for, with <paramref name="attacks"/> attacks and pauses each (off the update thread).</summary>
                public void Recount(int attacks)
                {
                    var map = playable;
                    var song = bands;
                    int version = ++recountVersion;

                    if (map == null)
                        return;

                    Task.Run(() =>
                    {
                        int most;

                        try
                        {
                            most = BulletPlanner.MostBosses(map, () => song, attacks);
                        }
                        catch
                        {
                            return;
                        }

                        Schedule(() =>
                        {
                            if (version == recountVersion)
                                picker.setMostOnMap(most);
                        });
                    });
                }

                /// <summary>The chosen mods that change the map's sections (Always Boss, Map Generation).</summary>
                private osu.Game.Rulesets.Mods.Mod[] sectionMods()
                    => mods?.Value?.Where(mod => mod is DanmakusuModAlwaysBoss or DanmakusuModMapGeneration).ToArray() ?? [];
                private BossInfo[] fromColours = [];
                private IReadOnlyList<BossInfo> picked = [];
                private BossHud? hud;
                private int loadVersion;

                public BarPreview(PickerControl picker)
                {
                    this.picker = picker;
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    beatmap?.BindValueChanged(map => loadMap(map.NewValue), true);

                    // (Always Boss or Map Generation put on or taken off: the map is read again)
                    mods?.BindValueChanged(changed =>
                    {
                        bool had = changed.OldValue?.Any(mod => mod is DanmakusuModAlwaysBoss or DanmakusuModMapGeneration) ?? false;
                        bool has = changed.NewValue?.Any(mod => mod is DanmakusuModAlwaysBoss or DanmakusuModMapGeneration) ?? false;

                        if (had || has)
                            loadMap(beatmap?.Value);
                    });
                }

                public void ShowPicked(IReadOnlyList<BossInfo> bosses)
                {
                    picked = bosses;
                    rebuild();
                }

                /// <summary>Converts the map (off the update thread: it can take a moment) and reads its colours.</summary>
                private void loadMap(WorkingBeatmap? working)
                {
                    int version = ++loadVersion;
                    playable = null;
                    rebuild();

                    if (working == null)
                        return;

                    var withMods = sectionMods();
                    int attacks = picker.AttacksNow;

                    Task.Run(() =>
                    {
                        IBeatmap? converted = null;
                        SongBands? song = null;
                        BossInfo[] colours = [];
                        int most = 1;

                        try
                        {
                            converted = working.GetPlayableBeatmap(new DanmakusuRuleset().RulesetInfo, withMods);

                            // (the song's bands are only read when the map has too few sections of its own: then the bar shows the sections chosen with them, as in the game)
                            bool read = false;
                            BulletPlanner.Sections(converted, () =>
                            {
                                read = true;
                                return song = SongBands.From(working);
                            });

                            if (!read)
                                song = null;

                            SongBands? known = song;
                            most = BulletPlanner.MostBosses(converted, () => known, attacks);

                            var mapColours = MapColours.Read(working);

                            if (mapColours.Count == 0 && BackgroundColour.Of(working) is Color4 background)
                                mapColours = [background];

                            colours = BossMatcher.Match(mapColours);
                        }
                        catch
                        {
                            converted = null; // (not a map this ruleset can play: no preview)
                        }

                        Schedule(() =>
                        {
                            if (version != loadVersion)
                                return;

                            playable = converted;
                            bands = song;
                            fromColours = colours;
                            picker.setMostOnMap(most);
                            rebuild();
                        });
                    });
                }

                private void rebuild()
                {
                    if (hud != null)
                    {
                        RemoveInternal(hud, true);
                        hud = null;
                    }

                    if (playable == null || fromColours.Length == 0 || playable.HitObjects.Count == 0)
                        return;

                    var bosses = DanmakusuModBossSelection.ChooseFrom(picked, fromColours, picker.max);
                    var song = bands;
                    var periods = BulletPlanner.BarPeriods(playable, bosses.Length, picker.doubled, () => song, picker.least > 2);
                    double mapStart = playable.HitObjects.Min(h => h.StartTime);
                    double mapEnd = playable.HitObjects.Max(h => h.GetEndTime());

                    AddInternal(hud = new BossHud(periods, bosses, mapStart, mapEnd, showNames: false)
                    {
                        BarFraction = 1f,
                        MaxBarWidth = float.MaxValue,
                    });
                }

                protected override void Update()
                {
                    base.Update();

                    if (hud == null)
                        return;

                    // (as it is when the map starts: full)
                    hud.SetBounds(Vector2.Zero, new Vector2(DrawWidth, HEIGHT));
                    hud.UpdateState(double.NegativeInfinity);
                }
            }

            internal void Toggle(BossInfo boss)
            {
                var ids = Ids(current.Value);

                if (!ids.Remove(boss.Id))
                {
                    // (all the places are taken: the new one takes the place of the one that was checked first)
                    while (ids.Count >= max)
                        ids.RemoveAt(0);

                    ids.Add(boss.Id);
                }

                current.Value = valueOf(max, ids);
            }

            private partial class Tile : Container, IHasTooltip
            {
                public readonly BossInfo Boss;

                private readonly PickerControl picker;
                private readonly Box background;
                private readonly Sprite head;
                private readonly SpriteIcon check;
                private readonly SpriteText number;
                private readonly FillFlowContainer name;
                private readonly Container frame;

                public LocalisableString TooltipText => Boss.Name;

                public Tile(BossInfo boss, PickerControl picker)
                {
                    Boss = boss;
                    this.picker = picker;

                    // (four in a row: a tile is a quarter of the list's width, with a gap of 4 px between two)
                    RelativeSizeAxes = Axes.X;
                    Width = 1f / columns;
                    Height = head_size + 12; // (two rows of name and the stars under them)
                    Padding = new MarginPadding(2);

                    Child = frame = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 4,
                        BorderThickness = 2,
                        BorderColour = new Color4(1f, 1f, 1f, 0f),
                    };

                    frame.Children = new Drawable[]
                    {
                        background = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f },
                        head = new Sprite { Size = new Vector2(head_size), FillMode = FillMode.Fit, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        // (the name in two rows, split between words where both rows come out about as long, and the boss's difficulty under it, on the same left edge)
                        name = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = head_size + 6,
                            Children = nameRows(boss.Name).Select(row => (Drawable)new SpriteText
                            {
                                Text = row,
                                Font = FontUsage.Default.With(size: 14),
                                Truncate = true,
                                MaxWidth = tile_width - head_size - 34, // (set to the tile's width in Update)
                            }).Append(new DifficultyStars(BossDifficulty.Of(boss), 9) { Margin = new MarginPadding { Top = 2 } }).ToArray(),
                        },
                        check = new SpriteIcon
                        {
                            Size = new Vector2(18),
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.Centre,
                            X = -14,
                            Icon = FontAwesome.Solid.Check,
                            Colour = Color4.White,
                            Alpha = 0f,
                        },
                        number = new SpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomRight,
                            X = head_size,
                            Font = FontUsage.Default.With(size: 12),
                            Shadow = true,
                            Margin = new MarginPadding(2),
                            Alpha = 0f,
                        },
                    };
                }

                /// <summary>A name in two rows (one when it is a single word): split at the space that makes the longer row shortest.</summary>
                private static string[] nameRows(string name)
                {
                    int best = -1, bestLength = int.MaxValue;

                    for (int i = name.IndexOf(' '); i >= 0; i = name.IndexOf(' ', i + 1))
                    {
                        int longer = Math.Max(i, name.Length - i - 1);

                        if (longer < bestLength)
                        {
                            best = i;
                            bestLength = longer;
                        }
                    }

                    return best < 0 ? new[] { name } : new[] { name[..best], name[(best + 1)..] };
                }

                [BackgroundDependencyLoader]
                private void load(IRenderer renderer)
                {
                    head.Texture = BossCharacter.LoadResource(renderer, $"Bosses.Heads.{Boss.Id}.png");
                }

                private bool chosen;
                private bool hovered;

                /// <summary>
                /// Which of the chosen bosses this one is (0 = the first), or -1 when it is not chosen. A chosen head is lit up (a bright frame and a check); when every place is taken
                /// (<paramref name="full"/>) the heads that are not chosen are dimmed.
                /// </summary>
                public void SetOrder(int index, bool full)
                {
                    chosen = index >= 0;

                    bool dim = full && !chosen;

                    head.Alpha = dim ? 0.3f : 1f;
                    name.Alpha = dim ? 0.45f : 1f;
                    background.Alpha = chosen ? 0.85f : 0.5f;
                    check.Alpha = chosen ? 1f : 0f;
                    number.Text = (index + 1).ToString();
                    number.Alpha = chosen && picker.max > 1 ? 1f : 0f;

                    updateBorder();
                }

                protected override void Update()
                {
                    base.Update();

                    float room = Math.Max(DrawWidth - 4 - head_size - 34, 20);

                    foreach (var row in name.Children.OfType<SpriteText>())
                        row.MaxWidth = room;
                }

                private void updateBorder()
                {
                    frame.BorderColour = chosen ? new Color4(0.55f, 0.85f, 1f, 1f) : new Color4(1f, 1f, 1f, hovered ? 0.7f : 0f);
                }

                protected override bool OnClick(ClickEvent e)
                {
                    picker.Toggle(Boss);
                    return true;
                }

                protected override bool OnHover(HoverEvent e)
                {
                    hovered = true;
                    updateBorder();
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e)
                {
                    hovered = false;
                    updateBorder();
                    base.OnHoverLost(e);
                }
            }
        }
    }

    /// <summary>The list of the boss selection mods.</summary>
    public partial class BossCountPicker : BossPicker
    {
    }
}
