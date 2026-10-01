# Explanation: how a player is identified

## The problem the patch solves

When *Dead Island: Epidemic* logs in, it sends the server a ticket obtained from Steam with
`ISteamUser::GetEncryptedAppTicket`. An encrypted app ticket is, by design, opaque to everyone except the
holder of the game's *publisher key* — it was meant for the original backend to decrypt. Anyone else,
including a community server, receives a couple of hundred bytes of ciphertext with no way to learn who sent
them.

So a community server has two bad options: identify players by something they can freely change (their
Steam display name), or trust whatever the client claims. Both mean one player can log in as another.

## What the patch changes

Steam has a second kind of ticket for exactly this situation: the **session ticket**
(`ISteamUser::GetAuthSessionTicket`). Any game server — including one running anonymously, with no key or
token from Valve — can hand a session ticket to Steam (`ISteamGameServer::BeginAuthSession`) and be told:

- whether the ticket is genuine and current,
- which SteamID64 it belongs to,
- which account owns the game licence being used (the same id, or a different one under Family Sharing).

The patch makes the game ask Steam for a session ticket and put *that* in the field it was already sending.
The game does not know the difference: it asked for "the ticket" and got bytes. The server does know: it can
now check them.

This is not a novel mechanism. It is the standard Steam authentication flow that most Steam games use; this
particular game happened to use the other kind of ticket because its backend had a publisher key.

## Why the identity cannot be forged

The ticket carries the SteamID in the clear. That is a **claim**, not a proof — anyone can write any id
there. What makes it a proof is the signed section
after it, which only Steam can produce for the account that is actually logged into the Steam client that
minted it. A server that validates with Valve learns whether the claim and the signature agree.

Consequences:

- Changing your Steam display name changes nothing; the id is stable for the life of the account.
- Presenting someone else's ticket does not work: tickets are bound to the minting client's session, and a
  ticket for a different SteamID than the one claimed is rejected outright.
- Tampering with a ticket produces something Steam simply never confirms.

## What the patch does *not* do

- It does not talk to any server. It changes what the game sends; the game sends it.
- It does not read, store or transmit anything about the player except the ticket Steam produced.
- It does not require the server to validate. An unvalidating server sees a different opaque blob and
  proceeds as before. Validation is the server's choice; the patch makes it *possible*.
- It does not touch the in-match client. Once logged in, the game identifies players by the session the
  server issued at login; the patch's job is over.

## Why in the game and not in the launcher

A launcher could mint a session ticket too. But a ticket minted by the launcher would have to be carried into
the game somehow — a file, an argument, a local socket — and something in the game would still need
modifying to send it. Minting where the sending happens removes the hand-off, removes any Steam code from the
launcher, and keeps the identity change to exactly one method. The launcher therefore never holds a ticket, token or
credential of any kind; it is a patcher and a starter, nothing more.
