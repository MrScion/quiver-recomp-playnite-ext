using System;
using System.Collections.Generic;
using System.IO;

namespace QuiverLibrary.Core.Gamepad
{
    /// <summary>
    /// Detects the "exit game" combination on XInput gamepads: Back (Select, View, −) held and then
    /// Start pressed, in that order. Start first, or both in the same poll, doesn't count.
    /// </summary>
    public sealed class ExitCombo
    {
        /// <summary>XInput button bits (<c>XINPUT_GAMEPAD_START</c>, <c>XINPUT_GAMEPAD_BACK</c>).</summary>
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;

        public const int MaxPads = 4;

        private readonly ushort[] _previous = new ushort[MaxPads];

        /// <summary>Feeds one poll of a pad; true when the combination has just been completed.</summary>
        public bool Update(int pad, ushort buttons)
        {
            if (pad < 0 || pad >= MaxPads)
            {
                return false;
            }

            var previous = _previous[pad];
            _previous[pad] = buttons;
            var startPressed = (buttons & Start) != 0 && (previous & Start) == 0;
            var backHeldBefore = (previous & Back) != 0 && (buttons & Back) != 0;
            return startPressed && backHeldBefore;
        }

        /// <summary>Forgets a pad's buttons (disconnected).</summary>
        public void Reset(int pad)
        {
            if (pad >= 0 && pad < MaxPads)
            {
                _previous[pad] = 0;
            }
        }
    }

    /// <summary>Which running programs belong to a game, by the folder they run from.</summary>
    public static class GameProcessFilter
    {
        /// <summary>Normalized folders (full path, trailing separator) with duplicates and empty ones left out.</summary>
        public static List<string> Folders(IEnumerable<string> folders)
        {
            var result = new List<string>();
            foreach (var folder in folders ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string full;
                try
                {
                    full = Path.GetFullPath(folder.Trim());
                }
                catch (Exception)
                {
                    continue;
                }

                // A drive root (or anything as short) would take in unrelated programs.
                if (Path.GetPathRoot(full)?.TrimEnd('\\', '/').Length >= full.TrimEnd('\\', '/').Length)
                {
                    continue;
                }

                full = full.TrimEnd('\\', '/') + "\\";
                if (!result.Exists(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(full);
                }
            }

            return result;
        }

        /// <summary>True when <paramref name="program"/> is inside one of <paramref name="folders"/> (from <see cref="Folders"/>).</summary>
        public static bool IsInside(string program, IEnumerable<string> folders)
        {
            if (string.IsNullOrWhiteSpace(program))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(program);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var folder in folders)
            {
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
