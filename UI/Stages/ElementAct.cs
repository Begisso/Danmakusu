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
        // Patchouli Knowledge: she never pauses; every eight beats the element she works with changes: fire, water, wood, metal, earth, and from the start again.
        //  - Fire: ripples like Raiko's, smoother, at twice the speed. A bullet one goes over turns orange-red, and a red ring half an osu! circle radius wide shows round it. Eight beats later it grows to the size of the ring while it
        //    shakes, turns yellow (and stops shaking), blooms and glows, and then fades out.
        //  - Water: ripples like Raiko's, blue, four times as wide. Every second bullet a ripple catches gets a clone on it: on one of three rings across the band in turn (its middle and one to either side), at the
        //    next place of that ring's even spread over the part of the ripple that is on the screen, flying out with it, blue. For four beats the bullet goes on as it was and the clone is see-through and harmless; then the bullet is gone and the clone is solid. The others become eight times as slow and blue, and get their speed and colour back over sixteen beats.
        //  - Wood: the edges of the playfield are a dim brown. For eight beats every tenth bullet that leaves the playfield grows into a little tree on the edge it left by, over eight beats: a trunk with four branches, wood that
        //    hurts like a slider, with two green leaves (ellipses) growing on each of its five tips. When the tree is grown the leaves are thrown into the centre, in a 90 degree spread, at half the speed of the bullet that
        //    grew it, and the tree grows backwards until it is gone.
        //  - Metal: a ripple like Zanmu's (the same speed). A bullet it goes over flies on, and a knife appears on top of it that freezes at once, pointing at the player (it keeps pointing that way); eight beats later it is
        //    let go and flies on that way at the speed the bullet had.
        //  - Earth: every twentieth bullet on the screen while it lasts (any boss's, any object's) turns into a brown stone as big as an osu! circle that glows and blooms; where it hits the wall of the playfield it bursts into
        //    five bullets of the same kind and size as the bullet it was (away from the wall), and an orange flash of twice the stone's radius shows where.
        //  - The trees of wood glow (a warm core) and bloom, and so do their leaves.
        // ======================================================================================================================================================

        private sealed class ElementAct : StageAct
        {
            private enum Element
            {
                Fire,
                Water,
                Wood,
                Metal,
                Earth,
            }

            private sealed class Ripple
            {
                public double Time;
                public Element Kind;
                public Vector2 Centre;
                public bool Known;

                /// <summary>How many bullets this (water) ripple has caught: every second one rides it.</summary>
                public int Caught;
            }

            private sealed class Wood
            {
                public double Start;
                public Vector2 Root;
                public int Seed;
                public Vector2[][] Points = [];
                public float[][] Arc = [];
                public float[] Begin = [];
                public float[] Finish = [];
                public float[] Width = []; // (half width at the base of each branch, px)
            }

            private readonly BulletField field;
            private readonly double beat;
            private readonly double phase;
            private readonly List<Ripple> ripples = new List<Ripple>();
            private readonly List<Ripple> live = new List<Ripple>();
            private readonly List<int> fireBullets = new List<int>();
            private readonly List<Wood> woods = new List<Wood>();
            private readonly HashSet<(BulletEmitter, int)> knifed = new HashSet<(BulletEmitter, int)>(); // (the bullets metal has put a knife on)
            private int crossings;
            private int earthSequence; // (how many bullets of earth have been seen: every twentieth is a stone)
            private readonly List<(double Time, Vector2 Position, float Radius)> bursts = new List<(double Time, Vector2 Position, float Radius)>();
            private readonly Vector2[] revealPoints = new Vector2[16];
            private readonly float[] revealWidths = new float[16];
            private readonly float[] revealArcs = new float[16];

            private const int max_woods = 60;

            public ElementAct(BulletField field, BossPeriod period, double start, double end)
            {
                this.field = field;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                phase = beat * DanmakusuBalance.PATCHOULI_PHASE_BEATS;
                AbsorbFrom = start;
                AbsorbTo = end;

                int phases = (int)Math.Ceiling((end - start) / phase - 1e-9);

                for (int k = 0; k < phases; k++)
                {
                    var kind = elementOfPhase(k);
                    double at = start + k * phase;

                    if (kind == Element.Fire || kind == Element.Water)
                    {
                        for (double t = at; t < Math.Min(at + phase, end) - 1; t += beat * DanmakusuBalance.PATCHOULI_RIPPLE_EVERY_BEATS)
                            ripples.Add(new Ripple { Time = t, Kind = kind });
                    }
                    else if (kind == Element.Metal)
                        ripples.Add(new Ripple { Time = at, Kind = kind });
                }
            }

            private static Element elementOfPhase(int k) => (Element)(((k % 5) + 5) % 5);

            private int phaseAt(double t) => (int)Math.Floor((t - Start) / phase);

            private Element elementAt(double t) => elementOfPhase(phaseAt(t));

            private static float speedOf(Element kind) => kind switch
            {
                Element.Fire => DanmakusuBalance.PATCHOULI_FIRE_SPEED,
                Element.Water => DanmakusuBalance.PATCHOULI_WATER_SPEED,
                _ => DanmakusuBalance.PATCHOULI_METAL_SPEED,
            };

            private static float halfWidthOf(Element kind) => kind switch
            {
                Element.Fire => DanmakusuBalance.PATCHOULI_FIRE_HALF_WIDTH,
                Element.Water => DanmakusuBalance.PATCHOULI_WATER_HALF_WIDTH,
                _ => DanmakusuBalance.PATCHOULI_METAL_HALF_WIDTH,
            };

            private static Color4 colourOf(Element kind) => kind switch
            {
                Element.Fire => new Color4(1f, 0.42f, 0.08f, 1f),
                Element.Water => new Color4(0.25f, 0.55f, 1f, 1f),
                _ => new Color4(0.84f, 0.88f, 0.96f, 1f),
            };

            /// <summary>How long a ripple lasts: until its band has left the playfield altogether.</summary>
            private double lifeOf(BulletField f, Ripple ripple)
            {
                float dx = Math.Max(Math.Abs(ripple.Centre.X - f.stageMin.X), Math.Abs(ripple.Centre.X - f.stageMax.X));
                float dy = Math.Max(Math.Abs(ripple.Centre.Y - f.stageMin.Y), Math.Abs(ripple.Centre.Y - f.stageMax.Y));

                return (MathF.Sqrt(dx * dx + dy * dy) + 2f * halfWidthOf(ripple.Kind)) / speedOf(ripple.Kind);
            }

            private Vector2 bossNow(BulletField f) => f.bossPositionOf(Period);

            public override void Reset(BulletField f, double time)
            {
                foreach (var ripple in ripples)
                {
                    ripple.Known = false;
                    ripple.Caught = 0;
                }

                live.Clear();
                fireBullets.Clear();
                woods.Clear();
                knifed.Clear();
                bursts.Clear();
                crossings = 0;
                earthSequence = 0;
                ClearBullets();

                foreach (var emitter in f.emitters)
                    emitter.EarthBase = -1;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                live.Clear();

                foreach (var ripple in ripples)
                {
                    if (now < ripple.Time)
                    {
                        ripple.Known = false;
                        continue;
                    }

                    if (!ripple.Known)
                    {
                        ripple.Known = true;
                        ripple.Centre = bossNow(f);
                    }

                    if (now - ripple.Time <= lifeOf(f, ripple))
                        live.Add(ripple);
                }
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                if (elementAt(t) == Element.Wood && crossedOut(f, from, to))
                    return sowTree(f, t, from, to, emitter, bullet);

                // earth: every twentieth bullet of the phase, whoever shot it, is a stone
                if (emitter.Time >= Start && elementAt(emitter.Time) == Element.Earth)
                {
                    if (emitter.EarthBase < 0)
                    {
                        emitter.EarthBase = earthSequence;
                        earthSequence += emitter.Count;
                    }

                    if ((emitter.EarthBase + bullet + 1) % DanmakusuBalance.PATCHOULI_EARTH_EVERY == 0)
                        return stone(f, t, from, to, emitter, bullet);
                }

                foreach (var ripple in live)
                {
                    double age = t - ripple.Time;

                    if (age <= 0)
                        continue;

                    float front = speedOf(ripple.Kind) * (float)age;
                    float away = Vector2.Distance(to, ripple.Centre);

                    if (MathF.Abs(away - front) > halfWidthOf(ripple.Kind))
                        continue;

                    Vector2 delta = to - from;

                    if (delta.LengthSquared < 1e-8f)
                        return false;

                    var stored = f.makeStored(emitter, t);
                    float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);

                    switch (ripple.Kind)
                    {
                        case Element.Fire:
                            return burn(f, t, to, Vector2.Normalize(delta) * speed, stored, emitter, bullet);

                        case Element.Water:
                            // (every second bullet it catches rides the ripple; the others are slowed down)
                            if (ripple.Caught++ % 2 == 1)
                                return ride(f, t, ripple, front, to, Vector2.Normalize(delta) * speed, stored, emitter, bullet);

                            return soak(f, t, to, Vector2.Normalize(delta) * speed, stored, emitter, bullet);

                        default:
                            // (the bullet flies on: a knife is put on top of it, once)
                            if (knifed.Add((emitter, bullet)))
                                knife(f, t, to, Vector2.Normalize(delta) * speed, stored, emitter, bullet);
                            return false;
                    }
                }

                return false;
            }

            // ---- fire ----

            private bool burn(BulletField f, double t, Vector2 at, Vector2 velocity, in StoredBullet stored, BulletEmitter emitter, int bullet)
            {
                var b = f.makeStageBullet(stored, emitter, bullet, t, at, velocity);
                double grows = t + beat * DanmakusuBalance.PATCHOULI_FIRE_DELAY_BEATS;
                double grown = grows + DanmakusuBalance.PATCHOULI_FIRE_GROW_MS;
                double fades = grown + DanmakusuBalance.PATCHOULI_FIRE_HOLD_MS;
                float ring = f.circleRadius * DanmakusuBalance.PATCHOULI_FIRE_RING_CIRCLES;

                b.Tint = BulletRenderer.TINT_FIRE;
                b.TintAmount = 1f;
                b.FadeIn = 250;
                b.ScaleFrom = grows;
                b.ScaleOver = DanmakusuBalance.PATCHOULI_FIRE_GROW_MS;
                b.ScaleTo = Math.Max(ring / Math.Max(b.Radius, 1f), 1f);
                b.ShakeFrom = grows;
                b.ShakeTo = grown;
                b.Tint2At = grown;
                b.Tint2 = BulletRenderer.TINT_YELLOW;
                b.Tint2Amount = 1f;
                b.Tint2Over = 150;
                b.BloomAt = grown;
                b.BloomTo = DanmakusuBalance.PATCHOULI_FIRE_BLOOM;
                b.AlphaFrom = fades;
                b.AlphaOver = DanmakusuBalance.PATCHOULI_FIRE_FADE_MS;
                b.Until = Math.Min(b.Until, fades + DanmakusuBalance.PATCHOULI_FIRE_FADE_MS);

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                fireBullets.Add(index);
                return true;
            }

            // ---- water ----

            private bool soak(BulletField f, double t, Vector2 at, Vector2 velocity, in StoredBullet stored, BulletEmitter emitter, int bullet)
            {
                var b = f.makeStageBullet(stored, emitter, bullet, t, at, velocity / DanmakusuBalance.PATCHOULI_WATER_SLOWDOWN);
                double recover = beat * DanmakusuBalance.PATCHOULI_WATER_RECOVER_BEATS;

                // (it speeds up evenly from an eighth of its speed to all of it, and flies on at that)
                b.Acc = velocity * ((1f - 1f / DanmakusuBalance.PATCHOULI_WATER_SLOWDOWN) / (float)recover);
                b.Switch = t + recover;
                b.V2 = velocity;
                b.Tint = BulletRenderer.TINT_BLUE;
                b.TintAmount = 1f;
                b.Tint2At = t;
                b.Tint2 = BulletRenderer.TINT_BLUE;
                b.Tint2Amount = 0f;
                b.Tint2Over = recover;

                // (it swells to one and a half times its size at once and shrinks back as it recovers)
                b.ScaleFrom = t;
                b.ScaleOver = DanmakusuBalance.PATCHOULI_WATER_SWELL_MS;
                b.ScaleTo = DanmakusuBalance.PATCHOULI_WATER_SIZE;
                b.ScaleBackAt = t;
                b.ScaleBackOver = recover;
                b.Until = t + recover + f.stageLife(velocity);

                AddBullet(b);
                return true;
            }

            /// <summary>
            /// The bullet gets a clone on the ripple: on the middle of the band, at the next place of an even spread over the part of it that is on the screen (the places follow the golden ratio, so
            /// however many it catches they are spread evenly), blue, flying out with the band. For four beats the bullet itself goes on as it was (where it was, the way it flew, as harmful as
            /// before) and the clone is see-through and harmless; then the bullet is gone and the clone takes its place, solid.
            /// </summary>
            private bool ride(BulletField f, double t, Ripple ripple, float front, Vector2 at, Vector2 velocity, in StoredBullet stored, BulletEmitter emitter, int bullet)
            {
                double replaces = t + beat * DanmakusuBalance.PATCHOULI_RIDE_GHOST_BEATS;

                // the bullet itself: as it was, until the clone takes over
                var original = f.makeStageBullet(stored, emitter, bullet, t, at, velocity);
                original.Until = Math.Min(original.Until, replaces);
                AddBullet(original);

                // the clone on the ripple: on one of its rings in turn (the middle of the band and one to either side of it), at the next place of that ring's own even spread
                int k = ripple.Caught / 2;
                int ring = k % DanmakusuBalance.PATCHOULI_WATER_RINGS;
                float across = (ring - (DanmakusuBalance.PATCHOULI_WATER_RINGS - 1) * 0.5f) * DanmakusuBalance.PATCHOULI_WATER_RING_GAP * DanmakusuBalance.PATCHOULI_WATER_HALF_WIDTH;
                float radius = Math.Max(front + across, 1f);
                float angle = visibleAngle(f, ripple.Centre, radius, k / DanmakusuBalance.PATCHOULI_WATER_RINGS * golden_ratio % 1f);
                Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 riding = direction * DanmakusuBalance.PATCHOULI_WATER_SPEED;
                var clone = f.makeStageBullet(stored, emitter, bullet, t, ripple.Centre + direction * radius, riding);

                clone.Tint = BulletRenderer.TINT_BLUE;
                clone.TintAmount = 1f;
                clone.FadeIn = 150;
                clone.GhostUntil = replaces; // (see-through and harmless until it takes the bullet's place)

                // (it stays on the ring for four beats after it has taken the bullet's place, and then flies on the way the bullet flew when the ripple caught it, as fast)
                double leaves = replaces + beat * DanmakusuBalance.PATCHOULI_RIDE_STAY_BEATS;
                clone.Switch = leaves;
                clone.V2 = velocity;
                clone.Until = leaves + f.stageLife(velocity) + f.stageLife(riding);
                clone.GhostVisible = DanmakusuBalance.PATCHOULI_RIDE_GHOST_VISIBLE;

                AddBullet(clone);
                return true;
            }

            private const float golden_ratio = 0.618034f;
            private const int arc_samples = 128;

            /// <summary>The angle of the place a share <paramref name="share"/> (0..1) along the part of the circle round <paramref name="centre"/> that is on the screen (all of it when none is).</summary>
            private static float visibleAngle(BulletField f, Vector2 centre, float radius, float share)
            {
                Vector2 low = f.hasScreenBounds ? f.screenMin : f.stageMin, high = f.hasScreenBounds ? f.screenMax : f.stageMax;
                float step = MathF.Tau / arc_samples;
                int visible = 0;

                for (int i = 0; i < arc_samples; i++)
                {
                    if (onScreen(centre, radius, i * step, low, high))
                        visible++;
                }

                if (visible == 0)
                    return share * MathF.Tau;

                float place = share * visible;
                int target = Math.Min((int)place, visible - 1);

                for (int i = 0, seen = 0; i < arc_samples; i++)
                {
                    if (!onScreen(centre, radius, i * step, low, high))
                        continue;

                    if (seen++ == target)
                        return (i + (place - target)) * step;
                }

                return share * MathF.Tau;
            }

            private static bool onScreen(Vector2 centre, float radius, float angle, Vector2 low, Vector2 high)
            {
                Vector2 p = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                return p.X >= low.X && p.X <= high.X && p.Y >= low.Y && p.Y <= high.Y;
            }

            // ---- metal ----

            private void knife(BulletField f, double t, Vector2 at, Vector2 velocity, in StoredBullet stored, BulletEmitter emitter, int bullet)
            {
                Vector2 toPlayer = f.characterPosition - at;
                Vector2 aim = toPlayer.LengthSquared > 1e-4f ? Vector2.Normalize(toPlayer) : Vector2.Normalize(velocity);
                float speed = velocity.Length;
                var b = f.makeStageBullet(stored, emitter, bullet, t, at, Vector2.Zero);
                double holds = t + beat * DanmakusuBalance.PATCHOULI_METAL_HOLD_BEATS;

                b.Shape = BulletShape.Knife;
                b.Blades = 1;
                b.BladeSpin = 0f;
                b.BladeAngle0 = 0f;
                b.Face = aim; // (it points at the player, and keeps pointing that way)
                b.Switch = holds;
                b.V2 = aim * speed;
                b.ShakeFrom = Math.Max(holds - beat * DanmakusuBalance.FROZEN_SHAKE_BEATS, t);
                b.ShakeTo = holds;
                b.Until = holds + f.stageLife(b.V2);

                AddBullet(b);
            }

            // ---- earth ----

            /// <summary>The bullet is a stone: brown, glowing, flying straight to the wall; there it bursts (the burst is made now: it is a closed form of time).</summary>
            private bool stone(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (outside(f, to))
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                Vector2 direction = Vector2.Normalize(delta);
                Vector2 velocity = direction * speed;
                Vector2 low = f.stageMin, high = f.stageMax;
                float tx = velocity.X > 1e-6f ? (high.X - to.X) / velocity.X : velocity.X < -1e-6f ? (low.X - to.X) / velocity.X : float.PositiveInfinity;
                float ty = velocity.Y > 1e-6f ? (high.Y - to.Y) / velocity.Y : velocity.Y < -1e-6f ? (low.Y - to.Y) / velocity.Y : float.PositiveInfinity;
                float reach = Math.Max(Math.Min(tx, ty), 0f);

                if (float.IsInfinity(reach))
                    return false;

                double bursts = t + reach;
                Vector2 wall = to + velocity * reach;
                var b = f.makeStageBullet(stored, emitter, bullet, t, to, velocity);

                // (a stone is as big as an osu! circle; its hitbox keeps the bullet's share of its size)
                float hitShare = b.Radius > 1e-3f ? b.Hit / b.Radius : DanmakusuBalance.BULLET_HITBOX_SCALE;
                b.Radius = f.circleRadius * DanmakusuBalance.PATCHOULI_EARTH_SIZE;
                b.Hit = b.Radius * hitShare;
                b.Tint = BulletRenderer.TINT_BROWN;
                b.TintAmount = 1f;
                b.Bloom = DanmakusuBalance.PATCHOULI_EARTH_BLOOM;
                b.Until = bursts;

                int index = Bullets.Count;
                AddBullet(b);

                if (Bullets.Count == index)
                    return false;

                // the burst: bullets of the same kind at half the size, away from the wall
                Vector2 away = tx <= ty ? new Vector2(velocity.X > 0f ? -1f : 1f, 0f) : new Vector2(0f, velocity.Y > 0f ? -1f : 1f);
                float centreAngle = MathF.Atan2(away.Y, away.X);
                float spread = DanmakusuBalance.PATCHOULI_EARTH_SPREAD_DEGREES * MathF.PI / 180f;
                int count = DanmakusuBalance.PATCHOULI_EARTH_CHILDREN;

                for (int i = 0; i < count; i++)
                {
                    float angle = centreAngle + (count > 1 ? (i / (count - 1f) - 0.5f) * spread : 0f);
                    Vector2 flung = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                    AddBullet(new StageBullet
                    {
                        T0 = bursts,
                        P0 = wall,
                        V = flung,
                        Radius = stored.Radius, // (as big as the bullet the stone was)
                        Hit = stored.Hit,
                        Style = stored.Style,
                        Shape = stored.Shape,
                        Tint = stored.Tint,
                        TintAmount = stored.TintAmount,
                        Shade = stored.Shade,
                        Until = bursts + f.stageLife(flung),
                    });
                }

                this.bursts.Add((bursts, wall, b.Radius));
                return true;
            }

            // ---- wood ----

            private static bool outside(BulletField f, Vector2 p) => p.X < f.stageMin.X || p.X > f.stageMax.X || p.Y < f.stageMin.Y || p.Y > f.stageMax.Y;

            private static bool crossedOut(BulletField f, Vector2 from, Vector2 to) => outside(f, to) && !outside(f, from);

            private bool sowTree(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (++crossings % DanmakusuBalance.PATCHOULI_TREE_EVERY != 0 || woods.Count >= max_woods)
                    return false;

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                float big = emitter.BigScale(bullet);
                Vector2 low = f.stageMin, high = f.stageMax;

                // the edge it left by: the one it is farthest out of
                float outX = Math.Max(low.X - to.X, to.X - high.X);
                float outY = Math.Max(low.Y - to.Y, to.Y - high.Y);
                Vector2 inward;
                Vector2 root = new Vector2(Math.Clamp(to.X, low.X, high.X), Math.Clamp(to.Y, low.Y, high.Y));

                if (outX >= outY)
                    inward = to.X < low.X ? new Vector2(1f, 0f) : new Vector2(-1f, 0f);
                else
                    inward = to.Y < low.Y ? new Vector2(0f, 1f) : new Vector2(0f, -1f);

                var wood = buildTree(f, t, root, inward, crossings);
                woods.Add(wood);

                // the leaves: a green ellipse on each tip, growing when its branch has grown; thrown into the centre when the tree is grown
                double grown = wood.Start + beat * DanmakusuBalance.PATCHOULI_TREE_GROW_BEATS;
                Vector2 middle = (low + high) * 0.5f;
                Vector2 toMiddle = middle - root;
                float centreAngle = MathF.Atan2(toMiddle.Y, toMiddle.X);
                int perTip = DanmakusuBalance.PATCHOULI_LEAVES_PER_TIP;
                int leaves = wood.Points.Length * perTip;
                float spread = DanmakusuBalance.PATCHOULI_LEAF_SPREAD_DEGREES * MathF.PI / 180f;
                float leafRadius = stored.Radius * big * DanmakusuBalance.PATCHOULI_LEAF_SIZE;

                for (int i = 0; i < leaves; i++)
                {
                    int tipIndex = i / perTip, onTip = i % perTip;
                    var branch = wood.Points[tipIndex];
                    Vector2 tip = branch[^1];
                    Vector2 outward = tip - branch[^2];
                    outward = outward.LengthSquared > 1e-6f ? Vector2.Normalize(outward) : inward;

                    // (the leaves of a tip sit side by side across it, turned a little away from each other)
                    float across = perTip > 1 ? (onTip / (perTip - 1f) - 0.5f) : 0f;
                    Vector2 sideways = new Vector2(-outward.Y, outward.X);
                    Vector2 face = Vector2.Normalize(outward + sideways * (across * 0.8f));
                    Vector2 at = tip + sideways * (across * leafRadius * 1.6f);

                    float angle = centreAngle + (leaves > 1 ? (i / (leaves - 1f) - 0.5f) * spread : 0f);
                    Vector2 thrown = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (speed * DanmakusuBalance.PATCHOULI_LEAF_SPEED);
                    double leafStart = wood.Start + beat * DanmakusuBalance.PATCHOULI_TREE_GROW_BEATS * wood.Finish[tipIndex];

                    AddBullet(new StageBullet
                    {
                        T0 = leafStart,
                        P0 = at,
                        V = Vector2.Zero,
                        Radius = leafRadius,
                        Hit = stored.Hit * big * DanmakusuBalance.PATCHOULI_LEAF_SIZE,
                        Style = 0,
                        Shape = BulletShape.Ellipse,
                        Tint = BulletRenderer.TINT_GREEN,
                        TintAmount = 1f,
                        Bloom = DanmakusuBalance.PATCHOULI_TREE_BLOOM, // (the leaves glow like their tree)
                        Face = face,
                        Grow = beat * DanmakusuBalance.PATCHOULI_TREE_GROW_BEATS * 0.12,
                        Switch = grown,
                        V2 = thrown,
                        Until = grown + f.stageLife(thrown),
                    });
                }

                return true;
            }

            /// <summary>A little tree: a trunk (a gently curved line growing inwards from the edge) and two pairs of branches, one pair a bit higher up than the other; the tips of all five carry a leaf.</summary>
            private Wood buildTree(BulletField f, double t, Vector2 root, Vector2 inward, int seed)
            {
                float length = f.circleRadius * DanmakusuBalance.PATCHOULI_TREE_LENGTH_CIRCLES;
                Vector2 side = new Vector2(-inward.Y, inward.X);
                float lean = (hash01(seed, 1, 5) - 0.5f) * 0.5f;

                Vector2[] trunk = new Vector2[7];

                for (int i = 0; i < trunk.Length; i++)
                {
                    float s = i / (trunk.Length - 1f);
                    trunk[i] = root + inward * (length * s) + side * (length * (lean * s * s + 0.05f * MathF.Sin(s * 5f + seed)));
                }

                Vector2 at(float s)
                {
                    float x = s * (trunk.Length - 1);
                    int i = Math.Min((int)x, trunk.Length - 2);
                    return Vector2.Lerp(trunk[i], trunk[i + 1], x - i);
                }

                Vector2[] branch(float from, float sign, float turn, float size)
                {
                    var points = new Vector2[5];
                    Vector2 start = at(from);
                    Vector2 heading = Vector2.Normalize(inward + side * sign * MathF.Tan(turn + (hash01(seed, (int)(from * 100), 9) - 0.5f) * 0.3f));
                    Vector2 bend = new Vector2(-heading.Y, heading.X) * -sign;

                    for (int i = 0; i < points.Length; i++)
                    {
                        float s = i / (points.Length - 1f);
                        points[i] = start + heading * (length * size * s) + bend * (length * size * 0.18f * s * s);
                    }

                    return points;
                }

                var wood = new Wood { Start = t, Root = root, Seed = seed };
                wood.Points = new[]
                {
                    trunk,
                    branch(0.42f, 1f, 0.9f, 0.5f),
                    branch(0.42f, -1f, 0.9f, 0.5f),
                    branch(0.72f, 1f, 0.8f, 0.4f),
                    branch(0.72f, -1f, 0.8f, 0.4f),
                };
                wood.Begin = new[] { 0f, 0.30f, 0.30f, 0.50f, 0.50f };
                wood.Finish = new[] { 0.55f, 0.75f, 0.75f, 0.95f, 0.95f };
                wood.Width = new[] { f.circleRadius * 0.20f, f.circleRadius * 0.10f, f.circleRadius * 0.10f, f.circleRadius * 0.08f, f.circleRadius * 0.08f };
                wood.Arc = new float[wood.Points.Length][];

                for (int i = 0; i < wood.Points.Length; i++)
                {
                    var points = wood.Points[i];
                    var arc = new float[points.Length];

                    for (int k = 1; k < points.Length; k++)
                        arc[k] = arc[k - 1] + Vector2.Distance(points[k - 1], points[k]);

                    wood.Arc[i] = arc;
                }

                return wood;
            }

            /// <summary>How far the tree has grown at <paramref name="t"/> (0..1): over eight beats, and backwards over the next eight.</summary>
            private float growthOf(Wood wood, double t)
            {
                double grow = beat * DanmakusuBalance.PATCHOULI_TREE_GROW_BEATS;
                double age = t - wood.Start;

                if (age <= 0)
                    return 0f;

                if (age <= grow)
                    return (float)(age / grow);

                return (float)Math.Max(1.0 - (age - grow) / grow, 0.0);
            }

            private bool woodLives(Wood wood, double t) => t >= wood.Start && t < wood.Start + 2 * beat * DanmakusuBalance.PATCHOULI_TREE_GROW_BEATS;

            /// <summary>The part of a branch that has grown (<paramref name="p"/>, 0..1 of its length) as points and half widths in the scratch arrays; the tip is sharp while it grows.</summary>
            private int reveal(Wood wood, int branch, float p)
            {
                var points = wood.Points[branch];
                var arc = wood.Arc[branch];
                float total = arc[^1];
                float reach = Math.Clamp(p, 0f, 1f) * total;
                int n = 0;

                for (int i = 0; i < points.Length && n < revealPoints.Length - 1; i++)
                {
                    if (arc[i] > reach + 1e-4f)
                        break;

                    revealPoints[n] = points[i];
                    revealArcs[n] = arc[i];
                    n++;
                }

                if (n < points.Length && n >= 1 && reach > arc[n - 1] + 1e-3f)
                {
                    float span = arc[n] - arc[n - 1];
                    revealPoints[n] = Vector2.Lerp(points[n - 1], points[n], span > 1e-4f ? (reach - arc[n - 1]) / span : 0f);
                    revealArcs[n] = reach;
                    n++;
                }

                float tip = Math.Max(total * 0.3f, 1f);

                for (int i = 0; i < n; i++)
                {
                    float shape = 1f - 0.65f * (revealArcs[i] / Math.Max(total, 1f)); // (thinner towards the tip)
                    float taper = p >= 1f ? 1f : Math.Clamp((reach - revealArcs[i]) / tip + 0.1f, 0.1f, 1f);
                    revealWidths[i] = Math.Max(wood.Width[branch] * shape * taper, 0.4f);
                }

                return n;
            }

            private float progressOf(Wood wood, int branch, float u) => Math.Min(Math.Max((u - wood.Begin[branch]) / Math.Max(wood.Finish[branch] - wood.Begin[branch], 1e-4f), 0f), 1f);

            /// <summary>Adds the wood of the little trees to the beams that are drawn (like Yuyuko's tree).</summary>
            public void AddWood(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                foreach (var wood in woods)
                {
                    if (!woodLives(wood, t))
                        continue;

                    float u = growthOf(wood, t);

                    for (int branch = 0; branch < wood.Points.Length; branch++)
                    {
                        float p = progressOf(wood, branch, u);

                        if (p <= 0f)
                            continue;

                        int n = reveal(wood, branch, p);

                        if (n >= 2)
                            f.addWoodBeam(revealPoints, revealWidths, n, ref beams, ref pointTotal, true);
                    }
                }
            }

            public override bool Hurts(BulletField f, double now)
            {
                if (woods.Count == 0)
                    return false;

                Vector2 player = f.characterPosition;
                float hit = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var wood in woods)
                {
                    if (!woodLives(wood, now) || Vector2.Distance(player, wood.Root) > f.circleRadius * (DanmakusuBalance.PATCHOULI_TREE_LENGTH_CIRCLES + 2f))
                        continue;

                    float u = growthOf(wood, now);

                    for (int branch = 0; branch < wood.Points.Length; branch++)
                    {
                        float p = progressOf(wood, branch, u);

                        if (p <= 0f)
                            continue;

                        int n = reveal(wood, branch, p);

                        for (int k = 0; k + 1 < n; k++)
                        {
                            if (distanceToSegment(player, revealPoints[k], revealPoints[k + 1]) <= Math.Max(revealWidths[k], revealWidths[k + 1]) + hit)
                                return true;
                        }
                    }
                }

                return false;
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                // the ripples: one smooth band each, in the colour of its element (the middle of the band is where bullets are touched)
                foreach (var ripple in live)
                {
                    double age = t - ripple.Time;

                    if (age <= 0)
                        continue;

                    float half = halfWidthOf(ripple.Kind);
                    float r = speedOf(ripple.Kind) * (float)age;
                    float alpha = DanmakusuBalance.PATCHOULI_RIPPLE_ALPHA * smoothStep(age / 150.0);

                    f.stageRipple.AddRing(ripple.Centre, r + half, half, alpha, colourOf(ripple.Kind));
                }

                // the orange flash where a stone has burst
                foreach (var (time, position, size) in bursts)
                {
                    double age = t - time;

                    if (age < 0 || age > DanmakusuBalance.PATCHOULI_EARTH_FLASH_MS)
                        continue;

                    float level = smoothStep(age / 40.0) * (1f - smoothStep(age / DanmakusuBalance.PATCHOULI_EARTH_FLASH_MS));
                    float diameter = 2f * DanmakusuBalance.PATCHOULI_EARTH_FLASH_RADIUS * size;

                    f.stageBack.Add(StageTexture.Glow, position, new Vector2(diameter * 1.15f), 0f, 0.95f * level, new Color4(1f, 0.55f, 0.1f, 1f), true);
                    f.stageBack.Add(StageTexture.Glow, position, new Vector2(diameter * 0.6f), 0f, 0.8f * level, new Color4(1f, 0.85f, 0.45f, 1f), true);
                }

                // the red ring round every bullet that fire has touched: it is there until the bullet has grown to it
                double growMs = DanmakusuBalance.PATCHOULI_FIRE_GROW_MS;
                float ringRadius = f.circleRadius * DanmakusuBalance.PATCHOULI_FIRE_RING_CIRCLES;
                float ringHalf = Math.Max(ringRadius * 0.07f, 1f);
                int rings = 0;

                foreach (int index in fireBullets)
                {
                    if (rings >= DanmakusuBalance.PATCHOULI_FIRE_MAX_RINGS)
                        break;

                    var bullet = Bullets[index];
                    double grows = bullet.ScaleFrom;

                    if (t < bullet.T0 || t > grows + growMs || (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + index)))
                        continue;

                    Vector2 position = stageBulletPosition(bullet, t);

                    if (f.hasScreenBounds && (position.X < f.screenMin.X - ringRadius || position.X > f.screenMax.X + ringRadius || position.Y < f.screenMin.Y - ringRadius || position.Y > f.screenMax.Y + ringRadius))
                        continue;

                    float alpha = 0.9f * smoothStep((t - bullet.T0) / 250.0) * (1f - smoothStep((t - grows) / growMs));

                    // (before it goes off the ring flashes: on and off, faster and faster, and brighter)
                    double flashSpan = beat * DanmakusuBalance.PATCHOULI_FIRE_FLASH_BEATS;
                    double flash = (t - (grows - flashSpan)) / flashSpan;

                    if (flash > 0 && t < grows)
                    {
                        double phase = DanmakusuBalance.PATCHOULI_FIRE_FLASHES * flash * flash * Math.PI * 2;
                        float on = 0.5f + 0.5f * (float)Math.Cos(phase);
                        alpha = Math.Min(1f, alpha * (0.35f + 0.65f * on) + 0.3f * on * (float)flash);
                        f.stageRipple.AddRing(position, ringRadius + ringHalf * 2f, ringHalf * 2f, 0.5f * on * (float)flash, new Color4(1f, 0.55f, 0.35f, 1f));
                    }

                    if (alpha > 0.02f)
                    {
                        f.stageRipple.AddRing(position, ringRadius + ringHalf, ringHalf, alpha, new Color4(0.95f, 0.1f, 0.06f, 1f));
                        rings++;
                    }
                }

                // wood: the edges of the playfield are a dim brown
                int k = phaseAt(t);
                double phaseStart = Start + k * phase;
                float presence = elementAt(t) == Element.Wood && t >= Start && t < End
                    ? smoothStep((t - phaseStart) / DanmakusuBalance.STAGE_FADE_IN_MS) * smoothStep((phaseStart + phase - t) / DanmakusuBalance.STAGE_FADE_OUT_MS)
                    : 0f;

                if (presence > 0.01f)
                {
                    Vector2 low = f.stageMin, high = f.stageMax;
                    float width = high.X - low.X, height = high.Y - low.Y;
                    float depth = Math.Min(width, height) * DanmakusuBalance.PATCHOULI_EDGE_DEPTH;
                    var brown = new Color4(0.30f, 0.17f, 0.07f, 1f);
                    float alpha = DanmakusuBalance.PATCHOULI_EDGE_ALPHA * presence;

                    // (the column of light is bright at its left end and fades away along it: turned so that the bright end is on the edge)
                    f.stageBack.Add(StageTexture.Light, new Vector2(low.X + depth * 0.5f, (low.Y + high.Y) * 0.5f), new Vector2(depth, height), 0f, alpha, brown);
                    f.stageBack.Add(StageTexture.Light, new Vector2(high.X - depth * 0.5f, (low.Y + high.Y) * 0.5f), new Vector2(depth, height), 180f, alpha, brown);
                    f.stageBack.Add(StageTexture.Light, new Vector2((low.X + high.X) * 0.5f, low.Y + depth * 0.5f), new Vector2(depth, width), 90f, alpha, brown);
                    f.stageBack.Add(StageTexture.Light, new Vector2((low.X + high.X) * 0.5f, high.Y - depth * 0.5f), new Vector2(depth, width), 270f, alpha, brown);
                }
            }
        }
    }
}
