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
        // Minoriko Aki (2026-11-28, "records all bullets positions at the moment every 16 beats, re shoot them as maples 16 beats later from that position (preview warning), they move at 0.25x speed.
        // Every 8 beats she shoots laserbeams like this, equal amount, equal angles and distributions, with preview too, stays for 8 beats, she also stays on place while shooting those laserbeams. All
        // leaves ricochet once from laserbeams" - with a picture of her beams, saved in Screenshots\2026-11-28 round 585).
        // Her own bullets are maple leaves (the planner's; her spinner's stars too).
        // Every MINORIKO_RECORD_EVERY_BEATS of her section she records where every bullet on the screen is and the way it flies (the rings' and the other acts', MINORIKO_MOST at most, spread evenly over
        // them); MINORIKO_RESHOOT_BEATS later a maple leaf flies from each of those places the way that bullet flew, MINORIKO_SLOW times as fast, standing there see-through and harmless for
        // MINORIKO_PREVIEW_BEATS before.
        // Every MINORIKO_BEAM_EVERY_BEATS of her attacks MINORIKO_PAIRS pairs of long straight beams come out of her all round (as the picture: the two of a pair MINORIKO_PAIR_DEGREES apart, the pairs
        // evenly round her, every next set turned half a step), see-through for MINORIKO_BEAM_PREVIEW_BEATS, then solid for MINORIKO_BEAM_BEATS, hurting like a slider body; she stands where she was while
        // a set is there (so all through her attacks, the next set coming before the last is gone).
        // Every maple leaf - hers, her spinner's, anyone's - bounces off her solid beams once, with a flash; what a ring shot flies on from there as a bullet of this act.
        // Where she is is taken as each set comes, and where the bullets are as each record is taken; going back in time takes them again from there.
        // ======================================================================================================================================================

        /// <summary>(the bench) How many leaves have bounced off Minoriko's beams: her own, and the rings'.</summary>
        public static int LeafBounces, RingLeafBounces;

        private sealed class HarvestAct : StageAct
        {
            private sealed class Set
            {
                public double T;
                public int Number;
                public Vector2 Centre;
                public bool Known;
            }

            private readonly double beat;
            private readonly double[] marks;
            private readonly CarriedBullet[]?[] taken;
            private readonly bool[] made;
            private readonly Set[] sets;

            // (the act's leaves - its own, and the rings' it has bounced - and which of them have bounced)
            private readonly List<int> leaves = new List<int>();
            private readonly HashSet<int> bounced = new HashSet<int>();
            private double checkedTo = double.NegativeInfinity;

            private readonly List<Vector2> line = new List<Vector2>(2);
            private readonly List<float> fades = new List<float>(2);

            public HarvestAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var times = new List<double>();

                for (double t = period.ShootStart; t + beat * DanmakusuBalance.MINORIKO_RESHOOT_BEATS <= period.End && times.Count < 2000; t += beat * DanmakusuBalance.MINORIKO_RECORD_EVERY_BEATS)
                    times.Add(t);

                marks = times.ToArray();
                taken = new CarriedBullet[]?[marks.Length];
                made = new bool[marks.Length];

                var list = new List<Set>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = Math.Max(start, period.ShootStart); t < end && list.Count < 4000; t += beat * DanmakusuBalance.MINORIKO_BEAM_EVERY_BEATS)
                        list.Add(new Set { T = t, Number = list.Count });
                }

                sets = list.ToArray();
                Start = period.ShootStart;
                End = period.End + beat * DanmakusuBalance.MINORIKO_RESHOOT_BEATS + leafLife(f);

                if (sets.Length > 0)
                {
                    AbsorbFrom = solidAt(sets[0]);
                    AbsorbTo = overAt(sets[^1]);
                }
            }

            private double solidAt(Set s) => s.T + beat * DanmakusuBalance.MINORIKO_BEAM_PREVIEW_BEATS;

            private double overAt(Set s) => solidAt(s) + beat * DanmakusuBalance.MINORIKO_BEAM_BEATS;

            private double shootAt(int k) => marks[k] + beat * DanmakusuBalance.MINORIKO_RESHOOT_BEATS;

            /// <summary>How long a slow leaf can be on the screen at the most.</summary>
            private double leafLife(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) / Math.Max(stageBulletSpeed(Period) * DanmakusuBalance.MINORIKO_SLOW, 1e-4f);

            private float half(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private static float beamLength(BulletField f) => Vector2.Distance(f.stageMin, f.stageMax) * 1.5f;

            /// <summary>Beam <paramref name="i"/> of a set: where it starts (a little out of her middle) and the way it goes - two of a pair a few degrees apart, the pairs evenly round her.</summary>
            private static void beam(Set s, int i, out Vector2 start, out Vector2 way)
            {
                int pairs = DanmakusuBalance.MINORIKO_PAIRS;
                float step = MathF.Tau / pairs;
                float angle = (s.Number % 2) * step * 0.5f + (i / 2) * step + (i % 2 == 0 ? -0.5f : 0.5f) * DanmakusuBalance.MINORIKO_PAIR_DEGREES * MathF.PI / 180f;
                way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                start = s.Centre + way * (DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 0.5f);
            }

            private static int beamCount => DanmakusuBalance.MINORIKO_PAIRS * 2;

            public override void Reset(BulletField f, double time)
            {
                foreach (var s in sets)
                {
                    if (s.T > time)
                        s.Known = false;
                }

                for (int k = 0; k < marks.Length; k++)
                {
                    if (marks[k] > time)
                        taken[k] = null;

                    made[k] = false; // (the leaves are made again from what was recorded)
                }

                ClearBullets();
                leaves.Clear();
                bounced.Clear();
                checkedTo = time;
            }

            /// <summary>She stands where she was while a set of her beams is there.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                foreach (var s in sets)
                {
                    if (s.T > now)
                        break;

                    if (s.Known && now < overAt(s))
                    {
                        position = s.Centre;
                        return true;
                    }
                }

                return false;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var s in sets)
                {
                    if (s.T > now)
                        break;

                    if (!s.Known)
                    {
                        s.Centre = f.bossPositionOf(Period); // (where she stands: the last set's place while that is still there)
                        s.Known = true;
                    }
                }

                // the records, and the leaves they shoot (made as the record is taken: see-through until they fly)
                double life = leafLife(f);

                for (int k = 0; k < marks.Length && marks[k] <= now; k++)
                {
                    // (taken as the mark comes; one passed over by a seek far ahead is not taken at all)
                    if (taken[k] == null && now - marks[k] <= beat * 2)
                        taken[k] = record(f);

                    if (made[k])
                        continue;

                    made[k] = true;

                    if (now < shootAt(k) + life)
                        shoot(f, k);
                }

                bounceLeaves(f, now);
                checkedTo = now;
            }

            /// <summary>Every bullet on the screen now, MINORIKO_MOST at most (spread evenly over them).</summary>
            private CarriedBullet[] record(BulletField f)
            {
                var all = f.bulletsOnScreen(this, 4000);

                if (all.Length <= DanmakusuBalance.MINORIKO_MOST)
                    return all;

                var some = new CarriedBullet[DanmakusuBalance.MINORIKO_MOST];

                for (int i = 0; i < some.Length; i++)
                    some[i] = all[(int)((long)i * all.Length / some.Length)];

                return some;
            }

            private void shoot(BulletField f, int k)
            {
                var recorded = taken[k];

                if (recorded == null)
                    return;

                double at = shootAt(k);
                float radius = f.stageBaseRadius(Period);
                float plain = stageBulletSpeed(Period);

                for (int i = 0; i < recorded.Length; i++)
                {
                    var b = recorded[i];
                    Vector2 v = b.Velocity * DanmakusuBalance.MINORIKO_SLOW;

                    if (v.LengthSquared < 1e-10f)
                    {
                        // (one that stood still flies out from her middle)
                        Vector2 out_ = b.Position - f.bossPositionOf(Period);
                        v = (out_.LengthSquared > 1f ? Vector2.Normalize(out_) : Vector2.UnitY) * plain * DanmakusuBalance.MINORIKO_SLOW;
                    }

                    int index = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = at - beat * DanmakusuBalance.MINORIKO_PREVIEW_BEATS,
                        P0 = b.Position,
                        V = Vector2.Zero,
                        Legs = new[] { new StageLeg { T = at, P = b.Position, V = v } },
                        Face = Vector2.Normalize(v),
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(k * 7 + i, true, Period.Rank),
                        Shape = BulletShape.Maple,
                        GhostUntil = at,
                        GhostVisible = 0.35f,
                        Untraced = true,
                        Until = at + f.stageLife(v),
                    });

                    if (Bullets.Count > index)
                        leaves.Add(index);
                }
            }

            /// <summary>Where does a step from <paramref name="a"/> to <paramref name="b"/> cross a solid beam (the first one found), and its line's normal?</summary>
            private bool crossing(BulletField f, double t, Vector2 a, Vector2 b, out Vector2 at, out Vector2 normal)
            {
                at = b;
                normal = Vector2.UnitX;
                float length = beamLength(f);

                foreach (var s in sets)
                {
                    if (s.T > t)
                        break;

                    if (!s.Known || t < solidAt(s) || t >= overAt(s))
                        continue;

                    for (int i = 0; i < beamCount; i++)
                    {
                        beam(s, i, out Vector2 start, out Vector2 way);
                        Vector2 n = new Vector2(-way.Y, way.X);
                        float da = Vector2.Dot(a - start, n), db = Vector2.Dot(b - start, n);

                        if ((da > 0f) == (db > 0f) || MathF.Abs(da - db) < 1e-6f)
                            continue;

                        float u = da / (da - db);
                        Vector2 there = a + (b - a) * u;
                        float along = Vector2.Dot(there - start, way);

                        if (along < 0f || along > length)
                            continue;

                        at = there;
                        normal = da > 0f ? n : -n; // (towards the side it came from)
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Her leaves that cross a solid beam since the last frame bounce off it, once.</summary>
            private void bounceLeaves(BulletField f, double now)
            {
                if (now <= checkedTo || sets.Length == 0)
                    return;

                float half_ = half(f);

                foreach (int index in leaves)
                {
                    if (bounced.Contains(index))
                        continue;

                    var b = Bullets[index];

                    if (b.Legs == null || now < b.Legs[0].T || now > b.Until)
                        continue;

                    double from = Math.Max(checkedTo, b.Legs[0].T);
                    Vector2 a = stageBulletPosition(b, from), c = stageBulletPosition(b, now);

                    if (!crossing(f, now, a, c, out Vector2 at, out Vector2 normal))
                        continue;

                    var leg = b.Legs[^1];
                    float u = Vector2.Distance(a, at) / Math.Max(Vector2.Distance(a, c), 1e-4f);
                    double when = from + (now - from) * u;
                    Vector2 v = leg.V - 2f * Vector2.Dot(leg.V, normal) * normal;
                    var legs = new StageLeg[b.Legs.Length + 1];
                    Array.Copy(b.Legs, legs, b.Legs.Length);
                    legs[^1] = new StageLeg { T = when, P = at + normal * (half_ + b.Radius * 0.5f), V = v };
                    b.Legs = legs;
                    b.Until = when + f.stageLife(v);
                    Bullets[index] = b;
                    bounced.Add(index);
                    LeafBounces++;
                    f.addFlash(when, at, b.Radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                }
            }

            /// <summary>A maple leaf of a ring that crosses one of her solid beams bounces off it and flies on as a bullet of this act (once: it does not bounce again).</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || (emitter.ShapeOf(bullet) != BulletShape.Maple && emitter.DrawShape != BulletShape.Maple) || emitter.Homing || emitter.Shape == BulletShape.Train)
                    return false;

                if (!crossing(f, t, from, to, out Vector2 at, out Vector2 normal))
                    return false;

                Vector2 seen = (to - from) / (float)Math.Max(t - ScanPrevious, 1e-3);

                if (seen.LengthSquared < 1e-10f)
                    return false;

                Vector2 v = seen - 2f * Vector2.Dot(seen, normal) * normal;
                var stored = f.makeStored(emitter, t);
                int index = Bullets.Count;
                AddBullet(f.makeStageBullet(stored, emitter, bullet, t, at + normal * half(f), v));

                if (Bullets.Count <= index)
                    return false;

                bounced.Add(index); // (once)
                RingLeafBounces++;
                f.addFlash(t, at, f.stageBaseRadius(Period) * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                return true;
            }

            /// <summary>Her beams: see-through, then solid, white with her gold.</summary>
            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                var core = new Color4(1f, 0.99f, 0.92f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.95f, 0.78f, 0.25f, 1f));
                float width = half(f), length = beamLength(f);

                foreach (var s in sets)
                {
                    if (s.T > t)
                        break;

                    if (!s.Known || t >= overAt(s))
                        continue;

                    bool solid = t >= solidAt(s);
                    float alpha = solid
                        ? 1f - smoothStep((t - (overAt(s) - beat * 0.25)) / (beat * 0.25))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - s.T) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int i = 0; i < beamCount; i++)
                    {
                        beam(s, i, out Vector2 start, out Vector2 way);
                        line.Clear();
                        fades.Clear();
                        line.Add(start);
                        line.Add(start + way * length);
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, width, core, border, ref beams, ref pointTotal, null, fades, null, solid ? 0.5f : 0f);
                    }
                }
            }

            /// <summary>Her solid beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float reach = half(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS, length = beamLength(f);

                foreach (var s in sets)
                {
                    if (s.T > now)
                        break;

                    if (!s.Known || now < solidAt(s) || now >= overAt(s))
                        continue;

                    for (int i = 0; i < beamCount; i++)
                    {
                        beam(s, i, out Vector2 start, out Vector2 way);

                        if (distanceToSegment(player, start, start + way * length) <= reach)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
