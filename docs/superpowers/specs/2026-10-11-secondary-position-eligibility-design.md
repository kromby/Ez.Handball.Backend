# Secondary Position Eligibility — Design Spec

**Issue:** [Backend#108](https://github.com/kromby/Ez.Handball.Backend/issues/108) — Allow players to be bought/started under their secondary position
**Date:** 2026-10-11
**Labels:** enhancement, game, fantasy-only

## Goal

#106 populated `Player.PositionSecondary` from HBStatz observations, but every rule still reads only the primary `Position`: squad `posLimit:{Position}` at buy time (`FantasyBuyPlayerFunction`), lineup `startMin/startMax:{Position}` (`LineupValidator.CheckPositions`), the automatic lineup (`DefaultLineup`) and bench-sub validity at settlement (`GameweekScoringService.KeepsPositionsValid`). This issue makes a player eligible under either position, without ever counting them against both at once.

## Scope decisions

Settled during brainstorming:

1. **Auto-solve, no manager choice.** A squad (or a set of starters) is legal iff *some* assignment of each player to one of their eligible positions satisfies the position limits. The backend finds that assignment; the manager never picks a "played-as" position and nothing new is stored per squad slot or lineup slot. Manually moving an owned player between positions is the follow-up [Backend#166](https://github.com/kromby/Ez.Handball.Backend/issues/166).
2. **Live eligibility.** Eligible positions are read from the current `Player` row (`Position` + `PositionSecondary`), not from the buy-time snapshot. `SquadEntryEntity.Position` / `GameRosterEntity.Position` stay as they are (primary at buy time) but no rule reads them for eligibility any more. A player who gains or loses a secondary mid-season is affected immediately. Squads that end up over a cap stay legal — caps gate only new buys. Frozen lineup snapshots are unaffected, but bench auto-subs are recomputed from live eligibility on re-settle, so rounds re-settled within `Settlement:RecentWindow` can change.
3. **Lineups too.** Lineup validation, the default lineup and bench subs all use the same solver as squad limits.
4. **Admin editing of positions** (including making admin-set values survive HBStatz re-derivation) is out of scope — [Backend#167](https://github.com/kromby/Ez.Handball.Backend/issues/167).

## Eligibility

```
Eligible(p) = { p.Position }                       if PositionSecondary is empty or equals Position
            = { p.Position, p.PositionSecondary }  otherwise
            = { }                                  if Position is null/empty (unknown)
```

A player with no known position is **unconstrained**: they satisfy no min and count against no max. This matches today's behaviour where an unknown position "never blocks".

## Component: `PositionAssignment` (Domain, pure)

New `Ez.Handball.Domain/PositionAssignment.cs`.

```csharp
public sealed record PositionCandidate(string PlayerId, IReadOnlyList<string> Eligible); // Eligible[0] = primary

public static class PositionAssignment
{
    // Returns playerId → assigned position (null for unconstrained players), or null when no
    // assignment meets every limit. Positions absent from `limits` have no min and no max.
    public static IReadOnlyDictionary<string, string?>? TrySolve(
        IReadOnlyList<PositionCandidate> players,
        IReadOnlyDictionary<string, (int Min, int Max)> limits);

    public static IReadOnlyList<string> EligibleFor(string? position, string? positionSecondary);
}
```

- **Algorithm:** depth-first backtracking over `players` in the given order. Each player tries `Eligible` in order (primary first), skipping a position already at its max. At a leaf, all mins are checked. Prune when the remaining players can't cover the outstanding min deficit. The worst case is 2¹⁵ leaves for a 15-player squad, so it's effectively instant.
- **Determinism:** callers pass players in a stable order (see below). Primary-first exploration means the first assignment found uses secondaries only where needed. The same input always gives the same assignment.
- Squad limits are built as `(0, posLimit)`. Lineup limits are `constraints.PositionStart` as-is.

## Rule changes

### Buy (`FantasyBuyPlayerFunction`)
- `BuyPlayerInputs` gains `IReadOnlyList<string> Eligible` for the candidate. `SquadSlot` gains `IReadOnlyList<string> Eligible` (live), populated by `GetBuyDecisionUseCase`. That use case looks each slot's player up via `IPlayerRepository.GetByIdAsync`, at most 15 lookups. If a player no longer resolves, it falls back to the slot's snapshot `Position`.
- `position_limit` fires iff `TrySolve(squad + candidate, posLimits)` is `null`. The code is unchanged, and the message names the candidate's primary position, as today. If the primary has no limit, the message is the generic "Squad position limits cannot accommodate this player".
- **Already-infeasible squad:** if the current squad *alone* has no valid assignment (it went over a cap after a live position change), the solve would block every buy. In that case the rule falls back to today's primary-only count for the candidate: block iff the squad slots whose primary equals the candidate's primary already reach that position's limit. This keeps "caps only gate new buys" true.
- The persisted `SquadEntry.Position` remains the candidate's primary.

### Lineup validation (`LineupValidator.CheckPositions`)
- `SquadPlayer` gains `PositionSecondary`, filled by `GetSquadUseCase` from the live player.
- Starters are solved against `PositionStart`. If an assignment exists, there's no position violation. If none exists, the existing primary-only count still runs to report the familiar `position_min` / `position_max` violations. If that count happens to find none (possible only through secondary interactions), emit a single `position_min` with the message "Starters cannot be arranged to meet the position rules."

### Default lineup (`DefaultLineup`)
- Same rating order (rating desc, then playerId). A player starts iff `starters.Count < StarterCount` and `TrySolve(starters + player, maxOnly(PositionStart))` succeeds. That replaces `HasRoom`. The function is still deterministic, so a re-settle freezes the same lineup.

### Bench subs (`GameweekScoringService.KeepsPositionsValid`)
- `posById` becomes `eligibleById`. A swap is valid iff an assignment exists for `decidedEffective + candidate` where every max is respected, and every position in the outgoing starter's eligible set has at least its min. Only those positions' mins are enforced, which is the same conservative stance as today. For single-position players under the current config (only GK has a non-zero min), this gives the same results as the current rule.

## API / view changes (additive)

- **Player pool filter:** in `GetPlayerPoolUseCase`, `?position=X` matches `Position == X` **or** `PositionSecondary == X`. `/players` and `/shortlist` share this via `PlayerHubTable`, so both pick it up.
- **Squad view** (`SquadPlayer` → `GET` squad): add `positionSecondary` and `assignedPosition`. The latter is the slot from solving the squad against `posLimit`, with players ordered by rating desc and then playerId. If the squad has no valid assignment (over a cap after a live change), `assignedPosition` is the primary. `GetSquadUseCase` gains `ISquadConstraintsRepository` (fixed version `fantasy-squad-v1`, as `GetBuyDecisionUseCase` uses); missing constraints → primary. Both new `SquadPlayer` fields are optional trailing parameters (`= null`) so existing constructions compile unchanged.
- **Lineup view** (`LineupView` → lineup endpoints): add `positionSecondary` and `assignedPosition`. Starters take the slot from the lineup solve; bench players take their primary.
- Existing `position` fields keep meaning "primary". No codes are renamed and no fields removed.

## Out of scope

- A manager picking or moving a player's position (#166).
- Admin editing and sticky overrides (#167).
- Persisting `assignedPosition` on settled gameweeks or into `GameRoster`.
- Any change to the seeded constraint values.

## Testing

xUnit in `Ez.Handball.Tests`, following existing fixtures.

- **`PositionAssignmentTests` (new):**
  - Single-position players match plain counting.
  - A dual player fills a gap at their secondary.
  - Greedy trap: A(CB/LB) + B(CB) with CB max 1 succeeds, with A→LB.
  - Infeasible returns null; mins are enforced (GK=1).
  - Unknown-position players are unconstrained; positions without a limit are unconstrained.
  - The same input gives the same assignment, and primaries are preferred.
- **`FantasyBuyPlayerFunctionTests`:** CB full and the candidate has an LB secondary → allowed. CB full and an existing squad member can move → allowed. No one can move → `position_limit`.
- **`LineupValidatorTests`:** no natural GK, but a starter with a GK secondary → valid. Existing single-position cases are unchanged.
- **`DefaultLineupTests`:** a high-rated dual player starts at their secondary when the primary is at max. Existing cases are unchanged.
- **`GameweekScoringServiceTests`:** a bench player whose secondary matches the absent starter's position is subbed in. Existing sub cases are unchanged.
- **Use case / endpoint tests:** the pool filter matches on the secondary. Squad and lineup responses include `positionSecondary` and `assignedPosition`. `GetBuyDecisionUseCase` passes live eligibility for squad slots.
- **Regression gate:** the full existing suite stays green.
