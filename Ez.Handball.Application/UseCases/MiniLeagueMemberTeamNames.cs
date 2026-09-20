using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// Shared glue for the mini-league use cases: resolve each member's fantasy team name so the
// API response doesn't have to fall back to a placeholder for anyone but the caller.
internal static class MiniLeagueMemberTeamNames
{
    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IGameTeamRepository teams, IReadOnlyList<MiniLeagueMember> members, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();
        foreach (var member in members)
        {
            var team = await teams.GetAsync(member.UserId, GameFlavor.Fantasy, ct);
            if (team is not null) names[member.UserId] = team.Name;
        }
        return names;
    }
}
