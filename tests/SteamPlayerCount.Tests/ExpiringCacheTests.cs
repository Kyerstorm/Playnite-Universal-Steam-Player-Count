using System;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class ExpiringCacheTests
    {
        [Fact]
        public void Returns_a_value_until_its_lifetime_has_passed()
        {
            var clock = new FakeClock();
            var cache = new ExpiringCache<int, string>(clock);
            cache.Set(1, "a", TimeSpan.FromSeconds(120));

            string value;
            clock.Advance(TimeSpan.FromSeconds(119));
            Assert.True(cache.TryGet(1, out value));
            Assert.Equal("a", value);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(cache.TryGet(1, out value));
            Assert.Null(value);
        }

        [Fact]
        public void Each_entry_keeps_its_own_lifetime()
        {
            var clock = new FakeClock();
            var cache = new ExpiringCache<int, string>(clock);
            cache.Set(1, "short", TimeSpan.FromSeconds(10));
            cache.Set(2, "long", TimeSpan.FromMinutes(10));

            clock.Advance(TimeSpan.FromSeconds(30));

            string value;
            Assert.False(cache.TryGet(1, out value));
            Assert.True(cache.TryGet(2, out value));
        }

        [Fact]
        public void Set_replaces_an_existing_value()
        {
            var cache = new ExpiringCache<int, string>(new FakeClock());
            cache.Set(1, "old", TimeSpan.FromMinutes(1));
            cache.Set(1, "new", TimeSpan.FromMinutes(1));

            string value;
            Assert.True(cache.TryGet(1, out value));
            Assert.Equal("new", value);
        }
    }
}
