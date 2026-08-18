namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="DbContextIdempotencyExtensions.TryInsertUniqueAsync{TEntity}(DbContext, TEntity, CancellationToken)"/>.
/// Uses the existing SQLite in-memory fixture; the helper itself is provider-agnostic.
/// </summary>
public sealed class DbContextIdempotencyExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public DbContextIdempotencyExtensionsTests() =>
        (_context, _connection) = TestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TestCustomer NewCustomer(string name, string email) => new()
    {
        Id = TestCustomerId.NewUniqueV4(),
        Name = TestCustomerName.Create(name),
        Email = EmailAddress.Create(email),
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task TryInsertUniqueAsync_FreshContext_Success_ReturnsInsertedEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = NewCustomer("Alice", "alice@example.com");

        var result = await _context.TryInsertUniqueAsync(customer, ct);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeSameAs(customer);
        (await _context.Customers.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task TryInsertUniqueAsync_DuplicateUniqueValue_ReturnsConflictWithDuplicateKeyReasonCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = NewCustomer("Alice", "duplicate@example.com");
        (await _context.TryInsertUniqueAsync(first, ct)).IsSuccess.Should().BeTrue();

        var second = NewCustomer("Bob", "duplicate@example.com");

        var result = await _context.TryInsertUniqueAsync(second, ct);

        result.IsSuccess.Should().BeFalse();
        var conflict = result.UnwrapError().Should().BeOfType<Error.Conflict>().Which;
        conflict.ReasonCode.Should().Be("duplicate.key");
        conflict.Detail.Should().Be("A record with the same unique value already exists.");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_DuplicateUniqueValue_PopulatesConstraintTableName()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = NewCustomer("Alice", "tableinfo@example.com");
        (await _context.TryInsertUniqueAsync(first, ct)).IsSuccess.Should().BeTrue();

        var second = NewCustomer("Bob", "tableinfo@example.com");

        var result = await _context.TryInsertUniqueAsync(second, ct);

        // SQLite does not expose a constraint name in its message, but it does expose
        // the table name via "UNIQUE constraint failed: <Table>.<Column>".
        var conflict = result.UnwrapError().Should().BeOfType<Error.Conflict>().Which;
        conflict.ConstraintTableName.Should().Be("Customers");
        conflict.ConstraintName.Should().BeNull(because: "SQLite does not surface a constraint name in its message");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_DuplicateUniqueValue_DetachesAttemptedEntity()
    {
        // Failed inserts must leave the change tracker empty so a caller retrying with a
        // freshly-built entity (typical idempotency pattern) does not get the previous
        // attempt re-flushed on the next SaveChangesAsync.
        var ct = TestContext.Current.CancellationToken;
        var first = NewCustomer("Alice", "detach@example.com");
        (await _context.TryInsertUniqueAsync(first, ct)).IsSuccess.Should().BeTrue();

        var second = NewCustomer("Bob", "detach@example.com");
        (await _context.TryInsertUniqueAsync(second, ct)).IsSuccess.Should().BeFalse();

        _context.Entry(second).State.Should().Be(EntityState.Detached);
        _context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_PreExistingPendingChanges_ThrowsInvalidOperationException()
    {
        // The helper attributes a duplicate-key failure to the entity it added. Allowing
        // unrelated pending changes would let an FK or duplicate from those changes be
        // misreported as a conflict on the inserted entity.
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(NewCustomer("Pending", "pending@example.com"));

        var act = async () => await _context.TryInsertUniqueAsync(NewCustomer("New", "new@example.com"), ct);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("pending changes");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_NonDuplicateDbUpdateException_RethrowsException()
    {
        // FK violation on the inserted entity is NOT a unique-constraint violation and
        // must propagate as an exception so callers do not silently treat it as "skipped".
        var ct = TestContext.Current.CancellationToken;
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = TestCustomerId.NewUniqueV4(), // dangling FK
            Amount = 1m,
            Status = TestOrderStatus.Draft,
        };

        var act = async () => await _context.TryInsertUniqueAsync(order, ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_CancellationBeforeFlush_PropagatesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _context.TryInsertUniqueAsync(
            NewCustomer("Cancel", "cancel@example.com"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_PreCancelledToken_LeavesChangeTrackerClean()
    {
        // A pre-cancelled call must not leave the entity attached as Added — that would
        // allow a later SaveChangesAsync on the same context to insert it accidentally.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var customer = NewCustomer("Cancel", "cancelclean@example.com");

        var act = async () => await _context.TryInsertUniqueAsync(customer, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _context.Entry(customer).State.Should().Be(EntityState.Detached);
        _context.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_AggregateWithDependents_DetachesEntireGraphOnDuplicate()
    {
        // context.Add(entity) walks navigations and attaches dependents as Added. After a
        // duplicate-key failure the helper must detach the whole introduced graph so a
        // retry call does not trip the HasChanges() guard and a later SaveChangesAsync
        // does not flush stale dependents.
        var ct = TestContext.Current.CancellationToken;

        // Seed the conflicting unique value via a separate, isolated insert.
        var first = NewCustomer("Alice", "graph@example.com");
        (await _context.TryInsertUniqueAsync(first, ct)).IsSuccess.Should().BeTrue();

        // Build a customer with an attached Order; both will be tracked when Add(customer) runs.
        var customer = NewCustomer("Bob", "graph@example.com");
        customer.Orders.Add(new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Customer = customer,
            Amount = 1m,
            Status = TestOrderStatus.Draft,
        });

        var result = await _context.TryInsertUniqueAsync(customer, ct);

        result.IsSuccess.Should().BeFalse();
        _context.Entry(customer).State.Should().Be(EntityState.Detached);
        _context.Entry(customer.Orders[0]).State.Should().Be(EntityState.Detached);
        _context.ChangeTracker.HasChanges().Should().BeFalse(
            because: "the helper must detach the entire graph it introduced, not just the root");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_AggregateWithDependents_LeavesPreExistingUnchangedEntitiesAlone()
    {
        // Pre-existing Unchanged tracked entities (typical pattern: a parent loaded for FK
        // validation) must NOT be detached by failure cleanup — only the entries
        // introduced by this call should be detached.
        var ct = TestContext.Current.CancellationToken;

        // Seed a tracked Unchanged parent customer (this represents "loaded for reference").
        var parent = NewCustomer("Parent", "parent@example.com");
        (await _context.TryInsertUniqueAsync(parent, ct)).IsSuccess.Should().BeTrue();
        _context.Entry(parent).State.Should().Be(EntityState.Unchanged);

        // Now attempt to insert another customer with a duplicate email.
        // HasChanges() must be false (parent is Unchanged) so the helper proceeds.
        _context.ChangeTracker.HasChanges().Should().BeFalse();
        var duplicate = NewCustomer("Dup", "parent@example.com");

        var result = await _context.TryInsertUniqueAsync(duplicate, ct);

        result.IsSuccess.Should().BeFalse();
        _context.Entry(parent).State.Should().Be(EntityState.Unchanged,
            because: "pre-existing Unchanged entries must not be detached by failure cleanup");
        _context.Entry(duplicate).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task TryInsertUniqueAsync_PreviouslyTrackedUnchangedEntity_RestoredToUnchangedOnDuplicate()
    {
        // When a caller passes an entity that is already tracked as Unchanged (e.g., it
        // was loaded for inspection, or a previous TryInsertUniqueAsync persisted it and
        // left it tracked), context.Add flips its state to Added. If the resulting INSERT
        // fails with a duplicate-key violation, the helper must restore the entry to its
        // prior state (Unchanged) — not leave it as Added, which would silently re-insert
        // on the next SaveChangesAsync, and not detach it, which would lose tracking of a
        // row the caller did not stage for removal.
        var ct = TestContext.Current.CancellationToken;
        var seed = NewCustomer("Alice", "rehydrate@example.com");
        (await _context.TryInsertUniqueAsync(seed, ct)).IsSuccess.Should().BeTrue();
        _context.Entry(seed).State.Should().Be(EntityState.Unchanged);
        _context.ChangeTracker.HasChanges().Should().BeFalse();

        var result = await _context.TryInsertUniqueAsync(seed, ct);

        result.IsSuccess.Should().BeFalse();
        _context.Entry(seed).State.Should().Be(EntityState.Unchanged,
            because: "an entry that transitioned from Unchanged to Added during this call must be restored, not left Added or detached");
        _context.ChangeTracker.HasChanges().Should().BeFalse(
            because: "no stray Added state should remain after the duplicate cleanup");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_CancellationDuringSave_CleansUpAddedGraph()
    {
        // A pre-cancelled token is observed before the change tracker is mutated, but
        // cancellation that arrives during SaveChangesAsync (mid-save) propagates out
        // of EF Core as OperationCanceledException. The helper must clean up the
        // entries it attached before rethrowing, otherwise a later SaveChangesAsync on
        // the same DI-scoped DbContext would silently insert the cancelled entity, and
        // HasChanges() would block any retry by tripping the clean-context guard.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CancelDuringSaveInterceptor())
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options;
        using var ctx = new TestDbContext(options);
        ctx.Database.EnsureCreated();
        var customer = NewCustomer("Cancel", "midsave@example.com");

        var act = async () => await ctx.TryInsertUniqueAsync(customer, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ctx.Entry(customer).State.Should().Be(EntityState.Detached,
            because: "OperationCanceledException raised during SaveChangesAsync must not leave the entity attached as Added");
        ctx.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_CancellationDuringSave_RestoresTransitionedEntries()
    {
        // Same scenario as above, but the entity was already tracked as Unchanged when
        // TryInsertUniqueAsync was called and context.Add flipped it to Added. The
        // cancellation-during-save cleanup must mirror the duplicate-key cleanup:
        // entries it newly attached are detached, entries it transitioned are restored.
        var ct = TestContext.Current.CancellationToken;
        using var seedConnection = new SqliteConnection("DataSource=:memory:");
        seedConnection.Open();
        using (var seedCtx = new TestDbContext(seedConnection))
        {
            seedCtx.Database.EnsureCreated();
            (await seedCtx.TryInsertUniqueAsync(NewCustomer("Seed", "midsave-restore@example.com"), ct))
                .IsSuccess.Should().BeTrue();
        }

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(seedConnection)
            .AddInterceptors(new CancelDuringSaveInterceptor())
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options;
        using var ctx = new TestDbContext(options);
        var loaded = (await ctx.Customers.ToListAsync(ct)).Single();
        ctx.Entry(loaded).State.Should().Be(EntityState.Unchanged);

        var act = async () => await ctx.TryInsertUniqueAsync(loaded, ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ctx.Entry(loaded).State.Should().Be(EntityState.Unchanged,
            because: "transitioned entries must be restored to their prior state on the cancellation cleanup path too");
        ctx.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private sealed class CancelDuringSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException("Simulated cancellation during SaveChangesAsync.");
    }

    [Fact]
    public async Task TryInsertUniqueAsync_NullContext_ThrowsArgumentNullException()
    {
        DbContext? nullContext = null;

        var act = async () => await nullContext!.TryInsertUniqueAsync(NewCustomer("X", "x@example.com"));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryInsertUniqueAsync_NullEntity_ThrowsArgumentNullException()
    {
        var act = async () => await _context.TryInsertUniqueAsync<TestCustomer>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}