# Transfer Rules & Chips Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add free-transfer accrual/rollover, point hits, cross-round transfer attribution, and three chips (wildcard, bench boost, triple captain) to the fantasy game, on top of the existing squad/budget/gameweek/lineup system.

**Architecture:** New Config-KV groups (`fantasy-transfers-v1`, `fantasy-chips-v1`) and three new per-team(-and-round) tables (`GameFreeTransferBanks`, `GameweekTransferState`, `GameweekChipActivations`), all following the exact ETag-retry / Config-KV recipes already used by `TableGameBudgetRepository` and `TableSquadConstraintsRepository`. `BuyPlayerUseCase`/`SellPlayerUseCase` gain one new dependency (`ITransferRuleApplier`) that consumes a free transfer or records a paid one, attributed to `IGameweekSnapshotGuard`'s `CurrentGameweek` (the first still-open round) — this already lands on the correct "next round" whenever an earlier round is locked, so no separate locked-round branch is needed. `SettleGameweekUseCase` deducts the resulting hit; `GameweekScoringService` gains chip-aware scoring (triple captain multiplier, bench-boost full-bench path).

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Azure.Data.Tables, xUnit + Moq, Azurite (local table storage emulator).

**Spec:** `docs/superpowers/specs/2026-09-10-transfer-rules-and-chips-design.md`

## Global Constraints

- Fantasy-only: every new use case checks `IGameTeamRepository.ExistsAsync(userId, GameFlavor.Fantasy, ct)` first, exactly like existing use cases — manager flavor is not touched by this plan.
- All new Config-KV groups follow the exact recipe in `TableSquadConstraintsRepository`/`SeedSquadConstraintsFunction`: `PartitionKey = "{group}-v{version}"`, `RowKey` = field name (or `prefix:key` for maps), `Value` = string, hand-parsed, `null` return if required fields are missing.
- All new per-team(-and-round) repositories use the ETag-retry pattern verbatim from `TableGameBudgetRepository` (`MaxRetries = 5`, catch `RequestFailedException` status `412` to retry, status `404` to treat as missing).
- New classes are `internal sealed` in Infrastructure (matching `TableGameBudgetRepository`, `TableSquadConstraintsRepository`), `public sealed`/`public interface` in Application/Domain/Api (matching every existing use case/entity).
- Every new file's namespace matches its containing project + folder exactly (e.g. `Ez.Handball.Application.Abstractions`, `Ez.Handball.Infrastructure.TableAccess`).
- Test conventions: xUnit + Moq, one `Mock<T>` field per constructor dependency, `private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;` for any clock dependency, a private `Sut()` factory method, `[Fact]` names in `Scenario_Action_Expectation` style — matching `BuyPlayerUseCaseTests.cs` exactly. Infrastructure (Azurite) tests use `IAsyncLifetime` to create/delete their table per test class, connection string `"UseDevelopmentStorage=true"`, mirroring `TableGameBudgetRepositoryTests.cs`'s fixture shape.
- Milestone ordering matters: Milestone A (shared scaffolding) must land before B; B (free transfers/hits) must land before C (chips), because `ITransferRuleApplier` (Milestone B) depends on `IChipActivationRepository` (Milestone A) for the wildcard bypass check, and `GameweekScoringService`'s chip-aware scoring (Milestone C) is the only place `ChipType.BenchBoost`/`TripleCaptain` change behavior. Milestone B is fully working and testable (via mocks) even before any chip can actually be activated by a manager, since `IChipActivationRepository.GetActiveAsync` simply returns `null` until Milestone C's `SetChipUseCase` ships.

---

## Milestone A — Shared scaffolding

### Task 1: Domain records for transfer rules and chips

**Files:**
- Create: `Ez.Handball.Domain/FreeTransferConfig.cs`
- Create: `Ez.Handball.Domain/ChipType.cs`
- Create: `Ez.Handball.Domain/ChipConfig.cs`
- Create: `Ez.Handball.Domain/FreeTransferBank.cs`
- Create: `Ez.Handball.Domain/ChipViolation.cs`
- Test: none (pure data records with no behavior — matches how `SquadConstraints`/`LineupConstraints` have no dedicated test file either; they're exercised through their repository/use-case tests instead)

**Interfaces:**
- Produces: `FreeTransferConfig(int StartingFreeTransfers, int MaxBankedFreeTransfers, int PointHitCost, bool FirstRoundUnlimited)`, `ChipType { Wildcard, BenchBoost, TripleCaptain }`, `ChipConfig(int WildcardSeasonLimit, int BenchBoostSeasonLimit, int TripleCaptainSeasonLimit, int? SecondWildcardUnlocksAtRound)`, `FreeTransferBank(int Bank, string? LastAccruedRound)`, `ChipViolation(string Code, string Message)` — all consumed by later tasks in this milestone and Milestones B/C.

- [ ] **Step 1: Write the domain records**

```csharp
// Ez.Handball.Domain/FreeTransferConfig.cs
namespace Ez.Handball.Domain;

public sealed record FreeTransferConfig(
    int StartingFreeTransfers,
    int MaxBankedFreeTransfers,
    int PointHitCost,
    bool FirstRoundUnlimited);
```

```csharp
// Ez.Handball.Domain/ChipType.cs
namespace Ez.Handball.Domain;

public enum ChipType
{
    Wildcard,
    BenchBoost,
    TripleCaptain
}
```

```csharp
// Ez.Handball.Domain/ChipConfig.cs
namespace Ez.Handball.Domain;

public sealed record ChipConfig(
    int WildcardSeasonLimit,
    int BenchBoostSeasonLimit,
    int TripleCaptainSeasonLimit,
    int? SecondWildcardUnlocksAtRound);
```

```csharp
// Ez.Handball.Domain/FreeTransferBank.cs
namespace Ez.Handball.Domain;

// LastAccruedRound is null for a team that has never been accrued (brand new team).
public sealed record FreeTransferBank(int Bank, string? LastAccruedRound);
```

```csharp
// Ez.Handball.Domain/ChipViolation.cs
namespace Ez.Handball.Domain;

public sealed record ChipViolation(string Code, string Message);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Ez.Handball.Domain/Ez.Handball.Domain.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Domain/FreeTransferConfig.cs Ez.Handball.Domain/ChipType.cs Ez.Handball.Domain/ChipConfig.cs Ez.Handball.Domain/FreeTransferBank.cs Ez.Handball.Domain/ChipViolation.cs
git commit -m "feat(fantasy): add domain records for transfer rules and chips"
```

---

### Task 2: Extend `GameweekScore` with `RawPoints`/`PointsHit`

**Files:**
- Modify: `Ez.Handball.Domain/GameweekScore.cs`
- Test: none in this task (the new fields are exercised by Task 13's `SettleGameweekUseCaseTests` additions and Task 16's `GameweekScoringServiceTests` additions)

**Interfaces:**
- Produces: `GameweekScore` gains two new **trailing optional** parameters `double RawPoints = 0` and `int PointsHit = 0`, appended after the existing `Breakdown` parameter. Because they're optional and trailing, every existing 5-arg positional call site (`GameweekScoringService.Score`'s `return new GameweekScore(teamId, roundLabel, total, captainId, breakdown);`, and any test that constructs `GameweekScore` positionally) keeps compiling unchanged, defaulting both new fields to 0.

- [ ] **Step 1: Extend the record**

```csharp
// Ez.Handball.Domain/GameweekScore.cs
namespace Ez.Handball.Domain;

// One player's contribution to a gameweek score. Played = appeared in a member match.
// AutoSubbedIn = a bench player promoted because a starter didn't play. Multiplier is the factor
// applied to this player's raw points (2.0 for the effective captain, 3.0 with triple captain, else 1.0).
public sealed record GameweekPlayerScore(
    string PlayerId,
    double RawPoints,
    double Points,          // RawPoints * Multiplier, and 0 for a non-playing unsubbed starter
    bool Played,
    bool AutoSubbedIn,
    bool CaptainApplied,
    double Multiplier);

// A settled gameweek score for one team. RawPoints = Σ Breakdown.Points before any transfer
// point hit. PointsHit = points deducted for paid transfers that round (0 if a wildcard was
// active). Points = RawPoints - PointsHit (the number that counts toward the running total).
public sealed record GameweekScore(
    string TeamId,
    string RoundLabel,
    double Points,
    string? CaptainPlayerId,    // the effective captain (vice if the chosen captain didn't play)
    IReadOnlyList<GameweekPlayerScore> Breakdown,
    double RawPoints = 0,
    int PointsHit = 0);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded — no existing call site of `new GameweekScore(...)` breaks, since the two new parameters are trailing and optional.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Domain/GameweekScore.cs
git commit -m "feat(fantasy): add RawPoints and PointsHit to GameweekScore"
```

---

### Task 3: Shared entities + table name constants

**Files:**
- Create: `Ez.Handball.Shared/Entities/FreeTransferBankEntity.cs`
- Create: `Ez.Handball.Shared/Entities/GameweekTransferStateEntity.cs`
- Create: `Ez.Handball.Shared/Entities/GameweekChipActivationEntity.cs`
- Modify: `Ez.Handball.Infrastructure/Tables.cs`
- Test: none (plain `ITableEntity` data classes, exercised by the repository tests in Tasks 6–8)

**Interfaces:**
- Produces: three `ITableEntity` classes and three new `Tables.*` string constants, consumed by every repository task in this milestone and Milestone B.

- [ ] **Step 1: Write the entity classes**

