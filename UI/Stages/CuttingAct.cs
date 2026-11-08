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
        // What Kagerou's and Elly's claws, Youmu's slashes and the tips of Ran's spirals have in common: they cut a bullet in two along a line, where the line runs through it but never more unevenly than 8 to 2, and the two cut shapes fly on from there: one the way the bullet was going, the other the opposite way.
        // A cut flashes where it is made, as a ricochet does. The pieces are polygons the act draws itself, cut exactly, on hidden stage bullets that hit as bullets do. Not what is a boss's own object (Yukari's train, Hecatia's worlds, Utsuho's big balls...: those are no bullets it can get hold of).
        // ======================================================================================================================================================

        private abstract class CuttingAct : StageAct
        {
            private readonly struct Piece
            {
                public readonly int Bullet;
                public readonly Vector2[] Outline;
                public readonly Color4 Colour;

                public Piece(int bullet, Vector2[] outline, Color4 colour)
                {
                    Bullet = bullet;
                    Outline = outline;
                    Colour = colour;
                }
            }

            private readonly List<Piece> pieces = new List<Piece>();
            private int piecesFrom;

            protected void ClearPieces()
            {
                ClearBullets();
                pieces.Clear();
                piecesFrom = 0;
            }

            /// <summary>Can this ring be cut at all?</summary>
            protected static bool CanBeCut(BulletField f, BulletEmitter emitter) => !f.failed && emitter.Shape != BulletShape.Train && !emitter.Deflector;

            /// <summary>Is the line <paramref name="off"/> from the middle of this bullet still through it?</summary>
            protected static float SizeOf(BulletEmitter emitter, int bullet) => emitter.VisualRadius * emitter.BigScale(bullet);

            /// <summary>A bullet of a ring that flew from <paramref name="from"/> to <paramref name="to"/> is cut along a line, which lies <paramref name="along"/> from its middle along <paramref name="normal"/>.</summary>
            protected void CutBullet(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet, Vector2 normal, float along)
            {
                var stored = f.makeStored(emitter, t);
                float big = emitter.BigScale(bullet);
                Vector2 velocity = (to - from) / (float)Math.Max(t - ScanPrevious, 1e-3);

                CutInTwo(f, t, to, velocity, normal, along, stored.Radius * big, stored.Hit * big, stored.Style, stored.Shape, stored.Tint);
            }

            /// <summary>The piece on the one side of the line and the piece on the other, the one flying on, the other flying back.</summary>
            protected void CutInTwo(BulletField f, double t, Vector2 position, Vector2 velocity, Vector2 normal, float along, float radius, float hit, int style, BulletShape shape, int tint)
            {
                Vector2 facing = velocity.LengthSquared > 1e-8f ? Vector2.Normalize(velocity) : normal;
                var whole = outlineOf(shape, radius, facing);

                // (a bullet that stood still still falls apart: the pieces go their ways from the cut)
                if (velocity.LengthSquared < 1e-8f)
                    velocity = normal * (stageBulletSpeed(Period) * 0.25f);

                float at = evenEnough(whole, normal, along);

                AddPiece(f, t, position, velocity, radius * DanmakusuBalance.CLAW_PIECE_HIT, hit * DanmakusuBalance.CLAW_PIECE_HIT, style, tint, clip(whole, normal, at));
                AddPiece(f, t, position, -velocity, radius * DanmakusuBalance.CLAW_PIECE_HIT, hit * DanmakusuBalance.CLAW_PIECE_HIT, style, tint, clip(whole, -normal, -at));

                // (and it flashes where it was cut)
                f.addFlash(t, position, radius * DanmakusuBalance.RICOCHET_FLASH_SIZE);
            }

            /// <summary>Where the cut lies: at <paramref name="wanted"/> along <paramref name="normal"/>, unless the smaller piece would then be less than <see cref="DanmakusuBalance.CLAW_CUT_SMALLEST"/> of the whole (8/2 at the most uneven): then just where it is that much.</summary>
            private static float evenEnough(Vector2[] outline, Vector2 normal, float wanted)
            {
                float whole = areaOf(outline);

                if (whole <= 1e-6f)
                    return 0f;

                float low = float.MaxValue, high = float.MinValue;

                foreach (Vector2 corner in outline)
                {
                    float there = Vector2.Dot(corner, normal);
                    low = Math.Min(low, there);
                    high = Math.Max(high, there);
                }

                float smallest = DanmakusuBalance.CLAW_CUT_SMALLEST;
                float at = Math.Clamp(wanted, low, high);
                float share = areaOf(clip(outline, normal, at)) / whole; // (what lies beyond the cut: it gets less as the cut goes further along)

                if (share >= smallest && share <= 1f - smallest)
                    return at;

                float target = share < smallest ? smallest : 1f - smallest;

                for (int step = 0; step < 16; step++)
                {
                    float middle = (low + high) * 0.5f;

                    if (areaOf(clip(outline, normal, middle)) / whole > target)
                        low = middle;
                    else
                        high = middle;
                }

                return (low + high) * 0.5f;
            }

            private static float areaOf(Vector2[] outline)
            {
                float sum = 0f;

                for (int i = 0; i < outline.Length; i++)
                {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                    sum += a.X * b.Y - b.X * a.Y;
                }

                return MathF.Abs(sum) * 0.5f;
            }

            protected void AddPiece(BulletField f, double t, Vector2 position, Vector2 velocity, float radius, float hit, int style, int tint, Vector2[] outline)
            {
                if (outline.Length < 3 || f.palette.Length == 0)
                    return;

                int before = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = position,
                    V = velocity,
                    Radius = radius,
                    Hit = hit,
                    Style = style,
                    Shape = BulletShape.Circle,
                    Hidden = true,
                    Until = t + f.stageLife(velocity),
                });

                if (Bullets.Count > before)
                    pieces.Add(new Piece(before, outline, tint > 0 ? BulletRenderer.SpecialBodies[tint - 1] : f.palette[style % f.palette.Length]));
            }

            /// <summary>The outline of a bullet of this shape, facing the way it flies.</summary>
            private static Vector2[] outlineOf(BulletShape shape, float r, Vector2 facing)
            {
                Vector2 across = new Vector2(-facing.Y, facing.X);

                Vector2 at(float along, float side) => facing * along + across * side;

                switch (shape)
                {
                    case BulletShape.Star:
                    case BulletShape.SoftStar:
                    {
                        var star = new Vector2[10];

                        for (int i = 0; i < star.Length; i++)
                        {
                            float angle = i * MathF.Tau / star.Length;
                            float reach = i % 2 == 0 ? r * 1.25f : r * 0.5f;
                            star[i] = at(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach);
                        }

                        return star;
                    }

                    case BulletShape.Triangle:
                        return new[] { at(r * 1.4f, 0f), at(-r, r), at(-r * 0.4f, 0f), at(-r, -r) };

                    case BulletShape.Knife:
                        return new[] { at(r * 2f, 0f), at(r * 0.3f, r * 0.35f), at(-r * 1.2f, r * 0.3f), at(-r * 1.2f, -r * 0.3f), at(r * 0.3f, -r * 0.35f) };

                    case BulletShape.Talisman:
                        return new[] { at(r * 1.3f, r * 0.75f), at(-r * 1.3f, r * 0.75f), at(-r * 1.3f, -r * 0.75f), at(r * 1.3f, -r * 0.75f) };

                    case BulletShape.Ellipse:
                    case BulletShape.Petal:
                    case BulletShape.Slice:
                        return roundOutline(r * 1.35f, r * 0.75f, at);

                    default:
                        return roundOutline(r, r, at);
                }
            }

            private static Vector2[] roundOutline(float along, float side, Func<float, float, Vector2> at)
            {
                var round = new Vector2[14];

                for (int i = 0; i < round.Length; i++)
                {
                    float angle = i * MathF.Tau / round.Length;
                    round[i] = at(MathF.Cos(angle) * along, MathF.Sin(angle) * side);
                }

                return round;
            }

            /// <summary>What of the outline lies on the side <paramref name="normal"/> points to, of a line <paramref name="at"/> along it from its middle: exactly the cut shape.</summary>
            private static Vector2[] clip(Vector2[] outline, Vector2 normal, float at)
            {
                var kept = new List<Vector2>(outline.Length + 2);

                for (int i = 0; i < outline.Length; i++)
                {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                    float da = Vector2.Dot(a, normal) - at, db = Vector2.Dot(b, normal) - at;

                    if (da >= 0f)
                        kept.Add(a);

                    if ((da >= 0f) != (db >= 0f))
                        kept.Add(a + (b - a) * (da / (da - db)));
                }

                return kept.ToArray();
            }

            protected void DrawPieces(BulletField f, double t)
            {
                while (piecesFrom < pieces.Count && Bullets[pieces[piecesFrom].Bullet].Until < t)
                    piecesFrom++;

                // (every piece that is on the screen is drawn. There was a limit of the 400 newest, counted with the pieces that had already flown off the screen: an older piece still on it vanished
                // when many were cut, and came back out of nowhere when newer, faster ones ran out of time)
                for (int i = piecesFrom; i < pieces.Count; i++)
                {
                    var piece = pieces[i];
                    var bullet = Bullets[piece.Bullet];

                    if (t < bullet.T0 || t >= bullet.Until)
                        continue;

                    if (DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Count > 0 && f.stageConsumed.Contains(Id * 65536 + piece.Bullet))
                        continue;

                    // (on its own clock, as the field moves the hidden bullet that hits: another boss's stop or slowdown holds the piece where it hurts)
                    Vector2 at = stageBulletPosition(bullet, f.stageClock(bullet, t));

                    // (its outline reaches up to twice the cut bullet's radius from its middle, a knife's tip)
                    if (f.hasScreenBounds && !f.onTheScreen(at, 2f * bullet.Radius / DanmakusuBalance.CLAW_PIECE_HIT))
                        continue;

                    Vector2 middle = Vector2.Zero;

                    foreach (Vector2 corner in piece.Outline)
                        middle += corner;

                    middle = at + middle / piece.Outline.Length;

                    for (int k = 0; k < piece.Outline.Length; k++)
                    {
                        Vector2 a = at + piece.Outline[k], b = at + piece.Outline[(k + 1) % piece.Outline.Length];
                        f.stageMeshes.Add(new MeshQuad { A = middle, B = middle, C = a, D = b, Near = Color4.White, Far = piece.Colour });
                    }
                }
            }
        }
    }
}
