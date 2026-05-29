using Ez.Handball.Ingestion.Parsing;
using Microsoft.Azure.Functions.Worker;

namespace Ez.Handball.Ingestion.Functions;

public class ParseMatchFunction
{
    private readonly IMatchParser _matchParser;

    public ParseMatchFunction(IMatchParser matchParser)
    {
        _matchParser = matchParser;
    }

    [Function("ParseMatch")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/details.json", Connection = "HandballStorageConnection")] string blobContent,
        string matchId,
        FunctionContext context)
    {
        await _matchParser.ParseAsync(blobContent, matchId, context.CancellationToken);
    }
}
