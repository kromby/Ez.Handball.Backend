using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Infrastructure.Ingestion;

internal sealed class HttpIngestionTrigger : IIngestionTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly IngestionSettings _settings;

    public HttpIngestionTrigger(HttpClient http, IngestionSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<SyncTriggerResult> TriggerSyncAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sync");
        if (!string.IsNullOrEmpty(_settings.FunctionKey))
            request.Headers.Add("x-functions-key", _settings.FunctionKey);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new SyncTriggerResult(false, 0, Array.Empty<string>(), $"ingestion_returned_{(int)response.StatusCode}");

            var body = await response.Content.ReadFromJsonAsync<IngestionSyncResponse>(JsonOptions, ct);
            IReadOnlyList<string> failed = body?.Failed ?? new List<string>();
            return new SyncTriggerResult(true, body?.Synced ?? 0, failed, null);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SyncTriggerResult(false, 0, Array.Empty<string>(), "ingestion_unreachable");
        }
        catch (JsonException)
        {
            return new SyncTriggerResult(false, 0, Array.Empty<string>(), "ingestion_returned_malformed_body");
        }
    }

    public async Task<HbStatzSyncTriggerResult> TriggerHbStatzSyncAsync(
        string? tournamentId, string? round, string? matchId, CancellationToken ct)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(tournamentId)) queryParams.Add($"tournamentId={Uri.EscapeDataString(tournamentId)}");
        if (!string.IsNullOrWhiteSpace(round)) queryParams.Add($"round={Uri.EscapeDataString(round)}");
        if (!string.IsNullOrWhiteSpace(matchId)) queryParams.Add($"matchId={Uri.EscapeDataString(matchId)}");
        var query = queryParams.Count > 0 ? $"?{string.Join('&', queryParams)}" : string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/hbstatz/sync{query}");
        if (!string.IsNullOrEmpty(_settings.FunctionKey))
            request.Headers.Add("x-functions-key", _settings.FunctionKey);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new HbStatzSyncTriggerResult(
                    false, 0, 0, Array.Empty<string>(), Array.Empty<string>(), $"ingestion_returned_{(int)response.StatusCode}");

            var body = await response.Content.ReadFromJsonAsync<HbStatzSyncResponse>(JsonOptions, ct);
            IReadOnlyList<string> unmatched = body?.Unmatched ?? new List<string>();
            IReadOnlyList<string> failed = body?.Failed ?? new List<string>();
            return new HbStatzSyncTriggerResult(
                true, body?.MatchesChecked ?? 0, body?.MatchesSynced ?? 0, unmatched, failed, null);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HbStatzSyncTriggerResult(
                false, 0, 0, Array.Empty<string>(), Array.Empty<string>(), "ingestion_unreachable");
        }
        catch (JsonException)
        {
            return new HbStatzSyncTriggerResult(
                false, 0, 0, Array.Empty<string>(), Array.Empty<string>(), "ingestion_returned_malformed_body");
        }
    }

    // Mirrors Ez.Handball.Ingestion.Functions.SyncResult on the wire.
    private sealed class IngestionSyncResponse
    {
        public int Synced { get; set; }
        public List<string> Failed { get; set; } = new();
    }

    // Mirrors Ez.Handball.Ingestion.Functions.HbStatzSyncResult on the wire.
    private sealed class HbStatzSyncResponse
    {
        public int MatchesChecked { get; set; }
        public int MatchesSynced { get; set; }
        public List<string> Unmatched { get; set; } = new();
        public List<string> Failed { get; set; } = new();
    }
}
