using Ez.Handball.Ingestion.Functions;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedTournamentsFunctionTests
{
    [Theory]
    [InlineData("1",       10)]
    [InlineData("1-final", 20)]
    [InlineData("2",       30)]
    [InlineData("2-final", 40)]
    [InlineData("cup",     50)]
    public void TournamentDefinitions_AssignExpectedPriorityForDivision(string division, int expectedPriority)
    {
        var matching = SeedTournamentsFunction.TournamentDefinitions
            .Where(d => d.Division == division)
            .ToList();

        Assert.NotEmpty(matching);
        Assert.All(matching, d => Assert.Equal(expectedPriority, d.Priority));
    }
}
