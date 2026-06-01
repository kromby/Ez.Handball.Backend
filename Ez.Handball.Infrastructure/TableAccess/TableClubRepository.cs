using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableClubRepository : IClubRepository
{
    private readonly TableServiceClient _client;

    public TableClubRepository(TableServiceClient client) => _client = client;

    public async Task<bool> ExistsAsync(string clubId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clubId)) return false;
        var table = _client.GetTableClient(Tables.Clubs);
        try
        {
            await table.GetEntityAsync<ClubEntity>("club", clubId, cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
