using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Hata no Kokoro (2026-11-20): no pauses. Every eight beats her mood changes - purple, blue, aquamarine, orange, and again from purple - with the sound of Yuyuko's petals leaving the tree, and
        // everything on the screen (bullets and beams, anyone's) takes it on, glowing in its colour (the ops are BossOps'):
        //  - purple (a neutral mask): arrowheads, each aimed at the player - what is there when the mood comes, then, and what is fired in it, as it is fired, at where the player was in that beat;
        //  - blue (an oni's laughing mask): ellipses, pulled at the player the same way, as Nazrin's are;
        //  - aquamarine (a sad mask): stars that ricochet off the border three times while it lasts;
        //  - orange (a smiling mask): everything falls to the bottom, a quarter as hard as when the player fails, keeping its way and speed. For two beats before it, an orange arrow beside every second bullet
        //    points where that bullet will be a beat into the fall.
        // The background is dimmed in the mood's colour and a giant see-through mask of the mood shows on it. She shoots very dense rings of plain bullets, which are of her mood as everything is.
        // This act places where the aimed and pulled ops go, plays the sound, and draws the dim, the masks and the arrows (the arrows are worked out with the bullets: see BulletField).
        // ======================================================================================================================================================

        private sealed class MasksAct : StageAct
        {
            private readonly double beat;
            private readonly List<(double Start, double End, int Mood)> moods;
            private readonly List<TrajectoryOp> aimed = new List<TrajectoryOp>(); // (her aimed and pulled ops, in time order: each goes where the player is when its time comes)
            private int nextSound;
            private double resetTime = double.NegativeInfinity;

            /// <summary>The colour of each mood: purple, blue, aquamarine, orange.</summary>
            public static readonly Color4[] MOOD_COLOURS =
            {
                new Color4(0.66f, 0.38f, 0.98f, 1f),
                new Color4(0.36f, 0.52f, 1f, 1f),
                new Color4(0.36f, 0.96f, 0.86f, 1f),
                new Color4(1f, 0.6f, 0.24f, 1f),
            };

            /// <summary>The dim of each mood, darker.</summary>
            private static readonly Color4[] dims =
            {
                new Color4(0.2f, 0.06f, 0.32f, 1f),
                new Color4(0.03f, 0.08f, 0.36f, 1f),
                new Color4(0.02f, 0.28f, 0.26f, 1f),
                new Color4(0.38f, 0.17f, 0.03f, 1f),
            };

            private static readonly StageTexture[] masks = { StageTexture.MaskNeutral, StageTexture.MaskOni, StageTexture.MaskSad, StageTexture.MaskSmile };

            public MasksAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                moods = BossOps.MaskMoods(period);
                Start = period.ShootStart;
                End = period.End;

                foreach (var op in f.bulletOps)
                {
                    if (op.Kind is TrajectoryOpKind.Aim or TrajectoryOpKind.Pull && op.Time >= period.ShootStart - 1 && op.Time < period.End)
                        aimed.Add(op);
                }

                // (the field draws the arrows that warn of her falls with the bullets, two beats before each)
                foreach (var (start, _, mood) in moods)
                {
                    if (mood == 3)
                        f.kokoroFalls.Add((start, beat));
                }

                f.kokoroFalls.Sort((a, b) => a.Start.CompareTo(b.Start));
            }

            public override void Reset(BulletField f, double time)
            {
                // (where the player was is placed again when the time of each comes)
                foreach (var op in aimed)
                {
                    if (op.Time > time)
                        op.Placed = false;
                }

                nextSound = 0;

                while (nextSound < moods.Count && moods[nextSound].Start <= time)
                    nextSound++;

                resetTime = time;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var op in aimed)
                {
                    if (op.Time > now)
                        break;

                    if (op.Placed)
                        continue;

                    op.Point = f.characterPosition;
                    op.Placed = true;
                    f.autoOpsMoved = true; // (every ring's way after it has changed: the autoplay looks again)
                }

                // (every change of mood is heard: Yuyuko's petals leaving the tree)
                for (; nextSound < moods.Count && moods[nextSound].Start <= now; nextSound++)
                {
                    if (now - moods[nextSound].Start < 250 && moods[nextSound].Start > resetTime)
                        f.playStageSound("yuyuko_petals");
                }
            }

            /// <summary>The mood at <paramref name="t"/>, and how far into it (ms), or -1.</summary>
            private int moodAt(double t, out double into, out double length)
            {
                into = length = 0;

                foreach (var (start, end, mood) in moods)
                {
                    if (t >= start && t < end)
                    {
                        into = t - start;
                        length = end - start;
                        return mood;
                    }
                }

                return -1;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                int mood = moodAt(t, out double into, out double length);

                if (mood < 0)
                    return;

                // the dim of her mood (it turns from the last one's colour over the first moments of the new)
                float change = smoothStep(into / 400.0);
                Color4 dim = mood == 0 && into < 1 ? dims[0] : lerp(dims[(mood + 3) % 4], dims[mood], change);
                float presence = smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((End - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (f.hasScreenBounds)
                {
                    f.kokoroBackground.Position = f.screenMin;
                    f.kokoroBackground.Size = f.screenMax - f.screenMin;
                }

                f.kokoroBackground.Colour = dim;
                f.kokoroBackground.Alpha = Math.Max(f.kokoroBackground.Alpha, presence * DanmakusuBalance.KOKORO_BACKGROUND_ALPHA);

                // and its giant see-through mask, coming in at the change and going at the end of the mood
                float shown = smoothStep(into / 500.0) * smoothStep((length - into) / 500.0) * presence;
                float size = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.KOKORO_MASK_SCREEN;
                Color4 tint = lerp(MOOD_COLOURS[mood], Color4.White, 0.45f);

                if (shown > 0.01f)
                    f.stageBack.Add(masks[mood], (f.stageMin + f.stageMax) * 0.5f, new Vector2(size), 0f, DanmakusuBalance.KOKORO_MASK_ALPHA * shown, tint);
            }

            private static Color4 lerp(Color4 a, Color4 b, float u) => new Color4(a.R + (b.R - a.R) * u, a.G + (b.G - a.G) * u, a.B + (b.B - a.B) * u, 1f);
        }

        // ---- the arrows that warn of Kokoro's falls (worked out with the bullets: where each will be a beat into the fall) ----

        /// <summary>When each of Kokoro's falls begins, and her beat.</summary>
        private readonly List<(double Start, double Beat)> kokoroFalls = new List<(double, double)>();

        /// <summary>Her mood's background (a dim of its colour).</summary>
        private Box kokoroBackground = null!;

        private readonly List<(Vector2 At, float Size, float Degrees)> kokoroArrows = new List<(Vector2, float, float)>();

        /// <summary>How much of the warning shows at <paramref name="t"/> (0: none), and the fall it is for: it comes in over the first half beat of the two before the fall, and blinks over the last.</summary>
        private float kokoroWarning(double t, out double fallAt, out double beat)
        {
            fallAt = beat = 0;

            foreach (var (start, b) in kokoroFalls)
            {
                double warn = b * DanmakusuBalance.KOKORO_WARN_BEATS;

                if (t < start - warn || t >= start)
                    continue;

                fallAt = start;
                beat = b;
                float level = smoothStep((t - (start - warn)) / (b * 0.5));

                if (start - t < b)
                    level *= 0.6f + 0.4f * MathF.Cos((float)((start - t) / b) * MathF.PI * 4f);

                return level;
            }

            return 0f;
        }

        /// <summary>
        /// An arrow beside a ring's bullet at <paramref name="position"/>, flying <paramref name="velocity"/> (px/ms): it points the way the bullet will be flying a little into the fall - its own way, bent down
        /// by the pull (2026-11-20, the user: "bullets fall bottom following movement inertia (arrows should aim considering inertia too)").
        /// </summary>
        private void addKokoroArrow(Vector2 position, Vector2 velocity, float radius, double beat)
        {
            float falling = (float)(beat * DanmakusuBalance.KOKORO_ARROW_AHEAD_BEATS);
            Vector2 way = velocity + new Vector2(0f, DanmakusuBalance.KOKORO_GRAVITY * falling);

            if (way.LengthSquared < 1e-4f)
                way = Vector2.UnitY;

            way = Vector2.Normalize(way);
            float arrow = Math.Max(radius, 3f) * DanmakusuBalance.KOKORO_ARROW_SIZE;
            kokoroArrows.Add((position + way * (radius + arrow), arrow * 2.2f, MathF.Atan2(way.Y, way.X) * 180f / MathF.PI));
        }

        private void drawKokoroArrows(double t)
        {
            if (kokoroArrows.Count == 0)
                return;

            float level = kokoroWarning(t, out _, out _);
            var orange = new Color4(1f, 0.55f, 0.15f, 1f);

            foreach (var (at, size, degrees) in kokoroArrows)
                stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), degrees, level, orange);
        }
    }
}
