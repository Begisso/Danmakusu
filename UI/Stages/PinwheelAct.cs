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
        // Yuiman Asama (2026-11-26, the user's two screenshots of her arcs): YUIMAN_POINTS points - red, yellow, green, blue - turn round her YUIMAN_POINT_CIRCLES circle radii out, once every
        // YUIMAN_TURN_BEATS, all the same way. Every bullet fired in her section (anyone's but Utsuho's big balls, Yukari's train and what homes; counted ring by ring) does not come out where it was
        // fired: it comes out of a point, the four in turn, two side by side (YUIMAN_PAIR_GAP of their radius apart), flying YUIMAN_AIM_DEGREES from straight out from her, the way the points turn, at the
        // speed it had, in the point's colour; the pairs a point gets of one ring come out one after another, YUIMAN_STREAM_GAP_BEATS apart. As the points turn the streams curl into arcs round her that
        // miss her middle ("those dissentered arcs").
        // Every YUIMAN_ARROW_EVERY_BEATS of her attacks a ring of YUIMAN_ARROWS arrowheads from her, one at the player, at YUIMAN_ARROW_SPEED a plain bullet's speed, flying straight (they do not
        // home): PinwheelArrowsAct. Each lets a basic bullet out to its left or its right about every YUIMAN_DROP_EVERY_BEATS while it is on the screen (which side, and a little earlier or later, by a
        // hash), flying off at once, square to the arrowhead's way give or take YUIMAN_DROP_SPREAD_DEGREES ("something like Parsee but bullets leave immediately"). One that has hit lets out no more.
        // (The arrowheads are an act of their own: they live long, and an act's bullets are passed over as gone only from the first one on - behind them every stream bullet was looked at every frame.
        // What they let out goes with the streams, which live about as long.)
        // Where she is and where the player is are taken when a ring of arrowheads is shot, and kept.
        // ======================================================================================================================================================

        private sealed class PinwheelAct : StageAct
        {
            // (the points' colours, in turn: as in her screenshots)
            private static readonly int[] point_tints = { BulletRenderer.TINT_RED, BulletRenderer.TINT_YELLOW, BulletRenderer.TINT_GREEN, BulletRenderer.TINT_BLUE };

            private static readonly Color4[] point_colours =
            {
                new Color4(1f, 0.25f, 0.3f, 1f), new Color4(1f, 0.9f, 0.25f, 1f), new Color4(0.25f, 0.95f, 0.55f, 1f), new Color4(0.35f, 0.45f, 1f, 1f),
            };

            private readonly double beat;
            private readonly Dictionary<BulletEmitter, long> bases = new Dictionary<BulletEmitter, long>();
            private long counted;

            public PinwheelAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * 32; // (what came out flies on out after her section)
                AbsorbFrom = period.ShootStart; // (what is fired in her section is hers to let out of her points)
                AbsorbTo = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                bases.Clear();
                counted = 0;
                ClearBullets(); // (what the arrowheads let out too: they let it out again)
            }

            /// <summary>How far round point <paramref name="p"/> is at <paramref name="t"/> (radians): the four evenly round her, turning once every YUIMAN_TURN_BEATS.</summary>
            private float pointAngle(int p, double t)
                => p * MathF.Tau / DanmakusuBalance.YUIMAN_POINTS + (float)((t - Period.Start) / (beat * DanmakusuBalance.YUIMAN_TURN_BEATS)) * MathF.Tau;

            private Vector2 pointAt(BulletField f, int p, double t)
            {
                float angle = pointAngle(p, t);
                return f.bossPositionOf(Period) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (f.circleRadius * DanmakusuBalance.YUIMAN_POINT_CIRCLES);
            }

            /// <summary>Which way point <paramref name="p"/> fires at <paramref name="t"/>: YUIMAN_AIM_DEGREES from straight out, the way the points turn.</summary>
            private Vector2 pointWay(int p, double t)
            {
                float angle = pointAngle(p, t) + DanmakusuBalance.YUIMAN_AIM_DEGREES * MathF.PI / 180f;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            /// <summary>Every bullet fired in her section comes out of one of her points instead, in its stream.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t > Period.End || emitter.Sharp || emitter.Shape == BulletShape.Train || emitter.Homing)
                    return false;

                // (only as it is fired: the first step of its ring)
                if (ScanPrevious > emitter.Time + 0.5)
                    return false;

                if (!bases.TryGetValue(emitter, out long first))
                {
                    bases[emitter] = first = counted;
                    counted += emitter.Count;
                }

                // (the points in turn, a pair to each: bullets 0 and 1 to the first, 2 and 3 to the next...; a point's pairs of one ring come out one after another)
                long number = first + bullet;
                long pair = number / 2;
                int p = (int)(pair % DanmakusuBalance.YUIMAN_POINTS);
                int side = number % 2 == 0 ? -1 : 1;
                double at = Math.Max(emitter.Time, Start) + (pair / DanmakusuBalance.YUIMAN_POINTS - first / 2 / DanmakusuBalance.YUIMAN_POINTS) * beat * DanmakusuBalance.YUIMAN_STREAM_GAP_BEATS;

                var stored = f.makeStored(emitter, t);
                Vector2 way = pointWay(p, at);
                Vector2 across = new Vector2(-way.Y, way.X);
                var made = f.makeStageBullet(stored, emitter, bullet, at, pointAt(f, p, at), way * stored.Speed);
                made.P0 += across * (side * made.Radius * DanmakusuBalance.YUIMAN_PAIR_GAP * 0.5f);
                made.Tint = point_tints[p % point_tints.Length];
                made.TintAmount = DanmakusuBalance.YUIMAN_TINT_AMOUNT;
                AddBullet(made);
                return true;
            }

            /// <summary>The points: a glow in each one's colour, with a white middle.</summary>
            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Period.Start || t > Period.End + 400)
                    return;

                float presence = smoothStep((t - Period.Start) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((Period.End + 400 - t) / 400.0);

                if (presence <= 0.005f)
                    return;

                float size = f.circleRadius * 0.9f;

                for (int p = 0; p < DanmakusuBalance.YUIMAN_POINTS; p++)
                {
                    Vector2 at = pointAt(f, p, t);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(size * 2.2f), 0f, 0.55f * presence, point_colours[p % point_colours.Length], true);
                    f.stageBack.Add(StageTexture.Glow, at, new Vector2(size * 0.8f), 0f, 0.9f * presence, Color4.White, true);
                }
            }
        }

        /// <summary>Yuiman Asama's rings of arrowheads, which let basic bullets out to their sides (into <see cref="PinwheelAct"/>'s bullets): see there.</summary>
        private sealed class PinwheelArrowsAct : StageAct
        {
            private sealed class Volley
            {
                public double T;
                public Vector2 From;
                public float Angle; // (of the arrowhead at the player)
                public readonly int[] Index = new int[DanmakusuBalance.YUIMAN_ARROWS]; // (each arrowhead's bullet in Bullets)
                public readonly int[] Drops = new int[DanmakusuBalance.YUIMAN_ARROWS]; // (how many bullets each has let out)
                public readonly bool[] Done = new bool[DanmakusuBalance.YUIMAN_ARROWS];
                public bool Made;
            }

            private readonly double beat;
            private readonly PinwheelAct streams;
            private readonly double[] throwTimes;
            private readonly List<Volley> volleys = new List<Volley>();

            public PinwheelArrowsAct(BossPeriod period, PinwheelAct streams)
            {
                Period = period;
                this.streams = streams;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                var times = new List<double>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = start; t < end && times.Count < 10000; t += beat * DanmakusuBalance.YUIMAN_ARROW_EVERY_BEATS)
                        times.Add(t);
                }

                throwTimes = times.ToArray();
                End = period.End + beat * 32;
            }

            public override double MechanicsEnd => throwTimes.Length > 0 ? throwTimes[^1] + beat * 4 : double.NegativeInfinity;

            public override void Reset(BulletField f, double time)
            {
                // (the rings shot by then keep where they were aimed; their arrowheads, and what they let out, are made again)
                volleys.RemoveAll(v => v.T > time);

                foreach (var volley in volleys)
                {
                    volley.Made = false;
                    Array.Clear(volley.Drops);
                    Array.Clear(volley.Done);
                }

                ClearBullets();
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                // (every ring whose time has come: from where she is, one arrowhead at the player)
                while (volleys.Count < throwTimes.Length && throwTimes[volleys.Count] <= now)
                {
                    Vector2 from = f.bossPositionOf(Period);
                    Vector2 way = f.characterPosition - from;
                    float angle = way.LengthSquared > 1e-6f ? MathF.Atan2(way.Y, way.X) : MathF.PI / 2f;
                    volleys.Add(new Volley { T = throwTimes[volleys.Count], From = from, Angle = angle });
                }

                foreach (var volley in volleys)
                {
                    if (!volley.Made)
                        makeVolley(f, volley);

                    for (int i = 0; i < volley.Index.Length; i++)
                        drop(f, volley, i, now);
                }
            }

            private float arrowSpeed => stageBulletSpeed(Period) * DanmakusuBalance.YUIMAN_ARROW_SPEED;

            private Vector2 arrowVelocity(Volley volley, int i)
            {
                float angle = volley.Angle + i * MathF.Tau / DanmakusuBalance.YUIMAN_ARROWS;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * arrowSpeed;
            }

            private void makeVolley(BulletField f, Volley volley)
            {
                float radius = f.stageBaseRadius(Period);

                for (int i = 0; i < volley.Index.Length; i++)
                {
                    Vector2 v = arrowVelocity(volley, i);
                    volley.Index[i] = Bullets.Count;

                    AddBullet(new StageBullet
                    {
                        T0 = volley.T,
                        P0 = volley.From,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(i, true, Period.Rank),
                        Shape = BulletShape.Triangle,
                        Until = volley.T + f.stageLife(v),
                    });

                    if (Bullets.Count <= volley.Index[i])
                        volley.Index[i] = int.MaxValue;
                }

                volley.Made = true;
            }

            private bool consumed(BulletField f, int index) => DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index);

            /// <summary>The bullets arrowhead <paramref name="i"/> lets out by <paramref name="now"/>, while it is on the screen and has not hit.</summary>
            private void drop(BulletField f, Volley volley, int i, double now)
            {
                if (volley.Done[i] || volley.Index[i] >= Bullets.Count)
                    return;

                double every = beat * DanmakusuBalance.YUIMAN_DROP_EVERY_BEATS;
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.YUIMAN_DROP_SIZE;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.YUIMAN_DROP_SPEED;
                float spread = DanmakusuBalance.YUIMAN_DROP_SPREAD_DEGREES * MathF.PI / 180f;

                for (double t = volley.T + (volley.Drops[i] + 1) * every; t <= now; t = volley.T + (volley.Drops[i] + 1) * every)
                {
                    // (a little earlier or later, to one side or the other, by its own hash)
                    int number = (int)(volley.T % 100000) * 8 + i * 4096 + volley.Drops[i];
                    double when = t - every * 0.6 * hash01(Id, number, 161);
                    var arrow = Bullets[volley.Index[i]];

                    if (when >= arrow.Until || consumed(f, volley.Index[i]) || !f.stageBulletAt(arrow, when, out Vector2 at)
                        || at.X < f.stageMin.X || at.X > f.stageMax.X || at.Y < f.stageMin.Y || at.Y > f.stageMax.Y)
                    {
                        volley.Done[i] = true;
                        return;
                    }

                    Vector2 way = arrowVelocity(volley, i);
                    way = way.LengthSquared > 1e-12f ? Vector2.Normalize(way) : Vector2.UnitY;
                    float side = hash01(Id, number, 162) < 0.5f ? -1f : 1f;
                    Vector2 outward = turned(new Vector2(-way.Y, way.X) * side, (hash01(Id, number, 163) * 2f - 1f) * spread);
                    Vector2 v = outward * speed;

                    streams.AddBullet(new StageBullet
                    {
                        T0 = when,
                        P0 = at,
                        V = v,
                        Radius = radius,
                        Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = f.comboStyle(number, true, Period.Rank),
                        Shape = BulletShape.Circle,
                        Until = when + f.stageLife(v),
                    });

                    volley.Drops[i]++;
                }
            }
        }
    }
}
