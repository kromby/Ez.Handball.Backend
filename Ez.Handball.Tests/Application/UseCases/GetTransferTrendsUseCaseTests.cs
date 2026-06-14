using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetTransferTrendsUseCaseTests
{
    private readonly Mock<ITransferLedgerRepository> _ledger = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private GetTransferTrendsUseCase Sut() => new(_ledger.Object, _players.Object, () => Now);

    private static Player AnyPlayer(string id, string name, string club) =>
        new(id, name, "23", null, 30, $"{club}-karlar", club, club == "385" ? "Stjarnan" : "Valur", "karlar", "VS", false);

    private static TransferEntry E(string playerId, TransferType type) =>
        new("u-1", playerId, GameFlavor.Fantasy, type, 1_000, "2025-26", Now);

    private void LedgerReturns(params TransferEntry[] entries) => _ledger
        .Setup(l => l.ListSinceAsync(GameFlavor.Fantasy, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(entries);

    private void PlayerExists(string id, string name, string club) => _players
        .Setup(p => p.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(AnyPlayer(id, name, club));

    [Fact]
    public async Task InvalidWindow_ReturnsInvalidWindow()
    {
        var result = await Sut().ExecuteAsync(GameFlavor.Fantasy, "5d", CancellationToken.None);
        Assert.IsType<TransferTrendsResult.InvalidWindow>(result);
    }

    [Fact]
    public async Task NullWindow_DefaultsToSevenDays()
    {
        LedgerReturns();
        await Sut().ExecuteAsync(GameFlavor.Fantasy, null, CancellationToken.None);
        _ledger.Verify(l => l.ListSinceAsync(GameFlavor.Fantasy, Now.AddDays(-7), Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CountsBuysAsSigned_AndSellsAsDropped_GrossPerType()
    {
        LedgerReturns(
            E("p-1", TransferType.Buy), E("p-1", TransferType.Buy), E("p-2", TransferType.Buy),
            E("p-1", TransferType.Sell));
        PlayerExists("p-1", "Aron", "385");
        PlayerExists("p-2", "Bjarni", "777");

        var result = Assert.IsType<TransferTrendsResult.Ok>(await Sut().ExecuteAsync(GameFlavor.Fantasy, "7d", CancellationToken.None));
        var trends = result.Trends;

        Assert.Equal("p-1", trends.MostSigned[0].PlayerId);
        Assert.Equal(2, trends.MostSigned[0].Count);
        Assert.Equal("Aron", trends.MostSigned[0].Name);
        Assert.Equal("Stjarnan", trends.MostSigned[0].ClubName);
        Assert.Equal("p-2", trends.MostSigned[1].PlayerId);
        // A buy and a later sell of p-1 count in both lists independently.
        Assert.Equal("p-1", Assert.Single(trends.MostDropped).PlayerId);
        Assert.Equal(1, trends.MostDropped[0].Count);
    }

    [Fact]
    public async Task MissingPlayer_StillIncludedWithNullName()
    {
        LedgerReturns(E("ghost", TransferType.Buy));
        _players.Setup(p => p.GetByIdAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = Assert.IsType<TransferTrendsResult.Ok>(await Sut().ExecuteAsync(GameFlavor.Fantasy, "7d", CancellationToken.None));

        var entry = Assert.Single(result.Trends.MostSigned);
        Assert.Equal("ghost", entry.PlayerId);
        Assert.Null(entry.Name);
        Assert.Null(entry.ClubName);
    }
}
