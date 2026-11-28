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
        // Rikako Asakura (2026-11-26, the user's pictures of the Earth from orbit, of asteroids and of a satellite).
        // The Earth: in every attack of hers the picture of the Earth seen from orbit lies over the whole screen, RIKAKO_EARTH_ALPHA see-through, under the bullets; it fades in over a beat as the attack
        // begins, with the sound of Yuyuko's tree growing, and out over a beat as it ends.
        // The asteroids: all through her section (Bullets/Asteroids.cs: closed forms, by a hash) rocks cut out of the user's picture fly across the screen from every side, each its own size, way, speed and
        // turn; they fade in as her section begins and out as it ends. Every bullet that flies into one bounces off it (BossOps: TrajectoryOpKind.Rocks); they do not hurt.
        // The satellites: as each attack begins a satellite (the user's picture, RIKAKO_SATELLITE_CIRCLES circle radii across) comes in slowly from behind her, from out of the screen - on the line from the
        // player through her - and flies on that way, at RIKAKO_SATELLITE_SPEED circle radii a beat, straight, until it has left the screen on the other side; it fades in over two beats. It faces the
        // player as it comes and is given the way to the player again every RIKAKO_TURN_EVERY_BEATS (it changes its mind only then, "can only change direction every 4 beats"); since 2026-11-26 it
        // turns towards that way evenly, at RIKAKO_TURN_DEGREES_PER_BEAT (it eased over the four beats before). Every
        // RIKAKO_BEAM_EVERY_BEATS a big beam (RIKAKO_BEAM_CIRCLES circle radii to each side of its middle line) both ways out of it, ahead and behind, along the way it faces, turning with it: a see-through
        // preview for RIKAKO_BEAM_PREVIEW_BEATS, then solid for RIKAKO_BEAM_ON_BEATS, with the Master Spark's sound and a shake (RIKAKO_SHAKE). Since 2026-11-26 the beam is narrow where it leaves the
        // satellite and widens as Marisa's spark does (RIKAKO_BEAM_ROOT, over RIKAKO_BEAM_GROW_CIRCLES), and the satellite is drawn over it (and over the bullets).
        // Since 2026-11-27 it turns half as fast again, and when it starts turning the other way round its grid flashes (RIKAKO_TURN_FLASH_BEATS); twice as many asteroids, half as big.
        // Since 2026-11-28 ("satelite turn frequency halved. Turn speed halved. Movement speed halved"): a new way every eight beats, turning half as fast again, flying half as fast; the flash is a
        // white light round every line of the grid (LaserInstance.BloomUnfaded, RIKAKO_FLASH_BLOOM), not the lines themselves turning solid white.
        // A square grid of beams (lines RIKAKO_GRID_CIRCLES circle radii apart) over the whole screen goes with every satellite, turned and moved with it: see-through (RIKAKO_GRID_ALPHA) and harmless.
        // Every RIKAKO_WAVE_EVERY_BEATS of its flight the satellite sends out a thick grey wave (RIKAKO_WAVE_CIRCLES circle radii, see-through), with Byakuren's beam sound, that runs out to past the
        // farthest corner of the screen in RIKAKO_WAVE_BEATS; under it the grid turns solid - softly at the wave's edges - and goes see-through again behind it. Solid, the grid and the beam hurt like a
        // slider body.
        // Where the player is and where she is are taken when a satellite comes and every time it turns, and kept.
        // ======================================================================================================================================================

        private sealed class SpaceAct : StageAct
        {
            private sealed class Satellite
            {
                public double Came;
                public bool Known;
                public Vector2 From; // (where it is as it comes, out of the screen)
                public Vector2 Way; // (the way it flies, for good)
                public double Gone;
                public readonly List<float> Targets = new List<float>(); // (the way it turns to at every turn: the first is the way it faces as it comes)
            }

            private readonly double beat;
            private readonly (double Start, double End)[] attacks;
            private readonly Satellite[] satellites;
            private readonly Asteroid[] rocks;
            private readonly double rocksLongest;
            private readonly HashSet<long> sounded = new HashSet<long>();
            private double resetTime = double.NegativeInfinity;

            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> fades = new List<float>();
            private readonly List<float> beamWidths = new List<float>();
            private readonly List<float> stops = new List<float>();

            public SpaceAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.Start;
                attacks = BossOps.Attacks(period).FindAll(a => a.End > a.Start).ToArray();
                satellites = new Satellite[attacks.Length];

                for (int k = 0; k < attacks.Length; k++)
                    satellites[k] = new Satellite { Came = attacks[k].Start };

                rocks = Asteroids.Of(period);
                rocksLongest = Asteroids.Longest(period);

                // (a satellite flies on until it has left the screen, also after her section: at most across the screen and back out from where it came)
                double longest = 2.0 * 900.0 / Math.Max(speedOf(period), 1e-4);
                End = Math.Max(period.End, attacks.Length > 0 ? attacks[^1].Start + longest : period.End);
            }

            private static float speedOf(BossPeriod period) => period.CircleRadius * DanmakusuBalance.RIKAKO_SATELLITE_SPEED / (float)Math.Max(period.BeatLength, 1);

            private float speed(BulletField f) => f.circleRadius * DanmakusuBalance.RIKAKO_SATELLITE_SPEED / (float)beat;

            private float satelliteSize(BulletField f) => f.circleRadius * DanmakusuBalance.RIKAKO_SATELLITE_CIRCLES;

            private float beamHalf(BulletField f) => f.circleRadius * DanmakusuBalance.RIKAKO_BEAM_CIRCLES;

            private float gridHalf(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            private double turnMs => beat * DanmakusuBalance.RIKAKO_TURN_EVERY_BEATS;

            public override void Reset(BulletField f, double time)
            {
                // (a satellite that came by then keeps where it came from and the turns it made by then; the later ones are taken again)
                foreach (var s in satellites)
                {
                    if (s.Came > time)
                    {
                        s.Known = false;
                        s.Targets.Clear();
                        continue;
                    }

                    int keep = Math.Max(1, (int)Math.Floor((time - s.Came) / turnMs) + 1);

                    if (s.Targets.Count > keep)
                        s.Targets.RemoveRange(keep, s.Targets.Count - keep);
                }

                sounded.Clear();
                resetTime = time;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                for (int k = 0; k < satellites.Length; k++)
                {
                    var s = satellites[k];

                    if (now < s.Came)
                        break;

                    if (!s.Known)
                        come(f, s);

                    if (now >= s.Gone)
                        continue;

                    // (every turn whose time has come: at the player as it is then, from where the satellite is)
                    while (s.Came + s.Targets.Count * turnMs <= now)
                    {
                        Vector2 at = positionOf(f, s, s.Came + s.Targets.Count * turnMs);
                        Vector2 toPlayer = f.characterPosition - at;
                        s.Targets.Add(toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : s.Targets[^1]);
                    }

                    // (every wave with Byakuren's beam sound)
                    double every = beat * DanmakusuBalance.RIKAKO_WAVE_EVERY_BEATS;

                    for (int w = 1; s.Came + w * every <= now && s.Came + w * every < s.Gone; w++)
                        sound(f, now, s.Came + w * every, "touhou_strike", k * 4096 + w);
                }

                // (the Earth comes with the sound of Yuyuko's tree growing)
                for (int a = 0; a < attacks.Length; a++)
                {
                    if (attacks[a].Start <= now)
                        sound(f, now, attacks[a].Start, "yuyuko_tree", -1 - a);
                }
            }

            private void sound(BulletField f, double now, double at, string name, long key)
            {
                if (at > resetTime && now - at < 250 && sounded.Add(key))
                    f.playStageSound(name);
            }

            /// <summary>
            /// A satellite comes: the way from her to the player is the way it flies and faces; it starts out of the screen behind her (on the line from the player through her), and is gone when it has
            /// left the screen on the other side.
            /// </summary>
            private void come(BulletField f, Satellite s)
            {
                Vector2 boss = f.bossPositionOf(Period);
                Vector2 toPlayer = f.characterPosition - boss;
                s.Way = toPlayer.LengthSquared > 1f ? Vector2.Normalize(toPlayer) : Vector2.UnitY;

                float pad = satelliteSize(f) * 0.6f;
                clip(f, boss, s.Way, pad, out float behind, out float ahead);
                s.From = boss + s.Way * behind; // (behind is below 0: back along the way)
                s.Gone = s.Came + (ahead - behind) / Math.Max(speed(f), 1e-4f);
                s.Targets.Clear();
                s.Targets.Add(MathF.Atan2(s.Way.Y, s.Way.X));
                s.Known = true;
            }

            /// <summary>Where the line through <paramref name="p"/> along <paramref name="d"/> is in the screen (and <paramref name="pad"/> round it): from <paramref name="low"/> to <paramref name="high"/> along it.</summary>
            private static bool clip(BulletField f, Vector2 p, Vector2 d, float pad, out float low, out float high)
            {
                Vector2 min = f.stageMin - new Vector2(pad), max = f.stageMax + new Vector2(pad);
                low = float.NegativeInfinity;
                high = float.PositiveInfinity;

                for (int axis = 0; axis < 2; axis++)
                {
                    float o = axis == 0 ? p.X : p.Y, w = axis == 0 ? d.X : d.Y, a = axis == 0 ? min.X : min.Y, b = axis == 0 ? max.X : max.Y;

                    if (MathF.Abs(w) < 1e-6f)
                    {
                        if (o < a || o > b)
                        {
                            low = high = 0f;
                            return false;
                        }

                        continue;
                    }

                    float t1 = (a - o) / w, t2 = (b - o) / w;
                    low = Math.Max(low, Math.Min(t1, t2));
                    high = Math.Min(high, Math.Max(t1, t2));
                }

                if (high <= low)
                {
                    low = high = 0f;
                    return false;
                }

                return true;
            }

            private Vector2 positionOf(BulletField f, Satellite s, double t) => s.From + s.Way * (speed(f) * (float)(t - s.Came));

            /// <summary>
            /// Which way satellite <paramref name="s"/> faces at <paramref name="t"/> (radians): from each turn on it turns towards the way it was given then, evenly, at RIKAKO_TURN_DEGREES_PER_BEAT, the
            /// nearer way round, until it faces it or the next turn comes (2026-11-26, "spin speed halved and constant": it eased over the four beats from one way to the next before). After the last way
            /// it has been given it goes on turning towards it.
            /// </summary>
            private float facingOf(Satellite s, double t)
            {
                if (s.Targets.Count == 0)
                    return MathF.Atan2(s.Way.Y, s.Way.X);

                float facing = s.Targets[0];
                double rate = DanmakusuBalance.RIKAKO_TURN_DEGREES_PER_BEAT * Math.PI / 180.0 / beat;

                for (int n = 1; n < s.Targets.Count; n++)
                {
                    double from = s.Came + n * turnMs;

                    if (t <= from)
                        break;

                    double until = n == s.Targets.Count - 1 ? t : Math.Min(t, from + turnMs);
                    float turn = s.Targets[n] - facing;

                    while (turn > MathF.PI)
                        turn -= MathF.Tau;

                    while (turn < -MathF.PI)
                        turn += MathF.Tau;

                    float most = (float)(rate * (until - from));
                    facing += Math.Clamp(turn, -most, most);
                }

                return facing;
            }

            /// <summary>Which way round satellite <paramref name="s"/> turns in stretch <paramref name="n"/> (from its n-th turn): 1, -1, or 0 when it faces the way already.</summary>
            private int turnWay(Satellite s, int n)
            {
                if (n < 1 || n >= s.Targets.Count)
                    return 0;

                float turn = s.Targets[n] - facingOf(s, s.Came + n * turnMs);

                while (turn > MathF.PI)
                    turn -= MathF.Tau;

                while (turn < -MathF.PI)
                    turn += MathF.Tau;

                return MathF.Abs(turn) < 0.01f ? 0 : Math.Sign(turn);
            }

            /// <summary>
            /// How bright the flash of satellite <paramref name="s"/>'s grid is at <paramref name="t"/> (0..1): as it starts turning the other way round from the way it turned before, over RIKAKO_TURN_FLASH_BEATS
            /// (2026-11-27, "warn about spin direction change with laserbeams flash").
            /// </summary>
            private float turnFlash(Satellite s, double t)
            {
                int n = (int)Math.Floor((t - s.Came) / turnMs);
                double since = t - (s.Came + n * turnMs), over = beat * DanmakusuBalance.RIKAKO_TURN_FLASH_BEATS;

                if (n < 2 || since >= over)
                    return 0f;

                int now = turnWay(s, n);
                int before = 0;

                for (int k = n - 1; k >= 1 && before == 0; k--)
                    before = turnWay(s, k);

                return now != 0 && before != 0 && now != before ? 1f - (float)(since / over) : 0f;
            }

            /// <summary>How wide the beam is (of its whole half width) this far from the satellite along it: from RIKAKO_BEAM_ROOT at it, widening ever more slowly, to all of it RIKAKO_BEAM_GROW_CIRCLES out.</summary>
            private float beamWidthAt(BulletField f, float along)
            {
                float x = Math.Clamp(MathF.Abs(along) / (f.circleRadius * DanmakusuBalance.RIKAKO_BEAM_GROW_CIRCLES), 0f, 1f);
                return DanmakusuBalance.RIKAKO_BEAM_ROOT + (1f - DanmakusuBalance.RIKAKO_BEAM_ROOT) * (1f - (1f - x) * (1f - x));
            }

            /// <summary>How much of satellite <paramref name="s"/> is seen at <paramref name="t"/>: it fades in over two beats as it comes, and out over the last beat before it is gone.</summary>
            private float presence(Satellite s, double t)
                => !s.Known || t < s.Came || t >= s.Gone ? 0f : smoothStep((t - s.Came) / (beat * 2)) * smoothStep((s.Gone - t) / beat);

            // ---- the beam ----

            /// <summary>The beam of satellite <paramref name="s"/> at <paramref name="t"/>: 0 none, 1 its preview, 2 solid; how far into that.</summary>
            private int beamAt(Satellite s, double t, out double into)
            {
                into = 0;

                if (!s.Known || t < s.Came || t >= s.Gone)
                    return 0;

                double every = beat * DanmakusuBalance.RIKAKO_BEAM_EVERY_BEATS;
                double preview = beat * DanmakusuBalance.RIKAKO_BEAM_PREVIEW_BEATS, on = beat * DanmakusuBalance.RIKAKO_BEAM_ON_BEATS;
                double from = s.Came + Math.Floor((t - s.Came) / every) * every;
                into = t - from;

                if (into < preview)
                    return 1;

                into -= preview;
                return into < on ? 2 : 0;
            }

            private bool firing(double t)
            {
                foreach (var s in satellites)
                {
                    if (beamAt(s, t, out _) == 2)
                        return true;
                }

                return false;
            }

            /// <summary>The Master Spark's sound plays while a satellite's beam is solid.</summary>
            public bool Firing(double now) => !double.IsNaN(now) && firing(now);

            /// <summary>The screen shakes while a satellite's beam is solid.</summary>
            public float ShakeAt(double now) => firing(now) ? DanmakusuBalance.RIKAKO_SHAKE : 0f;

            // ---- the waves and the grid ----

            /// <summary>How solid the grid of satellite <paramref name="s"/> is at <paramref name="p"/> at <paramref name="t"/> (0..1): under one of its waves, softly towards the wave's edges.</summary>
            private float waveOver(BulletField f, Satellite s, Vector2 p, double t)
            {
                double every = beat * DanmakusuBalance.RIKAKO_WAVE_EVERY_BEATS, runs = beat * DanmakusuBalance.RIKAKO_WAVE_BEATS;
                float thick = f.circleRadius * DanmakusuBalance.RIKAKO_WAVE_CIRCLES, soft = thick * 0.3f;
                float most = 0f;

                for (int w = Math.Max(1, (int)Math.Floor((t - s.Came - runs) / every)); s.Came + w * every <= t; w++)
                {
                    double sent = s.Came + w * every;

                    if (sent >= s.Gone || t >= sent + runs)
                        continue;

                    waveOf(f, s, sent, t, out Vector2 centre, out float outer);
                    float d = Vector2.Distance(p, centre);
                    most = Math.Max(most, smoothStep((d - (outer - thick)) / soft) * smoothStep((outer - d) / soft));
                }

                return most;
            }

            /// <summary>Where the wave sent at <paramref name="sent"/> started, and how far out its outer edge is at <paramref name="t"/>: out past the farthest corner of the screen over RIKAKO_WAVE_BEATS.</summary>
            private void waveOf(BulletField f, Satellite s, double sent, double t, out Vector2 centre, out float outer)
            {
                centre = positionOf(f, s, sent);
                float far = 0f;

                foreach (var corner in new[] { f.stageMin, f.stageMax, new Vector2(f.stageMin.X, f.stageMax.Y), new Vector2(f.stageMax.X, f.stageMin.Y) })
                    far = Math.Max(far, Vector2.Distance(corner, centre));

                float thick = f.circleRadius * DanmakusuBalance.RIKAKO_WAVE_CIRCLES;
                outer = (far + thick) * (float)Math.Clamp((t - sent) / (beat * DanmakusuBalance.RIKAKO_WAVE_BEATS), 0, 1);
            }

            // ---- drawing ----

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                Vector2 low = f.stageMin, high = f.stageMax;

                // (the Earth over the whole screen in her attacks, as big as it must be to cover it)
                foreach (var (start, end) in attacks)
                {
                    float shown = smoothStep((t - start) / beat) * smoothStep((end - t) / beat);

                    if (shown <= 0.005f)
                        continue;

                    Vector2 screen = high - low;
                    float cover = Math.Max(screen.X / 1920f, screen.Y / 1080f);
                    f.stageBack.Add(StageTexture.RikakoEarth, (low + high) / 2f, new Vector2(1920f, 1080f) * cover, 0f, DanmakusuBalance.RIKAKO_EARTH_ALPHA * shown, Color4.White);
                }

                // (the asteroids there now, in her section)
                float rocksShown = smoothStep((t - Period.Start) / beat) * smoothStep((Period.End - t) / beat);

                if (rocksShown > 0.005f)
                {
                    int r = firstRock(t);

                    for (; r < rocks.Length && rocks[r].Born <= t; r++)
                    {
                        var rock = rocks[r];

                        if (t >= rock.Gone)
                            continue;

                        Vector2 at = rock.At(t);
                        float size = rock.Size;

                        if (at.X < low.X - size || at.X > high.X + size || at.Y < low.Y - size || at.Y > high.Y + size)
                            continue;

                        Vector2 picture = StageLayer.ROCK_SIZES[rock.Picture] / 256f * size;
                        f.stageBack.Add(StageTexture.RikakoRock0 + rock.Picture, at, picture, rock.AngleAt(t) * 180f / MathF.PI, rocksShown, Color4.White);
                    }
                }

                // (the satellites and their waves)
                var grey = new Color4(0.72f, 0.74f, 0.78f, 1f);
                double every = beat * DanmakusuBalance.RIKAKO_WAVE_EVERY_BEATS, runs = beat * DanmakusuBalance.RIKAKO_WAVE_BEATS;
                float thick = f.circleRadius * DanmakusuBalance.RIKAKO_WAVE_CIRCLES;

                foreach (var s in satellites)
                {
                    float shown = presence(s, t);

                    if (shown <= 0.005f)
                        continue;

                    Vector2 at = positionOf(f, s, t);
                    float size = satelliteSize(f);
                    float degrees = facingOf(s, t) * 180f / MathF.PI - StageLayer.SATELLITE_FORWARD_DEGREES;
                    f.stageFront.Add(StageTexture.RikakoSatellite, at, new Vector2(size, size * StageLayer.SATELLITE_ASPECT), degrees, shown, Color4.White); // (over its beam: 2026-11-26)

                    for (int w = Math.Max(1, (int)Math.Floor((t - s.Came - runs) / every)); s.Came + w * every <= t; w++)
                    {
                        double sent = s.Came + w * every;

                        if (sent >= s.Gone || t >= sent + runs)
                            continue;

                        waveOf(f, s, sent, t, out Vector2 centre, out float outer);
                        float fading = 1f - smoothStep((t - sent - runs * 0.8) / (runs * 0.2));
                        f.stageRipple.AddRing(centre, outer, thick * 0.5f, DanmakusuBalance.RIKAKO_WAVE_ALPHA * fading, grey);
                    }
                }
            }

            /// <summary>The first asteroid that can be there at <paramref name="t"/> (born no longer ago than the longest any is there).</summary>
            private int firstRock(double t)
            {
                int lo = 0, hi = rocks.Length;
                double earliest = t - rocksLongest;

                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;

                    if (rocks[mid].Born < earliest)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                return lo;
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || t < Start)
                    return;

                foreach (var s in satellites)
                {
                    float shown = presence(s, t);

                    if (shown <= 0.005f)
                        continue;

                    Vector2 at = positionOf(f, s, t);
                    float facing = facingOf(s, t);
                    Vector2 u = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
                    Vector2 v = new Vector2(-u.Y, u.X);

                    // (the grid: see-through, solid under the waves - every line in short pieces, so that the waves' soft edges show along it)
                    float gridHalf = this.gridHalf(f);
                    float gap = f.circleRadius * DanmakusuBalance.RIKAKO_GRID_CIRCLES;
                    float lowU = float.PositiveInfinity, highU = float.NegativeInfinity, lowV = float.PositiveInfinity, highV = float.NegativeInfinity;

                    foreach (var corner in new[] { f.stageMin, f.stageMax, new Vector2(f.stageMin.X, f.stageMax.Y), new Vector2(f.stageMax.X, f.stageMin.Y) })
                    {
                        Vector2 local = corner - at;
                        float cu = Vector2.Dot(local, u), cv = Vector2.Dot(local, v);
                        lowU = Math.Min(lowU, cu);
                        highU = Math.Max(highU, cu);
                        lowV = Math.Min(lowV, cv);
                        highV = Math.Max(highV, cv);
                    }

                    var gridCore = new Color4(0.86f, 0.92f, 1f, 1f);
                    var gridBorder = DanmakusuBalance.BorderColour(new Color4(0.35f, 0.5f, 0.85f, 1f));
                    int piece = ++f.beamPieces;
                    float flash = turnFlash(s, t); // (it starts turning the other way round: a white light flashes round every line of the grid - 2026-11-28, "not laserbeams itselves, but white bloom around it"; the lines themselves flashed from 2026-11-27)

                    for (int across = 0; across < 2; across++)
                    {
                        // (lines along the way it faces, one gap apart across it; then the lines across it)
                        Vector2 along = across == 0 ? u : v, step = across == 0 ? v : u;
                        float from = across == 0 ? lowV : lowU, to = across == 0 ? highV : highU;

                        for (int n = (int)MathF.Floor(from / gap); n <= (int)MathF.Ceiling(to / gap); n++)
                        {
                            Vector2 through = at + step * (n * gap);

                            if (!clip(f, through, along, gridHalf + 2f, out float a, out float b))
                                continue;

                            int pieces = Math.Max(2, (int)MathF.Ceiling((b - a) / (gap * 0.25f)));
                            line.Clear();
                            fades.Clear();

                            for (int p = 0; p <= pieces; p++)
                            {
                                Vector2 point = through + along * (a + (b - a) * p / pieces);
                                float solid = waveOver(f, s, point, t);
                                line.Add(point);
                                fades.Add(shown * (DanmakusuBalance.RIKAKO_GRID_ALPHA + (1f - DanmakusuBalance.RIKAKO_GRID_ALPHA) * solid));
                            }

                            f.addPlainBeam(line, gridHalf, gridCore, gridBorder, ref beams, ref pointTotal, null, fades, null, flash * shown * DanmakusuBalance.RIKAKO_FLASH_BLOOM);
                            f.laserInstances[beams - 1].Piece = piece;
                            f.laserInstances[beams - 1].BloomUnfaded = true; // (as bright round a faint line as round a solid one)
                        }
                    }

                    // (the big beam, both ways along the way it faces, through it)
                    int beam = beamAt(s, t, out double into);

                    if (beam == 0)
                        continue;

                    float half = beamHalf(f);
                    float alpha = beam == 2
                        ? smoothStep(into / 40.0) * (1f - smoothStep((into - beat * (DanmakusuBalance.RIKAKO_BEAM_ON_BEATS - 0.25)) / (beat * 0.25)))
                        : DanmakusuBalance.BEAM_PREVIEW_ALPHA * smoothStep(into / (beat * 0.25));

                    if (alpha < 0.01f || !clip(f, at, u, half + 4f, out float back, out float ahead))
                        continue;

                    // (one seamless line from the far end behind it, through it, to the far end ahead: narrow where it leaves the satellite, widening as Marisa's spark does - 2026-11-26)
                    float width = beam == 2 ? half : half * 0.7f;
                    float grow = f.circleRadius * DanmakusuBalance.RIKAKO_BEAM_GROW_CIRCLES;
                    line.Clear();
                    fades.Clear();
                    beamWidths.Clear();

                    for (int side = -1; side <= 1; side += 2)
                    {
                        float end = side < 0 ? back : ahead;

                        if (side * end <= 0f)
                            continue; // (the satellite is past the edge on this side)

                        // (from the far end in towards the satellite on the side behind, out from it on the side ahead; the growing part in short steps)
                        stops.Clear();
                        stops.Add(end);

                        for (int k = 8; k >= 0; k--)
                        {
                            float d = grow * k / 8f;

                            if (d < MathF.Abs(end))
                                stops.Add(side * d);
                        }

                        if (side > 0)
                            stops.Reverse();

                        foreach (float d in stops)
                        {
                            if (side > 0 && line.Count > 0 && d == 0f)
                                continue; // (the satellite's own point is there already)

                            line.Add(at + u * d);
                            fades.Add(alpha);
                            beamWidths.Add(width * beamWidthAt(f, d));
                        }
                    }

                    if (line.Count < 2)
                        continue;

                    f.addPlainBeam(line, width, new Color4(1f, 0.97f, 0.9f, 1f), DanmakusuBalance.BorderColour(new Color4(0.55f, 0.35f, 0.85f, 1f)), ref beams, ref pointTotal, beamWidths, fades,
                        bloom: beam == 2 ? 0.5f : 0f);
                }
            }

            /// <summary>A solid beam, and the grid where a wave makes it solid, hurt like a slider body.</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return false;

                Vector2 player = f.characterPosition;
                float room = DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                float gap = f.circleRadius * DanmakusuBalance.RIKAKO_GRID_CIRCLES;
                float gridReach = gridHalf(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + room;

                foreach (var s in satellites)
                {
                    if (!s.Known || now < s.Came || now >= s.Gone)
                        continue;

                    Vector2 at = positionOf(f, s, now);
                    float facing = facingOf(s, now);
                    Vector2 u = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
                    Vector2 local = player - at;
                    float lu = Vector2.Dot(local, u), lv = local.X * -u.Y + local.Y * u.X;

                    // (the beam: the whole line through it, narrower near it)
                    if (beamAt(s, now, out double into) == 2 && into < beat * (DanmakusuBalance.RIKAKO_BEAM_ON_BEATS - 0.25)
                        && MathF.Abs(lv) <= beamHalf(f) * beamWidthAt(f, lu) * DanmakusuBalance.BULLET_HITBOX_SCALE + room)
                        return true;

                    // (the grid, where a wave is over the player)
                    float nearest = Math.Min(MathF.Abs(lu - gap * MathF.Round(lu / gap)), MathF.Abs(lv - gap * MathF.Round(lv / gap)));

                    if (nearest <= gridReach && waveOver(f, s, player, now) >= 0.5f)
                        return true;
                }

                return false;
            }
        }
    }
}
