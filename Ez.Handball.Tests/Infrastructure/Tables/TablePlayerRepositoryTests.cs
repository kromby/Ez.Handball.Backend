using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePlayerRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IPlayerRepository CreateSut(DateOnly? today = null)
    {
        var clock = today is null
            ? (Func<DateOnly>)(() => DateOnly.FromDateTime(DateTime.UtcNow))
            : (() => today.Value);
        return new TablePlayerRepository(_query.Object, clock, NullLogger<TablePlayerRepository>.Instance);
    }

    private void SetupRows(string playerId, params PlayerEntity[] rows)
    {
        _query
            .Setup(q => q.QueryAsync<PlayerEntity>(
                Ez.Handball.Infrastructure.Tables.Players, $"RowKey eq '{playerId}'", default))
            .Returns(ToAsync(rows));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetByIdAsync_NoMatch_ReturnsNull()
    {
        SetupRows("nope");

        var result = await CreateSut().GetByIdAsync("nope", default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_PlayerExists_MapsAllFields()
    {
        const string playerId = "12345";
        SetupRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Aron Pálmarsson",
            JerseyNumber = "23",
            DateOfBirth = new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero),
            Gender = "karlar",
            ClubId = "385",
            ClubName = "Stjarnan",
            Position = "VS"
        });

        var sut = CreateSut(today: new DateOnly(2026, 5, 22));

        var result = await sut.GetByIdAsync(playerId, default);

        Assert.NotNull(result);
        Assert.Equal(playerId, result!.PlayerId);
        Assert.Equal("Aron Pálmarsson", result.Name);
        Assert.Equal("23", result.JerseyNumber);
        Assert.Equal("385-karlar", result.TeamId);
        Assert.Equal("385", result.ClubId);
        Assert.Equal("Stjarnan", result.ClubName);
        Assert.Equal("karlar", result.Gender);
        Assert.Equal(35, result.Age);
        Assert.Equal(new DateOnly(1990, 7, 19), result.DateOfBirth);
        Assert.Equal("VS", result.Position);
    }

    [Fact]
    public async Task GetByIdAsync_NullClubName_PassesThrough()
    {
        SetupRows("1", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1",
            Name = "X", Gender = "karlar", ClubId = "385", ClubName = null
        });

        var result = await CreateSut().GetByIdAsync("1", default);

        Assert.NotNull(result);
        Assert.Null(result!.ClubName);
    }

    [Fact]
    public async Task GetByIdAsync_NullDateOfBirth_ReturnsNullAge()
    {
        SetupRows("1", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1",
            Name = "X", DateOfBirth = null, Gender = "karlar", ClubId = "385"
        });

        var result = await CreateSut().GetByIdAsync("1", default);

        Assert.NotNull(result);
        Assert.Null(result!.DateOfBirth);
        Assert.Null(result.Age);
    }

    [Theory]
    [InlineData("2026-07-19", 36)]  // today is the birthday
    [InlineData("2026-07-18", 35)]  // day before
    [InlineData("2026-07-20", 36)]  // day after
    public async Task GetByIdAsync_AgeAroundBirthday(string todayIso, int expectedAge)
    {
        SetupRows("1", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "1", Name = "X",
            DateOfBirth = new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero),
            Gender = "karlar", ClubId = "385"
        });

        var sut = CreateSut(today: DateOnly.Parse(todayIso));

        var result = await sut.GetByIdAsync("1", default);

        Assert.Equal(expectedAge, result!.Age);
    }
}
