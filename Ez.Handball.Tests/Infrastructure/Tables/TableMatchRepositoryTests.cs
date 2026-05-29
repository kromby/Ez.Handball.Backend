using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableMatchRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IMatchRepository CreateSut() =>
        new TableMatchRepository(_query.Object, NullLogger<TableMatchRepository>.Instance);

    private void SetupMatch(string matchId, params MatchEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<MatchEntity>(
                Ez.Handball.Infrastructure.Tables.Matches, $"RowKey eq '{matchId}'", default))
              .Returns(ToAsync(rows));

    private void SetupTournament(string tournamentId, params TournamentEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments, $"RowKey eq '{tournamentId}'", default))
              .Returns(ToAsync(rows));

    private void SetupTeams(string filter, params TeamEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TeamEntity>(
                Ez.Handball.Infrastructure.Tables.Teams, filter, default))
              .Returns(ToAsync(rows));

    private void SetupStats(string matchId, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                Ez.Handball.Infrastructure.Tables.PlayerStats, $"PartitionKey eq '{matchId}'", default))
              .Returns(ToAsync(rows));

    private void SetupPlayers(string teamId, params PlayerEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerEntity>(
                Ez.Handball.Infrastructure.Tables.Players, $"PartitionKey eq '{teamId}'", default))
              .Returns(ToAsync(rows));

    private static MatchEntity Match(
        string matchId, string tournamentId, string homeTeamId, string awayTeamId,
        int homeScore, int awayScore, int homeHalf, int awayHalf,
        string venue = "Ásgarður", int? attendance = 412, string status = "S") =>
        new()
        {
            PartitionKey = tournamentId, RowKey = matchId,
            HomeTeamId = homeTeamId, AwayTeamId = awayTeamId,
            HomeScore = homeScore, AwayScore = awayScore,
            HomeHalftimeScore = homeHalf, AwayHalftimeScore = awayHalf,
            Venue = venue, Attendance = attendance, Status = status,
            Date = DateTimeOffset.UnixEpoch
        };

    private static TeamEntity Team(string teamId, string clubId, string name) =>
        new() { PartitionKey = "team", RowKey = teamId, ClubId = clubId, Name = name, Gender = "karlar" };

    private static PlayerStatEntity Stat(string matchId, string playerId, string teamId, string? clubName,
        int g, int y, int tm, int r) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId, TeamId = teamId, ClubName = clubName,
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
    public async Task GetByIdAsync_NoMatchRow_ReturnsNull_AndQueriesNothingElse()
    {
        SetupMatch("nope");

        var result = await CreateSut().GetByIdAsync("nope", default);

        Assert.Null(result);
        _query.Verify(q => q.QueryAsync<PlayerStatEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_HappyPath_SplitsTeamsAndDerivesSecondHalf()
    {
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444", new TournamentEntity
        {
            PartitionKey = "2025-26", RowKey = "8444", Name = "Olís deild karla"
        });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"),
            Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414",
            Stat("103414", "9912", "385-karlar", "Stjarnan", 6, 1, 1, 0),
            Stat("103414", "7755", "390-karlar", "Breiðablik", 4, 0, 0, 0));
        SetupPlayers("385-karlar", Player("385-karlar", "9912", "Jón Jónsson", "7", "VS"));
        SetupPlayers("390-karlar", Player("390-karlar", "7755", "Páll Pálsson", "12", "MS"));

        var result = await CreateSut().GetByIdAsync("103414", default);

        Assert.NotNull(result);
        Assert.Equal("103414", result!.MatchId);
        Assert.Equal("8444", result.TournamentId);
        Assert.Equal("Olís deild karla", result.TournamentName);
        Assert.Equal("2025-26", result.Season);
        Assert.Equal("Ásgarður", result.Venue);
        Assert.Equal(412, result.Attendance);

        Assert.Equal("385", result.HomeTeam.ClubId);
        Assert.Equal("Stjarnan", result.HomeTeam.ClubName);
        Assert.Equal(14, result.HomeTeam.Score.FirstHalf);
        Assert.Equal(14, result.HomeTeam.Score.SecondHalf);
        Assert.Equal(28, result.HomeTeam.Score.Final);

        Assert.Equal(12, result.AwayTeam.Score.FirstHalf);
        Assert.Equal(13, result.AwayTeam.Score.SecondHalf);
        Assert.Equal(25, result.AwayTeam.Score.Final);

        var homePlayer = Assert.Single(result.HomeTeam.Players);
        Assert.Equal("Jón Jónsson", homePlayer.Name);
        Assert.Equal("7", homePlayer.JerseyNumber);
        Assert.Equal("VS", homePlayer.Position);
        Assert.Equal(6, homePlayer.Goals);

        Assert.Single(result.AwayTeam.Players);
    }

    [Fact]
    public async Task GetByIdAsync_MissingTournament_NameNull_SeasonEmpty()
    {
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444");
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414");
        SetupPlayers("385-karlar");
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        Assert.Null(result!.TournamentName);
        Assert.Equal(string.Empty, result.Season);
    }

    [Fact]
    public async Task GetByIdAsync_StatRowWithNoPlayerEntity_DisplayFieldsNull()
    {
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414", Stat("103414", "9912", "385-karlar", "Stjarnan", 6, 0, 0, 0));
        SetupPlayers("385-karlar");
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        var line = Assert.Single(result!.HomeTeam.Players);
        Assert.Null(line.Name);
        Assert.Null(line.JerseyNumber);
        Assert.Null(line.Position);
        Assert.Equal(6, line.Goals);
    }

    [Fact]
    public async Task GetByIdAsync_UpcomingMatch_NoStats_EmptyRosters_ZeroLineScore()
    {
        SetupMatch("200", Match("200", "8444", "385-karlar", "390-karlar", 0, 0, 0, 0,
            venue: "", attendance: null, status: "O"));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("200");
        SetupPlayers("385-karlar");
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("200", default);

        Assert.Equal("O", result!.Status);
        Assert.Empty(result.HomeTeam.Players);
        Assert.Empty(result.AwayTeam.Players);
        Assert.Equal(0, result.HomeTeam.Score.Final);
        Assert.Equal(0, result.HomeTeam.Score.SecondHalf);
        Assert.Equal("Stjarnan", result.HomeTeam.ClubName);
    }

    [Fact]
    public async Task GetByIdAsync_OrdersPlayersByJerseyNumberNumericNullsLast()
    {
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414",
            Stat("103414", "a", "385-karlar", "Stjarnan", 0, 0, 0, 0),
            Stat("103414", "b", "385-karlar", "Stjarnan", 0, 0, 0, 0),
            Stat("103414", "c", "385-karlar", "Stjarnan", 0, 0, 0, 0));
        SetupPlayers("385-karlar",
            Player("385-karlar", "a", "No Number", null, "VS"),
            Player("385-karlar", "b", "Twelve", "12", "MS"),
            Player("385-karlar", "c", "Seven", "7", "HS"));
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        var ids = result!.HomeTeam.Players.Select(p => p.PlayerId).ToArray();
        Assert.Equal(new[] { "c", "b", "a" }, ids);
    }

    [Theory]
    [InlineData("385-karlar", "385")]
    [InlineData("385", "385")]
    public async Task GetByIdAsync_ClubIdDerivation(string teamId, string expectedClubId)
    {
        SetupMatch("103414", Match("103414", "8444", teamId, "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams($"PartitionKey eq 'team' and (RowKey eq '{teamId}' or RowKey eq '390-karlar')",
            Team(teamId, expectedClubId, "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414");
        SetupPlayers(teamId);
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        Assert.Equal(expectedClubId, result!.HomeTeam.ClubId);
    }

    [Fact]
    public async Task GetByIdAsync_ClubNameFallsBackToStatRow_WhenTeamRowMissing()
    {
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 28, 25, 14, 12));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')");
        SetupStats("103414",
            Stat("103414", "9912", "385-karlar", "Stjarnan", 6, 0, 0, 0));
        SetupPlayers("385-karlar", Player("385-karlar", "9912", "Jón", "7", "VS"));
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        Assert.Equal("Stjarnan", result!.HomeTeam.ClubName);
        Assert.Null(result.AwayTeam.ClubName);
    }

    [Fact]
    public async Task GetByIdAsync_HalftimeExceedsFinal_SecondHalfFlooredAtZero()
    {
        // Corrupt source data: halftime (15) greater than final (14) must not yield a negative half.
        SetupMatch("103414", Match("103414", "8444", "385-karlar", "390-karlar", 14, 25, 15, 12));
        SetupTournament("8444", new TournamentEntity { PartitionKey = "2025-26", RowKey = "8444", Name = "Olís" });
        SetupTeams("PartitionKey eq 'team' and (RowKey eq '385-karlar' or RowKey eq '390-karlar')",
            Team("385-karlar", "385", "Stjarnan"), Team("390-karlar", "390", "Breiðablik"));
        SetupStats("103414");
        SetupPlayers("385-karlar");
        SetupPlayers("390-karlar");

        var result = await CreateSut().GetByIdAsync("103414", default);

        Assert.Equal(0, result!.HomeTeam.Score.SecondHalf);
        Assert.Equal(15, result.HomeTeam.Score.FirstHalf);
        Assert.Equal(14, result.HomeTeam.Score.Final);
    }
}
