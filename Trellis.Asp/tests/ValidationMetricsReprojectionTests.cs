namespace Trellis.Asp.Tests;

using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Trellis.Asp.Validation;

/// <summary>
/// Regression tests proving the ASP re-projection paths do not inflate
/// <see cref="ValidationMetrics"/> counts.
/// </summary>
/// <remarks>
/// These paths are the reason the counter lives on <see cref="FieldViolation"/> rather than on the
/// <see cref="Error.InvalidInput"/> that carries it. Both rebuild the carrying failure from
/// violations that already exist, so a counter on the failure would report a single rule firing
/// two or three times — and would do so silently, since an inflated count still looks like a
/// plausible number on a dashboard.
/// </remarks>
public class ValidationMetricsReprojectionTests
{
    [Fact]
    public void Rebasing_a_failures_pointers_does_not_recount_its_violations()
    {
        var original = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(new InputPointer("/name", InputLocation.Body), ValidationCodes.ValueNotNull),
            new FieldViolation(new InputPointer("/size", InputLocation.Body), ValidationCodes.StringLength)));

        using var probe = new MeterProbe();

        var rebased = JsonValidationPathRebase.RebaseTo(original, "/order");

        rebased.Fields.Items.Select(v => v.Field.Path).Should().Equal("/order/name", "/order/size");
        probe.Total.Should().Be(0);
    }

    /// <summary>
    /// The shared status-lookup probe carries no violations, so resolving a scalar validation
    /// status records no measurement.
    /// </summary>
    /// <remarks>
    /// A validation *probe* is not a validation *failure*. <c>ScalarValidationStatus</c> holds a
    /// shared <see cref="Error.InvalidInput"/> purely so the error map can be keyed on its runtime
    /// type, and it once built that probe with <c>ForRule</c>. Under violation-site counting that
    /// records a bogus failure at type-initialization time.
    /// <para>
    /// This asserts the invariant directly rather than by watching the meter, because the defect is
    /// timing-dependent: the increment only lands if a listener is attached before the type
    /// initializer runs, so a meter-watching test would pass vacuously whenever an earlier test in
    /// the assembly touched the type first. The invariant — the probe carries no violations — holds
    /// regardless of when the type initializes.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_scalar_validation_status_probe_carries_no_violations()
    {
        _ = ScalarValidationStatus.Resolve(new DefaultHttpContext());

        var probe = typeof(ScalarValidationStatus)
            .GetField("s_invalidInputProbe", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null);

        probe.Should().BeOfType<Error.InvalidInput>()
            .Which.Should().Match<Error.InvalidInput>(i => i.Fields.IsEmpty && i.Rules.IsEmpty);
    }

    private sealed class MeterProbe : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private long _total;

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

        public long Total => _total;

        public void Dispose() => _listener.Dispose();

        private void OnMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            if (Environment.CurrentManagedThreadId == _ownerThreadId)
                _total += measurement;
        }
    }
}
