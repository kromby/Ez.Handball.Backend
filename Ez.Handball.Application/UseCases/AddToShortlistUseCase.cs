using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public abstract record AddToShortlistResult
{
    public sealed record Added : AddToShortlistResult;
    public sealed record AlreadyPresent : AddToShortlistResult;
    public sealed record CapReached(int Max) : AddToShortlistResult;
    public sealed record PlayerNotFound : AddToShortlistResult;
}

public interface IAddToShortlistUseCase
{
    Task<AddToShortlistResult> ExecuteAsync(string userId, string playerId, CancellationToken ct);
}

public sealed class AddToShortlistUseCase : IAddToShortlistUseCase
{
    private readonly IShortlistRepository _shortlist;
    private readonly IPlayerRepository _players;
    private readonly ShortlistSettings _settings;
    private readonly Func<DateTimeOffset> _now;

    public AddToShortlistUseCase(
        IShortlistRepository shortlist,
        IPlayerRepository players,
        ShortlistSettings settings,
        Func<DateTimeOffset> now)
    {
        _shortlist = shortlist;
        _players = players;
        _settings = settings;
        _now = now;
    }

    public async Task<AddToShortlistResult> ExecuteAsync(string userId, string playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new AddToShortlistResult.PlayerNotFound();

        var existing = await _shortlist.GetAsync(userId, playerId, ct);
        if (existing is { DeletedAt: null }) return new AddToShortlistResult.AlreadyPresent();

        var active = await _shortlist.CountActiveAsync(userId, ct);
        if (active >= _settings.MaxSize) return new AddToShortlistResult.CapReached(_settings.MaxSize);

        // Creates a new row or reactivates a soft-deleted one (CreatedAt reset to now).
        await _shortlist.UpsertAsync(userId, playerId, _now(), deletedAt: null, ct);
        return new AddToShortlistResult.Added();
    }
}
