namespace Ez.Handball.Infrastructure.BlobAccess;

public sealed record BlobContent(string Text, DateTimeOffset LastModifiedUtc);

public interface IBlobReader
{
    // Null if the blob does not exist.
    Task<BlobContent?> ReadAsync(string path, CancellationToken ct);
}
