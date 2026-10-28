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
                plan.Others.RemoveAll(o => string.Equals(o.Path, chosen, StringComparison.OrdinalIgnoreCase));
            }

            return plan;
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
                plan.Play = entry.Play == null ? null : Target(null, FindFile(dir, entry.Play), entry.Play.Arguments);
                if (plan.Play == null && entry.Play?.SearchSubfolders != true)
                {
                    plan.Play = Target(null, QuiverLibraryData.ResolveExecutable(dir), null);
                }

                foreach (var other in entry.Others ?? new List<ExecutableTarget>())
                {
                    var target = other == null || string.IsNullOrWhiteSpace(other.Name) ? null : Target(other.Name, FindFile(dir, other), other.Arguments);
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
        /// in the file name only, and the highest version wins. Null when nothing matches.
        /// </summary>
        public static string FindFile(string dir, string relative)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                {
                    return null;
                }

                var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
                var pattern = Path.GetFileName(path);
                if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
                {
                    return File.Exists(path) ? path : null;
                }

                var folder = Path.GetDirectoryName(path);
                return Directory.Exists(folder) ? HighestMatch(folder, pattern) : null;
            }
            catch
            {
                return null;
            }
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
            // Directory.GetFiles also matches 8.3 short names and longer extensions; check the long name exactly.
            var regex = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);
            return Directory.GetFiles(folder, pattern)
                .Where(f => regex.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), NaturalComparer.Instance)
                .FirstOrDefault();
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
