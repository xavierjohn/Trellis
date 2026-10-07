namespace Trellis.Core.Tests.Results;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class ResultEnsureNotNullTests
{
    public enum GuardForm { Error, Factory, Required }

    [Theory]
    [InlineData(GuardForm.Error, true)]
    [InlineData(GuardForm.Error, false)]
    [InlineData(GuardForm.Factory, true)]
    [InlineData(GuardForm.Factory, false)]
    [InlineData(GuardForm.Required, true)]
    [InlineData(GuardForm.Required, false)]
    public void EnsureNotNull_Reference_Presence_ReturnsValueOrError(GuardForm form, bool present)
    {
        var value = present ? new object() : null;
        var error = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, "field", detail: "Required.");
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var result = form switch
        {
            GuardForm.Error => Result.EnsureNotNull(value, error),
            GuardForm.Factory => Result.EnsureNotNull(value, () => { calls++; return error; }),
            GuardForm.Required => Result.EnsureNotNull(value, "field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        if (present)
            result.Should().BeSuccess().Which.Should().BeSameAs(value);
        else if (form == GuardForm.Required)
            result.Should().BeFailure().Which.Should().Be(error);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNull",
            present ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData(GuardForm.Error, true)]
    [InlineData(GuardForm.Error, false)]
    [InlineData(GuardForm.Factory, true)]
    [InlineData(GuardForm.Factory, false)]
    [InlineData(GuardForm.Required, true)]
    [InlineData(GuardForm.Required, false)]
    public void EnsureNotNull_Struct_Presence_UnwrapsValueOrError(GuardForm form, bool present)
    {
        int? value = present ? 0 : null;
        var error = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, "field", detail: "Required.");
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var result = form switch
        {
            GuardForm.Error => Result.EnsureNotNull(value, error),
            GuardForm.Factory => Result.EnsureNotNull(value, () => { calls++; return error; }),
            GuardForm.Required => Result.EnsureNotNull(value, "field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        if (present)
            result.Should().BeSuccess().Which.Should().Be(0);
        else if (form == GuardForm.Required)
            result.Should().BeFailure().Which.Should().Be(error);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNull",
            present ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void EnsureNotNull_String_NonNullBlank_Succeeds(string value) =>
        Result.EnsureNotNull(value, "field").Should().BeSuccess().Which.Should().BeSameAs(value);

    [Fact]
    public void EnsureNotNull_Struct_DefaultValues_Succeed()
    {
        Result.EnsureNotNull((bool?)false, "field").Should().BeSuccess().Which.Should().BeFalse();
        Result.EnsureNotNull((DateTime?)default(DateTime), "field").Should().BeSuccess().Which.Should().Be(default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureNotNull_Factory_NullFactory_ThrowsEvenOnSuccess(bool present)
    {
        var reference = () => Result.EnsureNotNull(present ? "value" : null, errorFactory: null!);
        var scalar = () => Result.EnsureNotNull(present ? (int?)0 : null, errorFactory: null!);

        reference.Should().Throw<ArgumentNullException>().WithParameterName("errorFactory");
        scalar.Should().Throw<ArgumentNullException>().WithParameterName("errorFactory");
    }

    [Fact]
    public void EnsureNotNull_Factory_PresentValue_DoesNotInvokeThrowingFactory()
    {
        Result.EnsureNotNull("value", () => throw new InvalidOperationException()).Should().BeSuccess();
        Result.EnsureNotNull((int?)0, () => throw new InvalidOperationException()).Should().BeSuccess();
    }

    [Fact]
    public void EnsureNotNull_Factory_Exception_PropagatesUnchanged()
    {
        var exception = new InvalidOperationException("factory failed");
        var reference = () => Result.EnsureNotNull<string>(null, () => throw exception);
        var scalar = () => Result.EnsureNotNull<int>(null, () => throw exception);

        reference.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        scalar.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
    }

    [Fact]
    public void EnsureNotNull_Factory_NullProducedError_Throws()
    {
        var reference = () => Result.EnsureNotNull<string>(null, () => null!);
        var scalar = () => Result.EnsureNotNull<int>(null, () => null!);

        reference.Should().Throw<ArgumentNullException>().WithParameterName("error");
        scalar.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    [Fact]
    public void EnsureNotNull_Error_NullLiterals_BindToEagerOverload()
    {
        Result.EnsureNotNull("value", null!).Should().BeSuccess();
        Result.EnsureNotNull((int?)0, null!).Should().BeSuccess();
        var reference = () => Result.EnsureNotNull<string>(null, null!);
        var scalar = () => Result.EnsureNotNull<int>(null, null!);

        reference.Should().Throw<ArgumentNullException>().WithParameterName("error");
        scalar.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    [Fact]
    public void EnsureNotNull_Required_PresentValue_DoesNotNormalizeField()
    {
        Result.EnsureNotNull("value", "/invalid~2").Should().BeSuccess();
        Result.EnsureNotNull((int?)0, "/invalid~2").Should().BeSuccess();
    }

    [Fact]
    public void EnsureNotNull_Required_MissingValue_RejectsMalformedPointer()
    {
        var reference = () => Result.EnsureNotNull<string>(null, "/invalid~2");
        var scalar = () => Result.EnsureNotNull<int>(null, "/invalid~2");

        reference.Should().Throw<ArgumentException>();
        scalar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureNotNull_Combine_PresentValues_MapTypedNonNullValues()
    {
        string? id = "restaurant";
        string? name = "dish";
        int? category = 0;

        var result = Result.EnsureNotNull(id, "id")
            .Combine(Result.EnsureNotNull(name, "name"))
            .Combine(Result.EnsureNotNull(category, "category"))
            .Map((id, name, category) => $"{id.Length}:{name.Length}:{category}");

        result.Should().BeSuccess().Which.Should().Be("10:4:0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureNotNull_Combine_MissingValues_AccumulatesEveryRequiredField(bool allMissing)
    {
        string? id = null;
        string? name = allMissing ? null : "dish";
        int? category = null;
        var mapCalls = 0;

        var result = Result.EnsureNotNull(id, "id", "Id is required.")
            .Combine(Result.EnsureNotNull(name, "name", "Name is required."))
            .Combine(Result.EnsureNotNull(category, "category", "Category is required."))
            .Map((id, name, category) => { mapCalls++; return $"{id}:{name}:{category}"; });

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(allMissing
            ? ["/id", "/name", "/category"] : ["/id", "/category"]);
        error.Fields.Items.Should().OnlyContain(v => v.ReasonCode == ValidationCodes.ValueNotNull);
        error.Fields.Items.Select(v => v.Detail).Should().Equal(allMissing
            ? ["Id is required.", "Name is required.", "Category is required."]
            : ["Id is required.", "Category is required."]);
        mapCalls.Should().Be(0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
    }
}
