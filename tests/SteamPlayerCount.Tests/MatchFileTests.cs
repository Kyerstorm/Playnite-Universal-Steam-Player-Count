using System;
using System.IO;
using System.Linq;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class MatchFileTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "uspc-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeClock clock = new FakeClock();

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private MatchFile File_()
        {
            return new MatchFile(directory, clock);
        }

        private string FilePath
        {
            get { return Path.Combine(directory, MatchFile.FileName); }
        }

        private void WriteRaw(string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, content);
        }

        [Fact]
        public void A_missing_file_loads_as_an_empty_store()
        {
            Assert.Empty(File_().Load().Snapshot());
        }

        [Fact]
        public void Entries_survive_a_save_and_load()
        {
            var matched = Guid.NewGuid();
            var excluded = Guid.NewGuid();
            var checkedUtc = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            var store = new MatchStore();
            store.Set(matched, new MatchEntry { AppId = 480490, SteamName = "Prey \"2017\"", Source = MatchSource.NameSearch, CheckedUtc = checkedUtc });
            store.Set(excluded, new MatchEntry { AppId = null, SteamName = null, Source = MatchSource.ManualExcluded, CheckedUtc = checkedUtc });

            Assert.True(File_().Save(store));
            var loaded = File_().Load().Snapshot();

            Assert.Equal(2, loaded.Count);
            Assert.Equal(480490, loaded[matched].AppId);
            Assert.Equal("Prey \"2017\"", loaded[matched].SteamName);
            Assert.Equal(MatchSource.NameSearch, loaded[matched].Source);
            Assert.Equal(checkedUtc, loaded[matched].CheckedUtc);
            Assert.Equal(DateTimeKind.Utc, loaded[matched].CheckedUtc.Kind);
            Assert.Null(loaded[excluded].AppId);
            Assert.Equal(MatchSource.ManualExcluded, loaded[excluded].Source);
        }

        [Fact]
        public void Saving_twice_replaces_the_file_and_leaves_no_temp_file()
        {
            var store = new MatchStore();
            var file = File_();
            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 1, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });
            file.Save(store);
            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 2, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });
            file.Save(store);

            Assert.Equal(2, File_().Load().Snapshot().Count);
            Assert.Equal(new[] { MatchFile.FileName }, Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("")]
        [InlineData(@"{""SchemaVersion"":1}")]
        [InlineData(@"{""SchemaVersion"":2,""Entries"":[]}")]
        [InlineData(@"{""Entries"":[]}")]
        public void An_unusable_file_is_set_aside_and_an_empty_store_is_used(string content)
        {
            WriteRaw(content);
            clock.UtcNow = new DateTime(2026, 10, 5, 13, 14, 15, DateTimeKind.Utc);

            var store = File_().Load();

            Assert.Empty(store.Snapshot());
            Assert.False(File.Exists(FilePath));
            var aside = Path.Combine(directory, "matches.corrupt-20261005131415.json");
            Assert.True(File.Exists(aside));
            Assert.Equal(content, File.ReadAllText(aside));
        }

        [Fact]
        public void Entries_that_cannot_be_understood_are_skipped_and_the_rest_are_kept()
        {
            var good = Guid.NewGuid();
            WriteRaw(@"{""SchemaVersion"":1,""Entries"":[
                {""GameId"":""not-a-guid"",""AppId"":1,""Source"":1,""CheckedUtc"":""2026-10-05T12:00:00.0000000Z""},
                {""GameId"":""" + Guid.NewGuid() + @""",""AppId"":2,""Source"":99,""CheckedUtc"":""2026-10-05T12:00:00.0000000Z""},
                {""GameId"":""" + Guid.NewGuid() + @""",""AppId"":3,""Source"":1,""CheckedUtc"":""yesterday-ish""},
                null,
                {""GameId"":""" + good + @""",""AppId"":4,""Source"":1,""CheckedUtc"":""2026-10-05T12:00:00.0000000Z""}]}");

            var loaded = File_().Load().Snapshot();

            Assert.Equal(new[] { good }, loaded.Keys.ToArray());
            Assert.True(File.Exists(FilePath));
        }

        [Fact]
        public void Flush_saves_only_when_an_attached_store_has_changed()
        {
            var file = File_();
            var store = file.Load();
            file.Attach(store);

            file.Flush();
            Assert.False(File.Exists(FilePath));

            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 1, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });
            file.Flush();
            Assert.True(File.Exists(FilePath));
        }

        [Fact]
        public void Dispose_flushes_a_pending_change()
        {
            var file = File_();
            var store = file.Load();
            file.Attach(store);
            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 1, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });

            file.Dispose();

            Assert.Single(File_().Load().Snapshot());
        }

        [Fact]
        public void Steady_changes_do_not_keep_postponing_the_save()
        {
            var file = File_();
            var store = file.Load();
            file.Attach(store);

            // Two changes 1.3 s apart, as the bulk pass makes them. The save is due 2 s after the first.
            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 1, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });
            System.Threading.Thread.Sleep(1300);
            store.Set(Guid.NewGuid(), new MatchEntry { AppId = 2, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });
            System.Threading.Thread.Sleep(1300);

            Assert.True(File.Exists(FilePath));
            file.Dispose();
        }

        // Review Focus 5
        [Fact]
        public void A_save_that_cannot_write_reports_failure_logs_and_is_retried_by_the_next_flush()
        {
            // A file sits where the data folder should be, so the folder cannot be created.
            var blocker = Path.Combine(Path.GetTempPath(), "uspc-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "in the way");
            try
            {
                var logged = 0;
                var file = new MatchFile(blocker, clock, (e, m) => logged++);
                var store = new MatchStore();
                file.Attach(store);
                store.Set(Guid.NewGuid(), new MatchEntry { AppId = 1, Source = MatchSource.Manual, CheckedUtc = clock.UtcNow });

                Assert.False(file.Save(store));
                file.Flush();
                Assert.True(logged >= 2);

                File.Delete(blocker);
                file.Flush();
                Assert.True(File.Exists(Path.Combine(blocker, MatchFile.FileName)));
            }
            finally
            {
                if (File.Exists(blocker))
                {
                    File.Delete(blocker);
                }

                if (Directory.Exists(blocker))
                {
                    Directory.Delete(blocker, true);
                }
            }
        }
    }
}
