namespace Trellis.Primitives.Tests;

using Trellis.Testing;

// --- Test value objects exercising the reason-code override surface ---

/// <summary>
/// A length-constrained string whose length failures speak the application's vocabulary rather than
/// the framework's.
/// </summary>
[StringLength(8, MinimumLength = 3, Code = "account.reference.length")]
public partial class AccountReference : RequiredString<AccountReference> { }

/// <summary>
/// A string whose blank rejection is renamed, proving <c>[NotDefault].Code</c> reaches the
/// present-but-blank failure on <c>RequiredString</c>.
/// </summary>
[NotDefault(Code = "account.reference.missing")]
public partial class NamedReference : RequiredString<NamedReference> { }

/// <summary>
/// A code carrying characters that are not literal-safe. Nothing about the wire vocabulary invites
/// a quote, a backslash, or a newline in a reason code, but the generator inlines the override into
/// generated source, so an unescaped one would emit source that does not compile. Pinning the odd
/// value here keeps that escaping honest.
/// </summary>
[NotDefault(Code = "weird\\code\"with\r\nnewline\tand\ttabs")]
public partial class AwkwardlyNamedCode : RequiredString<AwkwardlyNamedCode> { }

/// <summary>
/// A ranged int whose two directional failures collapse onto one application code, which is the
/// documented consequence of a single <c>Code</c> on <c>[Range]</c>.
/// </summary>
[Range(1, 10, Code = "cart.quantity.out-of-range")]
public partial class CartQuantity : RequiredInt<CartQuantity> { }

/// <summary>
/// A sign-convenience attribute carrying an override, proving the convenience attributes share the
/// range slot rather than owning one of their own.
/// </summary>
[Positive(Code = "invoice.amount.not-positive")]
public partial class InvoiceAmount : RequiredDecimal<InvoiceAmount> { }

/// <summary>
/// A Guid whose default-value rejection is renamed.
/// </summary>
[NotDefault(Code = "tenant.id.missing")]
public partial class TenantId : RequiredGuid<TenantId> { }

/// <summary>
/// A value object using the four-argument <c>ValidateAdditional</c>, which can name its own failure
/// instead of falling back to <c>error.unspecified</c>.
/// </summary>
public partial class ReservationCode : RequiredString<ReservationCode>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
    {
        if (value.StartsWith("RES-", StringComparison.Ordinal))
            return;

        errorMessage = "Reservation Code must start with RES-.";
        errorCode = "reservation.code.malformed";
    }
}

/// <summary>
/// The four-argument overload leaving <c>errorCode</c> unset, which must still produce a violation
/// and must fall back to the framework's unspecified code.
/// </summary>
public partial class LegacyReservationCode : RequiredString<LegacyReservationCode>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
    {
        if (value.Length < 4)
            errorMessage = "Legacy Reservation Code is too short.";
    }
}

/// <summary>
/// The four-argument overload assigning a blank code. TRLS060 catches a blank <c>Code</c> on an
/// attribute because that is a literal an analyzer can read, but a code assigned here is only known
/// once the value is rejected, so the generated code has to fall back at runtime. Either way an
/// empty string must never reach the wire, where it would read as a reason rather than as the
/// absence of one.
/// </summary>
public partial class BlankCodedReservation : RequiredString<BlankCodedReservation>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
    {
        if (value.Length >= 4)
            return;

        errorMessage = "Blank Coded Reservation is too short.";
        errorCode = "   ";
    }
}

public class ReasonCodeOverrideTests
{
    private static FieldViolation FirstViolation(Error error) =>
        ((Error.InvalidInput)error).Fields[0];

    [Fact]
    public void StringLength_Code_override_replaces_both_length_codes()
    {
        FirstViolation(AccountReference.TryCreate("ab").UnwrapError()).ReasonCode
            .Should().Be("account.reference.length");
        FirstViolation(AccountReference.TryCreate("abcdefghi").UnwrapError()).ReasonCode
            .Should().Be("account.reference.length");
    }

