# Player `Retired` Flag Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a stored `Retired` boolean to players, seeded once by a bootstrap function from the latest season's stats and hand-maintained thereafter, that survives reparse and hides retired players from the pool and leaderboard.

**Architecture:** `Retired` is a nullable column on `PlayerEntity`, preserved across reparse with `TableUpdateMode.Merge` (the `LogoSrc`-on-`Clubs` pattern). A new Ingestion HTTP function bootstraps it. The pool use case and leaderboard repository filter retired players out of their lists; the player-detail endpoint exposes the flag. Single-player lookups are untouched.

**Tech Stack:** .NET 8, Azure Functions isolated worker (Ingestion), ASP.NET minimal API (Api), Azure Table Storage, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-06-11-player-retired-flag-design.md`

**Test/build commands** (no Azurite needed — every test below is Moq-based):
- Build: `dotnet build Ez.Handball.sln`
- Filtered test: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~<ClassName>"`
- Full suite: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`

---

## File Structure

**Modified:**
- `Ez.Handball.Shared/Entities/PlayerEntity.cs` — add `bool? Retired`.
- `Ez.Handball.Domain/Player.cs` — add `bool Retired`.
- `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs` — add `bool Retired` to `PooledPlayer`.
- `Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs` — map `Retired`.
- `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs` — populate `Retired`.
- `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs` — filter out retired.
- `Ez.Handball.Infrastructure/TableAccess/TableLeaderboardRepository.cs` — filter out retired.
- `Ez.Handball.Ingestion/Parsing/PlayerParser.cs` — `Merge` the Players upsert.
- `Ez.Handball.Api/Program.cs` — add `retired` to player-detail response.
- Existing tests updated for the new positional record fields.

**Created:**
- `Ez.Handball.Ingestion/Functions/BootstrapRetiredFunction.cs` — the bootstrap endpoint.
- `Ez.Handball.Tests/Functions/BootstrapRetiredFunctionTests.cs` — its tests.

---

## Task 1: Add `Retired` to the data model and map it on player read

**Files:**
- Modify: `Ez.Handball.Shared/Entities/PlayerEntity.cs`
- Modify: `Ez.Handball.Domain/Player.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs:41-51`
- Modify: `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs` (3 `new Player(...)` constructions at lines ~79, ~108, ~127)
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs`

- [ ] **Step 1: Add the nullable column to `PlayerEntity`**

In `Ez.Handball.Shared/Entities/PlayerEntity.cs`, add after the `ClubName` property:

```csharp
    public string? ClubName { get; set; }
    // Out-of-band, maintainer-owned flag. Nullable so a Merge upsert that doesn't
    // set it leaves the stored value untouched (same trick as LogoSrc on Clubs).
    public bool? Retired { get; set; }
```

- [ ] **Step 2: Add `Retired` to the `Player` domain record**

In `Ez.Handball.Domain/Player.cs`, add `Retired` as the final positional parameter:

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
    bool Retired);
```

- [ ] **Step 3: Make everything compile with a stub mapping**

In `Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs`, add `Retired: false` as the last argument of the `new Player(...)` return (temporary stub — Task makes it real in Step 5):

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
            Retired: false);
```

In `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`, append `, false` to each of the three `new Player(...)` constructions so they end `..., "VS", false);` (lines ~82, ~111, ~130).

- [ ] **Step 4: Build to confirm green compile (no behavior change yet)**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 5: Write the failing mapping test**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task GetByIdAsync_RetiredTrue_MapsToRetiredTrue()
    {
        const string playerId = "777";
        SetupRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Retired Rúnar",
            Gender = "karlar",
            ClubId = "385",
            Position = "VS",
            Retired = true
        });

        var result = await CreateSut(today: new DateOnly(2026, 5, 22)).GetByIdAsync(playerId, default);

        Assert.NotNull(result);
        Assert.True(result!.Retired);
    }

    [Fact]
    public async Task GetByIdAsync_RetiredNull_MapsToFalse()
    {
        const string playerId = "778";
        SetupRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Active Aron",
            Gender = "karlar",
            ClubId = "385",
            Position = "VS",
            Retired = null
        });

        var result = await CreateSut(today: new DateOnly(2026, 5, 22)).GetByIdAsync(playerId, default);

        Assert.NotNull(result);
        Assert.False(result!.Retired);
    }
