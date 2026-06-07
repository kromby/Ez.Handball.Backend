using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableLeaderboardRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private ILeaderboardRepository CreateSut() =>
        new TableLeaderboardRepository(_query.Object, NullLogger<TableLeaderboardRepository>.Instance);

    private void SetupStats(string? filter, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(Ez.Handball.Infrastructure.Tables.PlayerStats, filter!, default))
              .Returns(ToAsync(rows));

    private void SetupPlayers(params PlayerEntity[] players) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(Ez.Handball.Infrastructure.Tables.Players, It.IsAny<string>(), default))
              .Returns(ToAsync(players));

    private static PlayerStatEntity Stat(
        string matchId, string playerId, string season, string tournamentId,
        string teamId, string? clubName, int g, int y = 0, int tm = 0, int r = 0) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId,
            Goals = g, YellowCards = y, TwoMinuteSuspensions = tm, RedCards = r,
            TournamentId = tournamentId, Season = season, TeamId = teamId, ClubName = clubName
        };

    private static PlayerEntity Plr(string playerId, string teamId, string name) =>
        new() { PartitionKey = teamId, RowKey = playerId, Name = name };

    private static LeaderboardQuery Q(
        LeaderboardMetric metric = LeaderboardMetric.Goals,
        string? season = null, IReadOnlyList<string>? tournamentIds = null, string? gender = null) =>
        new(metric, season, tournamentIds, gender);

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    // ---- filter construction ----

    [Fact]
    public async Task GetRankedAsync_NoSeasonOrTournament_ScansWithNullFilter()
    {
        SetupStats(null, Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(Q(), default);

        Assert.Single(result);
        _query.Verify(q => q.QueryAsync<PlayerStatEntity>(Ez.Handball.Infrastructure.Tables.PlayerStats, null!, default), Times.Once);
    }

    [Fact]
    public async Task GetRankedAsync_SeasonOnly_BuildsSeasonFilter()
    {
        SetupStats("Season eq '2025-26'", Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(Q(season: "2025-26"), default);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetRankedAsync_SingleTournamentId_BuildsTournamentFilter()
    {
        SetupStats("TournamentId eq '8444'", Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(Q(tournamentIds: new[] { "8444" }), default);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetRankedAsync_MultipleTournamentIds_BuildsOrSetFilter()
    {
        SetupStats("(TournamentId eq '8444' or TournamentId eq '8427')",
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p1", "2025-26", "8427", "385-karlar", "Stjarnan", 3));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(
            Q(tournamentIds: new[] { "8444", "8427" }), default));

        Assert.Equal(8, e.Goals);
    }

    [Fact]
    public async Task GetRankedAsync_SeasonAndTournamentIds_BuildsAndFilter()
    {
        SetupStats("Season eq '2025-26' and (TournamentId eq '8444')",
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(
            Q(season: "2025-26", tournamentIds: new[] { "8444" }), default);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetRankedAsync_EmptyTournamentIds_ScansSeasonOnly()
    {
        SetupStats("Season eq '2025-26'", Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(
            Q(season: "2025-26", tournamentIds: Array.Empty<string>()), default);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetRankedAsync_EscapesSingleQuotesInFilterValues()
    {
        SetupStats("Season eq 'O''Brien'", Stat("m1", "p1", "O'Brien", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var result = await CreateSut().GetRankedAsync(Q(season: "O'Brien"), default);

        Assert.Single(result);
    }

    // ---- gender filter (in memory) ----

    [Fact]
    public async Task GetRankedAsync_GenderFilter_KeepsOnlyMatchingTeamSuffix()
    {
        SetupStats(null,
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5),
            Stat("m2", "p2", "2025-26", "8434", "390-kvenna", "Valur",    9));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"), Plr("p2", "390-kvenna", "Anna"));

        var result = await CreateSut().GetRankedAsync(Q(gender: "karlar"), default);

        Assert.Single(result);
        Assert.Equal("p1", result[0].PlayerId);
    }

    // ---- aggregation ----

    [Fact]
    public async Task GetRankedAsync_GroupsByPlayer_SumsStatsAndCountsGames()
    {
        SetupStats(null,
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 1, 2, 0),
            Stat("m2", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 7, 0, 1, 1));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal(2, e.Games);
        Assert.Equal(12, e.Goals);
        Assert.Equal(1, e.YellowCards);
        Assert.Equal(3, e.TwoMinuteSuspensions);
        Assert.Equal(1, e.RedCards);
    }

    [Fact]
    public async Task GetRankedAsync_AvgGoals_RoundedToTwoDecimals()
    {
        var rows = Enumerable.Range(1, 18)
            .Select(i => Stat($"m{i}", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", i == 1 ? 142 - 17 : 1))
            .ToArray(); // 18 games, 142 goals total
        SetupStats(null, rows);
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal(18, e.Games);
        Assert.Equal(142, e.Goals);
        Assert.Equal(7.89, e.AvgGoals); // 142 / 18 = 7.888... -> 7.89
    }

    // ---- ranking ----

    [Theory]
    [InlineData(LeaderboardMetric.Goals, "goalsKing")]
    [InlineData(LeaderboardMetric.YellowCards, "cardKing")]
    [InlineData(LeaderboardMetric.TwoMinuteSuspensions, "suspKing")]
    [InlineData(LeaderboardMetric.RedCards, "redKing")]
    [InlineData(LeaderboardMetric.Games, "gamesKing")]
    public async Task GetRankedAsync_RanksBySelectedMetricDescending(LeaderboardMetric metric, string expectedTop)
    {
        SetupStats(null,
            Stat("m1", "goalsKing", "2025-26", "8444", "385-karlar", "A", 50, 0, 0, 0),
            Stat("m2", "cardKing",  "2025-26", "8444", "385-karlar", "A", 1, 50, 0, 0),
            Stat("m3", "suspKing",  "2025-26", "8444", "385-karlar", "A", 1, 0, 50, 0),
            Stat("m4", "redKing",   "2025-26", "8444", "385-karlar", "A", 1, 0, 0, 50),
            // gamesKing: 4 separate matches -> 4 games, everyone else has 1
            Stat("g1", "gamesKing", "2025-26", "8444", "385-karlar", "A", 0),
            Stat("g2", "gamesKing", "2025-26", "8444", "385-karlar", "A", 0),
            Stat("g3", "gamesKing", "2025-26", "8444", "385-karlar", "A", 0),
            Stat("g4", "gamesKing", "2025-26", "8444", "385-karlar", "A", 0));
        SetupPlayers();

        var result = await CreateSut().GetRankedAsync(Q(metric), default);

        Assert.Equal(expectedTop, result[0].PlayerId);
        Assert.Equal(1, result[0].Rank);
    }

    [Fact]
    public async Task GetRankedAsync_AssignsSequentialRanks()
    {
        SetupStats(null,
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "A", 10),
            Stat("m2", "p2", "2025-26", "8444", "385-karlar", "A", 8),
            Stat("m3", "p3", "2025-26", "8444", "385-karlar", "A", 6));
        SetupPlayers();

        var result = await CreateSut().GetRankedAsync(Q(), default);

        Assert.Equal(new[] { 1, 2, 3 }, result.Select(e => e.Rank).ToArray());
        Assert.Equal(new[] { "p1", "p2", "p3" }, result.Select(e => e.PlayerId).ToArray());
    }

    [Fact]
    public async Task GetRankedAsync_Tiebreak_FewerGamesThenPlayerId()
    {
        SetupStats(null,
            // p2 and p1 both 10 goals; p2 in 1 game, p1 in 2 games -> p2 ranks first (fewer games)
            Stat("a1", "p1", "2025-26", "8444", "385-karlar", "A", 6),
            Stat("a2", "p1", "2025-26", "8444", "385-karlar", "A", 4),
            Stat("b1", "p2", "2025-26", "8444", "385-karlar", "A", 10),
            // p3 also 10 goals in 1 game -> tie with p2 on games, broken by playerId ordinal (p2 < p3)
            Stat("c1", "p3", "2025-26", "8444", "385-karlar", "A", 10));
        SetupPlayers();

        var result = await CreateSut().GetRankedAsync(Q(), default);

        Assert.Equal(new[] { "p2", "p3", "p1" }, result.Select(e => e.PlayerId).ToArray());
    }

    // ---- club attribution ----

    [Fact]
    public async Task GetRankedAsync_AttributesClubWithMostGoals()
    {
        SetupStats(null,
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 3),
            Stat("m2", "p1", "2025-26", "8444", "410-karlar", "Valur",    9));
        SetupPlayers(Plr("p1", "410-karlar", "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal("410", e.ClubId);
        Assert.Equal("Valur", e.ClubName);
        Assert.Equal(12, e.Goals); // total still spans both clubs
    }

    [Fact]
    public async Task GetRankedAsync_ClubTiebreak_EqualGoals_MoreGamesWins()
    {
        SetupStats(null,
            // club 385: 5 goals over 2 games; club 410: 5 goals over 1 game -> 385 wins (more games)
            Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 2),
            Stat("m2", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 3),
            Stat("m3", "p1", "2025-26", "8444", "410-karlar", "Valur",    5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal("385", e.ClubId);
        Assert.Equal("Stjarnan", e.ClubName);
    }

    // ---- name join ----

    [Fact]
    public async Task GetRankedAsync_JoinsPlayerName_FromPlayersTable()
    {
        SetupStats(null, Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(Plr("p1", "385-karlar", "Jón Jónsson"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal("Jón Jónsson", e.Name);
    }

    [Fact]
    public async Task GetRankedAsync_MissingPlayerRow_NameNull()
    {
        SetupStats(null, Stat("m1", "p1", "2025-26", "8444", "385-karlar", "Stjarnan", 5));
        SetupPlayers(); // no player rows

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Null(e.Name);
    }

    // ---- empty ----

    [Fact]
    public async Task GetRankedAsync_NoStatRows_ReturnsEmpty()
    {
        SetupStats(null);
        SetupPlayers();

        Assert.Empty(await CreateSut().GetRankedAsync(Q(), default));
    }

    [Fact]
    public async Task GetRankedAsync_AllFilteredOutByGender_ReturnsEmpty()
    {
        SetupStats(null, Stat("m1", "p1", "2025-26", "8434", "390-kvenna", "Valur", 9));
        SetupPlayers(Plr("p1", "390-kvenna", "Anna"));

        Assert.Empty(await CreateSut().GetRankedAsync(Q(gender: "karlar"), default));
    }

    // ---- clubId / gender derivation ----

    [Theory]
    [InlineData("385-karlar", "385", "karlar")]
    [InlineData("385", "385", "")]
    public async Task GetRankedAsync_DerivesClubIdAndGenderFromTeamId(
        string teamId, string expectedClubId, string expectedGender)
    {
        SetupStats(null, Stat("m1", "p1", "2025-26", "8444", teamId, "X", 5));
        SetupPlayers(Plr("p1", teamId, "Jón"));

        var e = Assert.Single(await CreateSut().GetRankedAsync(Q(), default));

        Assert.Equal(expectedClubId, e.ClubId);
        Assert.Equal(expectedGender, e.Gender);
    }
}
