using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerRepository : IPlayerRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;
    private readonly Func<DateOnly> _today;
    private readonly ILogger<TablePlayerRepository> _logger;

    public TablePlayerRepository(
        TableServiceClient client,
        ITableQuery query,
        Func<DateOnly> today,
        ILogger<TablePlayerRepository> logger)
    {
        _client = client;
        _query = query;
        _today = today;
        _logger = logger;
    }

    public async Task<Player?> GetByIdAsync(string playerId, CancellationToken ct)
    {
        PlayerEntity? row = null;
        await foreach (var r in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, $"RowKey eq '{ODataFilter.Escape(playerId)}'", ct))
        {
            row = r;
            break;
        }

        return row is null ? null : ToPlayer(row, _today());
    }

    public async Task<IReadOnlyList<Player>> ListByClubAsync(string clubId, CancellationToken ct)
    {
        var players = new List<Player>();
        if (string.IsNullOrWhiteSpace(clubId)) return players;

        var today = _today();
        await foreach (var r in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, $"ClubId eq '{ODataFilter.Escape(clubId)}'", ct))
        {
            if (r.Retired == true) continue;
            players.Add(ToPlayer(r, today));
        }

        return players;
    }

    public async Task<IReadOnlyList<Player>> ListMissingPositionAsync(CancellationToken ct)
    {
        var today = _today();
        var players = new List<Player>();
        await foreach (var r in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, "(Position eq '') or (Position eq 'Leikmaður')", ct))
        {
            if (r.Retired == true) continue;
            players.Add(ToPlayer(r, today));
        }

        return players;
    }

    public async Task<bool> SetPositionAsync(string playerId, string position, string positionSecondary, CancellationToken ct)
    {
        PlayerEntity? row = null;
        await foreach (var r in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, $"RowKey eq '{ODataFilter.Escape(playerId)}'", ct))
        {
            row = r;
            break;
        }

        if (row is null) return false;

        row.Position = position;
        row.PositionSecondary = positionSecondary;
        var table = _client.GetTableClient(Tables.Players);
        await table.UpsertEntityAsync(row, TableUpdateMode.Merge, ct);
        return true;
    }

    private static Player ToPlayer(PlayerEntity row, DateOnly today)
    {
        DateOnly? dob = row.DateOfBirth is null
            ? null
            : DateOnly.FromDateTime(row.DateOfBirth.Value.UtcDateTime);
        int? age = dob is null ? null : ComputeAge(dob.Value, today);

        return new Player(
            PlayerId: row.RowKey,
            Name: row.Name,
            JerseyNumber: row.JerseyNumber,
            DateOfBirth: dob,
            Age: age,
            TeamId: row.PartitionKey,
            ClubId: row.ClubId,
            ClubName: row.ClubName,
            Gender: row.Gender,
            Position: row.Position,
            Retired: row.Retired == true,
            PositionSecondary: row.PositionSecondary ?? string.Empty);
    }

    private static int ComputeAge(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return age;
    }
}
