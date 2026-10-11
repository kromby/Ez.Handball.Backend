# Manager Position Pins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a manager pin an owned dual-position player to their primary or secondary position. The pin is honoured by the live lineup and frozen per gameweek with the deadline snapshot (Backend#166).

**Architecture:**
- A nullable `PlayAs` on the roster row flows into `SquadSlot`, then `SquadPlayer`. `SquadPlayer.EligiblePositions` becomes pin-aware, so `DefaultLineup`, `LineupValidator` and the views honour pins without further changes.
- A new `EffectiveLineupService` stamps pins onto the live lineup for both the snapshot guard and settlement. Snapshots persist `PlayAs`, and scoring reads pins only from the snapshot.
- New PUT/DELETE endpoints under `/api/users/me/squad/players/{playerId}/position`.

**Tech Stack:** .NET 8, C#, Azure Table Storage, xUnit, Moq, Azurite.

**Spec:** `docs/superpowers/specs/2026-10-11-manager-position-pins-design.md` (read it first; it is the binding authority).

## Global Constraints

- Worktree `/home/kromby/source/Handbolti/wt-166-position-pins`, branch `feat/166-manager-position-pins`. Never touch `/home/kromby/source/Handbolti/Ez.Handball.Backend` (another process holds its index lock).
- Build: `dotnet build Ez.Handball.sln`. Tests: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "..."`.
  - Azurite tests need the emulator. Before starting one, run `pgrep -fa azurite`. If another session's Azurite already holds ports 10000–10002, **do not start a second one**: run your Azurite-backed tests against it, and treat unrelated endpoint/auth/mini-league failures as noise from that session (report them, don't fix them).
  - Otherwise start one with `azurite --silent --location /tmp/claude-1000/-home-kromby-source-Handbolti/a50117e9-4a58-4f39-ae03-11de34f43e04/scratchpad/azurite166 &` and stop it when done.
- Commits: Conventional Commits, imperative lowercase subject <70 chars, **no Co-Authored-By or any trailer lines**, do not push.
- New record parameters are **optional trailing** (`= null`), so existing constructions compile unchanged.
- Position codes are compared `StringComparer.Ordinal`. User input is trimmed and upper-cased before comparing.
- Error codes:
  - existing: `no_team`, `not_in_squad`, `invalid_rule_set`, `invalid_flavor`, `position_min`, `position_max`;
  - new: `position_not_eligible`, `lineup_invalid`, `conflict`.
- Comments: short *why* comments in the surrounding style. Keep business logic in Application/Domain, persistence in Infrastructure, HTTP thin.
- Scoring must read pins **only** from the snapshot slot (`LineupSlot.PlayAs`), never from the live `SquadPlayer.PlayAs`.

## Review Focus

1. **Stale pin** (the player lost that position after an HBStatz recompute) is ignored everywhere: not an error, the player is treated as unpinned. Tested in Task 1 and Task 5.
2. **A post-deadline pin change must not alter a locked round:** the guard runs before the write (Task 6), and scoring ignores live pins (Task 5).
3. **A resurrected (re-bought) player comes back unpinned.** Tested in Task 2.
4. **Legacy snapshot rows without `PlayAs`** read as null and score as before. Tested in Task 3.
5. **A pin that would break a saved lineup** is rejected with nothing written. Pre-existing non-position violations in a saved lineup don't block a pin. Tested in Task 6.

---

### Task 1: Pin-aware domain records

**Files:**
- Modify: `Ez.Handball.Domain/RosterEntry.cs`, `Ez.Handball.Domain/Squad.cs` (`SquadSlot`), `Ez.Handball.Domain/SquadView.cs` (`SquadPlayer`), `Ez.Handball.Domain/Lineup.cs` (`LineupSlot`)
- Test: `Ez.Handball.Tests/Domain/SquadPlayerEligibilityTests.cs` (new), `Ez.Handball.Tests/Domain/DefaultLineupTests.cs`, `Ez.Handball.Tests/Domain/LineupValidatorTests.cs`

**Interfaces produced:**
- `RosterEntry(string PlayerId, string? Position, double PricePaidAmount, DateTimeOffset? DeletedAt, string? PlayAs = null)`
- `SquadSlot(..., IReadOnlyList<string>? Eligible = null, string? PlayAs = null)`. `SquadSlot.EligiblePositions` is unchanged and ignores `PlayAs`.
- `SquadPlayer(..., string? AssignedPosition = null, string? PlayAs = null)` with:
  - `IReadOnlyList<string> NaturalPositions => PositionAssignment.EligibleFor(Position, PositionSecondary);`
  - `string? ValidPlayAs => !string.IsNullOrWhiteSpace(PlayAs) && NaturalPositions.Contains(PlayAs, StringComparer.Ordinal) ? PlayAs : null;`
  - `IReadOnlyList<string> EligiblePositions => ValidPlayAs is { } pin ? new[] { pin } : NaturalPositions;`
- `LineupSlot(string PlayerId, LineupRole Role, int? BenchOrder, string? PlayAs = null)`. Comment: the position this slot is played at when the manager pinned it (#166); set only on effective/frozen lineups.

- [ ] **Step 1: failing tests.**
  - `SquadPlayerEligibilityTests`:
    - (a) no pin → `EligiblePositions == ["CB","LB"]` for CB/LB;
    - (b) `PlayAs="LB"` → `["LB"]`, and `ValidPlayAs == "LB"`;
    - (c) `PlayAs="CB"`, the primary → `["CB"]`;
    - (d) stale `PlayAs="RW"` → `NaturalPositions`, `ValidPlayAs == null`;
    - (e) `PlayAs="  "` → natural.
  - `DefaultLineupTests`: squad GK 9, CB 8, CB 7, `pinned` (CB/LB, `PlayAs="CB"`) 6, LW, RW, LP, RB lower. CB max is 2, so `pinned` must be **benched**. Unpinned it would start at LB (the existing #108 test proves that); the assertion is `Assert.Contains("pinned", Ids(lineup, LineupRole.Bench))`.
  - `LineupValidatorTests`: six natural starters (GK, LW, RW, LP, CB, CB) plus `Dual("d","CB","LB") with { PlayAs = "CB" }` → contains `position_max` for CB. The same squad unpinned is valid (an existing test).
- [ ] **Step 2:** run them; expect compile failure (no `PlayAs`).
- [ ] **Step 3:** implement the record changes above. Update the `SquadPlayer` header comment to mention the pin.
- [ ] **Step 4:** run `--filter "FullyQualifiedName~Domain|FullyQualifiedName~Lineup|FullyQualifiedName~Scoring|FullyQualifiedName~Squad"`; all pass.
- [ ] **Step 5:** commit `feat(domain): add position pins to squad and lineup records`.

### Task 2: Roster persistence and squad plumbing

**Files:**
- Modify:
  - `Ez.Handball.Shared/Entities/GameRosterEntity.cs`
  - `Ez.Handball.Application/Abstractions/IGameRosterRepository.cs`
  - `Ez.Handball.Infrastructure/TableAccess/TableGameRosterRepository.cs`
  - `Ez.Handball.Infrastructure/TableAccess/TableSquadRepository.cs`
  - `Ez.Handball.Application/UseCases/GetSquadUseCase.cs`
  - `Ez.Handball.Api/SquadEndpoints.cs` (`SquadBody`)
- Test:
  - `Ez.Handball.Tests/Infrastructure/Tables/TableGameRosterRepositoryTests.cs` (Azurite)
  - `Ez.Handball.Tests/Application/UseCases/GetSquadUseCaseTests.cs`
  - `Ez.Handball.Tests/Api/Endpoints/SquadEndpointTests.cs`
  - the `TableSquadRepository` test, if one exists (`grep -rln TableSquadRepository Ez.Handball.Tests`)

**Interfaces produced:**
- `public enum SetPlayAsOutcome { Updated, NotFound, Conflict }` (Application/Abstractions, next to `RosterAddOutcome`).
- `Task<SetPlayAsOutcome> IGameRosterRepository.SetPlayAsAsync(string teamId, string playerId, string? playAs, CancellationToken ct)`. Null clears.

**Steps:**
- [ ] **Step 1: failing tests.**
  - Roster repository (Azurite):
    - set then read → `RosterEntry.PlayAs == "LB"`, with `Position` and `PricePaidAmount` unchanged;
    - clear → null;
    - missing row → `NotFound`;
    - soft-deleted row → `NotFound`;
    - resurrect after set + soft delete → `PlayAs == null`.
  - `GetSquadUseCaseTests`:
    - a slot with `PlayAs="CB"` for a CB/LB player under caps CB 2, LB 1 → `SquadPlayer.PlayAs == "CB"` and `AssignedPosition == "CB"`;
    - pinned infeasible: two CB-only players plus a CB/LB pinned CB, caps CB 2, LB 1 → falls back to the #108 solve, so the pinned player gets `AssignedPosition == "LB"`.
  - `SquadEndpointTests`: the JSON has `playAs`.
- [ ] **Step 2:** run; expect failures.
- [ ] **Step 3: implement.**
  - Entity: `public string? PlayAs { get; set; } // manager's position pin (#166); null = auto-solve`.
  - Repository:
    - map `PlayAs` in both entry constructions;
    - in the resurrect branch set `existing.PlayAs = null`;
    - `SetPlayAsAsync`: `GetEntityAsync` (404 → `NotFound`), `DeletedAt != null` → `NotFound`, set `PlayAs`, `UpdateEntityAsync(e, e.ETag, Replace)` (412 → `Conflict`), return `Updated`.
  - `TableSquadRepository`: `new SquadSlot(e.PlayerId, e.Position, price, PlayAs: e.PlayAs)`.
  - `GetSquadUseCase`: pass `PlayAs: slot.PlayAs`. In `WithAssignedPositionsAsync`, solve with `p.EligiblePositions`; if null, solve again with `p.NaturalPositions`; if still null, use the primary (keep the existing ordering and comments, and extend the comment).
  - `SquadBody`: `playAs = p.ValidPlayAs`.