```

- [ ] **Step 6: Run the tests to verify the `RetiredTrue` test fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerRepositoryTests"`
Expected: `GetByIdAsync_RetiredTrue_MapsToRetiredTrue` FAILS (stub returns `false`); `GetByIdAsync_RetiredNull_MapsToFalse` passes.

- [ ] **Step 7: Replace the stub with the real mapping**

In `TablePlayerRepository.cs`, change the stub:

```csharp
            Position: row.Position,
            Retired: row.Retired ?? false);
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerRepositoryTests"`
Expected: PASS (all).

- [ ] **Step 9: Commit**

```bash
git add Ez.Handball.Shared/Entities/PlayerEntity.cs Ez.Handball.Domain/Player.cs \
  Ez.Handball.Infrastructure/TableAccess/TablePlayerRepository.cs \
  Ez.Handball.Tests/Infrastructure/Tables/TablePlayerRepositoryTests.cs \
  Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs
git commit -m "feat: add Retired flag to PlayerEntity and Player, map on read"
```

---

## Task 2: Expose `retired` on the player-detail endpoint

**Files:**
- Modify: `Ez.Handball.Api/Program.cs:192-206` (the `GetPlayerProfileResult.Found` response object)
- Test: `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`

- [ ] **Step 1: Write the failing endpoint test**

Add to `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetPlayer_RetiredPlayer_Returns200WithRetiredTrue()
    {
        var player = new Player(
            "12345", "Retired Rúnar", "23",
            new DateOnly(1985, 7, 19),
            40, "385-karlar", "385", "Stjarnan", "karlar", "VS", true);

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(player, new PlayerPrice(5_000_000, "ISK"), 50.0));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("retired").GetBoolean());
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerEndpointsTests.GetPlayer_RetiredPlayer_Returns200WithRetiredTrue"`
Expected: FAIL — `body.GetProperty("retired")` throws because the response has no `retired` member.

- [ ] **Step 3: Add `retired` to the response object**

In `Ez.Handball.Api/Program.cs`, in the `GetPlayerProfileResult.Found f` branch, add `f.Player.Retired` to the anonymous object:

```csharp
        GetPlayerProfileResult.Found f        => Results.Ok(new
        {
            f.Player.PlayerId,
            f.Player.Name,
            f.Player.JerseyNumber,
            f.Player.DateOfBirth,
            f.Player.Age,
            f.Player.TeamId,
            f.Player.ClubId,
            f.Player.ClubName,
            f.Player.Gender,
            f.Player.Position,
            f.Player.Retired,
            price = f.Price,
            rating = f.Rating
        }),
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerEndpointsTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Api/Program.cs Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs
git commit -m "feat: expose retired on player-detail endpoint"
```

---

## Task 3: Carry `Retired` through the pool repository and exclude retired from the pool use case

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs` (the `PooledPlayer` record)
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs:58-65`
- Modify: `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs:85-88`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`

- [ ] **Step 1: Add `Retired` to `PooledPlayer`**

In `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs`, add `Retired` as the final positional parameter of `PooledPlayer`:

```csharp
public sealed record PooledPlayer(
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    AggregatedStats Stats,
    bool Retired);
```

- [ ] **Step 2: Make everything compile (stub repo value + fix test helper)**

In `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs`, add `Retired: false` as the last argument of the `new PooledPlayer(...)` (temporary stub — made real in Step 7):

```csharp
                return new PooledPlayer(
                    PlayerId: g.Key,
                    Name: player?.Name,
                    ClubId: clubId,
                    ClubName: clubName,
                    Gender: gender,
                    Position: player?.Position ?? string.Empty,
                    Stats: stats,
                    Retired: false);
```

In `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`, update the `Pooled` helper to accept and pass a `retired` flag:

```csharp
    private static PooledPlayer Pooled(
        string playerId, int goals, int games = 10, string position = "CB",
        string gender = "karlar", bool retired = false) =>
        new(playerId, $"P{playerId}", "385", "Stjarnan", gender, position,
            new AggregatedStats(games, goals, 0, 0, 0), retired);
