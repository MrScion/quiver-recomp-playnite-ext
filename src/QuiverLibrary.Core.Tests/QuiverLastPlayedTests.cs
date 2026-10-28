using System;
using System.IO;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverLastPlayedTests
    {
        [Fact]
        public void Read_ParsesQuiverFormat()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\LastPlayed.txt", "2026-10-12 19:25:08\r\n");

                Assert.Equal(new DateTime(2026, 10, 12, 19, 25, 8), QuiverLastPlayed.Read(temp.Combine("Game")));
            }
        }

        [Fact]
        public void Read_MissingOrInvalid_IsNull()
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateFolder("Empty");
                temp.WriteFile(@"Bad\LastPlayed.txt", "12/10/2026");

                Assert.Null(QuiverLastPlayed.Read(temp.Combine("Empty")));
                Assert.Null(QuiverLastPlayed.Read(temp.Combine("Bad")));
                Assert.Null(QuiverLastPlayed.Read(temp.Combine("Gone")));
            }
        }

        [Fact]
        public void Read_ExecutableInSubfolder_TakesTheNewestOfBothPlaces()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"Game\LastPlayed.txt", "2026-10-01 10:00:00");
                temp.WriteFile(@"Game\Launcher\LastPlayed.txt", "2026-10-05 10:00:00");
                var exe = temp.WriteFile(@"Game\Launcher\Launcher.exe");

                Assert.Equal(new DateTime(2026, 10, 5, 10, 0, 0), QuiverLastPlayed.Read(temp.Combine("Game"), exe));
            }
        }

        [Fact]
        public void Write_UsesQuiverFormat_AndRoundTrips()
        {
            using (var temp = new TempDirectory())
            {
                var dir = temp.CreateFolder("Game");
                var time = new DateTime(2026, 10, 23, 8, 5, 9, 750);

                Assert.True(QuiverLastPlayed.Write(dir, time));

                Assert.Equal("2026-10-23 08:05:09", File.ReadAllText(Path.Combine(dir, QuiverLastPlayed.FileName)));
                Assert.Equal(QuiverLastPlayed.ToSeconds(time), QuiverLastPlayed.Read(dir));
                Assert.False(QuiverLastPlayed.Write(temp.Combine("Gone"), time));
            }
        }

        [Fact]
        public void Compare_NewestWins_AndIgnoresFractionsOfASecond()
        {
            var t = new DateTime(2026, 10, 23, 8, 5, 9);

            Assert.Equal(LastPlayedSync.UpdatePlaynite, QuiverLastPlayed.Compare(null, t));
            Assert.Equal(LastPlayedSync.UpdatePlaynite, QuiverLastPlayed.Compare(t, t.AddSeconds(1)));
            Assert.Equal(LastPlayedSync.UpdateQuiver, QuiverLastPlayed.Compare(t, null));
            Assert.Equal(LastPlayedSync.UpdateQuiver, QuiverLastPlayed.Compare(t.AddMinutes(1), t));
            Assert.Equal(LastPlayedSync.None, QuiverLastPlayed.Compare(t.AddMilliseconds(900), t));
            Assert.Equal(LastPlayedSync.None, QuiverLastPlayed.Compare(null, null));
        }
    }
}