- [ ] **Step 4:** run `--filter "FullyQualifiedName~Roster|FullyQualifiedName~Squad"`; all pass.
- [ ] **Step 5:** commit `feat(squad): store and expose a manager's position pin`.

### Task 3: Snapshot persistence of pins

**Files:**
- Modify: `Ez.Handball.Shared/Entities/GameweekLineupEntity.cs`, `Ez.Handball.Infrastructure/TableAccess/TableGameweekLineupRepository.cs`
- Test: an Azurite test for `TableGameweekLineupRepository`. Use the existing one if present (`grep -rln TableGameweekLineupRepository Ez.Handball.Tests`); otherwise create `Ez.Handball.Tests/Infrastructure/Tables/TableGameweekLineupRepositoryTests.cs`, following `TableLineupRepositoryTests.cs` (collection, `IAsyncLifetime`, unique table cleanup).

- [ ] **Step 1: failing tests.**
  - Save a lineup with one slot `PlayAs="LB"` and one null → read back equal (`PlayAs` preserved, null stays null).
  - Write a raw legacy `GameweekLineupEntity` without `PlayAs` via a `TableClient` → `GetSnapshotAsync` returns the slot with `PlayAs == null`.
- [ ] **Step 2:** run; expect failure.
- [ ] **Step 3:** add `public string? PlayAs { get; set; } // position the slot was frozen at (#166); null = auto` to the entity. Write `PlayAs = s.PlayAs` on save, and read with `new LineupSlot(e.RowKey, role, e.BenchOrder, e.PlayAs)`.
- [ ] **Step 4:** run the tests; pass.
- [ ] **Step 5:** commit `feat(gameweek): freeze position pins in lineup snapshots`.

