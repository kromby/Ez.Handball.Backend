using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public enum ClubMatchStatusFilter
{
    Played,
    Upcoming
}

public abstract record GetClubMatchesResult
{
    public sealed record NotFound : GetClubMatchesResult;
    public sealed record Found(ClubMatchListing Listing) : GetClubMatchesResult;
}

public interface IGetClubMatchesUseCase
{
    Task<GetClubMatchesResult> ExecuteAsync(
        string clubId, ClubMatchStatusFilter? status, CancellationToken ct);
}

public sealed class GetClubMatchesUseCase : IGetClubMatchesUseCase
{
    private readonly IClubRepository _clubs;
    private readonly ITournamentScopeResolver _scope;
    private readonly ITournamentRepository _tournaments;
    private readonly IMatchRepository _matches;

    public GetClubMatchesUseCase(
        IClubRepository clubs,
        ITournamentScopeResolver scope,
        ITournamentRepository tournaments,
        IMatchRepository matches)
    {
        _clubs = clubs;
        _scope = scope;
        _tournaments = tournaments;
        _matches = matches;
    }

    public async Task<GetClubMatchesResult> ExecuteAsync(
        string clubId, ClubMatchStatusFilter? status, CancellationToken ct)
    {
        if (!await _clubs.ExistsAsync(clubId, ct))
            return new GetClubMatchesResult.NotFound();

        var season = await _scope.ResolveSeasonLabelAsync(null, ct);
        if (string.IsNullOrWhiteSpace(season))
            return new GetClubMatchesResult.Found(new ClubMatchListing(clubId, season, []));

        var tournaments = await _tournaments.ListActiveBySeasonAsync(season, ct);

        var clubMatches = new List<ClubMatch>();
        foreach (var t in tournaments)
        {
            var data = await _matches.ListByTournamentAsync(t.TournamentId, ct);
            if (data is null) continue;

            foreach (var m in data.Matches)
            {
                var isHome = m.Home.ClubId == clubId;
                var isAway = m.Away.ClubId == clubId;
                if (!isHome && !isAway) continue;

                clubMatches.Add(ToClubMatch(m, data.TournamentId, data.TournamentName, isHome));
            }
        }

        // Played newest-first, upcoming soonest-first; unfiltered => played block then upcoming.
        var played = clubMatches
            .Where(IsPlayed)
            .OrderByDescending(m => m.Date);

        var upcoming = clubMatches
            .Where(m => !IsPlayed(m))
            .OrderBy(m => m.Date);

        var ordered = status switch
        {
            ClubMatchStatusFilter.Played   => played.ToList(),
            ClubMatchStatusFilter.Upcoming => upcoming.ToList(),
            _                              => played.Concat(upcoming).ToList()
        };

        return new GetClubMatchesResult.Found(new ClubMatchListing(clubId, season, ordered));
    }

    private static bool IsPlayed(ClubMatch m) => m.Status == "played";

    private static ClubMatch ToClubMatch(
        MatchListItem m, string tournamentId, string? tournamentName, bool isHome)
    {
        var played = m.Status == "S";
        var opponent = isHome ? m.Away : m.Home;
        int? clubScore = played ? (isHome ? m.Home.Score : m.Away.Score) : null;
        int? oppScore = played ? (isHome ? m.Away.Score : m.Home.Score) : null;

        return new ClubMatch(
            MatchId: m.MatchId,
            TournamentId: tournamentId,
            TournamentName: tournamentName,
            Round: m.Round,
            Date: m.Date,
            Venue: m.Venue,
            Status: played ? "played" : "upcoming",
            IsHome: isHome,
            OpponentClubId: opponent.ClubId,
            OpponentName: opponent.ClubName,
            OpponentLogoUrl: opponent.LogoSrc,
            ClubScore: clubScore,
            OpponentScore: oppScore);
    }
}
