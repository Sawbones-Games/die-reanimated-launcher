# Reference: release artifacts

Produced by `.github/workflows/release.yml` on a pushed `v*` tag. Nothing else produces a player-facing binary.

| Asset | What | Built from |
|---|---|---|
| `DIE-Reanimated-Launcher.zip` | **for players**: the launcher as `Dead Island Epidemic - Launcher.exe`, the game's own launcher name, to drag over the retail file. A zip because GitHub rewrites spaces in asset names to dots | `src/Launcher` |
| `DIE-Reanimated-Launcher.exe` | the same binary, bare — what an installed launcher downloads to update itself (self-contained single-file `win-x64`, ~45 MB compressed; no trimming, no ICU) | `src/Launcher` |
| `DieAuth.dll` | the client patch's identity payload, .NET 3.5 | `patches/DieAuth` |
| `DieGamepad.dll` | the client patch's gamepad payload, .NET 3.5. Compiles against `patches/UnityEngine.Stub`, a compile-only facade carrying the assembly identity the runtime binds to — CI has no game install and Unity's DLL is not redistributable | `patches/DieGamepad` |
| `SHA256SUMS` | `sha256sum`-format digests of the files above | the build job |
| *(attestation)* | build-provenance record for both binaries, stored by GitHub, not a downloadable file — **public channel only** (GitHub offers attestations to public repositories; a dev release ships the digests alone and is marked a pre-release) | `actions/attest-build-provenance` |

## The icon

`src/Launcher/Assets/launcher.ico` (and `.png`) is the launcher's own mark — the dark teal tile, "DIE" in Oswald
and the website's red bar — embedded as the executable's icon and used as the window's. It is rendered by
`tools/make-icon.py` from the fonts in the repository; no game art is involved.

## Channels

The workflow file is the same in the public repository and the private development one; each build is stamped
with **the repository that built it** (`-p:ReleaseRepository=${{ github.repository }}` → an assembly metadata
attribute → `Defaults.ReleaseRepository`) and updates only from that repository's releases. A dev build says so
in its version (`0.1.0 · dev`) and needs a read token for the private repository's assets
([files](files-and-locations.md)); a public build needs nothing. Nothing at run time can point a build at
another repository.

## Versioning

The version is the tag with the `v` stripped (`v0.2.0` → `0.2.0`) and is stamped into both assemblies via
`-p:Version=`. There is no version to bump in source. `status` reports the installed payload's version from
this stamp.

## Pinned actions

Every action in the workflow is referenced by commit SHA with its tag in a comment. The tests run before the
build; a red test is a failed release.

## Workflow permissions

`contents: write` (create the release and upload assets), `id-token: write` and `attestations: write` (sign
and store the attestation). No secrets are used.

## What a consumer should do with these

Download the two binaries, check them against `SHA256SUMS`, optionally verify the attestation
([how-to](../how-to/verify-a-release.md)). A launcher performing a self-update does the digest check
unconditionally and refuses a file that does not match.
