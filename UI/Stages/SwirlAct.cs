using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Mugetsu: in her attacks every bullet on the screen goes round her, as fast round her as it flies, the even bullets of a ring one way and the odd ones the other (Bullets/Swirl.cs works out where they are).
        // Every bullet goes round a middle that does not move: where she stood when it was fired in the attack, or, for what was on the screen already, where she stood when the attack began. The act takes that place as
        // an attack begins and keeps where she is up to date while it is on (what is fired takes it then); when time goes back before an attack, its place is taken again.
        // ======================================================================================================================================================

        private sealed class SwirlAct : StageAct
        {
            private readonly Swirl swirl;
            private readonly int[] attacks; // (hers: the ones of the vortex that are in this section)

            public SwirlAct(BossPeriod period, Swirl swirl)
            {
                Period = period;
                this.swirl = swirl;
                Start = period.ShootStart;
                End = period.End;

                var list = new List<int>();

                for (int k = 0; k < swirl.Starts.Length; k++)
                {
                    if (swirl.Ranks[k] == period.Rank && swirl.Starts[k] >= period.ShootStart - 1 && swirl.Starts[k] < period.End)
                        list.Add(k);
                }

                attacks = list.ToArray();
            }

            public override void Reset(BulletField f, double time)
            {
                foreach (int k in attacks)
                {
                    if (swirl.Starts[k] > time)
                        swirl.Placed[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                foreach (int k in attacks)
                {
                    if (now < swirl.Starts[k])
                        continue;

                    // (as it begins: what is on the screen already goes round where she stands now)
                    if (!swirl.Placed[k])
                    {
                        swirl.Grabbed[k] = f.bossVisualOf(Period);
                        swirl.Placed[k] = true;
                    }

                    // (while it is on: what is fired goes round where she is then)
                    if (now < swirl.Ends[k])
                        swirl.Centres[k] = f.bossVisualOf(Period);
                }
            }
        }
    }
}
