using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public interface ITriggerHbStatzSyncUseCase
{
    Task<HbStatzSyncTriggerResult> ExecuteAsync(
        string? tournamentId, string? round, string? matchId, CancellationToken ct);
}

public class TriggerHbStatzSyncUseCase : ITriggerHbStatzSyncUseCase
{
    private readonly IIngestionTrigger _trigger;

    public TriggerHbStatzSyncUseCase(IIngestionTrigger trigger) => _trigger = trigger;

    public Task<HbStatzSyncTriggerResult> ExecuteAsync(
        string? tournamentId, string? round, string? matchId, CancellationToken ct) =>
        _trigger.TriggerHbStatzSyncAsync(tournamentId, round, matchId, ct);
}
