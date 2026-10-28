using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverInstallationTests
    {
        private static void CreatePortable(TempDirectory temp)
        {
            temp.WriteFile(".portable");
            temp.WriteFile("QuiverLauncher.exe");
            temp.WriteFile("Update.exe");
            temp.WriteFile(@"current\QuiverLauncher.exe");
        }

        [Fact]
        public void FromPath_PortableRoot_UsesCurrentAppForCliAndStubForLauncher()
        {
            using (var temp = new TempDirectory())
            {
                CreatePortable(temp);

                var install = QuiverInstallation.FromPath(temp.Path);

                Assert.NotNull(install);
                Assert.Equal(temp.Path, install.DataRoot);
                Assert.Equal(temp.Combine("current", "QuiverLauncher.exe"), install.CliExe);
                Assert.Equal(temp.Combine("QuiverLauncher.exe"), install.LauncherExe);
            }
        }

        [Fact]
        public void FromPath_CurrentSubfolder_ResolvesToPortableRoot()
        {
            using (var temp = new TempDirectory())
            {
                CreatePortable(temp);

                var install = QuiverInstallation.FromPath(temp.Combine("current"));

                Assert.Equal(temp.Path, install.DataRoot);
            }
        }

        [Fact]
        public void FromPath_ExeFile_ResolvesToPortableRoot()
        {
            using (var temp = new TempDirectory())
            {
                CreatePortable(temp);

                var install = QuiverInstallation.FromPath(temp.Combine("current", "QuiverLauncher.exe"));

                Assert.Equal(temp.Path, install.DataRoot);
            }
        }

        [Fact]
        public void FromPath_UnpackagedFolder_UsesExeBesideData()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile("QuiverLauncher.exe");

                var install = QuiverInstallation.FromPath(temp.Path);

                Assert.Equal(temp.Path, install.DataRoot);
                Assert.Equal(temp.Combine("QuiverLauncher.exe"), install.CliExe);
            }
        }

        [Fact]
        public void FromPath_FolderWithoutQuiver_ReturnsNull()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Null(QuiverInstallation.FromPath(temp.Path));
                Assert.Null(QuiverInstallation.FromPath(temp.Combine("missing")));
                Assert.Null(QuiverInstallation.FromPath(""));
            }
        }

        [Fact]
        public void Detect_ConfiguredFolderWins_AndIsNotReplacedByDefault()
        {
            using (var configured = new TempDirectory())
            using (var localAppData = new TempDirectory())
            {
                localAppData.WriteFile(@"QuiverLauncher\current\QuiverLauncher.exe");

                Assert.Null(QuiverInstallation.Detect(configured.Path, localAppData.Path));
                Assert.Equal(localAppData.Combine("QuiverLauncher"), QuiverInstallation.Detect("", localAppData.Path).DataRoot);
            }
        }
    }
}
