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
        // Kagerou Imaizumi: no pauses. When her section begins, and every sixteen beats of it, she howls and throws herself (as fast as Chen) at where the player was at that moment, and stays there for eight beats. While she flies and, half as fast, while she stays
        // does she shoot: rings of bright white bullets, twice as fast as any. While she stays, every beat she claws at the player: three thin arcs side by side round her, as far from her as the player is (they reach anywhere), turned 45 degrees round the player one way and the next claw the other, so that they cross. A claw
        // comes as Mima's moon comes: it grows, faint and harmless, from one tip to the other in half a beat and stays so; two beats after it began it lights up the same way round in a quarter of a beat, hurts like a slider body for a beat and fades out.
        // Elly: the same claw, one big thin arc at half the playfield's height from her, everything twice as slow. She slides after the player until they are within reach, and on the next two beats' mark she stops for a blow: the arc grows as the warning, harmless, from tip to tip as fast as it will light up, and stays so for the rest of the two beats; no scythe is to be seen. The blow itself is the arc lighting up:
        // her scythe appears, the tip of its blade at the end of what is lit, and goes from tip to tip the way the arc grew, at one speed, and fades. Only then does she go on, and what has lit up stays and hurts like a slider body; the scythe itself is harmless. She shoots bats that do not home (BatAct).
        // When a claw lights up it cuts what is in it at that moment in two, along the arc (see CuttingAct), any bullet, and any beam (but Marisa's).
        // ======================================================================================================================================================

        private sealed class ClawAct : CuttingAct
        {
            private sealed class Jump
            {
                public double Start, Arrive, StayUntil;
                public Vector2 From, To;
                public int Rings, Slashes;
            }

            private sealed class Slash
            {
                public double Time;
                public Vector2 Centre;
                public float Angle, Radius, Turn;
                public bool Active;
                public bool Heard; // (it swings aloud when it lights up, and at no other moment)
            }

            private readonly bool wolf;
            private readonly double beat;
            private readonly double slower;
            private readonly double[] marks;
            private readonly List<Jump> jumps = new List<Jump>();
            private readonly List<Slash> slashes = new List<Slash>();
            private readonly List<(Vector2 Velocity, double From, double Lit)> beamCuts = new List<(Vector2, double, double)>(); // (the parts of beams that fly back: the beam's way, the oldest slice of the part, and when; in the order they were cut)
            private readonly double finish;
            private int nextMark, slashCount;

            // (Elly's slide)
            private bool known;
            private Vector2 slide;
            private double slideTime;

            public ClawAct(BossPeriod period, bool wolf)
            {
                Period = period;
                this.wolf = wolf;
                beat = Math.Max(period.BeatLength, 1);
                slower = wolf ? 1 : DanmakusuBalance.ELLY_SLOWER;

                double every = beat * (wolf ? DanmakusuBalance.KAGEROU_EVERY_BEATS : DanmakusuBalance.ELLY_SLASH_EVERY_BEATS);
                var list = new List<double>();

                for (double t = period.ShootStart; t < period.End - 1; t += every)
                    list.Add(t);

                marks = list.ToArray();
                Start = period.ShootStart;

                // (her last claws go through to their end, also after her section: she stays until they have faded; Kagerou's flight is over within two beats)
                double last = marks.Length > 0 ? marks[^1] : period.ShootStart;
                finish = last + (wolf ? beat * (2 + DanmakusuBalance.KAGEROU_STAY_BEATS) : 0) + lifeMs;
                End = Math.Max(finish, period.End);
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            public override double MechanicsEnd => finish;

            private double growMs => beat * (wolf ? DanmakusuBalance.KAGEROU_GROW_BEATS : DanmakusuBalance.CLAW_GROW_BEATS * slower);

            // (Elly's scythe goes along with what lights up, tip to tip, at the speed of its swing)
            private double lightMs => beat * (wolf ? DanmakusuBalance.CLAW_LIGHT_BEATS : 2.0 * DanmakusuBalance.ELLY_ARC_DEGREES / DanmakusuBalance.ELLY_SWING_DEGREES_PER_BEAT);

            private double solidMs => beat * (wolf ? DanmakusuBalance.KAGEROU_SOLID_BEATS : DanmakusuBalance.CLAW_SOLID_BEATS * slower);

            private double fadeMs => beat * DanmakusuBalance.CLAW_FADE_BEATS * slower;

            private double lifeMs => growMs + solidMs + fadeMs;

            private double litAt(Slash slash) => slash.Time + growMs;

            private double goneAt(Slash slash) => slash.Time + growMs + solidMs;

            private int arcs => wolf ? DanmakusuBalance.KAGEROU_ARCS : 1;

            private float span => (wolf ? DanmakusuBalance.KAGEROU_ARC_DEGREES : DanmakusuBalance.ELLY_ARC_DEGREES) * MathF.PI / 180f;

            private float halfWidth(BulletField f) => f.circleRadius * (wolf ? DanmakusuBalance.KAGEROU_ARC_HALF_WIDTH_CIRCLES : DanmakusuBalance.ELLY_ARC_HALF_WIDTH_CIRCLES);

            private float reachOfElly(BulletField f) => (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.ELLY_RADIUS;

            private Color4 lightColour => wolf ? new Color4(1f, 0.32f, 0.28f, 1f) : new Color4(1f, 0.78f, 0.32f, 1f);

            /// <summary>A claw narrows to a point at both its tips.</summary>
            private static float taper(float u) => MathF.Pow(Math.Max(MathF.Sin(MathF.PI * Math.Clamp(u, 0f, 1f)), 0f), 0.7f);

            private float arcRadius(BulletField f, Slash slash, int arc) => slash.Radius + (arc - (arcs - 1) * 0.5f) * f.circleRadius * DanmakusuBalance.KAGEROU_ARC_GAP_CIRCLES;

            /// <summary>Is <paramref name="point"/> in one of the arcs (give or take <paramref name="extra"/>), and how far (0..1) from the tip the claw starts at?</summary>
            private bool inBody(BulletField f, Slash slash, Vector2 point, float extra, out float u)
            {
                Vector2 off = point - slash.Centre;
                float distance = off.Length;

                u = 0f;

                // (nowhere near any of its arcs: this is asked for every slice of every beam every frame, so the angle is not worked out for what is clearly not in it)
                float widest = halfWidth(f) + extra;

                if (distance < arcRadius(f, slash, 0) - widest || distance > arcRadius(f, slash, arcs - 1) + widest)
                    return false;

                float angle = MathF.IEEERemainder(MathF.Atan2(off.Y, off.X) - slash.Angle, MathF.Tau);

                u = (slash.Turn * angle / span + 1f) * 0.5f;

                if (MathF.Abs(angle) > span)
                    return false;

                float half = halfWidth(f) * taper(u) + extra;

                for (int arc = 0; arc < arcs; arc++)
                {
                    if (MathF.Abs(distance - arcRadius(f, slash, arc)) <= half)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearPieces();
                known = false;
                beamCuts.RemoveAll(cut => cut.Lit > time);

                while (jumps.Count > 0 && jumps[^1].Start > time)
                    jumps.RemoveAt(jumps.Count - 1);

                while (slashes.Count > 0 && slashes[^1].Time > time)
                    slashes.RemoveAt(slashes.Count - 1);

                nextMark = 0;

                while (nextMark < marks.Length && marks[nextMark] <= time)
                    nextMark++;

                foreach (var slash in slashes)
                    slash.Active = litAt(slash) <= time;
            }

            /// <summary>Who is about to be cut: the claws light up here, before anything moves, so that what they cut is what is in them at that moment.</summary>
            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                if (wolf)
                    updateWolf(f, now);
                else
                    updateElly(f, now);

                foreach (var slash in slashes)
                {
                    double lit = litAt(slash);

                    if (slash.Active || lit > now)
                        continue;

                    slash.Active = true;
                    cutOwn(f, slash, lit);
                    cutBeams(f, slash, lit);

                    if (slash.Heard && now - lit < 250)
                        f.playStageSound(wolf ? nextSwing() : "elly_swing");
                }
            }

            private int lastSwing = -1, swingsHeard;

            /// <summary>Kagerou's swing: one of the user's recorded swings (the first four seconds of "Various Swing Sound Effect", cut one by one), in an order of its own, never the same twice in a row.</summary>
            private string nextSwing()
            {
                int count = DanmakusuBalance.KAGEROU_SWINGS;
                int pick = (int)(hash01(swingsHeard++, Period.Rank, 41) * (count - 1));

                // (one of the others: the one just before is skipped)
                if (lastSwing >= 0 && pick >= lastSwing)
                    pick++;

                lastSwing = Math.Clamp(pick, 0, count - 1);
                return $"kagerou_swing_{lastSwing}";
            }

            private void updateWolf(BulletField f, double now)
            {
                while (nextMark < marks.Length && marks[nextMark] <= now)
                {
                    double start = marks[nextMark++];
                    Vector2 from = jumps.Count > 0 ? jumps[^1].To : f.bossVisualOf(Period);
                    Vector2 to = Vector2.Clamp(f.characterPosition, f.stageMin, f.stageMax);
                    float speed = stageBulletSpeed(Period) * DanmakusuBalance.KAGEROU_MOVE_SPEED;
                    double arrive = start + Vector2.Distance(from, to) / speed;

                    jumps.Add(new Jump { Start = start, Arrive = arrive, StayUntil = arrive + beat * DanmakusuBalance.KAGEROU_STAY_BEATS, From = from, To = to });

                    if (now - start < 250)
                        f.playStageSound("kagerou_howl");
                }

                foreach (var jump in jumps)
                {
                    // (she shoots while she flies and while she stays)
                    double every = beat * DanmakusuBalance.KAGEROU_RING_EVERY_BEATS;

                    for (double t = jump.Start + jump.Rings * every; t <= now && t < jump.StayUntil; t = jump.Start + jump.Rings * every)
                    {
                        fireRing(f, jump, t);
                        jump.Rings++;
                    }

                    for (double t = jump.Arrive + jump.Slashes * beat; t <= now && t < jump.StayUntil; t = jump.Arrive + jump.Slashes * beat)
                    {
                        addSlash(f, t, jump.To);
                        jump.Slashes++;
                    }
                }
            }

            private void updateElly(BulletField f, double now)
            {
                if (!known)
                {
                    // (after a seek she goes on from where her last blow fell)
                    slide = slashes.Count > 0 ? slashes[^1].Centre : f.bossVisualOf(Period);
                    slideTime = now;
                    known = true;
                }

                double dt = Math.Clamp(now - slideTime, 0, 100);
                slideTime = now;

                // (she stands still from the warning to the end of the swing)
                if (now <= Period.End && !blowOn(now))
                {
                    // (she comes close enough to reach them, no closer)
                    Vector2 toPlayer = f.characterPosition - slide;
                    float away = toPlayer.Length, wanted = reachOfElly(f) * DanmakusuBalance.ELLY_NEAR;

                    if (away > wanted)
                        slide += toPlayer * (Math.Min(stageBulletSpeed(Period) * DanmakusuBalance.ELLY_SLIDE_SPEED * (float)dt, away - wanted) / away);
                }

                // (a blow only when they are within reach, and the last one is over)
                while (nextMark < marks.Length && marks[nextMark] <= now)
                {
                    double mark = marks[nextMark++];

                    if (!blowOn(mark) && Vector2.Distance(f.characterPosition, slide) <= reachOfElly(f))
                        addSlash(f, mark, slide);
                }
            }

            private bool blowOn(double t) => slashes.Count > 0 && t < litAt(slashes[^1]) + lightMs;

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;

                if (now > finish)
                    return false;

                if (!wolf)
                {
                    position = slide;
                    return known;
                }

                for (int i = jumps.Count - 1; i >= 0; i--)
                {
                    var jump = jumps[i];

                    if (jump.Start > now)
                        continue;

                    position = now >= jump.Arrive ? jump.To : Vector2.Lerp(jump.From, jump.To, (float)((now - jump.Start) / Math.Max(jump.Arrive - jump.Start, 1e-3)));
                    return true;
                }

                return false;
            }

            private void addSlash(BulletField f, double time, Vector2 centre)
            {
                // (Kagerou's claws are turned round where they go through the player, 45 degrees clockwise and the next one as far the other way, so that they cross as her swings do; they still go through the player)
                if (wolf)
                {
                    float tilt = DanmakusuBalance.KAGEROU_CLAW_TILT_DEGREES * MathF.PI / 180f * (slashCount % 2 == 0 ? 1f : -1f);
                    centre = f.characterPosition + turned(centre - f.characterPosition, tilt);
                }

                Vector2 toPlayer = f.characterPosition - centre;
                float distance = toPlayer.Length;

                slashes.Add(new Slash
                {
                    Time = time,
                    Centre = centre,
                    Angle = distance > 1e-3f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : 0f,
                    Radius = wolf ? Math.Max(distance, f.circleRadius * DanmakusuBalance.KAGEROU_SMALLEST_RADIUS_CIRCLES) : reachOfElly(f),
                    Turn = slashCount % 2 == 0 ? 1f : -1f,
                    Heard = !wolf || slashCount % DanmakusuBalance.KAGEROU_SOUND_EVERY == 0,
                });

                slashCount++;
            }

            /// <summary>A ring of bright white bullets from where she is now, each ring turned a little further.</summary>
            private void fireRing(BulletField f, Jump jump, double t)
            {
                float u = (float)Math.Clamp((t - jump.Start) / Math.Max(jump.Arrive - jump.Start, 1e-3), 0, 1);
                Vector2 from = Vector2.Lerp(jump.From, jump.To, u);
                float radius = f.stageBaseRadius(Period);
                // (while she stays they fly half as fast as while she flies)
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.KAGEROU_RING_SPEED * (t >= jump.Arrive ? DanmakusuBalance.KAGEROU_STAY_RING_SPEED : 1f);
                float phase = jump.Rings * 0.13f;

                for (int i = 0; i < DanmakusuBalance.KAGEROU_RING_BULLETS; i++)
                {
                    float angle = phase + i * MathF.Tau / DanmakusuBalance.KAGEROU_RING_BULLETS;
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = from,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(0, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Tint = BulletRenderer.TINT_WHITE,
                        TintAmount = 1f,
                        Bloom = DanmakusuBalance.KAGEROU_RING_BLOOM,
                        Until = t + f.stageLife(velocity),
                    });
                }
            }

            // ---- the cut ----

            private double cutFrom(Slash slash) => litAt(slash);

            public override bool Absorbs(double t)
            {
                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    double lit = litAt(slashes[i]);

                    if (lit <= t && t < lit + DanmakusuBalance.CLAW_CUT_MS)
                        return true;
                }

                return false;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    double lit = litAt(slashes[i]);

                    if (lit <= to && lit + DanmakusuBalance.CLAW_CUT_MS > from)
                        return true;
                }

                return false;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];
                    double lit = cutFrom(slash);

                    if (t < lit || t >= lit + DanmakusuBalance.CLAW_CUT_MS || !inBody(f, slash, to, 0f, out _))
                        continue;

                    wayOfCut(f, slash, to, out Vector2 normal, out float along);
                    CutBullet(f, t, from, to, emitter, bullet, normal, along);
                    return true;
                }

                return false;
            }

            /// <summary>Her own rings are cut as well.</summary>
            private void cutOwn(BulletField f, Slash slash, double lit)
            {
                int count = Bullets.Count;

                for (int i = 0; i < count; i++)
                {
                    var bullet = Bullets[i];

                    if (bullet.Hidden || lit < bullet.T0 || lit >= bullet.Until)
                        continue;

                    Vector2 position = stageBulletPosition(bullet, lit);

                    if (!inBody(f, slash, position, 0f, out _))
                        continue;

                    bullet.Until = lit;
                    Bullets[i] = bullet;
                    wayOfCut(f, slash, position, out Vector2 normal, out float along);
                    CutInTwo(f, lit, position, bullet.V, normal, along, bullet.Radius, bullet.Hit, bullet.Style, bullet.Shape, bullet.Tint);
                }
            }

            /// <summary>
            /// A beam the claw runs through comes apart in two where it does: what is further out flies on as it was, and what is between there and where it comes from flies back the opposite way, in one piece, as pieces side by side that hit as bullets do (and are gone from the beam,
            /// see <see cref="Blasted"/>). The two parts are never more uneven than 20 to 1, by how long they are: nearer to an end than that it comes apart further in. A beam that turns has no straight parts: only what lies in the claw goes, as a piece each way.
            /// </summary>
            private void cutBeams(BulletField f, Slash slash, double at)
            {
                double step = DanmakusuBalance.CLAW_BEAM_PIECE_MS;

                foreach (var laser in f.lasers)
                {
                    if (laser.Immune || laser.Speed <= 0f || laser.StartTime > at || at > laser.EndTime + laser.Lifetime)
                        continue;

                    double newest = Math.Min(at, laser.EndTime);
                    double oldest = Math.Max(laser.StartTime, at - laser.Lifetime);
                    double first = laser.StartTime + Math.Ceiling((oldest - laser.StartTime) / step - 1e-9) * step;
                    int count = (int)Math.Floor((newest - first) / step + 1e-9) + 1;

                    if (count <= 0)
                        continue;

                    for (int beam = 0; beam < laser.BeamCount; beam++)
                    {
                        if (laser.Rotates)
                        {
                            bool flashed = false;

                            for (int k = 0; k < count; k++)
                            {
                                double fired = first + k * step;
                                float angle = laser.SliceAngle(fired, at) + beam * MathF.Tau / laser.BeamCount;
                                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * laser.Speed;
                                Vector2 position = laser.SliceOrigin(fired, at) + velocity * (float)(at - fired);

                                if (!onStage(f, position) || !inBody(f, slash, position, 0f, out _))
                                    continue;

                                addBeamPiece(f, laser, at, fired, position, velocity);
                                addBeamPiece(f, laser, at, fired, position, -velocity);

                                if (!flashed)
                                {
                                    flashed = true;
                                    f.addFlash(at, position, f.circleRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f);
                                }
                            }

                            continue;
                        }

                        Vector2 straight = laser.Directions[beam] * laser.Speed;
                        Vector2 sliceAt(int k) => laser.SlicePosition(first + k * step, first + k * step, beam) + straight * (float)(at - (first + k * step));
                        int cut = -1;

                        // (the oldest slices are the furthest out: the first in the claw is where it comes apart)
                        for (int k = 0; k < count && cut < 0; k++)
                        {
                            Vector2 position = sliceAt(k);

                            if (onStage(f, position) && inBody(f, slash, position, 0f, out _))
                                cut = k;
                        }

                        if (cut < 0 || count < 2)
                            continue;

                        int least = Math.Max((int)Math.Ceiling(count / (DanmakusuBalance.CLAW_BEAM_UNEVEN + 1.0)), 1);
                        cut = Math.Clamp(cut, least, count - least);
                        beamCuts.Add((straight, first + cut * step, at));
                        f.addFlash(at, sliceAt(cut), f.circleRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f);

                        for (int k = cut; k < count; k++)
                        {
                            Vector2 position = sliceAt(k);

                            if (onStage(f, position))
                                addBeamPiece(f, laser, at, first + k * step, position, -straight);
                        }
                    }
                }
            }

            private static bool onStage(BulletField f, Vector2 position) => position.X >= f.stageMin.X && position.X <= f.stageMax.X && position.Y >= f.stageMin.Y && position.Y <= f.stageMax.Y;

            private void addBeamPiece(BulletField f, LaserEmitter laser, double at, double fired, Vector2 position, Vector2 velocity)
            {
                float half = Math.Max(laser.HalfWidthAtAge(at - fired), f.circleRadius * 0.12f);
                float length = laser.Speed * (float)DanmakusuBalance.CLAW_BEAM_PIECE_MS * 0.5f;
                Vector2 way = velocity.LengthSquared > 1e-8f ? Vector2.Normalize(velocity) : Vector2.UnitX;
                Vector2 across = new Vector2(-way.Y, way.X);
                var outline = new[] { way * length + across * half, way * length - across * half, -way * length - across * half, -way * length + across * half };

                AddPiece(f, at, position, velocity, half, half * DanmakusuBalance.BULLET_HITBOX_SCALE, f.comboStyle(laser.ComboIndex, laser.FollowsBoss, laser.BossRank), 0, outline);
            }

            /// <summary>Has a claw that has lit up by <paramref name="time"/> taken the slice away, because it lay in the claw or in the part of a beam that now flies back? A slice can only have been cut after it was fired.</summary>
            public bool Blasted(BulletField f, Vector2 start, Vector2 velocity, double fired, double time)
            {
                for (int i = beamCuts.Count - 1; i >= 0; i--)
                {
                    var cut = beamCuts[i];

                    if (cut.Lit < fired)
                        break;

                    if (cut.Lit <= time && fired >= cut.From && (velocity - cut.Velocity).LengthSquared < 1e-8f)
                        return true;
                }

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];
                    double lit = litAt(slash);

                    if (lit < fired)
                        break;

                    if (lit <= time && slash.Active && inBody(f, slash, start + velocity * (float)(lit - fired), 0f, out _))
                        return true;
                }

                return false;
            }

            /// <summary>The line of the cut of what is at <paramref name="position"/>: where the arc that is nearest to it runs through it.</summary>
            private void wayOfCut(BulletField f, Slash slash, Vector2 position, out Vector2 normal, out float along)
            {
                Vector2 off = position - slash.Centre;
                float distance = off.Length;

                normal = off.LengthSquared > 1e-6f ? Vector2.Normalize(off) : Vector2.UnitX;
                along = float.MaxValue;

                for (int arc = 0; arc < arcs; arc++)
                {
                    float there = arcRadius(f, slash, arc) - distance;

                    if (MathF.Abs(there) < MathF.Abs(along))
                        along = there;
                }
            }

            // ---- what hurts ----

            /// <summary>
            /// Her scythe is only there with a blow: is it there at <paramref name="t"/>, where does its tip point, which way does it go, and how much is left of it? It goes along with what lights up, from the first tip of the arc to the last, the way the arc grew, and stays there as it fades.
            /// </summary>
            private bool scytheAt(BulletField f, double t, out Vector2 boss, out float tip, out float turn, out float alpha)
            {
                boss = default;
                tip = turn = alpha = 0f;

                for (int i = slashes.Count - 1; i >= 0; i--)
                {
                    var slash = slashes[i];
                    double lit = litAt(slash);

                    if (t < lit)
                        continue;

                    double over = lit + lightMs, fading = beat * DanmakusuBalance.ELLY_SCYTHE_FADE_BEATS;

                    if (t >= over + fading)
                        return false;

                    float first = slash.Angle - slash.Turn * span, last = slash.Angle + slash.Turn * span;

                    turn = slash.Turn;
                    boss = t < over ? slash.Centre : f.bossVisualOf(Period);
                    tip = t < over ? first + (last - first) * (float)((t - lit) / lightMs) : last;
                    alpha = t < over ? 1f : 1f - smoothStep((t - over) / fading);
                    return true;
                }

                return false;
            }

            /// <summary>A place on the blade: it lies on the circle of the swing, ahead of the shaft, curving in a little, <paramref name="s"/> 0..1 from the shaft to its tip.</summary>
            private Vector2 bladeAt(BulletField f, Vector2 boss, float tip, float turn, float s)
            {
                float angle = tip - turn * DanmakusuBalance.ELLY_BLADE_LENGTH * (1f - s);

                return boss + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (reachOfElly(f) * (1f - DanmakusuBalance.ELLY_BLADE_CURVE * s * s));
            }



            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                foreach (var slash in slashes)
                {
                    double lit = litAt(slash);

                    if (now < lit || now >= goneAt(slash) || !inBody(f, slash, f.characterPosition, DanmakusuBalance.PLAYER_HITBOX_RADIUS, out float u))
                        continue;

                    // (only what has lit up hurts)
                    if (u <= (now - lit) / lightMs)
                        return true;
                }


                return false;
            }

            // ---- what is drawn ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                foreach (var slash in slashes)
                {
                    if (t < slash.Time || t >= goneAt(slash) + fadeMs)
                        continue;

                    // (Kagerou's preview grows in half a beat; Elly's as fast as it lights up later; both stay until they light up)
                    float grown = (float)Math.Clamp((t - slash.Time) / (wolf ? beat * DanmakusuBalance.KAGEROU_SPREAD_BEATS : lightMs), 0, 1);
                    float litUp = (float)Math.Clamp((t - litAt(slash)) / lightMs, 0, 1);
                    float fade = t < goneAt(slash) ? 1f : 1f - smoothStep((t - goneAt(slash)) / fadeMs);

                    for (int arc = 0; arc < arcs; arc++)
                    {
                        if (litUp > 0f)
                            addArc(f, slash, arc, 0f, litUp, fade);

                        if (grown > litUp)
                            addArc(f, slash, arc, litUp, grown, (wolf ? DanmakusuBalance.KAGEROU_GHOST_ALPHA : DanmakusuBalance.MIMA_GHOST_ALPHA) * fade);
                    }
                }

                DrawPieces(f, t);

                if (!wolf)
                    drawScythe(f, t);
            }

            private void addArc(BulletField f, Slash slash, int arc, float from, float to, float alpha)
            {
                const int steps = 24;
                float radius = arcRadius(f, slash, arc), half = halfWidth(f);
                var white = new Color4(1f, 1f, 1f, alpha);
                Color4 glow = lightColour;
                glow.A = 0.55f * alpha;
                var none = new Color4(glow.R, glow.G, glow.B, 0f);

                (Vector2 Outer, Vector2 Inner, Vector2 Out, float Glow) rim(float u)
                {
                    float angle = slash.Angle + slash.Turn * span * (2f * u - 1f);
                    Vector2 e = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    float h = half * taper(u);
                    return (slash.Centre + e * (radius + h), slash.Centre + e * Math.Max(radius - h, 0f), e, h * 1.4f + 1.5f);
                }

                var previous = rim(from);
                int pieceCount = Math.Max((int)MathF.Ceiling((to - from) * steps), 1);

                for (int i = 1; i <= pieceCount; i++)
                {
                    var next = rim(from + (to - from) * i / pieceCount);

                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = previous.Inner, D = next.Inner, Near = white, Far = white });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = previous.Outer + previous.Out * previous.Glow, D = next.Outer + next.Out * next.Glow, Near = glow, Far = none });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Inner, B = next.Inner, C = previous.Inner - previous.Out * previous.Glow, D = next.Inner - next.Out * next.Glow, Near = glow, Far = none });
                    previous = next;
                }
            }

            private void drawScythe(BulletField f, double t)
            {
                if (!scytheAt(f, t, out Vector2 boss, out float tip, out float turn, out float alpha))
                    return;

                float shaftAngle = tip - turn * DanmakusuBalance.ELLY_BLADE_LENGTH;
                Vector2 way = new Vector2(MathF.Cos(shaftAngle), MathF.Sin(shaftAngle));
                Vector2 side = new Vector2(-way.Y, way.X);
                float reach = reachOfElly(f), shaft = f.circleRadius * DanmakusuBalance.ELLY_SHAFT_HALF_WIDTH_CIRCLES, width = f.circleRadius * DanmakusuBalance.ELLY_BLADE_WIDTH_CIRCLES;
                var wood = new Color4(0.66f, 0.45f, 0.26f, alpha);
                var dark = new Color4(0.16f, 0.08f, 0.03f, alpha);
                var grain = new Color4(0.92f, 0.76f, 0.52f, alpha);
                var steel = new Color4(0.85f, 0.88f, 0.92f, alpha);
                var edge = new Color4(1f, 1f, 1f, alpha);
                Color4 glow = lightColour;
                glow.A = 0.5f * alpha;
                var none = new Color4(glow.R, glow.G, glow.B, 0f);
                Vector2 end = boss + way * reach;

                // (the shaft: light wood with a dark outline, a bright streak down its middle and the glow of her light round it, so that it shows on anything)
                float outline = shaft + 1.5f, streak = shaft * 0.35f, halo = shaft + 5f;

                f.stageMeshes.Add(new MeshQuad { A = boss + side * outline, B = end + side * outline, C = boss + side * halo, D = end + side * halo, Near = glow, Far = none });
                f.stageMeshes.Add(new MeshQuad { A = boss - side * outline, B = end - side * outline, C = boss - side * halo, D = end - side * halo, Near = glow, Far = none });
                f.stageMeshes.Add(new MeshQuad { A = boss + side * outline, B = boss - side * outline, C = end + side * outline, D = end - side * outline, Near = dark, Far = dark });
                f.stageMeshes.Add(new MeshQuad { A = boss + side * shaft, B = boss - side * shaft, C = end + side * shaft, D = end - side * shaft, Near = wood, Far = wood });
                f.stageMeshes.Add(new MeshQuad { A = boss + side * streak, B = boss - side * streak, C = end + side * streak, D = end - side * streak, Near = grain, Far = grain });

                Vector2 before = bladeAt(f, boss, tip, turn, 0f);
                float widthBefore = width;

                for (int k = 1; k <= 12; k++)
                {
                    float s = k / 12f;
                    Vector2 next = bladeAt(f, boss, tip, turn, s);
                    float widthNext = width * (1f - s);
                    Vector2 outBefore = before - boss, outNext = next - boss;

                    outBefore = outBefore.LengthSquared > 1e-6f ? Vector2.Normalize(outBefore) : way;
                    outNext = outNext.LengthSquared > 1e-6f ? Vector2.Normalize(outNext) : way;

                    f.stageMeshes.Add(new MeshQuad { A = before + outBefore * widthBefore * 0.5f, B = next + outNext * widthNext * 0.5f, C = before - outBefore * widthBefore * 0.5f, D = next - outNext * widthNext * 0.5f, Near = steel, Far = edge });
                    f.stageMeshes.Add(new MeshQuad { A = before + outBefore * widthBefore * 0.5f, B = next + outNext * widthNext * 0.5f, C = before + outBefore * (widthBefore * 0.5f + 3f), D = next + outNext * (widthNext * 0.5f + 3f), Near = glow, Far = none });

                    before = next;
                    widthBefore = widthNext;
                }
            }
        }

        private readonly List<ClawAct> clawActs = new List<ClawAct>();
    }
}
