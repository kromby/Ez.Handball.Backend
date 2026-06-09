# Lineup & Captaincy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Layer a formation model (valid starting 7 + ordered bench, captain/vice) on top of the owned fantasy squad, with authenticated read/set endpoints.

**Architecture:** Clean-architecture, fantasy-only, round-agnostic (the round/freeze dimension arrives later in #60). A pure `LineupValidator` domain function is reused by both set (reject → 422) and read (compute `isValid` flag). Lineups persist one-row-per-player in a new `GameLineups` table, batch-reconciled under the team's partition key. Versioned formation rules live in the existing `Config` table under a new `fantasy-lineup-v{n}` group, seeded by an ingestion function — mirroring `fantasy-squad-v{n}`.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, Azure Table Storage (`Azure.Data.Tables`), xUnit + Moq, Azurite for table integration tests.

**Spec:** `docs/superpowers/specs/2026-06-09-lineup-and-captaincy-design.md`

---

## File structure

| File | Responsibility |
|------|----------------|
| `Ez.Handball.Domain/Lineup.cs` | `LineupRole` enum, `LineupSlot`, `Lineup` records |
| `Ez.Handball.Domain/LineupConstraints.cs` | `LineupConstraints` record |
| `Ez.Handball.Domain/LineupValidation.cs` | `LineupViolation`, `LineupValidation` records |
| `Ez.Handball.Domain/LineupValidator.cs` | Pure static validation function |
| `Ez.Handball.Shared/Entities/GameLineupEntity.cs` | Table row: one placed player |
| `Ez.Handball.Application/Abstractions/ILineupRepository.cs` | Read/replace stored lineup |
| `Ez.Handball.Application/Abstractions/ILineupConstraintsRepository.cs` | Read `fantasy-lineup-v{n}` |
| `Ez.Handball.Application/UseCases/LineupView.cs` | `LineupView`, `LineupPlayer` view records + `LineupViewMapper` |
| `Ez.Handball.Application/UseCases/GetLineupUseCase.cs` | Read + annotate validity |
| `Ez.Handball.Application/UseCases/SetLineupUseCase.cs` | Validate + persist |
| `Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs` | `GameLineups` reconcile |
| `Ez.Handball.Infrastructure/TableAccess/TableLineupConstraintsRepository.cs` | `Config` reader |
| `Ez.Handball.Infrastructure/Tables.cs` | Add `GameLineups` constant (modify) |
| `Ez.Handball.Infrastructure/InfrastructureRegistration.cs` | Register repos (modify) |
| `Ez.Handball.Api/LineupEndpoints.cs` | `GET`/`PUT /api/users/me/lineup` |
| `Ez.Handball.Api/Program.cs` | Register use cases, map endpoints (modify) |
| `Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs` | Seed `fantasy-lineup-v1` |

Tests live under `Ez.Handball.Tests/{Domain,Infrastructure/Tables,Application/UseCases,Api/Endpoints,Ingestion/Functions}/` mirroring the source path, per the existing convention.

---

## Task 1: Domain types

**Files:**
- Create: `Ez.Handball.Domain/Lineup.cs`
- Create: `Ez.Handball.Domain/LineupConstraints.cs`
- Create: `Ez.Handball.Domain/LineupValidation.cs`

- [ ] **Step 1: Create the lineup model**

`Ez.Handball.Domain/Lineup.cs`:

```csharp
namespace Ez.Handball.Domain;

// Captain and Vice ARE starters carrying the multiplier badge — collapsing role + captaincy
// into one enum makes "captain on the bench" and "captain == vice" unrepresentable.
public enum LineupRole
{
    Bench,
    Starter,
    Captain,
    Vice
}

public sealed record LineupSlot(
    string PlayerId,
    LineupRole Role,
    int? BenchOrder);    // 0-based bench priority; set iff Role == Bench, null otherwise

public sealed record Lineup(
    IReadOnlyList<LineupSlot> Slots);
```

- [ ] **Step 2: Create the constraints model**

`Ez.Handball.Domain/LineupConstraints.cs`:

```csharp
namespace Ez.Handball.Domain;

public sealed record LineupConstraints(
    int Version,
    int StarterCount,                                                   // 7
    IReadOnlyDictionary<string, (int Min, int Max)> PositionStart,      // per-position min/max among starters
    double CaptainMultiplier,                                           // 2.0 — stored here, applied by #60
    bool CaptainRequired,                                               // true
    bool ViceRequired);                                                 // false
```

- [ ] **Step 3: Create the validation result model**

`Ez.Handball.Domain/LineupValidation.cs`:

```csharp
namespace Ez.Handball.Domain;

public sealed record LineupViolation(string Code, string Message);

public sealed record LineupValidation(
    bool IsValid,
    IReadOnlyList<LineupViolation> Violations);
```

- [ ] **Step 4: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/Lineup.cs Ez.Handball.Domain/LineupConstraints.cs Ez.Handball.Domain/LineupValidation.cs
git commit -m "feat: add lineup domain types (#61)"
```

---

## Task 2: LineupValidator (pure domain function)

The heart of the feature. Validates a proposed lineup against the owned squad and constraints. No I/O. Reused by both use cases.

**Files:**
- Create: `Ez.Handball.Domain/LineupValidator.cs`
- Test: `Ez.Handball.Tests/Domain/LineupValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

`Ez.Handball.Tests/Domain/LineupValidatorTests.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class LineupValidatorTests
{
    private static readonly LineupConstraints Constraints = new(
        Version: 1,
        StarterCount: 7,
        PositionStart: new Dictionary<string, (int, int)>
        {
            ["GK"] = (1, 1),
            ["LW"] = (0, 2), ["RW"] = (0, 2),
            ["LB"] = (0, 3), ["CB"] = (0, 2), ["RB"] = (0, 3),
            ["LP"] = (0, 2),
        },
        CaptainMultiplier: 2,
        CaptainRequired: true,
        ViceRequired: false);

    // Build an owned squad of N players with the given positions; playerId = "p{index}".
    private static IReadOnlyList<SquadPlayer> Owned(params string[] positions)
        => positions.Select((pos, i) => new SquadPlayer(
            PlayerId: $"p{i}", Name: $"Name{i}", ClubId: "385", ClubName: "Stjarnan",
            Position: pos, Gender: "karlar",
            Price: new PlayerPrice(10_000_000, "ISK"),
            PricePaid: new PlayerPrice(10_000_000, "ISK"))).ToList();

    // A valid 8-player squad: 1 GK + 6 court starters + 1 bench court player.
    private static readonly string[] EightPositions =
        { "GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB" };

    // Place p0..p6 as the 7 starters (p0 captain), p7 as bench[0].
    private static Lineup ValidLineup() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Starter, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Starter, null),
        new LineupSlot("p4", LineupRole.Starter, null),
        new LineupSlot("p5", LineupRole.Starter, null),
        new LineupSlot("p6", LineupRole.Starter, null),
        new LineupSlot("p7", LineupRole.Bench, 0),
    });

    private static bool Has(LineupValidation v, string code) => v.Violations.Any(x => x.Code == code);

    [Fact]
    public void ValidLineup_IsValid_NoViolations()
    {
        var result = LineupValidator.Validate(ValidLineup(), Owned(EightPositions), Constraints);
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void UnownedPlayer_Flagged()
    {
        var lineup = new Lineup(ValidLineup().Slots
            .Select(s => s.PlayerId == "p7" ? s with { PlayerId = "ghost" } : s).ToList());
        var result = LineupValidator.Validate(lineup, Owned(EightPositions), Constraints);
        Assert.False(result.IsValid);
        Assert.True(Has(result, "unowned_player"));
    }

    [Fact]
    public void IncompleteSquad_WhenOwnedPlayerNotPlaced_Flagged()
    {
        // Owned squad has 9 players, lineup only places 8.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB", "LW");
        var result = LineupValidator.Validate(ValidLineup(), owned, Constraints);
        Assert.True(Has(result, "incomplete_squad"));
    }

    [Fact]
    public void DuplicatePlayer_Flagged()
    {
        var lineup = new Lineup(ValidLineup().Slots
            .Select(s => s.PlayerId == "p7" ? s with { PlayerId = "p6" } : s).ToList());
        var result = LineupValidator.Validate(lineup, Owned(EightPositions), Constraints);
        Assert.True(Has(result, "duplicate_slot"));
    }

    [Fact]
    public void WrongStarterCount_Flagged()
    {
        // Demote p6 to bench → only 6 starters. Give it a bench order alongside p7.
        var slots = ValidLineup().Slots.Select(s =>
            s.PlayerId == "p6" ? new LineupSlot("p6", LineupRole.Bench, 1) : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "wrong_starter_count"));
    }

    [Fact]
    public void NoGoalkeeperStarter_FlagsPositionMin()
    {
        // Replace the GK starter with a second-CB starter; bench the GK.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB");
        var slots = new[]
        {
            new LineupSlot("p7", LineupRole.Captain, null),  // CB starts instead of GK
            new LineupSlot("p1", LineupRole.Starter, null),
            new LineupSlot("p2", LineupRole.Starter, null),
            new LineupSlot("p3", LineupRole.Starter, null),
            new LineupSlot("p4", LineupRole.Starter, null),
            new LineupSlot("p5", LineupRole.Starter, null),
            new LineupSlot("p6", LineupRole.Starter, null),
            new LineupSlot("p0", LineupRole.Bench, 0),       // GK benched
        };
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "position_min"));   // GK min=1 unmet
    }

    [Fact]
    public void TooManyAtPosition_FlagsPositionMax()
    {
        // Owned squad has 3 LW; start all 3 (LW max=2). 1 GK + 3 LW + 3 others = 7 starters.
        var owned = Owned("GK", "LW", "LW", "LW", "CB", "RB", "LP", "LB");
        var slots = new[]
        {
            new LineupSlot("p0", LineupRole.Captain, null), // GK
            new LineupSlot("p1", LineupRole.Starter, null), // LW
            new LineupSlot("p2", LineupRole.Starter, null), // LW
            new LineupSlot("p3", LineupRole.Starter, null), // LW
            new LineupSlot("p4", LineupRole.Starter, null), // CB
            new LineupSlot("p5", LineupRole.Starter, null), // RB
            new LineupSlot("p6", LineupRole.Starter, null), // LP
            new LineupSlot("p7", LineupRole.Bench, 0),      // LB
        };
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "position_max"));
    }

    [Fact]
    public void MissingCaptain_WhenRequired_Flagged()
    {
        var slots = ValidLineup().Slots.Select(s =>
            s.Role == LineupRole.Captain ? s with { Role = LineupRole.Starter } : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "missing_captain"));
    }

    [Fact]
    public void MultipleCaptains_Flagged()
    {
        var slots = ValidLineup().Slots.Select(s =>
            s.PlayerId == "p1" ? s with { Role = LineupRole.Captain } : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "multiple_captains"));
    }

    [Fact]
    public void BenchOrderGap_Flagged()
    {
        // Owned squad of 9; two bench players with orders {0, 2} — a gap.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB", "LW");
        var slots = ValidLineup().Slots.ToList();
        slots.Add(new LineupSlot("p8", LineupRole.Bench, 2)); // p7=0, p8=2 → gap at 1
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "bench_order"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupValidatorTests"`
Expected: FAIL — `LineupValidator` does not exist (compile error).

- [ ] **Step 3: Write the validator**

`Ez.Handball.Domain/LineupValidator.cs`:

```csharp
namespace Ez.Handball.Domain;

public static class LineupValidator
{
    public static LineupValidation Validate(
        Lineup proposed,
        IReadOnlyList<SquadPlayer> ownedSquad,
        LineupConstraints constraints)
    {
        var violations = new List<LineupViolation>();
        var slots = proposed.Slots;
        var ownedById = ownedSquad.ToDictionary(p => p.PlayerId);

        // duplicate_slot
        var seen = new HashSet<string>();
        if (slots.Any(s => !seen.Add(s.PlayerId)))
            violations.Add(new("duplicate_slot", "A player appears more than once in the lineup."));

        // unowned_player
        if (slots.Any(s => !ownedById.ContainsKey(s.PlayerId)))
            violations.Add(new("unowned_player", "The lineup references a player not in the owned squad."));

        // incomplete_squad: lineup ids must equal owned ids exactly
        var lineupIds = slots.Select(s => s.PlayerId).ToHashSet();
        if (!lineupIds.SetEquals(ownedById.Keys))
            violations.Add(new("incomplete_squad", "Every owned player must be placed in the lineup exactly once."));

        // starters = any non-bench role
        var starters = slots.Where(s => s.Role is LineupRole.Starter or LineupRole.Captain or LineupRole.Vice).ToList();
        if (starters.Count != constraints.StarterCount)
            violations.Add(new("wrong_starter_count", $"A lineup must have exactly {constraints.StarterCount} starters."));

        // position min/max among starters — position resolved from the owned roster snapshot
        var startersByPosition = starters
            .Select(s => ownedById.TryGetValue(s.PlayerId, out var p) ? p.Position : null)
            .Where(pos => pos is not null)
            .GroupBy(pos => pos!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var (position, (min, max)) in constraints.PositionStart)
        {
            startersByPosition.TryGetValue(position, out var count);
            if (count < min)
                violations.Add(new("position_min", $"At least {min} starter(s) required at {position}."));
            if (count > max)
                violations.Add(new("position_max", $"At most {max} starter(s) allowed at {position}."));
        }

        // captain
        var captains = slots.Count(s => s.Role == LineupRole.Captain);
        if (constraints.CaptainRequired && captains == 0)
            violations.Add(new("missing_captain", "A captain must be selected."));
        if (captains > 1)
            violations.Add(new("multiple_captains", "Only one captain may be selected."));

        // vice
        var vices = slots.Count(s => s.Role == LineupRole.Vice);
        if (constraints.ViceRequired && vices == 0)
            violations.Add(new("missing_vice", "A vice-captain must be selected."));
        if (vices > 1)
            violations.Add(new("multiple_vices", "Only one vice-captain may be selected."));

        // bench_order: bench orders contiguous 0..n-1, no nulls; starters carry no order
        var bench = slots.Where(s => s.Role == LineupRole.Bench).ToList();
        var benchOk = bench.All(b => b.BenchOrder is not null)
            && bench.Select(b => b.BenchOrder!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, bench.Count));
        var startersOk = starters.All(s => s.BenchOrder is null);
        if (!benchOk || !startersOk)
            violations.Add(new("bench_order", "Bench priority must run contiguously from 0 and starters must carry no bench order."));

        return new LineupValidation(violations.Count == 0, violations);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupValidatorTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/LineupValidator.cs Ez.Handball.Tests/Domain/LineupValidatorTests.cs
git commit -m "feat: add LineupValidator with full violation coverage (#61)"
```

---

## Task 3: GameLineupEntity + table constant

**Files:**
- Create: `Ez.Handball.Shared/Entities/GameLineupEntity.cs`
- Modify: `Ez.Handball.Infrastructure/Tables.cs`

- [ ] **Step 1: Create the entity**

`Ez.Handball.Shared/Entities/GameLineupEntity.cs`:

```csharp
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One placed player in a team's current lineup. PartitionKey = teamId, RowKey = playerId.
// No soft-delete: a lineup is a complete snapshot wholly replaced on each save.
public sealed class GameLineupEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string Role { get; set; } = string.Empty;         // "Bench" | "Starter" | "Captain" | "Vice"
    public int? BenchOrder { get; set; }                     // set iff Role == "Bench"
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

- [ ] **Step 2: Add the table constant**

In `Ez.Handball.Infrastructure/Tables.cs`, add after the `GameTeamNameIndex` line (line 21):

```csharp
    public const string GameLineups = "GameLineups";
```

- [ ] **Step 3: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Shared/Entities/GameLineupEntity.cs Ez.Handball.Infrastructure/Tables.cs
git commit -m "feat: add GameLineups table + entity (#61)"
```

---

## Task 4: ILineupRepository + TableLineupRepository

**Files:**
- Create: `Ez.Handball.Application/Abstractions/ILineupRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs`

- [ ] **Step 1: Create the abstraction**

`Ez.Handball.Application/Abstractions/ILineupRepository.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ILineupRepository
{
    // The team's current lineup, or null if never set.
    Task<Lineup?> GetAsync(string teamId, CancellationToken ct);

    // Full replacement: upsert the new slot set and delete rows no longer present.
    Task ReplaceAsync(string teamId, Lineup lineup, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing tests**

`Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs`:

```csharp
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableLineupRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ILineupRepository Sut() => new TableLineupRepository(_client, new TableQuery(_client));
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameLineups).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameLineups).DeleteAsync();

    private static Lineup Sample() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Vice, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Bench, 0),
        new LineupSlot("p4", LineupRole.Bench, 1),
    });

    [Fact]
    public async Task Get_WhenNeverSet_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync(Team, default));
    }

    [Fact]
    public async Task Replace_ThenGet_RoundTripsRolesAndBenchOrder()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        var got = await Sut().GetAsync(Team, default);

        Assert.NotNull(got);
        Assert.Equal(5, got!.Slots.Count);
        Assert.Equal(LineupRole.Captain, got.Slots.Single(s => s.PlayerId == "p0").Role);
        Assert.Equal(LineupRole.Vice, got.Slots.Single(s => s.PlayerId == "p1").Role);
        var bench = got.Slots.Where(s => s.Role == LineupRole.Bench).OrderBy(s => s.BenchOrder).ToList();
        Assert.Equal(new[] { "p3", "p4" }, bench.Select(s => s.PlayerId));
        Assert.Equal(new int?[] { 0, 1 }, bench.Select(s => s.BenchOrder));
    }

    [Fact]
    public async Task Replace_DropsRowsNoLongerPresent()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        // New lineup with only p0 and p9 — p1..p4 must be removed.
        var next = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Starter, null),
            new LineupSlot("p9", LineupRole.Bench, 0),
        });
        await Sut().ReplaceAsync(Team, next, default);

        var got = await Sut().GetAsync(Team, default);
        Assert.Equal(new[] { "p0", "p9" }, got!.Slots.Select(s => s.PlayerId).OrderBy(x => x));
    }

    [Fact]
    public async Task Replace_IsScopedByTeam()
    {
        await Sut().ReplaceAsync(Team, Sample(), default);
        await Sut().ReplaceAsync("u-2:fantasy", new Lineup(new[]
        {
            new LineupSlot("x0", LineupRole.Starter, null),
        }), default);

        var got = await Sut().GetAsync(Team, default);
        Assert.Equal(5, got!.Slots.Count);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLineupRepositoryTests"`
Expected: FAIL — `TableLineupRepository` does not exist.

- [ ] **Step 4: Write the repository**

`Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs`:

```csharp
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

// One row per placed player, all under the team's partition key. A save fully replaces the
// set via a single-partition transaction (a 15-player squad is well under the 100-action cap).
internal sealed class TableLineupRepository : ILineupRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableLineupRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<Lineup?> GetAsync(string teamId, CancellationToken ct)
    {
        var slots = new List<LineupSlot>();
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(
                           Tables.GameLineups, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            if (Enum.TryParse<LineupRole>(e.Role, out var role)) // tolerate unknown stored roles by skipping
                slots.Add(new LineupSlot(e.RowKey, role, e.BenchOrder));
        }
        return slots.Count == 0 ? null : new Lineup(slots);
    }

    public async Task ReplaceAsync(string teamId, Lineup lineup, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameLineups);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var desired = lineup.Slots.ToDictionary(s => s.PlayerId);
        var actions = new List<TableTransactionAction>();

        // Delete rows no longer present.
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(
                           Tables.GameLineups, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            if (!desired.ContainsKey(e.RowKey))
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete,
                    new GameLineupEntity { PartitionKey = teamId, RowKey = e.RowKey, ETag = ETag.All }));
        }

        // Upsert every desired slot.
        foreach (var s in lineup.Slots)
            actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace,
                new GameLineupEntity
                {
                    PartitionKey = teamId,
                    RowKey = s.PlayerId,
                    Role = s.Role.ToString(),
                    BenchOrder = s.BenchOrder
                }));

        if (actions.Count > 0)
            await table.SubmitTransactionAsync(actions, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLineupRepositoryTests"`
Expected: PASS (4 tests). Requires Azurite running (`azurite --silent --location /tmp/azurite-test &`).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Abstractions/ILineupRepository.cs Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs
git commit -m "feat: add TableLineupRepository with batch reconcile (#61)"
```

---

## Task 5: ILineupConstraintsRepository + TableLineupConstraintsRepository

**Files:**
- Create: `Ez.Handball.Application/Abstractions/ILineupConstraintsRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableLineupConstraintsRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableLineupConstraintsRepositoryTests.cs`

- [ ] **Step 1: Create the abstraction**

`Ez.Handball.Application/Abstractions/ILineupConstraintsRepository.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ILineupConstraintsRepository
{
    // Reads the fantasy-lineup-v{version} config group; null if it doesn't exist.
    Task<LineupConstraints?> GetAsync(int version, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing tests**

`Ez.Handball.Tests/Infrastructure/Tables/TableLineupConstraintsRepositoryTests.cs`:

```csharp
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableLineupConstraintsRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ILineupConstraintsRepository Sut() => new TableLineupConstraintsRepository(new TableQuery(_client));

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.Config).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.Config).DeleteAsync();

    private async Task SeedAsync(params (string Key, string Value)[] rows)
    {
        var table = _client.GetTableClient(Tables.Config);
        foreach (var (key, value) in rows)
            await table.UpsertEntityAsync(new ConfigEntity
            {
                PartitionKey = "fantasy-lineup-v1", RowKey = key, Value = value
            }, TableUpdateMode.Replace);
    }

    [Fact]
    public async Task Get_WhenGroupAbsent_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync(1, default));
    }

    [Fact]
    public async Task Get_ParsesScalarsAndPositionMinMax()
    {
        await SeedAsync(
            ("starterCount", "7"),
            ("captainMultiplier", "2"),
            ("captainRequired", "true"),
            ("viceRequired", "false"),
            ("startMin:GK", "1"), ("startMax:GK", "1"),
            ("startMin:LB", "0"), ("startMax:LB", "3"));

        var c = await Sut().GetAsync(1, default);

        Assert.NotNull(c);
        Assert.Equal(7, c!.StarterCount);
        Assert.Equal(2, c.CaptainMultiplier);
        Assert.True(c.CaptainRequired);
        Assert.False(c.ViceRequired);
        Assert.Equal((1, 1), c.PositionStart["GK"]);
        Assert.Equal((0, 3), c.PositionStart["LB"]);
    }

    [Fact]
    public async Task Get_WhenRequiredKeyMissing_ReturnsNull()
    {
        await SeedAsync(("captainMultiplier", "2")); // no starterCount
        Assert.Null(await Sut().GetAsync(1, default));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLineupConstraintsRepositoryTests"`
Expected: FAIL — `TableLineupConstraintsRepository` does not exist.

- [ ] **Step 4: Write the repository**

`Ez.Handball.Infrastructure/TableAccess/TableLineupConstraintsRepository.cs`:

```csharp
using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableLineupConstraintsRepository : ILineupConstraintsRepository
{
    private const string MinPrefix = "startMin:";
    private const string MaxPrefix = "startMax:";

    private readonly ITableQuery _query;

    public TableLineupConstraintsRepository(ITableQuery query) => _query = query;

    public async Task<LineupConstraints?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-lineup-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "starterCount", out var starterCount) ||
            !TryGetDouble(values, "captainMultiplier", out var captainMultiplier))
            return null;

        var captainRequired = !values.TryGetValue("captainRequired", out var cr) || !bool.TryParse(cr, out var crv) || crv;
        var viceRequired = values.TryGetValue("viceRequired", out var vr) && bool.TryParse(vr, out var vrv) && vrv;

        var mins = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (kv.Key.StartsWith(MinPrefix, StringComparison.Ordinal)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
                mins[kv.Key[MinPrefix.Length..]] = min;
            else if (kv.Key.StartsWith(MaxPrefix, StringComparison.Ordinal)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
                maxs[kv.Key[MaxPrefix.Length..]] = max;
        }

        var positionStart = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
        foreach (var position in mins.Keys.Union(maxs.Keys))
        {
            mins.TryGetValue(position, out var min);
            // A position with only a min defaults max to the full starter count (effectively no cap).
            var max = maxs.TryGetValue(position, out var m) ? m : starterCount;
            positionStart[position] = (min, max);
        }

        return new LineupConstraints(version, starterCount, positionStart, captainMultiplier, captainRequired, viceRequired);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, string> values, string key, out double result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLineupConstraintsRepositoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Abstractions/ILineupConstraintsRepository.cs Ez.Handball.Infrastructure/TableAccess/TableLineupConstraintsRepository.cs Ez.Handball.Tests/Infrastructure/Tables/TableLineupConstraintsRepositoryTests.cs
git commit -m "feat: add TableLineupConstraintsRepository (#61)"
```

---

## Task 6: SeedLineupConstraintsFunction

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs`
- Test: `Ez.Handball.Tests/Ingestion/Functions/SeedLineupConstraintsFunctionTests.cs`

- [ ] **Step 1: Write the failing tests**

`Ez.Handball.Tests/Ingestion/Functions/SeedLineupConstraintsFunctionTests.cs`:

```csharp
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedLineupConstraintsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SeedLineupConstraintsFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public void Definitions_AreTheFantasyLineupV1Group()
    {
        var defs = SeedLineupConstraintsFunction.ConstraintDefinitions;

        Assert.All(defs, d => Assert.Equal("fantasy-lineup-v1", d.Group));
        Assert.Contains(defs, d => d.Key == "starterCount" && d.Value == "7");
        Assert.Contains(defs, d => d.Key == "captainMultiplier" && d.Value == "2");
        Assert.Contains(defs, d => d.Key == "startMin:GK" && d.Value == "1");
        Assert.Contains(defs, d => d.Key == "startMax:GK" && d.Value == "1");
    }

    [Fact]
    public async Task ProcessAsync_UpsertsEveryRow_IntoConfigTable()
    {
        var seeded = await CreateSut().ProcessAsync();

        Assert.Equal(SeedLineupConstraintsFunction.ConstraintDefinitions.Count, seeded);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Config",
            It.Is<ConfigEntity>(e => e.PartitionKey == "fantasy-lineup-v1"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace),
            Times.Exactly(SeedLineupConstraintsFunction.ConstraintDefinitions.Count));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SeedLineupConstraintsFunctionTests"`
Expected: FAIL — `SeedLineupConstraintsFunction` does not exist.

- [ ] **Step 3: Write the function**

`Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs`:

```csharp
using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedLineupConstraintsFunction
{
    // Fantasy lineup (formation) constraints. starterCount = size of the starting 7;
    // captainMultiplier is read by scoring (#60); startMin/startMax:{Position} bound how many
    // starters may play each position (GK min=max=1 = exactly one keeper). PLACEHOLDER position
    // vocabulary — must be reconciled with real Player.Position values (owner review). Tunable.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConstraintDefinitions =
    [
        ("fantasy-lineup-v1", "starterCount",      "7"),
        ("fantasy-lineup-v1", "captainMultiplier", "2"),
        ("fantasy-lineup-v1", "captainRequired",   "true"),
        ("fantasy-lineup-v1", "viceRequired",      "false"),
        ("fantasy-lineup-v1", "startMin:GK", "1"), ("fantasy-lineup-v1", "startMax:GK", "1"),
        ("fantasy-lineup-v1", "startMin:LW", "0"), ("fantasy-lineup-v1", "startMax:LW", "2"),
        ("fantasy-lineup-v1", "startMin:RW", "0"), ("fantasy-lineup-v1", "startMax:RW", "2"),
        ("fantasy-lineup-v1", "startMin:LB", "0"), ("fantasy-lineup-v1", "startMax:LB", "3"),
        ("fantasy-lineup-v1", "startMin:CB", "0"), ("fantasy-lineup-v1", "startMax:CB", "2"),
        ("fantasy-lineup-v1", "startMin:RB", "0"), ("fantasy-lineup-v1", "startMax:RB", "3"),
        ("fantasy-lineup-v1", "startMin:LP", "0"), ("fantasy-lineup-v1", "startMax:LP", "2"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedLineupConstraintsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedLineupConstraints")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/lineup-constraints")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in ConstraintDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace); // explicit Replace keeps seeding idempotent
        }

        return ConstraintDefinitions.Count;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SeedLineupConstraintsFunctionTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs Ez.Handball.Tests/Ingestion/Functions/SeedLineupConstraintsFunctionTests.cs
git commit -m "feat: add SeedLineupConstraintsFunction (#61)"
```

---

## Task 7: LineupView + LineupViewMapper

The enriched view returned by both use cases, plus the mapper that joins stored slots with the owned squad.

**Files:**
- Create: `Ez.Handball.Application/UseCases/LineupView.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/LineupViewMapperTests.cs`

- [ ] **Step 1: Write the failing test**

`Ez.Handball.Tests/Application/UseCases/LineupViewMapperTests.cs`:

```csharp
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.UseCases;

public class LineupViewMapperTests
{
    private static SquadPlayer Player(string id, string pos) => new(
        PlayerId: id, Name: $"N{id}", ClubId: "385", ClubName: "Stjarnan",
        Position: pos, Gender: "karlar",
        Price: new PlayerPrice(10_000_000, "ISK"), PricePaid: new PlayerPrice(9_000_000, "ISK"));

    [Fact]
    public void Map_EnrichesSlotsFromOwnedSquad()
    {
        var owned = new[] { Player("p0", "GK"), Player("p1", "LW") };
        var lineup = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Captain, null),
            new LineupSlot("p1", LineupRole.Bench, 0),
        });
        var constraints = new LineupConstraints(1, 7,
            new Dictionary<string, (int, int)>(), CaptainMultiplier: 2, CaptainRequired: true, ViceRequired: false);
        var validation = new LineupValidation(true, Array.Empty<LineupViolation>());

        var view = LineupViewMapper.Map(owned, lineup, constraints, validation);

        Assert.Equal(2, view.Slots.Count);
        Assert.Equal(2, view.CaptainMultiplier);
        Assert.True(view.IsValid);
        var captain = view.Slots.Single(s => s.Role == LineupRole.Captain);
        Assert.Equal("p0", captain.PlayerId);
        Assert.Equal("GK", captain.Position);
        Assert.Equal("Np0", captain.Name);
        Assert.Equal(10_000_000, captain.Price!.Amount);
    }

    [Fact]
    public void Map_UnownedSlot_HasNullEnrichment()
    {
        var owned = new[] { Player("p0", "GK") };
        var lineup = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Starter, null),
            new LineupSlot("ghost", LineupRole.Bench, 0),
        });
        var constraints = new LineupConstraints(1, 7,
            new Dictionary<string, (int, int)>(), 2, true, false);
        var validation = new LineupValidation(false,
            new[] { new LineupViolation("unowned_player", "x") });

        var view = LineupViewMapper.Map(owned, lineup, constraints, validation);

        var ghost = view.Slots.Single(s => s.PlayerId == "ghost");
        Assert.Null(ghost.Name);
        Assert.Null(ghost.Position);
        Assert.Null(ghost.Price);
        Assert.False(view.IsValid);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupViewMapperTests"`
Expected: FAIL — `LineupView` / `LineupViewMapper` do not exist.

- [ ] **Step 3: Write the view + mapper**

`Ez.Handball.Application/UseCases/LineupView.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// One placed player, enriched for display. Name/ClubName/Position/Price are null when the
// player can't be resolved in the current squad (e.g. a stale lineup referencing a sold player).
public sealed record LineupPlayer(
    string PlayerId,
    string? Name,
    string? ClubName,
    string? Position,
    PlayerPrice? Price,
    LineupRole Role,
    int? BenchOrder);

public sealed record LineupView(
    IReadOnlyList<LineupPlayer> Slots,
    double CaptainMultiplier,
    bool IsValid,
    IReadOnlyList<LineupViolation> Violations);

public static class LineupViewMapper
{
    public static LineupView Map(
        IReadOnlyList<SquadPlayer> owned,
        Lineup lineup,
        LineupConstraints constraints,
        LineupValidation validation)
    {
        var byId = owned.ToDictionary(p => p.PlayerId);
        var slots = lineup.Slots.Select(s =>
        {
            byId.TryGetValue(s.PlayerId, out var p);
            return new LineupPlayer(
                PlayerId: s.PlayerId,
                Name: p?.Name,
                ClubName: p?.ClubName,
                Position: p?.Position,
                Price: p?.Price,
                Role: s.Role,
                BenchOrder: s.BenchOrder);
        }).ToList();

        return new LineupView(slots, constraints.CaptainMultiplier, validation.IsValid, validation.Violations);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupViewMapperTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/LineupView.cs Ez.Handball.Tests/Application/UseCases/LineupViewMapperTests.cs
git commit -m "feat: add LineupView + LineupViewMapper (#61)"
```

---

## Task 8: GetLineupUseCase

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetLineupUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetLineupUseCaseTests.cs`

**Note on `ruleSetVersion`:** the single param drives the lineup-constraints version AND is forwarded to `IGetSquadUseCase` for price-scoped enrichment. In practice both share a version line (v1).

- [ ] **Step 1: Write the failing tests**

`Ez.Handball.Tests/Application/UseCases/GetLineupUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetLineupUseCaseTests
{
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<ILineupRepository> _lineup = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private GetLineupUseCase Sut() => new(_squad.Object, _lineup.Object, _constraints.Object);

    private static LineupConstraints C() => new(1, 7,
        new Dictionary<string, (int, int)> { ["GK"] = (1, 1) }, 2, true, false);

    private static SquadPlayer Player(string id, string pos) => new(
        id, $"N{id}", "385", "Stjarnan", pos, "karlar",
        new PlayerPrice(10_000_000, "ISK"), new PlayerPrice(9_000_000, "ISK"));

    private static SquadView SquadOf(params SquadPlayer[] players) => new(
        players, new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));

    [Fact]
    public async Task ConstraintsMissing_ReturnsRuleSetNotFound()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((LineupConstraints?)null);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        Assert.IsType<GetLineupResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task NoStoredLineup_ReturnsNotSet_WithMultiplier()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        var notSet = Assert.IsType<GetLineupResult.NotSet>(result);
        Assert.Equal(2, notSet.CaptainMultiplier);
    }

    [Fact]
    public async Task StoredLineup_ReturnsFound_WithValidityAnnotated()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("p0", LineupRole.Captain, null) }));
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(SquadOf(Player("p0", "GK"))));

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        var found = Assert.IsType<GetLineupResult.Found>(result);
        Assert.Single(found.View.Slots);
        // One GK starter but only 1 of 7 starters → invalid; the view still returns.
        Assert.False(found.View.IsValid);
    }

    [Fact]
    public async Task SquadRuleSetNotFound_PropagatesRuleSetNotFound()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("p0", LineupRole.Starter, null) }));
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadResult.RuleSetNotFound.Instance);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        Assert.IsType<GetLineupResult.RuleSetNotFound>(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetLineupUseCaseTests"`
Expected: FAIL — `GetLineupUseCase` does not exist.

- [ ] **Step 3: Write the use case**

`Ez.Handball.Application/UseCases/GetLineupUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetLineupResult
{
    public sealed record RuleSetNotFound : GetLineupResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record NotSet(double CaptainMultiplier) : GetLineupResult;
    public sealed record Found(LineupView View) : GetLineupResult;
}

public interface IGetLineupUseCase
{
    Task<GetLineupResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetLineupUseCase : IGetLineupUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGetSquadUseCase _squad;
    private readonly ILineupRepository _lineup;
    private readonly ILineupConstraintsRepository _constraints;

    public GetLineupUseCase(
        IGetSquadUseCase squad, ILineupRepository lineup, ILineupConstraintsRepository constraints)
    {
        _squad = squad;
        _lineup = lineup;
        _constraints = constraints;
    }

    public async Task<GetLineupResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        var version = ruleSetVersion ?? DefaultVersion;

        var constraints = await _constraints.GetAsync(version, ct);
        if (constraints is null) return GetLineupResult.RuleSetNotFound.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var stored = await _lineup.GetAsync(teamId, ct);
        if (stored is null) return new GetLineupResult.NotSet(constraints.CaptainMultiplier);

        var squadResult = await _squad.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
        if (squadResult is not GetSquadResult.Found found)
            return GetLineupResult.RuleSetNotFound.Instance;

        var validation = LineupValidator.Validate(stored, found.View.Players, constraints);
        var view = LineupViewMapper.Map(found.View.Players, stored, constraints, validation);
        return new GetLineupResult.Found(view);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetLineupUseCaseTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetLineupUseCase.cs Ez.Handball.Tests/Application/UseCases/GetLineupUseCaseTests.cs
git commit -m "feat: add GetLineupUseCase (#61)"
```

---

## Task 9: SetLineupUseCase

**Files:**
- Create: `Ez.Handball.Application/UseCases/SetLineupUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/SetLineupUseCaseTests.cs`

- [ ] **Step 1: Write the failing tests**

`Ez.Handball.Tests/Application/UseCases/SetLineupUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetLineupUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<ILineupRepository> _lineup = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private SetLineupUseCase Sut() => new(_teams.Object, _squad.Object, _lineup.Object, _constraints.Object);

    private static LineupConstraints C() => new(1, 7,
        new Dictionary<string, (int, int)> { ["GK"] = (1, 1) }, 2, true, false);

    private static SquadPlayer Player(string id, string pos) => new(
        id, $"N{id}", "385", "Stjarnan", pos, "karlar",
        new PlayerPrice(10_000_000, "ISK"), new PlayerPrice(9_000_000, "ISK"));

    private static SquadView SquadOf(params SquadPlayer[] players) => new(
        players, new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));

    // A valid 8-player owned squad and matching lineup (1 GK + 6 court starters + 1 bench).
    private static SquadView ValidSquad() => SquadOf(
        Player("p0", "GK"), Player("p1", "LW"), Player("p2", "RW"), Player("p3", "LB"),
        Player("p4", "CB"), Player("p5", "RB"), Player("p6", "LP"), Player("p7", "CB"));

    private static Lineup ValidLineup() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Starter, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Starter, null),
        new LineupSlot("p4", LineupRole.Starter, null),
        new LineupSlot("p5", LineupRole.Starter, null),
        new LineupSlot("p6", LineupRole.Starter, null),
        new LineupSlot("p7", LineupRole.Bench, 0),
    });

    private void Arrange(bool teamExists = true)
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(teamExists);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(ValidSquad()));
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        Arrange(teamExists: false);
        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);
        Assert.IsType<SetLineupResult.NoTeam>(result);
    }

    [Fact]
    public async Task ConstraintsMissing_ReturnsRuleSetNotFound()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((LineupConstraints?)null);

        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);

        Assert.IsType<SetLineupResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task InvalidLineup_ReturnsRejected_DoesNotPersist()
    {
        Arrange();
        // Demote the GK to bench → no GK starter, wrong starter count.
        var bad = new Lineup(ValidLineup().Slots.Select(s =>
            s.PlayerId == "p0" ? new LineupSlot("p0", LineupRole.Bench, 1) : s).ToList());

        var result = await Sut().ExecuteAsync("u-1", bad, null, null, null, default);

        var rejected = Assert.IsType<SetLineupResult.Rejected>(result);
        Assert.NotEmpty(rejected.Violations);
        _lineup.Verify(l => l.ReplaceAsync(It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidLineup_PersistsAndReturnsCommitted()
    {
        Arrange();

        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);

        var committed = Assert.IsType<SetLineupResult.Committed>(result);
        Assert.True(committed.View.IsValid);
        _lineup.Verify(l => l.ReplaceAsync("u-1:fantasy", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetLineupUseCaseTests"`
Expected: FAIL — `SetLineupUseCase` does not exist.

- [ ] **Step 3: Write the use case**

`Ez.Handball.Application/UseCases/SetLineupUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SetLineupResult
{
    public sealed record NoTeam : SetLineupResult { public static readonly NoTeam Instance = new(); }
    public sealed record RuleSetNotFound : SetLineupResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Rejected(IReadOnlyList<LineupViolation> Violations) : SetLineupResult;
    public sealed record Committed(LineupView View) : SetLineupResult;
}

public interface ISetLineupUseCase
{
    Task<SetLineupResult> ExecuteAsync(
        string userId, Lineup proposed, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class SetLineupUseCase : ISetLineupUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGetSquadUseCase _squad;
    private readonly ILineupRepository _lineup;
    private readonly ILineupConstraintsRepository _constraints;

    public SetLineupUseCase(
        IGameTeamRepository teams, IGetSquadUseCase squad,
        ILineupRepository lineup, ILineupConstraintsRepository constraints)
    {
        _teams = teams;
        _squad = squad;
        _lineup = lineup;
        _constraints = constraints;
    }

    public async Task<SetLineupResult> ExecuteAsync(
        string userId, Lineup proposed, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SetLineupResult.NoTeam.Instance;

        var version = ruleSetVersion ?? DefaultVersion;
        var constraints = await _constraints.GetAsync(version, ct);
        if (constraints is null) return SetLineupResult.RuleSetNotFound.Instance;

        var squadResult = await _squad.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
        if (squadResult is not GetSquadResult.Found found)
            return SetLineupResult.RuleSetNotFound.Instance;

        var validation = LineupValidator.Validate(proposed, found.View.Players, constraints);
        if (!validation.IsValid)
            return new SetLineupResult.Rejected(validation.Violations);

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        await _lineup.ReplaceAsync(teamId, proposed, ct);

        var view = LineupViewMapper.Map(found.View.Players, proposed, constraints, validation);
        return new SetLineupResult.Committed(view);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetLineupUseCaseTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/SetLineupUseCase.cs Ez.Handball.Tests/Application/UseCases/SetLineupUseCaseTests.cs
git commit -m "feat: add SetLineupUseCase (#61)"
```

---

## Task 10: LineupEndpoints + DI wiring

The HTTP edge: parse/validate the request shape, delegate to the use cases, map results to status codes. Plus DI registration so the WAF tests can resolve everything.

**Files:**
- Create: `Ez.Handball.Api/LineupEndpoints.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Modify: `Ez.Handball.Api/Program.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/LineupEndpointTests.cs`

- [ ] **Step 1: Register the repositories**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, add after the `IGameRosterRepository` line (line 33):

```csharp
        services.AddScoped<ILineupRepository, TableLineupRepository>();
        services.AddScoped<ILineupConstraintsRepository, TableLineupConstraintsRepository>();
```

- [ ] **Step 2: Register the use cases and map endpoints in Program.cs**

In `Ez.Handball.Api/Program.cs`, add after the `ISellPlayerUseCase` registration (line 146):

```csharp
builder.Services.AddScoped<IGetLineupUseCase, GetLineupUseCase>();
builder.Services.AddScoped<ISetLineupUseCase, SetLineupUseCase>();
```

And add after `app.MapSquadEndpoints();` (line 479):

```csharp
app.MapLineupEndpoints();
```

- [ ] **Step 3: Write the endpoints**

`Ez.Handball.Api/LineupEndpoints.cs`:

```csharp
using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record LineupStarterDto(string? PlayerId, string? Role);

public sealed record SetLineupRequest(
    string? Flavor,
    string? Season,
    string? TournamentId,
    int? RuleSetVersion,
    IReadOnlyList<LineupStarterDto>? Starters,
    IReadOnlyList<string>? Bench);

public static class LineupEndpoints
{
    private const string Base = "/api/users/me/lineup";

    public static void MapLineupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (
            string? flavor, string? season, string? tournamentId, int? ruleSetVersion,
            HttpContext http, IGetLineupUseCase uc, CancellationToken ct) =>
        {
            if (!IsFantasy(flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
            return result switch
            {
                GetLineupResult.RuleSetNotFound  => Results.BadRequest(new { error = "invalid_rule_set" }),
                GetLineupResult.NotSet n         => Results.Ok(EmptyBody(n.CaptainMultiplier)),
                GetLineupResult.Found f          => Results.Ok(LineupBody(f.View)),
                _                                => Results.Problem()
            };
        });

        group.MapPut("", async (
            SetLineupRequest req, HttpContext http, ISetLineupUseCase uc, CancellationToken ct) =>
        {
            if (!IsFantasy(req.Flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            if (!TryBuildLineup(req, out var lineup))
                return Results.BadRequest(new { error = "malformed_body" });

            var result = await uc.ExecuteAsync(userId, lineup, req.Season, req.TournamentId, req.RuleSetVersion, ct);
            return result switch
            {
                SetLineupResult.NoTeam          => Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict),
                SetLineupResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                SetLineupResult.Rejected r      => Results.Json(new { violations = r.Violations.Select(v => new { code = v.Code, message = v.Message }) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                SetLineupResult.Committed c     => Results.Ok(LineupBody(c.View)),
                _                               => Results.Problem()
            };
        });
    }

    // Build the domain Lineup from the request. Returns false on a structurally malformed body
    // (missing arrays, blank ids, or a starter role that isn't Starter/Captain/Vice). Business
    // rules (counts, positions, captaincy) are the validator's job, not the parser's.
    private static bool TryBuildLineup(SetLineupRequest req, out Lineup lineup)
    {
        lineup = new Lineup(Array.Empty<LineupSlot>());
        if (req.Starters is null || req.Bench is null) return false;

        var slots = new List<LineupSlot>(req.Starters.Count + req.Bench.Count);

        foreach (var s in req.Starters)
        {
            if (string.IsNullOrWhiteSpace(s.PlayerId)) return false;
            if (!Enum.TryParse<LineupRole>(s.Role, ignoreCase: true, out var role)) return false;
            if (role == LineupRole.Bench) return false; // bench players belong in Bench[]
            slots.Add(new LineupSlot(s.PlayerId, role, null));
        }

        for (var i = 0; i < req.Bench.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(req.Bench[i])) return false;
            slots.Add(new LineupSlot(req.Bench[i], LineupRole.Bench, i));
        }

        lineup = new Lineup(slots);
        return true;
    }

    private static bool IsFantasy(string? flavor)
        => string.IsNullOrWhiteSpace(flavor) || flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase);

    private static object EmptyBody(double captainMultiplier) => new
    {
        flavor = "fantasy",
        starters = Array.Empty<object>(),
        bench = Array.Empty<object>(),
        captainId = (string?)null,
        viceId = (string?)null,
        isValid = false,
        violations = Array.Empty<object>(),
        captainMultiplier
    };

    private static object LineupBody(LineupView view) => new
    {
        flavor = "fantasy",
        starters = view.Slots
            .Where(s => s.Role != LineupRole.Bench)
            .Select(s => new
            {
                playerId = s.PlayerId, name = s.Name, clubName = s.ClubName,
                position = s.Position, price = s.Price, role = s.Role.ToString()
            }),
        bench = view.Slots
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .Select(s => new
            {
                playerId = s.PlayerId, name = s.Name, clubName = s.ClubName,
                position = s.Position, price = s.Price, benchOrder = s.BenchOrder
            }),
        captainId = view.Slots.FirstOrDefault(s => s.Role == LineupRole.Captain)?.PlayerId,
        viceId = view.Slots.FirstOrDefault(s => s.Role == LineupRole.Vice)?.PlayerId,
        isValid = view.IsValid,
        violations = view.Violations.Select(v => new { code = v.Code, message = v.Message }),
        captainMultiplier = view.CaptainMultiplier
    };
}
```

- [ ] **Step 4: Write the failing tests**

`Ez.Handball.Tests/Api/Endpoints/LineupEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

[Collection("Azurite")]
public class LineupEndpointTests : IClassFixture<LineupEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetLineupUseCase> Get { get; } = new();
        public Mock<ISetLineupUseCase> Set { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                    ["Jwt:Issuer"] = "ez-handball",
                    ["Jwt:Audience"] = "ez-handball-web",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Auth:RateLimit:PermitLimit"] = "1000",
                    ["Auth:RateLimit:SensitivePermitLimit"] = "1000"
                }));
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetLineupUseCase)));
                services.AddSingleton(Get.Object);
                services.Remove(services.Single(d => d.ServiceType == typeof(ISetLineupUseCase)));
                services.AddSingleton(Set.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public LineupEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Get.Reset();
        _factory.Set.Reset();
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var clubs = _tables.GetTableClient(Tables.Clubs);
        await clubs.CreateIfNotExistsAsync();
        await clubs.UpsertEntityAsync(new ClubEntity { PartitionKey = "club", RowKey = "385", Name = "Stjarnan" });
    }

    public async Task DisposeAsync()
    {
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeamNameIndex })
        {
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { /* not created */ }
        }
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static LineupView SampleView() => new(
        new[]
        {
            new LineupPlayer("p0", "Aron", "Stjarnan", "GK", new PlayerPrice(10_000_000, "ISK"), LineupRole.Captain, null),
            new LineupPlayer("p7", "Bjarki", "Stjarnan", "CB", new PlayerPrice(8_000_000, "ISK"), LineupRole.Bench, 0),
        },
        CaptainMultiplier: 2, IsValid: true, Violations: Array.Empty<LineupViolation>());

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/lineup");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidFlavor_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/lineup?flavor=manager", token));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_Found_Returns200WithSplitShape()
    {
        _factory.Get.Setup(s => s.ExecuteAsync(It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetLineupResult.Found(SampleView()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/lineup", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p0", body.GetProperty("captainId").GetString());
        Assert.Equal(1, body.GetProperty("starters").GetArrayLength());
        Assert.Equal(1, body.GetProperty("bench").GetArrayLength());
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, body.GetProperty("captainMultiplier").GetDouble());
    }

    [Fact]
    public async Task Put_MalformedBody_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        // starters present but bench omitted → malformed
        req.Content = JsonContent.Create(new { flavor = "fantasy", starters = new[] { new { playerId = "p0", role = "Captain" } } });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("malformed_body", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Put_Rejected_Returns422WithViolations()
    {
        _factory.Set.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<Lineup>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetLineupResult.Rejected(new[] { new LineupViolation("wrong_starter_count", "x") }));
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        req.Content = JsonContent.Create(new
        {
            flavor = "fantasy",
            starters = new[] { new { playerId = "p0", role = "Captain" } },
            bench = Array.Empty<string>()
        });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("wrong_starter_count", body.GetProperty("violations")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_Committed_Returns200()
    {
        _factory.Set.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<Lineup>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetLineupResult.Committed(SampleView()));
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        req.Content = JsonContent.Create(new
        {
            flavor = "fantasy",
            starters = new[] { new { playerId = "p0", role = "Captain" } },
            bench = new[] { "p7" }
        });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p0", body.GetProperty("captainId").GetString());
    }
}
```

- [ ] **Step 5: Run the endpoint tests**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~LineupEndpointTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS — all prior tests plus the new lineup tests green (Azurite must be running).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Api/LineupEndpoints.cs Ez.Handball.Api/Program.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Api/Endpoints/LineupEndpointTests.cs
git commit -m "feat: add lineup endpoints + DI wiring (#61)"
```

---

## Done criteria

- `GET /api/users/me/lineup` returns the stored lineup (split into starters/bench/captainId/viceId) with a freshly-computed `isValid` + `violations`, or an empty body when never set.
- `PUT /api/users/me/lineup` validates the proposed lineup against the current owned squad and `fantasy-lineup-v{n}` constraints, persisting only when valid (422 with violations otherwise).
- Formation rules (exactly 7 starters, exactly 1 GK, per-position min/max, captain required, complete-squad placement, contiguous bench order, single captain/vice) all enforced by the pure `LineupValidator`.
- `POST /api/seed/lineup-constraints` seeds `fantasy-lineup-v1`.
- Full test suite green.

## Post-merge / ops note

After deploying, run `POST /api/seed/lineup-constraints` against each environment (mirrors the `seed/squad-constraints` step) so the `fantasy-lineup-v1` config group exists — otherwise both endpoints return `invalid_rule_set`. The position vocabulary in the seed is a placeholder pending owner reconciliation with real `Player.Position` values (same caveat as `seed/squad-constraints`).
