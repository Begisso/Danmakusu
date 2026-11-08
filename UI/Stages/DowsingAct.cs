using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Nazrin: every four beats every bullet within three osu! circles of her turns into an arrowhead of the same size, and that arrowhead is pulled towards the player like one (it turns
        // towards them at constant speed, flies straight once it points at them or is close, and is gone when it leaves the screen). It cannot be written as a function of time, so the act
        // simulates the arrowheads step by step and keeps where each one has been (a trail); the field draws and checks them like the other bullets of a stage.
        // ======================================================================================================================================================

        private sealed class DowsingAct : StageAct
        {
            private const double window_ms = 100; // (a conversion takes the bullets that the scan finds in this long after the moment: the scan goes in steps)
            private const double trail_step_ms = DanmakusuBalance.HOMING_STEP_MS;

            private readonly BulletField field;
            private readonly double[] marks;
            private readonly List<Arrow> arrows = new List<Arrow>();
            private int converted; // (about half of the bullets, picked at random, become ellipses, which fall towards the player instead)
            private readonly Vector2[] centres;
            private readonly bool[] known;

            private sealed class Arrow
            {
                public int Index; // (in Bullets)
                public Vector2 Position;
                public Vector2 Velocity;
                public double Time;
                public bool Locked;
                public bool Calm;
                public bool Gone;
                public float Radius;
                public List<Vector2> Trail = null!;
            }

            public DowsingAct(BulletField field, BossPeriod period, double[] marks)
            {
                this.field = field;
                this.marks = marks;
                centres = new Vector2[marks.Length];
                known = new bool[marks.Length];
                Period = period;
                Start = marks[0];
                End = period.End;
                AbsorbFrom = marks[0];
                AbsorbTo = marks[marks.Length - 1] + window_ms;
            }

            private Vector2 bossNow(BulletField f) => f.bossPositionOf(Period);

            public override bool Absorbs(double t)
            {
                foreach (double mark in marks)
                {
                    if (t >= mark && t < mark + window_ms)
                        return true;
                }

                return false;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (double mark in marks)
                {
                    if (mark <= to && mark + window_ms >= from)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Period.Start || t > Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    return;

                // the area of the conversion, round her for as long as she is on the screen: a smooth band for each of the map's combo colours, one inside the other
                float reach = DanmakusuBalance.NAZRIN_REACH_CIRCLES * 2f * f.circleRadius;
                float halfWidth = Math.Max(f.circleRadius * 0.15f, 2f);
                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);
                float flash = 0f;

                // (and it flares up when the bullets in it are turned)
                for (int k = 0; k < marks.Length; k++)
                {
                    double age = t - marks[k];

                    if (age >= 0 && age < DanmakusuBalance.NAZRIN_RIPPLE_FADE_MS)
                        flash = Math.Max(flash, 1f - (float)(age / DanmakusuBalance.NAZRIN_RIPPLE_FADE_MS));
                }

                float alpha = DanmakusuBalance.NAZRIN_RIPPLE_ALPHA * presence * (1f + 1.5f * flash);
                Vector2 centre = bossNow(f);

                // (the whole circle glows for a moment when the bullets are turned)
                if (flash > 0.01f && f.palette.Length > 0)
                    f.stageBack.Add(StageTexture.Glow, centre, new Vector2(reach * 2.4f), 0f, DanmakusuBalance.NAZRIN_FLASH_ALPHA * flash * presence, f.palette[0], true);

                for (int i = 0; i < f.palette.Length && i < 5; i++)
                    f.stageRipple.AddRing(centre, reach - i * halfWidth * 1.4f, halfWidth, alpha * (1f - 0.12f * i), f.palette[i]);
            }

            public override void Reset(BulletField f, double time)
            {
                Array.Clear(known, 0, known.Length);
                converted = 0;
                arrows.Clear();
                ClearBullets();
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train)
                    return false;

                // the bullets in the circle are turned
                int mark = -1;

                for (int k = 0; k < marks.Length; k++)
                {
                    if (t >= marks[k] && t < marks[k] + window_ms)
                        mark = k;
                }

                if (mark < 0)
                    return false;

                float reach = DanmakusuBalance.NAZRIN_REACH_CIRCLES * 2f * f.circleRadius;

                if (Vector2.Distance(to, known[mark] ? centres[mark] : bossNow(f)) > reach)
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false; // (it stands still: it has no way to go on in)

                var stored = f.makeStored(emitter, t);
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                float speed = (emitter.Ops == null ? Math.Max(delta.Length / (float)dt, 0.02f) : stored.Speed) * DanmakusuBalance.NAZRIN_CONVERTED_SPEED;
                float big = emitter.BigScale(bullet);

                int index = Bullets.Count;

                if (hash01((int)(to.X * 10f), (int)(to.Y * 10f), converted++) < 0.5f)
                {
                    // an ellipse: pulled towards the player (where they are now) with a constant pull, and never turning to follow them
                    Vector2 toPlayer = f.characterPosition - to;
                    Vector2 pull = toPlayer.LengthSquared > 1e-6f ? Vector2.Normalize(toPlayer) * Period.ArrowGravity : Vector2.Zero;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = to,
                        V = Vector2.Normalize(delta) * speed,
                        Acc = pull,
                        Radius = stored.Radius * big,
                        Hit = stored.Hit * big,
                        Style = stored.Style,
                        Shape = BulletShape.Ellipse,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Until = t + 20000,
                        Bloom = DanmakusuBalance.NAZRIN_BLOOM,
                    });

                    return Bullets.Count > index;
                }

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = Vector2.Normalize(delta) * speed,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = BulletShape.Triangle,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = t + DanmakusuBalance.HOMING_LIFETIME_MS,
                    Trail = new List<Vector2>(64) { to },
                    TrailStep = trail_step_ms,
                    Bloom = DanmakusuBalance.NAZRIN_BLOOM,
                });

                if (Bullets.Count == index)
                    return false; // (too many bullets: it stays as it is)

                arrows.Add(new Arrow
                {
                    Index = index,
                    Position = to,
                    Velocity = Vector2.Normalize(delta) * speed,
                    Time = t,
                    Radius = stored.Radius * big,
                    Trail = Bullets[index].Trail!,
                });

                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (the ripple starts where she stands when the moment comes)
                for (int k = 0; k < marks.Length; k++)
                {
                    if (!known[k] && now >= marks[k])
                    {
                        known[k] = true;
                        centres[k] = bossNow(f);
                    }
                }

                if (arrows.Count == 0)
                    return;

                float release = DanmakusuBalance.HOMING_RELEASE_RADIUS_FACTOR * f.circleRadius;
                Vector2 player = f.characterPosition;
                float step = (float)trail_step_ms;

                foreach (var arrow in arrows)
                {
                    int guard = 0;

                    while (!arrow.Gone && arrow.Time + trail_step_ms <= now && guard++ < 600)
                        advance(f, arrow, player, step, release);
                }
            }

            /// <summary>One step of an arrowhead, as <see cref="BulletEmitter.StepHoming"/> does it: close to the player it flies straight on, it stops turning once it points at them, otherwise it is turned towards them (the speed stays).</summary>
            private void advance(BulletField f, Arrow arrow, Vector2 player, float step, float release)
            {
                Vector2 velocity = arrow.Velocity;
                float speedSquared = velocity.LengthSquared;

                if (speedSquared > 1e-12f)
                {
                    float speed = MathF.Sqrt(speedSquared);
                    Vector2 heading = velocity / speed;

                    if (!arrow.Calm)
                    {
                        Vector2 relative = player - arrow.Position;
                        Vector2 relativeAhead = player - (arrow.Position + velocity * step);

                        if (segmentDistance(relative, relativeAhead) <= release)
                        {
                            arrow.Calm = true;
                            arrow.Locked = true;
                        }
                        else if (!arrow.Locked)
                        {
                            float along = Vector2.Dot(relative, heading);
                            float across = heading.X * relative.Y - heading.Y * relative.X;

                            if (along > 0f && MathF.Abs(across) <= along * DanmakusuBalance.HOMING_AIMED_TANGENT)
                                arrow.Locked = true;
                        }
                    }

                    if (!arrow.Calm && !arrow.Locked)
                    {
                        Vector2 toPlayer = player - arrow.Position;
                        float length = toPlayer.Length;

                        if (length > 1e-3f)
                        {
                            Vector2 pull = toPlayer * (Period.ArrowGravity * step / length);
                            Vector2 sideways = pull - heading * Vector2.Dot(pull, heading);
                            Vector2 turned = velocity + sideways * DanmakusuBalance.HOMING_TURN_SCALE;
                            float turnedLength = turned.Length;

                            if (turnedLength > 1e-9f)
                                arrow.Velocity = turned * (speed / turnedLength);
                        }
                    }
                    else
                        arrow.Velocity = heading * speed;
                }

                arrow.Position += arrow.Velocity * step;
                arrow.Time += trail_step_ms;
                arrow.Trail.Add(arrow.Position);

                Vector2 low = f.stageMin - new Vector2(arrow.Radius), high = f.stageMax + new Vector2(arrow.Radius);

                if (arrow.Position.X < low.X || arrow.Position.X > high.X || arrow.Position.Y < low.Y || arrow.Position.Y > high.Y)
                {
                    arrow.Gone = true;

                    var bullet = Bullets[arrow.Index];
                    bullet.Until = arrow.Time;
                    Bullets[arrow.Index] = bullet;
                }
            }

            private static float segmentDistance(Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a;
                float lengthSquared = ab.LengthSquared;

                if (lengthSquared < 1e-9f)
                    return a.Length;

                float t = Math.Clamp(-Vector2.Dot(a, ab) / lengthSquared, 0f, 1f);
                return (a + ab * t).Length;
            }
        }
    }
}
