using System.Collections.Generic;
using System.Threading;
using QuiverLibrary.Core.Cli;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverUpdateAllTests
    {
        private const string AppsJson = @"{ ""apps"": [
  { ""name"": ""Game A"", ""folderName"": ""A"" },
  { ""name"": ""Game B"", ""folderName"": ""B"" },
  { ""name"": ""Game C"", ""folderName"": ""C"" }
] }";

        private static TempDirectory Library()
        {
            var temp = new TempDirectory();
            temp.WriteFile("apps.json", AppsJson);
            foreach (var app in new[] { "A", "B", "C" })
            {
                temp.WriteFile($@"Apps\{app}\game.exe", "x");
                temp.WriteFile($@"Apps\{app}\version.txt", "v1");
                temp.WriteFile($@"Apps\{app}\installed-build.txt", "game.zip\r\nhash1");
            }

            return temp;
        }

        [Fact]
        public void Watch_FollowsTheAppQuiverIsUpdating_UntilItsNewDownloadStopsChanging()
        {
            using (var temp = Library())
            {
                var data = new QuiverLibraryData(temp.Path);
                var watch = new QuiverUpdateWatch(data, data.AppsFolder, data.GetStates());
                string started = null;
                watch.UpdatingStarted += n => started = n;

                Assert.False(watch.AppUpdatedAndSettled()); // nothing being updated yet
                watch.OnLine("Checking 3 installed game(s) for updates...");
                watch.OnLine("→ Updating Game B...");
                Assert.Equal("Game B", started);
                Assert.Equal("B", watch.Current.FolderName);
                Assert.False(watch.AppUpdatedAndSettled()); // still the old download

                temp.WriteFile(@"Apps\B\version.txt", "v2");
                temp.WriteFile(@"Apps\B\installed-build.txt", "game.zip\r\nhash2");
                Assert.False(watch.AppUpdatedAndSettled()); // first sight of the new download
                Assert.True(watch.AppUpdatedAndSettled());  // unchanged since
                temp.WriteFile(@"Apps\B\more.dat", "still unpacking");
                Assert.False(watch.AppUpdatedAndSettled());

                watch.OnLine("→ Updating Unknown App...");
                Assert.Null(watch.Current);
                Assert.Equal("Unknown App", watch.CurrentName);
                Assert.False(watch.AppUpdatedAndSettled());
            }
        }

        [Fact]
        public void Run_StartsQuiverAgainAfterEachUpdatedApp_UntilItEndsByItself()
        {
            using (var temp = Library())
            {
                var data = new QuiverLibraryData(temp.Path);
                var pending = new Queue<string>(new[] { "A", "C" });
                var updating = new List<string>();

                var result = QuiverUpdateAll.Run(data, watch =>
                {
                    if (pending.Count == 0)
                    {
                        watch.OnLine("✓ All games are up to date");
                        return new QuiverCliResult { ExitCode = 0 };
                    }

                    var app = pending.Dequeue();
                    watch.OnLine($"→ Updating Game {app}...");
                    temp.WriteFile($@"Apps\{app}\version.txt", "v2");
                    Assert.False(watch.AppUpdatedAndSettled());
                    Assert.True(watch.AppUpdatedAndSettled());
                    return new QuiverCliResult { EndedByWatcher = true };
                }, CancellationToken.None, updating.Add);

                Assert.True(result.Success);
                Assert.Equal(new[] { "Game A", "Game C" }, result.Updated);
                Assert.Equal(new[] { "Game A", "Game C" }, updating);
                Assert.Equal(3, result.Rounds);
            }
        }

        [Fact]
        public void Run_StopsWhenTheSameAppComesUpAgain_OrQuiverFailsOrIsCancelled()
        {
            using (var temp = Library())
            {
                var data = new QuiverLibraryData(temp.Path);
                var version = 2;
                var again = QuiverUpdateAll.Run(data, watch =>
                {
                    watch.OnLine("→ Updating Game A...");
                    temp.WriteFile(@"Apps\A\version.txt", "v" + version++);
                    return new QuiverCliResult { EndedByWatcher = true };
                }, CancellationToken.None);
                Assert.True(again.Stuck);
                Assert.False(again.Success);
                Assert.Equal(new[] { "Game A" }, again.Updated);

                var failed = QuiverUpdateAll.Run(data, watch => new QuiverCliResult { ExitCode = 1, Output = "ERROR: Update failed." }, CancellationToken.None);
                Assert.False(failed.Success);
                Assert.Equal(1, failed.Rounds);

                var timedOut = QuiverUpdateAll.Run(data, watch => new QuiverCliResult { TimedOut = true }, CancellationToken.None);
                Assert.True(timedOut.TimedOut);

                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    var cancelled = QuiverUpdateAll.Run(data, watch => new QuiverCliResult { ExitCode = 0 }, cancel.Token);
                    Assert.True(cancelled.Cancelled);
                    Assert.Equal(0, cancelled.Rounds);
                }
            }
        }
    }
}
