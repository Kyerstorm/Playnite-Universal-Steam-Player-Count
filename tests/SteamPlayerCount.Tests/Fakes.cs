using System;
using SteamPlayerCount.Core;

namespace SteamPlayerCount.Tests
{
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow + by;
        }
    }
}
