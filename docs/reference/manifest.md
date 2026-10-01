# Reference: the server manifest

The one document the launcher fetches per start: `GET {server}/api/launcher`. It is the contract between a
launcher and the server it is configured for. The launcher sends `If-None-Match` with the last ETag it saw and
keeps the last good copy on disk, so an unchanged manifest is a `304` (a few hundred bytes) and an unreachable
server means "use what we have".

The manifest **names versions and addresses. It never carries a binary, nor a URL to one.** Bytes come from the
release named by a version ([release artifacts](release-artifacts.md)).

```jsonc
{
  "v": 1,
  "server": {
    "name": "Reanimated",                       // shown in the launcher
    "requestHost": "crib.example.org",          // the game's login server → ip.cfg <RequestServerIP>
    "requestPort": 1555,                        //                          → <RequestServerPort>
    "matchmakingHostEU": "crib.example.org",    // → <MatchmakingServerIPEU> (defaults to requestHost)
    "matchmakingHostNA": "crib.example.org",    // → <MatchmakingServerIPNA> (defaults to requestHost)
    "matchmakingPort": 2555                     // → <MatchmakingServerPort>
  },
  "launcher": {
    "version": "0.2.0",                         // current release; newer than the running launcher → UPDATE
    "min": "0.1.0"                              // oldest accepted; older than this → UPDATE is the only action
  },
  "patch": {
    "version": "0.2.0",                         // client-patch version (DieAuth.dll + DieGamepad.dll, same release); differs from the installed → UPDATE
    "release": "v0.2.0"                         // optional: the release tag carrying those payloads (default v{version})
  },
  "links": { "discord": "https://discord.gg/…" },  // optional
  "maintenance": { "message": "Back at 18:00 UTC", "allowed": false },  // only while the server is in maintenance
  "news": [
    {
      "id": "2026-09-14-scavenger",
      "kind": "notes",                          // notes | event | roster | community
      "date": "2026-09-14",
      "eventDate": "2026-09-20",                // events only
      "title": "Scavenger is back on the board",
      "summary": "One line for the Home tile.",
      "image": "<sha256>",                      // fetched from /api/launcher/content/{sha256} only when not cached
      "body": "<sha256>"                        // Markdown; same rule
    }
  ]
}
```

Hostnames are allowed everywhere the game accepts them (it resolves DNS).

The launcher adds `?steamId=<SteamID64>`, the account Steam is signed in as (read from Steam's local files, `0`/absent
when unknown). The server uses it for one thing only: during maintenance, `maintenance.allowed` says whether that account
is whitelisted. Outside maintenance the key is absent and the manifest is the same for everyone.

## Status

`GET {server}/api/launcher/status` — small, live, polled only while the Servers screen is open:

```json
{ "crib": true, "matchmaking": true, "online": 14, "inPlay": 10, "matches": 3,
  "regions": [ { "region": "eu", "matches": 2, "players": 7, "online": true } ],
  "live":    [ { "mode": "Scavenger", "region": "eu", "players": 4 } ],
  "maintenance": false }
```

## What the launcher does with it

| Field | Effect |
|---|---|
| `server.*` | written to `ip.cfg` on PATCH and on every PLAY ([files](files-and-locations.md)) |
| `launcher.version` / `min` | the UPDATE state; `min` also disables PLAY |
| `patch.version` | compared with the installed `DieAuth.dll`'s assembly version; the launcher's own embedded payloads (`DieAuth.dll`, `DieGamepad.dll`) are used when it matches, else the named release's. ⚠️ Raise it only once that release exists, or every installed launcher tries to download a tag that is not there |
| `news[]` | newest first: `[0]` is the Home headline, `[1..2]` the tiles, all of them the News screen. `kind` ∈ `notes` · `event` (dated by `eventDate`) · `roster` · `community`. `summary` is the headline text and the reader's fallback |
| `news[].image`, `news[].body` | content hashes — see below. An item without them shows its summary over the palette |
| `news[].art` | art in the player's own install, decoded locally when the item has no `image` ([art](../explanation/art.md)): a texture name (`GameMode_Big_Scavenger`, `BergSurvivor_Large`) or an atlas sprite (`ShopCardAtlas#ShopCard_Berg_Survivor`; `#Sprite` searches every atlas). Nothing is fetched for it |
| `links.discord` | an invite URL (https only); the window shows *Join the Discord* on Home and a `DISCORD` link in the footers. The server, not the launcher, asks Discord for the invite's member/online counts and passes them in the status |
| `maintenance` | present → **MAINTENANCE** replaces PLAY, PATCH and UPDATE unless `allowed` is true (a whitelisted tester sees the usual button). `message` is shown under the button. Honoured only from a manifest the server answered now (`200`/`304`), never from the cache. The launcher re-asks every 30 s while it shows, and once more when PLAY is pressed. The gate is a courtesy: the server's login refuses a non-whitelisted account whatever the launcher shows |
| `news[].artFocus` | `"x,y"` in 0–1 from the top-left: the point of the picture that every crop keeps in view (tiles, thumbs and the reader hero are all different shapes). Default centre |

## Content — `GET {server}/api/launcher/content/{sha256}`

A news image (PNG/JPG/WebP, 16:9) or body (Markdown). The name **is** the SHA-256 of the bytes: the launcher
fetches an item the first time a manifest names it, checks the bytes against the name, keeps them under
`cache\content\<sha256>` for good and never asks again — a hash cannot change meaning, so there is nothing to
revalidate. Bytes that do not match their name are discarded and the item shows without them. The server
answers `Cache-Control: immutable`.

The Markdown a body may use is a subset, rendered natively: `##`/`###` headings, paragraphs, `-` bullet lists,
inline `**bold**`, `*italic*`, `` `code` ``. No HTML, no links, no images: a body cannot make the launcher fetch
or open anything.

All three routes are public and read-only. The server rate-limits them per client and route (one request per
second; content per hash) and answers `429` beyond that; the launcher never needs more.
