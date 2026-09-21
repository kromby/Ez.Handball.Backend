using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record MyMiniLeagueSummary(MiniLeague League, string Role, int MemberCount);

public interface IGetMyMiniLeaguesUseCase
{
    Task<IReadOnlyList<MyMiniLeagueSummary>> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetMyMiniLeaguesUseCase : IGetMyMiniLeaguesUseCase
{
    private readonly IMiniLeagueRepository _leagues;

    public GetMyMiniLeaguesUseCase(IMiniLeagueRepository leagues) => _leagues = leagues;

    public async Task<IReadOnlyList<MyMiniLeagueSummary>> ExecuteAsync(string userId, CancellationToken ct)
    {
        var memberships = await _leagues.GetLeaguesForUserAsync(userId, ct);
        var result = new List<MyMiniLeagueSummary>(memberships.Count);
        foreach (var membership in memberships)
        {
            var league = await _leagues.GetAsync(membership.LeagueId, ct);
            if (league is null) continue; // reverse-index row outlived a deleted league

            var members = await _leagues.GetMembersAsync(membership.LeagueId, ct);
            result.Add(new MyMiniLeagueSummary(league, membership.Role, members.Count));
        }
        return result;
    }
}
