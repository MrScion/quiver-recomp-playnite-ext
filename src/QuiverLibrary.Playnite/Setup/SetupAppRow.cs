using QuiverLibrary.Core;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Setup
{
    /// <summary>An installed app whose game is set up by hand after Quiver downloads it (<see cref="SetupInfo"/>).</summary>
    public class SetupAppRow
    {
        public string Name { get; set; }

        public string FolderName { get; set; }

        public string AppDir { get; set; }

        public SetupInfo Setup { get; set; }

        /// <summary>Quiver's current download (<see cref="QuiverSetupUpdates.Fingerprint"/>).</summary>
        public string Current { get; set; }

        /// <summary>The download the installed game was set up from; null while not known.</summary>
        public string Done { get; set; }

        public SetupUpdateCheck Check { get; set; }

        /// <summary>The program to run again (installer or launcher); null when not known: the folder is opened.</summary>
        public LaunchTarget Program { get; set; }

        /// <summary>The installed game's play program; null when not known.</summary>
        public string PlayPath { get; set; }

        /// <summary>Files chosen for its setup commands (placeholder name → path), as remembered; may no longer exist.</summary>
        public System.Collections.Generic.Dictionary<string, string> Files { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>The update command, ready to run or with the reason it can't; null when the app has none.</summary>
        public PreparedCommand Update { get; set; }

        public bool IsPending => Check == SetupUpdateCheck.Updated;

        public string DownloadedVersion => QuiverSetupUpdates.Describe(Current);

        public string InstalledVersion => QuiverSetupUpdates.Describe(Done);
    }
}
