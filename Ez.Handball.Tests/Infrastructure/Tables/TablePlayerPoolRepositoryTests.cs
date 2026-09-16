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

    private void SetupStatsFiltered(string filterContains, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                  Ez.Handball.Infrastructure.Tables.PlayerStats,
                  It.Is<string?>(f => f != null && f.Contains(filterContains)), default))
              .Returns(ToAsync(rows));

    private static PlayerStatEntity Stat(
        string matchId, string playerId, string season, string tournamentId,
        string teamId, string? clubName, int g,
        int? assists = null, int? steals = null, int? blocks = null, int? saves = null) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId,
            Goals = g, YellowCards = 0, TwoMinuteSuspensions = 0, RedCards = 0,
            TournamentId = tournamentId, Season = season, TeamId = teamId, ClubName = clubName,
            HbStatzAssists = assists, HbStatzSteals = steals, HbStatzBlocks = blocks, HbStatzSaves = saves
        };

    private static PlayerEntity Plr(string playerId, string teamId, string name, string position) =>
        new() { PartitionKey = teamId, RowKey = playerId, Name = name, Position = position };

    private static PlayerPoolQuery Q(
        string? season = null, IReadOnlyList<string>? tournamentIds = null, string? gender = null,
        string? previousSeason = null, IReadOnlyList<string>? previousSeasonTournamentIds = null) =>
        new(season, tournamentIds, gender, previousSeason, previousSeasonTournamentIds);

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
    public async Task GetAggregated_PopulatesRetiredFromPlayersTable()
    {
        SetupStats(
            Stat("m1", "active", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "retiree", "2025-26", "8444", "385-karlar", "Stjarnan", 3));
        SetupPlayers(
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "active", Name = "A", Position = "CB", Retired = false },
            new PlayerEntity { PartitionKey = "385-karlar", RowKey = "retiree", Name = "R", Position = "CB", Retired = true });

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        Assert.True(result.Single(p => p.PlayerId == "retiree").Retired);
        Assert.False(result.Single(p => p.PlayerId == "active").Retired);
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

    [Fact]
    public async Task GetAggregated_PreviousSeasonProvided_JoinsPreviousStatsByPlayer()
    {
        SetupStatsFiltered("'2025-26'",
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupStatsFiltered("'2024-25'",
            Stat("m0", "p1", "2024-25", "7777", "385-karlar", "Stjarnan", 8),
            Stat("m0b", "p1", "2024-25", "7777", "385-karlar", "Stjarnan", 2));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(season: "2025-26", tournamentIds: new[] { "8444" },
              previousSeason: "2024-25", previousSeasonTournamentIds: new[] { "7777" }),
            CancellationToken.None);

        var p = Assert.Single(result);
        Assert.NotNull(p.PreviousSeasonStats);
        Assert.Equal(2, p.PreviousSeasonStats!.Games);
        Assert.Equal(10, p.PreviousSeasonStats.Goals);
    }

    [Fact]
    public async Task GetAggregated_NoPreviousSeason_LeavesPreviousStatsNull()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        Assert.Null(Assert.Single(result).PreviousSeasonStats);
    }

    [Fact]
    public async Task GetAggregated_PreviousSeasonEmptyTournamentIds_LeavesPreviousStatsNull()
    {
        SetupStats(Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(previousSeason: "2024-25", previousSeasonTournamentIds: Array.Empty<string>()),
            CancellationToken.None);

        Assert.Null(Assert.Single(result).PreviousSeasonStats);
    }

    [Fact]
    public async Task GetAggregated_SumsHbStatzFields_DefaultingNullToZero()
    {
        SetupStats(
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5,
                assists: 2, steals: 1, blocks: null, saves: null),
            Stat("m2", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 3,
                assists: null, steals: 3, blocks: 1, saves: 8));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal(2, p.Stats.Assists);
        Assert.Equal(4, p.Stats.Steals);
        Assert.Equal(1, p.Stats.Blocks);
        Assert.Equal(8, p.Stats.Saves);
    }

    [Fact]
    public async Task GetAggregated_PreviousSeasonStats_SumHbStatzFields()
    {
        SetupStatsFiltered("'2025-26'",
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupStatsFiltered("'2024-25'",
            Stat("m0", "p1", "2024-25", "7777", "385-karlar", "Stjarnan", 8,
                assists: 3, steals: 2, blocks: 1, saves: 0));
        SetupPlayers(Plr("p1", "385-karlar", "Aron", "CB"));

        var result = await CreateSut().GetAggregatedAsync(
            Q(season: "2025-26", tournamentIds: new[] { "8444" },
              previousSeason: "2024-25", previousSeasonTournamentIds: new[] { "7777" }),
            CancellationToken.None);

        var p = Assert.Single(result);
        Assert.NotNull(p.PreviousSeasonStats);
        Assert.Equal(3, p.PreviousSeasonStats!.Assists);
        Assert.Equal(2, p.PreviousSeasonStats.Steals);
        Assert.Equal(1, p.PreviousSeasonStats.Blocks);
        Assert.Equal(0, p.PreviousSeasonStats.Saves);
    }
}
