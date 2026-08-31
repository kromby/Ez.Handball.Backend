using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMatchScheduleRepository
{
    // Reads the archived raw hsi.is match list for a tournament (written by the last
    // successful /api/sync). Null if the tournament has never been synced.
    Task<MatchSchedule?> GetAsync(string tournamentId, CancellationToken ct);
}
