using Ez.Handball.Domain;
using Xunit;

namespace Ez.Handball.Tests.Domain;

public class PositionVocabularyTests
{
    [Fact]
    public void Codes_ContainsExactlySevenCodes()
    {
        Assert.Equal(
            new[] { "CB", "GK", "LB", "LP", "LW", "RB", "RW" },
            PositionVocabulary.Codes.OrderBy(c => c));
    }
}
