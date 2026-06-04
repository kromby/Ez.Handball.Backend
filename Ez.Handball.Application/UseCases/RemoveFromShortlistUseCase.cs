using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public interface IRemoveFromShortlistUseCase
{
    Task ExecuteAsync(string userId, string playerId, CancellationToken ct);
}

public sealed class RemoveFromShortlistUseCase : IRemoveFromShortlistUseCase
{
    private readonly IShortlistRepository _shortlist;
    private readonly Func<DateTimeOffset> _now;

    public RemoveFromShortlistUseCase(IShortlistRepository shortlist, Func<DateTimeOffset> now)
    {
        _shortlist = shortlist;
        _now = now;
    }

    public async Task ExecuteAsync(string userId, string playerId, CancellationToken ct)
    {
        var existing = await _shortlist.GetAsync(userId, playerId, ct);
        if (existing is null || existing.DeletedAt is not null) return; // idempotent no-op
        await _shortlist.UpsertAsync(userId, playerId, existing.CreatedAt, deletedAt: _now(), ct);
    }
}