    [Fact]
    public void StringLength_violation_carries_the_rejected_length_as_totalLength()
    {
        var violation = FirstViolation(AccountReference.TryCreate("ab").UnwrapError());

        violation.Args.Should().Contain("minLength", "3");
        violation.Args.Should().Contain("totalLength", "2");
    }

    [Fact]
    public void StringLength_maximum_violation_carries_totalLength()
    {
        var violation = FirstViolation(AccountReference.TryCreate("abcdefghi").UnwrapError());

        violation.Args.Should().Contain("maxLength", "8");
        violation.Args.Should().Contain("totalLength", "9");
    }

    [Fact]
    public void NotDefault_Code_override_renames_the_blank_string_rejection() =>
        FirstViolation(NamedReference.TryCreate("").UnwrapError()).ReasonCode
            .Should().Be("account.reference.missing");

    [Fact]
    public void A_Code_containing_characters_that_are_not_literal_safe_round_trips_intact() =>
        FirstViolation(AwkwardlyNamedCode.TryCreate("").UnwrapError()).ReasonCode
            .Should().Be("weird\\code\"with\r\nnewline\tand\ttabs");

    [Fact]
    public void NotDefault_default_on_a_string_stays_the_present_but_blank_code() =>
        FirstViolation(NotDefaultRequiredString.TryCreate("").UnwrapError()).ReasonCode
            .Should().Be(ValidationCodes.ValueNotEmpty);

    [Fact]
    public void Range_Code_override_collapses_both_directional_codes()
    {
        FirstViolation(CartQuantity.TryCreate(0).UnwrapError()).ReasonCode
            .Should().Be("cart.quantity.out-of-range");
        FirstViolation(CartQuantity.TryCreate(11).UnwrapError()).ReasonCode
            .Should().Be("cart.quantity.out-of-range");
    }

    [Fact]
    public void Sign_convenience_attribute_Code_override_reaches_the_range_failure() =>
        FirstViolation(InvoiceAmount.TryCreate(0m).UnwrapError()).ReasonCode
            .Should().Be("invoice.amount.not-positive");

    [Fact]
    public void NotDefault_Code_override_renames_the_empty_guid_rejection() =>
        FirstViolation(TenantId.TryCreate(Guid.Empty).UnwrapError()).ReasonCode
            .Should().Be("tenant.id.missing");

    [Fact]
    public void The_null_rejection_is_never_overridable() =>
        FirstViolation(NamedReference.TryCreate(null!).UnwrapError()).ReasonCode
            .Should().Be(ValidationCodes.ValueNotNull);

    [Fact]
    public void ValidateAdditional_can_set_the_reason_code()
    {
        var violation = FirstViolation(ReservationCode.TryCreate("XYZ-1").UnwrapError());

        violation.ReasonCode.Should().Be("reservation.code.malformed");
        violation.Detail.Should().Be("Reservation Code must start with RES-.");
    }

    [Fact]
    public void ValidateAdditional_leaving_the_code_unset_falls_back_to_unspecified() =>
        FirstViolation(LegacyReservationCode.TryCreate("abc").UnwrapError()).ReasonCode
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void ValidateAdditional_setting_a_blank_code_falls_back_to_unspecified() =>
        FirstViolation(BlankCodedReservation.TryCreate("abc").UnwrapError()).ReasonCode
            .Should().Be(ValidationCodes.Unspecified,
                "a blank code on the wire reads as a reason rather than as the absence of one");

    [Fact]
    public void ValidateAdditional_still_reports_error_unspecified_for_the_three_argument_overload() =>
        FirstViolation(Sku.TryCreate("BAD").UnwrapError()).ReasonCode
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void An_override_leaves_the_success_path_untouched()
    {
        AccountReference.TryCreate("abcd").Unwrap().Value.Should().Be("abcd");
        CartQuantity.TryCreate(5).Unwrap().Value.Should().Be(5);
        ReservationCode.TryCreate("RES-1").Unwrap().Value.Should().Be("RES-1");
    }
}
