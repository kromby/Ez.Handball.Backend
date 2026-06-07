using System.Text.Json;
using Ez.Handball.Api.Serialization;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Api.Serialization;

public class TournamentTypeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TournamentTypeJsonConverter() }
    };

    [Fact]
    public void Serializes_ToLowercaseString()
    {
        Assert.Equal("\"league\"", JsonSerializer.Serialize(TournamentType.League, Options));
        Assert.Equal("\"playoffs\"", JsonSerializer.Serialize(TournamentType.Playoffs, Options));
        Assert.Equal("\"cup\"", JsonSerializer.Serialize(TournamentType.Cup, Options));
    }

    [Fact]
    public void Deserializes_FromLowercaseString()
    {
        Assert.Equal(TournamentType.Playoffs,
            JsonSerializer.Deserialize<TournamentType>("\"playoffs\"", Options));
    }

    [Fact]
    public void Deserialize_UnknownValue_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TournamentType>("\"bogus\"", Options));
    }
}
