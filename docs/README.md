# Launcher documentation

Documentation for the Reanimated launcher and its client patch — what the code in this repository does and why.
It follows the [Diátaxis](https://diataxis.fr) structure: pick the column that matches what you need.

| I want to… | Go to |
|---|---|
| **learn by doing** — build it, patch my own install, see it work, undo it | [Tutorials](tutorials/) |
| **get a task done** — apply / restore the patch, verify a download, cut a release | [How-to guides](how-to/) |
| **look something up** — the screens, commands, exit codes, files touched, log lines, release artifacts | [Reference](reference/) |
| **understand** — why a session ticket, why edit the client at all, what is and isn't trusted | [Explanation](explanation/) |

## Map

```
tutorials/
  getting-started.md        build from source → patch an install → watch a ticket get minted → restore
how-to/
  install-the-launcher.md
  apply-the-client-patch.md
  restore-the-original-client.md
  verify-a-release.md
  cut-a-release.md
reference/
  screens.md                the window: options, the frame, Home / News / Servers / Settings
  cli.md                    die-launcher state | patch | play | update | status | restore
  manifest.md               the server manifest + status: the contract the launcher speaks
  client-patch.md           the patch's two halves: the shapes of edit, detection, the payload contracts
  gamepad-patch.md          the gamepad half: what is installed in both clients, the payload, its frame budget
  files-and-locations.md    every file the launcher reads or writes
  dieauth-log.md            the payload's log lines
  release-artifacts.md      what a release contains and how it is produced
explanation/
  how-a-start-proceeds.md   manifest → where am I → patch state → the state machine → PLAY / UPDATE
  identity.md               how a player is identified, and why a session ticket
  patching-strategy.md      why a static edit rather than runtime injection, and its failure semantics
  trust-model.md            who is trusted for what
  art.md                    why the pictures are read from the install, never shipped
```

Releases: [reference/release-artifacts.md](reference/release-artifacts.md) covers the assets, the two channels
(a build updates from the repository that built it) and the icon; [how-to/cut-a-release.md](how-to/cut-a-release.md)
the tag → build → release steps.

The **layout of the source** mirrors this: `patches/` holds the two payloads that run inside the game
(`DieAuth`, `DieGamepad`) and the compile-only `UnityEngine.Stub` the second builds against;
`src/Launcher.Patching` is the rewriter that installs them, `src/Launcher.Core` everything else the launcher
does, `src/Launcher` the window over it, `src/Launcher.Cli` the same behaviour headless.
