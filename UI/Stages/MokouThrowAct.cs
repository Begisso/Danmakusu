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
        // Mokou in a break and in a spinner (the spinner shoots as it always does): every few beats she throws a ball at the player, where the player is at that moment. It grows out of nothing on her during
        // the default warning (stars, power-up, a small shake), flies as half of her big ball, and when it hits the wall it grows to the big ball's size and behaves like it (rings, shake, dimming, a stay).
        // ======================================================================================================================================================

        private sealed class MokouThrowAct : StageAct
        {
            /// <summary>When she has come back after the last section (BossPeriod.Revival) her ball and the bullets of her landings are twice as big.</summary>
            private float revived => Period.Revival ? DanmakusuBalance.MOKOU_REVIVAL_SIZE : 1f;

            private readonly double[] times;
            private readonly bool[] made;
            private readonly bool[] warned;
            private readonly bool[] landedSounded;
            private readonly Vector2[] origins;
            private readonly Vector2[] directions;
            private readonly Vector2[] landings;
            private readonly double[] hits;
            private readonly double[] holdEnds;
            private readonly float speed;
            private readonly double beat;

            public MokouThrowAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);

                double gap = beat * DanmakusuBalance.MOKOU_THROW_BEATS;
                var list = new List<double>();

                // (a ball whose charging has begun when the section ends is not cancelled: it is thrown after the section, as usual)
                for (double at = start; at - DanmakusuBalance.POWER_UP_LENGTH_MS < end && list.Count < 500; at += gap)
                    list.Add(at);

                times = list.ToArray();
                made = new bool[times.Length];
                warned = new bool[times.Length];
                landedSounded = new bool[times.Length];
                origins = new Vector2[times.Length];
                directions = new Vector2[times.Length];
                landings = new Vector2[times.Length];
                hits = new double[times.Length];
                holdEnds = new double[times.Length];
                speed = Math.Max(stageBulletSpeed(period) * DanmakusuBalance.MOKOU_THROW_SPEED_FACTOR, DanmakusuBalance.MOKOU_THROW_MIN_SPEED);
            }

            /// <summary>The moments she throws again (every one after the first has the default warning: stars flying in to her, and the power-up).</summary>
            public override void Warnings(BulletField f, double now, List<double> ends)
            {
                double length = DanmakusuBalance.POWER_UP_LENGTH_MS;

                for (int k = 1; k < times.Length; k++)
                {
                    if (times[k] - length <= now && times[k] > now - 2000)
                        ends.Add(times[k]);
                }
            }

            /// <summary>0..1: the screen shakes while a ball grows during its warning (1..2 px, more as it gets bigger) and, as in the jumps, when a ball hits the wall.</summary>
            public float ShakeAt(double now)
            {
                float shake = 0f;
                double length = DanmakusuBalance.POWER_UP_LENGTH_MS;

                for (int k = 0; k < times.Length; k++)
                {
                    if (now >= times[k] - length && now < times[k])
                        shake = Math.Max(shake, 0.05f + 0.95f * (float)((now - (times[k] - length)) / length));
                    else if (made[k] && now >= hits[k] && now - hits[k] <= DanmakusuBalance.MOKOU_SHAKE_MS)
                        shake = Math.Max(shake, DanmakusuBalance.MOKOU_SHAKE * (1f - (float)((now - hits[k]) / DanmakusuBalance.MOKOU_SHAKE_MS)));
                }

                return shake;
            }

            public override void Update(BulletField f, double now)
            {
                double length = f.stageSoundLength("power_up");

                for (int k = 0; k < times.Length; k++)
                {
                    // the power-up of the warning ends when she throws (the first throw has the one of the section)
                    if (k > 0 && !warned[k] && now >= times[k] - length)
                        warned[k] = true; // (no warning sound any more: the bullets flash before the throw, see Warnings)

                    if (now < times[k])
                        continue;

                    if (!made[k])
                    {
                        made[k] = true;
                        warned[k] = true;
                        origins[k] = f.bossVisualOf(Period);

                        Vector2 toPlayer = f.characterPosition - origins[k];
                        directions[k] = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : new Vector2(0f, 1f);
                        aimThrow(f, k);
                        addThrow(f, k);

                        if (now - times[k] < 250)
                            f.playStageSound("mokou_wall_hit");
                    }

                    if (!landedSounded[k] && now >= hits[k])
                    {
                        landedSounded[k] = true;

                        if (now - hits[k] < 250)
                            f.playStageSound("mokou_wall_hit");
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                double length = f.stageSoundLength("power_up");

                for (int k = 0; k < times.Length; k++)
                {
                    warned[k] = times[k] - length <= time;

                    if (times[k] > time)
                    {
                        made[k] = false;
                        landedSounded[k] = false;
                    }
                    else if (made[k])
                    {
                        landedSounded[k] = hits[k] <= time;
                        addThrow(f, k);
                    }
                }
            }

            /// <summary>Where the ball hits the wall (its edge touches the edge of the screen) and when, and when it is gone.</summary>
            private void aimThrow(BulletField f, int k)
            {
                float flying = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived * DanmakusuBalance.MOKOU_THROW_SIZE_FACTOR;
                Vector2 low = f.stageMin + new Vector2(flying);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(flying));
                Vector2 direction = directions[k];
                Vector2 standing = Vector2.Clamp(origins[k], low, high);
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

                landings[k] = standing + direction * far;
                hits[k] = times[k] + Math.Max(Vector2.Distance(origins[k], landings[k]) / speed, 60.0);
                holdEnds[k] = hits[k] + beat * DanmakusuBalance.MOKOU_HOLD_BEATS;
            }

            /// <summary>The rings of the impact (from the edge of the big ball, once it has grown out), as in Mokou's jumps.</summary>
            private void addThrow(BulletField f, int k)
            {
                float baseRadius = f.stageBaseRadius(Period) * revived; // (twice as big when she has come back)
                float baseSpeed = Math.Max(stageBulletSpeed(Period), 0.02f);
                int colours = Math.Max(f.palette.Length, 1);
                double ringGap = beat * DanmakusuBalance.MOKOU_RING_GAP_BEATS;
                float twist = DanmakusuBalance.MOKOU_RING_TWIST_DEGREES * MathF.PI / 180f;
                float aim = MathF.Atan2(directions[k].Y, directions[k].X);
                float edgeDistance = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived;

                for (int ring = 0; ring < DanmakusuBalance.MOKOU_RINGS; ring++)
                {
                    double fired = hits[k] + DanmakusuBalance.MOKOU_BIG_GROW_MS + ring * ringGap;

                    for (int piece = 0; piece < DanmakusuBalance.MOKOU_RING_BULLETS; piece++)
                    {
                        float angle = aim + ring * twist + MathF.Tau * piece / DanmakusuBalance.MOKOU_RING_BULLETS;
                        Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (baseSpeed * DanmakusuBalance.MOKOU_RING_SPEED_FACTOR);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = landings[k] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * edgeDistance,
                            V = velocity,
                            Radius = baseRadius,
                            Hit = baseRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = ring % colours,
                            Shape = BulletShape.Circle,
                            Until = fired + f.stageLife(velocity),
                        });
                    }
                }
            }

            /// <summary>
            /// Where the ball of throw k is at <paramref name="t"/> and how big: it grows out of nothing on the boss during the warning (<paramref name="warning"/>), flies as big as half of the big
            /// ball, and grows to the big ball's size when it hits the wall, stays, and shrinks away.
            /// </summary>
            private bool ballAt(BulletField f, int k, double t, out Vector2 position, out float radius, out bool warning)
            {
                // (a ball that has been thrown goes on when the section ends: it flies, hits the wall and stays as usual)
                return ballRaw(f, k, t, out position, out radius, out warning);
            }

            private bool ballRaw(BulletField f, int k, double t, out Vector2 position, out float radius, out bool warning)
            {
                position = default;
                radius = 0f;
                warning = false;

                float big = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived;
                float flying = big * DanmakusuBalance.MOKOU_THROW_SIZE_FACTOR;
                double length = DanmakusuBalance.POWER_UP_LENGTH_MS;

                if (t < times[k])
                {
                    if (t < times[k] - length)
                        return false;

                    warning = true;
                    position = f.bossVisualOf(Period);
                    radius = flying * smoothStep((t - (times[k] - length)) / length);
                    return radius >= 1f;
                }

                if (!made[k])
                    return false;

                if (t < hits[k])
                {
                    position = origins[k] + directions[k] * (speed * (float)(t - times[k]));
                    radius = flying;
                    return true;
                }

                if (t > holdEnds[k])
                    return false;

                position = landings[k];
                float grow = smoothStep((t - hits[k]) / DanmakusuBalance.MOKOU_BIG_GROW_MS);
                float fade = 1f - smoothStep((t - (holdEnds[k] - DanmakusuBalance.MOKOU_BIG_FADE_MS)) / DanmakusuBalance.MOKOU_BIG_FADE_MS);

                radius = (flying + (big - flying) * grow) * fade;
                return radius >= 1f;
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                float flying = f.circleRadius * DanmakusuBalance.MOKOU_BIG_SIZE * revived * DanmakusuBalance.MOKOU_THROW_SIZE_FACTOR;

                for (int k = 0; k < times.Length; k++)
                {
                    if (ballAt(f, k, t, out Vector2 at, out float radius, out _))
                        into.Add(new StageDim { Centre = at, Radius = radius, Strength = DanmakusuBalance.MOKOU_DIM * Math.Min(radius / flying, 1f) });
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                for (int k = 0; k < times.Length; k++)
                {
                    // (a ball that is still growing on the boss in the warning hurts nobody)
                    if (ballAt(f, k, now, out Vector2 at, out float radius, out bool warning) && !warning
                        && Vector2.Distance(f.characterPosition, at) <= radius * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                Color4 fire = f.palette[0];

                for (int k = 0; k < times.Length; k++)
                {
                    if (made[k])
                    {
                        mokouFlash(f, origins[k], t - times[k], fire);

                        mokouFlash(f, landings[k], t - hits[k], fire);
                    }

                    if (ballAt(f, k, t, out Vector2 at, out float radius, out _))
                        mokouOrb(f, at, radius, fire, true);
                }
            }
        }

        /// <summary>Mokou's big circle: a sharp-edged ball made like a small bullet (border, body, white core), above the bullets, with a soft glow round it under them (<paramref name="radius"/> is the ball's).</summary>
        private static void mokouOrb(BulletField f, Vector2 centre, float radius, Color4 colour, bool below = false, Color4? bodyColour = null, StageLayer? into = null, float stretch = 1f, float degrees = 0f)
        {
            if (radius < 1f)
                return;

            // (the glow round it, under the bullets; the ball itself over the bullets, made like the small ones: a border, the body a little smaller, and the glow of the body; `into`: another layer for the ball;
            // `stretch`: the black hole's bending, this many times as long the way `degrees` says and squeezed across it)
            var layer = into ?? (below ? f.stageBulletsUnder : f.stageBullets);
            Color4 body = bodyColour ?? f.palette[0];
            Color4 border = DanmakusuBalance.BorderColour(body);
            Color4 core = DanmakusuBalance.IsWhiteBullet(body) ? DanmakusuBalance.WHITE_BULLET_CENTRE_COLOUR : Color4.White;
            float inner = radius * 2f * MOKOU_BODY_SCALE;
            Vector2 shape = new Vector2(stretch, 1f / MathF.Sqrt(stretch));

            f.stageBack.Add(StageTexture.Glow, centre, shape * (radius * 3.6f), degrees, 0.7f, body, true);
            f.stageBack.Add(StageTexture.Glow, centre, shape * (radius * 2.5f), degrees, 0.5f, body, true);
            layer.Add(StageTexture.Orb, centre, shape * (radius * 2f), degrees, 1f, new Color4(border.R, border.G, border.B, 1f));
            layer.Add(StageTexture.Orb, centre, shape * inner, degrees, 1f, new Color4(body.R, body.G, body.B, 1f));
            layer.Add(StageTexture.OrbGlow, centre, shape * inner, degrees, 1f, new Color4(core.R, core.G, core.B, 1f));

            // the bloom: light that spills over the edge of the ball and over the bullets round it (added on top, so it only brightens)
            var bloom = new Color4(body.R * 0.6f + 0.4f, body.G * 0.6f + 0.4f, body.B * 0.6f + 0.4f, 1f);

            layer.Add(StageTexture.Glow, centre, shape * (radius * 2.4f), degrees, 0.55f, bloom, true);
            layer.Add(StageTexture.Glow, centre, shape * (radius * 4.2f), degrees, 0.4f, body, true);
        }

        /// <summary>How big the body of Mokou's ball is next to its border (the small bullets' is 0.84: her border is half as thick).</summary>
        private const float MOKOU_BODY_SCALE = 0.92f;

        /// <summary>A ring of fire that spreads from <paramref name="centre"/> for a moment, <paramref name="age"/> ms after it was lit.</summary>
        private static void mokouFlash(BulletField f, Vector2 centre, double age, Color4 colour)
        {
            if (age < 0 || age > DanmakusuBalance.MOKOU_FLASH_MS)
                return;

            float u = (float)(age / DanmakusuBalance.MOKOU_FLASH_MS);
            float eased = 1f - (1f - u) * (1f - u);
            float reach = DanmakusuBalance.MOKOU_FLASH_RADIUS * f.circleRadius * eased;

            f.stageRipple.AddRing(centre, reach, Math.Max(3f, reach * 0.1f), 0.9f * (1f - u), colour);
        }
    }
}
