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
        // Saki Kurokoma: her attacks are like Remilia's (the planner makes the rings denser and turn further as the attack goes on, every fortieth bullet is three times as big; everything glows more and more, the screen
        // shakes as it starts), without the red: the background dims brown and a giant see-through horse's head shows through it. Every bullet on the screen speeds up evenly to 1.2 times its speed over an attack and
        // back over the pause (a window of BossEffects). And they stampede: as with Zanmu's fall, when an attack begins the way of the line that joins her and the player is taken, the same for everything, and every bullet on
        // the screen (also what is fired during the attack) turns that way, at its own speed, once. What falls, homes or has ops of its own keeps its way. Every four beats of an attack (from four beats in) every fourth bullet
        // on the screen shoots two bullets half its size, one to its left and one to its right, as fast as it flies (those do not shoot again). Her own rings of beams are a LunaAct.
        // ======================================================================================================================================================

        private sealed class StampedeAct : StageAct
        {
            private const double mark_ms = 60; // (how long a mark lasts: the bullets are looked at once, frames are not as short as a moment)

            private readonly List<(double Start, double End)> attacks;
            private readonly Vector2[] ways;
            private readonly bool[] known;
            private readonly double[] marks;
            private readonly HashSet<(BulletEmitter, int)> shot = new HashSet<(BulletEmitter, int)>();
            private int lastMark = -1;

            public StampedeAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                ways = new Vector2[attacks.Count];
                known = new bool[attacks.Count];
                Start = period.ShootStart;
                End = period.End + DanmakusuBalance.REMILIA_FADE_OUT_MS;
                AbsorbFrom = Start;
                AbsorbTo = End;

                // (every four beats of an attack, from four beats after it begins)
                double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.SAKI_SIDE_EVERY_BEATS;
                var list = new List<double>();

                foreach (var (start, end) in attacks)
                {
                    for (double mark = start + gap; mark < end; mark += gap)
                        list.Add(mark);
                }

                marks = list.ToArray();
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

            private int attackAt(double t)
            {
                for (int k = 0; k < attacks.Count; k++)
                {
                    if (attacks[k].End > attacks[k].Start && t >= attacks[k].Start && t < attacks[k].End)
                        return k;
                }

                return -1;
            }

            public override bool Absorbs(double t) => attackAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var (start, end) in attacks)
                {
                    if (end > start && start <= to && end > from)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                shot.Clear();
                lastMark = -1;
                ClearBullets();
            }

            /// <summary>At every mark every fourth bullet on the screen shoots two bullets half its size, one to its left and one to its right, as fast as it flies; it flies on itself, so this takes nothing.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                int mark = markAt(t);

                if (f.failed || mark < 0 || emitter.Shape == BulletShape.Train || emitter.Deflector || (Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.SAKI_SIDE_EVERY != 0)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                if (mark != lastMark)
                {
                    lastMark = mark;
                    shot.Clear();
                }

                if (!shot.Add((emitter, bullet)))
                    return false;

                Vector2 velocity = (to - from) / (float)Math.Max(t - ScanPrevious, 1e-3);

                if (velocity.LengthSquared < 1e-8f)
                    return false; // (it stands still: it has no left or right)

                var stored = f.makeStored(emitter, t);
                float size = emitter.BigScale(bullet) * DanmakusuBalance.SAKI_SIDE_SIZE;
                Vector2 side = new Vector2(-velocity.Y, velocity.X);

                for (int s = -1; s <= 1; s += 2)
                {
                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = to,
                        V = side * s,
                        Face = Vector2.Normalize(side * s),
                        Radius = stored.Radius * size,
                        Hit = stored.Hit * size,
                        Style = stored.Style,
                        Shape = stored.Shape,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Until = t + f.stageLife(side),
                    });
                }

                return false;
            }

            public override void Update(BulletField f, double now)
            {
                for (int k = 0; k < attacks.Count; k++)
                {
                    if (known[k] || now < attacks[k].Start)
                        continue;

                    known[k] = true;

                    Vector2 toPlayer = f.characterPosition - f.bossVisualOf(Period);
                    ways[k] = toPlayer.LengthSquared > 1e-4f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                }
            }

            /// <summary>Every bullet on the screen turns the way of this attack, once.</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                int k = attackAt(t);

                if (f.failed || k < 0 || !known[k] || !emitter.CanBeDeflected || emitter.Shape == BulletShape.Train)
                    return false;

                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                double began = emitter.WithoutFrozenTime(f.effects.Effective(emitter, attacks[k].Start));

                if (emitter.Deflections != null && emitter.Deflections.TryGetValue(bullet, out var turned) && turned.Time >= began)
                    return false;

                direction = ways[k];
                speed = emitter.SpeedOf(bullet);
                return true;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float level = f.attackLevel(f.stampedeWindows, t);

                if (level <= 0.002f)
                    return;

                if (f.hasScreenBounds)
                {
                    f.stampedeBackground.Position = f.screenMin;
                    f.stampedeBackground.Size = f.screenMax - f.screenMin;
                }

                f.stampedeBackground.Alpha = Math.Max(f.stampedeBackground.Alpha, level * DanmakusuBalance.SAKI_BACKGROUND_ALPHA);

                float size = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.SAKI_HORSE_SCREEN;
                f.stageBack.Add(StageTexture.HorseHead, (f.stageMin + f.stageMax) * 0.5f, new Vector2(size), 0f, DanmakusuBalance.SAKI_HORSE_ALPHA * level, new Color4(0.62f, 0.38f, 0.18f, 1f));
            }
        }
    }
}
