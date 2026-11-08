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
        // Sekibanki: every eight beats of an attack she throws one of her heads. It starts flying straight away from the player and is locked onto them like Koakuma's bats (it keeps its
        // speed and turns towards the player as fast as a bat does). A head is a bullet twice as big as her plain ones, drawn as her head (an animated picture), and lives for 64 beats before
        // it fades out. Behind it it leaves a beam twice as wide as a slider's: every slice of it stays where the head was for 32 beats, then flies on the way the head was going there and is
        // gone once it has left the screen. The beam hurts like a slider body. Like the bats the heads cannot be written as a function of time: the act simulates them step by step.
        // Byakuren's beams are the same without a head: when an attack begins, one from each of five points round her, and 32 beats after every fifth circle or slider of her section (5 such places
        // at a time) one from each of five points round where it was, starting away from the player and homing the same way (always: pointing away they still turn, and while they home they come back
        // from past the edge).
        // They live like Koakuma's bats: until they have been within two osu! circle radii of the player for eight beats in all, or have left the playfield after their homing is over. As thin as a
        // slider's beam, and every slice of it is there for 4 beats, narrowing to nothing over the last two, so a beam ends in a point. While the star at the tip of a beam is outside the screen, a red arrow
        // glows at the wall, in the place nearest to it and pointing at the wall: it goes along with the star, grows as the star comes nearer, and beats against the wall twice a beat.
        // ======================================================================================================================================================

        private sealed class HeadAct : StageAct
        {
            /// <summary>How the heads and their beams of one boss are.</summary>
            private sealed class Style
            {
                public bool DrawHeads = true; // (the heads are drawn and hurt; otherwise only their beams are there)
                public double LifeBeats;
                public double StayBeats;
                public bool FlyAway = true; // (a slice flies on when its time is up; otherwise it is just gone)
                public float Width = 1f; // (as a share of a slider's beam)
                public Vector2[] Offsets = [Vector2.Zero]; // (where the heads start, round the boss: one head from each)
                public Func<BossPeriod, List<double>> Throws = null!;
                public bool FromSpots; // (the places the boss remembered throw heads too: BossPeriod.SpotThrows)
                public bool LikeBats; // (a head lives like Koakuma's bats: until it has been near the player long enough, or has left the playfield; otherwise for LifeBeats)
                public float Speed = 1f; // (as a share of the boss's plain bullets' speed)
                public double HomeBeats; // (a head turns towards the player only for this many beats, then flies straight on; 0 = all its life)
                public float TurnFloor; // (pointing away from the player a head turns at least this share of the full turn; 0 = as the pull says, which is nothing at exactly behind it)
                public double NarrowShare; // (a slice narrows to nothing over this last share of its time; 0 = it keeps its width)
                public bool StarHeads; // (the head is a star bullet, Marisa's, at the front tip of its beam: drawn by the field, it hurts like the beam)
                public double SoundBeats; // (the throws are heard at most once in this many beats; 0 = every one)
            }

            private static readonly Style sekibanki = new Style
            {
                LifeBeats = DanmakusuBalance.SEKIBANKI_HEAD_LIFE_BEATS,
                StayBeats = DanmakusuBalance.SEKIBANKI_TRAIL_STAY_BEATS,
                Width = DanmakusuBalance.SEKIBANKI_TRAIL_WIDTH,
                Throws = ThrowTimes,
            };

            private static readonly Style byakuren = new Style
            {
                DrawHeads = false,
                LifeBeats = DanmakusuBalance.BYAKUREN_TRAIL_LIFE_BEATS,
                StayBeats = DanmakusuBalance.BYAKUREN_TRAIL_STAY_BEATS,
                FlyAway = false,
                Width = DanmakusuBalance.BYAKUREN_TRAIL_WIDTH,
                Speed = DanmakusuBalance.BYAKUREN_TRAIL_SPEED,
                HomeBeats = DanmakusuBalance.BYAKUREN_HOMING_BEATS_LIMIT,
                TurnFloor = DanmakusuBalance.BYAKUREN_TURN_FLOOR,
                NarrowShare = DanmakusuBalance.BYAKUREN_TRAIL_NARROW_SHARE,
                StarHeads = true,
                SoundBeats = DanmakusuBalance.BYAKUREN_SOUND_EVERY_BEATS,
                Offsets = pentagon(DanmakusuBalance.BYAKUREN_POINT_RADIUS),
                Throws = period => period.Kind == BossKind.Spinner ? new List<double>() : BossOps.Attacks(period).Where(a => a.End > a.Start).Select(a => a.Start).ToList(),
                FromSpots = true,
                LikeBats = true,
            };

            private static Vector2[] pentagon(float radius)
                => Enumerable.Range(0, DanmakusuBalance.BYAKUREN_BEAM_POINTS).Select(k =>
                {
                    float at = -MathF.PI / 2f + k * MathF.Tau / DanmakusuBalance.BYAKUREN_BEAM_POINTS;
                    return new Vector2(MathF.Cos(at), MathF.Sin(at)) * radius;
                }).ToArray();

            /// <summary>Sekibanki's heads.</summary>
            public static HeadAct Heads(BossPeriod period) => new HeadAct(period, sekibanki);

            /// <summary>Byakuren's beams (no heads).</summary>
            public static HeadAct Beams(BossPeriod period) => new HeadAct(period, byakuren);

            private readonly Style style;
            private const double step_ms = DanmakusuBalance.HOMING_STEP_MS;

            /// <summary>How long each picture of the head is shown (ms), in order (the picture's own timing).</summary>
            private static readonly double[] frame_ms = { 400, 50, 400, 50, 400, 50, 400, 50, 400, 50, 400, 50 };

            private static readonly double cycle_ms = sum(frame_ms);

            private sealed class Head
            {
                public int Index; // (in Bullets)
                public Vector2 Position;
                public Vector2 Velocity;
                public double Time;
                public double Born;
                public double Near; // (like a bat: how long it has been near the player, in all)
                public double Ended = double.NaN; // (like a bat: when it stopped - near the player long enough, or out of the playfield)
                public float Side; // (which way round it turns while it points away from the player: 1 or -1, chosen the first time; 0 = not yet)
                public List<Vector2> Trail = null!;
            }

            private readonly double beat;
            private readonly List<(double Time, Vector2? Spot)> throws; // (no spot: from round the boss)
            private readonly List<Head> heads = new List<Head>();
            private int nextThrow;
            private double soundedAt = double.NegativeInfinity;

            /// <summary>When she throws her heads: every eight beats of every attack, from its start (the sound cues use it too).</summary>
            public static List<double> ThrowTimes(BossPeriod period)
            {
                double beat = Math.Max(period.BeatLength, 1);
                var times = new List<double>();

                foreach (var (start, end) in BossOps.Attacks(period))
                {
                    for (double t = start; t < end; t += beat * DanmakusuBalance.SEKIBANKI_THROW_BEATS)
                        times.Add(t);
                }

                times.Sort();
                return times;
            }

            private HeadAct(BossPeriod period, Style style)
            {
                this.style = style;
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                throws = style.Throws(period).Select(t => (t, (Vector2?)null)).ToList();

                if (style.FromSpots)
                    throws.AddRange(period.SpotThrows.Select(s => (s.Time, (Vector2?)s.Spot)));

                throws.Sort((a, b) => a.Time.CompareTo(b.Time));
                Start = throws.Count > 0 ? throws[0].Time : period.ShootStart;
                End = throws.Count > 0 ? throws[^1].Time + lifetime + beat * style.StayBeats : Start;
            }

            /// <summary>How long a head lives at most (one that lives like a bat: until the safety limit of the bats).</summary>
            private double lifetime => style.LikeBats ? Math.Min(DanmakusuBalance.HOMING_LIFETIME_MS, DanmakusuBalance.KOAKUMA_BAT_LIFETIME_MS) : beat * style.LifeBeats;

            /// <summary>Until when a head lays its beam (and is there to hurt).</summary>
            private double endOf(Head head) => double.IsNaN(head.Ended) ? head.Born + lifetime : head.Ended;

            public override void Reset(BulletField f, double time)
            {
                heads.Clear();
                ClearBullets();
                nextThrow = 0;
                soundedAt = double.NegativeInfinity;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // a head is thrown from where she is (or from a place she remembered), straight away from the player
                while (nextThrow < throws.Count && throws[nextThrow].Time <= now)
                {
                    var (time, spot) = throws[nextThrow++];
                    throwHead(f, time, spot ?? f.bossPositionOf(Period));

                    if (now - time < 250 && time - soundedAt >= beat * style.SoundBeats - 1)
                    {
                        soundedAt = time;
                        f.playStageSound("touhou_strike");
                    }
                }

                Vector2 player = f.characterPosition;
                float gravity = Period.ArrowGravity * DanmakusuBalance.KOAKUMA_BAT_TURN_FACTOR; // (as fast as Koakuma's bats turn)
                float near = f.circleRadius * DanmakusuBalance.KOAKUMA_BAT_FADE_RADIUS_CIRCLES;
                double nearFor = beat * DanmakusuBalance.KOAKUMA_BAT_FADE_BEATS;

                foreach (var head in heads)
                {
                    int guard = 0;

                    while (double.IsNaN(head.Ended) && head.Time + step_ms <= now && head.Time + step_ms <= head.Born + lifetime && guard++ < 600)
                    {
                        // (Byakuren's stop turning after sixteen beats and fly straight on)
                        advance(head, player, (float)step_ms, homing(head) ? gravity : 0f, style.TurnFloor);

                        if (style.LikeBats)
                            liveLikeABat(f, head, player, near, nearFor);
                    }
                }
            }

            private bool homing(Head head) => style.HomeBeats <= 0 || head.Time - head.Born < beat * style.HomeBeats;

            /// <summary>
            /// A head that lives like Koakuma's bats stops (its beam is laid no further, and it cannot hurt any more) once it has been within two circle radii of the player for eight beats in
            /// all, or once it has left the playfield (not while it still homes: thrown away from the player from near the edge it swings out past it, and comes back).
            /// </summary>
            private void liveLikeABat(BulletField f, Head head, Vector2 player, float near, double nearFor)
            {
                if (Vector2.Distance(head.Position, player) <= near)
                    head.Near += step_ms;

                float margin = trailHalfWidth(f) * 2f;
                bool outside = head.Position.X < f.stageMin.X - margin || head.Position.X > f.stageMax.X + margin || head.Position.Y < f.stageMin.Y - margin || head.Position.Y > f.stageMax.Y + margin;

                if (head.Near < nearFor && (!outside || homing(head)))
                    return;

                head.Ended = head.Time;

                var bullet = Bullets[head.Index];
                bullet.Until = head.Time;
                Bullets[head.Index] = bullet;
            }

            private void throwHead(BulletField f, double t, Vector2 centre)
            {
                foreach (var offset in style.Offsets)
                    throwOne(f, t, centre + offset);
            }

            private void throwOne(BulletField f, double t, Vector2 from)
            {
                Vector2 away = from - f.characterPosition;
                away = away.LengthSquared > 1e-6f ? Vector2.Normalize(away) : -Vector2.UnitY;
                // (Byakuren's heads are Marisa's star bullets at the tips of the beams, drawn by the field; Sekibanki's are her head, drawn by the act)
                float radius = style.StarHeads ? f.stageBaseRadius(Period) * DanmakusuBalance.SPINNER_STAR_SCALE * DanmakusuBalance.SPINNER_STAR_SIZE_FACTOR * DanmakusuBalance.BYAKUREN_TIP_STAR_SIZE : f.stageBaseRadius(Period) * DanmakusuBalance.SEKIBANKI_HEAD_SIZE;
                Vector2 velocity = away * (stageBulletSpeed(Period) * style.Speed);
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = from,
                    V = velocity,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Shape = style.StarHeads ? BulletShape.SoftStar : BulletShape.Circle,
                    Until = t + lifetime,
                    Trail = new List<Vector2>(1024) { from },
                    TrailStep = step_ms,
                    Hidden = !style.StarHeads, // (the act draws it: as her head)
                    Harmless = true, // (a head is never used up by a hit, so its beam never goes on without it: it hurts like a slider body instead, see Hurts)
                });

                if (Bullets.Count == index)
                    return;

                heads.Add(new Head { Index = index, Position = from, Velocity = velocity, Time = t, Born = t, Trail = Bullets[index].Trail! });
            }

            /// <summary>One step of a head: it turns towards the player as a bat does (the speed stays the same).</summary>
            private static void advance(Head head, Vector2 player, float step, float gravity, float turnFloor)
            {
                Vector2 velocity = head.Velocity;
                float speed = velocity.Length;

                if (speed > 1e-6f)
                {
                    Vector2 heading = velocity / speed;
                    Vector2 toPlayer = player - head.Position;
                    float length = toPlayer.Length;

                    if (length > 1e-3f)
                    {
                        Vector2 pull = toPlayer * (gravity * step / length);
                        Vector2 sideways = pull - heading * Vector2.Dot(pull, heading);
                        float across = sideways.Length, least = pull.Length * turnFloor;

                        // (pointing away from the player the sideways pull is small, and at exactly behind it nothing: it still turns, one way round for as long as it takes)
                        if (across < least && Vector2.Dot(pull, heading) < 0f)
                        {
                            Vector2 normal = new Vector2(-heading.Y, heading.X);

                            if (head.Side == 0f)
                                head.Side = across > least * 0.01f ? (Vector2.Dot(sideways, normal) >= 0f ? 1f : -1f) : head.Index % 2 == 0 ? 1f : -1f; // (dead straight: by its number)

                            sideways = normal * (head.Side * least);
                        }

                        Vector2 turned = velocity + sideways * DanmakusuBalance.HOMING_TURN_SCALE;
                        float turnedLength = turned.Length;

                        if (turnedLength > 1e-9f)
                            head.Velocity = turned * (speed / turnedLength);
                    }
                }

                head.Position += head.Velocity * step;
                head.Time += step_ms;
                head.Trail.Add(head.Position);
            }

            // ---- the beam behind a head ----

            private float trailHalfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100) * style.Width;

            private float sliceSpeed => stageBulletSpeed(Period) * DanmakusuBalance.LASER_SPEED_FACTOR;

            /// <summary>
            /// Where slice <paramref name="k"/> of a head's beam (laid where the head was <paramref name="k"/> steps after it was thrown) is at <paramref name="t"/>: where it was laid for 32 beats,
            /// then flying on the way the head went there. False when it is not laid yet, or has left the screen.
            /// </summary>
            private bool sliceAt(BulletField f, Head head, int k, double t, out Vector2 position)
            {
                position = default;
                double laid = head.Born + k * step_ms;

                if (k >= head.Trail.Count || t < laid)
                    return false;

                position = head.Trail[k];
                double moving = t - laid - beat * style.StayBeats;

                // (Byakuren's slices are just gone when their time is up)
                if (moving > 0 && !style.FlyAway)
                    return false;

                if (moving > 0)
                {
                    Vector2 way = head.Trail[Math.Min(k + 1, head.Trail.Count - 1)] - head.Trail[Math.Max(k - 1, 0)];
                    way = way.LengthSquared > 1e-9f ? Vector2.Normalize(way) : Vector2.UnitY;
                    position += way * (float)(sliceSpeed * moving);

                    float margin = trailHalfWidth(f) * 2f;

                    if (position.X < f.stageMin.X - margin || position.X > f.stageMax.X + margin || position.Y < f.stageMin.Y - margin || position.Y > f.stageMax.Y + margin)
                        return false;
                }

                return true;
            }

            /// <summary>
            /// The first slice of a head's beam that can still be there at <paramref name="t"/> (a multiple of the slice steps): slices that are just gone when their time is up (Byakuren's) are
            /// only there for a few beats, and her heads can lay their beams for a long time. -1: nothing of the beam is left.
            /// </summary>
            private int firstSlice(Head head, double t)
            {
                if (style.FlyAway)
                    return 0;

                double stay = beat * style.StayBeats;

                if (t > endOf(head) + stay)
                    return -1;

                int every = DanmakusuBalance.SEKIBANKI_TRAIL_SLICE_STEPS;
                int k = (int)Math.Floor((t - stay - head.Born) / step_ms);
                return Math.Max(0, k / every * every);
            }

            private readonly List<Vector2> beamPoints = new List<Vector2>();
            private readonly List<float> beamWidths = new List<float>();

            /// <summary>How wide slice <paramref name="k"/> of a head's beam is at <paramref name="t"/>.</summary>
            private float sliceWidth(BulletField f, Head head, int k, double t)
            {
                float full = trailHalfWidth(f);

                if (style.NarrowShare <= 0)
                    return full;

                double stay = beat * style.StayBeats;
                return full * smoothStep((head.Born + k * step_ms + stay - t) / (stay * style.NarrowShare));
            }

            /// <summary>The beams behind the heads, drawn like the other beams (the field calls this while it hands the beams to the renderer).</summary>
            public void AddTrails(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || heads.Count == 0)
                    return;

                float halfWidth = trailHalfWidth(f);
                float gap = halfWidth * 3f + (float)(stageBulletSpeed(Period) * this.style.Speed * step_ms * DanmakusuBalance.SEKIBANKI_TRAIL_SLICE_STEPS) * 2f;
                int style = f.comboStyle(0, true, Period.Rank);
                Color4 colour = f.palette[style];
                Color4 border = f.borderPalette[style % f.borderPalette.Length];

                foreach (var head in heads)
                {
                    int first = firstSlice(head, t);

                    if (first < 0)
                        continue;

                    beamPoints.Clear();
                    beamWidths.Clear();

                    for (int k = first; k < head.Trail.Count; k += DanmakusuBalance.SEKIBANKI_TRAIL_SLICE_STEPS)
                    {
                        // (a run of the beam ends where a slice is gone, or where two neighbours have drifted apart)
                        if (!sliceAt(f, head, k, t, out Vector2 position) || (beamPoints.Count > 0 && Vector2.Distance(beamPoints[^1], position) > gap))
                        {
                            f.addPlainBeam(beamPoints, halfWidth, colour, border, ref beams, ref pointTotal, beamWidths);
                            beamPoints.Clear();
                            beamWidths.Clear();

                            if (!sliceAt(f, head, k, t, out position))
                                continue;
                        }

                        beamPoints.Add(position);
                        beamWidths.Add(sliceWidth(f, head, k, t));
                    }

                    f.addPlainBeam(beamPoints, halfWidth, colour, border, ref beams, ref pointTotal, beamWidths);
                }
            }

            /// <summary>The heads and their beams hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || heads.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;

                foreach (var head in heads)
                {
                    var bullet = Bullets[head.Index];

                    if ((style.DrawHeads || style.StarHeads) && now >= bullet.T0 && now <= bullet.Until && Vector2.Distance(player, stageBulletPosition(bullet, now)) <= bullet.Hit + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                foreach (var head in heads)
                {
                    int first = firstSlice(head, now);

                    if (first < 0)
                        continue;

                    bool havePrevious = false;
                    Vector2 previous = default;

                    for (int k = first; k < head.Trail.Count; k += DanmakusuBalance.SEKIBANKI_TRAIL_SLICE_STEPS)
                    {
                        if (!sliceAt(f, head, k, now, out Vector2 position))
                        {
                            havePrevious = false;
                            continue;
                        }

                        float reach = sliceWidth(f, head, k, now) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS; // (as far as the beam is wide there)

                        if (havePrevious ? distanceToSegment(player, previous, position) <= reach : Vector2.Distance(player, position) <= reach)
                            return true;

                        previous = position;
                        havePrevious = true;
                    }
                }

                return false;
            }

            // ---- the heads themselves ----

            /// <summary>The red arrow of a star that is outside the screen: at the wall in the place nearest to the star and pointing at the wall, bigger the nearer the star is, beating against the wall; false when the star is not outside.</summary>
            private bool arrowOf(BulletField f, Head head, double t, out Vector2 position, out Vector2 way, out float size, out float beating)
            {
                position = way = Vector2.Zero;
                size = beating = 0f;

                if (t < head.Born || t > endOf(head))
                    return false;

                Vector2 star = stageBulletPosition(Bullets[head.Index], t);
                Vector2 past = star - Vector2.Clamp(star, f.stageMin, f.stageMax);

                if (past.LengthSquared < 1e-6f)
                    return false;

                float near = 1f - Math.Clamp(past.Length / ((f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.BYAKUREN_ARROW_FAR), 0f, 1f);

                beating = 0.5f + 0.5f * MathF.Cos((float)(t / Math.Max(Period.BeatLength, 1) * DanmakusuBalance.BYAKUREN_ARROW_BEATS * Math.Tau));
                size = f.stageBaseRadius(Period) * (DanmakusuBalance.BYAKUREN_ARROW_SIZE + (DanmakusuBalance.BYAKUREN_ARROW_NEAR_SIZE - DanmakusuBalance.BYAKUREN_ARROW_SIZE) * near);
                way = Vector2.Normalize(past);

                Vector2 low = f.stageMin + new Vector2(size), high = f.stageMax - new Vector2(size);

                position = Vector2.Clamp(star, low, high) - way * (size * 0.7f * (1f - beating));
                return true;
            }

            /// <summary>Red arrows at the walls of the screen, for the stars that are outside it (they cannot hit).</summary>
            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (!style.StarHeads || f.failed)
                    return;

                foreach (var head in heads)
                {
                    if (!arrowOf(f, head, t, out Vector2 position, out Vector2 way, out float size, out _))
                        continue;

                    into.Add(new StageBead
                    {
                        Position = position,
                        Radius = size,
                        Style = f.comboStyle(0, true, Period.Rank),
                        Shape = BulletShape.Triangle,
                        Direction = way,
                        Overlay = BulletRenderer.TINT_RED,
                        OverlayAmount = 1f,
                        Harmless = true,
                    });
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                if (style.StarHeads)
                {
                    // (the arrows glow as they beat)
                    foreach (var head in heads)
                    {
                        if (arrowOf(f, head, t, out Vector2 position, out _, out float size, out float beating))
                            f.stageTop.Add(StageTexture.Glow, position, new Vector2(size * 4f), 0f, 0.25f + 0.45f * beating, new Color4(1f, 0.1f, 0.1f, 1f), true);
                    }
                }

                if (!style.DrawHeads)
                    return;

                foreach (var head in heads)
                {
                    var bullet = Bullets[head.Index];

                    if (t < bullet.T0 || t > head.Born + lifetime + DanmakusuBalance.SEKIBANKI_FADE_MS)
                        continue;

                    // (after its 64 beats it stops where it is and fades out)
                    double at = Math.Min(t, head.Born + lifetime);
                    Vector2 position = stageBulletPosition(bullet, at);
                    float alpha = 1f - smoothStep((t - head.Born - lifetime) / DanmakusuBalance.SEKIBANKI_FADE_MS);
                    float size = 2f * bullet.Radius * DanmakusuBalance.SEKIBANKI_PICTURE_SCALE;

                    f.stageFront.Add(StageTexture.SekibankiHead0 + frameAt(t - head.Born), position, new Vector2(size), 0f, alpha, Color4.White);
                }
            }

            private static int frameAt(double age)
            {
                double u = ((age % cycle_ms) + cycle_ms) % cycle_ms;

                for (int i = 0; i < frame_ms.Length; i++)
                {
                    if (u < frame_ms[i])
                        return i;

                    u -= frame_ms[i];
                }

                return 0;
            }

            private static double sum(double[] values)
            {
                double total = 0;

                foreach (double value in values)
                    total += value;

                return total;
            }
        }
    }
}
