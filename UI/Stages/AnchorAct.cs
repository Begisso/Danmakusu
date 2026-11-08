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
        // Minamitsu Murasa: she stands in the middle all through her section and never pauses. Every sixteen beats she throws eight anchors round her, one after another over four beats (evenly round the circle, every next
        // sequence turned by half a step), and on top of them every eight beats one at the player. An anchor (the user's picture of a silver anchor) flies crown first to the wall in four beats, chained to her: the chain
        // is Wakasagihime's bubbles, strung from her to its ring. It stops with its crown a little out of the wall (a big metal clang, the user's hammer hit; the screen shakes for a moment, 1-2 px), and stays there for
        // eight beats; then it is unchained: the bubbles of its chain stay where they are and pop one after another from her out to the anchor, over two beats (silently), each into a ring of six small bullets (0.4 of a plain one) that spread away, and the anchor sinks: over four beats it crawls on out of
        // the playfield the way it flew, by its own length at an even pace, fading out and shrinking. The clang is played twice at once (louder), at most three clangs at a time. The anchors (where their picture is not see-through) and their bubbles (each until it pops)
        // hurt like a slider body (they are not used up by a hit). In the last twelve beats of her section she throws nothing more (what she throws must be unchained before it ends). What she throws at the player is
        // aimed when it is thrown (and aimed again when time goes back before that); everything else is fixed.
        // ======================================================================================================================================================

        private sealed class AnchorAct : StageAct
        {
            private sealed class Anchor
            {
                public int Number;
                public double Throw, Arrive, Unchain;
                public bool Aimed;

                /// <summary>(known once it is thrown: where she was and which way it flies; an aimed one only then)</summary>
                public bool Known;

                public Vector2 From, Direction;
                public bool Sounded;

                /// <summary>Where its bubbles were when it was unchained (null until then): they pop one after another from its ring towards her (see <see cref="popAt"/>).</summary>
                public List<Vector2>? Popped;
            }

            /// <summary>When bubble <paramref name="k"/> of an anchor (counted from its ring) pops: from her out to the anchor, the one next to her when it is unchained, the one at its ring two beats later, evenly between.</summary>
            private double popAt(Anchor anchor, int k)
            {
                int count = anchor.Popped?.Count ?? 0;
                double fromHer = count > 1 ? (count - 1 - k) / (double)(count - 1) : 0;
                return anchor.Unchain + fromHer * beat * DanmakusuBalance.MURASA_POP_SPREAD_BEATS;
            }

            // (the clangs that are sounding: one channel of each, at most so many at once)
            private readonly List<osu.Framework.Audio.Sample.SampleChannel> clangs = new List<osu.Framework.Audio.Sample.SampleChannel>();

            /// <summary>A clang, played so many times at once (stacked: louder), unless as many clangs are sounding already as may at once.</summary>
            private void playClang(BulletField f)
            {
                clangs.RemoveAll(channel => !channel.Playing);

                if (clangs.Count >= DanmakusuBalance.MURASA_SOUNDS_AT_ONCE)
                    return;

                for (int copy = 0; copy < DanmakusuBalance.MURASA_SOUND_COPIES; copy++)
                {
                    if (f.playHeldSound("murasa_anchor") is not { } started)
                        return;

                    started.Volume.Value = DanmakusuBalance.MURASA_SOUND_VOLUME;

                    if (copy == 0)
                        clangs.Add(started);
                }
            }

            private readonly double beat;
            private readonly List<Anchor> anchors;

            public AnchorAct(BossPeriod period)
            {
                Period = period;
                beat = Math.Max(period.BeatLength, 1);
                Start = period.ShootStart;
                End = period.End + beat * Math.Max(DanmakusuBalance.MURASA_SINK_BEATS, DanmakusuBalance.MURASA_POP_SPREAD_BEATS) + DanmakusuBalance.MURASA_POP_MS + 500; // (the last anchor sinks and its bubbles pop after her section)
                StageLayer.PrepareAnchorShape();

                double flight = beat * DanmakusuBalance.MURASA_FLIGHT_BEATS;
                double hold = beat * DanmakusuBalance.MURASA_HOLD_BEATS;
                double last = period.End - flight - hold; // (what would not be unchained before her section ends is not thrown: every anchor goes its whole way, none is let go early, all at once)
                var list = new List<Anchor>();

                // the sequences round her: evenly round the circle, one after another over four beats, every next sequence turned by half a step
                int sequence = 0;

                for (double from = period.ShootStart; from < last; from += beat * DanmakusuBalance.MURASA_SEQUENCE_BEATS, sequence++)
                {
                    for (int k = 0; k < DanmakusuBalance.MURASA_ANCHORS; k++)
                    {
                        double thrown = from + beat * DanmakusuBalance.MURASA_THROW_SPREAD_BEATS * k / DanmakusuBalance.MURASA_ANCHORS;

                        if (thrown >= last)
                            break;

                        float angle = MathF.Tau * (k + 0.5f * (sequence % 2)) / DanmakusuBalance.MURASA_ANCHORS - MathF.PI / 2f;
                        list.Add(new Anchor { Throw = thrown, Direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) });
                    }
                }

                // and one at the player every eight beats
                for (double thrown = period.ShootStart; thrown < last; thrown += beat * DanmakusuBalance.MURASA_AIMED_EVERY_BEATS)
                    list.Add(new Anchor { Throw = thrown, Aimed = true });

                list.Sort((a, b) => a.Throw.CompareTo(b.Throw));

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Number = i;
                    list[i].Arrive = list[i].Throw + flight;
                    list[i].Unchain = Math.Min(list[i].Arrive + hold, period.End);
                }

                anchors = list;
            }

            public override void Reset(BulletField f, double time)
            {
                // what was thrown before this moment flew as it did; the pops are seen again as time goes on
                foreach (var anchor in anchors)
                {
                    if (anchor.Throw > time)
                        anchor.Known = false;

                    anchor.Popped = null;
                    anchor.Sounded = anchor.Arrive <= time; // (what has hit the wall already is not heard again)
                }

                ClearBullets(); // (the rings of the pops are made again as time goes on)
            }

            /// <summary>How long the anchor is (px): its picture's length.</summary>
            private static float length(BulletField f) => f.circleRadius * DanmakusuBalance.MURASA_ANCHOR_CIRCLES;

            /// <summary>How wide it is (px): as its picture is.</summary>
            private static float width(BulletField f)
            {
                var (w, h) = StageLayer.AnchorPictureSize;
                return w > 0 ? length(f) * h / w : length(f) * 0.85f;
            }

            /// <summary>Where the middle of the anchor stops: its crown (the end of its picture) sticks a little out of the wall it flies to.</summary>
            private static Vector2 restOf(BulletField f, Anchor anchor)
            {
                Vector2 from = anchor.From, way = anchor.Direction;
                float across = way.X > 1e-6f ? (f.stageMax.X - from.X) / way.X : way.X < -1e-6f ? (f.stageMin.X - from.X) / way.X : float.PositiveInfinity;
                float down = way.Y > 1e-6f ? (f.stageMax.Y - from.Y) / way.Y : way.Y < -1e-6f ? (f.stageMin.Y - from.Y) / way.Y : float.PositiveInfinity;
                float toWall = Math.Max(Math.Min(across, down), 0f);

                if (float.IsInfinity(toWall))
                    toWall = 0f;

                float l = length(f);
                return from + way * (toWall + DanmakusuBalance.MURASA_ANCHOR_OUT * l - 0.5f * l);
            }

            /// <summary>Where the middle of the anchor is at <paramref name="t"/> (it flies there evenly from her and stays).</summary>
            private Vector2 centreOf(BulletField f, Anchor anchor, double t)
            {
                float flown = (float)Math.Clamp((t - anchor.Throw) / Math.Max(anchor.Arrive - anchor.Throw, 1), 0.0, 1.0);
                return Vector2.Lerp(anchor.From, restOf(f, anchor), flown);
            }

            private static bool outAt(Anchor anchor, double t) => anchor.Known && t >= anchor.Throw && t < anchor.Unchain;

            private float bubbleRadius(BulletField f) => f.stageBaseRadius(Period) * DanmakusuBalance.MURASA_BUBBLE_SIZE;

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                foreach (var anchor in anchors)
                {
                    if (anchor.Throw > now)
                        break;

                    if (!anchor.Known)
                    {
                        anchor.Known = true;
                        anchor.From = f.bossVisualOf(Period);

                        if (anchor.Aimed)
                        {
                            Vector2 aim = f.characterPosition - anchor.From;
                            anchor.Direction = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;
                        }
                    }

                    // (it hits the wall: a big metal clang; one that hit long ago, after a jump in time, is not heard)
                    if (!anchor.Sounded && now >= anchor.Arrive)
                    {
                        anchor.Sounded = true;

                        if (now - anchor.Arrive < DanmakusuBalance.MURASA_SOUND_LATE_MS)
                            playClang(f);
                    }

                    // (it is unchained: its bubbles stay where they are and pop one after another, each into a ring of small bullets)
                    if (anchor.Popped == null && now >= anchor.Unchain)
                    {
                        anchor.Popped = new List<Vector2>();
                        links(f, anchor, anchor.Unchain, anchor.Popped);
                        popRings(f, anchor);
                    }
                }
            }

            /// <summary>Every bubble of an unchained anchor pops into a ring of six small bullets that spread away, at its own moment (each ring turned a little from the one before).</summary>
            private void popRings(BulletField f, Anchor anchor)
            {
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.MURASA_RING_SIZE;
                float speed = Math.Max(stageBulletSpeed(Period), 0.02f) * DanmakusuBalance.MURASA_RING_SPEED;
                int colours = Math.Max(f.palette.Length, 1);
                float facing = MathF.Atan2(anchor.Direction.Y, anchor.Direction.X);

                for (int k = 0; k < anchor.Popped!.Count; k++)
                {
                    double pops = popAt(anchor, k);
                    float turn = facing + MathF.Tau * hash01(anchor.Number, k, 68) / DanmakusuBalance.MURASA_RING_BULLETS;

                    for (int b = 0; b < DanmakusuBalance.MURASA_RING_BULLETS; b++)
                    {
                        float angle = turn + MathF.Tau * b / DanmakusuBalance.MURASA_RING_BULLETS;
                        Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;

                        AddBullet(new StageBullet
                        {
                            T0 = pops,
                            P0 = anchor.Popped[k],
                            V = velocity,
                            Radius = radius,
                            Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                            Style = anchor.Number % colours,
                            Shape = BulletShape.Circle,
                            Until = pops + f.stageLife(velocity),
                        });
                    }
                }
            }

            /// <summary>The bubbles of the chain at <paramref name="t"/>: from the anchor's ring towards her, so many radii apart, as far as the chain reaches (none while the ring has not come out of her yet).</summary>
            private void links(BulletField f, Anchor anchor, double t, List<Vector2> into)
            {
                into.Clear();

                Vector2 centre = centreOf(f, anchor, t);
                Vector2 ring = centre + anchor.Direction * ((DanmakusuBalance.MURASA_RING_AT - 0.5f) * length(f));
                Vector2 her = f.bossVisualOf(Period);
                Vector2 back = her - ring;
                float distance = back.Length;
                float gap = f.stageBaseRadius(Period) * DanmakusuBalance.MURASA_LINK_GAP_RADII;

                if (distance < gap || Vector2.Dot(ring - her, anchor.Direction) <= 0f)
                    return;

                back /= distance;

                for (float along = 0f; along < distance - gap * 0.5f; along += gap)
                    into.Add(ring + back * along);
            }

            private readonly List<Vector2> linkBuffer = new List<Vector2>();

            public override bool Hurts(BulletField f, double now)
            {
                if (f.failed)
                    return false;

                float l = length(f), w = width(f);
                var (pictureWidth, pictureHeight) = StageLayer.AnchorPictureSize;
                float perPixel = pictureWidth > 0 ? l / pictureWidth : 1f;
                float bubbleReach = bubbleRadius(f) * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                foreach (var anchor in anchors)
                {
                    if (anchor.Throw > now)
                        break;

                    // (unchained: its bubbles hurt where they stay until each pops)
                    if (anchor.Popped != null && now >= anchor.Unchain)
                    {
                        for (int k = 0; k < anchor.Popped.Count; k++)
                        {
                            if (now < popAt(anchor, k) && Vector2.DistanceSquared(anchor.Popped[k], f.characterPosition) <= bubbleReach * bubbleReach)
                                return true;
                        }

                        continue;
                    }

                    if (!outAt(anchor, now))
                        continue;

                    // the anchor: where its picture is (along it and across it, in the picture's pixels)
                    Vector2 offset = f.characterPosition - centreOf(f, anchor, now);

                    if (offset.Length <= 0.5f * MathF.Sqrt(l * l + w * w) + DanmakusuBalance.PLAYER_HITBOX_RADIUS && pictureWidth > 0)
                    {
                        Vector2 way = anchor.Direction;
                        float along = Vector2.Dot(offset, way) / perPixel + pictureWidth * 0.5f;
                        float across = (way.X * offset.Y - way.Y * offset.X) / perPixel + pictureHeight * 0.5f;

                        if (StageLayer.AnchorDistance(along, across) * perPixel <= DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                            return true;
                    }

                    // its bubbles
                    links(f, anchor, now, linkBuffer);

                    foreach (var bubble in linkBuffer)
                    {
                        if (Vector2.DistanceSquared(bubble, f.characterPosition) <= bubbleReach * bubbleReach)
                            return true;
                    }
                }

                return false;
            }

            /// <summary>0..1: an anchor that hits the wall shakes the screen for a moment (the field makes 2 px of 1, dying down to 1 px).</summary>
            public float ShakeAt(double now)
            {
                float shake = 0f;

                foreach (var anchor in anchors)
                {
                    if (anchor.Arrive > now)
                        break;

                    if (anchor.Known && now < anchor.Arrive + DanmakusuBalance.MURASA_SHAKE_MS)
                        shake = Math.Max(shake, 1f - (float)((now - anchor.Arrive) / DanmakusuBalance.MURASA_SHAKE_MS));
                }

                return shake;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                float l = length(f), w = width(f);
                float radius = bubbleRadius(f);
                double sink = beat * DanmakusuBalance.MURASA_SINK_BEATS;
                var rim = new Color4(0.85f, 0.95f, 1f, 1f);

                foreach (var anchor in anchors)
                {
                    if (anchor.Throw > t)
                        break;

                    if (!anchor.Known)
                        continue;

                    if (t < anchor.Unchain + sink)
                    {
                        // (unchained, it sinks: crawls slowly on out of the playfield the way it flew, at an even pace, fading out and shrinking)
                        float sunk = t < anchor.Unchain ? 0f : smoothStep((t - anchor.Unchain) / sink);
                        float crawled = t < anchor.Unchain ? 0f : (float)Math.Clamp((t - anchor.Unchain) / sink, 0.0, 1.0);
                        float scale = 1f - (1f - DanmakusuBalance.MURASA_SINK_SIZE) * sunk;
                        float degrees = MathF.Atan2(anchor.Direction.Y, anchor.Direction.X) * 180f / MathF.PI;
                        Vector2 centre = centreOf(f, anchor, t) + anchor.Direction * (DanmakusuBalance.MURASA_SINK_CRAWL * l * crawled);

                        f.stageBack.Add(StageTexture.Anchor, centre, new Vector2(l, w) * scale, degrees, 1f - sunk, Color4.White);
                    }

                    if (outAt(anchor, t))
                    {
                        // the chain: hollow bubbles as Wakasagihime's (a rim, a fainter inner rim, a highlight)
                        links(f, anchor, t, linkBuffer);

                        foreach (var bubble in linkBuffer)
                            drawBubble(f, bubble, radius, rim);
                    }
                    else if (anchor.Popped != null && t >= anchor.Unchain)
                    {
                        // (unchained: each bubble stays until it pops, then swells by half and fades)
                        for (int k = 0; k < anchor.Popped.Count; k++)
                        {
                            double pops = popAt(anchor, k);

                            if (t < pops)
                                drawBubble(f, anchor.Popped[k], radius, rim);
                            else if (t < pops + DanmakusuBalance.MURASA_POP_MS)
                            {
                                float popped = (float)((t - pops) / DanmakusuBalance.MURASA_POP_MS);
                                f.stageRipple.AddRing(anchor.Popped[k], radius * (1f + 0.5f * popped), 1.5f, 0.9f * (1f - popped), rim);
                            }
                        }
                    }
                }
            }

            private static void drawBubble(BulletField f, Vector2 at, float radius, Color4 rim)
            {
                f.stageRipple.AddRing(at, radius, 2f, 0.9f, rim);
                f.stageRipple.AddRing(at, radius * 0.82f, 1f, 0.3f, rim);
                f.stageTree.Add(StageTexture.Glow, at + new Vector2(-0.4f, -0.4f) * radius, new Vector2(radius * 0.6f), 0f, 0.7f, Color4.White, true);
            }
        }
    }
}
