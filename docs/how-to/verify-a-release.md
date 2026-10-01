# How to verify a release

**Goal:** confirm that a `DIE-Reanimated-Launcher.zip`, `DIE-Reanimated-Launcher.exe`, `DieAuth.dll` or
`DieGamepad.dll` you downloaded is byte-for-byte the file the public build workflow produced from a tagged
commit. (The zip holds the
exe under the game's launcher name; the bare exe is the same bytes.)

Every release on GitHub carries three kinds of proof:

1. **`SHA256SUMS`** — the digests of the assets, written by the build job.
2. **A build-provenance attestation** — a signed statement, recorded by GitHub, that a specific workflow run
   in this repository produced these exact bytes from a specific commit.
3. **The tag** — the commit the build ran from, readable in the repository.

## Check the digest

```
sha256sum -c SHA256SUMS
```

(On Windows without coreutils: `certutil -hashfile DIE-Reanimated-Launcher.zip SHA256` and compare by eye.)

## Check the provenance

With the [GitHub CLI](https://cli.github.com/):

```
gh attestation verify DIE-Reanimated-Launcher.exe --owner Sawbones-Games
```

A successful verification names the workflow (`.github/workflows/release.yml`), the repository and the commit
SHA the file was built from. Open that commit on GitHub to read the exact source.

## What this does and does not prove

It proves the file came out of this repository's public build, unmodified. It does not audit the source for
you — that is what the source being public is for. See [trust model](../explanation/trust-model.md).
