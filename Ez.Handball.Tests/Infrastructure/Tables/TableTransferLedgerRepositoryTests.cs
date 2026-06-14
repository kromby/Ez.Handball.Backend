using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableTransferLedgerRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private ITransferLedgerRepository Sut() => new TableTransferLedgerRepository(_client, new TableQuery(_client));

    // A fixed reference instant well inside an ISO week (Wed 2026-06-10 = 2026-W24).
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameTransferLedger).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameTransferLedger).DeleteAsync();

    private static TransferEntry Entry(string playerId, TransferType type, DateTimeOffset at, double cost = 1_000) =>
        new("u-1", playerId, GameFlavor.Fantasy, type, cost, "2025-26", at);

    [Fact]
    public async Task Append_ThenListSince_ReturnsTheEvent()
    {
        await Sut().AppendAsync(Entry("p-1", TransferType.Buy, Now), default);

        var rows = await Sut().ListSinceAsync(GameFlavor.Fantasy, Now.AddDays(-7), Now, default);

        var e = Assert.Single(rows);
        Assert.Equal("p-1", e.PlayerId);
        Assert.Equal(TransferType.Buy, e.Type);
        Assert.Equal(1_000, e.Cost);
        Assert.Equal("2025-26", e.SeasonLabel);
    }

    [Fact]
    public async Task Append_BuyAndSellSamePlayer_BothPersist()
    {
        await Sut().AppendAsync(Entry("p-1", TransferType.Buy, Now), default);
        await Sut().AppendAsync(Entry("p-1", TransferType.Sell, Now.AddHours(1)), default);

        var rows = await Sut().ListSinceAsync(GameFlavor.Fantasy, Now.AddDays(-7), Now.AddDays(1), default);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Type == TransferType.Buy);
        Assert.Contains(rows, r => r.Type == TransferType.Sell);
    }

    [Fact]
    public async Task ListSince_ExcludesEventsBeforeWindow()
    {
        await Sut().AppendAsync(Entry("p-old", TransferType.Buy, Now.AddDays(-10)), default);
        await Sut().AppendAsync(Entry("p-new", TransferType.Buy, Now), default);

        var rows = await Sut().ListSinceAsync(GameFlavor.Fantasy, Now.AddDays(-7), Now, default);

        var e = Assert.Single(rows);
        Assert.Equal("p-new", e.PlayerId);
    }

    [Fact]
    public async Task ListSince_SpanningTwoIsoWeeks_IncludesBothBuckets()
    {
        // Mon 2026-06-08 is 2026-W24; the prior Sat 2026-06-06 is 2026-W23.
        var inW24 = new DateTimeOffset(2026, 6, 8, 9, 0, 0, TimeSpan.Zero);
        var inW23 = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        await Sut().AppendAsync(Entry("p-23", TransferType.Buy, inW23), default);
        await Sut().AppendAsync(Entry("p-24", TransferType.Buy, inW24), default);

        var rows = await Sut().ListSinceAsync(GameFlavor.Fantasy, inW23.AddHours(-1), inW24, default);

        Assert.Equal(2, rows.Count);
    }
}
