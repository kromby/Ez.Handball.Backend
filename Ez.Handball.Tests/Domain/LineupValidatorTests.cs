using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class LineupValidatorTests
{
    private static readonly LineupConstraints Constraints = new(
        Version: 1,
        StarterCount: 7,
        PositionStart: new Dictionary<string, (int, int)>
        {
            ["GK"] = (1, 1),
            ["LW"] = (0, 2), ["RW"] = (0, 2),
            ["LB"] = (0, 3), ["CB"] = (0, 2), ["RB"] = (0, 3),
            ["LP"] = (0, 2),
        },
        CaptainMultiplier: 2,
        CaptainRequired: true,
        ViceRequired: false);

    // Build an owned squad of N players with the given positions; playerId = "p{index}".
    private static IReadOnlyList<SquadPlayer> Owned(params string[] positions)
        => positions.Select((pos, i) => new SquadPlayer(
            PlayerId: $"p{i}", Name: $"Name{i}", ClubId: "385", ClubName: "Stjarnan",
            Position: pos, Gender: "karlar",
            Price: new PlayerPrice(10_000_000, "ISK"),
            PricePaid: new PlayerPrice(10_000_000, "ISK"), Rating: 0)).ToList();

    // A valid 8-player squad: 1 GK + 6 court starters + 1 bench court player.
    private static readonly string[] EightPositions =
        { "GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB" };

    // Place p0..p6 as the 7 starters (p0 captain), p7 as bench[0].
    private static Lineup ValidLineup() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Starter, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Starter, null),
        new LineupSlot("p4", LineupRole.Starter, null),
        new LineupSlot("p5", LineupRole.Starter, null),
        new LineupSlot("p6", LineupRole.Starter, null),
        new LineupSlot("p7", LineupRole.Bench, 0),
    });

    private static bool Has(LineupValidation v, string code) => v.Violations.Any(x => x.Code == code);

    [Fact]
    public void ValidLineup_IsValid_NoViolations()
    {
        var result = LineupValidator.Validate(ValidLineup(), Owned(EightPositions), Constraints);
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void UnownedPlayer_Flagged()
    {
        var lineup = new Lineup(ValidLineup().Slots
            .Select(s => s.PlayerId == "p7" ? s with { PlayerId = "ghost" } : s).ToList());
        var result = LineupValidator.Validate(lineup, Owned(EightPositions), Constraints);
        Assert.False(result.IsValid);
        Assert.True(Has(result, "unowned_player"));
    }

    [Fact]
    public void IncompleteSquad_WhenOwnedPlayerNotPlaced_Flagged()
    {
        // Owned squad has 9 players, lineup only places 8.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB", "LW");
        var result = LineupValidator.Validate(ValidLineup(), owned, Constraints);
        Assert.True(Has(result, "incomplete_squad"));
    }

    [Fact]
    public void DuplicatePlayer_Flagged()
    {
        var lineup = new Lineup(ValidLineup().Slots
            .Select(s => s.PlayerId == "p7" ? s with { PlayerId = "p6" } : s).ToList());
        var result = LineupValidator.Validate(lineup, Owned(EightPositions), Constraints);
        Assert.True(Has(result, "duplicate_slot"));
    }

    [Fact]
    public void WrongStarterCount_Flagged()
    {
        // Demote p6 to bench → only 6 starters. Give it a bench order alongside p7.
        var slots = ValidLineup().Slots.Select(s =>
            s.PlayerId == "p6" ? new LineupSlot("p6", LineupRole.Bench, 1) : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "wrong_starter_count"));
    }

    [Fact]
    public void NoGoalkeeperStarter_FlagsPositionMin()
    {
        // Replace the GK starter with a second-CB starter; bench the GK.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB");
        var slots = new[]
        {
            new LineupSlot("p7", LineupRole.Captain, null),  // CB starts instead of GK
            new LineupSlot("p1", LineupRole.Starter, null),
            new LineupSlot("p2", LineupRole.Starter, null),
            new LineupSlot("p3", LineupRole.Starter, null),
            new LineupSlot("p4", LineupRole.Starter, null),
            new LineupSlot("p5", LineupRole.Starter, null),
            new LineupSlot("p6", LineupRole.Starter, null),
            new LineupSlot("p0", LineupRole.Bench, 0),       // GK benched
        };
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "position_min"));   // GK min=1 unmet
    }

    [Fact]
    public void TooManyAtPosition_FlagsPositionMax()
    {
        // Owned squad has 3 LW; start all 3 (LW max=2). 1 GK + 3 LW + 3 others = 7 starters.
        var owned = Owned("GK", "LW", "LW", "LW", "CB", "RB", "LP", "LB");
        var slots = new[]
        {
            new LineupSlot("p0", LineupRole.Captain, null), // GK
            new LineupSlot("p1", LineupRole.Starter, null), // LW
            new LineupSlot("p2", LineupRole.Starter, null), // LW
            new LineupSlot("p3", LineupRole.Starter, null), // LW
            new LineupSlot("p4", LineupRole.Starter, null), // CB
            new LineupSlot("p5", LineupRole.Starter, null), // RB
            new LineupSlot("p6", LineupRole.Starter, null), // LP
            new LineupSlot("p7", LineupRole.Bench, 0),      // LB
        };
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "position_max"));
    }

    [Fact]
    public void MissingCaptain_WhenRequired_Flagged()
    {
        var slots = ValidLineup().Slots.Select(s =>
            s.Role == LineupRole.Captain ? s with { Role = LineupRole.Starter } : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "missing_captain"));
    }

    [Fact]
    public void MultipleCaptains_Flagged()
    {
        var slots = ValidLineup().Slots.Select(s =>
            s.PlayerId == "p1" ? s with { Role = LineupRole.Captain } : s).ToList();
        var result = LineupValidator.Validate(new Lineup(slots), Owned(EightPositions), Constraints);
        Assert.True(Has(result, "multiple_captains"));
    }

    [Fact]
    public void BenchOrderGap_Flagged()
    {
        // Owned squad of 9; two bench players with orders {0, 2} — a gap.
        var owned = Owned("GK", "LW", "RW", "LB", "CB", "RB", "LP", "CB", "LW");
        var slots = ValidLineup().Slots.ToList();
        slots.Add(new LineupSlot("p8", LineupRole.Bench, 2)); // p7=0, p8=2 → gap at 1
        var result = LineupValidator.Validate(new Lineup(slots), owned, Constraints);
        Assert.True(Has(result, "bench_order"));
    }
}
