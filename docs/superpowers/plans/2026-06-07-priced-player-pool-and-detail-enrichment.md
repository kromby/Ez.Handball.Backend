# Priced Player Pool + Detail Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated filterable/sortable priced player-pool endpoint (`GET /api/players/pool`) and enrich the player-detail response (`GET /api/players/{playerId}`) with `price`, reusing the #52 salary + rating primitives with no re-derivation and no N+1 queries.

**Architecture:** A new `GetPlayerPoolUseCase` + `IPlayerPoolRepository` follows the existing `TableLeaderboardRepository` bulk-scan pattern: one `PlayerStats` scan, aggregate per player in-memory, join the `Players` table for name + position. The use case loads the fantasy scoring + price rule-sets once, then computes rating + salary per player through a new shared `FantasyPricing` helper (extracted from `PlayerSalaryService` so both the single-player salary path and the bulk pool path call the identical formula). `pickPercentage` ships as `null` (ownership aggregation deferred to a follow-up). Player-detail composes the existing per-player salary service (O(1)).

**Tech Stack:** C# / .NET 8, ASP.NET Core minimal APIs (`Ez.Handball.Api`), clean-architecture layering (Domain / Application / Infrastructure), Azure Table Storage, xUnit + Moq.

---

## Design reference

Spec: `docs/superpowers/specs/2026-06-07-priced-player-pool-and-detail-enrichment-design.md`

## File Structure

**Domain (`Ez.Handball.Domain`)**
- Create `PlayerPool.cs` — `PlayerPoolEntry`, `PlayerPool` response records.

**Application (`Ez.Handball.Application`)**
- Create `Abstractions/IPlayerPoolRepository.cs` — `PooledPlayer`, `PlayerPoolQuery`, `IPlayerPoolRepository`.
- Create `Services/FantasyPricing.cs` — shared rating+salary compute from already-aggregated stats.
- Create `UseCases/GetPlayerPoolUseCase.cs` — `PlayerPoolSort`, `PlayerPoolRequest`, `PlayerPoolResult`, `IGetPlayerPoolUseCase`, `GetPlayerPoolUseCase`.
- Modify `Services/PlayerSalaryService.cs` — delegate the formula to `FantasyPricing`.
- Modify `UseCases/GetPlayerProfileUseCase.cs` — `Found` carries `PlayerCost? Price`; inject `IPlayerSalaryService`.

**Infrastructure (`Ez.Handball.Infrastructure`)**
- Create `TableAccess/TablePlayerPoolRepository.cs` — bulk scan + aggregation + position join.
- Modify `InfrastructureRegistration.cs` — register `IPlayerPoolRepository`.

**Api (`Ez.Handball.Api`)**
- Modify `Program.cs` — DI for `FantasyPricing`, `IGetPlayerPoolUseCase`; map `GET /api/players/pool`; enrich `GET /api/players/{playerId}`; add `TryParsePoolSort` helper.

**Tests (`Ez.Handball.Tests`)**
- Create `Application/Services/FantasyPricingTests.cs`
- Create `Application/UseCases/GetPlayerPoolUseCaseTests.cs`
- Create `Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`
- Create `Api/Endpoints/PlayerPoolEndpointTests.cs`
- Modify `Application/UseCases/GetPlayerProfileUseCaseTests.cs`
- Modify `Api/Endpoints/PlayerEndpointsTests.cs`

---

## Task 1: Pool domain + repository contract

**Files:**
- Create: `Ez.Handball.Domain/PlayerPool.cs`
- Create: `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs`

No tests in this task — these are pure type declarations consumed by later tasks. They compile and are verified by the build step.

- [ ] **Step 1: Create the response domain records**

Create `Ez.Handball.Domain/PlayerPool.cs`:

```csharp
namespace Ez.Handball.Domain;

// One entry in the priced player pool ("transfer market"). PickPercentage is
// reserved — always null until the ownership aggregation follow-up ships.
public sealed record PlayerPoolEntry(
    int Rank,
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    PlayerCost Price,
    double Rating,
    double? PickPercentage);

public sealed record PlayerPool(
    string Sort,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PlayerPoolEntry> Entries);
```

- [ ] **Step 2: Create the repository contract**

Create `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// One player's scope-aggregated stats plus identity + position. The use case
// turns these into rating + price; the repository does NOT price anything.
public sealed record PooledPlayer(
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    AggregatedStats Stats);

// Use case → repository. TournamentIds is the resolved scope: null = whole-season
// scan; empty = scope matched no tournaments (repository returns nothing).
public sealed record PlayerPoolQuery(
    string? Season,
    IReadOnlyList<string>? TournamentIds,
    string? Gender);

public interface IPlayerPoolRepository
{
    // Returns every scoped player aggregated once (no ranking, no paging, no
    // position filter — the use case owns those).
    Task<IReadOnlyList<PooledPlayer>> GetAggregatedAsync(PlayerPoolQuery q, CancellationToken ct);
}
```

- [ ] **Step 3: Build to verify the new types compile**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Ez.Handball.Domain/PlayerPool.cs Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs
git commit -m "feat: pool domain records + repository contract (#67)"
```

---

## Task 2: Extract `FantasyPricing` shared helper + refactor `PlayerSalaryService`

**Files:**
- Create: `Ez.Handball.Application/Services/FantasyPricing.cs`
- Test: `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs`
- Modify: `Ez.Handball.Application/Services/PlayerSalaryService.cs`

This extracts the pure *compute-from-aggregated-stats* step so the salary endpoint and the bulk pool path share one formula (no re-derivation). Existing `PlayerSalaryService` output stays byte-for-byte identical.

- [ ] **Step 1: Write the failing test for `FantasyPricing`**

Create `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs`:

```csharp
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.Services;

