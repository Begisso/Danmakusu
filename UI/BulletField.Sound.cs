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
        /// <summary>Plays the spinner laser sound (looped) while a spinner laser is firing and the game clock is running; stops it otherwise.</summary>
        private void updateMasterSpark(double now)
        {
            // the clock does not move while the game is paused
            bool moving = double.IsNaN(soundTime) || Math.Abs(now - soundTime) > 0.01;
            soundStillFrames = moving ? 0 : soundStillFrames + 1;
            soundTime = now;

            updatePowerUp(now);

            if (masterSpark == null)
                return;

            bool firing = false;
            bool otherLaser = false;

            if (!failed && soundStillFrames < 8)
            {
                foreach (int index in spinnerLasers)
                {
                    var laser = lasers[index];

                    if (laser.KeepsOrigin || laser.Silent)
                        continue; // (Reisen's short beams and Byakuren's make no beam sound)

                    if (now >= laser.StartTime && now <= laser.EndTime)
                    {
                        firing = true;
                        otherLaser = true;
                        break;
                    }
                }

                // (Clownpiece's moon laser has no sound of its own and does not use the beam sound either)
            }

            bool active = masterSparkStartChannel != null || masterSparkChannel != null;
            bool silent = failed || soundStillFrames >= 8; // (paused or failed: the sound is just cut, it does not play its end)

            if (firing && !active)
            {
                // the start of the sound, then (when it is over) the steady part in a loop
                sparkGain = otherLaser ? 1.0 : DanmakusuBalance.MOON_SOUND_VOLUME; // (only Clownpiece's moon laser is quieter: 50%)
                masterSparkStartChannel = masterSparkStart?.GetChannel();

                if (masterSparkStartChannel != null)
                {
                    masterSparkStartChannel.Volume.Value = sparkGain; // (a channel cannot go above 100%: the files themselves were made louder)
                    masterSparkStartChannel.Play();
                    masterSparkStartTimestamp = Stopwatch.GetTimestamp();
                }
                else
                    startMasterSparkLoop();
            }
            else if (firing && masterSparkStartChannel != null)
            {
                double elapsed = (Stopwatch.GetTimestamp() - masterSparkStartTimestamp) * 1000.0 / Stopwatch.Frequency;
                double length = masterSparkStart!.Length > 0 ? masterSparkStart.Length : 2300;

                if (!masterSparkStartChannel.Playing || elapsed >= length - sound_fade_ms)
                {
                    // the loop comes in a little before the start is over; the start fades out under it
                    fadingChannels.Add((masterSparkStartChannel, Stopwatch.GetTimestamp(), sparkGain, sound_fade_ms));
                    masterSparkStartChannel = null;
                    startMasterSparkLoop(true);
                }
            }
            else if (!firing && active)
            {
                stopMasterSpark(!silent);
            }

            if (masterSparkChannel != null && masterSparkLoopFadeInTimestamp != 0)
            {
                double fadeIn = (Stopwatch.GetTimestamp() - masterSparkLoopFadeInTimestamp) * 1000.0 / Stopwatch.Frequency / sound_fade_ms;

                masterSparkChannel.Volume.Value = Math.Min(1.0, fadeIn) * sparkGain;

                if (fadeIn >= 1)
                    masterSparkLoopFadeInTimestamp = 0;
            }

            updateFadingChannels();
        }

        private void startMasterSparkLoop(bool fadeIn = false)
        {
            masterSparkChannel = masterSpark!.GetChannel();
            masterSparkChannel.Looping = true;
            masterSparkChannel.Volume.Value = fadeIn ? 0 : sparkGain;
            masterSparkChannel.Play();
            masterSparkLoopFadeInTimestamp = fadeIn ? Stopwatch.GetTimestamp() : 0;
        }

        /// <summary>Fades the channels that are being stopped out in a few milliseconds (so the cut does not click) and stops them.</summary>
        private void updateFadingChannels()
        {
            for (int i = fadingChannels.Count - 1; i >= 0; i--)
            {
                var (channel, timestamp, gain, fadeMs) = fadingChannels[i];
                double elapsed = (Stopwatch.GetTimestamp() - timestamp) * 1000.0 / Stopwatch.Frequency;

                if (elapsed >= fadeMs)
                {
                    channel.Stop();
                    fadingChannels.RemoveAt(i);
                }
                else
                    channel.Volume.Value = (1.0 - elapsed / fadeMs) * gain;
            }
        }

        /// <summary>Seija's reversal makes a short sound when the screen turns over and when it returns (once each; not after a seek, not while paused).</summary>
        private void updateMirrorSound(double now)
        {
            if (mirrorFlipSound == null || mirrorWindows.Length == 0)
                return;

            if (mirrorFlipPlayed.Length != mirrorWindows.Length * 2)
                mirrorFlipPlayed = new bool[mirrorWindows.Length * 2];

            for (int i = 0; i < mirrorWindows.Length; i++)
            {
                for (int edge = 0; edge < 2; edge++)
                {
                    double at = edge == 0 ? mirrorWindows[i].Start : mirrorWindows[i].End;
                    int index = i * 2 + edge;

                    if (now < at)
                    {
                        mirrorFlipPlayed[index] = false; // (time went back, or it is not time yet)
                        continue;
                    }

                    if (mirrorFlipPlayed[index])
                        continue;

                    mirrorFlipPlayed[index] = true;

                    if (now - at < 250 && !failed && soundStillFrames < 8)
                    {
                        var channel = mirrorFlipSound.GetChannel();
                        channel.Volume.Value = 1.0;
                        channel.Play();
                    }
                }
            }
        }

        /// <summary>
        /// The boss of the last section explodes when that section ends: plays the explosion sound at that moment (once; not after a seek, not while paused). With two bosses there are two last sections, but only one
        /// explosion is heard: when the first of them ends.
        /// </summary>
        private void updateExplosionSound(double now)
        {
            if (explosionSound == null)
                return;

            double end = double.MaxValue;

            foreach (var period in bosses)
            {
                if (period.IsLast)
                    end = Math.Min(end, period.End);
            }

            if (end == double.MaxValue || now < end)
                explosionPlayed = false; // (time went back, or it is not time yet)
            else if (!explosionPlayed)
            {
                explosionPlayed = true;

                if (now - end < 250 && !failed && soundStillFrames < 8)
                {
                    var channel = explosionSound.GetChannel();
                    channel.Volume.Value = 1.0;
                    channel.Play();
                }
            }
        }

        /// <summary>Plays the power-up so that it ends right when a spinner / boss laser starts (it starts <c>length</c> before that).</summary>
        private void updatePowerUp(double now)
        {
            if (powerUp == null)
                return;

            if (powerUpStarted.Length != warningTimes.Length)
                powerUpStarted = new bool[warningTimes.Length];

            if (failed || soundStillFrames >= 8)
            {
                powerUpChannel?.Stop();
                powerUpChannel = null;
                return;
            }

            for (int k = 0; k < warningTimes.Length; k++)
            {
                Sample warning = powerUp;

                if (warningNames[k] != "power_up" && cueSamples.TryGetValue(warningNames[k], out var own) && own != null)
                    warning = own;

                double length = warning.Length > 0 ? warning.Length : DanmakusuBalance.POWER_UP_LENGTH_MS;
                double start = warningTimes[k];
                double lead = start - length;

                if (now < lead)
                {
                    powerUpStarted[k] = false; // (time went back, or it is not time yet)
                    continue;
                }

                if (powerUpStarted[k])
                    continue;

                powerUpStarted[k] = true;

                // only if we are (nearly) at the right moment: after a seek the laser is simply already under way
                if (now < start && now - lead < 250)
                {
                    powerUpChannel = warning.GetChannel();
                    powerUpChannel.Volume.Value = 1.0; // (was 0.8: 50% higher would be 1.2, but a channel cannot go above 100%)
                    powerUpChannel.Play();
                }
            }
        }

        /// <summary>Stops the beam sound: with <paramref name="playEnd"/> its ending is played (the rest fades out under it), otherwise it is just cut.</summary>
        private void stopMasterSpark(bool playEnd = false)
        {
            foreach (var channel in new[] { masterSparkStartChannel, masterSparkChannel })
            {
                if (channel == null)
                    continue;

                if (playEnd)
                    fadingChannels.Add((channel, Stopwatch.GetTimestamp(), sparkGain, sound_fade_ms));
                else
                    channel.Stop();
            }

            if (playEnd && masterSparkEnd != null)
            {
                var end = masterSparkEnd.GetChannel();
                end.Volume.Value = sparkGain;
                end.Play();
            }

            masterSparkStartChannel = null;
            masterSparkChannel = null;
            masterSparkLoopFadeInTimestamp = 0;
        }
    }
}
