using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePlayerPoolRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IPlayerPoolRepository CreateSut() =>
        new TablePlayerPoolRepository(_query.Object, NullLogger<TablePlayerPoolRepository>.Instance);

    private void SetupStats(params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                  Ez.Handball.Infrastructure.Tables.PlayerStats, It.IsAny<string?>(), default))
              .Returns(ToAsync(rows));

    private void SetupPlayers(params PlayerEntity[] players) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(
                  Ez.Handball.Infrastructure.Tables.Players, It.IsAny<string>(), default))
              .Returns(ToAsync(players));

    private static PlayerStatEntity Stat(
        string matchId, string playerId, string season, string tournamentId,
        string teamId, string? clubName, int g) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId,
            Goals = g, YellowCards = 0, TwoMinuteSuspensions = 0, RedCards = 0,
            TournamentId = tournamentId, Season = season, TeamId = teamId, ClubName = clubName
        };

    private static PlayerEntity Plr(string playerId, string teamId, string name, string position) =>
        new() { PartitionKey = teamId, RowKey = playerId, Name = name, Position = position };

    private static PlayerPoolQuery Q(
        string? season = null, IReadOnlyList<string>? tournamentIds = null, string? gender = null) =>
        new(season, tournamentIds, gender);

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetAggregated_SumsStatsPerPlayer_JoinsNameAndPosition()
    {
        SetupStats(
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 3));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal("p1", p.PlayerId);
        Assert.Equal("Aron", p.Name);
        Assert.Equal("CB", p.Position);
        Assert.Equal("385", p.ClubId);
        Assert.Equal("karlar", p.Gender);
        Assert.Equal(2, p.Stats.Games);
        Assert.Equal(8, p.Stats.Goals);
    }

    [Fact]
    public async Task GetAggregated_GenderFilter_DropsOtherGender()
    {
        SetupStats(
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p2", "2025-26", "8434", "385-kvenna", "Stjarnan", 7));
        SetupPlayers(
            Plr("p1", "385-karlar", "Aron", "CB"),
            Plr("p2", "385-kvenna", "Anna", "GK"));

        var result = await CreateSut().GetAggregatedAsync(Q(gender: "karlar"), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal("p1", p.PlayerId);
    }

    [Fact]
    public async Task GetAggregated_EmptyTournamentScope_ReturnsEmpty()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(tournamentIds: Array.Empty<string>()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAggregated_MissingPlayerRow_PositionEmpty_NameNull()
    {
        SetupStats(Stat("m1", "p9", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(); // no Players rows

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Null(p.Name);
        Assert.Equal(string.Empty, p.Position);
    }
}
