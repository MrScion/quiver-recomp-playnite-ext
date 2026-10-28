using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace QuiverLibrary.Core.Cli
{
    public class QuiverCliResult
    {
        // Messages Quiver's command line prints when it can only do the job from its own window
        // (Services/CLIHandler.cs, UpdateOrDownloadGame).
        private static readonly string[] NeedsQuiverWindowMessages =
        {
            "No eligible download was found",
            "Download selection is required",
            "MSI installation requires the Windows desktop interface",
        };

        public int ExitCode { get; set; }
        public bool Cancelled { get; set; }
        public bool TimedOut { get; set; }

        /// <summary>
        /// Quiver was stopped by <see cref="QuiverCli.Run"/> because the watched work was done:
        /// Quiver's <c>--download</c> finishes installing but its process never exits.
        /// </summary>
        public bool EndedByWatcher { get; set; }

        public string Output { get; set; }

        public bool Success => EndedByWatcher || (!Cancelled && !TimedOut && ExitCode == 0);

        /// <summary>
        /// Quiver found no file to download. Happens when its catalog cache says which release to
        /// install but it never cached that release's files: see <see cref="QuiverLibraryData.CatalogReleaseStatusCachePath"/>.
        /// </summary>
        public bool NoEligibleDownload =>
            !Success && (Output ?? "").IndexOf(NeedsQuiverWindowMessages[0], StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Quiver refused because the job can only be done from Quiver Launcher's window.</summary>
        public bool NeedsQuiverWindow =>
            !Success && NeedsQuiverWindowMessages.Any(m => (Output ?? "").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Quiver's last <c>ERROR: ...</c> line, or else its last meaningful output lines.</summary>
        public string Summary(int maxLines = 6)
        {
            var lines = (Output ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.All(c => c == '-' || c == '=' || c == '_' || c == '|' || c == '/' || c == '\\' || c == ' ' || c == '─'))
                .ToList();
            var error = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));
            if (error != null)
            {
                return error;
            }

            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Count - maxLines)));
        }
    }

    /// <summary>
    /// Runs Quiver Launcher's command line (<c>Services/CLIHandler.cs</c>: <c>--download</c>,
    /// <c>--uninstall</c>, <c>--update</c>) hidden, capturing its output. Quiver returns 0 on
    /// success and 1 on error, and never asks for confirmation.
    /// </summary>
    public static class QuiverCli
    {
        private static readonly Regex AnsiEscape = new Regex(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        /// <param name="finishedWhen">
        /// Optional check (polled about once a second, exceptions count as false) for work that is
        /// done even though Quiver hasn't exited: once it has held for <paramref name="stableFor"/>,
        /// Quiver is stopped and the result is <see cref="QuiverCliResult.EndedByWatcher"/>.
        /// </param>
        /// <param name="timeout">Quiver is stopped and the result is <see cref="QuiverCliResult.TimedOut"/> after this long.</param>
        public static QuiverCliResult Run(
            string quiverExe,
            IEnumerable<string> args,
            CancellationToken cancel,
            Func<bool> finishedWhen = null,
            TimeSpan? stableFor = null,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(quiverExe) || !File.Exists(quiverExe))
            {
                throw new FileNotFoundException("QuiverLauncher.exe was not found.", quiverExe);
            }

            var output = new StringBuilder();
            var info = new ProcessStartInfo(quiverExe, string.Join(" ", args.Select(QuoteArgument)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(quiverExe),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(Clean(e.Data)); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(Clean(e.Data)); };
                process.Start();
                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var started = Stopwatch.StartNew();
                var lastCheck = TimeSpan.FromSeconds(-1);
                TimeSpan? finishedSince = null;
                var stable = stableFor ?? TimeSpan.FromSeconds(5);
                bool cancelled = false, timedOut = false, endedByWatcher = false, exited;

                while (!(exited = process.WaitForExit(250)))
                {
                    if (cancel.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    if (timeout.HasValue && started.Elapsed >= timeout.Value)
                    {
                        timedOut = true;
                        break;
                    }

                    if (finishedWhen != null && started.Elapsed - lastCheck >= TimeSpan.FromSeconds(1))
                    {
                        lastCheck = started.Elapsed;
                        if (IsTrue(finishedWhen))
                        {
                            finishedSince = finishedSince ?? started.Elapsed;
                            if (started.Elapsed - finishedSince.Value >= stable)
                            {
                                endedByWatcher = true;
                                break;
                            }
                        }
                        else
                        {
                            finishedSince = null;
                        }
                    }
                }

                if (exited)
                {
                    // Flushes the asynchronous output readers.
                    process.WaitForExit();
                }
                else
                {
                    try { process.Kill(); } catch { }
                    // Bounded: an unbounded wait would also wait for the output pipes to close.
                    process.WaitForExit(5000);
                }

                lock (output)
                {
                    return new QuiverCliResult
                    {
                        ExitCode = exited ? process.ExitCode : -1,
                        Cancelled = cancelled,
                        TimedOut = timedOut,
                        EndedByWatcher = endedByWatcher,
                        Output = output.ToString()
                    };
                }
            }
        }

        private static bool IsTrue(Func<bool> check)
        {
            try
            {
                return check();
            }
            catch
            {
                return false;
            }
        }

        private static string Clean(string line) => AnsiEscape.Replace(line, string.Empty);

        /// <summary>Quoting rules of CommandLineToArgvW, which .NET uses to split arguments.</summary>
        public static string QuoteArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return arg;
            }

            var sb = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                sb.Append(c);
            }

            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
