using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.Danmakusu.Scoring;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Danmakusu.Bosses;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        #region Seija's mirrored screen

        /// <summary>Is there a boss who turns the screen round (so the playfield must be allowed to turn)?</summary>
        public bool HasMirror => mirrorWindows.Length > 0;

        /// <summary>
        /// How far the whole playfield is turned round its centre at <paramref name="now"/>, in degrees: 0 normally, 180 (the screen mirrored along both axes, cursor
        /// included) during an attack of Seija's. It turns smoothly in at the start of an attack and back where it ends. A pure function of time.
        /// </summary>
        public float MirrorAngleAt(double now)
        {
            if (mirrorWindows.Length == 0)
                return 0;

            double turn = DanmakusuBalance.MIRROR_TURN_MS;
            double amount = 0;

            foreach (var (start, end) in mirrorWindows)
            {
                if (now < start || now > end + turn)
                    continue;

                if (turn <= 0)
                {
                    // no transition: the screen is mirrored exactly during the attack
                    if (now <= end)
                        amount = 1;

                    continue;
                }

                double x = now <= end ? Math.Min((now - start) / turn, 1) : 1 - (now - end) / turn;
                x = Math.Clamp(x, 0, 1);
                amount = Math.Max(amount, x * x * (3 - 2 * x));
            }

            return (float)(amount * 180.0);
        }

        /// <summary>
        /// A point of this field as the screen shows it (after the playfield is turned) -> the point where it is without the turn. (The turn is round the playfield's centre,
        /// which is the centre of this field; <paramref name="local"/> is in the field's coordinates without its shake.)
        /// </summary>
        private Vector2 unturn(Vector2 local)
        {
            if (mirrorRadians == 0)
                return local;

            Vector2 centre = DrawSize / 2;
            Vector2 d = local - centre;
            float cos = MathF.Cos(mirrorRadians), sin = MathF.Sin(mirrorRadians);
            return centre + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
        }

        /// <summary>The centre the playfield turns round, in this field's coordinates.</summary>
        public Vector2 MirrorCentre => DrawSize / 2;

        /// <summary>
        /// How far the picture of the boss is turned on the screen, in degrees: Seija's is mirrored along both axes (turned by 180) while she warns, and turns to normal as the attack starts.
        /// </summary>
        private float bossArtTurn(double now)
        {
            var current = latestBoss(now);

            if (current == null || current.Boss.Mechanic != BossMechanic.ScreenMirror)
                return 0f;

            double turnMs = DanmakusuBalance.MIRROR_ART_TURN_MS; // (a variable, so that the constant can be tuned to 0 without the compiler warning about unreachable code)

            if (turnMs <= 0)
                return now < current.ShootStart ? 180f : 0f;

            double reversed = Math.Clamp((current.ShootStart + turnMs - now) / turnMs, 0, 1);
            return (float)(reversed * 180.0);
        }

        #endregion
    }
}
