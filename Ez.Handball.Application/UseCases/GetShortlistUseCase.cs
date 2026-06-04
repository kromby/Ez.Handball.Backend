using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetShortlistUseCase
{
    Task<ShortlistView> ExecuteAsync(string userId, CancellationToken ct);
}

public sealed class GetShortlistUseCase : IGetShortlistUseCase
{
    private readonly IShortlistRepository _shortlist;
    private readonly IPlayerRepository _players;
    private readonly ShortlistSettings _settings;

    public GetShortlistUseCase(
        IShortlistRepository shortlist, IPlayerRepository players, ShortlistSettings settings)
    {
        _shortlist = shortlist;
        _players = players;
        _settings = settings;
    }

    public async Task<ShortlistView> ExecuteAsync(string userId, CancellationToken ct)
    {
        var entries = await _shortlist.ListActiveAsync(userId, ct);
        var items = new List<ShortlistPlayer>(entries.Count);
        foreach (var entry in entries)
        {
            var player = await _players.GetByIdAsync(entry.PlayerId, ct);
            items.Add(new ShortlistPlayer(
                PlayerId: entry.PlayerId,
                Name: player?.Name,
                ClubId: player?.ClubId,
                ClubName: player?.ClubName,
                Position: player?.Position,
                Gender: player?.Gender,
                Price: null,           // reserved — future pricing system
                PickPercentage: null,  // reserved — #25
                CreatedAt: entry.CreatedAt));
        }
        return new ShortlistView(items, items.Count, _settings.MaxSize);
    }
}