### Task 4: EffectiveLineupService used by guard and settlement

**Files:**
- Create: `Ez.Handball.Application/Services/EffectiveLineupService.cs`
- Modify:
  - `Ez.Handball.Application/Services/GameweekSnapshotGuard.cs` (`EffectiveLineupAsync`, constructor)
  - `Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs` (snapshot-if-missing block)
  - DI in `Ez.Handball.Api/Program.cs`, and anywhere else the guard or settle use case is composed (`grep -rn "new GameweekSnapshotGuard(\|new SettleGameweekUseCase(" --include=*.cs .`)
- Test: `Ez.Handball.Tests/Application/Services/EffectiveLineupServiceTests.cs` (new), plus updated `GameweekSnapshotGuardTests` and `SettleGameweekUseCaseTests` constructions

**Interface produced:**
```csharp
public interface IEffectiveLineupService
{
    // The lineup the team plays right now: the saved lineup, or the default one (#142), with each
    // slot stamped with the player's valid position pin (#166). Null when there is nothing to play.
    Task<Lineup?> GetAsync(string teamId, IReadOnlyList<SquadPlayer> squad, LineupConstraints constraints, CancellationToken ct);
}
```
The implementation depends only on `ILineupRepository`:
```csharp
var lineup = await _saved.GetAsync(teamId, ct) ?? DefaultLineup.From(squad, constraints);
if (lineup.Slots.Count == 0) return null;
var pins = squad.ToDictionary(p => p.PlayerId, p => p.ValidPlayAs, StringComparer.Ordinal);
return new Lineup(lineup.Slots
    .Select(s => s with { PlayAs = pins.TryGetValue(s.PlayerId, out var pin) ? pin : null })
    .ToList());
```

