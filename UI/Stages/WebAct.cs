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
        // Yamame Kurodani (2026-11-20): no pauses. Every sixteen beats a giant spider web of beams, standing still where she was when its preview began (turned so that the player is between two threads):
        // twenty-five threads out from its middle, as long as YAMAME_SIZE of the playfield's width and as thick as a slider's beam, and between every two of them arcs, rung after rung like a ladder, every
        // second gap's half a step further out, each bowing a little in towards the middle, thinner than the threads. A see-through, harmless preview of it grows over eight beats; then the web grows over eight as
        // Mai's snowflake does (a front runs out from the middle along every thread at one speed, and an arc grows from its thread when the front gets to it), and over the next eight a second front takes it away
        // the same way. All the while it sways slowly round its middle, its rim a little behind it. It hurts like a slider body.
        // Every second bullet (of a ring, by its number) that flies into a thread of it is caught there: it stops, hangs where it was caught and sways with the web, and flies on the way it was flying, at its
        // speed, when the front that takes the web away gets to it. (Both are worked out when it is caught: it is a function of time.)
        // ======================================================================================================================================================

        private sealed class WebAct : StageAct
        {
            private readonly double beat;
            private readonly double[] starts; // (when every web's preview begins)
            private readonly bool[] placed;
            private readonly Vector2[] centres;
            private readonly float[] bases; // (the angle of each web's first thread)
            private readonly Dictionary<BulletEmitter, int> ringOrder = new Dictionary<BulletEmitter, int>();

            private readonly int spokes = DanmakusuBalance.YAMAME_SPOKES;
            private readonly float sector = MathF.Tau / DanmakusuBalance.YAMAME_SPOKES;

            private double previewMs => beat * DanmakusuBalance.YAMAME_PREVIEW_BEATS;
            private double growMs => beat * DanmakusuBalance.YAMAME_GROW_BEATS;
            private double outMs => beat * DanmakusuBalance.YAMAME_OUT_BEATS;
            private double solidFrom(int k) => starts[k] + previewMs;
            private double goesFrom(int k) => starts[k] + previewMs + growMs;
            private double goneAt(int k) => starts[k] + previewMs + growMs + outMs;

            private readonly float omega; // (of the sway, radians per ms)
            private readonly float swayAmp = DanmakusuBalance.YAMAME_SWAY_DEGREES * MathF.PI / 180f;

            private readonly List<Vector2> line = new List<Vector2>();
            private readonly List<float> widths = new List<float>();

            public WebAct(BulletField f, BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                omega = (float)(Math.Tau / (beat * DanmakusuBalance.YAMAME_SWAY_BEATS));

                var list = new List<double>();

                // (one begins as long as it can grow whole in her section, its preview first; the last one goes away after it)
                for (double t = period.ShootStart; t + previewMs + growMs <= period.End && list.Count < 10000; t += beat * DanmakusuBalance.YAMAME_EVERY_BEATS)
                    list.Add(t);

                if (list.Count == 0)
                    list.Add(period.ShootStart);

                starts = list.ToArray();
                placed = new bool[starts.Length];
                centres = new Vector2[starts.Length];
                bases = new float[starts.Length];

                for (int i = 0; i < f.emitters.Length; i++)
                    ringOrder[f.emitters[i]] = i;

                Start = starts[0];
                End = goneAt(starts.Length - 1);
                AbsorbFrom = solidFrom(0);
                AbsorbTo = End;
            }

            /// <summary>She stays until her last web is gone.</summary>
            public override double MechanicsEnd => End;

            private float size(BulletField f) => (f.stageMax.X - f.stageMin.X) * DanmakusuBalance.YAMAME_SIZE;

            private float step(BulletField f) => Math.Max(f.circleRadius * DanmakusuBalance.YAMAME_RUNG_CIRCLES, 6f);

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.LASER_SIZE_SCALE * DanmakusuBalance.VolumeSizeScale(100);

            /// <summary>How far from the middle the deepest thing of the web is, along the web (the rim and the last arc along it): what the fronts run over.</summary>
            private float deepest(BulletField f) => size(f) * (1f + sector);

            /// <summary>How far round the web is swayed at <paramref name="t"/>, <paramref name="r"/> px from its middle (the rim a little behind the middle).</summary>
            private float swayAt(int k, float r, float webSize, double t) => swayAngle(swayAmp, omega, swayPhase(k, r, webSize), t);

            private float swayPhase(int k, float r, float webSize) => (float)(-(omega * starts[k] % Math.Tau)) - DanmakusuBalance.YAMAME_SWAY_LAG * r / Math.Max(webSize, 1f);

            /// <summary>The webs that are there at <paramref name="t"/> (their preview or more), at most two: the one going and the next one's preview.</summary>
            private int firstAt(double t)
            {
                double every = beat * DanmakusuBalance.YAMAME_EVERY_BEATS;
                int k = (int)Math.Floor((t - starts[0]) / every);
                return Math.Clamp(k - 2, 0, starts.Length - 1);
            }

            private double stunReady = double.NegativeInfinity, resetTime = double.NegativeInfinity;
            private int sounded = -1; // (the last web whose growing was heard)

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                stunReady = double.NegativeInfinity;
                resetTime = time;
                sounded = -1;

                // (a web placed before that moment stays where it was; the later ones are placed again when their time comes)
                for (int k = 0; k < starts.Length; k++)
                {
                    if (starts[k] > time)
                        placed[k] = false;
                }
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                // the web stuns: the character that touches it is held there for two beats, swaying with the web, and it cannot be stunned again for four beats after that
                if (now >= stunReady && touchedWeb(f, f.characterPosition, now, out int web))
                {
                    float r = Vector2.Distance(f.characterPosition, centres[web]);
                    f.holdCharacterSwaying(now, beat * DanmakusuBalance.YAMAME_STUN_BEATS, centres[web], swayAmp, omega, swayPhase(web, r, size(f)));
                    stunReady = now + beat * (DanmakusuBalance.YAMAME_STUN_BEATS + DanmakusuBalance.YAMAME_STUN_COOLDOWN_BEATS);

                    if (now - resetTime > 250)
                        f.playStageSound("mokou_wall_hit");
                }

                // a web starts growing (after its preview) with Byakuren's beam sound (2026-11-20)
                for (int k = sounded + 1; k < starts.Length && solidFrom(k) <= now; k++)
                {
                    sounded = k;

                    if (now - solidFrom(k) < 250 && solidFrom(k) > resetTime)
                        f.playStageSound("touhou_strike");
                }

                for (int k = 0; k < starts.Length && starts[k] <= now; k++)
                {
                    if (placed[k])
                        continue;

                    // a new web: where she is, turned so that the player is between two of its threads
                    placed[k] = true;
                    centres[k] = f.bossPositionOf(Period);
                    Vector2 toPlayer = f.characterPosition - centres[k];
                    bases[k] = (toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f) + sector * 0.5f;
                }
            }

            // ---- the shape ----

            /// <summary>Where rung <paramref name="j"/> between thread <paramref name="i"/> and the next is from the middle (before it bows), or a negative number when there is none.</summary>
            private float rungRadius(BulletField f, int i, int j)
            {
                float webSize = size(f);
                float r = webSize * DanmakusuBalance.YAMAME_HUB + (j + (i % 2) * 0.5f) * step(f);
                return r <= webSize * 0.97f ? r : -1f;
            }

            private int rungsOf(BulletField f) => (int)Math.Ceiling(size(f) / step(f)) + 1;

            /// <summary>How far in a rung bows at <paramref name="s"/> (0 at one thread, 1 at the next): its distance from the middle there is r times this.</summary>
            private static float bow(float s) => 1f - DanmakusuBalance.YAMAME_SAG * MathF.Sin(MathF.PI * Math.Clamp(s, 0f, 1f));

            /// <summary>The fronts of web <paramref name="k"/> at <paramref name="t"/>: how far along the web it has grown, how far its preview has, and how far it has been taken away (px from the middle).</summary>
            private void fronts(BulletField f, int k, double t, out float grown, out float preview, out float gone)
            {
                float deep = deepest(f);
                preview = deep * (float)Math.Clamp((t - starts[k]) / previewMs, 0, 1);
                grown = deep * (float)Math.Clamp((t - solidFrom(k)) / growMs, 0, 1);
                gone = deep * (float)Math.Clamp((t - goesFrom(k)) / outMs, 0, 1);
            }

            /// <summary>A point of web <paramref name="k"/> at <paramref name="t"/>: <paramref name="r"/> px from its middle, <paramref name="angle"/> radians round from its first thread, swayed.</summary>
            private Vector2 pointOf(int k, float r, float angle, float webSize, double t)
            {
                float a = bases[k] + angle + swayAt(k, r, webSize, t);
                return centres[k] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }

            // ---- drawing ----

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed)
                    return;

                float half = halfWidth(f);
                var silk = new Color4(0.96f, 0.94f, 0.88f, 1f);
                var border = DanmakusuBalance.BorderColour(new Color4(0.62f, 0.55f, 0.42f, 1f));

                for (int k = firstAt(t); k < starts.Length && starts[k] <= t; k++)
                {
                    if (!placed[k] || t >= goneAt(k))
                        continue;

                    fronts(f, k, t, out float grown, out float preview, out float gone);

                    // (the preview: see-through and harmless, thinner, growing ahead of the web from where it has got to)
                    if (preview > grown + 0.5f)
                    {
                        int ghost = ++f.beamPieces;
                        int from = pointTotal;
                        drawWeb(f, k, t, Math.Max(grown, 0f), preview, 0f, half * 0.7f, silk, border, ghost, ref beams, ref pointTotal, true);
                        Array.Fill(f.laserFadePointBuffer, DanmakusuBalance.YAMAME_PREVIEW_ALPHA, from, pointTotal - from);
                    }

                    if (grown > gone + 0.5f)
                        drawWeb(f, k, t, gone, grown, gone, half, silk, border, ++f.beamPieces, ref beams, ref pointTotal, false);
                }
            }

            /// <summary>
            /// What there is of web <paramref name="k"/> between the fronts <paramref name="near"/> and <paramref name="far"/> (px along the web): every thread and every arc as a beam, with sharp ends where a
            /// front is (and a sharp tip at the rim). A preview starts from where the web has grown to (<paramref name="ghost"/>: it is not taken away).
            /// </summary>
            private void drawWeb(BulletField f, int k, double t, float near, float far, float gone, float half, Color4 colour, Color4 border, int piece, ref int beams, ref int pointTotal, bool ghost)
            {
                float webSize = size(f);
                float rungHalf = half * DanmakusuBalance.YAMAME_RUNG_WIDTH;
                float tip = half * DanmakusuBalance.MAI_TIP_WIDTHS;

                // the threads out from the middle
                for (int i = 0; i < spokes; i++)
                {
                    float a = Math.Clamp(near, 0f, webSize), b = Math.Clamp(far, 0f, webSize);

                    if (b - a < 0.5f)
                        continue;

                    line.Clear();
                    widths.Clear();
                    int n = Math.Max(2, (int)Math.Ceiling((b - a) / 40f) + 1);
                    float tipNear = a > 0f ? Math.Min(tip, (b - a) / 3f) : 0f, tipFar = Math.Min(tip, (b - a) / 3f);

                    for (int p = 0; p < n; p++)
                    {
                        float r = a + (b - a) * p / (n - 1);
                        line.Add(pointOf(k, r, i * sector, webSize, t));
                        widths.Add(half * Math.Min(tipNear > 0f ? Math.Clamp((r - a) / tipNear, 0f, 1f) : 1f, Math.Clamp((b - r) / tipFar, 0f, 1f)));
                    }

                    if (onScreen(f, half))
                        addBeam(f, half, colour, border, piece, ref beams, ref pointTotal);
                }

                // the arcs between them
                int rungs = rungsOf(f);

                for (int i = 0; i < spokes; i++)
                {
                    for (int j = 0; j < rungs; j++)
                    {
                        float r = rungRadius(f, i, j);

                        if (r < 0f)
                            break;

                        float length = r * sector;
                        float a = Math.Clamp(near - r, 0f, length), b = Math.Clamp(far - r, 0f, length);

                        if (b - a < 0.5f)
                            continue;

                        line.Clear();
                        widths.Clear();
                        float sa = a / length, sb = b / length;
                        int n = Math.Max(2, 1 + (int)Math.Ceiling(6f * (sb - sa)));
                        bool frontNear = a > 0f, frontFar = b < length;
                        float tipLength = Math.Min(tip * 0.6f, (b - a) / 3f);

                        for (int p = 0; p < n; p++)
                        {
                            float s = sa + (sb - sa) * p / (n - 1);
                            float along = s * length;
                            float rho = r * bow(s);
                            line.Add(pointOf(k, rho, (i + s) * sector, webSize, t));
                            float width = 1f;

                            if (frontNear)
                                width = Math.Min(width, Math.Clamp((along - a) / tipLength, 0f, 1f));

                            if (frontFar)
                                width = Math.Min(width, Math.Clamp((b - along) / tipLength, 0f, 1f));

                            widths.Add(rungHalf * width);
                        }

                        if (onScreen(f, rungHalf))
                            addBeam(f, rungHalf, colour, border, piece, ref beams, ref pointTotal);
                    }
                }
            }

            private void addBeam(BulletField f, float half, Color4 colour, Color4 border, int piece, ref int beams, ref int pointTotal)
            {
                int before = beams;
                f.addPlainBeam(line, half, colour, border, ref beams, ref pointTotal, widths);

                if (beams > before)
                    f.laserInstances[beams - 1].Piece = piece;
            }

            /// <summary>Whether something of <see cref="line"/> is on the screen.</summary>
            private bool onScreen(BulletField f, float half)
            {
                Vector2 min = f.stageMin - new Vector2(half + 4f), max = f.stageMax + new Vector2(half + 4f);

                foreach (var p in line)
                {
                    if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y)
                        return true;
                }

                return false;
            }

            // ---- where it is (the hit check and catching): in the web's own round coordinates ----

            /// <summary>Where <paramref name="p"/> is in web <paramref name="k"/> at <paramref name="t"/>: how far from its middle, and how far round from its first thread (0..2 pi), the sway taken off.</summary>
            private void polar(BulletField f, int k, Vector2 p, double t, out float r, out float angle)
            {
                Vector2 d = p - centres[k];
                r = d.Length;
                angle = MathF.Atan2(d.Y, d.X) - bases[k] - swayAt(k, r, size(f), t);
                angle %= MathF.Tau;

                if (angle < 0f)
                    angle += MathF.Tau;
            }

            /// <summary>Is the thread nearest to (<paramref name="r"/>, <paramref name="angle"/>) of web <paramref name="k"/> there, within <paramref name="reach"/> px? (Its number in <paramref name="spoke"/>.)</summary>
            private bool nearThread(BulletField f, float r, float angle, float near, float far, float reach, out int spoke)
            {
                spoke = (int)MathF.Round(angle / sector) % spokes;
                float off = angle - MathF.Round(angle / sector) * sector;
                float webSize = size(f);

                if (r < near - reach || r > Math.Min(far, webSize) + reach)
                    return false;

                return r * MathF.Abs(MathF.Sin(off)) <= reach;
            }

            /// <summary>Is a rung of web <paramref name="k"/> there within <paramref name="reach"/> px of (<paramref name="r"/>, <paramref name="angle"/>)? (Which one in <paramref name="rung"/>.)</summary>
            private bool nearRung(BulletField f, float r, float angle, float near, float far, float reach, out int rung)
            {
                rung = -1;
                int i = (int)(angle / sector) % spokes;
                float s = angle / sector - (int)(angle / sector);
                float bowed = bow(s);
                float hub = size(f) * DanmakusuBalance.YAMAME_HUB, gap = step(f);
                float j = (r / bowed - hub) / gap - (i % 2) * 0.5f;

                for (int c = (int)MathF.Floor(j); c <= (int)MathF.Floor(j) + 1; c++)
                {
                    if (c < 0)
                        continue;

                    float rr = rungRadius(f, i, c);

                    if (rr < 0f || MathF.Abs(r - rr * bowed) > reach)
                        continue;

                    // (there only where the fronts leave it: from its thread round to the next)
                    float along = s * rr * sector;

                    if (along > far - rr + reach || along < near - rr - reach)
                        continue;

                    rung = i * 1000 + c;
                    return true;
                }

                return false;
            }

            /// <summary>The web hurts like a slider body, and stuns as well (see <see cref="Update"/>; 2026-11-20, the user: "touching web stuns player for 2 beats. Cooldown 4 beats", "web still hits like a slider").</summary>
            public override bool Hurts(BulletField f, double now) => touching(f, f.characterPosition, now);

            private bool touching(BulletField f, Vector2 p, double now) => touchedWeb(f, p, now, out _);

            /// <summary>Does <paramref name="p"/> touch a web at <paramref name="now"/> (as wide as its beams are; the sharp ends taken as wide as the rest)? Which one in <paramref name="web"/>.</summary>
            private bool touchedWeb(BulletField f, Vector2 p, double now, out int web)
            {
                web = -1;

                if (f.failed)
                    return false;

                float half = halfWidth(f);
                float room = DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int k = firstAt(now); k < starts.Length && starts[k] <= now; k++)
                {
                    if (!placed[k] || now < solidFrom(k) || now >= goneAt(k))
                        continue;

                    fronts(f, k, now, out float grown, out _, out float gone);

                    if (grown <= gone)
                        continue;

                    polar(f, k, p, now, out float r, out float angle);

                    if (r > size(f) + half + room)
                        continue;

                    if (nearThread(f, r, angle, gone, grown, half * DanmakusuBalance.BULLET_HITBOX_SCALE + room, out _)
                        || nearRung(f, r, angle, gone, grown, half * DanmakusuBalance.YAMAME_RUNG_WIDTH * DanmakusuBalance.BULLET_HITBOX_SCALE + room, out _))
                    {
                        web = k;
                        return true;
                    }
                }

                return false;
            }

            // ---- catching ----

            public override bool Absorbs(double t)
            {
                for (int k = firstAt(t); k < starts.Length && starts[k] <= t; k++)
                {
                    if (t >= solidFrom(k) && t < goneAt(k))
                        return true;
                }

                return false;
            }

            public override bool AbsorbsBetween(double from, double to)
            {
                for (int k = firstAt(from); k < starts.Length && starts[k] <= to; k++)
                {
                    if (solidFrom(k) <= to && goneAt(k) >= from)
                        return true;
                }

                return false;
            }

            /// <summary>Every second bullet that flies into a thread or an arc of a web is caught there (see the top).</summary>
            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || emitter.Shape == BulletShape.Train || emitter.Sharp)
                    return false;

                if (((ringOrder.TryGetValue(emitter, out int order) ? order : 0) + bullet) % DanmakusuBalance.YAMAME_CATCH_EVERY != 0)
                    return false;

                for (int k = firstAt(t); k < starts.Length && starts[k] <= t; k++)
                {
                    if (!placed[k] || t < solidFrom(k) || t >= goneAt(k))
                        continue;

                    fronts(f, k, t, out float grown, out _, out float gone);

                    if (grown <= gone)
                        continue;

                    polar(f, k, from, t, out float r0, out float a0);
                    polar(f, k, to, t, out float r1, out float a1);

                    if (r1 > size(f))
                        continue;

                    // (into a thread: from one side of it to the other, where it is there; into an arc: past it, in the same gap)
                    int s0 = (int)(a0 / sector) % spokes, s1 = (int)(a1 / sector) % spokes;
                    bool crossed = s0 != s1 && r1 >= gone && r1 <= grown && r1 * sector > 2f;

                    if (!crossed && s0 == s1)
                        crossed = rungIndex(f, r0, a0) != rungIndex(f, r1, a1) && nearRung(f, r1, a1, gone, grown, step(f), out _);

                    if (!crossed)
                        continue;

                    return catchOne(f, k, t, from, to, emitter, bullet, r1);
                }

                return false;
            }

            /// <summary>Which gap between rungs <paramref name="r"/> is in, in its own gap between two threads (rungs bow, so it is counted along the bow).</summary>
            private int rungIndex(BulletField f, float r, float angle)
            {
                int i = (int)(angle / sector) % spokes;
                float s = angle / sector - (int)(angle / sector);
                float hub = size(f) * DanmakusuBalance.YAMAME_HUB;
                return (int)MathF.Floor((r / bow(s) - hub) / step(f) - (i % 2) * 0.5f);
            }

            /// <summary>The bullet hangs on the web where it was caught, swaying with it, until the front that takes the web away gets there; then it flies on the way it was flying, at its speed.</summary>
            private bool catchOne(BulletField f, int k, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, float r)
            {
                float webSize = size(f);
                double release = goesFrom(k) + Math.Clamp(r / deepest(f), 0f, 1f) * outMs;

                if (release <= t + 1)
                    return false; // (that part of the web is gone already)

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false;

                var stored = f.makeStored(emitter, t);
                float speed = seenSpeed(stored, emitter, from, to, ScanPrevious, t);
                Vector2 way = Vector2.Normalize(delta);
                float phase = swayPhase(k, r, webSize);

                var hanging = f.makeStageBullet(stored, emitter, bullet, t, swayed(to, centres[k], -swayAngle(swayAmp, omega, phase, t)), Vector2.Zero);
                hanging.SwayAmp = swayAmp;
                hanging.SwayOmega = omega;
                hanging.SwayPhase = phase;
                hanging.SwayCentre = centres[k];
                hanging.KeepFace = true; // (it hangs the way it was flying)
                hanging.Face = way;
                hanging.Untraced = true;
                hanging.Until = release;

                int index = Bullets.Count;
                AddBullet(hanging);

                if (Bullets.Count == index)
                    return false; // (too many: it flies through)

                Vector2 velocity = way * speed;
                var flying = f.makeStageBullet(stored, emitter, bullet, release, stageBulletPosition(hanging, release), velocity);
                flying.Face = way;
                flying.Until = release + f.stageLife(velocity);
                AddBullet(flying);
                return true;
            }
        }
    }
}
