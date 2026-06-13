# Gameweek Engine — Design Spec

**Issue:** [Backend#60](https://github.com/kromby/Ez.Handball.Backend/issues/60) — Gameweek engine: round lifecycle, squad lock, scoring rollup, auto-subs
**Date:** 2026-06-13
**Mode:** Fantasy-only

## Goal

The core fantasy loop. A gameweek is the scoring period that ties a manager's **locked lineup** to real match results, computes per-round points, applies the captain multiplier, and maintains a running total. Today nothing connects a squad to a scoring period — `#10` lists matches by round but is a read-only view, not a lifecycle/scoring entity.

## Decisions (resolved during brainstorming)

1. **Gameweek scope:** a *configurable fantasy calendar* — a per-season config names the fantasy tournament; gameweeks are not hardcoded to one tournament in code.
2. **Gameweek definition:** *auto-derived from HSÍ round labels* — the engine groups the fantasy tournament's matches by their `Round` label; the league's own round structure is the gameweek structure.
3. **Locking:** *lazy / derived, no timer.* A gameweek's deadline = *(earliest member-match throw-off) − `lockOffsetHours`*. The lock is enforced at mutation time (buy/sell/lineup edit), not by a scheduled job.
4. **Lineup freeze:** *lazy snapshot* — a per-(gameweek, team) frozen lineup copy captured at the first moment it's needed (first post-deadline mutation, or settlement), keyed per gameweek so overlapping/postponed rounds never cross wires.
5. **Settlement / scoring:** *triggered by ingestion, implemented in Application.* All computation lives in the Application layer (request-time capable, so a future manager flavor can compute rating live); ingestion holds no scoring logic and only pokes settlement. The fantasy flavor persists results; read endpoints serve stored scores.
6. **Auto-subs:** *FPL-style, position-valid*, with captain→vice armband fallback.
7. **Gameweek representation:** *derived calendar, persist only the deltas* (Approach A) — matches stay the single source of truth; we persist only the non-derivable state (pinned deadline, frozen snapshots, settled scores).
8. **Gameweek status to UI:** a *dedicated endpoint is the canonical source of truth*; read responses are not fattened with a gameweek block. Only the mutating endpoints echo a minimal `{ appliedToGameweek, currentGameweekLocked }` marker to close the lock-between-load-and-submit race.

## Architecture

### Layering

- **Application** owns calendar derivation, scoring rollup, and auto-subs as use cases/services — callable request-time.
- **Ingestion** stays dumb: after it parses a match's stats and the match is final, it invokes the settlement API endpoint. It contains no scoring logic.
- **API** exposes read endpoints (calendar, current, my-scores), the settlement endpoint, and the existing lock-aware mutating endpoints.

### Derived (not persisted)

**`GameweekCalendarService` (Application).** Reads the `Matches` table for the configured fantasy tournament + season, groups by HSÍ `Round` label (reusing the `#10` grouping/sort logic), and produces a `Gameweek` view:

```text
Gameweek {
  Number,        // ordinal position of the round in sorted order
  RoundLabel,    // HSÍ round label = the gwKey
  TournamentId,
  Deadline,      // earliest member-match throw-off − lockOffsetHours,
                 //   overridden by the pinned value when one exists
  Status,        // see below
  Matches[]
}
```

**Status** (derived, preserves the issue's 4 states):

| Status | Condition |
|---|---|
| `Open` | now &lt; deadline |
| `DeadlineLocked` | now ≥ deadline, no member match final yet |
| `InPlay` | ≥1 member match final, not all |
| `Settled` | all member matches final **and** rolled up |

**Current editable gameweek** = the earliest gameweek whose deadline has **not** passed. There is no global "current" pointer; everything keys off `gwKey`, so several gameweeks can be non-terminal at once (a postponed match keeps GW *n* in `InPlay` while GW *n+1* is already the editable one).

### Persisted — three new Game-prefixed tables (`Tables.cs`)

| Table | PartitionKey | RowKey | Columns | Written when |
|---|---|---|---|---|
| `GameweekLocks` | `tournamentId` | `gwKey` (round label) | `PinnedDeadline`, `LockedAt` | first time a deadline is observed as passed — pins it so a later reschedule can't move an already-passed deadline |
| `GameweekLineups` | `{teamId}\|{gwKey}` | `playerId` | `Role`, `BenchOrder` | lazy snapshot — frozen copy of the lineup at lock (mirrors `GameLineupEntity`) |
| `GameweekScores` | `teamId` | `gwKey` | `Points`, `CaptainPlayerId`, `BreakdownJson` | at settlement; `Replace` mode → idempotent/recomputable |

- `gwKey` = the HSÍ round label (stable per tournament, survives postponements since the label doesn't change).
- `teamId` = the existing `{userId}:fantasy` composite (`GameTeamId.For`).
- Running total per team = sum of that team's `GameweekScores` rows (few gameweeks; cheap — no separate stored counter to drift).

## Lifecycle, lock & snapshot flow

### Lazy-lock + lazy-snapshot guard

Runs inside every mutation use case (`BuyPlayerUseCase`, `SellPlayerUseCase`, `SetLineupUseCase`):

```text
Before applying the mutation:
  1. Load the gameweek calendar for this team's tournament/season.
  2. For every gameweek whose (pinned-or-derived) deadline has passed
     AND has no GameweekLineups snapshot for this team yet:
        - pin its deadline in GameweekLocks if not already pinned
        - copy the team's current live lineup into GameweekLineups[{teamId}|{gwKey}]
  3. Apply the mutation to the LIVE squad/lineup (GameRosters / GameLineups).
```

**Consequence:** once a gameweek is locked, its snapshot is frozen by `playerId`, so buy/sell/lineup edits after the deadline flow to the **live** records (next gameweek) and **cannot** alter the locked gameweek's score — the issue's "edits after deadline apply to the next round." No transfer is blocked; the snapshot is what protects the locked round.

### Snapshot-if-missing at settlement

If a team never touched anything after the deadline, no mutation fired the guard — so `SettleGameweekUseCase` takes the same snapshot itself (live lineup, unchanged since before the deadline → identical result) before scoring. A snapshot is therefore guaranteed to exist exactly once per (team, locked gameweek), via whichever path comes first. Both paths snapshot the *current live lineup*, which is provably unchanged since the deadline because every intervening mutation would have snapshotted first.

### Deadline pinning edge

Deadlines are derived (recompute as fixtures move) *until* first observed as passed, then pinned. A fixture moved *earlier* than an already-pinned deadline does not unlock a locked gameweek; a fixture moved earlier while still `Open` just recomputes normally.

### Overlap / postponement

When a match in round *n* is postponed, HSÍ keeps it under the same round label with a later date. Gameweek *n* still owns it; the deadline is set by the round's earliest (on-time) throw-off, so GW *n* still locks on schedule but cannot reach `Settled` until the straggler lands. Because snapshots and scores key off `gwKey`, GW *n* awaiting its straggler while GW *n+1* is live needs no special handling — settlement for GW *n* simply re-runs (idempotently) when the postponed match is finally played.

## Scoring rollup & auto-subs

### `GameweekScoringService` (Application, shared / request-time-capable)

Inputs: the frozen `Lineup` snapshot, the gameweek's member matches, each lineup player's `PlayerStatEntity` rows for those matches, the configured `ScoringRuleSet`, and the `CaptainMultiplier` from `LineupConstraints`.

**Per-player points** reuse the existing `FantasyPlayerRatingFunction` applied to a *single match's* stats (the season-rating formula run with `Games = 1`). This is the calibrated-scoring hook the issue calls for — `#27`'s calibration plugs in as a `ScoringRuleSet` version; no scoring numbers are baked into the engine, and there is no parallel scoring formula.

**Availability primitive:** a player **played** a gameweek iff they have a `PlayerStatEntity` row for one of that gameweek's member matches. No row → did-not-play. Covers injury/suspension/benched with zero new ingestion fields; extends cleanly later via `#4`.

### Auto-sub algorithm (FPL-style, position-valid)

```text
played(p) := p has a PlayerStatEntity row in this gameweek's matches
1. Start from the 7 frozen starters.
2. For each starter who did NOT play, in turn:
     scan bench by ascending BenchOrder for the first player who
       (a) played, AND
       (b) whose promotion keeps the lineup valid under LineupValidator
           (exactly 1 GK; per-position min/max preserved).
     If found -> promote (starter drops to 0, sub scores). Else -> starter scores 0.
3. Captain armband: if the captain did not play, the vice inherits the
   multiplier; if the vice also did not play, no multiplier is applied.
4. Gameweek points = Σ (effective-starter points), with the captain/vice
   multiplier applied to the armband holder.
```

The (b) validity check reuses the existing `LineupValidator` rather than re-encoding position rules — single source of truth for a legal formation.

### `SettleGameweekUseCase`

For a gameweek whose member matches are all final: snapshot-if-missing → run `GameweekScoringService` per team that has a snapshot → write `GameweekScores` with `Replace` (idempotent). Re-running after a stat correction or a postponed match landing recomputes identically and overwrites. A gameweek with a still-pending match isn't all-final, so it stays `InPlay` and settles when the straggler lands.

## API surface

### Read endpoints (public, like the other read endpoints)

- `GET /api/gameweeks?tournamentId=&season=` — the calendar: every gameweek with `{ number, roundLabel, deadline, status, matches[] }`. Defaults to the configured fantasy tournament + current season via `ITournamentScopeResolver`.
- `GET /api/gameweeks/current` — the editable gameweek (earliest not past deadline) plus the most recent settled one.
- `GET /api/users/me/gameweeks` *(authed, fantasy-only)* — the manager's per-gameweek scores + running total, each with the stored breakdown (per-player points, who was auto-subbed, captain applied).

### Settlement endpoint (authed / internal)

- `POST /api/gameweeks/settle?round=` (optional `&version=`) — runs `SettleGameweekUseCase` for the **authenticated caller's own team** (the team is derived from the auth token, not passed as a parameter). Idempotent. Called by ingestion after a match's stats finish parsing and the match is final; also usable manually for recompute.

### Mutating endpoints (existing, now lock-aware)

`POST/DELETE /api/users/me/squad/players` and `PUT /api/users/me/lineup` run the lazy snapshot guard before applying, and add a minimal `{ appliedToGameweek, currentGameweekLocked }` echo so the client can reconcile a lock that happened between page load and submit. Pure reads are not fattened.

## Config / seeding

New `SeedGameweekConfigFunction` writing Config partition `fantasy-gameweek-v{version}`:

| Key | Meaning |
|---|---|
| `tournamentId` | the fantasy tournament for the season (the configurable calendar) |
| `lockOffsetHours` | X — hours before first throw-off that the gameweek locks (owner sets the real value) |
| `scoringRuleSetVersion` | which `ScoringRuleSet` the rollup uses (`#27` plugs in here) |
| `lineupConstraintsVersion` | which `LineupConstraints` supplies captain multiplier + position rules |

Read by a `TableGameweekConfigRepository` (mirrors the existing constraint repos). Ops: seed per environment, like the other Seed functions.

## Testing (xUnit + Moq, repo conventions)

- `GameweekCalendarService`: round grouping, deadline = earliest − offset, pinned-deadline override, status transitions, overlapping/postponed rounds.
- Lazy snapshot guard: snapshots once before first post-deadline mutation; snapshot-if-missing at settlement; edits after lock cannot alter a frozen gameweek.
- `GameweekScoringService` / auto-subs: non-playing starter → valid promotion; no-eligible-sub → 0; GK-only-replaced-by-GK; captain→vice fallback; captain multiplier math.
- `SettleGameweekUseCase`: idempotent re-run, stat-correction absorption, not-all-final stays `InPlay`.

## Idempotency

`GameweekScores` / `GameweekLineups` / `GameweekLocks` all use `Replace`; settlement is a pure function of (frozen snapshot + match stats + rule set), so re-running is safe and convergent — satisfying the issue's "idempotent, recomputable settlement that absorbs stat corrections."

## Out of scope

- Scoring point *values* — `#27` calibration (this engine consumes a `ScoringRuleSet`, exposing the hook).
- Lineup/formation validation and captaincy *mechanics* — `#61` (merged); reused here.
- Manager leaderboard surfacing — `#62`.
- Chips that alter scoring — `#63`; the engine exposes the rule-set/multiplier hooks but implements no chips.

## Frontend follow-up

GitHub issues in `kromby/Ez.Handball.Web`:

1. Consume the gameweek calendar/current endpoints — status, deadline countdown.
2. Show per-gameweek score + breakdown + running total.
3. Lock-aware buy/sell/lineup UX using the `appliedToGameweek` / `currentGameweekLocked` echo.

## Dependencies

- Builds on `#54`/`#55` (squad persistence) and `#61` (lineup + captain), both merged.
- Consumes `#27` (scoring values) as a pluggable rule-set — `#27` may land after this engine.
- Relates to `#10` (round listing — reuses grouping logic), `#4` (stat schema — future availability enrichment), `#18` (deadline / round-result notifications).
- Feeds `#62` (manager standings).
