using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Louise (2026-11-27): in her section every LOUISE_EVERY-th bullet fired (anyone's, counted ring by ring as it is fired) is LOUISE_SIZE times as big and flies straight on, leaving a knife behind it
        // every LOUISE_KNIFE_EVERY_BEATS while it is on the screen. Each knife stays where it was left, pointing the way the bullet went, for LOUISE_STAY_BEATS, then flies at where the player is then at
        // LOUISE_KNIFE_SPEED times a bullet's speed. The big bullet and where it leaves its knives are known as it is fired; the way each knife flies off is taken as it goes.
        // ======================================================================================================================================================

        private sealed class KnifeTrailAct : StageAct
        {
            private readonly double beat;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;

            // (the knives waiting to fly off: when, where, how big, its colour; in the order they go)
            private readonly List<(double Go, Vector2 At, float Radius, int Style)> waiting = new List<(double, Vector2, float, int)>();
            private int nextGo;

            public KnifeTrailAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * (DanmakusuBalance.LOUISE_STAY_BEATS + 32);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                waiting.Clear();
                nextGo = 0;
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Period.ShootStart || t > Period.End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.LOUISE_EVERY != 0)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = Math.Max(seenSpeed(stored, emitter, from, to, ScanPrevious, t), stageBulletSpeed(Period) * 0.25f);
                Vector2 step = to - from;
                Vector2 way = step.LengthSquared > 1e-8f ? Vector2.Normalize(step) : Vector2.UnitY;
                Vector2 velocity = way * speed;
                float big = emitter.BigScale(bullet) * DanmakusuBalance.LOUISE_SIZE;
                double life = f.stageLife(velocity);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Untraced = true,
                    Until = t + life,
                });

                // (its knives: one every so often while it is on the screen, each standing until it flies off)
                double every = beat * DanmakusuBalance.LOUISE_KNIFE_EVERY_BEATS;
                double stay = beat * DanmakusuBalance.LOUISE_STAY_BEATS;
                float knife = f.stageBaseRadius(Period) * DanmakusuBalance.LOUISE_KNIFE_SIZE;

                for (double left = t + every; left < t + life; left += every)
                {
                    Vector2 at = to + velocity * (float)(left - t);

                    if (at.X < f.stageMin.X || at.X > f.stageMax.X || at.Y < f.stageMin.Y || at.Y > f.stageMax.Y)
                        break;

                    AddBullet(new StageBullet
                    {
                        T0 = left,
                        P0 = at,
                        V = Vector2.Zero,
                        Face = way,
                        KeepFace = true,
                        Radius = knife,
                        Hit = knife * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = stored.Style,
                        Shape = BulletShape.Knife,
                        Untraced = true,
                        Until = left + stay,
                    });

                    insert((left + stay, at, knife, stored.Style));
                }

                return true;
            }

            /// <summary>The knives wait in the order they go (big bullets fired later can leave knives that go sooner).</summary>
            private void insert((double Go, Vector2 At, float Radius, int Style) knife)
            {
                int i = waiting.Count;

                while (i > nextGo && waiting[i - 1].Go > knife.Go)
                    i--;

                waiting.Insert(i, knife);
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                float speed = stageBulletSpeed(Period) * DanmakusuBalance.LOUISE_KNIFE_SPEED;

                while (nextGo < waiting.Count && waiting[nextGo].Go <= now)
                {
                    var (go, at, radius, style) = waiting[nextGo++];
                    Vector2 toPlayer = f.characterPosition - at;
                    Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                    Vector2 velocity = way * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = go,
                        P0 = at,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = style,
                        Shape = BulletShape.Knife,
                        Untraced = true,
                        Until = go + f.stageLife(velocity),
                    });
                }

                // (what has gone is let go of now and then)
                if (nextGo > 4096)
                {
                    waiting.RemoveRange(0, nextGo);
                    nextGo = 0;
                }
            }
        }
    }
}
