using System;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using QuiverLibrary.Core.Cli;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>Runs <c>QuiverLauncher.exe --download &lt;folderName&gt;</c> in the background; Playnite only tracks the result.</summary>
    public class QuiverInstallController : InstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly QuiverLibraryPlugin _plugin;

        public QuiverInstallController(Game game, QuiverLibraryPlugin plugin) : base(game)
        {
            _plugin = plugin;
            Name = QuiverLibraryPlugin.Loc("LOCQuiverInstallActionName");
        }

        public override void Install(InstallActionArgs args)
        {
            // Throws straight to Playnite (shown as an install error) when Quiver isn't configured.
            var install = _plugin.RequireInstallation();
            var gameId = Game.GameId;
            var gameName = Game.Name;

            Task.Run(() =>
            {
                QuiverCliResult result = null;
                string reason;
                try
                {
                    result = _plugin.RunDownload(install, gameId);
                    var state = _plugin.GetAppState(gameId);
                    if (result.Success && state != null && state.IsInstalled)
                    {
                        InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData
                        {
                            InstallDirectory = state.InstallDirectory,
                        }));
                        return;
                    }

                    Logger.Warn($"Quiver --download {gameId} exited with {result.ExitCode}:{Environment.NewLine}{result.Output}");
                    reason = result.Success ? QuiverLibraryPlugin.Loc("LOCQuiverInstallNoFiles") : result.Summary();
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Quiver install of {gameId} failed.");
                    reason = e.Message;
                }

                _plugin.NotifyInstallFailed(gameId, gameName, result, reason);
                InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
            });
        }
    }
}
