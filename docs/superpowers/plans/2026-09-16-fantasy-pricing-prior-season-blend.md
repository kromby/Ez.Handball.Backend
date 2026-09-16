# Fantasy Pricing: Prior-Season Blend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop every player in a competition from pricing at the flat floor band (5,000,000 ISK) at the start of a season by fading a player's price from last season's rate toward their current-season rate as they accumulate games.

**Architecture:** `FantasyPricing.Compute` gains an optional `previousSeasonStats` parameter and blends its rate into the price-driving `Score` with a weight proportional to `currentGames / BlendGames` (a new tunable on `PriceRuleSet`). Two independent data-fetch paths — the single-player path (`PlayerStatsAggregator` → `PlayerPriceService`) and the bulk pool path (`TablePlayerPoolRepository` → `GetPlayerPoolUseCase`) — are each responsible only for resolving and supplying last season's same-competition stats; the blend math itself lives in exactly one place. Resolving "last season, same competition" reuses the existing `CompetitionId`-based tournament lookup already used for current-season scoping.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, Azure Table Storage (Azurite locally), xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-09-16-fantasy-pricing-prior-season-blend-design.md`

## Global Constraints

- `BlendGames` must be greater than `MinGames` (documented, not runtime-validated — config-level assumption, same as other seeded rule-set tuning).
- Only the price-driving `Score` blends; `PlayerPricing.Rating`/`PlayerPoolEntry.Rating` stay current-season-only (unchanged from today).
- Prior-season qualification requires the same `CompetitionId` and `priorGames >= MinGames` (spec decisions 3–4).
- Prior-season rate is recomputed through the *current* `ScoringRuleSet`, never a historical one (spec decision 5).
- `GetPlayerRatingUseCase` is out of scope — untouched.
- Every changed record gains new fields as **trailing optional/default parameters** so existing positional test call sites keep compiling without modification, except where a file's own tests are the task's explicit deliverable.

---

## Task 1: `BlendGames` config plumbing

**Files:**
- Modify: `Ez.Handball.Domain/PriceRuleSet.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePriceRuleSetRepository.cs`
- Modify: `Ez.Handball.Ingestion/Functions/SeedPriceRuleSetsFunction.cs`
- Test: `Ez.Handball.Tests/Domain/PriceRuleSetTests.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TablePriceRuleSetRepositoryTests.cs`
- Test: `Ez.Handball.Tests/Ingestion/Functions/SeedPriceRuleSetsFunctionTests.cs`
- Test: `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs` (compile fix only — new field on the `Prices` fixture)
- Test: `Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs` (compile fix only — new field on the `PriceV1` fixture)
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs` (compile fix only — new field on the `Prices` fixture)

**Interfaces:**
- Produces: `PriceRuleSet(int Version, int MinGames, string Currency, IReadOnlyList<PriceBand> Bands, int BlendGames)` — `BlendGames` is a new **required**, trailing positional parameter. `TablePriceRuleSetRepository.GetAsync` returns `null` when the `blendGames` config row is missing, exactly like `minGames`/`currency` today.

- [ ] **Step 1: Write the failing test for the missing-`Get_MissingBlendGames_ReturnsNull` repository case**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TablePriceRuleSetRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task Get_MissingBlendGames_ReturnsNull()
    {
        Rows(("minGames", "3"), ("currency", "ISK"), ("band:0", "5000000"));
        Assert.Null(await CreateSut().GetAsync(1, default));
    }
```

Also update the existing `Get_AssemblesSortedRuleSet` test to seed and assert `blendGames`:

```csharp
    [Fact]
    public async Task Get_AssemblesSortedRuleSet()
    {
        Rows(
            ("minGames", "3"),
            ("blendGames", "10"),
            ("currency", "ISK"),
            ("band:6", "20000000"),
            ("band:0", "5000000"),
            ("band:3", "10000000"));

        var result = await CreateSut().GetAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Version);
        Assert.Equal(3, result.MinGames);
        Assert.Equal(10, result.BlendGames);
        Assert.Equal("ISK", result.Currency);
        Assert.Equal(new[] { 0d, 3d, 6d }, result.Bands.Select(b => b.Threshold));
        Assert.Equal(5000000, result.Bands[0].Price);
        Assert.Equal("fantasy-price-v1", result.Name);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePriceRuleSetRepositoryTests"`
Expected: build error (`PriceRuleSet` has no `BlendGames` member yet) or, once the field compiles, `Get_MissingBlendGames_ReturnsNull` and `Get_AssemblesSortedRuleSet` FAIL.

- [ ] **Step 3: Add `BlendGames` to the domain record**

In `Ez.Handball.Domain/PriceRuleSet.cs`:

```csharp
namespace Ez.Handball.Domain;

public sealed record PriceBand(double Threshold, double Price);

public sealed record PriceRuleSet(
    int Version,
    int MinGames,
    string Currency,
    IReadOnlyList<PriceBand> Bands,   // sorted ascending by Threshold; non-empty
    int BlendGames)                   // prior-season blend fade horizon; must be > MinGames
{
    public string Name => $"fantasy-price-v{Version}";

    // Highest band whose threshold <= score; the floor band when below the lowest.
    public PriceBand BandFor(double score) =>
        Bands.LastOrDefault(b => b.Threshold <= score) ?? Bands[0];
}
```

- [ ] **Step 4: Parse `blendGames` in `TablePriceRuleSetRepository`**

