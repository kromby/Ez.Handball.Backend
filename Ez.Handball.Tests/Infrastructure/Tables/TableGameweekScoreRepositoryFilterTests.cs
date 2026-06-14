using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableGameweekScoreRepositoryFilterTests
{
    [Fact]
    public void BuildTeamPartitionFilters_Empty_ReturnsEmpty()
    {
        var filters = TableGameweekScoreRepository.BuildTeamPartitionFilters(Array.Empty<string>(), 15);
        Assert.Empty(filters);
    }

    [Fact]
    public void BuildTeamPartitionFilters_WithinBatch_SingleFilterWithAllIds()
    {
        var ids = new[] { "a:fantasy", "b:fantasy", "c:fantasy" };
        var filters = TableGameweekScoreRepository.BuildTeamPartitionFilters(ids, 15);
        Assert.Single(filters);
        Assert.Equal(
            "PartitionKey eq 'a:fantasy' or PartitionKey eq 'b:fantasy' or PartitionKey eq 'c:fantasy'",
            filters[0]);
    }

    [Fact]
    public void BuildTeamPartitionFilters_ExceedingBatch_SplitsIntoChunks_CoveringAllIds()
    {
        var ids = Enumerable.Range(0, 16).Select(i => $"t{i}:fantasy").ToArray();
        var filters = TableGameweekScoreRepository.BuildTeamPartitionFilters(ids, 15);
        Assert.Equal(2, filters.Count);
        foreach (var id in ids)
            Assert.Single(filters, f => f.Contains($"PartitionKey eq '{id}'"));
    }

    [Fact]
    public void BuildTeamPartitionFilters_EscapesSingleQuotes()
    {
        var filters = TableGameweekScoreRepository.BuildTeamPartitionFilters(new[] { "o'brien:fantasy" }, 15);
        Assert.Contains("PartitionKey eq 'o''brien:fantasy'", filters[0]);
    }
}
