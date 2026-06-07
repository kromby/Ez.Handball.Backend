using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerProfileUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerSalaryService> _salary = new();

    private GetPlayerProfileUseCase CreateSut() => new(_players.Object, _salary.Object);

    private static Player SamplePlayer() => new(
        PlayerId: "12345", Name: "Aron Pálmarsson", JerseyNumber: "23",
        DateOfBirth: new DateOnly(1990, 7, 19), Age: 35,
        TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
        Position: "VS");

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_ReturnsNotFound()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetPlayerProfileResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExists_ReturnsFoundWithPlayerAndPrice()
    {
        var player = SamplePlayer();
        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        _salary
            .Setup(s => s.GetSalaryAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerSalary(
                "12345", new PlayerCost(11_000_000, "ISK"), Score: 11, Games: 10, Version: "fantasy-price-v1"));

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.NotNull(found.Price);
        Assert.Equal(11_000_000, found.Price!.Amount);
        Assert.Equal("ISK", found.Price.Currency);
    }

    [Fact]
    public async Task ExecuteAsync_RuleSetMissing_ReturnsFoundWithNullPrice()
    {
        var player = SamplePlayer();
        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        _salary
            .Setup(s => s.GetSalaryAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerSalary?)null);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.Null(found.Price);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_DoesNotCallSalary()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        _salary.Verify(s => s.GetSalaryAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
