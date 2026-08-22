namespace Trellis.Core.Tests.Errors;

using System.Diagnostics.Metrics;

/// <summary>
/// Tests for <see cref="ValidationMetrics"/> and the counting site in <see cref="Error.InvalidInput"/>.
/// </summary>
public class ValidationMetricsTests
{
    [Fact]
    public void Creating_violations_counts_one_per_violation()
    {
        using var probe = new MeterProbe();

        _ = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("a"), ValidationCodes.ValueNotNull),
            new FieldViolation(InputPointer.ForProperty("b"), ValidationCodes.StringLength)));

        probe.Total.Should().Be(2);
        probe.CountFor(ValidationCodes.ValueNotNull).Should().Be(1);
        probe.CountFor(ValidationCodes.StringLength).Should().Be(1);
    }

    [Fact]
    public void Rule_violations_are_counted_and_tagged_as_rules()
    {
        using var probe = new MeterProbe();

        _ = new Error.InvalidInput(
            default,
            EquatableArray.Create(new RuleViolation(ValidationCodes.FieldsExactlyOne)));

        probe.Total.Should().Be(1);
        probe.ViolationKindFor(ValidationCodes.FieldsExactlyOne).Should().Be("rule");
    }

    /// <remarks>
    /// The guarantee that makes violation-site counting correct, and the defect this design was
    /// moved to avoid. The ASP layer rebuilds an <see cref="Error.InvalidInput"/> during pointer
    /// rebasing and again when aggregating collected violations, so counting on the carrying
    /// failure counted one rule firing two or three times. Counting on the violation is immune:
    /// re-packing existing violations into a new failure constructs no new violation.
    /// </remarks>
    [Fact]
    public void Repacking_existing_violations_into_a_new_failure_does_not_recount()
    {
        var violations = EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("a"), ValidationCodes.ValueNotNull));
        var original = new Error.InvalidInput(violations);

        using var probe = new MeterProbe();

        _ = new Error.InvalidInput(original.Fields, original.Rules);
        _ = original with { Detail = "re-projected" };

        probe.Total.Should().Be(0);
    }

    /// <remarks>
    /// The rebase path rewrites a violation's pointer with a <c>with</c>-expression. That must not
    /// recount, and does not, because the synthesized copy constructor copies backing fields
    /// instead of re-running the initializer.
    /// </remarks>
    [Fact]
    public void Rebasing_a_violations_pointer_with_a_with_expression_does_not_recount()
    {
        var violation = new FieldViolation(InputPointer.ForProperty("a"), ValidationCodes.ValueNotNull);

        using var probe = new MeterProbe();

        _ = violation with { Field = InputPointer.ForProperty("b") };

        probe.Total.Should().Be(0);
    }

    /// <remarks>
    /// An application code reaches the wire verbatim, so tagging it verbatim would let a caller
    /// mint unbounded time series. The total must stay exact while the breakdown is bucketed.
    /// </remarks>
    [Fact]
    public void An_application_code_is_bucketed_rather_than_tagged_verbatim()
    {
        using var probe = new MeterProbe();

        _ = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("a"), "order.9f3c-too-large")));

        probe.Total.Should().Be(1);
        probe.CountFor("order.9f3c-too-large").Should().Be(0);
        probe.CountFor(ValidationMetrics.OtherCode).Should().Be(1);
    }

    /// <remarks>
    /// The bucket is derived from the constants themselves, so a code added to the vocabulary is
    /// counted under its own name without anyone updating a second list. A hand-maintained copy
    /// would silently start bucketing new codes as <c>other</c>.
    /// </remarks>
    [Fact]
    public void Every_framework_code_is_tagged_under_its_own_name()
    {
        ValidationMetrics.Bucket(ValidationCodes.MoneyCurrencyMismatch)
            .Should().Be(ValidationCodes.MoneyCurrencyMismatch);
        ValidationMetrics.Bucket(ValidationCodes.EnumNameUndefined)
            .Should().Be(ValidationCodes.EnumNameUndefined);
        ValidationMetrics.Bucket(null).Should().Be(ValidationMetrics.OtherCode);
    }

    [Fact]
    public void AddTrellisValidationInstrumentation_rejects_a_null_builder()
    {
        OpenTelemetry.Metrics.MeterProviderBuilder builder = null!;

        var act = () => builder.AddTrellisValidationInstrumentation();

        act.Should().Throw<ArgumentNullException>();
    }

    /// <remarks>
    /// A <see cref="MeterListener"/> is process-wide, so it observes every concurrently running
    /// test that creates a validation failure — and in this assembly that is hundreds of them.
    /// Serializing the whole assembly to isolate six tests is not a trade worth making, so the
    /// probe isolates itself instead: a counter's callbacks fire synchronously on the thread that
    /// called <c>Add</c>, and no other test can be running on this test's thread while this test
    /// is between construction and assertion. Filtering on that thread makes the counts exactly
    /// the ones this test caused.
    /// </remarks>
    private sealed class MeterProbe : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<(string Code, string Violation, long Value)> _measurements = [];
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

        public MeterProbe()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == ValidationMetrics.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                },
            };

            _listener.SetMeasurementEventCallback<long>(OnMeasurement);
            _listener.Start();
        }

        public long Total => _measurements.Sum(m => m.Value);

        public long CountFor(string code) =>
            _measurements.Where(m => m.Code == code).Sum(m => m.Value);

        public string? ViolationKindFor(string code) =>
            _measurements.FirstOrDefault(m => m.Code == code).Violation;

        public void Dispose() => _listener.Dispose();

        private void OnMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId) return;

            var code = string.Empty;
            var violation = string.Empty;

            foreach (var tag in tags)
            {
                if (tag.Key == "validation.code") code = tag.Value as string ?? string.Empty;
                if (tag.Key == "validation.violation") violation = tag.Value as string ?? string.Empty;
            }

            _measurements.Add((code, violation, measurement));
        }
    }
}
