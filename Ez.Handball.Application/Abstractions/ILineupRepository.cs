using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ILineupRepository
{
    // The team's current lineup, or null if never set.
    Task<Lineup?> GetAsync(string teamId, CancellationToken ct);

    // Full replacement: upsert the new slot set and delete rows no longer present.
    Task ReplaceAsync(string teamId, Lineup lineup, CancellationToken ct);

    // Distinct team IDs that currently have a live lineup (one entry per team). Used by the
    // admin settle-all fan-out (#96) to enumerate which teams to settle for a round.
    Task<IReadOnlyList<string>> ListTeamIdsAsync(CancellationToken ct);
}
