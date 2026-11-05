using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuiverLibrary.Core.Executables
{
    /// <summary>
    /// The shipped database (<c>executables.json</c>, checked by hand against each project's
    /// documentation) plus the local one, which records on its own every installed app with
    /// several programs and no entry yet (<c>"reviewed": false</c>), so new Quiver apps show up
    /// there to be checked. A local entry set to <c>"reviewed": true</c> is never rewritten and
    /// wins over the shipped one.
    /// </summary>
    public class ExecutableDatabase
    {
        public const string LocalFileName = "executables.local.json";

        internal const string BundledResourceName = "QuiverLibrary.Core.Executables.executables.json";

        internal const string DetectedSource = "Detected automatically; not reviewed yet.";

        private readonly List<ExecutableEntry> bundled;
        private readonly List<ExecutableEntry> local;

        public string LocalPath { get; }

        public ExecutableDatabase(IEnumerable<ExecutableEntry> bundled, string localPath)
        {
            this.bundled = Clean(bundled);
            LocalPath = localPath;
            local = localPath != null && QuiverJson.TryReadFile<ExecutableDatabaseFile>(localPath, out var file)
                ? Clean(file.Apps)
                : new List<ExecutableEntry>();
        }

        /// <summary>The shipped database plus the local file at <paramref name="localPath"/> (may not exist yet).</summary>
        public static ExecutableDatabase Load(string localPath)
        {
            return new ExecutableDatabase(LoadBundled(), localPath);
        }

        public static List<ExecutableEntry> LoadBundled()
        {
            using (var stream = typeof(ExecutableDatabase).Assembly.GetManifestResourceStream(BundledResourceName))
            using (var reader = new StreamReader(stream))
            {
                return QuiverJson.ReadText<ExecutableDatabaseFile>(reader.ReadToEnd())?.Apps ?? new List<ExecutableEntry>();
            }
        }

        public IReadOnlyList<ExecutableEntry> LocalEntries => local;

        /// <summary>
        /// The entry that decides the app's programs: a reviewed local one, else a shipped one; null
        /// when the app has none (entries with only <see cref="ExecutableEntry.UserData"/> don't count).
        /// </summary>
        public ExecutableEntry Find(QuiverApp app)
        {
            return Match(local.Where(e => e.Reviewed && e.HasLaunchInfo), app) ?? Match(bundled.Where(e => e.HasLaunchInfo), app);
        }

        /// <summary>The app's user data from a reviewed local entry, else from a shipped one; null when unknown.</summary>
        public UserDataInfo FindUserData(QuiverApp app)
        {
            return Match(local.Where(e => e.Reviewed && e.UserData != null), app)?.UserData ?? Match(bundled.Where(e => e.UserData != null), app)?.UserData;
        }

        /// <summary>The app's achievements from a reviewed local entry, else from a shipped one; null when unknown.</summary>
        public AchievementsInfo FindAchievements(QuiverApp app)
        {
            return Match(local.Where(e => e.Reviewed && e.Achievements != null), app)?.Achievements ?? Match(bundled.Where(e => e.Achievements != null), app)?.Achievements;
        }

        /// <summary>How the app gets its game, from a reviewed local entry, else from a shipped one; null when ready to play or unknown.</summary>
        public SetupInfo FindSetup(QuiverApp app)
        {
            return Match(local.Where(e => e.Reviewed && e.Setup != null), app)?.Setup ?? Match(bundled.Where(e => e.Setup != null), app)?.Setup;
        }

        /// <summary>
        /// Writes, refreshes or drops the automatic local entry of an app. Returns true when the
        /// local entries changed (save them with <see cref="SaveLocal"/>).
        /// </summary>
        public bool RecordDetected(QuiverAppState state)
        {
            var app = state?.App;
            if (app == null || string.IsNullOrWhiteSpace(app.FolderName))
            {
                return false;
            }

            var current = local.FirstOrDefault(e => !e.Reviewed && string.Equals(e.FolderName, app.FolderName, StringComparison.OrdinalIgnoreCase));
            if (Find(app) != null)
            {
                // Reviewed meanwhile (here or in a newer shipped database): the automatic entry is obsolete.
                return current != null && local.Remove(current);
            }

            var detected = state.IsInstalled ? Detect(app, state.InstallDirectory) : null;
            if (detected == null)
            {
                // Uninstalled apps keep what was found: the folder may come back with the same programs.
                if (current == null || !state.IsInstalled)
                {
                    return false;
                }

                local.Remove(current);
                return true;
            }

            if (current != null && Same(current, detected))
            {
                return false;
            }

            if (current != null)
            {
                local[local.IndexOf(current)] = detected;
            }
            else
            {
                local.Add(detected);
            }

            return true;
        }

        public void SaveLocal()
        {
            QuiverJson.WriteFile(LocalPath, new ExecutableDatabaseFile
            {
                Apps = local.OrderBy(e => e.Name ?? e.FolderName, StringComparer.OrdinalIgnoreCase).ToList(),
            });
        }

        /// <summary>An automatic entry when the app has more than one candidate besides the helpers; null otherwise.</summary>
        internal static ExecutableEntry Detect(QuiverApp app, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) ||
                File.Exists(Path.Combine(dir, QuiverLibraryData.WindowsInstallerReceiptFileName)))
            {
                return null;
            }

            var play = QuiverLibraryData.ResolveExecutable(dir);
            var others = ExecutablePlanner.AutomaticOthers(dir, play);
            if ((play == null ? 0 : 1) + others.Count < 2)
            {
                return null;
            }

            return new ExecutableEntry
            {
                Name = app.Name,
                Repository = app.Repository,
                FolderName = app.FolderName,
                Play = play == null ? null : new ExecutableTarget { File = ExecutablePlanner.RelativePath(dir, play) },
                Others = others.Select(o => new ExecutableTarget { Name = o.Name, File = ExecutablePlanner.RelativePath(dir, o.Path) }).ToList(),
                Reviewed = false,
                Source = DetectedSource,
            };
        }

        private static ExecutableEntry Match(IEnumerable<ExecutableEntry> entries, QuiverApp app)
        {
            var list = entries.ToList();
            return list.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.FolderName) &&
                                            string.Equals(e.FolderName, app.FolderName, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.FolderName) && !string.IsNullOrWhiteSpace(e.Repository) &&
                                               string.Equals(e.Repository, app.Repository, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Same(ExecutableEntry a, ExecutableEntry b)
        {
            string Describe(ExecutableEntry e) => string.Join("|",
                new[] { e.Name, e.Repository, e.Play?.File }
                    .Concat((e.Others ?? new List<ExecutableTarget>()).Select(o => o?.Name + ">" + o?.File + ">" + o?.Arguments)));
            return string.Equals(Describe(a), Describe(b), StringComparison.Ordinal);
        }

        private static List<ExecutableEntry> Clean(IEnumerable<ExecutableEntry> entries)
        {
            return (entries ?? Enumerable.Empty<ExecutableEntry>()).Where(e => e != null).ToList();
        }
    }
}
