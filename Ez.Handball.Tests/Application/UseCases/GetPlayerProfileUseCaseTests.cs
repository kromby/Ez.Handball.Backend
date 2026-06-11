using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerProfileUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerPriceService> _price = new();

    private GetPlayerProfileUseCase CreateSut() => new(_players.Object, _price.Object);

    private static Player SamplePlayer() => new(
        PlayerId: "12345", Name: "Aron Pálmarsson", JerseyNumber: "23",
        DateOfBirth: new DateOnly(1990, 7, 19), Age: 35,
        TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
        Position: "VS", Retired: false);

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
        _price
            .Setup(s => s.GetPriceAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerPricing(
                "12345", new PlayerPrice(11_000_000, "ISK"), Score: 11, Games: 10, Version: "fantasy-price-v1", Rating: 128));

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.NotNull(found.Price);
        Assert.Equal(11_000_000, found.Price!.Amount);
        Assert.Equal("ISK", found.Price.Currency);
        Assert.Equal(128, found.Rating);
    }

    [Fact]
    public async Task ExecuteAsync_RuleSetMissing_ReturnsFoundWithNullPrice()
    {
        var player = SamplePlayer();
        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);
        _price
            .Setup(s => s.GetPriceAsync("12345", 1, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerPricing?)null);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
        Assert.Null(found.Price);
        Assert.Null(found.Rating);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_DoesNotCallPrice()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        _price.Verify(s => s.GetPriceAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
