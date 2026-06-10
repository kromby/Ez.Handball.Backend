# Round Listing Endpoint (umferð view) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a public `GET /api/tournaments/{tournamentId}/rounds` endpoint that returns a tournament's fixtures grouped by round, with each round's date range and each match's score (if played) or kickoff (if upcoming).

**Architecture:** Capture the HSÍ `Round` label onto `MatchEntity` during ingestion (read from the archived match-list blob), then read it back through a new repository query → grouping use case → dumb API edge, following the existing clean-architecture layering (`/api/matches/{id}` is the template).

**Tech Stack:** .NET 8, Azure Functions isolated worker (ingestion), ASP.NET minimal APIs (API), Azure Table Storage, xUnit + Moq.

Spec: `docs/superpowers/specs/2026-06-10-round-listing-endpoint-design.md`

---

## File Structure

**Ingestion (capture round):**
- Modify: `Ez.Handball.Shared/Entities/MatchEntity.cs` — add `Round` field.
- Modify: `Ez.Handball.Ingestion/Parsing/MatchParser.cs` — inject `IBlobArchiver`, read round from the list blob.
- Modify: `Ez.Handball.Ingestion/Program.cs` — no code change expected (DI resolves `IBlobArchiver` automatically); verify only.
- Modify: `Ez.Handball.Tests/Functions/MatchParserTests.cs` — add blob mock + round tests.

**Read model (Domain types):**
- Create: `Ez.Handball.Domain/MatchListItem.cs` — repository row + team (`MatchListItem`, `MatchListTeam`).
- Create: `Ez.Handball.Domain/TournamentMatches.cs` — repository result wrapper.
- Create: `Ez.Handball.Domain/RoundListing.cs` — API response shape (`RoundListing`, `RoundGroup`, `RoundMatch`, `RoundTeam`).

**Repository:**
- Modify: `Ez.Handball.Application/Abstractions/IMatchRepository.cs` — add `ListByTournamentAsync`.
- Modify: `Ez.Handball.Infrastructure/TableAccess/TableMatchRepository.cs` — implement it.
- Modify: `Ez.Handball.Tests/Infrastructure/Tables/TableMatchRepositoryTests.cs` — add tests.

**Use case:**
- Create: `Ez.Handball.Application/UseCases/GetRoundsUseCase.cs` — result type, interface, implementation.
- Create: `Ez.Handball.Tests/Application/UseCases/GetRoundsUseCaseTests.cs` — tests.

**API edge:**
- Modify: `Ez.Handball.Api/Program.cs` — register use case + map route.
- Create: `Ez.Handball.Tests/Api/Endpoints/RoundsEndpointTests.cs` — endpoint tests.

---

## Task 1: Ingestion — persist `Round` on `MatchEntity`

**Files:**
- Modify: `Ez.Handball.Shared/Entities/MatchEntity.cs`
- Modify: `Ez.Handball.Ingestion/Parsing/MatchParser.cs`
- Test: `Ez.Handball.Tests/Functions/MatchParserTests.cs`

- [ ] **Step 1: Add the `Round` field to `MatchEntity`**

In `Ez.Handball.Shared/Entities/MatchEntity.cs`, add below `AwayHalftimeScore` (line 26):

```csharp
    public string Round { get; set; } = string.Empty;   // HSÍ round label from the match list (e.g. "1", "Undanúrslit")
```

- [ ] **Step 2: Update the parser test harness to inject a blob archiver**

In `Ez.Handball.Tests/Functions/MatchParserTests.cs`, add the import near the top (after line 5):

```csharp
using System.Linq;
```

Add a blob mock field next to `_tableWriter` (after line 15) and update `CreateSut` (line 17):

