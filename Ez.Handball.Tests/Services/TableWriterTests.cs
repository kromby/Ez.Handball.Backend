using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Xunit;

namespace Ez.Handball.Tests.Services;

public class TableWriterTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "TestTournaments";
    private TableWriter _writer = null!;
    private TableClient _tableClient = null!;

    public async Task InitializeAsync()
    {
        var serviceClient = new TableServiceClient(ConnectionString);
        _writer = new TableWriter(serviceClient);
        _tableClient = serviceClient.GetTableClient(TableName);
        await _tableClient.CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync()
    {
        await _tableClient.DeleteAsync();
    }

    [Fact]
    public async Task UpsertAsync_InsertsNewEntity()
    {
        var entity = new TournamentEntity
        {
            PartitionKey = "2025",
            RowKey = "8444",
            Name = "Olís deild karla",
            Gender = "karlar",
            Division = "1"
        };

        await _writer.UpsertAsync(TableName, entity);

        var result = await _writer.GetAsync<TournamentEntity>(TableName, "2025", "8444");
        Assert.NotNull(result);
        Assert.Equal("Olís deild karla", result.Name);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingEntity()
    {
        var entity = new TournamentEntity
        {
            PartitionKey = "2025",
            RowKey = "8434",
            Name = "Old Name",
            Gender = "kvenna",
            Division = "1"
        };
        await _writer.UpsertAsync(TableName, entity);

        entity.Name = "Olís deild kvenna";
        await _writer.UpsertAsync(TableName, entity);

        var result = await _writer.GetAsync<TournamentEntity>(TableName, "2025", "8434");
        Assert.Equal("Olís deild kvenna", result!.Name);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenEntityMissing()
    {
        var result = await _writer.GetAsync<TournamentEntity>(TableName, "2025", "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task QueryAsync_ReturnsMatchingEntities()
    {
        await _writer.UpsertAsync(TableName, new TournamentEntity
            { PartitionKey = "2025", RowKey = "8444", Name = "Olís deild karla", Gender = "karlar", Division = "1" });
        await _writer.UpsertAsync(TableName, new TournamentEntity
            { PartitionKey = "2025", RowKey = "8434", Name = "Olís deild kvenna", Gender = "kvenna", Division = "1" });

        var results = await _writer.QueryAsync<TournamentEntity>(TableName, "RowKey eq '8444'");

        Assert.Single(results);
        Assert.Equal("8444", results[0].RowKey);
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmpty_WhenTableMissing()
    {
        // Azure table names must be alphanumeric, 3-63 chars, start with a letter — no underscores.
        var missing = "Missing" + Guid.NewGuid().ToString("N");

        var results = await _writer.QueryAsync<TournamentEntity>(missing, "RowKey eq 'anything'");

        Assert.Empty(results);
    }
}
