using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Danmakusu.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// Everything (input, replays, drawable hit objects, pools) is osu!standard's; only the playfield differs.
    /// </summary>
    public partial class DrawableDanmakusuRuleset : DrawableOsuRuleset
    {
        public DrawableDanmakusuRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods)
        {
        }

        // (the bosses the player has chosen with a boss selection mod, if one is on; with no boss mod at all there is no boss)
        protected override Playfield CreatePlayfield() => new DanmakusuPlayfield(Mods.OfType<IBossSelection>().FirstOrDefault(), Mods.OfType<IBossSelection>().Any(selection => selection.Double), !Mods.OfType<IBossSelection>().Any(), Mods.OfType<IBossSelection>().Any(selection => selection.Triple));

        // (the keys are osu!standard's: key 1 and key 2 as the player has bound them for osu!standard are the freeze keys here too - the bindings are looked up by the ruleset's name)
        protected override PassThroughInputManager CreateInputManager() => new OsuInputManager(new OsuRuleset().RulesetInfo);

        // ---- a gameplay clock that has locked up ----
        // osu! (FrameStabilityContainer) does not move gameplay time on while the clock it follows is more than half a second away from it (it calls that "likely invalid"), frame after frame, unless that very
        // frame took more than half a second. So a hitch that leaves gameplay further behind than that stops it for good: the picture stands still while the music plays on, and moving the clock further on
        // only makes it worse. This runs every frame (the playfield does not: it is only updated while gameplay time moves): when gameplay time has stood still for a moment, nothing is paused and the clock is
        // that far away, the clock is sought back to where gameplay stands. The music jumps back that little and gameplay goes on from there.

        private double stuckSince = double.NaN, stuckTime = double.NaN;

        protected override void Update()
        {
            base.Update();

            // osu! moves gameplay on in even steps of a sixtieth of a second, however long each takes: when one takes 50 ms gameplay runs at a third of the music's speed until it is too far behind (see below), and that is all the
            // player sees of a heavy moment. A bullet hell can take uneven steps (everything here goes by the time): gameplay stays with the music and the frames get fewer instead. A replay keeps the even steps.
            // (Autoplay's replay is only a still cursor - the field moves the character as the frames come - so it goes as a play does: in even steps a heavy frame was run again and again to catch up,
            // and the game froze)
            bool even = HasReplayLoaded.Value && !autoplay;

            if (frame_stable != null && (bool)frame_stable.GetValue(this)! != even)
                frame_stable.SetValue(this, even);

            unstickClock();
        }

        private bool autoplay => Mods.Any(mod => mod is ModAutoplay);

        /// <summary>(osu! keeps this switch to itself; if it is ever gone, gameplay simply takes even steps again)</summary>
        private static readonly PropertyInfo? frame_stable = typeof(DrawableRuleset).GetProperty("FrameStablePlayback", BindingFlags.Instance | BindingFlags.NonPublic);

        private void unstickClock()
        {
            if (HasReplayLoaded.Value && !autoplay)
                return;

            Drawable? parent = Parent;

            while (parent != null && parent is not GameplayClockContainer)
                parent = parent.Parent;

            if (parent is not GameplayClockContainer clock || clock.IsPaused.Value)
            {
                stuckSince = double.NaN;
                return;
            }

            double now = FrameStableClock.CurrentTime;
            double real = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

            if (double.IsNaN(stuckSince) || Math.Abs(now - stuckTime) > 0.01)
            {
                stuckSince = real;
                stuckTime = now;
                return;
            }

            if (real - stuckSince > DanmakusuBalance.CLOCK_STUCK_MS && Math.Abs(clock.CurrentTime - now) > DanmakusuBalance.CLOCK_LOCK_MS)
            {
                Logger.Log($"Danmakusu: gameplay stood at {now:F0} ms while the clock was at {clock.CurrentTime:F0} ms: the clock is put back on gameplay");
                clock.Seek(now);
                stuckSince = double.NaN;
            }
        }
    }
}
