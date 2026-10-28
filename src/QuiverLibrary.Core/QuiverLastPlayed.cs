using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace QuiverLibrary.Core
{
    /// <summary>
    /// Quiver's "last played" time: <c>LastPlayed.txt</c>, local time as <c>yyyy-MM-dd HH:mm:ss</c>,
    /// written when Quiver starts the app (<c>GameInfo.UpdateLastPlayedTime</c>) and read from the
    /// app folder to sort the library (<c>GameGridViewModel.GetLastPlayedTime</c>). Quiver writes it
    /// next to the executable but reads it from the app folder, so for an executable in a subfolder
    /// both places are read here, and it is always written to the app folder.
    /// </summary>
    public static class QuiverLastPlayed
    {
        public const string FileName = "LastPlayed.txt";

        public const string Format = "yyyy-MM-dd HH:mm:ss";

        /// <summary>The newest valid time in the app folder or the executable's folder; null when there is none.</summary>
        public static DateTime? Read(string appDir, string executable = null)
        {
            var fromApp = ReadFile(appDir);
            var exeDir = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
            var fromExe = exeDir == null || string.Equals(Normalize(exeDir), Normalize(appDir), StringComparison.OrdinalIgnoreCase)
                ? null
                : ReadFile(exeDir);
            return Newest(fromApp, fromExe);
        }

        /// <summary>Writes the time in Quiver's format; false when the folder is gone or not writable.</summary>
        public static bool Write(string appDir, DateTime localTime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appDir) || !Directory.Exists(appDir))
                {
                    return false;
                }

                File.WriteAllText(Path.Combine(appDir, FileName), localTime.ToString(Format, CultureInfo.InvariantCulture), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Times as both sides can store them (Quiver keeps whole seconds), so equal plays never look newer.</summary>
        public static DateTime? ToSeconds(DateTime? time)
        {
            return time == null ? (DateTime?)null : new DateTime(time.Value.Ticks - time.Value.Ticks % TimeSpan.TicksPerSecond, time.Value.Kind);
        }

        /// <summary>
        /// Which side must take the other's time, the newest one winning:
        /// <see cref="LastPlayedSync.UpdatePlaynite"/>, <see cref="LastPlayedSync.UpdateQuiver"/> or nothing.
        /// </summary>
        public static LastPlayedSync Compare(DateTime? playnite, DateTime? quiver)
        {
            var p = ToSeconds(playnite);
            var q = ToSeconds(quiver);
            if (q != null && (p == null || q.Value > p.Value))
            {
                return LastPlayedSync.UpdatePlaynite;
            }

            if (p != null && (q == null || p.Value > q.Value))
            {
                return LastPlayedSync.UpdateQuiver;
            }

            return LastPlayedSync.None;
        }

        private static DateTime? ReadFile(string dir)
        {
            try
            {
                var file = Path.Combine(dir, FileName);
                if (!File.Exists(file))
                {
                    return null;
                }

                return DateTime.TryParseExact(File.ReadAllText(file).Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time
                    : (DateTime?)null;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? Newest(DateTime? a, DateTime? b)
        {
            if (a == null)
            {
                return b;
            }

            return b == null || a.Value >= b.Value ? a : b;
        }

        private static string Normalize(string dir) => dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public enum LastPlayedSync
    {
        None,
        UpdatePlaynite,
        UpdateQuiver,
    }
}
