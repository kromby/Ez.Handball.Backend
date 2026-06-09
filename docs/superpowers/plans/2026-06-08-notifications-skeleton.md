# Notifications Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the rails for notifications — type/channel enums, a per-user type×channel preference matrix, a producer interface, an in-process fan-out dispatcher, and an in-memory test consumer — so email/push providers can be plugged in later without rewriting.

**Architecture:** Clean-architecture C# (.NET 9). Domain holds enums + records. Application holds the producer/consumer abstractions and the `NotificationPublisher` dispatcher (resolves a user's preferences, then synchronously fans out to each enabled `INotificationChannel`). Infrastructure holds the Azure Table preference repository and a placeholder `LoggingNotificationChannel`. Tests hold an `InMemoryNotificationChannel` that records what it received.

**Tech Stack:** .NET 9, Azure.Data.Tables, xUnit, Moq, Azurite (local table emulator).

**Spec:** `docs/superpowers/specs/2026-06-08-notifications-skeleton-design.md`

**Branch:** `feat/issue-18-notifications-skeleton` (already checked out).

---

## File structure

**Create:**
- `Ez.Handball.Domain/NotificationType.cs` — enum (MiniLeagueUpdate, RoundResult, SystemMessage)
- `Ez.Handball.Domain/NotificationChannel.cs` — enum (InApp, Email, Push)
- `Ez.Handball.Domain/Notification.cs` — the transient event record
- `Ez.Handball.Domain/NotificationPreferences.cs` — the type×channel matrix + `Default` + `IsEnabled`
- `Ez.Handball.Application/Abstractions/INotificationPublisher.cs` — producer interface
- `Ez.Handball.Application/Abstractions/INotificationChannel.cs` — consumer interface
- `Ez.Handball.Application/Abstractions/INotificationPreferenceRepository.cs` — preference store interface
- `Ez.Handball.Application/Services/NotificationPublisher.cs` — the in-process fan-out dispatcher
- `Ez.Handball.Shared/Entities/NotificationPreferenceEntity.cs` — one row per enabled cell
- `Ez.Handball.Infrastructure/TableAccess/TableNotificationPreferenceRepository.cs` — Table-backed store
- `Ez.Handball.Infrastructure/LoggingNotificationChannel.cs` — placeholder in-app channel
- `Ez.Handball.Tests/Fakes/InMemoryNotificationChannel.cs` — recording test consumer
- `Ez.Handball.Tests/Domain/NotificationPreferencesTests.cs`
- `Ez.Handball.Tests/Application/Services/NotificationPublisherTests.cs`
- `Ez.Handball.Tests/Infrastructure/Tables/TableNotificationPreferenceRepositoryTests.cs`

**Modify:**
- `Ez.Handball.Application/Ez.Handball.Application.csproj` — add `Microsoft.Extensions.Logging.Abstractions`
- `Ez.Handball.Infrastructure/Tables.cs` — add `NotificationPreferences` constant
- `Ez.Handball.Infrastructure/InfrastructureRegistration.cs` — register repo + logging channel
- `Ez.Handball.Api/Program.cs` — register the publisher

---

## Task 1: Notification domain types

**Files:**
- Create: `Ez.Handball.Domain/NotificationType.cs`
- Create: `Ez.Handball.Domain/NotificationChannel.cs`
- Create: `Ez.Handball.Domain/Notification.cs`
- Create: `Ez.Handball.Domain/NotificationPreferences.cs`
- Test: `Ez.Handball.Tests/Domain/NotificationPreferencesTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Ez.Handball.Tests/Domain/NotificationPreferencesTests.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class NotificationPreferencesTests
{
    [Fact]
    public void IsEnabled_DistinguishesEnabledAndDisabledCells()
    {
        var prefs = new NotificationPreferences("u-1", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
        });

        Assert.True(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Push));
    }

    [Fact]
    public void Default_EnablesInAppForAllTypes_AndNothingElse()
    {
        var prefs = NotificationPreferences.Default("u-1");

        Assert.True(prefs.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.InApp));
        Assert.True(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.InApp));
        Assert.True(prefs.IsEnabled(NotificationType.SystemMessage, NotificationChannel.InApp));
        Assert.False(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(prefs.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.Push));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build Ez.Handball.sln`
