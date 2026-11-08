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
        // Okina Matara: during an attack a part of the wall behind her is a portal (the wall of the screen that she stands nearest to: the top one, if she stands in the middle), and a second portal
        // opens in the wall opposite to it, behind the player. Every bullet that reaches the first portal (the ones that would ricochet off the wall too) goes through and comes out of the second
        // one, flying on the way it flew (so towards the player's back): where it comes out is as far from the middle of that portal as where it went in was from the middle of the first.
        // Both portals follow along the border of the screen: the first Okina, the second the player; when Okina goes to another side they slide there (round the corners), they never jump. A portal that is near a corner bends round it (an L): the part that does not fit on the wall goes on along the next one.
        // The portals grow to their size during the warning that leads into the attack, and the whole screen shakes a little meanwhile.
        // ======================================================================================================================================================

        private sealed class PortalAct : StageAct
        {
            private readonly BulletField field;
            private bool known;
            private int entryWall, exitWall; // (0 = top, 1 = right, 2 = bottom, 3 = left: the wall that the middle of each portal is on)
            private float entryAt, exitAt; // (where the middle of each portal is along its wall)
            private float entryS, exitS; // (where the middle of each portal is along the border of the screen: it slides along it, round the corners, so a portal never jumps from one wall to another)
            private double lastUpdate;

            private readonly List<Seg> entryPath = new List<Seg>();
            private readonly List<Seg> exitPath = new List<Seg>();
            private readonly List<Seg> drawPath = new List<Seg>();

            /// <summary>
            /// One straight piece of a portal, on one wall. The portal as a whole is a path of up to three of them (the part that bends round a corner, the part on the wall, the part that bends round the other corner), with
            /// a position <c>s</c> along it that is 0 in the middle: a piece covers s from <see cref="S0"/> to S0 + <see cref="Len"/>, and at s its place along the wall is Along0 + AlongDir x (s - S0).
            /// </summary>
            private struct Seg
            {
                public int Wall;
                public float Along0;
                public float AlongDir;
                public float S0;
                public float Len;
            }

            public PortalAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            private static bool alongX(int w) => w == 0 || w == 2; // (the wall runs along the x axis)

            private static float inward(int w) => w == 0 || w == 3 ? 1f : -1f;

            /// <summary>The way out of the screen through wall <paramref name="w"/>.</summary>
            private static Vector2 outward(int w) => w switch
            {
                0 => new Vector2(0f, -1f),
                1 => new Vector2(1f, 0f),
                2 => new Vector2(0f, 1f),
                _ => new Vector2(-1f, 0f),
            };

            private static float edge(BulletField f, int w) => w switch
            {
                0 => f.stageMin.Y,
                2 => f.stageMax.Y,
                3 => f.stageMin.X,
                _ => f.stageMax.X,
            };

            private static float tangentOf(int w, Vector2 p) => alongX(w) ? p.X : p.Y;

            private static float normalOf(int w, Vector2 p) => alongX(w) ? p.Y : p.X;

            private static float extent(BulletField f, int w) => alongX(w) ? f.stageMax.X - f.stageMin.X : f.stageMax.Y - f.stageMin.Y;

            /// <summary>A portal is as long (in px) on every wall: as long as one on the sides of the screen, which is a share of the screen's height.</summary>
            private static float halfLength(BulletField f, int w) => (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.OKINA_PORTAL_LENGTH * 0.5f;

            private static Vector2 pointOn(int w, float along, float normal) => alongX(w) ? new Vector2(along, normal) : new Vector2(normal, along);

            /// <summary>The path of a portal in wall <paramref name="w"/> with its middle at <paramref name="at"/> and half a length of <paramref name="half"/>: what is beyond an end of the wall goes on round the corner.</summary>
            private static void buildPath(BulletField f, int w, float at, float half, List<Seg> into)
            {
                into.Clear();

                float low = alongX(w) ? f.stageMin.X : f.stageMin.Y;
                float high = alongX(w) ? f.stageMax.X : f.stageMax.Y;
                at = Math.Clamp(at, low, high);

                float a0 = Math.Max(at - half, low), a1 = Math.Min(at + half, high);
                float lowOver = Math.Max(low - (at - half), 0f), highOver = Math.Max(at + half - high, 0f);
                int lowWall = alongX(w) ? 3 : 0, highWall = alongX(w) ? 1 : 2;
                float sign = inward(w);

                if (lowOver > 0.5f)
                    into.Add(new Seg { Wall = lowWall, Along0 = edge(f, w) + sign * lowOver, AlongDir = -sign, S0 = -half, Len = lowOver });

                into.Add(new Seg { Wall = w, Along0 = a0, AlongDir = 1f, S0 = a0 - at, Len = Math.Max(a1 - a0, 0f) });

                if (highOver > 0.5f)
                    into.Add(new Seg { Wall = highWall, Along0 = edge(f, w), AlongDir = sign, S0 = a1 - at, Len = highOver });
            }

            /// <summary>Where on its wall the point at <paramref name="s"/> of a path is (false if the path does not reach that far).</summary>
            private static bool locate(List<Seg> path, float s, out Seg seg, out float along)
            {
                foreach (var piece in path)
                {
                    if (s >= piece.S0 - 0.5f && s <= piece.S0 + piece.Len + 0.5f)
                    {
                        seg = piece;
                        along = piece.Along0 + piece.AlongDir * (Math.Clamp(s - piece.S0, 0f, piece.Len));
                        return true;
                    }
                }

                seg = default;
                along = 0f;
                return false;
            }

            private double warning(BulletField f) => f.stageSoundLength("power_up");

            /// <summary>0..1: how far the portals have grown (they grow during the warning, and are full size when the attack starts).</summary>
            private float grown(BulletField f, double t) => smoothStep((t - (Start - warning(f))) / warning(f));

            private float presence(BulletField f, double t)
                => t < Start - warning(f) || t > End + DanmakusuBalance.STAGE_FADE_OUT_MS ? 0f : smoothStep((t - (Start - warning(f))) / 200.0) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

            public override bool PushIsNoBounce => true;

            /// <summary>The whole screen shakes this much (px) while the warning before the attack is on, which is when the portals grow.</summary>
            public float ShakeAt(BulletField f, double t) => t >= Start - warning(f) && t < Start ? DanmakusuBalance.OKINA_PORTAL_SHAKE_PX : 0f;

            public override void Reset(BulletField f, double time)
            {
                known = false;
                entered.Clear();

                foreach (var laser in new List<LaserEmitter>(beamEntered.Keys))
                {
                    if (beamEntered[laser] > time)
                        beamEntered.Remove(laser);
                }
            }

            private readonly HashSet<(BulletEmitter, int)> entered = new HashSet<(BulletEmitter, int)>();
            private const double history_step_ms = 8;
            private readonly List<(double T, float Entry, float Exit)> history = new List<(double, float, float)>();
            private readonly List<Seg> pastEntry = new List<Seg>();
            private readonly List<Seg> pastExit = new List<Seg>();
            private int pastIndex = -1;

            /// <summary>The paths of the portals as they were at <paramref name="t"/> (the last moment that the act saw; false if it has not seen any yet).</summary>
            private bool geometryAt(BulletField f, double t, out List<Seg> entry, out List<Seg> exit)
            {
                entry = entryPath;
                exit = exitPath;

                if (history.Count == 0 || !known)
                    return false;

                if (Math.Abs(t - lastUpdate) < 1.0)
                    return true; // (now: the live paths)

                int lo = 0, hi = history.Count - 1, found = -1;

                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;

                    if (history[mid].T <= t)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else
                        hi = mid - 1;
                }

                if (found < 0)
                    found = 0;

                if (found != pastIndex)
                {
                    pastIndex = found;
                    wallOf(f, history[found].Entry, out int ew, out float ea);
                    wallOf(f, history[found].Exit, out int xw, out float xa);
                    buildPath(f, ew, ea, halfLength(f, ew), pastEntry);
                    buildPath(f, xw, xa, halfLength(f, xw), pastExit);
                }

                entry = pastEntry;
                exit = pastExit;
                return true;
            }

            /// <summary>When the portals of this act can take something (the time of the attack).</summary>
            public bool OpenBetween(double a, double b) => known && b >= Start && a <= End;

            /// <summary>
            /// A plain thing (a star, a slice of a beam) of radius <paramref name="radius"/> flew from <paramref name="from"/> to <paramref name="to"/> in the step that ended at <paramref name="t"/>: does it go through the first portal?
            /// Then it comes out of the second at <paramref name="exitPoint"/>, flying <paramref name="exitDirection"/>, having gone <paramref name="fraction"/> of the step by the time it went in. (Like <see cref="Pushes"/>, for things that are not rings.)
            /// </summary>
            public bool Transfer(BulletField f, double t, Vector2 from, Vector2 to, float radius, out Vector2 exitPoint, out Vector2 exitDirection, out float fraction, out int teleport, LaserEmitter? laser = null)
            {
                exitPoint = to;
                exitDirection = Vector2.UnitX;
                fraction = 1f;
                teleport = 0;

                if (f.failed || t < Start || t > End)
                    return false;

                // (a beam that has gone through keeps the portals where they were when it first did: every later slice of it goes in and comes out there, wherever the portals have slid since)
                double asOf = t;

                if (laser != null && beamEntered.TryGetValue(laser, out double first) && t >= first)
                    asOf = first;

                if (!geometryAt(f, asOf, out var entry, out var exit))
                    return false;

                if (!transferThrough(f, entry, exit, from, to, radius, out exitPoint, out exitDirection, out fraction, out int exitWall))
                    return false;

                if (laser != null && (!beamEntered.TryGetValue(laser, out double known) || t < known))
                    beamEntered[laser] = t;

                // (slices of a beam are only joined if they teleported together: through the same portals, of the same attack, out of the same wall. Otherwise they come out far apart or go different ways, and the line between them
                // would stretch across the screen and on. The number is the Portal of the look, and neighbours with different ones are cut.)
                teleport = 1 + Id * 4 + exitWall;
                return true;
            }

            /// <summary>When each beam first went through (the moment its later slices take the portals as they were then; forgotten for what came later when time goes back).</summary>
            private readonly Dictionary<LaserEmitter, double> beamEntered = new Dictionary<LaserEmitter, double>();

            private bool transferThrough(BulletField f, List<Seg> entry, List<Seg> exit, Vector2 from, Vector2 to, float radius, out Vector2 exitPoint, out Vector2 exitDirection, out float fraction, out int exitWall)
            {
                exitPoint = to;
                exitDirection = Vector2.UnitX;
                fraction = 1f;
                exitWall = 0;

                Vector2 delta = to - from;
                float travel = delta.Length;

                if (travel < 1e-6f)
                    return false;

                foreach (var seg in entry)
                {
                    int sw = seg.Wall;
                    float sign = inward(sw);
                    float gate = edge(f, sw) - sign * radius; // (the centre of a plain thing is a radius outside of the wall when all of it is)
                    float depth0 = sign * (normalOf(sw, from) - gate);
                    float depth1 = sign * (normalOf(sw, to) - gate);

                    if (depth0 < 0f || depth1 > 0f || depth0 - depth1 < 1e-6f)
                        continue;

                    float u = depth0 / (depth0 - depth1);
                    float along = tangentOf(sw, from + delta * u);
                    float onPiece = (along - seg.Along0) * seg.AlongDir;

                    if (onPiece < -0.5f || onPiece > seg.Len + 0.5f)
                        continue;

                    float s = seg.S0 + Math.Clamp(onPiece, 0f, seg.Len);

                    if (!locate(exit, s, out Seg exitSeg, out float exitAlong))
                        continue;

                    int xw = exitSeg.Wall;
                    exitPoint = pointOn(xw, exitAlong, edge(f, xw) - inward(xw) * radius);

                    Vector2 way = delta / travel;
                    Vector2 goingOut = outward(sw), comingIn = -outward(xw);
                    float turn = MathF.Atan2(comingIn.Y, comingIn.X) - MathF.Atan2(goingOut.Y, goingOut.X);
                    float cos = MathF.Cos(turn), sin = MathF.Sin(turn);

                    exitDirection = new Vector2(way.X * cos - way.Y * sin, way.X * sin + way.Y * cos);
                    fraction = u;
                    exitWall = xw;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// How far the point of the second portal that a slice came out of at <paramref name="crossed"/> (at <paramref name="exitPoint"/>) has moved along its wall by <paramref name="now"/>. A beam that has come out
            /// goes along with it, so that it keeps its shape while the portal slides. (Nothing if the portal has gone round a corner onto another wall since: the beam is left where it is then.)
            /// </summary>
            public Vector2 FollowExit(BulletField f, double crossed, double now, Vector2 exitPoint)
            {
                if (!known || history.Count == 0 || !geometryAt(f, crossed, out _, out var exitThen))
                    return Vector2.Zero;

                // where on the portal it came out (s from its middle), on which wall; read now, because the lists are reused by the next lookup
                bool found = false;
                float bestGap = float.MaxValue, s = 0f, alongThen = 0f;
                int wall = 0;

                foreach (var seg in exitThen)
                {
                    float along = tangentOf(seg.Wall, exitPoint);
                    float onPiece = (along - seg.Along0) * seg.AlongDir;

                    if (onPiece < -0.5f || onPiece > seg.Len + 0.5f)
                        continue;

                    float gap = MathF.Abs(normalOf(seg.Wall, exitPoint) - edge(f, seg.Wall));

                    if (gap >= bestGap)
                        continue;

                    bestGap = gap;
                    found = true;
                    wall = seg.Wall;
                    float clamped = Math.Clamp(onPiece, 0f, seg.Len);
                    s = seg.S0 + clamped;
                    alongThen = seg.Along0 + seg.AlongDir * clamped;
                }

                if (!found || !geometryAt(f, Math.Min(now, End + DanmakusuBalance.STAGE_FADE_OUT_MS), out _, out var exitNow))
                    return Vector2.Zero;

                if (!locate(exitNow, s, out Seg place, out float alongNow) || place.Wall != wall)
                    return Vector2.Zero;

                float moved = alongNow - alongThen;
                return alongX(wall) ? new Vector2(moved, 0f) : new Vector2(0f, moved);
            }

            // ---- the border of the screen as one line: top (left to right), right (down), bottom (right to left), left (up) ----

            private static float perimeterOf(BulletField f) => 2f * ((f.stageMax.X - f.stageMin.X) + (f.stageMax.Y - f.stageMin.Y));

            private static float paramOf(BulletField f, int w, float at)
            {
                float width = f.stageMax.X - f.stageMin.X, height = f.stageMax.Y - f.stageMin.Y;

                return w switch
                {
                    0 => Math.Clamp(at - f.stageMin.X, 0f, width),
                    1 => width + Math.Clamp(at - f.stageMin.Y, 0f, height),
                    2 => width + height + Math.Clamp(f.stageMax.X - at, 0f, width),
                    _ => 2f * width + height + Math.Clamp(f.stageMax.Y - at, 0f, height),
                };
            }

            private static void wallOf(BulletField f, float s, out int w, out float at)
            {
                float width = f.stageMax.X - f.stageMin.X, height = f.stageMax.Y - f.stageMin.Y;
                float total = 2f * (width + height);
                s = ((s % total) + total) % total;

                if (s < width)
                {
                    w = 0;
                    at = f.stageMin.X + s;
                }
                else if (s < width + height)
                {
                    w = 1;
                    at = f.stageMin.Y + (s - width);
                }
                else if (s < 2f * width + height)
                {
                    w = 2;
                    at = f.stageMax.X - (s - width - height);
                }
                else
                {
                    w = 3;
                    at = f.stageMax.Y - (s - 2f * width - height);
                }
            }

            /// <summary>Moves <paramref name="s"/> towards <paramref name="target"/> along the border (the shorter way round) by at most <paramref name="most"/> px.</summary>
            private static float slideTo(float s, float target, float total, float most)
            {
                float difference = target - s;
                difference -= total * MathF.Round(difference / total); // (the shorter way round)

                return s + Math.Clamp(difference, -most, most);
            }

            public override void Update(BulletField f, double now)
            {
                if (now < Start - warning(f) || now > End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                {
                    if (now < Start - warning(f))
                        known = false;

                    return;
                }

                // The wall Okina is nearest to (in the middle: the top) is where her portal wants to be (at the height / the place she is at), and the wall across from it, behind the player, is where the other wants to be. They do not
                // jump there: each slides along the border of the screen, round the corners, at a speed of its own, so when Okina moves to another side the portals travel there (bending round the corners on their way).
                Vector2 boss = f.bossPositionOf(Period);
                float[] distances = { boss.Y - f.stageMin.Y, f.stageMax.X - boss.X, f.stageMax.Y - boss.Y, boss.X - f.stageMin.X };
                int nearest = 0;

                for (int w = 1; w < 4; w++)
                {
                    if (distances[w] < distances[nearest] - 0.5f)
                        nearest = w;
                }

                float total = perimeterOf(f);
                float entryTarget = paramOf(f, nearest, tangentOf(nearest, boss));
                float exitTarget = paramOf(f, (nearest + 2) % 4, tangentOf((nearest + 2) % 4, f.characterPosition));

                if (!known || now < lastUpdate || now - lastUpdate > 250)
                {
                    entryS = entryTarget;
                    exitS = exitTarget;
                    known = true;
                }
                else
                {
                    float most = DanmakusuBalance.OKINA_PORTAL_SLIDE_SPEED * (float)(now - lastUpdate);
                    entryS = slideTo(entryS, entryTarget, total, most);
                    exitS = slideTo(exitS, exitTarget, total, most);
                }

                lastUpdate = now;

                // (where the portals were, for the things that fly through them and are worked out afterwards: the stars of the moon, the slices of a beam)
                while (history.Count > 0 && history[^1].T > now)
                    history.RemoveAt(history.Count - 1);

                if (history.Count == 0 || now - history[^1].T >= history_step_ms)
                    history.Add((now, entryS, exitS));

                wallOf(f, entryS, out entryWall, out entryAt);
                wallOf(f, exitS, out exitWall, out exitAt);

                buildPath(f, entryWall, entryAt, halfLength(f, entryWall), entryPath);
                buildPath(f, exitWall, exitAt, halfLength(f, exitWall), exitPath);
            }

            /// <summary>
            /// A bullet that flew from <paramref name="from"/> to <paramref name="to"/> in the last step goes into the first portal once it has passed all the way through the wall (the whole bullet
            /// is outside of the screen); a ricochet bullet, which turns back at the wall, goes in the moment it touches it. It comes out of the second portal at the same distance from its middle,
            /// flying on the same way at the same speed (a ricochet bullet keeps ricocheting, a plain one comes out from behind the wall). Where a portal bends round a corner the way turns with it.
            /// </summary>
            public override bool Pushes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                if (!known || f.failed || t < Start || t > End || !emitter.CanBeDeflected)
                    return false;

                // (a bullet that has gone through a portal does not go into it again)
                if (entered.Contains((emitter, bullet)))
                    return false;

                foreach (var seg in entryPath)
                {
                    if (push(f, t, from, to, emitter, bullet, seg, out position, out direction, out speed))
                    {
                        entered.Add((emitter, bullet));
                        return true;
                    }
                }

                return false;
            }

            private bool push(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, Seg seg, out Vector2 position, out Vector2 direction, out float speed)
            {
                position = to;
                direction = Vector2.UnitX;
                speed = 0f;

                int sw = seg.Wall;
                bool ricochet = emitter.Ricochet;
                float radius = emitter.VisualRadius;
                float sign = inward(sw);
                float wallEdge = edge(f, sw);
                float gate = ricochet ? wallEdge + sign * radius : wallEdge - sign * radius; // (the centre of a plain bullet is a radius outside of the wall when all of it is; a ricochet bullet turns back at the wall)
                float depth0 = sign * (normalOf(sw, from) - gate);
                float depth1 = sign * (normalOf(sw, to) - gate);

                // the bullet's own clock: it can be frozen
                double ea = emitter.WithoutFrozenTime(f.effects.Effective(emitter, ScanPrevious));
                double eb = emitter.WithoutFrozenTime(f.effects.Effective(emitter, t));
                double dt = eb - ea;

                if (dt <= 1e-6 || depth0 < 0f)
                    return false;

                float u, along, travel;
                Vector2 way;

                bool crossed = depth1 <= 0f; // (it is past the line: all the way through the wall; a ricochet bullet on its last wall flies on through it too)

                if (!ricochet || crossed)
                {
                    if (!crossed)
                        return false; // (it has not got all the way through)

                    Vector2 delta = to - from;
                    travel = delta.Length;

                    if (travel < 1e-6f)
                        return false;

                    u = depth0 / (depth0 - depth1);
                    along = tangentOf(sw, from + delta * u);
                    way = delta / travel;
                }
                else
                {
                    // it has turned back at the wall in this step: it was flying towards it, and now away from it
                    emitter.Evaluate(bullet, ea, out _, out Vector2 facingBefore, out bool aliveBefore);
                    emitter.Evaluate(bullet, eb, out _, out Vector2 facingAfter, out bool aliveAfter);

                    if (!aliveBefore || !aliveAfter || !(sign * normalOf(sw, facingBefore) < -1e-4f && sign * normalOf(sw, facingAfter) > 1e-4f))
                        return false;

                    float total = depth0 + depth1;
                    u = total > 1e-6f ? depth0 / total : 0.5f; // (it flies straight at the wall and straight back: the time to the wall is as the way to it)

                    float slide = tangentOf(sw, to) - tangentOf(sw, from);
                    Vector2 unfolded = pointOn(sw, slide, -sign * total); // (the way it would have flown if it had not turned back)

                    travel = unfolded.Length;

                    if (travel < 1e-6f)
                        return false;

                    along = tangentOf(sw, from) + slide * u;
                    way = unfolded / travel;
                }

                // where along the portal it went in (0 = the middle of it)
                float onPiece = (along - seg.Along0) * seg.AlongDir;

                if (onPiece < -0.5f || onPiece > seg.Len + 0.5f)
                    return false;

                float s = seg.S0 + Math.Clamp(onPiece, 0f, seg.Len);

                if (!locate(exitPath, s, out Seg exitSeg, out float exitAlong))
                    return false;

                int xw = exitSeg.Wall;
                float exitEdge = edge(f, xw);
                Vector2 exit = pointOn(xw, exitAlong, ricochet ? exitEdge + inward(xw) * radius : exitEdge - inward(xw) * radius);
                float rest = (float)((1f - u) * dt);

                // (it went out through the wall `sw` and comes out of the wall `xw` into the screen: where the two walls are not across from each other, its way turns)
                Vector2 goingOut = outward(sw), comingIn = -outward(xw);
                float turn = MathF.Atan2(comingIn.Y, comingIn.X) - MathF.Atan2(goingOut.Y, goingOut.X);
                float cos = MathF.Cos(turn), sin = MathF.Sin(turn);

                direction = new Vector2(way.X * cos - way.Y * sin, way.X * sin + way.Y * cos);
                speed = travel / (float)dt;
                position = exit + direction * (speed * rest); // (where it is at the end of the step: it has been flying on since it came out)
                return true;
            }

            /// <summary>
            /// Close to a portal: a column of light as wide as the portal and small round particles all along it. At Okina's portal the particles drift into the portal (and fade as they go in), at the player's they come out of it.
            /// </summary>
            private void drawIndicator(BulletField f, double t, bool entering, Seg seg, float length, float alpha, Color4 colour, int salt)
            {
                if (length < 1f)
                    return;

                float reach = DanmakusuBalance.OKINA_PORTAL_REACH;
                int w = seg.Wall;
                float sign = inward(w);
                float wallEdge = edge(f, w);
                float degrees = w switch { 0 => 90f, 1 => 180f, 2 => 270f, _ => 0f }; // (the light is bright at the wall and fades inwards)
                float at = seg.Along0 + seg.AlongDir * length * 0.5f; // (the middle of the piece, along the wall)

                f.stageTree.Add(StageTexture.Light, pointOn(w, at, wallEdge + sign * reach * 0.5f), new Vector2(reach, length), degrees, alpha * DanmakusuBalance.OKINA_PORTAL_CONE_ALPHA, colour, true);

                int count = Math.Max(2, (int)MathF.Round(DanmakusuBalance.OKINA_PORTAL_PARTICLES * length / (halfLength(f, 0) * 2f)));

                for (int k = 0; k < count; k++)
                {
                    double cycle = t / DanmakusuBalance.OKINA_PORTAL_PARTICLE_MS + (double)k / count;
                    int round = (int)Math.Floor(cycle);
                    float phase = (float)(cycle - round);
                    float side = (hash01(Id * 31 + k + salt, round, 11) * 2f - 1f) * 0.5f * length; // (where along the portal it goes: anywhere over its whole width)
                    float distance = (entering ? 1f - phase : phase) * reach; // (how far from the wall)
                    float fade = MathF.Sin(phase * MathF.PI);
                    float size = DanmakusuBalance.OKINA_PORTAL_PARTICLE_SIZE * (0.7f + 0.6f * hash01(Id * 31 + k + salt, round, 12));
                    Vector2 where = pointOn(w, at + side, wallEdge + sign * (distance + 3f));

                    f.stageTree.Add(StageTexture.Glow, where, new Vector2(size), 0f, alpha * fade * 0.9f, colour, true);
                }
            }

            public override void Draw(BulletField f, double t)
            {
                float alpha = presence(f, t);

                if (!known || f.failed || alpha <= 0.002f)
                    return;

                float g = grown(f, t);
                float thickness = DanmakusuBalance.OKINA_PORTAL_THICKNESS;
                int piece = 0;

                for (int portal = 0; portal < 2; portal++)
                {
                    int w = portal == 0 ? entryWall : exitWall;
                    float at = portal == 0 ? entryAt : exitAt;
                    var colour = portal == 0 ? new Color4(1f, 0.72f, 0.22f, 1f) : new Color4(0.4f, 0.85f, 1f, 1f);

                    buildPath(f, w, at, halfLength(f, w) * g, drawPath);

                    for (int i = 0; i < drawPath.Count; i++)
                    {
                        var seg = drawPath[i];

                        if (seg.Len < 1f || piece >= f.portalPieces.Length)
                            continue;

                        int sw = seg.Wall;
                        bool bend = sw != w;
                        float low = Math.Min(seg.Along0, seg.Along0 + seg.AlongDir * seg.Len), high = Math.Max(seg.Along0, seg.Along0 + seg.AlongDir * seg.Len);

                        // (a piece that bends round a corner reaches over it, so that the two pieces meet without a gap)
                        if (bend)
                        {
                            float over = thickness * 0.25f;

                            // (the end that is at the corner is the one that is on the wall's edge)
                            if (MathF.Abs(low - edge(f, w)) < 0.5f)
                                low -= over;
                            else
                                high += over;
                        }

                        float normalLow = edge(f, sw) - thickness * 0.25f * (inward(sw) > 0f ? 1f : -1f) - (inward(sw) > 0f ? 0f : thickness);
                        float normalSpan = thickness;
                        float length = high - low;
                        Vector2 min = alongX(sw) ? new Vector2(low, normalLow) : new Vector2(normalLow, low);
                        Vector2 size = alongX(sw) ? new Vector2(length, normalSpan) : new Vector2(normalSpan, length);

                        var bar = f.portalPieces[piece++];
                        bar.Position = min;
                        bar.Size = size;
                        bar.CornerRadius = Math.Min(thickness, length) * 0.5f;
                        bar.Colour = colour;
                        bar.Alpha = alpha * DanmakusuBalance.OKINA_PORTAL_ALPHA;

                        // (a soft glow round it)
                        Vector2 centre = min + size * 0.5f;
                        f.stageTree.Add(StageTexture.Glow, centre, alongX(sw) ? new Vector2(length * 1.15f, thickness * 2.6f) : new Vector2(thickness * 2.6f, length * 1.15f), 0f, alpha * 0.5f, colour, true);

                        drawIndicator(f, t, portal == 0, seg, seg.Len, alpha * g, colour, portal * 100 + i * 17);
                    }
                }
            }
        }
    }
}
