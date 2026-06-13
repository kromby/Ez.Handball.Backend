using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IGameweekScoringService
{
    // Pure rollup: applies auto-subs and the captain/vice multiplier to a frozen snapshot,
    // given who played (playedStatsByPlayer; absent key = did not play).
    GameweekScore Score(
        string teamId,
        string roundLabel,
        Lineup snapshot,
        IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet,
        LineupConstraints constraints);
}

public sealed class GameweekScoringService : IGameweekScoringService
{
    private readonly FantasyPlayerRatingFunction _rating;

    public GameweekScoringService(FantasyPlayerRatingFunction rating) => _rating = rating;

    public GameweekScore Score(
        string teamId, string roundLabel, Lineup snapshot, IReadOnlyList<SquadPlayer> ownedSquad,
        IReadOnlyDictionary<string, AggregatedStats> playedStatsByPlayer,
        ScoringRuleSet ruleSet, LineupConstraints constraints)
    {
        bool Played(string id) => playedStatsByPlayer.ContainsKey(id);

        double RawPoints(string id) => playedStatsByPlayer.TryGetValue(id, out var s)
            ? _rating.Compute(new PlayerRatingInputs(id, s, ruleSet,
                new PlayerRatingContext(null, null, null, ruleSet.Version, null, null))).Rating
            : 0;

        var starters = snapshot.Slots
            .Where(s => s.Role is LineupRole.Starter or LineupRole.Captain or LineupRole.Vice)
            .ToList();
        var bench = snapshot.Slots
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .ToList();

        // effectiveStarterIds: the final set of player ids counting toward the score, in order.
        // Maps original-starter-id → effective-player-id (same if starter played, sub if subbed).
        var effectiveMap = new Dictionary<string, string>();    // starterPlayerId → effectivePlayerId
        var subbedInIds = new HashSet<string>();
        var replacedStarterIds = new HashSet<string>();
        var usedBench = new HashSet<string>();
        var posById = ownedSquad.ToDictionary(p => p.PlayerId, p => p.Position);

        // Build an ordered list of effective (playing) ids decided before the current slot, used
        // to check position validity for sub candidates.
        var decidedEffective = new List<string>();

        foreach (var starter in starters)
        {
            if (Played(starter.PlayerId))
            {
                effectiveMap[starter.PlayerId] = starter.PlayerId;
                decidedEffective.Add(starter.PlayerId);
                continue;
            }

            // Non-playing starter: look for a valid bench sub.
            var sub = FindValidSub(
                starter.PlayerId, decidedEffective, bench, usedBench, posById, constraints, Played);

            if (sub is not null)
            {
                usedBench.Add(sub);
                subbedInIds.Add(sub);
                replacedStarterIds.Add(starter.PlayerId);
                effectiveMap[starter.PlayerId] = sub;
                decidedEffective.Add(sub);
            }
            else
            {
                // No eligible sub: slot stays with the non-playing starter (scores 0).
                effectiveMap[starter.PlayerId] = starter.PlayerId;
                decidedEffective.Add(starter.PlayerId);
            }
        }

        // Effective captain: captain if they are the effective player for their slot AND played,
        // else vice if they are the effective player for their slot AND played, else nobody.
        var captainId = EffectiveArmband(effectiveMap, replacedStarterIds, LineupRole.Captain, snapshot, Played)
            ?? EffectiveArmband(effectiveMap, replacedStarterIds, LineupRole.Vice, snapshot, Played);

        var breakdown = new List<GameweekPlayerScore>();
        double total = 0;

        // Emit all starters (original). Non-playing unsubbed starters score 0; replaced starters score 0.
        foreach (var starter in starters)
        {
            var effectiveId = effectiveMap[starter.PlayerId];
            bool wasReplaced = replacedStarterIds.Contains(starter.PlayerId);

            if (!wasReplaced)
            {
                // This starter IS the effective player for their slot.
                var played = Played(starter.PlayerId);
                var raw = RawPoints(starter.PlayerId);
                var isCaptain = starter.PlayerId == captainId && played;
                var multiplier = isCaptain ? constraints.CaptainMultiplier : 1.0;
                var points = played ? raw * multiplier : 0;
                total += points;
                breakdown.Add(new GameweekPlayerScore(
                    starter.PlayerId, raw, points, played,
                    AutoSubbedIn: false, isCaptain, multiplier));
            }
            else
            {
                // Starter was replaced → they score 0.
                breakdown.Add(new GameweekPlayerScore(
                    starter.PlayerId, RawPoints: 0, Points: 0,
                    Played: false, AutoSubbedIn: false, CaptainApplied: false, Multiplier: 1.0));
            }
        }

        // Emit all bench players.
        foreach (var benchSlot in bench)
        {
            bool subbed = subbedInIds.Contains(benchSlot.PlayerId);
            if (subbed)
            {
                // Sub contributed as an effective starter.
                var played = Played(benchSlot.PlayerId);
                var raw = RawPoints(benchSlot.PlayerId);
                var isCaptain = benchSlot.PlayerId == captainId && played;
                var multiplier = isCaptain ? constraints.CaptainMultiplier : 1.0;
                var points = played ? raw * multiplier : 0;
                total += points;
                breakdown.Add(new GameweekPlayerScore(
                    benchSlot.PlayerId, raw, points, played,
                    AutoSubbedIn: true, isCaptain, multiplier));
            }
            else
            {
                // Bench player who was not called upon: 0 points regardless of whether they played.
                breakdown.Add(new GameweekPlayerScore(
                    benchSlot.PlayerId, RawPoints: 0, Points: 0,
                    Played: Played(benchSlot.PlayerId),
                    AutoSubbedIn: false, CaptainApplied: false, Multiplier: 1.0));
            }
        }

        return new GameweekScore(teamId, roundLabel, total, captainId, breakdown);
    }

