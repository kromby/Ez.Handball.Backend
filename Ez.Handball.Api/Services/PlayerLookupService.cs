using Ez.Handball.Api.Models;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Api.Services;

public class PlayerLookupService : IPlayerLookupService
{
    private readonly ITableQuery _query;
    private readonly Func<DateOnly> _today;
    private readonly ILogger<PlayerLookupService> _logger;

    public PlayerLookupService(
        ITableQuery query,
        Func<DateOnly> today,
        ILogger<PlayerLookupService> logger)
    {
        _query = query;
        _today = today;
        _logger = logger;
    }

    public async Task<PlayerProfile?> GetPlayerAsync(string playerId, CancellationToken ct = default)
    {
        PlayerEntity? row = null;
        await foreach (var candidate in _query.QueryAsync<PlayerEntity>(
                           Tables.Players, $"RowKey eq '{playerId}'", ct))
        {
            if (row is null || candidate.Timestamp > row.Timestamp)
                row = candidate;
        }

        if (row is null) return null;

        var dateOfBirth = row.DateOfBirth;
        int? age = null;
        if (dateOfBirth is { } dob)
        {
            age = CalculateAge(DateOnly.FromDateTime(dob.UtcDateTime), _today());
        }

        return new PlayerProfile(
            PlayerId: row.RowKey,
            Name: row.Name,
            JerseyNumber: row.JerseyNumber,
            DateOfBirth: dateOfBirth,
            Age: age,
            TeamId: row.PartitionKey,
            ClubId: row.ClubId,
            ClubName: row.ClubName,
            Gender: row.Gender);
    }

    private static int CalculateAge(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return age;
    }
}
