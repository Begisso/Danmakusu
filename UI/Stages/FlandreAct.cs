using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        private readonly List<FlandreAct> flandreActs = new List<FlandreAct>();
        private FlandreBridge? flandreBridge;

        /// <summary>Tells the beams which of their slices Flandre's eyes have blown up (see <see cref="IBeamBlasts"/>).</summary>
        private sealed class FlandreBridge : IBeamBlasts
        {
            private readonly BulletField field;

            public FlandreBridge(BulletField field) => this.field = field;

            public bool Blasted(Vector2 start, Vector2 velocity, double fired, double time)
            {
                foreach (var act in field.flandreActs)
                {
                    if (act.Blasted(start, velocity, fired, time))
                        return true;
                }

                // (and Kagerou's and Elly's claws)
                foreach (var act in field.clawActs)
                {
                    if (act.Blasted(field, start, velocity, fired, time))
                        return true;
                }

                // (and Youmu's slashes)
                foreach (var act in field.slashActs)
                {
                    if (act.Blasted(start, velocity, fired, time))
                        return true;
                }

                return false;
            }
        }

        // ======================================================================================================================================================
        // Flandre Scarlet: she never pauses. Three clones of her turn round her (two and a half circle radii away; the other way every 18 beats). Every eight beats she throws three red eyes, two beats after each other: each is put on the
        // player's place at that moment and goes off four beats later: everything within about 3.4 circle radii of it is blown up (bullets and slices of beams alike; the player is hurt too). Everything that was blown up is shot again,
        // three times, once from each clone (the clones two beats apart), twice as fast, half as big, glowing red like Remilia's last bullets, outwards, along the line from the eye through where the blown-up thing was; what is shot again cannot be blown up again (it belongs to the
        // act, not to the rings that the eye looks at). Every fourth thing that was blown up also sends four bullets out, right, down, left and up, from the clones (they take it in turns).
        // ======================================================================================================================================================

        private sealed class FlandreAct : StageAct
        {
            private struct Taken
            {
                public Vector2 Position; // (where it was when the eye went off)
                public int Eye;
                public StoredBullet Stored;
                public bool Slice; // (a slice of a beam: it gets a size of its own)
                public bool Cross; // (every n-th: four more bullets)
            }

            private struct Volley
            {
                public double Time;
                public int Clone;
                public Vector2 EyeAt;
                public Taken[] Items;
            }

            private readonly BulletField field;
            private readonly double beat;

            private readonly Vector2[] eyes;
            private readonly bool[] captured;
            private readonly bool[] processed;
            private readonly List<Taken> taken = new List<Taken>();
            private readonly List<Volley> volleys = new List<Volley>();
            private int takenCounter;
            private int shotCount;
            private bool idle = true;

            private static readonly Vector2[] cross_directions = { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) };

            public FlandreAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);

                int most = (int)Math.Clamp((end - start) / Math.Max(beat, 50) * DanmakusuBalance.FLANDRE_EYES_PER_THROW + DanmakusuBalance.FLANDRE_EYES_PER_THROW + 2, 2, 6000);
                eyes = new Vector2[most];
                captured = new bool[most];
                processed = new bool[most];
                AbsorbFrom = start;
                AbsorbTo = end;

                // (the eyes that go off before her section ends; they go off in the order they are put down)
                eyeCount = 0;

                while (eyeCount < most && explodesAt(eyeCount) <= end + 1e-6)
                    eyeCount++;
            }

            private double eyeMs => Math.Max(beat * DanmakusuBalance.FLANDRE_EYE_BEATS, 100);

            private double throwMs => Math.Max(beat * DanmakusuBalance.FLANDRE_THROW_EVERY_BEATS, 100);

            private double gapMs => beat * DanmakusuBalance.FLANDRE_EYE_GAP_BEATS;

            private readonly int eyeCount;

            /// <summary>Eye <paramref name="k"/> is number k % 3 of throw k / 3: the throws come every eight beats, the three eyes of a throw two beats after each other.</summary>
            private double placedAt(int k) => Start + k / DanmakusuBalance.FLANDRE_EYES_PER_THROW * throwMs + k % DanmakusuBalance.FLANDRE_EYES_PER_THROW * gapMs;

            private double explodesAt(int k) => placedAt(k) + eyeMs;

            private float radius => this.field.circleRadius * DanmakusuBalance.FLANDRE_EYE_RADIUS;

            /// <summary>The last eye that goes off at or before <paramref name="time"/> (-1 if none).</summary>
            private int lastExploded(double time)
            {
                int lo = 0, hi = eyeCount - 1, found = -1;

                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;

                    if (explodesAt(mid) <= time + 1e-9)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else
                        hi = mid - 1;
                }

                return found;
            }

            public override bool Absorbs(double t)
            {
                int k = lastExploded(t);
                return k >= 0 && t - explodesAt(k) <= 1500;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                int k = lastExploded(to);
                return k >= 0 && explodesAt(k) >= from;
            }

            // ---- the clones ----

            /// <summary>How far the clones have turned at <paramref name="t"/> (radians): at the start they are at 0, and they turn the other way every 18 beats, so that they come back to it.</summary>
            private float cloneAngle(double t)
            {
                double rel = Math.Max(t - Start, 0);
                double segment = beat * DanmakusuBalance.FLANDRE_TURN_EVERY_BEATS;
                double omega = Math.Tau / (beat * DanmakusuBalance.FLANDRE_CLONE_TURN_BEATS);
                int n = (int)Math.Floor(rel / segment);
                double rest = rel - n * segment;

                return (float)(n % 2 == 1 ? segment * omega - omega * rest : omega * rest);
            }

            private Vector2 cloneOffset(double t, int clone)
            {
                float angle = cloneAngle(t) + clone * MathF.Tau / DanmakusuBalance.FLANDRE_CLONES;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (field.circleRadius * DanmakusuBalance.FLANDRE_CLONE_DISTANCE);
            }

            /// <summary>Puts the clones next to the boss's picture (they fade in and out with her section).</summary>
            public void ShowClones(BulletField f, double now, BossCharacter character)
            {
                if (f.failed || now < Start - 400 || now > End + 400)
                    return;

                float alpha = (float)Math.Clamp(Math.Min((now - Start) / 400.0 + 1.0, (End - now) / 400.0 + 1.0), 0.0, 1.0);

                if (alpha <= 0.01f)
                    return;

                var offsets = new Vector2[DanmakusuBalance.FLANDRE_CLONES];

                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = cloneOffset(now, i);

                character.SetClones(offsets, alpha);
            }

            // ---- the eyes ----

            public override void Reset(BulletField f, double time)
            {
                for (int k = 0; k < eyes.Length; k++)
                {
                    if (placedAt(k) > time)
                        captured[k] = false;

                    if (explodesAt(k) > time)
                        processed[k] = false;
                }

                ClearBullets();
                volleys.Clear();
                taken.Clear();
                takenCounter = 0;
                shotCount = 0;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                if (now < Start)
                {
                    if (!idle)
                        Reset(f, now);

                    idle = true;
                    return;
                }

                idle = false;

                if (now > End + DanmakusuBalance.FLANDRE_BLAST_MS + 60000)
                    return;

                int count = eyeCount;

                for (int k = 0; k < count; k++)
                {
                    double placed = placedAt(k);

                    if (placed > now)
                        break;

                    // (an eye is put on the place where the player is when it is put down; one that is found far too late - after a jump forward - is not put down at all)
                    if (!captured[k] && now <= explodesAt(k) + 50)
                    {
                        eyes[k] = f.characterPosition;
                        captured[k] = true;
                    }
                }
            }

            // ---- what the eyes blow up ----

            /// <summary>Has an eye taken the slice (fired at <paramref name="fired"/>, flying straight from <paramref name="start"/> at <paramref name="velocity"/>) by <paramref name="time"/>? Only the eyes up to <paramref name="lastEye"/> count.</summary>
            private bool blastedBy(Vector2 start, Vector2 velocity, double fired, double time, int lastEye)
            {
                int first = Math.Max(lastExploded(fired - 1e-6) + 1, 0); // (the first eye that goes off after the slice was fired)
                int last = Math.Min(Math.Min(lastExploded(time), lastEye), eyeCount - 1);

                if (last < first)
                    return false;

                float reach = radius;

                for (int k = first; k <= last; k++)
                {
                    if (!captured[k])
                        continue;

                    double at = explodesAt(k);

                    if (at < fired)
                        continue;

                    Vector2 position = start + velocity * (float)(at - fired);

                    if (Vector2.DistanceSquared(position, eyes[k]) <= reach * reach)
                        return true;
                }

                return false;
            }

            public bool Blasted(Vector2 start, Vector2 velocity, double fired, double time) => blastedBy(start, velocity, fired, time, int.MaxValue);

            private bool nextIsCross() => ++takenCounter % Math.Max(DanmakusuBalance.FLANDRE_CROSS_EVERY, 1) == 0;

            /// <summary>A bullet that is within reach of the eye at the moment it goes off is blown up (and shot again later).</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (emitter.Shape == BulletShape.Train)
                    return false;

                // (the step that the bullet has just made has to be the one that the eye went off in; the eyes of a throw go off two beats apart, so it is nearly always one at most)
                for (int k = lastExploded(t); k >= 0 && explodesAt(k) > ScanPrevious; k--)
                {
                    if (!captured[k])
                        continue;

                    double at = explodesAt(k);
                    float share = t > ScanPrevious ? (float)Math.Clamp((at - ScanPrevious) / (t - ScanPrevious), 0.0, 1.0) : 1f;
                    Vector2 position = Vector2.Lerp(from, to, share);

                    if (Vector2.DistanceSquared(position, eyes[k]) > radius * radius)
                        continue;

                    taken.Add(new Taken { Position = position, Eye = k, Stored = f.makeStored(emitter, at), Cross = nextIsCross() });
                    return true;
                }

                return false;
            }

            private void addBullet(BulletField f, double time, Vector2 from, Vector2 velocity, float size, float hit, in StoredBullet stored)
            {
                if (shotCount >= DanmakusuBalance.FLANDRE_MAX_SHOTS)
                    return;

                shotCount++;

                AddBullet(new StageBullet
                {
                    T0 = time,
                    P0 = from,
                    V = velocity,
                    Radius = size,
                    Hit = hit,
                    Style = stored.Style,
                    Shape = stored.Shape,
                    Tint = BulletRenderer.TINT_RED,
                    TintAmount = DanmakusuBalance.REMILIA_BULLET_RED,
                    Bloom = DanmakusuBalance.REMILIA_BLOOM,
                    Until = time + f.stageLife(velocity),
                });
            }

            private static float shotSize(BulletField f, in Taken item)
                => item.Slice ? Math.Max(item.Stored.Radius * DanmakusuBalance.FLANDRE_RESHOT_SIZE, f.circleRadius * 0.12f) : item.Stored.Radius * DanmakusuBalance.FLANDRE_RESHOT_SIZE;

            private static float shotHit(BulletField f, in Taken item)
                => item.Slice ? shotSize(f, item) * DanmakusuBalance.BULLET_HITBOX_SCALE : item.Stored.Hit * DanmakusuBalance.FLANDRE_RESHOT_SIZE;

            /// <summary>The slices of the beams that are within reach of eye <paramref name="k"/> at the moment it goes off (the ones that an eye before it has not taken already).</summary>
            private void takeSlices(BulletField f, int k, double at)
            {
                float reach = radius;
                double step = DanmakusuBalance.FLANDRE_SLICE_MS;
                Vector2 low = f.stageMin - new Vector2(reach), high = f.stageMax + new Vector2(reach);

                foreach (var laser in f.lasers)
                {
                    if (laser.Immune || laser.StartTime > at || at > laser.EndTime + laser.Lifetime)
                        continue;

                    double newest = Math.Min(at, laser.EndTime);
                    double oldest = Math.Max(laser.StartTime, at - laser.Lifetime);
                    double first = laser.StartTime + Math.Ceiling((oldest - laser.StartTime) / step - 1e-9) * step;

                    for (double fired = first; fired <= newest; fired += step)
                    {
                        for (int beam = 0; beam < laser.BeamCount; beam++)
                        {
                            Vector2 origin;
                            Vector2 velocity;

                            if (laser.Rotates)
                            {
                                float angle = laser.SliceAngle(fired, at) + beam * MathF.Tau / laser.BeamCount;
                                origin = laser.SliceOrigin(fired, at);
                                velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * laser.Speed;
                            }
                            else
                            {
                                origin = laser.SlicePosition(fired, fired, beam);
                                velocity = laser.Directions[beam] * laser.Speed;
                            }

                            Vector2 position = origin + velocity * (float)(at - fired);

                            if (position.X < low.X || position.X > high.X || position.Y < low.Y || position.Y > high.Y || Vector2.DistanceSquared(position, eyes[k]) > reach * reach)
                                continue;

                            if (k > 0 && blastedBy(origin, velocity, fired, at, k - 1))
                                continue;

                            var stored = new StoredBullet
                            {
                                Time = at,
                                Speed = Math.Max(laser.Speed, 0.02f),
                                Style = f.comboStyle(laser.ComboIndex, laser.FollowsBoss, laser.BossRank),
                                Shape = BulletShape.Circle,
                                Radius = laser.HalfWidthAtAge(at - fired),
                            };

                            stored.Hit = stored.Radius * DanmakusuBalance.BULLET_HITBOX_SCALE;
                            taken.Add(new Taken { Position = position, Eye = k, Stored = stored, Slice = true, Cross = nextIsCross() });
                        }
                    }
                }
            }

            public override void Late(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                int count = eyeCount;

                for (int k = 0; k < count; k++)
                {
                    double at = explodesAt(k);

                    if (at > now)
                        break;

                    if (processed[k])
                        continue;

                    processed[k] = true;

                    if (!captured[k])
                        continue;

                    explode(f, k, at, now);
                }

                for (int i = volleys.Count - 1; i >= 0; i--)
                {
                    var volley = volleys[i];

                    if (volley.Time > now)
                        continue;

                    volleys.RemoveAt(i);
                    Vector2 origin = f.bossVisualOf(Period) + cloneOffset(volley.Time, volley.Clone); // (each volley is shot from its clone, outwards, along the line from the eye through where the blown-up thing was)

                    for (int n = 0; n < volley.Items.Length; n++)
                    {
                        var item = volley.Items[n];

                        // every n-th thing that was blown up also sends four bullets out, right, down, left and up: from a clone (the clones take it in turns), not from where it was
                        if (item.Cross && n % DanmakusuBalance.FLANDRE_CLONES == volley.Clone)
                        {
                            foreach (var cross in cross_directions)
                                addBullet(f, volley.Time, origin, cross * (item.Stored.Speed * DanmakusuBalance.FLANDRE_RESHOT_SPEED), shotSize(f, item), shotHit(f, item), item.Stored);
                        }

                        Vector2 direction = item.Position - volley.EyeAt; // (outwards: the way from the eye to where the thing was)
                        direction = direction.LengthSquared < 1f ? new Vector2(0f, 1f) : Vector2.Normalize(direction);

                        addBullet(f, volley.Time, origin, direction * (item.Stored.Speed * DanmakusuBalance.FLANDRE_RESHOT_SPEED), shotSize(f, item), shotHit(f, item), item.Stored);
                    }
                }
            }

            /// <summary>(the autoplay) An eye that has been put down and explodes within the time, with the place in its reach.</summary>
            public override bool Springs(BulletField f, Vector2 at, double from, double to)
            {
                if (f.failed || eyeCount == 0)
                    return false;

                // (the first eye that explodes after from: they explode one after the other)
                int low = 0, high = eyeCount;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (explodesAt(middle) <= from)
                        low = middle + 1;
                    else
                        high = middle;
                }

                float reach = radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN;

                for (int k = low; k < eyeCount && explodesAt(k) <= to; k++)
                {
                    if (captured[k] && !processed[k] && Vector2.Distance(at, eyes[k]) <= reach)
                        return true;
                }

                return false;
            }

            private void explode(BulletField f, int k, double at, double now)
            {
                takeSlices(f, k, at);

                var items = new List<Taken>();

                for (int i = taken.Count - 1; i >= 0; i--)
                {
                    if (taken[i].Eye != k)
                        continue;

                    items.Add(taken[i]);
                    taken.RemoveAt(i);
                }

                items.Reverse();

                // and what was blown up is shot again once from each clone, one after the other
                if (items.Count > 0)
                {
                    var array = items.ToArray();
                    double apart = beat * DanmakusuBalance.FLANDRE_CLONE_ASYNC_BEATS;

                    for (int clone = 0; clone < DanmakusuBalance.FLANDRE_CLONES; clone++)
                        volleys.Add(new Volley { Time = at + clone * apart, Clone = clone, EyeAt = eyes[k], Items = array });
                }

                f.playStageSound("mokou_wall_hit");

                // the explosion hurts the player too
                if (Vector2.Distance(f.characterPosition, eyes[k]) <= radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                {
                    int key = Id * 65536 + 62000 + Math.Min(k, 3000);

                    if (f.stageConsumed.Add(key))
                    {
                        f.LastHitCause = $"Flandre's eye {k} (explosion)";
                        f.onDamaged(f.damagePerBullet, true, now);
                        f.hits.Add(new HitRecord(now, stage_hit, key, f.damagePerBullet));
                    }
                }
            }

            /// <summary>0..1: how much the screen shakes (a short jolt when an eye goes off).</summary>
            public float ShakeAt(double now)
            {
                int k = lastExploded(now);

                if (k < 0 || !captured[k])
                    return 0f;

                double u = (now - explodesAt(k)) / 250.0;
                return u >= 1.0 ? 0f : 0.3f * (1f - (float)u) * (1f - (float)u);
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                int count = eyeCount;
                float reach = radius;

                // (only the eyes that are on or have just gone off)
                int from = Math.Max(lastExploded(t - DanmakusuBalance.FLANDRE_BLAST_MS), 0);

                for (int k = from; k < count; k++)
                {
                    double placed = placedAt(k);

                    if (placed > t)
                        break;

                    double at = explodesAt(k);

                    if (!captured[k] || t > at + DanmakusuBalance.FLANDRE_BLAST_MS)
                        continue;

                    Vector2 centre = eyes[k];

                    if (t < at)
                    {
                        // waiting: the red circle (what the explosion will reach) with the eye in it; it comes in over a moment and glows stronger as the explosion nears
                        float arrival = smoothStep((t - placed) / DanmakusuBalance.FLANDRE_EYE_IN_MS);
                        float nearing = (float)((t - placed) / (at - placed));
                        float pulse = 0.5f + 0.5f * MathF.Sin((float)((t - placed) / beat) * MathF.Tau);

                        f.stageBack.Add(StageTexture.Eye, centre, new Vector2(2f * reach), 0f, arrival * (0.38f + 0.3f * nearing) * (0.5f + 0.5f * smoothStep(nearing)), Color4.White); // (the eye is half as opaque when it appears, and smoothly gets back to its opacity until it goes off)

                        // the approach circle: as long as a hit circle's, it closes in on the eye's circle and is gone when the eye goes off
                        double preempt = Math.Min(Math.Max(Period.Preempt, 100.0), at - placed);
                        double approachFrom = at - preempt;

                        if (t >= approachFrom)
                        {
                            float closing = (float)((t - approachFrom) / preempt);
                            float visible = smoothStep((t - approachFrom) / Math.Min(preempt * 0.4, 400.0));

                            f.stageRipple.AddSolidRing(centre, reach * (1f + DanmakusuBalance.FLANDRE_APPROACH_SCALE * (1f - closing)), 2f, 0.9f * visible, Color4.White);
                        }
                        f.stageRipple.AddSolidRing(centre, reach, 1.5f + 1.5f * nearing, arrival * (0.25f + 0.35f * nearing * (0.6f + 0.4f * pulse)), new Color4(1f, 0.1f, 0.1f, 1f));
                    }
                    else
                    {
                        // gone off: a flash, a ring that spreads and the circle fading out
                        float u = (float)((t - at) / DanmakusuBalance.FLANDRE_BLAST_MS);
                        float fade = (1f - u) * (1f - u);

                        f.stageBack.Add(StageTexture.Eye, centre, new Vector2(2f * reach), 0f, 0.68f * fade, Color4.White);
                        f.stageBack.Add(StageTexture.Glow, centre, new Vector2(2f * reach * (1.1f + 0.4f * u)), 0f, 0.9f * fade, new Color4(1f, 0.2f, 0.12f, 1f), true);
                        f.stageRipple.AddSolidRing(centre, reach * (1f + 0.5f * u), 3f + 4f * (1f - u), fade, new Color4(1f, 0.25f, 0.2f, 1f));
                    }
                }
            }
        }
    }
}
