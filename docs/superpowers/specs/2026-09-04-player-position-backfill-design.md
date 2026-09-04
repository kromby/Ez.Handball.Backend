# Player Position Backfill — Design Spec

**Issue:** [Backend#106](https://github.com/kromby/Ez.Handball.Backend/issues/106) — Player position data is unreliable/missing from hsi.is — blocks squad & lineup position rules
**Date:** 2026-09-04
**Labels:** bug, data, game, fantasy-only

## Goal

`Player.Position` drives squad-building position limits (`posLimit:{Position}`) and lineup starter rules (`startMin/startMax:{Position}`), but it's populated verbatim from hsi.is's `POSITION` field, which is empty/unreliable for many players. Both `SeedSquadConstraintsFunction.cs` and `SeedLineupConstraintsFunction.cs` hardcode a guessed 7-code vocabulary (GK/LW/RW/LB/CB/RB/LP) flagged as "PLACEHOLDER... pending owner review." This issue replaces that guesswork with a trustworthy, maintained source of truth for `Position`.

## Scope decisions

These were settled during brainstorming and shape the whole design:

1. **HBStatz is the source, via its JSON API.** HBStatz's `game.php` endpoint (e.g. `hbstatz.is/api/game.php?id=12933`) now returns a `position` and `position_secondary` field per player — no HTML scraping needed. This is already fetched by `HbStatzApiClient.GetGameJsonAsync` for stats enrichment; it's just not parsed yet. The vocabulary maps cleanly onto the existing placeholder codes:

   | HBStatz `position` | Code |
   |---|---|
   | Goalkeeper | GK |
   | Left Wing | LW |
   | Right Wing | RW |
   | Left Back | LB |
   | Right Back | RB |
   | Center | CB |
   | Line | LP |

   Any unrecognized string is skipped (no observation recorded), keeping the mapping resilient to future HBStatz label changes. `is_goalkeeper` is not used as a separate signal — it should always agree with `position == "Goalkeeper"`.

2. **Identity matching still goes through the existing reconciler.** `HbStatzPlayerReconciler.Resolve` already matches an `HbStatzPlayerLine` to a roster `PlayerEntity` by jersey/name — position data cannot be used to help find the player, since hsi.is's position is exactly what's unreliable.

3. **HBStatz always wins over hsi.is.** When HBStatz has a position for a player, it overwrites whatever hsi.is put in `Position`, unreliable-by-definition. `PlayerParser.cs` is otherwise unchanged — it keeps copying hsi.is's raw `POSITION` value as a weak initial fallback for players HBStatz hasn't reached yet.

4. **Primary/secondary position by observation frequency.** Rather than trusting any single match, `Position` (primary) is the mode across all of a player's HBStatz-observed matches. `PositionSecondary` is the second-most-frequent observed code, but only if it accounts for more than 10% of that player's total observations — otherwise left empty. A tie for primary breaks by whichever code was observed first chronologically (earliest match), keeping the result deterministic.

5. **Secondary position is stored data only — no eligibility change.** `PositionSecondary` is persisted for future use, but squad `posLimit` accounting and `LineupValidator.CheckPositions` continue to check only the primary `Position`, exactly as today. Multi-position buy/lineup eligibility is out of scope here — tracked separately as [Backend#108](https://github.com/kromby/Ez.Handball.Backend/issues/108).

6. **A persistent observation table, not blob rescanning or a running counter.** Reprocessing archived HBStatz blobs on every incremental sync (to recompute a player's full history) is wasteful and blobs aren't keyed by team. A running counter would need extra bookkeeping to stay idempotent under retries. Instead, one row per (player, match) observation is the natural fit for Table Storage: upserts are naturally idempotent (reprocessing the same match just replaces the same row), and mode/secondary computation is a cheap partition query.

7. **One aggregation path serves both the one-time backfill and ongoing maintenance.** The historical backfill and the incremental per-sync update both write to the same observation table and recompute `Position`/`PositionSecondary` the same way — the only difference is which set of matches they process.

## Out of scope

- Using `PositionSecondary` for squad-buy or lineup-start eligibility (Backend#108).
- Changing how squad `posLimit` / lineup `startMin`/`startMax` values themselves are tuned — only the vocabulary they're keyed on is being confirmed as real, not placeholder.
- Investigating exactly how often hsi.is's `POSITION` field is populated/correct (useful context, not a blocker to this design).
- Any change to `HbStatzFixtureMatcher`, `HbStatzCompetitionMap`, or how matches get selected for HBStatz sync in the first place.

## Data model

`Ez.Handball.Ingestion/Models/HbStatzGameResponse.cs` — `HbStatzPlayerLine` gains:
```
Position          : string   // from "position"
PositionSecondary : string   // from "position_secondary" — deserialized, not used yet
```

`Ez.Handball.Shared/Entities/PlayerEntity.cs` and `Ez.Handball.Domain/Player.cs` gain:
```
PositionSecondary : string   // plain string, no enum, mirrors Position
```

New table `PlayerPositionObservations`:
```
PartitionKey = PlayerId
RowKey       = MatchId
Position     = <code>   // GK/LW/RW/LB/CB/RB/LP, the mapped code observed in this match
```

## Components

**`HbStatzPositionMapper`** (new, static) — maps an HBStatz `position` string to the fantasy vocabulary code, or `null` if unrecognized.

**`HbStatzPlayerPositionAggregator`** (new, static) — given a set of reconciled `(PlayerEntity, HbStatzPlayerLine, MatchId)` triples:
1. Maps and upserts one `PlayerPositionObservations` row per triple (skipping unmapped positions).
2. For each affected player, queries their full `PlayerPositionObservations` partition, computes the mode (`Position`) and qualifying second-most-frequent code (`PositionSecondary`, >10% share), and merges onto `PlayerEntity` if changed.

**`TriggerHbStatzSyncFunction`** (existing, modified) — after its existing per-match reconciliation and stat merge, feeds the same reconciled lines through `HbStatzPlayerPositionAggregator`. This is what keeps `Position`/`PositionSecondary` maintained as new matches are ingested, with no separate manual step.

**`BackfillPlayerPositionsFunction`** (new, HTTP-triggered, function-key auth, dry-run-by-default — same shape as `MergePlayersFunction`/`TransferPlayersFunction`) — enumerates all archived `hbstatz/matches/*.json` blobs (for HBStatz-enabled tournaments), re-runs reconciliation against each match's roster, and feeds the results through the same aggregator. Rerunnable and idempotent; this is the one-time historical backfill for matches synced before this feature existed, since `TriggerHbStatzSyncFunction` won't reprocess matches with `HbStatzSyncedAt` already set.

**`SetPlayerPositionFunction`** (new, HTTP-triggered, function-key auth, dry-run-by-default, batch input) — manual fallback for players HBStatz can't reach (tournament not HBStatz-enabled, reconciliation never succeeds, or the player never appears in a synced match). Validates `Position`/`PositionSecondary` against the canonical vocabulary and full-entity-merges. Never conflicts with the automated path, since the aggregator only writes when it has actual observations.

## Cleanup

- `SeedSquadConstraintsFunction.cs` / `SeedLineupConstraintsFunction.cs`: remove the "PLACEHOLDER... pending owner review" comments now that the vocabulary is confirmed real.
- Update the three existing spec docs that reference the placeholder vocabulary (`2026-06-07-squad-constraints-endpoint-design.md`, `2026-06-09-lineup-and-captaincy-design.md`, `2026-06-07-priced-player-pool-and-detail-enrichment-design.md`) to note it's now resolved by this issue.

## Testing

- `HbStatzPositionMapper`: unit tests for every known label, case sensitivity, and unrecognized input.
- `HbStatzPlayerPositionAggregator`: unit tests for mode computation, the >10% secondary threshold, the chronological tie-break, and idempotent re-processing of the same match.
- `TriggerHbStatzSyncFunction`: integration test confirming `Position`/`PositionSecondary` land on `PlayerEntity` after a sync run, following the project's existing test patterns for this function.
- `BackfillPlayerPositionsFunction` / `SetPlayerPositionFunction`: dry-run vs. live-write tests, following `MergePlayersFunctionTests`/`TransferPlayersFunctionTests` conventions.

## Related

- Depends on none; unblocks correct squad/lineup position enforcement for beta.
- Backend#108 — allow buying/starting players under their secondary position (follow-up, depends on this issue).
- Backend#4 — broader player-stat schema enrichment; related but independently scoped, per the original issue.
