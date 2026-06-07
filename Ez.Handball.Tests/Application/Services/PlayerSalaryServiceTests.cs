using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class PlayerSalaryServiceTests
{
    private readonly Mock<IPlayerStatsAggregator> _aggregator = new();
    private readonly Mock<IScoringRuleSetRepository> _scoring = new();
    private readonly Mock<ISalaryRuleSetRepository> _prices = new();

    private static readonly ScoringRuleSet ScoringV1 =
        new(GameFlavor.Fantasy, 1, 2, -1, -2, -5, 1);

    private static readonly SalaryRuleSet PriceV1 = new(1, 3, "ISK", new[]
    {
        new SalaryBand(0, 5000000),
        new SalaryBand(3, 10000000),
        new SalaryBand(6, 20000000),
        new SalaryBand(9, 35000000),
        new SalaryBand(12, 50000000),
    });

    private PlayerSalaryService CreateSut() =>
        new(_aggregator.Object, _scoring.Object, _prices.Object, new FantasyPlayerRatingFunction());

    private void Aggregate(int games, int goals) =>
        _aggregator.Setup(a => a.AggregateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TournamentType?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AggregatedStats(games, goals, 0, 0, 0));

    public PlayerSalaryServiceTests()
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

        var salary = await CreateSut().GetSalaryAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(salary);
        Assert.Equal(6, salary!.Score);
        Assert.Equal(8, salary.Games);
        Assert.Equal(20000000, salary.Cost.Amount);
        Assert.Equal("ISK", salary.Cost.Currency);
        Assert.Equal("fantasy-price-v1", salary.Version);
    }

    [Fact]
    public async Task BelowMinGames_FloorBand()
    {
        Aggregate(games: 2, goals: 20);   // games < minGames 3 -> score 0 -> floor 5M

        var salary = await CreateSut().GetSalaryAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(salary);
        Assert.Equal(0, salary!.Score);
        Assert.Equal(5000000, salary.Cost.Amount);
    }

    [Fact]
    public async Task ZeroGames_FloorBand()
    {
        Aggregate(games: 0, goals: 0);

        var salary = await CreateSut().GetSalaryAsync("p1", 1, "2025-26", null, default);

        Assert.NotNull(salary);
        Assert.Equal(5000000, salary!.Cost.Amount);
    }

    [Fact]
    public async Task MissingScoringRuleSet_ReturnsNull()
    {
        _scoring.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScoringRuleSet?)null);

        var salary = await CreateSut().GetSalaryAsync("p1", 1, "2025-26", null, default);

        Assert.Null(salary);
    }

    [Fact]
    public async Task MissingPriceRuleSet_ReturnsNull()
    {
        _prices.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>()))
               .ReturnsAsync((SalaryRuleSet?)null);

        var salary = await CreateSut().GetSalaryAsync("p1", 1, "2025-26", null, default);

        Assert.Null(salary);
    }
}
