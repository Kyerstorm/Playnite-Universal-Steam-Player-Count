using System;
using System.Collections.Generic;

namespace SteamPlayerCount.Core
{
    public sealed class ExpiringCache<TKey, TValue>
    {
        private readonly IClock clock;
        private readonly object sync = new object();
        private readonly Dictionary<TKey, Item> items = new Dictionary<TKey, Item>();

        public ExpiringCache(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Set(TKey key, TValue value, TimeSpan lifetime)
        {
            lock (sync)
            {
                items[key] = new Item { Value = value, ExpiresUtc = clock.UtcNow + lifetime };
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            lock (sync)
            {
                Item item;
                if (items.TryGetValue(key, out item))
                {
                    if (clock.UtcNow < item.ExpiresUtc)
                    {
                        value = item.Value;
                        return true;
                    }

                    items.Remove(key);
                }
            }

            value = default(TValue);
            return false;
        }

        private struct Item
        {
            public TValue Value;
            public DateTime ExpiresUtc;
        }
    }
}
