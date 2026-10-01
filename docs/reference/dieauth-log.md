# Reference: the `DieAuth.log` file

The payload writes one line per event to `DieAuth.log` next to itself
(`Dead Island Epidemic - Crib_Data\Managed\`). Format: `HH:mm:ss.fff  message`. Logging never throws into
the game; if the file cannot be written the payload carries on silently.

| Line | Meaning |
|---|---|
| `GetAuthSessionTicket → handle N, L B` | Steam returned ticket handle `N` and `L` bytes. `handle 0` means Steam refused |
| `session ticket #N minted: L B, length prefix <hex>` | the N-th ticket minted this process (one per login-type request); the prefix is the ticket's first 4 bytes, nothing that identifies the player |
| `mint failed — falling back to the game's own buffer` | the walk succeeded but Steam gave no ticket; the game logs in as unpatched |
| a line naming a step of the reflection walk — the Steamworks wrapper, its user object, or the ticket call | that step did not resolve, so no ticket was minted; the game logs in as unpatched |
| `EXCEPTION — falling back …: <trace>` | any other error; same fallback |

Every failure line is followed by the game sending its original buffer. The payload never makes login
impossible; it can only make it verifiable.