- [ ] **Step 1: failing `EffectiveLineupServiceTests`:**
  - no saved lineup + a pinned player → the default lineup, with that slot's `PlayAs` set;
  - saved lineup + pin → the saved roles kept, with `PlayAs` stamped;
  - stale pin → `PlayAs == null`;
  - empty squad and no saved lineup → null;
  - a saved slot for a player not in the squad → `PlayAs == null`.
- [ ] **Step 2:** run; expect failure.
- [ ] **Step 3:** implement and register as scoped.
  - Guard: inject `IEffectiveLineupService`. In `EffectiveLineupAsync`, keep the userId/constraints/squad lookups and replace the saved-or-default lines with `_effective.GetAsync(teamId, found.View.Players, constraints, ct)`.
    - The guard currently returns the saved lineup *before* loading constraints and squad. After the change it always loads them, because pins need the squad. That's acceptable (one extra squad read per mutation), but say so in the report.
    - Keep the `ILineupRepository` dependency only if something else in the guard still uses it; otherwise remove it.
  - Settlement: replace `var live = ...; var effective = live ?? DefaultLineup.From(...)` with `var effective = await _effective.GetAsync(teamId, found.View.Players, constraints, ct);`. A null result maps to `NoSnapshotPossible`, as an empty one did before.
  - Fix every construction site of both classes (tests included). Existing guard and settle tests must keep passing. Adjust their mocks so `ILineupRepository` goes through a real `EffectiveLineupService` (prefer the real service over mocking it, so the tests still exercise the saved-vs-default behaviour).
- [ ] **Step 4:** run `--filter "FullyQualifiedName~EffectiveLineup|FullyQualifiedName~SnapshotGuard|FullyQualifiedName~Settle"`; all pass.
- [ ] **Step 5:** commit `refactor(gameweek): share effective lineup with pins between guard and settlement`.

### Task 5: Scoring reads frozen pins only

**Files:** Modify `Ez.Handball.Application/Services/GameweekScoringService.cs` (line ~52, the `eligibleById` construction). Test: `Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs`.

