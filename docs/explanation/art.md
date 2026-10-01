# Explanation: where the pictures come from

The launcher shows the game's own key art — the survivors' portraits behind the Home screen, the mode cards on
the match tiles — and ships none of it. It reads the pictures out of the player's install each time it starts.

## Why read, not ship

The art belongs to the game's rights holders. The player already has it, on disk, as part of the game they
own; the launcher decoding it for display is the same thing the game does. Shipping copies in the launcher's
binary or fetching them from the community server would be distributing it. So the rule is simple: **anything
from the game is read from the install, at runtime, never copied anywhere** — not into the cache, not into a
release, not onto the server. The launcher's own identity (wordmark, fonts, palette) is ours.

## How

`Launcher.Core/GameArt.cs`. The Crib's data folder holds three Unity 4.6 serialized files
(`resources.assets`, `sharedassets0.assets`, `sharedassets1.assets`, format 9). The reader walks each file's
object table for `Texture2D` objects, reads the small fixed header of each (name, size, format), and decodes
the one it wants from its first mip level. The files use four pixel formats — RGB24, RGBA32, DXT1, DXT5 — and
the reader implements exactly those, about two hundred lines with no dependency: a player-facing binary should
not carry an asset-ripping library for a decorative feature, and the reader can be read in one sitting.

The crib's UI art — every survivor's shop card, all item and weapon icons, the logo — is packed into three NGUI
atlases (`CribAtlas01`, `ShopCardAtlas`, `ItemIconAtlas`, up to 4096²). Their sprite tables are `UIAtlas`
MonoBehaviours with no type tree in this build, so the reader parses them from the raw bytes (name, rect,
rotation per sprite) and cuts a sprite by reading only the rows of the atlas it covers — a 40 KB icon never
decodes 64 MB.

Which textures: the eight `*Survivor_Large` / `*_Large` portraits (one per day of the year, so Home changes
but does not flicker between starts), `GameMode_Big_Scavenger` / `_Scout_Lab` / `_Scout_Club` /
`_Scout_Outpost` for the match cards, and whatever a news item's `art` field names — a texture or
`Atlas#Sprite` — so the manifest can point an item at a picture the player already has instead of serving one.
The names are the game's own; `die-launcher art-index` lists them all, with sizes, as the catalogue the server's
editor offers when an item's art is picked (names only: the server holds no pictures).

Every slot is a different shape (the Home backdrop, a 178×76 tile, a 64×40 thumb, the reader's hero), so a
picture is not scaled to fit but cropped to fill around a **focal point** — `artFocus`, "x,y" in 0–1 — which
stays in view whatever the box. The survivors' portraits, for instance, keep their faces.

## Failure

A missing file, an unexpected format, a truncated object: the reader answers `null` and the screen shows its
palette gradient instead. Nothing is retried, nothing is logged as an error — a launcher run from outside the
install, or against a modified one, simply has no pictures.