```csharp
// Ez.Handball.Shared/Entities/FreeTransferBankEntity.cs
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// A team's running free-transfer bank. PartitionKey = teamId, RowKey = "state" (single row).
public sealed class FreeTransferBankEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // "state"
    public int Bank { get; set; }
    public string? LastAccruedRound { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

```csharp
// Ez.Handball.Shared/Entities/GameweekTransferStateEntity.cs
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// How many paid (beyond-the-free-allowance) transfers a team made in one round.
// PartitionKey = teamId, RowKey = roundLabel (mirrors GameweekLineups / GameweekScores).
public sealed class GameweekTransferStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // roundLabel
    public int PaidTransfersCount { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

```csharp
// Ez.Handball.Shared/Entities/GameweekChipActivationEntity.cs
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Row exists only when a chip is active for that team/round. PartitionKey = teamId,
// RowKey = roundLabel. ChipType is the string name of the Ez.Handball.Domain.ChipType enum value.
public sealed class GameweekChipActivationEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // roundLabel
    public string ChipType { get; set; } = string.Empty;
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

- [ ] **Step 2: Add the table name constants**

In `Ez.Handball.Infrastructure/Tables.cs`, add three new constants alongside the existing gameweek ones:

```csharp
    public const string GameweekLocks = "GameweekLocks";
    public const string GameweekLineups = "GameweekLineups";
    public const string GameweekScores = "GameweekScores";
    public const string GameFreeTransferBanks = "GameFreeTransferBanks";
    public const string GameweekTransferState = "GameweekTransferState";
    public const string GameweekChipActivations = "GameweekChipActivations";
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Shared/Entities/FreeTransferBankEntity.cs Ez.Handball.Shared/Entities/GameweekTransferStateEntity.cs Ez.Handball.Shared/Entities/GameweekChipActivationEntity.cs Ez.Handball.Infrastructure/Tables.cs
git commit -m "feat(fantasy): add table entities and table names for transfer rules and chips"
```

---

### Task 4: `IFreeTransferConfigRepository` + seed function

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IFreeTransferConfigRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableFreeTransferConfigRepository.cs`
- Create: `Ez.Handball.Ingestion/Functions/SeedFreeTransferConfigFunction.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: Create `Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferConfigRepositoryTests.cs`

**Interfaces:**
- Consumes: `ITableQuery.QueryAsync<T>(string tableName, string? filter, CancellationToken ct)`, `ConfigEntity` (`PartitionKey`, `RowKey`, `Value`), `Tables.Config`.
- Produces: `IFreeTransferConfigRepository.GetAsync(int version, CancellationToken ct) -> Task<FreeTransferConfig?>`, consumed by `FreeTransferAccrualService` (Task 9), `TransferRuleApplier` (Task 10), `SettleGameweekUseCase` (Task 13), `GetTransferStatusUseCase` (Task 14).

- [ ] **Step 1: Write the failing repository test**

```csharp
// Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferConfigRepositoryTests.cs
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public sealed class TableFreeTransferConfigRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.Config).DeleteAsync();
        await _client.GetTableClient(Tables.Config).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _client.GetTableClient(Tables.Config).DeleteAsync();

    private TableFreeTransferConfigRepository Sut() => new(new TableQuery(_client));

    [Fact]
    public async Task GetAsync_NoRows_ReturnsNull()
    {
        var result = await Sut().GetAsync(1, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_AllFieldsPresent_ReturnsConfig()
    {
        var table = _client.GetTableClient(Tables.Config);
        foreach (var (key, value) in new[]
        {
            ("startingFreeTransfers", "1"), ("maxBankedFreeTransfers", "5"),
            ("pointHitCost", "4"), ("firstRoundUnlimited", "true")
        })
        {
            await table.UpsertEntityAsync(new ConfigEntity
            { PartitionKey = "fantasy-transfers-v1", RowKey = key, Value = value }, TableUpdateMode.Replace);
        }

        var result = await Sut().GetAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.StartingFreeTransfers);
        Assert.Equal(5, result.MaxBankedFreeTransfers);
        Assert.Equal(4, result.PointHitCost);
        Assert.True(result.FirstRoundUnlimited);
    }

    [Fact]
    public async Task GetAsync_MissingRequiredField_ReturnsNull()
    {
        var table = _client.GetTableClient(Tables.Config);
        await table.UpsertEntityAsync(new ConfigEntity
        { PartitionKey = "fantasy-transfers-v1", RowKey = "startingFreeTransfers", Value = "1" }, TableUpdateMode.Replace);

        var result = await Sut().GetAsync(1, CancellationToken.None);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferConfigRepositoryTests"`
Expected: FAIL to compile — `IFreeTransferConfigRepository`/`TableFreeTransferConfigRepository` don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Abstractions/IFreeTransferConfigRepository.cs
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IFreeTransferConfigRepository
{
    Task<FreeTransferConfig?> GetAsync(int version, CancellationToken ct);
}
```

```csharp
// Ez.Handball.Infrastructure/TableAccess/TableFreeTransferConfigRepository.cs
using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableFreeTransferConfigRepository : IFreeTransferConfigRepository
{
    private readonly ITableQuery _query;

    public TableFreeTransferConfigRepository(ITableQuery query) => _query = query;

    public async Task<FreeTransferConfig?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-transfers-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "startingFreeTransfers", out var starting) ||
            !TryGetInt(values, "maxBankedFreeTransfers", out var maxBanked) ||
            !TryGetInt(values, "pointHitCost", out var hitCost) ||
            !values.TryGetValue("firstRoundUnlimited", out var firstRoundRaw) ||
            !bool.TryParse(firstRoundRaw, out var firstRoundUnlimited))
            return null;

        return new FreeTransferConfig(starting, maxBanked, hitCost, firstRoundUnlimited);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferConfigRepositoryTests"`
Expected: PASS (3 tests) — requires `azurite --silent --location /tmp/azurite-test &` running first.

- [ ] **Step 5: Register in DI and add the seed function**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, add one line inside `AddTableStorageInfrastructure` next to the other Config-KV repositories:

```csharp
        services.AddScoped<ISquadConstraintsRepository, TableSquadConstraintsRepository>();
        services.AddScoped<IFreeTransferConfigRepository, TableFreeTransferConfigRepository>();
```

```csharp
// Ez.Handball.Ingestion/Functions/SeedFreeTransferConfigFunction.cs
using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedFreeTransferConfigFunction
{
    // Fantasy free-transfer rules. FPL-style: 1 free transfer accrued per round crossed,
    // banked up to maxBankedFreeTransfers, -4 points per transfer beyond the bank. Round 1
    // (before the first deadline) is unlimited. Tunable config.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConfigDefinitions =
    [
        ("fantasy-transfers-v1", "startingFreeTransfers", "1"),
        ("fantasy-transfers-v1", "maxBankedFreeTransfers", "5"),
        ("fantasy-transfers-v1", "pointHitCost", "4"),
        ("fantasy-transfers-v1", "firstRoundUnlimited", "true"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedFreeTransferConfigFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedFreeTransferConfig")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/free-transfer-config")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in ConfigDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace);
        }

        return ConfigDefinitions.Count;
    }
}
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferConfigRepositoryTests"`
Expected: Build succeeded, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IFreeTransferConfigRepository.cs Ez.Handball.Infrastructure/TableAccess/TableFreeTransferConfigRepository.cs Ez.Handball.Ingestion/Functions/SeedFreeTransferConfigFunction.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferConfigRepositoryTests.cs
git commit -m "feat(fantasy): add free-transfer Config-KV repository and seed function"
```

---

### Task 5: `IChipConfigRepository` + seed function

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IChipConfigRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableChipConfigRepository.cs`
- Create: `Ez.Handball.Ingestion/Functions/SeedChipConfigFunction.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: Create `Ez.Handball.Tests/Infrastructure/Tables/TableChipConfigRepositoryTests.cs`

**Interfaces:**
- Consumes: same `ITableQuery`/`ConfigEntity`/`Tables.Config` as Task 4.
- Produces: `IChipConfigRepository.GetAsync(int version, CancellationToken ct) -> Task<ChipConfig?>`, consumed by `SetChipUseCase`/`GetChipUseCase` (Task 15).

- [ ] **Step 1: Write the failing repository test**

```csharp
// Ez.Handball.Tests/Infrastructure/Tables/TableChipConfigRepositoryTests.cs
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public sealed class TableChipConfigRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.Config).DeleteAsync();
        await _client.GetTableClient(Tables.Config).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _client.GetTableClient(Tables.Config).DeleteAsync();

    private TableChipConfigRepository Sut() => new(new TableQuery(_client));

    [Fact]
    public async Task GetAsync_NoRows_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AllFieldsPresent_ReturnsConfig()
    {
        var table = _client.GetTableClient(Tables.Config);
        foreach (var (key, value) in new[]
        {
            ("wildcardSeasonLimit", "2"), ("benchBoostSeasonLimit", "1"),
            ("tripleCaptainSeasonLimit", "1"), ("secondWildcardUnlocksAtRound", "12")
        })
        {
            await table.UpsertEntityAsync(new ConfigEntity
            { PartitionKey = "fantasy-chips-v1", RowKey = key, Value = value }, TableUpdateMode.Replace);
        }

        var result = await Sut().GetAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.WildcardSeasonLimit);
        Assert.Equal(1, result.BenchBoostSeasonLimit);
        Assert.Equal(1, result.TripleCaptainSeasonLimit);
        Assert.Equal(12, result.SecondWildcardUnlocksAtRound);
    }

    [Fact]
    public async Task GetAsync_NoBoundaryRow_ReturnsNullBoundary()
    {
        var table = _client.GetTableClient(Tables.Config);
        foreach (var (key, value) in new[]
        {
            ("wildcardSeasonLimit", "2"), ("benchBoostSeasonLimit", "1"), ("tripleCaptainSeasonLimit", "1")
        })
        {
            await table.UpsertEntityAsync(new ConfigEntity
            { PartitionKey = "fantasy-chips-v1", RowKey = key, Value = value }, TableUpdateMode.Replace);
        }

        var result = await Sut().GetAsync(1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.SecondWildcardUnlocksAtRound);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipConfigRepositoryTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Abstractions/IChipConfigRepository.cs
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IChipConfigRepository
{
    Task<ChipConfig?> GetAsync(int version, CancellationToken ct);
}
```

```csharp
// Ez.Handball.Infrastructure/TableAccess/TableChipConfigRepository.cs
using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableChipConfigRepository : IChipConfigRepository
{
    private readonly ITableQuery _query;

    public TableChipConfigRepository(ITableQuery query) => _query = query;

    public async Task<ChipConfig?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-chips-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "wildcardSeasonLimit", out var wildcard) ||
            !TryGetInt(values, "benchBoostSeasonLimit", out var benchBoost) ||
            !TryGetInt(values, "tripleCaptainSeasonLimit", out var tripleCaptain))
            return null;

        int? boundary = TryGetInt(values, "secondWildcardUnlocksAtRound", out var b) ? b : null;

        return new ChipConfig(wildcard, benchBoost, tripleCaptain, boundary);
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int result)
    {
        result = 0;
        return values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipConfigRepositoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Register in DI and add the seed function**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`:

```csharp
        services.AddScoped<IFreeTransferConfigRepository, TableFreeTransferConfigRepository>();
        services.AddScoped<IChipConfigRepository, TableChipConfigRepository>();
```

```csharp
// Ez.Handball.Ingestion/Functions/SeedChipConfigFunction.cs
using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedChipConfigFunction
{
    // Fantasy chip rules. Wildcard is split 1-per-half via secondWildcardUnlocksAtRound (a
    // round-number cutoff owners set once the season schedule is known — left unset here
    // since it depends on the current season's fixture list); bench boost and triple captain
    // are once each per season. Tunable config.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConfigDefinitions =
    [
        ("fantasy-chips-v1", "wildcardSeasonLimit", "2"),
        ("fantasy-chips-v1", "benchBoostSeasonLimit", "1"),
        ("fantasy-chips-v1", "tripleCaptainSeasonLimit", "1"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedChipConfigFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedChipConfig")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/chip-config")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in ConfigDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace);
        }

        return ConfigDefinitions.Count;
    }
}
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipConfigRepositoryTests"`
Expected: Build succeeded, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IChipConfigRepository.cs Ez.Handball.Infrastructure/TableAccess/TableChipConfigRepository.cs Ez.Handball.Ingestion/Functions/SeedChipConfigFunction.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Infrastructure/Tables/TableChipConfigRepositoryTests.cs
git commit -m "feat(fantasy): add chip Config-KV repository and seed function"
```

---

### Task 6: `IChipActivationRepository`

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IChipActivationRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableChipActivationRepository.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: Create `Ez.Handball.Tests/Infrastructure/Tables/TableChipActivationRepositoryTests.cs`

**Interfaces:**
- Produces: `IChipActivationRepository { Task<ChipType?> GetActiveAsync(string teamId, string roundLabel, CancellationToken ct); Task SetAsync(string teamId, string roundLabel, ChipType? chip, CancellationToken ct); Task<IReadOnlyList<(string Round, ChipType Type)>> ListUsedAsync(string teamId, CancellationToken ct); }` — consumed by `TransferRuleApplier` (Task 10, wildcard bypass), `SettleGameweekUseCase` (Task 13, wildcard hit-bypass and Task 16, scoring effects), `SetChipUseCase`/`GetChipUseCase` (Task 15).

- [ ] **Step 1: Write the failing repository test**

```csharp
// Ez.Handball.Tests/Infrastructure/Tables/TableChipActivationRepositoryTests.cs
using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public sealed class TableChipActivationRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.GameweekChipActivations).DeleteAsync();
        await _client.GetTableClient(Tables.GameweekChipActivations).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameweekChipActivations).DeleteAsync();

    private TableChipActivationRepository Sut() => new(_client);

    [Fact]
    public async Task GetActiveAsync_NoRow_ReturnsNull()
    {
        Assert.Null(await Sut().GetActiveAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_ThenGetActiveAsync_ReturnsTheChip()
    {
        var sut = Sut();
        await sut.SetAsync("team1", "5", ChipType.BenchBoost, CancellationToken.None);

        Assert.Equal(ChipType.BenchBoost, await sut.GetActiveAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_Null_ClearsTheRow()
    {
        var sut = Sut();
        await sut.SetAsync("team1", "5", ChipType.Wildcard, CancellationToken.None);
        await sut.SetAsync("team1", "5", null, CancellationToken.None);

        Assert.Null(await sut.GetActiveAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task ListUsedAsync_ReturnsAllRoundsForTeam()
    {
        var sut = Sut();
        await sut.SetAsync("team1", "3", ChipType.Wildcard, CancellationToken.None);
        await sut.SetAsync("team1", "9", ChipType.TripleCaptain, CancellationToken.None);
        await sut.SetAsync("team2", "3", ChipType.BenchBoost, CancellationToken.None);

        var used = await sut.ListUsedAsync("team1", CancellationToken.None);

        Assert.Equal(2, used.Count);
        Assert.Contains(used, u => u.Round == "3" && u.Type == ChipType.Wildcard);
        Assert.Contains(used, u => u.Round == "9" && u.Type == ChipType.TripleCaptain);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipActivationRepositoryTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Abstractions/IChipActivationRepository.cs
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IChipActivationRepository
{
    Task<ChipType?> GetActiveAsync(string teamId, string roundLabel, CancellationToken ct);

    // null clears the activation for that round (deletes the row).
    Task SetAsync(string teamId, string roundLabel, ChipType? chip, CancellationToken ct);

    // All (round, chip) activations for this team, across every round. Used for season-limit
    // counting — a partition scan (PartitionKey == teamId), not bounded by round.
    Task<IReadOnlyList<(string Round, ChipType Type)>> ListUsedAsync(string teamId, CancellationToken ct);
}
```

```csharp
// Ez.Handball.Infrastructure/TableAccess/TableChipActivationRepository.cs
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableChipActivationRepository : IChipActivationRepository
{
    private readonly TableServiceClient _client;

    public TableChipActivationRepository(TableServiceClient client) => _client = client;

    public async Task<ChipType?> GetActiveAsync(string teamId, string roundLabel, CancellationToken ct)
    {
        try
        {
            var e = (await _client.GetTableClient(Tables.GameweekChipActivations)
                .GetEntityAsync<GameweekChipActivationEntity>(teamId, roundLabel, cancellationToken: ct)).Value;
            return Enum.Parse<ChipType>(e.ChipType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SetAsync(string teamId, string roundLabel, ChipType? chip, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekChipActivations);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        if (chip is null)
        {
            try
            {
                await table.DeleteEntityAsync(teamId, roundLabel, cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // already absent
            }
            return;
        }

        await table.UpsertEntityAsync(new GameweekChipActivationEntity
        {
            PartitionKey = teamId, RowKey = roundLabel, ChipType = chip.Value.ToString()
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<(string Round, ChipType Type)>> ListUsedAsync(string teamId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekChipActivations);
        var filter = $"PartitionKey eq '{ODataFilter.Escape(teamId)}'";

        var result = new List<(string, ChipType)>();
        await foreach (var e in table.QueryAsync<GameweekChipActivationEntity>(filter, cancellationToken: ct))
            result.Add((e.RowKey, Enum.Parse<ChipType>(e.ChipType)));

        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipActivationRepositoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Register in DI**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`:

```csharp
        services.AddScoped<IChipConfigRepository, TableChipConfigRepository>();
        services.AddScoped<IChipActivationRepository, TableChipActivationRepository>();
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableChipActivationRepositoryTests"`
Expected: Build succeeded, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IChipActivationRepository.cs Ez.Handball.Infrastructure/TableAccess/TableChipActivationRepository.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Infrastructure/Tables/TableChipActivationRepositoryTests.cs
git commit -m "feat(fantasy): add chip activation repository"
```

---

## Milestone B — Free transfers, point hits, cross-round attribution

### Task 7: `IFreeTransferBankRepository`

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IFreeTransferBankRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableFreeTransferBankRepository.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: Create `Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferBankRepositoryTests.cs`

**Interfaces:**
- Produces: `IFreeTransferBankRepository { Task<FreeTransferBank?> GetAsync(string teamId, CancellationToken ct); Task<FreeTransferBank> AccrueAsync(string teamId, string newWatermark, int delta, int cap, CancellationToken ct); Task<bool> TryConsumeAsync(string teamId, CancellationToken ct); }` — consumed by `FreeTransferAccrualService` (Task 9), `TransferRuleApplier` (Task 10), `GetTransferStatusUseCase` (Task 14).

- [ ] **Step 1: Write the failing repository test**

```csharp
// Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferBankRepositoryTests.cs
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public sealed class TableFreeTransferBankRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.GameFreeTransferBanks).DeleteAsync();
        await _client.GetTableClient(Tables.GameFreeTransferBanks).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameFreeTransferBanks).DeleteAsync();

    private TableFreeTransferBankRepository Sut() => new(_client);

    [Fact]
    public async Task GetAsync_NoRow_ReturnsNull()
    {
        Assert.Null(await Sut().GetAsync("team1", CancellationToken.None));
    }

    [Fact]
    public async Task AccrueAsync_NoExistingRow_CreatesWithDeltaCappedAtCap()
    {
        var result = await Sut().AccrueAsync("team1", "3", delta: 10, cap: 5, CancellationToken.None);

        Assert.Equal(5, result.Bank);
        Assert.Equal("3", result.LastAccruedRound);
    }

    [Fact]
    public async Task AccrueAsync_ExistingRow_AddsDeltaCappedAtCap()
    {
        var sut = Sut();
        await sut.AccrueAsync("team1", "3", delta: 2, cap: 5, CancellationToken.None);

        var result = await sut.AccrueAsync("team1", "4", delta: 10, cap: 5, CancellationToken.None);

        Assert.Equal(5, result.Bank);
        Assert.Equal("4", result.LastAccruedRound);
    }

    [Fact]
    public async Task AccrueAsync_SameWatermark_IsNoOp()
    {
        var sut = Sut();
        await sut.AccrueAsync("team1", "3", delta: 2, cap: 5, CancellationToken.None);

        var result = await sut.AccrueAsync("team1", "3", delta: 2, cap: 5, CancellationToken.None);

        Assert.Equal(2, result.Bank); // unchanged — already caught up to round 3
        Assert.Equal("3", result.LastAccruedRound);
    }

    [Fact]
    public async Task TryConsumeAsync_BankPositive_DecrementsAndReturnsTrue()
    {
        var sut = Sut();
        await sut.AccrueAsync("team1", "3", delta: 2, cap: 5, CancellationToken.None);

        var consumed = await sut.TryConsumeAsync("team1", CancellationToken.None);
        var after = await sut.GetAsync("team1", CancellationToken.None);

        Assert.True(consumed);
        Assert.Equal(1, after!.Bank);
    }

    [Fact]
    public async Task TryConsumeAsync_BankZero_ReturnsFalse()
    {
        Assert.False(await Sut().TryConsumeAsync("team-with-no-bank", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferBankRepositoryTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Abstractions/IFreeTransferBankRepository.cs
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IFreeTransferBankRepository
{
    Task<FreeTransferBank?> GetAsync(string teamId, CancellationToken ct);

    // Atomically: if the stored LastAccruedRound already equals newWatermark, no-op (idempotent
    // re-entry) and return the unchanged state. Otherwise add `delta` to Bank (capped at `cap`,
    // never negative) and set LastAccruedRound = newWatermark. Creates the row if missing,
    // with Bank = min(delta, cap). ETag-retry.
    Task<FreeTransferBank> AccrueAsync(string teamId, string newWatermark, int delta, int cap, CancellationToken ct);

    // Atomically decrement Bank by 1 if > 0. Returns false (no change) if Bank was already 0,
    // or the row doesn't exist. ETag-retry.
    Task<bool> TryConsumeAsync(string teamId, CancellationToken ct);
}
```

```csharp
// Ez.Handball.Infrastructure/TableAccess/TableFreeTransferBankRepository.cs
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableFreeTransferBankRepository : IFreeTransferBankRepository
{
    private const string StateRow = "state";
    private const int MaxRetries = 5;

    private readonly TableServiceClient _client;

    public TableFreeTransferBankRepository(TableServiceClient client) => _client = client;

    public async Task<FreeTransferBank?> GetAsync(string teamId, CancellationToken ct)
    {
        try
        {
            var e = (await _client.GetTableClient(Tables.GameFreeTransferBanks)
                .GetEntityAsync<FreeTransferBankEntity>(teamId, StateRow, cancellationToken: ct)).Value;
            return new FreeTransferBank(e.Bank, e.LastAccruedRound);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<FreeTransferBank> AccrueAsync(
        string teamId, string newWatermark, int delta, int cap, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameFreeTransferBanks);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var existing = await table.GetEntityIfExistsAsync<FreeTransferBankEntity>(teamId, StateRow, cancellationToken: ct);
                if (!existing.HasValue)
                {
                    var created = new FreeTransferBankEntity
                    {
                        PartitionKey = teamId, RowKey = StateRow,
                        Bank = Math.Min(delta, cap), LastAccruedRound = newWatermark
                    };
                    await table.AddEntityAsync(created, ct);
                    return new FreeTransferBank(created.Bank, created.LastAccruedRound);
                }

                var e = existing.Value!;
                if (e.LastAccruedRound == newWatermark)
                    return new FreeTransferBank(e.Bank, e.LastAccruedRound); // already caught up

                e.Bank = Math.Min(e.Bank + delta, cap);
                e.LastAccruedRound = newWatermark;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return new FreeTransferBank(e.Bank, e.LastAccruedRound);
            }
            catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
            {
                // lost the optimistic-concurrency race (or a concurrent create) — retry
            }
        }

        throw new InvalidOperationException($"Failed to accrue free-transfer bank for team {teamId} after {MaxRetries} attempts.");
    }

    public async Task<bool> TryConsumeAsync(string teamId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameFreeTransferBanks);
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var e = (await table.GetEntityAsync<FreeTransferBankEntity>(teamId, StateRow, cancellationToken: ct)).Value;
                if (e.Bank <= 0) return false;

                e.Bank -= 1;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // lost the optimistic-concurrency race — re-read and retry
            }
        }
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferBankRepositoryTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Register in DI**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`:

```csharp
        services.AddScoped<IChipActivationRepository, TableChipActivationRepository>();
        services.AddScoped<IFreeTransferBankRepository, TableFreeTransferBankRepository>();
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableFreeTransferBankRepositoryTests"`
Expected: Build succeeded, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IFreeTransferBankRepository.cs Ez.Handball.Infrastructure/TableAccess/TableFreeTransferBankRepository.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Infrastructure/Tables/TableFreeTransferBankRepositoryTests.cs
git commit -m "feat(fantasy): add free-transfer bank repository"
```

---

### Task 8: `IGameweekTransferStateRepository`

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IGameweekTransferStateRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableGameweekTransferStateRepository.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: Create `Ez.Handball.Tests/Infrastructure/Tables/TableGameweekTransferStateRepositoryTests.cs`

**Interfaces:**
- Produces: `IGameweekTransferStateRepository { Task<int> GetPaidCountAsync(string teamId, string roundLabel, CancellationToken ct); Task IncrementPaidAsync(string teamId, string roundLabel, CancellationToken ct); }` — consumed by `TransferRuleApplier` (Task 10), `SettleGameweekUseCase` (Task 13), `GetTransferStatusUseCase` (Task 14).

- [ ] **Step 1: Write the failing repository test**

```csharp
// Ez.Handball.Tests/Infrastructure/Tables/TableGameweekTransferStateRepositoryTests.cs
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public sealed class TableGameweekTransferStateRepositoryTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _client = new(ConnectionString);

    public async Task InitializeAsync()
    {
        await _client.GetTableClient(Tables.GameweekTransferState).DeleteAsync();
        await _client.GetTableClient(Tables.GameweekTransferState).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameweekTransferState).DeleteAsync();

    private TableGameweekTransferStateRepository Sut() => new(_client);

    [Fact]
    public async Task GetPaidCountAsync_NoRow_ReturnsZero()
    {
        Assert.Equal(0, await Sut().GetPaidCountAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task IncrementPaidAsync_NoExistingRow_CreatesWithCountOne()
    {
        var sut = Sut();
        await sut.IncrementPaidAsync("team1", "5", CancellationToken.None);

        Assert.Equal(1, await sut.GetPaidCountAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task IncrementPaidAsync_CalledTwice_Increments()
    {
        var sut = Sut();
        await sut.IncrementPaidAsync("team1", "5", CancellationToken.None);
        await sut.IncrementPaidAsync("team1", "5", CancellationToken.None);

        Assert.Equal(2, await sut.GetPaidCountAsync("team1", "5", CancellationToken.None));
    }

    [Fact]
    public async Task IncrementPaidAsync_DifferentRounds_AreIndependent()
    {
        var sut = Sut();
        await sut.IncrementPaidAsync("team1", "5", CancellationToken.None);
        await sut.IncrementPaidAsync("team1", "6", CancellationToken.None);

        Assert.Equal(1, await sut.GetPaidCountAsync("team1", "5", CancellationToken.None));
        Assert.Equal(1, await sut.GetPaidCountAsync("team1", "6", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableGameweekTransferStateRepositoryTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Abstractions/IGameweekTransferStateRepository.cs
namespace Ez.Handball.Application.Abstractions;

public interface IGameweekTransferStateRepository
{
    Task<int> GetPaidCountAsync(string teamId, string roundLabel, CancellationToken ct);

    // Atomically +1. Creates the row (count = 1) if missing. ETag-retry.
    Task IncrementPaidAsync(string teamId, string roundLabel, CancellationToken ct);
}
```

```csharp
// Ez.Handball.Infrastructure/TableAccess/TableGameweekTransferStateRepository.cs
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekTransferStateRepository : IGameweekTransferStateRepository
{
    private const int MaxRetries = 5;

    private readonly TableServiceClient _client;

    public TableGameweekTransferStateRepository(TableServiceClient client) => _client = client;

    public async Task<int> GetPaidCountAsync(string teamId, string roundLabel, CancellationToken ct)
    {
        try
        {
            var e = (await _client.GetTableClient(Tables.GameweekTransferState)
                .GetEntityAsync<GameweekTransferStateEntity>(teamId, roundLabel, cancellationToken: ct)).Value;
            return e.PaidTransfersCount;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return 0;
        }
    }

    public async Task IncrementPaidAsync(string teamId, string roundLabel, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekTransferState);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var existing = await table.GetEntityIfExistsAsync<GameweekTransferStateEntity>(teamId, roundLabel, cancellationToken: ct);
                if (!existing.HasValue)
                {
                    await table.AddEntityAsync(new GameweekTransferStateEntity
                    {
                        PartitionKey = teamId, RowKey = roundLabel, PaidTransfersCount = 1
                    }, ct);
                    return;
                }

                var e = existing.Value!;
                e.PaidTransfersCount += 1;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
            {
                // lost the optimistic-concurrency race (or a concurrent create) — retry
            }
        }

        throw new InvalidOperationException(
            $"Failed to increment paid transfer count for team {teamId}, round {roundLabel} after {MaxRetries} attempts.");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableGameweekTransferStateRepositoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Register in DI**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`:

```csharp
        services.AddScoped<IFreeTransferBankRepository, TableFreeTransferBankRepository>();
        services.AddScoped<IGameweekTransferStateRepository, TableGameweekTransferStateRepository>();
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableGameweekTransferStateRepositoryTests"`
Expected: Build succeeded, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IGameweekTransferStateRepository.cs Ez.Handball.Infrastructure/TableAccess/TableGameweekTransferStateRepository.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Tests/Infrastructure/Tables/TableGameweekTransferStateRepositoryTests.cs
git commit -m "feat(fantasy): add per-round paid-transfer count repository"
```

---

### Task 9: `IFreeTransferAccrualService`

**Files:**
- Create: `Ez.Handball.Application/Services/IFreeTransferAccrualService.cs`
- Test: Create `Ez.Handball.Tests/Application/Services/FreeTransferAccrualServiceTests.cs`

**Interfaces:**
- Consumes: `IGameweekConfigRepository.GetAsync(int version, CancellationToken ct)`, `IGameweekCalendarService.GetCalendarAsync(GameweekConfig config, CancellationToken ct)`, `IFreeTransferConfigRepository.GetAsync(int version, CancellationToken ct)`, `IFreeTransferBankRepository.GetAsync`/`AccrueAsync`, `RoundOrder.Compare(string a, string b)`.
- Produces: `IFreeTransferAccrualService { Task AccrueAsync(string teamId, string throughRound, CancellationToken ct); }` — consumed by `TransferRuleApplier` (Task 10).

- [ ] **Step 1: Write the failing test**

```csharp
// Ez.Handball.Tests/Application/Services/FreeTransferAccrualServiceTests.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public sealed class FreeTransferAccrualServiceTests
{
    private readonly Mock<IGameweekConfigRepository> _gwConfig = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IFreeTransferConfigRepository> _transferConfig = new();
    private readonly Mock<IFreeTransferBankRepository> _bank = new();

    private static readonly GameweekConfig AnyGwConfig = new("t1", 1, 1, 1, 1);
    private static readonly FreeTransferConfig Cfg = new(
        StartingFreeTransfers: 1, MaxBankedFreeTransfers: 5, PointHitCost: 4, FirstRoundUnlimited: true);

    public FreeTransferAccrualServiceTests()
    {
        _gwConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(AnyGwConfig);
        _transferConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cfg);
    }

    private FreeTransferAccrualService Sut() => new(_gwConfig.Object, _calendar.Object, _transferConfig.Object, _bank.Object);

    private static Gameweek Gw(string round) => new(0, round, "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []);

    [Fact]
    public async Task AccrueAsync_NeverAccruedBefore_DeltaIsRoundsUpToAndIncludingThroughRound()
    {
        _calendar.Setup(x => x.GetCalendarAsync(AnyGwConfig, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Gw("1"), Gw("2"), Gw("3") });
        _bank.Setup(x => x.GetAsync("team1", It.IsAny<CancellationToken>())).ReturnsAsync((FreeTransferBank?)null);

        await Sut().AccrueAsync("team1", "2", CancellationToken.None);

        _bank.Verify(x => x.AccrueAsync("team1", "2", 2, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AccrueAsync_AlreadyAccruedPastThroughRound_DeltaIsZero()
    {
        _calendar.Setup(x => x.GetCalendarAsync(AnyGwConfig, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Gw("1"), Gw("2"), Gw("3") });
        _bank.Setup(x => x.GetAsync("team1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeTransferBank(3, "2"));

        await Sut().AccrueAsync("team1", "2", CancellationToken.None);

        _bank.Verify(x => x.AccrueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccrueAsync_PartiallyAccrued_DeltaIsOnlyTheRoundsCrossedSince()
    {
        _calendar.Setup(x => x.GetCalendarAsync(AnyGwConfig, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Gw("1"), Gw("2"), Gw("3"), Gw("4") });
        _bank.Setup(x => x.GetAsync("team1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FreeTransferBank(1, "1"));

        await Sut().AccrueAsync("team1", "4", CancellationToken.None);

        _bank.Verify(x => x.AccrueAsync("team1", "4", 3, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AccrueAsync_GameweekConfigMissing_IsNoOp()
    {
        _gwConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        await Sut().AccrueAsync("team1", "2", CancellationToken.None);

        _bank.Verify(x => x.AccrueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Note: `GameweekConfig`'s exact constructor parameters (`TournamentId, LockOffsetHours, MatchFinalBufferHours, ScoringRuleSetVersion, LineupConstraintsVersion` — 5 args) must be confirmed against `Ez.Handball.Domain/GameweekConfig.cs` before writing this test; adjust the `AnyGwConfig` construction to match the real record shape if it differs from the 5-int-like-args placeholder above.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FreeTransferAccrualServiceTests"`
Expected: FAIL to compile — `IFreeTransferAccrualService`/`FreeTransferAccrualService` don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Services/IFreeTransferAccrualService.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IFreeTransferAccrualService
{
    // Accrues the team's free-transfer bank up through `throughRound` (walking the calendar
    // from the team's last-accrued round), capped at MaxBankedFreeTransfers. No-op if the team
    // is already caught up, or if gameweek/transfer config is missing.
    Task AccrueAsync(string teamId, string throughRound, CancellationToken ct);
}

public sealed class FreeTransferAccrualService : IFreeTransferAccrualService
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _gwConfig;
    private readonly IGameweekCalendarService _calendar;
    private readonly IFreeTransferConfigRepository _transferConfig;
    private readonly IFreeTransferBankRepository _bank;

    public FreeTransferAccrualService(
        IGameweekConfigRepository gwConfig, IGameweekCalendarService calendar,
        IFreeTransferConfigRepository transferConfig, IFreeTransferBankRepository bank)
    {
        _gwConfig = gwConfig;
        _calendar = calendar;
        _transferConfig = transferConfig;
        _bank = bank;
    }

    public async Task AccrueAsync(string teamId, string throughRound, CancellationToken ct)
    {
        var gwConfig = await _gwConfig.GetAsync(DefaultVersion, ct);
        if (gwConfig is null) return;

        var calendar = await _calendar.GetCalendarAsync(gwConfig, ct);
        if (calendar is null) return;

        var cfg = await _transferConfig.GetAsync(DefaultVersion, ct);
        if (cfg is null) return;

        var existing = await _bank.GetAsync(teamId, ct);
        var lastAccrued = existing?.LastAccruedRound;

        if (lastAccrued is not null && RoundOrder.Compare(lastAccrued, throughRound) >= 0)
            return; // already caught up

        var crossed = calendar.Count(g =>
            (lastAccrued is null || RoundOrder.Compare(g.RoundLabel, lastAccrued) > 0)
            && RoundOrder.Compare(g.RoundLabel, throughRound) <= 0);

        if (crossed == 0) return;

        var delta = crossed * cfg.StartingFreeTransfers;
        await _bank.AccrueAsync(teamId, throughRound, delta, cfg.MaxBankedFreeTransfers, ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FreeTransferAccrualServiceTests"`
Expected: PASS (4 tests). If `GameweekConfig`'s real constructor shape differs from the placeholder used in `AnyGwConfig`, fix the test's construction first (read `Ez.Handball.Domain/GameweekConfig.cs`), then re-run.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/IFreeTransferAccrualService.cs Ez.Handball.Tests/Application/Services/FreeTransferAccrualServiceTests.cs
git commit -m "feat(fantasy): add free-transfer bank accrual service"
```

---

### Task 10: `ITransferRuleApplier`

**Files:**
- Create: `Ez.Handball.Application/Services/ITransferRuleApplier.cs`
- Test: Create `Ez.Handball.Tests/Application/Services/TransferRuleApplierTests.cs`

**Interfaces:**
- Consumes: `IChipActivationRepository.GetActiveAsync`, `IFreeTransferConfigRepository.GetAsync`, `IFreeTransferAccrualService.AccrueAsync`, `IFreeTransferBankRepository.TryConsumeAsync`, `IGameweekTransferStateRepository.IncrementPaidAsync`, `SnapshotGuardResult` (`CurrentGameweek`, `CurrentGameweekLocked`).
- Produces: `ITransferRuleApplier { Task ApplyAsync(string teamId, SnapshotGuardResult guardResult, CancellationToken ct); }` — consumed by `BuyPlayerUseCase` (Task 11) and `SellPlayerUseCase` (Task 12).

- [ ] **Step 1: Write the failing test**

```csharp
// Ez.Handball.Tests/Application/Services/TransferRuleApplierTests.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public sealed class TransferRuleApplierTests
{
    private readonly Mock<IChipActivationRepository> _chipActivation = new();
    private readonly Mock<IFreeTransferConfigRepository> _transferConfig = new();
    private readonly Mock<IFreeTransferAccrualService> _accrual = new();
    private readonly Mock<IFreeTransferBankRepository> _bank = new();
    private readonly Mock<IGameweekTransferStateRepository> _transferState = new();

    private static readonly FreeTransferConfig Cfg = new(1, 5, 4, true);
    private static readonly Gameweek Gw = new(2, "5", "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []);

    public TransferRuleApplierTests()
    {
        _chipActivation.Setup(x => x.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChipType?)null);
        _transferConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cfg);
    }

    private TransferRuleApplier Sut() => new(_chipActivation.Object, _transferConfig.Object, _accrual.Object, _bank.Object, _transferState.Object);

    [Fact]
    public async Task ApplyAsync_NoGameweekLockedYet_IsNoOp()
    {
        var guard = new SnapshotGuardResult(Gw, CurrentGameweekLocked: false);

        await Sut().ApplyAsync("team1", guard, CancellationToken.None);

        _bank.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_NoCurrentGameweek_IsNoOp()
    {
        var guard = new SnapshotGuardResult(null, CurrentGameweekLocked: true);

        await Sut().ApplyAsync("team1", guard, CancellationToken.None);

        _bank.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_WildcardActive_SkipsBankAndPaidCount()
    {
        _chipActivation.Setup(x => x.GetActiveAsync("team1", "5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChipType.Wildcard);
        var guard = new SnapshotGuardResult(Gw, CurrentGameweekLocked: true);

        await Sut().ApplyAsync("team1", guard, CancellationToken.None);

        _bank.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _transferState.Verify(x => x.IncrementPaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_BankHasFreeTransfer_ConsumesAndDoesNotRecordPaid()
    {
        _bank.Setup(x => x.TryConsumeAsync("team1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var guard = new SnapshotGuardResult(Gw, CurrentGameweekLocked: true);

        await Sut().ApplyAsync("team1", guard, CancellationToken.None);

        _accrual.Verify(x => x.AccrueAsync("team1", "5", It.IsAny<CancellationToken>()), Times.Once);
        _bank.Verify(x => x.TryConsumeAsync("team1", It.IsAny<CancellationToken>()), Times.Once);
        _transferState.Verify(x => x.IncrementPaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_BankEmpty_RecordsPaidTransfer()
    {
        _bank.Setup(x => x.TryConsumeAsync("team1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var guard = new SnapshotGuardResult(Gw, CurrentGameweekLocked: true);

        await Sut().ApplyAsync("team1", guard, CancellationToken.None);

        _transferState.Verify(x => x.IncrementPaidAsync("team1", "5", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TransferRuleApplierTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// Ez.Handball.Application/Services/ITransferRuleApplier.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface ITransferRuleApplier
{
    // Consumes a free transfer (or records a paid one) for a successful buy/sell, attributed
    // to the team's currently editable gameweek (guardResult.CurrentGameweek — the first still-
    // Open round, which already skips past any locked round). No-op if no gameweek is
    // currently editable, if a wildcard is active for that round, or if no gameweek has locked
    // yet this season (the season's first-round-unlimited window).
    Task ApplyAsync(string teamId, SnapshotGuardResult guardResult, CancellationToken ct);
}

public sealed class TransferRuleApplier : ITransferRuleApplier
{
    private const int DefaultVersion = 1;

    private readonly IChipActivationRepository _chipActivation;
    private readonly IFreeTransferConfigRepository _transferConfig;
    private readonly IFreeTransferAccrualService _accrual;
    private readonly IFreeTransferBankRepository _bank;
    private readonly IGameweekTransferStateRepository _transferState;

    public TransferRuleApplier(
        IChipActivationRepository chipActivation, IFreeTransferConfigRepository transferConfig,
        IFreeTransferAccrualService accrual, IFreeTransferBankRepository bank,
        IGameweekTransferStateRepository transferState)
    {
        _chipActivation = chipActivation;
        _transferConfig = transferConfig;
        _accrual = accrual;
        _bank = bank;
        _transferState = transferState;
    }

    public async Task ApplyAsync(string teamId, SnapshotGuardResult guardResult, CancellationToken ct)
    {
        if (guardResult.CurrentGameweek is null || !guardResult.CurrentGameweekLocked) return;

        var round = guardResult.CurrentGameweek.RoundLabel;
        var activeChip = await _chipActivation.GetActiveAsync(teamId, round, ct);
        if (activeChip == ChipType.Wildcard) return;

        var cfg = await _transferConfig.GetAsync(DefaultVersion, ct);
        if (cfg is null) return;

        await _accrual.AccrueAsync(teamId, round, ct);
        var consumed = await _bank.TryConsumeAsync(teamId, ct);
        if (!consumed)
            await _transferState.IncrementPaidAsync(teamId, round, ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TransferRuleApplierTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/ITransferRuleApplier.cs Ez.Handball.Tests/Application/Services/TransferRuleApplierTests.cs
git commit -m "feat(fantasy): add transfer rule applier (free/paid transfer decision)"
```

---

### Task 11: Wire `BuyPlayerUseCase`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/BuyPlayerUseCase.cs`
- Modify: `Ez.Handball.Tests/Application/UseCases/BuyPlayerUseCaseTests.cs`

**Interfaces:**
- Consumes: `ITransferRuleApplier.ApplyAsync(string teamId, SnapshotGuardResult guardResult, CancellationToken ct)` (Task 10).
- Produces: `BuyPlayerUseCase`'s constructor gains one new parameter, `ITransferRuleApplier applier` (appended last).

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/UseCases/BuyPlayerUseCaseTests.cs` (add the new mock field, wire it into the existing `Sut()` factory, and add these two facts):

```csharp
    private readonly Mock<ITransferRuleApplier> _applier = new();

    // In Sut(): add `_applier.Object` as the last constructor argument to `new BuyPlayerUseCase(...)`.

    [Fact]
    public async Task Allowed_AppliesTransferRuleOnCommit()
    {
        TeamExists();
        PlayerExists();
        DecisionReturns(Allowed());
        SquadViewReturns();

        await Sut().ExecuteAsync("u1", "p1", AnyContext(), CancellationToken.None);

        _applier.Verify(x => x.ApplyAsync("u1:fantasy", It.IsAny<SnapshotGuardResult>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejected_DoesNotApplyTransferRule()
    {
        TeamExists();
        PlayerExists();
        DecisionReturns(Rejected());

        await Sut().ExecuteAsync("u1", "p1", AnyContext(), CancellationToken.None);

        _applier.Verify(x => x.ApplyAsync(It.IsAny<string>(), It.IsAny<SnapshotGuardResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

(`AnyContext()` should already exist as a private helper in that test file per its existing conventions — if it doesn't, add `private static BuyPlayerContext AnyContext() => new(null, null, null);`, matching `BuyPlayerContext`'s real shape.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BuyPlayerUseCaseTests"`
Expected: FAIL to compile — `BuyPlayerUseCase`'s constructor doesn't accept an `ITransferRuleApplier` yet.

- [ ] **Step 3: Modify `BuyPlayerUseCase`**

Replace the whole file:

```csharp
// Ez.Handball.Application/UseCases/BuyPlayerUseCase.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record BuyPlayerResult
{
    public sealed record Committed(SquadView View) : BuyPlayerResult;
    public sealed record Rejected(IReadOnlyList<BuyRuleViolation> Violations) : BuyPlayerResult; // -> 422
    public sealed record Duplicate : BuyPlayerResult { public static readonly Duplicate Instance = new(); } // -> 409
    public sealed record PlayerNotFound : BuyPlayerResult { public static readonly PlayerNotFound Instance = new(); } // -> 404
    public sealed record RuleSetNotFound : BuyPlayerResult { public static readonly RuleSetNotFound Instance = new(); } // -> 400
    public sealed record NoTeam : BuyPlayerResult { public static readonly NoTeam Instance = new(); } // -> 409
}

public interface IBuyPlayerUseCase
{
    Task<BuyPlayerResult> ExecuteAsync(string userId, string playerId, BuyPlayerContext context, CancellationToken ct);
}

public sealed class BuyPlayerUseCase : IBuyPlayerUseCase
{
    private const string DuplicateCode = "duplicate_player";

    private readonly IGetBuyDecisionUseCase _decision;
    private readonly IGetSquadUseCase _squadView;
    private readonly IPlayerRepository _players;
    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly Func<DateTimeOffset> _now;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly ITransferLedgerRecorder _ledger;
    private readonly ITransferRuleApplier _applier;

    public BuyPlayerUseCase(
        IGetBuyDecisionUseCase decision, IGetSquadUseCase squadView, IPlayerRepository players,
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        Func<DateTimeOffset> now, IGameweekSnapshotGuard guard, ITransferLedgerRecorder ledger,
        ITransferRuleApplier applier)
    {
        _decision = decision;
        _squadView = squadView;
        _players = players;
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _now = now;
        _guard = guard;
        _ledger = ledger;
        _applier = applier;
    }

    public async Task<BuyPlayerResult> ExecuteAsync(
        string userId, string playerId, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return BuyPlayerResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var guardResult = await _guard.EnsureSnapshotsAsync(teamId, null, ct);

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return BuyPlayerResult.PlayerNotFound.Instance;

        var decision = await _decision.ExecuteAsync(userId, playerId, GameFlavor.Fantasy, context, ct);
        switch (decision)
        {
            case BuyDecisionResult.PlayerNotFound: return BuyPlayerResult.PlayerNotFound.Instance;
            case BuyDecisionResult.RuleSetNotFound: return BuyPlayerResult.RuleSetNotFound.Instance;
            case BuyDecisionResult.InvalidFlavor: return BuyPlayerResult.RuleSetNotFound.Instance; // fantasy fixed; defensive
            case BuyDecisionResult.Decided d when !d.Decision.Allowed:
                return d.Decision.Violations.Any(v => v.Code == DuplicateCode)
                    ? BuyPlayerResult.Duplicate.Instance
                    : new BuyPlayerResult.Rejected(d.Decision.Violations);
            case BuyDecisionResult.Decided d:
                return await CommitAsync(userId, playerId, player.Position, d.Decision, context, guardResult, ct);
            default:
                return BuyPlayerResult.RuleSetNotFound.Instance;
        }
    }

    private async Task<BuyPlayerResult> CommitAsync(
        string userId, string playerId, string? position, BuyDecision decision, BuyPlayerContext context,
        SnapshotGuardResult guardResult, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var cost = decision.Cost.Amount;

        // Write-time duplicate guard (decision-time squad may be stale).
        var existing = await _roster.GetAsync(teamId, playerId, ct);
        if (existing is { DeletedAt: null }) return BuyPlayerResult.Duplicate.Instance;

        var now = _now();
        if (!await _budget.TryDeductAsync(teamId, cost, now, ct))
            return new BuyPlayerResult.Rejected(new[] { new BuyRuleViolation("insufficient_budget", "Cost exceeds remaining budget") });

        var outcome = await _roster.AddOrResurrectAsync(teamId, playerId, position, cost, now, ct);
        if (outcome == RosterAddOutcome.AlreadyActive)
        {
            await _budget.TryCreditAsync(teamId, cost, now, ct); // compensate the deduction
            return BuyPlayerResult.Duplicate.Instance;
        }

        await _ledger.RecordAsync(
            new TransferEntry(userId, playerId, GameFlavor.Fantasy, TransferType.Buy, cost, context.Season, now), ct);

        // Only a fully-committed buy consumes a free transfer / records a paid one — a write-
        // time failure above (duplicate race, insufficient budget) must not charge the manager.
        await _applier.ApplyAsync(teamId, guardResult, ct);

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        return view is GetSquadResult.Found f
            ? new BuyPlayerResult.Committed(f.View)
            : BuyPlayerResult.RuleSetNotFound.Instance;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BuyPlayerUseCaseTests"`
Expected: PASS (all existing tests plus the 2 new ones).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/BuyPlayerUseCase.cs Ez.Handball.Tests/Application/UseCases/BuyPlayerUseCaseTests.cs
git commit -m "feat(fantasy): apply transfer rules on buy"
```

---

### Task 12: Wire `SellPlayerUseCase`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/SellPlayerUseCase.cs`
- Modify: `Ez.Handball.Tests/Application/UseCases/SellPlayerUseCaseTests.cs`

**Interfaces:**
- Consumes: same `ITransferRuleApplier` as Task 11.
- Produces: `SellPlayerUseCase`'s constructor gains one new parameter, `ITransferRuleApplier applier` (appended last).

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/UseCases/SellPlayerUseCaseTests.cs` (add the mock field, wire into `Sut()`, add these facts — following that file's existing helper-method conventions for team/roster/price/constraints setup):

```csharp
    private readonly Mock<ITransferRuleApplier> _applier = new();

    // In Sut(): add `_applier.Object` as the last constructor argument to `new SellPlayerUseCase(...)`.

    [Fact]
    public async Task Sold_AppliesTransferRule()
    {
        TeamExists();
        RosterHasActiveEntry();
        PriceExists();
        ConstraintsExist();
        SquadViewReturns();

        await Sut().ExecuteAsync("u1", "p1", AnyContext(), CancellationToken.None);

        _applier.Verify(x => x.ApplyAsync("u1:fantasy", It.IsAny<SnapshotGuardResult>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotInSquad_DoesNotApplyTransferRule()
    {
        TeamExists();
        // no roster entry set up — GetAsync returns null by default mock behavior

        await Sut().ExecuteAsync("u1", "p1", AnyContext(), CancellationToken.None);

        _applier.Verify(x => x.ApplyAsync(It.IsAny<string>(), It.IsAny<SnapshotGuardResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

(If the existing test file's helper names for roster/price/constraints setup differ from `RosterHasActiveEntry`/`PriceExists`/`ConstraintsExist`/`AnyContext`, use the file's real helper names instead — this task must match whatever `SellPlayerUseCaseTests.cs` already established for its happy-path setup.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SellPlayerUseCaseTests"`
Expected: FAIL to compile — constructor mismatch.

- [ ] **Step 3: Modify `SellPlayerUseCase`**

Replace the whole file:

```csharp
// Ez.Handball.Application/UseCases/SellPlayerUseCase.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SellPlayerResult
{
    public sealed record Sold(SquadView View) : SellPlayerResult;                 // 200
    public sealed record NotInSquad : SellPlayerResult { public static readonly NotInSquad Instance = new(); }       // 404
    public sealed record RuleSetNotFound : SellPlayerResult { public static readonly RuleSetNotFound Instance = new(); } // 400
    public sealed record NoTeam : SellPlayerResult { public static readonly NoTeam Instance = new(); }               // 409
}

public interface ISellPlayerUseCase
{
    Task<SellPlayerResult> ExecuteAsync(string userId, string playerId, BuyPlayerContext context, CancellationToken ct);
}

public sealed class SellPlayerUseCase : ISellPlayerUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGetSquadUseCase _squadView;
    private readonly IPlayerPriceService _price;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly Func<DateTimeOffset> _now;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly ITransferLedgerRecorder _ledger;
    private readonly ITransferRuleApplier _applier;

    public SellPlayerUseCase(
        IGetSquadUseCase squadView, IPlayerPriceService price, ISquadConstraintsRepository constraints,
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        Func<DateTimeOffset> now, IGameweekSnapshotGuard guard, ITransferLedgerRecorder ledger,
        ITransferRuleApplier applier)
    {
        _squadView = squadView;
        _price = price;
        _constraints = constraints;
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _now = now;
        _guard = guard;
        _ledger = ledger;
        _applier = applier;
    }

    public async Task<SellPlayerResult> ExecuteAsync(
        string userId, string playerId, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SellPlayerResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var guardResult = await _guard.EnsureSnapshotsAsync(teamId, null, ct);
        var entry = await _roster.GetAsync(teamId, playerId, ct);
        if (entry is null || entry.DeletedAt is not null)
            return SellPlayerResult.NotInSquad.Instance;

        var version = context.RuleSetVersion ?? DefaultVersion;
        var price = await _price.GetPriceAsync(playerId, version, context.Season, context.TournamentId, ct);
        if (price is null) return SellPlayerResult.RuleSetNotFound.Instance;

        // Constraints are a separate versioned axis (fantasy-squad-v{n}) from the pricing
        // ruleSetVersion (fantasy-price-v{n}); v1 is the only constraints version in play.
        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        if (constraints is null) return SellPlayerResult.RuleSetNotFound.Instance;

        var credit = SellValue.Compute(entry.PricePaidAmount, price.Price.Amount, constraints.SellOnFeeRate);

        var now = _now();
        await _roster.SoftDeleteAsync(teamId, playerId, now, ct);
        await _budget.TryCreditAsync(teamId, credit, now, ct);

        await _ledger.RecordAsync(
            new TransferEntry(userId, playerId, GameFlavor.Fantasy, TransferType.Sell, credit, context.Season, now), ct);

        // Only a fully-committed sell consumes a free transfer / records a paid one.
        await _applier.ApplyAsync(teamId, guardResult, ct);

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        return view is GetSquadResult.Found f
            ? new SellPlayerResult.Sold(f.View)
            : SellPlayerResult.RuleSetNotFound.Instance;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SellPlayerUseCaseTests"`
Expected: PASS (all existing tests plus the 2 new ones).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/SellPlayerUseCase.cs Ez.Handball.Tests/Application/UseCases/SellPlayerUseCaseTests.cs
git commit -m "feat(fantasy): apply transfer rules on sell"
```

---

### Task 13: Deduct the point hit in `SettleGameweekUseCase`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs`
- Modify: `Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs`

**Interfaces:**
- Consumes: `IChipActivationRepository.GetActiveAsync`, `IFreeTransferConfigRepository.GetAsync`, `IGameweekTransferStateRepository.GetPaidCountAsync`.
- Produces: `SettleGameweekUseCase`'s constructor gains three new parameters (`IChipActivationRepository chipActivation, IFreeTransferConfigRepository transferConfig, IGameweekTransferStateRepository transferState`, appended last); `ExecuteAsync` now returns a `GameweekScore` with `RawPoints`/`PointsHit` populated and `Points = RawPoints - PointsHit`.

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs` (add the three mock fields, wire into `Sut()`, default `_chipActivation` to return `null` and `_transferConfig` to return a config in the constructor's shared setup, then add these facts):

```csharp
    private readonly Mock<IChipActivationRepository> _chipActivation = new();
    private readonly Mock<IFreeTransferConfigRepository> _transferConfig = new();
    private readonly Mock<IGameweekTransferStateRepository> _transferState = new();

    // In the test class constructor's shared setup:
    // _chipActivation.Setup(x => x.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChipType?)null);
    // _transferConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new FreeTransferConfig(1, 5, 4, true));
    // _transferState.Setup(x => x.GetPaidCountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
    // In Sut(): append _chipActivation.Object, _transferConfig.Object, _transferState.Object as the last three constructor arguments.

    [Fact]
    public async Task Settled_NoPaidTransfers_PointsHitIsZero()
    {
        // ... existing happy-path setup from this test file (config/calendar/ruleset/constraints/snapshot/squad/stats) ...

        var result = await Sut().ExecuteAsync("u1", "u1:fantasy", "5", null, CancellationToken.None);

        var settled = Assert.IsType<SettleGameweekResult.Settled>(result);
        Assert.Equal(0, settled.Score.PointsHit);
        Assert.Equal(settled.Score.RawPoints, settled.Score.Points);
    }

    [Fact]
    public async Task Settled_TwoPaidTransfers_DeductsEightPoints()
    {
        _transferState.Setup(x => x.GetPaidCountAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(2);
        // ... existing happy-path setup ...

        var result = await Sut().ExecuteAsync("u1", "u1:fantasy", "5", null, CancellationToken.None);

        var settled = Assert.IsType<SettleGameweekResult.Settled>(result);
        Assert.Equal(8, settled.Score.PointsHit);
        Assert.Equal(settled.Score.RawPoints - 8, settled.Score.Points);
    }

    [Fact]
    public async Task Settled_WildcardActive_NoHitEvenWithPaidTransfers()
    {
        _chipActivation.Setup(x => x.GetActiveAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(ChipType.Wildcard);
        _transferState.Setup(x => x.GetPaidCountAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(3);
        // ... existing happy-path setup ...

        var result = await Sut().ExecuteAsync("u1", "u1:fantasy", "5", null, CancellationToken.None);

        var settled = Assert.IsType<SettleGameweekResult.Settled>(result);
        Assert.Equal(0, settled.Score.PointsHit);
    }

    [Fact]
    public async Task TransferConfigMissing_ReturnsConfigMissing()
    {
        _transferConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((FreeTransferConfig?)null);
        // ... existing happy-path setup for everything up through constraints ...

        var result = await Sut().ExecuteAsync("u1", "u1:fantasy", "5", null, CancellationToken.None);

        Assert.IsType<SettleGameweekResult.ConfigMissing>(result);
    }
```

(Fill in `// ... existing happy-path setup ...` with whatever this test file's established helper calls are for a fully-ready settlement — config/calendar/rule-set/constraints/snapshot/squad/stats all present and the round's matches all final; copy the exact setup from this file's existing `Settled_...` happy-path test.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SettleGameweekUseCaseTests"`
Expected: FAIL to compile — constructor mismatch.

- [ ] **Step 3: Modify `SettleGameweekUseCase`**

Replace the constructor and `ExecuteAsync` (the private `BuildPlayedStatsAsync` helper is unchanged, keep it as-is):

```csharp
// Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs — constructor and ExecuteAsync
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SettleGameweekResult
{
    public sealed record ConfigMissing : SettleGameweekResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : SettleGameweekResult { public static readonly NotFound Instance = new(); }   // unknown round/tournament
    public sealed record RuleSetMissing : SettleGameweekResult { public static readonly RuleSetMissing Instance = new(); }
    public sealed record NoSnapshotPossible : SettleGameweekResult { public static readonly NoSnapshotPossible Instance = new(); } // no live lineup to freeze
    public sealed record SquadNotFound : SettleGameweekResult { public static readonly SquadNotFound Instance = new(); } // owned squad couldn't be resolved
    public sealed record NotReady : SettleGameweekResult { public static readonly NotReady Instance = new(); }  // not all member matches final
    public sealed record Settled(GameweekScore Score) : SettleGameweekResult;
}

public interface ISettleGameweekUseCase
{
    Task<SettleGameweekResult> ExecuteAsync(
        string userId, string teamId, string roundLabel, int? configVersion, CancellationToken ct);
}

public sealed class SettleGameweekUseCase : ISettleGameweekUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly IGameweekLineupRepository _snapshots;
    private readonly ILineupRepository _liveLineup;
    private readonly IGameweekScoreRepository _scores;
    private readonly IGetSquadUseCase _squad;
    private readonly IPlayerStatsRepository _stats;
    private readonly IScoringRuleSetRepository _ruleSets;
    private readonly ILineupConstraintsRepository _constraints;
    private readonly IGameweekScoringService _scoring;
    private readonly IChipActivationRepository _chipActivation;
    private readonly IFreeTransferConfigRepository _transferConfig;
    private readonly IGameweekTransferStateRepository _transferState;

    public SettleGameweekUseCase(
        IGameweekConfigRepository config, IGameweekCalendarService calendar,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup,
        IGameweekScoreRepository scores, IGetSquadUseCase squad, IPlayerStatsRepository stats,
        IScoringRuleSetRepository ruleSets, ILineupConstraintsRepository constraints,
        IGameweekScoringService scoring, IChipActivationRepository chipActivation,
        IFreeTransferConfigRepository transferConfig, IGameweekTransferStateRepository transferState)
    {
        _config = config;
        _calendar = calendar;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
        _scores = scores;
        _squad = squad;
        _stats = stats;
        _ruleSets = ruleSets;
        _constraints = constraints;
        _scoring = scoring;
        _chipActivation = chipActivation;
        _transferConfig = transferConfig;
        _transferState = transferState;
    }

    public async Task<SettleGameweekResult> ExecuteAsync(
        string userId, string teamId, string roundLabel, int? configVersion, CancellationToken ct)
    {
        if (teamId != GameTeamId.For(userId, GameFlavor.Fantasy))
            return SettleGameweekResult.NotFound.Instance;

        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return SettleGameweekResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return SettleGameweekResult.NotFound.Instance;

        var gw = calendar.FirstOrDefault(g => g.RoundLabel == roundLabel);
        if (gw is null) return SettleGameweekResult.NotFound.Instance;

        if (gw.Matches.Count == 0 || !gw.Matches.All(m => m.IsFinal))
            return SettleGameweekResult.NotReady.Instance;

        var ruleSet = await _ruleSets.GetAsync(GameFlavor.Fantasy, config.ScoringRuleSetVersion, ct);
        if (ruleSet is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var constraints = await _constraints.GetAsync(config.LineupConstraintsVersion, ct);
        if (constraints is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var transferConfig = await _transferConfig.GetAsync(DefaultVersion, ct);
        if (transferConfig is null) return SettleGameweekResult.ConfigMissing.Instance;

        var snapshot = await _snapshots.GetSnapshotAsync(teamId, roundLabel, ct);
        if (snapshot is null)
        {
            var live = await _liveLineup.GetAsync(teamId, ct);
            if (live is null) return SettleGameweekResult.NoSnapshotPossible.Instance;
            await _snapshots.SaveSnapshotAsync(teamId, roundLabel, live, ct);
            snapshot = live;
        }

        var squadResult = await _squad.ExecuteAsync(userId, null, null, null, ct);
        if (squadResult is not GetSquadResult.Found found)
            return SettleGameweekResult.SquadNotFound.Instance;

        var played = await BuildPlayedStatsAsync(gw, ct);

        var activeChip = await _chipActivation.GetActiveAsync(teamId, roundLabel, ct);

        var score = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints);

        var paidCount = await _transferState.GetPaidCountAsync(teamId, roundLabel, ct);
        var hit = activeChip == ChipType.Wildcard ? 0 : paidCount * transferConfig.PointHitCost;
        var finalScore = score with { PointsHit = hit, Points = score.RawPoints - hit };

        await _scores.SaveAsync(finalScore, ct);
        return new SettleGameweekResult.Settled(finalScore);
    }

    // BuildPlayedStatsAsync is unchanged from the existing file — keep it as-is below this point.
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SettleGameweekUseCaseTests"`
Expected: PASS (all existing tests plus the 4 new ones).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs
git commit -m "feat(fantasy): deduct point hits at gameweek settlement"
```

---

### Task 14: Register Milestone B DI and add the transfer-status endpoint

**Files:**
- Modify: `Ez.Handball.Api/Program.cs`
- Create: `Ez.Handball.Application/UseCases/GetTransferStatusUseCase.cs`
- Create: `Ez.Handball.Api/TransferStatusEndpoints.cs`
- Test: Create `Ez.Handball.Tests/Application/UseCases/GetTransferStatusUseCaseTests.cs`

**Interfaces:**
- Consumes: `IGameTeamRepository.ExistsAsync`, `IGameweekSnapshotGuard.EnsureSnapshotsAsync`, `IFreeTransferBankRepository.GetAsync`, `IGameweekTransferStateRepository.GetPaidCountAsync`, `IFreeTransferConfigRepository.GetAsync`, `IChipActivationRepository.GetActiveAsync`.
- Produces: `IGetTransferStatusUseCase { Task<TransferStatusView?> ExecuteAsync(string userId, CancellationToken ct); }` where `TransferStatusView(string? RoundLabel, bool Unlimited, int FreeTransfersAvailable, int PaidTransfersThisRound, int PointHitIfSettledNow)`, mapped at `GET /api/users/me/transfers/status`.

- [ ] **Step 1: Write the failing test**

```csharp
// Ez.Handball.Tests/Application/UseCases/GetTransferStatusUseCaseTests.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public sealed class GetTransferStatusUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    private readonly Mock<IFreeTransferBankRepository> _bank = new();
    private readonly Mock<IGameweekTransferStateRepository> _transferState = new();
    private readonly Mock<IFreeTransferConfigRepository> _transferConfig = new();
    private readonly Mock<IChipActivationRepository> _chipActivation = new();

    private static readonly Gameweek Gw = new(2, "5", "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []);

    public GetTransferStatusUseCaseTests()
    {
        _transferConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new FreeTransferConfig(1, 5, 4, true));
        _chipActivation.Setup(x => x.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChipType?)null);
    }

    private GetTransferStatusUseCase Sut() => new(_teams.Object, _guard.Object, _bank.Object, _transferState.Object, _transferConfig.Object, _chipActivation.Object);

    [Fact]
    public async Task NoTeam_ReturnsNull()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Null(await Sut().ExecuteAsync("u1", CancellationToken.None));
    }

    [Fact]
    public async Task NoGameweekLockedYet_ReturnsUnlimited()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(Gw, CurrentGameweekLocked: false));

        var result = await Sut().ExecuteAsync("u1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Unlimited);
    }

    [Fact]
    public async Task GameweekLocked_ReturnsBankAndPaidCountAndHit()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(Gw, CurrentGameweekLocked: true));
        _bank.Setup(x => x.GetAsync("u1:fantasy", It.IsAny<CancellationToken>())).ReturnsAsync(new FreeTransferBank(2, "4"));
        _transferState.Setup(x => x.GetPaidCountAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Sut().ExecuteAsync("u1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Unlimited);
        Assert.Equal("5", result.RoundLabel);
        Assert.Equal(2, result.FreeTransfersAvailable);
        Assert.Equal(1, result.PaidTransfersThisRound);
        Assert.Equal(4, result.PointHitIfSettledNow);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetTransferStatusUseCaseTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write the use case and endpoint**

```csharp
// Ez.Handball.Application/UseCases/GetTransferStatusUseCase.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record TransferStatusView(
    string? RoundLabel, bool Unlimited, int FreeTransfersAvailable, int PaidTransfersThisRound, int PointHitIfSettledNow);

public interface IGetTransferStatusUseCase
{
    Task<TransferStatusView?> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetTransferStatusUseCase : IGetTransferStatusUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly IFreeTransferBankRepository _bank;
    private readonly IGameweekTransferStateRepository _transferState;
    private readonly IFreeTransferConfigRepository _transferConfig;
    private readonly IChipActivationRepository _chipActivation;

    public GetTransferStatusUseCase(
        IGameTeamRepository teams, IGameweekSnapshotGuard guard, IFreeTransferBankRepository bank,
        IGameweekTransferStateRepository transferState, IFreeTransferConfigRepository transferConfig,
        IChipActivationRepository chipActivation)
    {
        _teams = teams;
        _guard = guard;
        _bank = bank;
        _transferState = transferState;
        _transferConfig = transferConfig;
        _chipActivation = chipActivation;
    }

    public async Task<TransferStatusView?> ExecuteAsync(string userId, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct)) return null;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var guardResult = await _guard.EnsureSnapshotsAsync(teamId, null, ct);
        var round = guardResult.CurrentGameweek?.RoundLabel;

        if (round is null || !guardResult.CurrentGameweekLocked)
            return new TransferStatusView(round, Unlimited: true, 0, 0, 0);

        var bank = await _bank.GetAsync(teamId, ct);
        var paidCount = await _transferState.GetPaidCountAsync(teamId, round, ct);
        var activeChip = await _chipActivation.GetActiveAsync(teamId, round, ct);
        var cfg = await _transferConfig.GetAsync(DefaultVersion, ct);

        var hit = activeChip == ChipType.Wildcard || cfg is null ? 0 : paidCount * cfg.PointHitCost;

        return new TransferStatusView(round, Unlimited: false, bank?.Bank ?? 0, paidCount, hit);
    }
}
```

```csharp
// Ez.Handball.Api/TransferStatusEndpoints.cs
using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public static class TransferStatusEndpoints
{
    public static void MapTransferStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users/me/transfers/status", async (
            HttpContext http, IGetTransferStatusUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var view = await uc.ExecuteAsync(userId, ct);
            if (view is null) return Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict);

            return Results.Ok(new
            {
                roundLabel = view.RoundLabel,
                unlimited = view.Unlimited,
                freeTransfersAvailable = view.FreeTransfersAvailable,
                paidTransfersThisRound = view.PaidTransfersThisRound,
                pointHitIfSettledNow = view.PointHitIfSettledNow
            });
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetTransferStatusUseCaseTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Register in DI and map the endpoint**

In `Ez.Handball.Api/Program.cs`, add alongside the other Buy/Sell registrations:

```csharp
builder.Services.AddScoped<IFreeTransferAccrualService, FreeTransferAccrualService>();
builder.Services.AddScoped<ITransferRuleApplier, TransferRuleApplier>();
builder.Services.AddScoped<IGetTransferStatusUseCase, GetTransferStatusUseCase>();
```

And alongside the other `app.Map*Endpoints()` calls:

```csharp
app.MapTransferStatusEndpoints();
```

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors — this is the point where every Milestone A/B constructor change must compose correctly through DI.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj` (with `azurite --silent --location /tmp/azurite-test &` running)
Expected: PASS — every test in the solution, old and new.

- [ ] **Step 8: Commit**

```bash
git add Ez.Handball.Api/Program.cs Ez.Handball.Api/TransferStatusEndpoints.cs Ez.Handball.Application/UseCases/GetTransferStatusUseCase.cs Ez.Handball.Tests/Application/UseCases/GetTransferStatusUseCaseTests.cs
git commit -m "feat(fantasy): register transfer-rule services and add transfer status endpoint"
```

**Milestone B complete: free transfers, point hits, and cross-round attribution are fully working and shippable on their own.**

---

## Milestone C — Chips

### Task 15: `SetChipUseCase` / `GetChipUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/SetChipUseCase.cs`
- Create: `Ez.Handball.Application/UseCases/GetChipUseCase.cs`
- Test: Create `Ez.Handball.Tests/Application/UseCases/SetChipUseCaseTests.cs`
- Test: Create `Ez.Handball.Tests/Application/UseCases/GetChipUseCaseTests.cs`

**Interfaces:**
- Consumes: `IGameTeamRepository.ExistsAsync`, `IGameweekSnapshotGuard.EnsureSnapshotsAsync`, `IChipConfigRepository.GetAsync`, `IChipActivationRepository.GetActiveAsync`/`SetAsync`/`ListUsedAsync`, `RoundOrder.Compare`.
- Produces: `SetChipResult` (`NoTeam`, `RoundLocked`, `ConfigMissing`, `Rejected(IReadOnlyList<ChipViolation>)`, `Committed(ChipType? Active)`), `ISetChipUseCase.ExecuteAsync(string userId, ChipType? chip, CancellationToken ct)`; `ChipUsageSummary(ChipType Type, int UsedCount, int Limit, int Remaining)`, `ChipStatusView(ChipType? ActiveThisRound, string? RoundLabel, IReadOnlyList<ChipUsageSummary> Usage)`, `IGetChipUseCase.ExecuteAsync(string userId, CancellationToken ct) -> Task<ChipStatusView?>`. Both consumed by `ChipEndpoints` (Task 17).

- [ ] **Step 1: Write the failing tests**

```csharp
// Ez.Handball.Tests/Application/UseCases/SetChipUseCaseTests.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public sealed class SetChipUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    private readonly Mock<IChipConfigRepository> _chipConfig = new();
    private readonly Mock<IChipActivationRepository> _activation = new();

    private static readonly Gameweek Gw = new(2, "5", "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []);
    private static readonly ChipConfig Cfg = new(WildcardSeasonLimit: 2, BenchBoostSeasonLimit: 1, TripleCaptainSeasonLimit: 1, SecondWildcardUnlocksAtRound: 12);

    public SetChipUseCaseTests()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(Gw, CurrentGameweekLocked: true));
        _chipConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cfg);
        _activation.Setup(x => x.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChipType?)null);
        _activation.Setup(x => x.ListUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<(string, ChipType)>());
    }

    private SetChipUseCase Sut() => new(_teams.Object, _guard.Object, _chipConfig.Object, _activation.Object);

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Sut().ExecuteAsync("u1", ChipType.BenchBoost, CancellationToken.None);

        Assert.IsType<SetChipResult.NoTeam>(result);
    }

    [Fact]
    public async Task NoCurrentGameweek_ReturnsRoundLocked()
    {
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(null, CurrentGameweekLocked: true));

        var result = await Sut().ExecuteAsync("u1", ChipType.BenchBoost, CancellationToken.None);

        Assert.IsType<SetChipResult.RoundLocked>(result);
    }

    [Fact]
    public async Task NullChip_ClearsActivation()
    {
        var result = await Sut().ExecuteAsync("u1", null, CancellationToken.None);

        Assert.IsType<SetChipResult.Committed>(result);
        _activation.Verify(x => x.SetAsync("u1:fantasy", "5", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FirstUseOfBenchBoost_Commits()
    {
        var result = await Sut().ExecuteAsync("u1", ChipType.BenchBoost, CancellationToken.None);

        var committed = Assert.IsType<SetChipResult.Committed>(result);
        Assert.Equal(ChipType.BenchBoost, committed.Active);
        _activation.Verify(x => x.SetAsync("u1:fantasy", "5", ChipType.BenchBoost, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BenchBoostAlreadyUsedThisSeason_Rejected()
    {
        _activation.Setup(x => x.ListUsedAsync("u1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ("2", ChipType.BenchBoost) });

        var result = await Sut().ExecuteAsync("u1", ChipType.BenchBoost, CancellationToken.None);

        var rejected = Assert.IsType<SetChipResult.Rejected>(result);
        Assert.Contains(rejected.Violations, v => v.Code == "chip_limit_exceeded");
    }

    [Fact]
    public async Task AnotherChipAlreadyActiveThisRound_Rejected()
    {
        _activation.Setup(x => x.GetActiveAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(ChipType.TripleCaptain);

        var result = await Sut().ExecuteAsync("u1", ChipType.BenchBoost, CancellationToken.None);

        var rejected = Assert.IsType<SetChipResult.Rejected>(result);
        Assert.Contains(rejected.Violations, v => v.Code == "chip_already_active_this_round");
    }

    [Fact]
    public async Task WildcardUsedOnceInFirstHalf_SecondWildcardInFirstHalfRejected()
    {
        _activation.Setup(x => x.ListUsedAsync("u1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ("3", ChipType.Wildcard) });
        // current round "5" is still <= boundary (12) => first half

        var result = await Sut().ExecuteAsync("u1", ChipType.Wildcard, CancellationToken.None);

        var rejected = Assert.IsType<SetChipResult.Rejected>(result);
        Assert.Contains(rejected.Violations, v => v.Code == "chip_limit_exceeded");
    }

    [Fact]
    public async Task WildcardUsedInFirstHalf_SecondHalfWildcardStillAllowed()
    {
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(new Gameweek(3, "13", "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []), CurrentGameweekLocked: true));
        _activation.Setup(x => x.ListUsedAsync("u1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ("3", ChipType.Wildcard) }); // used in first half already

        var result = await Sut().ExecuteAsync("u1", ChipType.Wildcard, CancellationToken.None);

        Assert.IsType<SetChipResult.Committed>(result);
    }
}
```

```csharp
// Ez.Handball.Tests/Application/UseCases/GetChipUseCaseTests.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public sealed class GetChipUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    private readonly Mock<IChipConfigRepository> _chipConfig = new();
    private readonly Mock<IChipActivationRepository> _activation = new();

    private static readonly Gameweek Gw = new(2, "5", "t1", DateTimeOffset.UnixEpoch, GameweekStatus.Open, []);
    private static readonly ChipConfig Cfg = new(2, 1, 1, 12);

    public GetChipUseCaseTests()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _guard.Setup(x => x.EnsureSnapshotsAsync("u1:fantasy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotGuardResult(Gw, CurrentGameweekLocked: true));
        _chipConfig.Setup(x => x.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cfg);
        _activation.Setup(x => x.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChipType?)null);
        _activation.Setup(x => x.ListUsedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<(string, ChipType)>());
    }

    private GetChipUseCase Sut() => new(_teams.Object, _guard.Object, _chipConfig.Object, _activation.Object);

    [Fact]
    public async Task NoTeam_ReturnsNull()
    {
        _teams.Setup(x => x.ExistsAsync("u1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Null(await Sut().ExecuteAsync("u1", CancellationToken.None));
    }

    [Fact]
    public async Task NoUsage_ReturnsFullRemainingForEachChip()
    {
        var view = await Sut().ExecuteAsync("u1", CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal("5", view!.RoundLabel);
        Assert.Null(view.ActiveThisRound);
        Assert.Contains(view.Usage, u => u.Type == ChipType.Wildcard && u.Remaining == 2);
        Assert.Contains(view.Usage, u => u.Type == ChipType.BenchBoost && u.Remaining == 1);
        Assert.Contains(view.Usage, u => u.Type == ChipType.TripleCaptain && u.Remaining == 1);
    }

    [Fact]
    public async Task OneWildcardUsed_ReducesRemaining()
    {
        _activation.Setup(x => x.ListUsedAsync("u1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ("2", ChipType.Wildcard) });

        var view = await Sut().ExecuteAsync("u1", CancellationToken.None);

        Assert.Contains(view!.Usage, u => u.Type == ChipType.Wildcard && u.UsedCount == 1 && u.Remaining == 1);
    }

    [Fact]
    public async Task ChipActiveThisRound_IsReported()
    {
        _activation.Setup(x => x.GetActiveAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(ChipType.TripleCaptain);

        var view = await Sut().ExecuteAsync("u1", CancellationToken.None);

        Assert.Equal(ChipType.TripleCaptain, view!.ActiveThisRound);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetChipUseCaseTests|FullyQualifiedName~GetChipUseCaseTests"`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Write `SetChipUseCase`**

```csharp
// Ez.Handball.Application/UseCases/SetChipUseCase.cs
using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SetChipResult
{
    public sealed record NoTeam : SetChipResult { public static readonly NoTeam Instance = new(); }
    public sealed record RoundLocked : SetChipResult { public static readonly RoundLocked Instance = new(); }
    public sealed record ConfigMissing : SetChipResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record Rejected(IReadOnlyList<ChipViolation> Violations) : SetChipResult;
    public sealed record Committed(ChipType? Active) : SetChipResult;
}

public interface ISetChipUseCase
{
    // chip = null clears any active chip for the team's currently editable round.
    Task<SetChipResult> ExecuteAsync(string userId, ChipType? chip, CancellationToken ct);
}

public sealed class SetChipUseCase : ISetChipUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly IChipConfigRepository _chipConfig;
    private readonly IChipActivationRepository _activation;

    public SetChipUseCase(
        IGameTeamRepository teams, IGameweekSnapshotGuard guard,
        IChipConfigRepository chipConfig, IChipActivationRepository activation)
    {
        _teams = teams;
        _guard = guard;
        _chipConfig = chipConfig;
        _activation = activation;
    }

    public async Task<SetChipResult> ExecuteAsync(string userId, ChipType? chip, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SetChipResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var guardResult = await _guard.EnsureSnapshotsAsync(teamId, null, ct);
        if (guardResult.CurrentGameweek is null)
            return SetChipResult.RoundLocked.Instance;

        var round = guardResult.CurrentGameweek.RoundLabel;

        if (chip is null)
        {
            await _activation.SetAsync(teamId, round, null, ct);
            return new SetChipResult.Committed(null);
        }

        var cfg = await _chipConfig.GetAsync(DefaultVersion, ct);
        if (cfg is null) return SetChipResult.ConfigMissing.Instance;

        var existingThisRound = await _activation.GetActiveAsync(teamId, round, ct);
        if (existingThisRound is not null && existingThisRound != chip)
            return new SetChipResult.Rejected(
                new[] { new ChipViolation("chip_already_active_this_round", "Only one chip can be active per round") });

        var used = await _activation.ListUsedAsync(teamId, ct);
        var usedElsewhere = used.Where(u => u.Round != round).ToList();

        var violation = chip switch
        {
            ChipType.Wildcard => CheckWildcardLimit(usedElsewhere, round, cfg),
            ChipType.BenchBoost => usedElsewhere.Count(u => u.Type == ChipType.BenchBoost) >= cfg.BenchBoostSeasonLimit
                ? new ChipViolation("chip_limit_exceeded", "Bench boost has already been used this season")
                : null,
            ChipType.TripleCaptain => usedElsewhere.Count(u => u.Type == ChipType.TripleCaptain) >= cfg.TripleCaptainSeasonLimit
                ? new ChipViolation("chip_limit_exceeded", "Triple captain has already been used this season")
                : null,
            _ => null
        };
        if (violation is not null)
            return new SetChipResult.Rejected(new[] { violation });

        await _activation.SetAsync(teamId, round, chip, ct);
        return new SetChipResult.Committed(chip);
    }

    // Wildcard is split into two independent halves at cfg.SecondWildcardUnlocksAtRound (a
    // round-number cutoff): a wildcard used in one half never carries into the other — each
    // half gets its own single use, counted independently.
    private static ChipViolation? CheckWildcardLimit(
        IReadOnlyList<(string Round, ChipType Type)> usedElsewhere, string round, ChipConfig cfg)
    {
        if (cfg.SecondWildcardUnlocksAtRound is not int boundary)
        {
            // No split configured: a flat season-total limit.
            var usedTotal = usedElsewhere.Count(u => u.Type == ChipType.Wildcard);
            return usedTotal >= cfg.WildcardSeasonLimit
                ? new ChipViolation("chip_limit_exceeded", "Wildcard has already been used this season")
                : null;
        }

        var boundaryLabel = boundary.ToString(CultureInfo.InvariantCulture);
        var isSecondHalf = RoundOrder.Compare(round, boundaryLabel) > 0;

        var usedInSameHalf = usedElsewhere.Count(u =>
            u.Type == ChipType.Wildcard && (RoundOrder.Compare(u.Round, boundaryLabel) > 0) == isSecondHalf);

        return usedInSameHalf >= 1
            ? new ChipViolation("chip_limit_exceeded", "Wildcard has already been used for this half of the season")
            : null;
    }
}
```

- [ ] **Step 4: Write `GetChipUseCase`**

```csharp
// Ez.Handball.Application/UseCases/GetChipUseCase.cs
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record ChipUsageSummary(ChipType Type, int UsedCount, int Limit, int Remaining);

public sealed record ChipStatusView(ChipType? ActiveThisRound, string? RoundLabel, IReadOnlyList<ChipUsageSummary> Usage);

public interface IGetChipUseCase
{
    Task<ChipStatusView?> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetChipUseCase : IGetChipUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly IChipConfigRepository _chipConfig;
    private readonly IChipActivationRepository _activation;

    public GetChipUseCase(
        IGameTeamRepository teams, IGameweekSnapshotGuard guard,
        IChipConfigRepository chipConfig, IChipActivationRepository activation)
    {
        _teams = teams;
        _guard = guard;
        _chipConfig = chipConfig;
        _activation = activation;
    }

    public async Task<ChipStatusView?> ExecuteAsync(string userId, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct)) return null;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var guardResult = await _guard.EnsureSnapshotsAsync(teamId, null, ct);
        var round = guardResult.CurrentGameweek?.RoundLabel;

        var active = round is not null ? await _activation.GetActiveAsync(teamId, round, ct) : null;
        var used = await _activation.ListUsedAsync(teamId, ct);
        var cfg = await _chipConfig.GetAsync(DefaultVersion, ct);

        var usage = new List<ChipUsageSummary>();
        if (cfg is not null)
        {
            // Season-wide counts (not split by wildcard half) — a simplified display; the
            // half-boundary enforcement itself lives in SetChipUseCase.
            void Add(ChipType type, int limit)
            {
                var count = used.Count(u => u.Type == type);
                usage.Add(new ChipUsageSummary(type, count, limit, Math.Max(0, limit - count)));
            }

            Add(ChipType.Wildcard, cfg.WildcardSeasonLimit);
            Add(ChipType.BenchBoost, cfg.BenchBoostSeasonLimit);
            Add(ChipType.TripleCaptain, cfg.TripleCaptainSeasonLimit);
        }

        return new ChipStatusView(active, round, usage);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SetChipUseCaseTests|FullyQualifiedName~GetChipUseCaseTests"`
Expected: PASS (8 + 4 tests).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/UseCases/SetChipUseCase.cs Ez.Handball.Application/UseCases/GetChipUseCase.cs Ez.Handball.Tests/Application/UseCases/SetChipUseCaseTests.cs Ez.Handball.Tests/Application/UseCases/GetChipUseCaseTests.cs
git commit -m "feat(fantasy): add chip activation use cases"
```

---

### Task 16: Chip-aware scoring in `GameweekScoringService`

**Files:**
- Modify: `Ez.Handball.Application/Services/GameweekScoringService.cs`
- Modify: `Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs` (pass `activeChip` into `_scoring.Score(...)`)
- Modify: `Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs`
- Modify: `Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs`

**Interfaces:**
- Produces: `IGameweekScoringService.Score(...)` gains one new **trailing optional** parameter `ChipType? activeChip = null` (existing call sites/tests with 7 positional args keep compiling).

- [ ] **Step 1: Write the failing scoring-service tests**

Add to `Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs` (using that file's existing fixture helpers for building a snapshot/squad/stats/ruleSet/constraints — follow its established naming):

```csharp
    [Fact]
    public void Score_TripleCaptainActive_CaptainGetsTripleMultiplier()
    {
        // Arrange a snapshot where the captain played, using this file's existing helpers.
        // var score = Sut().Score(TeamId, Round, snapshot, squad, played, ruleSet, constraints, ChipType.TripleCaptain);
        // var captainLine = score.Breakdown.Single(b => b.PlayerId == captainId);
        // Assert.Equal(3.0, captainLine.Multiplier);
        // Assert.Equal(captainLine.RawPoints * 3.0, captainLine.Points);
    }

    [Fact]
    public void Score_TripleCaptainActive_CaptainDidNotPlay_ViceGetsNormalMultiplierNotTriple()
    {
        // Arrange a snapshot where the captain did NOT play but the vice did.
        // var score = Sut().Score(..., ChipType.TripleCaptain);
        // var viceLine = score.Breakdown.Single(b => b.PlayerId == viceId);
        // Assert.Equal(constraints.CaptainMultiplier, viceLine.Multiplier); // NOT 3.0
    }

    [Fact]
    public void Score_BenchBoostActive_AllBenchPlayersScoreWithoutAutoSub()
    {
        // Arrange a snapshot with a non-playing starter AND a bench player who played
        // (normally this would trigger an auto-sub).
        // var score = Sut().Score(..., ChipType.BenchBoost);
        // Assert.DoesNotContain(score.Breakdown, b => b.AutoSubbedIn); // no auto-sub occurred
        // Assert.All(score.Breakdown.Where(b => b.Played), b => Assert.True(b.Points > 0)); // every played player (starter or bench) scores
    }

    [Fact]
    public void Score_NoChip_UnchangedBehavior()
    {
        // Re-run one of this file's existing happy-path scenarios with the new trailing
        // `activeChip` parameter omitted entirely, and assert the result is identical to
        // before this task (regression guard that the default null path is untouched).
    }
```

(These four are written as commented pseudocode because they must be filled in using `GameweekScoringServiceTests.cs`'s real existing fixture-building helpers, which this plan does not have full sight of — the engineer executing this task must open that file first, find its existing snapshot/squad/stats builder helpers, and write real assertions following its established style before proceeding. Do not skip this — replace every commented line with real code before running the test.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests"`
Expected: FAIL to compile — `Score(...)` doesn't accept an 8th `activeChip` argument yet.

- [ ] **Step 3: Modify `GameweekScoringService`**

Replace the whole file:

```csharp
// Ez.Handball.Application/Services/GameweekScoringService.cs
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IGameweekScoringService
{
    // Pure rollup: applies auto-subs and the captain/vice multiplier to a frozen snapshot,
    // given who played (playedStatsByPlayer; absent key = did not play). activeChip modifies
    // the rollup: TripleCaptain gives the captain (not a promoted vice) a x3 multiplier
    // instead of x2; BenchBoost scores every bench player too and skips auto-subs entirely
    // (nothing needs covering when the whole bench already counts).
    GameweekScore Score(
        string teamId,
        string roundLabel,
        Lineup snapshot,
        IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet,
        LineupConstraints constraints,
        ChipType? activeChip = null);
}

public sealed class GameweekScoringService : IGameweekScoringService
{
    private readonly FantasyPlayerRatingFunction _rating;

    public GameweekScoringService(FantasyPlayerRatingFunction rating) => _rating = rating;

    public GameweekScore Score(
        string teamId, string roundLabel, Lineup snapshot, IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet, LineupConstraints constraints, ChipType? activeChip = null)
    {
        bool Played(string id) => playedStatsByPlayer.ContainsKey(id);

        double RawPoints(string id) => playedStatsByPlayer.TryGetValue(id, out var s)
            ? _rating.Compute(new PlayerRatingInputs(id, s, ruleSet,
                new PlayerRatingContext(null, null, null, ruleSet.Version, null, null))).Rating
            : 0;

        if (activeChip == ChipType.BenchBoost)
            return ScoreWithBenchBoost(teamId, roundLabel, snapshot, Played, RawPoints, constraints);

        var tripleCaptain = activeChip == ChipType.TripleCaptain;

        var starters = snapshot.Slots
            .Where(s => s.Role is LineupRole.Starter or LineupRole.Captain or LineupRole.Vice)
            .ToList();
        var bench = snapshot.Slots
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .ToList();

        var effectiveMap = new Dictionary<string, string>();
        var subbedInIds = new HashSet<string>();
        var replacedStarterIds = new HashSet<string>();
        var usedBench = new HashSet<string>();
        var posById = ownedSquad.ToDictionary(p => p.PlayerId, p => p.Position);

        var decidedEffective = new List<string>();

        foreach (var starter in starters)
        {
            if (Played(starter.PlayerId))
            {
                effectiveMap[starter.PlayerId] = starter.PlayerId;
                decidedEffective.Add(starter.PlayerId);
                continue;
            }

            var sub = FindValidSub(starter.PlayerId, decidedEffective, bench, usedBench, posById, constraints, Played);

            if (sub is not null)
            {
                usedBench.Add(sub);
                subbedInIds.Add(sub);
                replacedStarterIds.Add(starter.PlayerId);
                effectiveMap[starter.PlayerId] = sub;
                decidedEffective.Add(sub);
            }
            else
            {
                effectiveMap[starter.PlayerId] = starter.PlayerId;
                decidedEffective.Add(starter.PlayerId);
            }
        }

        var captainFromCaptainSlot = EffectiveArmband(effectiveMap, replacedStarterIds, LineupRole.Captain, snapshot, Played);
        var captainId = captainFromCaptainSlot
            ?? EffectiveArmband(effectiveMap, replacedStarterIds, LineupRole.Vice, snapshot, Played);

        // Triple captain applies only when the effective captain IS the actual captain slot
        // (never a promoted vice) — the chip doesn't carry over to whoever ends up captaining.
        var tripleCaptainApplies = tripleCaptain && captainFromCaptainSlot is not null && captainId == captainFromCaptainSlot;

        double Multiplier(string playerId) =>
            playerId == captainId ? (tripleCaptainApplies ? 3.0 : constraints.CaptainMultiplier) : 1.0;

        var breakdown = new List<GameweekPlayerScore>();
        double total = 0;

        foreach (var starter in starters)
        {
            var wasReplaced = replacedStarterIds.Contains(starter.PlayerId);

            if (!wasReplaced)
            {
                var played = Played(starter.PlayerId);
                var raw = RawPoints(starter.PlayerId);
                var isCaptain = starter.PlayerId == captainId && played;
                var multiplier = isCaptain ? Multiplier(starter.PlayerId) : 1.0;
                var points = played ? raw * multiplier : 0;
                total += points;
                breakdown.Add(new GameweekPlayerScore(
                    starter.PlayerId, raw, points, played,
                    AutoSubbedIn: false, isCaptain, multiplier));
            }
            else
            {
                breakdown.Add(new GameweekPlayerScore(
                    starter.PlayerId, RawPoints: 0, Points: 0,
                    Played: false, AutoSubbedIn: false, CaptainApplied: false, Multiplier: 1.0));
            }
        }

        foreach (var benchSlot in bench)
        {
            bool subbed = subbedInIds.Contains(benchSlot.PlayerId);
            if (subbed)
            {
                var played = Played(benchSlot.PlayerId);
                var raw = RawPoints(benchSlot.PlayerId);
                var isCaptain = benchSlot.PlayerId == captainId && played;
                var multiplier = isCaptain ? Multiplier(benchSlot.PlayerId) : 1.0;
                var points = played ? raw * multiplier : 0;
                total += points;
                breakdown.Add(new GameweekPlayerScore(
                    benchSlot.PlayerId, raw, points, played,
                    AutoSubbedIn: true, isCaptain, multiplier));
            }
            else
            {
                breakdown.Add(new GameweekPlayerScore(
                    benchSlot.PlayerId, RawPoints: 0, Points: 0,
                    Played: Played(benchSlot.PlayerId),
                    AutoSubbedIn: false, CaptainApplied: false, Multiplier: 1.0));
            }
        }

        return new GameweekScore(teamId, roundLabel, total, captainId, breakdown, RawPoints: total, PointsHit: 0);
    }

    // Bench boost: every slot (starter or bench) scores independently at its own multiplier —
    // no auto-sub, since the whole bench already counts. Triple captain never co-occurs with
    // bench boost (SetChipUseCase enforces one active chip per round), so the captain always
    // gets the normal constraints.CaptainMultiplier here, never x3.
    private static GameweekScore ScoreWithBenchBoost(
        string teamId, string roundLabel, Lineup snapshot,
        Func<string, bool> played, Func<string, double> rawPoints, LineupConstraints constraints)
    {
        var captainId = snapshot.Slots.FirstOrDefault(s => s.Role == LineupRole.Captain)?.PlayerId;
        var viceId = snapshot.Slots.FirstOrDefault(s => s.Role == LineupRole.Vice)?.PlayerId;
        var effectiveCaptainId = captainId is not null && played(captainId) ? captainId
            : viceId is not null && played(viceId) ? viceId
            : null;

        var breakdown = new List<GameweekPlayerScore>();
        double total = 0;

        foreach (var slot in snapshot.Slots)
        {
            var isCaptain = slot.PlayerId == effectiveCaptainId;
            var wasPlayed = played(slot.PlayerId);
            var raw = rawPoints(slot.PlayerId);
            var multiplier = isCaptain ? constraints.CaptainMultiplier : 1.0;
            var points = wasPlayed ? raw * multiplier : 0;
            total += points;
            breakdown.Add(new GameweekPlayerScore(
                slot.PlayerId, raw, points, wasPlayed, AutoSubbedIn: false, isCaptain, multiplier));
        }

        return new GameweekScore(teamId, roundLabel, total, effectiveCaptainId, breakdown, RawPoints: total, PointsHit: 0);
    }

    private static string? FindValidSub(
        string nonPlayingStarterId, IReadOnlyList<string> decidedEffective, IReadOnlyList<LineupSlot> bench,
        HashSet<string> usedBench, IReadOnlyDictionary<string, string?> posById,
        LineupConstraints constraints, Func<string, bool> played)
    {
        foreach (var b in bench)
        {
            if (usedBench.Contains(b.PlayerId) || !played(b.PlayerId)) continue;
            if (KeepsPositionsValid(nonPlayingStarterId, b.PlayerId, decidedEffective, posById, constraints))
                return b.PlayerId;
        }
        return null;
    }

    private static bool KeepsPositionsValid(
        string nonPlayingStarterId, string candidateId, IReadOnlyList<string> decidedEffective,
        IReadOnlyDictionary<string, string?> posById, LineupConstraints constraints)
    {
        posById.TryGetValue(nonPlayingStarterId, out var outgoingPos);
        posById.TryGetValue(candidateId, out var incomingPos);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Inc(string? pos)
        {
            if (pos is not null) counts[pos] = counts.TryGetValue(pos, out var c) ? c + 1 : 1;
        }

        foreach (var id in decidedEffective) { posById.TryGetValue(id, out var p); Inc(p); }
        Inc(incomingPos);

        foreach (var kv in constraints.PositionStart)
        {
            counts.TryGetValue(kv.Key, out var count);
            if (count > kv.Value.Max) return false;
        }

        if (outgoingPos is not null
            && !string.Equals(outgoingPos, incomingPos, StringComparison.Ordinal)
            && constraints.PositionStart.TryGetValue(outgoingPos, out var outgoingConstraint))
        {
            counts.TryGetValue(outgoingPos, out var countWithoutOutgoing);
            if (countWithoutOutgoing < outgoingConstraint.Min) return false;
        }

        return true;
    }

    private static string? EffectiveArmband(
        IReadOnlyDictionary<string, string> effectiveMap, IReadOnlySet<string> replacedStarterIds,
        LineupRole role, Lineup snapshot, Func<string, bool> played)
    {
        var holder = snapshot.Slots.FirstOrDefault(s => s.Role == role)?.PlayerId;
        if (holder is null) return null;
        if (replacedStarterIds.Contains(holder)) return null;
        if (!played(holder)) return null;
        return holder;
    }
}
```

- [ ] **Step 4: Wire `activeChip` into `SettleGameweekUseCase`'s scoring call**

In `Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs`, change:

```csharp
        var score = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints);
```

to:

```csharp
        var score = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints, activeChip);
```

Add one test to `Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs`:

```csharp
    [Fact]
    public async Task Settled_BenchBoostActive_PassesChipThroughToScoring()
    {
        _chipActivation.Setup(x => x.GetActiveAsync("u1:fantasy", "5", It.IsAny<CancellationToken>())).ReturnsAsync(ChipType.BenchBoost);
        // ... existing happy-path setup ...

        await Sut().ExecuteAsync("u1", "u1:fantasy", "5", null, CancellationToken.None);

        _scoring.Verify(x => x.Score(
            "u1:fantasy", "5", It.IsAny<Lineup>(), It.IsAny<IReadOnlyList<SquadPlayer>>(),
            It.IsAny<IReadOnlyDictionary<string, AggregatedStats>>(), It.IsAny<ScoringRuleSet>(),
            It.IsAny<LineupConstraints>(), ChipType.BenchBoost), Times.Once);
    }
```

(This assumes `_scoring` is already a `Mock<IGameweekScoringService>` field in that test file per its existing conventions — if the scoring service is invoked through a concrete instance instead of a mock in some existing tests, add a `Mock<IGameweekScoringService>` field and route through it for this new test specifically.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests|FullyQualifiedName~SettleGameweekUseCaseTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekScoringService.cs Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs
git commit -m "feat(fantasy): chip-aware scoring (triple captain, bench boost)"
```

---

### Task 17: `ChipEndpoints.cs`

**Files:**
- Create: `Ez.Handball.Api/ChipEndpoints.cs`
- Test: Create `Ez.Handball.Tests/Api/Endpoints/ChipEndpointTests.cs`

**Interfaces:**
- Consumes: `ISetChipUseCase`, `IGetChipUseCase` (Task 15).
- Produces: `PUT /api/users/me/chips` and `GET /api/users/me/chips`, mapped via `MapChipEndpoints(this WebApplication app)` — same shape as `LineupEndpoints.MapLineupEndpoints`.

- [ ] **Step 1: Write the failing endpoint test**

Follow the exact `WebApplicationFactory<Program>` + table-seed/cleanup pattern used by this project's existing `Api/Endpoints/*EndpointTests.cs` files (e.g. seed/clean `Tables.GameweekChipActivations`, `Tables.Config` for `fantasy-chips-v1` in `foreach (var t in new[] { ... })`, authenticate the same way those tests do). Write at minimum:

```csharp
// Ez.Handball.Tests/Api/Endpoints/ChipEndpointTests.cs
// - Get_NoTeam_Returns409
// - Put_InvalidChipType_Returns400
// - Put_ValidChip_Returns200AndActivates
// - Put_NullChipType_ClearsActivation
// - Get_AfterPut_ReflectsActivation
//
// Model this file on an existing endpoint test file for an authenticated fantasy-team-scoped
// resource (e.g. the lineup or squad endpoint tests) for the exact WebApplicationFactory setup,
// auth token minting, and table seed/cleanup boilerplate — copy that structure verbatim and
// adapt the assertions to the chip endpoints' request/response shape below.
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~ChipEndpointTests"`
Expected: FAIL — `MapChipEndpoints` doesn't exist yet (compile error or 404, depending on how far the test file got written).

- [ ] **Step 3: Write `ChipEndpoints.cs`**

```csharp
// Ez.Handball.Api/ChipEndpoints.cs
using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record SetChipRequest(string? ChipType);

public static class ChipEndpoints
{
    private const string Base = "/api/users/me/chips";

    public static void MapChipEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (HttpContext http, IGetChipUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var view = await uc.ExecuteAsync(userId, ct);
            if (view is null) return Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict);

            return Results.Ok(new
            {
                activeThisRound = view.ActiveThisRound?.ToString(),
                roundLabel = view.RoundLabel,
                usage = view.Usage.Select(u => new
                {
                    chipType = u.Type.ToString(), used = u.UsedCount, limit = u.Limit, remaining = u.Remaining
                })
            });
        });

        group.MapPut("", async (SetChipRequest req, HttpContext http, ISetChipUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            ChipType? chip = null;
            if (!string.IsNullOrWhiteSpace(req.ChipType))
            {
                if (!Enum.TryParse<ChipType>(req.ChipType, ignoreCase: true, out var parsed))
                    return Results.BadRequest(new { error = "invalid_chip_type" });
                chip = parsed;
            }

            var result = await uc.ExecuteAsync(userId, chip, ct);
            return result switch
            {
                SetChipResult.NoTeam => Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict),
                SetChipResult.RoundLocked => Results.Json(new { error = "round_locked" }, statusCode: StatusCodes.Status409Conflict),
                SetChipResult.ConfigMissing => Results.BadRequest(new { error = "chip_config_missing" }),
                SetChipResult.Rejected r => Results.Json(
                    new { violations = r.Violations.Select(v => new { code = v.Code, message = v.Message }) },
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                SetChipResult.Committed c => Results.Ok(new { activeThisRound = c.Active?.ToString() }),
                _ => Results.Problem()
            };
        });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~ChipEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/ChipEndpoints.cs Ez.Handball.Tests/Api/Endpoints/ChipEndpointTests.cs
git commit -m "feat(fantasy): add chip activation endpoints"
```

---

### Task 18: Final DI registration and endpoint mapping

**Files:**
- Modify: `Ez.Handball.Api/Program.cs`

**Interfaces:**
- Consumes: everything produced by Tasks 15 and 17.

- [ ] **Step 1: Register the remaining services**

In `Ez.Handball.Api/Program.cs`, alongside the other lineup/gameweek registrations:

```csharp
builder.Services.AddScoped<ISetChipUseCase, SetChipUseCase>();
builder.Services.AddScoped<IGetChipUseCase, GetChipUseCase>();
```

And alongside the other `app.Map*Endpoints()` calls:

```csharp
app.MapChipEndpoints();
```

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj` (with Azurite running)
Expected: PASS — every test in the solution.

- [ ] **Step 4: Manually seed and smoke-test locally** (optional but recommended before merging)

```bash
cd Ez.Handball.Ingestion && func start &
curl -X POST "http://localhost:7071/api/seed/free-transfer-config"
curl -X POST "http://localhost:7071/api/seed/chip-config"
```

Then exercise `PUT /api/users/me/chips`, `GET /api/users/me/chips`, `GET /api/users/me/transfers/status`, and a buy/sell against a locally running `Ez.Handball.Api` to confirm the end-to-end wiring.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/Program.cs
git commit -m "feat(fantasy): register chip use cases and map chip endpoints"
```

**Milestone C complete: all three chips are fully wired — activation, season limits, half-season wildcard split, and their scoring/point-hit effects.**

---

## Plan self-review notes

- **Spec coverage:** free-transfer accrual/rollover (Tasks 4, 7, 9), point hits at settlement (Tasks 2, 13), cross-round attribution via the existing snapshot guard's `CurrentGameweek` (Tasks 10–12), wildcard (Tasks 6, 10, 13, 15–17), bench boost and triple captain scoring (Task 16), chip season limits + half-season wildcard split (Task 15), extensibility of the bank for a future non-transfer credit (documented in the spec; the bank repository's plain `AccrueAsync(teamId, watermark, delta, cap, ct)` signature already accepts an arbitrary delta from any future caller, so no plan change was needed to leave that door open).
- **Placeholder scan:** Tasks 16 and 17 each contain one deliberately-flagged exception to "no placeholders" — the scoring-service chip tests and the chip endpoint tests reference existing test files' fixture helpers that this plan's author could not fully inspect. Both are called out explicitly with instructions to open the real file first rather than left as silent gaps.
- **Type consistency:** `ChipType`, `ChipConfig`, `FreeTransferConfig`, `FreeTransferBank`, `ChipViolation`, `GameweekScore.RawPoints`/`PointsHit` and `IGameweekScoringService.Score(..., ChipType? activeChip = null)` are each defined once (Tasks 1, 2, 16) and referenced identically by name in every later task.
