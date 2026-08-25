using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetAdminGameStatusUseCaseTests
{
    private readonly Mock<ITournamentRepository> _tournaments = new();
    private readonly Mock<ISeasonRepository> _seasons = new();
    private readonly Mock<IMatchScheduleRepository> _schedules = new();
    private readonly Mock<IMatchRepository> _matches = new();

    private GetAdminGameStatusUseCase CreateSut() =>
        new(_tournaments.Object, _seasons.Object, _schedules.Object, _matches.Object);

    private static Tournament AnyTournament(string id = "8444") =>
        new(id, "Olís deild karla", "karlar", TournamentType.League, "olis-karla", "Olís deild karla");

    private static ScheduledMatch Scheduled(string matchId, string round, string status, DateTimeOffset? date = null) =>
        new(matchId, round, date ?? DateTimeOffset.UnixEpoch, "Ásgarður", "Stjarnan", "Breiðablik", status);

    private static TournamentMatches Ingested(string tournamentId, params string[] matchIds) => new(
        tournamentId, "Olís deild karla", "2025-26",
        matchIds.Select(id => new MatchListItem(
            id, "1", DateTimeOffset.UnixEpoch, "Ásgarður", "S",
            new MatchListTeam("385-karlar", "385", "Stjarnan", null, 28),
            new MatchListTeam("390-karlar", "390", "Breiðablik", null, 25))).ToList());

    [Fact]
    public async Task ExecuteAsync_ExplicitSeason_QueriesThatSeason_WithoutResolvingCurrent()
    {
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2024-25", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament>());

        await CreateSut().ExecuteAsync("2024-25", CancellationToken.None);

        _seasons.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NullSeason_ResolvesCurrentSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false), new("2025-26", true) });
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament>());

        await CreateSut().ExecuteAsync(null, CancellationToken.None);

        _tournaments.Verify(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoCurrentSeason_ReturnsEmpty_WithoutQueryingTournaments()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        Assert.Empty(result);
        _tournaments.Verify(
            r => r.ListActiveBySeasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MarksScheduledMatchIngested_WhenPresentInMatchesTable()
    {
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament> { AnyTournament("8444") });
        _schedules.Setup(r => r.GetAsync("8444", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new MatchSchedule(
                      new List<ScheduledMatch> { Scheduled("103414", "1", "S") },
                      DateTimeOffset.UnixEpoch));
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ingested("8444", "103414"));

        var result = await CreateSut().ExecuteAsync("2025-26", CancellationToken.None);

        var tournament = Assert.Single(result);
        var round = Assert.Single(tournament.Rounds);
        var game = Assert.Single(round.Games);
        Assert.Equal("103414", game.MatchId);
        Assert.Equal("played", game.Status);
        Assert.True(game.Ingested);
    }

    [Fact]
    public async Task ExecuteAsync_MarksScheduledMatchNotIngested_WhenAbsentFromMatchesTable()
    {
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament> { AnyTournament("8444") });
        _schedules.Setup(r => r.GetAsync("8444", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new MatchSchedule(
                      new List<ScheduledMatch> { Scheduled("999999", "1", "O") },
                      DateTimeOffset.UnixEpoch));
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("2025-26", CancellationToken.None);

        var game = Assert.Single(Assert.Single(result).Rounds.Single().Games);
        Assert.Equal("upcoming", game.Status);
        Assert.False(game.Ingested);
    }

    [Fact]
    public async Task ExecuteAsync_TournamentNeverSynced_ReturnsEmptyRounds_WithNullLastSyncedAt()
    {
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament> { AnyTournament("8444") });
        _schedules.Setup(r => r.GetAsync("8444", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((MatchSchedule?)null);
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("2025-26", CancellationToken.None);

        var tournament = Assert.Single(result);
        Assert.Empty(tournament.Rounds);
        Assert.Null(tournament.LastSyncedAt);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersRoundsNumericallyAscending()
    {
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament> { AnyTournament("8444") });
        _schedules.Setup(r => r.GetAsync("8444", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new MatchSchedule(
                      new List<ScheduledMatch>
                      {
                          Scheduled("2", "10", "O"),
                          Scheduled("1", "2", "O"),
                          Scheduled("3", "1", "O"),
                      },
                      DateTimeOffset.UnixEpoch));
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("2025-26", CancellationToken.None);

        Assert.Equal(new[] { "1", "2", "10" }, result.Single().Rounds.Select(r => r.Round).ToArray());
    }
}
