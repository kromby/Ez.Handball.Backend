namespace Ez.Handball.Application.Abstractions;

// Pins a gameweek's deadline the first time it is observed as passed, so a later fixture
// reschedule cannot move an already-passed deadline. PartitionKey = tournamentId, RowKey = roundLabel.
public interface IGameweekLockRepository
{
    Task<DateTimeOffset?> GetPinnedDeadlineAsync(string tournamentId, string roundLabel, CancellationToken ct);

    // Idempotent: writing an already-pinned (tournamentId, roundLabel) is a no-op overwrite.
    Task PinAsync(string tournamentId, string roundLabel, DateTimeOffset deadline, DateTimeOffset lockedAt, CancellationToken ct);
}
