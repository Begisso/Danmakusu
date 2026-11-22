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
        // Shou Toramaru: she stands in the middle of the screen. When her section begins a see-through preview of her cross shows for two beats (2026-11-21), then the cross of four beams grows out of her, over a
        // beat, until it reaches past every edge (it starts turned so that the player is between two of its arms): three plain bullets wide on each side, glowing, drawn as one piece, so its joint in the middle is
        // seamless (no big star over it since 2026-11-21). It turns one way and then the other, the way changing at every second start or end of one of her attacks (2026-11-21; it was at every one: clockwise in
        // her attacks, anticlockwise in her pauses), as fast as a spinner's beams times SHOU_CROSS_SPIN, flashing another colour before it turns, and hurts like a slider body; when her section is over it shrinks back. Every fourth
        // bullet fired in her section (anyone's, counted ring by ring) does not come out where it was fired: it comes out of the cross, the places spread evenly over its four arms and along them, and flies straight
        // across the arm, to one side or the other (half each), at the speed it had. Every four beats she shoots a ring of sixteen beams that wave from side to side as they fly out (as what Wakasagihime makes swim,
        // every second one the other way), each a beat and a half long, hurting like a slider body.
        // ======================================================================================================================================================

        private sealed class PagodaAct : StageAct
        {
            private const float reach = 4000f; // (px: past any screen)
            private const int wave_points = 14;

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly float spin; // (radians per ms)
            private readonly double[] rings; // (when every ring of waving beams is shot)
            private readonly Vector2[] ringFrom;
            private readonly bool[] ringKnown;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private long counted;
            private int shot;
            private bool aimed;
            private float aim;

            public PagodaAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                beat = Math.Max(period.BeatLength, 1);
                spin = Math.Max(period.AngularSpeed * DanmakusuBalance.SHOU_CROSS_SPIN, DanmakusuBalance.SHOU_CROSS_LEAST_DEGREES * MathF.PI / 180f / 1000f);
                Start = period.ShootStart;
                End = period.End + growMs;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.SHOU_RING_EVERY_BEATS)
                    times.Add(at);

                rings = times.ToArray();
                ringFrom = new Vector2[rings.Length];
                ringKnown = new bool[rings.Length];

                crossFrom = period.ShootStart + beat * DanmakusuBalance.SHOU_PREVIEW_BEATS;

                // (where the cross turns the other way: every second start or end of an attack after it has grown out; it begins turning clockwise if it begins in an attack, as it always did)
                var bounds = new List<double>();

                foreach (var (start, end) in attacks)
                {
                    foreach (double at in new[] { start, end })
                    {
                        if (at > crossFrom && at < period.End)
                            bounds.Add(at);
                    }
                }

                bounds.Sort();

                for (int i = 1; i < bounds.Count; i += 2)
                    turns.Add(bounds[i]);

                firstWay = attacks.Exists(a => crossFrom >= a.Start && crossFrom < a.End) ? 1f : -1f;
            }

            private readonly double crossFrom; // (when the cross grows out, after its preview)
            private readonly List<double> turns = new List<double>(); // (when it turns the other way)
            private readonly float firstWay; // (1 clockwise, -1 anticlockwise, until its first turn)

            private double previewMs => beat * DanmakusuBalance.SHOU_PREVIEW_BEATS;

            private double growMs => beat * DanmakusuBalance.SHOU_CROSS_GROW_BEATS;

            private float crossHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.SHOU_CROSS_WIDTH;

            private float waveHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.SHOU_WAVE_WIDTH;

            /// <summary>She stays until the cross has shrunk back and her last waving beams are gone.</summary>
            public override double MechanicsEnd => End;

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                shot = 0;
                ClearBullets();

                if (time < Start)
                    aimed = false;

                for (int k = 0; k < rings.Length; k++)
                {
                    if (rings[k] > time)
                        ringKnown[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;

                if (!aimed)
                {
                    // (the player is between two arms when it begins)
                    Vector2 toPlayer = f.characterPosition - middle;
                    aim = (toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : 0f) + MathF.PI / 4f;
                    aimed = true;
                }

                for (int k = 0; k < rings.Length && rings[k] <= now; k++)
                {
                    if (ringKnown[k])
                        continue;

                    ringKnown[k] = true;
                    ringFrom[k] = f.bossVisualOf(Period);
                }
            }

            // ---- the cross ----

            /// <summary>How far the cross has turned at <paramref name="t"/> (radians): one way until its first turn, the other way until the next, and so on (from when it grows out).</summary>
            private float angleAt(double t)
            {
                double from = crossFrom;
                float way = firstWay;
                double turned = 0;

                foreach (double turn in turns)
                {
                    if (turn >= t)
                        break;

                    turned += way * (turn - from);
                    from = turn;
                    way = -way;
                }

                turned += way * Math.Max(t - from, 0);
                return aim + spin * (float)turned;
            }

            /// <summary>How long the arms are at <paramref name="t"/>: growing out after the preview, shrinking back after her section.</summary>
            private float lengthAt(double t)
            {
                float grown = smoothStep((t - crossFrom) / growMs);
                float shrunk = 1f - smoothStep((t - Period.End) / growMs);
                return reach * Math.Min(grown, shrunk);
            }

            /// <summary>
            /// How much the cross shows its other colour at <paramref name="t"/> (0..1): over the last <see cref="DanmakusuBalance.SHOU_FLASH_BEATS"/> before it turns the other way, on and off
            /// <see cref="DanmakusuBalance.SHOU_FLASHES"/> times.
            /// </summary>
            private float switchFlash(double t)
            {
                double span = beat * DanmakusuBalance.SHOU_FLASH_BEATS;

                foreach (double turn in turns)
                {
                    if (t >= turn || t < turn - span)
                        continue;

                    double u = 1 - (turn - t) / span;
                    return 0.5f - 0.5f * MathF.Cos((float)(u * DanmakusuBalance.SHOU_FLASHES * MathF.Tau));
                }

                return 0f;
            }

            private static Color4 lerpColour(Color4 a, Color4 b, float u) => new Color4(a.R + (b.R - a.R) * u, a.G + (b.G - a.G) * u, a.B + (b.B - a.B) * u, a.A + (b.A - a.A) * u);

            private static Vector2 armWay(float angle, int arm)
            {
                float a = angle + arm * MathF.PI / 2f;
                return new Vector2(MathF.Cos(a), MathF.Sin(a));
            }

            /// <summary>Every fourth bullet fired in her section comes out of the cross instead.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || !aimed || t < Period.ShootStart || t > Period.End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.SHOU_FROM_CROSS_EVERY != 0)
                    return false;

                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                float angle = angleAt(t);
                int number = shot++;
                int arm = number % 4;
                Vector2 way = armWay(angle, arm);
                Vector2 across = new Vector2(-way.Y, way.X);

                // (evenly along the arm, as far as it reaches into the screen, from outside the big star in its middle: a golden-ratio sequence for each arm)
                float least = f.circleRadius * DanmakusuBalance.SHOU_CROSS_NEAREST_CIRCLES;
                float most = Math.Min((float)f.toWall(middle, way, out _), lengthAt(t));

                if (most <= least)
                    return false;

                float slot = (number / 4 * 0.618034f) % 1f;
                float side = hash01(number, 7, 113) < 0.5f ? 1f : -1f;
                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 velocity = across * (side * stored.Speed);
                Vector2 start = middle + way * (least + (most - least) * slot) + across * (side * (crossHalf(f) + stored.Radius * big));

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = start,
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
                    Until = t + f.stageLife(velocity),
                });

                return true;
            }

            // ---- the waving beams ----

            private float waveSpeed => stageBulletSpeed(Period);

            /// <summary>Where the head of beam <paramref name="beam"/> of ring <paramref name="ring"/> is <paramref name="age"/> ms after it was shot.</summary>
            private Vector2 waveHead(BulletField f, int ring, int beam, double age)
            {
                float phase = ring * 2.3999632f;
                float angle = phase + beam * MathF.Tau / DanmakusuBalance.SHOU_RING_BEAMS;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 across = new Vector2(-way.Y, way.X);
                float a = (float)Math.Max(age, 0);
                float swing = f.circleRadius * DanmakusuBalance.SHOU_WAVE_CIRCLES * MathF.Sin(wavePhase(f, a)) * smoothStep(a / (beat * 0.5));

                return ringFrom[ring] + way * (waveSpeed * a) + across * (swing * (beam % 2 == 0 ? 1f : -1f));
            }

            /// <summary>
            /// How far through its waving a beam's head is <paramref name="age"/> ms after it was shot (radians): once in <see cref="DanmakusuBalance.SHOU_WAVE_BEATS"/> near her, ever faster the further out it is,
            /// up to <see cref="DanmakusuBalance.SHOU_WAVE_FASTEST"/> times as often half a screen's diagonal out (2026-11-14) - the waving speeds up evenly with the distance, so its phase is what that adds up to.
            /// </summary>
            private float wavePhase(BulletField f, float age)
            {
                float far = Vector2.Distance(f.stageMin, f.stageMax) * 0.5f / Math.Max(waveSpeed, 1e-3f); // (ms to get half a diagonal out)
                float gain = DanmakusuBalance.SHOU_WAVE_FASTEST - 1f;
                float counted = age <= far ? age + gain * age * age / (2f * far) : far + gain * far / 2f + DanmakusuBalance.SHOU_WAVE_FASTEST * (age - far);
                return counted / (float)(beat * DanmakusuBalance.SHOU_WAVE_BEATS) * MathF.Tau;
            }

            private double waveLife(BulletField f)
            {
                float diagonal = Vector2.Distance(f.stageMin, f.stageMax);
                return (diagonal + waveSpeed * beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS) / Math.Max(waveSpeed, 1e-3f) + beat;
            }

            /// <summary>The points of a waving beam at <paramref name="t"/> (into <see cref="line"/>, with the widths): false when it is not there.</summary>
            private bool wave(BulletField f, int ring, int beam, double t, float half)
            {
                double age = t - rings[ring];
                double tail = beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS;

                line.Clear();
                widths.Clear();

                if (age <= 0)
                    return false;

                for (int i = 0; i < wave_points; i++)
                {
                    double back = tail * i / (wave_points - 1);

                    if (age - back < 0 && i > 0)
                        break;

                    line.Add(waveHead(f, ring, beam, age - back));
                    widths.Add(half * (1f - 0.7f * i / (wave_points - 1))); // (it narrows towards its end)
                }

                return line.Count >= 2;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || !aimed || f.palette.Length == 0 || t < Start || t > End + waveLife(f))
                    return;

                // the preview of the cross: see-through, harmless, thinner, where it will grow out (2026-11-21)
                if (t < crossFrom)
                {
                    int previewStyle = f.comboStyle(1, true, Period.Rank);
                    Vector2 centre = (f.stageMin + f.stageMax) * 0.5f;
                    float seen = DanmakusuBalance.SHOU_PREVIEW_ALPHA * smoothStep((t - Start) / (beat * 0.25));
                    int ghost = ++f.beamPieces;

                    for (int arm = 0; arm < 2; arm++)
                    {
                        Vector2 way = armWay(aim, arm);
                        int from = pointTotal;

                        line.Clear();
                        line.Add(centre - way * reach);
                        line.Add(centre);
                        line.Add(centre + way * reach);
                        f.addPlainBeam(line, crossHalf(f) * 0.6f, f.palette[previewStyle % f.palette.Length], f.borderPalette[previewStyle % f.borderPalette.Length], ref beams, ref pointTotal);
                        f.laserInstances[beams - 1].Piece = ghost;
                        Array.Fill(f.laserFadePointBuffer, seen, from, pointTotal - from);
                    }
                }

                // the cross: two beams through the middle, drawn as one piece (no seams where they cross) and glowing
                float length = lengthAt(t);

                if (length > 1f)
                {
                    int style = f.comboStyle(1, true, Period.Rank);
                    Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                    float angle = angleAt(t);
                    int piece = ++f.beamPieces;

                    // (over the beat before it turns the other way it flashes another colour, on and off: 2026-11-14)
                    Color4 body = f.palette[style % f.palette.Length], edge = f.borderPalette[style % f.borderPalette.Length];
                    float flash = switchFlash(t);

                    if (flash > 0f)
                    {
                        int other = f.comboStyle(3, true, Period.Rank);
                        body = lerpColour(body, f.palette[other % f.palette.Length], flash);
                        edge = lerpColour(edge, f.borderPalette[other % f.borderPalette.Length], flash);
                    }

                    for (int arm = 0; arm < 2; arm++)
                    {
                        Vector2 way = armWay(angle, arm);

                        line.Clear();
                        line.Add(middle - way * length);
                        line.Add(middle);
                        line.Add(middle + way * length);
                        f.addPlainBeam(line, crossHalf(f), body, edge, ref beams, ref pointTotal);
                        f.laserInstances[beams - 1].Piece = piece;
                        f.laserInstances[beams - 1].Bloom = DanmakusuBalance.SHOU_CROSS_BLOOM * (1f + flash);
                    }
                }

                // the waving beams
                int waveStyle = f.comboStyle(2, true, Period.Rank);
                double life = waveLife(f);
                float half = waveHalf(f);

                for (int ring = 0; ring < rings.Length; ring++)
                {
                    if (!ringKnown[ring] || t < rings[ring] || t > rings[ring] + life)
                        continue;

                    for (int beam = 0; beam < DanmakusuBalance.SHOU_RING_BEAMS; beam++)
                    {
                        if (wave(f, ring, beam, t, half))
                            f.addPlainBeam(line, half, f.palette[waveStyle % f.palette.Length], f.borderPalette[waveStyle % f.borderPalette.Length], ref beams, ref pointTotal, widths);
                    }
                }
            }

            // (no big star over the middle of the cross since 2026-11-21, "remove marisa star above": the cross is one piece, its joint seamless)

            /// <summary>The cross and the waving beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || !aimed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float length = lengthAt(now);

                if (length > f.circleRadius)
                {
                    Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                    float angle = angleAt(now);
                    float hit = crossHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    for (int arm = 0; arm < 4; arm++)
                    {
                        if (distanceToSegment(player, middle, middle + armWay(angle, arm) * length) <= hit)
                            return true;
                    }
                }

                double life = waveLife(f);
                float half = waveHalf(f);

                // (a quick look first: a ring's beams all lie in a band round where it was shot from, from how far their tails have got to how far their heads have, and each within its swing of its own
                // straight line; only a beam the player is near is worked out. The autoplay asks this many times a frame)
                float room = f.circleRadius * DanmakusuBalance.SHOU_WAVE_CIRCLES + half * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + 1f;
                double tail = beat * DanmakusuBalance.SHOU_WAVE_TAIL_BEATS;

                for (int ring = 0; ring < rings.Length; ring++)
                {
                    if (!ringKnown[ring] || now < rings[ring] || now > rings[ring] + life)
                        continue;

                    double age = now - rings[ring];
                    Vector2 off = player - ringFrom[ring];
                    float distance = off.Length;

                    if (distance > waveSpeed * (float)age + room || distance < waveSpeed * (float)(age - tail) - room)
                        continue;

                    float phase = ring * 2.3999632f;

                    for (int beam = 0; beam < DanmakusuBalance.SHOU_RING_BEAMS; beam++)
                    {
                        float angle = phase + beam * MathF.Tau / DanmakusuBalance.SHOU_RING_BEAMS;
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        if (Vector2.Dot(off, way) < -room || MathF.Abs(off.X * way.Y - off.Y * way.X) > room)
                            continue;

                        if (!wave(f, ring, beam, now, half))
                            continue;

                        for (int i = 0; i + 1 < line.Count; i++)
                        {
                            if (distanceToSegment(player, line[i], line[i + 1]) <= widths[i] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
