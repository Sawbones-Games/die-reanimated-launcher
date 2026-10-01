# Explanation: the patching strategy

## Edited on disk, not injected at run time

The patch is a static rewrite of `Assembly-CSharp.dll`: a tool opens the file, edits one method, and writes
the file back. Nothing attaches to the running game, nothing injects at start-up, no loader DLL is dropped
into the process. When the game starts, the modified method is simply what the runtime finds.

Why this shape:

**A property getter is the natural seam for the identity half.** One property is read wherever the value is
needed, so replacing the getter catches every reader at once, where replacing the underlying field would
mean finding everywhere it is written. The original body is short enough that "the original" is
unambiguous, and the rewriter refuses anything that does not match it exactly — an assembly it does not
recognise is left alone rather than corrupted.

**A prologue call is the natural seam for a payload that just needs to start.** Inserting one `call` before
a start-up method's first instruction replaces nothing, so several independent payloads can share one method
without knowing about each other.

**Static beats runtime injection here.** The game has no anti-cheat and no integrity check of its own; the
only thing that ever restores the file is Steam's *verify integrity*, which restores everything anyway. A
runtime injector would add a loader, a second file format to reason about, and a start-up race, to solve a
problem that does not exist. Static patching also means the payload is present from the first instruction —
there is no "was it loaded in time" question.

**The payload is a plain referenced assembly.** The rewritten getter calls `DieAuth.Bootstrap.Ticket`, so
`DieAuth.dll` is resolved by the runtime like any other dependency, from the same folder. No registration,
no manifest, no search path tricks.

## Why the payload uses reflection for everything

`DieAuth.dll` references only the base class library. It reaches Steam by looking for a field *of a certain
type* on the object it is handed, and calling that wrapper's methods by name.

- Searching by **type** rather than by name keeps the walk independent of names that are not stable between
  builds.
- Not referencing `UnityEngine.dll` or any assembly shipped with the game means the payload builds from a
  clean checkout with nothing but the .NET SDK — which is what lets a public CI produce it reproducibly.
  The gamepad payload does need Unity types, and solves the same problem a different way: it compiles
  against a facade of our own that is never shipped (see [gamepad-patch](../reference/gamepad-patch.md)).
- .NET 3.5 is the target because that is the profile the clients' runtime loads; nothing newer will.

## Failure semantics: never worse than unpatched

Every path through `Ticket` that does not end in a minted session ticket returns the *original* buffer the
game passed in. Steam not running, the wrapper not initialised, a method missing, an exception — all of them
log a line and hand back exactly what the game would have sent without the patch. The payload can make a
login *verifiable*; it cannot make one *fail*.

The one thing it cannot recover from is being absent: if `Assembly-CSharp.dll` still contains the call but
the payload has been deleted, the runtime cannot resolve the type and the edited method throws. The `status`
command reports this state (`PatchedPayloadMissing`) precisely so a launcher can repair it before starting the
game.

## Idempotency, backups, and Steam's verify

`patch` is designed to be run before every start:

- **Detection is structural.** "Patched" means the getter contains a call into `DieAuth.Bootstrap`. Running
  `patch` on a patched install rewrites nothing and copies the payload only if its bytes differ.
- **The backup is taken once and never overwritten.** `Assembly-CSharp.dll.bak` is created the first time
  and kept forever after, so it always holds the pristine original even across many payload updates.
- **Writes are temp-and-rename.** The rewriter writes `Assembly-CSharp.dll.tmp`, closes its read handle,
  then renames. A crash mid-write leaves the original untouched.
- **Steam's *verify integrity* is expected, not feared.** It restores the depot's assembly (undoing the hook)
  and leaves `DieAuth.dll` in place (Steam ignores files it did not ship). The next `patch` detects
  `NotPatched`, re-hooks, and the install is whole again. This is why the launcher re-checks the patch on
  every start rather than remembering that it applied it once.
- **The game must be closed.** Windows locks a loaded assembly. The rewriter surfaces this as an ordinary
  `IOException`; the CLI maps it to exit code 3 so callers can tell "close the game" from "something broke".
