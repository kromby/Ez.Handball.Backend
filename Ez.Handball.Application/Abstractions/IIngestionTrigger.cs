namespace Ez.Handball.Application.Abstractions;

public sealed record SyncTriggerResult(bool Success, int Synced, IReadOnlyList<string> Failed, string? Error);

public sealed record HbStatzSyncTriggerResult(
    bool Success, int MatchesChecked, int MatchesSynced,
    IReadOnlyList<string> Unmatched, IReadOnlyList<string> Failed, string? Error);

// Triggers a full sync on the Ingestion Functions app (a separate deployable) over HTTP.
// This project never runs the sync pipeline itself — Ingestion owns that logic; this is
// purely a remote trigger + result relay for the admin UI.
public interface IIngestionTrigger
{
    Task<SyncTriggerResult> TriggerSyncAsync(CancellationToken ct);

    // tournamentId null => every tournament with IngestHbStatz == true. round/matchId scope to
    // a specific round or single match within that tournament (tournamentId required for
    // either) and force a re-sync even if already synced — see TriggerHbStatzSyncFunction.
    Task<HbStatzSyncTriggerResult> TriggerHbStatzSyncAsync(
        string? tournamentId, string? round, string? matchId, CancellationToken ct);
}
