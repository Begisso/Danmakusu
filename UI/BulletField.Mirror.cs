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
        /// How an attack of Seija's mirrors (2026-11-28, "first attack is full mirror. Second attack is horizontal mirror (only horizontal arrows are warning), third attack is vertical mirror (only
        /// vertical arrows warning), fourth attack only playfield is mirrored, fifth attack - playfield stays as but player is mirrored"): attack by attack through her section, in this order, again
        /// from the first after the fifth. Until then every attack turned the screen upside down (Full).
        /// </summary>
        public enum MirrorKind
        {
            /// <summary>The screen turns upside down (mirrored along both axes), the character with it.</summary>
            Full,

            /// <summary>The screen is mirrored left to right, the character with it.</summary>
            Horizontal,

            /// <summary>The screen is mirrored top to bottom, the character with it.</summary>
            Vertical,

            /// <summary>The screen turns upside down, but the character stays under the mouse.</summary>
            ViewOnly,

            /// <summary>The screen stays as it is; the character is at the mirror image of the mouse (through the middle of the screen).</summary>
            PlayerOnly,
        }

        /// <summary>How attack <paramref name="attack"/> (0, 1, 2... of her section) mirrors.</summary>
        public static MirrorKind MirrorKindOf(int attack) => (MirrorKind)(((attack % 5) + 5) % 5);

        /// <summary>How far into the mirror of window <paramref name="i"/> it is at <paramref name="now"/> (0..1): it comes in smoothly at the start of the attack and goes back where it ends.</summary>
        private double mirrorAmount(int i, double now)
        {
            var (start, end) = mirrorWindows[i];
            double turn = DanmakusuBalance.MIRROR_TURN_MS;

            if (now < start || now > end + turn)
                return 0;

            if (turn <= 0)
                return now <= end ? 1 : 0; // (no transition: mirrored exactly during the attack)

            double x = now <= end ? Math.Min((now - start) / turn, 1) : 1 - (now - end) / turn;
            x = Math.Clamp(x, 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>
        /// How far the whole playfield is turned round its centre at <paramref name="now"/>, in degrees: 0 normally, 180 (the screen mirrored along both axes) during an attack of Seija's that turns it
        /// (<see cref="MirrorKind.Full"/>, <see cref="MirrorKind.ViewOnly"/>). It turns smoothly in at the start of an attack and back where it ends. A pure function of time.
        /// </summary>
        public float MirrorAngleAt(double now)
        {
            double amount = 0;

            for (int i = 0; i < mirrorWindows.Length; i++)
            {
                if (mirrorKinds[i] is MirrorKind.Full or MirrorKind.ViewOnly)
                    amount = Math.Max(amount, mirrorAmount(i, now));
            }

            return (float)(amount * 180.0);
        }

        /// <summary>
        /// How the playfield is mirrored left to right (x) and top to bottom (y) at <paramref name="now"/>: 1 as it is, -1 mirrored; in between it folds over smoothly, never quite flat (2026-11-28:
        /// <see cref="MirrorKind.Horizontal"/>, <see cref="MirrorKind.Vertical"/>).
        /// </summary>
        public Vector2 MirrorFlipAt(double now)
        {
            float x = 1f, y = 1f;

            for (int i = 0; i < mirrorWindows.Length; i++)
            {
                if (mirrorKinds[i] is not (MirrorKind.Horizontal or MirrorKind.Vertical))
                    continue;

                double amount = mirrorAmount(i, now);

                if (amount <= 0)
                    continue;

                float folded = MathF.Cos((float)(amount * Math.PI));
                folded = MathF.Abs(folded) < 0.02f ? (amount < 0.5 ? 0.02f : -0.02f) : folded;

                if (mirrorKinds[i] == MirrorKind.Horizontal)
                    x = Math.Min(x, folded);
                else
                    y = Math.Min(y, folded);
            }

            return new Vector2(x, y);
        }

        /// <summary>How the cursor (and the character, which follows it) is placed at <paramref name="now"/> (2026-11-28).</summary>
        public enum MirrorCursor
        {
            /// <summary>As the screen is mirrored, the cursor is too: it is where the mouse would be without the mirror, and the mirror shows it on the other side (as it always was).</summary>
            WithView,

            /// <summary>The cursor stays under the mouse, the screen mirrored round it (<see cref="MirrorKind.ViewOnly"/>).</summary>
            UnderMouse,

            /// <summary>The cursor is at the mirror image of the mouse through the middle of the screen, which is not mirrored (<see cref="MirrorKind.PlayerOnly"/>: all at once, as the attack starts).</summary>
            Mirrored,
        }

        public MirrorCursor MirrorCursorAt(double now)
        {
            for (int i = 0; i < mirrorWindows.Length; i++)
            {
                var (start, end) = mirrorWindows[i];

                if (mirrorKinds[i] == MirrorKind.PlayerOnly && now >= start && now < end)
                    return MirrorCursor.Mirrored;

                if (mirrorKinds[i] == MirrorKind.ViewOnly && mirrorAmount(i, now) > 0)
                    return MirrorCursor.UnderMouse;
            }

            return MirrorCursor.WithView;
        }

        /// <summary>How much the screen or the character is mirrored at <paramref name="now"/> (0..1), whichever way: her red glow round the player, the cursor's trail hidden.</summary>
        public float MirrorAmountAt(double now)
        {
            double amount = 0;

            for (int i = 0; i < mirrorWindows.Length; i++)
                amount = Math.Max(amount, mirrorKinds[i] == MirrorKind.PlayerOnly ? (now >= mirrorWindows[i].Start && now < mirrorWindows[i].End ? 1 : 0) : mirrorAmount(i, now));

            return (float)amount;
        }

        /// <summary>The kind of the attack of hers that starts or ends at <paramref name="turn"/> (as InversionAct has its turns), or Full.</summary>
        public MirrorKind MirrorKindAt(double turn)
        {
            for (int i = 0; i < mirrorWindows.Length; i++)
            {
                if (Math.Abs(mirrorWindows[i].Start - turn) < 1 || Math.Abs(mirrorWindows[i].End - turn) < 1)
                    return mirrorKinds[i];
            }

            return MirrorKind.Full;
        }

        /// <summary>How the screen is mirrored left to right and top to bottom now (see <see cref="MirrorFlipAt"/>); set at the start of every frame with <see cref="mirrorRadians"/>.</summary>
        private Vector2 mirrorFlip = Vector2.One, previousMirrorFlip = Vector2.One;

        private MirrorCursor mirrorCursor, previousMirrorCursor;

        /// <summary>
        /// A point of this field as the screen shows it (after the playfield is turned, or mirrored) -> the point where it is without the turn. (The turn is round the playfield's centre,
        /// which is the centre of this field; <paramref name="local"/> is in the field's coordinates without its shake.)
        /// </summary>
        private Vector2 unturn(Vector2 local)
        {
            if ((mirrorRadians == 0 && mirrorFlip == Vector2.One) || NothingTurnsProbe)
                return local;

            Vector2 centre = DrawSize / 2;
            Vector2 d = (local - centre) * mirrorFlip;
            float cos = MathF.Cos(mirrorRadians), sin = MathF.Sin(mirrorRadians);
            return centre + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
        }

        /// <summary>
        /// (Tools/SectionCheck) Nothing turns the field: the bench has no playfield round it to turn, so there is no turn to undo. (Undone all the same, the screen's bounds were turned round the corner of the
        /// field at every turn of Seija's, every ring was started over in a screen moved half a screen away and her stars jumped across the character: 11 hits in 40 s that the game never has, 2026-11-15.)
        /// </summary>
        public static bool NothingTurnsProbe;

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
