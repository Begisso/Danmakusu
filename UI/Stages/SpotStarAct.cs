using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Byakuren: where a spot she remembered throws its homing beams (see BulletPlanner.addRememberedBeams), a big star like the one under a boss while she fires (Marisa's), in Byakuren's
        // colours, flashes up for a moment, spinning: it fades in, stays for a beat and fades out. While it is there it hurts like a slider body.
        // ======================================================================================================================================================

        private sealed class SpotStarAct : StageAct
        {
            private const float big_star_radius_factor = 2.1f; // (as big as the boss's big star: see BossCharacter)
            private const float big_star_fraction = 0.6f;
            private const float big_star_spin = 40f; // (degrees per second, the same way as the boss's)

            private readonly List<(double Time, Vector2 Spot, double Beat)> stars;

            public SpotStarAct(IEnumerable<BossPeriod> periods)
            {
                stars = periods.SelectMany(p => p.SpotThrows.Select(s => (s.Time, s.Spot, Math.Max(p.BeatLength, 1)))).OrderBy(s => s.Time).ToList();
                Period = periods.First();
                Start = stars.Count > 0 ? stars[0].Time : 0;
                End = stars.Count > 0 ? stars[^1].Time + stayOf(stars[^1].Beat) + DanmakusuBalance.BYAKUREN_STAR_FADE_MS : 0;
            }

            private static double stayOf(double beat) => beat * DanmakusuBalance.BYAKUREN_SPOT_STAR_BEATS;

            /// <summary>0..1: how much of the star at <paramref name="star"/> is there at <paramref name="t"/> (fades in, stays, fades out).</summary>
            private static float presence((double Time, Vector2 Spot, double Beat) star, double t)
            {
                double age = t - star.Time, stay = stayOf(star.Beat), fade = DanmakusuBalance.BYAKUREN_STAR_FADE_MS;

                if (age < 0 || age > stay + fade)
                    return 0f;

                return smoothStep(age / fade) * (1f - smoothStep((age - stay) / fade));
            }

            /// <summary>The size (px) the star is drawn at: as much smaller than the boss's big star as a spot is than Marisa's beam.</summary>
            private static float sizeOf(BulletField f) => 2f * f.circleRadius * big_star_radius_factor / big_star_fraction * DanmakusuBalance.BYAKUREN_SPOT_BEAM_SIZE * DanmakusuBalance.BYAKUREN_SPOT_STAR_SIZE;

            /// <summary>(the stars that can be there at <paramref name="t"/>: the list is in time order and each lasts at most a few beats)</summary>
            private IEnumerable<(double Time, Vector2 Spot, double Beat)> around(double t)
            {
                foreach (var star in stars)
                {
                    if (star.Time > t)
                        yield break;

                    if (t - star.Time <= stayOf(star.Beat) + DanmakusuBalance.BYAKUREN_STAR_FADE_MS)
                        yield return star;
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start || now > End)
                    return false;

                // (the body of the star: the inner part of the picture, while it is mostly there)
                float reach = sizeOf(f) * 0.5f * big_star_fraction * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var star in around(now))
                {
                    if (presence(star, now) > 0.5f && Vector2.Distance(f.characterPosition, star.Spot) <= reach)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End)
                    return;

                float size = sizeOf(f);
                float rotation = (float)(-t / 1000.0 * big_star_spin);

                foreach (var star in around(t))
                {
                    float alpha = presence(star, t);

                    if (alpha <= 0.002f)
                        continue;

                    f.stageFront.Add(StageTexture.ByakurenStarGlow, star.Spot, new Vector2(size), rotation, alpha, Color4.White, additive: true);
                    f.stageFront.Add(StageTexture.ByakurenStar, star.Spot, new Vector2(size), rotation, alpha, Color4.White);
                }
            }
        }
    }
}
