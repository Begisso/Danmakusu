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
        // Utsuho: she stands in the middle of the screen the whole time and the screen shakes. Before every pause everything slows down to a stop (see BossEffects) while the black hole's music starts; in the pause a
        // black hole appears in the middle, hides her, bends the light and pulls in every bullet (but her big balls and Yuyuko's petals) at half its speed. What it takes in comes out through the whole of its time.
        // A bullet that touches it shrinks to nothing in a moment as it flies on, and is gone. Everything it takes in is shot out again split in two, one out of each of its two narrow rays (the jets);
        // its cone of light appears with the warning, aimed at the player then, and stands still until the hole shoots; when the first bullet comes out the way round they turn is chosen (towards the player,
        // right then) and the rays turn that way at an eighth of the speed of a spinner's beams, 90 degrees at most.
        // ======================================================================================================================================================

        private sealed class HoleAct : StageAct
        {
            private bool known;

            /// <summary>The way the cone of light (and the jets) point from the warning on: at where the player was when it appeared, still until the hole shoots.</summary>
            private float coneAngle;

            /// <summary>Which way round the jets turn (1 or -1), chosen when the hole shoots its first bullet out: the way that is nearer to the player then.</summary>
            private float turnWay = 1f;

            /// <summary>The cone of light appears this long before the hole does (it is the warning) and stands still until the hole shoots.</summary>
            private static double warning => DanmakusuBalance.POWER_UP_LENGTH_MS;

            public override void Init(BulletField f)
            {
                AbsorbFrom = Start + DanmakusuBalance.UTSUHO_PULL_DELAY_MS;
                AbsorbTo = End;
            }

            /// <summary>When the black hole's music starts: when everything starts to slow down before it (see BossEffects).</summary>
            public double SoundFrom => BossOps.UtsuhoStopFrom(Period, Start);

            private static float holeRadius(BulletField f) => DanmakusuBalance.UTSUHO_HOLE_RADIUS * f.circleRadius;

            public float Presence(double t)
                => t < Start || t > End + DanmakusuBalance.STAGE_FADE_OUT_MS ? 0f : smoothStep((t - Start) / DanmakusuBalance.UTSUHO_HOLE_FADE_MS) * smoothStep((End + DanmakusuBalance.STAGE_FADE_OUT_MS - t) / DanmakusuBalance.STAGE_FADE_OUT_MS);

            /// <summary>When the first bullet came out of the hole (NaN: none yet): the way round the jets turn was chosen right then, and they turn from then on.</summary>
            private double chosenAt = double.NaN;

            /// <summary>What it has taken in and has not shot out yet: each comes out at its own moment, split in two, and the way it goes is only worked out then (see <see cref="emit"/>).</summary>
            private readonly List<(StoredBullet Taken, double T0, int First)> waiting = new List<(StoredBullet, double, int)>();

            /// <summary>
            /// The direction of the jets (and of the cone) at <paramref name="t"/>: still, where the cone appeared, until the hole shoots; from its first bullet on turning the way chosen then at an eighth of a
            /// spinner beam's speed (never more than 90 degrees in all).
            /// </summary>
            private float axisAt(double t)
            {
                if (double.IsNaN(chosenAt))
                    return coneAngle;

                return coneAngle + turnWay * Math.Min(Period.AngularSpeed * DanmakusuBalance.UTSUHO_JET_TURN_FACTOR * (float)Math.Max(t - chosenAt, 0.0), DanmakusuBalance.UTSUHO_JET_MAX_TURN);
            }

            /// <summary>The way round towards the player from the nearer of the two jets (they point both ways, so the player is never more than 90 degrees from one).</summary>
            private float wayToPlayer(BulletField f)
            {
                float off = MathF.IEEERemainder(angleToPlayer(f) - coneAngle, MathF.PI); // (-90..90 degrees from the nearer jet)
                return off < 0f ? -1f : 1f;
            }

            private static float angleToPlayer(BulletField f)
            {
                Vector2 d = f.characterPosition - f.fieldCentre;
                return d.LengthSquared > 1e-4f ? MathF.Atan2(d.Y, d.X) : -MathF.PI / 2f;
            }

            public override void Update(BulletField f, double now)
            {
                if (now < Start - warning)
                {
                    known = false;
                    chosenAt = double.NaN;
                    return;
                }

                // (the cone of light appears aimed at where the player is and stands still there until the hole shoots)
                if (!known)
                {
                    known = true;
                    coneAngle = angleToPlayer(f);
                }

                // (what is due comes out now, the way the jets point at its moment; the first chooses which way round they turn: towards the player, right then)
                for (int i = 0; i < waiting.Count; i++)
                {
                    var (taken, t0, first) = waiting[i];

                    if (t0 > now)
                        continue;

                    if (double.IsNaN(chosenAt))
                    {
                        chosenAt = t0;
                        turnWay = wayToPlayer(f);
                    }

                    shoot(f, taken, t0, first);
                    waiting.RemoveAt(i--);
                }

                float presence = Presence(now);

                if (presence <= 0.001f)
                    return;

                f.stageHide = Math.Max(f.stageHide, presence);
                // (the bullets near it are not made see-through: the dark of the hole is drawn over what is in it)
                f.lensOn = true;
                f.lensCentre = f.fieldCentre;
                f.lensRadius = Math.Max(f.lensRadius, holeRadius(f) * DanmakusuBalance.UTSUHO_LENS_FACTOR * presence);
                f.lensEdge = Math.Max(f.lensEdge, holeRadius(f) * (0.7f + 0.3f * presence));
                f.lensPresence = Math.Max(f.lensPresence, presence);
            }

            /// <summary>How many bullets it has shot out (two for every one it took in).</summary>
            private int shot;

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                waiting.Clear();
                shot = 0;
                chosenAt = double.NaN;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (!known || Presence(t) < 0.3f || emitter.Sharp || emitter.Shape is BulletShape.Petal or BulletShape.Flower) // (her own big balls fly through it, and so do Yuyuko's petals)
                    return false;

                if (distanceToSegment(f.fieldCentre, from, to) > holeRadius(f) * 0.9f + emitter.HitRadius)
                    return false;

                var taken = f.makeStored(emitter, t);
                swallow(taken, from, to, t);
                emit(f, taken);
                return true;
            }

            /// <summary>The bullet that touched the hole flies on as it was, harmless, shrinking to nothing over a moment (it is drawn as the bullet it was).</summary>
            private void swallow(StoredBullet taken, Vector2 from, Vector2 to, double t)
            {
                double span = Math.Max(t - ScanPrevious, 1.0);
                double life = DanmakusuBalance.UTSUHO_SWALLOW_MS;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = (to - from) / (float)span,
                    Radius = taken.Radius,
                    Hit = 0f,
                    Harmless = true,
                    Style = taken.Style,
                    Shape = taken.Shape,
                    Tint = taken.Tint,
                    TintAmount = taken.TintAmount,
                    Shade = taken.Shade,
                    Fade = life,
                    Until = t + life,
                });
            }

            /// <summary>
            /// The black hole has taken a bullet in: later it comes out again split in two, one out of each of its two rays. It waits for its moment (<see cref="waiting"/>): the way the jets go is worked out
            /// only as it comes out, so that the way round they turn is chosen when the first one does.
            /// </summary>
            private void emit(BulletField f, StoredBullet taken)
            {
                if (shot + 2 > DanmakusuBalance.UTSUHO_MAX_OUTPUT)
                    return;

                // (what it takes in comes out bit by bit through the whole of its time: each at its own moment between now and when the hole goes, spread evenly)
                double soonest = taken.Time + 120;
                double t0 = soonest + Math.Max(End - soonest, 0.0) * ((shot / 2 * 0.6180340 + 0.25) % 1.0);
                waiting.Add((taken, t0, shot));
                shot += 2;
            }

            /// <summary>A bullet it took in comes out at <paramref name="t0"/>, split in two, one out of each ray, the way they go then.</summary>
            private void shoot(BulletField f, StoredBullet taken, double t0, int first)
            {
                float axis = axisAt(t0);
                float radius = holeRadius(f);
                Vector2 centre = f.fieldCentre;
                float speed = taken.Speed;

                for (int pole = 0; pole < 2; pole++)
                {
                    int index = first + pole;
                    float angle = axis + (pole == 0 ? 0f : MathF.PI) + (hash01(Id, index, 5) - 0.5f) * 2f * DanmakusuBalance.UTSUHO_JET_SPREAD;
                    Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 velocity = direction * (speed * DanmakusuBalance.UTSUHO_JET_SPEED);

                    AddBullet(new StageBullet
                    {
                        T0 = t0,
                        P0 = centre + direction * (radius * DanmakusuBalance.UTSUHO_JET_OUT), // (just outside the dark, where it flashes)
                        V = velocity,
                        Radius = taken.Radius,
                        Hit = taken.Hit,
                        Style = taken.Style,
                        Shape = taken.Shape,
                        Tint = taken.Tint,
                        TintAmount = taken.TintAmount,
                        Shade = taken.Shade,
                        Until = t0 + f.stageLife(velocity),
                        Untraced = true, // (the pull of the hole would turn it straight back in: what the rays shoot flies straight)
                        Elongate = DanmakusuBalance.UTSUHO_JET_STRETCH, // (spaghettified: long and thin along its way)
                    });
                }
            }

            public override bool Hurts(BulletField f, double now)
                => Presence(now) > 0.5f && Vector2.Distance(f.characterPosition, f.fieldCentre) <= holeRadius(f) * 0.9f + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

            public override void Draw(BulletField f, double t)
            {
                // the cone of light: where the jets go (from the warning on, until the hole is gone)
                if (known && t >= Start - warning && t <= End + DanmakusuBalance.STAGE_FADE_OUT_MS)
                {
                    float coneAlpha = smoothStep((t - (Start - warning)) / 300.0) * (1f - smoothStep((t - End) / (double)DanmakusuBalance.STAGE_FADE_OUT_MS));
                    float length = f.hasScreenBounds ? Vector2.Distance(f.screenMin, f.screenMax) : 2000f;
                    float tangent = MathF.Tan(DanmakusuBalance.UTSUHO_JET_SPREAD);
                    float coneAxis = axisAt(t);

                    for (int pole = 0; pole < 2; pole++)
                    {
                        float angle = coneAxis + (pole == 0 ? 0f : MathF.PI);
                        Vector2 along = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                        // (above the bullets, under the dark of the hole: a warm cone and the same again as light added on top, so it shines)
                        Vector2 place = f.fieldCentre + along * (length * 0.5f);
                        Vector2 size = new Vector2(length, 2f * length * tangent);
                        f.stageTop.Add(StageTexture.Wedge, place, size, angle * 180f / MathF.PI, coneAlpha * 0.7f, new Color4(1f, 0.85f, 0.5f, 1f));
                        f.stageTop.Add(StageTexture.Wedge, place, size, angle * 180f / MathF.PI, coneAlpha, new Color4(1f, 0.9f, 0.65f, 1f), true);
                    }
                }

                float presence = Presence(t);

                if (presence <= 0.002f)
                    return;

                // a dark circle like Rumia's (what is inside cannot be seen, see Update), and round its edge the light that the hole bends: a thin bright ring of light hugging the dark (the light that has gone
                // round it), a fainter one further out, and a warm halo; the field's lens pushes everything that is seen near it outwards
                Vector2 centre = f.fieldCentre;
                float radius = holeRadius(f) * (0.7f + 0.3f * presence);
                float shimmer = 1f + 0.03f * MathF.Sin((float)t * 0.013f);

                f.stageTop.Add(StageTexture.Glow, centre, new Vector2(radius * 9f), 0f, 0.3f * presence, new Color4(1f, 0.7f, 0.35f, 1f), true);
                f.stageTop.Add(StageTexture.Glow, centre, new Vector2(radius * 2.15f), 0f, 0.9f * presence, Color4.Black); // (the dark fades out over the edge: only a little, so the bent light just outside it shows)
                f.stageTop.Add(StageTexture.Orb, centre, new Vector2(radius * 2f), 0f, presence, Color4.Black);
                f.stageTop.Add(StageTexture.Ring, centre, new Vector2(radius * 2.16f * shimmer), 0f, 0.95f * presence, new Color4(1f, 0.93f, 0.8f, 1f), true); // (the photon ring)
                f.stageTop.Add(StageTexture.Ring, centre, new Vector2(radius * 2.3f * shimmer), 0f, 0.5f * presence, new Color4(1f, 0.75f, 0.4f, 1f), true);
                f.stageTop.Add(StageTexture.Ring, centre, new Vector2(radius * 3.1f / shimmer), 0f, 0.22f * presence, new Color4(0.85f, 0.85f, 1f, 1f), true); // (the fainter image further out)

                drawBentStars(f, t, centre, radius, presence);

                // where a ray shoots a bullet out, a flash of light for a moment (just outside the dark: it comes out of the hole there)
                for (int i = 0; i < Bullets.Count; i++)
                {
                    var shotOut = Bullets[i];
                    double age = t - shotOut.T0;

                    if (!shotOut.Untraced || age < 0 || age > DanmakusuBalance.UTSUHO_JET_FLASH_MS)
                        continue;

                    float fade = 1f - (float)(age / DanmakusuBalance.UTSUHO_JET_FLASH_MS);
                    f.stageTop.Add(StageTexture.Glow, shotOut.P0, new Vector2(shotOut.Radius * DanmakusuBalance.UTSUHO_JET_FLASH_SIZE * (0.6f + 0.4f * fade)), 0f, fade * fade, new Color4(1f, 0.92f, 0.7f, 1f), true);
                }
            }

            /// <summary>
            /// Faint stars of the background round the hole, as a real black hole shows them: every star is seen pushed out of the hole and stretched into an arc round it (the more the closer it is), and a
            /// second time on the other side, squeezed into the thin band of light that hugs the dark. They swirl slowly round it (the inner ones faster). Under the bullets.
            /// </summary>
            private void drawBentStars(BulletField f, double t, Vector2 centre, float radius, float presence)
            {
                float lensRadius = holeRadius(f) * DanmakusuBalance.UTSUHO_LENS_FACTOR * presence;
                float reach = lensRadius * DanmakusuBalance.UTSUHO_LENS_STAR_REACH;
                float dot = f.circleRadius * 0.16f;
                var light = new Color4(1f, 0.95f, 0.86f, 1f);

                if (lensRadius < 1f)
                    return;

                for (int i = 0; i < DanmakusuBalance.UTSUHO_LENS_STARS; i++)
                {
                    // (where the star really is: spread evenly over a disc round the hole, its brightness its own)
                    float share = (i * 0.7548777f + 0.13f) % 1f;
                    float behind = reach * MathF.Sqrt(0.02f + 0.98f * share);
                    float swirl = (float)(t * DanmakusuBalance.UTSUHO_LENS_SWIRL) * lensRadius / Math.Max(behind, lensRadius);
                    float angle = i * 2.3999632f + swirl;
                    Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    float root = MathF.Sqrt(behind * behind + 4f * lensRadius * lensRadius);
                    float primary = f.lensDistance(behind), secondary = (root - behind) * 0.5f; // (seen from the dark's edge on, as the bullets are)
                    float stretch = Math.Min(primary / behind, DanmakusuBalance.UTSUHO_LENS_MAX_STRETCH);
                    float bright = 0.35f + 0.65f * ((i * 0.618034f + 0.41f) % 1f);
                    float alpha = presence * bright * 0.55f * smoothStep((reach - behind) / (reach * 0.35f));

                    arc(f, centre, primary, angle, dot * 2f * stretch, dot * 2f / MathF.Sqrt(stretch), alpha, light);

                    // (the second image, on the other side, in the band of light at the edge of the dark)
                    float band = radius * (1.02f + 0.18f * Math.Clamp(secondary / lensRadius, 0f, 1f));
                    arc(f, centre, band, angle + MathF.PI, dot * 2f * DanmakusuBalance.UTSUHO_LENS_MAX_STRETCH, dot * 0.8f, alpha * 0.7f, light, 1); // (thin and close to the dark: one piece is enough)
                }
            }

            /// <summary>A streak of light <paramref name="length"/> long round <paramref name="centre"/> at <paramref name="distance"/>, its middle at <paramref name="angle"/>: short pieces laid along the circle (it is bent, not straight).</summary>
            private static void arc(BulletField f, Vector2 centre, float distance, float angle, float length, float thickness, float alpha, Color4 colour, int pieces = 3)
            {
                float piece = length / pieces;

                for (int k = 0; k < pieces; k++)
                {
                    float at = angle + (k - (pieces - 1) * 0.5f) * piece / Math.Max(distance, 1f);
                    Vector2 place = centre + new Vector2(MathF.Cos(at), MathF.Sin(at)) * distance;
                    f.stageBack.Add(StageTexture.Glow, place, new Vector2(pieces > 1 ? piece * 1.4f : piece, thickness), at * 180f / MathF.PI + 90f, alpha, colour, true); // (a little longer than their share: they overlap into one streak)
                }
            }
        }
    }
}
