using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Eternity Larva: all through her section (in a spinner too) she shoots rings of sixteen butterflies every four beats (eight until 2026-11-14), the first at the player (the user's picture of a blue morpho, flapping its wings): they drift
        // out at a bit more than half a plain bullet's speed and wander from side to side as Wriggle's fireflies do, facing the way they go.
        // When her section begins and every eight beats after, every twentieth bullet on the screen (anyone's, counted in the order of the map's rings, and her butterflies after them; a butterfly that is chosen
        // goes on as a plain bullet of its colour) is warned of as Patchouli's fire is: it turns aquamarine, flies at
        // half its speed and has a red ring round it as big as it will grow, flashing faster and faster in the last two beats. Four beats later it swells, with Mokou's impact sound, to a big round bullet two circle
        // radii wide (drawn as sharp as Mokou's ball; hitbox too), glowing, and flies on at its speed; over the last beat of the next four it shrinks back, and it flies on as it was, aquamarine.
        // ======================================================================================================================================================

        private sealed class MetamorphosisAct : StageAct
        {
            private struct Seen
            {
                public BulletEmitter Emitter;
                public int Bullet;
                public Vector2 At;
                public Vector2 Velocity;
                public double Time;
                public int Order;
            }

            private const double look_ms = 80;
            private const float picture_aspect = 331f / 512f; // (the butterfly's picture: height over width)

            private readonly double beat;
            private readonly double[] volleys;
            private readonly Vector2[] volleyFrom;
            private readonly float[] volleyAim;
            private readonly bool[] volleyKnown;
            private readonly double[] marks;
            private readonly bool[] done;
            private readonly bool[] sounded;
            private readonly Dictionary<(BulletEmitter, int), Seen>[] seen;
            private readonly Dictionary<BulletEmitter, int> ringOrder = new Dictionary<BulletEmitter, int>();
            private readonly List<int> butterflies = new List<int>();
            private readonly List<int> warned = new List<int>();
            private readonly int perRing = DanmakusuBalance.LARVA_RING_BUTTERFLIES;
            private double resetTime = double.NegativeInfinity;

            public MetamorphosisAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.LARVA_RING_EVERY_BEATS)
                    times.Add(at);

                volleys = times.ToArray();
                volleyFrom = new Vector2[volleys.Length];
                volleyAim = new float[volleys.Length];
                volleyKnown = new bool[volleys.Length];

                times.Clear();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.LARVA_EVERY_BEATS)
                    times.Add(at);

                marks = times.ToArray();
                done = new bool[marks.Length];
                sounded = new bool[marks.Length];
                seen = new Dictionary<(BulletEmitter, int), Seen>[marks.Length];

                for (int i = 0; i < seen.Length; i++)
                    seen[i] = new Dictionary<(BulletEmitter, int), Seen>();

                for (int i = 0; i < f.emitters.Length; i++)
                    ringOrder[f.emitters[i]] = i;

                // (with another boss on the screen as many butterflies as any boss keeps then: half, or a third)
                int sharing = period.Rank + 1;

                foreach (var other in f.bosses)
                {
                    if (other != period && other.Rank != period.Rank && !other.Borrowed && other.ShootStart < period.End && other.End > period.ShootStart)
                        sharing = Math.Max(sharing, other.Rank + 1);
                }

                if (sharing > 1)
                    perRing = Math.Max(1, (perRing + sharing - 1) / sharing);

                End = marks.Length > 0 ? Math.Max(period.End, marks[^1] + beat * (DanmakusuBalance.LARVA_WARN_BEATS + 4)) : period.End;
                AbsorbFrom = Start;
                AbsorbTo = marks.Length > 0 ? marks[^1] + look_ms : Start - 1;
            }

            /// <summary>What she swells last shrinks back after her section: she stays until it has.</summary>
            public override double MechanicsEnd => End;

            private double swellAt(int mark) => marks[mark] + beat * DanmakusuBalance.LARVA_WARN_BEATS;

            private double backAt(int mark) => swellAt(mark) + beat * 4;

            private int markAt(double t)
            {
                for (int i = 0; i < marks.Length; i++)
                {
                    if (t >= marks[i] && t <= marks[i] + look_ms)
                        return i;
                }

                return -1;
            }

            public override bool Absorbs(double t) => markAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double mark in marks)
                {
                    if (mark <= to && mark + look_ms >= from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                // (the butterflies are shot again from where she was; the rings are looked at again from when they were fired, and the same bullets are warned of)
                ClearBullets();
                butterflies.Clear();
                warned.Clear();
                resetTime = time;

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleys[k] > time)
                        volleyKnown[k] = false;
                    else if (volleyKnown[k])
                        shoot(f, k);
                }

                for (int m = 0; m < marks.Length; m++)
                {
                    done[m] = false;
                    seen[m].Clear();
                    sounded[m] = swellAt(m) <= time;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                for (int k = 0; k < volleys.Length && volleys[k] <= now; k++)
                {
                    if (volleyKnown[k])
                        continue;

                    volleyKnown[k] = true;
                    volleyFrom[k] = f.bossVisualOf(Period);
                    Vector2 toPlayer = f.characterPosition - volleyFrom[k];
                    volleyAim[k] = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    shoot(f, k);
                }

                for (int m = 0; m < marks.Length && swellAt(m) <= now; m++)
                {
                    if (sounded[m])
                        continue;

                    sounded[m] = true;

                    if (now - swellAt(m) < 250 && swellAt(m) > resetTime)
                        f.playStageSound("mokou_wall_hit");
                }
            }

            // ---- her butterflies ----

            /// <summary>Ring <paramref name="k"/> of butterflies: from where she was, the first at the player, every second ring turned half a step.</summary>
            private void shoot(BulletField f, int k)
            {
                double t = volleys[k];
                float radius = Math.Max(Period.StarRadius, 2f);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.LARVA_SPEED;

                for (int i = 0; i < perRing; i++)
                {
                    float angle = volleyAim[k] + (i + (k % 2) * 0.5f) * MathF.Tau / perRing;
                    Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 velocity = way * speed;
                    double until = t + Math.Min(120000, f.stageLife(velocity));
                    int index = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = volleyFrom[k] + way * DanmakusuBalance.SPIRAL_SPAWN_RADIUS,
                        V = velocity,
                        Face = way,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(i, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Hidden = true, // (the act draws it: as a butterfly)
                        Until = until,
                        AlphaFrom = until - DanmakusuBalance.WRIGGLE_TURN_MS,
                        AlphaOver = DanmakusuBalance.WRIGGLE_TURN_MS,
                        Wander = f.circleRadius * DanmakusuBalance.WRIGGLE_WANDER_CIRCLES,
                        WanderPace = DanmakusuBalance.WRIGGLE_WANDER_PACE,
                        WanderPhase = hash01(k, i, 211) * MathF.Tau,
                    });

                    if (Bullets.Count > index)
                        butterflies.Add(index);
                }
            }

            // ---- the bullets that swell ----

            /// <summary>Takes nothing: notes every bullet on the screen at a mark (fired before it), where it is and how fast it goes.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Sharp || emitter.Shape == BulletShape.Train)
                    return false;

                int mark = markAt(t);

                if (mark < 0 || done[mark] || emitter.Time > marks[mark])
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                {
                    seen[mark].Remove((emitter, bullet));
                    return false;
                }

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                Vector2 way = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitY;
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);

                seen[mark][(emitter, bullet)] = new Seen { Emitter = emitter, Bullet = bullet, At = to, Velocity = way * speed, Time = t, Order = ringOrder.TryGetValue(emitter, out int order) ? order : int.MaxValue };
                return false;
            }

            /// <summary>At a mark every twentieth of what was seen (in the order of the map's rings) is taken out of its ring and warned of; four beats later it swells, and four beats after that it is as it was.</summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int m = 0; m < marks.Length; m++)
                {
                    if (done[m] || now < marks[m])
                        continue;

                    done[m] = true;

                    var all = new List<Seen>(seen[m].Values);
                    seen[m].Clear();
                    all.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Bullet.CompareTo(b.Bullet));

                    // (her butterflies on the screen at the mark are counted too, after the rings: they may swell as well; 2026-11-14)
                    var bugs = new List<int>();

                    foreach (int index in butterflies)
                    {
                        var bug = Bullets[index];

                        if (bug.T0 > marks[m] || bug.Until <= marks[m] || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                            continue;

                        Vector2 at = stageBulletPosition(bug, marks[m]);

                        if (at.X >= f.stageMin.X && at.X <= f.stageMax.X && at.Y >= f.stageMin.Y && at.Y <= f.stageMax.Y)
                            bugs.Add(index);
                    }

                    for (int i = DanmakusuBalance.LARVA_SWELL_EVERY - 1; i < all.Count + bugs.Count; i += DanmakusuBalance.LARVA_SWELL_EVERY)
                    {
                        if (i >= all.Count)
                        {
                            swellButterfly(f, m, bugs[i - all.Count]);
                            continue;
                        }

                        var one = all[i];
                        ulong bit = 1UL << one.Bullet;

                        if (((one.Emitter.ConsumedMask | one.Emitter.AbsorbedMask | one.Emitter.HiddenMask) & bit) != 0)
                            continue;

                        one.Emitter.AbsorbedMask |= bit;
                        var stored = f.makeStored(one.Emitter, one.Time);
                        float big = one.Emitter.BigScale(one.Bullet);

                        swell(f, m, one.At, one.Velocity, one.Time, new Look
                        {
                            Radius = stored.Radius * big, Hit = stored.Hit * big, Style = stored.Style, Shape = stored.Shape, Tint = stored.Tint, TintAmount = stored.Tint > 0 ? stored.TintAmount : 0f,
                            Shade = stored.Shade, Blades = stored.Blades, BladeSpin = stored.BladeSpin,
                        });
                    }
                }
            }

            /// <summary>What a bullet that swells looks like before and after (its own: a ring's bullet as it was; a butterfly a plain bullet of its colour).</summary>
            private struct Look
            {
                public float Radius, Hit, TintAmount, Shade, BladeSpin;
                public int Style, Tint, Blades;
                public BulletShape Shape;
            }

            /// <summary>A butterfly that is chosen: it is gone from where it is at the mark, and goes on from there as a bullet of its colour that is warned of and swells, as any other.</summary>
            private void swellButterfly(BulletField f, int mark, int index)
            {
                var bug = Bullets[index];
                double at = marks[mark];
                Vector2 position = stageBulletPosition(bug, at);
                Vector2 velocity = stageBulletVelocity(bug, at);

                bug.Until = at;
                bug.AlphaFrom = Math.Min(bug.AlphaFrom, at);
                bug.AlphaOver = 1;
                Bullets[index] = bug;

                swell(f, mark, position, velocity, at, new Look { Radius = bug.Radius, Hit = bug.Hit, Style = bug.Style, Shape = BulletShape.Circle, Shade = bug.Shade });
            }

            private void swell(BulletField f, int mark, Vector2 oneAt, Vector2 oneVelocity, double oneTime, in Look look)
            {
                var one = (At: oneAt, Velocity: oneVelocity, Time: oneTime);
                var stored = look;
                float radius = look.Radius;
                float hit = look.Hit;
                double swellsAt = swellAt(mark), backs = backAt(mark);

                // the warning: aquamarine, at half its speed, with the red ring round it (drawn below)
                Vector2 slow = one.Velocity * DanmakusuBalance.LARVA_WARN_SLOW;
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = one.Time,
                    P0 = one.At,
                    V = slow,
                    Face = slow.LengthSquared > 1e-10f ? Vector2.Normalize(slow) : Vector2.UnitY,
                    Radius = radius,
                    Hit = hit,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Tint2At = one.Time,
                    Tint2 = BulletRenderer.TINT_AQUAMARINE,
                    Tint2Amount = 1f,
                    Tint2Over = 250,
                    Shade = stored.Shade,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Untraced = true, // (it swells where this puts it: the ops of the others leave it be)
                    Until = swellsAt,
                });

                if (Bullets.Count == index)
                    return;

                warned.Add(index);

                Vector2 there = one.At + slow * (float)(swellsAt - one.Time);
                float margin = f.circleRadius * DanmakusuBalance.LARVA_SWELL_CIRCLES;

                if (there.X < f.stageMin.X - margin || there.X > f.stageMax.X + margin || there.Y < f.stageMin.Y - margin || there.Y > f.stageMax.Y + margin)
                    return; // (it has left the screen before it could swell)

                // the swell: a big round bullet, as sharp as Mokou's ball, at its own speed again; it shrinks back over the last beat
                float swollen = f.circleRadius * DanmakusuBalance.LARVA_SWELL_CIRCLES;

                AddBullet(new StageBullet
                {
                    T0 = swellsAt,
                    P0 = there,
                    V = one.Velocity,
                    Radius = radius,
                    Hit = hit,
                    Style = stored.Style,
                    Shape = BulletShape.Circle,
                    Tint = BulletRenderer.TINT_AQUAMARINE,
                    TintAmount = 1f,
                    ScaleFrom = swellsAt,
                    ScaleOver = DanmakusuBalance.LARVA_SWELL_MS,
                    ScaleTo = Math.Max(swollen / Math.Max(radius, 1f), 1f),
                    ScaleBackAt = backs - beat * DanmakusuBalance.LARVA_SHRINK_BEATS,
                    ScaleBackOver = beat * DanmakusuBalance.LARVA_SHRINK_BEATS,
                    Bloom = DanmakusuBalance.LARVA_SWELL_BLOOM,
                    Untraced = true,
                    Until = backs,
                });

                // and then it flies on as it was, aquamarine
                Vector2 after = there + one.Velocity * (float)(backs - swellsAt);

                AddBullet(new StageBullet
                {
                    T0 = backs,
                    P0 = after,
                    V = one.Velocity,
                    Face = one.Velocity.LengthSquared > 1e-10f ? Vector2.Normalize(one.Velocity) : Vector2.UnitY,
                    Radius = radius,
                    Hit = hit,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_AQUAMARINE,
                    TintAmount = 1f,
                    Shade = stored.Shade,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Until = backs + f.stageLife(one.Velocity),
                });
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                // the butterflies: facing the way they go, flapping
                foreach (int index in butterflies)
                {
                    var bug = Bullets[index];

                    if (t < bug.T0 || t > bug.Until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    Vector2 position = stageBulletPosition(bug, t);

                    if (position.X < f.stageMin.X - 60 || position.X > f.stageMax.X + 60 || position.Y < f.stageMin.Y - 60 || position.Y > f.stageMax.Y + 60)
                        continue;

                    Vector2 way = position - stageBulletPosition(bug, Math.Max(t - 40, bug.T0 - 40));

                    if (way.LengthSquared < 1e-8f)
                        way = bug.Face;

                    float heading = MathF.Atan2(way.Y, way.X) * 180f / MathF.PI + 90f;
                    float flap = 0.3f + 0.7f * MathF.Abs(MathF.Cos((float)((t - bug.T0) / DanmakusuBalance.LARVA_FLAP_MS * Math.PI) + bug.WanderPhase));
                    float span = bug.Radius * DanmakusuBalance.LARVA_WINGSPAN;
                    float alpha = smoothStep((t - bug.T0) / 200.0) * (1f - smoothStep((t - bug.AlphaFrom) / Math.Max(bug.AlphaOver, 1.0)));

                    f.stageBack.Add(StageTexture.Glow, position, new Vector2(span * 1.6f), 0f, 0.3f * alpha, new Color4(0.3f, 0.75f, 1f, 1f), true);
                    f.stageBats.Add(StageTexture.Butterfly, position, new Vector2(span * flap, span * picture_aspect), heading, alpha, Color4.White);
                }

                // the warning: a red ring as big as it will swell, flashing faster and faster in the last two beats (as Patchouli's fire)
                float ringRadius = f.circleRadius * DanmakusuBalance.LARVA_SWELL_CIRCLES;
                float ringHalf = Math.Max(ringRadius * 0.07f, 1f);
                double flashSpan = beat * DanmakusuBalance.PATCHOULI_FIRE_FLASH_BEATS;
                int rings = 0;

                foreach (int index in warned)
                {
                    if (rings >= DanmakusuBalance.PATCHOULI_FIRE_MAX_RINGS)
                        break;

                    var bullet = Bullets[index];
                    double grows = bullet.Until;

                    if (t < bullet.T0 || t > grows + DanmakusuBalance.LARVA_SWELL_MS || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    Vector2 position = stageBulletPosition(bullet, Math.Min(t, grows));

                    if (position.X < f.stageMin.X - ringRadius || position.X > f.stageMax.X + ringRadius || position.Y < f.stageMin.Y - ringRadius || position.Y > f.stageMax.Y + ringRadius)
                        continue;

                    float alpha = 0.9f * smoothStep((t - bullet.T0) / 250.0) * (1f - smoothStep((t - grows) / DanmakusuBalance.LARVA_SWELL_MS));
                    double flash = (t - (grows - flashSpan)) / flashSpan;

                    if (flash > 0 && t < grows)
                    {
                        double phase = DanmakusuBalance.PATCHOULI_FIRE_FLASHES * flash * flash * Math.PI * 2;
                        float on = 0.5f + 0.5f * (float)Math.Cos(phase);
                        alpha = Math.Min(1f, alpha * (0.35f + 0.65f * on) + 0.3f * on * (float)flash);
                        f.stageRipple.AddRing(position, ringRadius + ringHalf * 2f, ringHalf * 2f, 0.5f * on * (float)flash, new Color4(1f, 0.55f, 0.35f, 1f));
                    }

                    if (alpha > 0.02f)
                    {
                        f.stageRipple.AddRing(position, ringRadius + ringHalf, ringHalf, alpha, new Color4(0.95f, 0.1f, 0.06f, 1f));
                        rings++;
                    }
                }
            }
        }
    }
}
