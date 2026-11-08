using System;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Urumi Ushizaki: she never pauses. Every eight beats a push is announced: for four beats everything that is on the screen (or is shot meanwhile) becomes a bullet of the act that glows, turns light grey and shakes
        // a little, and every eighth one swells to one and a half times its size. Then every one of them gets an extra straight speed (a multiple of the section's bullet speed) in the direction of the line from her to the player, as it was
        // when the announcement began; the extras add up, push after push, also for what was caught by an earlier one. The push is a closed form of time (see PushPlan), so a replay and a seek give the same.
        // ======================================================================================================================================================

        /// <summary>
        /// The pushes of one section of Urumi, shared by every bullet she has caught: pushes <c>j</c> is announced at <see cref="Marks"/>[j], takes effect at <see cref="At"/>[j] and adds <c>By[j]</c> (px per ms) to the velocity of every bullet caught
        /// by announcement <c>j</c> or before. The directions are learnt as the announcements come (the way to the player then).
        /// </summary>
        private sealed class PushPlan
        {
            public readonly double[] Marks;
            public readonly double[] At;
            public int KnownCount;
            private readonly double[] sumX, sumY, sumAtX, sumAtY; // (running sums of the extra speeds, and of the extra speeds times the moment they began: the offset is then one subtraction)

            public PushPlan(double[] marks, double preview)
            {
                Marks = marks;
                At = new double[marks.Length];

                for (int i = 0; i < marks.Length; i++)
                    At[i] = marks[i] + preview;

                sumX = new double[marks.Length + 1];
                sumY = new double[marks.Length + 1];
                sumAtX = new double[marks.Length + 1];
                sumAtY = new double[marks.Length + 1];
            }

            public void Learn(int cycle, Vector2 by)
            {
                sumX[cycle + 1] = sumX[cycle] + by.X;
                sumY[cycle + 1] = sumY[cycle] + by.Y;
                sumAtX[cycle + 1] = sumAtX[cycle] + by.X * At[cycle];
                sumAtY[cycle + 1] = sumAtY[cycle] + by.Y * At[cycle];
                KnownCount = cycle + 1;
            }

            public void Reset() => KnownCount = 0;

            /// <summary>How many of the known pushes have begun before <paramref name="t"/>.</summary>
            private int begun(double t)
            {
                int low = 0, high = KnownCount;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (At[middle] < t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            /// <summary>How far the pushes <paramref name="from"/> and later have moved a bullet by <paramref name="t"/>.</summary>
            public Vector2 Offset(int from, double t)
            {
                int m = begun(t);

                if (m <= from)
                    return Vector2.Zero;

                return new Vector2((float)(t * (sumX[m] - sumX[from]) - (sumAtX[m] - sumAtX[from])), (float)(t * (sumY[m] - sumY[from]) - (sumAtY[m] - sumAtY[from])));
            }

            /// <summary>The extra velocity the pushes <paramref name="from"/> and later have given a bullet by <paramref name="t"/>.</summary>
            public Vector2 Velocity(int from, double t)
            {
                int m = begun(t);

                if (m <= from)
                    return Vector2.Zero;

                return new Vector2((float)(sumX[m] - sumX[from]), (float)(sumY[m] - sumY[from]));
            }

            /// <summary>The announcement whose four beats contain <paramref name="t"/> (-1 = none).</summary>
            public int WindowAt(double t)
            {
                int low = 0, high = Marks.Length;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (At[middle] < t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low < Marks.Length && Marks[low] <= t ? low : -1;
            }

            /// <summary>Is a push being announced at <paramref name="t"/> (its direction known)? Then <paramref name="cycle"/> is which.</summary>
            public bool Previewing(double t, out int cycle)
            {
                cycle = WindowAt(t);
                return cycle >= 0 && cycle < KnownCount;
            }
        }

        private sealed class MomentumAct : StageAct
        {
            private readonly PushPlan plan;
            private readonly double[] marks;
            private readonly float pushSpeed;

            public MomentumAct(BulletField field, BossPeriod period, double[] marks)
            {
                this.marks = marks;
                Period = period;
                Start = marks[0];
                End = period.End;
                AbsorbFrom = marks[0];
                AbsorbTo = period.End + 60000; // (not used: Absorbs says when)

                double beat = Math.Max(period.BeatLength, 1);
                plan = new PushPlan(marks, beat * DanmakusuBalance.URUMI_PREVIEW_BEATS);
                pushSpeed = stageBulletSpeed(period) * DanmakusuBalance.URUMI_PUSH_SPEED;
            }

            private Vector2 bossNow(BulletField f) => f.bossPositionOf(Period);

            private bool onScreen(BulletField f, Vector2 p, float margin)
            {
                if (!f.hasScreenBounds)
                    return true;

                return p.X >= f.screenMin.X - margin && p.X <= f.screenMax.X + margin && p.Y >= f.screenMin.Y - margin && p.Y <= f.screenMax.Y + margin;
            }

            /// <summary>Bullets are caught for the four beats of every announcement (what is shot meanwhile too).</summary>
            public override bool Absorbs(double t) => plan.WindowAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                int cycle = plan.WindowAt(from);

                if (cycle >= 0)
                    return true;

                // (the first announcement that ends after <from>: does it begin before <to>?)
                for (int k = 0; k < marks.Length; k++)
                {
                    if (plan.At[k] >= from)
                        return marks[k] <= to;
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || !onScreen(f, to, 8f))
                    return false;

                int cycle = plan.WindowAt(t);

                if (cycle < 0 || cycle >= plan.KnownCount)
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                float speed = delta.LengthSquared > 1e-10f ? (emitter.Ops == null ? delta.Length / (float)dt : stored.Speed) : 0f;
                Vector2 facing = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitX;
                float big = emitter.BigScale(bullet);
                Vector2 velocity = facing * speed;
                int before = Bullets.Count;

                // (every eighth one swells, counted from the ring's time as well as its number, as her rings can be smaller than that: by which one it is, so a replay and a seek give the same)
                bool swells = (Math.Abs((long)emitter.Time) + bullet) % DanmakusuBalance.URUMI_SWELL_EVERY == 0;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Face = facing,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_GRAY,
                    TintAmount = DanmakusuBalance.URUMI_GREY,
                    FadeIn = DanmakusuBalance.URUMI_SWELL_MS,
                    ScaleFrom = t,
                    ScaleOver = swells ? DanmakusuBalance.URUMI_SWELL_MS : 0,
                    ScaleTo = swells ? DanmakusuBalance.URUMI_SIZE : 1f,
                    BloomAt = t,
                    BloomTo = DanmakusuBalance.URUMI_BLOOM,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    BladeAngle0 = stored.Blades > 1 ? emitter.BladeAngle(bullet, t) : 0f,
                    Shade = stored.Shade,
                    Until = t + Math.Min(120000, 2.0 * f.stageLife(velocity)), // (the pushes can keep it on the screen longer than its own speed would)
                    Pushes = plan,
                    PushFrom = cycle,
                });

                return Bullets.Count > before;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (the way to the player when the announcement begins is the way of its push)
                while (plan.KnownCount < marks.Length && now >= marks[plan.KnownCount])
                {
                    Vector2 line = f.characterPosition - bossNow(f);
                    Vector2 direction = line.LengthSquared > 1e-4f ? Vector2.Normalize(line) : Vector2.UnitY;

                    plan.Learn(plan.KnownCount, direction * pushSpeed);
                }
            }

            public override void Reset(BulletField f, double time)
            {
                plan.Reset();
                ClearBullets();
            }
        }
    }
}
