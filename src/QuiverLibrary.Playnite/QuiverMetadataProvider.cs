using System;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace QuiverLibrary.Playnite
{
    /// <summary>
    /// This library's "Store" metadata source: the same Quiver data the import writes
    /// (icon, links, features, project-type tags), for games Playnite already has.
    /// </summary>
    public class QuiverMetadataProvider : LibraryMetadataProvider
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly QuiverLibraryPlugin _plugin;

        public QuiverMetadataProvider(QuiverLibraryPlugin plugin)
        {
            _plugin = plugin;
        }

        public override GameMetadata GetMetadata(Game game)
        {
            try
            {
                var app = _plugin.GetData().FindApp(game.GameId);
                return app == null ? new GameMetadata() : QuiverLibraryPlugin.DescribeApp(app, _plugin.DisabledFeatures());
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Cannot read Quiver metadata for {game.GameId}.");
                return new GameMetadata();
            }
        }
    }
}
