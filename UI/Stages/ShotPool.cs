using System;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // A pool of acts for the bullets of an act that makes them all through a long section (Shinki's wings, Yuiman's points: 2026-11-26). An act holds at most 60,000 bullets and keeps every one it
        // has made until time goes back: with Shinki's four copies of every bullet her act was full after about 90 s of one section (in an Always Boss map, minutes long), and nothing more came out of
        // her wings. Here the bullets go into SHOT_POOL_ACTS acts in turn, SHOT_POOL_WINDOW_MS of their start times to each: an act that is given a bullet of a later stretch than the one it holds is emptied
        // first - by then its bullets have long left the screen (a stretch comes round again after SHOT_POOL_ACTS of them). Going back in time empties them all; what is fired again is made again.
        // ======================================================================================================================================================

        private const int shot_pool_acts = 4;
        private const double shot_pool_window_ms = 20000;

        private sealed class PoolAct : StageAct
        {
            public long Window = long.MinValue;

            public PoolAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                Window = long.MinValue;
            }

            /// <summary>Empties it for the bullets of <paramref name="window"/>.</summary>
            public void Empty(long window)
            {
                ClearBullets();
                Window = window;
            }
        }

        private sealed class ShotPool
        {
            private readonly PoolAct[] acts = new PoolAct[shot_pool_acts];

            public ShotPool(BulletField f, BossPeriod period, double start, double end)
            {
                for (int i = 0; i < acts.Length; i++)
                    f.registerStage(acts[i] = new PoolAct(period, start, end));
            }

            /// <summary>The bullet goes into the act of its stretch (by when it starts), which is emptied first if it held an earlier one.</summary>
            public void Add(StageBullet bullet)
            {
                long window = (long)Math.Floor(bullet.T0 / shot_pool_window_ms);
                var act = acts[(int)(((window % acts.Length) + acts.Length) % acts.Length)];

                if (act.Window != window)
                {
                    if (window < act.Window)
                        return; // (a bullet of a stretch long over: its act holds a later one now)

                    act.Empty(window);
                }

                act.AddBullet(bullet);
            }
        }
    }
}
