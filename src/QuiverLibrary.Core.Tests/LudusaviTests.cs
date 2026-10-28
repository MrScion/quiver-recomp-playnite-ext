using System.IO;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Ludusavi;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class LudusaviTests
    {
        private const string Manifest = @"C:\Playnite\ExtensionsData\a48d03b5\ludusavi-recomps.yaml";

        // As Ludusavi writes it (the user's own config, trimmed).
        private const string Config =
            "---\n" +
            "manifest:\n" +
            "  url: \"https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml\"\n" +
            "  etag: \"\\\"6351ee\\\"\"\n" +
            "roots:\n" +
            "  - path: \"H:\\\\Steam\"\n" +
            "    store: steam\n" +
            "backup:\n" +
            "  path: \"C:\\\\Users\\\\me\\\\ludusavi-backup\"\n";

        [Fact]
        public void AddSecondaryManifest_AddsItToTheManifestSection_Once()
        {
            var updated = LudusaviConfig.AddSecondaryManifest(Config, Manifest, out var result);

            Assert.Equal(LudusaviRegistration.Added, result);
            Assert.Contains(
                "  etag: \"\\\"6351ee\\\"\"\n" +
                "  secondary:\n" +
                "    - path: \"C:\\\\Playnite\\\\ExtensionsData\\\\a48d03b5\\\\ludusavi-recomps.yaml\"\n" +
                "roots:\n", updated);

            Assert.Equal(updated, LudusaviConfig.AddSecondaryManifest(updated, Manifest, out result));
            Assert.Equal(LudusaviRegistration.AlreadyRegistered, result);
        }

        [Fact]
        public void AddSecondaryManifest_AppendsToExistingOnes_AndKeepsCrLf()
        {
            var config = (Config.Replace("roots:\n",
                "  secondary:\n" +
                "    - url: \"https://example.com/extra.yaml\"\n" +
                "      enable: true\n" +
                "roots:\n")).Replace("\n", "\r\n");

            var updated = LudusaviConfig.AddSecondaryManifest(config, Manifest, out var result);

            Assert.Equal(LudusaviRegistration.Added, result);
            Assert.Contains(
                "      enable: true\r\n" +
                "    - path: \"C:\\\\Playnite\\\\ExtensionsData\\\\a48d03b5\\\\ludusavi-recomps.yaml\"\r\n" +
                "roots:\r\n", updated);
            Assert.DoesNotContain("\r\r", updated);
        }

        [Fact]
        public void AddSecondaryManifest_EmptyListOrNoSection_AndLeavesFlowListsAlone()
        {
            var empty = LudusaviConfig.AddSecondaryManifest(Config.Replace("roots:\n", "  secondary: []\nroots:\n"), Manifest, out var result);
            Assert.Equal(LudusaviRegistration.Added, result);
            Assert.Contains("  secondary:\n    - path: ", empty);

            var none = LudusaviConfig.AddSecondaryManifest("roots: []\n", Manifest, out result);
            Assert.Equal(LudusaviRegistration.Added, result);
            Assert.Equal("roots: []\nmanifest:\n  secondary:\n    - path: \"C:\\\\Playnite\\\\ExtensionsData\\\\a48d03b5\\\\ludusavi-recomps.yaml\"\n", none);

            var flow = Config.Replace("roots:\n", "  secondary: [{path: x.yaml}]\nroots:\n");
            Assert.Equal(flow, LudusaviConfig.AddSecondaryManifest(flow, Manifest, out result));
            Assert.Equal(LudusaviRegistration.Unrecognized, result);
        }

        [Fact]
        public void Register_KeepsABackup_AndReportsAMissingConfig()
        {
            using (var temp = new TempDirectory())
            {
                var config = temp.WriteFile(@"ludusavi\config.yaml", Config, bom: false);

                Assert.Equal(LudusaviRegistration.Added, LudusaviConfig.Register(config, Manifest));
                Assert.Equal(Config, File.ReadAllText(config + ".bak"));
                Assert.Equal(LudusaviRegistration.AlreadyRegistered, LudusaviConfig.Register(config, Manifest));
                Assert.Equal(LudusaviRegistration.NoConfig, LudusaviConfig.Register(temp.Combine("missing.yaml"), Manifest));
            }
        }

        [Fact]
        public void ReadExecutablePath_ReadsTheExtensionsSetting_AmongTheOthers()
        {
            using (var temp = new TempDirectory())
            {
                var config = temp.WriteFile("config.json", "{\"ExecutablePath\":\"D:\\\\Tools\\\\ludusavi.exe\",\"OverrideBackupPath\":true,\"AlternativeTitles\":{}}");

                Assert.Equal(@"D:\Tools\ludusavi.exe", LudusaviPlayniteSettings.ReadExecutablePath(config));
                Assert.Null(LudusaviPlayniteSettings.ReadExecutablePath(temp.Combine("missing.json")));
            }
        }

        [Fact]
        public void FindConfigPath_PortableNextToTheProgram_ElseAppData()
        {
            using (var temp = new TempDirectory())
            {
                var exe = temp.WriteFile(@"Tools\Ludusavi\ludusavi.exe");
                var appData = temp.Combine("AppData");

                Assert.Equal(Path.Combine(appData, "ludusavi", "config.yaml"), LudusaviConfig.FindConfigPath(exe, appData, null));

                temp.WriteFile(@"Tools\Ludusavi\ludusavi.portable");
                Assert.Equal(temp.Combine(@"Tools\Ludusavi\config.yaml"), LudusaviConfig.FindConfigPath(exe, appData, null));
                Assert.Equal(temp.Combine(@"Tools\Ludusavi\config.yaml"), LudusaviConfig.FindConfigPath("ludusavi", appData, @"C:\nowhere;" + temp.Combine(@"Tools\Ludusavi")));
            }
        }

        [Fact]
        public void Manifest_ListsEachGameByName_WithEscapedPaths()
        {
            var yaml = LudusaviManifest.Build(new[]
            {
                new LudusaviGame { Name = "Zelda's [Recomp]", Folders = { @"C:\Saves\Zelda [EU]\", @"C:\Saves\Zelda [EU]" } },
                new LudusaviGame { Name = "No folders" },
            });

            Assert.Contains("'Zelda''s [Recomp]':\n  files:\n    'C:/Saves/Zelda [[]EU[]]':\n      tags:\n        - save\n", yaml);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(yaml, "C:/Saves"));
            Assert.DoesNotContain("No folders", yaml);
        }

        [Fact]
        public void SavesForBackup_BothFolders_ButNeverTheWholeGameFolder()
        {
            using (var temp = new TempDirectory())
            {
                var play = temp.WriteFile(@"App\Game.exe");
                var app = temp.Combine("App");

                Assert.Equal(new[] { temp.Combine(@"AppData\Game"), temp.Combine(@"App\saves") },
                    QuiverSaves.ForBackup(app, play, new UserDataInfo { Portable = "portable.txt", Local = "saves", AppData = temp.Combine(@"AppData\Game") }, false));
                Assert.Equal(new[] { temp.Combine(@"AppData\Game") },
                    QuiverSaves.ForBackup(app, play, new UserDataInfo { Portable = "portable.txt", Local = "", AppData = temp.Combine(@"AppData\Game") }, false));
                Assert.Empty(QuiverSaves.ForBackup(app, play, new UserDataInfo { Portable = "builtin", Local = "" }, false));
                // The menu still opens it: that's where the saves are.
                Assert.Equal(app, Assert.Single(QuiverSaves.Find(app, play, new UserDataInfo { Portable = "builtin", Local = "" }, false)).Path);
            }
        }
    }
}
