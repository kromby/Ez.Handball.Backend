using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameweekScoreRepository
{
    // Replace-mode upsert → idempotent/recomputable settlement.
    Task SaveAsync(GameweekScore score, CancellationToken ct);
    Task<IReadOnlyList<GameweekScore>> ListByTeamAsync(string teamId, CancellationToken ct);

    // All settled scores across every team (slim projection — points only, no breakdown).
    // Used by the global manager leaderboard.
    Task<IReadOnlyList<GameweekScoreSummary>> ListAllSummariesAsync(CancellationToken ct);

    // Settled scores for a known set of teams (slim projection). Empty set → empty result.
    // Used by the mini-league-scoped standings. Intended for bounded team sets (not unbounded scans).
    Task<IReadOnlyList<GameweekScoreSummary>> ListSummariesByTeamsAsync(
        IReadOnlyCollection<string> teamIds, CancellationToken ct);
}
