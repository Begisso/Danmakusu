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
        // Luna Child: all the time her section lasts (attacks, pauses and a spinner alike) a ring of 10 arc beams flies out from where she stands every two beats: the arcs lie on a circle with a wide gap between every two of them
        // (an arc is 15 % of its share of the circle), and every ring is turned a little further than the one before. The arcs are beams (the plain beams of Sekibanki's heads, in her colour) and hurt like a slider body.
        // In her attacks everything is smooth (BulletField.lunaLevel: soft, glowing copies of every bullet, glow on every beam) and every hitsound is muted (DanmakusuPlayfield asks HitsoundsMuted); the hitboxes do not change.
        // The outline of every slider that starts in her attacks (while its body fades out, as with Hidden) turns into a beam that grows outwards from it: everything that is as far from the slider's path as the outline is,
        // further and further out at her rings' speed, until it has left the screen, cut into 20 pieces with gaps between them (by its length, so they grow with it). It hurts like a slider body. (Where it is follows from the distance to the path, so nothing of it is kept from frame to frame.)
        // Saki Kurokoma's rings of beams are the same rings with fewer, longer arcs, and nothing of the rest (Smooth is false).
        // ======================================================================================================================================================

        private sealed class LunaAct : StageAct
        {
            private readonly double[] marks;
            private readonly Vector2[] centres;
            private readonly bool[] known;
            private readonly (double Start, double End)[] attacks;
            private readonly List<Vector2> arc = new List<Vector2>();
            private readonly int arcs;
            private readonly float arcShare;

            /// <summary>The outline of a slider that started in her attacks, growing out from its path.</summary>
            private sealed class Outline
            {
                public double Time; // (when the slider starts: the outline leaves it then)
                public Vector2[] Path = []; // (the slider's path, a point every LUNA_OUTLINE_STEP px or so)
                public float Radius; // (the slider's: where the outline starts)
                public float Reach = -1f; // (past this distance from the path it has left the screen; worked out when it is first looked at)
            }

            private readonly List<Outline> outlines = new List<Outline>();
            private readonly List<Vector2> contour = new List<Vector2>();
            private readonly List<bool> kept = new List<bool>();
            private readonly List<bool> drawn = new List<bool>();
            private readonly List<float> along = new List<float>(); // (how far round the outline every place of it is, px, counting only what is the outline)
            private float outlineLength; // (all the way round)

            /// <summary>Everything is smooth in the attacks, and the hitsounds are muted (Luna Child herself).</summary>
            public readonly bool Smooth;

            public LunaAct(BossPeriod period, double[] marks, int arcs = DanmakusuBalance.LUNA_ARCS, float arcShare = DanmakusuBalance.LUNA_ARC_SHARE, bool smooth = true)
            {
                Period = period;
                this.marks = marks;
                this.arcs = arcs;
                this.arcShare = arcShare;
                Smooth = smooth;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                Start = marks[0];
                End = period.End;

                var list = new List<(double, double)>();

                foreach (var attack in BossOps.Attacks(period))
                {
                    if (attack.End > attack.Start)
                        list.Add(attack);
                }

                attacks = list.ToArray();
            }

            /// <summary>Is <paramref name="t"/> in one of her attacks (her hitsounds are muted)?</summary>
            public bool InAttack(double t)
            {
                if (!Smooth)
                    return false;

                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            private float smoothLevel(double t)
            {
                float best = 0f;

                foreach (var (start, end) in attacks)
                    best = Math.Max(best, Math.Min(smoothStep((t - start) / DanmakusuBalance.LUNA_FADE_MS), 1f - smoothStep((t - end) / DanmakusuBalance.LUNA_FADE_MS)));

                return best;
            }

            private static float speed(BossPeriod period) => stageBulletSpeed(period) * DanmakusuBalance.LUNA_RING_SPEED;

            private double life(BulletField f)
            {
                float diagonal = f.hasScreenBounds ? Vector2.Distance(f.screenMin, f.screenMax) : 1500f;
                return (diagonal + DanmakusuBalance.MAGIC_CIRCLE_RADIUS) / Math.Max(speed(Period), 1e-4f);
            }

            private float radiusOf(double t, int ring) => DanmakusuBalance.MAGIC_CIRCLE_RADIUS + speed(Period) * (float)(t - marks[ring]);

            private static float phaseOf(int ring) => ring * 0.17f;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>The first ring that can still be on the screen at <paramref name="t"/>.</summary>
            private int firstAlive(double t, double span)
            {
                int low = 0, high = marks.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (marks[middle] < t - span)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                float level = Smooth ? smoothLevel(now) : 0f;

                if (level > f.lunaLevel)
                {
                    f.lunaLevel = level;
                    f.lunaCentre = f.bossPositionOf(Period);
                }

                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = f.bossPositionOf(Period);
                    }
                }
            }

            public override void Reset(BulletField f, double time) => Array.Clear(known, 0, known.Length);

            public override void Init(BulletField f)
            {
                base.Init(f);

                if (!Smooth)
                    return;

                foreach (var zone in f.sliderZones)
                {
                    if (!InAttack(zone.StartTime) || zone.Points.Length == 0)
                        continue;

                    // (fewer points than the zone has: it is the distance to the path that matters, and every point of the outline is checked against every piece of it)
                    var path = new List<Vector2> { zone.Points[0] };

                    for (int i = 1; i < zone.Points.Length; i++)
                    {
                        if (Vector2.Distance(zone.Points[i], path[^1]) >= DanmakusuBalance.LUNA_OUTLINE_STEP || (i == zone.Points.Length - 1 && Vector2.Distance(zone.Points[i], path[^1]) > 1f))
                            path.Add(zone.Points[i]);
                    }

                    outlines.Add(new Outline { Time = zone.StartTime, Path = path.ToArray(), Radius = zone.Radius });
                }
            }

            private float outlineSpeed => stageBulletSpeed(Period) * DanmakusuBalance.LUNA_OUTLINE_SPEED;

            /// <summary>How far from the path of <paramref name="outline"/> its beam is at <paramref name="t"/>.</summary>
            private float outlineDistance(Outline outline, double t) => outline.Radius + outlineSpeed * (float)(t - outline.Time);

            private static float distanceToPath(Vector2 point, Vector2[] path)
            {
                if (path.Length == 1)
                    return Vector2.Distance(point, path[0]);

                float best = float.MaxValue;

                for (int i = 1; i < path.Length; i++)
                    best = Math.Min(best, distanceToSegment(point, path[i - 1], path[i]));

                return best;
            }

            /// <summary>How far from its path the outline can be and still be seen: the farthest any place on the screen is from the path (a grid of places), and its width.</summary>
            private float reachOf(BulletField f, Outline outline)
            {
                if (outline.Reach >= 0f)
                    return outline.Reach;

                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                float farthest = 0f;

                for (int y = 0; y <= 8; y++)
                {
                    for (int x = 0; x <= 8; x++)
                        farthest = Math.Max(farthest, distanceToPath(new Vector2(low.X + (high.X - low.X) * x / 8f, low.Y + (high.Y - low.Y) * y / 8f), outline.Path));
                }

                float reach = farthest + halfWidth(f) * 2f + (high - low).Length / 16f;

                // (kept once the screen is known)
                if (f.hasScreenBounds)
                    outline.Reach = reach;

                return reach;
            }

            /// <summary>
            /// The places at <paramref name="distance"/> from the path all round it, in order, into <see cref="contour"/>: along one side, round the end, back along the other side and round the start (round every bend as well, a
            /// point every few px). What lies closer to another part of the path than that is not the outline (inside a bend): <see cref="kept"/> says which are.
            /// </summary>
            private void outlineAt(Vector2[] path, float distance)
            {
                contour.Clear();
                kept.Clear();

                // (round the ends and bends a point every so many radians, whatever the distance: with a point every few px their number grew as the outline grew, every new one moved the length round it, and the
                // pieces, spread over that length, jumped along it - Luna's beams flickered and jittered)
                float turnStep = DanmakusuBalance.LUNA_OUTLINE_TURN;

                void round(Vector2 middle, float from, float by)
                {
                    int steps = Math.Max((int)MathF.Ceiling(MathF.Abs(by) / turnStep), 1);

                    for (int s = 1; s <= steps; s++)
                    {
                        float a = from + by * s / steps;
                        contour.Add(middle + new Vector2(MathF.Cos(a), MathF.Sin(a)) * distance);
                    }
                }

                if (path.Length == 1)
                {
                    contour.Add(path[0] + new Vector2(distance, 0f));
                    round(path[0], 0f, MathF.Tau);
                }
                else
                {
                    int n = path.Length;
                    var normals = new float[n - 1]; // (the way to the left of every piece, as an angle)

                    for (int i = 0; i < n - 1; i++)
                    {
                        Vector2 along = path[i + 1] - path[i];
                        normals[i] = MathF.Atan2(along.X, -along.Y);
                    }

                    // (along the left side, round every bend; round the end; back along the right side; round the start, which comes back to where it began)
                    contour.Add(path[0] + new Vector2(MathF.Cos(normals[0]), MathF.Sin(normals[0])) * distance);

                    for (int i = 0; i < n - 1; i++)
                    {
                        contour.Add(path[i + 1] + new Vector2(MathF.Cos(normals[i]), MathF.Sin(normals[i])) * distance);

                        if (i + 1 < n - 1)
                            round(path[i + 1], normals[i], MathF.IEEERemainder(normals[i + 1] - normals[i], MathF.Tau));
                    }

                    round(path[n - 1], normals[n - 2], -MathF.PI);

                    for (int i = n - 2; i >= 0; i--)
                    {
                        float right = normals[i] + MathF.PI;
                        contour.Add(path[i] + new Vector2(MathF.Cos(right), MathF.Sin(right)) * distance);

                        if (i > 0)
                            round(path[i], right, MathF.IEEERemainder(normals[i - 1] - normals[i], MathF.Tau));
                    }

                    round(path[0], normals[0] + MathF.PI, -MathF.PI);
                }

                float tolerance = 0.75f + distance * 0.002f;

                foreach (var point in contour)
                    kept.Add(distanceToPath(point, path) >= distance - tolerance);

                // (how far round it every place is: the pieces and gaps are spread over its length, so they grow with it as her arcs grow with their rings. Measured all round, also through what is left out inside
                // a bend: what is left out changes as the outline grows, and measured across it the length jumped whenever a place came or went, and every piece with it)
                along.Clear();
                float total = 0f;
                bool anyKept = false;

                for (int i = 0; i < contour.Count; i++)
                {
                    if (i > 0)
                        total += Vector2.Distance(contour[i - 1], contour[i]);

                    anyKept |= kept[i];
                    along.Add(total);
                }

                outlineLength = anyKept && contour.Count > 1 ? total + Vector2.Distance(contour[^1], contour[0]) : 0f;
            }

            /// <summary>Adds the arcs at time <paramref name="t"/> to the beams that are drawn.</summary>
            public void AddArcs(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || t < Start)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = halfWidth(f);
                double span = life(f);
                float step = MathF.Tau / arcs;
                float length = step * arcShare;
                float gap = (step - length) * 0.5f;

                for (int k = firstAlive(t, span); k < marks.Length && marks[k] <= t; k++)
                {
                    if (!known[k])
                        continue;

                    float r = radiusOf(t, k);

                    for (int i = 0; i < arcs; i++)
                    {
                        float from = phaseOf(k) + i * step + gap;
                        int n = DanmakusuBalance.LUNA_ARC_POINTS; // (as many points all along, however big the ring has grown: their number changing as it grew moved the corners of the arc, a jitter)
                        arc.Clear();
                        bool visible = false;

                        for (int p = 0; p <= n; p++)
                        {
                            float a = from + length * p / n;
                            Vector2 point = centres[k] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                            arc.Add(point);

                            if (!f.hasScreenBounds || (point.X >= f.screenMin.X - width && point.X <= f.screenMax.X + width && point.Y >= f.screenMin.Y - width && point.Y <= f.screenMax.Y + width))
                                visible = true;
                        }

                        if (visible)
                            f.addPlainBeam(arc, width, colour, border, ref beams, ref pointTotal);
                    }
                }

                // the outlines of the sliders, as far out as they have grown: what is on the screen of them, in pieces where a part is not the outline or is off the screen
                Vector2 low = (f.hasScreenBounds ? f.screenMin : f.stageMin) - new Vector2(width), high = (f.hasScreenBounds ? f.screenMax : f.stageMax) + new Vector2(width);

                foreach (var outline in outlines)
                {
                    if (t < outline.Time)
                        break; // (they are in the order the sliders start)

                    float distance = outlineDistance(outline, t);

                    if (distance > reachOf(f, outline))
                        continue;

                    outlineAt(outline.Path, distance);

                    if (outlineLength <= 0f)
                        continue;

                    int n = contour.Count;
                    drawn.Clear();

                    for (int i = 0; i < n; i++)
                        drawn.Add(kept[i] && contour[i].X >= low.X && contour[i].X <= high.X && contour[i].Y >= low.Y && contour[i].Y <= high.Y);

                    float pieces = DanmakusuBalance.LUNA_OUTLINE_PIECES / outlineLength; // (pieces per px round it)

                    // (from a place that is not drawn or lies in a gap, all the way round, so that no piece is cut in two where the loop closes)
                    int start = 0;

                    for (int i = 0; i < n; i++)
                    {
                        if (!drawn[i] || !inPiece(along[i] * pieces))
                        {
                            start = i;
                            break;
                        }
                    }

                    arc.Clear();

                    if (drawn[start] && inPiece(along[start] * pieces))
                        arc.Add(contour[start]);

                    for (int s = 1; s <= n; s++)
                    {
                        int a = (start + s - 1) % n, b = (start + s) % n;

                        if (!drawn[a] || !drawn[b])
                        {
                            flush(f, width, colour, border, ref beams, ref pointTotal);

                            if (drawn[b] && inPiece(along[b] * pieces))
                                arc.Add(contour[b]);

                            continue;
                        }

                        float xa = along[a] * pieces, xb = along[b] * pieces;

                        if (xb < xa)
                            xb += DanmakusuBalance.LUNA_OUTLINE_PIECES; // (past where the loop closes)

                        // (where a piece begins or ends between the two points: exactly there, not at the nearer point)
                        for (int k = (int)MathF.Floor(xa) - 1; k <= (int)MathF.Floor(xb) + 1; k++)
                        {
                            float begins = k + 0.5f - pieceHalf, ends = k + 0.5f + pieceHalf;

                            if (begins > xa && begins < xb)
                            {
                                flush(f, width, colour, border, ref beams, ref pointTotal);
                                arc.Add(Vector2.Lerp(contour[a], contour[b], (begins - xa) / (xb - xa)));
                            }

                            if (ends > xa && ends < xb)
                            {
                                arc.Add(Vector2.Lerp(contour[a], contour[b], (ends - xa) / (xb - xa)));
                                flush(f, width, colour, border, ref beams, ref pointTotal);
                            }
                        }

                        if (inPiece(xb))
                            arc.Add(contour[b]);
                    }

                    flush(f, width, colour, border, ref beams, ref pointTotal);
                }
            }

            /// <summary>The piece of an outline gathered in <see cref="arc"/> is drawn, if it is one (two points at least), and a new one begins.</summary>
            private void flush(BulletField f, float width, Color4 colour, Color4 border, ref int beams, ref int pointTotal)
            {
                if (arc.Count >= 2)
                {
                    f.addPlainBeam(arc, width, colour, border, ref beams, ref pointTotal);
                    ProfileOutlinePieces++;
                }

                arc.Clear();
            }

            /// <summary>Half of the share of a piece of an outline that is beam (the middle of the piece; the rest is the gap).</summary>
            private static float pieceHalf => DanmakusuBalance.LUNA_OUTLINE_SHARE * 0.5f;

            /// <summary>Is the place <paramref name="x"/> round an outline (in pieces) on the beam of its piece, not in a gap?</summary>
            private static bool inPiece(float x, float margin = 0f) => MathF.Abs(x - MathF.Floor(x) - 0.5f) <= pieceHalf + margin;

            /// <summary>Is <paramref name="player"/>, who is as far from the path as the outline is, on a piece of it, not in a gap (with the room the hit has round it)?</summary>
            private bool onPiece(Outline outline, float distance, Vector2 player, float hit)
            {
                outlineAt(outline.Path, distance);

                if (outlineLength <= 0f)
                    return false;

                float best = float.MaxValue, at = 0f;

                for (int i = 1; i < contour.Count; i++)
                {
                    if (!kept[i - 1] || !kept[i])
                        continue;

                    Vector2 a = contour[i - 1], along2 = contour[i] - a;
                    float u = Math.Clamp(Vector2.Dot(player - a, along2) / Math.Max(along2.LengthSquared, 1e-6f), 0f, 1f);
                    float away = Vector2.DistanceSquared(player, a + along2 * u);

                    if (away < best)
                    {
                        best = away;
                        at = along[i - 1] + (along[i] - along[i - 1]) * u;
                    }
                }

                float pieces = DanmakusuBalance.LUNA_OUTLINE_PIECES / outlineLength;
                return best < float.MaxValue && inPiece(at * pieces, hit * pieces);
            }

            /// <summary>The arcs hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                float width = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                double span = life(f);
                float step = MathF.Tau / arcs;
                float length = step * arcShare;
                float gap = (step - length) * 0.5f;

                for (int k = firstAlive(now, span); k < marks.Length && marks[k] <= now; k++)
                {
                    if (!known[k])
                        continue;

                    Vector2 offset = f.characterPosition - centres[k];
                    float distance = offset.Length;

                    if (distance < 1e-3f || MathF.Abs(distance - radiusOf(now, k)) > width)
                        continue;

                    // (the angle across the width of the beam counts too: a beam is as wide as it is)
                    float margin = width / distance;
                    float angle = MathF.Atan2(offset.Y, offset.X) - phaseOf(k);
                    float within = angle - MathF.Floor(angle / step) * step;

                    if (within >= gap - margin && within <= gap + length + margin)
                        return true;

                    if ((within - step >= gap - margin && within - step <= gap + length + margin) || (within + step >= gap - margin && within + step <= gap + length + margin))
                        return true;
                }

                // (the outlines: the player is on one where they are as far from the slider's path as it has grown)
                foreach (var outline in outlines)
                {
                    if (now < outline.Time)
                        break;

                    float distance = outlineDistance(outline, now);

                    if (distance <= reachOf(f, outline) && MathF.Abs(distanceToPath(f.characterPosition, outline.Path) - distance) <= width && onPiece(outline, distance, f.characterPosition, width))
                        return true;
                }

                return false;
            }
        }

        /// <summary>0..1: how smooth everything is drawn right now (Luna Child's attacks).</summary>
        private float lunaLevel;

        private readonly List<LunaAct> lunaActs = new List<LunaAct>();

        /// <summary>This map has Luna Child in it.</summary>
        public bool HasSilence => lunaActs.Exists(act => act.Smooth);

        /// <summary>Are the hitsounds muted at <paramref name="time"/> (Luna Child is attacking)?</summary>
        public bool HitsoundsMuted(double time)
        {
            foreach (var act in lunaActs)
            {
                if (act.InAttack(time))
                    return true;
            }

            return false;
        }

        /// <summary>Where Luna Child stands (the middle of her magic circle).</summary>
        private Vector2 lunaCentre;

        /// <summary>0..1: how blurry things are at <paramref name="p"/> right now: nothing inside her magic circle, more and more the further from her (full at a playfield's width), as strongly as her attack is on.</summary>
        private float lunaBlurAt(Vector2 p)
        {
            float inner = DanmakusuBalance.MAGIC_CIRCLE_RADIUS;
            float u = Math.Clamp((Vector2.Distance(p, lunaCentre) - inner) / Math.Max(Math.Max(stageMax.X - stageMin.X, 1f) - inner, 1f), 0f, 1f);

            return lunaLevel * smoothStep(u);
        }

        /// <summary>
        /// Luna Child's blur: the further a bullet is from her the more it glows and the softer it is drawn (<see cref="BulletInstance.Soft"/>: from the renderer's blurred sheet, so its edge fades into nothing and cannot be seen at all);
        /// it is not see-through, and what is in her magic circle is as it was. The hitbox is the bullet's own. No copies: every bullet on the screen gets it.
        /// </summary>
        private int softenBullets(int count)
        {
            if (lunaLevel <= 0f || count == 0 || failed)
                return count;

            for (int i = 0; i < count; i++)
            {
                float u = lunaBlurAt(instanceBuffer[i].Position);

                if (u <= 0.02f)
                    continue;

                instanceBuffer[i].Soft = Math.Max(instanceBuffer[i].Soft, Math.Min(u * DanmakusuBalance.LUNA_SOFT_RAMP, 1f));
                instanceBuffer[i].Bloom = Math.Max(instanceBuffer[i].Bloom, DanmakusuBalance.LUNA_BLOOM * u);
            }

            return count;
        }
    }
}