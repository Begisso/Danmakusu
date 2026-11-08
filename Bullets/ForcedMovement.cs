using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The star rating by how much a map makes the player move. The map is planned without bosses, and a lazy player plays it: they stand still until something would hit them within a moment, then walk the shortest way
    /// to a place that stays safe, at the keyboard's speed, and if no walk gets them out in time they teleport (what the freeze keys and Space are for) to the nearest safe place on the playfield. Rings and beams that aim
    /// at the player aim at this player, so what is aimed is what makes them move. How far they walk, how often they have to start a new dodge and how often they have to teleport make the load of a moment, about
    /// the last second of it counting; the hardest moments of the map's parts, from the hardest down and each counting a little less (as osu!'s own rating does), make its value, and the stars grow with it.
    /// Nothing here is random, so a map always gets the same stars.
    /// </summary>
    public static class ForcedMovement
    {
        public sealed class Result
        {
            public double Stars;

            /// <summary>The weighted parts of the load.</summary>
            public double Value;

            /// <summary>The hardest moment and when it is.</summary>
            public double Peak;

            public double PeakTime;

            /// <summary>On average while something is on the playfield: playfield diagonals walked per second, dodges started per second.</summary>
            public double MovedPerSecond;

            public double DodgesPerSecond;

            /// <summary>In the whole map: dodges started, teleports, and moments when there was no safe place at all.</summary>
            public int Dodges, Teleports, Trapped;

            public int Rings, Bullets, Beams;
        }

        /// <summary>A bullet, or one straight stretch of a bouncing bullet's flight.</summary>
        private struct Flier
        {
            public double T0, Until;
            public Vector2 P0, V, A;
            public float Reach;

            public readonly Vector2 At(double t)
            {
                float dt = (float)(t - T0);
                return P0 + V * dt + A * (0.5f * dt * dt);
            }
        }

        /// <summary>The piece of a beam between two slices: each end flies its own straight line from where it was fired.</summary>
        private struct Stretch
        {
            public double T1, T2, Until;
            public Vector2 P1, V1, P2, V2;
            public float Reach;

            public readonly void At(double t, out Vector2 a, out Vector2 b)
            {
                a = P1 + V1 * (float)(t - T1);
                b = P2 + V2 * (float)(t - T2);
            }
        }

        private sealed class Spinner
        {
            public LaserEmitter Laser = null!;
            public float Angle;
            public double Last;
            public float Turn; // (radians per ms: the beam that is nearest to the player turns the short way towards them)
        }

        private static readonly Vector2 field = OsuPlayfield.BASE_SIZE;
        private static readonly Vector2 middle = OsuPlayfield.BASE_SIZE / 2;
        private static readonly float diagonal = OsuPlayfield.BASE_SIZE.Length;

        /// <summary>
        /// Rates the (converted) map, played <paramref name="clockRate"/> times as fast. The player is the same player at any speed, so everything they do is measured in real time. Faster, it is never rated below what it
        /// is at the normal speed times the rate to <see cref="DanmakusuBalance.FORCED_RATE_POWER"/>, slower never above (see there): the map is played a second time at the normal speed for it.
        /// </summary>
        public static Result Rate(IBeatmap beatmap, double clockRate = 1)
        {
            var rings = BulletPlanner.Plan(beatmap, out var lasers, out _, noBoss: true);
            return Rate(rings, lasers, clockRate);
        }

        /// <summary>The same for what a map shoots, planned already without bosses (the rings are only read; the beams' directions are set as they are fired).</summary>
        public static Result Rate(BulletEmitter[] rings, LaserEmitter[] lasers, double clockRate = 1)
        {
            double rate = Math.Max(clockRate, 0.01);
            var result = new Simulation(rings, lasers, rate).Run();

            if (Math.Abs(rate - 1) < 1e-6)
                return result;

            double bound = new Simulation(rings, lasers, 1).Run().Value * Math.Pow(rate, DanmakusuBalance.FORCED_RATE_POWER);

            if (rate > 1 ? result.Value < bound : result.Value > bound)
            {
                result.Value = bound;
                result.Stars = DanmakusuBalance.FORCED_STAR_SCALE * Math.Pow(bound, DanmakusuBalance.FORCED_STAR_POWER);
            }

            return result;
        }

        private sealed class Simulation
        {
            private readonly BulletEmitter[] rings;
            private readonly LaserEmitter[] lasers;
            private readonly double rate;

            // (in the map's own time)
            private readonly double step, sample, horizon;
            private readonly float walkSpeed;
            private readonly Vector2[] directions;
            private readonly float[] distances;
            private readonly Vector2[] grid;

            private Flier[] fliers = new Flier[4096];
            private Stretch[] stretches = new Stretch[1024];
            private int flierCount, stretchCount;
            private readonly List<int> nearFliers = new List<int>(512), nearStretches = new List<int>(128);
            private readonly List<Spinner> spinners = new List<Spinner>();
            private int bullets;

            private Vector2 player = middle;
            private Vector2 target = middle;
            private bool walking;
            private Vector2 lastWay;

            public Simulation(BulletEmitter[] rings, LaserEmitter[] lasers, double rate)
            {
                this.rings = rings;
                this.lasers = lasers;
                this.rate = rate;

                step = DanmakusuBalance.FORCED_STEP_MS * rate;
                sample = DanmakusuBalance.FORCED_SAMPLE_MS * rate;
                horizon = DanmakusuBalance.FORCED_HORIZON_MS * rate;
                walkSpeed = DanmakusuBalance.KEYBOARD_SPEED / (float)rate;

                directions = new Vector2[DanmakusuBalance.FORCED_WAYS];

                for (int i = 0; i < directions.Length; i++)
                {
                    float angle = i * MathF.Tau / directions.Length;
                    directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                // (from a small step aside up to as far as they can walk before the moment is over)
                float furthest = walkSpeed * (float)horizon;
                distances = new[] { DanmakusuBalance.FORCED_SMALLEST_DODGE, furthest * 0.25f, furthest * 0.5f, furthest * 0.75f, furthest };
                Array.Sort(distances);

                grid = new Vector2[DanmakusuBalance.FORCED_GRID_X * DanmakusuBalance.FORCED_GRID_Y];

                for (int y = 0; y < DanmakusuBalance.FORCED_GRID_Y; y++)
                {
                    for (int x = 0; x < DanmakusuBalance.FORCED_GRID_X; x++)
                        grid[y * DanmakusuBalance.FORCED_GRID_X + x] = new Vector2(field.X * (x + 0.5f) / DanmakusuBalance.FORCED_GRID_X, field.Y * (y + 0.5f) / DanmakusuBalance.FORCED_GRID_Y);
                }
            }

            public Result Run()
            {
                double last = 0, first = double.PositiveInfinity;

                foreach (var ring in rings)
                {
                    last = Math.Max(last, ring.Time + ring.Lifetime);
                    first = Math.Min(first, ring.Time);
                }

                foreach (var laser in lasers)
                {
                    last = Math.Max(last, laser.EndTime + laser.Lifetime);
                    first = Math.Min(first, laser.StartTime);
                }

                var result = new Result { Rings = rings.Length, Beams = lasers.Length };

                if (double.IsInfinity(first))
                    return result;

                // (walked px, dodges started and teleports of every step)
                int steps = (int)Math.Ceiling((last - first) / step) + 2;
                var load = new double[steps];
                int nextRing = 0, nextLaser = 0;
                double walked = 0;
                int busy = 0;

                for (int s = 0; s < steps; s++)
                {
                    double t = first + s * step;

                    while (nextRing < rings.Length && rings[nextRing].Time <= t)
                        fire(rings[nextRing++]);

                    while (nextLaser < lasers.Length && lasers[nextLaser].StartTime <= t)
                        fireBeam(lasers[nextLaser++], t);

                    growSpinners(t);
                    gatherNear(t);

                    if (flierCount + stretchCount > 0)
                        busy++;

                    decide(t, out float moved, out int dodges, out int teleports, out int trapped);

                    walked += moved;
                    result.Dodges += dodges;
                    result.Teleports += teleports;
                    result.Trapped += trapped;

                    // (per real second)
                    double perSecond = 1000.0 / DanmakusuBalance.FORCED_STEP_MS;
                    load[s] = moved / diagonal * perSecond * DanmakusuBalance.FORCED_WALK_WEIGHT
                              + dodges * perSecond * DanmakusuBalance.FORCED_DODGE_WEIGHT
                              + (teleports + trapped) * perSecond * DanmakusuBalance.FORCED_TELEPORT_WEIGHT;
                }

                // (the load at every step: about the last second, the older steps counting less)
                double memory = Math.Exp(-DanmakusuBalance.FORCED_STEP_MS / DanmakusuBalance.FORCED_MEMORY_MS);
                int perSection = Math.Max((int)Math.Round(DanmakusuBalance.FORCED_SECTION_MS / DanmakusuBalance.FORCED_STEP_MS), 1);
                var peaks = new List<double>(steps / perSection + 1);
                double held = 0;

                for (int s = 0; s < steps; s++)
                {
                    held = held * memory + load[s] * (1 - memory);

                    if (held > result.Peak)
                    {
                        result.Peak = held;
                        result.PeakTime = first + s * step;
                    }

                    if (s % perSection == 0)
                        peaks.Add(held);
                    else
                        peaks[^1] = Math.Max(peaks[^1], held);
                }

                peaks.Sort((a, b) => b.CompareTo(a));
                double value = 0, weight = 1;

                foreach (double p in peaks)
                {
                    value += p * weight;
                    weight *= DanmakusuBalance.FORCED_SECTION_WEIGHT;
                }

                double seconds = Math.Max(busy * DanmakusuBalance.FORCED_STEP_MS / 1000.0, 1e-3);

                result.Value = value;
                result.Stars = DanmakusuBalance.FORCED_STAR_SCALE * Math.Pow(value, DanmakusuBalance.FORCED_STAR_POWER);
                result.MovedPerSecond = walked / diagonal / seconds;
                result.DodgesPerSecond = result.Dodges / seconds;
                result.Bullets = bullets;
                return result;
            }

            // ---- what is fired ----

            private void addFlier(in Flier flier)
            {
                if (flierCount == fliers.Length)
                    Array.Resize(ref fliers, fliers.Length * 2);

                fliers[flierCount++] = flier;
            }

            private void addStretch(in Stretch stretch)
            {
                if (stretchCount == stretches.Length)
                    Array.Resize(ref stretches, stretches.Length * 2);

                stretches[stretchCount++] = stretch;
            }

            private void fire(BulletEmitter ring)
            {
                if (ring.Count == 0 || ring.Speed <= 0)
                    return;

                float reach = Math.Max(ring.HitRadius, 0f) + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.FORCED_MARGIN;
                Vector2 turn = new Vector2(1f, 0f);
                Vector2 toPlayer = player - ring.Origin;

                // (as the field does it: the first bullet points at the player, turned by the ring's aim offset)
                if (ring.AimAtPlayer && toPlayer.LengthSquared > 1e-6f)
                {
                    Vector2 firstWay = ring.Directions[0];
                    float delta = MathF.Atan2(toPlayer.Y, toPlayer.X) - MathF.Atan2(firstWay.Y, firstWay.X) + ring.AimOffset;
                    turn = new Vector2(MathF.Cos(delta), MathF.Sin(delta));
                }

                Vector2 pull = ring.Falling && ring.Gravity > 0f ? (toPlayer.LengthSquared > 1e-6f ? Vector2.Normalize(toPlayer) : Vector2.UnitY) * ring.Gravity : Vector2.Zero;

                for (int i = 0; i < ring.Count; i++)
                {
                    float scale = ring.RadialScales != null && i < ring.RadialScales.Length ? ring.RadialScales[i] : 1f;
                    float speed = ring.Speed * scale;

                    if (speed <= 0f)
                        continue;

                    Vector2 origin = ring.Origins != null && i < ring.Origins.Length ? ring.Origins[i] : ring.Origin;
                    Vector2 way = rotate(ring.Directions[i], turn);

                    // (what homes turns towards the player and then flies straight: it is taken as flying straight at them from the start)
                    if (ring.Homing)
                    {
                        Vector2 at = player - origin;

                        if (at.LengthSquared > 1e-6f)
                            way = Vector2.Normalize(at);
                    }

                    bullets++;

                    var flier = new Flier
                    {
                        T0 = ring.Time,
                        Until = ring.Time + ring.Lifetime,
                        P0 = origin + way * (ring.SpawnRadius * scale),
                        V = way * speed,
                        A = pull,
                        Reach = reach,
                    };

                    if (ring.Ricochet)
                        addBouncing(flier, DanmakusuBalance.MAX_RICOCHETS + ring.ExtraRicochets);
                    else
                        addFlier(flier);
                }
            }

            /// <summary>A bullet that ricochets flies straight from wall to wall: every stretch is a flier of its own, made when it is fired.</summary>
            private void addBouncing(Flier flier, int bounces)
            {
                for (int b = 0; b <= bounces; b++)
                {
                    double until = flier.Until;
                    float toWall = float.PositiveInfinity;
                    bool acrossX = false;

                    // (only what flies straight: a falling bullet does not ricochet)
                    if (flier.V.X != 0f)
                    {
                        float x = ((flier.V.X > 0f ? field.X : 0f) - flier.P0.X) / flier.V.X;

                        if (x > 0f && x < toWall)
                        {
                            toWall = x;
                            acrossX = true;
                        }
                    }

                    if (flier.V.Y != 0f)
                    {
                        float y = ((flier.V.Y > 0f ? field.Y : 0f) - flier.P0.Y) / flier.V.Y;

                        if (y > 0f && y < toWall)
                        {
                            toWall = y;
                            acrossX = false;
                        }
                    }

                    if (float.IsInfinity(toWall) || flier.T0 + toWall >= until || b == bounces)
                    {
                        addFlier(flier);
                        return;
                    }

                    var piece = flier;
                    piece.Until = flier.T0 + toWall;
                    addFlier(piece);

                    flier.P0 += flier.V * toWall;
                    flier.T0 = piece.Until;
                    flier.V = acrossX ? new Vector2(-flier.V.X, flier.V.Y) : new Vector2(flier.V.X, -flier.V.Y);
                }
            }

            private void fireBeam(LaserEmitter laser, double t)
            {
                if (laser.Speed <= 0f || laser.BeamCount <= 0)
                    return;

                Vector2 toPlayer = player - laser.Origin;
                float first = (toPlayer.LengthSquared > 1e-6f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : 0f) + laser.AimOffset;

                for (int b = 0; b < laser.BeamCount; b++)
                {
                    float angle = first + b * MathF.Tau / laser.BeamCount;
                    laser.Directions[b] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                if (laser.Rotates)
                {
                    spinners.Add(new Spinner { Laser = laser, Angle = first, Last = laser.StartTime, Turn = laser.Spins ? laser.AngularSpeed : Math.Abs(laser.AngularSpeed) });
                    return;
                }

                float reach = Math.Max(laser.HitHalfWidth, 0f) + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.FORCED_MARGIN;
                double slice = DanmakusuBalance.FORCED_BEAM_SLICE_MS;
                double life = laser.Lifetime;

                // (a slider's beam: every slice flies straight from where the ball was when it was fired)
                for (int b = 0; b < laser.BeamCount; b++)
                {
                    Vector2 velocity = laser.Directions[b] * laser.Speed;

                    for (double a = laser.StartTime; a < laser.EndTime; a += slice)
                    {
                        double e = Math.Min(a + slice, laser.EndTime);

                        addStretch(new Stretch
                        {
                            T1 = a,
                            T2 = e,
                            Until = e + life,
                            P1 = laser.SlicePosition(a, a, b),
                            V1 = velocity,
                            P2 = laser.SlicePosition(e, e, b),
                            V2 = velocity,
                            Reach = reach,
                        });
                    }
                }
            }

            /// <summary>A beam that turns (a spinner's) is made as it goes: every step a new stretch from the way it pointed to the way it points, the nearest beam turning towards where the player is now.</summary>
            private void growSpinners(double t)
            {
                for (int i = spinners.Count - 1; i >= 0; i--)
                {
                    var spinner = spinners[i];
                    var laser = spinner.Laser;
                    double upTo = Math.Min(t, laser.EndTime);

                    if (upTo > spinner.Last)
                    {
                        float next = spinner.Angle;
                        float span = (float)(upTo - spinner.Last);

                        if (laser.Spins)
                            next += spinner.Turn * span;
                        else
                        {
                            Vector2 toPlayer = player - laser.Origin;
                            float wanted = MathF.Atan2(toPlayer.Y, toPlayer.X);
                            float share = MathF.Tau / laser.BeamCount;
                            float off = MathF.IEEERemainder(wanted - spinner.Angle, share);
                            next += Math.Clamp(off, -spinner.Turn * span, spinner.Turn * span);
                        }

                        float reach = Math.Max(laser.HitHalfWidth, 0f) + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.FORCED_MARGIN;

                        for (int b = 0; b < laser.BeamCount; b++)
                        {
                            float offset = b * MathF.Tau / laser.BeamCount;

                            addStretch(new Stretch
                            {
                                T1 = spinner.Last,
                                T2 = upTo,
                                Until = upTo + laser.Lifetime,
                                P1 = laser.Origin,
                                V1 = new Vector2(MathF.Cos(spinner.Angle + offset), MathF.Sin(spinner.Angle + offset)) * laser.Speed,
                                P2 = laser.Origin,
                                V2 = new Vector2(MathF.Cos(next + offset), MathF.Sin(next + offset)) * laser.Speed,
                                Reach = reach,
                            });
                        }

                        spinner.Angle = next;
                        spinner.Last = upTo;
                    }

                    if (t >= laser.EndTime)
                        spinners.RemoveAt(i);
                }
            }

            // ---- what is near ----

            /// <summary>Takes away what has gone and notes what can come near the player within the moment they look ahead (only that is looked at for every place they could go to).</summary>
            private void gatherNear(double t)
            {
                nearFliers.Clear();
                nearStretches.Clear();

                float walk = walkSpeed * (float)horizon;
                const float out_of_field = 60f;
                int kept = 0;

                for (int i = 0; i < flierCount; i++)
                {
                    ref readonly Flier flier = ref fliers[i];

                    if (t >= flier.Until)
                        continue;

                    Vector2 p = flier.At(Math.Max(t, flier.T0));
                    Vector2 v = flier.V + flier.A * (float)Math.Max(t - flier.T0, 0);

                    // (it has left and is going further away: gone, as in the game)
                    if ((p.X < -out_of_field && v.X <= 0f) || (p.X > field.X + out_of_field && v.X >= 0f) || (p.Y < -out_of_field && v.Y <= 0f) || (p.Y > field.Y + out_of_field && v.Y >= 0f))
                    {
                        if (flier.A == Vector2.Zero || Vector2.Dot(v, flier.A) >= 0f)
                            continue;
                    }

                    if (kept != i)
                        fliers[kept] = fliers[i];

                    float reach = fliers[kept].Reach + walk + v.Length * (float)horizon + fliers[kept].A.Length * (float)(horizon * horizon) * 0.5f;

                    if ((p - player).LengthSquared <= reach * reach)
                        nearFliers.Add(kept);

                    kept++;
                }

                flierCount = kept;
                kept = 0;

                for (int i = 0; i < stretchCount; i++)
                {
                    ref readonly Stretch stretch = ref stretches[i];

                    if (t >= stretch.Until)
                        continue;

                    if (kept != i)
                        stretches[kept] = stretches[i];

                    // (what is not there yet comes within the moment or later: it is looked at from when it has been fired)
                    if (stretches[kept].T2 <= t + horizon)
                    {
                        stretches[kept].At(t, out Vector2 a, out Vector2 b);
                        float reach = stretches[kept].Reach + walk + Math.Max(stretches[kept].V1.Length, stretches[kept].V2.Length) * (float)horizon;

                        if (distanceToSegment(player, a, b) <= reach)
                            nearStretches.Add(kept);
                    }

                    kept++;
                }

                stretchCount = kept;
            }

            // ---- what the player does ----

            private void decide(double t, out float moved, out int dodges, out int teleports, out int trapped)
            {
                moved = 0f;
                dodges = teleports = trapped = 0;

                // (still on the way to where they were going, and that way is still safe: they walk on)
                if (walking)
                {
                    if (safe(player, target, t, nearOnly: true))
                    {
                        moved = walkOn();
                        return;
                    }

                    walking = false;
                }
                else if (safe(player, player, t, nearOnly: true))
                    return;

                // (they have to do something: the nearest place they can walk to and stay safe at; of those as near, the one closest to the middle of the playfield)
                foreach (float distance in distances)
                {
                    bool found = false;
                    float best = float.PositiveInfinity;
                    Vector2 bestPlace = player;

                    foreach (Vector2 way in directions)
                    {
                        Vector2 place = Vector2.Clamp(player + way * distance, Vector2.Zero, field);

                        if ((place - player).LengthSquared < 1f)
                            continue;

                        float score = (place - middle).LengthSquared;

                        if (score < best && safe(player, place, t, nearOnly: true))
                        {
                            best = score;
                            bestPlace = place;
                            found = true;
                        }
                    }

                    if (!found)
                        continue;

                    Vector2 going = Vector2.Normalize(bestPlace - player);

                    // (going on much the same way is the same dodge)
                    if (lastWay == Vector2.Zero || Vector2.Dot(going, lastWay) < DanmakusuBalance.FORCED_SAME_DODGE_COS)
                        dodges = 1;

                    lastWay = going;
                    target = bestPlace;
                    walking = true;
                    moved = walkOn();
                    return;
                }

                // (no walk gets them out in time: they teleport to the nearest place that is safe for the whole moment)
                float nearest = float.PositiveInfinity;
                Vector2 nearestPlace = player;
                bool any = false;

                foreach (Vector2 place in grid)
                {
                    float away = (place - player).LengthSquared;

                    if (away < nearest && safe(place, place, t, nearOnly: false))
                    {
                        nearest = away;
                        nearestPlace = place;
                        any = true;
                    }
                }

                lastWay = Vector2.Zero;

                if (any)
                {
                    player = target = nearestPlace;
                    teleports = 1;
                }
                else
                    trapped = 1; // (nowhere is safe: it counts as much as a teleport, as they lose what they have got)
            }

            private float walkOn()
            {
                Vector2 way = target - player;
                float left = way.Length;
                float reach = walkSpeed * (float)step;

                if (left <= reach)
                {
                    player = target;
                    walking = false;
                    return left;
                }

                player += way * (reach / left);
                return reach;
            }

            /// <summary>Is the way from <paramref name="from"/> to <paramref name="to"/>, at walking speed, and then staying there, safe for the whole moment they look ahead?</summary>
            private bool safe(Vector2 from, Vector2 to, double t, bool nearOnly)
            {
                float distance = (to - from).Length;
                double walk = distance > 0f ? distance / walkSpeed : 0;
                int samples = (int)Math.Ceiling(horizon / sample);
                Vector2 before = from;

                for (int k = 1; k <= samples; k++)
                {
                    double tau0 = (k - 1) * sample, tau = k * sample;
                    Vector2 here = distance > 0f ? Vector2.Lerp(from, to, (float)Math.Min(tau / walk, 1.0)) : from;
                    double t0 = t + tau0, t1 = t + tau;

                    if (nearOnly)
                    {
                        for (int n = 0; n < nearFliers.Count; n++)
                        {
                            if (flierHits(fliers[nearFliers[n]], t0, t1, before, here))
                                return false;
                        }

                        for (int n = 0; n < nearStretches.Count; n++)
                        {
                            if (stretchHits(stretches[nearStretches[n]], t1, here))
                                return false;
                        }
                    }
                    else
                    {
                        for (int n = 0; n < flierCount; n++)
                        {
                            if (flierHits(fliers[n], t0, t1, before, here))
                                return false;
                        }

                        for (int n = 0; n < stretchCount; n++)
                        {
                            if (stretchHits(stretches[n], t1, here))
                                return false;
                        }
                    }

                    before = here;
                }

                return true;
            }

            /// <summary>Do the bullet and the player come within its reach between two moments? (Both move about straight between them: how near they come is worked out, so that a fast bullet does not fly through unseen.)</summary>
            private static bool flierHits(in Flier flier, double t0, double t1, Vector2 player0, Vector2 player1)
            {
                if (t1 <= flier.T0 || t0 >= flier.Until)
                    return false;

                Vector2 a = flier.At(Math.Max(t0, flier.T0)) - player0;
                Vector2 b = flier.At(Math.Min(t1, flier.Until)) - player1;
                Vector2 ab = b - a;
                float lengthSquared = ab.LengthSquared;
                float u = lengthSquared > 1e-9f ? Math.Clamp(-Vector2.Dot(a, ab) / lengthSquared, 0f, 1f) : 0f;

                return (a + ab * u).LengthSquared <= flier.Reach * flier.Reach;
            }

            private static bool stretchHits(in Stretch stretch, double t, Vector2 place)
            {
                if (t < stretch.T2 || t >= stretch.Until)
                    return false;

                stretch.At(t, out Vector2 a, out Vector2 b);
                return distanceToSegment(place, a, b) <= stretch.Reach;
            }
        }

        private static Vector2 rotate(Vector2 v, Vector2 turn) => new Vector2(v.X * turn.X - v.Y * turn.Y, v.X * turn.Y + v.Y * turn.X);

        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;
            float u = lengthSquared > 1e-9f ? Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f) : 0f;
            return (a + ab * u - point).Length;
        }
    }
}
