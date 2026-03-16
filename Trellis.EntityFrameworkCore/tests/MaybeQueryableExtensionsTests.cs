namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="MaybeQueryableExtensions"/> — WhereNone, WhereHasValue, WhereEquals.
/// Validates that expression tree rewriting correctly translates to SQL NULL/value checks.
/// </summary>
public class MaybeQueryableExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public MaybeQueryableExtensionsTests() =>
        (_context, _connection) = TestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region WhereNone — reference type inner (PhoneNumber)

    [Fact]
    public async Task WhereNone_ReturnsEntitiesWithNullBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");

        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereNone(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Name.Should().Be(withoutPhone.Name);
    }

    [Fact]
    public async Task WhereNone_AllHaveValue_ReturnsEmpty()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(CreateCustomer("Alice", "+1-555-0100"));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereNone(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region WhereHasValue — reference type inner (PhoneNumber)

    [Fact]
    public async Task WhereHasValue_ReturnsEntitiesWithNonNullBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");

        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereHasValue(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Name.Should().Be(withPhone.Name);
    }

    [Fact]
    public async Task WhereHasValue_NoneHaveValue_ReturnsEmpty()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(CreateCustomer("Bob"));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereHasValue(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void WhereHasValue_NestedMaybeSelector_ThrowsWithPropertySelectorParamName()
    {
        var act = () => _context.Orders.WhereHasValue(order => order.Customer.Phone);

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "propertySelector");
    }

    #endregion

    #region WhereEquals — reference type inner (PhoneNumber)

    [Fact]
    public async Task WhereEquals_MatchingValue_ReturnsMatchingEntities()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var phone = PhoneNumber.Create("+1-555-0100");
        var alice = CreateCustomer("Alice", "+1-555-0100");
        var bob = CreateCustomer("Bob", "+1-555-0200");
        var charlie = CreateCustomer("Charlie");

        _context.Customers.AddRange(alice, bob, charlie);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereEquals(c => c.Phone, phone)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Name.Should().Be(alice.Name);
    }

    [Fact]
    public async Task WhereEquals_NoMatch_ReturnsEmpty()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var phone = PhoneNumber.Create("+1-555-9999");
        _context.Customers.Add(CreateCustomer("Alice", "+1-555-0100"));
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereEquals(c => c.Phone, phone)
            .ToListAsync(ct);

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region Maybe ordering helpers

    [Fact]
    public async Task OrderByMaybe_ReferenceTypeInner_ReturnsEntitiesOrderedByBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var alice = CreateCustomer("Alice", "+1-555-0100");
        var charlie = CreateCustomer("Charlie", "+1-555-0300");
        var bob = CreateCustomer("Bob", "+1-555-0200");

        _context.Customers.AddRange(charlie, bob, alice);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereHasValue(c => c.Phone)
            .OrderByMaybe(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Select(customer => customer.Name.Value).Should().Equal(["Alice", "Bob", "Charlie"]);
    }

    [Fact]
    public async Task OrderByMaybeDescending_ReferenceTypeInner_ReturnsEntitiesOrderedDescendingByBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var alice = CreateCustomer("Alice", "+1-555-0100");
        var charlie = CreateCustomer("Charlie", "+1-555-0300");
        var bob = CreateCustomer("Bob", "+1-555-0200");

        _context.Customers.AddRange(alice, charlie, bob);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .WhereHasValue(c => c.Phone)
            .OrderByMaybeDescending(c => c.Phone)
            .ToListAsync(ct);

        // Assert
        results.Select(customer => customer.Name.Value).Should().Equal(["Charlie", "Bob", "Alice"]);
    }

    [Fact]
    public async Task ThenByMaybe_ValueTypeInner_UsesBackingFieldForSecondaryOrdering()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Gamma");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var older = CreateOrder(customer.Id);
        older.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));

        var newer = CreateOrder(customer.Id);
        newer.SubmittedAt = Maybe.From(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

        var latest = CreateOrder(customer.Id);
        latest.SubmittedAt = Maybe.From(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));

        _context.Orders.AddRange(newer, latest, older);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereHasValue(o => o.SubmittedAt)
            .OrderBy(o => o.Amount)
            .ThenByMaybe(o => o.SubmittedAt)
            .ToListAsync(ct);

        // Assert
        results.Select(order => order.Id).Should().Equal([older.Id, newer.Id, latest.Id]);
    }

    [Fact]
    public async Task ThenByMaybeDescending_ValueTypeInner_UsesBackingFieldForSecondaryOrdering()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Delta");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var older = CreateOrder(customer.Id);
        older.SubmittedAt = Maybe.From(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));

        var newer = CreateOrder(customer.Id);
        newer.SubmittedAt = Maybe.From(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

        var latest = CreateOrder(customer.Id);
        latest.SubmittedAt = Maybe.From(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));

        _context.Orders.AddRange(older, latest, newer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereHasValue(o => o.SubmittedAt)
            .OrderBy(o => o.Amount)
            .ThenByMaybeDescending(o => o.SubmittedAt)
            .ToListAsync(ct);

        // Assert
        results.Select(order => order.Id).Should().Equal([latest.Id, newer.Id, older.Id]);
    }

    #endregion

    #region WhereNone / WhereHasValue — enum inner (TestOrderStatus)

    [Fact]
    public async Task WhereNone_EnumInner_ReturnsEntitiesWithNullBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var withStatus = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        var withoutStatus = CreateOrder(customer.Id);

        _context.Orders.AddRange(withStatus, withoutStatus);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereNone(o => o.OptionalStatus)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withoutStatus.Id);
    }

    [Fact]
    public async Task WhereHasValue_EnumInner_ReturnsEntitiesWithValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var withStatus = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        var withoutStatus = CreateOrder(customer.Id);

        _context.Orders.AddRange(withStatus, withoutStatus);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereHasValue(o => o.OptionalStatus)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withStatus.Id);
    }

    #endregion

    #region WhereNone / WhereHasValue / WhereEquals — value-type inner (DateTime)

    [Fact]
    public async Task WhereNone_ValueTypeInner_ReturnsEntitiesWithNullBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var submitted = CreateOrder(customer.Id);
        submitted.SubmittedAt = Maybe.From(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var notSubmitted = CreateOrder(customer.Id);

        _context.Orders.AddRange(submitted, notSubmitted);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereNone(o => o.SubmittedAt)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(notSubmitted.Id);
    }

    [Fact]
    public async Task WhereHasValue_ValueTypeInner_ReturnsEntitiesWithValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Bob");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var submitted = CreateOrder(customer.Id);
        submitted.SubmittedAt = Maybe.From(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var notSubmitted = CreateOrder(customer.Id);

        _context.Orders.AddRange(submitted, notSubmitted);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereHasValue(o => o.SubmittedAt)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(submitted.Id);
    }

    [Fact]
    public async Task WhereEquals_ValueTypeInner_ReturnsMatchingEntities()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Charlie");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var targetDate = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var order1 = CreateOrder(customer.Id);
        order1.SubmittedAt = Maybe.From(targetDate);
        var order2 = CreateOrder(customer.Id);
        order2.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc));
        var order3 = CreateOrder(customer.Id); // no SubmittedAt

        _context.Orders.AddRange(order1, order2, order3);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Orders
            .WhereEquals(o => o.SubmittedAt, targetDate)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(order1.Id);
    }

    #endregion

    #region ExecuteUpdate helpers

    [Fact]
    public async Task SetMaybeValue_ExecuteUpdate_SetsMappedBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Echo");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var updatedPhone = PhoneNumber.Create("+1-555-0700");

        // Act
        await _context.Customers
            .Where(c => c.Id == customer.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetMaybeValue(c => c.Phone, updatedPhone),
                ct);

        _context.ChangeTracker.Clear();
        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Phone.HasValue.Should().BeTrue();
        loaded.Phone.Value.Value.Should().Be(updatedPhone.Value);
    }

    [Fact]
    public async Task SetMaybeNone_ExecuteUpdate_ClearsMappedBackingField()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Foxtrot", "+1-555-0800");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        await _context.Customers
            .Where(c => c.Id == customer.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetMaybeNone(c => c.Phone),
                ct);

        _context.ChangeTracker.Clear();
        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Phone.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task SetMaybeValue_ExecuteUpdate_SetsMappedBackingField_ForValueTypeInner()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Golf");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var order = CreateOrder(customer.Id);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var submittedAt = new DateTime(2026, 4, 15, 9, 30, 0, DateTimeKind.Utc);

        // Act
        await _context.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetMaybeValue(o => o.SubmittedAt, submittedAt),
                ct);

        _context.ChangeTracker.Clear();
        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.SubmittedAt.HasValue.Should().BeTrue();
        loaded.SubmittedAt.Value.Should().Be(submittedAt);
    }

    [Fact]
    public async Task SetMaybeNone_ExecuteUpdate_ClearsMappedBackingField_ForEnumInner()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Hotel");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var order = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        await _context.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetMaybeNone(o => o.OptionalStatus),
                ct);

        _context.ChangeTracker.Clear();
        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.OptionalStatus.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task SetMaybeValue_ExecuteUpdate_SetsMappedBackingField_ForInheritedMaybeProperty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new InheritedMaybeQueryDbContext(
            new DbContextOptionsBuilder<InheritedMaybeQueryDbContext>()
                .UseSqlite(connection)
                .Options);
        context.Database.EnsureCreated();

        var customer = new DerivedTestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("InheritedValue"),
            Email = EmailAddress.Create("inheritedvalue@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        context.DerivedCustomers.Add(customer);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var updatedPhone = PhoneNumber.Create("+1-555-0900");

        await context.DerivedCustomers
            .Where(c => c.Id == customer.Id)
            .ExecuteUpdateAsync(setters => setters.SetMaybeValue(c => c.Phone, updatedPhone), ct);

        context.ChangeTracker.Clear();
        var loaded = await context.DerivedCustomers.FindAsync([customer.Id], ct);

        loaded.Should().NotBeNull();
        loaded!.Phone.HasValue.Should().BeTrue();
        loaded.Phone.Value.Value.Should().Be(updatedPhone.Value);
    }

    [Fact]
    public async Task SetMaybeNone_ExecuteUpdate_ClearsMappedBackingField_ForInheritedMaybeProperty()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new InheritedMaybeQueryDbContext(
            new DbContextOptionsBuilder<InheritedMaybeQueryDbContext>()
                .UseSqlite(connection)
                .Options);
        context.Database.EnsureCreated();

        var customer = new DerivedTestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("InheritedNone"),
            Email = EmailAddress.Create("inheritednone@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-555-0910"))
        };

        context.DerivedCustomers.Add(customer);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        await context.DerivedCustomers
            .Where(c => c.Id == customer.Id)
            .ExecuteUpdateAsync(setters => setters.SetMaybeNone(c => c.Phone), ct);

        context.ChangeTracker.Clear();
        var loaded = await context.DerivedCustomers.FindAsync([customer.Id], ct);

        loaded.Should().NotBeNull();
        loaded!.Phone.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static TestCustomer CreateCustomer(string name, string? phone = null)
    {
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create(name),
            Email = EmailAddress.Create($"{name.ToLowerInvariant()}@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        if (phone is not null)
            customer.Phone = Maybe.From(PhoneNumber.Create(phone));

        return customer;
    }

    private static TestOrder CreateOrder(TestCustomerId customerId, TestOrderStatus? optionalStatus = null)
    {
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 100m,
            Status = TestOrderStatus.Draft
        };

        if (optionalStatus is not null)
            order.OptionalStatus = Maybe.From(optionalStatus);

        return order;
    }

    private sealed class InheritedMaybeQueryDbContext(DbContextOptions<InheritedMaybeQueryDbContext> options)
        : DbContext(options)
    {
        public DbSet<DerivedTestCustomer> DerivedCustomers => Set<DerivedTestCustomer>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DerivedTestCustomer>(builder =>
            {
                builder.HasKey(customer => customer.Id);
                builder.Property(customer => customer.Name).HasMaxLength(100).IsRequired();
                builder.Property(customer => customer.Email).HasMaxLength(254).IsRequired();
                builder.Property(customer => customer.CreatedAt).IsRequired();
            });
    }

    #endregion
}