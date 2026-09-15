# Player Position Backfill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder position vocabulary on `PlayerEntity.Position` with a trustworthy value derived from HBStatz's per-match `position` field, plus a `PositionSecondary`, backfilled for existing matches and kept current as new matches sync.

**Architecture:** HBStatz's `game.php` JSON already flows through `TriggerHbStatzSyncFunction` for stat enrichment. This plan adds a position-label mapper, a pure mode/secondary calculator, a new `PlayerPositionObservations` table (one row per player/match observation, so re-processing a match is a plain idempotent upsert), and a small aggregator service that writes an observation and recomputes `Position`/`PositionSecondary` from a player's full history. The same aggregation logic backs both the live sync path (incremental) and a new one-time `BackfillPlayerPositionsFunction` (reprocesses archived blobs, no new HTTP calls). A `SetPlayerPositionFunction` covers players HBStatz can't reach.

**Tech Stack:** .NET 8, Azure Functions v4 isolated worker, Azure Table Storage (`Azure.Data.Tables`), xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-09-04-player-position-backfill-design.md`

## Global Constraints

- All table reads/writes go through `ITableWriter` (`Ez.Handball.Ingestion.Services`); all blob reads/writes go through `IBlobArchiver`. No direct `TableServiceClient`/`BlobServiceClient` calls in new code.
- New HTTP-triggered Functions use `AuthorizationLevel.Function` and, if they mutate data in bulk, default to a dry run (`dryRun` query param defaults to `true`; pass `?dryRun=false` to write) — same convention as `MergePlayersFunction`/`TransferPlayersFunction`.
- Table name strings are passed as literals in `Ez.Handball.Ingestion` (it does not reference `Ez.Handball.Infrastructure`'s `Tables` constants class) — match the existing style (`"Players"`, `"PlayerStats"`, etc.).
- Position codes are exactly: `GK`, `LW`, `RW`, `LB`, `CB`, `RB`, `LP` (ordinal, case-sensitive).
- Build: `dotnet build Ez.Handball.sln`. Test: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~<ClassName>"`.

---

### Task 1: Position vocabulary and HBStatz label mapper

**Files:**
- Create: `Ez.Handball.Ingestion/Parsing/PositionVocabulary.cs`
- Create: `Ez.Handball.Ingestion/Parsing/HbStatzPositionMapper.cs`
- Test: `Ez.Handball.Tests/Ingestion/Parsing/HbStatzPositionMapperTests.cs`

**Interfaces:**
- Produces: `PositionVocabulary.Codes : IReadOnlySet<string>` (the 7 valid codes); `HbStatzPositionMapper.MapToCode(string? hbStatzPosition) : string?`.

- [ ] **Step 1: Write the failing test**

```csharp
using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzPositionMapperTests
{
    [Theory]
    [InlineData("Goalkeeper", "GK")]
    [InlineData("Left Wing", "LW")]
    [InlineData("Right Wing", "RW")]
    [InlineData("Left Back", "LB")]
    [InlineData("Right Back", "RB")]
    [InlineData("Center", "CB")]
    [InlineData("Line", "LP")]
    [InlineData("goalkeeper", "GK")] // case-insensitive
    [InlineData(" Left Wing ", "LW")] // trims whitespace
    public void MapToCode_KnownLabel_ReturnsExpectedCode(string label, string expected)
    {
        Assert.Equal(expected, HbStatzPositionMapper.MapToCode(label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pivot")] // not an HBStatz label we've seen
    public void MapToCode_UnrecognizedOrBlank_ReturnsNull(string? label)
    {
        Assert.Null(HbStatzPositionMapper.MapToCode(label));
    }

    [Fact]
    public void PositionVocabulary_ContainsExactlySevenCodes()
    {
        Assert.Equal(
            new[] { "GK", "LW", "RW", "LB", "CB", "RB", "LP" },
            PositionVocabulary.Codes.OrderBy(c => c));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~HbStatzPositionMapperTests"`
Expected: FAIL to compile — `HbStatzPositionMapper` and `PositionVocabulary` don't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Ez.Handball.Ingestion/Parsing/PositionVocabulary.cs
namespace Ez.Handball.Ingestion.Parsing;

// The fantasy position vocabulary confirmed real by Backend#106 — previously a placeholder
// pending owner review in SeedSquadConstraintsFunction/SeedLineupConstraintsFunction.
public static class PositionVocabulary
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "GK", "LW", "RW", "LB", "CB", "RB", "LP"
    };
}
```

```csharp
// Ez.Handball.Ingestion/Parsing/HbStatzPositionMapper.cs
namespace Ez.Handball.Ingestion.Parsing;

