# How to apply the client patch

**Goal:** install both halves of the client patch — identity (`DieAuth.dll`, Crib) and gamepad
(`DieGamepad.dll`, both clients) — into a *Dead Island: Epidemic* install.

**Preconditions:** the game is not running (the assemblies are locked while it is). Both payloads are
embedded in the launcher build, so nothing else is needed; with a reachable server, `ip.cfg` is written too.

```
die-launcher patch --game "<install folder>"
```

- Exit `0` and a last line of `Patched  payload=<version>  (hook present)` means done.
- Exit `3` means the game was running. Close it and rerun.
- A pristine `Assembly-CSharp.dll.bak` is created the first time and **never overwritten** afterwards, so it
  always holds the untouched original even after repeated patching.

The command is idempotent: an already-patched install reports `getter already hooked` (and, per client,
`already hooked`) and exits `0`. Updating is the same command with newer payloads — each file is replaced
when its bytes differ, and the hooks are left as they are.

To confirm without changing anything: `die-launcher status --game "<install folder>"`.
