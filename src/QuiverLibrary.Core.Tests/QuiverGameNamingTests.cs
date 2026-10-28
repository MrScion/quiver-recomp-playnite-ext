using QuiverLibrary.Core;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverGameNamingTests
    {
        [Fact]
        public void BuildNames_RepeatedNamesGetProject_OthersStayPlain()
        {
            var names = QuiverGameNaming.BuildNames(new[]
            {
                new QuiverApp { Name = "Mario Kart 64", FolderName = "SpaghettiKart", Project = "SpaghettiKart" },
                new QuiverApp { Name = "Mario Kart 64", FolderName = "MK64Recomp", Project = "MK64Recomp" },
                new QuiverApp { Name = "Zelda 64", FolderName = "Zelda64Recomp", Project = "Zelda64Recomp" },
            });

            Assert.Equal("Mario Kart 64 (SpaghettiKart)", names["SpaghettiKart"]);
            Assert.Equal("Mario Kart 64 (MK64Recomp)", names["MK64Recomp"]);
            Assert.Equal("Zelda 64", names["Zelda64Recomp"]);
        }

        [Fact]
        public void DisplayName_PrefersCustomName_ThenName_ThenFolder()
        {
            Assert.Equal("Custom", QuiverGameNaming.DisplayName(new QuiverApp { CustomDisplayName = "Custom", Name = "Name", FolderName = "F" }));
            Assert.Equal("Name", QuiverGameNaming.DisplayName(new QuiverApp { Name = "Name", FolderName = "F" }));
            Assert.Equal("F", QuiverGameNaming.DisplayName(new QuiverApp { FolderName = "F" }));
        }

        [Fact]
        public void RepositoryUrl_OnlyForOwnerRepo()
        {
            Assert.Equal("https://github.com/a/b", QuiverGameNaming.RepositoryUrl(new QuiverApp { Repository = "a/b" }));
            Assert.Null(QuiverGameNaming.RepositoryUrl(new QuiverApp { Repository = "https://gitlab.com/a/b" }));
            Assert.Null(QuiverGameNaming.RepositoryUrl(new QuiverApp()));
        }
    }
}
