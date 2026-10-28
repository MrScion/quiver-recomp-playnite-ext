using System.Collections.Generic;
using System.Linq;
using QuiverLibrary.Core.Tests.TestSupport;
using Xunit;

namespace QuiverLibrary.Core.Tests
{
    public class QuiverTagMappingTests
    {
        private static QuiverApp App(params string[] tags) => new QuiverApp { FolderName = "Game", Tags = tags.ToList() };

        [Fact]
        public void FeaturesFor_MapsPortFeatures_IgnoresConsoleAndFranchiseTags()
        {
            var app = App("recomp", "n64", "nintendo 64", "donkey kong", "Widescreen", "keyboard and mouse", "controller support");

            Assert.Equal(new[] { "Keyboard and Mouse", "Controller Support", "Widescreen" }, QuiverTagMapping.FeaturesFor(app));
        }

        [Fact]
        public void FeaturesFor_SkipsDisabledFeatures()
        {
            var app = App("widescreen", "ultrawide", "mod support");

            Assert.Equal(new[] { "Mod Support", "Ultrawide" }, QuiverTagMapping.FeaturesFor(app, new HashSet<string> { "widescreen" }));
        }

        [Fact]
        public void ProjectTypeTagsFor_MergesSynonyms()
        {
            var app = App("recomp", "recompilation", "decomp", "rexglue", "harbour masters", "xbox 360");

            Assert.Equal(new[] { "Recompilation", "Decompilation", "ReXGlue", "Harbour Masters" }, QuiverTagMapping.ProjectTypeTagsFor(app));
        }

        [Fact]
        public void TagsMissing_GiveNothing()
        {
            var app = new QuiverApp { FolderName = "Game" };

            Assert.Empty(QuiverTagMapping.FeaturesFor(app));
            Assert.Empty(QuiverTagMapping.ProjectTypeTagsFor(app));
            Assert.Empty(QuiverTagMapping.ModLinksFor(app));
        }

        [Fact]
        public void ModLinksFor_NamesProvider_SkipsInvalidAndDuplicateUrls()
        {
            var app = new QuiverApp
            {
                FolderName = "Game",
                Mods = new QuiverMods
                {
                    Sources = new List<QuiverModSource>
                    {
                        new QuiverModSource { Provider = "thunderstore", SourceUrl = "https://thunderstore.io/c/spaghetti-kart/" },
                        new QuiverModSource { Provider = "gamebanana", SourceUrl = "https://gamebanana.com/games/22970" },
                        new QuiverModSource { Provider = "somesite", SourceUrl = "https://example.com/mods" },
                        new QuiverModSource { Provider = "thunderstore", SourceUrl = "https://thunderstore.io/c/spaghetti-kart/" },
                        new QuiverModSource { Provider = "bad", SourceUrl = "not a url" },
                        new QuiverModSource { Provider = "file", SourceUrl = "file:///C:/mods" },
                    }
                }
            };

            var links = QuiverTagMapping.ModLinksFor(app);

            Assert.Equal(new[] { "Mods (Thunderstore)", "Mods (GameBanana)", "Mods (Somesite)" }, links.Select(l => l.Key));
            Assert.Equal("https://gamebanana.com/games/22970", links[1].Value);
        }

        [Fact]
        public void LoadApps_ReadsTagsAndMods()
        {
            using (var temp = new TempDirectory())
            {
                temp.WriteFile("apps.json",
                    "{\"apps\":[{\"folderName\":\"G\",\"tags\":[\"widescreen\"],\"mods\":{\"path\":\"mods\",\"layout\":\"folderPerMod\"," +
                    "\"sources\":[{\"provider\":\"thunderstore\",\"sourceUrl\":\"https://thunderstore.io/c/x/\"}]}}]}");

                var app = new QuiverLibraryData(temp.Path).LoadApps().Single();

                Assert.Equal(new[] { "widescreen" }, app.Tags);
                Assert.Equal("https://thunderstore.io/c/x/", app.Mods.Sources.Single().SourceUrl);
            }
        }
    }
}