In `Ez.Handball.Infrastructure/TableAccess/TablePriceRuleSetRepository.cs`, replace the body of `GetAsync`:

```csharp
    public async Task<PriceRuleSet?> GetAsync(int version, CancellationToken ct)
    {
        var group = $"fantasy-price-v{version}";
        var filter = $"PartitionKey eq '{ODataFilter.Escape(group)}'";

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var row in _query.QueryAsync<ConfigEntity>(Tables.Config, filter, ct))
            values[row.RowKey] = row.Value;

        if (values.Count == 0) return null;

        if (!TryGetInt(values, "minGames", out var minGames) ||
            !TryGetInt(values, "blendGames", out var blendGames) ||
            !values.TryGetValue("currency", out var currency))
            return null;

        var bands = new List<PriceBand>();
        foreach (var kv in values)
        {
            if (!kv.Key.StartsWith(BandPrefix, StringComparison.Ordinal)) continue;
            if (double.TryParse(kv.Key[BandPrefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
                && double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
                bands.Add(new PriceBand(threshold, price));
        }

        if (bands.Count == 0) return null;
        bands.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));

        return new PriceRuleSet(version, minGames, currency, bands, blendGames);
    }
```

- [ ] **Step 5: Seed `blendGames` alongside the other tunables**

In `Ez.Handball.Ingestion/Functions/SeedPriceRuleSetsFunction.cs`, update the definitions list and its comment:

```csharp
    // Fantasy ISK price rule set. minGames guards thin samples; blendGames is the
    // prior-season fade horizon (in current-season games) — below it, price blends
    // toward last season's same-competition rate instead of the floor band; bands
    // map points-per-game to a price. Thresholds/prices are tunable config
    // (scoring calibration: #27).
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> RuleSetDefinitions =
    [
        ("fantasy-price-v1", "minGames", "3"),
        ("fantasy-price-v1", "blendGames", "10"),
        ("fantasy-price-v1", "currency", "ISK"),
        ("fantasy-price-v1", "band:0",   "5000000"),
        ("fantasy-price-v1", "band:3",   "10000000"),
        ("fantasy-price-v1", "band:6",   "20000000"),
        ("fantasy-price-v1", "band:9",   "35000000"),
        ("fantasy-price-v1", "band:12",  "50000000"),
    ];
```

- [ ] **Step 6: Fix the `SeedPriceRuleSetsFunctionTests` assertion for the new row count/key**

In `Ez.Handball.Tests/Ingestion/Functions/SeedPriceRuleSetsFunctionTests.cs`, add to `Definitions_AreTheFantasyPriceV1Group`:

```csharp
        Assert.Contains(defs, d => d.Key == "blendGames" && d.Value == "10");
```

(The existing `Assert.Equal(SeedPriceRuleSetsFunction.RuleSetDefinitions.Count, seeded)` in `ProcessAsync_UpsertsEveryRow_IntoConfigTable` already adapts automatically to the new row — no other change needed there.)

- [ ] **Step 7: Fix compile errors in the other three fixtures**

In `Ez.Handball.Tests/Domain/PriceRuleSetTests.cs`, update `Rs()`:

```csharp
    private static PriceRuleSet Rs() => new(1, 3, "ISK", new[]
    {
        new PriceBand(0, 5000000),
        new PriceBand(3, 10000000),
        new PriceBand(6, 20000000),
    }, BlendGames: 10);
```

In `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs`, update `Prices`:

```csharp
    // Bands: score < 5 => 1_000_000; 5..<10 => 5_000_000; >=10 => 11_000_000
    private static readonly PriceRuleSet Prices =
        new(Version: 1, MinGames: 3, Currency: "ISK", Bands: new[]
        {
            new PriceBand(0, 1_000_000),
            new PriceBand(5, 5_000_000),
            new PriceBand(10, 11_000_000),
        }, BlendGames: 6);
```

In `Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs`, update `PriceV1`:

```csharp
    private static readonly PriceRuleSet PriceV1 = new(1, 3, "ISK", new[]
    {
        new PriceBand(0, 5000000),
        new PriceBand(3, 10000000),
        new PriceBand(6, 20000000),
        new PriceBand(9, 35000000),
        new PriceBand(12, 50000000),
    }, BlendGames: 10);
```

In `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`, update `Prices`:

```csharp
    private static readonly PriceRuleSet Prices =
        new(1, MinGames: 1, Currency: "ISK", Bands: new[]
        {
            new PriceBand(0, 1_000_000),
            new PriceBand(5, 5_000_000),
            new PriceBand(10, 11_000_000),
        }, BlendGames: 10);
```

- [ ] **Step 8: Run the full test suite to verify everything compiles and passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (no regressions; the two new/updated repository tests pass).

- [ ] **Step 9: Commit**

```bash
git add Ez.Handball.Domain/PriceRuleSet.cs \
        Ez.Handball.Infrastructure/TableAccess/TablePriceRuleSetRepository.cs \
        Ez.Handball.Ingestion/Functions/SeedPriceRuleSetsFunction.cs \
        Ez.Handball.Tests/Domain/PriceRuleSetTests.cs \
        Ez.Handball.Tests/Infrastructure/Tables/TablePriceRuleSetRepositoryTests.cs \
        Ez.Handball.Tests/Ingestion/Functions/SeedPriceRuleSetsFunctionTests.cs \
        Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs \
        Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs \
        Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs
git commit -m "feat(pricing): add tunable BlendGames to PriceRuleSet"
```

---

