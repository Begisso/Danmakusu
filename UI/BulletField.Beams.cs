using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        /// <summary>Beams are only checked for being gone this often (game time), and only once the slider is over.</summary>
        private const double finish_check_interval_ms = 200;

        /// <summary>
        /// Feeds the beams of all lasers that are alive to the renderer. A beam is a polyline with few points (straight parts are single
        /// segments); only the segments that touch the screen are handed over. Beams that are over and flying away from the screen are
        /// marked finished once, and cost nothing after that.
        /// </summary>
        private void updateLasers(double drawTime, float fall)
        {
            int beams = 0;
            int pointTotal = 0;

            beamTime = drawTime; // (what Zanmu's ripples have stopped gets darker the longer it falls)

            float reisenAlpha = failed || reisenWindows.Length == 0 ? 0f : reisenLevel(drawTime, false, out _);

            // (Koishi: the beams vanish with the bullets in the glitch, all but the wood of the acts - trees, branches, stems, seaweed: a boss's own things are not hidden by the others; while everything is invisible the slices of a
            // beam are shown in the dim light round the player, like the bullets are)
            bool flickeredOut = !koishiHideAll && koishiFlicker > 0f && hash01(0, 99, (int)(drawTime / DanmakusuBalance.KOISHI_FLICKER_STEP_MS)) < koishiFlicker;

            int lo = laserLowerBound(drawTime - maxLaserSpan);
            int hi = flickeredOut ? lo : laserLowerBound(drawTime + 0.001);

            for (int i = lo; i < hi; i++)
            {
                var laser = lasers[i];

                // after death everything falls, so beams that were flying away might come back: nothing is skipped then
                if ((laser.Finished && !failed) || drawTime > laser.EndTime + laser.Lifetime)
                    continue;

                // spinner and boss beams are drawn in the colour of the map's background
                var colour = laser.SpinnerLike && backgroundColour is Color4 background ? background : palette[laserStyle(laser)];
                var border = laser.SpinnerLike && backgroundColour is Color4 ? DanmakusuBalance.BorderColour(colour) : borderPalette[laserStyle(laser) % borderPalette.Length];
                float margin = laser.MaxHalfWidth;

                bool checkFinished = !failed && hasScreenBounds && drawTime >= laser.EndTime
                                     && (drawTime - laser.LastFinishCheck >= finish_check_interval_ms || drawTime < laser.LastFinishCheck);

                // (a beam that Zanmu's ripples can stop: only once none of its slices is stopped - her ripples only stop what is on the screen, so a beam that has left it whole stays gone)
                bool freezerDone = laser.Freezer == null || drawTime > laser.Freezer.DoneAt;

                if (checkFinished)
                    laser.LastFinishCheck = drawTime;

                for (int beam = 0; beam < laser.BeamCount; beam++)
                {
                    if (!failed && laser.BeamFinished[beam])
                        continue;

                    int n = laser.GetPoints(drawTime, laserBuffer, beam, laserWidthBuffer, laserDistanceBuffer, laserLookBuffer);

                    if (ProfileTicks != null)
                    {
                        ProfileBeams++;
                        ProfilePoints += n;
                    }

                    if (n == 0)
                        continue;

                    if (checkFinished && (freezerDone || !anyStopped(n)) && (laser.Rotates ? rotatingBeamGone(laser, drawTime) : !beamCanReachScreen(laserBuffer, n, laser.Directions[beam], margin)))
                    {
                        laser.MarkFinished(beam);
                        continue;
                    }

                    // a ripple makes the beam fatter where it is going over it; the black hole bends it (more points on it for both)
                    if (beamsNeedRefining)
                        n = refineBeam(n);

                    if ((stageRippleCount > 0 || stageSmokeCount > 0 || sukunaLevel > 0f) && !laser.KeepsSize)
                    {
                        for (int q = 0; q < n; q++)
                            laserWidthBuffer[q] *= beamGrow(laserBuffer[q]);
                    }

                    // the part of a beam that is in smoke is half see-through, as much as a bullet in smoke
                    if (laserFadeBuffer.Length < n)
                        Array.Resize(ref laserFadeBuffer, Math.Max(n, laserFadeBuffer.Length * 2));

                    for (int q = 0; q < n; q++)
                        laserFadeBuffer[q] = stageSmokeCount > 0 && !laser.Immune ? 1f - DanmakusuBalance.SANNYO_SMOKE_TRANSPARENCY * smokeAt(laserBuffer[q]) : 1f;

                    // (Junko's slices are see-through for their first four beats)
                    if (laser.GhostMs > 0)
                    {
                        for (int q = 0; q < n; q++)
                            laserFadeBuffer[q] *= laser.GhostVisibility(laserDistanceBuffer[q] / Math.Max(laser.Speed, 1e-6f));
                    }

                    // (a piece of a slider's beam that something has stretched far past its length fades out)
                    for (int q = 0; q < n; q++)
                    {
                        if (laserLookBuffer[q] is { StretchVisible: < 1f } stretched)
                            laserFadeBuffer[q] *= stretched.StretchVisible;
                    }

                    // (Koishi: while everything is invisible only what is near the player can be seen, less and less the farther it is: as the bullets)
                    if (koishiHideAll && !laser.Immune)
                    {
                        float near = circleRadius * DanmakusuBalance.KOISHI_REVEAL_NEAR;
                        float span = Math.Max(circleRadius * (DanmakusuBalance.KOISHI_REVEAL_FAR - DanmakusuBalance.KOISHI_REVEAL_NEAR), 1f);

                        for (int q = 0; q < n; q++)
                        {
                            float lit = Math.Clamp((Vector2.Distance(laserBuffer[q], characterPosition) - near) / span, 0f, 1f);
                            laserFadeBuffer[q] *= (1f - lit * lit * (3f - 2f * lit)) * DanmakusuBalance.KOISHI_REVEAL_VISIBILITY;
                        }
                    }

                    // (what Rumia's circles hide cannot be seen, and over their edge is half see-through)
                    if (stageHideCount > 0 && !laser.Immune)
                    {
                        for (int q = 0; q < n; q++)
                            laserFadeBuffer[q] *= 1f - hideAt(laserBuffer[q]);
                    }

                    // (what Reisen has frozen is see-through)
                    if (reisenAlpha > 0f)
                    {
                        for (int q = 0; q < n; q++)
                        {
                            var pointLook = laserLookBuffer[q];

                            if (pointLook != null && pointLook.Alpha < 1f)
                                laserFadeBuffer[q] *= 1f - (1f - pointLook.Alpha) * reisenAlpha;
                        }
                    }

                    markSlicesGone(laser, beam, n, drawTime);

                    // after death the whole beam falls like a bullet would
                    Vector2 offset = Vector2.Zero;

                    if (failed)
                    {
                        Vector2 velocity = DanmakusuBalance.DEATH_BULLETS_KEEP_VELOCITY ? laser.Directions[beam] * laser.Speed : Vector2.Zero;
                        offset = velocity * fall + new Vector2(0, 0.5f * DanmakusuBalance.DEATH_GRAVITY * fall * fall);
                    }

                    for (int p = 0; p < n; p++)
                        laserBuffer[p] += offset;

                    // (what is frozen shakes a little before it is let go)
                    if (!failed)
                    {
                        for (int p = 0; p < n; p++)
                        {
                            var shaken = laserLookBuffer[p];

                            if (shaken != null && (shaken.Shaking || (shaken.ShakeFrom > 0 && drawTime >= shaken.ShakeFrom && drawTime < shaken.ShakeTo)))
                                laserBuffer[p] += shakeOffset(i * 16 + beam, p, drawTime);
                        }
                    }

                    if (lensOn && !laser.Immune)
                    {
                        for (int p = 0; p < n; p++)
                            laserBuffer[p] = lens(laserBuffer[p]);
                    }

                    if (n == 1)
                    {
                        if (sliceGone[0] || (hasScreenBounds && !isOnScreen(laserBuffer[0], margin)))
                            continue;

                        var (single, singleBorder) = beamColours(laserLookBuffer[0], colour, border);
                        addBeamRun(laserBuffer, laserWidthBuffer, laserDistanceBuffer, 0, 1, laser, single, singleBorder, ref beams, ref pointTotal);
                        continue;
                    }

                    // runs of consecutive segments that touch the screen; segment s joins points s and s + 1. (A part of the beam that looks different - bounced off the
                    // screen border, frozen... - has the colour of that look; a segment between two different looks has its own colour.)
                    int runStart = -1;
                    BulletLook? runLook = null;

                    for (int s = 0; s < n; s++)
                    {
                        bool keep = s < n - 1 && !sliceGone[s] && !sliceGone[s + 1] && portalOf(laserLookBuffer[s]) == portalOf(laserLookBuffer[s + 1]) && !fallenApart(laserLookBuffer[s], laserLookBuffer[s + 1]) && !fallenTooFar(laserLookBuffer[s], laserLookBuffer[s + 1], laserBuffer[s], laserBuffer[s + 1], laserWidthBuffer[s]) && (!hasScreenBounds || segmentTouchesScreen(laserBuffer[s], laserBuffer[s + 1], margin));

                        // (a slice that has fallen out of the bottom and come in from the top is not joined to its neighbour that has not yet)
                        if (keep && laser.Freezer != null && laserLookBuffer[s] is { Level: >= 0 } && laserLookBuffer[s + 1] is { Level: >= 0 }
                            && Math.Abs(laserBuffer[s].Y - laserBuffer[s + 1].Y) > (hasScreenBounds ? screenMax.Y - screenMin.Y : stageMax.Y - stageMin.Y) * 0.6f)
                            keep = false;

                        BulletLook? segmentLook = keep && ReferenceEquals(laserLookBuffer[s], laserLookBuffer[s + 1]) ? laserLookBuffer[s] : null;

                        // (a stretch of a beam that is turning grey, step by step: a segment between two steps has the lower one)
                        if (keep && segmentLook == null && laserLookBuffer[s] is { Level: >= 0 } lowerA && laserLookBuffer[s + 1] is { Level: >= 0 } lowerB)
                            segmentLook = lowerA.Level <= lowerB.Level ? lowerA : lowerB;

                        if (keep && runStart >= 0 && !ReferenceEquals(segmentLook, runLook))
                        {
                            // the colour changes here: the run so far ends at this point, and a new one starts at it
                            var (runBody, runBorder) = beamColours(runLook, colour, border);
                            addBeamRun(laserBuffer, laserWidthBuffer, laserDistanceBuffer, runStart, s - runStart + 1, laser, runBody, runBorder, ref beams, ref pointTotal);
                            runStart = s;
                            runLook = segmentLook;
                        }

                        if (keep && runStart < 0)
                        {
                            runStart = s;
                            runLook = segmentLook;
                        }

                        if (keep || runStart < 0)
                            continue;

                        // segments runStart..s-1 are visible: points runStart..s
                        var (endBody, endBorder) = beamColours(runLook, colour, border);
                        addBeamRun(laserBuffer, laserWidthBuffer, laserDistanceBuffer, runStart, s - runStart + 1, laser, endBody, endBorder, ref beams, ref pointTotal);
                        runStart = -1;
                    }
                }
            }

            if (beams > 1)
                Array.Sort(laserInstances, 0, beams, laserSpeedComparer);

            // the wood of Yuyuko's tree is drawn with the beams (the renderer draws it first, under everything)
            foreach (var tree in treeActs)
                tree.AddWood(this, drawTime, ref beams, ref pointTotal);

            foreach (var lily in lilyActs)
                lily.AddWood(this, drawTime, ref beams, ref pointTotal);

            foreach (var luna in lunaActs)
                luna.AddArcs(this, drawTime, ref beams, ref pointTotal);

            addDrumArcs(drawTime, ref beams, ref pointTotal); // (taiko's big notes)

            foreach (var elements in woodActs)
                elements.AddWood(this, drawTime, ref beams, ref pointTotal);

            // (the beams that Sekibanki's heads leave behind them)
            foreach (var heads in headActs)
                heads.AddTrails(this, drawTime, ref beams, ref pointTotal);

            // (the stems of Yuuka's sunflowers)
            foreach (var sunflowers in sunflowerActs)
                sunflowers.AddStems(this, drawTime, ref beams, ref pointTotal);

            // (the beams of the acts that have a DrawBeams)
            foreach (var act in stageActs)
                act.DrawBeams(this, drawTime, ref beams, ref pointTotal);

            // (the tails of the stars in Byakuren's sections)
            addStarTails(drawTime, ref beams, ref pointTotal);

            // (the beams Yorihime has copied)
            foreach (var returned in returnActs)
                returned.AddBeams(this, drawTime, ref beams, ref pointTotal);

            // (the beams Nemuno has taken)
            foreach (var nemuno in nemunoActs)
                nemuno.AddBeams(this, drawTime, ref beams, ref pointTotal);

            // (the sliders that fall in Urumi's section)
            foreach (var falling in objectFallActs)
                falling.AddBeams(this, drawTime, ref beams, ref pointTotal);

            // (Koishi's glitch has taken the beams away: only the wood stays)
            if (flickeredOut)
            {
                int kept = 0;

                for (int i = 0; i < beams; i++)
                {
                    if (laserInstances[i].Wood)
                        laserInstances[kept++] = laserInstances[i];
                }

                beams = kept;
            }

            // Wakasagihime's attacks: the beams are blue too (not the seaweed)
            float tideLevel = failed || tideWindows.Count == 0 ? 0f : attackPresence(tideWindows, drawTime) * DanmakusuBalance.WAKASAGI_TINT;

            if (tideLevel > 0.002f)
            {
                var blue = BulletRenderer.SpecialBodies[BulletRenderer.TINT_BLUE - 1];

                for (int i = 0; i < beams; i++)
                {
                    if (!laserInstances[i].Wood && !laserInstances[i].Immune)
                        laserInstances[i].Colour = BossHud.Interpolate(laserInstances[i].Colour, blue, tideLevel);
                }
            }

            impactBeams(drawTime, beams);

            // Remilia's attack: the beams glow as much as the bullets
            float beamBloom = failed ? 0f : Math.Max(remiliaLevel(drawTime), attackLevel(stampedeWindows, drawTime)) * DanmakusuBalance.REMILIA_BLOOM;

            if (beamBloom > 0.002f)
            {
                for (int i = 0; i < beams; i++)
                {
                    if (!laserInstances[i].Wood && !laserInstances[i].Immune)
                        laserInstances[i].Bloom = beamBloom;
                }
            }

            // Luna Child's attacks: every beam is smooth and glows
            if (lunaLevel > 0f && !failed)
            {
                for (int i = 0; i < beams; i++)
                    laserInstances[i].Bloom = Math.Max(laserInstances[i].Bloom, DanmakusuBalance.LUNA_BLOOM * lunaBlurAt(laserPointBuffer[laserInstances[i].Start + laserInstances[i].Count / 2]));
            }

            renderer.SetLasers(laserPointBuffer, laserWidthPointBuffer, laserDistancePointBuffer, laserFadePointBuffer, pointTotal, laserInstances, beams);
        }

        /// <summary>Has one of the first <paramref name="n"/> points just read into the buffers been stopped by Zanmu's ripples (it has a step of her grey)?</summary>
        private bool anyStopped(int n)
        {
            for (int q = 0; q < n; q++)
            {
                if (laserLookBuffer[q] is { Level: >= 0 })
                    return true;
            }

            return false;
        }

        /// <summary>The colours of a part of a beam that looks like <paramref name="look"/> (a bullet's special colour shows over the beam's own).</summary>
        private double beamTime;

        private (Color4 Body, Color4 Border) beamColours(BulletLook? look, Color4 body, Color4 border)
        {
            if (look == null || look.Overlay <= 0)
                return (body, border);

            Color4 tint = BulletRenderer.SpecialBodies[Math.Min(look.Overlay, BulletRenderer.SpecialBodies.Length) - 1];
            float amount = Math.Clamp(look.OverlayAmount, 0f, 1f);

            var mixed = new Color4(
                body.R + (tint.R - body.R) * amount,
                body.G + (tint.G - body.G) * amount,
                body.B + (tint.B - body.B) * amount,
                1f);

            if (look.Dark > 0f)
                mixed = new Color4(mixed.R * (1f - look.Dark), mixed.G * (1f - look.Dark), mixed.B * (1f - look.Dark), 1f);

            return (mixed, DanmakusuBalance.BorderColour(mixed));
        }

        /// <summary>The radius of a flower that a bullet has turned into (as big as the flowers of Yuyuko's tree, times <see cref="DanmakusuBalance.YUYUKO_FALL_FLOWER_SCALE"/>).</summary>
        private float fallFlowerRadius => DanmakusuBalance.YUYUKO_FALL_FLOWER_SCALE * (DanmakusuBalance.YUYUKO_PETAL_REACH + 0.5f) * DanmakusuBalance.YUYUKO_PETAL_SIZE * DanmakusuBalance.YUYUKO_TREE_HEIGHT * (stageMax.Y - stageMin.Y);

        private static readonly Color4 wood_border = new Color4(0.19f, 0.10f, 0.05f, 1f);
        private static readonly Color4 wood_body = new Color4(0.38f, 0.23f, 0.12f, 1f);
        private static readonly Color4 wood_core = new Color4(0.52f, 0.34f, 0.19f, 1f);

        private static readonly Color4 glowing_wood_core = new Color4(0.95f, 0.85f, 0.45f, 1f);

        /// <summary>
        /// One branch of a tree (Yuyuko's, Patchouli's): a stroke through <paramref name="length"/> points with the half width of every point. Wood that <paramref name="glows"/> has a warm light in
        /// its core and blooms (Patchouli's).
        /// </summary>
        private void addWoodBeam(Vector2[] points, float[] halfWidths, int length, ref int beams, ref int pointTotal, bool glows = false)
        {
            if (length < 2)
                return;

            if (pointTotal + length > laserPointBuffer.Length)
            {
                int size = Math.Max(laserPointBuffer.Length * 2, pointTotal + length);
                Array.Resize(ref laserPointBuffer, size);
                Array.Resize(ref laserWidthPointBuffer, size);
                Array.Resize(ref laserDistancePointBuffer, size);
                Array.Resize(ref laserFadePointBuffer, size);
            }

            if (beams == laserInstances.Length)
                Array.Resize(ref laserInstances, laserInstances.Length * 2);

            Array.Copy(points, 0, laserPointBuffer, pointTotal, length);
            Array.Copy(halfWidths, 0, laserWidthPointBuffer, pointTotal, length);
            Array.Clear(laserDistancePointBuffer, pointTotal, length);
            Array.Fill(laserFadePointBuffer, 1f, pointTotal, length);

            laserInstances[beams++] = new LaserInstance
            {
                Start = pointTotal,
                Count = length,
                Wood = true,
                Colour = wood_body,
                BorderColour = wood_border,
                CoreColour = glows ? glowing_wood_core : wood_core,
                Bloom = glows ? DanmakusuBalance.PATCHOULI_TREE_BLOOM : 0f,
                Speed = -1f,
            };

            pointTotal += length;
        }

        /// <summary>The last number given to beams that are drawn as one piece (<see cref="LaserInstance.Piece"/>; a new one for every piece).</summary>
        private int beamPieces;

        /// <summary>A beam through <paramref name="points"/>, <paramref name="halfWidth"/> wide everywhere (or <paramref name="widths"/> at each point, at most that), that glows like a boss's beam (the beams behind Sekibanki's heads).</summary>
        private void addPlainBeam(List<Vector2> points, float halfWidth, Color4 colour, Color4 border, ref int beams, ref int pointTotal, List<float>? widths = null)
        {
            int length = points.Count;

            if (length < 2)
                return;

            if (pointTotal + length > laserPointBuffer.Length)
            {
                int size = Math.Max(laserPointBuffer.Length * 2, pointTotal + length);
                Array.Resize(ref laserPointBuffer, size);
                Array.Resize(ref laserWidthPointBuffer, size);
                Array.Resize(ref laserDistancePointBuffer, size);
                Array.Resize(ref laserFadePointBuffer, size);
            }

            if (beams == laserInstances.Length)
                Array.Resize(ref laserInstances, laserInstances.Length * 2);

            points.CopyTo(0, laserPointBuffer, pointTotal, length);

            if (widths != null)
                widths.CopyTo(0, laserWidthPointBuffer, pointTotal, length);
            else
                Array.Fill(laserWidthPointBuffer, halfWidth, pointTotal, length);

            Array.Clear(laserDistancePointBuffer, pointTotal, length);
            Array.Fill(laserFadePointBuffer, 1f, pointTotal, length);

            laserInstances[beams++] = new LaserInstance
            {
                Start = pointTotal,
                Count = length,
                HalfWidth = halfWidth,
                Glow = true,
                Colour = colour,
                BorderColour = border,
                Speed = 0.01f,
            };

            pointTotal += length;
        }

        /// <summary>How much fatter a beam is at <paramref name="point"/>: a ripple that goes over it makes it fatter, and so does smoke.</summary>
        private float beamGrow(Vector2 point)
            => (stageRippleCount > 0 ? rippleGrow(point) : 1f) * (stageSmokeCount > 0 ? smokeGrow(smokeAt(point)) : 1f) * (sukunaLevel > 0f ? sukunaGrow(point) : 1f);

        /// <summary>(Tools/SectionCheck: sees every run of a beam that is handed to the renderer - the laser, the points, the first one and how many - to find beams that are drawn wrong; null when unused)</summary>
        public static Action<LaserEmitter, Vector2[], int, int, BulletLook?[]>? BeamProbe;

        /// <summary>(Tools/SectionCheck: where the cursor is at a moment: a player who moves, so that the boss and the portals move too; null when unused)</summary>
        public static Func<double, Vector2>? CursorProbe;

        /// <summary>(Tools/SectionCheck: every stage bullet where it is drawn, with the name of its act and a number of its own)</summary>
        public static Action<string, long, Vector2, double>? StageBulletProbe;

        private void addBeamRun(Vector2[] points, float[] widths, float[] distances, int start, int length, LaserEmitter laser, Color4 colour, Color4 border, ref int beams, ref int pointTotal)
        {
            BeamProbe?.Invoke(laser, points, start, length, laserLookBuffer);

            if (pointTotal + length > laserPointBuffer.Length)
            {
                int size = Math.Max(laserPointBuffer.Length * 2, pointTotal + length);
                Array.Resize(ref laserPointBuffer, size);
                Array.Resize(ref laserWidthPointBuffer, size);
                Array.Resize(ref laserDistancePointBuffer, size);
                Array.Resize(ref laserFadePointBuffer, size);
            }

            if (beams == laserInstances.Length)
                Array.Resize(ref laserInstances, laserInstances.Length * 2);

            Array.Copy(points, start, laserPointBuffer, pointTotal, length);
            Array.Copy(widths, start, laserWidthPointBuffer, pointTotal, length);
            Array.Copy(distances, start, laserDistancePointBuffer, pointTotal, length);
            Array.Copy(laserFadeBuffer, start, laserFadePointBuffer, pointTotal, length);

            laserInstances[beams++] = new LaserInstance
            {
                Start = pointTotal,
                Count = length,
                HalfWidth = laser.HalfWidth,
                Glow = laser.SpinnerLike || (laser.Shrinks && laser.PathFromOrigin), // the beams of spinners and bosses (and Junko's) bloom
                Thin = laser.ThinOutlines,
                Immune = laser.Immune,
                Colour = colour,
                BorderColour = border,
                // the dark edges of a spinner / boss beam start as the colour of the beam, at the edge of the magic circle, and are fully dark at twice that distance
                BorderFadeStart = laser.SpinnerLike ? DanmakusuBalance.MAGIC_CIRCLE_RADIUS : 0f,
                BorderFadeEnd = laser.SpinnerLike ? DanmakusuBalance.MAGIC_CIRCLE_RADIUS * 2f : 0f,
                Speed = laser.Speed,
                Bloom = sukunaLevel > 0f ? sukunaGlowAt(points[start + length / 2]) : 0f, // (Sukuna: the further from her, the more it glows)
            };

            pointTotal += length;
        }

        /// <summary>Does the box around the segment (grown by <paramref name="margin"/>) overlap the screen?</summary>
        private bool segmentTouchesScreen(Vector2 a, Vector2 b, float margin)
            => Math.Max(a.X, b.X) >= screenMin.X - margin && Math.Min(a.X, b.X) <= screenMax.X + margin
               && Math.Max(a.Y, b.Y) >= screenMin.Y - margin && Math.Min(a.Y, b.Y) <= screenMax.Y + margin;

        /// <summary>
        /// Once the slider is over, a beam is a rigid polyline gliding along <paramref name="direction"/>. Can any part of it, now or later,
        /// be on screen? (A segment sweeps a parallelogram; the separating axis test against the screen rectangle decides.)
        /// </summary>
        private bool beamCanReachScreen(Vector2[] points, int count, Vector2 direction, float margin)
        {
            Vector2 min = screenMin - new Vector2(margin);
            Vector2 max = screenMax + new Vector2(margin);

            if (count == 1)
                return sweptSegmentTouchesRect(points[0], points[0], direction, min, max);

            for (int i = 0; i + 1 < count; i++)
            {
                if (sweptSegmentTouchesRect(points[i], points[i + 1], direction, min, max))
                    return true;
            }

            return false;
        }

        private static bool sweptSegmentTouchesRect(Vector2 a, Vector2 b, Vector2 direction, Vector2 min, Vector2 max)
        {
            const float sweep_length = 100000f;

            Vector2 d = direction * sweep_length;
            Span<Vector2> quad = stackalloc Vector2[] { a, b, b + d, a + d };
            Span<Vector2> rect = stackalloc Vector2[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) };
            Span<Vector2> axes = stackalloc Vector2[] { new Vector2(1, 0), new Vector2(0, 1), new Vector2(-(b - a).Y, (b - a).X), new Vector2(-direction.Y, direction.X) };

            foreach (var axis in axes)
            {
                if (axis.LengthSquared < 1e-9f)
                    continue;

                float quadMin = float.MaxValue, quadMax = float.MinValue, rectMin = float.MaxValue, rectMax = float.MinValue;

                foreach (var q in quad)
                {
                    float v = Vector2.Dot(q, axis);
                    quadMin = Math.Min(quadMin, v);
                    quadMax = Math.Max(quadMax, v);
                }

                foreach (var r in rect)
                {
                    float v = Vector2.Dot(r, axis);
                    rectMin = Math.Min(rectMin, v);
                    rectMax = Math.Max(rectMax, v);
                }

                if (quadMax < rectMin || rectMax < quadMin)
                    return false;
            }

            return true;
        }

        private bool isOnScreen(Vector2 point, float margin)
            => point.X >= screenMin.X - margin && point.X <= screenMax.X + margin && point.Y >= screenMin.Y - margin && point.Y <= screenMax.Y + margin;

        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 1e-6f)
                return Vector2.Distance(point, a);

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
