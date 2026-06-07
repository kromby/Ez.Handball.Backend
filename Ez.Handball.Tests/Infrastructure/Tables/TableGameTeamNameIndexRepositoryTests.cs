using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameTeamNameIndexRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameTeamNameIndexRepository Sut() => new TableGameTeamNameIndexRepository(_client);

    public async Task InitializeAsync() =>
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeamNameIndex).CreateIfNotExistsAsync();
    public async Task DisposeAsync() =>
        await _client.GetTableClient(Ez.Handball.Infrastructure.Tables.GameTeamNameIndex).DeleteAsync();

    [Fact]
    public async Task Reserve_FirstTime_ReturnsTrue()
    {
        Assert.True(await Sut().TryReserveAsync("dream team", "u-1:fantasy", default));
    }

    [Fact]
    public async Task Reserve_Duplicate_ReturnsFalse()
    {
        await Sut().TryReserveAsync("dream team", "u-1:fantasy", default);
        Assert.False(await Sut().TryReserveAsync("dream team", "u-2:fantasy", default));
    }

    [Fact]
    public async Task Release_ThenReserveAgain_Succeeds()
    {
        await Sut().TryReserveAsync("dream team", "u-1:fantasy", default);
        await Sut().ReleaseAsync("dream team", default);
        Assert.True(await Sut().TryReserveAsync("dream team", "u-2:fantasy", default));
    }

    [Fact]
    public async Task Release_Missing_DoesNotThrow()
    {
        await Sut().ReleaseAsync("never reserved", default);
    }
}
