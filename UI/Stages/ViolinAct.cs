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
        // Lunasa Prismriver (2026-11-27). Every LUNASA_EVERY_BEATS of her section she shoots LUNASA_BEAMS long straight beams at the player: a see-through preview for LUNASA_PREVIEW_BEATS, then solid for
        // LUNASA_BEAM_BEATS, hurting like a slider body. Where she is and where the player is are taken as the preview begins.
        // Since 2026-11-28 ("laserbeams are slider laserbeams thick. Shoot out of 5 points on magic circle edge, keep the distance in-between laserbeams. Byakuren sound plays. All bullets touching laserbeams
        // get slashed. Duration doubled. not parallel anymore, spread 30 angles. In gaps laserbeams of gap size come out as 5k osumania convert notes"): the beams are as thick as a slider's and solid twice as
        // long (so the next volley's preview comes while they are still there); they start on the edge of her magic circle, LUNASA_BEAM_GAP_CIRCLES circle radii apart along it (at most
        // LUNASA_EDGE_MOST_DEGREES of it between two), and fan out over LUNASA_SPREAD_DEGREES round the player; Byakuren's beam sound plays as they turn solid. Every bullet that touches a solid beam is cut
        // in two along it (CuttingAct). In every gap between two beams come the notes of an osu!mania convert of the map with a key for every gap (the planner's, BossPeriod.FallingNotes; five beams leave
        // four gaps, so four keys): a note that comes while the beams are solid comes out of the edge of her circle between the starts of the two beams on its time, as a beam laid across the gap - as long
        // as the gap is wide, wider as it goes - and flies out along the gap, crossing a screen's height in LUNASA_NOTE_BEATS; a held note is a band as long as it is held. They fly on to the edge of the
        // screen when the beams are gone, and hurt like a slider body.
        // Every circle of her section as it is hit, and every slider as it is over, breaks: into LUNASA_PIECES pieces flying out of its middle (a slider into a piece every two circle radii along it,
        // flying out of the slider's middle) - or, while her beams are solid, it is cut in two along the nearest of them and the halves fly apart across it. The pieces are drawn in the object's colour as
        // cut bullets are (CuttingAct), fly at LUNASA_PIECE_SPEED times a bullet's speed and hurt like a slider body.
        // ======================================================================================================================================================

        private sealed class ViolinAct : CuttingAct
        {
            private const int beams = DanmakusuBalance.LUNASA_BEAMS;
            private const int gaps = DanmakusuBalance.LUNASA_BEAMS - 1;

            private readonly double beat;
            private readonly double[] volleys;
            private readonly Vector2[] from, way;
            private readonly Vector2[] starts, dirs; // (every volley's beams: where each starts on her circle and the way it goes)
            private readonly float[] reachOut; // (every volley's: how far from her middle the farthest corner of the screen is)
            private readonly bool[] known;
            private readonly List<(double Time, Vector2 Place, float Radius, SliderZone? Slider, int Number)> objects = new List<(double, Vector2, float, SliderZone?, int)>();
            private int nextObject;

            // (every gap's notes in time order, the volley each comes out in (-1: none), and the latest end so far, to find the first one not gone yet)
            private readonly (double Start, double End)[][] lanes;
            private readonly int[][] laneVolley;
            private readonly double[][] endsSoFar;

            // (where the objects' pieces are among the act's bullets, in time order: only they hurt through Hurts - the cut bullets' pieces, far more of them since 2026-11-28, hit as bullets do)
            private readonly List<int> sliderPieces = new List<int>();

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);
            private readonly List<Vector2> notePoints = new List<Vector2>(32);
            private readonly List<float> noteWidths = new List<float>(32);
            private readonly List<Vector2> hurtPoints = new List<Vector2>(32);
            private readonly List<float> hurtWidths = new List<float>(32);

            /// <summary>When every volley's preview begins.</summary>
            public static List<double> VolleyTimes(BossPeriod period)
            {
                double beat = Math.Max(period.BeatLength, 1);
                var list = new List<double>();

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.LUNASA_PREVIEW_BEATS < period.End && list.Count < 10000; t += beat * DanmakusuBalance.LUNASA_EVERY_BEATS)
                    list.Add(t);

                return list;
            }

            /// <summary>When every volley's beams turn solid (Byakuren's beam sound then).</summary>
            public static List<double> SolidTimes(BossPeriod period)
            {
                double beat = Math.Max(period.BeatLength, 1);
                var list = VolleyTimes(period);

                for (int i = 0; i < list.Count; i++)
                    list[i] += beat * DanmakusuBalance.LUNASA_PREVIEW_BEATS;

                return list;
            }

            public ViolinAct(BossPeriod period, CircleZone[] circles, SliderZone[] sliders)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                volleys = VolleyTimes(period).ToArray();
                from = new Vector2[volleys.Length];
                way = new Vector2[volleys.Length];
                starts = new Vector2[volleys.Length * beams];
                dirs = new Vector2[volleys.Length * beams];
                reachOut = new float[volleys.Length];
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

                // the notes, every gap its own, each in the volley whose beams are solid on its time
                var perLane = new List<(double Start, double End)>[gaps];

                for (int c = 0; c < gaps; c++)
                    perLane[c] = new List<(double Start, double End)>();

                foreach (var (column, start, end) in period.FallingNotes)
                    perLane[Math.Clamp(column, 0, gaps - 1)].Add((start, Math.Max(end, start)));

                lanes = new (double Start, double End)[gaps][];
                laneVolley = new int[gaps][];
                endsSoFar = new double[gaps][];
                double longest = 0;

                for (int c = 0; c < gaps; c++)
                {
                    perLane[c].Sort((a, b) => a.Start.CompareTo(b.Start));
                    lanes[c] = perLane[c].ToArray();
                    laneVolley[c] = new int[lanes[c].Length];
                    endsSoFar[c] = new double[lanes[c].Length];
                    double latest = double.MinValue;

                    for (int k = 0; k < lanes[c].Length; k++)
                    {
                        laneVolley[c][k] = solidVolley(lanes[c][k].Start);
                        endsSoFar[c][k] = latest = Math.Max(latest, lanes[c][k].End);
                        longest = Math.Max(longest, lanes[c][k].End - lanes[c][k].Start);
                    }
                }

                double lastOver = volleys.Length > 0 ? overAt(volleys.Length - 1) : period.End;
                Start = period.ShootStart;
                End = Math.Max(period.End + beat * 32, lastOver + longest + beat * DanmakusuBalance.LUNASA_NOTE_BEATS * 4);

                // (bullets that touch a solid beam are cut in two)
                AbsorbFrom = volleys.Length > 0 ? solidAt(0) : double.PositiveInfinity;
                AbsorbTo = volleys.Length > 0 ? lastOver : double.NegativeInfinity;
            }

            private double solidAt(int k) => volleys[k] + beat * DanmakusuBalance.LUNASA_PREVIEW_BEATS;

            private double overAt(int k) => solidAt(k) + beat * DanmakusuBalance.LUNASA_BEAM_BEATS;

            private double every => beat * DanmakusuBalance.LUNASA_EVERY_BEATS;

            /// <summary>The volley whose beams are solid at <paramref name="t"/> (the newest, if two are), or -1.</summary>
            private int solidVolley(double t)
            {
                if (volleys.Length == 0 || t < solidAt(0))
                    return -1;

                int k = Math.Min(volleys.Length - 1, (int)((t - solidAt(0)) / every));
                return t < overAt(k) ? k : -1;
            }

            /// <summary>The newest volley that has begun by <paramref name="t"/> (its preview or its beams may be over), or -1.</summary>
            private int newestVolley(double t)
            {
                if (volleys.Length == 0 || t < volleys[0])
                    return -1;

                return Math.Min(volleys.Length - 1, (int)((t - volleys[0]) / every));
            }

            private float beamHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private float beamLength(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) * 1.5f;

            /// <summary>How fast the notes fly out (pixels per ms): a screen's height in LUNASA_NOTE_BEATS.</summary>
            private float noteSpeed(BulletField f) => (float)((f.stageMax.Y - f.stageMin.Y) / (beat * DanmakusuBalance.LUNASA_NOTE_BEATS));

            public override void Reset(BulletField f, double time)
            {
                ClearPieces();
                sliderPieces.Clear();

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
                    aimVolley(f, k);
                }

                // the objects break as they are hit or over
                while (nextObject < objects.Count && objects[nextObject].Time <= now)
                {
                    var o = objects[nextObject++];
                    int k = solidVolley(o.Time);
                    breakUp(f, o.Time, o.Place, o.Radius, o.Slider, o.Number, k >= 0 && known[k] ? nearestWay(f, k, o.Place) : (Vector2?)null);
                }
            }

            /// <summary>
            /// Where a volley's beams start and the ways they go, from where she is and where the player is as its preview begins: the starts on the edge of her magic circle round the way to the player,
            /// a gap apart along it (as far apart as two beams were), the beams fanned out round that way.
            /// </summary>
            private void aimVolley(BulletField f, int k)
            {
                Vector2 middle = from[k] = f.bossPositionOf(Period);
                Vector2 toPlayer = f.characterPosition - middle;
                way[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                float aim = MathF.Atan2(way[k].Y, way[k].X);
                float circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS;
                float gap = f.circleRadius * DanmakusuBalance.LUNASA_BEAM_GAP_CIRCLES;
                float apart = Math.Min(2f * MathF.Asin(Math.Min(1f, gap / (2f * circle))), DanmakusuBalance.LUNASA_EDGE_MOST_DEGREES * MathF.PI / 180f);
                float fan = DanmakusuBalance.LUNASA_SPREAD_DEGREES * MathF.PI / 180f / Math.Max(beams - 1, 1);

                for (int i = 0; i < beams; i++)
                {
                    float off = i - (beams - 1) * 0.5f;
                    float at = aim + off * apart, heading = aim + off * fan;
                    starts[k * beams + i] = middle + new Vector2(MathF.Cos(at), MathF.Sin(at)) * circle;
                    dirs[k * beams + i] = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                }

                // (the notes fly on until they are past the farthest corner of the screen)
                Vector2 low = f.hasScreenBounds ? Vector2.ComponentMin(f.screenMin, f.stageMin) : f.stageMin, high = f.hasScreenBounds ? Vector2.ComponentMax(f.screenMax, f.stageMax) : f.stageMax;
                float far = 0f;

                foreach (Vector2 corner in new[] { low, high, new Vector2(low.X, high.Y), new Vector2(high.X, low.Y) })
                    far = Math.Max(far, Vector2.Distance(corner, middle));

                reachOut[k] = far + f.circleRadius * 2f;
            }

            /// <summary>The way of the beam of a volley nearest to <paramref name="place"/>.</summary>
            private Vector2 nearestWay(BulletField f, int k, Vector2 place)
            {
                float best = float.MaxValue;
                Vector2 along = way[k];

                for (int i = 0; i < beams; i++)
                {
                    Vector2 a = starts[k * beams + i];
                    float there = distanceToSegment(place, a, a + dirs[k * beams + i] * beamLength(f));

                    if (there < best)
                    {
                        best = there;
                        along = dirs[k * beams + i];
                    }
                }

                return along;
            }

            /// <summary>The point of beam <paramref name="i"/> of volley <paramref name="k"/> that lies <paramref name="distance"/> from her middle (not less than the radius of her circle).</summary>
            private Vector2 onBeam(int k, int i, float distance)
            {
                Vector2 start = starts[k * beams + i], d = dirs[k * beams + i];
                Vector2 rel = start - from[k];
                float b = Vector2.Dot(d, rel);
                float s = -b + MathF.Sqrt(Math.Max(0f, b * b + distance * distance - rel.LengthSquared));
                return start + d * Math.Max(0f, s);
            }

            /// <summary>
            /// The shape of a note at <paramref name="t"/> (a line with a half width at every point), or false while it is not out or once it is gone. Its front comes out of the edge of her circle on
            /// its time, its back as it ends, and both fly out at the notes' speed; a tap (or a note held for less than it is thick) is one beam laid across the gap, as long as the gap is wide there (less
            /// the beams on either side), a held note a band along the gap, as wide as it.
            /// </summary>
            private bool noteShape(BulletField f, int k, int lane, (double Start, double End) note, double t, List<Vector2> points, List<float> widths)
            {
                points.Clear();
                widths.Clear();

                float circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS, speed = noteSpeed(f);
                float front = circle + speed * (float)(t - note.Start), back = circle + speed * (float)(t - note.End);

                if (front < circle || back > reachOut[k])
                    return false;

                float half = beamHalf(f);
                front = Math.Min(front, reachOut[k]);
                back = Math.Max(back, circle);

                float widthAt(float distance) => Math.Max(half, Vector2.Distance(onBeam(k, lane, distance), onBeam(k, lane + 1, distance)) * 0.5f - half);

                Vector2 middleAt(float distance) => (onBeam(k, lane, distance) + onBeam(k, lane + 1, distance)) * 0.5f;

                float wFront = widthAt(front), wBack = widthAt(back);

                if (front - back <= wFront + wBack)
                {
                    // (a beam across the gap)
                    float at = (front + back) * 0.5f;
                    float thick = Math.Max(half, (front - back) * 0.5f);
                    Vector2 a = onBeam(k, lane, at), b = onBeam(k, lane + 1, at);
                    float across = Vector2.Distance(a, b);
                    Vector2 middle = (a + b) * 0.5f;
                    float reach = across * 0.5f - half - thick;

                    if (reach <= 0.25f || across < 1e-3f)
                    {
                        Vector2 side = across >= 1e-3f ? (b - a) / across : Vector2.UnitX;
                        points.Add(middle - side * 0.25f);
                        points.Add(middle + side * 0.25f);
                    }
                    else
                    {
                        Vector2 side = (b - a) / across;
                        points.Add(middle - side * reach);
                        points.Add(middle + side * reach);
                    }

                    widths.Add(thick);
                    widths.Add(thick);
                    return true;
                }

                // (a band: its round ends kept inside where it is)
                float from_ = back + wBack, to = front - wFront;
                int steps = Math.Clamp((int)MathF.Ceiling((to - from_) / DanmakusuBalance.LUNASA_NOTE_STEP_PX), 1, 64);

                for (int s = 0; s <= steps; s++)
                {
                    float distance = from_ + (to - from_) * s / steps;
                    points.Add(middleAt(distance));
                    widths.Add(widthAt(distance));
                }

                return true;
            }

            /// <summary>The first note of a gap whose end is not before <paramref name="time"/>.</summary>
            private int firstLeft(int lane, double time)
            {
                double[] ends = endsSoFar[lane];
                int low = 0, high = ends.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (ends[middle] < time)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            /// <summary>How long a note flies before it is past the farthest corner of the screen at the most.</summary>
            private double flight(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) * 2f + DanmakusuBalance.MAGIC_CIRCLE_RADIUS) / Math.Max(noteSpeed(f), 1e-4f);

            /// <summary>A bullet that touches a solid beam (or goes through it) is cut in two along it.</summary>
            public override bool Takes(BulletField f, double t, Vector2 previous, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                int k = solidVolley(t);

                if (k < 0 || !known[k])
                    return false;

                float size = SizeOf(emitter, bullet), reach = beamHalf(f) + size;

                for (int i = 0; i < beams; i++)
                {
                    Vector2 start = starts[k * beams + i], d = dirs[k * beams + i];
                    Vector2 normal = new Vector2(-d.Y, d.X);

                    if (Vector2.Dot(to - start, d) < -size)
                        continue;

                    float off = Vector2.Dot(to - start, normal), before = Vector2.Dot(previous - start, normal);

                    if (MathF.Abs(off) > reach && MathF.Sign(off) == MathF.Sign(before))
                        continue;

                    CutBullet(f, t, previous, to, emitter, bullet, normal, -off);
                    return true;
                }

                return false;
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
                sliderPieces.Add(before);
            }

            public override void DrawBeams(BulletField f, double t, ref int beamCount, ref int pointTotal)
            {
                if (f.failed)
                    return;

                var core = new Color4(1f, 0.95f, 0.8f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.85f, 0.7f, 0.3f, 1f)); // (her gold)
                float half = beamHalf(f), length = beamLength(f);

                // her beams (the newest volley's preview can come while the one before is still solid)
                for (int k = newestVolley(t); k >= 0 && overAt(k) > t; k--)
                {
                    if (!known[k])
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? 1f - smoothStep((t - (overAt(k) - beat * 0.25)) / (beat * 0.25))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - volleys[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int i = 0; i < beams; i++)
                    {
                        Vector2 a = starts[k * beams + i];
                        line.Clear();
                        fades.Clear();
                        line.Add(a);
                        line.Add(a + dirs[k * beams + i] * length);
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, half, core, border, ref beamCount, ref pointTotal, null, fades, null, solid ? 0.5f : 0f);
                    }
                }

                // the notes in the gaps (in her blue)
                var noteCore = new Color4(0.95f, 0.97f, 1f, 1f);
                var noteBorder = DanmakusuBalance.BorderColour(new Color4(0.35f, 0.47f, 0.78f, 1f));
                double flies = flight(f);

                for (int lane = 0; lane < gaps; lane++)
                {
                    var list = lanes[lane];

                    for (int n = firstLeft(lane, t - flies); n < list.Length && list[n].Start <= t; n++)
                    {
                        int k = laneVolley[lane][n];

                        if (k < 0 || !known[k] || !noteShape(f, k, lane, list[n], t, notePoints, noteWidths))
                            continue;

                        f.addPlainBeam(notePoints, half, noteCore, noteBorder, ref beamCount, ref pointTotal, noteWidths);
                    }
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (!f.failed)
                    DrawPieces(f, t);
            }

            /// <summary>Her solid beams, the notes and the pieces hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = beamHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                int solid = solidVolley(now);

                if (solid >= 0 && known[solid])
                {
                    float length = beamLength(f);

                    for (int i = 0; i < beams; i++)
                    {
                        Vector2 a = starts[solid * beams + i];

                        if (distanceToSegment(player, a, a + dirs[solid * beams + i] * length) <= reach)
                            return true;
                    }
                }

                if (notesHurt(f, now, player))
                    return true;

                // (the objects' pieces are made in time order: only those made in the last while can still fly)
                var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bullets);
                int lo = 0, hi = sliderPieces.Count;

                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;

                    if (all[sliderPieces[mid]].T0 < now - 15000)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                for (int i = lo; i < sliderPieces.Count; i++)
                {
                    ref readonly var piece = ref all[sliderPieces[i]];

                    if (now < piece.T0 || now >= piece.Until)
                        continue;

                    if (Vector2.Distance(stageBulletPosition(piece, now), player) <= piece.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            /// <summary>Is the player within a note? (Only the notes of the gap the player is in, and only those near them as far from her middle.)</summary>
            private bool notesHurt(BulletField f, double now, Vector2 player)
            {
                float circle = DanmakusuBalance.MAGIC_CIRCLE_RADIUS, speed = noteSpeed(f);
                float margin = DanmakusuBalance.PLAYER_HITBOX_RADIUS + beamHalf(f) * 2f;
                double flies = flight(f);

                for (int lane = 0; lane < gaps; lane++)
                {
                    var list = lanes[lane];

                    for (int n = firstLeft(lane, now - flies); n < list.Length && list[n].Start <= now; n++)
                    {
                        int k = laneVolley[lane][n];

                        if (k < 0 || !known[k])
                            continue;

                        // (in the gap: on the inner side of both its beams)
                        Vector2 left = starts[k * beams + lane], right = starts[k * beams + lane + 1];
                        Vector2 dl = dirs[k * beams + lane], dr = dirs[k * beams + lane + 1];

                        if (Vector2.Dot(player - left, new Vector2(-dl.Y, dl.X)) < -margin || Vector2.Dot(player - right, new Vector2(-dr.Y, dr.X)) > margin)
                            continue;

                        // (and as far from her middle as the note is)
                        float distance = Vector2.Distance(player, from[k]);
                        float front = circle + speed * (float)(now - list[n].Start), back = Math.Max(circle, circle + speed * (float)(now - list[n].End));

                        if (distance > front + margin || distance < back * 0.9f - margin - f.circleRadius)
                            continue;

                        if (!noteShape(f, k, lane, list[n], now, hurtPoints, hurtWidths))
                            continue;

                        for (int p = 0; p + 1 < hurtPoints.Count; p++)
                        {
                            Vector2 a = hurtPoints[p], b = hurtPoints[p + 1], ab = b - a;
                            float u = ab.LengthSquared > 1e-6f ? Math.Clamp(Vector2.Dot(player - a, ab) / ab.LengthSquared, 0f, 1f) : 0f;
                            float width = hurtWidths[p] + (hurtWidths[p + 1] - hurtWidths[p]) * u;

                            if (Vector2.Distance(player, a + ab * u) <= width * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
