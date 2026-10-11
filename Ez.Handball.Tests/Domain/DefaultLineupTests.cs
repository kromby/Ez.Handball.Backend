using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class DefaultLineupTests
{
    private static readonly LineupConstraints Constraints = new(
        1, 7, new Dictionary<string, (int, int)>
        {
            ["GK"] = (1, 1), ["LW"] = (0, 2), ["RW"] = (0, 2), ["LB"] = (0, 2),
            ["CB"] = (0, 2), ["RB"] = (0, 2), ["LP"] = (0, 2),
        }, 2, true, false);

    private static SquadPlayer Player(string id, string? position, double rating) =>
        new(id, id, "1", "Club", position, "karlar", new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), rating);

    private static IEnumerable<string> Ids(Lineup lineup, LineupRole role) =>
        lineup.Slots.Where(s => s.Role == role).Select(s => s.PlayerId);

    [Fact]
    public void SevenPlayerSquad_AllStart_NoCaptain()
    {
        var squad = new[]
        {
            Player("gk", "GK", 1), Player("lw", "LW", 2), Player("rw", "RW", 3), Player("lb", "LB", 4),
            Player("cb", "CB", 5), Player("rb", "RB", 6), Player("lp", "LP", 7),
        };

        var lineup = DefaultLineup.From(squad, Constraints);

        Assert.Equal(7, Ids(lineup, LineupRole.Starter).Count());
        Assert.Empty(Ids(lineup, LineupRole.Bench));
        Assert.DoesNotContain(lineup.Slots, s => s.Role is LineupRole.Captain or LineupRole.Vice);
    }

    [Fact]
    public void LargerSquad_StartsHighestRated_RespectingPositionMax_BenchesRestByRating()
    {
        var squad = new[]
        {
            Player("gk-best", "GK", 9), Player("gk-2", "GK", 8),     // only one keeper may start
            Player("lw1", "LW", 7), Player("lw2", "LW", 6), Player("lw3", "LW", 5), // LW max 2
            Player("cb1", "CB", 4), Player("rb1", "RB", 3), Player("lp1", "LP", 2), Player("rw1", "RW", 1),
        };

        var lineup = DefaultLineup.From(squad, Constraints);

        Assert.Equal(new[] { "gk-best", "lw1", "lw2", "cb1", "rb1", "lp1", "rw1" }, Ids(lineup, LineupRole.Starter));
        Assert.Equal(new[] { "gk-2", "lw3" }, Ids(lineup, LineupRole.Bench));
        Assert.Equal(new int?[] { 0, 1 }, lineup.Slots.Where(s => s.Role == LineupRole.Bench).Select(s => s.BenchOrder));
    }

    [Fact]
    public void UnknownOrMissingPosition_IsNotBlockedByPositionLimits()
    {
        var lineup = DefaultLineup.From(new[] { Player("a", null, 2), Player("b", "FP", 1) }, Constraints);

        Assert.Equal(new[] { "a", "b" }, Ids(lineup, LineupRole.Starter));
    }

    [Fact]
    public void EmptySquad_EmptyLineup()
    {
        Assert.Empty(DefaultLineup.From(Array.Empty<SquadPlayer>(), Constraints).Slots);
    }

    private static SquadPlayer DualPlayer(string id, string pos, string sec, double rating) => Player(id, pos, rating) with { PositionSecondary = sec };

    [Fact]
    public void DualPlayer_StartsAtSecondary_WhenPrimaryAtMax()
    {
        var squad = new[]
        {
            Player("gk", "GK", 9), Player("cb1", "CB", 8), Player("cb2", "CB", 7),
            DualPlayer("cb3", "CB", "LB", 6),                       // CB max 2 → starts at LB
            Player("lw", "LW", 5), Player("rw", "RW", 4), Player("lp", "LP", 3), Player("rb", "RB", 2),
        };

        var lineup = DefaultLineup.From(squad, Constraints);

        Assert.Contains("cb3", Ids(lineup, LineupRole.Starter));
        Assert.Equal(new[] { "rb" }, Ids(lineup, LineupRole.Bench));
    }

    [Fact]
    public void DualPlayers_SameInput_SameLineup()
    {
        var squad = new[]
        {
            Player("gk", "GK", 9), DualPlayer("a", "CB", "LB", 8), DualPlayer("b", "CB", "RB", 8),
            Player("c", "CB", 8), Player("lw", "LW", 1),
        };

        Assert.Equal(DefaultLineup.From(squad, Constraints).Slots, DefaultLineup.From(squad, Constraints).Slots);
    }
}
