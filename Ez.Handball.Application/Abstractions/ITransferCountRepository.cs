using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITransferCountRepository
{
    // Adds one buy or sell to the team's count for the round the transfer applies to.
    Task IncrementAsync(string teamId, string roundLabel, TransferType type, DateTimeOffset now, CancellationToken ct);

    // Every round the team has transferred in (single-partition read).
    Task<IReadOnlyList<RoundTransferCount>> ListAsync(string teamId, CancellationToken ct);
}
