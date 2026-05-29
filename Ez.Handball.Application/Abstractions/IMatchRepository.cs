using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMatchRepository
{
    // Match metadata, both team headers, and line scores — without player lines.
    Task<MatchInfo?> GetByIdAsync(string matchId, CancellationToken ct);
}
