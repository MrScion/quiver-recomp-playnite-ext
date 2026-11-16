using System.Collections.Generic;
using System.IO;
using QuiverLibrary.Core.Executables;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class DisplayPlannerTests
    {
        private static readonly DisplayInfo ReXGlue = new DisplayInfo
        {
            NativeHeight = 720,
            IntegerScale = true,
            ScaleArguments = "--draw_resolution_scale_x={Scale} --draw_resolution_scale_y={Scale}",
        };

        private static readonly DisplayInfo WitchTime = new DisplayInfo
        {
            NativeHeight = 900,
            MaxScale = 3,
            ConfigFile = "config\\graphics.ini",
            FullscreenConfig = "window = fullscreen",
            ScaleConfig = "resolution_scale = {Scale}",
        };

        [Theory]
        [InlineData(1080, 2.0)]
        [InlineData(1440, 2.0)]
        [InlineData(2160, 3.0)]
        public void IntegerScaleRoundsHalvesUp(int height, double scale)
        {
            Assert.Equal(scale, DisplayPlanner.Scale(ReXGlue, height));
        }

        [Theory]
        [InlineData(1080, "1.2")]
        [InlineData(1440, "1.6")]
        [InlineData(2160, "2.4")]
        public void DecimalScaleFollowsTheNativeHeight(int height, string scale)
        {
            Assert.Equal(scale, DisplayPlanner.FormatScale(DisplayPlanner.Scale(WitchTime, height).Value));
        }

        [Fact]
        public void ScaleStopsAtTheMaximum()
        {
            var info = new DisplayInfo { NativeHeight = 480, MaxScale = 3, ScaleArguments = "x" };
            Assert.Equal(3.0, DisplayPlanner.Scale(info, 2160));
        }

        [Fact]
        public void DefaultChangesNothing()
        {
            var plan = DisplayPlanner.Plan(WitchTime, new DisplayPreferences(), "--play", "C:\\App", "C:\\App\\witch-time.exe");
            Assert.Equal("--play", plan.Arguments);
            Assert.Null(plan.Config);
            Assert.Null(plan.Scale);
        }

        [Fact]
        public void ArgumentsAreAddedOrReplaced()
        {
            var plan = DisplayPlanner.Plan(ReXGlue, new DisplayPreferences { ResolutionHeight = 1440 }, "--user_language=5", "C:\\App", "C:\\App\\game.exe");
            Assert.Equal("--user_language=5 --draw_resolution_scale_x=2 --draw_resolution_scale_y=2", plan.Arguments);

            var tekken = new DisplayInfo { NativeHeight = 720, IntegerScale = true, ScaleArguments = "--resolution_scale={Scale}" };
            plan = DisplayPlanner.Plan(tekken, new DisplayPreferences { ResolutionHeight = 1440 }, "--fullscreen=false --resolution_scale=3 --log_level=off", "C:\\App", "C:\\App\\game\\tekken6.exe");
            Assert.Equal("--fullscreen=false --resolution_scale=2 --log_level=off", plan.Arguments);
        }

        [Fact]
        public void ConfigLinesAreReplacedKeepingTheFile()
        {
            using (var temp = new TempDirectory())
            {
                var ini = temp.WriteFile("App\\config\\graphics.ini",
                    "; window = windowed | borderless | fullscreen\r\nwindow = windowed\r\n; resolution_scale = 1.0\r\nresolution_scale = 1.0\r\nvsync = on\r\n", bom: false);
                var plan = DisplayPlanner.Plan(WitchTime, new DisplayPreferences { Fullscreen = true, ResolutionHeight = 2160 }, "--play",
                    temp.Combine("App"), temp.Combine("App", "witch-time.exe"));

                Assert.Equal(ini, plan.Config.Path);
                var missing = DisplayPlanner.Apply(plan.Config, out var changed);

                Assert.Empty(missing);
                Assert.True(changed);
                Assert.Equal("; window = windowed | borderless | fullscreen\r\nwindow = fullscreen\r\n; resolution_scale = 1.0\r\nresolution_scale = 2.4\r\nvsync = on\r\n",
                    File.ReadAllText(ini));

                DisplayPlanner.Apply(plan.Config, out changed);
                Assert.False(changed);
            }
        }

        [Fact]
        public void MissingKeysAreReportedNotAdded()
        {
            var text = DisplayPlanner.EditConfigText("[Graphics]\nTargetFPS = 0\n",
                new[] { new KeyValuePair<string, string>("WindowMode", "0") }, out var missing);
            Assert.Equal("[Graphics]\nTargetFPS = 0\n", text);
            Assert.Equal(new[] { "WindowMode" }, missing);
        }

        [Fact]
        public void TheGamesOwnChoiceWins()
        {
            var common = new DisplayPreferences { ResolutionHeight = 1440 };
            var own = new DisplayPreferences { ResolutionHeight = 2160 };
            var perGame = new Dictionary<string, DisplayPreferences> { ["NeverDead-Neverdeth"] = own };

            Assert.Same(own, DisplayPlanner.Choose(common, perGame, "neverdead-neverdeth"));
            Assert.Same(common, DisplayPlanner.Choose(common, perGame, "Other"));
        }

        [Fact]
        public void BundledEntriesDescribeTheirDisplay()
        {
            var db = ExecutableDatabase.Load(null);
            var neverDead = db.Find(new QuiverApp { Name = "NeverDead", Repository = "Katzert/Neverdeth" }).Display;
            var witchTime = db.Find(new QuiverApp { Name = "Bayonetta 3", Repository = "Zexyen/Witch-Time" }).Display;
            var tekken = db.Find(new QuiverApp { Name = "Tekken 6", Repository = "mahmoud-mess/tekken6-recomp" }).Display;

            Assert.True(neverDead.SupportsFullscreen && neverDead.SupportsResolution);
            Assert.True(witchTime.SupportsFullscreen && witchTime.SupportsResolution);
            Assert.True(tekken.SupportsResolution);
            Assert.False(tekken.SupportsFullscreen);
        }
    }
}
