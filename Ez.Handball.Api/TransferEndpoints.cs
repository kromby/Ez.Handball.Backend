using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this WebApplication app)
    {
        // Public community data — no auth.
        app.MapGet("/api/transfers/trends", async (
            string? flavor,
            string? window,
            IGetTransferTrendsUseCase uc,
            CancellationToken ct) =>
        {
            // Fantasy-only: blank or "fantasy" is accepted; anything else is rejected.
            if (!IsFantasy(flavor))
                return Results.BadRequest(new { error = "invalid_flavor" });

            var result = await uc.ExecuteAsync(GameFlavor.Fantasy, window, ct);
            return result switch
            {
                TransferTrendsResult.InvalidWindow => Results.BadRequest(new { error = "invalid_window" }),
                TransferTrendsResult.Ok ok         => Results.Ok(new
                {
                    mostSigned = ok.Trends.MostSigned.Select(Body),
                    mostDropped = ok.Trends.MostDropped.Select(Body)
                }),
                _                                  => Results.Problem()
            };
        });
    }

    private static bool IsFantasy(string? flavor)
        => string.IsNullOrWhiteSpace(flavor) || flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase);

    private static object Body(TransferTrendEntry e) => new
    {
        playerId = e.PlayerId,
        name = e.Name,
        clubName = e.ClubName,
        count = e.Count
    };
}
