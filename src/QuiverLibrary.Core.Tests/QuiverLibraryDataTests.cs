using System.IO;
using System.Linq;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverLibraryDataTests
    {
        private const string AppsJson = @"{
  ""apps"": [
    { ""name"": ""Zelda 64"", ""folderName"": ""Zelda64Recomp"", ""installPath"": null, ""appIconUrl"": ""https://example.com/z.png"", ""repository"": ""Zelda64Recomp/Zelda64Recomp"", ""project"": ""Zelda64Recomp"", ""tags"": [""n64""], ""catalog"": { ""x"": 1 } },
    { ""name"": ""Mario Kart 64"", ""folderName"": ""SpaghettiKart"", ""project"": ""SpaghettiKart"" },
    { ""name"": ""Duplicate"", ""folderName"": ""zelda64recomp"" },
    { ""name"": ""No folder"" }
  ]
}";

        [Fact]
        public void LoadApps_ParsesCamelCase_IgnoresUnknownFields_AndDedupesByFolderName()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile("apps.json", AppsJson);

                var apps = new QuiverLibraryData(temp.Path).LoadApps();

                Assert.Equal(2, apps.Count);
                var zelda = apps.Single(a => a.FolderName == "Zelda64Recomp");
                Assert.Equal("Zelda 64", zelda.Name);
                Assert.Null(zelda.InstallPath);
                Assert.Equal("https://example.com/z.png", zelda.AppIconUrl);
                Assert.Equal("Zelda64Recomp/Zelda64Recomp", zelda.Repository);
            }
        }

        [Fact]
        public void LoadApps_WithoutAppsJson_Throws()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Throws<FileNotFoundException>(() => new QuiverLibraryData(temp.Path).LoadApps());
            }
        }

        [Fact]
        public void AppsFolder_UsesSettingsAppsPath_OrDefaultsToApps()
        {
            using (var temp = new TempDirectory())
            {
                var data = new QuiverLibraryData(temp.Path);
                Assert.Equal(temp.Combine("Apps"), data.AppsFolder);

                temp.WriteFile("settings.json", @"{ ""AppsPath"": """" }");
                Assert.Equal(temp.Combine("Apps"), data.AppsFolder);

                temp.WriteFile("settings.json", @"{ ""AppsPath"": ""D:\\Games\\Quiver"", ""HiddenApps"": [] }", bom: true);
                Assert.Equal(@"D:\Games\Quiver", data.AppsFolder);

                temp.WriteFile("settings.json", @"{ ""AppsPath"": ""E:\\Quiver"" }", bom: false);
                Assert.Equal(@"E:\Quiver", data.AppsFolder);
            }
        }

        private static QuiverAppState StateOf(TempDirectory temp, string folderName = "Game")
        {
            var data = new QuiverLibraryData(temp.Path);
            return data.GetState(new QuiverApp { FolderName = folderName }, temp.Combine("Apps"));
        }

        [Fact]
        public void GetState_MissingFolder_NotInstalled()
        {
            using (var temp = new TempDirectory())
            {
                var state = StateOf(temp);

                Assert.False(state.IsInstalled);
                Assert.Equal(temp.Combine("Apps", "Game"), state.InstallDirectory);
            }
        }

        [Fact]
        public void GetState_FolderWithExe_InstalledWithVersion()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\game.exe");
                temp.WriteFile(@"Apps\Game\version.txt", " v1.2.3 \r\n");

                var state = StateOf(temp);

                Assert.True(state.IsInstalled);
                Assert.Equal("v1.2.3", state.Version);
            }
        }

        [Fact]
        public void GetState_IncompleteMarker_NotInstalled()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\game.exe");
                temp.WriteFile(@"Apps\Game\install-incomplete.txt");

                Assert.False(StateOf(temp).IsInstalled);
            }
        }

        [Fact]
        public void GetState_OnlyMetadataFiles_NotInstalled()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\version.txt", "v1");

                Assert.False(StateOf(temp).IsInstalled);
            }
        }

        [Fact]
        public void GetState_BatInSubfolder_Installed()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\bin\launch.bat");

                Assert.True(StateOf(temp).IsInstalled);
            }
        }

        [Fact]
        public void GetState_ExplicitInstallPath_IsUsed()
        {
            using (var temp = new TempDirectory())
            {
                var dir = temp.CreateFolder("Elsewhere");
                temp.WriteFile(@"Elsewhere\game.exe");
                var data = new QuiverLibraryData(temp.Path);

                var state = data.GetState(new QuiverApp { FolderName = "Game", InstallPath = dir }, temp.Combine("Apps"));

                Assert.True(state.IsInstalled);
                Assert.Equal(dir, state.InstallDirectory);
            }
        }

        [Fact]
        public void GetState_InstallerReceipt_LinkedExe_InstalledWithReleaseTag()
        {
            using (var temp = new TempDirectory())
            {
                var exe = temp.WriteFile(@"ProgramFiles\Game\game.exe");
                temp.WriteFile(@"Apps\Game\windows-installer.json",
                    "{\"Phase\":\"Linked\",\"ReleaseTag\":\"v2.0\",\"ExecutablePath\":\"" + exe.Replace(@"\", @"\\") + "\"}", bom: false);

                var state = StateOf(temp);

                Assert.True(state.IsInstalled);
                Assert.Equal("v2.0", state.Version);
                Assert.Equal(exe, QuiverLibraryData.ResolveExecutable(state.InstallDirectory));
            }
        }

        [Fact]
        public void GetState_InstallerReceipt_NeedsExecutable_NotInstalledEvenWithExeInFolder()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\setup.exe");
                temp.WriteFile(@"Apps\Game\windows-installer.json",
                    "{\"Phase\":\"NeedsExecutable\",\"ReleaseTag\":\"v2.0\",\"ExecutablePath\":null}", bom: false);

                Assert.False(StateOf(temp).IsInstalled);
            }
        }

        [Fact]
        public void ResolveExecutable_SelectedExecutableWins()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\a.exe");
                var b = temp.WriteFile(@"Game\b.exe");
                temp.WriteFile(@"Game\selected_executable.txt", b + "\n");

                Assert.Equal(b, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_MissingSelectedExecutable_FallsBackToSingleCandidate()
        {
            using (var temp = new TempDirectory())
            {
                var a = temp.WriteFile(@"Game\a.exe");
                temp.WriteFile(@"Game\selected_executable.txt", temp.Combine("Game", "gone.exe"));

                Assert.Equal(a, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_TopFolderCandidate_BeatsNestedOnes()
        {
            using (var temp = new TempDirectory())
            {
                var top = temp.WriteFile(@"Game\game.exe");
                temp.WriteFile(@"Game\tools\editor.exe");

                Assert.Equal(top, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_OnlyNested_UsesSubfolders()
        {
            using (var temp = new TempDirectory())
            {
                var nested = temp.WriteFile(@"Game\bin\game.exe");

                Assert.Equal(nested, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_HelperExecutablesBreakTie()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\game.exe");
                temp.WriteFile(@"Game\unins000.exe");
                temp.WriteFile(@"Game\vc_redist.x64.exe");

                Assert.Equal(game, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_StaleSelectedExecutable_IsFoundInTheMovedFolder()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Game\a.exe");
                var b = temp.WriteFile(@"Apps\Game\bin\b.exe");
                temp.WriteFile(@"Apps\Game\c.exe");
                temp.WriteFile(@"Apps\Game\selected_executable.txt", @"C:\Old\Place\Apps\Game\bin\b.exe");

                Assert.Equal(b, QuiverLibraryData.ReadSelectedExecutable(temp.Combine("Apps", "Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_StaleSelectedExecutable_SettlesTie()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\a.exe");
                var b = temp.WriteFile(@"Game\b.exe");
                temp.WriteFile(@"Game\selected_executable.txt", @"C:\Users\someone\Desktop\Apps\Game\b.exe");

                Assert.Equal(b, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_GameBeatsLauncher_EvenWhenLauncherWasPicked()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\SilentHillPC.exe");
                var launcher = temp.WriteFile(@"Game\SilentHillPC_Launcher.exe");
                temp.WriteFile(@"Game\selected_executable.txt", launcher);

                Assert.Equal(game, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_OnlyLaunchers_KeepsTheLauncher()
        {
            using (var temp = new TempDirectory())
            {
                var launcher = temp.WriteFile(@"Game\GameLauncher.exe");
                temp.WriteFile(@"Game\unins000.exe");

                Assert.Equal(launcher, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_CrashReportersBreakTie()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\retip.exe");
                temp.WriteFile(@"Game\rexcrashreporter.exe");
                temp.WriteFile(@"Game\sentry-crash.exe");

                Assert.Equal(game, QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void ResolveExecutable_SeveralGameExecutables_ReturnsNull()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\game.exe");
                temp.WriteFile(@"Game\game-vulkan.exe");

                Assert.Null(QuiverLibraryData.ResolveExecutable(temp.Combine("Game")));
            }
        }

        [Fact]
        public void InstalledAndSettledCheck_TrueOnlyOnceInstalledAndUnchanged()
        {
            using (var temp = new TempDirectory())
            {
                var data = new QuiverLibraryData(temp.Path);
                var check = data.InstalledAndSettledCheck(new QuiverApp { FolderName = "Game" }, temp.Combine("Apps"));

                Assert.False(check()); // not installed
                temp.WriteFile(@"Apps\Game\game.exe", "a");
                Assert.False(check()); // first sight of the installed folder
                Assert.True(check());  // unchanged since last call
                temp.WriteFile(@"Apps\Game\data.bin", "more");
                Assert.False(check()); // still being written
                Assert.True(check());
                temp.WriteFile(@"Apps\Game\install-incomplete.txt");
                Assert.False(check()); // Quiver marked it incomplete
            }
        }

        [Fact]
        public void FindExecutableCandidates_IgnoresLongerExtensionsAndWinePrefixes()
        {
            using (var temp = new TempDirectory())
            {
                var exe = temp.WriteFile(@"Game\game.exe");
                temp.WriteFile(@"Game\game.exe_old");
                temp.WriteFile(@"Game\.wine-prefix\drive_c\other.exe");

                var candidates = QuiverLibraryData.FindExecutableCandidates(temp.Combine("Game"), recursive: true);

                Assert.Equal(new[] { exe }, candidates);
            }
        }
    }
}
