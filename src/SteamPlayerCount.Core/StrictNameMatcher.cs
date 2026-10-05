using System;
using System.Collections.Generic;

namespace SteamPlayerCount.Core
{
    public static class StrictNameMatcher
    {
        public const string AppType = "app";

        public static SteamCandidate FindMatch(string gameName, IEnumerable<SteamCandidate> candidates)
        {
            var wanted = GameNameNormalizer.Normalize(gameName);
            if (wanted.Length == 0 || candidates == null)
            {
                return null;
            }

            SteamCandidate found = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null ||
                    !string.Equals(candidate.Type, AppType, StringComparison.OrdinalIgnoreCase) ||
                    GameNameNormalizer.Normalize(candidate.Name) != wanted)
                {
                    continue;
                }

                if (found != null && found.AppId != candidate.AppId)
                {
                    return null;
                }

                found = found ?? candidate;
            }

            return found;
        }
    }
}
