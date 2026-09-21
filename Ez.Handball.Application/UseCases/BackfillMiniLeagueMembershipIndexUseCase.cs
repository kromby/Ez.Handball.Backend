using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public sealed record BackfillMiniLeagueMembershipIndexResult(int MembersScanned, int Written);

public interface IBackfillMiniLeagueMembershipIndexUseCase
{
    Task<BackfillMiniLeagueMembershipIndexResult> ExecuteAsync(bool dryRun, CancellationToken ct);
}

// One-off backfill for MiniLeagueMembersByUser: replays every existing MiniLeagueMembers row through
// AddMemberAsync, which now also writes the reverse-index row. Idempotent (Replace upserts), safe to re-run.
public sealed class BackfillMiniLeagueMembershipIndexUseCase : IBackfillMiniLeagueMembershipIndexUseCase
{
    private readonly IMiniLeagueRepository _leagues;

    public BackfillMiniLeagueMembershipIndexUseCase(IMiniLeagueRepository leagues) => _leagues = leagues;

    public async Task<BackfillMiniLeagueMembershipIndexResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        var rows = await _leagues.GetAllMembersAsync(ct);
        if (dryRun) return new BackfillMiniLeagueMembershipIndexResult(rows.Count, 0);

        foreach (var row in rows)
            await _leagues.AddMemberAsync(row.LeagueId, row.Member, ct);

        return new BackfillMiniLeagueMembershipIndexResult(rows.Count, rows.Count);
    }
}
