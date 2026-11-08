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
        // Ichirin Kumoi and Unzan: the cloud's head stands behind her and looks at the player. In her attacks, every eight beats, one of the two hands (the left and the right in turn) is thrown at the player along an outer arc, speeding
        // up a little, to the wall behind the player (the line from her through the player, as it was when the hand was thrown). Where it hits the wall the rings of Mokou's landing (without her big circle) come out of it, the field shakes
        // for four beats and the Moon's hit sound of Hecatia plays; the hand stays there for eight beats and then comes back to her in a straight line. In a pause the hands that are back clap together in front of her, which sends out four rings
        // of ellipses arranged as the tip of an arrowhead and shakes the field for four beats as well. A hand that is at home swings a little, slowly (heavy). In the warning before her first attack the head grows from nothing, the hands fade in and the first hand is already drawn back;
        // when her phase is over it all goes the other way. The hand
        // hurts like a slider body while it is out. Like Mokou's jumps, a throw is made once when it starts (where the player is then) and thrown away when time goes back before it.
        // Like Hecatia's Earth and Moon, a hand that is out (thrown, holding or coming back) gathers the bullets it touches (see Gathering) and shoots them all out at its next impact, in rings of four over
        // 180 degrees away from the wall it hits (at a clap: towards the player).
        // ======================================================================================================================================================

        private sealed class IchirinAct : StageAct
        {
            private sealed class Toss
            {
                public int Hand;
                public double Ready, T0, HitTime, ReturnStart, ReturnEnd; // (Ready: the hand starts to be drawn back; T0: it is thrown)
                public Vector2 From, Control, To, Direction;
                public float HoldTurn; // (the turn the hand has as it hits the wall, which is the way it was flying then: it keeps it while it stays)
                public bool Cancelled, Sounded, Released;
                public bool Aimed; // (the way is fixed: it is aimed again every frame of the windup, from where she and the player are then, and fixed when the hand is thrown)
            }

            private sealed class Clap
            {
                public double Approach, Time, Release, End; // (they meet at Time, stay together until Release, and are home again at End)
                public Vector2 Point, Aim;
                public bool Sounded, Released;
            }

            private readonly Gathering gathering = new Gathering(2, DanmakusuBalance.ICHIRIN_GATHER_MAX);

            private enum HandState { Home, Winding, Flying, Holding, Returning, Clapping }

            private readonly double beat;
            /// <summary>When the next section of her begins (her next cloud): this cloud is gone then, so there are never two clouds (four hands, two heads) at once.</summary>
            public double NextStart = double.PositiveInfinity;

            private double finish; // (when the last hand is home: a throw is never cut short, so the cloud stays until then even if her phase is over)
            private readonly double warn; // (how long the warning before her first attack is: the cloud comes and goes over as long)
            private readonly List<double> tossTimes = new List<double>();
            private readonly (double Start, double End)[] pauses;
            private readonly List<Toss> tosses = new List<Toss>();
            private readonly Clap?[] claps;
            private readonly bool[] clapMade;
            private readonly double[] approachAt; // (when the hands set off for the clap of a pause; NaN = there is none)

            public IchirinAct(BossPeriod period, double start, double end)
            {
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                warn = Math.Max(period.ShootStart - period.Start, 300);
                finish = end;

                // (every attack throws a hand when it starts and every eight beats after that)
                foreach (var (attackStart, attackEnd) in BossOps.Attacks(period))
                {
                    for (double t = attackStart; t < attackEnd && t < end; t += beat * DanmakusuBalance.ICHIRIN_THROW_EVERY_BEATS)
                        tossTimes.Add(t);
                }

                var list = new List<(double, double)>();

                foreach (var pause in BossOps.PausesWithTail(period))
                {
                    if (pause.End > pause.Start)
                        list.Add(pause);
                }

                pauses = list.ToArray();
                claps = new Clap?[pauses.Length];
                clapMade = new bool[pauses.Length];
                approachAt = new double[pauses.Length];
                Array.Fill(approachAt, double.NaN);

                // (the hands gather bullets while they are out: the window is worked out as the throws are made, see Absorbs)
                AbsorbFrom = start;
                AbsorbTo = double.PositiveInfinity;
            }

            public override bool Absorbs(double t) => t >= Start && t <= finish && t < NextStart && !double.IsNaN(handsAt(t).Reach);

            public override bool AbsorbsBetween(double from, double to) => Start <= to && Math.Min(finish, NextStart) >= from;

            private double handsTime = double.NaN;
            private (Vector2 A, Vector2 B, bool OutA, bool OutB, float Reach) hands;

            /// <summary>Where the hands are at <paramref name="t"/> and which of them are out (kept for the many bullets that are looked at at the same moment); Reach is NaN when neither is out.</summary>
            private (Vector2 A, Vector2 B, bool OutA, bool OutB, float Reach) handsAt(double t)
            {
                if (t == handsTime)
                    return hands;

                handsTime = t;
                var f = field!;
                var stateA = poseOf(f, 0, t, out Vector2 a, out _, out _);
                var stateB = poseOf(f, 1, t, out Vector2 b, out _, out _);
                bool outA = stateA is HandState.Flying or HandState.Holding or HandState.Returning;
                bool outB = stateB is HandState.Flying or HandState.Holding or HandState.Returning;
                hands = (a, b, outA, outB, outA || outB ? DanmakusuBalance.ICHIRIN_HAND_HIT * f.circleRadius : float.NaN);
                return hands;
            }

            private BulletField? field;

            public override void Init(BulletField f) => field = f;

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                var (a, b, outA, outB, reach) = handsAt(t);

                if (float.IsNaN(reach) || !Gathering.Gatherable(emitter, bullet))
                    return false;

                float within = reach + emitter.HitRadius;

                if (outA && distanceToSegment(a, from, to) <= within)
                    return gathering.Take(f, t, 0, emitter, bullet);

                if (outB && distanceToSegment(b, from, to) <= within)
                    return gathering.Take(f, t, 1, emitter, bullet);

                return false;
            }

            /// <summary>The way into the playfield from the wall nearest to <paramref name="point"/> (where a hand hit).</summary>
            private static Vector2 awayFromWall(BulletField f, Vector2 point)
            {
                float left = point.X - f.stageMin.X, right = f.stageMax.X - point.X, top = point.Y - f.stageMin.Y, bottom = f.stageMax.Y - point.Y;
                float nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

                if (nearest == left)
                    return new Vector2(1f, 0f);

                if (nearest == right)
                    return new Vector2(-1f, 0f);

                return nearest == top ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
            }

            /// <summary>After the bullets have been taken in: a hand that has hit the wall (or clapped) shoots out what it held.</summary>
            public override void Late(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                float speed = DanmakusuBalance.GATHER_SPEED * Math.Max(stageBulletSpeed(Period), 0.02f);
                double gap = beat * DanmakusuBalance.GATHER_RING_GAP_BEATS;

                foreach (var toss in tosses)
                {
                    if (toss.Released || toss.Cancelled || !toss.Aimed || now < toss.HitTime)
                        continue;

                    toss.Released = true;
                    gathering.Release(this, f, toss.HitTime, toss.Hand, awayFromWall(f, toss.To), toss.To, speed, gap);
                }

                foreach (var clap in claps)
                {
                    if (clap == null || clap.Released || now < clap.Time)
                        continue;

                    clap.Released = true;

                    for (int hand = 0; hand < 2; hand++)
                        gathering.Release(this, f, clap.Time, hand, clap.Aim, clap.Point, speed, gap);
                }
            }

            public override void Beads(BulletField f, double t, List<StageBead> into)
            {
                if (f.failed || t < Start || t >= NextStart)
                    return;

                for (int hand = 0; hand < 2; hand++)
                {
                    poseOf(f, hand, t, out Vector2 position, out float rotation, out float calm);
                    swing(f, hand, t, calm, ref position, ref rotation);
                    gathering.Beads(hand, t, position, DanmakusuBalance.ICHIRIN_HAND_HIT * f.circleRadius, rotation * MathF.PI / 180f, 1f, into);
                }
            }

            /// <summary>How far the hands have gone from the head to their places beside her (0..1): they come out of the head's side in the warning, and go back there when her phase is over.</summary>
            private float spreadAt(double t) => Math.Min(smoothStep((t - Start) / warn), 1f - smoothStep((t - finish) / warn));

            private Vector2 homeOf(BulletField f, int hand, double t)
            {
                float spread = spreadAt(t);
                float x = DanmakusuBalance.ICHIRIN_NEAR_X + (DanmakusuBalance.ICHIRIN_HOME_X - DanmakusuBalance.ICHIRIN_NEAR_X) * spread;
                float y = DanmakusuBalance.ICHIRIN_NEAR_Y + (DanmakusuBalance.ICHIRIN_HOME_Y - DanmakusuBalance.ICHIRIN_NEAR_Y) * spread;

                // (the cloud is turned towards the player as a whole: the hands flank her left and right of the way to the player, the head is behind her)
                Vector2 facing = facingOf(f);
                Vector2 across = new Vector2(facing.Y, -facing.X);

                return f.bossVisualOf(Period) + (across * (hand == 0 ? -x : x) + facing * y) * f.circleRadius;
            }

            private Vector2 lastFacing = new Vector2(0f, 1f);

            /// <summary>The way from her to the player (a unit vector); while the player is right on top of her it stays what it was (the cloud does not spin round).</summary>
            private Vector2 facingOf(BulletField f)
            {
                Vector2 look = f.aimPosition - f.bossVisualOf(Period);

                if (look.Length > f.circleRadius * 2f)
                    lastFacing = Vector2.Normalize(look);

                return lastFacing;
            }

            private static float degrees(Vector2 direction) => MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI - 90f; // (the hands point down in their picture)

            /// <summary>A turn from <paramref name="a"/> to <paramref name="b"/> degrees by the short way.</summary>
            private static float lerpAngle(float a, float b, float u) => a + MathF.IEEERemainder(b - a, 360f) * u;

            private static Vector2 bezier(Vector2 a, Vector2 control, Vector2 b, float s)
            {
                float u = 1f - s;
                return a * (u * u) + control * (2f * u * s) + b * (s * s);
            }

            private Toss? latestToss(int hand, double t)
            {
                for (int i = tosses.Count - 1; i >= 0; i--)
                {
                    if (tosses[i].Hand == hand && tosses[i].Ready <= t)
                        return tosses[i];
                }

                return null;
            }

            /// <summary>
            /// Where the hand is at <paramref name="t"/>, which way its picture is turned (degrees), and what it is doing. Everything is continuous from one thing to the next (no jumps in place or turn);
            /// <paramref name="calm"/> (0..1) is how much the hand is at rest, which is how much it swings.
            /// </summary>
            private HandState poseOf(BulletField f, int hand, double t, out Vector2 position, out float rotation, out float calm)
            {
                Vector2 home = homeOf(f, hand, t);
                var toss = latestToss(hand, t);

                // (a hand at rest faces the player, like the head)
                float rest = degrees(facingOf(f));

                if (toss != null && t < toss.ReturnEnd)
                {
                    if (t < toss.T0)
                    {
                        // the hand is drawn slowly back, away from the player (along the line it will fly on): the warning
                        float draw = smoothStep((t - toss.Ready) / Math.Max(toss.T0 - toss.Ready, 1.0));
                        position = Vector2.Lerp(home, toss.From, draw);
                        rotation = lerpAngle(rest, degrees(toss.Direction), draw);
                        calm = 1f - draw;
                        return HandState.Winding;
                    }

                    calm = 0f;

                    if (t < toss.HitTime)
                    {
                        float u = (float)((t - toss.T0) / Math.Max(toss.HitTime - toss.T0, 1.0));
                        float s = MathF.Pow(Math.Clamp(u, 0f, 1f), DanmakusuBalance.ICHIRIN_ACCEL_POWER);
                        position = bezier(toss.From, toss.Control, toss.To, s);
                        Vector2 ahead = bezier(toss.From, toss.Control, toss.To, Math.Min(s + 0.02f, 1f)) - position;
                        rotation = lerpAngle(degrees(toss.Direction), degrees(ahead.LengthSquared > 1e-6f ? ahead : toss.Direction), smoothStep(u * 5f));
                        return HandState.Flying;
                    }

                    if (t < toss.ReturnStart)
                    {
                        position = toss.To;
                        rotation = toss.HoldTurn;
                        return HandState.Holding;
                    }

                    float back = (float)Math.Clamp((t - toss.ReturnStart) / Math.Max(toss.ReturnEnd - toss.ReturnStart, 1.0), 0.0, 1.0);
                    position = Vector2.Lerp(toss.To, home, back);
                    rotation = lerpAngle(toss.HoldTurn, rest, smoothStep(back));
                    calm = smoothStep(back);
                    return HandState.Returning;
                }

                foreach (var clap in claps)
                {
                    if (clap == null || t < clap.Approach || t > clap.End)
                        continue;

                    // (the left hand meets the right on its own side: the two never cross)
                    float side = hand == 0 ? -1f : 1f;
                    Vector2 across = new Vector2(clap.Aim.Y, -clap.Aim.X);

                    Vector2 meeting = clap.Point + across * side * (f.circleRadius * ICHIRIN_HAND_WIDTH * 0.25f);
                    float clapTurn = degrees(clap.Aim) + side * 12f;
                    float together = smoothStep((t - clap.Approach) / Math.Max(clap.Time - clap.Approach, 1.0));
                    float apart = smoothStep((t - clap.Release) / Math.Max(clap.End - clap.Release, 1.0));

                    // (the hands go to the meeting along an outer arc, slowly at first and faster and faster, and come home along the same arc)
                    Vector2 clapControl = (home + meeting) * 0.5f + across * side * (Vector2.Distance(home, meeting) * DanmakusuBalance.ICHIRIN_CLAP_ARC);

                    if (t <= clap.Time)
                    {
                        float approachU = (float)Math.Clamp((t - clap.Approach) / Math.Max(clap.Time - clap.Approach, 1.0), 0.0, 1.0);
                        position = bezier(home, clapControl, meeting, MathF.Pow(approachU, DanmakusuBalance.ICHIRIN_CLAP_ACCEL_POWER));
                        rotation = lerpAngle(rest, clapTurn, together);
                        calm = 1f - together;
                    }
                    else if (t <= clap.Release)
                    {
                        // (they stay together for a while)
                        position = meeting;
                        rotation = clapTurn;
                        calm = 0f;
                    }
                    else
                    {
                        position = bezier(meeting, clapControl, home, apart);
                        rotation = lerpAngle(clapTurn, rest, apart);
                        calm = apart;
                    }

                    return HandState.Clapping;
                }

                position = home;
                rotation = rest;
                calm = 1f;
                return HandState.Home;
            }

            private const float ICHIRIN_HAND_WIDTH = DanmakusuBalance.ICHIRIN_HAND_HEIGHT * 0.54f;

            /// <summary>A hand swings a little (slowly: it is heavy) as much as it is at rest: a slow sway sideways and up and down, and a slight turn.</summary>
            private void swing(BulletField f, int hand, double t, float calm, ref Vector2 position, ref float rotation)
            {
                float phase = (float)(t / (beat * DanmakusuBalance.ICHIRIN_SWING_BEATS)) * MathF.Tau + hand * 2.1f;
                float c = f.circleRadius * DanmakusuBalance.ICHIRIN_SWING * calm;

                position += new Vector2(MathF.Sin(phase) * c, MathF.Sin(phase * 0.5f + 1.3f) * c * 0.7f);
                rotation += MathF.Sin(phase + 0.8f) * DanmakusuBalance.ICHIRIN_SWING_DEGREES * calm;
            }
            // ---- the throws and the claps ----

            private void advance(BulletField f, double now)
            {
                for (int guard = 0; guard < 64 && tosses.Count < tossTimes.Count && tossTimes[tosses.Count] - beat * DanmakusuBalance.ICHIRIN_WINDUP_BEATS <= now; guard++)
                    addToss(f, tosses.Count, tossTimes[tosses.Count]);

                // (a hand that is being drawn back follows her and the player; when it is thrown its way is fixed and the impact is made)
                foreach (var toss in tosses)
                {
                    if (toss.Aimed)
                        continue;

                    aim(f, toss, now);

                    if (now >= toss.T0)
                    {
                        toss.Aimed = true;
                        addImpact(f, toss);
                    }
                }

                for (int i = 0; i < pauses.Length; i++)
                {
                    if (!clapMade[i] && now >= pauses[i].Start)
                        planClap(i);

                    if (clapMade[i] && claps[i] == null && !double.IsNaN(approachAt[i]) && now >= approachAt[i])
                        startClap(f, i);
                }
            }

            /// <summary>
            /// Where a hand thrown from <paramref name="boss"/> along <paramref name="direction"/> lands: on one of the two walls that are far from her (never on the two that are behind her, the nearest side and the nearest top or bottom).
            /// If the line to the player would not reach a far wall inside the playfield (the player is on her side of the screen, behind her), the parts of the direction that point at the walls behind her are turned round, and <paramref name="direction"/> with them.
            /// </summary>
            private Vector2 landingOf(BulletField f, Vector2 boss, ref Vector2 direction, Vector2 low, Vector2 high)
            {
                Vector2 middle = (low + high) * 0.5f;
                float farX = boss.X < middle.X ? high.X : low.X, farY = boss.Y < middle.Y ? high.Y : low.Y;
                float signX = farX > boss.X ? 1f : -1f, signY = farY > boss.Y ? 1f : -1f;
                Vector2 standing = Vector2.Clamp(boss, low, high);

                bool farWall(Vector2 d, out Vector2 point)
                {
                    float best = float.MaxValue;
                    point = standing;

                    if (d.X * signX > 1e-4f)
                    {
                        float t = (farX - standing.X) / d.X;
                        float y = standing.Y + d.Y * t;

                        if (t >= 0f && y >= low.Y - 0.5f && y <= high.Y + 0.5f && t < best)
                        {
                            best = t;
                            point = standing + d * t;
                        }
                    }

                    if (d.Y * signY > 1e-4f)
                    {
                        float t = (farY - standing.Y) / d.Y;
                        float x = standing.X + d.X * t;

                        if (t >= 0f && x >= low.X - 0.5f && x <= high.X + 0.5f && t < best)
                        {
                            best = t;
                            point = standing + d * t;
                        }
                    }

                    return best < float.MaxValue;
                }

                if (farWall(direction, out Vector2 landing))
                    return landing;

                Vector2 turned = new Vector2(MathF.Abs(direction.X) * signX, MathF.Abs(direction.Y) * signY);
                turned = turned.LengthSquared > 1e-6f ? Vector2.Normalize(turned) : new Vector2(signX, signY) * 0.7071f;
                direction = turned;

                return farWall(turned, out landing) ? landing : Vector2.Clamp(new Vector2(farX, farY), low, high);
            }
            /// <summary>
            /// Aims a throw from where she and the player are at <paramref name="now"/>: the line from her to the player, the far wall it lands on, the place the hand is drawn back to and the arc.
            /// Done every frame of the windup (she stands opposite the player and moves with them: a way chosen when the windup started could point at a wall that is behind her by the time the hand flies).
            /// </summary>
            private void aim(BulletField f, Toss toss, double now)
            {
                float margin = DanmakusuBalance.MOKOU_WALL_MARGIN * f.circleRadius;
                Vector2 low = f.stageMin + new Vector2(margin);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(margin));
                Vector2 boss = f.bossPositionOf(Period);
                Vector2 toPlayer = f.aimPosition - boss;

                if (toPlayer.LengthSquared < 1f)
                    toPlayer = f.fieldCentre - boss;

                if (toPlayer.LengthSquared < 1f)
                    toPlayer = new Vector2(0f, 1f);

                Vector2 direction = Vector2.Normalize(toPlayer);
                Vector2 landing = landingOf(f, boss, ref direction, low, high);
                Vector2 from = Vector2.Clamp(homeOf(f, toss.Hand, Math.Min(now, toss.T0)) - direction * (DanmakusuBalance.ICHIRIN_PULLBACK * f.circleRadius), low, high); // (and the hand is not drawn back through the wall behind her) // (where it is drawn back to, and thrown from)
                Vector2 way = landing - from;
                float length = way.Length;
                Vector2 across = length > 1e-3f ? new Vector2(-way.Y, way.X) / length : new Vector2(1f, 0f);

                // (the arc bulges outwards: to the side of the line that is farther from her, whichever way the hand is thrown)
                Vector2 middle = (from + landing) * 0.5f;

                if (Vector2.DistanceSquared(middle - across * (length * DanmakusuBalance.ICHIRIN_ARC), boss) > Vector2.DistanceSquared(middle + across * (length * DanmakusuBalance.ICHIRIN_ARC), boss))
                    across = -across;

                toss.From = from;
                toss.To = landing;
                toss.Control = Vector2.Clamp((from + landing) * 0.5f + across * (length * DanmakusuBalance.ICHIRIN_ARC), low, high); // (never outside the playfield: the arc does not go through the walls behind her)
                toss.Direction = direction;

                Vector2 finalWay = toss.To - toss.Control;
                toss.HoldTurn = degrees(finalWay.LengthSquared > 1e-6f ? finalWay : direction);
            }

            private void addToss(BulletField f, int index, double t0)
            {
                int hand = index % 2;

                // (a hand that is still on its way home is not drawn back again at once: it has a moment of rest first, and the throw comes later if need be)
                double ready = t0 - beat * DanmakusuBalance.ICHIRIN_WINDUP_BEATS;
                var before = latestToss(hand, double.MaxValue);

                if (before != null)
                    ready = Math.Max(ready, before.ReturnEnd + beat * DanmakusuBalance.ICHIRIN_REST_BEATS);

                double throwAt = ready + beat * DanmakusuBalance.ICHIRIN_WINDUP_BEATS;

                var toss = new Toss
                {
                    Hand = hand,
                    Ready = ready, // (the hand is drawn back before the attack time: in the warning for the first throw)
                    T0 = throwAt,
                    HitTime = throwAt + beat * DanmakusuBalance.ICHIRIN_THROW_BEATS,
                    ReturnStart = throwAt + beat * (DanmakusuBalance.ICHIRIN_THROW_BEATS + DanmakusuBalance.ICHIRIN_HOLD_BEATS),
                    ReturnEnd = throwAt + beat * (DanmakusuBalance.ICHIRIN_THROW_BEATS + DanmakusuBalance.ICHIRIN_HOLD_BEATS + DanmakusuBalance.ICHIRIN_RETURN_BEATS),
                };

                // (a throw is never interrupted: not by the end of the attack, nor by the end of her phase; the cloud stays until the hand is home. It is aimed in `advance`, and its impact made when it is thrown)
                aim(f, toss, ready);
                tosses.Add(toss);
                finish = Math.Max(finish, toss.ReturnEnd);
            }

            /// <summary>The rings of Mokou's landing (without her big circle) at the place where the hand hits the wall.</summary>
            private void addImpact(BulletField f, Toss toss)
            {
                if (toss.Cancelled)
                    return;

                float baseRadius = f.stageBaseRadius(Period);
                float baseSpeed = Math.Max(stageBulletSpeed(Period), 0.02f);
                int colours = Math.Max(f.palette.Length, 1);
                double ringGap = beat * DanmakusuBalance.MOKOU_RING_GAP_BEATS;
                float twist = DanmakusuBalance.MOKOU_RING_TWIST_DEGREES * MathF.PI / 180f;
                float aim = MathF.Atan2(toss.Direction.Y, toss.Direction.X);
                float edge = f.circleRadius * DanmakusuBalance.ICHIRIN_HAND_HIT;

                for (int ring = 0; ring < DanmakusuBalance.ICHIRIN_IMPACT_RINGS; ring++)
                {
                    double fired = toss.HitTime + ring * ringGap;

                    for (int piece = 0; piece < DanmakusuBalance.MOKOU_RING_BULLETS; piece++)
                    {
                        float angle = aim + ring * twist + MathF.Tau * piece / DanmakusuBalance.MOKOU_RING_BULLETS;
                        Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (baseSpeed * DanmakusuBalance.MOKOU_RING_SPEED_FACTOR);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = toss.To + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * edge,
                            V = velocity,
                            Radius = baseRadius,
                            Hit = baseRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = ring % colours,
                            Shape = BulletShape.Circle,
                            Until = fired + f.stageLife(velocity),
                        });
                    }
                }
            }

            /// <summary>The clap of a pause: when both hands are back (and at the earliest half a beat into the pause) they start to go to one place, the place where the player is at that moment (see <see cref="startClap"/>), and come together there; it must fit into the pause.</summary>
            private void planClap(int pause)
            {
                clapMade[pause] = true;
                double pauseStart = pauses[pause].Start, pauseEnd = pauses[pause].End;
                double back = pauseStart;

                for (int hand = 0; hand < 2; hand++)
                {
                    Toss? last = null;

                    foreach (var toss in tosses)
                    {
                        if (toss.Hand == hand && toss.Ready < pauseStart)
                            last = toss;
                    }

                    if (last != null)
                        back = Math.Max(back, last.ReturnEnd);
                }

                double approach = Math.Max(pauseStart, back) + beat * 0.25;
                double end = approach + beat * (2 * DanmakusuBalance.ICHIRIN_CLAP_BEATS + DanmakusuBalance.ICHIRIN_CLAP_HOLD_BEATS);

                approachAt[pause] = end > pauseEnd || end > End ? double.NaN : approach;
            }

            /// <summary>The hands set off for the clap: the place is where the player is now (kept inside the playfield) and does not change any more.</summary>
            private void startClap(BulletField f, int pause)
            {
                float margin = DanmakusuBalance.MOKOU_WALL_MARGIN * f.circleRadius;
                Vector2 low = f.stageMin + new Vector2(margin);
                Vector2 high = Vector2.ComponentMax(low, f.stageMax - new Vector2(margin));
                Vector2 point = Vector2.Clamp(f.aimPosition, low, high);
                Vector2 outward = point - f.bossVisualOf(Period);
                Vector2 aim = outward.Length > f.circleRadius ? Vector2.Normalize(outward) : facingOf(f);
                double approach = approachAt[pause];
                double clapTime = approach + beat * DanmakusuBalance.ICHIRIN_CLAP_BEATS;
                var clap = new Clap { Approach = approach, Time = clapTime, Release = clapTime + beat * DanmakusuBalance.ICHIRIN_CLAP_HOLD_BEATS, End = clapTime + beat * (DanmakusuBalance.ICHIRIN_CLAP_HOLD_BEATS + DanmakusuBalance.ICHIRIN_CLAP_BEATS), Point = point, Aim = aim };

                claps[pause] = clap;
                addClapBullets(f, clap);
            }
            private void addClapBullets(BulletField f, Clap clap)
            {
                float baseRadius = f.stageBaseRadius(Period);
                float baseSpeed = Math.Max(stageBulletSpeed(Period), 0.02f) * DanmakusuBalance.ICHIRIN_CLAP_SPEED;
                int colours = Math.Max(f.palette.Length, 1);
                float aim = MathF.Atan2(clap.Aim.Y, clap.Aim.X);

                for (int ring = 0; ring < DanmakusuBalance.ICHIRIN_CLAP_RINGS; ring++)
                {
                    double fired = clap.Time + ring * beat * DanmakusuBalance.ICHIRIN_CLAP_RING_GAP_BEATS;

                    // the ring is the outline of an arrowhead bullet: a sharp tip towards the player (a corner), round arc sides and a round back (every bullet flies at a speed that keeps its place on the outline)
                    for (int i = 0; i < DanmakusuBalance.ICHIRIN_CLAP_BULLETS; i++)
                    {
                        float turn = MathF.Tau * i / DanmakusuBalance.ICHIRIN_CLAP_BULLETS;
                        float away = MathF.Abs(MathF.IEEERemainder(turn, MathF.Tau)) / MathF.PI; // (0 at the tip, 1 at the back)
                        float reach = DanmakusuBalance.ICHIRIN_ARROW_BACK + (1f - DanmakusuBalance.ICHIRIN_ARROW_BACK) * MathF.Pow(1f - away, DanmakusuBalance.ICHIRIN_ARROW_SHARPNESS);
                        Vector2 velocity = new Vector2(MathF.Cos(aim + turn), MathF.Sin(aim + turn)) * (baseSpeed * reach);

                        AddBullet(new StageBullet
                        {
                            T0 = fired,
                            P0 = clap.Point,
                            V = velocity,
                            Radius = baseRadius,
                            Hit = baseRadius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = ring % colours,
                            Shape = BulletShape.Ellipse,
                            Until = fired + f.stageLife(velocity),
                        });
                    }
                }
            }

            /// <summary>0..1: every hit of a hand and every clap shakes the field for four beats (the field turns it into 1..2 pixels).</summary>
            public float ShakeAt(double now)
            {
                double span = beat * DanmakusuBalance.ICHIRIN_SHAKE_BEATS;

                foreach (var toss in tosses)
                {
                    if (!toss.Cancelled && now >= toss.HitTime && now < toss.HitTime + span)
                        return DanmakusuBalance.ICHIRIN_SHAKE;
                }

                foreach (var clap in claps)
                {
                    if (clap != null && now >= clap.Time && now < clap.Time + span)
                        return DanmakusuBalance.ICHIRIN_SHAKE;
                }

                return 0f;
            }

            // ---- time ----

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                advance(f, now);

                // every hit of a hand (and every clap) plays the hit sound of Hecatia's Moon (not when it was jumped over by a seek)
                foreach (var toss in tosses)
                {
                    if (toss.Sounded || now < toss.HitTime)
                        continue;

                    toss.Sounded = true;

                    if (now - toss.HitTime < 250)
                        f.playStageSound("hecatia_moon_hit");
                }

                foreach (var clap in claps)
                {
                    if (clap == null || clap.Sounded || now < clap.Time)
                        continue;

                    clap.Sounded = true;

                    if (now - clap.Time < 250)
                        f.playStageSound("hecatia_moon_hit");
                }
            }

            public override void Reset(BulletField f, double time)
            {
                // the throws and the claps that began after this moment have not been made yet; the others (and everything that belongs to them) are as they were
                tosses.RemoveAll(toss => toss.Ready > time);
                finish = End;

                foreach (var kept in tosses)
                    finish = Math.Max(finish, kept.ReturnEnd);

                for (int i = 0; i < pauses.Length; i++)
                {
                    if (pauses[i].Start > time)
                    {
                        claps[i] = null;
                        clapMade[i] = false;
                        approachAt[i] = double.NaN;
                    }
                    else if (claps[i] != null && claps[i]!.Approach > time)
                        claps[i] = null; // (the hands have not set off yet: they will pick the place again)
                }

                // (what the hands gathered and shot after this moment is undone; the bullets shot before it are put back below)
                gathering.Reset(this, time);
                handsTime = double.NaN;
                ClearBullets();

                foreach (var toss in tosses)
                {
                    toss.Sounded = toss.Cancelled || toss.HitTime <= time;
                    toss.Released = toss.Cancelled || toss.HitTime <= time;

                    // (a hand that was still being drawn back then is aimed again from there)
                    if (toss.T0 > time)
                        toss.Aimed = false;
                    else if (toss.Aimed)
                        addImpact(f, toss);
                }

                foreach (var clap in claps)
                {
                    if (clap == null)
                        continue;

                    clap.Sounded = clap.Time <= time;
                    clap.Released = clap.Time <= time;
                    addClapBullets(f, clap);
                }

                gathering.Readd(this);
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start || now > finish || now >= NextStart)
                    return false;

                float reach = DanmakusuBalance.ICHIRIN_HAND_HIT * f.circleRadius + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int hand = 0; hand < 2; hand++)
                {
                    var state = poseOf(f, hand, now, out Vector2 position, out _, out _);

                    if ((state == HandState.Flying || state == HandState.Holding || state == HandState.Returning) && Vector2.Distance(f.characterPosition, position) <= reach)
                        return true;

                    // (the hands hurt while they are about to meet, as they meet and a little after: the slow clap is the warning)
                    if (state == HandState.Clapping)
                    {
                        foreach (var clap in claps)
                        {
                            if (clap != null && now >= clap.Time - (clap.Time - clap.Approach) * 0.3 && now <= clap.Release && Vector2.Distance(f.characterPosition, position) <= reach)
                                return true;
                        }
                    }
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start || t > finish + warn || t >= NextStart)
                    return;

                // (in the warning the head grows from nothing to its size and the hands fade in; when her phase is over it all goes the other way, as slowly)
                float presence = smoothStep((t - Start) / warn) * smoothStep((finish + warn - t) / warn) * smoothStep((NextStart - t) / (warn * 0.5)); // (the last factor: it gives way to the next cloud)
                float c = f.circleRadius;
                Vector2 boss = f.bossVisualOf(Period);

                // the head behind her, turned a little towards the player
                Vector2 toPlayer = f.aimPosition - boss;
                Vector2 toward = facingOf(f);
                float headWidth = DanmakusuBalance.ICHIRIN_HEAD_WIDTH * c;

                // (the head is always behind her, as seen from the player, and faces the player)
                // (the head sways a little too, even more slowly than the hands: heavy and steady)
                float sway = (float)(t / (beat * DanmakusuBalance.ICHIRIN_SWING_BEATS * 1.5)) * MathF.Tau;
                Vector2 headSway = new Vector2(MathF.Sin(sway), MathF.Sin(sway * 0.5f + 0.7f) * 0.7f) * (DanmakusuBalance.ICHIRIN_SWING * c);

                float handHeight = DanmakusuBalance.ICHIRIN_HAND_HEIGHT * c;

                for (int hand = 0; hand < 2; hand++)
                {
                    Vector2 position;
                    float rotation;
                    poseOf(f, hand, t, out position, out rotation, out float calm);
                    swing(f, hand, t, calm, ref position, ref rotation); // (a hand swings a little, slowly: it is heavy, as much as it is at rest)
                    f.stageCloud.Add(hand == 0 ? StageTexture.UnzanHandLeft : StageTexture.UnzanHandRight, position, new Vector2(handHeight * 0.54f, handHeight), rotation, presence, Color4.White);
                }

                // (the head is drawn after the hands: the hands are below it; both are below her and above her magic circle)
                f.stageCloud.Add(StageTexture.UnzanHead, boss - toward * (DanmakusuBalance.ICHIRIN_HEAD_BEHIND * headWidth) + headSway, new Vector2(headWidth, headWidth * 260f / 276f) * presence, degrees(toward) + MathF.Sin(sway + 1.1f) * DanmakusuBalance.ICHIRIN_SWING_DEGREES, Math.Min(presence * 2f, 1f), Color4.White);

                foreach (var toss in tosses)
                {
                    if (!toss.Cancelled && t >= toss.HitTime && t < toss.HitTime + 1000)
                        mokouFlash(f, toss.To, t - toss.HitTime, f.palette[0]);
                }

                foreach (var clap in claps)
                {
                    if (clap != null && t >= clap.Time && t < clap.Time + 1000)
                        mokouFlash(f, clap.Point, t - clap.Time, f.palette[0]);
                }
            }
        }
    }
}
