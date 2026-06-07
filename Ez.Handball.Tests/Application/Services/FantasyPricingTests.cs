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
