using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuiverLibrary.Core
{
    /// <summary>
    /// Reads Quiver Launcher's library files and the install state of each app.
    /// The install and executable rules mirror Quiver's own Windows logic
    /// (<c>GameStatusService</c>, <c>GameInstallationService.HasCompletePortableInstallation</c>/
    /// <c>FindExecutableCandidates</c>, <c>WindowsInstallerService</c>, <c>GameInfo.LoadSelectedExecutable</c>).
    /// </summary>
    public class QuiverLibraryData
    {
        public const string AppsJsonFileName = "apps.json";
        public const string SettingsJsonFileName = "settings.json";
        public const string AppsFolderName = "Apps";
        public const string IncompleteInstallFileName = "install-incomplete.txt";
        public const string SelectedExecutableFileName = "selected_executable.txt";
        public const string VersionFileName = "version.txt";
        public const string WindowsInstallerReceiptFileName = "windows-installer.json";

        private static readonly string[] CandidatePatterns = { "*.exe", "*.bat", "*.cmd" };

        // Not part of Quiver's rules: used only to break a tie between several
        // candidates when nothing was picked in Quiver yet.
        private static readonly string[] HelperExecutablePrefixes =
        {
            "unins", "uninstall", "setup", "vc_redist", "vcredist", "dxsetup", "dxwebsetup",
            "crashpad", "crashreport", "crash_handler", "rexcrashreporter", "sentry", "createdump",
            "updater", "update"
        };

        // Also not Quiver's: a launcher or settings tool loses a tie against the program that
        // starts the game directly, whatever was picked in Quiver.
        private static readonly string[] LauncherExecutableWords = { "launcher", "launch", "config", "settings" };

        public string DataRoot { get; }

        public QuiverLibraryData(string dataRoot)
        {
            DataRoot = dataRoot;
        }

        public string AppsJsonPath => Path.Combine(DataRoot, AppsJsonFileName);

        /// <summary>
        /// Quiver's cache of the releases quiverlauncher.com verified. While it is under 24 hours old,
        /// Quiver doesn't ask GitHub for the release (<c>StartupVersionResolver.FromCatalog</c>), so an
        /// app whose release files were never cached gets "No eligible download was found" from
        /// <c>--download</c>. Deleting it is safe: Quiver downloads it again on its next run.
        /// </summary>
        public string CatalogReleaseStatusCachePath => Path.Combine(DataRoot, "Cache", "catalog-release-status.json");

        public bool HasAppsJson => File.Exists(AppsJsonPath);

        /// <summary><c>settings.json</c> → <c>AppsPath</c>, or <c>&lt;root&gt;\Apps</c> when empty.</summary>
        public string AppsFolder
        {
            get
            {
                if (QuiverJson.TryReadFile<QuiverSettingsFile>(Path.Combine(DataRoot, SettingsJsonFileName), out var settings) &&
                    !string.IsNullOrWhiteSpace(settings.AppsPath))
                {
                    return settings.AppsPath;
                }

                return Path.Combine(DataRoot, AppsFolderName);
            }
        }

        /// <summary>Apps with a folder name, one per folder name (it is Quiver's unique key; names repeat).</summary>
        public List<QuiverApp> LoadApps()
        {
            if (!HasAppsJson)
            {
                throw new FileNotFoundException("Quiver library file not found.", AppsJsonPath);
            }

            var list = QuiverJson.ReadFile<QuiverAppList>(AppsJsonPath);
            return list?.Apps?
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.FolderName))
                .GroupBy(a => a.FolderName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList() ?? new List<QuiverApp>();
        }

        public QuiverApp FindApp(string folderName)
        {
            return LoadApps().FirstOrDefault(a => string.Equals(a.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetInstallDirectory(QuiverApp app, string appsFolder)
        {
            return !string.IsNullOrWhiteSpace(app.InstallPath)
                ? app.InstallPath
                : Path.Combine(appsFolder, app.FolderName);
        }

        public QuiverAppState GetState(QuiverApp app, string appsFolder)
        {
            var dir = GetInstallDirectory(app, appsFolder);
            var state = new QuiverAppState { App = app, InstallDirectory = dir };
            try
            {
                if (!Directory.Exists(dir))
                {
                    return state;
                }

                if (File.Exists(Path.Combine(dir, WindowsInstallerReceiptFileName)))
                {
                    var receipt = ReadReceipt(dir);
                    state.IsInstalled = receipt != null && IsUsableExecutable(receipt.ExecutablePath);
                    state.Version = state.IsInstalled ? NullIfBlank(receipt.ReleaseTag) : null;
                    return state;
                }

                state.IsInstalled = !File.Exists(Path.Combine(dir, IncompleteInstallFileName)) &&
                                    FindExecutableCandidates(dir, recursive: true).Count > 0;
                state.Version = state.IsInstalled ? ReadVersion(dir) : null;
            }
            catch
            {
                state.IsInstalled = false;
                state.Version = null;
            }

            return state;
        }

        /// <summary>
        /// A check for <see cref="Cli.QuiverCli.Run"/>'s watcher: true once the app is installed and
        /// its folder (file count, total size, newest write) didn't change since the previous call,
        /// so Quiver is never stopped while still writing files.
        /// </summary>
        public Func<bool> InstalledAndSettledCheck(QuiverApp app, string appsFolder)
        {
            string previous = null;
            return () =>
            {
                var state = GetState(app, appsFolder);
                var current = state.IsInstalled ? FolderFingerprint(state.InstallDirectory) : null;
                var settled = current != null && current == previous;
                previous = current;
                return settled;
            };
        }

        /// <summary>File count, total size and newest write time of a folder tree; null when unreadable.</summary>
        public static string FolderFingerprint(string dir)
        {
            try
            {
                long count = 0, size = 0, newest = 0;
                foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    count++;
                    size += file.Length;
                    newest = Math.Max(newest, file.LastWriteTimeUtc.Ticks);
                }

                return $"{count}|{size}|{newest}";
            }
            catch
            {
                return null;
            }
        }

        public List<QuiverAppState> GetStates()
        {
            var appsFolder = AppsFolder;
            return LoadApps().Select(a => GetState(a, appsFolder)).ToList();
        }

        /// <summary>
        /// The program to start: the installer receipt target, else the candidates of the top
        /// folder (subfolders only when the top folder has none, as in Quiver). With several,
        /// setup/uninstall/crash helpers and then launchers are dropped, and the executable
        /// picked in Quiver (<c>selected_executable.txt</c>) only settles a remaining tie.
        /// Returns null if it still can't be decided without asking the user.
        /// </summary>
        public static string ResolveExecutable(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    return null;
                }

                if (File.Exists(Path.Combine(dir, WindowsInstallerReceiptFileName)))
                {
                    var linked = ReadReceipt(dir)?.ExecutablePath;
                    return IsUsableExecutable(linked) ? linked : null;
                }

                var candidates = FindExecutableCandidates(dir, recursive: false);
                if (candidates.Count == 0)
                {
                    candidates = FindExecutableCandidates(dir, recursive: true);
                }

                if (candidates.Count <= 1)
                {
                    return candidates.FirstOrDefault();
                }

                var games = PreferWhere(candidates, c => !IsHelperExecutable(c));
                games = PreferWhere(games, c => !IsLauncherExecutable(c));
                if (games.Count == 1)
                {
                    return games[0];
                }

                var selected = ReadSelectedExecutable(dir);
                return games.FirstOrDefault(g => string.Equals(g, selected, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The existing file named in <c>selected_executable.txt</c>, or null. Quiver stores an
        /// absolute path, which goes stale when the apps folder is moved; it is then looked
        /// for at the same place under the app's current folder.
        /// </summary>
        public static string ReadSelectedExecutable(string dir)
        {
            try
            {
                var selectedFile = Path.Combine(dir, SelectedExecutableFileName);
                if (!File.Exists(selectedFile))
                {
                    return null;
                }

                var saved = File.ReadAllText(selectedFile).Trim();
                if (saved.Length == 0 || !Path.IsPathRooted(saved))
                {
                    return null;
                }

                if (File.Exists(saved))
                {
                    return saved;
                }

                var appFolder = Path.DirectorySeparatorChar + Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar;
                var at = saved.LastIndexOf(appFolder, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    return null;
                }

                var moved = Path.Combine(dir, saved.Substring(at + appFolder.Length));
                return File.Exists(moved) ? moved : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The items that pass the test, or all of them when none does.</summary>
        private static List<string> PreferWhere(List<string> items, Func<string, bool> test)
        {
            var preferred = items.Where(test).ToList();
            return preferred.Count > 0 ? preferred : items;
        }

        /// <summary>
        /// <c>*.exe</c>/<c>*.bat</c>/<c>*.cmd</c>, shallowest first. Like Quiver, never follows
        /// directory links and skips Wine/Proton prefixes.
        /// </summary>
        public static List<string> FindExecutableCandidates(string dir, bool recursive)
        {
            var found = new List<string>();
            if (!Directory.Exists(dir) || IsCompatibilityDirectory(dir))
            {
                return found;
            }

            Collect(dir, recursive, found);
            return found
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => PathDepth(dir, f))
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void Collect(string dir, bool recursive, List<string> found)
        {
            try
            {
                foreach (var pattern in CandidatePatterns)
                {
                    // A three-letter extension pattern also matches longer extensions (".exe_old"); check it exactly.
                    found.AddRange(Directory.GetFiles(dir, pattern)
                        .Where(f => string.Equals(Path.GetExtension(f), pattern.Substring(1), StringComparison.OrdinalIgnoreCase)));
                }

                if (!recursive)
                {
                    return;
                }

                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0 || IsCompatibilityDirectory(sub))
                    {
                        continue;
                    }

                    Collect(sub, true, found);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static bool IsCompatibilityDirectory(string path)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return name == ".steam-compat-data" || name == ".wine-prefix" ||
                   (Directory.Exists(Path.Combine(path, "dosdevices")) && Directory.Exists(Path.Combine(path, "drive_c")));
        }

        private static int PathDepth(string root, string file)
        {
            var relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
            return relative.Count(c => c == Path.DirectorySeparatorChar);
        }

        internal static bool IsHelperExecutable(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            return HelperExecutablePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
        }

        internal static bool IsLauncherExecutable(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return LauncherExecutableWords.Any(w => name.Contains(w));
        }

        /// <summary>Same test as Quiver's <c>WindowsInstallerService.IsExecutable</c>.</summary>
        private static bool IsUsableExecutable(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
                   path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        private static WindowsInstallerReceipt ReadReceipt(string dir)
        {
            return QuiverJson.TryReadFile<WindowsInstallerReceipt>(Path.Combine(dir, WindowsInstallerReceiptFileName), out var receipt)
                ? receipt
                : null;
        }

        private static string ReadVersion(string dir)
        {
            try
            {
                var file = Path.Combine(dir, VersionFileName);
                return File.Exists(file) ? NullIfBlank(File.ReadAllText(file).Trim()) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
