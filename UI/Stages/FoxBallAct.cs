using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Tsukasa Kudamaki: she shoots rings of six big balls (the planner's: addFoxBalls), and every fourth beam of her section bounces once off the border of the screen (the planner marks it, BossOps
        // bounces it). Here: every tenth bullet fired in her section (anyone's, counted ring by ring as it is fired; not her balls, not trains) does not come out where it was fired - it comes out of the edge of one
        // of her balls instead (on the side it flies to), the six of the newest ring that is out in turn, and flies on the way it was fired, at the speed it had. With no ball on the screen it stays where it was.
        // ======================================================================================================================================================

        private sealed class FoxBallAct : StageAct
        {
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private readonly List<BulletEmitter> balls = new List<BulletEmitter>(); // (her rings of balls, in time order)
            private long counted;
            private int shot;

            public FoxBallAct(BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Init(BulletField f)
            {
                base.Init(f);

                foreach (var ring in f.emitters)
                {
                    if (ring.Sharp && ring.OwnShot && ring.FollowsBoss && ring.BossRank == Period.Rank && ring.Time >= Period.ShootStart && ring.Time < Period.End)
                        balls.Add(ring);
                }

                balls.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                shot = 0;
                ClearBullets();
            }

            /// <summary>Where a ball is at <paramref name="t"/>, if it is there and on the screen.</summary>
            private static bool ballAt(BulletField f, BulletEmitter ring, int ball, double t, out Vector2 position)
            {
                position = default;

                if (t < ring.Time || ball >= ring.Count || ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & (1UL << ball)) != 0)
                    return false;

                ring.Evaluate(ball, ring.WithoutFrozenTime(f.effects.Effective(ring, t)), out position, out _, out bool alive);

                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                return alive && float.IsFinite(position.X) && float.IsFinite(position.Y) && position.X >= low.X && position.X <= high.X && position.Y >= low.Y && position.Y <= high.Y;
            }

            /// <summary>The next ball in turn that is out at <paramref name="t"/>: of the newest ring that has one out (the two before it too, if the newest has none on the screen).</summary>
            private bool nextBall(BulletField f, double t, out Vector2 position, out float ballRadius)
            {
                position = default;
                ballRadius = 0f;
                int newest = balls.Count - 1;

                while (newest >= 0 && balls[newest].Time > t)
                    newest--;

                for (int r = newest; r >= 0 && r >= newest - 2; r--)
                {
                    var ring = balls[r];

                    for (int k = 0; k < ring.Count; k++)
                    {
                        int ball = (shot + k) % ring.Count;

                        if (ballAt(f, ring, ball, t, out position))
                        {
                            shot += k + 1;
                            ballRadius = ring.VisualRadius * ring.BigScale(ball);
                            return true;
                        }
                    }
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t > End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing || balls.Count == 0)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.TSUKASA_RESHOOT_EVERY != 0)
                    return false;

                if (!nextBall(f, t, out Vector2 position, out float ballRadius))
                    return false;

                // (the way it was fired, at the speed it had)
                emitter.Evaluate(bullet, emitter.WithoutFrozenTime(f.effects.Effective(emitter, t)), out _, out Vector2 direction, out bool alive);

                if (!alive || direction.LengthSquared < 1e-8f)
                {
                    Vector2 delta = to - from;

                    if (delta.LengthSquared < 1e-8f)
                        return false;

                    direction = delta;
                }

                var stored = f.makeStored(emitter, t);
                Vector2 way = Vector2.Normalize(direction);
                Vector2 velocity = way * stored.Speed;

                // (out of the edge of the ball, on the side it flies to: not from its middle; 2026-11-14)
                AddBullet(f.makeStageBullet(stored, emitter, bullet, t, position + way * ballRadius, velocity));
                return true;
            }
        }
    }
}
