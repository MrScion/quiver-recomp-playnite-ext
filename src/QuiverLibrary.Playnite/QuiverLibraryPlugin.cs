using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using QuiverLibrary.Core;
using QuiverLibrary.Core.Achievements;
using QuiverLibrary.Core.Cli;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Ludusavi;
using QuiverLibrary.Playnite.Controllers;
using QuiverLibrary.Playnite.Settings;
using QuiverLibrary.Playnite.Setup;

namespace QuiverLibrary.Playnite
{
    /// <summary>
    /// Imports Quiver Launcher's apps (recompilations and PC ports). Quiver stays
    /// in charge of every file operation: Install/Uninstall/Update run Quiver's
    /// command line, Playnite only tracks the install state and starts the
    /// game's executable directly.
    /// </summary>
    public class QuiverLibraryPlugin : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // "@" puts the section at the root of Playnite's main menu.
        private const string MenuSection = "@Recomps (Quiver Launcher)";

        internal const string NotConfiguredNotificationId = "quiver-not-configured";

        internal static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public override Guid Id { get; } = Guid.Parse(PluginIdentity.LibraryPluginGuid);

        /// <summary>Source given to every imported game.</summary>
        internal const string SourceName = "Quiver Launcher";

        /// <summary>Source used by earlier builds; migrated to <see cref="SourceName"/>.</summary>
        private const string LegacySourceName = "Quiver";

        public override string Name => "Recomps";

        /// <summary>Library icon (filters, game details, default game icon): the extension's own icon.</summary>
        public override string LibraryIcon { get; } = Path.Combine(Path.GetDirectoryName(typeof(QuiverLibraryPlugin).Assembly.Location), "pluginicon.png");

        public override LibraryClient Client { get; }