public class FantasyPricingTests
{
    private static readonly ScoringRuleSet Scoring =
        new(GameFlavor.Fantasy, Version: 1,
            GoalPoints: 2, YellowCardPoints: -1, TwoMinutePoints: -1,
            RedCardPoints: -3, AppearancePoints: 1);

    // Bands: score < 5 => 1_000_000; 5..<10 => 5_000_000; >=10 => 11_000_000
    private static readonly SalaryRuleSet Prices =
        new(Version: 1, MinGames: 3, Currency: "ISK", Bands: new[]
        {
            new SalaryBand(0, 1_000_000),
            new SalaryBand(5, 5_000_000),
            new SalaryBand(10, 11_000_000),
        });

    private static readonly PlayerRatingContext Ctx = new(null, null, null, null, null, null);

    private FantasyPricing CreateSut() => new(new FantasyPlayerRatingFunction());

    [Fact]
    public void Compute_RatingIsSumOfWeightedComponents()
    {
        // 10 games, 50 goals, 0 cards: rating = 50*2 + 10*1 = 110
        var stats = new AggregatedStats(Games: 10, Goals: 50, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(110, result.Rating);
        // score = 110/10 = 11 => top band
        Assert.Equal(11, result.Score);
        Assert.Equal(11_000_000, result.Cost.Amount);
        Assert.Equal("ISK", result.Cost.Currency);
    }

    [Fact]
    public void Compute_BelowMinGames_ScoreIsZero_FloorBand()
    {
        // 2 games < MinGames(3): score forced to 0 => floor band
        var stats = new AggregatedStats(Games: 2, Goals: 40, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(82, result.Rating);     // rating still computed: 40*2 goals + 2*1 appearances
        Assert.Equal(0, result.Score);
        Assert.Equal(1_000_000, result.Cost.Amount);
    }

    [Fact]
    public void Compute_ZeroGames_ScoreIsZero()
    {
        var stats = new AggregatedStats(0, 0, 0, 0, 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(0, result.Rating);
        Assert.Equal(0, result.Score);
        Assert.Equal(1_000_000, result.Cost.Amount);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyPricingTests"`
Expected: FAIL — `FantasyPricing` does not exist (compile error).

- [ ] **Step 3: Implement `FantasyPricing`**

Create `Ez.Handball.Application/Services/FantasyPricing.cs`:

```csharp
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// The fantasy rating (#52 metric) + salary band for a player, computed from
// ALREADY-aggregated stats. Pure: no I/O, no rule-set loading. Both the
// single-player salary path and the bulk pool path call this so the formula
// lives in exactly one place.
public readonly record struct FantasyPriceResult(double Rating, double Score, PlayerCost Cost);

public sealed class FantasyPricing
{
    private readonly FantasyPlayerRatingFunction _rating;

    public FantasyPricing(FantasyPlayerRatingFunction rating) => _rating = rating;

    // The fantasy scoring rule-set version this pricing is built on.
    public int ScoringVersion => _rating.DefaultRuleSetVersion!.Value;

    public FantasyPriceResult Compute(
        string playerId,
        AggregatedStats stats,
        ScoringRuleSet scoring,
        SalaryRuleSet prices,
        PlayerRatingContext context)
    {
        var rating = _rating.Compute(new PlayerRatingInputs(playerId, stats, scoring, context)).Rating;
        var score = stats.Games >= prices.MinGames && stats.Games > 0 ? rating / stats.Games : 0;
        var band = prices.BandFor(score);
        return new FantasyPriceResult(rating, score, new PlayerCost(band.Price, prices.Currency));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyPricingTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Refactor `PlayerSalaryService` to delegate to `FantasyPricing`**

Replace the body of `Ez.Handball.Application/Services/PlayerSalaryService.cs` with:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class PlayerSalaryService : IPlayerSalaryService
{
    private readonly IPlayerStatsAggregator _aggregator;
    private readonly IScoringRuleSetRepository _scoring;
    private readonly ISalaryRuleSetRepository _prices;
    private readonly FantasyPricing _pricing;

    public PlayerSalaryService(
        IPlayerStatsAggregator aggregator,
        IScoringRuleSetRepository scoring,
        ISalaryRuleSetRepository prices,
        FantasyPricing pricing)
    {
        _aggregator = aggregator;
        _scoring = scoring;
        _prices = prices;
        _pricing = pricing;
    }

    public async Task<PlayerSalary?> GetSalaryAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct)
    {
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, _pricing.ScoringVersion, ct);
        if (scoring is null) return null;

        var priceRuleSet = await _prices.GetAsync(version, ct);
        if (priceRuleSet is null) return null;

        var stats = await _aggregator.AggregateAsync(playerId, season, tournamentId, null, null, ct);
        var ctx = new PlayerRatingContext(season, tournamentId, null, null, null, null);

        var result = _pricing.Compute(playerId, stats, scoring, priceRuleSet, ctx);
        return new PlayerSalary(
            playerId, result.Cost, result.Score, stats.Games, priceRuleSet.Name);
    }
}
```

- [ ] **Step 6: Register `FantasyPricing` in DI so existing salary wiring still resolves**

In `Ez.Handball.Api/Program.cs`, immediately after the line `builder.Services.AddScoped<FantasyPlayerRatingFunction>();` (currently line ~130), add:

```csharp
builder.Services.AddScoped<FantasyPricing>();
```

Add the using if not present at the top of `Program.cs`: `using Ez.Handball.Application.Services;` (verify — it may already be imported for `PlayerSalaryService`).

- [ ] **Step 7: Run the salary regression suite + build**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerSalary"`
Expected: PASS — all existing `GetPlayerSalaryUseCaseTests` and `PlayerSalaryEndpointTests` still green (output byte-identical).

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add Ez.Handball.Application/Services/FantasyPricing.cs \
        Ez.Handball.Application/Services/PlayerSalaryService.cs \
        Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs \
        Ez.Handball.Api/Program.cs
git commit -m "refactor: extract FantasyPricing shared compute helper (#67)"
```

---

## Task 3: `GetPlayerPoolUseCase`

**Files:**
- Create: `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerPoolUseCaseTests
{
    private readonly Mock<IPlayerPoolRepository> _repo = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();
    private readonly Mock<IScoringRuleSetRepository> _scoring = new();
    private readonly Mock<ISalaryRuleSetRepository> _prices = new();

    private static readonly ScoringRuleSet Scoring =
        new(GameFlavor.Fantasy, 1, GoalPoints: 2, YellowCardPoints: -1,
            TwoMinutePoints: -1, RedCardPoints: -3, AppearancePoints: 1);

    private static readonly SalaryRuleSet Prices =
        new(1, MinGames: 1, Currency: "ISK", Bands: new[]
        {
            new SalaryBand(0, 1_000_000),
            new SalaryBand(5, 5_000_000),
            new SalaryBand(10, 11_000_000),
        });

    private GetPlayerPoolUseCase CreateSut() =>
        new(_repo.Object, _scope.Object, _scoring.Object, _prices.Object,
            new FantasyPricing(new FantasyPlayerRatingFunction()));

    private void SetupRuleSets(ScoringRuleSet? scoring = null, SalaryRuleSet? prices = null)
    {
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(scoring ?? Scoring);
        _prices.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(prices ?? Prices);
    }

    private void SetupResolver(IReadOnlyList<string>? ids = null) =>
        _scope.Setup(s => s.ResolveTournamentIdsAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(ids);

    private void SetupPool(params PooledPlayer[] players) =>
        _repo.Setup(r => r.GetAggregatedAsync(It.IsAny<PlayerPoolQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(players);

    private static PooledPlayer Pooled(
        string playerId, int goals, int games = 10, string position = "CB", string gender = "karlar") =>
        new(playerId, $"P{playerId}", "385", "Stjarnan", gender, position,
            new AggregatedStats(games, goals, 0, 0, 0));

    private static PlayerPoolRequest Req(
        PlayerPoolSort sort = PlayerPoolSort.Rating, string? position = null,
        string? season = null, string? gender = null) =>
        new(season, null, null, null, gender, position, sort, PriceVersion: 1);

    [Fact]
    public async Task Execute_ComputesRatingAndPrice_PickPercentageNull()
    {
        SetupResolver();
        SetupRuleSets();
        // 10 games, 50 goals => rating 110, score 11 => top band 11_000_000
        SetupPool(Pooled("a", goals: 50));

        var result = await CreateSut().ExecuteAsync(Req(), offset: 0, limit: 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        var entry = Assert.Single(pool.Entries);
        Assert.Equal(110, entry.Rating);
        Assert.Equal(11_000_000, entry.Price.Amount);
        Assert.Equal("ISK", entry.Price.Currency);
        Assert.Equal("CB", entry.Position);
        Assert.Null(entry.PickPercentage);
        Assert.Equal(1, entry.Rank);
    }

    [Fact]
    public async Task Execute_DefaultSort_OrdersByRatingDescending()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(Pooled("low", goals: 10), Pooled("high", goals: 60), Pooled("mid", goals: 30));

        var result = await CreateSut().ExecuteAsync(Req(), 0, 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(new[] { "high", "mid", "low" }, pool.Entries.Select(e => e.PlayerId));
        Assert.Equal(new[] { 1, 2, 3 }, pool.Entries.Select(e => e.Rank));
    }

    [Fact]
    public async Task Execute_SortByPrice_OrdersByPriceDescending()
    {
        SetupResolver();
        SetupRuleSets();
        // rating = goals*2 + games*1; score = rating/games drives the band
        SetupPool(
            Pooled("cheap", goals: 10, games: 10),   // rating 30, score 3  => floor 1_000_000
            Pooled("rich", goals: 60, games: 10));   // rating 130, score 13 => 11_000_000

        var result = await CreateSut().ExecuteAsync(
            Req(sort: PlayerPoolSort.Price), 0, 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(new[] { "rich", "cheap" }, pool.Entries.Select(e => e.PlayerId));
        Assert.Equal("Price", pool.Sort);
    }

    [Fact]
    public async Task Execute_SortByPickPercentage_NoError_StableRatingOrder()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(Pooled("low", goals: 10), Pooled("high", goals: 60));

        var result = await CreateSut().ExecuteAsync(
            Req(sort: PlayerPoolSort.PickPercentage), 0, 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        // all pickPercentage null => falls through to rating-desc tie-break
        Assert.Equal(new[] { "high", "low" }, pool.Entries.Select(e => e.PlayerId));
        Assert.All(pool.Entries, e => Assert.Null(e.PickPercentage));
    }

    [Fact]
    public async Task Execute_PositionFilter_NarrowsToMatchingCode_CaseInsensitive()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(
            Pooled("a", goals: 50, position: "CB"),
            Pooled("b", goals: 40, position: "GK"),
            Pooled("c", goals: 30, position: "cb"));

        var result = await CreateSut().ExecuteAsync(
            Req(position: "CB"), 0, 50, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(new[] { "a", "c" }, pool.Entries.Select(e => e.PlayerId));
        Assert.Equal(2, pool.Total);
    }

    [Fact]
    public async Task Execute_PagesOverFullSortedSet_ReportsFullTotal()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPool(
            Pooled("a", goals: 60), Pooled("b", goals: 50),
            Pooled("c", goals: 40), Pooled("d", goals: 30));

        var result = await CreateSut().ExecuteAsync(Req(), offset: 1, limit: 2, CancellationToken.None);

        var pool = Assert.IsType<PlayerPoolResult.Found>(result).Pool;
        Assert.Equal(4, pool.Total);
        Assert.Equal(new[] { "b", "c" }, pool.Entries.Select(e => e.PlayerId));
        Assert.Equal(new[] { 2, 3 }, pool.Entries.Select(e => e.Rank));
        Assert.Equal(1, pool.Offset);
        Assert.Equal(2, pool.Limit);
    }

    [Fact]
    public async Task Execute_ScoringRuleSetMissing_ReturnsRuleSetNotFound()
    {
        SetupResolver();
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScoringRuleSet?)null);
        _prices.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Prices);

        var result = await CreateSut().ExecuteAsync(Req(), 0, 50, CancellationToken.None);

        Assert.IsType<PlayerPoolResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Execute_PriceRuleSetMissing_ReturnsRuleSetNotFound()
    {
        SetupResolver();
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Scoring);
        _prices.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SalaryRuleSet?)null);

        var result = await CreateSut().ExecuteAsync(Req(), 0, 50, CancellationToken.None);

        Assert.IsType<PlayerPoolResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Execute_ForwardsResolvedScopeAndGenderToRepository()
    {
        SetupResolver(new[] { "8444" });
        SetupRuleSets();
        PlayerPoolQuery? captured = null;
        _repo.Setup(r => r.GetAggregatedAsync(It.IsAny<PlayerPoolQuery>(), It.IsAny<CancellationToken>()))
             .Callback<PlayerPoolQuery, CancellationToken>((q, _) => captured = q)
             .ReturnsAsync(Array.Empty<PooledPlayer>());

        await CreateSut().ExecuteAsync(
            Req(season: "2025-26", gender: "karlar"), 0, 50, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("2025-26", captured!.Season);
        Assert.Equal(new[] { "8444" }, captured.TournamentIds);
        Assert.Equal("karlar", captured.Gender);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests"`
Expected: FAIL — `GetPlayerPoolUseCase`, `PlayerPoolRequest`, `PlayerPoolSort`, `PlayerPoolResult` do not exist (compile error).

- [ ] **Step 3: Implement the use case**

Create `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public enum PlayerPoolSort
{
    Rating,
    Price,
    PickPercentage
}

// Edge → use case. Carries the raw, unresolved scope + the chosen sort + the
// price rule-set version (default 1 at the edge).
public sealed record PlayerPoolRequest(
    string? Season,
    string? TournamentId,
    string? CompetitionId,
    TournamentType? Type,
    string? Gender,
    string? Position,
    PlayerPoolSort Sort,
    int PriceVersion);

public abstract record PlayerPoolResult
{
    public sealed record RuleSetNotFound : PlayerPoolResult;
    public sealed record Found(PlayerPool Pool) : PlayerPoolResult;
}

public interface IGetPlayerPoolUseCase
{
    Task<PlayerPoolResult> ExecuteAsync(
        PlayerPoolRequest request, int offset, int limit, CancellationToken ct);
}

public sealed class GetPlayerPoolUseCase : IGetPlayerPoolUseCase
{
    private readonly IPlayerPoolRepository _repo;
    private readonly ITournamentScopeResolver _scope;
    private readonly IScoringRuleSetRepository _scoring;
    private readonly ISalaryRuleSetRepository _prices;
    private readonly FantasyPricing _pricing;

    public GetPlayerPoolUseCase(
        IPlayerPoolRepository repo,
        ITournamentScopeResolver scope,
        IScoringRuleSetRepository scoring,
        ISalaryRuleSetRepository prices,
        FantasyPricing pricing)
    {
        _repo = repo;
        _scope = scope;
        _scoring = scoring;
        _prices = prices;
        _pricing = pricing;
    }

    public async Task<PlayerPoolResult> ExecuteAsync(
        PlayerPoolRequest request, int offset, int limit, CancellationToken ct)
    {
        // Load both rule-sets ONCE up front; bail before any per-player work if missing.
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, _pricing.ScoringVersion, ct);
        if (scoring is null) return new PlayerPoolResult.RuleSetNotFound();

        var prices = await _prices.GetAsync(request.PriceVersion, ct);
        if (prices is null) return new PlayerPoolResult.RuleSetNotFound();

        var tournamentIds = await _scope.ResolveTournamentIdsAsync(
            request.Season, request.TournamentId, request.CompetitionId, request.Type, ct);

        var query = new PlayerPoolQuery(request.Season, tournamentIds, request.Gender);
        var players = await _repo.GetAggregatedAsync(query, ct);

        // Context is accepted by the rating function but unused by the fantasy
        // formula; pass the season for completeness.
        var ctx = new PlayerRatingContext(request.Season, null, null, null, null, null);

        var computed = players
            .Where(p => string.IsNullOrWhiteSpace(request.Position)
                || string.Equals(p.Position, request.Position, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var priced = _pricing.Compute(p.PlayerId, p.Stats, scoring, prices, ctx);
                return new PlayerPoolEntry(
                    Rank: 0,
                    PlayerId: p.PlayerId,
                    Name: p.Name,
                    ClubId: p.ClubId,
                    ClubName: p.ClubName,
                    Gender: p.Gender,
                    Position: p.Position,
                    Price: priced.Cost,
                    Rating: priced.Rating,
                    PickPercentage: null); // deferred — ownership aggregation follow-up
            });

        var sorted = Sort(computed, request.Sort).ToList();

        var ranked = new List<PlayerPoolEntry>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
            ranked.Add(sorted[i] with { Rank = i + 1 });

        var page = ranked.Skip(offset).Take(limit).ToList();
        var pool = new PlayerPool(request.Sort.ToString(), ranked.Count, offset, limit, page);
        return new PlayerPoolResult.Found(pool);
    }

    // Stable tie-break: rating desc, then playerId ordinal. sort=PickPercentage
    // is accepted but every value is null, so it falls through to the tie-break.
    private static IEnumerable<PlayerPoolEntry> Sort(IEnumerable<PlayerPoolEntry> entries, PlayerPoolSort sort) =>
        sort switch
        {
            PlayerPoolSort.Price => entries
                .OrderByDescending(e => e.Price.Amount)
                .ThenByDescending(e => e.Rating)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal),
            PlayerPoolSort.PickPercentage => entries
                .OrderByDescending(e => e.PickPercentage ?? double.NegativeInfinity)
                .ThenByDescending(e => e.Rating)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal),
            _ => entries
                .OrderByDescending(e => e.Rating)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal),
        };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs
git commit -m "feat: GetPlayerPoolUseCase with rating/price/sort/position filter (#67)"
```

---

## Task 4: `TablePlayerPoolRepository`

**Files:**
- Create: `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`

This mirrors `TableLeaderboardRepository`: one `PlayerStats` scan, in-memory aggregation grouped by player, gender filter, and a `Players` join for `Name` + `Position`. It produces `PooledPlayer` (stats only — no pricing).

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePlayerPoolRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IPlayerPoolRepository CreateSut() =>
        new TablePlayerPoolRepository(_query.Object, NullLogger<TablePlayerPoolRepository>.Instance);

    private void SetupStats(params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                  Ez.Handball.Infrastructure.Tables.PlayerStats, It.IsAny<string?>(), default))
              .Returns(ToAsync(rows));

    private void SetupPlayers(params PlayerEntity[] players) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(
                  Ez.Handball.Infrastructure.Tables.Players, It.IsAny<string>(), default))
              .Returns(ToAsync(players));

    private static PlayerStatEntity Stat(
        string matchId, string playerId, string season, string tournamentId,
        string teamId, string? clubName, int g) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId,
            Goals = g, YellowCards = 0, TwoMinuteSuspensions = 0, RedCards = 0,
            TournamentId = tournamentId, Season = season, TeamId = teamId, ClubName = clubName
        };

    private static PlayerEntity Plr(string playerId, string teamId, string name, string position) =>
        new() { PartitionKey = teamId, RowKey = playerId, Name = name, Position = position };

    private static PlayerPoolQuery Q(
        string? season = null, IReadOnlyList<string>? tournamentIds = null, string? gender = null) =>
        new(season, tournamentIds, gender);

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAggregated_SumsStatsPerPlayer_JoinsNameAndPosition()
    {
        SetupStats(
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 3));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal("p1", p.PlayerId);
        Assert.Equal("Aron", p.Name);
        Assert.Equal("CB", p.Position);
        Assert.Equal("385", p.ClubId);
        Assert.Equal("karlar", p.Gender);
        Assert.Equal(2, p.Stats.Games);
        Assert.Equal(8, p.Stats.Goals);
    }

    [Fact]
    public async Task GetAggregated_GenderFilter_DropsOtherGender()
    {
        SetupStats(
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p2", "2025-26", "8434", "385-kvenna", "Stjarnan", 7));
        SetupPlayers(
            Plr("p1", "385-karlar", "Aron", "CB"),
            Plr("p2", "385-kvenna", "Anna", "GK"));

        var result = await CreateSut().GetAggregatedAsync(Q(gender: "karlar"), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal("p1", p.PlayerId);
    }

    [Fact]
    public async Task GetAggregated_EmptyTournamentScope_ReturnsEmpty()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(tournamentIds: Array.Empty<string>()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAggregated_MissingPlayerRow_PositionEmpty_NameNull()
    {
        SetupStats(Stat("m1", "p9", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(); // no Players rows

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Null(p.Name);
        Assert.Equal(string.Empty, p.Position);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests"`
Expected: FAIL — `TablePlayerPoolRepository` does not exist (compile error).

- [ ] **Step 3: Implement the repository**

Create `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerPoolRepository : IPlayerPoolRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TablePlayerPoolRepository> _logger;

    public TablePlayerPoolRepository(ITableQuery query, ILogger<TablePlayerPoolRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PooledPlayer>> GetAggregatedAsync(PlayerPoolQuery q, CancellationToken ct)
    {
        // Non-null but empty scope = "matched no tournaments" => no results.
        if (q.TournamentIds is { Count: 0 })
            return Array.Empty<PooledPlayer>();

        var filter = BuildFilter(q);

        var rows = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(Tables.PlayerStats, filter, ct))
            rows.Add(row);

        if (!string.IsNullOrEmpty(q.Gender))
            rows = rows.Where(r => GenderOf(r.TeamId) == q.Gender).ToList();

        if (rows.Count == 0) return Array.Empty<PooledPlayer>();

        // Join the Players table once for name + position.
        var playerById = new Dictionary<string, PlayerEntity>();
        await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, null, ct))
            playerById[p.RowKey] = p;

        var result = rows
            .GroupBy(r => r.RowKey)
            .Select(g =>
            {
                var (clubId, clubName, gender) = ResolveClub(g.ToList());
                playerById.TryGetValue(g.Key, out var player);
                if (player is null)
                    _logger.LogWarning(
                        "Player {PlayerId} not found in Players table while building pool", g.Key);

                var stats = new AggregatedStats(
                    Games: g.Count(),
                    Goals: g.Sum(r => r.Goals),
                    YellowCards: g.Sum(r => r.YellowCards),
                    TwoMinuteSuspensions: g.Sum(r => r.TwoMinuteSuspensions),
                    RedCards: g.Sum(r => r.RedCards));

                return new PooledPlayer(
                    PlayerId: g.Key,
                    Name: player?.Name,
                    ClubId: clubId,
                    ClubName: clubName,
                    Gender: gender,
                    Position: player?.Position ?? string.Empty,
                    Stats: stats);
            })
            .ToList();

        return result;
    }

    private static string? BuildFilter(PlayerPoolQuery q)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(q.Season))
            clauses.Add($"Season eq '{ODataFilter.Escape(q.Season)}'");

        if (q.TournamentIds is { Count: > 0 })
        {
            var ors = string.Join(" or ",
                q.TournamentIds.Select(id => $"TournamentId eq '{ODataFilter.Escape(id)}'"));
            var needsParens = q.TournamentIds.Count > 1 || clauses.Count > 0;
            clauses.Add(needsParens ? $"({ors})" : ors);
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    // The club the player scored most for in scope (matches leaderboard tie-breaks).
    private static (string ClubId, string? ClubName, string Gender) ResolveClub(List<PlayerStatEntity> rows)
    {
        var club = rows
            .GroupBy(r => ClubIdOf(r.TeamId))
            .Select(cg => new
            {
                ClubId = cg.Key,
                Goals = cg.Sum(r => r.Goals),
                Games = cg.Count(),
                ClubName = cg.Select(r => r.ClubName).FirstOrDefault(n => n != null),
                TeamId = cg.First().TeamId
            })
            .OrderByDescending(c => c.Goals)
            .ThenByDescending(c => c.Games)
            .ThenBy(c => c.ClubId, StringComparer.Ordinal)
            .First();

        return (club.ClubId, club.ClubName, GenderOf(club.TeamId));
    }

    private static string ClubIdOf(string teamId) => teamId.Split('-', 2)[0];

    private static string GenderOf(string teamId)
    {
        var parts = teamId.Split('-', 2);
        return parts.Length == 2 ? parts[1] : string.Empty;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Register the repository in DI**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, after the line `services.AddScoped<ILeaderboardRepository, TableLeaderboardRepository>();`, add:

```csharp
        services.AddScoped<IPlayerPoolRepository, TablePlayerPoolRepository>();
```

- [ ] **Step 6: Build to verify wiring compiles**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs \
        Ez.Handball.Infrastructure/InfrastructureRegistration.cs \
        Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs
git commit -m "feat: TablePlayerPoolRepository bulk aggregation + position join (#67)"
```

---

## Task 5: Map `GET /api/players/pool`

**Files:**
- Modify: `Ez.Handball.Api/Program.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/PlayerPoolEndpointTests.cs`

- [ ] **Step 1: Write the failing endpoint tests**

Create `Ez.Handball.Tests/Api/Endpoints/PlayerPoolEndpointTests.cs`:

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

namespace Ez.Handball.Tests.Api.Endpoints;

public class PlayerPoolEndpointTests : IClassFixture<PlayerPoolEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayerPoolUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerPoolUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerPoolEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static PlayerPool EmptyPool(string sort = "Rating") =>
        new(sort, 0, 0, 50, Array.Empty<PlayerPoolEntry>());

    private void SetupFound(PlayerPool pool) =>
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerPoolResult.Found(pool));

    [Fact]
    public async Task Get_NoParams_DefaultsRatingSortOffset0Limit50Version1()
    {
        PlayerPoolRequest? captured = null;
        var capturedOffset = -1;
        var capturedLimit = -1;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, o, l, _) =>
            {
                captured = r; capturedOffset = o; capturedLimit = l;
            })
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool()));

        var response = await _client.GetAsync("/api/players/pool");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(PlayerPoolSort.Rating, captured!.Sort);
        Assert.Equal(1, captured.PriceVersion);
        Assert.Null(captured.Position);
        Assert.Equal(0, capturedOffset);
        Assert.Equal(50, capturedLimit);
    }

    [Fact]
    public async Task Get_PassesFiltersAndSortThrough()
    {
        PlayerPoolRequest? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, _, _, _) => captured = r)
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool("Price")));

        var response = await _client.GetAsync(
            "/api/players/pool?season=2025-26&gender=karlar&position=CB&sort=price&version=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2025-26", captured!.Season);
        Assert.Equal("karlar", captured.Gender);
        Assert.Equal("CB", captured.Position);
        Assert.Equal(PlayerPoolSort.Price, captured.Sort);
        Assert.Equal(2, captured.PriceVersion);
    }

