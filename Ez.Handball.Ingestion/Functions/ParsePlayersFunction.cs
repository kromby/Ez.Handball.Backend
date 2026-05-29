using Ez.Handball.Ingestion.Parsing;
using Microsoft.Azure.Functions.Worker;

namespace Ez.Handball.Ingestion.Functions;

public class ParsePlayersFunction
{
    private readonly IPlayerParser _playerParser;

    public ParsePlayersFunction(IPlayerParser playerParser)
    {
        _playerParser = playerParser;
    }

    [Function("ParsePlayers")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/players-{clubId}.json", Connection = "HandballStorageConnection")] string blobContent,
        string matchId,
        string clubId,
        FunctionContext context)
    {
        await _playerParser.ParseAsync(blobContent, matchId, clubId, context.CancellationToken);
    }
}
