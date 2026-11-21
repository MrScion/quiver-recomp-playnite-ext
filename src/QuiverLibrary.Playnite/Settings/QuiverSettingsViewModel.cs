using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using QuiverLibrary.Core;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Settings
{
    /// <summary>One checkbox of the "features to import" list.</summary>
    public sealed class QuiverFeatureOption : ObservableObject
    {
        private bool _isEnabled;

        public string Key { get; }
        public string Name { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetValue(ref _isEnabled, value);
        }

        public QuiverFeatureOption(QuiverFeature feature, bool isEnabled)
        {
            Key = feature.Key;
            Name = feature.Name;
            _isEnabled = isEnabled;
        }
    }

    /// <summary>One entry of the internal resolution list: a height, 0 = the game's own.</summary>
    public sealed class QuiverResolutionOption
    {
        public int Height { get; }
        public string Name { get; }

        public QuiverResolutionOption(int height, string name)
        {
            Height = height;
            Name = name;
        }
    }

    public class QuiverSettingsViewModel : ObservableObject, ISettings
    {
        private readonly QuiverLibraryPlugin _plugin;

        private List<bool> _beforeFeatures;
        private string _beforeQuiverFolder;
        private string _beforeGamesFolder;
        private string _gamesFolder;
        private bool _beforeSyncLastPlayed;
        private bool _syncLastPlayed;
        private bool _beforeGamepadExit;
        private bool _gamepadExit;
        private bool _beforeMakePortable;
        private bool _makePortable;
        private bool _beforeLudusaviBackup;
        private bool _ludusaviBackup;
        private bool _beforeAchievementsExport;
        private bool _achievementsExport;
        private bool _beforeDisplayFullscreen;
        private bool _displayFullscreen;
        private int _beforeDisplayHeight;
        private int _displayHeight;
        private string _quiverFolder;
        private string _status;
        private bool _statusIsError;

        public string QuiverFolder
        {
            get => _quiverFolder;
            set
            {
                SetValue(ref _quiverFolder, value);
                UpdateStatus();
            }
        }

        /// <summary>Folder for games their compiler or installer puts outside the app; empty = &lt;Quiver&gt;\games.</summary>
        public string GamesFolder
        {
            get => _gamesFolder;
            set => SetValue(ref _gamesFolder, value);
        }

        /// <summary>What was found at <see cref="QuiverFolder"/> — refreshed as the user types.</summary>
        public string Status
        {
            get => _status;
            private set => SetValue(ref _status, value);
        }

        public bool StatusIsError
        {
            get => _statusIsError;
            private set => SetValue(ref _statusIsError, value);
        }

        public bool SyncLastPlayed
        {
            get => _syncLastPlayed;
            set => SetValue(ref _syncLastPlayed, value);
        }

        public bool GamepadExit
        {
            get => _gamepadExit;
            set => SetValue(ref _gamepadExit, value);
        }

        public bool MakePortable
        {
            get => _makePortable;
            set => SetValue(ref _makePortable, value);
        }

        public bool LudusaviBackup
        {
            get => _ludusaviBackup;
            set => SetValue(ref _ludusaviBackup, value);
        }

        public bool AchievementsExport
        {
            get => _achievementsExport;
            set => SetValue(ref _achievementsExport, value);
        }

        /// <summary>Common display setting: start the games in full screen.</summary>
        public bool DisplayFullscreen
        {
            get => _displayFullscreen;
            set => SetValue(ref _displayFullscreen, value);
        }

        /// <summary>Common display setting: internal render height, 0 = the game's own.</summary>
        public int DisplayHeight
        {
            get => _displayHeight;
            set => SetValue(ref _displayHeight, value);
        }

        public List<QuiverResolutionOption> Resolutions { get; }

        public RelayCommand BrowseQuiverFolderCommand { get; }

        public RelayCommand BrowseGamesFolderCommand { get; }

        /// <summary>Port features Playnite imports from Quiver's tags; unticked ones are left out.</summary>
        public List<QuiverFeatureOption> Features { get; }

        public QuiverSettingsViewModel(QuiverLibraryPlugin plugin)
        {
            _plugin = plugin;
            var settings = plugin.LoadSettings();
            _quiverFolder = settings.QuiverFolder;
            _gamesFolder = settings.GamesFolder;
            _syncLastPlayed = settings.SyncLastPlayed;
            _gamepadExit = settings.GamepadExit;
            _makePortable = settings.MakePortable;
            _ludusaviBackup = settings.LudusaviBackup;
            _achievementsExport = settings.AchievementsExport;
            _displayFullscreen = settings.Display?.Fullscreen ?? false;
            _displayHeight = settings.Display?.ResolutionHeight ?? 0;
            Resolutions = new List<QuiverResolutionOption> { new QuiverResolutionOption(0, ResourceProvider.GetString("LOCQuiverSettingsResolutionDefault")) };
            Resolutions.AddRange(DisplayPlanner.Heights.Select(h => new QuiverResolutionOption(h, ResourceProvider.GetString("LOCQuiverSettingsResolution" + h))));
            if (Resolutions.All(r => r.Height != _displayHeight))
            {
                Resolutions.Add(new QuiverResolutionOption(_displayHeight, _displayHeight + "p"));
            }
            var disabled = new HashSet<string>(settings.DisabledFeatures ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            Features = QuiverTagMapping.Features.Select(f => new QuiverFeatureOption(f, !disabled.Contains(f.Key))).ToList();

            BrowseQuiverFolderCommand = new RelayCommand(() =>
            {
                var folder = _plugin.PlayniteApi.Dialogs.SelectFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    QuiverFolder = folder;
                }
            });

            BrowseGamesFolderCommand = new RelayCommand(() =>
            {
                var folder = _plugin.PlayniteApi.Dialogs.SelectFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    GamesFolder = folder;
                }
            });

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var install = QuiverInstallation.Detect(QuiverFolder, QuiverLibraryPlugin.LocalAppData);
            if (install == null)
            {
                StatusIsError = true;
                Status = string.IsNullOrWhiteSpace(QuiverFolder)
                    ? ResourceProvider.GetString("LOCQuiverSettingsStatusNotDetected")
                    : string.Format(ResourceProvider.GetString("LOCQuiverSettingsStatusNoExe"),
                        string.Join(" · ", QuiverInstallation.SearchedExePaths(QuiverFolder)));
                return;
            }

            var data = new QuiverLibraryData(install.DataRoot);
            if (!data.HasAppsJson)
            {
                StatusIsError = true;
                Status = string.Format(ResourceProvider.GetString("LOCQuiverSettingsStatusNoLibrary"), install.DataRoot);
                return;
            }

            try
            {
                StatusIsError = false;
                Status = string.Format(ResourceProvider.GetString("LOCQuiverSettingsStatusOk"), install.DataRoot, data.LoadApps().Count);
            }
            catch (Exception ex)
            {
                StatusIsError = true;
                Status = string.Format(ResourceProvider.GetString("LOCQuiverAppsJsonUnreadable"), data.AppsJsonPath, ex.Message);
            }
        }

        public void BeginEdit()
        {
            _beforeQuiverFolder = QuiverFolder;
            _beforeGamesFolder = GamesFolder;
            _beforeSyncLastPlayed = SyncLastPlayed;
            _beforeGamepadExit = GamepadExit;
            _beforeMakePortable = MakePortable;
            _beforeLudusaviBackup = LudusaviBackup;
            _beforeAchievementsExport = AchievementsExport;
            _beforeDisplayFullscreen = DisplayFullscreen;
            _beforeDisplayHeight = DisplayHeight;
            _beforeFeatures = Features.Select(f => f.IsEnabled).ToList();
        }

        public void CancelEdit()
        {
            QuiverFolder = _beforeQuiverFolder;
            GamesFolder = _beforeGamesFolder;
            SyncLastPlayed = _beforeSyncLastPlayed;
            GamepadExit = _beforeGamepadExit;
            MakePortable = _beforeMakePortable;
            LudusaviBackup = _beforeLudusaviBackup;
            AchievementsExport = _beforeAchievementsExport;
            DisplayFullscreen = _beforeDisplayFullscreen;
            DisplayHeight = _beforeDisplayHeight;
            for (var i = 0; i < Features.Count && _beforeFeatures != null && i < _beforeFeatures.Count; i++)
            {
                Features[i].IsEnabled = _beforeFeatures[i];
            }
        }

        public void EndEdit()
        {
            // Reloaded so fields the plugin itself saves (cleanup flag, warned apps) aren't overwritten with stale values.
            var settings = _plugin.LoadSettings();
            settings.QuiverFolder = (QuiverFolder ?? "").Trim();
            settings.GamesFolder = (GamesFolder ?? "").Trim();
            ExecutablePlanner.GamesDirOverride = settings.GamesFolder;
            settings.DisabledFeatures = Features.Where(f => !f.IsEnabled).Select(f => f.Key).ToList();
            settings.SyncLastPlayed = SyncLastPlayed;
            settings.GamepadExit = GamepadExit;
            var portableTurnedOn = MakePortable && !settings.MakePortable;
            settings.MakePortable = MakePortable;
            var ludusaviTurnedOn = LudusaviBackup && !settings.LudusaviBackup;
            settings.LudusaviBackup = LudusaviBackup;
            settings.AchievementsExport = AchievementsExport;
            settings.Display = new DisplayPreferences { Fullscreen = DisplayFullscreen, ResolutionHeight = DisplayHeight };
            _plugin.SavePluginSettings(settings);
            if (_plugin.FindInstallation() != null)
            {
                _plugin.PlayniteApi.Notifications.Remove(QuiverLibraryPlugin.NotConfiguredNotificationId);
            }

            if (portableTurnedOn)
            {
                _plugin.MakeInstalledAppsPortable();
            }

            if (ludusaviTurnedOn)
            {
                _plugin.UpdateLudusaviFromDatabase();
            }
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            // Empty means "Quiver's default location", which may legitimately not exist yet.
            if (!string.IsNullOrWhiteSpace(QuiverFolder) && QuiverInstallation.FromPath(QuiverFolder) == null)
            {
                errors.Add(string.Format(ResourceProvider.GetString("LOCQuiverSettingsStatusNoExe"),
                    string.Join(" · ", QuiverInstallation.SearchedExePaths(QuiverFolder))));
            }

            return errors.Count == 0;
        }
    }
}
