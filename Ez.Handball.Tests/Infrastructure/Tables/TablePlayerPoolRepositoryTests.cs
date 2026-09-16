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
    public async Task GetAggregated_SumsHbStatzStats_AndJoinsPositionSecondary()
    {
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                  Ez.Handball.Infrastructure.Tables.PlayerStats, It.IsAny<string?>(), default))
              .Returns(ToAsync(new[]
              {
                  new PlayerStatEntity
                  {
                      PartitionKey = "m1", RowKey = "p1", Goals = 5,
                      TournamentId = "8444", Season = "2025-26", TeamId = "385-karlar", ClubName = "Stjarnan",
                      HbStatzAssists = 2, HbStatzSteals = 1, HbStatzBlocks = 1, HbStatzSaves = 3,
                      HbStatzTurnovers = 1, HbStatzLegalStops = 2, HbStatzShots = 6,
                      HbStatzExpectedGoals = 4.5, HbStatzShotsFaced = 10,
                      HbStatzExpectedSaves = 2.5, HbStatzGradeTotal = 7.0, HbStatzGradeOffense = 6.5,
                      HbStatzGradeDefense = 7.5, HbStatzGradeGoalkeeping = null,
                  },
                  new PlayerStatEntity
                  {
                      PartitionKey = "m2", RowKey = "p1", Goals = 3,
                      TournamentId = "8444", Season = "2025-26", TeamId = "385-karlar", ClubName = "Stjarnan",
                      HbStatzAssists = 1, HbStatzSteals = null, HbStatzBlocks = 2, HbStatzSaves = 1,
                      HbStatzShotsFaced = 5, HbStatzGradeTotal = 8.0,
                  },
              }));
        SetupPlayers(new PlayerEntity
        {
            PartitionKey = "385-karlar", RowKey = "p1", Name = "Aron", Position = "CB", PositionSecondary = "LB",
        });

        var result = await CreateSut().GetAggregatedAsync(Q(), CancellationToken.None);

        var p = Assert.Single(result);
        Assert.Equal("LB", p.PositionSecondary);
        Assert.Equal(3, p.Stats.Assists);
        Assert.Equal(1, p.Stats.Steals);
        Assert.Equal(3, p.Stats.Blocks);
        Assert.Equal(4, p.Stats.Saves);
        Assert.Equal(1, p.Stats.Turnovers);
        Assert.Equal(2, p.Stats.LegalStops);
        Assert.Equal(6, p.Stats.Shots);
        Assert.Equal(4.5, p.Stats.ExpectedGoals);
        Assert.Equal(15, p.Stats.ShotsFaced);
        Assert.Equal(26.7, p.Stats.SavePct); // 4/15 * 100, rounded to 1dp
        Assert.Equal(2.5, p.Stats.ExpectedSaves);
        Assert.Equal(7.5, p.Stats.GradeTotal); // (7.0 + 8.0) / 2
        Assert.Equal(6.5, p.Stats.GradeOffense); // single non-null value
        Assert.Equal(7.5, p.Stats.GradeDefense);
        Assert.Null(p.Stats.GradeGoalkeeping); // no non-null values
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
}
