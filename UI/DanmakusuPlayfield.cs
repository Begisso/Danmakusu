using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Osu.UI.Cursor;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Danmakusu.Mods;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>
    /// The regular osu! playfield (skinned cursor, approach circles, sliders, judgements, hit policy...)
    /// with a bullet layer on top.
    ///
    /// Hit objects are fully automated but visible:
    ///  - circles, slider heads, ticks, repeats and tails are hit (Great) the moment they are due, wherever the cursor is;
    ///  - spinners complete themselves invisibly (Spun Out logic); nobody spins anything, bullets spiral from the centre instead;
    ///  - clicking on circles is ignored, so a stray click can never produce a worse judgement;
    ///  - circles and sliders are drawn above the bullets; touching an active slider body damages the player (see <see cref="BulletField"/>).
    /// </summary>
    public partial class DanmakusuPlayfield : OsuPlayfield
    {
        private readonly OsuModSpunOut spinnerAutomation = new OsuModSpunOut();
        private readonly List<DrawableOsuHitObject> due = new List<DrawableOsuHitObject>();
        private BulletField? bulletField;
        private FlashlightOverlay? flashlight;

        private readonly IBossSelection? bossSelect;

        private readonly bool alwaysDouble;
        private readonly bool alwaysTriple;

        private readonly bool noBoss;

        public DanmakusuPlayfield(IBossSelection? bossSelect = null, bool alwaysDouble = false, bool noBoss = false, bool alwaysTriple = false)
        {
            this.bossSelect = bossSelect;
            this.alwaysDouble = alwaysDouble;
            this.alwaysTriple = alwaysTriple;
            this.noBoss = noBoss;

            // (the Smoke action is not used by this ruleset: no smoke trails)
            Smoke.Alpha = 0;
        }

        /// <summary>The beatmap being played; the colours of its own [Colours] section choose its bosses (whatever the skin and "beatmap colours" settings say).</summary>
        [Resolved(CanBeNull = true)]
        private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

        private IBeatmap? plannedBeatmap;
        private BossInfo[] fromColours = [];
        private IReadOnlyList<Color4> ownColours = [];
        private BossInfo[] currentBosses = [];
        private int plannedTuning;
        private string? tuningMap;

        /// <summary>Everything that belongs to the bullet field (the field and its proxies, the layers above it): taken away again when the bosses are changed.</summary>
        private readonly List<Drawable> fieldLayers = new List<Drawable>();

        /// <summary>The bosses that the combo colours of the map choose.</summary>
        public BossInfo[] BossesFromColours => fromColours;

        [BackgroundDependencyLoader]
        private void load(IBeatmap beatmap)
        {
            plannedBeatmap = beatmap;

            // (the adjustments of the boss menu stay while the same map is restarted and are reset otherwise)
            tuningMap = beatmap.BeatmapInfo.ID.ToString();
            BossTuning.MapOpened(tuningMap);

            // The bosses follow from the combo colours of the map: no colours of its own -> the colour of its background -> Marisa.
            var mapColours = MapColours.Read(workingBeatmap?.Value);

            if (mapColours.Count == 0 && BackgroundColour.Of(workingBeatmap?.Value) is Color4 background)
                mapColours = [background];

            fromColours = BossMatcher.Match(mapColours);
            ownColours = mapColours;

            // (the bosses of the boss selection mod, if one is on; otherwise the colours choose - the player can still change them in the menu before the map starts)
            buildField(beatmap, bossSelect?.Choose(fromColours) ?? fromColours);
        }

        /// <summary>Two or three bosses at a time needs that many bosses: if fewer are chosen, the others are the ones the colours choose, or else the next ones in the list.</summary>
        private BossInfo[] withEnough(BossInfo[] bosses)
        {
            int least = alwaysTriple ? 3 : alwaysDouble ? 2 : 1;

            if (bosses.Length >= least)
                return bosses;

            var all = bosses.ToList();

            foreach (var boss in fromColours)
            {
                if (all.Count < least && !all.Contains(boss))
                    all.Add(boss);
            }

            int next = all.Count > 0 ? all[^1].Index : 0;

            while (all.Count < least)
            {
                next = (next + 1) % BossRoster.All.Count;

                if (!all.Contains(BossRoster.All[next]))
                    all.Add(BossRoster.All[next]);
            }

            return all.ToArray();
        }

        /// <summary>Plans the map again with these bosses (nothing happens if they are the ones that the field has already). Used by the boss menu before the map starts.</summary>
        public void ChooseBosses(BossInfo[] bosses)
        {
            if (plannedBeatmap == null || bosses.Length == 0)
                return;

            bosses = withEnough(bosses);

            if (bosses.SequenceEqual(currentBosses) && plannedTuning == BossTuning.Version)
                return;

            foreach (var layer in fieldLayers)
                RemoveInternal(layer, true);

            fieldLayers.Clear();
            bulletField = null;
            flashlight = null;

            buildField(plannedBeatmap, bosses);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (tuningMap != null)
            {
                BossTuning.MapLeft(tuningMap);
                tuningMap = null;
            }

            base.Dispose(isDisposing);
        }

        private SongBands? bands;
        private bool bandsRead;

        /// <summary>The song's treble, bass and mid (read once, and only when a map has too few sections of its own: it decodes the whole track).</summary>
        private SongBands? songBands()
        {
            if (!bandsRead)
            {
                bands = SongBands.From(workingBeatmap?.Value);
                bandsRead = true;
            }

            return bands;
        }

        private void addLayer(Drawable layer)
        {
            AddInternal(layer);
            fieldLayers.Add(layer);
        }

        private void buildField(IBeatmap beatmap, BossInfo[] bosses)
        {
            var difficulty = beatmap.Difficulty;
            bosses = withEnough(bosses);
            currentBosses = bosses;
            plannedTuning = BossTuning.Version;

            // the bosses were chosen by a mod (and are not the ones that the colours choose anyway): the combo colours of the map become the colours of the bosses
            // (two bosses: the map has eight combo colours, the first four are the first boss's, the other four the second boss's)
            Color4[]? bossColours = bossSelect != null && !bosses.SequenceEqual(fromColours) ? BossColours.Of(bosses) : null;

            if (bossColours == null && bosses.Length == 2 && fromColours.Length == 1 && bosses[0] == fromColours[0] && ownColours.Count > 0)
            {
                // (the colours of the map choose one boss and the other comes from elsewhere: the map's own colours stay the first boss's four)
                var eight = new List<Color4>();

                for (int i = 0; i < BossMatcher.COLOURS_PER_BOSS; i++)
                    eight.Add(ownColours[i % ownColours.Count]);

                eight.AddRange(bosses[1].Colours);
                bossColours = eight.ToArray();
            }

            BossColours.Set(beatmap, bossColours);

            // a map of another mode (see ConvertedModes): a mania or taiko ring is twice as big in the boss sections (planned once to know them: the sections do not depend on the ring); a catch map is
            // the right way up
            int convertedFrom = ConvertedModes.ModeOf(beatmap);
            catchMap = convertedFrom == ConvertedModes.CATCH;
            ringMap = convertedFrom is ConvertedModes.MANIA or ConvertedModes.TAIKO;

            if (convertedFrom != ConvertedModes.STANDARD)
            {
                ConvertedModes.Arrange(beatmap, null);

                if (!noBoss && convertedFrom is ConvertedModes.MANIA or ConvertedModes.TAIKO)
                {
                    BulletPlanner.Plan(beatmap, out _, out var sections, bosses, alwaysDouble, noBoss, songBands, alwaysTriple);
                    ConvertedModes.Arrange(beatmap, sections.Select(period => (period.Start, period.End)).ToList());
                }
            }

            var emitters = BulletPlanner.Plan(beatmap, out var lasers, out var bossPeriods, bosses, alwaysDouble, noBoss, songBands, alwaysTriple);

            // the circles and sliders of a boss section take the colours of its boss (with two or three on the screen, the combos take turns between them); so do the bullets and beams they fire, when the map's colours are the bosses'
            var colourSections = bossPeriods.Where(period => !period.Borrowed && period.Rank is >= 0 and < 3 && period.Boss != null)
                                            .Select(period => new BossColours.Section(period.Start, Math.Max(period.End, period.SequenceEnd), period.Rank, period.BossSlot)).ToArray();
            BossColours.SetSections(beatmap, BossColours.Of(bosses), colourSections);

            // (Doremy's and Sagume's copies of the circles and sliders hurt like the originals: they are zones too)
            var copies = CloneLayer.MakeCopies(beatmap, bossPeriods);

            var field = new BulletField(
                emitters,
                lasers,
                bossPeriods,
                DanmakusuBalance.OsuCircleRadius(difficulty.CircleSize),
                BulletPlanner.PlanSliderZones(beatmap, copies.Select(copy => (HitObject)copy.Copy)),
                BulletPlanner.PlanCircleZones(beatmap, copies.Select(copy => (HitObject)copy.Copy)),
                DanmakusuBalance.DamagePerBullet(difficulty.DrainRate),
                DanmakusuBalance.DamagePerSliderTick(difficulty.DrainRate),
                getCursorScreenPosition)
            {
                RelativeSizeAxes = Axes.Both,
                PlainBulletSpeed = DanmakusuBalance.BaseBulletSpeed(difficulty.ApproachRate) / 1000f, // (what a fast bullet is, for the whooshes as they fly past)
                FlyBySpeedScale = ringMap ? DanmakusuBalance.CONVERT_RING_BULLET_SPEED : 1f, // (a mania or taiko map's objects shoot twice as fast)
                ColourSections = bossColours != null ? colourSections : null, // (the palette is the bosses' colours, four each: what circles and sliders fire takes the colours they have)
                FallbackSongEndTime = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Max(h => h.GetEndTime()) : double.PositiveInfinity,
                // (taiko's big notes: a ring of arcs each, as many as a plain ring has bullets)
                DrumRings = ConvertedModes.DrumRings(beatmap, time => DanmakusuBalance.BulletsPerRing(difficulty.OverallDifficulty, BulletKind.Normal, beatmap.ControlPointInfo.EffectPointAt(time).KiaiMode)),
                // positive depth = further back: bullets are drawn below circles and sliders.
                Depth = 1,
            };

            // miss markers, drawn above everything (playfield coordinates, same as the bullet field).
            var missLayer = new Container { RelativeSizeAxes = Axes.Both };
            field.PlayerHit += position => missLayer.Add(new BulletMissEffect { Position = position });

            addLayer(field);

            // Urumi: the circles and sliders that fall, under the bullets so that they hide none
            if (field.HasFallingObjects)
            {
                field.FallingObjectsShown = true;
                addLayer(new FallLayer(field) { Depth = 2 });
            }

            // Doremy's and Sagume's copies of the circles and sliders: above the bullets, under the real ones
            if (copies.Count > 0)
                addLayer(new CloneLayer(copies, DanmakusuBalance.BulletRadius(beatmap.Difficulty.CircleSize)) { Depth = 0.5f });

            // Clownpiece's moon is above everything (circles, sliders, bullets and beams too) except the boss, the character and the HUD
            if (field.HasMoon)
            {
                addLayer(field.CreateMoonProxy());
                addLayer(field.CreateBossProxy());
                addLayer(field.CreateSecondBossProxy());
                addLayer(field.CreateThirdBossProxy());
                addLayer(field.CreateBallsProxy());
            }

            // the planets, UFOs and drums of some bosses are above the hit objects too
            if (field.HasStage)
                addLayer(field.CreateStageFrontProxy());

            // the character itself is drawn on top of everything again.
            addLayer(field.CreateCharacterProxy());

            addLayer(missLayer);

            // the flashlight of the bosses' mechanics (Mystia's) is above all of that, but not above the boss HUD
            bulletField = field;
            addLayer(flashlight = new FlashlightOverlay());

            // Utsuho's black hole is above everything except the HUD
            if (field.HasStage)
                addLayer(field.CreateStageTopProxy());

            addLayer(field.CreateHudProxy());
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Shrinks the skinned cursor, its trail and its ripples (the cursor multiplies this into its own scale).
            if (Cursor is OsuCursorContainer osuCursor)
                osuCursor.ActiveCursor.ModScaleAdjust.Value = DanmakusuBalance.CURSOR_SCALE;

            // (a catch map: the follow points would stand along the bottom while what they join falls)
            if (catchMap)
                FollowPoints.Alpha = 0f;
        }

        // The cursor drawable is the skinned osu! cursor, so the character follows exactly what the player sees.
        private Vector2? getCursorScreenPosition() => Cursor?.ActiveCursor?.ScreenSpaceDrawQuad.Centre;

        #region Per-drawable setup (runs once per pooled drawable; nested drawables get their own call too)

        protected override void OnNewDrawableHitObject(DrawableHitObject drawable)
        {
            base.OnNewDrawableHitObject(drawable);

            switch (drawable)
            {
                case DrawableSpinner:
                    // The spinner completes itself (so judgement/combo stay intact) but is completely invisible:
                    // its place is taken by the spiral bullet pattern from BulletPlanner. A transparent colour is used
                    // instead of Alpha = 0, because a non-present drawable would stop updating and never complete.
                    spinnerAutomation.ApplyToDrawableHitObject(drawable);
                    drawable.Colour = Color4.Transparent;
                    break;

                case DrawableHitCircle circle:
                    // HitReceptor is created in the circle's load(), so wait until it exists. Slider heads are circles too.
                    circle.OnLoadComplete += _ => circle.HitArea.CanBeHit = () => false;
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Luna Child: in her attacks the circles and sliders are hidden the way the Hidden mod hides them (imitated): a circle (and a slider's head) fades out early, over 30 % of its approach
        /// from 40 % of it on, without an approach circle, and a slider's body fades out over the whole slider. Done after the objects' own updates (their transforms have been applied by then), on their
        /// colour's alpha, which osu! does not animate; every object outside her attacks gets its full colour back (the drawables are pooled and come back for other objects).
        /// </summary>
        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            swapCursor();

            if (catchMap)
                fallObjects();
            else if (ringMap)
                hideStillBalls();

            if (bulletField == null || !bulletField.HasSilence)
                return;

            double time = Time.Current;

            foreach (var drawable in HitObjectContainer.AliveObjects)
            {
                if (drawable is not DrawableOsuHitObject osuObject || drawable.HitObject is not Rulesets.Osu.Objects.OsuHitObject hitObject)
                    continue;

                bool hidden = bulletField.HitsoundsMuted(hitObject.StartTime);
                double preempt = hitObject.TimePreempt;
                double fadeOutStart = hitObject.StartTime - preempt + preempt * DanmakusuBalance.LUNA_HIDDEN_FADE_IN_SHARE;
                float circle = hidden ? 1f - (float)Math.Clamp((time - fadeOutStart) / Math.Max(preempt * DanmakusuBalance.LUNA_HIDDEN_FADE_OUT_SHARE, 1), 0, 1) : 1f;

                switch (osuObject)
                {
                    case DrawableHitCircle hitCircle:
                        hitCircle.Colour = new Color4(1f, 1f, 1f, Math.Max(circle, 0.002f));

                        if (hidden)
                            hitCircle.ApproachCircle.Alpha = 0f;

                        break;

                    case DrawableSlider slider:
                    {
                        double end = slider.HitObject.EndTime;
                        float body = hidden ? 1f - (float)Math.Clamp((time - fadeOutStart) / Math.Max(end - fadeOutStart, 1), 0, 1) : 1f;

                        slider.Body.Colour = new Color4(1f, 1f, 1f, Math.Max(body, 0.002f));
                        slider.HeadCircle.Colour = new Color4(1f, 1f, 1f, Math.Max(circle, 0.002f));

                        if (hidden)
                            slider.HeadCircle.ApproachCircle.Alpha = 0f;

                        break;
                    }
                }
            }
        }

        /// <summary>A catch map (see ConvertedModes): its circles and sliders fall.</summary>
        private bool catchMap;

        /// <summary>A mania or taiko map: its long notes and drumrolls are sliders that do not move.</summary>
        private bool ringMap;

        /// <summary>A long note or a drumroll looks like a circle: its slider has no ball (nor the ball's follow circle), only its round body for as long as it lasts.</summary>
        private void hideStillBalls()
        {
            foreach (var drawable in HitObjectContainer.AliveObjects)
            {
                if (drawable is DrawableSlider slider && ConvertedModes.Of(slider.HitObject) is { Static: true })
                    slider.Ball.Alpha = 0f;
            }
        }

        /// <summary>
        /// osu!catch: every circle and slider is drawn falling straight down, evenly, landing on the bottom wall of the screen when it is due (and on below it as it goes): where it is follows from the time.
        /// Its approach circle stands on the top wall straight above it, centred on its line, closing as it falls (the top half of its bullets comes out of the wall there). No slider ball, as in catch.
        /// Only the look: it is hit when it is due, and what hurts falls with it (SliderZone.FallSpeed).
        /// </summary>
        private void fallObjects()
        {
            if (bulletField == null)
                return;

            double time = Time.Current;
            Vector2 landing = bulletField.CatchLanding;
            float top = bulletField.TopWall;
            float bottom = DanmakusuBalance.CATCH_BOTTOM + landing.Y;

            foreach (var drawable in HitObjectContainer.AliveObjects)
            {
                if (drawable is not DrawableOsuHitObject osuObject || drawable.HitObject is not Rulesets.Osu.Objects.OsuHitObject hitObject || ConvertedModes.Of(hitObject) is not { Mode: ConvertedModes.CATCH } info)
                    continue;

                osuObject.Position = hitObject.StackedPosition + landing + ConvertedModes.FallOffset(info, hitObject.StartTime, time);

                // the approach circle warns that it comes in: it closes, over its approach time, the moment its middle comes in through the top wall, there (and is gone then), not as it lands. (It is drawn inside
                // the circle, at its scale: so far up, in its units, that its centre is on the top wall)
                double comesIn = hitObject.StartTime - (bottom - top) / Math.Max(info.FallSpeed, 1e-4f);
                double warn = Math.Max(info.WarnMs, 1);
                float u = (float)Math.Clamp((time - (comesIn - warn)) / warn, 0, 1);
                float alpha = time >= comesIn - warn && time < comesIn ? 0.9f * Math.Min(1f, 2f * u) : 0f;
                float up = (top - osuObject.Position.Y) / Math.Max(hitObject.Scale, 1e-3f);

                var approach = osuObject switch
                {
                    DrawableHitCircle circle => circle.ApproachCircle,
                    DrawableSlider slider => slider.HeadCircle?.ApproachCircle,
                    _ => null,
                };

                if (approach != null)
                {
                    approach.Position = new Vector2(0f, up);
                    approach.Scale = new Vector2(4f - 3f * u); // (from four times as big, as osu!'s)
                    approach.Alpha = alpha;
                }

                if (osuObject is DrawableSlider stream)
                    stream.Ball.Alpha = 0f;
            }
        }

        #region Automatic hitting

        protected override void Update()
        {
            base.Update();

            // (the skinned cursor goes back to the mouse before anything reads it: see swapCursor)
            restoreCursor();

            double time = Time.Current;

            if (bulletField != null && bulletField.HasMirror)
                updateMirror(time);

            // Koishi: the circles and sliders vanish with her bullets (never exactly 0: a drawable that is not present would stop updating and could never be judged)
            if (bulletField != null && bulletField.HasKoishi)
                HitObjectContainer.Alpha = Math.Max(bulletField.KoishiHitObjectAlpha(time), 0.002f);

            // (Hecatia's impact frame: the circles and sliders turn black with everything else)
            if (bulletField != null)
                HitObjectContainer.Colour = bulletField.InImpactFrame(time) ? osuTK.Graphics.Color4.Black : osuTK.Graphics.Color4.White;

            if (flashlight != null && bulletField != null)
            {
                Vector2? cursor = getCursorScreenPosition();

                if (cursor != null && bulletField.TryGetFlashlight(out float radius, out float strength))
                    flashlight.SetState(strength, radius, ToLocalSpace(cursor.Value));
                else
                    flashlight.SetState(0, 0, Vector2.Zero);
            }

            // Luna Child: no hitsounds while she attacks (the volume of every circle, slider and nested object of the screen is set every frame)
            if (bulletField != null && bulletField.HasSilence)
            {
                double volume = bulletField.HitsoundsMuted(time) ? 0 : 1;

                foreach (var drawable in HitObjectContainer.AliveObjects)
                {
                    foreach (var sound in drawable.ChildrenOfType<PausableSkinnableSound>())
                        sound.Volume.Value = volume;
                }
            }

            // Collect first, judge afterwards: judging can trigger state changes / health processing.
            due.Clear();

            foreach (var drawable in HitObjectContainer.AliveObjects)
            {
                switch (drawable)
                {
                    case DrawableSlider slider:
                        // head, ticks, repeats and tail; the slider itself is judged by its own logic once the tail is.
                        foreach (var nested in slider.NestedHitObjects)
                            collectIfDue(nested, time);

                        break;

                    case DrawableHitCircle circle:
                        collectIfDue(circle, time);
                        break;
                }
            }

            if (due.Count > 1)
            {
                // stable, so a slider's nested objects stay in order (ticks before the tail).
                foreach (var hitObject in due.OrderBy(d => d.HitObject.StartTime).ToList())
                    hitObject.HitForcefully();
            }
            else if (due.Count == 1)
                due[0].HitForcefully();
        }

        #region The cursor drawn where the character is

        private Vector2? realCursor, shownCursor;

        /// <summary>
        /// While the character is held away from the mouse (frozen, in Enoko's trap, at the edge of Eiki's circle) the skinned cursor is drawn where the character is (the character's ring is drawn where the
        /// mouse is: see BulletField.CursorSwapped). Only the look: the field has already read where the mouse is from the cursor drawable this frame, and it is put back on the mouse at the start of the
        /// next (<see cref="restoreCursor"/>), so that the field reads the mouse, not the character, then too.
        /// </summary>
        private void swapCursor()
        {
            var active = Cursor?.ActiveCursor;

            if (bulletField == null || active?.Parent == null || !bulletField.CursorSwapped)
                return;

            realCursor = active.Position;
            active.Position = active.Parent.ToLocalSpace(bulletField.CharacterScreenPosition);
            shownCursor = active.Position;
        }

        /// <summary>Puts the skinned cursor back where the mouse is, unless the mouse has moved since it was put on the character (the cursor container has put it on the mouse already then).</summary>
        private void restoreCursor()
        {
            var active = Cursor?.ActiveCursor;

            if (active != null && shownCursor != null && realCursor != null && active.Position == shownCursor.Value)
                active.Position = realCursor.Value;

            realCursor = shownCursor = null;
        }

        #endregion

        #region Seija's mirrored screen

        private CursorTrail? cursorTrail;
        private bool mirrorWasOn;

        /// <summary>
        /// While Seija attacks the whole playfield is turned 180 degrees round its centre (= mirrored along both axes), smoothly. Its cursor turns with it, so the cursor
        /// is drawn at the mirror image of where the mouse is - while everything that the game works out from it (the character, the flashlight...) keeps using the real
        /// place: the cursor drawable is put where the mouse would be without the turn, and the turn then carries it to the other side.
        /// </summary>
        private void updateMirror(double time)
        {
            float angle = bulletField!.MirrorAngleAt(time);
            Rotation = angle;

            bool on = angle > 0;

            // (also in the frame after the last mirrored one: a mouse move in that frame was placed by the still-turned playfield, and without this the cursor - and the
            // character, which follows it - would jump to the mirror image and sweep through every bullet on its way)
            if ((on || mirrorWasOn) && Cursor?.ActiveCursor != null)
            {
                var input = GetContainingInputManager();

                if (input != null)
                {
                    // (the mouse in the turned playfield's coordinates, then back through the turn: where it is without it)
                    Vector2 local = ToLocalSpace(input.CurrentState.Mouse.Position);
                    Vector2 centre = DrawSize / 2;
                    Vector2 d = local - centre;
                    float radians = angle * MathF.PI / 180f;
                    float cos = MathF.Cos(radians), sin = MathF.Sin(radians);

                    Cursor.ActiveCursor.Position = centre + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
                }
            }

            // the trail of the cursor remembers where the mouse really went: it would not follow the mirrored cursor, so it is hidden meanwhile
            if (on || mirrorWasOn)
            {
                if (cursorTrail?.Parent == null && Cursor != null)
                    cursorTrail = findTrail(Cursor);

                if (cursorTrail != null)
                    cursorTrail.Alpha = 1f - angle / 180f;
            }

            mirrorWasOn = on;
        }

        private static CursorTrail? findTrail(Drawable drawable)
        {
            switch (drawable)
            {
                case CursorTrail trail:
                    return trail;

                case SkinnableDrawable skinnable:
                    return skinnable.Drawable != null ? findTrail(skinnable.Drawable) : null;

                case Container<Drawable> container:
                    foreach (var child in container.Children)
                    {
                        var found = findTrail(child);

                        if (found != null)
                            return found;
                    }

                    return null;

                default:
                    return null;
            }
        }

        #endregion

        private void collectIfDue(DrawableHitObject drawable, double time)
        {
            if (drawable is DrawableOsuHitObject osuObject && !osuObject.Judged && time >= osuObject.HitObject.StartTime)
                due.Add(osuObject);
        }

        #endregion
    }
}
