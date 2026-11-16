using System;
using System.IO;
using System.Linq;
using System.Text;
using QuiverLibrary.Core.Achievements;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class ReXGlueAchievementsTests
    {
        // As the ReXGlue runtime writes it (SF3 3rd Strike Online Edition, 2026-11-03).
        private const string Toml = "# Achievement unlock state - managed by ReXGlue runtime\r\n\r\n[unlocked.2]\r\nfiletime = 134382053857774697\r\n\r\n[unlocked.7]\r\n";

        private const string ArcadeInfo = @"<ArcadeInfo xdkVersion=""1.0.0.0"" projectVersion=""1.0.176.0"">
    <TitleInfo ID=""1480657686"" Name=""SF3: Online Edition"" Path=""default.xex"" ImagePath=""Gameicon.png"">
        <AchievementInfo ID=""1"" ImagePath=""Achievement1.png"" />
        <AchievementInfo ID=""2"" ImagePath=""Achievement2.png"" />
        <AchievementInfo ID=""3"" ImagePath=""Achievement3.png"" />
    </TitleInfo>
    <TitleInfo locale=""fr-fr"" Name=""SF3: Online Edition"" />
</ArcadeInfo>";

        [Fact]
        public void ReadUnlocks_ReadsEachTable_AndItsLocalTime()
        {
            using (var temp = new TempDirectory())
            {
                var file = temp.WriteFile(@"data\achievements\58410B16.toml", Toml);

                var unlocks = ReXGlueAchievements.ReadUnlocks(file);

                Assert.Equal(new[] { 2, 7 }, unlocks.Keys.OrderBy(k => k));
                Assert.Equal(DateTime.FromFileTimeUtc(134382053857774697).ToLocalTime(), unlocks[2]);
                Assert.Equal(2026, unlocks[2].Value.Year);
                Assert.Null(unlocks[7]);
            }
        }

        [Fact]
        public void Read_ListsArcadeInfosAchievementsWithIcons_PlusOtherUnlocks()
        {
            using (var temp = new TempDirectory())
            {
                var toml = temp.WriteFile(@"App\data\achievements\58410B16.toml", Toml);
                temp.WriteFile(@"App\assets\Achievement1.png", "png");
                temp.WriteFile(@"App\assets\Achievement2.png", "png");
                // Xbox Live Arcade packages write it in UTF-16 with a byte order mark.
                File.WriteAllText(temp.Combine(@"App\assets\ArcadeInfo.xml"), ArcadeInfo, Encoding.Unicode);

                var achievements = ReXGlueAchievements.Read(toml, temp.Combine("App"), new AchievementReadOptions { PlaceholderName = "Logro {0}" });

                Assert.Equal(new[] { "1", "2", "3", "7" }, achievements.Select(a => a.Id));
                Assert.Equal("Logro 2", achievements[1].Name);
                Assert.True(achievements[1].Unlocked);
                Assert.NotNull(achievements[1].UnlockTime);
                Assert.False(achievements[0].Unlocked);
                Assert.Equal(temp.Combine(@"App\assets\Achievement1.png"), achievements[0].IconPath);
                Assert.Null(achievements[2].IconPath); // listed, but its picture isn't there
                Assert.True(achievements[3].Unlocked);  // unlocked, not in ArcadeInfo.xml
            }
        }

        [Fact]
        public void RecompAchievements_ReadsReXGlue_WithoutArcadeInfo()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\profiles\default\achievements\415608B2.toml", "[unlocked.55]\nfiletime = 134374962935936148\n");
                var info = new AchievementsInfo { Kind = AchievementsInfo.ReXGlue, File = @"profiles\default\achievements\415608B2.toml" };

                Assert.True(RecompAchievements.CanRead(info));
                var achievements = RecompAchievements.Read(temp.Combine("App"), info);

                var only = Assert.Single(achievements);
                Assert.Equal("55", only.Id);
                Assert.Equal("Achievement 55", only.Name);
                Assert.True(only.Unlocked);
                Assert.False(RecompAchievements.CanRead(new AchievementsInfo { Kind = AchievementsInfo.ReXGlue }));

                // The title id isn't always known: a wildcard takes the file that is there.
                var wildcard = new AchievementsInfo { Kind = AchievementsInfo.ReXGlue, File = @"resources\userdata\achievements\*.toml" };
                Assert.Null(RecompAchievements.Read(temp.Combine("App"), wildcard));
                temp.WriteFile(@"App\resources\userdata\achievements\584108A4.toml", "[unlocked.3]\nfiletime = 134374962935936148\n");
                Assert.Equal("3", Assert.Single(RecompAchievements.Read(temp.Combine("App"), wildcard)).Id);
            }
        }
    }
}
