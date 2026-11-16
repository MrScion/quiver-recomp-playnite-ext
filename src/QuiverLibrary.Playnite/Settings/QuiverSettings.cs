using System.Collections.Generic;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Settings
{
    /// <summary>
    /// Persisted settings — plain POCO, (de)serialized by Playnite itself via
    /// <c>LoadPluginSettings</c>/<c>SavePluginSettings</c>; no direct JSON
    /// library dependency on purpose.
    /// </summary>
    public class QuiverSettings
    {
        /// <summary>
        /// Quiver Launcher's folder (the portable folder holding <c>current\</c>).
        /// Empty = Quiver's default per-user install location.
        /// </summary>
        public string QuiverFolder { get; set; } = "";

        /// <summary>
        /// <c>QuiverFeature.Key</c>s the user unticked. Stored as the disabled ones so that a
        /// feature added in a later version is imported by default.
        /// </summary>
        public List<string> DisabledFeatures { get; set; } = new List<string>();

        /// <summary>
        /// Keeps Playnite's last played time and Quiver's (<c>LastPlayed.txt</c>) the same, the
        /// newest one winning: on game start and on every library update.
        /// </summary>
        public bool SyncLastPlayed { get; set; } = true;

        /// <summary>
        /// Creates each installed app's portable marker (usually <c>portable.txt</c>) on every library
        /// update and install. Unticking it removes nothing.
        /// </summary>
        public bool MakePortable { get; set; }

        /// <summary>
        /// Folder names of the apps already warned about data in their usual folder, so each one
        /// is warned about once.
        /// </summary>
        public List<string> PortableWarnedApps { get; set; } = new List<string>();

        /// <summary>
        /// Lets the Ludusavi extension for Playnite back up the apps' saves: on every library update
        /// a Ludusavi secondary manifest lists them by their Playnite name, and it is added once to
        /// Ludusavi's <c>config.yaml</c>. Unticking it changes nothing in Ludusavi.
        /// </summary>
        public bool LudusaviBackup { get; set; }

        /// <summary>
        /// Experimental: exports the achievements of the apps whose files are understood as a CSV for the
        /// Playnite Achievements extension (game menu entry, rewritten when the game stops). Off by default.
        /// </summary>
        public bool AchievementsExport { get; set; }

        /// <summary>
        /// Play program chosen from the game's menu, by app folder name: relative to the app
        /// folder when inside it, absolute otherwise (<c>ExecutablePlanner.ChosenPath</c>).
        /// </summary>
        public Dictionary<string, string> ChosenExecutables { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Download (<c>QuiverSetupUpdates.Fingerprint</c>) the installed game was set up from, for each app that is
        /// set up by hand after downloading (installers, launchers that build the game), by app folder name: the one
        /// seen first, then the one the player marks as done. While Quiver's download differs, the setup is pending.
        /// </summary>
        public Dictionary<string, string> SetupVersions { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Files the player chose for the setup commands (<c>Image</c>, <c>TitleUpdate</c>, ...), by app folder name then
        /// placeholder name: asked once, used again on every update while they exist.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> SetupFiles { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Download already announced with a notification, by app folder name, so each one is announced once.</summary>
        public Dictionary<string, string> SetupNotified { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Display settings for every game whose <c>executables.json</c> entry says how to set them (<c>display</c>):
        /// full screen and internal resolution, applied when the game starts. The default changes nothing.
        /// </summary>
        public DisplayPreferences Display { get; set; } = new DisplayPreferences();

        /// <summary>
        /// A game's own display settings, by app folder name, used instead of <see cref="Display"/>. Not editable in the
        /// settings yet (<c>DisplayPlanner.Choose</c> already reads it).
        /// </summary>
        public Dictionary<string, DisplayPreferences> GameDisplay { get; set; } = new Dictionary<string, DisplayPreferences>();

        /// <summary>
        /// One-time removal of the not-installed games imported by builds that still
        /// imported Quiver's whole library. Done once so a game the user installs and
        /// later uninstalls is never removed by it.
        /// </summary>
        public bool NotInstalledCleanupDone { get; set; }
    }
}
