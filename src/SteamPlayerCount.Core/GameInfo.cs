using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamPlayerCount.Core
{
    public sealed class GameInfo
    {
        public GameInfo(Guid id, string name, Guid libraryPluginId, string libraryGameId, IEnumerable<string> linkUrls)
        {
            Id = id;
            Name = name;
            LibraryPluginId = libraryPluginId;
            LibraryGameId = libraryGameId;
            LinkUrls = linkUrls == null ? new List<string>() : linkUrls.ToList();
        }

        public Guid Id { get; }
        public string Name { get; }
        public Guid LibraryPluginId { get; }
        public string LibraryGameId { get; }
        public IReadOnlyList<string> LinkUrls { get; }
    }
}
