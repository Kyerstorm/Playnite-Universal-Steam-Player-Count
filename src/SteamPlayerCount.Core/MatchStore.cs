using System;
using System.Collections.Generic;

namespace SteamPlayerCount.Core
{
    public sealed class MatchStore
    {
        private readonly object sync = new object();
        private readonly Dictionary<Guid, MatchEntry> entries;

        public MatchStore()
        {
            entries = new Dictionary<Guid, MatchEntry>();
        }

        public MatchStore(IDictionary<Guid, MatchEntry> initial)
        {
            entries = initial == null
                ? new Dictionary<Guid, MatchEntry>()
                : new Dictionary<Guid, MatchEntry>(initial);
        }

        public event EventHandler Changed;

        public bool TryGet(Guid gameId, out MatchEntry entry)
        {
            lock (sync)
            {
                return entries.TryGetValue(gameId, out entry);
            }
        }

        public void Set(Guid gameId, MatchEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (sync)
            {
                entries[gameId] = entry;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool Remove(Guid gameId)
        {
            bool removed;
            lock (sync)
            {
                removed = entries.Remove(gameId);
            }

            if (removed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return removed;
        }

        public Dictionary<Guid, MatchEntry> Snapshot()
        {
            lock (sync)
            {
                return new Dictionary<Guid, MatchEntry>(entries);
            }
        }
    }
}
