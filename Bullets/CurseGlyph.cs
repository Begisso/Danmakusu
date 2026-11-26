using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The character 呪 ("curse"), Noroiko's: its strokes in a box one unit tall (y down) round its middle - its mouth on the left, and 兄 (a mouth over two legs) on the right. Her curses are drawn with
    /// it in beams (CurseAct), and since 2026-11-25 the rings of her section are laid out on it (<see cref="RingShape.Curse"/>).
    /// </summary>
    public static class CurseGlyph
    {
        public static readonly Vector2[][] STROKES =
        {
            // 口 (left)
            new[] { new Vector2(-0.48f, -0.16f), new Vector2(-0.48f, 0.2f) },
            new[] { new Vector2(-0.48f, -0.16f), new Vector2(-0.2f, -0.16f), new Vector2(-0.2f, 0.2f) },
            new[] { new Vector2(-0.48f, 0.2f), new Vector2(-0.2f, 0.2f) },
            // 口 (right, top)
            new[] { new Vector2(-0.06f, -0.48f), new Vector2(-0.06f, -0.06f) },
            new[] { new Vector2(-0.06f, -0.48f), new Vector2(0.44f, -0.48f), new Vector2(0.44f, -0.06f) },
            new[] { new Vector2(-0.06f, -0.06f), new Vector2(0.44f, -0.06f) },
            // 儿 (the legs)
            new[] { new Vector2(0.08f, -0.06f), new Vector2(0.07f, 0.16f), new Vector2(0f, 0.32f), new Vector2(-0.12f, 0.44f), new Vector2(-0.22f, 0.5f) },
            new[] { new Vector2(0.28f, -0.06f), new Vector2(0.28f, 0.38f), new Vector2(0.31f, 0.46f), new Vector2(0.38f, 0.49f), new Vector2(0.48f, 0.49f), new Vector2(0.5f, 0.4f) },
        };
    }
}
