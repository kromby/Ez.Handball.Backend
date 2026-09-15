using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetPlayersMissingPositionUseCase
{
    Task<IReadOnlyList<Player>> ExecuteAsync(string? clubId, string? gender, CancellationToken ct);
}

public class GetPlayersMissingPositionUseCase : IGetPlayersMissingPositionUseCase
{
    private readonly IPlayerRepository _players;

    public GetPlayersMissingPositionUseCase(IPlayerRepository players)
    {
        _players = players;
    }

    public Task<IReadOnlyList<Player>> ExecuteAsync(string? clubId, string? gender, CancellationToken ct) =>
        _players.ListMissingPositionAsync(clubId, gender, ct);
}
