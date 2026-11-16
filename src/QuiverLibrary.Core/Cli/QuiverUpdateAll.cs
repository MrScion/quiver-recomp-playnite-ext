using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace QuiverLibrary.Core.Cli
{
    public class QuiverUpdateAllResult
    {
        /// <summary>The apps updated, in order (the names Quiver wrote).</summary>
        public List<string> Updated { get; } = new List<string>();

        /// <summary>How many times Quiver's <c>--update</c> was started.</summary>
        public int Rounds { get; set; }

        /// <summary>The last run: the one Quiver ended by itself, or the one cancelled, timed out or cut by the limit.</summary>
        public QuiverCliResult Last { get; set; }

        public bool Cancelled { get; set; }

        public bool TimedOut { get; set; }

        /// <summary>Stopped because the same app came up again or there were too many runs: Quiver would never finish.</summary>
        public bool Stuck { get; set; }

        /// <summary>The last run ended by itself with no error: every app with an update was updated.</summary>
        public bool Success => !Cancelled && !TimedOut && !Stuck && Last != null && !Last.EndedByWatcher && Last.ExitCode == 0;
    }

    /// <summary>
    /// Quiver's <c>--update</c> stops for good after the first app it updates (see <see cref="QuiverUpdateWatch"/>): it is
    /// run again and again, each run stopped once its app is updated, until Quiver ends by itself (nothing left to update,
    /// or only apps that failed: a failure doesn't stop Quiver's loop).
    /// </summary>
    public static class QuiverUpdateAll
    {
        /// <param name="runRound">Runs <c>--update</c> once with the watch (<see cref="QuiverCli.Run"/> with its check and output).</param>
        /// <param name="onUpdating">Called with each app Quiver starts updating.</param>
        /// <param name="maxRounds">Default: one per installed app, plus one.</param>
        public static QuiverUpdateAllResult Run(QuiverLibraryData data, Func<QuiverUpdateWatch, QuiverCliResult> runRound,
            CancellationToken cancel, Action<string> onUpdating = null, int? maxRounds = null)
        {
            var result = new QuiverUpdateAllResult();
            var appsFolder = data.AppsFolder;
            var limit = maxRounds ?? data.GetStates().Count(s => s.IsInstalled) + 1;
            for (var round = 1; round <= Math.Max(1, limit); round++)
            {
                if (cancel.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    return result;
                }

                var watch = new QuiverUpdateWatch(data, appsFolder, data.GetStates());
                if (onUpdating != null)
                {
                    watch.UpdatingStarted += onUpdating;
                }

                var run = runRound(watch);
                result.Rounds = round;
                result.Last = run;
                if (!run.EndedByWatcher)
                {
                    result.Cancelled = run.Cancelled;
                    result.TimedOut = run.TimedOut;
                    return result;
                }

                var name = watch.Current?.Name ?? watch.CurrentName;
                if (result.Updated.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    // Updated again right after: Quiver still thinks it has an update; it would never end.
                    result.Stuck = true;
                    return result;
                }

                result.Updated.Add(name);
            }

            result.Stuck = true;
            return result;
        }
    }
}
