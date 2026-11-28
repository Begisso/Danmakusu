using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>A bullet that was on the screen when a map of a marathon was over: where it was, how it flew and how it looked (see <see cref="BulletField.CarryOff"/>).</summary>
    public readonly struct CarriedBullet
    {
        public readonly Vector2 Position;

        /// <summary>Pixels per ms.</summary>
        public readonly Vector2 Velocity;

        public readonly Vector2 Face;
        public readonly float Radius;
        public readonly float Hit;
        public readonly Color4 Colour;
        public readonly BulletShape Shape;
        public readonly int Tint;
        public readonly float TintAmount;
        public readonly float Shade;

        public CarriedBullet(Vector2 position, Vector2 velocity, Vector2 face, float radius, float hit, Color4 colour, BulletShape shape, int tint, float tintAmount, float shade)
        {
            Position = position;
            Velocity = velocity;
            Face = face;
            Radius = radius;
            Hit = hit;
            Colour = colour;
            Shape = shape;
            Tint = tint;
            TintAmount = tintAmount;
            Shade = shade;
        }
    }

    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Marathon (2026-11-28, "transfer remaining bullets on the screen to next map"). When a map of a marathon is over (its results are about to be shown) every bullet still on the screen - the rings'
        // and the acts' - is taken as it is: where it is, the way and speed it flies, its size, shape and colour (CarryOff). The next map of the marathon starts with them (CarryIn): they fly on from
        // there, straight, as bullets of their own that hurt as any bullet does, in the colour of the new map nearest to their own, and leave the screen. What is not a plain bullet stays behind: beams,
        // what an act draws as a picture of its own, what cannot hurt, Yukari's trains. They are put on the new map at its first frame; when its intro is skipped they go on from where they had got to.
        // ======================================================================================================================================================

        /// <summary>At most this many bullets go on to the next map.</summary>
        private const int most_carried = 4000;

        /// <summary>The way a bullet flies is taken from where it was this long before (ms).</summary>
        private const double carry_step_ms = 16;

        /// <summary>Every bullet of the rings and the acts that is on the screen now, as it is (nothing when the player has failed).</summary>
        public CarriedBullet[] CarryOff()
        {
            var into = new List<CarriedBullet>();

            if (failed || palette.Length == 0)
                return into.ToArray();

            double now = Time.Current;
            Vector2 low = stageMin, high = stageMax;

            // the rings
            for (int k = 0; k < activeCount && into.Count < most_carried; k++)
            {
                var emitter = emitters[active[k]];

                if (emitter.Shape == BulletShape.Train)
                    continue;

                double ringTime = emitter.WithoutFrozenTime(effects.Effective(emitter, now));
                double age = ringTime - emitter.Time;

                if (age < 0 || age > emitter.Lifetime || emitter.RemovedAt(now))
                    continue;

                BulletLook? frozenLook = emitter.FreezeLookAt(now);
                Color4 colour = palette[((styleOf(emitter) % palette.Length) + palette.Length) % palette.Length];

                for (int b = 0; b < emitter.Count && into.Count < most_carried; b++)
                {
                    ulong bit = 1UL << b;

                    if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                        continue; // (what has hit, gone into the moon, or was never there)

                    emitter.Evaluate(b, ringTime, out Vector2 position, out Vector2 direction, out bool alive, out BulletLook? look);

                    if (!alive || position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y)
                        continue;

                    look ??= frozenLook;

                    if (look is { Harmless: true })
                        continue;

                    // (how it was flying: from where it was a moment before; what stands still - frozen - or jumped - a portal, a mirroring - flies as its ring does)
                    emitter.Evaluate(b, ringTime - carry_step_ms, out Vector2 before, out _, out bool aliveBefore);
                    Vector2 velocity = aliveBefore ? (position - before) / (float)carry_step_ms : Vector2.Zero;
                    float most = Math.Max(emitter.SpeedOf(b), 0.02f) * 4f;

                    if (velocity.LengthSquared < 1e-8f || velocity.Length > most)
                        velocity = emitter.VelocityAt(b, ringTime, direction);

                    float scale = (emitter.KeepsSize ? 1f : look?.Scale ?? 1f) * emitter.BigScale(b);
                    bool ownLook = look is { Overlay: > 0 };

                    into.Add(new CarriedBullet(position, velocity, direction, Math.Max(leastBulletRadius, emitter.VisualRadius * scale), ringHitRadius(emitter, 1f) * scale, colour,
                        look?.Shape ?? emitter.ShapeOf(b),
                        ownLook ? look!.Overlay : emitter.AltTint > 0 && b % 2 == 1 ? emitter.AltTint : emitter.Tint,
                        ownLook ? look!.OverlayAmount : emitter.TintAmount,
                        emitter.Shade));
                }
            }

            // the acts' bullets
            double slack = stageSlack;
            double allFreezes = freezeSpan(double.NegativeInfinity, double.PositiveInfinity);

            foreach (var act in stageActs)
            {
                if (act.Bullets.Count == 0 || now < act.FirstT0 || now > act.LastUntil + slack)
                    continue;

                for (int i = 0; i < act.Bullets.Count && into.Count < most_carried; i++)
                {
                    var bullet = act.Bullets[i];
                    bool traced = stageBulletTraced(bullet);

                    if (now < bullet.T0 || now > bullet.Until + (traced ? slack : 0) || now < bullet.GhostUntil || bullet.Hidden || bullet.Harmless || bullet.Shape == BulletShape.Train)
                        continue;

                    if (stageConsumed.Contains(act.Id * 65536 + i))
                        continue;

                    float grown = stageBulletScale(bullet, now);

                    if (grown < 0.02f)
                        continue;

                    Vector2 position, velocity;
                    BulletLook? look = null;

                    if (traced)
                    {
                        if (tracedStageBulletGone(bullet, now, allFreezes) || !tracedStageBullet(act, i, bullet, now, bullet.Radius, out position, out velocity, out look))
                            continue;
                    }
                    else
                    {
                        double own = stageClock(bullet, now);
                        position = stageBulletPosition(bullet, own);
                        velocity = stageBulletVelocity(bullet, own);

                        // (what goes round, sways or is moved step by step: the way it really went a moment before)
                        if (now - carry_step_ms >= bullet.T0)
                        {
                            Vector2 moved = (position - stageBulletPosition(bullet, stageClock(bullet, now - carry_step_ms))) / (float)carry_step_ms;

                            if (moved.LengthSquared > 1e-8f && moved.Length < 2f)
                                velocity = moved;
                        }
                    }

                    if (look is { Harmless: true } || position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y)
                        continue;

                    int tint = look is { Overlay: > 0 } ? look.Overlay : stageTint(bullet, now);
                    float amount = look is { Overlay: > 0 } ? look.OverlayAmount : stageTintAmount(bullet, now);
                    Vector2 face = bullet.KeepFace && bullet.Face.LengthSquared > 0f ? bullet.Face : velocity.LengthSquared > 1e-12f ? Vector2.Normalize(velocity) : bullet.Face.LengthSquared > 0f ? bullet.Face : Vector2.UnitX;

                    into.Add(new CarriedBullet(position, velocity, face, bullet.Radius * grown, (bullet.Hit > 0f ? bullet.Hit : bullet.Radius * 0.7f) * grown,
                        palette[((bullet.Style % palette.Length) + palette.Length) % palette.Length], look?.Shape ?? bullet.Shape, tint, amount, bullet.Shade));
                }
            }

            return into.ToArray();
        }

        /// <summary>This map of a marathon starts with the bullets the map before it left on the screen (see <see cref="CarryOff"/>).</summary>
        public void CarryIn(CarriedBullet[]? carried)
        {
            if (carried == null || carried.Length == 0)
                return;

            registerStage(new CarryAct(carried));
        }

        private sealed class CarryAct : StageAct
        {
            /// <summary>A jump of the time forward longer than this, early in the map, is its intro being skipped.</summary>
            private const double skip_ms = 300;

            /// <summary>What stands still is gone after this long (ms), fading out over the last half second of it; nothing stays longer than <see cref="most_ms"/>.</summary>
            private const double still_ms = 3000;

            private const double most_ms = 20000;

            private readonly CarriedBullet[] carried;

            // (when they were put on this map, how long they had flown by then - an intro skipped -, and the last frame's time)
            private double anchor = double.NaN;
            private double flown;
            private double last;

            public CarryAct(CarriedBullet[] carried)
            {
                this.carried = carried;
                Period = new BossPeriod { Rank = -1, Start = 0, ShootStart = 0, End = 0 }; // (no boss's: none of them does anything with it)
            }

            public override void Update(BulletField f, double now)
            {
                if (double.IsNaN(anchor))
                {
                    anchor = last = now;
                    place(f);
                    return;
                }

                // (the intro is skipped: they go on from where they had got to, not from where the jump would have flown them)
                if (now - last > skip_ms && last - anchor < most_ms)
                {
                    flown += last - anchor;
                    anchor = now;
                    place(f);
                }

                last = now;
            }

            private void place(BulletField f)
            {
                ClearBullets();

                foreach (var c in carried)
                {
                    Vector2 at = c.Position + c.Velocity * (float)flown;
                    bool still = c.Velocity.LengthSquared < 1e-4f;
                    double life = still ? still_ms - flown : Math.Min(f.stageLife(c.Velocity), most_ms);
                    float margin = c.Radius * 1.8f;

                    // (on the first frame the field may not know the screen yet: all of them are there then, they were on the screen)
                    bool there = life > 0 && (flown <= 0 || !f.hasScreenBounds
                                              || (at.X > f.stageMin.X - margin && at.X < f.stageMax.X + margin && at.Y > f.stageMin.Y - margin && at.Y < f.stageMax.Y + margin));

                    // (every one keeps its place in the list, gone or not: what has hit the player is known by it)
                    AddBullet(new StageBullet
                    {
                        T0 = anchor,
                        P0 = at,
                        V = c.Velocity,
                        Face = c.Face,
                        Radius = c.Radius,
                        Hit = c.Hit,
                        Style = nearestStyle(f.palette, c.Colour),
                        Shape = c.Shape,
                        Tint = c.Tint,
                        TintAmount = c.TintAmount,
                        Shade = c.Shade,
                        Untraced = true,
                        Until = there ? anchor + life : anchor - 1,
                        AlphaFrom = still ? anchor + life - 500 : 0,
                        AlphaOver = still ? 500 : 0,
                    });
                }
            }

            /// <summary>The colour of the new map's palette nearest to the one a bullet had.</summary>
            private static int nearestStyle(Color4[] palette, Color4 colour)
            {
                int best = 0;
                float nearest = float.MaxValue;

                for (int i = 0; i < palette.Length; i++)
                {
                    float r = palette[i].R - colour.R, g = palette[i].G - colour.G, b = palette[i].B - colour.B;
                    float distance = r * r + g * g + b * b;

                    if (distance < nearest)
                    {
                        nearest = distance;
                        best = i;
                    }
                }

                return best;
            }
        }
    }
}
