# Explanation: what happens when you press Play in Steam

Steam runs `Dead Island Epidemic - Launcher.exe` from the game folder with its environment set (that is how the
overlay and the "playing" status attach). Retail's file was a headless stub that started the game at once; ours
opens a window and does five things first, all local except one small request. The order is deliberate. (The
same sequence runs headless as `die-launcher state`; the window is [described here](../reference/screens.md).)

## 1. The manifest — one small request

`GET {server}/api/launcher` with the ETag from last time. Unchanged → `304`, a few hundred bytes; changed →
the new manifest is cached; unreachable → the cached one is used and the status dot says so. Everything the
launcher shows and decides comes from this document ([reference](../reference/manifest.md)). It is fetched
*first* because the next steps depend on what it says is current.

## 2. Where am I

The launcher expects to be **in** the game folder — that is the install ([how-to](../how-to/install-the-launcher.md)),
so the folder it runs from is the install, full stop. If `Dead Island Epidemic - Crib.exe` is not next to it,
the only thing it does is say where it should be (using Steam's own library list to find the path). No
searching, no guessing, no registry keys of its own.

## 3. The client patch — a state, not an action

Both clients' assemblies are inspected ([client patch](../reference/client-patch.md)): is each half's method
edited, are the payloads present, which version? This produces a *state*. Nothing is written here. The player
presses PATCH; the launcher never modifies the game on its own initiative. Steam's *verify integrity* is a
manual act, so a patch only ever disappears because the player removed it — and then the honest response is to
ask again, not to silently put it back.

## 4. The state machine

Exactly one primary action is offered:

| Condition (first match wins) | Action |
|---|---|
| the game is running | **IN GAME** (nothing to do) |
| a newer launcher was downloaded and verified this run | **RESTART** |
| the server answered with `maintenance` and this Steam account is not whitelisted | **MAINTENANCE** (disabled; re-checked every 30 s) |
| the manifest's `launcher.min` is above us, or `launcher.version` is newer | **UPDATE** |
| the client is not patched (or the payload file is missing) | **PATCH** |
| the installed payloads are not the version the manifest names | **UPDATE** |
| otherwise | **PLAY** |

UPDATE and PATCH never launch the game; after either, the state is recomputed and the button flips. PLAY is
always one click. UPDATE does everything the manifest asks for in one press and shows the download as it
happens: a new **launcher** is staged (the swap and restart wait for RESTART, so nothing disappears from under
the player) and a new **client patch** is applied at once — with the launcher when both are due, so RESTART
lands on PLAY; alone, the button simply becomes PLAY.

## 5. PLAY — the retail stub, reproduced

`ip.cfg` is rewritten from the manifest if it differs (so a server move needs no new launcher). Then the game
is started with **exactly** the arguments the retail launcher computed: refuse below 1024 px wide; add
`-popupwindow` when the game's own display-mode setting is borderless or unset; add `-screen-width/-height`
when the game has never saved a resolution; forward the launcher's own arguments. The Crib is a child of the
launcher process, so Steam's environment is inherited. Then the launcher exits. It is not resident: it holds no
connection, no ticket, no token — the game does its own login ([identity](identity.md)).

## UPDATE — policy from the server, bytes from the release

The manifest names a version. The launcher downloads that release's assets from this repository's GitHub
Releases, checks each against the release's `SHA256SUMS`, and only then acts: a new launcher is staged and
RESTART replaces the running file (rename → drop in → restart, the old file cleaned up next start); a new
payload is re-applied through PATCH. The server is never asked for bytes and cannot supply any
([trust model](trust-model.md)).

The payloads of the launcher's *own* release are embedded in the executable, so a plain PATCH works with no
network at all; only a manifest that names a *different* payload version causes a download.
