# Dead Island: Epidemic — Reanimated launcher

The launcher for the **Reanimated** community server of *Dead Island: Epidemic*. It replaces the retail
`Dead Island Epidemic - Launcher.exe` in your Steam install, so Steam's **Play** button keeps working.

> Unofficial fan project. Not affiliated with, endorsed by, or associated with Deep Silver, Stunlock
> Studios, or any original rights holder. All trademarks belong to their respective owners.

## What it does — the whole list

Install: open the release's `DIE-Reanimated-Launcher.zip` and drag the `Dead Island Epidemic - Launcher.exe`
inside it over the retail one in the game folder ([how-to](docs/how-to/install-the-launcher.md)). Then every
time you press Play in Steam:

1. Fetches one small manifest from the server (news, the current versions). Cached; unchanged = a few hundred bytes.
2. If the server names a newer version, the button says **UPDATE**: the release is **downloaded from this
   repository's GitHub Releases** (the button fills as it comes in), verified against the release's
   `SHA256SUMS`, and staged; **RESTART** swaps it in when you press it. A newer client patch is applied in the
   same press. The server only ever names a version; it never serves a binary.
3. Checks the **client patch** (below). Not patched → the button says **PATCH** and lists what it will write; the
   launcher never modifies the game on its own.
4. **PLAY** writes `ip.cfg` in the game folder so the game connects to the community server, starts
   `Dead Island Epidemic - Crib.exe` with the same arguments the retail launcher used, and exits.

The window: Home (news headline, the game's own key art from your install), News (a native reader), Servers
(what is up and being played, by region), Settings (display mode, the patch). [Screens](docs/reference/screens.md).

It does **not** run in the background, does **not** talk to Steam's API, and holds **no** account, token or secret.
Your Steam name shown in the window is read from Steam's own `loginusers.vdf` for display. Your SteamID64 is
sent with the manifest request, so the server can tell whitelisted testers apart during maintenance.

## What the client patch does

Two halves, installed together by one press of PATCH and versioned as one thing.

**Identity** (`DieAuth.dll`, ~10 KB, source in `patches/DieAuth/`). The original game sent Steam an
*encrypted app ticket* that only the original publisher could decrypt — so a community server can't tell who
you are. The patch changes exactly one thing: when the game builds its login request, it asks Steam for a
normal **session ticket** (`ISteamUser::GetAuthSessionTicket`, the call every Steam game makes) and sends
that instead. The server verifies the ticket with Valve. That is how your identity is proven without you
ever entering anything.

**Gamepad** (`DieGamepad.dll`, source in `patches/DieGamepad/`). Controller support: 8-way movement, analog
aim, a stick-driven pointer for menus, and the pad's buttons named in the game's own prompts. It starts when
the client starts, adds an **Input device** row to the game's own options, and defaults to **Keyboard &
Mouse**, under which it does nothing at all. It changes nothing on the wire — a pad player looks exactly
like a mouse player to the server — and is measured not to cost a frame
([reference](docs/reference/gamepad-patch.md)).

What it touches in the game folder:

| File | Change |
|---|---|
| `…_Data/Managed/Assembly-CSharp.dll` (both clients) | one method edited per half; the original is kept as `Assembly-CSharp.dll.bak` |
| `…_Data/Managed/DieAuth.dll` (Crib), `DieGamepad.dll` (both) | the payloads themselves |
| `Gamepad.cfg`, `Gamepad-*.log` | the gamepad payload's settings and log, written next to the game |
| `ip.cfg` | server addresses |
| `Dead Island Epidemic - Launcher.exe` | replaced by this launcher (Steam's *verify integrity* restores the retail file at any time) |

Outside the game folder: `%LocalAppData%\DIE Reanimated\` (cache, settings, log). Nothing else — the complete
inventory is in [docs/reference/files-and-locations.md](docs/reference/files-and-locations.md).

## Verifying a release

Every release is built by the public GitHub Actions workflow in this repository from the tagged commit, with a
build-provenance attestation. A build updates only from the repository that built it (stamped in at build
time; [release artifacts](docs/reference/release-artifacts.md)). To check the file you run is that build:

```
gh attestation verify DIE-Reanimated-Launcher.exe --owner Sawbones-Games
sha256sum -c SHA256SUMS
```

## Documentation

[`docs/`](docs/README.md) — tutorials, how-to guides, reference and explanation (Diátaxis) covering exactly what
this code does: the client patch, the CLI, every file touched, releases, and the trust model.

## Building

```
dotnet build -c Release
```

`patches/DieAuth` and `patches/DieGamepad` target .NET 3.5 (the runtime the clients load). Neither needs the
game to build: DieAuth references nothing but the base library, and DieGamepad compiles against
`patches/UnityEngine.Stub`, a compile-only facade that is never shipped. `src/Launcher.Patching` is the
Mono.Cecil rewriter,
`src/Launcher.Core` the rest of the behaviour (manifest, ip.cfg, state machine, launch, self-update),
`src/Launcher` the window (Avalonia; four screens over that behaviour, [docs](docs/reference/screens.md)), and
`src/Launcher.Cli` the same behaviour headless (`die-launcher state|patch|play|update|status|restore|art-index`).
`tools/make-icon.py` renders the icon. Tests: `dotnet test`.

## License

MIT — see `LICENSE`. Game assets are never included; the launcher reads artwork from your own installation at runtime.
