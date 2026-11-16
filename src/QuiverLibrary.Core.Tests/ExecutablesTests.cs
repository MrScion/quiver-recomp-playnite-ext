using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class ExecutablesTests
    {
        private static readonly QuiverApp Sms = new QuiverApp { Name = "Super Mario Sunshine", FolderName = "SuperMarioSunshine-SMSLauncher", Repository = "chasem-dev/sms-launcher" };

        private static ExecutableEntry Entry(string play, string arguments = null, params ExecutableTarget[] others)
        {
            return new ExecutableEntry
            {
                Repository = "owner/repo",
                Play = play == null ? null : new ExecutableTarget { File = play, Arguments = arguments },
                Others = others.ToList(),
                Reviewed = true,
            };
        }

        private static QuiverAppState Installed(QuiverApp app, string dir) => new QuiverAppState { App = app, IsInstalled = true, InstallDirectory = dir };

        [Fact]
        public void BundledDatabase_LoadsAndEveryEntryIsUsable()
        {
            var entries = ExecutableDatabase.LoadBundled();

            Assert.NotEmpty(entries);
            Assert.All(entries, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Repository) && string.IsNullOrWhiteSpace(e.FolderName));
                Assert.True(e.HasLaunchInfo || e.UserData != null || e.Achievements != null || e.Setup != null);
                // Without a kind, a setup only says when the game is ready (set up once, not after updates).
                Assert.True(e.Setup == null || e.Setup.IsInstaller || e.Setup.IsBuilder || (e.Setup.Kind == null && !string.IsNullOrWhiteSpace(e.Setup.Built)));
                Assert.True(e.Setup?.Install == null || !string.IsNullOrWhiteSpace(e.Setup.Install.File));
                Assert.True(e.Setup?.Update == null || !string.IsNullOrWhiteSpace(e.Setup.Update.File));
                if (e.HasLaunchInfo)
                {
                    Assert.False(string.IsNullOrWhiteSpace(e.Play?.File));
                }

                Assert.True(e.Reviewed);
                Assert.False(string.IsNullOrWhiteSpace(e.Source));
                Assert.All(e.Others ?? new List<ExecutableTarget>(), o => Assert.False(string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(o.File)));
                if (e.UserData != null)
                {
                    Assert.False(string.IsNullOrWhiteSpace(e.UserData.Portable));
                    // A portable app needs both folders: where its data was, to warn, and where it goes.
                    Assert.True(e.UserData.Marker == null || (e.UserData.AppData != null && e.UserData.Local != null));
                    Assert.True(!e.UserData.IsBuiltin || e.UserData.Local != null);
                }

                if (e.Achievements != null)
                {
                    Assert.Contains(e.Achievements.Kind, new[] { AchievementsInfo.ReXGlue, AchievementsInfo.RetroAchievements, AchievementsInfo.Ps4Trophy, AchievementsInfo.Custom, AchievementsInfo.None });
                }
            });
            Assert.Equal(entries.Count, entries.Select(e => e.FolderName ?? e.Repository.ToLowerInvariant()).Distinct().Count());
        }

        [Fact]
        public void Plan_BundledSmsEntry_PlaysHighestVersionWithArguments()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\SMS-Launcher-0.1.57-win-portable.exe");
                var latest = temp.WriteFile(@"Game\SMS-Launcher-0.1.60-win-portable.exe");
                temp.WriteFile(@"Game\SMS-Launcher-0.1.9-win-portable.exe");
                temp.WriteFile(@"Game\SMS-Launcher-Setup-0.1.56.exe");

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), ExecutableDatabase.Load(null).Find(Sms));

                Assert.Equal(latest, plan.Play.Path);
                Assert.Equal("--play", plan.Play.Arguments);
                var setup = Assert.Single(plan.Others);
                Assert.Equal("Setup", setup.Name);
                Assert.Equal(latest, setup.Path);
                Assert.Null(setup.Arguments);
            }
        }

        [Theory]
        [InlineData("Play God of War III.exe", "God of War III.exe")]
        [InlineData("Play SpartanPort.exe", "SpartanPort.exe")]
        public void Plan_BundledGow3Entry_HandlesOldAndNewProgramNames(string play, string launcher)
        {
            using (var temp = new TempDirectory())
            {
                var playPath = temp.WriteFile(@"Game\" + play);
                var launcherPath = temp.WriteFile(@"Game\" + launcher);
                var app = new QuiverApp { Name = "God of War III Remastered", FolderName = "GodOfWarIIIRemastered-Gow3Pc", Repository = "FelipePrado0/gow3_pc" };

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), ExecutableDatabase.Load(null).Find(app));

                Assert.Equal(playPath, plan.Play.Path);
                var setup = Assert.Single(plan.Others);
                Assert.Equal("Setup", setup.Name);
                Assert.Equal(launcherPath, setup.Path);
            }
        }

        [Fact]
        public void Plan_Entry_BeatsQuiverSelection_AndSkipsMissingOthers()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\game.exe");
                var launcher = temp.WriteFile(@"Game\tools\Launcher.exe");
                temp.WriteFile(@"Game\selected_executable.txt", launcher);

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), Entry("game.exe", null,
                    new ExecutableTarget { Name = "Setup", File = @"tools\Launcher.exe" },
                    new ExecutableTarget { Name = "Gone", File = "gone.exe" }));

                Assert.Equal(game, plan.Play.Path);
                Assert.Equal(new[] { "Setup" }, plan.Others.Select(o => o.Name));
                Assert.Equal(launcher, plan.Others[0].Path);
            }
        }

        [Fact]
        public void Plan_EntryWithMissingPlayFile_FallsBackToTheAutomaticChoice()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\renamed.exe");

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), Entry("old-name.exe"));

                Assert.Equal(game, plan.Play.Path);
            }
        }

        [Fact]
        public void Plan_WithoutEntry_OffersTheOtherCandidates_AndNamesASingleLauncherSetup()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Game\Game.exe");
                temp.WriteFile(@"Game\Game_Launcher.exe");
                temp.WriteFile(@"Game\Game_Vulkan.exe");
                temp.WriteFile(@"Game\sentry-crash.exe");
                temp.WriteFile(@"Game\selected_executable.txt", game);

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), null);

                Assert.Equal(game, plan.Play.Path);
                Assert.Equal(new[] { "Setup", "Game_Vulkan" }, plan.Others.Select(o => o.Name));
            }
        }

        [Fact]
        public void Plan_BundledTekken6Entry_FindsTheGameWhereverThePlayerBuiltIt()
        {
            using (var temp = new TempDirectory())
            {
                var builder = temp.WriteFile(@"Game\Tekken 6 Builder.exe");
                temp.WriteFile(@"Game\builder\cmake\bin\cmake.exe");
                var launcher = temp.WriteFile(@"Game\My Games\Tekken\tekken6.exe");
                var engine = temp.WriteFile(@"Game\My Games\Tekken\bin\tekken6.exe");
                var vulkan = temp.WriteFile(@"Game\My Games\Tekken\tekken6_vulkan.exe");
                var app = new QuiverApp { Name = "Tekken 6", FolderName = "Tekken6-Tekken6Recomp", Repository = "mahmoud-mess/tekken6-recomp" };

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), ExecutableDatabase.Load(null).Find(app));

                Assert.Equal(launcher, plan.Play.Path);
                Assert.Equal(engine, plan.Play.Program);
                Assert.True(plan.Play.Borderless);
                Assert.Equal(new[] { "Launcher", "Vulkan", "Setup" }, plan.Others.Select(o => o.Name));
                Assert.Equal(new[] { launcher, vulkan, builder }, plan.Others.Select(o => o.Path));
            }
        }

        [Fact]
        public void Plan_SubfolderPlayNotBuiltYet_HasNoPlayInsteadOfTheInstaller()
        {
            using (var temp = new TempDirectory())
            {
                var installer = temp.WriteFile(@"Game\ttt2_setup.exe");
                var entry = Entry(null, null, new ExecutableTarget { Name = "Setup", File = "ttt2_setup.exe" });
                entry.Play = new ExecutableTarget { File = "Tekken Tag Tournament 2.exe", SearchSubfolders = true };

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), entry);

                Assert.Null(plan.Play);
                Assert.True(plan.PlayMissing);
                Assert.Equal(installer, Assert.Single(plan.Others).Path);
            }
        }

        [Fact]
        public void Plan_PlayMissing_UntilTheGameIsBuilt_OrAProgramIsChosen()
        {
            using (var temp = new TempDirectory())
            {
                var launcher = temp.WriteFile(@"Game\lanceur.exe");
                var dir = temp.Combine("Game");
                var entry = Entry(@"recomp\build\full\recomp_main.exe", null, new ExecutableTarget { Name = "Setup", File = "lanceur.exe" });

                var notBuilt = ExecutablePlanner.Plan(dir, entry);
                Assert.True(notBuilt.PlayMissing);
                Assert.Equal(launcher, notBuilt.Play.Path);
                Assert.False(ExecutablePlanner.Plan(dir, entry, "lanceur.exe").PlayMissing);

                var game = temp.WriteFile(@"Game\recomp\build\full\recomp_main.exe");
                var built = ExecutablePlanner.Plan(dir, entry);
                Assert.False(built.PlayMissing);
                Assert.Equal(game, built.Play.Path);

                // Without a play program in the entry nothing tells a missing game apart.
                Assert.False(ExecutablePlanner.Plan(dir, null).PlayMissing);
            }
        }

        [Fact]
        public void GamesDir_IsTheGamesFolderNextToTheApps_ForPlayValuesAndPending()
        {
            using (var temp = new TempDirectory())
            {
                var compiler = temp.WriteFile(@"Quiver\Apps\HW\HWAoI-Compiler.exe");
                var dir = temp.Combine(@"Quiver\Apps\HW");
                var games = temp.Combine(@"Quiver\games");
                var entry = Entry(@"{GamesDir}\HWAoI-Game\work\hwaoi.exe");
                entry.Play.Environment = new List<EnvironmentVariable> { new EnvironmentVariable { Name = "HWAOI_EXEFS", Value = @"{GamesDir}\HWAoI-Game\exefs" } };

                Assert.Equal(games, ExecutablePlanner.GamesDir(dir));
                var notThere = ExecutablePlanner.Plan(dir, entry);
                Assert.True(notThere.PlayMissing);
                Assert.Equal(compiler, notThere.Play.Path);

                var game = temp.WriteFile(@"Quiver\games\HWAoI-Game\work\hwaoi.exe");
                var plan = ExecutablePlanner.Plan(dir, entry);
                Assert.False(plan.PlayMissing);
                Assert.Equal(game, plan.Play.Path);
                Assert.Equal(games + @"\HWAoI-Game\exefs", plan.Play.Environment["HWAOI_EXEFS"]);
                Assert.False(ExecutablePlanner.IsSetupPending(dir, plan, new SetupInfo { Built = @"{GamesDir}\HWAoI-Game\work" }));
                Assert.True(ExecutablePlanner.IsSetupPending(dir, plan, new SetupInfo { Built = @"{GamesDir}\Other" }));
            }
        }

        [Fact]
        public void GamesDir_WithAWildcardFolder_FindsTheInstalledGameAndItsSaves()
        {
            using (var temp = new TempDirectory())
            {
                var setup = temp.WriteFile(@"Quiver\Apps\PDZ\PDZ Setup.exe");
                var dir = temp.Combine(@"Quiver\Apps\PDZ");
                var entry = Entry(@"{GamesDir}\*\PDZReinstatedTU3.exe");
                var userData = new UserDataInfo { Portable = "builtin", Local = "user" };

                Assert.True(entry.Play.IsInstalledGame);
                Assert.False(Entry("game.exe").Play.IsInstalledGame);
                var notThere = ExecutablePlanner.Plan(dir, entry);
                Assert.True(notThere.PlayMissing);
                Assert.Equal(setup, notThere.Play.Path);

                temp.WriteFile(@"Quiver\games\HWAoI-Game\hwaoi.exe");
                var game = temp.WriteFile(@"Quiver\games\Perfect Dark Zero Reinstated\PDZReinstatedTU3.exe");
                var plan = ExecutablePlanner.Plan(dir, entry);
                Assert.False(plan.PlayMissing);
                Assert.Equal(game, plan.Play.Path);

                var saves = Assert.Single(QuiverSaves.Find(dir, plan.Play.Path, userData, entry.Play.IsInstalledGame));
                Assert.Equal(temp.Combine(@"Quiver\games\Perfect Dark Zero Reinstated\user"), saves.Path);
            }
        }

        [Fact]
        public void Plan_PlayHideConsole_ReachesTheTarget()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\hwaoi.exe");
                var entry = Entry("hwaoi.exe");
                entry.Play.HideConsole = true;

                Assert.True(ExecutablePlanner.Plan(temp.Combine("Game"), entry).Play.HideConsole);
                Assert.False(ExecutablePlanner.Plan(temp.Combine("Game"), Entry("hwaoi.exe")).Play.HideConsole);
            }
        }

        [Fact]
        public void Plan_PlayLaunch_StartsTheEngineButKeepsTheLauncherAsTheGame()
        {
            using (var temp = new TempDirectory())
            {
                var launcher = temp.WriteFile(@"Game\game\tekken6.exe");
                var entry = Entry("tekken6.exe", "--user_data_root=\"{ExeDir}\\user\"");
                entry.Play.SearchSubfolders = true;
                entry.Play.Launch = @"bin\tekken6.exe";
                entry.Play.Borderless = true;
                entry.Play.Environment = new List<EnvironmentVariable> { new EnvironmentVariable { Name = "REX_USER_LANGUAGE", Value = ExecutablePlanner.XboxLanguageVariable } };

                // Engine not there: the launcher as it is.
                var plain = ExecutablePlanner.Plan(temp.Combine("Game"), entry).Play;
                Assert.Equal(launcher, plain.Path);
                Assert.Null(plain.Program);
                Assert.Null(plain.Arguments);
                Assert.Empty(plain.Environment);
                Assert.False(plain.Borderless);

                var engine = temp.WriteFile(@"Game\game\bin\tekken6.exe");
                var play = ExecutablePlanner.Plan(temp.Combine("Game"), entry).Play;
                Assert.Equal(launcher, play.Path);
                Assert.Equal(engine, play.Program);
                Assert.Equal(engine, play.StartPath);
                Assert.Equal("--user_data_root=\"" + temp.Combine(@"Game\game\user") + "\"", play.Arguments);
                Assert.True(play.Borderless);
                Assert.Equal("5", ExecutablePlanner.ReplaceVariable(play.Environment["REX_USER_LANGUAGE"], "{xboxlanguage}", "5"));
            }
        }

        [Fact]
        public void Tekken6_PlaysTheEngineFromTheGameFolder()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\Tekken 6 Builder.exe");
                var launcher = temp.WriteFile(@"Game\game\tekken6.exe");
                var engine = temp.WriteFile(@"Game\game\bin\tekken6.exe");
                var entry = ExecutableDatabase.Load(null).Find(new QuiverApp { Name = "Tekken 6", Repository = "mahmoud-mess/tekken6-recomp" });

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), entry);
                Assert.Equal(launcher, plan.Play.Path);
                Assert.Equal(engine, plan.Play.Program);
                Assert.Contains("--game_data_root=\"" + temp.Combine(@"Game\game\game") + "\"", plan.Play.Arguments);
                Assert.True(plan.Play.Borderless);
                Assert.Contains(plan.Others, o => o.Name == "Launcher" && o.Path == launcher);
            }
        }

        [Fact]
        public void IsSetupPending_UntilTheBuiltFileOrFolderExists()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\DOA2U.exe");
                var dir = temp.Combine("Game");
                var entry = Entry("DOA2U.exe");
                var plan = ExecutablePlanner.Plan(dir, entry);
                var file = new SetupInfo { Kind = SetupInfo.Builder, Built = @"build\*\runner.exe" };
                var folder = new SetupInfo { Built = "assets" };

                Assert.False(plan.PlayMissing);
                Assert.True(ExecutablePlanner.IsSetupPending(dir, plan, file));
                Assert.True(ExecutablePlanner.IsSetupPending(dir, plan, folder));
                Assert.False(ExecutablePlanner.IsSetupPending(dir, plan, null));
                Assert.False(ExecutablePlanner.IsSetupPending(dir, plan, file, "DOA2U.exe"));

                temp.WriteFile(@"Game\build\mingw\runner.exe");
                temp.WriteFile(@"Game\assets\default.xex");
                Assert.False(ExecutablePlanner.IsSetupPending(dir, plan, file));
                Assert.False(ExecutablePlanner.IsSetupPending(dir, plan, folder));
            }
        }

        [Fact]
        public void Plan_ChosenProgram_ReplacesPlay_InsideOrOutsideTheAppFolder()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\setup.exe");
                var inside = temp.WriteFile(@"Game\game\Game.exe");
                var outside = temp.WriteFile(@"Elsewhere\Game.exe");
                var entry = Entry(null, null, new ExecutableTarget { Name = "Setup", File = "setup.exe" });
                entry.Play = new ExecutableTarget { File = "Game.exe", SearchSubfolders = true };
                var dir = temp.Combine("Game");

                Assert.Equal(@"game\Game.exe", ExecutablePlanner.StoredChosenPath(dir, inside));
                Assert.Equal(outside, ExecutablePlanner.StoredChosenPath(dir, outside));
                Assert.Equal(outside, ExecutablePlanner.Plan(dir, entry, outside).Play.Path);
                Assert.Equal(inside, ExecutablePlanner.Plan(dir, entry, @"game\Game.exe").Play.Path);
                // A chosen program that is gone leaves the database's choice.
                Assert.Equal(inside, ExecutablePlanner.Plan(dir, entry, temp.Combine("Gone.exe")).Play.Path);

                var chosenSetup = ExecutablePlanner.Plan(dir, entry, "setup.exe");
                Assert.Equal(temp.Combine(@"Game\setup.exe"), chosenSetup.Play.Path);
                Assert.Empty(chosenSetup.Others);
            }
        }

        [Fact]
        public void Plan_WithoutEntry_OffersNoToolchain()
        {
            using (var temp = new TempDirectory())
            {
                for (var i = 0; i < ExecutablePlanner.MaxAutomaticOthers + 1; i++)
                {
                    temp.WriteFile($@"Game\toolchain\bin\tool{i}.exe");
                }

                Assert.Empty(ExecutablePlanner.Plan(temp.Combine("Game"), null).Others);
            }
        }

        [Fact]
        public void Plan_BundledNebulaEntry_PlaysTheHighestVersion()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\versions\1.0.0\Nebula.exe");
                var play = temp.WriteFile(@"Game\versions\1.0.10\Nebula.exe");
                var settings = temp.WriteFile(@"Game\versions\1.0.10\NebulaSettings.exe");
                temp.WriteFile(@"Game\versions\1.0.9\NebulaSettings.exe");
                var setup = temp.WriteFile(@"Game\staging\nebula-recomp-1.0.10.exe");
                temp.WriteFile(@"Game\toolchain\abc\cmake\bin\cmake.exe");
                var app = new QuiverApp { Name = "Super Mario Galaxy", FolderName = "SuperMarioGalaxy-Nebula", Repository = "keepvaibin/nebula" };

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), ExecutableDatabase.Load(null).Find(app));

                Assert.Equal(play, plan.Play.Path);
                Assert.Equal(new[] { settings, setup }, plan.Others.Select(o => o.Path));
            }
        }

        [Fact]
        public void FindInSubfolders_ShallowestWins_AndRejectsPaths()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\a\b\c\game.exe");
                var shallow = temp.WriteFile(@"Game\z\game.exe");

                Assert.Equal(shallow, ExecutablePlanner.FindInSubfolders(temp.Combine("Game"), "game.exe"));
                Assert.Equal(shallow, ExecutablePlanner.FindInSubfolders(temp.Combine("Game"), "GAME.*"));
                Assert.Null(ExecutablePlanner.FindInSubfolders(temp.Combine("Game"), @"z\game.exe"));
                Assert.Null(ExecutablePlanner.FindInSubfolders(temp.Combine("Missing"), "game.exe"));
            }
        }

        [Fact]
        public void FindFile_WildcardMatchesTheLongNameExactly()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\App-1.exe_old");

                Assert.Null(ExecutablePlanner.FindFile(temp.Combine("Game"), "App-*.exe"));
            }
        }

        [Fact]
        public void FindFile_WildcardFolder_HighestVersionWithTheFileWins()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\versions\0.9.0\run\game.exe");
                var found = temp.WriteFile(@"Game\versions\0.10.0\run\game.exe");
                temp.WriteFile(@"Game\versions\0.11.0\payload\game.exe");

                Assert.Equal(found, ExecutablePlanner.FindFile(temp.Combine("Game"), @"versions\*\run\game.exe"));
                Assert.Equal(found, ExecutablePlanner.FindFile(temp.Combine("Game"), "versions/0.*/run/GAME.exe"), ignoreCase: true);
                Assert.Null(ExecutablePlanner.FindFile(temp.Combine("Game"), @"versions\*\bin\game.exe"));
            }
        }

        [Fact]
        public void Plan_BundledCrashOfTheTitansEntry_RunsTheRuntimeWithItsLauncherEnvironment()
        {
            using (var temp = new TempDirectory())
            {
                var dir = temp.Combine("Game");
                var launcher = temp.WriteFile(@"Game\MojoRecomp-Launcher-1.1.0-windows-x64.exe");
                temp.WriteFile(@"Game\games\.mojorecomp\components\runtime.cot\versions\0.2.0-alpha\payload\cot-runtime.exe");
                var runtime = temp.WriteFile(@"Game\games\.mojorecomp\components\runtime.cot\versions\0.2.0-alpha\run\cot-runtime.exe");
                var runDir = Path.GetDirectoryName(runtime);
                var app = new QuiverApp { Name = "Crash of the Titans", FolderName = "CrashOfTheTitans-MojoRecomp", Repository = "OAleex/MojoRecomp" };
                var localAppData = System.Environment.GetEnvironmentVariable("LOCALAPPDATA");

                var plan = ExecutablePlanner.Plan(dir, ExecutableDatabase.Load(null).Find(app));

                Assert.Equal(runtime, plan.Play.Path);
                Assert.Equal($"--cpu --config \"{localAppData}\\MojoRecomp\\titles\\cot\\settings.toml\" --game-root \"{dir}\\games\\cot\"", plan.Play.Arguments);
                Assert.Equal(System.Environment.GetEnvironmentVariable("USERPROFILE") + @"\Saved Games\MojoRecomp\cot", plan.Play.Environment["MOJORECOMP_SAVE_ROOT"]);
                Assert.Equal(runDir + @"\dxcompiler.dll", plan.Play.Environment["MOJORECOMP_DXC_LIB"]);
                Assert.StartsWith(runDir + ";", plan.Play.Environment["PATH"]);
                Assert.DoesNotContain("%", plan.Play.Environment["PATH"]);
                var setup = Assert.Single(plan.Others);
                Assert.Equal(launcher, setup.Path);
                Assert.Empty(setup.Environment);
            }
        }

        [Fact]
        public void Plan_CrashOfTheTitansNotSetUpInItsLauncher_PlaysTheLauncher()
        {
            using (var temp = new TempDirectory())
            {
                var launcher = temp.WriteFile(@"Game\MojoRecomp-Launcher-1.1.0-windows-x64.exe");
                var app = new QuiverApp { Name = "Crash of the Titans", FolderName = "CrashOfTheTitans-MojoRecomp", Repository = "OAleex/MojoRecomp" };

                var plan = ExecutablePlanner.Plan(temp.Combine("Game"), ExecutableDatabase.Load(null).Find(app));

                Assert.Equal(launcher, plan.Play.Path);
                Assert.Empty(plan.Play.Environment);
            }
        }

        [Fact]
        public void ExpandVariables_ReplacesFoldersAndEnvironmentVariables()
        {
            Assert.Equal(@"C:\A\games x C:\A\bin$1", ExecutablePlanner.ExpandVariables(@"{AppDir}\games x {exedir}$1", @"C:\A\", @"C:\A\bin\x.exe"));
            Assert.Equal(System.Environment.GetEnvironmentVariable("WINDIR") + @"\x", ExecutablePlanner.ExpandVariables(@"%WINDIR%\x", @"C:\A", null));
            Assert.Null(ExecutablePlanner.ExpandVariables(null, @"C:\A", null));
        }

        [Fact]
        public void RecordDetected_WritesUnreviewedEntry_ThatRoundTrips()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\New\new.exe");
                temp.WriteFile(@"Apps\New\new_launcher.exe");
                var app = new QuiverApp { Name = "New Game", FolderName = "New", Repository = "someone/new" };
                var localPath = temp.Combine("data", ExecutableDatabase.LocalFileName);
                var db = new ExecutableDatabase(null, localPath);

                Assert.True(db.RecordDetected(Installed(app, temp.Combine("Apps", "New"))));
                Assert.False(db.RecordDetected(Installed(app, temp.Combine("Apps", "New"))));
                db.SaveLocal();

                var saved = new ExecutableDatabase(null, localPath).LocalEntries.Single();
                Assert.Equal("New", saved.FolderName);
                Assert.Equal("someone/new", saved.Repository);
                Assert.False(saved.Reviewed);
                Assert.Equal("new.exe", saved.Play.File);
                Assert.Equal("Setup", saved.Others.Single().Name);
                Assert.Equal("new_launcher.exe", saved.Others.Single().File);
                // Unreviewed entries are a record only, never used to launch.
                Assert.Null(new ExecutableDatabase(null, localPath).Find(app));
            }
        }

        [Fact]
        public void RecordDetected_SkipsAppsWithOneProgramOrAnEntry_AndDropsObsoleteOnes()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Single\game.exe");
                temp.WriteFile(@"Apps\Single\unins000.exe");
                temp.WriteFile(@"Apps\Sms\a.exe");
                temp.WriteFile(@"Apps\Sms\b.exe");
                var single = new QuiverApp { FolderName = "Single" };
                var db = new ExecutableDatabase(new[] { new ExecutableEntry { Repository = Sms.Repository, Play = new ExecutableTarget { File = "a.exe" }, Reviewed = true } }, null);

                Assert.False(db.RecordDetected(Installed(single, temp.Combine("Apps", "Single"))));
                Assert.False(db.RecordDetected(Installed(Sms, temp.Combine("Apps", "Sms"))));
                Assert.Empty(db.LocalEntries);
            }
        }

        [Fact]
        public void ReviewedLocalEntry_WinsOverBundled_AndIsNeverRewritten()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\Sms\a.exe");
                temp.WriteFile(@"Apps\Sms\b.exe");
                var localPath = temp.WriteFile(ExecutableDatabase.LocalFileName,
                    "{\"apps\":[{\"folderName\":\"SuperMarioSunshine-SMSLauncher\",\"play\":{\"file\":\"b.exe\"},\"reviewed\":true}]}");
                var db = ExecutableDatabase.Load(localPath);

                Assert.Equal("b.exe", db.Find(Sms).Play.File);
                Assert.False(db.RecordDetected(Installed(Sms, temp.Combine("Apps", "Sms"))));
            }
        }

        [Fact]
        public void RecordDetected_RemovesEntryWhenTheAppNoLongerHasSeveralPrograms()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Apps\New\new.exe");
                var extra = temp.WriteFile(@"Apps\New\extra.exe");
                var app = new QuiverApp { FolderName = "New" };
                var db = new ExecutableDatabase(null, null);
                Assert.True(db.RecordDetected(Installed(app, temp.Combine("Apps", "New"))));

                File.Delete(extra);

                Assert.True(db.RecordDetected(Installed(app, temp.Combine("Apps", "New"))));
                Assert.Empty(db.LocalEntries);
            }
        }

        [Fact]
        public void UserDataOnlyEntry_LeavesTheProgramsToTheAutomaticChoice()
        {
            using (var temp = new TempDirectory())
            {
                var game = temp.WriteFile(@"Apps\Banjo\BanjoRecompiled.exe");
                temp.WriteFile(@"Apps\Banjo\Banjo_Launcher.exe");
                var banjo = new QuiverApp { Name = "Banjo-Kazooie", FolderName = "Banjo", Repository = "BanjoRecomp/BanjoRecomp" };
                var db = ExecutableDatabase.Load(null);

                Assert.Null(db.Find(banjo));
                var plan = ExecutablePlanner.Plan(temp.Combine("Apps", "Banjo"), new ExecutableEntry { UserData = db.FindUserData(banjo) });
                Assert.Equal(game, plan.Play.Path);
                Assert.Equal("Setup", plan.Others.Single().Name);
                // Its several programs are still recorded for review.
                Assert.True(db.RecordDetected(Installed(banjo, temp.Combine("Apps", "Banjo"))));

                var userData = db.FindUserData(banjo);
                Assert.Equal(UserDataInfo.DefaultMarker, userData.Marker);
                Assert.Equal(@"%LOCALAPPDATA%\BanjoRecompiled", userData.AppData);
                Assert.Equal("", userData.Local);
                Assert.Equal("saves", userData.Saves);
            }
        }

        [Fact]
        public void FindUserData_ReviewedLocalEntryWins_AndALocalEntryWithoutItKeepsTheBundledOne()
        {
            using (var temp = new TempDirectory())
            {
                var localPath = temp.WriteFile(ExecutableDatabase.LocalFileName,
                    "{\"apps\":[" +
                    "{\"folderName\":\"SuperMarioSunshine-SMSLauncher\",\"play\":{\"file\":\"b.exe\"},\"reviewed\":true}," +
                    "{\"repository\":\"BanjoRecomp/BanjoRecomp\",\"reviewed\":true,\"userData\":{\"portable\":\"none\",\"appData\":\"C:\\\\Elsewhere\"}}," +
                    "{\"repository\":\"sonicdcer/Starfox64Recomp\",\"reviewed\":false,\"userData\":{\"portable\":\"none\"}}]}");
                var db = ExecutableDatabase.Load(localPath);

                Assert.Equal("b.exe", db.Find(Sms).Play.File);
                Assert.True(db.FindUserData(Sms).IsBuiltin);
                var banjo = db.FindUserData(new QuiverApp { Repository = "BanjoRecomp/BanjoRecomp" });
                Assert.True(banjo.IsUnsupported);
                Assert.Null(banjo.Marker);
                Assert.Equal(@"C:\Elsewhere", banjo.AppData);
                Assert.Equal(UserDataInfo.DefaultMarker, db.FindUserData(new QuiverApp { Repository = "sonicdcer/Starfox64Recomp" }).Marker);
                Assert.Null(db.FindUserData(new QuiverApp { Repository = "someone/unknown" }));
            }
        }

        [Fact]
        public void FindAchievements_ReadsTheBundledEntries_AndAnAchievementsOnlyEntryHasNoPrograms()
        {
            var db = ExecutableDatabase.Load(null);

            var dusklight = db.FindAchievements(new QuiverApp { Repository = "TwilitRealm/dusklight" });
            Assert.Equal(AchievementsInfo.Custom, dusklight.Kind);
            Assert.Equal(@"data\achievements.json", dusklight.File);
            var bloodborne = db.FindAchievements(new QuiverApp { Repository = "Supermedo/bloodborne_pc" });
            Assert.Equal(AchievementsInfo.Ps4Trophy, bloodborne.Kind);
            Assert.Equal(@"CUSA00207\trophies.txt", bloodborne.File);
            Assert.Equal(0, bloodborne.RetroAchievementsId);
            var minishCap = db.FindAchievements(new QuiverApp { Repository = "999sian/tmc" });
            Assert.Equal(AchievementsInfo.RetroAchievements, minishCap.Kind);
            Assert.Equal(559, minishCap.RetroAchievementsId);
            var renut = new QuiverApp { Repository = "masterspike52/reNut" };
            Assert.Equal(AchievementsInfo.ReXGlue, db.FindAchievements(renut).Kind);
            Assert.Null(db.Find(renut));
            Assert.Null(db.FindUserData(renut));
            Assert.Null(db.FindAchievements(new QuiverApp { Repository = "someone/unknown" }));
        }

        [Fact]
        public void FindSetup_MarksTheInstallers_AndASetupOnlyEntryHasNoProgramsOrData()
        {
            var db = ExecutableDatabase.Load(null);

            Assert.True(db.FindSetup(new QuiverApp { Repository = "mahmoud-mess/tekken6-recomp" }).IsInstaller);
            Assert.True(db.FindSetup(new QuiverApp { Repository = "2023PerfectDark/Perfect-Dark-Zero-Reinstated" }).IsInstaller);
            var sfa3 = new QuiverApp { Repository = "irnutsmurt/Street-Alpha-3-Dreamcast-Recomp" };
            Assert.True(db.FindSetup(sfa3).IsInstaller);
            Assert.False(db.FindSetup(sfa3).IsBuilder);
            var xcx = new QuiverApp { Repository = "jmars125/xcx-recomp" };
            Assert.True(db.FindSetup(xcx).IsInstaller);
            Assert.Null(db.Find(xcx));
            Assert.Null(db.FindUserData(xcx));
            Assert.Null(db.FindAchievements(xcx));
            Assert.Equal("--update \"{GameDir}\"", db.FindSetup(sfa3).Update.Arguments);
            Assert.Equal("SFA3-Setup.exe", db.FindSetup(sfa3).Install.File);
            Assert.False(string.IsNullOrWhiteSpace(db.FindSetup(sfa3).Notes));
            var ttt2 = db.FindSetup(new QuiverApp { Repository = "shihabal3amri/tekken-tag-tournament-2-pc" });
            Assert.EndsWith("\"{GameDir}\"", ttt2.Update.Arguments);
            Assert.Null(db.FindSetup(new QuiverApp { Repository = "Rotmill/Dino-Crisis-Modern-Edition" }).Update);
            Assert.StartsWith("/silent ", ttt2.Install.Arguments);
            Assert.Null(db.FindSetup(new QuiverApp { Repository = "masterspike52/reNut" }));
            Assert.Null(db.FindSetup(new QuiverApp { Repository = "someone/unknown" }));
        }

        [Fact]
        public void UserData_CustomMarkerAndEmptyLocal_AreRead()
        {
            using (var temp = new TempDirectory())
            {
                var localPath = temp.WriteFile(ExecutableDatabase.LocalFileName,
                    "{\"apps\":[{\"repository\":\"a/b\",\"reviewed\":true,\"userData\":{\"portable\":\"portable.ini\",\"appData\":\"%APPDATA%\\\\B\",\"local\":\"\"}}]}");

                var userData = new ExecutableDatabase(null, localPath).FindUserData(new QuiverApp { Repository = "a/b" });

                Assert.Equal("portable.ini", userData.Marker);
                Assert.Equal(@"%APPDATA%\B", userData.AppData);
                Assert.Equal("", userData.Local);
                Assert.Null(userData.Saves);
                Assert.False(userData.IsBuiltin);
            }
        }

        [Fact]
        public void NaturalComparer_OrdersVersionsByValue()
        {
            var names = new[] { "v0.1.60", "v0.1.9", "v0.1.57", "v0.2.0" };

            Assert.Equal(new[] { "v0.1.9", "v0.1.57", "v0.1.60", "v0.2.0" }, names.OrderBy(n => n, ExecutablePlanner.NaturalComparer.Instance));
        }
    }
}
