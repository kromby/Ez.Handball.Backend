using Ez.Handball.Application.Abstractions;
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
    Task<CreateMiniLeagueResult> ExecuteAsync(string userId, string name, CancellationToken ct);
}

public sealed class CreateMiniLeagueUseCase : ICreateMiniLeagueUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly ISeasonRepository _seasons;
    private readonly Func<DateTimeOffset> _now;

    public CreateMiniLeagueUseCase(
        IMiniLeagueRepository leagues, ISeasonRepository seasons, Func<DateTimeOffset> now)
    {
        _leagues = leagues;
        _seasons = seasons;
        _now = now;
    }

    public async Task<CreateMiniLeagueResult> ExecuteAsync(string userId, string name, CancellationToken ct)
    {
        if (!MiniLeagueValidation.IsValidName(name))
            return new CreateMiniLeagueResult.ValidationError("name");

        var seasons = await _seasons.ListAsync(ct);
        var current = seasons.FirstOrDefault(s => s.IsCurrent);
        if (current is null) return new CreateMiniLeagueResult.NoCurrentSeason();

        var now = _now();
        var league = new MiniLeague(
            Guid.NewGuid().ToString("N"), name.Trim(), current.Label, userId, now);

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

        return new CreateMiniLeagueResult.Created(new MiniLeagueView(league, new[] { creator }));
    }
}
