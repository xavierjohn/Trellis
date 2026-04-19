namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>.
/// Validates that convention-based value converters are automatically registered for all
/// Trellis value object types and that round-trip save/load works correctly with SQLite in-memory.
/// </summary>
public class ApplyTrellisConventionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public ApplyTrellisConventionsTests() =>
        (_context, _connection) = TestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region RequiredGuid property converter

    [Fact]
    public async Task RequiredGuidProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        var customer = new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customerId], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(customerId);
    }

    #endregion

    #region RequiredString property converter

    [Fact]
    public async Task RequiredStringProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob Smith"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Value.Should().Be("Bob Smith");
    }

    #endregion

    #region RequiredEnum property converter

    [Fact]
    public async Task RequiredEnumProperty_StoredAsStringRoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("Customer"),
            Email = EmailAddress.Create("cust@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 99.99m,
            Status = TestOrderStatus.Confirmed
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(TestOrderStatus.Confirmed);
        loaded.Status.Value.Should().Be("Confirmed");
    }

    [Fact]
    public async Task RequiredEnumProperty_InvalidPersistedValue_ThrowsClearMappingException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await ExecuteNonQueryAsync(
            "INSERT INTO Customers (Id, Name, Email, CreatedAt) VALUES ($id, $name, $email, $createdAt)",
            ct,
            ("$id", customerId),
            ("$name", "Customer"),
            ("$email", "customer@example.com"),
            ("$createdAt", DateTime.UtcNow));

        await ExecuteNonQueryAsync(
            "INSERT INTO Orders (Id, CustomerId, Amount, Status) VALUES ($id, $customerId, $amount, $status)",
            ct,
            ("$id", orderId),
            ("$customerId", customerId),
            ("$amount", 99.99m),
            ("$status", "NotAStatus"));

        // Act
        var act = async () => await _context.Orders
            .AsNoTracking()
            .SingleAsync(order => order.Id == TestOrderId.Create(orderId), ct);

        // Assert
        var ex = await act.Should().ThrowAsync<TrellisPersistenceMappingException>();
        ex.Which.Message.Should().Contain("TestOrderStatus");
        ex.Which.Message.Should().Contain("NotAStatus");
        ex.Which.Message.Should().Contain("TryFromName");
        ex.Which.Message.Should().Contain("Valid values");
    }

    [Fact]
    public void SymbolicConverter_NullValue_ThrowsClearMappingException()
    {
        // Arrange — compile the converter expression directly to bypass EF Core's null short-circuit
        var converter = new TrellisScalarConverter<TestOrderStatus, string>();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        // Act
        var act = () => fromProvider(null!);

        // Assert
        var ex = act.Should().Throw<TrellisPersistenceMappingException>();
        ex.Which.Message.Should().Contain("TestOrderStatus");
        ex.Which.Message.Should().Contain("TryFromName");
        ex.Which.Message.Should().Contain("null");
    }

    [Fact]
    public void RequiredEnumProperty_UsesCategoryDrivenConverter()
    {
        // Act
        var property = _context.Model.FindEntityType(typeof(TestOrder))!
            .FindProperty(nameof(TestOrder.Status))!;
        var converter = property.GetTypeMapping().Converter;

        // Assert
        converter.Should().NotBeNull();
        converter.Should().BeOfType<TrellisScalarConverter<TestOrderStatus, string>>();
    }

    #endregion

    #region Built-in EmailAddress converter

    [Fact]
    public async Task BuiltInEmailAddress_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Charlie"),
            Email = EmailAddress.Create("charlie@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("charlie@example.com");
    }

    [Fact]
    public async Task ScalarValueProperty_InvalidPersistedValue_ThrowsClearMappingException()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = Guid.NewGuid();

        await ExecuteNonQueryAsync(
            "INSERT INTO Customers (Id, Name, Email, CreatedAt) VALUES ($id, $name, $email, $createdAt)",
            ct,
            ("$id", customerId),
            ("$name", "Customer"),
            ("$email", "not-an-email"),
            ("$createdAt", DateTime.UtcNow));

        // Act
        var act = async () => await _context.Customers
            .AsNoTracking()
            .SingleAsync(customer => customer.Id == TestCustomerId.Create(customerId), ct);

        // Assert
        var ex = await act.Should().ThrowAsync<TrellisPersistenceMappingException>();
        ex.Which.Message.Should().Contain("EmailAddress");
        ex.Which.Message.Should().Contain("not-an-email");
        ex.Which.Message.Should().Contain("TryCreate");
        ex.Which.Message.Should().Contain("not valid");
    }

    #endregion

    #region Manual HasConversion takes precedence

    [Fact]
    public async Task ManualHasConversion_TakesPrecedence()
    {
        // Arrange — build a model with an explicit converter on a property
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ManualConverterDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new ManualConverterDbContext(options);

        // If we get here without exception, the model was built successfully
        // The manual converter takes precedence
        await context.Database.EnsureCreatedAsync(ct);

        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Test"),
            Email = EmailAddress.Create("test@example.com"),
            CreatedAt = DateTime.UtcNow
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync(ct);

        context.ChangeTracker.Clear();

        var loaded = await context.Customers.FindAsync([customer.Id], ct);
        loaded.Should().NotBeNull();
        // Manual converter uppercases the name
        loaded!.Name.Value.Should().Be("TEST");
    }

    /// <summary>
    /// DbContext with a manual converter that uppercases customer names.
    /// The inline <c>HasConversion</c> in <c>OnModelCreating</c> should override
    /// the convention-set converter from <c>ApplyTrellisConventions</c>.
    /// </summary>
    private class ManualConverterDbContext : DbContext
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();

        public ManualConverterDbContext(DbContextOptions<ManualConverterDbContext> options)
            : base(options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                // Manual converter — uppercases names on write
                // Should override the convention-set converter for Name
                b.Property(c => c.Name)
                    .HasConversion(
                        name => name.Value.ToUpperInvariant(),
                        str => TestCustomerName.Create(str))
                    .HasMaxLength(100);
                b.Property(c => c.Email).HasMaxLength(254);
                b.Property(c => c.CreatedAt).IsRequired();
            });
    }

    #endregion

    #region Non-Trellis properties ignored

    [Fact]
    public async Task NonTrellisProperties_Ignored()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Dave"),
            Email = EmailAddress.Create("dave@example.com"),
            CreatedAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act — standard DateTime and decimal properties should not be affected
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.CreatedAt.Should().Be(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region RequiredInt property converter

    [Fact]
    public async Task RequiredIntProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("IntCustomer"),
            Email = EmailAddress.Create("int@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 50m,
            Status = TestOrderStatus.Draft,
            TicketNumber = Maybe.From(TestTicketNumber.Create(42))
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.TicketNumber.HasValue.Should().BeTrue();
        loaded.TicketNumber.Value.Value.Should().Be(42);
    }

    #endregion

    #region RequiredDecimal property converter

    [Fact]
    public async Task RequiredDecimalProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("DecCustomer"),
            Email = EmailAddress.Create("dec@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 100m,
            Status = TestOrderStatus.Draft,
            UnitPrice = Maybe.From(TestUnitPrice.Create(19.99m))
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.UnitPrice.HasValue.Should().BeTrue();
        loaded.UnitPrice.Value.Value.Should().Be(19.99m);
    }

    #endregion

    #region Internal Trellis types in scanned assembly

    [Fact]
    public async Task InternalScalarValueType_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<InternalValueObjectDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new InternalValueObjectDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);

        var entity = new InternalValueEntity
        {
            Id = Guid.NewGuid(),
            Code = InternalCustomerCode.Create("INT-42")
        };

        // Act
        context.Items.Add(entity);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var loaded = await context.Items.FindAsync([entity.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Code.Value.Should().Be("INT-42");
    }

    private class InternalValueObjectDbContext(DbContextOptions<InternalValueObjectDbContext> options)
        : DbContext(options)
    {
        public DbSet<InternalValueEntity> Items => Set<InternalValueEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(InternalCustomerCode).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<InternalValueEntity>(builder => builder.HasKey(entity => entity.Id));
    }

    private async Task ExecuteNonQueryAsync(string commandText, params (string Name, object? Value)[] parameters) =>
        await ExecuteNonQueryAsync(commandText, TestContext.Current.CancellationToken, parameters);

    private async Task ExecuteNonQueryAsync(
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = commandText;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion

    #region RequiredBool property converter

    [Fact]
    public async Task RequiredBoolProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("BoolCustomer"),
            Email = EmailAddress.Create("bool@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 75m,
            Status = TestOrderStatus.Draft,
            GiftWrap = Maybe.From(TestGiftWrap.Create(true))
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.GiftWrap.HasValue.Should().BeTrue();
        loaded.GiftWrap.Value.Value.Should().BeTrue();
    }

    [Fact]
    public async Task RequiredBoolProperty_FalseValue_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("BoolFalseCustomer"),
            Email = EmailAddress.Create("boolfalse@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 80m,
            Status = TestOrderStatus.Draft,
            GiftWrap = Maybe.From(TestGiftWrap.Create(false))
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.GiftWrap.HasValue.Should().BeTrue();
        loaded.GiftWrap.Value.Value.Should().BeFalse();
    }

    #endregion

    #region RequiredDateTime property converter

    [Fact]
    public async Task RequiredDateTimeProperty_RoundTripWorks()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("DateTimeCustomer"),
            Email = EmailAddress.Create("datetime@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var orderDate = TestOrderDate.Create(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 200m,
            Status = TestOrderStatus.Confirmed,
            OrderDate = Maybe.From(orderDate)
        };

        // Act
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.OrderDate.HasValue.Should().BeTrue();
        loaded.OrderDate.Value.Value.Year.Should().Be(2026);
        loaded.OrderDate.Value.Value.Month.Should().Be(1);
        loaded.OrderDate.Value.Value.Day.Should().Be(15);
    }

    #endregion
}

internal sealed class InternalCustomerCode : ScalarValueObject<InternalCustomerCode, string>, IScalarValue<InternalCustomerCode, string>
{
    private InternalCustomerCode(string value) : base(value) { }

    public static Result<InternalCustomerCode> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "code";
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<InternalCustomerCode>(Error.Validation("Code is required.", field));

        return Result.Ok(new InternalCustomerCode(value));
    }
}

internal sealed class InternalValueEntity
{
    public Guid Id { get; set; }
    public InternalCustomerCode Code { get; set; } = null!;
}