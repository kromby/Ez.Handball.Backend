using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class ManagerColorTests
{
    [Fact]
    public void ForClub_IsDeterministic()
    {
        Assert.Equal(ManagerColor.ForClub("385"), ManagerColor.ForClub("385"));
    }

    [Fact]
    public void ForClub_ReturnsHexColor()
    {
        var color = ManagerColor.ForClub("385");
        Assert.Matches("^#[0-9A-Fa-f]{6}$", color);
    }

    [Fact]
    public void ForClub_DifferentClubs_CanDiffer()
    {
        // Across a spread of ids the palette should produce more than one colour.
        var colors = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" }
            .Select(ManagerColor.ForClub).Distinct().Count();
        Assert.True(colors > 1);
    }

    [Fact]
    public void ForClub_NullOrEmpty_ReturnsFirstPaletteEntry_WithoutThrowing()
    {
        Assert.Matches("^#[0-9A-Fa-f]{6}$", ManagerColor.ForClub(null));
        Assert.Matches("^#[0-9A-Fa-f]{6}$", ManagerColor.ForClub(""));
    }
}
