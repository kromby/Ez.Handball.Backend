using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record SettleRoundReport(
    string Round, int TeamsConsidered, int Settled, int NotReady, int Skipped);

public abstract record SettleRoundForAllTeamsResult
{
    public sealed record ConfigMissing : SettleRoundForAllTeamsResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record RoundNotFound : SettleRoundForAllTeamsResult { public static readonly RoundNotFound Instance = new(); }
    public sealed record RuleSetMissing : SettleRoundForAllTeamsResult { public static readonly RuleSetMissing Instance = new(); }
    public sealed record Completed(SettleRoundReport Report) : SettleRoundForAllTeamsResult;
}

public interface ISettleRoundForAllTeamsUseCase
{
    Task<SettleRoundForAllTeamsResult> ExecuteAsync(string roundLabel, int? configVersion, CancellationToken ct);
}

public sealed class SettleRoundForAllTeamsUseCase : ISettleRoundForAllTeamsUseCase
{
    // Matches GameTeamId.For(userId, GameFlavor.Fantasy) == "{userId}:fantasy".
    private static readonly string FantasySuffix = ":" + GameFlavor.Fantasy.ToString().ToLowerInvariant();

    private readonly ILineupRepository _lineups;
    private readonly ISettleGameweekUseCase _settle;

    public SettleRoundForAllTeamsUseCase(ILineupRepository lineups, ISettleGameweekUseCase settle)
    {
        _lineups = lineups;
        _settle = settle;
    }

    public async Task<SettleRoundForAllTeamsResult> ExecuteAsync(
        string roundLabel, int? configVersion, CancellationToken ct)
    {
        // Length guard before the EndsWith so a malformed bare ":fantasy" id (which would slice to an
        // empty userId) is excluded rather than settled — validate at the boundary.
        var teamIds = (await _lineups.ListTeamIdsAsync(ct))
            .Where(t => t.Length > FantasySuffix.Length && t.EndsWith(FantasySuffix, StringComparison.Ordinal))
            .ToList();

        int settled = 0, notReady = 0, skipped = 0;
        foreach (var teamId in teamIds)
        {
            var userId = teamId[..^FantasySuffix.Length];
            var r = await _settle.ExecuteAsync(userId, teamId, roundLabel, configVersion, ct);
            switch (r)
            {
                case SettleGameweekResult.Settled:
                    settled++;
                    break;
                case SettleGameweekResult.NotReady:
                    notReady++;
                    break;
                case SettleGameweekResult.NoSnapshotPossible:
                case SettleGameweekResult.SquadNotFound:
                    skipped++;
                    break;
                // Team-independent failures: the round/config is wrong for everyone — stop and report once.
                case SettleGameweekResult.ConfigMissing:
                    return SettleRoundForAllTeamsResult.ConfigMissing.Instance;
                case SettleGameweekResult.NotFound:
                    return SettleRoundForAllTeamsResult.RoundNotFound.Instance;
                case SettleGameweekResult.RuleSetMissing:
                    return SettleRoundForAllTeamsResult.RuleSetMissing.Instance;
                // SettleGameweekResult is a sealed hierarchy, so the cases above are exhaustive today.
                // Fail loud if a new variant is added without being tallied here, rather than silently
                // counting it in TeamsConsidered but none of settled/notReady/skipped.
                default:
                    throw new InvalidOperationException($"Unhandled settle result: {r.GetType().Name}");
            }
        }

        return new SettleRoundForAllTeamsResult.Completed(
            new SettleRoundReport(roundLabel, teamIds.Count, settled, notReady, skipped));
    }
}
