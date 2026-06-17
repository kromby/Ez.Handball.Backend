# Admin advance-and-settle fan-out (settle every team for a round)

Sub-task [#96](https://github.com/kromby/Ez.Handball.Backend/issues/96) of the
time-shift replay harness epic [#93](https://github.com/kromby/Ez.Handball.Backend/issues/93).
Depends on the now-merged domain clock ([#94](https://github.com/kromby/Ez.Handball.Backend/issues/94))
and virtual-now finality gate ([#95](https://github.com/kromby/Ez.Handball.Backend/issues/95)).
See the epic spec `2026-06-17-time-shift-replay-harness.md` for the whole picture.

## Goal

Give the replay harness two debug controls so walking a season is one call per step instead
of one call per manager:

1. **Move the virtual clock** — set an absolute `now`, advance to the next gameweek deadline
   or the next round boundary, or clear the override.
2. **Settle a round across every team** — enumerate fantasy teams and run the existing
   per-team settlement for each, idempotently.

Plus a convenience that does both in one step: advance to the next round boundary and settle
that round.

## Today

- `POST /api/gameweeks/settle` is per authenticated user — it resolves only the caller's own
  team (`GameTeamId.For(userId, Fantasy)`). There is no path to settle all managers.
- The virtual `now` lives in a `Config` row (`debug-clock-v1` / `virtualNow`) read by
  `GameClock`, but it is set only by hand-editing the table — there is no setter endpoint.
- `TriggerSettlementFunction` is a logging stub ("fan-out deferred").

Moving the clock is passive: settlement is poke-driven, never clock-polling. So the harness
needs an explicit trigger to settle after each clock move.

## Architecture

All new logic follows the existing clean-arch split (dumb API edge → use-case layer →
repository layer). Settlement logic is reused untouched.

```
Ez.Handball.Api/DebugReplayEndpoints.cs   (new; mapped only when override flag on, behind a secret)
        │
        ├── AdvanceClockUseCase ──────► IClockOverrideStore        (new write seam)
        │        (reads current now via injected TimeProvider/GameClock
        │         + IGameweekCalendarService for boundary math)
        │
        └── SettleRoundForAllTeamsUseCase ──► ILineupRepository.ListTeamIdsAsync   (new query)
                 (loops per team) ──────────► ISettleGameweekUseCase               (existing, unchanged)
```

**Approach: loop the existing per-team use case.** `SettleRoundForAllTeamsUseCase` enumerates
teams and calls the proven `ISettleGameweekUseCase` once per team. Zero change to the settlement
logic; idempotency and the finality gate come for free. It redundantly re-reads config / calendar
/ rule-set / played-stats per team, which is irrelevant for a debug-only replay over a handful of
managers. Rejected: refactoring a shared round-level core, or a batch scoring path — both refactor
working, tested code for a debug path's benefit.

`SettleRoundForAllTeamsUseCase` is deliberately the reusable seam the production
`TriggerSettlementFunction` fan-out can later call (that wiring is out of scope here).

## Components

### 1. `IClockOverrideStore` (write seam)

New Application abstraction; Table implementation in Infrastructure.

- `SetAsync(DateTimeOffset utc, CancellationToken ct)` — upsert the `debug-clock-v1` /
  `virtualNow` `Config` row, value formatted as an ISO-8601 UTC instant (round-trips through
  `GameClock.TryReadVirtualNow`, e.g. `2025-09-01T17:00:00Z`).
- `ClearAsync(CancellationToken ct)` — delete the row. An absent row means no override (wall
  clock), per the clock spec.

Writes go through the normal async `ITableQuery` path the rest of Infrastructure uses.
`GameClock` keeps its existing synchronous point-read for reads — this seam is write-only.

| Field | Value |
|-------|-------|
| Table | `Config` |
| PartitionKey | `debug-clock-v1` (`GameClock.OverrideGroup`) |
| RowKey | `virtualNow` (`GameClock.OverrideKey`) |
| Value | ISO-8601 UTC instant |

### 2. `AdvanceClockUseCase`

Reads "current virtual now" via the injected `TimeProvider` (the `GameClock`, single source of
truth) and the calendar via `IGameweekCalendarService.GetCalendarAsync`. Four operations, each
returning the resulting virtual `now` (and, for round mode, the round label landed on):

- **Set absolute** — validate the supplied instant, normalize to UTC, `SetAsync`.
- **Advance to next deadline** — the earliest gameweek `Deadline` strictly greater than current
  now; `SetAsync` to it. Exercises lock behaviour (`Open → DeadlineLocked`).
- **Advance to next round** — the next round not yet all-final; target =
  `max(member match Date) + MatchFinalBufferHours`. That value exactly trips the
  `date + buffer <= now` finality test, so every match in the round becomes final and the round
  reads ready. `SetAsync` to it; return that round's label.
- **Clear** — `ClearAsync`.

Guard: if the master enable flag is off, the use case refuses with a clear error (the write
would be a no-op the clock ignores) rather than silently appearing to succeed. No-result cases
(no future deadline / no further unsettled round) return a distinct "nothing to advance to"
outcome.

### 3. `SettleRoundForAllTeamsUseCase` (the fan-out)

- New `ILineupRepository.ListTeamIdsAsync(CancellationToken ct)` — enumerates team IDs that have
  a live lineup (projection scan of the lineups table).
- Filter to fantasy teams (suffix `:fantasy`); derive `userId` by stripping that fixed suffix
  (robust even if a userId itself contains `:`).
- For each team, call the existing
  `ISettleGameweekUseCase.ExecuteAsync(userId, teamId, round, version, ct)`.
- Short-circuit the whole round if the round itself is unresolvable or not ready: a probe (or the
  first team's result) of `ConfigMissing` / `NotFound` / `NotReady` is reported once for the round
  rather than per team.
- Aggregate the per-team outcomes into a report:
  - `round` (label)
  - `teamsConsidered`
  - `settled` — count scored
  - `notReady` — count where the finality gate hasn't opened
  - `skipped` — count of `NoSnapshotPossible` / `SquadNotFound` (team has no lineup/squad to score)

### 4. Endpoints (`Ez.Handball.Api/DebugReplayEndpoints.cs`)

Three POST endpoints under `/api/debug/`:

- `POST /api/debug/clock` — body `{ mode: "set" | "advance-deadline" | "advance-round" | "clear", date? }`
  → `{ virtualNow, enabled }` (and `round` when mode is `advance-round`).
- `POST /api/debug/settle-round?round={label}&version={n?}` → the fan-out report.
- `POST /api/debug/advance-and-settle` → advance to the next round boundary, then settle that one
  round across all teams → `{ virtualNow, round, report }`. Endpoint-level composition of
  `AdvanceClockUseCase` (advance-round) + `SettleRoundForAllTeamsUseCase` — no extra use case.

Per the clarified scope, each call settles exactly one round (the caller loops round-by-round
across the season).

### 5. Gating (two layers)

1. **Master kill-switch.** `app.MapDebugReplayEndpoints()` is wrapped in
   `if (builder.Configuration.GetValue("Debug:GameClock:OverrideEnabled", false))`. In production
   the flag is off, so the routes are never mapped (404). This reuses the epic's existing
   enable flag — the same switch that makes the clock override effective at all.
2. **Shared secret.** An endpoint filter requires header `X-Debug-Key` to equal config
   `Debug:AdminKey`. If the flag is on but `Debug:AdminKey` is unset/blank, the endpoints refuse
   (secure default — never an open door). Mismatched/missing header → 401.

The virtual clock is scoped to domain/game time only (per the epic guardrail): auth token expiry,
rate limiting, and log timestamps stay on the wall clock and are unaffected.

## Data flow — one replay step

1. `POST /api/debug/advance-and-settle` (header `X-Debug-Key`).
2. `AdvanceClockUseCase` reads current virtual now, finds the next not-all-final round, writes
   `virtualNow = max(fixture Date) + MatchFinalBufferHours` via `IClockOverrideStore`.
3. The round's matches now read final through the `GameClock`-driven finality gate.
4. `SettleRoundForAllTeamsUseCase` enumerates teams with a live lineup, settles each via
   `ISettleGameweekUseCase` (snapshot-if-missing, score, save).
5. Response reports the new `virtualNow`, the `round`, and the settled/not-ready/skipped counts.

## Idempotency

Re-invoking is safe. `SettleGameweekUseCase` re-scores an already-settled team to the same result
(`IGameweekScoreRepository.SaveAsync` upsert) and returns `NotReady` for any round the finality
gate has not opened. So a re-run settles only what is newly ready and recomputes the rest to the
same value. Moving the clock back recomputes scores idempotently; it does not un-spend transfers
(a season reset is a separate concern, out of scope per the epic).

## Testing

- **`SettleRoundForAllTeamsUseCaseTests`**: fan-out over multiple teams (mixed `Settled` /
  `NotReady`); a not-ready round short-circuits and settles none; idempotent re-run yields
  identical scores; empty team set; non-fantasy teams filtered out; `skipped` counts a
  no-lineup/no-squad team.
- **`AdvanceClockUseCaseTests`** (fake `TimeProvider` + in-memory calendar): next-deadline picks
  the earliest deadline strictly after now; next-round sets now to last-fixture + buffer and the
  round then reads ready; clear removes the row; set-absolute round-trips; flag-off refuses;
  no-further-round / no-future-deadline returns the "nothing to advance" outcome.
- **`ClockOverrideStoreTests`**: `SetAsync` writes a row `GameClock` reads back as the same
  instant; `ClearAsync` deletes it.
- **Endpoint / gating (WAF integration, mirroring existing endpoint tests)**: flag off → routes
  return 404; missing/wrong `X-Debug-Key` → 401; flag on but no `Debug:AdminKey` configured →
  refused; happy-path `advance-and-settle` returns the report.

## Guardrails

- Admin-gated and behind the master enable flag; not reachable in production (flag off → 404).
- The shared secret stops any random authenticated caller on a debug-enabled host.
- Domain clock scoped to game time only; auth/rate-limit/log clocks untouched.

## Out of scope

- Wiring the production blob `TriggerSettlementFunction` fan-out (it can later call
  `SettleRoundForAllTeamsUseCase`).
- Real-time acceleration / time-scale multipliers (manual stepping only).
- Rewinding mutable manager state (transfers, chips, budget); a season reset is separate.
- Settling multiple rounds in one call — each call settles exactly one round by design.
