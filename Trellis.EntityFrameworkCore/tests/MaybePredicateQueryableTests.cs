namespace Trellis.EntityFrameworkCore.Tests;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public class MaybePredicateQueryableTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PredicateDbContext _context;

    public MaybePredicateQueryableTests()
    {
        _connection.Open();
        _context = CreateContext();
        _context.Database.EnsureCreated();
        _context.Rows.AddRange(
            new MaybePredicateRow { Id = 1, Number = 10, Text = "alpha", Flag = true, Scalar = TestTicketNumber.Create(10) },
            new MaybePredicateRow { Id = 2, Number = 20, Text = "bravo", Flag = false, Scalar = TestTicketNumber.Create(20) },
            new MaybePredicateRow { Id = 3, Number = 30, Text = "charlie", Flag = true, Scalar = TestTicketNumber.Create(30) },
            new MaybePredicateRow { Id = 4 });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("<", false, new[] { 1 })]
    [InlineData("<=", false, new[] { 1, 2 })]
    [InlineData(">", false, new[] { 3 })]
    [InlineData(">=", false, new[] { 2, 3 })]
    [InlineData("<", true, new[] { 1 })]
    [InlineData("<=", true, new[] { 1, 2 })]
    [InlineData(">", true, new[] { 3 })]
    [InlineData(">=", true, new[] { 2, 3 })]
    public async Task WhereHasValue_NumericPredicate_TranslatesAndExcludesNull(
        string comparison, bool withInterceptors, int[] expected)
    {
        using var context = CreateContext(withInterceptors);
        Expression<Func<int, bool>> predicate = comparison switch
        {
            "<" => value => value < 20,
            "<=" => value => value <= 20,
            ">" => value => value > 20,
            ">=" => value => value >= 20,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
        var query = context.Rows.WhereHasValue(row => row.Number, predicate);

        query.ToQueryString().Should().Contain("WHERE");
        var ids = await query.OrderBy(row => row.Id).Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal(expected);
    }

    [Fact]
    [SuppressMessage("Globalization", "CA1309:Use ordinal string comparison",
        Justification = "SQL string comparison intentionally uses the database collation.")]
    public async Task WhereHasValue_StringComparison_UsesExplicitProviderTranslatedComparison()
    {
        var boundary = "bravo";
        var query = _context.Rows.WhereHasValue(row => row.Text, text => string.Compare(text, boundary) < 0);

        query.ToQueryString().Should().Contain("WHERE");
        var ids = await query.Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1]);
    }

    [Fact]
    public async Task WhereHasValue_StringInequality_ExcludesNullDespiteNullCompensation()
    {
        var ids = await _context.Rows.WhereHasValue(row => row.Text, text => text != "bravo")
            .OrderBy(row => row.Id).Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1, 3]);
    }

    [Fact]
    public async Task WhereHasValue_ConstantTrue_ExcludesNullForValueAndReferenceTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var numbers = await _context.Rows.WhereHasValue(row => row.Number, value => true)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(ct);
        var texts = await _context.Rows.WhereHasValue(row => row.Text, value => true)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(ct);

        numbers.Should().Equal([1, 2, 3]);
        texts.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task WhereHasValue_ConstantFalse_ReturnsNoRows()
    {
        var count = await _context.Rows.WhereHasValue(row => row.Number, value => false)
            .CountAsync(TestContext.Current.CancellationToken);

        count.Should().Be(0);
    }

    [Fact]
    public async Task WhereHasValue_BooleanPredicate_DoesNotRequireComparisonOperators()
    {
        var ids = await _context.Rows.WhereHasValue(row => row.Flag, flag => !flag)
            .Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([2]);
    }

    [Fact]
    public async Task WhereHasValue_CompoundPredicate_PreservesGroupingAndNullExclusion()
    {
        var ids = await _context.Rows
            .WhereHasValue(row => row.Number, value => value < 15 || value > 25)
            .OrderBy(row => row.Id).Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1, 3]);
    }

    [Fact]
    public async Task WhereHasValue_ReusableExpression_PreservesCapturedParameterAcrossExecutions()
    {
        var cutoff = 20;
        Expression<Func<int, bool>> predicate = value => value < cutoff;
        var query = _context.Rows.WhereHasValue(row => row.Number, predicate)
            .OrderBy(row => row.Id).Select(row => row.Id);

        var first = await query.ToListAsync(TestContext.Current.CancellationToken);
        cutoff = 30;
        var second = await query.ToListAsync(TestContext.Current.CancellationToken);

        first.Should().Equal([1]);
        second.Should().Equal([1, 2]);
    }

    [Fact]
    public async Task WhereHasValue_NestedLambda_PreservesItsOwnParameter()
    {
        int[] bounds = [15, 25];
        var ids = await _context.Rows
            .WhereHasValue(row => row.Number, value => bounds.Any(bound => bound > value))
            .OrderBy(row => row.Id).Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([1, 2]);
    }

    [Fact]
    public async Task WhereHasValue_ScalarPredicate_WithInterceptorsTranslatesPrimitiveAccess()
    {
        using var context = CreateContext(withInterceptors: true);
        var query = context.Rows.WhereHasValue(row => row.Scalar, scalar => scalar.Value >= 20);

        query.ToQueryString().Should().Contain("WHERE");
        var ids = await query.OrderBy(row => row.Id).Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal([2, 3]);
    }

    [Fact]
    public void WhereHasValue_UntranslatableMethod_ThrowsInsteadOfFilteringOnClient()
    {
        var query = _context.Rows.WhereHasValue(row => row.Number, value => OnlyInMemory(value));
        var act = () => query.ToQueryString();

        act.Should().Throw<InvalidOperationException>().WithMessage("*could not be translated*");
    }

    [Fact]
    public void WhereHasValue_DateTimeOffsetComparison_SqliteRejectsUnsupportedTranslation()
    {
        var cutoff = DateTimeOffset.UtcNow;
        var query = _context.Rows.WhereHasValue(row => row.Timestamp, value => value < cutoff);
        var act = () => query.ToQueryString();

        act.Should().Throw<InvalidOperationException>().WithMessage("*could not be translated*");
    }

    [Fact]
    public void WhereHasValue_DateTimeOffsetAndGuidComparisons_SqlServerTranslatesWithoutConnecting()
    {
        using var context = new PredicateDbContext(new DbContextOptionsBuilder<PredicateDbContext>()
            .UseSqlServer("Server=unused;Database=TranslationOnly;Integrated Security=true")
            .Options);
        var cutoff = DateTimeOffset.UtcNow;
        var key = Guid.NewGuid();

        var dates = context.Rows.WhereHasValue(row => row.Timestamp, value => value <= cutoff).ToQueryString();
        var keys = context.Rows.WhereHasValue(row => row.Key, value => value.CompareTo(key) > 0).ToQueryString();

        dates.Should().Contain("WHERE").And.Contain("[Timestamp]").And.Contain("<=");
        keys.Should().Contain("WHERE").And.Contain("[Key]").And.Contain(">");
    }

    [Fact]
    public void WhereHasValue_PredicateOverload_NullSourceThrows()
    {
        IQueryable<MaybePredicateRow> source = null!;
        var act = () => source.WhereHasValue(row => row.Number, value => value > 0);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void WhereHasValue_PredicateOverload_NullSelectorThrows()
    {
        Expression<Func<MaybePredicateRow, Maybe<int>>> selector = null!;
        var act = () => _context.Rows.WhereHasValue(selector, value => value > 0);

        act.Should().Throw<ArgumentNullException>().WithParameterName("propertySelector");
    }

    [Fact]
    public void WhereHasValue_PredicateOverload_NullPredicateThrows()
    {
        Expression<Func<int, bool>> predicate = null!;
        var act = () => _context.Rows.WhereHasValue(row => row.Number, predicate);

        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void WhereHasValue_PredicateOverload_IndirectSelectorThrows()
    {
        var act = () => _context.Rows.WhereHasValue(row => Maybe.From(row.Id), value => value > 0);

        act.Should().Throw<ArgumentException>().WithParameterName("propertySelector");
    }

    [Fact]
    public void MaybeQueryableExtensions_PublicSurface_RemovesMisleadingComparisonHelpers()
    {
        var names = typeof(MaybeQueryableExtensions).GetMethods().Select(method => method.Name);

        names.Should().NotContain(["WhereLessThan", "WhereLessThanOrEqual", "WhereGreaterThan", "WhereGreaterThanOrEqual"]);
    }

    private static bool OnlyInMemory(int value) => value > 10;

    private PredicateDbContext CreateContext(bool withInterceptors = false)
    {
        var builder = new DbContextOptionsBuilder<PredicateDbContext>()
            .UseSqlite(_connection)
            .IgnoreManyServiceProvidersCreatedWarning();
        if (withInterceptors)
            builder.AddTrellisInterceptors();
        return new PredicateDbContext(builder.Options);
    }

    private sealed class PredicateDbContext(DbContextOptions<PredicateDbContext> options) : DbContext(options)
    {
        public DbSet<MaybePredicateRow> Rows => Set<MaybePredicateRow>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestTicketNumber).Assembly);
    }
}

public sealed partial class MaybePredicateRow
{
    public int Id { get; set; }
    public partial Maybe<int> Number { get; set; }
    public partial Maybe<string> Text { get; set; }
    public partial Maybe<bool> Flag { get; set; }
    public partial Maybe<DateTimeOffset> Timestamp { get; set; }
    public partial Maybe<Guid> Key { get; set; }
    public partial Maybe<TestTicketNumber> Scalar { get; set; }
}
