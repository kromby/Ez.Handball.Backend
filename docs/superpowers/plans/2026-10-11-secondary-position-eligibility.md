# Secondary Position Eligibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a player with a `PositionSecondary` be bought and started under either position, by auto-solving a valid player→position assignment instead of counting primaries.

**Architecture:** One pure domain helper, `PositionAssignment.TrySolve`, decides whether players with 1–2 eligible positions can each be given one position within per-position `(min, max)` limits. The buy rule, `LineupValidator`, `DefaultLineup`, bench-sub validity in `GameweekScoringService`, and the squad and lineup views all call it. Eligibility is read live from the `Player` row.

**Tech Stack:** .NET 8, C#, xUnit, Moq.

**Spec:** `docs/superpowers/specs/2026-10-11-secondary-position-eligibility-design.md`

## Global Constraints

- Work in worktree `/home/kromby/source/Handbolti/wt-108-secondary-position`, branch `feat/108-secondary-position-eligibility`.
- Build: `dotnet build Ez.Handball.sln`. Tests: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~<Class>"`. The full suite needs Azurite (`azurite --silent --location /tmp/azurite-test &`); for the per-task gate, run the filtered classes, and run the full suite in the final task.
- Commits: Conventional Commits (`feat(scope): subject`, imperative, lowercase, <70 chars). **No `Co-Authored-By` or any trailer lines. Do not push.**
- Position codes are compared `StringComparer.Ordinal` (matching the existing rule code). Only the pool filter is case-insensitive, as it already is.
- All new record parameters are **optional trailing parameters** (`= null`) so existing constructions compile unchanged.
- Error codes stay unchanged: `position_limit`, `position_min`, `position_max`.
- Positions absent from a limits dictionary have no min and no max. Players with no known position (null/whitespace) are unconstrained.
- Comment density and style should match the surrounding code: short *why* comments, no restating the code.

## Review Focus

1. **Squad already over a cap** (a live secondary change after purchase): an unrelated buy must still be allowed. The rule falls back to the primary-only count, tested in Task 2.
2. **`PositionSecondary` empty, whitespace, or equal to the primary** → treated as single-position, never double-counted. Tested in Task 1 (`EligibleFor`).
3. **Squad slot whose player no longer resolves** (`GetByIdAsync` → null): buy eligibility falls back to the slot's snapshot `Position`, with no crash. Tested in Task 2.
4. **Starter not in the owned squad** (stale lineup): skipped by the position check exactly as today, with no crash. Tested in Task 4.
5. **Determinism:** `DefaultLineup` and `assignedPosition` give identical output for identical input, and a dual player uses their secondary only when needed. Tested in Task 1 (primary preferred) and Task 4 (DefaultLineup).

---

### Task 1: `PositionAssignment` solver

