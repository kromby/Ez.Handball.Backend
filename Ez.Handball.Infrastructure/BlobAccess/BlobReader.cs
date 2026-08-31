using Azure;
using Azure.Storage.Blobs;

namespace Ez.Handball.Infrastructure.BlobAccess;

public sealed class BlobReader : IBlobReader
{
    private readonly BlobContainerClient _container;

    public BlobReader(BlobServiceClient client, string containerName) =>
        _container = client.GetBlobContainerClient(containerName);

    public async Task<BlobContent?> ReadAsync(string path, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(path);
        try
        {
            var download = await blob.DownloadContentAsync(ct);
            return new BlobContent(download.Value.Content.ToString(), download.Value.Details.LastModified);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
