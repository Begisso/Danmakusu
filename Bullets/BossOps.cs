using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// What the bosses' mechanics do to the bullets and beams that are already flying (freeze them, mirror them, send them to the centre...).
    /// Every such thing is a <see cref="TrajectoryOp"/>, which keeps the flight of a bullet a closed form of time. The ops are shared by all rings and beams of a field.
    /// </summary>
    public static class BossOps
    {
        /// <summary>A bullet that has bounced off the border during Seija's reversal.</summary>
        public static readonly BulletLook Red = new BulletLook { Overlay = BulletRenderer.TINT_RED, OverlayAmount = 1f };

        /// <summary>A thing that has bounced off one of Kyouko's walls once / twice: it looks like it did, but the looks are other objects (so that a beam gets a corner where it bounced).</summary>
        public static readonly BulletLook GlassOnce = new BulletLook();

        public static readonly BulletLook GlassTwice = new BulletLook();

        /// <summary>A bullet that Cirno has frozen: blue, with a snowflake in its body.</summary>
        public static readonly BulletLook Frozen = new BulletLook { Overlay = BulletRenderer.TINT_BLUE, OverlayAmount = 0.85f, Decal = BulletDecal.Snowflake };

        /// <summary>A bullet that Cirno has frozen, which shakes for two beats before the freeze ends at <paramref name="end"/>.</summary>
        public static BulletLook FrozenUntil(double end, double beat)
            => new BulletLook { Overlay = BulletRenderer.TINT_BLUE, OverlayAmount = 0.85f, Decal = BulletDecal.Snowflake, ShakeFrom = end - beat * DanmakusuBalance.FROZEN_SHAKE_BEATS, ShakeTo = end };

        /// <summary>A bullet that Yukari has frozen / sent off: it has become a paper talisman in the colour it had.</summary>
        public static readonly BulletLook Carriage = new BulletLook { Shape = BulletShape.Talisman };

        /// <summary>The stages of a bullet turning into a talisman (see <see cref="TrajectoryOp.Steps"/>).</summary>
        public static readonly BulletLook[] CarriageSteps = createCarriageSteps();

        private static BulletLook[] createCarriageSteps()
        {
            const int steps = 25;
            var looks = new BulletLook[steps];

            for (int i = 0; i < steps; i++)
                looks[i] = new BulletLook { Shape = BulletShape.Talisman, Morph = i / (float)(steps - 1) };

            return looks;
        }

        /// <summary>A bullet that Sakuya has turned into a knife.</summary>
        public static readonly BulletLook Knife = new BulletLook { Shape = BulletShape.Knife };

        /// <summary>A sakura flower that Yuyuko's tree has turned a bullet into: pink.</summary>
        public static readonly BulletLook PinkFlower = new BulletLook { Shape = BulletShape.Flower, Overlay = BulletRenderer.TINT_PINK, OverlayAmount = 1f };

        /// <summary>A beam that has started to fall like a petal: purple.</summary>
        public static readonly BulletLook PurpleBeam = new BulletLook { Overlay = BulletRenderer.TINT_PURPLE, OverlayAmount = 0.9f };

        /// <summary>
        /// The same purple for every second piece of a fallen beam: what falls of a beam falls as separate short pieces (see LaserEmitter), and two neighbouring pieces have different looks so that
        /// the beam is never joined from one to the next (they fell at different moments and are far apart).
        /// </summary>
        public static readonly BulletLook PurpleBeamAlt = new BulletLook { Overlay = BulletRenderer.TINT_PURPLE, OverlayAmount = 0.9f };

        /// <summary>Is this the look of a slice of a beam that has fallen from the top (Kaguya's)?</summary>
        public static bool IsFallenBeam(BulletLook? look) => ReferenceEquals(look, PurpleBeam) || ReferenceEquals(look, PurpleBeamAlt);

        /// <summary>Reisen: how a thing that she has frozen looks while it slides to the left / the right / up / down: see-through, and harmless.</summary>
        public static readonly BulletLook ReisenLeft = new BulletLook { Alpha = DanmakusuBalance.REISEN_VISIBLE, SlideSign = -1, Harmless = true };

        public static readonly BulletLook ReisenRight = new BulletLook { Alpha = DanmakusuBalance.REISEN_VISIBLE, SlideSign = 1, Harmless = true };

        public static readonly BulletLook ReisenUp = new BulletLook { Alpha = DanmakusuBalance.REISEN_VISIBLE, SlideSign = -1, SlideVertical = true, Harmless = true };

        public static readonly BulletLook ReisenDown = new BulletLook { Alpha = DanmakusuBalance.REISEN_VISIBLE, SlideSign = 1, SlideVertical = true, Harmless = true };

        /// <summary>A bullet that only stands still while Reisen's freeze is on (pulled and bouncing ones do not slide): see-through, with no preview.</summary>
        public static readonly BulletLook ReisenStill = new BulletLook { Alpha = DanmakusuBalance.REISEN_VISIBLE };

        /// <summary>A bullet that Megumu has turned into a star like Marisa's.</summary>
        public static readonly BulletLook MarisaStar = new BulletLook { Shape = BulletShape.Star };

        /// <summary>A bullet that stands still in Eirin's constellation: a little lighter.</summary>
        public static readonly BulletLook Constellation = new BulletLook { Overlay = BulletRenderer.TINT_WHITE, OverlayAmount = 0.35f };

        /// <summary>Eirin: the moments everything stands still in constellations: every eight beats from four beats into her section (the four before each are the preview).</summary>
        public static List<double> ConstellationMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double beat = Math.Max(period.BeatLength, 1);

            for (double mark = period.ShootStart + beat * DanmakusuBalance.EIRIN_PREVIEW_BEATS; mark < period.End; mark += beat * DanmakusuBalance.EIRIN_EVERY_BEATS)
                marks.Add(mark);

            return marks;
        }

        /// <summary>The attacks of a boss: (start, end) of every laser burst / attack window.</summary>
        public static List<(double Start, double End)> Attacks(BossPeriod period)
        {
            var list = new List<(double, double)>();
            int count = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);

            for (int i = 0; i < count; i++)
                list.Add((period.LaserStarts[i], period.LaserEnds[i]));

            return list;
        }

        /// <summary>Utsuho: when everything starts to slow down before the black hole of the pause that starts at <paramref name="pauseStart"/> (a few beats before it).</summary>
        public static double UtsuhoStopFrom(BossPeriod period, double pauseStart) => pauseStart - Math.Max(period.BeatLength, 1) * DanmakusuBalance.UTSUHO_STOP_BEATS;

        /// <summary>Nazrin: the moments (when her section starts, and every sixteen beats after that) at which the bullets round her turn into arrowheads.</summary>
        public static List<double> ConversionMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.NAZRIN_CONVERT_BEATS;

            for (double mark = period.ShootStart; mark < period.End; mark += gap)
                marks.Add(mark);

            return marks;
        }

        /// <summary>Zanmu: the moments (when her section starts, and every sixteen beats after that) at which a ripple starts from her.</summary>
        public static List<double> RippleMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.ZANMU_RIPPLE_BEATS;

            for (double mark = period.ShootStart; mark < period.End; mark += gap)
                marks.Add(mark);

            return marks;
        }

        /// <summary>Urumi: the moments (when her section starts, and every eight beats after that) at which a push is announced.</summary>
        public static List<double> PushMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.URUMI_CYCLE_BEATS;

            for (double mark = period.ShootStart; mark < period.End; mark += gap)
                marks.Add(mark);

            return marks;
        }

        /// <summary>Luna Child: the moments (when her section starts, and every beat after that) at which a ring of arcs flies out.</summary>
        public static List<double> LunaMarks(BossPeriod period, double everyBeats = DanmakusuBalance.LUNA_RING_EVERY_BEATS)
        {
            var marks = new List<double>();
            double gap = Math.Max(period.BeatLength, 1) * everyBeats;

            for (double mark = period.ShootStart; mark < period.End; mark += gap)
                marks.Add(mark);

            return marks;
        }

        /// <summary>Mima: when her moons appear: every sixteen beats from the start of her section, as long as it lasts (a moon goes on to its end, also after the section).</summary>
        public static List<double> MoonMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double beat = Math.Max(period.BeatLength, 1);

            for (double mark = period.ShootStart; mark < period.End - 1; mark += beat * DanmakusuBalance.MIMA_EVERY_BEATS)
                marks.Add(mark);

            return marks;
        }

        /// <summary>Reisen: the attacks in which she freezes and slides (every second one of her section, from the first). The dim, the warning and the blur of her slides come with these.</summary>
        public static List<(double Start, double End)> ReisenSlides(BossPeriod period)
            => Attacks(period).Where((attack, number) => number % 2 == 0 && attack.End > attack.Start).ToList();

        /// <summary>Shion: the moments everything turns round: every eight beats, from eight beats into her section on (so that the warning before the first is in her section too).</summary>
        public static List<double> ReverseMarks(BossPeriod period)
        {
            var marks = new List<double>();
            double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.SHION_EVERY_BEATS;

            for (double mark = period.ShootStart + gap; mark < period.End; mark += gap)
                marks.Add(mark);

            return marks;
        }

        /// <summary>How what Shion has turned round looks.</summary>
        public static readonly BulletLook ShionBlue = new BulletLook { Overlay = osu.Game.Rulesets.Danmakusu.UI.BulletRenderer.TINT_DARK_BLUE };

        /// <summary>The pauses of a boss: the gaps between two attacks of the same section.</summary>
        public static List<(double Start, double End)> Pauses(BossPeriod period)
        {
            var list = new List<(double, double)>();
            int count = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);

            for (int i = 0; i + 1 < count; i++)
            {
                if (period.LaserStarts[i + 1] > period.LaserEnds[i])
                    list.Add((period.LaserEnds[i], period.LaserStarts[i + 1]));
            }

            return list;
        }

        /// <summary>
        /// The pauses of a boss, and the one after her last attack when it is at least two beats long (the section goes on after the last attack; or she finishes her sequence after it, see
        /// <see cref="BossPeriod.SequenceEnd"/>).
        /// </summary>
        public static List<(double Start, double End)> PausesWithTail(BossPeriod period)
        {
            var list = Pauses(period);
            int count = Math.Min(period.LaserStarts.Length, period.LaserEnds.Length);
            double tailEnd = Math.Max(period.End, period.SequenceEnd);

            // (a spinner that stands alone is long enough for an attack and a pause; one that is shorter has only its attack)
            bool whole = period.Kind != BossKind.Spinner || period.End - period.ShootStart >= (DanmakusuBalance.BOSS_LASER_BURST_BEATS + DanmakusuBalance.BOSS_LASER_PAUSE_BEATS) * Math.Max(period.BeatLength, 1) - 1;

            if (count > 0 && whole && tailEnd - period.LaserEnds[count - 1] >= 2 * Math.Max(period.BeatLength, 1))
                list.Add((period.LaserEnds[count - 1], tailEnd));

            return list;
        }

        /// <summary>
        /// How long Yuyuko's tree stands: from when her warning is over (when she first shoots) to when she goes (sections of hers that are closer together than <see cref="DanmakusuBalance.YUYUKO_TREE_KEEP_GAP_MS"/>
        /// share one tree). Sorted by time.
        /// </summary>
        public static List<(double Start, double End)> TreeWindows(BossPeriod[] bosses, BossMechanic mechanic = BossMechanic.ButterflyFans)
        {
            var periods = new List<BossPeriod>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == mechanic)
                    periods.Add(period);
            }

            periods.Sort((a, b) => a.Start.CompareTo(b.Start));

            var windows = new List<(double Start, double End)>();

            foreach (var period in periods)
            {
                int last = windows.Count - 1;

                if (last >= 0 && period.Start - windows[last].End <= DanmakusuBalance.YUYUKO_TREE_KEEP_GAP_MS)
                    windows[last] = (windows[last].Start, Math.Max(windows[last].End, period.End));
                else
                    windows.Add((period.ShootStart, period.End)); // (the tree starts to grow when the warning is over)
            }

            return windows;
        }

        /// <summary>
        /// Builds the ops of all bosses: <paramref name="bulletOps"/> for the rings, <paramref name="laserOps"/> (only the ones that make sense for a beam) for the lasers.
        /// Both are sorted by time.
        /// </summary>
        public static void Build(BossPeriod[] bosses, out TrajectoryOp[] bulletOps, out TrajectoryOp[] laserOps)
        {
            var bullets = new List<TrajectoryOp>();
            var beams = new List<TrajectoryOp>();
            int reisenAttacks = 0;

            foreach (var period in bosses)
            {
                switch (period.Boss?.Mechanic)
                {
                    case BossMechanic.ScreenMirror:
                        // everything that exists when the reversal starts bounces once off the border of the screen during it, and turns red
                        foreach (var (start, end) in Attacks(period))
                        {
                            var op = new TrajectoryOp { Kind = TrajectoryOpKind.Bounce, Time = start, End = end, BornBefore = start, After = Red, Bounces = new System.Collections.Concurrent.ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>() };
                            bullets.Add(op);
                            beams.Add(op);
                        }

                        break;

                    case BossMechanic.Freeze:
                        // Cirno freezes every second bullet that exists when her attack starts (not what comes later) for the whole attack; it bursts at the end (see the planner). Beams freeze all the same
                        foreach (var (start, end) in Attacks(period))
                        {
                            var frozen = FrozenUntil(end, period.BeatLength);

                            // (the bullets that burst out of an earlier freeze are free: they are not frozen again, and they do not burst)
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start, During = frozen, SkipBursts = true, EveryOther = true, OrEverySecondRing = true });
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Remove, Time = end, End = end, BornBefore = start, SkipBursts = true, EveryOther = true, OrEverySecondRing = true });
                            beams.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start, During = frozen });
                        }

                        break;

                    case BossMechanic.Gaps:
                        // Yukari: in every pause every second bullet that exists freezes (turning into a talisman, facing the centre; the others fly on); when the pause ends it stays a talisman and flies at the centre,
                        // through it and on out of the screen (the pause after her last attack too, also when she finishes her sequence after her section)
                        foreach (var (start, end) in PausesWithTail(period))
                        {
                            bullets.Add(new TrajectoryOp
                            {
                                Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start, OfEight = DanmakusuBalance.YUKARI_FREEZE_OF_EIGHT, During = Carriage, After = Carriage, Steps = CarriageSteps, Blend = DanmakusuBalance.YUKARI_MORPH_MS,
                                FacesPoint = true,
                            });
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Redirect, Time = end, End = end, BornBefore = start, OfEight = DanmakusuBalance.YUKARI_FREEZE_OF_EIGHT, After = Carriage, Through = true });
                            beams.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start });
                        }

                        break;

                    case BossMechanic.Echo:
                    {
                        // Kyouko: the glass walls of every attack (from the top and the bottom in the first, from the left and the right in the next, and so on): everything that flies bounces off them
                        var attacks = Attacks(period);

                        for (int i = 0; i < attacks.Count; i++)
                        {
                            if (attacks[i].End <= attacks[i].Start)
                                continue;

                            var glass = new TrajectoryOp { Kind = TrajectoryOpKind.Glass, Time = attacks[i].Start, End = attacks[i].End, Window = true, Horizontal = i % 2 == 0, After = GlassOnce, AfterSecond = GlassTwice,
                                Bounces = new System.Collections.Concurrent.ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>() };
                            bullets.Add(glass); // (laser beams pass through the glass: it is not given to them)
                        }

                        break;
                    }

                    case BossMechanic.Nuclear:
                        // Utsuho: in every pause the black hole pulls in everything that is flying (also what is fired meanwhile): it flies at the centre, at half its speed, where the hole takes it. Her own big
                        // balls it leaves alone. (Before the hole everything slows to a stop, see BossEffects: the bullets' own clocks stand still at half the slowing down before the pause, so the pull is
                        // put there in their time: they turn to the hole where they stopped, and fly at it when the clocks run again)
                        foreach (var (start, end) in PausesWithTail(period))
                        {
                            double stopFrom = UtsuhoStopFrom(period, start);
                            // (a few ms before their clocks come to rest: put exactly there, rounding put it a hair before or after from one frame to the next, and they flicked between their own way and the hole's)
                            // (the pull is over when the hole goes: on the clock of a bullet that was flying before the stop, which has lost half the slowing down, the wait and half the speeding up again)
                            double lost = (start - stopFrom) * 0.5 + DanmakusuBalance.UTSUHO_PULL_DELAY_MS + DanmakusuBalance.UTSUHO_RESUME_MS * 0.5;
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Redirect, Time = start - (start - stopFrom) * 0.5 - 3, End = end, Window = true, SkipSharp = true, SkipPetals = true, RedirectSpeed = DanmakusuBalance.UTSUHO_PULL_SPEED, RedirectSpeedUp = DanmakusuBalance.UTSUHO_PULL_SPEED_UP, ReleaseAt = end - lost });
                        }

                        break;

                    case BossMechanic.Possibility:
                        // Yumemi: in her pauses everything, also what is fired meanwhile, is pulled straight away from her (her act says where she is when a pause begins); when it ends it flies on at the speed it has got
                        foreach (var (start, end) in PausesWithTail(period))
                        {
                            if (end <= start)
                                continue;

                            var pull = new TrajectoryOp { Kind = TrajectoryOpKind.Outward, Time = start, End = end, Window = true, SkipSharp = true, Push = DanmakusuBalance.ZANMU_GRAVITY * DanmakusuBalance.YUMEMI_PULL, PointKept = true };
                            var over = new TrajectoryOp { Kind = TrajectoryOpKind.Coast, Time = end, End = end };

                            bullets.Add(pull);
                            bullets.Add(over);
                            beams.Add(pull);
                            beams.Add(over);
                        }

                        break;

                    case BossMechanic.Misfortune:
                        // Shion: every eight beats everything there is, bullets and beams, turns dark blue and flies back the way it came
                        foreach (double mark in ReverseMarks(period))
                        {
                            var turn = new TrajectoryOp { Kind = TrajectoryOpKind.Reverse, Time = mark, End = mark, SkipSharp = true, After = ShionBlue };

                            bullets.Add(turn);
                            beams.Add(turn);
                        }

                        break;

                    case BossMechanic.HalfMoon:
                    {
                        // Mima: while a moon of hers is solid (all lit up), everything that flies into it, bullets and the slices of beams (not Yukari's train), bounces off it once; her act tells the op where the moon is
                        double beat = Math.Max(period.BeatLength, 1);

                        foreach (double mark in MoonMarks(period))
                        {
                            var moon = new TrajectoryOp
                            {
                                Kind = TrajectoryOpKind.Crescent, Time = mark + beat * (DanmakusuBalance.MIMA_GROW_BEATS + DanmakusuBalance.MIMA_LIGHT_BEATS), End = mark + beat * (DanmakusuBalance.MIMA_GROW_BEATS + DanmakusuBalance.MIMA_SOLID_BEATS),
                                Window = true, Bounces = new System.Collections.Concurrent.ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>(),
                            };

                            bullets.Add(moon);
                            beams.Add(moon);
                        }

                        break;
                    }

                    case BossMechanic.Judgement:
                        // Eiki: in her attacks everything, bullets and the slices of beams, that enters her circle (placed round the player when the attack begins) is sent at its middle at half its speed, flies on through it, and has its speed back when the circle is gone
                        foreach (var (start, end) in Attacks(period))
                        {
                            if (end <= start)
                                continue;

                            // (one op for both lists: her act tells it where the circle is, and how big)
                            var pull = new TrajectoryOp { Kind = TrajectoryOpKind.Redirect, Time = start, End = end, Window = true, SkipSharp = true, RedirectSpeed = DanmakusuBalance.EIKI_PULL_SPEED, ReleaseAt = double.NegativeInfinity, RestoreAt = end + DanmakusuBalance.STAGE_FADE_OUT_MS, PointKept = true };

                            bullets.Add(pull);
                            beams.Add(pull);
                        }

                        break;

                    case BossMechanic.Lunatic:
                        // Reisen: during every second attack everything that exists stands still (see-through) and slides at the same time, half to the left and half to the right (harmless meanwhile);
                        // when the attack ends it flies on from there. The next attack slides up and down, the one after that sideways again, and so on
                        foreach (var (start, end) in ReisenSlides(period))
                        {
                            bool vertical = reisenAttacks++ % 2 == 1;

                            bullets.Add(new TrajectoryOp
                            {
                                Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start, Slides = true, SlideVertical = vertical, EveryOther = true, // (every second bullet is left alone)
                                During = vertical ? ReisenUp : ReisenLeft, DuringRight = vertical ? ReisenDown : ReisenRight,
                            });

                            beams.Add(new TrajectoryOp
                            {
                                Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start, Slides = true, SlideVertical = vertical, SlideByDirection = true,
                                During = vertical ? ReisenUp : ReisenLeft, DuringRight = vertical ? ReisenDown : ReisenRight,
                            });
                        }

                        break;

                    case BossMechanic.TimeStop:
                        // Sakuya: during an attack everything that exists stands still; when it ends all bullets (also the ones that came meanwhile) are mirrored and become knives
                        foreach (var (start, end) in Attacks(period))
                        {
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start });
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Mirror, Time = end, End = end, After = Knife });
                            beams.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = start, End = end, BornBefore = start });
                        }

                        break;

                    case BossMechanic.Galaxy:
                    {
                        // Megumu: every eight beats every bullet on the screen turns into a star like Marisa's (a freeze that takes no time and leaves it a star) and ricochets once off the border before the next turn
                        double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.MEGUMU_STARS_EVERY_BEATS;

                        foreach (double mark in LunaMarks(period, DanmakusuBalance.MEGUMU_STARS_EVERY_BEATS))
                        {
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = mark, End = mark, BornBefore = mark, SkipSharp = true, After = MarisaStar });
                            bullets.Add(new TrajectoryOp
                            {
                                Kind = TrajectoryOpKind.Bounce, Time = mark, End = mark + gap, BornBefore = mark, SkipSharp = true, After = MarisaStar,
                                Bounces = new System.Collections.Concurrent.ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>(),
                            });
                        }

                        break;
                    }

                    case BossMechanic.Constellation:
                        // Eirin: every eight beats (the first four beats into her section, after the first preview) every bullet that exists stands still for four beats, a little lighter; her act joins them into constellations
                        foreach (double mark in ConstellationMarks(period))
                            bullets.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Freeze, Time = mark, End = mark + Math.Max(period.BeatLength, 1) * DanmakusuBalance.EIRIN_HOLD_BEATS, BornBefore = mark, SkipSharp = true, During = Constellation });

                        break;
                }
            }

            // Yuyuko: every bullet that leaves the top of the screen while her tree stands falls on like a petal (pink), straight down
            foreach (var (start, end) in TreeWindows(bosses))
            {
                bullets.Add(new TrajectoryOp
                {
                    Kind = TrajectoryOpKind.Fall, Time = start, End = end, Window = true, FallSpeed = fallSpeed(bosses, BossMechanic.ButterflyFans, start, end), FallTilt = DanmakusuBalance.YUYUKO_FALL_TILT_DEGREES, FallShare = DanmakusuBalance.YUYUKO_FALL_SHARE,
                    After = PinkFlower, // (straight down: they do not swing)
                });
            }

            // Kaguya: every beam (a slider's: the ones that turn are left alone) that leaves the top of the screen in her sections falls on, straight down, purple, in short pieces, as fast as Yuyuko's petals
            foreach (var (start, end) in TreeWindows(bosses, BossMechanic.ImpossibleRequests))
                beams.Add(new TrajectoryOp { Kind = TrajectoryOpKind.Fall, Time = start, End = end, Window = true, SkipRotating = true, FallSpeed = fallSpeed(bosses, BossMechanic.ImpossibleRequests, start, end), After = PurpleBeam });

            bullets.Sort((a, b) => a.Time.CompareTo(b.Time));
            beams.Sort((a, b) => a.Time.CompareTo(b.Time));
            bulletOps = bullets.ToArray();
            laserOps = beams.ToArray();
        }

        /// <summary>How fast what leaves the top falls in the window of a boss: by the speed of her bullets there.</summary>
        private static float fallSpeed(BossPeriod[] bosses, BossMechanic mechanic, double start, double end)
        {
            float speed = 0;

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic == mechanic && period.ShootStart >= start - 1 && period.ShootStart <= end)
                    speed = Math.Max(speed, period.BulletSpeed);
            }

            return Math.Max(DanmakusuBalance.YUYUKO_FALL_MIN_SPEED, speed * DanmakusuBalance.YUYUKO_FALL_SPEED);
        }
    }
}