## Task 2: Resolve "previous season, same competition"

**Files:**
- Create: `Ez.Handball.Domain/PreviousSeasonScope.cs`
- Modify: `Ez.Handball.Application/Abstractions/ITournamentScopeResolver.cs`
- Modify: `Ez.Handball.Application/Services/TournamentScopeResolver.cs`
- Test: `Ez.Handball.Tests/Application/Services/TournamentScopeResolverTests.cs`

**Interfaces:**
- Produces: `PreviousSeasonScope(string SeasonLabel, IReadOnlyList<string>? TournamentIds)` — `TournamentIds` follows the same `null` = "no narrowing, scan the whole season" / non-null (possibly empty) = "narrowed to these ids" convention as `ResolveTournamentIdsAsync`.
- Produces: `ITournamentScopeResolver.ResolvePreviousSeasonScopeAsync(string? season, string? tournamentId, string? competitionId, TournamentType? type, CancellationToken ct) : Task<PreviousSeasonScope?>` — returns `null` only when no previous season exists at all (no current season resolvable, or the current season is the oldest one tracked).
- Consumes (relies on the ordering contract already established by `TableSeasonRepository.ListAsync`): `ISeasonRepository.ListAsync` returns seasons **newest-first**.

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/Services/TournamentScopeResolverTests.cs` (new section at the end of the class, before the closing brace):

```csharp
    // ── ResolvePreviousSeasonScopeAsync ─────────────────────────────────────────

    [Fact]
    public async Task PreviousScope_ExplicitCompetitionId_ResolvesPreviousSeasonTournaments()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Equal(new[] { "7777" }, scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_ExplicitTournamentId_TranslatesToCompetitionIdOfThatSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: "8444", competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Equal(new[] { "7777" }, scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_UnknownTournamentIdInCurrentSeason_ResolvesLabelButEmptyIds()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2025-26", T("8444", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: "does-not-exist", competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.NotNull(scope.TournamentIds);
        Assert.Empty(scope.TournamentIds!);
    }

    [Fact]
    public async Task PreviousScope_NoNarrowing_ReturnsNullTournamentIds_WholeSeasonScan()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: null, type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
        Assert.Null(scope.TournamentIds);
    }

    [Fact]
    public async Task PreviousScope_CurrentSeasonIsOldestTracked_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            "2025-26", tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Null(scope);
    }

    [Fact]
    public async Task PreviousScope_NoCurrentSeasonResolvable_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season>());

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.Null(scope);
    }

    [Fact]
    public async Task PreviousScope_NullSeason_ResolvesCurrentSeasonFirst()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupSeason("2024-25", T("7777", TournamentType.League, "olis-karla"));

        var scope = await CreateSut().ResolvePreviousSeasonScopeAsync(
            season: null, tournamentId: null, competitionId: "olis-karla", type: null, default);

        Assert.NotNull(scope);
        Assert.Equal("2024-25", scope!.SeasonLabel);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TournamentScopeResolverTests"`
