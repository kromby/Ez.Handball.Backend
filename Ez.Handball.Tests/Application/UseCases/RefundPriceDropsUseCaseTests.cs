using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RefundPriceDropsUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameRosterRepository> _roster = new();
    private readonly Mock<IGameBudgetRepository> _budget = new();
    private readonly Mock<IPlayerPriceService> _price = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    public RefundPriceDropsUseCaseTests()
    {
        _roster.Setup(r => r.TryRebasePricePaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
        _budget.Setup(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
    }

    private RefundPriceDropsUseCase Sut() => new(_teams.Object, _roster.Object, _budget.Object, _price.Object, () => Now);

    private void Teams(params string[] teamIds) =>
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(teamIds.Select(id => new GameTeam(id, "Team", "#000", Now)).ToList());

    private void Owns(string teamId, params (string PlayerId, double Paid)[] slots) =>
        _roster.Setup(r => r.ListActiveAsync(teamId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(slots.Select(s => new RosterEntry(s.PlayerId, "CB", s.Paid, null)).ToList());

    // The squad view and sell path price with the default scope (no season/tournament), version 1.
    private void PriceIs(string playerId, double amount) =>
        _price.Setup(p => p.GetPriceAsync(playerId, 1, null, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new PlayerPricing(playerId, new PlayerPrice(amount, "ISK"), 10, 9, "fantasy-price-v1", 90));

    [Fact]
    public async Task PriceDrop_RebasesPricePaid_AndCreditsTheDifference()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        PriceIs("p-1", 35_000_000);

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        _roster.Verify(r => r.TryRebasePricePaidAsync("u-1:fantasy", "p-1", 40_000_000, 35_000_000, It.IsAny<CancellationToken>()), Times.Once);
        _budget.Verify(b => b.TryCreditAsync("u-1:fantasy", 5_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, result.Report.TeamsScanned);
        Assert.Equal(1, result.Report.PlayersRebased);
        Assert.Equal(5_000_000, result.Report.TotalRefunded);
        Assert.Empty(result.Report.Failures);
    }

    [Fact]
    public async Task PriceRiseOrUnchanged_IsLeftAlone()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 5_000_000), ("p-2", 20_000_000));
        PriceIs("p-1", 9_000_000);
        PriceIs("p-2", 20_000_000);

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        Assert.Equal(0, result.Report.PlayersRebased);
        _roster.Verify(r => r.TryRebasePricePaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        _budget.Verify(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DryRun_ReportsTotals_ButWritesNothing()
    {
        Teams("u-1:fantasy", "u-2:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000), ("p-2", 10_000_000));
        Owns("u-2:fantasy", ("p-1", 30_000_000));
        PriceIs("p-1", 35_000_000);
        PriceIs("p-2", 7_000_000);

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: true, CancellationToken.None));

        Assert.Equal(2, result.Report.TeamsScanned);
        Assert.Equal(2, result.Report.PlayersRebased);           // u-1's p-1 and p-2; u-2's p-1 rose
        Assert.Equal(8_000_000, result.Report.TotalRefunded);    // 5M + 3M
        _roster.Verify(r => r.TryRebasePricePaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        _budget.Verify(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RebaseLosesARace_SkipsTheCredit_AndReportsTheFailure()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        PriceIs("p-1", 35_000_000);
        _roster.Setup(r => r.TryRebasePricePaidAsync("u-1:fantasy", "p-1", 40_000_000, 35_000_000, It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        Assert.Equal(0, result.Report.PlayersRebased);
        var failure = Assert.Single(result.Report.Failures);
        Assert.StartsWith("u-1:fantasy/p-1: price paid not updated", failure);
        Assert.Contains("safe to re-run", failure);
        _budget.Verify(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreditFails_ReportsTheFailure_SoAnAdminCanFixTheBalance()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        PriceIs("p-1", 35_000_000);
        _budget.Setup(b => b.TryCreditAsync("u-1:fantasy", 5_000_000, Now, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        Assert.Equal(0, result.Report.TotalRefunded);
        var failure = Assert.Single(result.Report.Failures);
        Assert.StartsWith("u-1:fantasy/p-1: price paid updated but 5000000 ISK not credited", failure);
        Assert.Contains("credit it by hand", failure);
    }

    [Fact]
    public async Task CreditThrows_ReportsTheAmountOwed_AndCarriesOnWithTheNextRow()
    {
        Teams("u-1:fantasy", "u-2:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        Owns("u-2:fantasy", ("p-2", 10_000_000));
        PriceIs("p-1", 35_000_000);
        PriceIs("p-2", 7_000_000);
        _budget.Setup(b => b.TryCreditAsync("u-1:fantasy", 5_000_000, Now, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("storage timeout"));

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        Assert.Equal(2, result.Report.PlayersRebased);
        Assert.Equal(3_000_000, result.Report.TotalRefunded);   // only u-2's credit is known to have landed
        var failure = Assert.Single(result.Report.Failures);
        Assert.StartsWith("u-1:fantasy/p-1: price paid updated but the 5000000 ISK credit may not have applied", failure);
        Assert.Contains("storage timeout", failure);
        _budget.Verify(b => b.TryCreditAsync("u-2:fantasy", 3_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RebaseThrows_ReportsTheAmountThatMayBeOwed_AndSkipsTheCredit()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        PriceIs("p-1", 35_000_000);
        _roster.Setup(r => r.TryRebasePricePaidAsync("u-1:fantasy", "p-1", 40_000_000, 35_000_000, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("storage timeout"));

        var result = Assert.IsType<RefundPriceDropsResult.Done>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));

        Assert.Equal(0, result.Report.PlayersRebased);
        var failure = Assert.Single(result.Report.Failures);
        Assert.StartsWith("u-1:fantasy/p-1: updating the price paid failed", failure);
        Assert.Contains("5000000 ISK", failure);
        Assert.Contains("storage timeout", failure);
        _budget.Verify(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowedAsARowFailure()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        PriceIs("p-1", 35_000_000);
        _budget.Setup(b => b.TryCreditAsync("u-1:fantasy", 5_000_000, Now, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut().ExecuteAsync(dryRun: false, CancellationToken.None));
    }

    [Fact]
    public async Task MissingPriceRuleSet_ReturnsRuleSetNotFound_WithoutWriting()
    {
        Teams("u-1:fantasy");
        Owns("u-1:fantasy", ("p-1", 40_000_000));
        _price.Setup(p => p.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((PlayerPricing?)null);

        Assert.IsType<RefundPriceDropsResult.RuleSetNotFound>(await Sut().ExecuteAsync(dryRun: false, CancellationToken.None));
        _roster.Verify(r => r.TryRebasePricePaidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
