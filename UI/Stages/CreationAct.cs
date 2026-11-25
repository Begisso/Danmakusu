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
        // Shinki (2026-11-22): she stands where Kaguya stands, at the top of the screen in the middle. Three pairs of wings as in her old sprite (StageLayer's: long, white going pink towards their scalloped
        // lower edge, the tips curling up; each SHINKI_WING_SIZE of the screen long) come out of her back over two beats, turned SHINKI_WING_TURNS, and sway - rubbery, not jointed as Matenshi's: a bend runs
        // out along each wing from the root to the tip (each drawn as strips along its bent middle line), at most SHINKI_WING_SWAY_DEGREES at the tip, each pair a little after the one above it; they fold
        // back into her over the last beat of her section. They do not hurt.
        // Every bullet fired in her section while her wings are out (anyone's but Utsuho's and Nina's big balls and Yukari's train, counted ring by ring) does not come out where it was fired: it comes out of
        // the middle of a wing - the six in turn - flying the way that wing's stream points then, at the speed it had. Each wing's stream starts straight out from her and turns round once in
        // SHINKI_STREAM_TURN_BEATS, neighbouring wings the other way round; the bullets a wing gets of one ring come out of it one after another, SHINKI_STREAM_GAP_BEATS apart, so that they make streams.
        // Every beam of her section is fired again from a wing, the six in turn, straight, at the player (the planner makes it so: LaserEmitter.FromSpotStraight); this act puts where each starts.
        // ======================================================================================================================================================

        private sealed class CreationAct : StageAct
        {
            private readonly double beat;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;
            private readonly List<LaserEmitter> beams = new List<LaserEmitter>();

            public CreationAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End;
                AbsorbFrom = period.ShootStart;
                AbsorbTo = period.End;

                // (the beams the planner has given her wings, in the order they start)
                foreach (var laser in f.lasers)
                {
                    if (laser.FromSpot >= 0 && laser.BossRank == period.Rank && laser.StartTime >= period.ShootStart - 1 && laser.StartTime < period.End)
                        beams.Add(laser);
                }
            }

            /// <summary>She stands where Kaguya stands: in the middle at the top of the screen.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = new Vector2((f.stageMin.X + f.stageMax.X) / 2f, f.stageMin.Y + DanmakusuBalance.MAGIC_CIRCLE_RADIUS + DanmakusuBalance.KAGUYA_TOP_GAP);
                return now >= Period.Start && now <= Period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                ClearBullets();
            }

            // ---- the wings ----

            /// <summary>Wing <paramref name="w"/>: 0..2 on her right (the highest, the middle, the lowest), 3..5 on her left (the lowest, the middle, the highest): neighbours round her are next to each other.</summary>
            private static bool leftOf(int w) => w >= 3;

            private static int tierOf(int w) => w < 3 ? w : 5 - w;

            /// <summary>How open the wings are (0 in her .. 1 out): out over the first two beats, back in over the last one.</summary>
            private float openness(double t)
                => smoothStep((t - Start) / (beat * DanmakusuBalance.SHINKI_WING_GROW_BEATS)) * (1f - smoothStep((t - (End - beat)) / beat));

            /// <summary>How much of the wings is seen: they fade in at the start of her section and out at its very end.</summary>
            private float presence(double t)
            {
                double appear = beat * 0.5;
                return (float)Math.Clamp(Math.Min((t - Start) / appear, (End - t) / appear), 0, 1);
            }

            /// <summary>How long a wing is (the whole picture) at <paramref name="t"/>: small as it comes out of her.</summary>
            private float wingLength(BulletField f, double t) => (f.stageMax.X - f.stageMin.X) * DanmakusuBalance.SHINKI_WING_SIZE * (0.3f + 0.7f * openness(t));

            /// <summary>
            /// How far along wing <paramref name="w"/> (<paramref name="s"/>: 0 at the root, 1 at the tip) points at <paramref name="t"/>, radians, clockwise - down - for a right wing: its own turn (folded up behind her
            /// as it comes out), and the rubbery sway, which runs out along it from the root to the tip, bending the outer part more.
            /// </summary>
            private float heading(int w, float s, double t, float open)
            {
                int tier = tierOf(w);
                float wave = MathF.Sin((float)((t - Start) / (beat * DanmakusuBalance.SHINKI_WING_SWAY_BEATS)) * MathF.Tau - tier * 0.7f - 2.2f * s);
                float degrees = DanmakusuBalance.SHINKI_WING_TURNS[tier] * open - 70f * (1f - open) + DanmakusuBalance.SHINKI_WING_SWAY_DEGREES * (1f + 2f * s) / 3f * wave * open;
                return degrees * MathF.PI / 180f;
            }

            /// <summary>Where the root of wing <paramref name="w"/> is: at her back, the higher pair a little higher, the lower a little lower.</summary>
            private Vector2 rootOf(BulletField f, int w, double t, float length)
            {
                float side = leftOf(w) ? -1f : 1f;
                return f.bossPositionOf(Period) + new Vector2(side * 0.03f, DanmakusuBalance.SHINKI_WING_ROOTS[tierOf(w)]) * length * openness(t);
            }

            private const int spine_steps = 40;

            /// <summary>
            /// The middle line of wing <paramref name="w"/> at <paramref name="t"/>, bent as it is: the places (and headings) at shares <paramref name="along"/> of the picture's length from its root end - the root
            /// is <see cref="StageLayer.SHINKI_WING_ROOT_U"/> of the way along. In the frame of a right wing; mirrored for a left one.
            /// </summary>
            private void spine(BulletField f, int w, double t, float[] along, Vector2[] at, float[] heads)
            {
                float open = openness(t);
                float length = wingLength(f, t);
                float side = leftOf(w) ? -1f : 1f;
                float reach = (1f - StageLayer.SHINKI_WING_ROOT_U) * length; // (from the root to the tip)
                float h0 = heading(w, 0f, t, open);

                // (the line runs through the middle of the picture's height; the root is a little below it)
                Vector2 start = rootOf(f, w, t, length) + new Vector2(side * -MathF.Sin(h0), MathF.Cos(h0)) * ((0.5f - StageLayer.SHINKI_WING_ROOT_V) * StageLayer.SHINKI_WING_ASPECT * length);

                Vector2 p = Vector2.Zero;
                float done = 0f;
                float step = reach / spine_steps;

                for (int i = 0; i < along.Length; i++)
                {
                    float d = (along[i] - StageLayer.SHINKI_WING_ROOT_U) * length;

                    // (before the root: straight back from it)
                    if (d <= 0f)
                    {
                        at[i] = start + new Vector2(side * MathF.Cos(h0), MathF.Sin(h0)) * d;
                        heads[i] = h0;
                        continue;
                    }

                    while (done < d - 1e-3f)
                    {
                        float ds = Math.Min(step, d - done);
                        float h = heading(w, Math.Clamp((done + ds * 0.5f) / Math.Max(reach, 1f), 0f, 1f), t, open);
                        p += new Vector2(MathF.Cos(h), MathF.Sin(h)) * ds;
                        done += ds;
                    }

                    at[i] = start + new Vector2(side * p.X, p.Y);
                    heads[i] = heading(w, Math.Clamp(d / Math.Max(reach, 1f), 0f, 1f), t, open);
                }
            }

            private readonly float[] middleAlong = { 0.45f };
            private readonly Vector2[] middleAt = new Vector2[1];
            private readonly float[] middleHead = new float[1];

            /// <summary>The middle of wing <paramref name="w"/> at <paramref name="t"/>, where its stream comes out.</summary>
            private Vector2 wingMiddle(BulletField f, int w, double t)
            {
                spine(f, w, t, middleAlong, middleAt, middleHead);
                return middleAt[0];
            }

            /// <summary>Which way wing <paramref name="w"/>'s stream points at <paramref name="t"/>: from straight out from her along the wing (open and still), turning round once in SHINKI_STREAM_TURN_BEATS, every second wing the other way round.</summary>
            private float streamAngle(int w, double t)
            {
                float side = leftOf(w) ? -1f : 1f;
                float h = DanmakusuBalance.SHINKI_WING_TURNS[tierOf(w)] * MathF.PI / 180f;
                Vector2 rest = new Vector2(side * 0.03f, DanmakusuBalance.SHINKI_WING_ROOTS[tierOf(w)]) + new Vector2(side * MathF.Cos(h), MathF.Sin(h)) * (0.45f - StageLayer.SHINKI_WING_ROOT_U);
                float turn = (float)((t - Start) / (beat * DanmakusuBalance.SHINKI_STREAM_TURN_BEATS)) * MathF.Tau;
                return MathF.Atan2(rest.Y, rest.X) + (w % 2 == 0 ? turn : -turn);
            }

            private static readonly float[] strip_middles = makeStripMiddles();
            private readonly Vector2[] stripAt = new Vector2[StageLayer.SHINKI_WING_STRIPS];
            private readonly float[] stripHead = new float[StageLayer.SHINKI_WING_STRIPS];

            private static float[] makeStripMiddles()
            {
                var middles = new float[StageLayer.SHINKI_WING_STRIPS];

                for (int k = 0; k < middles.Length; k++)
                {
                    var (from, to) = StageLayer.ShinkiWingStrip(k);
                    middles[k] = (from + to) * 0.5f;
                }

                return middles;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > End)
                    return;

                float alpha = presence(t) * DanmakusuBalance.SHINKI_WING_ALPHA;

                if (alpha <= 0.005f)
                    return;

                float length = wingLength(f, t);
                float height = length * StageLayer.SHINKI_WING_ASPECT;

                // (the lowest pair first, the highest over them; every strip of a wing where the bent middle line has it, turned as the line is there)
                for (int tier = 2; tier >= 0; tier--)
                {
                    foreach (bool left in new[] { false, true })
                    {
                        int w = left ? 5 - tier : tier;
                        float side = left ? -1f : 1f;
                        spine(f, w, t, strip_middles, stripAt, stripHead);

                        for (int k = 0; k < StageLayer.SHINKI_WING_STRIPS; k++)
                        {
                            var (from, to) = StageLayer.ShinkiWingStrip(k);
                            var texture = (left ? StageTexture.ShinkiWingLeft0 : StageTexture.ShinkiWingRight0) + k;
                            f.stageBack.Add(texture, stripAt[k], new Vector2((to - from) * length, height), side * stripHead[k] * 180f / MathF.PI, alpha, Color4.White);
                        }
                    }
                }
            }

            // ---- the beams: each starts from its wing ----

            public override void Early(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (where each beam starts is put before the field aims it: the middle of its wing as it starts. The same place every frame, so it does not matter that it is put again)
                foreach (var laser in beams)
                {
                    if (laser.StartTime < now - 1000)
                        continue;

                    if (laser.StartTime > now + 1000)
                        break;

                    laser.Origin = wingMiddle(f, laser.FromSpot % DanmakusuBalance.SHINKI_WINGS, Math.Max(laser.StartTime, now));
                }
            }

            // ---- the streams ----

            /// <summary>Every bullet fired in her section comes out of a wing instead, in its stream.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t > End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (presence(t) < 0.5f)
                    return false; // (her wings are not out yet, or not any more)

                // (every bullet comes out twice, of two wings in turn: 2026-11-25, "double the bullets count" - with the gap halved, every stream is twice as dense)
                int copies = DanmakusuBalance.SHINKI_BULLET_COPIES;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += (long)emitter.Count * copies;
                }

                var stored = f.makeStored(emitter, t);

                for (int copy = 0; copy < copies; copy++)
                {
                    // (the six wings in turn; a wing's share of one ring comes out one after another)
                    long number = first + (long)bullet * copies + copy;
                    int w = (int)(number % DanmakusuBalance.SHINKI_WINGS);
                    double at = Math.Max(emitter.Time, Start) + (number / DanmakusuBalance.SHINKI_WINGS - first / DanmakusuBalance.SHINKI_WINGS) * beat * DanmakusuBalance.SHINKI_STREAM_GAP_BEATS;

                    float angle = streamAngle(w, at);
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * stored.Speed;

                    AddBullet(f.makeStageBullet(stored, emitter, bullet, at, wingMiddle(f, w, at), velocity));
                }

                return true;
            }
        }
    }
}
