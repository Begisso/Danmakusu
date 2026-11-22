using System;
using System.Collections.Generic;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // The warnings of what the timers of Rin's and Aunn's rings do to every bullet (the stops, swells and moves are ops of each ring's own: BossOps and BulletEmitter.ConfigureOps). They are worked
        // out with the bullets, as each is drawn:
        //  - Rin: for RIN_WARN_BEATS before a bullet stops and swells, a red circle as big as it will swell to flashes round it, RIN_WARN_FLASHES times;
        //  - Aunn: for AUNN_WARN_BEATS before each of a bullet's moves, a green arrow beside it points at the player (the way it will set off, if the player stays where they are).
        // ======================================================================================================================================================

        /// <summary>Rin's circles of this frame: where, how big, and how far through its warning (0..1) each bullet is.</summary>
        private readonly List<(Vector2 At, float Radius, float Progress)> swellWarnings = new List<(Vector2, float, float)>();

        /// <summary>Aunn's arrows of this frame: where, how big, which way (degrees) and how solid.</summary>
        private readonly List<(Vector2 At, float Size, float Degrees, float Level)> goArrows = new List<(Vector2, float, float, float)>();

        private static readonly Color4 swell_warning_colour = new Color4(1f, 0.32f, 0.18f, 1f);
        private static readonly Color4 go_arrow_colour = new Color4(0.35f, 1f, 0.65f, 1f);

        private void drawSwellWarnings()
        {
            foreach (var (at, radius, progress) in swellWarnings)
            {
                // (on at the start, off and on again RIN_WARN_FLASHES times, on when it swells)
                float flash = 0.5f + 0.5f * MathF.Cos(progress * MathF.Tau * DanmakusuBalance.RIN_WARN_FLASHES);
                float band = Math.Max(radius * 0.12f, 1.2f);
                stageRipple.AddRing(at, radius + band, band, DanmakusuBalance.RIN_WARN_ALPHA * flash, swell_warning_colour);
            }
        }

        /// <summary>An arrow beside a bullet of Aunn's ring at <paramref name="position"/> (drawn <paramref name="radius"/> big), when one of its moves (<paramref name="goAt"/>, on its own clock) is near: it points at the player.</summary>
        private void addGoArrow(double[] goAt, double warnMs, double ringTime, Vector2 position, float radius)
        {
            double next = double.NaN;

            foreach (double go in goAt)
            {
                if (go > ringTime)
                {
                    next = go;
                    break;
                }
            }

            if (double.IsNaN(next) || next - ringTime > warnMs || warnMs <= 0)
                return;

            Vector2 way = characterPosition - position;

            if (way.LengthSquared < 1f)
                return;

            way = Vector2.Normalize(way);

            // (it comes in over the first half of the warning and blinks over the second)
            double u = 1.0 - (next - ringTime) / warnMs;
            float level = smoothStep(u * 2.0);

            if (u > 0.5)
                level *= 0.6f + 0.4f * MathF.Cos((float)((u - 0.5) * 2.0) * MathF.PI * 4f);

            float arrow = Math.Max(radius, 3f) * DanmakusuBalance.AUNN_ARROW_SIZE * 0.5f;
            goArrows.Add((position + way * (radius + arrow), arrow * 2.2f, MathF.Atan2(way.Y, way.X) * 180f / MathF.PI, level));
        }

        private void drawGoArrows()
        {
            foreach (var (at, size, degrees, level) in goArrows)
                stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), degrees, level, go_arrow_colour);
        }
    }
}