```csharp
    private readonly Mock<IBlobArchiver> _blobArchiver = new();

    private MatchParser CreateSut() =>
        new(_tableWriter.Object, _blobArchiver.Object, NullLogger<MatchParser>.Instance);

    private void SetupRoundBlob(string tournamentId, string gameId, string round)
    {
        var path = $"tournaments/{tournamentId}/matches.json";
        var json = JsonSerializer.Serialize(new MatchListResponse
        {
            Data = new List<MatchSummary> { new() { GameId = gameId, Round = round } }
        });
        _blobArchiver.Setup(b => b.ExistsAsync(path, default)).ReturnsAsync(true);
        _blobArchiver.Setup(b => b.ReadAsync(path, default)).ReturnsAsync(json);
    }
```

Note: existing tests do not call `SetupRoundBlob`; the loose `IBlobArchiver` mock returns `false` from `ExistsAsync` by default, so the parser leaves `Round` empty and those tests keep passing.

- [ ] **Step 3: Write the failing test — round copied from the list blob**

Add to `MatchParserTests.cs`:

```csharp
    [Fact]
    public async Task ParseAsync_CopiesRoundFromListBlob()
    {
        var tournamentEntity = new TournamentEntity
        {
            PartitionKey = "2025", RowKey = "8444",
            Name = "Olís deild karla", Gender = "karlar", Division = "1"
        };
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity> { tournamentEntity });
        SetupRoundBlob("8444", "5001", "3");

        var blobContent = BuildMatchDetailsJson(tournamentId: "8444");

        await CreateSut().ParseAsync(blobContent, "5001");

        _tableWriter.Verify(t => t.UpsertAsync("Matches",
            It.Is<MatchEntity>(e => e.RowKey == "5001" && e.Round == "3"),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseAsync_MissingListBlob_LeavesRoundEmpty()
    {
        var tournamentEntity = new TournamentEntity
        {
            PartitionKey = "2025", RowKey = "8444",
            Name = "Olís deild karla", Gender = "karlar", Division = "1"
        };
        _tableWriter
            .Setup(t => t.QueryAsync<TournamentEntity>("Tournaments", "RowKey eq '8444'", default))
            .ReturnsAsync(new List<TournamentEntity> { tournamentEntity });
        // No SetupRoundBlob → ExistsAsync returns false.

        var blobContent = BuildMatchDetailsJson(tournamentId: "8444");

        await CreateSut().ParseAsync(blobContent, "5001");

        _tableWriter.Verify(t => t.UpsertAsync("Matches",
            It.Is<MatchEntity>(e => e.RowKey == "5001" && e.Round == ""),
            default), Times.Once);
    }
```

- [ ] **Step 4: Run the tests to verify they fail to compile/pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MatchParserTests"`
Expected: FAIL — compile error (`MatchParser` has no 3-arg constructor) and `MatchEntity` round assertion.

- [ ] **Step 5: Inject `IBlobArchiver` into `MatchParser` and capture round**

In `Ez.Handball.Ingestion/Parsing/MatchParser.cs`, update the fields + constructor (lines 17–24):

```csharp
    private readonly ITableWriter _tableWriter;
    private readonly IBlobArchiver _blobArchiver;
    private readonly ILogger<MatchParser> _logger;

    public MatchParser(ITableWriter tableWriter, IBlobArchiver blobArchiver, ILogger<MatchParser> logger)
    {
        _tableWriter = tableWriter;
        _blobArchiver = blobArchiver;
        _logger = logger;
    }
```

Resolve the round just before the Matches upsert. Add this line directly above the `await _tableWriter.UpsertAsync("Matches", ...)` call (currently line 120):

```csharp
        var round = await ResolveRoundAsync(tournamentId, matchId, ct);
```

Add `Round = round,` to the `MatchEntity` initializer (after `AwayHalftimeScore = awayHalftime`):

```csharp
            AwayHalftimeScore = awayHalftime,
            Round = round
```

Add this private method to the class (after `ParseAsync`):

