using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetManagerUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private readonly Mock<ISquadRepository> _squad = new();

    private GetManagerUseCase Sut() => new(_teams.Object, _users.Object, _constraints.Object, _squad.Object);

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private void Team() => _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GameTeam("u-1:fantasy", "Dream Team", "#1E88E5", T0));
    private void User() => _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UserEntity { RowKey = "u-1", FavoriteClubId = "385" });
    private void Constraints(int maxSize) => _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SquadConstraints(1, maxSize, new Dictionary<string, int>(), 100_000_000, "ISK"));
    private void OwnedPlayers(int count)
    {
        var slots = Enumerable.Range(0, count)
            .Select(i => new SquadSlot($"p-{i}", "VS", new PlayerCost(1, "ISK"))).ToList();
        _squad.Setup(s => s.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Squad(slots, 0, "ISK"));
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync((GameTeam?)null);

        var result = await Sut().ExecuteAsync("u-1", null, default);

        Assert.IsType<GetManagerResult.NoTeam>(result);
    }

    [Fact]
    public async Task MissingRuleSet_ReturnsRuleSetNotFound()
    {
        Team(); User();
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((SquadConstraints?)null);

        var result = await Sut().ExecuteAsync("u-1", null, default);

        Assert.IsType<GetManagerResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task PartialSquad_NotComplete()
    {
        Team(); User(); Constraints(15); OwnedPlayers(9);

        var result = await Sut().ExecuteAsync("u-1", null, default);

        var found = Assert.IsType<GetManagerResult.Found>(result);
        Assert.Equal("Dream Team", found.View.TeamName);
        Assert.Equal("385", found.View.FavoriteClubId);
        Assert.Equal("#1E88E5", found.View.Color);
        Assert.False(found.View.Onboarding.SquadComplete);
        Assert.Equal(9, found.View.Onboarding.PlayersOwned);
        Assert.Equal(15, found.View.Onboarding.SquadSize);
    }

    [Fact]
    public async Task FullSquad_Complete()
    {
        Team(); User(); Constraints(15); OwnedPlayers(15);

        var result = await Sut().ExecuteAsync("u-1", null, default);

        var found = Assert.IsType<GetManagerResult.Found>(result);
        Assert.True(found.View.Onboarding.SquadComplete);
        Assert.Equal(15, found.View.Onboarding.PlayersOwned);
    }
}
