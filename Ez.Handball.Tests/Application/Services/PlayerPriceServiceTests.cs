using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class PlayerPriceServiceTests
{
    private readonly Mock<IPlayerStatsAggregator> _aggregator = new();
    private readonly Mock<IScoringRuleSetRepository> _scoring = new();
    private readonly Mock<IPriceRuleSetRepository> _prices = new();

    private static readonly ScoringRuleSet ScoringV1 =
        new(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

    private static readonly PriceRuleSet PriceV1 = new(1, 3, "ISK", new[]
    {
        new PriceBand(0, 5000000),
        new PriceBand(3, 10000000),
        new PriceBand(6, 20000000),
        new PriceBand(9, 35000000),
        new PriceBand(12, 50000000),
    });

    private PlayerPriceService CreateSut() =>
        new(_aggregator.Object, _scoring.Object, _prices.Object, new FantasyPricing(new FantasyPlayerRatingFunction()));

    private void Aggregate(int games, int goals) =>
        _aggregator.Setup(a => a.AggregateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AggregatedStats(games, goals, 0, 0, 0));

    public PlayerPriceServiceTests()
    {
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ScoringV1);
        _prices.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
               .ReturnsAsync(PriceV1);
        Aggregate(0, 0);
    }

    [Fact]
    public async Task PerGameScore_SelectsBand()
    {
        Aggregate(games: 8, goals: 20);   // points = 20*2 + 8*1 = 48; 48/8 = 6 -> band 6 -> 20M

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(6, price!.Score);
        Assert.Equal(8, price.Games);
        Assert.Equal(20000000, price.Price.Amount);
        Assert.Equal("ISK", price.Price.Currency);
        Assert.Equal("fantasy-price-v1", price.Version);
    }

    [Fact]
    public async Task BelowMinGames_FloorBand()
    {
        Aggregate(games: 2, goals: 20);   // games < minGames 3 -> score 0 -> floor 5M

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(0, price!.Score);
        Assert.Equal(5000000, price.Price.Amount);
    }

    [Fact]
    public async Task ZeroGames_FloorBand()
    {
        Aggregate(games: 0, goals: 0);

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(5000000, price!.Price.Amount);
    }

    [Fact]
    public async Task MissingScoringRuleSet_ReturnsNull()
    {
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScoringRuleSet?)null);

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.Null(price);
    }

    [Fact]
    public async Task MissingPriceRuleSet_ReturnsNull()
    {
        _prices.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
               .ReturnsAsync((PriceRuleSet?)null);

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.Null(price);
    }

    [Fact]
    public async Task Rating_IsWeightedSeasonTotal()
    {
        Aggregate(games: 8, goals: 20);   // rating = 20*2 + 8*1 = 48

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(48, price!.Rating);
    }

    [Fact]
    public async Task Rating_ZeroGames_IsZero()
    {
        Aggregate(games: 0, goals: 0);

        var price = await CreateSut().GetPriceAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(price);
        Assert.Equal(0, price!.Rating);
    }
}
