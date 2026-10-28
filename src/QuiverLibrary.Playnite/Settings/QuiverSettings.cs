using System.Collections.Generic;

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
        /// Play program chosen from the game's menu, by app folder name: relative to the app
        /// folder when inside it, absolute otherwise (<c>ExecutablePlanner.ChosenPath</c>).
        /// </summary>
        public Dictionary<string, string> ChosenExecutables { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// One-time removal of the not-installed games imported by builds that still
        /// imported Quiver's whole library. Done once so a game the user installs and
        /// later uninstalls is never removed by it.
        /// </summary>
        public bool NotInstalledCleanupDone { get; set; }
    }
}
