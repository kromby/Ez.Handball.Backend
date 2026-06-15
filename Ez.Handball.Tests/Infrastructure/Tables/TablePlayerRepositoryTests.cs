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

    private void SetupClubRows(string clubId, params PlayerEntity[] rows)
    {
        _query
            .Setup(q => q.QueryAsync<PlayerEntity>(
                Ez.Handball.Infrastructure.Tables.Players, $"ClubId eq '{clubId}'", default))
            .Returns(ToAsync(rows));
    }

    private static PlayerEntity ClubPlayer(string id, string name, string clubId,
        string position = "VS", string? jersey = null, bool? retired = null) =>
        new()
        {
            PartitionKey = $"{clubId}-karlar", RowKey = id, Name = name,
            Position = position, JerseyNumber = jersey, Gender = "karlar",
            ClubId = clubId, ClubName = "KR", Retired = retired
        };

    [Fact]
    public async Task ListByClubAsync_ReturnsNonRetiredPlayers()
    {
        SetupClubRows("385",
            ClubPlayer("1", "Active A", "385", retired: null),
            ClubPlayer("2", "Active B", "385", retired: false),
            ClubPlayer("3", "Retired C", "385", retired: true));

        var result = await CreateSut().ListByClubAsync("385", default);

        Assert.Equal(new[] { "1", "2" }, result.Select(p => p.PlayerId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ListByClubAsync_MapsFieldsIncludingAge()
    {
        SetupClubRows("385", new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "12", Name = "Aron",
            JerseyNumber = "23", Position = "VS", Gender = "karlar", ClubId = "385",
            ClubName = "KR", DateOfBirth = new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero)
        });

        var result = await CreateSut(today: new DateOnly(2026, 5, 22)).ListByClubAsync("385", default);

        var p = Assert.Single(result);
        Assert.Equal("12", p.PlayerId);
        Assert.Equal("Aron", p.Name);
        Assert.Equal("23", p.JerseyNumber);
        Assert.Equal("VS", p.Position);
        Assert.Equal(35, p.Age);
        Assert.Equal("385", p.ClubId);
    }

    [Fact]
    public async Task ListByClubAsync_NoRows_ReturnsEmpty()
    {
        SetupClubRows("999");

        Assert.Empty(await CreateSut().ListByClubAsync("999", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListByClubAsync_BlankClubId_ReturnsEmptyWithoutQuerying(string clubId)
    {
        var result = await CreateSut().ListByClubAsync(clubId, default);

        Assert.Empty(result);
        _query.Verify(q => q.QueryAsync<PlayerEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task GetByIdAsync_RetiredTrue_MapsToRetiredTrue()
    {
        const string playerId = "777";
        SetupRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Retired Rúnar",
            Gender = "karlar",
            ClubId = "385",
            Position = "VS",
            Retired = true
        });

        var result = await CreateSut(today: new DateOnly(2026, 5, 22)).GetByIdAsync(playerId, default);

        Assert.NotNull(result);
        Assert.True(result!.Retired);
    }

    [Fact]
    public async Task GetByIdAsync_RetiredNull_MapsToFalse()
    {
        const string playerId = "778";
        SetupRows(playerId, new PlayerEntity
        {
            PartitionKey = "385-karlar",
            RowKey = playerId,
            Name = "Active Aron",
            Gender = "karlar",
            ClubId = "385",
            Position = "VS",
            Retired = null
        });

        var result = await CreateSut(today: new DateOnly(2026, 5, 22)).GetByIdAsync(playerId, default);

        Assert.NotNull(result);
        Assert.False(result!.Retired);
    }
}
