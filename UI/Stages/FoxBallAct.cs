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
        // Since 2026-11-28 ("shoots big marisa stars instead. Recorded bullets come out evenly in star shape every 4 beats instead (each star shoots separately)"): her balls are big Marisa stars, and what
        // they take is kept - each star its own - and every TSUKASA_RECORD_EVERY_BEATS of her section every star sends what it has kept out of itself at once, evenly along a five-pointed star round it
        // (its first point at the player), growing as it flies out, all as fast as they flew on the whole. A star that is gone by then takes what it kept with it.
        // ======================================================================================================================================================

        private sealed class FoxBallAct : StageAct
        {
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private readonly List<BulletEmitter> balls = new List<BulletEmitter>(); // (her rings of stars, in time order)
            private long counted;
            private int shot;

            /// <summary>What every star has kept (by its ring in <see cref="balls"/> and its number in it), to send out at the next mark.</summary>
            private readonly Dictionary<(int Ring, int Ball), List<(BulletEmitter Emitter, int Bullet, StoredBullet Stored)>> kept = new Dictionary<(int, int), List<(BulletEmitter, int, StoredBullet)>>();

            private readonly double beat;
            private double nextMark;

            public FoxBallAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * DanmakusuBalance.TSUKASA_RECORD_EVERY_BEATS;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
                nextMark = firstMark;
            }

            private double firstMark => Period.ShootStart + beat * DanmakusuBalance.TSUKASA_RECORD_EVERY_BEATS;

            public override void Init(BulletField f)
            {
                base.Init(f);

                foreach (var ring in f.emitters)
                {
                    // (her stars: big Marisa stars since 2026-11-28 - big sharp balls before)
                    if (ring.OwnShot && ring.HurtsLikeSlider && ring.Shape == BulletShape.SoftStar && ring.FollowsBoss && ring.BossRank == Period.Rank && ring.Time >= Period.ShootStart && ring.Time < Period.End)
                        balls.Add(ring);
                }

                balls.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                kept.Clear();
                counted = 0;
                shot = 0;
                nextMark = firstMark;

                while (nextMark <= time)
                    nextMark += beat * DanmakusuBalance.TSUKASA_RECORD_EVERY_BEATS;

                ClearBullets();
            }

            /// <summary>Where a star is at <paramref name="t"/>, if it is there and on the screen.</summary>
            private static bool ballAt(BulletField f, BulletEmitter ring, int ball, double t, out Vector2 position)
            {
                position = default;

                if (t < ring.Time || ball >= ring.Count || ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & (1UL << ball)) != 0)
                    return false;

                ring.Evaluate(ball, ring.WithoutFrozenTime(f.effects.Effective(ring, t)), out position, out _, out bool alive);

                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                return alive && float.IsFinite(position.X) && float.IsFinite(position.Y) && position.X >= low.X && position.X <= high.X && position.Y >= low.Y && position.Y <= high.Y;
            }

            /// <summary>The next star in turn that is out at <paramref name="t"/>: of the newest ring that has one out (the two before it too, if the newest has none on the screen).</summary>
            private bool nextBall(BulletField f, double t, out int ringIndex, out int ballIndex)
            {
                ringIndex = ballIndex = -1;
                int newest = balls.Count - 1;

                while (newest >= 0 && balls[newest].Time > t)
                    newest--;

                for (int r = newest; r >= 0 && r >= newest - 2; r--)
                {
                    var ring = balls[r];

                    for (int k = 0; k < ring.Count; k++)
                    {
                        int ball = (shot + k) % ring.Count;

                        if (ballAt(f, ring, ball, t, out _))
                        {
                            shot += k + 1;
                            ringIndex = r;
                            ballIndex = ball;
                            return true;
                        }
                    }
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t > Period.End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing || balls.Count == 0 || balls.Contains(emitter))
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

                if (!nextBall(f, t, out int ringIndex, out int ballIndex))
                    return false;

                // (kept by the star, until the next mark)
                if (!kept.TryGetValue((ringIndex, ballIndex), out var list))
                    kept[(ringIndex, ballIndex)] = list = new List<(BulletEmitter, int, StoredBullet)>();

                list.Add((emitter, bullet, f.makeStored(emitter, t)));
                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (; nextMark <= now && nextMark <= Period.End + 1; nextMark += beat * DanmakusuBalance.TSUKASA_RECORD_EVERY_BEATS)
                    release(f, nextMark);
            }

            /// <summary>Every star sends out what it has kept, at once, along a five-pointed star round it.</summary>
            private void release(BulletField f, double t)
            {
                foreach (var ((ringIndex, ballIndex), list) in kept)
                {
                    if (list.Count == 0)
                        continue;

                    var ring = balls[ringIndex];

                    if (!ballAt(f, ring, ballIndex, t, out Vector2 at))
                    {
                        list.Clear(); // (the star is gone: what it kept with it)
                        continue;
                    }

                    float starRadius = ring.VisualRadius * ring.BigScale(ballIndex);
                    Vector2 toPlayer = f.characterPosition - at;
                    float phase = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : -MathF.PI / 2f;
                    var directions = RingLayout.Create(RingShape.Star, list.Count, phase, out var scales);
                    float speed = 0f;

                    foreach (var (_, _, stored) in list)
                        speed += stored.Speed;

                    speed /= list.Count;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var (emitter, bullet, stored) = list[i];
                        float scale = scales != null ? scales[i] : 1f;
                        Vector2 way = directions[i];
                        AddBullet(f.makeStageBullet(stored, emitter, bullet, t, at + way * (starRadius * scale), way * (speed * scale)));
                    }

                    list.Clear();
                }
            }
        }
    }
}
