using Ez.Handball.Api;
using Ez.Handball.Api.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Api.Services;

public class PlayerLookupServiceTests
{
    private readonly Mock<ITableQuery> _query = new();

    private PlayerLookupService CreateSut(DateOnly? today = null)
    {
        var clock = today is null
            ? (Func<DateOnly>)(() => DateOnly.FromDateTime(DateTime.UtcNow))
            : (() => today.Value);
        return new PlayerLookupService(_query.Object, clock, NullLogger<PlayerLookupService>.Instance);
    }

    private void SetupPlayerRows(string playerId, params PlayerEntity[] rows)
    {
        _query
            .Setup(q => q.QueryAsync<PlayerEntity>(
                Tables.Players, $"RowKey eq '{playerId}'", default))
            .Returns(ToAsync(rows));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetPlayerAsync_PlayerExists_ReturnsMappedDto()
    {
        const string playerId = "12345";

        SetupPlayerRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Aron Pálmarsson",
            JerseyNumber = "23",
            DateOfBirth = new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero),
            Gender = "karlar",
            ClubId = "385",
            ClubName = "Stjarnan"
        });

        var sut = CreateSut(today: new DateOnly(2026, 5, 22));

        var result = await sut.GetPlayerAsync(playerId);

        Assert.NotNull(result);
        Assert.Equal(playerId, result!.PlayerId);
        Assert.Equal("Aron Pálmarsson", result.Name);
        Assert.Equal("23", result.JerseyNumber);
        Assert.Equal("385-karlar", result.TeamId);
        Assert.Equal("385", result.ClubId);
        Assert.Equal("Stjarnan", result.ClubName);
        Assert.Equal("karlar", result.Gender);
        Assert.Equal(35, result.Age);
        Assert.Equal(new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero), result.DateOfBirth);
    }

    [Fact]
    public async Task GetPlayerAsync_NoMatch_ReturnsNull()
    {
        SetupPlayerRows("nonexistent");

        var sut = CreateSut();

        var result = await sut.GetPlayerAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlayerAsync_NullClubName_PassesThrough()
    {
        const string playerId = "1";
        SetupPlayerRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "X",
            Gender = "karlar",
            ClubId = "385",
            ClubName = null
        });

        var sut = CreateSut();

        var result = await sut.GetPlayerAsync(playerId);

        Assert.NotNull(result);
        Assert.Null(result!.ClubName);
    }

    [Fact]
    public async Task GetPlayerAsync_NullDateOfBirth_ReturnsNullAge()
    {
        const string playerId = "1";
        SetupPlayerRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "X",
            DateOfBirth = null,
            Gender = "karlar",
            ClubId = "385"
        });

        var sut = CreateSut();

        var result = await sut.GetPlayerAsync(playerId);

        Assert.NotNull(result);
        Assert.Null(result!.DateOfBirth);
        Assert.Null(result.Age);
    }

    [Theory]
    [InlineData("2026-07-19", 36)]  // today is the birthday
    [InlineData("2026-07-18", 35)]  // day before
    [InlineData("2026-07-20", 36)]  // day after
    public async Task GetPlayerAsync_AgeAroundBirthday(string todayIso, int expectedAge)
    {
        const string playerId = "1";
        SetupPlayerRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "X",
            DateOfBirth = new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero),
            Gender = "karlar",
            ClubId = "385"
        });

        var sut = CreateSut(today: DateOnly.Parse(todayIso));

        var result = await sut.GetPlayerAsync(playerId);

        Assert.Equal(expectedAge, result!.Age);
    }
}
