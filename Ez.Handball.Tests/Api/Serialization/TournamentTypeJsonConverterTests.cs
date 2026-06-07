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

    [Theory]
    [InlineData(TournamentType.League, "\"league\"")]
    [InlineData(TournamentType.Playoffs, "\"playoffs\"")]
    [InlineData(TournamentType.Cup, "\"cup\"")]
    public void Serializes_ToLowercaseString(TournamentType type, string expectedJson)
    {
        Assert.Equal(expectedJson, JsonSerializer.Serialize(type, Options));
    }

    [Theory]
    [InlineData("\"league\"", TournamentType.League)]
    [InlineData("\"playoffs\"", TournamentType.Playoffs)]
    [InlineData("\"cup\"", TournamentType.Cup)]
    public void Deserializes_KnownValues(string json, TournamentType expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<TournamentType>(json, Options));
    }

    [Fact]
    public void Deserialize_UnknownValue_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TournamentType>("\"bogus\"", Options));
    }
}
