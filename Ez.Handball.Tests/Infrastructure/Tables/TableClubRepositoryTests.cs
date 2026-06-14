using Azure.Data.Tables;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableClubRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    // ExistsAsync uses TableServiceClient directly; ListAsync uses ITableQuery.
    // A bare TableServiceClient is fine here because no test exercises ExistsAsync.
    private TableClubRepository CreateSut() =>
        new(new TableServiceClient("UseDevelopmentStorage=true"), _query.Object);

    private void SetupClubs(params ClubEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<ClubEntity>(
                Ez.Handball.Infrastructure.Tables.Clubs, "PartitionKey eq 'club'", default))
              .Returns(ToAsync(rows));

    private void SetupClubById(string clubId, params ClubEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<ClubEntity>(
                Ez.Handball.Infrastructure.Tables.Clubs,
                $"PartitionKey eq 'club' and RowKey eq '{clubId}'", default))
              .Returns(ToAsync(rows));

    private static ClubEntity Club(string clubId, string name, string? logoSrc = null) =>
        new() { PartitionKey = "club", RowKey = clubId, Name = name, LogoSrc = logoSrc };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListAsync_OrdersByNameCaseInsensitive()
    {
        SetupClubs(
            Club("3", "Valur"),
            Club("1", "afturelding"),
            Club("2", "Fram"));

        var result = await CreateSut().ListAsync(default);

        Assert.Equal(new[] { "afturelding", "Fram", "Valur" }, result.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { "1", "2", "3" }, result.Select(c => c.ClubId).ToArray());
    }

    [Fact]
    public async Task ListAsync_OrdersByIcelandicCollation()
    {
        // Icelandic alphabet places Þ, Æ, Ö after Z (in that order). Ordinal would
        // instead give Æ, Ö, Þ by code point — this asserts the culture-aware order.
        SetupClubs(
            Club("4", "Ægir"),
            Club("1", "Akureyri"),
            Club("3", "Þór"),
            Club("2", "Valur"),
            Club("5", "Ölver"));

        var result = await CreateSut().ListAsync(default);

        Assert.Equal(
            new[] { "Akureyri", "Valur", "Þór", "Ægir", "Ölver" },
            result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task ListAsync_MapsLogoSrcToLogoUrl()
    {
        SetupClubs(Club("385", "KR", "https://logo/kr.png"));

        var club = Assert.Single(await CreateSut().ListAsync(default));

        Assert.Equal("385", club.ClubId);
        Assert.Equal("KR", club.Name);
        Assert.Equal("https://logo/kr.png", club.LogoUrl);
    }

    [Fact]
    public async Task ListAsync_MissingOrEmptyLogo_LogoUrlIsNull()
    {
        SetupClubs(
            Club("1", "Fram", logoSrc: null),
            Club("2", "Valur", logoSrc: ""));

        var result = await CreateSut().ListAsync(default);

        Assert.Null(result.Single(c => c.ClubId == "1").LogoUrl);
        Assert.Null(result.Single(c => c.ClubId == "2").LogoUrl);
    }

    [Fact]
    public async Task ListAsync_NoRows_ReturnsEmpty()
    {
        SetupClubs();

        Assert.Empty(await CreateSut().ListAsync(default));
    }

    [Fact]
    public async Task GetByIdAsync_Exists_MapsFields()
    {
        SetupClubById("385", Club("385", "KR", "https://logo/kr.png"));

        var club = await CreateSut().GetByIdAsync("385", default);

        Assert.NotNull(club);
        Assert.Equal("385", club!.ClubId);
        Assert.Equal("KR", club.Name);
        Assert.Equal("https://logo/kr.png", club.LogoUrl);
    }

    [Fact]
    public async Task GetByIdAsync_MissingLogo_LogoUrlIsNull()
    {
        SetupClubById("1", Club("1", "Fram", logoSrc: ""));

        var club = await CreateSut().GetByIdAsync("1", default);

        Assert.NotNull(club);
        Assert.Null(club!.LogoUrl);
    }

    [Fact]
    public async Task GetByIdAsync_NoRow_ReturnsNull()
    {
        SetupClubById("999");

        Assert.Null(await CreateSut().GetByIdAsync("999", default));
    }
}
