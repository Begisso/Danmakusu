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
        // Koakuma: every twentieth bullet that flies while she attacks (the bullets of the hit objects and the other boss too) turns into a bat, and everything she shoots herself (her stars, in a spinner the stars of the spinner)
        // is a bat from the start. A bat is a bullet as big as the one it was, drawn as a flapping bat (black, with a white outline that fades inwards), and it homes as Byakuren's beams do: it flies at constant speed and for its first
        // sixteen beats turns towards the player twice as fast as an arrowhead does (pointing away it still turns, and it comes back from past the edge of the screen), then flies straight on until it leaves the screen. Once it
        // has been within two osu! circle radii of the player for eight beats in all it fades out. Every bat dims the bullets within one circle radius of it.
        // Like Nazrin's arrowheads the bats cannot be written as a function of time: the act simulates them step by step and keeps where each one has been; the field checks them like the other bullets of a stage.
        // Chiyari's bats (Straight) are only what she shoots herself: they do not home, fly half as fast as what they were, and live until they leave the screen. Elly's are the same at their own speed, twice as many.
        // ======================================================================================================================================================

        private sealed class BatAct : StageAct
        {
            private const double trail_step_ms = DanmakusuBalance.HOMING_STEP_MS;

            private sealed class Bat
            {
                public int Index; // (in Bullets)
                public Vector2 Position;
                public Vector2 Velocity;
                public double Time;
                public float Radius; // (the radius of the bullet it was: the bat is drawn bigger)
                public double Near; // (how long it has been within two circle radii of the player, in all)
                public double FadeAt = double.NaN;
                public bool Gone;
                public float Phase;
                public float Side; // (which way round it turns while it points away from the player: 1 or -1, chosen the first time; 0 = not yet)
                public double Born;
                public List<Vector2> Trail = null!;
                public bool Straight; // (it flies straight on: the bullet itself says where it is)
            }

            private readonly BulletField field;
            private readonly double beat;
            private readonly List<Bat> bats = new List<Bat>();
            private int sequence; // (how many bullets of the others have been seen: every twentieth is turned into a bat)
            private readonly bool straight;
            private readonly float straightSpeed; // (a bat that does not home flies this many times as fast as what it was)

            public BatAct(BulletField field, BossPeriod period, double start, double end, bool straight = false, float straightSpeed = DanmakusuBalance.CHIYARI_BAT_SPEED)
            {
                this.field = field;
                this.straight = straight;
                this.straightSpeed = straightSpeed;
                Period = period;
                Start = start;
                End = end;
                beat = Math.Max(period.BeatLength, 1);
                AbsorbFrom = start;
                AbsorbTo = end;
            }

            public override void Reset(BulletField f, double time)
            {
                bats.Clear();
                sequence = 0;
                ClearBullets();

                foreach (var emitter in f.emitters)
                    emitter.BatBase = -1;
            }

            public override bool Takes(BulletField f, double t, Vector2 from, Vector2 to, BulletEmitter emitter, int bullet)
            {
                if (f.failed || t < Start || t >= End || emitter.Shape == BulletShape.Train)
                    return false;

                if (!emitter.Bats)
                {
                    if (straight)
                        return false;

                    // (the others' bullets are counted ring by ring, in the order the rings are first seen; every twentieth one is a bat)
                    if (emitter.BatBase < 0)
                    {
                        emitter.BatBase = sequence;
                        sequence += emitter.Count;
                    }

                    if ((emitter.BatBase + bullet + 1) % DanmakusuBalance.KOAKUMA_BAT_EVERY != 0)
                        return false;
                }

                Vector2 delta = to - from;

                if (delta.LengthSquared < 1e-8f)
                    return false; // (it stands still: it has no way to go on in)

                var stored = f.makeStored(emitter, t);
                double dt = Math.Max(t - ScanPrevious, 1e-3);
                float speed = emitter.Ops == null ? Math.Max(delta.Length / (float)dt, 0.02f) : stored.Speed;
                float big = emitter.BigScale(bullet);

                // (a homing bat flies as fast as Byakuren's beams, whatever it was before)
                if (!straight)
                    speed = stageBulletSpeed(Period) * DanmakusuBalance.BYAKUREN_TRAIL_SPEED;
                else
                    speed *= straightSpeed;

                Vector2 velocity = Vector2.Normalize(delta) * speed;
                int index = Bullets.Count;

                // (a bat that does not home flies straight on until it is past the edge of the screen: when that is is known now, and nothing has to follow it step by step)
                double until = t + Math.Min(DanmakusuBalance.HOMING_LIFETIME_MS, DanmakusuBalance.KOAKUMA_BAT_LIFE_MS);

                if (straight)
                {
                    float margin = stored.Radius * big * 2f;
                    double leaves = 600000;

                    if (velocity.X > 1e-6f)
                        leaves = Math.Min(leaves, (f.stageMax.X + margin - to.X) / velocity.X);
                    else if (velocity.X < -1e-6f)
                        leaves = Math.Min(leaves, (f.stageMin.X - margin - to.X) / velocity.X);

                    if (velocity.Y > 1e-6f)
                        leaves = Math.Min(leaves, (f.stageMax.Y + margin - to.Y) / velocity.Y);
                    else if (velocity.Y < -1e-6f)
                        leaves = Math.Min(leaves, (f.stageMin.Y - margin - to.Y) / velocity.Y);

                    until = t + Math.Max(leaves, 0.0);
                }

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = to,
                    V = velocity,
                    Radius = stored.Radius * big,
                    Hit = stored.Hit * big,
                    Style = stored.Style,
                    Shape = BulletShape.Circle,
                    Tint = stored.Tint,
                    TintAmount = stored.TintAmount,
                    Shade = stored.Shade,
                    Until = until,
                    Trail = straight ? null : new List<Vector2>(64) { to },
                    TrailStep = trail_step_ms,
                    Hidden = true, // (the act draws it: as a bat)
                });

                if (Bullets.Count == index)
                    return false; // (too many bullets: it stays as it is)

                bats.Add(new Bat
                {
                    Index = index,
                    Position = to,
                    Velocity = velocity,
                    Time = t,
                    Born = t,
                    Radius = stored.Radius * big,
                    Phase = hash01((int)(to.X * 10f), (int)(to.Y * 10f), index),
                    Trail = Bullets[index].Trail!,
                    Straight = straight,
                });

                return true;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || bats.Count == 0)
                    return;

                Vector2 player = f.characterPosition;
                float step = (float)trail_step_ms;
                float gravity = straight ? 0f : Period.ArrowGravity * DanmakusuBalance.KOAKUMA_BAT_TURN_FACTOR;
                float near = straight ? -1f : f.circleRadius * DanmakusuBalance.KOAKUMA_BAT_FADE_RADIUS_CIRCLES; // (a bat that does not home is never near enough to fade)
                double fadeAfter = beat * DanmakusuBalance.KOAKUMA_BAT_NEAR_BEATS;

                foreach (var bat in bats)
                {
                    int guard = 0;

                    if (bat.Straight)
                        continue;

                    while (!bat.Gone && bat.Time + trail_step_ms <= now && guard++ < 600)
                        advance(f, bat, player, step, gravity, near, fadeAfter);
                }
            }

            /// <summary>
            /// One step of a bat: for its first sixteen beats it turns towards the player as Byakuren's beams do (the speed stays; pointing away it still turns, and it comes back from past the edge of the screen), then it flies straight
            /// on; once it has been near them long enough it fades, and is gone when it has faded or leaves the screen after its homing is over.
            /// </summary>
            private void advance(BulletField f, Bat bat, Vector2 player, float step, float gravity, float near, double fadeAfter)
            {
                Vector2 velocity = bat.Velocity;
                float speedSquared = velocity.LengthSquared;
                bool homing = !straight && bat.Time - bat.Born < beat * DanmakusuBalance.KOAKUMA_HOMING_BEATS;

                if (homing && speedSquared > 1e-12f)
                {
                    float speed = MathF.Sqrt(speedSquared);
                    Vector2 heading = velocity / speed;
                    Vector2 toPlayer = player - bat.Position;
                    float length = toPlayer.Length;

                    if (length > 1e-3f)
                    {
                        Vector2 pull = toPlayer * (gravity * step / length);
                        Vector2 sideways = pull - heading * Vector2.Dot(pull, heading);
                        float across = sideways.Length, least = pull.Length * DanmakusuBalance.KOAKUMA_TURN_FLOOR;

                        // (pointing away from the player the sideways pull is small, and at exactly behind it nothing: it still turns, one way round for as long as it takes)
                        if (across < least && Vector2.Dot(pull, heading) < 0f)
                        {
                            Vector2 normal = new Vector2(-heading.Y, heading.X);

                            if (bat.Side == 0f)
                                bat.Side = across > least * 0.01f ? (Vector2.Dot(sideways, normal) >= 0f ? 1f : -1f) : bat.Index % 2 == 0 ? 1f : -1f; // (dead straight: by its number)

                            sideways = normal * (bat.Side * least);
                        }

                        Vector2 turned = velocity + sideways * DanmakusuBalance.HOMING_TURN_SCALE;
                        float turnedLength = turned.Length;

                        if (turnedLength > 1e-9f)
                            bat.Velocity = turned * (speed / turnedLength);
                    }
                }

                bat.Position += bat.Velocity * step;
                bat.Time += trail_step_ms;
                bat.Trail.Add(bat.Position);

                if (double.IsNaN(bat.FadeAt))
                {
                    if (Vector2.Distance(bat.Position, player) <= near)
                        bat.Near += trail_step_ms;

                    if (bat.Near >= fadeAfter)
                    {
                        // (it fades from now on, and cannot hurt any more)
                        bat.FadeAt = bat.Time;

                        var bullet = Bullets[bat.Index];
                        bullet.Until = bat.Time;
                        Bullets[bat.Index] = bullet;
                    }
                }
                else if (bat.Time >= bat.FadeAt + DanmakusuBalance.KOAKUMA_BAT_FADE_MS)
                {
                    bat.Gone = true;
                    return;
                }

                Vector2 low = f.stageMin - new Vector2(bat.Radius * 2f), high = f.stageMax + new Vector2(bat.Radius * 2f);

                if (!homing && (bat.Position.X < low.X || bat.Position.X > high.X || bat.Position.Y < low.Y || bat.Position.Y > high.Y))
                {
                    bat.Gone = true;

                    var bullet = Bullets[bat.Index];
                    bullet.Until = Math.Min(bullet.Until, bat.Time);
                    Bullets[bat.Index] = bullet;
                }
            }

            private bool alive(BulletField f, Bat bat, double t)
            {
                var bullet = Bullets[bat.Index];

                if (t < bullet.T0 || t >= bullet.Until)
                    return false;

                if (bat.Gone && t > bat.Time)
                    return false;

                if (!double.IsNaN(bat.FadeAt) && t > bat.FadeAt + DanmakusuBalance.KOAKUMA_BAT_FADE_MS)
                    return false;

                return !(DanmakusuBalance.HIDE_BULLETS_AFTER_HIT && f.stageConsumed.Contains(Id * 65536 + bat.Index));
            }

            private float presence(Bat bat, double t)
                => double.IsNaN(bat.FadeAt) || t <= bat.FadeAt ? 1f : 1f - smoothStep((t - bat.FadeAt) / DanmakusuBalance.KOAKUMA_BAT_FADE_MS);

            public override void Dims(BulletField f, double t, List<StageDim> into)
            {
                if (f.failed || bats.Count == 0)
                    return;

                foreach (var bat in bats)
                {
                    if (!alive(f, bat, t))
                        continue;

                    float level = presence(bat, t);

                    if (level < 0.02f)
                        continue;

                    into.Add(new StageDim { Centre = stageBulletPosition(Bullets[bat.Index], t), Radius = f.circleRadius * DanmakusuBalance.KOAKUMA_BAT_DIM_RADIUS_CIRCLES, Strength = DanmakusuBalance.KOAKUMA_BAT_DIM * level });
                }
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed)
                    return;

                foreach (var bat in bats)
                {
                    if (!alive(f, bat, t))
                        continue;

                    var bullet = Bullets[bat.Index];
                    Vector2 position = stageBulletPosition(bullet, t);
                    Vector2 velocity = stageBulletVelocity(bullet, t);
                    float heading = velocity.LengthSquared > 1e-12f ? MathF.Atan2(velocity.Y, velocity.X) * 180f / MathF.PI + 90f : 0f;
                    float wing = bat.Radius * DanmakusuBalance.KOAKUMA_BAT_SIZE_FACTOR;
                    float flap = (float)((t - bullet.T0) / DanmakusuBalance.KOAKUMA_BAT_FLAP_MS) + bat.Phase;
                    int frame = (int)MathF.Floor((flap - MathF.Floor(flap)) * StageLayer.BatFrames) % StageLayer.BatFrames;

                    f.stageBats.Add(StageLayer.BatFrame(frame), position, new Vector2(2f * StageLayer.BatExtent * wing), heading, presence(bat, t), Color4.White);
                }
            }
        }
    }
}
