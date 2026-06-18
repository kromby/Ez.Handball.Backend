# Admin advance-and-settle fan-out Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add debug-only admin endpoints to move the virtual game clock and settle a fantasy round across every team in one call, so a season replay is one call per step instead of one per manager.

**Architecture:** Three new Application use cases (`AdvanceClockUseCase`, `SettleRoundForAllTeamsUseCase`) plus a write-only `IClockOverrideStore` seam, surfaced by three POST endpoints under `/api/debug/`. The fan-out loops the existing, unchanged `ISettleGameweekUseCase` once per team (Approach A), so idempotency and the virtual-now finality gate come for free. Endpoints are mapped only when the master enable flag is on (404 in production) and sit behind an `X-Debug-Key` shared-secret filter.

**Tech Stack:** .NET 9, ASP.NET Core minimal APIs, Azure Table Storage (`Azure.Data.Tables`), xUnit + Moq, Azurite (`UseDevelopmentStorage=true`) for storage-backed tests.

## Global Constraints

- Clean-architecture layering: dumb API edge → Application use cases → Infrastructure repositories. Application must not reference Infrastructure types (`GameClock` is reached via the `TimeProvider` base type).
- Fantasy-only feature (`GameFlavor.Fantasy`); team IDs have the form `{userId}:fantasy` (`GameTeamId.For`).
- No new NuGet dependencies. Tests use xUnit + Moq and the existing `Ez.Handball.Tests/TestSupport/StubTimeProvider.cs`.
- The virtual clock is domain/game time only; auth, rate-limit, and log clocks stay on the wall clock (do not touch the auth `Func<DateTimeOffset>` or register anything as the framework `TimeProvider`).
- Master enable flag: `Debug:GameClock:OverrideEnabled` (bool, default false). Shared secret: `Debug:AdminKey` (string). Endpoints live under `/api/debug/`.
- Config override row (read today by `GameClock`): table `Config`, PartitionKey `debug-clock-v1` (`GameClock.OverrideGroup`), RowKey `virtualNow` (`GameClock.OverrideKey`), Value an ISO-8601 UTC instant like `2025-09-01T17:00:00Z`.
- Each settle call settles exactly one round (the caller loops round-by-round).
- Commit after each task. Build: `dotnet build Ez.Handball.sln`. Test (filtered): `dotnet test --filter "FullyQualifiedName~<TestClass>"`.

---

### Task 1: Enumerate fantasy team IDs with a live lineup

