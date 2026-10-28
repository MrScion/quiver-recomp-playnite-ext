using System;
using System.Linq;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverSavesTests
    {
        [Fact]
        public void Marker_PortableAppUsesItsFolderFirst_OtherwiseAppData()
        {
            using (var temp = new TempDirectory())
            {
                var play = temp.WriteFile(@"App\Game.exe");
                var local = temp.CreateFolder(@"App\saves");
                var appData = temp.CreateFolder(@"AppData\Game\saves");
                var userData = new UserDataInfo { Portable = "portable.txt", Local = "", AppData = temp.Combine(@"AppData\Game"), Saves = "saves" };

                Assert.Equal(new[] { appData, local }, QuiverSaves.Find(temp.Combine("App"), play, userData, false).Select(f => f.Path));

                temp.WriteFile(@"App\portable.txt");
                Assert.Equal(new[] { local, appData }, QuiverSaves.Find(temp.Combine("App"), play, userData, false).Select(f => f.Path));
            }
        }

        [Fact]
        public void NothingSavedYet_GivesTheFolderTheAppWillUse()
        {
            using (var temp = new TempDirectory())
            {
                var play = temp.WriteFile(@"App\Game.exe");
                var userData = new UserDataInfo { Portable = "none", AppData = "%QUIVER_TEST_SAVES%" };
                Environment.SetEnvironmentVariable("QUIVER_TEST_SAVES", temp.Combine("Missing"));
                try
                {
                    var folder = Assert.Single(QuiverSaves.Find(temp.Combine("App"), play, userData, false));

                    Assert.Equal(temp.Combine("Missing"), folder.Path);
                    Assert.False(folder.Exists);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("QUIVER_TEST_SAVES", null);
                }
            }
        }

        [Fact]
        public void SeveralSaves_FoldersAndFiles_BackedUpEach_ShownByFolder()
        {
            using (var temp = new TempDirectory())
            {
                var play = temp.WriteFile(@"App\Game.exe");
                var sdmc = temp.CreateFolder(@"App\user\sdmc");
                var nand = temp.CreateFolder(@"App\user\nand");
                var app = temp.Combine("App");

                Assert.Equal(new[] { sdmc, nand },
                    QuiverSaves.ForBackup(app, play, new UserDataInfo { Portable = "builtin", Local = "user", Saves = "sdmc; nand" }, false));

                var loose = new UserDataInfo { Portable = "builtin", Local = "", Saves = "default.sav;*.cfg" };
                Assert.Equal(new[] { temp.Combine(@"App\default.sav"), temp.Combine(@"App\*.cfg") }, QuiverSaves.ForBackup(app, play, loose, false));
                var folder = Assert.Single(QuiverSaves.Find(app, play, loose, false));
                Assert.Equal(app, folder.Path);
                Assert.True(folder.Exists);
            }
        }

        [Fact]
        public void Builtin_LocalFromProgram_FollowsTheGameTheInstallerPlaced()
        {
            using (var temp = new TempDirectory())
            {
                var play = temp.WriteFile(@"App\my game\tekken6.exe");
                var user = temp.CreateFolder(@"App\my game\user");
                var userData = new UserDataInfo { Portable = "builtin", Local = "user" };

                var folder = Assert.Single(QuiverSaves.Find(temp.Combine("App"), play, userData, true));

                Assert.Equal(user, folder.Path);
                Assert.True(folder.Exists);
                Assert.Empty(QuiverSaves.Find(temp.Combine("App"), play, null, false));
            }
        }
    }
}
