using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record CreateMiniLeagueResult
{
    public sealed record Created(MiniLeagueView View) : CreateMiniLeagueResult;
    public sealed record ValidationError(string Field) : CreateMiniLeagueResult;
    public sealed record NoCurrentSeason : CreateMiniLeagueResult;
}

public interface ICreateMiniLeagueUseCase
{
    // startRound: optional round label standings count from; null/blank defaults to the season's first gameweek.
    Task<CreateMiniLeagueResult> ExecuteAsync(string userId, string name, string? startRound, CancellationToken ct);
}

public sealed class CreateMiniLeagueUseCase : ICreateMiniLeagueUseCase
{
    private const int GameweekConfigVersion = 1;

    private readonly IMiniLeagueRepository _leagues;
    private readonly ISeasonRepository _seasons;
    private readonly IGameTeamRepository _teams;
    private readonly IGameweekConfigRepository _gameweekConfig;
    private readonly IGameweekCalendarService _calendar;
    private readonly Func<DateTimeOffset> _now;

    public CreateMiniLeagueUseCase(
        IMiniLeagueRepository leagues, ISeasonRepository seasons, IGameTeamRepository teams,
        IGameweekConfigRepository gameweekConfig, IGameweekCalendarService calendar, Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _seasons = seasons;
        _teams = teams;
        _gameweekConfig = gameweekConfig;
        _calendar = calendar;
        _now = now;
    }

    public async Task<CreateMiniLeagueResult> ExecuteAsync(
        string userId, string name, string? startRound, CancellationToken ct)
    {
        if (!MiniLeagueValidation.IsValidName(name))
            return new CreateMiniLeagueResult.ValidationError("name");
        if (!MiniLeagueValidation.TryNormalizeStartRound(startRound, out var normalizedStartRound))
            return new CreateMiniLeagueResult.ValidationError("startRound");

        var seasons = await _seasons.ListAsync(ct);
        var current = seasons.FirstOrDefault(s => s.IsCurrent);
        if (current is null) return new CreateMiniLeagueResult.NoCurrentSeason();

        normalizedStartRound ??= await FirstRoundAsync(ct);

        var now = _now();
        var league = new MiniLeague(
            Guid.NewGuid().ToString("N"), name.Trim(), current.Label, userId, now, normalizedStartRound);

        // Header first, then the creator membership — two tables, so not transactional.
        // The id is freshly generated per call, so a client retry would mint a new league
        // rather than complete this one; compensate by deleting the header if the member
        // write fails, leaving no orphaned (member-less) league behind.
        await _leagues.CreateAsync(league, ct);

        var creator = new MiniLeagueMember(userId, MiniLeagueRoles.Creator, now);
        try
        {
            await _leagues.AddMemberAsync(league.Id, creator, ct);
        }
        catch
        {
            await _leagues.DeleteAsync(league.Id, CancellationToken.None);
            throw;
        }

        var teamNames = await MiniLeagueMemberTeamNames.ResolveAsync(_teams, new[] { creator }, ct);
        return new CreateMiniLeagueResult.Created(new MiniLeagueView(league, new[] { creator }, teamNames));
    }

    // The season's first gameweek, so a new league's start round is always explicit.
    // Null when the gameweek config or calendar isn't available yet — that still counts every round.
    private async Task<string?> FirstRoundAsync(CancellationToken ct)
    {
        var config = await _gameweekConfig.GetAsync(GameweekConfigVersion, ct);
        if (config is null) return null;
        var calendar = await _calendar.GetCalendarAsync(config, ct);
        return calendar?.MinBy(g => g.Number)?.RoundLabel;
    }
}
