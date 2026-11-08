using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// The star rating by bullet density. The map is planned without bosses: only what its circles, sliders and spinners shoot (the rings, the spinners' stars and the slider beams). Every step of the
    /// song it is measured how much of the playfield they cut through per second: a bullet sweeps a strip as wide as it hurts (plus the character's hitbox) at its speed for as long as it is on the
    /// playfield, and a beam counts as if the stretch it covers were cut through again every <see cref="DanmakusuBalance.DENSITY_BEAM_SWEEP_MS"/>. The density at a moment is that of about the last
    /// second; the densest moments of the map's parts, from the densest down and each counting a little less (as osu!'s own rating does), make its value, and the stars grow with it.
    /// (Where an aimed ring or beam goes depends on the player: it is taken as aimed at the middle of the playfield. Bullets that bounce go back across the playfield once, as they do in the game, and are gone.)
    /// </summary>
    public static class BulletDensity
    {
        public sealed class Result
        {
            public double Stars;

            /// <summary>The weighted parts: playfields cut through per second.</summary>
            public double Value;

            /// <summary>The densest moment and the average over the time something is on the playfield (playfields per second).</summary>
            public double Peak;

            public double Average;

            /// <summary>How much of all that was cut through comes from beams.</summary>
            public double BeamShare;

            public double PeakTime;
            public int Rings, Bullets, Beams;
        }

        private static readonly Vector2 field = OsuPlayfield.BASE_SIZE;
        private static readonly Vector2 middle = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>Rates the (converted) map, played <paramref name="clockRate"/> times as fast.</summary>
        public static Result Rate(IBeatmap beatmap, double clockRate = 1)
        {
            var rings = BulletPlanner.Plan(beatmap, out var lasers, out _, noBoss: true);
            return Rate(rings, lasers, clockRate);
        }

        /// <summary>Rates what a map shoots, planned already without bosses (it is only read), played <paramref name="clockRate"/> times as fast.</summary>
        public static Result Rate(BulletEmitter[] rings, LaserEmitter[] lasers, double clockRate = 1)
        {
            double rate = Math.Max(clockRate, 0.01);
            double step = DanmakusuBalance.DENSITY_STEP_MS;
            double area = field.X * field.Y;
            double last = 0;

            foreach (var ring in rings)
                last = Math.Max(last, ring.Time + ring.Lifetime);

            foreach (var laser in lasers)
                last = Math.Max(last, laser.EndTime + laser.Lifetime);

            int steps = (int)Math.Ceiling(last / rate / step) + 2;
            // (each step: playfields cut through per second, on average over it; whole steps go in as a running sum, the ends of a stretch as the part of the step they cover)
            var density = new double[steps + 1];
            var running = new double[steps + 1];
            double fromBeams = 0, total = 0;
            int bullets = 0;

            void add(double start, double end, double perSecond)
            {
                double x0 = Math.Max(start / step, 0), x1 = Math.Min(end / step, steps - 1);

                if (x1 <= x0 || perSecond <= 0)
                    return;

                int i0 = (int)x0, i1 = (int)x1;
                total += perSecond * (x1 - x0) * step / 1000;

                if (i0 == i1)
                {
                    density[i0] += perSecond * (x1 - x0);
                    return;
                }

                density[i0] += perSecond * (i0 + 1 - x0);
                density[i1] += perSecond * (x1 - i1);
                running[i0 + 1] += perSecond;
                running[i1] -= perSecond;
            }

            foreach (var ring in rings)
            {
                if (ring.Count == 0 || ring.Speed <= 0)
                    continue;

                double width = 2 * (Math.Max(ring.HitRadius, 0) + DanmakusuBalance.PLAYER_HITBOX_RADIUS);
                double start = ring.Time / rate;
                Vector2 turn = aimTurn(ring);

                for (int i = 0; i < ring.Count; i++)
                {
                    double speed = ring.Speed * (ring.RadialScales != null && i < ring.RadialScales.Length ? ring.RadialScales[i] : 1) * rate; // (px per real ms)

                    if (speed <= 0)
                        continue;

                    Vector2 origin = ring.Origins != null && i < ring.Origins.Length ? ring.Origins[i] : ring.Origin;
                    Vector2 direction = rotate(ring.Directions[i], turn);

                    if (!crossing(origin, direction, out float near, out float far))
                        continue;

                    if (ring.Ricochet)
                        far = bounced(origin, direction, far);

                    double enter = Math.Max(near, ring.SpawnRadius) / speed;
                    double leave = Math.Min(far / speed, ring.Lifetime / rate);

                    bullets++;
                    add(start + enter, start + leave, width * speed * 1000 / area);
                }
            }

            foreach (var laser in lasers)
            {
                if (laser.Speed <= 0)
                    continue;

                double width = 2 * (Math.Max(laser.HitHalfWidth, 0) + DanmakusuBalance.PLAYER_HITBOX_RADIUS) * laser.BeamCount;
                double speed = laser.Speed * rate;
                double start = laser.StartTime / rate, end = laser.EndTime / rate;
                // (how far the beam reaches across the playfield: on average over every way it could point from where it starts)
                double reach = averageReach(laser.Origin);
                double grow = reach / speed;

                // (it grows out to the edge, stays while the slider lasts, and its tail flies off after it: measured in pieces of a step)
                for (double t = start; t < end + grow; t += step)
                {
                    double length = Math.Min(Math.Min(t + step / 2 - start, end + grow - (t + step / 2)) * speed, reach);

                    if (length <= 0)
                        continue;

                    double perSecond = width * length / area * 1000 / DanmakusuBalance.DENSITY_BEAM_SWEEP_MS;
                    double before = total;
                    add(t, Math.Min(t + step, end + grow), perSecond);
                    fromBeams += total - before;
                }
            }

            // (the density at every step: about the last second, the older steps counting less)
            double memory = Math.Exp(-step / DanmakusuBalance.DENSITY_MEMORY_MS);
            int perSection = Math.Max((int)Math.Round(DanmakusuBalance.DENSITY_SECTION_MS / step), 1);
            var peaks = new List<double>();
            double held = 0, flow = 0, peak = 0, peakTime = 0, sum = 0;
            int busy = 0;

            for (int i = 0; i < steps; i++)
            {
                flow += running[i];
                double now = density[i] + flow;
                held = held * memory + now * (1 - memory);

                if (now > 1e-6)
                {
                    sum += now;
                    busy++;
                }

                if (held > peak)
                {
                    peak = held;
                    peakTime = i * step * rate;
                }

                if (i % perSection == 0)
                    peaks.Add(held);
                else
                    peaks[^1] = Math.Max(peaks[^1], held);
            }

            peaks.Sort((a, b) => b.CompareTo(a));
            double value = 0, weight = 1;

            foreach (double p in peaks)
            {
                value += p * weight;
                weight *= DanmakusuBalance.DENSITY_SECTION_WEIGHT;
            }

            return new Result
            {
                Value = value,
                Stars = DanmakusuBalance.DENSITY_STAR_SCALE * Math.Pow(value, DanmakusuBalance.DENSITY_STAR_POWER),
                Peak = peak,
                PeakTime = peakTime,
                Average = busy > 0 ? sum / busy : 0,
                BeamShare = total > 0 ? fromBeams / total : 0,
                Rings = rings.Length,
                Bullets = bullets,
                Beams = lasers.Length,
            };
        }

        /// <summary>A ring aimed at the player is taken as aimed at the middle of the playfield: how far (as cos, sin) its directions are turned for that (none for any other ring).</summary>
        private static Vector2 aimTurn(BulletEmitter ring)
        {
            Vector2 towards = middle - ring.Origin;

            if (!ring.AimAtPlayer || ring.Count == 0 || towards.LengthSquared < 1)
                return new Vector2(1, 0);

            // (as the field does it: the first bullet points at the player, turned by the ring's aim offset)
            Vector2 first = ring.Directions[0];
            float delta = MathF.Atan2(towards.Y, towards.X) - MathF.Atan2(first.Y, first.X) + ring.AimOffset;
            return new Vector2(MathF.Cos(delta), MathF.Sin(delta));
        }

        private static Vector2 rotate(Vector2 v, Vector2 turn) => new Vector2(v.X * turn.X - v.Y * turn.Y, v.X * turn.Y + v.Y * turn.X);

        /// <summary>Where (px along it) a line from <paramref name="origin"/> the way of <paramref name="direction"/> is on the playfield; false if it never is.</summary>
        private static bool crossing(Vector2 origin, Vector2 direction, out float near, out float far)
        {
            near = 0;
            far = float.PositiveInfinity;

            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? origin.X : origin.Y, d = axis == 0 ? direction.X : direction.Y, size = axis == 0 ? field.X : field.Y;

                if (MathF.Abs(d) < 1e-6f)
                {
                    if (o < 0 || o > size)
                        return false;

                    continue;
                }

                float a = (0 - o) / d, b = (size - o) / d;
                near = Math.Max(near, Math.Min(a, b));
                far = Math.Min(far, Math.Max(a, b));
            }

            return far > near;
        }

        /// <summary>How far a bullet that reaches the edge after <paramref name="far"/> px flies until it is gone: it bounces off the edge <see cref="DanmakusuBalance.MAX_RICOCHETS"/> times and flies out through the next one.</summary>
        private static float bounced(Vector2 origin, Vector2 direction, float far)
        {
            Vector2 at = origin + direction * far;

            for (int i = 0; i < DanmakusuBalance.MAX_RICOCHETS; i++)
            {
                // (off the edge it has got to: the side it is nearest to)
                bool leftOrRight = Math.Min(Math.Abs(at.X), Math.Abs(at.X - field.X)) < Math.Min(Math.Abs(at.Y), Math.Abs(at.Y - field.Y));
                direction = leftOrRight ? new Vector2(-direction.X, direction.Y) : new Vector2(direction.X, -direction.Y);

                if (!crossing(at, direction, out _, out float next))
                    break;

                far += next;
                at += direction * next;
            }

            return far;
        }

        /// <summary>How far, on average over every way, it is from <paramref name="origin"/> to the edge of the playfield.</summary>
        private static double averageReach(Vector2 origin)
        {
            const int ways = 16;
            double sum = 0;

            for (int i = 0; i < ways; i++)
            {
                float angle = i * MathF.Tau / ways;

                if (crossing(origin, new Vector2(MathF.Cos(angle), MathF.Sin(angle)), out float near, out float far))
                    sum += far - near;
            }

            return sum / ways;
        }
    }
}
