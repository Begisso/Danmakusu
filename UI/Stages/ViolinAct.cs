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
        // Lunasa Prismriver (2026-11-27). Every LUNASA_EVERY_BEATS of her section she shoots LUNASA_BEAMS long straight beams side by side (LUNASA_BEAM_GAP_CIRCLES circle radii apart) at the player: a
        // see-through preview for LUNASA_PREVIEW_BEATS, then solid for LUNASA_BEAM_BEATS, hurting like a slider body. Where she is and where the player is are taken as the preview begins.
        // Every circle of her section as it is hit, and every slider as it is over, breaks: into LUNASA_PIECES pieces flying out of its middle (a slider into a piece every two circle radii along it,
        // flying out of the slider's middle) - or, while her beams are solid, it is cut in two along them and the halves fly apart across them. The pieces are drawn in the object's colour as cut
        // bullets are (CuttingAct), fly at LUNASA_PIECE_SPEED times a bullet's speed and hurt like a slider body.
        // ======================================================================================================================================================

        private sealed class ViolinAct : CuttingAct
        {
            private readonly double beat;
            private readonly double[] volleys;
            private readonly Vector2[] from, way;
            private readonly bool[] known;
            private readonly List<(double Time, Vector2 Place, float Radius, SliderZone? Slider, int Number)> objects = new List<(double, Vector2, float, SliderZone?, int)>();
            private int nextObject;
            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public ViolinAct(BossPeriod period, CircleZone[] circles, SliderZone[] sliders)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.LUNASA_PREVIEW_BEATS < period.End && list.Count < 10000; t += beat * DanmakusuBalance.LUNASA_EVERY_BEATS)
                    list.Add(t);

                volleys = list.ToArray();
                from = new Vector2[volleys.Length];
                way = new Vector2[volleys.Length];
                known = new bool[volleys.Length];

                int number = 0;

                foreach (var circle in circles)
                {
                    if (circle.Time >= period.ShootStart && circle.Time < period.End)
                        objects.Add((circle.Time, circle.Position, circle.Radius, null, number++));
                }

                foreach (var slider in sliders)
                {
                    if (slider.FallSpeed <= 0f && slider.EndTime >= period.ShootStart && slider.EndTime < period.End && slider.Points.Length > 1)
                        objects.Add((slider.EndTime, slider.Points[slider.Points.Length / 2], slider.Radius, slider, number++));
                }

                objects.Sort((a, b) => a.Time.CompareTo(b.Time));
                Start = period.ShootStart;
                End = period.End + beat * 32;
                AbsorbFrom = double.PositiveInfinity; // (she takes no bullets: only the objects break)
                AbsorbTo = double.NegativeInfinity;
            }

            private double solidAt(int k) => volleys[k] + beat * DanmakusuBalance.LUNASA_PREVIEW_BEATS;

            private double overAt(int k) => solidAt(k) + beat * DanmakusuBalance.LUNASA_BEAM_BEATS;

            /// <summary>The volley <paramref name="t"/> is in (its preview or its beams), or -1.</summary>
            private int volleyAt(double t)
            {
                if (volleys.Length == 0 || t < volleys[0])
                    return -1;

                int k = Math.Min(volleys.Length - 1, (int)((t - volleys[0]) / (beat * DanmakusuBalance.LUNASA_EVERY_BEATS)));
                return t < overAt(k) ? k : -1;
            }

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.LUNASA_BEAM_HALF_CIRCLES;

            private void beam(BulletField f, int k, int which, out Vector2 a, out Vector2 b)
            {
                Vector2 across = new Vector2(-way[k].Y, way[k].X);
                a = from[k] + across * ((which - (DanmakusuBalance.LUNASA_BEAMS - 1) * 0.5f) * f.circleRadius * DanmakusuBalance.LUNASA_BEAM_GAP_CIRCLES);
                b = a + way[k] * (Vector2.Distance(f.stageMin, f.stageMax) * 1.5f);
            }

            public override void Reset(BulletField f, double time)
            {
                ClearPieces();

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleys[k] > time)
                        known[k] = false;
                }

                nextObject = 0;

                while (nextObject < objects.Count && objects[nextObject].Time <= time)
                    nextObject++;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < volleys.Length && volleys[k] <= now; k++)
                {
                    if (known[k])
                        continue;

                    known[k] = true;
                    from[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from[k];
                    way[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                }

                // the objects break as they are hit or over
                while (nextObject < objects.Count && objects[nextObject].Time <= now)
                {
                    var o = objects[nextObject++];
                    int k = volleyAt(o.Time);
                    bool slashed = k >= 0 && known[k] && o.Time >= solidAt(k);
                    breakUp(f, o.Time, o.Place, o.Radius, o.Slider, o.Number, slashed ? way[k] : (Vector2?)null);
                }
            }

            private void breakUp(BulletField f, double t, Vector2 middle, float radius, SliderZone? slider, int number, Vector2? slashAlong)
            {
                int style = f.comboStyle(number, false, Period.Rank);
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.LUNASA_PIECE_SPEED;
                var places = new List<Vector2>();

                if (slider == null)
                    places.Add(middle);
                else
                {
                    // (a piece every two circle radii along the slider)
                    float run = 0f;
                    places.Add(slider.Points[0]);

                    for (int i = 1; i < slider.Points.Length; i++)
                    {
                        run += Vector2.Distance(slider.Points[i - 1], slider.Points[i]);

                        if (run >= radius * 2f)
                        {
                            places.Add(slider.Points[i]);
                            run = 0f;
                        }
                    }
                }

                foreach (Vector2 at in places)
                {
                    if (slashAlong is Vector2 along)
                    {
                        // (cut in two along her beams: the halves fly apart across them)
                        Vector2 normal = new Vector2(-along.Y, along.X);
                        var disc = disk(radius);
                        addSliderPiece(f, t, at, normal * speed, radius, style, clipHalf(disc, normal));
                        addSliderPiece(f, t, at, -normal * speed, radius, style, clipHalf(disc, -normal));
                        continue;
                    }

                    if (slider == null)
                    {
                        // (a circle: pieces like slices of it, flying out of its middle)
                        int n = DanmakusuBalance.LUNASA_PIECES;
                        float turn = hash01(number, 3, 617) * MathF.Tau;

                        for (int p = 0; p < n; p++)
                        {
                            float a0 = turn + p * MathF.Tau / n, a1 = turn + (p + 1) * MathF.Tau / n, mid = (a0 + a1) * 0.5f;
                            var slice = new[] { Vector2.Zero, new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius, new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * radius, new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius };
                            addSliderPiece(f, t, at, new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * speed, radius, style, slice);
                        }
                    }
                    else
                    {
                        // (a slider: every piece flies out of its middle)
                        Vector2 out_ = at - middle;
                        float spin = hash01(number, places.IndexOf(at), 619) * MathF.Tau;
                        out_ = out_.LengthSquared > 1f ? Vector2.Normalize(out_) : new Vector2(MathF.Cos(spin), MathF.Sin(spin));
                        addSliderPiece(f, t, at, out_ * speed, radius, style, disk(radius * 0.9f));
                    }
                }
            }

            private static Vector2[] disk(float radius)
            {
                var round = new Vector2[12];

                for (int i = 0; i < round.Length; i++)
                {
                    float angle = i * MathF.Tau / round.Length;
                    round[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                }

                return round;
            }

            /// <summary>The half of an outline on the side <paramref name="normal"/> points to.</summary>
            private static Vector2[] clipHalf(Vector2[] outline, Vector2 normal)
            {
                var kept = new List<Vector2>(outline.Length + 2);

                for (int i = 0; i < outline.Length; i++)
                {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                    float da = Vector2.Dot(a, normal), db = Vector2.Dot(b, normal);

                    if (da >= 0f)
                        kept.Add(a);

                    if ((da >= 0f) != (db >= 0f))
                        kept.Add(a + (b - a) * (da / (da - db)));
                }

                return kept.ToArray();
            }

            /// <summary>A piece that hurts like a slider body (Hurts), not as a bullet that is used up.</summary>
            private void addSliderPiece(BulletField f, double t, Vector2 at, Vector2 velocity, float radius, int style, Vector2[] outline)
            {
                int before = Bullets.Count;
                AddPiece(f, t, at, velocity, radius * 0.6f, radius * 0.5f, style, 0, outline, 0f, radius);

                if (Bullets.Count <= before)
                    return;

                var piece = Bullets[before];
                piece.Slider = true;
                Bullets[before] = piece;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                int k = volleyAt(t);

                if (f.failed || k < 0 || !known[k])
                    return;

                bool solid = t >= solidAt(k);
                float alpha = solid
                    ? 1f - smoothStep((t - (overAt(k) - beat * 0.25)) / (beat * 0.25))
                    : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - volleys[k]) / (beat * 0.25));

                if (alpha < 0.01f)
                    return;

                var core = new Color4(1f, 0.95f, 0.8f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.85f, 0.7f, 0.3f, 1f)); // (her gold)

                for (int which = 0; which < DanmakusuBalance.LUNASA_BEAMS; which++)
                {
                    beam(f, k, which, out Vector2 a, out Vector2 b);
                    line.Clear();
                    fades.Clear();
                    line.Add(a);
                    line.Add(b);
                    fades.Add(alpha);
                    fades.Add(alpha);
                    f.addPlainBeam(line, halfWidth(f), core, border, ref beams, ref pointTotal, null, fades, null, solid ? 0.5f : 0f);
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (!f.failed)
                    DrawPieces(f, t);
            }

            /// <summary>Her solid beams and the pieces hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                int k = volleyAt(now);

                if (k >= 0 && known[k] && now >= solidAt(k))
                {
                    float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    for (int which = 0; which < DanmakusuBalance.LUNASA_BEAMS; which++)
                    {
                        beam(f, k, which, out Vector2 a, out Vector2 b);

                        if (distanceToSegment(player, a, b) <= reach)
                            return true;
                    }
                }

                // (the pieces are made in time order: only those made in the last while can still fly)
                int lo = 0, hi = Bullets.Count;

                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;

                    if (Bullets[mid].T0 < now - 15000)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                for (int i = lo; i < Bullets.Count; i++)
                {
                    var piece = Bullets[i];

                    if (now < piece.T0 || now >= piece.Until)
                        continue;

                    if (Vector2.Distance(stageBulletPosition(piece, now), player) <= piece.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }
        }
    }
}
