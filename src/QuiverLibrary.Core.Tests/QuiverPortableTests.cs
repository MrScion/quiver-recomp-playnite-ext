using System.IO;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverPortableTests
    {
        [Fact]
        public void Apply_WithoutUserData_CreatesAnEmptyPortableTxtNextToThePlayProgram()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"App\bin\game.exe");

                var result = QuiverPortable.Apply(game, null);

                Assert.Equal(PortableOutcome.Created, result.Outcome);
                Assert.Equal(temp.Combine("App", "bin", "portable.txt"), result.MarkerPath);
                Assert.Equal(0, new FileInfo(result.MarkerPath).Length);
                Assert.Equal(PortableOutcome.AlreadyPortable, QuiverPortable.Apply(game, null).Outcome);
            }
        }

        [Fact]
        public void Apply_UsesTheEntrysMarker_AndSkipsBuiltinAndUnsupportedApps()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"App\game.exe");

                Assert.Equal(PortableOutcome.Builtin, QuiverPortable.Apply(game, new UserDataInfo { Portable = "builtin" }).Outcome);
                Assert.Equal(PortableOutcome.Unsupported, QuiverPortable.Apply(game, new UserDataInfo { Portable = "none" }).Outcome);
                Assert.False(File.Exists(temp.Combine("App", "portable.txt")));

                var result = QuiverPortable.Apply(game, new UserDataInfo { Portable = "portable.ini" });
                Assert.Equal(PortableOutcome.Created, result.Outcome);
                Assert.True(File.Exists(temp.Combine("App", "portable.ini")));
            }
        }

        [Fact]
        public void Apply_WithDataInTheUsualFolder_CreatesNothing_UntilThatFolderIsEmpty()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"App\game.exe");
                var save = temp.WriteFile(@"AppData\Game\saves\file.bin");
                var userData = new UserDataInfo { Portable = "portable.txt", AppData = temp.Combine("AppData", "Game"), Local = "" };

                var result = QuiverPortable.Apply(game, userData);

                Assert.Equal(PortableOutcome.HasUserData, result.Outcome);
                Assert.Equal(temp.Combine("AppData", "Game"), result.UserDataPath);
                Assert.False(File.Exists(result.MarkerPath));

                Directory.Delete(temp.Combine("AppData", "Game", "saves"), true);
                Assert.Equal(PortableOutcome.Created, QuiverPortable.Apply(game, userData).Outcome);
            }
        }

        [Fact]
        public void Apply_AlreadyPortable_ReportsDataLeftInTheUsualFolder()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"App\game.exe");
                temp.WriteFile(@"App\portable.txt");
                temp.WriteFile(@"AppData\Game\saves\file.bin");

                var result = QuiverPortable.Apply(game, new UserDataInfo { AppData = temp.Combine("AppData", "Game") });

                Assert.Equal(PortableOutcome.AlreadyPortable, result.Outcome);
                Assert.Equal(temp.Combine("AppData", "Game"), result.UserDataPath);
            }
        }

        [Fact]
        public void Apply_WithoutAPlayProgram_DoesNothing()
        {
            Assert.Equal(PortableOutcome.NoProgram, QuiverPortable.Apply(null, null).Outcome);
            Assert.Equal(PortableOutcome.NoProgram, QuiverPortable.Apply(@"Z:\nowhere\at\all\game.exe", null).Outcome);
        }

        [Fact]
        public void UserDataFolderWithData_ExpandsEnvironmentVariables()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Data\settings.json");
                System.Environment.SetEnvironmentVariable("QUIVER_TEST_ROOT", temp.Path);
                try
                {
                    Assert.Equal(temp.Combine("Data"), QuiverPortable.UserDataFolderWithData(new UserDataInfo { AppData = @"%QUIVER_TEST_ROOT%\Data" }));
                    Assert.Null(QuiverPortable.UserDataFolderWithData(new UserDataInfo { AppData = @"%QUIVER_TEST_ROOT%\Missing" }));
                    Assert.Null(QuiverPortable.UserDataFolderWithData(new UserDataInfo()));
                }
                finally
                {
                    System.Environment.SetEnvironmentVariable("QUIVER_TEST_ROOT", null);
                }
            }
        }
    }
}
