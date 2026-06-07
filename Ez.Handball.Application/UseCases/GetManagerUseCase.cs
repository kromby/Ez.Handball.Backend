using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record OnboardingView(bool SquadComplete, int PlayersOwned, int SquadSize);

public sealed record ManagerView(
    string TeamName,
    string FavoriteClubId,
    string Color,
    OnboardingView Onboarding);

public abstract record GetManagerResult
{
    public sealed record NoTeam : GetManagerResult { public static readonly NoTeam Instance = new(); }
    public sealed record RuleSetNotFound : GetManagerResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Found(ManagerView View) : GetManagerResult;
}

public interface IGetManagerUseCase
{
    Task<GetManagerResult> ExecuteAsync(string userId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetManagerUseCase : IGetManagerUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IUserRepository _users;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly ISquadRepository _squad;

    public GetManagerUseCase(
        IGameTeamRepository teams, IUserRepository users,
        ISquadConstraintsRepository constraints, ISquadRepository squad)
    {
        _teams = teams;
        _users = users;
        _constraints = constraints;
        _squad = squad;
    }

    public async Task<GetManagerResult> ExecuteAsync(string userId, int? ruleSetVersion, CancellationToken ct)
    {
        var team = await _teams.GetAsync(userId, GameFlavor.Fantasy, ct);
        if (team is null) return GetManagerResult.NoTeam.Instance;

        var version = ruleSetVersion ?? DefaultVersion;
        var constraints = await _constraints.GetAsync(version, ct);
        if (constraints is null) return GetManagerResult.RuleSetNotFound.Instance;

        var user = await _users.GetByIdAsync(userId, ct);
        var favoriteClubId = user?.FavoriteClubId ?? string.Empty;

        var squad = await _squad.GetAsync(userId, GameFlavor.Fantasy, ct);
        var ownedPlayers = squad.Players.Count;
        var squadComplete = ownedPlayers == constraints.MaxSquadSize;

        return new GetManagerResult.Found(new ManagerView(
            TeamName: team.Name,
            FavoriteClubId: favoriteClubId,
            Color: team.Color,
            Onboarding: new OnboardingView(squadComplete, ownedPlayers, constraints.MaxSquadSize)));
    }
}