Expected: build error (`ResolvePreviousSeasonScopeAsync` doesn't exist yet).

- [ ] **Step 3: Create the `PreviousSeasonScope` domain record**

Create `Ez.Handball.Domain/PreviousSeasonScope.cs`:

```csharp
namespace Ez.Handball.Domain;

// The previous season's resolved scope for a "same competition, one season back"
// lookup. TournamentIds follows the same convention as
// ITournamentScopeResolver.ResolveTournamentIdsAsync: null = no narrowing (scan
// the whole previous season), non-null (possibly empty) = narrowed to these ids.
public sealed record PreviousSeasonScope(
    string SeasonLabel,
    IReadOnlyList<string>? TournamentIds);
```

- [ ] **Step 4: Add the method to `ITournamentScopeResolver`**

In `Ez.Handball.Application/Abstractions/ITournamentScopeResolver.cs`, add inside the interface:

```csharp
    /// <summary>
    /// Resolves "the same competition, one season back" from the given scope: a
    /// single explicit <paramref name="tournamentId"/> is translated to that
    /// tournament's CompetitionId within the current season before looking up the
    /// previous season's tournament(s) for that competition. Returns null only
    /// when no previous season exists at all (no current season resolvable, or the
    /// current season is the oldest one tracked).
    /// </summary>
    Task<PreviousSeasonScope?> ResolvePreviousSeasonScopeAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);
```

- [ ] **Step 5: Implement it in `TournamentScopeResolver`**

In `Ez.Handball.Application/Services/TournamentScopeResolver.cs`, add:

```csharp
    public async Task<PreviousSeasonScope?> ResolvePreviousSeasonScopeAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var currentLabel = await ResolveSeasonLabelAsync(season, ct);
        if (string.IsNullOrWhiteSpace(currentLabel)) return null;

        // ISeasonRepository.ListAsync returns seasons newest-first (see
        // TableSeasonRepository) — "previous" is the entry right after current.
        var seasons = await _seasons.ListAsync(ct);
        var currentIndex = -1;
        for (var i = 0; i < seasons.Count; i++)
        {
            if (seasons[i].Label == currentLabel) { currentIndex = i; break; }
        }
        if (currentIndex < 0 || currentIndex + 1 >= seasons.Count) return null;
        var previousLabel = seasons[currentIndex + 1].Label;

        var effectiveCompetitionId = competitionId;
        if (string.IsNullOrWhiteSpace(effectiveCompetitionId) && !string.IsNullOrWhiteSpace(tournamentId))
        {
            var currentTournaments = await _tournaments.ListBySeasonAsync(currentLabel, ct);
            effectiveCompetitionId = currentTournaments
                .FirstOrDefault(t => t.TournamentId == tournamentId)?.CompetitionId;

            if (effectiveCompetitionId is null)
                return new PreviousSeasonScope(previousLabel, Array.Empty<string>());
        }

        var previousIds = await ResolveTournamentIdsAsync(previousLabel, null, effectiveCompetitionId, type, ct);
        return new PreviousSeasonScope(previousLabel, previousIds);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TournamentScopeResolverTests"`
Expected: PASS.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (no regressions elsewhere — `ITournamentScopeResolver` gained a member, but nothing else implements or strictly mocks it in a way that breaks).

- [ ] **Step 8: Commit**

```bash
git add Ez.Handball.Domain/PreviousSeasonScope.cs \
        Ez.Handball.Application/Abstractions/ITournamentScopeResolver.cs \
        Ez.Handball.Application/Services/TournamentScopeResolver.cs \
        Ez.Handball.Tests/Application/Services/TournamentScopeResolverTests.cs
git commit -m "feat(pricing): resolve previous-season same-competition tournament scope"
```

---

## Task 3: Blend the formula in `FantasyPricing.Compute`

**Files:**
- Modify: `Ez.Handball.Application/Services/FantasyPricing.cs`
- Test: `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs`

**Interfaces:**
- Consumes: `PriceRuleSet.BlendGames` (Task 1), `PriceRuleSet.MinGames` (existing).
- Produces: `FantasyPricing.Compute(string playerId, AggregatedStats stats, ScoringRuleSet scoring, PriceRuleSet prices, PlayerRatingContext context, AggregatedStats? previousSeasonStats = null) : FantasyPriceResult`. Existing 4-arg call sites keep compiling (the new parameter defaults to `null`, i.e. "no qualifying prior season" — byte-for-byte today's behavior).

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs`, inside the class:

```csharp
    [Fact]
    public void Compute_ZeroCurrentGames_QualifyingPriorSeason_UsesPriorRateFully()
    {
        var stats = new AggregatedStats(0, 0, 0, 0, 0);
        // prior: 5 games, 25 goals -> priorRating = 25*2 + 5*1 = 55, priorRate = 11
        var prior = new AggregatedStats(Games: 5, Goals: 25, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx, prior);

        Assert.Equal(0, result.Rating);          // current-season rating, unaffected
        Assert.Equal(11, result.Score);          // w=0 -> fully the prior rate
        Assert.Equal(11_000_000, result.Price.Amount);
    }

    [Fact]
    public void Compute_PartialCurrentGames_BlendsCurrentAndPriorRates()
    {
        // current: 3 games, 6 goals -> currentRating = 6*2+3*1 = 15, currentRate = 5
        var stats = new AggregatedStats(Games: 3, Goals: 6, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);
        // prior: 5 games, 25 goals -> priorRate = 11 (as above)
        var prior = new AggregatedStats(Games: 5, Goals: 25, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx, prior);

        // Prices.BlendGames = 6 -> w = 3/6 = 0.5 -> score = 0.5*5 + 0.5*11 = 8
        Assert.Equal(15, result.Rating);
        Assert.Equal(8, result.Score);
        Assert.Equal(5_000_000, result.Price.Amount);   // band 5..<10
    }

    [Fact]
    public void Compute_CurrentGamesAtBlendGames_MatchesUnblendedFormula()
    {
        // current: 6 games (== Prices.BlendGames), 30 goals -> currentRate = (60+6)/6 = 11
        var stats = new AggregatedStats(Games: 6, Goals: 30, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);
        var prior = new AggregatedStats(Games: 5, Goals: 0, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var withPrior = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx, prior);
        var withoutPrior = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(withoutPrior.Score, withPrior.Score);   // w=1 -> prior fully faded out
        Assert.Equal(11, withPrior.Score);
        Assert.Equal(11_000_000, withPrior.Price.Amount);
    }

    [Fact]
    public void Compute_PriorSeasonBelowMinGames_TreatedAsNoQualifyingPriorSeason()
    {
        // current: 1 game < MinGames(3) -> today's floor-band fallback applies
        var stats = new AggregatedStats(Games: 1, Goals: 0, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);
        // prior: only 2 games < MinGames(3) -> doesn't qualify, even though "juicy"
        var thinPrior = new AggregatedStats(Games: 2, Goals: 100, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx, thinPrior);

        Assert.Equal(0, result.Score);
        Assert.Equal(1_000_000, result.Price.Amount);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyPricingTests"`
Expected: build error (no `previousSeasonStats` overload yet), then FAIL once it compiles with a stub.

- [ ] **Step 3: Implement the blend in `FantasyPricing.Compute`**

Replace the body of `Ez.Handball.Application/Services/FantasyPricing.cs`:

```csharp
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// The fantasy rating (#52 metric) + price band for a player, computed from
// ALREADY-aggregated stats. Pure: no I/O, no rule-set loading. Both the
// single-player price path and the bulk pool path call this so the formula
// lives in exactly one place.
//
// Below MinGames, the price-driving Score fades from last season's rate
// (same competition, w=0) toward the current season's own rate (w=1) as
// currentGames approaches BlendGames — rather than jumping straight from a
// forced-zero score to the full current rate. Rating stays current-season-only.
public readonly record struct FantasyPriceResult(double Rating, double Score, PlayerPrice Price);

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
        PriceRuleSet prices,
        PlayerRatingContext context,
        AggregatedStats? previousSeasonStats = null)
    {
        var rating = _rating.Compute(new PlayerRatingInputs(playerId, stats, scoring, context)).Rating;
        var currentRate = stats.Games > 0 ? rating / stats.Games : 0;

        double score;
        if (previousSeasonStats is { Games: var priorGames } prior && priorGames >= prices.MinGames)
        {
            // Context is accepted by the rating function but unused by the fantasy
            // formula (see GetPlayerPoolUseCase) — reusing the caller's context here
            // is safe for the same reason.
            var priorRating = _rating.Compute(new PlayerRatingInputs(playerId, prior, scoring, context)).Rating;
            var priorRate = priorRating / priorGames;
            var weight = Math.Min((double)stats.Games / prices.BlendGames, 1.0);
            score = weight * currentRate + (1 - weight) * priorRate;
        }
        else
        {
            score = stats.Games >= prices.MinGames && stats.Games > 0 ? currentRate : 0;
        }

        var band = prices.BandFor(score);
        return new FantasyPriceResult(rating, score, new PlayerPrice(band.Price, prices.Currency));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~FantasyPricingTests"`
Expected: PASS, including the three pre-existing tests (`Compute_RatingIsSumOfWeightedComponents`, `Compute_BelowMinGames_ScoreIsZero_FloorBand`, `Compute_ZeroGames_ScoreIsZero`), unchanged, since they don't pass `previousSeasonStats`.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Services/FantasyPricing.cs \
        Ez.Handball.Tests/Application/Services/FantasyPricingTests.cs
git commit -m "feat(pricing): fade price toward previous season's rate below BlendGames"
```

---

## Task 4: Fetch previous-season stats for a single player

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IPlayerStatsAggregator.cs`
- Modify: `Ez.Handball.Application/Services/PlayerStatsAggregator.cs`
- Test: `Ez.Handball.Tests/Application/Services/PlayerStatsAggregatorTests.cs`

**Interfaces:**
- Consumes: `ITournamentScopeResolver.ResolvePreviousSeasonScopeAsync` (Task 2).
- Produces: `IPlayerStatsAggregator.AggregatePreviousSeasonAsync(string playerId, string? season, string? tournamentId, string? competitionId, TournamentType? type, CancellationToken ct) : Task<AggregatedStats?>` — returns `null` when there's no previous season, the competition didn't exist in it, or the player has zero qualifying rows in it (the `MinGames` qualification itself is checked later, inside `FantasyPricing.Compute`, per Task 3 — this method just returns the raw aggregate or `null` for "no data").

- [ ] **Step 1: View the current `IPlayerStatsAggregator.cs` to confirm the exact current contents**

Run: `cat Ez.Handball.Application/Abstractions/IPlayerStatsAggregator.cs`

- [ ] **Step 2: Write the failing tests**

Add to `Ez.Handball.Tests/Application/Services/PlayerStatsAggregatorTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task AggregatePreviousSeason_NoPreviousSeasonExists_ReturnsNull()
    {
        // constructor default: only "2025-26" is tracked -> no previous season
        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task AggregatePreviousSeason_SameCompetitionPriorSeason_SumsScopedRows()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupTournamentsBySeason("2025-26", Trn("8444", TournamentType.League, "olis-karla"));
        SetupTournamentsBySeason("2024-25",
            Trn("7777", TournamentType.League, "olis-karla"),
            Trn("6666", TournamentType.Cup, "bikar-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>
              {
                  Stat("2024-25", "7777", 5),
                  Stat("2024-25", "7777", 3),
                  Stat("2024-25", "6666", 99),   // decoy: different competition, must be excluded
              });

        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Games);
        Assert.Equal(8, result.Goals);
    }

    [Fact]
    public async Task AggregatePreviousSeason_NoQualifyingRows_ReturnsNull()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true), new("2024-25", false) });
        SetupTournamentsBySeason("2025-26", Trn("8444", TournamentType.League, "olis-karla"));
        SetupTournamentsBySeason("2024-25", Trn("7777", TournamentType.League, "olis-karla"));
        _stats.Setup(r => r.GetByPlayerAsync("p1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<PlayerStat>());

        var result = await CreateSut().AggregatePreviousSeasonAsync("p1", "2025-26", "8444", null, null, default);

        Assert.Null(result);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerStatsAggregatorTests"`
Expected: build error (`AggregatePreviousSeasonAsync` doesn't exist yet).

- [ ] **Step 4: Add the method to `IPlayerStatsAggregator`**

In `Ez.Handball.Application/Abstractions/IPlayerStatsAggregator.cs`, add the new method to the interface (keep the existing `AggregateAsync` member as-is):

```csharp
    // Same-competition, one-season-back aggregate for a player, or null when
    // there's no previous season, the competition didn't exist in it, or the
    // player has no rows in it. Callers apply their own "is this sample usable"
    // threshold (e.g. FantasyPricing.Compute checks Games against MinGames).
    Task<AggregatedStats?> AggregatePreviousSeasonAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);
```

- [ ] **Step 5: Implement it in `PlayerStatsAggregator`**

In `Ez.Handball.Application/Services/PlayerStatsAggregator.cs`, add:

```csharp
    public async Task<AggregatedStats?> AggregatePreviousSeasonAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var previous = await _scope.ResolvePreviousSeasonScopeAsync(season, tournamentId, competitionId, type, ct);
        if (previous is null) return null;
        if (previous.TournamentIds is { Count: 0 }) return null;

        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == previous.SeasonLabel);
        if (previous.TournamentIds is not null)
            scoped = scoped.Where(r => previous.TournamentIds.Contains(r.TournamentId));

        var list = scoped.ToList();
        if (list.Count == 0) return null;

        return new AggregatedStats(
            Games: list.Count,
            Goals: list.Sum(r => r.Goals),
            YellowCards: list.Sum(r => r.YellowCards),
            TwoMinuteSuspensions: list.Sum(r => r.TwoMinuteSuspensions),
            RedCards: list.Sum(r => r.RedCards),
            Assists: list.Sum(r => r.HbStatzAssists ?? 0),
            Steals: list.Sum(r => r.HbStatzSteals ?? 0),
            Blocks: list.Sum(r => r.HbStatzBlocks ?? 0),
            Saves: list.Sum(r => r.HbStatzSaves ?? 0));
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerStatsAggregatorTests"`
Expected: PASS, including all pre-existing tests in this file.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IPlayerStatsAggregator.cs \
        Ez.Handball.Application/Services/PlayerStatsAggregator.cs \
        Ez.Handball.Tests/Application/Services/PlayerStatsAggregatorTests.cs
git commit -m "feat(pricing): aggregate previous-season same-competition player stats"
```

---

## Task 5: Wire the blend into `PlayerPriceService`

**Files:**
- Modify: `Ez.Handball.Application/Services/PlayerPriceService.cs`
- Test: `Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs`

**Interfaces:**
- Consumes: `IPlayerStatsAggregator.AggregatePreviousSeasonAsync` (Task 4), `FantasyPricing.Compute(..., previousSeasonStats)` (Task 3).
- `PlayerPriceService.GetPriceAsync`'s public signature is unchanged — only its internals change.

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs`, inside the class. First add a helper alongside `Aggregate`:

```csharp
    private void AggregatePrevious(AggregatedStats? stats) =>
        _aggregator.Setup(a => a.AggregatePreviousSeasonAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
```

Call `AggregatePrevious(null);` at the end of the constructor (`PlayerPriceServiceTests()`), right after the existing `Aggregate(0, 0);` line, so every pre-existing test keeps its current "no prior season" behavior explicitly (rather than relying on Moq's loose-mock default).

Then add the new tests:

```csharp
    [Fact]
    public async Task BelowMinGames_WithQualifyingPriorSeason_BlendsInsteadOfFloor()
    {
        Aggregate(games: 0, goals: 0);
        // prior: 5 games, 25 goals -> priorRating = 25*2 + 5*1 = 55, priorRate = 11
        AggregatePrevious(new AggregatedStats(Games: 5, Goals: 25, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0));

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        // PriceV1.BlendGames = 10, currentGames = 0 -> w = 0 -> score = priorRate = 11
        Assert.Equal(11, price!.Score);
        Assert.Equal(0, price.Games);              // reported games stay current-season
        Assert.Equal(35000000, price.Price.Amount); // band 9..<12
    }

    [Fact]
    public async Task PriorSeasonBelowMinGames_StillFloorBand()
    {
        Aggregate(games: 1, goals: 0);
        AggregatePrevious(new AggregatedStats(Games: 2, Goals: 100, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0));

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(0, price!.Score);
        Assert.Equal(5000000, price.Price.Amount);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPriceServiceTests"`
Expected: build error (`AggregatePreviousSeasonAsync` not set up as a recognizable call target until Task 4 lands — should already exist by now) — the two new tests FAIL because `PlayerPriceService` doesn't call the new aggregator method yet.

- [ ] **Step 3: Wire it into `PlayerPriceService`**

Replace the body of `GetPriceAsync` in `Ez.Handball.Application/Services/PlayerPriceService.cs`:

```csharp
    public async Task<PlayerPricing?> GetPriceAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct)
    {
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, _pricing.ScoringVersion, ct);
        if (scoring is null) return null;

        var priceRuleSet = await _prices.GetAsync(version, ct);
        if (priceRuleSet is null) return null;

        var stats = await _aggregator.AggregateAsync(playerId, season, tournamentId, null, null, ct);
        var previousStats = await _aggregator.AggregatePreviousSeasonAsync(playerId, season, tournamentId, null, null, ct);
        var ctx = new PlayerRatingContext(season, tournamentId, null, null, null, null);

        var result = _pricing.Compute(playerId, stats, scoring, priceRuleSet, ctx, previousStats);
        return new PlayerPricing(
            playerId, result.Price, result.Score, stats.Games, priceRuleSet.Name, result.Rating);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~PlayerPriceServiceTests"`
Expected: PASS, including all pre-existing tests.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Services/PlayerPriceService.cs \
        Ez.Handball.Tests/Application/Services/PlayerPriceServiceTests.cs
git commit -m "feat(pricing): blend previous-season stats into the single-player price path"
```

---

## Task 6: Bulk-path previous-season fetch and join

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs`
- Modify: `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`

**Interfaces:**
- Produces: `PlayerPoolQuery(string? Season, IReadOnlyList<string>? TournamentIds, string? Gender, string? PreviousSeason = null, IReadOnlyList<string>? PreviousSeasonTournamentIds = null)` — two new trailing optional fields; existing 3-arg construction sites keep compiling.
- Produces: `PooledPlayer(..., bool Retired, AggregatedStats? PreviousSeasonStats = null)` — one new trailing optional field.
- This task does **not** resolve the previous-season scope itself (that's Task 7, in the use case) — it only consumes whatever `PlayerPoolQuery.PreviousSeason`/`PreviousSeasonTournamentIds` the caller supplies.

- [ ] **Step 1: Write the failing tests**

Add to `Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs`. First, extend the `Q` helper:

```csharp
    private static PlayerPoolQuery Q(
        string? season = null, IReadOnlyList<string>? tournamentIds = null, string? gender = null,
        string? previousSeason = null, IReadOnlyList<string>? previousSeasonTournamentIds = null) =>
        new(season, tournamentIds, gender, previousSeason, previousSeasonTournamentIds);
```

Add a filtered stats-setup helper alongside `SetupStats`:

```csharp
    private void SetupStatsFiltered(string filterContains, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                  Ez.Handball.Infrastructure.Tables.PlayerStats,
                  It.Is<string?>(f => f != null && f.Contains(filterContains)), default))
              .Returns(ToAsync(rows));
```

Then add the tests:

```csharp
    [Fact]
    public async Task GetAggregated_PreviousSeasonProvided_JoinsPreviousStatsByPlayer()
    {
        SetupStatsFiltered("'2025-26'",
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupStatsFiltered("'2024-25'",
            Stat("m0", "p1", "2024-25", "7777", "385-karlar", "Stjarnan", 8),
            Stat("m0b", "p1", "2024-25", "7777", "385-karlar", "Stjarnan", 2));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(season: "2025-26", tournamentIds: new[] { "8444" },
              previousSeason: "2024-25", previousSeasonTournamentIds: new[] { "7777" }),
            CancellationToken.None);

        var p = Assert.Single(result);
        Assert.NotNull(p.PreviousSeasonStats);
        Assert.Equal(2, p.PreviousSeasonStats!.Games);
        Assert.Equal(10, p.PreviousSeasonStats.Goals);
    }

    [Fact]
    public async Task GetAggregated_NoPreviousSeason_LeavesPreviousStatsNull()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        Assert.Null(Assert.Single(result).PreviousSeasonStats);
    }

    [Fact]
    public async Task GetAggregated_PreviousSeasonEmptyTournamentIds_LeavesPreviousStatsNull()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(previousSeason: "2024-25", previousSeasonTournamentIds: Array.Empty<string>()),
            CancellationToken.None);

        Assert.Null(Assert.Single(result).PreviousSeasonStats);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests"`
Expected: build error (`PlayerPoolQuery`/`PooledPlayer` don't have the new fields yet).

- [ ] **Step 3: Extend `PlayerPoolQuery` and `PooledPlayer`**

In `Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs`, replace the two records:

```csharp
// One player's scope-aggregated stats plus identity + position. The use case
// turns these into rating + price; the repository does NOT price anything.
// PreviousSeasonStats is the same-competition, one-season-back aggregate (or
// null when there isn't a qualifying one) — FantasyPricing.Compute uses it to
// fade the price toward last season's rate early in the current season.
public sealed record PooledPlayer(
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    AggregatedStats Stats,
    bool Retired,
    AggregatedStats? PreviousSeasonStats = null);

// Use case → repository. TournamentIds is the resolved current-season scope:
// null = whole-season scan; empty = scope matched no tournaments (repository
// returns nothing). PreviousSeason/PreviousSeasonTournamentIds carry the
// already-resolved "same competition, one season back" scope (see
// ITournamentScopeResolver.ResolvePreviousSeasonScopeAsync) — null
// PreviousSeason means no qualifying previous season exists at all.
public sealed record PlayerPoolQuery(
    string? Season,
    IReadOnlyList<string>? TournamentIds,
    string? Gender,
    string? PreviousSeason = null,
    IReadOnlyList<string>? PreviousSeasonTournamentIds = null);
```

- [ ] **Step 4: Fetch and join previous-season stats in `TablePlayerPoolRepository`**

In `Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs`, modify `GetAggregatedAsync`: insert the previous-season fetch after the existing `playerById` join is built, and use it in the final `.Select`:

```csharp
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

        var previousStatsByPlayer = await GetPreviousSeasonStatsByPlayerAsync(q, ct);

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

                previousStatsByPlayer.TryGetValue(g.Key, out var previousStats);

                return new PooledPlayer(
                    PlayerId: g.Key,
                    Name: player?.Name,
                    ClubId: clubId,
                    ClubName: clubName,
                    Gender: gender,
                    Position: player?.Position ?? string.Empty,
                    Stats: stats,
                    Retired: player?.Retired == true,
                    PreviousSeasonStats: previousStats);
            })
            .ToList();

        return result;
    }

    // Fetches and aggregates PlayerStats for the already-resolved previous-season
    // scope. Not gender-filtered: results are only ever looked up by PlayerId
    // against the current-season roster above, so an unused entry for the other
    // gender is simply never read.
    private async Task<Dictionary<string, AggregatedStats>> GetPreviousSeasonStatsByPlayerAsync(
        PlayerPoolQuery q, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(q.PreviousSeason)) return new Dictionary<string, AggregatedStats>();
        if (q.PreviousSeasonTournamentIds is { Count: 0 }) return new Dictionary<string, AggregatedStats>();

        var previousFilter = BuildFilter(new PlayerPoolQuery(q.PreviousSeason, q.PreviousSeasonTournamentIds, null));

        var previousRows = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(Tables.PlayerStats, previousFilter, ct))
            previousRows.Add(row);

        return previousRows
            .GroupBy(r => r.RowKey)
            .ToDictionary(g => g.Key, g => new AggregatedStats(
                Games: g.Count(),
                Goals: g.Sum(r => r.Goals),
                YellowCards: g.Sum(r => r.YellowCards),
                TwoMinuteSuspensions: g.Sum(r => r.TwoMinuteSuspensions),
                RedCards: g.Sum(r => r.RedCards)));
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TablePlayerPoolRepositoryTests"`
Expected: PASS, including all pre-existing tests in this file.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IPlayerPoolRepository.cs \
        Ez.Handball.Infrastructure/TableAccess/TablePlayerPoolRepository.cs \
        Ez.Handball.Tests/Infrastructure/Tables/TablePlayerPoolRepositoryTests.cs
git commit -m "feat(pricing): fetch and join previous-season stats in the bulk pool repository"
```

---

## Task 7: Wire the blend into `GetPlayerPoolUseCase`

**Files:**
- Modify: `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`

**Interfaces:**
- Consumes: `ITournamentScopeResolver.ResolvePreviousSeasonScopeAsync` (Task 2), `PlayerPoolQuery`'s new fields and `PooledPlayer.PreviousSeasonStats` (Task 6), `FantasyPricing.Compute(..., previousSeasonStats)` (Task 3).

- [ ] **Step 1: Write the failing test**

Add to `Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs`. First, add a setup helper alongside `SetupResolver`:

```csharp
    private void SetupPreviousScope(PreviousSeasonScope? scope) =>
        _scope.Setup(s => s.ResolvePreviousSeasonScopeAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(scope);
```

Then add the test:

```csharp
    [Fact]
    public async Task Execute_ZeroCurrentGamesWithPriorSeason_PricesFromPriorRate_RatingStaysCurrentSeason()
    {
        SetupResolver();
        SetupRuleSets();
        SetupPreviousScope(new PreviousSeasonScope("2024-25", new[] { "7777" }));
        _repo.Setup(r => r.GetAggregatedAsync(It.IsAny<PlayerPoolQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[]
             {
                 new PooledPlayer("a", "Pa", "385", "Stjarnan", "karlar", "CB",
                     new AggregatedStats(Games: 0, Goals: 0, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0),
                     Retired: false,
                     PreviousSeasonStats: new AggregatedStats(
                         Games: 5, Goals: 25, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0)),
             });

        var result = await CreateSut().ExecuteAsync(Req(), 0, 50, CancellationToken.None);

        var entry = Assert.Single(Assert.IsType<PlayerPoolResult.Found>(result).Pool.Entries);
        // Prices.BlendGames = 10, currentGames = 0 -> w = 0 -> score = priorRate = 11 -> top band
        Assert.Equal(11_000_000, entry.Price.Amount);
        Assert.Equal(0, entry.Rating);   // current-season rating, unaffected by the blend
        Assert.Equal(0, entry.Games);    // reported games stay current-season
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~Execute_ZeroCurrentGamesWithPriorSeason"`
Expected: FAIL (price still floors at the band for score=0, since the use case doesn't fetch/pass previous-season stats yet).

- [ ] **Step 3: Wire it into `GetPlayerPoolUseCase`**

In `Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs`, modify `ExecuteAsync`: after resolving `tournamentIds`, resolve the previous-season scope and pass it into the `PlayerPoolQuery`; then pass `p.PreviousSeasonStats` into `_pricing.Compute`.

```csharp
        var season = await _scope.ResolveSeasonLabelAsync(request.Season, ct);
        var tournamentIds = await _scope.ResolveTournamentIdsAsync(
            season, request.TournamentId, request.CompetitionId, request.Type, ct);
        var previousScope = await _scope.ResolvePreviousSeasonScopeAsync(
            season, request.TournamentId, request.CompetitionId, request.Type, ct);

        var query = new PlayerPoolQuery(
            season, tournamentIds, request.Gender,
            previousScope?.SeasonLabel, previousScope?.TournamentIds);
        var players = await _repo.GetAggregatedAsync(query, ct);
```

And in the `.Select(p => { ... })` block:

```csharp
                var priced = _pricing.Compute(p.PlayerId, p.Stats, scoring, prices, ctx, p.PreviousSeasonStats);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~Execute_ZeroCurrentGamesWithPriorSeason"`
Expected: PASS.

- [ ] **Step 5: Run the full `GetPlayerPoolUseCaseTests` file**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~GetPlayerPoolUseCaseTests"`
Expected: PASS, including every pre-existing test (they never set up `ResolvePreviousSeasonScopeAsync`, so the loose mock returns `null` — "no previous season" — leaving today's behavior unchanged).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/UseCases/GetPlayerPoolUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/GetPlayerPoolUseCaseTests.cs
git commit -m "feat(pricing): blend previous-season stats into the bulk pool price path"
```

---

## Task 8: Full solution verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 2: Run the full test suite one more time**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS, all tests green (including the Azurite-backed `BlobArchiverTests`/`TableWriterTests` if Azurite is running locally — start it first if needed: `azurite --silent --location /tmp/azurite-test &`).

- [ ] **Step 3: Confirm no stray debug output or TODOs were left behind**

Run: `git diff main --stat` and `git diff main -- '*.cs' | grep -n "TODO\|Console.Write" || echo "clean"`
Expected: `clean` — no leftover debug statements or TODO markers in the diff.
