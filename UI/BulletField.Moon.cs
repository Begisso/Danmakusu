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
        #region Clownpiece's moon

        /// <summary>One Clownpiece section: the moon and the laser source (opposite each other) circle the centre of the screen, the source with three beams that turn as fast as it circles.</summary>
        private sealed class MoonWindow
        {
            public BossPeriod Period = null!;
            public double Start;
            public double End;

            /// <summary>+1 or -1: which way round they go.</summary>
            public float Direction;

            /// <summary>Radians per ms.</summary>
            public float AngularSpeed;

            /// <summary>Where the moon is (as an angle from the centre) when the attack starts: towards the player. Follows the player during the warning, then stays.</summary>
            public float Angle0;

            public bool Latched;

            /// <summary>The stars of the three lines of stars the laser source fires: how big they are (playfield pixels) and how fast they fly (pixels per ms).</summary>
            public float StarRadius;

            public float StarSpeed;

            /// <summary>One star of each line is fired this often (ms), so that the stars of a line overlap: a continuous line.</summary>
            public double Interval => Math.Max(StarRadius * DanmakusuBalance.MOON_STREAM_SPACING_FACTOR / Math.Max(StarSpeed, 1e-4f), 2.0);
        }

        private struct MoonState
        {
            public Vector2 Centre;
            public Vector2 Moon;
            public Vector2 Source;
            public float Angle;

            /// <summary>The angle of beam 0: fixed at the start (straight up), then turning against the moon's direction at the same speed; it never looks at the player.</summary>
            public float BeamAngle;

            public float Presence;

            /// <summary>The attack has started (the moon grows during the warning before it, and does no damage then).</summary>
            public bool Attacking;

            public float Radius;
            public float FullRadius;
            public float BeamStartWidth;
            public float BeamEndWidth;
            public float Height;
            public float BeamLength;
        }

        private Vector2 fieldCentre => hasScreenBounds ? (screenMin + screenMax) / 2 : new Vector2(DanmakusuBalance.RICOCHET_WIDTH, DanmakusuBalance.RICOCHET_HEIGHT) / 2;

        /// <summary>Where everything of a moon section is at <paramref name="t"/> (a pure function of time, once the angle at its start is known). Presence 1 = fully there.</summary>
        private MoonState moonGeometry(MoonWindow window, double t, float presence, float beamPresence)
        {
            bool attacking = t >= window.Start;
            t = Math.Max(t, window.Start); // (before the attack the moon stays where it will start)

            float height = hasScreenBounds ? screenMax.Y - screenMin.Y : DanmakusuBalance.RICOCHET_HEIGHT;
            float diagonal = hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 640f;
            float full = circleRadius * DanmakusuBalance.MOON_RADIUS_FACTOR;

            // the outer edge of the moon's path is MOON_ORBIT_EXTENT of the screen's height
            float orbit = Math.Max(0f, DanmakusuBalance.MOON_ORBIT_EXTENT * height / 2f - full);
            float angle = window.Angle0 + window.Direction * window.AngularSpeed * (float)(t - window.Start);
            Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * orbit;
            Vector2 centre = fieldCentre;

            return new MoonState
            {
                Centre = centre,
                Moon = centre + offset,
                Source = centre - offset,
                Angle = angle,
                BeamAngle = -MathF.PI / 2 - window.Direction * window.AngularSpeed * (float)(t - window.Start),
                Presence = presence,
                Attacking = attacking,
                Radius = full * presence,
                FullRadius = full,
                BeamStartWidth = circleRadius * DanmakusuBalance.MOON_BEAM_START_HALF_WIDTH_FACTOR * beamPresence,
                BeamEndWidth = circleRadius * DanmakusuBalance.MOON_BEAM_END_HALF_WIDTH_FACTOR * beamPresence,
                Height = height,
                BeamLength = diagonal + 200f,
            };
        }

        /// <summary>
        /// Is a moon there at <paramref name="t"/>? It grows to its full size during the warning (and does no damage then, but already stops bullets), stays for the whole attack,
        /// and shrinks away just after it ends. The beams grow in when the attack starts.
        /// </summary>
        private bool moonStateAt(double t, out MoonState state, out MoonWindow? window, bool ignoreFailed = false)
        {
            state = default;
            window = null;

            if (failed && !ignoreFailed)
                return false;

            for (int i = moons.Length - 1; i >= 0; i--)
            {
                var w = moons[i];

                if (t < w.Period.Start || t > w.End + DanmakusuBalance.MOON_FADE_OUT_MS)
                    continue;

                double presence;

                if (t < w.Start)
                {
                    double u = Math.Clamp((t - w.Period.Start) / Math.Max(w.Start - w.Period.Start, 1), 0, 1);
                    presence = u * u * (3 - 2 * u);
                }
                else
                    presence = t <= w.End ? 1 : Math.Clamp(1 - (t - w.End) / DanmakusuBalance.MOON_FADE_OUT_MS, 0, 1);

                double beam = 0;

                if (t >= w.Start)
                {
                    double fadeIn = Math.Clamp((t - w.Start) / DanmakusuBalance.MOON_FADE_IN_MS, 0, 1);
                    beam = Math.Min(fadeIn * fadeIn * (3 - 2 * fadeIn), t <= w.End ? 1 : Math.Clamp(1 - (t - w.End) / DanmakusuBalance.MOON_FADE_OUT_MS, 0, 1));
                }

                if (presence <= 0)
                    continue;

                state = moonGeometry(w, t, (float)presence, (float)beam);
                window = w;
                return true;
            }

            return false;
        }

        /// <summary>The moon appears towards the player: its angle is where the player is (seen from the centre) when the attack starts, and until then it follows the player (the warning shows it).</summary>
        private void aimMoons(double now)
        {
            foreach (var window in moons)
            {
                if (now < window.Start)
                    window.Latched = false;

                if (window.Latched)
                    continue;

                Vector2 toPlayer = characterPosition - fieldCentre;
                window.Angle0 = toPlayer.LengthSquared > 1e-4f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : -MathF.PI / 2;

                if (now >= window.Start)
                    window.Latched = true;
            }
        }

        private bool moonWindowOverlaps(double from, double to)
        {
            foreach (var window in moons)
            {
                if (from <= window.End + DanmakusuBalance.MOON_FADE_OUT_MS && to >= window.Period.Start)
                    return true;
            }

            return false;
        }

        /// <summary>Bullets that fly into the moon disappear (checked in small steps of game time, from where each ring was last checked up to <paramref name="now"/>).</summary>
        private void absorbIntoMoon(double now)
        {
            if (moons.Length == 0)
                return;

            // (every ring is looked at on its own and only its own bullets change, so the rings are shared out between the cores; the arrowheads, whose flight is set up when it is first asked for,
            // are looked at here, one after the other)
            if (activeCount >= parallel_min_rings)
                System.Threading.Tasks.Parallel.For(0, activeCount, parallelOptions, k => scanMoonOf(emitters[active[k]], now));
            else
            {
                for (int k = 0; k < activeCount; k++)
                    scanMoonOf(emitters[active[k]], now);
            }

            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];

                if (!emitter.Retired && emitter.Count > 0 && emitter.Homing)
                    absorbHoming(emitter, now);
            }
        }

        private void scanMoonOf(BulletEmitter emitter, double now)
        {
            if (!emitter.Retired && emitter.Count > 0 && !emitter.Homing && !emitter.Sharp && emitter.Shape != BulletShape.Train) // (Utsuho's big balls cannot be destroyed: they fly through the moon; nor Yukari's trains)
                scanMoon(emitter, now);
        }

        private void scanMoon(BulletEmitter emitter, double now)
        {
            double from = double.IsNaN(emitter.MoonScanned) ? emitter.Time : Math.Max(emitter.MoonScanned, emitter.Time);
            double to = now;

            if (to <= from)
                return;

            if (!moonWindowOverlaps(from, to))
            {
                emitter.MoonScanned = to;
                return;
            }

            double step = Math.Max(DanmakusuBalance.MOON_SCAN_STEP_MS, (to - from) / DanmakusuBalance.SCAN_MOST_STEPS);
            double previous = from;

            while (previous < to)
            {
                double t = Math.Min(previous + step, to);

                if (moonStateAt(t, out var state, out _))
                {
                    double ea = emitter.WithoutFrozenTime(effects.Effective(emitter, previous)), eb = emitter.WithoutFrozenTime(effects.Effective(emitter, t));

                    for (int b = 0; b < emitter.Count; b++)
                    {
                        ulong bit = 1UL << b;

                        if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                            continue;

                        emitter.Evaluate(b, ea, out Vector2 pa, out _, out bool aliveA);
                        emitter.Evaluate(b, eb, out Vector2 pb, out _, out bool aliveB);

                        if (!aliveA || !aliveB)
                            continue;

                        if (distanceToSegment(state.Moon, pa, pb) <= state.Radius) // (only the middle of a bullet has to touch the moon)
                            emitter.AbsorbedMask |= bit;
                    }
                }

                previous = t;
            }

            emitter.MoonScanned = to;
        }

        /// <summary>Pulled bullets (arrowheads) are wherever the simulation has put them: look at the step they just made.</summary>
        private void absorbHoming(BulletEmitter emitter, double now)
        {
            if (!moonStateAt(now, out var state, out _))
                return;

            for (int b = 0; b < emitter.Count; b++)
            {
                ulong bit = 1UL << b;

                if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & bit) != 0)
                    continue;

                emitter.Evaluate(b, now, out Vector2 position, out _, out bool alive);

                if (!alive)
                    continue;

                if (distanceToSegment(state.Moon, emitter.PreviousPosition(b), position) <= state.Radius)
                    emitter.AbsorbedMask |= bit;
            }
        }

        /// <summary>
        /// Every beam that is fired (a spinner's, a boss's, a slider's) has flown into the moon where its slices did: <see cref="sliceGone"/> says, for each of the first
        /// <paramref name="count"/> points of the beam just read into the buffers, whether that slice has hit the moon at some moment of its flight (so the beam
        /// has a gap, or ends, there). Needs the flown distances of the points in <see cref="laserDistanceBuffer"/>.
        /// </summary>
        private void markSlicesGone(LaserEmitter laser, int beam, int count, double time)
        {
            if (failed)
            {
                Array.Clear(sliceGone, 0, Math.Min(count, sliceGone.Length));
                return;
            }

            // (a slice that an explosion has taken is gone too)
            for (int p = 0; p < count && p < sliceGone.Length; p++)
                sliceGone[p] = laserLookBuffer[p]?.Gone == true;

            if (moons.Length == 0 || laser.Immune)
                return;

            for (int p = 0; p < count && p < sliceGone.Length; p++)
            {
                if (sliceGone[p])
                    continue;

                double fired = time - laserDistanceBuffer[p] / Math.Max(laser.Speed, 1e-6f);
                Vector2 direction;

                if (laser.Rotates)
                {
                    float angle = laser.SliceAngle(fired, time) + beam * MathF.Tau / laser.BeamCount;
                    direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }
                else
                    direction = laser.Directions[beam];

                sliceGone[p] = sliceFlewIntoMoonCached(laser, beam, laserBuffer[p], direction, fired, time);
            }
        }

        /// <summary>
        /// What <see cref="sliceFlewIntoMoon"/> has found out about a slice so far: up to when its flight has been looked at, and whether it has been in the moon by then. A slice is looked at
        /// every frame; with this only the part of its flight since the last look is walked (a slice that is gone stays gone). The key is the beam and the moment the slice was fired (to 1/20 ms).
        /// </summary>
        private readonly Dictionary<(LaserEmitter, int, long), (double UpTo, bool Gone)> moonSlices = new Dictionary<(LaserEmitter, int, long), (double, bool)>();

        private const int moon_slices_max = 200000; // (forgotten all at once beyond this: it only costs the walks again)

        private bool sliceFlewIntoMoonCached(LaserEmitter laser, int beam, Vector2 position, Vector2 direction, double fired, double now)
        {
            var key = (laser, beam, (long)Math.Round(fired * 20));
            double from = fired;

            if (moonSlices.TryGetValue(key, out var known))
            {
                if (known.Gone || known.UpTo >= now)
                    return known.Gone;

                from = Math.Max(fired, known.UpTo);
            }
            else if (moonSlices.Count >= moon_slices_max)
                moonSlices.Clear();

            bool gone = sliceFlewIntoMoon(position, direction, laser.Speed, fired, now, from);
            moonSlices[key] = (now, gone);
            return gone;
        }

        /// <summary>
        /// Did a slice that is at <paramref name="position"/> now, was fired at <paramref name="fired"/> and has flown straight at <paramref name="speed"/> along <paramref name="direction"/>
        /// ever been inside the moon (which moves)? Walked backwards in small steps, each tested as a segment, back to <paramref name="since"/> (the part before it is known to be clear).
        /// </summary>
        private bool sliceFlewIntoMoon(Vector2 position, Vector2 direction, float speed, double fired, double now, double since)
        {
            if (!moonWindowOverlaps(since, now))
                return false;

            double span = now - fired;
            double step = Math.Max(16, span / 200);
            Vector2 later = position;
            double tLater = now;

            if (moonStateAt(now, out var here, out _) && Vector2.Distance(position, here.Moon) <= here.Radius)
                return true;

            while (tLater > since)
            {
                double t = Math.Max(tLater - step, since);
                Vector2 earlier = position - direction * (speed * (float)(now - t));

                if (moonStateAt(tLater, out var state, out _) && distanceToSegment(state.Moon, earlier, later) <= state.Radius)
                    return true;

                later = earlier;
                tLater = t;
            }

            return false;
        }

        /// <summary>Is the character in the moon right now?</summary>
        private bool touchesMoon(double now)
            => moonStateAt(now, out var state, out _) && state.Attacking && Vector2.Distance(characterPosition, state.Moon) <= state.Radius + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

        // The laser source of the moon fires three lines of very dense stars (twice the speed of other bullets) instead of beams: a star of each line is fired every few
        // milliseconds from where the source is at that moment, in the direction its three arms point in at that moment, and flies straight on. The source circles and the arms turn,
        // so the lines curve like the beams of a spinner. A star that flies into the moon is gone. All of it is a function of time.

        private struct StreamStar
        {
            public Vector2 Position;
            public Vector2 Direction;
            public Vector2 Travel; // the way it flies (Direction is the way it is turned: it spins)
            public float Radius;
            public float Speed;
            public BulletLook? Look; // (what the ops of another boss have made of it: frozen, bounced...)
        }

        private StreamStar[] streamStars = new StreamStar[1024];
        private int streamCount;
        private double streamBuiltAt = double.NaN;

        /// <summary>The stars of all the moon's lines of stars at time <paramref name="t"/> (built once per moment).</summary>
        private void buildStreamStars(double t)
        {
            if (streamBuiltAt == t)
                return;

            streamBuiltAt = t;
            streamCount = 0;

            float diagonal = hasScreenBounds ? Vector2.Distance(screenMin, screenMax) : 640f;

            foreach (var window in moons)
            {
                if (t < window.Start)
                    continue;

                double interval = window.Interval;
                double lifetime = (diagonal + window.StarRadius * 2f) / window.StarSpeed; // (a star is off the screen after this long)
                double lastFire = Math.Min(t, window.End);

                // (what other bosses do to the bullets can make a star live longer: it is frozen, or slowed down, or pulled back by Utsuho's black hole - see flyWithOps)
                double longest = otherOpsOn ? lifetime * (pullOpsOn ? 3 : effects.Any || portalActs.Count > 0 ? 2 : 1) + freezeSpan(window.Start, t) + (pullOpsOn ? DanmakusuBalance.PULLED_EXTRA_LIFE_MS : 0) : lifetime;
                double firstFire = Math.Max(window.Start, t - longest);

                if (firstFire > lastFire)
                    continue;

                int kFirst = (int)Math.Ceiling((firstFire - window.Start) / interval);
                int kLast = (int)Math.Floor((lastFire - window.Start) / interval);

                for (int k = kFirst; k <= kLast; k++)
                {
                    double fired = window.Start + k * interval;
                    var geometry = moonGeometry(window, fired, 1f, 1f);

                    for (int line = 0; line < DanmakusuBalance.MOON_BEAM_COUNT; line++)
                    {
                        float a = geometry.BeamAngle + line * MathF.Tau / DanmakusuBalance.MOON_BEAM_COUNT;
                        Vector2 direction = new Vector2(MathF.Cos(a), MathF.Sin(a));

                        if (streamStarAbsorbed(geometry.Source, direction, window.StarSpeed, window.StarRadius, fired, t))
                            continue;

                        if (streamCount == streamStars.Length)
                            Array.Resize(ref streamStars, streamStars.Length * 2);

                        // (like the stars of a spinner, they turn as they fly)
                        float spin = DanmakusuBalance.STAR_SPIN_RATE * (float)(t - fired);
                        float cos = MathF.Cos(spin), sin = MathF.Sin(spin);

                        Vector2 position = geometry.Source + direction * (window.StarSpeed * (float)(t - fired));
                        Vector2 travel = direction;
                        BulletLook? look = null;

                        // the stars are bullets like any other for the other bosses: their ops freeze them, bounce them, slow them...
                        if (otherOpsOn)
                        {
                            if (!flyTraced(geometry.Source, direction * window.StarSpeed, Vector2.Zero, fired, fired + lifetime, t, window.StarRadius, out position, out Vector2 flying, out look))
                                continue;

                            if (flying.LengthSquared > 1e-12f)
                                travel = Vector2.Normalize(flying);
                        }

                        streamStars[streamCount++] = new StreamStar
                        {
                            Position = position,
                            Direction = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos),
                            Travel = travel,
                            Radius = window.StarRadius,
                            Speed = window.StarSpeed,
                            Look = look,
                        };
                    }
                }
            }
        }

        /// <summary>Did a star that was fired at <paramref name="fired"/> from <paramref name="source"/> and has flown straight since touch the moon on its way (the moon moves)?</summary>
        private bool streamStarAbsorbed(Vector2 source, Vector2 direction, float speed, float radius, double fired, double now)
        {
            if (!moonWindowOverlaps(fired, now))
                return false;

            double span = now - fired;
            int steps = (int)Math.Clamp(span / 40, 1, 24);
            Vector2 previous = source;

            for (int i = 1; i <= steps; i++)
            {
                double t = fired + span * i / steps;
                Vector2 point = source + direction * (speed * (float)(t - fired));

                if (moonStateAt(t, out var state, out _, true) && distanceToSegment(state.Moon, previous, point) <= state.Radius)
                    return true;

                previous = point;
            }

            return false;
        }

        /// <summary>Is the character in one of the stars of the moon's lines right now?</summary>
        private bool touchesMoonStream(double now)
        {
            if (moons.Length == 0)
                return false;

            buildStreamStars(now);

            for (int i = 0; i < streamCount; i++)
            {
                if (streamStars[i].Look is { Harmless: true })
                    continue; // (Reisen's slide: it does not hurt meanwhile)

                if (Vector2.Distance(characterPosition, streamStars[i].Position) <= streamStars[i].Radius * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                    return true;
            }

            return false;
        }

        /// <summary>Hands the stars of the moon's lines to the renderer (after the bullets, so they are drawn above them); <paramref name="fall"/> is how long the player has been dead.</summary>
        private int addStreamStars(double drawTime, float fall, int count)
        {
            if (moons.Length == 0)
                return count;

            buildStreamStars(drawTime);

            int style = backgroundStyle >= 0 ? backgroundStyle : 0;

            for (int i = 0; i < streamCount; i++)
            {
                var star = streamStars[i];
                Vector2 position = star.Position;

                if (failed)
                {
                    // dead: the stars fall like every bullet
                    Vector2 velocity = DanmakusuBalance.DEATH_BULLETS_KEEP_VELOCITY ? star.Travel * star.Speed : Vector2.Zero;
                    position += velocity * fall + new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);
                }

                if (hasScreenBounds && (position.X < screenMin.X - star.Radius || position.X > screenMax.X + star.Radius || position.Y < screenMin.Y - star.Radius || position.Y > screenMax.Y + star.Radius))
                    continue;

                if (count == instanceBuffer.Length)
                    Array.Resize(ref instanceBuffer, instanceBuffer.Length * 2);

                instanceBuffer[count++] = new BulletInstance
                {
                    Position = position,
                    Radius = star.Radius,
                    Style = style,
                    Direction = star.Direction,
                    Shape = BulletShape.SoftStar,
                    Speed = star.Speed,
                    Overlay = star.Look is { Overlay: > 0 } ? star.Look.Overlay : 0,
                    OverlayAmount = star.Look is { Overlay: > 0 } ? star.Look.OverlayAmount : 0f,
                    Decal = star.Look?.Decal ?? BulletDecal.None,
                    Fade = star.Look is { Alpha: < 1f } ? 1f - star.Look.Alpha : 0f,
                };
            }

            return count;
        }

        /// <summary>The moon, the laser source and the warning that leads into them. When the player is dead the moon and the laser source fall too, like every bullet.</summary>
        private void updateMoonLayer(double drawTime, float fall)
        {
            if (moons.Length == 0)
                return;

            if (moonStateAt(drawTime, out var state, out var window, true))
            {
                Vector2 moonPosition = state.Moon, sourcePosition = state.Source;

                if (failed)
                {
                    // free fall from where they were, with the speed they had (they circle the centre)
                    Vector2 moonVelocity = Vector2.Zero, sourceVelocity = Vector2.Zero;

                    if (DanmakusuBalance.DEATH_BULLETS_KEEP_VELOCITY && window != null)
                    {
                        var later = moonGeometry(window, drawTime + 1, 1f, 1f);
                        moonVelocity = later.Moon - state.Moon;
                        sourceVelocity = later.Source - state.Source;
                    }

                    Vector2 down = new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);
                    moonPosition += moonVelocity * fall + down;
                    sourcePosition += sourceVelocity * fall + down;
                }

                moonLayer.SetState(state.Presence, moonPosition, state.Radius, state.Angle, sourcePosition, circleRadius);
                moonTopLayer.SetState(state.Presence, moonPosition, state.Radius, state.Angle, sourcePosition, circleRadius);
            }
            else
            {
                moonLayer.SetState(0, Vector2.Zero, 0, 0, Vector2.Zero, circleRadius);
                moonTopLayer.SetState(0, Vector2.Zero, 0, 0, Vector2.Zero, circleRadius);
            }

            moonWarnings.Clear();

            if (!failed)
            {
                foreach (var moonWindow in moons)
                {
                    if (drawTime < moonWindow.Period.Start || drawTime > moonWindow.Start + 2000)
                        continue;

                    // (the stars fly to where the moon will appear and, once it has, follow it)
                    var at = moonGeometry(moonWindow, Math.Max(drawTime, moonWindow.Start), 1f, 1f);
                    moonWarnings.Add(new MoonWarning(moonWindow.Period.Start, moonWindow.Start, at.Moon, at.FullRadius, at.Source));
                }
            }

            moonLayer.UpdateWarnings(drawTime, moonWarnings);
        }

        #endregion
    }
}
