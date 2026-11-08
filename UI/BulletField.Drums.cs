using System;
using System.Collections.Generic;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // osu!taiko's big notes (see Bullets/ConvertedModes): each shoots one ring of Luna Child's arc beams from where it is, the moment it is hit, instead of its rings of bullets: as many arcs as a plain
        // ring of that moment has bullets (so as many more with the OD), each 15 % of its share of the circle, every ring turned a little further than the one before, flying out at her rings' speed from the
        // note's edge until they have left the screen. They hurt like a slider body. Where they are follows from the time alone (nothing to forget when time goes back).
        // ======================================================================================================================================================

        /// <summary>(osu!catch) what falls lands on the bottom wall of the screen, not on the playfield's bottom where it was made to land: this much further down (playfield px).</summary>
        public Vector2 CatchLanding => new Vector2(0f, bounceArea.Max.Y - DanmakusuBalance.CATCH_BOTTOM);

        /// <summary>(osu!catch) where the top wall of the screen is (playfield px): the approach circles of what falls stand on it.</summary>
        public float TopWall => bounceArea.Min.Y;

        /// <summary>The big notes: when, where, how many arcs, and the combo (their colour).</summary>
        public (double Time, Vector2 Centre, int Arcs, int ComboIndex)[] DrumRings { get; init; } = [];

        private readonly List<Vector2> drumArc = new List<Vector2>();

        private float drumSpeed => Math.Max(PlainBulletSpeed, 0.02f) * DanmakusuBalance.LUNA_RING_SPEED;

        /// <summary>(as wide as Luna Child's: a slider beam of a plain bullet)</summary>
        private float drumHalfWidth => circleRadius * 0.25f * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

        private double drumLife => ((hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 1500f) + circleRadius) / drumSpeed;

        private float drumRadius(int ring, double t) => circleRadius + drumSpeed * (float)(t - DrumRings[ring].Time);

        private static float drumPhase(int ring) => ring * 0.17f;

        /// <summary>The first ring that can still be on the screen at <paramref name="t"/> (they are in time order).</summary>
        private int firstDrum(double t)
        {
            double life = drumLife;
            int low = 0, high = DrumRings.Length;

            while (low < high)
            {
                int middle = (low + high) / 2;

                if (DrumRings[middle].Time < t - life)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        /// <summary>The arcs at <paramref name="t"/>, added to the beams that are drawn.</summary>
        private void addDrumArcs(double t, ref int beams, ref int pointTotal)
        {
            if (failed || DrumRings.Length == 0)
                return;

            float width = drumHalfWidth;

            for (int k = firstDrum(t); k < DrumRings.Length && DrumRings[k].Time <= t; k++)
            {
                var ring = DrumRings[k];
                int style = comboStyle(ring.ComboIndex, false, 0);
                Color4 colour = palette[style % palette.Length];
                Color4 border = borderPalette[style % borderPalette.Length];
                float r = drumRadius(k, t);
                float step = MathF.Tau / ring.Arcs;
                float length = step * DanmakusuBalance.LUNA_ARC_SHARE;
                float gap = (step - length) * 0.5f;
                int n = DanmakusuBalance.LUNA_ARC_POINTS;

                for (int i = 0; i < ring.Arcs; i++)
                {
                    float from = drumPhase(k) + i * step + gap;
                    bool visible = false;
                    drumArc.Clear();

                    for (int p = 0; p <= n; p++)
                    {
                        float a = from + length * p / n;
                        Vector2 point = ring.Centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                        drumArc.Add(point);

                        if (!hasScreenBounds || (point.X >= screenMin.X - width && point.X <= screenMax.X + width && point.Y >= screenMin.Y - width && point.Y <= screenMax.Y + width))
                            visible = true;
                    }

                    if (visible)
                        addPlainBeam(drumArc, width, colour, border, ref beams, ref pointTotal);
                }
            }
        }

        /// <summary>Is the player on an arc of a big note's ring (not in a gap), with the room the hit has round it?</summary>
        private bool touchesDrums(double now)
        {
            if (failed || DrumRings.Length == 0)
                return false;

            float width = drumHalfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

            for (int k = firstDrum(now); k < DrumRings.Length && DrumRings[k].Time <= now; k++)
            {
                var ring = DrumRings[k];
                Vector2 offset = characterPosition - ring.Centre;
                float distance = offset.Length;

                if (distance < 1e-3f || MathF.Abs(distance - drumRadius(k, now)) > width)
                    continue;

                float step = MathF.Tau / ring.Arcs;
                float length = step * DanmakusuBalance.LUNA_ARC_SHARE;
                float gap = (step - length) * 0.5f;
                float margin = width / distance; // (the angle across the width of the beam counts too)
                float angle = MathF.Atan2(offset.Y, offset.X) - drumPhase(k);
                float within = angle - MathF.Floor(angle / step) * step;

                for (int shift = -1; shift <= 1; shift++)
                {
                    float w = within + shift * step;

                    if (w >= gap - margin && w <= gap + length + margin)
                        return true;
                }
            }

            return false;
        }
    }
}
