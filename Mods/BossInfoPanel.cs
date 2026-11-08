using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The information about the chosen boss in the boss menu: his name, his whole picture (the big one, Resources/Bosses/Portraits) and what he does. For two bosses: both names, both pictures, one text that has both of them in it, and (if their
    /// mechanics fight over the same thing) a line about that.
    /// </summary>
    public partial class BossInfoPanel : Container
    {
        private readonly Container content;
        private readonly Dictionary<string, Task<Texture?>> portraits = new Dictionary<string, Task<Texture?>>();
        private IRenderer? renderer;
        private BossInfo[]? pending;
        private bool pendingTogether;
        private int shownVersion;

        /// <summary>With more bosses than this the title only says how many there are.</summary>
        private const int most_names = 4;

        public BossInfoPanel()
        {
            Masking = true;
            CornerRadius = 6;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(1f, 1f, 1f, 0.06f) },
                content = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(14) },
            };
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            this.renderer = renderer;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (pending != null)
                ShowBosses(pending, pendingTogether, pendingTriple);
        }

        private bool pendingTriple;

        /// <param name="bosses">The bosses, in the order they come in the map.</param>
        /// <param name="together">Two of them are on the screen in every section.</param>
        /// <param name="triple">Three of them are, then.</param>
        public void ShowBosses(BossInfo[] bosses, bool together, bool triple = false)
        {
            pending = bosses;
            pendingTogether = together;
            pendingTriple = triple;

            if (renderer == null)
                return;

            content.Clear();
            shownVersion++;

            if (bosses.Length == 0)
                return;

            string title = bosses.Length > most_names ? $"{bosses.Length} bosses" : string.Join(" + ", Array.ConvertAll(bosses, boss => boss.Name));

            // the pictures on the left, in a grid that fills the space, each with the place it has in the order; the title above the text on the right
            float picturesWidth = bosses.Length > 1 ? 0.5f : 0.36f;
            int columns = (int)Math.Ceiling(Math.Sqrt(bosses.Length));
            int rows = (int)Math.Ceiling(bosses.Length / (float)columns);

            // (every cell has its own place: a row that flows can lose its last cell to rounding, and then the last rows are cut off)
            var pictures = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = picturesWidth,
            };

            for (int i = 0; i < bosses.Length; i++)
            {
                var cell = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Both,
                    X = (i % columns) / (float)columns,
                    Y = (i / columns) / (float)rows,
                    Width = 1f / columns,
                    Height = 1f / rows,
                    Child = new SpriteText
                    {
                        Text = (i + 1).ToString(),
                        Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                        Shadow = true,
                        Alpha = bosses.Length > 1 ? 1f : 0f,
                    },
                };

                pictures.Add(cell);
                showPortrait(cell, bosses[i]);
            }

            var text = new OsuTextFlowContainer(s => s.Font = FontUsage.Default.With(size: 15))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };

            var parts = BossDescriptions.Combined(bosses).Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    text.NewParagraph();

                // (the difficulty is a number in the text: it shows as the boss's stars, in a row of its own)
                if (parts[i].StartsWith(BossDescriptions.DIFFICULTY, StringComparison.Ordinal) && int.TryParse(parts[i][BossDescriptions.DIFFICULTY.Length..], out int rating))
                {
                    text.AddArbitraryDrawable(new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = BossDescriptions.DIFFICULTY.Trim(),
                                Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new DifficultyStars(rating, 13)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                        },
                    });

                    continue;
                }

                int colon = parts[i].IndexOf(": ", StringComparison.Ordinal);

                if (colon > 0)
                {
                    text.AddText(parts[i][..(colon + 2)], s => s.Font = FontUsage.Default.With(size: 15, weight: "Bold"));
                    text.AddText(parts[i][(colon + 2)..]);
                }
                else
                    text.AddText(parts[i], s => s.Font = FontUsage.Default.With(size: 16, weight: "Bold")); // (a boss's name, over the two parts of her text)
            }

            string? clash = BossDescriptions.Clash(bosses);

            if (clash != null)
            {
                text.NewParagraph();
                text.AddText("Clash: ", s =>
                {
                    s.Font = FontUsage.Default.With(size: 15, weight: "Bold");
                    s.Colour = new Color4(1f, 0.7f, 0.3f, 1f);
                });
                text.AddText(clash.Replace('\n', ' '), s => s.Colour = new Color4(1f, 0.82f, 0.55f, 1f));
            }

            if (together)
            {
                text.NewParagraph();
                text.AddText(triple ? "Three bosses are on the screen in every section: the numbers of bullets and beams are divided by three." : "Two bosses are on the screen in every section: the numbers of bullets and beams are halved.", s => s.Colour = new Color4(1f, 1f, 1f, 0.6f));
            }

            content.AddRange(new Drawable[]
            {
                pictures,
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new InfoColumn(picturesWidth, title, text, bosses.Length > 1),
                },
            });
        }

        /// <summary>
        /// A boss's big picture goes under the number in <paramref name="cell"/> as soon as it has been read, off the update thread: a whole grid of them would hold the menu up, and when the bosses have changed by then it is not wanted any more.
        /// (A boss without one has her sprite.)
        /// </summary>
        private void showPortrait(Container cell, BossInfo boss)
        {
            int version = shownVersion;

            if (!portraits.TryGetValue(boss.Id, out var portrait))
                portraits[boss.Id] = portrait = Task.Run(() => BossCharacter.LoadResource(renderer!, $"Bosses.Portraits.{boss.Id}.webp") ?? BossCharacter.LoadResource(renderer!, $"Bosses.Sprites.{boss.Id}.png"));

            void show(Texture? texture) => cell.Add(new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fit,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Depth = 1,
                Texture = texture,
            });

            if (portrait.IsCompletedSuccessfully)
                show(portrait.Result);
            else
            {
                portrait.ContinueWith(done => Schedule(() =>
                {
                    if (version == shownVersion)
                        show(done.Result);
                }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        /// <summary>The names, on one row: the text is as big as it can be (up to <c>size</c>) and still fit the width.</summary>
        private partial class TitleText : Container
        {
            private readonly SpriteText text;
            private readonly float size;

            public TitleText(string title, float size)
            {
                this.size = size;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Child = text = new SpriteText
                {
                    Text = title,
                    Font = FontUsage.Default.With(size: size, weight: "Bold"),
                };
            }

            protected override void Update()
            {
                base.Update();

                // (a long pair of names gets smaller instead of taking a second row)
                if (DrawWidth > 1 && text.DrawWidth > 1)
                {
                    float fitted = Math.Clamp(size * DrawWidth / (text.DrawWidth / text.Font.Size * size), 10f, size);

                    if (Math.Abs(fitted - text.Font.Size) > 0.25f)
                        text.Font = FontUsage.Default.With(size: fitted, weight: "Bold");
                }
            }
        }

        /// <summary>The right part of the panel: the title, and under it the text in a scroll.</summary>
        private partial class InfoColumn : Container
        {
            public InfoColumn(float picturesWidth, string title, TextFlowContainer text, bool two)
            {
                RelativeSizeAxes = Axes.Both;

                // (the pictures take their share of the width on the left; this column starts after it, with some room between)
                Anchor = Anchor.TopRight;
                Origin = Anchor.TopRight;
                Width = 1f - picturesWidth;

                // (the title is one row, as tall as the title; the text gets the rest)
                InternalChild = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding { Left = 12, Bottom = 8 },
                                Child = new TitleText(title, two ? 19 : 24),
                            },
                        },
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Left = 12 },
                                Child = new OsuScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = text,
                                },
                            },
                        },
                    },
                };
            }
        }
    }
}
