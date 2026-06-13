# Gameweek Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the fantasy gameweek engine — a derived gameweek calendar with lazy deadline locking, lazy lineup snapshots, ingestion-triggered scoring rollup with FPL-style position-valid auto-subs, and a recomputable settlement — per the approved spec `docs/superpowers/specs/2026-06-13-gameweek-engine-design.md`.

**Architecture:** Clean-architecture C# (.NET 8): dumb Api edge → Application use-cases/services (all computation, request-time capable) → Infrastructure Azure Table repos. The gameweek calendar is *derived* on demand from the `Matches` table (Approach A); only three non-derivable things are persisted — a pinned deadline per gameweek, a per-(team, gameweek) frozen lineup snapshot, and a per-(team, gameweek) settled score. Ingestion holds no scoring logic; it pokes an authenticated settlement endpoint.

**Tech Stack:** C# / .NET 8, Azure.Data.Tables, Azure Functions (Api = ASP.NET minimal API host; Ingestion = isolated worker), xUnit + Moq, Azurite for storage-integration tests.

**Build order (three phases, each shippable on its own):**
- **Phase 1 — Calendar + config + read endpoints.** Derive gameweeks, surface status/deadline. Ships a read-only gameweek view.
- **Phase 2 — Scoring rollup + settlement.** Snapshot, score, auto-sub, persist; manual/ingestion settle + score read.
- **Phase 3 — Lock-aware mutations.** Snapshot guard wired into buy/sell/lineup, `appliedToGameweek` echo, ingestion trigger.

**Conventions to follow (already in the codebase):**
- Tests: xUnit `[Fact]`, Moq, a `CreateSut()` helper, mirror `Ez.Handball.Tests/Application/Services/TournamentScopeResolverTests.cs`.
- Run a test class: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~<ClassName>"`
- Build: `dotnet build Ez.Handball.sln`
- Domain records are immutable `sealed record`s in `Ez.Handball.Domain`.
- Repo interfaces in `Ez.Handball.Application/Abstractions`, implementations `internal sealed` in `Ez.Handball.Infrastructure/TableAccess`, registered in `InfrastructureRegistration.cs`.
- Table entities (`ITableEntity`) live in `Ez.Handball.Shared/Entities`.
- `now` is injected as `Func<DateTimeOffset>` (registered in `AuthInfrastructureRegistration.cs`).
- Table queries go through `ITableQuery.QueryAsync<T>(table, oDataFilter, ct)`; escape keys with `ODataFilter.Escape(...)`.

---

## File Structure

**New — Domain (`Ez.Handball.Domain/`):**
- `Gameweek.cs` — `GameweekStatus` enum + `Gameweek` + `GameweekMatch` records (the derived calendar view).
- `GameweekConfig.cs` — the per-season fantasy-calendar config record.
- `GameweekScore.cs` — `GameweekScore` + `GameweekPlayerScore` records (settlement result).

**New — Application abstractions (`Ez.Handball.Application/Abstractions/`):**
- `IGameweekConfigRepository.cs`, `IGameweekLockRepository.cs`, `IGameweekLineupRepository.cs`, `IGameweekScoreRepository.cs`.
- Modify `IPlayerStatsRepository.cs` — add `GetByMatchAsync`.

**New — Application services (`Ez.Handball.Application/Services/`):**
- `GameweekCalendarService.cs` (+ `IGameweekCalendarService`).
- `GameweekScoringService.cs` (+ `IGameweekScoringService`).
- `GameweekSnapshotGuard.cs` (+ `IGameweekSnapshotGuard`) — Phase 3.

**New — Application use cases (`Ez.Handball.Application/UseCases/`):**
- `GetGameweeksUseCase.cs`, `GetCurrentGameweekUseCase.cs`, `GetMyGameweekScoresUseCase.cs`, `SettleGameweekUseCase.cs`.

**New — Shared entities (`Ez.Handball.Shared/Entities/`):**
- `GameweekLockEntity.cs`, `GameweekLineupEntity.cs`, `GameweekScoreEntity.cs`.

**New — Infrastructure (`Ez.Handball.Infrastructure/TableAccess/`):**
- `TableGameweekConfigRepository.cs`, `TableGameweekLockRepository.cs`, `TableGameweekLineupRepository.cs`, `TableGameweekScoreRepository.cs`.
- Modify `Tables.cs` (3 new constants), `TablePlayerStatsRepository.cs` (add `GetByMatchAsync`), `InfrastructureRegistration.cs` (register 4 repos).

**New — Ingestion (`Ez.Handball.Ingestion/Functions/`):**
- `SeedGameweekConfigFunction.cs`, and a settlement-trigger addition (Phase 3).

**New / modified — Api (`Ez.Handball.Api/`):**
- `GameweekEndpoints.cs` (new), `Program.cs` (register use-cases/services + `MapGameweekEndpoints`), `SquadEndpoints.cs` + `LineupEndpoints.cs` (Phase 3 echo).

**Tests (`Ez.Handball.Tests/`):**
- `Application/Services/GameweekCalendarServiceTests.cs`, `GameweekScoringServiceTests.cs`, `GameweekSnapshotGuardTests.cs`.
- `Application/UseCases/SettleGameweekUseCaseTests.cs`, `GetGameweeksUseCaseTests.cs`, `GetMyGameweekScoresUseCaseTests.cs`.

---

# PHASE 1 — Calendar, config, read endpoints

## Task 1: Domain — `GameweekConfig`

**Files:**
- Create: `Ez.Handball.Domain/GameweekConfig.cs`

- [ ] **Step 1: Create the record**

```csharp
namespace Ez.Handball.Domain;

// The per-season fantasy calendar config (Config group "fantasy-gameweek-v{Version}").
// TournamentId names the tournament whose HSÍ rounds become gameweeks. LockOffsetHours is
// how many hours before the first throw-off a gameweek's deadline falls. The two version
// fields point at which ScoringRuleSet (#27) and LineupConstraints (#61) the rollup uses.
public sealed record GameweekConfig(
    int Version,
    string TournamentId,
    double LockOffsetHours,
    int ScoringRuleSetVersion,
    int LineupConstraintsVersion);
```

- [ ] **Step 2: Build**

Run: `dotnet build Ez.Handball.Domain/Ez.Handball.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Domain/GameweekConfig.cs
git commit -m "feat: add GameweekConfig domain record (Backend#60)"
```

## Task 2: Domain — `Gameweek` calendar view

**Files:**
- Create: `Ez.Handball.Domain/Gameweek.cs`

- [ ] **Step 1: Create the records + enum**

```csharp
namespace Ez.Handball.Domain;

// Lifecycle of a gameweek, derived from the clock + member-match results (see GameweekCalendarService).
// "Settled" here means all member matches are final (results complete); the per-team scoring rollup
// is driven separately and per-team scores appear once it has run.
public enum GameweekStatus
{
    Open,            // now < deadline
    DeadlineLocked,  // now >= deadline, no member match final yet
    InPlay,          // some but not all member matches final
    Settled          // all member matches final
}

// One fixture inside a gameweek. IsFinal mirrors MatchEntity.Status == "S".
public sealed record GameweekMatch(
    string MatchId,
    DateTimeOffset Date,
    bool IsFinal,
    string HomeTeamId,
    string AwayTeamId);

// A derived gameweek. Number is the 1-based ordinal of the round in sorted order.
// RoundLabel is the HSÍ round label and the stable key (gwKey) for all persisted gameweek state.
public sealed record Gameweek(
    int Number,
    string RoundLabel,
    string TournamentId,
    DateTimeOffset Deadline,
    GameweekStatus Status,
    IReadOnlyList<GameweekMatch> Matches);
```

- [ ] **Step 2: Build**

Run: `dotnet build Ez.Handball.Domain/Ez.Handball.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Domain/Gameweek.cs
git commit -m "feat: add Gameweek calendar domain records (Backend#60)"
```

## Task 3: Abstractions — config + lock repositories

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IGameweekConfigRepository.cs`
- Create: `Ez.Handball.Application/Abstractions/IGameweekLockRepository.cs`

- [ ] **Step 1: Create `IGameweekConfigRepository`**

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameweekConfigRepository
{
    // Reads the fantasy-gameweek-v{version} Config group; null if it doesn't exist or is incomplete.
    Task<GameweekConfig?> GetAsync(int version, CancellationToken ct);
}
```

- [ ] **Step 2: Create `IGameweekLockRepository`**

```csharp
namespace Ez.Handball.Application.Abstractions;

// Pins a gameweek's deadline the first time it is observed as passed, so a later fixture
// reschedule cannot move an already-passed deadline. PartitionKey = tournamentId, RowKey = roundLabel.
public interface IGameweekLockRepository
{
    Task<DateTimeOffset?> GetPinnedDeadlineAsync(string tournamentId, string roundLabel, CancellationToken ct);

    // Idempotent: writing an already-pinned (tournamentId, roundLabel) is a no-op overwrite.
    Task PinAsync(string tournamentId, string roundLabel, DateTimeOffset deadline, DateTimeOffset lockedAt, CancellationToken ct);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Ez.Handball.Application/Ez.Handball.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IGameweekConfigRepository.cs Ez.Handball.Application/Abstractions/IGameweekLockRepository.cs
git commit -m "feat: add gameweek config + lock repository abstractions (Backend#60)"
```

## Task 4: `GameweekCalendarService` — derive the calendar

**Files:**
- Create: `Ez.Handball.Application/Services/GameweekCalendarService.cs`
- Test: `Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs`

