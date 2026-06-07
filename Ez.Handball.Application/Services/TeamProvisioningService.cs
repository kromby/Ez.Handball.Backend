using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class TeamProvisioningService : ITeamProvisioningService
{
    private const int ConstraintsVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameBudgetRepository _budget;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly Func<DateTimeOffset> _now;

    public TeamProvisioningService(
        IGameTeamRepository teams, IGameBudgetRepository budget,
        ISquadConstraintsRepository constraints, Func<DateTimeOffset> now)
    {
        _teams = teams;
        _budget = budget;
        _constraints = constraints;
        _now = now;
    }

    public async Task ProvisionAsync(string userId, GameFlavor flavor, string teamName, CancellationToken ct)
    {
        if (await _teams.ExistsAsync(userId, flavor, ct)) return;

        var now = _now();
        var constraints = await _constraints.GetAsync(ConstraintsVersion, ct);
        var startingCap = constraints?.StartingCap ?? 0;

        await _teams.CreateAsync(userId, flavor, teamName, string.Empty, now, ct);
        await _budget.CreateAsync(GameTeamId.For(userId, flavor), startingCap, now, ct);
    }
}
