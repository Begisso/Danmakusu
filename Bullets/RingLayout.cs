using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Where the bullets of a ring start and which way they fly. The ring is an outline (circle, square, triangle or five-pointed star,
    /// farthest corner at distance 1) that grows around the ring's centre: every bullet flies straight outwards from the centre through its
    /// point of the outline, and the speed of a bullet is proportional to how far its point is from the centre, so the whole outline
    /// keeps its shape while it expands.
    /// </summary>
    public static class RingLayout
    {
        private const float star_inner_ratio = 0.381966f; // the regular pentagram

        /// <summary>The corners of the outline, starting on the +x axis (before the ring is rotated), going round.</summary>
        public static Vector2[] Corners(RingShape shape)
        {
            switch (shape)
            {
                case RingShape.Square:
                    return regular(4);

                case RingShape.Triangle:
                    return regular(3);

                case RingShape.Pentagon:
                    return regular(5);

                case RingShape.Hexagon:
                    return regular(6);

                case RingShape.Star:
                {
                    var corners = new Vector2[10];

                    for (int i = 0; i < corners.Length; i++)
                    {
                        float angle = i * MathF.Tau / corners.Length;
                        float length = i % 2 == 0 ? 1f : star_inner_ratio;
                        corners[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
                    }

                    return corners;
                }

                case RingShape.Flower:
                {
                    // five petals, each a rounded lobe with a notch in the middle of its tip: valley, shoulder, notch, shoulder, (next petal's valley)...
                    var corners = new Vector2[20];
                    int next = 0;

                    for (int k = 0; k < 5; k++)
                    {
                        float middle = k * MathF.Tau / 5;
                        corners[next++] = polar(middle - MathF.Tau / 10, flower_valley);
                        corners[next++] = polar(middle - flower_shoulder_angle, 1f);
                        corners[next++] = polar(middle, flower_notch);
                        corners[next++] = polar(middle + flower_shoulder_angle, 1f);
                    }

                    return corners;
                }

                case RingShape.Ellipse:
                {
                    // a polygon with many corners, so that the bullets are shared out along the outline by its length
                    var corners = new Vector2[ellipse_corners];

                    for (int i = 0; i < corners.Length; i++)
                    {
                        float angle = i * MathF.Tau / corners.Length;
                        corners[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle) * ellipse_flatness);
                    }

                    return corners;
                }

                default:
                    return [];
            }
        }

        /// <summary>
        /// (Noroiko's rings, 2026-11-25) <paramref name="count"/> bullets along the strokes of 呪, upright whatever the ring's turn (a ring that aims at the player still turns it): every stroke gets bullets
        /// by its length, its two ends among them, so that every stroke shows. Distances are shares of the farthest point; nothing is nearer the middle than <see cref="glyph_nearest"/> (those would
        /// crawl and stay long).
        /// </summary>
        private static Vector2[] glyph(int count, out float[]? scales)
        {
            var strokes = CurseGlyph.STROKES;
            float farthest = 0f;

            foreach (var stroke in strokes)
            {
                foreach (var point in stroke)
                    farthest = Math.Max(farthest, point.Length);
            }

            // (since 2026-11-28 every corner of every stroke is a bullet, in the order the stroke goes, and the rest go between them by length: beams drawn through a stroke's bullets are the stroke itself -
            // her rings are drawn in beams, CurseAct; before, every stroke had its two ends and the rest by its length, and a corner fell between two bullets)
            int[][] between = GlyphBetween(count);
            var points = new List<Vector2>(count);

            for (int s = 0; s < strokes.Length; s++)
            {
                var stroke = strokes[s];

                for (int j = 0; j + 1 < stroke.Length && points.Count < count; j++)
                {
                    points.Add(stroke[j] / farthest);

                    for (int k = 1; k <= between[s][j] && points.Count < count; k++)
                        points.Add(Vector2.Lerp(stroke[j], stroke[j + 1], k / (float)(between[s][j] + 1)) / farthest);
                }

                if (points.Count < count)
                    points.Add(stroke[^1] / farthest);
            }

            var directions = new Vector2[count];
            scales = new float[count];

            for (int i = 0; i < count; i++)
            {
                Vector2 p = i < points.Count ? points[i] : new Vector2(1f, 0f);
                float length = p.Length;
                directions[i] = length > 1e-6f ? p / length : new Vector2(1f, 0f);
                scales[i] = Math.Max(length, glyph_nearest);
            }

            return directions;
        }

        /// <summary>
        /// How many bullets of a ring of <paramref name="count"/> laid out as 呪 go between every two corners of every stroke (<see cref="CurseGlyph.STROKES"/>, stroke by stroke, piece by piece): every corner
        /// has one, and the rest are shared out where the pieces are longest for the bullets they have.
        /// </summary>
        public static int[][] GlyphBetween(int count)
        {
            var strokes = CurseGlyph.STROKES;
            var between = new int[strokes.Length][];
            int corners = 0;

            for (int s = 0; s < strokes.Length; s++)
            {
                between[s] = new int[Math.Max(strokes[s].Length - 1, 0)];
                corners += strokes[s].Length;
            }

            for (int n = corners; n < count; n++)
            {
                int bestS = 0, bestJ = 0;
                float most = float.MinValue;

                for (int s = 0; s < strokes.Length; s++)
                {
                    for (int j = 0; j < between[s].Length; j++)
                    {
                        float room = Vector2.Distance(strokes[s][j], strokes[s][j + 1]) / (between[s][j] + 1);

                        if (room > most)
                        {
                            most = room;
                            bestS = s;
                            bestJ = j;
                        }
                    }
                }

                between[bestS][bestJ]++;
            }

            return between;
        }

        /// <summary>How many corners the strokes of 呪 have together (<see cref="CurseGlyph.STROKES"/>): a ring of as many bullets has one on each and no more (Noroiko's, drawn in beams).</summary>
        public static int GlyphCorners
        {
            get
            {
                int corners = 0;

                foreach (var stroke in CurseGlyph.STROKES)
                    corners += stroke.Length;

                return corners;
            }
        }

        /// <summary>The bullets of every stroke of a ring of <paramref name="count"/> laid out as 呪: the first one and how many, in the order the stroke goes (a stroke with too few left has fewer).</summary>
        public static (int First, int Count)[] GlyphRuns(int count)
        {
            var strokes = CurseGlyph.STROKES;
            int[][] between = GlyphBetween(count);
            var runs = new (int, int)[strokes.Length];
            int next = 0;

            for (int s = 0; s < strokes.Length; s++)
            {
                int length = strokes[s].Length;

                foreach (int b in between[s])
                    length += b;

                length = Math.Clamp(count - next, 0, length);
                runs[s] = (next, length);
                next += length;
            }

            return runs;
        }

        private const float glyph_nearest = 0.15f;

        private const int ellipse_corners = 48;
        private const float ellipse_flatness = 0.5f; // (height / width)
        private const float flower_valley = 0.42f; // how far from the middle the gap between two petals is
        private const float flower_notch = 0.8f; // ... and the notch of a petal's tip
        private const float flower_shoulder_angle = 0.2f; // radians either side of the notch where the tip is widest

        private static Vector2 polar(float angle, float length) => new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;

        private static Vector2[] regular(int sides)
        {
            var corners = new Vector2[sides];

            for (int i = 0; i < sides; i++)
            {
                float angle = i * MathF.Tau / sides;
                corners[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            return corners;
        }

        /// <summary>
        /// <paramref name="count"/> bullets on the outline, turned by <paramref name="phase"/> radians. The first one is on the first corner.
        /// With enough bullets every corner gets one (so the corners stay sharp) and the rest are spread along the sides by their length.
        /// <paramref name="scales"/> is null for a circle; otherwise it holds how far from the centre each bullet's point is (0..1).
        /// </summary>
        public static Vector2[] Create(RingShape shape, int count, float phase, out float[]? scales)
        {
            if (shape == RingShape.Curse)
                return glyph(count, out scales);

            var directions = new Vector2[count];
            var corners = Corners(shape);

            if (corners.Length == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = phase + i * MathF.Tau / count;
                    directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                scales = null;
                return directions;
            }

            var points = new List<Vector2>(count);
            int sides = corners.Length;
            var lengths = new float[sides];
            float perimeter = 0;

            for (int i = 0; i < sides; i++)
            {
                lengths[i] = Vector2.Distance(corners[i], corners[(i + 1) % sides]);
                perimeter += lengths[i];
            }

            if (count >= sides)
            {
                // one bullet on every corner; the rest are shared out between the sides by length (largest remainder)
                int spare = count - sides;
                var extra = new int[sides];
                var remainder = new float[sides];
                int given = 0;

                for (int i = 0; i < sides; i++)
                {
                    float exact = spare * lengths[i] / perimeter;
                    extra[i] = (int)MathF.Floor(exact);
                    remainder[i] = exact - extra[i];
                    given += extra[i];
                }

                for (; given < spare; given++)
                {
                    int best = 0;

                    for (int i = 1; i < sides; i++)
                    {
                        if (remainder[i] > remainder[best])
                            best = i;
                    }

                    extra[best]++;
                    remainder[best] = -1f;
                }

                for (int i = 0; i < sides; i++)
                {
                    Vector2 from = corners[i], to = corners[(i + 1) % sides];
                    points.Add(from);

                    for (int k = 1; k <= extra[i]; k++)
                        points.Add(Vector2.Lerp(from, to, k / (float)(extra[i] + 1)));
                }
            }
            else
            {
                // fewer bullets than corners: spread them evenly along the outline
                for (int n = 0; n < count; n++)
                {
                    float along = perimeter * n / count;
                    int side = 0;

                    while (side < sides - 1 && along > lengths[side])
                    {
                        along -= lengths[side];
                        side++;
                    }

                    points.Add(Vector2.Lerp(corners[side], corners[(side + 1) % sides], lengths[side] > 0 ? along / lengths[side] : 0f));
                }
            }

            scales = new float[count];
            float cos = MathF.Cos(phase), sin = MathF.Sin(phase);

            for (int i = 0; i < count; i++)
            {
                Vector2 p = points[i];
                float length = p.Length;
                Vector2 unit = length > 1e-6f ? p / length : new Vector2(1, 0);

                directions[i] = new Vector2(unit.X * cos - unit.Y * sin, unit.X * sin + unit.Y * cos);
                scales[i] = Math.Max(length, 0.05f);
            }

            return directions;
        }
    }
}