    // A bench player is a valid sub for a non-playing starter if they played, are unused,
    // and replacing the non-playing starter with them keeps the whole effective lineup
    // position-valid (no position exceeds its max AND no position falls below its min after
    // the swap, given the players already decided plus all yet-to-be-processed starters).
    private static string? FindValidSub(
        string nonPlayingStarterId,
        IReadOnlyList<string> decidedEffective,
        IReadOnlyList<LineupSlot> bench,
        HashSet<string> usedBench,
        IReadOnlyDictionary<string, string?> posById,
        LineupConstraints constraints,
        Func<string, bool> played)
    {
        foreach (var b in bench)
        {
            if (usedBench.Contains(b.PlayerId) || !played(b.PlayerId)) continue;
            if (KeepsPositionsValid(
                    nonPlayingStarterId, b.PlayerId, decidedEffective, posById, constraints))
                return b.PlayerId;
        }
        return null;
    }

    // After the swap (remove the non-playing starter, add the candidate), check that no position
    // exceeds its max in the decided-effective list. Also check that the non-playing starter's
    // position is covered: the candidate fills the same position OR the position count of the
    // outgoing slot still meets the minimum when the remaining players are considered.
    //
    // Concretely: the swap is valid iff the candidate's position equals the non-playing starter's
    // position OR the non-playing starter's position still has at least (min) representatives
    // among the already-decided effective players (excluding the outgoing starter, who isn't in
    // decidedEffective, but including any future starters — which we conservatively don't count).
    //
    // The rule reduces to: a bench player can only fill a position slot if their position matches
    // the outgoing starter's position, OR the outgoing starter's position has enough coverage
    // from other already-effective players to remain at or above its minimum.
    private static bool KeepsPositionsValid(
        string nonPlayingStarterId,
        string candidateId,
        IReadOnlyList<string> decidedEffective,
        IReadOnlyDictionary<string, string?> posById,
        LineupConstraints constraints)
    {
        posById.TryGetValue(nonPlayingStarterId, out var outgoingPos);
        posById.TryGetValue(candidateId, out var incomingPos);

        // Count positions in the already-decided effective list + the candidate.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Inc(string? pos)
        {
            if (pos is not null) counts[pos] = counts.TryGetValue(pos, out var c) ? c + 1 : 1;
        }

        foreach (var id in decidedEffective) { posById.TryGetValue(id, out var p); Inc(p); }
        Inc(incomingPos);

        foreach (var kv in constraints.PositionStart)
        {
            counts.TryGetValue(kv.Key, out var count);
            // Max: adding the candidate must not overflow this position.
            if (count > kv.Value.Max) return false;
        }

        // Min for the outgoing position: the candidate fills the gap only if same position,
        // otherwise the outgoing position must already have ≥ min representatives in the
        // decided-effective list (not counting future starters — conservative check).
        if (outgoingPos is not null
            && !string.Equals(outgoingPos, incomingPos, StringComparison.Ordinal)
            && constraints.PositionStart.TryGetValue(outgoingPos, out var outgoingConstraint))
        {
            // How many of the outgoing position are already decided (excluding the outgoing
            // starter, who isn't in decidedEffective)?
            counts.TryGetValue(outgoingPos, out var countWithoutOutgoing);
            // incomingPos may equal outgoingPos — handled above, so here they differ.
            // The already-decided count for outgoingPos doesn't include the candidate.
            if (countWithoutOutgoing < outgoingConstraint.Min) return false;
        }

        return true;
    }

    // Returns the PlayerId of the armband holder IF they are still the effective player for their
    // slot (i.e. not replaced by a sub) AND they actually played. Returns null otherwise.
    private static string? EffectiveArmband(
        IReadOnlyDictionary<string, string> effectiveMap,
        IReadOnlySet<string> replacedStarterIds,
        LineupRole role,
        Lineup snapshot,
        Func<string, bool> played)
    {
        var holder = snapshot.Slots.FirstOrDefault(s => s.Role == role)?.PlayerId;
        if (holder is null) return null;
        // Armband only counts if the holder was not replaced AND actually played.
        if (replacedStarterIds.Contains(holder)) return null;
        if (!played(holder)) return null;
        return holder;
    }
}
