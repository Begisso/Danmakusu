using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Urumi Ushizaki: in her section every circle, the moment it is hit, and every slider, the moment it is over, turns grey with a slight glow round it and falls on the player: it is pulled the way the player was then
        // and keeps that way, faster and faster and getting darker, as Zanmu's bullets fall, until it has left the screen. A slider falls as it is, keeping its shape. For a moment it does not hurt (so that standing in a circle
        // as it is hit does not cost twice); then both hurt like a slider body, for as long as the character touches them. Where each falls is a closed form of time, so only the way it falls has to be learnt, when it begins.
        // The objects are shown as they are, skin and all, in a grey combo colour, by a FallLayer; this act adds the light round them (with no layer, as in the boss menu's preview, it draws plain ones itself).
        // What already falls, a catch map's circles and juice streams, is left alone.
        // ======================================================================================================================================================

        private sealed class ObjectFallAct : StageAct
        {
            private sealed class Falling
            {
                public OsuHitObject? Source;
                public double Time;
                public Vector2 Place; // (where a circle is; the middle of a slider's path: the way to the player is measured from there)
                public float Radius;
                public SliderZone? Slider;
                public Vector2 Way = Vector2.UnitY;
            }

            private readonly List<Falling> objects = new List<Falling>();
            private readonly List<Vector2> beamPoints = new List<Vector2>();
            private int next;

            public ObjectFallAct(BossPeriod period, CircleZone[] circles, SliderZone[] sliders)
            {
                Period = period;

                foreach (var circle in circles)
                {
                    if (circle.Time >= period.ShootStart && circle.Time < period.End)
                        objects.Add(new Falling { Source = circle.Source, Time = circle.Time, Place = circle.Position, Radius = circle.Radius });
                }

                foreach (var slider in sliders)
                {
                    // (not what already falls, a catch map's: a juice stream is its trail, up to the screen's height, and fell again from where it was made to lie, a beam standing up across the whole screen)
                    if (slider.FallSpeed > 0f)
                        continue;

                    if (slider.EndTime >= period.ShootStart && slider.EndTime < period.End && slider.Points.Length > 1)
                        objects.Add(new Falling { Source = slider.Source, Time = slider.EndTime, Place = slider.Points[slider.Points.Length / 2], Radius = slider.Radius, Slider = slider });
                }

                objects.Sort((a, b) => a.Time.CompareTo(b.Time));
                Start = objects.Count > 0 ? objects[0].Time : period.ShootStart;
                End = objects.Count > 0 ? objects[^1].Time + DanmakusuBalance.URUMI_FALL_MS : Start;
            }

            public override void Reset(BulletField f, double time)
            {
                next = objects.FindIndex(o => o.Time > time);

                if (next < 0)
                    next = objects.Count;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // (the way it falls is the way to the player when it begins, and it keeps that way)
                while (next < objects.Count && objects[next].Time <= now)
                {
                    var o = objects[next++];
                    Vector2 way = f.characterPosition - o.Place;
                    o.Way = way.LengthSquared > 1e-4f ? Vector2.Normalize(way) : Vector2.UnitY;
                }
            }

            /// <summary>The first object that can still be falling at <paramref name="t"/> (they are in time order).</summary>
            private int firstAt(double t)
            {
                int low = 0, high = objects.Count;

                while (low < high)
                {
                    int middle = (low + high) / 2;

                    if (objects[middle].Time + DanmakusuBalance.URUMI_FALL_MS < t)
                        low = middle + 1;
                    else
                        high = middle;
                }

                return low;
            }

            private static Vector2 fallenBy(Falling o, double t)
            {
                float fallen = (float)Math.Max(t - o.Time, 0.0);
                return o.Way * (0.5f * DanmakusuBalance.ZANMU_GRAVITY * fallen * fallen);
            }

            /// <summary>Its grey at <paramref name="t"/>, darker and darker as it falls.</summary>
            private static Color4 greyAt(Falling o, double t)
            {
                Color4 grey = BulletRenderer.SpecialBodies[BulletRenderer.TINT_GRAY - 1];
                float keep = 1f - DanmakusuBalance.ZANMU_FALL_SHADE * smoothStep((t - o.Time) / DanmakusuBalance.ZANMU_FALL_DARKEN_MS);
                return new Color4(grey.R * keep, grey.G * keep, grey.B * keep, 1f);
            }

            /// <summary>What is falling at <paramref name="t"/>, for the <see cref="FallLayer"/> (only what has begun: its way has to be known).</summary>
            public void Collect(double t, List<(OsuHitObject Source, double Began, Vector2 By)> into)
            {
                for (int i = firstAt(t); i < next && objects[i].Time <= t; i++)
                {
                    var o = objects[i];

                    if (o.Source != null)
                        into.Add((o.Source, o.Time, fallenBy(o, t)));
                }
            }

            /// <summary>With no <see cref="FallLayer"/>: the sliders that are falling, drawn like the other beams (the field calls this while it hands the beams to the renderer).</summary>
            public void AddBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || f.FallingObjectsShown)
                    return;

                for (int i = firstAt(t); i < objects.Count && objects[i].Time <= t; i++)
                {
                    var o = objects[i];

                    if (o.Slider == null)
                        continue;

                    Vector2 by = fallenBy(o, t);
                    Color4 body = greyAt(o, t);
                    beamPoints.Clear();

                    foreach (var point in o.Slider.Points)
                        beamPoints.Add(point + by);

                    f.addPlainBeam(beamPoints, o.Radius, body, DanmakusuBalance.BorderColour(body), ref beams, ref pointTotal);
                }
            }

            /// <summary>What is falling hurts like a slider body, circles too (as far as they are drawn, as a slider is).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float hit = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = firstAt(now); i < objects.Count && objects[i].Time <= now; i++)
                {
                    var o = objects[i];

                    if (now < o.Time + DanmakusuBalance.URUMI_FALL_GRACE_MS)
                        continue;

                    Vector2 character = f.characterPosition - fallenBy(o, now);

                    if (o.Slider != null ? o.Slider.Touches(character, hit) : Vector2.Distance(character, o.Place) <= o.Radius + hit)
                        return true;
                }

                return false;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                for (int i = firstAt(t); i < objects.Count && objects[i].Time <= t; i++)
                {
                    var o = objects[i];
                    Vector2 by = fallenBy(o, t);
                    Color4 body = greyAt(o, t);
                    Vector2 glow = new Vector2(o.Radius * DanmakusuBalance.URUMI_FALL_GLOW_SIZE);

                    // (a slider: a little light all along it, less at each place as they overlap)
                    if (o.Slider != null)
                    {
                        if (f.FallingObjectsShown)
                        {
                            for (int k = 0; k < o.Slider.Points.Length; k += 3)
                                f.stageOrbs.Add(StageTexture.Glow, o.Slider.Points[k] + by, glow, 0f, DanmakusuBalance.URUMI_FALL_GLOW * 0.5f, body, true);
                        }

                        continue;
                    }

                    Vector2 at = o.Place + by;
                    float margin = o.Radius * DanmakusuBalance.URUMI_FALL_GLOW_SIZE;

                    if (f.hasScreenBounds && (at.X < f.screenMin.X - margin || at.X > f.screenMax.X + margin || at.Y < f.screenMin.Y - margin || at.Y > f.screenMax.Y + margin))
                        continue;

                    // (a little light round it: the field's big bullets glow far too much)
                    f.stageOrbs.Add(StageTexture.Glow, at, glow, 0f, DanmakusuBalance.URUMI_FALL_GLOW, body, true);

                    if (f.FallingObjectsShown)
                        continue;

                    // (with no layer: made like a small bullet, with a border, sharp however big it is)
                    float size = o.Radius * 2f;
                    f.stageOrbs.Add(StageTexture.Orb, at, new Vector2(size), 0f, 1f, DanmakusuBalance.BorderColour(body));
                    f.stageOrbs.Add(StageTexture.Orb, at, new Vector2(size * MOKOU_BODY_SCALE), 0f, 1f, body);
                }
            }
        }
    }
}
