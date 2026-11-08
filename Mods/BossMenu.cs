using System;
using System.Collections.Generic;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.UI;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The menu that opens when a map with a boss selection mod starts: the map is paused behind it, the player checks the bosses (see <see cref="BossPicker"/>) and presses Start.
    /// What is checked is saved in the mod (so it is saved with the score, and a replay does not ask again).
    /// </summary>
    public partial class BossMenu : Container
    {
        private readonly HUDOverlay hud;
        private readonly DanmakusuPlayfield playfield;
        private readonly IBossSelection selection;
        private GameplayClockContainer? clock;
        private bool confirmed;
        private readonly Bindable<string> chosen;
        private readonly Action<ValueChangedEvent<string>> valueChanged;
        private readonly BossInfoPanel info;
        private readonly BossPreview preview;

        /// <summary>Opens the menu in the HUD of a play (nothing happens in a replay: its bosses are saved in it).</summary>
        public static void Attach(HUDOverlay hud, DrawableRuleset<OsuHitObject>? drawableRuleset, IBossSelection selection, Bindable<string> chosen)
        {
            if (drawableRuleset == null || drawableRuleset.HasReplayLoaded.Value || drawableRuleset.Playfield is not DanmakusuPlayfield playfield)
                return;

            hud.Add(new BossMenu(hud, playfield, selection, chosen));
        }

        private BossMenu(HUDOverlay hud, DanmakusuPlayfield playfield, IBossSelection selection, Bindable<string> chosen)
        {
            this.hud = hud;
            this.playfield = playfield;
            this.selection = selection;

            this.chosen = chosen;
            valueChanged = _ => refreshBosses();

            RelativeSizeAxes = Axes.Both;

            var picker = new BossPicker.PickerControl
            {
                RelativeSizeAxes = Axes.Both,
                Height = 1f,
                Current = chosen,
            };

            // the left half: the heads to choose from, how the player moves (under them) and the Start button; the right half: what the chosen boss is (above) and how his attacks look (below, as big as half of the screen)
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.92f },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = selection.Triple ? "Choose the bosses (three at a time)" : selection.Double ? "Choose the bosses (two at a time)" : "Choose the bosses",
                            Font = FontUsage.Default.With(size: 34),
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Y = 24,
                        },
                        new TextFlowContainer(s => s.Font = FontUsage.Default.With(size: 16))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding { Horizontal = 40 },
                            TextAnchor = Anchor.TopCentre,
                            Colour = new Color4(1f, 1f, 1f, 0.7f),
                            Y = 72,
                            Text = "The slider says how many bosses there are. Click heads to check them (the first one is the first boss), click a checked head to take the check away. What is not checked: the combo colours choose.",
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 40, Top = 130, Bottom = controls_bottom + controls_height + 14 },
                            Child = picker,
                        },
                        // (how the player moves: under the heads, above the Start button)
                        new ControlsPanel
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = controls_height,
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Y = -controls_bottom,
                            Padding = new MarginPadding { Horizontal = 40 },
                        },
                        new StartButton(confirm)
                        {
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Y = -30,
                        },
                    },
                },
                info = new BossInfoPanel
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.5f),
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Padding = new MarginPadding { Right = 12, Top = 12, Bottom = 6, Left = 6 },
                },
                // (the bottom half on the right: the attack preview)
                preview = new BossPreview
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.5f),
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Padding = new MarginPadding { Right = 12, Bottom = 12, Top = 6, Left = 6 },
                },
            };
        }

        /// <summary>The controls under the heads: how tall they are, and how far above the bottom (room for the Start button under them).</summary>
        private const float controls_height = 210;

        private const float controls_bottom = 92;

        /// <summary>The two ways to move, side by side: a click chooses one (it is kept, see <see cref="Controls"/>); under them a slider for how fast the keyboard moves the character.</summary>
        private partial class ControlsPanel : Container
        {
            private readonly ControlChoice cursor, keyboard;
            private readonly BindableDouble speed = new BindableDouble(Controls.KeyboardSpeed) { MinValue = Controls.MIN_SPEED, MaxValue = Controls.MAX_SPEED, Precision = 0.05 };
            private readonly SpriteText speedLabel;

            public ControlsPanel()
            {
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "Controls",
                                Font = FontUsage.Default.With(size: 18, weight: "Bold"),
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    cursor = new ControlChoice("Cursor only", "The character is where the cursor is. Hold key 1 or key 2 (osu!standard's keys) to freeze it; press the other one while holding to move the freeze to the cursor.", () => choose(ControlMode.Cursor)) { Width = 0.49f },
                                    keyboard = new ControlChoice("Keyboard (WASD)", "W A S D or the arrows move, Shift at half the speed. Space teleports to the cursor; while it is held the character stays on the cursor. No freeze keys.", () => choose(ControlMode.Keyboard)) { Width = 0.49f },
                                },
                            },
                            speedLabel = new SpriteText { Font = FontUsage.Default.With(size: 15) },
                            new RoundedSliderBar<double>
                            {
                                RelativeSizeAxes = Axes.X,
                                Current = speed,
                                KeyboardStep = 0.05f,
                            },
                        },
                    },
                };

                speed.BindValueChanged(changed =>
                {
                    Controls.KeyboardSpeed = changed.NewValue;
                    speedLabel.Text = $"WASD speed: {changed.NewValue:0.00}x";
                }, true);

                refresh();
            }

            private void choose(ControlMode mode)
            {
                Controls.Mode = mode;
                refresh();
            }

            private void refresh()
            {
                cursor.Selected = Controls.Mode == ControlMode.Cursor;
                keyboard.Selected = Controls.Mode == ControlMode.Keyboard;
            }
        }

        private partial class ControlChoice : Container
        {
            private readonly Action action;
            private readonly Box background;
            private bool selected;

            public bool Selected
            {
                set
                {
                    selected = value;
                    BorderColour = value ? new Color4(0.55f, 0.85f, 1f, 1f) : new Color4(1f, 1f, 1f, 0.15f);
                    background.Colour = value ? new Color4(0.12f, 0.3f, 0.55f, 1f) : new Color4(0.08f, 0.08f, 0.1f, 1f);
                }
            }

            public ControlChoice(string title, string text, Action action)
            {
                this.action = action;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = 6;
                BorderThickness = 2;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(10),
                        Spacing = new Vector2(0, 4),
                        Children = new Drawable[]
                        {
                            new SpriteText { Text = title, Font = FontUsage.Default.With(size: 18, weight: "Bold") },
                            new TextFlowContainer(s => s.Font = FontUsage.Default.With(size: 14))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Colour = new Color4(1f, 1f, 1f, 0.75f),
                                Text = text,
                            },
                        },
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.FadeColour(new Color4(0.14f, 0.14f, 0.18f, 1f), 100);

                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                Selected = selected;
                base.OnHoverLost(e);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            chosen.ValueChanged += valueChanged;
            refreshBosses();
        }

        protected override void Dispose(bool isDisposing)
        {
            chosen.ValueChanged -= valueChanged;
            base.Dispose(isDisposing);
        }

        /// <summary>The bosses that would be played with what is checked now (nothing checked: the ones the combo colours choose): their information and their attacks are shown.</summary>
        private void refreshBosses()
        {
            var bosses = selection.Choose(playfield.BossesFromColours) ?? playfield.BossesFromColours;

            info.ShowBosses(bosses, selection.Double, selection.Triple);
            preview.ShowBosses(bosses, selection.Triple);
        }

        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnScroll(ScrollEvent e) => true;

        protected override void Update()
        {
            base.Update();

            if (confirmed)
                return;

            // the map waits behind the menu: the gameplay clock is started by the player when it opens, and stopped again here
            if (clock == null)
            {
                Drawable? parent = Parent;

                while (parent != null && parent is not GameplayClockContainer)
                    parent = parent.Parent;

                clock = parent as GameplayClockContainer;
            }

            if (clock != null && !clock.IsPaused.Value)
                clock.Stop();

            hideSkip();

            // (the HUD does not take clicks while it is hidden)
            if (!hud.ShowHud.Disabled && !hud.ShowHud.Value)
                hud.ShowHud.Value = true;
        }

        private void confirm()
        {
            if (confirmed)
                return;

            confirmed = true;

            // (what is not checked is filled in by the combo colours, as everywhere else)
            playfield.ChooseBosses(selection.Choose(playfield.BossesFromColours) ?? playfield.BossesFromColours);

            clock?.Start();
            showSkip();

            this.FadeOut(250).Expire();
        }

        // ---- the skip button ----

        // The skip button belongs to the play screen, not to the HUD: it is found in the play screen's fields (by its type, as osu! does not hand it out). While the menu is open it is hidden,
        // and as it is not there it does not take the skip key either; when the player presses Start it comes back as it was.

        private readonly List<(SkipOverlay Overlay, float Alpha, bool AlwaysPresent)> hiddenSkips = new List<(SkipOverlay, float, bool)>();
        private bool skipsLooked;

        private void hideSkip()
        {
            if (!skipsLooked)
            {
                Drawable? parent = Parent;

                while (parent != null && parent is not Player)
                    parent = parent.Parent;

                if (parent is not Player player)
                    return;

                skipsLooked = true;

                for (var type = player.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (typeof(SkipOverlay).IsAssignableFrom(field.FieldType) && field.GetValue(player) is SkipOverlay overlay && hiddenSkips.TrueForAll(s => s.Overlay != overlay))
                            hiddenSkips.Add((overlay, overlay.Alpha, overlay.AlwaysPresent));
                    }
                }
            }

            foreach (var (overlay, _, _) in hiddenSkips)
            {
                overlay.AlwaysPresent = false;
                overlay.Alpha = 0;
            }
        }

        private void showSkip()
        {
            foreach (var (overlay, alpha, alwaysPresent) in hiddenSkips)
            {
                overlay.Alpha = alpha;
                overlay.AlwaysPresent = alwaysPresent;
            }

            hiddenSkips.Clear();
        }

        private partial class StartButton : Container
        {
            private readonly Action action;
            private readonly Box background;

            public StartButton(Action action)
            {
                this.action = action;

                Size = new Vector2(220, 46);
                Masking = true;
                CornerRadius = 8;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.22f, 0.55f, 0.95f, 1f) },
                    new SpriteText
                    {
                        Text = "Start",
                        Font = FontUsage.Default.With(size: 24, weight: "Bold"),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(new Color4(0.4f, 0.7f, 1f, 1f), 100);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(new Color4(0.22f, 0.55f, 0.95f, 1f), 100);
                base.OnHoverLost(e);
            }
        }
    }
}
