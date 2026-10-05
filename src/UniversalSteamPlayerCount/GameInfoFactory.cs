using System.Linq;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public static class GameInfoFactory
    {
        public static GameInfo From(Game game)
        {
            var links = game.Links == null ? null : game.Links.Where(l => l != null).Select(l => l.Url).ToList();
            return new GameInfo(game.Id, game.Name, game.PluginId, game.GameId, links);
        }
    }
}
