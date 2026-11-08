using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Every ricochet flashes: wherever anything bounces, a white glow swells and dies over a moment, as where something bounces off Mima's moon. That is
        //  - a bullet of a ricocheting ring that touches a wall (worked out from the ring's flight, TurnsBetween, so a replay and a seek show the same);
        //  - a bullet that an act turns by bouncing it (StageAct.PushIsRicochet: Ran's beams, Kyouko's glass), where it was turned;
        //  - what bounces in the ops of the bosses (Seija's border, Kyouko's glass): the op keeps its recent bounces (TrajectoryOp.Bounces; Mima's moon flashes its own);
        //  - what the acts bounce themselves (Reimu's orbs, Hecatia's worlds, Joon's gold, Shion's talismans, Tenshi's walls...): they hand their bounces over (addFlash).
        // A push that is not a bounce (Futo's boat, Ringo's big bullets, Okina's portals...) does not flash.
        // ======================================================================================================================================================

        private readonly List<double> flashTurns = new List<double>();
        private readonly Dictionary<(long, int, int), (double Time, Vector2 At, float Size)> flashes = new Dictionary<(long, int, int), (double, Vector2, float)>();
        private readonly List<(long, int, int)> flashesGone = new List<(long, int, int)>();
        private List<ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>>? bounceRecords;
        private int flashesShown;

        /// <summary>
        /// Something bounced at <paramref name="time"/> at <paramref name="at"/>: it flashes there, <paramref name="size"/> across at its biggest. (The same bounce handed over again, as an act works it out once more
        /// after a seek, is one flash.)
        /// </summary>
        private void addFlash(double time, Vector2 at, float size)
        {
            if (flashes.Count < 4096)
                flashes.TryAdd(((long)Math.Floor(time / 25), (int)MathF.Floor(at.X / 8f), (int)MathF.Floor(at.Y / 8f)), (time, at, size));
        }

        private void flashAt(double t, double time, Vector2 at, float size)
        {
            double age = t - time;

            if (age < 0 || age > DanmakusuBalance.RICOCHET_FLASH_MS || flashesShown >= DanmakusuBalance.RICOCHET_FLASH_MOST)
                return;

            float left = 1f - (float)(age / DanmakusuBalance.RICOCHET_FLASH_MS);
            stageFront.Add(StageTexture.Glow, at, new Vector2(size * (0.6f + 0.4f * left)), 0f, left, Color4.White, true);
            flashesShown++;
        }

        private void drawRicochetFlashes(double t)
        {
            flashesShown = 0;

            if (failed)
                return;

            // (the bounces kept by the ops of the bosses, Mima's moon aside: it flashes its own)
            if (bounceRecords == null)
            {
                bounceRecords = new List<ConcurrentDictionary<(int, int, int), (double Time, Vector2 Position)>>();
                var seen = new HashSet<TrajectoryOp>();

                foreach (var emitter in emitters)
                    addRecords(emitter.Ops, seen);

                foreach (var laser in lasers)
                    addRecords(laser.Ops, seen);
            }

            float plain = circleRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f;

            foreach (var records in bounceRecords)
            {
                foreach (var bounce in records)
                {
                    double age = t - bounce.Value.Time;

                    if (age > DanmakusuBalance.RICOCHET_FLASH_MS || age < -DanmakusuBalance.RICOCHET_FLASH_MS)
                        records.TryRemove(bounce.Key, out _);
                    else
                        flashAt(t, bounce.Value.Time, bounce.Value.Position, plain);
                }
            }

            // (what the acts have handed over)
            flashesGone.Clear();

            foreach (var flash in flashes)
            {
                if (t - flash.Value.Time > DanmakusuBalance.RICOCHET_FLASH_MS)
                    flashesGone.Add(flash.Key);
                else
                    flashAt(t, flash.Value.Time, flash.Value.At, flash.Value.Size);
            }

            foreach (var key in flashesGone)
                flashes.Remove(key);

            for (int k = 0; k < activeCount && flashesShown < DanmakusuBalance.RICOCHET_FLASH_MOST; k++)
            {
                var emitter = emitters[active[k]];

                if (emitter.Retired || (!emitter.Ricochet && emitter.Deflections == null))
                    continue;

                double ringTime = emitter.WithoutFrozenTime(effects.Effective(emitter, t));
                double from = Math.Max(ringTime - DanmakusuBalance.RICOCHET_FLASH_MS, emitter.Time);

                if (ringTime <= from)
                    continue;

                float size = emitter.VisualRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE;

                // (bounced by an act)
                if (emitter.Deflections != null)
                {
                    foreach (var (bullet, pushed) in emitter.Deflections)
                    {
                        if (pushed.Time > from && pushed.Time <= ringTime && pushed.By >= 0 && pushed.By < stageActs.Count && stageActs[pushed.By].PushIsRicochet
                            && ((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & (1UL << bullet)) == 0)
                            flashAt(ringTime, pushed.Time, pushed.Position, size);
                    }
                }

                if (!emitter.Ricochet)
                    continue;

                // (off the walls)
                for (int b = 0; b < emitter.Count && flashesShown < DanmakusuBalance.RICOCHET_FLASH_MOST; b++)
                {
                    if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & (1UL << b)) != 0)
                        continue;

                    flashTurns.Clear();
                    emitter.TurnsBetween(b, from, ringTime, flashTurns);

                    foreach (double turn in flashTurns)
                    {
                        if (emitter.Deflections != null && emitter.Deflections.TryGetValue(b, out var pushed) && Math.Abs(turn - pushed.Time) < 1e-6)
                            continue; // (pushed: flashed above if it was a bounce)

                        emitter.Evaluate(b, turn, out Vector2 at, out _, out _);
                        flashAt(ringTime, turn, at, size);
                    }
                }
            }
        }

        private void addRecords(TrajectoryOp[]? ops, HashSet<TrajectoryOp> seen)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
            {
                if (op.Bounces != null && op.Kind != TrajectoryOpKind.Crescent && seen.Add(op))
                    bounceRecords!.Add(op.Bounces);
            }
        }
    }
}
