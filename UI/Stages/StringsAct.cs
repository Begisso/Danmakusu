using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Benben Tsukumo: all through her section every second bullet (of any ring; by the ring's time and the bullet's number) that crosses a beam - any beam on the screen: the sliders' beams, the copies of them
        // above and below that her section gives them (the planner's, see-through and harmless for their first four beats), her own three parallel beams, the other bosses' - becomes a note: an eighth note
        // (the user's picture) twice its size, standing upright, in its colour. A note flies on the way the bullet went, at half its speed at first; its speed swells to twice the bullet's and falls back to
        // half every two beats, as a cosine. After the first swell, when it is slow again, it turns towards the player, once, and flies on that way. Whether a bullet crosses a beam is worked out against the
        // beams as they are this frame (worked out once, onto a grid of squares, so every bullet is only compared with the beams near it).
        // ======================================================================================================================================================

        private sealed class StringsAct : StageAct
        {
            private const float cell = 64f;

            private readonly double beat;
            private double frameNow = double.NaN;
            private bool built;

            // (the beams of this frame: their pieces, and a grid of squares over the screen, each listing the pieces that pass through it)
            private readonly List<(Vector2 A, Vector2 B)> pieces = new List<(Vector2, Vector2)>();
            private List<int>[] cells = Array.Empty<List<int>>();
            private Vector2 gridMin;
            private int columns, rows;
            private readonly Vector2[] points = new Vector2[8192];

            // (notes that have not turned towards the player yet: which, and when)
            private readonly List<(int Index, double Turn)> pending = new List<(int, double)>();

            public StringsAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                pending.Clear();
                built = false;
            }

            public override void Update(BulletField f, double now)
            {
                frameNow = now;
                built = false; // (the beams have moved on)

                if (f.failed)
                    return;

                // a note whose first swell is over turns towards the player, once
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var (index, turn) = pending[i];

                    if (turn > now)
                        continue;

                    pending[i] = pending[^1];
                    pending.RemoveAt(pending.Count - 1);

                    if (index >= Bullets.Count)
                        continue;

                    var note = Bullets[index];

                    if (turn > note.Until)
                        continue;

                    Vector2 aim = f.characterPosition - stageBulletPosition(note, turn);

                    if (aim.LengthSquared < 1e-4f)
                        continue;

                    note.Switch = turn;
                    note.V2 = Vector2.Normalize(aim);
                    Bullets[index] = note;
                }
            }

            /// <summary>The pieces of every beam on the screen as they are now, onto the grid.</summary>
            private void build(BulletField f)
            {
                built = true;
                pieces.Clear();

                double now = frameNow;
                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                gridMin = low - new Vector2(cell);
                columns = Math.Clamp((int)((high.X - low.X) / cell) + 3, 1, 64);
                rows = Math.Clamp((int)((high.Y - low.Y) / cell) + 3, 1, 64);

                if (cells.Length < columns * rows)
                {
                    Array.Resize(ref cells, columns * rows);

                    for (int i = 0; i < cells.Length; i++)
                        cells[i] ??= new List<int>();
                }

                for (int i = 0; i < columns * rows; i++)
                    cells[i].Clear();

                if (double.IsNaN(now) || f.lasers.Length == 0)
                    return;

                int lo = f.laserLowerBound(now - f.maxLaserSpan), hi = f.laserLowerBound(now + 0.001);

                for (int i = lo; i < hi; i++)
                {
                    var laser = f.lasers[i];

                    if (laser.Finished || now > laser.EndTime + laser.Lifetime)
                        continue;

                    for (int beam = 0; beam < laser.BeamCount; beam++)
                    {
                        if (laser.BeamFinished[beam])
                            continue;

                        int n = laser.GetPoints(now, points, beam);

                        for (int p = 0; p + 1 < n; p++)
                        {
                            Vector2 a = points[p], b = points[p + 1];

                            if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(b.X) || !float.IsFinite(b.Y))
                                continue;

                            add(a, b);
                        }
                    }
                }
            }

            private int column(float x) => Math.Clamp((int)((x - gridMin.X) / cell), 0, columns - 1);
            private int row(float y) => Math.Clamp((int)((y - gridMin.Y) / cell), 0, rows - 1);

            private void add(Vector2 a, Vector2 b)
            {
                Vector2 min = Vector2.ComponentMin(a, b), max = Vector2.ComponentMax(a, b);

                // (wholly off the grid: no bullet that can be seen crosses it)
                if (max.X < gridMin.X || max.Y < gridMin.Y || min.X > gridMin.X + columns * cell || min.Y > gridMin.Y + rows * cell)
                    return;

                int index = pieces.Count;
                pieces.Add((a, b));

                for (int y = row(min.Y); y <= row(max.Y); y++)
                {
                    for (int x = column(min.X); x <= column(max.X); x++)
                        cells[y * columns + x].Add(index);
                }
            }

            /// <summary>Does the way from <paramref name="from"/> to <paramref name="to"/> cross a beam (the two ends on the two sides of one of its pieces)?</summary>
            private bool crosses(Vector2 from, Vector2 to)
            {
                if (pieces.Count == 0)
                    return false;

                Vector2 min = Vector2.ComponentMin(from, to), max = Vector2.ComponentMax(from, to);

                for (int y = row(min.Y); y <= row(max.Y); y++)
                {
                    for (int x = column(min.X); x <= column(max.X); x++)
                    {
                        foreach (int index in cells[y * columns + x])
                        {
                            var (a, b) = pieces[index];

                            if (properlyCross(from, to, a, b))
                                return true;
                        }
                    }
                }

                return false;
            }

            private static float side(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

            /// <summary>Do the two pieces cross, each one's ends strictly on the two sides of the other (a bullet fired from on a beam does not cross it as it leaves)?</summary>
            private static bool properlyCross(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
                => side(a, b, p) * side(a, b, q) < 0f && side(p, q, a) * side(p, q, b) < 0f;

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                // (every second bullet: by the ring's time and the bullet's number, the same in a replay)
                if ((((long)Math.Floor(emitter.Time) + bullet) & 1) != 0)
                    return false;

                if (!built)
                    build(f);

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f || !crosses(from, to))
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                var note = f.makeStageBullet(stored, emitter, bullet, t, to, Vector2.Normalize(delta) * speed);

                note.Shape = BulletShape.Note;
                note.Radius *= DanmakusuBalance.BENBEN_NOTE_SIZE;
                note.Hit *= DanmakusuBalance.BENBEN_NOTE_SIZE;
                note.Blades = 0;
                note.KeepFace = true; // (upright, whichever way it flies)
                note.Face = new Vector2(0f, -1f);
                note.PulseMs = beat * DanmakusuBalance.BENBEN_NOTE_PULSE_BEATS;
                note.PulseLow = DanmakusuBalance.BENBEN_NOTE_PULSE_LOW;
                note.PulseHigh = DanmakusuBalance.BENBEN_NOTE_PULSE_HIGH;
                note.Until = t + f.stageLife(note.V) * 1.5; // (it turns once: longer on its way)

                int index = Bullets.Count;
                AddBullet(note);

                if (Bullets.Count == index)
                    return false;

                pending.Add((index, t + note.PulseMs));
                return true;
            }
        }
    }
}
