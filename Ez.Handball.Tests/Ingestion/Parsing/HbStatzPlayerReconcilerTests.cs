using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzPlayerReconcilerTests
{
    private static PlayerEntity Player(string id, string name, string? jersey) => new()
    {
        RowKey = id, Name = name, JerseyNumber = jersey
    };

    private static HbStatzPlayerLine Line(string name, int? number) => new()
    {
        Name = name, Number = number
    };

    [Fact]
    public void Resolve_JerseyAndNameMatch_ReturnsPlayerId()
    {
        var roster = new List<PlayerEntity> { Player("p1", "Arnór Snær Óskarsson", "6") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Arnór Snær Óskarsson", 6));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_NameOnlyMatch_WhenJerseyDiffers_ReturnsPlayerId()
    {
        var roster = new List<PlayerEntity> { Player("p1", "Arnór Snær Óskarsson", "6") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Arnór Snær Óskarsson", 99));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_NameMatchIsCaseAndWhitespaceInsensitive()
    {
        var roster = new List<PlayerEntity> { Player("p1", " Arnór Snær Óskarsson ", "6") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("arnór snær óskarsson", null));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_AmbiguousName_FallsBackToUniqueJersey()
    {
        var roster = new List<PlayerEntity>
        {
            Player("p1", "Jón Jónsson", "6"),
            Player("p2", "Jón Jónsson", "9"),
        };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Jón Jónsson", 9));

        Assert.Equal("p2", result);
    }

    [Fact]
    public void Resolve_AmbiguousNameAndJersey_ReturnsNull()
    {
        var roster = new List<PlayerEntity>
        {
            Player("p1", "Jón Jónsson", "6"),
            Player("p2", "Jón Jónsson", "9"),
        };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Jón Jónsson", 12));

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NoMatchAtAll_ReturnsNull()
    {
        var roster = new List<PlayerEntity> { Player("p1", "Jón Jónsson", "6") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Einhver Annar", 40));

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NameMatchesDespiteMissingDiacritics_ReturnsPlayerId()
    {
        // Our roster stores this name with the accent dropped (an hsi.is export quirk);
        // HBStatz sends the correctly accented form.
        var roster = new List<PlayerEntity> { Player("p1", "Bjarni I Selvindi", "4") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Bjarni í Selvindi", 4));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_NameMatchDisambiguatesAJerseyNumberSharedByTwoRosterPlayers()
    {
        // Two roster entries share jersey #4 (a stale prior-season row alongside the current
        // player) — only the accent-insensitive name match should resolve it, not the jersey.
        var roster = new List<PlayerEntity>
        {
            Player("p1", "Bjarni I Selvindi", "4"),
            Player("p2", "Finnur Ingi Stefánsson", "4"),
        };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Bjarni í Selvindi", 4));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_IcelandicSpecialLettersFoldForComparison()
    {
        var roster = new List<PlayerEntity> { Player("p1", "Þór Þórsson", "10") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Thor Thorsson", 10));

        Assert.Equal("p1", result);
    }

    [Fact]
    public void Resolve_UnknownNameWithUniqueJersey_ReturnsNull_RatherThanTrustingTheJerseyAlone()
    {
        // A jersey number alone is not evidence of identity: it can be reused season to season,
        // and HBStatz may list someone entirely absent from our roster.
        var roster = new List<PlayerEntity> { Player("p1", "Jón Jónsson", "6") };

        var result = HbStatzPlayerReconciler.Resolve(roster, Line("Completely Different Name", 6));

        Assert.Null(result);
    }
}
