using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class SteamHttpTests
    {
        private sealed class FakeHandler : HttpMessageHandler
        {
            public readonly List<string> Urls = new List<string>();
            public readonly Queue<Func<HttpResponseMessage>> Responses = new Queue<Func<HttpResponseMessage>>();

            public void Enqueue(HttpStatusCode status, string body)
            {
                Responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
            }

            public void EnqueueError(Exception error)
            {
                Responses.Enqueue(() => { throw error; });
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Urls.Add(request.RequestUri.AbsoluteUri);
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Responses.Dequeue()());
            }
        }

        private readonly FakeHandler handler = new FakeHandler();
        private readonly List<TimeSpan> delays = new List<TimeSpan>();

        private SteamHttp Http()
        {
            return new SteamHttp(handler, (d, ct) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Returns_status_and_body()
        {
            handler.Enqueue(HttpStatusCode.OK, "hello");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.Equal("hello", result.Body);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Retries_once_after_one_second_on_a_server_error()
        {
            handler.Enqueue(HttpStatusCode.InternalServerError, "down");
            handler.Enqueue(HttpStatusCode.OK, "up");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.Equal(2, handler.Urls.Count);
            Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
        }

        [Fact]
        public async Task Retries_once_on_a_transport_error_and_then_reports_no_response()
        {
            handler.EnqueueError(new HttpRequestException("no network"));
            handler.EnqueueError(new HttpRequestException("no network"));

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(0, result.StatusCode);
            Assert.Null(result.Body);
            Assert.Equal(2, handler.Urls.Count);
        }

        [Fact]
        public async Task A_timeout_counts_as_a_transport_error()
        {
            handler.EnqueueError(new TaskCanceledException("timed out"));
            handler.Enqueue(HttpStatusCode.OK, "late");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData((HttpStatusCode)429)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task Does_not_retry_on_client_errors(HttpStatusCode status)
        {
            handler.Enqueue(status, "nope");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal((int)status, result.StatusCode);
            Assert.Single(handler.Urls);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Caller_cancellation_throws_and_does_not_retry()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Http().GetAsync("https://example.invalid/a", cts.Token));
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Player_count_client_calls_the_endpoint_and_parses_the_answer()
        {
            handler.Enqueue(HttpStatusCode.OK, @"{""response"":{""player_count"":42,""result"":1}}");

            var outcome = await new SteamPlayerCountsClient(Http()).GetAsync(570, CancellationToken.None);

            Assert.Equal(42, outcome.PlayerCount);
            Assert.Equal("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=570", handler.Urls[0]);
        }

        [Fact]
        public async Task Search_client_escapes_the_term_and_parses_the_answer()
        {
            handler.Enqueue(HttpStatusCode.OK, @"{""items"":[{""type"":""app"",""name"":""Ori & Friends"",""id"":7}]}");
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync("Ori & Friends", CancellationToken.None);

            Assert.Equal(7, outcome.Candidates[0].AppId);
            Assert.Equal("https://store.steampowered.com/api/storesearch/?term=Ori%20%26%20Friends&cc=us&l=english", handler.Urls[0]);
        }

        [Fact]
        public async Task Search_client_reports_rate_limiting()
        {
            handler.Enqueue((HttpStatusCode)429, "");
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync("Prey", CancellationToken.None);

            Assert.Equal(SearchStatus.RateLimited, outcome.Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task Search_client_does_not_call_steam_for_an_empty_term(string term)
        {
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync(term, CancellationToken.None);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Empty(outcome.Candidates);
            Assert.Empty(handler.Urls);
        }
    }
}
