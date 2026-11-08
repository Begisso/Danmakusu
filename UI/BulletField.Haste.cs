using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Gengetsu: over each of her attacks (while every bullet speeds up, see BossEffects) the background is dimmed grey more and more, the bullets blur towards the middle of the screen (a radial,
        // "zoom" blur: faint copies of every bullet between it and the middle, the further out the longer), anime speed lines come in from the edges of the screen, and a riser plays, rising in pitch and
        // loudness. When the attack ends it all goes in a moment.
        // ======================================================================================================================================================

        /// <summary>Gengetsu's attacks (start, end).</summary>
        private readonly List<(double Start, double End)> hasteWindows = new List<(double, double)>();

        /// <summary>How far into one of her attacks it is (0..1: it grows evenly over the attack and dies away over a moment after it).</summary>
        private float hasteLevel(double t)
        {
            float level = 0f;

            foreach (var (start, end) in hasteWindows)
            {
                if (t < start || t > end + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    continue;

                float grown = (float)Math.Clamp((t - start) / Math.Max(end - start, 1.0), 0.0, 1.0);
                level = Math.Max(level, grown * (t <= end ? 1f : 1f - smoothStep((t - end) / DanmakusuBalance.STAGE_FADE_OUT_MS)));
            }

            return level;
        }

        private void registerHaste(BossPeriod period)
        {
            stageOnClock = true;

            foreach (var (start, end) in BossOps.Attacks(period))
            {
                if (end > start)
                    hasteWindows.Add((start, end));
            }
        }

        /// <summary>How much the bullets blur towards the middle (Gengetsu's).</summary>
        private float radialBlurLevel(double t) => hasteWindows.Count > 0 ? hasteLevel(t) : 0f;

        /// <summary>The grey dim of the background (every frame, with the other backgrounds of the stage).</summary>
        private void drawHasteBackground(double t)
        {
            hasteBackground.Alpha = 0f;

            if (hasteWindows.Count == 0 || failed)
                return;

            float level = hasteLevel(t);

            if (level <= 0.002f)
                return;

            if (hasScreenBounds)
            {
                hasteBackground.Position = screenMin;
                hasteBackground.Size = screenMax - screenMin;
            }

            hasteBackground.Alpha = level * DanmakusuBalance.GENGETSU_DIM;
            drawSpeedLines(t, level);
        }

        /// <summary>Anime speed lines: thin streaks from the edge of the screen in towards the middle, under the bullets, more and brighter the further the attack has got; new ones every moment.</summary>
        private void drawSpeedLines(double t, float level)
        {
            Vector2 low = stageMin, high = stageMax;
            Vector2 middle = (low + high) * 0.5f, half = (high - low) * 0.5f;
            int lines = (int)(level * DanmakusuBalance.GENGETSU_SPEED_LINES);
            int moment = (int)(t / DanmakusuBalance.GENGETSU_SPEED_LINE_MS);

            for (int i = 0; i < lines; i++)
            {
                float angle = hash01(i, moment, 3) * MathF.Tau;
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                float toEdge = Math.Min(half.X / Math.Max(Math.Abs(cos), 1e-3f), half.Y / Math.Max(Math.Abs(sin), 1e-3f));
                float length = toEdge * (0.25f + 0.4f * hash01(i, moment, 5));
                float width = 1f + 2.5f * hash01(i, moment, 7);
                Vector2 outwards = new Vector2(cos, sin);

                // (the bright end of the picture is at the edge of the screen, it fades out towards the middle)
                stageBack.Add(StageTexture.Light, middle + outwards * (toEdge - length * 0.5f), new Vector2(length, width), angle * 180f / MathF.PI + 180f, level * DanmakusuBalance.GENGETSU_SPEED_LINE_ALPHA, Color4.White, true);
            }
        }

        /// <summary>The radial blur: every bullet gets faint copies between it and the middle of the screen, as far in as the level and its distance say (a bullet in the middle hardly blurs).</summary>
        private int hasteBlur(double t, int count)
        {
            if ((hasteWindows.Count == 0 && worldEnderActs.Count == 0) || failed || count == 0)
                return count;

            // (and after the explosion of Hecatia's World's Ender: stronger, as far and as visible, fading out slowly)
            float after = worldEnderActs.Count > 0 ? enderAfterBlur(t) * DanmakusuBalance.ENDER_AFTER_BLUR_STRENGTH : 0f;
            float level = Math.Max(radialBlurLevel(t), after);

            if (Math.Abs(level) <= 0.01f)
                return count;

            bool afterEnder = after > 0.01f && after >= radialBlurLevel(t);
            float blurAlpha = afterEnder ? DanmakusuBalance.GENGETSU_BLUR_ALPHA * after : DanmakusuBalance.GENGETSU_BLUR_ALPHA;

            Vector2 middle = hasScreenBounds ? (screenMin + screenMax) * 0.5f : fieldCentre;
            int copies = afterEnder ? DanmakusuBalance.ENDER_AFTER_BLUR_COPIES : DanmakusuBalance.GENGETSU_BLUR_COPIES;

            // (after the World's Ender every bullet blurs towards the nearest place on its split, not towards the middle of the screen)
            WorldEnderAct? split = afterEnder ? enderAfterBlurAct(t) : null;
            int total = count * (copies + 1);

            while (instanceBuffer.Length < total)
                Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

            // (the copies are drawn under the bullets: the bullets are moved to the end of the list, the copies are put in front of them)
            Array.Copy(instanceBuffer, 0, instanceBuffer, count * copies, count);

            for (int k = 0; k < copies; k++)
            {
                float pull = level * DanmakusuBalance.GENGETSU_BLUR_REACH * (k + 1) / copies; // (how far towards the middle: a share of the way there; below 0 away from it)
                float alpha = blurAlpha * (1f - k / (float)copies);

                for (int i = 0; i < count; i++)
                {
                    var copy = instanceBuffer[count * copies + i];
                    Vector2 towards = split != null ? split.TowardsSplit(copy.Position) : middle;
                    copy.Position = towards + (copy.Position - towards) * (1f - pull);
                    copy.Fade = 1f - (1f - copy.Fade) * alpha;
                    copy.Bloom = 0f;
                    instanceBuffer[k * count + i] = copy;
                }
            }

            return total;
        }

        private SampleChannel? hasteRiserChannel;

        /// <summary>The riser of her attacks: a loop whose pitch and loudness rise over the attack, and that dies away over a moment after it.</summary>
        private void updateHasteSound(double now)
        {
            float level = 0f;
            bool inside = false;

            foreach (var (start, end) in hasteWindows)
            {
                if (now >= start && now < end + 200)
                {
                    inside = true;
                    level = Math.Max(level, (float)Math.Clamp((now - start) / Math.Max(end - start, 1.0), 0.0, 1.0));
                }
            }

            bool wanted = inside && !failed && soundStillFrames < 8 && cueSamples.TryGetValue("gengetsu_riser", out var loop) && loop != null;

            if (wanted && hasteRiserChannel == null)
            {
                hasteRiserChannel = cueSamples["gengetsu_riser"]!.GetChannel();
                hasteRiserChannel.Looping = true;
                hasteRiserChannel.Play();
            }

            if (hasteRiserChannel != null)
            {
                if (!wanted)
                {
                    hasteRiserChannel.Stop();
                    hasteRiserChannel = null;
                    return;
                }

                // (after the end it is at its top and dies away over 200 ms)
                double after = 0;

                foreach (var (start, end) in hasteWindows)
                {
                    if (now >= end && now < end + 200)
                        after = Math.Max(after, (now - end) / 200.0);
                }

                hasteRiserChannel.Frequency.Value = DanmakusuBalance.GENGETSU_RISER_PITCH_FROM + (DanmakusuBalance.GENGETSU_RISER_PITCH_TO - DanmakusuBalance.GENGETSU_RISER_PITCH_FROM) * level;
                hasteRiserChannel.Volume.Value = (DanmakusuBalance.GENGETSU_RISER_VOLUME_FROM + (DanmakusuBalance.GENGETSU_RISER_VOLUME_TO - DanmakusuBalance.GENGETSU_RISER_VOLUME_FROM) * level) * (1.0 - after);
            }
        }
    }
}
