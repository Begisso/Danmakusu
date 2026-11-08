using System;
using System.Linq;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The star rating of a map: how dense its bullets are (<see cref="BulletDensity"/>: how much of the playfield they cut through per second) together with how much they make the player move to safer places
    /// (<see cref="ForcedMovement"/>: how far a lazy player walks, how often they start a dodge or must teleport). The map is planned once, without bosses, and both are measured on it; the stars are
    /// scale x density ^ a x movement ^ b (<see cref="DanmakusuBalance.STAR_SCALE"/>, <see cref="DanmakusuBalance.STAR_DENSITY_POWER"/>, <see cref="DanmakusuBalance.STAR_MOVEMENT_POWER"/>), a and b chosen
    /// so that the stars come out closest to osu!'s own over the downloaded maps (`sectioncheck stars` with STARS_CSV gives the numbers to fit on).
    /// </summary>
    public static class StarRating
    {
        /// <summary>
        /// The version of the ratings that song select keeps (<see cref="Stars.DanmakusuStars"/>, danmakusu_stars.csv in osu!'s data folder): raise it whenever the stars come out differently (the formula, its
        /// numbers, or what the planner makes of a map without bosses), and every map is rated again in the background.
        /// </summary>
        public const int STORE_VERSION = 2; // (2: the counts of bullets and beams and the most bosses are kept with the stars)

        public sealed class Result
        {
            public double Stars;

            /// <summary>How many bullets and beams the map's own circles, sliders and spinners make (without bosses).</summary>
            public int Bullets, Beams;

            public BulletDensity.Result Density = null!;
            public ForcedMovement.Result Movement = null!;
        }

        /// <summary>Rates the (converted) map, played <paramref name="clockRate"/> times as fast.</summary>
        public static Result Rate(IBeatmap beatmap, double clockRate = 1)
        {
            var rings = BulletPlanner.Plan(beatmap, out var lasers, out _, noBoss: true);
            var density = BulletDensity.Rate(rings, lasers, clockRate);
            var movement = ForcedMovement.Rate(rings, lasers, clockRate);

            int bullets = 0;

            foreach (var ring in rings)
                bullets += ring.Count;

            return new Result { Stars = Combine(density.Value, movement.Value), Bullets = bullets, Beams = lasers.Count(), Density = density, Movement = movement };
        }

        /// <summary>The stars of a density value and a movement value.</summary>
        public static double Combine(double density, double movement)
            => DanmakusuBalance.STAR_SCALE * Math.Pow(Math.Max(density, 1e-6), DanmakusuBalance.STAR_DENSITY_POWER) * Math.Pow(Math.Max(movement, 1e-6), DanmakusuBalance.STAR_MOVEMENT_POWER);
    }
}
