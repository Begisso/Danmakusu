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
        // Alice Margatroid (2026-11-20): no pauses. When she starts shooting, twelve fumos of her (twenty-five until 2026-11-21; the user's photo of the plush, as tall as a boss's picture) come out of her over two
        // beats and wander over the whole screen, each from one place to the next every eight beats of moving (chosen by its number, a little apart from the others), standing still while they shoot a stream;
        // each stands on a yellow magic circle half as big as a boss's, drawn above everything but the fumos. Every two beats each shoots a stream - two of every four at the player, one thirty degrees to its
        // right, one to its left: an arrowhead, a plain bullet, an ellipse (it falls on towards the player, as an ellipse of a ring does) and a ricochet star (it bounces once off the edge of
        // the screen), one after another along one line, with a straight beam as long as the stream on either side of it, flying with it; the beams hurt like a slider body. The fumos themselves do not hurt.
        // Since 2026-11-25 each beam flies ALICE_BEAM_TURN_DEGREES out from the stream's way, to its own side (they flew alongside it).
        // They go back into her over a beat when her section ends. Where each fumo is follows from the time; where each stream was aimed is kept, so time going back fires them again the same way.
        // ======================================================================================================================================================

        private sealed class DollsAct : StageAct
        {
            private struct Beam
            {
                public double T; // (its front sets off)
                public Vector2 From;
                public Vector2 Way;
                public float Speed;
                public float Length; // (as long as the stream)
                public float Across; // (from where it set off to past the edge of the screen)
                public double Gone;
            }

            private readonly double beat;
            private readonly double comeOut; // (when the fumos start coming out of her)
            private readonly int seed;
            private readonly List<double> volleys = new List<double>();
            private readonly Dictionary<int, (Vector2 From, Vector2 Way)[]> aimed = new Dictionary<int, (Vector2, Vector2)[]>();
            private int next;

            private readonly List<Beam> beams = new List<Beam>();
            private int firstAlive;
            private readonly MomentShapes shapes = new MomentShapes();
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<(double Time, Vector2 Where)> bounces = new List<(double, Vector2)>();

            public DollsAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                comeOut = period.ShootStart;
                seed = (int)(Math.Abs(period.Start) % 100000);

                for (double t = comeOut + beat * DanmakusuBalance.ALICE_COME_OUT_BEATS; t < period.End - 1 && volleys.Count < 10000; t += beat * DanmakusuBalance.ALICE_STREAM_EVERY_BEATS)
                    volleys.Add(t);

                Start = comeOut;
                End = period.End + 8000; // (the last streams fly on after her section)
            }

            /// <summary>She stays until her fumos are back in her.</summary>
            public override double MechanicsEnd => Period.End + beat * DanmakusuBalance.ALICE_GO_BACK_BEATS;

            // ---- the fumos ----

            /// <summary>Place <paramref name="k"/> of fumo <paramref name="i"/>'s wandering: anywhere on the screen (2026-11-21; it was the 512 x 384 playfield), a fumo's height in from its edges.</summary>
            private Vector2 spot(BulletField f, int i, int k)
            {
                float margin = DanmakusuBalance.ALICE_FUMO_HEIGHT;
                Vector2 low = f.stageMin + new Vector2(margin), size = Vector2.ComponentMax(f.stageMax - f.stageMin - new Vector2(2f * margin), Vector2.One);
                return low + new Vector2(hash01(i * 7 + 1, k, seed) * size.X, hash01(k * 13 + 5, i, seed + 11) * size.Y);
            }

            /// <summary>How long a stream takes to come out (from its first bullet to its last).</summary>
            private double streamMs(BulletField f)
                => 3.0 * DanmakusuBalance.ALICE_STREAM_SPACING * f.stageBaseRadius(Period) / Math.Max(stageBulletSpeed(Period) * DanmakusuBalance.ALICE_SPEED, 1e-4f);

            /// <summary>
            /// How long the fumos have moved by <paramref name="t"/>: they stand still while they shoot a stream and move between streams (2026-11-21, "stop when shooting stream, move during pauses").
            /// The streams come evenly, every two beats from the first, so this is worked out from the time.
            /// </summary>
            private double movingTime(BulletField f, double t)
            {
                if (volleys.Count == 0 || t <= volleys[0])
                    return t - comeOut;

                double every = beat * DanmakusuBalance.ALICE_STREAM_EVERY_BEATS;
                double stand = Math.Min(streamMs(f), every * 0.9);
                double after = t - volleys[0];
                double n = Math.Floor(after / every);
                double within = after - n * every;
                return volleys[0] - comeOut + n * (every - stand) + Math.Clamp(within - stand, 0, every - stand);
            }

            /// <summary>Where fumo <paramref name="i"/> has wandered to at <paramref name="t"/>: from one place to the next, easing in and out, each fumo a little apart from the others.</summary>
            private Vector2 wanderAt(BulletField f, int i, double t)
            {
                double u = movingTime(f, t) / (beat * DanmakusuBalance.ALICE_WANDER_BEATS) + i * 0.37;
                int k = (int)Math.Floor(u);
                return Vector2.Lerp(spot(f, i, k), spot(f, i, k + 1), smoothStep(u - k));
            }

            /// <summary>How far out of her the fumos are at <paramref name="t"/> (0 in her, 1 out).</summary>
            private float outOfHer(double t)
                => smoothStep((t - comeOut) / (beat * DanmakusuBalance.ALICE_COME_OUT_BEATS)) * (1f - smoothStep((t - Period.End) / (beat * DanmakusuBalance.ALICE_GO_BACK_BEATS)));

            private Vector2 fumoAt(BulletField f, int i, double t) => Vector2.Lerp(f.bossPositionOf(Period), wanderAt(f, i, t), outOfHer(t));

            /// <summary>Which way fumo <paramref name="i"/> aims (radians from straight at the player): two of every four at the player, one 30 degrees to the right of the player, one to the left.</summary>
            private static float asideOf(int i) => (i % 4) switch
            {
                2 => DanmakusuBalance.ALICE_ASIDE_DEGREES * MathF.PI / 180f,
                3 => -DanmakusuBalance.ALICE_ASIDE_DEGREES * MathF.PI / 180f,
                _ => 0f,
            };

            // ---- the streams ----

            public override void Reset(BulletField f, double time)
            {
                // (the streams before that moment are fired again the way they were; the later ones are aimed when their time comes)
                ClearBullets();
                beams.Clear();
                firstAlive = 0;
                shapes.Forget();
                next = 0;

                foreach (int k in new List<int>(aimed.Keys))
                {
                    if (volleys[k] > time)
                        aimed.Remove(k);
                }

                for (; next < volleys.Count && volleys[next] <= time; next++)
                {
                    if (aimed.TryGetValue(next, out var known))
                    {
                        for (int i = 0; i < known.Length; i++)
                            stream(f, volleys[next], i, known[i].From, known[i].Way, false);
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the beams move on)

                while (firstAlive < beams.Count && beams[firstAlive].Gone < now)
                    firstAlive++;

                if (f.failed)
                    return;

                for (; next < volleys.Count && volleys[next] <= now; next++)
                {
                    double t = volleys[next];
                    var each = new (Vector2 From, Vector2 Way)[DanmakusuBalance.ALICE_FUMOS];

                    for (int i = 0; i < each.Length; i++)
                    {
                        Vector2 from = fumoAt(f, i, t);
                        Vector2 toPlayer = f.characterPosition - from;
                        float angle = (toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f) + asideOf(i);
                        each[i] = (from, new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
                        stream(f, t, i, each[i].From, each[i].Way, true);
                    }

                    aimed[next] = each;
                }
            }

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>Fumo <paramref name="i"/>'s stream at <paramref name="t"/>: four bullets one after another along one line, a beam as long as they are on either side.</summary>
            private void stream(BulletField f, double t, int i, Vector2 from, Vector2 way, bool live)
            {
                float r = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.ALICE_SPEED;
                double gap = DanmakusuBalance.ALICE_STREAM_SPACING * r / Math.Max(speed, 1e-4f);
                Vector2 v = way * speed;
                int style = f.comboStyle(i, true, Period.Rank);
                double life = f.stageLife(v);
                float hit = r * DanmakusuBalance.BULLET_HITBOX_SCALE;

                // an arrowhead (aimed at the player, it has nothing to turn: it flies straight), a plain bullet, an ellipse that falls on towards the player
                AddBullet(new StageBullet { T0 = t, P0 = from, V = v, Radius = r, Hit = hit, Style = style, Shape = BulletShape.Triangle, Until = t + life });
                AddBullet(new StageBullet { T0 = t + gap, P0 = from, V = v, Radius = r, Hit = hit, Style = style, Shape = BulletShape.Circle, Until = t + gap + life });

                float gravity = Period.ArrowGravity > 0f ? Period.ArrowGravity : DanmakusuBalance.EllipseGravity(4f);
                AddBullet(new StageBullet { T0 = t + 2 * gap, P0 = from, V = v, Acc = way * gravity, Radius = r, Hit = hit, Style = style, Shape = BulletShape.Ellipse, Until = t + 2 * gap + life });

                // a ricochet star: it bounces off the edge of the screen once, as a ring's does
                double starAt = t + 3 * gap;
                float star = r * DanmakusuBalance.ALICE_STAR_SIZE;
                bounces.Clear();
                var legs = f.bouncingLegs(starAt, from, v, DanmakusuBalance.MAX_RICOCHETS, bounces);
                var lastLeg = legs[^1];

                AddBullet(new StageBullet
                {
                    T0 = starAt,
                    P0 = from,
                    V = v,
                    Legs = legs,
                    Radius = star,
                    Hit = star * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = style,
                    Shape = BulletShape.Star,
                    Spin = 0.004f * (i % 2 == 0 ? 1f : -1f),
                    SpinPhase = i,
                    Until = lastLeg.T + f.toWall(lastLeg.P, lastLeg.V, out _) + star * 3f / Math.Max(speed, 1e-3f),
                });

                if (live)
                {
                    foreach (var (time, where) in bounces)
                        f.addFlash(time, where, star * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                }

                // the beams: as long as the stream (from its first bullet to its last), either side of its middle line
                float length = speed * (float)(3 * gap);
                float half = halfWidth(f);
                Vector2 across = new Vector2(-way.Y, way.X) * (DanmakusuBalance.ALICE_BEAM_SIDE * r + half);

                for (int side = -1; side <= 1; side += 2)
                {
                    // (a little out from the stream's way, to its own side: 2026-11-25 - they flew alongside it)
                    Vector2 start = from + across * side;
                    Vector2 beamWay = turned(way, side * DanmakusuBalance.ALICE_BEAM_TURN_DEGREES * MathF.PI / 180f);
                    float reach = speed * (float)f.toWall(start, beamWay * speed, out _) + half;

                    beams.Add(new Beam
                    {
                        T = t,
                        From = start,
                        Way = beamWay,
                        Speed = speed,
                        Length = length,
                        Across = reach,
                        Gone = t + (reach + length) / Math.Max(speed, 1e-4f),
                    });
                }

                shapes.Forget();
            }

            /// <summary>Where a beam is at <paramref name="t"/>: its front runs out with the stream's first bullet, its back follows with the last.</summary>
            private static (Vector2 Back, Vector2 Front) span(in Beam beam, double t)
            {
                float run = beam.Speed * (float)(t - beam.T);
                float front = Math.Clamp(run, 0f, beam.Across);
                float back = Math.Clamp(run - beam.Length, 0f, beam.Across);
                return (beam.From + beam.Way * back, beam.From + beam.Way * front);
            }

            public override void DrawBeams(BulletField f, double t, ref int drawn, ref int pointTotal)
            {
                if (f.failed || beams.Count == 0)
                    return;

                var core = new Color4(0.95f, 0.97f, 1f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(74 / 255f, 143 / 255f, 216 / 255f, 1f));
                float half = halfWidth(f);

                for (int k = firstAlive; k < beams.Count; k++)
                {
                    var beam = beams[k];

                    if (beam.T > t || beam.Gone < t)
                        continue;

                    var (back, front) = span(beam, t);

                    if ((front - back).LengthSquared < 0.25f)
                        continue;

                    line.Clear();
                    line.Add(back);
                    line.Add(front);
                    f.addPlainBeam(line, half, core, border, ref drawn, ref pointTotal);
                }
            }

            /// <summary>The beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || beams.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the beams of that moment are worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    for (int k = firstAlive; k < beams.Count; k++)
                    {
                        if (beams[k].T > now || beams[k].Gone < now)
                            continue;

                        var (back, front) = span(beams[k], now);
                        fresh.Point(back, reach);
                        fresh.Point(front, reach);
                        fresh.EndRun();
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                for (int k = firstAlive; k < beams.Count; k++)
                {
                    if (beams[k].T > now || beams[k].Gone < now)
                        continue;

                    var (back, front) = span(beams[k], now);

                    if (distanceToSegment(player, back, front) <= reach)
                        return true;
                }

                return false;
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                float there = outOfHer(t);

                if (f.failed || there <= 0.001f || t < comeOut)
                    return;

                var yellow = new Color4(1f, 0.86f, 0.25f, 1f);
                float circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f * DanmakusuBalance.ALICE_CIRCLE_SHARE;
                Vector2 fumo = new Vector2(DanmakusuBalance.ALICE_FUMO_HEIGHT * 186f / 256f, DanmakusuBalance.ALICE_FUMO_HEIGHT);
                float alpha = Math.Clamp(there * 2f, 0f, 1f);

                // (the circles above everything but the fumos - all of them, then all the fumos over them: 2026-11-21; they were under the bullets. Each turns slowly as a boss's does, from its own angle)
                for (int i = 0; i < DanmakusuBalance.ALICE_FUMOS; i++)
                    f.stageFront.Add(StageTexture.BossCircleWhite, fumoAt(f, i, t), new Vector2(circle * (0.4f + 0.6f * there)), (float)(t * 0.024) + i * 37f, DanmakusuBalance.ALICE_CIRCLE_ALPHA * alpha, yellow);

                // (the fumo rocks a little from side to side as it goes)
                for (int i = 0; i < DanmakusuBalance.ALICE_FUMOS; i++)
                {
                    float rock = 6f * MathF.Sin((float)((t - comeOut) / beat * MathF.PI) + i * 1.3f);
                    f.stageFront.Add(StageTexture.Fumo, fumoAt(f, i, t), fumo * (0.3f + 0.7f * there), rock, alpha, Color4.White);
                }
            }
        }
    }
}
