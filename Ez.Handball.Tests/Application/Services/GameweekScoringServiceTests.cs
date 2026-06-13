using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekScoringServiceTests
{
    // 1 point per goal, 1 per appearance, no card penalties — keeps arithmetic obvious.
    private static readonly ScoringRuleSet Rules =
        new(GameFlavor.Fantasy, 1, GoalPoints: 1, YellowCardPoints: 0, TwoMinutePoints: 0, RedCardPoints: 0, AppearancePoints: 1);

    private static readonly LineupConstraints Constraints = new(
        Version: 1, StarterCount: 2,
        PositionStart: new Dictionary<string, (int Min, int Max)>
        {
            ["GK"] = (1, 1),
            ["FP"] = (1, 2),
        },
        CaptainMultiplier: 2, CaptainRequired: true, ViceRequired: false);

    private GameweekScoringService CreateSut() => new(new FantasyPlayerRatingFunction());

    private static SquadPlayer Owned(string id, string pos) =>
        new(id, $"name-{id}", "1", "Club", pos, "karlar",
            new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), 0);

    private static AggregatedStats Played(int goals) => new(Games: 1, Goals: goals, 0, 0, 0);

    // A 2-starter (GK + FP) + 1-bench lineup: captain is the FP starter, bench is an FP.
    private static Lineup Snapshot() => new(new[]
    {
        new LineupSlot("gk1", LineupRole.Starter, null),
        new LineupSlot("fp1", LineupRole.Captain, null),
        new LineupSlot("fp2", LineupRole.Bench, 0),
    });

    private static IReadOnlyList<SquadPlayer> Squad() => new[]
    {
        Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("fp2", "FP"),
    };

    [Fact]
    public void AllPlayed_CaptainDoubled()
    {
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk1"] = Played(0), // 1 (appearance)
            ["fp1"] = Played(3), // 4 raw → captain ×2 = 8
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        Assert.Equal("fp1", score.CaptainPlayerId);
        Assert.Equal(1 + 8, score.Points);
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp1").CaptainApplied);
    }

    [Fact]
    public void NonPlayingStarter_AutoSubbedByValidBench()
    {
        // fp1 (captain starter) didn't play; bench fp2 played and is an FP → valid promotion.
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk1"] = Played(0), // 1
            ["fp2"] = Played(2), // 3 raw, promoted into the FP slot
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        var fp2 = score.Breakdown.Single(b => b.PlayerId == "fp2");
        Assert.True(fp2.AutoSubbedIn);
        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "fp1").Points); // benched starter scores 0
        // captain didn't play and vice not set → no multiplier applied anywhere
        Assert.Equal(1 + 3, score.Points);
    }

    [Fact]
    public void CaptainDidNotPlay_ViceInheritsMultiplier()
    {
        var snapshot = new Lineup(new[]
        {
            new LineupSlot("gk1", LineupRole.Captain, null),
            new LineupSlot("fp1", LineupRole.Vice, null),
            new LineupSlot("fp2", LineupRole.Bench, 0),
        });
        var stats = new Dictionary<string, AggregatedStats>
        {
            // gk1 (captain) did NOT play; fp1 (vice) played → vice gets ×2.
            ["fp1"] = Played(3), // 4 raw × 2 = 8
            ["fp2"] = Played(0), // bench promoted for gk1? gk1 is GK; fp2 is FP → invalid GK sub, gk1 scores 0
        };

        var score = CreateSut().Score("team", "1", snapshot, Squad(), stats, Rules, Constraints);

        Assert.Equal("fp1", score.CaptainPlayerId); // vice became effective captain
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp1").CaptainApplied);
        // gk1 (GK) couldn't be subbed by an FP bench → 0; total = vice 8 only
        Assert.Equal(8, score.Points);
    }

    [Fact]
    public void GkOnlyReplacedByGk_NoEligibleSub_ScoresZero()
    {
        // gk1 didn't play; only bench is fp2 (FP) → promoting it would break the exactly-1-GK rule.
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["fp1"] = Played(2), // 3, captain ×2 = 6
            ["fp2"] = Played(5), // not eligible to cover a GK
        };

        var score = CreateSut().Score("team", "1", Snapshot(), Squad(), stats, Rules, Constraints);

        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "gk1").Points);
        Assert.False(score.Breakdown.Single(b => b.PlayerId == "fp2").AutoSubbedIn);
        Assert.Equal(6, score.Points);
    }

    [Fact]
    public void MultipleNonPlayingStarters_BothAutoSubbedByValidBench()
    {
        // gk1 (GK) and fp1 (FP) both didn't play; bench gk2 (GK) and fp2 (FP) both played →
        // each non-playing starter is replaced by a same-position bench player.
        var snapshot = new Lineup(new[]
        {
            new LineupSlot("gk1", LineupRole.Starter, null),
            new LineupSlot("fp1", LineupRole.Captain, null),
            new LineupSlot("gk2", LineupRole.Bench, 0),
            new LineupSlot("fp2", LineupRole.Bench, 1),
        });
        var squad = new[] { Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("gk2", "GK"), Owned("fp2", "FP") };
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk2"] = Played(0), // 1 appearance
            ["fp2"] = Played(2), // 3
        };

        var score = CreateSut().Score("team", "1", snapshot, squad, stats, Rules, Constraints);

        Assert.True(score.Breakdown.Single(b => b.PlayerId == "gk2").AutoSubbedIn);
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp2").AutoSubbedIn);
        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "gk1").Points);
        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "fp1").Points);
        Assert.Null(score.CaptainPlayerId);  // captain fp1 didn't play, no vice
        Assert.Equal(1 + 3, score.Points);
    }

    [Fact]
    public void CaptainAutoSubbedOut_ArmbandFallsToVice()
    {
        // StarterCount=3 (1 GK + 2 FP). Captain fp1 didn't play but has a valid same-position bench
        // sub (fp3); vice fp2 played → the armband falls to the vice, the sub scores un-multiplied.
        var constraints = new LineupConstraints(
            Version: 1, StarterCount: 3,
            PositionStart: new Dictionary<string, (int Min, int Max)> { ["GK"] = (1, 1), ["FP"] = (1, 3) },
            CaptainMultiplier: 2, CaptainRequired: true, ViceRequired: false);
        var snapshot = new Lineup(new[]
        {
            new LineupSlot("gk1", LineupRole.Starter, null),
            new LineupSlot("fp1", LineupRole.Captain, null),
            new LineupSlot("fp2", LineupRole.Vice, null),
            new LineupSlot("fp3", LineupRole.Bench, 0),
        });
        var squad = new[] { Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("fp2", "FP"), Owned("fp3", "FP") };
        var stats = new Dictionary<string, AggregatedStats>
        {
            ["gk1"] = Played(0), // 1
            ["fp2"] = Played(3), // 4 raw × 2 (vice armband) = 8
            ["fp3"] = Played(1), // 2 (subbed in for fp1, no multiplier)
        };

        var score = CreateSut().Score("team", "1", snapshot, squad, stats, Rules, constraints);

        Assert.Equal("fp2", score.CaptainPlayerId);
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp2").CaptainApplied);
        Assert.True(score.Breakdown.Single(b => b.PlayerId == "fp3").AutoSubbedIn);
        Assert.Equal(0, score.Breakdown.Single(b => b.PlayerId == "fp1").Points); // captain replaced
        Assert.Equal(1 + 8 + 2, score.Points);
    }
}
