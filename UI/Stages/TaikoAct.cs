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
        // Raiko: during an attack five taikos turn round her and are beaten in turn, one slam a beat. Every slam sends out a ripple (a circle that grows from the drum) that does not hurt, but
        // makes every bullet and every beam it goes over twice as big (to look at and to touch) for a very short time; while a bullet is swollen (at least half way) it hurts like a slider body and is not used up by a hit
        // (BulletField.collideRing). Every slam shakes the screen for a moment.
        // ======================================================================================================================================================

        private sealed class TaikoAct : StageAct
        {
            public readonly double Beat;

            /// <summary>The time between two hits of a row of the pattern: <see cref="DanmakusuBalance.RAIKO_SLAM_BEATS"/> beats.</summary>
            public readonly double SlamGap;

            /// <summary>How many times in all the drums are struck (the sticks included).</summary>
            public readonly int Slams;

            private readonly double[] hitTimes;
            private readonly bool[] hitSticks;
            private readonly int[] drums;
            private readonly Vector2[] centres;
            private readonly bool[] known;

            /// <summary>The hits that are made with the sticks (they push the bullets round Raiko away), where Raiko was when each was struck, and whether that has been captured yet.</summary>
            private readonly double[] stickTimes;

            private readonly Vector2[] stickCentres;
            private readonly bool[] stickKnown;

            // one round of the pattern (in gaps): drum drum drum stick, drum drum drum stick, then a drum half a gap after the last stick; the next round starts half a gap after that drum
            private static readonly double[] pattern_times = { 0, 1, 2, 3, 4, 5, 6, 7, 7.5 };
            private static readonly bool[] pattern_sticks = { false, false, false, true, false, false, false, true, false };
            private const double pattern_length = 8;
            private const int max_hits = 2000;

            public double HitTime(int k) => hitTimes[k];

            public bool IsStick(int k) => hitSticks[k];

            /// <summary>The drums go round the circle clockwise in the first round of the pattern, counter-clockwise in the next, and so on (every round is <see cref="pattern_length"/> gaps).</summary>
            public TaikoAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                Beat = Math.Max(period.BeatLength, 1);
                SlamGap = Beat * DanmakusuBalance.RAIKO_SLAM_BEATS;

                var times = new List<double>();
                var sticks = new List<bool>();

                for (int round = 0; times.Count < max_hits; round++)
                {
                    bool over = false;

                    for (int i = 0; i < pattern_times.Length && times.Count < max_hits; i++)
                    {
                        double at = start + (round * pattern_length + pattern_times[i]) * SlamGap;

                        if (at >= end && times.Count > 0)
                        {
                            over = true;
                            break;
                        }

                        times.Add(at);
                        sticks.Add(pattern_sticks[i]);
                    }

                    if (over)
                        break;
                }

                hitTimes = times.ToArray();
                hitSticks = sticks.ToArray();
                Slams = hitTimes.Length;
                centres = new Vector2[Slams];
                known = new bool[Slams];
                drums = new int[Slams];

                int count = DanmakusuBalance.RAIKO_TAIKO_COUNT;
                int drum = 0;

                for (int k = 0; k < Slams; k++)
                {
                    int round = (int)Math.Floor((hitTimes[k] - start) / (pattern_length * SlamGap) + 1e-9);
                    bool counterClockwise = round % 2 == 1;
                    drum = k == 0 ? 0 : (((drum + (counterClockwise ? -1 : 1)) % count) + count) % count; // (the drums are hit one after the other, round the circle)
                    drums[k] = drum;
                }

                var stickList = new List<double>();

                for (int k = 0; k < Slams; k++)
                {
                    if (hitSticks[k])
                        stickList.Add(hitTimes[k]);
                }

                stickTimes = stickList.ToArray();
                stickCentres = new Vector2[stickTimes.Length];
                stickKnown = new bool[stickTimes.Length];
            }

            public override void Init(BulletField f)
            {
                // the act only pushes bullets at the moment a stick is struck (see Absorbs)
                if (stickTimes.Length > 0)
                {
                    AbsorbFrom = stickTimes[0];
                    AbsorbTo = stickTimes[^1] + DanmakusuBalance.RAIKO_PUSH_WINDOW_MS;
                }
            }

            /// <summary>The index of the last hit at or before <paramref name="t"/> (-1 if there is none yet).</summary>
            private int latestHit(double t)
            {
                int i = Array.BinarySearch(hitTimes, t);
                return i >= 0 ? i : ~i - 1;
            }

            private int latestStick(double t)
            {
                int i = Array.BinarySearch(stickTimes, t);
                return i >= 0 ? i : ~i - 1;
            }

            /// <summary>The stick that has just been struck at <paramref name="t"/> (within the short moment in which it pushes), or -1.</summary>
            private int stickAt(double t)
            {
                int i = latestStick(t);
                return i >= 0 && t - stickTimes[i] < DanmakusuBalance.RAIKO_PUSH_WINDOW_MS ? i : -1;
            }

            public override bool Absorbs(double t) => stickAt(t) >= 0;

            public override bool AbsorbsBetween(double from, double to)
            {
                int i = latestStick(to);
                return i >= 0 && stickTimes[i] + DanmakusuBalance.RAIKO_PUSH_WINDOW_MS >= from;
            }

            /// <summary>The bullets round Raiko when a stick is struck (closer than <see cref="DanmakusuBalance.RAIKO_PUSH_RADIUS"/> hit circles) are pushed straight away from her, once, faster than they flew.</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                int s = stickAt(t);

                if (s < 0 || !stickKnown[s] || !emitter.CanBeDeflected)
                    return false;

                Vector2 away = to - stickCentres[s];
                float reach = DanmakusuBalance.RAIKO_PUSH_RADIUS * f.circleRadius;

                if (away.LengthSquared > reach * reach)
                    return false;

                // once per stick: a bullet that has been turned or pushed since the stick was struck is left alone
                double struck = emitter.WithoutFrozenTime(f.effects.Effective(emitter, stickTimes[s]));

                if (emitter.Deflections != null && emitter.Deflections.TryGetValue(bullet, out var turned) && turned.Time >= struck)
                    return false;

                direction = away.LengthSquared > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitX;
                speed = Math.Max(emitter.SpeedOf(bullet) * DanmakusuBalance.RAIKO_PUSH_SPEED_FACTOR, DanmakusuBalance.RAIKO_PUSH_MIN_SPEED);
                return true;
            }

            public Vector2 TaikoAt(BulletField f, int index, double t)
            {
                float radius = Math.Max(DanmakusuBalance.RAIKO_ORBIT_RADIUS * f.circleRadius, DanmakusuBalance.RAIKO_ORBIT_MIN) * smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS);
                float angle = -MathF.PI / 2f + index * MathF.Tau / DanmakusuBalance.RAIKO_TAIKO_COUNT + (float)(Math.Tau * (t - Start) / (DanmakusuBalance.RAIKO_ORBIT_BEATS * Beat));

                return f.bossVisualOf(Period) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }

            public float Alpha(double t)
                => t < Start || t > End + DanmakusuBalance.STAGE_FADE_OUT_MS ? 0f : smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

            /// <summary>The longest a ripple can last (a safe limit): the playfield's diagonal is crossed once, and the band has to leave as well.</summary>
            private static double lifeOf(BulletField f)
                => (Vector2.Distance(f.stageMin, f.stageMax) + 4f * DanmakusuBalance.RAIKO_RIPPLE_HALF_WIDTH) / DanmakusuBalance.RAIKO_RIPPLE_SPEED;

            /// <summary>How long a ripple that started at <paramref name="c"/> lasts: until it has gone beyond the playfield altogether.</summary>
            private static double lifeFor(BulletField f, Vector2 c)
            {
                float far = farthestCorner(c, f.stageMin, f.stageMax);

                return (far + 2f * DanmakusuBalance.RAIKO_RIPPLE_HALF_WIDTH) / DanmakusuBalance.RAIKO_RIPPLE_SPEED;
            }

            private static float farthestCorner(Vector2 p, Vector2 low, Vector2 high)
            {
                float dx = Math.Max(Math.Abs(p.X - low.X), Math.Abs(p.X - high.X));
                float dy = Math.Max(Math.Abs(p.Y - low.Y), Math.Abs(p.Y - high.Y));
                return MathF.Sqrt(dx * dx + dy * dy);
            }

            public override void Update(BulletField f, double now)
            {
                double life = lifeOf(f);

                for (int k = 0; k < Slams; k++)
                {
                    if (k % DanmakusuBalance.RAIKO_RIPPLE_EVERY != 0)
                        continue; // (only some hits of the drums send a ripple out)

                    double slammed = hitTimes[k];

                    if (now < slammed)
                    {
                        known[k] = false;
                        continue;
                    }

                    if (now > slammed + life)
                        continue;

                    if (!known[k])
                    {
                        known[k] = true;
                        centres[k] = TaikoAt(f, drums[k], slammed);
                    }

                    f.addRipple(centres[k], slammed);
                }

                // where Raiko is when a stick is struck is where it pushes from (taken in the first frame after the hit)
                for (int s = 0; s < stickTimes.Length; s++)
                {
                    if (now < stickTimes[s])
                    {
                        stickKnown[s] = false;
                        continue;
                    }

                    if (!stickKnown[s] && now - stickTimes[s] < 200)
                    {
                        stickKnown[s] = true;
                        stickCentres[s] = f.bossVisualOf(Period);
                    }
                }
            }

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < Slams; k++)
                {
                    if (hitTimes[k] > time)
                        known[k] = false;
                }

                for (int s = 0; s < stickTimes.Length; s++)
                {
                    if (stickTimes[s] > time)
                        stickKnown[s] = false;
                }
            }

            /// <summary>0..1: the shake of the latest slam, which dies down over a moment (the field turns it into 1..2 pixels).</summary>
            public float ShakeAt(double now)
            {
                if (now < Start || now > End + DanmakusuBalance.RAIKO_SHAKE_MS)
                    return 0f;

                int k = Math.Clamp(latestHit(now), 0, Slams - 1);
                float u = 1f - (float)((now - hitTimes[k]) / DanmakusuBalance.RAIKO_SHAKE_MS);

                return u > 0.02f ? Math.Min(u, 1f) : 0f;
            }

            public override void Draw(BulletField f, double t)
            {
                float alpha = Alpha(t);

                if (alpha > 0.002f)
                {
                    float size = DanmakusuBalance.RAIKO_TAIKO_SIZE * f.circleRadius;
                    int last = latestHit(t);
                    int count = DanmakusuBalance.RAIKO_TAIKO_COUNT;

                    for (int j = 0; j < count; j++)
                    {
                        Vector2 p = TaikoAt(f, j, t);
                        float pulse = 0f;
                        int k = -1; // (the latest hit of this drum)

                        for (int back = 0; back < count + 1; back++)
                        {
                            int candidate = last - back;

                            if (candidate >= 0 && candidate < Slams && drums[candidate] == j)
                            {
                                k = candidate;
                                break;
                            }
                        }

                        if (k >= 0 && k < Slams)
                            pulse = MathF.Exp(-(float)((t - hitTimes[k]) / DanmakusuBalance.RAIKO_DRUM_GROW_MS));

                        f.stageBack.Add(StageTexture.Taiko, p, new Vector2(size * (1f + DanmakusuBalance.RAIKO_DRUM_GROW * pulse)), 0f, alpha, Color4.White);

                        if (pulse > 0.02f)
                            f.stageBack.Add(StageTexture.Glow, p, new Vector2(size * 1.8f), 0f, 0.8f * pulse * alpha, new Color4(1f, 0.8f, 0.4f, 1f), true);
                    }
                }

                for (int k = 0; k < Slams; k++)
                {
                    double age = t - hitTimes[k];

                    // a ripple does not fade: it is there until it has left the playfield altogether
                    if (!known[k] || age <= 0 || age > lifeFor(f, centres[k]))
                        continue;

                    float r = DanmakusuBalance.RAIKO_RIPPLE_SPEED * (float)age;
                    float a = DanmakusuBalance.RAIKO_RIPPLE_ALPHA * smoothStep(age / 60.0);
                    Vector2 c = centres[k];
                    var colour = new Color4(1f, 0.78f, 0.15f, 1f); // (a golden yellow)
                    // (a thin line at the front of the band that makes bullets swell, its edges a little soft: the same thickness however far it has got)
                    f.stageRipple.AddSoftRing(c, r, DanmakusuBalance.RAIKO_RIPPLE_LINE, DanmakusuBalance.RAIKO_RIPPLE_SOFT, a, colour);
                }

                // the push of a stick: a quick ring that grows to the distance the bullets are pushed from
                float reach = DanmakusuBalance.RAIKO_PUSH_RADIUS * f.circleRadius;

                for (int s = 0; s < stickTimes.Length; s++)
                {
                    double age = t - stickTimes[s];

                    if (!stickKnown[s] || age < 0 || age > DanmakusuBalance.RAIKO_PUSH_RING_MS)
                        continue;

                    float u = (float)(age / DanmakusuBalance.RAIKO_PUSH_RING_MS);
                    float eased = 1f - (1f - u) * (1f - u);

                    f.stageRipple.AddRing(stickCentres[s], reach * eased, reach * 0.12f, 0.9f * (1f - u), new Color4(1f, 0.95f, 0.8f, 1f));
                }
            }
        }
    }
}
