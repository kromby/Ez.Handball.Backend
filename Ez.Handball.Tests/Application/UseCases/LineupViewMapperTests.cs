using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.UseCases;

public class LineupViewMapperTests
{
    private static SquadPlayer Player(string id, string pos) => new(
        PlayerId: id, Name: $"N{id}", ClubId: "385", ClubName: "Stjarnan",
        Position: pos, Gender: "karlar",
        Price: new PlayerPrice(10_000_000, "ISK"), PricePaid: new PlayerPrice(9_000_000, "ISK"));

    [Fact]
    public void Map_EnrichesSlotsFromOwnedSquad()
    {
        var owned = new[] { Player("p0", "GK"), Player("p1", "LW") };
        var lineup = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Captain, null),
            new LineupSlot("p1", LineupRole.Bench, 0),
        });
        var constraints = new LineupConstraints(1, 7,
            new Dictionary<string, (int, int)>(), CaptainMultiplier: 2, CaptainRequired: true, ViceRequired: false);
        var validation = new LineupValidation(true, Array.Empty<LineupViolation>());

        var view = LineupViewMapper.Map(owned, lineup, constraints, validation);

        Assert.Equal(2, view.Slots.Count);
        Assert.Equal(2, view.CaptainMultiplier);
        Assert.True(view.IsValid);
        var captain = view.Slots.Single(s => s.Role == LineupRole.Captain);
        Assert.Equal("p0", captain.PlayerId);
        Assert.Equal("GK", captain.Position);
        Assert.Equal("Np0", captain.Name);
        Assert.Equal(10_000_000, captain.Price!.Amount);
    }

    [Fact]
    public void Map_UnownedSlot_HasNullEnrichment()
    {
        var owned = new[] { Player("p0", "GK") };
        var lineup = new Lineup(new[]
        {
            new LineupSlot("p0", LineupRole.Starter, null),
            new LineupSlot("ghost", LineupRole.Bench, 0),
        });
        var constraints = new LineupConstraints(1, 7,
            new Dictionary<string, (int, int)>(), 2, true, false);
        var validation = new LineupValidation(false,
            new[] { new LineupViolation("unowned_player", "x") });

        var view = LineupViewMapper.Map(owned, lineup, constraints, validation);

        var ghost = view.Slots.Single(s => s.PlayerId == "ghost");
        Assert.Null(ghost.Name);
        Assert.Null(ghost.Position);
        Assert.Null(ghost.Price);
        Assert.False(view.IsValid);
    }
}
