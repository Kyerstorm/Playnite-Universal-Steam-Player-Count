# Universal Steam Player Count: design

Date: 2026-10-05
Status: awaiting review

## Purpose

A Playnite generic extension that shows how many people are playing a game on
Steam right now. It is a successor to the player-count half of darklinkpower's
"Steam News and Players Viewer" (NewsViewer, commit `b47c249`), with one main
addition: games from other libraries (GOG, Epic and so on) are matched to their
Steam equivalent so they get a count too.

Success means: selecting a GOG or Epic game that also exists on Steam shows the
Steam player count without the user doing anything, a wrong or missing match can
be fixed by hand, and the count is visible on any theme.

## Decisions made with the user

| Topic | Decision |
|---|---|
| Display | Theme custom element, plus a top panel item as a theme-independent fallback |
| Matching | Automatic and strict, with a manual fix |
| Match timing | On selection, plus an optional bulk pass from the main menu |
| Architecture | Core library with no Playnite reference, thin plugin project, test project |

Assumptions the user has not explicitly confirmed:

- Name: "Universal Steam Player Count".
- Clicking the count opens `https://steamdb.info/app/<appid>/graphs/`, the same URL NewsViewer opens.
- News is dropped entirely. Only the current player count is shown, no peaks or history.

## Out of scope

Steam news, historical or peak counts, a Steam Web API key, fuzzy name matching,
automatic background scanning of the library, translations other than `en_US`.

## Evidence this design rests on

Observed on 2026-10-05:

- `GET https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=570`
  returns `200` and `{"response":{"player_count":495050,"result":1}}` with no key.
- The same call with an unknown app returns HTTP `404` and `{"response":{"result":42}}`.
- `GET https://store.steampowered.com/api/storesearch/?term=Prey&cc=us&l=english`
  returns JSON `{"total":N,"items":[{"type":"app","name":"Prey","id":480490,...}]}` with no key.
- `ISteamApps/GetAppList/v2` returns `404`, so a local full app list is not an option without a key.
- Playnite SDK 6.17.0.0 (reflection over the local NuGet package): `GenericPlugin`,
  `AddCustomElementSupport`, `AddSettingsSupport`, `GetGameViewControl`,
  `GetTopPanelItems`, `GetGameMenuItems`, `GetMainMenuItems`, `OnGameSelected`,
  `PluginUserControl.GameContextChanged`, `TopPanelItem { Icon, Title, Visible, Activated }`,
  `IDialogsFactory.ChooseItemWithSearch`, `IDialogsFactory.ActivateGlobalProgress`,
  `NotificationType { Info, Error }`, `IMainViewAPI.SelectedGames`, `Game.Links`,
  `Game.PluginId`, `Game.GameId`.
- NewsViewer source: Steam library plugin id `cb91dfc9-b977-43bf-8e70-55f46e410fab`;
  Steam link regex `https?://(?:store\.steampowered|steamcommunity)\.com/app/(\d+)`;
  700 ms selection debounce; 120 s count cache.

## Solution layout

```
UniversalSteamPlayerCount.sln
src/
  SteamPlayerCount.Core/        net462 class library, no Playnite reference
  UniversalSteamPlayerCount/    net462 WPF plugin, references PlayniteSDK 6.17.0 and Core
tests/
  SteamPlayerCount.Tests/       xUnit, references Core only
```

All projects are SDK-style `.csproj` files targeting `net462`. The plugin
references only `PlayniteSDK` and Core. No other NuGet dependency ships with the
plugin.

Revised 2026-10-06 while planning: `Playnite.SDK.dll` references no JSON library,
so `Playnite.SDK.Data.Serialization` only works inside the running Playnite
process and cannot be unit-tested. Core therefore parses JSON itself with the
framework's `DataContractJsonSerializer`. As a result the HTTP wrapper, the two
Steam clients and `MatchFile` (described under "Plugin project" below) live in
Core, where they are tested. Playnite's serializer is used only for plugin
settings.

## Core library

Each unit has one job and no Playnite types.

**`GameNameNormalizer`**: `string Normalize(string name)`. Lower-case invariant,
remove `™ ® ©`, replace `&` with `and`, then remove every character that is not
a letter or digit. "Prey", "PREY®" and "Prey " all become `prey`.
"Prey - Mooncrash" becomes `preymooncrash`. No edition or year stripping.

