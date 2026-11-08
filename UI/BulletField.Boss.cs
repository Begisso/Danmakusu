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
        /// <summary>
        /// Moves the boss towards where he should stand; he glides to it instead of jumping, so he moves smoothly however fast the mouse is polled.
        /// The position his shots come from (the start of his laser beams, his stars) only looks at the target 125 times a second at most (game time);
        /// Marisa, her star and her magic circle follow it every frame. He is put straight on the target when no boss is there yet and after a seek.
        /// </summary>
        private void moveBoss(double now, bool noBoss, Vector2 wanted)
        {
            if (noBoss || now < bossSampleTime || now - bossSampleTime > 250 || now < bossSmoothTime || now - bossSmoothTime > 250)
            {
                bossTarget = wanted;
                bossPosition = wanted;
                bossVisualPosition = wanted;
                bossSampleTime = now;
                bossSmoothTime = now;
                return;
            }

            if (now - bossSampleTime >= DanmakusuBalance.BOSS_UPDATE_INTERVAL_MS)
            {
                bossTarget = wanted;
                bossSampleTime = now;
            }

            double dt = now - bossSmoothTime;
            bossSmoothTime = now;
            float follow = 1f - MathF.Exp((float)(-dt / DanmakusuBalance.BOSS_SMOOTHING_MS));
            bossPosition += (bossTarget - bossPosition) * follow;
            bossVisualPosition += (wanted - bossVisualPosition) * follow;
        }

        /// <summary>Marisa stands where a boss stands (opposite the player, or in the middle), but half as far from the middle of the screen (<see cref="DanmakusuBalance.MARISA_PLACE_SCALE"/>).</summary>
        private static Vector2 marisaPlace(BossPeriod? period, Vector2 wanted, Vector2 centre)
            => period?.Boss?.Mechanic == BossMechanic.MasterSpark ? centre + (wanted - centre) * DanmakusuBalance.MARISA_PLACE_SCALE : wanted;

        private readonly double[] heldWindow = { double.NaN, double.NaN, double.NaN };
        private readonly Vector2[] heldPlace = new Vector2[3];

        /// <summary>
        /// Marisa stands still while her Master Spark fires: from the moment one of her attacks begins until it ends she wants to be where she was then (the place is taken in the first frame of the attack;
        /// a seek takes it again).
        /// </summary>
        private Vector2 holdStill(BossPeriod? period, double now, Vector2 wanted, int rank)
        {
            if (period?.Boss?.Mechanic != BossMechanic.MasterSpark)
                return wanted;

            for (int i = 0; i < Math.Min(period.LaserStarts.Length, period.LaserEnds.Length); i++)
            {
                if (now < period.LaserStarts[i] || now >= period.LaserEnds[i])
                    continue;

                if (heldWindow[rank] != period.LaserStarts[i])
                {
                    heldWindow[rank] = period.LaserStarts[i];
                    heldPlace[rank] = bossPositionOf(rank);
                }

                return heldPlace[rank];
            }

            heldWindow[rank] = double.NaN;
            return wanted;
        }

        /// <summary>The second boss glides like the first one (see <see cref="moveBoss"/>).</summary>
        private void moveBoss2(double now, bool noBoss, Vector2 wanted)
        {
            if (noBoss || now < bossSampleTime2 || now - bossSampleTime2 > 250 || now < bossSmoothTime2 || now - bossSmoothTime2 > 250)
            {
                bossTarget2 = wanted;
                bossPosition2 = wanted;
                bossVisualPosition2 = wanted;
                bossSampleTime2 = now;
                bossSmoothTime2 = now;
                return;
            }

            if (now - bossSampleTime2 >= DanmakusuBalance.BOSS_UPDATE_INTERVAL_MS)
            {
                bossTarget2 = wanted;
                bossSampleTime2 = now;
            }

            double dt = now - bossSmoothTime2;
            bossSmoothTime2 = now;
            float follow = 1f - MathF.Exp((float)(-dt / DanmakusuBalance.BOSS_SMOOTHING_MS));
            bossPosition2 += (bossTarget2 - bossPosition2) * follow;
            bossVisualPosition2 += (wanted - bossVisualPosition2) * follow;
        }

        /// <summary>The third boss glides like the first one too.</summary>
        private void moveBoss3(double now, bool noBoss, Vector2 wanted)
        {
            if (noBoss || now < bossSampleTime3 || now - bossSampleTime3 > 250 || now < bossSmoothTime3 || now - bossSmoothTime3 > 250)
            {
                bossTarget3 = wanted;
                bossPosition3 = wanted;
                bossVisualPosition3 = wanted;
                bossSampleTime3 = now;
                bossSmoothTime3 = now;
                return;
            }

            if (now - bossSampleTime3 >= DanmakusuBalance.BOSS_UPDATE_INTERVAL_MS)
            {
                bossTarget3 = wanted;
                bossSampleTime3 = now;
            }

            double dt = now - bossSmoothTime3;
            bossSmoothTime3 = now;
            float follow = 1f - MathF.Exp((float)(-dt / DanmakusuBalance.BOSS_SMOOTHING_MS));
            bossPosition3 += (bossTarget3 - bossPosition3) * follow;
            bossVisualPosition3 += (wanted - bossVisualPosition3) * follow;
        }

        // (with three bosses on the screen: which of the places they stand at each one stands at with another, and so shares a magic circle there: 0 the middle, 1 opposite the player, -1 none)
        private readonly int[] circleGroup = { -1, -1, -1 };

        /// <summary>
        /// Three bosses on the screen: a boss stands in the middle or opposite the player (or wherever her act or her throw puts her, and then nothing here moves her). Those who stand at one place circle round it, evenly apart, and share one magic circle there. If the two places are on each
        /// other those opposite the player are pushed away from the middle (the way that keeps them inside the screen).
        /// </summary>
        private void spreadThree(double now, BossPeriod? first, BossPeriod? second, BossPeriod? third, ref Vector2 wanted, ref Vector2 wanted2, ref Vector2 wanted3)
        {
            sharedCircle = false;
            circleShare = 0f;
            orbitShown = Vector2.Zero;
            orbitTime = now;

            int groupOf(BossPeriod? period)
            {
                if (period == null || stageBossPosition(period, now, out _) || (chenThrows.Length > 0 && chenPosition(period, now, out _)))
                    return -1;

                bool middle = period.Kind == BossKind.Spinner || bossAtCentre(period, now);

                // (Marisa does not stand opposite the player but half way there: a place of her own, whose circle she shares with nobody)
                if (!middle && period.Boss?.Mechanic == BossMechanic.MasterSpark)
                    return -1;

                return middle ? 0 : 1;
            }

            Span<int> group = stackalloc int[] { groupOf(first), groupOf(second), groupOf(third) };
            Span<Vector2> at = stackalloc Vector2[] { wanted, wanted2, wanted3 };
            int inMiddle = 0, opposite = 0;
            Vector2 middle = Vector2.Zero, there = Vector2.Zero;

            for (int rank = 0; rank < 3; rank++)
            {
                if (group[rank] == 0)
                {
                    inMiddle++;
                    middle = at[rank];
                }
                else if (group[rank] == 1)
                {
                    opposite++;
                    there = at[rank];
                }
            }

            float reach = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f;
            float gap = Vector2.Distance(middle, there);

            if (inMiddle > 0 && opposite > 0 && gap < reach)
            {
                Vector2 away = gap > 1f ? (there - middle) / gap : new Vector2(1, 0);
                Vector2 pushed = middle + away * reach;
                Vector2 low = hasScreenBounds ? screenMin : Vector2.Zero;
                Vector2 high = hasScreenBounds ? screenMax : new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT);

                if (pushed.X < low.X || pushed.X > high.X || pushed.Y < low.Y || pushed.Y > high.Y)
                    pushed = middle - away * reach;

                for (int rank = 0; rank < 3; rank++)
                {
                    if (group[rank] == 1)
                        at[rank] = pushed;
                }
            }

            float angle = (float)(now / DanmakusuBalance.DOUBLE_BOSS_ORBIT_PERIOD_MS * Math.PI * 2);
            float radius = circleRadius * DanmakusuBalance.DOUBLE_BOSS_ORBIT_RADIUS;

            for (int place = 0; place < 2; place++)
            {
                int members = place == 0 ? inMiddle : opposite, seen = 0;

                if (members < 2)
                    continue;

                for (int rank = 0; rank < 3; rank++)
                {
                    if (group[rank] != place)
                        continue;

                    float round = angle + seen++ * MathF.Tau / members;

                    at[rank] += new Vector2(MathF.Cos(round), MathF.Sin(round)) * radius;
                    circleGroup[rank] = place;
                }
            }

            wanted = at[0];
            wanted2 = at[1];
            wanted3 = at[2];
        }

        /// <summary>Those of three bosses who circle round one place have one magic circle, in the middle of them.</summary>
        private void shareCirclesOfThree()
        {
            for (int place = 0; place < 2; place++)
            {
                int holder = -1, members = 0;
                Vector2 sum = Vector2.Zero;

                for (int rank = 0; rank < 3; rank++)
                {
                    if (circleGroup[rank] != place)
                        continue;

                    if (holder < 0)
                        holder = rank;

                    members++;
                    sum += bossVisualOf(rank);
                }

                if (members < 2)
                    continue;

                characterOf(holder).CircleOffset = sum / members - bossVisualOf(holder);

                for (int rank = holder + 1; rank < 3; rank++)
                {
                    if (circleGroup[rank] == place)
                        characterOf(rank).CircleFade = 1f;
                }
            }
        }

        private BossCharacter characterOf(int rank) => rank == 2 ? bossCharacter3 : rank == 1 ? bossCharacter2 : bossCharacter;

        /// <summary>
        /// Two bosses on the screen: if they stand on each other (their magic circles overlap) they circle round the place they stand at, on opposite sides, each with a magic circle of its own.
        /// <paramref name="wanted"/> and <paramref name="wanted2"/> are moved on their orbits.
        /// </summary>
        private void updateBossOrbit(double now, bool two, bool centre, bool centre2, ref Vector2 wanted, ref Vector2 wanted2)
        {
            sharedCircle = false;
            circleShare = 0f;

            if (!two)
            {
                orbitShown = Vector2.Zero;
                orbitTime = now;
                return;
            }

            float reach = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f;
            float gap = Vector2.Distance(wanted, wanted2);
            sharedCircle = gap < reach;

            if (centre != centre2)
            {
                // one stays in the middle and the other stands opposite the player: their circles never overlap. If the player's place puts them on each other the one who is not in the middle
                // is pushed away from the other (the way that keeps him inside the screen)
                orbitShown = Vector2.Zero;
                orbitTime = now;
                sharedCircle = false;

                if (gap >= reach)
                    return;

                ref Vector2 mover = ref (centre ? ref wanted2 : ref wanted);
                Vector2 fixedAt = centre ? wanted : wanted2;
                Vector2 away = gap > 1f ? (mover - fixedAt) / gap : new Vector2(1, 0);
                Vector2 pushed = fixedAt + away * reach;
                Vector2 low = hasScreenBounds ? screenMin : Vector2.Zero;
                Vector2 high = hasScreenBounds ? screenMax : new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT);

                if (pushed.X < low.X || pushed.X > high.X || pushed.Y < low.Y || pushed.Y > high.Y)
                    pushed = fixedAt - away * reach;

                mover = pushed;
                return;
            }

            float amount = 1f - smoothStep((gap - reach * 0.75f) / (reach * 0.25f)); // (full when they stand on each other, nothing when their circles just touch)
            double angle = now / DanmakusuBalance.DOUBLE_BOSS_ORBIT_PERIOD_MS * Math.PI * 2;
            Vector2 orbit = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (circleRadius * DanmakusuBalance.DOUBLE_BOSS_ORBIT_RADIUS * amount);

            wanted += orbit;
            wanted2 -= orbit;
            circleShare = amount; // (they circle round one place: the two circles become one, there)

            // (the shared circle stays where they circle round: it is drawn that far from the first boss, who is shown with the same smoothing as everything)
            double dt = now - orbitTime;
            orbitTime = now;

            if (dt < 0 || dt > 250)
                orbitShown = orbit;
            else
                orbitShown += (orbit - orbitShown) * (1f - MathF.Exp((float)(-dt / DanmakusuBalance.BOSS_SMOOTHING_MS)));
        }

        private bool sharedCircle; // (the two bosses stand on each other)
        private float circleShare; // (0..1: how much they circle round one place, and share one magic circle)

        /// <summary>Where the boss with this <see cref="BossPeriod.Rank"/> is (his shots come from it): what a boss's mechanic hangs on is its own boss.</summary>
        private Vector2 bossPositionOf(int rank) => rank == 2 ? bossPosition3 : rank == 1 ? bossPosition2 : bossPosition;

        /// <summary>Where the boss with this rank is drawn (without the delay of the shots).</summary>
        private Vector2 bossVisualOf(int rank) => rank == 2 ? bossVisualPosition3 : rank == 1 ? bossVisualPosition2 : bossVisualPosition;

        /// <summary>
        /// The flashlight of a boss's mechanic (Mystia's), if it is on: <paramref name="radius"/> is the radius of sight in playfield pixels, <paramref name="strength"/> 0..1 (it fades in and out).
        /// </summary>
        public bool TryGetFlashlight(out float radius, out float strength)
        {
            radius = 0;
            strength = 0;

            if (failed || !effects.FlashlightAt(previousTime, out float sight, out strength))
                return false;

            radius = sight * circleRadius;
            return true;
        }

        /// <summary>
        /// The boss period of this rank that is on at <paramref name="now"/> (they can overlap), or null: the one that started last, but while the next one only gives her warning the one before her, who still shoots, is the boss there (her shots come
        /// from where she is drawn, not out of the next boss).
        /// </summary>
        private BossPeriod? latestBoss(double now, int rank = 0)
        {
            BossPeriod? latest = null;

            foreach (var period in bosses)
            {
                // (what Yorihime is repeating is not a boss on the screen: she is)
                if (period.Rank == rank && !period.Borrowed && now >= period.Start && now <= period.End && (latest == null || Supersedes(period, latest, now)))
                    latest = period;
            }

            return latest;
        }

        /// <summary>Is <paramref name="period"/> the boss to show at <paramref name="now"/> rather than <paramref name="than"/> (both on, of one rank)? One that shoots by now before one that only gives her warning; of two alike the later.</summary>
        public static bool Supersedes(BossPeriod period, BossPeriod than, double now)
        {
            bool shoots = now >= period.ShootStart, other = now >= than.ShootStart;
            return shoots != other ? shoots : period.Start >= than.Start;
        }

        // (the boss at each rank this frame, and when that was worked out)
        private readonly BossPeriod?[] shownAt = new BossPeriod?[3];
        private double shownTime = double.NegativeInfinity;

        /// <summary>
        /// Where the boss of <paramref name="period"/> is drawn: as <see cref="bossVisualOf(int)"/>, but a boss who has left her section while a mechanic of hers goes on is where she lingers, not where the next boss of her rank (or nobody) is. What Yorihime
        /// repeats comes from her.
        /// </summary>
        private Vector2 bossVisualOf(BossPeriod period) => hasLeft(period) ? lingerOf(period.Rank) : bossVisualOf(period.Rank);

        /// <summary>Where the shots of the boss of <paramref name="period"/> come from: as <see cref="bossVisualOf(BossPeriod)"/>.</summary>
        private Vector2 bossPositionOf(BossPeriod period) => hasLeft(period) ? lingerOf(period.Rank) : bossPositionOf(period.Rank);

        private bool hasLeft(BossPeriod period)
            => !period.Borrowed && period.Rank is >= 0 and < 3 && shownTime > period.End && !(shownAt[period.Rank] is BossPeriod shown && (shown == period || shown.Boss == period.Boss));

        private Vector2 lingerOf(int rank) => rank == 2 ? lingerAt3 : rank == 1 ? lingerAt2 : lingerAt;

        /// <summary>Shows the boss (with her approach circle and the stars flying in) while a boss period lasts.</summary>
        private void updateBoss(double now)
        {
            bossCharacter.TargetPosition = bossVisualPosition;
            bossCharacter2.TargetPosition = bossVisualPosition2;
            bossCharacter3.TargetPosition = bossVisualPosition3;

            // (a boss who lingers stays where she stood as she left: the place is followed until she starts to linger, then kept)
            if (lingeringBoss.Alpha <= 0f)
                lingerAt = bossVisualPosition;

            if (lingeringBoss2.Alpha <= 0f)
                lingerAt2 = bossVisualPosition2;

            if (lingeringBoss3.Alpha <= 0f)
                lingerAt3 = bossVisualPosition3;

            lingeringBoss.TargetPosition = lingerAt;
            lingeringBoss2.TargetPosition = lingerAt2;
            lingeringBoss3.TargetPosition = lingerAt3;

            // (every boss has a magic circle of its own, until they stand on each other and circle round their place: then the two circles slide into one, at the middle between them)
            bossCharacter.CircleOffset = Vector2.Zero;
            bossCharacter.HideCircle = false;
            bossCharacter2.HideCircle = false;
            bossCharacter2.CircleOffset = Vector2.Zero;
            bossCharacter.CircleFade = 0f;
            bossCharacter2.CircleFade = 0f;
            bossCharacter3.HideCircle = false;
            bossCharacter3.CircleOffset = Vector2.Zero;
            bossCharacter3.CircleFade = 0f;

            if (circleShare > 0.001f && !failed)
            {
                bossCharacter.CircleOffset = (bossVisualPosition2 - bossVisualPosition) * (0.5f * circleShare);
                bossCharacter2.CircleFade = circleShare;
            }

            if (!failed)
                shareCirclesOfThree();

            // (the attack preview of the boss menu has no HUD: the menu shows the name and the picture of the boss itself, and a health bar that starts over every few seconds only glitches)
            float mirrorDegrees = mirrorRadians * 180f / MathF.PI;

            if (Preview)
                bossHud.Alpha = 0f;
            else
            {
                if (hasScreenBounds)
                    bossHud.SetBounds(screenMin, screenMax);

                // (Seija turns the screen round: the HUD and the boss's picture stay upright)
                bossHud.SetMirror(mirrorDegrees, MirrorCentre);
                bossHud.UpdateState(now);
            }

            bossCharacter.ArtRotation = -mirrorDegrees + bossArtTurn(now);
            bossCharacter2.ArtRotation = bossCharacter.ArtRotation;
            bossCharacter3.ArtRotation = bossCharacter.ArtRotation;

            // (the boss's own mechanic: a stub for each boss so far)
            var current = latestBoss(now);

            if (current != null && !failed)
                BossMechanics.Update(current.Boss.Mechanic, now, current);

            var currentSecond = latestBoss(now, 1);

            if (currentSecond != null && !failed)
                BossMechanics.Update(currentSecond.Boss.Mechanic, now, currentSecond);

            var currentThird = latestBoss(now, 2);

            if (currentThird != null && !failed)
                BossMechanics.Update(currentThird.Boss.Mechanic, now, currentThird);

            // (Koishi is gone with everything else while she is invisible; so is her magic circle, which is part of her picture)
            float koishiGone = 0f;

            if (!failed && koishiMarks.Length > 0 && koishiOn(now))
            {
                koishiGlitch(now, out bool koishiAllHidden, out float koishiFlickering);
                koishiGone = koishiAllHidden ? 1f : koishiFlickering * 0.6f;
            }

            bossCharacter.HideAmount = failed ? 0f : Math.Max(stageHide, koishiGone);
            bossCharacter2.HideAmount = bossCharacter.HideAmount;
            bossCharacter3.HideAmount = bossCharacter.HideAmount;

            // (the warnings the acts give before their attacks - Mokou's jumps - are flashes of the bullets now, not warnings: see flashLevel)
            bossCharacter.ExtraWarnings.Clear();
            actFlashTimes.Clear();

            foreach (var act in stageActs)
                act.Warnings(this, now, actFlashTimes);

            bossCharacter2.ExtraWarnings.Clear();
            bossCharacter3.ExtraWarnings.Clear();

            lingeringBoss.HideAmount = bossCharacter.HideAmount;
            lingeringBoss2.HideAmount = bossCharacter.HideAmount;
            lingeringBoss3.HideAmount = bossCharacter.HideAmount;

            // (Seija's clone before a turn of the screen: upright on the screen, as she is)
            seijaClone.TargetPosition = seijaCloneAt;
            seijaClone.GhostAlpha = seijaCloneLevel * DanmakusuBalance.SEIJA_CLONE_ALPHA;
            seijaClone.GhostSlot = seijaCloneSlot;
            seijaClone.ArtRotation = -mirrorDegrees;
            seijaClone.ExtraWarnings.Clear();

            if (seijaCloneLevel > 0f)
                seijaClone.ExtraWarnings.Add(seijaCloneTurn);

            if (failed)
            {
                bossCharacter.HideBoss();
                bossCharacter2.HideBoss();
                bossCharacter3.HideBoss();
                lingeringBoss.HideBoss();
                lingeringBoss2.HideBoss();
                lingeringBoss3.HideBoss();
                seijaClone.HideBoss();
            }
            else
            {
                bossCharacter.UpdateState(now, bosses, circleRadius);
                bossCharacter2.UpdateState(now, bosses, circleRadius);
                bossCharacter3.UpdateState(now, bosses, circleRadius);
                lingeringBoss.UpdateState(now, bosses, circleRadius);
                lingeringBoss2.UpdateState(now, bosses, circleRadius);
                lingeringBoss3.UpdateState(now, bosses, circleRadius);
                seijaClone.UpdateState(now, bosses, circleRadius);
            }

            // (Flandre's clones stand round her picture)
            bossCharacter.ClearClones();
            bossCharacter2.ClearClones();
            bossCharacter3.ClearClones();

            foreach (var act in flandreActs)
                act.ShowClones(this, now, characterOf(act.Period.Rank));
        }
    }
}
