using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetClubRosterResult
{
    public sealed record NotFound : GetClubRosterResult;
    public sealed record RuleSetNotFound : GetClubRosterResult;
    public sealed record Found(ClubRoster Roster) : GetClubRosterResult;
}

public interface IGetClubRosterUseCase
{
    Task<GetClubRosterResult> ExecuteAsync(string clubId, CancellationToken ct);
}

public sealed class GetClubRosterUseCase : IGetClubRosterUseCase
{
    private readonly IClubRepository _clubs;
    private readonly IPlayerRepository _players;
    private readonly IGetPlayerPoolUseCase _pool;
    private readonly ITournamentScopeResolver _scope;

    public GetClubRosterUseCase(
        IClubRepository clubs, IPlayerRepository players, IGetPlayerPoolUseCase pool,
        ITournamentScopeResolver scope)
    {
        _clubs = clubs;
        _players = players;
        _pool = pool;
        _scope = scope;
    }

    public async Task<GetClubRosterResult> ExecuteAsync(string clubId, CancellationToken ct)
    {
        if (!await _clubs.ExistsAsync(clubId, ct))
            return new GetClubRosterResult.NotFound();

        // The roster is the club's slice of the current-season player pool — the
        // same list /api/players?clubId= returns. The pool derives the club from
        // this season's PlayerStats and drops retired players, so stale Players
        // rows (old ClubId, unset Retired flag) never leak in.
        var season = await _scope.ResolveSeasonLabelAsync(null, ct);
        var request = new PlayerPoolRequest(
            season, TournamentId: null, CompetitionId: null, Type: null, Gender: null,
            Position: null, Name: null, ClubId: clubId, PlayerPoolSort.Rating, PriceVersion: 1);

        var result = await _pool.ExecuteAsync(request, 0, int.MaxValue, ct);
        if (result is not PlayerPoolResult.Found found)
            return new GetClubRosterResult.RuleSetNotFound();

        // Jersey number and age only live on the Players row.
        var profiles = await Task.WhenAll(
            found.Pool.Entries.Select(e => _players.GetByIdAsync(e.PlayerId, ct)));

        var roster = found.Pool.Entries
            .Zip(profiles, (entry, player) => new ClubRosterPlayer(entry, player?.JerseyNumber, player?.Age))
            .OrderBy(p => JerseyOrder(p.JerseyNumber))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GetClubRosterResult.Found(new ClubRoster(clubId, season, roster));
    }

    // Numeric jerseys ascending; blank/non-numeric sort last.
    private static int JerseyOrder(string? jersey) =>
        int.TryParse(jersey, out var n) ? n : int.MaxValue;
}
