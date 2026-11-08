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
        // Mima: no pauses. Every sixteen beats of her section a giant crescent moon (its outer radius 60 % of the playfield's height) appears where the player is, the middle of its body on them and its back to the
        // outside of the screen (away from the middle of the playfield). For four beats it is see-through and harmless and grows from one tip to the other while the screen shakes; then it lights up the same way round
        // in a beat and is solid until eight beats after it stopped growing: it hurts like a slider body, and everything that flies into it (bullets, the slices of every beam; not Yukari's
        // train) bounces off it once (an op, see BossOps: this act tells it where the moon is). Then it is gone, and stars with short tails fly off all along its edges (a ring of the planner's: this act says where they
        // start, a ring for each edge). The next moon grows and lights up the other way round. A moon that has appeared always goes through to its end, also when her section is over. It is white all through with a thin
        // grey line along its edges and a soft white light round it, a flat shape between the beams and the bullets: pieces side by side between its outer edge and its hollow's, so it is sharp at any size and evenly
        // see-through. Where something bounces off it, a white flash shows for a moment. In its last beat its light reaches out twice as far and back, and when its stars fly off it fades out over a beat, harmless.
        // ======================================================================================================================================================

        private sealed class HalfMoonAct : StageAct
        {
            private sealed class Moon
            {
                public Vector2 Centre; // (the middle of the outer circle)
                public Vector2 Back; // (the way its back faces)
                public float Radius;
            }

            private readonly double[] marks;
            private readonly Moon?[] moons;
            private readonly TrajectoryOp?[] opsOf; // (the op of each moon's solid time, once it is placed)
            private readonly bool[] lit, gone; // (their sounds have been played)
            private readonly double beat;

            public HalfMoonAct(BossPeriod period, double[] marks)
            {
                Period = period;
                this.marks = marks;
                moons = new Moon?[marks.Length];
                opsOf = new TrajectoryOp?[marks.Length];
                lit = new bool[marks.Length];
                gone = new bool[marks.Length];
                beat = Math.Max(period.BeatLength, 1);
                Start = marks.Length > 0 ? marks[0] : period.ShootStart;
                End = marks.Length > 0 ? goneAt(marks.Length - 1) + fadeMs : period.ShootStart;
            }

            private double growMs => beat * DanmakusuBalance.MIMA_GROW_BEATS;

            private double flashMs => beat * DanmakusuBalance.MIMA_FLASH_BEATS;

            private double fadeMs => beat * DanmakusuBalance.MIMA_FADE_BEATS;

            private double litFrom(int k) => marks[k] + growMs;

            private double goneAt(int k) => marks[k] + beat * (DanmakusuBalance.MIMA_GROW_BEATS + DanmakusuBalance.MIMA_SOLID_BEATS);

            /// <summary>(her last moon goes through to its end after her section: she stays until it has faded)</summary>
            public override double MechanicsEnd => marks.Length == 0 ? double.NegativeInfinity : goneAt(marks.Length - 1) + fadeMs;

            /// <summary>The moons take turns: one grows and lights up clockwise, the next the other way round.</summary>
            private static float turnOf(int k) => k % 2 == 0 ? 1f : -1f;

            /// <summary>How far round from its back the tips are (radians): where the hollow's circle cuts the outer one.</summary>
            private static float tipAngle
                => MathF.Acos(Math.Clamp((DanmakusuBalance.MIMA_HOLLOW * DanmakusuBalance.MIMA_HOLLOW - 1f - DanmakusuBalance.MIMA_HOLLOW_OFFSET * DanmakusuBalance.MIMA_HOLLOW_OFFSET) / (2f * DanmakusuBalance.MIMA_HOLLOW_OFFSET), -1f, 1f));

            private static Vector2 hollowOf(Moon moon) => moon.Centre - moon.Back * (moon.Radius * DanmakusuBalance.MIMA_HOLLOW_OFFSET);

            private static float hollowRadius(Moon moon) => moon.Radius * DanmakusuBalance.MIMA_HOLLOW;

            /// <summary>Along the way <paramref name="e"/> from the middle: how far out the hollow ends (the body is from there to the outer circle).</summary>
            private static float hollowEnd(Moon moon, Vector2 e)
            {
                float offset = moon.Radius * DanmakusuBalance.MIMA_HOLLOW_OFFSET, r = hollowRadius(moon);
                float along = offset * Vector2.Dot(e, moon.Back);

                return -along + MathF.Sqrt(Math.Max(along * along - offset * offset + r * r, 0f));
            }

            /// <summary>The way out from the middle <paramref name="u"/> (0..1) of the way from the tip it starts at to the other one.</summary>
            private static Vector2 wayAt(Moon moon, int k, float u) => turned(moon.Back, turnOf(k) * tipAngle * (2f * u - 1f));

            /// <summary>
            /// The moon is placed: on the player, the middle of its body on them, its back to the outside. The op of its solid time learns where it is.
            /// </summary>
            private void place(BulletField f, int k)
            {
                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                Vector2 player = f.characterPosition;
                Vector2 outward = player - middle;
                var moon = new Moon
                {
                    Back = outward.LengthSquared > 1f ? Vector2.Normalize(outward) : Vector2.UnitY,
                    Radius = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.MIMA_RADIUS,
                };

                // (along its back the body runs from where the hollow ends to the outer circle: the player is halfway)
                float bodyMiddle = (moon.Radius + hollowRadius(moon) - moon.Radius * DanmakusuBalance.MIMA_HOLLOW_OFFSET) * 0.5f;
                moon.Centre = player - moon.Back * bodyMiddle;
                moons[k] = moon;

                double solid = litFrom(k) + beat * DanmakusuBalance.MIMA_LIGHT_BEATS;

                foreach (var op in f.bulletOps)
                {
                    if (op.Kind == TrajectoryOpKind.Crescent && Math.Abs(op.Time - solid) < 0.5)
                    {
                        op.Point = moon.Centre;
                        op.Radius = moon.Radius;
                        op.InnerPoint = hollowOf(moon);
                        op.InnerRadius = hollowRadius(moon);
                        opsOf[k] = op;
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < marks.Length; k++)
                {
                    opsOf[k]?.Bounces?.Clear();

                    if (marks[k] > time)
                        moons[k] = null;

                    if (litFrom(k) > time)
                        lit[k] = false;

                    if (goneAt(k) > time)
                        gone[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    if (moons[k] == null)
                    {
                        place(f, k);

                        if (now - marks[k] < 250)
                            f.playStageSound("mima_moon");
                    }

                    if (!lit[k] && now >= litFrom(k))
                    {
                        lit[k] = true;

                        if (now - litFrom(k) < 250)
                            f.playStageSound("mima_moon_active");
                    }

                    if (!gone[k] && now >= goneAt(k))
                    {
                        gone[k] = true;

                        if (now - goneAt(k) < 250)
                            f.playStageSound("yuyuko_petals");
                    }
                }
            }

            /// <summary>How far the moon has grown (0..1) and how far it has lit up, at <paramref name="t"/>.</summary>
            private void progress(int k, double t, out float grown, out float litUp)
            {
                grown = (float)Math.Clamp((t - marks[k]) / growMs, 0, 1);
                litUp = (float)Math.Clamp((t - litFrom(k)) / (beat * DanmakusuBalance.MIMA_LIGHT_BEATS), 0, 1);
            }

            /// <summary>The stars of a moon that is gone, a ring for each edge: piece 0 on its outer edge, flying outwards, piece 1 on the edge of its hollow, flying into it. False if there is no such moon.</summary>
            public bool Edges(double time, int piece, Vector2[] origins, Vector2[] directions)
            {
                for (int k = 0; k < marks.Length; k++)
                {
                    if (Math.Abs(goneAt(k) - time) > 1 || moons[k] is not Moon moon)
                        continue;

                    Vector2 hollow = hollowOf(moon);

                    for (int i = 0; i < origins.Length; i++)
                    {
                        Vector2 e = wayAt(moon, k, (i + 0.5f) / origins.Length);

                        if (piece == 0)
                        {
                            origins[i] = moon.Centre + e * moon.Radius;
                            directions[i] = e;
                        }
                        else
                        {
                            origins[i] = moon.Centre + e * hollowEnd(moon, e);
                            Vector2 into = hollow - origins[i];
                            directions[i] = into.LengthSquared > 1e-4f ? Vector2.Normalize(into) : -e;
                        }
                    }

                    return true;
                }

                return false;
            }

            /// <summary>0..1: the screen shakes while a moon grows.</summary>
            public float ShakeAt(double now)
            {
                for (int k = 0; k < marks.Length; k++)
                {
                    if (now >= marks[k] && now < litFrom(k))
                        return DanmakusuBalance.MIMA_SHAKE;
                }

                return 0f;
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                for (int k = 0; k < marks.Length; k++)
                {
                    if (moons[k] is not Moon moon || now < litFrom(k) || now >= goneAt(k))
                        continue;

                    progress(k, now, out _, out float litUp);
                    Vector2 p = f.characterPosition;
                    float reach = DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                    Vector2 fromMiddle = p - moon.Centre;

                    if (fromMiddle.Length > moon.Radius + reach || (p - hollowOf(moon)).Length < hollowRadius(moon) - reach)
                        continue;

                    // (only what has lit up hurts: how far round from the tip it started at the player is)
                    float angle = MathF.Atan2(moon.Back.X * fromMiddle.Y - moon.Back.Y * fromMiddle.X, Vector2.Dot(moon.Back, fromMiddle));
                    float u = (turnOf(k) * angle / tipAngle + 1f) * 0.5f;

                    if (u <= litUp)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < marks.Length; k++)
                {
                    if (moons[k] is not Moon moon || t < marks[k] || t >= goneAt(k) + fadeMs)
                        continue;

                    progress(k, t, out float grown, out float litUp);

                    // (its light reaches out twice as far and back in its last beat, then it fades out while its stars fly off)
                    double left = goneAt(k) - t;
                    float fade = left >= 0 ? 1f : 1f - smoothStep(-left / fadeMs);
                    float flash = left >= 0 && left < flashMs ? MathF.Sin((float)(1 - left / flashMs) * MathF.PI) : 0f;

                    // (what has lit up is solid; the rest that has grown is see-through)
                    if (litUp > 0f)
                        addShape(f, moon, k, 0f, litUp, fade, flash);

                    if (grown > litUp)
                        addShape(f, moon, k, litUp, grown, DanmakusuBalance.MIMA_GHOST_ALPHA * fade, 0f);
                }

                // (a flash for a moment where something has bounced off a moon: the trace tells the op where and when)
                foreach (var op in opsOf)
                {
                    if (op?.Bounces is not { Count: > 0 } bounces)
                        continue;

                    foreach (var bounce in bounces)
                    {
                        double age = t - bounce.Value.Time;

                        if (age > DanmakusuBalance.MIMA_FLASH_MS)
                        {
                            bounces.TryRemove(bounce.Key, out _);
                            continue;
                        }

                        if (age < 0)
                            continue;

                        float left = 1f - (float)(age / DanmakusuBalance.MIMA_FLASH_MS);
                        f.stageFront.Add(StageTexture.Glow, bounce.Value.Position, new Vector2(f.circleRadius * DanmakusuBalance.MIMA_FLASH_SIZE * (0.6f + 0.4f * left)), 0f, left, Color4.White, true);
                    }
                }
            }

            /// <summary>
            /// The moon from <paramref name="from"/> to <paramref name="to"/> of the way from tip to tip, as pieces side by side, none over another (so a see-through moon is evenly see-through): its white body between
            /// its outer edge and its hollow's, a thin grey line just outside both edges (thinning out at the tips, which stay sharp), and beyond that a soft white light, which reaches further as it flashes.
            /// </summary>
            private void addShape(BulletField f, Moon moon, int k, float from, float to, float alpha, float flash)
            {
                int last = DanmakusuBalance.MIMA_POINTS - 1;
                float a = from * last, b = to * last;
                var white = new Color4(1f, 1f, 1f, alpha);
                var grey = new Color4(0.55f, 0.55f, 0.58f, alpha);
                var light = new Color4(1f, 1f, 1f, DanmakusuBalance.MIMA_GLOW_ALPHA * alpha);
                float reach = DanmakusuBalance.MIMA_GLOW_REACH + (DanmakusuBalance.MIMA_FLASH_REACH - DanmakusuBalance.MIMA_GLOW_REACH) * flash;
                var none = new Color4(1f, 1f, 1f, 0f);
                Vector2 hollow = hollowOf(moon);

                (Vector2 Outer, Vector2 Inner, Vector2 Out, Vector2 In, float Line, float Glow) rimAt(float i)
                {
                    Vector2 e = wayAt(moon, k, i / last);
                    float inner = Math.Min(hollowEnd(moon, e), moon.Radius);
                    Vector2 innerAt = moon.Centre + e * inner;
                    Vector2 into = hollow - innerAt;
                    float half = (moon.Radius - inner) * 0.5f;

                    return (moon.Centre + e * moon.Radius, innerAt, e, into.LengthSquared > 1e-6f ? Vector2.Normalize(into) : -e,
                            DanmakusuBalance.MIMA_OUTLINE * Math.Min(half / (2f * DanmakusuBalance.MIMA_OUTLINE), 1f), half * reach);
                }

                var previous = rimAt(a);

                void piece(float i)
                {
                    var next = rimAt(i);
                    Vector2 outerLine0 = previous.Outer + previous.Out * previous.Line, outerLine1 = next.Outer + next.Out * next.Line;
                    Vector2 innerLine0 = previous.Inner + previous.In * previous.Line, innerLine1 = next.Inner + next.In * next.Line;

                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = previous.Inner, D = next.Inner, Near = white, Far = white });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = outerLine0, D = outerLine1, Near = grey, Far = grey });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Inner, B = next.Inner, C = innerLine0, D = innerLine1, Near = grey, Far = grey });

                    f.stageMeshes.Add(new MeshQuad { A = outerLine0, B = outerLine1, C = outerLine0 + previous.Out * previous.Glow, D = outerLine1 + next.Out * next.Glow, Near = light, Far = none });
                    f.stageMeshes.Add(new MeshQuad { A = innerLine0, B = innerLine1, C = innerLine0 + previous.In * previous.Glow, D = innerLine1 + next.In * next.Glow, Near = light, Far = none });

                    previous = next;
                }

                for (int i = (int)MathF.Floor(a) + 1; i < b; i++)
                    piece(i);

                piece(b);
            }
        }

        private readonly List<HalfMoonAct> halfMoonActs = new List<HalfMoonAct>();
    }
}
