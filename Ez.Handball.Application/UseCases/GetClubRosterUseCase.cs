using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetClubRosterResult
{
    public sealed record NotFound : GetClubRosterResult;
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
    private readonly ITournamentScopeResolver _scope;

    public GetClubRosterUseCase(
        IClubRepository clubs, IPlayerRepository players, ITournamentScopeResolver scope)
    {
        _clubs = clubs;
        _players = players;
        _scope = scope;
    }

    public async Task<GetClubRosterResult> ExecuteAsync(string clubId, CancellationToken ct)
    {
        if (!await _clubs.ExistsAsync(clubId, ct))
            return new GetClubRosterResult.NotFound();

        var season = await _scope.ResolveSeasonLabelAsync(null, ct);
        var players = await _players.ListByClubAsync(clubId, ct);

        var roster = players
            .OrderBy(p => JerseyOrder(p.JerseyNumber))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ClubRosterPlayer(p.PlayerId, p.Name, p.JerseyNumber, p.Position, p.Age))
            .ToList();

        return new GetClubRosterResult.Found(new ClubRoster(clubId, season, roster));
    }

    // Numeric jerseys ascending; blank/non-numeric sort last.
    private static int JerseyOrder(string? jersey) =>
        int.TryParse(jersey, out var n) ? n : int.MaxValue;
}
