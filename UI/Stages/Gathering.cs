using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // What Hecatia's Earth and Moon and Unzan's hands gather: a bullet they touch is taken (it is gone from its ring) and lies under its holder, spread evenly over a disc a little bigger than the
        // holder (the places follow one another by the golden angle from the rim inwards, so the first ones show round the edge), turning with it, harmless (and not shown). At the holder's next impact
        // everything it holds is dealt out into five rings, each spread evenly over 180 degrees away from the wall (or what it hit), shot one after the other a quarter of a beat apart, each bullet with
        // its own look.
        // Like the UFOs, what was gathered is kept when time goes back (a bullet taken again is the same one); only what was gathered and shot after that moment is undone.
        // ======================================================================================================================================================

        private sealed class Gathering
        {
            private struct Held
            {
                public (BulletEmitter, int) Key;
                public double Time;
                public double Released;
                public int Holder;
                public int Slot;
                public int Style;
                public BulletShape Shape;
                public int Tint;
                public float TintAmount;
                public float Radius;
            }

            private struct Shot
            {
                public double Release;
                public int Index;
                public StageBullet Bullet;
            }

            private readonly List<Held> held = new List<Held>();
            private readonly Dictionary<(BulletEmitter, int), int> known = new Dictionary<(BulletEmitter, int), int>();
            private readonly List<Shot> shots = new List<Shot>();
            private readonly int[] holding;
            private readonly int max;

            public Gathering(int holders, int max)
            {
                holding = new int[holders];
                this.max = max;
            }

            /// <summary>Can the holder take a bullet of this ring? (Not the bullets that lie on Reimu's pile or keep other bullets off.)</summary>
            public static bool Gatherable(BulletEmitter emitter, int bullet) => !emitter.Deflector && !emitter.Retired && !emitter.IsStuck(bullet);

            /// <summary>The holder takes the bullet at <paramref name="t"/>; false when it is full (then the bullet is left to whatever else happens to it).</summary>
            public bool Take(BulletField f, double t, int holder, BulletEmitter emitter, int bullet)
            {
                if (known.ContainsKey((emitter, bullet)))
                    return true; // (taken again after time went back: it is the same one)

                if (holding[holder] >= max)
                    return false;

                known[(emitter, bullet)] = held.Count;
                held.Add(new Held
                {
                    Key = (emitter, bullet),
                    Time = t,
                    Released = double.PositiveInfinity,
                    Holder = holder,
                    Slot = holding[holder]++,
                    Style = f.styleOf(emitter),
                    Shape = emitter.DrawShape ?? emitter.Shape,
                    Tint = emitter.AltTint > 0 && bullet % 2 == 1 ? emitter.AltTint : emitter.Tint,
                    TintAmount = emitter.TintAmount,
                    Radius = Math.Max(emitter.VisualRadius, 1f),
                });

                return true;
            }

            /// <summary>
            /// The holder hits something at <paramref name="time"/>: all it holds is shot out from <paramref name="origin"/> (its middle then) at <paramref name="speed"/> px per ms, dealt out into
            /// <see cref="DanmakusuBalance.GATHER_RINGS"/> rings of (nearly) as many, one after the other <paramref name="gap"/> ms apart, each spread evenly over 180 degrees round <paramref name="away"/>
            /// (all the way round with <paramref name="all"/>), every second one turned half a step so that its bullets fly between the last one's.
            /// </summary>
            public void Release(StageAct act, BulletField f, double time, int holder, Vector2 away, Vector2 origin, float speed, double gap, bool all = false, float edge = 0f)
            {
                float baseAngle = MathF.Atan2(away.Y, away.X);
                float spread = all ? MathF.Tau : DanmakusuBalance.GATHER_SPREAD_DEGREES * MathF.PI / 180f;
                int total = 0;

                foreach (var h in held)
                {
                    if (h.Holder == holder && h.Released > time && h.Time <= time)
                        total++;
                }

                int rings = Math.Clamp(Math.Min(DanmakusuBalance.GATHER_RINGS, total), 1, DanmakusuBalance.GATHER_RINGS);
                int n = 0;

                for (int i = 0; i < held.Count; i++)
                {
                    var h = held[i];

                    if (h.Holder != holder || h.Released <= time || h.Time > time)
                        continue;

                    h.Released = time;
                    held[i] = h;

                    // (dealt out in turn: the n-th goes into ring n mod rings, as its (n / rings)-th bullet; a ring has (total - ring + rings - 1) / rings of them)
                    int ring = n % rings, place = n / rings;
                    int inRing = (total - ring + rings - 1) / rings;
                    n++;

                    // (the second ring is turned half a step one way, the fourth the other way, so that together they stay even round the middle; a ring of one goes straight out)
                    float shift = inRing < 2 && !all ? 0f : ring % 4 == 1 ? 0.5f : ring % 4 == 3 ? -0.5f : 0f;
                    float angle = baseAngle + (all ? (place + shift) / inRing : (place + 0.5f + shift) / inRing - 0.5f) * spread;

                    Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    double fired = time + ring * gap;
                    Vector2 velocity = direction * speed;

                    var bullet = new StageBullet
                    {
                        T0 = fired,
                        P0 = origin + direction * edge, // (from the edge of what shoots it out, when it is a big round thing)
                        V = velocity,
                        Radius = h.Radius,
                        Hit = h.Radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                        Style = h.Style,
                        Shape = h.Shape,
                        Tint = h.Tint,
                        TintAmount = h.TintAmount,
                        Until = fired + f.stageLife(velocity),
                    };

                    shots.Add(new Shot { Release = time, Index = act.Bullets.Count, Bullet = bullet });
                    act.AddBullet(bullet);
                }

                if (n == 0)
                    return;

                // (what it took after the impact, in the same frame, stays: it goes at the next one)
                holding[holder] = 0;

                foreach (var h in held)
                {
                    if (h.Holder == holder && h.Released == double.PositiveInfinity)
                        holding[holder] = Math.Max(holding[holder], h.Slot + 1);
                }
            }

            /// <summary>Adds the bullets it holds at <paramref name="t"/> under the holder at <paramref name="centre"/> (of <paramref name="radius"/>), turned <paramref name="turn"/> radians.</summary>
            public void Beads(int holder, double t, Vector2 centre, float radius, float turn, float alpha, List<StageBead> into)
            {
                // (what is held is not seen: it is inside the world / the hand until it is shot out)
                if (!DanmakusuBalance.GATHER_SHOW_HELD || alpha <= 0.01f)
                    return;

                float reach = radius * DanmakusuBalance.GATHER_SPREAD;

                foreach (var h in held)
                {
                    if (h.Holder != holder || t < h.Time || t >= h.Released || into.Count >= 1200)
                        continue;

                    // (from the rim inwards: the first ones lie round the edge, where they are seen)
                    float r = reach * MathF.Sqrt(Math.Max(1f - (h.Slot + 0.5f) / max, 0f));
                    float angle = turn + h.Slot * 2.3999632f;
                    Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    into.Add(new StageBead
                    {
                        Position = centre + outward * r,
                        Radius = h.Radius,
                        Hit = 0f,
                        Fade = 1f - alpha,
                        Style = h.Style,
                        Shape = h.Shape,
                        Direction = outward,
                        Overlay = h.Tint,
                        OverlayAmount = h.TintAmount,
                        Harmless = true,
                    });
                }
            }

            /// <summary>Time went back to <paramref name="time"/>: what was gathered after it is forgotten (it is taken again), what was shot after it is held again and its bullets are gone.</summary>
            public void Reset(StageAct act, double time)
            {
                for (int i = shots.Count - 1; i >= 0; i--)
                {
                    if (shots[i].Release <= time)
                        continue;

                    int index = shots[i].Index;

                    if (index < act.Bullets.Count)
                    {
                        var gone = act.Bullets[index];
                        gone.Until = double.NegativeInfinity; // (it stays in the list, so that the numbers of the others stay the same)
                        act.Bullets[index] = gone;
                    }

                    shots.RemoveAt(i);
                }

                // (what is kept is numbered again, and what is held again gets its place under the holder again)
                var kept = new List<Held>(held.Count);

                foreach (var h in held)
                {
                    if (h.Time <= time)
                        kept.Add(h);
                }

                held.Clear();
                known.Clear();
                Array.Clear(holding);

                foreach (var h in kept)
                {
                    var k = h;

                    if (k.Released > time)
                    {
                        k.Released = double.PositiveInfinity;
                        k.Slot = holding[k.Holder]++;
                    }

                    known[k.Key] = held.Count;
                    held.Add(k);
                }
            }

            /// <summary>The act cleared its bullets (and makes them again): the bullets shot so far are put back.</summary>
            public void Readd(StageAct act)
            {
                for (int i = 0; i < shots.Count; i++)
                {
                    var shot = shots[i];
                    shot.Index = act.Bullets.Count;
                    shots[i] = shot;
                    act.AddBullet(shot.Bullet);
                }
            }

            /// <summary>Everything is forgotten (the act starts again from nothing).</summary>
            public void Clear()
            {
                held.Clear();
                known.Clear();
                shots.Clear();
                Array.Clear(holding);
            }
        }
    }
}
