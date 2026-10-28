using System.Collections.Generic;
using System.Runtime.Serialization;

namespace QuiverLibrary.Core
{
    /// <summary>One entry of Quiver Launcher's <c>apps.json</c> (camelCase fields). Only the fields this extension uses.</summary>
    [DataContract]
    public class QuiverApp
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "folderName")]
        public string FolderName { get; set; }

        [DataMember(Name = "installPath")]
        public string InstallPath { get; set; }

        [DataMember(Name = "appIconUrl")]
        public string AppIconUrl { get; set; }

        [DataMember(Name = "repository")]
        public string Repository { get; set; }

        [DataMember(Name = "project")]
        public string Project { get; set; }

        [DataMember(Name = "customDisplayName")]
        public string CustomDisplayName { get; set; }

        [DataMember(Name = "tags")]
        public List<string> Tags { get; set; }

        [DataMember(Name = "mods")]
        public QuiverMods Mods { get; set; }
    }

    [DataContract]
    public class QuiverMods
    {
        [DataMember(Name = "sources")]
        public List<QuiverModSource> Sources { get; set; }
    }

    [DataContract]
    public class QuiverModSource
    {
        [DataMember(Name = "provider")]
        public string Provider { get; set; }

        [DataMember(Name = "sourceUrl")]
        public string SourceUrl { get; set; }
    }

    [DataContract]
    public class QuiverAppList
    {
        [DataMember(Name = "apps")]
        public List<QuiverApp> Apps { get; set; }
    }

    /// <summary>Quiver's own <c>settings.json</c> (PascalCase fields). Only the fields this extension uses.</summary>
    [DataContract]
    public class QuiverSettingsFile
    {
        [DataMember(Name = "AppsPath")]
        public string AppsPath { get; set; }
    }

    /// <summary>
    /// <c>windows-installer.json</c>, written by Quiver for apps installed through an
    /// MSI/EXE installer (<c>WindowsInstallerReceipt</c> record in Quiver's source).
    /// </summary>
    [DataContract]
    public class WindowsInstallerReceipt
    {
        [DataMember(Name = "Phase")]
        public string Phase { get; set; }

        [DataMember(Name = "ReleaseTag")]
        public string ReleaseTag { get; set; }

        [DataMember(Name = "ExecutablePath")]
        public string ExecutablePath { get; set; }
    }

    /// <summary>Install state of one Quiver app as found on disk.</summary>
    public class QuiverAppState
    {
        public QuiverApp App { get; set; }
        public string InstallDirectory { get; set; }
        public bool IsInstalled { get; set; }
        public string Version { get; set; }
    }
}