```csharp
    // Round lives only in the per-tournament match list, not the per-match details
    // this parser reads. Pull it from the archived list blob (source of truth) so a
    // plain reparse backfills it. Missing/unreadable → empty round, match still writes.
    private async Task<string> ResolveRoundAsync(string tournamentId, string matchId, CancellationToken ct)
    {
        var path = $"tournaments/{tournamentId}/matches.json";
        try
        {
            if (!await _blobArchiver.ExistsAsync(path, ct))
            {
                _logger.LogWarning(
                    "Match list blob {Path} not found; round unset for match {MatchId}", path, matchId);
                return string.Empty;
            }

            var json = await _blobArchiver.ReadAsync(path, ct);
            var summary = JsonSerializer.Deserialize<MatchListResponse>(json)?.Data
                .FirstOrDefault(s => s.GameId == matchId);

            if (summary is null)
            {
                _logger.LogWarning(
                    "Match {MatchId} not found in list blob {Path}; round unset", matchId, path);
                return string.Empty;
            }

            return summary.Round;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to read round for match {MatchId} from {Path}", matchId, path);
            return string.Empty;
        }
    }
```

`IBlobArchiver` is in `Ez.Handball.Ingestion.Services` (already imported at the top via `using Ez.Handball.Ingestion.Services;`). `MatchListResponse` / `MatchSummary` are in `Ez.Handball.Ingestion.Models` (already imported).

- [ ] **Step 6: Run the parser tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MatchParserTests"`
Expected: PASS (all MatchParserTests, including the two new ones).

- [ ] **Step 7: Verify the ingestion host still builds (DI wiring)**

The `IMatchParser` singleton registration in `Ez.Handball.Ingestion/Program.cs:39` resolves `IBlobArchiver` (already registered as a singleton at lines ~30–35), so no DI change is needed. Confirm with a build:

Run: `dotnet build Ez.Handball.Ingestion/Ez.Handball.Ingestion.csproj`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add Ez.Handball.Shared/Entities/MatchEntity.cs Ez.Handball.Ingestion/Parsing/MatchParser.cs Ez.Handball.Tests/Functions/MatchParserTests.cs
git commit -m "feat(ingestion): capture HSÍ round label on MatchEntity (#10)"
```

---

## Task 2: Domain types for the round listing read model

**Files:**
- Create: `Ez.Handball.Domain/MatchListItem.cs`
- Create: `Ez.Handball.Domain/TournamentMatches.cs`
- Create: `Ez.Handball.Domain/RoundListing.cs`

These are plain records with no behavior, so they have no standalone tests; the repository (Task 3) and use case (Task 4) tests exercise them. Create them now so later tasks compile.

- [ ] **Step 1: Create the repository row + team records**

Create `Ez.Handball.Domain/MatchListItem.cs`:

```csharp
namespace Ez.Handball.Domain;

// A lightweight fixture row for the round listing — no player lines. Score is the
// raw stored score; whether it is surfaced is decided by status downstream.
public sealed record MatchListItem(
    string MatchId,
    string Round,
    DateTimeOffset Date,
    string? Venue,
    string Status,
    MatchListTeam Home,
    MatchListTeam Away);

public sealed record MatchListTeam(
    string TeamId,
    string ClubId,
    string? ClubName,
    string? LogoSrc,
    int Score);
```

- [ ] **Step 2: Create the repository result wrapper**

Create `Ez.Handball.Domain/TournamentMatches.cs`:

```csharp
namespace Ez.Handball.Domain;

// Repository result for one tournament's fixtures. The repository returns null
// (not an empty instance) when the tournament id is unknown.
public sealed record TournamentMatches(
    string TournamentId,
    string? TournamentName,
    string Season,
    IReadOnlyList<MatchListItem> Matches);
```

- [ ] **Step 3: Create the API response records**

Create `Ez.Handball.Domain/RoundListing.cs`:

```csharp
namespace Ez.Handball.Domain;

public sealed record RoundListing(
    string TournamentId,
    string? TournamentName,
    string Season,
    IReadOnlyList<RoundGroup> Rounds);

public sealed record RoundGroup(
    string Round,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<RoundMatch> Matches);

