using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Application.UseCases;

public interface ITriggerIngestionSyncUseCase
{
    Task<SyncTriggerResult> ExecuteAsync(CancellationToken ct);
}

public class TriggerIngestionSyncUseCase : ITriggerIngestionSyncUseCase
{
    private readonly IIngestionTrigger _trigger;

    public TriggerIngestionSyncUseCase(IIngestionTrigger trigger) => _trigger = trigger;

    public Task<SyncTriggerResult> ExecuteAsync(CancellationToken ct) => _trigger.TriggerSyncAsync(ct);
}
