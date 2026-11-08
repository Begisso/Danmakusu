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
        // Mokou: she never pauses. Until her first attack she stands opposite the player; then she jumps at the player in a straight line (where the player was when she set off: no homing) until
        // she reaches the edge of the screen. There she stays for four beats, and a huge circle bullet grows fast out of her and stays on her; when it is gone she jumps at the player again, leaving an
        // exploding bullet where she jumped from. Each landing sends ten rings of circle bullets out of her in a spiral. She shoots nothing else. Touching her while she jumps hurts like a slider.
        // The only thing that is not a function of time is where the player was when a jump started, so every jump is made once, when it starts (a "leg": the jump, the landing, the stay, and all the
        // bullets that belong to it), and thrown away when time goes back before it.
        // ======================================================================================================================================================

        private sealed class MokouAct : StageAct
        {
            /// <summary>When she has come back after the last section (BossPeriod.Revival) her ball and the bullets of her landings are twice as big.</summary>
            private float revived => Period.Revival ? DanmakusuBalance.MOKOU_REVIVAL_SIZE : 1f;

            private sealed class Leg
            {
                public double JumpStart;
                public double HitTime;
                public double HoldEnd;
                public Vector2 From;
                public Vector2 To;
                public Vector2 Direction;
                public bool Sounded;

                /// <summary>The section is over before she lands (she goes back to the middle): there is no impact.</summary>
                public bool Cancelled;

                /// <summary>The power-up that leads into the jump after this stay has been played.</summary>
                public bool Warned;
            }

            public readonly double Beat;

            private readonly List<Leg> legs = new List<Leg>();
            private const int max_legs = 600;

            public MokouAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                Beat = Math.Max(period.BeatLength, 1);
            }

            private Leg? legAt(double t)
            {
                for (int i = legs.Count - 1; i >= 0; i--)
                {
                    if (legs[i].JumpStart <= t)
                        return legs[i];
                }

                return null;
            }

            private static Vector2 positionOf(Leg leg, double t)
            {
                if (t <= leg.JumpStart)
                    return leg.From;

                if (t >= leg.HitTime)
                    return leg.To;

                return Vector2.Lerp(leg.From, leg.To, (float)((t - leg.JumpStart) / (leg.HitTime - leg.JumpStart)));
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                if (now < Start || now > End)
                    return false;

                advance(f, now);

                var leg = legAt(now);

                if (leg == null)
                    return false;

                position = positionOf(leg, now);
                return true;
            }

            /// <summary>Starts every jump whose time has come (a jump starts when the stay before it is over).</summary>
            private void advance(BulletField f, double now)
            {
                for (int guard = 0; guard < 64 && legs.Count < max_legs; guard++)
                {
                    double next = legs.Count == 0 ? Start : legs[^1].HoldEnd;

                    if (next > now || next >= End)
                        break;

                    addLeg(f, next);
                }
            }

            private void addLeg(BulletField f, double jumpStart)
            {
                float margin = DanmakusuBalance.MOKOU_WALL_MARGIN * f.circleRadius;
                Vector2 low = f.stageMin + new Vector2(margin);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(margin));
                Vector2 from = legs.Count == 0 ? f.bossPositionOf(Period) : legs[^1].To;
                Vector2 toPlayer = f.characterPosition - from;

                if (toPlayer.LengthSquared < 1f)
                    toPlayer = f.fieldCentre - from;

                if (toPlayer.LengthSquared < 1f)
                    toPlayer = new Vector2(0f, -1f);

                Vector2 direction = Vector2.Normalize(toPlayer);
                Vector2 standing = Vector2.Clamp(from, low, high);
                float far = float.MaxValue;

                if (direction.X > 1e-4f)
                    far = Math.Min(far, (high.X - standing.X) / direction.X);
                else if (direction.X < -1e-4f)
                    far = Math.Min(far, (low.X - standing.X) / direction.X);

                if (direction.Y > 1e-4f)
                    far = Math.Min(far, (high.Y - standing.Y) / direction.Y);
                else if (direction.Y < -1e-4f)
                    far = Math.Min(far, (low.Y - standing.Y) / direction.Y);

                if (far >= float.MaxValue || far < 0f)
                    far = 0f;

                Vector2 landing = standing + direction * far;
                float speed = Math.Clamp(stageBulletSpeed(Period) * DanmakusuBalance.MOKOU_JUMP_SPEED_FACTOR, DanmakusuBalance.MOKOU_JUMP_MIN_SPEED, DanmakusuBalance.MOKOU_JUMP_MAX_SPEED);
                double flight = Math.Max(Vector2.Distance(from, landing) / speed, 60.0);

                var leg = new Leg
                {
                    JumpStart = jumpStart,
                    HitTime = jumpStart + flight,
                    HoldEnd = Math.Min(jumpStart + flight + Beat * DanmakusuBalance.MOKOU_HOLD_BEATS, End), // (everything of hers is gone when the section ends)
                    From = from,
                    To = landing,
                    Direction = direction,
                    Cancelled = jumpStart + flight >= End, // (the section ends while she is still in the air: she goes back to the middle and nothing hits the wall)
                };

                leg.Sounded = leg.Cancelled;

                legs.Add(leg);
                addBullets(f, legs.Count - 1, leg);
            }

            /// <summary>All the bullets of a leg: the exploding bullet she leaves behind, the rings of the landing and the big circle of her stay.</summary>
            private void addBullets(BulletField f, int index, Leg leg)
            {
                if (leg.Cancelled)
                    return;

                float baseRadius = f.stageBaseRadius(Period) * revived; // (twice as big when she has come back)
                float baseSpeed = Math.Max(stageBulletSpeed(Period), 0.02f);
                int colours = Math.Max(f.palette.Length, 1);

                // the landing: rings of circle bullets in a spiral (every ring is turned a little more than the one before and comes a little later)
                double ringGap = Beat * DanmakusuBalance.MOKOU_RING_GAP_BEATS;
                float twist = DanmakusuBalance.MOKOU_RING_TWIST_DEGREES * MathF.PI / 180f;
                float aim = MathF.Atan2(leg.Direction.Y, leg.Direction.X);

                float edgeDistance = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived;

                for (int ring = 0; ring < DanmakusuBalance.MOKOU_RINGS; ring++)
                {
                    // (the rings come out of the edge of the big circle once it has grown out)
                    double fired = leg.HitTime + DanmakusuBalance.MOKOU_BIG_GROW_MS + ring * ringGap;

                    for (int piece = 0; piece < DanmakusuBalance.MOKOU_RING_BULLETS; piece++)
                    {
                        float angle = aim + ring * twist + MathF.Tau * piece / DanmakusuBalance.MOKOU_RING_BULLETS;
                        Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (baseSpeed * DanmakusuBalance.MOKOU_RING_SPEED_FACTOR);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = leg.To + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * edgeDistance,
                            V = velocity,
                            Radius = baseRadius,
                            Hit = baseRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = ring % colours,
                            Shape = BulletShape.Circle,
                            Until = fired + f.stageLife(velocity),
                        });
                    }
                }

                // the big circle: it grows fast on her when she lands, stays on her, and is gone when she sets off again
                float bigRadius = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived;

                AddBullet(new StageBullet
                {
                    T0 = leg.HitTime,
                    P0 = leg.To,
                    V = Vector2.Zero,
                    Radius = bigRadius,
                    Hit = bigRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = 0,
                    Shape = BulletShape.Circle,
                    Until = leg.HoldEnd,
                    Grow = DanmakusuBalance.MOKOU_BIG_GROW_MS,
                    Fade = DanmakusuBalance.MOKOU_BIG_FADE_MS,
                    Slider = true,
                    Hidden = true,
                });
            }

            /// <summary>0..1: every landing shakes the screen for a moment (the field turns it into 1..2 pixels).</summary>
            public float ShakeAt(double now)
            {
                for (int i = legs.Count - 1; i >= 0; i--)
                {
                    double age = now - legs[i].HitTime;

                    if (age < 0 || legs[i].Cancelled)
                        continue;

                    if (age > DanmakusuBalance.MOKOU_SHAKE_MS)
                        return 0f;

                    return DanmakusuBalance.MOKOU_SHAKE * (1f - (float)(age / DanmakusuBalance.MOKOU_SHAKE_MS));
                }

                return 0f;
            }

            public override void Update(BulletField f, double now)
            {
                if (now < Start)
                    return;

                advance(f, now);

                // the warning of every jump after the first (the first has the one of the section): the power-up ends when she sets off
                double warningLength = f.stageSoundLength("power_up");

                foreach (var leg in legs)
                {
                    double sets = leg.HoldEnd;

                    if (leg.Warned || sets >= End || now < sets - warningLength)
                        continue;

                    leg.Warned = true; // (no warning sound any more: the bullets flash before the jump, see Warnings)
                }

                // every landing on the wall plays the wall-hit sound (the Touhou SFX clip) (not when it was jumped over by a seek)
                foreach (var leg in legs)
                {
                    if (leg.Sounded || now < leg.HitTime)
                        continue;

                    leg.Sounded = true;

                    if (now - leg.HitTime < 250)
                        f.playStageSound("mokou_wall_hit");
                }
            }

            /// <summary>The moments at which she sets off again (every one after the first has the default warning: stars flying in to her, and the power-up).</summary>
            public override void Warnings(BulletField f, double now, List<double> ends)
            {
                double length = DanmakusuBalance.POWER_UP_LENGTH_MS;

                foreach (var leg in legs)
                {
                    if (leg.HoldEnd < End && leg.HoldEnd - length <= now && leg.HoldEnd > now - 2000)
                        ends.Add(leg.HoldEnd);
                }
            }

            public override void Reset(BulletField f, double time)
            {
                // the jumps that started after this moment have not started yet; the others (and all their bullets) are as they were
                legs.RemoveAll(leg => leg.JumpStart > time);
                ClearBullets();

                foreach (var leg in legs)
                {
                    leg.Sounded = leg.Cancelled || leg.HitTime <= time;
                    leg.Warned = leg.HoldEnd - f.stageSoundLength("power_up") <= time;
                }

                for (int i = 0; i < legs.Count; i++)
                    addBullets(f, i, legs[i]);
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                for (int i = legs.Count - 1, shown = 0; i >= 0 && shown < 3; i--)
                {
                    var leg = legs[i];

                    if (leg.JumpStart > t)
                        continue;

                    shown++;

                    if (leg.Cancelled || t < leg.HitTime || t > leg.HoldEnd)
                        continue;

                    float grown = smoothStep((t - leg.HitTime) / DanmakusuBalance.MOKOU_BIG_GROW_MS) * (1f - smoothStep((t - (leg.HoldEnd - DanmakusuBalance.MOKOU_BIG_FADE_MS)) / DanmakusuBalance.MOKOU_BIG_FADE_MS));

                    into.Add(new StageDim { Centre = leg.To, Radius = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived * grown, Strength = DanmakusuBalance.MOKOU_DIM * grown });
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                var leg = legAt(now);

                if (leg == null || now > End)
                    return false;

                if (now <= leg.HitTime)
                    return Vector2.Distance(f.characterPosition, positionOf(leg, now)) <= DanmakusuBalance.MOKOU_BODY_RADIUS * f.circleRadius + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // her big circle (it hurts like a slider body for as long as it is there)
                if (leg.Cancelled || now > leg.HoldEnd)
                    return false;

                float grown = smoothStep((now - leg.HitTime) / DanmakusuBalance.MOKOU_BIG_GROW_MS) * (1f - smoothStep((now - (leg.HoldEnd - DanmakusuBalance.MOKOU_BIG_FADE_MS)) / DanmakusuBalance.MOKOU_BIG_FADE_MS));
                float bigHit = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived * DanmakusuBalance.BULLET_HITBOX_SCALE * grown;

                return grown >= 0.02f && Vector2.Distance(f.characterPosition, leg.To) <= bigHit + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
            }

            public override void Draw(BulletField f, double t)
            {
                if (t < Start)
                    return;

                float body = DanmakusuBalance.MOKOU_BODY_RADIUS * f.circleRadius;
                float baseRadius = f.stageBaseRadius(Period) * revived; // (twice as big when she has come back)
                Color4 fire = f.palette[0]; // (the combo colour, like the bullets)
                int shown = 0;

                // (the latest legs only: everything older is over)
                for (int i = legs.Count - 1; i >= 0 && shown < 3; i--)
                {
                    var leg = legs[i];

                    if (leg.JumpStart > t)
                        continue;

                    shown++;

                    // the flame she trails while she jumps
                    if (t <= leg.HitTime && t <= End)
                    {
                        for (int tail = 0; tail < 6; tail++)
                        {
                            Vector2 at = positionOf(leg, Math.Max(t - tail * 35.0, leg.JumpStart));
                            float keep = 1f - tail / 6f;

                            f.stageBack.Add(StageTexture.Glow, at, new Vector2(body * 3.2f * (0.6f + 0.4f * keep)), 0f, 0.5f * keep, fire, true);
                        }
                    }

                    mokouFlash(f, leg.From, t - leg.JumpStart, fire);

                    if (!leg.Cancelled)
                        mokouFlash(f, leg.To, t - leg.HitTime, fire);

                    // the big circle glows
                    if (!leg.Cancelled && t >= leg.HitTime && t <= leg.HoldEnd)
                    {
                        float grown = smoothStep((t - leg.HitTime) / DanmakusuBalance.MOKOU_BIG_GROW_MS) * (1f - smoothStep((t - (leg.HoldEnd - DanmakusuBalance.MOKOU_BIG_FADE_MS)) / DanmakusuBalance.MOKOU_BIG_FADE_MS));

                        mokouOrb(f, leg.To, f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived * grown, fire);
                    }
                }
            }
        }
    }
}
