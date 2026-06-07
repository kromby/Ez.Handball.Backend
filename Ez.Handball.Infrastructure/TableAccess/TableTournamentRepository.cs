using System.Globalization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableTournamentRepository : ITournamentRepository
{
    private readonly ITableQuery _query;

    public TableTournamentRepository(ITableQuery query) => _query = query;

    public Task<IReadOnlyList<Tournament>> ListActiveBySeasonAsync(string season, CancellationToken ct) =>
        QueryAsync($"PartitionKey eq '{ODataFilter.Escape(season)}' and Active eq true", ct);

    public Task<IReadOnlyList<Tournament>> ListBySeasonAsync(string season, CancellationToken ct) =>
        QueryAsync($"PartitionKey eq '{ODataFilter.Escape(season)}'", ct);

    private async Task<IReadOnlyList<Tournament>> QueryAsync(string filter, CancellationToken ct)
    {
        var rows = new List<TournamentEntity>();
        await foreach (var t in _query.QueryAsync<TournamentEntity>(Tables.Tournaments, filter, ct))
            rows.Add(t);

        // Priority is the deliberate display order (Olís=10 < Grill 66=30 < bikar=50);
        // tie-break on the Icelandic-collated name so Þ/Æ/Ö sort correctly.
        var nameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("is-IS"), ignoreCase: true);
        return rows
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.Name, nameComparer)
            .Select(Map)
            .ToList();
    }

    private static Tournament Map(TournamentEntity t)
    {
        // Defensive: rows seeded before this field existed default to League.
        var type = TournamentTypes.TryParse(t.Type, out var parsed) ? parsed : TournamentType.League;
        return new Tournament(t.RowKey, t.Name, t.Gender, type, t.CompetitionId, t.CompetitionName);
    }
}
