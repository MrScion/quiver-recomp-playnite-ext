using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace QuiverLibrary.Core.Executables
{
    /// <summary>
    /// <c>executables.json</c> (shipped with the extension) and <c>executables.local.json</c>
    /// (in the extension's data folder): which program of an app starts the game and which
    /// other programs it brings (setup tools, alternative renderers, ...).
    /// </summary>
    [DataContract]
    public class ExecutableDatabaseFile
    {
        [DataMember(Name = "apps", Order = 1)]
        public List<ExecutableEntry> Apps { get; set; }
    }

    [DataContract]
    public class ExecutableEntry
    {
        /// <summary>Only to make the file readable; never used to match an app.</summary>
        [DataMember(Name = "name", Order = 1, EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>Quiver's <c>repository</c> ("owner/repo"); matches every app of that repository.</summary>
        [DataMember(Name = "repository", Order = 2, EmitDefaultValue = false)]
        public string Repository { get; set; }

        /// <summary>Quiver's <c>folderName</c>; when set, the entry matches that app only.</summary>
        [DataMember(Name = "folderName", Order = 3, EmitDefaultValue = false)]
        public string FolderName { get; set; }

        /// <summary>The program that starts the game directly (Playnite's play action).</summary>
        [DataMember(Name = "play", Order = 4, EmitDefaultValue = false)]
        public ExecutableTarget Play { get; set; }

        /// <summary>Offered as Playnite's other (non-play) actions of the game.</summary>
        [DataMember(Name = "others", Order = 5, EmitDefaultValue = false)]
        public List<ExecutableTarget> Others { get; set; }

        /// <summary>
        /// Local file only: false for the entries written automatically, which are refreshed on
        /// every library update and not used to launch anything; true once checked by hand.
        /// </summary>
        [DataMember(Name = "reviewed", Order = 6)]
        public bool Reviewed { get; set; }

        /// <summary>Where the information comes from (README, release notes, ...).</summary>
        [DataMember(Name = "source", Order = 7, EmitDefaultValue = false)]
        public string Source { get; set; }

        /// <summary>Where the app keeps its saves and settings, and how it is made portable.</summary>
        [DataMember(Name = "userData", Order = 8, EmitDefaultValue = false)]
        public UserDataInfo UserData { get; set; }

        /// <summary>Whether the app has achievements or trophies, and where it keeps them.</summary>
        [DataMember(Name = "achievements", Order = 9, EmitDefaultValue = false)]
        public AchievementsInfo Achievements { get; set; }

        /// <summary>How the app gets its game: what an update from Quiver leaves to be done by hand.</summary>
        [DataMember(Name = "setup", Order = 10, EmitDefaultValue = false)]
        public SetupInfo Setup { get; set; }

        /// <summary>False for an entry that only describes <see cref="UserData"/>: its programs are chosen automatically.</summary>
        public bool HasLaunchInfo => Play != null || (Others != null && Others.Count > 0);
    }

    [DataContract]
    public class UserDataInfo
    {
        /// <summary>The app already keeps its data in its own folder.</summary>
        public const string Builtin = "builtin";

        /// <summary>The app can't be made portable.</summary>
        public const string None = "none";

        /// <summary>The usual marker file, created next to the play program.</summary>
        public const string DefaultMarker = "portable.txt";

        /// <summary>
        /// <see cref="Builtin"/>, <see cref="None"/>, or the name of the marker file that makes the
        /// app portable when it sits next to its play program.
        /// </summary>
        [DataMember(Name = "portable", Order = 1)]
        public string Portable { get; set; }

        /// <summary>Data folder when not portable; may contain environment variables (<c>%LOCALAPPDATA%</c>).</summary>
        [DataMember(Name = "appData", Order = 2, EmitDefaultValue = false)]
        public string AppData { get; set; }

        /// <summary>Data folder when portable, relative to the app folder ("" = the app folder itself).</summary>
        [DataMember(Name = "local", Order = 3, EmitDefaultValue = false)]
        public string Local { get; set; }

        /// <summary>
        /// Saves inside both data folders: a folder, a file or a pattern (<c>*.sav</c>), several separated
        /// by <c>;</c>; empty when the saves are the data folder itself.
        /// </summary>
        [DataMember(Name = "saves", Order = 4, EmitDefaultValue = false)]
        public string Saves { get; set; }

        /// <summary><see cref="Saves"/> split; a single empty item for none.</summary>
        public List<string> SaveItems
        {
            get
            {
                var items = (Saves ?? string.Empty).Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                return items.Count > 0 ? items : new List<string> { string.Empty };
            }
        }

        public bool IsBuiltin => string.Equals(Portable, Builtin, System.StringComparison.OrdinalIgnoreCase);

        public bool IsUnsupported => string.Equals(Portable, None, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The marker file name, or null for <see cref="Builtin"/> and <see cref="None"/>.</summary>
        public string Marker => IsBuiltin || IsUnsupported ? null : (string.IsNullOrWhiteSpace(Portable) ? DefaultMarker : Portable);
    }

    [DataContract]
    public class SetupInfo
    {
        /// <summary>
        /// Quiver's download is an installer that builds or installs the game into another folder
        /// (one the player chooses, usually <c>game\</c> inside the app). An update from Quiver only
        /// replaces the installer: the installed game stays as it was until the installer runs again.
        /// </summary>
        public const string Installer = "installer";

        /// <summary>
        /// Quiver's download is a launcher that builds the game in the app folder from the player's
        /// disc. After an update from Quiver the launcher may have to build the game again.
        /// </summary>
        public const string Builder = "builder";

        /// <summary><see cref="Installer"/> or <see cref="Builder"/>; apps without it are ready to play as downloaded.</summary>
        [DataMember(Name = "kind", Order = 1)]
        public string Kind { get; set; }

        /// <summary>
        /// Unattended install from the command line, when the installer has one. <see cref="ExecutableTarget.File"/>
        /// is relative to the app folder; besides the usual variables the arguments name what the player has to
        /// supply: <c>{Image}</c> (disc image), <c>{InstallDir}</c> (where the game goes) and others told in <see cref="Notes"/>.
        /// </summary>
        [DataMember(Name = "install", Order = 2, EmitDefaultValue = false)]
        public ExecutableTarget Install { get; set; }

        /// <summary>
        /// Updating an installed game after Quiver downloads a new installer, from the command line.
        /// <c>{GameDir}</c> is the folder of the installed game's play program. Null when the installer has no
        /// such command (or it is not known): then it runs again with its window.
        /// </summary>
        [DataMember(Name = "update", Order = 3, EmitDefaultValue = false)]
        public ExecutableTarget Update { get; set; }

        /// <summary>What else to know to install or update it (what it asks for, known problems).</summary>
        [DataMember(Name = "notes", Order = 4, EmitDefaultValue = false)]
        public string Notes { get; set; }

        /// <summary>
        /// A file the installer leaves in the installed game's folder, as a path relative to that folder
        /// (<c>xcx-install.json</c>, <c>data\install.json</c>): looked for in the app folder and its subfolders to
        /// find <c>{GameDir}</c>. Without it, <c>{GameDir}</c> is the play program's folder.
        /// </summary>
        [DataMember(Name = "installMarker", Order = 5, EmitDefaultValue = false)]
        public string InstallMarker { get; set; }

        /// <summary>
        /// Where the installer puts the game unless the player chooses another folder (environment variables allowed):
        /// a game left there is outside the app folder, so uninstalling the app in Quiver doesn't remove it.
        /// </summary>
        [DataMember(Name = "defaultInstallDir", Order = 7, EmitDefaultValue = false)]
        public string DefaultInstallDir { get; set; }

        /// <summary>Why <see cref="Update"/> can't be run as it is (it stays documented): the player updates by hand.</summary>
        [DataMember(Name = "updateProblem", Order = 6, EmitDefaultValue = false)]
        public string UpdateProblem { get; set; }

        public bool IsInstaller => string.Equals(Kind, Installer, System.StringComparison.OrdinalIgnoreCase);

        public bool IsBuilder => string.Equals(Kind, Builder, System.StringComparison.OrdinalIgnoreCase);
    }

    [DataContract]
    public class AchievementsInfo
    {
        /// <summary>The ReXGlue runtime's own achievements (Xbox 360 recompilations).</summary>
        public const string ReXGlue = "rexglue";

        /// <summary>Unlocked on the player's RetroAchievements account (rcheevos built in).</summary>
        public const string RetroAchievements = "retroachievements";

        /// <summary>PS4 trophies (<c>sceNpTrophy</c>).</summary>
        public const string Ps4Trophy = "ps4trophy";

        /// <summary>The app's own achievement system.</summary>
        public const string Custom = "custom";

        /// <summary>Checked: the app has no achievements.</summary>
        public const string None = "none";

        /// <summary><see cref="ReXGlue"/>, <see cref="RetroAchievements"/>, <see cref="Ps4Trophy"/>, <see cref="Custom"/> or <see cref="None"/>.</summary>
        [DataMember(Name = "kind", Order = 1)]
        public string Kind { get; set; }

        /// <summary>
        /// File with the unlocked achievements, relative to the app folder; may contain environment
        /// variables (<c>%APPDATA%</c>). Several places are separated by <c>;</c>, the one in use
        /// first (portable before AppData). Empty while unknown.
        /// </summary>
        [DataMember(Name = "file", Order = 2, EmitDefaultValue = false)]
        public string File { get; set; }

        /// <summary>
        /// The game's id on retroachievements.org (<c>/game/&lt;id&gt;</c>) for <see cref="RetroAchievements"/>;
        /// 0 while unknown.
        /// </summary>
        [DataMember(Name = "retroAchievementsId", Order = 3, EmitDefaultValue = false)]
        public int RetroAchievementsId { get; set; }
    }

    [DataContract]
    public class ExecutableTarget
    {
        /// <summary>Action name; ignored for <see cref="ExecutableEntry.Play"/>.</summary>
        [DataMember(Name = "name", Order = 1, EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Path relative to the app folder. The file name and the folder names may contain
        /// <c>*</c>/<c>?</c>; with several matches the highest version wins ("App-0.1.60.exe" over
        /// "App-0.1.57.exe", <c>versions\0.3.0</c> over <c>versions\0.2.0</c>).
        /// </summary>
        [DataMember(Name = "file", Order = 2)]
        public string File { get; set; }

        /// <summary>
        /// May contain environment variables (<c>%LOCALAPPDATA%</c>) and <c>{AppDir}</c>/<c>{ExeDir}</c>
        /// (see <see cref="ExecutablePlanner.ExpandVariables"/>).
        /// </summary>
        [DataMember(Name = "arguments", Order = 3, EmitDefaultValue = false)]
        public string Arguments { get; set; }

        /// <summary>
        /// <see cref="File"/> is a file name looked for in the app folder and all its subfolders,
        /// the shallowest match winning: for apps whose installer lets the player choose where the game
        /// goes. A play program set this way never falls back to the automatic choice (that would
        /// be the installer).
        /// </summary>
        [DataMember(Name = "searchSubfolders", Order = 4, EmitDefaultValue = false)]
        public bool SearchSubfolders { get; set; }

        /// <summary>
        /// Environment variables the program needs (what its own launcher sets), values expanded like
        /// <see cref="Arguments"/>. Play program only: Playnite's other actions can't set them.
        /// </summary>
        [DataMember(Name = "environment", Order = 5, EmitDefaultValue = false)]
        public List<EnvironmentVariable> Environment { get; set; }
    }

    [DataContract]
    public class EnvironmentVariable
    {
        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Name = "value", Order = 2)]
        public string Value { get; set; }
    }

    /// <summary>A resolved program of an installed app.</summary>
    public class LaunchTarget
    {
        public string Name { get; set; }

        /// <summary>Absolute path.</summary>
        public string Path { get; set; }

        public string Arguments { get; set; }

        /// <summary>Expanded environment variables to set; empty when the program needs none.</summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }

    public class ExecutablePlan
    {
        /// <summary>Null when the game's program can't be decided.</summary>
        public LaunchTarget Play { get; set; }

        public List<LaunchTarget> Others { get; set; } = new List<LaunchTarget>();
    }
}
