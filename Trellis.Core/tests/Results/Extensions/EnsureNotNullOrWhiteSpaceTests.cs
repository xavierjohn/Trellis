namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class EnsureNotNullOrWhiteSpaceTests
{
    public enum GuardForm { Error, Factory, Field }

    [Theory]
    [InlineData(GuardForm.Error, "Ada")]
    [InlineData(GuardForm.Error, " Ada ")]
    [InlineData(GuardForm.Factory, "Ada")]
    [InlineData(GuardForm.Factory, " Ada ")]
    [InlineData(GuardForm.Field, "Ada")]
    [InlineData(GuardForm.Field, " Ada ")]
    public void EnsureNotNullOrWhiteSpace_AllForms_ValidValue_ReturnsSameString(GuardForm form, string value)
    {
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var (result, violations) = MeasureValidation(() => form switch
        {
            GuardForm.Error => value.EnsureNotNullOrWhiteSpace(new Error.Unexpected("unused")),
            GuardForm.Factory => value.EnsureNotNullOrWhiteSpace(() =>
            {
                calls++;
                return Error.InvalidInput.ForField(ValidationCodes.ValueNotEmpty, "name");
            }),
            GuardForm.Field => value.EnsureNotNullOrWhiteSpace("name", "Name is required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        });

        result.Should().BeSuccess().Which.Should().BeSameAs(value);
        calls.Should().Be(0);
        violations.Should().Be(0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2003")]
    public void EnsureNotNullOrWhiteSpace_Factory_BlankValue_InvokesOnceAndPreservesError(string? value)
    {
        var error = new Error.Forbidden("name.denied");
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var result = value.EnsureNotNullOrWhiteSpace(() => { calls++; return error; });

        result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(1);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2003")]
    public void EnsureNotNullOrWhiteSpace_Field_BlankValue_CreatesOneNotEmptyViolation(string? value)
    {
        using var tracing = new ActivityTestHelper();

        var (result, violations) = MeasureValidation(() =>
            value.EnsureNotNullOrWhiteSpace("name", "Name is required."));

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Code.Should().Be(ValidationCodes.Unspecified);
        error.Detail.Should().BeNull();
        error.Rules.Items.Should().BeEmpty();
        var field = error.Fields.Items.Should().ContainSingle().Which;
        field.Field.Path.Should().Be("/name");
        field.Field.In.Should().Be(InputLocation.Unspecified);
        field.ReasonCode.Should().Be(ValidationCodes.ValueNotEmpty);
        field.Args.Should().BeNull();
        field.Detail.Should().Be("Name is required.");
        violations.Should().Be(1);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNullOrWhiteSpace", ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("display/name", "/display~1name")]
    [InlineData("/customers/0/name", "/customers/0/name")]
    public void EnsureNotNullOrWhiteSpace_Field_FieldName_PreservesPointerAndOptionalDetail(
        string? fieldName, string expectedPointer)
    {
        var result = ((string?)null).EnsureNotNullOrWhiteSpace(fieldName: fieldName);

        var field = result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items
            .Should().ContainSingle().Which;
        field.Field.Path.Should().Be(expectedPointer);
        field.Detail.Should().BeNull();
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_Field_ValidValue_DoesNotValidatePointer() =>
        "value".EnsureNotNullOrWhiteSpace("/invalid~2").Should().BeSuccess();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EnsureNotNullOrWhiteSpace_Field_BlankValue_RejectsMalformedPointer(string? value)
    {
        var act = () => value.EnsureNotNullOrWhiteSpace("/invalid~2");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("value")]
    public void EnsureNotNullOrWhiteSpace_Factory_NullFactory_ThrowsEvenOnSuccess(string? value)
    {
        var act = () => value.EnsureNotNullOrWhiteSpace(errorFactory: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("errorFactory");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_Factory_ValidValue_DoesNotInvokeThrowingFactory() =>
        "value".EnsureNotNullOrWhiteSpace(static () => throw new InvalidOperationException())
            .Should().BeSuccess();

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void EnsureNotNullOrWhiteSpace_Factory_Exception_PropagatesUnchanged(string? value)
    {
        var exception = new InvalidOperationException("factory failed");
        var act = () => value.EnsureNotNullOrWhiteSpace(() => throw exception);

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void EnsureNotNullOrWhiteSpace_Factory_NullProducedError_Throws(string? value)
    {
        var act = () => value.EnsureNotNullOrWhiteSpace(static () => null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_Error_NullLiteral_KeepsExistingBindingAndBehavior()
    {
        "value".EnsureNotNullOrWhiteSpace(null!).Should().BeSuccess();
        var act = () => ((string?)null).EnsureNotNullOrWhiteSpace(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_Combine_BlankValues_AccumulatesWithoutMapping()
    {
        var mapCalls = 0;

        var (result, violations) = MeasureValidation(() =>
            ((string?)null).EnsureNotNullOrWhiteSpace("name", "Name is required.")
                .Combine(" ".EnsureNotNullOrWhiteSpace("code", "Code is required."))
                .Map((name, code) => { mapCalls++; return $"{name}:{code}"; }));

        var fields = result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items;
        fields.Select(field => field.Field.Path).Should().Equal(["/name", "/code"]);
        fields.Select(field => field.ReasonCode).Should().Equal([
            ValidationCodes.ValueNotEmpty, ValidationCodes.ValueNotEmpty]);
        fields.Select(field => field.Detail).Should().Equal(["Name is required.", "Code is required."]);
        mapCalls.Should().Be(0);
        violations.Should().Be(2);
    }

    private static (Result<string> Result, long Violations) MeasureValidation(Func<Result<string>> action)
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        long violations = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ValidationMetrics.MeterName &&
                    instrument.Name == ValidationMetrics.FailuresInstrumentName)
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            // Counter callbacks are synchronous; isolate measurements from parallel tests.
            if (Environment.CurrentManagedThreadId == ownerThreadId)
                violations += measurement;
        });
        listener.Start();

        return (action(), violations);
    }
}
