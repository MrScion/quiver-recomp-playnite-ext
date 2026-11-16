using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuiverLibrary.Core.Cli
{
    /// <summary>
    /// Watches one run of Quiver's <c>--update</c> (<c>CLIHandler.UpdateAllGames</c>, commit a27f9a7). It updates the
    /// apps that have an update one after another with the same code as <c>--download</c>, which never returns once an
    /// app is installed: the run stops at the first app it updates. Quiver writes "→ Updating &lt;name&gt;..." before each
    /// one; this tells when that app has its new download (version or download hash changed) and its folder has stopped
    /// changing, so the run can be stopped and started again for the next app.
    /// </summary>
    public class QuiverUpdateWatch
    {
        private static readonly Regex UpdatingLine = new Regex(@"Updating (.+?)\.\.\.\s*$", RegexOptions.Compiled);

        private readonly QuiverLibraryData data;
        private readonly string appsFolder;
        private readonly Dictionary<string, QuiverApp> byName;
        private readonly Dictionary<string, string> before;
        private readonly object sync = new object();
        private string previousFolder;

        /// <param name="installed">The apps installed before the run, with their download as it was then.</param>
        public QuiverUpdateWatch(QuiverLibraryData data, string appsFolder, IEnumerable<QuiverAppState> installed)
        {
            this.data = data;
            this.appsFolder = appsFolder;
            var states = installed.Where(s => s.IsInstalled).ToList();
            byName = new Dictionary<string, QuiverApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var state in states)
            {
                if (!string.IsNullOrEmpty(state.App.Name) && !byName.ContainsKey(state.App.Name))
                {
                    byName[state.App.Name] = state.App;
                }
            }

            before = states.ToDictionary(
                s => s.App.FolderName,
                s => QuiverSetupUpdates.Fingerprint(s.InstallDirectory, s.Version),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The app Quiver said it is updating now; null before the first one.</summary>
        public QuiverApp Current { get; private set; }

        /// <summary>The name Quiver wrote, also when it is not an installed app this watch knows.</summary>
        public string CurrentName { get; private set; }

        /// <summary>Raised when Quiver starts updating another app (on Quiver's output thread).</summary>
        public event Action<string> UpdatingStarted;

        /// <summary>For <see cref="QuiverCli.Run"/>'s <c>onLine</c>.</summary>
        public void OnLine(string line)
        {
            var match = UpdatingLine.Match(line ?? string.Empty);
            if (!match.Success)
            {
                return;
            }

            var name = match.Groups[1].Value.Trim();
            lock (sync)
            {
                CurrentName = name;
                Current = byName.TryGetValue(name, out var app) ? app : null;
                previousFolder = null;
            }

            UpdatingStarted?.Invoke(name);
        }

        /// <summary>
        /// For <see cref="QuiverCli.Run"/>'s <c>finishedWhen</c>: true when the app being updated is installed with
        /// another download than before and its folder didn't change since the previous check.
        /// </summary>
        public bool AppUpdatedAndSettled()
        {
            QuiverApp app;
            lock (sync)
            {
                app = Current;
            }

            if (app == null)
            {
                return false;
            }

            var state = data.GetState(app, appsFolder);
            before.TryGetValue(app.FolderName, out var old);
            var now = state.IsInstalled ? QuiverSetupUpdates.Fingerprint(state.InstallDirectory, state.Version) : null;
            if (now == null || string.Equals(now, old, StringComparison.OrdinalIgnoreCase))
            {
                lock (sync)
                {
                    previousFolder = null;
                }

                return false;
            }

            var folder = QuiverLibraryData.FolderFingerprint(state.InstallDirectory);
            lock (sync)
            {
                var settled = folder != null && folder == previousFolder;
                previousFolder = folder;
                return settled;
            }
        }
    }
}
