using System.Net;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record ReparseError(string Blob, string Message);

public record ReparseResult(int MatchesReparsed, int PlayerFilesReparsed, IReadOnlyList<ReparseError> Errors);

public class ReparseFunction
{
    private readonly IBlobArchiver _blobArchiver;
    private readonly IMatchParser _matchParser;
    private readonly IPlayerParser _playerParser;

    public ReparseFunction(IBlobArchiver blobArchiver, IMatchParser matchParser, IPlayerParser playerParser)
    {
        _blobArchiver = blobArchiver;
        _matchParser = matchParser;
        _playerParser = playerParser;
    }

    [Function("Reparse")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "reparse")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<ReparseFunction>();
        var result = await ReparseAsync(req.Query["matchId"], logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<ReparseResult> ReparseAsync(
        string? matchId, ILogger? logger = null, CancellationToken ct = default)
    {
        var prefix = string.IsNullOrWhiteSpace(matchId) ? "matches/" : $"matches/{matchId}/";

        var details = new List<string>();
        var players = new List<string>();
        await foreach (var name in _blobArchiver.ListAsync(prefix, ct))
        {
            if (name.EndsWith("/details.json", StringComparison.Ordinal))
                details.Add(name);
            else if (name.Contains("/players-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
                players.Add(name);
        }

        var errors = new List<ReparseError>();
        var matchesReparsed = 0;
        var playerFilesReparsed = 0;

        // Pass 1: details first, so player parsing finds its Matches row.
        foreach (var blob in details)
        {
            try
            {
                var content = await _blobArchiver.ReadAsync(blob, ct);
                await _matchParser.ParseAsync(content, ExtractMatchId(blob), ct);
                matchesReparsed++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Reparse failed for blob {Blob}", blob);
                errors.Add(new ReparseError(blob, ex.Message));
            }
        }

        // Pass 2: players.
        foreach (var blob in players)
        {
            try
            {
                var content = await _blobArchiver.ReadAsync(blob, ct);
                await _playerParser.ParseAsync(content, ExtractMatchId(blob), ExtractClubId(blob), ct);
                playerFilesReparsed++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Reparse failed for blob {Blob}", blob);
                errors.Add(new ReparseError(blob, ex.Message));
            }
        }

        return new ReparseResult(matchesReparsed, playerFilesReparsed, errors);
    }

    // "matches/{matchId}/details.json" or "matches/{matchId}/players-{clubId}.json"
    private static string ExtractMatchId(string blobPath)
    {
        var parts = blobPath.Split('/');
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    // "matches/{matchId}/players-{clubId}.json" -> clubId
    private static string ExtractClubId(string blobPath)
    {
        var file = blobPath.Split('/')[^1];                 // players-{clubId}.json
        const string prefix = "players-";
        const string suffix = ".json";
        if (file.StartsWith(prefix, StringComparison.Ordinal) && file.EndsWith(suffix, StringComparison.Ordinal))
            return file[prefix.Length..^suffix.Length];
        return string.Empty;
    }
}
