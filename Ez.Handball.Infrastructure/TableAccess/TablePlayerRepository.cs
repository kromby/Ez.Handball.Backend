using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerRepository : IPlayerRepository
{
    private readonly ITableQuery _query;
    private readonly Func<DateOnly> _today;
    private readonly ILogger<TablePlayerRepository> _logger;

    public TablePlayerRepository(
        ITableQuery query,
        Func<DateOnly> today,
        ILogger<TablePlayerRepository> logger)
    {
        _query = query;
        _today = today;
        _logger = logger;
    }

    public async Task<Player?> GetByIdAsync(string playerId, CancellationToken ct)
    {
        PlayerEntity? row = null;
        await foreach (var r in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, $"RowKey eq '{playerId}'", ct))
        {
            row = r;
            break;
        }

        if (row is null) return null;

        DateOnly? dob = row.DateOfBirth is null
            ? null
            : DateOnly.FromDateTime(row.DateOfBirth.Value.UtcDateTime);
        int? age = dob is null ? null : ComputeAge(dob.Value, _today());

        return new Player(
            PlayerId: row.RowKey,
            Name: row.Name,
            JerseyNumber: row.JerseyNumber,
            DateOfBirth: dob,
            Age: age,
            TeamId: row.PartitionKey,
            ClubId: row.ClubId,
            ClubName: row.ClubName,
            Gender: row.Gender);
    }

    private static int ComputeAge(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return age;
    }
}
