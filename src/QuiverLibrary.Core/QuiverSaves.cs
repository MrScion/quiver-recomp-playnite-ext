using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Core
{
    public class SaveFolder
    {
        public string Path { get; set; }

        /// <summary>False when the app hasn't saved anything there yet.</summary>
        public bool Exists { get; set; }
    }

    /// <summary>Where an app keeps its saves, from its <see cref="UserDataInfo"/>.</summary>
    public static class QuiverSaves
    {
        /// <summary>
        /// The app's save folders that exist, the one it uses first; when none exists yet, the one it
        /// will use (<see cref="SaveFolder.Exists"/> false). Empty when the database doesn't say.
        /// </summary>
        /// <param name="appDir">The app folder, base of <see cref="UserDataInfo.Local"/>.</param>
        /// <param name="playProgram">The play program, next to which the portable marker sits; may be null.</param>
        /// <param name="localFromProgram">
        /// <see cref="UserDataInfo.Local"/> is relative to the play program's folder instead: for games
        /// in a folder the player chose (<see cref="ExecutableTarget.SearchSubfolders"/>).
        /// </param>
        public static List<SaveFolder> Find(string appDir, string playProgram, UserDataInfo userData, bool localFromProgram)
        {
            // Save files (and patterns) are shown by the folder they're in.
            var candidates = Candidates(appDir, playProgram, userData, localFromProgram, false)
                .Select(p => IsFilePattern(p) ? Path.GetDirectoryName(p) : p)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var existing = candidates.Where(Exists).Select(p => new SaveFolder { Path = p, Exists = true }).ToList();
            if (existing.Count > 0 || candidates.Count == 0)
            {
                return existing;
            }

            return new List<SaveFolder> { new SaveFolder { Path = candidates[0], Exists = false } };
        }

        /// <summary>
        /// Every folder (or save file, or pattern) the app may keep its saves in, existing or not, for
        /// backups: both the portable and the usual ones. A portable folder that is the game's own folder
        /// (saves mixed with the game files) is left out, so a backup never copies the whole game.
        /// </summary>
        public static List<string> ForBackup(string appDir, string playProgram, UserDataInfo userData, bool localFromProgram)
        {
            return Candidates(appDir, playProgram, userData, localFromProgram, true);
        }

        private static List<string> Candidates(string appDir, string playProgram, UserDataInfo userData, bool localFromProgram, bool skipGameFolder)
        {
            if (userData == null)
            {
                return new List<string>();
            }

            var programDir = string.IsNullOrWhiteSpace(playProgram) ? null : Path.GetDirectoryName(playProgram);
            var localBase = localFromProgram ? programDir : appDir;
            var local = userData.Local == null || string.IsNullOrWhiteSpace(localBase)
                ? new List<string>()
                : WithSaves(Path.Combine(localBase, userData.Local), userData);
            if (skipGameFolder)
            {
                local.RemoveAll(p => string.Equals(p, localBase.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
            }

            var appData = string.IsNullOrWhiteSpace(userData.AppData)
                ? new List<string>()
                : WithSaves(Environment.ExpandEnvironmentVariables(userData.AppData), userData);

            bool localFirst;
            if (userData.IsBuiltin)
            {
                localFirst = true;
            }
            else if (userData.IsUnsupported)
            {
                localFirst = false;
            }
            else
            {
                localFirst = programDir != null && File.Exists(Path.Combine(programDir, userData.Marker));
            }

            return (localFirst ? local.Concat(appData) : appData.Concat(local))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The data folder joined with each of <see cref="UserDataInfo.SaveItems"/>.</summary>
        private static List<string> WithSaves(string folder, UserDataInfo userData)
        {
            return userData.SaveItems
                .Select(item => (item.Length == 0 ? folder : Path.Combine(folder, item)).TrimEnd(Path.DirectorySeparatorChar))
                .ToList();
        }

        /// <summary>A save file or a wildcard pattern rather than a folder (<c>default.sav</c>, <c>*.sav</c>).</summary>
        private static bool IsFilePattern(string path)
        {
            var name = Path.GetFileName(path);
            return name.IndexOfAny(new[] { '*', '?' }) >= 0 || (Path.HasExtension(name) && !Exists(path));
        }

        private static bool Exists(string folder)
        {
            try
            {
                return Directory.Exists(folder);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
