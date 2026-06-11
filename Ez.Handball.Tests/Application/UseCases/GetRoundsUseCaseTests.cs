using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetRoundsUseCaseTests
{
    private readonly Mock<IMatchRepository> _matches = new();

    private GetRoundsUseCase CreateSut() => new(_matches.Object);

    private static MatchListItem Item(string id, string round, DateTimeOffset date, string status,
        int homeScore = 0, int awayScore = 0) =>
        new(id, round, date, "Höllin", status,
            new MatchListTeam("385-karlar", "385", "KR", "logo-385", homeScore),
            new MatchListTeam("390-karlar", "390", "Breiðablik", null, awayScore));

    private void Setup(string tournamentId, params MatchListItem[] items) =>
        _matches.Setup(m => m.ListByTournamentAsync(tournamentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TournamentMatches(tournamentId, "Olís deild karla", "2025-26", items));

    [Fact]
    public async Task ExecuteAsync_UnknownTournament_ReturnsNotFound()
    {
        _matches.Setup(m => m.ListByTournamentAsync("9999", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TournamentMatches?)null);

        var result = await CreateSut().ExecuteAsync("9999", default);

        Assert.IsType<GetRoundsResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersRounds_NumericAscendingThenTextLast()
    {
        Setup("8444",
            Item("a", "2", new DateTimeOffset(2025, 9, 10, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("b", "10", new DateTimeOffset(2025, 11, 1, 19, 0, 0, TimeSpan.Zero), "O"),
            Item("c", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("d", "Undanúrslit", new DateTimeOffset(2026, 4, 1, 19, 0, 0, TimeSpan.Zero), "O"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        Assert.Equal(new[] { "1", "2", "10", "Undanúrslit" },
            found.Listing.Rounds.Select(r => r.Round).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_PlayedMatch_SurfacesScore_UpcomingIsNull()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S", 28, 25),
            Item("b", "2", new DateTimeOffset(2025, 9, 10, 19, 0, 0, TimeSpan.Zero), "O", 0, 0));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single(r => r.Round == "1").Matches.Single();
        Assert.True(round1.Played);
        Assert.Equal(28, round1.Home.Score);
        Assert.Equal(25, round1.Away.Score);

        var round2 = found.Listing.Rounds.Single(r => r.Round == "2").Matches.Single();
        Assert.False(round2.Played);
        Assert.Null(round2.Home.Score);
        Assert.Null(round2.Away.Score);
    }

    [Fact]
    public async Task ExecuteAsync_MultiDayRound_HasDistinctStartAndEndDates()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"),
            Item("b", "1", new DateTimeOffset(2025, 9, 4, 14, 0, 0, TimeSpan.Zero), "S"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single(r => r.Round == "1");
        Assert.Equal(new DateOnly(2025, 9, 3), round1.StartDate);
        Assert.Equal(new DateOnly(2025, 9, 4), round1.EndDate);
        Assert.Equal(new[] { "a", "b" }, round1.Matches.Select(m => m.MatchId).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_SingleDayRound_HasEqualStartAndEndDates()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S"));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single();
        Assert.Equal(round1.StartDate, round1.EndDate);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyTournament_ReturnsEmptyRoundList()
    {
        Setup("8444"); // no items

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        Assert.Empty(found.Listing.Rounds);
    }

    [Fact]
    public async Task ExecuteAsync_MixedPlayedAndUpcomingInSameRound_GatesScorePerMatch()
    {
        Setup("8444",
            Item("a", "1", new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "S", 28, 25),
            Item("b", "1", new DateTimeOffset(2025, 9, 3, 21, 0, 0, TimeSpan.Zero), "O", 0, 0));

        var found = Assert.IsType<GetRoundsResult.Found>(await CreateSut().ExecuteAsync("8444", default));

        var round1 = found.Listing.Rounds.Single(r => r.Round == "1");
        var played = round1.Matches.Single(m => m.MatchId == "a");
        var upcoming = round1.Matches.Single(m => m.MatchId == "b");
        Assert.True(played.Played);
        Assert.Equal(28, played.Home.Score);
        Assert.False(upcoming.Played);
        Assert.Null(upcoming.Home.Score);
        Assert.Null(upcoming.Away.Score);
    }
}
