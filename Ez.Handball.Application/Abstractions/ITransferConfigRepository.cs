using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITransferConfigRepository
{
    // Reads the fantasy-transfers-v{version} Config group; null if the group doesn't exist.
    Task<TransferConfig?> GetAsync(int version, CancellationToken ct);
}
