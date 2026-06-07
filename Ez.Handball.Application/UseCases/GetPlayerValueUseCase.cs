using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.ValueFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerValueResult
{
    public sealed record NotFound : GetPlayerValueResult;
    public sealed record InvalidFlavor : GetPlayerValueResult;
    public sealed record RuleSetNotFound : GetPlayerValueResult;
    public sealed record Found(PlayerValue Value) : GetPlayerValueResult;
}

public interface IGetPlayerValueUseCase
{
    Task<GetPlayerValueResult> ExecuteAsync(
        string playerId, ValueFlavor flavor, PlayerValueContext context, CancellationToken ct);
}

public class GetPlayerValueUseCase : IGetPlayerValueUseCase
{
    private readonly IReadOnlyDictionary<ValueFlavor, IPlayerValueFunction> _functions;
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsRepository _stats;
    private readonly ISeasonRepository _seasons;
    private readonly IScoringRuleSetRepository _ruleSets;
    private readonly ITournamentScopeResolver _scope;

    public GetPlayerValueUseCase(
        IEnumerable<IPlayerValueFunction> functions,
        IPlayerRepository players,
        IPlayerStatsRepository stats,
        ISeasonRepository seasons,
        IScoringRuleSetRepository ruleSets,
        ITournamentScopeResolver scope)
    {
        _functions = functions.ToDictionary(f => f.Flavor);
        _players = players;
        _stats = stats;
        _seasons = seasons;
        _ruleSets = ruleSets;
        _scope = scope;
    }

    public async Task<GetPlayerValueResult> ExecuteAsync(
        string playerId, ValueFlavor flavor, PlayerValueContext context, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerValueResult.NotFound();

        if (!_functions.TryGetValue(flavor, out var function))
            return new GetPlayerValueResult.InvalidFlavor();

        var season = await ResolveSeasonAsync(context.Season, ct);
        var stats = await AggregateAsync(playerId, season, context, ct);

        ScoringRuleSet? ruleSet = null;
        if (function.DefaultRuleSetVersion is int defaultVersion)
        {
            var version = context.RuleSetVersion ?? defaultVersion;
            ruleSet = await _ruleSets.GetAsync(flavor, version, ct);
            if (ruleSet is null) return new GetPlayerValueResult.RuleSetNotFound();
        }

        var value = function.Compute(new PlayerValueInputs(playerId, stats, ruleSet, context));
        return new GetPlayerValueResult.Found(value);
    }

    private async Task<string?> ResolveSeasonAsync(string? season, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(season)) return season;
        var all = await _seasons.ListAsync(ct);
        return all.FirstOrDefault(s => s.IsCurrent)?.Label;
    }

    private async Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, PlayerValueContext context, CancellationToken ct)
    {
        if (season is null) return new AggregatedStats(0, 0, 0, 0, 0);

        var ids = await _scope.ResolveTournamentIdsAsync(
            season, context.TournamentId, context.CompetitionId, context.Type, ct);

        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == season);
        if (ids is not null)
            scoped = scoped.Where(r => ids.Contains(r.TournamentId));

        var list = scoped.ToList();
        return new AggregatedStats(
            Games: list.Count,
            Goals: list.Sum(r => r.Goals),
            YellowCards: list.Sum(r => r.YellowCards),
            TwoMinuteSuspensions: list.Sum(r => r.TwoMinuteSuspensions),
            RedCards: list.Sum(r => r.RedCards));
    }
}
