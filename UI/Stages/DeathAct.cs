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
        // Zanmu Nippaku: every sixteen beats one smooth, see-through grey ripple goes out from her. It does not hurt, but everything it has gone over stops: a bullet becomes a bullet of the act (it slows down to
        // nothing over a beat while it turns grey and a skull fades in on it; it still hurts), a slice of a beam stops the same way (the beam asks the act: it is a closed form of time, the slices fly straight until the
        // ripple catches them). A ripple catches everything that was fired before the moment its stopped things fall, also what is fired after the ripple started. Eight beats after the ripple started (at the latest when her
        // section ends) what it has stopped falls like it does when the player dies, getting darker as it falls. A giant skull (grey, see-through) shows over a dimmed grey background and fades out until the next ripple.
        // ======================================================================================================================================================

        private sealed class DeathAct : StageAct, IBeamFreezer
        {
            private readonly BulletField field;
            private readonly double[] marks;
            private readonly Vector2[] centres;
            private readonly bool[] known;
            private readonly double[] falls; // (until when a ripple stops what is fired: eight beats after it started)
            private readonly BulletLook[] fadeLooks; // (how a beam slice looks as it turns grey, step by step while it slows down)
            private readonly BulletLook[] darkLooks; // (and as it darkens, step by step while it falls)
            private readonly BulletLook shakeLook;
            private readonly double slowdown; // (how long a bullet takes to stop: a beat)
            private readonly double hold; // (how long a stopped thing stays: eight beats, or less if her section ends sooner)
            private readonly double beat;
            private readonly List<int> awaiting = new List<int>(); // (stopped bullets of the act whose fall has not begun yet: when it does, they are pulled towards where the player is then)
            private readonly List<(double Time, Vector2 Position)> trail = new List<(double Time, Vector2 Position)>(); // (the direction of the line from her to the player: a stopped slice of a beam falls that way, the way it pointed when its fall began)

            public DeathAct(BulletField field, BossPeriod period, double[] marks)
            {
                this.field = field;
                this.marks = marks;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                falls = new double[marks.Length];
                Period = period;
                Start = marks[0];
                End = period.End;
                AbsorbFrom = marks[0];
                AbsorbTo = period.End + 60000; // (not used: Absorbs says when)

                beat = Math.Max(period.BeatLength, 1);
                hold = beat * DanmakusuBalance.ZANMU_HOLD_BEATS;
                slowdown = beat * DanmakusuBalance.ZANMU_SLOWDOWN_BEATS;

                for (int k = 0; k < marks.Length; k++)
                    falls[k] = Math.Min(marks[k] + hold, Math.Max(period.End, marks[k]));

                int steps = DanmakusuBalance.ZANMU_FADE_STEPS;
                fadeLooks = new BulletLook[steps + 1];
                darkLooks = new BulletLook[steps + 1];

                for (int step = 0; step <= steps; step++)
                {
                    fadeLooks[step] = new BulletLook { Overlay = BulletRenderer.TINT_GRAY, OverlayAmount = DanmakusuBalance.ZANMU_FROZEN_GREY * step / steps, Level = step };
                    darkLooks[step] = new BulletLook { Overlay = BulletRenderer.TINT_GRAY, OverlayAmount = DanmakusuBalance.ZANMU_FROZEN_GREY, Level = steps + step, Dark = DanmakusuBalance.ZANMU_FALL_SHADE * step / steps };
                }

                shakeLook = new BulletLook { Overlay = BulletRenderer.TINT_GRAY, OverlayAmount = DanmakusuBalance.ZANMU_FROZEN_GREY, Level = steps, Shaking = true };
            }

            /// <summary>How long a thing that is stopped at <paramref name="at"/> stays where it is before it falls: eight beats, or until her section ends if that is sooner.</summary>
            private double holdOf(double at) => Math.Min(hold, Math.Max(End - at, 0.0));

            /// <summary>How a slice of a beam that was stopped <paramref name="age"/> ms ago looks (grey in steps while it slows down, shaking before it falls, darker and darker as it falls).</summary>
            private BulletLook lookAfter(double age, double held)
            {
                int steps = DanmakusuBalance.ZANMU_FADE_STEPS;

                if (age >= held)
                    return darkLooks[(int)MathF.Round(smoothStep((age - held) / DanmakusuBalance.ZANMU_FALL_DARKEN_MS) * steps)];

                if (age >= held - beat * DanmakusuBalance.FROZEN_SHAKE_BEATS && age >= slowdown)
                    return shakeLook;

                return fadeLooks[(int)MathF.Round(smoothStep(age / slowdown) * steps)];
            }

            /// <summary>After this the stopped things have fallen away (see <see cref="IBeamFreezer.DoneAt"/>).</summary>
            public double DoneAt => End + KeepAfterEnd;

            /// <summary>
            /// How long after her section the last thing she has stopped can still be on its way: a ripple still going over it, the time it is held, and two falls through the screen
            /// (nothing ends a fall sooner than that - this is only how long it takes, not a limit).
            /// </summary>
            public double KeepAfterEnd => life() + slowdown + hold + Math.Sqrt(2.0 * fallDistance() / DanmakusuBalance.ZANMU_GRAVITY) + 1000;

            /// <summary>The longest way a falling thing has to go to be out of the screen (it is pulled towards the player, so it can go any way).</summary>
            private float fallDistance()
            {
                float diagonal = field.hasScreenBounds ? Vector2.Distance(field.screenMin, field.screenMax) : 2000f;
                return diagonal + DanmakusuBalance.ZANMU_WRAP_MARGIN * 4f;
            }

            /// <summary>The way a thing that stands at <paramref name="at"/> and begins to fall at <paramref name="starts"/> is pulled: along the line from her to the player, as it was then.</summary>
            private Vector2 pullAt(double starts, Vector2 at)
            {
                if (trail.Count == 0 || starts > trail[^1].Time + 40)
                    return Vector2.UnitY;

                int low = 0, high = trail.Count - 1;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (trail[middle].Time < starts)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return trail[low].Position;
            }

            private Vector2 bossNow(BulletField f) => f.bossPositionOf(Period);

            /// <summary>How long a ripple takes to cross the whole screen (ms).</summary>
            private double life()
            {
                float diagonal = field.hasScreenBounds ? Vector2.Distance(field.screenMin, field.screenMax) : 2000f;
                return (diagonal + 2f * DanmakusuBalance.ZANMU_RIPPLE_HALF_WIDTH) / DanmakusuBalance.ZANMU_RIPPLE_SPEED;
            }

            public override bool Absorbs(double t)
            {
                double span = life();

                foreach (double mark in marks)
                {
                    if (t >= mark && t <= mark + span)
                        return true;
                }

                return false;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                double span = life();

                foreach (double mark in marks)
                {
                    if (mark <= to && mark + span >= from)
                        return true;
                }

                return false;
            }

            private float wrapTop() => (field.hasScreenBounds ? field.screenMin.Y : field.stageMin.Y) - DanmakusuBalance.ZANMU_WRAP_MARGIN;

            private float wrapHeight() => (field.hasScreenBounds ? field.screenMax.Y - field.screenMin.Y : field.stageMax.Y - field.stageMin.Y) + DanmakusuBalance.ZANMU_WRAP_MARGIN * 2f;

            private bool onScreen(Vector2 p, float margin)
            {
                if (!field.hasScreenBounds)
                    return true;

                return p.X >= field.screenMin.X - margin && p.X <= field.screenMax.X + margin && p.Y >= field.screenMin.Y - margin && p.Y <= field.screenMax.Y + margin;
            }

            /// <summary>How far a thing that was going at one unit of speed when it was caught has got by <paramref name="age"/> ms later: it slows down evenly to a stop (in units of speed x ms).</summary>
            private float slid(double age)
            {
                double s = Math.Clamp(age, 0.0, slowdown);
                return (float)(s - s * s / (2.0 * slowdown));
            }

            /// <summary>(a ripple can only have stopped a slice once it has started, if the slice was fired before what it stops falls, and before the ripple has gone over the whole screen)</summary>
            public bool CanHaveStopped(double oldestFired, double time)
            {
                double span = life();

                for (int k = 0; k < marks.Length; k++)
                {
                    if (time < marks[k])
                        break;

                    if (known[k] && oldestFired < falls[k] && oldestFired <= marks[k] + span)
                        return true;
                }

                return false;
            }

            public bool Stops(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out double stoppedAt, out BulletLook look)
            {
                position = default;
                stoppedAt = 0;
                look = fadeLooks[0];
                double span = life();

                for (int k = 0; k < marks.Length; k++)
                {
                    if (time < marks[k] || !known[k])
                        break;

                    if (fired >= falls[k])
                        continue; // (fired after what this ripple has stopped has fallen: it is not touched)

                    // a slice of a beam is stopped when the front gets to the place it was fired from (the slider ball, or wherever the beam comes from): every slice that has been fired by then is stopped at the
                    // same moment, wherever it has got to, and every one fired after that is stopped as it is fired. So the beam stands as it was, whole (stopping each slice where the front meets it would tear it
                    // up: a slice that flies away almost as fast as the front is met far away, and its neighbour not at all)
                    double at = Math.Max(fired, marks[k] + Vector2.Distance(start, centres[k]) / DanmakusuBalance.ZANMU_RIPPLE_SPEED);

                    if (at - marks[k] > span)
                        continue;

                    if (at > time)
                        continue; // (the front has not got to it yet)

                    Vector2 caughtAt = start + velocity * (float)(at - fired);

                    if (!onScreen(caughtAt, 24f))
                        continue;

                    // the slices that one ripple has stopped fall all at once, together (they were stopped at different moments, but if each fell on its own the beam would be torn apart as it falls):
                    // eight beats after the ripple has gone over the whole screen, or when her section ends if that is sooner
                    double starts = Math.Max(at, Math.Min(marks[k] + span + hold, End));
                    double held = starts - at;
                    position = caughtAt + velocity * slid(time - at);

                    if (time > starts)
                    {
                        // (it falls towards the player, the way the player was when the fall began, and keeps that way)
                        float fallen = (float)(time - starts);
                        Vector2 stopped = caughtAt + velocity * slid(held);
                        position = stopped + pullAt(starts, stopped) * (0.5f * DanmakusuBalance.ZANMU_GRAVITY * fallen * fallen);
                    }

                    stoppedAt = at;
                    look = lookAfter(time - at, held);
                    return true;
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || !onScreen(to, 8f))
                    return false;

                // every bullet that is inside a ripple is stopped (the ripple's front has gone over it, whether it was there before or was fired after): until what the ripple has stopped falls
                int caught = -1;

                for (int k = 0; k < marks.Length; k++)
                {
                    if (!known[k] || t < marks[k] || emitter.Time >= falls[k])
                        continue;

                    // (only while the ripple is on the screen, and only what its front goes over: a bullet that is fired inside the front, behind it, is not touched)
                    if (t - marks[k] > life())
                        continue;

                    float front = DanmakusuBalance.ZANMU_RIPPLE_SPEED * (float)(t - marks[k]);
                    float frontBefore = DanmakusuBalance.ZANMU_RIPPLE_SPEED * (float)Math.Max(ScanPrevious - marks[k], 0.0);

                    if (Vector2.Distance(to, centres[k]) <= front && Vector2.Distance(from, centres[k]) > frontBefore)
                    {
                        caught = k;
                        break;
                    }
                }

                if (caught < 0)
                    return false;

                var stored = f.makeStored(emitter, t);
                Vector2 delta = to - from;
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                float speed = delta.LengthSquared > 1e-10f ? (emitter.Ops == null ? delta.Length / (float)dt : stored.Speed) : 0f;
                Vector2 facing = delta.LengthSquared > 1e-8f ? Vector2.Normalize(delta) : Vector2.UnitX;
                float big = emitter.BigScale(bullet);
                float radius = stored.Radius * big;
                double starts = t + holdOf(t); // (eight beats after it has been stopped)
                Vector2 velocity = facing * speed;
                int before = Bullets.Count;

                // it falls (towards the player, see Update) and is gone when it is out of the screen: there is no time set for that (it was the time a fall through the screen takes, but it falls on its own clock, which
                // other bosses slow down or stop - Sakuya, Utsuho, Mystia... - so it was gone in the middle of the screen; the field takes what has left the screen, and nothing stays over 64 beats)

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Acc = -velocity / (float)slowdown, // (it slows down evenly to a stop)
                    StopAfter = slowdown,
                    Face = facing,
                    Radius = radius,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_GRAY,
                    TintAmount = DanmakusuBalance.ZANMU_FROZEN_GREY,
                    Decal = BulletDecal.Skull,
                    Blades = stored.Blades,
                    BladeSpin = stored.BladeSpin,
                    BladeAngle0 = stored.Blades > 1 ? emitter.BladeAngle(bullet, t) : 0f,
                    FadeIn = slowdown, // (the grey and the skull fade in as it slows down)
                    Shade = stored.Shade,
                    Until = double.PositiveInfinity, // (64 beats: see AddBullet)
                    FallAt = starts,
                    ShakeFrom = Math.Max(starts - beat * DanmakusuBalance.FROZEN_SHAKE_BEATS, t + slowdown),
                    ShakeTo = starts,
                    DarkenOver = DanmakusuBalance.ZANMU_FALL_DARKEN_MS,
                    DarkTo = DanmakusuBalance.ZANMU_FALL_SHADE,
                });

                if (Bullets.Count > before)
                    awaiting.Add(Bullets.Count - 1);

                return Bullets.Count > before;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // where the player is: the stopped things that begin to fall are pulled that way (and keep it)
                if (trail.Count > 0 && now < trail[^1].Time)
                    trail.RemoveAll(sample => sample.Time > now);

                if (now >= Start - 1 && now <= End + KeepAfterEnd && (trail.Count == 0 || now - trail[^1].Time >= 16))
                {
                    Vector2 line = f.characterPosition - bossNow(f);
                    trail.Add((now, line.LengthSquared > 1e-4f ? Vector2.Normalize(line) : Vector2.UnitY));

                    while (trail.Count > 0 && trail[0].Time < now - 20000)
                        trail.RemoveAt(0);
                }

                for (int k = awaiting.Count - 1; k >= 0; k--)
                {
                    int index = awaiting[k];

                    if (index >= Bullets.Count)
                    {
                        awaiting.RemoveAt(k);
                        continue;
                    }

                    var waiting = Bullets[index];

                    if (now < waiting.FallAt)
                        continue;

                    // (the way of the line that joins her and the player, as it is when the fall begins: the same for everything, and it does not change while it falls)
                    Vector2 toPlayer = f.characterPosition - bossNow(f);
                    Vector2 fallWay = toPlayer.LengthSquared > 1e-4f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                    awaiting.RemoveAt(k);

                    // (it falls as it is, that way)
                    waiting.FallDir = fallWay;
                    Bullets[index] = waiting;
                }

                // (a ripple starts where she stands when the moment comes)
                for (int k = 0; k < marks.Length; k++)
                {
                    if (!known[k] && now >= marks[k])
                    {
                        known[k] = true;
                        centres[k] = bossNow(f);
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                ClearBullets();
                awaiting.Clear();
                trail.Clear();
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                double span = life();

                // the ripple: one smooth band, see-through grey, until it has left the screen
                for (int k = 0; k < marks.Length; k++)
                {
                    double age = t - marks[k];

                    if (!known[k] || age <= 0 || age > span)
                        continue;

                    float half = DanmakusuBalance.ZANMU_RIPPLE_HALF_WIDTH;
                    float r = DanmakusuBalance.ZANMU_RIPPLE_SPEED * (float)age;
                    float alpha = DanmakusuBalance.ZANMU_RIPPLE_ALPHA * smoothStep(age / 150.0);

                    // (the middle of the band is where the bullets are stopped)
                    f.stageRipple.AddRing(centres[k], r + half, half, alpha, new Color4(0.78f, 0.78f, 0.82f, 1f));
                }

                // the dimmed background and the skull, for as long as her section lasts
                if (t < Start - 1 || t > End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    return;

                float presence = smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (f.hasScreenBounds)
                {
                    f.deathBackground.Position = f.screenMin;
                    f.deathBackground.Size = f.screenMax - f.screenMin;
                }

                f.deathBackground.Alpha = Math.Max(f.deathBackground.Alpha, presence * DanmakusuBalance.ZANMU_BACKGROUND_ALPHA);

                // the skull shows when a ripple is thrown, and slowly fades out until the next one (growing a little meanwhile)
                int current = 0;

                while (current + 1 < marks.Length && marks[current + 1] <= t)
                    current++;

                double next = current + 1 < marks.Length ? marks[current + 1] : End;
                float cycle = (float)Math.Clamp((t - marks[current]) / Math.Max(next - marks[current], 1.0), 0.0, 1.0);
                float shown = smoothStep((t - marks[current]) / 200.0) * (1f - cycle);
                float size = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.ZANMU_SKULL_SIZE * (1f + DanmakusuBalance.ZANMU_SKULL_GROW * cycle);
                float grey = DanmakusuBalance.ZANMU_SKULL_GREY;

                f.stageBack.Add(StageTexture.Skull, (f.stageMin + f.stageMax) * 0.5f, new Vector2(size), 0f, DanmakusuBalance.ZANMU_SKULL_ALPHA * presence * shown, new Color4(grey, grey, grey, 1f));
            }
        }
    }
}
