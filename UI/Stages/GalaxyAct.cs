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
        // Megumu Iizunamaru: no pauses. Every half beat a set of five straight beams, evenly round her, endless (past any edge of the screen) and whole at once: see-through and harmless for four beats (the preview),
        // then solid for four, hurting like a slider body, then gone. Each set is turned five degrees further than the one before, the same way round for 32 beats and then the other way, and the first set of her
        // section is aimed at the player as its preview comes. A set stays where she was when its preview came (it does not move with her: the user, 2026-11-13, "her laserbeams are static"); each beam starts
        // MEGUMU_SHOOT_CIRCLES circle radii from there on its own way out and is bent MEGUMU_BEND_DEGREES off that way, all the same way round, like the arms of a galaxy. The five starts of a set are joined by
        // beams into a five-pointed star (each start to the one two further round; 2026-11-14), see-through and solid with the set. Every set is in a colour of the rainbow, round the whole of it in
        // MEGUMU_RAINBOW_BEATS, and a solid set fades out over its last half beat (both 2026-11-14). Every eight beats every bullet on the
        // screen turns into a star like Marisa's and ricochets once (BossOps).
        // ======================================================================================================================================================

        private sealed class GalaxyAct : StageAct
        {
            private const float reach = 4000f; // (px: past any screen)

            private readonly double beat;
            private readonly double[] sets; // (when the preview of every set comes; it is solid four beats later)
            private readonly float[] turns; // (how far every set is turned from the first, radians)
            private readonly List<Vector2> line = new List<Vector2>();
            private bool aimed;
            private float aim;

            // (where she was when the preview of every set came: the set stays there)
            private readonly Vector2[] anchors;
            private readonly bool[] anchored;
            private int anchoredUpTo; // (every set before this one has its place, or is gone)
            private readonly Vector2[] starts, ways;

            public GalaxyAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;

                double gap = beat * DanmakusuBalance.MEGUMU_BEAM_EVERY_BEATS;
                double preview = beat * DanmakusuBalance.MEGUMU_PREVIEW_BEATS;
                var times = new List<double>();
                var angles = new List<float>();
                float turned = 0f;
                float step = DanmakusuBalance.MEGUMU_STEP_DEGREES * MathF.PI / 180f;

                // (sets come as long as they turn solid in her section)
                for (double at = period.ShootStart; at + preview < period.End && times.Count < 20000; at += gap)
                {
                    times.Add(at);
                    angles.Add(turned);

                    // (the same way round for 32 beats, then the other way)
                    int way = (int)Math.Floor((at - period.ShootStart) / (beat * DanmakusuBalance.MEGUMU_TURN_BEATS)) % 2 == 0 ? 1 : -1;
                    turned += step * way;
                }

                sets = times.ToArray();
                turns = angles.ToArray();
                anchors = new Vector2[sets.Length];
                anchored = new bool[sets.Length];
                starts = new Vector2[sets.Length * DanmakusuBalance.MEGUMU_BEAMS];
                ways = new Vector2[sets.Length * DanmakusuBalance.MEGUMU_BEAMS];
                End = sets.Length > 0 ? sets[^1] + beat * (DanmakusuBalance.MEGUMU_PREVIEW_BEATS + DanmakusuBalance.MEGUMU_LIVE_BEATS) : period.End;
            }

            /// <summary>She stays until her last beams are gone.</summary>
            public override double MechanicsEnd => End;

            private double solidAt(int set) => sets[set] + beat * DanmakusuBalance.MEGUMU_PREVIEW_BEATS;

            private double goneAt(int set) => solidAt(set) + beat * DanmakusuBalance.MEGUMU_LIVE_BEATS;

            private float halfWidth(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.MEGUMU_BEAM_WIDTH;

            public override void Reset(BulletField f, double time)
            {
                if (time < Start)
                    aimed = false;

                // (a set whose preview has not come yet at that time takes its place again when it comes)
                for (int set = 0; set < sets.Length; set++)
                {
                    if (sets[set] > time)
                        anchored[set] = false;
                }

                anchoredUpTo = 0;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                if (!aimed)
                {
                    // (the first set is aimed at the player as its preview comes; the others follow from it)
                    Vector2 toPlayer = f.characterPosition - f.bossVisualOf(Period);
                    aim = toPlayer.LengthSquared > 1f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : MathF.PI / 2f;
                    aimed = true;
                }

                // (every set whose preview has come takes where she is then, once; one passed over by a jump forward is gone before it is seen)
                var (first, last) = shown(now);
                anchoredUpTo = Math.Max(anchoredUpTo, Math.Max(first, 0));

                for (int set = anchoredUpTo; set <= last; set++)
                {
                    if (anchored[set] || sets[set] > now)
                        continue;

                    anchors[set] = f.bossVisualOf(Period);
                    anchored[set] = true;

                    // (its beams, worked out once: where each starts and which way it goes from there, bent off its way out)
                    float bend = DanmakusuBalance.MEGUMU_BEND_DEGREES * MathF.PI / 180f;
                    float cos = MathF.Cos(bend), sin = MathF.Sin(bend);

                    for (int beam = 0; beam < DanmakusuBalance.MEGUMU_BEAMS; beam++)
                    {
                        Vector2 outward = wayOf(set, beam);
                        starts[set * DanmakusuBalance.MEGUMU_BEAMS + beam] = anchors[set] + outward * (f.circleRadius * DanmakusuBalance.MEGUMU_SHOOT_CIRCLES);
                        ways[set * DanmakusuBalance.MEGUMU_BEAMS + beam] = new Vector2(outward.X * cos - outward.Y * sin, outward.X * sin + outward.Y * cos);
                    }
                }
            }

            /// <summary>Where a beam of a set starts, and which way it goes from there (bent off its way out).</summary>
            private void beamOf(BulletField f, int set, int beam, out Vector2 start, out Vector2 way)
            {
                start = starts[set * DanmakusuBalance.MEGUMU_BEAMS + beam];
                way = ways[set * DanmakusuBalance.MEGUMU_BEAMS + beam];
            }

            /// <summary>The sets that can be seen at <paramref name="t"/>: from the first whose beams are not gone yet to the last whose preview has come.</summary>
            private (int First, int Last) shown(double t)
            {
                double gap = beat * DanmakusuBalance.MEGUMU_BEAM_EVERY_BEATS;
                int last = Math.Min(sets.Length - 1, (int)Math.Floor((t - Start) / gap));
                int first = Math.Max(0, (int)Math.Floor((t - Start - beat * (DanmakusuBalance.MEGUMU_PREVIEW_BEATS + DanmakusuBalance.MEGUMU_LIVE_BEATS)) / gap));

                return (first, last);
            }

            private Vector2 wayOf(int set, int beam)
            {
                float angle = aim + turns[set] + beam * MathF.Tau / DanmakusuBalance.MEGUMU_BEAMS;
                return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            public override void DrawBeams(BulletField f, double t, ref int beams, ref int pointTotal)
            {
                if (f.failed || !aimed || f.palette.Length == 0 || sets.Length == 0 || t < Start || t > End)
                    return;

                float half = halfWidth(f);
                var (first, last) = shown(t);

                for (int set = first; set <= last; set++)
                {
                    if (t < sets[set] || t >= goneAt(set) || !anchored[set])
                        continue;

                    bool solid = t >= solidAt(set);

                    // (every set in a colour of the rainbow, going round the whole of it as the sets come: 2026-11-14)
                    Color4 colour = rainbow(sets[set]);
                    Color4 border = DanmakusuBalance.BorderColour(colour);

                    // (the preview fades in; a solid set flashes in at once and fades out over its last half beat - 2026-11-14, it went out in a tenth of a beat)
                    float alpha = solid
                        ? 1f - smoothStep((t - (goneAt(set) - fadeMs)) / fadeMs)
                        : DanmakusuBalance.MEGUMU_PREVIEW_ALPHA * smoothStep((t - sets[set]) / (beat * 0.25));

                    if (alpha < 0.01f)
                        continue;

                    for (int beam = 0; beam < DanmakusuBalance.MEGUMU_BEAMS; beam++)
                    {
                        beamOf(f, set, beam, out Vector2 start, out Vector2 way);
                        line.Clear();
                        line.Add(start);
                        line.Add(start + way * reach);
                        f.addPlainBeam(line, solid ? half : half * 0.6f, colour, border, ref beams, ref pointTotal);

                        if (alpha < 0.999f)
                            Array.Fill(f.laserFadePointBuffer, alpha, pointTotal - 2, 2);

                        // (and the star between the starts: every start joined to the one two further round)
                        line.Clear();
                        line.Add(start);
                        line.Add(starOf(set, beam));
                        f.addPlainBeam(line, solid ? half : half * 0.6f, colour, border, ref beams, ref pointTotal);

                        if (alpha < 0.999f)
                            Array.Fill(f.laserFadePointBuffer, alpha, pointTotal - 2, 2);
                    }
                }
            }

            private double fadeMs => beat * DanmakusuBalance.MEGUMU_FADE_BEATS;

            /// <summary>The colour of a set whose preview came at <paramref name="time"/>: round the whole rainbow once in MEGUMU_RAINBOW_BEATS.</summary>
            private Color4 rainbow(double time)
            {
                float hue = (float)(((time - Start) / (beat * DanmakusuBalance.MEGUMU_RAINBOW_BEATS)) % 1.0);
                float h = (hue < 0 ? hue + 1f : hue) * 6f;
                float x = 1f - MathF.Abs(h % 2f - 1f);
                var (r, g, b) = (int)h switch
                {
                    0 => (1f, x, 0f),
                    1 => (x, 1f, 0f),
                    2 => (0f, 1f, x),
                    3 => (0f, x, 1f),
                    4 => (x, 0f, 1f),
                    _ => (1f, 0f, x),
                };

                // (bright, not quite full: a little white in it)
                const float white = 0.25f;
                return new Color4(white + (1f - white) * r, white + (1f - white) * g, white + (1f - white) * b, 1f);
            }

            /// <summary>A solid beam hurts like a slider body (until it is half faded).</summary>
            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed || !aimed || sets.Length == 0 || now < Start || now > End)
                    return false;

                Vector2 player = f.characterPosition;
                float hit = halfWidth(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;
                var (first, last) = shown(now);

                for (int set = first; set <= last; set++)
                {
                    if (now < solidAt(set) || now >= goneAt(set) - fadeMs * 0.5 || !anchored[set])
                        continue;

                    for (int beam = 0; beam < DanmakusuBalance.MEGUMU_BEAMS; beam++)
                    {
                        beamOf(f, set, beam, out Vector2 start, out Vector2 way);

                        if (distanceToSegment(player, start, start + way * reach) <= hit)
                            return true;

                        // (the star between the beams' starts)
                        if (distanceToSegment(player, start, starOf(set, beam)) <= hit)
                            return true;
                    }
                }

                return false;
            }

            /// <summary>The other end of a line of the star a set's beams start on: every start joined to the one two further round (a pentagram; 2026-11-14).</summary>
            private Vector2 starOf(int set, int beam) => starts[set * DanmakusuBalance.MEGUMU_BEAMS + (beam + 2) % DanmakusuBalance.MEGUMU_BEAMS];
        }
    }
}