Expected: FAIL — compile errors, `NotificationPreferences` / `NotificationType` / `NotificationChannel` do not exist.

- [ ] **Step 3: Create the enums and records**

Create `Ez.Handball.Domain/NotificationType.cs`:

```csharp
namespace Ez.Handball.Domain;

public enum NotificationType
{
    MiniLeagueUpdate,
    RoundResult,
    SystemMessage
}
```

Create `Ez.Handball.Domain/NotificationChannel.cs`:

```csharp
namespace Ez.Handball.Domain;

public enum NotificationChannel
{
    InApp,
    Email,
    Push
}
```

Create `Ez.Handball.Domain/Notification.cs`:

```csharp
namespace Ez.Handball.Domain;

// A transient notification event handed to channels. No Id/timestamps — there is no
// store in the skeleton. Title/Body are plain strings; templating is out of scope.
public sealed record Notification(
    string UserId,
    NotificationType Type,
    string Title,
    string Body);
```

Create `Ez.Handball.Domain/NotificationPreferences.cs`:

```csharp
namespace Ez.Handball.Domain;

// Per-user type×channel matrix. A cell present in Enabled means "deliver this type on
// this channel". Absent = disabled.
public sealed record NotificationPreferences(
    string UserId,
    IReadOnlySet<(NotificationType Type, NotificationChannel Channel)> Enabled)
{
    public bool IsEnabled(NotificationType type, NotificationChannel channel)
        => Enabled.Contains((type, channel));

    // Defaults for a user who has never customized: in-app on for every type, email/push
    // off (no providers yet).
    public static NotificationPreferences Default(string userId)
        => new(userId, new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.MiniLeagueUpdate, NotificationChannel.InApp),
            (NotificationType.RoundResult, NotificationChannel.InApp),
            (NotificationType.SystemMessage, NotificationChannel.InApp),
        });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~NotificationPreferencesTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Domain/NotificationType.cs Ez.Handball.Domain/NotificationChannel.cs Ez.Handball.Domain/Notification.cs Ez.Handball.Domain/NotificationPreferences.cs Ez.Handball.Tests/Domain/NotificationPreferencesTests.cs
git commit -m "feat: notification domain types + preference matrix (#18)"
```

---

## Task 2: Producer, consumer, and repository abstractions

**Files:**
- Create: `Ez.Handball.Application/Abstractions/INotificationPublisher.cs`
- Create: `Ez.Handball.Application/Abstractions/INotificationChannel.cs`
- Create: `Ez.Handball.Application/Abstractions/INotificationPreferenceRepository.cs`

These are pure interfaces consumed by Task 3 (publisher) and Task 4 (repository). No standalone tests — they are exercised by those tasks. Verify with a build.

- [ ] **Step 1: Create the producer interface**

Create `Ez.Handball.Application/Abstractions/INotificationPublisher.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// The producer. Application code calls this to emit a notification; the implementation
// resolves preferences and fans out to enabled channels.
public interface INotificationPublisher
{
    Task PublishAsync(Notification notification, CancellationToken ct);
}
```

- [ ] **Step 2: Create the consumer interface**

Create `Ez.Handball.Application/Abstractions/INotificationChannel.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// A delivery channel (consumer). One implementation per NotificationChannel value.
public interface INotificationChannel
{
    NotificationChannel Channel { get; }
    Task SendAsync(Notification notification, CancellationToken ct);
}
```

- [ ] **Step 3: Create the preference repository interface**

Create `Ez.Handball.Application/Abstractions/INotificationPreferenceRepository.cs`:

