using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class TransferAllowanceCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    // Round n's deadline falls n weeks after T0.
    private static readonly IReadOnlyList<Gameweek> Calendar = Enumerable.Range(1, 6)
        .Select(n => new Gameweek(n, n.ToString(), "9142", T0.AddDays(7 * n), GameweekStatus.Open, []))
        .ToList();

    private static readonly TransferConfig Config = new(
        Version: 1, FreeTransfersPerRound: 1, MaxBankedFreeTransfers: 1, PointsPerExtraTransfer: 4, StartsFromRound: 1);

    // A team that existed before round 1's deadline.
    private static readonly DateTimeOffset? Founding = T0;

    private static TransferAllowance Compute(
        string round, IReadOnlyList<RoundTransferCount>? counts = null, DateTimeOffset? createdAt = null,
        TransferConfig? config = null, IReadOnlySet<string>? wildcardRounds = null)
        => TransferAllowanceCalculator.Compute(
               config ?? Config, Calendar, counts ?? [], createdAt ?? Founding, round, wildcardRounds)
           ?? throw new InvalidOperationException("round not found");

    [Fact]
    public void FirstRound_IsExempt()
    {
        var a = Compute("1", [new("1", Buys: 7, Sells: 3)]);

        Assert.True(a.Exempt);
        Assert.Equal(7, a.Used);
        Assert.Equal(0, a.Extra);
        Assert.Equal(0, a.PointsHit);
    }

    [Fact]
    public void OneBuyAndOneSell_AreFree()
    {
        var a = Compute("2", [new("2", Buys: 1, Sells: 1)]);

        Assert.False(a.Exempt);
        Assert.Equal(1, a.FreeAvailable);
        Assert.Equal(1, a.Used);
        Assert.Equal(0, a.Extra);
        Assert.Equal(0, a.PointsHit);
    }

    [Fact]
    public void SecondSwapInARound_IsExtra_AndCostsPoints()
    {
        var a = Compute("2", [new("2", Buys: 2, Sells: 2)]);

        Assert.Equal(2, a.Used);
        Assert.Equal(1, a.Extra);
        Assert.Equal(4, a.PointsHit);
    }

    [Fact]
    public void UnbalancedBuysAndSells_CountTheLarger()
    {
        var a = Compute("2", [new("2", Buys: 3, Sells: 1)]);

        Assert.Equal(3, a.Used);
        Assert.Equal(2, a.Extra);
        Assert.Equal(8, a.PointsHit);
    }

    [Fact]
    public void UnusedFreeTransfer_RollsOverToTheNextRound()
    {
        var a = Compute("3", [new("3", Buys: 2, Sells: 2)]);

        Assert.Equal(2, a.FreeAvailable);
        Assert.Equal(0, a.Extra);
    }

    [Fact]
    public void Rollover_IsCappedAtMaxBanked()
    {
        var a = Compute("5");

        Assert.Equal(2, a.FreeAvailable);
    }

    [Fact]
    public void UsedFreeTransfer_DoesNotBank()
    {
        var a = Compute("3", [new("2", Buys: 1, Sells: 1)]);

        Assert.Equal(1, a.FreeAvailable);
    }

    [Fact]
    public void BankedTransfer_IsSpentBeforeAHit()
    {
        var a = Compute("4", [new("3", Buys: 2, Sells: 2)]);

        // Round 3 had 2 free (1 banked from round 2) and used both, so nothing carries to round 4.
        Assert.Equal(1, a.FreeAvailable);
    }

    [Fact]
    public void TeamCreatedMidSeason_BuildsItsSquadFree()
    {
        // Created between round 2's and round 3's deadlines, so round 3 is its first round.
        var createdAt = Calendar[1].Deadline.AddHours(1);

        var building = Compute("3", [new("3", Buys: 7, Sells: 0)], createdAt);
        var firstCounted = Compute("4", [new("3", Buys: 7, Sells: 0)], createdAt);

        Assert.True(building.Exempt);
        Assert.Equal(0, building.PointsHit);
        Assert.False(firstCounted.Exempt);
        Assert.Equal(1, firstCounted.FreeAvailable); // building rounds bank nothing
    }

    [Fact]
    public void TeamCreatedExactlyAtADeadline_StillBuildsTheNextRoundFree()
    {
        var a = Compute("3", [new("3", Buys: 7, Sells: 0)], Calendar[1].Deadline);

        Assert.True(a.Exempt);
    }

    [Fact]
    public void RoundsBeforeStartsFromRound_AreExempt_AndBankNothing()
    {
        var config = Config with { StartsFromRound = 4 };

        var before = Compute("3", [new("3", Buys: 5, Sells: 5)], config: config);
        var first = Compute("4", config: config);

        Assert.True(before.Exempt);
        Assert.False(first.Exempt);
        Assert.Equal(1, first.FreeAvailable);
    }

    [Fact]
    public void WildcardRound_IsExempt()
    {
        var a = Compute("3", [new("3", Buys: 6, Sells: 6)], wildcardRounds: new HashSet<string> { "3" });

        Assert.True(a.Exempt);
        Assert.Equal(0, a.PointsHit);
    }

    [Fact]
    public void WildcardRound_KeepsTheBank()
    {
        var a = Compute("4", [new("3", Buys: 6, Sells: 6)], wildcardRounds: new HashSet<string> { "3" });

        Assert.Equal(2, a.FreeAvailable);
    }

    [Fact]
    public void ReportsTheRoundNumber()
    {
        Assert.Equal(4, Compute("4").RoundNumber);
    }

    [Fact]
    public void UnknownRound_ReturnsNull()
    {
        Assert.Null(TransferAllowanceCalculator.Compute(Config, Calendar, [], Founding, "99", null));
    }
}
