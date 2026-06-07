using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class BuyPlayerUseCaseTests
{
    private readonly Mock<IGetBuyDecisionUseCase> _decision = new();
    private readonly Mock<IGetSquadUseCase> _squadView = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameRosterRepository> _roster = new();
    private readonly Mock<IGameBudgetRepository> _budget = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private BuyPlayerUseCase Sut() => new(
        _decision.Object, _squadView.Object, _players.Object,
        _teams.Object, _roster.Object, _budget.Object, () => Now);

    private static Player AnyPlayer(string id, string position) =>
        new(id, "Aron", "23", null, 35, "385-karlar", "385", "Stjarnan", "karlar", position);

    private static BuyDecision Allowed(string id, double cost) =>
        new(id, "fantasy", true, new PlayerCost(cost, "ISK"), System.Array.Empty<BuyRuleViolation>(), "fantasy-price-v1");

    private static BuyDecision Rejected(string id, double cost, params BuyRuleViolation[] v) =>
        new(id, "fantasy", false, new PlayerCost(cost, "ISK"), v, "fantasy-price-v1");

    private static SquadView EmptyView() =>
        new(System.Array.Empty<SquadPlayer>(), new PlayerCost(0, "ISK"), new PlayerCost(0, "ISK"), new PlayerCost(0, "ISK"));

    private void TeamExists() => _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    private void PlayerExists(string id, string pos) => _players.Setup(p => p.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer(id, pos));
    private void DecisionReturns(BuyDecision d) => _decision
        .Setup(u => u.ExecuteAsync("u-1", "p-1", GameFlavor.Fantasy, It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new BuyDecisionResult.Decided(d));
    private void SquadViewReturns() => _squadView
        .Setup(u => u.ExecuteAsync("u-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GetSquadResult.Found(EmptyView()));

    private static BuyPlayerContext Ctx => new(null, null, null);

    [Fact]
    public async Task Allowed_AddsRoster_DeductsBudget_ReturnsCommitted()
    {
        TeamExists(); PlayerExists("p-1", "VS"); DecisionReturns(Allowed("p-1", 42_000_000)); SquadViewReturns();
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((RosterEntry?)null);
        _budget.Setup(b => b.TryDeductAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _roster.Setup(r => r.AddOrResurrectAsync("u-1:fantasy", "p-1", "VS", 42_000_000, Now, It.IsAny<CancellationToken>()))
               .ReturnsAsync(RosterAddOutcome.Added);

        var result = await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None);

        Assert.IsType<BuyPlayerResult.Committed>(result);
        _budget.Verify(b => b.TryDeductAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
        _roster.Verify(r => r.AddOrResurrectAsync("u-1:fantasy", "p-1", "VS", 42_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectedByRule_WritesNothing_ReturnsRejected()
    {
        TeamExists(); PlayerExists("p-1", "VS");
        DecisionReturns(Rejected("p-1", 42_000_000, new BuyRuleViolation("squad_full", "Squad is full")));

        var result = await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None);

        var rejected = Assert.IsType<BuyPlayerResult.Rejected>(result);
        Assert.Equal("squad_full", Assert.Single(rejected.Violations).Code);
        _budget.Verify(b => b.TryDeductAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _roster.Verify(r => r.AddOrResurrectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateViolation_MapsToDuplicate()
    {
        TeamExists(); PlayerExists("p-1", "VS");
        DecisionReturns(Rejected("p-1", 42_000_000, new BuyRuleViolation("duplicate_player", "Already owned")));

        Assert.IsType<BuyPlayerResult.Duplicate>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task WriteTimeDuplicate_WhenActiveRowExists_ReturnsDuplicate_NoDeduct()
    {
        TeamExists(); PlayerExists("p-1", "VS"); DecisionReturns(Allowed("p-1", 42_000_000));
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new RosterEntry("p-1", "VS", 10_000_000, null)); // active

        Assert.IsType<BuyPlayerResult.Duplicate>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
        _budget.Verify(b => b.TryDeductAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeductRacesIntoInsufficient_ReturnsRejected_InsufficientBudget()
    {
        TeamExists(); PlayerExists("p-1", "VS"); DecisionReturns(Allowed("p-1", 42_000_000));
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((RosterEntry?)null);
        _budget.Setup(b => b.TryDeductAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None);
        var rejected = Assert.IsType<BuyPlayerResult.Rejected>(result);
        Assert.Equal("insufficient_budget", Assert.Single(rejected.Violations).Code);
        _roster.Verify(r => r.AddOrResurrectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddRacesToAlreadyActive_CompensatesBudget_ReturnsDuplicate()
    {
        TeamExists(); PlayerExists("p-1", "VS"); DecisionReturns(Allowed("p-1", 42_000_000));
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((RosterEntry?)null);
        _budget.Setup(b => b.TryDeductAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _roster.Setup(r => r.AddOrResurrectAsync("u-1:fantasy", "p-1", "VS", 42_000_000, Now, It.IsAny<CancellationToken>()))
               .ReturnsAsync(RosterAddOutcome.AlreadyActive);

        Assert.IsType<BuyPlayerResult.Duplicate>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
        _budget.Verify(b => b.TryCreditAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>()), Times.Once); // compensated
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.IsType<BuyPlayerResult.NoTeam>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task PlayerNotFound_BeforeDecision_ReturnsPlayerNotFound()
    {
        TeamExists();
        _players.Setup(p => p.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);
        Assert.IsType<BuyPlayerResult.PlayerNotFound>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task PlayerNotFound_FromDecision_ReturnsPlayerNotFound()
    {
        TeamExists(); PlayerExists("p-1", "VS");
        _decision.Setup(u => u.ExecuteAsync("u-1", "p-1", GameFlavor.Fantasy, It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyDecisionResult.PlayerNotFound.Instance);
        Assert.IsType<BuyPlayerResult.PlayerNotFound>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Committed_ButSquadViewRuleSetMissing_ReturnsRuleSetNotFound()
    {
        TeamExists(); PlayerExists("p-1", "VS"); DecisionReturns(Allowed("p-1", 42_000_000));
        _roster.Setup(r => r.GetAsync("u-1:fantasy", "p-1", It.IsAny<CancellationToken>())).ReturnsAsync((RosterEntry?)null);
        _budget.Setup(b => b.TryDeductAsync("u-1:fantasy", 42_000_000, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _roster.Setup(r => r.AddOrResurrectAsync("u-1:fantasy", "p-1", "VS", 42_000_000, Now, It.IsAny<CancellationToken>()))
               .ReturnsAsync(RosterAddOutcome.Added);
        _squadView.Setup(u => u.ExecuteAsync("u-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadResult.RuleSetNotFound.Instance);

        Assert.IsType<BuyPlayerResult.RuleSetNotFound>(await Sut().ExecuteAsync("u-1", "p-1", Ctx, CancellationToken.None));
    }
}
