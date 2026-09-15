using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SetPlayerPositionResult
{
    public sealed record Ok : SetPlayerPositionResult { public static readonly Ok Instance = new(); }
    public sealed record InvalidPosition : SetPlayerPositionResult { public static readonly InvalidPosition Instance = new(); }
    public sealed record PlayerNotFound : SetPlayerPositionResult { public static readonly PlayerNotFound Instance = new(); }
}

public interface ISetPlayerPositionUseCase
{
    Task<SetPlayerPositionResult> ExecuteAsync(
        string playerId, string position, string? positionSecondary, CancellationToken ct);
}

public class SetPlayerPositionUseCase : ISetPlayerPositionUseCase
{
    private readonly IPlayerRepository _players;

    public SetPlayerPositionUseCase(IPlayerRepository players)
    {
        _players = players;
    }

    public async Task<SetPlayerPositionResult> ExecuteAsync(
        string playerId, string position, string? positionSecondary, CancellationToken ct)
    {
        if (!PositionVocabulary.Codes.Contains(position))
            return SetPlayerPositionResult.InvalidPosition.Instance;

        if (positionSecondary is not null && !PositionVocabulary.Codes.Contains(positionSecondary))
            return SetPlayerPositionResult.InvalidPosition.Instance;

        var updated = await _players.SetPositionAsync(playerId, position, positionSecondary ?? string.Empty, ct);
        return updated ? SetPlayerPositionResult.Ok.Instance : SetPlayerPositionResult.PlayerNotFound.Instance;
    }
}
