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
        // Watari Nina (2026-11-22): every NINA_EVERY_BEATS of her section a golden beam as Matenshi's, twice as thick, along the line from her to the player as it was when its preview came, across the
        // whole screen: a see-through preview for NINA_PREVIEW_BEATS, then it widens from its middle line over NINA_GROW_BEATS (with Byakuren's beam sound) and hurts like a slider body for
        // NINA_LIVE_BEATS, fading out over NINA_FADE_BEATS (harmless by then); the next preview comes as it fades.
        // While a beam hurts, every bullet that leaves the screen (anyone's, but her own, Yukari's train and what follows a boss) comes back in where it left, flying straight at the beam (square to it) at
        // the speed it had - once (2026-11-22; it came out of the beam, flying away from it). And what leaves the screen over the last NINA_RECORD_BEATS before a beam turns solid (its preview) is kept where it
        // left, unseen, and all of it comes back in from there at once as the beam turns solid, flying at it (2026-11-22); over the last NINA_WARN_BEATS before, each flashes gold where it will come in.
        // Her own shots are the planner's: fans of three big balls at the player.
        // ======================================================================================================================================================

        private sealed class MirageAct : StageAct
        {
            private readonly double beat;

            // (her beams: when each preview comes, and from where and which way, taken then)
            private readonly double[] strikes;
            private readonly Vector2[] from;
            private readonly Vector2[] way;
            private readonly bool[] known;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);
            private int soundedStrike = -1;
            private double resetTime = double.NegativeInfinity;

            public MirageAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.NINA_EVERY_BEATS;

                // (a beam whose preview is over in her section: it may go on hurting after it, as her mechanic does)
                for (double t = period.ShootStart; t + beat * DanmakusuBalance.NINA_PREVIEW_BEATS < period.End && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                from = new Vector2[strikes.Length];
                way = new Vector2[strikes.Length];
                known = new bool[strikes.Length];

                Start = period.ShootStart;
                End = strikes.Length > 0 ? goneAt(strikes.Length - 1) : period.End;

                if (strikes.Length > 0)
                {
                    AbsorbFrom = recordFrom(0);
                    AbsorbTo = liveEnd(strikes.Length - 1);
                }
            }

            /// <summary>From when what leaves the screen is kept for beam <paramref name="k"/>, to come back as it turns solid: NINA_RECORD_BEATS before, not before its preview (where it will be is known from then).</summary>
            private double recordFrom(int k) => Math.Max(strikes[k], solidAt(k) - beat * DanmakusuBalance.NINA_RECORD_BEATS);

            /// <summary>The beam that what leaves the screen at <paramref name="t"/> is kept for, or -1.</summary>
            private int recordingFor(double t)
            {
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && t >= recordFrom(k) && t < solidAt(k))
                        return k;
                }

                return -1;
            }

            /// <summary>She stays until her last beam has faded.</summary>
            public override double MechanicsEnd => strikes.Length > 0 ? goneAt(strikes.Length - 1) : double.NegativeInfinity;

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.NINA_PREVIEW_BEATS;

            private double liveEnd(int k) => solidAt(k) + beat * DanmakusuBalance.NINA_LIVE_BEATS;

            private double goneAt(int k) => liveEnd(k) + beat * DanmakusuBalance.NINA_FADE_BEATS;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.MATENSHI_BEAM_WIDTH_CIRCLES * 0.5f * DanmakusuBalance.NINA_BEAM_THICKNESS;

            /// <summary>How wide a solid beam is at <paramref name="t"/> (half width): it widens from its middle line.</summary>
            private float widthAt(BulletField f, int k, double t) => halfWidth(f) * smoothStep((t - solidAt(k)) / (beat * DanmakusuBalance.NINA_GROW_BEATS));

            /// <summary>The beams that can be there at <paramref name="t"/>: from the one whose time is not over yet to the last whose preview has come.</summary>
            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.NINA_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.NINA_PREVIEW_BEATS + DanmakusuBalance.NINA_LIVE_BEATS + DanmakusuBalance.NINA_FADE_BEATS)) / every));
                return (first, last);
            }

            /// <summary>The beam that hurts at <paramref name="t"/>, or -1.</summary>
            private int solidOne(double t)
            {
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && t >= solidAt(k) && t < liveEnd(k))
                        return k;
                }

                return -1;
            }

            public override bool Absorbs(double t) => solidOne(t) >= 0 || recordingFor(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                var (first, _) = around(from);
                var (_, last) = around(to);

                for (int k = first; k <= last; k++)
                {
                    if (recordFrom(k) <= to && liveEnd(k) >= from)
                        return true;
                }

                return false;
            }

            /// <summary>Beam <paramref name="k"/> as a stretch of its line: where the line through her goes in and out of the screen, <paramref name="pad"/> px further out.</summary>
            private (Vector2 A, Vector2 B) segment(BulletField f, int k, float pad)
            {
                Vector2 p = from[k], d = way[k];
                Vector2 low = f.stageMin - new Vector2(pad), high = f.stageMax + new Vector2(pad);
                float sMin = float.NegativeInfinity, sMax = float.PositiveInfinity;

                for (int axis = 0; axis < 2; axis++)
                {
                    float pa = axis == 0 ? p.X : p.Y, da = axis == 0 ? d.X : d.Y, lo = axis == 0 ? low.X : low.Y, hi = axis == 0 ? high.X : high.Y;

                    if (MathF.Abs(da) < 1e-6f)
                        continue;

                    float s1 = (lo - pa) / da, s2 = (hi - pa) / da;
                    sMin = Math.Max(sMin, Math.Min(s1, s2));
                    sMax = Math.Min(sMax, Math.Max(s1, s2));
                }

                if (float.IsInfinity(sMin) || float.IsInfinity(sMax) || sMax < sMin)
                    return (p, p);

                return (p + d * sMin, p + d * sMax);
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                resetTime = time;
                soundedStrike = -1;

                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;
                    else if (solidAt(k) <= time)
                        soundedStrike = k;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every beam whose preview has come takes where she is and where the player is, once)
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] || strikes[k] > now)
                        continue;

                    known[k] = true;
                    from[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from[k];
                    way[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                }

                // the beam comes with Byakuren's beam sound as it turns solid
                for (; soundedStrike + 1 < strikes.Length && solidAt(soundedStrike + 1) <= now; soundedStrike++)
                {
                    double solid = solidAt(soundedStrike + 1);

                    if (now - solid < 250 && solid > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var core = new Color4(1f, 0.93f, 0.6f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.95f, 0.68f, 0.12f, 1f));
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= goneAt(k))
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? 1f - smoothStep((t - liveEnd(k)) / (beat * DanmakusuBalance.NINA_FADE_BEATS))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));
                    float half = solid ? Math.Max(widthAt(f, k, t), 1f) : halfWidth(f);

                    if (alpha < 0.01f)
                        continue;

                    var (a, b) = segment(f, k, half);
                    line.Clear();
                    line.Add(a);
                    line.Add(b);
                    fades.Clear();
                    fades.Add(alpha);
                    fades.Add(alpha);
                    f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, fades);
                }
            }

            /// <summary>
            /// The bullets kept at the edge of the screen flash gold where they will come back in, over the last NINA_WARN_BEATS before they do - twice, brighter the second time (2026-11-22, "warn about bullets
            /// that about to come out of edge, golden flash").
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || Bullets.Count == 0 || t < Start)
                    return;

                double warn = beat * DanmakusuBalance.NINA_WARN_BEATS;
                var gold = new Color4(1f, 0.82f, 0.25f, 1f);
                var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bullets);

                for (int i = Math.Max(DrawFrom, 0); i < all.Length; i++)
                {
                    ref readonly var kept = ref all[i];

                    if (kept.T0 <= t || kept.T0 - t > warn)
                        continue;

                    float u = (float)(1.0 - (kept.T0 - t) / warn);
                    float pulse = MathF.Sin(MathF.PI * 2f * u);
                    float flash = pulse * pulse * (0.5f + 0.5f * u);

                    f.stageTop.Add(StageTexture.Glow, kept.P0, new Vector2(kept.Radius * DanmakusuBalance.NINA_WARN_SIZE), 0f, flash * DanmakusuBalance.NINA_WARN_ALPHA, gold, true);
                }
            }

            /// <summary>A solid beam hurts like a slider body, as wide as it has grown (not while it fades).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || strikes.Length == 0 || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || now < solidAt(k) || now >= liveEnd(k))
                        continue;

                    float reach = widthAt(f, k, now) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                    var (a, b) = segment(f, k, reach);

                    if (distanceToSegment(player, a, b) <= reach)
                        return true;
                }

                return false;
            }

            /// <summary>
            /// While a beam hurts, a bullet of the circles and sliders that leaves the screen comes back in where it left, flying across the screen at the beam, straight towards it (2026-11-22, the same day:
            /// it came out of the beam's side, flying away from it; anyone's).
            /// </summary>
            public override bool Takes(BulletField f, double t, Vector2 from_, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.FollowsBoss || emitter.OwnShot)
                    return false;

                // (while a beam hurts it comes back at once; over the last beats before one turns solid it is kept where it left, and comes back as the beam turns solid - 2026-11-22)
                int k = solidOne(t);
                double comes = t;

                if (k < 0)
                {
                    k = recordingFor(t);

                    if (k < 0)
                        return false;

                    comes = solidAt(k);
                }

                Vector2 low = f.stageMin, high = f.stageMax;
                bool wasIn = from_.X >= low.X && from_.X <= high.X && from_.Y >= low.Y && from_.Y <= high.Y;
                bool isIn = to.X >= low.X && to.X <= high.X && to.Y >= low.Y && to.Y <= high.Y;

                if (!wasIn || isIn)
                    return false;

                // (where it crossed the edge of the screen, and the point of the beam nearest to it, inside the screen)
                Vector2 crossed = Vector2.Clamp(to, low, high);
                var (a, b) = segment(f, k, 0f);
                Vector2 ab = b - a;
                float u = ab.LengthSquared > 1e-6f ? Math.Clamp(Vector2.Dot(crossed - a, ab) / ab.LengthSquared, 0f, 1f) : 0f;
                Vector2 axis = a + ab * u;

                Vector2 across = new Vector2(-way[k].Y, way[k].X);

                if (Vector2.Dot(crossed - axis, across) < 0f)
                    across = -across;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from_, to, ScanPrevious, t);
                Vector2 velocity = -across * speed;

                // (back in at the edge where it left, flying across the beam's line, at it - at once, or as the beam turns solid: it waits there unseen until then)
                var back = f.makeStageBullet(stored, emitter, bullet, comes, crossed, velocity);
                back.Untraced = true; // (it flies straight at the beam: no other boss's op turns it)

                int before = Bullets.Count;
                AddBullet(back);
                return Bullets.Count > before;
            }
        }
    }
}
