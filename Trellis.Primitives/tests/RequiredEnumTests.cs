namespace Trellis.Primitives.Tests;

using System.Text.Json;
using Trellis;
using Trellis.Testing;
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
/// Test enum value object with a custom external name for one member.
/// </summary>
public partial class TestOverriddenOrderState : RequiredEnum<TestOverriddenOrderState>
{
    public static readonly TestOverriddenOrderState Draft = new();

    [EnumValue("payment-pending")]
    public static readonly TestOverriddenOrderState AwaitingPayment = new();

    public static readonly TestOverriddenOrderState Shipped = new();
}

/// <summary>
/// Test enum value object with duplicate symbolic codes.
/// </summary>
public partial class TestDuplicateEnumValue : RequiredEnum<TestDuplicateEnumValue>
{
    [EnumValue("duplicate")]
    public static readonly TestDuplicateEnumValue First = new();

    [EnumValue("duplicate")]
    public static readonly TestDuplicateEnumValue Second = new();
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

/// <summary>
/// Composite value object holding <see cref="RequiredEnum{TSelf}"/> members — the scenario that
/// requires <c>RequiredEnum</c> to implement <see cref="IComparable"/> so members can be yielded
/// as equality components.
/// </summary>
public sealed class TestEnumHolder : ValueObject
{
    public TestEnumHolder(TestOrderState state, TestPaymentMethod method)
    {
        State = state;
        Method = method;
    }

    public TestOrderState State { get; }

