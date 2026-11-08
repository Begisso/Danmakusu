using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// What the mechanics of the bosses do to the whole field, worked out once from the boss sections of a map:
    ///  - windows in which all bullets fly slower (bullets are pure functions of their age, so a slowdown is a "warped" clock: <see cref="Effective"/> says how
    ///    old a bullet is when the speeds were halved for part of its life, and bullets that are already out speed up again when the window is over);
    ///  - windows in which a flashlight is on (<see cref="FlashlightAt"/>), with the radius of what the player can see.
    /// Mystia (<see cref="BossMechanic.Blindness"/>) is the first: every attack of hers (a laser burst's time - or a whole spinner) halves all bullet speeds and turns
    /// the flashlight on; the pauses between the attacks restore both. In a kiai section the circle of sight is 4x the radius of an osu! circle for the first attack,
    /// then 3x, then 2x, then 4x again and so on; in a break or a spinner it is always 4x, and such a section starts the kiai count over.
    /// </summary>
    public sealed class BossEffects
    {
        /// <summary>The speed of every bullet during a window (1 = normal).</summary>
        public const double SLOWED_SPEED = 0.5;

        /// <summary>The flashlight fades in and out this long (ms).</summary>
        public const double FLASHLIGHT_FADE_MS = 300;

        /// <summary>The radius of sight, in radii of an osu! circle, of the attacks of a kiai section (they take turns) and of everything else.</summary>
        private static readonly float[] kiai_sight = { 4f, 3f, 2f };

        private const float other_sight = 4f;

        public static readonly BossEffects None = new BossEffects(new List<(double, double, float, double, double)>());

        private readonly double[] starts;
        private readonly double[] ends;
        private readonly float[] sights; // (0: no flashlight in this window, only the speed)
        private readonly double[] speedsFrom, speedsTo; // (the speed of every bullet goes evenly from the first to the second over the window)
        private readonly double[] removedBefore; // the time that the windows before window i took away in total (negative where they sped the bullets up)

        private BossEffects(List<(double start, double end, float sight, double speedFrom, double speedTo)> windows)
        {
            windows.Sort((a, b) => a.start.CompareTo(b.start));

            var s = new List<double>();
            var e = new List<double>();
            var f = new List<float>();
            var a = new List<double>();
            var b = new List<double>();
            var removed = new List<double>();
            double total = 0;
            double previousEnd = double.NegativeInfinity;

            foreach (var (start, end, sight, speedFrom, speedTo) in windows)
            {
                double from = Math.Max(start, previousEnd);

                if (end <= from)
                    continue;

                s.Add(from);
                e.Add(end);
                f.Add(sight);
                a.Add(speedFrom);
                b.Add(speedTo);
                removed.Add(total);
                total += (end - from) * (1 - (speedFrom + speedTo) / 2);
                previousEnd = end;
            }

            starts = s.ToArray();
            ends = e.ToArray();
            sights = f.ToArray();
            speedsFrom = a.ToArray();
            speedsTo = b.ToArray();
            removedBefore = removed.ToArray();
        }

        /// <summary>Is there any window in which the bullets are slower (or faster)?</summary>
        public bool Any => starts.Length > 0;

        /// <summary>The "clock" the bullets use: it runs at normal speed, and inside a window at the window's speed (<see cref="SLOWED_SPEED"/> for Mystia; for Gengetsu from 1 up to 2).</summary>
        public double Warp(double time)
        {
            int index = windowAtOrBefore(time);

            if (index < 0)
                return time;

            double length = Math.Max(ends[index] - starts[index], 1e-6);
            double inside = Math.Clamp(time, starts[index], ends[index]) - starts[index];
            double lived = speedsFrom[index] * inside + (speedsTo[index] - speedsFrom[index]) * inside * inside / (2 * length); // (the integral of the speed over the part of the window so far)
            return time - removedBefore[index] - (inside - lived);
        }

        /// <summary>The time a bullet that was fired at <paramref name="spawnTime"/> has lived through at <paramref name="now"/>, as a point in time (spawn time + its warped age).</summary>
        public double Effective(double spawnTime, double now)
        {
            if (starts.Length == 0 || now <= spawnTime)
                return now;

            return spawnTime + (Warp(now) - Warp(spawnTime));
        }

        /// <summary>
        /// The time a ring's bullets have lived through at <paramref name="now"/> (see <see cref="Effective(double, double)"/>). Utsuho's big balls (<see cref="BulletEmitter.Sharp"/>) are not stopped
        /// before her black hole: their clock has every window but those.
        /// </summary>
        public double Effective(BulletEmitter emitter, double now)
            => emitter.Shape == Bullets.BulletShape.Train ? now // (Yukari's trains go by real time: no boss's clock - Sakuya's time stop, Gengetsu's haste, Utsuho's slowing - stops or speeds them)
                : emitter.Sharp && unstopped != null ? unstopped.Effective(emitter.Time, now) : Effective(emitter.Time, now);

        /// <summary>The same windows without Utsuho's stops (null when there are none of hers).</summary>
        private BossEffects? unstopped;

        /// <summary>Is a flashlight on at <paramref name="now"/>? <paramref name="sight"/> = the radius of sight in osu! circle radii, <paramref name="strength"/> = 0..1 (it fades).</summary>
        public bool FlashlightAt(double now, out float sight, out float strength)
        {
            sight = 0;
            strength = 0;

            int index = windowAtOrBefore(now);

            // (a window without a flashlight - Gengetsu's - hides the one before it: they do not overlap, so that is only its fade-out)
            if (index < 0 || now > ends[index] + FLASHLIGHT_FADE_MS || sights[index] <= 0f)
                return false;

            double fadeIn = (now - starts[index]) / FLASHLIGHT_FADE_MS;
            double fadeOut = (ends[index] + FLASHLIGHT_FADE_MS - now) / FLASHLIGHT_FADE_MS;
            strength = (float)Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
            sight = sights[index];
            return strength > 0;
        }

        private int windowAtOrBefore(double time)
        {
            int lo = 0, hi = starts.Length - 1, found = -1;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;

                if (starts[mid] <= time)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }

            return found;
        }

        public static BossEffects Build(IEnumerable<BossPeriod> bosses)
        {
            var windows = new List<(double start, double end, float sight, double speedFrom, double speedTo)>();
            var stops = new List<(double start, double end, float sight, double speedFrom, double speedTo)>(); // (Utsuho's: her big balls do not stop)
            int kiaiAttacks = 0;

            foreach (var period in bosses.OrderBy(p => p.ShootStart))
            {
                // Gengetsu: in every attack of hers every bullet on the screen speeds up evenly from its own speed to twice it; when the attack is over they are as fast as they were
                if (period.Boss?.Mechanic == BossMechanic.Haste)
                {
                    foreach (var (start, end) in BossOps.Attacks(period))
                    {
                        if (end > start)
                            windows.Add((start, end, 0f, 1.0, DanmakusuBalance.GENGETSU_SPEED_UP));
                    }

                    continue;
                }

                // Saki: in every attack of hers every bullet on the screen speeds up evenly from its own speed to 1.2 times it, and over the pause after it slows down evenly to its own speed again
                if (period.Boss?.Mechanic == BossMechanic.Stampede)
                {
                    var hers = BossOps.Attacks(period).Where(attack => attack.End > attack.Start).ToList();

                    for (int i = 0; i < hers.Count; i++)
                    {
                        double slowed = i + 1 < hers.Count ? hers[i + 1].Start : hers[i].End + Math.Max(period.BeatLength, 1) * DanmakusuBalance.BOSS_LASER_PAUSE_BEATS;

                        windows.Add((hers[i].Start, hers[i].End, 0f, 1.0, DanmakusuBalance.SAKI_SPEED_UP));
                        windows.Add((hers[i].End, slowed, 0f, DanmakusuBalance.SAKI_SPEED_UP, 1.0));
                    }

                    continue;
                }

                // Utsuho: before every black hole every bullet on the screen slows down evenly to a stop (over the last beats of the attack), stands still until the hole starts to pull, and then gets
                // its speed back over a moment (the pull itself is her op, see BossOps: it turns them towards the hole where they stopped)
                if (period.Boss?.Mechanic == BossMechanic.Nuclear)
                {
                    foreach (var (start, end) in BossOps.PausesWithTail(period))
                    {
                        if (end <= start)
                            continue;

                        double stopFrom = BossOps.UtsuhoStopFrom(period, start);
                        double pull = start + DanmakusuBalance.UTSUHO_PULL_DELAY_MS;

                        stops.Add((stopFrom, start, 0f, 1.0, 0.0));
                        stops.Add((start, pull, 0f, 0.0, 0.0));
                        stops.Add((pull, pull + DanmakusuBalance.UTSUHO_RESUME_MS, 0f, 0.0, 1.0));
                    }

                    continue;
                }

                if (period.Boss?.Mechanic != BossMechanic.Blindness)
                    continue;

                // a break or a spinner always has the widest sight and starts the count of the kiai attacks over
                if (period.Kind != BossKind.Kiai)
                    kiaiAttacks = 0;

                int attacks = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);

                for (int i = 0; i < attacks; i++)
                {
                    float sight = period.Kind == BossKind.Kiai ? kiai_sight[kiaiAttacks++ % kiai_sight.Length] : other_sight;
                    windows.Add((period.LaserStarts[i], period.LaserEnds[i], sight, SLOWED_SPEED, SLOWED_SPEED));
                }
            }

            if (stops.Count == 0)
                return new BossEffects(windows);

            var all = new List<(double start, double end, float sight, double speedFrom, double speedTo)>(windows);
            all.AddRange(stops);

            return new BossEffects(all) { unstopped = new BossEffects(windows) };
        }
    }
}
