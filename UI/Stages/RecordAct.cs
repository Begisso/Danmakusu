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
        // Hisami Yomotsu: every four beats of an attack everything within five and a half circle radii of the player is recorded: the bullets fly on, and each leaves a see-through preview where it was, in the colour of her hair (black with a violet sheen), which stays there wherever the
        // player goes. Two beats later every preview turns solid and flies off, as big as the bullet was and the way it was going. A bright ring, clear in the middle, fades in where the recording was made and stays for four beats. Her rings and the beams of the sliders
        // that are fired again four beats later are the planner's.
        // ======================================================================================================================================================

        private sealed class RecordAct : StageAct
        {
            private const double mark_ms = 60; // (how long a recording takes: frames are not as short as a moment)

            private readonly double beat;
            private readonly double[] marks;
            private readonly Vector2[] places;
            private readonly bool[] known;
            private readonly HashSet<(BulletEmitter, int)> recorded = new HashSet<(BulletEmitter, int)>();
            private int lastMark = -1;

            public RecordAct(BossPeriod period, double[] marks)
            {
                Period = period;
                this.marks = marks;
                places = new Vector2[marks.Length];
                known = new bool[marks.Length];
                beat = Math.Max(period.BeatLength, 1);
                Start = marks[0];
                End = marks[^1] + beat * Math.Max(DanmakusuBalance.HISAMI_PREVIEW_BEATS, DanmakusuBalance.HISAMI_RING_BEATS);
                AbsorbFrom = Start;
                AbsorbTo = marks[^1] + mark_ms;
            }

            private int markAt(double t)
            {
                int low = 0, high = marks.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (marks[middle] + mark_ms <= t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low < marks.Length && marks[low] <= t ? low : -1;
            }

            public override bool Absorbs(double t) => markAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double mark in marks)
                {
                    if (mark <= to && mark + mark_ms > from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                recorded.Clear();
                lastMark = -1;
                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    if (!known[k])
                    {
                        known[k] = true;
                        places[k] = f.characterPosition;
                    }
                }
            }

            /// <summary>A bullet near the player at a recording leaves a preview; it flies on itself, so this takes nothing.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                int mark = markAt(t);

                if (f.failed || mark < 0 || !known[mark] || emitter.Shape == BulletShape.Train || emitter.Deflector)
                    return false;

                float reach = f.circleRadius * DanmakusuBalance.HISAMI_REACH_CIRCLES;

                if ((to - places[mark]).LengthSquared > reach * reach)
                    return false;

                if (mark != lastMark)
                {
                    lastMark = mark;
                    recorded.Clear();
                }

                if (!recorded.Add((emitter, bullet)))
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                Vector2 velocity = delta.LengthSquared > 1e-10f ? delta / (float)dt : Vector2.Zero;
                double flies = marks[mark] + beat * DanmakusuBalance.HISAMI_PREVIEW_BEATS;
                float big = emitter.BigScale(bullet);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    Switch = flies,
                    V2 = velocity,
                    Face = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitX,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_HISAMI_HAIR, // (her hair's colour, whatever the bullet's was)
                    TintAmount = 1f,
                    Shade = stored.Shade,
                    GhostUntil = flies,
                    GhostVisible = DanmakusuBalance.HISAMI_PREVIEW_ALPHA,
                    Until = flies + f.stageLife(velocity),
                });

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float reach = f.circleRadius * DanmakusuBalance.HISAMI_REACH_CIRCLES;
                double span = beat * DanmakusuBalance.HISAMI_RING_BEATS;
                var violet = new Color4(0.72f, 0.45f, 0.95f, 1f);

                for (int k = 0; k < marks.Length; k++)
                {
                    double age = t - marks[k];

                    if (!known[k] || age < 0 || age >= span)
                        continue;

                    // (it fades in, stays as it is, and fades out over its last beat; the middle is clear)
                    float level = smoothStep(age / (beat * DanmakusuBalance.HISAMI_RING_FADE_BEATS)) * (1f - smoothStep((age - (span - beat)) / beat));

                    f.stageRipple.AddRing(places[k], reach, Math.Max(reach * 0.035f, 3f), DanmakusuBalance.HISAMI_RING_ALPHA * level, violet);
                }
            }
        }
    }
}
