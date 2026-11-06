using System.Text;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameTeamNameIndexRepository : IGameTeamNameIndexRepository
{
    private readonly TableServiceClient _client;

    public TableGameTeamNameIndexRepository(TableServiceClient client) => _client = client;

    public async Task<bool> TryReserveAsync(string normalizedName, string teamId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeamNameIndex);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        try
        {
            await table.AddEntityAsync(
                new GameTeamNameIndexEntity { RowKey = ToRowKey(normalizedName), TeamId = teamId }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false; // name already indexed → uniqueness violation
        }
    }

    public async Task ReleaseAsync(string normalizedName, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeamNameIndex);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.DeleteEntityAsync("name", ToRowKey(normalizedName), ETag.All, ct); // 404 swallowed by SDK
    }

    // Table keys reject '/', '\', '#', '?' and control characters, so those are percent-encoded.
    // Every other character (including '%') is kept as-is so reservations written before this
    // escaping still match. "a#" and "a%23" therefore share a key, which only makes one of them
    // look taken.
    internal static string ToRowKey(string normalizedName)
    {
        var sb = new StringBuilder(normalizedName.Length);
        foreach (var c in normalizedName)
        {
            if (c is '/' or '\\' or '#' or '?' || char.IsControl(c))
                sb.Append('%').Append(((int)c).ToString("X2"));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