public sealed record RoundMatch(
    string MatchId,
    bool Played,
    DateTimeOffset Date,
    string? Venue,
    RoundTeam Home,
    RoundTeam Away);

// Score is null for upcoming matches.
public sealed record RoundTeam(
    string TeamId,
    string ClubId,
    string? Name,
    string? LogoSrc,
    int? Score);
```

- [ ] **Step 4: Build to verify the records compile**

Run: `dotnet build Ez.Handball.Domain/Ez.Handball.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/MatchListItem.cs Ez.Handball.Domain/TournamentMatches.cs Ez.Handball.Domain/RoundListing.cs
git commit -m "feat(domain): add round-listing read-model records (#10)"
```

---

## Task 3: Repository — `ListByTournamentAsync`

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IMatchRepository.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TableMatchRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableMatchRepositoryTests.cs`

- [ ] **Step 1: Add the method to the interface**

In `Ez.Handball.Application/Abstractions/IMatchRepository.cs`, add inside the interface (after `GetByIdAsync`):

```csharp
    // All matches for a tournament, with club name/logo joined — for the round listing.
    // Returns null when the tournament id is not in the Tournaments table.
    Task<TournamentMatches?> ListByTournamentAsync(string tournamentId, CancellationToken ct);
```

- [ ] **Step 2: Write the failing tests**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TableMatchRepositoryTests.cs`. First add a helper setup for the Matches partition query and Clubs query near the other `Setup*` helpers (after line 29):

```csharp
    private void SetupMatchesByTournament(string tournamentId, params MatchEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<MatchEntity>(
                Ez.Handball.Infrastructure.Tables.Matches, $"PartitionKey eq '{tournamentId}'", default))
              .Returns(ToAsync(rows));

    private void SetupClubs(string filter, params ClubEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<ClubEntity>(
                Ez.Handball.Infrastructure.Tables.Clubs, filter, default))
              .Returns(ToAsync(rows));

    private static ClubEntity Club(string clubId, string name, string? logo) =>
        new() { PartitionKey = "club", RowKey = clubId, Name = name, LogoSrc = logo };
