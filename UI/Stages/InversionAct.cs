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
        // Seija Kijin: the screen turns upside down when her attacks start and turns back when they end, and for a moment the player has lost themselves: after every turn a big smooth red circle shows where they are, closing in on
        // them; after the turn upside down it stays round them until the screen turns back (then it fades out in half a beat), after the turn back it fades out over two beats. While the screen is upside down a big see-through red
        // glow lies round the player.
        // Before every turn she warns of it, for four beats: a see-through clone of her, with her magic circle and the stars of a warning flying in, fades in where she will be seen once the screen has turned (her
        // place mirrored through the middle of the screen), red arrows of her dress round the player point at her and as many in a circle round her point in at her, all flashing faster and faster (so the player knows where to
        // look). The clone is gone as the screen turns. And the whole screen fills with big see-through arrows,
        // the ones on her dress, in a grid: red ones flowing sideways from the side the player is nearer to the other, blue ones from the top or the bottom, whichever is nearer, to the other (as the warning starts: where the
        // turn will throw the player), fading in over a beat and out by the turn.
        // ======================================================================================================================================================

        private sealed class InversionAct : StageAct
        {
            private readonly List<double> turns = new List<double>(); // (the start of an attack, then its end, and so on: the even ones turn the screen upside down)
            private readonly double span;
            private readonly double warn;
            private readonly double beat;

            // (which way the arrows of the warning before each turn flow: chosen as it starts, by where the player is then)
            private readonly Dictionary<double, (float X, float Y)> flows = new Dictionary<double, (float, float)>();

            public InversionAct(BossPeriod period, List<(double Start, double End)> attacks)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                span = beat * DanmakusuBalance.SEIJA_CIRCLE_BEATS;
                warn = beat * DanmakusuBalance.SEIJA_WARN_BEATS;

                foreach (var (start, end) in attacks)
                {
                    if (end <= start)
                        continue;

                    turns.Add(start);
                    turns.Add(end);
                }

                Start = period.ShootStart;
                End = period.End + span;
            }

            /// <summary>How far into the warning before a turn <paramref name="t"/> is (0..1), and which turn; -1 when it is in none.</summary>
            private float warningAt(double t, out double turn)
            {
                turn = 0;

                foreach (double at in turns)
                {
                    if (t >= at - warn && t < at)
                    {
                        turn = at;
                        return (float)((t - (at - warn)) / warn);
                    }
                }

                return -1f;
            }

            public override void Reset(BulletField f, double time)
            {
                flows.Clear();
                base.Reset(f, time);
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                float u = warningAt(now, out double turn);

                if (u < 0f)
                    return;

                // (the clone: where she will be seen when the screen has turned round its middle)
                f.seijaCloneLevel = Math.Max(f.seijaCloneLevel, smoothStep(u / 0.3f));
                f.seijaCloneAt = 2f * f.MirrorCentre - f.bossVisualOf(Period);
                f.seijaCloneSlot = Period.BossSlot;
                f.seijaCloneTurn = turn;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                var red = new Color4(1f, 0.12f, 0.15f, 1f);

                // (while the screen is upside down a big see-through red glow lies round the player, coming and going as the screen turns)
                float inverted = f.MirrorAngleAt(t) / 180f;

                if (inverted > 0.002f && t >= Start && t < End)
                    f.stageBack.Add(StageTexture.Glow, f.characterPosition, new Vector2(f.circleRadius * DanmakusuBalance.SEIJA_GLOW_RADII * 2f), 0f, DanmakusuBalance.SEIJA_GLOW_ALPHA * inverted, red, true);

                for (int i = 0; i < turns.Count; i++)
                {
                    double turn = turns[i];
                    double age = t - turn;

                    // (after the turn upside down the circle stays round the player until the screen turns back, then fades out quickly; after the turn back it fades out over its two beats)
                    bool stays = i % 2 == 0 && i + 1 < turns.Count;
                    double gone = beat * DanmakusuBalance.SEIJA_CIRCLE_GONE_BEATS;

                    if (age < 0 || (stays ? t >= turns[i + 1] + gone : age >= span))
                        continue;

                    float u = (float)(age / span);
                    float radius = f.circleRadius * (DanmakusuBalance.SEIJA_CIRCLE_RADII - (DanmakusuBalance.SEIJA_CIRCLE_RADII - 1.5f) * smoothStep(u * 2f));
                    float level = stays ? 1f - smoothStep((t - turns[i + 1]) / gone) : 1f - smoothStep((u - 0.4f) / 0.6f);

                    f.stageTree.Add(StageTexture.Glow, f.characterPosition, new Vector2(radius * 3f), 0f, 0.55f * level, red, true);
                    f.stageRipple.AddRing(f.characterPosition, radius, Math.Max(radius * 0.09f, 3f), 0.85f * level, red);
                }

                float w = warningAt(t, out double warned);

                if (w >= 0f)
                    arrowGrid(f, t, warned);

                // (before a turn: red arrows of her dress evenly round the player, every one pointing at her, and as many in a circle round her pointing in at her, all flashing faster and faster: the player knows where to look)
                if (w >= 0f)
                {
                    Vector2 player = f.characterPosition;
                    Vector2 her = f.bossVisualOf(Period);
                    Vector2 toHer = her - player;
                    float aim = toHer.LengthSquared > 1f ? MathF.Atan2(toHer.Y, toHer.X) : -MathF.PI / 2f;
                    float flashes = DanmakusuBalance.SEIJA_ARROW_FLASHES * w * w; // (faster and faster: the count grows with the square of the time)
                    float flash = 0.55f + 0.45f * MathF.Cos(flashes * MathF.Tau);
                    float alpha = DanmakusuBalance.SEIJA_ARROW_ALPHA * smoothStep(w / 0.2f) * flash;
                    float distance = f.circleRadius * DanmakusuBalance.SEIJA_ARROW_RADII;
                    float size = f.circleRadius * DanmakusuBalance.SEIJA_ARROW_SIZE;

                    for (int k = 0; k < DanmakusuBalance.SEIJA_ARROWS; k++)
                    {
                        float round = aim + k * MathF.Tau / DanmakusuBalance.SEIJA_ARROWS;
                        Vector2 at = player + new Vector2(MathF.Cos(round), MathF.Sin(round)) * distance;

                        f.stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), aim * 180f / MathF.PI, alpha, red);
                    }

                    // (round her, just outside her magic circle, each pointing in at her; the first one on the side of the player)
                    float reach = DanmakusuBalance.MAGIC_CIRCLE_RADIUS + f.circleRadius * DanmakusuBalance.SEIJA_HER_ARROW_GAP_RADII + size * 0.5f;

                    for (int k = 0; k < DanmakusuBalance.SEIJA_HER_ARROWS; k++)
                    {
                        float round = aim + MathF.PI + k * MathF.Tau / DanmakusuBalance.SEIJA_HER_ARROWS;
                        Vector2 at = her + new Vector2(MathF.Cos(round), MathF.Sin(round)) * reach;

                        f.stageRipple.Add(StageTexture.SeijaArrow, at, new Vector2(size), round * 180f / MathF.PI + 180f, alpha, red);
                    }
                }
            }

            /// <summary>
            /// The arrows of the warning before the turn at <paramref name="turn"/>: a grid of them over the whole screen, red ones in the middles of the cells flowing sideways, blue ones at their corners flowing up or down, each from
            /// the side (the end) of the screen the player was nearer to as the warning started, to the other: where the turn will throw them. They fade in over a beat and out by the turn.
            /// </summary>
            private void arrowGrid(BulletField f, double t, double turn)
            {
                // (which way: chosen once, as the warning starts; the screen turns round its middle, so the side the player is nearer to is the one they leave)
                if (!flows.TryGetValue(turn, out var flow))
                {
                    Vector2 middle = f.MirrorCentre;
                    flows[turn] = flow = (f.characterPosition.X <= middle.X ? 1f : -1f, f.characterPosition.Y <= middle.Y ? 1f : -1f);
                }

                double into = t - (turn - warn);
                float alpha = DanmakusuBalance.SEIJA_GRID_ALPHA * smoothStep(into / (beat * DanmakusuBalance.SEIJA_GRID_FADE_IN_BEATS)) * smoothStep((turn - t) / (beat * DanmakusuBalance.SEIJA_GRID_FADE_OUT_BEATS));

                if (alpha <= 0.002f)
                    return;

                Vector2 min = f.hasScreenBounds ? f.screenMin : f.stageMin;
                Vector2 max = f.hasScreenBounds ? f.screenMax : f.stageMax;
                float cell = (max.Y - min.Y) / DanmakusuBalance.SEIJA_GRID_ROWS;

                if (cell < 1f)
                    return;

                int columns = (int)MathF.Ceiling((max.X - min.X) / cell);
                float size = cell * DanmakusuBalance.SEIJA_GRID_ARROW_SIZE;

                // (the grid flows on and every arrow is replaced by the one behind it as it moves a whole cell: it looks as if they all flowed on)
                float moved = (float)(into / beat) * DanmakusuBalance.SEIJA_GRID_CELLS_PER_BEAT;
                float shift = (moved - MathF.Floor(moved)) * cell;
                var red = new Color4(0.93f, 0.12f, 0.18f, 1f);
                var blue = new Color4(0.16f, 0.36f, 0.95f, 1f);

                for (int row = -1; row <= DanmakusuBalance.SEIJA_GRID_ROWS; row++)
                {
                    for (int column = -1; column <= columns; column++)
                    {
                        Vector2 corner = min + new Vector2(column * cell, row * cell);

                        f.stageBack.Add(StageTexture.SeijaArrow, corner + new Vector2(cell * 0.5f + shift * flow.X, cell * 0.5f), new Vector2(size), flow.X > 0f ? 0f : 180f, alpha, red);
                        f.stageBack.Add(StageTexture.SeijaArrow, corner + new Vector2(0f, shift * flow.Y), new Vector2(size), flow.Y > 0f ? 90f : -90f, alpha, blue);
                    }
                }
            }
        }
    }
}
