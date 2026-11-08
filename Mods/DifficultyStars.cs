using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Mods
{
    /// <summary>A boss's difficulty: a row of five stars, as many lit in the colour of the rating as she has and the rest dark, so that the rows of a list line up.</summary>
    public partial class DifficultyStars : FillFlowContainer
    {
        public DifficultyStars(int rating, float size)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FillDirection.Horizontal;
            Spacing = new Vector2(1, 0);

            Color4 lit = BossDifficulty.ColourOf(rating);

            for (int star = 1; star <= BossDifficulty.MOST; star++)
            {
                Add(new SpriteIcon
                {
                    Size = new Vector2(size),
                    Icon = FontAwesome.Solid.Star,
                    Colour = star <= rating ? lit : new Color4(1f, 1f, 1f, 0.15f),
                });
            }
        }
    }
}
