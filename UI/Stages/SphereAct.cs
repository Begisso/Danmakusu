using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Star Sapphire (2026-11-21): she stands in the middle of the screen all her section and shoots stars out (Marisa's: the bosses' soft star, since 2026-11-22) that make a sphere of lines round her - the user's animation
        // (Sphere_rotating_transparency.gif): meridians and parallels, a star every SAPPHIRE_STEP_DEGREES along each, seen a little from above, turning about the line from pole to pole, and the whole of it
        // spinning round her in the plane of the screen once in SAPPHIRE_SPIN_BEATS (2026-11-22; it tumbled through its equator). Both its sides are
        // seen (it is see-through) and every star hurts like a slider body. Over SAPPHIRE_GROW_BEATS the sphere grows from her until its edge is just past the farthest edge of the screen, turns as big for
        // SAPPHIRE_HOLD_BEATS, is pulled back into her over SAPPHIRE_BACK_BEATS, and the moment it is in it comes again (SAPPHIRE_REST_BEATS, 0), turning the other way. No pauses: what is left of the section
        // at its end gets a shorter one. A section too short for one is one as long as it is.
        // Since 2026-11-25 her stars are half as big, every part of a sphere lasts twice as long, and as each has grown another is thrown over it from her, turning the other way, that is never pulled back: it
        // grows on, as fast as the first grew in all, until it is wholly past the screen. Later that day: it is thrown with the sphere, as it begins, and its stars are white.
        // Everything is a closed form of time (the stars are beads: the autoplay asks where they are at any moment).
        // ======================================================================================================================================================

        private sealed class SphereAct : StageAct
        {
            /// <summary>Where every star is on the sphere (1 from its middle; y is up the screen's down, the poles at y -1 and 1).</summary>
            private static readonly Vector3[] points = makePoints();

            private static Vector3[] makePoints()
            {
                var list = new List<Vector3>();
                float step = DanmakusuBalance.SAPPHIRE_STEP_DEGREES * MathF.PI / 180f;
                int parallels = DanmakusuBalance.SAPPHIRE_PARALLELS, meridians = DanmakusuBalance.SAPPHIRE_MERIDIANS;
                var latitudes = new float[parallels];

                Vector3 at(float latitude, float longitude) => new Vector3(MathF.Cos(latitude) * MathF.Cos(longitude), -MathF.Sin(latitude), MathF.Cos(latitude) * MathF.Sin(longitude));

                // (the parallels, evenly between the poles: a star every step round each)
                for (int j = 0; j < parallels; j++)
                {
                    latitudes[j] = -MathF.PI / 2f + MathF.PI * (j + 1) / (parallels + 1);
                    int round = Math.Max((int)MathF.Round(MathF.Tau / step), 3);

                    for (int i = 0; i < round; i++)
                        list.Add(at(latitudes[j], i * MathF.Tau / round));
                }

                // (the meridians, a star every step from pole to pole - not where a parallel has one already, nor at the poles, which get one each)
                for (int i = 0; i < meridians; i++)
                {
                    float longitude = i * MathF.Tau / meridians;

                    for (float latitude = -MathF.PI / 2f + step; latitude < MathF.PI / 2f - step * 0.5f; latitude += step)
                    {
                        if (Array.Exists(latitudes, l => MathF.Abs(l - latitude) < step * 0.25f))
                            continue;

                        list.Add(at(latitude, longitude));
                    }
                }

                list.Add(new Vector3(0f, -1f, 0f));
                list.Add(new Vector3(0f, 1f, 0f));
                return list.ToArray();
            }

            private readonly double beat;

            /// <summary>Every sphere: when it starts, and how long it grows, stands and goes back (ms).</summary>
            private readonly (double Start, double Grow, double Hold, double Back)[] spheres;

            public SphereAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                spheres = Schedule(period).ToArray();
                rolls = new float[spheres.Length];
                rollKnown = new bool[spheres.Length];
                Start = period.ShootStart;
                End = period.End;
            }

            /// <summary>
            /// Every sphere of her section: when it starts, and how long it grows, stands and goes back (ms) - the next as soon as the last is pulled in (after SAPPHIRE_REST_BEATS). A section too short for a whole
            /// sphere has one as long as it is, each part as much shorter. (The cues play the sound of each: BulletField.Cues.)
            /// </summary>
            public static List<(double Start, double Grow, double Hold, double Back)> Schedule(BossPeriod period)
            {
                double beat = Math.Max(period.BeatLength, 1);
                double grow = beat * DanmakusuBalance.SAPPHIRE_GROW_BEATS, hold = beat * DanmakusuBalance.SAPPHIRE_HOLD_BEATS, back = beat * DanmakusuBalance.SAPPHIRE_BACK_BEATS;
                double every = grow + hold + back + beat * DanmakusuBalance.SAPPHIRE_REST_BEATS;
                var list = new List<(double, double, double, double)>();

                double s = period.ShootStart;

                for (; s + grow + hold + back <= period.End + 1 && list.Count < 10000; s += every)
                    list.Add((s, grow, hold, back));

                // (what is left at the end, too short for a whole sphere, has one as long as it is - each part as much shorter: she never stands without one. 2026-11-22, "She has no pause phases": the end of
                // a section was empty. A section too short for a whole one is the same)
                if (period.End > s + beat * 2)
                {
                    double share = (period.End - s) / (grow + hold + back);
                    list.Add((s, grow * share, hold * share, back * share));
                }

                return list;
            }

            // (how far each sphere is turned in the plane of the screen: its line from pole to pole at the player when it begins - taken then, 2026-11-21)
            private readonly float[] rolls;
            private readonly bool[] rollKnown;

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < spheres.Length; k++)
                {
                    if (spheres[k].Start > time)
                        rollKnown[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                for (int k = 0; k < spheres.Length && spheres[k].Start <= now; k++)
                {
                    if (!rollKnown[k])
                    {
                        rollKnown[k] = true;
                        rolls[k] = rollAtPlayer(f);
                    }
                }
            }

            /// <summary>The turn in the plane of the screen that puts the sphere's top pole towards the player.</summary>
            private static float rollAtPlayer(BulletField f)
            {
                Vector2 toPlayer = f.realCharacterPosition - middle(f);
                return toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) + MathF.PI / 2f : 0f;
            }

            /// <summary>The sphere there at <paramref name="t"/> (or -1), how big it is (px from its middle to its edge) and how fast that changes (px per ms).</summary>
            private int sphereAt(BulletField f, double t, out float size, out float growing)
            {
                size = 0f;
                growing = 0f;

                for (int k = 0; k < spheres.Length; k++)
                {
                    var (start, grow, hold, back) = spheres[k];

                    if (t < start || t >= start + grow + hold + back)
                        continue;

                    double u = t - start;
                    float full = fullSize(f);

                    if (u < grow)
                    {
                        // (shot out: fast at first, stopping just as it is as big as it gets)
                        float x = (float)(u / grow);
                        size = full * (1f - (1f - x) * (1f - x));
                        growing = full * 2f * (1f - x) / (float)grow;
                    }
                    else if (u < grow + hold)
                        size = full;
                    else
                    {
                        // (pulled back: slowly at first, ever faster into her)
                        float x = (float)((u - grow - hold) / back);
                        size = full * (1f - x * x);
                        growing = full * 2f * x / (float)back;
                    }

                    return k;
                }

                return -1;
            }

            private float starRadius(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.SAPPHIRE_STAR_SIZE;

            /// <summary>The middle of the screen, where she stands.</summary>
            private static Vector2 middle(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            /// <summary>As big as the sphere gets: its edge just past the farthest edge of the screen from the middle.</summary>
            private float fullSize(BulletField f)
            {
                Vector2 half = (f.stageMax - f.stageMin) * 0.5f;
                return Math.Max(half.X, half.Y) + starRadius(f) * DanmakusuBalance.SAPPHIRE_MARGIN;
            }

            /// <summary>
            /// The sphere thrown with each one (2026-11-25, "on top of same sphere there will be thrown another sphere but it just grows away"; with it, not once it has grown, the same day): it grows from her
            /// at as many px a ms as the first grows in all over its growing, and is never pulled back - it grows on until it is wholly past the screen. Turning the other way, its stars white. Returns its
            /// sphere (or -1) and how big it is.
            /// </summary>
            private int thrownAt(BulletField f, double t, out float size, out float growing)
            {
                size = 0f;
                growing = 0f;
                float full = fullSize(f);
                Vector2 half = (f.stageMax - f.stageMin) * 0.5f;
                float gone = half.Length + starRadius(f) * 2f;

                for (int k = 0; k < spheres.Length; k++)
                {
                    var (start, grow, _, _) = spheres[k];
                    double thrown = start; // (with the sphere itself: 2026-11-25, the same day, "both normal sphere and growing out sphere comes out at the same time" - it was thrown once that had grown)
                    float speed = full / (float)Math.Max(grow, 1);

                    if (t < thrown || t >= thrown + gone / speed)
                        continue;

                    size = speed * (float)(t - thrown);
                    growing = speed;
                    return k;
                }

                return -1;
            }

            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (f.failed)
                    return;

                int k = sphereAt(f, t, out float size, out float growing);

                if (k >= 0 && size >= 1f)
                    addSphere(f, t, into, k, size, growing, false);

                int thrown = thrownAt(f, t, out float thrownSize, out float thrownGrowing);

                if (thrown >= 0 && thrownSize >= 1f)
                    addSphere(f, t, into, thrown, thrownSize, thrownGrowing, true);
            }

            /// <summary>The stars of sphere <paramref name="k"/> as it is at <paramref name="t"/>, <paramref name="size"/> px from its middle to its edge (the one thrown over it, <paramref name="over"/>, turns the other way).</summary>
            private void addSphere(BulletField f, double t, List<StageBead> into, int k, float size, float growing, bool over)
            {
                // (turned about the line from pole to pole, every next sphere the other way round; seen a little from above; no perspective)
                float way = (k % 2 == 0) != over ? 1f : -1f;
                float turn = way * (float)((t - spheres[k].Start) / (beat * DanmakusuBalance.SAPPHIRE_TURN_BEATS) * Math.Tau);
                float cosTurn = MathF.Cos(turn), sinTurn = MathF.Sin(turn);

                // (it no longer tumbles about a line through its equator - 2026-11-22, the same day it began to: "rollback spin around equator")
                const float cosTumble = 1f, sinTumble = 0f;
                float tilt = DanmakusuBalance.SAPPHIRE_TILT_DEGREES * MathF.PI / 180f;
                float cosTilt = MathF.Cos(tilt), sinTilt = MathF.Sin(tilt);

                Vector2 centre = middle(f);
                float radius = starRadius(f);

                // (its line from pole to pole points at the player as it begins: a sphere not begun yet - the autoplay asks ahead - at where the player is now. And it spins round her, in the plane of the
                // screen, once in SAPPHIRE_SPIN_BEATS, the same way round as it turns: 2026-11-22, "Sphere spins around saphire too instead")
                float spinRound = way * (float)((t - spheres[k].Start) / (beat * DanmakusuBalance.SAPPHIRE_SPIN_BEATS) * Math.Tau);
                float roll = (rollKnown[k] ? rolls[k] : rollAtPlayer(f)) + spinRound;
                float cosRoll = MathF.Cos(roll), sinRoll = MathF.Sin(roll);
                float hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE;

                // (the autoplay looks at moments AUTO_SAMPLE_MS apart, and the stars of the rim go round fast: to it they are as much bigger as the fastest goes in half of that)
                if (autoAsking)
                    hit += ((float)(Math.Tau / (beat * DanmakusuBalance.SAPPHIRE_TURN_BEATS) + Math.Tau / (beat * DanmakusuBalance.SAPPHIRE_SPIN_BEATS)) * size + growing) * (float)DanmakusuBalance.AUTO_SAMPLE_MS * 0.5f;
                int style = f.comboStyle(0, true, Period.Rank);
                float spin = (float)(t * 0.004);

                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = points[i];
                    float x = p.X * cosTurn - p.Z * sinTurn;
                    float z0 = p.X * sinTurn + p.Z * cosTurn;
                    float y0 = p.Y * cosTumble - z0 * sinTumble;
                    float z = p.Y * sinTumble + z0 * cosTumble;
                    float y = y0 * cosTilt - z * sinTilt;

                    // (the stars spin, each from an angle of its own)
                    float angle = spin * (i % 2 == 0 ? 1f : -1f) + i * 0.7f;

                    into.Add(new StageBead
                    {
                        Position = centre + new Vector2(x * cosRoll - y * sinRoll, x * sinRoll + y * cosRoll) * size,
                        Radius = radius,
                        Hit = hit,
                        Style = style,
                        Shape = BulletShape.SoftStar, // (Marisa's: the bosses' plump, glowing star - 2026-11-22; the sharp star of the bullets before)
                        Direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle)),
                        Overlay = over ? BulletRenderer.TINT_WHITE : 0, // (the one that grows away is white: 2026-11-25)
                        OverlayAmount = over ? 1f : 0f,
                    });
                }
            }
        }
    }
}
