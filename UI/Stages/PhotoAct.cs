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
        // Hatate Himekaidou (2026-11-22): no pauses. Every HATATE_EVERY_BEATS of her section she throws a photo frame (StageLayer's, after the user's screenshot of her spell) from where she is at where the
        // player is then: harmless, it flies there over HATATE_FLY_BEATS, HATATE_FRAME_START circle diameters tall as it leaves her and HATATE_FRAME_END as it gets there (HATATE_FRAME_ASPECT as wide as tall),
        // turned square to the way it flies (its wide side across it, as she holds it up: 2026-11-22). Only its rim and cross show as it flies; its see-through grey glass comes as the picture is taken
        // (2026-11-24). Since 2026-11-25 the frame stays where it is, with its glass, for HATATE_FRAME_STAY_BEATS after the picture, and only what is in it as the picture is taken turns.
        // As it gets there she takes the picture (a shutter, a flash, and the frame fades): everything that is flying turns to fly the way from the frame to where the player is now, HATATE_SLOW times as fast
        // (BossOps: an aim at a point far off that way, which this act places as the picture is taken). A player whose character is in the frame then is hit once, and for HATATE_DELAY_LENGTH_BEATS the
        // character follows where the mouse was (and what the keys did) HATATE_DELAY_BEATS before.
        // Where each frame is thrown is taken when it is thrown and kept, so time going back throws it the same way.
        // ======================================================================================================================================================

        private sealed class PhotoAct : StageAct
        {
            private readonly double beat;
            private readonly List<double> shots;
            private readonly Vector2[] from;
            private readonly Vector2[] target;
            private readonly bool[] known;
            private readonly bool[] taken;
            private readonly float[] facing; // (how far each frame is turned, radians: across the way from her to where it was thrown, so that it faces her - 2026-11-22)
            private readonly List<TrajectoryOp> turns = new List<TrajectoryOp>(); // (the aims BossOps made for her photos, in the order of their times)
            private double resetTime = double.NegativeInfinity;

            public PhotoAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                shots = BossOps.PhotoShots(period);
                from = new Vector2[shots.Count];
                target = new Vector2[shots.Count];
                known = new bool[shots.Count];
                taken = new bool[shots.Count];
                facing = new float[shots.Count];

                Start = period.ShootStart;
                End = shots.Count > 0 ? photoAt(shots.Count - 1) + beat * DanmakusuBalance.HATATE_FRAME_STAY_BEATS : period.End;

                foreach (var op in f.bulletOps)
                {
                    if (op.Kind == TrajectoryOpKind.Aim && op.SpeedFactor == DanmakusuBalance.HATATE_SLOW && !op.PlayerAim && op.Time >= period.ShootStart - 1 && op.Time <= End)
                        turns.Add(op);
                }

                turns.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            private double fly => beat * DanmakusuBalance.HATATE_FLY_BEATS;

            private double photoAt(int k) => shots[k] + fly;

            /// <summary>How far frame <paramref name="k"/> has flown (0 .. 1).</summary>
            private float flown(int k, double t) => (float)Math.Clamp((t - shots[k]) / fly, 0, 1);

            private Vector2 centreOf(int k, double t) => Vector2.Lerp(from[k], target[k], flown(k, t));

            /// <summary>How big frame <paramref name="k"/> is at <paramref name="t"/>: its width and height.</summary>
            private Vector2 sizeOf(BulletField f, int k, double t)
            {
                float height = 2f * f.circleRadius * (DanmakusuBalance.HATATE_FRAME_START + (DanmakusuBalance.HATATE_FRAME_END - DanmakusuBalance.HATATE_FRAME_START) * flown(k, t));
                return new Vector2(height * DanmakusuBalance.HATATE_FRAME_ASPECT, height);
            }

            /// <summary>Is <paramref name="p"/> in frame <paramref name="k"/> as the picture is taken (the character's middle, or its hitbox's edge)?</summary>
            private bool inFrame(BulletField f, int k, Vector2 p)
            {
                Vector2 half = sizeOf(f, k, photoAt(k)) * 0.5f + new Vector2(DanmakusuBalance.PLAYER_HITBOX_RADIUS);
                Vector2 d = turned(p - target[k], -facing[k]); // (in the frame's own way round)
                return MathF.Abs(d.X) <= half.X && MathF.Abs(d.Y) <= half.Y;
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < shots.Count; k++)
                {
                    if (shots[k] > time)
                        known[k] = false;

                    if (photoAt(k) > time)
                        taken[k] = false;
                }

                foreach (var op in turns)
                {
                    if (op.Time > time)
                        op.Placed = false;
                }

                f.forgetInputDelayAfter(time);
                resetTime = time;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < shots.Count && shots[k] <= now; k++)
                {
                    // (every frame is thrown at where the player is then)
                    if (!known[k])
                    {
                        known[k] = true;
                        from[k] = f.bossPositionOf(Period);
                        target[k] = f.characterPosition;
                        Vector2 thrown = target[k] - from[k];
                        facing[k] = (thrown.LengthSquared > 1f ? MathF.Atan2(thrown.Y, thrown.X) : MathF.PI / 2f) + MathF.PI / 2f;
                    }

                    if (taken[k] || photoAt(k) > now)
                        continue;

                    // the picture is taken: everything flying turns to fly from the frame to the player, half as fast
                    taken[k] = true;
                    double photo = photoAt(k);
                    Vector2 way = f.characterPosition - target[k];

                    if (way.LengthSquared < 1f)
                        way = target[k] - from[k];

                    way = way.LengthSquared > 1e-4f ? Vector2.Normalize(way) : Vector2.UnitY;

                    foreach (var op in turns)
                    {
                        if (Math.Abs(op.Time - photo) > 1 || op.Placed)
                            continue;

                        op.Point = target[k] + way * 1e6f;
                        op.FrameCentre = target[k]; // (only what is in the frame now: 2026-11-25)
                        op.FrameHalf = sizeOf(f, k, photo) * 0.5f;
                        op.FrameAngle = facing[k];
                        op.Placed = true;
                        f.autoOpsMoved = true; // (every ring's way after it has changed: the autoplay looks again)
                    }

                    if (now - photo < 250 && photo > resetTime)
                        f.playStageSound("hatate_shutter");

                    // (a player in the frame is hit once, and follows the mouse late for a while)
                    if (inFrame(f, k, f.characterPosition))
                    {
                        int key = Id * 65536 + 61000 + Math.Min(k, 4000);

                        if (f.stageConsumed.Add(key))
                        {
                            f.LastHitCause = $"Hatate's photo {k}";
                            f.onDamaged(f.damagePerBullet, true, now);
                            f.hits.Add(new HitRecord(now, stage_hit, key, f.damagePerBullet));
                        }

                        f.delayInput(photo, beat * DanmakusuBalance.HATATE_DELAY_BEATS, beat * DanmakusuBalance.HATATE_DELAY_LENGTH_BEATS);
                    }
                }
            }

            /// <summary>(for the autoplay) The picture of a frame already thrown is taken between <paramref name="from_"/> and <paramref name="to"/>, with <paramref name="at"/> in it.</summary>
            public override bool Springs(BulletField f, Vector2 at, double from_, double to)
            {
                for (int k = 0; k < shots.Count; k++)
                {
                    double photo = photoAt(k);

                    if (known[k] && photo > from_ && photo <= to && inFrame(f, k, at))
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                double fade = beat * 0.25;
                double stay = beat * DanmakusuBalance.HATATE_FRAME_STAY_BEATS;

                for (int k = 0; k < shots.Count; k++)
                {
                    if (!known[k] || t < shots[k] || t > photoAt(k) + stay)
                        continue;

                    double photo = photoAt(k);
                    Vector2 centre = centreOf(k, t);
                    Vector2 size = sizeOf(f, k, t);

                    // (it comes out of her over a moment; after the picture it stays where it is, with its glass, for HATATE_FRAME_STAY_BEATS, fading over the last quarter beat - 2026-11-25: it faded as the picture was taken)
                    float alpha = DanmakusuBalance.HATATE_FRAME_ALPHA * smoothStep((t - shots[k]) / (beat * 0.15)) * (1f - smoothStep((t - (photo + stay - fade)) / fade));

                    if (alpha > 0.01f)
                        f.stageFront.Add(StageTexture.PhotoFrame, centre, size, facing[k] * 180f / MathF.PI, alpha, Color4.White);

                    // (its see-through grey glass from when the picture is taken, staying with the frame: 2026-11-24, 2026-11-25)
                    if (t >= photo && alpha > 0.01f)
                        f.stageFront.Add(StageTexture.PhotoGlass, centre, size, facing[k] * 180f / MathF.PI, alpha, Color4.White);

                    // (the flash of the picture)
                    if (t >= photo)
                    {
                        float flash = 1f - (float)Math.Clamp((t - photo) / (beat * 0.4), 0, 1);

                        if (flash > 0.01f)
                            f.stageFront.Add(StageTexture.Glow, target[k], new Vector2(Math.Max(size.X, size.Y) * 1.8f), 0f, flash * flash, Color4.White, true);
                    }
                }
            }
        }
    }
}
