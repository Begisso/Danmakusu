using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    // Hecatia Lapislazuli: in her first attack she throws the Earth and the Moon at the player (they ricochet off the walls and off each other until the pause); in her second the Earth,
    // the Moon and the Sun turn round her (no ricochet). The Sun is there in every attack, turns round her all the time and fires her stars. The Earth and the Moon (not the Sun) push bullets away, with a flash, all three hurt. What
    // the worlds shoot out when they hit something (stage bullets) is left alone by them. In every attack the worlds
    // hang on chains of circle bullets that start at Hecatia and behave like chains (their length follows the distance, late). Every time the Earth or the Moon ricochets it throws circle bullets to both sides and a hit sound plays (the Earth's, the Moon's, or the one of the two hitting each other).
    // While they are thrown the Earth and the Moon gather what they touch (each its own, see Gathering) and shoot it all out at each impact, from their edges, in rings of four over 180 degrees away from the wall (or the other world).
    // A throw is never cut short: when the attack ends each of the two flies on to its next impact, and only then fades out or goes back to its orbit. But when the World's Ender comes (see WorldEnderAct) it takes the worlds over from where they are, and what they hold goes out.
    public partial class BulletField
    {
        private struct WorldEvent
        {
            public double Time;
            public bool Sound;
            public bool Played;

            /// <summary>Which world it happened to (0 Earth, 1 Moon, -1 none) and where on it, relative to its middle: that spot glows for a while.</summary>
            public int Body;

            public Vector2 Offset;

            /// <summary>Which sound: the Earth hits a wall (0), the Moon hits a wall (1), the two hit each other (2).</summary>
            public int Kind;
        }

        private sealed class WorldsAct : StageAct
        {
            /// <summary>The first kind of attack (true) throws the Earth and the Moon; the other (false) turns the three worlds round her.</summary>
            public bool Throw;

            /// <summary>The attacks of Hecatia follow each other without a pause: the act before / after this one (if it is joined to it), which this one takes over from / hands over to.</summary>
            public WorldsAct? Prev, Next;

            /// <summary>The World's Ender that follows this throw: it takes the Earth and the Moon over from where they are when the attack ends.</summary>
            public WorldEnderAct? Ender;

            private bool handedOver;

            /// <summary>Where the Sun is on its circle when this act starts (an act that is joined to the one before goes on from where that one left it), and which way it turns during this act (1 or -1: every turning attack turns the other way round).</summary>
            public float AngleStart = -MathF.PI / 2f;

            public float Direction = 1f;

            private bool known;
            private Vector2 origin;
            private Vector2 target;
            private double simTime;
            private readonly List<Vector2> earth = new List<Vector2>();
            private readonly List<Vector2> moon = new List<Vector2>();
            private Vector2 earthVelocity;
            private Vector2 moonVelocity;
            private readonly List<WorldEvent> events = new List<WorldEvent>();
            private int salt;
            private readonly double[] lastCrash = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity }; // (when each kind of hit sounded last)
            private readonly List<(double Time, Vector2 At)> pushFlashes = new List<(double, Vector2)>(); // (where a bullet has bounced off the Earth or the Moon)

            private Vector2[][]? chain;
            private Vector2[][]? chainOld;
            private double chainTime = double.NaN;

            private const double step = DanmakusuBalance.STAGE_STEP_MS;
            private const int nodes = DanmakusuBalance.HECATIA_CHAIN_BEADS + 2;

            private readonly Gathering gathering = new Gathering(2, DanmakusuBalance.HECATIA_GATHER_MAX);
            private readonly List<(double Time, int Body, Vector2 Away, Vector2 Centre)> impacts = new List<(double, int, Vector2, Vector2)>();
            private int impactNext;

            /// <summary>When the Earth / the Moon hit something for the first time after the attack ended (its last impact: after it the world only fades or goes back), and how it flew away from there.</summary>
            private readonly double[] stopAt = { double.NaN, double.NaN };

            private readonly Vector2[] stopVelocity = new Vector2[2];

            private double beatLength => Math.Max(Period.BeatLength, 1);

            /// <summary>A throw that no other throw carries on flies past its end, to the next impact of each world.</summary>
            public bool Overruns => Throw && !(Next != null && Next.Throw) && Ender == null;

            private double overrunLimit => End + beatLength * DanmakusuBalance.HECATIA_OVERRUN_BEATS;

            /// <summary>Until when the act has the Earth or the Moon (+infinity while it is still flying to its last impact and the simulation has not got there).</summary>
            public double StopOf(int body) => !Overruns || body > 1 ? End : double.IsNaN(stopAt[body]) ? double.PositiveInfinity : stopAt[body];

            /// <summary>How the world flew away from its last impact (px per ms).</summary>
            public Vector2 LeaveVelocity(BulletField f, int body) => body < 2 && !double.IsNaN(stopAt[body]) ? stopVelocity[body] : VelocityAt(f, body, StopOf(body));

            /// <summary>Until when the act does anything at all.</summary>
            private double lastActive => Overruns ? Math.Max(End, Math.Min(Math.Max(StopOf(0), StopOf(1)), overrunLimit)) : End;

            private double lastVisible => Next == null && Ender == null ? lastActive + DanmakusuBalance.STAGE_FADE_OUT_MS : lastActive;

            public override void Init(BulletField f)
            {
                AbsorbFrom = Start + 150;
                AbsorbTo = End + beatLength * DanmakusuBalance.HECATIA_OVERRUN_BEATS + DanmakusuBalance.STAGE_FADE_OUT_MS + 100;
            }

            public override bool Absorbs(double t) => t >= AbsorbFrom && t <= lastVisible + 100;

            public override bool AbsorbsBetween(double from, double to) => AbsorbFrom <= to && lastVisible + 100 >= from;

            /// <summary>
            /// 0..1: how much of the Earth (0) or the Moon (1) this act shows. After a throw each flies on to its next impact; then it fades out, or (when an attack of turning worlds follows) that act
            /// shows it from the end of this one on (it follows this flight until the impact).
            /// </summary>
            public float BodyAlpha(double t, int body)
            {
                if (body > 1 || !Overruns || t <= End)
                    return Alpha(t);

                if (Next != null)
                    return 0f;

                float fadeIn = Prev == null ? smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) : 1f;
                double stop = Math.Min(StopOf(body), overrunLimit);

                return t <= stop ? fadeIn : fadeIn * smoothStep((stop + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);
            }

            /// <summary>Does this act look after the Earth or the Moon at <paramref name="t"/>? (After a throw the flight to the last impact is the throw's, also while the next act already shows it.)</summary>
            private bool owns(int body, double t)
            {
                if (!Throw && Prev != null && Prev.Throw && t < Prev.StopOf(body))
                    return false;

                if (Overruns && Next != null && t > End)
                    return t <= StopOf(body);

                return BodyAlpha(t, body) >= 0.4f;
            }

            /// <summary>From when the world goes back to its orbit (in an attack of turning worlds): when it starts, or when the throw before has had its last impact.</summary>
            private double returnFrom(int body) => Prev != null && Prev.Throw ? Math.Max(Start, Prev.StopOf(body)) : Start;

            public float Radius(BulletField f, int body) => f.circleRadius * (body == 0 ? DanmakusuBalance.HECATIA_EARTH_RADIUS : body == 1 ? DanmakusuBalance.HECATIA_MOON_RADIUS : DanmakusuBalance.HECATIA_SUN_RADIUS);

            /// <summary>0..1: how much of the worlds is there (they fade in when the attack starts and out a moment after it ends, unless another attack of hers joins on: then they go on).</summary>
            public float Alpha(double t)
            {
                double last = Next == null && Ender == null ? End + DanmakusuBalance.STAGE_FADE_OUT_MS : End;

                if (t < Start || t > last)
                    return 0f;

                float fadeIn = Prev == null ? smoothStep((t - Start) / DanmakusuBalance.STAGE_FADE_IN_MS) : 1f;
                float fadeOut = Next == null && Ender == null ? smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS) : 1f;
                return fadeIn * fadeOut;
            }

            /// <summary>How big the Sun is (0..1): it grows out of Hecatia when the worlds start to turn round her and shrinks back into her when they stop.</summary>
            public float SunScale(double t)
                => Math.Min(Prev == null ? smoothStep((t - Start) / DanmakusuBalance.HECATIA_TRANSITION_MS) : 1f, Next == null && Ender == null ? smoothStep((End - t) / DanmakusuBalance.HECATIA_TRANSITION_MS) : 1f);

            /// <summary>The angle the Sun is at on the circle round Hecatia: it turns all the time, through every attack (the Earth and the Moon follow it, 120 degrees apart, in the attacks that turn them too).</summary>
            public float SunAngle(double t)
            {
                double beat = Math.Max(Period.BeatLength, 1);
                return AngleStart + Direction * (float)(Math.Tau * (t - Start) / (DanmakusuBalance.HECATIA_ORBIT_BEATS * beat));
            }

            /// <summary>The size of a world (the Sun's changes).</summary>
            public float RadiusAt(BulletField f, int body, double t) => Radius(f, body) * (body == 2 ? SunScale(t) : 1f);

            /// <summary>
            /// Where a world is while the three turn round <paramref name="centre"/>: 120 degrees apart, one turn every few beats. The Sun grows out of the boss; the Earth and the Moon
            /// come from where the throw before left them (or out of the boss, if there was none) and settle into their places.
            /// </summary>
            public Vector2 OrbitBody(BulletField f, int body, double t, Vector2 centre)
            {
                float angle = SunAngle(t) + (body - 2) * (MathF.Tau / 3f);
                Vector2 place = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (DanmakusuBalance.HECATIA_ORBIT_RADIUS * f.circleRadius);
                double u = Math.Clamp((t - Start) / DanmakusuBalance.HECATIA_RETURN_MS, 0.0, 1.0);

                if (body == 2)
                    return Vector2.Lerp(centre, place, SunScale(t));

                if (Prev != null && Prev.Throw)
                {
                    // the throw flies on to its next impact first (it is never cut short)
                    double stop = Math.Max(Prev.StopOf(body), Start);

                    if (t < stop)
                        return Prev.BodyAt(f, body, t);

                    // then it goes on the way it flew away from there, slowing down (so there is no jerk), and is drawn into its place more and more
                    double coast = DanmakusuBalance.HECATIA_COAST_MS;
                    double flown = coast * (1.0 - Math.Exp(-(t - stop) / coast));
                    Vector2 drifting = Prev.BodyAt(f, body, stop) + Prev.LeaveVelocity(f, body) * (float)flown;
                    return Vector2.Lerp(drifting, place, smoothStep(Math.Clamp((t - stop) / DanmakusuBalance.HECATIA_RETURN_MS, 0.0, 1.0)));
                }

                return Vector2.Lerp(centre, place, smoothStep(u));
            }

            public Vector2 BodyAt(BulletField f, int body, double t)
            {
                if (!Throw || body == 2)
                    return OrbitBody(f, body, t, f.bossVisualOf(Period));

                if (earth.Count == 0)
                    return origin;

                return at(body == 0 ? earth : moon, t);
            }

            /// <summary>How a world flies while it is thrown (px per ms; the average of the last few ms).</summary>
            public Vector2 VelocityAt(BulletField f, int body, double t)
            {
                if (!Throw || earth.Count == 0)
                    return Vector2.Zero;

                const double span = 40;
                return (BodyAt(f, body, t) - BodyAt(f, body, t - span)) / (float)span;
            }

            private Vector2 at(List<Vector2> list, double t)
            {
                double x = (t - Start) / step;

                if (x <= 0)
                    return list[0];

                int i = (int)x;

                if (i >= list.Count - 1)
                    return list[list.Count - 1];

                return Vector2.Lerp(list[i], list[i + 1], (float)(x - i));
            }

            public override void Update(BulletField f, double now)
            {
                if (now < Start)
                {
                    known = false;
                    return;
                }

                if (now > lastVisible + 50)
                    return;

                if (Throw)
                {
                    if (!known)
                        capture(f);

                    advance(f, now);
                    playSounds(f, now);
                }

                advanceChain(f, now);
            }

            /// <summary>After the bullets have been taken in: every impact up to now shoots out what its world has gathered until then.</summary>
            public override void Late(BulletField f, double now)
            {
                float speed = DanmakusuBalance.GATHER_SPEED * stageBulletSpeed(Period);

                while (impactNext < impacts.Count && impacts[impactNext].Time <= now)
                {
                    var (time, body, away, centre) = impacts[impactNext++];
                    gathering.Release(this, f, time, body, away, centre, speed, beatLength * DanmakusuBalance.GATHER_RING_GAP_BEATS, edge: RadiusAt(f, body, time));
                }

                // (the World's Ender takes the worlds over when the attack ends: what the Earth and the Moon hold goes out then, towards the middle, and the chains break: their links fly off as bullets)
                if (Ender != null && !handedOver && now >= End)
                {
                    handedOver = true;
                    Vector2 middle = (f.stageMin + f.stageMax) * 0.5f;

                    if (Throw && earth.Count > 0)
                    {
                        for (int body = 0; body < 2; body++)
                        {
                            Vector2 at = BodyAt(f, body, End), toMiddle = middle - at;
                            gathering.Release(this, f, End, body, toMiddle.LengthSquared > 1f ? Vector2.Normalize(toMiddle) : new Vector2(0f, 1f), at, speed, beatLength * DanmakusuBalance.GATHER_RING_GAP_BEATS, edge: RadiusAt(f, body, End));
                        }
                    }

                    releaseChains(f);
                }
            }

            public override void Reset(BulletField f, double time)
            {
                gathering.Reset(this, time);
                handedOver = time >= End;
                pushFlashes.RemoveAll(flash => flash.Time > time);
                impactNext = impacts.FindIndex(impact => impact.Time > time);

                if (impactNext < 0)
                    impactNext = impacts.Count;

                chainTime = double.NaN;
                lastCrash[0] = lastCrash[1] = lastCrash[2] = double.NegativeInfinity;

                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];

                    if (ev.Time > time && ev.Played)
                    {
                        ev.Played = false;
                        events[i] = ev;
                    }
                }
            }

            private Vector2 aimFrom(Vector2 position)
            {
                Vector2 toTarget = target - position;
                return toTarget.LengthSquared > 1e-4f ? Vector2.Normalize(toTarget) : new Vector2(0, 1);
            }

            /// <summary>The attack begins: she throws the Earth and the Moon at where the player is.</summary>
            private void capture(BulletField f)
            {
                known = true;
                origin = f.bossPositionOf(Period);
                target = f.characterPosition;

                Vector2 low = f.stageMin, high = f.stageMax;
                float re = Radius(f, 0), rm = Radius(f, 1);

                // (after an attack of turning worlds they are thrown from where they are; otherwise from the boss)
                Vector2 fromEarth = origin, fromMoon = origin;

                if (Prev != null && !Prev.Throw)
                {
                    fromEarth = Prev.BodyAt(f, 0, Start);
                    fromMoon = Prev.BodyAt(f, 1, Start);
                }

                // (a world that comes out of an orbit leaves from exactly where it is, even if that is partly off the screen: it flies in by itself; only a start from the boss is kept inside)
                bool fromOrbit = Prev != null && !Prev.Throw;
                Vector2 startEarth = fromOrbit ? fromEarth : Vector2.Clamp(fromEarth, low + new Vector2(re), Vector2.ComponentMax(low + new Vector2(re), high - new Vector2(re)));
                Vector2 startMoon = fromOrbit ? fromMoon : Vector2.Clamp(fromMoon, low + new Vector2(rm), Vector2.ComponentMax(low + new Vector2(rm), high - new Vector2(rm)));

                // (thrown from the same place they would lie on top of each other: they start side by side, across the way they are thrown, with a little room between them)
                float room = re + rm + f.circleRadius * 0.4f;

                if (Vector2.Distance(startEarth, startMoon) < room)
                {
                    Vector2 forward = aimFrom((startEarth + startMoon) / 2f);
                    Vector2 side = new Vector2(-forward.Y, forward.X);
                    Vector2 middle = (startEarth + startMoon) / 2f;
                    startEarth = middle - side * (room / 2f);
                    startMoon = middle + side * (room / 2f);

                    if (!fromOrbit)
                    {
                        // (kept inside the screen; if one of them is pressed against an edge the other one gives way)
                        startEarth = Vector2.Clamp(startEarth, low + new Vector2(re), Vector2.ComponentMax(low + new Vector2(re), high - new Vector2(re)));
                        startMoon = Vector2.Clamp(startMoon, low + new Vector2(rm), Vector2.ComponentMax(low + new Vector2(rm), high - new Vector2(rm)));

                        Vector2 apart = startMoon - startEarth;

                        if (apart.Length < room)
                        {
                            Vector2 push = apart.LengthSquared > 1e-6f ? Vector2.Normalize(apart) : side;
                            startMoon = startEarth + push * room;
                            startMoon = Vector2.Clamp(startMoon, low + new Vector2(rm), Vector2.ComponentMax(low + new Vector2(rm), high - new Vector2(rm)));
                            apart = startMoon - startEarth;

                            if (apart.Length < room)
                                startEarth = startMoon - (apart.LengthSquared > 1e-6f ? Vector2.Normalize(apart) : push) * room;
                        }
                    }
                }

                float speed = DanmakusuBalance.HECATIA_THROW_SPEED * stageBulletSpeed(Period);

                earthVelocity = turned(aimFrom(startEarth), -DanmakusuBalance.HECATIA_THROW_SPREAD) * speed;
                moonVelocity = turned(aimFrom(startMoon), DanmakusuBalance.HECATIA_THROW_SPREAD) * (speed * DanmakusuBalance.HECATIA_MOON_SPEED_FACTOR);

                earthSpeed = earthVelocity.Length;
                moonSpeed = moonVelocity.Length;

                // (a throw that follows a throw, in the same section or after a spinner, carries on with the Earth and the Moon where they are and the way they fly: nothing is reset - but at the speed this throw has)
                if (Prev != null && Prev.Throw && Prev.earth.Count > 0)
                {
                    startEarth = Prev.BodyAt(f, 0, Start);
                    startMoon = Prev.BodyAt(f, 1, Start);
                    earthVelocity = keptSpeed(Prev.VelocityAt(f, 0, Start), earthVelocity, earthSpeed);
                    moonVelocity = keptSpeed(Prev.VelocityAt(f, 1, Start), moonVelocity, moonSpeed);
                }

                earth.Clear();
                moon.Clear();
                earth.Add(startEarth);
                moon.Add(startMoon);
                events.Clear();

                // (the worlds are thrown: one hit sound, unless this throw only carries on from the one before)
                if (!(Prev != null && Prev.Throw))
                    events.Add(new WorldEvent { Time = Start, Sound = true, Kind = 2, Body = -1 });

                ClearBullets();
                gathering.Clear();
                impacts.Clear();
                impactNext = 0;
                stopAt[0] = stopAt[1] = double.NaN;
                simTime = Start;
                salt = 0;
                chainTime = double.NaN;
            }

            /// <summary>Moves the Earth and the Moon on in steps of a few ms: they bounce off the walls (a ball in a box) and off each other (an elastic collision, the mass follows the area).</summary>
            private void advance(BulletField f, double to)
            {
                double limit = Math.Min(to, (Overruns ? overrunLimit : End) + DanmakusuBalance.STAGE_FADE_OUT_MS + step);
                Vector2 low = f.stageMin, high = f.stageMax;
                float re = Radius(f, 0), rm = Radius(f, 1);
                float dt = (float)step;
                int guard = 0;

                while (simTime + step <= limit && guard++ < 6000)
                {
                    simTime += step;

                    Vector2 pe = earth[earth.Count - 1] + earthVelocity * dt;
                    Vector2 pm = moon[moon.Count - 1] + moonVelocity * dt;

                    bounceWalls(f, ref pe, ref earthVelocity, re, low, high, 0);
                    bounceWalls(f, ref pm, ref moonVelocity, rm, low, high, 1);

                    Vector2 d = pm - pe;
                    float distance = d.Length;

                    // (a world that has had its last impact is out of the game: the other one does not hit it any more)
                    bool bothIn = !(stopped(0) || stopped(1));

                    if (bothIn && distance > 1e-3f && distance < re + rm)
                    {
                        Vector2 n = d / distance;
                        float closing = Vector2.Dot(earthVelocity - moonVelocity, n);

                        if (closing > 0f)
                        {
                            float me = re * re, mm = rm * rm;
                            float impulse = 2f * closing / (1f / me + 1f / mm);
                            Vector2 earthBefore = earthVelocity, moonBefore = moonVelocity;
                            earthVelocity -= n * (impulse / me);
                            moonVelocity += n * (impulse / mm);

                            // (they bounce off each other the ways an elastic hit sends them, but each keeps the speed it was thrown at: by the masses the light Moon came away from the Earth up to twice as fast,
                            // and faster again at every hit after)
                            earthVelocity = keptSpeed(earthVelocity, earthBefore, earthSpeed);
                            moonVelocity = keptSpeed(moonVelocity, moonBefore, moonSpeed);

                            float overlap = re + rm - distance;
                            pe -= n * (overlap * mm / (me + mm));
                            pm += n * (overlap * me / (me + mm));

                            Vector2 tangent = new Vector2(-n.Y, n.X);
                            ricochet(f, pe + n * re, tangent, simTime, true, 2, 0, pe);
                            ricochet(f, pm - n * rm, tangent, simTime, false, 2, 1, pm);
                        }
                    }

                    earth.Add(pe);
                    moon.Add(pm);

                    // (one that has not hit anything for long after the attack stops where it is in the air)
                    if (Overruns && simTime >= overrunLimit)
                    {
                        for (int body = 0; body < 2; body++)
                        {
                            if (double.IsNaN(stopAt[body]))
                            {
                                stopAt[body] = simTime;
                                stopVelocity[body] = body == 0 ? earthVelocity : moonVelocity;
                            }
                        }
                    }
                }
            }

            private float earthSpeed, moonSpeed;

            /// <summary>The way of <paramref name="way"/> (or of <paramref name="otherwise"/>, if it has none) at <paramref name="speed"/>: a world never flies faster or slower than it was thrown.</summary>
            private static Vector2 keptSpeed(Vector2 way, Vector2 otherwise, float speed)
            {
                Vector2 v = way.LengthSquared > 1e-10f ? way : otherwise;
                return v.LengthSquared > 1e-10f ? Vector2.Normalize(v) * speed : v;
            }

            /// <summary>Has the world had its last impact (in the simulation, by now)?</summary>
            private bool stopped(int body) => Overruns && !double.IsNaN(stopAt[body]) && simTime > stopAt[body];

            private void bounceWalls(BulletField f, ref Vector2 p, ref Vector2 v, float r, Vector2 low, Vector2 high, int kind)
            {
                float x0 = low.X + r, x1 = high.X - r, y0 = low.Y + r, y1 = high.Y - r;

                if (x1 <= x0 || y1 <= y0)
                    return; // (the screen is smaller than the world)

                if (p.X < x0 && v.X < 0f)
                {
                    p.X = x0 + (x0 - p.X);
                    v.X = -v.X;
                    ricochet(f, new Vector2(low.X, p.Y), new Vector2(0, 1), simTime, true, kind, kind, p);
                }
                else if (p.X > x1 && v.X > 0f)
                {
                    p.X = x1 - (p.X - x1);
                    v.X = -v.X;
                    ricochet(f, new Vector2(high.X, p.Y), new Vector2(0, 1), simTime, true, kind, kind, p);
                }

                if (p.Y < y0 && v.Y < 0f)
                {
                    p.Y = y0 + (y0 - p.Y);
                    v.Y = -v.Y;
                    ricochet(f, new Vector2(p.X, low.Y), new Vector2(1, 0), simTime, true, kind, kind, p);
                }
                else if (p.Y > y1 && v.Y > 0f)
                {
                    p.Y = y1 - (p.Y - y1);
                    v.Y = -v.Y;
                    ricochet(f, new Vector2(p.X, high.Y), new Vector2(1, 0), simTime, true, kind, kind, p);
                }
            }

            /// <summary>A world ricochets: it throws circle bullets to both sides of the place it hit (along the wall, or along the other world's surface), and a hit sound plays (the Earth's, the Moon's, or the one of the two hitting each other).</summary>
            private void ricochet(BulletField f, Vector2 position, Vector2 tangent, double time, bool sound, int kind, int body, Vector2 centre)
            {
                if (Overruns && time > End)
                {
                    // after the attack: the first impact is the world's last (it still throws and shoots out what it holds), after it the world only flies on
                    if (!double.IsNaN(stopAt[body]))
                    {
                        if (time > stopAt[body])
                            return;
                    }
                    else
                    {
                        stopAt[body] = time;
                        stopVelocity[body] = body == 0 ? earthVelocity : moonVelocity;
                    }
                }

                // (a ricochet: it flashes where it hit)
                f.addFlash(time, position, f.circleRadius * DanmakusuBalance.RICOCHET_FLASH_SIZE * 0.5f);

                // what the world holds goes out away from what it hit
                Vector2 away = centre - position;
                impacts.Add((time, body, away.LengthSquared > 1e-6f ? Vector2.Normalize(away) : new Vector2(0f, 1f), centre));

                events.Add(new WorldEvent { Time = time, Sound = sound, Kind = kind, Body = body, Offset = position - centre });
                salt++;

                float baseAngle = MathF.Atan2(tangent.Y, tangent.X);
                float radius = f.stageBaseRadius(Period);
                float speed = DanmakusuBalance.HECATIA_SIDE_SPEED * stageBulletSpeed(Period);
                int each = DanmakusuBalance.HECATIA_SIDE_BULLETS;

                for (int side = 0; side < 2; side++)
                {
                    for (int k = 0; k < each; k++)
                    {
                        float angle = baseAngle + (side == 0 ? 0f : MathF.PI) + (k - (each - 1) / 2f) * (DanmakusuBalance.HECATIA_SIDE_FAN / Math.Max(each - 1, 1));
                        Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        AddBullet(new StageBullet
                        {
                            T0 = time,
                            P0 = position,
                            V = direction * speed,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = (salt * 7 + side * 3 + k) % f.palette.Length,
                            Shape = BulletShape.Circle,
                            Tint = BulletRenderer.TINT_ORANGE,
                            TintAmount = 1f,
                            Until = time + f.stageLife(direction * speed),
                        });
                    }
                }
            }

            private void playSounds(BulletField f, double now)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];

                    if (ev.Played || ev.Time > now)
                        continue;

                    ev.Played = true;
                    events[i] = ev;

                    if (!ev.Sound || now - ev.Time >= 250 || ev.Time - lastCrash[ev.Kind] < 80)
                        continue;

                    lastCrash[ev.Kind] = ev.Time;
                    f.playStageSound(ev.Kind == 0 ? "hecatia_earth_hit" : ev.Kind == 1 ? "hecatia_moon_hit" : "hecatia_world_hit");
                }
            }

            // ---- the chains ----
            // every world hangs on a chain that starts at Hecatia. A chain has a length that follows the distance to its world with a delay: while the world flies away faster than the chain
            // grows the chain is taut and only the first part of it (from Hecatia) is there; when the world comes back the chain is longer than the distance and hangs loose until it has shortened.

            private float[]? chainLength;

            private int links => 3; // (the Sun hangs on a chain in every attack)

            /// <summary>The two ends of a chain: Hecatia, and the surface of the world (on the side that faces her).</summary>
            private void ends(BulletField f, int link, double t, out Vector2 endA, out Vector2 endB)
            {
                Vector2 pa = f.bossVisualOf(Period), pb = BodyAt(f, link, t);
                Vector2 d = pb - pa;
                float length = d.Length;
                Vector2 unit = length > 1e-4f ? d / length : new Vector2(1, 0);

                endA = pa;
                float surface = RadiusAt(f, link, t);
                endB = surface < length ? pb - unit * surface : pa;
            }

            private void initChain(BulletField f)
            {
                chain ??= new Vector2[3][];
                chainOld ??= new Vector2[3][];
                chainLength = new float[3];

                // the chains go on from where the attack before left them
                bool takeOver = Prev != null && Prev.chain != null && Prev.chainLength != null && !double.IsNaN(Prev.chainTime) && Prev.chainTime >= Start - 100 && Prev.chainTime <= Start + 400;

                for (int link = 0; link < 3; link++)
                {
                    chain[link] = new Vector2[nodes];
                    chainOld[link] = new Vector2[nodes];

                    if (link >= links)
                        continue;

                    if (takeOver && link < Prev!.links)
                    {
                        Array.Copy(Prev.chain![link], chain[link], nodes);
                        Array.Copy(Prev.chain[link], chainOld[link], nodes);
                        chainLength[link] = Prev.chainLength![link];
                        continue;
                    }

                    ends(f, link, Start, out Vector2 a, out Vector2 b);
                    Vector2 d = b - a;
                    float chord = d.Length;
                    Vector2 unit = chord > 1e-4f ? d / chord : new Vector2(1, 0);
                    Vector2 side = new Vector2(-unit.Y, unit.X);
                    float rest = chord * (1f + DanmakusuBalance.HECATIA_CHAIN_SLACK);
                    chainLength[link] = rest;
                    float sag = 0.6f * MathF.Sqrt(Math.Max(0f, rest * rest - chord * chord)) * 0.5f;

                    for (int i = 0; i < nodes; i++)
                    {
                        float u = i / (float)(nodes - 1);
                        Vector2 p = Vector2.Lerp(a, b, u) + side * (MathF.Sin(u * MathF.PI) * sag);
                        chain[link][i] = p;
                        chainOld[link][i] = p;
                    }
                }

                chainTime = Start;
            }

            private void advanceChain(BulletField f, double now)
            {
                double target = Math.Min(now, lastVisible);

                if (double.IsNaN(chainTime) || chain == null || chainLength == null || target < chainTime - 1)
                    initChain(f);

                int guard = 0;

                while (chainTime + step <= target && guard++ < 6000)
                {
                    chainTime += step;

                    for (int link = 0; link < links; link++)
                        stepChain(f, link, chainTime);
                }
            }

            private void stepChain(BulletField f, int link, double t)
            {
                var x = chain![link];
                var old = chainOld![link];

                ends(f, link, t, out Vector2 a, out Vector2 b);

                // the length follows the distance, late
                float wanted = Vector2.Distance(a, b) * (1f + DanmakusuBalance.HECATIA_CHAIN_SLACK);
                float follow = 1f - MathF.Exp((float)(-step / DanmakusuBalance.HECATIA_CHAIN_LAG_MS));
                chainLength![link] += (wanted - chainLength[link]) * follow;
                float h = Math.Max(chainLength[link], 1f) / (nodes - 1);

                for (int i = 1; i < nodes - 1; i++)
                {
                    Vector2 v = (x[i] - old[i]) * DanmakusuBalance.HECATIA_CHAIN_DAMPING;
                    old[i] = x[i];
                    x[i] += v;
                }

                x[0] = old[0] = a;
                x[nodes - 1] = old[nodes - 1] = b;

                for (int iteration = 0; iteration < 10; iteration++)
                {
                    for (int i = 0; i + 1 < nodes; i++)
                    {
                        Vector2 d = x[i + 1] - x[i];
                        float length = d.Length;

                        if (length < 1e-5f)
                            continue;

                        // (the links keep their length: when the chain is longer than the distance it bows out and hangs loose)
                        bool fixedStart = i == 0, fixedEnd = i + 1 == nodes - 1;

                        if (fixedStart && fixedEnd)
                            continue;

                        Vector2 correction = d * ((length - h) / length);

                        if (fixedStart)
                            x[i + 1] -= correction;
                        else if (fixedEnd)
                            x[i] += correction;
                        else
                        {
                            x[i] += correction * 0.5f;
                            x[i + 1] -= correction * 0.5f;
                        }
                    }
                }
            }

            /// <summary>The chains break: every link flies off as a bullet from where it hangs, away from her, each at a speed of its own (as it is shown in <see cref="Beads"/>, so nothing jumps).</summary>
            private void releaseChains(BulletField f)
            {
                if (chain == null || chainLength == null || double.IsNaN(chainTime))
                    return;

                float radius = DanmakusuBalance.HECATIA_BEAD_RADIUS * f.circleRadius;
                float spacing = radius * 2.4f;
                float speed = stageBulletSpeed(Period);
                int style = f.backgroundStyle >= 0 ? f.backgroundStyle : 0;
                Vector2 boss = f.bossVisualOf(Period);
                int number = 0;

                for (int link = 0; link < links; link++)
                {
                    var x = chain[link];
                    float reachEnd = chainLength[link], next = spacing, walked = 0f;

                    for (int i = 0; i + 1 < nodes; i++)
                    {
                        float length = Vector2.Distance(x[i], x[i + 1]);

                        while (next <= walked + length && next <= reachEnd)
                        {
                            Vector2 at = Vector2.Lerp(x[i], x[i + 1], length > 1e-6f ? (next - walked) / length : 0f);
                            Vector2 away = at - boss;
                            Vector2 way = away.LengthSquared > 1f ? Vector2.Normalize(away) : new Vector2(MathF.Cos(number), MathF.Sin(number));
                            Vector2 velocity = way * (speed * (DanmakusuBalance.ENDER_CHAIN_SLOWEST + (DanmakusuBalance.ENDER_CHAIN_FASTEST - DanmakusuBalance.ENDER_CHAIN_SLOWEST) * hash01(number, link, 91)));

                            AddBullet(new StageBullet
                            {
                                T0 = End,
                                P0 = at,
                                V = velocity,
                                Face = way,
                                Radius = radius,
                                Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                                Style = style,
                                Shape = BulletShape.Circle,
                                Until = End + f.stageLife(velocity),
                            });

                            number++;
                            next += spacing;
                        }

                        walked += length;

                        if (next > reachEnd)
                            break;
                    }
                }
            }

            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                // what the Earth and the Moon hold lies under them (also while the next act shows them on their way to the last impact)
                if (Throw)
                {
                    for (int body = 0; body < 2; body++)
                    {
                        float held = t <= StopOf(body) && t >= Start ? Math.Max(BodyAlpha(t, body), Overruns && Next != null && t > End ? 1f : 0f) : BodyAlpha(t, body);
                        gathering.Beads(body, t, BodyAt(f, body, t), Radius(f, body), spinOf(body, t) * MathF.PI / 180f, held, into);
                    }
                }

                if (chain == null || chainLength == null || double.IsNaN(chainTime))
                    return;

                float radius = DanmakusuBalance.HECATIA_BEAD_RADIUS * f.circleRadius;
                float spacing = radius * 2.4f;
                int style = f.backgroundStyle >= 0 ? f.backgroundStyle : 0;

                for (int link = 0; link < links; link++)
                {
                    float alpha = BodyAlpha(t, link);

                    if (alpha <= 0.01f)
                        continue;

                    var x = chain[link];
                    float reachEnd = chainLength[link]; // (only this much of the chain, counted from Hecatia, is there)
                    float next = spacing;
                    float walked = 0f;

                    for (int i = 0; i + 1 < nodes && into.Count < 400; i++)
                    {
                        float length = Vector2.Distance(x[i], x[i + 1]);

                        while (next <= walked + length && next <= reachEnd && into.Count < 400)
                        {
                            float u = length > 1e-6f ? (next - walked) / length : 0f;
                            into.Add(new StageBead { Position = Vector2.Lerp(x[i], x[i + 1], u), Radius = radius, Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE, Fade = 1f - alpha, Style = style });
                            next += spacing;
                        }

                        walked += length;

                        if (next > reachEnd)
                            break;
                    }
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                for (int body = 0; body < 2; body++)
                {
                    if (!owns(body, t) || distanceToSegment(BodyAt(f, body, t), from, to) > Radius(f, body) + emitter.HitRadius)
                        continue;

                    // while they are thrown the Earth and the Moon gather everything they touch (until the last impact)
                    if (Throw && t <= StopOf(body) && Gathering.Gatherable(emitter, bullet) && gathering.Take(f, t, body, emitter, bullet))
                        return true;

                    // otherwise (turning round her, or full) bullets that cannot be turned (falling, ricocheting, homing, bullets with their own rules) are taken away, the others are pushed
                    if (!emitter.CanBeDeflected)
                        return true;
                }

                return false;
            }

            /// <summary>The Earth and the Moon (not the Sun) push the bullets they touch away: the bullet flies on from the surface, straight away from the world, faster than the world itself moves.</summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                if (!emitter.CanBeDeflected)
                    return false;

                for (int body = 0; body < 2; body++)
                {
                    if (!owns(body, t))
                        continue;

                    // while the worlds come back to her (settling into their orbits) they do not push anything
                    if (!Throw && t - returnFrom(body) < DanmakusuBalance.HECATIA_RETURN_MS)
                        continue;

                    Vector2 centre = BodyAt(f, body, t);
                    float reach = Radius(f, body) + emitter.HitRadius;

                    if (distanceToSegment(centre, from, to) > reach)
                        continue;

                    // the point of the bullet's path that is nearest to the world's middle is where it was touched
                    Vector2 seg = to - from;
                    float len2 = seg.LengthSquared;
                    float u = len2 > 1e-8f ? Math.Clamp(Vector2.Dot(centre - from, seg) / len2, 0f, 1f) : 1f;
                    Vector2 near = from + seg * u;
                    Vector2 away = near - centre;

                    if (away.LengthSquared < 1e-6f)
                        away = to - centre;

                    if (away.LengthSquared < 1e-6f)
                        away = new Vector2(1, 0);

                    direction = Vector2.Normalize(away);
                    position = centre + direction * (reach + DanmakusuBalance.HECATIA_PUSH_CLEARANCE);

                    const double span = 24;
                    float worldSpeed = ((BodyAt(f, body, t) - BodyAt(f, body, t - span)) / (float)span).Length;
                    speed = Math.Max(emitter.SpeedOf(bullet) * DanmakusuBalance.HECATIA_PUSH_SPEED_FACTOR, worldSpeed * DanmakusuBalance.HECATIA_PUSH_OUTRUN);
                    pushFlashes.Add((t, centre + direction * Radius(f, body)));
                    return true;
                }

                return false;
            }

            /// <summary>How far a world's picture has turned (degrees).</summary>
            public static float spinOf(int body, double t) => (float)(t * 0.012 * (body == 0 ? 1.0 : body == 1 ? -1.4 : 0.6));

            public override bool Hurts(BulletField f, double now)
            {
                for (int body = 0; body < 3; body++)
                {
                    if (BodyAlpha(now, body) < 0.5f)
                        continue;

                    if (Vector2.Distance(f.characterPosition, BodyAt(f, body, now)) <= RadiusAt(f, body, now) + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                // a flash where a bullet has bounced off the Earth or the Moon (above the bullets)
                for (int i = pushFlashes.Count - 1; i >= 0; i--)
                {
                    double age = t - pushFlashes[i].Time;

                    if (age > DanmakusuBalance.HECATIA_PUSH_FLASH_MS)
                        break;

                    if (age < 0)
                        continue;

                    float fade = 1f - (float)(age / DanmakusuBalance.HECATIA_PUSH_FLASH_MS);
                    f.stageTree.Add(StageTexture.Glow, pushFlashes[i].At, new Vector2(f.circleRadius * DanmakusuBalance.HECATIA_PUSH_FLASH_SIZE * (0.6f + 0.4f * fade)), 0f, fade * fade, new Color4(1f, 0.92f, 0.7f, 1f), true);
                }

                // the bullets that the worlds throw when they hit something glow a little (under the bullets themselves)
                if (Bullets.Count > 0 && t >= FirstT0 && t <= LastUntil && !f.failed)
                {
                    for (int i = 0; i < Bullets.Count; i++)
                    {
                        var thrown = Bullets[i];

                        if (t < thrown.T0 || t > thrown.Until)
                            continue;

                        if (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + i))
                            continue;

                        f.stageBack.Add(StageTexture.Glow, stageBulletPosition(thrown, t), new Vector2(thrown.Radius * 3.4f), 0f, 0.3f, new Color4(1f, 0.68f, 0.32f, 1f), true);
                    }
                }

                for (int body = 0; body < 3; body++)
                {
                    float alpha = BodyAlpha(t, body);

                    if (alpha <= 0.002f)
                        continue;

                    Vector2 p = BodyAt(f, body, t);
                    float r = RadiusAt(f, body, t);
                    var texture = body == 0 ? StageTexture.Earth : body == 1 ? StageTexture.Moon : StageTexture.Sun;
                    float spin = spinOf(body, t);

                    if (body == 2)
                        f.stageFront.Add(StageTexture.Glow, p, new Vector2(r * 4.4f), 0f, 0.5f * alpha, new Color4(1f, 0.65f, 0.25f, 1f), true);

                    f.stageFront.Add(texture, p, new Vector2(r * 2f), spin, alpha, Color4.White);

                    // the spot of the Earth / Moon that has just hit a wall or the other one glows orange for a moment
                    if (body < 2 && Throw)
                    {
                        for (int e = events.Count - 1; e >= 0; e--)
                        {
                            var ev = events[e];
                            double age = t - ev.Time;

                            if (ev.Body != body || age < 0)
                                continue;

                            if (age > DanmakusuBalance.HECATIA_GLOW_MS)
                                break;

                            float fade = 1f - (float)(age / DanmakusuBalance.HECATIA_GLOW_MS);
                            f.stageFront.Add(StageTexture.Glow, p + ev.Offset * 0.75f, new Vector2(r * 1.5f), 0f, fade * fade * alpha, new Color4(1f, 0.55f, 0.12f, 1f), true);
                        }
                    }
                }
            }
        }
    }
}
