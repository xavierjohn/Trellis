using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

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
        loaded!.Phone.Should().HaveValue().Which.Value.Should().Be(phone.Value);
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
        loaded!.SubmittedAt.Should().HaveValue().Which.Should().Be(submittedAt);
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
        loaded!.OptionalStatus.Should().HaveValue().Which.Should().Be(TestOrderStatus.Shipped);
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
        tracked!.Phone = Maybe<PhoneNumber>.None;
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
        loaded.Customer.Phone.Should().HaveValue().Which.Value.Should().Be(phone.Value);
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
        confirmed.OptionalStatus.Should().HaveValue().Which.Should().Be(TestOrderStatus.Shipped);

        var draft = loaded.Orders.Single(order => order.Status == TestOrderStatus.Draft);
        draft.OptionalStatus.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region HasValueWhere — rewriter + SQL Server query translation

    [Fact]
    public async Task HasValueWhere_ValueType_GeneratesNullGuard_AndFiltersRealServer()
    {
        // End-to-end smoke against real SQL Server: HasValueWhere(predicate) must
        // (1) translate through MaybeQueryInterceptor, (2) emit `[_submittedAt] IS NOT NULL
        // AND [_submittedAt] < @cutoff`, (3) execute against SQL Server, (4) return only
        // rows that satisfy both clauses. The SQL-shape assertion is what makes this a
        // smoke test rather than a result-set assertion — the SQL itself proves the
        // rewriter's contract held all the way down to the wire.
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlHelen"),
            Email = EmailAddress.Create("sqlhelen@example.com"),
            CreatedAt = DateTime.UtcNow,
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 50m,
            Status = TestOrderStatus.Confirmed,
            SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
        };
        var late = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 75m,
            Status = TestOrderStatus.Confirmed,
            SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)),
        };
        var noDate = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customer.Id,
            Amount = 30m,
            Status = TestOrderStatus.Confirmed,
        };
        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var queryable = _context.Orders
            .Where(o => o.SubmittedAt.HasValueWhere(t => t < cutoff));
        var generatedSql = queryable.ToQueryString();

        var results = await queryable.ToListAsync(ct);

        results.Should().ContainSingle().Which.Id.Should().Be(early.Id);

        generatedSql.Should().MatchRegex(@"\[?SubmittedAt\]?\s+IS\s+NOT\s+NULL",
            "the MaybeExpressionRewriter must wrap HasValueWhere(predicate) with `_storage IS NOT NULL AND …`");
        generatedSql.Should().NotContain("HasValueWhere",
            "the method call itself must be translated away — leaving HasValueWhere in the SQL means the rewriter never ran");
    }

    [Fact]
    public async Task HasValueWhere_ReferenceType_GeneratesNullGuard_AndFiltersRealServer()
    {
        // Parallel smoke against the reference-type branch of the rewriter. The storage
        // type for `Maybe<PhoneNumber>` is a nullable scalar-value-object reference; the
        // rewriter's substitution path differs from the Nullable<T>.Value branch used
        // for value-type T. Pinning the reference-type path against real SQL Server is
        // the strongest signal that both branches generate executable SQL.
        var ct = TestContext.Current.CancellationToken;
        var targetPhone = PhoneNumber.Create("+1-555-0100");
        var match = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlMatch"),
            Email = EmailAddress.Create("sqlmatch@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(targetPhone),
        };
        var otherPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlOther"),
            Email = EmailAddress.Create("sqlother@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-555-9999")),
        };
        var noPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("SqlNoPhone"),
            Email = EmailAddress.Create("sqlnophone@example.com"),
            CreatedAt = DateTime.UtcNow,
        };
        _context.Customers.AddRange(match, otherPhone, noPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var queryable = _context.Customers
            .Where(c => c.Phone.HasValueWhere(p => p == targetPhone));
        var generatedSql = queryable.ToQueryString();

        var results = await queryable.ToListAsync(ct);

        results.Should().ContainSingle().Which.Id.Should().Be(match.Id);

        generatedSql.Should().MatchRegex(@"\[?Phone\]?\s+IS\s+NOT\s+NULL",
            "the MaybeExpressionRewriter must wrap HasValueWhere(predicate) with `_storage IS NOT NULL AND …`");
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
                .UseSqlServer(connectionString).IgnoreManyServiceProvidersCreatedWarning()
                .AddTrellisInterceptors()
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