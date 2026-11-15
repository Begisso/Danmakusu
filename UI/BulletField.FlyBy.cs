using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Fly-bys: a bullet that comes near the player (within FLYBY_CIRCLES circle radii) flying at least FLYBY_SPEED (200 px/s, the same on every map: about twice a plain bullet on the usual maps) whooshes past,
        // once: one of the whooshes, chosen at random but never the one that came just before, at most one every 0.3 s. How fast it flies is how far it has moved since the frame before, so whatever makes it fast
        // counts (its ring, a boss's speeding up). Each whoosh plays twice at the same moment, which makes it twice as loud without changing the sound (in step, so nothing cancels out).
        // ======================================================================================================================================================

        /// <summary>The speed of a plain bullet on this map (px per ms; the playfield sets it from the approach rate). 0: unknown, nothing whooshes.</summary>
        public float PlainBulletSpeed { get; set; }

        /// <summary>(How many whooshes have sounded, over every field: for the bench.)</summary>
        public static int FlyBysHeard;

        private Dictionary<long, Vector2> flyByNear = new Dictionary<long, Vector2>(), flyByNext = new Dictionary<long, Vector2>();
        private readonly HashSet<long> flyByDone = new HashSet<long>();
        private readonly List<long> flyByGone = new List<long>();
        private double flyByTime = double.NaN, flyByLastSound = double.NegativeInfinity, flyByStep;
        private int flyByLast = -1, flyBySounded;
        private bool flyByOn;
        private float flyByReachSq;

        /// <summary>A frame begins: how long since the last one (a seek, a pause or time going back starts it all again).</summary>
        private void beginFlyBys(double t)
        {
            double step = t - flyByTime;
            bool steady = step > 0 && step < 100;

            if (!steady)
            {
                flyByNear.Clear();
                flyByDone.Clear();
                flyByLastSound = double.NegativeInfinity;
            }

            flyByOn = steady && !failed && PlainBulletSpeed > 0f;
            flyByStep = step;
            flyByTime = t;
            flyByNext.Clear();

            float reach = circleRadius * DanmakusuBalance.FLYBY_CIRCLES;
            flyByReachSq = reach * reach;
        }

        /// <summary>A bullet (<paramref name="key"/>: which one, the same from frame to frame) is drawn at <paramref name="position"/>: if it is near the player and flies fast enough, it whooshes past.</summary>
        private void flyBy(long key, Vector2 position, double t)
        {
            if (PlainBulletSpeed <= 0f || Vector2.DistanceSquared(position, characterPosition) > flyByReachSq)
                return;

            flyByNext[key] = position;

            if (!flyByOn || flyByDone.Contains(key) || !flyByNear.TryGetValue(key, out var before))
                return;

            float speed = Vector2.Distance(position, before) / (float)flyByStep;

            // (fast enough - the same speed on every map, whatever its bullets' speed is; not a jump - through a portal, mirrored - that only looks fast)
            if (speed < DanmakusuBalance.FLYBY_SPEED / 1000f || speed > DanmakusuBalance.FLYBY_JUMP_SPEED / 1000f)
                return;

            flyByDone.Add(key);

            if (t - flyByLastSound < DanmakusuBalance.FLYBY_COOLDOWN_MS)
                return;

            // (at random, but never the one just before)
            int sounds = DanmakusuBalance.FLYBY_SOUNDS;
            int pick = flyByLast < 0 ? (int)(hash01(flyBySounded, (int)(t / 10), 61) * sounds) : (int)(hash01(flyBySounded, (int)(t / 10), 61) * (sounds - 1));

            if (flyByLast >= 0 && pick >= flyByLast)
                pick++;

            pick = Math.Clamp(pick, 0, sounds - 1);
            flyBySounded++;
            FlyBysHeard++;
            flyByLast = pick;
            flyByLastSound = t;

            for (int copy = 0; copy < DanmakusuBalance.FLYBY_COPIES; copy++)
                playStageSound($"fly_by_{pick}");
        }

        /// <summary>The frame is over: what is near now is what the next frame compares with; what has gone out of reach may whoosh again when it comes back.</summary>
        private void endFlyBys()
        {
            (flyByNear, flyByNext) = (flyByNext, flyByNear);
            flyByGone.Clear();

            foreach (long key in flyByDone)
            {
                if (!flyByNear.ContainsKey(key))
                    flyByGone.Add(key);
            }

            foreach (long key in flyByGone)
                flyByDone.Remove(key);
        }
    }
}
