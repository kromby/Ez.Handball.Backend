using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableSeasonRepository : ISeasonRepository
{
    private readonly ITableQuery _query;

    public TableSeasonRepository(ITableQuery query) => _query = query;

    public async Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct)
    {
        var seasons = new List<(int StartYear, Season Season)>();
        await foreach (var s in _query.QueryAsync<SeasonEntity>(Tables.Seasons, "PartitionKey eq 'season'", ct))
        {
            seasons.Add((s.StartYear, new Season(s.RowKey, s.IsCurrent)));
        }

        // Newest season first. "YYYY-YY" labels also sort this way as strings,
        // but ordering on StartYear keeps the intent explicit.
        return seasons
            .OrderByDescending(s => s.StartYear)
            .Select(s => s.Season)
            .ToList();
    }
}