```

- [ ] **Step 3: Build to confirm green compile**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 4: Write the failing use-case exclusion test**

Add to `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`:

```csharp
    [Fact]
    public async Task Execute_ExcludesRetiredPlayers_FromEntriesAndTotal()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(
            Pooled("active", goals: 10, retired: false),
            Pooled("retired", goals: 99, retired: true));

        var result = await CreateSut().ExecuteAsync(Req(), offset: 0, limit: 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(1, pool.Total);
        var entry = Assert.Single(pool.Entries);
        Assert.Equal("active", entry.PlayerId);
    }
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests.Execute_ExcludesRetiredPlayers_FromEntriesAndTotal"`
Expected: FAIL — `pool.Total` is 2 and the retired player ranks first.

- [ ] **Step 6: Filter retired players in the use case**

In `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`, add a `.Where(p => !p.Retired)` ahead of the existing position filter:

```csharp
        var computed = players
            .Where(p => !p.Retired)
            .Where(p => string.IsNullOrWhiteSpace(request.Position)
                || string.Equals(p.Position, request.Position, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
```

- [ ] **Step 7: Write the failing repo-mapping test, then make the repo real**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task GetAggregated_PopulatesRetiredFromPlayersTable()
    {
        SetupStats(
            Stat("m1", "active", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "retiree", "2025-26", "8444", "385-karlar", "Stjarnan", 3));
        SetupPlayers(
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "active", Name = "A", Position = "CB", Retired = false },
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "retiree", Name = "R", Position = "CB", Retired = true });

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        Assert.True(result.Single(p => p.PlayerId == "retiree").Retired);
        Assert.False(result.Single(p => p.PlayerId == "active").Retired);
    }
```

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests.GetAggregated_PopulatesRetiredFromPlayersTable"`
Expected: FAIL — both come back `false` (stub).

Then in `TablePlayerPoolRepository.cs` replace the stub:

```csharp
                    Stats: stats,
                    Retired: player?.Retired ?? false);
```

- [ ] **Step 8: Run the affected suites to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests"`
Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests"`
Expected: PASS (both suites).

- [ ] **Step 9: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs \
  Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs \
  Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs \
  Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs \
  Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs
git commit -m "feat: carry Retired through pool repo and exclude retired from pool"
```

---

## Task 4: Exclude retired players from the leaderboard

**Files:**
- Modify: `Ez.Handball.Infrastructure/TableAccess/TableLeaderboardRepository.cs:37-74`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableLeaderboardRepositoryTests.cs`

- [ ] **Step 1: Write the failing exclusion test**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TableLeaderboardRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task GetRankedAsync_ExcludesRetiredPlayers()
    {
        SetupStats(null,
            Stat("m1", "active", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "retiree", "2025-26", "8444", "385-karlar", "Stjarnan", 99));
        SetupPlayers(
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "active", Name = "Active", Retired = false },
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "retiree", Name = "Retiree", Retired = true });

        var result = await CreateSut().GetRankedAsync(Q(), default);

        var entry = Assert.Single(result);
        Assert.Equal("active", entry.PlayerId);
        Assert.Equal(1, entry.Rank);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLeaderboardRepositoryTests.GetRankedAsync_ExcludesRetiredPlayers"`
Expected: FAIL — two entries returned, retiree ranked first.

- [ ] **Step 3: Load the Players table before ranking and skip retired groups**

In `Ez.Handball.Infrastructure/TableAccess/TableLeaderboardRepository.cs`, replace the aggregates block and the name-lookup that follows. Change from:

```csharp
        var aggregates = rows
            .GroupBy(r => r.RowKey)
            .Select(g => BuildAggregate(g.Key, g.ToList()))
            .OrderByDescending(a => MetricValue(a, q.Metric))
            .ThenBy(a => a.Games)
            .ThenBy(a => a.PlayerId, StringComparer.Ordinal)
            .ToList();

        var nameById = new Dictionary<string, string>();
        await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, null, ct))
            nameById[p.RowKey] = p.Name;
```

to:

