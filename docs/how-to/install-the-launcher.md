# How to install the launcher

**Goal:** make Steam's Play button start the Reanimated launcher.

1. Download `DIE-Reanimated-Launcher.zip` from the [latest release](https://github.com/Sawbones-Games/die-reanimated-launcher/releases)
   (and, if you like, [verify it](verify-a-release.md)). Inside it is one file, `Dead Island Epidemic - Launcher.exe`.
2. Open the game folder: Steam → *Dead Island: Epidemic* → Properties → Installed Files → *Browse*.
3. Drag that file out of the zip into the game folder, **replacing** the file of the same name that is there.
   (Keep a copy of the original if you want; Steam's *Verify integrity of game files* also restores it at any
   time.)
4. Press Play in Steam. The launcher opens and shows **PATCH**; press it once. From then on it shows **PLAY**.

That is the whole install. There is no installer, no shortcut, and nothing outside the game folder except the
launcher's own cache under `%LocalAppData%\DIE Reanimated\` ([files](../reference/files-and-locations.md)).

**Windows SmartScreen** may warn the first time ("Windows protected your PC") because the file is not
code-signed. *More info → Run anyway.* The digest and provenance checks in [verify a release](verify-a-release.md)
are how you satisfy yourself the file is what this repository built.

**If you ran the exe from somewhere else** (your Downloads folder, say) it does nothing but tell you where to
put it.

## Undoing it

Steam → Properties → Installed Files → *Verify integrity of game files* restores the retail launcher **and** the
original game assemblies. The payloads are left behind (Steam ignores files it did not ship) and are harmless
once nothing calls them; delete them if you like.
