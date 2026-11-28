using System;
using System.Runtime.CompilerServices;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// One of Rikako's asteroids (2026-11-26): it flies straight across the screen at a steady speed from <see cref="Born"/> (at <see cref="From"/>) until <see cref="Gone"/>, turning slowly; one of the user's
    /// rocks (<see cref="Picture"/>), <see cref="Size"/> px on its picture's longer side; what flies into its hit circle (<see cref="Hit"/> px round its middle) bounces off it.
    /// </summary>
    public readonly struct Asteroid
    {
        public readonly double Born;
        public readonly double Gone;
        public readonly Vector2 From;
        public readonly Vector2 Velocity;
        public readonly float Size;
        public readonly float Hit;
        public readonly int Picture;
        public readonly float Turn;
        public readonly float Spin;

        /// <summary>
        /// While it can be on any screen (up to <see cref="Asteroids.SCREEN_HALF"/> round the middle of the playfield, its hit circle too): nothing bounces off it before or after, for a bounce is only
        /// on the screen (2026-11-28: twice as many rocks, and every bullet looked at all of them, most of them still far outside - 7.7 ms a frame for 2.8).
        /// </summary>
        public readonly double OnFrom, OnTo;

        /// <summary>The latest <see cref="OnTo"/> of this one and every one born before it: every rock before the first whose this is past a moment is off every screen then (a search starts there).</summary>
        public readonly double OnToSoFar;

        public Asteroid(in Asteroid other, double onToSoFar)
        {
            this = other;
            OnToSoFar = onToSoFar;
        }

        public Asteroid(double born, double gone, Vector2 from, Vector2 velocity, float size, float hit, int picture, float turn, float spin)
        {
            Born = born;
            Gone = gone;
            From = from;
            Velocity = velocity;
            Size = size;
            Hit = hit;
            Picture = picture;
            Turn = turn;
            Spin = spin;

            // (when its path is within the box round the middle, its hit circle reaching in: a slab on each axis)
            double inside = 0, outside = gone - born;
            Vector2 middle = new Vector2(256f, 192f), half = Asteroids.SCREEN_HALF + new Vector2(hit);

            for (int axis = 0; axis < 2; axis++)
            {
                float p = axis == 0 ? from.X - middle.X : from.Y - middle.Y, w = axis == 0 ? velocity.X : velocity.Y, h = axis == 0 ? half.X : half.Y;

                if (MathF.Abs(w) < 1e-9f)
                {
                    if (MathF.Abs(p) > h)
                        outside = -1; // (never in)

                    continue;
                }

                double a = (-h - p) / w, b = (h - p) / w;
                inside = Math.Max(inside, Math.Min(a, b));
                outside = Math.Min(outside, Math.Max(a, b));
            }

            if (outside <= inside)
                OnFrom = OnTo = born;
            else
            {
                OnFrom = born + inside;
                OnTo = born + outside;
            }
        }

        /// <summary>Where its middle is at <paramref name="t"/>.</summary>
        public Vector2 At(double t) => From + Velocity * (float)(t - Born);

        /// <summary>How far it has turned at <paramref name="t"/> (radians).</summary>
        public float AngleAt(double t) => Turn + Spin * (float)(t - Born);
    }

    /// <summary>
    /// Rikako's asteroids (2026-11-26, "During her entire section a lot of asteroids from random playfield sides to random direction with random size and shape (choosen from image, except veeery small
    /// ones). Bullets ricochet from those asteroids."): one every RIKAKO_ROCK_EVERY_BEATS, from RIKAKO_ROCK_FILL_BEATS before her section begins (so that the screen is full of them as it does, they fade
    /// in then) to its end. Each comes in from a place of its own on a circle round the middle of the playfield, well outside the screen, and flies through a place of its own near the middle and on out, at
    /// RIKAKO_ROCK_SPEED_LEAST..MOST circle radii a beat, RIKAKO_ROCK_LEAST..MOST_CIRCLES circle radii on its picture's longer side, turning slowly one way or the other; which rock it is, its size, way
    /// and speed are all by its number (a hash), so a seek makes the same ones. The ops (<see cref="TrajectoryOpKind.Rocks"/>) and her act both take them from here.
    /// </summary>
    public static class Asteroids
    {
        /// <summary>How far out each rock's hit circle reaches (a circle of the rock's own area), as a share of its picture's longer side (Tools/Bake `rocks` prints them; StageLayer has the pictures).</summary>
        public static readonly float[] HIT_SHARES = { 0.435f, 0.442f, 0.402f, 0.448f, 0.457f, 0.431f, 0.411f, 0.452f, 0.440f, 0.453f, 0.406f, 0.421f, 0.427f, 0.429f };

        /// <summary>Half of the biggest screen there can be round the middle of the playfield (px: about 2.5 to 1, a little taller than a screen is) - bounces are only on the screen, so a rock outside this is passed over.</summary>
        public static readonly Vector2 SCREEN_HALF = new Vector2(600f, 290f);

        /// <summary>How far from the middle of the playfield they come in (px): out of every screen up to 21:9.</summary>
        private const float start_distance = 720f;

        private static readonly ConditionalWeakTable<BossPeriod, Asteroid[]> made = new ConditionalWeakTable<BossPeriod, Asteroid[]>();

        /// <summary>The longest any of them is there (ms) in <paramref name="period"/>: whatever is there at a moment was born at most this long before it.</summary>
        public static double Longest(BossPeriod period)
        {
            double beat = Math.Max(period.BeatLength, 1);
            float slowest = period.CircleRadius * DanmakusuBalance.RIKAKO_ROCK_SPEED_LEAST / (float)beat;
            return 2f * (start_distance + period.CircleRadius * DanmakusuBalance.RIKAKO_ROCK_MOST_CIRCLES * DanmakusuBalance.RIKAKO_ROCK_BIG) / Math.Max(slowest, 1e-4f);
        }

        /// <summary>Rikako's asteroids in <paramref name="period"/>, in the order they are born (made once per period and kept).</summary>
        public static Asteroid[] Of(BossPeriod period)
        {
            lock (made)
            {
                if (made.TryGetValue(period, out var known))
                    return known;

                var list = make(period);
                made.Add(period, list);
                return list;
            }
        }

        private static Asteroid[] make(BossPeriod period)
        {
            double beat = Math.Max(period.BeatLength, 1);
            double every = beat * DanmakusuBalance.RIKAKO_ROCK_EVERY_BEATS;
            float circle = period.CircleRadius;
            var middle = new Vector2(256f, 192f);
            var list = new System.Collections.Generic.List<Asteroid>();
            int seed = (int)(period.Start % 1000003);
            int rocks = HIT_SHARES.Length;

            for (int k = 0; list.Count < 4000; k++)
            {
                double born = period.Start - beat * DanmakusuBalance.RIKAKO_ROCK_FILL_BEATS + k * every;

                if (born >= period.End)
                    break;

                float u(int salt) => hash01(seed, k, salt);

                // (which rock: the next ones are never the same as the one before - a step of 1..rocks-1 from it, by the hash)
                int picture = (k * 5 + (int)(u(1) * rocks)) % rocks;
                float size = circle * (DanmakusuBalance.RIKAKO_ROCK_LEAST_CIRCLES + (DanmakusuBalance.RIKAKO_ROCK_MOST_CIRCLES - DanmakusuBalance.RIKAKO_ROCK_LEAST_CIRCLES) * u(2));

                // (every third one twice as big: 2026-11-28, "Every third one is 2x size")
                if (k % DanmakusuBalance.RIKAKO_ROCK_BIG_EVERY == DanmakusuBalance.RIKAKO_ROCK_BIG_EVERY - 1)
                    size *= DanmakusuBalance.RIKAKO_ROCK_BIG;

                float hit = size * HIT_SHARES[picture];

                // (in from a place of its own round the middle, through a place of its own near the middle - so from any side, any way across)
                float from = u(3) * MathF.Tau;
                Vector2 start = middle + new Vector2(MathF.Cos(from), MathF.Sin(from)) * (start_distance + size * 0.5f);
                Vector2 through = middle + new Vector2((u(4) * 2f - 1f) * 220f, (u(5) * 2f - 1f) * 150f);
                Vector2 way = through - start;
                way = way.LengthSquared > 1e-6f ? Vector2.Normalize(way) : -Vector2.UnitY;

                float speed = circle * (DanmakusuBalance.RIKAKO_ROCK_SPEED_LEAST + (DanmakusuBalance.RIKAKO_ROCK_SPEED_MOST - DanmakusuBalance.RIKAKO_ROCK_SPEED_LEAST) * u(6)) / (float)beat;
                double gone = born + 2f * (start_distance + size) / Math.Max(speed, 1e-4f);
                float spin = (u(7) < 0.5f ? -1f : 1f) * DanmakusuBalance.RIKAKO_ROCK_SPIN_DEGREES * (0.4f + 0.6f * u(8)) * MathF.PI / 180f / (float)beat;

                list.Add(new Asteroid(born, gone, start, way * speed, size, hit, picture, u(9) * MathF.Tau, spin));
            }

            // (the latest end on a screen so far, for the search of the ops)
            double soFar = double.NegativeInfinity;

            for (int i = 0; i < list.Count; i++)
            {
                soFar = Math.Max(soFar, list[i].OnTo);
                list[i] = new Asteroid(list[i], soFar);
            }

            return list.ToArray();
        }

        /// <summary>A number in [0, 1) of its own for every (seed, number, salt).</summary>
        private static float hash01(int seed, int number, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)number * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>
        /// When (ms after now) a thing at <paramref name="position"/> flying at <paramref name="v"/> first flies into <paramref name="rock"/> (its middle into its hit circle, both moving on straight), within
        /// <paramref name="limit"/>; infinity when it does not, and when it is in it already (it flies through: on straight lines it never comes in again).
        /// </summary>
        public static double Enters(in Asteroid rock, Vector2 position, Vector2 v, double now, double limit)
        {
            // (only while it can be on a screen: a bounce is only on the screen)
            double from = Math.Max(now, Math.Max(rock.Born, rock.OnFrom));
            double until = Math.Min(now + limit, Math.Min(rock.Gone, rock.OnTo));

            if (until <= from)
                return double.PositiveInfinity;

            // (from when the rock is there: the thing has flown on to then)
            Vector2 p = position + v * (float)(from - now);
            Vector2 d = p - rock.At(from);
            Vector2 w = v - rock.Velocity;
            float r = rock.Hit;
            float c = Vector2.Dot(d, d) - r * r;

            if (c <= 0f)
                return double.PositiveInfinity; // (in it already)

            float a = Vector2.Dot(w, w);
            float b = Vector2.Dot(d, w);

            if (a < 1e-12f || b >= 0f)
                return double.PositiveInfinity; // (not coming closer)

            float disc = b * b - a * c;

            if (disc < 0f)
                return double.PositiveInfinity;

            double s = (-b - MathF.Sqrt(disc)) / a;

            if (s < 0 || from + s > until)
                return double.PositiveInfinity;

            return from + s - now;
        }
    }
}
