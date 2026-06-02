using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableClubRepository : IClubRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableClubRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

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

    public async Task<IReadOnlyList<Club>> ListAsync(CancellationToken ct)
    {
        var clubs = new List<Club>();
        await foreach (var c in _query.QueryAsync<ClubEntity>(Tables.Clubs, "PartitionKey eq 'club'", ct))
        {
            var logoUrl = string.IsNullOrEmpty(c.LogoSrc) ? null : c.LogoSrc;
            clubs.Add(new Club(c.RowKey, c.Name, logoUrl));
        }

        return clubs
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
