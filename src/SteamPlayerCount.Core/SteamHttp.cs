using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class HttpResult
    {
        public HttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        // 0 means no response was received (timeout or transport error).
        public int StatusCode { get; }
        public string Body { get; }
    }

    public sealed class SteamHttp : IDisposable
    {
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        private readonly HttpClient client;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        public SteamHttp(HttpMessageHandler handler = null, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalSteamPlayerCount/1.0");
            this.delay = delay ?? Task.Delay;
        }

        public async Task<HttpResult> GetAsync(string url, CancellationToken ct)
        {
            var first = await TryGetAsync(url, ct).ConfigureAwait(false);
            if (!IsTransient(first.StatusCode))
            {
                return first;
            }

            await delay(RetryDelay, ct).ConfigureAwait(false);
            return await TryGetAsync(url, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            client.Dispose();
        }

        private static bool IsTransient(int statusCode)
        {
            return statusCode == 0 || statusCode >= 500;
        }

        private async Task<HttpResult> TryGetAsync(string url, CancellationToken ct)
        {
            try
            {
                using (var response = await client.GetAsync(url, ct).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new HttpResult((int)response.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient reports its own timeout as a cancellation.
                return new HttpResult(0, null);
            }
            catch (HttpRequestException)
            {
                return new HttpResult(0, null);
            }
        }
    }
}
