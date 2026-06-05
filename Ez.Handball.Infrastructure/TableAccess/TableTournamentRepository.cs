using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableTournamentRepository : ITournamentRepository
{
    private readonly ITableQuery _query;

    public TableTournamentRepository(ITableQuery query) => _query = query;

    public async Task<IReadOnlyList<Tournament>> ListEnabledBySeasonAsync(string season, CancellationToken ct)
    {
        var filter = $"PartitionKey eq '{ODataFilter.Escape(season)}' and Enabled eq true";

        var rows = new List<TournamentEntity>();
        await foreach (var t in _query.QueryAsync<TournamentEntity>(Tables.Tournaments, filter, ct))
            rows.Add(t);

        // Priority is the deliberate display order (Olís=10 < Grill 66=30 < bikar=50);
        // tie-break on the Icelandic-collated name so Þ/Æ/Ö sort correctly.
        var nameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("is-IS"), ignoreCase: true);
        return rows
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.Name, nameComparer)
            .Select(t => new Tournament(t.RowKey, t.Name, t.Gender))
            .ToList();
    }
}
