using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuiverLibrary.Core.Executables
{
    /// <summary>Decides which programs of an installed app Playnite offers.</summary>
    public static class ExecutablePlanner
    {
        /// <summary>Name of an automatically found launcher or settings tool.</summary>
        public const string SetupActionName = "Setup";

        /// <summary>More programs than this, found automatically, are a toolchain: none is offered (add a database entry).</summary>
        public const int MaxAutomaticOthers = 8;

        /// <summary>
        /// With a database entry, its play program (falling back to
        /// <see cref="QuiverLibraryData.ResolveExecutable"/> when its file is missing, unless it is
        /// searched for in the subfolders) and the other
        /// programs it lists that exist. Without one, <see cref="QuiverLibraryData.ResolveExecutable"/>
        /// plus every other candidate except setup/uninstall/crash helpers.
        /// Apps installed by a Windows installer only get their linked program.
        /// <paramref name="chosenPlay"/> (see <see cref="ChosenPath"/>), when it exists, replaces the play program.
        /// </summary>
        public static ExecutablePlan Plan(string dir, ExecutableEntry entry, string chosenPlay = null)
        {
            var plan = new ExecutablePlan();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return plan;
            }

            plan = PlanFromDatabase(dir, entry);
            var chosen = ChosenPath(dir, chosenPlay);
            if (chosen != null && File.Exists(chosen))
            {
                plan.Play = Target(null, chosen, null);
                plan.PlayMissing = false;
                plan.Others.RemoveAll(o => string.Equals(o.Path, chosen, StringComparison.OrdinalIgnoreCase));
            }

            return plan;
        }

        /// <summary>
        /// The game still has to be built or installed: the database's play program isn't there yet
        /// (<see cref="ExecutablePlan.PlayMissing"/>), or <see cref="SetupInfo.Built"/> is missing. A program
        /// the player chose counts as built.
        /// </summary>
        public static bool IsSetupPending(string dir, ExecutablePlan plan, SetupInfo setup, string chosenPlay = null)
        {
            if (plan == null || string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }

            if (plan.PlayMissing)
            {
                return true;
            }

            var chosen = ChosenPath(dir, chosenPlay);
            if (string.IsNullOrWhiteSpace(setup?.Built) || (chosen != null && File.Exists(chosen)))
            {
                return false;
            }

            return FindFile(dir, setup.Built) == null && !DirectoryExists(dir, setup.Built);
        }

        private static bool DirectoryExists(string dir, string relative)
        {
            try
            {
                if (StartsWithGamesDir(relative))
                {
                    var games = GamesDir(dir);
                    return games != null && Directory.Exists(Path.Combine(games, relative.Substring(GamesDirVariable.Length).TrimStart('\\', '/')));
                }

                return !Path.IsPathRooted(relative) && Directory.Exists(Path.Combine(dir, relative));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Stands for <see cref="GamesDir"/> in the database's paths, arguments and values: games that can't be
        /// built or installed inside their app folder (the compiler refuses it) go to a folder of their own there.
        /// </summary>
        public const string GamesDirVariable = "{GamesDir}";

        /// <summary>Name of the shared games folder, next to Quiver's Apps folder.</summary>
        public const string GamesFolderName = "games";

        /// <summary>
        /// The games folder chosen in the settings ("Games folder"); empty or null = the default
        /// <c>&lt;Quiver&gt;\games</c>. Environment variables are expanded. Set by the plugin from its settings.
        /// </summary>
        public static string GamesDirOverride { get; set; }

        /// <summary>
        /// The shared games folder of the app in <paramref name="appDir"/>: <see cref="GamesDirOverride"/> when set,
        /// otherwise <c>games</c> next to the folder that holds the apps (Quiver's <c>Apps</c>), e.g.
        /// <c>&lt;Quiver&gt;\games</c>. Null when it can't be told.
        /// </summary>
        public static string GamesDir(string appDir)
        {
            var custom = GamesDirOverride;
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return Environment.ExpandEnvironmentVariables(custom.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            try
            {
                var apps = string.IsNullOrWhiteSpace(appDir) ? null : Path.GetDirectoryName(appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var root = string.IsNullOrEmpty(apps) ? null : Path.GetDirectoryName(apps);
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, GamesFolderName);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Replaces <see cref="GamesDirVariable"/> (left as it is when the folder can't be told).</summary>
        public static string ExpandFolders(string text, string appDir)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(GamesDirVariable, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return text;
            }

            var games = GamesDir(appDir);
            return games == null ? text : Regex.Replace(text, Regex.Escape(GamesDirVariable), m => games, RegexOptions.IgnoreCase);
        }

        private static bool StartsWithGamesDir(string path)
        {
            return path != null && path.StartsWith(GamesDirVariable, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A program the player chose, stored relative to the app folder when it's inside (so it
        /// survives moving Quiver's library) and absolute otherwise. Null for nothing chosen.
        /// </summary>
        public static string ChosenPath(string dir, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
            {
                return null;
            }

            try
            {
                return Path.IsPathRooted(stored) ? stored : Path.Combine(dir, stored);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>How <see cref="ChosenPath"/> stores <paramref name="path"/>.</summary>
        public static string StoredChosenPath(string dir, string path)
        {
            return (string.IsNullOrWhiteSpace(dir) ? null : RelativePath(dir, path)) ?? path;
        }

        private static ExecutablePlan PlanFromDatabase(string dir, ExecutableEntry entry)
        {
            var plan = new ExecutablePlan();
            if (File.Exists(Path.Combine(dir, QuiverLibraryData.WindowsInstallerReceiptFileName)))
            {
                plan.Play = Target(null, QuiverLibraryData.ResolveExecutable(dir), null);
                return plan;
            }

            if (entry != null && entry.HasLaunchInfo)
            {
                plan.Play = entry.Play == null ? null : Target(dir, null, FindFile(dir, entry.Play), entry.Play);
                plan.PlayMissing = entry.Play != null && plan.Play == null;
                if (plan.Play == null && entry.Play?.SearchSubfolders != true)
                {
                    plan.Play = Target(null, QuiverLibraryData.ResolveExecutable(dir), null);
                }

                foreach (var other in entry.Others ?? new List<ExecutableTarget>())
                {
                    var target = other == null || string.IsNullOrWhiteSpace(other.Name) ? null : Target(dir, other.Name, FindFile(dir, other), other);
                    if (target != null && !plan.Others.Any(o => string.Equals(o.Name, target.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        plan.Others.Add(target);
                    }
                }

                return plan;
            }

            plan.Play = Target(null, QuiverLibraryData.ResolveExecutable(dir), null);
            plan.Others = AutomaticOthers(dir, plan.Play?.Path);
            if (plan.Others.Count > MaxAutomaticOthers)
            {
                // Bundled build tools (compilers, SDKs), not the app's own programs.
                plan.Others.Clear();
            }
            return plan;
        }

        /// <summary>
        /// The candidates (as Quiver finds them) other than <paramref name="play"/> and the helpers.
        /// A single launcher is called <see cref="SetupActionName"/>; the rest use their file name.
        /// </summary>
        public static List<LaunchTarget> AutomaticOthers(string dir, string play)
        {
            var candidates = QuiverLibraryData.FindExecutableCandidates(dir, recursive: false);
            if (candidates.Count == 0)
            {
                candidates = QuiverLibraryData.FindExecutableCandidates(dir, recursive: true);
            }

            var others = candidates
                .Where(c => !string.Equals(c, play, StringComparison.OrdinalIgnoreCase) && !QuiverLibraryData.IsHelperExecutable(c))
                .ToList();
            var singleLauncher = others.Count(QuiverLibraryData.IsLauncherExecutable) == 1;
            return others
                .Select(c => new LaunchTarget
                {
                    Name = singleLauncher && QuiverLibraryData.IsLauncherExecutable(c) ? SetupActionName : Path.GetFileNameWithoutExtension(c),
                    Path = c,
                })
                .ToList();
        }

        /// <summary>The file a database target names (see <see cref="ExecutableTarget.SearchSubfolders"/>).</summary>
        public static string FindFile(string dir, ExecutableTarget target)
        {
            return target == null ? null : target.SearchSubfolders ? FindInSubfolders(dir, target.File) : FindFile(dir, target.File);
        }

        /// <summary>
        /// The file a database path names, relative to <paramref name="dir"/>; wildcards are allowed
        /// in the file name and the folder names, and the highest version wins (of a folder first,
        /// skipping the ones without a match). Null when nothing matches.
        /// </summary>
        public static string FindFile(string dir, string relative)
        {
            try
            {
                if (StartsWithGamesDir(relative))
                {
                    dir = GamesDir(dir);
                    relative = relative.Substring(GamesDirVariable.Length).TrimStart('\\', '/');
                }

                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                {
                    return null;
                }

                var parts = relative.Replace('/', Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
                return FindFile(dir, parts, 0);
            }
            catch
            {
                return null;
            }
        }

        private static string FindFile(string folder, string[] parts, int index)
        {
            var part = parts[index];
            var wildcard = part.IndexOfAny(new[] { '*', '?' }) >= 0;
            if (index == parts.Length - 1)
            {
                var path = Path.Combine(folder, part);
                return !wildcard ? (File.Exists(path) ? path : null) : Directory.Exists(folder) ? HighestMatch(folder, part) : null;
            }

            if (!wildcard)
            {
                var next = Path.Combine(folder, part);
                return Directory.Exists(next) ? FindFile(next, parts, index + 1) : null;
            }

            if (!Directory.Exists(folder))
            {
                return null;
            }

            var regex = WildcardRegex(part);
            return Directory.GetDirectories(folder, part)
                .Where(d => regex.IsMatch(Path.GetFileName(d)))
                .OrderByDescending(d => Path.GetFileName(d), NaturalComparer.Instance)
                .Select(d => FindFile(d, parts, index + 1))
                .FirstOrDefault(f => f != null);
        }

        /// <summary>
        /// The file called <paramref name="pattern"/> (wildcards allowed) in <paramref name="dir"/> or
        /// its subfolders, level by level so the shallowest wins (then the highest version, of the file
        /// or of its folders: <c>versions\1.0.1</c> over <c>versions\1.0.0</c>); links are not followed.
        /// </summary>
        public static string FindInSubfolders(string dir, string pattern)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !Directory.Exists(dir))
                {
                    return null;
                }

                var level = new List<string> { dir };
                while (level.Count > 0)
                {
                    var found = level
                        .Select(folder => HighestMatch(folder, pattern))
                        .Where(f => f != null)
                        .OrderByDescending(f => Path.GetFileName(f), NaturalComparer.Instance)
                        .ThenByDescending(f => f, NaturalComparer.Instance)
                        .FirstOrDefault();
                    if (found != null)
                    {
                        return found;
                    }

                    level = level.SelectMany(Subfolders).ToList();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> Subfolders(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Select(d => d.FullName)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private static string HighestMatch(string folder, string pattern)
        {
            var regex = WildcardRegex(pattern);
            return Directory.GetFiles(folder, pattern)
                .Where(f => regex.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), NaturalComparer.Instance)
                .FirstOrDefault();
        }

        // Directory.GetFiles/GetDirectories also match 8.3 short names and longer extensions; check the long name exactly.
        private static Regex WildcardRegex(string pattern)
        {
            return new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);
        }

        /// <summary>Path relative to <paramref name="dir"/>, for the database and for Playnite's <c>{InstallDir}</c>.</summary>
        public static string RelativePath(string dir, string path)
        {
            var root = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : null;
        }

        private static LaunchTarget Target(string name, string path, string arguments)
        {
            return path == null
                ? null
                : new LaunchTarget { Name = name, Path = path, Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim() };
        }

        private static LaunchTarget Target(string dir, string name, string path, ExecutableTarget source)
        {
            string program = null;
            if (path != null && !string.IsNullOrWhiteSpace(source.Launch))
            {
                program = FindFile(Path.GetDirectoryName(path), source.Launch.Trim());
                if (program == null)
                {
                    // Not built that far (or another layout): the launcher itself, as it is.
                    return Target(name, path, null);
                }
            }

            var target = Target(name, path, ExpandVariables(source.Arguments, dir, path));
            if (target != null)
            {
                target.HideConsole = source.HideConsole;
                target.Program = program;
                target.Borderless = source.Borderless;
            }

            foreach (var variable in source.Environment ?? new List<EnvironmentVariable>())
            {
                if (target != null && !string.IsNullOrWhiteSpace(variable?.Name))
                {
                    target.Environment[variable.Name.Trim()] = ExpandVariables(variable.Value, dir, path) ?? string.Empty;
                }
            }

            return target;
        }

        /// <summary>
        /// Playnite's language as an Xbox 360 language number (1 English, 5 Spanish…), for the play program's
        /// arguments and environment (ReXGlue's <c>REX_USER_LANGUAGE</c>). The Playnite extension replaces
        /// it when the game starts, since only it knows Playnite's language.
        /// </summary>
        public const string XboxLanguageVariable = "{XboxLanguage}";

        /// <summary>Replaces <paramref name="variable"/> (any letter case) with <paramref name="value"/>.</summary>
        public static string ReplaceVariable(string text, string variable, string value)
        {
            return string.IsNullOrEmpty(text)
                ? text
                : Regex.Replace(text, Regex.Escape(variable), m => value ?? string.Empty, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Replaces <c>{AppDir}</c> (the app folder), <c>{ExeDir}</c> (the folder of <paramref name="program"/>)
        /// and <see cref="GamesDirVariable"/>, then environment variables (<c>%LOCALAPPDATA%</c>, <c>%PATH%</c>).
        /// </summary>
        public static string ExpandVariables(string text, string dir, string program)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            text = ExpandFolders(text, dir);

            var exeDir = program == null ? null : Path.GetDirectoryName(program);
            text = Regex.Replace(text, Regex.Escape("{AppDir}"), m => (dir ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, Regex.Escape("{ExeDir}"), m => (exeDir ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), RegexOptions.IgnoreCase);
            return Environment.ExpandEnvironmentVariables(text);
        }

        /// <summary>Compares digit runs by value, so "0.1.60" sorts after "0.1.57" and "10" after "9".</summary>
        public class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();

            private static readonly Regex Chunks = new Regex(@"\d+|\D+");

            public int Compare(string x, string y)
            {
                var a = Chunks.Matches(x ?? string.Empty);
                var b = Chunks.Matches(y ?? string.Empty);
                for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
                {
                    var left = a[i].Value;
                    var right = b[i].Value;
                    int result;
                    if (char.IsDigit(left[0]) && char.IsDigit(right[0]))
                    {
                        left = left.TrimStart('0');
                        right = right.TrimStart('0');
                        result = left.Length != right.Length
                            ? left.Length.CompareTo(right.Length)
                            : string.CompareOrdinal(left, right);
                    }
                    else
                    {
                        result = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                    }

                    if (result != 0)
                    {
                        return result;
                    }
                }

                return a.Count.CompareTo(b.Count);
            }
        }
    }
}
