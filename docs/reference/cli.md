# Reference: `die-launcher` command line

*(The headless driver. The window's own options are in [screens](screens.md).)*

The headless driver over `Launcher.Core` — the same state machine and actions as the desktop launcher, for
scripts, CI and diagnosis.

`art-index` prints the game's textures and atlas sprites (names and sizes, no pixels) as JSON — the catalogue
a server's dashboard offers when a news item's `art` is chosen ([art](../explanation/art.md)).

```
die-launcher <command> [--game <install folder>] [--server <url>] [game arguments…]
```

`--game` defaults to the folder the executable is in (the launcher is meant to live in the game folder).
`--server` overrides the server URL in `settings.json` for this run. Anything else is passed to the game by `play`.

| Command | Does | Network | Exit code |
|---|---|---|---|
| `state` | Fetches the manifest, inspects the install, prints what the launcher would do now | manifest (304 when unchanged) | `0` play · `10` patch · `20` update · `30` not in a game folder · `40` in game · `50` maintenance |
| `patch` | Installs the client patch — both halves: the identity hook + `DieAuth.dll` in the Crib, and the gamepad start-up hook + `DieGamepad.dll` in both clients — and writes `ip.cfg` | manifest; a release download only if the manifest names a payload version other than the embedded one | `0` patched · `1` not · `3` the game is running |
| `play` | Writes `ip.cfg` from the manifest and starts the game with the retail launcher's arguments | manifest | `0` started · `50` the server is in maintenance (nothing started) |
| `update` | Brings the launcher and/or the client patch to the versions the manifest names | release download(s) | `0`; prints "updated launcher started" when it replaced itself |
| `status` | Inspects the client patch only | none | `0` patched · `1` not |
| `restore` | Puts both clients' original `Assembly-CSharp.dll` back and removes every payload the patch installed | none | `0` |

Every command echoes the same lines it appends to `%LocalAppData%\DIE Reanimated\launcher.log`:

```
19:18:56  manifest  https://crib.example.org → Network: 200 — 361 B
19:18:56  state     Patch: getter is the game's original
19:18:56  patch     payload → …\Dead Island Epidemic - Crib_Data\Managed\DieAuth.dll
19:18:57  patch     hooked the identity getter
19:18:57  patch     payload → …\Dead Island Epidemic - Crib_Data\Managed\DieGamepad.dll
19:18:57  patch     hooked the gamepad start-up call in Dead Island Epidemic - Crib_Data
19:18:58  patch     payload → …\Dead Island Epidemic_Data\Managed\DieGamepad.dll
19:18:58  patch     hooked the gamepad start-up call in Dead Island Epidemic_Data
19:18:58  ip.cfg    crib.example.org:1555 written
19:19:08  launch    "Dead Island Epidemic - Crib.exe" -popupwindow   (mode=Borderless, resolution saved=True)
```

## `status` / `patch` output

One line: `<State>  payload=<version|->  backup=<yes|no>  (<detail>)`.

| State | Meaning |
|---|---|
| `NotPatched` | at least one half is not installed — the getter is the game's original, or a client is missing its gamepad hook or payload. The detail says which |
| `Patched` | both halves are in place: the getter calls its payload, and both clients carry the gamepad hook and `DieGamepad.dll` |
| `PatchedPayloadMissing` | the getter calls the payload but `DieAuth.dll` is gone — the game would fail to resolve it; `patch` repairs it |
| `AssemblyMissing` | the Crib's `Assembly-CSharp.dll` was not found — this is not the install it was pointed at. A match client missing its assembly reports `NotPatched` instead, so a partial install is completed rather than refused |

`payload=` is the assembly version of the installed `DieAuth.dll` (major.minor.patch); both payloads ship
from one release and carry one version.

## Self-update under the CLI

`update` replaces the running binary only when that binary is a published launcher executable. Run as
`dotnet die-launcher.dll` (development, CI) it refuses with a clear message rather than renaming the dotnet host.
