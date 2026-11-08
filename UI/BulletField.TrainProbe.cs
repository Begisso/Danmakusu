using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        /// <summary>
        /// (Tools/SectionCheck, TRAIN_PROBE: follows every one of Yukari's trains while it is out: how long it was on the screen, how often it stood still or went slower or faster than real time (another boss's clock),
        /// how far it turned between two frames, whether something took it, and how often the rings were started over while it flew (a reset aims it again))
        /// </summary>
        public static bool TrainProbeOn;

        private sealed class TrainWatch
        {
            public double Fired;
            public int Frames, OnScreen, Still, Warped, Taken;
            public float MostTurn, MostJump;
            public Vector2 LastPosition, LastDirection;
            public double LastClock = double.NaN, LastTime = double.NaN;
            public int Resets;
            public string How = "";
        }

        /// <summary>(TRAIN_PROBE: which act last took or caught a train, by the act's name)</summary>
        private readonly Dictionary<BulletEmitter, string> trainTakers = new Dictionary<BulletEmitter, string>();

        private string lastTakerOf(BulletEmitter emitter) => trainTakers.TryGetValue(emitter, out string? name) ? name : "no act";

        private void noteTrainTaker(BulletEmitter emitter, string name)
        {
            if (TrainProbeOn && emitter.Shape == BulletShape.Train)
                trainTakers[emitter] = name;
        }

        private readonly Dictionary<BulletEmitter, TrainWatch> trainWatch = new Dictionary<BulletEmitter, TrainWatch>();

        /// <summary>Where every train came from and which way it went, as it was aimed when it came (see the Train case of the ring's start).</summary>
        private readonly Dictionary<BulletEmitter, (Vector2 Origin, Vector2 Direction)> trainAims = new Dictionary<BulletEmitter, (Vector2, Vector2)>();

        /// <summary>(TRAIN_PROBE: the first times the screen's bounds moved, which starts the rings over)</summary>
        public static readonly List<string> BoundsProbe = new List<string>();
        private int ringResets;

        private void probeTrains(double drawTime)
        {
            if (!TrainProbeOn)
                return;

            foreach (var emitter in emitters)
            {
                if (emitter.Shape != BulletShape.Train || drawTime < emitter.Time || drawTime > emitter.Time + emitter.Lifetime)
                    continue;

                if (!trainWatch.TryGetValue(emitter, out var watch))
                    trainWatch[emitter] = watch = new TrainWatch { Fired = emitter.Time, Resets = ringResets };

                double clock = emitter.WithoutFrozenTime(effects.Effective(emitter, drawTime));
                emitter.Evaluate(0, clock, out Vector2 position, out Vector2 direction, out bool alive);
                watch.Frames++;

                if (((emitter.AbsorbedMask | emitter.ConsumedMask | emitter.HiddenMask) & 1UL) != 0 || emitter.RemovedAt(drawTime) || !alive)
                {
                    if (watch.Taken++ == 0)
                        watch.How = $"first at {drawTime:0} ({drawTime - emitter.Time:0} ms out): absorbed {(emitter.AbsorbedMask & 1UL) != 0}, used up {(emitter.ConsumedMask & 1UL) != 0}, hidden {(emitter.HiddenMask & 1UL) != 0}, "
                                    + $"removed {emitter.RemovedAt(drawTime)}, alive {alive}, at {position}, last taken by {lastTakerOf(emitter)}";
                }

                if (hasScreenBounds && position.X > screenMin.X && position.X < screenMax.X && position.Y > screenMin.Y && position.Y < screenMax.Y)
                    watch.OnScreen++;

                if (!double.IsNaN(watch.LastTime) && drawTime > watch.LastTime)
                {
                    double rate = (clock - watch.LastClock) / (drawTime - watch.LastTime);

                    if (rate < 0.98 || rate > 1.02)
                        watch.Warped++;

                    if ((position - watch.LastPosition).LengthSquared < 1e-4f)
                        watch.Still++;

                    watch.MostJump = Math.Max(watch.MostJump, (position - watch.LastPosition).Length);
                    float cos = Vector2.Dot(Vector2.Normalize(watch.LastDirection), Vector2.Normalize(direction));
                    watch.MostTurn = Math.Max(watch.MostTurn, MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI);
                }

                watch.LastPosition = position;
                watch.LastDirection = direction.LengthSquared > 1e-8f ? direction : Vector2.UnitX;
                watch.LastClock = clock;
                watch.LastTime = drawTime;
            }
        }

        /// <summary>(TRAIN_PROBE: one line for every train that was out)</summary>
        public IEnumerable<string> TrainReport()
        {
            foreach (var (emitter, watch) in trainWatch)
            {
                yield return $"train fired {TimeSpan.FromMilliseconds(watch.Fired):m\\:ss\\.f}: {watch.Frames} frames, on the screen {watch.OnScreen}, standing still {watch.Still}, clock not real time {watch.Warped}, taken {watch.Taken}, "
                             + $"most turn {watch.MostTurn:F1} deg, most jump {watch.MostJump:F0} px, ring resets while out {ringResets - watch.Resets}; bosses then: {bossesAt(watch.Fired)}"
                             + (watch.How.Length > 0 ? $"\n      taken {watch.How}" : "");
            }
        }

        private string bossesAt(double time)
        {
            var names = new List<string>();

            foreach (var period in bosses)
            {
                if (period.Boss != null && time >= period.Start && time <= period.End)
                    names.Add(period.Boss.Id);
            }

            return string.Join(", ", names);
        }
    }
}