```csharp
        // Load players first: their Retired flag must gate the ranking, and we
        // reuse the same dictionary for name resolution below.
        var playerById = new Dictionary<string, PlayerEntity>();
        await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, null, ct))
            playerById[p.RowKey] = p;

        var aggregates = rows
            .GroupBy(r => r.RowKey)
            .Where(g => !(playerById.TryGetValue(g.Key, out var pe) && (pe.Retired ?? false)))
            .Select(g => BuildAggregate(g.Key, g.ToList()))
            .OrderByDescending(a => MetricValue(a, q.Metric))
            .ThenBy(a => a.Games)
            .ThenBy(a => a.PlayerId, StringComparer.Ordinal)
            .ToList();
```

Then in the `for` loop that builds `LeaderboardEntry`, change the name lookup from:

```csharp
            var a = aggregates[i];
            if (!nameById.TryGetValue(a.PlayerId, out var name))
            {
                _logger.LogWarning(
                    "Player {PlayerId} not found in Players table while building leaderboard", a.PlayerId);
            }
```

to:

```csharp
            var a = aggregates[i];
            string? name = null;
            if (playerById.TryGetValue(a.PlayerId, out var pe))
            {
                name = pe.Name;
            }
            else
            {
                _logger.LogWarning(
                    "Player {PlayerId} not found in Players table while building leaderboard", a.PlayerId);
            }
```

- [ ] **Step 4: Run the leaderboard suite to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableLeaderboardRepositoryTests"`
Expected: PASS (all — the new test plus every existing one).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Infrastructure/TableAccess/TableLeaderboardRepository.cs \
  Ez.Handball.Tests/Infrastructure/Tables/TableLeaderboardRepositoryTests.cs
git commit -m "feat: exclude retired players from the leaderboard"
```

---

## Task 5: Preserve `Retired` across reparse (PlayerParser uses Merge)

**Files:**
- Modify: `Ez.Handball.Ingestion/Parsing/PlayerParser.cs:1-6` (usings) and `:84-95` (Players upsert)
- Test: `Ez.Handball.Tests/Functions/PlayerParserTests.cs`

- [ ] **Step 1: Update the existing happy-path verify to expect Merge + Retired untouched**

In `Ez.Handball.Tests/Functions/PlayerParserTests.cs`, in `ParseAsync_HappyPath_UpsertsPlayerAndPlayerStats`, change the Players verify (currently ending `e.ClubName == "Stjarnan"), default), Times.Once);`) to assert the entity leaves `Retired` null and the call uses `Merge`:

```csharp
        // Assert — PlayerEntity upsert (Merge preserves the out-of-band Retired flag)
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e =>
                e.PartitionKey == teamId &&
                e.RowKey == "42" &&
                e.Name == "Jón Jónsson" &&
                e.Position == "Goalkeeper" &&
                e.Gender == "karlar" &&
                e.ClubId == "385" &&
                e.ClubName == "Stjarnan" &&
                e.Retired == null),
            default, Azure.Data.Tables.TableUpdateMode.Merge), Times.Once);
```

In the same file, in `ParseAsync_AwayClubResolution_UsesAwayTeamId`, change the Players verify (currently ending `e.ClubName == "Breiðablik"), default), Times.Once);`) to:

```csharp
        // Assert — teamId resolves to away team (Players upsert uses Merge)
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e =>
                e.PartitionKey == awayTeamId &&
                e.RowKey == "99" &&
                e.Gender == "karlar" &&
                e.ClubId == "390" &&
                e.ClubName == "Breiðablik"),
            default, Azure.Data.Tables.TableUpdateMode.Merge), Times.Once);
```

(Leave the `PlayerStats` verifies and the `ParseAsync_StaffEntry_IsFiltered_NoUpserts` test unchanged — `PlayerStats` stays `Replace`, and the staff test asserts `Times.Never`.)

- [ ] **Step 2: Run the parser suite to verify the two updated tests fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerParserTests"`
Expected: FAIL — the two Players verifies fail because the parser still upserts with the default `Replace`.

- [ ] **Step 3: Switch the Players upsert to Merge**

In `Ez.Handball.Ingestion/Parsing/PlayerParser.cs`, add the Tables namespace to the usings at the top:

```csharp
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;
```

Then change the Players upsert (it currently ends `ClubName = club?.Name }, ct);`) to pass `Merge` — leaving `Retired` unset so the stored value is preserved:

