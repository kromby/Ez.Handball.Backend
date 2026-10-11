using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetSquadUseCaseTests
{
    private readonly Mock<ISquadRepository> _squad = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerPriceService> _price = new();

    private readonly Mock<ISquadConstraintsRepository> _constraints = new();

    private GetSquadUseCase CreateSut() => new(_squad.Object, _players.Object, _price.Object, _constraints.Object);

    private static Player AnyPlayer(string id, string position) => new(
        id, "Aron", "23", null, 35, "385-karlar", "385", "Stjarnan", "karlar", position, false);

    private static PlayerPricing PriceOf(string id, double amount) =>
        new(id, new PlayerPrice(amount, "ISK"), 5.0, 10, "fantasy-price-v1", 50.0);

    private void SetupSquad(double budget, params SquadSlot[] slots) =>
        _squad.Setup(r => r.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Squad(slots, budget, "ISK"));

    [Fact]
    public async Task Found_AssignsDualPlayerToSecondary_WhenPrimaryCapped()
    {
        SetupSquad(0,
            new SquadSlot("p-1", "CB", new PlayerPrice(1, "ISK")),
            new SquadSlot("p-2", "CB", new PlayerPrice(1, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "CB"));
        _players.Setup(r => r.GetByIdAsync("p-2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("p-2", "CB") with { PositionSecondary = "LB" });
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 2) with { Rating = 9 });
        _price.Setup(s => s.GetPriceAsync("p-2", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-2", 1) with { Rating = 1 });
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int> { ["CB"] = 1, ["LB"] = 1 }, 100_000_000, "ISK"));

        var view = Assert.IsType<GetSquadResult.Found>(
            await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None)).View;

        var p2 = view.Players.Single(p => p.PlayerId == "p-2");
        Assert.Equal("LB", p2.PositionSecondary);
        Assert.Equal("LB", p2.AssignedPosition);
        Assert.Equal("CB", view.Players.Single(p => p.PlayerId == "p-1").AssignedPosition);
    }

    [Fact]
    public async Task Found_NoConstraints_AssignedPositionIsPrimary()
    {
        SetupSquad(0, new SquadSlot("p-1", "CB", new PlayerPrice(1, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("p-1", "CB") with { PositionSecondary = "LB" });
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 1));

        var view = Assert.IsType<GetSquadResult.Found>(
            await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None)).View;

        Assert.Equal("CB", view.Players.Single().AssignedPosition);
    }

    [Fact]
    public async Task Found_EnrichesPlayers_AndComputesMoneyFields()
    {
        // Paid 40M + 30M = 70M; cap 100M => budget 30M. Current prices 50M + 25M => value 75M.
        SetupSquad(30_000_000,
            new SquadSlot("p-1", "VS", new PlayerPrice(40_000_000, "ISK")),
            new SquadSlot("p-2", "MM", new PlayerPrice(30_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _players.Setup(r => r.GetByIdAsync("p-2", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-2", "MM"));
        _price.Setup(s => s.GetPriceAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 50_000_000));
        _price.Setup(s => s.GetPriceAsync("p-2", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-2", 25_000_000));

        var result = await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        Assert.Equal(2, view.Players.Count);
        var p1 = Assert.Single(view.Players, p => p.PlayerId == "p-1");
        Assert.Equal("Aron", p1.Name);
        Assert.Equal("Stjarnan", p1.ClubName);
        Assert.Equal("VS", p1.Position);
        Assert.Equal(50.0, p1.Rating);
        Assert.Equal(50_000_000, p1.Price.Amount);
        Assert.Equal(40_000_000, p1.PricePaid.Amount);
        Assert.Equal(70_000_000, view.BudgetUsed.Amount);
        Assert.Equal(30_000_000, view.RemainingBudget.Amount);
        Assert.Equal(75_000_000, view.SquadValue.Amount);
        Assert.Equal("ISK", view.SquadValue.Currency);
    }

    [Fact]
    public async Task UnresolvedPlayer_IsKept_WithNullEnrichment()
    {
        SetupSquad(60_000_000, new SquadSlot("ghost", "VS", new PlayerPrice(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);
        _price.Setup(s => s.GetPriceAsync("ghost", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("ghost", 0));

        var result = await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        var p = Assert.Single(view.Players);
        Assert.Equal("ghost", p.PlayerId);
        Assert.Null(p.Name);
        Assert.Equal(0, p.Price.Amount);          // unpriceable contributes 0
        Assert.Equal(0, view.SquadValue.Amount);
        Assert.Equal(40_000_000, view.BudgetUsed.Amount);
    }

    [Fact]
    public async Task InvalidRuleSet_ReturnsRuleSetNotFound()
    {
        SetupSquad(60_000_000, new SquadSlot("p-1", "VS", new PlayerPrice(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _price.Setup(s => s.GetPriceAsync("p-1", 9, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((PlayerPricing?)null);

        var result = await CreateSut().ExecuteAsync("u-1", null, null, 9, CancellationToken.None);

        Assert.IsType<GetSquadResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task EmptySquad_ReturnsZeroSums_AndCapAsRemaining()
    {
        SetupSquad(100_000_000); // no slots, budget == cap

        var result = await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        Assert.Empty(view.Players);
        Assert.Equal(0, view.BudgetUsed.Amount);
        Assert.Equal(0, view.SquadValue.Amount);
        Assert.Equal(100_000_000, view.RemainingBudget.Amount);
        Assert.Equal("ISK", view.RemainingBudget.Currency);
    }

    [Fact]
    public async Task PassesScopeThroughToPriceService()
    {
        SetupSquad(60_000_000, new SquadSlot("p-1", "VS", new PlayerPrice(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _price.Setup(s => s.GetPriceAsync("p-1", 2, "2024", "t-9", It.IsAny<CancellationToken>())).ReturnsAsync(PriceOf("p-1", 11_000_000));

        var result = await CreateSut().ExecuteAsync("u-1", "2024", "t-9", 2, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        Assert.Equal(11_000_000, Assert.Single(view.Players).Price.Amount);
        _price.Verify(s => s.GetPriceAsync("p-1", 2, "2024", "t-9", It.IsAny<CancellationToken>()), Times.Once);
    }
}
