using osu.Game.Rulesets.Danmakusu.Bosses;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    public enum BossKind
    {
        /// <summary>A long break: mirrors the player and fires the whole spinner pattern.</summary>
        Break,

        /// <summary>A kiai section: mirrors the player and fires one laser beam at the player and stars at half the frequency of the break boss.</summary>
        Kiai,

        /// <summary>A spinner (outside kiai): stands in the middle of the screen and does nothing, the spinner attacks anyway.</summary>
        Spinner,
    }

    /// <summary>
    /// A stretch of the map with a boss: another "player" who sits opposite the real one (mirrored through the centre of the screen).
    /// He appears when the warning sound starts (<see cref="Start"/>), the warning (approach circle, stars flying in) lasts until
    /// <see cref="ShootStart"/> - the moment the break / kiai section / spinner starts - and he disappears at <see cref="End"/>.
    /// </summary>
    public sealed class BossPeriod
    {
        public double Start { get; init; }

        /// <summary>The warning is over: the break / kiai section / spinner starts and the boss's first laser fires.</summary>
        public double ShootStart { get; init; }

        public double End { get; init; }

        public BossKind Kind { get; init; }

        /// <summary>Which of the map's bosses this section belongs to: 0 = A, 1 = B (they alternate section by section).</summary>
        public int BossSlot { get; init; }

        /// <summary>The boss of this section.</summary>
        public BossInfo Boss { get; init; } = null!;

        /// <summary>0 = the boss of the section; 1 = the second boss of the last kiai section of a map with two bosses (both are on the screen then, the second one has its own picture and position).</summary>
        public int Rank { get; init; }

        /// <summary>The last section of the map: the boss explodes when it ends.</summary>
        public bool IsLast { get; init; }

        /// <summary>Yorihime is repeating this boss: it is not on the screen or in the HUD itself, and everything it does comes from where she stands (always <see cref="Rank"/> 1; see the end of the section planning).</summary>
        public bool Borrowed { get; init; }

        /// <summary>The boss fires laser beams (so the big star hides their start and the bullets are dimmed while they fire); not all bosses do.</summary>
        public bool HasLasers { get; init; } = true;

        /// <summary>When each laser burst of the boss starts (the first one is <see cref="ShootStart"/>). Every one is announced by stars flying in.</summary>
        public double[] LaserStarts { get; init; } = [];

        /// <summary>When each laser burst of the boss ends (same order as <see cref="LaserStarts"/>). The boss is only visible between the bursts.</summary>
        public double[] LaserEnds { get; init; } = [];

        /// <summary>Byakuren: the places she remembered (every fifth circle or slider, 5 at a time) and when each throws its five homing beams (filled by the planner, see addRememberedBeams).</summary>
        public System.Collections.Generic.List<(double Time, osuTK.Vector2 Spot)> SpotThrows { get; } = new System.Collections.Generic.List<(double Time, osuTK.Vector2 Spot)>();

        /// <summary>How long (ms) the approach circle takes to shrink onto the boss; it ends when <see cref="ShootStart"/> is reached.</summary>
        public double ApproachDuration { get; init; }

        /// <summary>How long a hit circle's approach circle takes in this map (ms, from the approach rate; the boss's own approach circle takes longer).</summary>
        public double Preempt { get; init; }

        /// <summary>Length of a beat (ms) where the section starts (the moon's orbit takes a number of beats).</summary>
        public double BeatLength { get; init; } = 500;

        /// <summary>Radius (playfield pixels) of the boss's stars where this section starts (0 = not set).</summary>
        public float StarRadius { get; init; }

        /// <summary>Speed (pixels per ms) of an ordinary bullet where this section starts (0 = not set).</summary>
        public float BulletSpeed { get; init; }

        /// <summary>How fast a spinner's beams turn on this map (radians per ms): Utsuho's jets turn at half of it.</summary>
        public float AngularSpeed { get; init; }

        /// <summary>Nemuno: the slider beams of her section that she takes (every tenth): each stops when its slider ends, turns to the player and falls as five (filled by the planner).</summary>
        public System.Collections.Generic.List<LaserEmitter> TakenBeams { get; } = new System.Collections.Generic.List<LaserEmitter>();

        /// <summary>When the last of the boss's mechanics of this section is over, if that is after <see cref="End"/> (Miko's rays, Mima's moon): she stays until then, where she stood (set by the field from its acts).</summary>
        public double MechanicsEnd { get; set; }

        /// <summary>How long a whole attack and a whole pause of the boss are here (ms; before a short section squeezed them; the pause is 0 for a boss who never pauses).</summary>
        public double FullBurst { get; init; }

        public double FullPause { get; init; }

        /// <summary>
        /// When her last attack and the pause after it are over, if her section ended before that (0 = it did not): she goes through the whole of them, staying where she stood (see <see cref="MechanicsEnd"/>),
        /// and what she does in an attack or a pause goes on until then (set by the planner; a boss who never pauses does not, or she would never stop).
        /// </summary>
        public double SequenceEnd { get; set; }

        /// <summary>Ringo: the numbers of the threes of her attacks (every ring fired in them and the two copies right behind it, see <see cref="BulletEmitter.SkewerId"/>; filled by the planner): every fourth of their threes is joined by a beam.</summary>
        public System.Collections.Generic.List<int> Skewers { get; } = new System.Collections.Generic.List<int>();

        /// <summary>The section after the last one, where a boss that cannot die (Mokou) comes back until the map ends.</summary>
        public bool Revival { get; init; }

        /// <summary>The pull of an arrowhead on this map (what turns it towards the player): the arrowheads that Nazrin makes out of bullets use it.</summary>
        public float ArrowGravity { get; init; }
    }
}
