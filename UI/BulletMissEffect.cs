using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The skin's miss judgement (hit0 image / animation, or the skin's own piece), shown where the player got hit.
    /// Position it in playfield coordinates; it removes itself when done.
    /// </summary>
    public partial class BulletMissEffect : CompositeDrawable
    {
        private readonly SkinnableDrawable judgement;
        private bool started;

        public BulletMissEffect()
        {
            InternalChild = judgement = new SkinnableDrawable(
                new SkinComponentLookup<HitResult>(HitResult.Miss),
                _ => new SpriteText
                {
                    Text = "MISS",
                    Colour = new Color4(255, 70, 70, 255),
                    Font = OsuFont.Default.With(size: 14, weight: FontWeight.Bold),
                },
                confineMode: ConfineMode.NoScaling)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
            };
        }

        protected override void Update()
        {
            base.Update();

            // the skin drawable is resolved asynchronously; wait until it exists.
            if (started || judgement.Drawable == null)
                return;

            started = true;

            if (judgement.Drawable is IAnimatableJudgement animatable)
            {
                // skinned pieces animate themselves (fade, drop, rotate...)
                animatable.PlayAnimation();
            }
            else
            {
                judgement.FadeInFromZero(50).Then(250).FadeOut(300);
                judgement.MoveToOffset(new Vector2(0, 12), 600, Easing.OutQuint);
            }

            this.Delay(1200).Expire();
        }
    }
}
