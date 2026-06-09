# Lineup & Captaincy — Design Spec

**Issue:** [Backend#61](https://github.com/kromby/Ez.Handball.Backend/issues/61) — Lineup & captaincy: formation, starting 7 vs bench, captain/vice multipliers
**Date:** 2026-06-09
**Labels:** enhancement, fantasy-only, game

## Goal

Owning players is not a lineup. This issue introduces the **formation model** layered on top of the owned squad (#54): a valid **starting 7 + ordered bench** with per-position constraints, plus **captain / vice-captain** selection carrying a points multiplier. This is the structure the gameweek engine (#60) will actually score.

## Scope decisions

These were settled during brainstorming and shape the whole design:

1. **Round-agnostic "current" lineup.** The issue calls lineups "per-round" and "frozen at the deadline," but the round/gameweek engine (#60) does not exist yet and in fact *depends on* this issue. So #61 stores a single **current** lineup per team, always editable. No round binding, no deadline lock. #60 later adds the round dimension and the freeze on top of this model.

2. **Formation = exactly 1 GK + per-position min/max.** A valid starting 7 requires exactly one goalkeeper and six court players, with configurable min/max per court position (LW/RW/LB/CB/RB/LP). The min/max live in versioned config and are tuning knobs, not carved-in-stone gameplay rules.

3. **Captain required, vice optional.** A valid lineup must name a captain (a starter); a vice is optional but, if set, must be a distinct starter. The x2 multiplier and vice-promotion logic are applied later by scoring (#60); #61 only stores and validates the picks.

4. **Stored lineup + validity flag on read.** The read endpoint returns exactly what the manager saved (validated at set-time) plus a freshly-computed `isValid` + violations re-checked against the *current* squad. It never auto-mutates. If a started player was later sold (via #53/#55), the lineup reads back as invalid and the manager fixes it before the deadline. No coupling into the sell path.

## Out of scope

- Scoring the lineup (#60).
- Buying/selling players (#53 / #55) and per-round transfer limits (#63).
- Manager-flavor modifications — the manager flavor short-circuits, consistent with #53/#54.
- The round lifecycle, deadlines, and freeze (#60).

## Domain model

`Ez.Handball.Domain`:

```
LineupRole (enum) : Bench | Starter | Captain | Vice
  // Captain and Vice ARE starters carrying the multiplier badge.
  // "Starters" = any slot whose Role ∈ {Starter, Captain, Vice}.

LineupSlot (record)
  PlayerId   : string
  Role       : LineupRole
  BenchOrder : int?        // set iff Role == Bench (0-based priority); null otherwise

Lineup (record)
  Slots : IReadOnlyList<LineupSlot>

LineupConstraints (record)   // versioned, read from Config
  Version            : int
  StarterCount       : int                                       // 7
  PositionStart      : IReadOnlyDictionary<string,(int Min,int Max)>  // per-position min/max among starters
  CaptainMultiplier  : double                                    // 2.0 — stored here, applied by #60
  CaptainRequired    : bool                                      // true
  ViceRequired       : bool                                      // false

LineupViolation (record)     // (Code, Message); mirrors BuyRuleViolation
LineupValidation (record)
  IsValid    : bool
  Violations : IReadOnlyList<LineupViolation>
```

**Why a single role enum instead of separate `Slot` + `Captaincy` fields:** captain/vice must be starters, so a separate captaincy field would let us represent "captain on the bench" — an illegal state we'd then have to validate away. Collapsing into one role makes captain/vice-on-bench *and* captain==vice unrepresentable by construction. Validation is left checking only counts and positions.

The lineup carries only `PlayerId` + role + bench order. **Position is never sent by the client** — the validator resolves each player's position from their owned roster slot (`GameRosterEntity.Position`), keeping logic server-side (server owns logic, UI is dumb).

## Constraints & config

A new versioned config group `fantasy-lineup-v{n}` in the existing `Config` table, read by `TableLineupConstraintsRepository` (mirrors `TableSquadConstraintsRepository` reading `fantasy-squad-v{n}`). This is a **separate version line** from `fantasy-squad-v{n}` (squad-building rules), since lineup rules evolve independently. Default version 1, overridable via `ruleSetVersion` query param like the squad endpoints.

Seeded by a new `SeedLineupConstraintsFunction` (`POST /api/seed/lineup-constraints`), mirroring `SeedSquadConstraintsFunction`. Placeholder values, owner-tunable — flagged in a comment like the squad-constraints seed (the position vocabulary must be reconciled with real `Player.Position` values):

```
fantasy-lineup-v1  starterCount        7
fantasy-lineup-v1  captainMultiplier   2
fantasy-lineup-v1  captainRequired     true
fantasy-lineup-v1  viceRequired        false
fantasy-lineup-v1  startMin:GK         1     startMax:GK   1
fantasy-lineup-v1  startMin:LW         0     startMax:LW   2
fantasy-lineup-v1  startMin:RW         0     startMax:RW   2
fantasy-lineup-v1  startMin:LB         0     startMax:LB   3
fantasy-lineup-v1  startMin:CB         0     startMax:CB   2
fantasy-lineup-v1  startMin:RB         0     startMax:RB   3
fantasy-lineup-v1  startMin:LP         0     startMax:LP   2
```

`GK` min=max=1 encodes "exactly one keeper." Court min/max are deliberately loose so most squads can field a valid 7. `captainMultiplier` lives here so #60 reads it without #61 applying it. Position keys reuse the same vocabulary as the squad `posLimit:` keys.

## Validation rules

A pure domain function — `LineupValidator.Validate(Lineup proposed, IReadOnlyList<SquadPlayer> ownedSquad, LineupConstraints constraints) → LineupValidation`. No I/O; reused verbatim by both set (reject) and read (annotate). Each failed check appends a `LineupViolation(Code, Message)`.

Checks:

| Code | Rule |
|------|------|
| `unowned_player` | Every slot's `PlayerId` is in the owned squad. |
| `incomplete_squad` | Every owned player appears exactly once in the lineup (starters ∪ bench == owned set, no gaps). |
| `duplicate_slot` | No `PlayerId` appears twice. |
| `wrong_starter_count` | Exactly `starterCount` (7) slots have a starter role. |
| `position_min` / `position_max` | Among starters, each position's count is within `[Min, Max]` — position resolved from the owned roster snapshot, not the request. Covers "exactly 1 GK." |
| `missing_captain` | A `Captain` slot exists when `captainRequired`. |
| `missing_vice` | A `Vice` slot exists when `viceRequired` (default false → never fires). |
| `bench_order` | Bench slots carry a contiguous `BenchOrder` 0..(n-1), no dupes/gaps; starters carry none. |

`incomplete_squad` enforces that **the whole owned squad must be placed** — 7 start, the rest are benched in priority order — so #60 always has a complete, ordered bench for auto-subs. A squad with fewer than 7 owned players therefore can't produce a valid lineup (it fails `wrong_starter_count`).

There is no captain/vice-must-be-starter or captain≠vice check — the role enum makes both impossible.

## Persistence

New `GameLineups` table (added to `Tables.cs`), one row per placed player — mirrors `GameRosters`:

```
GameLineupEntity : ITableEntity
  PartitionKey : string   // teamId = GameTeamId.For(userId, Fantasy)
  RowKey       : string   // playerId
  Role         : string   // "Bench" | "Starter" | "Captain" | "Vice"
  BenchOrder   : int?     // set iff Role == "Bench"
```

No soft-delete column. Unlike the roster (which keeps history for sell/resurrect), a lineup is a complete snapshot wholly replaced each save — there's no per-player lifecycle to preserve.

`ILineupRepository`:

```
Task<Lineup?> GetAsync(string teamId, CancellationToken ct);          // null = never set
Task ReplaceAsync(string teamId, Lineup lineup, CancellationToken ct); // full reconcile
```

`ReplaceAsync` reconciles the row set the way `TableNotificationPreferenceRepository` does: all rows share the team's partition key, so the write uses a `TableTransactionActions` batch over the single partition for atomicity (upsert the new set, delete rows no longer present). Validation already guarantees internal consistency before this runs.

`TableLineupConstraintsRepository` reads the `fantasy-lineup-v{n}` config group exactly like `TableSquadConstraintsRepository` reads `fantasy-squad-v{n}`.

## Use cases

`Ez.Handball.Application`:

**`GetLineupUseCase.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct)`:**
1. Load owned squad (reuse `IGetSquadUseCase` → `SquadView`, giving enriched names/positions/prices for the response).
2. Load `fantasy-lineup-v{n}` constraints → `RuleSetNotFound` if missing.
3. Load stored lineup. If none → `NotSet` (endpoint returns an empty-lineup body, `isValid: false`).
4. Run `LineupValidator` against the *current* squad → attach `isValid` + violations. Return stored slots enriched with player name/position/price + captaincy.

Result shape (mirrors `GetSquadResult`):
```
GetLineupResult : RuleSetNotFound | NotSet | Found(LineupView)
```

**`SetLineupUseCase.ExecuteAsync(userId, proposedLineup, context, ct)`:**
1. Fantasy team exists? else `NoTeam` (409, mirrors buy).
2. Load owned squad + constraints (`RuleSetNotFound` → 400).
3. `LineupValidator.Validate(...)`. Invalid → `Rejected(violations)` (422, mirrors buy).
4. Valid → `ILineupRepository.ReplaceAsync`. Return the same enriched view as GET.

Result shape (mirrors `BuyPlayerResult`):
```
SetLineupResult : NoTeam | RuleSetNotFound | Rejected(violations) | Committed(LineupView)
```

## API contract

`LineupEndpoints`, authed group `/api/users/me/lineup`, fantasy-only `IsFantasy(flavor)` short-circuit, `userId` from token — identical conventions to `SquadEndpoints`.

```
GET /api/users/me/lineup?flavor=&season=&tournamentId=&ruleSetVersion=
  200 { flavor, starters:[…], bench:[…ordered], captainId, viceId,
        isValid, violations:[], captainMultiplier }
  400 invalid_flavor | invalid_rule_set
  401 unauthorized

PUT /api/users/me/lineup            (full replacement — idempotent)
  body { flavor?, season?, tournamentId?, ruleSetVersion?,
         starters:[ {playerId, role} ],   // role ∈ Starter|Captain|Vice
         bench:[ playerId, … ] }          // array order = bench priority
  200 { …same shape as GET… }
  400 invalid_flavor | invalid_rule_set | malformed_body
  401 unauthorized
  409 no_team
  422 { violations:[ {code, message} ] }
```

Contract choices:
- **PUT, not POST/PATCH** — the lineup is one resource per team that you fully replace; PUT is idempotent and matches "set the lineup."
- **Request encodes roles structurally** — `starters[]` carry `role` (captain/vice ride along with the starter list); `bench[]` is a plain ordered `playerId` array where order *is* the priority. The server derives `BenchOrder` from array index and resolves positions from the roster. The client never sends positions or bench indices.
- **Response splits the stored single-role model** back into `starters`/`bench`/`captainId`/`viceId` for client convenience, and echoes `captainMultiplier` from config so the UI can show "×2" without a second call.

Each starter/bench entry in the response is enriched (playerId, name, clubName, position, price) from the `SquadView`, plus its role.

## Testing

Follows the existing layout (xUnit + Moq for units, Azurite for table integration):

- **`LineupValidatorTests`** (domain, pure) — one test per violation code (unowned, incomplete squad, duplicate, wrong starter count, position min, position max, missing captain, bench-order gaps) + a happy-path valid 7+bench. No mocks.
- **`GetLineupUseCaseTests` / `SetLineupUseCaseTests`** (Moq) — `NoTeam`, `RuleSetNotFound`, `NotSet`, `Rejected` carries violations, valid set → `ReplaceAsync` called, and the stale-squad case: a stored lineup whose player was sold reads back `isValid:false` + `unowned_player`.
- **`TableLineupRepositoryTests`** (Azurite) — replace-then-get round-trip, reconcile drops rows no longer present, bench order preserved.
- **`TableLineupConstraintsRepositoryTests`** (Azurite) — reads `fantasy-lineup-v1`, null when absent, parses min/max keys.
- **`SeedLineupConstraintsFunctionTests`** — idempotent upsert, count.
- **`LineupEndpointTests`** (WAF) — auth required (401), invalid flavor (400), 422 on invalid body, 200 round-trip GET after PUT.

## DI wiring

Register `ILineupRepository`, `ILineupConstraintsRepository`, `GetLineupUseCase`, `SetLineupUseCase`, and `LineupValidator` in the same composition root as the squad services; map `LineupEndpoints` alongside `SquadEndpoints`. The `Jwt__SigningKey` test-isolation gotcha (#69/#64) is already handled by `TestModuleInitializer`, so WAF lineup tests inherit it.

## Dependencies

- **Builds on** #54 (squad persistence — owned roster + `GameTeamId`) and the #53 squad-constraint config pattern.
- **Required by** #60 (gameweek scoring reads the starting 7 + captain multiplier + bench order), and reused by #63 (chips) and #64 (onboarding draft).
- Fantasy-only: the manager flavor short-circuits.
