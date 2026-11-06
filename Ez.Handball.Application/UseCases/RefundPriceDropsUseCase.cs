using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record RefundPriceDropsReport(
    int TeamsScanned,
    int PlayersRebased,
    double TotalRefunded,
    IReadOnlyList<string> Failures);   // "teamId/playerId: what happened and what to do", one per row that didn't fully land

public abstract record RefundPriceDropsResult
{
    public sealed record Done(RefundPriceDropsReport Report) : RefundPriceDropsResult;
    public sealed record RuleSetNotFound : RefundPriceDropsResult { public static readonly RuleSetNotFound Instance = new(); }
}

public interface IRefundPriceDropsUseCase
{
    Task<RefundPriceDropsResult> ExecuteAsync(bool dryRun, CancellationToken ct);
}

// One-off after a price recalibration: for every owned player whose current price is now below
// what the manager paid, lower the locked PricePaid to the current price and credit the difference
// to the team's balance, so a re-pricing doesn't cost managers money they'd lose on a sale. Rises are
// left alone (the owner keeps the usual sell-on profit). The credit goes straight to the balance, not
// the transfer ledger, so it never counts as a transfer. Idempotent: a second run finds PricePaid
// equal to the current price and does nothing.
public sealed class RefundPriceDropsUseCase : IRefundPriceDropsUseCase
{
    private const int PriceVersion = 1; // the version the squad view and sell path use

    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly IPlayerPriceService _price;
    private readonly Func<DateTimeOffset> _now;

    public RefundPriceDropsUseCase(
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        IPlayerPriceService price, Func<DateTimeOffset> now)
    {
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _price = price;
        _now = now;
    }

    public async Task<RefundPriceDropsResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        var teams = await _teams.ListByFlavorAsync(GameFlavor.Fantasy, ct);

        // Price everything before writing anything, so a missing rule set leaves no half-applied run.
        var drops = new List<(string TeamId, string PlayerId, double Paid, double Current)>();
        var prices = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var team in teams)
        {
            foreach (var entry in await _roster.ListActiveAsync(team.TeamId, ct))
            {
                if (!prices.TryGetValue(entry.PlayerId, out var current))
                {
                    // Default scope (no season/tournament), as the web app's squad and sell calls use.
                    var pricing = await _price.GetPriceAsync(entry.PlayerId, PriceVersion, null, null, ct);
                    if (pricing is null) return RefundPriceDropsResult.RuleSetNotFound.Instance;
                    prices[entry.PlayerId] = current = pricing.Price.Amount;
                }

                if (current < entry.PricePaidAmount)
                    drops.Add((team.TeamId, entry.PlayerId, entry.PricePaidAmount, current));
            }
        }

        if (dryRun)
            return new RefundPriceDropsResult.Done(new RefundPriceDropsReport(
                teams.Count, drops.Count, drops.Sum(d => d.Paid - d.Current), []));

        var rebased = 0;
        double refunded = 0;
        var failures = new List<string>();
        var now = _now();
        // From here on the caller's token is ignored: a cancelled run would lose the report, leaving rows
        // that were rebased but not credited unrecorded. The list is at most a few players per team.
        foreach (var (teamId, playerId, paid, current) in drops)
        {
            // The roster and the balance live in different tables, so the two writes can't share a
            // transaction. Rebase first, so a re-run never credits twice; every row that doesn't fully
            // land is reported with what's owed, and one bad row doesn't stop the rest.
            var row = $"{teamId}/{playerId}";
            var owed = paid - current;

            var wasRebased = false;
            try
            {
                wasRebased = await _roster.TryRebasePricePaidAsync(teamId, playerId, paid, current, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add($"{row}: updating the price paid failed ({ex.Message}); if it now shows {current}, " +
                             $"{owed} ISK is owed, so credit it by hand, otherwise re-run");
                continue;
            }

            if (!wasRebased)
            {
                // Sold, re-bought or otherwise changed since the scan.
                failures.Add($"{row}: price paid not updated (row changed since the scan); safe to re-run");
                continue;
            }

            rebased++;
            try
            {
                if (await _budget.TryCreditAsync(teamId, owed, now, CancellationToken.None))
                    refunded += owed;
                else
                    failures.Add($"{row}: price paid updated but {owed} ISK not credited; credit it by hand");
            }
            catch (Exception ex)
            {
                failures.Add($"{row}: price paid updated but the {owed} ISK credit may not have applied ({ex.Message}); " +
                             "check the balance and credit it by hand if needed");
            }
        }

        return new RefundPriceDropsResult.Done(new RefundPriceDropsReport(teams.Count, rebased, refunded, failures));
    }
}
