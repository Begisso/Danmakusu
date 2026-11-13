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
        // Yuki: in her attacks the laser bursts every boss had before Marisa's Master Spark: five beams evenly round her (three with another boss on the screen, two with two more), the first aimed at the player
        // when the attack begins, turning slowly together (as fast as a spinner's beams) so that the nearest comes round to the player; their slices fly out twice as fast as a plain bullet. But every slice goes
        // out (fading) as far from her as the player was when it was fired, and every third leaves a scorch mark where it went out: charred, glowing embers that burn for eight beats and hurt like a slider body.
        // The beams turn the way the player has gone, so it is all worked out step by step (a slice every 30 ms) and kept; time going back throws away what came after.
        // Her own shots, all through her section (in a spinner too): every four beats five rings of five ricochet stars, each from one of five places round her, two and a half circle radii away; a star ricochets
        // once off the edge of the screen and then flies out.
        // ======================================================================================================================================================

        private sealed class ScorchAct : StageAct
        {
            private struct Slice
            {
                public double Time;
                public Vector2 From;
                public float Angle; // (of the first beam)
                public float Reach; // (how far out it goes)
                public int Number;
            }

            private readonly double beat;
            private readonly List<(double Start, double End)> attacks;
            private readonly List<Slice> slices = new List<Slice>();
            private readonly double[] volleys;
            private readonly Vector2[] volleyFrom;
            private readonly bool[] volleyKnown;
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();
            private readonly List<(double Time, Vector2 Where)> bounces = new List<(double, Vector2)>();
            private int beamCount = DanmakusuBalance.SPINNER_LASER_BEAMS;
            private int sliceNumber;

            public ScorchAct(BulletField f, BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                this.attacks = attacks;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;

                var times = new List<double>();

                for (double at = period.ShootStart; at < period.End; at += beat * DanmakusuBalance.YUKI_STARS_EVERY_BEATS)
                    times.Add(at);

                volleys = times.ToArray();
                volleyFrom = new Vector2[volleys.Length];
                volleyKnown = new bool[volleys.Length];

                // (with another boss on the screen as many beams as any boss keeps then: half, or a third)
                int sharing = 1;

                foreach (var other in f.bosses)
                {
                    if (other != period && other.Rank != period.Rank && !other.Borrowed && other.ShootStart < period.End && other.End > period.ShootStart)
                        sharing = Math.Max(sharing, other.Rank + 1);
                }

                sharing = Math.Max(sharing, period.Rank + 1);

                if (sharing > 1)
                    beamCount = Math.Max(1, (DanmakusuBalance.SPINNER_LASER_BEAMS + sharing - 1) / sharing);
            }

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.YUKI_BEAM_SPEED;

            private float halfWidth => Math.Max(Period.StarRadius, 2f) * DanmakusuBalance.YUKI_BEAM_WIDTH;

            private double scorchMs => beat * DanmakusuBalance.YUKI_SCORCH_BEATS;

            /// <summary>The scorch marks of her last attack burn on after her section: she stays until they are out.</summary>
            public override double MechanicsEnd => attacks.Count > 0 ? Math.Max(Period.End, attacks[^1].End + scorchMs + 3000) : Period.End;

            private bool inAttack(double t)
            {
                foreach (var (start, end) in attacks)
                {
                    if (t >= start && t < end)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                while (slices.Count > 0 && slices[^1].Time > time)
                    slices.RemoveAt(slices.Count - 1);

                sliceNumber = slices.Count > 0 ? slices[^1].Number + 1 : 0;

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleys[k] > time)
                        volleyKnown[k] = false;
                }

                // (the stars are made again from where she was: they fly the same)
                ClearBullets();

                for (int k = 0; k < volleys.Length; k++)
                {
                    if (volleyKnown[k])
                        shootStars(f, k, false);
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                fire(f, now);

                for (int k = 0; k < volleys.Length && volleys[k] <= now; k++)
                {
                    if (volleyKnown[k])
                        continue;

                    volleyKnown[k] = true;
                    volleyFrom[k] = f.bossVisualOf(Period);
                    shootStars(f, k, true);
                }
            }

            // ---- the beams ----

            /// <summary>Fires the slices up to <paramref name="now"/>: from where she is, as far out as the player is, the beams turning a little towards the player.</summary>
            private void fire(BulletField f, double now)
            {
                double step = DanmakusuBalance.YUKI_SLICE_MS;
                Vector2 player = f.characterPosition;
                Vector2 from = f.bossVisualOf(Period);
                Vector2 toPlayer = player - from;
                float playerAngle = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                float reach = Math.Max(toPlayer.Length, f.circleRadius);
                float gap = MathF.Tau / beamCount;

                foreach (var (start, end) in attacks)
                {
                    if (now < start || end <= start)
                        continue;

                    double last = slices.Count > 0 && slices[^1].Time >= start ? slices[^1].Time : double.NaN;
                    float angle = double.IsNaN(last) ? playerAngle : slices[^1].Angle;
                    int guard = 0;

                    for (double t = double.IsNaN(last) ? start : last + step; t < Math.Min(now, end) + 1e-6 && guard++ < 2000; t += step)
                    {
                        if (!double.IsNaN(last) || t > start)
                        {
                            // (they turn together, the nearest beam towards the player, as fast as a spinner's beams)
                            float off = MathF.IEEERemainder(playerAngle - angle, gap);
                            float most = Period.AngularSpeed * (float)step;
                            angle += Math.Clamp(off, -most, most);
                        }

                        slices.Add(new Slice { Time = t, From = from, Angle = angle, Reach = reach, Number = sliceNumber++ });
                        last = t;
                    }
                }
            }

            /// <summary>The first slice that can still be seen or leave a mark at <paramref name="t"/> (they are kept in time order).</summary>
            private int firstShown(BulletField f, double t)
            {
                float diagonal = Vector2.Distance(f.stageMin, f.stageMax);
                double bound = t - scorchMs - diagonal / Math.Max(speed, 1e-3f) - 100;
                int low = 0, high = slices.Count;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (slices[middle].Time < bound)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            private double outAt(in Slice slice) => slice.Time + slice.Reach / Math.Max(speed, 1e-3f);

            private static Vector2 wayOf(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            // (the runs of a beam: drawn, or cut into the pieces that hurt)
            private bool drawing;
            private int drawnBeams, drawnTotal;
            private Color4 runColour, runBorder;
            private readonly List<(Vector2 A, Vector2 B)> hitSegments = new List<(Vector2, Vector2)>();

            /// <summary>
            /// Beam <paramref name="beam"/> at <paramref name="t"/>, from her out: every run of slices that are still flying (a slice that has gone out cuts it, when an older one fired further still flies on) is drawn,
            /// or its pieces are kept for the hit check.
            /// </summary>
            private void beamAt(BulletField f, int beam, double t, int from)
            {
                line.Clear();
                fades.Clear();

                float gap = MathF.Tau / beamCount;
                float fadeLength = Math.Max(halfWidth * 2f, 12f);
                bool started = false;

                for (int i = slices.Count - 1; i >= from; i--)
                {
                    var slice = slices[i];

                    if (slice.Time > t)
                        continue;

                    float flown = speed * (float)(t - slice.Time);

                    if (flown >= slice.Reach)
                    {
                        runDone(f);
                        continue;
                    }

                    // (the newest run starts at her)
                    if (!started)
                    {
                        line.Add(slice.From);
                        fades.Add(1f);
                        started = true;
                    }

                    line.Add(slice.From + wayOf(slice.Angle + beam * gap) * flown);
                    fades.Add(smoothStep((slice.Reach - flown) / fadeLength));
                }

                runDone(f);
            }

            private void runDone(BulletField f)
            {
                if (line.Count >= 2)
                {
                    if (drawing)
                    {
                        f.addPlainBeam(line, halfWidth, runColour, runBorder, ref drawnBeams, ref drawnTotal);

                        for (int i = 0; i < fades.Count; i++)
                            f.laserFadePointBuffer[drawnTotal - line.Count + i] = fades[i];
                    }
                    else
                    {
                        for (int i = 0; i + 1 < line.Count; i++)
                        {
                            if (fades[i] > 0.5f && fades[i + 1] > 0.5f)
                                hitSegments.Add((line[i], line[i + 1]));
                        }
                    }
                }

                line.Clear();
                fades.Clear();
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0 || slices.Count == 0)
                    return;

                int style = f.comboStyle(1, true, Period.Rank);
                runColour = f.palette[style % f.palette.Length];
                runBorder = f.borderPalette[style % f.borderPalette.Length];
                drawing = true;
                drawnBeams = beams;
                drawnTotal = pointTotal;

                int from = firstShown(f, t);

                for (int beam = 0; beam < beamCount; beam++)
                    beamAt(f, beam, t, from);

                beams = drawnBeams;
                pointTotal = drawnTotal;
                drawing = false;
            }

            // ---- the scorch marks ----

            /// <summary>How much a scorch mark left by <paramref name="slice"/> burns at <paramref name="t"/> (0 = not there).</summary>
            private float burning(in Slice slice, double t)
            {
                if (slice.Number % DanmakusuBalance.YUKI_SCORCH_EVERY != 0)
                    return 0f;

                double age = t - outAt(slice);

                if (age < 0 || age > scorchMs)
                    return 0f;

                return Math.Min(smoothStep(age / 120.0), 1f - smoothStep((age - (scorchMs - beat)) / beat));
            }

            private Vector2 markOf(in Slice slice, int beam) => slice.From + wayOf(slice.Angle + beam * MathF.Tau / beamCount) * slice.Reach;

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || slices.Count == 0)
                    return;

                float size = halfWidth * DanmakusuBalance.YUKI_SCORCH_SIZE * 2f;
                var char_colour = new Color4(0.12f, 0.06f, 0.03f, 1f);
                var ember = new Color4(1f, 0.42f, 0.08f, 1f);

                for (int i = firstShown(f, t); i < slices.Count; i++)
                {
                    var slice = slices[i];
                    float level = burning(slice, t);

                    if (level < 0.01f)
                        continue;

                    for (int beam = 0; beam < beamCount; beam++)
                    {
                        Vector2 at = markOf(slice, beam);
                        float flicker = 0.75f + 0.25f * MathF.Sin((float)(t * 0.013) + slice.Number * 1.7f + beam * 2.3f);

                        f.stageBack.Add(StageTexture.Glow, at, new Vector2(size * 1.25f), 0f, 0.7f * level, char_colour);
                        f.stageBack.Add(StageTexture.Glow, at, new Vector2(size * 0.8f), 0f, 0.55f * level * flicker, ember, true);
                    }
                }
            }

            /// <summary>The beams and the scorch marks hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || slices.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                int from = firstShown(f, now);

                // the marks
                float markHit = halfWidth * DanmakusuBalance.YUKI_SCORCH_SIZE * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = from; i < slices.Count; i++)
                {
                    var slice = slices[i];

                    if (burning(slice, now) < 0.5f)
                        continue;

                    for (int beam = 0; beam < beamCount; beam++)
                    {
                        if (Vector2.DistanceSquared(player, markOf(slice, beam)) <= markHit * markHit)
                            return true;
                    }
                }

                // the beams
                float hit = halfWidth * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                hitSegments.Clear();

                for (int beam = 0; beam < beamCount; beam++)
                    beamAt(f, beam, now, from);

                foreach (var (a, b) in hitSegments)
                {
                    if (distanceToSegment(player, a, b) <= hit)
                        return true;
                }

                return false;
            }

            // ---- her stars ----

            /// <summary>Volley <paramref name="k"/>: from five places round where she was, a ring of five ricochet stars each, flying out from her; each ricochets once and then flies out of the screen.</summary>
            private void shootStars(BulletField f, int k, bool flash)
            {
                double t = volleys[k];
                float radius = Math.Max(Period.StarRadius, 2f);
                float starSpeed = stageBulletSpeed(Period);
                float place = f.circleRadius * DanmakusuBalance.YUKI_STAR_PLACE_CIRCLES;
                float turn = k * 2.3999632f;

                for (int p = 0; p < DanmakusuBalance.YUKI_STAR_PLACES; p++)
                {
                    float placeAngle = turn + p * MathF.Tau / DanmakusuBalance.YUKI_STAR_PLACES;
                    Vector2 at = volleyFrom[k] + wayOf(placeAngle) * place;

                    for (int s = 0; s < DanmakusuBalance.YUKI_STARS; s++)
                    {
                        Vector2 velocity = wayOf(placeAngle + s * MathF.Tau / DanmakusuBalance.YUKI_STARS) * starSpeed;
                        bounces.Clear();
                        var legs = f.bouncingLegs(t, at, velocity, 1, bounces);
                        var lastLeg = legs[^1];
                        double gone = lastLeg.T + f.toWall(lastLeg.P, lastLeg.V, out _) + radius * 3f / Math.Max(starSpeed, 1e-3f);

                        AddBullet(new StageBullet
                        {
                            T0 = t,
                            P0 = at,
                            V = velocity,
                            Legs = legs,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = f.comboStyle(p + s, true, Period.Rank),
                            Shape = BulletShape.Star,
                            Spin = 0.004f * (s % 2 == 0 ? 1f : -1f),
                            SpinPhase = s,
                            Until = gone,
                        });

                        if (flash)
                        {
                            foreach (var (time, where) in bounces)
                                f.addFlash(time, where, radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);
                        }
                    }
                }
            }
        }
    }
}