**`StrictNameMatcher`**: takes the library game name and a list of search
candidates `(appId, name, type)`. Keeps candidates of type `app` whose normalized
name equals the normalized game name. Exactly one such candidate is a match.
Zero or more than one is no match. Ambiguity is never resolved by guessing.

**`MatchEntry`**: `AppId` (nullable int), `SteamName`, `Source`, `CheckedUtc`.

**`MatchSource`** (persisted; values are never renumbered or reused):
`Manual = 1`, `NameSearch = 2`, `NotFound = 3`, `ManualExcluded = 4`.

**`MatchStore`**: in-memory dictionary of Playnite game id to `MatchEntry` with
`TryGet`, `Set`, `Remove` and a `Changed` event. Thread-safe.

**`AppIdResolver`**: decides the AppID for a game from plain inputs (game id,
name, library plugin id, library game id, link URLs). Order, first hit wins.
Steps 3 to 5 are skipped when non-Steam matching is disabled in settings:

1. Store entry with `Manual` → that AppID. Store entry with `ManualExcluded` → none.
2. Game belongs to the Steam library and its id parses as a 32-bit integer → that id.
3. A link URL matches the Steam link regex → that AppID. Not stored; links are cheap to re-read.
4. Store entry with `NameSearch` → that AppID. Store entry with `NotFound` younger than 30 days → none.
5. Search by name through `ISteamSearch`, run
   `StrictNameMatcher`, and store the outcome as `NameSearch` or `NotFound`.
   A failed search (network error) stores nothing.

**`ExpiringCache<TKey, TValue>`**: values with a per-entry lifetime and an
injected clock.

**`MinIntervalGate`**: async gate that enforces a minimum time between calls,
with an injected clock and delay function.

**Interfaces implemented by the plugin**: `ISteamSearch`
(`Task<SearchOutcome> SearchAsync(string term, CancellationToken)`),
`ISteamPlayerCounts` (`Task<CountOutcome> GetAsync(int appId, CancellationToken)`),
`IClock`. Outcomes distinguish success, "no data", and failure, so a network
error is never mistaken for "not on Steam".

**`PlayerCountService`**: the single coordinator. `Task<PlayerCountResult>
GetAsync(GameInfo game, CancellationToken)` resolves the AppID, then returns the
count from the cache or fetches it. Counts are cached per AppID for 120 s; a
"no data" answer is cached for 10 minutes. Concurrent requests for the same
AppID share one fetch.

## Plugin project

**`SteamHttp`**: one shared `HttpClient`, 10 s timeout, a descriptive
`User-Agent`. One retry after 1 s on timeout or 5xx. No retry on 404 or 429.

**`SteamPlayerCountsClient : ISteamPlayerCounts`**: calls
`GetNumberOfCurrentPlayers`. `200` with `result == 1` is a count. `404` or any
other `result` is "no data". Anything else is failure.

**`SteamStoreSearchClient : ISteamSearch`**: calls `storesearch` with
`cc=us&l=english`, behind a `MinIntervalGate` of 1.5 s. `429` is reported as a
distinct "rate limited" failure.

**`MatchFile`**: loads and saves `matches.json` under `GetPluginUserDataPath()`.
The file is `{ "SchemaVersion": 1, "Entries": [ { "GameId": "<game guid>", "AppId": 480490, "SteamName": "Prey", "Source": 2, "CheckedUtc": "<ISO 8601 UTC>" } ] }`.
If an unreadable file cannot be renamed aside, saving is disabled for the session
so the file is never overwritten.
A file that fails to parse is renamed to `matches.corrupt-<yyyyMMddHHmmss>.json`
and an empty store is used. Saves are debounced by 2 s and written to a
temporary file that then replaces the real one. A pending save is flushed in
`OnApplicationStopped`.

**`SelectionWatcher`**: one 700 ms `DispatcherTimer` driven by `OnGameSelected`.
When exactly one game is selected it cancels the previous request, asks
`PlayerCountService`, and publishes the result on the UI dispatcher. The result
is ignored if the selection changed while the request was running. Cancelling
stops a name search and stops the caller waiting; a count fetch already under way
is shared, runs to completion (at most the 10 s timeout) and fills the cache.

**`PlayerCountControl : PluginUserControl`**: theme element. A `Button` whose
content is the formatted count, collapsed when there is no count. It gets its
value from `GameContextChanged` through `PlayerCountService`, with its own
700 ms debounce, and ignores context changes when the active desktop view is not
the one it was created in (the guard NewsViewer uses). Colors and fonts come
from theme resources only.

