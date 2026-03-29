namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class BindZipTests : TestBase
{
    #region BindZip 2-tuple

    [Fact]
    public void BindZip_Success_FuncSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(s => Result.Success(s.Length));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(("hello", 5));
    }

    [Fact]
    public void BindZip_Success_FuncFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(_ => Result.Failure<int>(Error1));

        // Assert
        result.Should().BeFailure();
        result.Error.Should().Be(Error1);
    }

    [Fact]
    public void BindZip_Failure_FuncNotInvoked_ReturnsOriginalFailure()
    {
        // Arrange
        var funcInvoked = false;

        // Act
        var result = Result.Failure<string>(Error1)
            .BindZip(v => { funcInvoked = true; return Result.Success(42); });

        // Assert
        funcInvoked.Should().BeFalse();
        result.Should().BeFailure();
        result.Error.Should().Be(Error1);
    }

    #endregion

    #region BindZip 3-tuple (chained)

    [Fact]
    public void BindZip_Chain_AllSuccess_Returns3Tuple()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(s => Result.Success(s.Length))
            .BindZip((s, len) => Result.Success(s.ToUpperInvariant()));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(("hello", 5, "HELLO"));
    }

    [Fact]
    public void BindZip_Chain_SecondFails_ReturnsSecondFailure()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(_ => Result.Failure<int>(Error1))
            .BindZip((s, len) => Result.Success(s.ToUpperInvariant()));

        // Assert
        result.Should().BeFailure();
        result.Error.Should().Be(Error1);
    }

    [Fact]
    public void BindZip_Chain_ThirdFails_ReturnsThirdFailure()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(s => Result.Success(s.Length))
            .BindZip((s, len) => Result.Failure<string>(Error2));

        // Assert
        result.Should().BeFailure();
        result.Error.Should().Be(Error2);
    }

    [Fact]
    public void BindZip_Chain_FirstFails_ShortCircuits()
    {
        // Arrange
        var secondInvoked = false;
        var thirdInvoked = false;

        // Act
        var result = Result.Failure<string>(Error1)
            .BindZip(s => { secondInvoked = true; return Result.Success(s.Length); })
            .BindZip((s, len) => { thirdInvoked = true; return Result.Success(s.ToUpperInvariant()); });

        // Assert
        secondInvoked.Should().BeFalse();
        thirdInvoked.Should().BeFalse();
        result.Should().BeFailure();
        result.Error.Should().Be(Error1);
    }

    #endregion

    #region BindZip 4-tuple

    [Fact]
    public void BindZip_Chain_AllSuccess_Returns4Tuple()
    {
        // Arrange & Act
        var result = Result.Success("hello")
            .BindZip(s => Result.Success(s.Length))
            .BindZip((s, len) => Result.Success(s.ToUpperInvariant()))
            .BindZip((s, len, upper) => Result.Success(len * 2));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(("hello", 5, "HELLO", 10));
    }

    #endregion

    #region BindZip async

    [Fact]
    public async Task BindZipAsync_TaskLeft_Success_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Result.Success("hello").AsTask()
            .BindZipAsync(s => Result.Success(s.Length));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(("hello", 5));
    }

    [Fact]
    public async Task BindZipAsync_TaskBoth_Success_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Result.Success("hello").AsTask()
            .BindZipAsync(s => Task.FromResult(Result.Success(s.Length)));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(("hello", 5));
    }

    #endregion

    #region Null arguments

    [Fact]
    public void BindZip_NullFunc_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success("hello");

        // Act
        var act = () => result.BindZip<string, int>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    #endregion

    #region Real-world scenario

    [Fact]
    public void BindZip_RealWorld_SequentialLookup()
    {
        // Simulate: GetOrder -> GetCustomer -> GetInventory
        var orderId = 42;
        var customerId = 7;
        var productId = 99;

        Result<(string Order, string Customer, string Inventory)> result =
            GetOrder(orderId)
                .BindZip(order => GetCustomer(customerId))
                .BindZip((order, customer) => GetInventory(productId));

        result.Should().BeSuccess()
            .Which.Should().Be(($"Order-{orderId}", $"Customer-{customerId}", $"Inventory-{productId}"));
    }

    private static Result<string> GetOrder(int id) => Result.Success($"Order-{id}");
    private static Result<string> GetCustomer(int id) => Result.Success($"Customer-{id}");
    private static Result<string> GetInventory(int id) => Result.Success($"Inventory-{id}");

    #endregion
}