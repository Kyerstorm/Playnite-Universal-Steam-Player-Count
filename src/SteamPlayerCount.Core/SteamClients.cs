using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class SteamPlayerCountsClient : ISteamPlayerCounts
    {
        private const string UrlFormat = "https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={0}";

        private readonly SteamHttp http;

        public SteamPlayerCountsClient(SteamHttp http)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<CountOutcome> GetAsync(int appId, CancellationToken ct)
        {
            var url = string.Format(CultureInfo.InvariantCulture, UrlFormat, appId);
            var result = await http.GetAsync(url, ct).ConfigureAwait(false);
            return SteamResponseParser.ParsePlayerCount(result.StatusCode, result.Body);
        }
    }

    public sealed class SteamStoreSearchClient : ISteamSearch
    {
        private const string UrlFormat = "https://store.steampowered.com/api/storesearch/?term={0}&cc=us&l=english";

        private readonly SteamHttp http;
        private readonly MinIntervalGate gate;

        public SteamStoreSearchClient(SteamHttp http, MinIntervalGate gate)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public async Task<SearchOutcome> SearchAsync(string term, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return SearchOutcome.Success(null);
            }

            await gate.WaitAsync(ct).ConfigureAwait(false);
            var url = string.Format(CultureInfo.InvariantCulture, UrlFormat, Uri.EscapeDataString(term));
            var result = await http.GetAsync(url, ct).ConfigureAwait(false);
            return SteamResponseParser.ParseSearch(result.StatusCode, result.Body);
        }
    }
}
