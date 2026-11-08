using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>One boss of the roster (see <see cref="BossRoster"/>).</summary>
    public sealed class BossInfo
    {
        public BossInfo(int index, string id, string name, int template, BossMechanic mechanic, Color4[] colours, float[] weights)
        {
            Index = index;
            Id = id;
            Name = name;
            Template = template;
            Mechanic = mechanic;
            Colours = colours;
            Weights = weights;
        }

        /// <summary>Position in the roster.</summary>
        public int Index { get; }

        /// <summary>Lower case name that is also the file name of the boss's pictures.</summary>
        public string Id { get; }

        /// <summary>The name shown at the top of the screen.</summary>
        public string Name { get; }

        /// <summary>
        /// Which placeholder pattern the boss shoots (0 = the spiral of stars, as the first boss had it; 1.. = see <see cref="Bullets.BulletPlanner"/>).
        /// Every boss gets his real patterns later; until then this keeps them distinguishable.
        /// </summary>
        public int Template { get; }

        /// <summary>The boss's special mechanic (a stub so far).</summary>
        public BossMechanic Mechanic { get; }

        /// <summary>The mechanics that are still only a name (no code behind them yet): a boss with one of these shoots the placeholder attack only.</summary>
        private static readonly HashSet<BossMechanic> placeholder_mechanics = new HashSet<BossMechanic>
        {
            BossMechanic.None, BossMechanic.Density, BossMechanic.Distance, BossMechanic.Dolls,
            BossMechanic.Elixir,BossMechanic.Frog, BossMechanic.Illusions,
            BossMechanic.Onbashira, BossMechanic.Poison, BossMechanic.Psychokinesis, BossMechanic.Thunder,
        };

        /// <summary>True when the boss has a mechanic of his own (modified); false when he is still a placeholder (only the placeholder attack).</summary>
        public bool IsModified => !placeholder_mechanics.Contains(Mechanic);

        /// <summary>The colour signature: 1-4 colours (not the skin of the character).</summary>
        public Color4[] Colours { get; }

        /// <summary>How much of the character each colour of <see cref="Colours"/> is (they add up to 1).</summary>
        public float[] Weights { get; }
    }
}
