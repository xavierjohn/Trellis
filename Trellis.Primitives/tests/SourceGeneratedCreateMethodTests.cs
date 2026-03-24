namespace Trellis.Primitives.Tests;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for source-generated Create() methods that throw on validation failure.
/// These methods are generated for all RequiredGuid, RequiredString,
/// RequiredInt, and RequiredDecimal value objects.
/// 
/// Note: Test value objects (EmployeeId, TicketNumber, UnitPrice, TrackingId)
/// are declared in their respective test files (RequiredGuidTests.cs, etc.).
/// </summary>
public class SourceGeneratedCreateMethodTests
{
    #region RequiredGuid Create Methods

    [Fact]
    public void RequiredGuid_Create_WithValidGuid_ReturnsInstance()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var employeeId = EmployeeId.Create(validGuid);

        // Assert
        employeeId.Should().NotBeNull();
        employeeId.Value.Should().Be(validGuid);
    }

    [Fact]
    public void RequiredGuid_Create_WithEmptyGuid_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act
        var act = () => EmployeeId.Create(emptyGuid);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create EmployeeId:*Employee Id cannot be empty*");
    }

    [Fact]
    public void RequiredGuid_Create_WithValidStringGuid_ReturnsInstance()
    {
        // Arrange
        var guidString = "2F45ACF9-6E51-4DC7-8732-DBE7F260E951";

        // Act
        var employeeId = EmployeeId.Create(guidString);

        // Assert
        employeeId.Should().NotBeNull();
        employeeId.Value.Should().Be(Guid.Parse(guidString));
    }

    [Fact]
    public void RequiredGuid_Create_WithInvalidStringGuid_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidGuidString = "not-a-guid";

        // Act
        var act = () => EmployeeId.Create(invalidGuidString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create EmployeeId:*Guid should contain 32 digits with 4 dashes*");
    }

    [Fact]
    public void RequiredGuid_Create_WithEmptyString_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyString = "";

        // Act
        var act = () => EmployeeId.Create(emptyString);

        // Assert - Empty string fails at parsing stage, not empty validation
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create EmployeeId:*Guid should contain 32 digits with 4 dashes*");
    }

    #endregion

    #region RequiredInt Create Methods

    [Fact]
    public void RequiredInt_Create_WithValidInt_ReturnsInstance()
    {
        // Arrange
        var validInt = 42;

        // Act
        var ticketNumber = TicketNumber.Create(validInt);

        // Assert
        ticketNumber.Should().NotBeNull();
        ticketNumber.Value.Should().Be(validInt);
    }

    [Fact]
    public void RequiredInt_Create_WithValidStringInt_ReturnsInstance()
    {
        // Arrange
        var intString = "123";

        // Act
        var ticketNumber = TicketNumber.Create(intString);

        // Assert
        ticketNumber.Should().NotBeNull();
        ticketNumber.Value.Should().Be(123);
    }

    [Fact]
    public void RequiredInt_Create_WithInvalidStringInt_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidIntString = "not-a-number";

        // Act
        var act = () => TicketNumber.Create(invalidIntString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TicketNumber:*Value must be a valid integer*");
    }

    #endregion

    #region RequiredDecimal Create Methods

    [Fact]
    public void RequiredDecimal_Create_WithValidDecimal_ReturnsInstance()
    {
        // Arrange
        var validDecimal = 99.99m;

        // Act
        var unitPrice = UnitPrice.Create(validDecimal);

        // Assert
        unitPrice.Should().NotBeNull();
        unitPrice.Value.Should().Be(validDecimal);
    }

    [Fact]
    public void RequiredDecimal_Create_WithValidStringDecimal_ReturnsInstance()
    {
        // Arrange
        var decimalString = "19.99";

        // Act
        var unitPrice = UnitPrice.Create(decimalString);

        // Assert
        unitPrice.Should().NotBeNull();
        unitPrice.Value.Should().Be(19.99m);
    }

    [Fact]
    public void RequiredDecimal_Create_WithInvalidStringDecimal_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidDecimalString = "not-a-decimal";

        // Act
        var act = () => UnitPrice.Create(invalidDecimalString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create UnitPrice:*Value must be a valid decimal*");
    }

    #endregion

    #region RequiredString Create Methods

    [Fact]
    public void RequiredString_Create_WithValidString_ReturnsInstance()
    {
        // Arrange
        var validString = "ABC123";

        // Act
        var trackingId = TrackingId.Create(validString);

        // Assert
        trackingId.Should().NotBeNull();
        trackingId.Value.Should().Be(validString);
    }

    [Fact]
    public void RequiredString_Create_WithNullString_ThrowsInvalidOperationException()
    {
        // Arrange
        string? nullString = null;

        // Act
        var act = () => TrackingId.Create(nullString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TrackingId:*Tracking Id cannot be empty*");
    }

    [Fact]
    public void RequiredString_Create_WithEmptyString_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyString = "";

        // Act
        var act = () => TrackingId.Create(emptyString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TrackingId:*Tracking Id cannot be empty*");
    }

    [Fact]
    public void RequiredString_Create_WithWhitespaceString_ThrowsInvalidOperationException()
    {
        // Arrange
        var whitespaceString = "   ";

        // Act
        var act = () => TrackingId.Create(whitespaceString);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TrackingId:*Tracking Id cannot be empty*");
    }

    [Fact]
    public void RequiredString_ExplicitCast_WithEmptyString_ThrowsCreateStyleInvalidOperationException()
    {
        // Arrange
        TrackingId trackingId;

        // Act
        Action act = () => trackingId = (TrackingId)string.Empty;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TrackingId:*Tracking Id cannot be empty*");
    }

    #endregion

    #region Integration Tests - TRLS007 Compatibility

    [Fact]
    public void Create_CanBeUsedInsteadOfTryCreateValue_ForAllTypes()
    {
        // This test verifies that Create() methods work as drop-in replacements
        // for TryCreate().Value pattern (what TRLS007 analyzer suggests)

        // Arrange & Act & Assert - All should succeed
        var employeeId = EmployeeId.Create(Guid.NewGuid());
        employeeId.Should().NotBeNull();

        var ticketNumber = TicketNumber.Create(42);
        ticketNumber.Should().NotBeNull();

        var unitPrice = UnitPrice.Create(99.99m);
        unitPrice.Should().NotBeNull();

        var trackingId = TrackingId.Create("ABC123");
        trackingId.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithStringParsing_WorksForAllTypes()
    {
        // Verifies that Create(string) overloads work correctly

        // Arrange & Act & Assert
        var employeeId = EmployeeId.Create("2F45ACF9-6E51-4DC7-8732-DBE7F260E951");
        employeeId.Value.Should().Be(Guid.Parse("2F45ACF9-6E51-4DC7-8732-DBE7F260E951"));

        var ticketNumber = TicketNumber.Create("123");
        ticketNumber.Value.Should().Be(123);

        var unitPrice = UnitPrice.Create("19.99");
        unitPrice.Value.Should().Be(19.99m);
    }

    #endregion
}