namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="MaybeConvention"/> and source-generated <c>partial Maybe&lt;T&gt;</c> properties.
/// Validates that Maybe{T} properties backed by generated private nullable fields
/// round-trip correctly through EF Core with SQLite in-memory.
/// </summary>
public class MaybePropertyTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public MaybePropertyTests() =>
        (_context, _connection) = TestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Maybe<T> with scalar value object (reference type inner)

    [Fact]
    public async Task MaybeScalar_WithValue_RoundTripPreservesValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var phone = PhoneNumber.Create("+1-555-0100");
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(phone)
        };

        // Act
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Phone.HasValue.Should().BeTrue();
        loaded.Phone.Value.Value.Should().Be(phone.Value);
    }

    [Fact]
    public async Task MaybeScalar_WithNone_RoundTripPreservesNone()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = DateTime.UtcNow
            // Phone is default (Maybe.None<PhoneNumber>())
        };

        // Act
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Phone.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task MaybeScalar_SetThenClear_RoundTripsCorrectly()
    {
        // Arrange — start with a phone number
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Charlie"),
            Email = EmailAddress.Create("charlie@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-555-0200"))
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        // Act — clear to None
        _context.ChangeTracker.Clear();
        var tracked = await _context.Customers.FindAsync([customer.Id], ct);
        tracked!.Phone = Maybe.None<PhoneNumber>();
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();
        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Phone.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Maybe<T> with value type (DateTime inner)

    [Fact]
    public async Task MaybeValueType_WithValue_RoundTripPreservesValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("ValueTypeCustomer"),
            Email = EmailAddress.Create("valuetype@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var submittedAt = new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc);
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 99.95m,
            Status = TestOrderStatus.Confirmed,
            SubmittedAt = Maybe.From(submittedAt)
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.SubmittedAt.HasValue.Should().BeTrue();
        loaded.SubmittedAt.Value.Should().Be(submittedAt);
    }

    [Fact]
    public async Task MaybeValueType_WithNone_RoundTripPreservesNone()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("ValueTypeNoneCustomer"),
            Email = EmailAddress.Create("valuetypenone@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 50m,
            Status = TestOrderStatus.Draft
            // SubmittedAt is default (Maybe.None<DateTime>())
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.SubmittedAt.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Maybe<T> with RequiredEnum (enum inner type)

    [Fact]
    public async Task MaybeEnum_WithValue_RoundTripPreservesValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("EnumCustomer"),
            Email = EmailAddress.Create("enum@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 42m,
            Status = TestOrderStatus.Draft,
            OptionalStatus = Maybe.From(TestOrderStatus.Shipped)
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.OptionalStatus.HasValue.Should().BeTrue();
        loaded.OptionalStatus.Value.Should().Be(TestOrderStatus.Shipped);
    }

    [Fact]
    public async Task MaybeEnum_WithNone_RoundTripPreservesNone()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("EnumNoneCustomer"),
            Email = EmailAddress.Create("enumnone@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 50m,
            Status = TestOrderStatus.Confirmed
            // OptionalStatus is default (Maybe.None<TestOrderStatus>())
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.OptionalStatus.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Column metadata

    [Fact]
    public void MaybeConvention_BackingField_IsNullableInModel()
    {
        var customerType = _context.Model.FindEntityType(typeof(TestCustomer))!;
        var phoneProp = customerType.FindProperty("_phone")!;

        phoneProp.IsNullable.Should().BeTrue("Maybe<T> backing field should be nullable");
    }

    [Fact]
    public void MaybeConvention_BackingField_ColumnName_UsesPropertyName()
    {
        var customerType = _context.Model.FindEntityType(typeof(TestCustomer))!;
        var phoneProp = customerType.FindProperty("_phone")!;

        phoneProp.GetColumnName().Should().Be("Phone", "column name should use the original property name, not the backing field name");
    }

    [Fact]
    public void MaybeConvention_MaybeProperty_IsIgnored()
    {
        var customerType = _context.Model.FindEntityType(typeof(TestCustomer))!;
        var phoneProp = customerType.FindProperty("Phone");

        phoneProp.Should().BeNull("The Maybe<T> CLR property should be ignored by EF Core");
    }

    [Fact]
    public void MaybeConvention_ValueType_BackingField_IsNullableInModel()
    {
        var orderType = _context.Model.FindEntityType(typeof(TestOrder))!;
        var submittedAtProp = orderType.FindProperty("_submittedAt")!;

        submittedAtProp.IsNullable.Should().BeTrue("Maybe<DateTime> backing field should be nullable");
    }

    [Fact]
    public void MaybeConvention_Enum_BackingField_IsNullableInModel()
    {
        var orderType = _context.Model.FindEntityType(typeof(TestOrder))!;
        var optionalStatusProp = orderType.FindProperty("_optionalStatus")!;

        optionalStatusProp.IsNullable.Should().BeTrue("Maybe<TestOrderStatus> backing field should be nullable");
    }

    [Fact]
    public void GetMaybePropertyMappings_ReturnsResolvedMaybeMappings()
    {
        var mappings = _context.Model.GetMaybePropertyMappings();
        var phoneMapping = mappings.Single(mapping =>
            mapping.EntityClrType == typeof(TestCustomer)
            && mapping.PropertyName == nameof(TestCustomer.Phone));

        phoneMapping.BackingFieldName.Should().Be("_phone");
        phoneMapping.ColumnName.Should().Be(nameof(TestCustomer.Phone));
        phoneMapping.IsMapped.Should().BeTrue();
        phoneMapping.IsNullable.Should().BeTrue();
        phoneMapping.ProviderClrType.Should().Be<string>();
    }

    [Fact]
    public void ToMaybeMappingDebugString_ContainsResolvedPropertyDetails()
    {
        var debugString = _context.ToMaybeMappingDebugString();

        debugString.Should().Contain("TestCustomer.Phone => field=_phone");
        debugString.Should().Contain("column=Phone");
    }

    #endregion

    #region HasTrellisIndex

    [Fact]
    public void HasTrellisIndex_MaybeProperty_UsesBackingFieldName()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new IndexedMaybeDbContext(
            new DbContextOptionsBuilder<IndexedMaybeDbContext>()
                .UseSqlite(connection)
                .Options);

        var customerType = context.Model.FindEntityType(typeof(TestCustomer))!;
        var index = customerType.GetIndexes()
            .Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(["_phone"]));

        index.Properties.Select(property => property.Name).Should().Equal(["_phone"]);
    }

    [Fact]
    public void HasTrellisIndex_CompositeSelector_ResolvesMixedMaybeAndRegularProperties()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new IndexedMaybeDbContext(
            new DbContextOptionsBuilder<IndexedMaybeDbContext>()
                .UseSqlite(connection)
                .Options);

        var orderType = context.Model.FindEntityType(typeof(TestOrder))!;
        var index = orderType.GetIndexes()
            .Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(TestOrder.Status), "_submittedAt"]));

        index.Properties.Select(property => property.Name).Should().Equal([nameof(TestOrder.Status), "_submittedAt"]);
    }

    [Fact]
    public void HasTrellisIndex_MaybeProperty_PreservesConventionMapping()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new IndexedMaybeDbContext(
            new DbContextOptionsBuilder<IndexedMaybeDbContext>()
                .UseSqlite(connection)
                .Options);

        var customerType = context.Model.FindEntityType(typeof(TestCustomer))!;
        var phoneProperty = customerType.FindProperty("_phone")!;

        phoneProperty.IsNullable.Should().BeTrue("indexed Maybe<T> backing fields should remain optional");
        phoneProperty.GetColumnName().Should().Be(nameof(TestCustomer.Phone), "indexed Maybe<T> backing fields should still map to the CLR property column name");
    }

    [Fact]
    public void HasTrellisIndex_InheritedMaybeProperty_FindsBackingFieldOnBaseType()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new InheritedMaybeIndexedDbContext(
            new DbContextOptionsBuilder<InheritedMaybeIndexedDbContext>()
                .UseSqlite(connection)
                .Options);

        var customerType = context.Model.FindEntityType(typeof(DerivedTestCustomer))!;
        var index = customerType.GetIndexes()
            .Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(["_phone"]));
        var phoneProperty = customerType.FindProperty("_phone")!;

        index.Properties.Select(property => property.Name).Should().Equal(["_phone"]);
        phoneProperty.GetColumnName().Should().Be(nameof(TestCustomer.Phone));
    }

    [Fact]
    public void HasTrellisIndex_InvalidSelectorShape_ThrowsWithPropertySelectorParamName()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new InvalidSelectorIndexedDbContext(
            new DbContextOptionsBuilder<InvalidSelectorIndexedDbContext>()
                .UseSqlite(connection)
                .Options);

        var act = () => context.Model;

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "propertySelector");
    }

    [Fact]
    public void HasTrellisIndex_NestedMaybeSelector_ThrowsWithPropertySelectorParamName()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new NestedMaybeSelectorIndexedDbContext(
            new DbContextOptionsBuilder<NestedMaybeSelectorIndexedDbContext>()
                .UseSqlite(connection)
                .Options);

        var act = () => context.Model;

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "propertySelector");
    }

    #endregion

    #region Navigation property with Include

    [Fact]
    public async Task MaybeScalar_NavigationInclude_LoadsRelatedEntityWithMaybeProperty()
    {
        // Arrange
        var (context, connection) = TestDbContext.CreateInMemory();
        using var _ = connection;
        await using var __ = context;
        var ct = TestContext.Current.CancellationToken;

        var phone = PhoneNumber.Create("+1-555-0199");
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("NavAlice"),
            Email = EmailAddress.Create("navalice@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(phone)
        };
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 42.50m,
            Status = TestOrderStatus.Confirmed
        };

        context.Customers.Add(customer);
        context.Orders.Add(order);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        // Act
        var loaded = await context.Orders
            .Include(o => o.Customer)
            .FirstAsync(o => o.Id == order.Id, ct);

        // Assert
        loaded.Customer.Should().NotBeNull();
        loaded.Customer.Name.Value.Should().Be("NavAlice");
        loaded.Customer.Phone.HasValue.Should().BeTrue();
        loaded.Customer.Phone.Value.Value.Should().Be(phone.Value);
    }

    #endregion

    #region MaybeConvention — entity without backing field fails fast

    [Fact]
    public void MaybeConvention_NoBackingField_ThrowsClearException()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new NoBackingFieldDbContext(
            new DbContextOptionsBuilder<NoBackingFieldDbContext>().UseSqlite(connection).Options);

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Website*_website*partial*");
    }

    [Fact]
    public void HasTrellisIndex_NoBackingField_ThrowsClearException()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var context = new NoBackingFieldIndexedDbContext(
            new DbContextOptionsBuilder<NoBackingFieldIndexedDbContext>().UseSqlite(connection).Options);

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Website*_website*");
    }

    private class EntityWithoutBackingField
    {
        public Guid Id { get; set; }
        public Maybe<Url> Website { get; set; }
    }

    private class NoBackingFieldDbContext(DbContextOptions<NoBackingFieldDbContext> options)
        : DbContext(options)
    {
        public DbSet<EntityWithoutBackingField> Items => Set<EntityWithoutBackingField>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntityWithoutBackingField>(b => b.HasKey(e => e.Id));
    }

    private class NoBackingFieldIndexedDbContext(DbContextOptions<NoBackingFieldIndexedDbContext> options)
        : DbContext(options)
    {
        public DbSet<EntityWithoutBackingField> Items => Set<EntityWithoutBackingField>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntityWithoutBackingField>(builder =>
            {
                builder.HasKey(entity => entity.Id);
                builder.HasTrellisIndex(entity => entity.Website);
            });
    }

    private class IndexedMaybeDbContext(DbContextOptions<IndexedMaybeDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestCustomer>(builder =>
            {
                builder.HasKey(customer => customer.Id);
                builder.HasTrellisIndex(customer => customer.Phone);
            });

            modelBuilder.Entity<TestOrder>(builder =>
            {
                builder.HasKey(order => order.Id);
                builder.HasTrellisIndex(order => new { order.Status, order.SubmittedAt });
            });
        }
    }

    private class InheritedMaybeIndexedDbContext(DbContextOptions<InheritedMaybeIndexedDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DerivedTestCustomer>(builder =>
            {
                builder.HasKey(customer => customer.Id);
                builder.HasTrellisIndex(customer => customer.Phone);
            });
    }

    private class InvalidSelectorIndexedDbContext(DbContextOptions<InvalidSelectorIndexedDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestCustomer>(builder =>
            {
                builder.HasKey(customer => customer.Id);
                builder.HasTrellisIndex(customer => customer.Name.Value.Contains('a'));
            });
    }

    private class NestedMaybeSelectorIndexedDbContext(DbContextOptions<NestedMaybeSelectorIndexedDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestOrder>(builder =>
            {
                builder.HasKey(order => order.Id);
                builder.HasTrellisIndex(order => order.Customer.Phone);
            });
    }

    #endregion
}