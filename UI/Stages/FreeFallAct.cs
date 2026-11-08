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
        // Toyohime: during an attack every bullet that leaves the top of the screen stops there and falls freely (it is dropped, see BulletEmitter.Deflection.Gravity). Every other bullet that
        // comes within TOYOHIME_PULL_RADIUS circle radii of the player is pulled towards the player (it turns at a steady rate, at its own speed, until it points at the player: then it flies
        // straight on). Neither goes on in the pauses (what has been dropped keeps falling).
        // ======================================================================================================================================================

        private sealed class FreeFallAct : StageAct
        {
            private readonly BulletField field;
            private readonly HashSet<(BulletEmitter, int)> aimed = new HashSet<(BulletEmitter, int)>(); // (pulled until it pointed at the player: not pulled again)
            private readonly HashSet<(BulletEmitter, int)> dropped = new HashSet<(BulletEmitter, int)>(); // (a bullet falls from the top once: it is not dropped again)
            private float lastGravity;

            public FreeFallAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            public override float PushGravity => lastGravity;

            public override void Reset(BulletField f, double time)
            {
                // (the field makes every ring scan again from its start: the bullets are pulled again, and are golden again only then)
                aimed.Clear();
                dropped.Clear();

                foreach (var emitter in f.emitters)
                    emitter.GoldMask = 0;
            }

            private static readonly Color4 gold = new Color4(1f, 0.8f, 0.3f, 1f);

            /// <summary>
            /// The circle round the player in which bullets are pulled: a golden magic circle (its outer ring is the edge of the pull) that turns slowly, with golden arrows on it that keep
            /// sliding in towards the player; there for as long as the attack.
            /// </summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                    return;

                float presence = smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);
                Vector2 player = f.characterPosition;
                float reach = DanmakusuBalance.TOYOHIME_PULL_RADIUS * f.circleRadius;
                float spin = (float)(t - Start) * DanmakusuBalance.TOYOHIME_CIRCLE_SPIN; // (degrees)

                f.stageRipple.Add(StageTexture.MagicCircle, player, new Vector2(2f * reach), spin, DanmakusuBalance.TOYOHIME_CIRCLE_ALPHA * presence, gold, additive: true);

                // the arrows: evenly round the circle, each sliding in from the outer ring towards the player and fading as it goes, one after the other
                int arrows = DanmakusuBalance.TOYOHIME_ARROWS;
                float arrowSize = DanmakusuBalance.TOYOHIME_ARROW_SIZE * f.circleRadius;
                double cycle = Math.Max(Period.BeatLength, 1) * DanmakusuBalance.TOYOHIME_ARROW_BEATS;

                for (int k = 0; k < arrows; k++)
                {
                    float phase = (float)(((t - Start) / cycle + (double)k / arrows) % 1.0);
                    float angle = MathF.Tau * k / arrows + spin * MathF.PI / 180f * 0.5f;
                    Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    float distance = f.circleRadius * (DanmakusuBalance.TOYOHIME_ARROW_FAR - (DanmakusuBalance.TOYOHIME_ARROW_FAR - DanmakusuBalance.TOYOHIME_ARROW_NEAR) * phase); // (close round the player)
                    float fade = smoothStep(phase / 0.2) * (1f - smoothStep((phase - 0.6f) / 0.4));

                    f.stageRipple.Add(StageTexture.Chevron, player + outward * distance, new Vector2(arrowSize), angle * 180f / MathF.PI + 180f, DanmakusuBalance.TOYOHIME_ARROW_ALPHA * presence * fade, gold, additive: true);
                }
            }

            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;
                lastGravity = 0f;

                if (t < Start || t > End || !emitter.CanBeDeflected)
                    return false;

                // it leaves the top of the screen: it stops there and falls (at the speed it had; once: it falls again from the top only if it has not fallen yet)
                float top = f.stageMin.Y;

                if (!emitter.Ricochet && from.Y >= top && to.Y < top && !dropped.Contains((emitter, bullet)))
                {
                    double before = emitter.WithoutFrozenTime(f.effects.Effective(emitter, ScanPrevious));
                    double after = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));
                    float flown = (to - from).Length;
                    float u = Math.Clamp((from.Y - top) / Math.Max(from.Y - to.Y, 1e-6f), 0f, 1f);
                    position = new Vector2(from.X + (to.X - from.X) * u, top);
                    direction = Vector2.UnitY;
                    speed = after - before > 1e-6 && flown > 1e-6f ? flown / (float)(after - before) : Math.Max(emitter.Speed, 0.02f);
                    lastGravity = 0f;
                    dropped.Add((emitter, bullet));
                    return true;
                }

                // within reach of the player: pulled towards the player (only every second bullet of a ring; the others fly on)
                if (bullet % DanmakusuBalance.TOYOHIME_PULL_EVERY != 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = DanmakusuBalance.TOYOHIME_PULL_RADIUS * f.circleRadius;
                Vector2 away = to - player;

                if (away.LengthSquared > reach * reach || aimed.Contains((emitter, bullet)))
                    return false;

                double ea = emitter.WithoutFrozenTime(f.effects.Effective(emitter, ScanPrevious));
                double eb = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));
                double dt = eb - ea;
                Vector2 delta = to - from;
                float travelled = delta.Length;

                if (dt <= 1e-6 || travelled < 1e-6f)
                    return false;

                Vector2 heading = delta / travelled;
                Vector2 wanted = -away;
                float distance = wanted.Length;

                if (distance < 1e-3f)
                    return false;

                wanted /= distance;
                float cos = Vector2.Dot(heading, wanted);

                if (cos >= DanmakusuBalance.HOMING_AIMED_COSINE)
                {
                    aimed.Add((emitter, bullet)); // (it points at the player: it flies straight on)
                    return false;
                }

                float angle = MathF.Atan2(heading.X * wanted.Y - heading.Y * wanted.X, cos);
                float turn = Math.Clamp(angle, -DanmakusuBalance.TOYOHIME_TURN_RATE * (float)dt, DanmakusuBalance.TOYOHIME_TURN_RATE * (float)dt);
                float c = MathF.Cos(turn), s = MathF.Sin(turn);

                direction = new Vector2(heading.X * c - heading.Y * s, heading.X * s + heading.Y * c);
                speed = travelled / (float)dt;

                // (a bullet she pulls is golden and glows from now on)
                if (bullet < 64)
                    emitter.GoldMask |= 1UL << bullet;

                return true;
            }
        }
    }
}
