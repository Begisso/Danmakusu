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
        // Aya Shameimaru (2026-11-22): no pauses. When she starts shooting, and again the moment she gets to the edge of the screen, she chooses a straight line out from where she is to the edge of the screen:
        // of AYA_DIRECTIONS ways round, the one along which the most bullets are (as they were drawn in the last frame; the longest of those that are as good). A see-through beam shows it from then until
        // she goes, at the next AYA_DASH_EVERY_BEATS of a beat at least AYA_PREVIEW_LEAST_BEATS later; then she flashes along it at AYA_DASH_SPEED times Mokou's jump speed, and behind her a beam as thick as a slider's stays for
        // AYA_TRACE_BEATS (it hurts like a slider body; it fades over its last AYA_TRACE_FADE_BEATS, harmless by then). Every bullet of a ring she passes (AYA_SLASH_CIRCLES circle radii to either side of her
        // way; not her own, not Yukari's train) comes apart into two of Matenshi's feathers, black as a crow's, which fly straight out to either side of her way, AYA_SLASH_SPEED times as fast as it was
        // (2026-11-22; it flew out whole to its own side). The feathers turn round as they fly, the two of a bullet opposite ways (AYA_FEATHER_SPIN, 2026-11-25). Each swing is heard (Kagerou's).
        // Her own shots are the planner's: fans of orange maple leaves that pull towards the player, from wherever she is.
        // The ways she chose are kept: time going back takes the same ways again (the later ones are chosen when their time comes).
        // ======================================================================================================================================================

        private sealed class WindAct : StageAct
        {
            private sealed class Dash
            {
                public double Chosen; // (when the way was chosen: the preview shows from then)
                public double Start;
                public double Arrive;
                public Vector2 From;
                public Vector2 To;
                public Vector2 Way;
            }

            private readonly double beat;
            private readonly List<Dash> dashes = new List<Dash>();
            private bool done;
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);
            private int sounded = -1;
            private double resetTime = double.NegativeInfinity;

            public WindAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * (DanmakusuBalance.AYA_TRACE_BEATS + 1);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End + beat * 2;
            }

            /// <summary>She stays until her last dash is over.</summary>
            public override double MechanicsEnd => dashes.Count > 0 ? Math.Max(Period.End, dashes[^1].Arrive) : double.NegativeInfinity;

            private float speed => Math.Clamp(stageBulletSpeed(Period) * DanmakusuBalance.MOKOU_JUMP_SPEED_FACTOR, DanmakusuBalance.MOKOU_JUMP_MIN_SPEED, DanmakusuBalance.MOKOU_JUMP_MAX_SPEED) * DanmakusuBalance.AYA_DASH_SPEED;

            private double traceLife => beat * DanmakusuBalance.AYA_TRACE_BEATS;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.LASER_SIZE_SCALE;

            /// <summary>Where she is in dash <paramref name="d"/> at <paramref name="t"/>.</summary>
            private Vector2 placeIn(Dash d, double t)
                => t <= d.Start ? d.From : t >= d.Arrive ? d.To : d.From + d.Way * (speed * (float)(t - d.Start));

            /// <summary>The last dash whose way has been chosen by <paramref name="t"/>, or -1.</summary>
            private int dashAt(double t)
            {
                for (int k = dashes.Count - 1; k >= 0; k--)
                {
                    if (dashes[k].Chosen <= t)
                        return k;
                }

                return -1;
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                int k = dashAt(now);
                position = k >= 0 ? placeIn(dashes[k], now) : default;
                return k >= 0 && now <= Math.Max(Period.End, dashes[k].Arrive);
            }

            public override void Reset(BulletField f, double time)
            {
                // (the ways chosen before that moment stay: she takes them again)
                dashes.RemoveAll(d => d.Chosen > time);
                done = false;
                ClearBullets();
                resetTime = time;
                sounded = -1;

                for (int k = 0; k < dashes.Count; k++)
                {
                    if (dashes[k].Start <= time)
                        sounded = k;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (the next way is chosen as soon as she has got to the edge: the first one when she starts shooting)
                while (!done)
                {
                    double chooseAt = dashes.Count == 0 ? Start : dashes[^1].Arrive;

                    if (chooseAt > now)
                        break;

                    double grid = beat * DanmakusuBalance.AYA_DASH_EVERY_BEATS;
                    double start = Start + Math.Ceiling((chooseAt + beat * DanmakusuBalance.AYA_PREVIEW_LEAST_BEATS - Start) / grid - 1e-6) * grid;

                    if (start >= Period.End)
                    {
                        done = true;
                        break;
                    }

                    Vector2 from = dashes.Count == 0 ? f.bossPositionOf(Period) : dashes[^1].To;
                    Vector2 way = choose(f, from, out float length);
                    dashes.Add(new Dash { Chosen = chooseAt, Start = start, Arrive = start + length / Math.Max(speed, 1e-3f), From = from, To = from + way * length, Way = way });
                }

                // (every dash is heard as it goes: one of Kagerou's swings)
                for (; sounded + 1 < dashes.Count && dashes[sounded + 1].Start <= now; sounded++)
                {
                    double start = dashes[sounded + 1].Start;

                    if (now - start < 250 && start > resetTime)
                        f.playStageSound($"kagerou_swing_{(sounded + 1) % 10}");
                }
            }

            /// <summary>The way out from <paramref name="from"/> to the edge of the screen along which the most bullets are; <paramref name="length"/> how far it is to the edge.</summary>
            private Vector2 choose(BulletField f, Vector2 from, out float length)
            {
                Vector2 low = f.stageMin, high = f.stageMax;
                from = Vector2.Clamp(from, low, high);
                float corridor = f.circleRadius * DanmakusuBalance.AYA_SLASH_CIRCLES;
                float shortest = Math.Min(high.X - low.X, high.Y - low.Y) * 0.25f;
                float best = float.NegativeInfinity;
                Vector2 bestWay = Vector2.UnitY;
                length = 0f;

                for (int k = 0; k < DanmakusuBalance.AYA_DIRECTIONS; k++)
                {
                    float angle = k * MathF.Tau / DanmakusuBalance.AYA_DIRECTIONS;
                    var way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    float reach = (float)f.toWall(from, way, out _);

                    if (reach < shortest)
                        continue; // (not into the wall right beside her)

                    int count = 0;

                    for (int i = 0; i < f.drawnInstances && i < f.instanceBuffer.Length; i++)
                    {
                        Vector2 d = f.instanceBuffer[i].Position - from;
                        float along = Vector2.Dot(d, way);

                        if (along < 0f || along > reach)
                            continue;

                        if (MathF.Abs(d.X * way.Y - d.Y * way.X) <= corridor)
                            count++;
                    }

                    // (the most bullets; of those as good, the longest way)
                    float score = count * 100000f + reach;

                    if (score > best)
                    {
                        best = score;
                        bestWay = way;
                        length = reach;
                    }
                }

                // (nowhere far enough: straight across, the longer way)
                if (float.IsNegativeInfinity(best))
                {
                    bestWay = from.Y - low.Y > high.Y - from.Y ? -Vector2.UnitY : Vector2.UnitY;
                    length = (float)f.toWall(from, bestWay, out _);
                }

                return bestWay;
            }

            // ---- her way: the preview, and the trace behind her ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || dashes.Count == 0)
                    return;

                var core = new Color4(1f, 0.95f, 0.92f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.86f, 0.18f, 0.18f, 1f));
                float half = halfWidth(f);

                for (int k = dashes.Count - 1; k >= 0; k--)
                {
                    var d = dashes[k];

                    if (d.Arrive + traceLife < t)
                        break; // (the older ones are gone too)

                    if (t < d.Chosen)
                        continue;

                    float alpha;
                    Vector2 end;

                    if (t < d.Start)
                    {
                        alpha = DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - d.Chosen) / (beat * 0.15));
                        end = d.To;
                    }
                    else
                    {
                        alpha = 1f - smoothStep((t - (d.Arrive + traceLife - beat * DanmakusuBalance.AYA_TRACE_FADE_BEATS)) / (beat * DanmakusuBalance.AYA_TRACE_FADE_BEATS));
                        end = placeIn(d, t);
                    }

                    if (alpha < 0.01f || (end - d.From).LengthSquared < 0.25f)
                        continue;

                    line.Clear();
                    line.Add(d.From);
                    line.Add(end);
                    fades.Clear();
                    fades.Add(alpha);
                    fades.Add(alpha);
                    f.addPlainBeam(line, half, core, border, ref beams, ref pointTotal, null, fades);
                }
            }

            /// <summary>The trace behind her hurts like a slider body (not while it fades).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || dashes.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int k = dashes.Count - 1; k >= 0; k--)
                {
                    var d = dashes[k];

                    if (d.Arrive + traceLife < now)
                        break;

                    if (now < d.Start || now >= d.Arrive + traceLife - beat * DanmakusuBalance.AYA_TRACE_FADE_BEATS)
                        continue;

                    if (distanceToSegment(player, d.From, placeIn(d, now)) <= reach)
                        return true;
                }

                return false;
            }

            // ---- what she passes ----

            /// <summary>The dash she is flying at <paramref name="t"/>, or -1.</summary>
            private int flying(double t)
            {
                for (int k = dashes.Count - 1; k >= 0; k--)
                {
                    if (dashes[k].Start <= t && t <= dashes[k].Arrive)
                        return k;

                    if (dashes[k].Arrive < t)
                        break;
                }

                return -1;
            }

            public override bool Absorbs(double t) => flying(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                for (int k = dashes.Count - 1; k >= 0; k--)
                {
                    if (dashes[k].Start <= to && dashes[k].Arrive >= from)
                        return true;

                    if (dashes[k].Arrive < from)
                        break;
                }

                return false;
            }

            /// <summary>A bullet she passes flies straight out to its side of her way, twice as fast.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || (emitter.FollowsBoss && emitter.BossRank == Period.Rank))
                    return false;

                int k = flying(t);

                if (k < 0)
                    return false;

                var d = dashes[k];
                Vector2 a = placeIn(d, Math.Min(ScanPrevious, t)), b = placeIn(d, t);
                float reach = f.circleRadius * DanmakusuBalance.AYA_SLASH_CIRCLES + emitter.HitRadius * emitter.BigScale(bullet);

                if (distanceToSegment(to, a, b) > reach)
                    return false;

                // (it comes apart into two black feathers, as a crow's - Matenshi's feathers, black - flying straight out to either side of her way, twice as fast: 2026-11-22, it flew out to its own side as it was)
                Vector2 side = new Vector2(-d.Way.Y, d.Way.X);
                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t) * DanmakusuBalance.AYA_SLASH_SPEED;
                int before = Bullets.Count;

                foreach (float sign in new[] { 1f, -1f })
                {
                    var feather = f.makeStageBullet(stored, emitter, bullet, t, to, side * (sign * speed));
                    feather.Shape = BulletShape.Feather;
                    feather.Spin = DanmakusuBalance.AYA_FEATHER_SPIN * sign; // (it turns round as it flies, the two of a bullet opposite ways: 2026-11-25)
                    feather.SpinPhase = MathF.Atan2(side.Y * sign, side.X * sign);
                    feather.Radius *= DanmakusuBalance.MATENSHI_FEATHER_SIZE;
                    feather.Hit *= DanmakusuBalance.MATENSHI_FEATHER_SIZE;
                    feather.Tint = BulletRenderer.TINT_HISAMI_HAIR; // (black, with a sheen: a crow's)
                    feather.TintAmount = 1f;
                    feather.Rimmed = true; // (with a white rim, to be seen on the dark: 2026-11-22)
                    feather.Untraced = true; // (it flies straight out: no other boss's op turns it)
                    AddBullet(feather);
                }

                return Bullets.Count > before;
            }
        }
    }
}
