using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public enum PlayerPoolSort
{
    Rating,
    Price,
    PickPercentage,
    Goals,
    Games,
    YellowCards,
    TwoMinuteSuspensions,
    RedCards
}

// Edge → use case. Carries the raw, unresolved scope + the chosen sort + the
// price rule-set version (default 1 at the edge).
public sealed record PlayerPoolRequest(
    string? Season,
    string? TournamentId,
    string? CompetitionId,
    TournamentType? Type,
    string? Gender,
    string? Position,
    PlayerPoolSort Sort,
    int PriceVersion);

public abstract record PlayerPoolResult
{
    public sealed record RuleSetNotFound : PlayerPoolResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Found(PlayerPool Pool) : PlayerPoolResult;
}

public interface IGetPlayerPoolUseCase
{
    Task<PlayerPoolResult> ExecuteAsync(
        PlayerPoolRequest request, int offset, int limit, CancellationToken ct);
}

public sealed class GetPlayerPoolUseCase : IGetPlayerPoolUseCase
{
    private readonly IPlayerPoolRepository _repo;
    private readonly ITournamentScopeResolver _scope;
    private readonly IScoringRuleSetRepository _scoring;
    private readonly IPriceRuleSetRepository _prices;
    private readonly FantasyPricing _pricing;

    public GetPlayerPoolUseCase(
        IPlayerPoolRepository repo,
        ITournamentScopeResolver scope,
        IScoringRuleSetRepository scoring,
        IPriceRuleSetRepository prices,
        FantasyPricing pricing)
    {
        _repo = repo;
        _scope = scope;
        _scoring = scoring;
        _prices = prices;
        _pricing = pricing;
    }

    public async Task<PlayerPoolResult> ExecuteAsync(
        PlayerPoolRequest request, int offset, int limit, CancellationToken ct)
    {
        // Load both rule-sets ONCE up front; bail before any per-player work if missing.
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, _pricing.ScoringVersion, ct);
        if (scoring is null) return PlayerPoolResult.RuleSetNotFound.Instance;

        var prices = await _prices.GetAsync(request.PriceVersion, ct);
        if (prices is null) return PlayerPoolResult.RuleSetNotFound.Instance;

        var season = await _scope.ResolveSeasonLabelAsync(request.Season, ct);
        var tournamentIds = await _scope.ResolveTournamentIdsAsync(
            season, request.TournamentId, request.CompetitionId, request.Type, ct);

        var query = new PlayerPoolQuery(season, tournamentIds, request.Gender);
        var players = await _repo.GetAggregatedAsync(query, ct);

        // Context is accepted by the rating function but unused by the fantasy
        // formula; pass the season for completeness.
        var ctx = new PlayerRatingContext(season, null, null, null, null, null);

        var computed = players
            .Where(p => !p.Retired)
            .Where(p => string.IsNullOrWhiteSpace(request.Position)
                || string.Equals(p.Position, request.Position, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var priced = _pricing.Compute(p.PlayerId, p.Stats, scoring, prices, ctx);
                return new PlayerPoolEntry(
                    Rank: 0,
                    PlayerId: p.PlayerId,
                    Name: p.Name,
                    ClubId: p.ClubId,
                    ClubName: p.ClubName,
                    Gender: p.Gender,
                    Position: p.Position,
                    Games: p.Stats.Games,
                    Goals: p.Stats.Goals,
                    YellowCards: p.Stats.YellowCards,
                    TwoMinuteSuspensions: p.Stats.TwoMinuteSuspensions,
                    RedCards: p.Stats.RedCards,
                    AvgGoals: p.Stats.Games > 0
                        ? Math.Round((double)p.Stats.Goals / p.Stats.Games, 2)
                        : 0,
                    Price: priced.Price,
                    Rating: priced.Rating,
                    PickPercentage: null); // deferred — ownership aggregation follow-up
            });

        var sorted = Sort(computed, request.Sort).ToList();

        var ranked = new List<PlayerPoolEntry>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
            ranked.Add(sorted[i] with { Rank = i + 1 });

        var page = ranked.Skip(offset).Take(limit).ToList();
        var pool = new PlayerPool(request.Sort.ToString(), ranked.Count, offset, limit, page);
        return new PlayerPoolResult.Found(pool);
    }

    // Stable tie-break: chosen metric desc, then rating desc, then playerId
    // ordinal. sort=PickPercentage is accepted but every value is null, so it
    // falls through to the rating tie-break.
    private static IEnumerable<PlayerPoolEntry> Sort(IEnumerable<PlayerPoolEntry> entries, PlayerPoolSort sort) =>
        sort switch
        {
            PlayerPoolSort.Price => ByMetricThenRating(entries, e => e.Price.Amount),
            PlayerPoolSort.Goals => ByMetricThenRating(entries, e => e.Goals),
            PlayerPoolSort.Games => ByMetricThenRating(entries, e => e.Games),
            PlayerPoolSort.YellowCards => ByMetricThenRating(entries, e => e.YellowCards),
            PlayerPoolSort.TwoMinuteSuspensions => ByMetricThenRating(entries, e => e.TwoMinuteSuspensions),
            PlayerPoolSort.RedCards => ByMetricThenRating(entries, e => e.RedCards),
            PlayerPoolSort.PickPercentage => entries
                .OrderByDescending(e => e.PickPercentage ?? double.NegativeInfinity)
                .ThenByDescending(e => e.Rating)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal),
            _ => entries
                .OrderByDescending(e => e.Rating)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal),
        };

    private static IEnumerable<PlayerPoolEntry> ByMetricThenRating(
        IEnumerable<PlayerPoolEntry> entries, Func<PlayerPoolEntry, double> metric) =>
        entries
            .OrderByDescending(metric)
            .ThenByDescending(e => e.Rating)
            .ThenBy(e => e.PlayerId, StringComparer.Ordinal);
}