// Maps HBStatz's English position labels (hbstatz.is/api/game.php's "position" field) onto the
// fantasy vocabulary. Unrecognized labels return null rather than throwing, so an HBStatz label
// we haven't seen yet is silently skipped instead of breaking the sync.
public static class HbStatzPositionMapper
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Goalkeeper"] = "GK",
            ["Left Wing"] = "LW",
            ["Right Wing"] = "RW",
            ["Left Back"] = "LB",
            ["Right Back"] = "RB",
            ["Center"] = "CB",
            ["Line"] = "LP",
        };

    public static string? MapToCode(string? hbStatzPosition)
    {
        if (string.IsNullOrWhiteSpace(hbStatzPosition)) return null;
        return Map.TryGetValue(hbStatzPosition.Trim(), out var code) ? code : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~HbStatzPositionMapperTests"`
Expected: PASS (11 tests)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Parsing/PositionVocabulary.cs Ez.Handball.Ingestion/Parsing/HbStatzPositionMapper.cs Ez.Handball.Tests/Ingestion/Parsing/HbStatzPositionMapperTests.cs
git commit -m "feat(hbstatz): add position vocabulary and HBStatz label mapper"
```

---

### Task 2: Position mode/secondary calculator

**Files:**
- Create: `Ez.Handball.Ingestion/Parsing/PositionModeCalculator.cs`
- Test: `Ez.Handball.Tests/Ingestion/Parsing/PositionModeCalculatorTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (pure).
- Produces: `PositionModeCalculator.Compute(IReadOnlyList<(string Code, DateTimeOffset MatchDate)> observations) : (string Primary, string? Secondary)` — used by Task 4's aggregator and Task 6's backfill function.

- [ ] **Step 1: Write the failing test**

```csharp
using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class PositionModeCalculatorTests
{
    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_SingleObservation_PrimaryIsThatCodeNoSecondary()
    {
        var (primary, secondary) = PositionModeCalculator.Compute(new[] { ("LB", Day(1)) });

        Assert.Equal("LB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_OneDistinctCode_NoSecondaryEvenWithManyObservations()
    {
        var observations = Enumerable.Range(1, 5).Select(d => ("CB", Day(d))).ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_SecondCodeAboveTenPercent_IsReturnedAsSecondary()
    {
        // 100 observations: 89 CB, 11 LB -> 11% > 10%
        var observations = Enumerable.Range(1, 89).Select(d => ("CB", Day(1)))
            .Concat(Enumerable.Range(1, 11).Select(d => ("LB", Day(2))))
            .ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Equal("LB", secondary);
    }

    [Fact]
    public void Compute_SecondCodeAtExactlyTenPercent_IsNotSecondary()
    {
        // 100 observations: 90 CB, 10 LB -> exactly 10%, rule requires STRICTLY more than 10%
        var observations = Enumerable.Range(1, 90).Select(d => ("CB", Day(1)))
            .Concat(Enumerable.Range(1, 10).Select(d => ("LB", Day(2))))
            .ToList();

        var (primary, secondary) = PositionModeCalculator.Compute(observations);

        Assert.Equal("CB", primary);
        Assert.Null(secondary);
    }

    [Fact]
    public void Compute_TiedPrimaryCounts_EarliestFirstSeenWins()
    {
        var observations = new[]
        {
            ("LB", Day(5)),  // LB first seen day 5
            ("RB", Day(2)),  // RB first seen day 2 -> should win the tie
            ("LB", Day(6)),
            ("RB", Day(7)),
        };

        var (primary, _) = PositionModeCalculator.Compute(observations);

        Assert.Equal("RB", primary);
    }

    [Fact]
    public void Compute_EmptyObservations_Throws()
    {
        Assert.Throws<ArgumentException>(() => PositionModeCalculator.Compute(Array.Empty<(string, DateTimeOffset)>()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PositionModeCalculatorTests"`
Expected: FAIL to compile — `PositionModeCalculator` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Ez.Handball.Ingestion/Parsing/PositionModeCalculator.cs
namespace Ez.Handball.Ingestion.Parsing;

// Primary = most-frequently-observed code; ties break by whichever code was observed earliest
// (deterministic without needing extra state). Secondary = the next most frequent code, but
// only if it accounts for more than 10% of total observations — otherwise there's no secondary.
public static class PositionModeCalculator
{
    public static (string Primary, string? Secondary) Compute(
        IReadOnlyList<(string Code, DateTimeOffset MatchDate)> observations)
    {
        if (observations.Count == 0)
            throw new ArgumentException("At least one observation is required.", nameof(observations));

        var ranked = observations
            .GroupBy(o => o.Code)
            .Select(g => new { Code = g.Key, Count = g.Count(), FirstSeen = g.Min(o => o.MatchDate) })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.FirstSeen)
            .ToList();

        var primary = ranked[0].Code;
        string? secondary = null;
        if (ranked.Count > 1 && (double)ranked[1].Count / observations.Count > 0.10)
            secondary = ranked[1].Code;

        return (primary, secondary);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PositionModeCalculatorTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Parsing/PositionModeCalculator.cs Ez.Handball.Tests/Ingestion/Parsing/PositionModeCalculatorTests.cs
git commit -m "feat(hbstatz): add primary/secondary position mode calculator"
```

---

### Task 3: PositionSecondary on PlayerEntity/Player, and the observation entity

**Files:**
- Modify: `Ez.Handball.Shared/Entities/PlayerEntity.cs`
- Modify: `Ez.Handball.Domain/Player.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs`
- Create: `Ez.Handball.Shared/Entities/PlayerPositionObservationEntity.cs`
- Modify: `Ez.Handball.Ingestion/Models/HbStatzGameResponse.cs`
- Modify: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs`

**Interfaces:**
- Produces: `PlayerEntity.PositionSecondary : string`; `Player.PositionSecondary : string` (trailing optional parameter, default `""`, so every existing `new Player(...)` call site keeps compiling unchanged); `PlayerPositionObservationEntity` (`PartitionKey`=playerId, `RowKey`=matchId, `Position`, `MatchDate`) — consumed by Task 4 and Task 6; `HbStatzPlayerLine.Position : string` and `HbStatzPlayerLine.PositionSecondary : string?` — consumed by Task 5 and Task 6.

- [ ] **Step 1: Write the failing test**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs` (inside the existing `TablePlayerRepositoryTests` class):

```csharp
    [Fact]
    public async Task GetByIdAsync_MapsPositionSecondary()
    {
        SetupRows("1", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1", Name = "X",
            Gender = "karlar", ClubId = "385", Position = "CB", PositionSecondary = "LB"
        });

        var result = await CreateSut().GetByIdAsync("1", default);

        Assert.NotNull(result);
        Assert.Equal("CB", result!.Position);
        Assert.Equal("LB", result.PositionSecondary);
    }

    [Fact]
    public async Task GetByIdAsync_BlankPositionSecondary_MapsToEmptyString()
    {
        SetupRows("1", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1", Name = "X",
            Gender = "karlar", ClubId = "385", Position = "CB"
        });

        var result = await CreateSut().GetByIdAsync("1", default);

        Assert.Equal(string.Empty, result!.PositionSecondary);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerRepositoryTests"`
Expected: FAIL to compile — `PlayerEntity.PositionSecondary` and `Player.PositionSecondary` don't exist yet.

- [ ] **Step 3: Implement**

`Ez.Handball.Shared/Entities/PlayerEntity.cs` — add a field right after `Position`:

```csharp
    public string Position { get; set; } = string.Empty;
    public string PositionSecondary { get; set; } = string.Empty;
```

`Ez.Handball.Domain/Player.cs` — append a trailing optional parameter (keeps every existing positional `new Player(...)` call site compiling):

```csharp
public sealed record Player(
    string PlayerId,
    string Name,
    string? JerseyNumber,
    DateOnly? DateOfBirth,
    int? Age,
    string TeamId,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    bool Retired,
    string PositionSecondary = "");
```

`Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs` — in `ToPlayer`, add the named argument:

```csharp
        return new Player(
            PlayerId: row.RowKey,
            Name: row.Name,
            JerseyNumber: row.JerseyNumber,
            DateOfBirth: dob,
            Age: age,
            TeamId: row.PartitionKey,
            ClubId: row.ClubId,
            ClubName: row.ClubName,
            Gender: row.Gender,
            Position: row.Position,
            Retired: row.Retired == true,
            PositionSecondary: row.PositionSecondary);
```

```csharp
// Ez.Handball.Shared/Entities/PlayerPositionObservationEntity.cs
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class PlayerPositionObservationEntity : ITableEntity
{
    // PartitionKey = hsi.is playerId; RowKey = matchId — one row per (player, match) HBStatz
    // observation, so re-processing the same match is a plain idempotent upsert (no double-count).
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Position { get; set; } = string.Empty; // mapped code, e.g. "LB"
    public DateTimeOffset MatchDate { get; set; }
}
```

`Ez.Handball.Ingestion/Models/HbStatzGameResponse.cs` — add two properties to `HbStatzPlayerLine` (after `Name`, before `Number` — order doesn't matter, grouping with identity fields):

```csharp
    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("position_secondary")]
    public string? PositionSecondary { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerRepositoryTests"`
Expected: PASS (all tests in the class, including the two new ones)

Then run the full suite once to confirm the `Player` record change didn't break other call sites:
Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (no regressions — `PlayerEndpointsTests.cs`'s positional `new Player(...)` calls keep compiling because `PositionSecondary` is trailing and optional)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Shared/Entities/PlayerEntity.cs Ez.Handball.Domain/Player.cs Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs Ez.Handball.Shared/Entities/PlayerPositionObservationEntity.cs Ez.Handball.Ingestion/Models/HbStatzGameResponse.cs Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs
git commit -m "feat(player): add PositionSecondary field and position observation entity"
```

---

### Task 4: HbStatzPlayerPositionAggregator

**Files:**
- Create: `Ez.Handball.Ingestion/Parsing/IHbStatzPlayerPositionAggregator.cs`
- Create: `Ez.Handball.Ingestion/Parsing/HbStatzPlayerPositionAggregator.cs`
- Modify: `Ez.Handball.Ingestion/Program.cs`
- Test: `Ez.Handball.Tests/Ingestion/Parsing/HbStatzPlayerPositionAggregatorTests.cs`

**Interfaces:**
- Consumes: `ITableWriter` (Task existing), `PlayerPositionObservationEntity`/`PlayerEntity` (Task 3), `PositionModeCalculator.Compute` (Task 2).
- Produces: `IHbStatzPlayerPositionAggregator.RecordAndRecomputeAsync(string playerId, string matchId, DateTimeOffset matchDate, string positionCode, CancellationToken ct = default) : Task` — consumed by Task 5.

- [ ] **Step 1: Write the failing test**

```csharp
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzPlayerPositionAggregatorTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private HbStatzPlayerPositionAggregator CreateSut() => new(_tableWriter.Object);

    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAndRecomputeAsync_RecordsObservationRow()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "" } });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations",
            It.Is<PlayerPositionObservationEntity>(e =>
                e.PartitionKey == "hsi-1" && e.RowKey == "m1" && e.Position == "LB" && e.MatchDate == Day(1)),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_UpdatesPlayerPositionFromFullHistory()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) },
                new() { PartitionKey = "hsi-1", RowKey = "m2", Position = "LB", MatchDate = Day(2) },
                new() { PartitionKey = "hsi-1", RowKey = "m3", Position = "CB", MatchDate = Day(3) },
            });
        var player = new PlayerEntity { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "" };
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { player });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m3", Day(3), "CB");

        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB" && e.PositionSecondary == ""),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_UnchangedPosition_DoesNotUpsertPlayer()
    {
        var player = new PlayerEntity { PartitionKey = "385-karlar", RowKey = "hsi-1", Position = "LB", PositionSecondary = "" };
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "hsi-1", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { player });

        await CreateSut().RecordAndRecomputeAsync("hsi-1", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task RecordAndRecomputeAsync_NoMatchingPlayerRow_StillRecordsObservationWithoutThrowing()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerPositionObservationEntity>(
                "PlayerPositionObservations", "PartitionKey eq 'ghost'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerPositionObservationEntity>
            {
                new() { PartitionKey = "ghost", RowKey = "m1", Position = "LB", MatchDate = Day(1) }
            });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>(
                "Players", "RowKey eq 'ghost'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        await CreateSut().RecordAndRecomputeAsync("ghost", "m1", Day(1), "LB");

        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~HbStatzPlayerPositionAggregatorTests"`
Expected: FAIL to compile — the interface/class don't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Ez.Handball.Ingestion/Parsing/IHbStatzPlayerPositionAggregator.cs
namespace Ez.Handball.Ingestion.Parsing;

public interface IHbStatzPlayerPositionAggregator
{
    Task RecordAndRecomputeAsync(
        string playerId, string matchId, DateTimeOffset matchDate, string positionCode, CancellationToken ct = default);
}
```

```csharp
// Ez.Handball.Ingestion/Parsing/HbStatzPlayerPositionAggregator.cs
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Ingestion.Parsing;

// Records one (player, match) position observation and immediately recomputes that player's
// Position/PositionSecondary from their full observation history. Called once per reconciled,
// position-mapped HBStatz player line by TriggerHbStatzSyncFunction's live sync path.
public class HbStatzPlayerPositionAggregator : IHbStatzPlayerPositionAggregator
{
    private readonly ITableWriter _tableWriter;

    public HbStatzPlayerPositionAggregator(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    public async Task RecordAndRecomputeAsync(
        string playerId, string matchId, DateTimeOffset matchDate, string positionCode, CancellationToken ct = default)
    {
        await _tableWriter.UpsertAsync("PlayerPositionObservations", new PlayerPositionObservationEntity
        {
            PartitionKey = playerId,
            RowKey = matchId,
            Position = positionCode,
            MatchDate = matchDate
        }, ct);

        var observations = await _tableWriter.QueryAsync<PlayerPositionObservationEntity>(
            "PlayerPositionObservations", $"PartitionKey eq '{Escape(playerId)}'", ct);

        var (primary, secondary) = PositionModeCalculator.Compute(
            observations.Select(o => (o.Position, o.MatchDate)).ToList());

        var players = await _tableWriter.QueryAsync<PlayerEntity>(
            "Players", $"RowKey eq '{Escape(playerId)}'", ct);
        var player = players.FirstOrDefault();
        if (player is null) return;

        var newSecondary = secondary ?? string.Empty;
        if (player.Position == primary && player.PositionSecondary == newSecondary) return;

        player.Position = primary;
        player.PositionSecondary = newSecondary;
        await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
```

In `Ez.Handball.Ingestion/Program.cs`, add a registration right after the existing `IPlayerParser` line:

```csharp
                services.AddSingleton<IHbStatzPlayerPositionAggregator, HbStatzPlayerPositionAggregator>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~HbStatzPlayerPositionAggregatorTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Parsing/IHbStatzPlayerPositionAggregator.cs Ez.Handball.Ingestion/Parsing/HbStatzPlayerPositionAggregator.cs Ez.Handball.Ingestion/Program.cs Ez.Handball.Tests/Ingestion/Parsing/HbStatzPlayerPositionAggregatorTests.cs
git commit -m "feat(hbstatz): add position observation aggregator and DI registration"
```

---

### Task 5: Wire the aggregator into TriggerHbStatzSyncFunction

**Files:**
- Modify: `Ez.Handball.Ingestion/Functions/TriggerHbStatzSyncFunction.cs`
- Modify: `Ez.Handball.Tests/Ingestion/Functions/TriggerHbStatzSyncFunctionTests.cs`

**Interfaces:**
- Consumes: `IHbStatzPlayerPositionAggregator.RecordAndRecomputeAsync` (Task 4), `HbStatzPositionMapper.MapToCode` (Task 1), `HbStatzPlayerLine.Position` (Task 3).

- [ ] **Step 1: Update the test file's `CreateSut` and add a new test**

In `Ez.Handball.Tests/Ingestion/Functions/TriggerHbStatzSyncFunctionTests.cs`:

```csharp
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();
    private readonly Mock<IHbStatzApiClient> _hbStatzClient = new();
    private readonly Mock<IHbStatzPlayerPositionAggregator> _positionAggregator = new();

    private TriggerHbStatzSyncFunction CreateSut() =>
        new(_tableWriter.Object, _blobArchiver.Object, _hbStatzClient.Object, _positionAggregator.Object);
```

(add `using Ez.Handball.Ingestion.Parsing;` to the file's usings for `IHbStatzPlayerPositionAggregator`.)

Add a new test to the same class:

```csharp
    [Fact]
    public async Task SyncAsync_ReconciledPlayerWithPosition_RecordsPositionObservation()
    {
        const string gameJsonWithPosition = """
        {
          "players": {
            "home": [ { "player_id": 803, "name": "Arnór Snær Óskarsson", "number": 6, "position": "Left Back", "goals": 9, "shots": 14, "assists": 2, "turnovers": 3, "steals": 0, "blocks": 0, "legal_stops": 2, "grade_total": 8.78 } ],
            "away": []
          }
        }
        """;

        SetupTournamentQuery("IngestHbStatz eq true", Tournament());
        _hbStatzClient.Setup(c => c.GetFixturesJsonAsync("olis", "M", 2025, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FixturesJson);
        _hbStatzClient.Setup(c => c.GetGameJsonAsync(12924, It.IsAny<CancellationToken>())).ReturnsAsync(gameJsonWithPosition);
        var match = Match("103414");
        SetupMatches(match);
        SetupClubs();
        _tableWriter.Setup(t => t.GetAsync<ClubEntity>("Clubs", "club", "390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClubEntity { RowKey = "390", Name = "Breiðablik" });
        SetupReconcilableRoster();
        SetupExistingPlayerStat("103414");

        await CreateSut().SyncAsync(null);

        _positionAggregator.Verify(a => a.RecordAndRecomputeAsync(
            "hsi-1", "103414", match.Date, "LB", It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TriggerHbStatzSyncFunctionTests"`
Expected: FAIL to compile — the constructor doesn't take a 4th argument yet.

- [ ] **Step 3: Implement**

In `Ez.Handball.Ingestion/Functions/TriggerHbStatzSyncFunction.cs`:

```csharp
public class TriggerHbStatzSyncFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly IBlobArchiver _blobArchiver;
    private readonly IHbStatzApiClient _hbStatzClient;
    private readonly IHbStatzPlayerPositionAggregator _positionAggregator;

    public TriggerHbStatzSyncFunction(
        ITableWriter tableWriter, IBlobArchiver blobArchiver, IHbStatzApiClient hbStatzClient,
        IHbStatzPlayerPositionAggregator positionAggregator)
    {
        _tableWriter = tableWriter;
        _blobArchiver = blobArchiver;
        _hbStatzClient = hbStatzClient;
        _positionAggregator = positionAggregator;
    }
```

Update the two call sites in `SyncMatchAsync` to pass `match.Date`:

```csharp
        var homeReconciled = await MergePlayerStatsAsync(match.RowKey, match.Date, match.HomeTeamId, game.Players.Home, logger, ct);
        var awayReconciled = await MergePlayerStatsAsync(match.RowKey, match.Date, match.AwayTeamId, game.Players.Away, logger, ct);
```

Update `MergePlayerStatsAsync`'s signature and add the position-recording call right after a line reconciles (independent of whether the stats merge itself succeeds, since position tracking isn't a stats concern):

```csharp
    private async Task<bool> MergePlayerStatsAsync(
        string matchId, DateTimeOffset matchDate, string teamId, IReadOnlyList<HbStatzPlayerLine> lines,
        ILogger? logger, CancellationToken ct)
    {
        var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{Escape(teamId)}'", ct);
        var allReconciled = true;

        foreach (var line in lines)
        {
            var playerId = HbStatzPlayerReconciler.Resolve(roster, line);
            if (playerId is null)
            {
                logger?.LogWarning(
                    "Could not reconcile HBStatz player {Name} (#{Number}) for team {TeamId} in match {MatchId}",
                    line.Name, line.Number, teamId, matchId);
                allReconciled = false;
                continue;
            }

            var positionCode = HbStatzPositionMapper.MapToCode(line.Position);
            if (positionCode is not null)
            {
                await _positionAggregator.RecordAndRecomputeAsync(playerId, matchId, matchDate, positionCode, ct);
            }

            // Fetch-then-merge, not a bare partial upsert: PlayerStatEntity's existing HSÍ
            // fields (Goals, TournamentId, Season, ...) are non-nullable, so a fresh partial
            // entity would serialize them as 0/"" and clobber the real values under Merge.
            var existing = await _tableWriter.GetAsync<PlayerStatEntity>("PlayerStats", matchId, playerId, ct);
            if (existing is null)
            {
                logger?.LogWarning(
                    "No existing PlayerStats row for player {PlayerId} in match {MatchId}; skipping HBStatz merge",
                    playerId, matchId);
                allReconciled = false;
                continue;
            }

            existing.HbStatzAssists = line.Assists;
            existing.HbStatzTurnovers = line.Turnovers;
            existing.HbStatzSteals = line.Steals;
            existing.HbStatzBlocks = line.Blocks;
            existing.HbStatzLegalStops = line.LegalStops;
            existing.HbStatzShots = line.Shots;
            existing.HbStatzExpectedGoals = line.Xg;
            existing.HbStatzSaves = line.GkSaves;
            existing.HbStatzShotsFaced = line.GkShotsFaced;
            existing.HbStatzSavePct = line.GkSavePct;
            existing.HbStatzExpectedSaves = line.GkXs;
            existing.HbStatzGradeTotal = line.GradeTotal;
            existing.HbStatzGradeOffense = line.GradeOffense;
            existing.HbStatzGradeDefense = line.GradeDefense;
            existing.HbStatzGradeGoalkeeping = line.GradeGoalkeeping;

            await _tableWriter.UpsertAsync("PlayerStats", existing, ct, TableUpdateMode.Merge);
        }

        return allReconciled;
    }
```

Add `using Ez.Handball.Ingestion.Parsing;` to the file's usings if not already present (it already imports `Ez.Handball.Ingestion.Parsing` for `HbStatzPlayerReconciler` — confirm and reuse).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TriggerHbStatzSyncFunctionTests"`
Expected: PASS (all existing tests plus the new one — existing `GameJson` fixtures have no `"position"` field, so `MapToCode` returns null and the aggregator is never called for them, leaving prior assertions unaffected)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/TriggerHbStatzSyncFunction.cs Ez.Handball.Tests/Ingestion/Functions/TriggerHbStatzSyncFunctionTests.cs
git commit -m "feat(hbstatz): record position observations during live sync"
```

---

### Task 6: BackfillPlayerPositionsFunction (historical backfill)

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/BackfillPlayerPositionsFunction.cs`
- Test: `Ez.Handball.Tests/Ingestion/Functions/BackfillPlayerPositionsFunctionTests.cs`

**Interfaces:**
- Consumes: `IBlobArchiver.ListAsync("hbstatz/matches/", ct)` / `.ReadAsync`, `ITableWriter`, `HbStatzPlayerReconciler.Resolve`, `HbStatzPositionMapper.MapToCode`, `PositionModeCalculator.Compute`.
- Produces: `BackfillPlayerPositionsFunction.ProcessAsync(bool dryRun, ILogger? logger = null, CancellationToken ct = default) : Task<BackfillPlayerPositionsResult>`, route `POST /api/players/backfill-positions`.

- [ ] **Step 1: Write the failing test**

```csharp
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class BackfillPlayerPositionsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();

    private BackfillPlayerPositionsFunction CreateSut() => new(_tableWriter.Object, _blobArchiver.Object);

    private static DateTimeOffset Day(int d) => new(2026, 1, d, 0, 0, 0, TimeSpan.Zero);

    private const string GameJsonLeftBack = """
    {
      "players": {
        "home": [ { "player_id": 803, "name": "Arnór Snær Óskarsson", "number": 6, "position": "Left Back" } ],
        "away": []
      }
    }
    """;

    private void SetupOneArchivedMatch(string matchId, DateTimeOffset date, string json)
    {
        _blobArchiver.Setup(b => b.ListAsync("hbstatz/matches/", It.IsAny<CancellationToken>()))
            .Returns(ToAsync(new[] { $"hbstatz/matches/{matchId}.json" }));
        _blobArchiver.Setup(b => b.ReadAsync($"hbstatz/matches/{matchId}.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity> { new() { PartitionKey = "9142", RowKey = matchId, HomeTeamId = "385-karlar", AwayTeamId = "390-karlar", Date = date } });
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ReportsChangeWithoutWriting()
    {
        SetupOneArchivedMatch("103414", Day(1), GameJsonLeftBack);
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", JerseyNumber = "6", Position = "" } });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", Position = "" } });

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.BlobsProcessed);
        var change = Assert.Single(result.Changes);
        Assert.Equal("hsi-1", change.PlayerId);
        Assert.Equal("LB", change.NewPosition);
        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations", It.IsAny<PlayerPositionObservationEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LiveRun_WritesObservationAndUpdatesPlayer()
    {
        SetupOneArchivedMatch("103414", Day(1), GameJsonLeftBack);
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", JerseyNumber = "6", Position = "" } });
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '390-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Arnór", Position = "" } });

        var result = await CreateSut().ProcessAsync(dryRun: false);

        Assert.False(result.DryRun);
        Assert.Equal(1, result.PlayersUpdated);
        _tableWriter.Verify(t => t.UpsertAsync("PlayerPositionObservations",
            It.Is<PlayerPositionObservationEntity>(e => e.PartitionKey == "hsi-1" && e.RowKey == "103414" && e.Position == "LB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Replace), Times.Once);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoMatchesRowForBlob_RecordsErrorAndContinues()
    {
        _blobArchiver.Setup(b => b.ListAsync("hbstatz/matches/", It.IsAny<CancellationToken>()))
            .Returns(ToAsync(new[] { "hbstatz/matches/999999.json" }));
        _tableWriter.Setup(t => t.QueryAsync<MatchEntity>("Matches", "RowKey eq '999999'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity>());

        var result = await CreateSut().ProcessAsync(dryRun: true);

        Assert.Equal(0, result.BlobsProcessed);
        Assert.Single(result.Errors);
        Assert.Empty(result.Changes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BackfillPlayerPositionsFunctionTests"`
Expected: FAIL to compile — `BackfillPlayerPositionsFunction` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Ez.Handball.Ingestion/Functions/BackfillPlayerPositionsFunction.cs
using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record BackfillPositionResult(
    string PlayerId, string PlayerName, string OldPosition, string NewPosition, string OldSecondary, string NewSecondary);

public record BackfillPlayerPositionsResult(
    bool DryRun, int BlobsProcessed, int PlayersUpdated, IReadOnlyList<BackfillPositionResult> Changes, IReadOnlyList<string> Errors);

// One-time (rerunnable) historical backfill: replays every archived hbstatz/matches/*.json blob
// through the same reconciliation + position mapping the live TriggerHbStatzSyncFunction uses,
// without any new HTTP calls to HBStatz. Needed because TriggerHbStatzSyncFunction's default
// sweep skips matches that already have HbStatzSyncedAt set, so matches synced before this
// feature existed would otherwise never get a Position.
public class BackfillPlayerPositionsFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly IBlobArchiver _blobArchiver;

    public BackfillPlayerPositionsFunction(ITableWriter tableWriter, IBlobArchiver blobArchiver)
    {
        _tableWriter = tableWriter;
        _blobArchiver = blobArchiver;
    }

    [Function("BackfillPlayerPositions")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/backfill-positions")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<BackfillPlayerPositionsFunction>();
        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);
        var result = await ProcessAsync(dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<BackfillPlayerPositionsResult> ProcessAsync(
        bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var observationsByPlayer = new Dictionary<string, List<(string Code, DateTimeOffset MatchDate, string MatchId)>>();
        var errors = new List<string>();
        var blobsProcessed = 0;

        await foreach (var blob in _blobArchiver.ListAsync("hbstatz/matches/", ct))
        {
            if (!blob.EndsWith(".json", StringComparison.Ordinal)) continue;

            var matchId = ExtractMatchId(blob);
            try
            {
                var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{Escape(matchId)}'", ct);
                var match = matches.FirstOrDefault();
                if (match is null)
                {
                    errors.Add($"{blob}: no Matches row for {matchId}");
                    continue;
                }

                var json = await _blobArchiver.ReadAsync(blob, ct);
                var game = JsonSerializer.Deserialize<HbStatzGameResponse>(json);
                if (game?.Players is null)
                {
                    errors.Add($"{blob}: no players payload");
                    continue;
                }

                await TallyTeamAsync(match.HomeTeamId, match.Date, matchId, game.Players.Home, observationsByPlayer, ct);
                await TallyTeamAsync(match.AwayTeamId, match.Date, matchId, game.Players.Away, observationsByPlayer, ct);
                blobsProcessed++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Backfill failed for blob {Blob}", blob);
                errors.Add($"{blob}: {ex.Message}");
            }
        }

        var changes = new List<BackfillPositionResult>();
        foreach (var (playerId, observations) in observationsByPlayer)
        {
            var (primary, secondary) = PositionModeCalculator.Compute(
                observations.Select(o => (o.Code, o.MatchDate)).ToList());

            var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"RowKey eq '{Escape(playerId)}'", ct);
            var player = players.FirstOrDefault();
            if (player is null) continue;

            var newSecondary = secondary ?? string.Empty;
            var changed = player.Position != primary || player.PositionSecondary != newSecondary;
            if (changed)
            {
                changes.Add(new BackfillPositionResult(
                    playerId, player.Name, player.Position, primary, player.PositionSecondary, newSecondary));
            }

            if (!dryRun)
            {
                foreach (var (code, matchDate, matchId) in observations)
                {
                    await _tableWriter.UpsertAsync("PlayerPositionObservations", new PlayerPositionObservationEntity
                    {
                        PartitionKey = playerId, RowKey = matchId, Position = code, MatchDate = matchDate
                    }, ct);
                }

                if (changed)
                {
                    player.Position = primary;
                    player.PositionSecondary = newSecondary;
                    await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);
                }
            }
        }

        return new BackfillPlayerPositionsResult(dryRun, blobsProcessed, changes.Count, changes, errors);
    }

    private async Task TallyTeamAsync(
        string teamId, DateTimeOffset matchDate, string matchId, IReadOnlyList<HbStatzPlayerLine> lines,
        Dictionary<string, List<(string Code, DateTimeOffset MatchDate, string MatchId)>> tally, CancellationToken ct)
    {
        var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{Escape(teamId)}'", ct);
        foreach (var line in lines)
        {
            var playerId = HbStatzPlayerReconciler.Resolve(roster, line);
            if (playerId is null) continue;

            var code = HbStatzPositionMapper.MapToCode(line.Position);
            if (code is null) continue;

            if (!tally.TryGetValue(playerId, out var list))
            {
                list = new List<(string, DateTimeOffset, string)>();
                tally[playerId] = list;
            }
            list.Add((code, matchDate, matchId));
        }
    }

    // "hbstatz/matches/{matchId}.json"
    private static string ExtractMatchId(string blobPath)
    {
        var file = blobPath.Split('/')[^1];
        const string suffix = ".json";
        return file.EndsWith(suffix, StringComparison.Ordinal) ? file[..^suffix.Length] : file;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BackfillPlayerPositionsFunctionTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/BackfillPlayerPositionsFunction.cs Ez.Handball.Tests/Ingestion/Functions/BackfillPlayerPositionsFunctionTests.cs
git commit -m "feat(hbstatz): add one-time historical position backfill function"
```

---

### Task 7: SetPlayerPositionFunction (manual fallback)

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/SetPlayerPositionFunction.cs`
- Test: `Ez.Handball.Tests/Ingestion/Functions/SetPlayerPositionFunctionTests.cs`

**Interfaces:**
- Consumes: `PositionVocabulary.Codes` (Task 1), `ITableWriter`.
- Produces: `POST /api/players/set-position`, batch request `SetPlayerPositionRequest(string PlayerId, string Position, string? PositionSecondary)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SetPlayerPositionFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private SetPlayerPositionFunction CreateSut() => new(_tableWriter.Object);

    [Fact]
    public async Task ProcessAsync_ValidRequest_DryRun_DoesNotWrite()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Aron", Position = "" } });

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "LB", null) }, dryRun: true);

        Assert.Equal("DryRun", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players", It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ValidRequest_LiveRun_SetsPositionAndSecondary()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'hsi-1'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity> { new() { PartitionKey = "385-karlar", RowKey = "hsi-1", Name = "Aron", Position = "" } });

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "LB", "CB") }, dryRun: false);

        Assert.Equal("Applied", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "hsi-1" && e.Position == "LB" && e.PositionSecondary == "CB"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPositionCode_ReturnsInvalidWithoutWriting()
    {
        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("hsi-1", "GOALKEEPER", null) }, dryRun: false);

        Assert.Equal("InvalidPosition", Assert.Single(result.Results).Status);
        _tableWriter.Verify(t => t.QueryAsync<PlayerEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PlayerNotFound_ReturnsPlayerNotFound()
    {
        _tableWriter.Setup(t => t.QueryAsync<PlayerEntity>("Players", "RowKey eq 'nope'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEntity>());

        var result = await CreateSut().ProcessAsync(
            new[] { new SetPlayerPositionRequest("nope", "LB", null) }, dryRun: false);

        Assert.Equal("PlayerNotFound", Assert.Single(result.Results).Status);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetPlayerPositionFunctionTests"`
Expected: FAIL to compile — `SetPlayerPositionFunction` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Ez.Handball.Ingestion/Functions/SetPlayerPositionFunction.cs
using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// Manual fallback for players HBStatz can't reach: their tournament isn't HBStatz-enabled,
// reconciliation never succeeds, or they never appear in a synced match. Never conflicts with
// the automated aggregator (Backend#106), since that only writes when it has actual observations.
public record SetPlayerPositionRequest(string PlayerId, string Position, string? PositionSecondary);

public record SetPlayerPositionResult(string PlayerId, string Status, string? Detail);

public record SetPlayerPositionBatchResult(bool DryRun, IReadOnlyList<SetPlayerPositionResult> Results);

public class SetPlayerPositionFunction
{
    private readonly ITableWriter _tableWriter;

    public SetPlayerPositionFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SetPlayerPosition")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/set-position")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<SetPlayerPositionFunction>();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var requests = await JsonSerializer.DeserializeAsync<List<SetPlayerPositionRequest>>(
            req.Body, options, context.CancellationToken) ?? [];

        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);
        var result = await ProcessAsync(requests, dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<SetPlayerPositionBatchResult> ProcessAsync(
        IReadOnlyList<SetPlayerPositionRequest> requests, bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var results = new List<SetPlayerPositionResult>();

        foreach (var request in requests)
        {
            try
            {
                results.Add(await SetAsync(request, dryRun, ct));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "SetPlayerPosition failed for {PlayerId}", request.PlayerId);
                results.Add(new SetPlayerPositionResult(request.PlayerId, "Error", ex.Message));
            }
        }

        return new SetPlayerPositionBatchResult(dryRun, results);
    }

    private async Task<SetPlayerPositionResult> SetAsync(SetPlayerPositionRequest request, bool dryRun, CancellationToken ct)
    {
        if (!PositionVocabulary.Codes.Contains(request.Position))
            return new SetPlayerPositionResult(request.PlayerId, "InvalidPosition", $"'{request.Position}' is not a valid position code.");

        if (request.PositionSecondary is not null && !PositionVocabulary.Codes.Contains(request.PositionSecondary))
            return new SetPlayerPositionResult(request.PlayerId, "InvalidPositionSecondary", $"'{request.PositionSecondary}' is not a valid position code.");

        var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"RowKey eq '{Escape(request.PlayerId)}'", ct);
        var player = players.FirstOrDefault();
        if (player is null)
            return new SetPlayerPositionResult(request.PlayerId, "PlayerNotFound", null);

        var newSecondary = request.PositionSecondary ?? string.Empty;
        var detail = $"{player.Name}: {player.Position}/{player.PositionSecondary} -> {request.Position}/{newSecondary}";
        if (dryRun) return new SetPlayerPositionResult(request.PlayerId, "DryRun", detail);

        player.Position = request.Position;
        player.PositionSecondary = newSecondary;
        await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);

        return new SetPlayerPositionResult(request.PlayerId, "Applied", detail);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetPlayerPositionFunctionTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/SetPlayerPositionFunction.cs Ez.Handball.Tests/Ingestion/Functions/SetPlayerPositionFunctionTests.cs
git commit -m "feat(players): add manual position-correction fallback endpoint"
```

---

### Task 8: Cleanup — remove placeholder comments and update docs

**Files:**
- Modify: `Ez.Handball.Ingestion/Functions/SeedSquadConstraintsFunction.cs`
- Modify: `Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs`
- Modify: `docs/superpowers/specs/2026-06-07-squad-constraints-endpoint-design.md`
- Modify: `docs/superpowers/specs/2026-06-09-lineup-and-captaincy-design.md`
- Modify: `docs/superpowers/specs/2026-06-07-priced-player-pool-and-detail-enrichment-design.md`
- Modify: `CLAUDE.md`

**Interfaces:** none — documentation/comment-only changes, no code behavior change.

- [ ] **Step 1: Update the two Seed*ConstraintsFunction.cs comments**

In `Ez.Handball.Ingestion/Functions/SeedSquadConstraintsFunction.cs`, replace:

```csharp
    // Fantasy squad constraints. startingCap = a new manager's cash; maxSquadSize caps the
    // roster; posLimit:{Position} caps players per position. PLACEHOLDER position vocabulary —
    // must be reconciled with real Player.Position values (owner review). Tunable config.
```

with:

```csharp
    // Fantasy squad constraints. startingCap = a new manager's cash; maxSquadSize caps the
    // roster; posLimit:{Position} caps players per position. The GK/LW/RW/LB/CB/RB/LP vocabulary
    // is confirmed real, backed by HBStatz-observed positions (Backend#106). Tunable config.
```

In `Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs`, replace:

```csharp
    // Fantasy lineup (formation) constraints. starterCount = size of the starting 7;
    // captainMultiplier is read by scoring (#60); startMin/startMax:{Position} bound how many
    // starters may play each position (GK min=max=1 = exactly one keeper). PLACEHOLDER position
    // vocabulary — must be reconciled with real Player.Position values (owner review). Tunable.
```

with:

```csharp
    // Fantasy lineup (formation) constraints. starterCount = size of the starting 7;
    // captainMultiplier is read by scoring (#60); startMin/startMax:{Position} bound how many
    // starters may play each position (GK min=max=1 = exactly one keeper). The GK/LW/RW/LB/CB/
    // RB/LP vocabulary is confirmed real, backed by HBStatz-observed positions (Backend#106).
```

- [ ] **Step 2: Update the three spec docs**

Read each of `docs/superpowers/specs/2026-06-07-squad-constraints-endpoint-design.md`, `docs/superpowers/specs/2026-06-09-lineup-and-captaincy-design.md`, and `docs/superpowers/specs/2026-06-07-priced-player-pool-and-detail-enrichment-design.md`. Search each (`grep -n -i placeholder <file>`) for the sentence(s) noting the position vocabulary is a placeholder pending owner review. Replace each such sentence with:

> Resolved by Backend#106 — the position vocabulary (GK/LW/RW/LB/CB/RB/LP) is populated from HBStatz-observed positions, not a placeholder.

adjusting surrounding grammar minimally so the sentence reads naturally in context.

- [ ] **Step 3: Update CLAUDE.md**

Add a row to the "Table Storage schema" table:

```markdown
| PlayerPositionObservations | playerId | matchId |
```

Add a paragraph under "Backfill after schema changes" (after the existing `bootstrap-retired` paragraph):

```markdown
After deploying the HBStatz position backfill (Backend#106), run
`POST /api/players/backfill-positions` once (add `?dryRun=false` to actually
write — it defaults to a dry run) to derive `Position`/`PositionSecondary` for
every player observed in an already-archived `hbstatz/matches/*.json` blob.
It's idempotent and safe to re-run. Going forward, `POST /api/hbstatz/sync`
keeps both fields current automatically as new matches sync. Players HBStatz
never reaches can be corrected manually via `POST /api/players/set-position`.
```

- [ ] **Step 4: Verify the full solution builds and all tests pass**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, no warnings about unused usings introduced by this plan.

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS — full suite, no regressions.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/SeedSquadConstraintsFunction.cs Ez.Handball.Ingestion/Functions/SeedLineupConstraintsFunction.cs docs/superpowers/specs/2026-06-07-squad-constraints-endpoint-design.md docs/superpowers/specs/2026-06-09-lineup-and-captaincy-design.md docs/superpowers/specs/2026-06-07-priced-player-pool-and-detail-enrichment-design.md CLAUDE.md
git commit -m "docs: resolve placeholder position vocabulary references (Backend#106)"
```
