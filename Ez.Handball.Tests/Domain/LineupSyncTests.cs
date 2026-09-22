using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class LineupSyncTests
{
    private static LineupSlot Starter(string id) => new(id, LineupRole.Starter, null);
    private static LineupSlot Bench(string id, int order) => new(id, LineupRole.Bench, order);

    [Fact]
    public void WithoutPlayer_Captain_RemovesSlotAndLeavesNoCaptain()
    {
        var lineup = new Lineup(new[]
        {
            new LineupSlot("cap", LineupRole.Captain, null),
            new LineupSlot("vice", LineupRole.Vice, null),
            Starter("s1"),
        });

        var result = LineupSync.WithoutPlayer(lineup, "cap");

        Assert.DoesNotContain(result.Slots, s => s.PlayerId == "cap");
        Assert.DoesNotContain(result.Slots, s => s.Role == LineupRole.Captain);
        Assert.Contains(result.Slots, s => s.PlayerId == "vice" && s.Role == LineupRole.Vice);
        Assert.Equal(2, result.Slots.Count);
    }

    [Fact]
    public void WithoutPlayer_BenchPlayer_KeepsBenchOrderContiguous()
    {
        var lineup = new Lineup(new[] { Starter("s1"), Bench("b0", 0), Bench("b1", 1), Bench("b2", 2) });

        var result = LineupSync.WithoutPlayer(lineup, "b1");

        Assert.Equal(new[] { Starter("s1"), Bench("b0", 0), Bench("b2", 1) }, result.Slots);
    }

    [Fact]
    public void WithoutPlayer_NotInLineup_ReturnsSameSlots()
    {
        var lineup = new Lineup(new[] { Starter("s1"), Bench("b0", 0) });

        var result = LineupSync.WithoutPlayer(lineup, "nobody");

        Assert.Equal(lineup.Slots, result.Slots);
    }

    [Fact]
    public void WithStarter_AddsPlayerAsStarter()
    {
        var lineup = new Lineup(new[] { new LineupSlot("vice", LineupRole.Vice, null), Starter("s1") });

        var result = LineupSync.WithStarter(lineup, "new");

        Assert.Contains(result.Slots, s => s == Starter("new"));
        Assert.Equal(3, result.Slots.Count);
    }

    [Fact]
    public void WithStarter_AlreadyInLineup_ReturnsSameSlots()
    {
        var lineup = new Lineup(new[] { new LineupSlot("cap", LineupRole.Captain, null) });

        var result = LineupSync.WithStarter(lineup, "cap");

        Assert.Equal(lineup.Slots, result.Slots);
    }
}
