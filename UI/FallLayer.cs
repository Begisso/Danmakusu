using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Urumi: what has been hit in her section falls on the player, shown as what it is. Each is a real osu! circle or slider, as Doremy's copies are (skin, combo number and all), with a grey combo colour.
    /// It stands on a clock of its own, stopped just before the object is due: so it stays whole, a slider with all its body, and is never judged (nothing of it counts). Only the place and the shade change, from
    /// what <see cref="BulletField.FallingObjects"/> says, under the bullets so that they hide none. What is on the screen follows from the time alone, so going back in time needs nothing.
    /// </summary>
    public partial class FallLayer : Container
    {
        private sealed class Shown
        {
            public Container Holder = null!;
            public DrawableOsuHitObject Drawable = null!;
        }

        private readonly BulletField field;
        private readonly List<(OsuHitObject Source, double Began, Vector2 By)> falling = new List<(OsuHitObject, double, Vector2)>();
        private readonly Dictionary<OsuHitObject, Shown> shown = new Dictionary<OsuHitObject, Shown>();
        private readonly HashSet<OsuHitObject> stillFalling = new HashSet<OsuHitObject>();

        public FallLayer(BulletField field)
        {
            this.field = field;
            RelativeSizeAxes = Axes.Both;
        }

        private Shown show(OsuHitObject source)
        {
            DrawableOsuHitObject drawable = source switch
            {
                Slider slider => new DrawableSlider(slider),
                HitCircle circle => new DrawableHitCircle(circle),
                _ => throw new InvalidOperationException(),
            };

            var holder = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Clock = new FramedClock(new ManualClock { CurrentTime = source.StartTime - 1 }),
                Child = drawable,
            };

            Add(holder);
            return new Shown { Holder = holder, Drawable = drawable };
        }

        protected override void Update()
        {
            base.Update();

            double time = Time.Current;

            falling.Clear();
            stillFalling.Clear();
            field.FallingObjects(time, falling);

            foreach (var (source, began, by) in falling)
            {
                stillFalling.Add(source);

                if (!shown.TryGetValue(source, out var item))
                    shown[source] = item = show(source);

                // (darker and darker as it falls, like Zanmu's bullets)
                float u = (float)Math.Clamp((time - began) / DanmakusuBalance.ZANMU_FALL_DARKEN_MS, 0, 1);
                float keep = 1f - DanmakusuBalance.ZANMU_FALL_SHADE * u * u * (3f - 2f * u);

                item.Holder.Position = by;
                item.Holder.Colour = new Color4(keep, keep, keep, 1f);
            }

            foreach (var source in shown.Keys.Where(source => !stillFalling.Contains(source)).ToList())
            {
                Remove(shown[source].Holder, true);
                shown.Remove(source);
            }
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            foreach (var item in shown.Values)
            {
                if (!item.Drawable.IsLoaded)
                    continue;

                settle(item.Drawable);

                if (item.Drawable is DrawableSlider slider)
                {
                    foreach (var nested in slider.NestedHitObjects.OfType<DrawableOsuHitObject>())
                        settle(nested);
                }
            }
        }

        /// <summary>Every part has the grey combo colour (the skin would give it the map's); a circle (or a slider's head) shows no approach circle and cannot be clicked.</summary>
        private static void settle(DrawableOsuHitObject drawable)
        {
            if (drawable.AccentColour.Value != DanmakusuBalance.URUMI_FALL_COLOUR)
                drawable.AccentColour.Value = DanmakusuBalance.URUMI_FALL_COLOUR;

            if (drawable is DrawableHitCircle circle)
            {
                circle.ApproachCircle.Alpha = 0f;
                circle.HitArea.CanBeHit = () => false;
            }
        }
    }
}
