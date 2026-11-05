using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuiverLibrary.Core.Executables
{
    /// <summary>A setup command (<see cref="SetupInfo.Update"/> or <see cref="SetupInfo.Install"/>) ready to run, or why not.</summary>
    public class PreparedCommand
    {
        /// <summary>Absolute path of the program; null when it is not in the app folder.</summary>
        public string File { get; set; }

        public string Arguments { get; set; }

        /// <summary>The app folder: programs like <c>python -m installer</c> find their files from there.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>Placeholders nobody filled (<c>{Image}</c>, <c>{GameDir}</c> when the game is not found, ...).</summary>
        public List<string> Missing { get; set; } = new List<string>();

        /// <summary>Why it can't run (<see cref="SetupInfo.UpdateProblem"/>, program not found, ...); null when it can.</summary>
        public string Problem { get; set; }

        public bool CanRun => Problem == null && File != null && Missing.Count == 0;

        /// <summary>
        /// What is missing are files the player can choose (<c>{Image}</c>, <c>{TitleUpdate}</c>, ...), not folders
        /// worked out from the install: ask for them, then prepare it again with them.
        /// </summary>
        public bool CanAsk => Problem == null && File != null && Missing.Count > 0 &&
                              Missing.All(m => !SetupCommands.Derived.Contains(m, StringComparer.OrdinalIgnoreCase));

        /// <summary>The command line as shown to the player.</summary>
        public string Display => "\"" + File + "\"" + (string.IsNullOrEmpty(Arguments) ? string.Empty : " " + Arguments);
    }

    /// <summary>Turns the commands of <c>executables.json</c>'s <c>setup</c> into something to run.</summary>
    public static class SetupCommands
    {
        private static readonly Regex Placeholder = new Regex(@"\{([A-Za-z]+)\}");

        /// <summary>Placeholders filled from the install itself, never asked to the player.</summary>
        public static readonly string[] Derived = { "GameDir", "AppDir", "ExeDir", "InstallDir" };

        /// <summary>The given values that still point to an existing file or folder (a disc moved away is asked again).</summary>
        public static Dictionary<string, string> Existing(IDictionary<string, string> values)
        {
            var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(value.Value) && (File.Exists(value.Value) || Directory.Exists(value.Value)))
                {
                    existing[value.Key] = value.Value;
                }
            }

            return existing;
        }

        /// <summary>
        /// The installed game's folder: where <see cref="SetupInfo.InstallMarker"/> is (the shallowest one in the app
        /// folder), else the play program's folder; null when neither is found.
        /// </summary>
        public static string FindGameDir(string appDir, SetupInfo setup, string playPath)
        {
            var marker = setup?.InstallMarker;
            if (!string.IsNullOrWhiteSpace(marker))
            {
                marker = marker.Replace('/', '\\').Trim('\\');
                var file = ExecutablePlanner.FindInSubfolders(appDir, Path.GetFileName(marker));
                if (file == null || !file.EndsWith("\\" + marker, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var dir = file.Substring(0, file.Length - marker.Length - 1);
                return dir.Length == 0 ? null : dir;
            }

            return string.IsNullOrEmpty(playPath) ? null : Path.GetDirectoryName(playPath);
        }

        /// <summary>
        /// Prepares <paramref name="command"/>: finds its program in the app folder and fills <c>{GameDir}</c>,
        /// <c>{AppDir}</c>, environment variables and the <paramref name="values"/> given (<c>Image</c>, ...).
        /// </summary>
        public static PreparedCommand Prepare(string appDir, ExecutableTarget command, string gameDir,
            IDictionary<string, string> values = null, string problem = null)
        {
            var prepared = new PreparedCommand { WorkingDirectory = appDir, Problem = problem };
            if (command == null || string.IsNullOrWhiteSpace(command.File))
            {
                prepared.Problem = prepared.Problem ?? "No command.";
                return prepared;
            }

            prepared.File = ExecutablePlanner.FindFile(appDir, command);
            if (prepared.File == null && prepared.Problem == null)
            {
                prepared.Problem = $"{command.File} is not in the app folder.";
            }

            var arguments = command.Arguments ?? string.Empty;
            arguments = Placeholder.Replace(arguments, m =>
            {
                var name = m.Groups[1].Value;
                if (string.Equals(name, "GameDir", StringComparison.OrdinalIgnoreCase))
                {
                    if (gameDir != null)
                    {
                        return gameDir.TrimEnd('\\');
                    }
                }
                else if (string.Equals(name, "AppDir", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "ExeDir", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }
                else if (values != null)
                {
                    var known = values.FirstOrDefault(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(known.Value))
                    {
                        return known.Value;
                    }
                }

                if (!prepared.Missing.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    prepared.Missing.Add(name);
                }

                return m.Value;
            });
            prepared.Arguments = ExecutablePlanner.ExpandVariables(arguments, appDir, prepared.File);
            return prepared;
        }

        /// <summary>
        /// Folders where an installer's game may be outside the app folder (which uninstalling in Quiver removes):
        /// the folder of a play program the player chose there, and the installer's default folder, when they exist.
        /// </summary>
        public static List<string> OutsideInstallDirs(string appDir, SetupInfo setup, string chosenPlay)
        {
            var dirs = new List<string>();
            if (setup == null || !setup.IsInstaller)
            {
                return dirs;
            }

            void Add(string dir)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    {
                        return;
                    }

                    var full = Path.GetFullPath(dir).TrimEnd('\\');
                    var app = string.IsNullOrWhiteSpace(appDir) ? null : Path.GetFullPath(appDir).TrimEnd('\\');
                    var inside = app != null && (string.Equals(full, app, StringComparison.OrdinalIgnoreCase) ||
                                                 full.StartsWith(app + "\\", StringComparison.OrdinalIgnoreCase));
                    if (!inside && !dirs.Contains(full, StringComparer.OrdinalIgnoreCase))
                    {
                        dirs.Add(full);
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException || e is UnauthorizedAccessException)
                {
                }
            }

            var chosen = ExecutablePlanner.ChosenPath(appDir, chosenPlay);
            if (chosen != null && Path.IsPathRooted(chosenPlay ?? string.Empty))
            {
                Add(Path.GetDirectoryName(chosen));
            }

            if (!string.IsNullOrWhiteSpace(setup.DefaultInstallDir))
            {
                Add(Environment.ExpandEnvironmentVariables(setup.DefaultInstallDir));
            }

            return dirs;
        }

        /// <summary>The update command of an installed app, ready to run or with the reason it can't.</summary>
        public static PreparedCommand PrepareUpdate(string appDir, SetupInfo setup, string playPath, IDictionary<string, string> values = null)
        {
            if (setup?.Update == null)
            {
                return new PreparedCommand { WorkingDirectory = appDir, Problem = "No update command." };
            }

            return Prepare(appDir, setup.Update, FindGameDir(appDir, setup, playPath), values,
                string.IsNullOrWhiteSpace(setup.UpdateProblem) ? null : setup.UpdateProblem);
        }
    }
}
