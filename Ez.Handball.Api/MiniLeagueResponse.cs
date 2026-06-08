using Ez.Handball.Domain;

namespace Ez.Handball.Api;

internal static class MiniLeagueResponse
{
    // role is the caller's role in the league, resolved from the members; null if the caller is not a member.
    public static object Body(MiniLeagueView view, string callerUserId) => new
    {
        id            = view.League.Id,
        name          = view.League.Name,
        season        = view.League.Season,
        creatorUserId = view.League.CreatorUserId,
        memberCount   = view.Members.Count,
        role          = view.Members.FirstOrDefault(m => m.UserId == callerUserId)?.Role,
        createdAt     = view.League.CreatedAt,
        members       = view.Members.Select(m => new
        {
            userId   = m.UserId,
            role     = m.Role,
            joinedAt = m.JoinedAt
        })
    };
}
