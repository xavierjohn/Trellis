namespace Trellis.Mediator.Tests;

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="MediatorTraceProviderBuilderExtensions"/>.
/// </summary>
/// <remarks>
/// The behaviour under test is a visibility guarantee, so the assertions are written against a
/// real <see cref="TracerProvider"/> rather than a bare <see cref="ActivityListener"/>. A listener
/// proves an activity was created; only a provider proves a consumer's tracing configuration
/// would actually collect it, which is the thing that was missing.
/// </remarks>
[Collection(SerializedMediatorActivitySource.Name)]
public class MediatorTraceProviderBuilderExtensionsTests
{
    [Fact]
    public async Task AddTrellisMediatorInstrumentation_makes_handler_spans_visible()
    {
        var captured = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddTrellisMediatorInstrumentation()
            .AddProcessor(new CaptureProcessor(captured))
            .Build();

        await RunOneRequestAsync();

        captured.Should().ContainSingle()
            .Which.DisplayName.Should().Be(nameof(TestCommand));
    }

    /// <remarks>
    /// The control for the test above. Without it, that test would still pass if the source were
    /// registered by something else entirely, and the extension would be proven to do nothing.
    /// This also pins the failure mode the extension exists to remove: the span is not reported
    /// missing, it is simply never collected.
    /// </remarks>
    [Fact]
    public async Task Without_the_helper_handler_spans_are_silently_absent()
    {
        var captured = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddProcessor(new CaptureProcessor(captured))
            .Build();

        await RunOneRequestAsync();

        captured.Should().BeEmpty();
    }

    /// <remarks>
    /// Pins the helper to the constant the behaviour actually emits from. Were the extension to
    /// register a hand-typed copy of the name, the two could drift and the helper would go quietly
    /// inert — the same silent failure it was added to prevent, one level up.
    /// </remarks>
    [Fact]
    public async Task AddTrellisMediatorInstrumentation_registers_the_source_the_behavior_emits_from()
    {
        var captured = new List<Activity>();

        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddTrellisMediatorInstrumentation()
            .AddProcessor(new CaptureProcessor(captured))
            .Build();

        await RunOneRequestAsync();

        captured.Should().ContainSingle()
            .Which.Source.Name.Should().Be(TracingBehavior<TestCommand, Result<string>>.ActivitySourceName);
    }

    [Fact]
    public void AddTrellisMediatorInstrumentation_returns_the_same_builder_for_chaining()
    {
        var builder = Sdk.CreateTracerProviderBuilder();

        builder.AddTrellisMediatorInstrumentation().Should().BeSameAs(builder);
    }

    [Fact]
    public void AddTrellisMediatorInstrumentation_rejects_a_null_builder()
    {
        TracerProviderBuilder builder = null!;

        var act = () => builder.AddTrellisMediatorInstrumentation();

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task RunOneRequestAsync()
    {
        var behavior = new TracingBehavior<TestCommand, Result<string>>();
        var next = NextDelegate.ReturningAsync<TestCommand, Result<string>>(Result.Ok("Hello."));

        await behavior.Handle(new TestCommand("Alice"), next, CancellationToken.None);
    }

    private sealed class CaptureProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
