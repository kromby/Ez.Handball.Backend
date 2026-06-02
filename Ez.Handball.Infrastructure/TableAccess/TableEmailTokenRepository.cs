using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableEmailTokenRepository : IEmailTokenRepository
{
    private readonly TableServiceClient _client;

    public TableEmailTokenRepository(TableServiceClient client) => _client = client;

    public async Task AddAsync(EmailTokenEntity token, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.EmailTokens);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.AddEntityAsync(token, ct);
    }

    public async Task<EmailTokenEntity?> GetAsync(string purpose, string tokenHash, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.EmailTokens);
        try
        {
            return (await table.GetEntityAsync<EmailTokenEntity>(purpose, tokenHash, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string purpose, string tokenHash, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.EmailTokens);
        await table.DeleteEntityAsync(purpose, tokenHash, ETag.All, ct);
    }
}
