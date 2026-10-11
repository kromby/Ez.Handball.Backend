using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableGameRosterRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private IGameRosterRepository Sut() => new TableGameRosterRepository(_client, new TableQuery(_client));
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T1 = DateTimeOffset.UnixEpoch.AddDays(1);
    private const string Team = "u-1:fantasy";

    public async Task InitializeAsync() => await _client.GetTableClient(Tables.GameRosters).CreateIfNotExistsAsync();
    public async Task DisposeAsync() => await _client.GetTableClient(Tables.GameRosters).DeleteAsync();

    [Fact]
    public async Task Add_ThenListActive_ReturnsSlot()
    {
        Assert.Equal(RosterAddOutcome.Added, await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default));
        var active = await Sut().ListActiveAsync(Team, default);
        var entry = Assert.Single(active);
        Assert.Equal("p-1", entry.PlayerId);
        Assert.Equal("VS", entry.Position);
        Assert.Equal(40_000_000, entry.PricePaidAmount);
    }

    [Fact]
    public async Task Add_WhenActiveExists_ReturnsAlreadyActive()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        Assert.Equal(RosterAddOutcome.AlreadyActive,
            await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 99_000_000, T1, default));
        // Price unchanged by the rejected re-add.
        Assert.Equal(40_000_000, (await Sut().GetAsync(Team, "p-1", default))!.PricePaidAmount);
    }

    [Fact]
    public async Task SoftDelete_RemovesFromActive_ThenResurrectRelocksPrice()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        Assert.Empty(await Sut().ListActiveAsync(Team, default));
        Assert.NotNull((await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);

        // Re-buying a sold player resurrects the row with a fresh price and clears DeletedAt.
        Assert.Equal(RosterAddOutcome.Added,
            await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 55_000_000, T1, default));
        var entry = Assert.Single(await Sut().ListActiveAsync(Team, default));
        Assert.Equal(55_000_000, entry.PricePaidAmount);
    }

    [Fact]
    public async Task SoftDelete_IsIdempotent()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        // First soft-delete succeeds.
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        // Second soft-delete on an already-deleted row is a silent no-op.
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        // Row remains soft-deleted and absent from the active set.
        Assert.Empty(await Sut().ListActiveAsync(Team, default));
        Assert.NotNull((await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);
    }

    [Fact]
    public async Task ListActive_IsScopedByTeam()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "VS", 40_000_000, T0, default);
        await Sut().AddOrResurrectAsync("u-2:fantasy", "p-9", "VS", 10_000_000, T0, default);
        Assert.Single(await Sut().ListActiveAsync(Team, default));
    }

    [Fact]
    public async Task SetPlayAs_ThenGet_StoresPinAndKeepsOtherFields()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        Assert.Equal(SetPlayAsOutcome.Updated, await Sut().SetPlayAsAsync(Team, "p-1", "LB", default));
        var entry = (await Sut().GetAsync(Team, "p-1", default))!;
        Assert.Equal("LB", entry.PlayAs);
        Assert.Equal("CB", entry.Position);
        Assert.Equal(40_000_000, entry.PricePaidAmount);
        Assert.Equal("LB", Assert.Single(await Sut().ListActiveAsync(Team, default)).PlayAs);
    }

    [Fact]
    public async Task SetPlayAs_Null_ClearsPin()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        await Sut().SetPlayAsAsync(Team, "p-1", "LB", default);
        Assert.Equal(SetPlayAsOutcome.Updated, await Sut().SetPlayAsAsync(Team, "p-1", null, default));
        Assert.Null((await Sut().GetAsync(Team, "p-1", default))!.PlayAs);
    }

    [Fact]
    public async Task SetPlayAs_MissingRow_ReturnsNotFound()
        => Assert.Equal(SetPlayAsOutcome.NotFound, await Sut().SetPlayAsAsync(Team, "nope", "LB", default));

    [Fact]
    public async Task SetPlayAs_SoftDeletedRow_ReturnsNotFound()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        Assert.Equal(SetPlayAsOutcome.NotFound, await Sut().SetPlayAsAsync(Team, "p-1", "LB", default));
    }

    [Fact]
    public async Task Resurrect_ClearsPin()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        await Sut().SetPlayAsAsync(Team, "p-1", "LB", default);
        await Sut().SoftDeleteAsync(Team, "p-1", T1, default);
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 50_000_000, T1, default);
        Assert.Null((await Sut().GetAsync(Team, "p-1", default))!.PlayAs);
    }

    // F1: a pin write landing between the sell's read and its conditional replace must not
    // make the soft-delete silently skip — the sell would credit the money and keep the player.
    [Fact]
    public async Task SoftDelete_AfterConcurrentPinWrite_StillSoftDeletesAndKeepsPin()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        var racing = new InterleavingServiceClient(() => Sut().SetPlayAsAsync(Team, "p-1", "LB", default));

        await RacingSut(racing).SoftDeleteAsync(Team, "p-1", T1, default);

        var entry = (await Sut().GetAsync(Team, "p-1", default))!;
        Assert.Equal(T1, entry.DeletedAt);
        Assert.Equal("LB", entry.PlayAs);
        Assert.Empty(await Sut().ListActiveAsync(Team, default));
        Assert.Equal(2, racing.ReplaceCalls); // lost the first race, won on the retry
    }

    [Fact]
    public async Task SoftDelete_WhenConcurrentlySoftDeleted_IsNoOp()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        var racing = new InterleavingServiceClient(() => Sut().SoftDeleteAsync(Team, "p-1", T0, default));

        await RacingSut(racing).SoftDeleteAsync(Team, "p-1", T1, default);

        // The concurrent delete stands; the losing call neither throws nor overwrites it.
        Assert.Equal(T0, (await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);
    }

    [Fact]
    public async Task SoftDelete_WhenRowRemovedConcurrently_IsNoOp()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        var racing = new InterleavingServiceClient(
            () => _client.GetTableClient(Tables.GameRosters).DeleteEntityAsync(Team, "p-1"));

        await RacingSut(racing).SoftDeleteAsync(Team, "p-1", T1, default);

        Assert.Null(await Sut().GetAsync(Team, "p-1", default));
    }

    [Fact]
    public async Task SoftDelete_WhenConflictPersists_Throws()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        var flip = false;
        var racing = new InterleavingServiceClient(
            () => Sut().SetPlayAsAsync(Team, "p-1", (flip = !flip) ? "LB" : "CB", default), everyTime: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RacingSut(racing).SoftDeleteAsync(Team, "p-1", T1, default));

        // Aborted without deleting, so the caller must not credit the sale.
        Assert.Null((await Sut().GetAsync(Team, "p-1", default))!.DeletedAt);
        Assert.Equal(3, racing.ReplaceCalls);
    }

    // F4: two pin writes racing — the loser reports Conflict instead of overwriting the winner.
    [Fact]
    public async Task SetPlayAs_LostEtagRace_ReturnsConflict()
    {
        await Sut().AddOrResurrectAsync(Team, "p-1", "CB", 40_000_000, T0, default);
        var racing = new InterleavingServiceClient(() => Sut().SetPlayAsAsync(Team, "p-1", "CB", default));

        Assert.Equal(SetPlayAsOutcome.Conflict, await RacingSut(racing).SetPlayAsAsync(Team, "p-1", "LB", default));
        Assert.Equal("CB", (await Sut().GetAsync(Team, "p-1", default))!.PlayAs);
    }

    private static IGameRosterRepository RacingSut(TableServiceClient client)
        => new TableGameRosterRepository(client, new TableQuery(client));

    // Runs a competing write right before the repository's conditional replace — after its read —
    // so the replace goes out with a stale ETag. Deterministic, against real Azurite.
    private sealed class InterleavingServiceClient : TableServiceClient
    {
        private const string Conn = "UseDevelopmentStorage=true";
        private readonly Func<Task> _interleave;
        private readonly bool _everyTime;
        private bool _fired;

        public InterleavingServiceClient(Func<Task> interleave, bool everyTime = false) : base(Conn)
        {
            _interleave = interleave;
            _everyTime = everyTime;
        }

        public int ReplaceCalls { get; private set; }

        public override TableClient GetTableClient(string tableName) => new InterleavingTableClient(tableName, this);

        private async Task BeforeReplaceAsync()
        {
            ReplaceCalls++;
            if (_fired && !_everyTime) return;
            _fired = true;
            await _interleave();
        }

        private sealed class InterleavingTableClient : TableClient
        {
            private readonly InterleavingServiceClient _owner;

            public InterleavingTableClient(string tableName, InterleavingServiceClient owner) : base(Conn, tableName)
                => _owner = owner;

            public override async Task<Response> UpdateEntityAsync<T>(
                T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
            {
                await _owner.BeforeReplaceAsync();
                return await base.UpdateEntityAsync(entity, ifMatch, mode, cancellationToken);
            }
        }
    }
}
