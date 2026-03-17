namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="DbContextExtensions.SaveChangesResultAsync"/> and
/// <see cref="DbContextExtensions.SaveChangesResultUnitAsync"/>.
/// </summary>
public class DbContextExtensionsTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly SqliteConnection _connection;

    public DbContextExtensionsTests() =>
        (_context, _connection) = TestDbContext.CreateInMemory();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #region SaveChangesResultAsync — success

    [Fact]
    public async Task SaveChangesResultAsync_Success_ReturnsSuccessWithCount()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customer = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("alice@example.com"),
            CreatedAt = DateTime.UtcNow
        };
        _context.Customers.Add(customer);

        // Act
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesResultAsync_MultipleEntities_ReturnsCorrectCount()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("bob@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Carol"),
            Email = EmailAddress.Create("carol@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    #endregion

    #region SaveChangesResultAsync — duplicate key

    [Fact]
    public async Task SaveChangesResultAsync_DuplicateKey_ReturnsConflictError()
    {
        // Arrange — email has a unique index
        var ct = TestContext.Current.CancellationToken;
        var customer1 = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("duplicate@example.com"),
            CreatedAt = DateTime.UtcNow
        };
        _context.Customers.Add(customer1);
        await _context.SaveChangesAsync(ct);

        var customer2 = new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("duplicate@example.com"), // Same email
            CreatedAt = DateTime.UtcNow
        };
        _context.Customers.Add(customer2);

        // Act
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task SaveChangesResultAsync_DuplicatePrimaryKey_ReturnsConflictError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var customerId = TestCustomerId.NewUniqueV4();

        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("Alice"),
            Email = EmailAddress.Create("pk-1@example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        _context.Customers.Add(new TestCustomer
        {
            Id = customerId,
            Name = TestCustomerName.Create("Bob"),
            Email = EmailAddress.Create("pk-2@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ConflictError>();
    }

    #endregion

    #region SaveChangesResultAsync — foreign key violation

    [Fact]
    public async Task SaveChangesResultAsync_ForeignKeyViolation_ReturnsDomainError()
    {
        // Arrange — order references a non-existent customer
        var ct = TestContext.Current.CancellationToken;
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = TestCustomerId.NewUniqueV4(), // Non-existent customer
            Amount = 100m,
            Status = TestOrderStatus.Draft
        };
        _context.Orders.Add(order);

        // Act
        var result = await _context.SaveChangesResultAsync(ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<DomainError>();
    }

    #endregion

    #region SaveChangesResultUnitAsync — success

    [Fact]
    public async Task SaveChangesResultUnitAsync_Success_ReturnsResultUnit()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("UnitTest"),
            Email = EmailAddress.Create("unit@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultUnitAsync(ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region SaveChangesResultAsync — acceptAllChangesOnSuccess

    [Fact]
    public async Task SaveChangesResultAsync_AcceptAllChangesOnSuccess_True_ReturnsSuccessWithCount()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("AcceptTrue"),
            Email = EmailAddress.Create("accept-true@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: true, ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesResultAsync_AcceptAllChangesOnSuccess_False_ReturnsSuccessAndPreservesTrackerState()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("AcceptFalse"),
            Email = EmailAddress.Create("accept-false@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: false, ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        _context.ChangeTracker.HasChanges().Should().BeTrue("acceptAllChangesOnSuccess: false should preserve tracker state");
    }

    [Fact]
    public async Task SaveChangesResultAsync_AcceptAllChangesOnSuccess_DuplicateKey_ReturnsConflictError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var email = EmailAddress.Create("dup-accept@example.com");
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("First"),
            Email = email,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Second"),
            Email = email,
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: false, ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task SaveChangesResultAsync_AcceptAllChangesOnSuccess_ForeignKeyViolation_ReturnsDomainError()
    {
        // Arrange — order references a non-existent customer
        var ct = TestContext.Current.CancellationToken;
        var order = new TestOrder
        {
            Id = TestOrderId.NewUniqueV4(),
            CustomerId = TestCustomerId.NewUniqueV4(),
            Amount = 50m,
            Status = TestOrderStatus.Draft
        };
        _context.Orders.Add(order);

        // Act
        var result = await _context.SaveChangesResultAsync(acceptAllChangesOnSuccess: false, ct);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<DomainError>();
    }

    #endregion

    #region SaveChangesResultUnitAsync — acceptAllChangesOnSuccess

    [Fact]
    public async Task SaveChangesResultUnitAsync_AcceptAllChangesOnSuccess_True_ReturnsSuccess()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("UnitAcceptTrue"),
            Email = EmailAddress.Create("unit-accept-true@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultUnitAsync(acceptAllChangesOnSuccess: true, ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SaveChangesResultUnitAsync_AcceptAllChangesOnSuccess_False_PreservesTrackerState()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("UnitAcceptFalse"),
            Email = EmailAddress.Create("unit-accept-false@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var result = await _context.SaveChangesResultUnitAsync(acceptAllChangesOnSuccess: false, ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _context.ChangeTracker.HasChanges().Should().BeTrue("acceptAllChangesOnSuccess: false should preserve tracker state");
    }

    #endregion

    #region OperationCanceledException — not caught

    [Fact]
    public async Task SaveChangesResultAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        _context.Customers.Add(new TestCustomer
        {
            Id = TestCustomerId.NewUniqueV4(),
            Name = TestCustomerName.Create("Cancel"),
            Email = EmailAddress.Create("cancel@example.com"),
            CreatedAt = DateTime.UtcNow
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _context.SaveChangesResultAsync(cts.Token));
    }

    #endregion
}