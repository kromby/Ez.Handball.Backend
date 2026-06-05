using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableSeasonRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableSeasonRepository CreateSut() => new(_query.Object);

    private void SetupSeasons(params SeasonEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<SeasonEntity>(
                Ez.Handball.Infrastructure.Tables.Seasons, "PartitionKey eq 'season'", default))
              .Returns(ToAsync(rows));

    private static SeasonEntity Season(string label, int startYear, bool isCurrent = false) =>
        new() { PartitionKey = "season", RowKey = label, StartYear = startYear, IsCurrent = isCurrent };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListAsync_OrdersByStartYearDescending()
    {
        SetupSeasons(
            Season("2023-24", 2023),
            Season("2025-26", 2025),
            Season("2024-25", 2024));

        var result = await CreateSut().ListAsync(default);

        Assert.Equal(new[] { "2025-26", "2024-25", "2023-24" }, result.Select(s => s.Label).ToArray());
    }

    [Fact]
    public async Task ListAsync_MapsLabelAndIsCurrent()
    {
        SetupSeasons(
            Season("2025-26", 2025, isCurrent: true),
            Season("2024-25", 2024, isCurrent: false));

        var result = await CreateSut().ListAsync(default);

        Assert.True(result.Single(s => s.Label == "2025-26").IsCurrent);
        Assert.False(result.Single(s => s.Label == "2024-25").IsCurrent);
    }

    [Fact]
    public async Task ListAsync_NoRows_ReturnsEmpty()
    {
        SetupSeasons();

        Assert.Empty(await CreateSut().ListAsync(default));
    }
}
