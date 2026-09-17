using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetShortlistUseCase
{
    Task<ShortlistView> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetShortlistUseCase : IGetShortlistUseCase
{
    private readonly IShortlistRepository _shortlist;
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsAggregator _stats;
    private readonly ShortlistSettings _settings;

    public GetShortlistUseCase(
        IShortlistRepository shortlist, IPlayerRepository players,
        IPlayerStatsAggregator stats, ShortlistSettings settings)
    {
        _shortlist = shortlist;
        _players = players;
        _stats = stats;
        _settings = settings;
    }

    public async Task<ShortlistView> ExecuteAsync(string userId, CancellationToken ct)
    {
        var entries = await _shortlist.ListActiveAsync(userId, ct);
        var items = new List<ShortlistPlayer>(entries.Count);
        foreach (var entry in entries)
        {
            var player = await _players.GetByIdAsync(entry.PlayerId, ct);
            // Current season, whole-scope aggregate — same call FantasyPricing uses for the
            // single-player path. Only fetched when the player resolves, matching the rest
            // of this record's "null when unresolved" convention.
            var stats = player is null
                ? null
                : await _stats.AggregateAsync(entry.PlayerId, null, null, null, null, ct);

            items.Add(new ShortlistPlayer(
                PlayerId: entry.PlayerId,
                Name: player?.Name,
                ClubId: player?.ClubId,
                ClubName: player?.ClubName,
                Position: player?.Position,
                Gender: player?.Gender,
                Price: null,           // reserved — future pricing system
                PickPercentage: null,  // reserved — #25
                CreatedAt: entry.CreatedAt,
                PositionSecondary: player?.PositionSecondary,
                Games: stats?.Games,
                Goals: stats?.Goals,
                YellowCards: stats?.YellowCards,
                TwoMinuteSuspensions: stats?.TwoMinuteSuspensions,
                RedCards: stats?.RedCards,
                Assists: stats?.Assists,
                Steals: stats?.Steals,
                Blocks: stats?.Blocks,
                Saves: stats?.Saves,
                Turnovers: stats?.Turnovers,
                LegalStops: stats?.LegalStops,
                Shots: stats?.Shots,
                ExpectedGoals: stats?.ExpectedGoals,
                ShotsFaced: stats?.ShotsFaced,
                SavePct: stats?.SavePct,
                ExpectedSaves: stats?.ExpectedSaves,
                GradeTotal: stats?.GradeTotal,
                GradeOffense: stats?.GradeOffense,
                GradeDefense: stats?.GradeDefense,
                GradeGoalkeeping: stats?.GradeGoalkeeping));
        }
        return new ShortlistView(items, items.Count, _settings.MaxSize);
    }
}
