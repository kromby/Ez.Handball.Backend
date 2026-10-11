using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IEffectiveLineupService
{
    // The lineup the team plays right now: the saved lineup, or the default one (#142), with each
    // slot stamped with the player's valid position pin (#166). Null when there is nothing to play.
    Task<Lineup?> GetAsync(string teamId, IReadOnlyList<SquadPlayer> squad, LineupConstraints constraints, CancellationToken ct);
}

public sealed class EffectiveLineupService : IEffectiveLineupService
{
    private readonly ILineupRepository _saved;

    public EffectiveLineupService(ILineupRepository saved) => _saved = saved;

    public async Task<Lineup?> GetAsync(
        string teamId, IReadOnlyList<SquadPlayer> squad, LineupConstraints constraints, CancellationToken ct)
    {
        var lineup = await _saved.GetAsync(teamId, ct) ?? DefaultLineup.From(squad, constraints);
        if (lineup.Slots.Count == 0) return null;

        // Stamped here so the frozen snapshot carries the pin; a stale pin or a player no longer owned gets none.
        var pins = squad.ToDictionary(p => p.PlayerId, p => p.ValidPlayAs, StringComparer.Ordinal);
        return new Lineup(lineup.Slots
            .Select(s => s with { PlayAs = pins.TryGetValue(s.PlayerId, out var pin) ? pin : null })
            .ToList());
    }
}
