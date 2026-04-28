namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Integration tests for the source-generated <c>ApplyTrellisConventionsFor&lt;TContext&gt;()</c>
/// extension produced by <c>ApplyTrellisConventionsForGenerator</c>.
/// </summary>
/// <remarks>
/// These tests intentionally use a separate <see cref="GeneratedConventionsTestDbContext"/> so the
/// existing reflection-path round-trip suite in <see cref="ApplyTrellisConventionsTests"/> remains
/// untouched. Both paths must produce equivalent EF Core models.
/// </remarks>
public class ApplyTrellisConventionsForTests : IDisposable
{
    private readonly GeneratedConventionsTestDbContext _context;
    private readonly SqliteConnection _connection;

    public ApplyTrellisConventionsForTests() =>
        (_context, _connection) = GeneratedConventionsTestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ScalarRequiredGuid_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = DateTime.UtcNow,
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(customer.Id);
        loaded.Name.Value.Should().Be("Alice");
    }

    [Fact]
    public async Task BuiltInEmailAddress_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = DateTime.UtcNow,
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);
        loaded.Should().NotBeNull();
        loaded!.Email.Value.Should().Be("bob@example.com");
    }

    [Fact]
    public async Task RequiredEnum_StoredAsString_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();
        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("Eve"),
            Email = EmailAddress.Create("eve@example.com"),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = customerId,
            Amount = 42m,
            Status = TestOrderStatus.Confirmed,
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Orders.FindAsync([order.Id], ct);
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(TestOrderStatus.Confirmed);
        loaded.Status.Value.Should().Be("Confirmed");
    }

    [Fact]
    public async Task MaybeWrappedScalar_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Carol"),
            Email = EmailAddress.Create("carol@example.com"),
            CreatedAt = DateTime.UtcNow,
            Phone = Maybe.From(PhoneNumber.Create("+1-415-555-0100")),
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Customers.FindAsync([customer.Id], ct);
        loaded.Should().NotBeNull();
        loaded!.Phone.HasValue.Should().BeTrue();
        loaded.Phone.Value.Value.Should().Be("+14155550100");
    }

    [Fact]
    public void ScalarConverters_AreRegistered_OnGeneratedPath()
    {
        var statusProperty = _context.Model.FindEntityType(typeof(TestOrder))!
            .FindProperty(nameof(TestOrder.Status))!;
        statusProperty.GetTypeMapping().Converter
            .Should().BeOfType<TrellisScalarConverter<TestOrderStatus, string>>();

        var idProperty = _context.Model.FindEntityType(typeof(TestCustomer))!
            .FindProperty(nameof(TestCustomer.Id))!;
        idProperty.GetTypeMapping().Converter
            .Should().BeOfType<TrellisScalarConverter<TestCustomerId, Guid>>();
    }

    [Fact]
    public void UnknownDbContext_Throws_InvalidOperationException()
    {
        // The generator skips private nested types, so UnregisteredDbContext is excluded
        // from the dispatch table; calling ApplyTrellisConventionsFor for it must throw.
        // We trigger it indirectly through a context whose ConfigureConventions calls the
        // wrong type-argument.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var act = () =>
        {
            using var ctx = new MisconfiguredDbContext(connection);
            ctx.Database.EnsureCreated();
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UnregisteredDbContext*");
    }

    /// <summary>
    /// Private nested DbContext deliberately excluded from generation by accessibility.
    /// Used only as a TContext type-argument in the negative test.
    /// </summary>
    private class UnregisteredDbContext : DbContext
    {
    }

    /// <summary>
    /// DbContext whose <c>ConfigureConventions</c> intentionally requests a TContext that
    /// has no generated dispatch entry, exercising the throw branch.
    /// </summary>
    private class MisconfiguredDbContext : DbContext
    {
        public MisconfiguredDbContext(SqliteConnection connection)
            : base(new DbContextOptionsBuilder<MisconfiguredDbContext>().UseSqlite(connection).Options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventionsFor<UnregisteredDbContext>();
    }
}

/// <summary>
/// Internal DbContext used to exercise the source-generated <c>ApplyTrellisConventionsFor</c>.
/// Mirrors <c>TestDbContext</c>'s schema.
/// </summary>
internal class GeneratedConventionsTestDbContext : DbContext
{
    public DbSet<TestCustomer> Customers => Set<TestCustomer>();
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    public GeneratedConventionsTestDbContext(SqliteConnection connection)
        : base(new DbContextOptionsBuilder<GeneratedConventionsTestDbContext>()
            .UseSqlite(connection)
            .Options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<GeneratedConventionsTestDbContext>();

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

    public static (GeneratedConventionsTestDbContext Context, SqliteConnection Connection) CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var context = new GeneratedConventionsTestDbContext(connection);
        context.Database.EnsureCreated();
        return (context, connection);
    }
}