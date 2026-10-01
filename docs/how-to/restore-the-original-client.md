# How to restore the original client

**Goal:** put both clients' own `Assembly-CSharp.dll` back and remove every payload the patch installed.

**Preconditions:** the game is not running; `Assembly-CSharp.dll.bak` exists next to each assembly (created
by the first `patch`).

```
die-launcher restore --game "<install folder>"
```

This copies each `.bak` over its `Assembly-CSharp.dll` and deletes `DieAuth.dll` and `DieGamepad.dll`. The
backups themselves are kept.

The gamepad payload's own files beside the game — `Gamepad.cfg` and its logs — are left alone, so settings
survive a restore-and-repatch. Delete them by hand if you want a clean slate.

**Alternative:** Steam → the game's Properties → *Installed Files* → *Verify integrity of game files*. Steam
replaces the modified assemblies with the depot's copies. It does not remove the payloads (Steam ignores
files it did not ship), which is harmless: nothing references them once the hooks are gone.
