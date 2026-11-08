using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Soga no Tojiko: heavy thunderclouds hang along the top of the dark grey screen, over the lightning and the rain and under every other bullet, and flash where a bolt starts as it drops. Every bullet that goes out of the top falls back from there as an ellipse half its size, at half of Zanmu's pull, twice as bright and never dim, and a second one
        // from somewhere else along the top. Every two beats a bolt of lightning (a zigzag of a beam that glows, light blue while it is see-through and white once it is solid, drawn in one piece with its branches, with stretches of every length at about one angle and small kinks all along it, and thin, fainter branches that end in narrow tips, with smaller branches off those) grows from the top to the bottom in one beat; it is see-through and harmless for four beats, then solid for four, half again as thick, hurting like a slider body, and goes the way it came in one beat.
        // Bullets near a bolt that is solid are dim; every second bolt thunders, heard only while it is solid (the thunder fades out as it stops hurting). As a bolt turns solid five bullets of its colour, half a plain bullet's size and never dim, fly back from where it ends (or leaves the playfield), spread over 120 degrees. She shoots lightning too: every four beats of an attack a bolt grows from her at where the player is then, and stays that way. In a spinner the clouds are in the middle of the
        // screen, and the ellipses and bolts go from there outwards. The bolts are worked out from their number, so a replay and a seek give the same.
        // ======================================================================================================================================================

        private sealed class StormAct : StageAct
        {
            private sealed class Bolt
            {
                public double Time;
                public bool Aimed; // (hers: no see-through time)
                public bool Struck; // (it has turned solid: its thunder has been heard, its bullets thrown)
                public bool Thunders; // (every second bolt is heard)
                public SampleChannel? Thunder; // (its thunder while it plays: it is heard only while the bolt is solid)
                public readonly List<Vector2> Points = new List<Vector2>();
                public readonly List<(float From, float Size, List<Vector2> Points)> Branches = new List<(float, float, List<Vector2>)>(); // (how far along the bolt each one starts, 0..1, and how thick it is compared to a branch)
            }

            private readonly double beat;
            private readonly bool spinner;
            private readonly double[] skyBolts;
            private readonly double[] herBolts;
            private readonly List<Bolt> bolts = new List<Bolt>();
            private readonly List<Vector2> shown = new List<Vector2>();
            private readonly List<float> branchWidths = new List<float>();
            private int nextSky, nextHers;
            private int crossings;

            public StormAct(BossPeriod period, double[] skyBolts, double[] herBolts)
            {
                Period = period;
                this.skyBolts = skyBolts;
                this.herBolts = herBolts;
                beat = Math.Max(period.BeatLength, 1);
                spinner = period.Kind == BossKind.Spinner;
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            public override void Reset(BulletField f, double time)
            {
                foreach (var bolt in bolts)
                {
                    if (bolt.Thunder != null)
                        f.fadeOutSound(bolt.Thunder, DanmakusuBalance.TOJIKO_THUNDER_FADE_MS);
                }

                bolts.Clear();
                nextSky = nextHers = 0;
                crossings = 0;
                ClearBullets();
            }

            private double growMs => beat * DanmakusuBalance.TOJIKO_GROW_BEATS;

            private double ghostMs(Bolt bolt) => bolt.Aimed ? 0 : beat * DanmakusuBalance.TOJIKO_GHOST_BEATS;

            private double solidMs(Bolt bolt) => beat * (bolt.Aimed ? DanmakusuBalance.TOJIKO_HER_SOLID_BEATS : DanmakusuBalance.TOJIKO_SOLID_BEATS);

            private double lifeOf(Bolt bolt) => ghostMs(bolt) + growMs + solidMs(bolt) + growMs;

            private double solidFrom(Bolt bolt) => bolt.Time + growMs + ghostMs(bolt);

            /// <summary>How much of the bolt is there at <paramref name="t"/> (0..1).</summary>
            private float lengthAt(Bolt bolt, double t)
            {
                double age = t - bolt.Time;
                double gone = lifeOf(bolt) - growMs;

                return age < growMs ? smoothStep(age / growMs) : age < gone ? 1f : 1f - smoothStep((age - gone) / growMs);
            }

            private bool isSolid(Bolt bolt, double t) => t >= solidFrom(bolt) && t < bolt.Time + lifeOf(bolt) - growMs;

            /// <summary>How many times as thick the bolt is at <paramref name="t"/>: it swells as it turns solid.</summary>
            private float thickness(Bolt bolt, double t) => 1f + (DanmakusuBalance.TOJIKO_SOLID_WIDTH - 1f) * smoothStep((t - solidFrom(bolt)) / (beat * 0.25));

            /// <summary>Where the points of a zigzag of <paramref name="stretches"/> stretches lie along it (0..1): every stretch has a length of its own, by the number of the bolt, most of them short and a few long.</summary>
            private static float[] zigzagPlaces(int stretches, int number, int salt)
            {
                var places = new float[stretches + 1];

                for (int i = 1; i <= stretches; i++)
                {
                    float chance = hash01(number, i, salt);

                    places[i] = places[i - 1] + DanmakusuBalance.TOJIKO_SHORTEST_ZIG + (DanmakusuBalance.TOJIKO_LONGEST_ZIG - DanmakusuBalance.TOJIKO_SHORTEST_ZIG) * chance * chance;
                }

                for (int i = 1; i <= stretches; i++)
                    places[i] /= places[stretches];

                return places;
            }

            /// <summary>
            /// A zigzag from <paramref name="from"/> along <paramref name="way"/>: every stretch leans about the same way off the line, to the one side and then to the other, however long it is. The whole of it is then turned a little, so
            /// that it ends where it is going and the angles stay as they are.
            /// </summary>
            private static List<Vector2> zigzag(Vector2 from, Vector2 way, int stretches, int number, int salt)
            {
                float[] along = zigzagPlaces(stretches, number, salt);
                var off = new float[stretches + 1];
                float lean = MathF.Tan(DanmakusuBalance.TOJIKO_ZIG_DEGREES * MathF.PI / 180f);

                for (int i = 1; i <= stretches; i++)
                    off[i] = off[i - 1] + (i % 2 == 0 ? 1f : -1f) * lean * (1f + DanmakusuBalance.TOJIKO_ZIG_SPREAD * (2f * hash01(number, i, salt + 1) - 1f)) * (along[i] - along[i - 1]);

                Vector2 across = new Vector2(-way.Y, way.X);
                float tilt = off[stretches];
                float straight = 1f / (1f + tilt * tilt), turn = -tilt * straight;
                var points = new List<Vector2>(stretches + 1);

                for (int i = 0; i <= stretches; i++)
                    points.Add(from + way * (along[i] * straight - off[i] * turn) + across * (along[i] * turn + off[i] * straight));

                return points;
            }

            /// <summary>Every stretch broken in two at a point thrown a little to the side, <paramref name="levels"/> times over: the small kinks of real lightning on its big zigzag.</summary>
            private static List<Vector2> jagged(List<Vector2> points, int levels, int number, int salt)
            {
                for (int level = 0; level < levels; level++)
                {
                    var finer = new List<Vector2>(points.Count * 2);

                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        Vector2 a = points[i], c = points[i + 1], way = c - a;
                        float kink = (hash01(number, i + level * 1000, salt) * 2f - 1f) * DanmakusuBalance.TOJIKO_KINK;

                        finer.Add(a);
                        finer.Add((a + c) * 0.5f + new Vector2(-way.Y, way.X) * kink);
                    }

                    finer.Add(points[^1]);
                    points = finer;
                }

                return points;
            }

            /// <summary>A jagged line from one end to the other, with its branches (by the number of the bolt).</summary>
            private Bolt makeBolt(double time, Vector2 from, Vector2 to, int number, bool aimed)
            {
                var bolt = new Bolt { Time = time, Aimed = aimed, Thunders = number % 2 == 0 };
                Vector2 way = to - from;

                bolt.Points.AddRange(jagged(zigzag(from, way, DanmakusuBalance.TOJIKO_BOLT_POINTS, number, 76), DanmakusuBalance.TOJIKO_JAG_LEVELS, number, 90));

                // (smaller branches leave it at sharp angles, and jag on towards a narrow tip)
                for (int b = 0; b < DanmakusuBalance.TOJIKO_BRANCHES; b++)
                {
                    int at = 1 + (int)(hash01(number, b, 78) * (bolt.Points.Count * 0.8f - 1f)); // (anywhere but near its end)
                    float turn = (hash01(number, b, 79) < 0.5f ? 1f : -1f) * DanmakusuBalance.TOJIKO_BRANCH_DEGREES * (0.6f + 0.6f * hash01(number, b, 86)) * MathF.PI / 180f;
                    Vector2 reach = turned(way, turn) * (DanmakusuBalance.TOJIKO_BRANCH_LENGTH * (0.6f + 0.4f * hash01(number, b, 80)));
                    var branch = jagged(zigzag(bolt.Points[at], reach, DanmakusuBalance.TOJIKO_BRANCH_POINTS, number + b * 31, 81), 1, number + b * 31, 91);
                    float start = at / (float)(bolt.Points.Count - 1);

                    bolt.Branches.Add((start, 1f, branch));

                    // (and smaller ones leave every branch in turn)
                    for (int s = 0; s < DanmakusuBalance.TOJIKO_SUB_BRANCHES; s++)
                    {
                        int seed = number + b * 31 + s * 97 + 1;
                        int on = 1 + (int)(hash01(seed, s, 82) * (branch.Count - 2));
                        float subTurn = (hash01(seed, s, 85) < 0.5f ? 1f : -1f) * DanmakusuBalance.TOJIKO_BRANCH_DEGREES * MathF.PI / 180f;
                        Vector2 subReach = turned(reach, subTurn) * (DanmakusuBalance.TOJIKO_SUB_LENGTH * (0.7f + 0.3f * hash01(seed, s, 83)));
                        var twig = jagged(zigzag(branch[on], subReach, DanmakusuBalance.TOJIKO_SUB_POINTS, seed, 84), 1, seed, 92);

                        bolt.Branches.Add((Math.Min(start + 0.1f * on / (float)(branch.Count - 1), 0.95f), 0.6f, twig));
                    }
                }

                return bolt;
            }

            private Vector2 middle(BulletField f) => (f.stageMin + f.stageMax) * 0.5f;

            /// <summary>Where the bolt ends, or leaves the playfield if it does that first.</summary>
            private static Vector2 tipOf(BulletField f, Bolt bolt)
            {
                bool onStage(Vector2 p) => p.X >= f.stageMin.X - 1f && p.X <= f.stageMax.X + 1f && p.Y >= f.stageMin.Y - 1f && p.Y <= f.stageMax.Y + 1f;
                static float leaves(float from, float to, float least, float most) => to > most ? (most - from) / (to - from) : to < least ? (least - from) / (to - from) : 1f;

                int last = bolt.Points.Count - 1;

                while (last > 0 && !onStage(bolt.Points[last]))
                    last--;

                if (last == bolt.Points.Count - 1)
                    return bolt.Points[last];

                Vector2 inside = bolt.Points[last], outside = bolt.Points[last + 1];
                float share = Math.Min(leaves(inside.X, outside.X, f.stageMin.X, f.stageMax.X), leaves(inside.Y, outside.Y, f.stageMin.Y, f.stageMax.Y));

                return Vector2.Lerp(inside, outside, Math.Clamp(share, 0f, 1f));
            }

            /// <summary>The bolt has turned solid: from where it ends bullets of its colour fly back the way it came, in a fan.</summary>
            private void throwBack(BulletField f, Bolt bolt, double now)
            {
                double time = solidFrom(bolt);
                Vector2 tip = tipOf(f, bolt);
                Vector2 back = bolt.Points[0] - tip;
                float facing = back.LengthSquared > 1e-4f ? MathF.Atan2(back.Y, back.X) : -MathF.PI / 2f;
                float spread = DanmakusuBalance.TOJIKO_IMPACT_DEGREES * MathF.PI / 180f;
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.TOJIKO_IMPACT_SIZE;
                float speed = stageBulletSpeed(Period);

                for (int i = 0; i < DanmakusuBalance.TOJIKO_IMPACT_BULLETS; i++)
                {
                    float angle = facing + spread * (i / (float)(DanmakusuBalance.TOJIKO_IMPACT_BULLETS - 1) - 0.5f);
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                    double until = time + f.stageLife(velocity);

                    // (after a seek: it has long gone)
                    if (until <= now)
                        continue;

                    AddBullet(new StageBullet
                    {
                        T0 = time,
                        P0 = tip,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Shape = BulletShape.Circle,
                        Tint = BulletRenderer.TINT_LIGHTNING,
                        TintAmount = 1f,
                        Undimmed = true,
                        Until = until,
                    });
                }
            }

            public override void Update(BulletField f, double now)
            {
                // (the thunder is heard only while its bolt is solid: it fades out as the bolt stops hurting, and when the game is paused or failed)
                foreach (var bolt in bolts)
                {
                    if (bolt.Thunder != null && (f.failed || f.soundStillFrames >= 8 || !isSolid(bolt, now)))
                    {
                        f.fadeOutSound(bolt.Thunder, DanmakusuBalance.TOJIKO_THUNDER_FADE_MS);
                        bolt.Thunder = null;
                    }
                }

                if (f.failed)
                    return;

                float reach = Vector2.Distance(f.stageMin, f.stageMax) * 0.5f;

                while (nextSky < skyBolts.Length && skyBolts[nextSky] <= now)
                {
                    int number = Id * 1000 + nextSky;

                    if (spinner)
                    {
                        float angle = hash01(number, 1, 75) * MathF.Tau;
                        bolts.Add(makeBolt(skyBolts[nextSky], middle(f), middle(f) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach, number, false));
                    }
                    else
                    {
                        float x = f.stageMin.X + (f.stageMax.X - f.stageMin.X) * hash01(number, 2, 75);
                        float lean = (f.stageMax.X - f.stageMin.X) * 0.1f * (hash01(number, 3, 75) - 0.5f);
                        bolts.Add(makeBolt(skyBolts[nextSky], new Vector2(x, f.stageMin.Y), new Vector2(x + lean, f.stageMax.Y), number, false));
                    }

                    nextSky++;
                }

                while (nextHers < herBolts.Length && herBolts[nextHers] <= now)
                {
                    Vector2 from = f.bossVisualOf(Period);
                    Vector2 aim = f.characterPosition - from;
                    Vector2 way = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;

                    bolts.Add(makeBolt(herBolts[nextHers], from, from + way * reach * 2f, Id * 2000 + nextHers, true));
                    nextHers++;
                }

                // (every second bolt thunders as it turns solid, and every one throws its bullets back)
                foreach (var bolt in bolts)
                {
                    if (bolt.Struck || now < solidFrom(bolt))
                        continue;

                    bolt.Struck = true;
                    throwBack(f, bolt, now);

                    if (bolt.Thunders && now - solidFrom(bolt) < 250 && isSolid(bolt, now))
                        bolt.Thunder = f.playHeldSound("tojiko_thunder");
                }

                foreach (var bolt in bolts)
                {
                    if (bolt.Thunder != null && now > bolt.Time + lifeOf(bolt))
                        f.fadeOutSound(bolt.Thunder, DanmakusuBalance.TOJIKO_THUNDER_FADE_MS);
                }

                bolts.RemoveAll(bolt => now > bolt.Time + lifeOf(bolt));
            }

            /// <summary>A bullet that goes out of the top falls back as a small bright ellipse (in a spinner it is shot from the clouds outwards). It has gone, so nothing is taken.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                float top = f.stageMin.Y;

                if (f.failed || t < Start || t >= End || from.Y < top || to.Y >= top || crossings++ % DanmakusuBalance.TOJIKO_EVERY != 0)
                    return false;

                var stored = f.makeStored(emitter, t);
                float pull = DanmakusuBalance.ZANMU_GRAVITY / DanmakusuBalance.TOJIKO_FALL;
                float size = DanmakusuBalance.TOJIKO_ELLIPSE_SIZE;
                float across = f.stageMax.Y - f.stageMin.Y;

                // (the first drop falls where the bullet left, the others somewhere else along the top, by the number of the crossing; in a spinner each goes its own way)
                for (int drop = 0; drop < DanmakusuBalance.TOJIKO_RAIN_DROPS; drop++)
                {
                    float angle = hash01(crossings, 4 + drop, 75) * MathF.Tau;
                    Vector2 way = spinner ? new Vector2(MathF.Cos(angle), MathF.Sin(angle)) : Vector2.UnitY;
                    float x = drop == 0 ? to.X : f.stageMin.X + (f.stageMax.X - f.stageMin.X) * hash01(crossings, drop, 76);

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = spinner ? middle(f) : new Vector2(x, top),
                        Acc = way * pull,
                        Face = way,
                        Radius = stored.Radius * size,
                        Hit = stored.Hit * size,
                        Style = stored.Style,
                        Shape = BulletShape.Ellipse,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = 1f - (1f - stored.Shade) * DanmakusuBalance.TOJIKO_RAIN_BRIGHT, // (below 0: brighter than it was)
                        Undimmed = true,
                        UnderClouds = true,
                        Until = t + Math.Sqrt(2.0 * (across + stored.Radius * 4f) / pull),
                    });
                }

                return false;
            }

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                if (f.failed)
                    return;

                foreach (var bolt in bolts)
                {
                    if (!isSolid(bolt, t))
                        continue;

                    for (int i = 0; i < bolt.Points.Count; i += DanmakusuBalance.TOJIKO_DIM_EVERY)
                        into.Add(new StageDim { Centre = bolt.Points[i], Radius = f.circleRadius * DanmakusuBalance.TOJIKO_DIM_CIRCLES, Strength = DanmakusuBalance.TOJIKO_DIM });
                }
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                float width = f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);
                var ghost = new Color4(0.7f, 0.78f, 1f, 1f); // (light blue while it is see-through, white once it is solid, blue at the edge, a white core)
                var edge = new Color4(0.5f, 0.6f, 1f, 1f);

                foreach (var bolt in bolts)
                {
                    if (t < bolt.Time)
                        continue;

                    float length = lengthAt(bolt, t) * (bolt.Points.Count - 1);
                    float thick = width * thickness(bolt, t);

                    if (length < 0.02f)
                        continue;

                    // (it turns white as it swells; the bolt and its branches are one piece, drawn layer by layer together, with no seams where the branches start)
                    float lit = smoothStep((t - solidFrom(bolt)) / (beat * 0.25));
                    var body = new Color4(ghost.R + (1f - ghost.R) * lit, ghost.G + (1f - ghost.G) * lit, 1f, 1f);
                    int piece = ++f.beamPieces;

                    shown.Clear();
                    int whole = (int)length;

                    for (int i = 0; i <= whole; i++)
                        shown.Add(bolt.Points[i]);

                    if (whole + 1 < bolt.Points.Count)
                        shown.Add(Vector2.Lerp(bolt.Points[whole], bolt.Points[whole + 1], length - whole));

                    int drawn = beams;
                    f.addPlainBeam(shown, thick, body, edge, ref beams, ref pointTotal);

                    // (it glows; see-through until it is solid)
                    if (beams > drawn)
                    {
                        onePiece(f, beams - 1, piece);
                        f.laserInstances[beams - 1].Bloom = DanmakusuBalance.TOJIKO_BLOOM * (t < solidFrom(bolt) ? DanmakusuBalance.TOJIKO_GHOST_ALPHA : 1f);

                        if (t < solidFrom(bolt))
                            Array.Fill(f.laserFadePointBuffer, DanmakusuBalance.TOJIKO_GHOST_ALPHA, pointTotal - shown.Count, shown.Count);
                    }

                    // (the branches grow out once the bolt has got to them, thin from the start and narrowing to a point)
                    float reached = length / (bolt.Points.Count - 1);

                    foreach (var (from, size, points) in bolt.Branches)
                    {
                        if (reached <= from)
                            continue;

                        // (each has grown out by the time the bolt gets to the far end)
                        float grown = Math.Min((reached - from) / Math.Max(1f - from, 0.05f), 1f) * (points.Count - 1);

                        shown.Clear();
                        branchWidths.Clear();

                        for (int i = 0; i < points.Count; i++)
                        {
                            if (i <= grown)
                                shown.Add(points[i]);
                            else
                            {
                                shown.Add(Vector2.Lerp(points[i - 1], points[i], grown - (i - 1)));
                                break;
                            }
                        }

                        for (int i = 0; i < shown.Count; i++)
                            branchWidths.Add(Math.Max(thick * DanmakusuBalance.TOJIKO_BRANCH_WIDTH * size * (1f - i / (float)(points.Count - 1)), 0.4f));

                        if (shown.Count < 2)
                            continue;

                        f.addPlainBeam(shown, thick * DanmakusuBalance.TOJIKO_BRANCH_WIDTH * size, body, edge, ref beams, ref pointTotal, branchWidths);
                        onePiece(f, beams - 1, piece);

                        // (fainter than the bolt, and see-through with it)
                        Array.Fill(f.laserFadePointBuffer, DanmakusuBalance.TOJIKO_BRANCH_ALPHA * (t < solidFrom(bolt) ? DanmakusuBalance.TOJIKO_GHOST_ALPHA : 1f), pointTotal - shown.Count, shown.Count);
                    }
                }
            }

            /// <summary>Beam <paramref name="index"/> is part of the bolt's piece, with a white core.</summary>
            private static void onePiece(BulletField f, int index, int piece)
            {
                f.laserInstances[index].Piece = piece;
                f.laserInstances[index].CoreColour = Color4.White;
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float width = f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * DanmakusuBalance.BULLET_HITBOX_SCALE;

                foreach (var bolt in bolts)
                {
                    if (!isSolid(bolt, now))
                        continue;

                    float thick = width * thickness(bolt, now);
                    float reach = thick + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    for (int i = 1; i < bolt.Points.Count; i++)
                    {
                        if (distanceToSegment(f.characterPosition, bolt.Points[i - 1], bolt.Points[i]) <= reach)
                            return true;
                    }

                    foreach (var (_, size, points) in bolt.Branches)
                    {
                        for (int i = 1; i < points.Count; i++)
                        {
                            if (distanceToSegment(f.characterPosition, points[i - 1], points[i]) <= thick * DanmakusuBalance.TOJIKO_BRANCH_WIDTH * size * (1f - (i - 0.5f) / (points.Count - 1)) + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                                return true;
                        }
                    }
                }

                return false;
            }

            private static void addCloud(BulletField f, Vector2 place, Vector2 size, float alpha, Color4 colour, bool additive = false, bool flash = false)
                => f.stormClouds.Add(new CloudSprite { Position = place, Size = size, Colour = new Color4(colour.R, colour.G, colour.B, Math.Min(alpha, 1f)), Additive = additive, Flash = flash });

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Period.Start)
                    return;

                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

                if (presence <= 0.002f)
                    return;

                if (f.hasScreenBounds)
                {
                    f.stormBackground.Position = f.screenMin;
                    f.stormBackground.Size = f.screenMax - f.screenMin;
                }

                f.stormBackground.Alpha = Math.Max(f.stormBackground.Alpha, presence * DanmakusuBalance.TOJIKO_BACKGROUND_ALPHA);

                // (the clouds: heavy dark billows with lit tops that drift and swell)
                var dark = new Color4(0.16f, 0.17f, 0.24f, 1f);
                var lit = new Color4(0.72f, 0.75f, 0.85f, 1f);
                float width = f.stageMax.X - f.stageMin.X;
                float size = width / DanmakusuBalance.TOJIKO_CLOUDS * 2.6f;

                for (int i = 0; i < DanmakusuBalance.TOJIKO_CLOUDS; i++)
                {
                    float drift = MathF.Sin((float)t * 0.0007f + i * 1.7f) * size * 0.25f;
                    float swell = 1f + 0.12f * MathF.Sin((float)t * 0.0016f + i * 2.3f);
                    float along = (i + 0.5f) / DanmakusuBalance.TOJIKO_CLOUDS;
                    Vector2 place = spinner
                        ? middle(f) + new Vector2(MathF.Cos(i * 2.4f), MathF.Sin(i * 2.4f)) * (size * 0.5f * (0.4f + 0.6f * along))
                        : new Vector2(f.stageMin.X + width * along + drift, f.stageMin.Y + size * 0.14f * (1f + MathF.Sin(i * 2.1f)));
                    Vector2 puff = new Vector2(size * swell, size * 0.72f * swell);

                    addCloud(f, place, puff * 1.25f, DanmakusuBalance.TOJIKO_CLOUD_ALPHA * presence, dark);
                    addCloud(f, place + new Vector2(size * 0.22f, size * 0.1f), puff * 0.8f, DanmakusuBalance.TOJIKO_CLOUD_ALPHA * presence, dark);
                    addCloud(f, place - new Vector2(size * 0.1f, size * 0.12f), puff * 0.6f, 0.55f * presence, lit, true);
                }

                // (the cloud flashes where a bolt of the sky starts, as it drops; hers start from her)
                foreach (var bolt in bolts)
                {
                    double since = t - solidFrom(bolt);

                    if (!bolt.Aimed && since >= 0 && since < beat)
                        addCloud(f, bolt.Points[0], new Vector2(size * 2f, size * 1.4f), (1f - (float)(since / beat)) * presence, Color4.White, true, true);
                }
            }
        }
    }
}
