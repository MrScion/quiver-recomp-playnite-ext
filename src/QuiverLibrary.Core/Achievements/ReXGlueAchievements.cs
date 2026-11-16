using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Core.Achievements
{
    /// <summary>
    /// The ReXGlue runtime's achievements (Xbox 360 recompilations). It keeps the unlocked ones in
    /// <c>&lt;user data&gt;\achievements\&lt;title id&gt;.toml</c>: a <c>[unlocked.&lt;id&gt;]</c> table with
    /// <c>filetime = &lt;Windows FILETIME&gt;</c> for each (seen in SF3 3rd Strike Online Edition and Spider-Man: Edge
    /// of Time, 2026-11-05). The names are only inside the game's <c>.xex</c>; Xbox Live Arcade games also bring an
    /// <c>ArcadeInfo.xml</c> listing every achievement's id and icon.
    /// </summary>
    public static class ReXGlueAchievements
    {
        private static readonly Regex Table = new Regex(@"^\s*\[\s*unlocked\.(\d+)\s*\]\s*$", RegexOptions.Compiled);
        private static readonly Regex FileTime = new Regex(@"^\s*filetime\s*=\s*(\d+)\s*$", RegexOptions.Compiled);

        /// <summary>Unlocked achievement ids and their local unlock time (null when the file has none).</summary>
        public static Dictionary<int, DateTime?> ReadUnlocks(string tomlPath)
        {
            var unlocks = new Dictionary<int, DateTime?>();
            int? current = null;
            foreach (var line in File.ReadAllLines(tomlPath))
            {
                var table = Table.Match(line);
                if (table.Success)
                {
                    current = int.Parse(table.Groups[1].Value, CultureInfo.InvariantCulture);
                    unlocks[current.Value] = null;
                    continue;
                }

                if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    current = null;
                    continue;
                }

                var time = FileTime.Match(line);
                if (current.HasValue && time.Success && long.TryParse(time.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var fileTime))
                {
                    try
                    {
                        unlocks[current.Value] = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
            }

            return unlocks;
        }

        /// <summary>Every achievement id an <c>ArcadeInfo.xml</c> lists, with its icon's full path when that file exists.</summary>
        public static Dictionary<int, string> ReadArcadeInfo(string arcadeInfoPath)
        {
            var icons = new Dictionary<int, string>();
            var dir = Path.GetDirectoryName(arcadeInfoPath);
            var xml = XDocument.Load(arcadeInfoPath);
            // The default TitleInfo (no locale) lists the achievements.
            foreach (var element in xml.Descendants().Where(e => e.Name.LocalName == "AchievementInfo"))
            {
                if (!int.TryParse((string)element.Attribute("ID"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || icons.ContainsKey(id))
                {
                    continue;
                }

                var image = (string)element.Attribute("ImagePath");
                var path = string.IsNullOrWhiteSpace(image) ? null : Path.Combine(dir, image);
                icons[id] = path != null && File.Exists(path) ? path : null;
            }

            return icons;
        }

        /// <summary>
        /// The achievements of an app. From the game's XDBF when its <c>.xex</c> can be read (names, descriptions,
        /// gamerscore and icons); otherwise those its <c>ArcadeInfo.xml</c> lists, with placeholder names
        /// (<see cref="AchievementReadOptions.PlaceholderName"/>). Plus, either way, any other unlocked one.
        /// </summary>
        public static List<RecompAchievement> Read(string tomlPath, string appDir, AchievementReadOptions options)
        {
            options = options ?? new AchievementReadOptions();
            var unlocks = ReadUnlocks(tomlPath);
            var titleId = Path.GetFileNameWithoutExtension(tomlPath);
            var fromGame = FromGame(appDir, titleId, options);
            if (fromGame != null)
            {
                foreach (var a in fromGame)
                {
                    var id = int.Parse(a.Id, CultureInfo.InvariantCulture);
                    a.Unlocked = unlocks.ContainsKey(id);
                    a.UnlockTime = unlocks.TryGetValue(id, out var time) ? time : null;
                }

                var format = string.IsNullOrWhiteSpace(options.PlaceholderName) ? "Achievement {0}" : options.PlaceholderName;
                var known = new HashSet<string>(fromGame.Select(a => a.Id));
                fromGame.AddRange(unlocks.Keys
                    .Where(id => !known.Contains(id.ToString(CultureInfo.InvariantCulture)))
                    .OrderBy(id => id)
                    .Select(id => new RecompAchievement
                    {
                        Id = id.ToString(CultureInfo.InvariantCulture),
                        Name = string.Format(CultureInfo.CurrentCulture, format, id),
                        Unlocked = true,
                        UnlockTime = unlocks[id],
                    }));
                return fromGame;
            }

            return FromArcadeInfo(unlocks, appDir, options.PlaceholderName);
        }

        /// <summary>
        /// The achievements the game defines (its XDBF, the <c>.xex</c> resource named after the title id), or null when
        /// it can't be read (no <c>default.xex</c>, LZX compression, ...). The XDBF and the icons are kept in
        /// <see cref="AchievementReadOptions.CacheDir"/>, so the program is decrypted once.
        /// </summary>
        public static List<RecompAchievement> FromGame(string appDir, string titleId, AchievementReadOptions options)
        {
            byte[] xdbfBytes = null;
            var cached = string.IsNullOrWhiteSpace(options.CacheDir) ? null : Path.Combine(options.CacheDir, titleId + ".xdbf");
            try
            {
                if (cached != null && File.Exists(cached))
                {
                    xdbfBytes = File.ReadAllBytes(cached);
                }
                else
                {
                    var xex = string.IsNullOrWhiteSpace(appDir) ? null : ExecutablePlanner.FindInSubfolders(appDir, "default.xex");
                    if (xex == null)
                    {
                        return null;
                    }

                    xdbfBytes = Xex.ReadResource(File.ReadAllBytes(xex), titleId);
                    if (xdbfBytes == null)
                    {
                        return null;
                    }

                    if (cached != null)
                    {
                        Directory.CreateDirectory(options.CacheDir);
                        File.WriteAllBytes(cached, xdbfBytes);
                    }
                }

                var xdbf = new Xdbf(xdbfBytes);
                var spa = xdbf.Achievements(options.Language);
                if (spa.Count == 0)
                {
                    return null;
                }

                var iconDir = string.IsNullOrWhiteSpace(options.CacheDir) ? null : Path.Combine(options.CacheDir, titleId);
                return spa.Select(a => new RecompAchievement
                {
                    Id = a.Id.ToString(CultureInfo.InvariantCulture),
                    Name = a.Name,
                    Description = string.IsNullOrEmpty(a.Description) ? a.LockedDescription : a.Description,
                    Points = a.Gamerscore,
                    IconPath = SaveIcon(xdbf, a.ImageId, iconDir),
                }).ToList();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException ||
                                      e is NotSupportedException || e is ArgumentException ||
                                      e is System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        /// <summary>Writes an XDBF image as <c>&lt;id&gt;.png</c> once; its path, or null.</summary>
        private static string SaveIcon(Xdbf xdbf, uint imageId, string iconDir)
        {
            if (iconDir == null)
            {
                return null;
            }

            var path = Path.Combine(iconDir, imageId.ToString(CultureInfo.InvariantCulture) + ".png");
            if (!File.Exists(path))
            {
                var png = xdbf.Image(imageId);
                if (png == null)
                {
                    return null;
                }

                Directory.CreateDirectory(iconDir);
                File.WriteAllBytes(path, png);
            }

            return path;
        }

        /// <summary>The achievements an <c>ArcadeInfo.xml</c> lists plus the other unlocked ones, with placeholder names.</summary>
        private static List<RecompAchievement> FromArcadeInfo(Dictionary<int, DateTime?> unlocks, string appDir, string placeholderName)
        {
            var icons = new Dictionary<int, string>();
            var arcadeInfo = string.IsNullOrWhiteSpace(appDir) ? null : ExecutablePlanner.FindInSubfolders(appDir, "ArcadeInfo.xml");
            if (arcadeInfo != null)
            {
                try
                {
                    icons = ReadArcadeInfo(arcadeInfo);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Xml.XmlException)
                {
                }
            }

            var format = string.IsNullOrWhiteSpace(placeholderName) ? "Achievement {0}" : placeholderName;
            return icons.Keys.Union(unlocks.Keys)
                .OrderBy(id => id)
                .Select(id => new RecompAchievement
                {
                    Id = id.ToString(CultureInfo.InvariantCulture),
                    Name = string.Format(CultureInfo.CurrentCulture, format, id),
                    Unlocked = unlocks.ContainsKey(id),
                    UnlockTime = unlocks.TryGetValue(id, out var time) ? time : null,
                    IconPath = icons.TryGetValue(id, out var icon) ? icon : null,
                })
                .ToList();
        }
    }
}
