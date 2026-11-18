using System;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Matenshi's wing as a bird's wing is built (2026-11-15, the user: "you sliced her wings wrongly. You need to segment her feathers and bones instead"; it was six slices of the angle round the shoulder):
    /// three bones along its leading edge - the humerus from the shoulder to the elbow, the forearm to the wrist, the hand to its tip - each with the small covering feathers that lie on it, and the long feathers,
    /// each on its own: the primaries on the hand, the secondaries on the forearm, the tertials at the elbow. Every pixel of the picture belongs to the bone or feather it is nearest to (a bone as far as its
    /// covering reaches, a feather a little round its shaft), and each piece turns about its own joint or root, carried by the bone it is on. All in pixels of the right wing's picture (512 x 433, the user's,
    /// cut out by Tools\Bake wings); the left wing is its mirror image.
    /// </summary>
    public static class WingRig
    {
        public const int BONES = 3;

        /// <summary>The joints along the leading edge: the shoulder (<see cref="StageLayer.WING_ROOT"/>), the elbow, the wrist and the hand's tip.</summary>
        public static readonly Vector2[] JOINTS =
        {
            new Vector2(20.5f, 312.6f), new Vector2(118f, 232f), new Vector2(238f, 138f), new Vector2(335f, 68f),
        };

        /// <summary>How far round each bone its covering feathers reach (px): the humerus carries the whole mass at the shoulder.</summary>
        private static readonly float[] bone_reach = { 55f, 26f, 18f };

        /// <summary>The long feathers: the bone each grows on, its root (on the bone) and its tip, read off the picture.</summary>
        public static readonly (int Bone, Vector2 Root, Vector2 Tip)[] FEATHERS =
        {
            // (primaries, on the hand: the short one at the top, then from the longest down)
            (2, new Vector2(300f, 66f), new Vector2(397f, 18f)),
            (2, new Vector2(330f, 72f), new Vector2(505f, 12f)),
            (2, new Vector2(318f, 82f), new Vector2(500f, 45f)),
            (2, new Vector2(304f, 92f), new Vector2(498f, 80f)),
            (2, new Vector2(288f, 103f), new Vector2(480f, 122f)),
            (2, new Vector2(270f, 115f), new Vector2(450f, 168f)),
            (2, new Vector2(252f, 128f), new Vector2(418f, 210f)),
            // (secondaries, on the forearm, from the wrist in)
            (1, new Vector2(230f, 146f), new Vector2(385f, 245f)),
            (1, new Vector2(210f, 160f), new Vector2(345f, 275f)),
            (1, new Vector2(190f, 175f), new Vector2(310f, 308f)),
            (1, new Vector2(172f, 189f), new Vector2(280f, 333f)),
            (1, new Vector2(154f, 203f), new Vector2(258f, 352f)),
            (1, new Vector2(136f, 217f), new Vector2(232f, 376f)),
            // (tertials, at the elbow and the humerus, the lowest of the wing)
            (0, new Vector2(118f, 240f), new Vector2(200f, 398f)),
            (0, new Vector2(104f, 250f), new Vector2(162f, 412f)),
            (0, new Vector2(88f, 262f), new Vector2(126f, 420f)),
            (0, new Vector2(70f, 276f), new Vector2(94f, 406f)),
            (0, new Vector2(50f, 290f), new Vector2(60f, 392f)),
        };

        /// <summary>How far round its shaft a feather reaches before a neighbour or a bone is nearer (px).</summary>
        private const float feather_reach = 10f;

        /// <summary>The bones first, then the feathers.</summary>
        public static int Pieces => BONES + FEATHERS.Length;

        /// <summary>The bone a piece is on (a bone is on itself).</summary>
        public static int BoneOf(int piece) => piece < BONES ? piece : FEATHERS[piece - BONES].Bone;

        /// <summary>The point a piece turns about: a bone's joint at its inner end, a feather's root.</summary>
        public static Vector2 PivotOf(int piece) => piece < BONES ? JOINTS[piece] : FEATHERS[piece - BONES].Root;

        /// <summary>How far out along the wing a piece is (0 at the shoulder .. 1 at the hand's tip): the further out, the later it follows a sway.</summary>
        public static float Along(int piece)
        {
            int bone = BoneOf(piece);

            if (piece < BONES)
                return bone / (float)BONES;

            Vector2 a = JOINTS[bone], b = JOINTS[bone + 1];
            float u = Math.Clamp(Vector2.Dot(FEATHERS[piece - BONES].Root - a, b - a) / Math.Max((b - a).LengthSquared, 1e-6f), 0f, 1f);
            return (bone + u) / BONES;
        }

        /// <summary>How near piece <paramref name="piece"/> is at <paramref name="p"/>: its distance less its reach (the least is the piece the point belongs to).</summary>
        public static float Score(int piece, Vector2 p)
        {
            if (piece < BONES)
                return distanceToSegment(p, JOINTS[piece], JOINTS[piece + 1]) - bone_reach[piece];

            var feather = FEATHERS[piece - BONES];
            return distanceToSegment(p, feather.Root, feather.Tip) - feather_reach;
        }

        /// <summary>The piece the point belongs to, and its score.</summary>
        public static int PieceAt(Vector2 p, out float best)
        {
            int nearest = 0;
            best = float.MaxValue;

            for (int i = 0; i < Pieces; i++)
            {
                float score = Score(i, p);

                if (score < best)
                {
                    best = score;
                    nearest = i;
                }
            }

            return nearest;
        }

        public static int PieceAt(Vector2 p) => PieceAt(p, out _);

        /// <summary>
        /// How far (px) a neighbour's part reaches into piece <paramref name="nearest"/>'s at <paramref name="p"/>, so that no gap opens between them as they turn apart: more the further from its joint or root
        /// (a degree or so apart opens most at the tips), from 3.5 px to 14.
        /// </summary>
        public static float OverlapAt(Vector2 p, int nearest) => Math.Min(3.5f + 0.06f * (p - PivotOf(nearest)).Length, 14f);

        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;
            float u = lengthSquared > 1e-9f ? Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
            return (a + ab * u - point).Length;
        }
    }
}