```csharp
            await _tableWriter.UpsertAsync("Players", new PlayerEntity
            {
                PartitionKey = teamId,
                RowKey = playerId,
                Name = player.Name,
                Position = player.Position,
                JerseyNumber = player.PlayerJerseyNumber,
                DateOfBirth = ParseDateOfBirth(player.Identifier),
                Gender = derivedGender,
                ClubId = derivedClubId,
                ClubName = club?.Name
                // Retired intentionally not set — Merge preserves the maintainer's value.
            }, ct, TableUpdateMode.Merge);
```

(The `PlayerStats` upsert immediately below stays exactly as-is.)

- [ ] **Step 4: Run the parser suite to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerParserTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Ingestion/Parsing/PlayerParser.cs Ez.Handball.Tests/Functions/PlayerParserTests.cs
git commit -m "feat: Merge Players upsert so Retired survives reparse"
```

---

## Task 6: Bootstrap function — seed `Retired` from the latest season

**Files:**
- Create: `Ez.Handball.Ingestion/Functions/BootstrapRetiredFunction.cs`
- Create: `Ez.Handball.Tests/Functions/BootstrapRetiredFunctionTests.cs`

No DI registration is required: `ITableWriter` is already registered in the Ingestion host (used by `SeedTournamentsFunction`), and Azure Functions discovers the `[Function]` by attribute.

- [ ] **Step 1: Create the function with its `ProcessAsync` core**

Create `Ez.Handball.Ingestion/Functions/BootstrapRetiredFunction.cs`:

```csharp
using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public record BootstrapRetiredResult(string Season, int Marked);

public class BootstrapRetiredFunction
{
    private readonly ITableWriter _tableWriter;