**Top panel item**: one prebuilt `TopPanelItem`. `Icon` is a `TextBlock` with
the formatted count, `Title` is "<count> playing on Steam: <Steam name>",
`Visible` is false when there is no count or the setting is off. Fed by
`SelectionWatcher`. `Activated` opens the SteamDB graphs page.

**Theme integration**: source name `SteamPlayerCount`, element
`PlayerCountControl`, so themes host
`<ContentControl x:Name="SteamPlayerCount_PlayerCountControl" />`. Settings are
exposed with `AddSettingsSupport`, including a non-persisted
`PlayerCountAvailable` flag for theme visibility bindings.

**Game menu** (section "Steam Player Count", prebuilt, no I/O when the menu opens):

- "Match to Steam game…": `ChooseItemWithSearch`, pre-filled with the game name,
  backed by the store search. Saves a `Manual` entry. One game at a time.
- "Mark as not on Steam": saves `ManualExcluded` for the selected games.
- "Reset Steam match": removes the entries, so automatic matching runs again.

**Main menu** (`MenuSection = "@Steam Player Count"`):

- "Match unmatched games": `ActivateGlobalProgress`, cancellable, over every
  non-Steam game that has no store entry and no Steam link. It stops early on a
  rate-limit response. It ends with one notification, stable id
  `SteamPlayerCount-bulk-match`, giving matched, not found and failed counts.

**Settings** (all default to on):

| Setting | Effect |
|---|---|
| `EnableNonSteamMatching` | Steps 3 to 5 of the resolver run for non-Steam games |
| `ShowTopPanelItem` | The top panel item may become visible |
| `EnableThemeControl` | The theme element may become visible |

**Localization**: every user-visible string is in `Localization/en_US.xaml`
with `LOCSteamPlayerCount…` keys.

**Manifest**: `extension.yaml` with `Id`, `Name`, `Author`, `Version`,
`Module: UniversalSteamPlayerCount.dll`, `Type: GenericPlugin`, `Icon`, `Links`.
The plugin GUID is generated once during scaffolding and never changed.

## Data flow

```
selection change ─► SelectionWatcher (700 ms) ─┐
theme element GameContextChanged (700 ms) ─────┤
                                               ▼
                                     PlayerCountService
                                       │            │
                              AppIdResolver     count cache (per AppID)
                               │       │            │
                         MatchStore  ISteamSearch  ISteamPlayerCounts
                               │
                           MatchFile (matches.json)
```

## Failure behaviour

- Any network, timeout or parse failure: the count is hidden, the exception is
  logged, nothing is stored and nothing is cached, so the next selection retries.
- No dialog is ever opened from background work. Only the bulk pass reports,
  through its single notification.
- No network at all: both surfaces stay hidden. Stored matches are untouched.
- Every method Playnite calls (`OnGameSelected`, `GetGameViewControl`, menu
  actions, `OnApplicationStopped`) catches and logs.

## Testing

xUnit tests, no network:

- `GameNameNormalizer`: symbols, case, punctuation, `&`.
- `StrictNameMatcher`: the recorded "Prey" response selects 480490 and rejects
  the DLC, the demo and "911: Prey"; two equal candidates give no match;
  non-`app` types are ignored.
- `AppIdResolver`: each of the five steps, their order, the 30-day retry, the
  setting that disables matching, and that a failed search stores nothing.
- `PlayerCountService`: cache hit, expiry, "no data" caching, shared in-flight
  fetch, cancellation.
- `MinIntervalGate` and `ExpiringCache` with a fake clock.
- `MatchFile`: round trip, missing file, corrupt file set aside, unknown
  `SchemaVersion` set aside.
- Client DTO parsing against the recorded JSON responses, including the 404 body.

Manual checks in Playnite are listed in the implementation plan.

## Not verified yet

- Whether a top panel item is shown in Fullscreen mode. If it is not,
  Fullscreen relies on the theme element only. To be checked in Playnite.
- Where Playnite is installed on this machine. It was not found in the standard
  locations, and packing and the in-Playnite checks need it.
- Rate limits of `storesearch`. It is undocumented, so the 1.5 s gap is a
  conservative constant in one place.
- That an SDK-style `net462` WPF project builds and loads as a Playnite plugin
  on this machine. To be confirmed by the first build step of the plan.
