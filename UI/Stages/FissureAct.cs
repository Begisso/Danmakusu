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
        // Suika Ibuki: all through her section (in a spinner too) she stomps every four beats, where she stands: a ring goes out over the ground, everything within four circle radii of her (bullets of anyone)
        // is thrown on the way it was going, twice as fast, glowing like magma, with a narrow tail of beam two beats long behind it (it hurts like a slider body), and where it reaches the wall it bursts into four
        // ellipses half its size, fanned out over 90 degrees round the way it would bounce. The stomp hurts the player if they are in it, and thin glowing cracks in the ground round her come up at once and fade out
        // over two beats, hurting like a slider body meanwhile.
        // Every sixteen beats a wide crack of glowing magma, two circles across and as jagged as the World's Ender's cut, splits the ground from where she stands towards the player, out past the edge of the screen,
        // in a beat (Hecatia's cut sounds). It hurts like a slider body. Eight beats after it has grown it explodes (Hecatia's explosion): glowing magma ellipses fly off both sides all along it, as the World's
        // Ender's bullets do, and it cools away.
        // ======================================================================================================================================================

        private sealed class FissureAct : StageAct
        {
            private const int crack_points = 7; // (points of each thin crack)
            private const int fissure_steps = 96;

            private readonly double beat;
            private readonly double[] stomps;
            private readonly Vector2[] stompAt;
            private readonly bool[] stompKnown;
            private readonly HashSet<(BulletEmitter, int)>[] stompSeen;

            private readonly double[] fissures;
            private readonly Vector2[] fissureFrom;
            private readonly Vector2[] fissureWay;
            private readonly float[] fissureLength;
            private readonly bool[] fissureKnown;
            private readonly bool[] exploded;

            private readonly List<(int Index, double Until)> tails = new List<(int, double)>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private readonly List<Vector2> crack = new List<Vector2>();
            private double resetTime = double.NegativeInfinity;
            private int glowFrom;
            private double glowFromTime = double.NegativeInfinity;

            public FissureAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.SUIKA_STOMP_EVERY_BEATS)
                    times.Add(at);

                stomps = times.ToArray();
                stompAt = new Vector2[stomps.Length];
                stompKnown = new bool[stomps.Length];
                stompSeen = new HashSet<(BulletEmitter, int)>[stomps.Length];

                for (int k = 0; k < stompSeen.Length; k++)
                    stompSeen[k] = new HashSet<(BulletEmitter, int)>();

                times.Clear();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.SUIKA_FISSURE_EVERY_BEATS)
                    times.Add(at);

                fissures = times.ToArray();
                fissureFrom = new Vector2[fissures.Length];
                fissureWay = new Vector2[fissures.Length];
                fissureLength = new float[fissures.Length];
                fissureKnown = new bool[fissures.Length];
                exploded = new bool[fissures.Length];

                End = fissures.Length > 0 ? Math.Max(period.End, explodeAt(fissures.Length - 1) + beat) : period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = stomps.Length > 0 ? stomps[^1] + takeMs : Start - 1;
            }

            private double takeMs => beat * 0.5;

            private double grownAt(int k) => fissures[k] + beat * DanmakusuBalance.SUIKA_FISSURE_GROW_BEATS;

            private double explodeAt(int k) => grownAt(k) + beat * DanmakusuBalance.SUIKA_FISSURE_STAY_BEATS;

            /// <summary>Her last crack explodes after her section: she stays until it has.</summary>
            public override double MechanicsEnd => End;

            private float stompRadius(BulletField f) => f.circleRadius * DanmakusuBalance.SUIKA_STOMP_CIRCLES;

            private float fissureHalf(BulletField f) => f.circleRadius * DanmakusuBalance.SUIKA_FISSURE_CIRCLES;

            private static readonly Color4 magma = new Color4(1f, 0.38f, 0.08f, 1f);
            private static readonly Color4 magma_border = new Color4(0.45f, 0.06f, 0.02f, 1f);

            private int stompAtTime(double t)
            {
                for (int k = 0; k < stomps.Length; k++)
                {
                    if (t >= stomps[k] && t <= stomps[k] + takeMs)
                        return k;
                }

                return -1;
            }

            public override bool Absorbs(double t) => stompAtTime(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double stomp in stomps)
                {
                    if (stomp <= to && stomp + takeMs >= from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                // (what she stomped and blew up is made again: the rings are looked at again from when they were fired, and the explosions come from where the cracks were)
                ClearBullets();
                tails.Clear();
                glowFrom = 0;
                glowFromTime = double.NegativeInfinity;
                resetTime = time;

                for (int k = 0; k < stomps.Length; k++)
                {
                    stompSeen[k].Clear();

                    if (stomps[k] > time)
                        stompKnown[k] = false;
                }

                for (int k = 0; k < fissures.Length; k++)
                {
                    exploded[k] = false;

                    if (fissures[k] > time)
                        fissureKnown[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                for (int k = 0; k < stomps.Length && stomps[k] <= now; k++)
                {
                    if (stompKnown[k])
                        continue;

                    stompKnown[k] = true;
                    stompAt[k] = f.bossVisualOf(Period);

                    if (now - stomps[k] < 250 && stomps[k] > resetTime)
                        f.playStageSound("hecatia_earth_hit");
                }

                for (int k = 0; k < fissures.Length && fissures[k] <= now; k++)
                {
                    if (!fissureKnown[k])
                    {
                        fissureKnown[k] = true;
                        Vector2 from = f.bossVisualOf(Period);
                        Vector2 toPlayer = f.characterPosition - from;
                        Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                        fissureFrom[k] = from;
                        fissureWay[k] = way;
                        fissureLength[k] = (float)f.toWall(from, way, out _) + fissureHalf(f) * 2f; // (out past the edge of the screen)

                        if (now - fissures[k] < 250 && fissures[k] > resetTime)
                            f.playStageSound("hecatia_cut");
                    }

                    if (!exploded[k] && now >= explodeAt(k))
                    {
                        exploded[k] = true;
                        explode(f, k);

                        if (now - explodeAt(k) < 250 && explodeAt(k) > resetTime)
                            f.playStageSound("hecatia_explosion");
                    }
                }
            }

            // ---- the stomp ----

            /// <summary>What is in the stomp when it comes is thrown on, twice as fast, glowing; at the wall it bursts into four small ellipses.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Sharp || emitter.Shape == BulletShape.Train)
                    return false;

                int k = stompAtTime(t);

                if (k < 0 || !stompKnown[k] || emitter.Time > stomps[k] || !stompSeen[k].Add((emitter, bullet)))
                    return false; // (only what is there when she stomps, the first time it is seen)

                if (Vector2.Distance(to, stompAt[k]) > stompRadius(f))
                    return false;

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                Vector2 way = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : to - stompAt[k];

                if (way.LengthSquared < 1e-8f)
                    way = Vector2.UnitY;

                way = Vector2.Normalize(way);

                float pace = emitter.Ops == null && delta.LengthSquared > 1e-8f ? Math.Max(delta.Length / (float)dt, 0.02f) : stored.Speed;
                Vector2 velocity = way * (pace * DanmakusuBalance.SUIKA_STOMP_SPEED);
                double reach = f.toWall(to, velocity, out bool sideways);
                double wall = t + reach;
                float radius = stored.Radius * big;
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = radius,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_FIRE,
                    TintAmount = 1f,
                    Bloom = DanmakusuBalance.SUIKA_MAGMA_BLOOM,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    Untraced = true, // (its flight to the wall is known: the ops of the others leave it be)
                    Until = wall,
                });

                if (Bullets.Count == index)
                    return false; // (too many: it stays as it is)

                tails.Add((index, wall));

                // (at the wall: four ellipses half its size, at the speed it had, fanned out round the way it would bounce)
                Vector2 at = Vector2.Clamp(to + velocity * (float)reach, f.stageMin, f.stageMax);
                Vector2 bounce = sideways ? new Vector2(-way.X, way.Y) : new Vector2(way.X, -way.Y);
                float spread = DanmakusuBalance.SUIKA_WALL_SPREAD_DEGREES * MathF.PI / 180f;
                int pieces = DanmakusuBalance.SUIKA_WALL_PIECES;

                for (int p = 0; p < pieces; p++)
                {
                    Vector2 pieceVelocity = turned(bounce, spread * (p / (float)Math.Max(pieces - 1, 1) - 0.5f)) * pace;
                    float pieceRadius = radius * DanmakusuBalance.SUIKA_WALL_PIECE_SIZE;

                    AddBullet(new StageBullet
                    {
                        T0 = wall,
                        P0 = at,
                        V = pieceVelocity,
                        Radius = pieceRadius,
                        Hit = pieceRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = stored.Style,
                        Shape = BulletShape.Ellipse,
                        Tint = BulletRenderer.TINT_FIRE,
                        TintAmount = 1f,
                        Bloom = DanmakusuBalance.SUIKA_MAGMA_BLOOM * 0.5f,
                        Untraced = true, // (there are many: the ops of the others leave them be, or every one would be traced every frame)
                        Until = wall + f.stageLife(pieceVelocity),
                    });
                }

                return true;
            }

            /// <summary>Thin crack <paramref name="c"/> of stomp <paramref name="k"/> (into <see cref="crack"/>): from near her out to the edge of the stomp, jagged.</summary>
            private void thinCrack(BulletField f, int k, int c)
            {
                crack.Clear();

                float reach = stompRadius(f);
                float angle = (c + 0.6f * (hash01(k, c, 71) - 0.5f)) * MathF.Tau / DanmakusuBalance.SUIKA_CRACKS + k * 0.9f;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 side = new Vector2(-way.Y, way.X);
                float length = reach * (0.75f + 0.25f * hash01(k, c, 72));

                for (int i = 0; i < crack_points; i++)
                {
                    float u = i / (crack_points - 1f);
                    float jag = i == 0 ? 0f : (hash01(k * 31 + c, i, 73) - 0.5f) * reach * 0.22f;
                    crack.Add(stompAt[k] + way * (reach * 0.12f + (length - reach * 0.12f) * u) + side * jag);
                }
            }

            private double cracksMs => beat * DanmakusuBalance.SUIKA_CRACKS_BEATS;

            // ---- the big crack ----

            /// <summary>How far the line of the crack runs from straight at <paramref name="u"/> (0..1 along it), a share of its length: the World's Ender's cut.</summary>
            private static float profile(float u)
                => 0.035f * (0.5f * MathF.Sin(MathF.Tau * 1.3f * u + 0.7f) + 0.3f * MathF.Sin(MathF.Tau * 3.1f * u + 2.1f) + 0.2f * MathF.Sin(MathF.Tau * 7.3f * u + 4.4f) + 0.12f * MathF.Sin(MathF.Tau * 17.7f * u + 1.2f)
                            + 0.06f * MathF.Sin(MathF.Tau * 41.3f * u + 3.3f));

            private Vector2 lineAt(int k, float u)
            {
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                float length = fissureLength[k];
                // (it starts right at her: the jag grows in from nothing)
                return fissureFrom[k] + fissureWay[k] * (length * u) + side * (length * 0.5f * (profile(u) - profile(0f)) * smoothStep(u / 0.05f));
            }

            /// <summary>How wide the crack is on each side at <paramref name="u"/>: two circles across, uneven, narrower right at her.</summary>
            private float widthAt(BulletField f, int k, float u)
                => fissureHalf(f) * (0.85f + 0.2f * MathF.Sin(MathF.Tau * 2.3f * u + k) + 0.1f * MathF.Sin(MathF.Tau * 5.7f * u + 2.4f * k)) * (0.35f + 0.65f * smoothStep(u / 0.06f));

            /// <summary>How far along it has grown at <paramref name="t"/> (0..1).</summary>
            private float grownTo(int k, double t) => smoothStep((t - fissures[k]) / (grownAt(k) - fissures[k]));

            /// <summary>The explosion: glowing magma ellipses fly off both sides all along the crack (as the World's Ender's bullets: each at a speed, a size and a way of its own).</summary>
            private void explode(BulletField f, int k)
            {
                double t = explodeAt(k);
                float radius = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period);
                float spread = DanmakusuBalance.ENDER_SPREAD_DEGREES * MathF.PI / 180f;
                Vector2 side = new Vector2(-fissureWay[k].Y, fissureWay[k].X);
                int points = DanmakusuBalance.SUIKA_FISSURE_POINTS;

                for (int i = 0; i < points; i++)
                {
                    float u = (i + 0.5f) / points;
                    Vector2 place = lineAt(k, u);

                    if (place.X < f.stageMin.X || place.X > f.stageMax.X || place.Y < f.stageMin.Y || place.Y > f.stageMax.Y)
                        continue;

                    for (int b = 0; b < DanmakusuBalance.SUIKA_FISSURE_BULLETS * 2; b++)
                    {
                        int number = i * 16 + b;
                        Vector2 outward = b % 2 == 0 ? side : -side;
                        float angle = spread * (hash01(number, k, 81) - 0.5f);
                        float fast = speed * (DanmakusuBalance.ENDER_BURST_SLOWEST + (DanmakusuBalance.ENDER_BURST_FASTEST - DanmakusuBalance.ENDER_BURST_SLOWEST) * hash01(number, k, 82));
                        float size = radius * DanmakusuBalance.ENDER_BULLET_SIZE * (0.7f + 0.7f * hash01(number, k, 83));
                        Vector2 velocity = turned(outward, angle) * fast;
                        double t0 = t + hash01(number, k, 84) * beat * 0.25;

                        AddBullet(new StageBullet
                        {
                            T0 = t0,
                            P0 = place,
                            V = velocity,
                            Radius = size,
                            Hit = size * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = 0,
                            Shape = BulletShape.Ellipse,
                            Tint = BulletRenderer.TINT_FIRE,
                            TintAmount = 1f,
                            Bloom = DanmakusuBalance.ENDER_BULLET_BLOOM,
                            Untraced = true,
                            Until = t0 + f.stageLife(velocity),
                        });
                    }
                }
            }

            // ---- hurting ----

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float me = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // the stomp itself, and its thin cracks while they glow
                float thin = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES * DanmakusuBalance.BULLET_HITBOX_SCALE + me;

                for (int k = 0; k < stomps.Length && stomps[k] <= now; k++)
                {
                    if (!stompKnown[k] || now > stomps[k] + cracksMs)
                        continue;

                    if (now <= stomps[k] + DanmakusuBalance.SUIKA_STOMP_HURT_MS && Vector2.Distance(player, stompAt[k]) <= stompRadius(f) + me)
                        return true;

                    for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                    {
                        thinCrack(f, k, c);

                        for (int i = 0; i + 1 < crack.Count; i++)
                        {
                            if (distanceToSegment(player, crack[i], crack[i + 1]) <= thin)
                                return true;
                        }
                    }
                }

                // the big crack, as far as it has grown, until it explodes
                for (int k = 0; k < fissures.Length && fissures[k] <= now; k++)
                {
                    if (!fissureKnown[k] || now >= explodeAt(k))
                        continue;

                    float grown = grownTo(k, now);
                    Vector2 previous = lineAt(k, 0f);

                    for (int i = 1; i <= fissure_steps; i++)
                    {
                        float u = Math.Min(i / (float)fissure_steps, grown);
                        Vector2 next = lineAt(k, u);

                        if (distanceToSegment(player, previous, next) <= widthAt(f, k, u) * DanmakusuBalance.BULLET_HITBOX_SCALE + me)
                            return true;

                        previous = next;

                        if (u >= grown)
                            break;
                    }
                }

                // the tails of what she stomped
                foreach (var (index, until) in tails)
                {
                    var bullet = Bullets[index];

                    if (now < bullet.T0 || now >= until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    if (tail(f, bullet, now, until) && distanceToSegment(player, line[0], line[^1]) <= widths[0] * DanmakusuBalance.BULLET_HITBOX_SCALE * 0.6f + me)
                        return true;
                }

                return false;
            }

            /// <summary>The tail of a stomped bullet at <paramref name="t"/> (into <see cref="line"/>, from its head back, with the widths): two beats of the way it has come, narrowing to a point.</summary>
            private bool tail(BulletField f, in StageBullet bullet, double t, double until)
            {
                line.Clear();
                widths.Clear();

                double back = Math.Min(t - bullet.T0, beat * DanmakusuBalance.SUIKA_TAIL_BEATS);

                if (back <= 1)
                    return false;

                Vector2 head = bullet.P0 + bullet.V * (float)(t - bullet.T0);
                Vector2 end = bullet.P0 + bullet.V * (float)(t - back - bullet.T0);
                float half = bullet.Radius * DanmakusuBalance.SUIKA_TAIL_WIDTH;
                const int points = 5;

                for (int i = 0; i < points; i++)
                {
                    float u = i / (points - 1f);
                    line.Add(Vector2.Lerp(head, end, u));
                    widths.Add(half * (1f - 0.9f * u));
                }

                return true;
            }

            // ---- drawing ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || tails.Count == 0)
                    return;

                foreach (var (index, until) in tails)
                {
                    var bullet = Bullets[index];

                    if (t < bullet.T0 || t >= until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    if (tail(f, bullet, t, until))
                        f.addPlainBeam(line, widths[0], magma, magma_border, ref beams, ref pointTotal, widths);
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                // the stomps: a ring over the ground, and the thin cracks fading out
                for (int k = 0; k < stomps.Length && stomps[k] <= t; k++)
                {
                    if (!stompKnown[k] || t > stomps[k] + cracksMs)
                        continue;

                    double age = t - stomps[k];
                    float ring = smoothStep(age / 220.0);
                    float radius = stompRadius(f);

                    if (age < 400)
                        f.stageRipple.AddSoftRing(stompAt[k], radius * (0.3f + 0.7f * ring), Math.Max(f.circleRadius * 0.25f, 3f), Math.Max(f.circleRadius * 0.15f, 2f), 0.8f * (1f - smoothStep(age / 400.0)), new Color4(1f, 0.7f, 0.4f, 1f));

                    float glow = Math.Min(smoothStep(age / 60.0), 1f - smoothStep(age / cracksMs));
                    float half = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES;

                    for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                    {
                        thinCrack(f, k, c);
                        moltenBand(f, crack, _ => half, glow);
                    }
                }

                // the big cracks
                for (int k = 0; k < fissures.Length && fissures[k] <= t; k++)
                {
                    if (!fissureKnown[k] || t > explodeAt(k) + beat * 0.5)
                        continue;

                    float grown = grownTo(k, t);
                    float heat = t < explodeAt(k) ? 0.75f + 0.25f * smoothStep((t - grownAt(k)) / (explodeAt(k) - grownAt(k))) : 1f - smoothStep((t - explodeAt(k)) / (beat * 0.5));

                    crack.Clear();

                    for (int i = 0; i <= fissure_steps; i++)
                    {
                        float u = Math.Min(i / (float)fissure_steps, grown);
                        crack.Add(lineAt(k, u));

                        if (u >= grown)
                            break;
                    }

                    int fissure = k;
                    moltenBand(f, crack, i => widthAt(f, fissure, Math.Min(i / (float)fissure_steps, grown)), heat);

                    // (the explosion flashes along it)
                    double flash = (t - explodeAt(k)) / (beat * 0.5);

                    if (flash >= 0 && flash < 1)
                        f.stageFront.Add(StageTexture.Glow, lineAt(k, 0.5f), new Vector2(fissureLength[k] * 1.2f), 0f, 0.7f * (float)((1 - flash) * (1 - flash)), new Color4(1f, 0.6f, 0.25f, 1f), true);
                }

                // (what glows like magma has a soft orange light under it)
                for (int i = SkipGone(ref glowFrom, ref glowFromTime, t, 0); i < Bullets.Count; i++)
                {
                    var bullet = Bullets[i];

                    if (bullet.Hidden || t < bullet.T0 || t >= bullet.Until)
                        continue;

                    Vector2 at = stageBulletPosition(bullet, t);

                    if (at.X < f.stageMin.X - 40 || at.X > f.stageMax.X + 40 || at.Y < f.stageMin.Y - 40 || at.Y > f.stageMax.Y + 40)
                        continue;

                    f.stageOrbs.Add(StageTexture.Glow, at, new Vector2(bullet.Radius * DanmakusuBalance.ENDER_BULLET_GLOW), 0f, 0.5f, new Color4(1f, 0.45f, 0.12f, 1f), true);
                }
            }

            /// <summary>A line of molten rock through <paramref name="points"/> (half as wide as <paramref name="halfWidth"/> says at each point): white hot in its middle, orange round that, dying away softly, all <paramref name="heat"/> bright.</summary>
            private static void moltenBand(BulletField f, List<Vector2> points, Func<int, float> halfWidth, float heat)
            {
                if (points.Count < 2 || heat <= 0.01f)
                    return;

                var hot = new Color4(1f, 0.93f, 0.65f, heat);
                var orange = new Color4(1f, 0.5f, 0.1f, 0.9f * heat);
                var soft = new Color4(1f, 0.32f, 0.06f, 0.4f * heat);
                var none = new Color4(0.9f, 0.25f, 0.05f, 0f);

                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 a = points[i - 1], b = points[i];
                    Vector2 along = b - a;

                    if (along.LengthSquared < 1e-6f)
                        continue;

                    Vector2 side = Vector2.Normalize(new Vector2(-along.Y, along.X));
                    float w0 = halfWidth(i - 1), w1 = halfWidth(i);

                    void band(float from0, float from1, float to0, float to1, Color4 near, Color4 far)
                    {
                        f.stageMeshes.Add(new MeshQuad { A = a + side * from0, B = b + side * from1, C = a + side * to0, D = b + side * to1, Near = near, Far = far });
                        f.stageMeshes.Add(new MeshQuad { A = a - side * from0, B = b - side * from1, C = a - side * to0, D = b - side * to1, Near = near, Far = far });
                    }

                    band(0f, 0f, w0 * 0.3f, w1 * 0.3f, hot, orange);
                    band(w0 * 0.3f, w1 * 0.3f, w0, w1, orange, soft);
                    band(w0, w1, w0 * 1.6f, w1 * 1.6f, soft, none);
                }
            }
        }
    }
}
