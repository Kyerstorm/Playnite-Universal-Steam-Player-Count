using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class MinIntervalGateTests
    {
        private static readonly TimeSpan Gap = TimeSpan.FromSeconds(1.5);

        [Fact]
        public async Task First_call_does_not_wait_and_the_next_waits_for_the_gap()
        {
            var clock = new FakeClock();
            var delays = new List<TimeSpan>();
            var gate = new MinIntervalGate(Gap, clock, (d, ct) =>
            {
                delays.Add(d);
                clock.Advance(d);
                return Task.CompletedTask;
            });

            await gate.WaitAsync(CancellationToken.None);
            await gate.WaitAsync(CancellationToken.None);

            Assert.Equal(new[] { Gap }, delays);
        }

        [Fact]
        public async Task Only_the_remaining_part_of_the_gap_is_waited()
        {
            var clock = new FakeClock();
            var delays = new List<TimeSpan>();
            var gate = new MinIntervalGate(Gap, clock, (d, ct) =>
            {
                delays.Add(d);
                clock.Advance(d);
                return Task.CompletedTask;
            });

            await gate.WaitAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(1));
            await gate.WaitAsync(CancellationToken.None);

            Assert.Equal(new[] { TimeSpan.FromSeconds(0.5) }, delays);
        }

        [Fact]
        public async Task No_wait_when_the_gap_has_already_passed()
        {
            var clock = new FakeClock();
            var delays = new List<TimeSpan>();
            var gate = new MinIntervalGate(Gap, clock, (d, ct) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            });

            await gate.WaitAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(5));
            await gate.WaitAsync(CancellationToken.None);

            Assert.Empty(delays);
        }

        [Fact]
        public async Task A_cancelled_token_throws()
        {
            var gate = new MinIntervalGate(Gap, new FakeClock());
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitAsync(cts.Token));
        }
    }
}
