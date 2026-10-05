using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class MinIntervalGate
    {
        private readonly TimeSpan minInterval;
        private readonly IClock clock;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly SemaphoreSlim turn = new SemaphoreSlim(1, 1);
        private DateTime nextAllowedUtc = DateTime.MinValue;

        public MinIntervalGate(TimeSpan minInterval, IClock clock, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.minInterval = minInterval;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.delay = delay ?? Task.Delay;
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            await turn.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var wait = nextAllowedUtc - clock.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await delay(wait, ct).ConfigureAwait(false);
                }

                nextAllowedUtc = clock.UtcNow + minInterval;
            }
            finally
            {
                turn.Release();
            }
        }
    }
}
