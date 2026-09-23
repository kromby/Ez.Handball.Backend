# Transfer rules & chips: free transfers, point hits, cross-round budget, chips

Issue [#63](https://github.com/kromby/Ez.Handball.Backend/issues/63).
**Date:** 2026-09-10. **Labels:** enhancement, game, fantasy-only. **Mode:** Fantasy-only (manager flavor short-circuits, consistent with #53).

## Goal

Builds on #53/#55/#58 (budget, buy/sell, transfer ledger) to add the per-round rules that
create strategy: a free-transfer allowance that banks and rolls over, a point-hit cost for
transfers beyond that allowance, correct attribution of transfers made while a round is
locked, and three chips (wildcard, bench boost, triple captain) that modify the transfer or
scoring rules for a single round each, limited per season.

## Today

None of this exists yet. Squad/budget/ledger (#53/#55/#58), the gameweek lifecycle
(`Open → DeadlineLocked → InPlay → Settled`, #60), and lineup/captaincy (#61) are fully
implemented — buy/sell mutate a live (non-round-scoped) squad and budget balance, gated by
`IGameweekSnapshotGuard` before every mutation. There is no free-transfer counter, no point-hit
concept on `GameweekScore`, and no chip of any kind.

## Scope decisions

1. **One spec for the whole epic.** Free transfers/hits, cross-round attribution, and chips
   are separate mechanics but share the same round-boundary seam (`IGameweekSnapshotGuard`,
   `Gameweek.RoundLabel`) and are small enough individually to combine, matching the depth of
   existing multi-concern specs (gameweek-engine, lineup-and-captaincy).
2. **Free-transfer accrual: FPL-style bank.** +1 free transfer granted per round crossed,
   banked (rolls over) up to a configurable cap (default 5). Round 1 (before the first
   deadline) is unlimited — there's no prior squad to have penalized changes to.
3. **Point hit: −4 per transfer beyond the bank**, configurable via Config KV, deducted at
   settlement (not at transfer time) so the `GameweekScore` for a round carries both the raw
   score and the hit.
4. **Locked-round attribution: always to the next round.** A transfer made once the current
   round is `DeadlineLocked`/`InPlay`/`Settled` can't affect that round (its lineup is already
   frozen) — it's attributed to the next round's free-transfer bank/hit count instead of being
   blocked outright.
5. **Chip season limits**: wildcard ×2 (split across a configurable half-season boundary —
   a fixed round-number cutoff, since no season-phase concept exists in code and total round
   count isn't known ahead of the season), bench boost ×1, triple captain ×1. All configurable
   via Config KV, not hardcoded. **No carryover**: the two wildcard slots are counted
   independently as "used at `Round <= cutoff`" and "used at `Round > cutoff`" — an unused
   first-half wildcard does not become a second usable wildcard in the second half; it's
   simply forfeited (there is no explicit "forfeit" write, this falls out of the count check
   itself).
6. **Triple captain doesn't carry to a promoted vice.** If the captain didn't play, the vice
   gets the normal vice multiplier — the chip is tied to the captain slot specifically.
7. **Bench boost disables auto-subs for that round.** Every bench player already counts, so
   there's no gap left for an auto-sub to cover.
8. **Extensibility, not scope**: the free-transfer bank is a plain per-team adjustable counter
   (same shape as the budget balance) specifically so a *future*, out-of-scope rule (e.g.
   crediting a low-performing team a bonus free transfer) can hook in as just another caller of
   the same atomic adjust operation. No such rule is built here.

## Out of scope

- Core buy/sell mechanics (#55) and the transfer ledger (#58) — unchanged, reused as-is.
- Lineup/captaincy mechanics (#61) — chips reference the lineup/captain slots but don't
  redefine them.
- Any performance-based or admin-granted bonus free transfers (see scope decision 8 — the
  architecture supports it, nothing here implements it).
- A production all-teams settlement fan-out — settlement stays per-team/poke-driven, as today;
  the debug replay fan-out (#96) is a separate, non-production concern.
- Season reset / un-spending transfers or chips.

## Domain model

```csharp
// Ez.Handball.Domain/FreeTransferConfig.cs
public sealed record FreeTransferConfig(
    int StartingFreeTransfers,       // default 1, granted per round crossed
    int MaxBankedFreeTransfers,      // default 5, cap on the running bank
    int PointHitCost,                // default 4, points deducted per paid transfer
    bool FirstRoundUnlimited);       // default true — no bank/hits before round 1's deadline

// Ez.Handball.Domain/ChipType.cs
public enum ChipType { Wildcard, BenchBoost, TripleCaptain }

// Ez.Handball.Domain/ChipConfig.cs
public sealed record ChipConfig(
    int WildcardSeasonLimit,            // default 2
    int BenchBoostSeasonLimit,          // default 1
    int TripleCaptainSeasonLimit,       // default 1
    int? SecondWildcardUnlocksAtRound); // e.g. 12 — round number; null = no split enforced

// Ez.Handball.Domain/FreeTransferBank.cs
public sealed record FreeTransferBank(int Bank, string LastAccruedRound);

// Ez.Handball.Domain/GameweekScore.cs (extended)
public sealed record GameweekScore(
    /* existing fields */
    int RawPoints,
    int PointsHit,          // new
    int Points);            // = RawPoints - PointsHit
```

Config groups follow the existing recipe exactly (§ Constraints & config below).

## Constraints & config

| Config group | Repository | Partition key | Record |
|---|---|---|---|
| `fantasy-transfers-v{n}` | `TableFreeTransferConfigRepository` | `fantasy-transfers-v1` | `FreeTransferConfig` |
| `fantasy-chips-v{n}` | `TableChipConfigRepository` | `fantasy-chips-v1` | `ChipConfig` |

Both are seeded once per environment by a one-shot `Seed*ConfigFunction` in
`Ez.Handball.Ingestion/Functions/`, matching `SeedGameweekConfigFunction` /
`SeedSquadConstraintsFunction`. Seed defaults: `StartingFreeTransfers=1`,
`MaxBankedFreeTransfers=5`, `PointHitCost=4`, `FirstRoundUnlimited=true`,
`WildcardSeasonLimit=2`, `BenchBoostSeasonLimit=1`, `TripleCaptainSeasonLimit=1`,
`SecondWildcardUnlocksAtRound` set once the season's round count is known.

## Components

### Storage (Ez.Handball.Shared / Infrastructure)

```csharp
// FreeTransferBankEntity — PartitionKey = teamId, RowKey = "state" (one row per team)
public class FreeTransferBankEntity : ITableEntity
{
    public int Bank { get; set; }
    public string LastAccruedRound { get; set; }
}

// GameweekTransferStateEntity — PartitionKey = teamId, RowKey = roundLabel
// (mirrors GameweekLineups / GameweekScores: one row per team per round)
public class GameweekTransferStateEntity : ITableEntity
{
    public int PaidTransfersCount { get; set; }
}

// GameweekChipActivationEntity — PartitionKey = teamId, RowKey = roundLabel
// row exists only when a chip is active that round
public class GameweekChipActivationEntity : ITableEntity
{
    public string ChipType { get; set; }
}
```

Tables: `GameFreeTransferBanks`, `GameweekTransferState`, `GameweekChipActivations` (new
entries in `Ez.Handball.Infrastructure/Tables.cs`).

### Application abstractions

```csharp
public interface IFreeTransferBankRepository
{
    // Walks LastAccruedRound -> throughRound via the calendar from IGameweekCalendarService
    // (bounded: stops early once Bank hits MaxBankedFreeTransfers, never iterates the whole
    // season for a long-idle team). ETag-retry, same idiom as TableGameBudgetRepository.
    Task<FreeTransferBank> AccrueAsync(string teamId, string throughRound, FreeTransferConfig cfg, CancellationToken ct);

    // Decrements Bank by 1 if > 0 (ETag-retry). False => caller records a paid transfer instead.
    Task<bool> TryConsumeAsync(string teamId, CancellationToken ct);
}

public interface IGameweekTransferStateRepository
{
    Task IncrementPaidAsync(string teamId, string round, CancellationToken ct); // ETag-retry upsert
    Task<int> GetPaidCountAsync(string teamId, string round, CancellationToken ct);
}

public interface IChipActivationRepository
{
    Task<ChipType?> GetActiveAsync(string teamId, string round, CancellationToken ct);
    Task SetAsync(string teamId, string round, ChipType? chip, CancellationToken ct); // null clears it
    Task<IReadOnlyList<(string Round, ChipType Type)>> ListUsedAsync(string teamId, CancellationToken ct);
    // partition-scan for the team; season-limit counting reads this directly, no separate counter row
}
```

`Table*Repository` implementations in `Ez.Handball.Infrastructure/TableAccess/` follow the
`TableGameBudgetRepository` ETag-retry (up to 5 retries on HTTP 412) pattern throughout.

### Use cases (Ez.Handball.Application/UseCases)

- **`BuyPlayerUseCase` / `SellPlayerUseCase` (modified)** — after the existing
  `IGameweekSnapshotGuard.EnsureSnapshotsAsync` call:
  1. Resolve the *effective round*: `SnapshotGuardResult.CurrentGameweekLocked` ? next
     calendar round : current round (reuses a value the guard already computes).
  2. `IChipActivationRepository.GetActiveAsync` for the effective round — if `Wildcard`,
     skip steps 3–4 entirely (free, uncounted).
  3. `IFreeTransferBankRepository.AccrueAsync` (no-op before round 1 if
     `FirstRoundUnlimited`) then `TryConsumeAsync`.
  4. On `TryConsumeAsync` false, `IGameweekTransferStateRepository.IncrementPaidAsync` for the
     effective round.
  Placed in the same position as the existing budget-deduct call; same retry idiom, easy to
  diff against.
- **`SetChipUseCase` / `GetChipUseCase` (new)** — same shape as `SetLineupUseCase`: team-exists
  → `EnsureSnapshotsAsync` guard (rejects a locked target round) → season-limit check via
  `ListUsedAsync` (count by `ChipType`; `Wildcard` split by `Round <= SecondWildcardUnlocksAtRound`
  vs. after) → one-active-chip-per-round check → `SetAsync`. Violations follow the existing
  `BuyRuleViolation`-style 200-with-violations shape: `chip_limit_exceeded`,
  `chip_already_active_this_round`, `round_locked`.
- **`SettleGameweekUseCase` (modified)** — before/during the scoring rollup:
  1. Read chip activation for (team, round).
  2. Scoring pass: `BenchBoost` active → sum every lineup slot (starters + bench), auto-sub
     skipped entirely. Otherwise unchanged. `TripleCaptain` active and captain played →
     captain multiplier `3.0`; otherwise the normal `LineupConstraints.CaptainMultiplier`
     (2.0). Vice-captain fallback always uses its own normal multiplier, never `3.0`.
  3. `RawPoints` computed as today; `PointsHit = (Wildcard active for this round) ? 0 :
     GetPaidCountAsync(teamId, round) * cfg.PointHitCost`; `Points = RawPoints - PointsHit`.
  Re-running settlement stays idempotent: once a round is locked, no further transfer can
  attribute to it (step 1 above always routes to the *next* round), so `PaidTransfersCount`
  and the chip activation for a given round are stable by the time settlement first runs.

### Endpoints (Ez.Handball.Api)

- `PUT /api/users/me/chips` (auth) — body `{ round, chipType }` (or `{ round, chipType: null }`
  to clear) → `200` current activation or `409`/`422` with violations.
- `GET /api/users/me/chips` (auth) → activation history + remaining season counts per chip.
- No new endpoint for free transfers/hits — surfaced as part of the existing
  `GET /api/users/me/gameweeks` response (bank balance, this round's paid count, hit applied).

## Data flow — a transfer during a locked round

1. Manager calls `POST /api/squad/players` after this round's deadline has passed.
2. `EnsureSnapshotsAsync` confirms `CurrentGameweekLocked = true` (lineup already frozen).
3. Effective round resolves to next round. Chip check: no wildcard active next round.
4. `AccrueAsync` catches the bank up through next round; `TryConsumeAsync` — bank was 0, so
   `IncrementPaidAsync` bumps next round's `PaidTransfersCount` to 1.
5. Buy proceeds as today (budget deduct, roster write, ledger append) — unaffected.
6. When next round later settles, `PointsHit = 1 * 4 = 4` is read and subtracted.

## Testing

Same conventions as `BuyPlayerUseCaseTests` / `SettleGameweekUseCaseTests` (xUnit + Moq, fixed
clock, `Sut()` factory):

- **Accrual**: catch-up capping at `MaxBankedFreeTransfers`, bounded iteration for a long-idle
  team, `FirstRoundUnlimited` bypass before round 1's deadline.
- **Consumption**: free vs. paid branching, round-attribution when the current round is
  locked.
- **Chips**: season-limit enforcement per chip, wildcard half-season split, one-active-chip-
  per-round, rejection once the target round is locked.
- **Settlement**: wildcard nullifies the hit, bench boost includes bench without auto-subs,
  triple captain multiplier vs. non-carryover to a promoted vice, idempotent re-run with hits
  and chips applied yields the same score.
- **Infrastructure (Azurite)**: `FreeTransferBankRepositoryTests` / `GameweekTransferStateRepositoryTests`
  / `ChipActivationRepositoryTests` mirroring `TableGameBudgetRepositoryTests`'s ETag-retry
  coverage.
- **Endpoint**: `SetChipUseCase` HTTP contract (200/409/422), auth, `GET` shape.
