using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetSquadUseCaseTests
{
    private readonly Mock<ISquadRepository> _squad = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerSalaryService> _salary = new();

    private GetSquadUseCase CreateSut() => new(_squad.Object, _players.Object, _salary.Object);

    private static Player AnyPlayer(string id, string position) => new(
        id, "Aron", "23", null, 35, "385-karlar", "385", "Stjarnan", "karlar", position);

    private static PlayerSalary SalaryOf(string id, double amount) =>
        new(id, new PlayerCost(amount, "ISK"), 5.0, 10, "fantasy-price-v1");

    private void SetupSquad(double budget, params SquadSlot[] slots) =>
        _squad.Setup(r => r.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Squad(slots, budget, "ISK"));

    [Fact]
    public async Task Found_EnrichesPlayers_AndComputesMoneyFields()
    {
        // Paid 40M + 30M = 70M; cap 100M => budget 30M. Current prices 50M + 25M => value 75M.
        SetupSquad(30_000_000,
            new SquadSlot("p-1", "VS", new PlayerCost(40_000_000, "ISK")),
            new SquadSlot("p-2", "MM", new PlayerCost(30_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _players.Setup(r => r.GetByIdAsync("p-2", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-2", "MM"));
        _salary.Setup(s => s.GetSalaryAsync("p-1", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(SalaryOf("p-1", 50_000_000));
        _salary.Setup(s => s.GetSalaryAsync("p-2", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(SalaryOf("p-2", 25_000_000));

        var result = await CreateSut().ExecuteAsync("u-1", null, null, null, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        Assert.Equal(2, view.Players.Count);
        var p1 = Assert.Single(view.Players, p => p.PlayerId == "p-1");
        Assert.Equal("Aron", p1.Name);
        Assert.Equal("Stjarnan", p1.ClubName);
        Assert.Equal("VS", p1.Position);
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
        SetupSquad(60_000_000, new SquadSlot("ghost", "VS", new PlayerCost(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);
        _salary.Setup(s => s.GetSalaryAsync("ghost", 1, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(SalaryOf("ghost", 0));

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
        SetupSquad(60_000_000, new SquadSlot("p-1", "VS", new PlayerCost(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _salary.Setup(s => s.GetSalaryAsync("p-1", 9, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((PlayerSalary?)null);

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
    public async Task PassesScopeThroughToSalaryService()
    {
        SetupSquad(60_000_000, new SquadSlot("p-1", "VS", new PlayerCost(40_000_000, "ISK")));
        _players.Setup(r => r.GetByIdAsync("p-1", It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer("p-1", "VS"));
        _salary.Setup(s => s.GetSalaryAsync("p-1", 2, "2024", "t-9", It.IsAny<CancellationToken>())).ReturnsAsync(SalaryOf("p-1", 11_000_000));

        var result = await CreateSut().ExecuteAsync("u-1", "2024", "t-9", 2, CancellationToken.None);

        var view = Assert.IsType<GetSquadResult.Found>(result).View;
        Assert.Equal(11_000_000, Assert.Single(view.Players).Price.Amount);
        _salary.Verify(s => s.GetSalaryAsync("p-1", 2, "2024", "t-9", It.IsAny<CancellationToken>()), Times.Once);
    }
}
