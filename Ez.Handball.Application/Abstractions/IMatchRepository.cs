using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMatchRepository
{
    // Match metadata, both team headers, and line scores — without player lines.
    Task<MatchInfo?> GetByIdAsync(string matchId, CancellationToken ct);

    // All matches for a tournament, with club name/logo joined — for the round listing.
    // Returns null when the tournament id is not in the Tournaments table.
    Task<TournamentMatches?> ListByTournamentAsync(string tournamentId, CancellationToken ct);
}
