using System;
using System.Diagnostics;
using System.Threading;
using QuiverLibrary.Core.Cli;
using Xunit;

namespace QuiverLibrary.Core.Tests.Cli
{
    public class QuiverCliTests
    {
        [Theory]
        [InlineData("Zelda64Recomp", "Zelda64Recomp")]
        [InlineData("", "\"\"")]
        [InlineData("two words", "\"two words\"")]
        [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [InlineData(@"C:\a b\", "\"C:\\a b\\\\\"")]
        public void QuoteArgument_FollowsCommandLineToArgvRules(string input, string expected)
        {
            Assert.Equal(expected, QuiverCli.QuoteArgument(input));
        }

        [Fact]
        public void Summary_PrefersLastErrorLine()
        {
            var result = new QuiverCliResult
            {
                ExitCode = 1,
                Output = "=====\nQuiver Launcher\n-----\nDownloading...\nERROR: first\nERROR: Game not found: x\n"
            };

            Assert.False(result.Success);
            Assert.Equal("ERROR: Game not found: x", result.Summary());
        }

        [Fact]
        public void Summary_WithoutError_ReturnsLastLinesWithoutRules()
        {
            var result = new QuiverCliResult { Output = "a\n-----\nb\nc\n" };

            Assert.True(result.Success);
            Assert.Equal("b\r\nc", result.Summary(2));
        }

        [Fact]
        public void Cancelled_IsNeverSuccess()
        {
            Assert.False(new QuiverCliResult { ExitCode = 0, Cancelled = true }.Success);
        }

        [Theory]
        [InlineData("→ Detected platform: Windows\nNo eligible download was found.\n")]
        [InlineData("Download selection is required. Open Quiver to choose a file.")]
        [InlineData("ERROR: MSI installation requires the Windows desktop interface. Open Quiver and run the installer there.")]
        public void NeedsQuiverWindow_RecognizesQuiverRefusals(string output)
        {
            Assert.True(new QuiverCliResult { ExitCode = 1, Output = output }.NeedsQuiverWindow);
        }

        [Fact]
        public void NoEligibleDownload_OnlyForThatMessage()
        {
            Assert.True(new QuiverCliResult { ExitCode = 1, Output = "No eligible download was found." }.NoEligibleDownload);
            Assert.False(new QuiverCliResult { ExitCode = 1, Output = "Download selection is required." }.NoEligibleDownload);
            Assert.False(new QuiverCliResult { ExitCode = 0, Output = "No eligible download was found." }.NoEligibleDownload);
        }

        [Fact]
        public void NeedsQuiverWindow_FalseForOtherErrorsAndSuccess()
        {
            Assert.False(new QuiverCliResult { ExitCode = 1, Output = "ERROR: App not found: 'x'" }.NeedsQuiverWindow);
            Assert.False(new QuiverCliResult { ExitCode = 0, Output = "No eligible download was found." }.NeedsQuiverWindow);
        }

        private static string Cmd => System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // Stands in for Quiver's --download, which installs the app but never exits.
        private static readonly string[] HangingArgs = { "/c", "ping", "-n", "60", "127.0.0.1" };

        [Fact]
        public void Run_ReturnsExitCodeAndOutput()
        {
            var result = QuiverCli.Run(Cmd, new[] { "/c", "echo", "hello", "&", "exit", "3" }, CancellationToken.None);

            Assert.Equal(3, result.ExitCode);
            Assert.False(result.Success);
            Assert.Contains("hello", result.Output);
        }

        [Fact]
        public void Run_StopsHangingProcess_OnceWorkIsDoneAndStable()
        {
            var watch = Stopwatch.StartNew();

            var result = QuiverCli.Run(Cmd, HangingArgs, CancellationToken.None,
                finishedWhen: () => watch.Elapsed > TimeSpan.FromSeconds(1),
                stableFor: TimeSpan.FromSeconds(2),
                timeout: TimeSpan.FromSeconds(30));

            Assert.True(result.EndedByWatcher);
            Assert.True(result.Success);
            Assert.InRange(watch.Elapsed.TotalSeconds, 2, 15);
        }

        [Fact]
        public void Run_WatcherNeverTrue_TimesOut()
        {
            var watch = Stopwatch.StartNew();

            var result = QuiverCli.Run(Cmd, HangingArgs, CancellationToken.None,
                finishedWhen: () => throw new InvalidOperationException("never done"),
                timeout: TimeSpan.FromSeconds(3));

            Assert.True(result.TimedOut);
            Assert.False(result.EndedByWatcher);
            Assert.False(result.Success);
            Assert.InRange(watch.Elapsed.TotalSeconds, 2, 15);
        }
    }
}
