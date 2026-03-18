namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;
using Trellis.Testing;

/// <summary>
/// SQL Server integration tests for <c>MaybeConvention</c> and source-generated
/// <c>partial Maybe&lt;T&gt;</c> properties. Excluded from default test runs —
/// use <c>dotnet test --filter "Category=Integration"</c> to run.
/// </summary>
[Trait("Category", "Integration")]
public class SqlServerMaybeIntegrationTests : IAsyncLifetime
{
    // Set this to a valid SQL Server connection string to run these tests.
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=TrellisEfCoreTests;Trusted_Connection=True;TrustServerCertificate=True";

    private SqlServerTestDbContext _context = null!;

    public async ValueTask InitializeAsync()
    {
        _context = new SqlServerTestDbContext(ConnectionString);
        await _context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await _context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    #region Maybe<T> with reference type (PhoneNumber)

    [Fact]
    public async Task MaybeScalar_RoundTrip_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var phone = PhoneNumber.Create("+1-555-0100");
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlAlice"),
            Email = EmailAddress.Create("sqlalice@example.com"),
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
    public async Task MaybeScalar_None_RoundTrip_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlBob"),
            Email = EmailAddress.Create("sqlbob@example.com"),
            CreatedAt = DateTime.UtcNow
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

    #endregion

    #region Maybe<T> with value type (DateTime)

    [Fact]
    public async Task MaybeValueType_RoundTrip_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("SqlCharlie"),
            Email = EmailAddress.Create("sqlcharlie@example.com"),
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

    #endregion

    #region Maybe<T> with enum (TestOrderStatus)

    [Fact]
    public async Task MaybeEnum_RoundTrip_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("SqlDave"),
            Email = EmailAddress.Create("sqldave@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 150m,
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

    #endregion

    #region Set then clear Maybe<T>

    [Fact]
    public async Task MaybeScalar_SetThenClear_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlEve"),
            Email = EmailAddress.Create("sqleve@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+44-20-7946-0958"))
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

    #region Navigation property with Include

    [Fact]
    public async Task MaybeScalar_NavigationInclude_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var phone = PhoneNumber.Create("+1-555-0300");
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlFrank"),
            Email = EmailAddress.Create("sqlfrank@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(phone)
        };
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 75.00m,
            Status = TestOrderStatus.Confirmed
        };

        _context.Customers.Add(customer);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var loaded = await _context.Orders
            .Include(o => o.Customer)
            .FirstAsync(o => o.Id == order.Id, ct);

        // Assert
        loaded.Customer.Should().NotBeNull();
        loaded.Customer.Name.Value.Should().Be("SqlFrank");
        loaded.Customer.Phone.HasValue.Should().BeTrue();
        loaded.Customer.Phone.Value.Value.Should().Be(phone.Value);
    }

    [Fact]
    public async Task RequiredEnumAndMaybeEnum_CollectionInclude_SqlServer()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlGrace"),
            Email = EmailAddress.Create("sqlgrace@example.com"),
            CreatedAt = DateTime.UtcNow
        };
        var shippedOrder = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 110m,
            Status = TestOrderStatus.Confirmed,
            OptionalStatus = Maybe.From(TestOrderStatus.Shipped)
        };
        var draftOrder = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 65m,
            Status = TestOrderStatus.Draft
        };

        _context.Customers.Add(customer);
        _context.Orders.AddRange(shippedOrder, draftOrder);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var loaded = await _context.Customers
            .Include(c => c.Orders)
            .SingleAsync(c => c.Id == customer.Id, ct);

        // Assert
        loaded.Orders.Should().HaveCount(2);

        var confirmed = loaded.Orders.Single(order => order.Status == TestOrderStatus.Confirmed);
        confirmed.OptionalStatus.HasValue.Should().BeTrue();
        confirmed.OptionalStatus.Value.Should().Be(TestOrderStatus.Shipped);

        var draft = loaded.Orders.Single(order => order.Status == TestOrderStatus.Draft);
        draft.OptionalStatus.HasNoValue.Should().BeTrue();
    }

    #endregion

    /// <summary>
    /// SQL Server DbContext reusing the same entity types and conventions as the SQLite tests.
    /// </summary>
    private sealed class SqlServerTestDbContext : DbContext
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();
        public DbSet<TestOrder> Orders => Set<TestOrder>();

        public SqlServerTestDbContext(string connectionString)
            : base(new DbContextOptionsBuilder<SqlServerTestDbContext>()
                .UseSqlServer(connectionString)
                .Options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Name).HasMaxLength(100).IsRequired();
                b.Property(c => c.Email).HasMaxLength(254).IsRequired();
                b.HasIndex(c => c.Email).IsUnique();
                b.Property(c => c.CreatedAt).IsRequired();
            });

            modelBuilder.Entity<TestOrder>(b =>
            {
                b.HasKey(o => o.Id);
                b.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
                b.Property(o => o.Amount).IsRequired();
                b.Property(o => o.Status).IsRequired();
            });
        }
    }
}