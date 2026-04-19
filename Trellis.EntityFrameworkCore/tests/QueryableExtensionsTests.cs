using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="QueryableExtensions"/> methods:
/// FirstOrDefaultMaybeAsync, SingleOrDefaultMaybeAsync, FirstOrDefaultResultAsync,
/// and Where with Specification.
/// </summary>
public class QueryableExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public QueryableExtensionsTests()
    {
        (_context, _connection) = TestDbContext.CreateInMemory();
        SeedData();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedData()
    {
        var customer1 = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var customer2 = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _context.Customers.AddRange(customer1, customer2);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    #region FirstOrDefaultMaybeAsync — entity exists

    [Fact]
    public async Task FirstOrDefaultMaybeAsync_EntityExists_ReturnsMaybeWithValue()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .FirstOrDefaultMaybeAsync(ct);

        // Assert
        maybe.HasValue.Should().BeTrue();
    }

    #endregion

    #region FirstOrDefaultMaybeAsync — entity does not exist

    [Fact]
    public async Task FirstOrDefaultMaybeAsync_NoEntity_ReturnsMaybeNone()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .Where(c => c.Name == TestCustomerName.Create("NonExistent"))
            .FirstOrDefaultMaybeAsync(ct);

        // Assert
        maybe.HasValue.Should().BeFalse();
    }

    #endregion

    #region FirstOrDefaultMaybeAsync with predicate — match

    [Fact]
    public async Task FirstOrDefaultMaybeAsync_WithPredicate_Match_ReturnsMaybeWithValue()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .FirstOrDefaultMaybeAsync(c => c.Email == EmailAddress.Create("alice@example.com"), ct);

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Name.Value.Should().Be("Alice");
    }

    #endregion

    #region FirstOrDefaultMaybeAsync with predicate — no match

    [Fact]
    public async Task FirstOrDefaultMaybeAsync_WithPredicate_NoMatch_ReturnsMaybeNone()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .FirstOrDefaultMaybeAsync(c => c.Email == EmailAddress.Create("nobody@example.com"), ct);

        // Assert
        maybe.HasValue.Should().BeFalse();
    }

    #endregion

    #region SingleOrDefaultMaybeAsync — single match

    [Fact]
    public async Task SingleOrDefaultMaybeAsync_SingleMatch_ReturnsMaybeWithValue()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .Where(c => c.Email == EmailAddress.Create("alice@example.com"))
            .SingleOrDefaultMaybeAsync(ct);

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Name.Value.Should().Be("Alice");
    }

    #endregion

    #region SingleOrDefaultMaybeAsync — no match

    [Fact]
    public async Task SingleOrDefaultMaybeAsync_NoMatch_ReturnsMaybeNone()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .Where(c => c.Email == EmailAddress.Create("nobody@example.com"))
            .SingleOrDefaultMaybeAsync(ct);

        // Assert
        maybe.HasValue.Should().BeFalse();
    }

    #endregion

    #region SingleOrDefaultMaybeAsync — multiple matches throws

    [Fact]
    public async Task SingleOrDefaultMaybeAsync_MultipleMatches_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _context.Customers.SingleOrDefaultMaybeAsync(ct));
    }

    #endregion

    #region SingleOrDefaultMaybeAsync with predicate

    [Fact]
    public async Task SingleOrDefaultMaybeAsync_WithPredicate_Match_ReturnsMaybeWithValue()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .SingleOrDefaultMaybeAsync(c => c.Email == EmailAddress.Create("bob@example.com"), ct);

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Name.Value.Should().Be("Bob");
    }

    [Fact]
    public async Task SingleOrDefaultMaybeAsync_WithPredicate_NoMatch_ReturnsMaybeNone()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var maybe = await _context.Customers
            .SingleOrDefaultMaybeAsync(c => c.Email == EmailAddress.Create("nobody@example.com"), ct);

        // Assert
        maybe.HasValue.Should().BeFalse();
    }

    #endregion

    #region FirstOrDefaultResultAsync — entity exists

    [Fact]
    public async Task FirstOrDefaultResultAsync_EntityExists_ReturnsSuccess()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var result = await _context.Customers
            .FirstOrDefaultResultAsync(Error.NotFound("Not found"), ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region FirstOrDefaultResultAsync — entity does not exist

    [Fact]
    public async Task FirstOrDefaultResultAsync_NoEntity_ReturnsProvidedNotFoundError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var notFoundError = Error.NotFound("Customer not found");

        // Act
        var result = await _context.Customers
            .Where(c => c.Email == EmailAddress.Create("nobody@example.com"))
            .FirstOrDefaultResultAsync(notFoundError, ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.UnwrapError().Should().BeOfType<NotFoundError>();
        result.UnwrapError().Detail.Should().Be("Customer not found");
    }

    #endregion

    #region FirstOrDefaultResultAsync with predicate — match

    [Fact]
    public async Task FirstOrDefaultResultAsync_WithPredicate_Match_ReturnsSuccess()
    {
        // Act
        var ct = TestContext.Current.CancellationToken;
        var result = await _context.Customers
            .FirstOrDefaultResultAsync(
                c => c.Email == EmailAddress.Create("alice@example.com"),
                Error.NotFound("Not found"),
                ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Name.Value.Should().Be("Alice");
    }

    #endregion

    #region FirstOrDefaultResultAsync with predicate — no match

    [Fact]
    public async Task FirstOrDefaultResultAsync_WithPredicate_NoMatch_ReturnsNotFoundError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var notFoundError = Error.NotFound("User not found");

        // Act
        var result = await _context.Customers
            .FirstOrDefaultResultAsync(
                c => c.Email == EmailAddress.Create("nobody@example.com"),
                notFoundError,
                ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.UnwrapError().Should().Be(notFoundError);
    }

    #endregion

    #region Where with Specification — filters correctly

    [Fact]
    public async Task Where_WithSpecification_FiltersCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var spec = new CreatedAfterSpec(new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var customers = await _context.Customers
            .Where(spec)
            .ToListAsync(ct);

        // Assert
        customers.Should().HaveCount(1);
        customers[0].Name.Value.Should().Be("Bob");
    }

    [Fact]
    public async Task Where_WithComposedSpecification_FiltersCorrectly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var afterSpec = new CreatedAfterSpec(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var beforeSpec = new CreatedBeforeSpec(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var combined = afterSpec.And(beforeSpec);

        // Act
        var customers = await _context.Customers
            .Where(combined)
            .ToListAsync(ct);

        // Assert — Alice (2024-01-01) is after 2023-01-01 and before 2024-06-01
        customers.Should().HaveCount(1);
        customers[0].Name.Value.Should().Be("Alice");
    }

    #endregion

    #region Test Specifications

    private class CreatedAfterSpec : Specification<TestCustomer>
    {
        private readonly DateTime _date;
        public CreatedAfterSpec(DateTime date) => _date = date;

        public override System.Linq.Expressions.Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.CreatedAt > _date;
    }

    private class CreatedBeforeSpec : Specification<TestCustomer>
    {
        private readonly DateTime _date;
        public CreatedBeforeSpec(DateTime date) => _date = date;

        public override System.Linq.Expressions.Expression<Func<TestCustomer, bool>> ToExpression()
            => c => c.CreatedAt < _date;
    }

    #endregion
}
