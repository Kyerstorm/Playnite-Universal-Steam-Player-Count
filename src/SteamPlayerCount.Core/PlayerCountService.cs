using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class PlayerCountService
    {
        public static readonly TimeSpan CountLifetime = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan NoDataLifetime = TimeSpan.FromMinutes(10);

        private readonly AppIdResolver resolver;
        private readonly ISteamPlayerCounts counts;
        private readonly ExpiringCache<int, CountOutcome> cache;
        private readonly object sync = new object();
        private readonly Dictionary<int, Task<CountOutcome>> inFlight = new Dictionary<int, Task<CountOutcome>>();

        public PlayerCountService(AppIdResolver resolver, ISteamPlayerCounts counts, IClock clock)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.counts = counts ?? throw new ArgumentNullException(nameof(counts));
            cache = new ExpiringCache<int, CountOutcome>(clock ?? throw new ArgumentNullException(nameof(clock)));
        }

        public async Task<PlayerCountResult> GetAsync(GameInfo game, CancellationToken ct)
        {
            if (game == null)
            {
                return PlayerCountResult.None;
            }

            var resolved = await resolver.ResolveAsync(game, ct).ConfigureAwait(false);
            if (resolved.Status != ResolveStatus.Resolved)
            {
                return PlayerCountResult.None;
            }

            CountOutcome outcome;
            if (!cache.TryGet(resolved.AppId, out outcome))
            {
                outcome = await WaitWithCancellation(GetOrStartFetch(resolved.AppId), ct).ConfigureAwait(false);
            }

            return outcome.Status == CountStatus.Success
                ? new PlayerCountResult(resolved.AppId, resolved.SteamName ?? game.Name, outcome.PlayerCount)
                : PlayerCountResult.None;
        }

        private Task<CountOutcome> GetOrStartFetch(int appId)
        {
            lock (sync)
            {
                Task<CountOutcome> running;
                if (!inFlight.TryGetValue(appId, out running))
                {
                    // Task.Run guarantees the task is registered before its cleanup can run.
                    running = Task.Run(() => FetchAndCacheAsync(appId));
                    inFlight[appId] = running;
                }

                return running;
            }
        }

        private async Task<CountOutcome> FetchAndCacheAsync(int appId)
        {
            try
            {
                CountOutcome outcome;
                try
                {
                    outcome = await counts.GetAsync(appId, CancellationToken.None).ConfigureAwait(false) ?? CountOutcome.Failed();
                }
                catch (Exception)
                {
                    outcome = CountOutcome.Failed();
                }

                if (outcome.Status == CountStatus.Success)
                {
                    cache.Set(appId, outcome, CountLifetime);
                }
                else if (outcome.Status == CountStatus.NoData)
                {
                    cache.Set(appId, outcome, NoDataLifetime);
                }

                return outcome;
            }
            finally
            {
                lock (sync)
                {
                    inFlight.Remove(appId);
                }
            }
        }

        private static async Task<T> WaitWithCancellation<T>(Task<T> task, CancellationToken ct)
        {
            if (!task.IsCompleted && ct.CanBeCanceled)
            {
                var cancelled = new TaskCompletionSource<bool>();
                using (ct.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                    {
                        throw new OperationCanceledException(ct);
                    }
                }
            }

            return await task.ConfigureAwait(false);
        }
    }
}
