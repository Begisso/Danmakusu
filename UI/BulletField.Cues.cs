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
        // ---- sound cues: a sample that is played once at a moment (or so that it ends at that moment) ----

        private sealed class Cue
        {
            public double Time;
            public string Name = "";

            /// <summary>The sample ends at <see cref="Time"/> (it starts its own length before that) instead of starting there.</summary>
            public bool EndAligned;

            public bool Played;
        }

        private Cue[] cues = [];

        /// <summary>
        /// The moments that had a warning before them (an attack that is not the start of a boss's section) or a reversed warning ending at them: the warning sounds and stars are only there when a boss appears now,
        /// and over the warning's length before each of these every bullet flashes instead (its glow pulses, faster and brighter towards the moment). Sorted.
        /// </summary>
        private readonly List<double> flashTimes = new List<double>();

        /// <summary>How much every bullet glows at <paramref name="t"/> because of a coming flash moment (0 = none).</summary>
        private float flashLevel(double t)
        {
            double length = DanmakusuBalance.POWER_UP_LENGTH_MS;
            int lo = 0, hi = flashTimes.Count;

            // (the first moment that is not over yet)
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (flashTimes[mid] < t)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            float level = 0f;

            for (int k = lo; k < flashTimes.Count && flashTimes[k] - length <= t; k++)
                level = Math.Max(level, flashAt(t, flashTimes[k], length));

            foreach (double end in actFlashTimes)
            {
                if (t >= end - length && t <= end)
                    level = Math.Max(level, flashAt(t, end, length));
            }

            return level;
        }

        private static float flashAt(double t, double end, double length)
        {
            double u = (t - (end - length)) / length;
            float pulse = 0.5f - 0.5f * MathF.Cos((float)(DanmakusuBalance.ATTACK_FLASHES * u * u * Math.PI * 2));
            return DanmakusuBalance.ATTACK_FLASH_BLOOM * pulse * (0.35f + 0.65f * (float)u);
        }

        /// <summary>The moments the acts warn about (given again every frame: Mokou's jumps), which the bullets flash before.</summary>
        private readonly List<double> actFlashTimes = new List<double>();
        private readonly Dictionary<string, Sample?> cueSamples = new Dictionary<string, Sample?>();
        private static readonly string[] cue_names = { "freeze", "cirno_reverse", "cirno_unfreeze", "chen_cat", "kagerou_howl", "kagerou_swing_0", "kagerou_swing_1", "kagerou_swing_2", "kagerou_swing_3", "kagerou_swing_4", "kagerou_swing_5", "kagerou_swing_6", "kagerou_swing_7", "kagerou_swing_8", "kagerou_swing_9", "elly_swing", "hecatia_cut", "hecatia_explosion", "koishi_ring", "koishi_glitch_in", "koishi_glitch_out", "hecatia_earth_hit", "hecatia_moon_hit", "hecatia_world_hit", "mokou_wall_hit", "power_up", "kyouko_walls", "kyouko_walls_reverse", "taiko_slam", "taiko_stick", "nuclear_alarm", "ufo_suck", "ufo_shoot", "train_horn", "yukari_warning", "yukari_reverse", "sakuya_stop", "sakuya_swap", "warning_reverse", "yuyuko_tree", "yuyuko_tree_loop", "yuyuko_petals", "reimu_orb_throw", "remilia_bell", "zanmu_graveyard", "zanmu_impact", "utsuho_hole_loop", "gengetsu_riser", "tojiko_thunder", "yumemi_bomb", "touhou_strike", "mima_moon", "mima_moon_active", "fly_by_0", "fly_by_1", "fly_by_2", "fly_by_3", "fly_by_4", "fly_by_5", "fly_by_6", "fly_by_7", "fly_by_8", "fly_by_9", "fly_by_10", "fly_by_11", "murasa_anchor" };

        /// <summary>Cues that sound exactly like another sample share its file (Yukari's warning is the power-up, her reverse the one of Cirno, Elly's scythe the lighting up of Mima's moon).</summary>
        private static readonly Dictionary<string, string> cue_sample_aliases = new Dictionary<string, string>
        {
            ["yukari_warning"] = "power_up",
            ["yukari_reverse"] = "cirno_reverse",
            ["elly_swing"] = "mima_moon_active",
        };

        private void buildCues()
        {
            var list = new List<Cue>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.Freeze)
                {
                    foreach (var (start, end) in BossOps.Attacks(period))
                    {
                        // with nothing on the screen there is nothing to freeze, so there is no freezing and no thawing (the thaw is the burst of what was frozen)
                        bool bursts = false;

                        foreach (var ring in emitters)
                        {
                            if (ring.Derive == DeriveKind.Explode && Math.Abs(ring.Time - end) < 1)
                            {
                                bursts = true;
                                break;
                            }
                        }

                        if (!bursts)
                            continue;

                        list.Add(new Cue { Time = start, Name = "freeze" }); // (everything freezes)
                        list.Add(new Cue { Time = end, Name = "cirno_reverse", EndAligned = true }); // (the reversed warning: it ends where everything bursts)
                        list.Add(new Cue { Time = end, Name = "cirno_unfreeze" }); // (everything thaws and bursts)
                    }
                }
            }

            // Remilia: the church bell strikes when each of her attacks starts
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.FateSpear)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                        list.Add(new Cue { Time = start, Name = "remilia_bell" });
                }
            }

            // Yukari: a warning that ends where everything freezes (the pause starts), and the reversed warning that ends where it is let go (the pause ends)
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Gaps)
                    continue;

                foreach (var (start, end) in BossOps.PausesWithTail(period))
                {
                    list.Add(new Cue { Time = start, Name = "yukari_warning", EndAligned = true });
                    list.Add(new Cue { Time = end, Name = "yukari_reverse", EndAligned = true });
                }
            }

            // Hecatia: the reversed warning ends where the worlds start to turn round her (every second attack; the others throw the Earth and the Moon)
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ThreeWorlds)
                    continue;

                var hecatiaAttacks = BossOps.Attacks(period);

                for (int i = 1; i < hecatiaAttacks.Count; i += 2)
                {
                    if (hecatiaAttacks[i].End > hecatiaAttacks[i].Start)
                        list.Add(new Cue { Time = hecatiaAttacks[i].Start, Name = "yukari_reverse", EndAligned = true });
                }
            }

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == BossMechanic.Shikigami)
                {
                    foreach (var (start, _) in BossOps.Attacks(period))
                        list.Add(new Cue { Time = start, Name = "chen_cat" }); // (she is charged: the cat cries once)
                }
                else if (period.Boss?.Mechanic == BossMechanic.Unconscious)
                {
                    foreach (var (time, beat) in koishiMarks)
                    {
                        if (time < period.ShootStart - 1 || time > period.End)
                            continue;

                        list.Add(new Cue { Time = time, Name = "koishi_glitch_in" }); // (the first half of the glitch sound: everything glitches away)
                        list.Add(new Cue { Time = time + beat * DanmakusuBalance.KOISHI_HIDE_BEATS, Name = "koishi_glitch_out" }); // (the second half: it glitches back)
                    }
                }
            }

            // Nue: Kyouko's wall sound at the start of an attack and when the UFOs start to rearrange; Raiko: every slam; Utsuho: the nuclear alarm at every attack
            foreach (var act in ufoActs)
            {
                // (her own sounds are gone but the warning: Kyouko's wall sound plays when the attack begins and when the UFOs begin to gather for the shot)
                list.Add(new Cue { Time = act.Start, Name = "kyouko_walls" });
                list.Add(new Cue { Time = act.End, Name = "kyouko_walls" });
            }

            foreach (var act in taikoActs)
            {
                for (int k = 0; k < act.Slams; k++)
                    list.Add(new Cue { Time = act.HitTime(k), Name = act.IsStick(k) ? "taiko_stick" : "taiko_slam" }); // (every fourth hit is the sticks)
            }

            // Yukari: the train's horn. The sound is a horn that falls in pitch as the train rushes past (the passing is TRAIN_HORN_PASS_MS into it), so it
            // starts early enough for the passing to be when the train reaches the player.
            foreach (var emitter in emitters)
            {
                if (emitter.Derive != DeriveKind.Train || emitter.Speed <= 0)
                    continue;

                double arrives = emitter.Time + (osu.Game.Rulesets.Osu.UI.OsuPlayfield.BASE_SIZE.Length + emitter.HitRadius * 2f) / emitter.Speed;
                list.Add(new Cue { Time = arrives - DanmakusuBalance.TRAIN_HORN_PASS_MS, Name = "train_horn" });
            }

            // Kyouko: a sound when the walls start closing, and the same backwards before they go
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Echo)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                    {
                        list.Add(new Cue { Time = start, Name = "kyouko_walls" });
                        list.Add(new Cue { Time = end, Name = "kyouko_walls_reverse", EndAligned = true }); // (the same sound backwards, ending where the walls go)
                    }
                }
            }

            // Yuyuko: a sound when the tree starts to grow (the steady part of it is looped for as long as the tree stands, see updateTreeSound)
            var treeStarts = new List<double>();

            foreach (var (start, end) in BossOps.TreeWindows(bosses))
            {
                treeStarts.Add(start);
                list.Add(new Cue { Time = start, Name = "yuyuko_tree" });
            }

            // ...and again when the petals grow on it in the next attack; and a sound every time the petals fly away
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ButterflyFans)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end <= start)
                        continue;

                    if (!treeStarts.Exists(treeStart => Math.Abs(treeStart - start) < 50))
                        list.Add(new Cue { Time = start, Name = "yuyuko_tree" });

                    list.Add(new Cue { Time = end, Name = "yuyuko_petals" });
                }
            }

            // Sannyo: the sound of Kyouko's walls appearing at every throw of smoke
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Smoke)
                    continue;

                if (period.End > period.ShootStart)
                {
                    foreach (double time in SmokeAct.RingTimes(period, period.ShootStart, period.End))
                        list.Add(new Cue { Time = time, Name = "kyouko_walls" });
                }
            }

            // Sukuna: Kyouko's wall sound every time an attack starts
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Inchling)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                        list.Add(new Cue { Time = start, Name = "kyouko_walls" });
                }
            }

            // Rumia: the same sound at every throw of her circles
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Darkness)
                    continue;

                if (period.End > period.ShootStart)
                {
                    foreach (double time in RumiaAct.RingTimes(period, period.ShootStart, period.End))
                        list.Add(new Cue { Time = time, Name = "kyouko_walls" });
                }
            }

            // Nazrin: the petals sound every time she turns the bullets round her into arrowheads
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Dowsing)
                    continue;

                foreach (double mark in BossOps.ConversionMarks(period))
                    list.Add(new Cue { Time = mark, Name = "yuyuko_petals" });
            }

            // Sekibanki: the sound of Yuyuko's petals every time she throws a head
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Heads)
                    continue;

                foreach (double time in HeadAct.ThrowTimes(period))
                    list.Add(new Cue { Time = time, Name = "yuyuko_petals" });
            }

            // Zanmu: an impact at every ripple
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Death)
                    continue;

                foreach (double mark in BossOps.RippleMarks(period))
                    list.Add(new Cue { Time = mark, Name = "zanmu_impact" });
            }

            // Reisen: the sound of the petals that Yuyuko's tree throws when everything freezes, and again when it is set free (only in the attacks she slides in)
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Lunatic)
                    continue;

                foreach (var (start, end) in BossOps.ReisenSlides(period))
                {
                    list.Add(new Cue { Time = start, Name = "yuyuko_petals" });
                    list.Add(new Cue { Time = end, Name = "yuyuko_petals" });
                }
            }

            // Sakuya: the time stop sounds when everything stops, the reversed warning ends where everything is swapped, and a sound plays at the swap
            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.TimeStop)
                    continue;

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    if (end > start)
                    {
                        list.Add(new Cue { Time = start, Name = "sakuya_stop" });
                        list.Add(new Cue { Time = end, Name = "warning_reverse", EndAligned = true }); // (the warning sound backwards, ending where everything is swapped)
                        list.Add(new Cue { Time = end, Name = "sakuya_swap" }); // (and the swap itself)
                    }
                }
            }

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.Nuclear)
                    continue;

                foreach (var (start, _) in BossOps.Attacks(period))
                    list.Add(new Cue { Time = start, Name = "nuclear_alarm", EndAligned = true });
            }

            // every ring of Utsuho's big balls is shot with Mokou's impact sound (once for the rings that are shot at the same moment)
            var bigShots = new SortedSet<double>();

            foreach (var emitter in emitters)
            {
                if (emitter.Sharp && !emitter.Derived)
                    bigShots.Add(Math.Round(emitter.Time));
            }

            foreach (double at in bigShots)
                list.Add(new Cue { Time = at, Name = "mokou_wall_hit" });

            bigShotTimes = bigShots.ToArray(); // (they shake the screen too, see stageShake)

            // (the reversed warnings and Yukari's warning are gone: where one ended the bullets flash instead, see flashLevel)
            static bool isWarning(Cue cue) => cue.Name.EndsWith("_reverse", StringComparison.Ordinal) || cue.Name == "yukari_warning";

            foreach (var cue in list)
            {
                if (isWarning(cue))
                    flashTimes.Add(cue.Time);
            }

            list.RemoveAll(isWarning);
            flashTimes.Sort();

            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            cues = list.ToArray();
        }

        private SampleChannel? treeLoopChannel;

        /// <summary>The quiet part of the tree's sound is looped from when the first part is over until the tree is gone.</summary>
        private void updateTreeSound(double now)
        {
            bool wanted = false;

            if (!failed && soundStillFrames < 8 && cueSamples.TryGetValue("yuyuko_tree_loop", out var loop) && loop != null)
            {
                double intro = cueSamples.TryGetValue("yuyuko_tree", out var first) && first != null && first.Length > 0 ? first.Length : 4000;

                foreach (var tree in treeActs)
                {
                    if (now >= tree.Start + intro - 100 && now < tree.Gone)
                    {
                        wanted = true;
                        break;
                    }
                }

                if (wanted && treeLoopChannel == null)
                {
                    treeLoopChannel = loop.GetChannel();
                    treeLoopChannel.Looping = true;
                    treeLoopChannel.Volume.Value = 1.0;
                    treeLoopChannel.Play();
                }
            }

            if (!wanted && treeLoopChannel != null)
            {
                treeLoopChannel.Stop();
                treeLoopChannel = null;
            }
        }

        private SampleChannel? holeLoopChannel;

        /// <summary>Utsuho: the black hole's music is played (looped) from when everything starts to slow down before a hole until that hole is gone.</summary>
        private void updateHoleSound(double now)
        {
            bool wanted = false;

            if (!failed && soundStillFrames < 8 && holeActs.Count > 0 && cueSamples.TryGetValue("utsuho_hole_loop", out var loop) && loop != null)
            {
                foreach (var hole in holeActs)
                {
                    if (now >= hole.SoundFrom && now < hole.End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    {
                        wanted = true;
                        break;
                    }
                }

                if (wanted && holeLoopChannel == null)
                {
                    holeLoopChannel = loop.GetChannel();
                    holeLoopChannel.Looping = true;
                    holeLoopChannel.Volume.Value = 1.0;
                    holeLoopChannel.Play();
                }
            }

            if (!wanted && holeLoopChannel != null)
            {
                holeLoopChannel.Stop();
                holeLoopChannel = null;
            }
        }

        private SampleChannel? deathLoopChannel;

        /// <summary>Zanmu: the graveyard ambience is played, an octave lower, for as long as her section lasts (looped).</summary>
        private void updateDeathSound(double now)
        {
            bool wanted = false;

            if (!failed && soundStillFrames < 8 && cueSamples.TryGetValue("zanmu_graveyard", out var loop) && loop != null)
            {
                foreach (var act in stageActs)
                {
                    if (act is DeathAct && now >= act.Start && now < act.End)
                    {
                        wanted = true;
                        break;
                    }
                }

                if (wanted && deathLoopChannel == null)
                {
                    deathLoopChannel = loop.GetChannel();
                    deathLoopChannel.Looping = true;
                    deathLoopChannel.Frequency.Value = 0.5; // (an octave down)
                    deathLoopChannel.Volume.Value = 1.0;
                    deathLoopChannel.Play();
                }
            }

            if (!wanted && deathLoopChannel != null)
            {
                deathLoopChannel.Stop();
                deathLoopChannel = null;
            }
        }

        private void updateCues(double now)
        {
            foreach (var cue in cues)
            {
                if (!cueSamples.TryGetValue(cue.Name, out var sample) || sample == null)
                    continue;

                double length = sample.Length > 0 ? sample.Length : DanmakusuBalance.POWER_UP_LENGTH_MS;
                double lead = cue.EndAligned ? cue.Time - length : cue.Time;

                if (now < lead)
                {
                    cue.Played = false; // (time went back, or it is not time yet)
                    continue;
                }

                if (cue.Played)
                    continue;

                cue.Played = true;

                // only if we are (nearly) at the right moment: after a seek it is simply already over
                if (now - lead < 250 && (!cue.EndAligned || now < cue.Time) && !failed && soundStillFrames < 8)
                {
                    // (only one of Utsuho's alarms at a time: one that comes while the last is still sounding is left out)
                    bool alarm = cue.Name == "nuclear_alarm";

                    if (alarm && alarmChannel is { Playing: true })
                        continue;

                    var channel = sample.GetChannel();
                    channel.Volume.Value = 1.0;
                    channel.Play();

                    if (alarm)
                        alarmChannel = channel;
                }
            }
        }

        private SampleChannel? alarmChannel;
    }
}
