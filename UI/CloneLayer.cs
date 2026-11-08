using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Copies of the map's circles and sliders that some bosses make, shown in a combo colour of their own and without sound:
    ///  - Doremy: every circle and slider that starts in one of her attacks, at the same time, mirrored through the middle of the playfield (x and y), dark blue;
    ///  - Sagume: as soon as one of her attacks ends it is played again backwards: what started t ms before the end starts again t ms after it, where it was, white (a slider that ended
    ///    at the far end of its path goes the other way along it). The approach circles play as always.
    /// They are real osu! circles and sliders (they look exactly like the originals) and are hit when they are due, like the originals, but nothing of theirs counts: they are not in the
    /// playfield's own list of objects. They hurt like the originals (see <see cref="BulletPlanner.PlanCircleZones"/>); what they shoot is planned with the map.
    /// </summary>
    public partial class CloneLayer : Container
    {
        private readonly List<(OsuHitObject Copy, Color4 Colour)> copies;
        private readonly List<(DrawableOsuHitObject Drawable, Color4 Colour)> shown = new List<(DrawableOsuHitObject, Color4)>();
        private int next;
        private double lastTime = double.NegativeInfinity;

        /// <summary>How big (radius) the white bullet is that is drawn on a copy of Doremy's instead of its combo number.</summary>
        private readonly float markRadius;

        /// <summary>The white bullets on Doremy's copied circles (and slider heads), above the copies.</summary>
        private readonly Container marks;

        private readonly Dictionary<DrawableHitCircle, Drawable> markOf = new Dictionary<DrawableHitCircle, Drawable>();

        public CloneLayer(List<(OsuHitObject Copy, Color4 Colour)> copies, float markRadius)
        {
            this.copies = copies;
            this.markRadius = markRadius;
            RelativeSizeAxes = Axes.Both;
            AddInternal(marks = new Container { RelativeSizeAxes = Axes.Both, Depth = -1 });
        }

        /// <summary>A white bullet (white body, light grey rim) of <see cref="markRadius"/>: what Doremy's copies show instead of their combo numbers.</summary>
        private Drawable makeMark() => new CircularContainer
        {
            Origin = Anchor.Centre,
            Size = new Vector2(2f * markRadius),
            Masking = true,
            BorderThickness = markRadius * 0.35f,
            BorderColour = new Color4(0.82f, 0.84f, 0.9f, 1f),
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
        };

        /// <summary>
        /// A circle (or slider head) of one of Doremy's copies: its combo number is hidden, and a white bullet sits where the number was, as there and as faded as the circle is.
        /// </summary>
        private void markCircle(DrawableHitCircle circle)
        {
            foreach (var text in circle.ChildrenOfType<SpriteText>())
            {
                if (text.Alpha > 0)
                    text.Alpha = 0;
            }

            if (!markOf.TryGetValue(circle, out var mark))
                marks.Add(markOf[circle] = mark = makeMark());

            mark.Position = marks.ToLocalSpace(circle.ScreenSpaceDrawQuad.Centre);
            mark.Alpha = circle.IsPresent ? circle.DrawColourInfo.Colour.MaxAlpha : 0f;
        }

        private void unmark(DrawableOsuHitObject drawable)
        {
            foreach (var circle in markOf.Keys.Where(c => c == drawable || (drawable is DrawableSlider s && s.NestedHitObjects.Contains(c))).ToList())
            {
                marks.Remove(markOf[circle], true);
                markOf.Remove(circle);
            }
        }

        /// <summary>The copies that the bosses of <paramref name="periods"/> make of the map's circles and sliders, in the order they start (empty when none of them makes any).</summary>
        public static List<(OsuHitObject Copy, Color4 Colour)> MakeCopies(IBeatmap beatmap, BossPeriod[] periods)
        {
            var copies = new List<(OsuHitObject, Color4)>();

            foreach (var period in periods)
            {
                var mechanic = period.Boss?.Mechanic;

                if (mechanic != BossMechanic.Dream && mechanic != BossMechanic.Rewind)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end <= start)
                        continue;

                    foreach (var hitObject in beatmap.HitObjects)
                    {
                        if (hitObject.StartTime < start || hitObject.StartTime >= end || hitObject is not (HitCircle or Slider))
                            continue;

                        OsuHitObject copy = mechanic == BossMechanic.Dream ? mirrored((OsuHitObject)hitObject) : replayed((OsuHitObject)hitObject, end);
                        copy.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                        copies.Add((copy, mechanic == BossMechanic.Dream ? DanmakusuBalance.DOREMY_COPY_COLOUR : DanmakusuBalance.SAGUME_COPY_COLOUR));
                    }
                }
            }

            copies.Sort((a, b) => a.Item1.StartTime.CompareTo(b.Item1.StartTime));
            return copies;
        }

        // ---- Doremy: mirrored ----

        private static OsuHitObject mirrored(OsuHitObject original)
        {
            Vector2 position = OsuPlayfield.BASE_SIZE - original.StackedPosition;

            if (original is not Slider slider)
                return circle(original, original.StartTime, position);

            // (the path is mirrored too: every control point the other way from the start)
            var path = new SliderPath(slider.Path.ControlPoints.Select(point => new PathControlPoint(-point.Position, point.Type)).ToArray(), slider.Path.ExpectedDistance.Value);
            return sliderLike(slider, slider.StartTime, position, path);
        }

        // ---- Sagume: played backwards after the attack ----

        private static OsuHitObject replayed(OsuHitObject original, double attackEnd)
        {
            if (original is not Slider slider)
                return circle(original, 2 * attackEnd - original.StartTime, original.StackedPosition);

            // (what ended latest comes first; a slider that ended at the far end of its path starts there and goes back along it, one that came back to its start goes the same way)
            double start = 2 * attackEnd - slider.EndTime;

            if (slider.RepeatCount % 2 == 1)
                return sliderLike(slider, start, slider.StackedPosition, new SliderPath(slider.Path.ControlPoints.Select(point => new PathControlPoint(point.Position, point.Type)).ToArray(), slider.Path.ExpectedDistance.Value));

            Vector2 far = slider.Path.PositionAt(1);
            int steps = Math.Clamp((int)Math.Ceiling(slider.Path.Distance / 4.0), 2, 400);
            var points = new PathControlPoint[steps + 1];

            for (int i = 0; i <= steps; i++)
                points[i] = new PathControlPoint(slider.Path.PositionAt(1 - i / (double)steps) - far, i == 0 ? PathType.LINEAR : null);

            return sliderLike(slider, start, slider.StackedPosition + far, new SliderPath(points, slider.Path.Distance));
        }

        // ---- building the copies ----

        private static HitCircle circle(OsuHitObject original, double startTime, Vector2 position) => new HitCircle
        {
            StartTime = startTime,
            Position = position,
            NewCombo = original.NewCombo,
            ComboOffset = original.ComboOffset,
            IndexInCurrentCombo = original.IndexInCurrentCombo,
            ComboIndex = original.ComboIndex,
            ComboIndexWithOffsets = original.ComboIndexWithOffsets,
            Samples = new List<HitSampleInfo>(), // (the copies make no sound)
        };

        private static Slider sliderLike(Slider original, double startTime, Vector2 position, SliderPath path) => new Slider
        {
            StartTime = startTime,
            Position = position,
            NewCombo = original.NewCombo,
            ComboOffset = original.ComboOffset,
            IndexInCurrentCombo = original.IndexInCurrentCombo,
            ComboIndex = original.ComboIndex,
            ComboIndexWithOffsets = original.ComboIndexWithOffsets,
            RepeatCount = original.RepeatCount,
            SliderVelocityMultiplier = original.SliderVelocityMultiplier,
            TickDistanceMultiplier = original.TickDistanceMultiplier,
            ClassicSliderBehaviour = original.ClassicSliderBehaviour,
            Path = path,
            Samples = new List<HitSampleInfo>(),
            NodeSamples = Enumerable.Range(0, original.RepeatCount + 2).Select(_ => (IList<HitSampleInfo>)new List<HitSampleInfo>()).ToList(),
        };

        // ---- showing them ----

        protected override void Update()
        {
            base.Update();

            double time = Time.Current;

            // time went back: start again from what is on the screen now
            if (time < lastTime - 1)
            {
                foreach (var (drawable, _) in shown)
                {
                    unmark(drawable);
                    Remove(drawable, true);
                }

                shown.Clear();
                next = 0;

                while (next < copies.Count && copies[next].Copy.GetEndTime() + 1000 < time)
                    next++;
            }

            lastTime = time;

            while (next < copies.Count && copies[next].Copy.StartTime - copies[next].Copy.TimePreempt - 200 <= time)
            {
                var (copy, colour) = copies[next++];

                DrawableOsuHitObject drawable = copy switch
                {
                    Slider slider => new DrawableSlider(slider),
                    HitCircle circle => new DrawableHitCircle(circle),
                    _ => throw new InvalidOperationException(),
                };

                // (clicking on a copy does nothing)
                if (drawable is DrawableHitCircle circleDrawable)
                    circleDrawable.OnLoadComplete += _ => circleDrawable.HitArea.CanBeHit = () => false;

                Add(drawable);
                shown.Add((drawable, colour));
            }

            for (int i = shown.Count - 1; i >= 0; i--)
            {
                var (drawable, colour) = shown[i];

                if (!drawable.IsLoaded)
                    continue;

                recolour(drawable, colour);
                bool doremy = colour == DanmakusuBalance.DOREMY_COPY_COLOUR;

                // hit when due, like the originals (a slider's head, ticks, repeats and tail in order; the slider itself then judges itself)
                if (drawable is DrawableSlider sliderDrawable)
                {
                    foreach (var nested in sliderDrawable.NestedHitObjects.OfType<DrawableOsuHitObject>().OrderBy(nested => nested.HitObject.StartTime))
                    {
                        recolour(nested, colour);

                        if (doremy && nested is DrawableHitCircle head)
                            markCircle(head);

                        if (!nested.Judged && time >= nested.HitObject.StartTime)
                            nested.HitForcefully();
                    }
                }
                else
                {
                    if (doremy && drawable is DrawableHitCircle circle)
                        markCircle(circle);

                    if (!drawable.Judged && time >= drawable.HitObject.StartTime)
                        drawable.HitForcefully();
                }

                if (drawable.Judged && time > drawable.HitObject.GetEndTime() + 1000)
                {
                    unmark(drawable);
                    Remove(drawable, true);
                    shown.RemoveAt(i);
                }
            }
        }

        /// <summary>Every part of a copy has the copy's combo colour (the skin would give it the map's).</summary>
        private static void recolour(DrawableOsuHitObject drawable, Color4 colour)
        {
            if (drawable.AccentColour.Value != colour)
                drawable.AccentColour.Value = colour;
        }
    }
}
