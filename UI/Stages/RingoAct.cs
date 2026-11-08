using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Ringo: every bullet fired in her attacks has two copies right behind it (the planner's); every one of these threes is a dango on a skewer: a thin beam in the colour of the bullets runs through the three,
        // sticking out a little in front of the first and further behind the last (the handle), and hurts like a slider body. Only a whole three has one: once one of them is gone (it hit, or a boss took it) its skewer is gone. Every
        // time she shoots her big fast bullets, Mokou's impact sounds. The threes come from the planner (BossPeriod.Skewers, the marks on the rings); which of them has a skewer is worked out from the ring's time and the
        // bullet's number, so a replay and a seek give the same.
        // ======================================================================================================================================================

        private sealed class RingoAct : StageAct
        {
            private readonly List<(BulletEmitter First, BulletEmitter Second, BulletEmitter Third)> skewers = new List<(BulletEmitter, BulletEmitter, BulletEmitter)>();
            private readonly double[] volleys;
            private readonly List<Vector2> points = new List<Vector2>(3);
            private int nextVolley;

            public RingoAct(BossPeriod period)
            {
                Period = period;
                Start = period.ShootStart;
                End = period.End;

                double gap = Math.Max(period.BeatLength, 1) * DanmakusuBalance.RINGO_VOLLEY_BEATS;
                var list = new List<double>();

                for (double t = period.ShootStart; t < period.End; t += gap)
                    list.Add(t);

                volleys = list.ToArray();
            }

            public override void Init(BulletField f)
            {
                base.Init(f);

                // (the threes are found by the marks on their rings, as the field has them: with another boss on the screen the rings were halved after the planner made the threes, new rings in their place.
                // A three whose rings are not all there once, with as many bullets each, has no skewer: another boss's mechanic has made something else of it)
                var mine = new HashSet<int>(Period.Skewers);
                var parts = new Dictionary<int, BulletEmitter?[]>();
                var spoilt = new HashSet<int>();

                foreach (var ring in f.emitters)
                {
                    if (ring.SkewerId == 0 || !mine.Contains(ring.SkewerId) || ring.SkewerPart is < 0 or > 2)
                        continue;

                    if (!parts.TryGetValue(ring.SkewerId, out var three))
                        parts[ring.SkewerId] = three = new BulletEmitter?[3];

                    if (three[ring.SkewerPart] != null)
                        spoilt.Add(ring.SkewerId);

                    three[ring.SkewerPart] = ring;
                }

                foreach (int id in Period.Skewers)
                {
                    if (spoilt.Contains(id) || !parts.TryGetValue(id, out var three) || three[0] == null || three[1] == null || three[2] == null)
                        continue;

                    if (three[0]!.Count == three[1]!.Count && three[0]!.Count == three[2]!.Count)
                        skewers.Add((three[0]!, three[1]!, three[2]!));
                }
            }

            public override void Reset(BulletField f, double time)
            {
                nextVolley = 0;

                while (nextVolley < volleys.Length && volleys[nextVolley] < time)
                    nextVolley++;
            }

            public override void Update(BulletField f, double now)
            {
                for (; nextVolley < volleys.Length && volleys[nextVolley] <= now; nextVolley++)
                {
                    if (now - volleys[nextVolley] < 250 && !f.failed)
                        f.playStageSound("mokou_wall_hit");
                }
            }

            private static bool skewered(BulletEmitter first, int bullet) => (Math.Abs((long)first.Time) + bullet) % DanmakusuBalance.RINGO_SKEWER_EVERY == 0;

            /// <summary>
            /// Where the bullets of a three are at <paramref name="t"/>, the ones that are there, first to last, into <see cref="points"/>, and the skewer's tip in front of the first and its handle behind the last (out of the bullets,
            /// where it can be seen: between them only a little of it shows). Fewer than two points: there is no skewer.
            /// </summary>
            private void threeAt(BulletField f, (BulletEmitter First, BulletEmitter Second, BulletEmitter Third) skewer, int bullet, double t)
            {
                points.Clear();
                add(f, skewer.First, bullet, t);
                add(f, skewer.Second, bullet, t);
                add(f, skewer.Third, bullet, t);

                // (only a whole three is on a skewer: once one of them is gone, the skewer is too; nor is a three that has come apart, see together)
                if (points.Count < 3 || !together(skewer.First.VisualRadius))
                {
                    points.Clear();
                    return;
                }

                stick(skewer.First.VisualRadius);
            }

            /// <summary>
            /// Are the three (in <see cref="points"/>) still one behind the other, close: no two neighbours further apart than <see cref="DanmakusuBalance.RINGO_SKEWER_APART"/> times the gap they were fired with? What has sent them different ways
            /// (a ring that aims at the player was aimed again for each of them, another boss turned one and not the others, one went round the screen) has no skewer: it ran across the screen from one to the other.
            /// </summary>
            private bool together(float radius)
            {
                float most = radius * DanmakusuBalance.RINGO_BEHIND_SPACING * DanmakusuBalance.RINGO_SKEWER_APART;

                for (int i = 1; i < points.Count; i++)
                {
                    if (Vector2.DistanceSquared(points[i - 1], points[i]) > most * most)
                        return false;
                }

                return true;
            }

            private void stick(float radius)
            {
                if (points.Count < 2)
                    return;

                Vector2 forward = points[0] - points[^1];

                if (forward.LengthSquared < 1e-6f)
                    return;

                forward = Vector2.Normalize(forward);
                points.Insert(0, points[0] + forward * (radius * DanmakusuBalance.RINGO_SKEWER_TIP));
                points.Add(points[^1] - forward * (radius * DanmakusuBalance.RINGO_SKEWER_HANDLE));
            }

            private void add(BulletField f, BulletEmitter emitter, int bullet, double t)
            {
                if (t < emitter.Time || bullet >= emitter.Count || ((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & (1UL << bullet)) != 0)
                    return;

                emitter.Evaluate(bullet, emitter.WithoutFrozenTime(f.effects.Effective(emitter, t)), out Vector2 position, out _, out bool alive);

                if (alive)
                    points.Add(position);
            }

            /// <summary>Is the three of no interest at <paramref name="t"/>: not fired yet, or long gone?</summary>
            private static bool outside((BulletEmitter First, BulletEmitter Second, BulletEmitter Third) skewer, double t)
                => t < skewer.First.Time || t > skewer.Third.Time + skewer.Third.Lifetime;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.palette.Length == 0)
                    return;

                foreach (var skewer in skewers)
                {
                    if (outside(skewer, t))
                        continue;

                    int style = f.styleOf(skewer.First);
                    float halfWidth = skewer.First.VisualRadius * DanmakusuBalance.RINGO_SKEWER_WIDTH;

                    for (int b = 0; b < skewer.First.Count; b++)
                    {
                        if (!skewered(skewer.First, b))
                            continue;

                        threeAt(f, skewer, b, t);

                        if (points.Count >= 2)
                            f.addPlainBeam(points, halfWidth, f.palette[style % f.palette.Length], f.borderPalette[style % f.borderPalette.Length], ref beams, ref pointTotal);
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;

                foreach (var skewer in skewers)
                {
                    if (outside(skewer, now))
                        continue;

                    float hit = skewer.First.VisualRadius * DanmakusuBalance.RINGO_SKEWER_WIDTH * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    // (a three is never longer than this, with its stick: a skewer further from the player than that cannot touch them)
                    float reach = skewer.First.VisualRadius * (DanmakusuBalance.RINGO_BEHIND_SPACING * 3f + DanmakusuBalance.RINGO_SKEWER_TIP + DanmakusuBalance.RINGO_SKEWER_HANDLE) + hit;

                    for (int b = 0; b < skewer.First.Count; b++)
                    {
                        if (!skewered(skewer.First, b))
                            continue;

                        // (the first is looked at first: most are far away)
                        points.Clear();
                        add(f, skewer.First, b, now);

                        if (points.Count == 1 && (points[0] - player).LengthSquared > reach * reach)
                            continue;

                        add(f, skewer.Second, b, now);
                        add(f, skewer.Third, b, now);

                        // (as it is drawn: only a whole three that is still together)
                        if (points.Count < 3 || !together(skewer.First.VisualRadius))
                            continue;

                        stick(skewer.First.VisualRadius);

                        for (int i = 1; i < points.Count; i++)
                        {
                            Vector2 from = points[i - 1], along = points[i] - from;
                            float u = Math.Clamp(Vector2.Dot(player - from, along) / Math.Max(along.LengthSquared, 1e-6f), 0f, 1f);

                            if (Vector2.Distance(player, from + along * u) <= hit)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
