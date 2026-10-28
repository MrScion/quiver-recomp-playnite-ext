using System;
using System.Diagnostics;
using System.IO;
using Playnite.SDK;

namespace QuiverLibrary.Playnite
{
    /// <summary>Lets Playnite open Quiver Launcher itself (library client button and "Open Quiver Launcher").</summary>
    public class QuiverClient : LibraryClient
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly QuiverLibraryPlugin _plugin;

        public QuiverClient(QuiverLibraryPlugin plugin)
        {
            _plugin = plugin;
        }

        public override bool IsInstalled => _plugin.FindInstallation() != null;

        public override void Open()
        {
            var install = _plugin.FindInstallation();
            if (install == null)
            {
                _plugin.Notify("open", QuiverLibraryPlugin.Loc("LOCQuiverNotFound"), NotificationType.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(install.LauncherExe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(install.LauncherExe),
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, "Cannot start Quiver Launcher.");
                _plugin.Notify("open", e.Message, NotificationType.Error);
            }
        }
    }
}
