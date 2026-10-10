# Reference: the client patch

The patch has **two halves**, installed together by one PATCH (or UPDATE) and versioned as one thing:

| Half | Payload | Where | What it does |
|---|---|---|---|
| Identity | `DieAuth.dll` | Crib | makes login present a Steam session ticket the server can validate |
| Gamepad | `DieGamepad.dll` | Crib **and** match client | starts controller support when the client starts ([gamepad](gamepad-patch.md)) |

Both ship from the same release and carry the same version, so `patch.version` in the manifest names both.
An install carrying only one half reports `NotPatched`, and PATCH completes it.

The same PATCH also applies **fixes to the game itself** — edits with no payload, see
[Game fixes](#game-fixes).

## What the patcher changes

`Assembly-CSharp.dll`, the main managed assembly beside each client, under `…_Data\Managed\`. Three shapes of
edit, and no others:

| Shape | Used by | Effect |
|---|---|---|
| **Replace a property getter's body** | identity | the getter returns what the payload gives it instead of its original value |
| **Insert one `call` before the first instruction** | gamepad | the payload's `Init()` runs first, then the method's original body, untouched |
| **Edit a method's own code** | game fixes | a few instructions of a game method change, and the fix's own method and fields are added to that type; no payload is involved |

The prologue shape is why several payloads can share one method: each inserts its own call and none replaces
a body, so independent patches coexist. Which methods are edited is not documented here — it is a property
of one build, and it is declared in one place in the code, `ClientPatcher` and `GamepadPatcher.Hooks`.

The getter rewrite reads the field to return out of the *original body* rather than being told its name, and
refuses anything that is not the exact shape it expects. That is a safety property: an assembly the patcher
does not recognise is left alone rather than corrupted.

Nothing else in the assembly is modified. The metadata is re-emitted, so the patched file is usually
slightly smaller than the original.

## Detection

Structural, never remembered: an install is patched when the edited method contains a `call` into the
payload's `Bootstrap`. This is what `status` reports and what makes `patch` idempotent and safe to run
before every start.

## Game fixes

Bugs in the shipped game that no server can work around, fixed by editing the match client's own code. They
carry no payload and no version of their own: they ride the patch release that introduced them, and
`Inspect` asks for PATCH when one could be applied and is not.

| Fix | Client | What was wrong |
|---|---|---|
| **Escape countdown** (`EscapeCountdownPatcher`) | match | In Scavenger, when a team's escape is cancelled (its supplies are stolen back below the win count), the "Team X is winning in N…" countdown stayed on screen for the rest of the match, and every later announcement queued behind it. A regression of the game's own 0.8.4 build, when the announcement moved to the new HUD and lost the `overwrite` that used to clear it |

The escape-countdown fix keeps one countdown banner alive at a time. It times the countdown from when the
client learned of the newest escape, removes the banner when no team is escaping any more, and leaves a
countdown that ends in a win exactly as it was.

**Frame budget (2026-10-08).** The fix adds a field read and a null check per gameplay announcement, plus four
property reads per frame while a countdown banner is up. The live check
(match client, Resort, the DieGamepad `frameStats` sampler, stick held through `Gamepad.inject`) cycled an
escape every 10 s, far more often than a real match, with the unfixed client as control:

| Workload | Arm | Runs | p50 (avg) | `>50ms` | `gc/s` | ~17 ms bursts |
|---|---|---|---|---|---|---|
| escape cycling, empty world | fixed | 5 | 4.5–4.7 ms | 0 | 0–0.1 | in 20 of 30 windows |
| escape cycling, empty world | unfixed | 5 | 4.7–5.0 ms | 0 | 0–0.1 | in 6 of 30 windows |
| walking only, empty world | fixed | 2 | 4.5 ms | 0 | 0–0.1 | in 8 of 12 windows |
| walking only, empty world | unfixed | 2 | 4.4 ms | 0 | 0–0.1 | in 8 of 12 windows |
| escape cycling, 1 s resolution | fixed / unfixed | 1 bursty each | — | 0 | — | 133 / **342** long frames |

Allocation is unchanged at 1.3–1.9 MB/s in both arms. The bursts are frames of one display refresh (16.7–18 ms). They occur
**without the fix and without any escape**. The worst single run was unfixed, and at 1 s resolution its long
frames do not line up with escape events, so they are the test machine's present/compositor and not the
patch. The escape-cycling windows lean toward the fixed arm, but that ordering does not survive the
walking-only or 1 s runs. Accepted for release on these numbers: no frame over 50 ms, no allocation change, and
no cost that separates from the machine's own noise.

A fix is **cosmetic, so it never blocks a patch.** It recognises its target by the exact shape of the
methods it edits. An assembly that does not match (another build, or the fix half-applied by something
else) is reported `NotApplicable` and left untouched, and the rest of the patch goes ahead. The backup
rules are the halves' rules: the first writer makes the `.bak`, and restoring it undoes the fix along with
everything else.

## The identity payload's contract

```csharp
namespace DieAuth
{
    public static class Bootstrap
    {
        public static byte[] Ticket(object client, byte[] original);
    }
}
```

- `original` — the value the getter would have returned.
- Returns a Steam **session ticket** to send instead; **on any failure, returns `original` unchanged**.

A **fresh ticket is minted on every call** — never cached. Steam allows exactly one validation per ticket,
ever, so a cached ticket re-presented later (after a server restart, say) would be met with silence and
rejected. The property is read once per login-type request, so this is one ticket per login.

The payload references only the base class library. It reaches Steam by reflection, finding the Steamworks
wrapper by the *type* of a field rather than by any name, so it does not depend on names that are not
stable between builds. That is also what lets it build in a public CI from a clean checkout with nothing but
the .NET SDK.

The ticket carries the player's SteamID in the clear, but that is the *claim*, not the proof; the signed
section is the proof and only Valve can check it. See [trust-model](../explanation/trust-model.md).

## Files written by `patch`

| Path (under the install) | Content |
|---|---|
| `…_Data\Managed\DieAuth.dll`, `DieGamepad.dll` | the payloads |
| `…_Data\Managed\Assembly-CSharp.dll` | edited as above |
| `…_Data\Managed\Assembly-CSharp.dll.bak` | the pristine original, created once, never overwritten |

`Assembly-CSharp.dll.tmp` exists briefly during a write and is renamed over the original only after the
patcher has closed its read handle. Both halves check for the backup before writing, so the `.bak` always
holds the original file however many times the payloads are updated.

To undo everything, see [restore the original client](../how-to/restore-the-original-client.md).
