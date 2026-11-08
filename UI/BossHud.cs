using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The top of the screen while a map has bosses: each boss's name and head, and one health bar for all of them.
    /// The bar is as long as the whole map, from its first object to its last (the warnings are not counted); the parts before the first boss and after the last one are dark gray.
    /// It drains (from the right, the first section is the right-most) as the map goes, and is empty exactly when the map ends. Every section is a segment of its own, in the
    /// colour of its boss, with a mark where it starts. It is there from the start of the map and fades out after the last boss has exploded.
    /// </summary>
    public partial class BossHud : Container
    {
        private const float head_size = 18f;
        private const float name_size = 11f;
        private const float bar_height = 5f;
        private const float margin_top = 6f;
        private const float tick_extra = 2f;

        /// <summary>Where the bar is (below the names and heads; at the top when there are none).</summary>
        private readonly float bar_y;

        /// <summary>How much of the screen's width the bar takes, and at most how wide it is (the preview in the boss list fills its width).</summary>
        public float BarFraction { get; init; } = 0.46f;

        public float MaxBarWidth { get; init; } = 420f;
        private static readonly Color4 gray_section = new Color4(0.52f, 0.52f, 0.56f, 1f); // (the parts of the map without a boss: plain gray, clearly seen on the dark screen)

        /// <summary>The HUD fades out this long (ms) after the last section ended, once the explosion is over.</summary>
        private const double fade_out_delay = 1100;

        private const double fade_out_ms = 600;

        private readonly BossPeriod[] periods;
        private readonly BossInfo[] slotBosses;
        private readonly double totalTime;
        private readonly double timeStart;

        /// <summary>The bar from the first attack to the end of the last one, cut where a boss comes or goes: who is on in each piece (slot -1 = nobody: a section of the map without a boss, dark gray; two = two stripes).</summary>
        private readonly (double From, double To, int SlotA, int SlotB, int SlotC)[] segments;

        private Vector2 boundsMin, boundsMax;
        private bool hasBounds;
        private float barWidth, barLeft;

        /// <summary>Where the top centre of the HUD is on the real screen (field coordinates).</summary>
        private Vector2 top;

        private float mirrorAngle;
        private Vector2 mirrorCentre;

        /// <summary>
        /// The field is turned by <paramref name="degrees"/> round <paramref name="centre"/> (Seija's mirrored screen): the HUD is turned back and moved so that it
        /// stays where it is on the screen, upright.
        /// </summary>
        public void SetMirror(float degrees, Vector2 centre)
        {
            mirrorAngle = degrees;
            mirrorCentre = centre;
        }

        private readonly Container bar;
        private readonly Box[] fillTop, fillBottom, backTop, backBottom;
        private readonly Box?[] fillMiddle, backMiddle; // (only where three bosses are on)
        private readonly Box[] ticks;
        private readonly Container[] nameRows;
        private readonly Container[] headFrames;
        private readonly Sprite[] heads;
        private readonly SpriteText[] names;

        /// <summary>When the map's last object ends (the bar is empty then, and fades after it).</summary>
        private readonly double mapEnd;

        /// <param name="showNames">false: only the bar, without the names and heads of the bosses (the preview in the boss list).</param>
        public BossHud(BossPeriod[] periods, BossInfo[] slotBosses, double mapStart = double.NaN, double mapEnd = double.NaN, bool showNames = true)
        {
            this.periods = periods;
            this.slotBosses = slotBosses;
            bar_y = showNames ? margin_top + head_size + 3f : tick_extra;

            AlwaysPresent = true;
            Alpha = 0;
            Origin = Anchor.TopCentre; // (placed by its top centre: see UpdateState)

            // the pieces of the bar: between every two moments that a boss comes or goes
            var moments = new SortedSet<double>();

            foreach (var period in periods)
            {
                if (period.End > period.ShootStart)
                {
                    moments.Add(period.ShootStart);
                    moments.Add(period.End);
                }
            }

            // (the sections of the map before the first boss comes and after the last one has gone are part of the bar too, dark gray)
            if (moments.Count > 0)
            {
                if (!double.IsNaN(mapStart) && mapStart < moments.Min)
                    moments.Add(mapStart);

                if (!double.IsNaN(mapEnd) && mapEnd > moments.Max)
                    moments.Add(mapEnd);

                this.mapEnd = Math.Max(moments.Max, double.IsNaN(mapEnd) ? 0 : mapEnd);
            }

            var edges = new List<double>(moments);
            var pieces = new List<(double From, double To, int SlotA, int SlotB, int SlotC)>();

            for (int i = 0; i + 1 < edges.Count; i++)
            {
                int first = -1, second = -1, third = -1;

                foreach (var period in periods)
                {
                    if (period.ShootStart > edges[i] + 0.5 || period.End < edges[i + 1] - 0.5)
                        continue;

                    if (first < 0 || first == period.BossSlot)
                        first = period.BossSlot;
                    else if (second < 0 || second == period.BossSlot)
                        second = period.BossSlot;
                    else if (third < 0)
                        third = period.BossSlot;
                }

                pieces.Add((edges[i], edges[i + 1], first, second, third));
            }

            segments = pieces.ToArray();
            timeStart = edges.Count > 0 ? edges[0] : 0;
            totalTime = edges.Count > 0 ? edges[^1] - edges[0] : 0;

            var children = new List<Drawable>();
            children.Add(bar = new Container { Height = bar_height });

            backTop = new Box[segments.Length];
            backBottom = new Box[segments.Length];
            fillTop = new Box[segments.Length];
            fillBottom = new Box[segments.Length];
            backMiddle = new Box?[segments.Length];
            fillMiddle = new Box?[segments.Length];
            ticks = new Box[segments.Length + 1];

            for (int i = 0; i < segments.Length; i++)
            {
                Color4 upper = segments[i].SlotA < 0 ? gray_section : bossColour(segments[i].SlotA);
                Color4 lower = segments[i].SlotC >= 0 ? bossColour(segments[i].SlotC) : segments[i].SlotB >= 0 ? bossColour(segments[i].SlotB) : upper; // (two bosses: two lines, one over the other; three: three)
                float line = bar_height / (segments[i].SlotC >= 0 ? 3f : 2f);

                bar.Add(backTop[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Colour = Interpolate(upper, Color4.Black, 0.6f), Alpha = 0.7f });
                bar.Add(backBottom[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Y = bar_height - line, Colour = Interpolate(lower, Color4.Black, 0.6f), Alpha = 0.7f });
                bar.Add(fillTop[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Colour = Interpolate(upper, Color4.White, segments[i].SlotA < 0 ? 0.05f : 0.3f) });
                bar.Add(fillBottom[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Y = bar_height - line, Colour = Interpolate(lower, Color4.White, segments[i].SlotA < 0 ? 0.05f : 0.3f) });

                if (segments[i].SlotC >= 0)
                {
                    Color4 between = bossColour(segments[i].SlotB);

                    bar.Add(backMiddle[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Y = line, Colour = Interpolate(between, Color4.Black, 0.6f), Alpha = 0.7f });
                    bar.Add(fillMiddle[i] = new Box { RelativeSizeAxes = Axes.None, Height = line, Y = line, Colour = Interpolate(between, Color4.White, 0.3f) });
                }
            }

            for (int i = 0; i < ticks.Length; i++)
            {
                int slot = i < segments.Length ? segments[i].SlotA : segments.Length > 0 ? segments[^1].SlotA : 0;
                Color4 colour = slot < 0 ? gray_section : bossColour(slot);
                children.Add(ticks[i] = new Box { Width = 1.5f, Height = bar_height + tick_extra * 2, Colour = Interpolate(colour, Color4.White, 0.75f), Alpha = 0.95f });
            }

            int rows = showNames ? slotBosses.Length : 0;
            nameRows = new Container[rows];
            headFrames = new Container[rows];
            heads = new Sprite[rows];
            names = new SpriteText[rows];

            for (int slot = 0; slot < rows; slot++)
            {
                bool right = slot == 1;
                Color4 colour = bossColour(slot);

                var head = heads[slot] = new Sprite { Size = new Vector2(head_size), FillMode = FillMode.Fit };

                var headFrame = headFrames[slot] = new Container
                {
                    Size = new Vector2(head_size),
                    Masking = true,
                    CornerRadius = 3f,
                    BorderThickness = 1.2f,
                    BorderColour = Interpolate(colour, Color4.White, 0.5f),
                    Anchor = right ? Anchor.CentreRight : Anchor.CentreLeft,
                    Origin = right ? Anchor.CentreRight : Anchor.CentreLeft,
                    Children = new Drawable[] { new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f }, head },
                };

                var text = names[slot] = new SpriteText
                {
                    Text = slotBosses[slot].Name,
                    Font = FontUsage.Default.With(size: name_size),
                    Shadow = true,
                    Anchor = right ? Anchor.CentreRight : Anchor.CentreLeft,
                    Origin = right ? Anchor.CentreRight : Anchor.CentreLeft,
                    X = right ? -(head_size + 4f) : head_size + 4f,
                };

                children.Add(nameRows[slot] = new Container
                {
                    Height = head_size,
                    Y = margin_top,
                    Children = new Drawable[] { headFrame, text },
                });
            }

            InternalChildren = children;
        }

        private Color4 bossColour(int slot)
            => slot >= 0 && slot < slotBosses.Length && slotBosses[slot].Colours.Length > 0 ? slotBosses[slot].Colours[Math.Min(2, slotBosses[slot].Colours.Length - 1)] : new Color4(1f, 0.4f, 0.4f, 1f);

        public static Color4 Interpolate(Color4 from, Color4 to, float amount)
            => new Color4(from.R + (to.R - from.R) * amount, from.G + (to.G - from.G) * amount, from.B + (to.B - from.B) * amount, 1f);

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            for (int slot = 0; slot < heads.Length; slot++)
                heads[slot].Texture = BossCharacter.LoadResource(renderer, $"Bosses.Heads.{slotBosses[slot].Id}.png");
        }

        /// <summary>The real screen in the field's coordinates; the HUD sits at the top of it.</summary>
        public void SetBounds(Vector2 min, Vector2 max)
        {
            if (hasBounds && Vector2.Distance(min, boundsMin) < 0.5f && Vector2.Distance(max, boundsMax) < 0.5f)
                return;

            hasBounds = true;
            boundsMin = min;
            boundsMax = max;

            float width = max.X - min.X;
            barWidth = Math.Clamp(width * BarFraction, Math.Min(120f, width), MaxBarWidth);
            barLeft = (min.X + max.X) / 2 - barWidth / 2;

            top = new Vector2(barLeft + barWidth / 2, min.Y);
            Width = barWidth;
            bar.Width = barWidth;
            bar.Y = bar_y;

            foreach (var row in nameRows)
                row.Width = barWidth;

            for (int i = 0; i < segments.Length; i++)
            {
                // the first section is the right-most one
                double from = totalTime > 0 ? (segments[i].From - timeStart) / totalTime : 0;
                double to = totalTime > 0 ? (segments[i].To - timeStart) / totalTime : 0;
                float x = (float)(barWidth * (1 - to));
                float w = (float)(barWidth * (to - from));

                backTop[i].X = backBottom[i].X = x;
                backTop[i].Width = backBottom[i].Width = w;
                fillTop[i].X = fillBottom[i].X = x;
                fillTop[i].Width = fillBottom[i].Width = w;

                if (backMiddle[i] is Box back && fillMiddle[i] is Box fill)
                {
                    back.X = fill.X = x;
                    back.Width = fill.Width = w;
                }
            }

            for (int i = 0; i < ticks.Length; i++)
            {
                double at = totalTime > 0 ? ((i < segments.Length ? segments[i].From : segments[^1].To) - timeStart) / totalTime : 0;
                ticks[i].X = (float)(barWidth * (1 - at)) - ticks[i].Width / 2;
                ticks[i].Y = bar_y - tick_extra;
            }
        }

        /// <summary>The health bar and the names at <paramref name="now"/>.</summary>
        public void UpdateState(double now)
        {
            if (periods.Length == 0 || !hasBounds)
            {
                Alpha = 0;
                return;
            }

            // (the field is turned by mirrorAngle round mirrorCentre: put the HUD where that turn brings it to `top`, turned back by the same angle)
            float radians = mirrorAngle * MathF.PI / 180f;
            Vector2 offset = top - mirrorCentre;
            Position = mirrorCentre + new Vector2(offset.X * MathF.Cos(radians) + offset.Y * MathF.Sin(radians), -offset.X * MathF.Sin(radians) + offset.Y * MathF.Cos(radians));
            Rotation = -mirrorAngle;

            double lastEnd = Math.Max(periods[^1].End, mapEnd);

            foreach (var period in periods)
                lastEnd = Math.Max(lastEnd, period.End);

            double sinceEnd = now - (lastEnd + fade_out_delay);
            Alpha = sinceEnd <= 0 ? 1f : (float)Math.Clamp(1 - sinceEnd / fade_out_ms, 0, 1);

            // the bosses that are on the screen right now (from when they appear, with the warning, until they are gone)
            bool[] onScreen = new bool[Math.Max(nameRows.Length, 2)];
            bool anyOn = false;

            for (int i = 0; i < segments.Length; i++)
            {
                double length = Math.Max(segments[i].To - segments[i].From, 0);
                double consumed = Math.Clamp(now - segments[i].From, 0, length);
                float remaining = length > 0 ? (float)(1 - consumed / length) : 0f;

                fillTop[i].Width = fillBottom[i].Width = backTop[i].Width * remaining;

                if (fillMiddle[i] is Box middleFill)
                    middleFill.Width = backTop[i].Width * remaining;
            }

            foreach (var period in periods)
            {
                if (now >= period.Start && now <= period.End && period.BossSlot >= 0 && period.BossSlot < onScreen.Length)
                {
                    onScreen[period.BossSlot] = true;
                    anyOn = true;
                }
            }

            // (more than two bosses: only those on the screen are shown, the first boss of a section on the left, the second on the right and the third in the middle. While one takes over from another both are on the screen for a moment:
            // each side has one name, the newest, so that the names never lie over each other)
            if (nameRows.Length > 2)
            {
                BossPeriod? leftPeriod = null, rightPeriod = null, middlePeriod = null;

                foreach (var period in periods)
                {
                    if (now < period.Start || now > period.End || period.BossSlot < 0 || period.BossSlot >= nameRows.Length)
                        continue;

                    if (period.Rank == 0 && (leftPeriod == null || period.Start >= leftPeriod.Start))
                        leftPeriod = period;
                    else if (period.Rank == 1 && (rightPeriod == null || period.Start >= rightPeriod.Start))
                        rightPeriod = period;
                    else if (period.Rank == 2 && (middlePeriod == null || period.Start >= middlePeriod.Start))
                        middlePeriod = period;
                }

                for (int slot = 0; slot < nameRows.Length; slot++)
                {
                    bool onLeft = leftPeriod?.BossSlot == slot, onRight = !onLeft && rightPeriod?.BossSlot == slot, inMiddle = !onLeft && !onRight && middlePeriod?.BossSlot == slot;

                    nameRows[slot].Alpha = onLeft || onRight || inMiddle ? 1f : 0f;

                    if (!onLeft && !onRight && !inMiddle)
                        continue;

                    var side = onLeft ? Anchor.CentreLeft : onRight ? Anchor.CentreRight : Anchor.Centre;

                    headFrames[slot].Anchor = side;
                    headFrames[slot].Origin = inMiddle ? Anchor.CentreRight : side;
                    headFrames[slot].X = inMiddle ? -2f : 0f;
                    names[slot].Anchor = side;
                    names[slot].Origin = inMiddle ? Anchor.CentreLeft : side;
                    names[slot].X = onLeft ? head_size + 4f : onRight ? -(head_size + 4f) : 2f;
                    names[slot].Colour = Interpolate(bossColour(slot), Color4.White, 0.55f);
                }

                return;
            }

            // (with two bosses the one on the screen is bright, bigger and in her own colour; the other one is dim and small. With one boss, or none on the screen, nothing is highlighted)
            for (int slot = 0; slot < nameRows.Length; slot++)
            {
                bool two = nameRows.Length > 1;
                bool highlighted = two && onScreen[slot]; // (both are highlighted when both are on the screen)
                bool dimmed = two && anyOn && !onScreen[slot];

                nameRows[slot].Alpha = dimmed ? 0.4f : 1f;
                names[slot].Colour = highlighted ? Interpolate(bossColour(slot), Color4.White, 0.55f) : dimmed ? new Color4(0.75f, 0.75f, 0.75f, 1f) : Color4.White;
                names[slot].Scale = new Vector2(highlighted ? 1.2f : dimmed ? 0.9f : 1f);
                headFrames[slot].Scale = new Vector2(highlighted ? 1.12f : 1f);
                headFrames[slot].BorderColour = highlighted ? Color4.White : Interpolate(bossColour(slot), Color4.White, 0.5f);
            }
        }
    }
}
