# Enrich priced-player list + player-detail with price/position (Backend #67)

**Status:** design approved
**Date:** 2026-06-07
**Issue:** [Backend #67](https://github.com/kromby/Ez.Handball.Backend/issues/67)
**Consumed by:** [Web #16](https://github.com/kromby/Ez.Handball.Web/issues/16) (Fantasy Squad Builder UI)

## Goal

The Fantasy Squad Builder UI needs a filterable/sortable priced "transfer market"
list and a player-detail page that can render Buy/Sell. Both currently lack the
fields those controls need: `price`, `rating`, `pickPercentage`, and (for the
detail page) a reliable `position` alongside price.

This issue adds a dedicated priced player-pool endpoint and enriches the
player-detail response. It reuses the salary + rating primitives from #52 — no
re-derivation of the scoring or pricing formulas.

## Scope clarification vs. the issue text

The issue states *"Shortlist items already carry `price`, `position`, and
`pickPercentage`."* In the actual code only `Position` is real:
`ShortlistPlayer.Price` and `ShortlistPlayer.PickPercentage` are explicitly
reserved and always `null` today (`// reserved — #25`). **No ownership
aggregation exists anywhere in the codebase.**

Decision: `pickPercentage` is **deferred**. The field is added to the contract
returning `null` (so the UI can wire its "Owned" sort), and the ownership
aggregation is filed as a separate follow-up. This decouples #67 from #55
entirely — squad-ownership % only becomes meaningful once squads are populated
(#55), but shipping `null` now does not depend on #55.

## Part A — priced player pool

A new dedicated endpoint, kept separate from `/api/leaderboard` so the
leaderboard stays a lean stats-ranking surface and does not pay rating/salary
enrichment cost for every caller.

### Endpoint

`GET /api/players/pool`

| Param | Notes |
|-------|-------|
| `season`, `tournamentId`, `competitionId`, `type`, `gender` | Scope filters — reuse the existing `ITournamentScopeResolver` machinery, identical semantics to `/api/leaderboard` (incl. `tournamentId` + `competitionId` together → `400 invalid_scope`). |
| `position` | Optional. Filters to the stored position code (placeholder vocabulary, owner review pending). Exact match. |
| `sort` | `rating` (default) \| `price` \| `pickPercentage`. All **descending**. Sort applies to the full scoped set, then pages. |
| `offset` / `limit` | Same rules as leaderboard: `offset` default 0, `limit` default 50, max 200; out-of-range → `400 invalid_pagination`. |
| `version` | Optional price-rule-set version. Defaults to `1` (parity with `/api/players/{id}/salary`). |

### Response entry

```jsonc
{
  "rank": 1,
  "playerId": "12345",
  "name": "...",
  "clubId": "...",
  "clubName": "...",
  "gender": "karlar",
  "position": "CB",
  "price":  { "amount": 11000000, "currency": "ISK" },
  "rating": 49,
  "pickPercentage": null
}
```

The envelope mirrors `Leaderboard` (`total`, `offset`, `limit`, `entries`).

- `position` is the stored code from the `Players` table (placeholder vocabulary).
- `price` reuses the #52 salary primitive (`PlayerCost` → `{ amount, currency }`).
- `rating` reuses the #52 fantasy rating metric.
- `pickPercentage` is always `null` (deferred — see follow-up below).

### Sort semantics

- Sort orders the **full** scoped (and position-filtered) set, then `rank` is
  assigned `1..N`, then the page slice is taken.
- Tie-break is stable: `rating` descending, then `playerId` ordinal.
- `sort=pickPercentage` is **accepted** but, because every value is `null`,
  ordering falls through to the stable tie-break — no error. This lets the UI
  wire all three sort buttons now; the order becomes meaningful when the
  ownership follow-up ships.

## Part B — player detail

`GET /api/players/{playerId}` gains `price`. `Position` is already present on the
`Player` record, so no change is needed for it beyond confirming it is surfaced.

- The response is wrapped in a small DTO that composes the existing `Player`
  with `price` (Money), sourced from the existing per-player salary service
  (single player = O(1) — no bulk path needed here).
- If the price rule-set is missing, `price` is `null` and the rest of the
  profile still returns `200`.
- `404 player_not_found` behaviour is unchanged.

## Compute path (bulk, no N+1, no re-derivation)

Both `rating` and `salary` are computed per-player today: each call does its own
`IPlayerStatsRepository.GetByPlayerAsync` query plus rule-set loads. Enriching a
full pool that way would be O(N) Table round-trips. The leaderboard repo instead
does **one** bulk `PlayerStats` scan and aggregates everyone in-memory.

The pool follows the leaderboard's bulk pattern:

1. **`IPlayerPoolRepository`** does one `PlayerStats` scan, aggregates per player
   into the same per-player stat shape the leaderboard repo already produces
   (games/goals/yellow/2min/red + club resolution + gender), and joins the
   `Players` table for `Name` **and** `Position` (the leaderboard repo already
   joins for `Name`).
2. **`GetPlayerPoolUseCase`** loads the fantasy **scoring** rule-set and the
   **price** rule-set **once**, then per aggregated player:
   - `rating = FantasyPlayerRatingFunction.Compute(stats, scoringRuleSet).Rating`
     — the same function the #52 rating endpoint uses (no re-derivation).
   - `score = games >= minGames && games > 0 ? rating / games : 0` (the existing
     min-games guard).
   - `price = priceRuleSet.BandFor(score).Price`.
3. Applies the `position` filter, sorts by the chosen key, assigns `rank`, pages.

### Targeted refactor

Today `PlayerSalaryService.GetSalaryAsync` couples *aggregate-then-compute*. The
pure *compute-from-already-aggregated-stats + rule-sets* step is extracted into a
shared helper so both the single-player salary path and the bulk pool path call
the **identical** formula. Salary and rating output stay byte-for-byte the same;
a regression test guards this.

This stays request-time, consistent with #20/#52 (the ingestion-time precompute
is the separate #50 follow-up and is out of scope here).

## Testing

- **Pool use case:** entries carry `price` / `position` / `rating` /
  `pickPercentage(null)`; position filter narrows the set; each sort key
  (`rating`, `price`) orders correctly; `sort=pickPercentage` is a stable no-op
  (no error, tie-break order); paging is taken over the sorted full set;
  rule-set-missing is handled gracefully; gender + scope filters honoured.
- **Pool repository:** one-scan aggregation + `Position` join (Azurite
  integration test, mirroring `TableLeaderboardRepositoryTests`).
- **Player detail:** response carries `price` + `position`; `price` is `null`
  when the rule-set is absent; `404` unchanged.
- **Shared compute helper:** salary/rating output identical to the pre-refactor
  service (regression guard).
- **Endpoint:** `400` on bad pagination / `invalid_scope` / invalid sort key /
  invalid type / invalid gender, mirroring the leaderboard endpoint's validation.

## Acceptance criteria

- `GET /api/players/pool` returns entries carrying `price`, `position`,
  `rating`, `pickPercentage`; supports a `position` filter and `sort` by
  `rating` / `price` / `pickPercentage`; tested.
- `GET /api/players/{playerId}` response carries `price` + `position`; tested.
- `price` reuses the salary primitive (#52); `rating` reuses the #52 metric — no
  re-derivation (shared compute helper, regression-tested).
- Position values are the stored codes (placeholder vocabulary).
- The pool computes rating + salary for the whole list with O(1) Table queries
  (bulk scan + rule-sets loaded once), not O(N).

## Follow-up (out of scope, to be filed)

`pickPercentage` ownership aggregation — squad-ownership % (count of fantasy
squads containing the player ÷ total squads), meaningful once #55 squads exist.
The contract field ships now as `null`; the aggregation is a separate issue.

## Dependencies

- Consumes salary + rating (#52).
- Consumed by Web #16 (Fantasy Squad Builder UI); complements squad read (#54),
  mutations (#55), and the constraints endpoint (#66).
- Independent of #55 (the deferred `pickPercentage` removes that coupling).
