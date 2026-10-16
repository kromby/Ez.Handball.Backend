using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

// Which gameweek a squad/lineup change landed on, echoed by every mutation that the lock can defer.
// It's read after the commit, so a failure here must not fail the request: a 500 would invite a
// retry that hits duplicate_player / not_in_squad. Degrade to "unknown" (both fields null) instead,
// same reasoning as TransferEcho (#178). currentGameweekLocked is null rather than false because we
// don't know: false would claim an editable gameweek exists, which the normal path never pairs with
// a null appliedToGameweek.
public sealed class GameweekEcho
{
    private readonly IGetCurrentGameweekUseCase _gameweek;
    private readonly ILogger<GameweekEcho> _logger;

    public GameweekEcho(IGetCurrentGameweekUseCase gameweek, ILogger<GameweekEcho> logger)
    {
        _gameweek = gameweek;
        _logger = logger;
    }

    public async Task<object> BuildAsync(string userId, CancellationToken ct)
    {
        try
        {
            var r = await _gameweek.ExecuteAsync(null, ct);
            var current = (r as GetCurrentGameweekResult.Found)?.Current;
            return new { appliedToGameweek = current?.Number, currentGameweekLocked = (bool?)(current is null) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read the current gameweek for user {UserId}", userId);
            return new { appliedToGameweek = (int?)null, currentGameweekLocked = (bool?)null };
        }
    }
}
