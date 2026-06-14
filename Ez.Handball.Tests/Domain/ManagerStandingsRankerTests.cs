using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class ManagerStandingsRankerTests
{
    private static IReadOnlyDictionary<string, (string Name, string Color)> Names(
        params (string teamId, string name)[] entries)
        => entries.ToDictionary(e => e.teamId, e => (e.name, "#abcdef"));

    [Fact]
    public void Rank_EmptyInput_ReturnsEmptyWithNoLatestRound()
    {
        var result = ManagerStandingsRanker.Rank(
            Array.Empty<GameweekScoreSummary>(),
            Names());

        Assert.Null(result.LatestRoundLabel);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Rank_OrdersByTotalDescending_WithRoundPointsFromLatestRound()
    {
        var summaries = new[]
        {
            new GameweekScoreSummary("a:fantasy", "1", 30),
            new GameweekScoreSummary("a:fantasy", "2", 40),
            new GameweekScoreSummary("b:fantasy", "1", 50),
            new GameweekScoreSummary("b:fantasy", "2", 35),
        };

        var result = ManagerStandingsRanker.Rank(summaries, Names(("a:fantasy", "Alpha"), ("b:fantasy", "Bravo")));

        Assert.Equal("2", result.LatestRoundLabel);
        Assert.Equal(2, result.Entries.Count);
        // a: 70 total (round 2 = 40); b: 85 total (round 2 = 35)
        Assert.Equal("b:fantasy", result.Entries[0].TeamId);
        Assert.Equal(1, result.Entries[0].Rank);
        Assert.Equal(85, result.Entries[0].TotalPoints);
        Assert.Equal(35, result.Entries[0].RoundPoints);
        Assert.Equal("a:fantasy", result.Entries[1].TeamId);
        Assert.Equal(2, result.Entries[1].Rank);
        Assert.Equal(40, result.Entries[1].RoundPoints);
    }

    [Fact]
    public void Rank_TiedTotals_ShareRank_OrderedByNameThenTeamId()
    {
        var summaries = new[]
        {
            new GameweekScoreSummary("z:fantasy", "1", 50),
            new GameweekScoreSummary("a:fantasy", "1", 50),
            new GameweekScoreSummary("m:fantasy", "1", 10),
        };

        var result = ManagerStandingsRanker.Rank(
            summaries, Names(("z:fantasy", "Zeta"), ("a:fantasy", "Alpha"), ("m:fantasy", "Mike")));

        // Two tied for rank 1, ordered Alpha before Zeta; next is rank 3.
        Assert.Equal("a:fantasy", result.Entries[0].TeamId);
        Assert.Equal(1, result.Entries[0].Rank);
        Assert.Equal("z:fantasy", result.Entries[1].TeamId);
        Assert.Equal(1, result.Entries[1].Rank);
        Assert.Equal("m:fantasy", result.Entries[2].TeamId);
        Assert.Equal(3, result.Entries[2].Rank);
    }

    [Fact]
    public void Rank_SingleRound_HasNoPreviousRankOrDelta()
    {
        var summaries = new[]
        {
            new GameweekScoreSummary("a:fantasy", "1", 30),
            new GameweekScoreSummary("b:fantasy", "1", 50),
        };

        var result = ManagerStandingsRanker.Rank(summaries, Names(("a:fantasy", "Alpha"), ("b:fantasy", "Bravo")));

        Assert.All(result.Entries, e => Assert.Null(e.PreviousRank));
        Assert.All(result.Entries, e => Assert.Null(e.RankDelta));
    }

    [Fact]
    public void Rank_MovementUp_HasPositiveDelta()
    {
        // After round 1: a=50 (rank 1), b=20 (rank 2).
        // After round 2: a=50+5=55, b=20+60=80 → b climbs to rank 1, a drops to rank 2.
        var summaries = new[]
        {
            new GameweekScoreSummary("a:fantasy", "1", 50),
            new GameweekScoreSummary("b:fantasy", "1", 20),
            new GameweekScoreSummary("a:fantasy", "2", 5),
            new GameweekScoreSummary("b:fantasy", "2", 60),
        };

        var result = ManagerStandingsRanker.Rank(summaries, Names(("a:fantasy", "Alpha"), ("b:fantasy", "Bravo")));

        var b = result.Entries.Single(e => e.TeamId == "b:fantasy");
        var a = result.Entries.Single(e => e.TeamId == "a:fantasy");
        Assert.Equal(1, b.Rank);
        Assert.Equal(2, b.PreviousRank);
        Assert.Equal(1, b.RankDelta);   // climbed from 2 to 1
        Assert.Equal(2, a.Rank);
        Assert.Equal(1, a.PreviousRank);
        Assert.Equal(-1, a.RankDelta);  // dropped from 1 to 2
    }

    [Fact]
    public void Rank_NewEntrantInLatestRound_HasNullPreviousRank_AndZeroLatestRoundPointsForLateSettler()
    {
        // a settled rounds 1 and 2; c only appears in round 2 (new entrant).
        // b settled round 1 only → has a total but no round-2 score (roundPoints = 0).
        var summaries = new[]
        {
            new GameweekScoreSummary("a:fantasy", "1", 40),
            new GameweekScoreSummary("a:fantasy", "2", 10),
            new GameweekScoreSummary("b:fantasy", "1", 30),
            new GameweekScoreSummary("c:fantasy", "2", 90),
        };

        var result = ManagerStandingsRanker.Rank(
            summaries, Names(("a:fantasy", "Alpha"), ("b:fantasy", "Bravo"), ("c:fantasy", "Charlie")));

        var c = result.Entries.Single(e => e.TeamId == "c:fantasy");
        Assert.Null(c.PreviousRank);    // not present through round 1
        Assert.Null(c.RankDelta);
        Assert.Equal(90, c.RoundPoints);

        var b = result.Entries.Single(e => e.TeamId == "b:fantasy");
        Assert.Equal(30, b.TotalPoints);
        Assert.Equal(0, b.RoundPoints);  // settled round 1, not the latest round
    }

    [Fact]
    public void Rank_MissingNameMapEntry_FallsBackToTeamId()
    {
        var summaries = new[] { new GameweekScoreSummary("ghost:fantasy", "1", 10) };

        var result = ManagerStandingsRanker.Rank(summaries, Names());

        Assert.Equal("ghost:fantasy", result.Entries[0].TeamName);
        Assert.Equal("", result.Entries[0].Color);
    }
}
