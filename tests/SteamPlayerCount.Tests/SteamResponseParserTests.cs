using System.Linq;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class SteamResponseParserTests
    {
        // Trimmed from a real response recorded on 2026-10-05.
        private const string PreySearch = @"{""total"":3,""items"":[
            {""type"":""app"",""name"":""Prey"",""id"":480490,""price"":{""currency"":""USD"",""initial"":2999,""final"":599},""tiny_image"":""https:\/\/example.invalid\/a.jpg"",""metascore"":"""",""platforms"":{""windows"":true,""mac"":false,""linux"":false},""streamingvideo"":false,""controller_support"":""full""},
            {""type"":""app"",""name"":""Prey - Mooncrash"",""id"":865670,""platforms"":{""windows"":true}},
            {""type"":""app"",""name"":""Prey Demo"",""id"":609380}]}";

        [Fact]
        public void PlayerCount_200_with_result_1_is_a_count()
        {
            var outcome = SteamResponseParser.ParsePlayerCount(200, @"{""response"":{""player_count"":495050,""result"":1}}");

            Assert.Equal(CountStatus.Success, outcome.Status);
            Assert.Equal(495050, outcome.PlayerCount);
        }

        [Fact]
        public void PlayerCount_404_is_no_data()
        {
            var outcome = SteamResponseParser.ParsePlayerCount(404, @"{""response"":{""result"":42}}");

            Assert.Equal(CountStatus.NoData, outcome.Status);
        }

        [Fact]
        public void PlayerCount_200_with_another_result_is_no_data()
        {
            Assert.Equal(CountStatus.NoData, SteamResponseParser.ParsePlayerCount(200, @"{""response"":{""result"":42}}").Status);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(500, "oops")]
        [InlineData(429, "")]
        [InlineData(200, "")]
        [InlineData(200, "<html>not json</html>")]
        [InlineData(200, "{}")]
        [InlineData(200, @"{""response"":null}")]
        public void PlayerCount_anything_else_is_a_failure(int status, string body)
        {
            Assert.Equal(CountStatus.Failed, SteamResponseParser.ParsePlayerCount(status, body).Status);
        }

        [Fact]
        public void Search_200_returns_the_candidates_and_ignores_unknown_fields()
        {
            var outcome = SteamResponseParser.ParseSearch(200, PreySearch);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Equal(new[] { 480490, 865670, 609380 }, outcome.Candidates.Select(c => c.AppId).ToArray());
            Assert.Equal("Prey", outcome.Candidates[0].Name);
            Assert.Equal("app", outcome.Candidates[0].Type);
        }

        [Fact]
        public void Search_result_feeds_the_strict_matcher()
        {
            var outcome = SteamResponseParser.ParseSearch(200, PreySearch);

            Assert.Equal(480490, StrictNameMatcher.FindMatch("Prey", outcome.Candidates).AppId);
        }

        [Fact]
        public void Search_429_is_rate_limited()
        {
            Assert.Equal(SearchStatus.RateLimited, SteamResponseParser.ParseSearch(429, "").Status);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(500, "oops")]
        [InlineData(403, "")]
        [InlineData(200, "")]
        [InlineData(200, "<html>not json</html>")]
        public void Search_anything_else_is_a_failure(int status, string body)
        {
            Assert.Equal(SearchStatus.Failed, SteamResponseParser.ParseSearch(status, body).Status);
        }

        // Review Focus 4
        [Theory]
        [InlineData(@"{""total"":0}")]
        [InlineData(@"{""total"":0,""items"":null}")]
        [InlineData(@"{""total"":0,""items"":[]}")]
        public void Search_with_no_items_is_an_empty_success(string body)
        {
            var outcome = SteamResponseParser.ParseSearch(200, body);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Empty(outcome.Candidates);
        }

        // Review Focus 4
        [Fact]
        public void Search_drops_items_without_a_name_or_an_id()
        {
            var body = @"{""items"":[
                {""type"":""app"",""id"":5},
                {""type"":""app"",""name"":null,""id"":6},
                {""type"":""app"",""name"":""   "",""id"":7},
                {""type"":""app"",""name"":""No Id""},
                {""type"":""app"",""name"":""Zero"",""id"":0},
                {""name"":""No Type"",""id"":8},
                {""type"":""app"",""name"":""Good"",""id"":9}]}";

            var outcome = SteamResponseParser.ParseSearch(200, body);

            Assert.Equal(new[] { 8, 9 }, outcome.Candidates.Select(c => c.AppId).ToArray());
        }

        [Fact]
        public void Json_round_trips_and_rejects_garbage()
        {
            var json = Json.Serialize(new PlayerCountResponse { Response = new PlayerCountBody { PlayerCount = 3, Result = 1 } });

            PlayerCountResponse back;
            Assert.True(Json.TryDeserialize(json, out back));
            Assert.Equal(3, back.Response.PlayerCount);

            Assert.False(Json.TryDeserialize("not json", out back));
            Assert.Null(back);
            Assert.False(Json.TryDeserialize<PlayerCountResponse>(null, out back));
        }
    }
}
