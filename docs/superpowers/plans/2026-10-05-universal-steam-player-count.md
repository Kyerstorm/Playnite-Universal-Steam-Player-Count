# Universal Steam Player Count Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Playnite generic extension that shows the current Steam player count for the selected game, including GOG, Epic and other non-Steam games matched to their Steam equivalent.

**Architecture:** A core library with no Playnite reference holds matching, caching, HTTP, JSON parsing and the match file, all unit-tested. A thin WPF plugin project adapts it to Playnite: a theme custom element, a top panel item, menus and settings.

**Tech Stack:** C# 7.3, .NET Framework 4.6.2 (`net462`), SDK-style projects built with the .NET SDK 10 CLI, PlayniteSDK 6.17.0, WPF, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-05-universal-steam-player-count-design.md`

## Global Constraints

- Every project targets `net462` and compiles as C# 7.3. No tuples (`System.ValueTuple` is not in 4.6.2), no nullable reference types, no `using var`, no switch expressions, no `System.Text.Json`.
- The plugin ships exactly two of its own assemblies: `UniversalSteamPlayerCount.dll` and `SteamPlayerCount.Core.dll`. No third-party NuGet package ships with it.
- `Playnite.SDK.Data.Serialization` is used only for plugin settings through `LoadPluginSettings` / `SavePluginSettings`. All other JSON goes through `SteamPlayerCount.Core.Json` (`DataContractJsonSerializer`), because `Playnite.SDK.dll` has no JSON implementation outside the running Playnite process and so cannot be unit-tested.
- Plugin id: `aa33d12d-49ed-42b6-86eb-d96efe1ccbb3`. Never change it.
- Theme source name `SteamPlayerCount`, element name `PlayerCountControl`.
- Steam library plugin id: `cb91dfc9-b977-43bf-8e70-55f46e410fab`.
- Constants: selection debounce 700 ms; count cache 120 s; "no data" cache 10 minutes; "not found" retry after 30 days; store search minimum gap 1.5 s; HTTP timeout 10 s; one retry after 1 s on timeout, transport error or 5xx; no retry on 404 or 429; match file save delay 2 s.
- `MatchSource` values are persisted and never renumbered: `Manual = 1`, `NameSearch = 2`, `NotFound = 3`, `ManualExcluded = 4`.
- User data only under `GetPluginUserDataPath()`.
- Every user-visible string is in `Localization/en_US.xaml` with a `LOCSteamPlayerCount…` key.
- Every awaited call inside `SteamPlayerCount.Core` uses `.ConfigureAwait(false)`. The manual-match dialog blocks on a search, and a captured UI context there would deadlock.
- Every method Playnite calls catches and logs. Nothing modal opens from background work.
- Playnite members used in this plan were checked by reflection against `Playnite.SDK 6.17.0.0`. Do not introduce a Playnite member that is not already in this plan without checking it the same way:
  `powershell -NoProfile -File "$env:USERPROFILE\.claude\skills\playnite-development-core\scripts\dump-sdk-api.ps1" -Type '<TypeName>'`
- Commits need a git author identity. If `git commit` fails with "Author identity unknown", stop and ask the user to set one. Do not invent a name.

## Review Focus

1. A game whose name has no letters or digits (for example `???` or an empty name) must never match anything and must not trigger a search. Pinned in Task 2 (matcher) and Task 5 (resolver).
2. A manual choice saved while an automatic search for the same game is still running must win; the search result must not overwrite it. Pinned in Task 5.
3. Malformed library data must not crash resolution: a Steam library game with a non-numeric or oversized id, and links whose URL is null, empty or not a URL. Pinned in Task 5.
4. A store search response with missing or null fields (`items` absent, an item with no name or `id` 0) must parse to an empty or filtered list without throwing. Pinned in Task 7.
5. `matches.json` that cannot be written (folder missing or blocked) must not throw into Playnite, and the pending save must be retried on the next change. Pinned in Task 9.

## File Structure

```
.gitignore
Directory.Build.props                         shared net462 / C# 7.3 settings
UniversalSteamPlayerCount.sln
README.md                                     user docs and the theme snippet
src/SteamPlayerCount.Core/
  SteamPlayerCount.Core.csproj
  GameNameNormalizer.cs                       name -> comparable key
  SteamCandidate.cs                           one store search hit
  StrictNameMatcher.cs                        exact-name, unambiguous match
  MatchEntry.cs                               MatchSource enum + MatchEntry
  MatchStore.cs                               thread-safe game id -> MatchEntry
  Clock.cs                                    IClock + SystemClock
  ExpiringCache.cs                            TTL cache
  MinIntervalGate.cs                          minimum gap between calls
  GameInfo.cs                                 plain view of a library game
  SteamSearch.cs                              ISteamSearch + SearchOutcome
  AppIdResolver.cs                            the five-step resolution + ResolveResult
  PlayerCounts.cs                             ISteamPlayerCounts, CountOutcome, PlayerCountResult
  PlayerCountService.cs                       resolve + cached count
  Json.cs                                     DataContractJsonSerializer helper
  SteamResponses.cs                           DTOs + SteamResponseParser
  SteamHttp.cs                                HttpClient wrapper with retry
  SteamClients.cs                             the two endpoint clients
  MatchFile.cs                                matches.json load / save
src/UniversalSteamPlayerCount/
  UniversalSteamPlayerCount.csproj
  extension.yaml
  icon.png
  Localization/en_US.xaml
  PluginSettings.cs                           settings data + ISettings view model
  PluginSettingsView.xaml(.cs)
  PluginServices.cs                           composition root
  GameInfoFactory.cs                          Playnite Game -> GameInfo
  SteamLinks.cs                               opens the SteamDB page
  SelectionWatcher.cs                         debounced selection -> count
  UniversalSteamPlayerCountPlugin.cs          the GenericPlugin
  PlayerCountControl.xaml(.cs)                theme element
  MatchActions.cs                             game menu and main menu actions
tests/SteamPlayerCount.Tests/
  SteamPlayerCount.Tests.csproj
  Fakes.cs                                    FakeClock, FakeSearch, FakeCounts, Games
  *Tests.cs                                   one file per core unit
```

Run all commands from the repository root in PowerShell.

---

### Task 1: Solution scaffold and GameNameNormalizer

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `UniversalSteamPlayerCount.sln`
- Create: `src/SteamPlayerCount.Core/SteamPlayerCount.Core.csproj`, `src/SteamPlayerCount.Core/GameNameNormalizer.cs`
- Create: `tests/SteamPlayerCount.Tests/SteamPlayerCount.Tests.csproj`
- Test: `tests/SteamPlayerCount.Tests/GameNameNormalizerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string SteamPlayerCount.Core.GameNameNormalizer.Normalize(string name)`; a working `dotnet test` loop.

- [ ] **Step 1: Write the shared build files**

`.gitignore`:

```
bin/
obj/
.vs/
*.user
*.pext
dist/
TestResults/
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`src/SteamPlayerCount.Core/SteamPlayerCount.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>SteamPlayerCount.Core</AssemblyName>
    <RootNamespace>SteamPlayerCount.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System.Net.Http" />
    <Reference Include="System.Runtime.Serialization" />
  </ItemGroup>
</Project>
```

`tests/SteamPlayerCount.Tests/SteamPlayerCount.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <RootNamespace>SteamPlayerCount.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="System.Net.Http" />
    <ProjectReference Include="..\..\src\SteamPlayerCount.Core\SteamPlayerCount.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the solution**

```powershell
dotnet new sln -n UniversalSteamPlayerCount --format sln
dotnet sln add src/SteamPlayerCount.Core/SteamPlayerCount.Core.csproj tests/SteamPlayerCount.Tests/SteamPlayerCount.Tests.csproj
```

Expected: `UniversalSteamPlayerCount.sln` exists and lists both projects.

- [ ] **Step 3: Write the failing test**

`tests/SteamPlayerCount.Tests/GameNameNormalizerTests.cs`:

```csharp
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class GameNameNormalizerTests
    {
        [Theory]
        [InlineData("Prey", "prey")]
        [InlineData("PREY®", "prey")]
        [InlineData("  Prey  ", "prey")]
        [InlineData("Prey - Mooncrash", "preymooncrash")]
        [InlineData("The Witcher 3: Wild Hunt", "thewitcher3wildhunt")]
        [InlineData("Ori & the Blind Forest", "oriandtheblindforest")]
        [InlineData("Ori and the Blind Forest", "oriandtheblindforest")]
        [InlineData("DOOM™ Eternal", "doometernal")]
        public void Normalize_reduces_a_name_to_letters_and_digits(string input, string expected)
        {
            Assert.Equal(expected, GameNameNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public void Normalize_returns_empty_when_nothing_comparable_remains(string input)
        {
            Assert.Equal(string.Empty, GameNameNormalizer.Normalize(input));
        }
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests`
Expected: build FAILS with `CS0103` or `CS0246`: `GameNameNormalizer` does not exist. If restore fails instead, fix the package versions before going on; the toolchain must work before any other task.

- [ ] **Step 5: Write the implementation**

`src/SteamPlayerCount.Core/GameNameNormalizer.cs`:

```csharp
using System.Text;

namespace SteamPlayerCount.Core
{
    public static class GameNameNormalizer
    {
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var source = name.Replace("&", " and ").ToLowerInvariant();
            var builder = new StringBuilder(source.Length);
            foreach (var c in source)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests`
Expected: PASS, 0 failed.

- [ ] **Step 7: Commit**

```powershell
git add .gitignore Directory.Build.props UniversalSteamPlayerCount.sln src tests docs
git commit -m "Scaffold solution and add game name normalizer"
```

---

### Task 2: StrictNameMatcher

**Files:**
- Create: `src/SteamPlayerCount.Core/SteamCandidate.cs`, `src/SteamPlayerCount.Core/StrictNameMatcher.cs`
- Test: `tests/SteamPlayerCount.Tests/StrictNameMatcherTests.cs`

**Interfaces:**
- Consumes: `GameNameNormalizer.Normalize(string)`.
- Produces: `sealed class SteamCandidate(int appId, string name, string type)` with `AppId`, `Name`, `Type`; `static SteamCandidate StrictNameMatcher.FindMatch(string gameName, IEnumerable<SteamCandidate> candidates)` returning `null` for no match or an ambiguous match.

- [ ] **Step 1: Write the failing test**

`tests/SteamPlayerCount.Tests/StrictNameMatcherTests.cs`:

```csharp
using System.Collections.Generic;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class StrictNameMatcherTests
    {
        private static readonly List<SteamCandidate> PreyResults = new List<SteamCandidate>
        {
            new SteamCandidate(480490, "Prey", "app"),
            new SteamCandidate(865670, "Prey - Mooncrash", "app"),
            new SteamCandidate(609380, "Prey Demo", "app"),
            new SteamCandidate(354427, "Predator/Prey Pack", "app"),
            new SteamCandidate(2537120, "911: Prey", "app"),
        };

        [Fact]
        public void Picks_the_only_candidate_with_the_same_normalized_name()
        {
            var match = StrictNameMatcher.FindMatch("PREY®", PreyResults);

            Assert.NotNull(match);
            Assert.Equal(480490, match.AppId);
        }

        [Fact]
        public void Returns_null_when_no_candidate_has_the_same_name()
        {
            Assert.Null(StrictNameMatcher.FindMatch("Prey 2", PreyResults));
        }

        [Fact]
        public void Returns_null_when_two_different_apps_share_the_name()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(1, "Doom", "app"),
                new SteamCandidate(2, "DOOM", "app"),
            };

            Assert.Null(StrictNameMatcher.FindMatch("Doom", candidates));
        }

        [Fact]
        public void The_same_app_listed_twice_is_not_ambiguous()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(7, "Doom", "app"),
                new SteamCandidate(7, "DOOM", "app"),
            };

            Assert.Equal(7, StrictNameMatcher.FindMatch("Doom", candidates).AppId);
        }

        [Fact]
        public void Ignores_candidates_that_are_not_apps()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(5, "Prey", "bundle"),
                new SteamCandidate(6, "Prey", null),
            };

            Assert.Null(StrictNameMatcher.FindMatch("Prey", candidates));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("???")]
        public void A_name_with_nothing_comparable_never_matches(string gameName)
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(9, "!!!", "app"),
                new SteamCandidate(10, "", "app"),
            };

            Assert.Null(StrictNameMatcher.FindMatch(gameName, candidates));
        }

        [Fact]
        public void Tolerates_null_candidates_and_null_entries()
        {
            Assert.Null(StrictNameMatcher.FindMatch("Prey", null));
            Assert.Null(StrictNameMatcher.FindMatch("Prey", new List<SteamCandidate> { null }));
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~StrictNameMatcherTests`
Expected: build FAILS, `SteamCandidate` and `StrictNameMatcher` do not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/SteamCandidate.cs`:

```csharp
namespace SteamPlayerCount.Core
{
    public sealed class SteamCandidate
    {
        public SteamCandidate(int appId, string name, string type)
        {
            AppId = appId;
            Name = name;
            Type = type;
        }

