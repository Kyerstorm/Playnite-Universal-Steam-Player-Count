using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace SteamPlayerCount.Core
{
    [DataContract]
    public sealed class MatchFileData
    {
        [DataMember]
        public int SchemaVersion { get; set; }

        [DataMember]
        public List<MatchFileEntry> Entries { get; set; }
    }

    [DataContract]
    public sealed class MatchFileEntry
    {
        [DataMember]
        public string GameId { get; set; }

        [DataMember]
        public int? AppId { get; set; }

        [DataMember]
        public string SteamName { get; set; }

        [DataMember]
        public int Source { get; set; }

        [DataMember]
        public string CheckedUtc { get; set; }
    }

    public sealed class MatchFile : IDisposable
    {
        public const string FileName = "matches.json";
        public const int CurrentSchemaVersion = 1;
        public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        private readonly string directory;
        private readonly IClock clock;
        private readonly Action<Exception, string> logError;
        private readonly object sync = new object();
        private Timer timer;
        private MatchStore attached;
        private bool pending;
        private bool timerArmed;
        private bool savingDisabled;

        public MatchFile(string directory, IClock clock, Action<Exception, string> logError = null)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.logError = logError ?? ((e, m) => { });
        }

        public string FilePath
        {
            get { return Path.Combine(directory, FileName); }
        }

        public MatchStore Load()
        {
            if (!File.Exists(FilePath))
            {
                return new MatchStore();
            }

            string json = null;
            try
            {
                json = File.ReadAllText(FilePath);
            }
            catch (Exception e)
            {
                logError(e, "Could not read " + FilePath);
            }

            MatchFileData data;
            if (json == null ||
                !Json.TryDeserialize(json, out data) ||
                data.SchemaVersion != CurrentSchemaVersion ||
                data.Entries == null)
            {
                SetAside();
                return new MatchStore();
            }

            var entries = new Dictionary<Guid, MatchEntry>();
            foreach (var item in data.Entries)
            {
                Guid gameId;
                DateTime checkedUtc;
                if (item == null ||
                    !Guid.TryParse(item.GameId, out gameId) ||
                    !Enum.IsDefined(typeof(MatchSource), item.Source) ||
                    !DateTime.TryParse(item.CheckedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out checkedUtc))
                {
                    continue;
                }

                entries[gameId] = new MatchEntry
                {
                    AppId = item.AppId,
                    SteamName = item.SteamName,
                    Source = (MatchSource)item.Source,
                    CheckedUtc = checkedUtc.ToUniversalTime(),
                };
            }

            return new MatchStore(entries);
        }

        public void Attach(MatchStore store)
        {
            attached = store ?? throw new ArgumentNullException(nameof(store));
            timer = new Timer(state => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            store.Changed += OnStoreChanged;
        }

        public void Flush()
        {
            lock (sync)
            {
                timerArmed = false;
                if (!pending || attached == null)
                {
                    return;
                }

                pending = !Save(attached);
            }
        }

        public bool Save(MatchStore store)
        {
            lock (sync)
            {
                if (savingDisabled)
                {
                    return false;
                }

                try
                {
                    var data = new MatchFileData { SchemaVersion = CurrentSchemaVersion, Entries = new List<MatchFileEntry>() };
                    foreach (var pair in store.Snapshot())
                    {
                        data.Entries.Add(new MatchFileEntry
                        {
                            GameId = pair.Key.ToString(),
                            AppId = pair.Value.AppId,
                            SteamName = pair.Value.SteamName,
                            Source = (int)pair.Value.Source,
                            CheckedUtc = pair.Value.CheckedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                        });
                    }

                    Directory.CreateDirectory(directory);
                    var temp = FilePath + ".tmp";
                    File.WriteAllText(temp, Json.Serialize(data));
                    if (File.Exists(FilePath))
                    {
                        File.Replace(temp, FilePath, null);
                    }
                    else
                    {
                        File.Move(temp, FilePath);
                    }

                    return true;
                }
                catch (Exception e)
                {
                    logError(e, "Could not save " + FilePath);
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (attached != null)
            {
                attached.Changed -= OnStoreChanged;
            }

            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }

            Flush();
        }

        private void OnStoreChanged(object sender, EventArgs e)
        {
            // Arm the timer once per pending save. Re-arming on every change would let a steady
            // stream of changes (the bulk pass) postpone the save until the stream ends.
            bool arm;
            lock (sync)
            {
                pending = true;
                arm = !timerArmed;
                timerArmed = true;
            }

            var current = timer;
            if (arm && current != null)
            {
                try
                {
                    current.Change(SaveDelay, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void SetAside()
        {
            try
            {
                var name = "matches.corrupt-" + clock.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".json";
                File.Move(FilePath, Path.Combine(directory, name));
            }
            catch (Exception e)
            {
                // The bad file is still in place. Never overwrite it with an empty store.
                savingDisabled = true;
                logError(e, "Could not set aside unreadable " + FilePath + "; saving is disabled for this session");
            }
        }
    }
}
