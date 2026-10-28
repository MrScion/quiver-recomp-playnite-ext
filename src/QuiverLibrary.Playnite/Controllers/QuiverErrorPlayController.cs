using System;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace QuiverLibrary.Playnite.Controllers
{
    /// <summary>Reports why a game can't be started, through Playnite's own error dialog.</summary>
    public class QuiverErrorPlayController : PlayController
    {
        private readonly string _message;

        public QuiverErrorPlayController(Game game, string message) : base(game)
        {
            _message = message;
            Name = QuiverLibraryPlugin.Loc("LOCQuiverPlayActionName");
        }

        public override void Play(PlayActionArgs args)
        {
            throw new Exception(_message);
        }
    }
}
