using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerSalaryUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerSalaryService> _salary = new();

    private GetPlayerSalaryUseCase CreateSut() => new(_players.Object, _salary.Object);

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back");

    private static PlayerSalary Salary() =>
        new("p1", new PlayerCost(20000000, "ISK"), 6, 8, "fantasy-price-v1");

    public GetPlayerSalaryUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        _salary.Setup(s => s.GetSalaryAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Salary());
    }

    [Fact]
    public async Task PlayerNotFound_ReturnsNotFound()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("ghost", null, null, null, default);

        Assert.IsType<GetPlayerSalaryResult.NotFound>(result);
    }

    [Fact]
    public async Task NullVersion_DefaultsToVersion1()
    {
        await CreateSut().ExecuteAsync("p1", null, "2025-26", "8444", default);

        _salary.Verify(s => s.GetSalaryAsync("p1", 1, "2025-26", "8444", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ServiceReturnsNull_ReturnsRuleSetNotFound()
    {
        _salary.Setup(s => s.GetSalaryAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((PlayerSalary?)null);

        var result = await CreateSut().ExecuteAsync("p1", null, null, null, default);

        Assert.IsType<GetPlayerSalaryResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Found_CarriesSalary()
    {
        var result = await CreateSut().ExecuteAsync("p1", 1, null, null, default);

        var found = Assert.IsType<GetPlayerSalaryResult.Found>(result);
        Assert.Equal(20000000, found.Salary.Cost.Amount);
        Assert.Equal("fantasy-price-v1", found.Salary.Version);
    }
}