```csharp
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface INotificationPreferenceRepository
{
    // Returns null when the user has never stored any preference (caller falls back to
    // NotificationPreferences.Default). An empty-but-non-null result means "configured
    // to receive nothing".
    Task<NotificationPreferences?> GetAsync(string userId, CancellationToken ct);

    Task UpsertAsync(NotificationPreferences preferences, CancellationToken ct);
}
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build Ez.Handball.sln`
Expected: SUCCESS, no errors.

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Application/Abstractions/INotificationPublisher.cs Ez.Handball.Application/Abstractions/INotificationChannel.cs Ez.Handball.Application/Abstractions/INotificationPreferenceRepository.cs
git commit -m "feat: notification producer/consumer/preference abstractions (#18)"
```

---

## Task 3: NotificationPublisher dispatcher + in-memory test consumer

**Files:**
- Modify: `Ez.Handball.Application/Ez.Handball.Application.csproj` (add logging package)
- Create: `Ez.Handball.Application/Services/NotificationPublisher.cs`
- Create: `Ez.Handball.Tests/Fakes/InMemoryNotificationChannel.cs`
- Test: `Ez.Handball.Tests/Application/Services/NotificationPublisherTests.cs`

- [ ] **Step 1: Add the logging package to Application**

The dispatcher logs channel failures via `ILogger<NotificationPublisher>`. The Application project does not yet reference any logging package. Open `Ez.Handball.Application/Ez.Handball.Application.csproj` and add a `<PackageReference>` inside a new `<ItemGroup>` (the project currently has only `<ProjectReference>` entries). The file should become:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ez.Handball.Domain\Ez.Handball.Domain.csproj" />
    <ProjectReference Include="..\Ez.Handball.Shared\Ez.Handball.Shared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the in-memory test consumer**

Create `Ez.Handball.Tests/Fakes/InMemoryNotificationChannel.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Fakes;

// Records every notification it receives. Stands in for a real channel in tests.
public sealed class InMemoryNotificationChannel : INotificationChannel
{
    private readonly List<Notification> _received = new();

    public InMemoryNotificationChannel(NotificationChannel channel) => Channel = channel;

    public NotificationChannel Channel { get; }

