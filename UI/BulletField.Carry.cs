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

    /// <summary>
    /// A beam that was on the screen when a map of a marathon was over (2026-11-28, "carry beams to the next map too"): its points as they were, each flying on the way its slice was going, with its half
    /// width at each, in its colours (see <see cref="BulletField.CarryOffBeams"/>).
    /// </summary>
    public sealed class CarriedBeam
    {
        public Vector2[] Points = Array.Empty<Vector2>();

        /// <summary>Pixels per ms, every point its own.</summary>
        public Vector2[] Velocities = Array.Empty<Vector2>();

        public float[] HalfWidths = Array.Empty<float>();
        public Color4 Colour;
        public Color4 Border;
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

        /// <summary>At most this many points of beams go on to the next map.</summary>
        private const int most_carried_points = 20000;

        /// <summary>
        /// Every laser beam of the map that is on the screen now, as it is (2026-11-28, "carry beams to the next map too"): its points, each flying on the way its slice goes - a beam that turns, out from
        /// where it is fired; any other, the way it was fired - at the beam's speed, with its widths and colours. Not what an act draws as a beam of its own (it does not fly on its own way), nor
        /// what cannot hurt (nothing when the player has failed).
        /// </summary>
        public CarriedBeam[] CarryOffBeams()
        {
            var into = new List<CarriedBeam>();

            if (failed || palette.Length == 0)
                return into.ToArray();

            double now = Time.Current;
            Vector2 low = stageMin, high = stageMax;
            int points = 0;
            int lo = laserLowerBound(now - maxLaserSpan), hi = laserLowerBound(now + 0.001);

            for (int i = lo; i < hi && points < most_carried_points; i++)
            {
                var laser = lasers[i];

                if (laser.Finished || now > laser.EndTime + laser.Lifetime)
                    continue;

                var colour = laser.SpinnerLike && backgroundColour is Color4 background ? background : palette[laserStyle(laser)];
                var border = laser.SpinnerLike && backgroundColour is Color4 ? DanmakusuBalance.BorderColour(colour) : borderPalette[laserStyle(laser) % borderPalette.Length];

                for (int beam = 0; beam < laser.BeamCount && points < most_carried_points; beam++)
                {
                    if (laser.BeamFinished[beam] || (laser.BeamGoneAt != null && now > laser.BeamGoneAt[beam]))
                        continue;

                    int n = laser.GetPoints(now, laserBuffer, beam, laserWidthBuffer, laserDistanceBuffer, laserLookBuffer);

                    if (n < 2)
                        continue;

                    bool seen = false, hurts = false;

                    for (int q = 0; q < n; q++)
                    {
                        Vector2 p = laserBuffer[q];
                        seen |= p.X >= low.X && p.X <= high.X && p.Y >= low.Y && p.Y <= high.Y;
                        hurts |= laserLookBuffer[q]?.Harmless != true;
                    }

                    if (!seen || !hurts)
                        continue;

                    var carried = new CarriedBeam
                    {
                        Points = new Vector2[n],
                        Velocities = new Vector2[n],
                        HalfWidths = new float[n],
                        Colour = colour,
                        Border = border,
                    };

                    for (int q = 0; q < n; q++)
                    {
                        Vector2 p = laserBuffer[q];
                        Vector2 way = laser.Rotates ? p - laser.Origin : laser.Directions[beam];
                        carried.Points[q] = p;
                        carried.Velocities[q] = way.LengthSquared > 1e-8f ? Vector2.Normalize(way) * laser.Speed : Vector2.Zero;
                        carried.HalfWidths[q] = laserWidthBuffer[q];
                    }

                    into.Add(carried);
                    points += n;
                }
            }

            return into.ToArray();
        }

        /// <summary>This map of a marathon starts with the bullets (and since 2026-11-28 the beams) the map before it left on the screen (see <see cref="CarryOff"/>, <see cref="CarryOffBeams"/>).</summary>
        public void CarryIn(CarriedBullet[]? carried, CarriedBeam[]? beams = null)
        {
            if ((carried == null || carried.Length == 0) && (beams == null || beams.Length == 0))
                return;

            registerStage(new CarryAct(carried ?? Array.Empty<CarriedBullet>(), beams ?? Array.Empty<CarriedBeam>()));
        }

        private sealed class CarryAct : StageAct
        {
            /// <summary>A jump of the time forward longer than this, early in the map, is its intro being skipped.</summary>
            private const double skip_ms = 300;

            /// <summary>What stands still is gone after this long (ms), fading out over the last half second of it; nothing stays longer than <see cref="most_ms"/>.</summary>
            private const double still_ms = 3000;

            private const double most_ms = 20000;

            private readonly CarriedBullet[] carried;
            private readonly CarriedBeam[] beams;
            private readonly double[] beamUntil;

            // (when they were put on this map, how long they had flown by then - an intro skipped -, and the last frame's time)
            private double anchor = double.NaN;
            private double flown;
            private double last;

            public CarryAct(CarriedBullet[] carried, CarriedBeam[] beams)
            {
                this.carried = carried;
                this.beams = beams;
                beamUntil = new double[beams.Length];
                Array.Fill(beamUntil, double.NegativeInfinity);
                Period = new BossPeriod { Rank = -1, Start = 0, ShootStart = 0, End = 0 }; // (no boss's: none of them does anything with it)
            }

            // ---- the beams (2026-11-28, "carry beams to the next map too"): every point flies on its own way; drawn and hurting as a beam does, until all of it has left the screen ----

            private readonly List<Vector2> beamLine = new List<Vector2>(64);
            private readonly List<float> beamWidths = new List<float>(64);
            private readonly GridShapes grid = new GridShapes();

            /// <summary>Beam <paramref name="k"/> at <paramref name="t"/> into <see cref="beamLine"/> / <see cref="beamWidths"/> (false: it is gone then, or not put here yet).</summary>
            private bool beamAt(int k, double t)
            {
                beamLine.Clear();
                beamWidths.Clear();

                if (double.IsNaN(anchor) || t < anchor || t > beamUntil[k])
                    return false;

                var beam = beams[k];
                float age = (float)(flown + t - anchor);

                for (int q = 0; q < beam.Points.Length; q++)
                {
                    beamLine.Add(beam.Points[q] + beam.Velocities[q] * age);
                    beamWidths.Add(beam.HalfWidths[q]);
                }

                return beamLine.Count >= 2;
            }

            public override void DrawBeams(BulletField f, double t, ref int drawn, ref int pointTotal)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < beams.Length; k++)
                {
                    if (!beamAt(k, t))
                        continue;

                    float widest = 0f;

                    foreach (float w in beamWidths)
                        widest = Math.Max(widest, w);

                    f.addPlainBeam(beamLine, widest, beams[k].Colour, beams[k].Border, ref drawn, ref pointTotal, beamWidths);
                }
            }

            private bool beamsTouch(double t, Vector2 player, float extra, MomentShapes.Moment? into)
            {
                for (int k = 0; k < beams.Length; k++)
                {
                    if (!beamAt(k, t))
                        continue;

                    for (int q = 0; q < beamLine.Count; q++)
                    {
                        float reach = beamWidths[q] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + extra;

                        if (into != null)
                            into.Point(beamLine[q], reach);
                        else if (q > 0 && distanceToSegment(player, beamLine[q - 1], beamLine[q]) <= reach)
                            return true;
                    }

                    into?.EndRun();
                }

                return false;
            }

            /// <summary>The beams hurt as a beam does.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || beams.Length == 0)
                    return false;

                if (autoAsking)
                {
                    var shape = grid.Get(now, f.Time.Current, out double at, out bool fresh);

                    if (fresh)
                    {
                        float fastest = 0f;

                        foreach (var beam in beams)
                        {
                            foreach (var v in beam.Velocities)
                                fastest = Math.Max(fastest, v.Length);
                        }

                        beamsTouch(at, default, fastest * (float)(GridShapes.GRID_MS / 2), shape);
                    }

                    return shape.Touches(f.characterPosition);
                }

                return beamsTouch(now, f.characterPosition, 0f, null);
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
                grid.Forget();

                // (the beams: each there until the slowest of its points that can still be seen has left - none that has flown out in an intro skipped)
                for (int k = 0; k < beams.Length; k++)
                {
                    var beam = beams[k];
                    double life = 0;
                    bool on = flown <= 0 || !f.hasScreenBounds;

                    for (int q = 0; q < beam.Points.Length; q++)
                    {
                        Vector2 at = beam.Points[q] + beam.Velocities[q] * (float)flown;
                        float margin = beam.HalfWidths[q] * 2f;
                        on |= at.X > f.stageMin.X - margin && at.X < f.stageMax.X + margin && at.Y > f.stageMin.Y - margin && at.Y < f.stageMax.Y + margin;
                        life = Math.Max(life, beam.Velocities[q].LengthSquared < 1e-8f ? still_ms - flown : Math.Min(f.stageLife(beam.Velocities[q]), most_ms));
                    }

                    beamUntil[k] = on && life > 0 ? anchor + life : anchor - 1;
                }

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
