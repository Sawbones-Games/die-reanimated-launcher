# Reference: the gamepad half of the client patch

*(The identity half is in [client-patch.md](client-patch.md); both are installed by one PATCH and share one
version.)*

## What is installed

`DieGamepad.dll` beside **both** clients — the hub and the match client are separate executables with
separate assemblies, and the pad has to work in a match as well as in the hub — plus one inserted `call` at
the top of a start-up method in each, so the payload starts when the client does. Which method that is in
each client is declared in `GamepadPatcher.Hooks`, one build's detail in one place.

The insertion adds a single instruction and replaces nothing:

```
call    void DieGamepad.Bootstrap::Init()
…the method's original body, untouched…
```

That is why several payloads can share one start-up method: each inserts its own call.

## Detection

A client is hooked when that method contains a `call` whose target is declared on `DieGamepad.Bootstrap`.
Both clients must be hooked and carry the payload, or `ClientPatcher.Inspect` reports `NotPatched` and PATCH
completes the install.

## The payload contract

```csharp
namespace DieGamepad
{
    public static class Bootstrap
    {
        public static void Init();   // never throws; a failure here must not stop the client starting
    }
}
```

`Init` creates one `DontDestroyOnLoad` GameObject and returns. Everything after that is driven from Unity
callbacks on that object. It catches everything: a dead pad is recoverable, a throw inside a start-up method
means the client does not start at all.

## What the payload does, and does not, touch

Nothing on the network. It reads an XInput pad and drives the game through the paths a mouse and keyboard
already use — the player's own key bindings, and the real cursor — so **a pad player is byte-identical to a
mouse player on the wire and the server needs to know nothing**. While the pad is driving it also hands the
pad's buttons to the client's own binding system, in memory only and restored afterwards, so the player's
saved keyboard profile is never rewritten.

It adds one row to the client's own options screen (**Options → General → Input device**) in each client,
built through that screen's own builder and registered with its own save type. That last part is what makes
it behave like a real option rather than a switch that happens to sit among them: the value is read back out
of the settings store the screen's **Apply** button writes, so Apply, Cancel and the unsaved-changes prompt
all work without the payload knowing they exist. The row is disabled while no XInput pad is present, and
the default is **Keyboard & Mouse**, under which the payload synthesises nothing at all.

The button layout and every setting are in `Gamepad.cfg` beside the game exe, written with comments on first
run ([files](files-and-locations.md)).

## Frame budget

A payload every player installs must cost them nothing, and that is measured rather than assumed. Measured
in a match while moving, against the same client with the payload switched off in its config: median frame
time **3.52 ms vs 3.59 ms**, 99th percentile **5.5 ms** either way, and **no frame over 50 ms in either
arm**. Allocation is the one number that moves (about +330 KB/s), which at that rate causes no additional
garbage collection.

Setting `frameStats=<seconds>` in the payload's config makes it report its own frame times to its log, which
is how those numbers are taken. It is off by default, and off means the sampler is never created.

## Building it

`patches/DieGamepad`, .NET 3.5 — the only target the clients' runtime loads. It needs UnityEngine types, so
it compiles against `patches/UnityEngine.Stub`, a facade of our own carrying the assembly identity the
runtime will bind to (`UnityEngine, Version=0.0.0.0, PublicKeyToken=null`), referenced with `Private=false`
and never shipped. At run time the real `UnityEngine.dll` already present beside the game answers every
member. Unity's assembly is not redistributable and CI has no game install, so a facade is the only way a
public build can compile this at all.

Three things must stay true of that stub, and none of them are guesses — each is checked by running the
stub-built DLL in the game:

1. **Identity** — the reference the compiler records is what the runtime binds to.
2. **Member kind** — a property stubbed as a field emits the wrong instruction and fails at run time, so
   properties must be stubbed as properties and fields as fields.
3. **Layout and values** — enums the payload does arithmetic on must carry the same values the runtime has.

Adding a Unity API to the payload means adding it to the stub; a missing member is a compile error.

## Uninstalling

`restore` puts both original assemblies back from their `.bak` and deletes `DieGamepad.dll` from both
`Managed` folders. A player who only wants the pad off can set `enabled=0` in the payload's config instead,
or choose **Keyboard & Mouse** under Options → General → Input device, which is the default and makes the
payload synthesise nothing.
