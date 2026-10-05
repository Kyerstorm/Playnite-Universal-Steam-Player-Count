# Theme integration

Universal Steam Player Count shows its number inside the theme by default. A
theme supports it by adding one placeholder element to its game views. Without
that placeholder nothing is shown, and the user has to turn on the top panel
fallback in the extension settings.

Status: the markup below follows the same pattern Playnite themes use for other
extensions, and the worked example mirrors markup that exists in the Laylu
theme today. It has not yet been run in a theme with this extension installed.

## Names

| What | Value | Used in |
|---|---|---|
| Element host name | `SteamPlayerCount_PlayerCountControl` | `x:Name` of the `ContentControl` |
| Settings source | `SteamPlayerCount` | `{PluginSettings Plugin=...}` |
| Add-on id | `UniversalSteamPlayerCount_aa33d12d-49ed-42b6-86eb-d96efe1ccbb3` | `{PluginStatus Plugin=...}` |

## Minimal integration

Add this where the count should appear, in `Views/DetailsViewGameOverview.xaml`
and, if the theme has a Grid view side panel, `Views/GridViewGameOverview.xaml`:

```xml
<ContentControl x:Name="SteamPlayerCount_PlayerCountControl" Focusable="False" />
```

That is enough. The element collapses itself when there is no count, so it
takes no space for games that are not on Steam.

## What the element is

- A `Button`. Its content is the current player count, formatted with the
  user's thousands separator (for example `495,050`).
- Clicking it opens `https://steamdb.info/app/<appid>/graphs/` in the browser.
- It sets no colors, fonts or sizes. It picks up the theme's own `Button` style
  and inherits font properties from the `ContentControl` that hosts it.
- It is collapsed while a count is loading and whenever there is none: the game
  has no Steam match, Steam has no data for it, the network failed, or the user
  turned the theme element off.
- It updates about 0.7 seconds after the selected game changes.

## Hiding a caption or container with it

A label or box around the element should disappear together with it. Bind the
container's visibility to the extension's `PlayerCountAvailable` flag:

```xml
<StackPanel>
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{PluginSettings Plugin=SteamPlayerCount, Path=PlayerCountAvailable, FallbackValue=False}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <ContentControl x:Name="SteamPlayerCount_PlayerCountControl" Focusable="False" />
    <TextBlock Text="Playing Now" />
</StackPanel>
```

`FallbackValue=False` keeps the container hidden when the extension is not
installed, so the theme works with or without it.

## Settings a theme can bind to

All through `{PluginSettings Plugin=SteamPlayerCount, Path=<name>}`:

| Path | Type | Meaning |
|---|---|---|
| `PlayerCountAvailable` | bool | A count is being shown for the selected game. Changes at runtime. |
| `EnableThemeControl` | bool | User setting: show the count inside the theme. Default on. |
| `ShowTopPanelItem` | bool | User setting: also show the count in the top panel. Default off. |
| `EnableNonSteamMatching` | bool | User setting: match games from other libraries to Steam. Default on. |

To show something only when the extension is installed:

```xml
Visibility="{PluginStatus Plugin=UniversalSteamPlayerCount_aa33d12d-49ed-42b6-86eb-d96efe1ccbb3, Status=Installed}"
```

## Coming from "Steam News and Players Viewer"

This extension replaces the player-count half of that one. A theme that already
supports it needs two renames:

| NewsViewer | Universal Steam Player Count |
|---|---|
| `x:Name="NewsViewer_PlayersInGameViewerControl"` | `x:Name="SteamPlayerCount_PlayerCountControl"` |
| `{PluginSettings Plugin=NewsViewer, Path=PlayersCountAvailable}` | `{PluginSettings Plugin=SteamPlayerCount, Path=PlayerCountAvailable}` |

Note the flag name: `PlayersCountAvailable` there, `PlayerCountAvailable` here.

The element behaves the same way (a button that shows the number and opens
SteamDB), so no layout change is needed. The difference users see is that games
from GOG, Epic and other libraries now get a count too.

A theme can support both extensions by keeping both blocks. Each hides itself
when its own extension has no count, but a user with both installed would see
the number twice for Steam games.

## Worked example: Laylu

Laylu shows a row of statistics under the game title (score, time to beat,
achievements, players). The players entry is in two files, with identical
markup:

- `theme/Views/DetailsViewGameOverview.xaml`, the block starting at the comment
  `Players online right now (News Viewer)`
- `theme/Views/GridViewGameOverview.xaml`, the same block

Replace that block, in both files, with:

```xml
<!-- Players online right now (Universal Steam Player Count); the extension draws the number -->
<StackPanel Margin="14,0,14,0">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{PluginSettings Plugin=SteamPlayerCount, Path=PlayerCountAvailable, FallbackValue=False}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <ContentControl x:Name="SteamPlayerCount_PlayerCountControl" Focusable="False"
                    HorizontalAlignment="Center"
                    FontSize="{DynamicResource FontSizeLarger}" />
    <TextBlock Text="Playing Now" Style="{StaticResource LayluStatCaption}" />
</StackPanel>
```

Only three things differ from the existing block: the comment, the
`PluginSettings` source and path, and the `x:Name`. `LayluStatCaption` and
`FontSizeLarger` are Laylu's own resources and stay as they are.

Then restart Playnite (themes load at startup) and check:

1. A Steam game shows a number above "Playing Now" in the stat row.
2. A GOG or Epic game that is also on Steam shows one too.
3. A game that is not on Steam shows no "Playing Now" entry and leaves no gap.
4. The same holds in the Grid view side panel.
5. With the extension disabled, the stat row has no "Playing Now" entry and
   `playnite.log` has no XAML error.

## Things to know

- The stat container starts collapsed and is made visible by the flag, while
  the element that sets the flag sits inside it. This works because Playnite
  gives the element its game even while its parent is collapsed. Laylu relies on
  exactly this with NewsViewer.
- `PlayerCountAvailable` is one flag shared by every copy of the element. A
  theme that hosts the element in several views sees the same value in all of
  them.
- In Desktop mode each copy of the element only updates while the view it was
  created in is the active one, so a hidden view does not make requests.
- Fullscreen themes can host the element with the same `x:Name`. That has not
  been tested.
- The element is a `Button`, so it follows the theme's button style. A theme
  that wants the number to look like plain text should style buttons inside
  that `ContentControl` accordingly.
