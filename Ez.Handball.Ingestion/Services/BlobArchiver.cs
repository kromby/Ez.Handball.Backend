using System.Runtime.CompilerServices;
using System.Text;
using Azure.Storage.Blobs;

namespace Ez.Handball.Ingestion.Services;

public class BlobArchiver : IBlobArchiver
{
    private readonly BlobContainerClient _container;

    public BlobArchiver(BlobServiceClient serviceClient, string containerName)
    {
        _container = serviceClient.GetBlobContainerClient(containerName);
    }

    public async Task SaveAsync(string path, string json, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = _container.GetBlobClient(path);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        var response = await blob.ExistsAsync(ct);
        return response.Value;
    }

    public async Task<string> ReadAsync(string path, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        var download = await blob.DownloadContentAsync(ct);
        return download.Value.Content.ToString();
    }

    public async IAsyncEnumerable<string> ListAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in _container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
        {
            yield return item.Name;
        }
    }
}
