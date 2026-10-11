# Manager Position Pins — Design Spec

**Issue:** [Backend#166](https://github.com/kromby/Ez.Handball.Backend/issues/166) — Let managers move a dual-position player between their eligible positions
**Date:** 2026-10-11
**Depends on:** #108 (dual-position eligibility via `PositionAssignment` auto-solve), merged

## Goal

Since #108, the backend auto-solves which position (primary or secondary) each player fills. #166 lets a manager override that: "play X at LB, not CB". The override is a **pin**. It lives with the player while owned, is honoured by the live lineup, and is **frozen per gameweek** with the deadline snapshot, so a later change never alters a locked round.

## Scope decisions (settled in brainstorming)

1. **Lineup-level choice, frozen per gameweek.** The pin affects the manager's lineup: default-lineup starter selection, lineup validation, bench subs, and the squad/lineup views. It does **not** affect buying; squad caps keep auto-solving as in #108.
2. **Pin overlay, never a saved lineup.** Pinning a player does not create or alter a saved lineup. A manager without a saved lineup keeps playing `DefaultLineup` (still rating-ordered), which now honours pins.
3. **Storage on the roster row.** A new nullable `GameRosterEntity.PlayAs` column; null means auto-solve. The existing `Position` column (buy-time primary) is **not** reused: the over-cap buy fallback and match-line display still read it with that meaning.
4. **Frozen with the snapshot.** `GameweekLineupEntity` gains a nullable `PlayAs`. Scoring reads pins from the snapshot, never from the live roster. Snapshots frozen before deploy have no pins and score exactly as today.

## Eligibility with pins

```
Natural(p)   = PositionAssignment.EligibleFor(p.Position, p.PositionSecondary)       // #108
ValidPin(p)  = p.PlayAs if p.PlayAs is non-blank and Natural(p) contains it, else null
Eligible(p)  = ValidPin(p) is { } pin ? [pin] : Natural(p)
```

A **stale pin** (the player lost that position after an HBStatz recompute) is ignored everywhere: the player is treated as unpinned. It is never an error on read.

## Components

### Domain
- `RosterEntry` gains `string? PlayAs = null` (trailing optional).
- `SquadSlot` gains `string? PlayAs = null`. Squad caps (`SquadSlot.EligiblePositions`) ignore it, since buying is unchanged.
- `SquadPlayer` gains `string? PlayAs = null` and:
  - `NaturalPositions` (= #108 `EligibleFor`),
  - `ValidPlayAs` (the pin when valid, else null),
  - `EligiblePositions`, which becomes **pin-aware**: `[ValidPlayAs]` when pinned, otherwise `NaturalPositions`.

  Because `DefaultLineup`, `LineupValidator`, `LineupViewMapper` and `GetSquadUseCase` already read `EligiblePositions`, they honour pins without further changes.
- `LineupSlot` gains `string? PlayAs = null`, set only on effective and frozen lineups.

### Persistence
- `GameRosterEntity.PlayAs` (nullable). `TableGameRosterRepository`:
  - maps it into `RosterEntry`;
  - `AddOrResurrectAsync` clears it on resurrect (a re-bought player starts unpinned);
  - new `SetPlayAsAsync(teamId, playerId, string? playAs, ct)` reads the active row, sets `PlayAs`, and replaces it with the row's ETag. It returns `SetPlayAsOutcome { Updated, NotFound, Conflict }`, where `NotFound` covers both no row and a soft-deleted row.
- `TableSquadRepository` passes `PlayAs` into `SquadSlot`. `GetSquadUseCase` passes it into `SquadPlayer`.
- `GameweekLineupEntity.PlayAs` (nullable). `TableGameweekLineupRepository` writes and reads it into `LineupSlot.PlayAs`.

### Effective lineup (new `IEffectiveLineupService`, Application/Services)
`Task<Lineup?> GetAsync(string teamId, SquadView squad, LineupConstraints constraints, ct)`:
- Use the saved lineup (`ILineupRepository`), or else `DefaultLineup.From(squad.Players, constraints)`. Return null when the result has no slots.
- Stamp each slot with `PlayAs = squadPlayer.ValidPlayAs`, where the slot's player is in the squad.

`GameweekSnapshotGuard.EffectiveLineupAsync` and `SettleGameweekUseCase` (snapshot-if-missing) both use it instead of their own saved-or-default code. Snapshots therefore freeze pins.

### Scoring (`GameweekScoringService`)
Per player, eligibility = the snapshot slot's `PlayAs` if it is non-blank and in the owned player's `NaturalPositions`, else `NaturalPositions`. The live `SquadPlayer.EligiblePositions` must **not** be used here, because it would leak post-deadline pin changes into locked rounds. Everything else (#108 solver, covered positions, assigned-position mins) is unchanged.

### Squad view `assignedPosition`
`GetSquadUseCase` solves with pin-aware eligibility first. If that is infeasible, it re-solves with `NaturalPositions` (#108 behaviour). If that also fails, each player shows their primary. The squad and lineup JSON add `playAs` (the valid pin, or null) next to `assignedPosition`.

### API
Under the existing squad group `/api/users/me/squad` (authorized):

| Verb | Route | Body | Success |
|---|---|---|---|
| PUT | `/players/{playerId}/position` | `{ "position": "LB" }` | 200 `{ squad, gameweek }` |
| DELETE | `/players/{playerId}/position` | — | 200 `{ squad, gameweek }` (no-op if not pinned) |

`gameweek` is `GameweekEcho.BuildAsync(...)`, as buy and sell use.

**`SetPlayAsUseCase`** (`ISetPlayAsUseCase.ExecuteAsync(userId, playerId, string? position, BuyPlayerContext context, ct)`; a null position means clear), in order:
1. No fantasy team → `NoTeam` → 409 `{ error: "no_team" }`.
2. `IGameweekSnapshotGuard.EnsureSnapshotsAsync(teamId, null, ct)`, **before** any read for validation or any write, so locked rounds freeze the old pins.
3. Squad via `IGetSquadUseCase`. `RuleSetNotFound` → 400 `{ error: "invalid_rule_set" }`. Player not in the squad → `NotInSquad` → 404 `{ error: "not_in_squad" }` (the same code sell uses).
4. Setting only: `position` not in the player's `NaturalPositions` → `NotEligible` → 422 `{ error: "position_not_eligible" }`. The comparison is ordinal, and `position` is trimmed and upper-cased first. Pinning to the primary is allowed.
5. Setting only, and only if the team has a **saved** lineup: run `LineupValidator.Validate(saved, squadWithNewPin, constraints)`. If it returns any `position_min`/`position_max` violation → `Rejected(violations)` → 422 `{ error: "lineup_invalid", violations: [{ code, message }] }`, and nothing is written. Other pre-existing violations in a saved lineup (e.g. a missing captain) don't block a pin.
6. `IGameRosterRepository.SetPlayAsAsync`. `Conflict` → 409 `{ error: "conflict" }`; `NotFound` → `NotInSquad`.
7. Re-read the squad → `Updated(SquadView)`.
8. Log one Information line: team id, player id, position (or "cleared"). Never personal data.

Invalid `flavor` → 400 `invalid_flavor`; missing user → 401, mirroring sell.

## Out of scope
- The Web "move player" UI (separate Web issue once this ships).
- Pins affecting buy validation or squad caps.
- Per-round history of pins beyond what the snapshot freezes.
- Admin position edits (#167).

## Testing
- **Domain:**
  - `SquadPlayer.EligiblePositions` with valid, stale and blank pins.
  - `DefaultLineup`: a pinned high-rated player starts only at their pin, and is benched when that position is at its max.
  - `LineupValidator`: a pinned starter counts only at their pin.
- **Persistence (Azurite):**
  - `SetPlayAsAsync` sets and clears `PlayAs` and leaves the other columns alone.
  - A resurrect clears `PlayAs`.
  - `NotFound` for a missing or soft-deleted row.
  - The snapshot repository round-trips `PlayAs`, and legacy rows read as null.
- **`EffectiveLineupService`:** saved lineup + pins; default lineup + pins; stale pin; empty squad → null.
- **Guard and settlement:** both delegate to the service. A frozen snapshot carries `PlayAs`.
- **Scoring:**
  - An absent starter frozen at LB needs an LB-capable sub.
  - A frozen bench pin limits what that sub can cover.
  - A live pin differing from the snapshot is ignored.
- **`SetPlayAsUseCase`:**
  - Happy set and clear.
  - `NoTeam`, `NotInSquad`, `NotEligible`, and saved-lineup `Rejected` with nothing written.
  - The guard runs before the write (Moq `MockSequence` or a recorded call order).
- **Endpoints:** PUT and DELETE status codes and bodies; `playAs` and `assignedPosition` in the squad and lineup JSON.
- **Regression:** the full suite. Check first that no other session holds the Azurite ports (`pgrep -fa azurite`).
