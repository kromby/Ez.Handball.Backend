# Player `Retired` flag — design

**Date:** 2026-06-11
**Scope:** Backend (`Ez.Handball.Backend`) only. Web consumption is a separate follow-up.

## Problem

Players never leave the dataset: a `PlayerEntity` is created the first time a
player appears in any match's player-stats blob, and there's currently no way to
say "this player has stopped playing." We want to mark players as **retired** so
they can be hidden from forward-looking discovery surfaces (the fantasy player
pool and the leaderboard) while keeping the underlying historical data intact.

## Behaviour

`Retired` is a **stored boolean owned by the data maintainer**, not a derived
value:

1. A one-time **bootstrap** seeds the flag: every player with no stats in the
   latest season is marked retired.
2. After that it is **manual** — the maintainer edits the flag directly in the
   Azure `Players` table (e.g. for players who *did* play last season but have
   since retired).
3. The flag **survives reparse** — ongoing ingestion must never overwrite it.

The bootstrap exists because manually marking the long tail of players who
simply stopped appearing would be tedious; the manual edits exist because the
bootstrap can't know about players who played last season but won't play again.

## Approach

Store `Retired` as a column on `PlayerEntity` and preserve it across reparse
with `TableUpdateMode.Merge` — the same mechanism that keeps the out-of-band
`LogoSrc` alive on the `Clubs` table. (A separate `RetiredPlayers` table was
considered and rejected: it adds a table, a repository, and an extra read on
every pool/leaderboard request for no benefit, since those paths already load
the `Players` table.)

## Data model

- **`PlayerEntity`** (`Ez.Handball.Shared/Entities/PlayerEntity.cs`): add
  `public bool? Retired { get; set; }`. **Nullable on purpose** — a nullable
  property is omitted from a `Merge` write when null, which is what lets the
  ingestion parser leave it untouched.
- **`Player`** (`Ez.Handball.Domain/Player.cs`): add `bool Retired`, mapped in
  `TablePlayerRepository.GetByIdAsync` as `row.Retired ?? false`. Consumers see a
  plain bool.
- **`PooledPlayer`** (`IPlayerPoolRepository.cs`): add `bool Retired` so the use
  case can filter on it.

## Ingestion preserves the flag

- **`PlayerParser.ParseAsync`**: switch the **`Players`** upsert from the default
  `Replace` to `Merge`, leaving `Retired` unset (null). This preserves any
  hand-set value across every reparse — identical to how `MatchParser` keeps
  `LogoSrc` on `Clubs`. The `PlayerStats` upsert is unchanged.
  - Side note: `Merge` also means nullable identity fields (`JerseyNumber`,
    `DateOfBirth`, `ClubName`) are not *cleared* to null on reparse. This is a
    non-issue in practice: reparses replay the *same* archived blob, so a field
    that was non-null stays non-null — only the out-of-band `Retired` behaves
    differently, which is the intent.

## Bootstrap function

New HTTP function `BootstrapRetiredFunction` in **`Ez.Handball.Ingestion`**
(`Functions/BootstrapRetiredFunction.cs`), mirroring the existing seed/reparse
functions: a thin `RunAsync` wrapper delegating to a testable `ProcessAsync`
core.

- **Route:** `POST /api/players/bootstrap-retired`
- **Logic:**
  1. Determine the **latest season label** — enumerate distinct `Tournaments`
     partition keys and take the lexical maximum (`"2025-26"` > `"2024-25"`;
     the `YYYY-YY` format sorts correctly).
  2. Collect the distinct `playerId`s (RowKeys) with any `PlayerStats` row whose
     `Season` equals that label.
  3. Enumerate the `Players` table; for every player **not** in that set,
     `Merge`-write `Retired = true`.
- **Only ever sets `true`, never `false`.** This makes it safely re-runnable and
  guarantees it never clobbers a manual edit on a currently-playing player.
- **Returns** a count of players marked retired (and the season label used).

## Read side

"Hide everywhere" applies to **list/discovery surfaces**, not direct lookups.
Opening a retired player's page directly still loads and shows `retired: true`.

- **Pool** (`GetPlayerPoolUseCase`): filter `!p.Retired` alongside the existing
  position filter, *before* ranking and paging so the reported `Total` excludes
  retired players. Requires `Retired` on `PooledPlayer`, populated by
  `TablePlayerPoolRepository` from the `Players` join it already performs.
- **Leaderboard** (`TableLeaderboardRepository.GetRankedAsync`): it already
  enumerates the `Players` table to resolve names — capture `Retired` in that
  same pass and drop retired players before ranking. The filter lives in the
  repository because ranking does too (the repo returns a fully-ranked list).
- **Player detail** (`GET /api/players/{playerId}` in
  `Ez.Handball.Api/Program.cs`): add `retired` (`f.Player.Retired`) to the
  response object so the Web UI can grey out retired players later.
- **Single-player stats / history / rating endpoints**: unchanged — direct
  lookups still return data for retired players.

No new endpoints in the API project; only use-case/repository tweaks and the
player-detail response shape change there. The single new endpoint is the
bootstrap, in Ingestion.

## Testing

- **`PlayerParser`**: the `Players` upsert uses `Merge` and does not write
  `Retired`.
- **`BootstrapRetiredFunction`** (`ProcessAsync`): marks exactly the players with
  no stats in the latest season; leaves latest-season players untouched; sets
  only `true`; re-running is idempotent.
- **`GetPlayerPoolUseCase`**: retired players are excluded and `Total` reflects
  the exclusion.
- **`TableLeaderboardRepository`**: retired players are excluded from the ranking.
- **`TablePlayerRepository`**: maps `Retired` (`null` → `false`).
- **Player-detail endpoint**: response includes `retired`.

## Ops / rollout

1. Deploy.
2. Run `POST /api/players/bootstrap-retired` once.
3. Curate manually thereafter by editing the `Retired` column in the Azure
   `Players` table — **use `Edm.Boolean`, not String** (a String value here
   causes a 500 on read; known gotcha).
4. Reparse (`POST /api/reparse`) preserves all `Retired` values via `Merge`.

## Out of scope

- Web UI changes to display/grey out retired players (separate Web-repo task).
- Any automatic re-evaluation of `Retired` on future season rollovers — the flag
  is bootstrapped once and then maintained by hand.
