using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameweekScoreRepository
{
    // Replace-mode upsert → idempotent/recomputable settlement.
    Task SaveAsync(GameweekScore score, CancellationToken ct);
    Task<IReadOnlyList<GameweekScore>> ListByTeamAsync(string teamId, CancellationToken ct);
}
