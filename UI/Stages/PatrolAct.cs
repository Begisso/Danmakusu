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
        // Momiji Inubashiri (2026-11-21): no pauses. For MOMIJI_PILE_BEATS every bullet (of a ring) that crosses the top or the bottom of the screen on its way out is caught there and laid into a pile
        // along that edge: into the free place nearest where it crossed (the places MOMIJI_SPACING of a plain bullet's width apart, so the pile is one connected band), sliding there over
        // MOMIJI_SETTLE_BEATS; it takes a place up to MOMIJI_LAYER_COST places aside before it lies on top of the others, so the pile stays even. When the time is up the top pile moves down and the bottom one
        // up, over MOMIJI_CLOSE_BEATS, until their innermost rows meet in the middle - the crash - and every bullet of them flies off, away from the middle of the screen, drawing a narrow beam behind it,
        // MOMIJI_TRACE_BEATS long, which hurts like a slider body. For MOMIJI_REST_BEATS after the crash nothing is caught; then again.
        // What it caught is laid out as it is caught; where the piles go is worked out when the time to catch is up (each bullet a stage bullet of legs: a closed form of time).
        // ======================================================================================================================================================

        private sealed class PatrolAct : StageAct
        {
            private sealed class Caught
            {
                public bool Top;
                public int Layer;
                public Vector2 Slot;
                public StoredBullet Stored;
                public BulletEmitter Emitter = null!;
                public int Bullet;
                public Vector2 Face;
            }

            private sealed class Pile
            {
                public double From, To, Crash;
                public readonly List<bool[]> Top = new List<bool[]>(), Bottom = new List<bool[]>();
                public readonly List<Caught> Caught = new List<Caught>();
                public bool Done;
            }

            private readonly double beat;
            private readonly Pile[] piles;

            // (the beams behind what flew off at a crash: from where, how fast, from when)
            private readonly List<(Vector2 From, Vector2 Velocity, double At)> traces = new List<(Vector2, Vector2, double)>();

            private readonly MomentShapes shapes = new MomentShapes(); // (the beams at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> line = new List<Vector2>();

            private const int most_layers = 8;

            public PatrolAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                double pile = beat * DanmakusuBalance.MOMIJI_PILE_BEATS, close = beat * DanmakusuBalance.MOMIJI_CLOSE_BEATS;
                double every = pile + close + beat * DanmakusuBalance.MOMIJI_REST_BEATS;
                var list = new List<Pile>();

                // (a pile is gathered only if it can crash in her section)
                for (double s = period.ShootStart; s + pile + close <= period.End + 1 && list.Count < 10000; s += every)
                    list.Add(new Pile { From = s, To = s + pile, Crash = s + pile + close });

                // (a section too short for one: one as long as it is, two thirds gathering and a third closing)
                if (list.Count == 0 && period.End > period.ShootStart + beat)
                {
                    double span = period.End - period.ShootStart;
                    list.Add(new Pile { From = period.ShootStart, To = period.ShootStart + span * 2 / 3, Crash = period.End });
                }

                piles = list.ToArray();
                Start = period.ShootStart;
                End = piles.Length > 0 ? piles[^1].Crash + beat * DanmakusuBalance.MOMIJI_TRACE_BEATS : period.End;

                if (piles.Length > 0)
                {
                    AbsorbFrom = piles[0].From;
                    AbsorbTo = piles[^1].To;
                }
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                traces.Clear();
                shapes.Forget();

                foreach (var pile in piles)
                {
                    pile.Top.Clear();
                    pile.Bottom.Clear();
                    pile.Caught.Clear();
                    pile.Done = false;
                }
            }

            public override bool Absorbs(double t) => pileAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var pile in piles)
                {
                    if (pile.From <= to && pile.To >= from)
                        return true;
                }

                return false;
            }

            /// <summary>The pile that catches at <paramref name="t"/>, or -1.</summary>
            private int pileAt(double t)
            {
                for (int k = 0; k < piles.Length; k++)
                {
                    if (t >= piles[k].From && t < piles[k].To)
                        return k;
                }

                return -1;
            }

            // ---- the places of a pile ----

            private float place(BulletField f) => f.stageBaseRadius(Period);

            private float spacing(BulletField f) => 2f * place(f) * DanmakusuBalance.MOMIJI_SPACING;

            private float rowHeight(BulletField f) => spacing(f) * 0.87f; // (every row lies in the dips of the one below it)

            private int placesIn(BulletField f, int layer) => Math.Max((int)((f.stageMax.X - f.stageMin.X - 2f * place(f)) / spacing(f)) + 1 - layer % 2, 1);

            private Vector2 slotOf(BulletField f, bool top, int layer, int index)
            {
                float x = f.stageMin.X + place(f) + (index + (layer % 2) * 0.5f) * spacing(f);
                float y = top ? f.stageMin.Y + place(f) + layer * rowHeight(f) : f.stageMax.Y - place(f) - layer * rowHeight(f);
                return new Vector2(x, y);
            }

            /// <summary>The free place for a bullet that crossed at <paramref name="x"/>: the nearest in the lowest row, unless one up to MOMIJI_LAYER_COST places nearer is free in a row above it.</summary>
            private bool freePlace(BulletField f, List<bool[]> layers, float x, out int bestLayer, out int bestIndex)
            {
                bestLayer = bestIndex = -1;
                float bestCost = float.PositiveInfinity;
                float gap = spacing(f);

                for (int layer = 0; layer < most_layers; layer++)
                {
                    if (layer * DanmakusuBalance.MOMIJI_LAYER_COST >= bestCost)
                        break;

                    if (layers.Count <= layer)
                        layers.Add(new bool[placesIn(f, layer)]);

                    var row = layers[layer];
                    float at = (x - f.stageMin.X - place(f)) / gap - (layer % 2) * 0.5f;
                    int near = Math.Clamp((int)MathF.Round(at), 0, row.Length - 1);

                    // (outwards from the nearest, both ways at once)
                    for (int step = 0; step < row.Length; step++)
                    {
                        float cost = layer * DanmakusuBalance.MOMIJI_LAYER_COST + step;

                        if (cost >= bestCost)
                            break;

                        int left = near - step, right = near + step;
                        int found = left >= 0 && !row[left] ? left : right < row.Length && !row[right] ? right : -1;

                        if (found < 0)
                            continue;

                        bestCost = cost;
                        bestLayer = layer;
                        bestIndex = found;
                        break;
                    }
                }

                return bestLayer >= 0;
            }

            // ---- catching ----

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                int k = pileAt(t);

                if (k < 0)
                    return false;

                float topEdge = f.stageMin.Y, bottomEdge = f.stageMax.Y;
                bool top = from.Y >= topEdge && to.Y < topEdge;
                bool bottom = from.Y <= bottomEdge && to.Y > bottomEdge;

                if (!top && !bottom)
                    return false;

                float edge = top ? topEdge : bottomEdge;
                float share = Math.Abs(to.Y - from.Y) > 1e-4f ? (edge - from.Y) / (to.Y - from.Y) : 1f;
                float x = Math.Clamp(from.X + (to.X - from.X) * share, f.stageMin.X, f.stageMax.X);
                var pile = piles[k];

                if (!freePlace(f, top ? pile.Top : pile.Bottom, x, out int layer, out int index))
                    return false; // (the pile is full: it flies on out)

                Vector2 crossed = new Vector2(x, edge);
                Vector2 slot = slotOf(f, top, layer, index);
                Vector2 way = to - from;
                var stored = f.makeStored(emitter, t);
                double settle = beat * DanmakusuBalance.MOMIJI_SETTLE_BEATS;

                // (it slides from where it crossed into its place, and lies there until the pile moves)
                var lying = f.makeStageBullet(stored, emitter, bullet, t, crossed, Vector2.Zero);
                lying.Legs = new[]
                {
                    new StageLeg { T = t, P = crossed, V = (slot - crossed) / (float)settle },
                    new StageLeg { T = t + settle, P = slot, V = Vector2.Zero },
                };
                lying.Face = way.LengthSquared > 1e-8f ? Vector2.Normalize(way) : Vector2.UnitX;
                lying.Untraced = true;
                lying.Until = pile.To;

                int before = Bullets.Count;
                AddBullet(lying);

                if (Bullets.Count == before)
                    return false; // (too many: it flies on out)

                (top ? pile.Top : pile.Bottom)[layer][index] = true;
                pile.Caught.Add(new Caught { Top = top, Layer = layer, Slot = slot, Stored = stored, Emitter = emitter, Bullet = bullet, Face = lying.Face });
                return true;
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (the beams move on)

                if (f.failed)
                    return;

                foreach (var pile in piles)
                {
                    if (pile.Done || now < pile.To)
                        continue;

                    pile.Done = true;
                    crash(f, pile);
                }
            }

            /// <summary>The time to catch is up: the top pile moves down and the bottom one up until their innermost rows meet in the middle, and then all of it flies off away from the middle.</summary>
            private void crash(BulletField f, Pile pile)
            {
                if (pile.Caught.Count == 0)
                    return;

                int innerTop = -1, innerBottom = -1;

                foreach (var caught in pile.Caught)
                {
                    if (caught.Top)
                        innerTop = Math.Max(innerTop, caught.Layer);
                    else
                        innerBottom = Math.Max(innerBottom, caught.Layer);
                }

                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                float r = place(f);

                // (the innermost rows end touching each other on the middle line; a pile without the other one comes to the middle line all the same)
                float topMove = innerTop >= 0 ? middle.Y - r - slotOf(f, true, innerTop, 0).Y : 0f;
                float bottomMove = innerBottom >= 0 ? slotOf(f, false, innerBottom, 0).Y - (middle.Y + r) : 0f;
                double close = Math.Max(pile.Crash - pile.To, 1);
                float plain = stageBulletSpeed(Period);

                foreach (var caught in pile.Caught)
                {
                    float move = caught.Top ? Math.Max(topMove, 0f) : -Math.Max(bottomMove, 0f);
                    Vector2 crashed = caught.Slot + new Vector2(0f, move);
                    Vector2 away = crashed - middle;

                    if (away.LengthSquared < 1f)
                        away = new Vector2(0f, caught.Top ? -1f : 1f);

                    Vector2 velocity = Vector2.Normalize(away) * Math.Max(caught.Stored.Speed, plain) * DanmakusuBalance.MOMIJI_BURST_SPEED;

                    var going = f.makeStageBullet(caught.Stored, caught.Emitter, caught.Bullet, pile.To, caught.Slot, Vector2.Zero);
                    going.Legs = new[]
                    {
                        new StageLeg { T = pile.To, P = caught.Slot, V = new Vector2(0f, move / (float)close) },
                        new StageLeg { T = pile.Crash, P = crashed, V = velocity },
                    };
                    going.Face = caught.Face;
                    going.Untraced = true;
                    going.Until = pile.Crash + f.stageLife(velocity);
                    AddBullet(going);

                    traces.Add((crashed, velocity, pile.Crash));
                }
            }

            // ---- the beams behind what flew off ----

            private float traceWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.MOMIJI_TRACE_WIDTH;

            private double traceMs => beat * DanmakusuBalance.MOMIJI_TRACE_BEATS;

            /// <summary>The beam behind trace <paramref name="i"/> at <paramref name="t"/>: from its end to the bullet; false when there is none (not yet, or all of it gone off the screen).</summary>
            private bool traceAt(BulletField f, int i, double t, out Vector2 tail, out Vector2 head)
            {
                var (from, velocity, at) = traces[i];
                tail = head = from;

                if (t <= at)
                    return false;

                head = from + velocity * (float)(t - at);
                tail = from + velocity * (float)Math.Max(t - at - traceMs, 0);

                if (!f.hasScreenBounds)
                    return true;

                // (its end has left the screen: all of it has)
                float margin = traceWidth(f) * 2f;
                return tail.X >= f.screenMin.X - margin && tail.X <= f.screenMax.X + margin && tail.Y >= f.screenMin.Y - margin && tail.Y <= f.screenMax.Y + margin;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || traces.Count == 0 || f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 own = f.palette[style % f.palette.Length];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float width = traceWidth(f);

                for (int i = 0; i < traces.Count; i++)
                {
                    if (!traceAt(f, i, t, out Vector2 tail, out Vector2 head) || (head - tail).LengthSquared < 0.25f)
                        continue;

                    line.Clear();
                    line.Add(tail);
                    line.Add(head);
                    f.addPlainBeam(line, width, own, border, ref beams, ref pointTotal);
                }
            }

            /// <summary>The beams behind what flew off hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || traces.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = traceWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (asked about the same moment again - the autoplay, for many places: the beams of that moment are worked out once, and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    for (int i = 0; i < traces.Count; i++)
                    {
                        if (!traceAt(f, i, now, out Vector2 tail, out Vector2 head))
                            continue;

                        fresh.Point(tail, reach);
                        fresh.Point(head, reach);
                        fresh.EndRun();
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                for (int i = 0; i < traces.Count; i++)
                {
                    if (traceAt(f, i, now, out Vector2 tail, out Vector2 head) && distanceToSegment(player, tail, head) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
