# Reference: the client patch

The patch has **two halves**, installed together by one PATCH (or UPDATE) and versioned as one thing:

| Half | Payload | Where | What it does |
|---|---|---|---|
| Identity | `DieAuth.dll` | Crib | makes login present a Steam session ticket the server can validate |
| Gamepad | `DieGamepad.dll` | Crib **and** match client | starts controller support when the client starts ([gamepad](gamepad-patch.md)) |

Both ship from the same release and carry the same version, so `patch.version` in the manifest names both.
An install carrying only one half reports `NotPatched`, and PATCH completes it.

## What the patcher changes

`Assembly-CSharp.dll`, the main managed assembly beside each client, under `…_Data\Managed\`. Two shapes of
edit, and no others:

| Shape | Used by | Effect |
|---|---|---|
| **Replace a property getter's body** | identity | the getter returns what the payload gives it instead of its original value |
| **Insert one `call` before the first instruction** | gamepad | the payload's `Init()` runs first, then the method's original body, untouched |

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
