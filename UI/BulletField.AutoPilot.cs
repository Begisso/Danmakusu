using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Mods;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    // Autoplay: the character dodges by itself, as little as it can - the way the lazy player of the star rating does (ForcedMovement), but on the field as it really is, bosses and all.
    //
    // It looks AUTO_HORIZON_MS ahead in steps of AUTO_SAMPLE_MS. While nothing would touch it within that time it stands still. Otherwise it walks the shortest way to a place that it can reach and stay
    // at safely for the whole time (of places as near, the one nearest the middle of the screen): with keyboard controls at the keyboard's speed (the player's own, from the boss menu), with the mouse
    // a little faster (AUTO_CURSOR_SPEED). If no walk is safe it teleports, as the freeze keys or Space do, to the nearest place that is; if nowhere is, it goes where it is touched latest.
    //
    // What it knows of the future: every ring's bullets where their own flight puts them (an aimed ring that has not been fired yet is aimed only when it is: it is dodged from then on), the stages'
    // bullets on their lines (one that the ops steer goes straight on from where it was last seen), the beams, the moon's stars, the circles about to be hit and the slider bodies; the acts (planets,
    // cracks, hands, claws, the train, the big notes' rings...) answer for any place and moment themselves. It only moves the character: the map plays itself as ever.
    public partial class BulletField
    {
        /// <summary>(Tools/SectionCheck) the bench's character is moved by the autoplay.</summary>
        public static bool AutoPilotProbe;

        /// <summary>(Tools/SectionCheck) how often the autoplay walked off and teleported, and how often it found nowhere safe, since this was last cleared.</summary>
        public static int AutoWalks, AutoTeleports, AutoTrapped;

        /// <summary>(Tools/SectionCheck) the time the autoplay took (Stopwatch ticks): gathering near, gathering for the walks, gathering everything, looking at ways, looking at teleports.</summary>
        public static readonly long[] AutoTicks = new long[9];

        private static void autoTick(int step, long since) => AutoTicks[step] += System.Diagnostics.Stopwatch.GetTimestamp() - since;

        /// <summary>(Tools/SectionCheck) rings, bullets and whole tracks the gathering has looked at.</summary>
        public static long AutoRingsSeen, AutoBulletsSeen, AutoTracksMade;

        /// <summary>(Tools/SectionCheck) why the rings looked at all the way were not passed by at once.</summary>
        public static Dictionary<string, long>? AutoWhy;

        private bool? autoPilotOn;

        /// <summary>Does the autoplay move the character? (An Autoplay mod is on; never in the boss menu's preview.)</summary>
        private bool autoPilot => autoPilotOn ??= !Preview && (AutoPilotProbe || drawableRuleset?.Mods?.Any(mod => mod is ModAutoplay) == true);

        /// <summary>(Tools/SectionCheck) how long the bench's autoplay may think in a frame (ms; 0: <see cref="DanmakusuBalance.AUTO_BUDGET_MS"/>).</summary>
        public static double AutoThinkingProbe;

        /// <summary>How long the autoplay may think in a frame (ms; no setting: the user, 2026-11-13, "choose one that fits for your algorithm").</summary>
        private static double autoThinking => AutoThinkingProbe > 0 ? AutoThinkingProbe : DanmakusuBalance.AUTO_BUDGET_MS;

        private static readonly int auto_samples = Math.Max((int)Math.Round(DanmakusuBalance.AUTO_HORIZON_MS / DanmakusuBalance.AUTO_SAMPLE_MS), 1);
        private static readonly int auto_stride = auto_samples + 1;

        /// <summary>(a bullet that moves this far between two samples has jumped - through a wall, a portal: it is not swept along the way)</summary>
        private const float auto_jump = 120f;

        /// <summary>(how far off the straight line from where it is first to where it is last a bullet's way can bend within the time, for the first look at whether it comes near)</summary>
        private const float auto_bend = 40f;

        /// <summary>(the room kept from a homing bullet, more than from others: where it goes is only guessed, straight on)</summary>
        private const float auto_homing_room = 14f;

        private readonly double[] autoTimes = new double[auto_stride];

        // what can touch the character: every bullet's place at every sample (NaN: not there, or harmless then) and how near it touches
        private Vector2[] autoTrack = new Vector2[512 * auto_stride];
        private float[] autoReach = new float[512];
        private int autoTracks;

        private struct AutoBeam
        {
            public Vector2 A, B;
            public float WidthA, WidthB;
        }

        private struct AutoPoint
        {
            public Vector2 Position;
            public float Reach;
        }

        // the beams and the moon's stars at every second sample (the sample between uses the one before)
        private readonly List<AutoBeam>[] autoBeams = Enumerable.Range(0, auto_stride).Select(_ => new List<AutoBeam>()).ToArray();
        private readonly List<AutoPoint>[] autoPoints = Enumerable.Range(0, auto_stride).Select(_ => new List<AutoPoint>()).ToArray();

        private readonly Vector2[] autoLaserPoints = new Vector2[4096];
        private readonly float[] autoLaserWidths = new float[4096];
        private readonly BulletLook?[] autoLaserLooks = new BulletLook?[4096];

        private readonly Vector2[] autoPath = new Vector2[auto_stride];
        private readonly double[] autoOwn = new double[auto_stride];

        private Vector2 autoTarget;
        private bool autoWalking;
        private double autoLast = double.NaN;

        // (where the teleport may go: a grid over the screen, and which of its places something passes within the time)
        private bool[] autoMarked = Array.Empty<bool>();
        private int autoColumns, autoRows;

        /// <summary>
        /// Where the autoplay puts the character this frame (<paramref name="previous"/>: when the last frame was). <paramref name="teleported"/>: it got there without going the way between (the freeze
        /// keys' or Space's teleport), so the way is not checked.
        /// </summary>
        private Vector2 autoPilotMove(double now, double previous, out bool teleported)
        {
            teleported = false;
            Vector2 here = characterPosition;
            double dt = Math.Clamp(now - previous, 0, 100);

            // (time went back - a seek: it starts again from where it is, and looks at every ring again)
            if (double.IsNaN(autoLast) || now < autoLast)
            {
                autoWalking = false;
                Array.Clear(autoPassedUntil);
                autoForgetTracks(now, true);
            }
            else
                autoForgetTracks(now, false);

            autoLast = now;
            autoNewFrame(now);

            // (a ring passed by stays passed by for a few frames: longer when the frames are long, or at a low frame rate every ring would be looked at again every frame - and the frames get longer still)
            autoPassMs = Math.Clamp(dt * 2.5, auto_pass_ms, auto_pass_most_ms);

            bool keyboard = Controls.Mode == ControlMode.Keyboard;
            float speed = keyboard ? DanmakusuBalance.KEYBOARD_SPEED * (float)Controls.KeyboardSpeed : DanmakusuBalance.AUTO_CURSOR_SPEED;
            float furthest = speed * (float)DanmakusuBalance.AUTO_HORIZON_MS;
            Vector2 low = stageMin + new Vector2(DanmakusuBalance.AUTO_EDGE), high = stageMax - new Vector2(DanmakusuBalance.AUTO_EDGE);
            Vector2 middle = (stageMin + stageMax) / 2;

            // (first only what comes near where it is, or near the way it is walking: most of the time that is all there is to look at)
            long since = System.Diagnostics.Stopwatch.GetTimestamp();
            autoGather(now, here, autoWalking ? Vector2.Distance(here, autoTarget) : 0f);
            autoTick(0, since);
            since = System.Diagnostics.Stopwatch.GetTimestamp();

            if (autoWalking)
            {
                bool on = autoWay(here, autoTarget, speed, dt, previous) > auto_samples;
                autoTick(3, since);

                if (on)
                    return autoStep(here, speed, dt);

                autoWalking = false;
            }
            else
            {
                int stay = autoWay(here, here, 0f, dt, previous);
                autoTick(3, since);

                if (stay > auto_samples)
                    return here;
            }

            // it has to move: the nearest place it can walk to and stay at, safe all the time it looks ahead - the short walks first, with only what comes near them gathered (most dodges are short,
            // and gathering everything within a long walk is what costs), the long ones while there is time.
            // (how long it has thought: the search gets AUTO_BUDGET_MS whatever gathering what is near and asking whether it can stay took - those are done every frame, and when they took the whole
            // budget it looked only at the shortest walks and was hit, 2026-11-13; past it, it takes the best it has)
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = autoThinking;
            bool over() => (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency > budget;

            int latest = int.MinValue;
            Vector2 latestPlace = here;
            bool latestJump = false;
            int n = 0;

            foreach (float reach in new[] { Math.Min(DanmakusuBalance.AUTO_SHORT_WALK, furthest), furthest })
            {
                if (reach > DanmakusuBalance.AUTO_SHORT_WALK && over())
                    break;

                since = System.Diagnostics.Stopwatch.GetTimestamp();
                autoGather(now, here, reach + speed * (float)dt);
                autoTick(1, since);
                since = System.Diagnostics.Stopwatch.GetTimestamp();

                if (latest == int.MinValue)
                    latest = autoWay(here, here, 0f, dt, previous);

                for (; n < DanmakusuBalance.AUTO_LENGTHS; n++)
                {
                    float distance = DanmakusuBalance.AUTO_SMALLEST_DODGE + (furthest - DanmakusuBalance.AUTO_SMALLEST_DODGE) * n / Math.Max(DanmakusuBalance.AUTO_LENGTHS - 1, 1);

                    if (distance > reach + 0.5f || (reach > DanmakusuBalance.AUTO_SHORT_WALK && over()))
                        break;

                    float best = float.PositiveInfinity;
                    Vector2 bestPlace = here;
                    bool found = false;

                    for (int w = 0; w < DanmakusuBalance.AUTO_WAYS; w++)
                    {
                        // (a long walk stops looking once the time is up, even within its length: what it found so far still counts)
                        if (reach > DanmakusuBalance.AUTO_SHORT_WALK && (w & 3) == 3 && over())
                            break;

                        float angle = w * MathF.Tau / DanmakusuBalance.AUTO_WAYS;
                        Vector2 place = Vector2.Clamp(here + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance, low, high);

                        if ((place - here).LengthSquared < 1f)
                            continue;

                        int hit = autoWay(here, place, speed, dt, previous);

                        if (hit > auto_samples)
                        {
                            float score = (place - middle).LengthSquared;

                            if (score < best)
                            {
                                best = score;
                                bestPlace = place;
                                found = true;
                            }
                        }
                        else if (hit > latest)
                        {
                            latest = hit;
                            latestPlace = place;
                            latestJump = false;
                        }
                    }

                    if (found)
                    {
                        autoTick(3, since);
                        AutoWalks++;
                        autoTarget = bestPlace;
                        autoWalking = true;
                        return autoStep(here, speed, dt);
                    }
                }

                autoTick(3, since);
            }

            // no walk gets it out in time: a teleport to the nearest place that is safe for the whole time (first among the places nothing passes at all) - looked for near it first, then on the whole screen,
            // as long as there is time
            foreach (float room in new[] { DanmakusuBalance.AUTO_TELEPORT_NEAR, float.PositiveInfinity })
            {
                // (the places near it are always looked at, the first few of them even past the time: a slow frame must not leave it standing in what comes; the whole screen only while there is time)
                bool nearOnly = !float.IsPositiveInfinity(room);

                if (!nearOnly && over())
                    break;

                since = System.Diagnostics.Stopwatch.GetTimestamp();
                autoGather(now, here, room);
                autoMarkGrid(low, high);
                autoTick(2, since);
                since = System.Diagnostics.Stopwatch.GetTimestamp();

                var places = new List<(float Away, Vector2 Place, bool Marked)>(autoMarked.Length);

                // (only what was gathered is known: places further than that are not looked at)
                float within = float.IsPositiveInfinity(room) ? float.PositiveInfinity : (room - DanmakusuBalance.AUTO_GRID) * (room - DanmakusuBalance.AUTO_GRID);

                for (int y = 0; y < autoRows; y++)
                {
                    for (int x = 0; x < autoColumns; x++)
                    {
                        Vector2 place = new Vector2(low.X + (x + 0.5f) * DanmakusuBalance.AUTO_GRID, low.Y + (y + 0.5f) * DanmakusuBalance.AUTO_GRID);
                        float away = (place - here).LengthSquared;

                        if (place.X > high.X || place.Y > high.Y || away > within)
                            continue;

                        places.Add((away, place, autoMarked[y * autoColumns + x]));
                    }
                }

                places.Sort((a, b) => a.Marked != b.Marked ? (a.Marked ? 1 : -1) : a.Away.CompareTo(b.Away));
                int looked = 0, tried = 0;

                foreach (var (_, place, marked) in places)
                {
                    // (of the places something passes, only the nearest few are looked at closely: when the whole screen is that full, one of them is the way out if any is)
                    if (marked && looked++ > 200)
                        break;

                    if (++tried > (nearOnly ? auto_teleport_least : 0) && (tried & 15) == 0 && over())
                        break;

                    int hit = autoWay(place, place, 0f, dt, previous);

                    if (hit > auto_samples)
                    {
                        autoTick(4, since);
                        AutoTeleports++;
                        teleported = true;
                        autoWalking = false;
                        return place;
                    }

                    if (hit > latest)
                    {
                        latest = hit;
                        latestPlace = place;
                        latestJump = true;
                    }
                }

                autoTick(4, since);
            }

            // nowhere is safe (or no time left to find it): where it is touched latest
            AutoTrapped++;
            autoWalking = false;

            if (latestJump)
            {
                teleported = true;
                return latestPlace;
            }

            if (latestPlace != here)
            {
                autoTarget = latestPlace;
                return autoStep(here, speed, dt);
            }

            return here;
        }

        /// <summary>One frame's walk towards <see cref="autoTarget"/>.</summary>
        private Vector2 autoStep(Vector2 here, float speed, double dt)
        {
            Vector2 way = autoTarget - here;
            float left = way.Length;
            float reach = speed * (float)dt;

            if (left <= reach || left < 1e-3f)
            {
                autoWalking = false;
                return autoTarget;
            }

            return here + way * (reach / left);
        }

        /// <summary>
        /// Walking from <paramref name="from"/> to <paramref name="to"/> at <paramref name="speed"/> (this frame's step first) and staying there - or, at speed 0, being at <paramref name="to"/> from this
        /// frame on: the first sample at which something touches it (more than <see cref="auto_samples"/>: never within the time it looks ahead).
        /// </summary>
        private int autoWay(Vector2 from, Vector2 to, float speed, double dt, double previous)
        {
            float distance = Vector2.Distance(from, to);

            for (int i = 0; i <= auto_samples; i++)
            {
                if (speed <= 0f || distance < 1e-3f)
                    autoPath[i] = to;
                else
                {
                    float walked = speed * (float)(dt + autoTimes[i] - autoTimes[0]);
                    autoPath[i] = Vector2.Lerp(from, to, Math.Min(walked / distance, 1f));
                }

                // (Eiki's circles hold the character in: where it would really be, on their border at most; 2026-11-14)
                foreach (var judgement in judgementActs)
                    autoPath[i] = judgement.Held(this, autoPath[i], autoTimes[i]);
            }

            return autoFirstHit(previous);
        }

        /// <summary>The first sample at which <see cref="autoPath"/> is touched by anything gathered, a circle being hit, a slider body or an act.</summary>
        private int autoFirstHit(double previous)
        {
            for (int i = 0; i <= auto_samples; i++)
            {
                Vector2 p = autoPath[i];
                Vector2 before = i > 0 ? autoPath[i - 1] : p;

                // the bullets: swept between two samples, as the hit check sweeps them between two frames
                for (int j = 0; j < autoTracks; j++)
                {
                    Vector2 a = autoTrack[j * auto_stride + i];

                    if (float.IsNaN(a.X))
                        continue;

                    float reach = autoReach[j];
                    Vector2 relative = a - p;

                    if (i > 0)
                    {
                        Vector2 b = autoTrack[j * auto_stride + i - 1];

                        if (!float.IsNaN(b.X) && (a - b).LengthSquared < auto_jump * auto_jump)
                        {
                            if (distanceToSegment(Vector2.Zero, b - before, relative) <= reach)
                                return i;

                            continue;
                        }
                    }

                    if (relative.LengthSquared <= reach * reach)
                        return i;
                }

                // the beams and the moon's stars
                foreach (var beam in autoBeams[autoBeamSlot[i]])
                {
                    Vector2 along = beam.B - beam.A;
                    float lengthSquared = along.LengthSquared;
                    float u = lengthSquared < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - beam.A, along) / lengthSquared, 0f, 1f);

                    if (Vector2.Distance(p, beam.A + along * u) <= beam.WidthA + (beam.WidthB - beam.WidthA) * u)
                        return i;
                }

                foreach (var point in autoPoints[autoStarSlot[i]])
                {
                    if ((point.Position - p).LengthSquared <= point.Reach * point.Reach)
                        return i;
                }

                // the circles that are hit in this step
                double from = i > 0 ? autoTimes[i - 1] : previous, to = autoTimes[i];

                for (int z = circleUpperBound(from); z < circleZones.Length && circleZones[z].Time <= to; z++)
                {
                    var zone = circleZones[z];
                    float f = to > from ? (float)((zone.Time - from) / (to - from)) : 1f;

                    if (Vector2.Distance(Vector2.Lerp(before, p, f), zone.Position) <= zone.Radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN)
                        return i;
                }

                // what the sliders and the stages say for themselves (every second sample: they are slow, or warned of), and what of the stages goes off since the last time asked
                if ((i & 1) == 0 && autoProbe(p, i >= 2 ? autoTimes[i - 2] : previous, autoTimes[i]))
                    return i;
            }

            return auto_samples + 1;
        }

        /// <summary>
        /// Would a slider body, the moon, the train, a big note's ring or an act hurt the character at <paramref name="p"/> at <paramref name="t"/>, or something of an act go off there since
        /// <paramref name="from"/>?
        /// </summary>
        private bool autoProbe(Vector2 p, double from, double t)
        {
            foreach (var act in stageActs)
            {
                if (act.Springs(this, p, from, t))
                    return true;
            }

            int lo = zoneLowerBound(t - maxZoneDuration);
            int hi = zoneLowerBound(t + 0.001);

            for (int i = lo; i < hi; i++)
            {
                var zone = sliderZones[i];

                if (t > zone.EndTime)
                    continue;

                if (zone.Touches(p - zone.FallOffset(t) - (zone.FallSpeed > 0f ? CatchLanding : Vector2.Zero), DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN))
                    return true;
            }

            if (stageActs.Count == 0 && stageBeads.Count == 0 && trainRings.Length == 0 && DrumRings.Length == 0 && moons.Length == 0)
                return false;

            // (they all look at where the character is: it is put there for the question, and back)
            Vector2 kept = characterPosition;
            characterPosition = p;

            try
            {
                return touchesStage(t) || touchesTrain(t) || touchesDrums(t) || touchesMoon(t);
            }
            finally
            {
                characterPosition = kept;
            }
        }

        // ---- what can touch the character ----

        /// <summary>Gathers what can touch the character within the time it looks ahead and comes within <paramref name="radius"/> of <paramref name="centre"/> (infinity: everything).</summary>
        private void autoGather(double now, Vector2 centre, float radius)
        {
            autoSetTimes(now);

            for (int i = 0; i <= auto_samples; i++)
            {
                autoBeams[i].Clear();
                autoPoints[i].Clear();
            }

            autoTracks = 0;

            float growMax = (stageRippleCount > 0 ? DanmakusuBalance.RAIKO_RIPPLE_GROW : 1f) * (stageSmokeCount > 0 ? DanmakusuBalance.SANNYO_SMOKE_SIZE : 1f) * (sukunaLevel > 0f ? DanmakusuBalance.SUKUNA_MAX_SIZE : 1f);

            if (autoPassedUntil.Length != emitters.Length)
            {
                autoPassedUntil = new double[emitters.Length];
                autoPassedAt = new Vector2[emitters.Length];
                autoPassedRoom = new float[emitters.Length];
            }

            long since = System.Diagnostics.Stopwatch.GetTimestamp();

            for (int k = 0; k < activeCount; k++)
                autoRing(active[k], emitters[active[k]], centre, radius, growMax);

            // (and the rings that are fired within the time)
            for (int j = nextSpawn; j < emitters.Length && emitters[j].Time <= autoTimes[auto_samples]; j++)
                autoRing(j, emitters[j], centre, radius, growMax);

            autoTick(5, since);
            since = System.Diagnostics.Stopwatch.GetTimestamp();
            autoStage(centre, radius);
            autoTick(6, since);
            since = System.Diagnostics.Stopwatch.GetTimestamp();
            autoLasers(centre, radius);
            autoTick(7, since);
            since = System.Diagnostics.Stopwatch.GetTimestamp();
            autoStream(centre, radius);
            autoTick(8, since);
        }

        private int autoNewTrack(float reach)
        {
            if (autoTracks == autoReach.Length)
            {
                Array.Resize(ref autoReach, autoReach.Length * 2);
                Array.Resize(ref autoTrack, autoReach.Length * auto_stride);
            }

            autoReach[autoTracks] = reach;
            return autoTracks++ * auto_stride;
        }

        private static readonly Vector2 not_there = new Vector2(float.NaN);

        /// <summary>(a ring passed by - nothing of it comes near - stays passed by this long, while the character is within this much more room of where it was: a ring is looked at again every few frames;
        /// with long frames up to two and a half frames, at most auto_pass_most_ms)</summary>
        private const double auto_pass_ms = 60;

        private const double auto_pass_most_ms = 150;

        private double autoPassMs = auto_pass_ms;

        /// <summary>(the teleport places near the character that are looked at even when its time to think is up)</summary>
        private const int auto_teleport_least = 32;

        private const float auto_pass_room = 40f;

        private double[] autoPassedUntil = Array.Empty<double>();
        private Vector2[] autoPassedAt = Array.Empty<Vector2>();
        private float[] autoPassedRoom = Array.Empty<float>();

        private void autoRing(int index, BulletEmitter emitter, Vector2 centre, float radius, float growMax)
        {
            if (emitter.Retired || emitter.Derive == DeriveKind.Train || autoTimes[auto_samples] < emitter.Time)
                return;

            // (nothing left in it: every bullet has burst into another ring, been used up or taken)
            ulong gone = emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask;

            if (emitter.Count <= 64 && gone == (emitter.Count == 64 ? ulong.MaxValue : (1UL << emitter.Count) - 1))
                return;

            bool infinite = float.IsPositiveInfinity(radius);

            // (passed by a moment ago, while the character has not gone far: passed by again)
            if (!infinite && index < autoPassedUntil.Length && autoTimes[0] < autoPassedUntil[index] && Vector2.Distance(centre, autoPassedAt[index]) + radius <= autoPassedRoom[index])
                return;

            // (the tests below hold for a little longer and a little further than asked, so that what they pass by stays passed by for that long: most rings are far away, and reading their clocks is what costs)
            float room = infinite ? radius : radius + auto_pass_room;

            // (its clock at the first and the last sample: how far it can get within the time)
            double ownFirst = emitter.WithoutFrozenTime(effects.Effective(emitter, Math.Max(autoTimes[0], emitter.Time)));

            if (ownFirst > emitter.Time + emitter.Lifetime)
                return;

            double ownLast = Math.Min(emitter.WithoutFrozenTime(effects.Effective(emitter, autoTimes[auto_samples] + autoPassMs)), emitter.Time + emitter.Lifetime);
            double ownSpan = Math.Max(ownLast - ownFirst, 0) + 30; // (and a frame: it was seen a frame ago)

            void passBy()
            {
                // (only a ring that has been fired: one fired by a boss takes where she is then, and one that comes out of a wall where the wall is then)
                if (infinite || index >= autoPassedUntil.Length || autoTimes[0] < emitter.Time)
                    return;

                autoPassedUntil[index] = autoTimes[0] + autoPassMs;
                autoPassedAt[index] = centre;
                autoPassedRoom[index] = room;
            }

            // (a first look at the whole ring, from where the hit check saw its bullets a frame ago: the box round them, as far out as the fastest of them flies in the time its clock moves on. Most
            // rings are far away, and working out their clocks at every sample is what costs)
            var seen = emitter.Seen;

            if (!infinite && seen != null && seen.Known != 0 && autoTimes[0] >= emitter.Time && !emitter.Homing)
            {
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, fastest = 0f;
                bool known = true;

                for (int b = 0; b < emitter.Count && b < 64; b++)
                {
                    ulong bit = 1UL << b;

                    if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0 || ((seen.Known & bit) != 0 && (seen.Alive & bit) == 0))
                        continue;

                    if ((seen.Known & bit) == 0)
                    {
                        known = false;
                        break;
                    }

                    Vector2 p = seen.Position[b];
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                    fastest = Math.Max(fastest, emitter.SpeedOf(b));
                }

                if (known)
                {
                    if (float.IsPositiveInfinity(minX))
                        return; // (nothing of it is left)

                    float out_ = fastest * Math.Max(emitter.SpeedRampTo, 1f) * (emitter.Falling ? 3f : 1.25f) * (float)ownSpan
                                 + Math.Max(emitter.HitRadius, emitter.FlowerScale > 1f || treeActs.Count > 0 ? fallFlowerRadius * emitter.FlowerScale : 0f) * growMax * emitter.BigScaleMax
                                 + emitter.VisualRadius * DanmakusuBalance.KNIFE_LENGTH_FACTOR * 2f + auto_bend + room;

                    if (centre.X < minX - out_ || centre.X > maxX + out_ || centre.Y < minY - out_ || centre.Y > maxY + out_)
                    {
                        passBy();
                        return;
                    }
                }
            }

            float body = Math.Max(emitter.HitRadius, treeActs.Count > 0 || emitter.FlowerScale > 1f ? fallFlowerRadius * emitter.FlowerScale : 0f);
            float bladeReach = emitter.Blades > 1 ? emitter.VisualRadius * DanmakusuBalance.KNIFE_LENGTH_FACTOR * growMax * emitter.BigScaleMax * DanmakusuBalance.KNIFE_PIVOT * 2f : 0f;
            float extra = bladeReach + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN;

            // (not fired yet: from where it will be fired, as far as its fastest bullet flies in the time)
            if (!infinite && seen == null && autoTimes[0] < emitter.Time && !emitter.Homing)
            {
                Vector2 low = emitter.Origin, high = emitter.Origin;

                if (emitter.Origins != null)
                {
                    foreach (var origin in emitter.Origins)
                    {
                        low = Vector2.ComponentMin(low, origin);
                        high = Vector2.ComponentMax(high, origin);
                    }
                }

                float fastest = 0f;

                for (int b = 0; b < emitter.Count; b++)
                    fastest = Math.Max(fastest, emitter.SpeedOf(b));

                float out_ = fastest * Math.Max(emitter.SpeedRampTo, 1f) * (emitter.Falling ? 3f : 1.25f) * (float)ownSpan + emitter.SpawnRadius + body * growMax * emitter.BigScaleMax + extra + auto_bend + room;

                if (float.IsFinite(low.X) && float.IsFinite(high.X) && (centre.X < low.X - out_ || centre.X > high.X + out_ || centre.Y < low.Y - out_ || centre.Y > high.Y + out_))
                {
                    passBy();
                    return;
                }
            }

            // (a ring that only grows round its middle: not at all when it never comes near - from its clock at the first and the last sample only)
            if (emitter.IsRadial && !infinite)
            {
                float most = body * growMax * emitter.BigScaleMax + extra;
                float d = Vector2.Distance(emitter.Origin, centre);

                if (d + room < emitter.SlowestRadiusAt(ownFirst) - most || d - room > emitter.RadiusAt(ownLast) + most)
                {
                    passBy();
                    return;
                }
            }

            // (a ring that has been fired and is not steered step by step: what is worked out of it is kept from frame to frame, on the grid of moments - see RingTrack)
            var track = !emitter.Homing && emitter.Count <= 64 && index < nextSpawn && autoTimes[0] >= emitter.Time ? autoRingTrack(index, emitter) : null;

            // (its own clock at every sample; NaN: not fired yet, or gone)
            int first = -1, last = -1;

            for (int i = 0; i <= auto_samples; i++)
            {
                double own = double.NaN;

                if (track != null && i > 0)
                    own = track.Own[autoSlot(track, emitter, autoGrid0 + i)];
                else
                {
                    double t = autoTimes[i];

                    if (t >= emitter.Time && !emitter.RemovedAt(t))
                    {
                        own = emitter.WithoutFrozenTime(effects.Effective(emitter, t));

                        if (own > emitter.Time + emitter.Lifetime)
                            own = double.NaN;
                    }
                }

                autoOwn[i] = own;

                if (double.IsNaN(own))
                    continue;

                if (first < 0)
                    first = i;

                last = i;
            }

            if (first < 0)
                return;

            AutoRingsSeen++;
            AutoBulletsSeen += emitter.Count;

            if (AutoWhy != null)
            {
                string why = emitter.IsRadial ? "radial" : seen == null ? (emitter.Count > 64 ? "big" : "no snapshot") : seen.Known == 0 ? "nothing seen" : "box near";
                AutoWhy[why] = AutoWhy.GetValueOrDefault(why) + 1;
            }

            int firstSlot = track != null ? autoSlot(track, emitter, autoGrid0 + Math.Max(first, 1)) : -1;
            int nowSlot = track != null ? autoSlot(track, emitter, autoGrid0) : -1;
            float between = (float)Math.Clamp((autoTimes[0] - autoGrid0 * DanmakusuBalance.AUTO_SAMPLE_MS) / DanmakusuBalance.AUTO_SAMPLE_MS, 0, 1);

            for (int b = 0; b < emitter.Count; b++)
            {
                if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & (1UL << b)) != 0)
                    continue;

                float reach = body * (emitter.KeepsSize ? 1f : growMax * emitter.BigScale(b)) + extra;

                if (emitter.Homing)
                {
                    // (steered step by step: where it is now, going straight on the way it flies)
                    emitter.Evaluate(b, autoOwn[first], out Vector2 now, out Vector2 facing, out bool there, out BulletLook? look);

                    if (!there || look?.Harmless == true)
                        continue;

                    Vector2 velocity = facing * emitter.SpeedOf(b);

                    if (!infinite && distanceToSegment(centre, now, now + velocity * (float)(autoTimes[last] - autoTimes[first])) > radius + reach + auto_bend)
                        continue;

                    // (it turns towards the character as it flies: kept further from)
                    int homing = autoNewTrack(reach + auto_homing_room);

                    for (int i = 0; i <= auto_samples; i++)
                        autoTrack[homing + i] = double.IsNaN(autoOwn[i]) ? not_there : now + velocity * (float)(autoTimes[i] - autoTimes[first]);

                    continue;
                }

                // (a first look: where it is - as the hit check saw it a frame ago, or as it is kept - and how far it can fly within the time at the most; only what can come near is looked at all the way)
                if (!infinite)
                {
                    ulong bit = 1UL << b;
                    Vector2 a;

                    if (seen != null && (seen.Known & bit) != 0 && Math.Abs(seen.Time - autoOwn[first]) < 100)
                    {
                        if ((seen.Alive & bit) == 0)
                            continue; // (a bouncing bullet that has touched its last wall)

                        a = seen.Position[b];
                    }
                    else if (track != null)
                    {
                        if (!autoBullet(track, emitter, firstSlot, b, out a))
                            continue;
                    }
                    else
                    {
                        emitter.Evaluate(b, autoOwn[first], out a, out _, out bool aliveA);

                        if (!aliveA)
                            continue;
                    }

                    // (as fast as it flies, ramped up, and more for what falls or what the ops of the bosses steer)
                    float fastest = emitter.SpeedOf(b) * Math.Max(emitter.SpeedRampTo, 1f) * (emitter.Falling ? 3f : 1.25f);
                    float flown = fastest * (float)ownSpan + auto_bend;

                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || Vector2.Distance(a, centre) > radius + reach + flown)
                        continue;
                }

                int at = autoNewTrack(reach);
                bool any = false;
                AutoTracksMade++;

                if (track != null)
                {
                    // (from what is kept: every moment of the grid is worked out once; this frame's own moment is between the two round it)
                    for (int i = 1; i <= auto_samples; i++)
                    {
                        bool hurts = autoBullet(track, emitter, autoSlot(track, emitter, autoGrid0 + i), b, out Vector2 position);
                        autoTrack[at + i] = hurts ? position : not_there;
                        any |= hurts;
                    }

                    if (double.IsNaN(autoOwn[0]))
                        autoTrack[at] = not_there;
                    else if (autoBullet(track, emitter, nowSlot, b, out Vector2 before) && !float.IsNaN(autoTrack[at + 1].X) && (autoTrack[at + 1] - before).LengthSquared < auto_jump * auto_jump)
                    {
                        autoTrack[at] = Vector2.Lerp(before, autoTrack[at + 1], between);
                        any = true;
                    }
                    else
                    {
                        emitter.Evaluate(b, autoOwn[0], out Vector2 position, out _, out bool alive, out BulletLook? look);
                        bool hurts = alive && look?.Harmless != true && float.IsFinite(position.X) && float.IsFinite(position.Y);
                        autoTrack[at] = hurts ? position : not_there;
                        any |= hurts;
                    }
                }
                else
                {
                    // (not kept - a ring not fired yet, whose way is only known once it is: worked out at every second sample; a sample between two of them is halfway, unless something happens there - it
                    // jumps, or is there at only one of them - and the last is worked out)
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int i = pass; i <= auto_samples; i += 2)
                        {
                            if (double.IsNaN(autoOwn[i]))
                            {
                                autoTrack[at + i] = not_there;
                                continue;
                            }

                            if (pass == 1 && i < auto_samples)
                            {
                                Vector2 before = autoTrack[at + i - 1], after = autoTrack[at + i + 1];

                                if (!float.IsNaN(before.X) && !float.IsNaN(after.X) && (after - before).LengthSquared < auto_jump * auto_jump)
                                {
                                    float share = (float)((autoTimes[i] - autoTimes[i - 1]) / Math.Max(autoTimes[i + 1] - autoTimes[i - 1], 1e-6));
                                    autoTrack[at + i] = Vector2.Lerp(before, after, share);
                                    continue;
                                }
                            }

                            emitter.Evaluate(b, autoOwn[i], out Vector2 position, out _, out bool alive, out BulletLook? look);
                            bool hurts = alive && look?.Harmless != true && float.IsFinite(position.X) && float.IsFinite(position.Y);
                            autoTrack[at + i] = hurts ? position : not_there;
                            any |= hurts;
                        }
                    }
                }

                if (!any)
                    autoTracks--;
            }
        }

        // ---- what is kept of the rings from frame to frame ----
        // A fired ring's bullets fly where their own flight puts them (their turns at runtime aside): where each is at the moments of a grid every AUTO_SAMPLE_MS is worked out once and kept while the
        // moment is within the time looked ahead, so a frame works out only the newest moment (it was all of them, every frame: what froze the game with many bullets). A ring whose bullet is turned
        // (a deflection) is worked out again, and so is a moment kept longer than auto_track_keep_ms, in case anything else changed.

        private sealed class RingTrack
        {
            public readonly long[] Grid = new long[auto_track_slots];
            public readonly double[] Own = new double[auto_track_slots];
            public readonly double[] Made = new double[auto_track_slots];
            public readonly ulong[] Known = new ulong[auto_track_slots];
            public readonly ulong[] Hurts = new ulong[auto_track_slots];
            public readonly Vector2[] Positions = new Vector2[auto_track_slots * 64];
            public int Deflections;
            public double Used;

            public void Clear() => Array.Fill(Grid, long.MinValue);
        }

        private static readonly int auto_track_slots = auto_samples + 3;

        private const double auto_track_keep_ms = 400;

        private readonly Dictionary<int, RingTrack> autoRingTracks = new Dictionary<int, RingTrack>();
        private readonly Stack<RingTrack> autoRingTrackPool = new Stack<RingTrack>();
        private readonly List<int> autoStaleTracks = new List<int>();

        /// <summary>The grid moment at or before this frame's (its number: moments every <see cref="DanmakusuBalance.AUTO_SAMPLE_MS"/>).</summary>
        private long autoGrid0;

        /// <summary>The samples: this frame's moment, then the moments of the grid after it (the same moments frame after frame, so that what is worked out at them is kept).</summary>
        private void autoSetTimes(double now)
        {
            autoGrid0 = (long)Math.Floor(now / DanmakusuBalance.AUTO_SAMPLE_MS);
            autoTimes[0] = now;

            for (int i = 1; i <= auto_samples; i++)
                autoTimes[i] = (autoGrid0 + i) * DanmakusuBalance.AUTO_SAMPLE_MS;
        }

        private RingTrack autoRingTrack(int index, BulletEmitter emitter)
        {
            int deflections = emitter.Deflections?.Count ?? 0;

            if (!autoRingTracks.TryGetValue(index, out var track))
            {
                track = autoRingTrackPool.Count > 0 ? autoRingTrackPool.Pop() : new RingTrack();
                track.Clear();
                track.Deflections = deflections;
                autoRingTracks[index] = track;
            }
            else if (track.Deflections != deflections)
            {
                track.Clear();
                track.Deflections = deflections;
            }

            track.Used = autoTimes[0];
            return track;
        }

        /// <summary>The slot of a grid moment in a ring's track: its clock there worked out, if it was not yet (or long ago).</summary>
        private int autoSlot(RingTrack track, BulletEmitter emitter, long grid)
        {
            int slot = (int)(((grid % auto_track_slots) + auto_track_slots) % auto_track_slots);

            if (track.Grid[slot] == grid && autoTimes[0] - track.Made[slot] <= auto_track_keep_ms)
                return slot;

            track.Grid[slot] = grid;
            track.Made[slot] = autoTimes[0];
            track.Known[slot] = track.Hurts[slot] = 0;

            double t = grid * DanmakusuBalance.AUTO_SAMPLE_MS;
            double own = double.NaN;

            if (t >= emitter.Time && !emitter.RemovedAt(t))
            {
                own = emitter.WithoutFrozenTime(effects.Effective(emitter, t));

                if (own > emitter.Time + emitter.Lifetime)
                    own = double.NaN;
            }

            track.Own[slot] = own;
            return slot;
        }

        /// <summary>Where a bullet of the ring is at a slot's moment (worked out once), and whether it is there and hurts.</summary>
        private bool autoBullet(RingTrack track, BulletEmitter emitter, int slot, int bullet, out Vector2 position)
        {
            ulong bit = 1UL << bullet;

            if ((track.Known[slot] & bit) == 0)
            {
                track.Known[slot] |= bit;

                if (!double.IsNaN(track.Own[slot]))
                {
                    emitter.Evaluate(bullet, track.Own[slot], out Vector2 at, out _, out bool alive, out BulletLook? look);

                    if (alive && look?.Harmless != true && float.IsFinite(at.X) && float.IsFinite(at.Y))
                    {
                        track.Positions[slot * 64 + bullet] = at;
                        track.Hurts[slot] |= bit;
                    }
                }
            }

            position = track.Positions[slot * 64 + bullet];
            return (track.Hurts[slot] & bit) != 0;
        }

        /// <summary>(a new frame) the rings not looked at for a while are forgotten; when time went back, all of them.</summary>
        private void autoForgetTracks(double now, bool all)
        {
            autoStaleTracks.Clear();

            foreach (var (index, track) in autoRingTracks)
            {
                if (all || now - track.Used > 200)
                    autoStaleTracks.Add(index);
            }

            foreach (int index in autoStaleTracks)
            {
                autoRingTrackPool.Push(autoRingTracks[index]);
                autoRingTracks.Remove(index);
            }
        }

        private void autoStage(Vector2 centre, float radius)
        {
            if (stageActs.Count == 0)
                return;

            double t0 = autoTimes[0], tEnd = autoTimes[auto_samples];
            float sukuna = sukunaLevel > 0f ? DanmakusuBalance.SUKUNA_MAX_SIZE : 1f;
            bool infinite = float.IsPositiveInfinity(radius);

            foreach (var act in stageActs)
            {
                if (act.Bullets.Count == 0 || tEnd < act.FirstT0)
                    continue;

                // (what the hit check has passed as gone for good is gone for good)
                int from = act.HitFromTime <= t0 ? Math.Clamp(act.HitFrom, 0, act.Bullets.Count) : 0;

                for (int i = from; i < act.Bullets.Count; i++)
                {
                    var bullet = act.Bullets[i];

                    if (bullet.Slider || bullet.Harmless || bullet.T0 > tEnd)
                        continue;

                    bool traced = stageBulletTraced(bullet);
                    bool seen = traced && i < act.Traced.Length && !double.IsNaN(act.Traced[i].Time);

                    if (seen ? !act.Traced[i].Alive : bullet.Until < t0)
                        continue;

                    if (stageConsumed.Contains(act.Id * 65536 + i))
                        continue;

                    double solid = Math.Max(bullet.T0, bullet.GhostUntil); // (a see-through one only hurts from when it is solid)
                    float grown = Math.Max(stageBulletScale(bullet, Math.Max(t0, bullet.T0)), stageBulletScale(bullet, tEnd));

                    if (grown < 0.02f)
                        continue;

                    float reach = bullet.Hit * grown * sukuna + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN;
                    TracedAt kept = seen ? act.Traced[i] : default;

                    Vector2 placeAt(double t) => seen ? kept.Position + kept.Velocity * (float)(t - kept.Time) : stageBulletPosition(bullet, stageClock(bullet, t));

                    double firstTime = Math.Max(t0, solid);

                    if (firstTime > tEnd)
                        continue;

                    double lastTime = seen ? tEnd : Math.Min(tEnd, bullet.Until);

                    if (lastTime < firstTime)
                        continue;

                    if (!infinite)
                    {
                        Vector2 a = placeAt(firstTime), m = placeAt((firstTime + lastTime) / 2), z = placeAt(lastTime);
                        float near = radius + reach + auto_bend;

                        if (distanceToSegment(centre, a, m) > near && distanceToSegment(centre, m, z) > near)
                            continue;
                    }

                    int at = autoNewTrack(reach);

                    // (at every second sample and the last; a sample between is halfway, unless it is there at only one of them or jumps - it wraps, it is traced)
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int s = pass; s <= auto_samples; s += 2)
                        {
                            double t = autoTimes[s];

                            if (pass == 1 && s < auto_samples)
                            {
                                Vector2 before = autoTrack[at + s - 1], after = autoTrack[at + s + 1];

                                if (!float.IsNaN(before.X) && !float.IsNaN(after.X) && (after - before).LengthSquared < auto_jump * auto_jump)
                                {
                                    float share = (float)((t - autoTimes[s - 1]) / Math.Max(autoTimes[s + 1] - autoTimes[s - 1], 1e-6));
                                    autoTrack[at + s] = Vector2.Lerp(before, after, share);
                                    continue;
                                }
                            }

                            bool there = t >= solid && (seen || t <= bullet.Until);
                            Vector2 position = there ? placeAt(t) : not_there;
                            autoTrack[at + s] = float.IsFinite(position.X) && float.IsFinite(position.Y) ? position : not_there;
                        }
                    }
                }
            }
        }

        // ---- the beams and the moon's stars: at the moments of a grid, kept from frame to frame ----
        // (a beam's shape is costly to work out: it is worked out for moments every auto_beam_grid_ms - the same moments frame after frame, so that what was worked out is used again, and worked out
        // again when it is auto_grid_keep_ms old, as what is aimed or steered may have changed. A sample takes the nearest of those moments, the beam as much wider as its slices fly in half a step.
        // The moon's stars the same, on a finer grid)

        private const double auto_beam_grid_ms = 100;
        private const double auto_star_grid_ms = 50;
        private const double auto_grid_keep_ms = 100;

        /// <summary>(a beam's shape is kept longer: one cut or blown up meanwhile is only dodged when it need not be)</summary>
        private const double auto_beam_keep_ms = 300;

        private sealed class AutoShape<T>
        {
            public double Made;
            public float MinX, MinY, MaxX, MaxY;
            public readonly List<T> Parts = new List<T>();
        }

        // (for every sample, which of this frame's moments its beams / stars are taken at, and those moments as numbers of the grid)
        private readonly int[] autoBeamSlot = new int[auto_stride];
        private readonly int[] autoStarSlot = new int[auto_stride];
        private readonly List<long> autoBeamMoments = new List<long>();
        private readonly List<long> autoStarMoments = new List<long>();

        private readonly Dictionary<(int Laser, long Moment), AutoShape<AutoBeam>> autoLaserShapes = new Dictionary<(int, long), AutoShape<AutoBeam>>();
        private readonly Dictionary<long, AutoShape<AutoPoint>> autoStarShapes = new Dictionary<long, AutoShape<AutoPoint>>();
        private readonly Stack<AutoShape<AutoBeam>> autoLaserShapePool = new Stack<AutoShape<AutoBeam>>();
        private readonly List<(int, long)> autoStale = new List<(int, long)>();
        private double autoGridFrame = double.NaN;

        private static void autoSlots(double[] times, double grid, int[] slotOf, List<long> moments)
        {
            moments.Clear();

            for (int i = 0; i < times.Length; i++)
            {
                long moment = (long)Math.Round(times[i] / grid);
                int slot = moments.IndexOf(moment);

                if (slot < 0)
                {
                    slot = moments.Count;
                    moments.Add(moment);
                }

                slotOf[i] = slot;
            }
        }

        /// <summary>A new frame: which moments of the grids its samples take, and what is too old to keep.</summary>
        private void autoNewFrame(double now)
        {
            if (autoGridFrame == now)
                return;

            // (time went back: nothing is kept)
            if (now < autoGridFrame)
            {
                foreach (var shape in autoLaserShapes.Values)
                    autoLaserShapePool.Push(shape);

                autoLaserShapes.Clear();
                autoStarShapes.Clear();
            }

            autoGridFrame = now;
            autoSetTimes(now);

            autoSlots(autoTimes, auto_beam_grid_ms, autoBeamSlot, autoBeamMoments);
            autoSlots(autoTimes, auto_star_grid_ms, autoStarSlot, autoStarMoments);

            autoStale.Clear();

            foreach (var (key, shape) in autoLaserShapes)
            {
                if (key.Moment * auto_beam_grid_ms < now - auto_beam_grid_ms || now - shape.Made >= auto_beam_keep_ms)
                    autoStale.Add(key);
            }

            foreach (var key in autoStale)
            {
                autoLaserShapePool.Push(autoLaserShapes[key]);
                autoLaserShapes.Remove(key);
            }

            foreach (long moment in autoStarShapes.Keys.ToArray())
            {
                if (moment * auto_star_grid_ms < now - auto_star_grid_ms || now - autoStarShapes[moment].Made >= auto_grid_keep_ms)
                    autoStarShapes.Remove(moment);
            }
        }

        private double[] autoLaserPassedUntil = Array.Empty<double>();
        private Vector2[] autoLaserPassedAt = Array.Empty<Vector2>();
        private float[] autoLaserPassedRoom = Array.Empty<float>();

        private void autoLasers(Vector2 centre, float radius)
        {
            if (lasers.Length == 0)
                return;

            if (autoLaserPassedUntil.Length != lasers.Length)
            {
                autoLaserPassedUntil = new double[lasers.Length];
                autoLaserPassedAt = new Vector2[lasers.Length];
                autoLaserPassedRoom = new float[lasers.Length];
            }

            bool infinite = float.IsPositiveInfinity(radius);
            double t0 = autoTimes[0], tEnd = autoTimes[auto_samples];
            int lo = laserLowerBound(t0 - auto_beam_grid_ms - maxLaserSpan);
            int hi = laserLowerBound(tEnd + auto_beam_grid_ms);

            for (int l = lo; l < hi; l++)
            {
                var laser = lasers[l];

                if (laser.Finished || t0 > laser.EndTime + laser.Lifetime + auto_beam_grid_ms || t0 >= laser.VanishAt)
                    continue;

                // (passed by a moment ago, while the character has not gone far: passed by again)
                if (!infinite && t0 < autoLaserPassedUntil[l] && Vector2.Distance(centre, autoLaserPassedAt[l]) + radius <= autoLaserPassedRoom[l])
                    continue;

                // (the box round all of it at all its moments, as wide as it gets: when the character is far from it, it is passed by for a while)
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

                for (int slot = 0; slot < autoBeamMoments.Count; slot++)
                {
                    var shape = autoLaserShape(l, laser, autoBeamMoments[slot]);
                    minX = Math.Min(minX, shape.MinX);
                    minY = Math.Min(minY, shape.MinY);
                    maxX = Math.Max(maxX, shape.MaxX);
                    maxY = Math.Max(maxY, shape.MaxY);
                }

                if (!infinite)
                {
                    float room = radius + auto_pass_room;

                    if (float.IsPositiveInfinity(minX) || centre.X < minX - room || centre.X > maxX + room || centre.Y < minY - room || centre.Y > maxY + room)
                    {
                        autoLaserPassedUntil[l] = t0 + Math.Min(auto_pass_ms, auto_beam_grid_ms);
                        autoLaserPassedAt[l] = centre;
                        autoLaserPassedRoom[l] = room;
                        continue;
                    }
                }

                for (int slot = 0; slot < autoBeamMoments.Count; slot++)
                {
                    var list = autoBeams[slot];

                    foreach (var segment in autoLaserShape(l, laser, autoBeamMoments[slot]).Parts)
                    {
                        if (infinite || distanceToSegment(centre, segment.A, segment.B) <= radius + Math.Max(segment.WidthA, segment.WidthB))
                            list.Add(segment);
                    }
                }
            }
        }

        /// <summary>A beam at a moment of the grid: its pieces, as wide as they hurt and more by as far as its slices fly in half a step, and the box round them (worked out once, then kept).</summary>
        private AutoShape<AutoBeam> autoLaserShape(int l, LaserEmitter laser, long moment)
        {
            if (autoLaserShapes.TryGetValue((l, moment), out var shape))
                return shape;

            shape = autoLaserShapePool.Count > 0 ? autoLaserShapePool.Pop() : new AutoShape<AutoBeam>();
            shape.Parts.Clear();
            shape.Made = autoGridFrame;
            shape.MinX = shape.MinY = float.PositiveInfinity;
            shape.MaxX = shape.MaxY = float.NegativeInfinity;
            autoLaserShapes[(l, moment)] = shape;

            double t = moment * auto_beam_grid_ms;
            float moved = laser.Speed * (float)(auto_beam_grid_ms / 2);

            if (t < laser.StartTime || t > laser.EndTime + laser.Lifetime || t >= laser.VanishAt)
                return shape;

            for (int beam = 0; beam < laser.BeamCount; beam++)
            {
                if (laser.BeamFinished[beam])
                    continue;

                int n = laser.GetPoints(t, autoLaserPoints, beam, autoLaserWidths, null, autoLaserLooks);

                for (int p = 0; p < n; p++)
                {
                    int q = Math.Min(p + 1, n - 1);

                    if (n > 1 && q == p)
                        break;

                    Vector2 a = autoLaserPoints[p], b = autoLaserPoints[q];

                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(b.X) || !float.IsFinite(b.Y))
                        continue;

                    var lookA = autoLaserLooks[p];
                    var lookB = autoLaserLooks[q];

                    // (what does not hurt: a sliding slice of Reisen's, a stretched piece half faded out, a cut at a portal)
                    if (lookA?.Harmless == true && lookB?.Harmless == true)
                        continue;

                    if ((lookA?.StretchVisible ?? 1f) <= 0.5f && (lookB?.StretchVisible ?? 1f) <= 0.5f)
                        continue;

                    if (portalOf(lookA) != portalOf(lookB))
                        continue;

                    float widthA = autoLaserWidths[p] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN + moved;
                    float widthB = autoLaserWidths[q] * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN + moved;
                    float wide = Math.Max(widthA, widthB);

                    shape.Parts.Add(new AutoBeam { A = a, B = b, WidthA = widthA, WidthB = widthB });
                    shape.MinX = Math.Min(shape.MinX, Math.Min(a.X, b.X) - wide);
                    shape.MinY = Math.Min(shape.MinY, Math.Min(a.Y, b.Y) - wide);
                    shape.MaxX = Math.Max(shape.MaxX, Math.Max(a.X, b.X) + wide);
                    shape.MaxY = Math.Max(shape.MaxY, Math.Max(a.Y, b.Y) + wide);

                    if (n == 1)
                        break;
                }
            }

            return shape;
        }

        private void autoStream(Vector2 centre, float radius)
        {
            if (moons.Length == 0)
                return;

            bool infinite = float.IsPositiveInfinity(radius);

            for (int slot = 0; slot < autoStarMoments.Count; slot++)
            {
                long moment = autoStarMoments[slot];

                if (!autoStarShapes.TryGetValue(moment, out var shape))
                {
                    shape = new AutoShape<AutoPoint> { Made = autoGridFrame };
                    autoStarShapes[moment] = shape;
                    buildStreamStars(moment * auto_star_grid_ms);

                    for (int i = 0; i < streamCount; i++)
                    {
                        var star = streamStars[i];

                        if (star.Look is { Harmless: true })
                            continue;

                        float reach = star.Radius * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN + star.Speed * (float)(auto_star_grid_ms / 2);
                        shape.Parts.Add(new AutoPoint { Position = star.Position, Reach = reach });
                    }
                }

                foreach (var point in shape.Parts)
                {
                    if (infinite || Vector2.Distance(point.Position, centre) <= radius + point.Reach)
                        autoPoints[slot].Add(point);
                }
            }
        }

        /// <summary>Marks the places of the teleport grid that something gathered passes within the time (whenever it does).</summary>
        private void autoMarkGrid(Vector2 low, Vector2 high)
        {
            float cell = DanmakusuBalance.AUTO_GRID;
            autoColumns = Math.Max((int)Math.Ceiling((high.X - low.X) / cell), 1);
            autoRows = Math.Max((int)Math.Ceiling((high.Y - low.Y) / cell), 1);

            if (autoMarked.Length < autoColumns * autoRows)
                autoMarked = new bool[autoColumns * autoRows];

            Array.Clear(autoMarked, 0, autoColumns * autoRows);

            void mark(Vector2 a, Vector2 b, float reach)
            {
                int x0 = Math.Max((int)Math.Floor((Math.Min(a.X, b.X) - reach - low.X) / cell - 0.5f), 0);
                int x1 = Math.Min((int)Math.Ceiling((Math.Max(a.X, b.X) + reach - low.X) / cell - 0.5f), autoColumns - 1);
                int y0 = Math.Max((int)Math.Floor((Math.Min(a.Y, b.Y) - reach - low.Y) / cell - 0.5f), 0);
                int y1 = Math.Min((int)Math.Ceiling((Math.Max(a.Y, b.Y) + reach - low.Y) / cell - 0.5f), autoRows - 1);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (autoMarked[y * autoColumns + x])
                            continue;

                        Vector2 place = new Vector2(low.X + (x + 0.5f) * cell, low.Y + (y + 0.5f) * cell);

                        if (distanceToSegment(place, a, b) <= reach)
                            autoMarked[y * autoColumns + x] = true;
                    }
                }
            }

            for (int j = 0; j < autoTracks; j++)
            {
                Vector2 before = not_there;

                for (int i = 0; i <= auto_samples; i++)
                {
                    Vector2 here = autoTrack[j * auto_stride + i];

                    if (float.IsNaN(here.X))
                    {
                        before = not_there;
                        continue;
                    }

                    bool joined = !float.IsNaN(before.X) && (here - before).LengthSquared < auto_jump * auto_jump;
                    mark(joined ? before : here, here, autoReach[j]);
                    before = here;
                }
            }

            for (int slot = 0; slot < autoBeamMoments.Count; slot++)
            {
                foreach (var beam in autoBeams[slot])
                    mark(beam.A, beam.B, Math.Max(beam.WidthA, beam.WidthB));
            }

            for (int slot = 0; slot < autoStarMoments.Count; slot++)
            {
                foreach (var point in autoPoints[slot])
                    mark(point.Position, point.Position, point.Reach);
            }
        }
    }
}