    public TestPaymentMethod Method { get; }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return State;
        yield return Method;
    }
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
        result.Unwrap().Should().Be(TestOrderState.Draft);
    }

    [Fact]
    public void TryCreate_ValidName_CaseInsensitive_ReturnsSuccess()
    {
        // Act
        var result = TestOrderState.TryCreate("CONFIRMED");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(TestOrderState.Confirmed);
    }

    [Fact]
    public void TryCreate_InvalidName_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate("InvalidState");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InvalidInput>();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Contain("'InvalidState' is not a valid TestOrderState");
    }

    [Fact]
    public void TryCreate_NullValue_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate(null, null);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void TryCreate_EmptyString_ReturnsFailure()
    {
        // Act
        var result = TestOrderState.TryCreate("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void TryCreate_WithFieldName_UsesFieldNameInError()
    {
        // Act
        var result = TestOrderState.TryCreate("Invalid", "orderStatus");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/orderStatus");
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
    public void JsonDeserialize_Null_ThrowsJsonException()
    {
        // Round-4 contract change: deserializing JSON null into a Required enum value object
        // is a contract violation that must surface at the JSON boundary. The converter now
        // overrides HandleNull and throws JsonException for null tokens.
        var act = () => JsonSerializer.Deserialize<TestOrderState>("null");
        act.Should().Throw<JsonException>();
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

    [Fact]
    public void JsonSerialize_OverriddenEnumValue_WritesCustomExternalName()
    {
        // Act
        var json = JsonSerializer.Serialize(TestOverriddenOrderState.AwaitingPayment);

        // Assert
        json.Should().Be("\"payment-pending\"");
    }

    [Fact]
    public void JsonDeserialize_OverriddenEnumValue_ReadsCustomExternalName()
    {
        // Act
        var state = JsonSerializer.Deserialize<TestOverriddenOrderState>("\"payment-pending\"");

        // Assert
        state.Should().Be(TestOverriddenOrderState.AwaitingPayment);
    }

    #endregion

    #region IScalarValue Interface Tests

    [Fact]
    public void ImplementsIScalarValue() =>
        ((IScalarValue<TestOrderState, string>)TestOrderState.Draft).Value.Should().Be("Draft");

    #endregion

    #region Create Tests

    [Fact]
    public void Create_ValidName_ReturnsInstance()
    {
        // Act
        var state = TestOrderState.Create("Draft");

        // Assert
        state.Should().Be(TestOrderState.Draft);
    }

    [Fact]
    public void Create_InvalidName_ThrowsInvalidOperationException()
    {
        // Act
        Action act = () => TestOrderState.Create("NonExistent");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create TestOrderState:*");
    }

    [Fact]
    public void Create_OverriddenEnumValue_ReturnsInstance()
    {
        // Act
        var state = TestOverriddenOrderState.Create("payment-pending");

        // Assert
        state.Should().Be(TestOverriddenOrderState.AwaitingPayment);
    }

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
        result.Unwrap().Fee.Should().Be(0.005m);
    }

    [Fact]
    public void TryCreate_OverriddenEnumValue_ReturnsMatchingMember()
    {
        // Act
        var result = TestOverriddenOrderState.TryCreate("payment-pending");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(TestOverriddenOrderState.AwaitingPayment);
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
    public void Value_UsesFieldNameByDefault_AndOnlyOverridesWhenSpecified()
    {
        // Assert
        TestOverriddenOrderState.Draft.Value.Should().Be("Draft");
        TestOverriddenOrderState.AwaitingPayment.Value.Should().Be("payment-pending");
        TestOverriddenOrderState.Shipped.Value.Should().Be("Shipped");
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

    [Fact]
    public void GetAll_DuplicateSymbolicValues_ThrowsInvalidOperationException()
    {
        // Act
        var act = () => TestDuplicateEnumValue.GetAll();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate symbolic value 'duplicate'*");
    }

    #endregion

    #region IComparable Tests

    [Fact]
    public void RequiredEnum_IsAssignableTo_IComparable()
    {
        TestOrderState.Draft.Should().BeAssignableTo<IComparable>();
        TestOrderState.Draft.Should().BeAssignableTo<IComparable<RequiredEnum<TestOrderState>>>();
    }

    [Fact]
    public void CompareTo_SameMember_ReturnsZero() =>
        ((IComparable)TestOrderState.Draft).CompareTo(TestOrderState.Draft).Should().Be(0);

    [Fact]
    public void CompareTo_OrdersByDeclarationOrder()
    {
        // Draft is declared before Confirmed, so it sorts first (by Ordinal, not alphabetically).
        ((IComparable)TestOrderState.Draft).CompareTo(TestOrderState.Confirmed).Should().BeNegative();
        ((IComparable)TestOrderState.Confirmed).CompareTo(TestOrderState.Draft).Should().BePositive();
    }

    [Fact]
    public void CompareTo_IsConsistentWithEquality()
    {
        var left = TestOrderState.Shipped;
        var right = TestOrderState.Shipped;

        (((IComparable)left).CompareTo(right) == 0).Should().Be(left.Equals(right));
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive() =>
        ((IComparable)TestOrderState.Draft).CompareTo(null).Should().BePositive();

    [Fact]
    public void CompareTo_DifferentType_ThrowsArgumentException()
    {
        var act = () => ((IComparable)TestOrderState.Draft).CompareTo("not an enum");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OrderBy_SortsMembersByDeclarationOrderNotAlphabetically()
    {
        // Declaration order: Draft(0), Confirmed(1), Shipped(2), Delivered(3), Cancelled(4).
        // Alphabetical would be Cancelled, Draft, Shipped — so this also proves ordering is NOT by Value.
        var sorted = new[] { TestOrderState.Shipped, TestOrderState.Cancelled, TestOrderState.Draft }
            .OrderBy(state => (IComparable)state)
            .ToList();

        sorted.Should().Equal(TestOrderState.Draft, TestOrderState.Shipped, TestOrderState.Cancelled);
    }

    [Fact]
    public void GenericCompareTo_OrdersByDeclarationOrder()
    {
        TestOrderState.Draft.CompareTo(TestOrderState.Confirmed).Should().BeNegative();
        TestOrderState.Confirmed.CompareTo(TestOrderState.Draft).Should().BePositive();
        TestOrderState.Draft.CompareTo(TestOrderState.Draft).Should().Be(0);
    }

    [Fact]
    public void ComparisonOperators_FollowDeclarationOrder()
    {
        var draft = TestOrderState.Draft;
        var draftAgain = TestOrderState.Draft;
        var confirmed = TestOrderState.Confirmed;

        (draft < confirmed).Should().BeTrue();
        (confirmed > draft).Should().BeTrue();
        (draft <= draftAgain).Should().BeTrue();
        (draft >= draftAgain).Should().BeTrue();
        (confirmed < draft).Should().BeFalse();
    }

    [Fact]
    public void CompositeValueObject_WithRequiredEnumComponents_SupportsEqualityAndOrdering()
    {
        var first = new TestEnumHolder(TestOrderState.Draft, TestPaymentMethod.Cash);
        var same = new TestEnumHolder(TestOrderState.Draft, TestPaymentMethod.Cash);
        var different = new TestEnumHolder(TestOrderState.Shipped, TestPaymentMethod.Cash);

        first.Should().Be(same);
        first.Equals(different).Should().BeFalse();
        first.CompareTo(different).Should().NotBe(0);
    }

    #endregion
}