    [Fact]
    public async Task Get_SortPickPercentage_Accepted()
    {
        SetupFound(EmptyPool("PickPercentage"));

        var response = await _client.GetAsync("/api/players/pool?sort=pickPercentage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidSort_Returns400()
    {
        var response = await _client.GetAsync("/api/players/pool?sort=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_sort", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_InvalidGender_Returns400()
    {
        var response = await _client.GetAsync("/api/players/pool?gender=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_TournamentIdAndCompetitionId_Returns400()
    {
        var response = await _client.GetAsync(
            "/api/players/pool?tournamentId=8444&competitionId=olis-karla");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_scope", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_LimitTooLarge_Returns400()
    {
        var response = await _client.GetAsync("/api/players/pool?limit=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_RuleSetNotFound_Returns400InvalidRuleSet()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerPoolResult.RuleSetNotFound());

        var response = await _client.GetAsync("/api/players/pool");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPoolEndpointTests"`
Expected: FAIL — endpoint returns 404 (not mapped) / `IGetPlayerPoolUseCase` not registered.

- [ ] **Step 3: Register the use case in DI**

In `Ez.Handball.Api/Program.cs`, after the line `builder.Services.AddScoped<IGetLeaderboardUseCase, GetLeaderboardUseCase>();` (currently line ~122), add:

```csharp
builder.Services.AddScoped<IGetPlayerPoolUseCase, GetPlayerPoolUseCase>();
```

- [ ] **Step 4: Map the endpoint**

In `Ez.Handball.Api/Program.cs`, immediately after the `/api/leaderboard` mapping block (the block ending at line ~353, before `app.MapGet("/api/matches/{matchId}"...`), insert:

```csharp
app.MapGet("/api/players/pool", async Task<IResult> (
    string? season,
    string? tournamentId,
    string? competitionId,
    string? type,
    string? gender,
    string? position,
    string? sort,
    int? offset,
    int? limit,
    int? version,
    IGetPlayerPoolUseCase uc,
    CancellationToken ct) =>
{
    if (!TryParsePoolSort(sort, out var parsedSort))
        return Results.BadRequest(new { error = "invalid_sort" });

    if (!TryNormalizeGender(gender, out var parsedGender))
        return Results.BadRequest(new { error = "invalid_gender" });

    if (!TryParseTournamentType(type, out var parsedType))
        return Results.BadRequest(new { error = "invalid_type" });

    if (!string.IsNullOrWhiteSpace(tournamentId) && !string.IsNullOrWhiteSpace(competitionId))
        return Results.BadRequest(new { error = "invalid_scope" });

    var off = offset ?? 0;
    var lim = limit ?? 50;
    if (off < 0 || lim < 1 || lim > 200)
        return Results.BadRequest(new { error = "invalid_pagination" });

    var request = new PlayerPoolRequest(
        season, tournamentId, competitionId, parsedType, parsedGender, position,
        parsedSort, version ?? 1);

    var result = await uc.ExecuteAsync(request, off, lim, ct);
    return result switch
    {
        PlayerPoolResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
        PlayerPoolResult.Found f         => Results.Ok(f.Pool),
        _                                => Results.Problem()
    };
});
```

- [ ] **Step 5: Add the `TryParsePoolSort` helper**

In `Ez.Handball.Api/Program.cs`, after the `TryParseMetric` static local function (currently ends ~line 441), add:

```csharp
static bool TryParsePoolSort(string? value, out PlayerPoolSort sort)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        sort = PlayerPoolSort.Rating;
        return true;
    }
    return Enum.TryParse(value, ignoreCase: true, out sort) && Enum.IsDefined(sort);
}
```

- [ ] **Step 6: Run the endpoint tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPoolEndpointTests"`
Expected: PASS (8 tests).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Api/Program.cs \
        Ez.Handball.Tests/Api/Endpoints/PlayerPoolEndpointTests.cs
git commit -m "feat: map GET /api/players/pool endpoint (#67)"
```

---

## Task 6: Part B — enrich player-detail with `price`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/GetPlayerProfileUseCase.cs`
- Modify: `Ez.Handball.Api/Program.cs`
- Modify: `Ez.Handball.Tests/Application/UseCases/GetPlayerProfileUseCaseTests.cs`
- Modify: `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`

The detail page needs `price` (Money) and `position`. `Position` is already on `Player`; this task adds `price` via the existing per-player `IPlayerSalaryService` (single player, O(1)). `price` is `null` when the rule-set is absent; `404` unchanged.

- [ ] **Step 1: Update the use-case tests (failing)**

Replace `Ez.Handball.Tests/Application/UseCases/GetPlayerProfileUseCaseTests.cs` with:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerProfileUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerSalaryService> _salary = new();

    private GetPlayerProfileUseCase CreateSut() => new(_players.Object, _salary.Object);

    private static Player SamplePlayer() => new(
        PlayerId: "12345", Name: "Aron Pálmarsson", JerseyNumber: "23",
        DateOfBirth: new DateOnly(1990, 7, 19), Age: 35,
        TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
        Position: "VS");

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_ReturnsNotFound()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetPlayerProfileResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExists_ReturnsFoundWithPlayerAndPrice()
    {
        var player = SamplePlayer();
        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        _salary
            .Setup(s => s.GetSalaryAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerSalary(
                "12345", new PlayerCost(11_000_000, "ISK"), Score: 11, Games: 10, Version: "fantasy-price-v1"));

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.NotNull(found.Price);
        Assert.Equal(11_000_000, found.Price!.Amount);
        Assert.Equal("ISK", found.Price.Currency);
    }

    [Fact]
    public async Task ExecuteAsync_RuleSetMissing_ReturnsFoundWithNullPrice()
    {
        var player = SamplePlayer();
        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        _salary
            .Setup(s => s.GetSalaryAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerSalary?)null);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.Null(found.Price);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_DoesNotCallSalary()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        _salary.Verify(s => s.GetSalaryAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerProfileUseCaseTests"`
Expected: FAIL — `GetPlayerProfileUseCase` constructor takes one arg; `Found` has no `Price` (compile error).

- [ ] **Step 3: Update the use case**

Replace `Ez.Handball.Application/UseCases/GetPlayerProfileUseCase.cs` with:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerProfileResult
{
    public sealed record NotFound : GetPlayerProfileResult;
    public sealed record Found(Player Player, PlayerCost? Price) : GetPlayerProfileResult;
}

public interface IGetPlayerProfileUseCase
{
    Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct);
}

public class GetPlayerProfileUseCase : IGetPlayerProfileUseCase
{
    // The default fantasy price rule-set version (matches GetPlayerSalaryUseCase).
    private const int DefaultPriceVersion = 1;

    private readonly IPlayerRepository _players;
    private readonly IPlayerSalaryService _salary;

    public GetPlayerProfileUseCase(IPlayerRepository players, IPlayerSalaryService salary)
    {
        _players = players;
        _salary = salary;
    }

    public async Task<GetPlayerProfileResult> ExecuteAsync(string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerProfileResult.NotFound();

        // Season/tournament null => current-season salary. Null when the rule-set
        // is absent; the rest of the profile still returns.
        var salary = await _salary.GetSalaryAsync(playerId, DefaultPriceVersion, null, null, ct);
        return new GetPlayerProfileResult.Found(player, salary?.Cost);
    }
}
```

- [ ] **Step 4: Update the endpoint to serialize price (flat shape)**

In `Ez.Handball.Api/Program.cs`, in the `/api/players/{playerId}` mapping (currently line ~164), replace the `GetPlayerProfileResult.Found f => Results.Ok(f.Player),` arm with:

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
            price = f.Price
        }),
```

- [ ] **Step 5: Update the existing player-detail endpoint test**

In `Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs`, in `GetPlayer_Existing_Returns200WithProfile`, change the `ReturnsAsync` line from:

```csharp
            .ReturnsAsync(new GetPlayerProfileResult.Found(player));
```

to:

```csharp
            .ReturnsAsync(new GetPlayerProfileResult.Found(player, new PlayerCost(11_000_000, "ISK")));
```

and add these assertions at the end of that test (after the `age` assertion):

```csharp
        Assert.Equal("VS", body.GetProperty("position").GetString());
        var price = body.GetProperty("price");
        Assert.Equal(11_000_000, price.GetProperty("amount").GetDouble());
        Assert.Equal("ISK", price.GetProperty("currency").GetString());
```

- [ ] **Step 6: Run the affected suites to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerProfileUseCaseTests|FullyQualifiedName~PlayerEndpointsTests"`
Expected: PASS — use-case tests (4) and player endpoint tests all green.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetPlayerProfileUseCase.cs \
        Ez.Handball.Api/Program.cs \
        Ez.Handball.Tests/Application/UseCases/GetPlayerProfileUseCaseTests.cs \
        Ez.Handball.Tests/Api/Endpoints/PlayerEndpointsTests.cs
git commit -m "feat: enrich player-detail response with price (#67)"
```

---

## Task 7: Full suite green + final verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Run the full test suite**

Ensure Azurite is running if any integration tests require it (see CLAUDE.md), then:

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS — all tests green (previous count + the new FantasyPricing, pool use case, pool repo, pool endpoint, and updated profile tests).

- [ ] **Step 3: Confirm no regression in salary/rating output**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerSalary|FullyQualifiedName~PlayerRating"`
Expected: PASS — the `FantasyPricing` refactor did not change salary/rating behaviour.

- [ ] **Step 4: Final commit (only if any verification fix was needed)**

If steps 1–3 surfaced anything requiring a fix, commit it:

```bash
git add -A
git commit -m "test: verify full suite green for player pool + detail enrichment (#67)"
```

Otherwise no commit is needed — the work is already committed task-by-task.

---

## Acceptance criteria coverage

- **Priced list carries `price`, `position`, `rating`, `pickPercentage`** → Tasks 1, 3, 4 (entry shape + compute + repo).
- **Position filter + sort by `rating`/`price`/`pickPercentage`, tested** → Task 3 (use case tests), Task 5 (endpoint param parsing).
- **Player detail carries `price` + `position`, tested** → Task 6.
- **`price` reuses salary primitive (#52); `rating` reuses #52 metric — no re-derivation** → Task 2 (shared `FantasyPricing`, regression-guarded).
- **Position values are the stored codes** → Task 4 (joins `PlayerEntity.Position` verbatim).
- **O(1) Table queries for the list (bulk scan + rule-sets once), not O(N)** → Task 3 (rule-sets loaded once) + Task 4 (single stats scan + single players scan).
- **`sort=pickPercentage` accepted, stable no-op while null** → Task 3 (`Sort` tie-break) + Task 5 (endpoint accepts it).

## Out of scope (filed as follow-up)

`pickPercentage` ownership aggregation (squad-ownership %). The field ships as `null`; the aggregation is a separate issue, meaningful once #55 squads are populated.
