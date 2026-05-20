using Azure.Storage.Blobs;
using Ez.Handball.Ingestion.Services;
using Xunit;

namespace Ez.Handball.Tests.Services;

public class BlobArchiverTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private const string ContainerName = "raw-test";
    private BlobArchiver _archiver = null!;
    private BlobContainerClient _container = null!;

    public async Task InitializeAsync()
    {
        var serviceClient = new BlobServiceClient(ConnectionString);
        _container = serviceClient.GetBlobContainerClient(ContainerName);
        await _container.CreateIfNotExistsAsync();
        _archiver = new BlobArchiver(serviceClient, ContainerName);
    }

    public async Task DisposeAsync()
    {
        await _container.DeleteIfExistsAsync();
    }

    [Fact]
    public async Task SaveAsync_WritesBlob()
    {
        await _archiver.SaveAsync("tournaments/100/matches.json", """{"data":[]}""");

        var exists = await _archiver.ExistsAsync("tournaments/100/matches.json");
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenBlobMissing()
    {
        var exists = await _archiver.ExistsAsync("tournaments/999/matches.json");
        Assert.False(exists);
    }

    [Fact]
    public async Task ReadAsync_ReturnsStoredContent()
    {
        var json = """{"data":[{"GameId":"1"}]}""";
        await _archiver.SaveAsync("matches/1/details.json", json);

        var result = await _archiver.ReadAsync("matches/1/details.json");

        Assert.Equal(json, result);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_ExistingBlob()
    {
        await _archiver.SaveAsync("matches/2/details.json", """{"old":"data"}""");
        await _archiver.SaveAsync("matches/2/details.json", """{"new":"data"}""");

        var result = await _archiver.ReadAsync("matches/2/details.json");
        Assert.Equal("""{"new":"data"}""", result);
    }
}
