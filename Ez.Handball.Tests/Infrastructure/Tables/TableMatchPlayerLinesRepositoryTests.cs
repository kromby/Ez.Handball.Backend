using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableMatchPlayerLinesRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IMatchPlayerLinesRepository CreateSut() =>
        new TableMatchPlayerLinesRepository(_query.Object, NullLogger<TableMatchPlayerLinesRepository>.Instance);

    private void SetupStats(string matchId, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                Ez.Handball.Infrastructure.Tables.PlayerStats, $"PartitionKey eq '{matchId}'", default))
              .Returns(ToAsync(rows));

    private void SetupPlayers(string teamId, params PlayerEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(
                Ez.Handball.Infrastructure.Tables.Players, $"PartitionKey eq '{teamId}'", default))
              .Returns(ToAsync(rows));

    private void SetupPlayersById(string filter, params PlayerEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(
                Ez.Handball.Infrastructure.Tables.Players, filter, default))
              .Returns(ToAsync(rows));

    private static PlayerStatEntity Stat(string matchId, string playerId, string teamId,
        int g, int y, int tm, int r) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId, TeamId = teamId,
            Goals = g, YellowCards = y, TwoMinuteSuspensions = tm, RedCards = r,
            TournamentId = "8444", Season = "2025-26"
        };

    private static PlayerEntity Player(string teamId, string playerId, string name, string? jersey, string position) =>
        new() { PartitionKey = teamId, RowKey = playerId, Name = name, JerseyNumber = jersey, Position = position };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetByMatchAsync_NoStats_ReturnsEmpty_AndDoesNotQueryPlayers()
    {
        SetupStats("103414");

        var result = await CreateSut().GetByMatchAsync("103414", default);

        Assert.Empty(result);
        _query.Verify(q => q.QueryAsync<PlayerEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByMatchAsync_GroupsByTeam_AndJoinsDisplayFields()
    {
        SetupStats("103414",
            Stat("103414", "9912", "385-karlar", 6, 1, 1, 0),
            Stat("103414", "7755", "390-karlar", 4, 0, 0, 0));
        SetupPlayers("385-karlar", Player("385-karlar", "9912", "Jón Jónsson", "7", "VS"));
        SetupPlayers("390-karlar", Player("390-karlar", "7755", "Páll Pálsson", "12", "MS"));

        var result = await CreateSut().GetByMatchAsync("103414", default);

        Assert.Equal(2, result.Count);

        var home = Assert.Single(result["385-karlar"]);
        Assert.Equal("9912", home.PlayerId);
        Assert.Equal("Jón Jónsson", home.Name);
        Assert.Equal("7", home.JerseyNumber);
        Assert.Equal("VS", home.Position);
        Assert.Equal(6, home.Goals);
        Assert.Equal(1, home.YellowCards);
        Assert.Equal(1, home.TwoMinuteSuspensions);

        var away = Assert.Single(result["390-karlar"]);
        Assert.Equal("Páll Pálsson", away.Name);
    }

    [Fact]
    public async Task GetByMatchAsync_StatRowWithNoPlayerEntity_DisplayFieldsNull()
    {
        SetupStats("103414", Stat("103414", "9912", "385-karlar", 6, 0, 0, 0));
        SetupPlayers("385-karlar");   // roster lookup returns nothing
        SetupPlayersById("RowKey eq '9912'");   // ...and no row under any other team either

        var result = await CreateSut().GetByMatchAsync("103414", default);

        var line = Assert.Single(result["385-karlar"]);
        Assert.Null(line.Name);
        Assert.Null(line.JerseyNumber);
        Assert.Null(line.Position);
        Assert.Equal(6, line.Goals);
    }

    [Fact]
    public async Task GetByMatchAsync_PlayerRowUnderAnotherTeam_FallsBackToIt_ForNameAndPosition()
    {
        // The player's Players row lives under a different club's partition (e.g. stale after a
        // transfer). The line still gets a name and position, but not that other club's jersey.
        SetupStats("111452",
            Stat("111452", "177347", "96-karlar", 2, 0, 0, 0),
            Stat("111452", "177119", "96-karlar", 10, 0, 1, 0),
            Stat("111452", "176059", "96-karlar", 3, 0, 0, 0));
        SetupPlayers("96-karlar", Player("96-karlar", "177347", "Ingvar Dagur Gunnarsson", "7", "LP"));
        SetupPlayersById("RowKey eq '177119' or RowKey eq '176059'",
            Player("166-karlar", "177119", "Ágúst Guðmundsson", "18", "RB"),
            Player("127-karlar", "176059", "Antoine Óskar Pantano", "34", "LB"));

        var result = await CreateSut().GetByMatchAsync("111452", default);

        var lines = result["96-karlar"].ToDictionary(l => l.PlayerId);
        Assert.Equal("Ingvar Dagur Gunnarsson", lines["177347"].Name);
        Assert.Equal("Ágúst Guðmundsson", lines["177119"].Name);
        Assert.Equal("RB", lines["177119"].Position);
        Assert.Null(lines["177119"].JerseyNumber);
        Assert.Equal(10, lines["177119"].Goals);
        Assert.Equal("Antoine Óskar Pantano", lines["176059"].Name);
    }

    [Fact]
    public async Task GetByMatchAsync_AllPlayersFoundUnderTeam_DoesNotRunFallbackQuery()
    {
        SetupStats("103414", Stat("103414", "9912", "385-karlar", 6, 0, 0, 0));
        SetupPlayers("385-karlar", Player("385-karlar", "9912", "Jón Jónsson", "7", "VS"));

        await CreateSut().GetByMatchAsync("103414", default);

        _query.Verify(q => q.QueryAsync<PlayerEntity>(
            Ez.Handball.Infrastructure.Tables.Players,
            It.Is<string>(f => f.StartsWith("RowKey")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByMatchAsync_OrdersByJerseyNumberNumericNullsLast()
    {
        SetupStats("103414",
            Stat("103414", "a", "385-karlar", 0, 0, 0, 0),
            Stat("103414", "b", "385-karlar", 0, 0, 0, 0),
            Stat("103414", "c", "385-karlar", 0, 0, 0, 0));
        SetupPlayers("385-karlar",
            Player("385-karlar", "a", "No Number", null, "VS"),
            Player("385-karlar", "b", "Twelve", "12", "MS"),
            Player("385-karlar", "c", "Seven", "7", "HS"));

        var result = await CreateSut().GetByMatchAsync("103414", default);

        var ids = result["385-karlar"].Select(p => p.PlayerId).ToArray();
        Assert.Equal(new[] { "c", "b", "a" }, ids);   // 7, 12, then null last
    }
}
