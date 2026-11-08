using System;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>
    /// Mugetsu's vortex: in her attacks every bullet on the screen goes round her, as fast round her as it flies, the even bullets of a ring one way and the odd ones the other. It is a closed form of time: the
    /// bullet's own flight, turned round her by an angle that grows by its speed over its distance from her (its speed round her is then its speed, at any distance). The angle is worked out along the straight line
    /// the bullet was fired on (exact for a bullet that flies straight, near enough for one that something else turns); once an attack is over the bullet flies on straight, turned by what the attack gave it.
    /// Every bullet goes round a middle of its own that does not move: where she stood when it was fired, if that was in the attack, or where she stood when the attack began, for what was on the screen already.
    /// </summary>
    public sealed class Swirl
    {
        public double[] Starts { get; init; } = [];
        public double[] Ends { get; init; } = [];

        /// <summary>Which boss of the section (0 or 1) each attack is hers as.</summary>
        public int[] Ranks { get; init; } = [];

        /// <summary>Where she is in each attack while it is on (the field keeps it up to date): a ring fired then takes it as the middle it goes round.</summary>
        public Vector2[] Centres { get; init; } = [];

        /// <summary>Where she stood when each attack began: the middle for what was on the screen already (the field takes it then).</summary>
        public Vector2[] Grabbed { get; init; } = [];

        /// <summary>The field has taken <see cref="Grabbed"/> for the attack.</summary>
        public bool[] Placed { get; init; } = [];

        /// <summary>Is <paramref name="time"/> in one of her attacks, and where is she in it now (the middle of a ring fired then)?</summary>
        public bool CentreAt(double time, out Vector2 centre)
        {
            for (int k = 0; k < Starts.Length; k++)
            {
                if (time >= Starts[k] && time < Ends[k])
                {
                    centre = Centres[k];
                    return true;
                }
            }

            centre = default;
            return false;
        }

        /// <summary>Does the life of a ring fired at <paramref name="time"/>, living <paramref name="lifetime"/> ms, meet one of her attacks?</summary>
        public bool Meets(double time, double lifetime)
        {
            for (int k = 0; k < Starts.Length; k++)
            {
                if (time < Ends[k] && time + lifetime > Starts[k])
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Turns a bullet that was fired at <paramref name="fired"/> from <paramref name="start"/> at <paramref name="velocity"/> round her for the attacks between then and <paramref name="time"/>: where it is,
        /// and the way it faces (in an attack, its way round her as well). <paramref name="own"/> is the middle the ring took when it was fired in an attack (<paramref name="ownKnown"/>); in an attack that began
        /// after it was fired it goes round where she stood as that began. Returns false when no attack has touched it yet.
        /// </summary>
        public bool Apply(double fired, Vector2 start, Vector2 velocity, int bullet, double time, ref Vector2 position, ref Vector2 direction, Vector2 own = default, bool ownKnown = false)
        {
            float speed = velocity.Length;

            if (speed < 1e-6f)
                return false;

            float sign = bullet % 2 == 0 ? DanmakusuBalance.MUGETSU_SWIRL : -DanmakusuBalance.MUGETSU_SWIRL;
            Vector2 lineFrom = start, lineWay = velocity;
            bool touched = false;

            for (int k = 0; k < Starts.Length; k++)
            {
                if (Starts[k] >= time)
                    break;

                double from = Math.Max(Starts[k], fired), to = Math.Min(Ends[k], time);

                if (to <= from)
                    continue;

                Vector2 centre = ownKnown && fired >= Starts[k] && fired < Ends[k] ? own : Grabbed[k];
                float angle = sign * (along(lineFrom, lineWay, centre, to - fired) - along(lineFrom, lineWay, centre, from - fired));
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

                position = centre + turn(position - centre, cos, sin);
                direction = turn(direction, cos, sin);

                if (time < Ends[k])
                {
                    // (in the attack it also goes round her: its way is its own way plus the way round, as fast)
                    Vector2 outward = position - centre;
                    float distance = outward.Length;

                    if (distance > 1e-3f)
                    {
                        Vector2 moving = direction + new Vector2(-outward.Y, outward.X) / distance * sign;

                        if (moving.LengthSquared > 1e-12f)
                            direction = Vector2.Normalize(moving);
                    }
                }

                // (after the attack the line it flies on is turned with it: the next attack goes on from that)
                lineFrom = centre + turn(lineFrom - centre, cos, sin);
                lineWay = turn(lineWay, cos, sin);
                touched = true;
            }

            return touched;
        }

        /// <summary>
        /// How far round her a thing on the line has gone by <paramref name="age"/> (ms after it was fired), in radians, up to a constant: the sum of its speed over its distance from her, which along a straight line
        /// is the log of (the distance, kept off zero by <see cref="DanmakusuBalance.MUGETSU_SWIRL_NEAR"/>, plus how far along the line it is past its nearest point to her).
        /// </summary>
        private static float along(Vector2 lineFrom, Vector2 lineWay, Vector2 centre, double age)
        {
            Vector2 offset = lineFrom + lineWay * (float)age - centre;
            Vector2 way = Vector2.Normalize(lineWay);
            float near = DanmakusuBalance.MUGETSU_SWIRL_NEAR;

            return MathF.Log(Math.Max(Vector2.Dot(way, offset) + MathF.Sqrt(offset.LengthSquared + near * near), 1e-4f));
        }

        private static Vector2 turn(Vector2 v, float cos, float sin) => new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
