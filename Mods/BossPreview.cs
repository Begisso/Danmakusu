using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>
    /// The attack preview of the boss menu: the chosen boss (or both, if two are chosen) attacks a little map of circles and sliders (see <see cref="BossPreviewMap"/>) in a window of its own, over and over.
    /// It is a real bullet field (with its sounds), is not connected to the health and the score of the play, and has its own clock that goes round.
    /// </summary>
    public partial class BossPreview : Container
    {
        private readonly Container window;
        private readonly Container playfield;
        private Container? current;
        private BossInfo[]? pending;
        private ScheduledDelegate? rebuildDelay;
        private CancellationTokenSource? cancel;

        public BossPreview()
        {
            Masking = true;
            CornerRadius = 6;
            BorderThickness = 2;
            BorderColour = new Color4(1f, 1f, 1f, 0.25f);

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.03f, 0.03f, 0.05f, 1f) },
                window = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Child = playfield = new Container
                    {
                        Size = new Vector2(512, 384),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                },
                new SpriteText
                {
                    Text = "Attack preview",
                    Font = FontUsage.Default.With(size: 14),
                    Colour = new Color4(1f, 1f, 1f, 0.7f),
                    Shadow = true,
                    Margin = new MarginPadding(6),
                },
            };
        }

        /// <summary>Plays the attacks of these bosses (a short moment after the last call: clicking through the heads must not plan a map for each).</summary>
        public void ShowBosses(BossInfo[] bosses, bool triple = false)
        {
            pending = bosses;
            threeAtOnce = triple;

            if (!IsLoaded)
                return;

            rebuildDelay?.Cancel();
            rebuildDelay = Scheduler.AddDelayed(() => rebuild(bosses), 250);
        }

        private bool threeAtOnce;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (pending != null)
                rebuild(pending);
        }

        protected override void Update()
        {
            base.Update();

            // the playfield is as big on this screen as the real one is on the big one (the real one is 0.8 of the height of the screen)
            playfield.Scale = new Vector2(0.8f * window.DrawHeight / 384f);
        }

        private void rebuild(BossInfo[] bosses)
        {
            cancel?.Cancel();
            cancel = new CancellationTokenSource();

            Container fresh;

            try
            {
                fresh = createContent(bosses);
            }
            catch (Exception e)
            {
                Logger.Error(e, "The attack preview could not be made.");
                return;
            }

            LoadComponentAsync(fresh, loaded =>
            {
                current?.Expire();
                current = loaded;
                playfield.Add(loaded);
            }, cancel.Token);
        }

        private Container createContent(BossInfo[] bosses)
        {
            var map = BossPreviewMap.Create();
            var emitters = BulletPlanner.Plan(map, out var lasers, out var periods, bosses, bosses.Length > 1, alwaysTriple: threeAtOnce);
            var colours = BossColours.Of(bosses);

            // (the preview starts at the warning of the first attack, without the quiet lead-in, and starts there again every time)
            double warningAt = BossPreviewMap.LENGTH;

            foreach (var period in periods)
                warningAt = Math.Min(warningAt, period.Start);

            var clock = new FramedClock(new LoopClock(BossPreviewMap.LENGTH, Math.Max(0, warningAt)));
            var difficulty = map.Difficulty;

            // (the character dodges by itself: it goes round in the lower half, and the bosses stand opposite it)
            BulletField? fieldMade = null;

            Vector2? characterAt()
            {
                double seconds = clock.CurrentTime / 1000.0;
                var place = new Vector2(256 + 170 * MathF.Sin((float)(seconds * 0.8)), 270 + 60 * MathF.Sin((float)(seconds * 1.7 + 1)));

                // (while Seija turns the screen the character is where it is without the turn, as with the mouse of the real game: the screen position of that place in the turned field)
                return fieldMade != null ? fieldMade.ToScreenSpace(place) : playfield.ToScreenSpace(place);
            }

            var field = new BulletField(
                emitters,
                lasers,
                periods,
                DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize),
                BulletPlanner.PlanSliderZones(map),
                BulletPlanner.PlanCircleZones(map),
                DanmakusuBalance.DamagePerBullet(difficulty.DrainRate),
                DanmakusuBalance.DamagePerSliderTick(difficulty.DrainRate),
                characterAt)
            {
                RelativeSizeAxes = Axes.Both,
                Preview = true,
                ScreenBounds = window,
                PaletteOverride = colours,
                FallbackSongEndTime = BossPreviewMap.LENGTH,
                Depth = 1,
            };

            fieldMade = field;

            return new TurningContent(field)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Clock = clock,
                Children = new Drawable[]
                {
                    field,
                    new PreviewObjects(map, colours),
                },
            };
        }

        /// <summary>The preview as a whole: it is turned half way round while Seija mirrors the screen, as the playfield of the real game is (the field and the circles turn together).</summary>
        private partial class TurningContent : Container
        {
            private readonly BulletField field;

            public TurningContent(BulletField field)
            {
                this.field = field;
            }

            protected override void Update()
            {
                base.Update();

                Rotation = field.MirrorAngleAt(Time.Current);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            cancel?.Cancel();
        }

        /// <summary>A clock that goes from 0 to a length and starts again.</summary>
        private sealed class LoopClock : IClock
        {
            private readonly Stopwatch watch = new Stopwatch(); // (it starts when the preview is first looked at, so the loading time is not part of it)
            private readonly double length;
            private readonly double from;

            /// <summary>The clock plays from <paramref name="from"/> to <paramref name="length"/> and starts again at <paramref name="from"/>.</summary>
            public LoopClock(double length, double from = 0)
            {
                this.length = length;
                this.from = Math.Clamp(from, 0, length - 1);
            }

            public double CurrentTime
            {
                get
                {
                    if (!watch.IsRunning)
                        watch.Start();

                    return from + watch.Elapsed.TotalMilliseconds % (length - from);
                }
            }

            public double Rate => 1;

            public bool IsRunning => true;
        }

        /// <summary>The circles, sliders and spinner of the little map, drawn simply (a circle, an approach ring, a slider body with its ball), in the colours of the bosses.</summary>
        private partial class PreviewObjects : Container
        {
            private const double preempt = 600;
            private const double fade_out = 250;

            public PreviewObjects(IBeatmap map, Color4[] colours)
            {
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;

                foreach (var hitObject in map.HitObjects)
                {
                    if (hitObject is not OsuHitObject osuObject)
                        continue;

                    Color4 colour = colours.Length > 0 ? colours[Math.Abs(osuObject.ComboIndexWithOffsets) % colours.Length] : Color4.White;

                    switch (hitObject)
                    {
                        case Slider slider:
                            Add(new SliderPiece(slider, colour));
                            break;

                        case HitCircle circle:
                            Add(new CirclePiece(circle, colour));
                            break;

                        case Spinner spinner:
                            Add(new SpinnerPiece(spinner));
                            break;
                    }
                }
            }

            /// <summary>0..1: how visible something is that appears <see cref="preempt"/> before its start and goes <see cref="fade_out"/> after its end.</summary>
            private static float visibility(double now, double start, double end)
            {
                if (now < start - preempt || now > end + fade_out)
                    return 0f;

                if (now < start)
                    return (float)Math.Clamp((now - (start - preempt)) / (preempt * 0.66), 0, 1);

                if (now > end)
                    return (float)(1 - (now - end) / fade_out);

                return 1f;
            }

            private partial class CirclePiece : Container
            {
                private readonly double start;
                private readonly Container approach;

                public CirclePiece(HitCircle circle, Color4 colour)
                {
                    start = circle.StartTime;

                    float radius = (float)circle.Radius;

                    AlwaysPresent = true;
                    Origin = Anchor.Centre;
                    Position = circle.StackedPosition;
                    Size = new Vector2(radius * 2);

                    Children = new Drawable[]
                    {
                        new Circle { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0.85f },
                        new CircularContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            BorderThickness = 3,
                            BorderColour = Color4.White,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        },
                        approach = new CircularContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Origin = Anchor.Centre,
                            Anchor = Anchor.Centre,
                            Masking = true,
                            BorderThickness = 2,
                            BorderColour = colour,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        },
                    };
                }

                protected override void Update()
                {
                    base.Update();

                    double now = Time.Current;

                    Alpha = visibility(now, start, start);
                    approach.Alpha = now < start ? 1f : 0f;
                    approach.Scale = new Vector2(now < start ? 1f + 2.5f * (float)((start - now) / preempt) : 1f);
                    Scale = new Vector2(now > start ? 1f + 0.25f * (float)Math.Clamp((now - start) / fade_out, 0, 1) : 1f);
                }
            }

            private partial class SliderPiece : Container
            {
                private readonly Slider slider;
                private readonly Path body;
                private readonly Container ball;
                private readonly CirclePiece head;

                public SliderPiece(Slider slider, Color4 colour)
                {
                    this.slider = slider;

                    RelativeSizeAxes = Axes.Both;
                    AlwaysPresent = true;

                    float radius = (float)slider.Radius;
                    var vertices = new List<Vector2>(slider.Path.CalculatedPath);

                    InternalChildren = new Drawable[]
                    {
                        body = new Path
                        {
                            PathRadius = radius,
                            Position = slider.StackedPosition,
                            Vertices = vertices,
                            Colour = colour,
                            AlwaysPresent = true,
                        },
                        head = new CirclePiece(slider.HeadCircle, colour),
                        ball = new Container
                        {
                            AlwaysPresent = true,
                            Origin = Anchor.Centre,
                            Size = new Vector2(radius * 1.8f),
                            Children = new Drawable[]
                            {
                                new Circle { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0.35f },
                                new CircularContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    BorderThickness = 3,
                                    BorderColour = Color4.White,
                                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                                },
                            },
                        },
                    };
                }

                protected override void Update()
                {
                    base.Update();

                    double now = Time.Current;
                    float seen = visibility(now, slider.StartTime, slider.EndTime);

                    body.Alpha = 0.5f * seen;
                    ball.Alpha = now >= slider.StartTime && now <= slider.EndTime ? 1f : 0f;

                    if (ball.Alpha > 0f)
                    {
                        double span = Math.Max(slider.SpanDuration, 1);
                        double along = (now - slider.StartTime) / span;
                        int index = (int)Math.Floor(along);
                        double fraction = along - index;

                        if (index % 2 == 1)
                            fraction = 1 - fraction;

                        ball.Position = slider.StackedPosition + slider.Path.PositionAt(Math.Clamp(fraction, 0, 1));
                    }
                }
            }

            private partial class SpinnerPiece : Container
            {
                private readonly Spinner spinner;

                public SpinnerPiece(Spinner spinner)
                {
                    this.spinner = spinner;

                    AlwaysPresent = true;
                    Origin = Anchor.Centre;
                    Position = new Vector2(256, 192);
                    Size = new Vector2(180);

                    Child = new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = 4,
                        BorderColour = new Color4(1f, 1f, 1f, 0.6f),
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    };
                }

                protected override void Update()
                {
                    base.Update();

                    double now = Time.Current;

                    Alpha = visibility(now, spinner.StartTime, spinner.EndTime) * 0.6f;
                    Scale = new Vector2(1f - 0.4f * (float)Math.Clamp((now - spinner.StartTime) / Math.Max(spinner.Duration, 1), 0, 1));
                }
            }
        }
    }
}
