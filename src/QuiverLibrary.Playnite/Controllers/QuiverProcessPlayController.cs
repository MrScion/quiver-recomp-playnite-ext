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
    /// Starts a play program that needs environment variables, no console window, another program than
    /// the game's (<see cref="LaunchTarget.Program"/>) or a borderless full screen window (Playnite's own
    /// play actions can't do any of them) and reports the session when that process exits.
    /// </summary>
    public class QuiverProcessPlayController : PlayController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(60);

        private readonly LaunchTarget _target;
        private Process _process;

        public QuiverProcessPlayController(Game game, LaunchTarget target) : base(game)
        {
            _target = target;
            Name = game.Name;
        }

        /// <summary>Whether <paramref name="target"/> needs this controller instead of Playnite's own.</summary>
        public static bool IsNeeded(LaunchTarget target)
        {
            return target.Environment.Count > 0 || target.HideConsole || target.Program != null || target.Borderless;
        }

        public override void Play(PlayActionArgs args)
        {
            var info = new ProcessStartInfo(_target.StartPath, _target.Arguments ?? string.Empty)
            {
                // The game's folder (the launcher's), not the engine's: the launcher starts it from there.
                WorkingDirectory = Path.GetDirectoryName(_target.Path),
                UseShellExecute = false,
                CreateNoWindow = _target.HideConsole,
            };
            foreach (var variable in _target.Environment)
            {
                info.EnvironmentVariables[variable.Key] = variable.Value;
            }

            Logger.Info($"Starting {_target.StartPath} {_target.Arguments} with {string.Join(", ", _target.Environment.Keys)}.");
            _process = Process.Start(info);
            var context = SynchronizationContext.Current;
            var stopwatch = Stopwatch.StartNew();
            InvokeOnStarted(new GameStartedEventArgs { StartedProcessId = _process.Id });

            var process = _process;
            if (_target.Borderless)
            {
                Task.Run(() => MakeBorderless(process));
            }

            Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Lost track of {_target.StartPath}.");
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

        private void MakeBorderless(Process process)
        {
            try
            {
                var waited = Stopwatch.StartNew();
                var window = IntPtr.Zero;
                while (window == IntPtr.Zero && waited.Elapsed < WindowTimeout)
                {
                    Thread.Sleep(500);
                    if (process.HasExited)
                    {
                        return;
                    }

                    process.Refresh();
                    window = process.MainWindowHandle;
                }

                if (window == IntPtr.Zero)
                {
                    Logger.Warn($"{_target.StartPath} showed no window in {WindowTimeout.TotalSeconds} s; left as it is.");
                    return;
                }

                // Let the game finish creating its window first.
                Thread.Sleep(500);
                if (BorderlessWindow.Apply(window))
                {
                    Logger.Info($"{_target.StartPath}: window made borderless full screen.");
                }
                else
                {
                    Logger.Warn($"{_target.StartPath}: could not make the window borderless full screen.");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot make the window of {_target.StartPath} borderless.");
            }
        }

        public override void Dispose()
        {
            _process?.Dispose();
            _process = null;
            base.Dispose();
        }
    }
}
