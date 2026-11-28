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
        // Ellen: she throws a fan of twenty ellipses at the player every two beats (the planner's; rings of talismans until 2026-11-14). Every two neighbouring ellipses of a fan - the first and the second, the
        // third and the fourth... - are joined by a beam as wide as a slider's over and over, from the throw: see-through and harmless for a beat (the warning), solid and hurting like a slider body for ELLEN_BEAM_SOLID_BEATS (four since 2026-11-28; two, one before),
        // then gone for a beat, for as long as both are there.
        // Socrates, her cat (a white cat's face, twice as big as a plain bullet): it comes out of her and stays on her until its first jump; every four beats it jumps onto the bullet (anyone's) within four circle
        // radii of it that is nearest the player, if that one is nearer the player than the cat is, and rides it (where the bullet goes, it goes; if the bullet is gone, it stays where it was). A jump takes half
        // a beat, with a little hop. It likes best a bullet within reach whose leap goes over the player (the player between the cat and it, near the line), and it jumps more often when it is far from the player:
        // every beat when further than it can jump, every half beat when twice as far. The bullet of a jump is chosen a beat before (half the time between its jumps at most), and red arrows point from the cat
        // to it until it leaps. Chen's cat cries once, when it comes out of her (not at every jump). The cat hurts like a slider body, on its leaps too.
        // Since 2026-11-25 it can jump onto any bullet, the bosses' own too (the acts' bullets: Seat); it was the rings' only.
        // ======================================================================================================================================================

        private sealed class FamiliarAct : StageAct
        {
            private readonly double beat;
            private readonly List<BulletEmitter> rings = new List<BulletEmitter>(); // (her own rings of talismans, in the order they are thrown)
            private double longestRing;
            private readonly MomentShapes shapes = new MomentShapes(); // (the beams and the cat at the moments the autoplay asks about, kept for the frame)
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();

            // ---- the cat ----
            private double nextJump; // (when it jumps next: every two beats near the player, more often further off)
            private Seat ridden; // (the bullet it rides - a ring's or an act's; none: it is on Ellen, or stays where it was)
            private bool onEllen = true;
            private Vector2 resting; // (where it stays when what it rode is gone)
            private double leapStart = double.NaN;
            private Vector2 leapFrom;
            private bool meowed; // (it cries once, when it comes out of her)

            public FamiliarAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;

                nextJump = period.ShootStart + beat * DanmakusuBalance.ELLEN_CAT_JUMP_BEATS;
                warnMs = beat * DanmakusuBalance.ELLEN_CAT_WARN_BEATS;
            }

            public override void Init(BulletField f)
            {
                base.Init(f);

                foreach (var ring in f.emitters)
                {
                    if (ring.OwnShot && ring.FollowsBoss && ring.BossRank == Period.Rank && ring.Shape == BulletShape.Ellipse && ring.Count >= 2 && ring.Time >= Period.ShootStart && ring.Time < Period.End)
                    {
                        rings.Add(ring);
                        longestRing = Math.Max(longestRing, ring.Lifetime);
                    }
                }

                rings.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                shapes.Forget();

                // (it starts again on Ellen: what it rode before cannot be known again)
                ridden = default;
                onEllen = true;
                leapStart = double.NaN;
                meowed = time > Start;
                aiming = false;
                aim = default;
                warnMs = beat * DanmakusuBalance.ELLEN_CAT_WARN_BEATS;

                // (the next jump on the grid of its usual jumps)
                double every = beat * DanmakusuBalance.ELLEN_CAT_JUMP_BEATS;
                nextJump = Period.ShootStart + every * Math.Max(1, Math.Floor((time - Period.ShootStart) / every) + 1);
            }

            private float catRadius(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.ELLEN_CAT_SIZE;

            private double leapMs => beat * DanmakusuBalance.ELLEN_CAT_LEAP_BEATS;

            /// <summary>Where a bullet of a ring is at <paramref name="t"/>, if it is there.</summary>
            private static bool bulletAt(BulletField f, BulletEmitter ring, int bullet, double t, out Vector2 position)
            {
                position = default;

                if (t < ring.Time || bullet >= ring.Count || bullet >= 64 || ((ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask) & (1UL << bullet)) != 0)
                    return false;

                ring.Evaluate(bullet, ring.WithoutFrozenTime(f.effects.Effective(ring, t)), out position, out _, out bool alive);
                return alive && float.IsFinite(position.X) && float.IsFinite(position.Y);
            }

            /// <summary>A bullet the cat can sit on: a ring's (<see cref="Ring"/>, <see cref="Bullet"/>) or an act's (<see cref="Act"/>, <see cref="Index"/>: since 2026-11-25).</summary>
            private struct Seat
            {
                public BulletEmitter? Ring;
                public int Bullet;
                public StageAct? Act;
                public int Index;

                public bool Any => Ring != null || Act != null;
            }

            /// <summary>Where the bullet of <paramref name="seat"/> is at <paramref name="t"/>, if it is there.</summary>
            private static bool onSeat(BulletField f, in Seat seat, double t, out Vector2 position)
            {
                position = default;

                if (seat.Ring != null)
                    return bulletAt(f, seat.Ring, seat.Bullet, t, out position);

                return seat.Act != null && stageAt(f, seat.Act, seat.Index, t, out position);
            }

            /// <summary>Where bullet <paramref name="index"/> of <paramref name="act"/> is at <paramref name="t"/>, if it is there (not hidden, harmless or used up).</summary>
            private static bool stageAt(BulletField f, StageAct act, int index, double t, out Vector2 position)
            {
                position = default;

                if (index >= act.Bullets.Count)
                    return false;

                var bullet = act.Bullets[index];

                if (t < bullet.T0 || t > bullet.Until || bullet.Hidden || bullet.Harmless || f.stageConsumed.Contains(act.Id * 65536 + index))
                    return false;

                return f.stageBulletAt(bullet, t, out position) && float.IsFinite(position.X) && float.IsFinite(position.Y);
            }
            /// <summary>Where the cat sits at <paramref name="t"/>, leaving out a leap: on Ellen, on the bullet it rides, or where it stays.</summary>
            private Vector2 seatAt(BulletField f, double t)
            {
                if (onEllen)
                    return f.bossPositionOf(Period);

                if (onSeat(f, ridden, t, out Vector2 position))
                    return position;

                return resting;
            }

            /// <summary>Where the cat is at <paramref name="t"/>: on its way from where it leapt to its bullet over a quarter of a beat, then on it.</summary>
            private Vector2 catAt(BulletField f, double t)
            {
                Vector2 seat = seatAt(f, t);

                if (double.IsNaN(leapStart) || t >= leapStart + leapMs)
                    return seat;

                return Vector2.Lerp(leapFrom, seat, smoothStep((t - leapStart) / leapMs));
            }

            public override void Update(BulletField f, double now)
            {
                shapes.Forget(); // (a new frame)

                if (f.failed || now < Start || now >= End)
                    return;

                // (Chen's cat cries once, as it comes out of Ellen: not at every jump; 2026-11-14)
                if (!meowed)
                {
                    meowed = true;

                    if (now - Start < 250)
                        f.playStageSound("chen_cat");
                }

                // (where it stays if what it rides goes)
                if (onSeat(f, ridden, now, out Vector2 riding))
                    resting = riding;
                else if (ridden.Any)
                    ridden = default;

                // (the bullet of its next jump is chosen when the warning begins, and the arrow points at it until it leaps)
                if (!aiming && nextJump < End && now >= nextJump - warnMs && now < nextJump)
                {
                    aiming = choose(f, now, out aim);
                    aimedAt = now;
                }

                for (int guard = 0; nextJump <= now && nextJump < End && guard < 64; guard++)
                {
                    double jumpTime = nextJump;
                    Vector2 from = catAt(f, now);
                    Vector2 player = f.characterPosition;
                    float reach = f.circleRadius * DanmakusuBalance.ELLEN_CAT_REACH_CIRCLES;

                    // (the next jump: in four beats near the player; in one when it is further off than it can jump, in half a beat when twice as far - "increases jump frequency if too far", "jump frequency
                    // halved when she is near player", 2026-11-14)
                    float away = Vector2.Distance(from, player);
                    double every = beat * (away > reach * 2f ? DanmakusuBalance.ELLEN_CAT_FAR_JUMP_BEATS * 0.5 : away > reach ? DanmakusuBalance.ELLEN_CAT_FAR_JUMP_BEATS : DanmakusuBalance.ELLEN_CAT_JUMP_BEATS);
                    nextJump = jumpTime + every;
                    warnMs = Math.Min(beat * DanmakusuBalance.ELLEN_CAT_WARN_BEATS, every * 0.5);

                    // (the bullet warned of, if it is still there; otherwise one chosen now)
                    Seat target = default;

                    if (aiming && onSeat(f, aim, now, out _))
                        target = aim;
                    else if (choose(f, now, out var other))
                        target = other;

                    aiming = false;
                    aim = default;

                    if (!target.Any)
                        continue; // (nothing nearer the player within reach: it stays)

                    leapFrom = from;
                    leapStart = jumpTime;
                    ridden = target;
                    onEllen = false;
                    resting = from;
                }
            }

            // (the warning of its next jump: the bullet it will leap to, chosen when the warning began)
            private bool aiming;
            private Seat aim;
            private double aimedAt;
            private double warnMs;

            /// <summary>
            /// The bullet the cat would jump to at <paramref name="now"/>: within its reach, best one whose leap goes over the player, else the one nearest the player (if nearer than the cat is). Any bullet:
            /// a ring's, or one of a boss's own (an act's: Kanako's talismans, Alice's streams... - 2026-11-25, "can jump on any bullet, including bosses"; it was the rings' only).
            /// </summary>
            private bool choose(BulletField f, double now, out Seat chosen)
            {
                Vector2 from = catAt(f, now);
                Vector2 player = f.characterPosition;
                float reach = f.circleRadius * DanmakusuBalance.ELLEN_CAT_REACH_CIRCLES;
                float best = (from - player).LengthSquared;
                Seat nearest = default;

                // (best of all, one whose leap goes over the player: the player lies along the way, between where it is and the bullet - "tries to jump over player", 2026-11-14)
                float bestOver = float.PositiveInfinity;
                Seat over = default;

                void consider(Vector2 position, Seat seat)
                {
                    if ((position - from).LengthSquared > reach * reach)
                        return;

                    float toPlayer = (position - player).LengthSquared;

                    if (toPlayer < best)
                    {
                        best = toPlayer;
                        nearest = seat;
                    }

                    // (over the player: it lies a good way along the leap, and near its line)
                    Vector2 leap = position - from;
                    float length = leap.LengthSquared;

                    if (length <= 1f)
                        return;

                    float along = Vector2.Dot(player - from, leap) / length;

                    if (along > 0.2f && along < 0.85f)
                    {
                        float off = distanceToSegment(player, from, position);

                        if (off <= f.circleRadius * DanmakusuBalance.ELLEN_CAT_OVER_CIRCLES && off < bestOver)
                        {
                            bestOver = off;
                            over = seat;
                        }
                    }
                }

                // the rings' bullets (where the hit check saw them a frame ago, when it did: it is only for choosing)
                for (int k = 0; k < f.activeCount; k++)
                {
                    var ring = f.emitters[f.active[k]];

                    if (ring.Retired || ring.Count == 0 || ring.Shape == BulletShape.Train || now < ring.Time)
                        continue;

                    var seen = ring.Seen;
                    ulong gone = ring.ConsumedMask | ring.AbsorbedMask | ring.HiddenMask;

                    for (int b = 0; b < ring.Count && b < 64; b++)
                    {
                        ulong bit = 1UL << b;

                        if ((gone & bit) != 0)
                            continue;

                        Vector2 position;

                        if (seen != null && (seen.Known & bit) != 0)
                        {
                            if ((seen.Alive & bit) == 0)
                                continue;

                            position = seen.Position[b];
                        }
                        else if (!bulletAt(f, ring, b, now, out position))
                            continue;

                        consider(position, new Seat { Ring = ring, Bullet = b });
                    }
                }

                // the bosses' own bullets (not its own, nor what follows something else round)
                foreach (var act in f.stageActs)
                {
                    if (act == this)
                        continue;

                    for (int i = 0; i < act.Bullets.Count; i++)
                    {
                        if (act.Bullets[i].T0 > now || act.Bullets[i].Until < now)
                            continue;

                        if (stageAt(f, act, i, now, out Vector2 position))
                            consider(position, new Seat { Act = act, Index = i });
                    }
                }

                chosen = over.Any ? over : nearest;
                return chosen.Any;
            }

            // ---- the beams between the talismans ----

            /// <summary>The first of her rings that can still be there at <paramref name="t"/>.</summary>
            private int firstRing(double t)
            {
                int low = 0, high = rings.Count;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (rings[middle].Time < t - longestRing)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            private float beamHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>
            /// The beams of a fan thrown at <paramref name="thrown"/> at <paramref name="t"/>, over and over from the throw: see-through and harmless (the warning), then solid, then gone (2026-11-14: "periodically
            /// connected, warn about it with previews"). How visible they are (0: not there) and whether they hurt.
            /// </summary>
            private float beamState(double thrown, double t, out bool solid)
            {
                double preview = beat * DanmakusuBalance.ELLEN_BEAM_PREVIEW_BEATS, held = beat * DanmakusuBalance.ELLEN_BEAM_SOLID_BEATS;
                double cycle = preview + held + beat * DanmakusuBalance.ELLEN_BEAM_OFF_BEATS;
                double u = (t - thrown) % cycle;
                solid = false;

                if (u < 0)
                    return 0f;

                if (u < preview)
                    return DanmakusuBalance.JUNKO_GHOST_ALPHA * smoothStep(u / Math.Min(preview * 0.25, 120.0));

                if (u < preview + held)
                {
                    solid = true;
                    float come = smoothStep((u - preview) / 60.0); // (it comes in quickly from the warning)
                    float go = 1f - smoothStep((u - (preview + held - 60.0)) / 60.0);
                    return DanmakusuBalance.JUNKO_GHOST_ALPHA + (1f - DanmakusuBalance.JUNKO_GHOST_ALPHA) * Math.Min(come, go);
                }

                return 0f;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || rings.Count == 0 || f.palette.Length == 0)
                    return;

                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style % f.palette.Length];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];
                float half = beamHalf(f);

                for (int r = firstRing(t); r < rings.Count && rings[r].Time <= t; r++)
                {
                    var ring = rings[r];

                    if (t > ring.Time + ring.Lifetime)
                        continue;

                    float alpha = beamState(ring.Time, t, out _);

                    if (alpha < 0.01f)
                        continue;

                    for (int b = 0; b + 1 < ring.Count && b + 1 < 64; b += 2)
                    {
                        if (!bulletAt(f, ring, b, t, out Vector2 a) || !bulletAt(f, ring, b + 1, t, out Vector2 c))
                            continue;

                        line.Clear();
                        line.Add(a);
                        line.Add(c);
                        fades.Clear();
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, half, colour, border, ref beams, ref pointTotal, null, fades);
                    }
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t >= End)
                    return;

                // (it comes out of her over a quarter of a beat, and goes when her section ends)
                float alpha = smoothStep((t - Start) / leapMs) * (1f - smoothStep((t - (End - leapMs)) / leapMs));
                float size = catRadius(f) * 2f / 0.76f; // (the head is 0.76 of the picture's half width: it is as wide as the cat)
                float hop = double.IsNaN(leapStart) || t >= leapStart + leapMs ? 0f : MathF.Sin(MathF.PI * (float)((t - leapStart) / leapMs));

                f.stageFront.Add(StageTexture.CatFace, catAt(f, t) - new Vector2(0f, hop * catRadius(f) * 0.6f), new Vector2(size * (1f + 0.15f * hop)), 0f, alpha, Color4.White);

                // (the warning of its next jump: red arrows (Seija's) from it to the bullet it will leap to, coming in as the warning begins and blinking faster as the jump comes; 2026-11-14)
                if (aiming && t >= aimedAt && t < nextJump && onSeat(f, aim, t, out Vector2 target))
                {
                    Vector2 cat = catAt(f, t);
                    Vector2 way = target - cat;
                    float length = way.Length;

                    if (length > catRadius(f))
                    {
                        way /= length;
                        float left = (float)((nextJump - t) / Math.Max(warnMs, 1));
                        float warn = smoothStep((t - aimedAt) / 80.0) * (0.6f + 0.4f * MathF.Cos((1f - left) * MathF.PI * 6f));
                        float arrow = Math.Max(catRadius(f) * 0.9f, f.circleRadius * 0.5f);
                        int arrows = Math.Clamp((int)(length / (arrow * 1.6f)), 1, 6);
                        var red = new Color4(1f, 0.25f, 0.2f, 1f);

                        for (int i = 0; i < arrows; i++)
                        {
                            float along = catRadius(f) + (length - catRadius(f)) * (i + 0.5f) / arrows;
                            f.stageRipple.Add(StageTexture.SeijaArrow, cat + way * along, new Vector2(arrow), MathF.Atan2(way.Y, way.X) * 180f / MathF.PI, warn * 0.9f * alpha, red);
                        }
                    }
                }
            }

            // ---- what hurts ----

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;
                float beamReach = beamHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float catReach = catRadius(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                bool catThere = now >= Start && now < End;

                // (asked about the same moment again - the autoplay, for many places: the beams and the cat of that moment are worked out once and kept for the frame)
                var kept = shapes.Kept(now);

                if (kept == null && shapes.Begin(now) is MomentShapes.Moment fresh)
                {
                    if (catThere)
                    {
                        fresh.Point(catAt(f, now), catReach);
                        fresh.EndRun();
                    }

                    for (int r = firstRing(now); r < rings.Count && rings[r].Time <= now; r++)
                    {
                        var ring = rings[r];

                        if (now > ring.Time + ring.Lifetime)
                            continue;

                        beamState(ring.Time, now, out bool solid);

                        if (!solid)
                            continue;

                        for (int b = 0; b + 1 < ring.Count && b + 1 < 64; b += 2)
                        {
                            if (!bulletAt(f, ring, b, now, out Vector2 a) || !bulletAt(f, ring, b + 1, now, out Vector2 c))
                                continue;

                            fresh.Point(a, beamReach);
                            fresh.Point(c, beamReach);
                            fresh.EndRun();
                        }
                    }

                    kept = fresh;
                }

                if (kept != null)
                    return kept.Touches(player);

                if (catThere && Vector2.Distance(player, catAt(f, now)) <= catReach)
                    return true;

                for (int r = firstRing(now); r < rings.Count && rings[r].Time <= now; r++)
                {
                    var ring = rings[r];

                    if (now > ring.Time + ring.Lifetime)
                        continue;

                    beamState(ring.Time, now, out bool solid);

                    if (!solid)
                        continue;

                    for (int b = 0; b + 1 < ring.Count && b + 1 < 64; b += 2)
                    {
                        if (!bulletAt(f, ring, b, now, out Vector2 a) || !bulletAt(f, ring, b + 1, now, out Vector2 c))
                            continue;

                        if (distanceToSegment(player, a, c) <= beamReach)
                            return true;
                    }
                }

                return false;
            }
        }
    }
}
