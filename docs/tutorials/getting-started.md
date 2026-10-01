# Tutorial: from source to a patched client, and back

In this tutorial you build the launcher from source, patch your own *Dead Island: Epidemic* install, watch
the patch mint a Steam session ticket when the game logs in, and then put the original client back.
About fifteen minutes. You need: the game installed through Steam, Steam running, the .NET 8 SDK, and a
terminal.

Nothing in this tutorial needs a server. The patch's job ends when the game has a ticket to send.

## 1. Build

```
git clone https://github.com/Sawbones-Games/die-reanimated-launcher.git
cd die-reanimated-launcher
dotnet build -c Release
```

Several projects build. Three outputs matter for now:

- `patches/DieAuth/bin/Release/net35/DieAuth.dll` — the identity payload (~10 KB). It targets .NET 3.5
  because that is what the clients' runtime can load, and it references nothing but the base library.
  (`patches/DieGamepad` builds the other half of the patch the same way; this tutorial does not use it.)
- `src/Launcher.Cli/bin/Release/net8.0/die-launcher.dll` — the command-line driver.
- `src/Launcher/bin/Release/net8.0/DIE-Reanimated-Launcher.exe` — the window. Run it with the same
  `--game`/`--server` options as the CLI to see the same state as a screen ([screens](../reference/screens.md)).

## 2. Look before you touch anything

Point the CLI at your install (adjust the path):

```
dotnet src/Launcher.Cli/bin/Release/net8.0/die-launcher.dll status --game "C:\Program Files (x86)\Steam\steamapps\common\Dead Island Epidemic"
```

You should see:

```
NotPatched  payload=-  backup=no  (getter is the game's original)
```

`status` opened the clients' assemblies, found the methods the patch cares about, and confirmed they are
still the game's own. It changed nothing. (Exit code 1 here simply means "not patched".)

## 3. Patch

Make sure the game is **closed** — the assembly is locked while it runs — then:

```
dotnet src/Launcher.Cli/bin/Release/net8.0/die-launcher.dll patch --game "<your install>"
```

```
payload → …\Dead Island Epidemic - Crib_Data\Managed\DieAuth.dll
backup → …\Dead Island Epidemic - Crib_Data\Managed\Assembly-CSharp.dll.bak
hooked the identity getter
payload → …\Dead Island Epidemic - Crib_Data\Managed\DieGamepad.dll
hooked the gamepad start-up call in Dead Island Epidemic - Crib_Data
payload → …\Dead Island Epidemic_Data\Managed\DieGamepad.dll
hooked the gamepad start-up call in Dead Island Epidemic_Data
Patched  payload=0.1.0  (hook present)
```

The same three things happened for each half, in that order: the payload (embedded in the build you just
made) was copied next to the game's assemblies; a pristine copy of `Assembly-CSharp.dll` was saved as `.bak`
the first time anything touched it; and one method was edited. (Without a reachable server the manifest line
says so and `ip.cfg` is left alone; that is fine for this tutorial.) Run the same command again and every
line says "already hooked" — the patch is idempotent, which is what lets a launcher run it on every start
without thinking.

## 4. Watch it work

Start the game normally from Steam and log in. Then open the payload's log, which lives next to it:

```
…\Dead Island Epidemic - Crib_Data\Managed\DieAuth.log
```

```
17:25:14.627  GetAuthSessionTicket → handle 15, 234 B
17:25:14.627  session ticket #1 minted: 234 B, length prefix 14000000
```

That is the moment the game asked for its login ticket and received a Steam **session** ticket instead of
the encrypted app ticket it used to send. Your own SteamID is in there in the clear — but that is the
*claim*, not the proof: the signed part is the proof, and only Valve can check it. A server hands this ticket
to Valve and gets back a yes or a no — see [Explanation: identity](../explanation/identity.md).

If you launched against a server that does not yet validate session tickets, the login still succeeds:
the ticket is just bytes in a field the game already sent.

## 5. Restore

Close the game, then:

```
dotnet src/Launcher.Cli/bin/Release/net8.0/die-launcher.dll restore --game "<your install>"
```

```
restored …\Dead Island Epidemic - Crib_Data\Managed\Assembly-CSharp.dll from .bak
removed …\Dead Island Epidemic - Crib_Data\Managed\DieAuth.dll
removed …\Dead Island Epidemic - Crib_Data\Managed\DieGamepad.dll
restored …\Dead Island Epidemic_Data\Managed\Assembly-CSharp.dll from .bak
removed …\Dead Island Epidemic_Data\Managed\DieGamepad.dll
```

`status` now reports `NotPatched` again. Steam's *Verify integrity of game files* would have done the same
thing for the assembly (it restores any changed depot file) — which is why a launcher re-checks the patch
before every start rather than trusting that it is still there.

## Where next

- To understand *why* the client is edited on disk rather than injected at run time:
  [patching strategy](../explanation/patching-strategy.md).
- To see exactly what each half installs: [reference: client patch](../reference/client-patch.md).
- To ship a build: [how-to: cut a release](../how-to/cut-a-release.md).
