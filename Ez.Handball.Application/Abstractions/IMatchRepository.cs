using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMatchRepository
{
    Task<MatchDetail?> GetByIdAsync(string matchId, CancellationToken ct);
}
