using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Byakuren's passive: every fourth ricochet star fired in her sections, whoever fires it, has a beam for a tail: the last eight beats of its way, narrowing to nothing behind it. It hurts like a beam.
        // A star flies straight between its bounces and where it has been is a closed form of time, so the tail is worked out afresh from the star's own flight every frame: from the star, through every
        // place it turned exactly, to where it was eight beats ago. (It stays there after the star has flown off the screen, until its end has followed it out.)
        // ======================================================================================================================================================

        private readonly List<Vector2> tailPoints = new List<Vector2>();
        private readonly List<float> tailWidths = new List<float>();
        private readonly List<double> tailTurns = new List<double>();
        private const int tail_steps = 16;

        /// <summary>Has this star of a ring with tails got one? (by the ring's time as well as its number, as the rings can be small: which one it is stays the same in a replay and a seek)</summary>
        private static bool hasTail(BulletEmitter emitter, int bullet) => (Math.Abs((long)emitter.Time) + bullet) % (emitter.TailEvery > 0 ? emitter.TailEvery : DanmakusuBalance.BYAKUREN_STAR_TAIL_EVERY) == 0;

        /// <summary>Works out the tail of a star at <paramref name="now"/> into <see cref="tailPoints"/> and <see cref="tailWidths"/>; false when there is none to show.</summary>
        private bool starTail(BulletEmitter emitter, int bullet, double now)
        {
            tailPoints.Clear();
            tailWidths.Clear();

            if (((emitter.ConsumedMask | emitter.AbsorbedMask | emitter.HiddenMask) & (1UL << bullet)) != 0)
                return false;

            double ringTime = emitter.WithoutFrozenTime(effects.Effective(emitter, now));
            double back = Math.Max(ringTime - emitter.TailMs, emitter.Time);

            if (ringTime <= back)
                return false;

            tailTurns.Clear();
            emitter.TurnsBetween(bullet, back, ringTime, tailTurns);

            // (a bullet that the ops of a boss act on does not fly straight between its turns: Yumemi's tails, which are pulled along with it, are followed in steps; only while an op can act on it, and only if
            // it has not flown straight all the way (the same way at both ends, and as far as that takes it), as the steps are costly: Mima's stars all have tails)
            if (emitter.Ops != null && opsActBetween(emitter.Ops, back, ringTime) && (tailTurns.Count > 0 || !flewStraight(emitter, bullet, back, ringTime)))
            {
                for (int i = 1; i < tail_steps; i++)
                    tailTurns.Add(back + (ringTime - back) * i / tail_steps);

                tailTurns.Sort();
            }

            addTailPoint(emitter, bullet, ringTime, ringTime);

            for (int i = tailTurns.Count - 1; i >= 0; i--)
                addTailPoint(emitter, bullet, tailTurns[i], ringTime);

            addTailPoint(emitter, bullet, back, ringTime);
            return true;
        }

        /// <summary>Has the bullet flown straight from <paramref name="from"/> to <paramref name="to"/>: the same way at both ends, and as far as that way at its speed takes it (nothing turned, stopped or moved it between)?</summary>
        private static bool flewStraight(BulletEmitter emitter, int bullet, double from, double to)
        {
            emitter.Evaluate(bullet, from, out Vector2 start, out Vector2 startWay, out _);
            emitter.Evaluate(bullet, to, out Vector2 end, out Vector2 endWay, out _);

            if (Vector2.Dot(startWay, endWay) < 0.9999f)
                return false;

            Vector2 straight = start + startWay * (emitter.SpeedOf(bullet) * (float)(to - from));
            return (end - straight).LengthSquared < 4f;
        }

        /// <summary>Does an op act on what flies between <paramref name="from"/> and <paramref name="to"/>? (One that acted before only turned it: it flies straight from there.)</summary>
        private static bool opsActBetween(TrajectoryOp[] ops, double from, double to)
        {
            foreach (var op in ops)
            {
                if (op.Time <= to && Math.Max(op.End, op.Time) >= from)
                    return true;
            }

            return false;
        }

        private void addTailPoint(BulletEmitter emitter, int bullet, double at, double ringTime)
        {
            emitter.Evaluate(bullet, at, out Vector2 position, out _, out _);

            tailPoints.Add(position);
            tailWidths.Add(emitter.VisualRadius * DanmakusuBalance.BYAKUREN_STAR_TAIL_WIDTH * (1f - (float)((ringTime - at) / emitter.TailMs)));
        }

        /// <summary>The tails, drawn like the other beams (while the beams are handed to the renderer).</summary>
        private void addStarTails(double drawTime, ref int beams, ref int pointTotal)
        {
            if (failed || palette.Length == 0)
                return;

            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];

                if (emitter.TailMs <= 0 || emitter.Retired)
                    continue;

                int style = styleOf(emitter);

                for (int b = 0; b < emitter.Count; b++)
                {
                    if (hasTail(emitter, b) && starTail(emitter, b, drawTime))
                        addPlainBeam(tailPoints, tailWidths[0], palette[style % palette.Length], borderPalette[style % borderPalette.Length], ref beams, ref pointTotal, tailWidths);
                }
            }
        }

        /// <summary>Is the character inside the tail of a star right now?</summary>
        private bool touchesStarTail(double now)
        {
            for (int k = 0; k < activeCount; k++)
            {
                var emitter = emitters[active[k]];

                if (emitter.TailMs <= 0 || emitter.Retired)
                    continue;

                // (a tail whose star is further from the player than the tail can be long cannot touch them: most of them, and working one out is costly)
                double ringTime = emitter.WithoutFrozenTime(effects.Effective(emitter, now));
                float reach = emitter.Speed * (float)emitter.TailMs * 3f + emitter.VisualRadius * 2f + DanmakusuBalance.PLAYER_HITBOX_RADIUS;

                for (int b = 0; b < emitter.Count; b++)
                {
                    if (!hasTail(emitter, b))
                        continue;

                    emitter.Evaluate(b, ringTime, out Vector2 head, out _, out bool alive);

                    if (!alive || (head - characterPosition).LengthSquared > reach * reach || !starTail(emitter, b, now))
                        continue;

                    for (int i = 1; i < tailPoints.Count; i++)
                    {
                        // (as wide as the tail is at the nearest point of this straight piece)
                        Vector2 from = tailPoints[i - 1], along = tailPoints[i] - from;
                        float u = Math.Clamp(Vector2.Dot(characterPosition - from, along) / Math.Max(along.LengthSquared, 1e-6f), 0f, 1f);
                        float width = tailWidths[i - 1] + (tailWidths[i] - tailWidths[i - 1]) * u;

                        if (Vector2.Distance(characterPosition, from + along * u) <= width * DanmakusuBalance.BULLET_HITBOX_SCALE + DanmakusuBalance.PLAYER_HITBOX_RADIUS)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
