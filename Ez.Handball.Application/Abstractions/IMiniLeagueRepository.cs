using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMiniLeagueRepository
{
    // Write the league header row.
    Task CreateAsync(MiniLeague league, CancellationToken ct);

    // Delete the league header row. Idempotent — a missing row is not an error.
    // Used to compensate a header write when the follow-up member write fails.
    Task DeleteAsync(string leagueId, CancellationToken ct);

    // Write a single membership row (PartitionKey = leagueId, RowKey = member.UserId),
    // and the matching reverse-index row (PartitionKey = member.UserId, RowKey = leagueId).
    Task AddMemberAsync(string leagueId, MiniLeagueMember member, CancellationToken ct);

    // Point read of the header; null if no league with that id exists.
    Task<MiniLeague?> GetAsync(string leagueId, CancellationToken ct);

    // All membership rows for a league.
    Task<IReadOnlyList<MiniLeagueMember>> GetMembersAsync(string leagueId, CancellationToken ct);

    // All leagues a user belongs to, from the reverse-index table.
    Task<IReadOnlyList<MiniLeagueMembership>> GetLeaguesForUserAsync(string userId, CancellationToken ct);
}
