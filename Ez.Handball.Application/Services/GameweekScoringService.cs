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
            ? FantasyPointsCalculator.Whole(_rating.Compute(new PlayerRatingInputs(id, s, ruleSet,
                new PlayerRatingContext(null, null, null, ruleSet.Version, null, null))).Rating)
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
        // Frozen pins only (#166): the snapshot slot's PlayAs, when still one of the player's natural
        // positions. The live SquadPlayer pin is ignored so post-deadline moves can't touch locked rounds.
        var frozenPins = snapshot.Slots.GroupBy(s => s.PlayerId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().PlayAs, StringComparer.Ordinal); // first-wins: tolerate duplicate slots
        var eligibleById = ownedSquad.ToDictionary(p => p.PlayerId, p =>
        {
            var natural = p.NaturalPositions;
            return frozenPins.TryGetValue(p.PlayerId, out var pin) && pin is not null && natural.Contains(pin, StringComparer.Ordinal)
                ? (IReadOnlyList<string>)new[] { pin }
                : natural;
        });

        // Position each original starter was solved into; a starter's absence only needs its own
        // assigned position refilled, not every position it is merely eligible for.
        var assigned = PositionAssignment.TrySolve(
            starters.Select(s => new PositionCandidate(s.PlayerId, EligibleOf(s.PlayerId))).ToList(),
            constraints.PositionStart);

        IReadOnlyList<string> EligibleOf(string playerId)
            => eligibleById.TryGetValue(playerId, out var eligible) ? eligible : Array.Empty<string>();

        IReadOnlyList<string> MinPositions(string starterId)
        {
            if (assigned is not null && assigned.TryGetValue(starterId, out var pos) && pos is not null)
                return new[] { pos };
            return EligibleOf(starterId);
        }

        // Build an ordered list of effective (playing) ids decided before the current slot, used
        // to check position validity for sub candidates.
        var decidedEffective = new List<string>();

        // Positions already-filled slots cover. Later subs must keep them covered, or the solver
        // could shuffle a dual-position sub off the slot it was brought on to fill.
        var coveredPositions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var starter in starters)
        {
            if (Played(starter.PlayerId))
            {
                effectiveMap[starter.PlayerId] = starter.PlayerId;
                decidedEffective.Add(starter.PlayerId);
                coveredPositions.UnionWith(MinPositions(starter.PlayerId));
                continue;
            }

            // Non-playing starter: look for a valid bench sub.
            var required = coveredPositions.Union(MinPositions(starter.PlayerId)).ToList();
            var sub = FindValidSub(
                required, decidedEffective, bench, usedBench, eligibleById, constraints, Played);

            if (sub is not null)
            {
                usedBench.Add(sub);
                subbedInIds.Add(sub);
                replacedStarterIds.Add(starter.PlayerId);
                effectiveMap[starter.PlayerId] = sub;
                decidedEffective.Add(sub);
                coveredPositions.UnionWith(MinPositions(starter.PlayerId));
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
    // position-valid (see KeepsPositionsValid for the solver-based check, #108).
    private static string? FindValidSub(
        IReadOnlyList<string> minPositions,
        IReadOnlyList<string> decidedEffective,
        IReadOnlyList<LineupSlot> bench,
        HashSet<string> usedBench,
        IReadOnlyDictionary<string, IReadOnlyList<string>> eligibleById,
        LineupConstraints constraints,
        Func<string, bool> played)
    {
        foreach (var b in bench)
        {
            if (usedBench.Contains(b.PlayerId) || !played(b.PlayerId)) continue;
            if (KeepsPositionsValid(
                    minPositions, b.PlayerId, decidedEffective, eligibleById, constraints))
                return b.PlayerId;
        }
        return null;
    }

    // After the swap, the decided-effective players plus the candidate must still be arrangeable
    // (primary or secondary, #108) with no position over its max, and every position in
    // minPositions — the outgoing starter's plus those already-filled slots cover — must still
    // reach its min. Future slots' mins aren't enforced: those starters aren't decided yet.
    private static bool KeepsPositionsValid(
        IReadOnlyList<string> minPositions,
        string candidateId,
        IReadOnlyList<string> decidedEffective,
        IReadOnlyDictionary<string, IReadOnlyList<string>> eligibleById,
        LineupConstraints constraints)
    {
        IReadOnlyList<string> Eligible(string id)
            => eligibleById.TryGetValue(id, out var e) ? e : Array.Empty<string>();

        var limits = constraints.PositionStart.ToDictionary(
            kv => kv.Key,
            kv => (minPositions.Contains(kv.Key) ? kv.Value.Min : 0, kv.Value.Max),
            StringComparer.Ordinal);

        var players = decidedEffective.Append(candidateId)
            .Select(id => new PositionCandidate(id, Eligible(id)))
            .ToList();
        return PositionAssignment.TrySolve(players, limits) is not null;
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
