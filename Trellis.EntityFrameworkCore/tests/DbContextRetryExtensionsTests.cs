using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="DbContextRetryExtensions.SaveChangesWithRetryAsync(DbContext, Func{DbUpdateException, bool}, Func{IReadOnlyList{EntityEntry}, int, CancellationToken, ValueTask{bool}}, int, CancellationToken)"/>.
/// </summary>
public class DbContextRetryExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public DbContextRetryExtensionsTests() =>
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

    private async Task SeedAsync(string email, CancellationToken ct)
    {
        _context.Customers.Add(NewCustomer("Seed", email));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task First_attempt_succeeds_returns_ok_and_regenerate_is_not_called()
    {
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(NewCustomer("Alice", "alice@example.com"));

        var regenerateCalls = 0;
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => true,
            regenerate: (_, _, _) => { regenerateCalls++; return ValueTask.FromResult(true); },
            cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        regenerateCalls.Should().Be(0);
        (await _context.Customers.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Retry_succeeds_after_regenerate_mutates_email()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        var customer = NewCustomer("Bob", "taken@example.com");
        _context.Customers.Add(customer);

        var attempts = new List<int>();
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (entries, attempt, _) =>
            {
                attempts.Add(attempt);
                foreach (var entry in entries)
                    if (entry.Entity is TestCustomer c)
                        c.Email = EmailAddress.Create($"regenerated-{attempt}@example.com");
                return ValueTask.FromResult(true);
            },
            cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        attempts.Should().ContainSingle().Which.Should().Be(1);
        customer.Email.Value.Should().Be("regenerated-1@example.com");
        (await _context.Customers.CountAsync(ct)).Should().Be(2);
    }

    [Fact]
    public async Task Non_retryable_failure_returns_conflict_immediately_without_calling_regenerate()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        _context.Customers.Add(NewCustomer("Bob", "taken@example.com"));

        var regenerateCalls = 0;
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => false,
            regenerate: (_, _, _) => { regenerateCalls++; return ValueTask.FromResult(true); },
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be("duplicate.key");
        regenerateCalls.Should().Be(0);
        _context.ChangeTracker.Entries<TestCustomer>().Should().HaveCount(1,
            "change tracker is left untouched when shouldRetry returns false");
    }

    [Fact]
    public async Task Regenerate_returning_false_returns_conflict_and_leaves_entries_detached()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        var customer = NewCustomer("Bob", "taken@example.com");
        _context.Customers.Add(customer);

        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => ValueTask.FromResult(false),
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.Detail.Should().Contain("Retry aborted by regenerate callback");
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be("retry.aborted");
        _context.Entry(customer).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task Exhausting_max_attempts_returns_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        var customer = NewCustomer("Bob", "taken@example.com");
        _context.Customers.Add(customer);

        var attempts = new List<int>();
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, attempt, _) =>
            {
                attempts.Add(attempt);
                // Do NOT mutate the email — every retry collides again.
                return ValueTask.FromResult(true);
            },
            maxAttempts: 3,
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.Detail.Should().Contain("Maximum retry attempts");
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be("retry.exhausted");
        attempts.Should().HaveCount(2);
        attempts[0].Should().Be(1);
        attempts[1].Should().Be(2);
    }

    [Fact]
    public async Task Detaches_only_DbUpdateException_Entries_sibling_aggregate_survives_on_abort()
    {
        var ct = TestContext.Current.CancellationToken;
        var seedCustomer = NewCustomer("Seed", "taken@example.com");
        _context.Customers.Add(seedCustomer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var conflicting = NewCustomer("Bob", "taken@example.com");
        var siblingOrder = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = seedCustomer.Id,
            Amount = 100m,
            Status = TestOrderStatus.Draft,
        };
        _context.Customers.Add(conflicting);
        _context.Orders.Add(siblingOrder);

        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => ValueTask.FromResult(false),
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        _context.Entry(conflicting).State.Should().Be(EntityState.Detached);
        _context.Entry(siblingOrder).State.Should().Be(EntityState.Added,
            "sibling aggregate not in DbUpdateException.Entries must not be touched");
    }

    [Fact]
    public async Task Detaches_only_DbUpdateException_Entries_sibling_aggregate_persists_on_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var seedCustomer = NewCustomer("Seed", "taken@example.com");
        _context.Customers.Add(seedCustomer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var conflicting = NewCustomer("Bob", "taken@example.com");
        var siblingOrder = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = seedCustomer.Id,
            Amount = 100m,
            Status = TestOrderStatus.Draft,
        };
        _context.Customers.Add(conflicting);
        _context.Orders.Add(siblingOrder);

        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (entries, _, _) =>
            {
                foreach (var entry in entries)
                    if (entry.Entity is TestCustomer c)
                        c.Email = EmailAddress.Create("bob-fixed@example.com");
                return ValueTask.FromResult(true);
            },
            cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        (await _context.Customers.CountAsync(ct)).Should().Be(2);
        (await _context.Orders.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Sibling_promoted_via_entry_State_Added_survives_detach()
    {
        var ct = TestContext.Current.CancellationToken;
        var seedCustomer = NewCustomer("Seed", "taken@example.com");
        _context.Customers.Add(seedCustomer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var conflicting = NewCustomer("Bob", "taken@example.com");
        var promotedSibling = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = seedCustomer.Id,
            Amount = 50m,
            Status = TestOrderStatus.Draft,
        };
        _context.Customers.Add(conflicting);
        // Sibling promoted via Entry().State = Added rather than DbSet.Add. The naive
        // "detach all Added entries" loop would clobber this; the retry helper must not.
        _context.Entry(promotedSibling).State = EntityState.Added;

        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => ValueTask.FromResult(false),
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        _context.Entry(promotedSibling).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task Conflict_re_added_entity_carries_regenerated_value_into_next_save()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("first@example.com", ct);
        await SeedAsync("second@example.com", ct);

        var customer = NewCustomer("Bob", "first@example.com");
        _context.Customers.Add(customer);

        var emails = new[] { "second@example.com", "third@example.com" };
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (entries, attempt, _) =>
            {
                foreach (var entry in entries)
                    if (entry.Entity is TestCustomer c)
                        c.Email = EmailAddress.Create(emails[attempt - 1]);
                return ValueTask.FromResult(true);
            },
            maxAttempts: 3,
            cancellationToken: ct);

        result.IsSuccess.Should().BeTrue();
        customer.Email.Value.Should().Be("third@example.com");
        (await _context.Customers.CountAsync(ct)).Should().Be(3);
    }

    [Fact]
    public async Task Concurrency_exception_bypasses_classifier_and_maps_to_concurrent_modification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (faultCtx, faultConn) = FaultInjectingTestDbContext.CreateInMemory();
        try
        {
            faultCtx.Customers.Add(NewCustomer("Bob", "bob@example.com"));
            faultCtx.OnSaveChanges = _ => new DbUpdateConcurrencyException(
                "Simulated concurrency conflict");

            var classifierWasCalled = false;
            var regenerateWasCalled = false;

            var result = await faultCtx.SaveChangesWithRetryAsync(
                shouldRetry: _ => { classifierWasCalled = true; return true; },
                regenerate: (_, _, _) => { regenerateWasCalled = true; return ValueTask.FromResult(true); },
                cancellationToken: ct);

            result.IsFailure.Should().BeTrue();
            result.Error.Should().BeOfType<Error.Conflict>()
                .Which.ReasonCode.Should().Be(FaultCodes.ConcurrentModification);
            classifierWasCalled.Should().BeFalse("concurrency exceptions must bypass shouldRetry");
            regenerateWasCalled.Should().BeFalse();
        }
        finally
        {
            faultCtx.Dispose();
            faultConn.Dispose();
        }
    }

    [Fact]
    public async Task Non_retryable_foreign_key_failure_maps_to_referential_integrity()
    {
        var ct = TestContext.Current.CancellationToken;
        _context.Orders.Add(new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = TestCustomerId.NewUniqueV4(), // non-existent customer
            Amount = 1m,
            Status = TestOrderStatus.Draft,
        });

        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => false,
            regenerate: (_, _, _) => ValueTask.FromResult(true),
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be("referential.integrity");
    }

    [Fact]
    public async Task ShouldRetry_exception_propagates_with_no_detach()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        var customer = NewCustomer("Bob", "taken@example.com");
        _context.Customers.Add(customer);

        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => throw new InvalidOperationException("classifier-boom"),
            regenerate: (_, _, _) => ValueTask.FromResult(true),
            cancellationToken: ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("classifier-boom");
        _context.Entry(customer).State.Should().Be(EntityState.Added,
            "classifier exceptions propagate before any detach");
    }

    [Fact]
    public async Task Regenerate_exception_propagates_with_entries_detached()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        var customer = NewCustomer("Bob", "taken@example.com");
        _context.Customers.Add(customer);

        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => throw new InvalidOperationException("regenerate-boom"),
            cancellationToken: ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("regenerate-boom");
        _context.Entry(customer).State.Should().Be(EntityState.Detached,
            "detach happens before regenerate is invoked");
    }

    [Fact]
    public async Task Non_DbUpdateException_propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var (faultCtx, faultConn) = FaultInjectingTestDbContext.CreateInMemory();
        try
        {
            faultCtx.Customers.Add(NewCustomer("Bob", "bob@example.com"));
            faultCtx.OnSaveChanges = _ => new InvalidOperationException("savechanges-boom");

            var act = async () => await faultCtx.SaveChangesWithRetryAsync(
                shouldRetry: _ => true,
                regenerate: (_, _, _) => ValueTask.FromResult(true),
                cancellationToken: ct);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("savechanges-boom");
        }
        finally
        {
            faultCtx.Dispose();
            faultConn.Dispose();
        }
    }

    [Fact]
    public async Task OperationCanceledException_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _context.Customers.Add(NewCustomer("Bob", "bob@example.com"));

        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => true,
            regenerate: (_, _, _) => ValueTask.FromResult(true),
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Unknown_non_classified_DbUpdateException_is_rethrown_when_not_retryable()
    {
        var ct = TestContext.Current.CancellationToken;
        var (faultCtx, faultConn) = FaultInjectingTestDbContext.CreateInMemory();
        try
        {
            faultCtx.Customers.Add(NewCustomer("Bob", "bob@example.com"));
            faultCtx.OnSaveChanges = _ => new DbUpdateException(
                "unknown-dbupdate-boom",
                new InvalidOperationException("inner-not-recognized"));

            var act = async () => await faultCtx.SaveChangesWithRetryAsync(
                shouldRetry: _ => false,
                regenerate: (_, _, _) => ValueTask.FromResult(true),
                cancellationToken: ct);

            await act.Should().ThrowAsync<DbUpdateException>()
                .WithMessage("unknown-dbupdate-boom");
        }
        finally
        {
            faultCtx.Dispose();
            faultConn.Dispose();
        }
    }

    [Fact]
    public async Task Retry_with_non_Added_entry_throws_InvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        // Seed a row that owns "occupied@example.com".
        await SeedAsync("occupied@example.com", ct);

        // Insert a second row we can later modify; then update its email to collide.
        var customer = NewCustomer("Bob", "bob@example.com");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        // Mutate the email to one already in use — Modified-state save raises a unique-key
        // DbUpdateException whose Entries contains a Modified entry. The retry helper must
        // throw InvalidOperationException rather than attempt the fragile detach/re-Modify cycle.
        customer.Email = EmailAddress.Create("occupied@example.com");

        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => ValueTask.FromResult(true),
            cancellationToken: ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Added state*Modified*");
    }

    [Fact]
    public async Task Null_db_throws_ArgumentNullException()
    {
        var act = async () => await DbContextRetryExtensions.SaveChangesWithRetryAsync(
            db: null!,
            shouldRetry: _ => true,
            regenerate: (_, _, _) => ValueTask.FromResult(true));

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("db");
    }

    [Fact]
    public async Task Null_shouldRetry_throws_ArgumentNullException()
    {
        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: null!,
            regenerate: (_, _, _) => ValueTask.FromResult(true));

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("shouldRetry");
    }

    [Fact]
    public async Task Null_regenerate_throws_ArgumentNullException()
    {
        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => true,
            regenerate: null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("regenerate");
    }

    [Fact]
    public async Task MaxAttempts_less_than_1_throws_ArgumentOutOfRangeException()
    {
        var act = async () => await _context.SaveChangesWithRetryAsync(
            shouldRetry: _ => true,
            regenerate: (_, _, _) => ValueTask.FromResult(true),
            maxAttempts: 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("maxAttempts");
    }

    [Fact]
    public async Task MaxAttempts_1_with_retryable_failure_maps_to_conflict_without_calling_regenerate()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync("taken@example.com", ct);

        _context.Customers.Add(NewCustomer("Bob", "taken@example.com"));

        var regenerateCalls = 0;
        var result = await _context.SaveChangesWithRetryAsync(
            shouldRetry: DbExceptionClassifier.IsDuplicateKey,
            regenerate: (_, _, _) => { regenerateCalls++; return ValueTask.FromResult(true); },
            maxAttempts: 1,
            cancellationToken: ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.Detail.Should().Contain("Maximum retry attempts");
        result.Error.Should().BeOfType<Error.Conflict>()
            .Which.ReasonCode.Should().Be("retry.exhausted");
        regenerateCalls.Should().Be(0);
    }
}