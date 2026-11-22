using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Focus Lens (2026-11-22): the edges of the playfield, as the camera follows the player - a white line about a pixel and a half thick on the screen, and black beyond it, far out on every side, so that
    /// nothing past the line is seen. A layer of the playfield (in its coordinates), above everything in it but the boss HUD (DanmakusuPlayfield puts it there: see updateFocus).
    /// </summary>
    public partial class FocusFrame : Container
    {
        private readonly Box[] dark = new Box[4];
        private readonly Box[] edges = new Box[4];

        public FocusFrame()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0f;

            for (int i = 0; i < 4; i++)
                Add(dark[i] = new Box { Colour = Color4.Black });

            for (int i = 0; i < 4; i++)
                Add(edges[i] = new Box { Colour = Color4.White });
        }

        /// <summary>The playfield's edges are <paramref name="min"/> and <paramref name="max"/>; it is shown <paramref name="zoom"/> times as big (the line stays as thick on the screen).</summary>
        public void Place(Vector2 min, Vector2 max, float zoom)
        {
            const float far = 30000f;
            float line = 1.5f / zoom;
            Vector2 size = max - min;

            // (black beyond every edge: above and below all the way across, left and right between them)
            dark[0].Position = new Vector2(min.X - far, min.Y - far);
            dark[0].Size = new Vector2(size.X + 2 * far, far);
            dark[1].Position = new Vector2(min.X - far, max.Y);
            dark[1].Size = new Vector2(size.X + 2 * far, far);
            dark[2].Position = new Vector2(min.X - far, min.Y);
            dark[2].Size = new Vector2(far, size.Y);
            dark[3].Position = new Vector2(max.X, min.Y);
            dark[3].Size = new Vector2(far, size.Y);

            // (the line just outside the edges, on the black)
            edges[0].Position = new Vector2(min.X - line, min.Y - line);
            edges[0].Size = new Vector2(size.X + 2 * line, line);
            edges[1].Position = new Vector2(min.X - line, max.Y);
            edges[1].Size = new Vector2(size.X + 2 * line, line);
            edges[2].Position = new Vector2(min.X - line, min.Y);
            edges[2].Size = new Vector2(line, size.Y);
            edges[3].Position = new Vector2(max.X, min.Y);
            edges[3].Size = new Vector2(line, size.Y);
        }
    }
}
