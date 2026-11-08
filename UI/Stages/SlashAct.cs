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
        // Youmu Konpaku: in her attacks she watches every sixteenth bullet on the screen. Every two beats of an attack eighteen thin slashes go through them, two by two, each as different from the ones before it as the bullets allow (what lies at the edge of the screen does not count), across the whole screen: straight beams, see-through and harmless for
        // four beats, then solid and twice as thick for two, hurting like a slider body, then fading out. A slash that turns solid cuts the bullets across it in two (see CuttingAct); where it crosses the beam of a slider it pokes a hole in it, and the slices it cuts out fall like the
        // petals of a tree. She herself throws rings of beams, as Luna Child does, all the time her section lasts, in a spinner too; hers are straight and fly straight out of her, end first.
        // ======================================================================================================================================================

        private sealed class SlashAct : CuttingAct
        {
            private sealed class Slash
            {
                public double Time;
                public Vector2 A, B;
                public Vector2 Normal; // (across it; with Off how far a place is from it: it runs off the screen at both ends, so on the screen it is a line)
                public float Off;
                public bool Cut;

                public float Distance(Vector2 place) => MathF.Abs(Vector2.Dot(place, Normal) - Off);
            }

            /// <summary>A line through two bullets, by its angle (0..pi) and how far it passes from the middle of the screen; and how like it is to the ones chosen already.</summary>
            private struct LineThrough
            {
                public Vector2 First, Second;
                public float Angle, Off, Nearest;
            }

            private readonly double beat;
            private readonly double[] slashTimes;
            private readonly double[] rays;
            private readonly Vector2[] rayCentres;
            private readonly bool[] rayKnown;
            private readonly List<(float Distance, Vector2 Place)> watched = new List<(float, Vector2)>();
            private readonly List<LineThrough> lines = new List<LineThrough>();
            private readonly List<Slash> slashes = new List<Slash>();
            private readonly List<Vector2> line = new List<Vector2>();
            private int nextSlash, nextCut;
            private double soundedAt = double.NegativeInfinity; // (the slashes of one mark turn solid together, and sound once)

            public SlashAct(BossPeriod period, double[] slashTimes, double[] rays)
            {
                Period = period;
                this.slashTimes = slashTimes;
                this.rays = rays;
                rayCentres = new Vector2[rays.Length];
                rayKnown = new bool[rays.Length];
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End + beat * (DanmakusuBalance.YOUMU_SLASH_GHOST_BEATS + DanmakusuBalance.YOUMU_SLASH_SOLID_BEATS);
            }

            // (only when a slash turns solid: the rest of the time nothing is asked for every bullet at every step)
            public override bool Absorbs(double t) => AbsorbsBetween(t, t);

            public override bool AbsorbsBetween(double from, double to)
            {
                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    double solid = solidAt(slashes[i]);

                    if (solid <= to && solid + DanmakusuBalance.CLAW_CUT_MS > from)
                        return true;

                    if (solid + DanmakusuBalance.CLAW_CUT_MS <= from)
                        break;
                }

                return false;
            }

            /// <summary>A slash that has just turned solid cuts every bullet that lies across it, along it.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                float half = slashWidth(f) * DanmakusuBalance.YOUMU_SLASH_THICK;

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];
                    double solid = solidAt(slash);

                    if (t < solid)
                        continue;

                    if (t >= solid + DanmakusuBalance.CLAW_CUT_MS)
                        break;

                    float off = Vector2.Dot(to, slash.Normal) - slash.Off;

                    if (MathF.Abs(off) > half + SizeOf(emitter, bullet))
                        continue;

                    CutBullet(f, t, from, to, emitter, bullet, slash.Normal, -off);
                    return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (!f.failed)
                    DrawPieces(f, t);
            }

            private double solidAt(Slash slash) => slash.Time + beat * DanmakusuBalance.YOUMU_SLASH_GHOST_BEATS;

            private double fadesAt(Slash slash) => solidAt(slash) + beat * DanmakusuBalance.YOUMU_SLASH_SOLID_BEATS;

            private double goneAt(Slash slash) => fadesAt(slash) + beat * DanmakusuBalance.YOUMU_SLASH_FADE_BEATS;

            private float width(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private float slashWidth(BulletField f) => width(f) * DanmakusuBalance.YOUMU_SLASH_WIDTH;

            private float rayLength(BulletField f) => f.circleRadius * DanmakusuBalance.YOUMU_RAY_CIRCLES;

            private float raySpeed => stageBulletSpeed(Period) * DanmakusuBalance.YOUMU_RAY_SPEED;

            private double rayLife(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) + rayLength(f)) / raySpeed;

            private static float rayPhase(int ring) => ring * 0.17f;

            private static float rayStep => MathF.Tau / DanmakusuBalance.YOUMU_RAYS;

            public override void Reset(BulletField f, double time)
            {
                slashes.Clear();
                Array.Clear(rayKnown, 0, rayKnown.Length);
                nextSlash = 0;
                nextCut = 0;
                soundedAt = double.NegativeInfinity;
                ClearPieces();
            }

            /// <summary>Has a slash that has turned solid by <paramref name="time"/> cut out the slice (see <see cref="IBeamBlasts"/>)? A slice can only have been cut after it was fired.</summary>
            public bool Blasted(Vector2 start, Vector2 velocity, double fired, double time)
            {
                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];
                    double cut = solidAt(slash);

                    if (cut < fired)
                        break;

                    if (cut <= time && slash.Cut && slash.Distance(start + velocity * (float)(cut - fired)) <= cutReach)
                        return true;
                }

                return false;
            }

            private float cutReach; // (how far from a slash a slice is cut out)

            /// <summary>
            /// The slashes of <paramref name="mark"/>: each goes through two bullets, of every sixteenth bullet (by the ring's time as well as its number, as rings can be smaller than that), far enough from each other to draw a line through; what lies at the edge is left out. The first goes through the two nearest to the
            /// middle, and every next one is the line least like any chosen so far, by its angle and by how far it passes from the middle, so that they spread over the screen instead of all crossing in its middle.
            /// </summary>
            private void pick(BulletField f, double mark)
            {
                Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                Vector2 inner = (f.stageMax - f.stageMin) * (0.5f * DanmakusuBalance.YOUMU_INNER);

                watched.Clear();

                for (int k = 0; k < f.activeCount; k++)
                {
                    var emitter = f.emitters[f.active[k]];

                    if (emitter.Retired || emitter.Count == 0 || emitter.Homing || emitter.Shape == BulletShape.Train || emitter.Time > mark)
                        continue;

                    ulong skip = emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask;
                    double e = emitter.WithoutFrozenTime(f.effects.Effective(emitter, mark));

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        if ((skip & (1UL << b)) != 0 || (Math.Abs((long)emitter.Time) + b) % DanmakusuBalance.YOUMU_EVERY != 0)
                            continue;

                        emitter.Evaluate(b, e, out Vector2 position, out _, out bool there);

                        Vector2 offset = position - middle;

                        if (there && Math.Abs(offset.X) <= inner.X && Math.Abs(offset.Y) <= inner.Y)
                            watched.Add((offset.LengthSquared, position));
                    }
                }

                watched.Sort((a, c) => a.Distance.CompareTo(c.Distance));

                float apart = f.circleRadius * DanmakusuBalance.YOUMU_APART_CIRCLES;
                float scale = Vector2.Distance(f.stageMin, f.stageMax) * 0.5f;
                int looked = Math.Min(watched.Count, DanmakusuBalance.YOUMU_WATCHED_MOST);

                lines.Clear();

                for (int i = 0; i < looked; i++)
                {
                    for (int k = i + 1; k < looked; k++)
                    {
                        Vector2 first = watched[i].Place, second = watched[k].Place;

                        if (Vector2.DistanceSquared(first, second) < apart * apart)
                            continue;

                        Vector2 way = (second - first).Normalized();
                        float angle = MathF.Atan2(way.Y, way.X);
                        float off = Vector2.Dot(first - middle, new Vector2(-way.Y, way.X));

                        // (the same line, the other way round)
                        if (angle < 0f)
                        {
                            angle += MathF.PI;
                            off = -off;
                        }

                        lines.Add(new LineThrough { First = first, Second = second, Angle = angle, Off = off, Nearest = float.MaxValue });
                    }
                }

                float unlike(in LineThrough a, in LineThrough b)
                {
                    float turned = MathF.Abs(a.Angle - b.Angle), bOff = b.Off;

                    if (turned > MathF.PI / 2f)
                    {
                        turned = MathF.PI - turned;
                        bOff = -bOff;
                    }

                    turned /= MathF.PI / 2f;
                    float aside = (a.Off - bOff) / scale;

                    return turned * turned + aside * aside;
                }

                int next = lines.Count > 0 ? 0 : -1;

                for (int made = 0; made <= DanmakusuBalance.YOUMU_MORE_SLASHES && next >= 0; made++)
                {
                    var chosen = lines[next];

                    // (the line goes on past both bullets, off the screen at both ends)
                    Vector2 along = (chosen.Second - chosen.First).Normalized();
                    Vector2 far = along * Vector2.Distance(f.stageMin, f.stageMax);
                    Vector2 across = new Vector2(-along.Y, along.X);

                    slashes.Add(new Slash { Time = mark, A = chosen.First - far, B = chosen.Second + far, Normal = across, Off = Vector2.Dot(chosen.First, across) });

                    next = -1;
                    float best = DanmakusuBalance.YOUMU_LINES_APART * DanmakusuBalance.YOUMU_LINES_APART;

                    for (int c = 0; c < lines.Count; c++)
                    {
                        var line = lines[c];
                        line.Nearest = Math.Min(line.Nearest, unlike(line, chosen));
                        lines[c] = line;

                        if (line.Nearest > best)
                        {
                            best = line.Nearest;
                            next = c;
                        }
                    }
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                cutReach = slashWidth(f) * DanmakusuBalance.YOUMU_SLASH_THICK;

                while (nextSlash < slashTimes.Length && slashTimes[nextSlash] <= now)
                {
                    pick(f, slashTimes[nextSlash]);
                    nextSlash++;
                }

                // (the slashes of one mark turn solid together: the beams are looked at once for all of them)
                while (nextCut < slashes.Count && solidAt(slashes[nextCut]) <= now)
                {
                    int first = nextCut;
                    double at = solidAt(slashes[first]);

                    while (nextCut < slashes.Count && slashes[nextCut].Time == slashes[first].Time)
                        slashes[nextCut++].Cut = true;

                    cutBeams(f, first, nextCut, at);

                    if (at > soundedAt)
                    {
                        soundedAt = at;

                        if (now - soundedAt < 250)
                            f.playStageSound("touhou_strike");
                    }
                }

                for (int k = 0; k < rays.Length && rays[k] <= now; k++)
                {
                    if (!rayKnown[k])
                    {
                        rayKnown[k] = true;
                        rayCentres[k] = f.bossVisualOf(Period);
                    }
                }
            }

            /// <summary>What the slashes of a mark have cut out of the slider beams falls like petals.</summary>
            private void cutBeams(BulletField f, int firstSlash, int endSlash, double at)
            {
                float reach = cutReach;
                double step = DanmakusuBalance.FLANDRE_SLICE_MS;
                Vector2 low = f.stageMin, high = f.stageMax;

                foreach (var laser in f.lasers)
                {
                    if (laser.Rotates || laser.StartTime > at || at > laser.EndTime + laser.Lifetime)
                        continue;

                    double newest = Math.Min(at, laser.EndTime);
                    double oldest = Math.Max(laser.StartTime, at - laser.Lifetime);
                    double first = laser.StartTime + Math.Ceiling((oldest - laser.StartTime) / step - 1e-9) * step;

                    for (double fired = first; fired <= newest; fired += step)
                    {
                        for (int beam = 0; beam < laser.BeamCount; beam++)
                        {
                            Vector2 position = laser.SlicePosition(fired, fired, beam) + laser.Directions[beam] * (laser.Speed * (float)(at - fired));

                            if (position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y)
                                continue;

                            bool across = false;

                            for (int s = firstSlash; s < endSlash && !across; s++)
                                across = slashes[s].Distance(position) <= reach;

                            if (!across)
                                continue;

                            float radius = Math.Max(laser.HalfWidthAtAge(at - fired), f.circleRadius * 0.12f);
                            float fall = (float)(f.stageMax.Y - position.Y);
                            float pull = DanmakusuBalance.ZANMU_GRAVITY / DanmakusuBalance.YOUMU_PETAL_FALL;

                            AddBullet(new StageBullet
                            {
                                T0 = at,
                                P0 = position,
                                Acc = new Vector2(0f, pull),
                                Radius = radius,
                                Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                Style = f.comboStyle(laser.ComboIndex, laser.FollowsBoss, laser.BossRank),
                                Shape = BulletShape.Petal,
                                Tint = BulletRenderer.TINT_PINK,
                                TintAmount = 1f,
                                Wander = f.circleRadius * DanmakusuBalance.SHIZUHA_SWAY_CIRCLES,
                                WanderPhase = hash01(beam, (int)fired, 91) * MathF.Tau,
                                Until = at + Math.Sqrt(2.0 * Math.Max(fall, 1f) / pull) + 300,
                            });
                        }
                    }
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float half = width(f), thin = slashWidth(f);

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];

                    if (t > goneAt(slash))
                        break;

                    if (t < slash.Time)
                        continue;

                    float alpha = t < solidAt(slash) ? DanmakusuBalance.YOUMU_GHOST_ALPHA : t < fadesAt(slash) ? 1f : 1f - smoothStep((t - fadesAt(slash)) / (goneAt(slash) - fadesAt(slash)));

                    line.Clear();
                    line.Add(slash.A);
                    line.Add(slash.B);
                    f.addPlainBeam(line, t < solidAt(slash) ? thin : thin * DanmakusuBalance.YOUMU_SLASH_THICK, colour, border, ref beams, ref pointTotal);

                    if (alpha < 0.999f)
                        Array.Fill(f.laserFadePointBuffer, alpha, pointTotal - 2, 2);
                }

                double life = rayLife(f);
                float length = rayLength(f);

                for (int k = rays.Length - 1; k >= 0; k--)
                {
                    double age = t - rays[k];

                    if (age < 0 || !rayKnown[k])
                        continue;

                    if (age > life)
                        break;

                    float near = raySpeed * (float)age;

                    for (int i = 0; i < DanmakusuBalance.YOUMU_RAYS; i++)
                    {
                        float angle = rayPhase(k) + i * rayStep;
                        Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        line.Clear();
                        line.Add(rayCentres[k] + way * Math.Max(near - length, 0f));
                        line.Add(rayCentres[k] + way * near);
                        f.addPlainBeam(line, half, colour, border, ref beams, ref pointTotal);
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float half = width(f);
                float hit = half * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float slashHit = slashWidth(f) * DanmakusuBalance.YOUMU_SLASH_THICK * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];

                    if (now >= fadesAt(slash))
                        break;

                    if (now >= solidAt(slash) && slash.Distance(f.characterPosition) <= slashHit)
                        return true;
                }

                double life = rayLife(f);
                float length = rayLength(f);

                for (int k = rays.Length - 1; k >= 0; k--)
                {
                    double age = now - rays[k];

                    if (age < 0 || !rayKnown[k])
                        continue;

                    if (age > life)
                        break;

                    Vector2 offset = f.characterPosition - rayCentres[k];
                    float distance = offset.Length;
                    float near = raySpeed * (float)age;

                    if (distance > near + hit || distance < near - length - hit)
                        continue;

                    float angle = (MathF.Atan2(offset.Y, offset.X) - rayPhase(k)) % rayStep;

                    if (angle < 0f)
                        angle += rayStep;

                    if (Math.Min(angle, rayStep - angle) * distance <= hit)
                        return true;
                }

                return false;
            }
        }

        private readonly List<SlashAct> slashActs = new List<SlashAct>();
    }
}
