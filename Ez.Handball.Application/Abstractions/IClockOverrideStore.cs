namespace Ez.Handball.Application.Abstractions;

// Write seam for the debug virtual-clock override row that GameClock reads (#96). Write-only:
// reads happen via the synchronous GameClock point-read, not here.
public interface IClockOverrideStore
{
    // Upsert the virtual `now` as an ISO-8601 UTC instant.
    Task SetAsync(DateTimeOffset utc, CancellationToken ct);

    // Delete the override row. Absent row → no override (wall clock). Safe if already absent.
    Task ClearAsync(CancellationToken ct);
}