```

Add the import for `ClubEntity` if not already present (it shares the `Ez.Handball.Shared.Entities` namespace already imported at line 3 — no change needed).

Then add the tests:

```csharp
    [Fact]
    public async Task ListByTournamentAsync_UnknownTournament_ReturnsNull()
    {
        SetupTournament("9999"); // no rows

        var result = await CreateSut().ListByTournamentAsync("9999", default);

        Assert.Null(result);
        _query.Verify(q => q.QueryAsync<MatchEntity>(
            Ez.Handball.Infrastructure.Tables.Matches, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListByTournamentAsync_JoinsClubNameAndLogo_AndCarriesRound()
    {
        SetupTournament("8444", new TournamentEntity
        {
            PartitionKey = "2025-26", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar"
        });

        var match = Match("5001", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12);
        match.Round = "3";
        SetupMatchesByTournament("8444", match);

        SetupClubs(
            "PartitionKey eq 'club' and (RowKey eq '385' or RowKey eq '390')",
            Club("385", "KR", "https://logo/385.png"),
            Club("390", "Breiðablik", null));

        var result = await CreateSut().ListByTournamentAsync("8444", default);

        Assert.NotNull(result);
        Assert.Equal("Olís deild karla", result!.TournamentName);
        Assert.Equal("2025-26", result.Season);
        var item = Assert.Single(result.Matches);
        Assert.Equal("5001", item.MatchId);
        Assert.Equal("3", item.Round);
        Assert.Equal("385", item.Home.ClubId);
        Assert.Equal("KR", item.Home.ClubName);
        Assert.Equal("https://logo/385.png", item.Home.LogoSrc);
        Assert.Equal(28, item.Home.Score);
        Assert.Equal("Breiðablik", item.Away.ClubName);
        Assert.Null(item.Away.LogoSrc);
    }
```

Note: the existing `Match(...)` helper returns a `MatchEntity` whose `Round` defaults to `""`; the test sets `match.Round = "3"` after construction.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableMatchRepositoryTests.ListByTournamentAsync"`
Expected: FAIL — `IMatchRepository` has no `ListByTournamentAsync` (compile error).

- [ ] **Step 4: Implement `ListByTournamentAsync`**

In `Ez.Handball.Infrastructure/TableAccess/TableMatchRepository.cs`, add `using Ez.Handball.Infrastructure;` is not needed (`Tables` is referenced unqualified already? No — the existing code uses `Tables.Matches`; confirm the file already has access). The existing file uses `Tables.Matches`/`Tables.Tournaments`/`Tables.Teams` unqualified, so `Tables` is in scope. Add this method after `GetByIdAsync` (before `BuildTeam`):

```csharp
    public async Task<TournamentMatches?> ListByTournamentAsync(string tournamentId, CancellationToken ct)
    {
        var escaped = ODataFilter.Escape(tournamentId);

        string? tournamentName = null;
        string? season = null;
        await foreach (var t in _query.QueryAsync<TournamentEntity>(
                           Tables.Tournaments, $"RowKey eq '{escaped}'", ct))
        {
            tournamentName = t.Name;
            season = t.PartitionKey;
            break;
        }
        if (season is null) return null; // unknown tournament

        var matches = new List<MatchEntity>();
        await foreach (var row in _query.QueryAsync<MatchEntity>(
                           Tables.Matches, $"PartitionKey eq '{escaped}'", ct))
        {
            matches.Add(row);
        }

        var clubs = await LoadClubsAsync(matches, ct);

        var items = matches
            .Select(m => new MatchListItem(
                MatchId: m.RowKey,
                Round: m.Round,
                Date: m.Date,
                Venue: string.IsNullOrEmpty(m.Venue) ? null : m.Venue,
                Status: m.Status,
                Home: BuildListTeam(m.HomeTeamId, m.HomeScore, clubs),
                Away: BuildListTeam(m.AwayTeamId, m.AwayScore, clubs)))
            .ToList();

        return new TournamentMatches(tournamentId, tournamentName, season, items);
    }

    private async Task<IReadOnlyDictionary<string, ClubEntity>> LoadClubsAsync(
        IReadOnlyList<MatchEntity> matches, CancellationToken ct)
    {
        var clubIds = matches
            .SelectMany(m => new[] { ClubIdOf(m.HomeTeamId), ClubIdOf(m.AwayTeamId) })
            .Where(id => id.Length > 0)
            .Distinct()
            .ToList();

        var clubs = new Dictionary<string, ClubEntity>();
        if (clubIds.Count == 0) return clubs;

        var clubFilter = "PartitionKey eq 'club' and (" +
            string.Join(" or ", clubIds.Select(id => $"RowKey eq '{ODataFilter.Escape(id)}'")) + ")";
        await foreach (var c in _query.QueryAsync<ClubEntity>(Tables.Clubs, clubFilter, ct))
        {
            clubs[c.RowKey] = c;
        }
        return clubs;
    }

    private static string ClubIdOf(string teamId) => teamId.Split('-', 2)[0];

    private static MatchListTeam BuildListTeam(
        string teamId, int score, IReadOnlyDictionary<string, ClubEntity> clubs)
    {
        var clubId = ClubIdOf(teamId);
        clubs.TryGetValue(clubId, out var club);
        return new MatchListTeam(teamId, clubId, club?.Name, club?.LogoSrc, score);
    }
```

The `ClubEntity` type lives in `Ez.Handball.Shared.Entities` (already imported at the top of the file).

- [ ] **Step 5: Run the repository tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableMatchRepositoryTests"`
Expected: PASS (existing `GetByIdAsync` tests + the two new `ListByTournamentAsync` tests).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IMatchRepository.cs Ez.Handball.Infrastructure/TableAccess/TableMatchRepository.cs Ez.Handball.Tests/Infrastructure/Tables/TableMatchRepositoryTests.cs
git commit -m "feat(infra): list matches by tournament with club join (#10)"
```

---

## Task 4: Use case — `GetRoundsUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetRoundsUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetRoundsUseCaseTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Application/UseCases/GetRoundsUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetRoundsUseCaseTests
{
    private readonly Mock<IMatchRepository> _matches = new();

    private GetRoundsUseCase CreateSut() => new(_matches.Object);

    private static MatchListItem Item(string id, string round, DateTimeOffset date, string status,
        int homeScore = 0, int awayScore = 0) =>
        new(id, round, date, "Höllin", status,
            new MatchListTeam("385-karlar", "385", "KR", "logo-385", homeScore),
            new MatchListTeam("390-karlar", "390", "Breiðablik", null, awayScore));

    private void Setup(string tournamentId, params MatchListItem[] items) =>
        _matches.Setup(m => m.ListByTournamentAsync(tournamentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TournamentMatches(tournamentId, "Olís deild karla", "2025-26", items));

    [Fact]
    public async Task ExecuteAsync_UnknownTournament_ReturnsNotFound()
    {
        _matches.Setup(m => m.ListByTournamentAsync("9999", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("9999", default);

        Assert.IsType<GetRoundsResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersRounds_NumericAscendingThenTextLast()
    {
        Setup("8444",
            Item("a", "2", new DateTimeOffset(2025, 9, 10, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("b", "10", new DateTimeOffset(2025, 11, 1, 19, 0, 0, TimeSpan.Zero), "O"),
            Item("c", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("d", "Undanúrslit", new DateTimeOffset(2026, 4, 1, 19, 0, 0, TimeSpan.Zero), "O"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        Assert.Equal(new[] { "1", "2", "10", "Undanúrslit" },
            found.Listing.Rounds.Select(r => r.Round).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_PlayedMatch_SurfacesScore_UpcomingIsNull()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S", 28, 25),
            Item("b", "2", new DateTimeOffset(2025, 9, 10, 19, 0, 0, TimeSpan.Zero), "O", 0, 0));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single(r => r.Round == "1").Matches.Single();
        Assert.True(round1.Played);
        Assert.Equal(28, round1.Home.Score);
        Assert.Equal(25, round1.Away.Score);

        var round2 = found.Listing.Rounds.Single(r => r.Round == "2").Matches.Single();
        Assert.False(round2.Played);
        Assert.Null(round2.Home.Score);
        Assert.Null(round2.Away.Score);
    }

    [Fact]
    public async Task ExecuteAsync_MultiDayRound_HasDistinctStartAndEndDates()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("b", "1", new DateTimeOffset(2025, 9, 4, 14, 0, 0, TimeSpan.Zero), "S"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single(r => r.Round == "1");
        Assert.Equal(new DateOnly(2025, 9, 3), round1.StartDate);
        Assert.Equal(new DateOnly(2025, 9, 4), round1.EndDate);
        // matches sorted by kickoff
        Assert.Equal(new[] { "a", "b" }, round1.Matches.Select(m => m.MatchId).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_SingleDayRound_HasEqualStartAndEndDates()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single();
        Assert.Equal(round1.StartDate, round1.EndDate);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetRoundsUseCaseTests"`
Expected: FAIL — `GetRoundsUseCase` / `IGetRoundsUseCase` / `GetRoundsResult` do not exist (compile error).

- [ ] **Step 3: Implement the use case**

Create `Ez.Handball.Application/UseCases/GetRoundsUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetRoundsResult
{
    public sealed record NotFound : GetRoundsResult;
    public sealed record Found(RoundListing Listing) : GetRoundsResult;
}

public interface IGetRoundsUseCase
{
    Task<GetRoundsResult> ExecuteAsync(string tournamentId, CancellationToken ct);
}

public sealed class GetRoundsUseCase : IGetRoundsUseCase
{
    private readonly IMatchRepository _matches;

    public GetRoundsUseCase(IMatchRepository matches) => _matches = matches;

    public async Task<GetRoundsResult> ExecuteAsync(string tournamentId, CancellationToken ct)
    {
        var data = await _matches.ListByTournamentAsync(tournamentId, ct);
        if (data is null) return new GetRoundsResult.NotFound();

        var rounds = data.Matches
            .GroupBy(m => m.Round)
            .Select(BuildRound)
            // Numeric rounds ascending; non-numeric labels last, then by ordinal label.
            .OrderBy(r => int.TryParse(r.Round, out _) ? 0 : 1)
            .ThenBy(r => int.TryParse(r.Round, out var n) ? n : 0)
            .ThenBy(r => r.Round, StringComparer.Ordinal)
            .ToList();

        return new GetRoundsResult.Found(
            new RoundListing(data.TournamentId, data.TournamentName, data.Season, rounds));
    }

    private static RoundGroup BuildRound(IGrouping<string, MatchListItem> group)
    {
        var ordered = group.OrderBy(m => m.Date).ToList();
        var days = ordered.Select(m => DateOnly.FromDateTime(m.Date.Date)).ToList();
        return new RoundGroup(
            Round: group.Key,
            StartDate: days.Min(),
            EndDate: days.Max(),
            Matches: ordered.Select(ToRoundMatch).ToList());
    }

    private static RoundMatch ToRoundMatch(MatchListItem m)
    {
        var played = m.Status == "S";
        return new RoundMatch(
            MatchId: m.MatchId,
            Played: played,
            Date: m.Date,
            Venue: m.Venue,
            Home: ToRoundTeam(m.Home, played),
            Away: ToRoundTeam(m.Away, played));
    }

    private static RoundTeam ToRoundTeam(MatchListTeam t, bool played) =>
        new(t.TeamId, t.ClubId, t.ClubName, t.LogoSrc, played ? t.Score : null);
}
```

- [ ] **Step 4: Run the use-case tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetRoundsUseCaseTests"`
Expected: PASS (all five tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetRoundsUseCase.cs Ez.Handball.Tests/Application/UseCases/GetRoundsUseCaseTests.cs
git commit -m "feat(application): group tournament fixtures into rounds (#10)"
```

---

## Task 5: API edge — `GET /api/tournaments/{tournamentId}/rounds`

**Files:**
- Modify: `Ez.Handball.Api/Program.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/RoundsEndpointTests.cs`

- [ ] **Step 1: Write the failing endpoint tests**

Create `Ez.Handball.Tests/Api/Endpoints/RoundsEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Api.Endpoints;

public class RoundsEndpointTests : IClassFixture<RoundsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetRoundsUseCase> Rounds { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetRoundsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Rounds.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public RoundsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Rounds.Reset();
        _client = _factory.CreateClient();
    }

    private static RoundListing SampleListing() => new(
        "8444", "Olís deild karla", "2025-26",
        new[]
        {
            new RoundGroup("1", new DateOnly(2025, 9, 3), new DateOnly(2025, 9, 3),
                new[]
                {
                    new RoundMatch("5001", true, DateTimeOffset.UnixEpoch, "Höllin",
                        new RoundTeam("385-karlar", "385", "KR", "logo-385", 28),
                        new RoundTeam("390-karlar", "390", "Breiðablik", null, 25))
                })
        });

    [Fact]
    public async Task GetRounds_Found_Returns200WithListing()
    {
        _factory.Rounds
            .Setup(s => s.ExecuteAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRoundsResult.Found(SampleListing()));

        var response = await _client.GetAsync("/api/tournaments/8444/rounds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("8444", root.GetProperty("tournamentId").GetString());
        var round = root.GetProperty("rounds")[0];
        Assert.Equal("1", round.GetProperty("round").GetString());
        Assert.Equal("2025-09-03", round.GetProperty("startDate").GetString());
        var match = round.GetProperty("matches")[0];
        Assert.True(match.GetProperty("played").GetBoolean());
        Assert.Equal(28, match.GetProperty("home").GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task GetRounds_NotFound_Returns404WithErrorJson()
    {
        _factory.Rounds
            .Setup(s => s.ExecuteAsync("9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRoundsResult.NotFound());

        var response = await _client.GetAsync("/api/tournaments/9999/rounds");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("tournament_not_found", doc.RootElement.GetProperty("error").GetString());
    }
}
```

Note on the date assertion: `DateOnly` serializes to `"2025-09-03"` with the default System.Text.Json options used by the API.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~RoundsEndpointTests"`
Expected: FAIL — `IGetRoundsUseCase` is not registered (the `services.Single(...)` in the factory throws), and the route returns 404 from the framework (no mapping).

- [ ] **Step 3: Register the use case in DI**

In `Ez.Handball.Api/Program.cs`, add directly after the `IGetMatchUseCase` registration (line 124):

```csharp
builder.Services.AddScoped<IGetRoundsUseCase, GetRoundsUseCase>();
```

- [ ] **Step 4: Map the route**

In `Ez.Handball.Api/Program.cs`, add immediately after the `app.MapGet("/api/matches/{matchId}", ...)` block (ends around line 423):

```csharp
app.MapGet("/api/tournaments/{tournamentId}/rounds", async (
    string tournamentId,
    IGetRoundsUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(tournamentId))
        return Results.BadRequest(new { error = "invalid_tournament_id" });

    var result = await uc.ExecuteAsync(tournamentId, ct);
    return result switch
    {
        GetRoundsResult.NotFound  => Results.NotFound(new { error = "tournament_not_found" }),
        GetRoundsResult.Found f   => Results.Ok(f.Listing),
        _                         => Results.Problem()
    };
});
```

The `Ez.Handball.Application.UseCases` namespace is already imported in `Program.cs` (the existing match endpoint uses `GetMatchResult`).

- [ ] **Step 5: Run the endpoint tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~RoundsEndpointTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Api/Program.cs Ez.Handball.Tests/Api/Endpoints/RoundsEndpointTests.cs
git commit -m "feat(api): add GET /api/tournaments/{id}/rounds (#10)"
```

---

## Task 6: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full test suite**

Ensure Azurite is running first (some infra tests need it):
`azurite --silent --location /tmp/azurite-test &`

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: All tests pass (prior green count + the new MatchParser/repo/use-case/endpoint tests).

- [ ] **Step 3: Record the ops follow-up**

After deploying the ingestion change, `Round` must be backfilled onto existing matches. Because the parser reads round from the already-archived list blob, a plain reparse is sufficient — no hsi.is re-fetch:

```bash
# Per match (preferred — full reparse can time out ~60s in prod):
curl -X POST "https://<ingestion-host>/api/reparse?matchId=<id>"
# Or full reparse locally:
curl -X POST "http://localhost:7071/api/reparse"
```

Note this in the PR description so the deployer runs it.

---

## Self-Review Notes

- **Spec coverage:** rounds for a tournament's season (Task 3 reads season from Tournaments) ✓; date range per round → `startDate`/`endDate` (Task 4) ✓; score-if-played / time-if-upcoming → `played` + nullable score, `date` always present (Task 4) ✓; round capture in ingestion (Task 1) ✓; per-tournament scope + public + numeric-asc/text-last ordering + 404 for unknown tournament (Tasks 4/5) ✓; teams names+IDs, club logos, venue (Tasks 3/4) ✓.
- **Type consistency:** `MatchListItem`/`MatchListTeam`/`TournamentMatches` (Task 2) consumed unchanged by Task 3; `RoundListing`/`RoundGroup`/`RoundMatch`/`RoundTeam` (Task 2) produced by Task 4 and asserted by Task 5; `GetRoundsResult.NotFound`/`Found(RoundListing)` and `IGetRoundsUseCase.ExecuteAsync(string, CancellationToken)` consistent across Tasks 4 and 5.
- **No `season` query param** — tournament id pins the season, per the spec's Scope section.
