namespace Trellis.Primitives.Tests;

using System.Text.Json;
using Trellis;
using Xunit;

/// <summary>
/// Test enum value object for order states.
/// </summary>
public partial class TestOrderState : RequiredEnum<TestOrderState>
{
    public static readonly TestOrderState Draft = new();
    public static readonly TestOrderState Confirmed = new();
    public static readonly TestOrderState Shipped = new();
    public static readonly TestOrderState Delivered = new();
    public static readonly TestOrderState Cancelled = new();
}

/// <summary>
/// Test enum value object with behavior.
/// </summary>
public partial class TestPaymentMethod : RequiredEnum<TestPaymentMethod>
{
    public static readonly TestPaymentMethod CreditCard = new(fee: 0.029m);
    public static readonly TestPaymentMethod BankTransfer = new(fee: 0.005m);
    public static readonly TestPaymentMethod Cash = new(fee: 0m);

    public decimal Fee { get; }

    private TestPaymentMethod(decimal fee) => Fee = fee;

    public decimal CalculateFee(decimal amount) => amount * Fee;
}

/// <summary>
/// Test enum used to verify GetAll does not expose mutable shared cache state.
/// </summary>
public partial class TestImmutableEnumMembers : RequiredEnum<TestImmutableEnumMembers>
{
    public static readonly TestImmutableEnumMembers One = new();
    public static readonly TestImmutableEnumMembers Two = new();
    public static readonly TestImmutableEnumMembers Three = new();
}

public class RequiredEnumTests
{
    #region TryCreate Tests

