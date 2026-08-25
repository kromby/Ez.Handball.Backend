using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Infrastructure.BlobAccess;

internal sealed class BlobMatchScheduleRepository : IMatchScheduleRepository
{
    private readonly IBlobReader _blobs;

    public BlobMatchScheduleRepository(IBlobReader blobs) => _blobs = blobs;

    public async Task<MatchSchedule?> GetAsync(string tournamentId, CancellationToken ct)
    {
        var blob = await _blobs.ReadAsync($"tournaments/{tournamentId}/matches.json", ct);
        if (blob is null) return null;

        var response = JsonSerializer.Deserialize<RawMatchListResponse>(blob.Text);
        var matches = (response?.Data ?? new List<RawMatchSummary>()).Select(Map).ToList();
        return new MatchSchedule(matches, blob.LastModifiedUtc);
    }

    private static ScheduledMatch Map(RawMatchSummary m) => new(
        MatchId: m.GameId,
        Round: m.Round,
        Date: ParseDate(m.GameDayTime),
        Venue: string.IsNullOrWhiteSpace(m.StadiumName) ? null : m.StadiumName.Trim(),
        HomeTeamName: m.HomeTeamName,
        AwayTeamName: m.AwayTeamName,
        HsiStatus: m.Status);

    // hsi.is list dates carry no timezone offset; Iceland has no DST, so the local
    // wall-clock value is numerically identical to UTC (mirrors MatchParser's
    // AssumeUniversal handling of the equivalent field on the match-details endpoint).
    private static DateTimeOffset ParseDate(string raw) =>
        DateTimeOffset.TryParseExact(
            raw, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;

    // Mirrors Ez.Handball.Ingestion.Models.MatchListResponse — duplicated here rather than
    // shared, since this project doesn't reference the Functions project.
    private sealed class RawMatchListResponse
    {
        [JsonPropertyName("data")]
        public List<RawMatchSummary> Data { get; set; } = new();
    }

    private sealed class RawMatchSummary
    {
        [JsonPropertyName("GameId")] public string GameId { get; set; } = string.Empty;
        [JsonPropertyName("Round")] public string Round { get; set; } = string.Empty;
        [JsonPropertyName("GameDayTime")] public string GameDayTime { get; set; } = string.Empty;
        [JsonPropertyName("HomeTeamName")] public string HomeTeamName { get; set; } = string.Empty;
        [JsonPropertyName("AwayTeamName")] public string AwayTeamName { get; set; } = string.Empty;
        [JsonPropertyName("Status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("StadiumName")] public string StadiumName { get; set; } = string.Empty;
    }
}
