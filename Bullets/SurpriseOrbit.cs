using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Kogasa Tatara (2026-11-23): the circles and sliders of her section turn round the middle of the playfield - every first one clockwise, every second the other way round, KOGASA_ORBIT_DEGREES a beat -
    /// standing where the map has them at the moment they are due (so they are hit where they are, and come there turning), and stay KOGASA_HOLD_BEATS after they are over, still turning. The whole object
    /// turns round the middle as one piece, itself turning as it goes (since 2026-11-25; a slider kept the way it lay until then). The planner (what they shoot), the zones (what hurts), the playfield
    /// (where they are drawn) and the FallLayer (the ones that stay) all go by this.
    /// </summary>
    public static class SurpriseOrbit
    {
        /// <summary>How one of hers turns: which way (1 clockwise on the screen, -1 the other way), when it is due, a beat of its section, and until when it stays.</summary>
        public readonly struct Turn
        {
            public readonly float Way;
            public readonly double Due;
            public readonly double Beat;
            public readonly double Until;

            public Turn(float way, double due, double beat, double until)
            {
                Way = way;
                Due = due;
                Beat = beat;
                Until = until;
            }

            /// <summary>How far the object has turned round the middle at <paramref name="t"/> (radians; clockwise on the screen when positive).</summary>
            public float Angle(double t) => Way * DanmakusuBalance.KOGASA_ORBIT_DEGREES * MathF.PI / 180f * (float)((t - Due) / Beat);

            /// <summary>The same in degrees (a drawable's rotation).</summary>
            public float Degrees(double t) => Angle(t) * 180f / MathF.PI;

            /// <summary>
            /// Where a point of the object (<paramref name="point"/>, where the map has it) is at <paramref name="t"/>: the whole object turned round the middle of the playfield, itself turning as it goes
            /// (2026-11-25, "osu objects themselves turn around center too": until then it was only moved, keeping the way it lay).
            /// </summary>
            public Vector2 Place(Vector2 point, double t) => turn(point, Angle(t));

            /// <summary>Where on the map a point that is at <paramref name="point"/> at <paramref name="t"/> belongs: the turn undone (for the zones: whether the player touches the turned object).</summary>
            public Vector2 Unplace(Vector2 point, double t) => turn(point, -Angle(t));

            private static Vector2 turn(Vector2 point, float angle)
            {
                Vector2 middle = OsuPlayfield.BASE_SIZE / 2f;
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                Vector2 from = point - middle;
                return middle + new Vector2(from.X * cos - from.Y * sin, from.X * sin + from.Y * cos);
            }

            /// <summary>How far the object's head is moved at <paramref name="t"/> from where the map has it (<paramref name="head"/>).</summary>
            public Vector2 Offset(Vector2 head, double t) => Place(head, t) - head;
        }

        /// <summary>Every circle and slider of the map in one of her sections, and how it turns.</summary>
        public static Dictionary<OsuHitObject, Turn> Of(IBeatmap beatmap, IEnumerable<BossPeriod> periods)
        {
            var turns = new Dictionary<OsuHitObject, Turn>();

            foreach (var period in periods)
            {
                if (period.Boss?.Mechanic != Bosses.BossMechanic.Surprise)
                    continue;

                double beat = Math.Max(period.BeatLength, 1);
                int count = 0;

                foreach (var hitObject in beatmap.HitObjects.OfType<OsuHitObject>().Where(h => h is HitCircle or Slider && h.StartTime >= period.ShootStart && h.StartTime < period.End).OrderBy(h => h.StartTime))
                {
                    if (turns.ContainsKey(hitObject))
                        continue;

                    turns[hitObject] = new Turn(count++ % 2 == 0 ? 1f : -1f, hitObject.StartTime, beat, hitObject.GetEndTime() + beat * DanmakusuBalance.KOGASA_HOLD_BEATS);
                }
            }

            return turns;
        }
    }
}
