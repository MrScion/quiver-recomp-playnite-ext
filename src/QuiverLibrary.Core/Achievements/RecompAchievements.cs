using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>One achievement or trophy of a recomp, as its own files describe it.</summary>
    public class RecompAchievement
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>bronze, silver, gold or platinum for trophies; null otherwise.</summary>
        public string TrophyType { get; set; }

        public bool Hidden { get; set; }

        /// <summary>Gamerscore (Xbox 360), when the game defines it.</summary>
        public int? Points { get; set; }

        public bool Unlocked { get; set; }

        /// <summary>Local time of the unlock, when the app records it.</summary>
        public DateTime? UnlockTime { get; set; }

        /// <summary>Full path of its icon, or null.</summary>
        public string IconPath { get; set; }
    }

    /// <summary>How to read achievements whose definitions come from the game itself (ReXGlue).</summary>
    public class AchievementReadOptions
    {
        /// <summary>For achievements whose name isn't known: "Achievement {0}", {0} = id.</summary>
        public string PlaceholderName { get; set; }

        /// <summary>Xbox 360 language of the texts (<see cref="Xdbf.LanguageEnglish"/> = 1, Spanish = 5, ...).</summary>
        public int Language { get; set; } = Xdbf.LanguageEnglish;

        /// <summary>
        /// Folder for what is taken out of the game (its achievements data and icons), so the game's program is read once;
        /// null reads it every time and gives no icons.
        /// </summary>
        public string CacheDir { get; set; }
    }

    /// <summary>
    /// Reads a recomp's achievements from the file named in its <see cref="AchievementsInfo"/>.
    /// Experimental: only the formats seen in played installs are understood.
    /// </summary>
    public static class RecompAchievements
    {
        /// <summary>True when the app's achievements can be read (a known file in a known format).</summary>
        public static bool CanRead(AchievementsInfo info)
        {
            return info != null && !string.IsNullOrWhiteSpace(info.File) &&
                   (info.Kind == AchievementsInfo.Ps4Trophy || info.Kind == AchievementsInfo.ReXGlue);
        }

        /// <summary>
        /// The first of <see cref="AchievementsInfo.File"/>'s places that exists, relative to the app folder
        /// and with environment variables expanded; null when the app hasn't written it yet.
        /// </summary>
        public static string FindFile(string appDir, AchievementsInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.File))
            {
                return null;
            }

            return info.File.Split(';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => Environment.ExpandEnvironmentVariables(ExecutablePlanner.ExpandFolders(p, appDir)))
                .Select(p => Path.IsPathRooted(p) || string.IsNullOrWhiteSpace(appDir) ? p : Path.Combine(appDir, p))
                .Select(Existing)
                .FirstOrDefault(p => p != null);
        }

        /// <summary>
        /// The file, or with <c>*</c>/<c>?</c> in its name (<c>achievements\*.toml</c>: the title id isn't always known)
        /// the most recently written match; null when there is none.
        /// </summary>
        private static string Existing(string path)
        {
            try
            {
                var name = Path.GetFileName(path);
                if (name.IndexOfAny(new[] { '*', '?' }) < 0)
                {
                    return File.Exists(path) ? path : null;
                }

                var dir = Path.GetDirectoryName(path);
                return dir == null || !Directory.Exists(dir)
                    ? null
                    : new DirectoryInfo(dir).GetFiles(name).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).FirstOrDefault();
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The app's achievements, or null when they can't be read.</summary>
        public static List<RecompAchievement> Read(string appDir, AchievementsInfo info, AchievementReadOptions options = null)
        {
            if (!CanRead(info))
            {
                return null;
            }

            var file = FindFile(appDir, info);
            if (file == null)
            {
                return null;
            }

            switch (info.Kind)
            {
                case AchievementsInfo.Ps4Trophy:
                    return Ps4Trophies.Read(file);
                case AchievementsInfo.ReXGlue:
                    return ReXGlueAchievements.Read(file, appDir, options ?? new AchievementReadOptions());
                default:
                    return null;
            }
        }
    }
}
