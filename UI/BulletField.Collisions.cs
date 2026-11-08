using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        /// <summary>Is the character inside a laser beam right now?</summary>
        private bool touchesLaser(double now)
        {
            int lo = laserLowerBound(now - maxLaserSpan);
            int hi = laserLowerBound(now + 0.001);

            for (int i = lo; i < hi; i++)
            {
                var laser = lasers[i];

                if (laser.Finished || now > laser.EndTime + laser.Lifetime)
                    continue;

                // (the beam can be wider in some places than in others: every point has its own width)
                for (int beam = 0; beam < laser.BeamCount; beam++)
                {
                    if (laser.BeamFinished[beam])
                        continue;

                    int n = laser.GetPoints(now, laserBuffer, beam, laserWidthBuffer, laserDistanceBuffer, laserLookBuffer);

                    // a ripple that is going over the beam makes it fatter there, and so does smoke
                    if ((stageRippleCount > 0 || stageSmokeCount > 0 || sukunaLevel > 0f) && !laser.KeepsSize)
                    {
                        n = refineBeam(n);

                        for (int q = 0; q < n; q++)
                            laserWidthBuffer[q] *= beamGrow(laserBuffer[q]);
                    }

                    markSlicesGone(laser, beam, n, now);

                    if (n == 1 && !sliceGone[0] && laserLookBuffer[0]?.Harmless != true && Vector2.Distance(characterPosition, laserBuffer[0]) <= laserWidthBuffer[0] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;

                    for (int p = 0; p + 1 < n; p++)
                    {
                        if (sliceGone[p] || sliceGone[p + 1])
                            continue; // (that part of the beam has flown into the moon)

                        if (laserLookBuffer[p]?.Harmless == true && laserLookBuffer[p + 1]?.Harmless == true)
                            continue; // (a beam of Reisen's that slides does not hurt)

                        if ((laserLookBuffer[p]?.StretchVisible ?? 1f) <= 0.5f && (laserLookBuffer[p + 1]?.StretchVisible ?? 1f) <= 0.5f)
                            continue; // (nor a piece of a slider's beam that has been stretched so far that it is half faded out)

                        if (laser.GhostMs > 0 && laser.IsGhostAge(Math.Max(laserDistanceBuffer[p], laserDistanceBuffer[p + 1]) / Math.Max(laser.Speed, 1e-6f)) && laser.IsGhostAge(Math.Min(laserDistanceBuffer[p], laserDistanceBuffer[p + 1]) / Math.Max(laser.Speed, 1e-6f)))
                            continue; // (Junko's slices do not hurt while they are see-through)

                        if (portalOf(laserLookBuffer[p]) != portalOf(laserLookBuffer[p + 1]))
                            continue; // (the two slices are on the two sides of a portal: the beam is cut there)

                        if (fallenApart(laserLookBuffer[p], laserLookBuffer[p + 1]) || fallenTooFar(laserLookBuffer[p], laserLookBuffer[p + 1], laserBuffer[p], laserBuffer[p + 1], laserWidthBuffer[p]))
                            continue; // (one has fallen and the other has not, or they are two pieces of what has fallen: the beam is cut there)

                        Vector2 a = laserBuffer[p], b = laserBuffer[p + 1];

                        // (a slice that Zanmu has stopped, has fallen out of the bottom and come in from the top is not joined to its neighbour that has not yet)
                        if (laser.Freezer != null && laserLookBuffer[p] is { Level: >= 0 } && laserLookBuffer[p + 1] is { Level: >= 0 }
                            && Math.Abs(a.Y - b.Y) > (hasScreenBounds ? screenMax.Y - screenMin.Y : stageMax.Y - stageMin.Y) * 0.6f)
                            continue;

                        Vector2 ab = b - a;
                        float lengthSquared = ab.LengthSquared;
                        float t = lengthSquared < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(characterPosition - a, ab) / lengthSquared, 0f, 1f);
                        float width = laserWidthBuffer[p] + (laserWidthBuffer[p + 1] - laserWidthBuffer[p]) * t;

                        if (Vector2.Distance(characterPosition, a + ab * t) <= width * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A rotating beam has slices in all directions, but every slice flies straight away from the origin, and the newest slice (fired when the
        /// laser ended) is the nearest one. When even that is farther away than the farthest corner of the screen, nothing of the beam can be seen again.
        /// </summary>
        private bool rotatingBeamGone(LaserEmitter laser, double time)
        {
            float dx = Math.Max(Math.Abs(laser.Origin.X - screenMin.X), Math.Abs(laser.Origin.X - screenMax.X));
            float dy = Math.Max(Math.Abs(laser.Origin.Y - screenMin.Y), Math.Abs(laser.Origin.Y - screenMax.Y));
            float nearest = laser.Speed * (float)(time - laser.EndTime);

            return nearest - laser.MaxHalfWidth > MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The time at which the last slice of a spinner / boss beam has left the whole screen (see <see cref="rotatingBeamGone"/>).</summary>
        private double rotatingGoneTime(LaserEmitter laser)
        {
            float dx = Math.Max(Math.Abs(laser.Origin.X - screenMin.X), Math.Abs(laser.Origin.X - screenMax.X));
            float dy = Math.Max(Math.Abs(laser.Origin.Y - screenMin.Y), Math.Abs(laser.Origin.Y - screenMax.Y));

            return laser.EndTime + (MathF.Sqrt(dx * dx + dy * dy) + laser.MaxHalfWidth) / laser.Speed;
        }

        /// <summary>Index of the first laser whose start time is >= <paramref name="time"/>.</summary>
        private int laserLowerBound(double time)
        {
            int lo = 0, hi = lasers.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (lasers[mid].StartTime < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        /// <summary>A hit circle is auto-hit at its start time: if the character is inside it right then, that is one bullet-sized hit.</summary>
        private void checkCircleZones(double t0, double t1, Vector2 p0, Vector2 p1, double now)
        {
            double span = t1 - t0;
            if (span <= 0)
                return;

            int lo = circleUpperBound(t0);
            int hi = circleUpperBound(t1);

            for (int i = lo; i < hi; i++)
            {
                var zone = circleZones[i];
                Vector2 position = Vector2.Lerp(p0, p1, (float)((zone.Time - t0) / span));

                if (Vector2.Distance(position, zone.Position) > zone.Radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                    continue;

                LastHitCause = "hit circle";
                onDamaged(damagePerBullet, true, now);
                hits.Add(new HitRecord(now, circle_hit, -1, damagePerBullet));
            }
        }

        /// <summary>Index of the first circle zone whose time is > <paramref name="time"/>.</summary>
        private int circleUpperBound(double time)
        {
            int lo = 0, hi = circleZones.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (circleZones[mid].Time <= time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        // (the collisions are worked out ring by ring on several cores: each ring only marks its own bullets as used up and keeps its own snapshot; what it hit is written down for it, and then, one
        // ring after the other in the same order as ever, the damage is done, the hits are recorded and the slider timer is set - so the health and the replays are what they always were)
        private ulong[] ringHits = new ulong[256];
        private int[] ringSliderTouch = new int[256]; // (the first bullet of the ring that hurts like a slider body and touched, -1: none)

        private void checkCollisions(double t0, double t1, Vector2 p0, Vector2 p1, double now)
        {
            if (ringHits.Length < activeCount)
            {
                ringHits = new ulong[Math.Max(activeCount, ringHits.Length * 2)];
                ringSliderTouch = new int[ringHits.Length];
            }

            if (activeCount >= parallel_min_rings)
                System.Threading.Tasks.Parallel.For(0, activeCount, parallelOptions, k => collideRing(k, t0, t1, p0, p1));
            else
            {
                for (int k = 0; k < activeCount; k++)
                    collideRing(k, t0, t1, p0, p1);
            }

            for (int k = 0; k < activeCount; k++)
            {
                ulong hitBits = ringHits[k];
                int slider = ringSliderTouch[k];

                if (hitBits == 0 && slider < 0)
                    continue;

                int i = active[k];
                var emitter = emitters[i];
                bool sliderDone = slider < 0;

                for (int b = 0; b < emitter.Count; b++)
                {
                    if (!sliderDone && slider <= b)
                    {
                        sliderTouch(emitter, now);
                        sliderDone = true;
                    }

                    if ((hitBits & (1UL << b)) == 0)
                        continue;

                    LastHitCause = $"bullet {emitter.Shape} (ring {i}, no. {b}, ring time {emitter.Time:0}){(emitter.Deflections != null && emitter.Deflections.ContainsKey(b) ? ", deflected" : "")}";
                    onDamaged(damagePerBullet, true, now);
                    hits.Add(new HitRecord(now, i, b, damagePerBullet));
                }

                if (!sliderDone)
                    sliderTouch(emitter, now);
            }
        }

        /// <summary>A bullet that hurts like a slider body touches the character: it costs the slider damage every interval (one timer for all of them).</summary>
        private void sliderTouch(BulletEmitter emitter, double now)
        {
            if (now < nextSliderDamageTime)
                return;

            LastHitCause = emitter.HurtsLikeSlider ? "big ball (like a slider)" : "bullet in smoke or swollen by a ripple";
            nextSliderDamageTime = now + DanmakusuBalance.SLIDER_DAMAGE_INTERVAL_MS;
            onDamaged(damagePerSliderTick, DanmakusuBalance.SLIDER_DAMAGE_COUNTS_AS_MISS, now);
            hits.Add(new HitRecord(now, slider_hit, -1, damagePerSliderTick));
        }

        /// <summary>One ring against the character over the step from <paramref name="t0"/> to <paramref name="t1"/>: what it hit goes into <see cref="ringHits"/> / <see cref="ringSliderTouch"/> at <paramref name="k"/> (it runs on any core).</summary>
        private void collideRing(int k, double t0, double t1, Vector2 p0, Vector2 p1)
        {
            ringHits[k] = 0;
            ringSliderTouch[k] = -1;

            double span = t1 - t0;

            {
                int i = active[k];
                var emitter = emitters[i];

                // (ta, tb: the frame in game time, for the player's path; ea, eb: the same moments on the bullets' own clock, which runs slow during a boss's slowdown)
                double ta = Math.Max(t0, emitter.Time);
                double tb = t1;
                if (ta > tb)
                    return;

                if (emitter.RemovedAt(ta))
                    return; // (it burst: the bullets that came out of it are another ring)

                if (emitter.Derive == DeriveKind.Train)
                    return; // (a train is not used up by a hit: it hurts like a slider body, see touchesTrain)

                double ea = emitter.WithoutFrozenTime(effects.Effective(emitter, ta));
                double eb = Math.Min(emitter.WithoutFrozenTime(effects.Effective(emitter, tb)), emitter.Time + emitter.Lifetime);
                if (ea > eb)
                    return;

                Vector2 pa = Vector2.Lerp(p0, p1, (float)((ta - t0) / span));
                Vector2 pb = Vector2.Lerp(p0, p1, (float)((tb - t0) / span));
                float ra = emitter.SlowestRadiusAt(ea); // the ring spans from its slowest to its fastest bullet
                float rb = emitter.RadiusAt(eb);
                float growMax = (stageRippleCount > 0 ? DanmakusuBalance.RAIKO_RIPPLE_GROW : 1f) * (stageSmokeCount > 0 ? DanmakusuBalance.SANNYO_SMOKE_SIZE : 1f) * (sukunaLevel > 0f ? DanmakusuBalance.SUKUNA_MAX_SIZE : 1f); // (a ripple makes what it goes over bigger, to touch as well, and so does smoke, and Sukuna's distance)
                float reachGrow = growMax * emitter.BigScaleMax; // (and the big bullets of Remilia's attack)
                float bladeHalf = emitter.Blades > 1 ? emitter.VisualRadius * DanmakusuBalance.KNIFE_LENGTH_FACTOR * reachGrow : 0f; // (the blades of a many-edged knife lie up to two handle lengths from its middle)
                float reach = Math.Max(emitter.HitRadius, treeActs.Count > 0 || emitter.FlowerScale > 1f ? fallFlowerRadius * emitter.FlowerScale : 0f) * reachGrow + DanmakusuBalance.PLAYER_HITBOX_RADIUS + bladeHalf * DanmakusuBalance.KNIFE_PIVOT * 2f; // (a bullet that has fallen into a flower is as big as one)

                // broad phase: the player's distance from the ring centre and the ring's radius must overlap at some point in this frame.
                float nearest = distanceToSegment(emitter.Origin, pa, pb);
                float farthest = Math.Max(Vector2.Distance(emitter.Origin, pa), Vector2.Distance(emitter.Origin, pb));
                if (emitter.IsRadial && (farthest < ra - reach || nearest > rb + reach))
                    return;

                // (a ring that is not checked as a whole - ops, bursts, bounces: where its bullets were at the end of the last frame is where this frame starts, and what is worked out now is kept
                // for the renderer and the next frame)
                var seen = !emitter.IsRadial && !emitter.Homing && emitter.Count <= 64 ? snapshotOf(emitter) : null;
                bool fromSeen = seen != null && seen.Time == ea;
                ulong seenAlive = 0, seenKnown = 0;

                for (int b = 0; b < emitter.Count; b++)
                {
                    ulong bit = 1UL << b;
                    if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                        continue;

                    Vector2 positionA;

                    if (emitter.Homing)
                        positionA = emitter.PreviousPosition(b); // pulled bullets are wherever the simulation put them, a step ago and now
                    else
                    {
                        // a bullet that was turned in this very frame has only its newest record: before that moment the closed form gives the path that it would have flown without every turn so far,
                        // which can be a long way from where it was. It is checked from where it was turned on (not along a line from there to here)
                        double sweepFrom = ea;

                        if (emitter.Deflections != null && emitter.Deflections.TryGetValue(b, out var turnedAt) && turnedAt.Time > ea)
                            sweepFrom = Math.Min(turnedAt.Time, eb);

                        if (fromSeen && sweepFrom == ea && (seen!.Known & seen.Alive & bit) != 0)
                            positionA = seen.Position[b];
                        else
                            emitter.Evaluate(b, sweepFrom, out positionA, out _, out _);
                    }

                    Vector2 positionB, dir;
                    bool alive;
                    BulletLook? lookB;

                    // (a frozen bullet stands exactly where it stood until its freeze ends: it is not traced again)
                    if (seen != null && (seen.Known & bit) != 0 && eb >= seen.Time && seen.StillUntil[b] >= eb)
                    {
                        positionB = seen.Position[b];
                        dir = seen.Direction[b];
                        alive = (seen.Alive & bit) != 0;
                        lookB = seen.Look[b];
                    }
                    else
                    {
                        emitter.Evaluate(b, eb, out positionB, out dir, out alive, out lookB, out double still);

                        if (seen != null)
                        {
                            seen.Position[b] = positionB;
                            seen.Direction[b] = dir;
                            seen.Look[b] = lookB;
                            seen.StillUntil[b] = still;
                        }
                    }

                    if (seen != null)
                    {
                        seenKnown |= bit;

                        if (alive)
                            seenAlive |= bit;
                    }

                    if (!alive)
                        continue; // a bouncing bullet that has touched its second wall

                    if (lookB != null && lookB.Harmless)
                        continue; // (Reisen's bullets do not hurt while they slide)

                    // (a bullet of Daiyousei's that has just come in from the other wall has not flown across the screen)
                    if (emitter.WrapsOnce && Vector2.DistanceSquared(positionA, positionB) > DanmakusuBalance.DAIYOUSEI_JUMP * DanmakusuBalance.DAIYOUSEI_JUMP)
                        positionA = positionB;

                    // (one of Daiyousei's that has left the screen and come in again has lost her buff: its own size, and used up by a hit)
                    bool plain = emitter.WrapsOnce && emitter.LostVerdant(b, eb);
                    float ownHit = plain ? emitter.HitRadius / emitter.VerdantScale : emitter.HitRadius;

                    // work in the player's frame of reference: the bullet travels a straight line relative to the player.
                    Vector2 rel0 = positionA - pa;
                    Vector2 rel1 = positionB - pb;

                    float smoke = stageSmokeCount > 0 ? smokeAt(positionB) : 0f;
                    float ripple = stageRippleCount > 0 ? rippleGrow(positionB) : 1f;
                    float grow = emitter.KeepsSize ? 1f : (growMax > 1f ? ripple * smokeGrow(smoke) * (sukunaLevel > 0f ? sukunaGrow(positionB) : 1f) : 1f) * emitter.BigScale(b); // (Utsuho's big balls and the trains keep their size: their hitbox too)

                    // (Raiko: a bullet that her ripple has swollen far enough hurts like a slider body while it is swollen; what keeps its size is not swollen)
                    bool swollen = !emitter.KeepsSize && ripple >= 1f + (DanmakusuBalance.RAIKO_RIPPLE_GROW - 1f) * DanmakusuBalance.RAIKO_SLIDER_FROM;

                    if (grow < 0.02f)
                        continue; // (Sukuna: nothing is there inside her magic circle)

                    // the circle that bounds the hit shape first (cheap), then the real shape.
                    // a bullet that has fallen into a flower (Yuyuko) is hit as a flower: the same five petals that are drawn
                    bool flower = (lookB?.Shape ?? emitter.DrawShape ?? emitter.Shape) == BulletShape.Flower;
                    float hitSize = (flower ? fallFlowerRadius * emitter.FlowerScale : ownHit) * grow;

                    float bladeReach = emitter.Blades > 1 ? emitter.VisualRadius * grow * DanmakusuBalance.KNIFE_LENGTH_FACTOR * DanmakusuBalance.KNIFE_PIVOT * 2f : 0f;

                    if (distanceToSegment(Vector2.Zero, rel0, rel1) > hitSize + DanmakusuBalance.PLAYER_HITBOX_RADIUS + bladeReach)
                        continue;

                    if (emitter.Blades > 1 && !flower)
                    {
                        // a knife with more blades hurts where any of its blades is (each blade is a knife: a circle round its middle)
                        bool touched = false;
                        float half = emitter.VisualRadius * grow * DanmakusuBalance.KNIFE_LENGTH_FACTOR;
                        float spin = emitter.BladeAngle(b, eb);

                        for (int blade = 0; blade < emitter.Blades && !touched; blade++)
                        {
                            Vector2 offset = BulletCollision.BladeOffset(emitter.Blades, blade, dir, half, spin);
                            touched = distanceToSegment(Vector2.Zero, rel0 + offset, rel1 + offset) <= hitSize + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                        }

                        if (!touched)
                            continue;
                    }
                    else

                    if (flower)
                    {
                        if (!BulletCollision.HitsFlower(hitSize, dir, -rel0, -rel1, DanmakusuBalance.PLAYER_HITBOX_RADIUS))
                            continue;
                    }
                    else if (emitter.Shape != BulletShape.Circle
                        && !BulletCollision.Hits(emitter.Shape, ownHit * grow, emitter.Shape is BulletShape.Star or BulletShape.SoftStar ? emitter.Orientation(b, eb) : dir, -rel0, -rel1, DanmakusuBalance.PLAYER_HITBOX_RADIUS))
                        continue;

                    if (smoke >= 0.5f || swollen || (emitter.HurtsLikeSlider && !plain))
                    {
                        // in smoke, or swollen by Raiko's ripple, a bullet hurts like a slider body: it is not used up, it costs the slider damage every interval (one timer for all of them, see sliderTouch); Utsuho's big balls always do
                        if (ringSliderTouch[k] < 0)
                            ringSliderTouch[k] = b;

                        continue;
                    }

                    // (used up: the damage is done afterwards, ring by ring, in order - see checkCollisions)
                    emitter.ConsumedMask |= bit;
                    ringHits[k] |= bit;
                }

                if (seen != null)
                {
                    seen.Time = eb;
                    seen.Alive = seenAlive;
                    seen.Known = seenKnown;
                }
            }
        }

        private readonly Stack<RingSnapshot> snapshotPool = new Stack<RingSnapshot>();

        private RingSnapshot snapshotOf(BulletEmitter emitter)
        {
            if (emitter.Seen == null)
            {
                // (the pool is shared by the cores that look at the rings; a snapshot is cleared when it is handed out, so which one a ring gets does not matter)
                RingSnapshot? pooled;

                lock (snapshotPool)
                    snapshotPool.TryPop(out pooled);

                emitter.Seen = pooled ?? new RingSnapshot();
                emitter.Seen.Time = double.NaN;
                emitter.Seen.Known = emitter.Seen.Alive = 0;
            }

            return emitter.Seen;
        }

        /// <summary>A ring is not looked at any more (or time went back): its snapshot goes back to the pool.</summary>
        private void dropSnapshot(BulletEmitter emitter)
        {
            if (emitter.Seen == null)
                return;

            Array.Clear(emitter.Seen.Look); // (no looks are kept alive by the pool)

            lock (snapshotPool)
                snapshotPool.Push(emitter.Seen);
            emitter.Seen = null;
        }

        /// <summary>
        /// Touching an active slider body, or a laser, costs the standard slider damage every <see cref="DanmakusuBalance.SLIDER_DAMAGE_INTERVAL_MS"/>
        /// (one shared timer, so standing in a laser on a slider is not damaged twice).
        /// </summary>
        private void checkSliderZones(double now)
        {
            if (now < nextSliderDamageTime)
                return;

            int lo = zoneLowerBound(now - maxZoneDuration);
            int hi = zoneLowerBound(now + 0.001);
            bool touching = false;
            string cause = "slider body";

            for (int i = lo; i < hi && !touching; i++)
            {
                var zone = sliderZones[i];

                if (now > zone.EndTime)
                    continue;

                touching = zone.Touches(characterPosition - zone.FallOffset(now) - (zone.FallSpeed > 0f ? CatchLanding : Vector2.Zero), DanmakusuBalance.PLAYER_HITBOX_RADIUS); // (a catch object falls, and lands on the bottom wall: see SliderZone.FallSpeed)
            }

            // the moon hurts like a slider body
            if (!touching)
            {
                touching = touchesMoon(now);
                cause = "moon";
            }

            // the planets, the tree, the black hole and the chains hurt like a slider body too
            if (!touching)
            {
                touching = touchesStage(now);
                cause = "stage: " + lastStageHurt;
            }

            // so does Yukari's train
            if (!touching)
            {
                touching = touchesTrain(now);
                cause = "train";
            }

            // and the arcs of taiko's big notes
            if (!touching)
            {
                touching = touchesDrums(now);
                cause = "big note's ring";
            }

            // a slider body hurts fully; a laser alone hurts less (a laser over a slider body is just the slider's damage)
            double damage = damagePerSliderTick;

            if (!touching)
            {
                if (touchesLaser(now) || touchesStarTail(now))
                    cause = "laser beam";
                else if (touchesMoonStream(now))
                    cause = "moon stream";
                else
                    return;

                damage *= DanmakusuBalance.LASER_DAMAGE_SCALE;
            }

            LastHitCause = cause;

            nextSliderDamageTime = now + DanmakusuBalance.SLIDER_DAMAGE_INTERVAL_MS;
            onDamaged(damage, DanmakusuBalance.SLIDER_DAMAGE_COUNTS_AS_MISS, now);
            hits.Add(new HitRecord(now, slider_hit, -1, damage));
        }

        private int zoneLowerBound(double time)
        {
            int lo = 0, hi = sliderZones.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (sliderZones[mid].StartTime < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        /// <summary>Damage + miss: health loss, miss marker at the hit position, combo break (with sound) and an accuracy-costing miss.</summary>
        private void onDamaged(double amount, bool countsAsMiss, double now)
        {
            if (healthProcessor == null || healthProcessor.HasFailed)
                return;

            // once the song is over nothing can hurt the player any more (bullets and beams may still be flying)
            if (now >= songEndTime)
                return;

            applyDamage(amount);
            PlayerHit?.Invoke(characterPosition);

            if (!countsAsMiss || scoreProcessor is not DanmakusuScoreProcessor danmakusuScore)
                return;

            // at most one miss sound per cooldown. Misses that osu! itself voices (combo >= 20) also start the cooldown.
            if (now - lastMissSoundTime >= DanmakusuBalance.MISS_SOUND_COOLDOWN_MS || now < lastMissSoundTime)
            {
                lastMissSoundTime = now;

                if (danmakusuScore.Combo.Value < built_in_combo_break_threshold)
                    comboBreakSound.Play();
            }

            danmakusuScore.AddMiss();
        }

        private void applyDamage(double amount)
        {
            if (healthProcessor == null || healthProcessor.HasFailed)
                return;

            healthProcessor.Health.Value -= amount;

            if (healthProcessor.Health.Value <= healthProcessor.Health.MinValue)
                healthProcessor.TriggerFailure();
        }
    }
}
