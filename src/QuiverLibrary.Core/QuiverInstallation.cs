using System;
using System.IO;

namespace QuiverLibrary.Core
{
    /// <summary>
    /// Locates a Quiver Launcher install. Portable layout (Velopack):
    /// <c>&lt;root&gt;\QuiverLauncher.exe</c> (stub), <c>&lt;root&gt;\Update.exe</c>,
    /// <c>&lt;root&gt;\.portable</c> and the real app in <c>&lt;root&gt;\current\</c>.
    /// Quiver keeps its library (<c>apps.json</c>, <c>settings.json</c>, <c>Apps\</c>) in
    /// Velopack's root folder, i.e. the parent of <c>current\</c>.
    /// </summary>
    public sealed class QuiverInstallation
    {
        public const string ExeName = "QuiverLauncher.exe";
        public const string CurrentFolderName = "current";

        /// <summary>Velopack root folder; also Quiver's data root.</summary>
        public string RootFolder { get; }

        /// <summary>The real app (<c>current\QuiverLauncher.exe</c>), used for the command line so its exit code and output can be read.</summary>
        public string CliExe { get; }

        /// <summary>What "Open Quiver Launcher" starts: the Velopack stub when present, otherwise the app itself.</summary>
        public string LauncherExe { get; }

        public string DataRoot => RootFolder;

        private QuiverInstallation(string rootFolder, string cliExe, string launcherExe)
        {
            RootFolder = rootFolder;
            CliExe = cliExe;
            LauncherExe = launcherExe;
        }

        /// <summary>
        /// Resolves an install from a folder the user picked — the portable root,
        /// its <c>current\</c> subfolder, or the path of a <c>QuiverLauncher.exe</c>.
        /// Returns null when no Quiver executable is found there. Never throws.
        /// </summary>
        public static QuiverInstallation FromPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                var folder = Path.GetFullPath(path.Trim().Trim('"'));
                if (File.Exists(folder))
                {
                    folder = Path.GetDirectoryName(folder);
                }

                if (!Directory.Exists(folder))
                {
                    return null;
                }

                folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(Path.GetFileName(folder), CurrentFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(folder);
                    if (parent != null && (File.Exists(Path.Combine(parent, ExeName)) || File.Exists(Path.Combine(parent, "Update.exe"))))
                    {
                        folder = parent;
                    }
                }

                var appExe = Path.Combine(folder, CurrentFolderName, ExeName);
                var rootExe = Path.Combine(folder, ExeName);
                if (File.Exists(appExe))
                {
                    return new QuiverInstallation(folder, appExe, File.Exists(rootExe) ? rootExe : appExe);
                }

                // Unpackaged build: no current\ folder, data lives beside the exe.
                if (File.Exists(rootExe))
                {
                    return new QuiverInstallation(folder, rootExe, rootExe);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The executable paths <see cref="FromPath"/> looks for, for error messages. Never throws.</summary>
        public static string[] SearchedExePaths(string path)
        {
            try
            {
                var folder = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return new[] { Path.Combine(folder, CurrentFolderName, ExeName), Path.Combine(folder, ExeName) };
            }
            catch
            {
                return new[] { path };
            }
        }

        /// <summary>
        /// The configured folder when set; otherwise Quiver's default per-user
        /// install (<c>%LocalAppData%\QuiverLauncher</c>). A configured folder is never
        /// silently replaced by the default, so a wrong path shows up as an error.
        /// </summary>
        public static QuiverInstallation Detect(string configuredPath, string localAppData)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return FromPath(configuredPath);
            }

            return string.IsNullOrWhiteSpace(localAppData)
                ? null
                : FromPath(Path.Combine(localAppData, "QuiverLauncher"));
        }
    }
}
