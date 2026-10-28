using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using QuiverLibrary.Core;

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

    public class QuiverSettingsViewModel : ObservableObject, ISettings
    {
        private readonly QuiverLibraryPlugin _plugin;

        private List<bool> _beforeFeatures;
        private string _beforeQuiverFolder;
        private bool _beforeSyncLastPlayed;
        private bool _syncLastPlayed;
        private bool _beforeMakePortable;
        private bool _makePortable;
        private bool _beforeLudusaviBackup;
        private bool _ludusaviBackup;
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

        public RelayCommand BrowseQuiverFolderCommand { get; }

        /// <summary>Port features Playnite imports from Quiver's tags; unticked ones are left out.</summary>
        public List<QuiverFeatureOption> Features { get; }

        public QuiverSettingsViewModel(QuiverLibraryPlugin plugin)
        {
            _plugin = plugin;
            var settings = plugin.LoadSettings();
            _quiverFolder = settings.QuiverFolder;
            _syncLastPlayed = settings.SyncLastPlayed;
            _makePortable = settings.MakePortable;
            _ludusaviBackup = settings.LudusaviBackup;
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
            _beforeSyncLastPlayed = SyncLastPlayed;
            _beforeMakePortable = MakePortable;
            _beforeLudusaviBackup = LudusaviBackup;
            _beforeFeatures = Features.Select(f => f.IsEnabled).ToList();
        }

        public void CancelEdit()
        {
            QuiverFolder = _beforeQuiverFolder;
            SyncLastPlayed = _beforeSyncLastPlayed;
            MakePortable = _beforeMakePortable;
            LudusaviBackup = _beforeLudusaviBackup;
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
            settings.DisabledFeatures = Features.Where(f => !f.IsEnabled).Select(f => f.Key).ToList();
            settings.SyncLastPlayed = SyncLastPlayed;
            var portableTurnedOn = MakePortable && !settings.MakePortable;
            settings.MakePortable = MakePortable;
            var ludusaviTurnedOn = LudusaviBackup && !settings.LudusaviBackup;
            settings.LudusaviBackup = LudusaviBackup;
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
