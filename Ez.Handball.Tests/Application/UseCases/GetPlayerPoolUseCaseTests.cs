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
