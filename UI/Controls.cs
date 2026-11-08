using System;
using System.IO;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    /// <summary>How the player moves the character.</summary>
    public enum ControlMode
    {
        /// <summary>The character is where the cursor is; key 1 and key 2 freeze it (holding both and pressing again moves the freeze to the cursor).</summary>
        Cursor,

        /// <summary>
        /// W, A, S, D (or the arrow keys) move the character, Shift slowly; pressing Space puts it where the cursor is, and while Space is held it stays on the cursor. No freeze keys.
        /// (A replay only has the cursor: in it the character follows the cursor.)
        /// </summary>
        Keyboard,
    }

    /// <summary>The control mode the player has chosen in the boss menu (kept in a small file next to osu!'s settings, so it stays from one start of the game to the next).</summary>
    public static class Controls
    {
        private static ControlMode? mode;

        private static string file => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu", "danmakusu_controls.txt");

        public static ControlMode Mode
        {
            get
            {
                if (mode != null)
                    return mode.Value;

                mode = ControlMode.Cursor;

                try
                {
                    if (File.Exists(file) && Enum.TryParse(File.ReadAllText(file).Trim(), out ControlMode saved))
                        mode = saved;
                }
                catch
                {
                    // (it cannot be read: the cursor, as always)
                }

                return mode.Value;
            }
            set
            {
                mode = value;

                try
                {
                    File.WriteAllText(file, value.ToString());
                }
                catch
                {
                    // (it cannot be saved: it is kept for this start of the game only)
                }
            }
        }

        /// <summary>The slowest and the fastest the keyboard can move the character, as a multiple of <see cref="DanmakusuBalance.KEYBOARD_SPEED"/>.</summary>
        public const double MIN_SPEED = 0.25, MAX_SPEED = 2.0;

        private static double? speed;

        private static string speedFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osu", "danmakusu_keyboard_speed.txt");

        /// <summary>How fast the keyboard moves the character (a multiple of the usual speed; Shift halves it as always), chosen with the slider in the boss menu and kept like <see cref="Mode"/>.</summary>
        public static double KeyboardSpeed
        {
            get
            {
                if (speed != null)
                    return speed.Value;

                speed = 1.0;

                try
                {
                    if (File.Exists(speedFile) && double.TryParse(File.ReadAllText(speedFile).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double saved))
                        speed = Math.Clamp(saved, MIN_SPEED, MAX_SPEED);
                }
                catch
                {
                    // (it cannot be read: the usual speed)
                }

                return speed.Value;
            }
            set
            {
                speed = Math.Clamp(value, MIN_SPEED, MAX_SPEED);

                try
                {
                    File.WriteAllText(speedFile, speed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch
                {
                    // (it cannot be saved: it is kept for this start of the game only)
                }
            }
        }
    }
}
