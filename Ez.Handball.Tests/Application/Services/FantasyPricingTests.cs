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
    private static readonly PriceRuleSet Prices =
        new(Version: 1, MinGames: 3, Currency: "ISK", Bands: new[]
        {
            new PriceBand(0, 1_000_000),
            new PriceBand(5, 5_000_000),
            new PriceBand(10, 11_000_000),
        }, BlendGames: 6);

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
        Assert.Equal(11_000_000, result.Price.Amount);
        Assert.Equal("ISK", result.Price.Currency);
    }

    [Fact]
    public void Compute_BelowMinGames_ScoreIsZero_FloorBand()
    {
        // 2 games < MinGames(3): score forced to 0 => floor band
        var stats = new AggregatedStats(Games: 2, Goals: 40, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(82, result.Rating);     // rating still computed: 40*2 goals + 2*1 appearances
        Assert.Equal(0, result.Score);
        Assert.Equal(1_000_000, result.Price.Amount);
    }

    [Fact]
    public void Compute_ZeroGames_ScoreIsZero()
    {
        var stats = new AggregatedStats(0, 0, 0, 0, 0);

        var result = CreateSut().Compute("p1", stats, Scoring, Prices, Ctx);

        Assert.Equal(0, result.Rating);
        Assert.Equal(0, result.Score);
        Assert.Equal(1_000_000, result.Price.Amount);
    }

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
}
