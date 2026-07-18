using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Shared.Entities;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class PlayerReconcilerTests
{
    private readonly List<PlayerEntity> _roster =
    [
        new() { RowKey = "p-1", Name = "Arnór Snær Óskarsson", JerseyNumber = "6" },
        new() { RowKey = "p-2", Name = "Gísli Þorgeir Kristjánsson", JerseyNumber = "10" },
        new() { RowKey = "p-3", Name = "Ólafur Andrés Guðmundsson", JerseyNumber = "11" }
    ];

    [Fact]
    public void Reconcile_ExactJerseyAndName_Matches()
    {
        var line = new PlayerStatLine("home", 6, "Arnór Snær Óskarsson", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Equal("p-1", id);
    }

    [Fact]
    public void Reconcile_ExactNameWithoutJersey_Matches()
    {
        // "Arnor Snær Oskarsson" has different accents but normalizes to "arnor snær oskarsson" which is exactly equal
        var line = new PlayerStatLine("home", null, "Arnor Snær Oskarsson", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Equal("p-1", id);
    }

    [Fact]
    public void Reconcile_FuzzyNameWithoutJersey_Matches()
    {
        // "Arnor Snaer Oskarsson" has 'ae' instead of 'æ', which doesn't normalize exactly, but has similarity >= 0.9
        var line = new PlayerStatLine("home", null, "Arnor Snaer Oskarsson", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Equal("p-1", id);
    }

    [Fact]
    public void Reconcile_FuzzyNameMatch_Matches()
    {
        // "Arnór Snær Óskarsso" is missing the last 'n' (distance 1, length 20, similarity 19/20 = 0.95 >= 0.9)
        var line = new PlayerStatLine("home", null, "Arnór Snær Óskarsso", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Equal("p-1", id);
    }

    [Fact]
    public void Reconcile_UniqueJerseyMatch_Matches()
    {
        var line = new PlayerStatLine("home", 11, "Unknown Player", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Equal("p-3", id);
    }

    [Fact]
    public void Reconcile_DuplicateJerseyMatch_ReturnsNull()
    {
        var rosterWithDuplicates = new List<PlayerEntity>(_roster)
        {
            new() { RowKey = "p-4", Name = "Another Player", JerseyNumber = "11" }
        };
        var line = new PlayerStatLine("home", 11, "Unknown Player", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, rosterWithDuplicates);
        Assert.Null(id);
    }

    [Fact]
    public void Reconcile_NoMatch_ReturnsNull()
    {
        var line = new PlayerStatLine("home", 99, "No Match Person", false, 0, 0, 0, 0);
        var id = PlayerReconciler.Reconcile(line, _roster);
        Assert.Null(id);
    }
}
