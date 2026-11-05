using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Playnite.SDK;
using QuiverLibrary.Core.Executables;

namespace QuiverLibrary.Playnite.Setup
{
    public enum SetupJobState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Canceled,
    }

    public class SetupJob
    {
        public SetupAppRow Row { get; set; }

        public PreparedCommand Command { get; set; }

        public SetupJobState State { get; set; }

        /// <summary>The last line the program wrote, to show while it runs.</summary>
        public string LastLine { get; set; }

        public int? ExitCode { get; set; }

        public string LogPath { get; set; }

        public DateTime? Started { get; set; }

        public DateTime? Ended { get; set; }

        public bool IsActive => State == SetupJobState.Queued || State == SetupJobState.Running;

        internal Process Process { get; set; }

        internal bool CancelRequested { get; set; }
    }

    /// <summary>
    /// Runs the setup commands of <c>executables.json</c> one at a time, in the background: each one's output goes to its
    /// own log (<c>setup-logs\&lt;app folder&gt;.log</c> in the extension's data folder) and to <c>extensions.log</c>.
    /// No time limit: some build the game for half an hour.
    /// </summary>
    public class SetupRunner
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly object sync = new object();
        private readonly List<SetupJob> jobs = new List<SetupJob>();
        private readonly string logDir;
        private Thread worker;
        private DateTime lastProgressEvent = DateTime.MinValue;

        public SetupRunner(string logDir)
        {
            this.logDir = logDir;
        }

        /// <summary>Raised (on a background thread) when a job is queued, starts, writes a line or ends.</summary>
        public event Action Changed;

        /// <summary>Raised (on a background thread) when a job ends: succeeded, failed or canceled.</summary>
        public event Action<SetupJob> Finished;

        /// <summary>Raised (on a background thread) when a job starts.</summary>
        public event Action<SetupJob> Started;

        /// <summary>The latest job of each app.</summary>
        public SetupJob Find(string folderName)
        {
            lock (sync)
            {
                return jobs.LastOrDefault(j => string.Equals(j.Row.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Jobs still to finish, and how many of the current batch have finished (for the sidebar's progress).</summary>
        public void Progress(out int active, out int batch)
        {
            lock (sync)
            {
                active = jobs.Count(j => j.IsActive);
                // The batch: the jobs since the queue was last empty.
                var firstActive = jobs.FindIndex(j => j.IsActive);
                batch = firstActive < 0 ? 0 : jobs.Count - firstActive;
            }
        }

        /// <summary>Queues the command; false when the app already has one queued or running.</summary>
        public bool Enqueue(SetupAppRow row, PreparedCommand command)
        {
            lock (sync)
            {
                if (jobs.Any(j => j.IsActive && string.Equals(j.Row.FolderName, row.FolderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                jobs.RemoveAll(j => !j.IsActive && string.Equals(j.Row.FolderName, row.FolderName, StringComparison.OrdinalIgnoreCase));
                jobs.Add(new SetupJob
                {
                    Row = row,
                    Command = command,
                    State = SetupJobState.Queued,
                    LogPath = Path.Combine(logDir, SafeName(row.FolderName) + ".log"),
                });
                if (worker == null || !worker.IsAlive)
                {
                    worker = new Thread(Work) { IsBackground = true, Name = "Quiver setup runner" };
                    worker.Start();
                }
            }

            Raise();
            return true;
        }

        /// <summary>Removes a queued job, or ends the running one with all the programs it started.</summary>
        public void Cancel(string folderName)
        {
            Process process = null;
            lock (sync)
            {
                var job = jobs.LastOrDefault(j => j.IsActive && string.Equals(j.Row.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
                if (job == null)
                {
                    return;
                }

                job.CancelRequested = true;
                if (job.State == SetupJobState.Queued)
                {
                    job.State = SetupJobState.Canceled;
                    job.Ended = DateTime.Now;
                }
                else
                {
                    process = job.Process;
                }
            }

            if (process != null)
            {
                KillTree(process);
            }

            Raise();
        }

        private void Work()
        {
            while (true)
            {
                SetupJob job;
                lock (sync)
                {
                    job = jobs.FirstOrDefault(j => j.State == SetupJobState.Queued);
                    if (job == null)
                    {
                        worker = null;
                        break;
                    }

                    job.State = SetupJobState.Running;
                    job.Started = DateTime.Now;
                }

                Raise();
                Invoke(Started, job);
                Run(job);
                Raise();
                Invoke(Finished, job);
            }

            Raise();
        }

        private void Run(SetupJob job)
        {
            var command = job.Command;
            StreamWriter log = null;
            try
            {
                Directory.CreateDirectory(logDir);
                log = new StreamWriter(job.LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                log.WriteLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} {job.Row.Name}");
                log.WriteLine($"# {command.Display}");
                log.WriteLine($"# in {command.WorkingDirectory}");
                Logger.Info($"Setup of {job.Row.Name}: {command.Display} (in {command.WorkingDirectory}); log {job.LogPath}.");

                var info = new ProcessStartInfo(command.File, command.Arguments ?? string.Empty)
                {
                    WorkingDirectory = command.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // Python writes its output as it goes, and in UTF-8, only when asked.
                info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (var outputEnded = new CountdownEvent(2))
                using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
                {
                    var writer = log;
                    DataReceivedEventHandler onLine = (s, e) =>
                    {
                        if (e.Data == null)
                        {
                            try
                            {
                                outputEnded.Signal();
                            }
                            catch (Exception)
                            {
                                // Already disposed: a program it started closed the output after the wait gave up.
                            }

                            return;
                        }

                        try
                        {
                            lock (writer)
                            {
                                writer.WriteLine(e.Data);
                            }
                        }
                        catch (Exception)
                        {
                            // The log is closed: output after the job ended.
                        }

                        if (e.Data.Trim().Length > 0)
                        {
                            job.LastLine = e.Data.Trim().Length > 200 ? e.Data.Trim().Substring(0, 200) : e.Data.Trim();
                            RaiseThrottled();
                        }
                    };
                    process.OutputDataReceived += onLine;
                    process.ErrorDataReceived += onLine;
                    lock (sync)
                    {
                        if (job.CancelRequested)
                        {
                            job.State = SetupJobState.Canceled;
                            job.Ended = DateTime.Now;
                            return;
                        }

                        process.Start();
                        job.Process = process;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    while (!process.WaitForExit(1000))
                    {
                    }

                    // Not the parameterless WaitForExit: it also waits for the redirected output to close, which never
                    // happens while a program it started (and that inherited it) still runs.
                    outputEnded.Wait(TimeSpan.FromSeconds(10));
                    job.ExitCode = process.ExitCode;
                }

                lock (sync)
                {
                    job.Process = null;
                    job.Ended = DateTime.Now;
                    job.State = job.CancelRequested ? SetupJobState.Canceled
                        : job.ExitCode == 0 ? SetupJobState.Succeeded
                        : SetupJobState.Failed;
                }

                log.WriteLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} exit code {job.ExitCode} ({job.State})");
                Logger.Info($"Setup of {job.Row.Name} ended: exit code {job.ExitCode} ({job.State}).");
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Setup of {job.Row.Name} failed: {command.Display}.");
                lock (sync)
                {
                    job.Process = null;
                    job.Ended = DateTime.Now;
                    job.State = job.CancelRequested ? SetupJobState.Canceled : SetupJobState.Failed;
                    job.LastLine = e.Message;
                }

                try
                {
                    log?.WriteLine($"# failed: {e.Message}");
                }
                catch (IOException)
                {
                }
            }
            finally
            {
                log?.Dispose();
            }
        }

        /// <summary>Ends the process and everything it started (installers start compilers, Python, ...).</summary>
        private static void KillTree(Process process)
        {
            try
            {
                using (var kill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {process.Id} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    kill?.WaitForExit(10000);
                }
            }
            catch (Exception e)
            {
                Logger.Warn(e, "taskkill failed; ending only the setup program.");
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                }
            }
        }

        private static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "app").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private void RaiseThrottled()
        {
            var now = DateTime.UtcNow;
            if ((now - lastProgressEvent).TotalMilliseconds < 1000)
            {
                return;
            }

            lastProgressEvent = now;
            Raise();
        }

        private void Raise()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Setup runner listener failed.");
            }
        }

        private static void Invoke(Action<SetupJob> handler, SetupJob job)
        {
            try
            {
                handler?.Invoke(job);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Setup runner listener failed.");
            }
        }
    }
}