    [Fact]
    public void TryCreate_ValidName_ReturnsSuccess()
    {
        // Act
        var result = TestOrderState.TryCreate("Draft");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TestOrderState.Draft);
    }

    [Fact]
    public void TryCreate_ValidName_CaseInsensitive_ReturnsSuccess()
    {
        // Act
        var result = TestOrderState.TryCreate("CONFIRMED");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TestOrderState.Confirmed);
    }

    [Fact]
    public void TryCreate_InvalidName_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate("InvalidState");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Contain("'InvalidState' is not a valid TestOrderState");
    }

    [Fact]
    public void TryCreate_NullValue_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate(null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_EmptyString_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void TryCreate_WithFieldName_UsesFieldNameInError()
    {
        // Act
        var result = TestOrderState.TryCreate("Invalid", "orderStatus");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("orderStatus");
    }

    #endregion

    #region Parse/TryParse Tests (IParsable)

    [Fact]
    public void Parse_ValidName_ReturnsInstance()
    {
        // Act
        var state = TestOrderState.Parse("Shipped", null);

        // Assert
        state.Should().Be(TestOrderState.Shipped);
    }

    [Fact]
    public void Parse_InvalidName_ThrowsFormatException()
    {
        // Act & Assert
        var act = () => TestOrderState.Parse("Invalid", null);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidName_ReturnsTrue()
    {
        // Act
        var success = TestOrderState.TryParse("Delivered", null, out var result);

        // Assert
        success.Should().BeTrue();
        result.Should().Be(TestOrderState.Delivered);
    }

    [Fact]
    public void TryParse_InvalidName_ReturnsFalse()
    {
        // Act
        var success = TestOrderState.TryParse("Invalid", null, out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void JsonSerialize_WritesName()
    {
        // Act
        var json = JsonSerializer.Serialize(TestOrderState.Confirmed);

        // Assert
        json.Should().Be("\"Confirmed\"");
    }

    [Fact]
    public void JsonDeserialize_ReadsName()
    {
        // Act
        var state = JsonSerializer.Deserialize<TestOrderState>("\"Shipped\"");

        // Assert
        state.Should().Be(TestOrderState.Shipped);
    }

    [Fact]
    public void JsonDeserialize_InvalidName_ThrowsJsonException()
    {
        // Act & Assert
        var act = () => JsonSerializer.Deserialize<TestOrderState>("\"Invalid\"");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void JsonDeserialize_Null_ReturnsNull()
    {
        // Act
        var state = JsonSerializer.Deserialize<TestOrderState>("null");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void JsonDeserialize_UnexpectedToken_ThrowsJsonException()
    {
        // Act & Assert
        var act = () => JsonSerializer.Deserialize<TestOrderState>("123");
        act.Should().Throw<JsonException>()
            .WithMessage("*Unexpected token type*");
    }

    [Fact]
    public void JsonSerialize_NullValue_WritesNull()
    {
        // Arrange
        TestOrderState? state = null;

        // Act
        var json = JsonSerializer.Serialize(state);

        // Assert
        json.Should().Be("null");
    }

    [Fact]
    public void JsonSerialize_InObject_WritesCorrectly()
    {
        // Arrange
        var order = new { State = TestOrderState.Draft, Id = 123 };

        // Act
        var json = JsonSerializer.Serialize(order);

        // Assert
        json.Should().Contain("\"State\":\"Draft\"");
    }

    #endregion

    #region IScalarValue Interface Tests

    [Fact]
    public void ImplementsIScalarValue() =>
        ((IScalarValue<TestOrderState, string>)TestOrderState.Draft).Value.Should().Be("Draft");

    #endregion

    #region Enum with Behavior Tests

    [Fact]
    public void EnumWithBehavior_CanAccessProperties()
    {
        // Assert
        TestPaymentMethod.CreditCard.Fee.Should().Be(0.029m);
        TestPaymentMethod.BankTransfer.Fee.Should().Be(0.005m);
        TestPaymentMethod.Cash.Fee.Should().Be(0m);
    }

    [Fact]
    public void EnumWithBehavior_CanCallMethods()
    {
        // Act
        var fee = TestPaymentMethod.CreditCard.CalculateFee(100m);

        // Assert
        fee.Should().Be(2.9m);
    }

    [Fact]
    public void EnumWithBehavior_TryCreate_Works()
    {
        // Act
        var result = TestPaymentMethod.TryCreate("BankTransfer");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Fee.Should().Be(0.005m);
    }

    #endregion

    #region GetAll and Standard RequiredEnum Tests

    [Fact]
    public void GetAll_ReturnsAllMembers()
    {
        // Act
        var all = TestOrderState.GetAll();

        // Assert
        all.Should().HaveCount(5);
        all.Should().Contain(TestOrderState.Draft);
        all.Should().Contain(TestOrderState.Confirmed);
        all.Should().Contain(TestOrderState.Shipped);
        all.Should().Contain(TestOrderState.Delivered);
        all.Should().Contain(TestOrderState.Cancelled);
    }

    [Fact]
    public void GetAll_ReturnedCollection_CannotMutateCachedMembers()
    {
        // Arrange
        var all = TestImmutableEnumMembers.GetAll();

        // Act
        var act = () => ((ICollection<TestImmutableEnumMembers>)all).Clear();

        // Assert
        act.Should().Throw<NotSupportedException>();
        TestImmutableEnumMembers.GetAll().Should().Equal(
            [TestImmutableEnumMembers.One, TestImmutableEnumMembers.Two, TestImmutableEnumMembers.Three]);
    }

    [Fact]
    public void Value_IsStringName()
    {
        // Assert
        TestOrderState.Draft.Value.Should().Be("Draft");
        TestOrderState.Confirmed.Value.Should().Be("Confirmed");
    }

    [Fact]
    public void Ordinal_IsAssignedInOrder()
    {
        // Assert (ordinals are assigned based on declaration order)
        TestOrderState.Draft.Ordinal.Should().Be(0);
        TestOrderState.Confirmed.Ordinal.Should().Be(1);
        TestOrderState.Shipped.Ordinal.Should().Be(2);
        TestOrderState.Delivered.Ordinal.Should().Be(3);
        TestOrderState.Cancelled.Ordinal.Should().Be(4);
    }

    [Fact]
    public void Equality_SameInstance_ReturnsTrue()
    {
        // Arrange
        var state1 = TestOrderState.Draft;
        var state2 = TestOrderState.Draft;

        // Assert
        (state1 == state2).Should().BeTrue();
        state1.Equals(state2).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentInstances_ReturnsFalse()
    {
        // Assert
        (TestOrderState.Draft == TestOrderState.Confirmed).Should().BeFalse();
        TestOrderState.Draft.Equals(TestOrderState.Confirmed).Should().BeFalse();
    }

    #endregion
}