    public IReadOnlyList<Notification> Received => _received;

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        _received.Add(notification);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `Ez.Handball.Tests/Application/Services/NotificationPublisherTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Ez.Handball.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class NotificationPublisherTests
{
    private static Notification Sample(NotificationType type = NotificationType.RoundResult)
        => new("u-1", type, "Title", "Body");

    private static NotificationPublisher Sut(
        NotificationPreferences? stored,
        params INotificationChannel[] channels)
    {
        var repo = new Mock<INotificationPreferenceRepository>();
        repo.Setup(r => r.GetAsync("u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        return new NotificationPublisher(repo.Object, channels, NullLogger<NotificationPublisher>.Instance);
    }

    private static NotificationPreferences Prefs(params (NotificationType, NotificationChannel)[] cells)
        => new("u-1", new HashSet<(NotificationType, NotificationChannel)>(cells));

    [Fact]
    public async Task DeliversToChannel_WhenCellEnabled()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);

        await Sut(Prefs((NotificationType.RoundResult, NotificationChannel.InApp)), inApp)
            .PublishAsync(Sample(), default);

        Assert.Single(inApp.Received);
    }

    [Fact]
    public async Task SkipsChannel_WhenCellDisabled()
    {
        var email = new InMemoryNotificationChannel(NotificationChannel.Email);

        // RoundResult enabled on InApp only — email cell absent.
        await Sut(Prefs((NotificationType.RoundResult, NotificationChannel.InApp)), email)
            .PublishAsync(Sample(), default);

        Assert.Empty(email.Received);
    }

    [Fact]
    public async Task FallsBackToDefaults_WhenNoStoredPreferences()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);

        await Sut(stored: null, inApp).PublishAsync(Sample(), default);

        Assert.Single(inApp.Received); // Default() enables InApp for all types
    }

    [Fact]
    public async Task OneThrowingChannel_DoesNotBlockOthers()
    {
        var good = new InMemoryNotificationChannel(NotificationChannel.InApp);
        var bad = new ThrowingChannel(NotificationChannel.Email);

        await Sut(Prefs(
                    (NotificationType.RoundResult, NotificationChannel.InApp),
                    (NotificationType.RoundResult, NotificationChannel.Email)),
                 bad, good)
            .PublishAsync(Sample(), default);

        Assert.Single(good.Received); // good delivered despite bad throwing
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public ThrowingChannel(NotificationChannel channel) => Channel = channel;
        public NotificationChannel Channel { get; }
        public Task SendAsync(Notification notification, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet build Ez.Handball.sln`
Expected: FAIL — `NotificationPublisher` does not exist.

- [ ] **Step 5: Implement the dispatcher**

Create `Ez.Handball.Application/Services/NotificationPublisher.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Application.Services;

// In-process synchronous fan-out. Resolves the user's preferences, then invokes every
// channel whose (type, channel) cell is enabled. A failing channel is logged and skipped
// so it cannot block delivery to the others.
public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly INotificationPreferenceRepository _preferences;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(
        INotificationPreferenceRepository preferences,
        IEnumerable<INotificationChannel> channels,
        ILogger<NotificationPublisher> logger)
    {
        _preferences = preferences;
        _channels = channels;
        _logger = logger;
    }

    public async Task PublishAsync(Notification notification, CancellationToken ct)
    {
        var prefs = await _preferences.GetAsync(notification.UserId, ct)
                    ?? NotificationPreferences.Default(notification.UserId);

        foreach (var channel in _channels)
        {
            if (!prefs.IsEnabled(notification.Type, channel.Channel))
                continue;

            try
            {
                await channel.SendAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Notification channel {Channel} failed for user {UserId}, type {Type}",
                    channel.Channel, notification.UserId, notification.Type);
            }
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~NotificationPublisherTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Application/Ez.Handball.Application.csproj Ez.Handball.Application/Services/NotificationPublisher.cs Ez.Handball.Tests/Fakes/InMemoryNotificationChannel.cs Ez.Handball.Tests/Application/Services/NotificationPublisherTests.cs
git commit -m "feat: in-process notification fan-out dispatcher (#18)"
```

---

## Task 4: Preference entity + Table repository

**Files:**
- Create: `Ez.Handball.Shared/Entities/NotificationPreferenceEntity.cs`
- Modify: `Ez.Handball.Infrastructure/Tables.cs` (add constant after line 24, `MiniLeagueInvites`)
- Create: `Ez.Handball.Infrastructure/TableAccess/TableNotificationPreferenceRepository.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Tables/TableNotificationPreferenceRepositoryTests.cs`

> **Azurite required.** These are integration tests against the local table emulator. Start it first if it is not running: `azurite --silent --location /tmp/azurite-test &`

- [ ] **Step 1: Create the table entity**

Create `Ez.Handball.Shared/Entities/NotificationPreferenceEntity.cs`:

```csharp
using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One enabled (type, channel) cell. PartitionKey = userId, RowKey = "{Type}:{Channel}".
// A row's existence means the cell is enabled; there are no rows for disabled cells.
public sealed class NotificationPreferenceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // "{Type}:{Channel}"
    public string Type { get; set; } = string.Empty;         // NotificationType name
    public string Channel { get; set; } = string.Empty;      // NotificationChannel name
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
```

- [ ] **Step 2: Add the table name constant**

In `Ez.Handball.Infrastructure/Tables.cs`, add the following line immediately after the `MiniLeagueInvites` constant (line 24):

```csharp
    public const string NotificationPreferences = "NotificationPreferences";
```

- [ ] **Step 3: Write the failing test**

Create `Ez.Handball.Tests/Infrastructure/Tables/TableNotificationPreferenceRepositoryTests.cs`:

```csharp
using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableNotificationPreferenceRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private readonly ITableQuery _query;

    public TableNotificationPreferenceRepositoryTests() => _query = new TableQuery(_client);

    private TableNotificationPreferenceRepository Sut() => new(_client, _query);

    public async Task InitializeAsync()
        => await _client.GetTableClient(Tables.NotificationPreferences).CreateIfNotExistsAsync();

    public async Task DisposeAsync()
        => await _client.GetTableClient(Tables.NotificationPreferences).DeleteAsync();

    [Fact]
    public async Task Get_ReturnsNull_WhenNoRowsStored()
    {
        var got = await Sut().GetAsync("u-none", default);

        Assert.Null(got);
    }

    [Fact]
    public async Task Upsert_ThenGet_RoundTripsEnabledCells()
    {
        var prefs = new NotificationPreferences("u-1", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
            (NotificationType.MiniLeagueUpdate, NotificationChannel.InApp),
        });

        await Sut().UpsertAsync(prefs, default);
        var got = await Sut().GetAsync("u-1", default);

        Assert.NotNull(got);
        Assert.True(got!.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.True(got.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.InApp));
        Assert.False(got.IsEnabled(NotificationType.RoundResult, NotificationChannel.InApp));
    }

    [Fact]
    public async Task Upsert_RemovesCells_NoLongerEnabled()
    {
        await Sut().UpsertAsync(new NotificationPreferences("u-2", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
            (NotificationType.RoundResult, NotificationChannel.Push),
        }), default);

        // Re-save with Push removed.
        await Sut().UpsertAsync(new NotificationPreferences("u-2", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
        }), default);

        var got = await Sut().GetAsync("u-2", default);

        Assert.NotNull(got);
        Assert.True(got!.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(got.IsEnabled(NotificationType.RoundResult, NotificationChannel.Push));
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet build Ez.Handball.sln`
Expected: FAIL — `TableNotificationPreferenceRepository` does not exist.

- [ ] **Step 5: Implement the repository**

Create `Ez.Handball.Infrastructure/TableAccess/TableNotificationPreferenceRepository.cs`:

```csharp
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableNotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableNotificationPreferenceRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<NotificationPreferences?> GetAsync(string userId, CancellationToken ct)
    {
        var cells = new HashSet<(NotificationType, NotificationChannel)>();
        var any = false;
        await foreach (var e in _query.QueryAsync<NotificationPreferenceEntity>(
                           Tables.NotificationPreferences,
                           $"PartitionKey eq '{ODataFilter.Escape(userId)}'", ct))
        {
            any = true;
            cells.Add((Enum.Parse<NotificationType>(e.Type), Enum.Parse<NotificationChannel>(e.Channel)));
        }

        return any ? new NotificationPreferences(userId, cells) : null;
    }

    public async Task UpsertAsync(NotificationPreferences preferences, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.NotificationPreferences);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var desired = preferences.Enabled
            .ToDictionary(c => RowKeyFor(c.Type, c.Channel), c => c);

        // Remove cells that are no longer enabled.
        await foreach (var e in _query.QueryAsync<NotificationPreferenceEntity>(
                           Tables.NotificationPreferences,
                           $"PartitionKey eq '{ODataFilter.Escape(preferences.UserId)}'", ct))
        {
            if (!desired.ContainsKey(e.RowKey))
            {
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);
            }
        }

        // Upsert every enabled cell.
        foreach (var (rowKey, cell) in desired)
        {
            await table.UpsertEntityAsync(new NotificationPreferenceEntity
            {
                PartitionKey = preferences.UserId,
                RowKey = rowKey,
                Type = cell.Type.ToString(),
                Channel = cell.Channel.ToString()
            }, TableUpdateMode.Replace, ct);
        }
    }

    private static string RowKeyFor(NotificationType type, NotificationChannel channel)
        => $"{type}:{channel}";
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~TableNotificationPreferenceRepositoryTests"`
Expected: PASS (3 tests). (Requires Azurite running.)

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Shared/Entities/NotificationPreferenceEntity.cs Ez.Handball.Infrastructure/Tables.cs Ez.Handball.Infrastructure/TableAccess/TableNotificationPreferenceRepository.cs Ez.Handball.Tests/Infrastructure/Tables/TableNotificationPreferenceRepositoryTests.cs
git commit -m "feat: Table-backed notification preference repository (#18)"
```

---

## Task 5: Placeholder channel + DI wiring

**Files:**
- Create: `Ez.Handball.Infrastructure/LoggingNotificationChannel.cs`
- Modify: `Ez.Handball.Infrastructure/InfrastructureRegistration.cs` (add two registrations after line 35, `IMiniLeagueInviteRepository`)
- Modify: `Ez.Handball.Api/Program.cs` (register the publisher near the other use-case/service registrations, ~line 130)

No new unit test — this task wires existing, already-tested components. Verified by a green full test run (the suite builds the API host, which fails if DI registration is wrong) plus a `dotnet build`.

- [ ] **Step 1: Create the placeholder in-app channel**

Create `Ez.Handball.Infrastructure/LoggingNotificationChannel.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure;

// Placeholder in-app channel: logs what it would deliver. No real provider yet (#18
// skeleton). Keeps the wired fan-out path observably alive in production.
internal sealed class LoggingNotificationChannel : INotificationChannel
{
    private readonly ILogger<LoggingNotificationChannel> _logger;

