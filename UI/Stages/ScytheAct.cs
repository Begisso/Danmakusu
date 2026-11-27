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
        // Komachi Onozuka (2026-11-27): all through her section she goes as bosses go for a beat and stops for KOMACHI_STOP_BEATS, in turn (KOMACHI_EVERY_BEATS a round). As each stop begins she throws an
        // arc at the player with one of Kagerou's swings: Elly's arc (as wide round, as thin at the tips) KOMACHI_ARC_SIZE as big - radius and width - which flies on at KOMACHI_ARC_SPEED times a bullet's
        // speed, its round side ahead, until it has left the screen. It hurts like a slider body, and every bullet of a ring it goes over is cut in two along it (CuttingAct), the pieces flying apart
        // across it. Where she is and where the player is are taken as each stop begins.
        // ======================================================================================================================================================

        private sealed class ScytheAct : CuttingAct
        {
            private sealed class Arc
            {
                public double Time;
                public Vector2 From, Way;
                public float Angle;
            }

            private readonly double beat;
            private readonly double[] stops;
            private readonly Vector2?[] standAt;
            private readonly List<Arc> arcs = new List<Arc>();
            private int nextStop;
            private int lastSwing = -1, swingsHeard;
            private double resetTime = double.NegativeInfinity;

            public ScytheAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);

                var list = new List<double>();
                double every = beat * DanmakusuBalance.KOMACHI_EVERY_BEATS;

                for (double t = period.ShootStart + every - beat * DanmakusuBalance.KOMACHI_STOP_BEATS; t < period.End && list.Count < 20000; t += every)
                    list.Add(t);

                stops = list.ToArray();
                standAt = new Vector2?[stops.Length];
                Start = period.ShootStart;
                End = period.End + beat * 32;
                AbsorbFrom = Start;
                AbsorbTo = End;
            }

            private float radius(BulletField f) => (f.stageMax.Y - f.stageMin.Y) * DanmakusuBalance.ELLY_RADIUS * DanmakusuBalance.KOMACHI_ARC_SIZE;

            private float halfWidth(BulletField f) => f.circleRadius * DanmakusuBalance.ELLY_ARC_HALF_WIDTH_CIRCLES * DanmakusuBalance.KOMACHI_ARC_SIZE;

            private static float span => DanmakusuBalance.ELLY_ARC_DEGREES * MathF.PI / 180f;

            private float speed => stageBulletSpeed(Period) * DanmakusuBalance.KOMACHI_ARC_SPEED;

            private double life(BulletField f) => (Vector2.Distance(f.stageMin, f.stageMax) + radius(f) * 2f) / Math.Max(speed, 1e-3f);

            private Vector2 centreOf(Arc arc, double t) => arc.From + arc.Way * (speed * (float)Math.Max(t - arc.Time, 0));

            /// <summary>The arc narrows to a point at both its tips, as Elly's does.</summary>
            private static float taper(float u) => MathF.Pow(Math.Max(MathF.Sin(MathF.PI * Math.Clamp(u, 0f, 1f)), 0f), 0.7f);

            public override void Reset(BulletField f, double time)
            {
                ClearPieces();
                arcs.Clear();
                nextStop = 0;

                for (int k = 0; k < stops.Length; k++)
                {
                    if (stops[k] > time)
                        standAt[k] = null;
                }

                while (nextStop < stops.Length && stops[nextStop] + life(f) < time)
                    nextStop++;

                resetTime = time;
            }

            /// <summary>The stop <paramref name="t"/> is in, or -1.</summary>
            private int stopAt(double t)
            {
                if (stops.Length == 0 || t < stops[0])
                    return -1;

                int k = Math.Min(stops.Length - 1, (int)((t - stops[0]) / (beat * DanmakusuBalance.KOMACHI_EVERY_BEATS)));
                return t < stops[k] + beat * DanmakusuBalance.KOMACHI_STOP_BEATS ? k : -1;
            }

            /// <summary>She stands where she was as the stop began.</summary>
            public override bool BossAt(BulletField f, double now, out Vector2 position)
            {
                position = default;
                int k = stopAt(now);

                if (k < 0 || f.failed)
                    return false;

                standAt[k] ??= f.bossPositionOf(Period);
                position = standAt[k]!.Value;
                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                while (nextStop < stops.Length && stops[nextStop] <= now)
                {
                    int k = nextStop++;
                    Vector2 from = standAt[k] ??= f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - from;
                    Vector2 way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                    arcs.Add(new Arc { Time = stops[k], From = from, Way = way, Angle = MathF.Atan2(way.Y, way.X) });

                    if (now - stops[k] < 250 && stops[k] > resetTime)
                        f.playStageSound(nextSwing());
                }

                double longest = life(f);
                arcs.RemoveAll(arc => now > arc.Time + longest);
            }

            /// <summary>Kagerou's swings, in an order of their own, never the same twice in a row.</summary>
            private string nextSwing()
            {
                int count = DanmakusuBalance.KAGEROU_SWINGS;
                int pick = (int)(hash01(swingsHeard++, Period.Rank, 43) * (count - 1));

                if (lastSwing >= 0 && pick >= lastSwing)
                    pick++;

                lastSwing = Math.Clamp(pick, 0, count - 1);
                return $"kagerou_swing_{lastSwing}";
            }

            /// <summary>How far along the arc <paramref name="point"/> is from its first tip (0..1), and how far from its line, at <paramref name="t"/>; false when it is beside the arc's ends.</summary>
            private bool place(BulletField f, Arc arc, Vector2 point, double t, out float u, out float off, out Vector2 normal)
            {
                Vector2 rel = point - centreOf(arc, t);
                float distance = rel.Length;
                normal = distance > 1e-4f ? rel / distance : arc.Way;
                off = distance - radius(f);
                float angle = MathF.IEEERemainder(MathF.Atan2(rel.Y, rel.X) - arc.Angle, MathF.Tau);
                u = (angle / span + 1f) * 0.5f;
                return MathF.Abs(angle) <= span;
            }

            /// <summary>Every bullet of a ring the arc goes over (between the scan before and now) is cut along it.</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!CanBeCut(f, emitter))
                    return false;

                float size = SizeOf(emitter, bullet);

                for (int i = arcs.Count - 1; i >= 0; i--)
                {
                    var arc = arcs[i];

                    if (t < arc.Time)
                        continue;

                    if (!place(f, arc, to, t, out float u, out float off, out Vector2 normal))
                        continue;

                    place(f, arc, from, Math.Max(ScanPrevious, arc.Time), out _, out float before, out _);
                    float reach = halfWidth(f) * taper(u) + size;

                    if (MathF.Abs(off) > reach && Math.Sign(off) == Math.Sign(before))
                        continue;

                    CutBullet(f, t, from, to, emitter, bullet, normal, -off);
                    return true;
                }

                return false;
            }

            /// <summary>The arc hurts like a slider body (what it went over in the last moment too: it is thin and fast).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                Vector2 player = f.characterPosition;

                foreach (var arc in arcs)
                {
                    if (now < arc.Time || !place(f, arc, player, now, out float u, out float off, out _))
                        continue;

                    float reach = halfWidth(f) * taper(u) + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                    if (MathF.Abs(off) <= reach)
                        return true;

                    place(f, arc, player, Math.Max(arc.Time, now - 24), out _, out float before, out _);

                    if (Math.Sign(off) != Math.Sign(before))
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                foreach (var arc in arcs)
                {
                    if (t < arc.Time)
                        continue;

                    float grown = smoothStep((t - arc.Time) / 120.0);
                    addArc(f, arc, t, grown);
                }

                DrawPieces(f, t);
            }

            private void addArc(BulletField f, Arc arc, double t, float alpha)
            {
                const int steps = 20;
                float r = radius(f), half = halfWidth(f);
                Vector2 centre = centreOf(arc, t);
                var white = new Color4(1f, 1f, 1f, alpha);
                var glow = new Color4(1f, 0.35f, 0.4f, 0.6f * alpha);
                var none = new Color4(glow.R, glow.G, glow.B, 0f);

                (Vector2 Outer, Vector2 Inner, Vector2 Out, float Glow) rim(float u)
                {
                    float angle = arc.Angle + span * (2f * u - 1f);
                    Vector2 e = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    float h = half * taper(u);
                    return (centre + e * (r + h), centre + e * Math.Max(r - h, 0f), e, h * 1.6f + 2f);
                }

                var previous = rim(0f);

                for (int i = 1; i <= steps; i++)
                {
                    var next = rim(i / (float)steps);

                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = previous.Inner, D = next.Inner, Near = white, Far = white });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Outer, B = next.Outer, C = previous.Outer + previous.Out * previous.Glow, D = next.Outer + next.Out * next.Glow, Near = glow, Far = none });
                    f.stageMeshes.Add(new MeshQuad { A = previous.Inner, B = next.Inner, C = previous.Inner - previous.Out * previous.Glow, D = next.Inner - next.Out * next.Glow, Near = glow, Far = none });
                    previous = next;
                }
            }
        }
    }
}