    public BootstrapRetiredFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("BootstrapRetired")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/bootstrap-retired")] HttpRequestData req,
        FunctionContext context)
    {
        var result = await ProcessAsync(context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<BootstrapRetiredResult> ProcessAsync(CancellationToken ct = default)
    {
        // 1. Latest season = lexical max of the distinct Tournaments partition keys
        //    (the YYYY-YY label format sorts correctly: "2025-26" > "2024-25").
        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", null!, ct);
        var latestSeason = tournaments
            .Select(t => t.PartitionKey)
            .Where(s => !string.IsNullOrEmpty(s))
            .DefaultIfEmpty(string.Empty)
            .Max(StringComparer.Ordinal)!;

        if (string.IsNullOrEmpty(latestSeason))
            return new BootstrapRetiredResult(string.Empty, 0);

        // 2. Player ids that appear in PlayerStats for that season.
        var stats = await _tableWriter.QueryAsync<PlayerStatEntity>(
            "PlayerStats", $"Season eq '{latestSeason}'", ct);
        var played = stats.Select(s => s.RowKey).ToHashSet(StringComparer.Ordinal);

        // 3. Every player with no stats that season gets Retired = true. We write
        //    back the FULL entity we read (not a partial one) — a Merge upsert of a
        //    partial PlayerEntity would blank Name/Position/etc. to their empty-string
        //    defaults. Only ever sets true, so re-runs never clobber manual edits.
        var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", null!, ct);
        var marked = 0;
        foreach (var p in players.Where(p => !played.Contains(p.RowKey)))
        {
            p.Retired = true;
            await _tableWriter.UpsertAsync("Players", p, ct, TableUpdateMode.Merge);
            marked++;
        }

        return new BootstrapRetiredResult(latestSeason, marked);
    }
}
```

- [ ] **Step 2: Create the test file with a failing test**

Create `Ez.Handball.Tests/Functions/BootstrapRetiredFunctionTests.cs`:

```csharp
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Functions;

public class BootstrapRetiredFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    private BootstrapRetiredFunction CreateSut() => new(_tableWriter.Object);

    private void SetupTournaments(params string[] seasons) =>
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seasons.Select(s => new TournamentEntity { PartitionKey = s, RowKey = "8444" }).ToList());

    private void SetupStatsForSeason(string season, params string[] playerIds) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerStatEntity>("PlayerStats", $"Season eq '{season}'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(playerIds.Select(id => new PlayerStatEntity { PartitionKey = "m1", RowKey = id, Season = season }).ToList());

    private void SetupPlayers(params PlayerEntity[] players) =>
        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private static PlayerEntity Plr(string id) =>
        new() { PartitionKey = "385-karlar", RowKey = id, Name = $"P{id}", Position = "CB", Gender = "karlar", ClubId = "385" };

    [Fact]
    public async Task Process_MarksOnlyPlayersWithNoStatsInLatestSeason()
    {
        // 2025-26 is the lexical-max season; only "active" played it.
        SetupTournaments("2024-25", "2025-26");
        SetupStatsForSeason("2025-26", "active");
        SetupPlayers(Plr("active"), Plr("retiree"));

        var result = await CreateSut().ProcessAsync();

        Assert.Equal("2025-26", result.Season);
        Assert.Equal(1, result.Marked);

        // "retiree" is marked true; the full entity is preserved (Name not blanked).
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "retiree" && e.Retired == true && e.Name == "Pretiree"),
            It.IsAny<CancellationToken>(), TableUpdateMode.Merge), Times.Once);

        // "active" is never written.
        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.RowKey == "active"),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task Process_OnlyEverSetsTrue_NeverWritesFalse()
    {
        SetupTournaments("2025-26");
        SetupStatsForSeason("2025-26", "active");
        SetupPlayers(Plr("active"), Plr("retiree"));

        await CreateSut().ProcessAsync();

        _tableWriter.Verify(t => t.UpsertAsync("Players",
            It.Is<PlayerEntity>(e => e.Retired == false),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task Process_NoTournaments_MarksNothing()
    {
        SetupTournaments();
        SetupPlayers(Plr("active"));

        var result = await CreateSut().ProcessAsync();

        Assert.Equal(string.Empty, result.Season);
        Assert.Equal(0, result.Marked);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<PlayerEntity>(),
            It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~BootstrapRetiredFunctionTests"`
Expected: PASS (all three). (The implementation from Step 1 already satisfies them — this task writes the function and its tests together; if any fail, fix the function until green.)

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Ingestion/Functions/BootstrapRetiredFunction.cs \
  Ez.Handball.Tests/Functions/BootstrapRetiredFunctionTests.cs
git commit -m "feat: add bootstrap-retired function to seed Retired from latest season"
```

---

## Task 7: Full verification and docs

**Files:**
- Modify: `Ez.Handball.Backend/CLAUDE.md` (the "Backfill after schema changes" section)

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: All tests pass. (Note the count — it should be the prior green count plus the new tests added across Tasks 1–6.)

- [ ] **Step 2: Document the rollout step in CLAUDE.md**

In `Ez.Handball.Backend/CLAUDE.md`, append to the "Backfill after schema changes" section:

```markdown
After deploying the `Retired` flag, run `POST /api/players/bootstrap-retired`
once. It marks every player with no `PlayerStats` in the latest season
(lexical-max `Tournaments` partition key) as `Retired = true`, writing back the
full row via `Merge`. It only ever sets `true`, so it is safe to re-run and never
clobbers manual edits. Curate further by editing the `Retired` column directly in
the `Players` table — use `Edm.Boolean`, not String (a String value causes a 500
on read). `POST /api/reparse` preserves all `Retired` values because the Players
upsert uses `Merge`.
```

- [ ] **Step 3: Commit**

```bash
git add Ez.Handball.Backend/CLAUDE.md
git commit -m "docs: document bootstrap-retired rollout and Retired editing"
```

---

## Self-Review Notes

- **Spec coverage:** data model (Task 1), parser Merge preservation (Task 5), bootstrap function in Ingestion (Task 6), pool exclusion (Task 3), leaderboard exclusion (Task 4), player-detail exposure (Task 2), single-player lookups untouched (no task changes them), ops/docs (Task 7). All spec sections map to a task.
- **Out of scope (per spec):** Web UI changes; automatic re-evaluation on season rollover.
- **Type consistency:** `Retired` is `bool?` on `PlayerEntity`; `bool` on `Player` and `PooledPlayer` (mapped `?? false`); `BootstrapRetiredResult(string Season, int Marked)` is the only new type and is used only within Task 6.
- **Critical gotcha encoded:** the bootstrap writes back the full read entity, never a partial one, because `Merge` of a partial `PlayerEntity` would overwrite non-null string fields (`Name`, `Position`, …) with empty strings.
