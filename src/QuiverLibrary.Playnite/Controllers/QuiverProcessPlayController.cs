using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>
    /// Starts a play program that needs environment variables (Playnite's own play actions can't set
    /// them) and reports the session when that process exits.
    /// </summary>
    public class QuiverProcessPlayController : PlayController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly LaunchTarget _target;
        private Process _process;

        public QuiverProcessPlayController(Game game, LaunchTarget target) : base(game)
        {
            _target = target;
            Name = game.Name;
        }

        public override void Play(PlayActionArgs args)
        {
            var info = new ProcessStartInfo(_target.Path, _target.Arguments ?? string.Empty)
            {
                WorkingDirectory = Path.GetDirectoryName(_target.Path),
                UseShellExecute = false,
            };
            foreach (var variable in _target.Environment)
            {
                info.EnvironmentVariables[variable.Key] = variable.Value;
            }

            Logger.Info($"Starting {_target.Path} {_target.Arguments} with {string.Join(", ", _target.Environment.Keys)}.");
            _process = Process.Start(info);
            var context = SynchronizationContext.Current;
            var stopwatch = Stopwatch.StartNew();
            InvokeOnStarted(new GameStartedEventArgs { StartedProcessId = _process.Id });

            var process = _process;
            Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Lost track of {_target.Path}.");
                }

                var stopped = new GameStoppedEventArgs { SessionLength = Convert.ToUInt64(stopwatch.Elapsed.TotalSeconds) };
                if (context != null)
                {
                    context.Post(_ => InvokeOnStopped(stopped), null);
                }
                else
                {
                    InvokeOnStopped(stopped);
                }
            });
        }

        public override void Dispose()
        {
            _process?.Dispose();
            _process = null;
            base.Dispose();
        }
    }
}
