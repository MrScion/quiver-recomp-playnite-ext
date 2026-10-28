using System;
using System.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using QuiverLibrary.Core.Cli;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>
    /// Runs <c>QuiverLauncher.exe --uninstall &lt;folderName&gt;</c>. Synchronous on purpose:
    /// <see cref="UninstallController"/> has no "cancelled" callback, so a failure is
    /// reported by throwing — Playnite shows the message and keeps the game installed.
    /// </summary>
    public class QuiverUninstallController : UninstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly QuiverLibraryPlugin _plugin;

        public QuiverUninstallController(Game game, QuiverLibraryPlugin plugin) : base(game)
        {
            _plugin = plugin;
            Name = QuiverLibraryPlugin.Loc("LOCQuiverUninstallActionName");
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            var cliExe = _plugin.RequireInstallation().CliExe;
            QuiverCliResult result = null;
            Exception error = null;

            _plugin.PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                try
                {
                    result = QuiverCli.Run(cliExe, new[] { "--uninstall", Game.GameId }, CancellationToken.None);
                }
                catch (Exception e)
                {
                    error = e;
                }
            }, new GlobalProgressOptions(string.Format(QuiverLibraryPlugin.Loc("LOCQuiverProgressUninstalling"), Game.Name), false));

            if (error != null)
            {
                Logger.Error(error, $"Quiver uninstall of {Game.GameId} failed.");
                throw new Exception(string.Format(QuiverLibraryPlugin.Loc("LOCQuiverUninstallFailed"), Game.Name, error.Message), error);
            }

            var state = _plugin.GetAppState(Game.GameId);
            if (!result.Success || (state != null && state.IsInstalled))
            {
                Logger.Warn($"Quiver --uninstall {Game.GameId} exited with {result.ExitCode}:{Environment.NewLine}{result.Output}");
                var reason = result.Success ? QuiverLibraryPlugin.Loc("LOCQuiverUninstallFilesRemain") : result.Summary();
                throw new Exception(string.Format(QuiverLibraryPlugin.Loc("LOCQuiverUninstallFailed"), Game.Name, reason));
            }

            InvokeOnUninstalled();
        }
    }
}