    public LoggingNotificationChannel(ILogger<LoggingNotificationChannel> logger) => _logger = logger;

    public NotificationChannel Channel => NotificationChannel.InApp;

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[notification] user={UserId} type={Type} title={Title}",
            notification.UserId, notification.Type, notification.Title);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Register the repository and channel in Infrastructure**

In `Ez.Handball.Infrastructure/InfrastructureRegistration.cs`, add these two lines immediately after the `IMiniLeagueInviteRepository` registration (line 35), before `return services;`:

```csharp
        services.AddScoped<INotificationPreferenceRepository, TableNotificationPreferenceRepository>();
        services.AddScoped<INotificationChannel, LoggingNotificationChannel>();
```

(The file already has `using Ez.Handball.Application.Abstractions;` and `using Ez.Handball.Infrastructure.TableAccess;`, so no new usings are needed. `LoggingNotificationChannel` lives in the `Ez.Handball.Infrastructure` namespace, also already in scope.)

- [ ] **Step 3: Register the publisher in the API**

In `Ez.Handball.Api/Program.cs`, add this line alongside the other `AddScoped` service registrations (e.g. right after line 130, the `GetPlayerRatingUseCase` registration):

```csharp
builder.Services.AddScoped<INotificationPublisher, NotificationPublisher>();
```

Confirm the file's using directives include both `using Ez.Handball.Application.Abstractions;` and `using Ez.Handball.Application.Services;`. If either is missing, add it at the top of the file.

- [ ] **Step 4: Build to verify wiring compiles**

Run: `dotnet build Ez.Handball.sln`
Expected: SUCCESS, no errors.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS — all pre-existing tests plus the 9 new notification tests green. (Requires Azurite running.)

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Infrastructure/LoggingNotificationChannel.cs Ez.Handball.Infrastructure/InfrastructureRegistration.cs Ez.Handball.Api/Program.cs
git commit -m "feat: wire notification publisher + logging channel into DI (#18)"
```

---

## Done

The notification rails are laid:
- `NotificationType` / `NotificationChannel` enums and the `Notification` event record.
- A per-user type×channel preference matrix (`NotificationPreferences`) with sensible defaults, persisted via `TableNotificationPreferenceRepository`.
- `INotificationPublisher` (producer) → `NotificationPublisher` (in-process fan-out) → `INotificationChannel` consumers, with a placeholder `LoggingNotificationChannel` registered in production and an `InMemoryNotificationChannel` proving the path in tests.

**Deferred (not in this plan, per spec):** persisted notification inbox/feed, HTTP endpoints (read/update prefs, fetch feed), SMTP/push providers, templates, and real call sites that emit notifications (e.g. on mini-league join or round result).
