namespace Trellis.EntityFrameworkCore.Tests;

using System.Linq.Expressions;
using Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Primitives;

/// <summary>
/// Tests Specification&lt;T&gt; with in-memory, SQLite, and SQL Server (LocalDB) backends.
/// In-memory tests verify specification logic; SQLite and SQL Server tests verify EF Core translation.
/// </summary>
public class SpecificationTests : IAsyncLifetime
{
    private static readonly string SqlServerConnectionString = GetSqlServerConnectionString();

    private static string GetSqlServerConnectionString()
    {
        var envValue = Environment.GetEnvironmentVariable("TRELLIS_TEST_SQLSERVER_CONNECTION");
        return string.IsNullOrWhiteSpace(envValue)
            ? "Server=(localdb)\\MSSQLLocalDB;Database=TrellisSpecTests;Trusted_Connection=True;TrustServerCertificate=True"
            : envValue;
    }

    private SqliteConnection _sqliteConnection = null!;
    private ScalarValueTestDbContext _sqliteContext = null!;
    private ScalarValueTestDbContext? _sqlServerContext;
    private List<TestCustomer> _memoryStore = null!;
    private bool _sqlServerAvailable;

    public async ValueTask InitializeAsync()
    {
        // SQLite setup
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        var sqliteOptions = new DbContextOptionsBuilder<ScalarValueTestDbContext>()
            .UseSqlite(_sqliteConnection)
            .AddTrellisInterceptors()
            .Options;

        _sqliteContext = new ScalarValueTestDbContext(sqliteOptions);
        await _sqliteContext.Database.EnsureCreatedAsync();

        // SQL Server setup (opt-in via environment variable — not available on GitHub Actions)
        if (Environment.GetEnvironmentVariable("TRELLIS_TEST_SQLSERVER") == "true")
        {
            try
            {
                var sqlServerOptions = new DbContextOptionsBuilder<ScalarValueTestDbContext>()
                    .UseSqlServer(SqlServerConnectionString)
                    .AddTrellisInterceptors()
                    .Options;

                _sqlServerContext = new ScalarValueTestDbContext(sqlServerOptions);
                await _sqlServerContext.Database.EnsureDeletedAsync();
                await _sqlServerContext.Database.EnsureCreatedAsync();
                _sqlServerAvailable = true;
            }
            catch
            {
                _sqlServerAvailable = false;
            }
        }

        // Seed data
        var alice = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var bob = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _sqliteContext.Customers.AddRange(alice, bob);
        await _sqliteContext.SaveChangesAsync();
        _sqliteContext.ChangeTracker.Clear();

        if (_sqlServerAvailable)
        {
            // Create fresh instances for SQL Server (entities are tracked by SQLite context)
            var aliceSql = new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create("Alice"),
                Email = EmailAddress.Create("alice-sql@example.com"),
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            var bobSql = new TestCustomer
            {
                Id = TestCustomerId.NewUniqueV4(),
                Name = TestCustomerName.Create("Bob"),
                Email = EmailAddress.Create("bob-sql@example.com"),
                CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            _sqlServerContext!.Customers.AddRange(aliceSql, bobSql);
            await _sqlServerContext.SaveChangesAsync();
            _sqlServerContext.ChangeTracker.Clear();
        }

        _memoryStore = [alice, bob];
    }

    public async ValueTask DisposeAsync()
    {
        await _sqliteContext.DisposeAsync();
        await _sqliteConnection.DisposeAsync();

        if (_sqlServerContext is not null)
        {
            await _sqlServerContext.Database.EnsureDeletedAsync();
            await _sqlServerContext.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Backends => new() { "Memory", "SQLite", "SqlServer" };

    private async Task<List<TestCustomer>> QueryAsync(Specification<TestCustomer> spec, string backend)
    {
        if (backend == "Memory")
            return _memoryStore.Where(spec.IsSatisfiedBy).ToList();

        if (backend == "SqlServer")
        {
            if (!_sqlServerAvailable)
                Assert.Skip("SQL Server (LocalDB) is not available");
            return await _sqlServerContext!.Customers.Where(spec).ToListAsync(TestContext.Current.CancellationToken);
        }

        return await _sqliteContext.Customers.Where(spec).ToListAsync(TestContext.Current.CancellationToken);
    }

    private ScalarValueTestDbContext GetEfContext(string backend)
    {
        if (backend == "SqlServer")
        {
            if (!_sqlServerAvailable)
                Assert.Skip("SQL Server (LocalDB) is not available");
            return _sqlServerContext!;
        }

        return _sqliteContext;
    }

    #region Primitive property specifications (work on both backends)

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task CreatedAfterSpec_filters_correctly(string backend)
    {
        var spec = new CreatedAfterSpec(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Bob");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Composed_And_spec_filters_correctly(string backend)
    {
        var afterSpec = new CreatedAfterSpec(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var beforeSpec = new CreatedBeforeSpec(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var combined = afterSpec.And(beforeSpec);

        var results = await QueryAsync(combined, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Not_spec_inverts_filter(string backend)
    {
        var spec = new CreatedAfterSpec(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)).Not();

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Or_spec_combines_results(string backend)
    {
        var aliceSpec = new CreatedBeforeSpec(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var bobSpec = new CreatedAfterSpec(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var combined = aliceSpec.Or(bobSpec);

        var results = await QueryAsync(combined, backend);

        results.Should().HaveCount(2);
    }

    #endregion

    #region OrderBy and Length (EF Core backends)

    public static TheoryData<string> EfBackends => new() { "SQLite", "SqlServer" };

    #endregion

    #region Natural OrderBy and Length (no .Value)

    [Theory]
    [MemberData(nameof(EfBackends))]
    public async Task OrderBy_VO_without_Value(string backend)
    {
        var ctx = GetEfContext(backend);
        var ct = TestContext.Current.CancellationToken;

        var customers = await ctx.Customers
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        customers.Should().HaveCount(2);
        customers[0].Name.Value.Should().Be("Alice");
        customers[1].Name.Value.Should().Be("Bob");
    }

    [Theory]
    [MemberData(nameof(EfBackends))]
    public async Task OrderByDescending_VO_without_Value(string backend)
    {
        var ctx = GetEfContext(backend);
        var ct = TestContext.Current.CancellationToken;

        var customers = await ctx.Customers
            .OrderByDescending(c => c.Name)
            .ToListAsync(ct);

        customers.Should().HaveCount(2);
        customers[0].Name.Value.Should().Be("Bob");
        customers[1].Name.Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Where_Length_without_Value(string backend)
    {
        var spec = new NameLengthSpec(4);

        var results = await QueryAsync(spec, backend);

        // "Alice" has 5 chars, "Bob" has 3 — neither has exactly 4
        results.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Where_Length_greater_than(string backend)
    {
        var spec = new NameLongerThanSpec(3);

        var results = await QueryAsync(spec, backend);

        // "Alice" has 5 chars > 3
        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    #endregion

    #region Edge cases — .Value on ParameterExpression (Select then filter)

    [Theory]
    [MemberData(nameof(EfBackends))]
    public async Task Select_VO_then_Where_equals(string backend)
    {
        var ctx = GetEfContext(backend);
        var ct = TestContext.Current.CancellationToken;

        var names = await ctx.Customers
            .Select(c => c.Name)
            .Where(n => n == "Alice")
            .ToListAsync(ct);

        names.Should().HaveCount(1);
        names[0].Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(EfBackends))]
    public async Task Select_VO_then_Where_StartsWith(string backend)
    {
        var ctx = GetEfContext(backend);
        var ct = TestContext.Current.CancellationToken;

        var names = await ctx.Customers
            .Select(c => c.Name)
            .Where(n => n.StartsWith("Al"))
            .ToListAsync(ct);

        names.Should().HaveCount(1);
        names[0].Value.Should().Be("Alice");
    }

    #endregion

    #region Test Specifications

    private class CreatedAfterSpec : Specification<TestCustomer>
    {
        private readonly DateTime _date;
        public CreatedAfterSpec(DateTime date) => _date = date;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.CreatedAt > _date;
    }

    private class CreatedBeforeSpec : Specification<TestCustomer>
    {
        private readonly DateTime _date;
        public CreatedBeforeSpec(DateTime date) => _date = date;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.CreatedAt < _date;
    }

    /// <summary>Natural VO comparison: Name == "Alice" without .Value</summary>
    private class NaturalNameEqualsStringSpec : Specification<TestCustomer>
    {
        private readonly string _name;
        public NaturalNameEqualsStringSpec(string name) => _name = name;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name == _name;
    }

    /// <summary>Natural VO comparison: Name == someName (VO vs VO)</summary>
    private class NaturalNameEqualsVoSpec : Specification<TestCustomer>
    {
        private readonly TestCustomerName _name;
        public NaturalNameEqualsVoSpec(TestCustomerName name) => _name = name;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name == _name;
    }

    /// <summary>Natural string method: Name.StartsWith("Al") without .Value</summary>
    private class NaturalNameStartsWithSpec : Specification<TestCustomer>
    {
        private readonly string _prefix;
        public NaturalNameStartsWithSpec(string prefix) => _prefix = prefix;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name.StartsWith(_prefix);
    }

    /// <summary>Natural string method: Name.Contains("lic") without .Value</summary>
    private class NaturalNameContainsSpec : Specification<TestCustomer>
    {
        private readonly string _value;
        public NaturalNameContainsSpec(string value) => _value = value;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name.Contains(_value);
    }

    /// <summary>Natural Length: Name.Length == n without .Value</summary>
    private class NameLengthSpec : Specification<TestCustomer>
    {
        private readonly int _length;
        public NameLengthSpec(int length) => _length = length;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name.Length == _length;
    }

    /// <summary>Natural Length: Name.Length > n without .Value</summary>
    private class NameLongerThanSpec : Specification<TestCustomer>
    {
        private readonly int _minLength;
        public NameLongerThanSpec(int minLength) => _minLength = minLength;

        public override Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.Name.Length > _minLength;
    }

    #endregion

    #region Natural VO comparisons (no .Value) — all backends

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Natural_VO_equals_string(string backend)
    {
        var spec = new NaturalNameEqualsStringSpec("Alice");

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Natural_VO_equals_VO(string backend)
    {
        var spec = new NaturalNameEqualsVoSpec(TestCustomerName.Create("Bob"));

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Bob");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Natural_StartsWith_without_Value(string backend)
    {
        var spec = new NaturalNameStartsWithSpec("Al");

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Natural_Contains_without_Value(string backend)
    {
        var spec = new NaturalNameContainsSpec("ob");

        var results = await QueryAsync(spec, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Bob");
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Natural_VO_comparison_composed_with_primitive(string backend)
    {
        var nameSpec = new NaturalNameStartsWithSpec("Al");
        var dateSpec = new CreatedAfterSpec(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var combined = nameSpec.And(dateSpec);

        var results = await QueryAsync(combined, backend);

        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("Alice");
    }

    #endregion
}

/// <summary>
/// DbContext with Trellis interceptors registered for specification translation testing.
/// </summary>
internal class ScalarValueTestDbContext(DbContextOptions<ScalarValueTestDbContext> options) : DbContext(options)
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