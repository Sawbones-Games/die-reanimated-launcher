# Explanation: the trust model

Who has to be trusted for what. The short version: **the launcher itself is trusted for nothing.**

## Three parties, three jobs

| Party | Trusted for | Not trusted for |
|---|---|---|
| **Valve** | saying whether a session ticket is genuine and whose it is | anything about this project |
| **This repository + GitHub** | the bytes of the launcher and the patch: what the code is, and that a release was built from it | who a player is |
| **The game server** the launcher is configured for | game policy: which launcher/patch version is current, news, whether to require verified identity | producing binaries; it never serves one |

The launcher and the patch hold no key, secret, token or credential. There is nothing in them that would be
worth extracting, and nothing a modified copy could forge.

## Identity

Proven by Valve, checked by the server, carried by the game. The patch only changes *which* Steam ticket the
game sends ([identity](identity.md)). A tampered patch could send garbage, an old ticket, or a ticket for a
different account — and each of those fails validation at the server, because the server asks Valve, not the
client. The worst a malicious patch can do is fail its own user's login.

## Binaries

A release is produced only by the public workflow in this repository, from a tagged commit, and is
accompanied by a digest list and a signed build-provenance attestation ([release artifacts](../reference/release-artifacts.md)).
Anyone can read the tag's source, read the workflow that built it, verify the attestation with GitHub's
tooling, and compare the digest of the file they hold ([how-to](../how-to/verify-a-release.md)).

The boundary this draws: **whoever can push a `v*` tag to this repository can ship a binary to players.**
That is the correct boundary for an open-source launcher, and it is why a repository ruleset lets only repository
admins create, move or delete a `v*` tag.

## Updates

The server tells an installed launcher only *that* a newer version exists — a version number. The launcher
then fetches that version from the GitHub Releases of **the repository that built it** (stamped in by the
workflow at build time, not read from any setting or manifest), checks it against the release's own
`SHA256SUMS`, and only then replaces itself. Consequences:

- The server cannot supply bytes. It can name a version; if that version is not a public tag, nothing happens.
- A compromised server can, at most, point players at an *older* public release or at nothing — never at code
  that was not published here.
- Update traffic never touches the game server.

## Content

News bodies and images are named by their SHA-256. The manifest (which the launcher takes on the server's word,
like every other policy value) names a hash; the bytes that arrive for it are hashed and compared to the name
before they are kept. So the server cannot substitute content under a name a manifest already gave out, and a
cached item never needs checking again. A body is a Markdown subset with no links, no HTML and no images
([manifest](../reference/manifest.md)): the worst a body can do is read badly.

## What is deliberately *not* verified in the launcher

The launcher checks the release digest (integrity: the file is what the release says it is). It does not
itself verify the build-provenance attestation (provenance: the file was built by the public workflow). The
attestation is verifiable by anyone with GitHub's tooling, and the digest it covers is the same one the
launcher checks, so a player who wants the full chain can close it in one command. Verifying the attestation
inside the launcher would add a signature-verification stack to a program whose whole appeal is being small
and readable; it is a reasonable future addition, not a gap in the model.

## What the launcher can see about you

Your Steam display name, avatar and SteamID64, read from the local Steam client's own files. The name and
avatar are only shown in the window. The SteamID64 is also sent, with the manifest request, to the server the
launcher is configured for, which uses it for one thing: during maintenance, to say whether your account is on
the testers' whitelist ([manifest](../reference/manifest.md)). Nothing else about you is sent anywhere. The
identity that matters — the ticket — never passes through the launcher at all.
