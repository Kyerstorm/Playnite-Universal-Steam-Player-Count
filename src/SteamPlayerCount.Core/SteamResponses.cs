using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SteamPlayerCount.Core
{
    [DataContract]
    public sealed class PlayerCountResponse
    {
        [DataMember(Name = "response")]
        public PlayerCountBody Response { get; set; }
    }

    [DataContract]
    public sealed class PlayerCountBody
    {
        [DataMember(Name = "player_count")]
        public int PlayerCount { get; set; }

        [DataMember(Name = "result")]
        public int Result { get; set; }
    }

    [DataContract]
    public sealed class StoreSearchResponse
    {
        [DataMember(Name = "items")]
        public List<StoreSearchItem> Items { get; set; }
    }

    [DataContract]
    public sealed class StoreSearchItem
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "id")]
        public int Id { get; set; }
    }

    public static class SteamResponseParser
    {
        private const int SteamResultOk = 1;

        public static CountOutcome ParsePlayerCount(int statusCode, string body)
        {
            if (statusCode == 404)
            {
                return CountOutcome.NoData();
            }

            PlayerCountResponse parsed;
            if (statusCode != 200 || !Json.TryDeserialize(body, out parsed) || parsed.Response == null)
            {
                return CountOutcome.Failed();
            }

            return parsed.Response.Result == SteamResultOk
                ? CountOutcome.Success(parsed.Response.PlayerCount)
                : CountOutcome.NoData();
        }

        public static SearchOutcome ParseSearch(int statusCode, string body)
        {
            if (statusCode == 429)
            {
                return SearchOutcome.RateLimited();
            }

            StoreSearchResponse parsed;
            if (statusCode != 200 || !Json.TryDeserialize(body, out parsed))
            {
                return SearchOutcome.Failed();
            }

            var candidates = new List<SteamCandidate>();
            if (parsed.Items != null)
            {
                foreach (var item in parsed.Items)
                {
                    if (item != null && item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
                    {
                        candidates.Add(new SteamCandidate(item.Id, item.Name, item.Type));
                    }
                }
            }

            return SearchOutcome.Success(candidates);
        }
    }
}
