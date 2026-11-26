using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Matenshi: she always stands in the middle of the screen. When her section begins two big white wings (the user's picture, each MATENSHI_WING_SIZE of the playfield wide) fade in at her back, closed:
        // small and standing up behind her, as if folded; with the sound of Yuyuko's tree growing they come out of her over four beats, each turning out about its shoulder and growing to full, then sway a little,
        // slowly, the two together; they close again over the last beat of her section. The wings hurt like a slider body (StageLayer.WingDistance: the picture's solid part).
        // Every bullet fired in her section while her wings are out (anyone's, counted ring by ring: every MATENSHI_FROM_WINGS_EVERY-th, every second until 2026-11-20) does not come out where it was fired: it comes out of her wings - the two in turn, the places spread evenly over what each wing
        // covers (StageLayer.WING_SPOTS) - as a glowing white feather (the user's picture), flying straight away from her at the speed it had (since 2026-11-26 away from a place below her,
        // MATENSHI_FEATHER_PIVOT_SCREEN of the screen's height, so that feathers go up over her too: from her middle nothing went up between her wings).
        // Her own shot: every four beats two straight beams three circle radii wide, straight down: one over where the player is, one at a place of its own (by its number): for four beats a see-through preview,
        // then they grow down from the top of the screen to the bottom in a quarter of a beat and hurt like a slider body for four beats.
        // ======================================================================================================================================================

        private sealed class FallenAngelAct : StageAct
        {
            private readonly double beat;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;
            private int shot;
            private bool sounded;

            // (her beams: when each preview comes, and where it is, taken then)
            private readonly double[] strikes;
            private readonly float[] strikeX;
            private readonly bool[] known;
            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();

            public FallenAngelAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;

                var list = new List<double>();
                double every = beat * DanmakusuBalance.MATENSHI_BEAM_EVERY_BEATS;
                double whole = beat * (DanmakusuBalance.MATENSHI_BEAM_PREVIEW_BEATS + DanmakusuBalance.MATENSHI_BEAM_LIVE_BEATS);

                for (double t = period.ShootStart; t + whole <= period.End + 1 && list.Count < 10000; t += every)
                    list.Add(t);

                strikes = list.ToArray();
                strikeX = new float[strikes.Length];
                known = new bool[strikes.Length];

                // (the second beam of every strike: a place of its own across the screen, by its number)
                otherShare = new float[strikes.Length];

                for (int k = 0; k < strikes.Length; k++)
                    otherShare[k] = 0.04f + 0.92f * hash01(k, (int)period.Start, 4523);

                StageLayer.PrepareWingShape();
                StageLayer.PrepareWingPieces();
            }

            private readonly float[] otherShare;

            private float otherX(BulletField f, int k) => f.stageMin.X + (f.stageMax.X - f.stageMin.X) * otherShare[k];

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                shot = 0;
                ClearBullets();
                sounded = time > Start;
                soundedStrike = -1;
                resetTime = time;

                for (int k = 0; k < strikes.Length; k++)
                {
                    if (strikes[k] > time)
                        known[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                if (!sounded)
                {
                    sounded = true;

                    if (now - Start < 250)
                        f.playStageSound("yuyuko_tree");
                }

                // (every beam whose preview has come takes where the player is, once)
                for (int k = 0; k < strikes.Length && strikes[k] <= now; k++)
                {
                    if (known[k])
                        continue;

                    known[k] = true;
                    strikeX[k] = Math.Clamp(f.characterPosition.X, f.stageMin.X, f.stageMax.X);
                }

                // the beams come down with Byakuren's beam sound as they turn solid (2026-11-21)
                for (; soundedStrike + 1 < strikes.Length && solidAt(soundedStrike + 1) <= now; soundedStrike++)
                {
                    double solid = solidAt(soundedStrike + 1);

                    if (now - solid < 250 && solid > resetTime)
                        f.playStageSound("touhou_strike");
                }
            }

            private int soundedStrike = -1; // (the last strike whose beams were heard turning solid)
            private double resetTime = double.NegativeInfinity;

            // ---- the wings ----

            private float wingSize(BulletField f) => (f.stageMax.X - f.stageMin.X) * DanmakusuBalance.MATENSHI_WING_SIZE;

            /// <summary>How open the wings are (0 closed .. 1 open): opening over the first four beats, closing again over the last one.</summary>
            private float openness(double t)
                => smoothStep((t - Start) / (beat * DanmakusuBalance.MATENSHI_WING_GROW_BEATS)) * (1f - smoothStep((t - (End - beat)) / beat));

            /// <summary>How much of the wings is seen (0..1): they fade in, still closed, at the start of her section and fade out, closed again, at its very end.</summary>
            private float presence(double t)
            {
                double appear = beat * DanmakusuBalance.MATENSHI_WING_APPEAR_BEATS;
                return (float)Math.Clamp(Math.Min((t - Start) / appear, (End - t) / appear), 0, 1);
            }

            /// <summary>
            /// How far a wing is turned from its open shape (radians, clockwise for the right wing) and how big it looks at <paramref name="t"/>: closed it stands up behind her, small, as if still folded at her back;
            /// it opens out and grows to full, and open it sways a little, slowly, the two together.
            /// </summary>
            private (float Angle, float Scale) fold(double t)
            {
                float open = openness(t);
                float sway = swayDegrees(swayAt(t), 0) * open;
                return (((1f - open) * DanmakusuBalance.MATENSHI_WING_CLOSED_DEGREES + sway) * MathF.PI / 180f, DanmakusuBalance.MATENSHI_WING_CLOSED_SCALE + (1f - DanmakusuBalance.MATENSHI_WING_CLOSED_SCALE) * open);
            }

            /// <summary>
            /// How far (degrees; positive is down) a sway of <paramref name="s"/> (-1 .. 1) turns bone <paramref name="bone"/>: up a little, MATENSHI_WING_SWAY_DEGREES, but down as far as
            /// MATENSHI_WING_SWAY_DOWN_DEGREES (2026-11-21, "now it can sway 45 degree down too"; it was the little sway both ways), each joint further out a little more.
            /// </summary>
            private static float swayDegrees(float s, int bone)
                => s * ((s > 0f ? DanmakusuBalance.MATENSHI_WING_SWAY_DOWN_DEGREES : DanmakusuBalance.MATENSHI_WING_SWAY_DEGREES) + DanmakusuBalance.MATENSHI_WING_FLEX_DEGREES * bone);

            // ---- the wing's bones and feathers (WingRig: 2026-11-15, the user: "you sliced her wings wrongly. You need to segment her feathers and bones instead") ----

            /// <summary>The wing's sway at <paramref name="t"/> (-1 .. 1).</summary>
            private float swayAt(double t) => MathF.Sin((float)((t - Start) / (beat * DanmakusuBalance.MATENSHI_WING_SWAY_BEATS)) * MathF.Tau);

            /// <summary>
            /// How far bone <paramref name="bone"/> is turned at <paramref name="t"/> (degrees, from the wing's open shape): the shoulder sways as the wing does; each joint further out a little more and a little later,
            /// as a whip follows the hand; none of it while the wing is closed.
            /// </summary>
            private float boneDegrees(int bone, double t, float open)
                => swayDegrees(swayAt(t - beat * DanmakusuBalance.MATENSHI_BONE_LAG_BEATS * bone), bone) * open;

            // (the pose of the wing's pieces at the moment last asked for: every piece's turn, from the wing as a whole, and where its joint or root has gone - in pixels of the right wing's picture)
            private double posedAt = double.NaN;
            private readonly float[] pieceTurn = new float[WingRig.Pieces];
            private readonly Vector2[] pivotMoved = new Vector2[WingRig.Pieces];
            private readonly float[] boneTurn = new float[WingRig.BONES];
            private readonly Vector2[] jointMoved = new Vector2[WingRig.BONES + 1];

            /// <summary>
            /// Poses the wing's pieces for <paramref name="t"/>: the bones turn each about its inner joint, carrying the joints further out; each feather is carried by its bone, and follows its bone's sway later
            /// the further out along the wing it grows (it trails behind it) and flutters a little on its own (the user, 2026-11-14: "feathers on her wing also have physics separately").
            /// </summary>
            private void pose(double t)
            {
                if (t == posedAt)
                    return;

                posedAt = t;
                float open = openness(t);
                float shoulder = boneDegrees(0, t, open); // (the wing as a whole sways with the shoulder: fold)
                jointMoved[0] = WingRig.JOINTS[0];

                for (int bone = 0; bone < WingRig.BONES; bone++)
                {
                    boneTurn[bone] = (boneDegrees(bone, t, open) - shoulder) * MathF.PI / 180f;
                    jointMoved[bone + 1] = jointMoved[bone] + turn(WingRig.JOINTS[bone + 1] - WingRig.JOINTS[bone], boneTurn[bone]);
                }

                for (int piece = 0; piece < WingRig.Pieces; piece++)
                {
                    int bone = WingRig.BoneOf(piece);
                    pivotMoved[piece] = jointMoved[bone] + turn(WingRig.PivotOf(piece) - WingRig.JOINTS[bone], boneTurn[bone]);

                    if (piece < WingRig.BONES)
                    {
                        pieceTurn[piece] = boneTurn[bone];
                        continue;
                    }

                    double lag = beat * DanmakusuBalance.MATENSHI_FEATHER_LAG_BEATS * (1 + 2 * WingRig.Along(piece));
                    float trailing = boneDegrees(bone, t - lag, open) - shoulder;
                    float flutter = MathF.Sin((float)((t - Start) / (beat * DanmakusuBalance.MATENSHI_FEATHER_FLUTTER_BEATS)) * MathF.Tau + piece * 1.7f) * DanmakusuBalance.MATENSHI_FEATHER_FLUTTER_DEGREES * open;
                    pieceTurn[piece] = (trailing + flutter) * MathF.PI / 180f;
                }
            }

            /// <summary>Where a point of the right wing's picture (pixels) is once piece <paramref name="piece"/> has moved (still in the picture's pixels, before the wing is turned and sized as a whole).</summary>
            private Vector2 moved(int piece, Vector2 p) => pivotMoved[piece] + turn(p - WingRig.PivotOf(piece), pieceTurn[piece]);

            /// <summary>The order the pieces are drawn in: the long feathers (from the lowest up to the primaries), then the bones with their covering feathers over their roots.</summary>
            private static readonly int[] draw_order = Enumerable.Range(WingRig.BONES, WingRig.FEATHERS.Length).Reverse().Concat(Enumerable.Range(0, WingRig.BONES)).ToArray();

            private static Vector2 turn(Vector2 v, float radians)
            {
                float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
                return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
            }

            /// <summary>
            /// A place on a wing at <paramref name="t"/>: <paramref name="spot"/> is on the right wing's picture, from its root, in shares of its width (mirrored for the left wing); it moves with the bone or feather it
            /// is on.
            /// </summary>
            private Vector2 onWing(BulletField f, bool left, Vector2 spot, double t)
            {
                var (angle, scale) = fold(t);
                var (pictureWidth, pictureHeight) = StageLayer.WingPictureSize;

                if (pictureWidth > 0)
                {
                    pose(t);
                    Vector2 root = StageLayer.WING_ROOT * new Vector2(pictureWidth, pictureHeight);
                    Vector2 p = root + spot * pictureWidth;
                    spot = (moved(WingRig.PieceAt(p), p) - root) / pictureWidth;
                }

                float side = left ? -1f : 1f;
                return f.bossPositionOf(Period) + turn(new Vector2(side * spot.X, spot.Y) * (wingSize(f) * scale), side * angle);
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End)
                    return;

                float alpha = presence(t) * DanmakusuBalance.MATENSHI_WING_ALPHA;
                var (pictureWidth, pictureHeight) = StageLayer.WingPictureSize;

                if (alpha <= 0.005f || pictureWidth == 0)
                    return;

                var (angle, scale) = fold(t);
                float perPixel = wingSize(f) * scale / pictureWidth;
                Vector2 her = f.bossPositionOf(Period);
                Vector2 root = StageLayer.WING_ROOT * new Vector2(pictureWidth, pictureHeight);
                pose(t);

                foreach (bool left in new[] { false, true })
                {
                    float side = left ? -1f : 1f;

                    // (every piece where its bone has carried it and turned as it is: the wing as a whole turns about its root, at her back)
                    foreach (int piece in draw_order)
                    {
                        var (x0, y0, x1, y1) = StageLayer.WingPieceBounds(piece);

                        if (x1 <= x0)
                            continue;

                        Vector2 centre = moved(piece, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f)) - root;
                        Vector2 at = her + turn(new Vector2(side * centre.X, centre.Y) * perPixel, side * angle);
                        float turned = side * (angle + pieceTurn[piece]);
                        var texture = (left ? StageTexture.WingLeftPiece0 : StageTexture.WingRightPiece0) + piece;
                        f.stageBack.Add(texture, at, new Vector2(x1 - x0, y1 - y0) * perPixel, turned * 180f / MathF.PI, alpha, Color4.White);
                    }
                }
            }

            /// <summary>Every bullet fired in her section (every MATENSHI_FROM_WINGS_EVERY-th) comes out of her wings instead, as a feather.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t > End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                if ((first + bullet + 1) % DanmakusuBalance.MATENSHI_FROM_WINGS_EVERY != 0)
                    return false;

                if (presence(t) < 0.5f)
                    return false; // (her wings are not there yet, or not any more)

                // (the two wings in turn, the places spread evenly over what each covers: the golden ratio over its spots)
                int number = shot++;
                bool left = number % 2 == 1;
                var spots = StageLayer.WING_SPOTS;
                Vector2 spot = spots[(int)((number / 2) * 0.618034 % 1.0 * spots.Length) % spots.Length];
                Vector2 position = onWing(f, left, spot, t);

                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);

                // (straight away from a place below her, so that the feathers from high on her wings fly up over her too: 2026-11-26, "she has blind spot for feathers on top of her" - straight
                // away from her middle, nothing went up between her wings)
                Vector2 pivot = f.bossPositionOf(Period) + new Vector2(0f, (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.MATENSHI_FEATHER_PIVOT_SCREEN);
                Vector2 outward = position - pivot;
                Vector2 velocity = (outward.LengthSquared > 1e-4f ? Vector2.Normalize(outward) : -Vector2.UnitY) * stored.Speed;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = position,
                    V = velocity,
                    Radius = stored.Radius * big * DanmakusuBalance.MATENSHI_FEATHER_SIZE,
                    Hit = stored.Hit * big * DanmakusuBalance.MATENSHI_FEATHER_SIZE,
                    Style = stored.Style,
                    Shape = BulletShape.Feather,
                    Tint = BulletRenderer.TINT_WHITE,
                    TintAmount = 1f,
                    Bloom = DanmakusuBalance.MATENSHI_FEATHER_BLOOM,
                    Until = t + f.stageLife(velocity),
                });

                return true;
            }

            // ---- her beams ----

            private float beamHalf(BulletField f) => f.circleRadius * DanmakusuBalance.MATENSHI_BEAM_WIDTH_CIRCLES * 0.5f;

            private double solidAt(int k) => strikes[k] + beat * DanmakusuBalance.MATENSHI_BEAM_PREVIEW_BEATS;

            private double goneAt(int k) => solidAt(k) + beat * DanmakusuBalance.MATENSHI_BEAM_LIVE_BEATS;

            private float top(BulletField f) => (f.hasScreenBounds ? f.screenMin.Y : f.stageMin.Y) - beamHalf(f);

            private float bottom(BulletField f) => (f.hasScreenBounds ? f.screenMax.Y : f.stageMax.Y) + beamHalf(f);

            /// <summary>How far down the beam reaches at <paramref name="t"/>: from the top to the bottom of the screen over a quarter of a beat once it is solid.</summary>
            private float reachDown(BulletField f, int k, double t)
                => top(f) + (bottom(f) - top(f)) * smoothStep((t - solidAt(k)) / (beat * DanmakusuBalance.MATENSHI_BEAM_GROW_BEATS));

            /// <summary>The beams that can be there at <paramref name="t"/>: from the one whose time is not over yet to the last whose preview has come.</summary>
            private (int First, int Last) around(double t)
            {
                double every = beat * DanmakusuBalance.MATENSHI_BEAM_EVERY_BEATS;
                int last = Math.Min(strikes.Length - 1, (int)Math.Floor((t - Start) / every));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.MATENSHI_BEAM_PREVIEW_BEATS + DanmakusuBalance.MATENSHI_BEAM_LIVE_BEATS)) / every));
                return (first, last);
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || strikes.Length == 0 || t < Start)
                    return;

                var white = new Color4(1f, 0.98f, 0.92f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.95f, 0.8f, 0.35f, 1f));
                float half = beamHalf(f);
                var (first, last) = around(t);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || t < strikes[k] || t >= goneAt(k))
                        continue;

                    bool solid = t >= solidAt(k);
                    float alpha = solid
                        ? 1f - smoothStep((t - (goneAt(k) - beat * 0.1)) / (beat * 0.1))
                        : DanmakusuBalance.MATENSHI_PREVIEW_ALPHA * smoothStep((t - strikes[k]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    foreach (float x in new[] { strikeX[k], otherX(f, k) })
                    {
                        line.Clear();
                        line.Add(new Vector2(x, top(f)));
                        line.Add(new Vector2(x, solid ? reachDown(f, k, t) : bottom(f)));
                        fades.Clear();
                        fades.Add(alpha);
                        fades.Add(alpha);
                        f.addPlainBeam(line, solid ? half : half * 0.6f, white, border, ref beams, ref pointTotal, null, fades);
                    }
                }
            }

            /// <summary>Whether the player at <paramref name="player"/> touches a wing at <paramref name="t"/> (as a slider body: the picture's solid part, as far as the player's hitbox reaches).</summary>
            private bool onWings(BulletField f, Vector2 player, double t)
            {
                if (t < Start || t > End || presence(t) < 0.5f)
                    return false;

                var (angle, scale) = fold(t);
                float width = wingSize(f) * scale;
                var (pictureWidth, pictureHeight) = StageLayer.WingPictureSize;

                if (pictureWidth == 0 || width < 1f)
                    return false;

                Vector2 her = f.bossPositionOf(Period);
                Vector2 apart = player - her;
                float reach = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                // (far from her: on neither)
                if (apart.Length > width * 1.25f + reach)
                    return false;

                float perPixel = width / pictureWidth;
                var root = new Vector2(StageLayer.WING_ROOT.X * pictureWidth, StageLayer.WING_ROOT.Y * pictureHeight);
                pose(t);

                foreach (float side in new[] { 1f, -1f })
                {
                    // (into the right wing's picture as its pieces have moved: mirrored for the left wing, turned back about the root, in pixels of the picture)
                    Vector2 posed = turn(new Vector2(side * apart.X, apart.Y), -angle) / perPixel + root;

                    // (then back by each piece's own move: on the wing where the picture is solid and it is that piece's part)
                    for (int piece = 0; piece < WingRig.Pieces; piece++)
                    {
                        Vector2 local = WingRig.PivotOf(piece) + turn(posed - pivotMoved[piece], -pieceTurn[piece]);

                        if (StageLayer.WingDistance(local.X, local.Y) * perPixel > reach)
                            continue;

                        // (a piece carries its own part of the picture and its overlap, as far as the player's hitbox reaches round it)
                        int nearest = WingRig.PieceAt(local, out float best);

                        if (WingRig.Score(piece, local) <= best + WingRig.OverlapAt(local, nearest) + reach / perPixel)
                            return true;
                    }
                }

                return false;
            }

            /// <summary>Her wings hurt like a slider body; so does a solid beam (as far down as it has grown; not in the last tenth of a beat, while it fades).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;

                if (onWings(f, player, now))
                    return true;

                if (strikes.Length == 0)
                    return false;

                float reach = beamHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                var (first, last) = around(now);

                for (int k = first; k <= last; k++)
                {
                    if (!known[k] || now < solidAt(k) || now >= goneAt(k) - beat * 0.1)
                        continue;

                    if (player.Y > reachDown(f, k, now) + reach)
                        continue;

                    if (MathF.Abs(player.X - strikeX[k]) <= reach || MathF.Abs(player.X - otherX(f, k)) <= reach)
                        return true;
                }

                return false;
            }
        }
    }
}
