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
        // ellipses half its size, fanned out over 90 degrees round the way it would bounce. The stomp hurts the player if they are in it, and thick cracks of magma in the ground, meeting under her, come up at once
        // and fade out over two beats, hurting like a slider body meanwhile.
        // Every sixteen beats a wide crack of glowing magma, two circles across and as jagged as the World's Ender's cut, splits the ground from where she stands towards the player, out past the edge of the screen,
        // in a beat (Hecatia's cut sounds). It hurts like a slider body. Eight beats after it has grown it explodes (Hecatia's explosion): glowing magma ellipses fly off both sides all along it, as the World's
        // Ender's bullets do, and it cools away.
        // Every crack is drawn as molten rock (the user's picture): a glow round it, a dark crust along its edges, orange inside going yellow and a white-hot vein; all the cracks of a frame are drawn layer by
        // layer together, so they run into each other without a seam. Every sound of hers plays twice at once.
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
                        sound(f, "hecatia_earth_hit");
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
                            sound(f, "hecatia_cut");
                    }

                    if (!exploded[k] && now >= explodeAt(k))
                    {
                        exploded[k] = true;
                        explode(f, k);

                        if (now - explodeAt(k) < 250 && explodeAt(k) > resetTime)
                            sound(f, "hecatia_explosion");
                    }
                }
            }

            /// <summary>Every sound of hers plays twice at once (2026-11-14: "all her sounds play double times at the same time").</summary>
            private static void sound(BulletField f, string name)
            {
                f.playStageSound(name);
                f.playStageSound(name);
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

            /// <summary>
            /// Crack <paramref name="c"/> of stomp <paramref name="k"/> (into <see cref="crack"/>, its half width at every point into <see cref="crackWidths"/>): from right under her out to the edge of the stomp,
            /// jagged, unevenly wide and narrowing to its end (all of them meet in one pool of magma under her).
            /// </summary>
            private void thinCrack(BulletField f, int k, int c)
            {
                crack.Clear();
                crackWidths.Clear();

                float reach = stompRadius(f);
                float half = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES;
                float angle = (c + 0.6f * (hash01(k, c, 71) - 0.5f)) * MathF.Tau / DanmakusuBalance.SUIKA_CRACKS + k * 0.9f;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 side = new Vector2(-way.Y, way.X);
                float length = reach * (0.75f + 0.25f * hash01(k, c, 72));

                for (int i = 0; i < crack_points; i++)
                {
                    float u = i / (crack_points - 1f);
                    float jag = i == 0 ? 0f : (hash01(k * 31 + c, i, 73) - 0.5f) * reach * 0.22f;
                    crack.Add(stompAt[k] + way * (length * u) + side * jag);
                    crackWidths.Add(half * (i == 0 ? 1f : 0.75f + 0.5f * hash01(k * 31 + c, i, 74)) * (1f - 0.8f * u * u));
                }
            }

            private readonly List<float> crackWidths = new List<float>();

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

                // the stomp itself, and its cracks while they glow
                float thinMost = f.circleRadius * DanmakusuBalance.SUIKA_THIN_CRACK_CIRCLES * 1.25f + me;

                for (int k = 0; k < stomps.Length && stomps[k] <= now; k++)
                {
                    if (!stompKnown[k] || now > stomps[k] + cracksMs)
                        continue;

                    if (now <= stomps[k] + DanmakusuBalance.SUIKA_STOMP_HURT_MS && Vector2.Distance(player, stompAt[k]) <= stompRadius(f) + me)
                        return true;

                    // (far from all of them: none)
                    if (Vector2.Distance(player, stompAt[k]) > stompRadius(f) * 1.2f + thinMost)
                        continue;

                    for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                    {
                        thinCrack(f, k, c);

                        for (int i = 0; i + 1 < crack.Count; i++)
                        {
                            if (distanceToSegment(player, crack[i], crack[i + 1]) <= Math.Max(crackWidths[i], crackWidths[i + 1]) * DanmakusuBalance.BULLET_HITBOX_SCALE + me)
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

                    for (int c = 0; c < DanmakusuBalance.SUIKA_CRACKS; c++)
                    {
                        thinCrack(f, k, c);
                        addBand(crack, crackWidths, glow, k * 977 + c * 31);
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
                    crackWidths.Clear();

                    for (int i = 0; i <= fissure_steps; i++)
                    {
                        float u = Math.Min(i / (float)fissure_steps, grown);
                        crack.Add(lineAt(k, u));
                        crackWidths.Add(widthAt(f, k, u));

                        if (u >= grown)
                            break;
                    }

                    addBand(crack, crackWidths, heat, 50000 + k * 131);

                    // (the explosion flashes along it)
                    double flash = (t - explodeAt(k)) / (beat * 0.5);

                    if (flash >= 0 && flash < 1)
                        f.stageFront.Add(StageTexture.Glow, lineAt(k, 0.5f), new Vector2(fissureLength[k] * 1.2f), 0f, 0.7f * (float)((1 - flash) * (1 - flash)), new Color4(1f, 0.6f, 0.25f, 1f), true);
                }

                drawBands(f);

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

            // ---- the magma (2026-11-14: thick, as the user's picture of a crack in cooling lava; one seamless piece) ----

            // (the cracks of this frame, all drawn at once layer by layer: every point with its side (mitred, so the pieces of a crack meet without a seam or a gap), its half width and its vein's width)
            private readonly List<Vector2> bandPoints = new List<Vector2>();
            private readonly List<Vector2> bandSides = new List<Vector2>();
            private readonly List<float> bandWidths = new List<float>();
            private readonly List<float> bandVeins = new List<float>();
            private readonly List<(int Start, int Count, float Heat)> bandRuns = new List<(int, int, float)>();

            /// <summary>Keeps a crack through <paramref name="points"/> (half as wide as <paramref name="halfWidths"/> says at each point), <paramref name="heat"/> bright, for <see cref="drawBands"/>.</summary>
            private void addBand(List<Vector2> points, List<float> halfWidths, float heat, int seed)
            {
                if (points.Count < 2 || heat <= 0.01f)
                    return;

                int start = bandPoints.Count;

                for (int i = 0; i < points.Count; i++)
                {
                    // (the side at a corner is between the sides of the two pieces, longer so that the band keeps its width: at most twice as long at a sharp corner)
                    Vector2 before = i > 0 ? points[i] - points[i - 1] : points[1] - points[0];
                    Vector2 after = i + 1 < points.Count ? points[i + 1] - points[i] : before;
                    Vector2 sideBefore = before.LengthSquared > 1e-8f ? Vector2.Normalize(new Vector2(-before.Y, before.X)) : Vector2.UnitY;
                    Vector2 sideAfter = after.LengthSquared > 1e-8f ? Vector2.Normalize(new Vector2(-after.Y, after.X)) : sideBefore;
                    Vector2 side = sideBefore + sideAfter;
                    side = side.LengthSquared > 1e-6f ? Vector2.Normalize(side) : sideAfter;
                    float stretch = Math.Min(1f / Math.Max(Vector2.Dot(side, sideAfter), 0.5f), 2f);

                    bandPoints.Add(points[i]);
                    bandSides.Add(side * stretch);
                    bandWidths.Add(halfWidths[i]);
                    bandVeins.Add(0.08f + 0.16f * hash01(seed, i, 91)); // (the white-hot vein in the middle, unevenly wide)
                }

                bandRuns.Add((start, points.Count, heat));
            }

            /// <summary>
            /// Every crack kept this frame, as molten rock: a soft orange glow round it, a dark crust along its edges, orange inside going yellow, and a white-hot vein down the middle. Each layer of every crack is
            /// drawn before the next layer of any (the glows, then the crusts, then the insides), so where cracks meet or cross their insides run together and no edge lies across them.
            /// </summary>
            private void drawBands(BulletField f)
            {
                if (bandRuns.Count == 0)
                    return;

                // (from the middle out, as shares of the half width: where each layer begins and ends, and its colour at its inner and outer edge)
                for (int layer = 0; layer < 5; layer++)
                {
                    foreach (var (start, count, heat) in bandRuns)
                    {
                        Color4 near, far;
                        float from, to;
                        bool vein = false;

                        switch (layer)
                        {
                            case 0: // (the glow over the ground round it)
                                from = 1f;
                                to = 1.75f;
                                near = new Color4(1f, 0.36f, 0.06f, 0.42f * heat);
                                far = new Color4(0.9f, 0.22f, 0.04f, 0f);
                                break;

                            case 1: // (the dark crust)
                                from = 0.74f;
                                to = 1f;
                                near = new Color4(0.42f, 0.09f, 0.03f, heat);
                                far = new Color4(0.12f, 0.035f, 0.02f, heat);
                                break;

                            case 2: // (deep orange under the crust)
                                from = 0.42f;
                                to = 0.74f;
                                near = new Color4(1f, 0.52f, 0.08f, heat);
                                far = new Color4(0.85f, 0.2f, 0.03f, heat);
                                break;

                            case 3: // (yellow towards the middle)
                                from = 0f;
                                to = 0.42f;
                                near = new Color4(1f, 0.86f, 0.36f, heat);
                                far = new Color4(1f, 0.52f, 0.08f, heat);
                                break;

                            default: // (the white-hot vein)
                                from = 0f;
                                to = 1f;
                                vein = true;
                                near = new Color4(1f, 1f, 0.86f, heat);
                                far = new Color4(1f, 0.86f, 0.36f, heat);
                                break;
                        }

                        for (int i = start + 1; i < start + count; i++)
                        {
                            Vector2 a = bandPoints[i - 1], b = bandPoints[i];
                            Vector2 sa = bandSides[i - 1] * (vein ? bandWidths[i - 1] * bandVeins[i - 1] : bandWidths[i - 1]);
                            Vector2 sb = bandSides[i] * (vein ? bandWidths[i] * bandVeins[i] : bandWidths[i]);

                            f.stageMeshes.Add(new MeshQuad { A = a + sa * from, B = b + sb * from, C = a + sa * to, D = b + sb * to, Near = near, Far = far });
                            f.stageMeshes.Add(new MeshQuad { A = a - sa * from, B = b - sb * from, C = a - sa * to, D = b - sb * to, Near = near, Far = far });
                        }
                    }
                }

                bandPoints.Clear();
                bandSides.Clear();
                bandWidths.Clear();
                bandVeins.Clear();
                bandRuns.Clear();
            }
        }
    }
}
