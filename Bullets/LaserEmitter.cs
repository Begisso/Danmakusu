using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>Something that stops the slices of a beam where they are and lets them fall later (Zanmu's ripple): a closed form of time, like everything that happens to a slice.</summary>
    public interface IBeamFreezer
    {
        /// <summary>Has the slice that was fired at <paramref name="fired"/> from <paramref name="start"/> with <paramref name="velocity"/> been stopped by <paramref name="time"/>? Then it is at <paramref name="position"/> (stopped at <paramref name="stoppedAt"/>).</summary>
        bool Stops(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out double stoppedAt, out BulletLook look);

        /// <summary>After this (game time) the stopped slices are gone (they have fallen out of the screen). <paramref name="look"/> in <c>Stops</c> is how a stopped slice looks.</summary>
        double DoneAt { get; }

        /// <summary>Can a slice fired at <paramref name="oldestFired"/> or later have been stopped by <paramref name="time"/>? (False: the beam is drawn as a plain one, from its kept points only.)</summary>
        bool CanHaveStopped(double oldestFired, double time);
    }

    /// <summary>
    /// Several freezers at once (a beam that lives through more than one of Zanmu's sections is stopped by the ripples of each of them): a slice is where the one that stopped it first has put it.
    /// </summary>
    public sealed class BeamFreezers : IBeamFreezer
    {
        private readonly IBeamFreezer[] all;

        public BeamFreezers(IBeamFreezer first, IBeamFreezer second)
        {
            var list = new List<IBeamFreezer>();

            foreach (var freezer in new[] { first, second })
            {
                if (freezer is BeamFreezers many)
                    list.AddRange(many.all);
                else
                    list.Add(freezer);
            }

            all = list.ToArray();
        }

        public double DoneAt
        {
            get
            {
                double done = double.MinValue;

                foreach (var freezer in all)
                    done = Math.Max(done, freezer.DoneAt);

                return done;
            }
        }

        public bool Stops(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out double stoppedAt, out BulletLook look)
        {
            position = default;
            stoppedAt = 0;
            look = null!;
            bool stopped = false;

            foreach (var freezer in all)
            {
                if (freezer.Stops(start, velocity, fired, time, out Vector2 stoppedPlace, out double at, out BulletLook how) && (!stopped || at < stoppedAt))
                {
                    position = stoppedPlace;
                    stoppedAt = at;
                    look = how;
                    stopped = true;
                }
            }

            return stopped;
        }

        public bool CanHaveStopped(double oldestFired, double time)
        {
            foreach (var freezer in all)
            {
                if (freezer.CanHaveStopped(oldestFired, time))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Something that carries the slices of a beam through Okina's portals: a slice that flies into the first portal comes out of the second.</summary>
    public interface IBeamPortals
    {
        /// <summary>Where the slice (fired at <paramref name="fired"/> from <paramref name="start"/> with <paramref name="velocity"/>) is at <paramref name="time"/> after the portals, and how many it has been through (<paramref name="through"/>).</summary>
        void Carry(LaserEmitter laser, Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out BulletLook? look, out int through);
    }

    /// <summary>Something that blows slices of a beam up (Flandre's eye): a slice that was inside the explosion when it went off is gone from then on.</summary>
    public interface IBeamBlasts
    {
        /// <summary>Has the slice (fired at <paramref name="fired"/> from <paramref name="start"/> with <paramref name="velocity"/>, flying straight) been blown up by <paramref name="time"/>?</summary>
        bool Blasted(Vector2 start, Vector2 velocity, double fired, double time);
    }

    /// <summary>
    /// The lasers of a slider with a whistle at its start (and of a spinner: one straight beam from a ball that does not move). The slider ball fires <see cref="BeamCount"/> beams, spread evenly around a circle
    /// (the first one aimed at the player). A beam is a continuous stream of bullet "slices" fired for as long as the slider lasts;
    /// every slice flies in the beam's direction at the same speed, so the beam is the path the ball took, dragged
    /// along that direction. When the ball goes round a bend the beam curves with it.
    /// A slice's position is a pure function of time, like a bullet's, which keeps gameplay and replays deterministic.
    ///
    /// The ball's path is stored simplified (a straight part is just its two ends), together with the moment the ball is at each kept point.
    /// Every slice moves at the same velocity, so a straight part of the path stays straight and nothing is lost.
    /// </summary>
    public sealed class LaserEmitter
    {
        public double StartTime { get; init; }
        public double EndTime { get; init; }

        /// <summary>Combo index of the slider's start (the beam has the colour of the slider).</summary>
        public int ComboIndex { get; init; }

        /// <summary>Slider start position, used to aim the beam at the player when the slider starts.</summary>
        public Vector2 Origin { get; set; }

        /// <summary>The boss fires this laser: the field keeps <see cref="Origin"/> at the boss's position while it fires (every slice keeps the origin it was fired from).</summary>
        public bool FollowsBoss { get; init; }

        /// <summary>The beam stays where it was shot from (the field does not move <see cref="Origin"/> with the boss any more once it has started), so that it stays straight.</summary>
        public bool KeepsOrigin { get; init; }

        /// <summary>A beam that <see cref="FollowsBoss"/> is fired from this far from the boss (Byakuren's five points round her).</summary>
        public Vector2 BossOffset { get; init; }

        /// <summary>
        /// A beam that <see cref="FollowsBoss"/> is fired from this far to the side of the boss, across the way it points (Benben's three parallel beams: -d, 0, +d); it is aimed and turned from the boss herself,
        /// so beams that differ only in this stay parallel.
        /// </summary>
        public float Across { get; init; }

        /// <summary>When the laser starts, beam 0 points this much (radians) away from the player (π = straight away from the player: Byakuren's beams start backwards).</summary>
        public float AimOffset { get; init; }

        /// <summary>When the laser starts, beam 0 points the way <see cref="BossOffset"/> does (straight out from the boss) instead of at the player; <see cref="AimOffset"/> is added to that.</summary>
        public bool AimOutward { get; init; }

        /// <summary>A turning beam that just spins at <see cref="AngularSpeed"/> instead of being steered towards the player (Byakuren's spinner).</summary>
        public bool Spins { get; init; }

        /// <summary>
        /// A turning beam that stays a straight line (Marisa's Master Spark): every slice lies the way the beam points now, at the distance it has flown, instead of flying on the way it was fired
        /// (which curls a turning beam into a spiral). Its widening part is drawn with many points, so its curve is smooth.
        /// </summary>
        public bool Rigid { get; init; }

        /// <summary>
        /// A beam that spins (<see cref="Spins"/>) also turns towards the player from this moment on (Marisa's Master Spark, once it has grown), <see cref="TurnSpeed"/> radians per ms, always the same way round:
        /// the way that was nearer when it began to turn (<see cref="TurnWay"/>). Infinity: never.
        /// </summary>
        public double TurnsFrom { get; init; } = double.PositiveInfinity;

        public float TurnSpeed { get; init; }

        /// <summary>No other boss's mechanic changes how thick the beam is (Marisa's Master Spark): not Raiko's ripples, Sannyo's smoke, Sukuna's distance, Gengetsu's or Remilia's thicker beams.</summary>
        public bool KeepsSize { get; init; }

        /// <summary>No mechanic of any boss touches the beam (Marisa's Master Spark): no op stops, turns, bends or mirrors it, no portal takes it, nothing cuts or blows up its slices, no moon swallows them, nothing hides it, it does not fade in smoke.</summary>
        public bool Immune { get; init; }

        /// <summary>Tsukasa: one of every fourth beam of her section - its slices bounce once off the border of the screen (set by the planner; the bounce is an op of hers, see <see cref="TrajectoryOp.OnlyMarked"/>).</summary>
        public bool BouncesOnce { get; set; }

        /// <summary>(The field's: the palette slot of the colour its slider has in its boss section, -1 none, -2 not worked out yet.)</summary>
        public int SectionStyle = -2;

        /// <summary>Which way round it turns to the player: +1, -1, or 0 before it has begun (set as the game runs, and again when the beam starts again after a seek).</summary>
        public int TurnWay { get; set; }

        /// <summary>The beam is drawn with its dark edge and its coloured layer half as thick (Marisa's).</summary>
        public bool ThinOutlines { get; init; }

        /// <summary>The field draws a big star where the beam starts while it fires (Byakuren's beams from the spots she remembered).</summary>
        public bool StarAtOrigin { get; init; }

        /// <summary>The beam does not shake the screen while it fires, as turning beams otherwise do (Byakuren's).</summary>
        public bool NoShake { get; init; }

        /// <summary>The beam makes no beam sound while it fires (Byakuren's).</summary>
        public bool Silent { get; init; }

        /// <summary>Stops the slices of the beam (Zanmu's ripple); null = nothing does.</summary>
        public IBeamFreezer? Freezer { get; set; }

        /// <summary>Okina's portals, if there are any in the field.</summary>
        public IBeamPortals? Portals { get; set; }

        /// <summary>Flandre's eyes, if there are any in the field: they blow slices of the beam up.</summary>
        public IBeamBlasts? Blasts { get; set; }

        /// <summary>More room in the buffers for the points that the ops, the portals and the explosions add where something changes along the beam.</summary>
        private int extraPoints => (Ops != null || Portals != null ? 16 : 0) + (Blasts != null ? 32 : 0) + (Freezer != null ? 24 : 0); // (room for the corners where a stopped part meets the rest)

        /// <summary>Which of the bosses on the screen the beam comes from (see <see cref="BossPeriod.Rank"/>).</summary>
        public int BossRank { get; init; }

        /// <summary>Drawn half width of the beam.</summary>
        public float HalfWidth { get; set; }

        /// <summary>Half width that actually hurts.</summary>
        public float HitHalfWidth { get; set; }

        /// <summary>
        /// The beam grows like a parabola on its way out (like a Master Spark): a slice has <see cref="StartHalfWidth"/> where it is fired (at the
        /// origin) and widens quickly at first and then ever more slowly, until it has <see cref="MaxHalfWidth"/> after
        /// <see cref="DanmakusuBalance.LASER_GROWTH_DISTANCE"/>.
        /// </summary>
        public bool Grows { get; init; }

        /// <summary>Half width of a growing beam at its origin (the radius of an osu! hit circle).</summary>
        public float StartHalfWidth { get; set; }

        /// <summary>The same slider's beam, fired again <paramref name="milliseconds"/> later from the same place (Hisami's).</summary>
        public LaserEmitter Later(double milliseconds)
        {
            var times = new double[BallTimes.Length];

            for (int i = 0; i < times.Length; i++)
                times[i] = BallTimes[i] + milliseconds;

            return new LaserEmitter
            {
                StartTime = StartTime + milliseconds,
                EndTime = EndTime + milliseconds,
                ComboIndex = ComboIndex,
                Origin = Origin,
                HalfWidth = HalfWidth,
                HitHalfWidth = HitHalfWidth,
                Speed = Speed,
                BeamCount = BeamCount,
                Directions = new Vector2[Directions.Length],
                BallPositions = BallPositions,
                BallTimes = times,
                FollowsBoss = FollowsBoss,
                BossRank = BossRank,
                PathFromOrigin = PathFromOrigin,
                Grows = Grows,
                StartHalfWidth = StartHalfWidth,
                GrowthPower = GrowthPower,
                PeakScale = PeakScale,
                GhostMs = GhostMs,
                CutAge = CutAge,
                CutStretch = CutStretch,
                ShrinkDelay = ShrinkDelay,
                Lifetime = Lifetime,
                Fan = Fan,
                FanCentre = FanCentre,
                Rotates = Rotates && HoldBeam, // (a long note's beams turn again; a spinner's are not fired again this way)
                AngularSpeed = HoldBeam ? AngularSpeed : 0f,
                HoldBeam = HoldBeam,
            };
        }

        /// <summary>
        /// The same slider's beam with its path moved by <paramref name="shift"/> (Benben's strings above and below it): aimed as it is (from the same <see cref="Origin"/>), so it stays parallel, and every slice
        /// see-through and harmless for at least <paramref name="ghostMs"/> after it is fired (as Junko's).
        /// </summary>
        public LaserEmitter Shifted(Vector2 shift, double ghostMs)
        {
            var positions = new Vector2[BallPositions.Length];

            for (int i = 0; i < positions.Length; i++)
                positions[i] = BallPositions[i] + shift;

            return new LaserEmitter
            {
                StartTime = StartTime,
                EndTime = EndTime,
                ComboIndex = ComboIndex,
                Origin = Origin,
                HalfWidth = HalfWidth,
                HitHalfWidth = HitHalfWidth,
                Speed = Speed,
                BeamCount = BeamCount,
                Directions = new Vector2[Directions.Length],
                BallPositions = positions,
                BallTimes = BallTimes,
                FollowsBoss = FollowsBoss,
                BossRank = BossRank,
                PathFromOrigin = PathFromOrigin,
                Grows = Grows,
                StartHalfWidth = StartHalfWidth,
                GrowthPower = GrowthPower,
                PeakScale = PeakScale,
                GhostMs = Math.Max(GhostMs, ghostMs),
                CutAge = CutAge,
                CutStretch = CutStretch,
                ShrinkDelay = ShrinkDelay,
                Lifetime = Lifetime,
                Fan = Fan,
                FanCentre = FanCentre,
                WallSide = WallSide,
                Rotates = Rotates,
                AngularSpeed = AngularSpeed,
                HoldBeam = HoldBeam,
            };
        }

        /// <summary>
        /// (a long note of mania, a drumroll of taiko) a slider's beams that turn as a spinner's do: every rule that tells a slider's beams from a spinner's or a boss's (which a boss takes away, which colour,
        /// which glow) counts them as a slider's. See <see cref="SpinnerLike"/>.
        /// </summary>
        public bool HoldBeam { get; init; }

        /// <summary>A spinner's or a boss's beams (they turn), not a slider's.</summary>
        public bool SpinnerLike => Rotates && !HoldBeam;

        /// <summary>
        /// (osu!catch) the beams are spread evenly over this angle (radians) round <see cref="FanCentre"/>, the first at one end and the last at the other, instead of round the whole circle from the
        /// player (0: round the whole circle). For a beam that does not turn.
        /// </summary>
        public float Fan { get; init; }

        public float FanCentre { get; init; }

        /// <summary>(osu!catch) the beams come out of a wall of the screen: 1 the bottom one, -1 the top one (the field moves the path there when the beams start). 0: where the path is.</summary>
        public int WallSide { get; init; }

        /// <summary>The widest the beam gets (slightly above <see cref="HalfWidth"/> when it <see cref="Grows"/>, but never less than where it starts).</summary>
        public float MaxHalfWidth => Grows ? Math.Max(HalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR * PeakScale, StartHalfWidth) : HalfWidth;

        /// <summary>How far (px from where it is fired) a growing beam widens to its widest (0 = the usual <see cref="DanmakusuBalance.LASER_GROWTH_DISTANCE"/>; Marisa's Master Spark: see <see cref="DanmakusuBalance.MARISA_SPARK_GROW"/>).</summary>
        public float GrowthDistance { get; set; }

        private float growthDistance => GrowthDistance > 0f ? GrowthDistance : DanmakusuBalance.LASER_GROWTH_DISTANCE;

        /// <summary>The widest a growing beam gets at the end of its growth (without the cone after it).</summary>
        private float grownHalfWidth => Math.Max(HalfWidth * DanmakusuBalance.LASER_PEAK_FACTOR * PeakScale, StartHalfWidth);

        /// <summary>The widest a growing beam gets is multiplied by this (the boss's beams are 20% wider than the spinner's).</summary>
        public float PeakScale { get; set; } = 1f;

        /// <summary>
        /// How fast a growing beam widens: its width follows 1 - (1 - u)^power, u being how far along the way to full width it is. With 2 it widens
        /// twice as fast as it is long at first (u = 0), more with a higher power - and the wider it gets, the slower, until it stops at its widest.
        /// </summary>
        public float GrowthPower { get; init; } = 2f;

        /// <summary>
        /// Junko's beams (a slider's beams fired from the boss): the path of the slider ball is moved so that it starts where the laser's <see cref="Origin"/> is
        /// (the field puts it at the boss's position when the laser starts), and turned so that the slider points the way the beam does (towards the player when the slider starts).
        /// </summary>
        public bool PathFromOrigin { get; init; }

        /// <summary>
        /// A slice stays at its widest until it is this old (ms), and then narrows again the way it widened (the other way round, down to nothing); negative = it never does.
        /// </summary>
        public double ShrinkDelay { get; init; } = -1;

        /// <summary>Letty's beams: a slice is gone, all at once and at its full width, when it is this old (ms; it turns into bullets then); negative = it narrows away as <see cref="ShrinkDelay"/> says. The beam is shortened from its oldest end at half the speed it grew: the slice fired at s is gone at s + CutAge + (s - StartTime).</summary>
        public double CutAge { get; init; } = -1;

        /// <summary>How much slower than it grew a beam with a <see cref="CutAge"/> is eaten away from its oldest end: the slice fired at s is gone at s + CutAge + (CutStretch - 1) x (s - StartTime) (1 = as fast, 2 = half as fast).</summary>
        public double CutStretch { get; init; } = 1;

        /// <summary>Junko's beams: every slice is see-through and does not hurt for this long (ms) after it was fired; then it is as it always is (it comes back over a moment).</summary>
        public double GhostMs { get; init; }

        /// <summary>Is a slice that was fired <paramref name="age"/> ms ago in its see-through time (it does not hurt then)?</summary>
        public bool IsGhostAge(double age) => GhostMs > 0 && age < GhostMs;

        /// <summary>How visible a slice that was fired <paramref name="age"/> ms ago is: 1 normally; a see-through slice is only <see cref="DanmakusuBalance.JUNKO_GHOST_ALPHA"/> as strong, and comes back to full over 250 ms when its time is over.</summary>
        public float GhostVisibility(double age)
        {
            if (GhostMs <= 0 || age >= GhostMs + 250)
                return 1f;

            if (age < GhostMs)
                return DanmakusuBalance.JUNKO_GHOST_ALPHA;

            float u = (float)((age - GhostMs) / 250.0);
            return DanmakusuBalance.JUNKO_GHOST_ALPHA + (1f - DanmakusuBalance.JUNKO_GHOST_ALPHA) * u * u * (3f - 2f * u);
        }

        /// <summary>The beams narrow again when they are old enough (see <see cref="ShrinkDelay"/>).</summary>
        public bool Shrinks => Grows && ShrinkDelay >= 0;

        /// <summary>How long (ms) a slice takes to widen to its widest (and, for a beam that <see cref="Shrinks"/>, to narrow to nothing).</summary>
        public double GrowthTime => DanmakusuBalance.LASER_GROWTH_DISTANCE / Math.Max(Speed, 1e-6f);

        /// <summary>Half width of a slice that was fired <paramref name="age"/> ms ago (wider as it flies on).</summary>
        public float HalfWidthAtAge(double age)
        {
            if (!Grows)
                return HalfWidth;

            // widens fast at first and ever more slowly: 1 - (1 - u)^power
            double flown = Speed * Math.Max(age, 0);
            double u = Math.Clamp(flown / growthDistance, 0, 1);
            double growth = 1 - Math.Pow(1 - u, GrowthPower);
            double width = StartHalfWidth + (grownHalfWidth - StartHalfWidth) * growth;

            if (Shrinks && CutAge < 0 && age > ShrinkDelay)
            {
                // the way back: narrows slowly at first and ever faster, to nothing (the widening played backwards)
                double x = Math.Clamp((age - ShrinkDelay) / GrowthTime, 0, 1);
                width *= 1 - Math.Pow(x, GrowthPower);
            }

            return (float)width;
        }

        /// <summary>Pixels per millisecond.</summary>
        public float Speed { get; init; }

        /// <summary>The (simplified) path of the slider ball, in order.</summary>
        public Vector2[] BallPositions { get; init; } = [];

        /// <summary>The time at which the ball is at the matching entry of <see cref="BallPositions"/> (ascending, first = start, last = end).</summary>
        public double[] BallTimes { get; init; } = [];

        /// <summary>Time after <see cref="EndTime"/> until every slice has left the playfield.</summary>
        public double Lifetime { get; set; } // (spinner / boss beams get it lengthened once the size of the real screen is known)

        /// <summary>The longest <see cref="Lifetime"/> may get, also lengthened: a beam is gone 64 beats after it started (the planner sets it, see <see cref="DanmakusuBalance.LIFE_MOST_BEATS"/>).</summary>
        public double MostLifetime { get; set; } = double.PositiveInfinity;

        /// <summary>Number of beams (OD 0..10 -> 1..3 for sliders, 1 for spinners).</summary>
        public int BeamCount { get; init; } = 1;

        /// <summary>
        /// Unit direction of every beam. The field sets them when the slider starts: the first one at the player,
        /// the others evenly around the circle.
        /// </summary>
        public Vector2[] Directions { get; init; } = [new Vector2(1, 0)];

        /// <summary>
        /// Beams that are completely gone (past the end of the slider, off screen and flying away) never matter again;
        /// the field marks them so that they cost nothing any more. Reset when time goes backwards.
        /// </summary>
        public bool[] BeamFinished => beamFinished ??= new bool[Math.Max(BeamCount, 16)]; // (made when first asked for: the beams are counted by then; Reisen's rings have 25)

        private bool[]? beamFinished;

        /// <summary>Game time of the last check whether the beams are finished.</summary>
        public double LastFinishCheck = double.NegativeInfinity;

        /// <summary>Every beam is finished.</summary>
        public bool Finished { get; private set; }

        /// <summary>
        /// The beams turn (a spinner's beams). The beam directions are then not fixed: the field steers them while the laser fires and logs
        /// the angle, so that every slice flies in the direction the beam pointed in when the slice was fired (the beams curve into spirals).
        /// </summary>
        public bool Rotates { get; init; }

        /// <summary>Radians per millisecond the beams turn at, when they turn.</summary>
        public float AngularSpeed { get; init; }

        /// <summary>Angle (radians) of beam 0 right now; the others follow at equal spacing. Only meaningful when <see cref="Rotates"/>.</summary>
        public float CurrentAngle { get; private set; }

        private readonly List<double> logTimes = new List<double>();
        private readonly List<float> logAngles = new List<float>();
        private readonly List<Vector2> logOrigins = new List<Vector2>();

        /// <summary>Rotating beams are drawn with a point whenever the angle has changed by this much (3 degrees).</summary>
        private const float angle_step = 3f * MathF.PI / 180f;

        private const int rotating_point_capacity = 1024;

        /// <summary>The laser starts: forget the angle log and begin at <paramref name="angle"/>.</summary>
        public void StartRotation(double time, float angle)
        {
            logTimes.Clear();
            logAngles.Clear();
            logOrigins.Clear();
            logTimes.Add(time);
            logAngles.Add(angle);
            logOrigins.Add(Origin);
            CurrentAngle = angle;
            TurnWay = 0;
        }

        /// <summary>Beam 0 points at <paramref name="angle"/> from <paramref name="time"/> on (times only go forward).</summary>
        public void SetAngle(double time, float angle)
        {
            CurrentAngle = angle;

            if (logTimes.Count > 0 && time <= logTimes[^1])
            {
                logAngles[^1] = angle;
                logOrigins[^1] = Origin;
                return;
            }

            logTimes.Add(time);
            logAngles.Add(angle);
            logOrigins.Add(Origin);
        }

        /// <summary>Time went backwards: forget the log after <paramref name="time"/>.</summary>
        public void TruncateRotation(double time)
        {
            while (logTimes.Count > 1 && logTimes[^1] > time)
            {
                logTimes.RemoveAt(logTimes.Count - 1);
                logAngles.RemoveAt(logAngles.Count - 1);
                logOrigins.RemoveAt(logOrigins.Count - 1);
            }

            if (logAngles.Count > 0)
            {
                CurrentAngle = logAngles[^1];

                if (FollowsBoss)
                    Origin = logOrigins[^1];
            }
        }

        /// <summary>The angle beam 0 had at <paramref name="time"/> (linear between log entries, the first/last one outside the log).</summary>
        public float AngleAt(double time)
        {
            int count = logTimes.Count;

            if (count == 0)
                return CurrentAngle;

            if (time <= logTimes[0])
                return logAngles[0];

            if (time >= logTimes[count - 1])
                return logAngles[count - 1];

            int lo = 0, hi = count - 1; // logTimes[lo] <= time < logTimes[hi]

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;

                if (logTimes[mid] <= time)
                    lo = mid;
                else
                    hi = mid;
            }

            double span = logTimes[hi] - logTimes[lo];
            float fraction = span <= 0 ? 0f : (float)((time - logTimes[lo]) / span);
            return logAngles[lo] + (logAngles[hi] - logAngles[lo]) * fraction;
        }

        /// <summary>Which way (beam 0) the slice of a turning beam fired at <paramref name="fired"/> is flying at <paramref name="time"/>: the way it was fired, or for a <see cref="Rigid"/> beam the way the beam points then.</summary>
        public float SliceAngle(double fired, double time) => AngleAt(Rigid ? time : fired);

        /// <summary>Where the slice of a turning beam fired at <paramref name="fired"/> flies from, as seen at <paramref name="time"/> (see <see cref="SliceAngle"/>).</summary>
        public Vector2 SliceOrigin(double fired, double time) => OriginAt(Rigid ? time : fired);

        /// <summary>Where the beams were fired from at <paramref name="time"/> (linear between log entries; constant unless the laser follows the boss).</summary>
        public Vector2 OriginAt(double time)
        {
            int count = logTimes.Count;

            if (!FollowsBoss || count == 0)
                return Origin;

            if (time <= logTimes[0])
                return logOrigins[0];

            if (time >= logTimes[count - 1])
                return logOrigins[count - 1];

            int lo = 0, hi = count - 1;

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;

                if (logTimes[mid] <= time)
                    lo = mid;
                else
                    hi = mid;
            }

            double span = logTimes[hi] - logTimes[lo];
            float fraction = span <= 0 ? 0f : (float)((time - logTimes[lo]) / span);
            return Vector2.Lerp(logOrigins[lo], logOrigins[hi], fraction);
        }

        /// <summary>Size of the buffer <see cref="GetPoints"/> needs.</summary>
        public int PointCapacity => (Rotates || Shrinks || Freezer != null ? rotating_point_capacity : BallPositions.Length + 2) + extraPoints;

        /// <summary>How long this laser can matter at all, from its start.</summary>
        public double Span => EndTime - StartTime + Lifetime;

        public void MarkFinished(int beam)
        {
            BeamFinished[beam] = true;

            for (int i = 0; i < BeamCount; i++)
            {
                if (!BeamFinished[i])
                    return;
            }

            Finished = true;
        }

        public void ResetFinished()
        {
            Array.Clear(BeamFinished);
            Finished = false;
            LastFinishCheck = double.NegativeInfinity;
        }

        /// <summary>
        /// The things that happen to the slices of this laser while they fly (frozen, bounced off the screen, falling like petals...): null = nothing.
        /// Only the ops that begin while the laser has slices, and that touch slices fired at their time, are kept (see <see cref="ConfigureOps"/>).
        /// </summary>
        public TrajectoryOp[]? Ops { get; private set; }

        /// <summary>The screen the ops work with.</summary>
        public RicochetArea? OpsArea { get; private set; }

        /// <summary>The slices stay this far inside the screen when they bounce.</summary>
        private float bounceMargin => Math.Max(HalfWidth, 1f) * 0.5f;

        /// <summary>Gives the laser the ops that begin while it has slices (and that touch slices fired at its time); the slices live longer for them.</summary>
        public void ConfigureOps(TrajectoryOp[] ops, RicochetArea area)
        {
            if (Immune)
                return;

            var list = new List<TrajectoryOp>();
            double frozen = 0;

            foreach (var op in ops)
            {
                if (Rotates && op.SkipRotating)
                    continue;

                if (op.OnlyMarked && !BouncesOnce)
                    continue;

                if ((op.Time <= StartTime && !op.Window) || StartTime >= op.BornBefore || op.Time >= EndTime + Lifetime || (op.Window && StartTime > op.End))
                    continue;

                list.Add(op);

                if (op.Kind == TrajectoryOpKind.Freeze)
                    frozen += op.End - op.Time;
            }

            if (list.Count == 0)
                return;

            double reach = 0;
            bool windowed = false;

            foreach (var op in list)
            {
                if (op.Window)
                    windowed = true;
                else
                    reach = Math.Max(reach, op.End - StartTime);
            }

            Ops = list.ToArray();
            OpsArea = area;
            fallsInPieces = list.Exists(op => op.Kind == TrajectoryOpKind.Fall);
            Lifetime = Math.Max(Lifetime, reach) + Lifetime * 2 + frozen + 2000 + (windowed ? 6000 : 0); // (a bounced slice flies on, back across the screen)
        }

        private double[] pointFired = new double[64];
        private BulletLook?[] scratchLooks = new BulletLook?[64];

        private void limits(out Vector2 min, out Vector2 max, out float top)
        {
            min = OpsArea!.Min + new Vector2(bounceMargin);
            max = OpsArea.Max - new Vector2(bounceMargin);
            top = OpsArea.Min.Y;
        }

        /// <summary>Where a slice is at <paramref name="time"/> and how it looks after what the ops have done to it (without the portals).</summary>
        public void TraceSlice(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out BulletLook? look)
        {
            look = null;

            if (Ops != null && OpsArea != null)
            {
                limits(out Vector2 min, out Vector2 max, out float top);
                Trajectory.Trace(start, velocity, Vector2.Zero, fired, time, Ops, min, max, top, out TrajectoryState state);
                position = state.Position;
                look = state.Look;

                // (an op that has taken the slice away - Reisen's freeze cuts off what is outside the playfield: gone, as a blasted one)
                if (!state.Alive)
                    look = BulletLook.Blasted(look);
            }
            else
                position = start + velocity * (float)(time - fired);
        }

        /// <summary>
        /// Where a slice (fired at <paramref name="fired"/> from <paramref name="start"/> with <paramref name="velocity"/>) is at <paramref name="time"/> and how it looks: after what has happened to it (see <see cref="TrajectoryOp"/>),
        /// and after the ripple that has stopped it (then <paramref name="stopped"/> is true and <paramref name="shown"/> is the age it is drawn at: a slice that has been stopped does not grow or travel any more).
        /// </summary>
        private void evaluate(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out BulletLook? look, out double shown, out bool stopped)
        {
            look = null;

            // (a slice of Junko's that is still see-through is not fully formed: nothing the bosses do touches it yet - it flies straight, and what they do starts from where it is when it has formed)
            if (GhostMs > 0)
            {
                double formed = fired + GhostMs;

                if (time <= formed)
                {
                    position = start + velocity * (float)(time - fired);
                    shown = time;
                    stopped = false;
                    return;
                }

                Vector2 formedAt = start + velocity * (float)GhostMs;
                evaluate(formedAt, velocity, formed, time, out position, out look, out shown, out stopped, fired);
                return;
            }

            evaluate(start, velocity, fired, time, out position, out look, out shown, out stopped, fired);
        }

        /// <summary>(<paramref name="bornAt"/>: when the slice was really fired, for its age; <paramref name="fired"/> is when the bosses' doings start to count for it)</summary>
        private void evaluate(Vector2 start, Vector2 velocity, double fired, double time, out Vector2 position, out BulletLook? look, out double shown, out bool stopped, double bornAt)
        {
            look = null;

            if (Portals != null)
            {
                Portals.Carry(this, start, velocity, fired, time, out position, out look, out int through);

                // (what has fallen stays what has fallen, also if it went through a portal before: its neighbours are cut from it by that)
                if (!BossOps.IsFallenBeam(look))
                    look = BulletLook.Portaled(look, through);
            }
            else
                TraceSlice(start, velocity, fired, time, out position, out look);

            if (fallsInPieces && ReferenceEquals(look, BossOps.PurpleBeam))
                look = fallenPieceLook(bornAt);

            shown = time;
            stopped = false;

            if (Freezer != null && Freezer.Stops(start, velocity, fired, time, out Vector2 frozenAt, out double stoppedAt, out BulletLook frozenLook))
            {
                position = frozenAt;
                look = frozenLook;
                shown = Math.Max(stoppedAt, fired);
                stopped = true;
            }

            // (a slice that an explosion has taken stays gone, wherever else it has been; so does one that had left the screen when something would have brought it back)
            if ((Blasts != null && Blasts.Blasted(start, velocity, fired, time)) || leftTheScreen(start, velocity, fired, time))
                look = BulletLook.Blasted(look);
        }

        /// <summary>
        /// Had the slice left the screen altogether by the time the first thing was done to it that could bring it back in (turned round, sent at a place, mirrored)? Until then it flew straight, so this costs next to nothing, and
        /// as a slice's place it is a function of time alone. Portals are not looked at: what goes through them comes out somewhere else, and that is what they are for. Nor is a beam that is one straight line.
        /// </summary>
        private bool leftTheScreen(Vector2 start, Vector2 velocity, double fired, double time)
        {
            if (Ops == null || OpsArea == null || Portals != null || Rigid)
                return false;

            Vector2 min = OpsArea.Min - new Vector2(HalfWidth * 2f), max = OpsArea.Max + new Vector2(HalfWidth * 2f);

            bool outside(Vector2 place) => place.X < min.X || place.X > max.X || place.Y < min.Y || place.Y > max.Y;

            if (outside(start))
                return false; // (it has not been on the screen yet)

            double first = double.PositiveInfinity;

            foreach (var op in Ops)
            {
                if (op.Kind is TrajectoryOpKind.Redirect or TrajectoryOpKind.Reverse or TrajectoryOpKind.Mirror or TrajectoryOpKind.Outward or TrajectoryOpKind.Crescent && op.Time > fired && op.Time <= time)
                    first = Math.Min(first, op.Time);
            }

            return !double.IsInfinity(first) && outside(start + velocity * (float)(first - fired));
        }

        // ---- what falls from the top (Kaguya's) falls as separate short pieces ----

        /// <summary>One of the ops makes slices fall from the top: what has fallen is cut into pieces (see <see cref="fallenPieceLook"/>).</summary>
        private bool fallsInPieces;

        /// <summary>How long (fire time, ms) one piece of a fallen beam and the gap after it are together.</summary>
        private double pieceCycleMs => Math.Min(DanmakusuBalance.KAGUYA_BEAM_PIECE_PX / Math.Max(Speed, 1e-3f), DanmakusuBalance.KAGUYA_BEAM_PIECE_MAX_MS); // (a slow beam: slices fired far apart reach the top at very different moments)

        private static readonly BulletLook fallen_gap = BulletLook.Blasted(BossOps.PurpleBeam);

        /// <summary>
        /// The look of a fallen slice fired at <paramref name="fired"/>: the slices of one piece share a look (every second piece the other purple, so two pieces are never joined), and the slices of the gap after a piece are gone
        /// (not drawn, and they do not hurt). A fallen beam is a curtain of slices that left the top at different moments; joined as one beam it would be a line from the top down across the screen.
        /// </summary>
        private BulletLook fallenPieceLook(double fired)
        {
            double cycle = fired / pieceCycleMs;
            double piece = Math.Floor(cycle);

            if (cycle - piece >= DanmakusuBalance.KAGUYA_BEAM_PIECE_SHARE)
                return fallen_gap;

            return (long)piece % 2 == 0 ? BossOps.PurpleBeam : BossOps.PurpleBeamAlt;
        }

        private static bool fallenOrGap(BulletLook? look) => BossOps.IsFallenBeam(look) || ReferenceEquals(look, fallen_gap);

        /// <summary>
        /// Between two kept points of which one (or both) has fallen, a point at the start and at the end of every piece and at the start of every gap: so that every piece is drawn on its own, from where it starts to where it ends
        /// (a straight beam has only its two ends as points, and the line between them would cross every piece and gap).
        /// </summary>
        private int insertFallenPieces(double time, int beam, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, int count)
        {
            if (!fallsInPieces)
                return count;

            double cycleMs = pieceCycleMs;
            double drawn = cycleMs * DanmakusuBalance.KAGUYA_BEAM_PIECE_SHARE;

            for (int k = count - 2; k >= 0; k--)
            {
                if (!fallenOrGap(looks[k]) && !fallenOrGap(looks[k + 1]))
                    continue;

                double f0 = pointFired[k], f1 = pointFired[k + 1];

                // (from the last cycle down to the first, each point put in at k + 1: they end up in order)
                for (double cycle = Math.Floor(f1 / cycleMs) * cycleMs; cycle + cycleMs > f0; cycle -= cycleMs)
                {
                    foreach (double at in new[] { cycle + drawn, cycle + drawn - 0.01, cycle })
                    {
                        if (at <= f0 || at >= f1 || count + 1 >= buffer.Length || count + 1 >= looks.Length)
                            continue;

                        insertPointAt(k + 1, at, k, beam, time, buffer, widths, distances, looks, ref count);
                    }
                }
            }

            return count;
        }

        /// <summary>Puts one slice (fired at <paramref name="fired"/> from <paramref name="start"/> with <paramref name="velocity"/>) into the buffers.</summary>
        private void put(Vector2 start, Vector2 velocity, double fired, float width, double time, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, ref int count)
        {
            evaluate(start, velocity, fired, time, out Vector2 position, out BulletLook? look, out double shown, out bool stopped);

            if (stopped)
                width = HalfWidthAtAge(shown - fired);

            if (widths != null)
                widths[count] = width;

            if (distances != null)
                distances[count] = Speed * (float)(shown - fired);

            if (count >= pointFired.Length)
                Array.Resize(ref pointFired, Math.Max(count + 1, pointFired.Length * 2));

            pointFired[count] = fired;
            looks[count] = look;
            buffer[count++] = position;
        }

        private BulletLook?[] looksFor(BulletLook?[]? given, int needed)
        {
            if (given != null)
                return given;

            if (scratchLooks.Length < needed)
                scratchLooks = new BulletLook?[needed];

            return scratchLooks;
        }

        /// <summary>
        /// Where a part of the beam that something has happened to (it has bounced, it has started to fall) meets the part that it has not happened to yet, the beam has a corner.
        /// Adds a point there: between two neighbouring points with different looks, the slice that is just at the change is searched for, and put in between.
        /// </summary>
        private int insertKinks(double time, int beam, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, int count)
        {
            if ((Ops == null || OpsArea == null) && Freezer == null && Portals == null && Blasts == null)
                return count;

            for (int k = count - 2; k >= 0; k--)
            {
                if (ReferenceEquals(looks[k], looks[k + 1]) || count >= buffer.Length || count >= looks.Length)
                    continue;

                // (two steps of Zanmu's grey are no corner: both were stopped, and the segment between them is drawn in the lower one; searching for the change cost a lot on every pair of points)
                if (looks[k] is { Level: >= 0 } && looks[k + 1] is { Level: >= 0 })
                    continue;

                // (nor do two neighbours that have both fallen need it: insertFallenPieces has already put points at the ends of the pieces and gaps, and a fallen beam has dozens of those)
                if (fallenOrGap(looks[k]) && fallenOrGap(looks[k + 1]))
                    continue;

                double lo = pointFired[k], hi = pointFired[k + 1];
                var loLook = looks[k];

                for (int iteration = 0; iteration < 16; iteration++)
                {
                    double mid = (lo + hi) / 2;
                    stateAt(mid, k, beam, out Vector2 s, out Vector2 v);
                    evaluate(s, v, mid, time, out _, out BulletLook? probeLook, out _, out _);

                    if (ReferenceEquals(probeLook, loLook))
                        lo = mid;
                    else
                        hi = mid;
                }

                // A place where the beam is cut (the two sides have been through a different number of portals, or one of them has been blown up) needs a point on each side: the part up to the cut
                // is drawn all the way to it, and the part after the cut starts at it. (A single corner point would belong to one side only, and the other side would lose its stretch up to the cut.)
                // (The same where one side has fallen from the top and the other has not: the two can be far apart - whether a slice falls depends on whether it reached the top before the fall's window ended - and a corner
                // with the look of the fallen side but the place of the other joined the fallen slices to one still flying up: a line from the top down across the screen.)
                bool cut = (looks[k]?.Portal ?? 0) != (looks[k + 1]?.Portal ?? 0) || (looks[k]?.Gone ?? false) != (looks[k + 1]?.Gone ?? false) || fallenOrGap(looks[k]) || fallenOrGap(looks[k + 1]);

                if (cut && count + 1 < buffer.Length && count + 1 < looks.Length)
                {
                    insertPointAt(k + 1, hi, k, beam, time, buffer, widths, distances, looks, ref count);
                    insertPointAt(k + 1, lo, k, beam, time, buffer, widths, distances, looks, ref count);
                    continue;
                }

                double f = (lo + hi) / 2;
                stateAt(f, k, beam, out Vector2 start, out Vector2 velocity);
                evaluate(start, velocity, f, time, out Vector2 changeAt, out _, out double changeShown, out _);

                // make room at k + 1
                if (pointFired.Length <= count + 1)
                    Array.Resize(ref pointFired, count + 16);

                for (int i = count; i > k + 1; i--)
                {
                    buffer[i] = buffer[i - 1];
                    looks[i] = looks[i - 1];
                    pointFired[i] = pointFired[i - 1];

                    if (widths != null)
                        widths[i] = widths[i - 1];

                    if (distances != null)
                        distances[i] = distances[i - 1];
                }

                double span = pointFired[k + 2] - pointFired[k];
                float fraction = span > 1e-9 ? (float)((f - pointFired[k]) / span) : 0.5f;

                buffer[k + 1] = changeAt;
                looks[k + 1] = looks[k + 2] ?? looks[k]; // (the corner itself belongs to the part that has changed)

                if (widths != null)
                    widths[k + 1] = widths[k] + (widths[k + 2] - widths[k]) * fraction;

                if (distances != null)
                    distances[k + 1] = Speed * (float)(changeShown - f);

                pointFired[k + 1] = f;
                count++;
            }

            return count;
        }

        /// <summary>Puts the slice fired at <paramref name="f"/> (looked at as it is at <paramref name="time"/>) in at index <paramref name="at"/>, between its two neighbours; it keeps the look it really has.</summary>
        private void insertPointAt(int at, double f, int segment, int beam, double time, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, ref int count)
        {
            stateAt(f, segment, beam, out Vector2 start, out Vector2 velocity);
            evaluate(start, velocity, f, time, out Vector2 position, out BulletLook? look, out double shown, out _);

            if (pointFired.Length <= count + 1)
                Array.Resize(ref pointFired, count + 16);

            double span = pointFired[at] - pointFired[at - 1];
            float fraction = span > 1e-9 ? (float)Math.Clamp((f - pointFired[at - 1]) / span, 0.0, 1.0) : 0.5f;
            float width = widths != null ? widths[at - 1] + (widths[at] - widths[at - 1]) * fraction : 0f;

            for (int i = count; i > at; i--)
            {
                buffer[i] = buffer[i - 1];
                looks[i] = looks[i - 1];
                pointFired[i] = pointFired[i - 1];

                if (widths != null)
                    widths[i] = widths[i - 1];

                if (distances != null)
                    distances[i] = distances[i - 1];
            }

            buffer[at] = position;
            looks[at] = look;
            pointFired[at] = f;

            if (widths != null)
                widths[at] = width;

            if (distances != null)
                distances[at] = Speed * (float)(shown - f);

            count++;
        }

        /// <summary>Where and how the slice that was fired at <paramref name="fired"/> started: for a beam that does not turn, on the straight stretch of the path that begins at kept point <paramref name="segment"/>.</summary>
        private void stateAt(double fired, int segment, int beam, out Vector2 start, out Vector2 velocity)
        {
            if (Rotates)
            {
                float a = AngleAt(fired) + beam * MathF.Tau / BeamCount;
                start = OriginAt(fired);
                velocity = new Vector2(MathF.Cos(a), MathF.Sin(a)) * Speed;
                return;
            }

            start = ballAt(fired, beam);
            velocity = Directions[beam] * Speed;
        }

        private float? pathHeading;

        /// <summary>The way the slider points on the map: from where it starts to the farthest point of its path (radians; 0 for a slider that does not go anywhere).</summary>
        public float PathHeading
        {
            get
            {
                if (pathHeading is float known)
                    return known;

                float heading = 0f;
                float farthest = 1e-3f;

                for (int i = 1; i < BallPositions.Length; i++)
                {
                    Vector2 offset = BallPositions[i] - BallPositions[0];

                    if (offset.Length > farthest)
                    {
                        farthest = offset.Length;
                        heading = MathF.Atan2(offset.Y, offset.X);
                    }
                }

                pathHeading = heading;
                return heading;
            }
        }

        /// <summary>
        /// Where a point of the slider's path is for beam number <paramref name="beam"/>. Normally on the map; Junko's beams (<see cref="PathFromOrigin"/>) start at <see cref="Origin"/> and the
        /// path is turned so that the slider points the way the beam does (the beam's direction is set when the slider starts and does not change after that).
        /// </summary>
        private Vector2 place(Vector2 point, int beam)
        {
            if (!PathFromOrigin || BallPositions.Length == 0)
                return point;

            float heading = PathHeading;
            Vector2 d = Directions[beam];
            float hx = MathF.Cos(heading), hy = MathF.Sin(heading);
            float cos = d.X * hx + d.Y * hy, sin = d.Y * hx - d.X * hy; // (the turn that takes the slider's heading to the beam's direction)
            Vector2 offset = point - BallPositions[0];

            return Origin + new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
        }

        /// <summary>Where the slice of beam <paramref name="beam"/> that was fired at <paramref name="fired"/> is at <paramref name="now"/> (it flies straight on in the direction of the beam).</summary>
        public Vector2 SlicePosition(double fired, double now, int beam)
            => ballAt(fired, beam) + Directions[beam] * (Speed * (float)(now - fired));

        /// <summary>The way the slider's path goes where the slice fired at <paramref name="fired"/> comes from (unit vector; zero if the slider does not move there).</summary>
        public Vector2 PathDirection(double fired, int beam)
        {
            const double around = 20; // ms
            Vector2 d = ballAt(fired + around, beam) - ballAt(fired - around, beam);
            return d.LengthSquared > 1e-6f ? Vector2.Normalize(d) : Vector2.Zero;
        }

        /// <summary>Where the slider ball is at <paramref name="time"/> for beam <paramref name="beam"/> (between the kept points of its path it is on the straight line; before the start / after the end it is at the ends).</summary>
        private Vector2 ballAt(double time, int beam)
        {
            int last = BallPositions.Length - 1;

            if (time <= BallTimes[0])
                return place(BallPositions[0], beam);

            if (time >= BallTimes[last])
                return place(BallPositions[last], beam);

            int lo = 0, hi = last; // BallTimes[lo] <= time < BallTimes[hi]

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;

                if (BallTimes[mid] <= time)
                    lo = mid;
                else
                    hi = mid;
            }

            double span = BallTimes[hi] - BallTimes[lo];
            float fraction = span > 1e-9 ? (float)((time - BallTimes[lo]) / span) : 0f;
            return place(Vector2.Lerp(BallPositions[lo], BallPositions[hi], fraction), beam);
        }

        /// <summary>
        /// Writes the centre line of beam number <paramref name="beam"/> at <paramref name="time"/> into <paramref name="buffer"/>,
        /// oldest slice first and the slice at the ball last, and returns the number of points. <paramref name="widths"/> and <paramref name="distances"/> (if given) get the half width of the beam and how far the slice has flown at every point;
        /// <paramref name="looks"/> (if given) gets for every point the look of its slice after what has happened to it (see <see cref="TrajectoryOp"/>; null = nothing).
        /// </summary>
        /// <summary>From this moment on the beam is gone (Nemuno has taken it: her act draws what became of it).</summary>
        public double VanishAt { get; set; } = double.PositiveInfinity;

        public int GetPoints(double time, Vector2[] buffer, int beam, float[]? widths = null, float[]? distances = null, BulletLook?[]? looks = null)
        {
            BulletLook?[] flags = looksFor(looks, buffer.Length);
            int count = pointsOf(time, buffer, beam, widths, distances, flags);

            count = refineBends(time, beam, buffer, widths, distances, flags, count);
            // (only what the bosses can move can have a point that is not a number)
            if (!Rigid && ((Ops != null && OpsArea != null) || Freezer != null || Portals != null))
                cutNotNumbers(buffer, flags, count);
            return count;
        }

        /// <summary>(Tools/SectionCheck: leaves the bends unrefined, to see what refining does)</summary>
        public static bool KeepBends;

        /// <summary>
        /// A beam is only its points: between two of them it is drawn straight. Where the bosses steer its slices (Eiki's circle turns what enters it to its middle, Shion turns things back, a moon bounces them...), the slices between
        /// two points do not go where the straight line between them is: a beam of two points (a straight slider's) whose one end has turned and the other not was a single line across the screen. So every pair of neighbouring points is
        /// looked at: where the slice halfway between them really is; if it is off the line between them by more than <see cref="DanmakusuBalance.BEAM_BEND_PX"/>, or the pair is stretched (further apart than it would be if nothing had
        /// happened), that slice is put in, and both halves are looked at again, down to <see cref="DanmakusuBalance.BEAM_BEND_DEPTH"/> halvings. The beam bends where its slices go; cutStretches then has every pair to compare.
        /// </summary>
        private int refineBends(double time, int beam, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, int count)
        {
            if (count < 2 || Rigid || KeepBends || ((Ops == null || OpsArea == null) && Freezer == null && Portals == null))
                return count;

            // (only while something can have steered its slices: an op that has begun by now and does not end before the beam began; portals and ripples always)
            if (Freezer == null && Portals == null)
            {
                bool steered = false;

                foreach (var op in Ops!)
                {
                    if (op.Time <= time && Math.Max(op.End, op.Time) >= StartTime - 1)
                    {
                        steered = true;
                        break;
                    }
                }

                if (!steered)
                    return count;
            }

            for (int k = count - 2; k >= 0; k--)
                refinePair(k, DanmakusuBalance.BEAM_BEND_DEPTH, time, beam, buffer, widths, distances, looks, ref count);

            return count;
        }

        private void refinePair(int k, int depth, double time, int beam, Vector2[] buffer, float[]? widths, float[]? distances, BulletLook?[] looks, ref int count)
        {
            if (depth <= 0 || count + 1 >= buffer.Length || count + 1 >= looks.Length || (widths != null && count + 1 >= widths.Length) || (distances != null && count + 1 >= distances.Length))
                return;

            double lo = pointFired[k], hi = pointFired[k + 1];

            // (a place where the beam is cut has its points already: portals, what is gone, what has fallen)
            if (hi - lo < 1.0 || (looks[k]?.Portal ?? 0) != (looks[k + 1]?.Portal ?? 0) || (looks[k]?.Gone ?? false) || (looks[k + 1]?.Gone ?? false) || fallenOrGap(looks[k]) || fallenOrGap(looks[k + 1]))
                return;

            Vector2 a = buffer[k], b = buffer[k + 1];

            if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(b.X) || !float.IsFinite(b.Y))
                return;

            double mid = (lo + hi) / 2;
            stateAt(mid, k, beam, out Vector2 start, out Vector2 velocity);
            evaluate(start, velocity, mid, time, out Vector2 middle, out _, out _, out _);

            if (!float.IsFinite(middle.X) || !float.IsFinite(middle.Y))
                return;

            float natural = Vector2.Distance(flownStraight(lo, k, beam, time), flownStraight(hi, k, beam, time));
            bool stretched = Vector2.Distance(a, b) > natural * DanmakusuBalance.BEAM_BEND_STRETCH + DanmakusuBalance.BEAM_BEND_PX;

            if (!stretched && distanceToLine(middle, a, b) <= DanmakusuBalance.BEAM_BEND_PX)
                return;

            insertPointAt(k + 1, mid, k, beam, time, buffer, widths, distances, looks, ref count);

            // (the later half first: what is put in there does not move the earlier one)
            refinePair(k + 1, depth - 1, time, beam, buffer, widths, distances, looks, ref count);
            refinePair(k, depth - 1, time, beam, buffer, widths, distances, looks, ref count);
        }

        private static float distanceToLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;
            float u = lengthSquared > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
            return (a + ab * u - p).Length;
        }

        /// <summary>How far apart in a beam the portal numbers of two pieces are that are cut from each other: more than anything really goes through portals, so that a cut can never cancel out a real one.</summary>
        private const int nan_cut_step = 1 << 16;

        /// <summary>
        /// A point that is not a number (something went wrong with its slice) is cut off from its neighbours, by a portal number of its own, which is what drawing and hitting look at. (Every pair of slices stretched far
        /// more than the pairs round it was cut too until 2026-11-08: that fixed none of the real bugs and made new ones, and is gone. Whatever has fallen, and is gone, has cuts of its own.)
        /// </summary>
        private static void cutNotNumbers(Vector2[] buffer, BulletLook?[] looks, int count)
        {
            int cuts = 0;

            for (int k = 0; k + 1 < count; k++)
            {
                if (!float.IsFinite(Vector2.Distance(buffer[k], buffer[k + 1])))
                    cuts++;

                if (cuts > 0 && looks[k + 1]?.Gone != true && !fallenOrGap(looks[k + 1]))
                    looks[k + 1] = BulletLook.Portaled(looks[k + 1], (looks[k + 1]?.Portal ?? 0) + cuts * nan_cut_step);
            }
        }

        /// <summary>Where the slice fired at <paramref name="fired"/> would be at <paramref name="time"/> if nothing had happened to it.</summary>
        private Vector2 flownStraight(double fired, int segment, int beam, double time)
        {
            stateAt(fired, segment, beam, out Vector2 start, out Vector2 velocity);
            return start + velocity * (float)(time - fired);
        }

        private int pointsOf(double time, Vector2[] buffer, int beam, float[]? widths, float[]? distances, BulletLook?[]? looks)
        {
            if (time >= VanishAt)
                return 0;

            if (Rotates)
                return getRotatingPoints(time, buffer, beam, widths, distances, looks);

            if (time < StartTime || BallPositions.Length < 2)
                return 0;

            // (a beam that Zanmu's ripples go over is sampled slice by slice too: the front of a ripple can be across the middle of a straight beam and be at neither of its ends)
            if (Shrinks || (Freezer != null && Freezer.CanHaveStopped(StartTime, time)))
                return getShrinkingPoints(time, buffer, beam, widths, distances, looks);

            BulletLook?[] flags = looksFor(looks, buffer.Length);
            int last = BallPositions.Length - 1;
            double fired = Math.Min(time, EndTime); // nothing is fired after the slider ends
            Vector2 drift = Directions[beam] * Speed;

            // the kept points the ball has already passed
            int whole = 0;

            while (whole < last && BallTimes[whole + 1] <= fired)
                whole++;

            int count = 0;

            for (int i = 0; i <= whole; i++)
                put(place(BallPositions[i], beam), drift, BallTimes[i], HalfWidth, time, buffer, widths, distances, flags, ref count);

            // the slice being fired right now, between two kept points
            if (whole < last && fired - BallTimes[whole] > 1e-3)
            {
                float fraction = (float)((fired - BallTimes[whole]) / (BallTimes[whole + 1] - BallTimes[whole]));
                put(place(Vector2.Lerp(BallPositions[whole], BallPositions[whole + 1], fraction), beam), drift, fired, HalfWidth, time, buffer, widths, distances, flags, ref count);
            }

            count = insertFallenPieces(time, beam, buffer, widths, distances, flags, count);
            return insertKinks(time, beam, buffer, widths, distances, flags, count);
        }

        /// <summary>
        /// A beam whose slices widen and then narrow again (Junko's): only slices that have not narrowed to nothing yet are part of it. Points are taken where the slider ball has a
        /// kept point, and in between often enough that the width follows its curve.
        /// </summary>
        private int getShrinkingPoints(double time, Vector2[] buffer, int beam, float[]? widths, float[]? distances, BulletLook?[]? looks)
        {
            BulletLook?[] flags = looksFor(looks, buffer.Length);
            double fired = Math.Min(time, EndTime); // nothing is fired after the slider ends
            double oldest = CutAge >= 0 ? Math.Max(StartTime, (time - CutAge + (CutStretch - 1) * StartTime) / CutStretch) : Shrinks ? Math.Max(StartTime, time - (ShrinkDelay + GrowthTime)) : StartTime;

            if (oldest > fired)
                return 0;

            Vector2 drift = Directions[beam] * Speed;
            int room = buffer.Length - 1 - extraPoints;
            double step = Shrinks ? Math.Max(GrowthTime / 6, 1) : Math.Max(20.0 / Math.Max(Speed, 1e-6f), 1); // (a beam that is only stopped by a ripple: a point every twenty pixels, and at every kept point of the path)

            // (a very long beam gets fewer points rather than not fit: the step only ever doubles, and the points are at multiples of it, so they stay where they are from one frame to the next -
            // a step that changed a little every frame would move every point along the path and the beam would shimmer)
            double needed = (fired - oldest) / Math.Max(room - BallTimes.Length - 8, 8);

            if (needed > step)
                step *= Math.Pow(2, Math.Ceiling(Math.Log2(needed / step)));

            int next = 0; // the first kept point after the slice that is looked at

            while (next < BallTimes.Length && BallTimes[next] <= oldest)
                next++;

            int count = 0;
            double f = oldest;

            while (true)
            {
                put(ballAt(f, beam), drift, f, HalfWidthAtAge(time - f), time, buffer, widths, distances, flags, ref count);

                if (f >= fired || count >= room)
                    break;

                double following = Math.Min(fired, (Math.Floor(f / step + 1e-9) + 1) * step);

                if (next < BallTimes.Length && BallTimes[next] < following)
                    following = BallTimes[next];

                while (next < BallTimes.Length && BallTimes[next] <= following)
                    next++;

                f = following;
            }

            count = insertFallenPieces(time, beam, buffer, widths, distances, flags, count);
            return insertKinks(time, beam, buffer, widths, distances, flags, count);
        }

        /// <summary>
        /// A rotating beam: the slice fired at time f is at Origin + (direction at f) x Speed x (time - f). Points are taken at the start of the
        /// window and then whenever the direction has turned by <see cref="angle_step"/>, so a straight (non-turning) part is just two points.
        /// </summary>
        private int getRotatingPoints(double time, Vector2[] buffer, int beam, float[]? widths, float[]? distances, BulletLook?[]? looks)
        {
            if (time < StartTime || logTimes.Count == 0)
                return 0;

            double fired = Math.Min(time, EndTime);
            double oldest = Math.Max(StartTime, time - Lifetime);

            if (oldest > fired)
                return 0;

            BulletLook?[] flags = looksFor(looks, buffer.Length);
            float offset = beam * MathF.Tau / BeamCount;
            int count = 0;

            if (Rigid)
            {
                // a straight beam: every slice the way it points now, from where it is fired now; the far end, many points along the part that widens (at fixed distances, so they do not shimmer), the near end
                float a = AngleAt(time) + offset;
                Vector2 way = new Vector2(MathF.Cos(a), MathF.Sin(a)) * Speed;
                Vector2 from = OriginAt(time);
                double growing = growthDistance / Math.Max(Speed, 1e-6f);
                const int growth_points = 32;

                put(from, way, oldest, HalfWidthAtAge(time - oldest), time, buffer, widths, distances, flags, ref count);

                for (int k = growth_points; k >= 1; k--)
                {
                    double slice = time - growing * k / growth_points;

                    if (slice > oldest && slice < fired)
                        put(from, way, slice, HalfWidthAtAge(time - slice), time, buffer, widths, distances, flags, ref count);
                }

                if (fired - oldest > 1e-3)
                    put(from, way, fired, HalfWidthAtAge(time - fired), time, buffer, widths, distances, flags, ref count);

                count = insertFallenPieces(time, beam, buffer, widths, distances, flags, count);
                return insertKinks(time, beam, buffer, widths, distances, flags, count);
            }

            void add(double slice, float angle)
            {
                float a = angle + offset;
                put(OriginAt(slice), new Vector2(MathF.Cos(a), MathF.Sin(a)) * Speed, slice, HalfWidthAtAge(time - slice), time, buffer, widths, distances, flags, ref count);
            }

            float last = AngleAt(oldest);
            Vector2 lastOrigin = OriginAt(oldest);
            float lastWidth = HalfWidthAtAge(time - oldest);
            add(oldest, last);

            // first log entry after the oldest slice
            int lo = 0, hi = logTimes.Count;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (logTimes[mid] <= oldest)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int room = buffer.Length - 1 - extraPoints;

            for (int i = lo; i < logTimes.Count && logTimes[i] < fired && count < room; i++)
            {
                // (a laser that follows the boss also needs a point where the boss has moved on)
                float width = HalfWidthAtAge(time - logTimes[i]);

                // (a beam that grows also needs points where its width has changed enough)
                if (Math.Abs(logAngles[i] - last) < angle_step && (!FollowsBoss || Vector2.DistanceSquared(logOrigins[i], lastOrigin) < 9f)
                    && (!Grows || Math.Abs(width - lastWidth) < MaxHalfWidth * 0.05f))
                    continue;

                last = logAngles[i];
                lastOrigin = logOrigins[i];
                lastWidth = width;
                add(logTimes[i], last);
            }

            if (fired - oldest > 1e-3)
                add(fired, AngleAt(fired));

            count = insertFallenPieces(time, beam, buffer, widths, distances, flags, count);
            return insertKinks(time, beam, buffer, widths, distances, flags, count);
        }
    }
}
