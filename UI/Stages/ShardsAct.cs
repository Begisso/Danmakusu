using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Cirno (2026-11-26, "for some reason not all frozen bullets explode to shards"): her freeze stops the bullets of the acts too (every one the ops trace, not every second: they are not bullets of a
        // ring) and takes them away when it lets go, but only the rings had shards made for them (the planner's). As each freeze lets go, every such bullet that was there before it began and is still on the
        // screen bursts as a ring's does: CIRNO_FAN_COUNT shards, CIRNO_CHILD_SIZE as big, CIRNO_FAN_DEGREES apart round the way it was flying, CIRNO_BURST_SPEED_FACTOR times as fast; they fly straight.
        // Worked out when the freeze lets go, from the acts' bullets as they are then; made again when time goes back past it.
        // ======================================================================================================================================================

        private sealed class ShardsAct : StageAct
        {
            private readonly List<(double Start, double End)> freezes = new List<(double, double)>();
            private readonly bool[] done;

            public ShardsAct(BulletField f, BossPeriod period, BossPeriod[] bosses)
            {
                Period = period;

                foreach (var (start, attackEnd) in BossOps.Attacks(period))
                {
                    if (attackEnd > start)
                        freezes.Add((start, BossOps.UnfreezeAt(bosses, period, start, attackEnd).End));
                }

                done = new bool[freezes.Count];
                Start = freezes.Count > 0 ? freezes[0].End : period.End;
                End = (freezes.Count > 0 ? freezes[^1].End : period.End) + 60000;
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(done);
                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start || !f.otherOpsOn)
                    return;

                for (int k = 0; k < freezes.Count; k++)
                {
                    var (start, end) = freezes[k];

                    if (done[k] || end > now)
                        continue;

                    done[k] = true;

                    // (long ago: what burst then has flown away)
                    if (now - end > 20000)
                        continue;

                    foreach (var act in f.stageActs)
                    {
                        if (act == this)
                            continue;

                        var list = act.Bullets;

                        for (int i = 0; i < list.Count; i++)
                        {
                            var b = list[i];

                            if (b.T0 >= start || b.Until <= start || !f.stageBulletTraced(b) || f.stageConsumed.Contains(act.Id * 65536 + i))
                                continue;

                            if (!f.stageBulletAt(b, end - 1, out Vector2 at) || !f.isOnScreen(at, b.Radius))
                                continue;

                            burst(f, b, at, end);
                        }
                    }
                }
            }

            private void burst(BulletField f, in StageBullet b, Vector2 at, double end)
            {
                float speed = b.V.Length * DanmakusuBalance.CIRNO_BURST_SPEED_FACTOR;

                if (speed < 1e-4f)
                    speed = stageBulletSpeed(Period) * DanmakusuBalance.CIRNO_BURST_SPEED_FACTOR;

                Vector2 way = b.V.LengthSquared > 1e-12f ? Vector2.Normalize(b.V) : Vector2.UnitY;
                float radius = b.Radius * DanmakusuBalance.CIRNO_CHILD_SIZE;

                for (int fanIndex = 0; fanIndex < DanmakusuBalance.CIRNO_FAN_COUNT; fanIndex++)
                {
                    int fan = fanIndex - DanmakusuBalance.CIRNO_FAN_COUNT / 2;
                    Vector2 v = turned(way, fan * DanmakusuBalance.CIRNO_FAN_DEGREES * MathF.PI / 180f) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = end,
                        P0 = at,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = b.Style,
                        Shape = b.Shape,
                        Tint = b.Tint,
                        TintAmount = b.TintAmount,
                        Untraced = true,
                        Until = end + f.stageLife(v),
                    });
                }
            }
        }
    }
}
