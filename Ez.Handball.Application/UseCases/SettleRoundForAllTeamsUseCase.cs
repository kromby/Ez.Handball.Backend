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
    private readonly IGameTeamRepository _teams;
    private readonly ISettleGameweekUseCase _settle;

    public SettleRoundForAllTeamsUseCase(IGameTeamRepository teams, ISettleGameweekUseCase settle)
    {
        _teams = teams;
        _settle = settle;
    }

    public async Task<SettleRoundForAllTeamsResult> ExecuteAsync(
        string roundLabel, int? configVersion, CancellationToken ct)
    {
        // Every fantasy team, not just those with a saved lineup: without a lineup editor in the Web,
        // lineup rows are rare, and a team without one still plays its default lineup (#142).
        // Malformed ids (e.g. a bare ":fantasy" that would slice to an empty userId) are excluded.
        var teams = (await _teams.ListByFlavorAsync(GameFlavor.Fantasy, ct))
            .Select(t => (TeamId: t.TeamId, UserId: GameTeamId.UserIdOf(t.TeamId, GameFlavor.Fantasy)))
            .Where(t => t.UserId is not null)
            .ToList();

        int settled = 0, notReady = 0, skipped = 0;
        foreach (var (teamId, userId) in teams)
        {
            var r = await _settle.ExecuteAsync(userId!, teamId, roundLabel, configVersion, ct);
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
            new SettleRoundReport(roundLabel, teams.Count, settled, notReady, skipped));
    }
}
