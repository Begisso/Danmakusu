using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Danmakusu
{
    /// <summary>
    /// osu!'s, and then (osu!catch, see <see cref="ConvertedModes"/>) every falling object is kept on the screen longer than its approach: its approach circle warns, over the approach time, that it comes in
    /// through the top wall, and then it falls to the bottom wall, landing when it is due. So it is there from the approach time before it comes in (<see cref="ConvertedModes.Info.WarnMs"/>), and it takes
    /// as long to fall as its speed says (for a screen up to a quarter taller than a wide one). Only how long it is kept and drawn changes: it is still hit when it is due.
    /// </summary>
    public class DanmakusuBeatmapProcessor : OsuBeatmapProcessor
    {
        public DanmakusuBeatmapProcessor(IBeatmap beatmap)
            : base(beatmap)
        {
        }

        public override void PostProcess()
        {
            base.PostProcess();

            foreach (var hitObject in Beatmap.HitObjects.OfType<OsuHitObject>())
            {
                if (ConvertedModes.Of(hitObject) is not { Mode: ConvertedModes.CATCH, FallSpeed: > 0f } info)
                    continue;

                info.WarnMs = hitObject.TimePreempt; // (the approach time its AR gives, mods and all)
                double kept = info.WarnMs + DanmakusuBalance.CATCH_FALL_HEIGHT * 1.25 / info.FallSpeed;
                hitObject.TimePreempt = kept;

                // (a slider's head has its own approach circle)
                if (hitObject is Slider { HeadCircle: { } head })
                    head.TimePreempt = kept;
            }
        }
    }
}
