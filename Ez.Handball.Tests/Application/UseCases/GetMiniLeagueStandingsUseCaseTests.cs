using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMiniLeagueStandingsUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IGameTeamRepository> _teams = new();

    private GetMiniLeagueStandingsUseCase CreateSut() => new(_leagues.Object, _scores.Object, _teams.Object);

    private static GameTeam Team(string userId, string name) =>
        new($"{userId}:fantasy", name, "#abcdef", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task MissingLeague_ReturnsNotFound()
    {
        _leagues.Setup(l => l.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("missing", 0, 50, default);

        Assert.IsType<GetMiniLeagueStandingsResult.NotFound>(result);
    }

    [Fact]
    public async Task RanksOnlyLeagueMembers()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "a", DateTimeOffset.UnixEpoch));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new MiniLeagueMember("a", MiniLeagueRoles.Creator, DateTimeOffset.UnixEpoch),
                new MiniLeagueMember("b", MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch),
            });

        List<string>? requestedTeamIds = null;
        _scores.Setup(s => s.ListSummariesByTeamsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((ids, _) => requestedTeamIds = ids.ToList())
            .ReturnsAsync(new[]
            {
                new GameweekScoreSummary("a:fantasy", "1", 30),
                new GameweekScoreSummary("b:fantasy", "1", 70),
            });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo") });

        var result = await CreateSut().ExecuteAsync("lg-1", 0, 50, default);

        var found = Assert.IsType<GetMiniLeagueStandingsResult.Found>(result);
        Assert.Equal(new[] { "a:fantasy", "b:fantasy" }, requestedTeamIds);
        Assert.Equal(2, found.Standings.Total);
        Assert.Equal("b:fantasy", found.Standings.Entries[0].TeamId);
    }

    [Fact]
    public async Task MembersWithoutSettledScores_AreRankedWithZeroPoints()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "a", DateTimeOffset.UnixEpoch));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new MiniLeagueMember("a", MiniLeagueRoles.Creator, DateTimeOffset.UnixEpoch),
                new MiniLeagueMember("b", MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch),
                new MiniLeagueMember("no-team", MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch),
            });
        _scores.Setup(s => s.ListSummariesByTeamsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new GameweekScoreSummary("b:fantasy", "1", 40) });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo") });

        var result = await CreateSut().ExecuteAsync("lg-1", 0, 50, default);

        var found = Assert.IsType<GetMiniLeagueStandingsResult.Found>(result);
        // A member with no fantasy team at all has nothing to rank and is left out.
        Assert.Equal(2, found.Standings.Total);
        Assert.Equal(("b:fantasy", 1, 40.0), (found.Standings.Entries[0].TeamId, found.Standings.Entries[0].Rank, found.Standings.Entries[0].TotalPoints));
        var alpha = found.Standings.Entries[1];
        Assert.Equal(("a:fantasy", "Alpha", 2, 0.0, 0.0), (alpha.TeamId, alpha.TeamName, alpha.Rank, alpha.TotalPoints, alpha.RoundPoints));
        Assert.Null(alpha.PreviousRank);
    }

    [Fact]
    public async Task NoSettledScoresYet_RanksEveryMemberAtZero()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "a", DateTimeOffset.UnixEpoch));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new MiniLeagueMember("a", MiniLeagueRoles.Creator, DateTimeOffset.UnixEpoch),
                new MiniLeagueMember("b", MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch),
            });
        _scores.Setup(s => s.ListSummariesByTeamsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameweekScoreSummary>());
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo") });

        var result = await CreateSut().ExecuteAsync("lg-1", 0, 50, default);

        var found = Assert.IsType<GetMiniLeagueStandingsResult.Found>(result);
        Assert.Null(found.Standings.LatestRoundLabel);
        Assert.Equal(new[] { "Alpha", "Bravo" }, found.Standings.Entries.Select(e => e.TeamName));
        Assert.All(found.Standings.Entries, e => Assert.Equal((1, 0.0), (e.Rank, e.TotalPoints)));
    }

    [Fact]
    public async Task StartRound_CountsOnlyRoundsFromIt()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "a", DateTimeOffset.UnixEpoch, StartRound: "6"));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new MiniLeagueMember("a", MiniLeagueRoles.Creator, DateTimeOffset.UnixEpoch),
                new MiniLeagueMember("b", MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch),
            });
        _scores.Setup(s => s.ListSummariesByTeamsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                // "a" ran up a big lead in rounds 1–5, before the league started counting.
                new GameweekScoreSummary("a:fantasy", "1", 100),
                new GameweekScoreSummary("a:fantasy", "5", 100),
                new GameweekScoreSummary("a:fantasy", "6", 10),
                new GameweekScoreSummary("a:fantasy", "10", 20), // numeric order, not ordinal: 10 > 6
                new GameweekScoreSummary("b:fantasy", "6", 40),
                new GameweekScoreSummary("b:fantasy", "10", 30),
            });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo") });

        var result = await CreateSut().ExecuteAsync("lg-1", 0, 50, default);

        var found = Assert.IsType<GetMiniLeagueStandingsResult.Found>(result);
        Assert.Equal("10", found.Standings.LatestRoundLabel);
        var bravo = found.Standings.Entries[0];
        Assert.Equal(("b:fantasy", 1, 70.0, 2, 35.0), (bravo.TeamId, bravo.Rank, bravo.TotalPoints, bravo.RoundsPlayed, bravo.AveragePoints));
        var alpha = found.Standings.Entries[1];
        Assert.Equal(("a:fantasy", 2, 30.0, 2, 15.0), (alpha.TeamId, alpha.Rank, alpha.TotalPoints, alpha.RoundsPlayed, alpha.AveragePoints));
        Assert.Equal(new[] { new RoundScore("6", 10, 10), new RoundScore("10", 20, 30) }, alpha.Rounds);
    }

    [Fact]
    public async Task StartRoundAfterEverySettledRound_RanksEveryMemberAtZero()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "a", DateTimeOffset.UnixEpoch, StartRound: "6"));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MiniLeagueMember("a", MiniLeagueRoles.Creator, DateTimeOffset.UnixEpoch) });
        _scores.Setup(s => s.ListSummariesByTeamsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new GameweekScoreSummary("a:fantasy", "5", 100) });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha") });

        var result = await CreateSut().ExecuteAsync("lg-1", 0, 50, default);

        var found = Assert.IsType<GetMiniLeagueStandingsResult.Found>(result);
        Assert.Null(found.Standings.LatestRoundLabel);
        var alpha = Assert.Single(found.Standings.Entries);
        Assert.Equal((0.0, 0, 0.0), (alpha.TotalPoints, alpha.RoundsPlayed, alpha.AveragePoints));
        Assert.Empty(alpha.Rounds);
    }
}