Add `ListTeamIdsAsync` to `ILineupRepository` and implement it as a distinct-partition-key scan of the `GameLineups` table. The fan-out (Task 4) uses this to know which teams to settle.

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/ILineupRepository.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs` (create)

**Interfaces:**
- Produces: `ILineupRepository.ListTeamIdsAsync(CancellationToken ct) : Task<IReadOnlyList<string>>` — distinct team IDs (one per partition) that currently have at least one lineup row.

- [ ] **Step 1: Write the failing test**

Create `Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs`:

```csharp
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableLineupRepositoryTests
{
    [Fact]
    public async Task ListTeamIdsAsync_ReturnsDistinctPartitionKeys()
    {
        var rows = new[]
        {
            new GameLineupEntity { PartitionKey = "u1:fantasy", RowKey = "p1" },
            new GameLineupEntity { PartitionKey = "u1:fantasy", RowKey = "p2" },
            new GameLineupEntity { PartitionKey = "u2:fantasy", RowKey = "p1" },
        };
        var query = new Mock<ITableQuery>();
        query.Setup(q => q.QueryAsync<GameLineupEntity>(Tables.GameLineups, null, It.IsAny<CancellationToken>()))
            .Returns(ToAsync(rows));

        var repo = new TableLineupRepository(new TableServiceClient("UseDevelopmentStorage=true"), query.Object);

        var ids = await repo.ListTeamIdsAsync(default);

        Assert.Equal(new[] { "u1:fantasy", "u2:fantasy" }, ids.OrderBy(x => x));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TableLineupRepositoryTests"`
Expected: FAIL — compile error, `ILineupRepository` has no `ListTeamIdsAsync`.

- [ ] **Step 3: Add the interface method**

In `Ez.Handball.Application/Abstractions/ILineupRepository.cs`, add inside the interface:

```csharp
    // Distinct team IDs that currently have a live lineup (one entry per team). Used by the
    // admin settle-all fan-out (#96) to enumerate which teams to settle for a round.
    Task<IReadOnlyList<string>> ListTeamIdsAsync(CancellationToken ct);
```

- [ ] **Step 4: Implement it**

In `Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs`, add this method (the class already holds `_query`):

```csharp
    public async Task<IReadOnlyList<string>> ListTeamIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(Tables.GameLineups, null, ct))
            ids.Add(e.PartitionKey);
        return ids.ToList();
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TableLineupRepositoryTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Abstractions/ILineupRepository.cs \
        Ez.Handball.Infrastructure/TableAccess/TableLineupRepository.cs \
        Ez.Handball.Tests/Infrastructure/Tables/TableLineupRepositoryTests.cs
git commit -m "Add ILineupRepository.ListTeamIdsAsync for settle fan-out (Backend#96)"
```

---

### Task 2: Clock override write seam (`IClockOverrideStore`)

A write-only abstraction to set/clear the `debug-clock-v1`/`virtualNow` Config row that `GameClock` reads. The Table implementation mirrors `GameClock`'s table-name override so tests can use an isolated table, and the round-trip is proved by reading back through `GameClock`.

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IClockOverrideStore.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableClockOverrideStore.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`
- Test: `Ez.Handball.Tests/Infrastructure/TableClockOverrideStoreTests.cs` (create)

**Interfaces:**
- Produces: `IClockOverrideStore.SetAsync(DateTimeOffset utc, CancellationToken ct) : Task` and `IClockOverrideStore.ClearAsync(CancellationToken ct) : Task`.
- Consumes: `GameClock.OverrideGroup`, `GameClock.OverrideKey` (existing consts), `Tables.Config`, `ConfigEntity`.

- [ ] **Step 1: Write the failing test**

Create `Ez.Handball.Tests/Infrastructure/TableClockOverrideStoreTests.cs` (Azurite-backed, mirrors `GameClockTests`):

```csharp
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Xunit;

namespace Ez.Handball.Tests.Infrastructure;

public class TableClockOverrideStoreTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "TestClockOverrideStore";
    private TableServiceClient _serviceClient = null!;
    private TableClient _table = null!;

    public async Task InitializeAsync()
    {
        _serviceClient = new TableServiceClient(ConnectionString);
        _table = _serviceClient.GetTableClient(TableName);
        await _table.CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _table.DeleteAsync();

    private TableClockOverrideStore Store() => new(_serviceClient, TableName);
    private GameClock Clock() => new(enabled: true, _serviceClient, TableName);

    [Fact]
    public async Task SetAsync_WritesInstant_GameClockReadsItBack()
    {
        var instant = new DateTimeOffset(2025, 9, 1, 17, 0, 0, TimeSpan.Zero);

        await Store().SetAsync(instant, default);

        Assert.Equal(instant, Clock().GetUtcNow());
    }

    [Fact]
    public async Task ClearAsync_RemovesRow_GameClockFallsBackToWallClock()
    {
        await Store().SetAsync(new DateTimeOffset(2025, 9, 1, 17, 0, 0, TimeSpan.Zero), default);

        await Store().ClearAsync(default);

        Assert.True((DateTimeOffset.UtcNow - Clock().GetUtcNow()).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ClearAsync_WhenAbsent_DoesNotThrow()
    {
        await Store().ClearAsync(default); // row never written
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TableClockOverrideStoreTests"`
Expected: FAIL — `IClockOverrideStore` / `TableClockOverrideStore` do not exist.

- [ ] **Step 3: Create the abstraction**

Create `Ez.Handball.Application/Abstractions/IClockOverrideStore.cs`:

```csharp
namespace Ez.Handball.Application.Abstractions;

// Write seam for the debug virtual-clock override row that GameClock reads (#96). Write-only:
// reads happen via the synchronous GameClock point-read, not here.
public interface IClockOverrideStore
{
    // Upsert the virtual `now` as an ISO-8601 UTC instant.
    Task SetAsync(DateTimeOffset utc, CancellationToken ct);

    // Delete the override row. Absent row → no override (wall clock). Safe if already absent.
    Task ClearAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implement the Table store**

Create `Ez.Handball.Infrastructure/TableAccess/TableClockOverrideStore.cs`:

```csharp
using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

// Writes the debug-clock override row GameClock reads. configTableName defaults to the shared
// Config table; overridable for isolated tests (mirrors GameClock's ctor).
internal sealed class TableClockOverrideStore : IClockOverrideStore
{
    private readonly TableClient _config;

    public TableClockOverrideStore(TableServiceClient client, string? configTableName = null)
        => _config = client.GetTableClient(configTableName ?? Tables.Config);

    public async Task SetAsync(DateTimeOffset utc, CancellationToken ct)
    {
        await _config.CreateIfNotExistsAsync(cancellationToken: ct);
        // "yyyy-MM-ddTHH:mm:ssZ" round-trips through GameClock's DateTimeOffset.TryParse
        // (AssumeUniversal | AdjustToUniversal). Advance targets are whole-second instants.
        var value = utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        await _config.UpsertEntityAsync(new ConfigEntity
        {
            PartitionKey = GameClock.OverrideGroup,
            RowKey = GameClock.OverrideKey,
            Value = value
        }, TableUpdateMode.Replace, ct);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        try
        {
            await _config.DeleteEntityAsync(GameClock.OverrideGroup, GameClock.OverrideKey, cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            // already absent → nothing to clear
        }
    }
}
```

- [ ] **Step 5: Register it**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, add alongside the other `AddScoped` calls (factory form because the table-name param is optional):

```csharp
        services.AddScoped<IClockOverrideStore>(sp =>
            new TableClockOverrideStore(sp.GetRequiredService<TableServiceClient>()));
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TableClockOverrideStoreTests"`
Expected: PASS (requires Azurite running, as with `GameClockTests`).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IClockOverrideStore.cs \
        Ez.Handball.Infrastructure/TableAccess/TableClockOverrideStore.cs \
        Ez.Handball.Infrastructure/InfrastructureRegistration.cs \
        Ez.Handball.Tests/Infrastructure/TableClockOverrideStoreTests.cs
git commit -m "Add IClockOverrideStore write seam for the debug clock (Backend#96)"
```

---

### Task 3: `AdvanceClockUseCase`

Set absolute / advance-to-next-deadline / advance-to-next-round / clear the virtual clock. Advance modes derive the target from the gameweek calendar and current virtual `now`.

**Files:**
- Create: `Ez.Handball.Application/UseCases/AdvanceClockUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/AdvanceClockUseCaseTests.cs` (create)

**Interfaces:**
- Consumes: `IClockOverrideStore` (Task 2); `IGameweekConfigRepository.GetAsync(int version, CancellationToken)`; `IGameweekCalendarService.GetCalendarAsync(GameweekConfig, CancellationToken) : Task<IReadOnlyList<Gameweek>?>`; `TimeProvider.GetUtcNow()`; domain records `GameweekConfig(Version, TournamentId, LockOffsetHours, ScoringRuleSetVersion, LineupConstraintsVersion, MatchFinalBufferHours)`, `Gameweek(Number, RoundLabel, TournamentId, Deadline, Status, Matches)`, `GameweekMatch(MatchId, Date, IsFinal, HomeTeamId, AwayTeamId)`.
- Produces:
  - `enum ClockMode { Set, AdvanceDeadline, AdvanceRound, Clear }`
  - `IAdvanceClockUseCase.ExecuteAsync(ClockMode mode, DateTimeOffset? date, int? configVersion, CancellationToken ct) : Task<AdvanceClockResult>`
  - `AdvanceClockResult` with `Disabled`, `ConfigMissing`, `CalendarUnavailable`, `NothingToAdvance`, `Cleared`, `Moved(DateTimeOffset VirtualNow, string? RoundLabel)`.

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Application/UseCases/AdvanceClockUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class AdvanceClockUseCaseTests
{
    private readonly Mock<IClockOverrideStore> _store = new();
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1, 3);
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private AdvanceClockUseCase Sut(bool enabled = true) =>
        new(_store.Object, _config.Object, _calendar.Object, new StubTimeProvider(Now), enabled);

    private void SetupCalendar(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gws);
    }

    private static Gameweek GW(int n, string round, DateTimeOffset deadline, params GameweekMatch[] m) =>
        new(n, round, "8444", deadline, GameweekStatus.Open, m);

    private static GameweekMatch Match(string id, DateTimeOffset date, bool final) =>
        new(id, date, final, "h", "a");

    [Fact]
    public async Task Disabled_WhenFlagOff_DoesNotWrite()
    {
        var result = await Sut(enabled: false).ExecuteAsync(ClockMode.Clear, null, null, default);

        Assert.IsType<AdvanceClockResult.Disabled>(result);
        _store.Verify(s => s.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Clear_DeletesOverride()
    {
        var result = await Sut().ExecuteAsync(ClockMode.Clear, null, null, default);

        Assert.IsType<AdvanceClockResult.Cleared>(result);
        _store.Verify(s => s.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_WritesSuppliedInstantAsUtc()
    {
        var target = new DateTimeOffset(2025, 10, 5, 18, 30, 0, TimeSpan.FromHours(2)); // 16:30Z

        var result = await Sut().ExecuteAsync(ClockMode.Set, target, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(target.ToUniversalTime(), moved.VirtualNow);
        _store.Verify(s => s.SetAsync(target.ToUniversalTime(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceDeadline_PicksEarliestDeadlineAfterNow()
    {
        SetupCalendar(
            GW(1, "1", Now.AddHours(-1)),  // past
            GW(2, "2", Now.AddHours(5)),   // next
            GW(3, "3", Now.AddHours(50)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(Now.AddHours(5), moved.VirtualNow);
        Assert.Equal("2", moved.RoundLabel);
        _store.Verify(s => s.SetAsync(Now.AddHours(5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceDeadline_NoFutureDeadline_ReturnsNothingToAdvance()
    {
        SetupCalendar(GW(1, "1", Now.AddHours(-1)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        Assert.IsType<AdvanceClockResult.NothingToAdvance>(result);
    }

    [Fact]
    public async Task AdvanceRound_SetsNowToLastFixturePlusBuffer_ForFirstNotAllFinalRound()
    {
        // Round 1 already all-final; round 2 not yet. Buffer = 3h (Config.MatchFinalBufferHours).
        var r2Last = Now.AddHours(10);
        SetupCalendar(
            GW(1, "1", Now.AddHours(-50), Match("a", Now.AddHours(-48), final: true)),
            GW(2, "2", Now.AddHours(8),
                Match("b", Now.AddHours(9),  final: false),
                Match("c", r2Last,           final: false)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(r2Last.AddHours(3), moved.VirtualNow);
        Assert.Equal("2", moved.RoundLabel);
        _store.Verify(s => s.SetAsync(r2Last.AddHours(3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceRound_AllRoundsFinal_ReturnsNothingToAdvance()
    {
        SetupCalendar(GW(1, "1", Now.AddHours(-50), Match("a", Now.AddHours(-48), final: true)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        Assert.IsType<AdvanceClockResult.NothingToAdvance>(result);
    }

    [Fact]
    public async Task Advance_ConfigMissing_ReturnsConfigMissing()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        Assert.IsType<AdvanceClockResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task Advance_UnknownTournament_ReturnsCalendarUnavailable()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Gameweek>?)null);

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        Assert.IsType<AdvanceClockResult.CalendarUnavailable>(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AdvanceClockUseCaseTests"`
Expected: FAIL — `AdvanceClockUseCase` / `ClockMode` / `AdvanceClockResult` do not exist.

- [ ] **Step 3: Implement the use case**

Create `Ez.Handball.Application/UseCases/AdvanceClockUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public enum ClockMode { Set, AdvanceDeadline, AdvanceRound, Clear }

public abstract record AdvanceClockResult
{
    public sealed record Disabled : AdvanceClockResult { public static readonly Disabled Instance = new(); }
    public sealed record ConfigMissing : AdvanceClockResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record CalendarUnavailable : AdvanceClockResult { public static readonly CalendarUnavailable Instance = new(); }
    public sealed record NothingToAdvance : AdvanceClockResult { public static readonly NothingToAdvance Instance = new(); }
    public sealed record Cleared : AdvanceClockResult { public static readonly Cleared Instance = new(); }
    public sealed record Moved(DateTimeOffset VirtualNow, string? RoundLabel) : AdvanceClockResult;
}

public interface IAdvanceClockUseCase
{
    Task<AdvanceClockResult> ExecuteAsync(ClockMode mode, DateTimeOffset? date, int? configVersion, CancellationToken ct);
}

public sealed class AdvanceClockUseCase : IAdvanceClockUseCase
{
    private const int DefaultVersion = 1;

    private readonly IClockOverrideStore _store;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly TimeProvider _clock;
    private readonly bool _overrideEnabled;

    public AdvanceClockUseCase(
        IClockOverrideStore store, IGameweekConfigRepository config,
        IGameweekCalendarService calendar, TimeProvider clock, bool overrideEnabled)
    {
        _store = store;
        _config = config;
        _calendar = calendar;
        _clock = clock;
        _overrideEnabled = overrideEnabled;
    }

    public async Task<AdvanceClockResult> ExecuteAsync(
        ClockMode mode, DateTimeOffset? date, int? configVersion, CancellationToken ct)
    {
        // The override row is a no-op unless the master flag is on (it would be silently ignored
        // by GameClock). Refuse rather than appear to succeed.
        if (!_overrideEnabled) return AdvanceClockResult.Disabled.Instance;

        if (mode == ClockMode.Clear)
        {
            await _store.ClearAsync(ct);
            return AdvanceClockResult.Cleared.Instance;
        }

        if (mode == ClockMode.Set)
        {
            // The endpoint guarantees date is present for Set; guard for direct callers.
            if (date is null) return AdvanceClockResult.NothingToAdvance.Instance;
            var utc = date.Value.ToUniversalTime();
            await _store.SetAsync(utc, ct);
            return new AdvanceClockResult.Moved(utc, null);
        }

        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return AdvanceClockResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return AdvanceClockResult.CalendarUnavailable.Instance;

        var now = _clock.GetUtcNow();

        if (mode == ClockMode.AdvanceDeadline)
        {
            var next = calendar
                .Where(g => g.Deadline > now)
                .OrderBy(g => g.Deadline)
                .FirstOrDefault();
            if (next is null) return AdvanceClockResult.NothingToAdvance.Instance;
            await _store.SetAsync(next.Deadline, ct);
            return new AdvanceClockResult.Moved(next.Deadline, next.RoundLabel);
        }

        // AdvanceRound: the first round (calendar order) not yet all-final at the current clock.
        // Target = its last fixture + the finality buffer, which exactly trips the
        // `date + buffer <= now` finality gate so the whole round reads ready.
        var buffer = TimeSpan.FromHours(config.MatchFinalBufferHours);
        var round = calendar.FirstOrDefault(g => g.Matches.Count > 0 && !g.Matches.All(m => m.IsFinal));
        if (round is null) return AdvanceClockResult.NothingToAdvance.Instance;
        var target = round.Matches.Max(m => m.Date) + buffer;
        await _store.SetAsync(target, ct);
        return new AdvanceClockResult.Moved(target, round.RoundLabel);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AdvanceClockUseCaseTests"`
Expected: PASS (all nine tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/AdvanceClockUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/AdvanceClockUseCaseTests.cs
git commit -m "Add AdvanceClockUseCase (set/advance/clear virtual now) (Backend#96)"
```

---

### Task 4: `SettleRoundForAllTeamsUseCase` (the fan-out)

Enumerate fantasy teams with a live lineup, derive each `userId`, and run the existing per-team settlement, tallying outcomes into a report. Round/config-level failures (team-independent) surface once and stop.

**Files:**
- Create: `Ez.Handball.Application/UseCases/SettleRoundForAllTeamsUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/SettleRoundForAllTeamsUseCaseTests.cs` (create)

**Interfaces:**
- Consumes: `ILineupRepository.ListTeamIdsAsync` (Task 1); existing `ISettleGameweekUseCase.ExecuteAsync(string userId, string teamId, string roundLabel, int? configVersion, CancellationToken)` returning `SettleGameweekResult` (`Settled`, `NotReady`, `NoSnapshotPossible`, `SquadNotFound`, `ConfigMissing`, `NotFound`, `RuleSetMissing`); `GameTeamId.For(userId, GameFlavor.Fantasy)` → `"{userId}:fantasy"`.
- Produces:
  - `record SettleRoundReport(string Round, int TeamsConsidered, int Settled, int NotReady, int Skipped)`
  - `ISettleRoundForAllTeamsUseCase.ExecuteAsync(string roundLabel, int? configVersion, CancellationToken ct) : Task<SettleRoundForAllTeamsResult>`
  - `SettleRoundForAllTeamsResult` with `ConfigMissing`, `RoundNotFound`, `RuleSetMissing`, `Completed(SettleRoundReport Report)`.

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Application/UseCases/SettleRoundForAllTeamsUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class SettleRoundForAllTeamsUseCaseTests
{
    private readonly Mock<ILineupRepository> _lineups = new();
    private readonly Mock<ISettleGameweekUseCase> _settle = new();

    private SettleRoundForAllTeamsUseCase Sut() => new(_lineups.Object, _settle.Object);

    private void SetupTeams(params string[] teamIds) =>
        _lineups.Setup(l => l.ListTeamIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamIds);

    private void SetupSettle(string userId, string teamId, SettleGameweekResult result) =>
        _settle.Setup(s => s.ExecuteAsync(userId, teamId, "1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static SettleGameweekResult.Settled Settled() =>
        new(new GameweekScore("t", "1", 0, null, Array.Empty<GameweekPlayerScore>()));

    [Fact]
    public async Task FansOutOverFantasyTeams_TalliesOutcomes()
    {
        SetupTeams("u1:fantasy", "u2:fantasy", "u3:fantasy");
        SetupSettle("u1", "u1:fantasy", Settled());
        SetupSettle("u2", "u2:fantasy", SettleGameweekResult.NotReady.Instance);
        SetupSettle("u3", "u3:fantasy", SettleGameweekResult.NoSnapshotPossible.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal("1", report.Round);
        Assert.Equal(3, report.TeamsConsidered);
        Assert.Equal(1, report.Settled);
        Assert.Equal(1, report.NotReady);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public async Task IgnoresNonFantasyTeams()
    {
        SetupTeams("u1:fantasy", "u2:manager");
        SetupSettle("u1", "u1:fantasy", Settled());

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(1, report.TeamsConsidered);
        Assert.Equal(1, report.Settled);
        _settle.Verify(s => s.ExecuteAsync("u2", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyTeamSet_ReportsZeros()
    {
        SetupTeams();

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(0, report.TeamsConsidered);
        Assert.Equal(0, report.Settled);
    }

    [Fact]
    public async Task IdempotentReRun_AllAlreadySettled_StillReportsSettled()
    {
        SetupTeams("u1:fantasy", "u2:fantasy");
        SetupSettle("u1", "u1:fantasy", Settled()); // re-scored to same value
        SetupSettle("u2", "u2:fantasy", Settled());

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(2, report.Settled);
    }

    [Fact]
    public async Task RoundNotFound_SurfacesOnceAndStops()
    {
        SetupTeams("u1:fantasy", "u2:fantasy");
        SetupSettle("u1", "u1:fantasy", SettleGameweekResult.NotFound.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        Assert.IsType<SettleRoundForAllTeamsResult.RoundNotFound>(result);
        _settle.Verify(s => s.ExecuteAsync("u2", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfigMissing_SurfacesConfigMissing()
    {
        SetupTeams("u1:fantasy");
        SetupSettle("u1", "u1:fantasy", SettleGameweekResult.ConfigMissing.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        Assert.IsType<SettleRoundForAllTeamsResult.ConfigMissing>(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SettleRoundForAllTeamsUseCaseTests"`
Expected: FAIL — `SettleRoundForAllTeamsUseCase` / `SettleRoundForAllTeamsResult` / `SettleRoundReport` do not exist.

- [ ] **Step 3: Implement the use case**

Create `Ez.Handball.Application/UseCases/SettleRoundForAllTeamsUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record SettleRoundReport(
    string Round, int TeamsConsidered, int Settled, int NotReady, int Skipped);

public abstract record SettleRoundForAllTeamsResult
{
    public sealed record ConfigMissing : SettleRoundForAllTeamsResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record RoundNotFound : SettleRoundForAllTeamsResult { public static readonly RoundNotFound Instance = new(); }
    public sealed record RuleSetMissing : SettleRoundForAllTeamsResult { public static readonly RuleSetMissing Instance = new(); }
    public sealed record Completed(SettleRoundReport Report) : SettleRoundForAllTeamsResult;
}

public interface ISettleRoundForAllTeamsUseCase
{
    Task<SettleRoundForAllTeamsResult> ExecuteAsync(string roundLabel, int? configVersion, CancellationToken ct);
}

public sealed class SettleRoundForAllTeamsUseCase : ISettleRoundForAllTeamsUseCase
{
    // Matches GameTeamId.For(userId, GameFlavor.Fantasy) == "{userId}:fantasy".
    private static readonly string FantasySuffix = ":" + GameFlavor.Fantasy.ToString().ToLowerInvariant();

    private readonly ILineupRepository _lineups;
    private readonly ISettleGameweekUseCase _settle;

    public SettleRoundForAllTeamsUseCase(ILineupRepository lineups, ISettleGameweekUseCase settle)
    {
        _lineups = lineups;
        _settle = settle;
    }

    public async Task<SettleRoundForAllTeamsResult> ExecuteAsync(
        string roundLabel, int? configVersion, CancellationToken ct)
    {
        var teamIds = (await _lineups.ListTeamIdsAsync(ct))
            .Where(t => t.EndsWith(FantasySuffix, StringComparison.Ordinal))
            .ToList();

        int settled = 0, notReady = 0, skipped = 0;
        foreach (var teamId in teamIds)
        {
            var userId = teamId[..^FantasySuffix.Length];
            var r = await _settle.ExecuteAsync(userId, teamId, roundLabel, configVersion, ct);
            switch (r)
            {
                case SettleGameweekResult.Settled:
                    settled++;
                    break;
                case SettleGameweekResult.NotReady:
                    notReady++;
                    break;
                case SettleGameweekResult.NoSnapshotPossible:
                case SettleGameweekResult.SquadNotFound:
                    skipped++;
                    break;
                // Team-independent failures: the round/config is wrong for everyone — stop and report once.
                case SettleGameweekResult.ConfigMissing:
                    return SettleRoundForAllTeamsResult.ConfigMissing.Instance;
                case SettleGameweekResult.NotFound:
                    return SettleRoundForAllTeamsResult.RoundNotFound.Instance;
                case SettleGameweekResult.RuleSetMissing:
                    return SettleRoundForAllTeamsResult.RuleSetMissing.Instance;
            }
        }

        return new SettleRoundForAllTeamsResult.Completed(
            new SettleRoundReport(roundLabel, teamIds.Count, settled, notReady, skipped));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SettleRoundForAllTeamsUseCaseTests"`
Expected: PASS (all six tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/SettleRoundForAllTeamsUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/SettleRoundForAllTeamsUseCaseTests.cs
git commit -m "Add SettleRoundForAllTeamsUseCase fan-out over teams (Backend#96)"
```

---

### Task 5: Debug endpoints + gated wiring

Map `POST /api/debug/clock`, `/settle-round`, `/advance-and-settle` behind an `X-Debug-Key` filter, mapped only when `Debug:GameClock:OverrideEnabled` is on. Register the new use cases. Gating is verified with storage-independent WAF tests (flag off → 404; bad/missing/unconfigured key → 401/403).

**Files:**
- Create: `Ez.Handball.Api/DebugReplayEndpoints.cs`
- Modify: `Ez.Handball.Api/Program.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/DebugReplayEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `IAdvanceClockUseCase` + `ClockMode`/`AdvanceClockResult` (Task 3); `ISettleRoundForAllTeamsUseCase` + `SettleRoundForAllTeamsResult` (Task 4).
- Produces: `DebugReplayEndpoints.MapDebugReplayEndpoints(this WebApplication app, string? adminKey)`; header const `DebugReplayEndpoints.HeaderName = "X-Debug-Key"`.

- [ ] **Step 1: Write the failing gating tests**

Create `Ez.Handball.Tests/Api/Endpoints/DebugReplayEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ez.Handball.Tests.Api.Endpoints;

public class DebugReplayEndpointTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _settings;
        public Factory(bool enabled, string? adminKey)
        {
            _settings = new()
            {
                ["Debug:GameClock:OverrideEnabled"] = enabled ? "true" : "false",
                ["Debug:AdminKey"] = adminKey,
            };
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(_settings));
            return base.CreateHost(builder);
        }
    }

    private static HttpRequestMessage Clear(string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/debug/clock")
        {
            Content = JsonContent.Create(new { mode = "clear" })
        };
        if (key is not null) req.Headers.Add("X-Debug-Key", key);
        return req;
    }

    [Fact]
    public async Task FlagOff_RouteNotMapped_Returns404()
    {
        using var factory = new Factory(enabled: false, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("secret"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_MissingKey_Returns401()
    {
        using var factory = new Factory(enabled: true, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear(key: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_WrongKey_Returns401()
    {
        using var factory = new Factory(enabled: true, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_KeyNotConfigured_Returns403()
    {
        using var factory = new Factory(enabled: true, adminKey: null);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("anything"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DebugReplayEndpointTests"`
Expected: FAIL — the route is not mapped; the first test may pass coincidentally (404) but the 401/403 tests fail.

- [ ] **Step 3: Create the endpoints**

Create `Ez.Handball.Api/DebugReplayEndpoints.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

// Debug-only replay harness controls (#96). Mapped only when Debug:GameClock:OverrideEnabled is on
// (so absent in production) and gated behind an X-Debug-Key shared secret. Domain-clock time only.
public static class DebugReplayEndpoints
{
    public const string HeaderName = "X-Debug-Key";

    public sealed record ClockRequest(string Mode, DateTimeOffset? Date, int? Version);

    public static void MapDebugReplayEndpoints(this WebApplication app, string? adminKey)
    {
        var group = app.MapGroup("/api/debug").AddEndpointFilter(new DebugKeyFilter(adminKey));

        group.MapPost("/clock", async (ClockRequest body, IAdvanceClockUseCase uc, CancellationToken ct) =>
        {
            var mode = ParseMode(body.Mode);
            if (mode is null) return Results.BadRequest(new { error = "invalid_mode" });
            if (mode == ClockMode.Set && body.Date is null) return Results.BadRequest(new { error = "date_required" });

            return MapClock(await uc.ExecuteAsync(mode.Value, body.Date, body.Version, ct));
        });

        group.MapPost("/settle-round", async (string round, int? version,
            ISettleRoundForAllTeamsUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(round)) return Results.BadRequest(new { error = "invalid_round" });
            return MapSettle(await uc.ExecuteAsync(round, version, ct));
        });

        group.MapPost("/advance-and-settle", async (int? version,
            IAdvanceClockUseCase clock, ISettleRoundForAllTeamsUseCase settle, CancellationToken ct) =>
        {
            var advanced = await clock.ExecuteAsync(ClockMode.AdvanceRound, null, version, ct);
            if (advanced is not AdvanceClockResult.Moved moved)
                return MapClock(advanced); // Disabled / ConfigMissing / CalendarUnavailable / NothingToAdvance

            var settleResult = await settle.ExecuteAsync(moved.RoundLabel!, version, ct);
            if (settleResult is not SettleRoundForAllTeamsResult.Completed completed)
                return MapSettle(settleResult);

            return Results.Ok(new { virtualNow = moved.VirtualNow, round = moved.RoundLabel, report = completed.Report });
        });
    }

    private static ClockMode? ParseMode(string mode) => mode switch
    {
        "set" => ClockMode.Set,
        "advance-deadline" => ClockMode.AdvanceDeadline,
        "advance-round" => ClockMode.AdvanceRound,
        "clear" => ClockMode.Clear,
        _ => null
    };

    private static IResult MapClock(AdvanceClockResult result) => result switch
    {
        AdvanceClockResult.Disabled => Results.Json(new { error = "override_disabled" }, statusCode: StatusCodes.Status409Conflict),
        AdvanceClockResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
        AdvanceClockResult.CalendarUnavailable => Results.NotFound(new { error = "tournament_not_found" }),
        AdvanceClockResult.NothingToAdvance => Results.Json(new { error = "nothing_to_advance" }, statusCode: StatusCodes.Status409Conflict),
        AdvanceClockResult.Cleared => Results.Ok(new { virtualNow = (DateTimeOffset?)null, round = (string?)null, enabled = true }),
        AdvanceClockResult.Moved m => Results.Ok(new { virtualNow = m.VirtualNow, round = m.RoundLabel, enabled = true }),
        _ => Results.Problem()
    };

    private static IResult MapSettle(SettleRoundForAllTeamsResult result) => result switch
    {
        SettleRoundForAllTeamsResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
        SettleRoundForAllTeamsResult.RoundNotFound => Results.NotFound(new { error = "round_not_found" }),
        SettleRoundForAllTeamsResult.RuleSetMissing => Results.BadRequest(new { error = "rule_set_missing" }),
        SettleRoundForAllTeamsResult.Completed c => Results.Ok(c.Report),
        _ => Results.Problem()
    };
}

internal sealed class DebugKeyFilter : IEndpointFilter
{
    private readonly string? _adminKey;

    public DebugKeyFilter(string? adminKey) => _adminKey = adminKey;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        // Secure default: if the flag enabled the endpoints but no key is configured, refuse — never an open door.
        if (string.IsNullOrEmpty(_adminKey))
            return Results.Json(new { error = "debug_key_not_configured" }, statusCode: StatusCodes.Status403Forbidden);

        var provided = ctx.HttpContext.Request.Headers[DebugReplayEndpoints.HeaderName].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(_adminKey)))
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

        return await next(ctx);
    }
}
```

- [ ] **Step 4: Register use cases and map the group (gated)**

In `Ez.Handball.Api/Program.cs`, add to the registration region (near the other gameweek `AddScoped` calls, after `ISettleGameweekUseCase` is registered):

```csharp
builder.Services.AddScoped<ISettleRoundForAllTeamsUseCase, SettleRoundForAllTeamsUseCase>();
builder.Services.AddScoped<IAdvanceClockUseCase>(sp => new AdvanceClockUseCase(
    sp.GetRequiredService<IClockOverrideStore>(),
    sp.GetRequiredService<IGameweekConfigRepository>(),
    sp.GetRequiredService<IGameweekCalendarService>(),
    sp.GetRequiredService<GameClock>(),
    builder.Configuration.GetValue("Debug:GameClock:OverrideEnabled", false)));
```

Then, in the endpoint-mapping section near the other `app.Map...Endpoints()` calls (after `app.MapGameweekEndpoints();`), add the gated mapping:

```csharp
// Debug-only replay harness (#96): only exists when the master override flag is on (off in
// production → routes return 404). Behind an X-Debug-Key shared secret (see DebugKeyFilter).
if (builder.Configuration.GetValue("Debug:GameClock:OverrideEnabled", false))
    app.MapDebugReplayEndpoints(builder.Configuration["Debug:AdminKey"]);
```

- [ ] **Step 5: Run the gating tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DebugReplayEndpointTests"`
Expected: PASS (all four tests).

- [ ] **Step 6: Run the full suite to confirm nothing regressed**

Run: `dotnet test Ez.Handball.sln`
Expected: PASS (all green; storage-backed tests require Azurite running).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Api/DebugReplayEndpoints.cs Ez.Handball.Api/Program.cs \
        Ez.Handball.Tests/Api/Endpoints/DebugReplayEndpointTests.cs
git commit -m "Add gated /api/debug clock + advance-and-settle endpoints (Backend#96)"
```

---

## Operational notes (for the implementer / reviewer)

- **Production safety:** with `Debug:GameClock:OverrideEnabled` absent/false (the production default), the three routes are never mapped — `GameClock` already returns the wall clock with no table I/O. Nothing here is reachable in production.
- **Enabling in a debug environment:** set `Debug:GameClock:OverrideEnabled=true` and a strong `Debug:AdminKey`; pass that key in the `X-Debug-Key` header. The flag is captured at host build (a restart flips it); the virtual `now` value stays runtime-settable via the endpoints.
- **Replay loop:** repeatedly `POST /api/debug/advance-and-settle` (one round per call) to walk a finished season; the response reports the new `virtualNow`, the `round`, and settled/notReady/skipped counts. Re-invoking is idempotent.
- **Known limitation (inherited, documented in the spec):** `AdvanceRound` only makes a round ready if its fixtures are stored final (`Status == "S"`); for an unfinished season the clock still moves but such teams report `notReady`. The replay harness targets finished seasons.

## Out of scope

- Wiring the production blob `TriggerSettlementFunction` fan-out — `SettleRoundForAllTeamsUseCase` is the reusable seam it can later call.
- Real-time acceleration / time-scale multipliers; rewinding mutable manager state (transfers, chips, budget).
- Settling multiple rounds in one call.
