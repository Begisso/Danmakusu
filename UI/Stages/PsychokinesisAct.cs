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
        // Sumireko Usami (2026-11-25): every SUMIREKO_EVERY_BEATS of her section a huge beam comes down from the top, as Matenshi's do, SUMIREKO_BEAM_CIRCLES circle radii wide (four times hers), over
        // where the player is when its see-through preview comes (SUMIREKO_PREVIEW_BEATS). Solid, it reaches the bottom in a quarter of a beat and glows (SUMIREKO_BLOOM), the screen dims round it
        // (SUMIREKO_DIM, Miko's dim), and the Master Spark's sound and a shake (SUMIREKO_SHAKE) go on while it is there. For SUMIREKO_MOVE_BEATS it drifts slowly towards the side the player is on as it
        // turns solid (SUMIREKO_DRIFT_CIRCLES circle radii a beat); then it explodes (Hecatia's explosion) into SUMIREKO_STARS of Marisa's stars thrown out of it left and right, with Suika's impact
        // frame on it for SUMIREKO_IMPACT_MS (the beam's band turned inside out, soft-edged, impact stars beside it, in flashes). It hurts like a slider body while it is solid; the stars as bullets.
        // Since 2026-11-26 the preview is as thick as the beam and half as solid as Matenshi's (SUMIREKO_PREVIEW_ALPHA), the impact frame flashes over the SUMIREKO_IMPACT_MS right before it turns solid
        // (since 2026-11-27 that one is half as long and see-through: SUMIREKO_IMPACT_BEFORE_MS, SUMIREKO_IMPACT_BEFORE_STRENGTH - a quarter since 2026-11-28 -, ImpactStarLayer.Strength)
        // too, and its stars are three times as many, in its violet.
        // Where the player is when a preview comes and which way it drifts are taken then and kept; the stars are made again from them when time goes back.
        // ======================================================================================================================================================

        private sealed class PsychokinesisAct : StageAct
        {
            private readonly double beat;

            private readonly double[] strikes;
            private readonly float[] startX;
            private readonly float[] drift; // (-1 left, 1 right: towards the player as it turns solid)
            private readonly bool[] known, driftKnown, exploded;
            private int sounded = -1;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);
            private readonly List<Vector2> outline = new List<Vector2>(4);

            public PsychokinesisAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.SUMIREKO_EVERY_BEATS;

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.SUMIREKO_PREVIEW_BEATS < period.End && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                startX = new float[strikes.Length];
                drift = new float[strikes.Length];
                known = new bool[strikes.Length];
                driftKnown = new bool[strikes.Length];
                exploded = new bool[strikes.Length];

                Start = period.ShootStart;
                End = strikes.Length > 0 ? explodeAt(strikes.Length - 1) + DanmakusuBalance.SUMIREKO_IMPACT_MS + 60000 : period.End;
            }

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.SUMIREKO_PREVIEW_BEATS;

            private double explodeAt(int k) => solidAt(k) + beat * DanmakusuBalance.SUMIREKO_MOVE_BEATS;

            /// <summary>A beam always goes on to its explosion, also after her section.</summary>
            public override double MechanicsEnd => strikes.Length > 0 ? explodeAt(strikes.Length - 1) + DanmakusuBalance.SUMIREKO_IMPACT_MS : double.NegativeInfinity;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.SUMIREKO_BEAM_CIRCLES * 0.5f;

            private float top(BulletField f) => (f.hasScreenBounds ? f.screenMin.Y : f.stageMin.Y) - halfWidth(f);

            private float bottom(BulletField f) => (f.hasScreenBounds ? f.screenMax.Y : f.stageMax.Y) + halfWidth(f);

            private float reachDown(BulletField f, int k, double t)
                => top(f) + (bottom(f) - top(f)) * smoothStep((t - solidAt(k)) / (beat * DanmakusuBalance.MATENSHI_BEAM_GROW_BEATS));

            /// <summary>Where beam <paramref name="k"/> is across at <paramref name="t"/>: where it came, drifting once it is solid, until it explodes.</summary>
            private float xAt(BulletField f, int k, double t)
            {
                double moving = Math.Clamp(t - solidAt(k), 0, explodeAt(k) - solidAt(k));
                return startX[k] + (driftKnown[k] ? drift[k] : 0f) * f.circleRadius * DanmakusuBalance.SUMIREKO_DRIFT_CIRCLES * (float)(moving / beat);
            }

            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.SUMIREKO_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.SUMIREKO_PREVIEW_BEATS + DanmakusuBalance.SUMIREKO_MOVE_BEATS) - DanmakusuBalance.SUMIREKO_IMPACT_MS) / every));
                return (first, last);
            }

            /// <summary>The beam that is solid at <paramref name="t"/>, or -1.</summary>
            private int solidOne(double t)
            {
                if (strikes.Length == 0 || t < Start)
                    return -1;

                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (known[k] && t >= solidAt(k) && t < explodeAt(k))
                        return k;
                }

                return -1;
            }

            /// <summary>The Master Spark's sound plays while a beam is there.</summary>
            public bool Firing(double now) => solidOne(now) >= 0;

            /// <summary>The screen shakes while a beam is there.</summary>
            public float ShakeAt(double now) => solidOne(now) >= 0 ? DanmakusuBalance.SUMIREKO_SHAKE : 0f;

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();

                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;

                    if (solidAt(k) > time)
                        driftKnown[k] = false;

                    exploded[k] = false;
                }

                sounded = -1;

                for (int k = 0; k < strikes.Length && explodeAt(k) <= time; k++)
                    sounded = k;

                // (the stars of what had exploded by then are made again, as they were: they are a function of where it was)
                for (int k = 0; k < strikes.Length && explodeAt(k) <= time; k++)
                {
                    if (known[k])
                        explode(f, k);
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                var (first, last) = around(now);
                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] && strikes[k] <= now)
                    {
                        known[k] = true;
                        startX[k] = Math.Clamp(f.characterPosition.X, low.X, high.X);
                    }

                    if (known[k] && !driftKnown[k] && solidAt(k) <= now)
                    {
                        driftKnown[k] = true;
                        drift[k] = f.characterPosition.X >= startX[k] ? 1f : -1f;
                    }

                    if (known[k] && !exploded[k] && explodeAt(k) <= now)
                    {
                        explode(f, k);

                        if (k > sounded)
                        {
                            sounded = k;

                            if (now - explodeAt(k) < 250)
                                f.playStageSound("hecatia_explosion");
                        }
                    }
                }
            }

            /// <summary>The stars it explodes into: from all over it, the ones of its left half thrown left and the others right, each at a way and a speed of its own (by a hash).</summary>
            private void explode(BulletField f, int k)
            {
                exploded[k] = true;
                double at = explodeAt(k);
                float x = xAt(f, k, at);
                float half = halfWidth(f);
                float low = top(f) + half, high = bottom(f) - half;
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.SUMIREKO_STAR_SIZE;
                float plain = stageBulletSpeed(Period);
                int style = f.comboStyle(k, true, Period.Rank);

                for (int i = 0; i < DanmakusuBalance.SUMIREKO_STARS; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float across = hash01(k * 131 + i, 1, 501);
                    float down = hash01(k * 131 + i, 2, 502);
                    float angle = (side < 0 ? MathF.PI : 0f) + (hash01(k * 131 + i, 3, 503) - 0.5f) * MathF.PI * 0.6f;
                    float speed = plain * (DanmakusuBalance.SUMIREKO_STAR_SPEED_LEAST + (DanmakusuBalance.SUMIREKO_STAR_SPEED_MOST - DanmakusuBalance.SUMIREKO_STAR_SPEED_LEAST) * hash01(k * 131 + i, 4, 504));
                    Vector2 v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = at,
                        P0 = new Vector2(x + side * half * across, low + (high - low) * down),
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = style,
                        Shape = BulletShape.SoftStar, // (Marisa's)
                        Tint = BulletRenderer.TINT_PURPLE, // (her beam's violet: 2026-11-26)
                        TintAmount = 1f,
                        Spin = 0.004f * side,
                        SpinPhase = i,
                        Until = at + f.stageLife(v),
                    });
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k])
                        continue;

                    // (the screen dims round it while it is there, coming in and going quickly)
                    if (t >= solidAt(k) && t < explodeAt(k) + beat * 0.5)
                    {
                        float dim = smoothStep((t - solidAt(k)) / (beat * 0.5)) * (1f - smoothStep((t - explodeAt(k)) / (beat * 0.5)));

                        if (f.hasScreenBounds)
                        {
                            f.mikoBackground.Position = f.screenMin;
                            f.mikoBackground.Size = f.screenMax - f.screenMin;
                        }

                        f.mikoBackground.Alpha = Math.Max(f.mikoBackground.Alpha, dim * DanmakusuBalance.SUMIREKO_DIM);
                    }

                    impactFrame(f, k, t);
                }
            }

            /// <summary>
            /// Suika's impact frame on it as it explodes: its band, a little wider than it, turned inside out with a soft edge, and impact stars beside it, in flashes. Since 2026-11-26 ("white impact frames
            /// should be right before spawn too") the same over the SUMIREKO_IMPACT_MS right before it turns solid, where it will come down.
            /// </summary>
            private void impactFrame(BulletField f, int k, double t)
            {
                // (the one before it turns solid: half as long and half as strong, 2026-11-27 - "impact frame duration halved before laserbeam activation. Transparency doubled too")
                double before = solidAt(k) - DanmakusuBalance.SUMIREKO_IMPACT_BEFORE_MS;

                if (t >= before && t < solidAt(k))
                {
                    f.impactStars.Strength = DanmakusuBalance.SUMIREKO_IMPACT_BEFORE_STRENGTH;
                    impactFrame(f, k, t - before, xAt(f, k, solidAt(k)), 1, DanmakusuBalance.SUMIREKO_IMPACT_BEFORE_MS);
                    f.impactStars.Strength = 1f;
                }
                else
                    impactFrame(f, k, t - explodeAt(k), xAt(f, k, explodeAt(k)), 0, DanmakusuBalance.SUMIREKO_IMPACT_MS);
            }

            private void impactFrame(BulletField f, int k, double since, float x, int which, double lasts)
            {
                if (since < 0 || since >= lasts)
                    return;

                int flash = (int)(since / DanmakusuBalance.SUIKA_IMPACT_FLASH_MS);

                if (flash % 2 == 1)
                    return;

                k = k * 2 + which; // (the two frames of a beam: stars of their own)
                float w = halfWidth(f) * 1.25f;
                float up = top(f), down = bottom(f);
                Vector2 a = new Vector2(x - w, up), b = new Vector2(x + w, up), c = new Vector2(x - w, down), d = new Vector2(x + w, down);

                f.impactStars.AddPiece(a, b, c, d);
                outline.Clear();
                outline.Add(a);
                outline.Add(b);
                outline.Add(d);
                outline.Add(c);
                f.impactStars.AddOutline(outline, DanmakusuBalance.IMPACT_BAND_FEATHER_PX);

                for (int s = 0; s < DanmakusuBalance.SUMIREKO_IMPACT_STARS; s++)
                {
                    float side = s % 2 == 0 ? -1f : 1f;
                    float along = (s + 0.3f + 0.4f * hash01(k * 17 + s, flash, 511)) / DanmakusuBalance.SUMIREKO_IMPACT_STARS;
                    float size = f.circleRadius * (2.5f + 2f * hash01(k * 17 + s, flash, 512));
                    Vector2 place = new Vector2(x + side * (w + size * (0.6f + 0.8f * hash01(k * 17 + s, flash, 513))), up + (down - up) * along);
                    f.addImpactStar(place, new Vector2(size, size * (0.8f + 0.4f * hash01(k * 17 + s, flash, 514))), 8000 + k * 64 + s, flash);
                }

                f.impactStars.ShapesDone();
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var core = new Color4(1f, 0.96f, 1f, 1f);
                var body = new Color4(0.78f, 0.5f, 0.98f, 1f); // (her violet)
                var border = DanmakusuBalance.BorderColour(new Color4(0.48f, 0.22f, 0.54f, 1f));
                float half = halfWidth(f);
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= explodeAt(k))
                        continue;

                    // (the preview: half as solid as Matenshi's, and as thick as the beam - 2026-11-26, "double the transparency for harmless parts of the beam. Preview should be as thick as harmful part")
                    bool solid = t >= solidAt(k);
                    float alpha = solid ? 1f : DanmakusuBalance.SUMIREKO_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    float x = xAt(f, k, t);
                    line.Clear();
                    line.Add(new Vector2(x, top(f)));
                    line.Add(new Vector2(x, solid ? reachDown(f, k, t) : bottom(f)));
                    fades.Clear();
                    fades.Add(alpha);
                    fades.Add(alpha);
                    f.addPlainBeam(line, half, body, border, ref beams, ref pointTotal, null, fades, core, solid ? DanmakusuBalance.SUMIREKO_BLOOM : 0f);
                }
            }

            /// <summary>It hurts like a slider body while it is there, as far down as it has come.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                int k = solidOne(now);

                if (f.failed || k < 0)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float x = xAt(f, k, now);
                return distanceToSegment(player, new Vector2(x, top(f)), new Vector2(x, reachDown(f, k, now))) <= reach;
            }
        }
    }
}
