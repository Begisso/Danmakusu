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
        // Son Biten (2026-11-24, "arrow warnings for ... Biten spin"): an arrow at either end of every beam of hers that turns round its own middle, square to the beam, pointing the way that end goes round - the
        // sliders' held beams from when they begin until a beat after they start to spin, her thrown ones while they are fired and a beat after. Gold, only to look at. The beams themselves are the planner's
        // (LaserEmitter.Held, LaserEmitter.SelfSpin). Since 2026-11-25 a slider's beam that was on the screen as her section began spins from then on too (LaserEmitter.SpinFrom): its arrows show from then for a beat.
        // ======================================================================================================================================================

        private sealed class StaffAct : StageAct
        {
            private readonly LaserEmitter[] beams;
            private readonly double beat;

            public StaffAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<LaserEmitter>();
                double last = period.ShootStart;

                foreach (var laser in f.lasers)
                {
                    // (and what was on the screen as her section began, spinning from then on: 2026-11-25)
                    bool late = laser.SpinFrom > 0 && laser.LateSpin != 0f && laser.SpinFrom >= period.ShootStart - 1 && laser.SpinFrom < period.End;

                    if (!late && ((laser.SelfSpin == 0f && !(laser.HoldUntil > 0 && laser.HoldSpin != 0f)) || laser.StartTime < period.ShootStart - 1 || laser.StartTime >= period.End))
                        continue;

                    list.Add(laser);
                    last = Math.Max(last, shownUntil(laser));
                }

                beams = list.ToArray();
                Start = period.ShootStart;
                End = last;
            }

            /// <summary>Until when the arrows of a beam show: a beat after a held one starts to spin, a beat after a thrown one is all fired.</summary>
            private double shownUntil(LaserEmitter laser) => (laser.HoldUntil > 0 ? laser.HoldUntil : laser.SpinFrom > 0 ? laser.SpinFrom : laser.EndTime) + beat * DanmakusuBalance.SON_ARROW_AFTER_BEATS;

            /// <summary>From when the arrows of a beam show: as it begins, or as it begins to spin (one that was on the screen already).</summary>
            private static double shownFrom(LaserEmitter laser) => laser.SpinFrom > 0 ? laser.SpinFrom : laser.StartTime;

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End)
                    return;

                float size = f.stageBaseRadius(Period) * DanmakusuBalance.SON_ARROW_SIZE;
                double fade = beat * 0.25;

                foreach (var laser in beams)
                {
                    double until = shownUntil(laser);

                    if (t < shownFrom(laser) || t >= until)
                        continue;

                    float alpha = (float)Math.Min(Math.Clamp((t - shownFrom(laser)) / fade, 0, 1), Math.Clamp((until - t) / (beat * DanmakusuBalance.SON_ARROW_AFTER_BEATS), 0, 1)) * DanmakusuBalance.SON_ARROW_ALPHA;

                    if (alpha <= 0.01f)
                        continue;

                    for (int b = 0; b < laser.BeamCount; b++)
                    {
                        if (!laser.SpinShape(t, b, out Vector2 first, out Vector2 newest, out Vector2 middle, out float way))
                            continue;

                        arrow(f, first, middle, way, size, alpha);
                        arrow(f, newest, middle, way, size, alpha);
                    }
                }
            }

            /// <summary>An arrow just past one end of a beam, square to it, the way that end goes round the middle.</summary>
            private static void arrow(BulletField f, Vector2 end, Vector2 middle, float way, float size, float alpha)
            {
                Vector2 out_ = end - middle;
                float length = out_.Length;

                if (length < 4f)
                    return;

                Vector2 along = out_ / length;
                Vector2 round = new Vector2(-along.Y, along.X) * way;
                Vector2 at = end + along * (size * 0.6f) + round * (size * 0.3f);

                f.stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), MathF.Atan2(round.Y, round.X) * 180f / MathF.PI, alpha, gold);
            }

            private static readonly Color4 gold = new Color4(1f, 0.82f, 0.3f, 1f);
        }
    }
}
