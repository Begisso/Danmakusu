using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        /// <summary>Is a boss of Koishi's on the screen at <paramref name="now"/>?</summary>
        private bool koishiOn(double now)
        {
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.Unconscious && now >= period.Start && now <= period.End)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Koishi's vanishing: for two beats from every mark nothing can be seen (<paramref name="hideAll"/>); a glitch (bullets that flicker on and off, with
        /// <paramref name="flicker"/> the share of them that is off right now) goes out into it and out of it.
        /// </summary>
        private void koishiGlitch(double now, out bool hideAll, out float flicker)
        {
            hideAll = false;
            flicker = 0f;

            foreach (var (time, beat) in koishiMarks)
            {
                double duration = beat * DanmakusuBalance.KOISHI_HIDE_BEATS;
                double glitchOut = DanmakusuBalance.KOISHI_GLITCH_OUT_MS;
                double glitchIn = DanmakusuBalance.KOISHI_GLITCH_IN_MS;

                if (now < time || now > time + duration + glitchIn)
                    continue;

                if (now < time + glitchOut)
                    flicker = Math.Max(flicker, (float)((now - time) / glitchOut)); // (more and more of them are off)
                else if (now < time + duration)
                    hideAll = true;
                else
                    flicker = Math.Max(flicker, 1f - (float)((now - time - duration) / glitchIn)); // (fewer and fewer)
            }
        }

        /// <summary>
        /// Koishi: the bullets that the mechanics of the bosses make (not the ones of the rings, see the ring loop) vanish with the others: near the player, all of them (but in the dim light round the player) while everything is invisible,
        /// and flickering out and in. Takes the instances from <paramref name="from"/> to <paramref name="count"/> out of the buffer if they are not to be seen, and returns the new count.
        /// </summary>
        private int koishiHideMechanics(int from, int count, bool active, int flickerBucket)
        {
            if (!active)
                return count;

            int kept = from;

            for (int i = from; i < count; i++)
            {
                var instance = instanceBuffer[i];
                float fade;

                // (a boss's own thing - a bead of an act, a flower on Yuyuko's tree - stays as it is)
                if (instance.Immune)
                {
                    instanceBuffer[kept++] = instance;
                    continue;
                }

                if (koishiHideAll)
                {
                    float lit = Math.Clamp((Vector2.Distance(instance.Position, characterPosition) - circleRadius * DanmakusuBalance.KOISHI_REVEAL_NEAR) / Math.Max(circleRadius * (DanmakusuBalance.KOISHI_REVEAL_FAR - DanmakusuBalance.KOISHI_REVEAL_NEAR), 1f), 0f, 1f);
                    float seen = (1f - lit * lit * (3f - 2f * lit)) * DanmakusuBalance.KOISHI_REVEAL_VISIBILITY;

                    if (seen < 0.02f)
                        continue;

                    fade = 1f - seen;
                    instance.Smile = true;
                }
                else if (koishiFlicker > 0f && hash01(i - from, 33, flickerBucket) < koishiFlicker)
                    continue;
                else
                {
                    float near = circleRadius * DanmakusuBalance.KOISHI_FADE_NEAR, far = circleRadius * DanmakusuBalance.KOISHI_FADE_FAR;
                    float u = Math.Clamp((Vector2.Distance(instance.Position, characterPosition) - near) / Math.Max(far - near, 1f), 0f, 1f);
                    fade = 1f - u * u * (3f - 2f * u);

                    if (koishiFlicker > 0f)
                        instance.Position += new Vector2((hash01(i - from, flickerBucket, 7) - 0.5f) * 10f * koishiFlicker, (hash01(i - from, flickerBucket, 11) - 0.5f) * 4f * koishiFlicker);
                }

                instance.Fade = 1f - (1f - instance.Fade) * (1f - fade);
                instanceBuffer[kept++] = instance;
            }

            return kept;
        }

        /// <summary>This map has a boss of Koishi's (whose vanishing the hit objects share).</summary>
        public bool HasKoishi => koishiMarks.Length > 0;

        /// <summary>
        /// How visible the circles and sliders are at <paramref name="now"/>: they vanish with Koishi's bullets (completely while everything is invisible, flickering
        /// on and off like the beams while it glitches away and back).
        /// </summary>
        public float KoishiHitObjectAlpha(double now)
        {
            if (failed || koishiMarks.Length == 0 || !koishiOn(now))
                return 1f;

            koishiGlitch(now, out bool hideAll, out float flicker);

            if (hideAll)
                return 0f;

            if (flicker > 0f && hash01(0, 99, (int)(now / DanmakusuBalance.KOISHI_FLICKER_STEP_MS)) < flicker)
                return 0f;

            return 1f;
        }

        /// <summary>A number in [0, 1) that is the same for the same three inputs and looks random.</summary>
        private static float hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>0..1: how far Remilia's attack has got at <paramref name="now"/> (0 outside her attacks): the colour of the bullets and of the background follows it.</summary>
        private float remiliaLevel(double now) => attackLevel(remiliaWindows, now);

        /// <summary>0..1: how far an attack that grows over its length (Remilia's, Saki's) has got at <paramref name="now"/>: it fades out smoothly after it.</summary>
        private float attackLevel((double Start, double End)[] windows, double now)
        {
            float level = 0f;

            foreach (var (start, end) in windows)
            {
                if (end <= start || now < start)
                    continue;

                if (now <= end)
                    level = Math.Max(level, (float)((now - start) / (end - start)));
                else if (now < end + DanmakusuBalance.REMILIA_FADE_OUT_MS)
                    level = Math.Max(level, 1f - smoothStep((now - end) / DanmakusuBalance.REMILIA_FADE_OUT_MS)); // (it goes back to normal smoothly, not at once)
            }

            return level;
        }

        /// <summary>Okina's shake in px at <paramref name="now"/>: 1 px while the warning before an attack is on.</summary>
        private float okinaShake(double now)
        {
            float shake = 0f;

            foreach (var act in stageActs)
            {
                if (act is PortalAct portal)
                    shake = Math.Max(shake, portal.ShakeAt(this, now));
            }

            return shake;
        }

        /// <summary>Remilia's and Saki's shake in px at <paramref name="now"/>: 2 px as an attack starts, less and less, nothing from the middle of it on.</summary>
        private float remiliaShake(double now)
        {
            float shake = 0f;

            foreach (var windows in new[] { remiliaWindows, stampedeWindows })
            {
                foreach (var (start, end) in windows)
                {
                    if (end > start && now >= start && now <= end)
                        shake = Math.Max(shake, DanmakusuBalance.REMILIA_SHAKE_PX * Math.Max(0f, 1f - 2f * (float)((now - start) / (end - start))));
                }
            }

            return shake;
        }

        /// <summary>
        /// Sakuya's knives that hang round her: one per knife of every held ring that is not thrown yet, in the order they appeared. While her preview shows (<paramref name="previewAlpha"/> above 0) every knife also has a green copy
        /// where the mirroring will put it, pointing the way it will fly.
        /// </summary>
        /// <summary>Does Sakuya's preview show a green copy at <paramref name="at"/>: only within <see cref="DanmakusuBalance.SAKUYA_PREVIEW_CIRCLES"/> circle radii of the player.</summary>
        private bool nearSakuyaPreview(Vector2 at)
        {
            float reach = circleRadius * DanmakusuBalance.SAKUYA_PREVIEW_CIRCLES;
            return Vector2.DistanceSquared(at, characterPosition) <= reach * reach;
        }

        private int addHeldKnives(double drawTime, int count, float previewAlpha)
        {
            if (failed || heldEmitters.Length == 0)
                return count;

            foreach (var held in heldEmitters)
            {
                if (drawTime >= held.Time || held.HeldOffsets == null || held.HeldFrom == null)
                    continue;

                for (int b = 0; b < held.HeldOffsets.Length; b++)
                {
                    double appeared = held.HeldFrom[b];

                    if (drawTime < appeared)
                        break; // (they appear one after the other)

                    Vector2 offset = held.HeldOffsets[b];
                    Vector2 position = bossPositionOf(held.BossRank) + offset;

                    if (hasScreenBounds && !isOnScreen(position, held.VisualRadius * 2f))
                        continue;

                    if (count == instanceBuffer.Length)
                        Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                    instanceBuffer[count++] = new BulletInstance
                    {
                        Position = position,
                        Radius = held.VisualRadius,
                        Direction = offset.LengthSquared > 1e-6f ? -Vector2.Normalize(offset) : new Vector2(1, 0), // (they point at her)
                        Shape = BulletShape.Knife,
                        Speed = held.Speed,
                        Fade = 1f - (float)Math.Clamp((drawTime - appeared) / DanmakusuBalance.SAKUYA_KNIFE_FADE_IN_MS, 0.0, 1.0),
                    };

                    if (previewAlpha > 0f && nearSakuyaPreview(fieldCentre * 2f - bossPositionOf(held.BossRank) - offset * DanmakusuBalance.SAKUYA_RELEASE_DISTANCE_FACTOR))
                    {
                        if (count == instanceBuffer.Length)
                            Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                        // (as DeriveKind.Held will put it: mirrored through the centre, further out, flying the way it hung from her)
                        var ghost = instanceBuffer[count - 1];
                        ghost.Position = fieldCentre * 2f - bossPositionOf(held.BossRank) - offset * DanmakusuBalance.SAKUYA_RELEASE_DISTANCE_FACTOR;
                        ghost.Direction = offset.LengthSquared > 1e-6f ? Vector2.Normalize(offset) : new Vector2(1, 0);
                        ghost.Overlay = BulletRenderer.TINT_GREEN;
                        ghost.OverlayAmount = 1f;
                        ghost.Fade = 1f - DanmakusuBalance.SAKUYA_PREVIEW_VISIBLE * previewAlpha * (1f - ghost.Fade);
                        instanceBuffer[count++] = ghost;
                    }
                }
            }

            return count;
        }

        /// <summary>Remilia's attack turns the background dark red, more and more until the attack ends (then it fades out smoothly), and a giant red moon shows through it.</summary>
        private void updateRemiliaBackground(double drawTime)
        {
            float level = failed ? 0f : remiliaLevel(drawTime);
            remiliaBackground.Alpha = level * DanmakusuBalance.REMILIA_BACKGROUND_ALPHA;
            remiliaMoon.Alpha = level * DanmakusuBalance.REMILIA_MOON_ALPHA;

            if (hasScreenBounds)
            {
                remiliaBackground.Position = screenMin;
                remiliaBackground.Size = screenMax - screenMin;

                float size = (screenMax.Y - screenMin.Y) * DanmakusuBalance.REMILIA_MOON_SIZE;
                remiliaMoon.Position = new Vector2((screenMin.X + screenMax.X) / 2f, screenMin.Y + (screenMax.Y - screenMin.Y) * 0.4f);
                remiliaMoon.Size = new Vector2(size);
            }
        }

        /// <summary>When Cirno freezes everything the background turns dark blue at once and fades out again.</summary>
        private void updateCirnoBackground(double drawTime)
        {
            float level = 0f;

            if (!failed)
            {
                foreach (double start in cirnoFreezes)
                {
                    double age = drawTime - start;

                    if (age < 0)
                        break;

                    if (age < DanmakusuBalance.CIRNO_FLASH_MS)
                        level = Math.Max(level, (float)Math.Min(1.0, age / DanmakusuBalance.CIRNO_FLASH_IN_MS) * (float)(1.0 - age / DanmakusuBalance.CIRNO_FLASH_MS));
                }
            }

            cirnoBackground.Alpha = level * DanmakusuBalance.CIRNO_BACKGROUND_ALPHA;

            if (hasScreenBounds)
            {
                cirnoBackground.Position = screenMin;
                cirnoBackground.Size = screenMax - screenMin;
            }
        }

        /// <summary>
        /// 0..1: how far Sakuya's time stop has come on at <paramref name="now"/> (it comes in over <see cref="DanmakusuBalance.SAKUYA_STOP_IN_MS"/>); with <paramref name="withTail"/> it also
        /// fades out over <see cref="DanmakusuBalance.SAKUYA_STOP_OUT_MS"/> after the swap. <paramref name="progress"/>: how far through the stop (0..1, the tail counts as 1).
        /// </summary>
        private float sakuyaLevel(double now, bool withTail, out float progress)
        {
            progress = 0f;

            foreach (var (start, end) in sakuyaStops)
            {
                if (now < start)
                    break;

                if (now <= end)
                {
                    progress = (float)((now - start) / (end - start));
                    return (float)Math.Min(1.0, (now - start) / DanmakusuBalance.SAKUYA_STOP_IN_MS);
                }

                if (withTail && now - end < DanmakusuBalance.SAKUYA_STOP_OUT_MS)
                {
                    progress = 1f;
                    return 1f - (float)((now - end) / DanmakusuBalance.SAKUYA_STOP_OUT_MS);
                }
            }

            return 0f;
        }

        /// <summary>While Sakuya has stopped time the background is dark brown, and an old clock very faintly appears and grows away (see also the sideways blur in <see cref="blurStopped"/>).</summary>
        private void updateSakuyaBackground(double drawTime)
        {
            float level = 0f, progress = 0f;

            if (!failed && sakuyaStops.Length > 0)
                level = sakuyaLevel(drawTime, true, out progress);

            sakuyaBackground.Alpha = level * DanmakusuBalance.SAKUYA_BACKGROUND_ALPHA;

            // the clock: starts at about the height of the screen and grows (and fades) until the time starts again
            sakuyaClock.Alpha = level * DanmakusuBalance.SAKUYA_CLOCK_ALPHA * (1f - progress);

            if (hasScreenBounds)
            {
                sakuyaBackground.Position = screenMin;
                sakuyaBackground.Size = screenMax - screenMin;

                float height = screenMax.Y - screenMin.Y;
                float size = height * (DanmakusuBalance.SAKUYA_CLOCK_START_SIZE + (DanmakusuBalance.SAKUYA_CLOCK_END_SIZE - DanmakusuBalance.SAKUYA_CLOCK_START_SIZE) * progress);
                sakuyaClock.Position = (screenMin + screenMax) / 2;
                sakuyaClock.Size = new Vector2(size);
            }
        }

        /// <summary>Reisen's distance for the slide of what she has frozen: three osu! circles (diameters).</summary>
        private static float reisenDistance(float circleRadius) => DanmakusuBalance.REISEN_SLIDE_CIRCLES * 2f * circleRadius;

        /// <summary>
        /// 0..1: how far Reisen's freeze has come on at <paramref name="now"/>. For what is frozen (<paramref name="background"/> false) it comes in over <see cref="DanmakusuBalance.REISEN_IN_MS"/>
        /// and goes in the same time before the attack ends; for the background it stays until the attack ends and then fades out. <paramref name="slide"/>: how far what is frozen has slid (0..1, eased).
        /// </summary>
        private float reisenLevel(double now, bool background, out float slide)
        {
            slide = 0f;

            foreach (var (start, end) in reisenWindows)
            {
                if (now < start)
                    break;

                if (now <= end)
                {
                    slide = smoothStep((now - start) / (end - start));
                    float comingIn = (float)Math.Min(1.0, (now - start) / DanmakusuBalance.REISEN_IN_MS);
                    return background ? comingIn : Math.Min(comingIn, (float)Math.Min(1.0, (end - now) / DanmakusuBalance.REISEN_IN_MS));
                }

                if (background && now < end + DanmakusuBalance.REISEN_BACKGROUND_OUT_MS)
                {
                    slide = 1f;
                    return 1f - smoothStep((now - end) / DanmakusuBalance.REISEN_BACKGROUND_OUT_MS);
                }
            }

            return 0f;
        }

        /// <summary>While Reisen's attack freezes everything the background is a dim purple; it fades out once what was frozen has slid away.</summary>
        private void updateReisenBackground(double drawTime)
        {
            float level = failed || reisenWindows.Length == 0 ? 0f : reisenLevel(drawTime, true, out _);
            reisenBackground.Alpha = level * DanmakusuBalance.REISEN_BACKGROUND_ALPHA;

            if (hasScreenBounds)
            {
                reisenBackground.Position = screenMin;
                reisenBackground.Size = screenMax - screenMin;
            }
        }

        private static readonly float[] blur_offsets = { -2.5f, -1.25f, 1.25f, 2.5f };
        private static readonly float[] blur_strengths = { 0.18f, 0.35f, 0.35f, 0.18f };

        private readonly List<(int Index, float Strength)> blurred = new List<(int, float)>();

        /// <summary>
        /// While Sakuya's time is stopped everything that is drawn as an instance gets a faint copy to either side (slight motion blur sideways). The copies go first, so that
        /// the real things are above them. Sakuya's blur is only round the player (<see cref="DanmakusuBalance.SAKUYA_BLUR_CIRCLES"/>, fading out at its edge); Reisen's freeze blurs the whole screen.
        /// </summary>
        private int blurStopped(double drawTime, int count)
        {
            if (failed || count == 0 || (sakuyaStops.Length == 0 && reisenWindows.Length == 0))
                return count;

            float sakuya = sakuyaStops.Length > 0 ? sakuyaLevel(drawTime, false, out _) : 0f;
            float reisen = reisenWindows.Length > 0 ? reisenLevel(drawTime, false, out _) : 0f;
            float level = Math.Max(sakuya, reisen);

            if (level <= 0f)
                return count;

            // (what gets copies, and how strongly: within Sakuya's reach of the player, less and less over its last circle radius)
            bool nearOnly = reisen <= 0f;
            float reach = circleRadius * DanmakusuBalance.SAKUYA_BLUR_CIRCLES;
            float soft = Math.Min(circleRadius, reach);
            blurred.Clear();

            for (int i = 0; i < count; i++)
            {
                float strength = level;

                if (nearOnly)
                {
                    float distance = Vector2.Distance(instanceBuffer[i].Position, characterPosition);

                    if (distance >= reach)
                        continue;

                    strength *= 1f - smoothStep((distance - (reach - soft)) / soft);
                }

                if (strength > 0.001f)
                    blurred.Add((i, strength));
            }

            int ghosts = blur_offsets.Length;
            int copies = blurred.Count * ghosts;

            if (copies == 0)
                return count;

            int total = count + copies;

            while (instanceBuffer.Length < total)
                Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

            Array.Copy(instanceBuffer, 0, instanceBuffer, copies, count);

            for (int g = 0; g < ghosts; g++)
            {
                for (int j = 0; j < blurred.Count; j++)
                {
                    var (index, strength) = blurred[j];
                    var copy = instanceBuffer[copies + index];
                    copy.Position += new Vector2(blur_offsets[g] * strength, 0f);
                    copy.Fade = 1f - (1f - copy.Fade) * blur_strengths[g] * strength;
                    instanceBuffer[g * blurred.Count + j] = copy;
                }
            }

            return total;
        }
    }
}
