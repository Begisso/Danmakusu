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
        // Ellen: she throws rings of talismans every two beats (the planner's). Every two neighbouring talismans of a ring - the first and the second, the third and the fourth... - are joined by a beam as wide as a
        // slider's, see-through and harmless for its first four beats, then hurting like a slider body, for as long as both talismans are there: a dashed ring that grows with them.
        // Socrates, her cat (a white cat's face, twice as big as a plain bullet): it comes out of her and stays on her until its first jump; every two beats it jumps onto the bullet (anyone's) within four circle
        // radii of it that is nearest the player, if that one is nearer the player than the cat is, and rides it (where the bullet goes, it goes; if the bullet is gone, it stays where it was). A jump takes a
        // quarter of a beat, with a little hop. Since 2026-11-14 it likes best a bullet within reach whose leap goes over the player (the player between the cat and it, near the line), and it jumps more often
        // when it is far from the player: every beat when further than it can jump, every half beat when twice as far. Chen's cat cries once, when it comes out of her (not at every jump). The cat hurts like a
        // slider body, on its leaps too.
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
            private BulletEmitter? ridden; // (the ring of the bullet it rides; none: it is on Ellen, or stays where it was)
            private int riddenBullet;
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
            }

            public override void Init(BulletField f)
            {
                base.Init(f);

                foreach (var ring in f.emitters)
                {
                    if (ring.OwnShot && ring.FollowsBoss && ring.BossRank == Period.Rank && ring.Shape == BulletShape.Talisman && ring.Count >= 2 && ring.Time >= Period.ShootStart && ring.Time < Period.End)
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
                ridden = null;
                onEllen = true;
                leapStart = double.NaN;
                meowed = time > Start;

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

            /// <summary>Where the cat sits at <paramref name="t"/>, leaving out a leap: on Ellen, on the bullet it rides, or where it stays.</summary>
            private Vector2 seatAt(BulletField f, double t)
            {
                if (onEllen)
                    return f.bossPositionOf(Period);

                if (ridden != null && bulletAt(f, ridden, riddenBullet, t, out Vector2 position))
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
                if (ridden != null && bulletAt(f, ridden, riddenBullet, now, out Vector2 riding))
                    resting = riding;
                else if (ridden != null)
                    ridden = null;

                for (int guard = 0; nextJump <= now && nextJump < End && guard < 64; guard++)
                {
                    double jumpTime = nextJump;
                    Vector2 from = catAt(f, now);
                    Vector2 player = f.characterPosition;
                    float reach = f.circleRadius * DanmakusuBalance.ELLEN_CAT_REACH_CIRCLES;

                    // (the next jump: in two beats near the player; in one when it is further off than it can jump, in half a beat when twice as far - "increases jump frequency if too far", 2026-11-14)
                    float away = Vector2.Distance(from, player);
                    nextJump = jumpTime + beat * (away > reach * 2f ? DanmakusuBalance.ELLEN_CAT_FAR_JUMP_BEATS * 0.5 : away > reach ? DanmakusuBalance.ELLEN_CAT_FAR_JUMP_BEATS : DanmakusuBalance.ELLEN_CAT_JUMP_BEATS);

                    float best = (from - player).LengthSquared;
                    BulletEmitter? chosen = null;
                    int chosenBullet = 0;

                    // (best of all, one whose leap goes over the player: the player lies along the way, between where it is and the bullet - "tries to jump over player", 2026-11-14)
                    float bestOver = float.PositiveInfinity;
                    BulletEmitter? over = null;
                    int overBullet = 0;

                    // the bullet within reach that is nearest the player (where the hit check saw them a frame ago, when it did: it is only for choosing)
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

                            if ((position - from).LengthSquared > reach * reach)
                                continue;

                            float toPlayer = (position - player).LengthSquared;

                            if (toPlayer < best)
                            {
                                best = toPlayer;
                                chosen = ring;
                                chosenBullet = b;
                            }

                            // (over the player: it lies a good way along the leap, and near its line)
                            Vector2 leap = position - from;
                            float length = leap.LengthSquared;

                            if (length > 1f)
                            {
                                float along = Vector2.Dot(player - from, leap) / length;

                                if (along > 0.2f && along < 0.85f)
                                {
                                    float off = distanceToSegment(player, from, position);

                                    if (off <= f.circleRadius * DanmakusuBalance.ELLEN_CAT_OVER_CIRCLES && off < bestOver)
                                    {
                                        bestOver = off;
                                        over = ring;
                                        overBullet = b;
                                    }
                                }
                            }
                        }
                    }

                    if (over != null)
                    {
                        chosen = over;
                        chosenBullet = overBullet;
                    }

                    if (chosen == null)
                        continue; // (nothing nearer the player within reach: it stays)

                    leapFrom = from;
                    leapStart = jumpTime;
                    ridden = chosen;
                    riddenBullet = chosenBullet;
                    onEllen = false;
                    resting = from;
                }
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

            private double harmlessMs => beat * DanmakusuBalance.ELLEN_BEAM_HARMLESS_BEATS;

            /// <summary>How visible the beams of a ring thrown at <paramref name="thrown"/> are at <paramref name="t"/>: see-through while harmless, coming in over 250 ms when they start to hurt.</summary>
            private float beamVisibility(double thrown, double t)
            {
                double age = t - thrown - harmlessMs;

                if (age < 0)
                    return DanmakusuBalance.JUNKO_GHOST_ALPHA;

                float u = (float)Math.Min(age / 250.0, 1.0);
                return DanmakusuBalance.JUNKO_GHOST_ALPHA + (1f - DanmakusuBalance.JUNKO_GHOST_ALPHA) * u * u * (3f - 2f * u);
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

                    float alpha = beamVisibility(ring.Time, t);

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

                        if (now > ring.Time + ring.Lifetime || now < ring.Time + harmlessMs)
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

                    if (now > ring.Time + ring.Lifetime || now < ring.Time + harmlessMs)
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
