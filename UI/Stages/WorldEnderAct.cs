using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Hecatia Lapislazuli, World's Ender: every 32 beats of an attack of hers, none in one shorter than that, one at a time (see planWorldEnders); it goes through to its end, also when her section is over by then (she stays until it is done); the power-up warns of it. Over four beats, while the screen shakes, the Earth goes to the
        // middle of the screen and grows as big as the playfield is high, turning from the small picture into a big sharp one, under the bullets; once it has grown it hurts like a slider body, and so do its chunks and the very big chunks of its halves. The Moon, off its chain, goes slowly round it, far out; the Sun shrinks and goes to one side of it.
        // Four beats after the Earth has grown the Sun crosses it, once, from that side to the other, the way that was towards the player when it all began, and leaves a line of molten rock behind it, not straight, that glows orange. The screen shakes
        // from then until the split. At both ends golden glowing bullets fly away from the Earth, five rings over 90 degrees, with pieces of it among them, and bullets fly off to both sides all along the line as the Sun passes. Two beats after the cut the Earth splits. Right
        // before that comes an impact frame, half a second long whatever the tempo: the background turns white and everything black, with a sharp white arc on the side of the round things that faces the middle, and flickering four-pointed stars and sharp lines out from the split, as in anime, white on the Earth
        // and black beyond it; every bullet has a sharp streak towards the split, and those go on through the explosion. As soon as the Sun begins to cut, orange cracks come up all over it, every way, where it will fall apart and more. At the split the bullets come again, along the line and at both ends, with the rest of the chunks. The halves, cracking into very big chunks along those cracks, their edges still
        // glowing (it is in their pictures), drift off to the sides of the line at one speed, fanning out over 120 degrees, as do the many small chunks that come out all along the cut.
        // ======================================================================================================================================================

        private sealed class WorldEnderAct : StageAct
        {
            private readonly WorldsAct? source; // (the attack it takes the worlds over from, when one ends as it begins)
            private readonly double beat;
            private readonly int[] pieceBullets = new int[EarthPieces.All.Length]; // (the bullet each chunk of the Earth has become, once it has broken away; -1 = it still sits in its place)
            private readonly int[] debrisBullets = new int[EarthPieces.Debris.Length]; // (the same for the many small ones that come out all along the cut at the split)
            private bool known, firstDone, lastDone, splitDone;
            private Vector2 earthFrom, moonFrom, sunFrom, centre, way, side;
            private float bigRadius, smallRadius, spinFrom, lineDegrees;

            public WorldEnderAct(BossPeriod period, double start, WorldsAct? source)
            {
                Period = period;
                this.source = source;
                beat = Math.Max(period.BeatLength, 1);
                Start = start;
                End = start + beat * (DanmakusuBalance.ENDER_GROW_BEATS + DanmakusuBalance.ENDER_WAIT_BEATS + DanmakusuBalance.ENDER_SWEEP_BEATS + DanmakusuBalance.ENDER_SPLIT_DELAY_BEATS + DanmakusuBalance.ENDER_APART_BEATS);
                Array.Fill(pieceBullets, -1);
                Array.Fill(debrisBullets, -1);
            }

            public override double MechanicsEnd => End;

            private double grownAt => Start + beat * DanmakusuBalance.ENDER_GROW_BEATS;

            private double slashFrom => grownAt + beat * DanmakusuBalance.ENDER_WAIT_BEATS;

            private double slashTo => slashFrom + beat * DanmakusuBalance.ENDER_SWEEP_BEATS;

            private double splitAt => slashTo + beat * DanmakusuBalance.ENDER_SPLIT_DELAY_BEATS;

            private double impactFrom => splitAt - DanmakusuBalance.ENDER_IMPACT_MS;

            /// <summary>Where the Earth stands when it has grown, and how big it is then (known once the act has begun).</summary>
            public Vector2 Centre => centre;

            /// <summary>A place on the line of the cut, <paramref name="u"/> 0..1 from one end to the other, and the way across it.</summary>
            public Vector2 SplitPoint(float u) => lineAt(u);

            public Vector2 SplitSide => side;

            /// <summary>The nearest place on the (straight) line of the cut to <paramref name="at"/>: what the lines of the impact frame and the blur go out from.</summary>
            public Vector2 TowardsSplit(Vector2 at) => centre + way * Math.Clamp(Vector2.Dot(at - centre, way), -bigRadius, bigRadius);

            /// <summary>
            /// The Earth hurts like a slider body from when it has grown until it splits (where its chunks have flown off too, as it is one round thing to hit); after the split its very big chunks do, while they can be seen, each as an ellipse in its picture, and so do the chunks
            /// that fly off, all the way.
            /// </summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || !known || now < grownAt)
                    return false;

                Vector2 player = f.characterPosition;
                float me = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                if (now < splitAt)
                {
                    if (Vector2.Distance(player, centre) <= bigRadius * DanmakusuBalance.ENDER_EARTH_HIT + me)
                        return true;
                }
                else if (now < End)
                {
                    double u = (now - splitAt) / (End - splitAt);
                    float beats = (float)((now - splitAt) / beat);
                    float apart = bigRadius * DanmakusuBalance.ENDER_HALVES_SPEED * beats;

                    // (no longer once they have faded half way)
                    if (u < 0.875)
                    {
                        for (int k = 0; k < EarthPieces.Big.Length; k++)
                        {
                            var chunk = EarthPieces.Big[k];
                            Vector2 at = centre + way * (chunk.X * bigRadius) + side * (chunk.Y * bigRadius) + fanned(chunk.X, chunk.Half == 0 ? -1f : 1f) * apart;
                            float angle = (lineDegrees + (hash01(k, 9, 68) * 2f - 1f) * DanmakusuBalance.ENDER_BIG_SPIN * beats) * MathF.PI / 180f;
                            Vector2 off = player - at;
                            float along = off.X * MathF.Cos(angle) + off.Y * MathF.Sin(angle), across = -off.X * MathF.Sin(angle) + off.Y * MathF.Cos(angle);
                            float a = chunk.W * bigRadius * DanmakusuBalance.ENDER_CHUNK_HIT + me, b = chunk.H * bigRadius * DanmakusuBalance.ENDER_CHUNK_HIT + me;

                            if (along * along / (a * a) + across * across / (b * b) <= 1f)
                                return true;
                        }
                    }
                }

                // (the chunks that have broken away and fly)
                foreach (int index in pieceBullets)
                {
                    if (index >= 0 && touches(index))
                        return true;
                }

                foreach (int index in debrisBullets)
                {
                    if (index >= 0 && touches(index))
                        return true;
                }

                return false;

                bool touches(int index)
                {
                    var bullet = Bullets[index];

                    return now >= bullet.T0 && now < bullet.Until && Vector2.Distance(stageBulletPosition(bullet, now), player) <= bullet.Hit + me;
                }
            }

            public float BigRadius => bigRadius;

            /// <summary>The impact frame comes right before the Earth splits.</summary>
            public bool InImpact(double t) => known && t >= impactFrom && t < splitAt;

            /// <summary>0..1: the sharp streaks: whole in the impact frame, then dying away through the explosion.</summary>
            public float StreakLevel(double t)
            {
                if (!known || t < impactFrom || t >= End)
                    return 0f;

                return t < splitAt ? 1f : 1f - (float)Math.Clamp((t - splitAt) / (beat * DanmakusuBalance.ENDER_EXPLOSION_BEATS), 0.0, 1.0);
            }

            /// <summary>0..1: the radial blur of the bullets after the explosion, as Gengetsu's (see hasteBlur): whole at the split, fading out slowly, also when the rest is over.</summary>
            public float AfterBlurLevel(double t)
            {
                if (!known || t < splitAt)
                    return 0f;

                return 1f - (float)Math.Clamp((t - splitAt) / (beat * DanmakusuBalance.ENDER_AFTER_BLUR_BEATS), 0.0, 1.0);
            }

            /// <summary>0..1: the screen shakes while the Earth grows, and from the cut until it splits.</summary>
            public float ShakeAt(double now)
            {
                if ((now >= Start && now < grownAt) || (now >= slashFrom && now < splitAt))
                    return DanmakusuBalance.ENDER_SHAKE;

                // (the explosion shakes it hard, and that dies away slowly, also when the rest is over)
                if (now >= splitAt)
                    return DanmakusuBalance.ENDER_AFTER_SHAKE * (1f - (float)Math.Clamp((now - splitAt) / (beat * DanmakusuBalance.ENDER_AFTER_SHAKE_BEATS), 0.0, 1.0));

                return 0f;
            }

            /// <summary>How far (a share of the Earth's width) the cut runs from the straight line at <paramref name="u"/> (0..1 from one side of the Earth to the other). The pictures of the two halves were cut along this too: change it there as well.</summary>
            private static float cutProfile(float u)
                => 0.035f * (0.5f * MathF.Sin(MathF.Tau * 1.3f * u + 0.7f) + 0.3f * MathF.Sin(MathF.Tau * 3.1f * u + 2.1f) + 0.2f * MathF.Sin(MathF.Tau * 7.3f * u + 4.4f) + 0.12f * MathF.Sin(MathF.Tau * 17.7f * u + 1.2f) + 0.06f * MathF.Sin(MathF.Tau * 41.3f * u + 3.3f));

            private Vector2 lineAt(float u) => centre + way * (bigRadius * (2f * u - 1f)) + side * (2f * bigRadius * cutProfile(u));

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                Array.Fill(pieceBullets, -1);
                Array.Fill(debrisBullets, -1);

                if (time < Start)
                    known = false;

                // (what has been thrown is thrown again, from when it was: a seek shows the same; the sounds only play when it is just then)
                firstDone = lastDone = splitDone = false;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                if (!known)
                {
                    known = true;
                    centre = (f.stageMin + f.stageMax) * 0.5f;
                    Vector2 boss = f.bossVisualOf(Period);
                    earthFrom = source != null ? source.BodyAt(f, 0, Start) : boss;
                    moonFrom = source != null ? source.BodyAt(f, 1, Start) : boss;
                    sunFrom = source != null ? source.BodyAt(f, 2, Start) : boss;
                    bigRadius = (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.ENDER_EARTH_SIZE * 0.5f;
                    smallRadius = f.circleRadius * DanmakusuBalance.HECATIA_EARTH_RADIUS;
                    spinFrom = WorldsAct.spinOf(0, Start);

                    Vector2 toPlayer = f.characterPosition - centre;
                    way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;
                    side = new Vector2(-way.Y, way.X);
                    lineDegrees = MathF.Atan2(way.Y, way.X) * 180f / MathF.PI;
                }

                if (!firstDone && now >= slashFrom)
                {
                    firstDone = true;
                    burst(f, slashFrom, lineAt(0f), -way, 0);
                    breakAway(f, slashFrom, 0, true);
                    alongTheLine(f, slashFrom, slashTo);

                    if (now - slashFrom < 250)
                        f.playStageSound("hecatia_cut");
                }

                if (!lastDone && now >= slashTo)
                {
                    lastDone = true;
                    burst(f, slashTo, lineAt(1f), way, 1);
                    breakAway(f, slashTo, 1, true);
                }

                // (the split: the bullets come again, all at once, and the big pieces of the Earth from both ends)
                if (!splitDone && now >= splitAt)
                {
                    splitDone = true;
                    burst(f, splitAt, lineAt(0f), -way, 2);
                    burst(f, splitAt, lineAt(1f), way, 3);
                    breakAway(f, splitAt, 0, false);
                    breakAway(f, splitAt, 1, false);
                    breakDebris(f, splitAt);
                    alongTheLine(f, splitAt, splitAt);

                    if (now - splitAt < 250)
                        f.playStageSound("hecatia_explosion");
                }
            }

            /// <summary>A golden glowing bullet, as all of hers are in this.</summary>
            private void addGolden(BulletField f, double t0, Vector2 from, Vector2 direction, float radius, float speed)
            {
                Vector2 velocity = direction * speed;

                AddBullet(new StageBullet
                {
                    T0 = t0,
                    P0 = from,
                    V = velocity,
                    Face = direction,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = 0,
                    Shape = BulletShape.Circle,
                    Tint = BulletRenderer.TINT_YELLOW,
                    TintAmount = DanmakusuBalance.TOYOHIME_GOLD_TINT,
                    Bloom = DanmakusuBalance.ENDER_BULLET_BLOOM,
                    Until = t0 + f.stageLife(velocity),
                });
            }

            /// <summary>Where a chunk of the Earth sits, for an Earth of this size in this place and turned so: its middle.</summary>
            private Vector2 homeOf(int k, Vector2 place, float radius, float degrees)
            {
                var piece = EarthPieces.All[k];
                float angle = degrees * MathF.PI / 180f;
                Vector2 along = new Vector2(MathF.Cos(angle), MathF.Sin(angle)), across = new Vector2(-along.Y, along.X);

                return place + along * (piece.X * radius) + across * (piece.Y * radius);
            }

            private static StageTexture pictureOf(int k) => StageTexture.EarthPiece0 + k;

            /// <summary>The way what the Earth breaks into at the split goes: off to its side of the line, and the further along the line it was, the further that way (<paramref name="along"/> -1..1), over <see cref="DanmakusuBalance.ENDER_BIG_FAN_DEGREES"/> in all.</summary>
            private Vector2 fanned(float along, float sideOf)
            {
                float angle = Math.Clamp(along, -1f, 1f) * DanmakusuBalance.ENDER_BIG_FAN_DEGREES * 0.5f * MathF.PI / 180f;
                return side * (sideOf * MathF.Cos(angle)) + way * MathF.Sin(angle);
            }

            /// <summary>The many small chunks all along the cut come out at the split, each to its side and fanned out as the big ones are, at one speed, and hit as bullets do.</summary>
            private void breakDebris(BulletField f, double t)
            {
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.ENDER_SHARD_SPEED;

                for (int k = 0; k < debrisBullets.Length; k++)
                {
                    var piece = EarthPieces.Debris[k];

                    if (piece.W <= 0.02f || debrisBullets[k] >= 0)
                        continue;

                    Vector2 home = centre + way * (piece.X * bigRadius) + side * (piece.Y * bigRadius);
                    Vector2 velocity = fanned(piece.X, piece.Y < 0f ? -1f : 1f) * (speed * (0.7f + 1.1f * hash01(k, 7, 69)));
                    float radius = Math.Min(piece.W, piece.H) * bigRadius * DanmakusuBalance.ENDER_PIECE_HIT;
                    int before = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = home,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = 0,
                        Shape = BulletShape.Circle,
                        Hidden = true,
                        Slider = true, // (it hurts like a slider body: see Hurts)
                        Until = t + f.stageLife(velocity),
                    });

                    if (Bullets.Count > before)
                        debrisBullets[k] = before;
                }
            }

            /// <summary>The chunks of one end of the line break away from the Earth: each flies off from where it sat, away from the middle of the Earth, at one speed, and hits as a bullet does.</summary>
            private void breakAway(BulletField f, double t, int end, bool atCut)
            {
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.ENDER_SHARD_SPEED;

                for (int k = 0; k < pieceBullets.Length; k++)
                {
                    var piece = EarthPieces.All[k];

                    if (piece.End != end || piece.AtCut != atCut || pieceBullets[k] >= 0)
                        continue;

                    Vector2 home = homeOf(k, centre, bigRadius, lineDegrees);
                    Vector2 outward = home - centre;
                    outward = outward.LengthSquared > 1f ? Vector2.Normalize(outward) : (end == 0 ? -way : way);
                    Vector2 velocity = outward * (speed * (0.7f + 0.8f * hash01(k, end, 62)));
                    float radius = Math.Min(piece.W, piece.H) * bigRadius * DanmakusuBalance.ENDER_PIECE_HIT;
                    int before = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = t,
                        P0 = home,
                        V = velocity,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = 0,
                        Shape = BulletShape.Circle,
                        Hidden = true,
                        Slider = true, // (it hurts like a slider body: see Hurts)
                        Until = t + f.stageLife(velocity),
                    });

                    if (Bullets.Count > before)
                        pieceBullets[k] = before;
                }
            }

            /// <summary>From one end of the line, away from the Earth: five bursts of small golden glowing bullets over 90 degrees, as chaotic as what they fly with: each bullet in a way, at a speed (half to one and a half times a plain bullet's, evenly), a size and a moment of its own (by its number: a replay shows the same).</summary>
            private void burst(BulletField f, double t, Vector2 from, Vector2 outward, int salt)
            {
                float radius = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period);
                float spread = DanmakusuBalance.ENDER_SPREAD_DEGREES * MathF.PI / 180f;
                double gap = beat * DanmakusuBalance.ENDER_RING_GAP_BEATS;

                for (int ring = 0; ring < DanmakusuBalance.ENDER_RINGS; ring++)
                {
                    for (int i = 0; i < DanmakusuBalance.ENDER_RING_BULLETS; i++)
                    {
                        int number = ring * DanmakusuBalance.ENDER_RING_BULLETS + i;
                        float way = spread * (hash01(number, salt, 81) - 0.5f);
                        float fast = speed * (DanmakusuBalance.ENDER_BURST_SLOWEST + (DanmakusuBalance.ENDER_BURST_FASTEST - DanmakusuBalance.ENDER_BURST_SLOWEST) * hash01(number, salt, 82));
                        float size = radius * DanmakusuBalance.ENDER_BULLET_SIZE * (0.7f + 0.7f * hash01(number, salt, 83));

                        addGolden(f, t + (ring + hash01(number, salt, 84)) * gap, from, turned(outward, way), size, fast);
                    }
                }
            }

            /// <summary>All along the line, one place after the other between the two moments: a few bullets off to each side of it.</summary>
            private void alongTheLine(BulletField f, double from, double to)
            {
                float radius = f.stageBaseRadius(Period);
                float speed = stageBulletSpeed(Period);
                float spread = DanmakusuBalance.ENDER_SPREAD_DEGREES * MathF.PI / 180f;

                for (int i = 0; i < DanmakusuBalance.ENDER_LINE_POINTS; i++)
                {
                    float u = (i + 0.5f) / DanmakusuBalance.ENDER_LINE_POINTS;
                    Vector2 place = lineAt(u);

                    if (place.X < f.stageMin.X || place.X > f.stageMax.X || place.Y < f.stageMin.Y || place.Y > f.stageMax.Y)
                        continue;

                    double t0 = from + (to - from) * u;

                    for (int b = 0; b < DanmakusuBalance.ENDER_LINE_BULLETS; b++)
                    {
                        float angle = spread * (b / (DanmakusuBalance.ENDER_LINE_BULLETS - 1f) - 0.5f);
                        float size = radius * DanmakusuBalance.ENDER_BULLET_SIZE;

                        // (each at a speed of its own, evenly between the slowest and the fastest)
                        float fast(int one) => speed * (DanmakusuBalance.ENDER_BURST_SLOWEST + (DanmakusuBalance.ENDER_BURST_FASTEST - DanmakusuBalance.ENDER_BURST_SLOWEST) * hash01(i * 8 + b, one, (int)(from / 7)));

                        addGolden(f, t0, place, turned(side, angle), size, fast(85));
                        addGolden(f, t0, place, turned(-side, angle), size, fast(86));
                    }
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || !known || t < Start)
                    return;

                // (the chunks fly on until they have left the screen, also when the rest is over)
                if (t >= End)
                {
                    drawFlownPieces(f, t, Color4.White);
                    return;
                }

                bool impact = InImpact(t);
                Color4 tone = impact ? Color4.Black : Color4.White;
                float grown = smoothStep((t - Start) / (grownAt - Start));

                if (t < splitAt)
                {
                    float radius = smallRadius + (bigRadius - smallRadius) * grown;
                    Vector2 place = Vector2.Lerp(earthFrom, centre, grown);
                    float degrees = spinFrom + MathF.IEEERemainder(lineDegrees - spinFrom, 360f) * grown;
                    float sharp = (float)Math.Clamp((t - Start) / (beat * DanmakusuBalance.ENDER_PICTURE_BEATS), 0.0, 1.0);

                    // (the small picture turns into the big one: the big one comes up over it, and only then the small one goes)
                    if (sharp < 1f)
                        f.stageEnder.Add(StageTexture.Earth, place, new Vector2(radius * 2f), degrees, 1f, tone);

                    f.stageEnder.Add(StageTexture.EarthBig, place, new Vector2(radius * 2f), degrees, sharp, tone);
                    drawHomePieces(f, place, radius, degrees, sharp, tone);

                    if (!impact)
                    {
                        // (the edges of the holes that the chunks of the cut have left are molten, from when they leave)
                        if (t >= slashFrom)
                            f.stageEnder.Add(StageTexture.EarthHoleGlow0, place, new Vector2(radius * 2f), degrees, smoothStep((t - slashFrom) / (beat * 0.25)), Color4.White);

                        if (t >= slashTo)
                            f.stageEnder.Add(StageTexture.EarthHoleGlow1, place, new Vector2(radius * 2f), degrees, smoothStep((t - slashTo) / (beat * 0.25)), Color4.White);

                        // (the warning: orange cracks come up all over the Earth as soon as the Sun begins to cut it)
                        if (t >= slashFrom)
                            f.stageEnder.Add(StageTexture.EarthCracks, place, new Vector2(radius * 2f), degrees, smoothStep((t - slashFrom) / (beat * DanmakusuBalance.ENDER_CRACKS_BEATS)), Color4.White);
                    }
                    else if (t >= slashFrom)
                    {
                        // (in the impact frame the cracks are white on the black Earth, blurred out from the middle of the screen: fainter copies, each a little bigger and further out from it)
                        float cracks = smoothStep((t - slashFrom) / (beat * DanmakusuBalance.ENDER_CRACKS_BEATS));
                        Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;
                        int copies = DanmakusuBalance.ENDER_CRACK_BLUR_COPIES;

                        for (int k = copies; k >= 1; k--)
                        {
                            float grow = 1f + DanmakusuBalance.ENDER_CRACK_BLUR_SPREAD * k / copies;

                            f.stageEnder.Add(StageTexture.EarthCracksWhite, middle + (place - middle) * grow, new Vector2(radius * 2f * grow), degrees,
                                cracks * DanmakusuBalance.ENDER_CRACK_BLUR_ALPHA * (1f - k / (copies + 1f)), Color4.White);
                        }

                        f.stageEnder.Add(StageTexture.EarthCracksWhite, place, new Vector2(radius * 2f), degrees, cracks, Color4.White);
                    }

                    drawFlownPieces(f, t, tone);
                }
                else
                {
                    // (the halves break into very big chunks, with the molten edges in their pictures: those of each half drift off to its side of the line, at one speed, spreading a little, turning a little, and fade at the end)
                    double u = (t - splitAt) / (End - splitAt);
                    float beats = (float)((t - splitAt) / beat);
                    float apart = bigRadius * DanmakusuBalance.ENDER_HALVES_SPEED * beats;
                    float alpha = 1f - smoothStep((u - 0.75) / 0.25);

                    for (int k = 0; k < EarthPieces.Big.Length; k++)
                    {
                        var chunk = EarthPieces.Big[k];
                        Vector2 offset = way * (chunk.X * bigRadius) + side * (chunk.Y * bigRadius);
                        Vector2 at = centre + offset + fanned(chunk.X, chunk.Half == 0 ? -1f : 1f) * apart;
                        float turn = (hash01(k, 9, 68) * 2f - 1f) * DanmakusuBalance.ENDER_BIG_SPIN * beats;

                        f.stageEnder.Add(StageTexture.EarthChunk0 + k, at, new Vector2(chunk.W * bigRadius, chunk.H * bigRadius), lineDegrees + turn, alpha, Color4.White);
                    }

                    drawFlownPieces(f, t, Color4.White);

                    // (the explosion)
                    double flash = (t - splitAt) / (beat * DanmakusuBalance.ENDER_EXPLOSION_BEATS);

                    if (flash < 1)
                        f.stageFront.Add(StageTexture.Glow, centre, new Vector2(bigRadius * 3f), 0f, 0.9f * (float)((1 - flash) * (1 - flash)), new Color4(1f, 0.72f, 0.35f, 1f), true);
                }

                if (!impact)
                    drawMolten(f, t);

                drawWorlds(f, t, impact, tone);

                // (the golden bullets glow: a soft orange light under each)
                if (!impact)
                {
                    for (int i = 0; i < Bullets.Count; i++)
                    {
                        var bullet = Bullets[i];

                        if (bullet.Hidden || t < bullet.T0 || t >= bullet.Until)
                            continue;

                        f.stageOrbs.Add(StageTexture.Glow, stageBulletPosition(bullet, t), new Vector2(bullet.Radius * DanmakusuBalance.ENDER_BULLET_GLOW), 0f, 0.55f, new Color4(1f, 0.6f, 0.2f, 1f), true);
                    }
                }
            }

            /// <summary>
            /// The molten line behind the Sun, in one piece: white hot in its middle, orange round that, dying away softly. Every stretch ends where the next begins, and all of it spreads the same way, straight across the line of the cut, whichever way
            /// the rock breaks, so its glow has no seams and no spikes at the bends. (After the split the glow is in the halves' pictures.)
            /// </summary>
            private void drawMolten(BulletField f, double t)
            {
                if (t < slashFrom || t >= splitAt)
                    return;

                float reached = (float)Math.Clamp((t - slashFrom) / (slashTo - slashFrom), 0.0, 1.0);
                float heat = 0.65f + 0.35f * reached;
                var hot = new Color4(1f, 0.93f, 0.65f, heat);
                var orange = new Color4(1f, 0.5f, 0.1f, 0.9f * heat);
                var soft = new Color4(1f, 0.42f, 0.08f, 0.35f * heat);
                var none = new Color4(0.9f, 0.3f, 0.05f, 0f);
                const int steps = 120;

                // (how far it is to a hole that has been left or to the rim of the Earth, along the line: nothing glows beyond that, so the crack ends at the rock's edge and its glow does not spill over it)
                float roomAt(float u)
                {
                    float place = Math.Clamp(u, 0f, 1f) * (EarthPieces.LineRoom.Length - 1);
                    int low = Math.Min((int)place, EarthPieces.LineRoom.Length - 2);
                    return (EarthPieces.LineRoom[low] + (EarthPieces.LineRoom[low + 1] - EarthPieces.LineRoom[low]) * (place - low)) * bigRadius;
                }

                float widthAt(float u)
                    => Math.Min(f.circleRadius * (0.85f + 0.3f * MathF.Sin(MathF.Tau * 2.3f * u + 1f) + 0.15f * MathF.Sin(MathF.Tau * 5.7f * u + 2.4f)), roomAt(u) / 1.8f);

                void band(Vector2 a, Vector2 b, float from0, float from1, float to0, float to1, Color4 near, Color4 far)
                {
                    f.stageMeshes.Add(new MeshQuad { A = a + side * from0, B = b + side * from1, C = a + side * to0, D = b + side * to1, Near = near, Far = far });
                    f.stageMeshes.Add(new MeshQuad { A = a - side * from0, B = b - side * from1, C = a - side * to0, D = b - side * to1, Near = near, Far = far });
                }

                Vector2 previous = lineAt(0f);
                float previousWidth = widthAt(0f);

                for (int i = 1; i <= steps; i++)
                {
                    float u = Math.Min(i / (float)steps, reached);
                    Vector2 next = lineAt(u);
                    float width = widthAt(u);

                    // (no rock on either end of this stretch: it is over a hole, or past the Earth)
                    if (previousWidth > 0.01f || width > 0.01f)
                    {
                        band(previous, next, 0f, 0f, previousWidth * 0.16f, width * 0.16f, hot, orange);
                        band(previous, next, previousWidth * 0.16f, width * 0.16f, previousWidth * 0.6f, width * 0.6f, orange, soft);
                        band(previous, next, previousWidth * 0.6f, width * 0.6f, previousWidth * 1.8f, width * 1.8f, soft, none);
                    }

                    previous = next;
                    previousWidth = width;

                    if (u >= reached)
                        break;
                }
            }

            /// <summary>The chunks of the Earth that still sit in their places, over their holes in the Earth's picture, with no edges to show for it yet.</summary>
            private void drawHomePieces(BulletField f, Vector2 place, float radius, float degrees, float alpha, Color4 tone)
            {
                for (int k = 0; k < pieceBullets.Length; k++)
                {
                    if (pieceBullets[k] < 0)
                    {
                        var piece = EarthPieces.All[k];
                        f.stageEnder.Add(pictureOf(k), homeOf(k, place, radius, degrees), new Vector2(piece.W * radius, piece.H * radius), degrees, alpha, tone);
                    }
                }
            }

            /// <summary>The chunks that have broken away: flying, turning slowly, their broken edges molten (another picture of each), with an orange glow behind them.</summary>
            private void drawFlownPieces(BulletField f, double t, Color4 tone)
            {
                bool streaks = StreakLevel(t) > 0.01f;

                for (int k = 0; k < pieceBullets.Length; k++)
                {
                    var piece = EarthPieces.All[k];
                    int index = pieceBullets[k];

                    if (index < 0)
                        continue;

                    var bullet = Bullets[index];

                    if (t < bullet.T0 || t >= bullet.Until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    Vector2 at = stageBulletPosition(bullet, t);
                    float turn = (hash01(k, 5, 66) * 2f - 1f) * DanmakusuBalance.ENDER_PIECE_SPIN * (float)((t - bullet.T0) / beat);
                    Vector2 size = new Vector2(piece.W * bigRadius, piece.H * bigRadius);

                    // (it turns round the middle of its own picture)
                    if (tone == Color4.White)
                        f.stageEnder.Add(StageTexture.Glow, at, size * 1.9f, 0f, 0.65f, new Color4(1f, 0.55f, 0.15f, 1f), true);

                    f.stageEnder.Add(StageTexture.EarthPieceHot0 + k, at, size, lineDegrees + turn, 1f, tone);

                    if (streaks)
                        f.addStreak(at, Math.Min(size.X, size.Y), tone == Color4.White ? new Color4(1f, 0.6f, 0.25f, 0.6f * StreakLevel(t)) : Color4.Black);

                    if (tone != Color4.White)
                        f.addLitArc(at, Math.Min(size.X, size.Y));
                }

                for (int k = 0; k < debrisBullets.Length; k++)
                {
                    int index = debrisBullets[k];

                    if (index < 0)
                        continue;

                    var bullet = Bullets[index];

                    if (t < bullet.T0 || t >= bullet.Until || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    var piece = EarthPieces.Debris[k];
                    Vector2 at = stageBulletPosition(bullet, t);
                    float turn = (hash01(k, 11, 66) * 2f - 1f) * DanmakusuBalance.ENDER_PIECE_SPIN * 2f * (float)((t - bullet.T0) / beat);
                    Vector2 size = new Vector2(piece.W * bigRadius, piece.H * bigRadius);

                    f.stageEnder.Add(StageTexture.Glow, at, size * 2f, 0f, 0.55f, new Color4(1f, 0.55f, 0.15f, 1f), true);
                    f.stageEnder.Add(StageTexture.EarthDebris0 + k, at, size, lineDegrees + turn, 1f, tone);

                    if (streaks)
                        f.addStreak(at, Math.Min(size.X, size.Y), new Color4(1f, 0.6f, 0.25f, 0.6f * StreakLevel(t)));
                }
            }

            private void drawWorlds(BulletField f, double t, bool impact, Color4 tone)
            {
                float grown = smoothStep((t - Start) / (grownAt - Start));
                float gone = smoothStep((t - (End - beat)) / beat);
                float streak = StreakLevel(t);

                // (the Moon, off its chain, goes round the Earth, so far out that its outer edge just touches the left and the right wall: past the top and the bottom it is off the screen)
                float orbit = (float)((t - Start) / (beat * DanmakusuBalance.ENDER_MOON_BEATS)) * MathF.Tau;
                float moonSize = f.circleRadius * DanmakusuBalance.HECATIA_MOON_RADIUS * 2f;
                Vector2 round = centre + new Vector2(MathF.Cos(orbit), MathF.Sin(orbit)) * ((f.stageMax.X - f.stageMin.X - moonSize) * 0.5f);
                Vector2 moon = Vector2.Lerp(moonFrom, round, grown);

                f.stageFront.Add(StageTexture.Moon, moon, new Vector2(moonSize), WorldsAct.spinOf(1, t), 1f - gone, tone);

                if (impact)
                    f.addLitArcOverWorlds(moon, moonSize);

                if (streak > 0.01f)
                    f.addStreak(moon, moonSize, impact ? Color4.Black : new Color4(0.8f, 0.8f, 0.85f, streak * 0.7f));

                // (the Sun shrinks and goes to the side it starts from, and crosses the Earth once, from there to the other side, and fades out there; on the screen as much as it can be, as the ends of the line can be off it)
                Vector2 first = Vector2.Clamp(lineAt(0f), f.stageMin, f.stageMax);
                Vector2 sun;

                if (t < slashFrom)
                    sun = Vector2.Lerp(sunFrom, first, grown);
                else if (t < slashTo)
                    sun = Vector2.Clamp(lineAt((float)((t - slashFrom) / (slashTo - slashFrom))), f.stageMin, f.stageMax);
                else
                    sun = Vector2.Clamp(lineAt(1f), f.stageMin, f.stageMax);

                float sunShare = t >= slashTo ? 1f - smoothStep((t - slashTo) / (beat * DanmakusuBalance.ENDER_SUN_FADE_BEATS)) : 1f;

                if (sunShare <= 0f)
                    return;

                float size = f.circleRadius * DanmakusuBalance.HECATIA_SUN_RADIUS * 2f * (1f + (DanmakusuBalance.ENDER_SUN_SIZE - 1f) * grown);

                if (!impact)
                    f.stageFront.Add(StageTexture.Glow, sun, new Vector2(size * 2.2f), 0f, 0.6f * sunShare, new Color4(1f, 0.65f, 0.25f, 1f), true);

                f.stageFront.Add(StageTexture.Sun, sun, new Vector2(size), WorldsAct.spinOf(2, t), sunShare, tone);

                if (impact)
                    f.addLitArcOverWorlds(sun, size);

                if (streak > 0.01f)
                    f.addStreak(sun, size, impact ? Color4.Black : new Color4(1f, 0.7f, 0.3f, streak * 0.7f));
            }
        }

        // ---- when it comes ----

        private List<(BossPeriod Period, double Start)>? worldEnderPlan;

        private static double worldEnderLength(BossPeriod period)
            => Math.Max(period.BeatLength, 1) * (DanmakusuBalance.ENDER_GROW_BEATS + DanmakusuBalance.ENDER_WAIT_BEATS + DanmakusuBalance.ENDER_SWEEP_BEATS + DanmakusuBalance.ENDER_SPLIT_DELAY_BEATS + DanmakusuBalance.ENDER_APART_BEATS);

        /// <summary>
        /// Every World's Ender of the map: one every <see cref="DanmakusuBalance.ENDER_EVERY_BEATS"/> beats of an attack of hers (her attacks follow each other without a pause: what follows on without a gap is one attack), counted from when it starts; an attack shorter than that has none. Only one
        /// can be on at a time, and the last one goes first: going back from the end of the map, one that would still be on when the next one begins is left out.
        /// </summary>
        private static List<(BossPeriod Period, double Start)> planWorldEnders(BossPeriod[] bosses)
        {
            var wanted = new List<(BossPeriod Period, double Start)>();

            foreach (var period in bosses)
            {
                if (period.Boss?.Mechanic != BossMechanic.ThreeWorlds)
                    continue;

                var attacks = BossOps.Attacks(period);
                double every = Math.Max(period.BeatLength, 1) * DanmakusuBalance.ENDER_EVERY_BEATS;

                for (int i = 0; i < attacks.Count;)
                {
                    double from = attacks[i].Start, to = attacks[i].End;
                    int j = i + 1;

                    while (j < attacks.Count && attacks[j].Start <= to + 50)
                        to = Math.Max(to, attacks[j++].End);

                    for (double at = from + every; at <= to + 1; at += every)
                        wanted.Add((period, at));

                    i = j;
                }
            }

            wanted.Sort((a, b) => b.Start.CompareTo(a.Start));

            var kept = new List<(BossPeriod Period, double Start)>();

            foreach (var ender in wanted)
            {
                if (kept.Count == 0 || ender.Start + worldEnderLength(ender.Period) <= kept[^1].Start)
                    kept.Add(ender);
            }

            kept.Reverse();
            return kept;
        }

        // ---- the impact frame, for the whole field ----

        private readonly List<WorldEnderAct> worldEnderActs = new List<WorldEnderAct>();
        private readonly List<(Vector2 At, float Diameter, Color4 Colour)> impactSpots = new List<(Vector2, float, Color4)>();
        private StageLayer stageEnder = null!; // (the World's Ender's Earth: under the bullets)
        private Box impactBackground = null!; // (the white of its impact frame)
        private ImpactStarLayer impactStars = null!; // (its stars, which turn what is under them inside out)
        private bool impactShown;

        /// <summary>Is an impact frame on at <paramref name="t"/>?</summary>
        public bool InImpactFrame(double t)
        {
            if (failed)
                return false;

            foreach (var act in worldEnderActs)
            {
                if (act.InImpact(t))
                    return true;
            }

            return false;
        }

        /// <summary>0..1: the radial blur after the World's Ender's explosion.</summary>
        /// <summary>The World's Ender whose blur after the explosion is the strongest at <paramref name="t"/> (its split is what the blur goes towards).</summary>
        private WorldEnderAct? enderAfterBlurAct(double t)
        {
            WorldEnderAct? strongest = null;
            float level = 0.01f;

            foreach (var act in worldEnderActs)
            {
                float there = act.AfterBlurLevel(t);

                if (there > level)
                {
                    level = there;
                    strongest = act;
                }
            }

            return strongest;
        }

        private float enderAfterBlur(double t)
        {
            float level = 0f;

            foreach (var act in worldEnderActs)
                level = Math.Max(level, act.AfterBlurLevel(t));

            return level;
        }

        /// <summary>
        /// A sharp white arc on the side of a round thing that faces the middle of the screen, as on a ball lit from there. The further from the middle, the thicker: next to nothing in the middle, half of the thing at a corner of the playfield (one of a few pictures, by how far out it is).
        /// </summary>
        private void addLitArc(Vector2 at, float diameter) => addLitArc(stageTree, at, diameter);

        /// <summary>The same for what is drawn over the layer of the arcs: the Moon and the Sun, whose arcs would be under them.</summary>
        private void addLitArcOverWorlds(Vector2 at, float diameter) => addLitArc(stageTop, at, diameter);

        private void addLitArc(StageLayer layer, Vector2 at, float diameter)
        {
            Vector2 middle = (stageMin + stageMax) * 0.5f;
            Vector2 toMiddle = middle - at;

            if (toMiddle.LengthSquared <= 1f)
                return;

            float outThere = Math.Clamp(toMiddle.Length / Math.Max(Vector2.Distance(middle, stageMax), 1f), 0f, 1f);
            int look = Math.Clamp((int)(outThere * StageLayer.LIT_ARCS), 0, StageLayer.LIT_ARCS - 1);

            layer.Add(StageTexture.LitArc0 + look, at, new Vector2(diameter), MathF.Atan2(toMiddle.Y, toMiddle.X) * 180f / MathF.PI, 1f, Color4.White);
        }

        // (the World's Ender whose impact frame and explosion are on this frame: their lines and blur go out from its split)
        private WorldEnderAct? impactAct;

        /// <summary>What the blur of the impact frame goes towards from <paramref name="at"/>: the nearest place on the split, or the middle of the screen when there is none.</summary>
        private Vector2 towardsImpact(Vector2 at) => impactAct != null ? impactAct.TowardsSplit(at) : (stageMin + stageMax) * 0.5f;

        /// <summary>The sharp blur of an anime impact frame: thin lines from all across a thing straight towards the split (the nearest place on it), a few of slightly different lengths, of one thickness nearly all the way, narrowing to a point only at the end towards it.</summary>
        private void addStreak(Vector2 at, float diameter, Color4 colour)
        {
            Vector2 middle = (stageMin + stageMax) * 0.5f;
            Vector2 toMiddle = towardsImpact(at) - at;

            if (toMiddle.LengthSquared < 1f)
                return;

            Vector2 sideways = new Vector2(-toMiddle.Y, toMiddle.X).Normalized();
            int lines = Math.Clamp((int)(diameter / (circleRadius * DanmakusuBalance.ENDER_STREAK_LINE_GAP)), 3, 9);
            float half = circleRadius * DanmakusuBalance.ENDER_STREAK_HALF_WIDTH;

            for (int i = 0; i < lines; i++)
            {
                float aside = (i / (lines - 1f) - 0.5f) * diameter * 0.9f;
                Vector2 from = at + sideways * aside;
                Vector2 toward = towardsImpact(from) - from;
                float distance = toward.Length;

                if (distance < 1f)
                    continue;

                Vector2 way = toward / distance;
                Vector2 across = new Vector2(-way.Y, way.X) * half;

                // (from where it starts, inside the thing, to the thing's edge does not count: a big thing's lines stick out as far as a small one's)
                float inside = MathF.Sqrt(Math.Max(diameter * diameter * 0.25f - aside * aside, 0f));
                float length = inside + distance * DanmakusuBalance.ENDER_STREAK_SHARE * (0.6f + 0.4f * hash01(i, (int)at.X, (int)at.Y));
                Vector2 shoulder = from + way * (length * 0.7f), tip = from + way * length;

                stageMeshes.Add(new MeshQuad { A = from + across, B = from - across, C = shoulder + across, D = shoulder - across, Near = colour, Far = colour });
                stageMeshes.Add(new MeshQuad { A = shoulder + across, B = shoulder - across, C = tip, D = tip, Near = colour, Far = colour });
            }
        }

        /// <summary>
        /// Sharp speed lines all round the middle of the impact (the Earth's), every one pointing at it: each at an angle of its own round it (evenly round, a little off), coming to a point at its inner end, somewhere between a third of the
        /// way out and the Earth's edge, and widening out past the corners of the screen; a new set every moment, and between two sets they still waver a little (another turn and another width every few ms). In the impact frame they
        /// turn what is under them inside out; through the explosion a hot light, dying away, white on the Earth and black beyond it.
        /// </summary>
        private void drawRadialLines(double t, WorldEnderAct act, float level, bool impact)
        {
            Vector2 earth = act.Centre;
            float rim = act.BigRadius;
            float far = Vector2.Distance(stageMin, stageMax) * 0.6f + Vector2.Distance(earth, (stageMin + stageMax) * 0.5f);
            int moment = (int)(t / DanmakusuBalance.ENDER_PEN_LINE_MS);
            int wobble = (int)(t / DanmakusuBalance.ENDER_PEN_WOBBLE_MS);
            float fan = DanmakusuBalance.ENDER_PEN_JITTER, waver = DanmakusuBalance.ENDER_PEN_TURN_DEGREES * MathF.PI / 180f;
            Color4 inside = impact ? Color4.White : new Color4(1f, 0.85f, 0.6f, 0.75f * level);
            Color4 outside = impact ? Color4.Black : inside;

            void piece(Vector2 start, Vector2 e, float from, float to, float tipAt, float widest, Color4 colour)
            {
                if (to <= from)
                    return;

                Vector2 across = new Vector2(-e.Y, e.X);
                float w0 = widest * (from - tipAt) / (far - tipAt), w1 = widest * (to - tipAt) / (far - tipAt);

                stageMeshes.Add(new MeshQuad { A = start + e * from + across * w0, B = start + e * from - across * w0, C = start + e * to + across * w1, D = start + e * to - across * w1, Near = colour, Far = colour });
            }

            for (int i = 0; i < DanmakusuBalance.ENDER_PEN_LINES; i++)
            {
                // (out from the middle: evenly round it, each a little off its place by up to a share of the gap between two, and wavering)
                Vector2 start = earth;
                float angle = MathF.Tau * (i + fan * (hash01(i, moment, 13) - 0.5f)) / DanmakusuBalance.ENDER_PEN_LINES + waver * (hash01(i, wobble, 14) - 0.5f);
                Vector2 e = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float tipAt = rim * (0.35f + 0.65f * hash01(i, moment, 15));
                float widest = (2f + 9f * hash01(i, moment, 17)) * (1f + DanmakusuBalance.ENDER_PEN_WOBBLE * (2f * hash01(i, wobble, 18) - 1f));

                // (in the impact frame they go over everything, bullets and stars too, and turn what is under them inside out, so that they cut through their shapes)
                if (impact)
                {
                    Vector2 tip = start + e * tipAt, end = start + e * far, side = new Vector2(-e.Y, e.X) * widest;

                    impactStars.AddPiece(tip, tip, end + side, end - side);
                    continue;
                }

                // (where it leaves the Earth: inside it is white, beyond it black)
                Vector2 d = start - earth;
                float b = Vector2.Dot(d, e), c = d.LengthSquared - rim * rim;
                float leaves = Math.Max(-b + MathF.Sqrt(Math.Max(b * b - c, 0f)), 0f);

                piece(start, e, tipAt, Math.Min(leaves, far), tipAt, widest, inside);
                piece(start, e, Math.Max(tipAt, leaves), far, tipAt, widest, outside);
            }
        }

        /// <summary>White behind everything, the player and the bosses black, the sharp lines, the flickering stars of an anime impact frame, and the white arcs and sharp streaks of the bullets; the lines and the streaks go on through the explosion.</summary>
        private void drawImpactFrame(double t)
        {
            WorldEnderAct? current = null;
            float level = 0f;

            if (!failed)
            {
                foreach (var act in worldEnderActs)
                {
                    float there = act.StreakLevel(t);

                    if (there > level)
                    {
                        level = there;
                        current = act;
                    }
                }
            }

            bool on = current != null && current.InImpact(t);
            impactAct = level > 0.01f ? current : null;

            if (on != impactShown)
            {
                impactShown = on;
                character.Colour = on ? Color4.Black : Color4.White;
                bossCharacter.Colour = on ? Color4.Black : Color4.White;
                bossCharacter2.Colour = on ? Color4.Black : Color4.White;
                bossCharacter3.Colour = on ? Color4.Black : Color4.White;
                lingeringBoss.Colour = on ? Color4.Black : Color4.White;
                lingeringBoss2.Colour = on ? Color4.Black : Color4.White;
                lingeringBoss3.Colour = on ? Color4.Black : Color4.White;
            }

            impactBackground.Alpha = on ? 1f : 0f;
            impactStars.ClearShapes();

            if (current == null || level <= 0.01f)
            {
                impactSpots.Clear();
                return;
            }

            if (on && hasScreenBounds)
            {
                impactBackground.Position = screenMin;
                impactBackground.Size = screenMax - screenMin;
            }

            drawRadialLines(t, current, level, on);

            foreach (var (at, diameter, colour) in impactSpots)
            {
                addStreak(at, diameter, on ? Color4.Black : new Color4(colour.R, colour.G, colour.B, level * 0.7f));

                if (on)
                    addLitArc(at, diameter);
            }

            if (!on)
                return;

            addLitArc(characterPosition, DanmakusuBalance.CHARACTER_RING_SIZE);
            addStreak(characterPosition, DanmakusuBalance.CHARACTER_RING_SIZE, Color4.Black);

            drawImpactStars(t);
            impactStars.ShapesDone();
        }

        private readonly Vector2[] starPlaces = new Vector2[DanmakusuBalance.ENDER_IMPACT_STARS];
        private readonly Vector2[] starArms = new Vector2[DanmakusuBalance.ENDER_IMPACT_STARS];

        /// <summary>
        /// The stars: a few at a time, their tails straight across and straight up and down, each jumping to a place of its own every moment, at its own beat, where its tails cross no other star's, and turning what is under it inside out. Each has a few echoes towards the
        /// middle of the screen (the blur of the rest), as black and white as itself. Their shape never holds still: other jagged edges and a slightly other size every few ms.
        /// </summary>
        private void drawImpactStars(double t)
        {
            Vector2 span = stageMax - stageMin, middle = (stageMin + stageMax) * 0.5f;
            int wobble = (int)(t / DanmakusuBalance.ENDER_STAR_WOBBLE_MS);
            int echoes = DanmakusuBalance.ENDER_STAR_ECHOES;
            float apart = circleRadius * DanmakusuBalance.ENDER_STAR_APART_CIRCLES;
            int placed = 0;

            bool crosses(Vector2 at, Vector2 arms)
            {
                for (int other = 0; other < placed; other++)
                {
                    Vector2 there = starPlaces[other], reach = starArms[other];
                    float dx = Math.Abs(at.X - there.X), dy = Math.Abs(at.Y - there.Y);

                    // (the tail across of the one over the tail up and down of the other, either way round; or two tails along one line)
                    if ((dx < arms.X && dy < reach.Y) || (dx < reach.X && dy < arms.Y) || (dy < apart && dx < arms.X + reach.X) || (dx < apart && dy < arms.Y + reach.Y))
                        return true;
                }

                return false;
            }

            for (int i = 0; i < DanmakusuBalance.ENDER_IMPACT_STARS; i++)
            {
                int moment = (int)(t / DanmakusuBalance.ENDER_STAR_MS + i * 0.37);
                float share = hash01(i, moment, 73);
                float across = circleRadius * (DanmakusuBalance.ENDER_STAR_SHORTEST + (DanmakusuBalance.ENDER_STAR_LONGEST - DanmakusuBalance.ENDER_STAR_SHORTEST) * share * share);
                float upDown = across * (0.6f + 0.6f * hash01(i, moment, 75));
                Vector2 arms = new Vector2(across, upDown) * 0.5f * (1f + DanmakusuBalance.ENDER_STAR_UNSTABLE);
                Vector2 place = Vector2.Zero;
                bool found = false;

                for (int attempt = 0; attempt < DanmakusuBalance.ENDER_STAR_ATTEMPTS && !found; attempt++)
                {
                    place = stageMin + new Vector2(span.X * hash01(i * 32 + attempt, moment, 71), span.Y * hash01(i * 32 + attempt, moment, 72));
                    found = !crosses(place, arms);
                }

                if (!found)
                    continue;

                starPlaces[placed] = place;
                starArms[placed] = arms;
                placed++;

                float unstable = DanmakusuBalance.ENDER_STAR_UNSTABLE;
                Vector2 reach = new Vector2(across * (1f + unstable * (2f * hash01(i, wobble, 76) - 1f)), upDown * (1f + unstable * (2f * hash01(i, wobble, 77) - 1f))) * 0.5f;

                // (all the way inside out, the echoes too: black and white, no grey)
                for (int k = echoes; k >= 0; k--)
                {
                    float towards = k * DanmakusuBalance.ENDER_STAR_ECHO_SHARE;

                    addImpactStar(place + (middle - place) * towards, reach * (1f - towards * 0.5f), i * (echoes + 1) + k, wobble);
                }
            }
        }

        /// <summary>
        /// One star: a small box in its middle and a tail out of each side of it, the tails side by side pieces that never overlap. The edge of a tail follows |x|^p + |y|^p = 1 from one tail's tip towards the next (deep curves, the box at the deepest), with a straight taper added, so
        /// that a tail stays a line all the way to its tip. Every corner of a tail's edges but the first and the tip sits further in or out, the next one the other way, the two sides of the tail against each other, and differently at every value of <paramref name="wobble"/>: sharp corners and straight lines between them, that never hold still.
        /// </summary>
        private void addImpactStar(Vector2 at, Vector2 reach, int star, int wobble)
        {
            float power = DanmakusuBalance.ENDER_STAR_CURVE, jag = DanmakusuBalance.ENDER_STAR_JAG;
            float needle = Math.Min(reach.X, reach.Y) * DanmakusuBalance.ENDER_STAR_NEEDLE;
            int steps = DanmakusuBalance.ENDER_STAR_STEPS;
            float deepest = MathF.Pow(0.5f, power);
            Vector2 box = reach * deepest + new Vector2(needle * (1f - deepest));

            impactStars.AddPiece(at - box, at + new Vector2(box.X, -box.Y), at + new Vector2(-box.X, box.Y), at + box);

            for (int tail = 0; tail < 4; tail++)
            {
                Vector2 along = tail switch
                {
                    0 => Vector2.UnitX,
                    1 => Vector2.UnitY,
                    2 => -Vector2.UnitX,
                    _ => -Vector2.UnitY,
                };
                Vector2 side = new Vector2(-along.Y, along.X);
                bool across = tail % 2 == 0;
                float start = across ? box.X : box.Y, wide = across ? box.Y : box.X;
                float length = across ? reach.X : reach.Y, other = across ? reach.Y : reach.X;

                Vector2 a = at + along * start + side * wide, c = at + along * start - side * wide;

                for (int j = 1; j <= steps; j++)
                {
                    // (from the box, halfway along the curve between two tails, to the tip; the corners unevenly along it)
                    float s = 0.5f * (1f - j / (float)steps);

                    if (j < steps)
                        s += 0.5f / steps * 0.7f * (hash01(star * 97 + tail * 31 + j, wobble, 81) - 0.5f);

                    float towardsTip = MathF.Pow(1f - s, power);
                    float half = (other - needle) * MathF.Pow(s, power) + needle * (1f - towardsTip) / (1f - deepest);
                    float there = start + (length - start) * (towardsTip - deepest) / (1f - deepest);
                    float up = half, down = half;

                    if (j < steps)
                    {
                        float way = j % 2 == 0 ? jag : -jag;

                        up = Math.Min(half * (1f + way * (0.3f + 0.7f * hash01(star * 97 + tail * 31 + j, wobble, 82))), wide);
                        down = Math.Min(half * (1f - way * (0.3f + 0.7f * hash01(star * 97 + tail * 31 + j, wobble, 83))), wide);
                    }

                    Vector2 b = at + along * there + side * up, d = at + along * there - side * down;

                    impactStars.AddPiece(a, b, c, d);

                    a = b;
                    c = d;
                }
            }
        }

        /// <summary>The bullets of an impact frame are black; each of them is noted for its streak and its arc, also through the explosion (the stage is drawn after the bullets, so they go with them).</summary>
        private void impactBullets(double t, int count)
        {
            impactSpots.Clear();

            if (worldEnderActs.Count == 0 || failed || palette.Length == 0)
                return;

            float level = 0f;

            foreach (var act in worldEnderActs)
                level = Math.Max(level, act.StreakLevel(t));

            if (level <= 0.01f)
                return;

            bool black = InImpactFrame(t);

            for (int i = 0; i < count; i++)
            {
                ref var instance = ref instanceBuffer[i];

                if (black)
                {
                    instance.Shade = 1f;
                    instance.Bloom = 0f;
                }

                if (impactSpots.Count < DanmakusuBalance.ENDER_WHITE_SPOTS)
                    impactSpots.Add((instance.Position, instance.Radius * 2f, palette[instance.Style % palette.Length]));
            }
        }

        /// <summary>The beams of an impact frame are black.</summary>
        private void impactBeams(double t, int beams)
        {
            if (worldEnderActs.Count == 0 || !InImpactFrame(t))
                return;

            for (int i = 0; i < beams; i++)
            {
                laserInstances[i].Colour = Color4.Black;
                laserInstances[i].BorderColour = Color4.Black;
                laserInstances[i].Bloom = 0f;
            }
        }
    }
}