        public int AppId { get; }
        public string Name { get; }
        public string Type { get; }
    }
}
```

`src/SteamPlayerCount.Core/StrictNameMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace SteamPlayerCount.Core
{
    public static class StrictNameMatcher
    {
        public const string AppType = "app";

        public static SteamCandidate FindMatch(string gameName, IEnumerable<SteamCandidate> candidates)
        {
            var wanted = GameNameNormalizer.Normalize(gameName);
            if (wanted.Length == 0 || candidates == null)
            {
                return null;
            }

            SteamCandidate found = null;
            foreach (var candidate in candidates)
            {
                if (candidate == null ||
                    !string.Equals(candidate.Type, AppType, StringComparison.OrdinalIgnoreCase) ||
                    GameNameNormalizer.Normalize(candidate.Name) != wanted)
                {
                    continue;
                }

                if (found != null && found.AppId != candidate.AppId)
                {
                    return null;
                }

                found = found ?? candidate;
            }

            return found;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~StrictNameMatcherTests`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Add strict Steam name matcher"
```

---

### Task 3: MatchEntry and MatchStore

**Files:**
- Create: `src/SteamPlayerCount.Core/MatchEntry.cs`, `src/SteamPlayerCount.Core/MatchStore.cs`
- Test: `tests/SteamPlayerCount.Tests/MatchStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum MatchSource { Manual = 1, NameSearch = 2, NotFound = 3, ManualExcluded = 4 }`
  - `sealed class MatchEntry { int? AppId; string SteamName; MatchSource Source; DateTime CheckedUtc; }` (settable properties)
  - `sealed class MatchStore` with `MatchStore()`, `MatchStore(IDictionary<Guid, MatchEntry> initial)`, `bool TryGet(Guid gameId, out MatchEntry entry)`, `void Set(Guid gameId, MatchEntry entry)`, `bool Remove(Guid gameId)`, `Dictionary<Guid, MatchEntry> Snapshot()`, `event EventHandler Changed`.

- [ ] **Step 1: Write the failing test**

`tests/SteamPlayerCount.Tests/MatchStoreTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~MatchStoreTests`
Expected: build FAILS, `MatchStore`, `MatchEntry` and `MatchSource` do not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/MatchEntry.cs`:

```csharp
using System;

namespace SteamPlayerCount.Core
{
    // Persisted to matches.json. Never renumber or reuse a value.
    public enum MatchSource
    {
        Manual = 1,
        NameSearch = 2,
        NotFound = 3,
        ManualExcluded = 4,
    }

    public sealed class MatchEntry
    {
        public int? AppId { get; set; }
        public string SteamName { get; set; }
        public MatchSource Source { get; set; }
        public DateTime CheckedUtc { get; set; }
    }
}
```

`src/SteamPlayerCount.Core/MatchStore.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~MatchStoreTests`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Add match entry model and in-memory match store"
```

---

### Task 4: Clock, ExpiringCache and MinIntervalGate

**Files:**
- Create: `src/SteamPlayerCount.Core/Clock.cs`, `src/SteamPlayerCount.Core/ExpiringCache.cs`, `src/SteamPlayerCount.Core/MinIntervalGate.cs`
- Create: `tests/SteamPlayerCount.Tests/Fakes.cs`
- Test: `tests/SteamPlayerCount.Tests/ExpiringCacheTests.cs`, `tests/SteamPlayerCount.Tests/MinIntervalGateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `interface IClock { DateTime UtcNow { get; } }`, `sealed class SystemClock : IClock`
  - `sealed class ExpiringCache<TKey, TValue>(IClock clock)` with `void Set(TKey key, TValue value, TimeSpan lifetime)`, `bool TryGet(TKey key, out TValue value)`
  - `sealed class MinIntervalGate(TimeSpan minInterval, IClock clock, Func<TimeSpan, CancellationToken, Task> delay = null)` with `Task WaitAsync(CancellationToken ct)`
  - Test helper `FakeClock : IClock` with settable `UtcNow` and `void Advance(TimeSpan)`.

- [ ] **Step 1: Write the test helper and the failing tests**

`tests/SteamPlayerCount.Tests/Fakes.cs`:

```csharp
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
```

`tests/SteamPlayerCount.Tests/ExpiringCacheTests.cs`:

```csharp
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
```

`tests/SteamPlayerCount.Tests/MinIntervalGateTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter "FullyQualifiedName~ExpiringCacheTests|FullyQualifiedName~MinIntervalGateTests"`
Expected: build FAILS, `IClock`, `ExpiringCache` and `MinIntervalGate` do not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/Clock.cs`:

```csharp
using System;

namespace SteamPlayerCount.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
```

`src/SteamPlayerCount.Core/ExpiringCache.cs`:

```csharp
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
```

`src/SteamPlayerCount.Core/MinIntervalGate.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter "FullyQualifiedName~ExpiringCacheTests|FullyQualifiedName~MinIntervalGateTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Add clock, expiring cache and minimum-interval gate"
```

---

### Task 5: AppIdResolver

**Files:**
- Create: `src/SteamPlayerCount.Core/GameInfo.cs`, `src/SteamPlayerCount.Core/SteamSearch.cs`, `src/SteamPlayerCount.Core/AppIdResolver.cs`
- Modify: `tests/SteamPlayerCount.Tests/Fakes.cs` (add `FakeSearch` and `Games`)
- Test: `tests/SteamPlayerCount.Tests/AppIdResolverTests.cs`

**Interfaces:**
- Consumes: `MatchStore`, `MatchEntry`, `MatchSource`, `IClock`, `StrictNameMatcher.FindMatch`, `GameNameNormalizer.Normalize`, `SteamCandidate`.
- Produces:
  - `sealed class GameInfo(Guid id, string name, Guid libraryPluginId, string libraryGameId, IEnumerable<string> linkUrls)` with `Id`, `Name`, `LibraryPluginId`, `LibraryGameId`, `IReadOnlyList<string> LinkUrls` (never null)
  - `enum SearchStatus { Success, Failed, RateLimited }`
  - `sealed class SearchOutcome` with `Status`, `IReadOnlyList<SteamCandidate> Candidates` (never null), `static Success(IEnumerable<SteamCandidate>)`, `static Failed()`, `static RateLimited()`
  - `interface ISteamSearch { Task<SearchOutcome> SearchAsync(string term, CancellationToken ct); }`
  - `enum ResolveStatus { Resolved, NoMatch, Failed, RateLimited }`
  - `sealed class ResolveResult` with `Status`, `int AppId`, `string SteamName`, `static readonly NoMatch / Failed / RateLimited`, `static Resolved(int appId, string steamName)`
  - `sealed class AppIdResolver(MatchStore store, ISteamSearch search, IClock clock, Func<bool> nonSteamMatchingEnabled)` with `static readonly Guid SteamLibraryPluginId`, `static readonly TimeSpan NotFoundRetryAfter`, `bool NeedsSearch(GameInfo game)`, `Task<ResolveResult> ResolveAsync(GameInfo game, CancellationToken ct)`. `ResolveAsync` lets `OperationCanceledException` propagate.

- [ ] **Step 1: Add the test helpers**

Append to `tests/SteamPlayerCount.Tests/Fakes.cs`, inside the namespace, and add these usings at the top of the file: `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`.

```csharp
    internal sealed class FakeSearch : ISteamSearch
    {
        public int Calls;
        public Func<string, SearchOutcome> Handler = term => SearchOutcome.Success(new SteamCandidate[0]);
        public Action BeforeReturn;

        public Task<SearchOutcome> SearchAsync(string term, CancellationToken ct)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            var outcome = Handler(term);
            BeforeReturn?.Invoke();
            return Task.FromResult(outcome);
        }
    }

    internal static class Games
    {
        public static readonly Guid GogPluginId = new Guid("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e");

        public static GameInfo Steam(string libraryGameId, string name = "Steam Game")
        {
            return new GameInfo(Guid.NewGuid(), name, AppIdResolver.SteamLibraryPluginId, libraryGameId, null);
        }

        public static GameInfo Gog(string name, params string[] links)
        {
            return new GameInfo(Guid.NewGuid(), name, GogPluginId, "gog-1", links);
        }
    }
```

- [ ] **Step 2: Write the failing test**

`tests/SteamPlayerCount.Tests/AppIdResolverTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class AppIdResolverTests
    {
        private readonly MatchStore store = new MatchStore();
        private readonly FakeSearch search = new FakeSearch();
        private readonly FakeClock clock = new FakeClock();
        private bool nonSteamEnabled = true;

        private AppIdResolver Resolver()
        {
            return new AppIdResolver(store, search, clock, () => nonSteamEnabled);
        }

        private MatchEntry Entry(MatchSource source, int? appId)
        {
            return new MatchEntry { AppId = appId, SteamName = "Saved", Source = source, CheckedUtc = clock.UtcNow };
        }

        private void SearchReturns(params SteamCandidate[] candidates)
        {
            search.Handler = term => SearchOutcome.Success(candidates);
        }

        [Fact]
        public async Task Step1_manual_match_wins_over_everything()
        {
            var game = Games.Steam("570");
            store.Set(game.Id, Entry(MatchSource.Manual, 999));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(999, result.AppId);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step1_manual_exclusion_means_no_match_even_with_a_link()
        {
            var game = Games.Gog("Prey", "https://store.steampowered.com/app/480490/Prey/");
            store.Set(game.Id, Entry(MatchSource.ManualExcluded, null));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step2_steam_library_game_uses_its_own_id()
        {
            var result = await Resolver().ResolveAsync(Games.Steam("570", "Dota 2"), CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(570, result.AppId);
            Assert.Equal("Dota 2", result.SteamName);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step2_still_works_when_non_steam_matching_is_disabled()
        {
            nonSteamEnabled = false;

            var result = await Resolver().ResolveAsync(Games.Steam("570"), CancellationToken.None);

            Assert.Equal(570, result.AppId);
        }

        [Theory]
        [InlineData("https://store.steampowered.com/app/480490/Prey/")]
        [InlineData("http://steamcommunity.com/app/480490")]
        [InlineData("HTTPS://STORE.STEAMPOWERED.COM/app/480490")]
        public async Task Step3_steam_link_gives_the_id_without_storing_anything(string url)
        {
            var game = Games.Gog("Prey", "https://www.gog.com/game/prey", url);

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(480490, result.AppId);
            Assert.Equal(0, search.Calls);
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public async Task Step4_saved_search_match_is_reused()
        {
            var game = Games.Gog("Prey");
            store.Set(game.Id, Entry(MatchSource.NameSearch, 480490));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal("Saved", result.SteamName);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step4_recent_not_found_is_not_searched_again()
        {
            var game = Games.Gog("Obscure");
            store.Set(game.Id, Entry(MatchSource.NotFound, null));
            clock.Advance(TimeSpan.FromDays(29));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Step4_not_found_older_than_30_days_is_searched_again()
        {
            var game = Games.Gog("Prey");
            store.Set(game.Id, Entry(MatchSource.NotFound, null));
            clock.Advance(TimeSpan.FromDays(30));
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal(1, search.Calls);
        }

        [Fact]
        public async Task Step5_search_hit_is_stored_as_NameSearch()
        {
            var game = Games.Gog("Prey");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"), new SteamCandidate(865670, "Prey - Mooncrash", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.Resolved, result.Status);
            Assert.Equal(480490, result.AppId);
            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(MatchSource.NameSearch, saved.Source);
            Assert.Equal(480490, saved.AppId);
            Assert.Equal("Prey", saved.SteamName);
            Assert.Equal(clock.UtcNow, saved.CheckedUtc);
        }

        [Fact]
        public async Task Step5_search_miss_is_stored_as_NotFound()
        {
            var game = Games.Gog("Obscure");

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(MatchSource.NotFound, saved.Source);
            Assert.Null(saved.AppId);
        }

        [Theory]
        [InlineData(SearchStatus.Failed, ResolveStatus.Failed)]
        [InlineData(SearchStatus.RateLimited, ResolveStatus.RateLimited)]
        public async Task Step5_a_failed_search_stores_nothing(SearchStatus searchStatus, ResolveStatus expected)
        {
            var game = Games.Gog("Prey");
            search.Handler = term => searchStatus == SearchStatus.Failed ? SearchOutcome.Failed() : SearchOutcome.RateLimited();

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(expected, result.Status);
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public async Task Disabled_non_steam_matching_skips_links_saved_matches_and_search()
        {
            nonSteamEnabled = false;
            var withLink = Games.Gog("Prey", "https://store.steampowered.com/app/480490/");
            var withSaved = Games.Gog("Prey");
            store.Set(withSaved.Id, Entry(MatchSource.NameSearch, 480490));

            Assert.Equal(ResolveStatus.NoMatch, (await Resolver().ResolveAsync(withLink, CancellationToken.None)).Status);
            Assert.Equal(ResolveStatus.NoMatch, (await Resolver().ResolveAsync(withSaved, CancellationToken.None)).Status);
            Assert.Equal(0, search.Calls);
        }

        [Fact]
        public async Task Cancellation_propagates_and_stores_nothing()
        {
            var game = Games.Gog("Prey");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver().ResolveAsync(game, cts.Token));
            Assert.Empty(store.Snapshot());
        }

        [Fact]
        public void NeedsSearch_is_true_only_when_local_steps_are_inconclusive()
        {
            var resolver = Resolver();
            var saved = Games.Gog("Saved");
            store.Set(saved.Id, Entry(MatchSource.NameSearch, 1));

            Assert.True(resolver.NeedsSearch(Games.Gog("Prey")));
            Assert.False(resolver.NeedsSearch(Games.Steam("570")));
            Assert.False(resolver.NeedsSearch(Games.Gog("Prey", "https://store.steampowered.com/app/480490/")));
            Assert.False(resolver.NeedsSearch(saved));
            Assert.False(resolver.NeedsSearch(null));
        }

        // Review Focus 1
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("???")]
        public async Task A_name_with_nothing_comparable_is_no_match_without_a_search(string name)
        {
            var game = Games.Gog(name);

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(0, search.Calls);
            Assert.Empty(store.Snapshot());
        }

        // Review Focus 2
        [Theory]
        [InlineData(MatchSource.Manual)]
        [InlineData(MatchSource.ManualExcluded)]
        public async Task A_manual_choice_made_during_the_search_is_not_overwritten(MatchSource manualSource)
        {
            var game = Games.Gog("Prey");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));
            search.BeforeReturn = () => store.Set(game.Id, Entry(manualSource, manualSource == MatchSource.Manual ? (int?)777 : null));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            MatchEntry saved;
            Assert.True(store.TryGet(game.Id, out saved));
            Assert.Equal(manualSource, saved.Source);
            if (manualSource == MatchSource.Manual)
            {
                Assert.Equal(777, result.AppId);
            }
            else
            {
                Assert.Equal(ResolveStatus.NoMatch, result.Status);
            }
        }

        // Review Focus 3
        [Theory]
        [InlineData("not-a-number")]
        [InlineData("99999999999999999999")]
        [InlineData("-5")]
        [InlineData("0")]
        [InlineData(null)]
        public async Task A_steam_library_game_with_an_unusable_id_falls_through_to_the_search(string libraryGameId)
        {
            var game = Games.Steam(libraryGameId, "Some Mod");

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(ResolveStatus.NoMatch, result.Status);
            Assert.Equal(1, search.Calls);
        }

        // Review Focus 3
        [Fact]
        public async Task Null_empty_and_malformed_links_are_ignored()
        {
            var game = Games.Gog("Prey", null, "", "not a url", "https://store.steampowered.com/app/abc/", "https://store.steampowered.com/app/99999999999999999999/");
            SearchReturns(new SteamCandidate(480490, "Prey", "app"));

            var result = await Resolver().ResolveAsync(game, CancellationToken.None);

            Assert.Equal(480490, result.AppId);
            Assert.Equal(1, search.Calls);
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~AppIdResolverTests`
Expected: build FAILS, `GameInfo`, `ISteamSearch`, `SearchOutcome` and `AppIdResolver` do not exist.

- [ ] **Step 4: Write the implementation**

`src/SteamPlayerCount.Core/GameInfo.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamPlayerCount.Core
{
    public sealed class GameInfo
    {
        public GameInfo(Guid id, string name, Guid libraryPluginId, string libraryGameId, IEnumerable<string> linkUrls)
        {
            Id = id;
            Name = name;
            LibraryPluginId = libraryPluginId;
            LibraryGameId = libraryGameId;
            LinkUrls = linkUrls == null ? new List<string>() : linkUrls.ToList();
        }

        public Guid Id { get; }
        public string Name { get; }
        public Guid LibraryPluginId { get; }
        public string LibraryGameId { get; }
        public IReadOnlyList<string> LinkUrls { get; }
    }
}
```

`src/SteamPlayerCount.Core/SteamSearch.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum SearchStatus
    {
        Success,
        Failed,
        RateLimited,
    }

    public sealed class SearchOutcome
    {
        private static readonly IReadOnlyList<SteamCandidate> None = new SteamCandidate[0];

        private SearchOutcome(SearchStatus status, IReadOnlyList<SteamCandidate> candidates)
        {
            Status = status;
            Candidates = candidates;
        }

        public SearchStatus Status { get; }
        public IReadOnlyList<SteamCandidate> Candidates { get; }

        public static SearchOutcome Success(IEnumerable<SteamCandidate> candidates)
        {
            return new SearchOutcome(SearchStatus.Success, candidates == null ? None : candidates.ToList());
        }

        public static SearchOutcome Failed()
        {
            return new SearchOutcome(SearchStatus.Failed, None);
        }

        public static SearchOutcome RateLimited()
        {
            return new SearchOutcome(SearchStatus.RateLimited, None);
        }
    }

    public interface ISteamSearch
    {
        Task<SearchOutcome> SearchAsync(string term, CancellationToken ct);
    }
}
```

`src/SteamPlayerCount.Core/AppIdResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum ResolveStatus
    {
        Resolved,
        NoMatch,
        Failed,
        RateLimited,
    }

    public sealed class ResolveResult
    {
        public static readonly ResolveResult NoMatch = new ResolveResult(ResolveStatus.NoMatch, 0, null);
        public static readonly ResolveResult Failed = new ResolveResult(ResolveStatus.Failed, 0, null);
        public static readonly ResolveResult RateLimited = new ResolveResult(ResolveStatus.RateLimited, 0, null);

        private ResolveResult(ResolveStatus status, int appId, string steamName)
        {
            Status = status;
            AppId = appId;
            SteamName = steamName;
        }

        public ResolveStatus Status { get; }
        public int AppId { get; }
        public string SteamName { get; }

        public static ResolveResult Resolved(int appId, string steamName)
        {
            return new ResolveResult(ResolveStatus.Resolved, appId, steamName);
        }
    }

    public sealed class AppIdResolver
    {
        public static readonly Guid SteamLibraryPluginId = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        public static readonly TimeSpan NotFoundRetryAfter = TimeSpan.FromDays(30);

        private static readonly Regex SteamLinkRegex = new Regex(
            @"https?://(?:store\.steampowered|steamcommunity)\.com/app/(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly MatchStore store;
        private readonly ISteamSearch search;
        private readonly IClock clock;
        private readonly Func<bool> nonSteamMatchingEnabled;

        public AppIdResolver(MatchStore store, ISteamSearch search, IClock clock, Func<bool> nonSteamMatchingEnabled)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.search = search ?? throw new ArgumentNullException(nameof(search));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.nonSteamMatchingEnabled = nonSteamMatchingEnabled ?? throw new ArgumentNullException(nameof(nonSteamMatchingEnabled));
        }

        public bool NeedsSearch(GameInfo game)
        {
            ResolveResult ignored;
            return game != null && !TryResolveLocal(game, out ignored);
        }

        public async Task<ResolveResult> ResolveAsync(GameInfo game, CancellationToken ct)
        {
            if (game == null)
            {
                return ResolveResult.NoMatch;
            }

            ResolveResult local;
            if (TryResolveLocal(game, out local))
            {
                return local;
            }

            var outcome = await search.SearchAsync(game.Name, ct).ConfigureAwait(false);
            if (outcome.Status == SearchStatus.RateLimited)
            {
                return ResolveResult.RateLimited;
            }

            if (outcome.Status != SearchStatus.Success)
            {
                return ResolveResult.Failed;
            }

            // The user may have chosen by hand while the search was running.
            MatchEntry current;
            ResolveResult manual;
            if (store.TryGet(game.Id, out current) && TryResolveManual(current, out manual))
            {
                return manual;
            }

            var match = StrictNameMatcher.FindMatch(game.Name, outcome.Candidates);
            store.Set(game.Id, new MatchEntry
            {
                AppId = match == null ? (int?)null : match.AppId,
                SteamName = match == null ? null : match.Name,
                Source = match == null ? MatchSource.NotFound : MatchSource.NameSearch,
                CheckedUtc = clock.UtcNow,
            });

            return match == null ? ResolveResult.NoMatch : ResolveResult.Resolved(match.AppId, match.Name);
        }

        private static bool TryResolveManual(MatchEntry entry, out ResolveResult result)
        {
            if (entry.Source == MatchSource.Manual && entry.AppId.HasValue)
            {
                result = ResolveResult.Resolved(entry.AppId.Value, entry.SteamName);
                return true;
            }

            if (entry.Source == MatchSource.ManualExcluded)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            result = null;
            return false;
        }

        private bool TryResolveLocal(GameInfo game, out ResolveResult result)
        {
            MatchEntry entry;
            var hasEntry = store.TryGet(game.Id, out entry);

            // 1. Manual choice.
            if (hasEntry && TryResolveManual(entry, out result))
            {
                return true;
            }

            // 2. Steam library game.
            int appId;
            if (game.LibraryPluginId == SteamLibraryPluginId && TryParseAppId(game.LibraryGameId, out appId))
            {
                result = ResolveResult.Resolved(appId, game.Name);
                return true;
            }

            if (!nonSteamMatchingEnabled())
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // 3. Steam link in the game's links.
            if (TryGetLinkAppId(game.LinkUrls, out appId))
            {
                result = ResolveResult.Resolved(appId, game.Name);
                return true;
            }

            // 4. Saved search result.
            if (hasEntry && entry.Source == MatchSource.NameSearch && entry.AppId.HasValue)
            {
                result = ResolveResult.Resolved(entry.AppId.Value, entry.SteamName);
                return true;
            }

            if (hasEntry && entry.Source == MatchSource.NotFound && clock.UtcNow - entry.CheckedUtc < NotFoundRetryAfter)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // A name with nothing comparable can never match, so do not search for it.
            if (GameNameNormalizer.Normalize(game.Name).Length == 0)
            {
                result = ResolveResult.NoMatch;
                return true;
            }

            // 5. Needs a search.
            result = null;
            return false;
        }

        private static bool TryGetLinkAppId(IReadOnlyList<string> urls, out int appId)
        {
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var match = SteamLinkRegex.Match(url);
                if (match.Success && TryParseAppId(match.Groups[1].Value, out appId))
                {
                    return true;
                }
            }

            appId = 0;
            return false;
        }

        private static bool TryParseAppId(string text, out int appId)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out appId) && appId > 0;
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~AppIdResolverTests`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "Add Steam AppID resolver with the five-step resolution order"
```

---

### Task 6: PlayerCountService

**Files:**
- Create: `src/SteamPlayerCount.Core/PlayerCounts.cs`, `src/SteamPlayerCount.Core/PlayerCountService.cs`
- Modify: `tests/SteamPlayerCount.Tests/Fakes.cs` (add `FakeCounts`)
- Test: `tests/SteamPlayerCount.Tests/PlayerCountServiceTests.cs`

**Interfaces:**
- Consumes: `AppIdResolver.ResolveAsync`, `ResolveResult`, `ResolveStatus`, `ExpiringCache<int, CountOutcome>`, `IClock`, `GameInfo`.
- Produces:
  - `enum CountStatus { Success, NoData, Failed }`
  - `sealed class CountOutcome` with `Status`, `int PlayerCount`, `static Success(int)`, `static NoData()`, `static Failed()`
  - `interface ISteamPlayerCounts { Task<CountOutcome> GetAsync(int appId, CancellationToken ct); }`
  - `sealed class PlayerCountResult` with `bool HasCount`, `int PlayerCount`, `int AppId`, `string SteamName`, `static readonly None`, constructor `(int appId, string steamName, int playerCount)`
  - `sealed class PlayerCountService(AppIdResolver resolver, ISteamPlayerCounts counts, IClock clock)` with `static readonly TimeSpan CountLifetime` (120 s), `NoDataLifetime` (10 min), `Task<PlayerCountResult> GetAsync(GameInfo game, CancellationToken ct)`. Throws `OperationCanceledException` when `ct` is cancelled. Never throws for a failed fetch.

Design note: a count fetch is shared between callers and is not cancelled when one caller goes away. A caller's token only stops that caller waiting. The fetch itself is bounded by the 10 s HTTP timeout and fills the cache for the next selection.

- [ ] **Step 1: Add the test helper**

Append to `tests/SteamPlayerCount.Tests/Fakes.cs`, inside the namespace:

```csharp
    internal sealed class FakeCounts : ISteamPlayerCounts
    {
        public int Calls;
        public Func<int, CountOutcome> Handler = appId => CountOutcome.Success(100);
        public Task Gate = Task.CompletedTask;

        public async Task<CountOutcome> GetAsync(int appId, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Gate.ConfigureAwait(false);
            return Handler(appId);
        }
    }
```

- [ ] **Step 2: Write the failing test**

`tests/SteamPlayerCount.Tests/PlayerCountServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class PlayerCountServiceTests
    {
        private readonly FakeClock clock = new FakeClock();
        private readonly FakeCounts counts = new FakeCounts();
        private readonly FakeSearch search = new FakeSearch();

        private PlayerCountService Service()
        {
            var resolver = new AppIdResolver(new MatchStore(), search, clock, () => true);
            return new PlayerCountService(resolver, counts, clock);
        }

        [Fact]
        public async Task Returns_the_count_for_a_resolved_game()
        {
            counts.Handler = appId => CountOutcome.Success(495050);

            var result = await Service().GetAsync(Games.Steam("570", "Dota 2"), CancellationToken.None);

            Assert.True(result.HasCount);
            Assert.Equal(495050, result.PlayerCount);
            Assert.Equal(570, result.AppId);
            Assert.Equal("Dota 2", result.SteamName);
        }

        [Fact]
        public async Task An_unresolved_game_has_no_count_and_no_fetch()
        {
            var result = await Service().GetAsync(Games.Gog("Obscure"), CancellationToken.None);

            Assert.False(result.HasCount);
            Assert.Equal(0, counts.Calls);
        }

        [Fact]
        public async Task A_count_is_cached_per_app_for_120_seconds()
        {
            var service = Service();

            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(119));
            await service.GetAsync(Games.Gog("Dota 2", "https://store.steampowered.com/app/570/"), CancellationToken.None);
            Assert.Equal(1, counts.Calls);

            clock.Advance(TimeSpan.FromSeconds(1));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task No_data_is_cached_for_ten_minutes()
        {
            counts.Handler = appId => CountOutcome.NoData();
            var service = Service();

            Assert.False((await service.GetAsync(Games.Steam("570"), CancellationToken.None)).HasCount);
            clock.Advance(TimeSpan.FromMinutes(9));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(1, counts.Calls);

            clock.Advance(TimeSpan.FromMinutes(1));
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task A_failed_fetch_is_not_cached()
        {
            counts.Handler = appId => CountOutcome.Failed();
            var service = Service();

            Assert.False((await service.GetAsync(Games.Steam("570"), CancellationToken.None)).HasCount);
            await service.GetAsync(Games.Steam("570"), CancellationToken.None);

            Assert.Equal(2, counts.Calls);
        }

        [Fact]
        public async Task A_throwing_client_gives_no_count_instead_of_an_exception()
        {
            counts.Handler = appId => { throw new InvalidOperationException("boom"); };

            var result = await Service().GetAsync(Games.Steam("570"), CancellationToken.None);

            Assert.False(result.HasCount);
        }

        [Fact]
        public async Task Concurrent_requests_for_the_same_app_share_one_fetch()
        {
            var gate = new TaskCompletionSource<bool>();
            counts.Gate = gate.Task;
            var service = Service();

            var first = service.GetAsync(Games.Steam("570"), CancellationToken.None);
            var second = service.GetAsync(Games.Steam("570"), CancellationToken.None);
            gate.SetResult(true);
            var results = await Task.WhenAll(first, second);

            Assert.True(results[0].HasCount);
            Assert.True(results[1].HasCount);
            Assert.Equal(1, counts.Calls);
        }

        [Fact]
        public async Task A_cancelled_caller_stops_waiting_but_the_fetch_still_fills_the_cache()
        {
            var gate = new TaskCompletionSource<bool>();
            counts.Gate = gate.Task;
            var service = Service();
            var cts = new CancellationTokenSource();

            var waiting = service.GetAsync(Games.Steam("570"), cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

            gate.SetResult(true);
            counts.Gate = Task.CompletedTask;
            PlayerCountResult result = PlayerCountResult.None;
            for (var i = 0; i < 50 && !result.HasCount; i++)
            {
                await Task.Delay(20);
                result = await service.GetAsync(Games.Steam("570"), CancellationToken.None);
            }

            Assert.True(result.HasCount);
            Assert.Equal(1, counts.Calls);
        }

        [Fact]
        public async Task A_null_game_has_no_count()
        {
            Assert.False((await Service().GetAsync(null, CancellationToken.None)).HasCount);
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~PlayerCountServiceTests`
Expected: build FAILS, `CountOutcome`, `ISteamPlayerCounts`, `PlayerCountResult` and `PlayerCountService` do not exist.

- [ ] **Step 4: Write the implementation**

`src/SteamPlayerCount.Core/PlayerCounts.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum CountStatus
    {
        Success,
        NoData,
        Failed,
    }

    public sealed class CountOutcome
    {
        private CountOutcome(CountStatus status, int playerCount)
        {
            Status = status;
            PlayerCount = playerCount;
        }

        public CountStatus Status { get; }
        public int PlayerCount { get; }

        public static CountOutcome Success(int playerCount)
        {
            return new CountOutcome(CountStatus.Success, playerCount);
        }

        public static CountOutcome NoData()
        {
            return new CountOutcome(CountStatus.NoData, 0);
        }

        public static CountOutcome Failed()
        {
            return new CountOutcome(CountStatus.Failed, 0);
        }
    }

    public interface ISteamPlayerCounts
    {
        Task<CountOutcome> GetAsync(int appId, CancellationToken ct);
    }

    public sealed class PlayerCountResult
    {
        public static readonly PlayerCountResult None = new PlayerCountResult();

        private PlayerCountResult()
        {
        }

        public PlayerCountResult(int appId, string steamName, int playerCount)
        {
            HasCount = true;
            AppId = appId;
            SteamName = steamName;
            PlayerCount = playerCount;
        }

        public bool HasCount { get; }
        public int AppId { get; }
        public string SteamName { get; }
        public int PlayerCount { get; }
    }
}
```

`src/SteamPlayerCount.Core/PlayerCountService.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~PlayerCountServiceTests`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "Add player count service with per-app caching and shared fetches"
```

---

### Task 7: Json helper and Steam response parsing

**Files:**
- Create: `src/SteamPlayerCount.Core/Json.cs`, `src/SteamPlayerCount.Core/SteamResponses.cs`
- Test: `tests/SteamPlayerCount.Tests/SteamResponseParserTests.cs`

**Interfaces:**
- Consumes: `CountOutcome`, `SearchOutcome`, `SteamCandidate`.
- Produces:
  - `static class Json` with `bool TryDeserialize<T>(string json, out T value) where T : class`, `string Serialize<T>(T value)`
  - `static class SteamResponseParser` with `CountOutcome ParsePlayerCount(int statusCode, string body)`, `SearchOutcome ParseSearch(int statusCode, string body)`. Status code `0` means the request never got a response.

- [ ] **Step 1: Write the failing test**

`tests/SteamPlayerCount.Tests/SteamResponseParserTests.cs`:

```csharp
using System.Linq;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class SteamResponseParserTests
    {
        // Trimmed from a real response recorded on 2026-10-05.
        private const string PreySearch = @"{""total"":3,""items"":[
            {""type"":""app"",""name"":""Prey"",""id"":480490,""price"":{""currency"":""USD"",""initial"":2999,""final"":599},""tiny_image"":""https:\/\/example.invalid\/a.jpg"",""metascore"":"""",""platforms"":{""windows"":true,""mac"":false,""linux"":false},""streamingvideo"":false,""controller_support"":""full""},
            {""type"":""app"",""name"":""Prey - Mooncrash"",""id"":865670,""platforms"":{""windows"":true}},
            {""type"":""app"",""name"":""Prey Demo"",""id"":609380}]}";

        [Fact]
        public void PlayerCount_200_with_result_1_is_a_count()
        {
            var outcome = SteamResponseParser.ParsePlayerCount(200, @"{""response"":{""player_count"":495050,""result"":1}}");

            Assert.Equal(CountStatus.Success, outcome.Status);
            Assert.Equal(495050, outcome.PlayerCount);
        }

        [Fact]
        public void PlayerCount_404_is_no_data()
        {
            var outcome = SteamResponseParser.ParsePlayerCount(404, @"{""response"":{""result"":42}}");

            Assert.Equal(CountStatus.NoData, outcome.Status);
        }

        [Fact]
        public void PlayerCount_200_with_another_result_is_no_data()
        {
            Assert.Equal(CountStatus.NoData, SteamResponseParser.ParsePlayerCount(200, @"{""response"":{""result"":42}}").Status);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(500, "oops")]
        [InlineData(429, "")]
        [InlineData(200, "")]
        [InlineData(200, "<html>not json</html>")]
        [InlineData(200, "{}")]
        [InlineData(200, @"{""response"":null}")]
        public void PlayerCount_anything_else_is_a_failure(int status, string body)
        {
            Assert.Equal(CountStatus.Failed, SteamResponseParser.ParsePlayerCount(status, body).Status);
        }

        [Fact]
        public void Search_200_returns_the_candidates_and_ignores_unknown_fields()
        {
            var outcome = SteamResponseParser.ParseSearch(200, PreySearch);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Equal(new[] { 480490, 865670, 609380 }, outcome.Candidates.Select(c => c.AppId).ToArray());
            Assert.Equal("Prey", outcome.Candidates[0].Name);
            Assert.Equal("app", outcome.Candidates[0].Type);
        }

        [Fact]
        public void Search_result_feeds_the_strict_matcher()
        {
            var outcome = SteamResponseParser.ParseSearch(200, PreySearch);

            Assert.Equal(480490, StrictNameMatcher.FindMatch("Prey", outcome.Candidates).AppId);
        }

        [Fact]
        public void Search_429_is_rate_limited()
        {
            Assert.Equal(SearchStatus.RateLimited, SteamResponseParser.ParseSearch(429, "").Status);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(500, "oops")]
        [InlineData(403, "")]
        [InlineData(200, "")]
        [InlineData(200, "<html>not json</html>")]
        public void Search_anything_else_is_a_failure(int status, string body)
        {
            Assert.Equal(SearchStatus.Failed, SteamResponseParser.ParseSearch(status, body).Status);
        }

        // Review Focus 4
        [Theory]
        [InlineData(@"{""total"":0}")]
        [InlineData(@"{""total"":0,""items"":null}")]
        [InlineData(@"{""total"":0,""items"":[]}")]
        public void Search_with_no_items_is_an_empty_success(string body)
        {
            var outcome = SteamResponseParser.ParseSearch(200, body);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Empty(outcome.Candidates);
        }

        // Review Focus 4
        [Fact]
        public void Search_drops_items_without_a_name_or_an_id()
        {
            var body = @"{""items"":[
                {""type"":""app"",""id"":5},
                {""type"":""app"",""name"":null,""id"":6},
                {""type"":""app"",""name"":""   "",""id"":7},
                {""type"":""app"",""name"":""No Id""},
                {""type"":""app"",""name"":""Zero"",""id"":0},
                {""name"":""No Type"",""id"":8},
                {""type"":""app"",""name"":""Good"",""id"":9}]}";

            var outcome = SteamResponseParser.ParseSearch(200, body);

            Assert.Equal(new[] { 8, 9 }, outcome.Candidates.Select(c => c.AppId).ToArray());
        }

        [Fact]
        public void Json_round_trips_and_rejects_garbage()
        {
            var json = Json.Serialize(new PlayerCountResponse { Response = new PlayerCountBody { PlayerCount = 3, Result = 1 } });

            PlayerCountResponse back;
            Assert.True(Json.TryDeserialize(json, out back));
            Assert.Equal(3, back.Response.PlayerCount);

            Assert.False(Json.TryDeserialize("not json", out back));
            Assert.Null(back);
            Assert.False(Json.TryDeserialize<PlayerCountResponse>(null, out back));
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~SteamResponseParserTests`
Expected: build FAILS, `SteamResponseParser`, `Json` and the response types do not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/Json.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SteamPlayerCount.Core
{
    // Framework-only JSON. Playnite's serializer only works inside Playnite, so it cannot be unit-tested.
    public static class Json
    {
        public static bool TryDeserialize<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    value = serializer.ReadObject(stream) as T;
                }

                return value != null;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        public static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
```

`src/SteamPlayerCount.Core/SteamResponses.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SteamPlayerCount.Core
{
    [DataContract]
    public sealed class PlayerCountResponse
    {
        [DataMember(Name = "response")]
        public PlayerCountBody Response { get; set; }
    }

    [DataContract]
    public sealed class PlayerCountBody
    {
        [DataMember(Name = "player_count")]
        public int PlayerCount { get; set; }

        [DataMember(Name = "result")]
        public int Result { get; set; }
    }

    [DataContract]
    public sealed class StoreSearchResponse
    {
        [DataMember(Name = "items")]
        public List<StoreSearchItem> Items { get; set; }
    }

    [DataContract]
    public sealed class StoreSearchItem
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "id")]
        public int Id { get; set; }
    }

    public static class SteamResponseParser
    {
        private const int SteamResultOk = 1;

        public static CountOutcome ParsePlayerCount(int statusCode, string body)
        {
            if (statusCode == 404)
            {
                return CountOutcome.NoData();
            }

            PlayerCountResponse parsed;
            if (statusCode != 200 || !Json.TryDeserialize(body, out parsed) || parsed.Response == null)
            {
                return CountOutcome.Failed();
            }

            return parsed.Response.Result == SteamResultOk
                ? CountOutcome.Success(parsed.Response.PlayerCount)
                : CountOutcome.NoData();
        }

        public static SearchOutcome ParseSearch(int statusCode, string body)
        {
            if (statusCode == 429)
            {
                return SearchOutcome.RateLimited();
            }

            StoreSearchResponse parsed;
            if (statusCode != 200 || !Json.TryDeserialize(body, out parsed))
            {
                return SearchOutcome.Failed();
            }

            var candidates = new List<SteamCandidate>();
            if (parsed.Items != null)
            {
                foreach (var item in parsed.Items)
                {
                    if (item != null && item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
                    {
                        candidates.Add(new SteamCandidate(item.Id, item.Name, item.Type));
                    }
                }
            }

            return SearchOutcome.Success(candidates);
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~SteamResponseParserTests`
Expected: PASS, 0 failed. If `PlayerCount_anything_else_is_a_failure` fails for the `{}` case, the cause is `parsed.Response == null` not being checked; do not weaken the test.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Add framework JSON helper and Steam response parsing"
```

---

### Task 8: SteamHttp and the two endpoint clients

**Files:**
- Create: `src/SteamPlayerCount.Core/SteamHttp.cs`, `src/SteamPlayerCount.Core/SteamClients.cs`
- Test: `tests/SteamPlayerCount.Tests/SteamHttpTests.cs`

**Interfaces:**
- Consumes: `SteamResponseParser`, `MinIntervalGate`, `ISteamSearch`, `ISteamPlayerCounts`.
- Produces:
  - `sealed class HttpResult(int statusCode, string body)` with `StatusCode` (0 = no response), `Body`
  - `sealed class SteamHttp : IDisposable` with constructor `(HttpMessageHandler handler = null, Func<TimeSpan, CancellationToken, Task> delay = null)`, `Task<HttpResult> GetAsync(string url, CancellationToken ct)`
  - `sealed class SteamPlayerCountsClient(SteamHttp http) : ISteamPlayerCounts`
  - `sealed class SteamStoreSearchClient(SteamHttp http, MinIntervalGate gate) : ISteamSearch`

- [ ] **Step 1: Write the failing test**

`tests/SteamPlayerCount.Tests/SteamHttpTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class SteamHttpTests
    {
        private sealed class FakeHandler : HttpMessageHandler
        {
            public readonly List<string> Urls = new List<string>();
            public readonly Queue<Func<HttpResponseMessage>> Responses = new Queue<Func<HttpResponseMessage>>();

            public void Enqueue(HttpStatusCode status, string body)
            {
                Responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
            }

            public void EnqueueError(Exception error)
            {
                Responses.Enqueue(() => { throw error; });
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Urls.Add(request.RequestUri.AbsoluteUri);
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Responses.Dequeue()());
            }
        }

        private readonly FakeHandler handler = new FakeHandler();
        private readonly List<TimeSpan> delays = new List<TimeSpan>();

        private SteamHttp Http()
        {
            return new SteamHttp(handler, (d, ct) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task Returns_status_and_body()
        {
            handler.Enqueue(HttpStatusCode.OK, "hello");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.Equal("hello", result.Body);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Retries_once_after_one_second_on_a_server_error()
        {
            handler.Enqueue(HttpStatusCode.InternalServerError, "down");
            handler.Enqueue(HttpStatusCode.OK, "up");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
            Assert.Equal(2, handler.Urls.Count);
            Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
        }

        [Fact]
        public async Task Retries_once_on_a_transport_error_and_then_reports_no_response()
        {
            handler.EnqueueError(new HttpRequestException("no network"));
            handler.EnqueueError(new HttpRequestException("no network"));

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(0, result.StatusCode);
            Assert.Null(result.Body);
            Assert.Equal(2, handler.Urls.Count);
        }

        [Fact]
        public async Task A_timeout_counts_as_a_transport_error()
        {
            handler.EnqueueError(new TaskCanceledException("timed out"));
            handler.Enqueue(HttpStatusCode.OK, "late");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal(200, result.StatusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData((HttpStatusCode)429)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task Does_not_retry_on_client_errors(HttpStatusCode status)
        {
            handler.Enqueue(status, "nope");

            var result = await Http().GetAsync("https://example.invalid/a", CancellationToken.None);

            Assert.Equal((int)status, result.StatusCode);
            Assert.Single(handler.Urls);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Caller_cancellation_throws_and_does_not_retry()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Http().GetAsync("https://example.invalid/a", cts.Token));
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Player_count_client_calls_the_endpoint_and_parses_the_answer()
        {
            handler.Enqueue(HttpStatusCode.OK, @"{""response"":{""player_count"":42,""result"":1}}");

            var outcome = await new SteamPlayerCountsClient(Http()).GetAsync(570, CancellationToken.None);

            Assert.Equal(42, outcome.PlayerCount);
            Assert.Equal("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=570", handler.Urls[0]);
        }

        [Fact]
        public async Task Search_client_escapes_the_term_and_parses_the_answer()
        {
            handler.Enqueue(HttpStatusCode.OK, @"{""items"":[{""type"":""app"",""name"":""Ori & Friends"",""id"":7}]}");
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync("Ori & Friends", CancellationToken.None);

            Assert.Equal(7, outcome.Candidates[0].AppId);
            Assert.Equal("https://store.steampowered.com/api/storesearch/?term=Ori%20%26%20Friends&cc=us&l=english", handler.Urls[0]);
        }

        [Fact]
        public async Task Search_client_reports_rate_limiting()
        {
            handler.Enqueue((HttpStatusCode)429, "");
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync("Prey", CancellationToken.None);

            Assert.Equal(SearchStatus.RateLimited, outcome.Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task Search_client_does_not_call_steam_for_an_empty_term(string term)
        {
            var gate = new MinIntervalGate(TimeSpan.FromSeconds(1.5), new FakeClock(), (d, ct) => Task.CompletedTask);

            var outcome = await new SteamStoreSearchClient(Http(), gate).SearchAsync(term, CancellationToken.None);

            Assert.Equal(SearchStatus.Success, outcome.Status);
            Assert.Empty(outcome.Candidates);
            Assert.Empty(handler.Urls);
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~SteamHttpTests`
Expected: build FAILS, `SteamHttp` and the two clients do not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/SteamHttp.cs`:

```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class HttpResult
    {
        public HttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        // 0 means no response was received (timeout or transport error).
        public int StatusCode { get; }
        public string Body { get; }
    }

    public sealed class SteamHttp : IDisposable
    {
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        private readonly HttpClient client;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;

        public SteamHttp(HttpMessageHandler handler = null, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            client = handler == null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalSteamPlayerCount/1.0");
            this.delay = delay ?? Task.Delay;
        }

        public async Task<HttpResult> GetAsync(string url, CancellationToken ct)
        {
            var first = await TryGetAsync(url, ct).ConfigureAwait(false);
            if (!IsTransient(first.StatusCode))
            {
                return first;
            }

            await delay(RetryDelay, ct).ConfigureAwait(false);
            return await TryGetAsync(url, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            client.Dispose();
        }

        private static bool IsTransient(int statusCode)
        {
            return statusCode == 0 || statusCode >= 500;
        }

        private async Task<HttpResult> TryGetAsync(string url, CancellationToken ct)
        {
            try
            {
                using (var response = await client.GetAsync(url, ct).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new HttpResult((int)response.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient reports its own timeout as a cancellation.
                return new HttpResult(0, null);
            }
            catch (HttpRequestException)
            {
                return new HttpResult(0, null);
            }
        }
    }
}
```

`src/SteamPlayerCount.Core/SteamClients.cs`:

```csharp
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public sealed class SteamPlayerCountsClient : ISteamPlayerCounts
    {
        private const string UrlFormat = "https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={0}";

        private readonly SteamHttp http;

        public SteamPlayerCountsClient(SteamHttp http)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<CountOutcome> GetAsync(int appId, CancellationToken ct)
        {
            var url = string.Format(CultureInfo.InvariantCulture, UrlFormat, appId);
            var result = await http.GetAsync(url, ct).ConfigureAwait(false);
            return SteamResponseParser.ParsePlayerCount(result.StatusCode, result.Body);
        }
    }

    public sealed class SteamStoreSearchClient : ISteamSearch
    {
        private const string UrlFormat = "https://store.steampowered.com/api/storesearch/?term={0}&cc=us&l=english";

        private readonly SteamHttp http;
        private readonly MinIntervalGate gate;

        public SteamStoreSearchClient(SteamHttp http, MinIntervalGate gate)
        {
            this.http = http ?? throw new ArgumentNullException(nameof(http));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public async Task<SearchOutcome> SearchAsync(string term, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return SearchOutcome.Success(null);
            }

            await gate.WaitAsync(ct).ConfigureAwait(false);
            var url = string.Format(CultureInfo.InvariantCulture, UrlFormat, Uri.EscapeDataString(term));
            var result = await http.GetAsync(url, ct).ConfigureAwait(false);
            return SteamResponseParser.ParseSearch(result.StatusCode, result.Body);
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~SteamHttpTests`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "Add Steam HTTP wrapper and endpoint clients"
```

---

### Task 9: MatchFile

**Files:**
- Create: `src/SteamPlayerCount.Core/MatchFile.cs`
- Test: `tests/SteamPlayerCount.Tests/MatchFileTests.cs`

**Interfaces:**
- Consumes: `MatchStore` (`Snapshot`, `Changed`, constructor with initial entries), `MatchEntry`, `MatchSource`, `IClock`, `Json`.
- Produces: `sealed class MatchFile : IDisposable` with constructor `(string directory, IClock clock, Action<Exception, string> logError = null)`, `const string FileName = "matches.json"`, `const int CurrentSchemaVersion = 1`, `string FilePath`, `MatchStore Load()`, `void Attach(MatchStore store)`, `bool Save(MatchStore store)`, `void Flush()`, `void Dispose()` (flushes).

File shape:

```json
{"SchemaVersion":1,"Entries":[{"GameId":"3f2b...","AppId":480490,"SteamName":"Prey","Source":2,"CheckedUtc":"2026-10-05T12:00:00.0000000Z"}]}
```

- [ ] **Step 1: Write the failing test**

`tests/SteamPlayerCount.Tests/MatchFileTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~MatchFileTests`
Expected: build FAILS, `MatchFile` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SteamPlayerCount.Core/MatchFile.cs`:

```csharp
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
            lock (sync)
            {
                pending = true;
            }

            var current = timer;
            if (current != null)
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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SteamPlayerCount.Tests --filter FullyQualifiedName~MatchFileTests`
Expected: PASS, 0 failed.

- [ ] **Step 5: Run the whole suite and commit**

Run: `dotnet test tests/SteamPlayerCount.Tests`
Expected: PASS, 0 failed.

```powershell
git add src tests
git commit -m "Add match file persistence with corrupt-file handling"
```

---

### Task 10: Plugin shell with settings, selection watcher and top panel item

There is no unit test in this task: everything here is Playnite wiring, and `Playnite.SDK` types cannot run outside Playnite. The deliverable is a clean build. It is checked in Playnite in Task 13.

**Files:**
- Create: `src/UniversalSteamPlayerCount/UniversalSteamPlayerCount.csproj`
- Create: `src/UniversalSteamPlayerCount/extension.yaml`, `src/UniversalSteamPlayerCount/icon.png`
- Create: `src/UniversalSteamPlayerCount/Localization/en_US.xaml`
- Create: `src/UniversalSteamPlayerCount/PluginSettings.cs`, `PluginSettingsView.xaml`, `PluginSettingsView.xaml.cs`
- Create: `src/UniversalSteamPlayerCount/PluginServices.cs`, `GameInfoFactory.cs`, `SteamLinks.cs`, `SelectionWatcher.cs`, `UniversalSteamPlayerCountPlugin.cs`

**Interfaces:**
- Consumes: everything in `SteamPlayerCount.Core`.
- Produces:
  - `class PluginSettings : ObservableObject` with `bool EnableNonSteamMatching`, `bool ShowTopPanelItem`, `bool EnableThemeControl` (all default `true`), `[DontSerialize] bool PlayerCountAvailable`
  - `class PluginSettingsViewModel : ObservableObject, ISettings` with `PluginSettings Settings`
  - `sealed class PluginServices : IDisposable` with `MatchStore Store`, `AppIdResolver Resolver`, `PlayerCountService Counts`, `ISteamSearch Search`, `IClock Clock`
  - `static GameInfo GameInfoFactory.From(Game game)`
  - `static void SteamLinks.OpenGraphs(int appId, ILogger logger)`
  - `sealed class SelectionWatcher` with `OnSelectionChanged(IList<Game> games)`, `Refresh()`, `Stop()`
  - `class UniversalSteamPlayerCountPlugin : GenericPlugin` with `const string SourceName = "SteamPlayerCount"`, `const string PlayerCountControlName = "PlayerCountControl"`, `PluginSettingsViewModel SettingsViewModel`, `PluginServices Services`, `void OnMatchesChanged()`, `void OnSettingsSaved()`

- [ ] **Step 1: Write the project file and add it to the solution**

`src/UniversalSteamPlayerCount/UniversalSteamPlayerCount.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWPF>true</UseWPF>
    <AssemblyName>UniversalSteamPlayerCount</AssemblyName>
    <RootNamespace>UniversalSteamPlayerCount</RootNamespace>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <!-- Playnite provides Playnite.SDK.dll at runtime; do not ship a copy. -->
    <PackageReference Include="PlayniteSDK" Version="6.17.0" ExcludeAssets="runtime" />
    <ProjectReference Include="..\SteamPlayerCount.Core\SteamPlayerCount.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Playnite loads localization files from disk; they must not be compiled into the assembly. -->
    <Page Remove="Localization\**\*.xaml" />
    <None Include="Localization\**\*.xaml" CopyToOutputDirectory="PreserveNewest" />
    <None Update="extension.yaml" CopyToOutputDirectory="PreserveNewest" />
    <None Update="icon.png" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

```powershell
dotnet sln add src/UniversalSteamPlayerCount/UniversalSteamPlayerCount.csproj
```

- [ ] **Step 2: Write the manifest and generate the icon**

`src/UniversalSteamPlayerCount/extension.yaml`:

```yaml
Id: UniversalSteamPlayerCount_aa33d12d-49ed-42b6-86eb-d96efe1ccbb3
Name: Universal Steam Player Count
Author: Kyerstorm
Version: 1.0.0
Module: UniversalSteamPlayerCount.dll
Type: GenericPlugin
Icon: icon.png
```

Generate a plain 128x128 placeholder icon (the user can replace the file later):

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 128, 128
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::FromArgb(27, 40, 56))
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(102, 192, 244))
$g.FillEllipse($brush, 44, 18, 40, 40)
$g.FillPie($brush, 24, 62, 80, 96, 180, 180)
$g.Dispose()
$bmp.Save("$PWD\src\UniversalSteamPlayerCount\icon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
```

Expected: `src/UniversalSteamPlayerCount/icon.png` exists and opens as an image.

- [ ] **Step 3: Write the localization file**

`src/UniversalSteamPlayerCount/Localization/en_US.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">
    <sys:String x:Key="LOCSteamPlayerCountName">Steam Player Count</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountTopPanelTitle">{0} playing on Steam: {1}</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountSettingEnableNonSteamMatching">Show Steam player counts for games from other libraries (GOG, Epic and so on)</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountSettingShowTopPanelItem">Show the player count in the top panel</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountSettingEnableThemeControl">Show the player count in themes that support it</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountMenuMatch">Match to Steam game…</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountMenuExclude">Mark as not on Steam</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountMenuReset">Reset Steam match</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountMenuBulk">Match unmatched games</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountMatchDialogCaption">Choose the Steam game for {0}</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountBulkProgress">Matching games to Steam…</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountBulkResult">Steam matching finished: {0} matched, {1} not found, {2} failed.</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountBulkRateLimited">Steam matching stopped because Steam asked for fewer requests. {0} matched, {1} not found, {2} failed. Run it again later.</sys:String>
    <sys:String x:Key="LOCSteamPlayerCountBulkNothing">There are no unmatched games.</sys:String>
</ResourceDictionary>
```

- [ ] **Step 4: Write the settings classes and view**

`src/UniversalSteamPlayerCount/PluginSettings.cs`:

```csharp
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace UniversalSteamPlayerCount
{
    // ObservableObject lives in System.Collections.Generic in Playnite.SDK 6.17.
    public class PluginSettings : ObservableObject
    {
        private bool enableNonSteamMatching = true;
        private bool showTopPanelItem = true;
        private bool enableThemeControl = true;
        private bool playerCountAvailable;

        public bool EnableNonSteamMatching
        {
            get { return enableNonSteamMatching; }
            set
            {
                enableNonSteamMatching = value;
                OnPropertyChanged(nameof(EnableNonSteamMatching));
            }
        }

        public bool ShowTopPanelItem
        {
            get { return showTopPanelItem; }
            set
            {
                showTopPanelItem = value;
                OnPropertyChanged(nameof(ShowTopPanelItem));
            }
        }

        public bool EnableThemeControl
        {
            get { return enableThemeControl; }
            set
            {
                enableThemeControl = value;
                OnPropertyChanged(nameof(EnableThemeControl));
            }
        }

        // Runtime state for theme bindings; never saved.
        [DontSerialize]
        public bool PlayerCountAvailable
        {
            get { return playerCountAvailable; }
            set
            {
                playerCountAvailable = value;
                OnPropertyChanged(nameof(PlayerCountAvailable));
            }
        }
    }

    public class PluginSettingsViewModel : ObservableObject, ISettings
    {
        private readonly UniversalSteamPlayerCountPlugin plugin;
        private PluginSettings editingClone;
        private PluginSettings settings;

        public PluginSettingsViewModel(UniversalSteamPlayerCountPlugin plugin)
        {
            this.plugin = plugin;
            // LoadPluginSettings returns null on first run.
            Settings = plugin.LoadPluginSettings<PluginSettings>() ?? new PluginSettings();
        }

        public PluginSettings Settings
        {
            get { return settings; }
            set
            {
                settings = value;
                OnPropertyChanged(nameof(Settings));
            }
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsSaved();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
```

`src/UniversalSteamPlayerCount/PluginSettingsView.xaml`:

```xml
<UserControl x:Class="UniversalSteamPlayerCount.PluginSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="20">
        <CheckBox IsChecked="{Binding Settings.EnableNonSteamMatching}"
                  Content="{DynamicResource LOCSteamPlayerCountSettingEnableNonSteamMatching}" />
        <CheckBox Margin="0,10,0,0"
                  IsChecked="{Binding Settings.ShowTopPanelItem}"
                  Content="{DynamicResource LOCSteamPlayerCountSettingShowTopPanelItem}" />
        <CheckBox Margin="0,10,0,0"
                  IsChecked="{Binding Settings.EnableThemeControl}"
                  Content="{DynamicResource LOCSteamPlayerCountSettingEnableThemeControl}" />
    </StackPanel>
</UserControl>
```

`src/UniversalSteamPlayerCount/PluginSettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace UniversalSteamPlayerCount
{
    public partial class PluginSettingsView : UserControl
    {
        public PluginSettingsView()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 5: Write the composition root and small helpers**

`src/UniversalSteamPlayerCount/PluginServices.cs`:

```csharp
using System;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public sealed class PluginServices : IDisposable
    {
        private static readonly TimeSpan SearchGap = TimeSpan.FromSeconds(1.5);

        private readonly SteamHttp http;
        private readonly MatchFile matchFile;

        public PluginServices(string dataPath, Func<bool> nonSteamMatchingEnabled, Action<Exception, string> logError)
        {
            Clock = new SystemClock();
            http = new SteamHttp();
            matchFile = new MatchFile(dataPath, Clock, logError);
            Store = matchFile.Load();
            matchFile.Attach(Store);
            Search = new SteamStoreSearchClient(http, new MinIntervalGate(SearchGap, Clock));
            Resolver = new AppIdResolver(Store, Search, Clock, nonSteamMatchingEnabled);
            Counts = new PlayerCountService(Resolver, new SteamPlayerCountsClient(http), Clock);
        }

        public IClock Clock { get; }
        public MatchStore Store { get; }
        public ISteamSearch Search { get; }
        public AppIdResolver Resolver { get; }
        public PlayerCountService Counts { get; }

        public void Dispose()
        {
            matchFile.Dispose();
            http.Dispose();
        }
    }
}
```

`src/UniversalSteamPlayerCount/GameInfoFactory.cs`:

```csharp
using System.Linq;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public static class GameInfoFactory
    {
        public static GameInfo From(Game game)
        {
            var links = game.Links == null ? null : game.Links.Where(l => l != null).Select(l => l.Url).ToList();
            return new GameInfo(game.Id, game.Name, game.PluginId, game.GameId, links);
        }
    }
}
```

`src/UniversalSteamPlayerCount/SteamLinks.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Globalization;
using Playnite.SDK;

namespace UniversalSteamPlayerCount
{
    public static class SteamLinks
    {
        public static void OpenGraphs(int appId, ILogger logger)
        {
            if (appId <= 0)
            {
                return;
            }

            var url = string.Format(CultureInfo.InvariantCulture, "https://steamdb.info/app/{0}/graphs/", appId);
            try
            {
                Process.Start(url);
            }
            catch (Exception e)
            {
                logger.Error(e, "Could not open " + url);
            }
        }
    }
}
```

- [ ] **Step 6: Write the selection watcher**

`src/UniversalSteamPlayerCount/SelectionWatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    // Turns selection changes into one debounced player-count request. UI thread only.
    public sealed class SelectionWatcher
    {
        public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(700);

        private readonly Dispatcher dispatcher;
        private readonly Func<PlayerCountService> service;
        private readonly Action<PlayerCountResult> publish;
        private readonly Action<Exception> logError;
        private readonly DispatcherTimer timer;
        private CancellationTokenSource pending;
        private GameInfo current;

        public SelectionWatcher(
            Dispatcher dispatcher,
            Func<PlayerCountService> service,
            Action<PlayerCountResult> publish,
            Action<Exception> logError)
        {
            this.dispatcher = dispatcher;
            this.service = service;
            this.publish = publish;
            this.logError = logError;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Debounce };
            timer.Tick += OnTick;
        }

        public void OnSelectionChanged(IList<Game> games)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => OnSelectionChanged(games)));
                return;
            }

            current = games != null && games.Count == 1 && games[0] != null ? GameInfoFactory.From(games[0]) : null;
            Restart();
        }

        // Re-runs the request for the current selection, for example after a manual match.
        public void Refresh()
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(Refresh));
                return;
            }

            Restart();
        }

        public void Stop()
        {
            timer.Stop();
            CancelPending();
        }

        private void Restart()
        {
            timer.Stop();
            CancelPending();
            publish(PlayerCountResult.None);
            if (current != null)
            {
                timer.Start();
            }
        }

        private void CancelPending()
        {
            if (pending != null)
            {
                pending.Cancel();
                pending = null;
            }
        }

        private async void OnTick(object sender, EventArgs e)
        {
            timer.Stop();
            var game = current;
            if (game == null)
            {
                return;
            }

            CancelPending();
            var mine = new CancellationTokenSource();
            pending = mine;
            try
            {
                var result = await service().GetAsync(game, mine.Token);
                // The selection may have moved on while the request was running.
                if (mine.IsCancellationRequested || !ReferenceEquals(game, current))
                {
                    return;
                }

                publish(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logError(ex);
            }
        }
    }
}
```

- [ ] **Step 7: Write the plugin class**

`src/UniversalSteamPlayerCount/UniversalSteamPlayerCountPlugin.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public class UniversalSteamPlayerCountPlugin : GenericPlugin
    {
        public const string SourceName = "SteamPlayerCount";
        public const string PlayerCountControlName = "PlayerCountControl";

        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly Lazy<PluginServices> services;
        private SelectionWatcher watcher;
        private TopPanelItem topPanelItem;
        private TextBlock topPanelText;
        private PlayerCountResult lastResult = PlayerCountResult.None;

        public UniversalSteamPlayerCountPlugin(IPlayniteAPI api) : base(api)
        {
            SettingsViewModel = new PluginSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = SourceName,
                ElementList = new List<string> { PlayerCountControlName },
            });

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = SourceName,
                SettingsRoot = nameof(SettingsViewModel) + "." + nameof(PluginSettingsViewModel.Settings),
            });

            services = new Lazy<PluginServices>(() => new PluginServices(
                GetPluginUserDataPath(),
                () => SettingsViewModel.Settings.EnableNonSteamMatching,
                (e, message) => logger.Error(e, message)));
        }

        public override Guid Id { get; } = Guid.Parse("aa33d12d-49ed-42b6-86eb-d96efe1ccbb3");

        public PluginSettingsViewModel SettingsViewModel { get; }

        public PluginServices Services
        {
            get { return services.Value; }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                watcher = new SelectionWatcher(
                    PlayniteApi.MainView.UIDispatcher,
                    () => Services.Counts,
                    Publish,
                    e => logger.Error(e, "Could not get the Steam player count"));

                // Read matches.json off the UI thread before the first selection needs it.
                Task.Run(() =>
                {
                    try
                    {
                        var warm = Services;
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Could not start Steam Player Count services");
                    }
                });
            }
            catch (Exception e)
            {
                logger.Error(e, "OnApplicationStarted failed");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.Stop();
                }

                if (services.IsValueCreated)
                {
                    services.Value.Dispose();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "OnApplicationStopped failed");
            }
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            try
            {
                if (watcher != null)
                {
                    watcher.OnSelectionChanged(args.NewValue);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "OnGameSelected failed");
            }
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            if (topPanelItem == null)
            {
                topPanelText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                topPanelItem = new TopPanelItem
                {
                    Icon = topPanelText,
                    Visible = false,
                    Activated = () => SteamLinks.OpenGraphs(lastResult.AppId, logger),
                };
                Publish(lastResult);
            }

            yield return topPanelItem;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PluginSettingsView();
        }

        // Called after a match was set, cleared or excluded by the user.
        public void OnMatchesChanged()
        {
            if (watcher != null)
            {
                watcher.Refresh();
            }
        }

        public void OnSettingsSaved()
        {
            if (watcher != null)
            {
                watcher.Refresh();
            }
        }

        private void Publish(PlayerCountResult result)
        {
            lastResult = result ?? PlayerCountResult.None;
            if (topPanelItem == null)
            {
                return;
            }

            var show = lastResult.HasCount && SettingsViewModel.Settings.ShowTopPanelItem;
            if (show)
            {
                var count = lastResult.PlayerCount.ToString("N0", CultureInfo.CurrentCulture);
                topPanelText.Text = count;
                topPanelItem.Title = string.Format(
                    PlayniteApi.Resources.GetString("LOCSteamPlayerCountTopPanelTitle"),
                    count,
                    lastResult.SteamName);
            }

            topPanelItem.Visible = show;
        }
    }
}
```

- [ ] **Step 8: Build**

Run: `dotnet build UniversalSteamPlayerCount.sln -c Debug`
Expected: `Build succeeded`, 0 errors. `src/UniversalSteamPlayerCount/bin/Debug/` contains `UniversalSteamPlayerCount.dll`, `SteamPlayerCount.Core.dll`, `extension.yaml`, `icon.png` and `Localization/en_US.xaml`, and does **not** contain `Playnite.SDK.dll`.

If the build reports that a Playnite member does not exist, check it with `dump-sdk-api.ps1` (see Global Constraints) and fix the call; do not guess.

- [ ] **Step 9: Run the tests and commit**

Run: `dotnet test tests/SteamPlayerCount.Tests`
Expected: PASS, 0 failed.

```powershell
git add UniversalSteamPlayerCount.sln src
git commit -m "Add plugin shell with settings, selection watcher and top panel item"
```

---

### Task 11: Theme element PlayerCountControl

No unit test: this is a WPF control hosted by Playnite. The deliverable is a clean build; behaviour is checked in Task 13.

**Files:**
- Create: `src/UniversalSteamPlayerCount/PlayerCountControl.xaml`, `src/UniversalSteamPlayerCount/PlayerCountControl.xaml.cs`
- Modify: `src/UniversalSteamPlayerCount/UniversalSteamPlayerCountPlugin.cs` (add the `GetGameViewControl` override)

**Interfaces:**
- Consumes: `PlayerCountService.GetAsync`, `GameInfoFactory.From`, `SteamLinks.OpenGraphs`, `PluginSettingsViewModel`, `SelectionWatcher.Debounce`, `UniversalSteamPlayerCountPlugin.PlayerCountControlName`.
- Produces: `PlayerCountControl(IPlayniteAPI api, PluginSettingsViewModel settings, Func<PlayerCountService> service, ILogger logger)`; themes host it as `<ContentControl x:Name="SteamPlayerCount_PlayerCountControl" />`.

- [ ] **Step 1: Write the XAML**

`src/UniversalSteamPlayerCount/PlayerCountControl.xaml`:

```xml
<pn:PluginUserControl x:Class="UniversalSteamPlayerCount.PlayerCountControl"
                      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:pn="clr-namespace:Playnite.SDK.Controls;assembly=Playnite.SDK">
    <!-- No colors, fonts or sizes here: the hosting theme styles the button. -->
    <Button Visibility="{Binding ControlVisibility}"
            Command="{Binding OpenSteamDbCommand}"
            Content="{Binding PlayerCountText}" />
</pn:PluginUserControl>
```

- [ ] **Step 2: Write the code-behind**

`src/UniversalSteamPlayerCount/PlayerCountControl.xaml.cs`:

```csharp
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public partial class PlayerCountControl : PluginUserControl, INotifyPropertyChanged
    {
        private readonly IPlayniteAPI api;
        private readonly PluginSettingsViewModel settings;
        private readonly Func<PlayerCountService> service;
        private readonly ILogger logger;
        private readonly DispatcherTimer timer;
        private readonly DesktopView viewAtCreation;
        private CancellationTokenSource pending;
        private GameInfo current;
        private int appId;
        private string playerCountText = string.Empty;
        private Visibility controlVisibility = Visibility.Collapsed;

        public PlayerCountControl(IPlayniteAPI api, PluginSettingsViewModel settings, Func<PlayerCountService> service, ILogger logger)
        {
            InitializeComponent();
            this.api = api;
            this.settings = settings;
            this.service = service;
            this.logger = logger;
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                viewAtCreation = api.MainView.ActiveDesktopView;
            }

            OpenSteamDbCommand = new RelayCommand(() => SteamLinks.OpenGraphs(appId, logger));
            DataContext = this;

            timer = new DispatcherTimer { Interval = SelectionWatcher.Debounce };
            timer.Tick += OnTick;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public RelayCommand OpenSteamDbCommand { get; }

        public string PlayerCountText
        {
            get { return playerCountText; }
            set
            {
                playerCountText = value;
                RaisePropertyChanged(nameof(PlayerCountText));
            }
        }

        public Visibility ControlVisibility
        {
            get { return controlVisibility; }
            set
            {
                controlVisibility = value;
                RaisePropertyChanged(nameof(ControlVisibility));
            }
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            try
            {
                timer.Stop();
                CancelPending();

                // GameContextChanged also fires for controls in views that are not on screen.
                if (api.ApplicationInfo.Mode == ApplicationMode.Desktop && viewAtCreation != api.MainView.ActiveDesktopView)
                {
                    return;
                }

                Show(PlayerCountResult.None);
                current = newContext == null || !settings.Settings.EnableThemeControl ? null : GameInfoFactory.From(newContext);
                if (current != null)
                {
                    timer.Start();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "GameContextChanged failed");
            }
        }

        private async void OnTick(object sender, EventArgs e)
        {
            timer.Stop();
            var game = current;
            if (game == null)
            {
                return;
            }

            CancelPending();
            var mine = new CancellationTokenSource();
            pending = mine;
            try
            {
                var result = await service().GetAsync(game, mine.Token);
                if (mine.IsCancellationRequested || !ReferenceEquals(game, current))
                {
                    return;
                }

                Show(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not get the Steam player count");
            }
        }

        private void Show(PlayerCountResult result)
        {
            appId = result.HasCount ? result.AppId : 0;
            PlayerCountText = result.HasCount ? result.PlayerCount.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
            ControlVisibility = result.HasCount ? Visibility.Visible : Visibility.Collapsed;
            settings.Settings.PlayerCountAvailable = result.HasCount;
        }

        private void CancelPending()
        {
            if (pending != null)
            {
                pending.Cancel();
                pending = null;
            }
        }

        private void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
```

- [ ] **Step 3: Return the control from the plugin**

In `src/UniversalSteamPlayerCount/UniversalSteamPlayerCountPlugin.cs`, add this method directly after `GetTopPanelItems`:

```csharp
        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            try
            {
                if (args.Name == PlayerCountControlName)
                {
                    return new PlayerCountControl(PlayniteApi, SettingsViewModel, () => Services.Counts, logger);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "GetGameViewControl failed");
            }

            return null;
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build UniversalSteamPlayerCount.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src
git commit -m "Add theme element that shows the Steam player count"
```

---

### Task 12: Game menu and main menu actions

No unit test: the logic these actions call (`MatchStore`, `AppIdResolver.NeedsSearch`, `ResolveAsync`) is already covered in Tasks 3 and 5. The deliverable is a clean build; behaviour is checked in Task 13.

**Files:**
- Create: `src/UniversalSteamPlayerCount/MatchActions.cs`
- Modify: `src/UniversalSteamPlayerCount/UniversalSteamPlayerCountPlugin.cs` (add `GetGameMenuItems` and `GetMainMenuItems`)

**Interfaces:**
- Consumes: `PluginServices` (`Store`, `Resolver`, `Search`, `Clock`), `GameInfoFactory.From`, `StrictNameMatcher.AppType`, `UniversalSteamPlayerCountPlugin.OnMatchesChanged`.
- Produces: `sealed class MatchActions(IPlayniteAPI api, Func<PluginServices> services, Action afterChange, ILogger logger)` with `List<GameMenuItem> GameMenuItems`, `List<MainMenuItem> MainMenuItems` (both built once in the constructor).

- [ ] **Step 1: Write the actions**

`src/UniversalSteamPlayerCount/MatchActions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SteamPlayerCount.Core;

namespace UniversalSteamPlayerCount
{
    public sealed class MatchActions
    {
        public const string BulkNotificationId = "SteamPlayerCount-bulk-match";

        private readonly IPlayniteAPI api;
        private readonly Func<PluginServices> services;
        private readonly Action afterChange;
        private readonly ILogger logger;

        public MatchActions(IPlayniteAPI api, Func<PluginServices> services, Action afterChange, ILogger logger)
        {
            this.api = api;
            this.services = services;
            this.afterChange = afterChange;
            this.logger = logger;

            var section = Loc("LOCSteamPlayerCountName");
            GameMenuItems = new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuMatch"),
                    Action = a => Guarded(() => MatchManually(a.Games)),
                },
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuExclude"),
                    Action = a => Guarded(() => Exclude(a.Games)),
                },
                new GameMenuItem
                {
                    MenuSection = section,
                    Description = Loc("LOCSteamPlayerCountMenuReset"),
                    Action = a => Guarded(() => Reset(a.Games)),
                },
            };

            MainMenuItems = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    MenuSection = "@" + section,
                    Description = Loc("LOCSteamPlayerCountMenuBulk"),
                    Action = a => Guarded(BulkMatch),
                },
            };
        }

        public List<GameMenuItem> GameMenuItems { get; }

        public List<MainMenuItem> MainMenuItems { get; }

        private string Loc(string key)
        {
            return api.Resources.GetString(key);
        }

        private void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                logger.Error(e, "Steam Player Count menu action failed");
            }
        }

        private void MatchManually(List<Game> games)
        {
            if (games == null || games.Count == 0 || games[0] == null)
            {
                return;
            }

            // One game at a time: each needs its own choice.
            var game = games[0];
            var chosen = api.Dialogs.ChooseItemWithSearch(
                new List<GenericItemOption>(),
                SearchOptions,
                game.Name,
                string.Format(Loc("LOCSteamPlayerCountMatchDialogCaption"), game.Name));

            int appId;
            if (chosen == null ||
                !int.TryParse(chosen.Description, NumberStyles.None, CultureInfo.InvariantCulture, out appId))
            {
                return;
            }

            var s = services();
            s.Store.Set(game.Id, new MatchEntry
            {
                AppId = appId,
                SteamName = chosen.Name,
                Source = MatchSource.Manual,
                CheckedUtc = s.Clock.UtcNow,
            });
            afterChange();
        }

        // Called by the dialog, possibly on the UI thread. Task.Run plus ConfigureAwait(false)
        // throughout the core keeps this blocking wait from deadlocking.
        private List<GenericItemOption> SearchOptions(string term)
        {
            var options = new List<GenericItemOption>();
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    return options;
                }

                var search = services().Search;
                var outcome = Task.Run(() => search.SearchAsync(term, CancellationToken.None)).GetAwaiter().GetResult();
                foreach (var candidate in outcome.Candidates)
                {
                    if (string.Equals(candidate.Type, StrictNameMatcher.AppType, StringComparison.OrdinalIgnoreCase))
                    {
                        options.Add(new GenericItemOption(candidate.Name, candidate.AppId.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Steam search failed for " + term);
            }

            return options;
        }

        private void Exclude(List<Game> games)
        {
            if (games == null)
            {
                return;
            }

            var s = services();
            foreach (var game in games.Where(g => g != null))
            {
                s.Store.Set(game.Id, new MatchEntry
                {
                    AppId = null,
                    SteamName = null,
                    Source = MatchSource.ManualExcluded,
                    CheckedUtc = s.Clock.UtcNow,
                });
            }

            afterChange();
        }

        private void Reset(List<Game> games)
        {
            if (games == null)
            {
                return;
            }

            var s = services();
            foreach (var game in games.Where(g => g != null))
            {
                s.Store.Remove(game.Id);
            }

            afterChange();
        }

        private void BulkMatch()
        {
            var total = 0;
            var matched = 0;
            var notFound = 0;
            var failed = 0;
            var rateLimited = false;

            var options = new GlobalProgressOptions(Loc("LOCSteamPlayerCountBulkProgress"), true) { IsIndeterminate = false };
            api.Dialogs.ActivateGlobalProgress(async progress =>
            {
                var s = services();
                var games = api.Database.Games.Select(GameInfoFactory.From).Where(s.Resolver.NeedsSearch).ToList();
                total = games.Count;
                progress.ProgressMaxValue = games.Count;

                foreach (var game in games)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        break;
                    }

                    progress.Text = game.Name;
                    try
                    {
                        var result = await s.Resolver.ResolveAsync(game, progress.CancelToken).ConfigureAwait(false);
                        if (result.Status == ResolveStatus.RateLimited)
                        {
                            rateLimited = true;
                            break;
                        }

                        if (result.Status == ResolveStatus.Resolved)
                        {
                            matched++;
                        }
                        else if (result.Status == ResolveStatus.NoMatch)
                        {
                            notFound++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        logger.Error(e, "Steam matching failed for " + game.Name);
                    }

                    progress.CurrentProgressValue = matched + notFound + failed;
                }
            }, options);

            string text;
            if (total == 0)
            {
                text = Loc("LOCSteamPlayerCountBulkNothing");
            }
            else
            {
                var format = Loc(rateLimited ? "LOCSteamPlayerCountBulkRateLimited" : "LOCSteamPlayerCountBulkResult");
                text = string.Format(format, matched, notFound, failed);
            }

            api.Notifications.Add(new NotificationMessage(BulkNotificationId, text, NotificationType.Info));
            afterChange();
        }
    }
}
```

- [ ] **Step 2: Wire the menus into the plugin**

In `src/UniversalSteamPlayerCount/UniversalSteamPlayerCountPlugin.cs`:

Add a field next to `watcher`:

```csharp
        private MatchActions actions;
```

Add these methods directly after `GetGameViewControl`:

```csharp
        // Menus are built once, on first use, and returned as-is every time a menu opens.
        private MatchActions Actions
        {
            get
            {
                if (actions == null)
                {
                    actions = new MatchActions(PlayniteApi, () => Services, OnMatchesChanged, logger);
                }

                return actions;
            }
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            try
            {
                return Actions.GameMenuItems;
            }
            catch (Exception e)
            {
                logger.Error(e, "GetGameMenuItems failed");
                return new List<GameMenuItem>();
            }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            try
            {
                return Actions.MainMenuItems;
            }
            catch (Exception e)
            {
                logger.Error(e, "GetMainMenuItems failed");
                return new List<MainMenuItem>();
            }
        }
```

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build UniversalSteamPlayerCount.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/SteamPlayerCount.Tests`
Expected: PASS, 0 failed.

- [ ] **Step 4: Commit**

```powershell
git add src
git commit -m "Add manual match, exclude, reset and bulk match actions"
```

---

### Task 13: README, packaging and checks in Playnite

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: the Release build output.
- Produces: `dist/*.pext` and the list of in-Playnite checks with their results.

- [ ] **Step 1: Write the README**

`README.md`:

````markdown
# Universal Steam Player Count

A Playnite extension that shows how many people are playing the selected game on
Steam right now. It works for Steam games and for games from other libraries
(GOG, Epic and so on) that also exist on Steam.

It is a successor to the player-count part of darklinkpower's
"Steam News and Players Viewer".

## Where the count appears

- **Top panel:** the count for the selected game. Click it to open the game's
  SteamDB charts. Can be turned off in the extension settings.
- **Themes:** a theme can place the count anywhere in its game views.

## How a non-Steam game is matched

In order, the first that applies:

1. A match you chose by hand.
2. A Steam store or community link in the game's links.
3. A saved earlier search result.
4. A search of the Steam store by name. Only an exact name match is accepted,
   and only if exactly one Steam game has that name.

Right-click a game and open **Steam Player Count** to:

- **Match to Steam game…** pick the Steam game yourself.
- **Mark as not on Steam** stop matching this game.
- **Reset Steam match** forget the match and search again.

**Main menu > Extensions > Steam Player Count > Match unmatched games** matches
the whole library in one pass.

## For theme authors

```xml
<ContentControl x:Name="SteamPlayerCount_PlayerCountControl" />
```

The control is a `Button` whose content is the formatted count. It is collapsed
when there is no count. Settings available for bindings:

```xml
{PluginSettings Plugin=SteamPlayerCount, Path=PlayerCountAvailable}
```

## Data

Matches are stored in `matches.json` in the extension's data folder. No Steam
account or API key is used.

## Building

```powershell
dotnet test tests/SteamPlayerCount.Tests
dotnet build UniversalSteamPlayerCount.sln -c Release
```
````

- [ ] **Step 2: Build Release and run the tests**

```powershell
dotnet test tests/SteamPlayerCount.Tests
dotnet build UniversalSteamPlayerCount.sln -c Release
```

Expected: all tests pass; `Build succeeded`; `src/UniversalSteamPlayerCount/bin/Release/` holds `UniversalSteamPlayerCount.dll`, `SteamPlayerCount.Core.dll`, `extension.yaml`, `icon.png`, `Localization/en_US.xaml`.

- [ ] **Step 3: Locate Playnite and pack**

Playnite was not found in the standard install locations on this machine when the plan was written. Ask the user for the folder that contains `Playnite.DesktopApp.exe` and `Toolbox.exe`, then:

```powershell
$Playnite = '<folder the user gave>'
New-Item -ItemType Directory -Force dist | Out-Null
& "$Playnite\Toolbox.exe" pack "$PWD\src\UniversalSteamPlayerCount\bin\Release" "$PWD\dist"
```

Expected: a `.pext` file in `dist/`. If Toolbox reports a manifest problem, fix `extension.yaml` to match its message; do not add fields it did not ask for.

- [ ] **Step 4: Install and check in Playnite**

Install by dragging the `.pext` onto Playnite, restart it, then go through this list and record pass or fail for each line.

1. `extensions.log` in the Playnite data folder has no error mentioning `UniversalSteamPlayerCount`.
2. Add-ons > Extension settings shows "Universal Steam Player Count" with three checkboxes, all ticked, with readable labels (not `LOC…` keys).
3. Select a popular Steam library game: within about a second the top panel shows a number; its tooltip reads "N playing on Steam: <name>".
4. Click the number: the browser opens `https://steamdb.info/app/<appid>/graphs/`.
5. Select a GOG or Epic game that is also on Steam under the same name: a count appears. `matches.json` in the extension data folder now has an entry with `"Source":2`.
6. Select several games at once, then none: the number disappears.
7. Move quickly through ten games with the arrow keys: only the last game's count is shown, and it belongs to that game.
8. Right-click a non-Steam game > Steam Player Count > Match to Steam game…: the dialog opens pre-filled, typing searches, choosing a result shows that game's count.
9. Mark as not on Steam: the count disappears and does not return when the game is reselected.
10. Reset Steam match: the automatic match returns.
11. Main menu > Extensions > Steam Player Count > Match unmatched games: a cancellable progress dialog runs, then a notification gives matched, not found and failed counts. Cancelling mid-way still gives a notification.
12. Settings: untick "Show the player count in the top panel", Save: the number disappears. Tick it again, **Cancel**: still hidden. Tick, Save, restart Playnite: shown again.
13. Settings: untick the non-Steam option: non-Steam games show nothing, Steam games still show a count.
14. Disconnect the network and select games: nothing is shown, nothing pops up, `extensions.log` has no unhandled exception. `matches.json` gains no `"Source":3` entries while offline.
15. Close Playnite within two seconds of making a manual match, reopen: the match is still there.
16. Replace `matches.json` with the text `broken` and start Playnite: it starts normally and a `matches.corrupt-<timestamp>.json` file appears next to it.
17. Add `<ContentControl x:Name="SteamPlayerCount_PlayerCountControl" />` to a copy of a desktop theme's game details view: the count shows as a button there and follows the selection.
18. Switch to Fullscreen mode: note whether the top panel item is visible (the spec lists this as unknown), and confirm nothing errors.

- [ ] **Step 5: Commit**

```powershell
git add README.md
git commit -m "Add README with usage and theme integration notes"
```

Report the result of each of the 18 checks to the user. Any failure is a defect to fix before calling the work finished.

---

## Not verified when this plan was written

These are checked by the tasks named, and are called out here so nobody treats them as settled:

- That an SDK-style `net462` WPF project builds with the .NET 10 SDK and loads as a Playnite plugin (Task 10 build, Task 13 check 1).
- That Playnite sets the settings view's `DataContext` to the `ISettings` object, as the official template relies on (Task 13 check 2 and 12).
- That `OnGameSelected` and `GetTopPanelItems` are called on the UI thread. `SelectionWatcher` marshals to the dispatcher either way; the top panel `TextBlock` is created inside `GetTopPanelItems` for this reason.
- Whether `ChooseItemWithSearch` calls the search function on the UI thread, and whether it accepts an empty initial list (Task 13 check 8).
- Whether a top panel item is shown in Fullscreen mode (Task 13 check 18).
- `{PluginSettings Plugin=SteamPlayerCount, Path=PlayerCountAvailable}` in the README is the documented theme markup form; it was not run (Task 13 check 17 can confirm it).
- Whether `Toolbox.exe pack` needs manifest fields beyond those in `extension.yaml` (Task 13 step 3).
- The request limits of the undocumented `storesearch` endpoint. The 1.5 s gap is one constant in `PluginServices`.