- [ ] **Step 1: failing tests** (reuse the file's `Owned`/`Played` helpers, and build custom constraints as `LaterSub_CannotPullEarlierSubOffThePositionItFilled` does):
  - (a) **Frozen pin restricts a bench sub.** (LB needs min 1, so the pin matters.)
    - Constraints: GK(1,1), LB(1,1), CB(0,2); starter count 3.
    - Starters `gk1` (GK), `lb1` (LB, absent), `cb1` (CB). Bench: `d` (CB/LB) with snapshot slot `PlayAs = "CB"` at order 0, then `l` (LB) at order 1. All bench players played.
    - `d` pinned CB can't cover LB, so expect `l` subbed in and `d` not. Unpinned, `d` would have covered LB.
  - (b) **A live pin is ignored.** Same setup, but the snapshot slot has no `PlayAs` and the squad's `d` has a live `PlayAs = "CB"`. Expect `d` subbed in (natural eligibility).
  - (c) **A stale frozen pin is ignored.** Snapshot `PlayAs = "RW"` on `d` → treated as natural → `d` subbed in.
- [ ] **Step 2:** run; expect (a) to fail (and (b) too, if the live pin leaks through `EligiblePositions`).
- [ ] **Step 3:** replace the `eligibleById` line with:
```csharp
// Frozen pins only (#166): the snapshot slot's PlayAs, when still one of the player's natural
// positions. The live SquadPlayer pin is ignored so post-deadline moves can't touch locked rounds.
var frozenPins = snapshot.Slots.ToDictionary(s => s.PlayerId, s => s.PlayAs, StringComparer.Ordinal);
var eligibleById = ownedSquad.ToDictionary(p => p.PlayerId, p =>
{
    var natural = p.NaturalPositions;
    return frozenPins.TryGetValue(p.PlayerId, out var pin) && pin is not null && natural.Contains(pin, StringComparer.Ordinal)
        ? (IReadOnlyList<string>)new[] { pin }
        : natural;
});
```
  Use a `GroupBy`/first-wins dictionary instead if a duplicate `PlayerId` in the snapshot is possible; check how `Score` already handles duplicates, and match it.
- [ ] **Step 4:** run `--filter "FullyQualifiedName~GameweekScoringServiceTests|FullyQualifiedName~Settle"`; all pass.
- [ ] **Step 5:** commit `feat(scoring): honour frozen position pins in bench subs`.

### Task 6: SetPlayAsUseCase and endpoints

**Files:**
- Create: `Ez.Handball.Application/UseCases/SetPlayAsUseCase.cs`
- Modify: `Ez.Handball.Api/SquadEndpoints.cs`, `Ez.Handball.Api/Program.cs` (DI)
- Test: `Ez.Handball.Tests/Application/UseCases/SetPlayAsUseCaseTests.cs` (new), `Ez.Handball.Tests/Api/Endpoints/SquadMutationEndpointTests.cs` (or wherever buy/sell endpoint tests live)

**Interfaces produced:**
```csharp
public abstract record SetPlayAsResult
{
    public sealed record Updated(SquadView View) : SetPlayAsResult;                       // 200
    public sealed record NoTeam : SetPlayAsResult { public static readonly NoTeam Instance = new(); }             // 409 no_team
    public sealed record NotInSquad : SetPlayAsResult { public static readonly NotInSquad Instance = new(); }     // 404 not_in_squad
    public sealed record NotEligible : SetPlayAsResult { public static readonly NotEligible Instance = new(); }   // 422 position_not_eligible
    public sealed record Rejected(IReadOnlyList<LineupViolation> Violations) : SetPlayAsResult;                   // 422 lineup_invalid
    public sealed record Conflict : SetPlayAsResult { public static readonly Conflict Instance = new(); }         // 409 conflict
    public sealed record RuleSetNotFound : SetPlayAsResult { public static readonly RuleSetNotFound Instance = new(); } // 400 invalid_rule_set
}
public interface ISetPlayAsUseCase
{
    // position null = clear the pin.
    Task<SetPlayAsResult> ExecuteAsync(string userId, string playerId, string? position, BuyPlayerContext context, CancellationToken ct);
}
```
**Dependencies:**
- `IGameTeamRepository` (team existence, as sell checks it; mirror `SellPlayerUseCase`);
- `IGameweekSnapshotGuard`;
- `IGetSquadUseCase`;
- `ILineupRepository` (saved lineup);
- `ILineupConstraintsRepository` + `IGameweekConfigRepository` (the lineup constraints version, as the guard and settlement resolve it; mirror their lookup);
- `IGameRosterRepository`;
- `ILogger<SetPlayAsUseCase>`.

**Order (from the spec):** team → guard → squad (`RuleSetNotFound`) → player in squad → eligibility (set only) → saved-lineup validation (set only; reject only on `position_min`/`position_max`) → `SetPlayAsAsync` → re-read squad → log → `Updated`.

- [ ] **Step 1: failing use-case tests** (Moq):
  - set happy path (calls `SetPlayAsAsync(teamId, "p1", "LB")`; returns `Updated`);
  - clear (null passed through; no eligibility or lineup checks);
  - `NoTeam`; `NotInSquad`;
  - `NotEligible` (`"RW"` for a CB/LB player; and `" lb "` is accepted, normalised to `"LB"`);
  - saved lineup broken by the pin → `Rejected`, with `SetPlayAsAsync` never called;
  - a saved lineup missing its captain but otherwise fine → still `Updated`;
  - roster `Conflict` → `Conflict`;
  - call order: the guard's `EnsureSnapshotsAsync` before `SetPlayAsAsync` (use `MockSequence`, or record calls into a list).
- [ ] **Step 2:** run; expect failure.
- [ ] **Step 3:** implement the use case. Map routes in `SquadEndpoints` following the sell handler's shape (flavor check, user check, `GameweekEcho` on success):
  - `group.MapPut("/players/{playerId}/position", ...)` binds a body record `SetPlayerPositionBody(string? Position)`. A missing or blank position → 400 `{ error: "invalid_position" }`.
  - `group.MapDelete("/players/{playerId}/position", ...)`.
  - Result mapping: `Rejected` → `Results.Json(new { error = "lineup_invalid", violations = r.Violations.Select(v => new { code = v.Code, message = v.Message }) }, statusCode: 422)`; the other cases per the record comments.
  - Register `ISetPlayAsUseCase` next to `ISellPlayerUseCase`.
- [ ] **Step 4: endpoint tests** following the buy/sell endpoint test file's pattern:
  - PUT 200 with `squad` + `gameweek`;
  - PUT 422 `position_not_eligible`;
  - PUT 400 on a blank body;
  - DELETE 200;
  - 404 `not_in_squad`.

  Run `--filter "FullyQualifiedName~SetPlayAs|FullyQualifiedName~SquadMutation|FullyQualifiedName~SquadEndpoint"`; all pass.
- [ ] **Step 5:** commit `feat(squad): add endpoints to pin a player's position`.

### Task 7: Lineup view, docs, full verification

**Files:**
- Modify: `Ez.Handball.Application/UseCases/LineupView.cs` (`LineupPlayer` + mapper), `Ez.Handball.Api/LineupEndpoints.cs` (`LineupBody`), `CLAUDE.md`
- Test: `LineupViewMapperTests`, `LineupEndpointTests`

- [ ] **Step 1: failing test.** `LineupViewMapper` with an owned player `PlayAs="CB"` (CB/LB) → `LineupPlayer.PlayAs == "CB"` and `AssignedPosition == "CB"` (pins reach the solve through `EligiblePositions`). The endpoint JSON has `playAs` for starters and bench.
- [ ] **Step 2:** run; expect failure.
- [ ] **Step 3:**
  - Add the trailing `string? PlayAs = null` to `LineupPlayer`, set from `p?.ValidPlayAs`, and add `playAs = s.PlayAs` to both projections in `LineupBody`.
  - Add a CLAUDE.md bullet after "Secondary positions (Backend#108)":
```markdown
- **Position pins (Backend#166):** a manager can pin an owned dual-position player to their
  primary or secondary (`PUT`/`DELETE /api/users/me/squad/players/{playerId}/position`). The pin
  is `GameRosters.PlayAs` (null = auto-solve, cleared on re-buy); `SquadPlayer.EligiblePositions`
  honours it, so the default lineup, lineup validation and views do too. Buying ignores pins. The
  snapshot guard runs before a pin change and `IEffectiveLineupService` stamps pins onto the lineup
  it freezes (`GameweekLineups.PlayAs`); scoring reads pins only from the snapshot, so a later move
  never changes a locked round. A pin that's no longer one of the player's positions is ignored.
```
- [ ] **Step 4: full verification.** `dotnet build Ez.Handball.sln` has no new warnings. Then run the full `dotnet test` (after the Azurite check in Global Constraints) and report the totals and any failing test names verbatim.
- [ ] **Step 5:** commit `feat(lineup): expose position pins on lineup view and document #166`.
