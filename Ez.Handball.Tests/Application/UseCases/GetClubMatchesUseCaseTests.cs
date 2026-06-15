using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetClubMatchesUseCaseTests
{
    private readonly Mock<IClubRepository> _clubs = new();
    private readonly Mock<ITournamentScopeResolver> _scope = new();
    private readonly Mock<ITournamentRepository> _tournaments = new();
    private readonly Mock<IMatchRepository> _matches = new();

    private GetClubMatchesUseCase CreateSut() =>
        new(_clubs.Object, _scope.Object, _tournaments.Object, _matches.Object);

    private void ClubExists(string clubId, bool exists = true) =>
        _clubs.Setup(c => c.ExistsAsync(clubId, It.IsAny<CancellationToken>())).ReturnsAsync(exists);

    private void Season(string? label) =>
        _scope.Setup(s => s.ResolveSeasonLabelAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(label);

    private void Tournaments(string season, params (string id, string name)[] ts) =>
        _tournaments.Setup(t => t.ListActiveBySeasonAsync(season, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ts.Select(t => new Tournament(
                t.id, t.name, "karlar", TournamentType.League, "c1", "Olís")).ToList());

    private void TournamentMatchesReturn(string tournamentId, string name, params MatchListItem[] items) =>
        _matches.Setup(m => m.ListByTournamentAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TournamentMatches(tournamentId, name, "2025-2026", items));

    private static MatchListItem Item(
        string id, string round, DateTimeOffset date, string status,
        int homeScore = 0, int awayScore = 0,
        string homeClub = "385", string awayClub = "390") =>
        new(id, round, date, "Höllin", status,
            new MatchListTeam($"{homeClub}-karlar", homeClub, "KR", "logo-h", homeScore),
            new MatchListTeam($"{awayClub}-karlar", awayClub, "Breidablik", "logo-a", awayScore));

    private static DateTimeOffset D(int month, int day) =>
        new(2025, month, day, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_UnknownClub_ReturnsNotFound()
    {
        ClubExists("999", false);

        var result = await CreateSut().ExecuteAsync("999", null, default);

        Assert.IsType<GetClubMatchesResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_NoCurrentSeason_ReturnsEmpty()
    {
        ClubExists("385");
        Season(null);

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        Assert.Empty(found.Listing.Matches);
        Assert.Null(found.Listing.Season);
    }

    [Fact]
    public async Task ExecuteAsync_HomeMatch_ClubPerspectiveAndScores()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("m1", "1", D(9, 3), "S", homeScore: 28, awayScore: 24, homeClub: "385", awayClub: "390"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        var m = Assert.Single(found.Listing.Matches);
        Assert.True(m.IsHome);
        Assert.Equal("played", m.Status);
        Assert.Equal("390", m.OpponentClubId);
        Assert.Equal("Breidablik", m.OpponentName);
        Assert.Equal("logo-a", m.OpponentLogoUrl);
        Assert.Equal(28, m.ClubScore);
        Assert.Equal(24, m.OpponentScore);
        Assert.Equal("Olís deild karla", m.TournamentName);
        Assert.Equal("8444", m.TournamentId);
    }

    [Fact]
    public async Task ExecuteAsync_AwayMatch_FlipsPerspectiveScores()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("m1", "1", D(9, 3), "S", homeScore: 30, awayScore: 27, homeClub: "390", awayClub: "385"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        var m = Assert.Single(found.Listing.Matches);
        Assert.False(m.IsHome);
        Assert.Equal("390", m.OpponentClubId);
        Assert.Equal(27, m.ClubScore);
        Assert.Equal(30, m.OpponentScore);
    }

    [Fact]
    public async Task ExecuteAsync_UpcomingMatch_ScoresNull()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("m1", "5", D(11, 1), "O", homeScore: 0, awayScore: 0));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        var m = Assert.Single(found.Listing.Matches);
        Assert.Equal("upcoming", m.Status);
        Assert.Null(m.ClubScore);
        Assert.Null(m.OpponentScore);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesMatchesNotInvolvingClub()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("m1", "1", D(9, 3), "S", homeClub: "111", awayClub: "222"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        Assert.Empty(found.Listing.Matches);
    }

    [Fact]
    public async Task ExecuteAsync_MergesAcrossTournaments_AndOrders()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"), ("9001", "Bikar"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("played-old", "1", D(9, 3), "S"),
            Item("played-new", "4", D(10, 20), "S"),
            Item("up-late", "8", D(12, 15), "O"));
        TournamentMatchesReturn("9001", "Bikar",
            Item("up-soon", "R1", D(11, 5), "O"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", null, default));

        Assert.Equal(new[] { "played-new", "played-old", "up-soon", "up-late" },
            found.Listing.Matches.Select(m => m.MatchId).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_StatusPlayedFilter_OnlyPlayed()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("p", "1", D(9, 3), "S"),
            Item("u", "2", D(11, 1), "O"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", ClubMatchStatusFilter.Played, default));

        var m = Assert.Single(found.Listing.Matches);
        Assert.Equal("p", m.MatchId);
    }

    [Fact]
    public async Task ExecuteAsync_StatusUpcomingFilter_OnlyUpcoming()
    {
        ClubExists("385");
        Season("2025-2026");
        Tournaments("2025-2026", ("8444", "Olís deild karla"));
        TournamentMatchesReturn("8444", "Olís deild karla",
            Item("p", "1", D(9, 3), "S"),
            Item("u", "2", D(11, 1), "O"));

        var found = Assert.IsType<GetClubMatchesResult.Found>(
            await CreateSut().ExecuteAsync("385", ClubMatchStatusFilter.Upcoming, default));

        var m = Assert.Single(found.Listing.Matches);
        Assert.Equal("u", m.MatchId);
    }
}
