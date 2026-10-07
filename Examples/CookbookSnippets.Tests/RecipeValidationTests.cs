namespace CookbookSnippets.Tests;

using System.Diagnostics.Metrics;
using Trellis.Authorization;
using Trellis.Testing;

public class RecipeValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(17)]
    public void TryCreate_ValidInputs_DoesNotCreateValidationViolations(int recipe)
    {
        Func<IResult> create = recipe switch
        {
            1 => () => Recipe01.Order.TryCreate(Recipe01.OrderId.NewUniqueV7(),
                new Recipe01.Money(10m, Recipe01.CurrencyCode.Create("USD")), ActorId.Create("owner")),
            13 => () => Recipe13.Customer.TryCreate(Recipe13.CustomerId.NewUniqueV7(), "Ada",
                Recipe13.ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap()),
            16 => () => Recipe16.Order.TryCreate(new Recipe16.Money(10m)),
            17 => () => Recipe17.Order.TryCreate(Recipe17.OrderId.NewUniqueV7(), new Recipe17.Money(10m)),
            _ => throw new ArgumentOutOfRangeException(nameof(recipe)),
        };

        var (result, violations) = MeasureValidation(create);

        result.IsSuccess.Should().BeTrue();
        violations.Should().Be(0);
    }

    [Fact]
    public void TryCreate_CrudMissingFields_AccumulatesRequiredErrors()
    {
        var (result, violations) = MeasureValidation(() => Recipe01.Order.TryCreate(null, null, null));

        var error = result.Error.Should().BeOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(["/id", "/total", "/ownerId"]);
        error.Fields.Items.Should().OnlyContain(v => v.ReasonCode == ValidationCodes.ValueNotNull);
        error.Fields.Items.Select(v => v.Detail).Should().Equal(
            ["Order id is required.", "Total is required.", "Owner id is required."]);
        violations.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void TryCreate_CustomerMissingFields_CountsOnlyMissingInputs(int missing)
    {
        var id = (missing & 1) == 0 ? Recipe13.CustomerId.NewUniqueV7() : null;
        var name = (missing & 2) == 0 ? "Ada" : null;
        var shipping = (missing & 4) == 0
            ? Recipe13.ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap()
            : null;
        var expectedPaths = new List<string>();
        if ((missing & 1) != 0) expectedPaths.Add("/id");
        if ((missing & 2) != 0) expectedPaths.Add("/name");
        if ((missing & 4) != 0) expectedPaths.Add("/shipping");

        var (result, violations) = MeasureValidation(() => Recipe13.Customer.TryCreate(id, name, shipping));

        result.IsSuccess.Should().Be(missing == 0);
        violations.Should().Be(expectedPaths.Count);
        if (result.IsSuccess)
            return;

        var error = result.Error.Should().BeOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(expectedPaths);
        error.Fields.Items.Should().OnlyContain(v => v.ReasonCode == ValidationCodes.ValueNotNull);
    }

    [Theory]
    [InlineData(null, false, ValidationCodes.ValueNotNull)]
    [InlineData("", false, ValidationCodes.ValueNotEmpty)]
    [InlineData(" \t", false, ValidationCodes.ValueNotEmpty)]
    [InlineData(" Ada ", true, null)]
    public void TryCreate_CustomerName_NullBlankOrPresent_UsesMatchingValidationCode(
        string? name, bool succeeds, string? expectedCode)
    {
        var id = Recipe13.CustomerId.NewUniqueV7();
        var shipping = Recipe13.ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap();

        var (result, violations) = MeasureValidation(() => Recipe13.Customer.TryCreate(id, name, shipping));

        result.IsSuccess.Should().Be(succeeds);
        violations.Should().Be(succeeds ? 0 : 1);
        if (succeeds)
        {
            Recipe13.Customer.TryCreate(id, name, shipping).Unwrap().Name.Should().Be(name);
            return;
        }

        var field = result.Error.Should().BeOfType<Error.InvalidInput>().Which.Fields.Items.Should().ContainSingle().Which;
        field.Field.Path.Should().Be("/name");
        field.ReasonCode.Should().Be(expectedCode);
        field.Detail.Should().Be("Name is required.");
    }

    [Fact]
    public void Submit_DomainRule_CountsOnlyRejectedTransitions()
    {
        var order = Recipe17.Order.TryCreate(Recipe17.OrderId.NewUniqueV7(), new Recipe17.Money(10m)).Unwrap();

        var (submitted, successViolations) = MeasureValidation(() => order.Submit(TimeProvider.System));
        var (rejected, failureViolations) = MeasureValidation(() => order.Submit(TimeProvider.System));

        submitted.IsSuccess.Should().BeTrue();
        successViolations.Should().Be(0);
        var rule = rejected.Error.Should().BeOfType<Error.InvalidInput>().Which.Rules.Items.Should().ContainSingle().Which;
        rule.ReasonCode.Should().Be("order.already-submitted");
        rule.Detail.Should().Be("Already submitted");
        failureViolations.Should().Be(1);
        order.UncommittedEvents().Should().ContainSingle();
    }

    [Fact]
    public void CanReturn_DomainRules_CountOnlyTheFailedGuard()
    {
        var order = Recipe22.Order.ForTesting(Recipe22.OrderId.NewUniqueV7(),
            [new(Recipe22.ProductId.NewUniqueV7(), 1)]);

        var (accepted, successViolations) = MeasureValidation(() => order.CanReturn("damaged"));
        var (rejected, failureViolations) = MeasureValidation(() => order.CanReturn(""));

        accepted.IsSuccess.Should().BeTrue();
        successViolations.Should().Be(0);
        rejected.Error.Should().BeOfType<Error.InvalidInput>().Which.Fields.Items.Should().ContainSingle();
        failureViolations.Should().Be(1);
    }

    [Fact]
    public void Approve_DomainRule_CountsOnlyRejectedTransitions()
    {
        var order = new Recipe23.Order(Recipe23.OrderId.NewUniqueV7());

        var (approved, successViolations) = MeasureValidation(() => order.Approve());
        var (rejected, failureViolations) = MeasureValidation(() => order.Approve());

        approved.IsSuccess.Should().BeTrue();
        successViolations.Should().Be(0);
        rejected.Error.Should().BeOfType<Error.InvalidInput>().Which.Rules.Items.Should().ContainSingle();
        failureViolations.Should().Be(1);
    }

    [Fact]
    public void Replace_DomainRule_CountsOnlyInvalidInput()
    {
        var order = new Recipe23.Order(Recipe23.OrderId.NewUniqueV7());

        var (replaced, successViolations) = MeasureValidation(() => order.Replace(new("reference")));
        var (rejected, failureViolations) = MeasureValidation(() => order.Replace(new("")));

        replaced.IsSuccess.Should().BeTrue();
        successViolations.Should().Be(0);
        rejected.Error.Should().BeOfType<Error.InvalidInput>().Which.Fields.Items.Should().ContainSingle();
        failureViolations.Should().Be(1);
        order.CustomerReference.Should().Be("reference");
    }

    private static (IResult Result, long Violations) MeasureValidation(Func<IResult> action)
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
            // Counter callbacks are synchronous; exclude measurements from parallel tests.
            if (Environment.CurrentManagedThreadId == ownerThreadId)
                violations += measurement;
        });
        listener.Start();

        return (action(), violations);
    }
}
