# Reference: the window

`DIE-Reanimated-Launcher.exe` (project `src/Launcher`, Avalonia 11) is one fixed-size window, 1000 × 640, with
its own chrome (no system title bar; the header is the drag handle). Four screens share the same frame.

## Command line

```
DIE-Reanimated-Launcher.exe [--game <install folder>] [--server <url>] [--tab <screen>] [--updated] [game arguments…]
```

| Option | Purpose |
|---|---|
| `--game <dir>` | development: treat `<dir>` as the install instead of the exe's own folder |
| `--server <url>` | development: fetch the manifest and status from `<url>` instead of the setting |
| `--tab home\|news\|servers\|settings` | development: open on that screen |
| `--updated` | passed by `SelfUpdate.Apply` to the freshly swapped-in binary (only logs a line) |
| anything else | forwarded to the game unchanged, as the retail launcher forwarded its own arguments |

Players never pass anything: Steam runs the exe from the game folder with no arguments.

## The frame (every screen)

| Element | Shows | Source |
|---|---|---|
| wordmark + tabs | `DEAD ISLAND EPIDEMIC / REANIMATED`, Home · News · Servers · Settings; the News tab carries the item count | — / manifest `news` |
| `● ONLINE · N` / `● OFFLINE` | whether the crib answered, and how many players are signed in | `/api/launcher/status` |
| identity chip | the Steam persona, avatar and SteamID Steam is signed in as; amber `STEAM IS NOT RUNNING` when it is not | Steam's local files ([files](files-and-locations.md)) |
| three ticks | `CLIENT PATCHED` / `PATCH NEEDED` / `PATCH OUTDATED` · `UP TO DATE` / `UPDATE AVAILABLE` / `UPDATE REQUIRED` · `SERVER REACHED` / `OFFLINE · CACHED` / `NO SERVER` | the state ([how a start proceeds](../explanation/how-a-start-proceeds.md)) |
| the primary button | `PLAY` · `PATCH` · `UPDATE` · `RESTART` · `IN GAME` · `MAINTENANCE` (both disabled); while working: `CHECKING` · `PATCHING` · `UPDATING` · `STARTING` · `RESTORING` · `RESTARTING` | `LauncherState.Action` |
| the download | while UPDATE downloads, the button fills from the left and the detail line counts `DOWNLOADING · 12.3 / 45.0 MB`; nothing else moves | `Releases.Progress` |
| the line under it | `PLAYING AS <persona> · <server>` · `ONE CLICK · THE FILES BELOW ARE WRITTEN` (with the file list) · the update detail · `LAUNCHER x.y.z DOWNLOADED · RESTART TO FINISH` · `START STEAM, THEN PLAY` · an error, in red | — |
| status bar | `Up to date · checked HH:MM` / `Server unreachable · using the last manifest` / `… · no manifest yet`, the version, `DISCORD` (when the manifest names an invite), `LAUNCHER GITHUB` | — |

`Enter` presses the primary button; `Esc` returns to Home. `—` minimises, `✕` closes.

## Home

The newest news item as a headline (kicker, title, summary, *Read the notes*, and *Join the Discord · N online*
when the manifest names an invite — the count comes with the server's status), then up to two more
items as tiles (their images, when they have one) and a *Live now* tile summarising `/api/launcher/status`
(→ Servers), carrying the game's own Scavenger card. With no news published, a fixed welcome line under the
server's name. Behind everything: one of the eight survivors' key art, chosen by the day of the year, read from
the install ([art](../explanation/art.md)); the palette gradient when the install has none.

## News

A list (kind, date, title) and a reader (kind · full date, title, text, `← NEWER` / `OLDER →`). Kinds and their
colours: `PATCH NOTES` red, `EVENT` amber (dated by `eventDate`), `ROSTER` and `COMMUNITY` teal. The reader's
hero is the item's image; its text is the item's Markdown body ([manifest](manifest.md) §Content — `##`
headings become red-bar section titles, bullets get a hanging indent), or the `summary` when there is none. A
body or image is fetched the first time it is named, then read from the cache.

## Servers

Three cards — Crib (online, signed in), Matchmaking (online), In play (players, matches) — a *Match hosts* table
by region (name, matches, players, status; nothing else about a host is shown) and a *Live matches* strip
(the game's mode card, mode, region, players). Read-only: matchmaking decides placement, nothing here is joinable. This screen
re-fetches the status every 15 s while it is open; no other screen polls.

## Settings

| Setting | Effect |
|---|---|
| Install folder | read-only: where the launcher is, and that the game is there |
| Display mode: Borderless · Windowed | writes the game's own `Options_CribDisplayMode` value ([files](files-and-locations.md)); applies on the next PLAY. No Fullscreen: the Crib forces itself windowed every frame once the hub loads, so a fullscreen start ends as an oversized window that crashes on alt-tab — the game's own menu offers only these two |
| Client patch: status line, `RE-APPLY`, `RESTORE ORIGINAL` | `Applied · up to date` / `Applied · update available` / `Not applied` / `Incomplete`; Re-apply runs PATCH again; Restore puts the `.bak` back. Both are disabled while the game runs |
| footer | the launcher version, `OPEN LOG` (the log file in the default editor), `DISCORD` (when set), `LAUNCHER GITHUB` (this repository) |

There is no setting for the server, the cache, updates or the launcher's own window: none of those is
something a player should have to decide. The server URL lives in `settings.json` for development only.

## What the window never does

It never launches the game unpatched, never patches without the button being pressed, never fetches anything
but the four requests in [files and locations](files-and-locations.md), opens nothing but this repository and
the manifest's https Discord invite (in the default browser, on a press), and holds no credential. After PLAY it
exits: the game runs on its own ([identity](../explanation/identity.md)).
