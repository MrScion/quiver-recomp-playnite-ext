using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverSetupUpdatesTests
    {
        [Fact]
        public void Fingerprint_JoinsTheVersionAndTheDownloadHash()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile(@"App\installed-build.txt", "SFA3-Setup-windows-x86_64.zip\r\nb32bcaef\r\n\r\n");
                temp.CreateFolder("Empty");

                Assert.Equal("v0.2.13|b32bcaef", QuiverSetupUpdates.Fingerprint(temp.Combine("App"), " v0.2.13 "));
                Assert.Equal("|b32bcaef", QuiverSetupUpdates.Fingerprint(temp.Combine("App"), null));
                Assert.Equal("v1|", QuiverSetupUpdates.Fingerprint(temp.Combine("Empty"), "v1"));
                Assert.Null(QuiverSetupUpdates.Fingerprint(temp.Combine("Empty"), null));
                Assert.Null(QuiverSetupUpdates.Fingerprint(null, " "));
            }
        }

        [Fact]
        public void Describe_ShowsTheVersion_OrTheStartOfTheHash()
        {
            Assert.Equal("v0.2.13", QuiverSetupUpdates.Describe("v0.2.13|b32bcaeff99d"));
            Assert.Equal("b32bcaef", QuiverSetupUpdates.Describe("|b32bcaeff99d"));
            Assert.Equal("v1", QuiverSetupUpdates.Describe("v1|"));
            Assert.Null(QuiverSetupUpdates.Describe("|"));
            Assert.Null(QuiverSetupUpdates.Describe(null));
        }

        [Fact]
        public void Compare_RemembersTheFirstDownload_AndWarnsOnlyWhenItChanges()
        {
            Assert.Equal(SetupUpdateCheck.FirstSeen, QuiverSetupUpdates.Compare(null, "v1|a"));
            Assert.Equal(SetupUpdateCheck.Unchanged, QuiverSetupUpdates.Compare("v1|a", "V1|A"));
            Assert.Equal(SetupUpdateCheck.Updated, QuiverSetupUpdates.Compare("v1|a", "v2|b"));
            Assert.Equal(SetupUpdateCheck.Updated, QuiverSetupUpdates.Compare("v1|a", "v1|b"));
            Assert.Equal(SetupUpdateCheck.Unchanged, QuiverSetupUpdates.Compare("v1|a", null));
        }
    }
}
