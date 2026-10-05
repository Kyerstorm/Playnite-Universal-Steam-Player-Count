using System;
using System.Collections.Generic;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class MatchStoreTests
    {
        private static MatchEntry Entry(int appId)
        {
            return new MatchEntry
            {
                AppId = appId,
                SteamName = "Game " + appId,
                Source = MatchSource.Manual,
                CheckedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
        }

        [Fact]
        public void MatchSource_values_are_stable()
        {
            Assert.Equal(1, (int)MatchSource.Manual);
            Assert.Equal(2, (int)MatchSource.NameSearch);
            Assert.Equal(3, (int)MatchSource.NotFound);
            Assert.Equal(4, (int)MatchSource.ManualExcluded);
        }

        [Fact]
        public void Set_then_TryGet_returns_the_entry_and_raises_Changed()
        {
            var store = new MatchStore();
            var id = Guid.NewGuid();
            var changes = 0;
            store.Changed += (s, e) => changes++;

            store.Set(id, Entry(480490));

            MatchEntry found;
            Assert.True(store.TryGet(id, out found));
            Assert.Equal(480490, found.AppId);
            Assert.Equal(1, changes);
        }

        [Fact]
        public void TryGet_on_an_unknown_game_returns_false()
        {
            MatchEntry found;
            Assert.False(new MatchStore().TryGet(Guid.NewGuid(), out found));
            Assert.Null(found);
        }

        [Fact]
        public void Remove_raises_Changed_only_when_something_was_removed()
        {
            var id = Guid.NewGuid();
            var store = new MatchStore(new Dictionary<Guid, MatchEntry> { { id, Entry(1) } });
            var changes = 0;
            store.Changed += (s, e) => changes++;

            Assert.True(store.Remove(id));
            Assert.False(store.Remove(id));
            Assert.Equal(1, changes);
        }

        [Fact]
        public void Snapshot_is_a_copy()
        {
            var id = Guid.NewGuid();
            var store = new MatchStore(new Dictionary<Guid, MatchEntry> { { id, Entry(1) } });

            var snapshot = store.Snapshot();
            snapshot.Clear();

            MatchEntry found;
            Assert.True(store.TryGet(id, out found));
        }

        [Fact]
        public void Set_rejects_a_null_entry()
        {
            Assert.Throws<ArgumentNullException>(() => new MatchStore().Set(Guid.NewGuid(), null));
        }
    }
}
