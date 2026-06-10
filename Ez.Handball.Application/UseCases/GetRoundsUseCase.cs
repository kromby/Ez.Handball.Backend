using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetRoundsResult
{
    public sealed record NotFound : GetRoundsResult;
    public sealed record Found(RoundListing Listing) : GetRoundsResult;
}

public interface IGetRoundsUseCase
{
    Task<GetRoundsResult> ExecuteAsync(string tournamentId, CancellationToken ct);
}

public sealed class GetRoundsUseCase : IGetRoundsUseCase
{
    private readonly IMatchRepository _matches;

    public GetRoundsUseCase(IMatchRepository matches) => _matches = matches;

    public async Task<GetRoundsResult> ExecuteAsync(string tournamentId, CancellationToken ct)
    {
        var data = await _matches.ListByTournamentAsync(tournamentId, ct);
        if (data is null) return new GetRoundsResult.NotFound();

        var rounds = data.Matches
            .GroupBy(m => m.Round)
            .Select(BuildRound)
            // Numeric rounds ascending; non-numeric labels last, then by ordinal label.
            .OrderBy(r => RoundSortKey(r.Round).Bucket)
            .ThenBy(r => RoundSortKey(r.Round).Value)
            .ThenBy(r => r.Round, StringComparer.Ordinal)
            .ToList();

        return new GetRoundsResult.Found(
            new RoundListing(data.TournamentId, data.TournamentName, data.Season, rounds));
    }

    private static RoundGroup BuildRound(IGrouping<string, MatchListItem> group)
    {
        var ordered = group.OrderBy(m => m.Date).ToList();
        var days = ordered.Select(m => DateOnly.FromDateTime(m.Date.UtcDateTime.Date)).ToList();
        return new RoundGroup(
            Round: group.Key,
            StartDate: days.Min(),
            EndDate: days.Max(),
            Matches: ordered.Select(ToRoundMatch).ToList());
    }

    private static RoundMatch ToRoundMatch(MatchListItem m)
    {
        var played = m.Status == "S";
        return new RoundMatch(
            MatchId: m.MatchId,
            Played: played,
            Date: m.Date,
            Venue: m.Venue,
            Home: ToRoundTeam(m.Home, played),
            Away: ToRoundTeam(m.Away, played));
    }

    private static RoundTeam ToRoundTeam(MatchListTeam t, bool played) =>
        new(t.TeamId, t.ClubId, t.ClubName, t.LogoSrc, played ? t.Score : null);

    // Numeric rounds first (bucket 0, ordered by value); non-numeric labels last (bucket 1).
    private static (int Bucket, int Value) RoundSortKey(string round) =>
        int.TryParse(round, out var n) ? (0, n) : (1, 0);
}
