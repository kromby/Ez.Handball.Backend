using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// Frozen per-(team, gameweek) lineup snapshot. The gwKey is the round label.
public interface IGameweekLineupRepository
{
    Task<Lineup?> GetSnapshotAsync(string teamId, string roundLabel, CancellationToken ct);
    Task SaveSnapshotAsync(string teamId, string roundLabel, Lineup lineup, CancellationToken ct);
}
