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
        // Chiyari Tenkajin: in her attacks three rings of pools of blood turn round her, three, five and seven circle radii from her, every ring the other way round from the one inside it. A pool is uneven, about two circle radii across, with vampire's fangs in it.
        // A bullet that touches one turns red, glowing as everything does at the end of Remilia's attack, and is aimed at the player as they are at that moment; it does not turn again. The pools do not hurt.
        // What she shoots is Koakuma's bats that do not home and live until they leave the screen (a BatAct with Straight).
        // ======================================================================================================================================================

        private sealed class BloodAct : StageAct
        {
            private readonly List<(double Start, double End)> attacks;

            public BloodAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private float presence(double t)
            {
                float level = 0f;

                foreach (var (start, end) in attacks)
                {
                    if (end > start && t >= start && t <= end + DanmakusuBalance.STAGE_FADE_OUT_MS)
                        level = Math.Max(level, smoothStep((t - start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((end + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS));
                }

                return level;
            }

            public override bool Absorbs(double t) => presence(t) >= 0.5f;

            public override bool AbsorbsBetween(double from, double to)
            {
                foreach (var (start, end) in attacks)
                {
                    if (end > start && start <= to && end + DanmakusuBalance.STAGE_FADE_OUT_MS >= from)
                        return true;
                }

                return false;
            }

            private Vector2 poolAt(BulletField f, int ring, int pool, double t)
            {
                float distance = f.circleRadius * (DanmakusuBalance.CHIYARI_RING_FIRST + DanmakusuBalance.CHIYARI_RING_GAP * ring);
                float turn = (float)(Math.Tau * (t - Start) / (Math.Max(Period.BeatLength, 1) * DanmakusuBalance.CHIYARI_TURN_BEATS)) * (ring % 2 == 0 ? 1f : -1f);
                float angle = turn + ring * 1.1f + pool * MathF.Tau / DanmakusuBalance.CHIYARI_POOLS;

                return f.bossVisualOf(Period) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Deflector || emitter.Bats || emitter.IsStuck(bullet))
                    return false;

                // (a pool outside the screen catches nothing: what has left does not come back at the player)
                if (to.X < f.stageMin.X || to.X > f.stageMax.X || to.Y < f.stageMin.Y || to.Y > f.stageMax.Y)
                    return false;

                float reach = f.circleRadius * DanmakusuBalance.CHIYARI_POOL_RADIUS + emitter.HitRadius;

                for (int ring = 0; ring < DanmakusuBalance.CHIYARI_RINGS; ring++)
                {
                    for (int pool = 0; pool < DanmakusuBalance.CHIYARI_POOLS; pool++)
                    {
                        if (distanceToSegment(poolAt(f, ring, pool, t), from, to) > reach)
                            continue;

                        var stored = f.makeStored(emitter, t);
                        Vector2 aim = f.characterPosition - to;
                        Vector2 way = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;
                        Vector2 velocity = way * stored.Speed;
                        float big = emitter.BigScale(bullet);
                        int before = Bullets.Count;

                        AddBullet(new StageBullet
                        {
                            T0 = t,
                            P0 = to,
                            V = velocity,
                            Face = way,
                            Radius = stored.Radius * big,
                            Hit = stored.Hit * big,
                            Style = stored.Style,
                            Shape = stored.Shape,
                            Tint = BulletRenderer.TINT_RED,
                            TintAmount = DanmakusuBalance.REMILIA_BULLET_RED,
                            Bloom = DanmakusuBalance.REMILIA_BLOOM,
                            Shade = stored.Shade,
                            Until = t + f.stageLife(velocity),
                        });

                        return Bullets.Count > before;
                    }
                }

                return false;
            }

            public override void Reset(BulletField f, double time) => ClearBullets();

            public override void Draw(BulletField f, double t)
            {
                float level = presence(t);

                if (f.failed || level <= 0.002f)
                    return;

                float size = 2f * f.circleRadius * DanmakusuBalance.CHIYARI_POOL_RADIUS;

                for (int ring = 0; ring < DanmakusuBalance.CHIYARI_RINGS; ring++)
                {
                    for (int pool = 0; pool < DanmakusuBalance.CHIYARI_POOLS; pool++)
                    {
                        Vector2 at = poolAt(f, ring, pool, t);
                        float wobble = 1f + 0.06f * MathF.Sin((float)t * 0.004f + ring * 2f + pool);

                        // (an uneven pool with fangs in it, turning its own way, with a glow round it)
                        f.stageBack.Add(StageTexture.Glow, at, new Vector2(size * 2.2f * wobble), 0f, 0.8f * level, new Color4(0.9f, 0.06f, 0.1f, 1f), true);
                        f.stageBack.Add(StageTexture.BloodPool, at, new Vector2(size * 1.25f / wobble), (float)(t * 0.012) * (ring % 2 == 0 ? 1f : -1f) + pool * 50f, DanmakusuBalance.CHIYARI_POOL_ALPHA * level, Color4.White);
                    }
                }
            }
        }
    }
}
