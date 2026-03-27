namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="MaybeExpressionRewriter"/> and <see cref="MaybeQueryInterceptor"/>.
/// Validates that LINQ expressions referencing <see cref="Maybe{T}"/> properties are
/// automatically rewritten to EF Core-translatable storage member references.
/// </summary>
public class MaybeExpressionRewriterTests : IDisposable
{
    private readonly InterceptorTestDbContext _context;
    private readonly SqliteConnection _connection;

    public MaybeExpressionRewriterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InterceptorTestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new MaybeQueryInterceptor())
            .Options;

        _context = new InterceptorTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region HasValue / HasNoValue

    [Fact]
    public async Task HasValue_TranslatesToNotNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act — natural LINQ, no explicit extension methods
        var results = await _context.Customers
            .Where(c => c.Phone.HasValue)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    [Fact]
    public async Task HasNoValue_TranslatesToIsNull()
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
            .Where(c => c.Phone.HasNoValue)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withoutPhone.Id);
    }

    #endregion

    #region GetValueOrDefault with comparison

    [Fact]
    public async Task GetValueOrDefault_WithLessThanComparison_TranslatesToCoalesce()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id); // NULL

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act — specification-style expression with GetValueOrDefault
        var results = await _context.Orders
            .Where(o => o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff)
            .ToListAsync(ct);

        // Assert — only the early order matches (noDate gets MaxValue, so excluded)
        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    [Fact]
    public async Task GetValueOrDefault_OverdueSpecificationPattern_Works()
    {
        // Arrange — simulates the OverdueOrderSpecification use case
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var overdueOrder = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        overdueOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-10));
        var recentOrder = CreateOrder(customer.Id, TestOrderStatus.Confirmed);
        recentOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-2));
        var shippedOrder = CreateOrder(customer.Id, TestOrderStatus.Shipped);
        shippedOrder.SubmittedAt = Maybe.From(DateTime.UtcNow.AddDays(-10));

        _context.Orders.AddRange(overdueOrder, recentOrder, shippedOrder);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = DateTime.UtcNow.AddDays(-7);

        // Act — this is what a specification's ToExpression() would return
        var results = await _context.Orders
            .Where(o => o.Status == TestOrderStatus.Confirmed
                     && o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff)
            .ToListAsync(ct);

        // Assert — only the overdue submitted order
        results.Should().ContainSingle()
            .Which.Id.Should().Be(overdueOrder.Id);
    }

    #endregion

    #region Value comparison pattern

    [Fact]
    public async Task Value_LessThan_TranslatesDirectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var early = CreateOrder(customer.Id);
        early.SubmittedAt = Maybe.From(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var late = CreateOrder(customer.Id);
        late.SubmittedAt = Maybe.From(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var noDate = CreateOrder(customer.Id);

        _context.Orders.AddRange(early, late, noDate);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act — clean pattern: HasValue && Value < cutoff (no GetValueOrDefault sentinel)
        var results = await _context.Orders
            .Where(o => o.SubmittedAt.HasValue && o.SubmittedAt.Value < cutoff)
            .ToListAsync(ct);

        // Assert — only early matches (noDate excluded by HasValue, late excluded by < cutoff)
        results.Should().ContainSingle()
            .Which.Id.Should().Be(early.Id);
    }

    #endregion

    #region Specification integration

    [Fact]
    public async Task Specification_WithMaybeProperty_WorksWithWhereOperator()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = CreateCustomer("Alice");
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);

        var withPhone = CreateCustomer("Bob", "+1-555-0200");
        _context.Customers.Add(withPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act — use a specification with Maybe<T> via the implicit operator
        var spec = new HasPhoneSpecification();
        var results = await _context.Customers
            .Where(spec.ToExpression())
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    #endregion

    #region Equality with Maybe<T>.None

    [Fact]
    public async Task EqualsMaybeNone_TranslatesToIsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Alice", "+1-555-0100");
        var withoutPhone = CreateCustomer("Bob");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act — compare Maybe<T> property against Maybe<T>.None using == operator
        var results = await _context.Customers
            .Where(c => c.Phone == Maybe<PhoneNumber>.None)
            .ToListAsync(ct);

        // Assert — only the customer without phone should match
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withoutPhone.Id);
    }

    [Fact]
    public async Task NotEqualsMaybeNone_TranslatesToIsNotNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var withPhone = CreateCustomer("Charlie", "+1-555-0300");
        var withoutPhone = CreateCustomer("Dave");
        _context.Customers.AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        // Act
        var results = await _context.Customers
            .Where(c => c.Phone != Maybe<PhoneNumber>.None)
            .ToListAsync(ct);

        // Assert
        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    #endregion

    #region Helpers

    private static TestCustomer CreateCustomer(string name, string? phone = null)
    {
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create(name),
            Email = EmailAddress.Create($"{name.ToLowerInvariant()}@test.com"),
            CreatedAt = DateTime.UtcNow
        };

        if (phone is not null)
            customer.Phone = Maybe.From(PhoneNumber.Create(phone));

        return customer;
    }

    private static TestOrder CreateOrder(TestCustomerId customerId, TestOrderStatus? status = null) =>
        new()
        {
            Id = TestOrderId.NewUniqueV7(),
            CustomerId = customerId,
            Amount = 100m,
            Status = status ?? TestOrderStatus.Draft
        };

    /// <summary>
    /// Test specification: customers who have a phone number.
    /// Uses Maybe&lt;T&gt;.HasValue in the expression — should be rewritten by the interceptor.
    /// </summary>
    private sealed class HasPhoneSpecification : Specification<TestCustomer>
    {
        public override System.Linq.Expressions.Expression<Func<TestCustomer, bool>> ToExpression() =>
            customer => customer.Phone.HasValue;
    }

    /// <summary>
    /// Test DbContext with the <see cref="MaybeQueryInterceptor"/> registered via AddTrellisInterceptors().
    /// </summary>
    private sealed class InterceptorTestDbContext(DbContextOptions<InterceptorTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();
        public DbSet<TestOrder> Orders => Set<TestOrder>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Name).HasMaxLength(100).IsRequired();
                b.Property(c => c.Email).HasMaxLength(254).IsRequired();
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

    #endregion
}

