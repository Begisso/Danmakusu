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
        // Enoko Mitsugashira: bear traps, put down the way Flandre puts down her eyes but twice as rarely (every sixteen beats three of them, two beats after each other, each on the player's place at that moment, with an approach
        // circle that closes in four beats), 2.25 circle radii wide. When its approach circle has closed the trap is armed and stays on the field. A player who steps into an armed trap is caught:
        // the trap snaps shut, hurts, fades out, and holds the character where it is for two beats, with a red glow round it. A trap nobody has stepped into snaps shut by itself 32 beats after it was put down, catching no one.
        // There are never more than 6 traps on the map: the oldest one simply fades out when a new one is put down. The traps of all her sections are one act (they stay after a section is over).
        // ======================================================================================================================================================

        private sealed class TrapAct : StageAct
        {
            private sealed class Trap
            {
                public double Placed, Armed, Closes;
                public Vector2 At;
                public bool Known;
                public bool ArmSounded; // (the sound of Flandre's eye going off played when it was armed)
                public double Sprung = double.NaN;
                public double Gone = double.PositiveInfinity;
            }

            private readonly BulletField field;
            private readonly List<Trap> traps = new List<Trap>();
            private readonly double beat;
            private int liveFrom; // (every trap before this one is shut or gone for good)

            public TrapAct(BulletField field, List<BossPeriod> periods)
            {
                this.field = field;
                Period = periods[0];
                beat = Math.Max(Period.BeatLength, 1);

                // (the same times as Flandre's eyes, section by section)
                foreach (var period in periods)
                {
                    double periodBeat = Math.Max(period.BeatLength, 1);
                    double throwMs = Math.Max(periodBeat * DanmakusuBalance.FLANDRE_THROW_EVERY_BEATS * DanmakusuBalance.ENOKO_RARER, 100);
                    double gapMs = periodBeat * DanmakusuBalance.ENOKO_TRAP_GAP_BEATS;
                    double armMs = Math.Max(periodBeat * DanmakusuBalance.FLANDRE_EYE_BEATS, 100);

                    for (int k = 0; k < 6000; k++)
                    {
                        double placed = period.ShootStart + k / DanmakusuBalance.FLANDRE_EYES_PER_THROW * throwMs + k % DanmakusuBalance.FLANDRE_EYES_PER_THROW * gapMs;

                        if (placed + armMs > period.End)
                            break;

                        traps.Add(new Trap { Placed = placed, Armed = placed + armMs, Closes = placed + periodBeat * DanmakusuBalance.ENOKO_TRAP_LIFE_BEATS });
                    }
                }

                traps.Sort((a, b) => a.Placed.CompareTo(b.Placed));
                Start = traps.Count > 0 ? traps[0].Placed : Period.ShootStart;
                End = traps.Count > 0 ? traps[^1].Armed + 600000 : Start; // (far past the last trap: what it holds and its fading are over long before)
            }

            private float radius => this.field.circleRadius * DanmakusuBalance.ENOKO_TRAP_RADIUS;

            private static bool shut(Trap trap, double t) => !double.IsNaN(trap.Sprung) && trap.Sprung <= t;

            private bool on(Trap trap, double t) => trap.Known && trap.Placed <= t && !shut(trap, t) && t < trap.Gone;

            /// <summary>(the autoplay) A trap that is armed within the time, with the place in its reach: stepping in is caught.</summary>
            public override bool Springs(BulletField f, Vector2 at, double from, double to)
            {
                float reach = radius * DanmakusuBalance.ENOKO_TRAP_TRIGGER + DanmakusuBalance.PLAYER_HITBOX_RADIUS + DanmakusuBalance.AUTO_MARGIN;

                for (int i = liveFrom; i < traps.Count; i++)
                {
                    var trap = traps[i];

                    if (trap.Placed > to)
                        break;

                    if (to < trap.Armed || !on(trap, Math.Max(from, trap.Armed)))
                        continue;

                    if (Vector2.Distance(at, trap.At) <= reach)
                        return true;
                }

                return false;
            }

            public override void Reset(BulletField f, double time)
            {
                foreach (var trap in traps)
                {
                    if (trap.Placed > time)
                        trap.Known = false;

                    if (trap.Sprung > time)
                        trap.Sprung = double.NaN;

                    if (trap.Gone > time)
                        trap.Gone = double.PositiveInfinity;

                    trap.ArmSounded = trap.Armed <= time;
                }

                liveFrom = 0;
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed || now < Start)
                    return;

                int alive = 0;

                for (int i = liveFrom; i < traps.Count; i++)
                {
                    var trap = traps[i];

                    if (trap.Placed > now)
                        break;

                    // (a trap is put on the place where the player is when it is put down; one that is found far too late - after a jump forward - is not put down at all)
                    if (!trap.Known && now <= trap.Armed + 50)
                    {
                        trap.At = f.characterPosition;
                        trap.Known = true;
                    }

                    // (one that has lain long enough snaps shut by itself, on nobody; not with a sound when a seek jumped over it)
                    if (on(trap, now) && now >= trap.Closes)
                    {
                        trap.Sprung = trap.Closes;

                        if (now - trap.Closes < 250)
                            f.playStageSound("mokou_wall_hit");
                    }

                    if (on(trap, now))
                        alive++;

                    // (when its approach circle has closed the trap is set down with the sound of Flandre's eye going off; not when a seek jumped over it)
                    if (trap.Known && !trap.ArmSounded && now >= trap.Armed)
                    {
                        trap.ArmSounded = true;

                        if (now - trap.Armed < 250 && on(trap, now))
                            f.playStageSound("mokou_wall_hit");
                    }
                }

                // (no more than ENOKO_MAX_TRAPS on the map: the oldest ones fade out)
                for (int i = liveFrom; i < traps.Count && alive > DanmakusuBalance.ENOKO_MAX_TRAPS; i++)
                {
                    if (!on(traps[i], now))
                        continue;

                    traps[i].Gone = now;
                    alive--;
                }

                // an armed trap that the player steps into snaps shut: it hurts, and it holds the character for two beats
                float reach = radius * DanmakusuBalance.ENOKO_TRAP_TRIGGER + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int i = liveFrom; i < traps.Count; i++)
                {
                    var trap = traps[i];

                    if (trap.Placed > now)
                        break;

                    if (!on(trap, now) || now < trap.Armed || Vector2.Distance(f.characterPosition, trap.At) > reach)
                        continue;

                    trap.Sprung = now;
                    f.holdCharacter(now, beat * DanmakusuBalance.ENOKO_HOLD_BEATS);
                    f.playStageSound("mokou_wall_hit");

                    int key = Id * 65536 + 60000 + Math.Min(i, 5000);

                    if (f.stageConsumed.Add(key))
                    {
                        f.LastHitCause = $"Enoko's trap {i}";
                        f.onDamaged(f.damagePerBullet, true, now);
                        f.hits.Add(new HitRecord(now, stage_hit, key, f.damagePerBullet));
                    }
                }

                while (liveFrom < traps.Count && traps[liveFrom].Known && (shut(traps[liveFrom], now - DanmakusuBalance.ENOKO_FADE_MS) || traps[liveFrom].Gone < now - DanmakusuBalance.ENOKO_FADE_MS))
                    liveFrom++;
            }

            public override void Draw(BulletField f, double t)
            {
                if (f.failed || t < Start)
                    return;

                // (while a trap holds the character it has a red glow round it)
                if (t >= f.forcedFreezeFrom && t < f.forcedFreezeUntil)
                {
                    double glowFade = DanmakusuBalance.ENOKO_HELD_GLOW_FADE_MS;
                    float glow = smoothStep((t - f.forcedFreezeFrom) / glowFade) * smoothStep((f.forcedFreezeUntil - t) / glowFade);
                    f.stageTop.Add(StageTexture.Glow, f.characterPosition, new Vector2(DanmakusuBalance.ENOKO_HELD_GLOW_SIZE), 0f, DanmakusuBalance.ENOKO_HELD_GLOW_ALPHA * glow, new Color4(1f, 0.25f, 0.2f, 1f), true);
                }

                float size = 2f * radius;

                for (int i = liveFrom; i < traps.Count; i++)
                {
                    var trap = traps[i];

                    if (trap.Placed > t)
                        break;

                    if (!trap.Known)
                        continue;

                    if (shut(trap, t))
                    {
                        // snapped shut: the closed jaws, a little pop, fading out
                        double since = t - trap.Sprung;

                        if (since < DanmakusuBalance.ENOKO_FADE_MS)
                        {
                            float u = (float)(since / DanmakusuBalance.ENOKO_FADE_MS);
                            float pop = 1f + 0.15f * MathF.Sin(MathF.Min(u * 4f, 1f) * MathF.PI);
                            f.stageBack.Add(StageTexture.BearTrapClosed, trap.At, new Vector2(size * pop), 0f, 1f - u * u, Color4.White);
                        }

                        continue;
                    }

                    // pushed out by a newer one: it fades out where it is
                    float leaving = t < trap.Gone ? 1f : 1f - smoothStep((t - trap.Gone) / DanmakusuBalance.ENOKO_FADE_MS);

                    if (leaving <= 0.002f)
                        continue;

                    if (t < trap.Armed)
                    {
                        // being put down: it comes in over a moment, half see-through, and the approach circle closes in on it
                        float arrival = smoothStep((t - trap.Placed) / DanmakusuBalance.FLANDRE_EYE_IN_MS);
                        float nearing = (float)((t - trap.Placed) / (trap.Armed - trap.Placed));
                        f.stageBack.Add(StageTexture.BearTrapOpen, trap.At, new Vector2(size), 0f, leaving * arrival * (0.35f + 0.35f * nearing), Color4.White);

                        double preempt = Math.Min(Math.Max(Period.Preempt, 100.0), trap.Armed - trap.Placed);
                        double approachFrom = trap.Armed - preempt;

                        if (t >= approachFrom)
                        {
                            float closing = (float)((t - approachFrom) / preempt);
                            float visible = smoothStep((t - approachFrom) / Math.Min(preempt * 0.4, 400.0));
                            f.stageRipple.AddSolidRing(trap.At, radius * (1f + DanmakusuBalance.FLANDRE_APPROACH_SCALE * (1f - closing)), 2f, 0.9f * visible * leaving, Color4.White);
                        }
                    }
                    else
                    {
                        // armed: there to stay, solid, with a faint red rim that says it bites
                        f.stageBack.Add(StageTexture.BearTrapOpen, trap.At, new Vector2(size), 0f, 0.95f * leaving, Color4.White);
                        f.stageRipple.AddSolidRing(trap.At, radius, 1.5f, 0.35f * leaving, new Color4(1f, 0.25f, 0.2f, 1f));
                    }
                }
            }
        }
    }
}
