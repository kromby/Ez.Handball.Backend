using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SellPlayerUseCaseTests
{
    private readonly Mock<IGetSquadUseCase> _squadView = new();
    private readonly Mock<IPlayerPriceService> _price = new();
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameRosterRepository> _roster = new();
    private readonly Mock<IGameBudgetRepository> _budget = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private SellPlayerUseCase Sut() => new(
        _squadView.Object, _price.Object, _constraints.Object,
        _teams.Object, _roster.Object, _budget.Object, () => Now);

    private static PlayerPricing PriceOf(string id, double amount) =>
        new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1");
    private static SquadView EmptyView() =>
        new(System.Array.Empty<SquadPlayer>(), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));
    private static BuyPlayerContext Ctx => new(null, null, null);

    private void TeamExists() => _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    private void Owns(string id, double pricePaid) => _roster.Setup(r => r.GetAsync("u-1:fantasy", id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RosterEntry(id, "VS", pricePaid, null));
    private void Constraints(double fee) => _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK", fee));
    private void ViewReturns() => _squadView.Setup(u => u.ExecuteAsync("u-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GetSquadResult.Found(EmptyView()));

    [Fact]
    public async Task Sell_OnProfit_SoftDeletes_AndCreditsWithFee()
    {
        TeamExists(); Owns("p-1", 40_000_000); Constraints(0.5); ViewReturns();
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 50_000_000));

        var result = await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None);

        Assert.IsType<SellPlayerResult.Sold>(result);
        _roster.Verify(r => r.SoftDeleteAsync("u-1:fantasy", "p-1", Now, It.IsAny<CancellationToken>()), Times.Once);
        // 40M + floor(10M * 0.5) = 45M
        _budget.Verify(b => b.TryCreditAsync("u-1:fantasy", 45_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sell_OnLoss_CreditsCurrentValue()
    {
        TeamExists(); Owns("p-1", 40_000_000); Constraints(0.5); ViewReturns();
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 30_000_000));

        await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None);
        _budget.Verify(b => b.TryCreditAsync("u-1:fantasy", 30_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotInSquad_WhenNoRow_Returns404()
    {
        TeamExists();
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((RosterEntry?)null);
        Assert.IsType<SellPlayerResult.NotInSquad>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task NotInSquad_WhenSoftDeleted_Returns404()
    {
        TeamExists();
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RosterEntry("p-1", "VS", 40_000_000, Now)); // already sold
        Assert.IsType<SellPlayerResult.NotInSquad>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task RuleSetMissing_ReturnsRuleSetNotFound_NoWrite()
    {
        TeamExists(); Owns("p-1", 40_000_000);
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((PlayerPricing?)null);

        Assert.IsType<SellPlayerResult.RuleSetNotFound>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
        _roster.Verify(r => r.SoftDeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConstraintsMissing_ReturnsRuleSetNotFound_NoWrite()
    {
        TeamExists(); Owns("p-1", 40_000_000);
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 50_000_000));
        // _constraints.GetAsync not set up -> returns null (Moq default)

        Assert.IsType<SellPlayerResult.RuleSetNotFound>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
        _roster.Verify(r => r.SoftDeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _budget.Verify(b => b.TryCreditAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.IsType<SellPlayerResult.NoTeam>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }
}
