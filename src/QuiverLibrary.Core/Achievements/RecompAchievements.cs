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

        public bool Unlocked { get; set; }

        /// <summary>Local time of the unlock, when the app records it.</summary>
        public DateTime? UnlockTime { get; set; }

        /// <summary>Full path of its icon, or null.</summary>
        public string IconPath { get; set; }
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
            return info != null && !string.IsNullOrWhiteSpace(info.File) && info.Kind == AchievementsInfo.Ps4Trophy;
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
                .Select(p => Environment.ExpandEnvironmentVariables(p))
                .Select(p => Path.IsPathRooted(p) || string.IsNullOrWhiteSpace(appDir) ? p : Path.Combine(appDir, p))
                .FirstOrDefault(File.Exists);
        }

        /// <summary>The app's achievements, or null when they can't be read.</summary>
        public static List<RecompAchievement> Read(string appDir, AchievementsInfo info)
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
                default:
                    return null;
            }
        }
    }
}
