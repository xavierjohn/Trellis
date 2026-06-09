namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="MediatorIntegrationEventPublisher"/> - the default in-process integration-event
/// consumer. Mirrors the domain-event publisher's best-effort fan-out contract.
/// </summary>
public class MediatorIntegrationEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_DispatchesToAllHandlersForExactRuntimeType()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(first);
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(second);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        var evt = new TestIntegrationEvent("payload", DateTimeOffset.UtcNow);
        await publisher.PublishAsync(evt, CancellationToken.None);

        first.Received.Should().ContainSingle().Which.Should().BeSameAs(evt);
        second.Received.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }

    [Fact]
    public async Task PublishAsync_NoHandlersRegistered_IsNoOp()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(
            new TestIntegrationEvent("payload", DateTimeOffset.UtcNow), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_FirstHandlerThrows_LogsAndContinuesWithRemaining()
    {
        var captureLogger = new CaptureLogger();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>, ThrowingHandler>();
        var second = new RecordingHandler();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(second);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(provider, captureLogger);

        var evt = new TestIntegrationEvent("payload", DateTimeOffset.UtcNow);
        await publisher.PublishAsync(evt, CancellationToken.None);

        second.Received.Should().ContainSingle("a thrown handler must not block the others");
        captureLogger.Records.Should().Contain(r =>
            r.Level == LogLevel.Error
            && r.Message.Contains("threw for event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_NullEvent_Throws()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_SynchronousHandlerOCE_PropagatesAsCancellation()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>, SynchronousCancellingHandler>();

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await publisher.PublishAsync(
            new TestIntegrationEvent("payload", DateTimeOffset.UtcNow), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private sealed record TestIntegrationEvent(string Payload, DateTimeOffset OccurredAt) : IIntegrationEvent;

    private sealed class RecordingHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public List<TestIntegrationEvent> Received { get; } = [];

        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
            => throw new InvalidOperationException("integration handler boom");
    }

    private sealed class SynchronousCancellingHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILogger<MediatorIntegrationEventPublisher>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }
}
