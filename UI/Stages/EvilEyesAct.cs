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
        // YuugenMagan (2026-11-27): she stands in the middle of the screen (at the top, where Kaguya stands, until the same day), with her five big circles (YUUGENMAGAN_EYE_CIRCLES circle radii), each with an
        // eye, at the corners of a pentagon round her, joined to each other by flickering lightning (see-through: it does not hurt). Every second bullet fired in her section (anyone's, counted ring by ring as
        // it is fired) is taken from where it was fired and shot from one of her four lower eyes at the player, at its own speed, each eye in turn its own kind: basic, arrowhead, ellipse, star. Her top eye
        // is where her sliders' beams come from, as Junko's do, one beam each at the player (the planner). Every YUUGENMAGAN_THUNDER_BEATS every eye flashes white for YUUGENMAGAN_WARN_BEATS, its bolt shown see-through meanwhile, and then strikes
        // a bolt of Tojiko's lightning the way its number says, twice as thick, with twice as many branches, twice as long, solid for YUUGENMAGAN_BOLT_BEATS (it hurts like a slider body). Every circle hit and
        // slider over in her section is thrown from her eyes in turn at the player, golden, falling as Urumi's do (ObjectFallAct with FromEye).
        // ======================================================================================================================================================

        private sealed class EvilEyesAct : StageAct
        {
            public const int EYES = 5;

            private sealed class Bolt
            {
                public double Time;
                public bool Heard, First; // (the first of the five is heard)
                public readonly List<Vector2> Points = new List<Vector2>();
                public readonly List<List<Vector2>> Branches = new List<List<Vector2>>();
            }

            private static readonly BulletShape[] shapes = { BulletShape.Circle, BulletShape.Triangle, BulletShape.Ellipse, BulletShape.Star };

            private readonly double beat;
            private readonly double[] thunders;
            private readonly List<Bolt> bolts = new List<Bolt>();
            private int nextThunder;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;
            private int shot;
            private readonly List<Vector2> shown = new List<Vector2>();
            private readonly List<float> widths = new List<float>();
            private double resetTime = double.NegativeInfinity;

            public EvilEyesAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();

                for (double t = period.ShootStart + beat * DanmakusuBalance.YUUGENMAGAN_THUNDER_BEATS; t < period.End && list.Count < 4000; t += beat * DanmakusuBalance.YUUGENMAGAN_THUNDER_BEATS)
                    list.Add(t);

                thunders = list.ToArray();
                Start = period.Start;
                End = period.End + beat * (DanmakusuBalance.YUUGENMAGAN_BOLT_BEATS + 2);
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;
            }

            /// <summary>Where she stands: in the middle of the screen (2026-11-27, "boss is on the middle now"; where Kaguya stands, at the top, before).</summary>
            private static Vector2 place(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            /// <summary>Where eye <paramref name="eye"/> is: round her, on a pentagon, 0 at its top corner; then round it, the right one first.</summary>
            public static Vector2 EyeAt(BulletField f, int eye)
            {
                float size = DanmakusuBalance.MAGIC_CIRCLE_RADIUS * DanmakusuBalance.YUUGENMAGAN_PENTAGON;
                Vector2 centre = place(f);
                float angle = -MathF.PI / 2f + eye * MathF.Tau / EYES;
                return centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size;
            }

            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = place(f);
                return now >= Period.Start && now <= Period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                shot = 0;
                bolts.Clear();
                nextThunder = 0;

                while (nextThunder < thunders.Length && thunders[nextThunder] + boltLife < time)
                    nextThunder++;

                resetTime = time;
                ClearBullets();
            }

            private double growMs => beat * 0.25;

            private double boltLife => growMs + beat * DanmakusuBalance.YUUGENMAGAN_BOLT_BEATS + beat * 0.5;

            private float eyeRadius(BulletField f) => f.circleRadius * DanmakusuBalance.YUUGENMAGAN_EYE_CIRCLES;

            // ---- every second bullet is shot again from a lower eye ----

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Period.ShootStart || t > Period.End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.YUUGENMAGAN_EVERY != 0)
                    return false;

                int kind = shot++ % shapes.Length;
                Vector2 at = EyeAt(f, 1 + kind);
                var stored = f.makeStored(emitter, t);
                Vector2 way = f.characterPosition - at;
                way = way.LengthSquared > 1e-4f ? Vector2.Normalize(way) : Vector2.UnitY;
                float big = emitter.BigScale(bullet);
                Vector2 velocity = way * Math.Max(stored.Speed, stageBulletSpeed(Period) * 0.5f);

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = velocity,
                    Face = way,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = shapes[kind],
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Untraced = true,
                    Until = t + f.stageLife(velocity),
                });

                return true;
            }

            // ---- the thunders ----

            private Bolt makeBolt(BulletField f, double time, int eye, int number)
            {
                var bolt = new Bolt { Time = time, First = eye == 0 };
                Vector2 from = EyeAt(f, eye);
                float angle = hash01(number, eye, 601) * MathF.Tau;
                Vector2 way = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Vector2.Distance(f.stageMin, f.stageMax) * 0.7f;
                int seed = number * 7 + eye;

                bolt.Points.AddRange(StormAct.jagged(StormAct.zigzag(from, way, DanmakusuBalance.TOJIKO_BOLT_POINTS, seed, 76), DanmakusuBalance.TOJIKO_JAG_LEVELS, seed, 90));

                // (twice as many branches as Tojiko's, twice as long)
                for (int b = 0; b < DanmakusuBalance.TOJIKO_BRANCHES * 2; b++)
                {
                    int at = 1 + (int)(hash01(seed, b, 78) * (bolt.Points.Count * 0.8f - 1f));
                    float turn = (hash01(seed, b, 79) < 0.5f ? 1f : -1f) * DanmakusuBalance.TOJIKO_BRANCH_DEGREES * (0.6f + 0.6f * hash01(seed, b, 86)) * MathF.PI / 180f;
                    Vector2 reach = turned(way, turn) * (DanmakusuBalance.TOJIKO_BRANCH_LENGTH * 2f * (0.6f + 0.4f * hash01(seed, b, 80)));
                    bolt.Branches.Add(StormAct.jagged(StormAct.zigzag(bolt.Points[at], reach, DanmakusuBalance.TOJIKO_BRANCH_POINTS, seed + b * 31, 81), 1, seed + b * 31, 91));
                }

                return bolt;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (made as the warning begins: where they will strike is known then, for the autoplay too)
                double warn = beat * DanmakusuBalance.YUUGENMAGAN_WARN_BEATS;

                while (nextThunder < thunders.Length && thunders[nextThunder] - warn <= now)
                {
                    double time = thunders[nextThunder];

                    for (int eye = 0; eye < EYES; eye++)
                        bolts.Add(makeBolt(f, time, eye, Id * 1000 + nextThunder));

                    nextThunder++;
                }

                foreach (var bolt in bolts)
                {
                    if (bolt.Heard || now < bolt.Time)
                        continue;

                    bolt.Heard = true;

                    if (now - bolt.Time < 250 && bolt.Time > resetTime && bolt.First)
                        f.playStageSound("tojiko_thunder");
                }

                bolts.RemoveAll(bolt => now > bolt.Time + boltLife);
            }

            private float lengthAt(Bolt bolt, double t)
            {
                double age = t - bolt.Time;
                double gone = boltLife - beat * 0.5;
                return age < 0 ? 0f : age < growMs ? smoothStep(age / growMs) : age < gone ? 1f : 1f - smoothStep((age - gone) / (beat * 0.5));
            }

            private bool solid(Bolt bolt, double t) => t >= bolt.Time + growMs && t < bolt.Time + boltLife - beat * 0.5;

            private float boltHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.TOJIKO_SOLID_WIDTH * 2f;

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || t < Period.Start)
                    return;

                var body = Color4.White;
                var edge = new Color4(0.85f, 0.4f, 1f, 1f);

                // the lightning between her eyes: it flickers, see-through
                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (presence > 0.01f)
                {
                    int flicker = (int)(t / 90.0);
                    float linkHalf = f.circleRadius * DanmakusuBalance.YUUGENMAGAN_LINK_WIDTH;

                    for (int eye = 0; eye < EYES; eye++)
                    {
                        Vector2 a = EyeAt(f, eye), b = EyeAt(f, (eye + 1) % EYES);
                        Vector2 apart = b - a;
                        float length = apart.Length;

                        if (length < 1f)
                            continue;

                        Vector2 way = apart / length;
                        float trim = eyeRadius(f) * 0.8f;
                        var line = StormAct.jagged(StormAct.zigzag(a + way * trim, way * (length - 2f * trim), 6, flicker * 5 + eye, 140), 2, flicker * 5 + eye, 141);

                        int drawn = beams;
                        f.addPlainBeam(line, linkHalf, body, edge, ref beams, ref pointTotal);

                        if (beams > drawn)
                        {
                            f.laserInstances[beams - 1].Bloom = 0.6f * presence;
                            Array.Fill(f.laserFadePointBuffer, DanmakusuBalance.YUUGENMAGAN_LINK_ALPHA * presence, pointTotal - line.Count, line.Count);
                        }
                    }
                }

                // the bolts
                float half = boltHalf(f);

                foreach (var bolt in bolts)
                {
                    // (its preview: the whole bolt, see-through and harmless, while the eye flashes - 2026-11-27)
                    if (t < bolt.Time)
                    {
                        float preview = DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep((t - (bolt.Time - beat * DanmakusuBalance.YUUGENMAGAN_WARN_BEATS)) / (beat * 0.25));
                        int before = beams;
                        f.addPlainBeam(bolt.Points, half, body, edge, ref beams, ref pointTotal);

                        if (beams > before)
                            Array.Fill(f.laserFadePointBuffer, preview, pointTotal - bolt.Points.Count, bolt.Points.Count);

                        foreach (var branch in bolt.Branches)
                        {
                            before = beams;
                            f.addPlainBeam(branch, half * DanmakusuBalance.TOJIKO_BRANCH_WIDTH, body, edge, ref beams, ref pointTotal);

                            if (beams > before)
                                Array.Fill(f.laserFadePointBuffer, preview * DanmakusuBalance.TOJIKO_BRANCH_ALPHA, pointTotal - branch.Count, branch.Count);
                        }

                        continue;
                    }

                    float length = lengthAt(bolt, t) * (bolt.Points.Count - 1);

                    if (length < 0.02f)
                        continue;

                    shown.Clear();
                    int whole = (int)length;

                    for (int i = 0; i <= whole && i < bolt.Points.Count; i++)
                        shown.Add(bolt.Points[i]);

                    if (whole + 1 < bolt.Points.Count)
                        shown.Add(Vector2.Lerp(bolt.Points[whole], bolt.Points[whole + 1], length - whole));

                    int piece = ++f.beamPieces;
                    int drawn = beams;
                    f.addPlainBeam(shown, half, body, edge, ref beams, ref pointTotal);

                    if (beams > drawn)
                    {
                        f.laserInstances[beams - 1].Piece = piece;
                        f.laserInstances[beams - 1].CoreColour = Color4.White;
                        f.laserInstances[beams - 1].Bloom = DanmakusuBalance.TOJIKO_BLOOM;
                    }

                    foreach (var points in bolt.Branches)
                    {
                        widths.Clear();

                        if (t < bolt.Time + growMs)
                            continue; // (they come out once the bolt has struck)

                        for (int i = 0; i < points.Count; i++)
                            widths.Add(Math.Max(half * DanmakusuBalance.TOJIKO_BRANCH_WIDTH * (1f - i / (float)(points.Count - 1)), 0.4f));

                        drawn = beams;
                        f.addPlainBeam(points, half * DanmakusuBalance.TOJIKO_BRANCH_WIDTH, body, edge, ref beams, ref pointTotal, widths);

                        if (beams > drawn)
                        {
                            f.laserInstances[beams - 1].Piece = piece;
                            f.laserInstances[beams - 1].CoreColour = Color4.White;
                            Array.Fill(f.laserFadePointBuffer, DanmakusuBalance.TOJIKO_BRANCH_ALPHA * lengthAt(bolt, t), pointTotal - points.Count, points.Count);
                        }
                    }
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Period.Start)
                    return;

                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (presence <= 0.002f)
                    return;

                float r = eyeRadius(f);

                // (the warning: every eye flashes white, faster and faster, before it strikes)
                float flash = 0f;
                double warn = beat * DanmakusuBalance.YUUGENMAGAN_WARN_BEATS;

                for (int k = Math.Max(nextThunder - 2, 0); k < thunders.Length && thunders[k] - warn <= t; k++)
                {
                    if (t >= thunders[k])
                        continue;

                    float u = (float)((t - (thunders[k] - warn)) / warn);
                    flash = Math.Max(flash, (0.5f + 0.5f * MathF.Cos(u * u * MathF.PI * 8f)) * smoothStep(u / 0.2f));
                }

                for (int eye = 0; eye < EYES; eye++)
                {
                    Vector2 at = EyeAt(f, eye);
                    Vector2 looks = f.characterPosition - at;
                    float angle = looks.LengthSquared > 1f ? MathF.Atan2(looks.Y, looks.X) * 180f / MathF.PI : 90f;

                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(r * 3.2f), 0f, 0.45f * presence, new Color4(0.85f, 0.3f, 1f, 1f), true);
                    f.stageBack.Add(StageTexture.Orb, at, new Vector2(r * 2f), 0f, presence, new Color4(0.55f, 0.2f, 0.75f, 1f));
                    f.stageBack.Add(StageTexture.Orb, at, new Vector2(r * 2f * MOKOU_BODY_SCALE), 0f, presence, new Color4(0.16f, 0.05f, 0.22f, 1f));
                    f.stageBack.Add(StageTexture.Eye, at, new Vector2(r * 1.7f), angle, presence, new Color4(1f, 0.85f, 0.95f, 1f));

                    if (flash > 0.01f)
                        f.stageBack.Add(StageTexture.Glow, at, new Vector2(r * 4.5f), 0f, flash * presence, Color4.White, true);
                }
            }

            /// <summary>The bolts hurt like a slider body once they have struck (the lightning between her eyes does not).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float half = boltHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE;
                Vector2 player = f.characterPosition;

                foreach (var bolt in bolts)
                {
                    if (!solid(bolt, now))
                        continue;

                    for (int i = 1; i < bolt.Points.Count; i++)
                    {
                        if (distanceToSegment(player, bolt.Points[i - 1], bolt.Points[i]) <= half + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                            return true;
                    }

                    foreach (var points in bolt.Branches)
                    {
                        for (int i = 1; i < points.Count; i++)
                        {
                            if (distanceToSegment(player, points[i - 1], points[i]) <= half * DanmakusuBalance.TOJIKO_BRANCH_WIDTH * (1f - (i - 0.5f) / (points.Count - 1)) + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