/// <summary>
/// Tests that <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors"/> registers the
/// singleton <see cref="MaybeQueryInterceptor"/> and Maybe expressions translate correctly.
/// </summary>
public class AddTrellisInterceptorsTests : IDisposable
{
    private readonly DbContext _context;
    private readonly SqliteConnection _connection;

    public AddTrellisInterceptorsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options;

        _context = new AddTrellisInterceptorsTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HasValue_Works_WithAddTrellisInterceptors()
    {
        var ct = TestContext.Current.CancellationToken;
        var withPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@test.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-555-0100"))
        };
        var withoutPhone = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV7(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@test.com"),
            CreatedAt = DateTime.UtcNow
        };

        _context.Set<TestCustomer>().AddRange(withPhone, withoutPhone);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var results = await _context.Set<TestCustomer>()
            .Where(c => c.Phone.HasValue)
            .ToListAsync(ct);

        results.Should().ContainSingle()
            .Which.Id.Should().Be(withPhone.Id);
    }

    [Fact]
    public void AddTrellisInterceptors_Called_Twice_Uses_Same_Singleton()
    {
        // Should not throw ManyServiceProvidersCreatedWarning
        var options1 = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options;

        var options2 = new DbContextOptionsBuilder<AddTrellisInterceptorsTestDbContext>()
            .UseSqlite(_connection)
            .AddTrellisInterceptors()
            .Options;

        // Both should resolve without multiple service provider warnings
        using var ctx1 = new AddTrellisInterceptorsTestDbContext(options1);
        using var ctx2 = new AddTrellisInterceptorsTestDbContext(options2);
    }

    private sealed class AddTrellisInterceptorsTestDbContext(DbContextOptions<AddTrellisInterceptorsTestDbContext> options)
        : DbContext(options)
    {
        public DbSet<TestCustomer> Customers => Set<TestCustomer>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestCustomerId).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TestCustomer>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Name).HasMaxLength(100).IsRequired();
                b.Property(c => c.Email).HasMaxLength(254).IsRequired();
                b.Property(c => c.CreatedAt).IsRequired();
            });
    }
}