        public QuiverLibraryPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = true };
            Client = new QuiverClient(this);
        }

        #region Settings and paths

        internal QuiverSettings LoadSettings() => LoadPluginSettings<QuiverSettings>() ?? new QuiverSettings();

        internal QuiverInstallation FindInstallation() => QuiverInstallation.Detect(LoadSettings().QuiverFolder, LocalAppData);

        internal QuiverInstallation RequireInstallation()
        {
            var install = FindInstallation();
            if (install == null)
            {
                Logger.Warn($"Quiver Launcher not found (configured folder: '{LoadSettings().QuiverFolder}').");
                throw new Exception(Loc("LOCQuiverNotFound"));
            }

            return install;
        }

        internal QuiverLibraryData GetData()
        {
            var data = new QuiverLibraryData(RequireInstallation().DataRoot);
            if (!data.HasAppsJson)
            {
                throw new Exception(string.Format(Loc("LOCQuiverAppsJsonMissing"), data.AppsJsonPath));
            }

            return data;
        }

        /// <summary>Longer than the 10 minutes Quiver itself waits for a download.</summary>
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Runs <c>--download</c>. Quiver installs the app but its process never exits afterwards,
        /// so it's stopped once the app is installed and its folder has stopped changing.
        /// </summary>
        internal QuiverCliResult RunDownload(QuiverInstallation install, string folderName)
        {
            var data = new QuiverLibraryData(install.DataRoot);
            var app = data.FindApp(folderName);
            QuiverCliResult Download() => QuiverCli.Run(install.CliExe, new[] { "--download", folderName }, CancellationToken.None,
                app == null ? null : data.InstalledAndSettledCheck(app, data.AppsFolder), TimeSpan.FromSeconds(5), DownloadTimeout);

            var result = Download();
            if (!result.NoEligibleDownload || !File.Exists(data.CatalogReleaseStatusCachePath))
            {
                return result;
            }

            // Without its catalog cache Quiver asks GitHub for the release, files included.
            Logger.Info($"Quiver found no download for {folderName}; retrying without its catalog cache.");
            try
            {
                File.Delete(data.CatalogReleaseStatusCachePath);
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Cannot delete Quiver's catalog cache.");
                return result;
            }

            return Download();
        }

        /// <summary>Install failure notification; when only Quiver's window can install the app, clicking it opens Quiver.</summary>
        internal void NotifyInstallFailed(string folderName, string gameName, QuiverCliResult result, string reason)
        {
            var id = $"quiver-install-{folderName}";
            if (result != null && result.NeedsQuiverWindow)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    id, string.Format(Loc("LOCQuiverInstallNeedsQuiver"), gameName), NotificationType.Error, () => Client.Open()));
                return;
            }

            if (result != null && result.TimedOut)
            {
                reason = Loc("LOCQuiverInstallTimedOut");
            }

            PlayniteApi.Notifications.Add(new NotificationMessage(
                id, string.Format(Loc("LOCQuiverInstallFailed"), gameName, reason), NotificationType.Error));
        }

        internal QuiverAppState GetAppState(string folderName)
        {
            var data = GetData();
            var app = data.FindApp(folderName);
            return app == null ? null : data.GetState(app, data.AppsFolder);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return new QuiverSettingsViewModel(this);
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new QuiverSettingsView();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            RemoveNotInstalledGamesOnce();
            MigrateLegacySource();

            if (FindInstallation() == null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    NotConfiguredNotificationId, Loc("LOCQuiverNotConfiguredNotification"), NotificationType.Info, () => OpenSettingsView()));
            }
        }

        /// <summary>
        /// See <see cref="QuiverSettings.NotInstalledCleanupDone"/>. Only games without any
        /// play history are removed, so no stats are ever lost.
        /// </summary>
        private void RemoveNotInstalledGamesOnce()
        {
            var settings = LoadSettings();
            if (settings.NotInstalledCleanupDone)
            {
                return;
            }

            try
            {
                var toRemove = PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && !g.IsInstalled && !g.IsInstalling && !g.IsUninstalling &&
                                g.Playtime == 0 && g.PlayCount == 0 && g.LastActivity == null)
                    .ToList();

                if (toRemove.Count > 0)
                {
                    PlayniteApi.Database.Games.Remove(toRemove);
                    Logger.Info($"Removed {toRemove.Count} not-installed Quiver games imported by an earlier build.");
                    Notify("cleanup", string.Format(Loc("LOCQuiverCleanupDone"), toRemove.Count), NotificationType.Info);
                }

                settings.NotInstalledCleanupDone = true;
                SavePluginSettings(settings);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot remove not-installed Quiver games.");
            }
        }

        /// <summary>
        /// Playnite never changes the source of a game it already has on re-import, so games
        /// imported with the old "Quiver" source (or none) are moved to <see cref="SourceName"/> here.
        /// A source the user picked by hand is left alone. The old source is removed once unused.
        /// </summary>
        private void MigrateLegacySource()
        {
            try
            {
                var sources = PlayniteApi.Database.Sources;
                var legacy = sources.FirstOrDefault(s => string.Equals(s.Name, LegacySourceName, StringComparison.OrdinalIgnoreCase));
                var toMigrate = PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && (g.SourceId == Guid.Empty || (legacy != null && g.SourceId == legacy.Id)))
                    .ToList();

                if (toMigrate.Count > 0)
                {
                    var target = sources.FirstOrDefault(s => string.Equals(s.Name, SourceName, StringComparison.OrdinalIgnoreCase))
                                 ?? sources.Add(SourceName);
                    using (PlayniteApi.Database.BufferedUpdate())
                    {
                        foreach (var game in toMigrate)
                        {
                            game.SourceId = target.Id;
                            PlayniteApi.Database.Games.Update(game);
                        }
                    }

                    Logger.Info($"Moved {toMigrate.Count} game(s) to the '{SourceName}' source.");
                }

                if (legacy != null && !PlayniteApi.Database.Games.Any(g => g.SourceId == legacy.Id))
                {
                    sources.Remove(legacy.Id);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot move games to the '{SourceName}' source.");
            }
        }

        #endregion

        #region Library

        /// <summary>
        /// Only apps installed in Quiver are imported as new games — new games are
        /// installed from Quiver itself. Apps already in Playnite are always returned too,
        /// because Playnite only refreshes the install state of the games a library returns
        /// (<c>GameDatabase.ImportGames</c>): that's what marks an uninstalled game as not
        /// installed while keeping it, and its stats, in Playnite.
        /// </summary>
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var allStates = GetData().GetStates();
            var names = QuiverGameNaming.BuildNames(allStates.Select(s => s.App));
            var existing = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
                .GroupBy(g => g.GameId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var disabledFeatures = DisabledFeatures();
            var executables = LoadExecutables(allStates);
            var settings = LoadSettings();
            var syncLastPlayed = settings.SyncLastPlayed;

            var games = new List<GameMetadata>();
            var updated = new List<Game>();
            var plans = new List<KeyValuePair<QuiverAppState, ExecutablePlan>>();
            foreach (var state in allStates.Where(s => s.IsInstalled || existing.ContainsKey(s.App.FolderName)))
            {
                var metadata = ToMetadata(state, names[state.App.FolderName], disabledFeatures);
                var plan = PlanFor(state, executables, settings);
                plans.Add(new KeyValuePair<QuiverAppState, ExecutablePlan>(state, plan));
                var otherActions = OtherActionsFor(state, plan);
                var quiverLastPlayed = syncLastPlayed && state.IsInstalled
                    ? QuiverLastPlayed.Read(state.InstallDirectory, plan?.Play?.Path)
                    : null;
                if (existing.TryGetValue(state.App.FolderName, out var game))
                {
                    // Playnite never changes the actions of a game it already has on re-import, nor its
                    // last activity unless the library also reports play time (GameDatabase.ImportGames).
                    var changed = otherActions != null && SyncOtherActions(game, otherActions);
                    if (syncLastPlayed && state.IsInstalled && SyncLastPlayedTime(game, state.InstallDirectory, quiverLastPlayed))
                    {
                        changed = true;
                    }

                    if (changed)
                    {
                        updated.Add(game);
                    }
                }
                else
                {
                    if (otherActions != null && otherActions.Count > 0)
                    {
                        metadata.GameActions = otherActions;
                    }

                    metadata.LastActivity = quiverLastPlayed;
                }

                games.Add(metadata);
            }

            if (updated.Count > 0)
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    updated.ForEach(g => PlayniteApi.Database.Games.Update(g));
                }

                Logger.Info($"Updated the other actions or last played time of {updated.Count} Quiver game(s).");
            }

            MakePortable(plans, executables);
            CheckSetupUpdates(allStates);
            if (settings.LudusaviBackup)
            {
                // The Ludusavi extension asks for the game by its name in Playnite, which the user may have changed.
                UpdateLudusavi(plans, executables, s => existing.TryGetValue(s.App.FolderName, out var g) ? g.Name : names[s.App.FolderName]);
            }

            return games;
        }

        /// <summary>
        /// Makes Playnite's last activity and Quiver's <c>LastPlayed.txt</c> equal, the newest one winning.
        /// Returns true when the game changed and needs saving; Quiver's file is written here.
        /// </summary>
        private static bool SyncLastPlayedTime(Game game, string appDir, DateTime? quiverLastPlayed)
        {
            var playnite = game.LastActivity?.Kind == DateTimeKind.Utc ? game.LastActivity.Value.ToLocalTime() : game.LastActivity;
            switch (QuiverLastPlayed.Compare(playnite, quiverLastPlayed))
            {
                case LastPlayedSync.UpdatePlaynite:
                    game.LastActivity = quiverLastPlayed;
                    return true;
                case LastPlayedSync.UpdateQuiver:
                    if (!QuiverLastPlayed.Write(appDir, playnite.Value))
                    {
                        Logger.Warn($"Cannot write Quiver's last played time of {game.Name} in {appDir}.");
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Quiver's "last played" order follows the games started from Playnite (Quiver writes it on launch too).</summary>
        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var game = args.Game;
            if (game == null || game.PluginId != Id || !LoadSettings().SyncLastPlayed)
            {
                return;
            }

            try
            {
                var state = GetAppState(game.GameId);
                if (state != null && state.IsInstalled && !QuiverLastPlayed.Write(state.InstallDirectory, DateTime.Now))
                {
                    Logger.Warn($"Cannot write Quiver's last played time of {game.Name} in {state.InstallDirectory}.");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot update Quiver's last played time of {game.Name}.");
            }
        }

        /// <summary>Keeps the achievements CSV (experimental) up to date with what was unlocked in the session.</summary>
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var game = args.Game;
            if (game == null || game.PluginId != Id || !LoadSettings().AchievementsExport)
            {
                return;
            }

            try
            {
                var state = GetAppState(game.GameId);
                var achievements = state == null ? null : LoadExecutables().FindAchievements(state.App);
                if (state != null && state.IsInstalled && RecompAchievements.CanRead(achievements))
                {
                    WriteAchievementsCsv(game, state.InstallDirectory, achievements, out _);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot update the achievements CSV of {game.Name}.");
            }
        }

        internal HashSet<string> DisabledFeatures()
        {
            return new HashSet<string>(LoadSettings().DisabledFeatures ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private static GameMetadata ToMetadata(QuiverAppState state, string name, ICollection<string> disabledFeatures)
        {
            var game = DescribeApp(state.App, disabledFeatures);
            game.GameId = state.App.FolderName;
            game.Name = name;
            game.IsInstalled = state.IsInstalled;
            game.InstallDirectory = state.IsInstalled ? state.InstallDirectory : null;
            game.Version = state.Version;
            game.Source = new MetadataNameProperty(SourceName);
            return game;
        }

        /// <summary>
        /// What Quiver knows about an app beyond its install state: icon, links (GitHub and
        /// mod sites), the port's features (minus the ones disabled in the settings) and the kind
        /// of project as tags. Shared by the import and the "Store" metadata source.
        /// </summary>
        internal static GameMetadata DescribeApp(QuiverApp app, ICollection<string> disabledFeatures)
        {
            var game = new GameMetadata();

            if (!string.IsNullOrWhiteSpace(app.AppIconUrl))
            {
                game.Icon = new MetadataFile(app.AppIconUrl);
            }

            var links = new List<Link>();
            var repositoryUrl = QuiverGameNaming.RepositoryUrl(app);
            if (repositoryUrl != null)
            {
                links.Add(new Link("GitHub", repositoryUrl));
            }

            links.AddRange(QuiverTagMapping.ModLinksFor(app).Select(l => new Link(l.Key, l.Value)));
            if (links.Count > 0)
            {
                game.Links = links;
            }

            var features = QuiverTagMapping.FeaturesFor(app, disabledFeatures);
            if (features.Count > 0)
            {
                game.Features = new HashSet<MetadataProperty>(features.Select(f => new MetadataNameProperty(f)));
            }

            var tags = QuiverTagMapping.ProjectTypeTagsFor(app);
            if (tags.Count > 0)
            {
                game.Tags = new HashSet<MetadataProperty>(tags.Select(t => new MetadataNameProperty(t)));
            }

            return game;
        }

        /// <summary>Playnite's "Store" metadata source for this library, so "Download metadata" also fills games imported earlier.</summary>
        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new QuiverMetadataProvider(this);
        }

        #endregion

        #region Executables

        /// <summary>Base of the paths of the other actions; also how they are told apart from the user's own.</summary>
        private const string InstallDirVariable = "{InstallDir}";

        internal string LocalExecutablesPath => Path.Combine(GetPluginUserDataPath(), ExecutableDatabase.LocalFileName);

        /// <summary>
        /// The executables database; with <paramref name="states"/>, apps with several programs and
        /// no entry are first recorded in the local file (see <see cref="ExecutableDatabase"/>).
        /// Never throws: on error, an empty database is used.
        /// </summary>
        internal ExecutableDatabase LoadExecutables(IEnumerable<QuiverAppState> states = null)
        {
            ExecutableDatabase executables;
            try
            {
                executables = ExecutableDatabase.Load(LocalExecutablesPath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot load the executables database.");
                return new ExecutableDatabase(null, null);
            }

            if (states == null)
            {
                return executables;
            }

            try
            {
                var changed = false;
                foreach (var state in states)
                {
                    changed |= executables.RecordDetected(state);
                }

                if (changed)
                {
                    executables.SaveLocal();
                    var pending = executables.LocalEntries.Where(e => !e.Reviewed).Select(e => e.Name ?? e.FolderName).ToList();
                    Logger.Info($"Quiver apps with several programs not reviewed yet ({pending.Count}): {string.Join(", ", pending)}. See {LocalExecutablesPath}.");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot update the local executables database.");
            }

            return executables;
        }

        /// <summary>The programs of an installed app; null when it isn't installed or on error.</summary>
        private static ExecutablePlan PlanFor(QuiverAppState state, ExecutableDatabase executables, QuiverSettings settings)
        {
            if (!state.IsInstalled)
            {
                return null;
            }

            try
            {
                return ExecutablePlanner.Plan(state.InstallDirectory, executables.Find(state.App), ChosenExecutable(settings, state.App.FolderName));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot work out the programs of Quiver app {state.App.FolderName}.");
                return null;
            }
        }

        /// <summary>The other (non-play) actions an app should have: none when it isn't installed; null on error.</summary>
        private static List<GameAction> OtherActionsFor(QuiverAppState state, ExecutablePlan plan)
        {
            if (!state.IsInstalled)
            {
                return new List<GameAction>();
            }

            if (plan == null)
            {
                return null;
            }

            try
            {
                return plan.Others
                    .Select(o => new { Target = o, Relative = ExecutablePlanner.RelativePath(state.InstallDirectory, o.Path) })
                    .Where(o => o.Relative != null)
                    .Select(o => new GameAction
                    {
                        Name = o.Target.Name,
                        Type = GameActionType.File,
                        Path = Path.Combine(InstallDirVariable, o.Relative),
                        Arguments = o.Target.Arguments,
                        WorkingDir = Path.Combine(InstallDirVariable, Path.GetDirectoryName(o.Relative)).TrimEnd(Path.DirectorySeparatorChar),
                        IsPlayAction = false,
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot work out the programs of Quiver app {state.App.FolderName}.");
                return null;
            }
        }

        /// <summary>
        /// Replaces the game's other actions written by this extension (file actions under
        /// <c>{InstallDir}</c> that aren't play actions) with <paramref name="wanted"/>, keeping
        /// the user's own actions. Returns true when the game changed and needs saving.
        /// </summary>
        private static bool SyncOtherActions(Game game, List<GameAction> wanted)
        {
            var current = game.GameActions?.ToList() ?? new List<GameAction>();
            var managed = current.Where(IsManagedAction).ToList();
            if (managed.Count == wanted.Count && managed.Zip(wanted, SameAction).All(same => same))
            {
                return false;
            }

            game.GameActions = new ObservableCollection<GameAction>(current.Where(a => !IsManagedAction(a)).Concat(wanted));
            return true;
        }

        private static bool IsManagedAction(GameAction action)
        {
            return action != null && !action.IsPlayAction && action.Type == GameActionType.File &&
                   action.Path != null && action.Path.StartsWith(InstallDirVariable + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameAction(GameAction a, GameAction b)
        {
            return a.Name == b.Name &&
                   string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(a.WorkingDir, b.WorkingDir, StringComparison.OrdinalIgnoreCase) &&
                   (a.Arguments ?? string.Empty) == (b.Arguments ?? string.Empty);
        }

        /// <summary>Installing or uninstalling changes the programs on disk; library updates catch the rest (e.g. Quiver's updates).</summary>
        private void RefreshOtherActions(Game game)
        {
            if (game == null || game.PluginId != Id)
            {
                return;
            }

            try
            {
                var state = GetAppState(game.GameId);
                if (state == null)
                {
                    return;
                }

                var wanted = OtherActionsFor(state, PlanFor(state, LoadExecutables(new[] { state }), LoadSettings()));
                if (wanted != null && SyncOtherActions(game, wanted))
                {
                    PlayniteApi.Database.Games.Update(game);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot update the other actions of {game.Name}.");
            }
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            RefreshOtherActions(args.Game);
            MakePortable(args.Game);
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            RefreshOtherActions(args.Game);
            WarnGameLeftOutside(args.Game);
        }

        /// <summary>
        /// Quiver removes only the app folder: an installer's game put somewhere else stays. Says where, so the player
        /// can delete it (or run its own uninstaller there).
        /// </summary>
        private void WarnGameLeftOutside(Game game)
        {
            if (game == null || game.PluginId != Id)
            {
                return;
            }

            try
            {
                var state = GetAppState(game.GameId);
                if (state == null || state.IsInstalled)
                {
                    return;
                }

                var setup = LoadExecutables(new[] { state }).FindSetup(state.App);
                string chosen = null;
                LoadSettings().ChosenExecutables?.TryGetValue(state.App.FolderName, out chosen);
                var dirs = SetupCommands.OutsideInstallDirs(state.InstallDirectory, setup, chosen);
                if (dirs.Count == 0)
                {
                    return;
                }

                Logger.Info($"{game.Name} was uninstalled, but its installer's game may still be in: {string.Join("; ", dirs)}.");
                var first = dirs[0];
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    $"quiver-left-outside-{state.App.FolderName}",
                    string.Format(Loc("LOCQuiverSetupLeftOutside"), game.Name, string.Join("\n", dirs)),
                    NotificationType.Info, () => OpenFolder(first)));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot check whether {game.Name} left a game outside its app folder.");
            }
        }

        #endregion

        #region Portable

        /// <summary>See <see cref="QuiverSettings.MakePortable"/>; run when the setting is turned on.</summary>
        internal void MakeInstalledAppsPortable()
        {
            try
            {
                var executables = LoadExecutables();
                var settings = LoadSettings();
                MakePortable(GetData().GetStates()
                    .Where(s => s.IsInstalled)
                    .Select(s => new KeyValuePair<QuiverAppState, ExecutablePlan>(s, PlanFor(s, executables, settings))), executables);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot make the Quiver apps portable.");
            }
        }

        private void MakePortable(Game game)
        {
            if (game == null || game.PluginId != Id)
            {
                return;
            }

            try
            {
                var state = GetAppState(game.GameId);
                if (state != null)
                {
                    var executables = LoadExecutables();
                    MakePortable(new[] { new KeyValuePair<QuiverAppState, ExecutablePlan>(state, PlanFor(state, executables, LoadSettings())) }, executables);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot make {game.Name} portable.");
            }
        }

        /// <summary>
        /// Creates the portable marker of each installed app (see <see cref="QuiverPortable"/>) when
        /// <see cref="QuiverSettings.MakePortable"/> is on. Apps with data in their usual folder are
        /// left alone and, like portable apps that still have data there, warned about once.
        /// </summary>
        private void MakePortable(IEnumerable<KeyValuePair<QuiverAppState, ExecutablePlan>> apps, ExecutableDatabase executables)
        {
            var settings = LoadSettings();
            if (!settings.MakePortable)
            {
                return;
            }

            var warned = new HashSet<string>(settings.PortableWarnedApps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var newWarnings = false;
            foreach (var app in apps.Where(a => a.Key.IsInstalled))
            {
                var state = app.Key;
                var name = state.App.Name ?? state.App.FolderName;
                PortableResult result;
                try
                {
                    result = QuiverPortable.Apply(app.Value?.Play?.Path, executables.FindUserData(state.App));
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Cannot make {name} portable.");
                    continue;
                }

                string warning = null;
                switch (result.Outcome)
                {
                    case PortableOutcome.Created:
                        Logger.Info($"Made {name} portable: created {result.MarkerPath}.");
                        break;
                    case PortableOutcome.HasUserData:
                        Logger.Warn($"{name} not made portable: it has data in {result.UserDataPath}, which it would stop seeing. Move it to the game's folder (or delete it) to make it portable.");
                        warning = Loc("LOCQuiverPortableHasUserData");
                        break;
                    case PortableOutcome.AlreadyPortable when result.UserDataPath != null:
                        Logger.Warn($"{name} is portable ({result.MarkerPath}) but still has data in {result.UserDataPath}, which it doesn't use.");
                        warning = Loc("LOCQuiverPortableLeftoverData");
                        break;
                    case PortableOutcome.Failed:
                        Logger.Error($"Cannot make {name} portable: {result.MarkerPath}: {result.Error}");
                        break;
                }

                if (warning != null && warned.Add(state.App.FolderName))
                {
                    newWarnings = true;
                    var folder = result.UserDataPath;
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"quiver-portable-{state.App.FolderName}", string.Format(warning, name, folder), NotificationType.Info, () => OpenFolder(folder)));
                }
            }

            if (newWarnings)
            {
                settings.PortableWarnedApps = warned.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                SavePluginSettings(settings);
            }
        }

        #endregion

        #region Setup updates

        /// <summary>
        /// Warns once when Quiver has replaced the download of an installed app that is set up by hand
        /// (<see cref="SetupInfo"/>): an update only replaces the installer or launcher, so the game has
        /// to be installed or built again. Apps seen for the first time are only remembered.
        /// </summary>
        private void CheckSetupUpdates(IEnumerable<QuiverAppState> states)
        {
            try
            {
                var settings = LoadSettings();
                var done = new Dictionary<string, string>(settings.SetupVersions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                var notified = new Dictionary<string, string>(settings.SetupNotified ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                var changed = false;
                foreach (var row in SetupRows(states, settings, done))
                {
                    var name = row.Name;
                    if (row.Check == SetupUpdateCheck.FirstSeen)
                    {
                        done[row.FolderName] = row.Current;
                        changed = true;
                        Logger.Info($"{name}: remembered its download ({row.Current}) to tell when an update needs the setup again.");
                        continue;
                    }

                    if (row.Check != SetupUpdateCheck.Updated ||
                        (notified.TryGetValue(row.FolderName, out var announced) && string.Equals(announced, row.Current, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    notified[row.FolderName] = row.Current;
                    changed = true;
                    Logger.Info($"{name} was updated by Quiver ({row.Done} -> {row.Current}); its setup has to run again: {row.Program?.Path ?? row.AppDir}.");
                    var text = string.Format(Loc(row.Setup.IsInstaller ? "LOCQuiverSetupUpdatedInstaller" : "LOCQuiverSetupUpdatedBuilder"), name, row.DownloadedVersion ?? "?");
                    var pendingRow = row;
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        SetupNotificationId(row.FolderName), text, NotificationType.Info, () => UpdateOrRunSetup(pendingRow)));
                }

                if (changed)
                {
                    settings.SetupVersions = done;
                    settings.SetupNotified = notified;
                    SavePluginSettings(settings);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot check the Quiver apps that need their setup again.");
            }
        }

        private static string SetupNotificationId(string folderName) => $"quiver-setup-{folderName}";

        /// <summary>The installed apps set up by hand (<see cref="SetupInfo"/>), with their download and setup state.</summary>
        private List<SetupAppRow> SetupRows(IEnumerable<QuiverAppState> states, QuiverSettings settings, IDictionary<string, string> done)
        {
            var installed = states.Where(s => s.IsInstalled).ToList();
            var executables = LoadExecutables(installed);
            var rows = new List<SetupAppRow>();
            foreach (var state in installed)
            {
                var setup = executables.FindSetup(state.App);
                if (setup == null || !(setup.IsInstaller || setup.IsBuilder))
                {
                    continue;
                }

                var current = QuiverSetupUpdates.Fingerprint(state.InstallDirectory, state.Version);
                done.TryGetValue(state.App.FolderName, out var previous);
                var plan = PlanFor(state, executables, settings);
                var playPath = plan?.Play?.Path;
                rows.Add(new SetupAppRow
                {
                    Name = state.App.Name ?? state.App.FolderName,
                    FolderName = state.App.FolderName,
                    AppDir = state.InstallDirectory,
                    Setup = setup,
                    Current = current,
                    Done = previous,
                    Check = QuiverSetupUpdates.Compare(previous, current),
                    Program = SetupProgram(plan, setup, executables.Find(state.App) == null),
                    PlayPath = playPath,
                    Files = StoredSetupFiles(settings, state.App.FolderName),
                });
                var row = rows[rows.Count - 1];
                row.Update = setup.Update == null ? null : SetupCommands.PrepareUpdate(state.InstallDirectory, setup, playPath, SetupCommands.Existing(row.Files));
            }

            return rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>For the sidebar panel: every installed app set up by hand, read now (slow on a network drive: not on the UI thread).</summary>
        internal List<SetupAppRow> LoadSetupRows()
        {
            var settings = LoadSettings();
            var done = new Dictionary<string, string>(settings.SetupVersions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return SetupRows(GetData().GetStates(), settings, done);
        }

        /// <summary>The player says the game is set up from Quiver's current download (after running its setup).</summary>
        internal void MarkSetupDone(SetupAppRow row)
        {
            var settings = LoadSettings();
            var done = new Dictionary<string, string>(settings.SetupVersions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            done[row.FolderName] = row.Current;
            settings.SetupVersions = done;
            SavePluginSettings(settings);
            PlayniteApi.Notifications.Remove(SetupNotificationId(row.FolderName));
            Logger.Info($"{row.Name}: setup marked as done for {row.Current}.");
        }

        internal void RunSetup(SetupAppRow row) => RunSetup(row.Program, row.AppDir);

        private SetupRunner setupRunner;

        /// <summary>The queue that runs the setup commands (see <see cref="SetupRunner"/>).</summary>
        internal SetupRunner Runner
        {
            get
            {
                if (setupRunner == null)
                {
                    setupRunner = new SetupRunner(Path.Combine(GetPluginUserDataPath(), "setup-logs"));
                    setupRunner.Changed += UpdateSidebarProgress;
                    setupRunner.Started += job => SetGameInstalling(job.Row.FolderName, true);
                    setupRunner.Finished += OnSetupFinished;
                }

                return setupRunner;
            }
        }

        /// <summary>From the notification: the update command when there is one that can run, else the installer's window.</summary>
        private void UpdateOrRunSetup(SetupAppRow row)
        {
            if (row.Update != null && (row.Update.CanRun || row.Update.CanAsk))
            {
                RequestUpdate(row);
            }
            else
            {
                RunSetup(row);
            }
        }

        private static Dictionary<string, string> StoredSetupFiles(QuiverSettings settings, string folderName)
        {
            return settings.SetupFiles != null && settings.SetupFiles.TryGetValue(folderName, out var files) && files != null
                ? new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Asks for each file the update still needs (and remembers it); false when the player cancels (UI thread).</summary>
        private bool AskSetupFiles(SetupAppRow row)
        {
            while (row.Update != null && row.Update.CanAsk)
            {
                var name = row.Update.Missing[0];
                var label = ResourceProvider.GetString("LOCQuiverSetupInput" + name);
                if (string.IsNullOrEmpty(label) || label.StartsWith("<!", StringComparison.Ordinal))
                {
                    label = name;
                }

                var go = PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("LOCQuiverSetupAskFile"), row.Name, label), row.Name,
                    System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Information);
                if (go != System.Windows.MessageBoxResult.OK)
                {
                    return false;
                }

                var path = PlayniteApi.Dialogs.SelectFile(string.Equals(name, "Image", StringComparison.OrdinalIgnoreCase)
                    ? Loc("LOCQuiverSetupImageFilter")
                    : Loc("LOCQuiverSetupAnyFileFilter"));
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                var settings = LoadSettings();
                var all = new Dictionary<string, Dictionary<string, string>>(settings.SetupFiles ?? new Dictionary<string, Dictionary<string, string>>(), StringComparer.OrdinalIgnoreCase);
                var files = StoredSetupFiles(settings, row.FolderName);
                files[name] = path;
                all[row.FolderName] = files;
                settings.SetupFiles = all;
                SavePluginSettings(settings);
                Logger.Info($"{row.Name}: {name} for its setup = {path}.");

                row.Files = files;
                row.Update = SetupCommands.PrepareUpdate(row.AppDir, row.Setup, row.PlayPath, SetupCommands.Existing(files));
            }

            return row.Update != null && row.Update.CanRun;
        }

        /// <summary>Forgets the files chosen for an app's setup commands; they are asked again next time.</summary>
        internal void ForgetSetupFiles(SetupAppRow row)
        {
            var settings = LoadSettings();
            if (settings.SetupFiles != null && settings.SetupFiles.Remove(row.FolderName))
            {
                SavePluginSettings(settings);
                Logger.Info($"{row.Name}: forgot the files chosen for its setup.");
            }
        }

        /// <summary>Asks before queueing the update command of <paramref name="row"/> (UI thread).</summary>
        internal void RequestUpdate(SetupAppRow row)
        {
            if (row.Update != null && row.Update.CanAsk && !AskSetupFiles(row))
            {
                return;
            }

            var command = row.Update;
            if (command == null || !command.CanRun)
            {
                return;
            }

            var answer = PlayniteApi.Dialogs.ShowMessage(
                string.Format(Loc("LOCQuiverSetupConfirmUpdate"), row.Name, command.Display, command.WorkingDirectory),
                row.Name, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            if (!Runner.Enqueue(row, command))
            {
                PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("LOCQuiverSetupAlreadyQueued"), row.Name), row.Name);
            }
        }

        private void OnSetupFinished(SetupJob job)
        {
            SetGameInstalling(job.Row.FolderName, false);
            var row = job.Row;
            var playMissing = row.PlayPath != null && !File.Exists(row.PlayPath);
            if (job.State == SetupJobState.Succeeded && !playMissing)
            {
                MarkSetupDone(row);
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    SetupNotificationId(row.FolderName), string.Format(Loc("LOCQuiverSetupUpdateDone"), row.Name), NotificationType.Info));
            }
            else if (job.State != SetupJobState.Canceled)
            {
                var reason = playMissing ? string.Format(Loc("LOCQuiverSetupPlayMissing"), row.PlayPath) : string.Format(Loc("LOCQuiverSetupExitCode"), job.ExitCode?.ToString() ?? "?");
                var logPath = job.LogPath;
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    SetupNotificationId(row.FolderName), string.Format(Loc("LOCQuiverSetupUpdateFailed"), row.Name, reason),
                    NotificationType.Error, () => OpenFile(logPath)));
            }
        }

        /// <summary>The game shows as installing while its setup runs, so it isn't started half updated.</summary>
        private void SetGameInstalling(string folderName, bool installing)
        {
            try
            {
                PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                {
                    var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.PluginId == Id && string.Equals(g.GameId, folderName, StringComparison.OrdinalIgnoreCase));
                    if (game != null && game.IsInstalling != installing)
                    {
                        game.IsInstalling = installing;
                        PlayniteApi.Database.Games.Update(game);
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot mark {folderName} as installing={installing}.");
            }
        }

        /// <summary>The sidebar icon's bar: how much of the current batch of setups has finished.</summary>
        private void UpdateSidebarProgress()
        {
            if (setupSidebar == null || setupRunner == null)
            {
                return;
            }

            setupRunner.Progress(out var active, out var batch);
            PlayniteApi.MainView.UIDispatcher.BeginInvoke((Action)(() =>
            {
                // A running job counts as half done, so the bar shows something from the start.
                setupSidebar.ProgressMaximum = active == 0 ? 0 : batch;
                setupSidebar.ProgressValue = active == 0 ? 0 : batch - active + 0.5;
            }));
        }

        internal static void OpenFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot open {path}.");
            }
        }

        internal void OpenAppFolder(SetupAppRow row) => OpenFolder(row.AppDir);

        private SidebarItem setupSidebar;

        /// <summary>Desktop mode only (Fullscreen has no sidebar): the panel of the apps set up by hand.</summary>
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            if (setupSidebar == null)
            {
                setupSidebar = new SidebarItem
                {
                    Title = Loc("LOCQuiverSetupPanelTitle"),
                    Type = SiderbarItemType.View,
                    Icon = LibraryIcon,
                    Opened = () => new SetupPanelView(this),
                };
            }

            yield return setupSidebar;
        }

        /// <summary>
        /// The program to run again after an update: the "Setup" action of an installer, the play program
        /// of a launcher that builds the game, or (no programs in its entry) what Quiver itself would start,
        /// which is the installer when it is the only program at the top of the app folder. Null: none known.
        /// </summary>
        private static LaunchTarget SetupProgram(ExecutablePlan plan, SetupInfo setup, bool automatic)
        {
            if (plan == null)
            {
                return null;
            }

            var named = plan.Others.FirstOrDefault(o => string.Equals(o.Name, "Setup", StringComparison.OrdinalIgnoreCase));
            if (named != null)
            {
                return named;
            }

            return setup.IsBuilder || automatic ? plan.Play : null;
        }

        private static void RunSetup(LaunchTarget program, string appDir)
        {
            if (program == null || !File.Exists(program.Path))
            {
                OpenFolder(appDir);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(program.Path, program.Arguments ?? string.Empty)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(program.Path),
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot start {program.Path}.");
                OpenFolder(appDir);
            }
        }

        #endregion

        #region Ludusavi

        private const string LudusaviNotificationId = "quiver-ludusavi";

        /// <summary>See <see cref="QuiverSettings.LudusaviBackup"/>; run when the setting is turned on, for the games already in Playnite.</summary>
        internal void UpdateLudusaviFromDatabase()
        {
            try
            {
                var games = PlayniteApi.Database.Games
                    .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
                    .GroupBy(g => g.GameId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var executables = LoadExecutables();
                var settings = LoadSettings();
                var apps = GetData().GetStates()
                    .Where(s => games.ContainsKey(s.App.FolderName))
                    .Select(s => new KeyValuePair<QuiverAppState, ExecutablePlan>(s, PlanFor(s, executables, settings)))
                    .ToList();
                UpdateLudusavi(apps, executables, s => games[s.App.FolderName].Name);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot set up the Ludusavi backups.");
            }
        }

        /// <summary>
        /// Writes the Ludusavi secondary manifest with the apps' save folders (see <see cref="QuiverSaves.ForBackup"/>)
        /// and makes sure Ludusavi's config lists it; the Ludusavi extension then backs them up like any other game.
        /// </summary>
        private void UpdateLudusavi(IEnumerable<KeyValuePair<QuiverAppState, ExecutablePlan>> apps, ExecutableDatabase executables, Func<QuiverAppState, string> nameOf)
        {
            try
            {
                var games = new List<LudusaviGame>();
                var unknown = new List<string>();
                foreach (var app in apps)
                {
                    var state = app.Key;
                    var name = nameOf(state);
                    var folders = QuiverSaves.ForBackup(state.InstallDirectory, app.Value?.Play?.Path, executables.FindUserData(state.App),
                        executables.Find(state.App)?.Play?.SearchSubfolders == true);
                    if (folders.Count == 0)
                    {
                        unknown.Add(name);
                    }
                    else
                    {
                        games.Add(new LudusaviGame { Name = name, Folders = folders });
                    }
                }

                var manifestPath = Path.Combine(GetPluginUserDataPath(), LudusaviManifest.FileName);
                var content = LudusaviManifest.Build(games);
                if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != content)
                {
                    File.WriteAllText(manifestPath, content, new UTF8Encoding(false));
                    Logger.Info($"Wrote the Ludusavi manifest with {games.Count} Quiver game(s): {manifestPath}.");
                }

                if (unknown.Count > 0)
                {
                    Logger.Info($"Quiver games left out of the Ludusavi backups, their save folders are unknown ({unknown.Count}): {string.Join(", ", unknown.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}.");
                }

                var configPath = LudusaviConfig.FindConfigPath(LudusaviExecutable(), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetEnvironmentVariable("PATH"));
                var result = LudusaviConfig.Register(configPath, manifestPath);
                Logger.Info($"Ludusavi config {configPath}: {result}.");
                switch (result)
                {
                    case LudusaviRegistration.AlreadyRegistered:
                        PlayniteApi.Notifications.Remove(LudusaviNotificationId);
                        break;
                    case LudusaviRegistration.Added:
                        PlayniteApi.Notifications.Add(new NotificationMessage(LudusaviNotificationId,
                            string.Format(Loc("LOCQuiverLudusaviAdded"), games.Count, configPath), NotificationType.Info));
                        break;
                    case LudusaviRegistration.NoConfig:
                        PlayniteApi.Notifications.Add(new NotificationMessage(LudusaviNotificationId,
                            string.Format(Loc("LOCQuiverLudusaviNoConfig"), configPath), NotificationType.Error));
                        break;
                    default:
                        PlayniteApi.Notifications.Add(new NotificationMessage(LudusaviNotificationId,
                            string.Format(Loc("LOCQuiverLudusaviManual"), configPath, manifestPath), NotificationType.Error, () => OpenFolder(Path.GetDirectoryName(manifestPath))));
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot update the Ludusavi backups of the Quiver games.");
            }
        }

        /// <summary>The Ludusavi program set in the Ludusavi extension, or null (then found on PATH).</summary>
        private string LudusaviExecutable()
        {
            var config = Path.Combine(Directory.GetParent(GetPluginUserDataPath()).FullName, LudusaviPlayniteSettings.PluginId, "config.json");
            return LudusaviPlayniteSettings.ReadExecutablePath(config);
        }

        #endregion

        #region Folders

        private static void OpenFolder(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot open {folder}.");
            }
        }

        #endregion

        #region Actions

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new QuiverInstallController(args.Game, this);
            }
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new QuiverUninstallController(args.Game, this);
            }
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            var game = args.Game;
            LaunchTarget exe = null;
            string problem = null;
            try
            {
                var state = GetAppState(game.GameId);
                if (state == null)
                {
                    problem = string.Format(Loc("LOCQuiverPlayNotInLibrary"), game.Name);
                }
                else if (!state.IsInstalled)
                {
                    problem = string.Format(Loc("LOCQuiverPlayNotInstalled"), game.Name);
                }
                else
                {
                    var executables = LoadExecutables();
                    var entry = executables.Find(state.App);
                    exe = PlanFor(state, executables, LoadSettings())?.Play;
                    if (exe == null)
                    {
                        // An installer that puts the game where the player chooses: not run yet, or somewhere else.
                        problem = string.Format(Loc(entry?.Play?.SearchSubfolders == true ? "LOCQuiverPlayNotSetUp" : "LOCQuiverPlayNoExecutable"), game.Name);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot resolve the executable of Quiver app {game.GameId}.");
                problem = e.Message;
            }

            if (exe == null)
            {
                yield return new QuiverErrorPlayController(game, problem);
                yield break;
            }

            if (exe.Environment.Count > 0)
            {
                yield return new QuiverProcessPlayController(game, exe);
                yield break;
            }

            yield return new AutomaticPlayController(game)
            {
                Name = game.Name,
                Type = AutomaticPlayActionType.File,
                Path = exe.Path,
                Arguments = exe.Arguments,
                WorkingDir = Path.GetDirectoryName(exe.Path),
                TrackingMode = TrackingMode.Default,
            };
        }

        #endregion

        #region Game menu

        // No "@" here: game menu sections are submenus of the game's context menu; "|" nests them.
        private const string GameMenuSection = "Recomps (Quiver Launcher)";

        private static string ChosenExecutable(QuiverSettings settings, string folderName)
        {
            var chosen = settings?.ChosenExecutables;
            if (chosen == null || string.IsNullOrEmpty(folderName))
            {
                return null;
            }

            return chosen.FirstOrDefault(c => string.Equals(c.Key, folderName, StringComparison.OrdinalIgnoreCase)).Value;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count != 1 || args.Games[0].PluginId != Id)
            {
                return Enumerable.Empty<GameMenuItem>();
            }

            var game = args.Games[0];
            var items = new List<GameMenuItem>();
            try
            {
                var state = GetAppState(game.GameId);
                if (state == null)
                {
                    return items;
                }

                var executables = LoadExecutables();
                var settings = LoadSettings();
                var plan = PlanFor(state, executables, settings);
                var saves = QuiverSaves.Find(state.InstallDirectory, plan?.Play?.Path, executables.FindUserData(state.App),
                    executables.Find(state.App)?.Play?.SearchSubfolders == true);
                if (saves.Count == 1)
                {
                    items.Add(new GameMenuItem
                    {
                        MenuSection = GameMenuSection,
                        Description = Loc("LOCQuiverMenuOpenSaves"),
                        Action = a => OpenSaveFolder(game, saves[0]),
                    });
                }
                else
                {
                    items.AddRange(saves.Select(folder => new GameMenuItem
                    {
                        MenuSection = GameMenuSection + "|" + Loc("LOCQuiverMenuOpenSaves"),
                        Description = folder.Path,
                        Action = a => OpenSaveFolder(game, folder),
                    }));
                }

                if (state.IsInstalled)
                {
                    items.Add(new GameMenuItem
                    {
                        MenuSection = GameMenuSection,
                        Description = Loc("LOCQuiverMenuChooseExecutable"),
                        Action = a => ChooseExecutable(game),
                    });
                }

                if (ChosenExecutable(settings, state.App.FolderName) != null)
                {
                    items.Add(new GameMenuItem
                    {
                        MenuSection = GameMenuSection,
                        Description = Loc("LOCQuiverMenuResetExecutable"),
                        Action = a => SetChosenExecutable(game, null),
                    });
                }

                var achievements = executables.FindAchievements(state.App);
                if (settings.AchievementsExport && state.IsInstalled && RecompAchievements.CanRead(achievements))
                {
                    items.Add(new GameMenuItem
                    {
                        MenuSection = GameMenuSection,
                        Description = Loc("LOCQuiverMenuExportAchievements"),
                        Action = a => ExportAchievements(game, state.InstallDirectory, achievements),
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot build the Quiver menu of {game.Name}.");
            }

            return items;
        }

        private void OpenSaveFolder(Game game, SaveFolder folder)
        {
            if (folder.Exists)
            {
                OpenFolder(folder.Path);
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("LOCQuiverSavesNotYet"), game.Name, folder.Path), game.Name);
            }
        }

        /// <summary>
        /// Experimental, until Playnite Achievements offers a way for other extensions: writes the app's
        /// achievements as a CSV that the player imports in that extension's editor (also rewritten when
        /// the game stops, see <see cref="OnGameStopped"/>).
        /// </summary>
        private void ExportAchievements(Game game, string appDir, AchievementsInfo info)
        {
            try
            {
                var path = WriteAchievementsCsv(game, appDir, info, out var achievements);
                if (path == null)
                {
                    PlayniteApi.Dialogs.ShowMessage(string.Format(Loc("LOCQuiverAchievementsNotYet"), game.Name), game.Name);
                    return;
                }

                PlayniteApi.Dialogs.ShowMessage(
                    string.Format(Loc("LOCQuiverAchievementsExported"), game.Name, achievements.Count, achievements.Count(a => a.Unlocked), path),
                    game.Name);
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot export the achievements of {game.Name}.");
                PlayniteApi.Dialogs.ShowErrorMessage(e.Message, game.Name);
            }
        }

        /// <summary>
        /// Writes <c>achievements\&lt;game&gt;.csv</c> in the extension's data folder; returns its path, or null
        /// when the app has no achievements to read yet.
        /// </summary>
        private string WriteAchievementsCsv(Game game, string appDir, AchievementsInfo info, out List<RecompAchievement> achievements)
        {
            achievements = RecompAchievements.Read(appDir, info);
            if (achievements == null || achievements.Count == 0)
            {
                return null;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var fileName = new string(game.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()) + ".csv";
            var folder = Path.Combine(GetPluginUserDataPath(), "achievements");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllText(path, PlayniteAchievementsCsv.Build(achievements), new UTF8Encoding(true));
            Logger.Info($"Wrote {achievements.Count} achievements of {game.Name} ({achievements.Count(a => a.Unlocked)} unlocked) to {path}.");
            return path;
        }

        /// <summary>For games whose installer puts them where the player chooses, or to override the database.</summary>
        private void ChooseExecutable(Game game)
        {
            var path = PlayniteApi.Dialogs.SelectFile("Programs|*.exe;*.bat;*.cmd");
            if (!string.IsNullOrWhiteSpace(path))
            {
                SetChosenExecutable(game, path);
            }
        }

        /// <summary>Saves (or with null, forgets) the game's chosen play program and refreshes its other actions.</summary>
        private void SetChosenExecutable(Game game, string path)
        {
            try
            {
                var state = GetAppState(game.GameId);
                if (state == null)
                {
                    return;
                }

                var settings = LoadSettings();
                var chosen = (settings.ChosenExecutables ?? new Dictionary<string, string>())
                    .Where(c => !string.Equals(c.Key, state.App.FolderName, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(c => c.Key, c => c.Value);
                if (path != null)
                {
                    chosen[state.App.FolderName] = ExecutablePlanner.StoredChosenPath(state.InstallDirectory, path);
                }

                settings.ChosenExecutables = chosen;
                SavePluginSettings(settings);
                Logger.Info(path == null ? $"{game.Name}: back to the default play program." : $"{game.Name}: plays {path} (chosen by the user).");
                RefreshOtherActions(game);

                var play = PlanFor(state, LoadExecutables(), settings)?.Play?.Path;
                PlayniteApi.Dialogs.ShowMessage(play == null
                    ? string.Format(Loc("LOCQuiverPlayNotSetUp"), game.Name)
                    : string.Format(Loc("LOCQuiverChosenExecutable"), game.Name, play), game.Name);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot save the program chosen for {game.Name}.");
                PlayniteApi.Dialogs.ShowErrorMessage(e.Message, game.Name);
            }
        }

        #endregion

        #region Main menu

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc("LOCQuiverMenuInstallFromLibrary"),
                Action = a => InstallFromQuiverLibrary(),
            };
            yield return new MainMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc("LOCQuiverMenuUpdateAll"),
                Action = a => UpdateAll(),
            };
            yield return new MainMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc("LOCQuiverMenuRefresh"),
                Action = a => RefreshInstallStatusFromMenu(),
            };
            yield return new MainMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc("LOCQuiverMenuOpenQuiver"),
                Action = a => Client.Open(),
            };
            yield return new MainMenuItem
            {
                MenuSection = MenuSection,
                Description = Loc("LOCQuiverMenuSettings"),
                Action = a => OpenSettingsView(),
            };
        }

        private void UpdateAll()
        {
            QuiverInstallation install;
            try
            {
                install = RequireInstallation();
            }
            catch (Exception e)
            {
                Notify("update", e.Message, NotificationType.Error);
                return;
            }

            QuiverCliResult result = null;
            Exception error = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    result = QuiverCli.Run(install.CliExe, new[] { "--update" }, CancellationToken.None);
                }
                catch (Exception e)
                {
                    error = e;
                }
            }, new GlobalProgressOptions(Loc("LOCQuiverProgressUpdating"), false));

            if (error != null)
            {
                Logger.Error(error, "Quiver --update failed.");
                Notify("update", string.Format(Loc("LOCQuiverUpdateFailed"), error.Message), NotificationType.Error);
                return;
            }

            TryRefreshInstallStatus();
            if (result.Success)
            {
                Notify("update", Loc("LOCQuiverUpdateDone"), NotificationType.Info);
            }
            else
            {
                Logger.Warn($"Quiver --update exited with {result.ExitCode}:{Environment.NewLine}{result.Output}");
                Notify("update", string.Format(Loc("LOCQuiverUpdatePartial"), result.Summary()), NotificationType.Error);
            }
        }

        private sealed class QuiverAppOption : GenericItemOption
        {
            public QuiverAppState State { get; }
            public string DisplayName { get; }

            public QuiverAppOption(QuiverAppState state, string displayName)
                : base(displayName, string.IsNullOrWhiteSpace(state.App.Repository) ? state.App.Project : state.App.Repository)
            {
                State = state;
                DisplayName = displayName;
            }
        }

        /// <summary>
        /// Lists the apps of Quiver's library that aren't installed (they're never imported
        /// into Playnite), installs the chosen one with <c>--download</c> and adds it to
        /// Playnite. Apps only in Quiver's App Catalog can't be installed this way: Quiver's
        /// command line only finds apps already in its library.
        /// </summary>
        private void InstallFromQuiverLibrary()
        {
            QuiverInstallation install;
            List<QuiverAppOption> options;
            try
            {
                install = RequireInstallation();
                var states = GetData().GetStates();
                var names = QuiverGameNaming.BuildNames(states.Select(s => s.App));
                options = states
                    .Where(s => !s.IsInstalled)
                    .Select(s => new QuiverAppOption(s, names[s.App.FolderName]))
                    .OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot list Quiver library apps.");
                Notify("install-list", e.Message, NotificationType.Error);
                return;
            }

            if (options.Count == 0)
            {
                Notify("install-list", Loc("LOCQuiverInstallListEmpty"), NotificationType.Info);
                return;
            }

            var picked = PlayniteApi.Dialogs.ChooseItemWithSearch(
                options.Cast<GenericItemOption>().ToList(),
                search => options
                    .Where(o => string.IsNullOrWhiteSpace(search) ||
                                o.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                (o.Description ?? "").IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                o.State.App.FolderName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Cast<GenericItemOption>()
                    .ToList(),
                null,
                Loc("LOCQuiverInstallListCaption")) as QuiverAppOption;
            if (picked == null)
            {
                return;
            }

            var folderName = picked.State.App.FolderName;
            QuiverCliResult result = null;
            Exception error = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    result = RunDownload(install, folderName);
                }
                catch (Exception e)
                {
                    error = e;
                }
            }, new GlobalProgressOptions(string.Format(Loc("LOCQuiverProgressInstalling"), picked.DisplayName), false));

            QuiverAppState state = null;
            string reason;
            if (error != null)
            {
                Logger.Error(error, $"Quiver install of {folderName} failed.");
                reason = error.Message;
            }
            else
            {
                try
                {
                    state = GetAppState(folderName);
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Cannot read Quiver state of {folderName} after installing it.");
                }

                if (result.Success && state != null && state.IsInstalled)
                {
                    AddOrRefreshInstalledGame(state, picked.DisplayName);
                    return;
                }

                Logger.Warn($"Quiver --download {folderName} exited with {result.ExitCode}:{Environment.NewLine}{result.Output}");
                reason = result.Success ? Loc("LOCQuiverInstallNoFiles") : result.Summary();
            }

            NotifyInstallFailed(folderName, picked.DisplayName, result, reason);
        }

        /// <summary>Imports a just-installed app, or updates it if it was already in Playnite (installed and uninstalled before).</summary>
        private void AddOrRefreshInstalledGame(QuiverAppState state, string displayName)
        {
            var folderName = state.App.FolderName;
            try
            {
                var existing = PlayniteApi.Database.Games.FirstOrDefault(g =>
                    g.PluginId == Id && string.Equals(g.GameId, folderName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.IsInstalled = true;
                    existing.InstallDirectory = state.InstallDirectory;
                    if (state.Version != null)
                    {
                        existing.Version = state.Version;
                    }

                    PlayniteApi.Database.Games.Update(existing);
                }
                else
                {
                    PlayniteApi.Database.ImportGame(ToMetadata(state, displayName, DisabledFeatures()), this);
                }

                Notify($"install-{folderName}", string.Format(Loc("LOCQuiverInstallDone"), displayName), NotificationType.Info);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot add Quiver app {folderName} to Playnite.");
                Notify($"install-{folderName}", string.Format(Loc("LOCQuiverInstallDoneNotAdded"), displayName, e.Message), NotificationType.Error);
            }
        }

        private void RefreshInstallStatusFromMenu()
        {
            var changed = TryRefreshInstallStatus();
            if (changed >= 0)
            {
                Notify("refresh", string.Format(Loc("LOCQuiverRefreshDone"), changed), NotificationType.Info);
            }
        }

        /// <summary>Never throws: -1 (and an error notification) when Quiver's library can't be read.</summary>
        private int TryRefreshInstallStatus()
        {
            try
            {
                return RefreshInstallStatus();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot refresh Quiver install status.");
                Notify("refresh", string.Format(Loc("LOCQuiverRefreshFailed"), e.Message), NotificationType.Error);
                return -1;
            }
        }

        /// <summary>
        /// Copies Quiver's install state (and version) to the games already in
        /// Playnite, without importing new ones. Skips games Playnite is busy with.
        /// </summary>
        internal int RefreshInstallStatus()
        {
            var data = GetData();
            var appsFolder = data.AppsFolder;
            var apps = data.LoadApps().ToDictionary(a => a.FolderName, StringComparer.OrdinalIgnoreCase);
            var changed = 0;
            var states = new List<QuiverAppState>();

            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var game in PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList())
                {
                    if (game.IsInstalling || game.IsUninstalling || game.IsRunning || game.IsLaunching)
                    {
                        continue;
                    }

                    if (!apps.TryGetValue(game.GameId, out var app))
                    {
                        continue;
                    }

                    var state = data.GetState(app, appsFolder);
                    states.Add(state);
                    var dir = state.IsInstalled ? state.InstallDirectory : null;
                    var versionChanged = state.IsInstalled && state.Version != null && game.Version != state.Version;
                    if (game.IsInstalled == state.IsInstalled &&
                        string.Equals(game.InstallDirectory, dir, StringComparison.OrdinalIgnoreCase) &&
                        !versionChanged)
                    {
                        continue;
                    }

                    game.IsInstalled = state.IsInstalled;
                    game.InstallDirectory = dir;
                    if (versionChanged)
                    {
                        game.Version = state.Version;
                    }

                    PlayniteApi.Database.Games.Update(game);
                    changed++;
                }
            }

            CheckSetupUpdates(states);
            return changed;
        }

        #endregion

        internal static string Loc(string key) => ResourceProvider.GetString(key);

        internal void Notify(string id, string text, NotificationType type)
        {
            PlayniteApi.Notifications.Add(new NotificationMessage($"quiver-{id}", text, type));
        }
    }
}
