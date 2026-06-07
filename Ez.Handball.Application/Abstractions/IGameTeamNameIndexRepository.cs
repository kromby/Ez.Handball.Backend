namespace Ez.Handball.Application.Abstractions;

public interface IGameTeamNameIndexRepository
{
    // Insert-or-conflict. Returns false if the normalized name is already taken.
    Task<bool> TryReserveAsync(string normalizedName, string teamId, CancellationToken ct);

    // Free a previously-reserved name (used on rename). No-op if the name is not present.
    Task ReleaseAsync(string normalizedName, CancellationToken ct);
}
