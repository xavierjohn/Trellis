using Trellis.Mediator;
using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static RepositoryBaseTests;

/// <summary>
/// Tests for <see cref="EfUnitOfWork{TContext}"/>'s implementation of
/// <see cref="ITrackedAggregateSource"/>. The contract is documented on
/// <see cref="ITrackedAggregateSource.CommittedAggregates"/>: empty before any commit,
/// snapshot on success, empty on failure or exception, untouched during deferred
/// nested commits.
/// </summary>
public class EfUnitOfWorkTrackedAggregateSourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RepoTestDbContext _context;
    private readonly EfUnitOfWork<RepoTestDbContext> _unitOfWork;

    public EfUnitOfWorkTrackedAggregateSourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RepoTestDbContext>()
            .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
            .Options;

        _context = new RepoTestDbContext(options);
        _context.Database.EnsureCreated();
        _unitOfWork = new EfUnitOfWork<RepoTestDbContext>(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CommittedAggregates_is_empty_before_any_commit() =>
        TrackedSource().CommittedAggregates.Should().BeEmpty();

    [Fact]
    public async Task CommittedAggregates_populated_after_successful_commit()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var item = TestItem.Create(id, TestItemName.Create("snapshot-success"));
        _context.Items.Add(item);

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert
        result.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().HaveCount(1);
        TrackedSource().CommittedAggregates[0].Should().BeSameAs(item);
    }

    [Fact]
    public async Task CommittedAggregates_empty_after_failed_commit()
    {
        // Arrange — stage a duplicate-key insert: persist once, clear the tracker,
        // then add a fresh instance with the same primary key. The second SaveChangesAsync
        // hits a UNIQUE-constraint violation which DbContextExtensions.SaveChangesResultUnitAsync
        // maps to Error.Conflict (a Result failure, not an exception).
        var ct = TestContext.Current.CancellationToken;
        var id = TestItemId.Create(Guid.NewGuid());
        var first = TestItem.Create(id, TestItemName.Create("first"));
        _context.Items.Add(first);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var duplicate = TestItem.Create(id, TestItemName.Create("duplicate"));
        _context.Items.Add(duplicate);

        // Act
        var result = await _unitOfWork.CommitAsync(ct);

        // Assert — failed commit must leave the snapshot empty.
        result.IsFailure.Should().BeTrue();
        TrackedSource().CommittedAggregates.Should().BeEmpty(
            "a failed commit must leave CommittedAggregates as the empty list so the next " +
            "successful commit never auto-dispatches events from a previously failed handler.");
    }

    [Fact]
    public async Task CommittedAggregates_empty_after_thrown_save()
    {
        // Arrange — stage a successful commit first so CommittedAggregates is non-empty,
        // then queue another aggregate and cancel before save so the exception path runs.
        var ct = TestContext.Current.CancellationToken;
        var firstId = TestItemId.Create(Guid.NewGuid());
        var first = TestItem.Create(firstId, TestItemName.Create("first-success"));
        _context.Items.Add(first);
        var firstResult = await _unitOfWork.CommitAsync(ct);
        firstResult.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().HaveCount(1);

        var nextId = TestItemId.Create(Guid.NewGuid());
        var next = TestItem.Create(nextId, TestItemName.Create("never-persisted"));
        _context.Items.Add(next);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.Cancel();

        // Act
        var act = async () => await _unitOfWork.CommitAsync(cts.Token);

        // Assert — cancellation propagates, the prior successful snapshot is cleared
        // BEFORE the save attempt so no stale data leaks across the throw.
        await act.Should().ThrowAsync<OperationCanceledException>();
        TrackedSource().CommittedAggregates.Should().BeEmpty(
            "the snapshot is cleared before the save attempt so an exception (cancellation, " +
            "connection failure, ...) cannot leave a stale prior snapshot exposed.");
    }

    [Fact]
    public async Task CommittedAggregates_unchanged_during_deferred_nested_commit()
    {
        // Arrange — set up a successful outer-only snapshot, then enter a nested scope
        // whose commit must defer (return success without touching the snapshot).
        var ct = TestContext.Current.CancellationToken;
        var firstId = TestItemId.Create(Guid.NewGuid());
        var first = TestItem.Create(firstId, TestItemName.Create("outer"));
        _context.Items.Add(first);
        var firstResult = await _unitOfWork.CommitAsync(ct);
        firstResult.Should().BeSuccess();

        var snapshotBefore = TrackedSource().CommittedAggregates;
        snapshotBefore.Should().HaveCount(1);

        // Open two scopes so depth = 2 ⇒ inner commit defers. Stage another aggregate so
        // the snapshot would change if the deferred commit (incorrectly) touched it.
        using var outer = _unitOfWork.BeginScope();
        using var inner = _unitOfWork.BeginScope();

        var nestedId = TestItemId.Create(Guid.NewGuid());
        var nested = TestItem.Create(nestedId, TestItemName.Create("nested"));
        _context.Items.Add(nested);

        // Act
        var nestedResult = await _unitOfWork.CommitAsync(ct);

        // Assert — deferred commit reports success and the prior snapshot is untouched.
        nestedResult.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().BeSameAs(snapshotBefore,
            "deferred nested commits must not touch CommittedAggregates; only the outermost " +
            "commit owns the snapshot. Reference-equality is asserted because the EF UoW " +
            "exposes the same backing list while the scope is in-flight.");
    }

    [Fact]
    public async Task CommittedAggregates_cleared_then_replaced_on_second_commit()
    {
        // Arrange — first commit produces a snapshot.
        var ct = TestContext.Current.CancellationToken;
        var firstId = TestItemId.Create(Guid.NewGuid());
        var first = TestItem.Create(firstId, TestItemName.Create("first-commit"));
        _context.Items.Add(first);
        var firstResult = await _unitOfWork.CommitAsync(ct);
        firstResult.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().HaveCount(1);
        TrackedSource().CommittedAggregates[0].Should().BeSameAs(first);

        // Act — second commit on the same UoW. The change tracker still holds `first`
        // in the Unchanged state plus the newly Added `second`, so the snapshot must
        // contain both.
        var secondId = TestItemId.Create(Guid.NewGuid());
        var second = TestItem.Create(secondId, TestItemName.Create("second-commit"));
        _context.Items.Add(second);
        var secondResult = await _unitOfWork.CommitAsync(ct);

        // Assert
        secondResult.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().HaveCount(2);
        TrackedSource().CommittedAggregates.Should().Contain(first);
        TrackedSource().CommittedAggregates.Should().Contain(second);
    }

    [Fact]
    public async Task CommittedAggregates_empty_when_commit_persists_no_aggregates()
    {
        // Sanity: a commit with nothing staged returns success and leaves CommittedAggregates empty.
        var ct = TestContext.Current.CancellationToken;
        var result = await _unitOfWork.CommitAsync(ct);

        result.Should().BeSuccess();
        TrackedSource().CommittedAggregates.Should().BeEmpty();
    }

    // Returns the unit-of-work as ITrackedAggregateSource so the test asserts the
    // public sidecar contract rather than the concrete type's surface (CA1859 would
    // prefer the concrete return type for perf, but the perf gain is irrelevant in
    // tests and we deliberately want to exercise the interface contract).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Deliberately exercising the ITrackedAggregateSource contract.")]
    private ITrackedAggregateSource TrackedSource() => _unitOfWork;

    [Fact]
    public void IUnitOfWork_and_ITrackedAggregateSource_resolve_to_same_instance()
    {
        // The DI forwarder casts IUnitOfWork as ITrackedAggregateSource, so both
        // service descriptors back the same scoped instance. This is required so that
        // the TrackedAggregateDomainEventDispatchBehavior sees the same UoW the
        // TransactionalCommandBehavior just committed.
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var trackedSource = scope.ServiceProvider.GetRequiredService<ITrackedAggregateSource>();

        trackedSource.Should().BeSameAs(uow);
    }

    [Fact]
    public void AddTrellisUnitOfWorkWithoutBehavior_also_registers_tracked_aggregate_source()
    {
        // The without-behavior variant must wire the forwarder too: the tracked-aggregate
        // dispatcher and the response-shape dispatcher both work whether or not the
        // TransactionalCommandBehavior is in the pipeline.
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());
        services.AddTrellisUnitOfWorkWithoutBehavior<RepoTestDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var trackedSource = scope.ServiceProvider.GetRequiredService<ITrackedAggregateSource>();

        trackedSource.Should().BeSameAs(uow);
    }

    [Fact]
    public void Custom_IUnitOfWork_without_tracked_source_throws_on_resolve()
    {
        // If a consumer registers a custom IUnitOfWork that doesn't implement
        // ITrackedAggregateSource, the forwarder must fail loud at resolve-time
        // rather than silently handing out a different EF UoW instance whose
        // snapshot is never populated.
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());

        // Custom UoW must be registered BEFORE AddTrellisUnitOfWork so the
        // TryAddScoped<IUnitOfWork>(...) call inside AddTrellisUnitOfWork is a no-op
        // and leaves our custom registration as the IUnitOfWork.
        services.AddScoped<IUnitOfWork, CustomUnitOfWorkWithoutTrackedSource>();
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ITrackedAggregateSource>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement ITrackedAggregateSource*");
    }

    private sealed class CustomUnitOfWorkWithoutTrackedSource : IUnitOfWork
    {
        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok());

        public IDisposable BeginScope() => new NullScope();

        private sealed class NullScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}