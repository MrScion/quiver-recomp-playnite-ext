using System.Collections.Generic;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class SetupCommandsTests
    {
        [Fact]
        public void FindGameDir_UsesTheInstallMarker_ElseThePlayProgram()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\game\data\install.json", "{}");
                temp.WriteFile(@"App\game\sfa3.exe", "x");
                temp.WriteFile(@"Xcx\xcx\xcx-install.json", "{}");
                var app = temp.Combine("App");

                Assert.Equal(temp.Combine(@"App\game"), SetupCommands.FindGameDir(app, new SetupInfo { InstallMarker = @"data\install.json" }, null));
                Assert.Equal(temp.Combine(@"Xcx\xcx"), SetupCommands.FindGameDir(temp.Combine("Xcx"), new SetupInfo { InstallMarker = "xcx-install.json" }, null));
                Assert.Null(SetupCommands.FindGameDir(app, new SetupInfo { InstallMarker = @"other\install.json" }, null));
                Assert.Null(SetupCommands.FindGameDir(temp.Combine("Xcx"), new SetupInfo { InstallMarker = "missing.json" }, null));
                Assert.Equal(temp.Combine(@"App\game"), SetupCommands.FindGameDir(app, new SetupInfo(), temp.Combine(@"App\game\sfa3.exe")));
                Assert.Null(SetupCommands.FindGameDir(app, new SetupInfo(), null));
            }
        }

        [Fact]
        public void Prepare_FillsTheFolders_AndListsWhatIsMissing()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\python\python.exe", "x");
                temp.WriteFile(@"App\Setup.exe", "x");
                var app = temp.Combine("App");

                var update = SetupCommands.Prepare(app, new ExecutableTarget { File = @"python\python.exe", Arguments = "-m installer --dest \"{GameDir}\" --root \"{AppDir}\"" }, temp.Combine(@"App\game\"));
                Assert.True(update.CanRun);
                Assert.Equal(temp.Combine(@"App\python\python.exe"), update.File);
                Assert.Equal($"-m installer --dest \"{temp.Combine(@"App\game")}\" --root \"{app}\"", update.Arguments);
                Assert.Equal(app, update.WorkingDirectory);

                var install = SetupCommands.Prepare(app, new ExecutableTarget { File = "Setup.exe", Arguments = "--iso \"{Image}\" --path \"{GameDir}\"" }, null,
                    new Dictionary<string, string> { { "image", @"D:\disc.iso" } });
                Assert.False(install.CanRun);
                Assert.Equal(new[] { "GameDir" }, install.Missing);
                Assert.Contains(@"--iso ""D:\disc.iso""", install.Arguments);

                Assert.False(SetupCommands.Prepare(app, new ExecutableTarget { File = "Missing.exe" }, null).CanRun);
                Assert.True(SetupCommands.Prepare(app, new ExecutableTarget { File = "Setup.exe" }, null).CanRun);
            }
        }

        [Fact]
        public void CanAsk_OnlyForFilesThePlayerChooses_AndValuesOfMissingFilesAreDropped()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\Setup.exe", "x");
                temp.WriteFile(@"Discs\game.iso", "x");
                var app = temp.Combine("App");
                var command = new ExecutableTarget { File = "Setup.exe", Arguments = "--iso \"{Image}\" --tu \"{TitleUpdate}\" --path \"{GameDir}\"" };

                var noFiles = SetupCommands.Prepare(app, command, temp.Combine("App"));
                Assert.False(noFiles.CanRun);
                Assert.True(noFiles.CanAsk);
                Assert.Equal(new[] { "Image", "TitleUpdate" }, noFiles.Missing);

                var values = SetupCommands.Existing(new Dictionary<string, string>
                {
                    { "Image", temp.Combine(@"Discs\game.iso") },
                    { "TitleUpdate", temp.Combine(@"Discs\moved.bin") },
                });
                var oneFile = SetupCommands.Prepare(app, command, temp.Combine("App"), values);
                Assert.Equal(new[] { "TitleUpdate" }, oneFile.Missing);
                Assert.True(oneFile.CanAsk);

                values["TitleUpdate"] = temp.Combine("Discs");
                Assert.True(SetupCommands.Prepare(app, command, temp.Combine("App"), SetupCommands.Existing(values)).CanRun);

                var noGame = SetupCommands.Prepare(app, command, null, values);
                Assert.False(noGame.CanRun);
                Assert.False(noGame.CanAsk);
            }
        }

        [Fact]
        public void OutsideInstallDirs_ListsAChosenProgramOutside_AndTheDefaultFolder_WhenTheyExist()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\Setup.exe", "x");
                temp.WriteFile(@"App\game\game.exe", "x");
                temp.WriteFile(@"Elsewhere\game.exe", "x");
                temp.CreateFolder("Default");
                var app = temp.Combine("App");
                var installer = new SetupInfo { Kind = SetupInfo.Installer, DefaultInstallDir = temp.Combine("Default") };

                Assert.Equal(new[] { temp.Combine("Elsewhere"), temp.Combine("Default") },
                    SetupCommands.OutsideInstallDirs(app, installer, temp.Combine(@"Elsewhere\game.exe")));
                // A program chosen inside the app folder (stored relative) is removed with it.
                Assert.Equal(new[] { temp.Combine("Default") }, SetupCommands.OutsideInstallDirs(app, installer, @"game\game.exe"));
                Assert.Empty(SetupCommands.OutsideInstallDirs(app, new SetupInfo { Kind = SetupInfo.Installer, DefaultInstallDir = temp.Combine("Missing") }, null));
                Assert.Empty(SetupCommands.OutsideInstallDirs(app, new SetupInfo { Kind = SetupInfo.Builder, DefaultInstallDir = temp.Combine("Default") }, null));
                Assert.Empty(SetupCommands.OutsideInstallDirs(app, null, temp.Combine(@"Elsewhere\game.exe")));
            }
        }

        [Fact]
        public void PrepareUpdate_KeepsAnUpdateWithAKnownProblem_FromRunning()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\SFA3-Setup.exe", "x");
                temp.WriteFile(@"App\game\data\install.json", "{}");
                var db = ExecutableDatabase.Load(null);
                var sfa3 = db.FindSetup(new QuiverApp { Repository = "irnutsmurt/Street-Alpha-3-Dreamcast-Recomp" });

                var prepared = SetupCommands.PrepareUpdate(temp.Combine("App"), sfa3, null);
                Assert.False(prepared.CanRun);
                Assert.NotNull(prepared.Problem);
                Assert.Equal($"--update \"{temp.Combine(@"App\game")}\"", prepared.Arguments);

                var wet = db.FindSetup(new QuiverApp { Repository = "zonehammer/wet-recomp" });
                temp.WriteFile(@"Wet\WetSetup.exe", "x");
                Assert.True(SetupCommands.PrepareUpdate(temp.Combine("Wet"), wet, null).CanRun);
                Assert.False(SetupCommands.PrepareUpdate(temp.Combine("Wet"), new SetupInfo { Kind = SetupInfo.Installer }, null).CanRun);
            }
        }
    }
}