**Files:**
- Create: `Ez.Handball.Domain/PositionAssignment.cs`
- Test: `Ez.Handball.Tests/Domain/PositionAssignmentTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PositionCandidate(string PlayerId, IReadOnlyList<string> Eligible);` (`Eligible[0]` is the primary; an empty list means unconstrained)
  - `public static IReadOnlyList<string> PositionAssignment.EligibleFor(string? position, string? positionSecondary)`
  - `public static IReadOnlyDictionary<string, string?>? PositionAssignment.TrySolve(IReadOnlyList<PositionCandidate> players, IReadOnlyDictionary<string, (int Min, int Max)> limits)`. Returns playerId → position (null for unconstrained players), or `null` when infeasible.
  - `public static IReadOnlyDictionary<string, (int Min, int Max)> PositionAssignment.MaxOnly(IReadOnlyDictionary<string, int> maxByPosition)` and an overload `MaxOnly(IReadOnlyDictionary<string, (int Min, int Max)> limits)`. Both return the same keys with `Min = 0`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class PositionAssignmentTests
{
    private static PositionCandidate P(string id, params string[] eligible) => new(id, eligible);

    private static Dictionary<string, (int Min, int Max)> Limits(params (string Pos, int Min, int Max)[] l)
        => l.ToDictionary(x => x.Pos, x => (x.Min, x.Max));

    [Theory]
    [InlineData("CB", "LB", new[] { "CB", "LB" })]
    [InlineData("CB", "", new[] { "CB" })]
    [InlineData("CB", "  ", new[] { "CB" })]
    [InlineData("CB", null, new[] { "CB" })]
    [InlineData("CB", "CB", new[] { "CB" })]
    [InlineData(null, "LB", new string[0])]
    [InlineData("", "LB", new string[0])]
    public void EligibleFor_PrimaryFirst_DropsEmptyOrDuplicateSecondary(string? pos, string? sec, string[] expected)
        => Assert.Equal(expected, PositionAssignment.EligibleFor(pos, sec));

    [Fact]
    public void SinglePositionPlayers_WithinMax_Solves()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "CB") }, Limits(("CB", 0, 2)));

        Assert.NotNull(result);
        Assert.Equal("CB", result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void SinglePositionPlayers_OverMax_IsInfeasible()
        => Assert.Null(PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "CB"), P("c", "CB") }, Limits(("CB", 0, 2))));

    [Fact]
    public void DualPlayer_FillsSecondary_WhenPrimaryFull()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("b", "CB"), P("a", "CB", "LB") }, Limits(("CB", 0, 1), ("LB", 0, 1)));

        Assert.Equal("CB", result!["b"]);
        Assert.Equal("LB", result["a"]);
    }

    [Fact]
    public void GreedyTrap_DualPlayerFirst_StillSolvesByBacktracking()
    {
        // A tries CB first (primary), which would strand B; backtracking moves A to LB.
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "LB"), P("b", "CB") }, Limits(("CB", 0, 1), ("LB", 0, 1)));

        Assert.Equal("LB", result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void PrimaryPreferred_WhenBothFit()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "LB") }, Limits(("CB", 0, 2), ("LB", 0, 2)));

        Assert.Equal("CB", result!["a"]);
    }

    [Fact]
    public void Min_MetBySecondary()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "CB", "GK"), P("b", "LW") }, Limits(("GK", 1, 1), ("CB", 0, 2), ("LW", 0, 2)));

        Assert.Equal("GK", result!["a"]);
    }

    [Fact]
    public void Min_Unmet_IsInfeasible()
        => Assert.Null(PositionAssignment.TrySolve(
            new[] { P("a", "CB"), P("b", "LW") }, Limits(("GK", 1, 1), ("CB", 0, 2), ("LW", 0, 2))));

    [Fact]
    public void UnknownPosition_IsUnconstrained_AssignedNull()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a"), P("b", "CB") }, Limits(("CB", 0, 1)));

        Assert.Null(result!["a"]);
        Assert.Equal("CB", result["b"]);
    }

    [Fact]
    public void PositionWithoutLimit_IsUnconstrained()
    {
        var result = PositionAssignment.TrySolve(
            new[] { P("a", "FP"), P("b", "FP"), P("c", "FP") }, Limits(("CB", 0, 1)));

        Assert.Equal("FP", result!["c"]);
    }

    [Fact]
    public void EmptyPlayers_WithZeroMins_Solves()
        => Assert.NotNull(PositionAssignment.TrySolve(Array.Empty<PositionCandidate>(), Limits(("CB", 0, 1))));

    [Fact]
    public void FifteenDualPlayers_Infeasible_ReturnsQuickly()
    {
        var players = Enumerable.Range(0, 15).Select(i => P($"p{i}", "CB", "LB")).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = PositionAssignment.TrySolve(players, Limits(("CB", 0, 3), ("LB", 0, 3)));

        Assert.Null(result);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void SameInput_SameAssignment()
    {
        var players = new[] { P("a", "CB", "LB"), P("b", "CB", "RB"), P("c", "CB") };
        var limits = Limits(("CB", 0, 1), ("LB", 0, 1), ("RB", 0, 1));

        Assert.Equal(PositionAssignment.TrySolve(players, limits), PositionAssignment.TrySolve(players, limits));
    }

    [Fact]
    public void MaxOnly_ZeroesMins()
    {
        var max = PositionAssignment.MaxOnly(Limits(("GK", 1, 1), ("CB", 0, 2)));
        Assert.Equal((0, 1), max["GK"]);
        Assert.Equal((0, 2), max["CB"]);

        var fromCaps = PositionAssignment.MaxOnly(new Dictionary<string, int> { ["LB"] = 3 });
        Assert.Equal((0, 3), fromCaps["LB"]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PositionAssignmentTests"`
Expected: build FAIL — `PositionAssignment` / `PositionCandidate` not defined.

- [ ] **Step 3: Implement**

```csharp
namespace Ez.Handball.Domain;

// A player and the positions they may be counted under; Eligible[0] is the primary. An empty
// list means the position is unknown and the player never blocks a limit.
public sealed record PositionCandidate(string PlayerId, IReadOnlyList<string> Eligible);

// Backend#108: a player may play their primary or secondary position, but counts against only
// one. Instead of counting primaries, rules ask whether *some* assignment of players to eligible
// positions meets every (min, max). Squads are ≤15 players with ≤2 options each, so plain
// backtracking (primary first) is instant and yields a deterministic, primary-preferring answer.
public static class PositionAssignment
{
    public static IReadOnlyList<string> EligibleFor(string? position, string? positionSecondary)
    {
        if (string.IsNullOrWhiteSpace(position)) return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(positionSecondary)
            || string.Equals(position, positionSecondary, StringComparison.Ordinal))
            return new[] { position };
        return new[] { position, positionSecondary };
    }

    public static IReadOnlyDictionary<string, (int Min, int Max)> MaxOnly(IReadOnlyDictionary<string, int> maxByPosition)
        => maxByPosition.ToDictionary(kv => kv.Key, kv => (0, kv.Value), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, (int Min, int Max)> MaxOnly(IReadOnlyDictionary<string, (int Min, int Max)> limits)
        => limits.ToDictionary(kv => kv.Key, kv => (0, kv.Value.Max), StringComparer.Ordinal);

    // playerId → assigned position (null for unconstrained players), or null when no assignment
    // meets every limit. Positions absent from `limits` have no min and no max.
    public static IReadOnlyDictionary<string, string?>? TrySolve(
        IReadOnlyList<PositionCandidate> players,
        IReadOnlyDictionary<string, (int Min, int Max)> limits)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var assigned = new string?[players.Count];
        if (!Assign(0)) return null;

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < players.Count; i++) result[players[i].PlayerId] = assigned[i];
        return result;

        bool Assign(int i)
        {
            // Prune: the players left can't cover what the mins still need.
            if (MinDeficit() > players.Count - i) return false;
            if (i == players.Count) return true;

            var eligible = players[i].Eligible;
            if (eligible.Count == 0)
            {
                assigned[i] = null;
                return Assign(i + 1);
            }

            foreach (var position in eligible)
            {
                var count = counts.GetValueOrDefault(position);
                if (limits.TryGetValue(position, out var limit) && count >= limit.Max) continue;

                counts[position] = count + 1;
                assigned[i] = position;
                if (Assign(i + 1)) return true;
                counts[position] = count;
            }
            return false;
        }

        int MinDeficit()
            => limits.Sum(kv => Math.Max(0, kv.Value.Min - counts.GetValueOrDefault(kv.Key)));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PositionAssignmentTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/PositionAssignment.cs Ez.Handball.Tests/Domain/PositionAssignmentTests.cs
git commit -m "feat(domain): add position assignment solver for dual-position players"
```

---

### Task 2: Buy rule uses live dual eligibility

**Files:**
- Modify: `Ez.Handball.Domain/Squad.cs` (`SquadSlot`)
- Modify: `Ez.Handball.Application/BuyFunctions/IBuyPlayerFunction.cs` (`BuyPlayerInputs`)
- Modify: `Ez.Handball.Application/BuyFunctions/FantasyBuyPlayerFunction.cs`
- Modify: `Ez.Handball.Application/UseCases/GetBuyDecisionUseCase.cs`
- Test: `Ez.Handball.Tests/Application/BuyFunctions/FantasyBuyPlayerFunctionTests.cs`, `Ez.Handball.Tests/Application/UseCases/GetBuyDecisionUseCaseTests.cs`

**Interfaces:**
- Consumes: `PositionAssignment.EligibleFor`, `TrySolve`, `MaxOnly(IReadOnlyDictionary<string,int>)`, `PositionCandidate` (Task 1).
- Produces:
  - `SquadSlot(string PlayerId, string? Position, PlayerPrice PricePaid, IReadOnlyList<string>? Eligible = null)`. When `Eligible` is null, eligibility is `EligibleFor(Position, null)`.
  - `BuyPlayerInputs(..., BuyPlayerContext Context, IReadOnlyList<string>? Eligible = null)`. When `Eligible` is null, eligibility is `EligibleFor(Position, null)`.

- [ ] **Step 1: Write the failing tests.** Append to `FantasyBuyPlayerFunctionTests`:

```csharp
    private static SquadSlot Dual(string id, string pos, string sec) =>
        new(id, pos, new PlayerPrice(10_000_000, "ISK"), new[] { pos, sec });

    private static BuyPlayerInputs DualInputs(Squad squad, SquadConstraints constraints, string pos, string? sec) =>
        new("p1", pos, Cost, "fantasy-price-v1", constraints, squad, new BuyPlayerContext(null, null, null),
            PositionAssignment.EligibleFor(pos, sec));

    [Fact]
    public void PrimaryFull_CandidateSecondaryHasRoom_Allowed()
    {
        var limits = new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 };
        var result = _sut.Evaluate(DualInputs(Squad(100_000_000, Slot("x", "CB")), Constraints(posLimits: limits), "CB", "LB"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void PrimaryFull_ExistingDualPlayerCanMove_Allowed()
    {
        var limits = new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 };
        var result = _sut.Evaluate(Inputs(Squad(100_000_000, Dual("x", "CB", "LB")), Constraints(posLimits: limits), "CB"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void PrimaryFull_NobodyCanMove_PositionLimit()
    {
        var limits = new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 };
        var result = _sut.Evaluate(DualInputs(
            Squad(100_000_000, Slot("x", "CB"), Slot("y", "LB")), Constraints(posLimits: limits), "CB", "LB"));

        Assert.False(result.Allowed);
        var v = Assert.Single(result.Violations, v => v.Code == "position_limit");
        Assert.Contains("CB", v.Message);
    }

    [Fact]
    public void SquadAlreadyOverCap_UnrelatedBuyStillAllowed()
    {
        // Two CBs owned under a CB cap of 1 (a live position change after purchase) — the squad
        // alone has no valid assignment, so the rule falls back to the primary-only count.
        var limits = new Dictionary<string, int> { ["CB"] = 1, ["LW"] = 2 };
        var result = _sut.Evaluate(Inputs(
            Squad(100_000_000, Slot("x", "CB"), Slot("y", "CB")), Constraints(posLimits: limits), "LW"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public void SquadAlreadyOverCap_SamePrimaryBuyBlocked()
    {
        var limits = new Dictionary<string, int> { ["CB"] = 1 };
        var result = _sut.Evaluate(Inputs(
            Squad(100_000_000, Slot("x", "CB"), Slot("y", "CB")), Constraints(posLimits: limits), "CB"));

        Assert.Contains(result.Violations, v => v.Code == "position_limit");
    }
```

Append to `GetBuyDecisionUseCaseTests`:

```csharp
    [Fact]
    public async Task Fantasy_UsesLivePlayerEligibilityForSquadSlots()
    {
        // The owned "x" was bought as CB but now has LB as a live secondary, so buying a CB fits.
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 }, 100_000_000, "ISK"));
        _squad.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Squad(new[] { new SquadSlot("x", "CB", new PlayerPrice(1, "ISK")) }, 100_000_000, "ISK"));
        _players.Setup(r => r.GetByIdAsync("x", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Player("x", "X", null, null, null, "team", "club", "Club", "karlar", "CB", false, "LB"));
        _players.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Player("p1", "P", null, null, null, "team", "club", "Club", "karlar", "CB", false));

        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        Assert.True(Assert.IsType<BuyDecisionResult.Decided>(result).Decision.Allowed);
    }

    [Fact]
    public async Task Fantasy_UnresolvableSquadPlayer_FallsBackToSnapshotPosition()
    {
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int> { ["CB"] = 1 }, 100_000_000, "ISK"));
        _squad.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Squad(new[] { new SquadSlot("gone", "CB", new PlayerPrice(1, "ISK")) }, 100_000_000, "ISK"));
        _players.Setup(r => r.GetByIdAsync("gone", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);
        _players.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Player("p1", "P", null, null, null, "team", "club", "Club", "karlar", "CB", false));

        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        var decision = Assert.IsType<BuyDecisionResult.Decided>(result).Decision;
        Assert.Contains(decision.Violations, v => v.Code == "position_limit");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyBuyPlayerFunctionTests|FullyQualifiedName~GetBuyDecisionUseCaseTests"`
Expected: build FAIL (`SquadSlot`/`BuyPlayerInputs` have no `Eligible` parameter).

- [ ] **Step 3: Implement.**

`Ez.Handball.Domain/Squad.cs`:

```csharp
public sealed record SquadSlot(
    string PlayerId,
    string? Position,
    PlayerPrice PricePaid,                           // price locked at buy time, NOT the player's current market price
    IReadOnlyList<string>? Eligible = null)          // live eligible positions (#108); null → just the snapshot Position
{
    public IReadOnlyList<string> EligiblePositions => Eligible ?? PositionAssignment.EligibleFor(Position, null);
}
```

`BuyPlayerInputs`: add a trailing parameter `IReadOnlyList<string>? Eligible = null` with the comment `// candidate's live eligible positions (#108); null → just Position`.

`FantasyBuyPlayerFunction`: replace the `position_limit` block with:

```csharp
        if (ExceedsPositionLimits(inputs))
            violations.Add(new("position_limit", PositionLimitMessage(inputs)));
```

and add:

```csharp
    // #108: a buy fits if some assignment of every player (squad + candidate) to one of their
    // eligible positions stays within the caps. A squad already over a cap (positions changed
    // after purchase) can't be solved at all, so it falls back to the old primary-only count —
    // caps only ever gate new buys.
    private static bool ExceedsPositionLimits(BuyPlayerInputs inputs)
    {
        var limits = inputs.Constraints.PositionLimits;
        if (limits.Count == 0) return false;

        var maxOnly = PositionAssignment.MaxOnly(limits);
        var squad = inputs.Squad.Players
            .Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions))
            .ToList();

        if (PositionAssignment.TrySolve(squad, maxOnly) is null)
            return inputs.Position is { } position
                && limits.TryGetValue(position, out var limit)
                && inputs.Squad.Players.Count(p => p.Position == position) >= limit;

        var candidate = new PositionCandidate(
            inputs.PlayerId, inputs.Eligible ?? PositionAssignment.EligibleFor(inputs.Position, null));
        return PositionAssignment.TrySolve(squad.Append(candidate).ToList(), maxOnly) is null;
    }

    private static string PositionLimitMessage(BuyPlayerInputs inputs)
        => inputs.Position is { } position && inputs.Constraints.PositionLimits.TryGetValue(position, out var limit)
            ? $"Squad already has the maximum of {limit} players in position {position}"
            : "Squad position limits cannot accommodate this player";
```

`GetBuyDecisionUseCase` (fantasy path): after `var squad = await _squad.GetAsync(userId, flavor, ct);` add

```csharp
        squad = await WithLiveEligibilityAsync(squad, ct);
```

pass `PositionAssignment.EligibleFor(player.Position, player.PositionSecondary)` as the new trailing `Eligible` argument of the fantasy `BuyPlayerInputs`, and add:

```csharp
    // #108: eligibility is live — a player's secondary can change after purchase. Falls back to
    // the buy-time snapshot when the player row no longer resolves.
    private async Task<Squad> WithLiveEligibilityAsync(Squad squad, CancellationToken ct)
    {
        var slots = new List<SquadSlot>(squad.Players.Count);
        foreach (var slot in squad.Players)
        {
            var live = await _players.GetByIdAsync(slot.PlayerId, ct);
            slots.Add(live is null
                ? slot
                : slot with { Eligible = PositionAssignment.EligibleFor(live.Position, live.PositionSecondary) });
        }
        return squad with { Players = slots };
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyBuyPlayerFunctionTests|FullyQualifiedName~GetBuyDecisionUseCaseTests|FullyQualifiedName~BuyPlayerUseCase"`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/Squad.cs Ez.Handball.Application/BuyFunctions Ez.Handball.Application/UseCases/GetBuyDecisionUseCase.cs Ez.Handball.Tests/Application
git commit -m "feat(buy): allow buying under a secondary position via assignment solve"
```

---

### Task 3: Squad view carries `PositionSecondary` and `AssignedPosition`

**Files:**
- Modify: `Ez.Handball.Domain/SquadView.cs` (`SquadPlayer`)
- Modify: `Ez.Handball.Application/UseCases/GetSquadUseCase.cs`
- Modify: `Ez.Handball.Api/SquadEndpoints.cs` (`SquadBody`)
- Test: `Ez.Handball.Tests/Application/UseCases/GetSquadUseCaseTests.cs`, `Ez.Handball.Tests/Api/Endpoints/SquadEndpointTests.cs`; update every other `new GetSquadUseCase(` call site (`grep -rn "new GetSquadUseCase(" --include=*.cs .`)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `SquadPlayer(..., double Rating, string? PositionSecondary = null, string? AssignedPosition = null)` with a computed property `public IReadOnlyList<string> EligiblePositions => PositionAssignment.EligibleFor(Position, PositionSecondary);` (a computed getter, so it doesn't take part in record equality).
  - `GetSquadUseCase(ISquadRepository squad, IPlayerRepository players, IPlayerPriceService price, ISquadConstraintsRepository constraints)`.

- [ ] **Step 1: Write the failing tests.** In `GetSquadUseCaseTests`, add `private readonly Mock<ISquadConstraintsRepository> _constraints = new();` and change `CreateSut()` to `new(_squad.Object, _players.Object, _price.Object, _constraints.Object)`. Leave the mock un-setup by default (Moq returns null → no constraints). Add:

```csharp
    [Fact]
    public async Task Found_AssignsDualPlayerToSecondary_WhenPrimaryCapped()
    {
        SetupSquad(0,
            new SquadSlot("p-1", "CB", new PlayerPrice(1, "ISK")),
            new SquadSlot("p-2", "CB", new PlayerPrice(1, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "CB"));
        _players.Setup(r => r.GetByIdAsync("p-2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("p-2", "CB") with { PositionSecondary = "LB" });
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 2) with { Rating = 9 });
        _price.Setup(s => s.GetPriceAsync("p-2", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-2", 1) with { Rating = 1 });
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 }, 100_000_000, "ISK"));

        var view = Assert.IsType<GetSquadResult.Found>(
            await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None)).View;

        var p2 = view.Players.Single(p => p.PlayerId == "p-2");
        Assert.Equal("LB", p2.PositionSecondary);
        Assert.Equal("LB", p2.AssignedPosition);
        Assert.Equal("CB", view.Players.Single(p => p.PlayerId == "p-1").AssignedPosition);
    }

    [Fact]
    public async Task Found_NoConstraints_AssignedPositionIsPrimary()
    {
        SetupSquad(0, new SquadSlot("p-1", "CB", new PlayerPrice(1, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("p-1", "CB") with { PositionSecondary = "LB" });
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 1));

        var view = Assert.IsType<GetSquadResult.Found>(
            await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None)).View;

        Assert.Equal("CB", view.Players.Single().AssignedPosition);
    }
```

(If `PlayerPricing` is not a record supporting `with { Rating = ... }`, construct it positionally as `new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1", rating)`. The last positional argument is the rating, per `PriceOf`.)

In `SquadEndpointTests`, extend an existing squad-read test's assertions (or add one following that file's pattern) so the JSON for a player contains `positionSecondary` and `assignedPosition`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetSquadUseCaseTests|FullyQualifiedName~SquadEndpointTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement.**

`SquadPlayer`: append `string? PositionSecondary = null, string? AssignedPosition = null` after `Rating` (move the `// current-season fantasy rating` comment onto the `Rating` line), and add the record body:

```csharp
{
    public IReadOnlyList<string> EligiblePositions => PositionAssignment.EligibleFor(Position, PositionSecondary);
}
```

`GetSquadUseCase`: inject `ISquadConstraintsRepository constraints` (field `_constraints`). Set `PositionSecondary: player?.PositionSecondary` when building each `SquadPlayer`. Before building the `SquadView`, add `items = await WithAssignedPositionsAsync(items, ct);` (make `items` a `List<SquadPlayer>` variable you can reassign, or use a new local) and add:

```csharp
    private const int ConstraintsVersion = 1;   // fantasy-squad-v1, as GetBuyDecisionUseCase

    // #108: the slot each player fills under the squad caps, preferring primaries; highest-rated
    // players are placed first so the result is stable. Primary when there are no caps or the squad
    // can't be arranged (over a cap after a live position change).
    private async Task<List<SquadPlayer>> WithAssignedPositionsAsync(List<SquadPlayer> items, CancellationToken ct)
    {
        var constraints = await _constraints.GetAsync(ConstraintsVersion, ct);
        var ordered = items.OrderByDescending(p => p.Rating).ThenBy(p => p.PlayerId, StringComparer.Ordinal).ToList();
        var assignment = constraints is null
            ? null
            : PositionAssignment.TrySolve(
                ordered.Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions)).ToList(),
                PositionAssignment.MaxOnly(constraints.PositionLimits));

        return items.Select(p => p with
        {
            AssignedPosition = assignment is not null && assignment.TryGetValue(p.PlayerId, out var pos) ? pos : p.Position
        }).ToList();
    }
```

Check `ISquadConstraintsRepository.GetAsync`'s signature and what version `GetBuyDecisionUseCase` passes (`DefaultVersion`), and use the same value. If its version parameter has a different type, adapt the constant.

`SquadEndpoints.SquadBody`: add `positionSecondary = p.PositionSecondary,` and `assignedPosition = p.AssignedPosition,` after `position = p.Position,`.

Fix all other `new GetSquadUseCase(` call sites (tests or composition) to pass a constraints repository: a `Mock<ISquadConstraintsRepository>().Object` in tests. DI resolves it automatically if `ISquadConstraintsRepository` is already registered in `Ez.Handball.Api/Program.cs`; verify with `grep -n "ISquadConstraintsRepository" Ez.Handball.Api/Program.cs`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~Squad|FullyQualifiedName~Manager"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A Ez.Handball.Domain Ez.Handball.Application Ez.Handball.Api Ez.Handball.Tests
git commit -m "feat(squad): expose secondary and assigned position on squad view"
```

---

### Task 4: Lineup validation and default lineup use the solver

**Files:**
- Modify: `Ez.Handball.Domain/LineupValidator.cs` (`CheckPositions`)
- Modify: `Ez.Handball.Domain/DefaultLineup.cs`
- Test: `Ez.Handball.Tests/Domain/LineupValidatorTests.cs`, `Ez.Handball.Tests/Domain/DefaultLineupTests.cs`

**Interfaces:**
- Consumes: Task 1, plus `SquadPlayer.PositionSecondary` / `EligiblePositions` (Task 3).
- Produces: no new public API.

- [ ] **Step 1: Write the failing tests.** In `LineupValidatorTests`, add a helper and tests. Read the file first, and reuse its existing captain/bench slot conventions: look at how the valid-lineup test builds its `Lineup` and mirror it.

```csharp
    private static SquadPlayer Dual(string id, string pos, string sec) => new(
        PlayerId: id, Name: id, ClubId: "385", ClubName: "Stjarnan", Position: pos, Gender: "karlar",
        Price: new PlayerPrice(10_000_000, "ISK"), PricePaid: new PlayerPrice(10_000_000, "ISK"),
        Rating: 0, PositionSecondary: sec);

    private static Lineup StartAll(IReadOnlyList<SquadPlayer> squad) => new(
        squad.Select((p, i) => new LineupSlot(p.PlayerId, i == 0 ? LineupRole.Captain : LineupRole.Starter, null)).ToList());

    [Fact]
    public void NoNaturalKeeper_StarterWithGkSecondary_IsValid()
    {
        var squad = Owned("LW", "RW", "LB", "CB", "RB", "LP").Append(Dual("d", "CB", "GK")).ToList();

        var result = LineupValidator.Validate(StartAll(squad), squad, Constraints);

        Assert.DoesNotContain(result.Violations, v => v.Code is "position_min" or "position_max");
    }

    [Fact]
    public void ThreeCbStarters_OneWithLbSecondary_IsValid()
    {
        var squad = Owned("GK", "LW", "RW", "LP", "CB", "CB").Append(Dual("d", "CB", "LB")).ToList();

        var result = LineupValidator.Validate(StartAll(squad), squad, Constraints);

        Assert.DoesNotContain(result.Violations, v => v.Code is "position_min" or "position_max");
    }

    [Fact]
    public void NoKeeperAnywhere_StillReportsPositionMin()
    {
        var squad = Owned("LW", "RW", "LB", "CB", "RB", "LP", "LP");

        var result = LineupValidator.Validate(StartAll(squad), squad, Constraints);

        Assert.Contains(result.Violations, v => v.Code == "position_min" && v.Message.Contains("GK"));
    }

    [Fact]
    public void UnownedStarter_IsSkippedByPositionCheck()
    {
        var squad = Owned("GK", "LW", "RW", "LB", "CB", "RB");
        var lineup = new Lineup(StartAll(squad).Slots.Append(new LineupSlot("ghost", LineupRole.Starter, null)).ToList());

        var result = LineupValidator.Validate(lineup, squad, Constraints);

        Assert.Contains(result.Violations, v => v.Code == "unowned_player");
        Assert.DoesNotContain(result.Violations, v => v.Code is "position_min" or "position_max");
    }
```

In `DefaultLineupTests`, add a dual helper `private static SquadPlayer DualPlayer(string id, string pos, string sec, double rating) => Player(id, pos, rating) with { PositionSecondary = sec };` and:

```csharp
    [Fact]
    public void DualPlayer_StartsAtSecondary_WhenPrimaryAtMax()
    {
        var squad = new[]
        {
            Player("gk", "GK", 9), Player("cb1", "CB", 8), Player("cb2", "CB", 7),
            DualPlayer("cb3", "CB", "LB", 6),                       // CB max 2 → starts at LB
            Player("lw", "LW", 5), Player("rw", "RW", 4), Player("lp", "LP", 3), Player("rb", "RB", 2),
        };

        var lineup = DefaultLineup.From(squad, Constraints);

        Assert.Contains("cb3", Ids(lineup, LineupRole.Starter));
        Assert.Equal(new[] { "rb" }, Ids(lineup, LineupRole.Bench));
    }

    [Fact]
    public void DualPlayers_SameInput_SameLineup()
    {
        var squad = new[]
        {
            Player("gk", "GK", 9), DualPlayer("a", "CB", "LB", 8), DualPlayer("b", "CB", "RB", 8),
            Player("c", "CB", 8), Player("lw", "LW", 1),
        };

        Assert.Equal(DefaultLineup.From(squad, Constraints), DefaultLineup.From(squad, Constraints));
    }
```

(If `Lineup` record equality compares the list by reference, compare `lineup.Slots` with `Assert.Equal(first.Slots, second.Slots)` instead.)

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupValidatorTests|FullyQualifiedName~DefaultLineupTests"`
Expected: `NoNaturalKeeper_…`, `ThreeCbStarters_…` and `DualPlayer_StartsAtSecondary_…` FAIL. The others may already pass.

- [ ] **Step 3: Implement.**

`LineupValidator.CheckPositions`: replace the body with:

```csharp
        // #108: starters are legal if some assignment to primary/secondary positions meets every
        // min/max. Only when none exists do we report — via the primary-only count, so managers
        // see the familiar per-position messages.
        var candidates = starters
            .Where(s => ownedById.ContainsKey(s.PlayerId))
            .Select(s => new PositionCandidate(s.PlayerId, ownedById[s.PlayerId].EligiblePositions))
            .ToList();
        if (PositionAssignment.TrySolve(candidates, constraints.PositionStart) is not null) return;

        var before = violations.Count;
        // (existing primary-only GroupBy + foreach min/max loop, unchanged)
        if (violations.Count == before)
            violations.Add(new("position_min", "Starters cannot be arranged to meet the position rules."));
```

Keep the existing `startersByPosition` count and loop verbatim where the comment indicates, and update the method's header comment to mention the solve.

`DefaultLineup.From`: compute `var maxOnly = PositionAssignment.MaxOnly(constraints.PositionStart);` and keep `var starterCandidates = new List<PositionCandidate>();`. Replace the `HasRoom` condition and the `startersByPosition` bookkeeping with:

```csharp
            var candidate = new PositionCandidate(player.PlayerId, player.EligiblePositions);
            if (starters.Count < constraints.StarterCount
                && PositionAssignment.TrySolve([.. starterCandidates, candidate], maxOnly) is not null)
            {
                starters.Add(new LineupSlot(player.PlayerId, LineupRole.Starter, null));
                starterCandidates.Add(candidate);
            }
```

Delete `HasRoom` and the `startersByPosition` dictionary. Update the class comment: "as long as their position still has room" → "as long as the starters can still be arranged within each position's max (primary or secondary, #108)".

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupValidatorTests|FullyQualifiedName~DefaultLineupTests|FullyQualifiedName~Lineup"`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/LineupValidator.cs Ez.Handball.Domain/DefaultLineup.cs Ez.Handball.Tests/Domain
git commit -m "feat(lineup): let starters fill a secondary position in validation and default lineup"
```

---

### Task 5: Bench auto-subs use the solver

**Files:**
- Modify: `Ez.Handball.Application/Services/GameweekScoringService.cs` (`posById`, `FindValidSub`, `KeepsPositionsValid`)
- Test: `Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs`

**Interfaces:**
- Consumes: Task 1, `SquadPlayer.EligiblePositions` (Task 3).

- [ ] **Step 1: Write the failing test.** Append to `GameweekScoringServiceTests`:

```csharp
    [Fact]
    public void AbsentKeeper_SubbedByBenchPlayerWithGkSecondary()
    {
        var squad = new[]
        {
            Owned("gk1", "GK"), Owned("fp1", "FP"),
            Owned("fp2", "FP") with { PositionSecondary = "GK" },
        };
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["fp1"] = Played(0),
            ["fp2"] = Played(1),
        };

        var score = CreateSut().Score("team", "1", Snapshot(), squad, stats, Rules, Constraints);

        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp2").AutoSubbedIn);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests"`
Expected: the new test FAILS. All existing tests pass.

- [ ] **Step 3: Implement.** Replace `posById` with

```csharp
        var eligibleById = ownedSquad.ToDictionary(p => p.PlayerId, p => p.EligiblePositions);
```

and thread it through `FindValidSub` (parameter type `IReadOnlyDictionary<string, IReadOnlyList<string>>`). Replace `KeepsPositionsValid`'s body, and its long header comment, with:

```csharp
    // After the swap, the decided-effective players plus the candidate must still be arrangeable
    // (primary or secondary, #108) with no position over its max, and every position the outgoing
    // starter could have covered must still reach its min. Mins elsewhere aren't enforced: future
    // starters aren't decided yet, so this stays as conservative as the single-position rule was.
    private static bool KeepsPositionsValid(
        string nonPlayingStarterId,
        string candidateId,
        IReadOnlyList<string> decidedEffective,
        IReadOnlyDictionary<string, IReadOnlyList<string>> eligibleById,
        LineupConstraints constraints)
    {
        IReadOnlyList<string> Eligible(string id)
            => eligibleById.TryGetValue(id, out var e) ? e : Array.Empty<string>();

        var outgoing = Eligible(nonPlayingStarterId);
        var limits = constraints.PositionStart.ToDictionary(
            kv => kv.Key,
            kv => (outgoing.Contains(kv.Key) ? kv.Value.Min : 0, kv.Value.Max),
            StringComparer.Ordinal);

        var players = decidedEffective.Append(candidateId)
            .Select(id => new PositionCandidate(id, Eligible(id)))
            .ToList();
        return PositionAssignment.TrySolve(players, limits) is not null;
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests|FullyQualifiedName~SettleGameweek"`
Expected: PASS (all, including every pre-existing sub case).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekScoringService.cs Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs
git commit -m "feat(scoring): let bench subs cover a starter via their secondary position"
```

---

### Task 6: Lineup view carries `PositionSecondary` and `AssignedPosition`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/LineupView.cs` (`LineupPlayer`, `LineupViewMapper.Map`)
- Modify: `Ez.Handball.Api/LineupEndpoints.cs` (`LineupBody`)
- Test: `Ez.Handball.Tests/Application/UseCases/LineupViewMapperTests.cs`, plus the lineup endpoint test file (`grep -rln "LineupBody\|/lineup" Ez.Handball.Tests/Api`)

**Interfaces:**
- Consumes: Task 1, `SquadPlayer.PositionSecondary` / `EligiblePositions`.
- Produces: `LineupPlayer(..., int? BenchOrder, string? PositionSecondary = null, string? AssignedPosition = null)`.

- [ ] **Step 1: Write the failing test** in `LineupViewMapperTests`:

```csharp
    [Fact]
    public void Map_StarterAssignedToSecondary_BenchKeepsPrimary()
    {
        var owned = new[]
        {
            Player("a", "CB"), Player("b", "CB") with { PositionSecondary = "LB" }, Player("c", "CB") with { PositionSecondary = "LB" },
        };
        var lineup = new Lineup(new[]
        {
            new LineupSlot("a", LineupRole.Captain, null),
            new LineupSlot("b", LineupRole.Starter, null),
            new LineupSlot("c", LineupRole.Bench, 0),
        });
        var constraints = new LineupConstraints(1, 2,
            new Dictionary<string, (int, int)> { ["CB"] = (0, 1), ["LB"] = (0, 1) }, 2, true, false);

        var view = LineupViewMapper.Map(owned, lineup, constraints, new LineupValidation(true, Array.Empty<LineupViolation>()));

        Assert.Equal("CB", view.Slots.Single(s => s.PlayerId == "a").AssignedPosition);
        Assert.Equal("LB", view.Slots.Single(s => s.PlayerId == "b").AssignedPosition);
        Assert.Equal("LB", view.Slots.Single(s => s.PlayerId == "b").PositionSecondary);
        Assert.Equal("CB", view.Slots.Single(s => s.PlayerId == "c").AssignedPosition);
    }
```

In the lineup endpoint test, assert that a starter's and a bench player's JSON contain `positionSecondary` and `assignedPosition`, following that file's existing assertion pattern.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupViewMapperTests|FullyQualifiedName~LineupEndpoint"`
Expected: build FAIL.

- [ ] **Step 3: Implement.** Add the two trailing optional parameters to `LineupPlayer`. In `Map`:

```csharp
        // #108: starters take the slot the lineup solve gives them (highest-rated placed first, so
        // it's stable); bench players and unsolvable lineups show the primary.
        var starterCandidates = lineup.Slots
            .Where(s => s.Role != LineupRole.Bench && byId.ContainsKey(s.PlayerId))
            .Select(s => byId[s.PlayerId])
            .OrderByDescending(p => p.Rating).ThenBy(p => p.PlayerId, StringComparer.Ordinal)
            .Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions))
            .ToList();
        var assigned = PositionAssignment.TrySolve(starterCandidates, constraints.PositionStart);
```

and in the slot projection add `PositionSecondary: p?.PositionSecondary` and `AssignedPosition: assigned is not null && assigned.TryGetValue(s.PlayerId, out var pos) ? pos : p?.Position`.

`LineupEndpoints.LineupBody`: in both the `starters` and `bench` projections add `positionSecondary = s.PositionSecondary, assignedPosition = s.AssignedPosition` after `position = s.Position`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~Lineup"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/LineupView.cs Ez.Handball.Api/LineupEndpoints.cs Ez.Handball.Tests
git commit -m "feat(lineup): expose secondary and assigned position on lineup view"
```

---

### Task 7: Pool filter matches secondary; docs; full verification

**Files:**
- Modify: `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs` (position `Where`)
- Modify: `CLAUDE.md` (Gameweek engine section)
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`

- [ ] **Step 1: Write the failing test** in `GetPlayerPoolUseCaseTests`:

```csharp
    [Fact]
    public async Task Execute_PositionFilter_AlsoMatchesSecondary()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(
            Pooled("a", goals: 50, position: "CB"),
            Pooled("b", goals: 40, position: "GK") with { PositionSecondary = "cb" },
            Pooled("c", goals: 30, position: "LW"));

        var result = await CreateSut().ExecuteAsync(Req(position: "CB"), 0, 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(new[] { "a", "b" }, pool.Entries.Select(e => e.PlayerId));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests"`
Expected: the new test FAILS.

- [ ] **Step 3: Implement.** Change the position predicate to:

```csharp
            // #108: a player is buyable under their secondary too, so filtering by a position
            // includes them.
            .Where(p => string.IsNullOrWhiteSpace(request.Position)
                || string.Equals(p.Position, request.Position, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.PositionSecondary, request.Position, StringComparison.OrdinalIgnoreCase))
```

In `CLAUDE.md`, add a bullet at the end of the "Gameweek engine" list:

```markdown
- **Secondary positions (Backend#108):** a player is eligible under `Position` and, when set,
  `PositionSecondary` (read live from `Players`). Squad caps, lineup starter rules, the default
  lineup and bench subs all ask `PositionAssignment.TrySolve` whether *some* assignment of
  players to one eligible position each fits the limits — nobody counts twice. A squad already
  over a cap (positions changed after purchase) falls back to the primary-only count so unrelated
  buys still go through. Squad and lineup views return `positionSecondary` and the solved
  `assignedPosition`; the pool's `position` filter matches either position.
```

- [ ] **Step 4: Full verification**

Run:
```bash
dotnet build Ez.Handball.sln
(azurite --silent --location /tmp/azurite-test &) ; dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj
```
Expected: build succeeds with no new warnings, and all tests PASS. If Azurite isn't installed, run `dotnet test ... --filter "FullyQualifiedName!~Azurite&FullyQualifiedName!~TableWriterTests&FullyQualifiedName!~BlobArchiverTests"` and report that the integration tests were skipped.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs CLAUDE.md
git commit -m "feat(pool): match secondary position in pool filter and document #108"
```
