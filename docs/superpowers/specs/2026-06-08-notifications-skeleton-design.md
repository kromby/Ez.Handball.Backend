# Notifications Infrastructure (Skeleton) — Design

**Issue:** [#18](https://github.com/kromby/Ez.Handball.Backend/issues/18)
**Date:** 2026-06-08
**Status:** Approved

## Goal

Lay the rails for notifications so email and push can be plugged in later without
rewriting. Skeleton only — no real delivery providers, no templates, no inbox, no HTTP
surface. The deliverable is a working in-process dispatch path proven end-to-end by an
in-memory consumer in tests.

## Acceptance criteria (from the issue)

- Notification type enum (mini-league update, round result, system message).
- Per-user notification preferences (which channels and types).
- A producer interface and an in-memory consumer for tests.

## Settled design decisions

- **Scope:** pure dispatch rails. Type enum + preference model + producer/consumer
  interfaces + dispatcher + in-memory test consumer. The only persistence is the
  preference store (durable user state). No notification inbox, no HTTP endpoints, no
  real call sites.
- **Dispatch:** in-process synchronous fan-out. The publisher resolves the user's
  preferences and synchronously invokes each enabled channel. No queue infrastructure.
- **Preference model:** full type×channel matrix — each (type, channel) cell is
  independently enabled/disabled.

## Components

### 1. Domain (`Ez.Handball.Domain/`)

Two C# enums (clean to enumerate for matrix defaults; persisted as their string name):

```csharp
public enum NotificationType { MiniLeagueUpdate, RoundResult, SystemMessage }
public enum NotificationChannel { InApp, Email, Push }
```

The transient event a producer emits (no Id/timestamps — there is no store; plain
strings because templates are out of scope):

```csharp
public sealed record Notification(
    string UserId,
    NotificationType Type,
    string Title,
    string Body);
```

The preference matrix:

```csharp
public sealed record NotificationPreferences(
    string UserId,
    IReadOnlySet<(NotificationType Type, NotificationChannel Channel)> Enabled)
{
    public bool IsEnabled(NotificationType t, NotificationChannel c) => Enabled.Contains((t, c));

    public static NotificationPreferences Default(string userId);
}
```

`Default` returns `InApp` enabled for all three types; `Email`/`Push` off (no providers
yet). Defaults are computed, not written to storage until the user customizes.

### 2. Producer + consumer contracts (`Ez.Handball.Application/Abstractions/`)

```csharp
public interface INotificationPublisher          // the producer
{
    Task PublishAsync(Notification notification, CancellationToken ct);
}

public interface INotificationChannel            // the consumer (one per delivery channel)
{
    NotificationChannel Channel { get; }         // which cell it answers to
    Task SendAsync(Notification notification, CancellationToken ct);
}
```

### 3. Dispatcher (`Ez.Handball.Application/Services/`)

`NotificationPublisher : INotificationPublisher`, injected with
`INotificationPreferenceRepository` and `IEnumerable<INotificationChannel>` (all
registered channels). On `PublishAsync`:

1. Load the user's preferences (falling back to `Default` when none stored).
2. For each registered channel, if `prefs.IsEnabled(notification.Type, channel.Channel)`,
   `await channel.SendAsync(...)`.
3. A channel that throws is caught and logged; it must not break delivery to the other
   channels.

### 4. Preference storage (`Abstractions/` + `Infrastructure/TableAccess/` + `Shared/Entities/`)

Preferences are durable user state — the one persisted piece in scope.

- `INotificationPreferenceRepository`: `GetAsync(userId, ct)` / `UpsertAsync(prefs, ct)`.
- `TableNotificationPreferenceRepository` over a new `Tables.NotificationPreferences` table.
- **Storage shape:** one row per enabled `(type, channel)` cell. PartitionKey = `userId`,
  RowKey = `"{type}:{channel}"`. `GetAsync` reads the partition and rebuilds the set;
  absent row = disabled. This avoids schema churn when types/channels are added later.
  `UpsertAsync` reconciles the partition to the supplied set (writes enabled cells,
  removes cells no longer present).
- **Configured-empty vs never-configured:** the interface distinguishes `null` (never
  configured → caller uses `Default`) from a non-null empty set (configured to receive
  nothing). To preserve this when every cell is disabled, `UpsertAsync` writes a single
  `"__configured__"` marker row; `GetAsync` treats the marker as "configured" but carries
  no enabled cell, so an explicit "disable all" round-trips as non-null/empty rather than
  reverting to defaults.

### 5. In-memory consumer (`Ez.Handball.Tests/`)

`InMemoryNotificationChannel : INotificationChannel` — records every `Notification` it
receives in a list. Lives in the test project. Tests register it (alone or beside fakes
for other channels), publish, and assert on what landed. This proves the rails work
end-to-end without any real provider.

### 6. DI registration (`InfrastructureRegistration.cs` / `Program.cs`)

- `INotificationPublisher` → `NotificationPublisher` (scoped).
- `INotificationPreferenceRepository` → `TableNotificationPreferenceRepository` (scoped).
- A tiny `LoggingNotificationChannel` registered for production (answers `InApp`, logs
  what it would deliver) so the wired path is observably alive. No Email/Push channels
  registered yet.

## Data flow

```text
caller → INotificationPublisher.PublishAsync(notification)
            → load NotificationPreferences (or Default)
            → for each registered INotificationChannel:
                 prefs.IsEnabled(type, channel.Channel) ? channel.SendAsync(...) : skip
                 (per-channel try/catch + log)
```

## Error handling

- Missing preferences → fall back to `NotificationPreferences.Default`.
- A throwing channel is caught and logged; other channels still receive the notification.
- Table 404 on preference read → treated as "no stored preferences" (use defaults).

## Testing

- **Unit — `NotificationPublisher`** (Moq pref repo + in-memory channel): enabled cell
  delivers, disabled cell does not, multi-channel fan-out, one throwing channel does not
  block another.
- **Unit — `NotificationPreferences`**: `Default` and `IsEnabled` logic.
- **Azurite integration — `TableNotificationPreferenceRepository`**: upsert cells → get
  rebuilds the set; absent cell = disabled; upsert reconciles removals.

## Explicitly out of scope (deferred)

- Persisted notification inbox/feed.
- HTTP endpoints (read/update preferences, fetch feed).
- SMTP / push delivery providers.
- Templates.
- Real call sites that emit notifications (e.g. on mini-league join or round result).
  The rails are laid; nothing pulls them yet.
