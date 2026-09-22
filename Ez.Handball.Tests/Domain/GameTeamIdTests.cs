using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class GameTeamIdTests
{
    [Theory]
    [InlineData("u1:fantasy", "u1")]
    [InlineData(":fantasy", null)]
    [InlineData("u1:manager", null)]
    [InlineData("u1", null)]
    public void UserIdOf_InvertsFor(string teamId, string? expected)
    {
        Assert.Equal(expected, GameTeamId.UserIdOf(teamId, GameFlavor.Fantasy));
    }
}
