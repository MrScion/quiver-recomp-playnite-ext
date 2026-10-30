using System;
using System.Linq;
using QuiverLibrary.Core.Achievements;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class AchievementsTests
    {
        // As the Bloodborne port writes it (trimmed to three trophies).
        private const string Metadata = @"{
  ""complete"": true,
  ""entries"": [
    { ""description"": ""All trophies acquired. Hats off!"", ""grade"": ""P"", ""hidden"": false, ""id"": 0, ""name"": ""Bloodborne"", ""score"": 100 },
    { ""description"": ""Captivated by the Moon Presence,\nyou pledge to watch over the Hunter's Dream."", ""grade"": ""G"", ""hidden"": false, ""id"": 2, ""name"": ""Honoring Wishes"", ""score"": 60 },
    { ""description"": ""Defeat Gascoigne, the beast."", ""grade"": ""B"", ""hidden"": true, ""id"": 29, ""name"": ""Father Gascoigne, the Hunter"", ""score"": 15 }
  ],
  ""language"": ""en"",
  ""version"": 1
}";

        [Fact]
        public void Ps4Trophies_EveryTrophyFromTheMetadata_UnlockedOnesWithTheirLocalTimeAndIcon()
        {
            using (var temp = new TempDirectory())
            {
                var unlockedFile = temp.WriteFile(@"App\CUSA00207\trophies.txt", "29 1792346213\n", false);
                temp.WriteFile(@"App\CUSA00207\trophy_cache\metadata.json", Metadata, false);
                var icon = temp.WriteFile(@"App\CUSA00207\trophy_cache\icons\TROP029.PNG");

                var trophies = Ps4Trophies.Read(unlockedFile);

                Assert.Equal(new[] { "0", "2", "29" }, trophies.Select(t => t.Id));
                var gascoigne = trophies[2];
                Assert.Equal("Father Gascoigne, the Hunter", gascoigne.Name);
                Assert.Equal("bronze", gascoigne.TrophyType);
                Assert.True(gascoigne.Hidden);
                Assert.True(gascoigne.Unlocked);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792346213).LocalDateTime, gascoigne.UnlockTime);
                Assert.Equal(icon, gascoigne.IconPath);
                Assert.Equal("platinum", trophies[0].TrophyType);
                Assert.False(trophies[0].Unlocked);
                Assert.Null(trophies[0].UnlockTime);
                Assert.Null(trophies[0].IconPath);
            }
        }

        [Fact]
        public void Ps4Trophies_WithoutMetadata_OnlyTheUnlockedIds()
        {
            using (var temp = new TempDirectory())
            {
                var unlockedFile = temp.WriteFile(@"CUSA00207\trophies.txt", "5 0\nnot a line\n7 1792346213\n", false);

                var trophies = Ps4Trophies.Read(unlockedFile);

                Assert.Equal(new[] { "5", "7" }, trophies.Select(t => t.Id));
                Assert.All(trophies, t => Assert.True(t.Unlocked));
                Assert.Null(trophies[0].UnlockTime);
                Assert.Equal("Trophy 5", trophies[0].Name);
            }
        }

        [Fact]
        public void RecompAchievements_FindsTheFirstExistingPlace_AndReadsOnlyKnownFormats()
        {
            using (var temp = new TempDirectory())
            {
                var app = temp.Combine("App");
                var appData = temp.WriteFile(@"AppData\save\ACH-DATA");
                Environment.SetEnvironmentVariable("QUIVER_TEST_ACH", temp.Combine("AppData"));
                try
                {
                    var unleashed = new AchievementsInfo { Kind = AchievementsInfo.Custom, File = @"save\ACH-DATA;%QUIVER_TEST_ACH%\save\ACH-DATA" };
                    Assert.Equal(appData, RecompAchievements.FindFile(app, unleashed));
                    var portable = temp.WriteFile(@"App\save\ACH-DATA");
                    Assert.Equal(portable, RecompAchievements.FindFile(app, unleashed));
                    Assert.False(RecompAchievements.CanRead(unleashed));
                    Assert.Null(RecompAchievements.Read(app, unleashed));
                }
                finally
                {
                    Environment.SetEnvironmentVariable("QUIVER_TEST_ACH", null);
                }

                var bloodborne = new AchievementsInfo { Kind = AchievementsInfo.Ps4Trophy, File = @"CUSA00207\trophies.txt" };
                Assert.True(RecompAchievements.CanRead(bloodborne));
                Assert.Null(RecompAchievements.Read(app, bloodborne));
                temp.WriteFile(@"App\CUSA00207\trophies.txt", "1 1792346213\n", false);
                Assert.Equal("1", Assert.Single(RecompAchievements.Read(app, bloodborne)).Id);
            }
        }

        [Fact]
        public void Csv_HeaderAndRows_AsPlayniteAchievementsImportsThem()
        {
            var csv = PlayniteAchievementsCsv.Build(new[]
            {
                new RecompAchievement { Id = "29", Name = "Father Gascoigne, the Hunter", Description = "Say \"hi\"", TrophyType = "bronze", Hidden = true, Unlocked = true, UnlockTime = new DateTime(2026, 10, 18, 19, 56, 53), IconPath = @"C:\icons\TROP029.PNG" },
                new RecompAchievement { Id = "0", Name = "Bloodborne", Description = "Line one\nline two", TrophyType = "platinum" },
            });

            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.Equal(PlayniteAchievementsCsv.Header, lines[0]);
            Assert.Equal("29,\"Father Gascoigne, the Hunter\",\"Say \"\"hi\"\"\",,bronze,true,,,,,true,2026-10-18 19:56:53,C:\\icons\\TROP029.PNG,", lines[1]);
            Assert.Equal("0,Bloodborne,\"Line one\nline two\",,platinum,false,,,,,false,,,", lines[2]);
        }
    }
}
