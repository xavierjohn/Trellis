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

    [Fact]
    public async Task PublishAsync_HandlerOnlyMatchesExactRuntimeType()
    {
        // Dispatch matches the runtime type exactly — a handler registered against the base
        // IIntegrationEvent interface must not be invoked for a derived event type.
        var services = new ServiceCollection();
        AddNullLogging(services);
        var baseHandler = new RecordingBaseHandler();
        services.AddSingleton<IIntegrationEventHandler<IIntegrationEvent>>(baseHandler);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        await publisher.PublishAsync(
            new TestIntegrationEvent("payload", DateTimeOffset.UtcNow), CancellationToken.None);

        baseHandler.Received.Should().BeEmpty(
            "dispatch is by exact runtime type only — base/interface-type handlers are not invoked");
    }

    [Fact]
    public async Task PublishAsync_HandlerResolutionThrows_LogsAndDoesNotThrow()
    {
        // A handler with an unresolvable constructor dependency makes resolving the
        // IEnumerable<IIntegrationEventHandler<T>> throw; the publisher logs and returns.
        var captureLogger = new CaptureLogger();
        var services = new ServiceCollection();
        services.AddScoped<IIntegrationEventHandler<TestIntegrationEvent>, UnresolvableHandler>();

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(provider, captureLogger);

        var act = async () => await publisher.PublishAsync(
            new TestIntegrationEvent("payload", DateTimeOffset.UtcNow), CancellationToken.None);

        await act.Should().NotThrowAsync();
        captureLogger.Records.Should().Contain(r =>
            r.Level == LogLevel.Error
            && r.Message.Contains("resolve handlers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_LogsDebug()
    {
        var captureLogger = new CaptureLogger();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(provider, captureLogger);

        await publisher.PublishAsync(
            new TestIntegrationEvent("payload", DateTimeOffset.UtcNow), CancellationToken.None);

        captureLogger.Records.Should().Contain(r =>
            r.Level == LogLevel.Debug
            && r.Message.Contains("No IIntegrationEventHandler", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_HandlerImplementingMultipleEventInterfaces_IsInvokedForEach()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var handler = new MultiEventHandler();
        services.AddSingleton<IIntegrationEventHandler<TestIntegrationEvent>>(handler);
        services.AddSingleton<IIntegrationEventHandler<OtherIntegrationEvent>>(handler);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorIntegrationEventPublisher(
            provider, NullLogger<MediatorIntegrationEventPublisher>.Instance);

        var first = new TestIntegrationEvent("a", DateTimeOffset.UtcNow);
        var second = new OtherIntegrationEvent(7, DateTimeOffset.UtcNow);
        await publisher.PublishAsync(first, CancellationToken.None);
        await publisher.PublishAsync(second, CancellationToken.None);

        handler.Received.Should().Equal(new IIntegrationEvent[] { first, second });
    }

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private sealed record TestIntegrationEvent(string Payload, DateTimeOffset OccurredAt) : IIntegrationEvent;

    private sealed record OtherIntegrationEvent(int Value, DateTimeOffset OccurredAt) : IIntegrationEvent;

    private interface IUnregisteredDependency;

    private sealed class UnresolvableHandler(IUnregisteredDependency dependency) : IIntegrationEventHandler<TestIntegrationEvent>
    {
        private readonly IUnregisteredDependency _dependency = dependency;

        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            _ = _dependency;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiEventHandler : IIntegrationEventHandler<TestIntegrationEvent>, IIntegrationEventHandler<OtherIntegrationEvent>
    {
        public List<IIntegrationEvent> Received { get; } = [];

        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(OtherIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler : IIntegrationEventHandler<TestIntegrationEvent>
    {
        public List<TestIntegrationEvent> Received { get; } = [];

        public ValueTask HandleAsync(TestIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBaseHandler : IIntegrationEventHandler<IIntegrationEvent>
    {
        public List<IIntegrationEvent> Received { get; } = [];

        public ValueTask HandleAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
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
