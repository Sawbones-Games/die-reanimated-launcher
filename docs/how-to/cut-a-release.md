# How to cut a release

**Goal:** publish a new launcher / payload version that installed launchers can pick up.

**Preconditions:** the change is on `master`; you can create `v*` tags (a repository ruleset restricts
creating, moving and deleting them to repository admins).

1. Bump nothing by hand — the version comes from the tag.
2. Tag and push:

   ```
   git tag v0.2.0
   git push origin v0.2.0
   ```

3. The `release` workflow runs on the tag: builds both payloads (`DieAuth.dll`, `DieGamepad.dll`; net35) and
   the launcher (self-contained, single file), writes `SHA256SUMS`, records a build-provenance attestation,
   and creates the GitHub Release with the assets and generated notes. See
   [release artifacts](../reference/release-artifacts.md).
4. Check the run's log for the printed digests and open the release page. That is the whole release.

Installed launchers learn about the new version from the server they are configured for, which only ever
states *which* version is current; the bytes are always fetched from this repository's release. See
[trust model](../explanation/trust-model.md).

**Pulling a release:** delete the GitHub Release and the tag. A launcher that already updated keeps running
the version it has; nothing phones home about it.

## Channels

The same workflow runs in the public repository and in the private development one. A tag on the dev
repository produces a **dev** pre-release whose launcher says `x.y.z · dev` and updates only from the dev
repository (it needs a read token in `settings.json`, see [files](../reference/files-and-locations.md)); a tag
on the public repository produces the public release with the attestation. Nothing is copied between them:
the public `master` is merged and tagged when the dev channel has proven a version.

## Never move a tag

A tag that has a release is immutable in practice: any launcher that already took it holds that version number
and will never ask for the same number again. A mistake in a release is fixed forward — the next tag — never by
deleting and re-pushing the same one.