This service groups the configured tournament's matches by HSÍ round label, computes each gameweek's deadline (earliest throw-off − offset, overridden by a pinned value), and derives status from the clock + member-match results. It is a pure read — it does **not** pin deadlines (pinning happens in the snapshot guard and settlement, Phases 2–3).

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekCalendarServiceTests
{
    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IGameweekLockRepository> _locks = new();
    private DateTimeOffset _now = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private GameweekCalendarService CreateSut() => new(_matches.Object, _locks.Object, () => _now);

    private static readonly GameweekConfig Config = new(
        Version: 1, TournamentId: "8444", LockOffsetHours: 1,
        ScoringRuleSetVersion: 1, LineupConstraintsVersion: 1);

    private static MatchListItem M(string id, string round, DateTimeOffset date, string status) =>
        new(id, round, date, Venue: null, status,
            Home: new MatchListTeam($"h{id}", "1", "Home", null, 0),
            Away: new MatchListTeam($"a{id}", "2", "Away", null, 0));

    private void SetupMatches(params MatchListItem[] items) =>
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TournamentMatches("8444", "Olís deild karla", "2025-26", items));

    [Fact]
    public async Task UnknownTournament_ReturnsNull()
    {
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentMatches?)null);

        Assert.Null(await CreateSut().GetCalendarAsync(Config, default));
    }

    [Fact]
    public async Task GroupsByRound_NumbersInSortedOrder_DeadlineIsEarliestMinusOffset()
    {
        var r2a = new DateTimeOffset(2026, 1, 20, 18, 0, 0, TimeSpan.Zero);
        var r1a = new DateTimeOffset(2026, 1, 13, 19, 0, 0, TimeSpan.Zero);
        var r1b = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero); // earliest in round 1
        SetupMatches(
            M("103", "2", r2a, "O"),
            M("101", "1", r1a, "O"),
            M("102", "1", r1b, "O"));

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.NotNull(cal);
        Assert.Equal(2, cal!.Count);
        Assert.Equal(1, cal[0].Number);
        Assert.Equal("1", cal[0].RoundLabel);
        Assert.Equal(r1b.AddHours(-1), cal[0].Deadline);   // earliest throw-off − 1h
        Assert.Equal(2, cal[1].Number);
        Assert.Equal("2", cal[1].RoundLabel);
    }

    [Fact]
    public async Task PinnedDeadline_OverridesDerived()
    {
        var date = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero);
        var pinned = new DateTimeOffset(2026, 1, 13, 15, 30, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", date, "O"));
        _locks.Setup(l => l.GetPinnedDeadlineAsync("8444", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pinned);

        var cal = await CreateSut().GetCalendarAsync(Config, default);

        Assert.Equal(pinned, cal![0].Deadline);
    }

    [Fact]
    public async Task Status_Open_When_NowBeforeDeadline()
    {
        SetupMatches(M("101", "1", _now.AddDays(2), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.Open, cal![0].Status);
    }

    [Fact]
    public async Task Status_DeadlineLocked_When_PastDeadline_NoneFinal()
    {
        SetupMatches(M("101", "1", _now.AddHours(2), "O")); // deadline = now+1h, already passed at now? no
        _now = new DateTimeOffset(2026, 1, 13, 17, 0, 0, TimeSpan.Zero);
        SetupMatches(M("101", "1", _now.AddMinutes(30), "O")); // deadline = (now+30m)-1h = now-30m → passed
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.DeadlineLocked, cal![0].Status);
    }

    [Fact]
    public async Task Status_InPlay_When_SomeButNotAllFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        SetupMatches(
            M("101", "1", _now.AddDays(-1), "S"),
            M("102", "1", _now.AddHours(-1), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.InPlay, cal![0].Status);
    }

    [Fact]
    public async Task Status_Settled_When_AllFinal()
    {
        _now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        SetupMatches(
            M("101", "1", _now.AddDays(-1), "S"),
            M("102", "1", _now.AddDays(-1), "S"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal(GameweekStatus.Settled, cal![0].Status);
    }

    [Fact]
    public async Task NonNumericRounds_SortAfterNumeric()
    {
        SetupMatches(
            M("201", "Undanúrslit", _now.AddDays(30), "O"),
            M("101", "1", _now.AddDays(2), "O"));
        var cal = await CreateSut().GetCalendarAsync(Config, default);
        Assert.Equal("1", cal![0].RoundLabel);
        Assert.Equal("Undanúrslit", cal[1].RoundLabel);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekCalendarServiceTests"`
Expected: FAIL — `GameweekCalendarService` does not exist (compile error).

- [ ] **Step 3: Implement the service**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IGameweekCalendarService
{
    // The derived gameweek calendar for the configured fantasy tournament, ordered by number.
    // Null when the tournament id is unknown (mirrors IMatchRepository.ListByTournamentAsync).
    Task<IReadOnlyList<Gameweek>?> GetCalendarAsync(GameweekConfig config, CancellationToken ct);
}

public sealed class GameweekCalendarService : IGameweekCalendarService
{
    private readonly IMatchRepository _matches;
    private readonly IGameweekLockRepository _locks;
    private readonly Func<DateTimeOffset> _now;

    public GameweekCalendarService(
        IMatchRepository matches, IGameweekLockRepository locks, Func<DateTimeOffset> now)
    {
        _matches = matches;
        _locks = locks;
        _now = now;
    }

    public async Task<IReadOnlyList<Gameweek>?> GetCalendarAsync(GameweekConfig config, CancellationToken ct)
    {
        var data = await _matches.ListByTournamentAsync(config.TournamentId, ct);
        if (data is null) return null;

        var now = _now();
        var offset = TimeSpan.FromHours(config.LockOffsetHours);

        var ordered = data.Matches
            .GroupBy(m => m.Round)
            .OrderBy(g => RoundSortKey(g.Key).Bucket)
            .ThenBy(g => RoundSortKey(g.Key).Value)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<Gameweek>(ordered.Count);
        var number = 1;
        foreach (var group in ordered)
        {
            var roundLabel = group.Key;
            var members = group
                .Select(m => new GameweekMatch(m.MatchId, m.Date, IsFinal(m.Status), m.Home.TeamId, m.Away.TeamId))
                .OrderBy(m => m.Date)
                .ToList();

            var derived = members.Min(m => m.Date) - offset;
            var pinned = await _locks.GetPinnedDeadlineAsync(config.TournamentId, roundLabel, ct);
            var deadline = pinned ?? derived;

            result.Add(new Gameweek(
                number++, roundLabel, config.TournamentId, deadline,
                ComputeStatus(now, deadline, members), members));
        }

        return result;
    }

    private static bool IsFinal(string status) => status == "S";

    private static GameweekStatus ComputeStatus(
        DateTimeOffset now, DateTimeOffset deadline, IReadOnlyList<GameweekMatch> members)
    {
        if (now < deadline) return GameweekStatus.Open;
        var finalCount = members.Count(m => m.IsFinal);
        if (finalCount == 0) return GameweekStatus.DeadlineLocked;
        if (finalCount < members.Count) return GameweekStatus.InPlay;
        return GameweekStatus.Settled;
    }

    // Numeric rounds first (ascending), text rounds (playoffs/finals) last — mirrors GetRoundsUseCase.
    private static (int Bucket, int Value) RoundSortKey(string round)
        => int.TryParse(round, out var n) ? (0, n) : (1, 0);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekCalendarServiceTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekCalendarService.cs Ez.Handball.Tests/Application/Services/GameweekCalendarServiceTests.cs
git commit -m "feat: derive gameweek calendar from round labels (Backend#60)"
```

## Task 5: Table entity + repo — `GameweekLockEntity` / `TableGameweekLockRepository`

**Files:**
- Create: `Ez.Handball.Shared/Entities/GameweekLockEntity.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableGameweekLockRepository.cs`
- Modify: `Ez.Handball.Infrastructure/Tables.cs`

- [ ] **Step 1: Add the table constant**

In `Ez.Handball.Infrastructure/Tables.cs`, add inside the `Tables` class alongside the other `Game*` constants:

```csharp
    public const string GameweekLocks = "GameweekLocks";
    public const string GameweekLineups = "GameweekLineups";
    public const string GameweekScores = "GameweekScores";
```

- [ ] **Step 2: Create the entity**

```csharp
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Pins a gameweek's deadline the first time it locks. PartitionKey = tournamentId, RowKey = roundLabel.
public sealed class GameweekLockEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // tournamentId
    public string RowKey { get; set; } = string.Empty;       // roundLabel (gwKey)
    public DateTimeOffset PinnedDeadline { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

- [ ] **Step 3: Create the repository**

```csharp
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekLockRepository : IGameweekLockRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekLockRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<DateTimeOffset?> GetPinnedDeadlineAsync(
        string tournamentId, string roundLabel, CancellationToken ct)
    {
        var filter = $"PartitionKey eq '{ODataFilter.Escape(tournamentId)}' and RowKey eq '{ODataFilter.Escape(roundLabel)}'";
        await foreach (var e in _query.QueryAsync<GameweekLockEntity>(Tables.GameweekLocks, filter, ct))
            return e.PinnedDeadline;
        return null;
    }

    public async Task PinAsync(
        string tournamentId, string roundLabel, DateTimeOffset deadline, DateTimeOffset lockedAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekLocks);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        // First-write-wins: only pin if no row exists yet, so a passed deadline can't shift later.
        var existing = await GetPinnedDeadlineAsync(tournamentId, roundLabel, ct);
        if (existing is not null) return;

        await table.UpsertEntityAsync(new GameweekLockEntity
        {
            PartitionKey = tournamentId,
            RowKey = roundLabel,
            PinnedDeadline = deadline,
            LockedAt = lockedAt
        }, TableUpdateMode.Replace, ct);
    }
}
```

- [ ] **Step 4: Register the repo**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, add alongside the other `AddScoped` registrations:

```csharp
        services.AddScoped<IGameweekConfigRepository, TableGameweekConfigRepository>();
        services.AddScoped<IGameweekLockRepository, TableGameweekLockRepository>();
```

(`TableGameweekConfigRepository` is created in Task 6; add both lines now and the build passes after Task 6. If building between tasks, add only the lock line here and the config line in Task 6.)

- [ ] **Step 5: Build**

Run: `dotnet build Ez.Handball.Infrastructure/Ez.Handball.Infrastructure.csproj`
Expected: Build succeeded (after Task 6 if you added both registration lines).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Shared/Entities/GameweekLockEntity.cs Ez.Handball.Infrastructure/TableAccess/TableGameweekLockRepository.cs Ez.Handball.Infrastructure/Tables.cs
git commit -m "feat: persist pinned gameweek deadlines (Backend#60)"
```

## Task 6: Config repo — `TableGameweekConfigRepository`

**Files:**
- Create: `Ez.Handball.Infrastructure/TableAccess/TableGameweekConfigRepository.cs`

Reads the `fantasy-gameweek-v{version}` Config group. Mirrors `TableLineupConstraintsRepository`'s parse-rows-into-a-dict pattern.

- [ ] **Step 1: Implement**

```csharp
using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekConfigRepository : IGameweekConfigRepository
{
    private readonly ITableQuery _query;

    public TableGameweekConfigRepository(ITableQuery query) => _query = query;

    public async Task<GameweekConfig?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-gameweek-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;
        if (!values.TryGetValue("tournamentId", out var tournamentId) || string.IsNullOrWhiteSpace(tournamentId))
            return null;

        var lockOffsetHours = GetDouble(values, "lockOffsetHours", 1);
        var scoringVersion = GetInt(values, "scoringRuleSetVersion", 1);
        var lineupVersion = GetInt(values, "lineupConstraintsVersion", 1);

        return new GameweekConfig(version, tournamentId, lockOffsetHours, scoringVersion, lineupVersion);
    }

    private static double GetDouble(IReadOnlyDictionary<string, string> values, string key, double fallback)
        => values.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
```

- [ ] **Step 2: Ensure registration** (from Task 5 Step 4 — confirm both lines present in `InfrastructureRegistration.cs`).

- [ ] **Step 3: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Infrastructure/TableAccess/TableGameweekConfigRepository.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs
git commit -m "feat: read gameweek config from Config table (Backend#60)"
```

## Task 7: Seed function — `SeedGameweekConfigFunction`

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/SeedGameweekConfigFunction.cs`

Mirrors `SeedLineupConstraintsFunction` exactly (idempotent `Replace` upserts via `ITableWriter`).

- [ ] **Step 1: Implement**

```csharp
using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedGameweekConfigFunction
{
    // The fantasy calendar config. tournamentId names the tournament whose HSÍ rounds become
    // gameweeks; lockOffsetHours is how far before first throw-off a gameweek locks (owner-tunable);
    // the version keys point at which scoring rule set (#27) and lineup constraints (#61) the rollup uses.
    // PLACEHOLDER tournamentId 8444 (Olís deild karla) — owner must confirm per season/environment.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConfigDefinitions =
    [
        ("fantasy-gameweek-v1", "tournamentId",             "8444"),
        ("fantasy-gameweek-v1", "lockOffsetHours",          "1"),
        ("fantasy-gameweek-v1", "scoringRuleSetVersion",    "1"),
        ("fantasy-gameweek-v1", "lineupConstraintsVersion", "1"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedGameweekConfigFunction(ITableWriter tableWriter) => _tableWriter = tableWriter;

    [Function("SeedGameweekConfig")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/gameweek-config")] HttpRequestData req,
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

- [ ] **Step 2: Build**

Run: `dotnet build Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/SeedGameweekConfigFunction.cs
git commit -m "feat: seed fantasy gameweek config (Backend#60)"
```

## Task 8: `GetGameweeksUseCase` + `GetCurrentGameweekUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetGameweeksUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetGameweeksUseCaseTests.cs`

Both use cases load the config (default version 1), then call the calendar service. `Current` picks the earliest gameweek whose deadline has not passed, plus the most recent gameweek that is `Settled`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetGameweeksUseCaseTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();

    private GetGameweeksUseCase CreateSut() => new(_config.Object, _calendar.Object);
    private GetCurrentGameweekUseCase CreateCurrentSut() => new(_config.Object, _calendar.Object);

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);

    private static Gameweek GW(int n, GameweekStatus status, DateTimeOffset deadline) =>
        new(n, n.ToString(), "8444", deadline, status, Array.Empty<GameweekMatch>());

    private void Setup(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(gws);
    }

    [Fact]
    public async Task NoConfig_ReturnsConfigMissing()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);
        var result = await CreateSut().ExecuteAsync(null, default);
        Assert.IsType<GetGameweeksResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task UnknownTournament_ReturnsNotFound()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Gameweek>?)null);
        var result = await CreateSut().ExecuteAsync(null, default);
        Assert.IsType<GetGameweeksResult.NotFound>(result);
    }

    [Fact]
    public async Task ReturnsCalendar()
    {
        var t = new DateTimeOffset(2026, 1, 13, 0, 0, 0, TimeSpan.Zero);
        Setup(GW(1, GameweekStatus.Settled, t), GW(2, GameweekStatus.Open, t.AddDays(7)));
        var result = await CreateSut().ExecuteAsync(null, default);
        var found = Assert.IsType<GetGameweeksResult.Found>(result);
        Assert.Equal(2, found.Gameweeks.Count);
    }

    [Fact]
    public async Task Current_PicksEarliestNonOpenPassed_AndLastSettled()
    {
        var t = new DateTimeOffset(2026, 1, 13, 0, 0, 0, TimeSpan.Zero);
        Setup(
            GW(1, GameweekStatus.Settled, t),
            GW(2, GameweekStatus.InPlay, t.AddDays(7)),
            GW(3, GameweekStatus.Open, t.AddDays(14)),
            GW(4, GameweekStatus.Open, t.AddDays(21)));

        var result = await CreateCurrentSut().ExecuteAsync(null, default);
        var found = Assert.IsType<GetCurrentGameweekResult.Found>(result);
        Assert.Equal(3, found.Current!.Number);          // earliest Open = the editable one
        Assert.Equal(1, found.LastSettled!.Number);       // most recent Settled
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetGameweeksUseCaseTests"`
Expected: FAIL — use cases not defined.

- [ ] **Step 3: Implement both use cases**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetGameweeksResult
{
    public sealed record ConfigMissing : GetGameweeksResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : GetGameweeksResult { public static readonly NotFound Instance = new(); }
    public sealed record Found(IReadOnlyList<Gameweek> Gameweeks) : GetGameweeksResult;
}

public interface IGetGameweeksUseCase
{
    Task<GetGameweeksResult> ExecuteAsync(int? configVersion, CancellationToken ct);
}

public sealed class GetGameweeksUseCase : IGetGameweeksUseCase
{
    private const int DefaultVersion = 1;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;

    public GetGameweeksUseCase(IGameweekConfigRepository config, IGameweekCalendarService calendar)
    {
        _config = config;
        _calendar = calendar;
    }

    public async Task<GetGameweeksResult> ExecuteAsync(int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return GetGameweeksResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return GetGameweeksResult.NotFound.Instance;

        return new GetGameweeksResult.Found(calendar);
    }
}

public abstract record GetCurrentGameweekResult
{
    public sealed record ConfigMissing : GetCurrentGameweekResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : GetCurrentGameweekResult { public static readonly NotFound Instance = new(); }
    // Current is null only if every gameweek has passed its deadline; LastSettled is null pre-season.
    public sealed record Found(Gameweek? Current, Gameweek? LastSettled) : GetCurrentGameweekResult;
}

public interface IGetCurrentGameweekUseCase
{
    Task<GetCurrentGameweekResult> ExecuteAsync(int? configVersion, CancellationToken ct);
}

public sealed class GetCurrentGameweekUseCase : IGetCurrentGameweekUseCase
{
    private const int DefaultVersion = 1;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;

    public GetCurrentGameweekUseCase(IGameweekConfigRepository config, IGameweekCalendarService calendar)
    {
        _config = config;
        _calendar = calendar;
    }

    public async Task<GetCurrentGameweekResult> ExecuteAsync(int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return GetCurrentGameweekResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return GetCurrentGameweekResult.NotFound.Instance;

        // Editable gameweek = earliest still Open (deadline not passed).
        var current = calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open);
        var lastSettled = calendar.LastOrDefault(g => g.Status == GameweekStatus.Settled);
        return new GetCurrentGameweekResult.Found(current, lastSettled);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetGameweeksUseCaseTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetGameweeksUseCase.cs Ez.Handball.Tests/Application/UseCases/GetGameweeksUseCaseTests.cs
git commit -m "feat: gameweek calendar + current read use cases (Backend#60)"
```

## Task 9: Public read endpoints + DI wiring

**Files:**
- Create: `Ez.Handball.Api/GameweekEndpoints.cs`
- Modify: `Ez.Handball.Api/Program.cs`

- [ ] **Step 1: Create the endpoints file (calendar + current; `/users/me/gameweeks` is added in Phase 2)**

```csharp
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public static class GameweekEndpoints
{
    public static void MapGameweekEndpoints(this WebApplication app)
    {
        app.MapGet("/api/gameweeks", async (
            int? version, IGetGameweeksUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(version, ct);
            return result switch
            {
                GetGameweeksResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
                GetGameweeksResult.NotFound      => Results.NotFound(new { error = "tournament_not_found" }),
                GetGameweeksResult.Found f       => Results.Ok(f.Gameweeks.Select(Body)),
                _                                => Results.Problem()
            };
        });

        app.MapGet("/api/gameweeks/current", async (
            int? version, IGetCurrentGameweekUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(version, ct);
            return result switch
            {
                GetCurrentGameweekResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
                GetCurrentGameweekResult.NotFound      => Results.NotFound(new { error = "tournament_not_found" }),
                GetCurrentGameweekResult.Found f       => Results.Ok(new
                {
                    current = f.Current is null ? null : Body(f.Current),
                    lastSettled = f.LastSettled is null ? null : Body(f.LastSettled)
                }),
                _ => Results.Problem()
            };
        });
    }

    internal static object Body(Gameweek g) => new
    {
        number = g.Number,
        roundLabel = g.RoundLabel,
        tournamentId = g.TournamentId,
        deadline = g.Deadline,
        status = g.Status.ToString(),
        matches = g.Matches.Select(m => new
        {
            matchId = m.MatchId,
            date = m.Date,
            isFinal = m.IsFinal,
            homeTeamId = m.HomeTeamId,
            awayTeamId = m.AwayTeamId
        })
    };
}
```

- [ ] **Step 2: Register services + use cases + map endpoints in `Program.cs`**

Add to the `AddScoped` block (near the other gameweek-adjacent registrations such as `IGetRoundsUseCase`):

```csharp
builder.Services.AddScoped<IGameweekCalendarService, GameweekCalendarService>();
builder.Services.AddScoped<IGetGameweeksUseCase, GetGameweeksUseCase>();
builder.Services.AddScoped<IGetCurrentGameweekUseCase, GetCurrentGameweekUseCase>();
```

Add the endpoint mapping alongside the other `app.Map*Endpoints()` calls:

```csharp
app.MapGameweekEndpoints();
```

(`GameweekCalendarService` lives in `Ez.Handball.Application.Services`; that namespace is already imported in `Program.cs`.)

- [ ] **Step 3: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke test**

Start Azurite + the Api host, seed config, then:
```bash
curl -s "http://localhost:5xxx/api/gameweeks" | head
```
Expected: `gameweek_config_missing` before seeding `fantasy-gameweek-v1`; a JSON array of gameweeks once seeded and matches exist for tournament 8444.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/GameweekEndpoints.cs Ez.Handball.Api/Program.cs
git commit -m "feat: public gameweek calendar + current endpoints (Backend#60)"
```

**End of Phase 1 — a read-only gameweek calendar with derived status ships here.**

---

# PHASE 2 — Scoring rollup + settlement

## Task 10: Domain — `GameweekScore`

**Files:**
- Create: `Ez.Handball.Domain/GameweekScore.cs`

- [ ] **Step 1: Create the records**

```csharp
namespace Ez.Handball.Domain;

// One player's contribution to a gameweek score. Played = appeared in a member match.
// AutoSubbedIn = a bench player promoted because a starter didn't play. Multiplier is the factor
// applied to this player's raw points (2.0 for the effective captain, else 1.0).
public sealed record GameweekPlayerScore(
    string PlayerId,
    double RawPoints,
    double Points,          // RawPoints * Multiplier, and 0 for a non-playing unsubbed starter
    bool Played,
    bool AutoSubbedIn,
    bool CaptainApplied,
    double Multiplier);

// A settled gameweek score for one team. Points = Σ Breakdown.Points over effective starters.
public sealed record GameweekScore(
    string TeamId,
    string RoundLabel,
    double Points,
    string? CaptainPlayerId,    // the effective captain (vice if the chosen captain didn't play)
    IReadOnlyList<GameweekPlayerScore> Breakdown);
```

- [ ] **Step 2: Build**

Run: `dotnet build Ez.Handball.Domain/Ez.Handball.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Domain/GameweekScore.cs
git commit -m "feat: add GameweekScore domain records (Backend#60)"
```

## Task 11: `GameweekScoringService` — rollup + auto-subs

**Files:**
- Create: `Ez.Handball.Application/Services/GameweekScoringService.cs`
- Test: `Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs`

Pure service (no I/O). Inputs: the frozen snapshot, the owned squad (for positions), a `playerId → AggregatedStats` map of who played in this gameweek's matches (absent = did not play), the `ScoringRuleSet`, and `LineupConstraints`. It reuses `FantasyPlayerRatingFunction` for per-player points (no parallel formula), applies FPL-style position-valid auto-subs via `LineupValidator`, and applies the captain/vice multiplier.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekScoringServiceTests
{
    // 1 point per goal, 1 per appearance, no card penalties — keeps arithmetic obvious.
    private static readonly ScoringRuleSet Rules =
        new(GameFlavor.Fantasy, 1, GoalPoints: 1, YellowCardPoints: 0, TwoMinutePoints: 0, RedCardPoints: 0, AppearancePoints: 1);

    private static readonly LineupConstraints Constraints = new(
        Version: 1, StarterCount: 2,
        PositionStart: new Dictionary<string, (int Min, int Max)>
        {
            ["GK"] = (1, 1),
            ["FP"] = (1, 2),
        },
        CaptainMultiplier: 2, CaptainRequired: true, ViceRequired: false);

    private GameweekScoringService CreateSut() => new(new FantasyPlayerRatingFunction());

    private static SquadPlayer Owned(string id, string pos) =>
        new(id, $"name-{id}", "1", "Club", pos, "karlar",
            new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), 0);

    private static AggregatedStats Played(int goals) => new(Games: 1, Goals: goals, 0, 0, 0);

    // A 2-starter (GK + FP) + 1-bench lineup: captain is the FP starter, bench is an FP.
    private static Lineup Snapshot() => new(new[]
    {
        new LineupSlot("gk1", LineupRole.Starter, null),
        new LineupSlot("fp1", LineupRole.Captain, null),
        new LineupSlot("fp2", LineupRole.Bench, 0),
    });

    private static IReadOnlyList<SquadPlayer> Squad() => new[]
    {
        Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("fp2", "FP"),
    };

    [Fact]
    public void AllPlayed_CaptainDoubled()
    {
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk1"] = Played(0), // 1 (appearance)
            ["fp1"] = Played(3), // 4 raw → captain ×2 = 8
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        Assert.Equal("fp1", score.CaptainPlayerId);
        Assert.Equal(1 + 8, score.Points);
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp1").CaptainApplied);
    }

    [Fact]
    public void NonPlayingStarter_AutoSubbedByValidBench()
    {
        // fp1 (captain starter) didn't play; bench fp2 played and is an FP → valid promotion.
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk1"] = Played(0), // 1
            ["fp2"] = Played(2), // 3 raw, promoted into the FP slot
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        var fp2 = score.Breakdown.Single(b => b.PlayerId == "fp2");
        Assert.True(fp2.AutoSubbedIn);
        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "fp1").Points); // benched starter scores 0
        // captain didn't play and vice not set → no multiplier applied anywhere
        Assert.Equal(1 + 3, score.Points);
    }

    [Fact]
    public void CaptainDidNotPlay_ViceInheritsMultiplier()
    {
        var snapshot = new Lineup(new[]
        {
            new LineupSlot("gk1", LineupRole.Captain, null),
            new LineupSlot("fp1", LineupRole.Vice, null),
            new LineupSlot("fp2", LineupRole.Bench, 0),
        });
        var stats = new Dictionary<string, AggregatedStats>
        {
            // gk1 (captain) did NOT play; fp1 (vice) played → vice gets ×2.
            ["fp1"] = Played(3), // 4 raw × 2 = 8
            ["fp2"] = Played(0), // bench promoted for gk1? gk1 is GK; fp2 is FP → invalid GK sub, gk1 scores 0
        };

        var score = CreateSut().Score("team", "1", snapshot, Squad(), stats, Rules, Constraints);

        Assert.Equal("fp1", score.CaptainPlayerId); // vice became effective captain
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp1").CaptainApplied);
        // gk1 (GK) couldn't be subbed by an FP bench → 0; total = vice 8 only
        Assert.Equal(8, score.Points);
    }

    [Fact]
    public void GkOnlyReplacedByGk_NoEligibleSub_ScoresZero()
    {
        // gk1 didn't play; only bench is fp2 (FP) → promoting it would break the exactly-1-GK rule.
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["fp1"] = Played(2), // 3, captain ×2 = 6
            ["fp2"] = Played(5), // not eligible to cover a GK
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "gk1").Points);
        Assert.False(score.Breakdown.Single(b => b.PlayerId == "fp2").AutoSubbedIn);
        Assert.Equal(6, score.Points);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests"`
Expected: FAIL — `GameweekScoringService` not defined.

- [ ] **Step 3: Implement the service**

```csharp
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IGameweekScoringService
{
    // Pure rollup: applies auto-subs and the captain/vice multiplier to a frozen snapshot,
    // given who played (playedStatsByPlayer; absent key = did not play).
    GameweekScore Score(
        string teamId,
        string roundLabel,
        Lineup snapshot,
        IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet,
        LineupConstraints constraints);
}

public sealed class GameweekScoringService : IGameweekScoringService
{
    private readonly FantasyPlayerRatingFunction _rating;

    public GameweekScoringService(FantasyPlayerRatingFunction rating) => _rating = rating;

    public GameweekScore Score(
        string teamId, string roundLabel, Lineup snapshot, IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet, LineupConstraints constraints)
    {
        bool Played(string id) => playedStatsByPlayer.ContainsKey(id);

        double RawPoints(string id) => playedStatsByPlayer.TryGetValue(id, out var s)
            ? _rating.Compute(new PlayerRatingInputs(id, s, ruleSet,
                new PlayerRatingContext(null, null, null, ruleSet.Version, null, null))).Rating
            : 0;

        var starters = snapshot.Slots
            .Where(s => s.Role is LineupRole.Starter or LineupRole.Captain or LineupRole.Vice)
            .ToList();
        var bench = snapshot.Slots
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .ToList();

        // The set of player ids actually counted (effective starters). Begin with all frozen
        // starters who played; non-playing starters are replaced where a valid bench sub exists.
        var effective = new List<(string PlayerId, bool SubbedIn)>();
        var usedBench = new HashSet<string>();

        foreach (var starter in starters)
        {
            if (Played(starter.PlayerId))
            {
                effective.Add((starter.PlayerId, false));
                continue;
            }

            var sub = FindValidSub(effective, bench, usedBench, ownedSquad, constraints, Played);
            if (sub is not null)
            {
                usedBench.Add(sub);
                effective.Add((sub, true));
            }
            else
            {
                // No eligible sub: the slot stays with the non-playing starter and scores 0.
                effective.Add((starter.PlayerId, false));
            }
        }

        // Effective captain: the chosen captain if they are an effective (playing) starter,
        // else the vice if they are, else nobody.
        var captainId = EffectiveArmband(starters, effective, LineupRole.Captain, snapshot)
            ?? EffectiveArmband(starters, effective, LineupRole.Vice, snapshot);

        var breakdown = new List<GameweekPlayerScore>();
        double total = 0;
        foreach (var (playerId, subbedIn) in effective)
        {
            var played = Played(playerId);
            var raw = RawPoints(playerId);
            var isCaptain = playerId == captainId && played;
            var multiplier = isCaptain ? constraints.CaptainMultiplier : 1.0;
            var points = played ? raw * multiplier : 0;
            total += points;
            breakdown.Add(new GameweekPlayerScore(playerId, raw, points, played, subbedIn, isCaptain, multiplier));
        }

        return new GameweekScore(teamId, roundLabel, total, captainId, breakdown);
    }

    // A bench player is a valid sub for a non-playing starter if they played, are unused, and
    // replacing the starter with them keeps the whole effective lineup position-valid.
    private static string? FindValidSub(
        IReadOnlyList<(string PlayerId, bool SubbedIn)> effectiveSoFar,
        IReadOnlyList<LineupSlot> bench,
        HashSet<string> usedBench,
        IReadOnlyList<SquadPlayer> ownedSquad,
        LineupConstraints constraints,
        Func<string, bool> played)
    {
        foreach (var b in bench)
        {
            if (usedBench.Contains(b.PlayerId) || !played(b.PlayerId)) continue;
            if (KeepsPositionsValid(b.PlayerId, effectiveSoFar, ownedSquad, constraints))
                return b.PlayerId;
        }
        return null;
    }

    // The candidate keeps positions valid if adding it to the already-decided effective starters
    // does not push any position over its max. The GK-only-replaced-by-GK rule falls out of GK max = 1.
    private static bool KeepsPositionsValid(
        string candidateId,
        IReadOnlyList<(string PlayerId, bool SubbedIn)> effectiveSoFar,
        IReadOnlyList<SquadPlayer> ownedSquad,
        LineupConstraints constraints)
    {
        var posById = ownedSquad.ToDictionary(p => p.PlayerId, p => p.Position);

        // The final starting set = everyone already locked in as effective (those before this slot)
        // + the candidate for this slot. We validate the candidate's position against the max only,
        // counting the candidate plus all already-decided effective starters.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string id)
        {
            if (posById.TryGetValue(id, out var pos) && pos is not null)
                counts[pos] = counts.TryGetValue(pos, out var c) ? c + 1 : 1;
        }

        foreach (var (pid, _) in effectiveSoFar) Add(pid);
        Add(candidateId);

        foreach (var kv in constraints.PositionStart)
        {
            counts.TryGetValue(kv.Key, out var count);
            if (count > kv.Value.Max) return false; // candidate would exceed this position's cap
        }
        return true;
    }

    private static string? EffectiveArmband(
        IReadOnlyList<LineupSlot> starters,
        IReadOnlyList<(string PlayerId, bool SubbedIn)> effective,
        LineupRole role,
        Lineup snapshot)
    {
        var holder = snapshot.Slots.FirstOrDefault(s => s.Role == role)?.PlayerId;
        if (holder is null) return null;
        // Armband only counts if the holder is an effective (playing) starter — i.e. not subbed out.
        return effective.Any(e => e.PlayerId == holder) ? holder : null;
    }
}
```

> **Implementation note:** the position check only needs the already-decided effective set plus the candidate — the remaining undecided starters are evaluated in their own iterations. This is a greedy per-slot promotion that matches FPL behaviour for a 7-a-side lineup; the GK-only-replaced-by-GK rule falls out of the position max (GK max = 1). If you find the greedy check insufficient for a tricky multi-sub case during implementation, prefer fixing the test first to express the exact expectation, then adjust — do not loosen the position rules.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekScoringServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Services/GameweekScoringService.cs Ez.Handball.Tests/Application/Services/GameweekScoringServiceTests.cs
git commit -m "feat: gameweek scoring rollup with position-valid auto-subs (Backend#60)"
```

## Task 12: Snapshot + score repositories

**Files:**
- Create: `Ez.Handball.Application/Abstractions/IGameweekLineupRepository.cs`
- Create: `Ez.Handball.Application/Abstractions/IGameweekScoreRepository.cs`
- Create: `Ez.Handball.Shared/Entities/GameweekLineupEntity.cs`
- Create: `Ez.Handball.Shared/Entities/GameweekScoreEntity.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableGameweekLineupRepository.cs`
- Create: `Ez.Handball.Infrastructure/TableAccess/TableGameweekScoreRepository.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`

- [ ] **Step 1: Abstractions**

`IGameweekLineupRepository.cs`:
```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// Frozen per-(team, gameweek) lineup snapshot. The gwKey is the round label.
public interface IGameweekLineupRepository
{
    Task<Lineup?> GetSnapshotAsync(string teamId, string roundLabel, CancellationToken ct);
    Task SaveSnapshotAsync(string teamId, string roundLabel, Lineup lineup, CancellationToken ct);
}
```

`IGameweekScoreRepository.cs`:
```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameweekScoreRepository
{
    // Replace-mode upsert → idempotent/recomputable settlement.
    Task SaveAsync(GameweekScore score, CancellationToken ct);
    Task<IReadOnlyList<GameweekScore>> ListByTeamAsync(string teamId, CancellationToken ct);
}
```

- [ ] **Step 2: Entities**

`GameweekLineupEntity.cs` (PartitionKey = `{teamId}|{roundLabel}`, mirrors `GameLineupEntity`):
```csharp
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One frozen lineup slot. PartitionKey = "{teamId}|{roundLabel}", RowKey = playerId.
public sealed class GameweekLineupEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // "{teamId}|{roundLabel}"
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string Role { get; set; } = string.Empty;
    public int? BenchOrder { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

`GameweekScoreEntity.cs`:
```csharp
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One settled score. PartitionKey = teamId, RowKey = roundLabel. BreakdownJson is the serialized
// per-player breakdown (opaque to storage; deserialized by the repository).
public sealed class GameweekScoreEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // roundLabel
    public double Points { get; set; }
    public string? CaptainPlayerId { get; set; }
    public string BreakdownJson { get; set; } = "[]";
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

- [ ] **Step 3: `TableGameweekLineupRepository` (mirror `TableLineupRepository`, composite partition key)**

```csharp
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekLineupRepository : IGameweekLineupRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekLineupRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    private static string Partition(string teamId, string roundLabel) => $"{teamId}|{roundLabel}";

    public async Task<Lineup?> GetSnapshotAsync(string teamId, string roundLabel, CancellationToken ct)
    {
        var pk = Partition(teamId, roundLabel);
        var slots = new List<LineupSlot>();
        await foreach (var e in _query.QueryAsync<GameweekLineupEntity>(
                           Tables.GameweekLineups, $"PartitionKey eq '{ODataFilter.Escape(pk)}'", ct))
        {
            if (Enum.TryParse<LineupRole>(e.Role, out var role))
                slots.Add(new LineupSlot(e.RowKey, role, e.BenchOrder));
        }
        return slots.Count == 0 ? null : new Lineup(slots);
    }

    public async Task SaveSnapshotAsync(string teamId, string roundLabel, Lineup lineup, CancellationToken ct)
    {
        var pk = Partition(teamId, roundLabel);
        var table = _client.GetTableClient(Tables.GameweekLineups);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var actions = lineup.Slots.Select(s => new TableTransactionAction(
            TableTransactionActionType.UpsertReplace,
            new GameweekLineupEntity
            {
                PartitionKey = pk,
                RowKey = s.PlayerId,
                Role = s.Role.ToString(),
                BenchOrder = s.BenchOrder
            })).ToList();

        if (actions.Count > 0)
            await table.SubmitTransactionAsync(actions, ct);
    }
}
```

- [ ] **Step 4: `TableGameweekScoreRepository`**

```csharp
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekScoreRepository : IGameweekScoreRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekScoreRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task SaveAsync(GameweekScore score, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekScores);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        await table.UpsertEntityAsync(new GameweekScoreEntity
        {
            PartitionKey = score.TeamId,
            RowKey = score.RoundLabel,
            Points = score.Points,
            CaptainPlayerId = score.CaptainPlayerId,
            BreakdownJson = JsonSerializer.Serialize(score.Breakdown)
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<GameweekScore>> ListByTeamAsync(string teamId, CancellationToken ct)
    {
        var result = new List<GameweekScore>();
        await foreach (var e in _query.QueryAsync<GameweekScoreEntity>(
                           Tables.GameweekScores, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            var breakdown = string.IsNullOrWhiteSpace(e.BreakdownJson)
                ? Array.Empty<GameweekPlayerScore>()
                : JsonSerializer.Deserialize<GameweekPlayerScore[]>(e.BreakdownJson) ?? Array.Empty<GameweekPlayerScore>();
            result.Add(new GameweekScore(e.PartitionKey, e.RowKey, e.Points, e.CaptainPlayerId, breakdown));
        }
        return result;
    }
}
```

- [ ] **Step 5: Register both repos** in `InfrastructureRegistration.cs`:

```csharp
        services.AddScoped<IGameweekLineupRepository, TableGameweekLineupRepository>();
        services.AddScoped<IGameweekScoreRepository, TableGameweekScoreRepository>();
```

- [ ] **Step 6: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IGameweekLineupRepository.cs Ez.Handball.Application/Abstractions/IGameweekScoreRepository.cs Ez.Handball.Shared/Entities/GameweekLineupEntity.cs Ez.Handball.Shared/Entities/GameweekScoreEntity.cs Ez.Handball.Infrastructure/TableAccess/TableGameweekLineupRepository.cs Ez.Handball.Infrastructure/TableAccess/TableGameweekScoreRepository.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs
git commit -m "feat: persist gameweek snapshots + scores (Backend#60)"
```

## Task 13: `IPlayerStatsRepository.GetByMatchAsync`

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IPlayerStatsRepository.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerStatsRepository.cs`

- [ ] **Step 1: Add the interface method**

Add to `IPlayerStatsRepository`:
```csharp
    // All player stat rows for one match (PlayerStats PartitionKey = matchId).
    Task<IReadOnlyList<PlayerStat>> GetByMatchAsync(string matchId, CancellationToken ct);
```

- [ ] **Step 2: Implement in `TablePlayerStatsRepository`**

Add this method (it queries by `PartitionKey eq matchId`; tournament-name enrichment isn't needed for scoring, so it's left null):
```csharp
    public async Task<IReadOnlyList<PlayerStat>> GetByMatchAsync(string matchId, CancellationToken ct)
    {
        var result = new List<PlayerStat>();
        await foreach (var s in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"PartitionKey eq '{ODataFilter.Escape(matchId)}'", ct))
        {
            result.Add(new PlayerStat(
                MatchId: s.PartitionKey,
                TournamentId: s.TournamentId,
                TournamentName: null,
                Season: s.Season,
                TeamId: s.TeamId,
                ClubName: s.ClubName,
                Goals: s.Goals,
                YellowCards: s.YellowCards,
                TwoMinuteSuspensions: s.TwoMinuteSuspensions,
                RedCards: s.RedCards));
        }
        return result;
    }
```
(In `PlayerStatEntity`, `RowKey` is the playerId — `s.RowKey` is needed by the settlement use case to key by player; it's already available on the entity. `PlayerStat` doesn't carry PlayerId, so the settlement use case will read `s.RowKey` differently — see Task 14, which queries via the repo and keys by a separate lookup. To make PlayerId available, also add it: see Step 3.)

- [ ] **Step 3: Add `PlayerId` to the `PlayerStat` domain record so callers can key by player**

In `Ez.Handball.Domain/PlayerStat.cs`, add `string PlayerId` as the first parameter:
```csharp
public sealed record PlayerStat(
    string PlayerId,
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    string TeamId,
    string? ClubName,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
```
Then update the two construction sites in `TablePlayerStatsRepository` (`GetByPlayerAsync` and the new `GetByMatchAsync`) to pass `PlayerId: s.RowKey` as the first argument. Search for other `new PlayerStat(` usages: `grep -rn "new PlayerStat(" Ez.Handball.Application Ez.Handball.Infrastructure Ez.Handball.Tests` and add `PlayerId:` to each (the player-history aggregation builds `PlayerStat` — pass the playerId it already has in scope).

- [ ] **Step 4: Build + run the existing stats tests**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerStat OR FullyQualifiedName~PlayerHistory"`
Expected: Build succeeded; existing stats/history tests PASS (after adding `PlayerId:` at each construction site).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IPlayerStatsRepository.cs Ez.Handball.Infrastructure/TableAccess/TablePlayerStatsRepository.cs Ez.Handball.Domain/PlayerStat.cs
git commit -m "feat: read player stats by match + carry PlayerId (Backend#60)"
```

## Task 14: `SettleGameweekUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs`

Settles one gameweek (by round label) for one team: guard that all member matches are final, snapshot-if-missing, build the played-stats map from member matches, score, and persist. Idempotent — re-running overwrites with `Replace`. For the test, settlement targets a single team (the team whose lineup we settle); a higher-level loop over all teams with a roster is out of V0 scope and noted below.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SettleGameweekUseCaseTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<ILineupRepository> _liveLineup = new();
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);
    private static readonly ScoringRuleSet Rules =
        new(GameFlavor.Fantasy, 1, 1, 0, 0, 0, 1);
    private static readonly LineupConstraints Constraints = new(
        1, 2, new Dictionary<string, (int, int)> { ["GK"] = (1, 1), ["FP"] = (1, 2) }, 2, true, false);

    private SettleGameweekUseCase CreateSut() => new(
        _config.Object, _calendar.Object, _snapshots.Object, _liveLineup.Object,
        _scores.Object, _squad.Object, _stats.Object, _ruleSets.Object, _constraints.Object,
        new GameweekScoringService(new FantasyPlayerRatingFunction()));

    private static Gameweek GW(string round, GameweekStatus status, params GameweekMatch[] m) =>
        new(1, round, "8444", DateTimeOffset.UnixEpoch, status, m);

    private static GameweekMatch Match(string id, bool final) =>
        new(id, DateTimeOffset.UnixEpoch, final, "h", "a");

    private static Lineup Lineup() => new(new[]
    {
        new LineupSlot("gk1", LineupRole.Starter, null),
        new LineupSlot("fp1", LineupRole.Captain, null),
        new LineupSlot("fp2", LineupRole.Bench, 0),
    });

    private static SquadPlayer Owned(string id, string pos) =>
        new(id, id, "1", "Club", pos, "karlar", new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), 0);

    private void SetupCommon(GameweekStatus status, bool snapshotExists)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { GW("1", status, Match("m1", final: status == GameweekStatus.Settled)) });
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>())).ReturnsAsync(Rules);
        _constraints.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Constraints);
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotExists ? Lineup() : null);
        _liveLineup.Setup(s => s.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync(Lineup());
        _squad.Setup(s => s.ExecuteAsync("user", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(new SquadView(
                new[] { Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("fp2", "FP") },
                new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"))));
        _stats.Setup(s => s.GetByMatchAsync("m1", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new PlayerStat("gk1", "m1", "8444", null, "2025-26", "h", "Club", 0, 0, 0, 0),
            new PlayerStat("fp1", "m1", "8444", null, "2025-26", "h", "Club", 3, 0, 0, 0),
        });
    }

    [Fact]
    public async Task NotAllFinal_ReturnsNotReady_DoesNotPersist()
    {
        SetupCommon(GameweekStatus.InPlay, snapshotExists: true);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { GW("1", GameweekStatus.InPlay, Match("m1", true), Match("m2", false)) });

        var result = await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        Assert.IsType<SettleGameweekResult.NotReady>(result);
        _scores.Verify(s => s.SaveAsync(It.IsAny<GameweekScore>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllFinal_Scores_Persists_CaptainDoubled()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: true);

        var result = await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        var settled = Assert.IsType<SettleGameweekResult.Settled>(result);
        Assert.Equal(1 + (4 * 2), settled.Score.Points); // gk1: 1, fp1: 4 raw × 2 captain
        _scores.Verify(s => s.SaveAsync(
            It.Is<GameweekScore>(g => g.TeamId == "team" && g.RoundLabel == "1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoSnapshot_SnapshotsLiveLineupFirst()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: false);

        await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync("team", "1", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownRound_ReturnsNotFound()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: true);
        var result = await CreateSut().ExecuteAsync("user", "team", "99", null, default);
        Assert.IsType<SettleGameweekResult.NotFound>(result);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SettleGameweekUseCaseTests"`
Expected: FAIL — use case not defined.

- [ ] **Step 3: Implement**

```csharp
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
    public sealed record NotReady : SettleGameweekResult { public static readonly NotReady Instance = new(); }  // not all member matches final
    public sealed record Settled(GameweekScore Score) : SettleGameweekResult;
}

public interface ISettleGameweekUseCase
{
    // userId is needed to resolve the owned squad (for positions); teamId is the GameTeamId.
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

    public SettleGameweekUseCase(
        IGameweekConfigRepository config, IGameweekCalendarService calendar,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup,
        IGameweekScoreRepository scores, IGetSquadUseCase squad, IPlayerStatsRepository stats,
        IScoringRuleSetRepository ruleSets, ILineupConstraintsRepository constraints,
        IGameweekScoringService scoring)
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
    }

    public async Task<SettleGameweekResult> ExecuteAsync(
        string userId, string teamId, string roundLabel, int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return SettleGameweekResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return SettleGameweekResult.NotFound.Instance;

        var gw = calendar.FirstOrDefault(g => g.RoundLabel == roundLabel);
        if (gw is null) return SettleGameweekResult.NotFound.Instance;

        // Only settle once every member match is final (results complete). Postponed match → not yet.
        if (gw.Matches.Count == 0 || !gw.Matches.All(m => m.IsFinal))
            return SettleGameweekResult.NotReady.Instance;

        var ruleSet = await _ruleSets.GetAsync(GameFlavor.Fantasy, config.ScoringRuleSetVersion, ct);
        if (ruleSet is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var constraints = await _constraints.GetAsync(config.LineupConstraintsVersion, ct);
        if (constraints is null) return SettleGameweekResult.RuleSetMissing.Instance;

        // Snapshot-if-missing: freeze the live lineup (unchanged since the deadline) before scoring.
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
            return SettleGameweekResult.RuleSetMissing.Instance;

        // Build playerId → aggregated stats across the gameweek's member matches (presence = played).
        var played = await BuildPlayedStatsAsync(gw, ct);

        var score = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints);
        await _scores.SaveAsync(score, ct);
        return new SettleGameweekResult.Settled(score);
    }

    private async Task<IReadOnlyDictionary<string, AggregatedStats>> BuildPlayedStatsAsync(
        Gameweek gw, CancellationToken ct)
    {
        var acc = new Dictionary<string, AggregatedStats>(StringComparer.Ordinal);
        foreach (var match in gw.Matches)
        {
            foreach (var s in await _stats.GetByMatchAsync(match.MatchId, ct))
            {
                if (acc.TryGetValue(s.PlayerId, out var cur))
                    acc[s.PlayerId] = cur with
                    {
                        Games = cur.Games + 1,
                        Goals = cur.Goals + s.Goals,
                        YellowCards = cur.YellowCards + s.YellowCards,
                        TwoMinuteSuspensions = cur.TwoMinuteSuspensions + s.TwoMinuteSuspensions,
                        RedCards = cur.RedCards + s.RedCards
                    };
                else
                    acc[s.PlayerId] = new AggregatedStats(1, s.Goals, s.YellowCards, s.TwoMinuteSuspensions, s.RedCards);
            }
        }
        return acc;
    }
}
```

> **V0 scope note:** This settles one team per call (the team passed in). Fan-out across every manager who owns a roster is deferred — the settlement endpoint (Task 15) and the ingestion trigger (Task 19) call this per team. Log this limitation rather than implying full-league settlement; a "settle all teams" loop is a clean follow-up once a `list all teams` query exists.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SettleGameweekUseCaseTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Register + commit**

In `Program.cs` add:
```csharp
builder.Services.AddScoped<IGameweekScoringService, GameweekScoringService>();
builder.Services.AddScoped<ISettleGameweekUseCase, SettleGameweekUseCase>();
```
(`FantasyPlayerRatingFunction` is already registered as itself in `Program.cs`.)

```bash
git add Ez.Handball.Application/UseCases/SettleGameweekUseCase.cs Ez.Handball.Tests/Application/UseCases/SettleGameweekUseCaseTests.cs Ez.Handball.Api/Program.cs
git commit -m "feat: settle a gameweek score idempotently (Backend#60)"
```

## Task 15: `GetMyGameweekScoresUseCase` + read/settle endpoints

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetMyGameweekScoresUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetMyGameweekScoresUseCaseTests.cs`
- Modify: `Ez.Handball.Api/GameweekEndpoints.cs`
- Modify: `Ez.Handball.Api/Program.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMyGameweekScoresUseCaseTests
{
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private GetMyGameweekScoresUseCase CreateSut() => new(_scores.Object);

    [Fact]
    public async Task SumsRunningTotal_AcrossGameweeks()
    {
        _scores.Setup(s => s.ListByTeamAsync("user:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GameweekScore("user:fantasy", "1", 40, "fp1", Array.Empty<GameweekPlayerScore>()),
                new GameweekScore("user:fantasy", "2", 55, "fp2", Array.Empty<GameweekPlayerScore>()),
            });

        var result = await CreateSut().ExecuteAsync("user", default);

        Assert.Equal(95, result.RunningTotal);
        Assert.Equal(2, result.Gameweeks.Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetMyGameweekScoresUseCaseTests"`
Expected: FAIL — use case not defined.

- [ ] **Step 3: Implement the use case**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record MyGameweekScores(double RunningTotal, IReadOnlyList<GameweekScore> Gameweeks);

public interface IGetMyGameweekScoresUseCase
{
    Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetMyGameweekScoresUseCase : IGetMyGameweekScoresUseCase
{
    private readonly IGameweekScoreRepository _scores;

    public GetMyGameweekScoresUseCase(IGameweekScoreRepository scores) => _scores = scores;

    public async Task<MyGameweekScores> ExecuteAsync(string userId, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var rows = await _scores.ListByTeamAsync(teamId, ct);
        var ordered = rows.OrderBy(r => RoundSortKey(r.RoundLabel)).ThenBy(r => r.RoundLabel, StringComparer.Ordinal).ToList();
        return new MyGameweekScores(ordered.Sum(r => r.Points), ordered);
    }

    private static (int, int) RoundSortKey(string round)
        => int.TryParse(round, out var n) ? (0, n) : (1, 0);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetMyGameweekScoresUseCaseTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Add the authed read endpoint + the settle endpoint**

In `GameweekEndpoints.cs`, add inside `MapGameweekEndpoints` (note the new `using Ez.Handball.Api.Auth;` and `using Ez.Handball.Domain;` at the top):

```csharp
        app.MapGet("/api/users/me/gameweeks", async (
            HttpContext http, IGetMyGameweekScoresUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, ct);
            return Results.Ok(new
            {
                runningTotal = result.RunningTotal,
                gameweeks = result.Gameweeks.Select(g => new
                {
                    roundLabel = g.RoundLabel,
                    points = g.Points,
                    captainPlayerId = g.CaptainPlayerId,
                    breakdown = g.Breakdown
                })
            });
        }).RequireAuthorization();

        app.MapPost("/api/gameweeks/settle", async (
            string round, string? teamId, HttpContext http, int? version,
            ISettleGameweekUseCase uc, CancellationToken ct) =>
        {
            // Authed: the caller settles their own team unless an explicit teamId is given (admin/ingestion).
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
            if (string.IsNullOrWhiteSpace(round))
                return Results.BadRequest(new { error = "invalid_round" });

            var team = string.IsNullOrWhiteSpace(teamId) ? GameTeamId.For(userId, GameFlavor.Fantasy) : teamId;
            var result = await uc.ExecuteAsync(userId, team, round, version, ct);
            return result switch
            {
                SettleGameweekResult.ConfigMissing      => Results.BadRequest(new { error = "gameweek_config_missing" }),
                SettleGameweekResult.NotFound           => Results.NotFound(new { error = "round_not_found" }),
                SettleGameweekResult.RuleSetMissing     => Results.BadRequest(new { error = "rule_set_missing" }),
                SettleGameweekResult.NoSnapshotPossible => Results.Json(new { error = "no_lineup" }, statusCode: StatusCodes.Status409Conflict),
                SettleGameweekResult.NotReady           => Results.Json(new { error = "not_ready" }, statusCode: StatusCodes.Status409Conflict),
                SettleGameweekResult.Settled s          => Results.Ok(new { round = s.Score.RoundLabel, points = s.Score.Points }),
                _                                       => Results.Problem()
            };
        }).RequireAuthorization();
```

In `Program.cs` register the use case:
```csharp
builder.Services.AddScoped<IGetMyGameweekScoresUseCase, GetMyGameweekScoresUseCase>();
```

- [ ] **Step 6: Build + full test pass**

Run: `dotnet build Ez.Handball.sln && dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: Build succeeded; all tests PASS (start Azurite first: `azurite --silent --location /tmp/azurite-test &`).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetMyGameweekScoresUseCase.cs Ez.Handball.Tests/Application/UseCases/GetMyGameweekScoresUseCaseTests.cs Ez.Handball.Api/GameweekEndpoints.cs Ez.Handball.Api/Program.cs
git commit -m "feat: my-gameweek-scores read + settle endpoint (Backend#60)"
```

**End of Phase 2 — scores can be settled (manually/by API) and read with a running total.**

---

# PHASE 3 — Lock-aware mutations + ingestion trigger

## Task 16: `GameweekSnapshotGuard`

**Files:**
- Create: `Ez.Handball.Application/Services/GameweekSnapshotGuard.cs`
- Test: `Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs`

Runs before any mutation: for every gameweek whose (pinned-or-derived) deadline has passed and has no snapshot for this team yet, pin its deadline and freeze the current live lineup into the per-gameweek snapshot. Returns the current editable gameweek (the echo source).

- [ ] **Step 1: Write the failing tests**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekSnapshotGuardTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IGameweekLockRepository> _locks = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<ILineupRepository> _liveLineup = new();
    private DateTimeOffset _now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private GameweekSnapshotGuard CreateSut() => new(
        _config.Object, _calendar.Object, _locks.Object, _snapshots.Object, _liveLineup.Object, () => _now);

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);

    private static Gameweek GW(int n, string round, DateTimeOffset deadline, GameweekStatus status) =>
        new(n, round, "8444", deadline, status, Array.Empty<GameweekMatch>());

    private static Lineup Live() => new(new[] { new LineupSlot("p1", LineupRole.Starter, null) });

    private void Setup(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(gws);
        _liveLineup.Setup(l => l.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync(Live());
    }

    [Fact]
    public async Task PastDeadline_NoSnapshot_PinsAndSnapshots()
    {
        Setup(
            GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled),  // locked, no snapshot yet
            GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _locks.Verify(l => l.PinAsync("8444", "1", It.IsAny<DateTimeOffset>(), _now, It.IsAny<CancellationToken>()), Times.Once);
        _snapshots.Verify(s => s.SaveSnapshotAsync("team", "1", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.CurrentGameweek!.Number);     // editable = earliest Open
        Assert.True(result.CurrentGameweekLocked);           // a locked GW exists
    }

    [Fact]
    public async Task ExistingSnapshot_NotReSnapshotted()
    {
        Setup(GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled), GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync(Live());

        await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllOpen_NoSnapshots_CurrentIsFirst_NotLocked()
    {
        Setup(GW(1, "1", _now.AddDays(2), GameweekStatus.Open), GW(2, "2", _now.AddDays(9), GameweekStatus.Open));

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, result.CurrentGameweek!.Number);
        Assert.False(result.CurrentGameweekLocked);
    }

    [Fact]
    public async Task NoLiveLineup_SkipsSnapshot_StillReportsCurrent()
    {
        Setup(GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled), GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);
        _liveLineup.Setup(l => l.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(2, result.CurrentGameweek!.Number);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekSnapshotGuardTests"`
Expected: FAIL — guard not defined.

- [ ] **Step 3: Implement**

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed record SnapshotGuardResult(Gameweek? CurrentGameweek, bool CurrentGameweekLocked);

public interface IGameweekSnapshotGuard
{
    // Freezes any locked-but-unsnapshotted gameweek for this team, then reports the current editable
    // gameweek (earliest Open) and whether at least one gameweek is currently locked.
    Task<SnapshotGuardResult> EnsureSnapshotsAsync(string teamId, int? configVersion, CancellationToken ct);
}

public sealed class GameweekSnapshotGuard : IGameweekSnapshotGuard
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly IGameweekLockRepository _locks;
    private readonly IGameweekLineupRepository _snapshots;
    private readonly ILineupRepository _liveLineup;
    private readonly Func<DateTimeOffset> _now;

    public GameweekSnapshotGuard(
        IGameweekConfigRepository config, IGameweekCalendarService calendar, IGameweekLockRepository locks,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup, Func<DateTimeOffset> now)
    {
        _config = config;
        _calendar = calendar;
        _locks = locks;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
        _now = now;
    }

    public async Task<SnapshotGuardResult> EnsureSnapshotsAsync(string teamId, int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return new SnapshotGuardResult(null, false);

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return new SnapshotGuardResult(null, false);

        var now = _now();
        var anyLocked = false;
        Lineup? live = null;
        var liveLoaded = false;

        foreach (var gw in calendar)
        {
            if (now < gw.Deadline) continue; // still Open → not locked
            anyLocked = true;

            // Pin the deadline the first time it's observed as passed (idempotent first-write-wins).
            await _locks.PinAsync(config.TournamentId, gw.RoundLabel, gw.Deadline, now, ct);

            var existing = await _snapshots.GetSnapshotAsync(teamId, gw.RoundLabel, ct);
            if (existing is not null) continue;

            if (!liveLoaded)
            {
                live = await _liveLineup.GetAsync(teamId, ct);
                liveLoaded = true;
            }
            if (live is not null)
                await _snapshots.SaveSnapshotAsync(teamId, gw.RoundLabel, live, ct);
        }

        var current = calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open);
        return new SnapshotGuardResult(current, anyLocked);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GameweekSnapshotGuardTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Register + commit**

In `Program.cs`:
```csharp
builder.Services.AddScoped<IGameweekSnapshotGuard, GameweekSnapshotGuard>();
```
```bash
git add Ez.Handball.Application/Services/GameweekSnapshotGuard.cs Ez.Handball.Tests/Application/Services/GameweekSnapshotGuardTests.cs Ez.Handball.Api/Program.cs
git commit -m "feat: lazy snapshot guard for locked gameweeks (Backend#60)"
```

## Task 17: Wire the guard into the mutation use cases

**Files:**
- Modify: `Ez.Handball.Application/UseCases/BuyPlayerUseCase.cs`
- Modify: `Ez.Handball.Application/UseCases/SellPlayerUseCase.cs`
- Modify: `Ez.Handball.Application/UseCases/SetLineupUseCase.cs`
- Modify: their existing test files (constructor signature changes).

The guard runs **before** the mutation applies, so the pre-edit lineup is frozen for any locked gameweek. The guard is best-effort: if config is missing (pre-season), it returns `(null, false)` and the mutation proceeds unchanged.

- [ ] **Step 1: Update the existing tests to the new constructor (write failing test first)**

In `BuyPlayerUseCaseTests`, `SellPlayerUseCaseTests`, `SetLineupUseCaseTests` (find them under `Ez.Handball.Tests/Application/UseCases/`), add a guard mock and pass it to the SUT constructor. Add to each test class:

```csharp
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    // in the field initializer or a [constructor], make the guard a no-op:
    // _guard.Setup(g => g.EnsureSnapshotsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
    //       .ReturnsAsync(new SnapshotGuardResult(null, false));
```
Add `_guard.Object` as the **last** constructor argument in each `CreateSut()`/SUT construction. Add a new test to `BuyPlayerUseCaseTests` asserting the guard runs:

```csharp
    [Fact]
    public async Task RunsSnapshotGuardBeforeBuying()
    {
        // ... existing happy-path arrange that returns Committed ...
        _guard.Setup(g => g.EnsureSnapshotsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SnapshotGuardResult(null, false));

        await CreateSut().ExecuteAsync("user", "p1", new BuyPlayerContext(null, null, null), default);

        _guard.Verify(g => g.EnsureSnapshotsAsync(
            GameTeamId.For("user", GameFlavor.Fantasy), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```
Add `using Ez.Handball.Application.Services;` to each test file.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BuyPlayerUseCaseTests OR FullyQualifiedName~SellPlayerUseCaseTests OR FullyQualifiedName~SetLineupUseCaseTests"`
Expected: FAIL — constructor arity mismatch / `IGameweekSnapshotGuard` not a dependency yet.

- [ ] **Step 3: Add the guard to each use case**

The guard takes the **gameweek config version** (not the pricing `ruleSetVersion`), so pass `null` to use the default (v1) — the two version axes are unrelated.

In **`BuyPlayerUseCase`**: add `using Ez.Handball.Application.Services;`, add a field + constructor param `IGameweekSnapshotGuard guard` (assign `_guard`), and at the very start of `ExecuteAsync`, after the `NoTeam` check, insert:
```csharp
        await _guard.EnsureSnapshotsAsync(GameTeamId.For(userId, GameFlavor.Fantasy), null, ct);
```
(Place it after `if (!await _teams.ExistsAsync(...)) return NoTeam;` so we don't snapshot for a non-existent team.)

In **`SellPlayerUseCase`**: same — add `using`, field, constructor param `IGameweekSnapshotGuard guard`, and after the `NoTeam` check:
```csharp
        await _guard.EnsureSnapshotsAsync(teamId, null, ct);
```
(`teamId` is already computed right after the `NoTeam` check — place the guard call immediately after that line.)

In **`SetLineupUseCase`**: add `using`, field, constructor param `IGameweekSnapshotGuard guard`, and after the `NoTeam` check, before validation:
```csharp
        await _guard.EnsureSnapshotsAsync(GameTeamId.For(userId, GameFlavor.Fantasy), null, ct);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BuyPlayerUseCaseTests OR FullyQualifiedName~SellPlayerUseCaseTests OR FullyQualifiedName~SetLineupUseCaseTests"`
Expected: PASS.

- [ ] **Step 5: Build + commit**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded (DI resolves `IGameweekSnapshotGuard` — registered in Task 16).
```bash
git add Ez.Handball.Application/UseCases/BuyPlayerUseCase.cs Ez.Handball.Application/UseCases/SellPlayerUseCase.cs Ez.Handball.Application/UseCases/SetLineupUseCase.cs Ez.Handball.Tests/Application/UseCases/
git commit -m "feat: run snapshot guard before squad/lineup mutations (Backend#60)"
```

## Task 18: `appliedToGameweek` echo on the mutating endpoints

**Files:**
- Modify: `Ez.Handball.Api/SquadEndpoints.cs`
- Modify: `Ez.Handball.Api/LineupEndpoints.cs`

The endpoints resolve the current editable gameweek (after the mutation/guard ran) via `IGetCurrentGameweekUseCase` and attach a small echo to the success bodies. Pure reads (`GET`) are untouched.

- [ ] **Step 1: Add the shared echo helper**

Define one shared helper in `GameweekEndpoints.cs` (both `SquadEndpoints` and `LineupEndpoints` call it — no duplication). The echo is two fields: `appliedToGameweek` = the editable gameweek the change applies to (the earliest `Open` one), and `currentGameweekLocked` = true when there is no editable gameweek (everything has locked).

```csharp
// in GameweekEndpoints.cs
using Ez.Handball.Application.UseCases;

public static class GameweekEcho
{
    public static async Task<object> BuildAsync(IGetCurrentGameweekUseCase gw, CancellationToken ct)
    {
        var r = await gw.ExecuteAsync(null, ct);
        var current = (r as GetCurrentGameweekResult.Found)?.Current;
        return new { appliedToGameweek = current?.Number, currentGameweekLocked = current is null };
    }
}
```

In `SquadEndpoints.cs`, inject `IGetCurrentGameweekUseCase gw` into the `MapPost("/players", ...)` and `MapDelete("/players/{playerId}", ...)` handler lambdas (add the parameter after the existing use-case param). Then in the buy `Committed` branch:
```csharp
                BuyPlayerResult.Committed c =>
                    Results.Json(new { squad = SquadBody(c.View), gameweek = await GameweekEcho.BuildAsync(gw, ct) },
                        statusCode: StatusCodes.Status201Created),
```
and in the sell `Sold` branch:
```csharp
                SellPlayerResult.Sold s =>
                    Results.Ok(new { squad = SquadBody(s.View), gameweek = await GameweekEcho.BuildAsync(gw, ct) }),
```
Note: this nests the squad body under `squad` and the echo under `gameweek` — a deliberate, additive response-shape change; update the Web client (Web#38) accordingly.

- [ ] **Step 2: Same echo on the lineup PUT**

In `LineupEndpoints.cs`, inject `IGetCurrentGameweekUseCase gw` into the `MapPut` lambda (after the existing `ISetLineupUseCase` param) and change the `Committed` branch:
```csharp
                SetLineupResult.Committed c =>
                    Results.Ok(new { lineup = LineupBody(c.View), gameweek = await GameweekEcho.BuildAsync(gw, ct) }),
```
Both endpoints now call the single `GameweekEcho.BuildAsync` from Step 1.

- [ ] **Step 3: Build**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke test**

With Azurite + Api running, config + matches + a team seeded: buy a player and confirm the response is `{ "squad": {...}, "gameweek": { "appliedToGameweek": <n>, "currentGameweekLocked": false } }`.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/SquadEndpoints.cs Ez.Handball.Api/LineupEndpoints.cs Ez.Handball.Api/GameweekEndpoints.cs
git commit -m "feat: echo applied-gameweek on squad/lineup mutations (Backend#60)"
```

## Task 19: Ingestion settlement trigger

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/TriggerSettlementFunction.cs`

After a match's player stats finish parsing, ingestion pokes the Api settlement endpoint. Ingestion holds no scoring logic. Because settlement is per-team and idempotent, this is a best-effort fire-and-log call; the Api enforces "not ready" until all member matches are final.

> **Design constraint:** Ingestion (isolated worker) references `Ez.Handball.Shared`, not `Ez.Handball.Application`, so it cannot call the use case directly — it makes an HTTP POST to the Api. The Api base URL + a function/admin key come from config (`Settlement:ApiBaseUrl`). Settlement fan-out across all teams is out of V0 scope (see Task 14 note); this trigger settles for the configured tournament's current round once the Api gains a "settle all teams" path. For V0, wire the HTTP call and log it; full automation lands with the team fan-out follow-up.

- [ ] **Step 1: Implement the trigger as a thin blob-triggered poke**

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// Fires after player stats are parsed (raw/matches/*/players-*.json). Best-effort: pokes the Api
// settlement endpoint so it can recompute any gameweek whose matches are now all final. The Api is
// authoritative (idempotent, "not ready" until complete); failures here are logged, not fatal.
public class TriggerSettlementFunction
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TriggerSettlementFunction> _logger;

    public TriggerSettlementFunction(IHttpClientFactory httpFactory, ILogger<TriggerSettlementFunction> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [Function("TriggerSettlement")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/{name}", Connection = "AzureWebJobsStorage")] string content,
        string matchId, string name, FunctionContext context)
    {
        if (!name.StartsWith("players-", StringComparison.Ordinal)) return;

        var baseUrl = Environment.GetEnvironmentVariable("Settlement__ApiBaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("Settlement trigger skipped for match {MatchId}: no Settlement__ApiBaseUrl configured.", matchId);
            return;
        }

        try
        {
            var client = _httpFactory.CreateClient();
            // V0: the Api decides readiness; the round + team fan-out is a follow-up. This logs intent.
            _logger.LogInformation("Match {MatchId} parsed; settlement poke target {BaseUrl} (fan-out deferred).", matchId, baseUrl);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settlement poke failed for match {MatchId}.", matchId);
        }
    }
}
```

> **Note:** This V0 trigger is intentionally a logging stub for the HTTP fan-out (the round → all-teams settlement loop doesn't exist yet). It establishes the trigger point and config surface. The actual per-team POST loop ships with the team-fan-out follow-up. Keep the blob-trigger wiring so the integration point is real and reviewable.

- [ ] **Step 2: Build**

Run: `dotnet build Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj`
Expected: Build succeeded. (If `IHttpClientFactory` isn't registered in the Ingestion host, add `services.AddHttpClient();` in the Ingestion `Program.cs`.)

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/TriggerSettlementFunction.cs Ez.Handball.Ingestion/Program.cs
git commit -m "feat: ingestion settlement trigger point (Backend#60)"
```

## Task 20: Full suite + finish

- [ ] **Step 1: Run the entire suite**

Run: `azurite --silent --location /tmp/azurite-test & dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: All tests PASS.

- [ ] **Step 2: Update operational docs**

Add a short note to `CLAUDE.md` (or the deployment memory) under a "Gameweek engine" heading: seed `fantasy-gameweek-v1` per environment via `POST /api/seed/gameweek-config`; settlement is idempotent (`POST /api/gameweeks/settle?round=`); deadlines pin on first lock; V0 settles per team (fan-out is a follow-up).

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: gameweek engine ops notes (Backend#60)"
```

---

## Open follow-ups (explicitly out of this plan)

- **Team fan-out settlement:** settle every manager's gameweek in one pass (needs a "list all game teams" query). Tasks 14/19 settle per team.
- **Calibrated scoring values (#27):** this engine consumes whatever `ScoringRuleSet` version the config names; the actual point values land via #27.
- **Notifications (#18):** deadline-approaching / round-settled emits are not wired here.
- **Manager standings (#62):** consumes `GameweekScores`; separate plan.
- **Real position vocabulary:** auto-sub validity uses `LineupConstraints.PositionStart`, whose position codes are still placeholders pending real `Player.Position` values (shared with the squad/lineup work).
