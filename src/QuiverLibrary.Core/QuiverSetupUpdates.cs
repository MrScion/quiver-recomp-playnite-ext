using System;
using System.IO;
using System.Linq;

namespace QuiverLibrary.Core
{
    public enum SetupUpdateCheck
    {
        /// <summary>Nothing known about the app's download: remember it without warning.</summary>
        FirstSeen,

        /// <summary>The download is the one already seen.</summary>
        Unchanged,

        /// <summary>Quiver downloaded another version or build: the game has to be set up again.</summary>
        Updated,
    }

    /// <summary>
    /// Tells when Quiver has replaced the download of an app whose game is set up by hand afterwards
    /// (an installer, or a launcher that builds the game): an update from Quiver only replaces what
    /// it downloaded, never the game the installer put elsewhere.
    /// </summary>
    public static class QuiverSetupUpdates
    {
        /// <summary>Written by Quiver next to <c>version.txt</c>: the downloaded file's name, then its SHA-256.</summary>
        public const string BuildFileName = "installed-build.txt";

        /// <summary>
        /// What identifies the app's current download: its version (<c>version.txt</c>) and the
        /// download's hash from <see cref="BuildFileName"/>, so a release replaced under the same tag
        /// counts too. Null when Quiver left neither.
        /// </summary>
        public static string Fingerprint(string appDir, string version)
        {
            string build = null;
            try
            {
                var file = string.IsNullOrEmpty(appDir) ? null : Path.Combine(appDir, BuildFileName);
                if (file != null && File.Exists(file))
                {
                    var lines = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    build = lines.Count > 0 ? lines[lines.Count - 1] : null;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            if (version == null && build == null)
            {
                return null;
            }

            return (version ?? string.Empty) + "|" + (build ?? string.Empty);
        }

        /// <summary>
        /// The readable part of a <see cref="Fingerprint"/>: the version, or the start of the download's hash when
        /// Quiver left no version; null for none.
        /// </summary>
        public static string Describe(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                return null;
            }

            var cut = fingerprint.IndexOf('|');
            var version = cut < 0 ? fingerprint : fingerprint.Substring(0, cut);
            if (version.Length > 0)
            {
                return version;
            }

            var hash = cut < 0 ? string.Empty : fingerprint.Substring(cut + 1);
            return hash.Length == 0 ? null : (hash.Length > 8 ? hash.Substring(0, 8) : hash);
        }

        public static SetupUpdateCheck Compare(string known, string current)
        {
            if (string.IsNullOrEmpty(current))
            {
                return SetupUpdateCheck.Unchanged;
            }

            if (string.IsNullOrEmpty(known))
            {
                return SetupUpdateCheck.FirstSeen;
            }

            return string.Equals(known, current, StringComparison.OrdinalIgnoreCase) ? SetupUpdateCheck.Unchanged : SetupUpdateCheck.Updated;
        }
    }
}
