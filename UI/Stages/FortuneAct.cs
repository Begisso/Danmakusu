using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Joon Yorigami: every two beats of her section every fifth bullet on the screen turns gold, glows and is pulled at Zanmu's pull, from where it was and the way it was going. Its gravity points the way it was going when it turned
        // (each bullet has its own), and stays so. It bounces off any wall of the screen as a ball does: every bounce leaves it 70 % of its energy, and it bounces twice; after that its gravity takes it out of the screen. In an attack the share grows,
        // from every fifth bullet at its start to every second at its end, and starts over with the next attack. Her own bullets are golden (the planner's) and fly the same way from the moment she throws them, at a quarter of the pull.
        // The flight is worked out when the bullet turns, so a replay and a seek give the same.
        // ======================================================================================================================================================

        private sealed class FortuneAct : StageAct
        {
            private const double flight_step = 10;
            private const double mark_ms = 60; // (how long a mark lasts: the bullets are looked at once, frames are not as short as a moment)
            private const double own_catch_ms = 200; // (her own bullets are taken over the moment they are thrown: this long after her section too, for those of its last moment)

            private readonly double[] marks;
            private readonly List<(double Start, double End)> attacks;
            private readonly List<int> bounced = new List<int>();

            public FortuneAct(BossPeriod period, double[] marks, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.marks = marks;
                this.attacks = attacks;
                Start = Math.Min(marks[0], period.ShootStart);
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + own_catch_ms;
            }

            private int markAt(double t)
            {
                int low = 0, high = marks.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (marks[middle] + mark_ms <= t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low < marks.Length && marks[low] <= t ? low : -1;
            }

            public override void Reset(BulletField f, double time) => ClearBullets();

            /// <summary>Is this one of her own bullets? (only hers have a pull and do not fall by themselves: see makeJoonBullets)</summary>
            private static bool own(BulletEmitter emitter) => emitter.Gravity > 0f && !emitter.Falling;

            /// <summary>Which bullets turn at <paramref name="t"/>: every tenth, in an attack more and more, up to every second at its end.</summary>
            private int everyAt(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (end > start && t >= start && t < end)
                        return (int)Math.Round(DanmakusuBalance.JOON_EVERY + (DanmakusuBalance.JOON_EVERY_LAST - DanmakusuBalance.JOON_EVERY) * (t - start) / (end - start));
                }

                return DanmakusuBalance.JOON_EVERY;
            }

            /// <summary>The way of a bullet that is pulled from <paramref name="position"/> the way it was going (straight down if it was standing still) and bounces twice, a point every <see cref="flight_step"/> ms, until it has left the screen.</summary>
            private static List<Vector2> flight(BulletField f, Vector2 position, Vector2 velocity, float radius, float gravity, List<int> bounced)
            {
                var way = new List<Vector2>(256) { position };
                float dt = (float)flight_step;
                float kept = MathF.Sqrt(DanmakusuBalance.JOON_ENERGY_KEPT); // (a share of the energy: the speed times its root)
                float low = f.stageMin.X + radius, high = f.stageMax.X - radius, floor = f.stageMax.Y - radius, ceiling = f.stageMin.Y + radius;
                int bounces = DanmakusuBalance.JOON_BOUNCES;
                Vector2 pull = (velocity.LengthSquared > 1e-10f ? Vector2.Normalize(velocity) : Vector2.UnitY) * (gravity * dt);

                while (way.Count < 2500)
                {
                    velocity += pull;
                    position += velocity * dt;

                    if (bounces > 0 && ((position.Y > floor && velocity.Y > 0f) || (position.Y < ceiling && velocity.Y < 0f)))
                    {
                        position.Y = Math.Clamp(position.Y, ceiling, floor);
                        velocity = new Vector2(velocity.X, -velocity.Y) * kept;
                        bounces--;
                        bounced.Add(way.Count);
                    }
                    else if (bounces > 0 && ((position.X < low && velocity.X < 0f) || (position.X > high && velocity.X > 0f)))
                    {
                        position.X = Math.Clamp(position.X, low, high);
                        velocity = new Vector2(-velocity.X, velocity.Y) * kept;
                        bounces--;
                        bounced.Add(way.Count);
                    }

                    way.Add(position);

                    if (position.Y > f.stageMax.Y + radius * 2f || position.Y < f.stageMin.Y - radius * 2f || position.X < f.stageMin.X - radius * 2f || position.X > f.stageMax.X + radius * 2f)
                        break;
                }

                return way;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                bool hers = own(emitter);

                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Deflector || emitter.Falling)
                    return false;

                // (hers as soon as they are thrown, in her section; the others at the marks, by the share of the moment)
                if (hers ? t > End + own_catch_ms : markAt(t) < 0 || t > End || (Math.Abs((long)emitter.Time) + bullet) % everyAt(t) != 0)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                Vector2 velocity = delta.LengthSquared > 1e-10f ? delta / (float)dt : Vector2.Zero;
                float big = emitter.BigScale(bullet);
                bounced.Clear();
                var way = flight(f, to, velocity, stored.Radius * big, hers ? emitter.Gravity : DanmakusuBalance.ZANMU_GRAVITY, bounced);
                int before = Bullets.Count;

                // (every bounce is a ricochet: it flashes where it will be)
                foreach (int point in bounced)
                    f.addFlash(t + point * flight_step, way[point], stored.Radius * big * DanmakusuBalance.RICOCHET_FLASH_SIZE);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_YELLOW,
                    TintAmount = DanmakusuBalance.TOYOHIME_GOLD_TINT,
                    FadeIn = hers ? 0 : DanmakusuBalance.JOON_TURN_MS, // (hers are gold already)
                    BloomAt = t,
                    BloomTo = DanmakusuBalance.TOYOHIME_GOLD_BLOOM,
                    Shade = stored.Shade,
                    Trail = way,
                    TrailStep = flight_step,
                    Until = t + way.Count * flight_step,
                });

                return Bullets.Count > before;
            }
        }
    }
}
