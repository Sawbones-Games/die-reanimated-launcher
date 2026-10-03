# Reference: files and locations

Everything the launcher and the patch read or write. If it is not here, they do not touch it.

## In the game install

| Path | Read / write | By | Purpose |
|---|---|---|---|
| `Dead Island Epidemic - Launcher.exe` | — | the player | the launcher itself: the player copies it over the retail file ([install](../how-to/install-the-launcher.md)) |
| `Dead Island Epidemic - Crib.exe` | read (presence) | launcher | what makes a folder "the install"; started by PLAY |
| `Dead Island Epidemic - Crib_Data\Managed\Assembly-CSharp.dll` | rewrite | PATCH | one method edited per half ([client patch](client-patch.md)) |
| `Dead Island Epidemic_Data\Managed\Assembly-CSharp.dll` | rewrite | PATCH | the gamepad half only |
| `…_Data\Managed\Assembly-CSharp.dll.bak` (both clients) | write once | PATCH | pristine original |
| `Dead Island Epidemic - Crib_Data\Managed\DieAuth.dll` · `Dead Island Epidemic - Crib_Data\Managed\DieGamepad.dll` · `Dead Island Epidemic_Data\Managed\DieGamepad.dll` | write | PATCH / UPDATE | the payload |
| `Dead Island Epidemic - Crib_Data\Managed\DieAuth.log` | append | payload | one line per event, see [DieAuth log](dieauth-log.md) |
| `Gamepad.cfg` *(beside the game exe)* | create, then read; one line rewritten on a settings change | payload | the gamepad payload's settings and button layout. Written with defaults on first run, and rewritten when a newer payload changes the file's shape — keeping the player's settings |
| `Gamepad-crib.log`, `Gamepad-match.log` *(beside the game exe)* | write | payload | one per client, truncated at start. Both clients share a folder, so the name carries the client |
| `Gamepad.inject` *(beside the game exe)* | read, only with `allowInject=1` in `Gamepad.cfg` (off by default) | payload | a test seam: pad state from a file instead of the controller. Never created by the launcher or the payload |
| `ip.cfg` | rewrite | PATCH, PLAY | the four server addresses from the manifest in the `public` branch; every other element and branch left as is |
| `Dead Island Epidemic - Crib_Data\resources.assets`, `sharedassets0.assets`, `sharedassets1.assets` | read | window | the game's key art (the survivors' portraits, the mode cards) for the Home backdrop and the match cards — decoded from the install at start, never copied anywhere ([explanation](../explanation/art.md)) |

Assemblies referenced but never modified: `UnityEngine.dll`, `SteamworksManaged.dll` (the rewriter must be
able to *resolve* them when it writes, which is why it searches the `Managed` folder).

## Outside the game install — `%LocalAppData%\DIE Reanimated\`

| Path | Purpose |
|---|---|
| `settings.json` | `serverUrl`; and, for a *dev-channel* build only, `releaseToken` (a GitHub token with read access to the private dev repository, so the build can download its releases — sent to api.github.com and nowhere else) |
| `launcher.log` | what the launcher did, one line per step |
| `cache\manifest.json`, `cache\manifest.etag` | the last good manifest and its ETag |
| `cache\content\<sha256>` | news bodies and images, by hash, verified against their name on arrival, kept forever (a hash never changes meaning) |
| `cache\DieAuth.dll`, `cache\DieGamepad.dll` | the payloads embedded in this launcher build, extracted for PATCH |
| `updates\` | downloaded release assets, verified before use |
| `…\Dead Island Epidemic - Launcher.exe.old` *(in the game folder)* | the previous launcher, briefly, during a self-update; removed on the next start |

Nothing else. In particular no Start-menu entries, no services, no scheduled tasks, no other registry keys than
the two below.

## Registry — `HKCU\Software\Deep Silver\Dead Island: Epidemic`

| Value | Access | Purpose |
|---|---|---|
| `Options_CribDisplayMode*` | read on PLAY; written by Settings → Display mode | the game's own window mode: `0` windowed, `1` borderless (the game's default), `2` fullscreen (never offered for the Crib by the game's menu, and unsupported — the Crib forces windowed once the hub loads). `1`, `2` or absent → the game is started with `-popupwindow`; the retail stub passed it for `1`/absent only, `2` is folded in so a value written by an older launcher build can't strand the window |
| `Screenmanager*` | read on PLAY | present once the game has saved a resolution; absent → the launcher passes the primary screen's size, as the retail launcher did |

And read-only, for display and hints: `HKCU\Software\Valve\Steam\SteamPath`, then Steam's own
`steamapps\libraryfolders.vdf` (to say where the game folder is when the exe is run from elsewhere) and
`ActiveProcess\ActiveUser` (which account Steam is signed in as; with Steam closed, the most recent login in
`config\loginusers.vdf`), `config\loginusers.vdf` (the persona name) and `config\avatarcache\<steamid>.png`
(the avatar) — the identity chip in the window. Whether Steam runs is read from the process list.

## Network

| Request | When | Size |
|---|---|---|
| `GET {server}/api/launcher?steamId=…` with `If-None-Match` | every start | ~0.4 KB when unchanged (`304`) |
| `GET {server}/api/launcher/status` | once at start, then every 15 s only while Servers is open | < 1 KB |
| `GET {server}/api/launcher/content/{sha256}` | a news body or image the cache does not have | that item, once ever |
| `GET github.com/<the repository that built this launcher>/releases/download/v{version}/{asset}` | UPDATE only | the launcher (~45 MB) or the payloads (`DieAuth.dll` ~10 KB, `DieGamepad.dll` ~56 KB) + `SHA256SUMS`. A dev-channel build reaches the same release through `api.github.com` with its token |

That is the complete list. The only thing about the player in any of it is the SteamID64 on the manifest
request, which the server uses for its maintenance whitelist ([manifest](manifest.md)).
