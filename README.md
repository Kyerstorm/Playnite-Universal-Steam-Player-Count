# Universal Steam Player Count

> [!NOTE]
> **AI disclaimer:** I am not completely confident in this specific extension. I have reviewed everything to my knowledge us fine and i will refine this more as i find or get reported issues. You can treat this as humanely reviewed 

A Playnite extension that shows how many people are playing the selected game on
Steam right now. It works for Steam games and for games from other libraries
(GOG, Epic and so on) that also exist on Steam.

It is a successor to the player-count part of darklinkpower's
"Steam News and Players Viewer".

## Where the count appears

- **Inside the theme (default):** a theme that supports the extension places
  the count in its game views. Click it to open the game's SteamDB charts.
  A theme without support shows nothing; see
  [docs/theme-integration.md](docs/theme-integration.md) to add it.
- **Top panel (off by default):** turn on "Also show the player count in the
  top panel" in the extension settings if your theme does not support the
  extension. It works on any theme.

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

Full guide, with a worked example: [docs/theme-integration.md](docs/theme-integration.md).

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
